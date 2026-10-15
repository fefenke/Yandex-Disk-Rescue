using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DiskRescue {
    internal sealed class ApiPacerStatus {
        public int IntervalMs,Waiting,Code;
        public double CooldownSeconds;
    }
    internal sealed class ApiPacer {
        readonly object sync=new object();
        readonly SemaphoreSlim launch=new SemaphoreSlim(1,1);
        readonly Stopwatch clock=Stopwatch.StartNew();
        readonly Func<double> milliseconds;
        readonly Func<int,CancellationToken,Task> delay;
        int baseline=50,effective=50,level,generation,successes,waiting,lastCode;
        double nextLaunch,cooldownUntil,lastFailure;
        bool configured;
        public ApiPacer(){milliseconds=()=>clock.Elapsed.TotalMilliseconds;delay=(ms,token)=>Task.Delay(ms,token);}
        internal ApiPacer(Func<double> milliseconds,Func<int,CancellationToken,Task> delay){this.milliseconds=milliseconds;this.delay=delay;}
        public void Configure(int intervalMs){lock(sync){baseline=Math.Max(25,Math.Min(1000,intervalMs));if(!configured){configured=true;effective=baseline;nextLaunch=milliseconds()+baseline;}else effective=Math.Max(baseline,effective);}}
        public async Task<int> WaitAsync(CancellationToken cancellation){
            Interlocked.Increment(ref waiting);bool acquired=false;
            try{
                await launch.WaitAsync(cancellation).ConfigureAwait(false);acquired=true;
                while(true){
                    cancellation.ThrowIfCancellationRequested();int wait;
                    lock(sync){double now=milliseconds(),due=Math.Max(nextLaunch,cooldownUntil);if(now>=due){nextLaunch=now+effective;return generation;}wait=(int)Math.Max(1,Math.Min(250,Math.Ceiling(due-now)));}
                    await delay(wait,cancellation).ConfigureAwait(false);
                }
            }finally{if(acquired)launch.Release();Interlocked.Decrement(ref waiting);}
        }
        public void Failure(int code,double retryAfterSeconds,int ticket){
            if(code!=408&&code!=429&&code<500)return;
            if(Double.IsNaN(retryAfterSeconds)||Double.IsInfinity(retryAfterSeconds)||retryAfterSeconds<0)retryAfterSeconds=0;
            lock(sync){
                double now=milliseconds();
                // Responses from the same already-sent batch share one congestion episode.
                if(level==0||(ticket==generation&&now>=cooldownUntil)){level=Math.Min(7,level+1);effective=Math.Min(5000,Math.Max(baseline,effective)*2);generation++;}
                successes=0;lastFailure=now;lastCode=code;
                double fallback=Math.Min(60,1<<Math.Max(0,level-1));
                cooldownUntil=Math.Max(cooldownUntil,now+Math.Max(fallback,retryAfterSeconds)*1000);
            }
        }
        public void Success(int ticket){lock(sync){
            double now=milliseconds();if(ticket!=generation||now<cooldownUntil)return;
            successes++;
            if(successes>=20&&now-lastFailure>=5000){effective=Math.Max(baseline,(int)Math.Floor(effective*0.8));level=0;successes=0;}
        }}
        public ApiPacerStatus Status(){lock(sync){return new ApiPacerStatus{IntervalMs=effective,Waiting=Interlocked.CompareExchange(ref waiting,0,0),Code=lastCode,CooldownSeconds=Math.Max(0,(cooldownUntil-milliseconds())/1000)};}}
        internal static double RetryAfterSeconds(HttpResponseMessage response){
            try{
                var header=response.Headers.RetryAfter;if(header==null)return 0;
                if(header.Delta.HasValue)return Math.Max(0,header.Delta.Value.TotalSeconds);
                if(header.Date.HasValue)return Math.Max(0,(header.Date.Value-(response.Headers.Date??DateTimeOffset.UtcNow)).TotalSeconds);
            }catch(FormatException){}
            return 0;
        }
    }
}
