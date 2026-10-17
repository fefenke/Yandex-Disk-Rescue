using System;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;

namespace DiskRescue {
    static class ArchiveNameMigrationTests {
        static void Assert(bool value,string message){if(!value)throw new Exception(message);}
        public static void Run(string root){
            foreach(string scenario in new[]{"normal","resume-before","resume-after","personal","collision","pending","cancel","foreign"}){
                string folder=Path.Combine(root,"archive-name-"+scenario),data=folder+"-data",state=Path.Combine(data,"preservation");
                string plain=Path.Combine(folder,"Сохранённые прежние файлы"),old=plain+" (YDR-1010e270)",owner=HistoryStore.IdentityKey(data);
                Directory.CreateDirectory(plain);Directory.CreateDirectory(old);Directory.CreateDirectory(state);
                File.WriteAllText(Path.Combine(plain,".ydr-owner"),HistoryStore.IdentityKey("before-move"));
                File.WriteAllText(Path.Combine(old,".ydr-owner"),owner);File.WriteAllText(Path.Combine(old,"saved.bin"),"precious");
                File.WriteAllText(Path.Combine(state,"Сохранённые прежние файлы.root"),old);
                bool migrate=scenario=="normal"||scenario.StartsWith("resume-");
                if(scenario.StartsWith("resume-"))File.WriteAllText(Path.Combine(state,"archive-rename.pending"),new JavaScriptSerializer().Serialize(new{Source=old,Target=plain}));
                if(scenario=="resume-after"){File.Delete(Path.Combine(plain,".ydr-owner"));Directory.Delete(plain);Directory.Move(old,plain);}
                if(scenario=="foreign")File.WriteAllText(Path.Combine(old,".ydr-owner"),HistoryStore.IdentityKey("someone-else"));
                if(scenario=="personal")File.WriteAllText(Path.Combine(plain,"personal.bin"),"personal");
                if(scenario=="pending")File.WriteAllText(Path.Combine(state,"intent-broken.json"),new JavaScriptSerializer().Serialize(new{Source=Path.Combine(folder,"missing.ydr-download"),Target=Path.Combine(folder,"missing.bin"),LedgerPath=Path.Combine(state,"aux-missing.json"),Destination=Path.Combine(folder,"missing.bin")}));
                using(var cancellation=new CancellationTokenSource()){
                    if(scenario=="cancel")cancellation.Cancel();
                    try{new FilePreserver(data,folder,scenario=="collision"?new[]{Path.Combine(plain,"cloud.bin")}:new string[0],cancellation.Token);}catch(OperationCanceledException){if(scenario!="cancel")throw;}
                }
                if(migrate)Assert(!Directory.Exists(old)&&File.ReadAllText(Path.Combine(plain,"saved.bin"))=="precious"&&File.ReadAllText(Path.Combine(state,"Сохранённые прежние файлы.root"))==plain&&!File.Exists(Path.Combine(state,"archive-rename.pending")),"Owned suffixed archive was not safely restored to its ordinary name.");
                else Assert(File.ReadAllText(Path.Combine(old,"saved.bin"))=="precious"&&File.ReadAllText(Path.Combine(state,"Сохранённые прежние файлы.root"))==old,"Archive rename ignored a conflict, recovery operation or cancellation.");
            }
            string historyRoot=Path.Combine(root,"archive-history-files"),historyData=Path.Combine(root,"archive-history-data");Directory.CreateDirectory(historyRoot);
            string identity="archive-history",oldRoot=Path.Combine(historyRoot,Title()+" (YDR-1010e270)"),newRoot=Path.Combine(historyRoot,Title());Directory.CreateDirectory(oldRoot);File.WriteAllText(Path.Combine(oldRoot,"saved.bin"),"copy");
            using(var history=new HistoryStore(historyData,historyRoot,identity)){
                string state=Path.Combine(history.DirectoryPath,"preservation");Directory.CreateDirectory(state);File.WriteAllText(Path.Combine(state,Title()+".root"),oldRoot);File.WriteAllText(Path.Combine(oldRoot,".ydr-owner"),HistoryStore.IdentityKey(history.DirectoryPath));
                history.SaveApi(new DiskFile{Path="disk:/saved.bin",Length=4,Modified="v1",PreservedPath=Path.Combine(oldRoot,"saved.bin"),ActualHash="evidence"},"failed","original message");
                new FilePreserver(history.DirectoryPath,historyRoot,new string[0]);history.RefreshPreservedPaths();
            }
            using(var history=new HistoryStore(historyData,historyRoot,identity)){var record=history.Find("disk:/saved.bin");Assert(record.preservedPath==Path.Combine(newRoot,"saved.bin")&&File.Exists(record.preservedPath)&&record.actualHash=="evidence"&&record.message=="original message","Archive rename lost history evidence or left a stale preserved path.");}
        }
        static string Title(){return "Сохранённые прежние файлы";}
    }
}
