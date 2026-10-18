using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace DiskRescue {
    internal sealed class UpdateRelease {public Version Version;public string Url,Digest,Asset;}
    internal static class AppUpdater {
        const long Limit=100*1024*1024;
        internal static readonly string[] Allowed={"YandexDiskRescue.exe","YandexDiskRescue.exe.config","Start Yandex Disk Rescue.cmd","README.md","README.en.md","LICENSE","assets/preview.png","assets/preview-en.png","assets/preview-api.png","assets/preview-api-en.png"};
        static HttpClient Client(){ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;HttpClient client=new HttpClient{Timeout=TimeSpan.FromSeconds(90)};client.DefaultRequestHeaders.UserAgent.ParseAdd("YandexDiskRescue-Updater/1.0");return client;}
        internal static UpdateRelease Choose(string json,Version current,bool x64){
            JavaScriptSerializer parser=new JavaScriptSerializer{MaxJsonLength=8*1024*1024};object[] releases=parser.DeserializeObject(json) as object[];UpdateRelease best=null;
            if(releases==null)return null;string name="YandexDiskRescue-win-"+(x64?"x64":"x86")+".zip";
            foreach(object raw in releases){Dictionary<string,object> r=raw as Dictionary<string,object>;object prerelease;Version version;if(r==null||Convert.ToBoolean(r["draft"])||(r.TryGetValue("prerelease",out prerelease)&&Convert.ToBoolean(prerelease))||!Version.TryParse(Convert.ToString(r["tag_name"]).TrimStart('v'),out version)||version<=new Version(current.Major,current.Minor,Math.Max(0,current.Build))||(best!=null&&version<=best.Version))continue;
                foreach(object entry in (object[])r["assets"]){Dictionary<string,object> asset=(Dictionary<string,object>)entry;if(Convert.ToString(asset["name"])!=name)continue;object digest;string hash=asset.TryGetValue("digest",out digest)?Convert.ToString(digest):"";Uri uri;if(!hash.StartsWith("sha256:",StringComparison.Ordinal)||hash.Length!=71||!Uri.TryCreate(Convert.ToString(asset["browser_download_url"]),UriKind.Absolute,out uri)||uri.Scheme!="https"||uri.Host!="github.com"||!uri.AbsolutePath.StartsWith("/fefenke/Yandex-Disk-Rescue/releases/download/",StringComparison.Ordinal))continue;best=new UpdateRelease{Version=version,Asset=name,Url=uri.AbsoluteUri,Digest=hash};}
            }return best;
        }
        internal static async Task<UpdateRelease> Check(){using(HttpClient client=Client()){string json=await client.GetStringAsync("https://api.github.com/repos/fefenke/Yandex-Disk-Rescue/releases?per_page=30").ConfigureAwait(false);return Choose(json,Assembly.GetExecutingAssembly().GetName().Version,IntPtr.Size==8);}}
        internal static void Extract(string archive,string folder,string digest){Extract(archive,folder,digest,CancellationToken.None);}
        static void Extract(string archive,string folder,string digest,CancellationToken cancellation){
            using(SHA256 sha=SHA256.Create())using(Stream input=File.OpenRead(archive)){
                byte[] buffer=new byte[65536];int n;
                while((n=input.Read(buffer,0,buffer.Length))>0){cancellation.ThrowIfCancellationRequested();sha.TransformBlock(buffer,0,n,buffer,0);}
                sha.TransformFinalBlock(new byte[0],0,0);string actual="sha256:"+BitConverter.ToString(sha.Hash).Replace("-","").ToLowerInvariant();if(!String.Equals(actual,digest,StringComparison.OrdinalIgnoreCase))throw new IOException("Update checksum mismatch.");
            }
            cancellation.ThrowIfCancellationRequested();
            Directory.CreateDirectory(folder);HashSet<string> names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long expanded=0;
            using(ZipArchive zip=ZipFile.OpenRead(archive))foreach(ZipArchiveEntry entry in zip.Entries){cancellation.ThrowIfCancellationRequested();string name=entry.FullName.Replace('\\','/');if(!Allowed.Contains(name,StringComparer.Ordinal)||!names.Add(name)||(expanded+=entry.Length)>Limit)throw new IOException("Unexpected update archive entry.");string target=Path.Combine(folder,name.Replace('/','\\'));Directory.CreateDirectory(Path.GetDirectoryName(target));using(Stream source=entry.Open())using(FileStream output=File.Create(target)){byte[] buffer=new byte[65536];int read;long count=0;while((read=source.Read(buffer,0,buffer.Length))>0){cancellation.ThrowIfCancellationRequested();count+=read;if(count>entry.Length||count>Limit)throw new IOException("Update entry too large.");output.Write(buffer,0,read);}if(count!=entry.Length)throw new IOException("Incomplete update entry.");}}
            if(!names.Contains("YandexDiskRescue.exe")||!names.Contains("YandexDiskRescue.exe.config"))throw new IOException("Update executable missing.");
        }
        internal static async Task<string> Download(UpdateRelease release,CancellationToken cancellation){
            using(HttpClient client=Client())return await Download(release,client,cancellation,TimeSpan.FromSeconds(30),Path.GetTempPath()).ConfigureAwait(false);
        }
        internal static async Task<string> Download(UpdateRelease release,HttpClient client,CancellationToken cancellation,TimeSpan idleTimeout,string temporaryRoot){
            cancellation.ThrowIfCancellationRequested();
            string staging=Path.Combine(temporaryRoot,"YandexDiskRescue-update-"+Guid.NewGuid().ToString("N"));
            string archive=Path.Combine(staging,"update.zip"),package=Path.Combine(staging,"package");
            try{
                Directory.CreateDirectory(staging);
                using(CancellationTokenSource overall=CancellationTokenSource.CreateLinkedTokenSource(cancellation)){
                    overall.CancelAfter(TimeSpan.FromMinutes(15));
                    using(HttpResponseMessage response=await client.GetAsync(release.Url,HttpCompletionOption.ResponseHeadersRead,overall.Token).ConfigureAwait(false)){
                        response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength>Limit)throw new IOException("Update too large.");
                        using(Stream input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using(FileStream output=File.Create(archive)){
                            byte[] buffer=new byte[65536];long total=0;
                            while(true){
                                int read;
                                using(CancellationTokenSource idle=CancellationTokenSource.CreateLinkedTokenSource(overall.Token)){
                                    idle.CancelAfter(idleTimeout);
                                    using(idle.Token.Register(delegate{try{response.Dispose();}catch{/* Cancellation is still observed below. */}})){
                                        try{read=await ReadCancelled(input.ReadAsync(buffer,0,buffer.Length,idle.Token),idle.Token).ConfigureAwait(false);}
                                        catch(Exception){idle.Token.ThrowIfCancellationRequested();throw;}
                                    }
                                }
                                if(read==0)break;
                                if((total+=read)>Limit)throw new IOException("Update too large.");
                                await output.WriteAsync(buffer,0,read,overall.Token).ConfigureAwait(false);
                            }
                        }
                    }
                    overall.Token.ThrowIfCancellationRequested();Extract(archive,package,release.Digest,overall.Token);
                    overall.Token.ThrowIfCancellationRequested();return package;
                }
            }catch{Discard(package);throw;}
        }
        static async Task<int> ReadCancelled(Task<int> pending,CancellationToken cancellation){
            var cancelled=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using(cancellation.Register(()=>cancelled.TrySetResult(true))){
                if(await Task.WhenAny(pending,cancelled.Task).ConfigureAwait(false)!=pending){
                    // A transport may ignore cancellation. Observe a later failure
                    // after response disposal without waiting indefinitely for it.
                    ObserveReadFailure(pending);throw new OperationCanceledException(cancellation);
                }
                cancellation.ThrowIfCancellationRequested();return await pending.ConfigureAwait(false);
            }
        }
        static void ObserveReadFailure(Task pending){
            pending.ContinueWith(task=>{var ignored=task.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
        }
        internal static void Discard(string package){
            // Only a freshly generated staging tree may be removed; refuse links.
            try{
                string staging=Path.GetDirectoryName(Path.GetFullPath(package));Guid id;
                const string prefix="YandexDiskRescue-update-";string name=Path.GetFileName(staging);
                if(Path.GetFileName(package)!="package"||!name.StartsWith(prefix,StringComparison.Ordinal)||!Guid.TryParseExact(name.Substring(prefix.Length),"N",out id)||!Directory.Exists(staging))return;
                CheckStaging(staging);Directory.Delete(staging,true);
            }catch{/* Cleanup must not mask a failed or cancelled download. */}
        }
        static void CheckStaging(string directory){
            DurablePartial.SafePath(directory);
            foreach(string entry in Directory.GetFileSystemEntries(directory)){
                FileAttributes attributes=File.GetAttributes(entry);
                if((attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Update staging contains a link.");
                if((attributes&FileAttributes.Directory)!=0)CheckStaging(entry);
            }
        }
        internal static void Launch(string package){
            string helper=Path.Combine(Path.GetDirectoryName(package),"helper");Directory.CreateDirectory(helper);foreach(string name in new[]{"YandexDiskRescue.exe","YandexDiskRescue.exe.config"})File.Copy(Path.Combine(package,name),Path.Combine(helper,name));
            string target=AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');string args="--apply-update "+Process.GetCurrentProcess().Id+" \""+target+"\" \""+package+"\"";
            Process.Start(new ProcessStartInfo(Path.Combine(helper,"YandexDiskRescue.exe"),args){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});
        }
        internal static void Install(string package,string target){
            target=Path.GetFullPath(target);string backup=Path.Combine(Path.GetDirectoryName(package),"backup");Directory.CreateDirectory(backup);List<string> changed=new List<string>();
            if(!File.Exists(Path.Combine(target,"YandexDiskRescue.exe")))throw new IOException("Application directory missing.");
            try{foreach(string name in Allowed){string source=Path.Combine(package,name.Replace('/','\\'));if(!File.Exists(source))continue;string destination=Path.Combine(target,name.Replace('/','\\'));for(string p=Path.GetDirectoryName(destination);p!=null&&p.Length>=target.Length;p=Path.GetDirectoryName(p))if(Directory.Exists(p)&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("Update destination contains a link.");Directory.CreateDirectory(Path.GetDirectoryName(destination));string old=Path.Combine(backup,name.Replace('/','\\'));if(File.Exists(destination)){Directory.CreateDirectory(Path.GetDirectoryName(old));File.Copy(destination,old);}string temporary=destination+".update-"+Guid.NewGuid().ToString("N");File.Copy(source,temporary);changed.Add(name);if(File.Exists(destination))File.Replace(temporary,destination,null);else File.Move(temporary,destination);}}
            catch{foreach(string name in changed.AsEnumerable().Reverse()){string destination=Path.Combine(target,name.Replace('/','\\')),old=Path.Combine(backup,name.Replace('/','\\'));if(File.Exists(old))File.Copy(old,destination,true);else if(File.Exists(destination))File.Delete(destination);}throw;}
        }
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
        static extern bool QueryFullProcessImageName(IntPtr process,int flags,StringBuilder path,ref int size);
        internal static void WaitForParent(int pid,string expected,int timeout){
            Process parent;try{parent=Process.GetProcessById(pid);}catch(ArgumentException){return;}
            using(parent){
                if(parent.HasExited)return;
                StringBuilder path=new StringBuilder(32768);int size=path.Capacity;
                // MainModule reads the other process's module list and can fail while
                // the CLR is shutting down. Query only its executable image instead.
                try{if(!QueryFullProcessImageName(parent.Handle,0,path,ref size)){
                    int code=Marshal.GetLastWin32Error();if(parent.HasExited)return;throw new Win32Exception(code);
                }}catch(Win32Exception){if(parent.HasExited)return;throw;}catch(InvalidOperationException){if(parent.HasExited)return;throw;}
                if(!String.Equals(path.ToString(),expected,StringComparison.OrdinalIgnoreCase))throw new IOException("Updater process mismatch.");
                if(!parent.WaitForExit(timeout))throw new IOException("Application is still running.");
            }
        }
        internal static int Apply(string[] args){
            if(args.Length!=4)return 2;string target=Path.GetFullPath(args[2]),package=Path.GetFullPath(args[3]);
            string stage="wait";
            try{WaitForParent(int.Parse(args[1]),Path.Combine(target,"YandexDiskRescue.exe"),120000);stage="install";Install(package,target);stage="restart";Process.Start(new ProcessStartInfo(Path.Combine(target,"YandexDiskRescue.exe")){UseShellExecute=false,WorkingDirectory=target});return 0;}
            catch(Exception ex){Win32Exception native=ex as Win32Exception;string code=native==null?"0x"+ex.HResult.ToString("X8"):native.NativeErrorCode.ToString();MessageBox.Show((stage=="restart"?"Обновление установлено. Запустите приложение вручную.":"Не удалось установить обновление.")+"\n"+stage+" · "+ex.GetType().Name+" · "+code,"Yandex Disk Rescue");return 1;}
        }
    }
}
