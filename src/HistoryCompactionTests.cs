using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace DiskRescue {
    static class HistoryCompactionTests {
        static void Check(bool value,string message){if(!value)throw new Exception(message);}
        static string Seed(string data,string identity){
            string directory=Path.Combine(data,HistoryStore.IdentityKey(identity));Directory.CreateDirectory(directory);
            string path=Path.Combine(directory,"history.jsonl");
            var record=new HistoryRecord{path="disk:/A",length=123,modified="version",status="success",message="evidence",time="time",downloadedBytes=123,localWriteTicks=638000000000000000,verification="hash",actualHash="actual",expectedHash="expected",preservedPath="preserved"};
            string line=new JavaScriptSerializer().Serialize(record);
            using(var writer=new StreamWriter(path,false,new UTF8Encoding(false))){for(int i=0;i<100000;i++)writer.WriteLine(line);writer.WriteLine(line.Replace("disk:/A","disk:/a"));}
            return path;
        }
        public static void Run(string root){
            string data=Path.Combine(root,"compaction-tests"),journal=Seed(data,"compact");long before=new FileInfo(journal).Length;
            Check(before>=16L*1024*1024,"Test journal too small.");
            File.WriteAllText(journal+".compact.tmp","stale interrupted compaction");
            using(var store=new HistoryStore(data,root,"compact")){}
            Check(new FileInfo(journal).Length<before/2,"Repeated history was not compacted.");
            using(var store=new HistoryStore(data,root,"compact")){
                Check(store.LoadedFromIndex,"Compacted journal index was not rebuilt.");Check(store.Records.Count==2,"API paths lost ordinal identity.");
                var r=store.Find("disk:/A");Check(r.length==123&&r.modified=="version"&&r.status=="success"&&r.message=="evidence"&&r.time=="time"&&r.downloadedBytes==123&&r.localWriteTicks==638000000000000000&&r.verification=="hash"&&r.actualHash=="actual"&&r.expectedHash=="expected"&&r.preservedPath=="preserved","Compaction lost resume evidence.");
                store.SaveApi(new DiskFile{Path="disk:/new",Length=5},"pending","");
            }
            using(var store=new HistoryStore(data,root,"compact"))Check(store.LoadedFromIndex&&store.Find("disk:/new")!=null,"Subsequent append/index invalid.");
            journal=Seed(data,"cancel");byte[] original=File.ReadAllBytes(journal);
            using(var cancel=new CancellationTokenSource()){using(var store=new HistoryStore(data,root,"cancel",cancel.Token)){cancel.Cancel();}}
            Check(Convert.ToBase64String(original)==Convert.ToBase64String(File.ReadAllBytes(journal)),"Cancelled preparation rewrote journal.");
            journal=Seed(data,"clean-cancelled-lifetime");before=new FileInfo(journal).Length;
            using(var cancel=new CancellationTokenSource())using(var store=new HistoryStore(data,root,"clean-cancelled-lifetime",cancel.Token)){
                cancel.Cancel();store.Close(true);
            }
            Check(new FileInfo(journal).Length<before/2,"Clean API close skipped compaction after lifetime cancellation.");
            using(var store=new HistoryStore(data,root,"clean-cancelled-lifetime"))Check(store.LoadedFromIndex,"Clean API close skipped index after lifetime cancellation.");
            journal=Seed(data,"failure");original=File.ReadAllBytes(journal);
            using(var store=new HistoryStore(data,root,"failure"))using(var held=new FileStream(journal,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){store.Dispose();}
            Check(Convert.ToBase64String(original)==Convert.ToBase64String(File.ReadAllBytes(journal)),"Failed replace damaged original.");
            Check(!File.Exists(journal+".compact.tmp"),"Failed replacement left a growing temporary journal.");
            string nativeRoot=Path.Combine(root,"native-case"),nativeDirectory=Path.Combine(data,HistoryStore.RootKey(nativeRoot));Directory.CreateDirectory(nativeDirectory);
            File.Copy(Seed(data,"native-seed"),Path.Combine(nativeDirectory,"history.jsonl"),true);
            using(var store=new HistoryStore(data,nativeRoot)){Check(store.Records.Count==1,"Native paths lost case-insensitive identity.");}
            using(var store=new HistoryStore(data,nativeRoot)){Check(store.LoadedFromIndex&&store.Records.Count==1&&store.Find("DISK:/A")!=null,"Native comparer changed after compaction.");}
        }
    }
}
