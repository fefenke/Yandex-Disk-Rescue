using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace DiskRescue {
    public enum RunMode { All=1, Remaining=7, PartialFiles=8, IntegrityFailures=9, DownloadErrors=11 }
    public sealed class Settings {
        public string Folder = "";
        public string Language = "ru";
        public int Concurrency = 5;
        public int IdleSeconds = 3;
        public int LinkTimeoutSeconds = 3;
        public int ApiLinkConcurrency = 7;
        public int ApiDownloadConcurrency = 20;
        public int ApiRequestIntervalMs = 50;
        public int StreamsPerFile = 4;
        public int MultiStreamThresholdMB = 256;
        public int MultiStreamThresholdMiB; // Legacy setting read once during migration.
        public bool RepeatProblemFiles = true;
        public int TransferSettingsVersion;
        public bool RestartClient = true;
        public bool UseApi;
        public bool UseCachedApiList = true;
        public bool ScanCloudOnStart,ScanCloudOnRepeats;
        public int CloudRefreshDefaultsVersion;

        public void ApplyCloudRefreshDefaults(){
            if(CloudRefreshDefaultsVersion>=1)return;
            ScanCloudOnStart=false;ScanCloudOnRepeats=false;CloudRefreshDefaultsVersion=1;
        }

        public bool AutoCheckUpdates = true;
        public string ApiFolder = "", RemoteFolder = "disk:/";
        [ScriptIgnore] public string ApiToken = "";
        public string GitHubReleasesUrl = "https://github.com/fefenke/Yandex-Disk-Rescue/releases", SupportUrl = "https://yoomoney.ru/fundraise/1KS67440FP0.261012";
        public void ApplyTransferDefaults() {
            if(MultiStreamThresholdMiB>0){MultiStreamThresholdMB=(int)Math.Max(64,Math.Min(4096,((long)MultiStreamThresholdMiB*1048576+999999)/1000000));MultiStreamThresholdMiB=0;}
            if(TransferSettingsVersion>=1)return;
            IdleSeconds=LinkTimeoutSeconds=3;RepeatProblemFiles=true;TransferSettingsVersion=1;
        }
    }
    public sealed class HistoryRecord {
        public string verification, actualHash, expectedHash, preservedPath;
        public string path, modified, status, message, time;
        public long length;
        public long? downloadedBytes;
        public long? localWriteTicks;
    }
    public sealed class DiskFile {
        public string Path, Folder, Category, Modified, Status = "pending", Message = "";
        public long Length;
        public long? DownloadedBytes;
        public string Md5 = "", Sha256 = "", Destination = "",Verification="",ActualHash="",PreservedPath="";
        public bool Local;
        public bool SizeKnown;
        public bool ResumeEligible;
        internal string DestinationError="";
    }
    public sealed class ActiveFile {
        public string Path, Category, State;
        public long Bytes, Length;
    }
    public sealed class CategoryStats {
        public string Category;
        public int Total, Available, Downloaded, Deferred, CloudErrors, Remaining, ZeroDownload;
    }
    public sealed class Snapshot {
        public string Phase = "idle", Error = "", Current = "", Report = "";
        public int Total, Selected, Processed, Downloaded, Available, Deferred, CloudErrors, OtherErrors, Skipped, Active, ScanErrors;
        public double Seconds, PerHour, RemainingSeconds;
        public bool Finished;
        public bool RecoveryRequired; public bool RetryableError;
        public int CloudErrorsThisPass;
        public int ProgressChecked, ProgressTotal;
        public int CheckFilesChecked,CheckFilesTotal;
        public long CheckFileBytes,CheckFileLength;
        public DateTime CheckFileStartedUtc;
        public int ApiEffectiveIntervalMs,ApiWaitingRequests,ApiCooldownCode;
        public double ApiCooldownSeconds;
        public DateTime ApiStatusCapturedUtc;
        public List<CategoryStats> Categories = new List<CategoryStats>();
        public List<ActiveFile> Files = new List<ActiveFile>();
        [ScriptIgnore] public List<BrowserFile> Catalog;
        public List<string> Events = new List<string>();
    }
    public static class FileRules {
        static readonly HashSet<string> Photos = Set("jpg jpeg png heic heif webp gif bmp tif tiff raw cr2 cr3 nef arw dng avif svg ico psd ai eps");
        static readonly HashSet<string> Videos = Set("mp4 mov avi mkv m4v mts m2ts 3gp webm wmv mpg mpeg flv vob ts");
        static readonly HashSet<string> Audio = Set("mp3 wav flac aac m4a ogg opus wma aiff alac");
        static readonly HashSet<string> Documents = Set("pdf doc docx xls xlsx ppt pptx odt ods odp rtf txt md csv json xml html htm epub fb2 djvu pages numbers key");
        static readonly HashSet<string> Archives = Set("zip rar 7z tar gz bz2 xz tgz iso img cab zst");
        static HashSet<string> Set(string value) { return new HashSet<string>(value.Split(' '), StringComparer.OrdinalIgnoreCase); }
        public static string Category(string path) {
            // Cloud paths may contain characters forbidden in Windows paths.
            // Categorization must inspect the name without filesystem parsing.
            string value=path??"";int slash=Math.Max(value.LastIndexOf('/'),value.LastIndexOf('\\'));int dot=value.LastIndexOf('.');
            string ext=dot>slash?value.Substring(dot+1):"";
            if (Photos.Contains(ext)) return "photos";
            if (Videos.Contains(ext)) return "videos";
            if (Audio.Contains(ext)) return "audio";
            if (Documents.Contains(ext)) return "documents";
            if (Archives.Contains(ext)) return "archives";
            return "other";
        }
        public static bool Success(string status) { return status == "success" || status == "local"; }
        public static bool CancelledZero(DiskFile file) { return !file.Local && (file.Status == "timeout" || file.Status == "cancelled_user" || file.Status == "failed"); }
        public static bool SameVersion(DiskFile file, HistoryRecord record) {
            return record != null && String.Equals(file.Path, record.path, StringComparison.OrdinalIgnoreCase) && file.Length == record.length && file.Modified == record.modified;
        }
        public static string NormalizeStatus(HistoryRecord record) {
            if (record == null) return "pending";
            if (record.status == "cancelled") return "timeout";
            if (record.status == "failed" && (record.message ?? "").IndexOf("80070185", StringComparison.OrdinalIgnoreCase) >= 0) return "cloud_error";
            if (record.status == "error") return "failed";
            return record.status;
        }
        public static bool Select(DiskFile file, HistoryRecord old, RunMode mode) {
            if(file.Status=="integrity_failed")return mode==RunMode.IntegrityFailures;
            bool same = SameVersion(file, old);
            string status = same ? NormalizeStatus(old) : "pending";
            if(mode==RunMode.IntegrityFailures)return status=="integrity_failed";
            if(status=="integrity_failed")return false;
            if (mode == RunMode.All) return true;
            if (mode == RunMode.Remaining) return !file.Local;
            if (mode == RunMode.DownloadErrors) return same&&!file.Local&&(status=="cloud_error"||status=="failed"||status=="timeout");
            if (mode == RunMode.PartialFiles) return !file.Local && HasPartialData(file.Destination);
            return false;
        }
        public static bool HasPartialData(string destination){
            if(String.IsNullOrEmpty(destination))return false;
            if(RangeMap.Progress(destination)>0)return true;
            if(File.Exists(destination+".ydr-part")&&new FileInfo(destination+".ydr-part").Length>0)return true;
            string folder=destination+".ydr-segments";
            return Directory.Exists(folder)&&Enumerable.Range(0,8).Any(i=>File.Exists(Path.Combine(folder,i+".part"))&&new FileInfo(Path.Combine(folder,i+".part")).Length>0);
        }
        public static List<DiskFile> Order(IEnumerable<DiskFile> files) {
            return files.OrderBy(f => f.Status == "pending" || Success(f.Status) ? 0 : 1).ToList();
        }
    }
    public sealed class HistoryStore : IDisposable {
        readonly StreamWriter writer;
        readonly string journal;bool dirty,disposed;
        readonly CancellationToken preparationToken;
        internal bool LoadedFromIndex{get;private set;}
        readonly JavaScriptSerializer json = new JavaScriptSerializer();
        public readonly Dictionary<string, HistoryRecord> Records;
        public readonly string DirectoryPath;
        public HistoryStore(string dataFolder, string root) : this(dataFolder,root,null) { }
        public HistoryStore(string dataFolder, string root, string identity,CancellationToken token=default(CancellationToken)) {
            token.ThrowIfCancellationRequested();
            preparationToken=token;
            Records=new Dictionary<string,HistoryRecord>(identity==null?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal);
            DirectoryPath = System.IO.Path.Combine(dataFolder, identity == null ? RootKey(root) : IdentityKey(identity));
            Directory.CreateDirectory(DirectoryPath);
            string path = System.IO.Path.Combine(DirectoryPath, "history.jsonl");
            journal=path;LoadedFromIndex=HistoryIndex.TryLoad(path,Records,token);
            if(!LoadedFromIndex&&File.Exists(path))ReadRecords(Records,File.ReadLines(path,Encoding.UTF8),token);
            token.ThrowIfCancellationRequested();
            FileStream stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.AutoFlush = true;
            RefreshPreservedPaths();
        }
        internal void RefreshPreservedPaths(){
            ArchiveNameMigration.Rename move=ArchiveNameMigration.Load(System.IO.Path.Combine(DirectoryPath,"preservation"));if(move==null)return;
            foreach(HistoryRecord record in Records.Values){string path=ArchiveNameMigration.Resolve(record.preservedPath,move);if(path==record.preservedPath)continue;record.preservedPath=path;writer.WriteLine();writer.WriteLine(json.Serialize(record));dirty=true;}
        }
        internal static void ReadRecords(Dictionary<string,HistoryRecord> records,IEnumerable<string> lines,CancellationToken token){
            var json=new JavaScriptSerializer();token.ThrowIfCancellationRequested();
            foreach(string line in lines){
                token.ThrowIfCancellationRequested();
                try {
                    HistoryRecord record = json.Deserialize<HistoryRecord>(line);
                    if (record != null && !String.IsNullOrEmpty(record.path)) records[record.path] = record;
                } catch { /* A torn final journal line does not discard older results. */ }
            }
            token.ThrowIfCancellationRequested();
        }
        public static string RootKey(string root) {
            string normalized = System.IO.Path.GetFullPath(root).TrimEnd('\\').ToUpperInvariant();
            using (SHA256 sha = SHA256.Create()) { return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(normalized))).Replace("-", "").Substring(0, 24); }
        }
        public static string IdentityKey(string identity) { using(SHA256 sha=SHA256.Create())return "api-"+BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-", "").Substring(0,24); }
        public HistoryRecord Find(string path) { HistoryRecord record; return Records.TryGetValue(path, out record) ? record : null; }
        public void Save(DiskFile file, string status, string message) {
            FileInfo current = new FileInfo(file.Path);
            if (current.Exists) { file.Length = current.Length; file.Modified = current.LastWriteTimeUtc.ToString("o"); }
            file.Status = status; file.Message = message ?? "";
            HistoryRecord record = new HistoryRecord { path = file.Path, length = file.Length, modified = file.Modified, status = status, message = file.Message, time = DateTimeOffset.Now.ToString("o") };
            // Leading newline separates the next record from a previously torn tail.
            writer.WriteLine(); writer.WriteLine(json.Serialize(record));
            Records[file.Path] = record;
            dirty=true;
        }
        public void Dispose() {Close(!preparationToken.IsCancellationRequested);}
        internal void Close(bool allowMaintenance) {
            if(disposed)return;disposed=true;long length;
            try{writer.Flush();length=writer.BaseStream.Length;}finally{writer.Dispose();}
            if(!allowMaintenance)return;
            long compactedLength=HistoryCompaction.TryCompact(journal,length,Records,CancellationToken.None);
            if(compactedLength!=length||dirty||!LoadedFromIndex&&Records.Count>0)HistoryIndex.Save(journal,compactedLength,Records);
        }
        public void SaveApi(DiskFile file, string status, string message) {
            file.Status=status;file.Message=message??"";
            HistoryRecord record=new HistoryRecord{path=file.Path,length=file.Length,modified=file.Modified,status=status,message=file.Message,time=DateTimeOffset.Now.ToString("o"),downloadedBytes=file.DownloadedBytes,verification=file.Verification,actualHash=file.ActualHash,expectedHash=!String.IsNullOrEmpty(file.Md5)?file.Md5:file.Sha256,preservedPath=file.PreservedPath};
            if(FileRules.Success(status)&&!String.IsNullOrEmpty(file.Destination)){FileInfo local=new FileInfo(file.Destination);if(local.Exists&&local.Length==file.Length)record.localWriteTicks=local.LastWriteTimeUtc.Ticks;}
            writer.WriteLine();writer.WriteLine(json.Serialize(record));Records[file.Path]=record;dirty=true;
        }
    }
    public static class Reports {
        static string Csv(object value) { return "\"" + Convert.ToString(value).Replace("\"", "\"\"") + "\""; }
        public static void Write(string directory, string root, IList<DiskFile> files, Snapshot snapshot) {
            Directory.CreateDirectory(directory);
            using (StreamWriter output = new StreamWriter(System.IO.Path.Combine(directory, "files.csv"), false, new UTF8Encoding(true))) {
                output.WriteLine("Path;Folder;Category;Status;Bytes;Message;Verification;ExpectedHash;ActualHash;PreservedPath");
                foreach (DiskFile f in files) output.WriteLine(String.Join(";", new string[] { Csv(f.Path), Csv(f.Folder), Csv(f.Category), Csv(f.Status), Csv(f.Length), Csv(f.Message), Csv(f.Verification), Csv(String.IsNullOrEmpty(f.Md5)?f.Sha256:f.Md5), Csv(f.ActualHash), Csv(f.PreservedPath) }));
            }
            using (StreamWriter output = new StreamWriter(System.IO.Path.Combine(directory, "categories.csv"), false, new UTF8Encoding(true))) {
                output.WriteLine("Category;Total;DownloadedTotal;DownloadedThisRun;Remaining;IncompleteDownload;SyncErrors");
                foreach (CategoryStats c in snapshot.Categories) output.WriteLine(String.Join(";", new string[] { c.Category, c.Total.ToString(), c.Available.ToString(), c.Downloaded.ToString(), c.Remaining.ToString(), c.ZeroDownload.ToString(), c.CloudErrors.ToString() }));
            }
            Dictionary<string, List<DiskFile>> direct = new Dictionary<string, List<DiskFile>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<DiskFile>> tree = new Dictionary<string, List<DiskFile>>(StringComparer.OrdinalIgnoreCase);
            string rootPath = System.IO.Path.GetFullPath(root).TrimEnd('\\');
            tree[rootPath] = new List<DiskFile>(); direct[rootPath] = new List<DiskFile>();
            foreach (DiskFile f in files) {
                if (!direct.ContainsKey(f.Folder)) direct[f.Folder] = new List<DiskFile>();
                direct[f.Folder].Add(f);
                string parent = f.Folder;
                while (true) {
                    if (!tree.ContainsKey(parent)) tree[parent] = new List<DiskFile>();
                    tree[parent].Add(f);
                    if (String.Equals(parent, rootPath, StringComparison.OrdinalIgnoreCase)) break;
                    DirectoryInfo next = Directory.GetParent(parent);
                    if (next == null) break;
                    parent = next.FullName.TrimEnd('\\');
                }
            }
            using (StreamWriter output = new StreamWriter(System.IO.Path.Combine(directory, "folders.csv"), false, new UTF8Encoding(true))) {
                output.WriteLine("Folder;Scope;Total;Available;NotDownloaded;CloudErrors");
                foreach (string folder in tree.Keys.OrderBy(x => x)) foreach (string scope in new string[] { "direct", "tree" }) {
                    List<DiskFile> items = scope == "tree" ? tree[folder] : direct.ContainsKey(folder) ? direct[folder] : new List<DiskFile>();
                    output.WriteLine(String.Join(";", new string[] { Csv(folder), scope, items.Count.ToString(), items.Count(f => FileRules.Success(f.Status)).ToString(), items.Count(f => f.Status == "timeout" || f.Status == "failed" || f.Status == "cancelled_user").ToString(), items.Count(f => f.Status == "cloud_error").ToString() }));
                }
            }
            File.WriteAllText(System.IO.Path.Combine(directory, "summary.json"), new JavaScriptSerializer().Serialize(snapshot), new UTF8Encoding(false));
            DataRetention.PruneReports(directory);
        }
    }
}
