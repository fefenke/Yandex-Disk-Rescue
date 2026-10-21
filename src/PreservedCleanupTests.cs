using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace DiskRescue {
    static class PreservedCleanupTests {
        static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
        static string Fixture(string directory,string data,string name,string kind){
            Directory.CreateDirectory(directory);string archive=Path.Combine(directory,"Сохранённые прежние файлы"),state=Path.Combine(data,"preservation");Directory.CreateDirectory(archive);Directory.CreateDirectory(state);
            File.WriteAllText(Path.Combine(archive,".ydr-owner"),HistoryStore.IdentityKey(data));File.WriteAllText(Path.Combine(state,"Сохранённые прежние файлы.root"),archive);
            string version="disk:/"+name+".bin\nv1|"+SafeDownloadTests.Hash(SafeDownloadTests.Content,false)+"\n1048576\n";
            string source=Path.Combine(directory,name+".bin"),identity=(kind=="foreign"?data+"-other":data)+"\n"+version;
            if(kind=="modern")identity="ydr-context:api-465C68E575D77DDE04F06B30\n"+version;
            RangeMap map=RangeMap.Open(source,identity,1048576,1,CancellationToken.None);
            if(kind=="confirmed")using(Stream output=map.Writer(0)){output.Write(new byte[]{1},0,1);}
            if(kind=="unconfirmed")using(FileStream output=File.OpenWrite(source+".ydr-download")){output.WriteByte(1);output.Flush(true);}
            string target=Path.Combine(archive,name+"_20261016_120000_0123456789.bin");File.Move(source+".ydr-download",target);File.Move(source+".ydr-map",target+".map");
            if(kind=="torn")File.AppendAllText(target+".map","{broken");
            if(kind=="ordinary"){File.Delete(target);File.WriteAllBytes(target,new byte[1048576]);}
            if(kind!="no-ledger")File.WriteAllText(Path.Combine(state,"aux-"+name+".json"),new JavaScriptSerializer().Serialize(new{Version=kind=="modern"?version:version+SafeDownloadTests.Hash(SafeDownloadTests.Content,true),Blocked=false,Copies=new object[0]}));
            return target;
        }
        public static void Run(string root){
            string directory=Path.Combine(root,"cleanup"),data=Path.Combine(root,"cleanup-data");
            string empty=Fixture(directory,data,"empty","empty");
            string emptyParent=Path.Combine(Path.GetDirectoryName(empty),"empty-parent");Directory.CreateDirectory(Path.Combine(emptyParent,"empty-child"));
            string[] retained=new string[7];int n=0;foreach(string kind in new[]{"confirmed","unconfirmed","foreign","torn","ordinary","no-ledger","locked"})retained[n++]=Fixture(directory,data,kind,kind);
            using(FileStream held=new FileStream(retained[6],FileMode.Open,FileAccess.Read,FileShare.None)){
                new FilePreserver(data,directory,new string[0]);
                Assert(!File.Exists(empty)&&!File.Exists(empty+".map"),"Empty 1.0.5 archive pair was not silently cleaned.");
                foreach(string path in retained)Assert(File.Exists(path)&&File.Exists(path+".map"),"Cleanup removed nonempty, unowned, invalid or locked data.");
            }
            new FilePreserver(data,directory,new string[0]);Assert(!File.Exists(retained[6]),"Interrupted cleanup did not retry the locked empty candidate.");
            Assert(!Directory.Exists(emptyParent),"Cleanup left empty nested archive directories.");
            string later=Fixture(directory,data,"later","empty");new FilePreserver(data,directory,new string[0]);Assert(File.Exists(later),"Completed migration was repeated.");
            directory=Path.Combine(root,"cleanup-wrong-owner");data=Path.Combine(root,"cleanup-wrong-owner-data");string foreignOwner=Fixture(directory,data,"empty","empty");File.WriteAllText(Path.Combine(Path.GetDirectoryName(foreignOwner),".ydr-owner"),"other");new FilePreserver(data,directory,new string[0]);Assert(File.Exists(foreignOwner),"Cleanup touched another owner's archive.");
            directory=Path.Combine(root,"cleanup-cancel");data=Path.Combine(root,"cleanup-cancel-data");string cancelled=Fixture(directory,data,"empty","empty");
            using(var stop=new CancellationTokenSource()){stop.Cancel();bool stopped=false;try{new FilePreserver(data,directory,new string[0],stop.Token);}catch(OperationCanceledException){stopped=true;}Assert(stopped&&File.Exists(cancelled)&&!File.Exists(Path.Combine(data,"preservation","empty-sparse-105.done")),"Cancelled cleanup deleted a file or marked itself complete.");}
            new FilePreserver(data,directory,new string[0]);Assert(!File.Exists(cancelled),"Cancelled cleanup did not resume.");
            directory=Path.Combine(root,"cleanup-old-marker");data=Path.Combine(root,"cleanup-old-marker-data");string old=Fixture(directory,data,"old","empty");File.Delete(old);File.Delete(old+".map");
            string archive=Path.GetDirectoryName(old),nested=Path.Combine(archive,"empty","nested"),keep=Path.Combine(archive,"keep");Directory.CreateDirectory(nested);Directory.CreateDirectory(keep);File.WriteAllText(Path.Combine(keep,"personal.txt"),"keep");
            File.WriteAllText(Path.Combine(data,"preservation","empty-sparse-105.done"),"1\n");
            new FilePreserver(data,directory,new string[0]);Assert(!Directory.Exists(Path.Combine(archive,"empty"))&&File.ReadAllText(Path.Combine(keep,"personal.txt"))=="keep"&&File.Exists(Path.Combine(archive,".ydr-owner")),"Directory cleanup did not upgrade the previous marker safely.");
            Directory.CreateDirectory(nested);new FilePreserver(data,directory,new string[0]);Assert(Directory.Exists(nested),"Completed directory cleanup was repeated.");
        }
        public static void RunCurrent(string root){
            string folder=Path.Combine(root,"current"),data=Path.Combine(root,"current-data"),state=Path.Combine(data,"preservation");Directory.CreateDirectory(folder);
            var f=new DiskFile{Path="disk:/photo.bin",Destination=Path.Combine(folder,"photo.bin"),Length=1048576,Modified="v1",Md5=SafeDownloadTests.Hash(SafeDownloadTests.Content,false)};
            string identity="ydr-context:api-465C68E575D77DDE04F06B30\n"+CloudIntegrity.Version(f);
            RangeMap.Open(f.Destination,identity,f.Length,1,CancellationToken.None);
            new FilePreserver(data,folder,new string[0]).PreservePartial(f,f.Destination+".ydr-download",f.Destination+".ydr-map");
            Assert(!Directory.Exists(Path.Combine(folder,"Сохранённые прежние файлы")),"An empty mapped placeholder was archived as a previous file.");
            Assert(!RangeMap.Exists(f.Destination),"Empty placeholder was not removed.");
            RangeMap map=RangeMap.Open(f.Destination,identity,f.Length,1,CancellationToken.None);
            using(FileStream output=File.OpenWrite(f.Destination+".ydr-download")){output.WriteByte(7);output.Flush(true);}
            new FilePreserver(data,folder,new string[0]).PreservePartial(f,f.Destination+".ydr-download",f.Destination+".ydr-map");
            Assert(Directory.GetFiles(Path.Combine(folder,"Сохранённые прежние файлы"),"*.map",SearchOption.AllDirectories).Length==1,"Unconfirmed written data was discarded.");
        }
        public static void RunUpgrade(string root){
            string folder=Path.Combine(root,"upgrade"),data=Path.Combine(root,"upgrade-data"),state=Path.Combine(data,"preservation");
            string old=Fixture(folder,data,"old","modern");File.WriteAllText(Path.Combine(state,"empty-sparse-105.done"),"1\n");File.WriteAllText(Path.Combine(state,"empty-archive-directories.done"),"1\n");
            new FilePreserver(data,folder,new string[0]);
            Assert(!File.Exists(old)&&!File.Exists(old+".map"),"Previously skipped modern empty archive survived cleanup upgrade.");
        }
        public static void RunOtherContext(string root){
            string folder=Path.Combine(root,"other-context-files"),parent=Path.Combine(root,"other-context-data"),oldData=Path.Combine(parent,"api-111111111111111111111111"),activeData=Path.Combine(parent,"api-222222222222222222222222");
            string old=Fixture(folder,oldData,"old","modern"),state=Path.Combine(activeData,"preservation");Directory.CreateDirectory(state);
            File.WriteAllText(Path.Combine(state,"Сохранённые прежние файлы.root"),Path.GetDirectoryName(old));
            new FilePreserver(activeData,folder,new string[0]);
            Assert(!File.Exists(old)&&!File.Exists(old+".map"),"An archive belonging to an inactive sibling context was never cleaned.");
        }
    }
}
