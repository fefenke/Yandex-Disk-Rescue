using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace DiskRescue {
    static class StartupReliabilityTests {
        static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
        sealed class Api:HttpMessageHandler {
            public object Size;public bool Omit,WithHash;public int Links;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
                string body;if(request.RequestUri.AbsolutePath.EndsWith("/download")){Links++;body="{\"href\":\"https://download.example/size-test\"}";}
                else if(request.RequestUri.AbsolutePath.EndsWith("/resources")){
                    var file=new Dictionary<string,object>{{"path","disk:/a.bin"},{"type","file"},{"modified","v1"}};if(!Omit)file["size"]=Size;
                    if(WithHash)file["md5"]="098f6bcd4621d373cade4e832627b4f6";
                    bool metadata=Uri.UnescapeDataString(request.RequestUri.Query).StartsWith("?path=disk:/a.bin&",StringComparison.Ordinal);
                    body=new JavaScriptSerializer().Serialize(metadata?(object)file:new{type="dir",_embedded=new{items=new[]{file}}});
                }else body="{\"user\":{\"uid\":\"startup-test\"}}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body)});
            }
        }
        public static void LegacyZero(string root){
            foreach(bool existing in new[]{false,true}){
            string folder=Path.Combine(root,"cached-zero-"+existing),data=Path.Combine(root,"cached-zero-data-"+existing);Snapshot last=null;
            var settings=new Settings{UseApi=true,ApiToken="test-secret",ApiFolder=folder,UseCachedApiList=true,ApiRequestIntervalMs=25};
            new ApiEngine(data,s=>last=s,new Api{Size=0},new Download()).Run(settings,RunMode.Remaining);
            string file=Path.Combine(folder,"a.bin");if(!existing)File.Delete(file);
            string inventory=Directory.GetFiles(data,"inventory.json",SearchOption.AllDirectories).Single();File.WriteAllText(inventory,File.ReadAllText(inventory).Replace("\"SizeKnown\":true","\"SizeKnown\":false"));
            new ApiEngine(data,s=>last=s,new Api{Size=4},new Download()).Run(settings,RunMode.Remaining);
            Assert(last.Downloaded==1&&File.ReadAllText(file)=="test","Cached zero size was trusted without fresh metadata.");
            }
        }
        public static void FastFiles(string root){
            string folder=Path.Combine(root,"fast-local"),data=Path.Combine(root,"fast-local-data");Snapshot last=null;
            var settings=new Settings{UseApi=true,ApiToken="test-secret",ApiFolder=folder,UseCachedApiList=true,ApiRequestIntervalMs=25};
            new ApiEngine(data,s=>last=s,new Api{Size=4,WithHash=true},new Download()).Run(settings,RunMode.Remaining);
            string file=Path.Combine(folder,"a.bin");var api=new Api{Size=4,WithHash=true};
            using(var locked=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.None))new ApiEngine(data,s=>last=s,api,new Download()).Run(settings,RunMode.Remaining);
            Assert(last.Phase=="completed"&&last.Available==1&&api.Links==0,"Unchanged verified file was reopened for hashing instead of using history.");
            File.WriteAllText(file,"evil");File.SetLastWriteTimeUtc(file,DateTime.UtcNow.AddMinutes(1));api=new Api{Size=4,WithHash=true};
            new ApiEngine(data,s=>last=s,api,new Download()).Run(settings,RunMode.Remaining);
            Assert(api.Links==1&&File.ReadAllText(file)=="test","Changed same-size file bypassed verification.");
            DateTime unchanged=File.GetLastWriteTimeUtc(file);File.WriteAllText(file,"evil");File.SetLastWriteTimeUtc(file,unchanged);
            api=new Api{Size=4,WithHash=true};new ApiEngine(data,s=>last=s,api,new Download()).Run(settings,RunMode.All);
            Assert(api.Links==1&&File.ReadAllText(file)=="test","Full verification trusted cached evidence after detecting changed content with unchanged size and timestamp.");
        }
        sealed class Download:HttpMessageHandler {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("test")});}
        }
        public static void Size(string root){
            object[] values={null,null,false,"0",0.5,-1,0,4};
            for(int i=0;i<values.Length;i++){
                string folder=Path.Combine(root,"strict-size-"+i),data=Path.Combine(root,"strict-size-data-"+i);var api=new Api{Size=values[i],Omit=i==0};Snapshot last=null;
                new ApiEngine(data,s=>last=s,api,new Download()).Run(new Settings{UseApi=true,ApiToken="test-secret",ApiFolder=folder,UseCachedApiList=false,ApiRequestIntervalMs=25},RunMode.Remaining);
                string destination=Path.Combine(folder,"a.bin");
                if(i<6)Assert(last.Phase=="error"&&last.Downloaded==0&&api.Links==0&&!File.Exists(destination),"Invalid initial listing size was treated as a real file: case "+i);
                else Assert(last.Phase=="completed"&&last.Downloaded==1&&File.Exists(destination)&&new FileInfo(destination).Length==Convert.ToInt64(values[i]),"Legitimate zero or ordinary size was rejected.");
            }
        }
        static bool Indexed(HistoryStore store){PropertyInfo property=typeof(HistoryStore).GetProperty("LoadedFromIndex",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);Assert(property!=null,"History has no compact-index fast path.");return (bool)property.GetValue(store,null);}
        public static void History(string root){
            string folder=Path.Combine(root,"indexed-history"),data=Path.Combine(root,"indexed-data");Directory.CreateDirectory(folder);string local=Path.Combine(folder,"a.bin");File.WriteAllText(local,"test");
            var f=new DiskFile{Path="disk:/a.bin",Destination=local,Length=4,Modified="v1",Verification="md5_verified",ActualHash="abc",Md5="expected",PreservedPath="saved"};string directory;
            using(var store=new HistoryStore(data,folder,"index-test")){directory=store.DirectoryPath;for(int i=0;i<100;i++)store.SaveApi(f,"success","");}
            string journal=Path.Combine(directory,"history.jsonl"),index=Path.Combine(directory,"history.latest");Assert(File.Exists(index),"Compact history index was not saved.");string original=File.ReadAllText(journal);
            using(var store=new HistoryStore(data,folder,"index-test")){Assert(Indexed(store),"Unchanged journal did not use its compact index.");HistoryRecord r=store.Find(f.Path);Assert(r.status=="success"&&r.actualHash=="abc"&&r.expectedHash=="expected"&&r.preservedPath=="saved"&&r.localWriteTicks.HasValue,"History index lost verification evidence.");}
            Assert(File.ReadAllText(journal)==original,"Indexing rewrote the original history journal.");
            File.AppendAllText(journal,"\n{\"path\":\"disk:/a.bin\",\"length\":4,\"modified\":\"v2\",\"status\":\"timeout\"}\n{torn");
            using(var store=new HistoryStore(data,folder,"index-test"))Assert(!Indexed(store)&&store.Find(f.Path).modified=="v2","Appended journal or torn tail bypassed index invalidation.");
            using(var store=new HistoryStore(data,folder,"index-test"))Assert(Indexed(store)&&store.Find(f.Path).modified=="v2","Index was not rebuilt after journal change.");
            string valid=File.ReadAllText(index);File.WriteAllText(index,valid.Replace("timeout","success"));
            using(var store=new HistoryStore(data,folder,"index-test"))Assert(!Indexed(store)&&store.Find(f.Path).status=="timeout","Corrupted but valid-JSON index overrode the original journal.");
            using(var cancel=new CancellationTokenSource()){cancel.Cancel();bool stopped=false;try{new HistoryStore(data,folder,"index-test",cancel.Token);}catch(OperationCanceledException){stopped=true;}Assert(stopped,"Compact history load ignored cancellation.");}
        }
        public static void Relocation(string root){
            string oldData=Path.Combine(root,"relocation-old"),newData=Path.Combine(root,"relocation-new"),folder=Path.Combine(root,"relocation-files");Directory.CreateDirectory(folder);
            var f=new DiskFile{Path="disk:/moved.bin",Destination=Path.Combine(folder,"moved.bin"),Length=8,Modified="v1|abc",Md5="abc"};string context;
            using(var history=new HistoryStore(oldData,folder,"relocation-account")){
                context=history.DirectoryPath;
                RangeMap map=RangeMap.Open(f.Destination,context+"\n"+CloudIntegrity.Version(f),8,1,CancellationToken.None);
                using(Stream output=map.Writer(0))output.Write(new byte[]{1,2,3,4},0,4);
            }
            Directory.Move(oldData,newData);
            using(var history=new HistoryStore(newData,folder,"relocation-account")){
                var engine=new ApiEngine(newData,x=>{});var flags=BindingFlags.Instance|BindingFlags.NonPublic;typeof(ApiEngine).GetField("history",flags).SetValue(engine,history);
                string identity=(string)typeof(ApiEngine).GetMethod("MapIdentity",flags).Invoke(engine,new object[]{f});
                Assert(identity=="ydr-context:"+Path.GetFileName(context)+"\n"+CloudIntegrity.Version(f),"Map identity still depends on the executable/data directory.");
                Assert(RangeMap.Matches(f.Destination,identity,8),"Relocated legacy map was treated as another cloud version.");
                RangeMap migrated=RangeMap.Open(f.Destination,identity,8,1,CancellationToken.None);Assert(migrated.Bytes==4,"Relocation discarded confirmed extents.");
                Assert(!RangeMap.Matches(f.Destination,identity.Replace(Path.GetFileName(context),"api-000000000000000000000000"),8),"Relocation accepted a different account/root context.");
                Assert(!RangeMap.Matches(f.Destination,identity+"changed",8),"Relocation accepted another cloud version.");
                using(var damaged=new FileStream(f.Destination+".ydr-download",FileMode.Open,FileAccess.Write)){damaged.WriteByte(99);}
                Assert(RangeMap.Open(f.Destination,identity,8,1,CancellationToken.None).Bytes==0,"Relocation trusted a corrupt saved extent.");
            }
            ArchiveOwnerRelocation(root);
        }
        static void ArchiveOwnerRelocation(string root){
            string folder=Path.Combine(root,"owner-files"),oldData=Path.Combine(root,"owner-old"),newData=Path.Combine(root,"owner-new");Directory.CreateDirectory(folder);
            var f=new DiskFile{Path="disk:/owner.bin",Destination=Path.Combine(folder,"owner.bin"),Length=8,Modified="v1"};
            string context=Path.Combine(oldData,"api-111111111111111111111111");var preserver=new FilePreserver(context,folder,new[]{f.Destination});
            File.WriteAllText(f.Destination+".ydr-part","saved");preserver.PreservePartial(f,f.Destination+".ydr-part",null);
            Directory.Move(oldData,newData);context=Path.Combine(newData,Path.GetFileName(context));preserver=new FilePreserver(context,folder,new[]{f.Destination});
            File.WriteAllText(f.Destination+".ydr-part","saved-again");preserver.PreservePartial(f,f.Destination+".ydr-part",null);
            Assert(Directory.GetDirectories(folder).Count(p=>File.Exists(Path.Combine(p,".ydr-owner")))==1,"Moving data changed archive ownership and created another archive.");
        }
        public static void Interface(string root){
            string data=Path.Combine(root,"api-only-shell");Directory.CreateDirectory(data);File.WriteAllText(Path.Combine(data,"settings.json"),"{\"UseApi\":false,\"RestartClient\":false}");
            using(var form=new MainForm(data)){
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                var method=(System.Windows.Forms.ComboBox)typeof(MainForm).GetField("method",flags).GetValue(form);
                var settings=(Settings)typeof(MainForm).GetField("settings",flags).GetValue(form);
                Assert(settings.UseApi&&method.Items.Count==1&&method.SelectedIndex==0,"Retired client mode remains selectable or old settings disabled API.");
                typeof(MainForm).GetField("ru",flags).SetValue(form,false);typeof(MainForm).GetMethod("Translate",flags).Invoke(form,null);
                Assert(settings.UseApi&&method.Items.Count==1&&method.SelectedIndex==0,"Language switch restored retired client mode.");
            }
        }
        public static void Compatibility(string root){
            var serializer=new JavaScriptSerializer();
            const string savedSettings="{\"UseApi\":false,\"RestartClient\":true,\"Folder\":\"C:\\\\Legacy Disk\",\"Concurrency\":3,\"ApiFolder\":\"C:\\\\Saved\",\"Language\":\"en\",\"IdleSeconds\":9,\"TransferSettingsVersion\":1,\"unknownSetting\":true}";
            Settings old=serializer.Deserialize<Settings>(savedSettings);old.ApplyTransferDefaults();
            Assert(old.Folder==@"C:\Legacy Disk"&&old.ApiFolder==@"C:\Saved"&&old.Language=="en"&&old.IdleSeconds==9,"Legacy settings fields or saved user choices were lost.");
            old.ApiToken="fixture-secret";Assert(!serializer.Serialize(old).Contains("fixture-secret"),"Compatibility settings fixture leaked a token.");
            string passes=Path.Combine(root,"legacy-passes.jsonl");
            const string savedPasses="{\"StartedUtc\":\"\\/Date(0)\\/\",\"Method\":\"Desktop\",\"Mode\":\"Continue\",\"Outcome\":\"completed\",\"Downloaded\":2}\n{\"Method\":\"API\",\"Mode\":\"unknown-old-mode\",\"Downloaded\":3}\n{broken";
            File.WriteAllText(passes,savedPasses);var rows=PassHistory.Load(passes);
            Assert(rows.Count==2&&rows[0].Method=="Desktop"&&rows[0].Mode=="Continue"&&rows[0].Downloaded==2&&rows[0].StartedUtc.ToUniversalTime()==new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)&&rows[1].Mode=="unknown-old-mode","Legacy pass strings, dates or torn-tail recovery changed.");
            Assert(File.ReadAllText(passes)==savedPasses,"Reading legacy passes rewrote source data.");
            string data=Path.Combine(root,"compat-data"),identity="compatibility-fixture";
            string directory=Path.Combine(data,HistoryStore.IdentityKey(identity));Directory.CreateDirectory(directory);
            string journal=Path.Combine(directory,"history.jsonl");
            const string savedHistory="{\"path\":\"disk:/A\",\"length\":4,\"modified\":\"v1\",\"status\":\"success\",\"downloadedBytes\":4,\"verification\":\"md5_verified\",\"actualHash\":\"098f6bcd4621d373cade4e832627b4f6\"}\n{\"path\":\"disk:/a\",\"length\":4,\"modified\":\"v1\",\"status\":\"timeout\",\"downloadedBytes\":2}\n{broken";
            File.WriteAllText(journal,savedHistory);
            using(var history=new HistoryStore(data,root,identity))Assert(history.Records.Count==2&&history.Find("disk:/A").status=="success"&&history.Find("disk:/a").downloadedBytes==2,"Legacy cloud history lost case-sensitive identities or progress.");
            Assert(File.ReadAllText(journal)==savedHistory,"Reading cloud history rewrote its authoritative journal.");
        }
        public static void Run(string root){Compatibility(root);Relocation(root);Interface(root);Size(root);LegacyZero(root);History(root);FastFiles(root);}
    }
}
