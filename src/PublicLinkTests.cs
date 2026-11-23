using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace DiskRescue {
    internal static class PublicLinkTests {
        const string Url="https://disk.yandex.ru/d/public-test";
        const string Hash="098f6bcd4621d373cade4e832627b4f6";
        static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
        static void Public(Settings s){var field=typeof(Settings).GetField("Source");Assert(field!=null,"Missing explicit source mode.");field.SetValue(s,Enum.Parse(field.FieldType,"PublicLink"));typeof(Settings).GetField("PublicUrl").SetValue(s,Url);typeof(Settings).GetField("PublicFolder").SetValue(s,s.ApiFolder);}
        static Settings Options(string root,string name){var s=new Settings{UseApi=true,ApiFolder=Path.Combine(root,name,"files"),ApiToken="test-secret",UseCachedApiList=false,ApiRequestIntervalMs=25,IdleSeconds=2,LinkTimeoutSeconds=2,StreamsPerFile=1};Public(s);Directory.CreateDirectory(s.ApiFolder);return s;}
        static Snapshot Run(string data,Settings s,Api api,Download download){Snapshot last=null;new ApiEngine(data,x=>last=x,api,download).Run(s,RunMode.Remaining);return last;}
        public static void Links(){
            foreach(string valid in new[]{Url,"https://yadi.sk/d/abc","https://disk.yandex.ru/i/abc","https://disk.360.yandex.ru/d/abc"})Assert(ApiEngine.IsPublicUrl(valid),"Valid public link rejected.");
            foreach(string bad in new[]{"http://disk.yandex.ru/d/abc","https://example.invalid/d/abc","https://disk.yandex.ru.evil.invalid/d/abc","https://user@disk.yandex.ru/d/abc","https://disk.yandex.ru:444/d/abc","https://disk.yandex.ru/","https://disk.yandex.ru/d/abc/extra","https://disk.yandex.ru/d/../d/abc","https://disk.yandex.ru/d/abc?oauth_token=secret","https://disk.yandex.ru/d/abc#fragment"})Assert(!ApiEngine.IsPublicUrl(bad),"Unsafe public URL accepted: "+bad);
            Assert(ApiEngine.NormalizeRemote(" "+Url+" ")==Url,"Public normalization failed.");
            Assert(ApiEngine.IsPublicUrl(Url+"?from=public")&&ApiEngine.NormalizeRemote(Url+"?from=public")==Url+"?from=public","Ordinary query parameters were lost.");
            Assert(ApiEngine.Validation(new Settings{ApiToken="test-secret",ApiFolder="unused",RemoteFolder=Url})=="cloud_path","Account mode silently switched to public access.");
        }
        public static void Modes(string root){
            var s=new Settings{ApiFolder="account-folder",RemoteFolder="disk:/Photos"};Public(s);typeof(Settings).GetField("PublicFolder").SetValue(s,"public-folder");
            var run=(Settings)typeof(Settings).GetMethod("ForRun").Invoke(s,null);
            Assert(run.ApiFolder=="public-folder"&&run.RemoteFolder==Url&&run.ApiToken=="","Public run inherited account credentials or destination.");
            Assert(s.ApiFolder=="account-folder"&&s.RemoteFolder=="disk:/Photos","Resolving public run mutated account settings.");
            string json=new JavaScriptSerializer().Serialize(s);var loaded=new JavaScriptSerializer().Deserialize<Settings>(json);
            Assert((string)typeof(Settings).GetField("PublicFolder").GetValue(loaded)=="public-folder"&&loaded.ApiFolder=="account-folder","Mode destinations did not roundtrip.");
            var fresh=new JavaScriptSerializer().Deserialize<Settings>("{\"ApiFolder\":\"old-account\",\"RemoteFolder\":\"disk:/\"}");
            Assert((string)typeof(Settings).GetField("PublicFolder").GetValue(fresh)==""&&fresh.ApiFolder=="old-account","Legacy destination leaked into public mode.");
            fresh.ApplySourceDefaults();Assert(fresh.Source==DownloadSource.Account&&fresh.ApiFolder=="old-account"&&fresh.PublicFolder=="","Account migration changed destinations.");
            var oldPublic=new Settings{ApiFolder="old-public",RemoteFolder=Url};oldPublic.ApplySourceDefaults();Assert(oldPublic.Source==DownloadSource.PublicLink&&oldPublic.ApiFolder==""&&oldPublic.PublicFolder=="old-public"&&oldPublic.PublicUrl==Url,"Legacy public settings were assigned to the account.");
        }
        public static void Ui(string root){
            string data=Path.Combine(root,"public-ui");Directory.CreateDirectory(data);
            File.WriteAllText(Path.Combine(data,"settings.json"),"{\"ApiFolder\":\"account-choice\",\"RemoteFolder\":\"disk:/Photos\",\"AutoCheckUpdates\":false}");
            using(var form=new MainForm(data)){
                var flags=BindingFlags.NonPublic|BindingFlags.Instance;var type=typeof(MainForm);var field=type.GetField("sourceMode",flags);Assert(field!=null,"Missing explicit UI source selector.");
                var source=(SourceTabs)field.GetValue(form);var folder=(TextBox)type.GetField("folder",flags).GetValue(form);var remote=(TextBox)type.GetField("remoteFolder",flags).GetValue(form);var settings=(Settings)type.GetField("settings",flags).GetValue(form);
                source.SelectedIndex=1;Assert(folder.Text==""&&remote.Text=="","Public UI inherited account fields.");folder.Text="public-choice";remote.Text=Url;
                source.SelectedIndex=0;Assert(folder.Text=="account-choice"&&remote.Text=="disk:/Photos","Account UI lost its independent fields.");
                source.SelectedIndex=1;Assert(folder.Text=="public-choice"&&remote.Text==Url,"Public UI did not remember fields.");
                type.GetMethod("Translate",flags).Invoke(form,null);Assert(folder.Text=="public-choice"&&source.SelectedIndex==1,"Language switch changed source fields.");
                var saving=System.Diagnostics.Stopwatch.StartNew();while(SettingsStore.Load(Path.Combine(data,"settings.json")).Value.Source!=DownloadSource.PublicLink&&saving.ElapsedMilliseconds<2500){Application.DoEvents();Thread.Sleep(10);}
                var saved=SettingsStore.Load(Path.Combine(data,"settings.json")).Value;Assert(saved.ApiFolder=="account-choice"&&(string)typeof(Settings).GetField("PublicFolder").GetValue(saved)=="public-choice","UI settings did not persist both destinations.");
                type.GetMethod("SetBusy",flags).Invoke(form,new object[]{true});Assert(!source.Enabled,"Source can change during download.");
            }
        }
        public static void Catalog(string root){
            var files=BrowserCatalog.Capture(new[]{new DiskFile{Path="/sub/leaf.txt",Destination=Path.Combine(root,"sub","leaf.txt")}},root);
            Assert(files.Single().Path=="sub/leaf.txt","Public paths disappear from the file browser.");
        }
        public static void Tabs(string root){
            string data=Path.Combine(root,"source-tabs");Directory.CreateDirectory(data);File.WriteAllText(Path.Combine(data,"settings.json"),"{\"AutoCheckUpdates\":false}");
            using(var form=new MainForm(data)){
                var flags=BindingFlags.NonPublic|BindingFlags.Instance;var type=typeof(MainForm);var source=(Control)type.GetField("sourceMode",flags).GetValue(form);
                Assert(!(source is ComboBox),"Source selection is still a dropdown instead of two visible tabs.");
                var help=type.GetField("sourceHelpButton",flags);Assert(help!=null&&help.GetValue(form)!=null,"Source help button is missing.");
                string text=(string)type.GetMethod("SourceHelpText",flags).Invoke(form,null);Assert(text.Contains("OAuth")&&text.Contains("disk:/"),"Source help omits the account/public distinction.");
                var token=(Control)type.GetField("apiToken",flags).GetValue(form);var remote=(Control)type.GetField("remoteFolder",flags).GetValue(form);var grid=(TableLayoutPanel)type.GetField("credentials",flags).GetValue(form);
                Control authRow=token;while(authRow.Parent!=grid)authRow=authRow.Parent;Assert(grid.GetRow(remote.Parent)<grid.GetRow(authRow),"Common source field must stay above optional OAuth controls.");
                source.GetType().GetProperty("SelectedIndex").SetValue(source,1,null);remote.Text="https://example.invalid/file";
                var hint=(Label)type.GetField("sourceHint",flags).GetValue(form);Assert(hint.Text.Length>0&&!hint.Text.Contains("распознана"),"Invalid link is labelled as recognized.");
                remote.Text=Url;Assert(hint.Text.Contains("OAuth")&&hint.Text.Contains("распознана"),"Valid link recognition hint is missing.");
                IntPtr handle=form.Handle;typeof(Program).GetMethod("PrepareControls",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{form});
                foreach(bool russian in new[]{true,false}){
                    type.GetField("ru",flags).SetValue(form,russian);type.GetMethod("Translate",flags).Invoke(form,null);
                    foreach(float scale in new[]{1.25f,0.8f}){form.Scale(new System.Drawing.SizeF(scale,scale));form.PerformLayout();var title=(Control)type.GetField("sourceTitle",flags).GetValue(form);var helpButton=(Control)help.GetValue(form);Assert(title.Left>=26&&helpButton.Left>=title.Right+3,"Cloud, title or question mark overlap after scaling/translation.");}
                }
            }
        }
        public static void Switching(string root){
            string data=Path.Combine(root,"source-switching");Directory.CreateDirectory(data);string path=Path.Combine(data,"settings.json");File.WriteAllText(path,"{\"AutoCheckUpdates\":false}");
            using(var form=new MainForm(data)){
                var flags=BindingFlags.NonPublic|BindingFlags.Instance;var source=(SourceTabs)typeof(MainForm).GetField("sourceMode",flags).GetValue(form);
                using(var locked=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){
                    source.SelectedIndex=1;
                    Assert(typeof(MainForm).GetField("settingsSaveWarning",flags).GetValue(form)==null,"Switching a tab performs blocking disk persistence.");
                }
                var deadline=System.Diagnostics.Stopwatch.StartNew();while(SettingsStore.Load(path).Value.Source!=DownloadSource.PublicLink&&deadline.ElapsedMilliseconds<2500){Application.DoEvents();Thread.Sleep(10);}
                Assert(SettingsStore.Load(path).Value.Source==DownloadSource.PublicLink,"Deferred source choice was not saved.");
                string help=(string)typeof(MainForm).GetMethod("SourceHelpText",flags).Invoke(form,null);Assert(!help.Contains("формата")&&!help.Contains("Папка сохранения")&&!help.Contains("Esc"),"Source help retains unwanted paragraphs.");
                source.SelectedIndex=0;var waiting=System.Diagnostics.Stopwatch.StartNew();while(waiting.ElapsedMilliseconds<350){Application.DoEvents();Thread.Sleep(5);}
                source.SelectedIndex=1;typeof(MainForm).GetMethod("SaveSettings",flags).Invoke(form,null);waiting.Restart();while(waiting.ElapsedMilliseconds<400){Application.DoEvents();Thread.Sleep(5);}
                Assert(SettingsStore.Load(path).Value.Source==DownloadSource.PublicLink,"A stale background save overwrote the latest source.");
            }
        }
        public static void Benchmark(string root){
            string data=Path.Combine(root,"source-benchmark");Directory.CreateDirectory(data);File.WriteAllText(Path.Combine(data,"settings.json"),"{\"AutoCheckUpdates\":false}");
            using(var form=new MainForm(data)){
                var flags=BindingFlags.NonPublic|BindingFlags.Instance;var source=(SourceTabs)typeof(MainForm).GetField("sourceMode",flags).GetValue(form);IntPtr handle=form.Handle;typeof(Program).GetMethod("PrepareControls",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{form});
                var times=new List<double>();for(int i=0;i<30;i++){var clock=System.Diagnostics.Stopwatch.StartNew();source.SelectedIndex=1-source.SelectedIndex;clock.Stop();times.Add(clock.Elapsed.TotalMilliseconds);}
                times.Sort();Console.WriteLine("Tab switch: median="+times[15].ToString("F2")+" ms, max="+times[29].ToString("F2")+" ms (30 switches).");
            }
        }
        public static void StableLayout(string root){
            string data=Path.Combine(root,"source-fixed-layout");Directory.CreateDirectory(data);File.WriteAllText(Path.Combine(data,"settings.json"),"{\"AutoCheckUpdates\":false}");
            using(var form=new MainForm(data)){
                var flags=BindingFlags.NonPublic|BindingFlags.Instance;var type=typeof(MainForm);IntPtr handle=form.Handle;typeof(Program).GetMethod("PrepareControls",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{form});
                var source=(SourceTabs)type.GetField("sourceMode",flags).GetValue(form);var remote=(Control)type.GetField("remoteFolder",flags).GetValue(form);var folder=(Control)type.GetField("folder",flags).GetValue(form);var card=(Control)type.GetField("sourceCard",flags).GetValue(form);var token=(Control)type.GetField("apiToken",flags).GetValue(form);var help=(Control)type.GetField("tokenHelp",flags).GetValue(form);var caption=(Control)type.GetField("apiCaption",flags).GetValue(form);
                var remotePosition=remote.PointToScreen(System.Drawing.Point.Empty);var folderPosition=folder.PointToScreen(System.Drawing.Point.Empty);int cardHeight=card.Height;
                for(int i=0;i<10;i++){source.SelectedIndex=1-source.SelectedIndex;form.PerformLayout();Assert(card.Height==cardHeight&&remote.PointToScreen(System.Drawing.Point.Empty)==remotePosition&&folder.PointToScreen(System.Drawing.Point.Empty)==folderPosition,"Source switching moves fields or resizes the page.");}
                Assert(help.Parent==caption.Parent&&help.PointToScreen(System.Drawing.Point.Empty).Y+help.Height<=token.Parent.PointToScreen(System.Drawing.Point.Empty).Y+3,"OAuth link is not above the token field.");
            }
        }
        public static void PublicWarning(string root){
            string data=Path.Combine(root,"source-warning");Directory.CreateDirectory(data);File.WriteAllText(Path.Combine(data,"settings.json"),"{\"AutoCheckUpdates\":false}");
            using(var form=new MainForm(data)){
                var flags=BindingFlags.NonPublic|BindingFlags.Instance;var type=typeof(MainForm);var field=type.GetField("publicWarning",flags);Assert(field!=null,"Public mode has no visible privacy warning.");var warning=(Control)field.GetValue(form);
                var source=(SourceTabs)type.GetField("sourceMode",flags).GetValue(form);source.SelectedIndex=1;
                Assert(warning.Text.Contains("Не публикуйте")&&warning.Text.Contains("получателю")&&warning.Text.Contains("OAuth"),"Public warning omits sharing or credential risks.");
                Assert(GuideText.Ru.Contains("Публичная ссылка")&&GuideText.Ru.Contains("Не публикуйте")&&GuideText.En.Contains("Public link")&&GuideText.En.Contains("Do not publish"),"Embedded guide is out of sync with public mode/privacy guidance.");
                type.GetField("ru",flags).SetValue(form,false);type.GetMethod("Translate",flags).Invoke(form,null);Assert(warning.Text.Contains("Do not publish")&&warning.Text.Contains("OAuth"),"English privacy warning is missing.");
            }
        }
        sealed class Api:HttpMessageHandler {
            public string RootPath="/",Name="root.txt",ExpectedKey=Url;public bool Folder,WrongIdentity,Changed,RejectListing,InvalidSize,Large,Empty,NoHash,MissingModified,WrongName,Deep;public int ListingStatus=404,Links,Metadata,Pages;public readonly List<string> Requests=new List<string>();
            string Leaf{get{return Deep?"/sub/deeper/third/leaf.txt":"/sub/leaf.txt";}}
            Dictionary<string,object> FileEntry(string path){var entry=new Dictionary<string,object>{{"type","file"},{"name",Name},{"size",InvalidSize?(object)"4":4},{"modified",MissingModified?"":Changed?"v2":"v1"},{"md5",NoHash?"":Hash}};if(path!=null)entry["path"]=path;return entry;}
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
                Assert(request.RequestUri.Host=="cloud-api.yandex.net"&&request.RequestUri.AbsolutePath.StartsWith("/v1/disk/public/",StringComparison.Ordinal),"Public operation used private/external API.");
                Assert(request.Headers.Authorization==null,"OAuth leaked to public API.");Requests.Add(request.RequestUri.PathAndQuery);
                var query=request.RequestUri.Query.TrimStart('?').Split('&').Select(x=>x.Split(new[]{'='},2)).ToDictionary(x=>x[0],x=>x.Length>1?Uri.UnescapeDataString(x[1]):"");
                Assert(query["public_key"]==ExpectedKey,"Public key changed.");string path=query.ContainsKey("path")?query["path"]:null;object body;
                if(request.RequestUri.AbsolutePath.EndsWith("/download")){Links++;Assert(Folder?path==Leaf:path==null,"Wrong root/child download path.");body=new{href="https://download.example/payload"};}
                else if(query.ContainsKey("fields")){Metadata++;var entry=FileEntry(WrongIdentity?"/other.txt":Folder?Leaf:RootPath);if(WrongName)entry["name"]="other.txt";body=entry;}
                else {
                    Pages++;if(RejectListing)return Task.FromResult(new HttpResponseMessage((HttpStatusCode)ListingStatus));
                    if(Empty)body=new{type="dir",path="/",_embedded=new{items=new object[0]}};
                    else if(Large){int offset=Int32.Parse(query["offset"]);body=new{type="dir",path="/",_embedded=new{items=Enumerable.Range(offset,Math.Min(1000,1001-offset)).Select(i=>new{type="file",path="/f"+i+".txt",size=0,modified="v1"}).ToArray()}};}
                    else if(!Folder)body=FileEntry(RootPath);
                    else if(path==null)body=new{type="dir",path="/",_embedded=new{items=new[]{new{type="dir",path="/sub"}}}};
                    else if(Deep&&(path=="/sub"||path=="/sub/deeper")){string next=path=="/sub"?"/sub/deeper":"/sub/deeper/third";body=new{type="dir",path=path,_embedded=new{items=new[]{new{type="dir",path=next}}}};}
                    else {Assert(path==(Deep?"/sub/deeper/third":"/sub"),"Wrong nested listing route.");body=new{type="dir",path=path,_embedded=new{items=new[]{FileEntry(Leaf)}}};}
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(new JavaScriptSerializer().Serialize(body))});
            }
        }
        sealed class Download:HttpMessageHandler {
            public bool RejectFirst;public int Calls;public long Offset;public Action Rejected;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Assert(request.Headers.Authorization==null,"OAuth leaked to signed URL.");Calls++;if(RejectFirst&&Calls==1){if(Rejected!=null)Rejected();return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));}var range=request.Headers.Range;Offset=range==null?0:range.Ranges.Single().From.Value;var response=new HttpResponseMessage(range==null?HttpStatusCode.OK:HttpStatusCode.PartialContent){Content=new StringContent("test".Substring((int)Offset))};if(range!=null)response.Content.Headers.ContentRange=new ContentRangeHeaderValue(Offset,3,4);return Task.FromResult(response);}
        }
        public static void Resume(string root){
            var s=Options(root,"public-resume");string data=Path.Combine(root,"public-resume","data"),destination=Path.Combine(s.ApiFolder,"root.txt");File.WriteAllText(destination+".ydr-part","te");
            string account;using(var sha=SHA256.Create())account="public_"+BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Url))).Replace("-","").Substring(0,8);
            using(var history=new HistoryStore(data,s.ApiFolder,account+"|"+Url+"|"+s.ApiFolder.ToUpperInvariant()))history.SaveApi(new DiskFile{Path="/root.txt",Destination=destination,Length=4,Modified="v1|"+Hash,Md5=Hash,DownloadedBytes=2},"timeout","");
            var api=new Api();var download=new Download();var last=Run(data,s,api,download);Assert(last.Downloaded==1&&download.Offset==2&&api.Metadata==1&&File.ReadAllText(destination)=="test","Public root resume failed to validate metadata or use Range.");
            s.UseCachedApiList=true;File.WriteAllText(destination,"evil");api=new Api();last=Run(data,s,api,new Download());Assert(last.Downloaded==1&&api.Pages==0&&api.Metadata==1&&File.ReadAllText(destination)=="test","Cached root identity failed.");Assert(Directory.GetFiles(s.ApiFolder,"*",SearchOption.AllDirectories).Any(p=>File.ReadAllText(p)=="evil"),"Existing conflicting file was lost.");
            s.PublicUrl=Url+"-other";api=new Api{ExpectedKey=s.PublicUrl};last=Run(data,s,api,new Download());Assert(last.Available==1&&Directory.GetFiles(data,"history.jsonl",SearchOption.AllDirectories).Length==2,"Different public resources shared history.");
            var noHash=Options(root,"public-nohash");last=Run(Path.Combine(root,"public-nohash","data"),noHash,new Api{NoHash=true},new Download());Assert(last.Downloaded==1&&last.Catalog.Single().Verification=="size_only","Missing hash was presented as hash verification.");
            var empty=Options(root,"public-empty");last=Run(Path.Combine(root,"public-empty","data"),empty,new Api{Empty=true},new Download());Assert(last.Phase=="completed"&&last.Total==0&&last.ScanErrors==0,"Empty public folder failed.");
        }
        public static void Downloads(string root){
            int index=0;foreach(string path in new string[]{null,"","/"}){
                string name="public-root-"+(index++);var s=Options(root,name);var api=new Api{RootPath=path};var download=new Download();var last=Run(Path.Combine(root,name,"data"),s,api,download);
                Assert(last.Phase=="completed"&&last.Downloaded==1&&File.ReadAllText(Path.Combine(s.ApiFolder,"root.txt"))=="test","Root public file failed: "+path+" "+last.Error);
                Assert(api.Links==1&&download.Calls==1,"Root file did not use download route.");
            }
            var nested=Options(root,"public-nested");var nestedApi=new Api{Folder=true};var nestedLast=Run(Path.Combine(root,"public-nested","data"),nested,nestedApi,new Download());Assert(nestedLast.Downloaded==1&&File.ReadAllText(Path.Combine(nested.ApiFolder,"sub","leaf.txt"))=="test"&&nestedApi.Pages==2,"Nested public folder failed.");
            var deep=Options(root,"public-deep");var deepApi=new Api{Folder=true,Deep=true};var deepLast=Run(Path.Combine(root,"public-deep","data"),deep,deepApi,new Download());Assert(deepLast.Downloaded==1&&deepApi.Pages==4&&File.ReadAllText(Path.Combine(deep.ApiFolder,"sub","deeper","third","leaf.txt"))=="test","Multi-level public folder failed.");
            foreach(bool changed in new[]{false,true}){
                string name="public-refresh-"+changed;var s=Options(root,name);var api=new Api();var download=new Download{RejectFirst=true,Rejected=delegate{api.Changed=changed;}};var last=Run(Path.Combine(root,name,"data"),s,api,download);
                Assert(changed?last.Downloaded==0&&api.Links==1:last.Downloaded==1&&api.Links==2&&download.Calls==2,"Public link refresh used wrong version/route.");
            }
        }
        public static void Safety(string root){
            foreach(bool invalidSize in new[]{false,true}){
                string name="public-identity-"+invalidSize;var s=Options(root,name);string partial=Path.Combine(s.ApiFolder,"root.txt.ydr-part");File.WriteAllText(partial,"saved");var api=new Api{WrongIdentity=!invalidSize,InvalidSize=invalidSize};var download=new Download();var last=Run(Path.Combine(root,name,"data"),s,api,download);
                Assert(last.Downloaded==0&&download.Calls==0&&File.ReadAllText(partial)=="saved","Untrusted metadata changed saved bytes.");
            }
            foreach(string badName in new[]{"","..","../outside","/root.txt","a\\b.txt"}){var s=Options(root,"public-name-"+Guid.NewGuid().ToString("N"));var d=new Download();var last=Run(Path.Combine(s.ApiFolder,"data"),s,new Api{Name=badName},d);Assert(last.Phase=="error"&&d.Calls==0,"Unsafe root filename accepted.");}
            var malformed=Options(root,"public-modified");var malformedDownload=new Download();var malformedResult=Run(Path.Combine(root,"public-modified","data"),malformed,new Api{MissingModified=true},malformedDownload);Assert(malformedResult.Phase=="error"&&malformedDownload.Calls==0,"Missing modified metadata accepted.");
            var renamed=Options(root,"public-renamed");string retained=Path.Combine(renamed.ApiFolder,"root.txt.ydr-part");File.WriteAllText(retained,"saved");var renamedDownload=new Download();var renamedResult=Run(Path.Combine(root,"public-renamed","data"),renamed,new Api{WrongName=true},renamedDownload);Assert(renamedResult.Downloaded==0&&renamedDownload.Calls==0&&File.ReadAllText(retained)=="saved","Root name mismatch consumed partial bytes.");
            var missing=Options(root,"public-missing");var failed=Run(Path.Combine(root,"public-missing","data"),missing,new Api{RejectListing=true},new Download());Assert(failed.Phase=="error"&&failed.ScanErrors==1,"Missing public root reported completion.");
            foreach(int code in new[]{400,401,403}){var denied=Options(root,"public-denied-"+code);var deniedDownload=new Download();var result=Run(Path.Combine(root,"public-denied-"+code,"data"),denied,new Api{RejectListing=true,ListingStatus=code},deniedDownload);Assert(result.Phase=="error"&&result.Downloaded==0&&deniedDownload.Calls==0,"Public listing HTTP "+code+" was reported as success.");}
            var paged=Options(root,"public-paged");var apiPaged=new Api{Large=true};Snapshot snapshot=null;ApiEngine engine=null;engine=new ApiEngine(Path.Combine(root,"public-paged","data"),s=>{snapshot=s;if(s.Phase=="checking_local")engine.Stop();},apiPaged,new Download());engine.Run(paged,RunMode.Remaining);Assert(snapshot.Total==1001&&apiPaged.Pages==2,"Public pagination lost files.");
        }
        public static void Run(string root){Links();Modes(root);Ui(root);Tabs(root);Switching(root);StableLayout(root);PublicWarning(root);Catalog(root);Downloads(root);Resume(root);Safety(root);}
    }
}
