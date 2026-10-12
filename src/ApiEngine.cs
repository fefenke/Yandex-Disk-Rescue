using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace DiskRescue {
    public sealed class ApiEngine {
        const string Endpoint="https://cloud-api.yandex.net/v1/disk";
        readonly string data;
        readonly Action<Snapshot> publish;
        readonly CancellationTokenSource stop=new CancellationTokenSource();
        readonly List<DiskFile> files=new List<DiskFile>();
        readonly HashSet<string> remotePaths=new HashSet<string>(StringComparer.Ordinal);
        readonly SemaphoreSlim apiGate=new SemaphoreSlim(1,1);
        DateTime lastPublish=DateTime.MinValue;
        string lastPhase="";
        readonly List<string> events=new List<string>();
        readonly HashSet<string> downloaded=new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> errors=new HashSet<string>(StringComparer.Ordinal);
        readonly List<Job> jobs=new List<Job>();
        readonly HttpMessageHandler apiHandler, fileHandler;
        readonly Stopwatch clock=new Stopwatch();
        HistoryStore history;
        HttpClient client;
        HttpClient downloadClient;
        Settings settings;
        int selected,processed,skipped,scanErrors;
        string report="",current="";
        RunMode runMode;
        bool queueReady;
        sealed class Job { public DiskFile File; public Task<Result> Task; public long Bytes; }
        sealed class Result { public string Status,Message; public long Bytes; }
        public ApiEngine(string data,Action<Snapshot> publish){this.data=data;this.publish=publish;}
        internal ApiEngine(string data,Action<Snapshot> publish,HttpMessageHandler apiHandler,HttpMessageHandler fileHandler):this(data,publish){this.apiHandler=apiHandler;this.fileHandler=fileHandler;}
        public void Stop(){stop.Cancel();}
        static string Text(IDictionary<string,object> o,string key){object v;return o.TryGetValue(key,out v)?Convert.ToString(v):"";}
        static long Number(IDictionary<string,object> o,string key){object v;return o.TryGetValue(key,out v)?Convert.ToInt64(v):0;}
        void Event(string message){events.Add(DateTime.Now.ToString("HH:mm:ss")+"  "+message);if(events.Count>160)events.RemoveAt(0);}
        public static string NormalizeRemote(string path){path=(path??"").Trim().Replace('\\','/');if(path=="")return "disk:/";if(!path.StartsWith("disk:/",StringComparison.Ordinal))path="disk:/"+path.TrimStart('/');if(path.Split('/').Any(x=>x==".."||x=="."))throw new ArgumentException("Invalid cloud folder path.");return path;}
        public static string Validation(Settings s){if(!s.UseApi)return String.IsNullOrWhiteSpace(s.Folder)?"folder":"";if(String.IsNullOrWhiteSpace(s.ApiToken))return "token";if(String.IsNullOrWhiteSpace(s.ApiFolder))return "destination";return "";}
        static string Suffix(string value){using(SHA256 sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").Substring(0,8);}
        public static string LocalPath(string root,string remote){
            if(!remote.StartsWith("disk:/",StringComparison.Ordinal))throw new IOException("Unsupported remote path.");
            List<string> parts=new List<string>();
            foreach(string original in remote.Substring(6).Split('/')){
                if(original==""||original=="."||original=="..")throw new IOException("Unsafe remote path.");
                string safe=new string(original.Select(c=>Array.IndexOf(Path.GetInvalidFileNameChars(),c)>=0?'_':c).ToArray()).TrimEnd(' ','.');
                string stem=safe.Split('.')[0].ToUpperInvariant();
                if(new[]{"CON","PRN","AUX","NUL","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9","LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"}.Contains(stem))safe="_"+safe;
                if(safe!=original||safe=="")safe+="_"+Suffix(original);
                parts.Add(safe);
            }
            string fullRoot=Path.GetFullPath(root).TrimEnd('\\')+'\\';
            string path=Path.GetFullPath(Path.Combine(fullRoot,Path.Combine(parts.ToArray())));
            if(!path.StartsWith(fullRoot,StringComparison.OrdinalIgnoreCase))throw new IOException("Destination outside download folder.");
            return path;
        }
        async Task<Dictionary<string,object>> Json(string route,CancellationToken cancellation){
            await apiGate.WaitAsync(cancellation).ConfigureAwait(false);try{await Task.Delay(50,cancellation).ConfigureAwait(false);}finally{apiGate.Release();}
            using(HttpRequestMessage request=new HttpRequestMessage(HttpMethod.Get,Endpoint+route)){
                request.Headers.Authorization=new AuthenticationHeaderValue("OAuth",settings.ApiToken);
                using(HttpResponseMessage response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellation).ConfigureAwait(false)){
                    using(CancellationTokenRegistration abort=cancellation.Register(delegate{response.Dispose();})){
                    if(!response.IsSuccessStatusCode)throw new ApiException((int)response.StatusCode);
                    using(Stream stream=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(MemoryStream buffer=new MemoryStream()){
                        byte[] bytes=new byte[8192];int n;while((n=await stream.ReadAsync(bytes,0,bytes.Length,cancellation).ConfigureAwait(false))>0){buffer.Write(bytes,0,n);if(buffer.Length>32*1024*1024)throw new IOException("API response too large.");}
                        return new JavaScriptSerializer{MaxJsonLength=32*1024*1024}.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(buffer.ToArray()));
                    }
                    }
                }
            }
        }
        Dictionary<string,object> Listing(string route){using(CancellationTokenSource timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token)){timeout.CancelAfter(TimeSpan.FromSeconds(30));return Json(route,timeout.Token).GetAwaiter().GetResult();}}
        void Scan(string root,string cachePath,string account,InventoryCache resume){
            Stack<string> folders=new Stack<string>();if(resume==null)folders.Push(root);else{foreach(string pending in (resume.PendingFolders??new List<string>()).AsEnumerable().Reverse())folders.Push(pending);if(!String.IsNullOrEmpty(resume.ActiveFolder))folders.Push(resume.ActiveFolder);}
            HashSet<string> seen=new HashSet<string>(StringComparer.Ordinal);
            while(folders.Count>0){stop.Token.ThrowIfCancellationRequested();string folder=folders.Pop();if(!seen.Add(folder))continue;
                int firstOffset=resume!=null&&folder==resume.ActiveFolder?resume.NextOffset:0;
                for(int offset=firstOffset;;offset+=1000){
                    SaveInventory(cachePath,account,root,false,folders,folder,offset);
                    current=folder;Snapshot("scanning", "",false);
                    Dictionary<string,object> resource;try{resource=Listing("/resources?path="+Uri.EscapeDataString(folder)+"&limit=1000&offset="+offset);}catch(ApiException ex){if(ex.Code==401||ex.Code==403)throw;scanErrors++;Event(folder+"  ·  Listing HTTP "+ex.Code);break;}catch(OperationCanceledException){if(stop.IsCancellationRequested)throw;scanErrors++;Event(folder+"  ·  Listing timeout.");break;}
                    if(Text(resource,"type")=="file"){Add(resource);break;}
                    object raw;Dictionary<string,object> embedded=resource.TryGetValue("_embedded",out raw)?raw as Dictionary<string,object>:null;
                    if(embedded==null)throw new IOException("Missing directory listing in API response.");
                    IEnumerable entries=embedded.TryGetValue("items",out raw)?raw as IEnumerable:null;if(entries==null)throw new IOException("Missing API items.");
                    int count=0;foreach(object item in entries){Dictionary<string,object> entry=item as Dictionary<string,object>;if(entry==null)throw new IOException("Invalid API item.");count++;if(Text(entry,"type")=="dir")folders.Push(Text(entry,"path"));else if(Text(entry,"type")=="file")Add(entry);}
                    SaveInventory(cachePath,account,root,false,folders,count<1000?null:folder,count<1000?0:offset+1000);
                    if(count<1000)break;
                }
            }
            AssignDestinations();
            SaveInventory(cachePath,account,root,true,folders,null,0);
        }
        void AssignDestinations(){
            HashSet<string> destinations=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(DiskFile f in files.OrderBy(x=>x.Path,StringComparer.Ordinal)){
                f.Destination=LocalPath(settings.ApiFolder,f.Path);
                if(!destinations.Add(f.Destination)){string ext=Path.GetExtension(f.Destination);f.Destination=Path.Combine(Path.GetDirectoryName(f.Destination),Path.GetFileNameWithoutExtension(f.Destination)+"_"+Suffix(f.Path)+ext);if(!destinations.Add(f.Destination))throw new IOException("Conflicting file destinations.");}
                f.Folder=Path.GetDirectoryName(f.Destination);
            }
        }
        public sealed class InventoryCache {
            public int Version=2, ScanErrors,NextOffset;
            public bool Complete=true;
            public string ActiveFolder;
            public List<string> PendingFolders;
            public string Account,Root,CreatedUtc;
            public List<DiskFile> Files;
        }
        void SaveInventory(string path,string account,string root,bool complete,Stack<string> pending,string active,int offset){
            InventoryCache value=new InventoryCache{Account=account,Root=root,CreatedUtc=DateTime.UtcNow.ToString("o"),Files=files,ScanErrors=scanErrors,Complete=complete,PendingFolders=pending.ToList(),ActiveFolder=active,NextOffset=offset};
            string temporary=path+".tmp";File.WriteAllText(temporary,new JavaScriptSerializer{MaxJsonLength=128*1024*1024}.Serialize(value),new UTF8Encoding(false));if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
        }
        void Inventory(string account,string remote){
            string directory=Path.Combine(data,"api-inventory",HistoryStore.IdentityKey(account+"|"+remote));Directory.CreateDirectory(directory);string path=Path.Combine(directory,"inventory.json");
            JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=128*1024*1024};
            if(settings.UseCachedApiList&&File.Exists(path)){
                InventoryCache cache;try{cache=json.Deserialize<InventoryCache>(File.ReadAllText(path,Encoding.UTF8));}catch{throw new IOException("Saved API list is unreadable. Choose Refresh list from Disk.");}
                if(cache==null||(cache.Version!=1&&cache.Version!=2)||cache.Account!=account||cache.Root!=remote||cache.Files==null||cache.NextOffset<0)throw new IOException("Saved API list does not match the selected account and cloud folder.");
                foreach(DiskFile file in cache.Files){stop.Token.ThrowIfCancellationRequested();if(file==null||String.IsNullOrEmpty(file.Path)||file.Length<0)throw new IOException("Saved API list is invalid. Refresh the list.");file.Status="pending";file.Message="";file.Local=false;file.DownloadedBytes=null;if(remotePaths.Add(file.Path))files.Add(file);}
                scanErrors=cache.ScanErrors;if(!cache.Complete){Event("Resuming API scan from saved position: "+files.Count+" files already found.");Scan(remote,path,account,cache);}AssignDestinations();Event("Using saved API list: "+files.Count+" files. New or changed cloud files require refreshing the list.");if(scanErrors>0)Event("Saved list is incomplete: "+scanErrors+" listing errors.");return;
            }
            if(settings.UseCachedApiList)Event("No saved API list for this account and folder. Scanning first.");
            Scan(remote,path,account,null);stop.Token.ThrowIfCancellationRequested();
            Event("Saved API list: "+files.Count+" files.");
        }
        void Add(Dictionary<string,object> entry){string path=Text(entry,"path");if(!remotePaths.Add(path))return;string destination=LocalPath(settings.ApiFolder,path);files.Add(new DiskFile{Path=path,Destination=destination,Folder=Path.GetDirectoryName(destination),Length=Number(entry,"size"),Md5=Text(entry,"md5"),Modified=Text(entry,"modified")+"|"+Text(entry,"md5"),Category=FileRules.Category(path)});}
        public static bool Verified(string path,long length,string md5,CancellationToken cancellation){
            FileInfo info=new FileInfo(path);if(!info.Exists||info.Length!=length)return false;if(String.IsNullOrEmpty(md5))return true;
            using(MD5 hash=MD5.Create())using(FileStream stream=File.OpenRead(path)){byte[] buffer=new byte[1024*1024];int n;while((n=stream.Read(buffer,0,buffer.Length))>0){cancellation.ThrowIfCancellationRequested();hash.TransformBlock(buffer,0,n,buffer,0);}hash.TransformFinalBlock(buffer,0,0);return String.Equals(BitConverter.ToString(hash.Hash).Replace("-",""),md5,StringComparison.OrdinalIgnoreCase);}
        }
        static void SafeDestination(string path){
            for(string current=path;!String.IsNullOrEmpty(current);current=Path.GetDirectoryName(current))if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new IOException("Download destination contains a link or cloud placeholder.");
        }
        async Task<Result> Download(Job job){
            string partial=job.File.Destination+".ydr-part";
            using(CancellationTokenSource idle=CancellationTokenSource.CreateLinkedTokenSource(stop.Token)){
                try{
                    SafeDestination(partial);SafeDestination(job.File.Destination);Directory.CreateDirectory(Path.GetDirectoryName(partial));idle.CancelAfter(TimeSpan.FromSeconds(settings.IdleSeconds));
                    Dictionary<string,object> link=await Json("/resources/download?path="+Uri.EscapeDataString(job.File.Path),idle.Token).ConfigureAwait(false);
                    Uri uri;if(!Uri.TryCreate(Text(link,"href"),UriKind.Absolute,out uri)||uri.Scheme!="https"||!String.IsNullOrEmpty(uri.UserInfo))throw new IOException("Invalid HTTPS download link.");
                    // No OAuth header is sent to the signed download URL.
                    using(HttpResponseMessage response=await downloadClient.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,idle.Token).ConfigureAwait(false))using(CancellationTokenRegistration abort=idle.Token.Register(delegate{response.Dispose();})){
                        if(!response.IsSuccessStatusCode)throw new ApiException((int)response.StatusCode);
                        idle.CancelAfter(TimeSpan.FromSeconds(settings.IdleSeconds));
                        using(Stream input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(FileStream output=new FileStream(partial,FileMode.Create,FileAccess.Write,FileShare.Read,65536,true)){
                            byte[] buffer=new byte[65536];int n;
                            while((n=await input.ReadAsync(buffer,0,buffer.Length,idle.Token).ConfigureAwait(false))>0){idle.CancelAfter(TimeSpan.FromSeconds(settings.IdleSeconds));await output.WriteAsync(buffer,0,n,stop.Token).ConfigureAwait(false);Interlocked.Add(ref job.Bytes,n);}
                        }
                    }
                    idle.CancelAfter(Timeout.Infinite);
                    if(!Verified(partial,job.File.Length,job.File.Md5,stop.Token))return new Result{Status="failed",Message="File size or MD5 mismatch.",Bytes=job.Bytes};
                    stop.Token.ThrowIfCancellationRequested();
                    if(File.Exists(job.File.Destination))File.Replace(partial,job.File.Destination,null);else File.Move(partial,job.File.Destination);
                    return new Result{Status="success",Message="",Bytes=job.Bytes};
                }catch(OperationCanceledException){return new Result{Status=stop.IsCancellationRequested?"cancelled_user":"timeout",Message=stop.IsCancellationRequested?"Stopped by user.":"No progress during the idle timeout.",Bytes=job.Bytes};}
                catch(ApiException ex){return new Result{Status=ex.Code>=500?"cloud_error":"failed",Message="HTTP "+ex.Code,Bytes=job.Bytes};}
                catch(Exception){return new Result{Status=stop.IsCancellationRequested?"cancelled_user":idle.IsCancellationRequested?"timeout":"failed",Message=stop.IsCancellationRequested?"Stopped by user.":idle.IsCancellationRequested?"No progress during the idle timeout.":"Network or local file operation failed.",Bytes=job.Bytes};}
            }
        }
        public void Run(Settings options,RunMode mode){
            settings=options;runMode=mode;clock.Start();string phase="completed",error="";
            try{
                string missing=Validation(settings);if(missing!="")throw new IOException("Missing required API field: "+missing);
                string destination=Path.GetFullPath(settings.ApiFolder);settings.ApiFolder=destination;Directory.CreateDirectory(destination);
                if(File.GetAttributes(destination).HasFlag(FileAttributes.ReparsePoint))throw new IOException("Choose an ordinary local download folder, not a cloud placeholder or link.");
                ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
                ServicePointManager.DefaultConnectionLimit=Math.Max(10,ServicePointManager.DefaultConnectionLimit);
                // Automatic redirects are disabled so authorization never crosses hosts.
                client=new HttpClient(apiHandler??new HttpClientHandler{AllowAutoRedirect=false}){Timeout=Timeout.InfiniteTimeSpan};
                downloadClient=new HttpClient(fileHandler??new HttpClientHandler{AllowAutoRedirect=true}){Timeout=Timeout.InfiniteTimeSpan};
                Snapshot("preparing","",false);Dictionary<string,object> disk=Listing("");object raw;Dictionary<string,object> user=disk.TryGetValue("user",out raw)?raw as Dictionary<string,object>:null;
                string account=user==null?"":Text(user,"uid");if(account=="")throw new IOException("API did not return an account identifier.");
                string remote=NormalizeRemote(settings.RemoteFolder);
                history=new HistoryStore(data,destination,account+"|"+remote+"|"+destination.ToUpperInvariant());
                report=Path.Combine(history.DirectoryPath,"reports",DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")+"_"+Guid.NewGuid().ToString("N").Substring(0,6));
                Inventory(account,remote);
                foreach(DiskFile f in files){stop.Token.ThrowIfCancellationRequested();current=f.Path;HistoryRecord old=history.Find(f.Path);bool same=FileRules.SameVersion(f,old);if(same){f.Status=FileRules.NormalizeStatus(old);f.DownloadedBytes=old.downloadedBytes;}
                    SafeDestination(f.Destination);f.Local=Verified(f.Destination,f.Length,f.Md5,stop.Token);if(f.Local){f.DownloadedBytes=f.Length;history.SaveApi(f,"local","");}else if(FileRules.Success(f.Status))f.Status="pending";
                    string partial=f.Destination+".ydr-part";if(File.Exists(partial))f.DownloadedBytes=new FileInfo(partial).Length;
                    Snapshot("preparing","",false);
                }
                List<DiskFile> queue=FileRules.Order(files.Where(f=>mode==RunMode.Continue ? f.Status=="pending"||f.Status=="cancelled_user" : FileRules.Select(f,history.Find(f.Path),mode)));selected=queue.Count;skipped=files.Count-selected;queueReady=true;int next=0;Stopwatch reportClock=Stopwatch.StartNew();
                while(next<queue.Count||jobs.Count>0){
                    while(!stop.IsCancellationRequested&&jobs.Count<5&&next<queue.Count){DiskFile f=queue[next++];if(f.Local){processed++;continue;}Job job=new Job{File=f};jobs.Add(job);job.Task=Download(job);}
                    foreach(Job job in jobs.ToArray())if(job.Task.IsCompleted){Result result=job.Task.GetAwaiter().GetResult();job.File.DownloadedBytes=result.Bytes;history.SaveApi(job.File,result.Status,result.Message);processed++;if(result.Status=="success")downloaded.Add(job.File.Path);if(result.Status=="cloud_error")errors.Add(job.File.Path);Event(job.File.Path+"  ·  "+result.Status+(result.Message==""?"":"  ·  "+result.Message));jobs.Remove(job);}
                    Snapshot(stop.IsCancellationRequested?"stopping":"running","",false);
                    if(reportClock.Elapsed.TotalSeconds>=120){Reports.Write(report,destination,files,latest);reportClock.Restart();}
                    if(stop.IsCancellationRequested&&jobs.Count==0){phase="stopped";break;}Thread.Sleep(100);
                }
            }catch(OperationCanceledException){phase=stop.IsCancellationRequested?"stopped":"error";error=stop.IsCancellationRequested?"":"API listing request timed out.";}
            catch(ApiException ex){phase="error";error=ex.Code==401?"OAuth token is invalid or expired.":ex.Code==403?"Token has no access to these files.":"Yandex API returned HTTP "+ex.Code;Event(error);}
            catch(Exception ex){phase=stop.IsCancellationRequested?"stopped":"error";error=stop.IsCancellationRequested?"":ex is IOException||ex is ArgumentException?ex.Message:"API request failed or timed out.";Event(error);}
            finally{stop.Cancel();foreach(Job job in jobs)try{Result r=job.Task.GetAwaiter().GetResult();job.File.DownloadedBytes=r.Bytes;history.SaveApi(job.File,r.Status,r.Message);}catch{}jobs.Clear();clock.Stop();Snapshot(phase,error,true);if(history!=null){try{Reports.Write(report,settings.ApiFolder,files,latest);}catch(Exception reportError){string reportMessage="Could not save reports: "+reportError.GetType().Name;Event(reportMessage);Snapshot("error",String.IsNullOrEmpty(error)?reportMessage:error+"\n"+reportMessage,true);}history.Dispose();}if(client!=null)client.Dispose();if(downloadClient!=null)downloadClient.Dispose();stop.Dispose();}
        }
        Snapshot latest;
        void Snapshot(string phase,string error,bool finished){
            if(!finished&&lastPhase==phase&&(DateTime.UtcNow-lastPublish).TotalMilliseconds<400)return;lastPhase=phase;lastPublish=DateTime.UtcNow;
            Snapshot s=new Snapshot{Phase=phase,Error=error,Finished=finished,Total=files.Count,Selected=selected,Processed=processed,Skipped=skipped,ScanErrors=scanErrors,Report=report,Current=current,Seconds=clock.Elapsed.TotalSeconds,Active=jobs.Count,Downloaded=downloaded.Count,CloudErrorsThisPass=errors.Count};
            foreach(string category in new[]{"photos","videos","documents","archives","audio","other"}){List<DiskFile> items=files.Where(f=>f.Category==category).ToList();s.Categories.Add(new CategoryStats{Category=category,Total=items.Count,Available=items.Count(f=>FileRules.Success(f.Status)),Downloaded=items.Count(f=>downloaded.Contains(f.Path)),Remaining=items.Count(f=>!FileRules.Success(f.Status)),ZeroDownload=items.Count(FileRules.CancelledZero),CloudErrors=items.Count(f=>f.Status=="cloud_error"),Deferred=items.Count(f=>f.Status=="timeout"||f.Status=="failed"||f.Status=="cancelled_user")});}
            s.Available=s.Categories.Sum(c=>c.Available);s.CloudErrors=s.Categories.Sum(c=>c.CloudErrors);s.Deferred=s.Categories.Sum(c=>c.Deferred);s.PerHour=s.Seconds>0?processed*3600.0/s.Seconds:0;s.RemainingSeconds=s.PerHour>0?Math.Max(0,selected-processed)*3600.0/s.PerHour:0;s.Events=events.ToList();
            if(queueReady){s.ProgressTotal=runMode==RunMode.Continue?files.Count:selected;s.ProgressChecked=runMode==RunMode.Continue?skipped+processed:processed;}latest=s;publish(s);
        }
        sealed class ApiException:Exception {public readonly int Code;public ApiException(int code){Code=code;}}
    }
}
