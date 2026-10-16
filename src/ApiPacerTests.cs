using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace DiskRescue {
    static class ApiPacerTests {
        static void Assert(bool value,string message){if(!value)throw new Exception(message);}
        sealed class Api:HttpMessageHandler {
            public Stopwatch Clock=Stopwatch.StartNew();
            public double FailureAt=-1,NextAt=-1,FirstAt=-1;public int Links,FailLink=1,Files=6,RetrySeconds=1;
            public readonly ManualResetEvent Limited=new ManualResetEvent(false);
            public bool RequireNoContext,SawCallerContext;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation){
                if(RequireNoContext&&SynchronizationContext.Current!=null){SawCallerContext=true;throw new Exception("API handler ran on the caller synchronization context.");}
                double now=Clock.Elapsed.TotalMilliseconds;if(FirstAt<0)FirstAt=now;if(FailureAt>=0&&NextAt<0)NextAt=now;
                if(request.RequestUri.AbsolutePath.EndsWith("/download")){
                    if(Interlocked.Increment(ref Links)==FailLink){FailureAt=now;HttpResponseMessage limited=new HttpResponseMessage((HttpStatusCode)429);limited.Headers.RetryAfter=new RetryConditionHeaderValue(TimeSpan.FromSeconds(RetrySeconds));Limited.Set();return Task.FromResult(limited);}
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"href\":\"https://download.example/file\"}")});
                }
                string requested=Uri.UnescapeDataString(request.RequestUri.Query.Split('&')[0].TrimStart('?'));
                if(request.RequestUri.AbsolutePath.EndsWith("/resources")&&requested.StartsWith("path=disk:/pacer-",StringComparison.Ordinal))return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(new JavaScriptSerializer().Serialize(new{path=requested.Substring(5),type="file",size=4,md5="098f6bcd4621d373cade4e832627b4f6",modified="v"}))});
                string json=request.RequestUri.AbsolutePath.EndsWith("/resources")?new JavaScriptSerializer().Serialize(new{type="dir",_embedded=new{items=Enumerable.Range(0,Files).Select(i=>new{path="disk:/pacer-"+i+".txt",type="file",size=4,md5="098f6bcd4621d373cade4e832627b4f6",modified="v"}).ToArray()}}):"{\"user\":{\"uid\":\"pacer-account\"}}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json)});
            }
        }
        sealed class Download:HttpMessageHandler {
            public bool Slow;public int Bytes,Code;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation){
                Assert(request.Headers.Authorization==null,"Pacer leaked OAuth to download host.");
                return Task.FromResult(new HttpResponseMessage(Code==0?HttpStatusCode.OK:(HttpStatusCode)Code){Content=Slow?(HttpContent)new StreamContent(new Body(this)):new StringContent("test")});
            }
        }
        sealed class BurstApi:HttpMessageHandler {
            readonly HttpMessageInvoker normal=new HttpMessageInvoker(new Api{FailLink=0});
            readonly TaskCompletionSource<bool> ready=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            readonly Stopwatch clock=Stopwatch.StartNew();readonly object sync=new object();int links;
            public double FailureAt=-1,NextAt=-1;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
                if(!request.RequestUri.AbsolutePath.EndsWith("/download"))return await normal.SendAsync(request,token).ConfigureAwait(false);
                int call=Interlocked.Increment(ref links);
                if(call<=3){
                    if(call==3)ready.TrySetResult(true);
                    await ready.Task.ConfigureAwait(false);token.ThrowIfCancellationRequested();
                    lock(sync)if(FailureAt<0)FailureAt=clock.Elapsed.TotalMilliseconds;
                    HttpResponseMessage limited=new HttpResponseMessage((HttpStatusCode)429);limited.Headers.RetryAfter=new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));return limited;
                }
                lock(sync)if(NextAt<0)NextAt=clock.Elapsed.TotalMilliseconds;
                return await normal.SendAsync(request,token).ConfigureAwait(false);
            }
            protected override void Dispose(bool disposing){if(disposing)normal.Dispose();base.Dispose(disposing);}
        }
        sealed class Body:Stream {
            readonly Download owner;int position;
            public Body(Download owner){this.owner=owner;}
            public override async Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken token){if(position==4)return 0;await Task.Delay(200,token);buffer[offset]=(byte)"test"[position++];Interlocked.Increment(ref owner.Bytes);return 1;}
            public override int Read(byte[] b,int o,int c){return ReadAsync(b,o,c,CancellationToken.None).GetAwaiter().GetResult();}
            public override bool CanRead{get{return true;}}public override bool CanWrite{get{return false;}}public override bool CanSeek{get{return false;}}public override long Length{get{return 4;}}public override long Position{get{return position;}set{throw new NotSupportedException();}}
            public override void Flush(){}public override long Seek(long p,SeekOrigin o){throw new NotSupportedException();}public override void SetLength(long l){throw new NotSupportedException();}public override void Write(byte[] b,int o,int c){throw new NotSupportedException();}
        }
        static void PolicyTests(){
            double now=0;ApiPacer pacer=new ApiPacer(()=>now,(ms,token)=>{token.ThrowIfCancellationRequested();now+=ms;return Task.FromResult(true);});pacer.Configure(25);
            int oldTicket=pacer.WaitAsync(CancellationToken.None).GetAwaiter().GetResult();Assert(now>=25,"API baseline spacing was skipped.");
            pacer.Failure(429,7,oldTicket);Assert(pacer.Status().IntervalMs==50&&pacer.Status().CooldownSeconds>=7,"Server Retry-After was shortened.");
            for(int i=0;i<7;i++)pacer.Failure(429,1,oldTicket);
            Assert(pacer.Status().IntervalMs==50,"Concurrent 429 responses multiplied one congestion episode.");
            now+=8000;for(int i=0;i<30;i++)pacer.Success(oldTicket);Assert(pacer.Status().IntervalMs==50,"Old in-flight successes erased adaptive slowdown.");
            int ticket=pacer.WaitAsync(CancellationToken.None).GetAwaiter().GetResult();pacer.Failure(503,0,ticket);Assert(pacer.Status().IntervalMs==100&&pacer.Status().CooldownSeconds>=2,"Repeated API congestion did not increase backoff.");
            pacer.Configure(25);Assert(pacer.Status().IntervalMs==100&&pacer.Status().CooldownSeconds>=2,"Pass setup reset the shared pacer.");
            ticket=pacer.WaitAsync(CancellationToken.None).GetAwaiter().GetResult();for(int i=0;i<20;i++)pacer.Success(ticket);Assert(pacer.Status().IntervalMs==100,"Recovery ignored the healthy-time window.");
            now+=5000;for(int i=0;i<20;i++)pacer.Success(ticket);Assert(pacer.Status().IntervalMs==80,"Recovery did not gradually lower the interval.");
            for(int i=0;i<200;i++)pacer.Success(ticket);Assert(pacer.Status().IntervalMs==25,"Recovery failed to reach or exceeded the selected request rate.");
            pacer.Failure(403,60,ticket);Assert(pacer.Status().CooldownSeconds==0,"Access denial incorrectly throttled the API.");
            for(int i=0;i<14;i++){ticket=pacer.WaitAsync(CancellationToken.None).GetAwaiter().GetResult();pacer.Failure(429,0,ticket);}
            Assert(pacer.Status().IntervalMs==5000&&pacer.Status().CooldownSeconds==60,"Adaptive limits are not bounded.");
            using(HttpResponseMessage response=new HttpResponseMessage((HttpStatusCode)429)){
                DateTimeOffset server=new DateTimeOffset(2020,1,1,0,0,0,TimeSpan.Zero);response.Headers.Date=server;response.Headers.RetryAfter=new RetryConditionHeaderValue(server.AddSeconds(7));Assert(ApiPacer.RetryAfterSeconds(response)==7,"Date Retry-After did not account for server clock.");
                response.Headers.RetryAfter=new RetryConditionHeaderValue(server.AddSeconds(-1));Assert(ApiPacer.RetryAfterSeconds(response)==0,"Past Retry-After created a negative wait.");
                response.Headers.RetryAfter=new RetryConditionHeaderValue(TimeSpan.FromSeconds(12));Assert(ApiPacer.RetryAfterSeconds(response)==12,"Delta Retry-After parsed incorrectly.");
                response.Headers.Date=null;response.Headers.RetryAfter=new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(7));double localDateWait=ApiPacer.RetryAfterSeconds(response);Assert(localDateWait>6&&localDateWait<=7,"Date Retry-After without a server Date header parsed incorrectly.");
                response.Headers.Remove("Retry-After");response.Headers.TryAddWithoutValidation("Retry-After","invalid");Assert(ApiPacer.RetryAfterSeconds(response)==0,"Malformed Retry-After did not use fallback.");
            }
            ApiPacer real=new ApiPacer();real.Configure(25);ticket=real.WaitAsync(CancellationToken.None).GetAwaiter().GetResult();real.Failure(429,60,ticket);
            using(CancellationTokenSource first=new CancellationTokenSource())using(CancellationTokenSource second=new CancellationTokenSource()){
                Task a=real.WaitAsync(first.Token),b=real.WaitAsync(second.Token);second.Cancel();first.Cancel();
                Assert(Task.WhenAll(a,b).ContinueWith(t=>{}).Wait(1000),"Cancellation remained blocked behind the API cooldown.");Assert(a.IsCanceled&&b.IsCanceled&&real.Status().Waiting==0,"Cancelled API waiters leaked or ran requests.");
                Assert(real.Status().CooldownSeconds>50,"Cancelling waiters discarded the server cooldown.");
            }
        }
        public static void Run(string root){
            PolicyTests();
            SynchronizationContext previous=SynchronizationContext.Current;
            try{
                SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
                double now=0;ApiPacer immediate=new ApiPacer(()=>now,(ms,token)=>{now+=ms;return Task.FromResult(true);});
                Api contextApi=new Api{FailLink=0,RequireNoContext=true};Snapshot contextResult=null;
                new ApiEngine(Path.Combine(root,"pacer-context-data"),x=>contextResult=x,contextApi,new Download(),immediate).Run(new Settings{UseApi=true,ApiToken="test-secret",ApiFolder=Path.Combine(root,"pacer-context-files"),UseCachedApiList=false},RunMode.Remaining);
                Assert(!contextApi.SawCallerContext&&contextResult.Downloaded==6,"Synchronous API admission inherited the caller context and can deadlock UI-bound handlers.");
            }finally{SynchronizationContext.SetSynchronizationContext(previous);}
            using(MainForm form=new MainForm(Path.Combine(root,"pacer-ui"))){
                BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
                Snapshot view=new Snapshot{Phase="running",ApiEffectiveIntervalMs=100,ApiCooldownCode=429,ApiCooldownSeconds=10,ApiStatusCapturedUtc=DateTime.UtcNow.AddSeconds(-2)};
                MethodInfo render=typeof(MainForm).GetMethod("Render",flags);Label detail=(Label)typeof(MainForm).GetField("detail",flags).GetValue(form);
                render.Invoke(form,new object[]{view});Assert(detail.Text.Contains("HTTP 429")&&detail.Text.Contains("ещё 8 с"),"API cooldown reason or live countdown is missing.");
                view.ApiStatusCapturedUtc=DateTime.UtcNow.AddSeconds(-11);render.Invoke(form,new object[]{view});Assert(!detail.Text.Contains("пауза ещё")&&detail.Text.Contains("100 мс"),"Expired cooldown remains visible or adaptive interval is hidden.");
            }
            Api api=new Api();Snapshot last=null;
            Settings settings=new Settings{UseApi=true,ApiFolder=Path.Combine(root,"pacer-files"),ApiToken="test-secret",UseCachedApiList=false,ApiRequestIntervalMs=25,ApiLinkConcurrency=7,LinkTimeoutSeconds=1};
            new ApiEngine(Path.Combine(root,"pacer-data"),x=>last=x,api,new Download()).Run(settings,RunMode.Remaining);
            Assert(api.NextAt-api.FailureAt>=950,"HTTP 429 did not pause the shared API queue for Retry-After.");
            Assert(last.Downloaded==5&&last.Processed==6,"API cooldown consumed queued link timeout or lost files.");
            Assert(settings.ApiRequestIntervalMs==25,"Adaptive pacing overwrote the selected API interval.");
            settings.ApiFolder=Path.Combine(root,"pacer-burst-files");BurstApi burstApi=new BurstApi();ApiPacer burstPacer=new ApiPacer();
            new ApiEngine(Path.Combine(root,"pacer-burst-data"),x=>last=x,burstApi,new Download(),burstPacer).Run(settings,RunMode.Remaining);
            Assert(burstApi.NextAt-burstApi.FailureAt>=950&&burstPacer.Status().IntervalMs==50&&last.Downloaded==3,"Overlapping 429 responses bypassed cooldown or multiplied one congestion episode.");
            settings.ApiFolder=Path.Combine(root,"pacer-shared-files");
            ApiPacer shared=new ApiPacer();Api firstApi=new Api{Files=1};
            new ApiEngine(Path.Combine(root,"pacer-shared-data"),x=>last=x,firstApi,new Download(),shared).Run(settings,RunMode.Remaining);
            Assert(shared.Status().CooldownSeconds>0,"A completed pass discarded an active server cooldown.");
            Api secondApi=new Api{Files=1,FailLink=0,Clock=firstApi.Clock};
            new ApiEngine(Path.Combine(root,"pacer-shared-data"),x=>last=x,secondApi,new Download(),shared).Run(settings,RunMode.Remaining);
            Assert(secondApi.FirstAt-firstApi.FailureAt>=950&&last.Downloaded==1,"The next pass bypassed shared API cooldown: elapsed="+(secondApi.FirstAt-firstApi.FailureAt)+", downloaded="+last.Downloaded+", error="+last.Error+", events="+String.Join(" | ",last.Events));
            settings.ApiFolder=Path.Combine(root,"pacer-independent-files");
            Api slowApi=new Api{FailLink=2};Download slowDownload=new Download{Slow=true};bool receivedDuringPause=false;
            new ApiEngine(Path.Combine(root,"pacer-independent-data"),x=>{last=x;if(x.ApiCooldownSeconds>0&&slowDownload.Bytes>0)receivedDuringPause=true;},slowApi,slowDownload).Run(settings,RunMode.All);
            Assert(receivedDuringPause,"API cooldown blocked already-started file data.");
            settings.ApiFolder=Path.Combine(root,"pacer-stop-files");
            Api stoppedApi=new Api{RetrySeconds=60};ApiPacer stoppedPacer=new ApiPacer();
            ApiEngine stopped=new ApiEngine(Path.Combine(root,"pacer-stop-data"),x=>last=x,stoppedApi,new Download(),stoppedPacer);
            Task run=Task.Run(()=>stopped.Run(settings,RunMode.All));Assert(stoppedApi.Limited.WaitOne(2000),"Stop test never reached API limiting.");Thread.Sleep(50);stopped.Stop();
            Assert(run.Wait(2000)&&last.Phase=="stopped"&&stoppedPacer.Status().Waiting==0,"Stop did not cancel the API pipeline during cooldown.");
            settings.ApiFolder=Path.Combine(root,"pacer-storage-files");
            Api storageApi=new Api{FailLink=0};ApiPacer storagePacer=new ApiPacer();
            new ApiEngine(Path.Combine(root,"pacer-storage-data"),x=>last=x,storageApi,new Download{Code=429},storagePacer).Run(settings,RunMode.All);
            Assert(storagePacer.Status().CooldownSeconds==0&&storagePacer.Status().IntervalMs==25,"Storage HTTP 429 incorrectly paused the REST API.");
        }
    }
}
