using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace DiskRescue {
    internal static class UpdaterTests {
        static void Assert(bool value,string message){if(!value)throw new Exception(message);}
        static string Hash(string file){using(SHA256 sha=SHA256.Create())using(Stream stream=File.OpenRead(file))return "sha256:"+BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
        static void Archive(string file,bool malicious){using(ZipArchive zip=ZipFile.Open(file,ZipArchiveMode.Create)){foreach(string name in new[]{"YandexDiskRescue.exe","YandexDiskRescue.exe.config"})using(StreamWriter writer=new StreamWriter(zip.CreateEntry(name).Open()))writer.Write("new app");if(malicious)using(StreamWriter writer=new StreamWriter(zip.CreateEntry("data/history.jsonl").Open()))writer.Write("must not be installed");}}
        internal static void Run(string root){
            string folder=Path.Combine(root,"updater");Directory.CreateDirectory(folder);string zip=Path.Combine(folder,"safe.zip"),package=Path.Combine(folder,"package");Archive(zip,false);AppUpdater.Extract(zip,package,Hash(zip));
            bool rejected=false;try{AppUpdater.Extract(zip,Path.Combine(folder,"wrong-hash"),"sha256:"+new string('0',64));}catch(IOException){rejected=true;}Assert(rejected,"Updater accepted wrong hash.");
            string bad=Path.Combine(folder,"bad.zip");Archive(bad,true);rejected=false;try{AppUpdater.Extract(bad,Path.Combine(folder,"bad-extract"),Hash(bad));}catch(IOException){rejected=true;}Assert(rejected,"Updater accepted a private data entry.");
            string target=Path.Combine(folder,"installed");Directory.CreateDirectory(Path.Combine(target,"data"));File.WriteAllText(Path.Combine(target,"YandexDiskRescue.exe"),"old app");File.WriteAllText(Path.Combine(target,"data","history.jsonl"),"history");File.WriteAllBytes(Path.Combine(target,"data","api-token.bin"),new byte[]{1,2,3});AppUpdater.Install(package,target);Assert(File.ReadAllText(Path.Combine(target,"YandexDiskRescue.exe"))=="new app"&&File.ReadAllText(Path.Combine(target,"data","history.jsonl"))=="history"&&File.ReadAllBytes(Path.Combine(target,"data","api-token.bin"))[2]==3,"Updater changed private data or failed to install.");
            string rollbackPackage=Path.Combine(folder,"rollback-source","package"),rollback=Path.Combine(folder,"rollback-target");Directory.CreateDirectory(rollbackPackage);File.Copy(Path.Combine(package,"YandexDiskRescue.exe"),Path.Combine(rollbackPackage,"YandexDiskRescue.exe"));File.Copy(Path.Combine(package,"YandexDiskRescue.exe.config"),Path.Combine(rollbackPackage,"YandexDiskRescue.exe.config"));Directory.CreateDirectory(Path.Combine(rollback,"YandexDiskRescue.exe.config"));File.WriteAllText(Path.Combine(rollback,"YandexDiskRescue.exe"),"original");rejected=false;try{AppUpdater.Install(rollbackPackage,rollback);}catch(IOException){rejected=true;}Assert(rejected&&File.ReadAllText(Path.Combine(rollback,"YandexDiskRescue.exe"))=="original","Updater failed to roll back.");
            string metadata=new JavaScriptSerializer().Serialize(new[]{new{draft=false,tag_name="v9.8.7",assets=new[]{new{name="YandexDiskRescue-win-x64.zip",digest="sha256:"+new string('a',64),browser_download_url="https://github.com/fefenke/Yandex-Disk-Rescue/releases/download/v9.8.7/YandexDiskRescue-win-x64.zip"}}}});
            Assert(AppUpdater.Choose(metadata,new Version(0,2,7,0),true)!=null&&AppUpdater.Choose(metadata,new Version(9,8,7,0),true)==null&&AppUpdater.Choose(metadata,new Version(0,2,7,0),false)==null,"Updater version or architecture selection incorrect.");
            Assert(AppUpdater.Choose(metadata.Replace("github.com/","example.com/"),new Version(0,2,7,0),true)==null,"Updater accepted a foreign download host.");
        }
    }
}
