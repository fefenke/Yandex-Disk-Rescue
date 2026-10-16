using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace DiskRescue {
    static class TransferRetryTests {
        const string Payload="abcdefghijkl";
        static void Assert(bool value,string message){if(!value)throw new Exception(message);}
        static string Digest(){using(MD5 hash=MD5.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.ASCII.GetBytes(Payload))).Replace("-","").ToLowerInvariant();}
        sealed class Api:HttpMessageHandler {
            public int Files=1;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation){
                string text;
                if(request.RequestUri.AbsolutePath.EndsWith("/download"))text="{\"href\":\"https://download.example/"+(request.RequestUri.Query.Contains("b.txt")?"b":"a")+"\"}";
                else if(request.RequestUri.AbsolutePath.EndsWith("/resources")){
                    string requested=Uri.UnescapeDataString(request.RequestUri.Query.Split('&')[0].Substring(6));
                    text=requested!="disk:/"?new JavaScriptSerializer().Serialize(new{path=requested,type="file",size=Payload.Length,md5=Digest(),modified="v"}):new JavaScriptSerializer().Serialize(new{type="dir",_embedded=new{items=Enumerable.Range(0,Files).Select(i=>new{path="disk:/"+(i==0?"a":"b")+".txt",type="file",size=Payload.Length,md5=Digest(),modified="v"}).ToArray()}});
                }
                else text="{\"user\":{\"uid\":\"retry-account\"}}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(text)});
            }
        }
        sealed class Body:Stream {
            readonly int start,length,failAfter,delay;readonly bool eof;int position;bool delayed;
            public Body(int start,int length,int failAfter,int delay,bool eof){this.start=start;this.length=length;this.failAfter=failAfter;this.delay=delay;this.eof=eof;}
            public override async Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken cancellation){
                if(!delayed&&delay>0){delayed=true;await Task.Delay(delay,cancellation).ConfigureAwait(false);}
                if(failAfter>=0&&position>=failAfter){if(eof)return 0;throw new IOException("Synthetic network interruption");}
                int size=Math.Min(count,length-position);if(failAfter>=0)size=Math.Min(size,failAfter-position);
                for(int i=0;i<size;i++)buffer[offset+i]=(byte)Payload[start+position+i];position+=size;return size;
            }
            public override int Read(byte[] b,int o,int c){return ReadAsync(b,o,c,CancellationToken.None).GetAwaiter().GetResult();}
            public override bool CanRead{get{return true;}}public override bool CanWrite{get{return false;}}public override bool CanSeek{get{return false;}}
            public override long Length{get{return length;}}public override long Position{get{return position;}set{throw new NotSupportedException();}}
            public override void Flush(){}public override long Seek(long p,SeekOrigin o){throw new NotSupportedException();}public override void SetLength(long l){throw new NotSupportedException();}public override void Write(byte[] b,int o,int c){throw new NotSupportedException();}
        }
        sealed class Download:HttpMessageHandler {
            public string Scenario;public bool Segmented;public Action Stop;
            public readonly List<long> Starts=new List<long>();public readonly List<string> Order=new List<string>();
            int attempts;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation){
                Assert(request.Headers.Authorization==null,"Retry leaked OAuth.");
                long start=request.Headers.Range==null?0:request.Headers.Range.Ranges.Single().From.Value;
                long end=request.Headers.Range==null?Payload.Length-1:request.Headers.Range.Ranges.Single().To??Payload.Length-1;
                bool target=request.RequestUri.AbsolutePath=="/a"&&(!Segmented||end==5);
                int attempt=0;lock(Order){Order.Add(request.RequestUri.AbsolutePath+":"+start);if(target){attempt=++attempts;Starts.Add(start);}}
                if(Scenario=="fair"&&target&&attempt==1)await Task.Delay(250,cancellation).ConfigureAwait(false);
                if(Scenario=="stop"&&target&&attempt==2){Stop();cancellation.ThrowIfCancellationRequested();}
                if(Scenario=="status"&&target&&attempt==2)return new HttpResponseMessage((HttpStatusCode)429);
                bool fail=target&&(Scenario=="always"||Scenario=="no-progress"||Scenario=="old-progress"||attempt<=2&&(Scenario=="twice"||Scenario=="capped")||attempt==1);
                int failAfter=fail?2:-1;
                if(target&&Scenario=="full-body-break")failAfter=Payload.Length;
                if(target&&(Scenario=="empty"||Scenario=="old-progress"||Scenario=="no-progress"&&attempt==2))failAfter=0;
                int delay=target&&(Scenario=="longer"||Scenario=="timeout")&&attempt==2?1500:target&&Scenario=="capped"&&attempt==3?3400:0;
                // A timeout after data, followed by a response slower than the original idle budget.
                Stream stream=new Body((int)start,(int)(end-start+1),Scenario=="timeout"&&target&&attempt==1?-1:failAfter,delay,Scenario=="eof");
                if((Scenario=="timeout"||Scenario=="disposed-timeout")&&target&&attempt==1)stream=new StallBody((int)start,(int)(end-start+1),Scenario=="disposed-timeout");
                bool range=request.Headers.Range!=null;
                HttpResponseMessage response=new HttpResponseMessage(range?HttpStatusCode.PartialContent:HttpStatusCode.OK){Content=new StreamContent(stream)};
                response.Content.Headers.ContentLength=end-start+1;
                if(range)response.Content.Headers.ContentRange=new ContentRangeHeaderValue(Scenario=="wrong-range"&&target&&attempt==2?start+1:start,end,Payload.Length);
                return response;
            }
        }
        sealed class StallBody:Stream {
            readonly int start,length;readonly bool disposedTimeout;bool sent;
            public StallBody(int start,int length,bool disposedTimeout){this.start=start;this.length=length;this.disposedTimeout=disposedTimeout;}
            public override async Task<int> ReadAsync(byte[] b,int o,int c,CancellationToken token){if(sent){try{await Task.Delay(10000,token);}catch(OperationCanceledException){if(disposedTimeout)throw new ObjectDisposedException("Synthetic response disposal");throw;}return 0;}sent=true;int n=Math.Min(2,c);for(int i=0;i<n;i++)b[o+i]=(byte)Payload[start+i];return n;}
            public override int Read(byte[] b,int o,int c){return ReadAsync(b,o,c,CancellationToken.None).GetAwaiter().GetResult();}
            public override bool CanRead{get{return true;}}public override bool CanWrite{get{return false;}}public override bool CanSeek{get{return false;}}public override long Length{get{return length;}}public override long Position{get{return 0;}set{throw new NotSupportedException();}}
            public override void Flush(){}public override long Seek(long p,SeekOrigin o){throw new NotSupportedException();}public override void SetLength(long l){throw new NotSupportedException();}public override void Write(byte[] b,int o,int c){throw new NotSupportedException();}
        }
        public static void Run(string root){
            foreach(bool segmented in new[]{false,true})foreach(string scenario in new[]{"full-body-break","disposed-timeout","twice","always","no-progress","old-progress","empty","longer","timeout","capped","eof","wrong-range","status","stop","fair"}){
                if(segmented&&scenario=="full-body-break")continue;
                string id=(segmented?"segment":"single")+"-"+scenario,folder=Path.Combine(root,"retry-files-"+id),data=Path.Combine(root,"retry-data-"+id);Directory.CreateDirectory(folder);
                DiskFile file=new DiskFile{Path="disk:/a.txt",Destination=Path.Combine(folder,"a.txt"),Length=Payload.Length,Modified="v|"+Digest(),Md5=Digest(),DownloadedBytes=0};
                if(segmented||scenario=="old-progress"){
                    File.WriteAllText(file.Destination+".ydr-part",!segmented&&scenario=="old-progress"?"ab":"");
                    if(segmented){string parts=file.Destination+".ydr-segments";Directory.CreateDirectory(parts);File.WriteAllText(Path.Combine(parts,"plan.json"),new JavaScriptSerializer().Serialize(new{Identity=file.Path+"\n"+file.Modified+"\n"+file.Length,Offset=0,Length=Payload.Length,Parts=2}));if(scenario=="old-progress")File.WriteAllText(Path.Combine(parts,"0.part"),"ab");}
                    using(HistoryStore history=new HistoryStore(data,folder,"retry-account|disk:/|"+folder.ToUpperInvariant()))history.SaveApi(file,"timeout","");
                }
                Api api=new Api{Files=scenario=="fair"?2:1};Download download=new Download{Scenario=scenario,Segmented=segmented};Snapshot last=null;
                ApiEngine engine=new ApiEngine(data,x=>last=x,api,download);download.Stop=engine.Stop;
                Settings settings=new Settings{UseApi=true,ApiFolder=folder,ApiToken="test-secret",UseCachedApiList=false,ApiDownloadConcurrency=1,StreamsPerFile=2,IdleSeconds=1};
                engine.Run(settings,RunMode.Remaining);
                int expected=scenario=="always"||scenario=="twice"||scenario=="capped"?3:scenario=="full-body-break"||scenario=="old-progress"||scenario=="empty"?1:2;
                Assert(download.Starts.Count==expected,id+": unexpected attempts: "+download.Starts.Count+", expected "+expected);
                Assert(download.Starts[0]==(scenario=="old-progress"?2:0),id+": wrong initial range.");
                if(expected>1)Assert(download.Starts[1]==2,id+": retry lost the saved byte offset.");
                if(expected==3)Assert(download.Starts[2]==4,id+": second retry did not continue its range.");
                bool complete=scenario=="twice"||scenario=="longer"||scenario=="timeout"||scenario=="disposed-timeout"||scenario=="eof"||scenario=="fair";
                Assert(File.Exists(file.Destination)==complete,id+": unexpected completed file.");
                if(complete)Assert(File.ReadAllText(file.Destination)==Payload,id+": final content was corrupted.");
                if(scenario=="stop")Assert(last.Phase=="stopped",id+": retry ignored Stop.");
                Assert(settings.IdleSeconds==1,id+": retry changed the user's idle timeout.");
                if(scenario=="fair"){int other=download.Order.FindIndex(x=>x.StartsWith("/b:")),retry=download.Order.FindIndex(x=>x=="/a:2");Assert(other>=0&&other<retry,id+": retry overtook a waiting file.");}
                if(scenario=="full-body-break"){
                    Assert(File.ReadAllText(file.Destination+".ydr-download")==Payload,id+": complete partial was discarded after a final read error.");
                    new ApiEngine(data,x=>last=x,new Api(),download).Run(settings,RunMode.PartialFiles);
                    Assert(download.Starts.Count==1&&File.ReadAllText(file.Destination)==Payload,id+": complete partial was downloaded again instead of verified.");
                }
            }
        }
    }
}
