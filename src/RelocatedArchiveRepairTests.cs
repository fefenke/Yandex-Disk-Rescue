using System;
using System.IO;
using System.Threading;
namespace DiskRescue {
    static class RelocatedArchiveRepairTests {
        static void Assert(bool value,string message){if(!value)throw new Exception(message);}
        static DiskFile Fixture(string root,string kind,out string context,out string archived){
            string directory=Path.Combine(root,kind);context=Path.Combine(directory,"data","api-account");string output=Path.Combine(directory,"output"),archive=Path.Combine(output,"Сохранённые прежние файлы"),state=Path.Combine(context,"preservation");
            Directory.CreateDirectory(state);Directory.CreateDirectory(archive);string owner=HistoryStore.IdentityKey(context);
            File.WriteAllText(Path.Combine(state,"owner.id"),owner);File.WriteAllText(Path.Combine(state,"Сохранённые прежние файлы.root"),archive);File.WriteAllText(Path.Combine(archive,".ydr-owner"),owner);
            var f=new DiskFile{Path="disk:/example.bin",Modified="v1",Length=1048576,Destination=Path.Combine(output,"example.bin")};
            string identity="C:\\old\\"+(kind=="foreign"?"api-other":"api-account")+"\n"+CloudIntegrity.Version(f);
            RangeMap map=RangeMap.Open(f.Destination,identity,f.Length,1,CancellationToken.None);
            if(kind!="empty"&&kind!="empty-final"&&kind!="owner"&&kind!="cancel")using(Stream writer=map.Writer(0)){writer.Write(new byte[]{11,22,33,44},0,4);}
            archived=Path.Combine(archive,"example_20261016_120000_0123456789.bin");File.Move(f.Destination+".ydr-download",archived);File.Move(f.Destination+".ydr-map",archived+".map");
            if(kind=="corrupt")using(FileStream corrupt=File.OpenWrite(archived)){corrupt.WriteByte(99);}
            if(kind=="version")f.Modified="v2";
            if(kind=="owner")File.WriteAllText(Path.Combine(archive,".ydr-owner"),"other");return f;
        }
        public static void Run(string root){
            foreach(string kind in new[]{"empty","empty-final","confirmed","nonempty-final","guarded","progress","foreign","version","corrupt","owner","cancel"}){
                string context,source;DiskFile f=Fixture(root,kind,out context,out source);string output=Path.GetDirectoryName(f.Destination),identity="ydr-context:api-account\n"+CloudIntegrity.Version(f);
                if(kind=="progress"){RangeMap map=RangeMap.Open(f.Destination,identity,f.Length,1,CancellationToken.None);using(Stream writer=map.Writer(0)){writer.Write(new byte[]{11,22},0,2);}}
                if(kind=="empty-final"||kind=="nonempty-final")File.WriteAllText(f.Destination,"completed final");
                string nested=Path.Combine(Path.GetDirectoryName(source),"empty-child","nested");Directory.CreateDirectory(nested);
                if(kind=="cancel"){
                    using(var stop=new CancellationTokenSource()){stop.Cancel();bool cancelled=false;try{RelocatedArchiveRepair.Run(context,output,new[]{f},stop.Token);}catch(OperationCanceledException){cancelled=true;}Assert(cancelled&&File.Exists(source)&&!RangeMap.Exists(f.Destination),"Cancellation changed archives.");}continue;
                }
                bool guardCalled=false;
                RelocatedArchiveRepair.Run(context,output,new[]{f},CancellationToken.None,kind=="guarded"?(Func<DiskFile,bool>)delegate(DiskFile candidate){guardCalled=true;return false;}:null);
                if(kind=="guarded")Assert(guardCalled&&File.Exists(source)&&File.Exists(source+".map")&&!RangeMap.Exists(f.Destination),"Rejected import changed payloads or did not call ownership guard.");
                if(kind=="empty"||kind=="empty-final")Assert(!File.Exists(source)&&!File.Exists(source+".map"),"Owned empty pair survived repair.");
                else if(kind=="confirmed"||kind=="progress"){
                    Assert(File.Exists(source)&&RangeMap.Progress(f.Destination)==4,"Confirmed bytes were not retained/imported.");
                    Assert(File.ReadAllBytes(f.Destination+".ydr-download")[0]==11,"Current progress was overwritten.");
                    RelocatedArchiveRepair.Run(context,output,new[]{f},CancellationToken.None);Assert(RangeMap.Progress(f.Destination)==4,"Repair was not idempotent.");
                }else Assert(File.Exists(source)&&!RangeMap.Exists(f.Destination),"Foreign, corrupt, or unowned archive was modified.");
                if(kind=="empty-final"||kind=="nonempty-final")Assert(File.ReadAllText(f.Destination)=="completed final","Existing final changed.");
                if(kind!="owner")Assert(!Directory.Exists(Path.GetDirectoryName(nested))&&File.Exists(Path.Combine(Path.GetDirectoryName(source),".ydr-owner")),"Empty child directories remain or archive owner was removed.");
            }
        }
    }
}
