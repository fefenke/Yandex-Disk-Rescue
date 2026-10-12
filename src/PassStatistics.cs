using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
namespace DiskRescue {
    public sealed class PassResult {
        public DateTime StartedUtc;
        public string Method,Mode,Outcome;
        public double Seconds;
        public int Total,Processed,Downloaded,Available,Remaining,ZeroDownload,Errors;
        public static PassResult From(Snapshot s,DateTime started,bool api,RunMode mode){return new PassResult{StartedUtc=started,Method=api?"API":"Desktop",Mode=mode.ToString(),Outcome=s.Phase,Seconds=s.Seconds,Total=s.Total,Processed=s.Processed,Downloaded=s.Downloaded,Available=s.Available,Remaining=Math.Max(0,s.Total-s.Available),ZeroDownload=s.Categories.ConvertAll(c=>c.ZeroDownload).SumValues(),Errors=s.CloudErrors+s.OtherErrors};}
    }
    public static class PassHistory {
        public static bool ShouldRepeat(Snapshot result,bool repeat,bool stopped){return repeat&&!stopped&&!result.RecoveryRequired&&((result.Phase=="error"&&result.RetryableError)||(result.Phase=="completed"&&result.CloudErrors+result.OtherErrors+result.Deferred>0));}
        public static int SumValues(this List<int> values){int n=0;foreach(int value in values)n+=value;return n;}
        public static List<PassResult> Load(string path){List<PassResult> rows=new List<PassResult>();if(File.Exists(path))foreach(string line in File.ReadLines(path)){try{PassResult row=new JavaScriptSerializer().Deserialize<PassResult>(line);if(row!=null)rows.Add(row);}catch{}}return rows;}
        public static void Append(string path,PassResult row){File.AppendAllText(path,new JavaScriptSerializer().Serialize(row)+Environment.NewLine,new UTF8Encoding(false));}
    }
}
