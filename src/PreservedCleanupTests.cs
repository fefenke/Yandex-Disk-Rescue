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
            RangeMap map=RangeMap.Open(source,identity,1048576,1,CancellationToken.None);
            if(kind=="confirmed")using(Stream output=map.Writer(0)){output.Write(new byte[]{1},0,1);}
            if(kind=="unconfirmed")using(FileStream output=File.OpenWrite(source+".ydr-download")){output.WriteByte(1);output.Flush(true);}
            string target=Path.Combine(archive,name+"_20261016_120000_0123456789.bin");File.Move(source+".ydr-download",target);File.Move(source+".ydr-map",target+".map");
            if(kind=="torn")File.AppendAllText(target+".map","{broken");
            if(kind=="ordinary"){File.Delete(target);File.WriteAllBytes(target,new byte[1048576]);}
            if(kind!="no-ledger")File.WriteAllText(Path.Combine(state,"aux-"+name+".json"),new JavaScriptSerializer().Serialize(new{Version=version+SafeDownloadTests.Hash(SafeDownloadTests.Content,true),Blocked=false,Copies=new object[0]}));
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
    }
}
