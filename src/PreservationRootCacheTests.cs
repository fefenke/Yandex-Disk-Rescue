using System;
using System.IO;

namespace DiskRescue {
    static class PreservationRootCacheTests {
        static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
        static bool Blocked(FilePreserver preserver,string path){try{preserver.CheckDestination(path);return false;}catch(IOException){return true;}}
        public static void Run(string root){
            string folder=Path.Combine(root,"root-cache-files"),data=Path.Combine(root,"root-cache-data"),state=Path.Combine(data,"preservation");
            Directory.CreateDirectory(folder);Directory.CreateDirectory(state);string archive=Path.Combine(folder,"archive"),config=Path.Combine(state,"custom.root");
            Directory.CreateDirectory(archive);File.WriteAllText(config,archive);var preserver=new FilePreserver(data,folder,new string[0]);
            using(var held=new FileStream(config,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){
                try{for(int i=0;i<500;i++)preserver.CheckDestination(Path.Combine(folder,"ordinary.bin"));}catch(IOException){throw new Exception("Per-file destination checks reread archive configuration instead of using the pass snapshot.");}
                Assert(Blocked(preserver,Path.Combine(archive,"saved.bin")),"Cached archive no longer protects preserved content.");
            }
            string otherArchive=Path.Combine(folder,"other-archive");Directory.CreateDirectory(otherArchive);File.WriteAllText(config,otherArchive);
            var nextPass=new FilePreserver(data,folder,new string[0]);Assert(Blocked(nextPass,Path.Combine(otherArchive,"saved.bin"))&&!Blocked(nextPass,Path.Combine(archive,"ordinary.bin")),"Next pass did not reload changed archive roots.");
            folder=Path.Combine(root,"root-cache-created-files");data=Path.Combine(root,"root-cache-created-data");Directory.CreateDirectory(folder);preserver=new FilePreserver(data,folder,new string[0]);
            var file=new DiskFile{Path="disk:/a.bin",Destination=Path.Combine(folder,"a.bin"),Length=3,Modified="v1"};string partial=file.Destination+".ydr-part";File.WriteAllText(partial,"old");preserver.PreservePartial(file,partial,null);
            string created=Path.Combine(folder,"Сохранённые прежние файлы");Assert(Blocked(preserver,Path.Combine(created,"saved.bin")),"Newly created archive was absent from the current pass cache.");
        }
    }
}
