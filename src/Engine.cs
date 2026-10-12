using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace DiskRescue {
    public sealed class Engine {
        public volatile Snapshot Latest = new Snapshot();
        volatile bool stopping;
        readonly List<string> events = new List<string>();
        readonly HashSet<string> downloadedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> cloudErrorPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly List<DiskFile> inventory = new List<DiskFile>();
        readonly List<Job> active = new List<Job>();
        Stopwatch elapsed = new Stopwatch();
        DateTime lastPublish = DateTime.MinValue;
        HistoryStore history;
        string report = "", current = "";
        int processed, selected, skipped, scanErrors;
        public string DataFolder { get; private set; }
        public bool RecoveryRequired { get; private set; }
        sealed class Job { public DiskFile File; public YandexCloudNative.Operation Operation; }
        public Engine(string dataFolder) { DataFolder = dataFolder; }
        public void Stop() { stopping = true; }
        void Event(string text) { events.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + text); if (events.Count > 160) events.RemoveAt(0); }
        public void Run(Settings settings, RunMode mode) {
            if(RecoveryRequired){Latest=new Snapshot{Phase="error",Finished=true,RecoveryRequired=true,Error="Restart Yandex Disk and this application before starting another pass."};return;}
            stopping = false; processed = selected = skipped = scanErrors = 0;
            inventory.Clear(); active.Clear(); events.Clear(); downloadedPaths.Clear(); cloudErrorPaths.Clear(); report="";elapsed.Restart(); lastPublish = DateTime.MinValue;
            string phase = "completed", error = "";
            Publish("preparing", "", false, true);
            try {
                YandexCloudNative.EnablePlaceholdersForCurrentThread();
                if (!Directory.Exists(settings.Folder)) throw new DirectoryNotFoundException("Choose an existing Yandex Disk folder.");
                history = new HistoryStore(DataFolder, settings.Folder);
                report = Path.Combine(history.DirectoryPath, "reports", DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6));
                Publish("scanning", "", false, true);
                Scan(settings.Folder, true);
                if (stopping) { phase = "stopped"; return; }
                foreach (DiskFile file in inventory) {
                    if(stopping){phase="stopped";return;}
                    current=file.Path;
                    HistoryRecord old = history.Find(file.Path);
                    bool same = FileRules.SameVersion(file, old);
                    if (same) { file.Status = FileRules.NormalizeStatus(old); file.Message = old.message ?? ""; }
                    // Existence of a cached file is recorded without launching it.
                    if (file.Local) {
                        if (!(same && FileRules.Success(file.Status))) history.Save(file, "local", "");
                    } else if (FileRules.Success(file.Status)) { file.Status = "pending"; }
                    Publish("preparing", "", false, false);
                }
                List<DiskFile> queue = FileRules.Order(inventory.Where(f => FileRules.Select(f, history.Find(f.Path), mode)));
                selected = queue.Count; skipped = inventory.Count - selected;
                int next = 0;
                bool restartNeeded = false;
                Stopwatch reportClock = Stopwatch.StartNew();
                Publish("running", "", false, true);
                while (next < queue.Count || active.Count > 0) {
                    while (!stopping && !restartNeeded && active.Count < settings.Concurrency && next < queue.Count) {
                        DiskFile file = queue[next++]; current = file.Path;
                        if (file.Local) { history.Save(file, "local", ""); processed++; continue; }
                        try {
                            YandexCloudNative.ProbeResult probe = YandexCloudNative.Probe(file.Path);
                            if (!probe.Supported) { Save(file, "failed", "Not a Windows Cloud Files placeholder."); continue; }
                            if ((probe.State & 0x30) == 0 && (probe.Attributes & (0x1000u | 0x40000u | 0x400000u)) == 0) { Save(file, "local", ""); continue; }
                            active.Add(new Job { File = file, Operation = YandexCloudNative.Start(file.Path) });
                        } catch (Exception ex) { Save(file, "failed", ex.Message); }
                    }
                    foreach (Job job in active.ToArray()) {
                        YandexCloudNative.Operation op = job.Operation;
                        op.Poll();
                        if (!op.Completed && !op.CancelRequested && (stopping || (DateTime.UtcNow - op.LastProgressUtc).TotalSeconds >= settings.IdleSeconds)) op.RequestCancel();
                        if (!op.Completed && op.CancelRequested && (DateTime.UtcNow - op.CancelRequestedUtc).TotalSeconds > 5) {
                            throw new IOException("Windows has not acknowledged cancellation. No more files will be started; the client queue may be blocked.");
                        }
                        if (op.Completed) {
                            string code = op.ErrorCode.ToString("X8");
                            string status = op.Status == "success" ? "success" : op.Status == "cancelled" ? (stopping ? "cancelled_user" : "timeout") : code == "80070185" ? "cloud_error" : "failed";
                            if(status=="timeout" || status=="cancelled_user")try{job.File.DownloadedBytes=YandexCloudNative.HydratedBytes(job.File.Path);}catch{job.File.DownloadedBytes=null;}
                            Save(job.File, status, op.ErrorCode == 0 ? "" : "0x" + code);
                            if (status == "success") downloadedPaths.Add(job.File.Path);
                            if (settings.RestartClient && status == "cloud_error") restartNeeded = true;
                            op.Dispose(); active.Remove(job);
                        }
                    }
                    if (restartNeeded && active.Count == 0 && !stopping) {
                        Publish("restarting", "", false, true);
                        RestartClient(); restartNeeded = false;
                    }
                    Publish(stopping ? "stopping" : "running", "", false, false);
                    if (reportClock.Elapsed.TotalSeconds >= 120) { Reports.Write(report, settings.Folder, inventory, Latest); reportClock.Restart(); }
                    if (stopping && active.Count == 0) break;
                    Thread.Sleep(100);
                }
                if (stopping) phase = "stopped";
            } catch (Exception ex) { phase = "error"; error = ex.Message; Event(ex.Message); }
            finally {
                foreach (Job job in active.ToArray()) { try { job.Operation.RequestCancel(); } catch { } }
                Stopwatch drain = Stopwatch.StartNew();
                while (active.Count > 0 && drain.Elapsed.TotalSeconds < 5) {
                    foreach (Job job in active.ToArray()) {
                        try {
                            job.Operation.Poll();
                            if (job.Operation.Completed) {
                                string status = job.Operation.Status == "success" ? "success" : job.Operation.Status=="cancelled" ? "cancelled_user" : job.Operation.ErrorCode.ToString("X8")=="80070185" ? "cloud_error" : "failed";
                                if(status=="cancelled_user")try{job.File.DownloadedBytes=YandexCloudNative.HydratedBytes(job.File.Path);}catch{job.File.DownloadedBytes=null;}
                                Save(job.File, status, "Finished while stopping.");
                                if (status == "success") downloadedPaths.Add(job.File.Path);
                                job.Operation.Dispose(); active.Remove(job);
                            }
                        } catch { }
                    }
                    if (active.Count > 0) Thread.Sleep(100);
                }
                foreach (Job job in active.ToArray()) {
                    try { Save(job.File, "failed", "Cancellation not acknowledged."); } catch { }
                    job.Operation.Dispose();
                    phase = "error"; error = "Cancellation was not acknowledged. Restart Yandex Disk before retrying.";
                    RecoveryRequired=true;
                }
                active.Clear(); elapsed.Stop();
                Publish(phase, error, true, true);
                try { if (history != null && !String.IsNullOrEmpty(report)) Reports.Write(report, settings.Folder, inventory, Latest); }
                catch (Exception ex) { Event("Report: " + ex.Message); Publish("error", ex.Message, true, true); }
                if (history != null) { history.Dispose(); history = null; }
            }
        }
        void Save(DiskFile file, string status, string message) { history.Save(file, status, message); if(status=="cloud_error")cloudErrorPaths.Add(file.Path);processed++; Event(Path.GetFileName(file.Path) + "  ·  " + status + (message.Length > 0 ? "  ·  " + message : "")); }
        void Scan(string root, bool readHydratedBytes) {
            Stack<string> folders = new Stack<string>(); folders.Push(Path.GetFullPath(root));
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (folders.Count > 0 && !stopping) {
                string folder = folders.Pop(); if (!seen.Add(folder)) continue;
                current = folder;
                try {
                    foreach (string path in Directory.GetFiles(folder)) {
                        if (stopping) break;
                        try {
                            FileInfo info = new FileInfo(path); uint flags = (uint)info.Attributes;
                            bool local=(flags & (0x1000u | 0x40000u | 0x400000u)) == 0;
                            if(local&&(flags&0x400u)!=0){YandexCloudNative.ProbeResult probe=YandexCloudNative.Probe(path);local=!probe.Supported||(probe.State&0x30)==0;}
                            DiskFile item=new DiskFile { Path = info.FullName, Folder = info.DirectoryName, Length = info.Length, Modified = info.LastWriteTimeUtc.ToString("o"), Category = FileRules.Category(path), Local = local };
                            if(local)item.DownloadedBytes=item.Length;
                            else if(readHydratedBytes && FileRules.SameVersion(item,history.Find(path)) && (FileRules.NormalizeStatus(history.Find(path))=="timeout" || FileRules.NormalizeStatus(history.Find(path))=="cancelled_user"))try{item.DownloadedBytes=YandexCloudNative.HydratedBytes(path);}catch(Exception ex){scanErrors++;Event(path+"  ·  "+ex.Message);}
                            inventory.Add(item);
                        } catch (Exception ex) { scanErrors++; Event(path + "  ·  " + ex.Message); }
                        Publish("scanning", "", false, false);
                    }
                    foreach (string child in Directory.GetDirectories(folder)) {
                        // Traverse cloud placeholder folders, skip ordinary junctions
                        // and symbolic links so external trees/cycles are not followed.
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0 && !YandexCloudNative.Probe(child).Supported) continue;
                        folders.Push(child);
                    }
                } catch (Exception ex) { scanErrors++; Event(folder + "  ·  " + ex.Message); }
                Publish("scanning", "", false, false);
            }
        }
        void Publish(string phase, string error, bool finished, bool force) {
            if (!force && (DateTime.UtcNow - lastPublish).TotalMilliseconds < 400) return;
            lastPublish = DateTime.UtcNow;
            Snapshot s = new Snapshot { Phase = phase, Error = error, Finished = finished, RecoveryRequired=RecoveryRequired, Total = inventory.Count, Selected = selected, Processed = processed, Skipped = skipped, ScanErrors = scanErrors, Report = report, Current = current, Seconds = elapsed.Elapsed.TotalSeconds, Active = active.Count };
            foreach (string category in new string[] { "photos", "videos", "documents", "archives", "audio", "other" }) {
                List<DiskFile> items = inventory.Where(f => f.Category == category).ToList();
                s.Categories.Add(new CategoryStats { Category = category, Total = items.Count, Available = items.Count(f => FileRules.Success(f.Status)), Downloaded = items.Count(f => downloadedPaths.Contains(f.Path)), Remaining = items.Count(f => !FileRules.Success(f.Status)), ZeroDownload = items.Count(FileRules.CancelledZero), Deferred = items.Count(f => f.Status == "timeout" || f.Status == "failed" || f.Status == "cancelled_user"), CloudErrors = items.Count(f => f.Status == "cloud_error") });
            }
            s.Downloaded = downloadedPaths.Count; s.Available = s.Categories.Sum(c => c.Available); s.Deferred = s.Categories.Sum(c => c.Deferred); s.CloudErrors = s.Categories.Sum(c => c.CloudErrors); s.OtherErrors = inventory.Count(f => f.Status == "failed");
            s.CloudErrorsThisPass=cloudErrorPaths.Count;
            s.PerHour = s.Seconds > 0 ? processed * 3600.0 / s.Seconds : 0;
            s.RemainingSeconds = s.PerHour > 0 ? Math.Max(0, selected - processed) * 3600.0 / s.PerHour : 0;
            foreach (Job job in active) s.Files.Add(new ActiveFile { Path = job.File.Path, Category = job.File.Category, Bytes = job.Operation.ProgressBytes, Length = job.File.Length, State = job.Operation.CancelRequested ? "cancelling" : "downloading" });
            s.Events = events.ToList(); Latest = s;
        }
        void RestartClient() {
            Process[] processes = Process.GetProcessesByName("YandexDisk4");
            if (processes.Length == 0) processes = Process.GetProcessesByName("YandexDisk2");
            string executable = null;
            foreach (Process p in processes) {
                try { executable = p.MainModule.FileName; } catch { }
                if (executable != null) break;
            }
            if (executable == null) {
                string candidate = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Yandex", "YandexDisk4", "YandexDisk4.exe");
                if (File.Exists(candidate)) executable = candidate;
            }
            if (executable == null) throw new FileNotFoundException("Cannot find the Yandex Disk client to restart.");
            Event("Restarting Yandex Disk.");
            foreach (Process p in processes) {
                try {
                    if (p.MainModule.FileName == executable) { p.Kill(); if (!p.WaitForExit(10000)) throw new IOException("Yandex Disk did not exit."); }
                } finally { p.Dispose(); }
            }
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
            for (int i = 0; i < 50 && !stopping; i++) Thread.Sleep(100);
            if (!stopping && Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)).Length == 0) throw new IOException("Yandex Disk failed to restart.");
        }
    }
}
