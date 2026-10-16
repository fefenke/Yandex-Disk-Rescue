using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace DiskRescue {
    public sealed partial class ApiEngine {
        DownloadSlotQueue downloadSlots;
        // SemaphoreSlim does not promise FIFO ordering. Retries must join the tail.
        sealed class DownloadSlotQueue {
            readonly object sync=new object();readonly Queue<TaskCompletionSource<bool>> waiting=new Queue<TaskCompletionSource<bool>>();int available;
            public DownloadSlotQueue(int capacity){available=capacity;}
            public Task WaitAsync(CancellationToken cancellation){
                TaskCompletionSource<bool> waiter=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                lock(sync){if(cancellation.IsCancellationRequested){waiter.SetCanceled();return waiter.Task;}if(available>0){available--;return Task.FromResult(true);}waiting.Enqueue(waiter);}
                return WaitQueued(waiter,cancellation);
            }
            static async Task WaitQueued(TaskCompletionSource<bool> waiter,CancellationToken cancellation){using(cancellation.Register(delegate{waiter.TrySetCanceled();}))await waiter.Task.ConfigureAwait(false);}
            public void Release(){lock(sync){while(waiting.Count>0)if(waiting.Dequeue().TrySetResult(true))return;available++;}}
        }
        sealed class DownloadReadException:IOException {public DownloadReadException(Exception inner):base("Download stream was interrupted.",inner){}}
        static async Task<int> ReadDownloadData(Stream input,byte[] buffer,int count,CancellationToken cancellation){
            try{return await input.ReadAsync(buffer,0,count,cancellation).ConfigureAwait(false);}
            catch(ObjectDisposedException){cancellation.ThrowIfCancellationRequested();throw;}
            catch(IOException ex){throw new DownloadReadException(ex);}
            catch(HttpRequestException ex){throw new DownloadReadException(ex);}
        }
        sealed class SegmentPlan { public string Identity;public long Offset,Length;public int Parts; }
        static string SegmentIdentity(DiskFile f){return f.Path+"\n"+f.Modified+"\n"+f.Length;}
        static string SegmentFolder(DiskFile f){return f.Destination+".ydr-segments";}
        static long SegmentStart(SegmentPlan p,int i){long length=p.Length-p.Offset;return p.Offset+(length/p.Parts)*i+Math.Min(i,length%p.Parts);}
        static string SegmentPath(string folder,int index){return Path.Combine(folder,index+".part");}
        static SegmentPlan ReadSegmentPlan(DiskFile f){
            string folder=SegmentFolder(f),path=Path.Combine(folder,"plan.json");SafeDestination(folder);SafeDestination(path);
            if(!File.Exists(path))return null;
            SegmentPlan p;try{p=new JavaScriptSerializer().Deserialize<SegmentPlan>(File.ReadAllText(path,Encoding.UTF8));}catch{return null;}
            if(p==null||p.Identity!=SegmentIdentity(f)||p.Length!=f.Length||p.Offset<0||p.Offset>=p.Length||p.Parts<2||p.Parts>8||p.Length-p.Offset<p.Parts)return null;
            return p;
        }
        static void ClearSegments(DiskFile f){
            string folder=SegmentFolder(f);SafeDestination(folder);if(!Directory.Exists(folder))return;
            foreach(string name in Enumerable.Range(0,8).SelectMany(i=>new[]{i+".part",i+".part.ydr-state",i+".part.ydr-state.tmp"}).Concat(new[]{"plan.json","plan.tmp","plan.json.tmp"})){
                string path=Path.Combine(folder,name);SafeDestination(path);if(File.Exists(path))File.Delete(path);
            }
            if(!Directory.EnumerateFileSystemEntries(folder).Any())Directory.Delete(folder);
        }
        static long SegmentBytes(DiskFile f,SegmentPlan plan){
            if(plan==null)return 0;long bytes=0;
            for(int i=0;i<plan.Parts;i++){string path=SegmentPath(SegmentFolder(f),i);SafeDestination(path);if(File.Exists(path))bytes+=Math.Min(new FileInfo(path).Length,SegmentStart(plan,i+1)-SegmentStart(plan,i));}
            return bytes;
        }
        async Task<Result> Download(Job job){
            try{
                if(job.Map!=null)return await DownloadMapped(job).ConfigureAwait(false);
                bool segmented=!String.IsNullOrEmpty(job.File.Md5)&&job.File.Length>=Math.Max(64,Math.Min(4096,settings.MultiStreamThresholdMB))*1000000L&&Math.Max(1,Math.Min(8,settings.StreamsPerFile))>1;
                SegmentPlan saved=ReadSegmentPlan(job.File);
                if(segmented||saved!=null)return await DownloadSegments(job,Math.Max(1,Math.Min(8,settings.StreamsPerFile)),saved).ConfigureAwait(false);
                return await DownloadSingleWithSlot(job).ConfigureAwait(false);
            }catch(OperationCanceledException){return new Result{Status="cancelled_user",Message="Stopped by user.",Bytes=job.Bytes};}
            catch(Exception ex){return new Result{Status=stop.IsCancellationRequested?"cancelled_user":"failed",Message="Download preparation failed: "+ex.GetType().Name,Bytes=job.Bytes};}
        }
        async Task<Result> DownloadSingleWithSlot(Job job){
            for(int attempt=0;;attempt++){
                await downloadSlots.WaitAsync(stop.Token).ConfigureAwait(false);Result result;
                try{result=await DownloadSingle(job,settings.IdleSeconds+(attempt>0?2:0)).ConfigureAwait(false);}finally{downloadSlots.Release();}
                // A read error after the last byte must not send Range: size- and
                // risk resetting a complete partial on HTTP 416. Next preparation verifies it.
                if(stop.IsCancellationRequested||!result.RetryTransfer||attempt>=2||job.Bytes>=job.File.Length)return result;
            }
        }
        async Task<Result> ReadSegment(Job job,SegmentPlan plan,int index,TaskCompletionSource<bool> supported){
            for(int attempt=0;;attempt++){
                Result result=await ReadSegmentAttempt(job,plan,index,attempt==0?supported:null,settings.IdleSeconds+(attempt>0?2:0)).ConfigureAwait(false);
                if(stop.IsCancellationRequested||!result.RetryTransfer||attempt>=2)return result;
            }
        }
        async Task<Result> ReadSegmentAttempt(Job job,SegmentPlan plan,int index,TaskCompletionSource<bool> supported,int idleSeconds){
            string path=SegmentPath(SegmentFolder(job.File),index);long start=SegmentStart(plan,index),end=SegmentStart(plan,index+1)-1;
            bool acquired=false;long received=0;using(CancellationTokenSource idle=CancellationTokenSource.CreateLinkedTokenSource(stop.Token)){
                try{
                    SafeDestination(path);long existing=new FileInfo(path).Length;
                    if(existing>end-start+1)throw new IOException("Segment is larger than its range.");
                    if(existing==end-start+1){if(supported!=null)supported.TrySetResult(true);return new Result{Status="success"};}
                    await downloadSlots.WaitAsync(stop.Token).ConfigureAwait(false);acquired=true;
                    idle.CancelAfter(TimeSpan.FromSeconds(idleSeconds));
                    using(HttpRequestMessage request=new HttpRequestMessage(HttpMethod.Get,job.Link)){
                        request.Headers.Range=new RangeHeaderValue(start+existing,end);
                        using(HttpResponseMessage response=await downloadClient.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,idle.Token).ConfigureAwait(false))
                        using(CancellationTokenRegistration abort=idle.Token.Register(delegate{response.Dispose();})){
                            if(response.StatusCode==HttpStatusCode.OK||response.StatusCode==HttpStatusCode.RequestedRangeNotSatisfiable)return new Result{Status="range_unavailable",Message="Server does not support the requested byte range."};
                            if(!response.IsSuccessStatusCode)throw new ApiException((int)response.StatusCode);
                            ContentRangeHeaderValue range=response.Content.Headers.ContentRange;
                            if(response.StatusCode!=HttpStatusCode.PartialContent||range==null||range.Unit!="bytes"||range.From!=start+existing||range.To!=end||range.Length!=job.File.Length||response.Content.Headers.ContentEncoding.Count>0){InvalidateLink(job);throw new IOException("Invalid segmented download range.");}
                            if(supported!=null)supported.TrySetResult(true);
                            idle.CancelAfter(TimeSpan.FromSeconds(idleSeconds));
                            using(Stream input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                            using(DurablePartial output=new DurablePartial(path,SegmentIdentity(job.File)+"\nsegment:"+index,existing)){
                                byte[] buffer=new byte[65536];long left=end-start+1-existing;
                                while(left>0){int n=await ReadDownloadData(input,buffer,(int)Math.Min(left,buffer.Length),idle.Token).ConfigureAwait(false);if(n==0)throw new DownloadReadException(new EndOfStreamException());idle.CancelAfter(TimeSpan.FromSeconds(idleSeconds));await output.WriteAsync(buffer,0,n,stop.Token).ConfigureAwait(false);left-=n;received+=n;Interlocked.Add(ref job.Bytes,n);ConfirmLink(job);}
                                if(await ReadDownloadData(input,buffer,1,idle.Token).ConfigureAwait(false)!=0){InvalidateLink(job);output.SetLength(0);throw new IOException("Segment response exceeded its range.");}
                            }
                        }
                    }
                    return new Result{Status="success"};
                }catch(OperationCanceledException){if(!stop.IsCancellationRequested&&received==0)InvalidateLink(job);return new Result{Status=stop.IsCancellationRequested?"cancelled_user":"timeout",Message=stop.IsCancellationRequested?"Stopped by user.":"A file segment received no data during the idle timeout.",RetryTransfer=!stop.IsCancellationRequested&&received>0};}
                catch(ApiException ex){InvalidateLink(job);return new Result{Status=ex.Code>=500?"cloud_error":"failed",Message="Segment HTTP "+ex.Code};}
                catch(Exception ex){if(!stop.IsCancellationRequested&&received==0&&(idle.IsCancellationRequested||ex is DownloadReadException||ex is HttpRequestException))InvalidateLink(job);return new Result{Status=stop.IsCancellationRequested?"cancelled_user":idle.IsCancellationRequested?"timeout":"failed",Message=stop.IsCancellationRequested?"Stopped by user.":idle.IsCancellationRequested?"A file segment received no data during the idle timeout.":"Segment download failed: "+ex.GetType().Name,RetryTransfer=!stop.IsCancellationRequested&&received>0&&(ex is DownloadReadException||ex is HttpRequestException)};}
                finally{if(acquired)downloadSlots.Release();if(supported!=null)supported.TrySetResult(false);}
            }
        }
        async Task<Result> DownloadSegments(Job job,int workers,SegmentPlan plan){
            string folder=SegmentFolder(job.File),partial=job.File.Destination+".ydr-part";
            if(plan==null){
                ClearSegments(job.File);SafeDestination(folder);Directory.CreateDirectory(folder);
                plan=new SegmentPlan{Identity=SegmentIdentity(job.File),Offset=job.Bytes,Length=job.File.Length,Parts=Math.Min(workers,(int)Math.Min(8,(job.File.Length-job.Bytes)/1048576))};
                if(plan.Parts<2){ClearSegments(job.File);return await DownloadSingleWithSlot(job).ConfigureAwait(false);}
                SafeDestination(partial);using(FileStream prefix=new FileStream(partial,FileMode.OpenOrCreate,FileAccess.Write)){}
                DurablePartial.AtomicText(Path.Combine(folder,"plan.json"),new JavaScriptSerializer().Serialize(plan));
            }
            List<int> pending=new List<int>();
            // Verify each extent once, before request admission and HTTP idle
            // timers. Retried attempts use the checkpointed offset on disposal.
            for(int i=0;i<plan.Parts;i++){string path=SegmentPath(folder,i);SafeDestination(path);long length=DurablePartial.Recover(path,SegmentIdentity(job.File)+"\nsegment:"+i,!String.IsNullOrEmpty(job.File.Md5),stop.Token);long expected=SegmentStart(plan,i+1)-SegmentStart(plan,i);if(length>expected){DurablePartial.Reset(path,SegmentIdentity(job.File)+"\nsegment:"+i);length=0;}if(length!=expected)pending.Add(i);}
            job.Bytes=plan.Offset+SegmentBytes(job.File,plan);
            List<Result> results=new List<Result>();
            if(pending.Count>0){
                TaskCompletionSource<bool> supported=new TaskCompletionSource<bool>();Task<Result> first=ReadSegment(job,plan,pending[0],supported);
                if(await supported.Task.ConfigureAwait(false)){
                    int next=0;List<Task<List<Result>>> tasks=new List<Task<List<Result>>>();
                    for(int worker=1;worker<Math.Min(workers,pending.Count);worker++)tasks.Add(Task.Run(async delegate{List<Result> output=new List<Result>();int i;while((i=Interlocked.Increment(ref next))<pending.Count)output.Add(await ReadSegment(job,plan,pending[i],null).ConfigureAwait(false));return output;}));
                    results.Add(await first.ConfigureAwait(false));
                    if(workers==1)for(int i=1;i<pending.Count;i++)results.Add(await ReadSegment(job,plan,pending[i],null).ConfigureAwait(false));
                    foreach(List<Result> output in await Task.WhenAll(tasks).ConfigureAwait(false))results.AddRange(output);
                }else results.Add(await first.ConfigureAwait(false));
            }
            job.Bytes=plan.Offset+SegmentBytes(job.File,plan);
            if(stop.IsCancellationRequested)return new Result{Status="cancelled_user",Message="Stopped by user; file segments retained.",Bytes=job.Bytes};
            if(results.Any(r=>r.Status=="range_unavailable")){
                // All range requests are finished before switching to a single response.
                ClearSegments(job.File);job.Bytes=plan.Offset;return await DownloadSingleWithSlot(job).ConfigureAwait(false);
            }
            Result failure=results.FirstOrDefault(r=>r.Status!="success");if(failure!=null){failure.Bytes=job.Bytes;return failure;}
            // The contiguous .ydr-part remains unchanged until every segment is ready.
            // If merging is interrupted, preparation rolls it back to the saved offset.
            SafeDestination(partial);using(FileStream output=new FileStream(partial,FileMode.OpenOrCreate,FileAccess.Write,FileShare.Read,65536,true)){
                output.SetLength(plan.Offset);output.Position=plan.Offset;byte[] buffer=new byte[65536];
                for(int i=0;i<plan.Parts;i++){
                    string path=SegmentPath(folder,i);SafeDestination(path);long expected=SegmentStart(plan,i+1)-SegmentStart(plan,i);
                    using(FileStream input=File.OpenRead(path)){if(input.Length!=expected)throw new IOException("Incomplete segment before merge.");int n;while((n=await input.ReadAsync(buffer,0,buffer.Length,stop.Token).ConfigureAwait(false))>0)await output.WriteAsync(buffer,0,n,stop.Token).ConfigureAwait(false);}
                }
                output.Flush(true);
            }
            return FinishPayload(job,partial);
        }
    }
}
