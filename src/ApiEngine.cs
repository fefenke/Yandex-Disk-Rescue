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
    public sealed partial class ApiEngine {
        const string Endpoint="https://cloud-api.yandex.net/v1/disk";
        readonly string data;
        readonly Action<Snapshot> publish;
        readonly CancellationTokenSource stop=new CancellationTokenSource();
        readonly List<DiskFile> files=new List<DiskFile>();
        readonly HashSet<string> occupiedDestinations=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> remotePaths=new HashSet<string>(StringComparer.Ordinal);
        internal const int LinkConcurrency=7,DownloadConcurrency=20,ReadyLinkCapacity=20;
        readonly ApiPacer apiPacer=new ApiPacer();
        readonly SignedLinkCache linkCache=new SignedLinkCache();
        DateTime lastPublish=DateTime.MinValue;
        string lastPhase="";
        DateTime catalogTime=DateTime.MinValue;List<BrowserFile> catalog;
        readonly List<string> events=new List<string>();
        readonly HashSet<string> downloaded=new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> errors=new HashSet<string>(StringComparer.Ordinal);
        readonly List<Job> jobs=new List<Job>();
        readonly HttpMessageHandler apiHandler, fileHandler;
        readonly Stopwatch clock=new Stopwatch();
        HistoryStore history;
        FilePreserver preserver;
        HttpClient client;
        HttpClient downloadClient;
        Settings settings;
        int selected,processed,skipped,scanErrors;
        string report="",current="";
        string inventoryPath="",inventoryAccount="",inventoryRoot="";int refreshedInventory;
        RunMode runMode;
        bool queueReady;
        int checkFilesChecked;long checkFileBytes,checkFileLength;DateTime checkFileStarted;
        sealed class Job { public DiskFile File; public HistoryRecord Prior; public Task<Result> Task; public Task<LinkResult> LinkTask; public Uri Link; public long Bytes; public RangeMap Map;public SignedLinkCache.Lease LinkLease; public int LinkConfirmed,LinkRejected; }
        sealed class LinkResult { public Uri Link; public Result Completed; }
        sealed class Result { public string Status,Message; public long Bytes; public bool RetryTransfer; }
        public ApiEngine(string data,Action<Snapshot> publish){this.data=data;this.publish=publish;}
        internal ApiEngine(string data,Action<Snapshot> publish,HttpMessageHandler apiHandler,HttpMessageHandler fileHandler):this(data,publish){this.apiHandler=apiHandler;this.fileHandler=fileHandler;}
        internal ApiEngine(string data,Action<Snapshot> publish,HttpMessageHandler apiHandler,HttpMessageHandler fileHandler,ApiPacer apiPacer):this(data,publish,apiHandler,fileHandler){this.apiPacer=apiPacer;}
        internal ApiEngine(string data,Action<Snapshot> publish,HttpMessageHandler apiHandler,HttpMessageHandler fileHandler,ApiPacer apiPacer,SignedLinkCache linkCache):this(data,publish,apiHandler,fileHandler,apiPacer){this.linkCache=linkCache;}
        void ConfirmLink(Job job){if(Interlocked.CompareExchange(ref job.LinkConfirmed,1,0)==0)linkCache.Confirm(job.LinkLease);}
        void InvalidateLink(Job job){linkCache.Invalidate(job.LinkLease);}
        void RejectLink(Job job,int code){InvalidateLink(job);if(code==401||code==403||code==410)Interlocked.Exchange(ref job.LinkRejected,1);}
        public void Stop(){try{stop.Cancel();}catch(ObjectDisposedException){ /* Run has already finished. */ }}
        static string Text(IDictionary<string,object> o,string key){object v;return o.TryGetValue(key,out v)?Convert.ToString(v):"";}
        static long RequiredSize(IDictionary<string,object> entry){object value;if(!entry.TryGetValue("size",out value)||!(value is int||value is long))throw new IOException("Missing or invalid cloud file size.");long length=Convert.ToInt64(value);if(length<0)throw new IOException("Invalid cloud file size.");return length;}
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
        Task<Dictionary<string,object>> Json(string route,CancellationToken cancellation,int timeoutSeconds=0){
            // An immediately admitted request must not inherit a UI synchronization
            // context from a caller that synchronously waits for this API operation.
            return SynchronizationContext.Current==null?JsonCore(route,cancellation,timeoutSeconds):Task.Run(()=>JsonCore(route,cancellation,timeoutSeconds),cancellation);
        }
        async Task<Dictionary<string,object>> JsonCore(string route,CancellationToken cancellation,int timeoutSeconds){
            int ticket=await apiPacer.WaitAsync(cancellation).ConfigureAwait(false);
            using(CancellationTokenSource requestTimeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation)){
            if(timeoutSeconds>0)requestTimeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            cancellation=requestTimeout.Token;
            try{
            using(HttpRequestMessage request=new HttpRequestMessage(HttpMethod.Get,Endpoint+route)){
                request.Headers.Authorization=new AuthenticationHeaderValue("OAuth",settings.ApiToken);
                using(HttpResponseMessage response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellation).ConfigureAwait(false)){
                    using(CancellationTokenRegistration abort=cancellation.Register(delegate{response.Dispose();})){
                    if(!response.IsSuccessStatusCode){double retryAfter=ApiPacer.RetryAfterSeconds(response);apiPacer.Failure((int)response.StatusCode,retryAfter,ticket);throw new ApiException((int)response.StatusCode,retryAfter);}
                    using(Stream stream=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(MemoryStream buffer=new MemoryStream()){
                        byte[] bytes=new byte[8192];int n;while((n=await stream.ReadAsync(bytes,0,bytes.Length,cancellation).ConfigureAwait(false))>0){buffer.Write(bytes,0,n);if(buffer.Length>32*1024*1024)throw new IOException("API response too large.");}
                        var result=new JavaScriptSerializer{MaxJsonLength=32*1024*1024}.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(buffer.ToArray()));apiPacer.Success(ticket);return result;
                    }
                    }
                }
            }
            }catch(Exception){if(requestTimeout.IsCancellationRequested)throw new OperationCanceledException(cancellation);throw;}
            }
        }
        Dictionary<string,object> Listing(string route){
            int failures=0;
            while(true){stop.Token.ThrowIfCancellationRequested();using(CancellationTokenSource timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token)){
                try{var result=Json(route,timeout.Token,30).GetAwaiter().GetResult();if(failures>0)Snapshot(route==""?"preparing":"scanning","",false);return result;}
                catch(Exception ex){
                    stop.Token.ThrowIfCancellationRequested();ApiException http=ex as ApiException;
                    bool temporary=timeout.IsCancellationRequested||ex is OperationCanceledException||ex is HttpRequestException||ex is System.Net.WebException||(http!=null&&(http.Code==408||http.Code==429||http.Code>=500));
                    if(!temporary)throw;
                    double delay=failures==0?0:failures==1?1:failures==2?2:5;
                    if(http!=null)delay=Math.Max(delay,http.RetryAfterSeconds);failures++;
                    Event("Temporary API connection/listing failure; retry "+failures+" in "+delay.ToString("0")+" s.");Snapshot("api_retry","",false);
                    if(stop.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(Math.Min(delay,86400))))stop.Token.ThrowIfCancellationRequested();
                }
            }}
        }
        void Scan(string root,string cachePath,string account,InventoryCache resume){
            Stack<string> folders=new Stack<string>();if(resume==null)folders.Push(root);else{foreach(string pending in (resume.PendingFolders??new List<string>()).AsEnumerable().Reverse())folders.Push(pending);if(!String.IsNullOrEmpty(resume.ActiveFolder))folders.Push(resume.ActiveFolder);}
            HashSet<string> seen=new HashSet<string>(StringComparer.Ordinal);
            while(folders.Count>0){stop.Token.ThrowIfCancellationRequested();string folder=folders.Pop();if(!seen.Add(folder))continue;
                // Offsets are positions, not stable cursors. Restart the interrupted
                // folder; remotePaths retains discovered entries and removes repeats.
                for(int offset=0;;offset+=1000){
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
            AssignDestinations(files,settings.ApiFolder);
        }
        internal static void AssignDestinations(List<DiskFile> files,string root){
            HashSet<string> destinations=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(DiskFile f in files.OrderBy(x=>x.Path,StringComparer.Ordinal)){
                f.Destination=LocalPath(root,f.Path);
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
            DurablePartial.AtomicText(path,new JavaScriptSerializer{MaxJsonLength=128*1024*1024}.Serialize(value));
        }
        void Inventory(string account,string remote){
            string directory=Path.Combine(data,"api-inventory",HistoryStore.IdentityKey(account+"|"+remote));Directory.CreateDirectory(directory);string path=Path.Combine(directory,"inventory.json");
            inventoryPath=path;inventoryAccount=account;inventoryRoot=remote;
            JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=128*1024*1024};
            if(settings.UseCachedApiList&&File.Exists(path)){
                InventoryCache cache;try{stop.Token.ThrowIfCancellationRequested();string saved=File.ReadAllText(path,Encoding.UTF8);stop.Token.ThrowIfCancellationRequested();cache=json.Deserialize<InventoryCache>(saved);stop.Token.ThrowIfCancellationRequested();}catch(OperationCanceledException){throw;}catch{throw new IOException("Saved API list is unreadable. Choose Refresh list from Disk.");}
                if(cache==null||(cache.Version!=1&&cache.Version!=2)||cache.Account!=account||cache.Root!=remote||cache.Files==null||cache.NextOffset<0)throw new IOException("Saved API list does not match the selected account and cloud folder.");
                foreach(DiskFile file in cache.Files){stop.Token.ThrowIfCancellationRequested();if(file==null||String.IsNullOrEmpty(file.Path)||file.Length<0)throw new IOException("Saved API list is invalid. Refresh the list.");file.Status="pending";file.Message="";file.Local=false;file.DownloadedBytes=null;if(remotePaths.Add(file.Path))files.Add(file);}
                scanErrors=cache.ScanErrors;if(!cache.Complete){Event("Resuming API scan from saved position: "+files.Count+" files already found.");Scan(remote,path,account,cache);}AssignDestinations();Event("Using saved API list: "+files.Count+" files. New or changed cloud files require refreshing the list.");if(scanErrors>0)Event("Saved list is incomplete: "+scanErrors+" listing errors.");return;
            }
            if(settings.UseCachedApiList)Event("No saved API list for this account and folder. Scanning first.");
            Scan(remote,path,account,null);stop.Token.ThrowIfCancellationRequested();
            Event("Saved API list: "+files.Count+" files.");
        }
        static string CloudModified(string modified,string md5,string sha){return modified+"|"+md5+(String.IsNullOrEmpty(md5)&&!String.IsNullOrEmpty(sha)?"|sha256:"+sha:"");}
        void Add(Dictionary<string,object> entry){string path=Text(entry,"path");if(!remotePaths.Add(path))return;string destination=LocalPath(settings.ApiFolder,path);files.Add(new DiskFile{Path=path,Destination=destination,Folder=Path.GetDirectoryName(destination),Length=RequiredSize(entry),SizeKnown=true,Md5=Text(entry,"md5"),Sha256=Text(entry,"sha256"),Modified=CloudModified(Text(entry,"modified"),Text(entry,"md5"),Text(entry,"sha256")),Category=FileRules.Category(path)});}
        bool CanRecoverArchived(DiskFile file){
            if(ArtifactCollision(file)||preserver.Blocked(file))return false;
            try{preserver.CheckDestination(file.Destination);return true;}catch(IOException){return false;}
        }
        Result FinishPayload(Job job,string partial){
            stop.Token.ThrowIfCancellationRequested();IntegrityResult check=CloudIntegrity.Check(partial,job.File,stop.Token);job.File.Verification=check.Kind;job.File.ActualHash=check.Actual;
            bool accepted=preserver.Complete(job.File,partial,check);
            DurablePartial.Forget(partial);ClearSegments(job.File);File.Delete(Path.Combine(history.DirectoryPath,"partials",HistoryStore.IdentityKey(job.File.Path)+".txt"));
            if(job.Map!=null)File.Delete(job.File.Destination+".ydr-map");
            if(!accepted)InvalidateLink(job);
            return new Result{Status=accepted?"success":"integrity_failed",Bytes=accepted?job.File.Length:0,Message=accepted?(check.Kind=="size_only"?"Downloaded; cloud checksum unavailable.":"Cloud checksum verified."):"Cloud checksum mismatch; copy preserved. Automatic retries for this cloud version are blocked."};
        }
        public static bool Verified(string path,long length,string md5,CancellationToken cancellation){
            return Verified(path,length,md5,cancellation,null);
        }
        internal static bool Verified(string path,long length,string md5,CancellationToken cancellation,Action<long,long> progress){
            FileInfo info=new FileInfo(path);if(!info.Exists||info.Length!=length)return false;if(String.IsNullOrEmpty(md5))return true;
            using(MD5 hash=MD5.Create())using(FileStream stream=File.OpenRead(path)){byte[] buffer=new byte[1024*1024];int n;long done=0;if(progress!=null)progress(0,length);while(true){cancellation.ThrowIfCancellationRequested();n=stream.Read(buffer,0,buffer.Length);if(n==0)break;hash.TransformBlock(buffer,0,n,buffer,0);done+=n;if(progress!=null)progress(done,length);}hash.TransformFinalBlock(buffer,0,0);return String.Equals(BitConverter.ToString(hash.Hash).Replace("-",""),md5,StringComparison.OrdinalIgnoreCase);}
        }
        public static bool PreviouslyVerified(DiskFile file,HistoryRecord record){
            if(!FileRules.SameVersion(file,record)||!FileRules.Success(record.status)||!record.localWriteTicks.HasValue)return false;
            FileInfo local=new FileInfo(file.Destination);return local.Exists&&local.Length==file.Length&&local.LastWriteTimeUtc.Ticks==record.localWriteTicks.Value;
        }
        static void SafeDestination(string path){
            for(string current=path;!String.IsNullOrEmpty(current);current=Path.GetDirectoryName(current))if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new IOException("Download destination contains a link or cloud placeholder.");
        }
        bool ArtifactCollision(DiskFile f){
            string[] reserved={f.Destination+".ydr-download",f.Destination+".ydr-map",f.Destination+".ydr-map.tmp",f.Destination+".ydr-part",f.Destination+".ydr-part.ydr-state",f.Destination+".ydr-part.ydr-state.tmp",SegmentFolder(f)};
            return reserved.Any(p=>occupiedDestinations.Contains(p));
        }
        async Task<LinkResult> PrepareLink(Job job){
            string partial=job.File.Destination+".ydr-part";
            string metadata=Path.Combine(history.DirectoryPath,"partials",HistoryStore.IdentityKey(job.File.Path)+".txt");
            long offset=0;bool checkingMetadata=false;
            try{stop.Token.ThrowIfCancellationRequested();
                    if(ArtifactCollision(job.File))return new LinkResult{Completed=new Result{Status="failed",Message="Temporary filename conflicts with another cloud file; originals retained."}};
                    preserver.CheckDestination(job.File.Destination);SafeDestination(partial);SafeDestination(job.File.Destination);Directory.CreateDirectory(Path.GetDirectoryName(partial));
                    SafeDestination(SegmentFolder(job.File));
                    if((job.File.Length==0&&!job.File.SizeKnown)||FileRules.HasPartialData(job.File.Destination)||RangeMap.Exists(job.File.Destination)||File.Exists(job.File.Destination)){
                        SegmentPlan existing=ReadSegmentPlan(job.File);
                        job.Bytes=existing==null?(File.Exists(partial)?Math.Min(new FileInfo(partial).Length,job.File.Length):0):existing.Offset+SegmentBytes(job.File,existing);
                        checkingMetadata=true;
                        var remote=await Json("/resources?path="+Uri.EscapeDataString(job.File.Path)+"&fields=path,type,size,modified,md5,sha256",stop.Token,settings.LinkTimeoutSeconds).ConfigureAwait(false);
                        string digest=Text(remote,"md5"),sha=Text(remote,"sha256"),modified=Text(remote,"modified");CloudIntegrity.Validate(digest,32);CloudIntegrity.Validate(sha,64);
                        long remoteSize=RequiredSize(remote);
                        if(Text(remote,"type")!="file"||Text(remote,"path")!=job.File.Path||remoteSize<0||String.IsNullOrEmpty(modified)||(CloudIntegrity.HasHash(job.File)&&String.IsNullOrEmpty(digest)&&String.IsNullOrEmpty(sha)))throw new IOException("Cloud metadata cannot safely identify the file version.");
                        stop.Token.ThrowIfCancellationRequested();
                        if(!job.File.SizeKnown||job.File.Length!=remoteSize||job.File.Modified!=CloudModified(modified,digest,sha)||job.File.Sha256!=sha)Interlocked.Exchange(ref refreshedInventory,1);
                        job.File.SizeKnown=true;job.File.Length=remoteSize;job.File.Md5=digest;job.File.Sha256=sha;job.File.Modified=CloudModified(modified,digest,sha);
                        job.File.ResumeEligible=FileRules.SameVersion(job.File,job.Prior)&&CloudIntegrity.HasHash(job.File);
                        checkingMetadata=false;
                        if((runMode!=RunMode.All&&job.File.Verification!="hash_mismatch"&&PreviouslyVerified(job.File,job.Prior))||CloudIntegrity.CheckAndRecord(job.File.Destination,job.File,stop.Token)){
                            job.File.Local=true;return new LinkResult{Completed=new Result{Status="local",Bytes=job.File.Length,Message="Local file matches refreshed cloud metadata."}};
                        }
                    }
                    if(PrepareMapped(job)){if(job.Map.Bytes==job.File.Length)return new LinkResult{Completed=FinishPayload(job,job.File.Destination+".ydr-download")};}
                    else {
                    string identity=SegmentIdentity(job.File);
                    bool samePartial=(File.Exists(metadata)&&File.ReadAllText(metadata)==identity)||(!File.Exists(metadata)&&job.File.ResumeEligible);
                    if(!samePartial)ClearSegments(job.File);
                    SegmentPlan plan=ReadSegmentPlan(job.File);
                    // An interrupted merge is not a downloaded prefix. Roll it
                    // back before importing/checking any legacy checkpoint data.
                    if(plan!=null&&File.Exists(partial)&&new FileInfo(partial).Length>plan.Offset)using(FileStream rollback=new FileStream(partial,FileMode.Open,FileAccess.Write)){rollback.SetLength(plan.Offset);rollback.Flush(true);}
                    if(File.Exists(partial)&&samePartial){offset=DurablePartial.Recover(partial,identity,!String.IsNullOrEmpty(job.File.Md5),stop.Token);if(offset>job.File.Length)offset=0;}
                    if(plan!=null&&(!samePartial||offset!=plan.Offset)){ClearSegments(job.File);plan=null;}
                    else if(plan==null&&Directory.Exists(SegmentFolder(job.File)))ClearSegments(job.File);
                    if(!samePartial||!File.Exists(partial)||new FileInfo(partial).Length>job.File.Length)DurablePartial.Reset(partial,identity);
                    Directory.CreateDirectory(Path.GetDirectoryName(metadata));DurablePartial.AtomicText(metadata,identity);
                    if(offset==job.File.Length&&offset>0)return new LinkResult{Completed=FinishPayload(job,partial)};
                    job.Bytes=offset;
                    }
                string cacheKey=history.DirectoryPath+"\n"+job.File.Path;
                job.LinkLease=linkCache.Get(cacheKey,SegmentIdentity(job.File));
                if(job.LinkLease!=null)return new LinkResult{Link=job.LinkLease.Link};
                Dictionary<string,object> link=await Json("/resources/download?path="+Uri.EscapeDataString(job.File.Path),stop.Token,settings.LinkTimeoutSeconds).ConfigureAwait(false);
                Uri uri;if(!Uri.TryCreate(Text(link,"href"),UriKind.Absolute,out uri)||uri.Scheme!="https"||!String.IsNullOrEmpty(uri.UserInfo))throw new IOException("Invalid HTTPS download link.");
                job.LinkLease=linkCache.Offer(cacheKey,SegmentIdentity(job.File),uri);
                return new LinkResult{Link=uri};
            }catch(OperationCanceledException){return new LinkResult{Completed=new Result{Status=stop.IsCancellationRequested?"cancelled_user":"timeout",Message=stop.IsCancellationRequested?"Stopped by user.":checkingMetadata?"Cloud metadata request timed out; saved data retained.":"Download link request timed out.",Bytes=job.Bytes}};}
            catch(ApiException ex){return new LinkResult{Completed=new Result{Status=ex.Code>=500?"cloud_error":"failed",Message=(checkingMetadata?"Metadata HTTP ":"Link HTTP ")+ex.Code,Bytes=job.Bytes}};}
            catch(Exception ex){return new LinkResult{Completed=new Result{Status=stop.IsCancellationRequested?"cancelled_user":"failed",Message=stop.IsCancellationRequested?"Stopped by user.":(checkingMetadata?"Metadata validation failed; saved data retained: ":"Link preparation failed: ")+ex.GetType().Name+" (0x"+ex.HResult.ToString("X8")+")",Bytes=job.Bytes}};}
        }
        async Task<Result> DownloadSingle(Job job,int idleSeconds){
            string partial=job.File.Destination+".ydr-part";
            string metadata=Path.Combine(history.DirectoryPath,"partials",HistoryStore.IdentityKey(job.File.Path)+".txt");
            long offset=job.Bytes,received=0;
            using(CancellationTokenSource idle=CancellationTokenSource.CreateLinkedTokenSource(stop.Token)){
                try{
                    stop.Token.ThrowIfCancellationRequested();SafeDestination(partial);SafeDestination(job.File.Destination);
                    idle.CancelAfter(TimeSpan.FromSeconds(idleSeconds));
                    Uri uri=job.Link;
                    // No OAuth header is sent to the signed download URL.
                    using(HttpRequestMessage request=new HttpRequestMessage(HttpMethod.Get,uri)){
                    if(offset>0)request.Headers.Range=new RangeHeaderValue(offset,null);
                    using(HttpResponseMessage response=await downloadClient.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,idle.Token).ConfigureAwait(false))using(CancellationTokenRegistration abort=idle.Token.Register(delegate{response.Dispose();})){
                        if(!response.IsSuccessStatusCode&&response.StatusCode!=HttpStatusCode.RequestedRangeNotSatisfiable)throw new ApiException((int)response.StatusCode);
                        if(response.StatusCode==HttpStatusCode.PartialContent){ContentRangeHeaderValue range=response.Content.Headers.ContentRange;if(range==null||range.Unit!="bytes"||range.From!=offset||range.Length!=job.File.Length||range.To!=job.File.Length-1){InvalidateLink(job);throw new IOException("Invalid download byte range.");}}
                        else if(response.StatusCode==HttpStatusCode.OK){if(offset>0)DurablePartial.Reset(partial,SegmentIdentity(job.File));offset=0;job.Bytes=0;}
                        else if(response.StatusCode==HttpStatusCode.RequestedRangeNotSatisfiable){InvalidateLink(job);DurablePartial.Reset(partial,SegmentIdentity(job.File));job.Bytes=0;throw new IOException("Server rejected resume range; next pass starts from zero.");}
                        if(response.Content.Headers.ContentEncoding.Count>0){InvalidateLink(job);throw new IOException("Encoded download cannot be safely resumed.");}
                        if(!response.IsSuccessStatusCode)throw new ApiException((int)response.StatusCode);
                        idle.CancelAfter(TimeSpan.FromSeconds(idleSeconds));
                        using(Stream input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(DurablePartial output=new DurablePartial(partial,SegmentIdentity(job.File),offset)){
                            byte[] buffer=new byte[65536];int n;
                            while((n=await ReadDownloadData(input,buffer,buffer.Length,idle.Token).ConfigureAwait(false))>0){idle.CancelAfter(Timeout.Infinite);await output.WriteAsync(buffer,0,n,stop.Token).ConfigureAwait(false);received+=n;Interlocked.Add(ref job.Bytes,n);ConfirmLink(job);idle.CancelAfter(TimeSpan.FromSeconds(idleSeconds));}
                        }
                    }
                    }
                    idle.CancelAfter(Timeout.Infinite);
                    if(new FileInfo(partial).Length<job.File.Length){if(received==0)InvalidateLink(job);return new Result{Status="failed",Message="Incomplete download response; received data retained.",Bytes=job.Bytes,RetryTransfer=received>0};}
                    return FinishPayload(job,partial);
                }catch(OperationCanceledException){if(!stop.IsCancellationRequested&&received==0)InvalidateLink(job);return new Result{Status=stop.IsCancellationRequested?"cancelled_user":"timeout",Message=stop.IsCancellationRequested?"Stopped by user.":"No progress during the idle timeout.",Bytes=job.Bytes,RetryTransfer=!stop.IsCancellationRequested&&received>0};}
                catch(ApiException ex){RejectLink(job,ex.Code);return new Result{Status=ex.Code>=500?"cloud_error":"failed",Message="HTTP "+ex.Code,Bytes=job.Bytes};}
                catch(Exception ex){if(!stop.IsCancellationRequested&&received==0&&(idle.IsCancellationRequested||ex is DownloadReadException||ex is HttpRequestException))InvalidateLink(job);return new Result{Status=stop.IsCancellationRequested?"cancelled_user":idle.IsCancellationRequested?"timeout":"failed",Message=stop.IsCancellationRequested?"Stopped by user.":idle.IsCancellationRequested?"No progress during the idle timeout.":"Download failed: "+ex.GetType().Name+" (0x"+ex.HResult.ToString("X8")+")",Bytes=job.Bytes,RetryTransfer=!stop.IsCancellationRequested&&received>0&&(ex is DownloadReadException||ex is HttpRequestException)};}
            }
        }
        public void Run(Settings options,RunMode mode){
            settings=options;runMode=mode;clock.Start();string phase="completed",error="";
            apiPacer.Configure(settings.ApiRequestIntervalMs);
            try{
                string missing=Validation(settings);if(missing!="")throw new IOException("Missing required API field: "+missing);
                int linkConcurrency=Math.Max(1,Math.Min(30,settings.ApiLinkConcurrency));
                int downloadConcurrency=Math.Max(1,Math.Min(30,settings.ApiDownloadConcurrency));
                downloadSlots=new DownloadSlotQueue(downloadConcurrency);
                int waitingCapacity=Math.Max(ReadyLinkCapacity,linkConcurrency);
                string destination=Path.GetFullPath(settings.ApiFolder);settings.ApiFolder=destination;Directory.CreateDirectory(destination);
                if(File.GetAttributes(destination).HasFlag(FileAttributes.ReparsePoint))throw new IOException("Choose an ordinary local download folder, not a cloud placeholder or link.");
                ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
                ServicePointManager.DefaultConnectionLimit=Math.Max(linkConcurrency+downloadConcurrency,Math.Max(40,ServicePointManager.DefaultConnectionLimit));
                // Automatic redirects are disabled so authorization never crosses hosts.
                client=new HttpClient(apiHandler??new HttpClientHandler{AllowAutoRedirect=false}){Timeout=Timeout.InfiniteTimeSpan};
                downloadClient=new HttpClient(fileHandler??new HttpClientHandler{AllowAutoRedirect=true}){Timeout=Timeout.InfiniteTimeSpan};
                Snapshot("preparing","",false);Dictionary<string,object> disk=Listing("");object raw;Dictionary<string,object> user=disk.TryGetValue("user",out raw)?raw as Dictionary<string,object>:null;
                string account=user==null?"":Text(user,"uid");if(account=="")throw new IOException("API did not return an account identifier.");
                string remote=NormalizeRemote(settings.RemoteFolder);
                history=new HistoryStore(data,destination,account+"|"+remote+"|"+destination.ToUpperInvariant(),stop.Token);
                report=Path.Combine(history.DirectoryPath,"reports",DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")+"_"+Guid.NewGuid().ToString("N").Substring(0,6));
                Inventory(account,remote);
                foreach(DiskFile entry in files){stop.Token.ThrowIfCancellationRequested();string occupied=entry.Destination;while(!String.IsNullOrEmpty(occupied)){occupiedDestinations.Add(occupied);occupied=Path.GetDirectoryName(occupied);}}
                preserver=new FilePreserver(history.DirectoryPath,settings.ApiFolder,files.Select(f=>f.Destination),stop.Token);
                history.RefreshPreservedPaths();
                RelocatedArchiveRepair.Run(history.DirectoryPath,settings.ApiFolder,files,stop.Token,CanRecoverArchived);
                foreach(DiskFile f in files){stop.Token.ThrowIfCancellationRequested();current=f.Path;checkFileBytes=checkFileLength=0;checkFileStarted=DateTime.UtcNow;Snapshot("checking_local","",false);HistoryRecord old=history.Find(f.Path);bool same=FileRules.SameVersion(f,old);f.ResumeEligible=same&&CloudIntegrity.HasHash(f);if(same){f.Status=FileRules.NormalizeStatus(old);f.DownloadedBytes=old.downloadedBytes;}
                    try{preserver.CheckDestination(f.Destination);}catch(IOException){f.Local=false;f.Status="failed";f.Message="Destination conflicts with preserved files or pending recovery.";checkFilesChecked++;continue;}
                    SafeDestination(f.Destination);bool cached=(f.Length>0||f.SizeKnown)&&mode!=RunMode.All&&PreviouslyVerified(f,old);if(cached){f.Verification=CloudIntegrity.HasHash(f)?"history":"size_only";f.ActualHash=old.actualHash;}f.Local=(f.Length>0||f.SizeKnown)&&(cached||CloudIntegrity.CheckAndRecord(f.Destination,f,stop.Token,delegate(long done,long total){checkFileBytes=done;checkFileLength=total;if(done==0)lastPhase="";Snapshot("checking_local","",false);}));if(f.Local){f.DownloadedBytes=f.Length;if(!same||!FileRules.Success(old.status)||!old.localWriteTicks.HasValue||old.localWriteTicks.Value!=new FileInfo(f.Destination).LastWriteTimeUtc.Ticks)history.SaveApi(f,"local","");else f.Status="local";}else if(FileRules.Success(f.Status))f.Status="pending";
                    if(preserver.Blocked(f)){f.Status="integrity_failed";f.Local=false;f.Message=File.Exists(f.PreservedPath)?"Checksum mismatch; copy preserved.":"Checksum mismatch; preserved copy is missing. Automatic retries remain blocked.";}
                    string partial=f.Destination+".ydr-part";if(RangeMap.Exists(f.Destination))f.DownloadedBytes=RangeMap.Progress(f.Destination);else if(File.Exists(partial))f.DownloadedBytes=new FileInfo(partial).Length;
                    checkFilesChecked++;Snapshot("checking_local","",false);
                }
                List<DiskFile> queue=FileRules.Order(files.Where(f=>mode==RunMode.Continue ? f.Status=="pending"||f.Status=="cancelled_user" : FileRules.Select(f,history.Find(f.Path),mode)));selected=queue.Count;skipped=files.Count-selected;queueReady=true;int next=0;Stopwatch reportClock=Stopwatch.StartNew();
                Queue<Job> readyJobs=new Queue<Job>();
                while(next<queue.Count||jobs.Count>0){
                    foreach(Job job in jobs.ToArray())if(job.Task!=null&&job.Task.IsCompleted)Finish(job,job.Task.GetAwaiter().GetResult());
                    foreach(Job job in jobs.ToArray())if(job.LinkTask!=null&&job.LinkTask.IsCompleted){
                        LinkResult link=job.LinkTask.GetAwaiter().GetResult();job.LinkTask=null;
                        if(link.Completed!=null)Finish(job,link.Completed);
                        else if(stop.IsCancellationRequested)Finish(job,new Result{Status="cancelled_user",Message="Stopped before download started.",Bytes=job.Bytes});
                        else{job.Link=link.Link;readyJobs.Enqueue(job);}
                    }
                    if(stop.IsCancellationRequested){
                        while(readyJobs.Count>0){Job job=readyJobs.Dequeue();Finish(job,new Result{Status="cancelled_user",Message="Stopped before download started.",Bytes=job.Bytes});}
                        if(jobs.Count==0){phase="stopped";break;}
                    }else{
                        // Keep a bounded set of file tasks waiting at the connection gate,
                        // so a retry yields its connection to files already in that queue.
                        while(readyJobs.Count>0&&jobs.Count(j=>j.Task!=null)<downloadConcurrency+waitingCapacity){Job job=readyJobs.Dequeue();job.Task=Task.Run(()=>Download(job));}
                        while(next<queue.Count&&jobs.Count(j=>j.LinkTask!=null)<linkConcurrency&&jobs.Count(j=>j.Task==null)<waitingCapacity){
                            DiskFile f=queue[next++];if(f.Local){processed++;continue;}
                            Job job=new Job{File=f,Prior=history.Find(f.Path)};jobs.Add(job);job.LinkTask=PrepareLink(job);
                        }
                    }
                    Snapshot(stop.IsCancellationRequested?"stopping":"running","",false);
                    if(reportClock.Elapsed.TotalSeconds>=120){Reports.Write(report,destination,files,latest);reportClock.Restart();}
                    Thread.Sleep(50);
                }

            }catch(OperationCanceledException){retryableError=!stop.IsCancellationRequested;phase=stop.IsCancellationRequested?"stopped":"error";error=stop.IsCancellationRequested?"":"API listing request timed out.";}
            catch(ApiException ex){retryableError=ex.Code>=500||ex.Code==429||ex.Code==408;phase="error";error=ex.Code==401?"OAuth token is invalid or expired.":ex.Code==403?"Token has no access to these files.":"Yandex API returned HTTP "+ex.Code;Event(error);}
            catch(Exception ex){retryableError=ex is HttpRequestException||ex is System.Net.WebException;phase=stop.IsCancellationRequested?"stopped":"error";error=stop.IsCancellationRequested?"":ex is IOException||ex is ArgumentException?ex.Message:"API request failed or timed out.";Event(error);}
            finally{
                bool cleanCompletion=phase=="completed"&&!stop.IsCancellationRequested;
                stop.Cancel();foreach(Job job in jobs.ToArray())try{Result r=job.Task!=null?job.Task.GetAwaiter().GetResult():job.LinkTask!=null?job.LinkTask.GetAwaiter().GetResult().Completed:null;if(r==null)r=new Result{Status="cancelled_user",Message="Stopped before download started.",Bytes=job.Bytes};job.File.DownloadedBytes=r.Bytes;history.SaveApi(job.File,r.Status,r.Message);}catch{}jobs.Clear();
                if(refreshedInventory!=0&&!String.IsNullOrEmpty(inventoryPath))try{SaveInventory(inventoryPath,inventoryAccount,inventoryRoot,true,new Stack<string>(),null,0);}catch(Exception cacheError){retryableError=false;phase="error";error="Could not save refreshed cloud metadata: "+cacheError.GetType().Name;}
                clock.Stop();Snapshot(phase,error,true);if(history!=null){try{if(phase!="stopped"||queueReady)Reports.Write(report,settings.ApiFolder,files,latest);}catch(Exception reportError){retryableError=false;string reportMessage="Could not save reports: "+reportError.GetType().Name;Event(reportMessage);Snapshot("error",String.IsNullOrEmpty(error)?reportMessage:error+"\n"+reportMessage,true);}history.Close(cleanCompletion);}if(client!=null)client.Dispose();if(downloadClient!=null)downloadClient.Dispose();stop.Dispose();
            }

        }
        void Finish(Job job,Result result){
            if(FileRules.Success(result.Status))linkCache.Discard(history.DirectoryPath+"\n"+job.File.Path);
            job.File.DownloadedBytes=result.Bytes;history.SaveApi(job.File,result.Status,result.Message);processed++;
            if(result.Status=="success")downloaded.Add(job.File.Path);if(result.Status=="cloud_error")errors.Add(job.File.Path);
            Event(job.File.Path+"  ?  "+result.Status+(result.Message==""?"":"  ?  "+result.Message));jobs.Remove(job);
        }
        bool retryableError; Snapshot latest;
        void Snapshot(string phase,string error,bool finished){
            if(!finished&&lastPhase==phase&&(DateTime.UtcNow-lastPublish).TotalMilliseconds<400)return;lastPhase=phase;lastPublish=DateTime.UtcNow;
            Snapshot s=new Snapshot{Phase=phase,Error=error,RetryableError=retryableError,Finished=finished,Total=files.Count,Selected=selected,Processed=processed,Skipped=skipped,ScanErrors=scanErrors,Report=report,Current=current,Seconds=clock.Elapsed.TotalSeconds,Active=jobs.Count,Downloaded=downloaded.Count,CloudErrorsThisPass=errors.Count};
            ApiPacerStatus apiStatus=apiPacer.Status();s.ApiEffectiveIntervalMs=apiStatus.IntervalMs;s.ApiWaitingRequests=apiStatus.Waiting;s.ApiCooldownCode=apiStatus.Code;s.ApiCooldownSeconds=apiStatus.CooldownSeconds;s.ApiStatusCapturedUtc=DateTime.UtcNow;
            if(phase=="checking_local"){s.CheckFilesChecked=checkFilesChecked;s.CheckFilesTotal=files.Count;s.CheckFileBytes=checkFileBytes;s.CheckFileLength=checkFileLength;s.CheckFileStartedUtc=checkFileStarted;s.ProgressChecked=checkFilesChecked;s.ProgressTotal=files.Count;}
            foreach(string category in new[]{"photos","videos","documents","archives","audio","other"}){List<DiskFile> items=files.Where(f=>f.Category==category).ToList();s.Categories.Add(new CategoryStats{Category=category,Total=items.Count,Available=items.Count(f=>FileRules.Success(f.Status)),Downloaded=items.Count(f=>downloaded.Contains(f.Path)),Remaining=items.Count(f=>!FileRules.Success(f.Status)),ZeroDownload=items.Count(FileRules.CancelledZero),CloudErrors=items.Count(f=>f.Status=="cloud_error"),Deferred=items.Count(f=>f.Status=="timeout"||f.Status=="failed"||f.Status=="cancelled_user")});}
            s.OtherErrors=files.Count(f=>f.Status=="integrity_failed");s.Available=s.Categories.Sum(c=>c.Available);s.CloudErrors=s.Categories.Sum(c=>c.CloudErrors);s.Deferred=s.Categories.Sum(c=>c.Deferred);s.PerHour=s.Seconds>0?processed*3600.0/s.Seconds:0;s.RemainingSeconds=s.PerHour>0?Math.Max(0,selected-processed)*3600.0/s.PerHour:0;s.Events=events.ToList();
            if(catalog==null||finished||(DateTime.UtcNow-catalogTime).TotalSeconds>=2){catalog=BrowserCatalog.Capture(files,settings.ApiFolder);foreach(Job job in jobs){BrowserFile item=catalog.FirstOrDefault(f=>f.LocalPath==job.File.Destination);if(item!=null){item.Bytes=Interlocked.Read(ref job.Bytes);item.Status="downloading";}}catalogTime=DateTime.UtcNow;}s.Catalog=catalog;
            if(queueReady){s.ProgressTotal=runMode==RunMode.Continue?files.Count:selected;s.ProgressChecked=runMode==RunMode.Continue?skipped+processed:processed;}latest=s;publish(s);
        }
        sealed class ApiException:Exception {public readonly int Code;public readonly double RetryAfterSeconds;public ApiException(int code,double retryAfterSeconds=0){Code=code;RetryAfterSeconds=retryAfterSeconds;}}
    }
}
