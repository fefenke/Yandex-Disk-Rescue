using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace DiskRescue {
    internal sealed class PassTransition {
        int failures;
        public DateTime ReadyUtc {get;private set;}
        public PassTransition(){}
        public int Prepare(Snapshot result){
            int delay=0;
            if((result.Phase=="error"&&result.RetryableError)||(result.Phase=="completed"&&result.ScanErrors>0&&result.Selected==0)){failures=Math.Min(failures+1,6);delay=Math.Min(30,1<<(failures-1));}
            else failures=0;
            ReadyUtc=DateTime.UtcNow.AddSeconds(delay);return delay;
        }
        public async Task WaitAsync(CancellationToken cancellation){
            cancellation.ThrowIfCancellationRequested();
            TimeSpan remaining=ReadyUtc-DateTime.UtcNow;
            if(remaining>TimeSpan.Zero)await Task.Delay(remaining,cancellation).ConfigureAwait(false);
        }
    }
    public sealed class PassResult {
        public DateTime StartedUtc;
        public string Method,Mode,Outcome;
        public double Seconds;
        public int IdleSeconds,LinkTimeoutSeconds,ApiLinkConcurrency,ApiDownloadConcurrency,ApiRequestIntervalMs;
        public int Total,Processed,Downloaded,Available,Remaining,ZeroDownload,Errors;
        public static PassResult From(Snapshot s,DateTime started,bool api,RunMode mode){return new PassResult{StartedUtc=started,Method=api?"API":"Desktop",Mode=mode.ToString(),Outcome=s.Phase,Seconds=s.Seconds,Total=s.Total,Processed=s.Processed,Downloaded=s.Downloaded,Available=s.Available,Remaining=Math.Max(0,s.Total-s.Available),ZeroDownload=s.Categories.ConvertAll(c=>c.ZeroDownload).SumValues(),Errors=s.CloudErrors+s.OtherErrors};}
    }
    public static class PassHistory {
        public const int MaximumEntries=1000;
        public static bool ShouldRepeat(Snapshot result,bool repeat,bool stopped){return repeat&&!stopped&&!result.RecoveryRequired&&((result.Phase=="error"&&result.RetryableError)||(result.Phase=="completed"&&result.ScanErrors+result.CloudErrors+result.OtherErrors+result.Deferred>0));}
        public static int SumValues(this List<int> values){int n=0;foreach(int value in values)n+=value;return n;}
        static List<PassResult> Read(string path,out long count){
            var rows=new Queue<PassResult>();count=0;var json=new JavaScriptSerializer();
            if(File.Exists(path))foreach(string line in File.ReadLines(path)){
                PassResult row;try{row=json.Deserialize<PassResult>(line);}catch{continue;}
                if(row==null)continue;count++;if(rows.Count==MaximumEntries)rows.Dequeue();rows.Enqueue(row);
            }
            return new List<PassResult>(rows);
        }
        public static List<PassResult> Load(string path){long count;return Read(path,out count);}
        public static void Append(string path,PassResult row){
            DurablePartial.SafePath(path);var json=new JavaScriptSerializer();
            using(var stream=new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.Read)){
                bool separator=false;if(stream.Length>0){stream.Position=stream.Length-1;separator=stream.ReadByte()!=10;}stream.Position=stream.Length;
                byte[] bytes=new UTF8Encoding(false).GetBytes((separator?Environment.NewLine:"")+json.Serialize(row)+Environment.NewLine);stream.Write(bytes,0,bytes.Length);stream.Flush(true);
            }
            // Keep writers excluded from reading the retained state through replacement.
            using(var guard=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete)){
                long count;var rows=Read(path,out count);
                if(count>MaximumEntries){var text=new StringBuilder();foreach(var item in rows)text.Append(json.Serialize(item)).Append(Environment.NewLine);DurablePartial.AtomicText(path,text.ToString());}
            }
        }
    }
}
