using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace DiskRescue {
    static class SegmentTests {
        const long Length=64L*1048576;
        static void Assert(bool value,string message){if(!value)throw new Exception(message);}
        static byte Pattern(long position,int seed){return (byte)((position+seed)%251);}
        static string Hash(int seed){using(MD5 hash=MD5.Create()){byte[] buffer=new byte[65536];for(long position=0;position<Length;position+=buffer.Length){for(int i=0;i<buffer.Length;i++)buffer[i]=Pattern(position+i,seed);hash.TransformBlock(buffer,0,buffer.Length,buffer,0);}hash.TransformFinalBlock(buffer,0,0);return BitConverter.ToString(hash.Hash).Replace("-","").ToLowerInvariant();}}
        sealed class Api:HttpMessageHandler {
            public int Seed,Files=1;public string Digest;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
                string json=request.RequestUri.AbsolutePath.EndsWith("/download")?"{\"href\":\"https://download.example/large\"}":request.RequestUri.AbsolutePath.EndsWith("/resources")?new JavaScriptSerializer().Serialize(new{type="dir",_embedded=new{items=Enumerable.Range(0,Files).Select(i=>new{path="disk:/large"+i+".mov",type="file",size=Length,md5=Digest,modified="v"+Seed}).ToArray()}}):"{\"user\":{\"uid\":\"segment-test\"}}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json)});
            }
        }
        sealed class Body:Stream {
            readonly Download owner;readonly long begin,length;readonly bool stall;long position;bool disposed;
            public Body(Download owner,long begin,long length,bool stall){this.owner=owner;this.begin=begin;this.length=length;this.stall=stall;}
            public override async Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken token){
                if(stall&&position>=65536)await Task.Delay(10000,token).ConfigureAwait(false);
                if(position>=length)return 0;await Task.Delay(1,token).ConfigureAwait(false);int n=(int)Math.Min(Math.Min(count,65536),length-position);
                for(int i=0;i<n;i++)buffer[offset+i]=Pattern(begin+position+i,owner.Seed);
                if(owner.Corrupt&&begin==0&&position==0)buffer[offset]^=1;
                position+=n;Interlocked.Add(ref owner.Bytes,n);if(owner.Stop!=null&&position>=131072)owner.Stop();return n;
            }
            protected override void Dispose(bool disposing){if(!disposed){disposed=true;Interlocked.Decrement(ref owner.Active);}base.Dispose(disposing);}
            public override int Read(byte[] b,int o,int c){return ReadAsync(b,o,c,CancellationToken.None).GetAwaiter().GetResult();}
            public override bool CanRead{get{return true;}}public override bool CanSeek{get{return false;}}public override bool CanWrite{get{return false;}}public override long Length{get{return length;}}public override long Position{get{return position;}set{throw new NotSupportedException();}}
            public override void Flush(){}public override long Seek(long p,SeekOrigin o){throw new NotSupportedException();}public override void SetLength(long l){throw new NotSupportedException();}public override void Write(byte[] b,int o,int c){throw new NotSupportedException();}
        }
        sealed class Download:HttpMessageHandler {
            public int Seed,Active,Peak,Calls,HeaderDelay=30;public bool Ignore,Wrong,Corrupt,Stall;public long Bytes;public Action Stop;public bool Resumed;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
                Assert(request.Headers.Authorization==null,"Segment leaked OAuth to the download host.");Interlocked.Increment(ref Calls);int active=Interlocked.Increment(ref Active);int old;do{old=Peak;if(old>=active)break;}while(Interlocked.CompareExchange(ref Peak,active,old)!=old);
                try{await Task.Delay(HeaderDelay,token).ConfigureAwait(false);}catch{Interlocked.Decrement(ref Active);throw;}
                bool range=request.Headers.Range!=null&&!Ignore;long from=range?request.Headers.Range.Ranges.Single().From.Value:0,to=range?request.Headers.Range.Ranges.Single().To??Length-1:Length-1;
                if(range&&from%(Length/4)!=0)Resumed=true;
                HttpResponseMessage response=new HttpResponseMessage(range?HttpStatusCode.PartialContent:HttpStatusCode.OK){Content=new StreamContent(new Body(this,from,to-from+1,Stall&&from>=Length/4&&from<Length/2))};
                response.Content.Headers.ContentLength=to-from+1;if(range)response.Content.Headers.ContentRange=new ContentRangeHeaderValue(Wrong?from+1:from,to,Length);return response;
            }
        }
        public static void Run(string root){
            string hash=Hash(0),newHash=Hash(7);
            for(int variant=0;variant<8;variant++){
                string data=Path.Combine(root,"segment-data-"+variant),folder=Path.Combine(root,"segments-"+variant);Directory.CreateDirectory(folder);
                Settings settings=new Settings{UseApi=true,ApiFolder=folder,ApiToken="test-secret",UseCachedApiList=false,StreamsPerFile=4,MultiStreamThresholdMB=64,ApiDownloadConcurrency=variant==6?1:3,IdleSeconds=1};
                Api api=new Api{Digest=hash,Files=variant==0?2:1};Download download=new Download{Ignore=variant==2,Wrong=variant==3,Corrupt=variant==4,Stall=variant==1||variant==7,HeaderDelay=variant==6?650:30};Snapshot last=null;
                ApiEngine engine=new ApiEngine(data,x=>last=x,api,download);if(variant==5)download.Stop=engine.Stop;
                engine.Run(settings,RunMode.Remaining);
                Assert(download.Active==0&&download.Peak<=settings.ApiDownloadConcurrency,"Segment requests exceeded the global connection budget or remained active.");
                string final=Path.Combine(folder,"large0.mov"),partial=final+".ydr-part";
                if(variant==0||variant==2||variant==6){Assert(last.Downloaded==api.Files&&ApiEngine.Verified(final,Length,hash,CancellationToken.None),"Parallel download, no-range fallback or queue-excluded timeout failed.");if(variant==0)Assert(download.Peak==3,"Segment downloads did not use available parallelism.");Assert(!Directory.Exists(final+".ydr-segments"),"Completed segmented download left chunk files.");}
                else if(variant==1||variant==7){
                    Assert(last.Downloaded==0&&last.Deferred==1&&!File.Exists(final)&&FileRules.HasPartialData(final),"Segment timeout lost partial data or promoted an incomplete file.");
                    // Simulate interruption halfway through merging. Preparation must roll
                    // the contiguous file back to the manifest offset before resuming.
                    using(FileStream torn=new FileStream(partial,FileMode.Append,FileAccess.Write))torn.Write(new byte[1000],0,1000);
                    int seed=variant==7?7:0;Download resumed=new Download{Seed=seed};Snapshot second=null;
                    new ApiEngine(data,x=>second=x,new Api{Seed=seed,Digest=seed==0?hash:newHash},resumed).Run(settings,RunMode.PartialFiles);
                    Assert(second.Downloaded==1&&ApiEngine.Verified(final,Length,seed==0?hash:newHash,CancellationToken.None),"Resumed segments, torn-merge recovery or cloud-version invalidation failed.");
                    if(variant==1)Assert(resumed.Resumed&&resumed.Bytes<Length/2,"Resume redownloaded completed segments or ignored a partial segment offset.");
                    else Assert(resumed.Bytes==Length,"Cloud version change reused old segment data.");
                }else if(variant==3||variant==4)Assert(last.Downloaded==0&&!File.Exists(final),"Invalid range or MD5-corrupted data became a completed file.");
                else Assert(last.Phase=="stopped"&&!File.Exists(final)&&FileRules.HasPartialData(final),"Stopping segment requests lost state or promoted an incomplete file.");
            }
        }
    }
}
