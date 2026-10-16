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
                PassTransitionTests.Run(root);
                SafeDownloadTests.Run(root);
                PreservedCleanupTests.Run(root);
                PreparationStopTests.Run(root);
                RangeMapTests.Run(root);
                DurablePartialTests.Run(root);
                SignedLinkTests.Run(root);
                ApiPacerTests.Run(root);
                Settings defaults=new Settings();defaults.ApplyTransferDefaults();Assert(defaults.IdleSeconds==3&&defaults.LinkTimeoutSeconds==3&&defaults.RepeatProblemFiles,"Transfer defaults differ from fixed 3 + 3 with retries.");defaults.IdleSeconds=4;defaults.LinkTimeoutSeconds=6;defaults.RepeatProblemFiles=false;defaults.ApplyTransferDefaults();Assert(defaults.IdleSeconds==4&&defaults.LinkTimeoutSeconds==6&&!defaults.RepeatProblemFiles,"Transfer migration overwrote saved choices more than once.");
                UpdaterTests.Run(root);
                string diagnostic=CrashDiagnostics.Describe(new IOException("sensitive-credential"));Assert(diagnostic.Contains("IOException")&&!diagnostic.Contains("sensitive-credential"),"Crash diagnostics retained exception messages.");
                using(MainForm shell=new MainForm(Path.Combine(root,"shell-preview"))){
                    System.Reflection.BindingFlags transferFlags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                    System.Windows.Forms.NumericUpDown linkWait=(System.Windows.Forms.NumericUpDown)typeof(MainForm).GetField("linkTimeout",transferFlags).GetValue(shell),fileWait=(System.Windows.Forms.NumericUpDown)typeof(MainForm).GetField("idle",transferFlags).GetValue(shell);
                    System.Windows.Forms.NumericUpDown links=(System.Windows.Forms.NumericUpDown)typeof(MainForm).GetField("linkConcurrency",transferFlags).GetValue(shell),downloads=(System.Windows.Forms.NumericUpDown)typeof(MainForm).GetField("downloadConcurrency",transferFlags).GetValue(shell);
                    System.Windows.Forms.NumericUpDown interval=(System.Windows.Forms.NumericUpDown)typeof(MainForm).GetField("requestInterval",transferFlags).GetValue(shell);Assert(interval.Value==50&&interval.Minimum==25&&interval.Maximum==1000,"API launch interval default or range incorrect.");
                    System.Windows.Forms.NumericUpDown streams=(System.Windows.Forms.NumericUpDown)typeof(MainForm).GetField("streamsPerFile",transferFlags).GetValue(shell),threshold=(System.Windows.Forms.NumericUpDown)typeof(MainForm).GetField("multiStreamThreshold",transferFlags).GetValue(shell);
                    Assert(streams.Value==4&&streams.Minimum==1&&streams.Maximum==8&&threshold.Value==256&&threshold.Minimum==64&&threshold.Maximum==4096,"Multi-stream defaults or bounds incorrect.");
                    Assert(links.Value==7&&downloads.Value==20&&links.Minimum==1&&downloads.Minimum==1&&links.Maximum==30&&downloads.Maximum==30,"Concurrency defaults or ranges incorrect.");
                    System.Windows.Forms.CheckBox repeats=(System.Windows.Forms.CheckBox)typeof(MainForm).GetField("repeatProblems",transferFlags).GetValue(shell);
                    Assert(linkWait.Value==3&&fileWait.Value==3&&repeats.Checked,"UI did not show fixed 3 + 3 retry defaults.");
                    typeof(MainForm).GetMethod("SetBusy",transferFlags).Invoke(shell,new object[]{true});Assert(!linkWait.Enabled&&!fileWait.Enabled,"Timeout fields remained editable during a pass.");typeof(MainForm).GetMethod("SetBusy",transferFlags).Invoke(shell,new object[]{false});Assert(linkWait.Enabled&&fileWait.Enabled,"Timeout fields did not unlock.");
                    typeof(MainForm).GetMethod("SetBusy",transferFlags).Invoke(shell,new object[]{true});Assert(!links.Enabled&&!downloads.Enabled&&!interval.Enabled&&!streams.Enabled&&!threshold.Enabled,"Concurrency or interval fields remained editable during a pass.");typeof(MainForm).GetMethod("SetBusy",transferFlags).Invoke(shell,new object[]{false});links.Value=30;downloads.Value=30;interval.Value=100;streams.Value=8;threshold.Value=512;
                    linkWait.Value=6;fileWait.Value=4;repeats.Checked=false;
                    Settings transferSaved=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(Path.Combine(root,"shell-preview","settings.json")));transferSaved.ApplyTransferDefaults();Assert(transferSaved.LinkTimeoutSeconds==6&&transferSaved.IdleSeconds==4&&!transferSaved.RepeatProblemFiles&&transferSaved.ApiLinkConcurrency==30&&transferSaved.ApiDownloadConcurrency==30&&transferSaved.ApiRequestIntervalMs==100&&transferSaved.StreamsPerFile==8&&transferSaved.MultiStreamThresholdMB==512,"Edited timeout, concurrency, interval or retry values were not preserved.");
                    System.Reflection.BindingFlags shellFlags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;System.Windows.Forms.CheckBox automatic=(System.Windows.Forms.CheckBox)typeof(MainForm).GetField("autoCheck",shellFlags).GetValue(shell);automatic.Checked=false;Settings savedSettings=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(Path.Combine(root,"shell-preview","settings.json")));Assert(!savedSettings.AutoCheckUpdates&&((System.Windows.Forms.Button)typeof(MainForm).GetField("checkRelease",shellFlags).GetValue(shell)).Enabled,"Manual update check or automatic-check setting was lost.");
                    Assert((int)typeof(MainForm).GetField("section",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(shell)==4,"Application did not start on Settings.");shell.PreviewDashboard();foreach(string section in new[]{"overview","settings","journal","passes","files"})shell.PreviewShellSection(section);
                    Snapshot categorySample=new Snapshot{Total=5};categorySample.Categories.Add(new CategoryStats{Category="photos",Total=5});categorySample.Categories.Add(new CategoryStats{Category="audio",Total=0});typeof(MainForm).GetMethod("Render",shellFlags).Invoke(shell,new object[]{categorySample});Assert(((System.Windows.Forms.DataGridView)typeof(MainForm).GetField("categories",shellFlags).GetValue(shell)).Rows.Count==1,"Overview displayed categories absent from the file list.");
                    System.Windows.Forms.Control setup=(System.Windows.Forms.Control)typeof(MainForm).GetField("settingsCard",shellFlags).GetValue(shell);Assert(!setup.Enabled,"Download settings remained enabled during a pass.");typeof(MainForm).GetMethod("SetBusy",shellFlags).Invoke(shell,new object[]{false});Assert(setup.Enabled,"Download settings remained locked after stopping.");
                    Snapshot checking=new Snapshot{Phase="checking_local",CheckFilesChecked=1,CheckFilesTotal=5,ProgressChecked=1,ProgressTotal=5,CheckFileLength=8*1048576,CheckFileBytes=2*1048576,CheckFileStartedUtc=DateTime.UtcNow.AddSeconds(-3),Current="disk:/sample.mov"};typeof(MainForm).GetMethod("Render",shellFlags).Invoke(shell,new object[]{checking});string checkText=((System.Windows.Forms.Label)typeof(MainForm).GetField("detail",shellFlags).GetValue(shell)).Text;Assert(checkText.Contains("1/5")&&checkText.Contains("25%")&&checkText.Contains("sample.mov"),"Local verification UI hid file count, hash progress or current filename.");
                    Assert(shell.MinimumSize.Height<800&&shell.MinimumSize.Width>=1000&&shell.Width>=shell.MinimumSize.Width,"New shell still requires the old tall window.");
                }
                using(FileDashboard dashboard=new FileDashboard()){
                    System.Collections.Generic.List<BrowserFile> entries=new System.Collections.Generic.List<BrowserFile>{new BrowserFile{Path="photos/2024/a.jpg",LocalPath=Path.Combine(root,"a.jpg"),Length=10,Bytes=10,Status="success"},new BrowserFile{Path="photos/2024/b.jpg",LocalPath=Path.Combine(root,"b.jpg"),Length=20,Bytes=5,Status="timeout"},new BrowserFile{Path="photos/2025/c.jpg",LocalPath=Path.Combine(root,"c.jpg"),Length=30,Status="cloud_error"}};
                    dashboard.UpdateCatalog(entries,root);dashboard.SelectFolder("photos/2024");
                    System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                    System.Windows.Forms.TreeView tree=(System.Windows.Forms.TreeView)typeof(FileDashboard).GetField("tree",flags).GetValue(dashboard);
                    System.Windows.Forms.DataGridView grid=(System.Windows.Forms.DataGridView)typeof(FileDashboard).GetField("grid",flags).GetValue(dashboard);
                    Assert(tree.Nodes[0].ToolTipText.Contains("1 / 3")&&grid.Rows.Count==2,"Dashboard folder totals or direct file listing incorrect.");grid.CurrentCell=grid.Rows[1].Cells[0];
                    File.WriteAllText(entries[0].LocalPath,"test");string opened=null;dashboard.FileOpener=path=>opened=path;grid.CurrentCell=grid.Rows[0].Cells[1];typeof(FileDashboard).GetMethod("OpenSelected",flags).Invoke(dashboard,null);Assert(opened==entries[0].LocalPath,"Downloaded file did not open.");opened=null;grid.CurrentCell=grid.Rows[1].Cells[1];typeof(FileDashboard).GetMethod("OpenSelected",flags).Invoke(dashboard,null);Assert(opened==null,"Dashboard opened a partial file.");
                    System.Windows.Forms.TreeNode originalRoot=tree.Nodes[0];Assert(((BufferedTree)tree).Buffered,"Folder tree double buffering was not enabled.");dashboard.UpdateCatalog(entries.Select(f=>new BrowserFile{Path=f.Path,LocalPath=f.LocalPath,Length=f.Length,Bytes=f.Bytes+1,Status=f.Status}).ToList(),root);Assert(ReferenceEquals(originalRoot,tree.Nodes[0]),"Statistics update rebuilt the folder tree.");
                    Assert(tree.SelectedNode.Text.StartsWith("2024")&&((BrowserFile)grid.CurrentRow.Tag).Path.EndsWith("b.jpg"),"Dashboard update lost folder or selected file.");
                    ((System.Windows.Forms.CheckBox)typeof(FileDashboard).GetField("remaining",flags).GetValue(dashboard)).Checked=true;
                    Assert(grid.Rows.Count==1&&((BrowserFile)grid.Rows[0].Tag).Path.EndsWith("b.jpg"),"Dashboard remaining filter incorrect.");
                    ((System.Windows.Forms.TextBox)typeof(FileDashboard).GetField("search",flags).GetValue(dashboard)).Text="absent";Assert(grid.Rows.Count==0,"Dashboard search ignored query.");
                    System.Collections.Generic.List<BrowserFile> grown=entries.Concat(new[]{new BrowserFile{Path="photos/2026/new.jpg",LocalPath=Path.Combine(root,"new.jpg"),Length=10,Status="pending"}}).ToList();dashboard.UpdateCatalog(grown,root);Assert(ReferenceEquals(originalRoot,tree.Nodes[0])&&tree.SelectedNode.Text=="2024","Growing scan rebuilt the tree or lost the selected folder.");dashboard.UpdateCatalog(entries,root);Assert(ReferenceEquals(originalRoot,tree.Nodes[0]),"Removing a folder rebuilt the tree.");((System.Windows.Forms.TextBox)typeof(FileDashboard).GetField("search",flags).GetValue(dashboard)).Clear();dashboard.SelectFolder("");Assert(!Convert.ToString(grid.Rows[0].Cells[1].Value).StartsWith("▸")&&!Convert.ToString(grid.Rows[0].Cells[2].Value).Contains("photos"),"Folder rows repeated the name in status or added an extra arrow.");
                    Assert(!new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new Snapshot{Catalog=entries}).Contains("a.jpg"),"Dashboard catalog leaked into report summary.");
                }
                using(StableLog log=new StableLog()){
                    log.Multiline=true;log.UpdateEvents(new System.Collections.Generic.List<string>{"first","second"});log.Select(1,3);
                    log.UpdateEvents(new System.Collections.Generic.List<string>{"second","third"});
                    Assert(log.SelectedText=="irs"&&log.Text=="first"+Environment.NewLine+"second"+Environment.NewLine+"third","Activity log replaced text or cleared selection.");
                    log.UpdateEvents(new System.Collections.Generic.List<string>{"second","third"});Assert(log.SelectedText=="irs","Unchanged activity log cleared selection.");
                    log.UpdateEvents(new System.Collections.Generic.List<string>{"12:00  photo.jpg  ·  success","12:01  success.jpg  ·  failed"});log.SetFilter("",2);Assert(log.Text.Contains("photo.jpg")&&!log.Text.Contains("success.jpg"),"Successful filter used a filename instead of outcome.");log.SetFilter("",1);Assert(log.Text.Contains("success.jpg")&&!log.Text.Contains("photo.jpg"),"Error filter missed a failed download.");log.SetFilter("",0);Assert(log.Text.Contains("first")&&log.Text.Contains("photo.jpg"),"All-events filter lost previous entries.");
                }
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
                File.WriteAllText(result,"PASS: safe copy preservation and recovery, MD5/SHA256 priority, missing-hash evidence, version failure ledger, manual retry and deduplication, single-payload range maps and process-kill recovery; segmented range downloads, shared connection budget, per-stream idle timeouts and queue exclusion, retained segment resume, torn merge and changed-version recovery, unsupported/invalid ranges, corruption and Stop; native checks plus independent 7-link / 20-download pipeline, separate timeout budgets, queue exclusion, cancellation and history, API authentication, path safety, resume/MD5 integrity, retry filters, defaults and encrypted token storage.",Encoding.UTF8);
                return 0;
            }catch(Exception ex){File.WriteAllText(result,"FAIL: "+ex.ToString(),Encoding.UTF8);return 1;}
        }
        static DiskFile Make(string path){FileInfo f=new FileInfo(path);return new DiskFile{Path=f.FullName,Folder=f.DirectoryName,Length=f.Length,Modified=f.LastWriteTimeUtc.ToString("o"),Category=FileRules.Category(path),Local=true};}
    }
}
