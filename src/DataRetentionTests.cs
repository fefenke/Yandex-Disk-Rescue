using System;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
namespace DiskRescue {
    static class DataRetentionTests {
        static void Check(bool value,string message){if(!value)throw new Exception(message);}
        static string Report(string parent,int index){string path=Path.Combine(parent,"2025-01-01_00-00-00_"+index.ToString("x6"));Directory.CreateDirectory(path);foreach(string name in new[]{"files.csv","categories.csv","folders.csv"})File.WriteAllText(Path.Combine(path,name),"data");File.WriteAllText(Path.Combine(path,"summary.json"),new JavaScriptSerializer().Serialize(new Snapshot{Report=path}));return path;}
        public static void RunReportRelocation(string root){
            string context="stable-context-"+Guid.NewGuid().ToString("N"),parent=Path.Combine(root,context,"reports");Directory.CreateDirectory(parent);
            string relocated=Report(parent,0),foreign=Report(parent,1),current=null;
            for(int i=2;i<60;i++)current=Report(parent,i);
            string legacy=Path.Combine(root,"old-root",context,"reports",Path.GetFileName(relocated));
            File.WriteAllText(Path.Combine(relocated,"summary.json"),new JavaScriptSerializer().Serialize(new Snapshot{Report=legacy}));
            File.WriteAllText(Path.Combine(foreign,"summary.json"),new JavaScriptSerializer().Serialize(new Snapshot{Report=Path.Combine(root,"old-root","foreign-context","reports",Path.GetFileName(foreign))}));
            typeof(HistoryStore).Assembly.GetType("DiskRescue.DataRetention").GetMethod("PruneReports").Invoke(null,new object[]{current});
            Check(!Directory.Exists(relocated),"Relocated report with stable context identity was never pruned.");
            Check(Directory.Exists(foreign),"Report belonging to a foreign context was pruned.");
        }
        public static void RunPassTrimGuard(string root){
            Directory.CreateDirectory(root);string path=Path.Combine(root,"guard-passes-"+Guid.NewGuid().ToString("N")+".jsonl");var json=new JavaScriptSerializer();
            using(var writer=new StreamWriter(path)){for(int i=0;i<1001;i++)writer.WriteLine(json.Serialize(new PassResult{Outcome="old-"+i}));}
            PassHistory.Append(path,new PassResult{Outcome="latest"});var rows=PassHistory.Load(path);
            Check(rows.Count==1000&&rows[0].Outcome=="old-2"&&rows[999].Outcome=="latest","Guarded pass trim lost latest records.");
        }
        public static void Run(string root){
            RunReportRelocation(root);
            RunPassTrimGuard(root);
            string data=Path.Combine(root,"retention-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(data);string history=Path.Combine(data,"passes.jsonl");
            for(int i=0;i<1005;i++)PassHistory.Append(history,new PassResult{StartedUtc=new DateTime(2025,1,1),Method="API",Outcome="outcome-"+i,ApiDownloadConcurrency=i,Seconds=i+0.25});
            var rows=PassHistory.Load(history);Check(rows.Count==1000&&rows[0].Outcome=="outcome-5"&&rows[999].Outcome=="outcome-1004"&&rows[999].ApiDownloadConcurrency==1004&&rows[999].Seconds==1004.25,"Pass history must retain exactly the latest 1000 complete records.");
            File.AppendAllText(history,"{torn");PassHistory.Append(history,new PassResult{Outcome="after-torn"});rows=PassHistory.Load(history);Check(rows.Count==1000&&rows[999].Outcome=="after-torn","Torn tail discarded new pass.");
            string parent=Path.Combine(data,"reports");Directory.CreateDirectory(parent);string current=Report(parent,0),unknown=Report(parent,1),busy=Report(parent,2),wrong=Report(parent,3),nested=Report(parent,4);File.WriteAllText(Path.Combine(unknown,"keep.txt"),"mine");File.WriteAllText(Path.Combine(wrong,"summary.json"),"{\"Report\":\"elsewhere\"}");Directory.CreateDirectory(Path.Combine(nested,"nested"));
            for(int i=5;i<60;i++)Report(parent,i);
            Type type=typeof(HistoryStore).Assembly.GetType("DiskRescue.DataRetention");Check(type!=null,"Report retention missing.");
            using(var held=new FileStream(Path.Combine(busy,"files.csv"),FileMode.Open,FileAccess.Read,FileShare.Read)){type.GetMethod("PruneReports").Invoke(null,new object[]{current});}
            Check(Directory.Exists(current)&&Directory.Exists(unknown)&&Directory.Exists(busy)&&Directory.Exists(wrong)&&Directory.Exists(nested),"Retention removed current/unknown/busy/unverified report.");
            Check(!Directory.Exists(Path.Combine(parent,"2025-01-01_00-00-00_000005"))&&Directory.Exists(Path.Combine(parent,"2025-01-01_00-00-00_00003b")),"Reports did not retain newest 50.");
        }
    }
}
