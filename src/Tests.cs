using System;
using System.IO;
using System.Linq;
using System.Text;

namespace DiskRescue {
    static class Tests {
        static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
        public static int Run(){
            string root=Path.Combine(Path.GetTempPath(),"disk-rescue-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            string result=Path.Combine(Path.GetDirectoryName(typeof(Tests).Assembly.Location),"self-test-results.txt");
            try{
                Directory.CreateDirectory(Path.Combine(root,"a"));Directory.CreateDirectory(Path.Combine(root,"b"));
                string a=Path.Combine(root,"a","same.jpg"),b=Path.Combine(root,"b","same.jpg");File.WriteAllText(a,"test");File.WriteAllText(b,"test");
                DiskFile first=Make(a),second=Make(b);string data=Path.Combine(root,"state");
                using(HistoryStore store=new HistoryStore(data,root)){
                    store.Save(first,"success","");store.Save(second,"cloud_error","0x80070185");
                    Assert(store.Records.Count==2,"Same-name files collided.");
                    Assert(!FileRules.Select(first,store.Find(a),RunMode.Continue),"Successful file not skipped.");
                    Assert(FileRules.Select(first,store.Find(a),RunMode.All),"Recheck-all excludes a success.");
                    Assert(FileRules.Select(second,store.Find(b),RunMode.CloudErrors),"Cloud-error retry failed.");
                    Assert(!FileRules.Select(first,store.Find(a),RunMode.CloudErrors),"Cloud-error retry includes a success.");
                    second.Local=false;Assert(!FileRules.Select(second,store.Find(b),RunMode.Continue),"Continue retried a completed cloud error.");
                    second.Local=false;second.DownloadedBytes=0;Assert(!FileRules.Select(second,store.Find(b),RunMode.ZeroDownload),"Sync error included in cancelled retry.");store.Save(second,"timeout","");Assert(!FileRules.Select(second,null,RunMode.ZeroDownload),"New file selected in cancelled retry.");Assert(FileRules.Select(second,store.Find(b),RunMode.ZeroDownload),"Zero-byte retry failed.");second.DownloadedBytes=1;Assert(FileRules.Select(second,store.Find(b),RunMode.ZeroDownload),"Partial timeout missing from incomplete retry.");second.DownloadedBytes=null;Assert(FileRules.Select(second,store.Find(b),RunMode.ZeroDownload),"Known timeout with unknown bytes missing from incomplete retry.");
                    Assert(FileRules.Select(first,null,RunMode.Continue),"New file is not selected.");
                    Assert(!FileRules.Select(second,store.Find(b),RunMode.Continue),"Continue retried a completed idle timeout.");store.Save(second,"cancelled_user","");Assert(FileRules.Select(second,store.Find(b),RunMode.Continue),"Stop-interrupted request was not resumed.");
                    first.Length++;Assert(FileRules.Select(first,store.Find(a),RunMode.Continue),"Changed file was skipped.");
                    Assert(FileRules.Order(new[]{second,first})[0]==first,"New files are not before failures.");
                }
                using(HistoryStore store=new HistoryStore(data,root))Assert(store.Records.Count==2,"History reload failed.");
                Assert(FileRules.Category("x.HEIC")=="photos"&&FileRules.Category("x.mov")=="videos"&&FileRules.Category("x.pdf")=="documents"&&FileRules.Category("x.iso")=="archives"&&FileRules.Category("x.flac")=="audio"&&FileRules.Category("x.exe")=="other","File categories failed.");
                Assert(Prerequisites.FrameworkSupported(461308)&&Prerequisites.FrameworkSupported(533509)&&!Prerequisites.FrameworkSupported(460798),"Framework prerequisite threshold failed.");
                Assert(!YandexCloudNative.Probe(a).Supported,"Local file incorrectly marked cloud-only.");
                Engine engine=new Engine(data);engine.Run(new Settings{Folder=root,RestartClient=false},RunMode.Continue);
                Assert(engine.Latest.Finished&&engine.Latest.Phase=="completed"&&engine.Latest.Available>=2,"Local engine run failed.");
                Assert(File.Exists(Path.Combine(engine.Latest.Report,"folders.csv")),"Folder report missing.");
                Assert(engine.Latest.ProgressChecked==engine.Latest.Total&&engine.Latest.ProgressTotal==engine.Latest.Total,"Native cumulative progress reset on continuation.");
                string before=File.ReadAllText(a);engine.Run(new Settings{Folder=root,RestartClient=false},RunMode.All);Assert(File.ReadAllText(a)==before,"Recheck changed local content.");
                string missing=Path.Combine(root,"missing");engine.Run(new Settings{Folder=missing},RunMode.Continue);Assert(engine.Latest.Phase=="error","Missing folder was not reported.");
                ApiTests.Run(root);
                File.WriteAllText(result,"PASS: native checks plus API authentication, five-request limit, idle cancellation, stop, path safety, separate history, case collisions, MD5 integrity, retry filters and encrypted token storage.",Encoding.UTF8);
                return 0;
            }catch(Exception ex){File.WriteAllText(result,"FAIL: "+ex.ToString(),Encoding.UTF8);return 1;}
        }
        static DiskFile Make(string path){FileInfo f=new FileInfo(path);return new DiskFile{Path=f.FullName,Folder=f.DirectoryName,Length=f.Length,Modified=f.LastWriteTimeUtc.ToString("o"),Category=FileRules.Category(path),Local=true};}
    }
}
