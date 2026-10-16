using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace DiskRescue {
    public sealed partial class ApiEngine {
        string MapIdentity(DiskFile f){return history.DirectoryPath+"\n"+CloudIntegrity.Version(f);}
        bool PrepareMapped(Job job){
            DiskFile f=job.File;string identity=MapIdentity(f),partial=f.Destination+".ydr-part",metadata=Path.Combine(history.DirectoryPath,"partials",HistoryStore.IdentityKey(f.Path)+".txt");
            SafeDestination(metadata);string legacyIdentity=SegmentIdentity(f);bool same=(File.Exists(metadata)&&File.ReadAllText(metadata)==legacyIdentity)||(!File.Exists(metadata)&&f.ResumeEligible);
            if(!same&&File.Exists(metadata)&&!String.IsNullOrEmpty(f.Sha256)&&String.IsNullOrEmpty(f.Md5)){string previous=f.Modified.Split(new[]{"|sha256:"},StringSplitOptions.None)[0];same=File.ReadAllText(metadata)==f.Path+"\n"+previous+"\n"+f.Length;}
            if(!same){preserver.PreservePartial(f,partial,partial+".ydr-state");preserver.PreservePartial(f,SegmentFolder(f),null);}
            SegmentPlan old=same?ReadSegmentPlan(f):null;
            if(RangeMap.Exists(f.Destination)&&!RangeMap.Matches(f.Destination,identity,f.Length,!String.IsNullOrEmpty(f.Md5)))preserver.PreservePartial(f,f.Destination+".ydr-download",f.Destination+".ydr-map");
            int count=old!=null?old.Parts:CloudIntegrity.HasHash(f)&&f.Length>=Math.Max(64,Math.Min(4096,settings.MultiStreamThresholdMB))*1000000L?Math.Max(1,Math.Min(8,settings.StreamsPerFile)):1;
            long[] boundaries=null;if(old!=null){boundaries=new long[old.Parts+1];for(int i=1;i<=old.Parts;i++)boundaries[i]=SegmentStart(old,i);}
            try{job.Map=RangeMap.OpenPlanned(f.Destination,identity,f.Length,count,boundaries,stop.Token,!String.IsNullOrEmpty(f.Md5));}catch(RangeMap.SparseUnavailable){if(!File.Exists(f.Destination+".ydr-map")&&File.Exists(f.Destination+".ydr-download")&&new FileInfo(f.Destination+".ydr-download").Length==0)File.Delete(f.Destination+".ydr-download");return false;}
            if(same&&File.Exists(partial)){
                long bytes=DurablePartial.Recover(partial,legacyIdentity,CloudIntegrity.HasHash(f),stop.Token);if(old!=null)bytes=Math.Min(bytes,old.Offset);bytes=Math.Min(bytes,f.Length);
                if(bytes>0)job.Map.Import(partial,0,0,bytes,stop.Token);
            }
            if(old!=null)for(int i=0;i<old.Parts;i++){
                string piece=SegmentPath(SegmentFolder(f),i);if(!File.Exists(piece))continue;long bytes=DurablePartial.Recover(piece,legacyIdentity+"\nsegment:"+i,CloudIntegrity.HasHash(f),stop.Token);
                bytes=Math.Min(bytes,SegmentStart(old,i+1)-SegmentStart(old,i));if(bytes>0)job.Map.Import(piece,0,SegmentStart(old,i),bytes,stop.Token);
            }
            // Sources remain intact until the whole migration is revalidated.
            if(same&&(File.Exists(partial)||old!=null)){
                job.Map=RangeMap.Open(f.Destination,identity,f.Length,count,stop.Token);
                if(File.Exists(partial)){File.Delete(partial);DurablePartial.Forget(partial);}ClearSegments(f);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(metadata));DurablePartial.AtomicText(metadata,legacyIdentity);job.Bytes=job.Map.Bytes;return true;
        }
        async Task<Result> DownloadMapped(Job job){
            RangeMap map=job.Map;List<int> pending=Enumerable.Range(0,map.Parts).Where(i=>map.Offset(i)<map.End(i)).ToList();
            if(pending.Count==0)return FinishPayload(job,job.File.Destination+".ydr-download");
            TaskCompletionSource<bool> supported=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<Result> first=ReadMapped(job,pending[0],supported);List<Result> results=new List<Result>();
            if(await supported.Task.ConfigureAwait(false)){
                int next=0;List<Task<List<Result>>> workers=new List<Task<List<Result>>>();
                for(int i=1;i<Math.Min(Math.Max(1,settings.StreamsPerFile),pending.Count);i++)workers.Add(Task.Run(async delegate{var output=new List<Result>();int n;while((n=Interlocked.Increment(ref next))<pending.Count)output.Add(await ReadMapped(job,pending[n],null).ConfigureAwait(false));return output;}));
                results.Add(await first.ConfigureAwait(false));if(workers.Count==0)for(int i=1;i<pending.Count;i++)results.Add(await ReadMapped(job,pending[i],null).ConfigureAwait(false));
                foreach(List<Result> output in await Task.WhenAll(workers).ConfigureAwait(false))results.AddRange(output);
            }else results.Add(await first.ConfigureAwait(false));
            job.Bytes=map.Bytes;
            if(stop.IsCancellationRequested)return new Result{Status="cancelled_user",Message="Stopped; mapped ranges retained.",Bytes=job.Bytes};
            if(results.Any(r=>r.Status=="range_unavailable")){
                // Every writer has finished before the plan is replaced.
                job.Map=RangeMap.Single(job.File.Destination,MapIdentity(job.File),job.File.Length,stop.Token);job.Bytes=0;
                Result full=await ReadMapped(job,0,null).ConfigureAwait(false);job.Bytes=job.Map.Bytes;if(full.Status!="success"){full.Bytes=job.Bytes;return full;}
            }else{Result failure=results.FirstOrDefault(r=>r.Status!="success");if(failure!=null){failure.Bytes=job.Bytes;return failure;}}
            if(job.Map.Bytes!=job.File.Length)return new Result{Status="failed",Message="Mapped download has unconfirmed ranges.",Bytes=job.Map.Bytes};
            return FinishPayload(job,job.File.Destination+".ydr-download");
        }
        async Task<Result> ReadMapped(Job job,int index,TaskCompletionSource<bool> supported){
            for(int attempt=0;;attempt++){
                long before=job.Map.Offset(index);Result result=await ReadMappedAttempt(job,index,attempt==0?supported:null,settings.IdleSeconds+(attempt>0?2:0)).ConfigureAwait(false);
                if(stop.IsCancellationRequested||!result.RetryTransfer||attempt>=2||job.Map.Offset(index)>=job.Map.End(index)||job.Map.Offset(index)<=before)return result;
            }
        }
        async Task<Result> ReadMappedAttempt(Job job,int index,TaskCompletionSource<bool> supported,int timeout){
            bool acquired=false,reset=false;long received=0;RangeMap map=job.Map;long offset=map.Offset(index),end=map.End(index);
            using(CancellationTokenSource idle=CancellationTokenSource.CreateLinkedTokenSource(stop.Token)){
                try{
                    stop.Token.ThrowIfCancellationRequested();if(offset==end){if(supported!=null)supported.TrySetResult(true);return new Result{Status="success"};}
                    await downloadSlots.WaitAsync(stop.Token).ConfigureAwait(false);acquired=true;idle.CancelAfter(TimeSpan.FromSeconds(timeout));
                    using(HttpRequestMessage request=new HttpRequestMessage(HttpMethod.Get,job.Link)){
                        if(map.Parts>1||offset>0)request.Headers.Range=new RangeHeaderValue(offset,map.Parts==1?(long?)null:end-1);
                        using(HttpResponseMessage response=await downloadClient.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,idle.Token).ConfigureAwait(false))using(CancellationTokenRegistration abort=idle.Token.Register(delegate{response.Dispose();})){
                            if(response.StatusCode==HttpStatusCode.OK&&map.Parts>1||response.StatusCode==HttpStatusCode.RequestedRangeNotSatisfiable&&map.Parts>1)return new Result{Status="range_unavailable",Message="Requested ranges unavailable."};
                            if(response.StatusCode==HttpStatusCode.RequestedRangeNotSatisfiable){reset=true;InvalidateLink(job);return new Result{Status="failed",Message="Resume range rejected."};}
                            if(!response.IsSuccessStatusCode)throw new ApiException((int)response.StatusCode);
                            if(response.StatusCode==HttpStatusCode.PartialContent){ContentRangeHeaderValue range=response.Content.Headers.ContentRange;if(range==null||range.Unit!="bytes"||range.From!=offset||range.To!=end-1||range.Length!=job.File.Length){InvalidateLink(job);throw new IOException("Invalid mapped byte range.");}}
                            else if(response.StatusCode==HttpStatusCode.OK&&offset>0){map.Reset(index);offset=map.Start(index);Interlocked.Exchange(ref job.Bytes,map.Bytes);}
                            else if(response.StatusCode!=HttpStatusCode.OK){InvalidateLink(job);throw new IOException("Unsupported mapped response.");}
                            if(response.Content.Headers.ContentEncoding.Count!=0){InvalidateLink(job);throw new IOException("Encoded response cannot be resumed.");}
                            if(supported!=null)supported.TrySetResult(true);
                            using(Stream input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false)){
                                idle.CancelAfter(Timeout.Infinite);
                                using(Stream output=map.Writer(index)){
                                    byte[] buffer=new byte[65536];long left=end-offset;
                                    while(left>0){idle.CancelAfter(TimeSpan.FromSeconds(timeout));int n=await ReadDownloadData(input,buffer,(int)Math.Min(left,buffer.Length),idle.Token).ConfigureAwait(false);idle.CancelAfter(Timeout.Infinite);if(n==0)throw new DownloadReadException(new EndOfStreamException());await output.WriteAsync(buffer,0,n,stop.Token).ConfigureAwait(false);left-=n;received+=n;Interlocked.Add(ref job.Bytes,n);ConfirmLink(job);}
                                    idle.CancelAfter(TimeSpan.FromSeconds(timeout));int extra=await ReadDownloadData(input,buffer,1,idle.Token).ConfigureAwait(false);idle.CancelAfter(Timeout.Infinite);if(extra!=0){reset=true;InvalidateLink(job);throw new IOException("Response exceeded mapped range.");}
                                }
                            }
                        }
                    }
                    return new Result{Status="success"};
                }catch(OperationCanceledException){if(!stop.IsCancellationRequested&&received==0)InvalidateLink(job);return new Result{Status=stop.IsCancellationRequested?"cancelled_user":"timeout",Message=stop.IsCancellationRequested?"Stopped by user.":"Mapped download idle timeout.",RetryTransfer=!stop.IsCancellationRequested&&received>0};}
                catch(ApiException ex){InvalidateLink(job);return new Result{Status=ex.Code>=500?"cloud_error":"failed",Message="Download HTTP "+ex.Code};}
                catch(Exception ex){if(received==0&&(ex is DownloadReadException||ex is HttpRequestException))InvalidateLink(job);return new Result{Status=stop.IsCancellationRequested?"cancelled_user":idle.IsCancellationRequested?"timeout":"failed",Message="Mapped download failed: "+ex.GetType().Name,RetryTransfer=!stop.IsCancellationRequested&&received>0&&(ex is DownloadReadException||ex is HttpRequestException)};}
                finally{if(reset)map.Reset(index);if(acquired)downloadSlots.Release();if(supported!=null)supported.TrySetResult(false);}
            }
        }
    }
}
