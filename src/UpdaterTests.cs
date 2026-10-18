using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace DiskRescue {
    internal static class UpdaterTests {
        static void Assert(bool value,string message){if(!value)throw new Exception(message);}
        static string Hash(string file){using(SHA256 sha=SHA256.Create())using(Stream stream=File.OpenRead(file))return "sha256:"+BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
        static void Archive(string file,bool malicious){using(ZipArchive zip=ZipFile.Open(file,ZipArchiveMode.Create)){foreach(string name in new[]{"YandexDiskRescue.exe","YandexDiskRescue.exe.config"})using(StreamWriter writer=new StreamWriter(zip.CreateEntry(name).Open()))writer.Write("new app");if(malicious)using(StreamWriter writer=new StreamWriter(zip.CreateEntry("data/history.jsonl").Open()))writer.Write("must not be installed");}}
        public static void ReleaseChannel(string root){
            string metadata=new JavaScriptSerializer().Serialize(new[]{new{draft=false,prerelease=true,tag_name="v9.8.7",assets=new[]{new{name="YandexDiskRescue-win-x64.zip",digest="sha256:"+new string('a',64),browser_download_url="https://github.com/fefenke/Yandex-Disk-Rescue/releases/download/v9.8.7/YandexDiskRescue-win-x64.zip"}}}});
            Assert(AppUpdater.Choose(metadata,new Version(1,1,2),true)==null,"A numeric prerelease was offered to a stable user.");
            Assert(AppUpdater.Choose(metadata.Replace("\"prerelease\":true","\"prerelease\":false"),new Version(1,1,2),true)!=null,"A stable update was rejected.");
        }
        sealed class DownloadHandler:HttpMessageHandler {
            public Stream Body;public HttpResponseMessage Response;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
                Response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(Body)};
                return Task.FromResult(Response);
            }
        }
        // Deliberately ignores ReadAsync's token: the updater must still return
        // promptly and dispose the response when the transport stalls.
        sealed class StalledBody:Stream {
            readonly TaskCompletionSource<int> read=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            public readonly TaskCompletionSource<bool> Started=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public bool Closed;
            public override Task<int> ReadAsync(byte[] b,int o,int n,CancellationToken token){Started.TrySetResult(true);return read.Task;}
            protected override void Dispose(bool disposing){Closed=true;read.TrySetException(new ObjectDisposedException("stalled-body"));base.Dispose(disposing);}
            public override bool CanRead{get{return true;}}public override bool CanSeek{get{return false;}}public override bool CanWrite{get{return false;}}
            public override long Length{get{throw new NotSupportedException();}}public override long Position{get{throw new NotSupportedException();}set{throw new NotSupportedException();}}
            public override void Flush(){throw new NotSupportedException();}public override int Read(byte[] b,int o,int n){throw new NotSupportedException();}public override long Seek(long o,SeekOrigin s){throw new NotSupportedException();}public override void SetLength(long n){throw new NotSupportedException();}public override void Write(byte[] b,int o,int n){throw new NotSupportedException();}
        }
        static Task<string> Fetch(UpdateRelease release,HttpClient client,CancellationToken token,TimeSpan idle,string tempRoot){
            MethodInfo method=typeof(AppUpdater).GetMethod("Download",BindingFlags.Static|BindingFlags.NonPublic,null,new[]{typeof(UpdateRelease),typeof(HttpClient),typeof(CancellationToken),typeof(TimeSpan),typeof(string)},null);
            Assert(method!=null,"Updater has no cancellable, idle-limited download boundary.");
            return (Task<string>)method.Invoke(null,new object[]{release,client,token,idle,tempRoot});
        }
        static void ExpectCancellation(Task<string> task,string reason){
            try{Assert(task.Wait(3000),reason+" did not finish within 3 seconds.");throw new Exception(reason+" returned a package.");}
            catch(AggregateException ex){Assert(ex.GetBaseException() is OperationCanceledException,reason+" failed with "+ex.GetBaseException().GetType().Name);}
        }
        public static void DownloadReliability(string root){
            string temp=Path.Combine(root,"update-temp");Directory.CreateDirectory(temp);
            var release=new UpdateRelease{Url="https://download.example/update.zip",Digest="sha256:"+new string('0',64)};
            var stalled=new StalledBody();using(var client=new HttpClient(new DownloadHandler{Body=stalled})){
                ExpectCancellation(Fetch(release,client,CancellationToken.None,TimeSpan.FromMilliseconds(120),temp),"Idle timeout");
                Assert(stalled.Closed,"Timeout did not dispose the response stream.");Assert(Directory.GetDirectories(temp).Length==0,"Timed-out update left staging data.");
            }
            stalled=new StalledBody();using(var stop=new CancellationTokenSource())using(var client=new HttpClient(new DownloadHandler{Body=stalled})){
                Task<string> task=Fetch(release,client,stop.Token,TimeSpan.FromSeconds(30),temp);
                Assert(stalled.Started.Task.Wait(2000),"Cancellation test never started reading the body.");stop.Cancel();ExpectCancellation(task,"User cancellation");
                Assert(stalled.Closed&&Directory.GetDirectories(temp).Length==0,"Cancelled update did not close its stream and remove staging.");
            }
            string zip=Path.Combine(root,"retry-update.zip");Archive(zip,false);release.Digest=Hash(zip);
            using(var client=new HttpClient(new DownloadHandler{Body=new MemoryStream(File.ReadAllBytes(zip))})){
                Task<string> task=Fetch(release,client,CancellationToken.None,TimeSpan.FromSeconds(3),temp);
                Assert(task.Wait(3000)&&File.ReadAllText(Path.Combine(task.Result,"YandexDiskRescue.exe"))=="new app","Retry after cancellation failed to verify and extract a valid update.");
            }
            release.Digest="sha256:"+new string('0',64);int before=Directory.GetDirectories(temp).Length;
            using(var client=new HttpClient(new DownloadHandler{Body=new MemoryStream(File.ReadAllBytes(zip))})){
                bool rejected=false;try{Fetch(release,client,CancellationToken.None,TimeSpan.FromSeconds(3),temp).GetAwaiter().GetResult();}catch(IOException){rejected=true;}
                Assert(rejected&&Directory.GetDirectories(temp).Length==before,"Checksum failure left an update package or accepted corrupt bytes.");
            }
        }
        public static void UpdateControls(string root){
            MethodInfo download=typeof(MainForm).GetMethod("DownloadUpdate",BindingFlags.Instance|BindingFlags.NonPublic);
            Assert(download!=null,"Update UI has no cancellable download operation.");
            using(var form=new MainForm(Path.Combine(root,"update-controls"))){
                var received=new TaskCompletionSource<string>();
                Func<UpdateRelease,CancellationToken,Task<string>> fetch=delegate(UpdateRelease release,CancellationToken token){token.Register(()=>received.TrySetCanceled());return received.Task;};
                Task running=(Task)download.Invoke(form,new object[]{new UpdateRelease(),fetch});
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                var start=(System.Windows.Forms.Button)typeof(MainForm).GetField("start",flags).GetValue(form);
                var cancel=(System.Windows.Forms.Button)typeof(MainForm).GetField("cancelRelease",flags).GetValue(form);
                Assert(!start.Enabled&&cancel.Enabled,"Download did not disable starting a pass or enable cancellation.");
                typeof(System.Windows.Forms.Button).GetMethod("OnClick",flags).Invoke(cancel,new object[]{EventArgs.Empty});
                var timer=System.Diagnostics.Stopwatch.StartNew();
                while(!running.IsCompleted&&timer.ElapsedMilliseconds<3000){System.Windows.Forms.Application.DoEvents();Thread.Sleep(5);}
                Assert(running.IsCompleted,"Update cancellation did not return control to the UI.");running.GetAwaiter().GetResult();
                Assert(start.Enabled&&!(bool)typeof(MainForm).GetField("installingUpdate",flags).GetValue(form),"Cancelled update left application controls locked.");
                Assert((bool)typeof(MainForm).GetField("renderingFailed",flags).GetValue(form)==false,"Cancellation was treated as a UI crash.");
            }
        }
        internal static void Run(string root){
            ReleaseChannel(root);DownloadReliability(root);UpdateControls(root);
            using(System.Diagnostics.Process current=System.Diagnostics.Process.GetCurrentProcess()){
                bool stopped=false;try{AppUpdater.WaitForParent(current.Id,current.MainModule.FileName,1);}catch(IOException){stopped=true;}Assert(stopped,"Updater did not wait for a running parent.");
                bool mismatch=false;try{AppUpdater.WaitForParent(current.Id,"wrong-parent.exe",1);}catch(IOException){mismatch=true;}Assert(mismatch,"Updater accepted another parent executable.");
            }
            using(System.Diagnostics.Process exited=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"cmd.exe"),"/c exit 0"){UseShellExecute=false,CreateNoWindow=true})){exited.WaitForExit();AppUpdater.WaitForParent(exited.Id,"already-exited.exe",1000);}
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
