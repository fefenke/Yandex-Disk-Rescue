using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace DiskRescue {
    static class InventoryCompletenessTests {
        static void Assert(bool value,string message){if(!value)throw new Exception(message);}
        static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=128*1024*1024};
        static string Hash(string value){using(var hash=MD5.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant();}
        sealed class Api:HttpMessageHandler {
            public bool Fail=true,RefreshMetadata;public Action Interrupt;public int Root,Healthy,Bad;public bool Paged,Insert;
            static object FileEntry(string path){return new{path=path,type="file",size=0,modified="v1"};}
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
                token.ThrowIfCancellationRequested();object result;
                if(request.RequestUri.AbsolutePath.EndsWith("/download"))result=new{href="https://download.example/empty"};
                else if(request.RequestUri.AbsolutePath.EndsWith("/resources")){
                    string path=Uri.UnescapeDataString(request.RequestUri.Query.TrimStart('?').Split('&')[0].Substring(5));
                    if(request.RequestUri.Query.Contains("fields="))result=new{path=path,type="file",size=1,md5=Hash("x"),modified="v2"};
                    else if(path=="disk:/"){
                        Root++;var entries=new List<object>{new{path="disk:/bad",type="dir"},new{path="disk:/healthy",type="dir"}};
                        if(RefreshMetadata)entries.Add(new{path="disk:/refresh.bin",type="file",size=1,md5=Hash("y"),modified="v1"});
                        result=new{type="dir",_embedded=new{items=entries}};
                    }else if(path=="disk:/healthy"){Healthy++;result=new{type="dir",_embedded=new{items=new[]{FileEntry("disk:/healthy/ready.bin")}}};}
                    else if(path=="disk:/bad"){
                        Bad++;if(Interrupt!=null){Interrupt();token.ThrowIfCancellationRequested();}
                        int offset=Int32.Parse(request.RequestUri.Query.Split('&').Single(x=>x.StartsWith("offset=")).Substring(7));
                        if(Fail&&(!Paged||offset>0))return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                        IEnumerable<string> names=Paged?Enumerable.Range(0,1001).Select(i=>"disk:/bad/f"+i+".bin"):new[]{"disk:/bad/rescued.bin"};
                        if(Insert)names=new[]{"disk:/bad/added-first.bin"}.Concat(names);
                        result=new{type="dir",_embedded=new{items=names.Skip(offset).Take(1000).Select(FileEntry).ToArray()}};
                    }else throw new Exception("Unexpected listing in inventory test.");
                }else result=new{user=new{uid="inventory-test"}};
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Json.Serialize(result))});
            }
        }
        sealed class Downloads:HttpMessageHandler {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(new byte[0])});}
        }
        static Settings SettingsFor(string root,string name){var value=new Settings{UseApi=true,UseCachedApiList=true,ApiToken="test-secret",ApiFolder=Path.Combine(root,name+"-files"),ApiRequestIntervalMs=25};Directory.CreateDirectory(value.ApiFolder);return value;}
        static ApiEngine.InventoryCache Read(string data){return Json.Deserialize<ApiEngine.InventoryCache>(System.IO.File.ReadAllText(Directory.GetFiles(data,"inventory.json",SearchOption.AllDirectories).Single()));}
        static Snapshot Run(string data,Settings settings,Api api){Snapshot last=null;new ApiEngine(data,x=>last=x,api,new Downloads()).Run(settings,RunMode.Remaining);return last;}
        public static void Run(string root){
            foreach(bool metadata in new[]{false,true}){
                var settings=SettingsFor(root,"inventory-error-"+metadata);string data=Path.Combine(root,"inventory-error-data-"+metadata);
                if(metadata)System.IO.File.WriteAllText(Path.Combine(settings.ApiFolder,"refresh.bin"),"x");
                var first=Run(data,settings,new Api{RefreshMetadata=metadata});var cache=Read(data);
                Assert(first.Phase=="completed"&&first.ScanErrors==1&&System.IO.File.Exists(Path.Combine(settings.ApiFolder,"healthy","ready.bin")),"A failed folder blocked already discovered downloads.");
                Assert(!cache.Complete,"A folder failure was saved as a complete inventory, including after metadata refresh.");
                Assert(Json.Serialize(cache).Contains("disk:/bad"),"Failed folder path was lost.");
                var again=new Api();var second=Run(data,settings,again);Assert(second.ScanErrors==1&&!Read(data).Complete&&again.Bad==1&&again.Root==0&&again.Healthy==0,"Failed folder was lost, retried repeatedly in one scan, or caused successful folders to rescan.");
                var repaired=new Api{Fail=false};var third=Run(data,settings,repaired);cache=Read(data);
                Assert(third.ScanErrors==0&&cache.ScanErrors==0&&cache.Complete&&repaired.Root==0&&repaired.Healthy==0&&repaired.Bad==1,"Cached retry failed to clear unresolved enumeration errors.");
                Assert(System.IO.File.Exists(Path.Combine(settings.ApiFolder,"bad","rescued.bin")),"Recovered folder contents did not enter the download queue.");
            }
            foreach(int version in new[]{1,2}){
                var settings=SettingsFor(root,"inventory-legacy-"+version);string data=Path.Combine(root,"inventory-legacy-data-"+version);
                Run(data,settings,new Api());string path=Directory.GetFiles(data,"inventory.json",SearchOption.AllDirectories).Single();var old=Read(data);old.Version=version;old.Complete=true;old.ScanErrors=1;old.PendingFolders=new List<string>();old.ActiveFolder=null;
                var raw=Json.Deserialize<Dictionary<string,object>>(Json.Serialize(old));raw.Remove("FailedFolders");System.IO.File.WriteAllText(path,Json.Serialize(raw));
                var api=new Api{Fail=false};var last=Run(data,settings,api);
                Assert(api.Root==1&&last.ScanErrors==0&&Read(data).Complete&&System.IO.File.Exists(Path.Combine(settings.ApiFolder,"bad","rescued.bin")),"Legacy erroneous complete list was not rebuilt automatically.");
            }
            var cancelledSettings=SettingsFor(root,"inventory-stop");string cancelledData=Path.Combine(root,"inventory-stop-data");Run(cancelledData,cancelledSettings,new Api());Snapshot stopped=null;ApiEngine engine=null;
            var cancelled=new Api{Fail=false,Interrupt=()=>engine.Stop()};engine=new ApiEngine(cancelledData,x=>stopped=x,cancelled,new Downloads());engine.Run(cancelledSettings,RunMode.Remaining);
            Assert(stopped.Phase=="stopped"&&!Read(cancelledData).Complete,"Stop discarded the incomplete checkpoint.");
            Assert(Run(cancelledData,cancelledSettings,new Api{Fail=false}).ScanErrors==0&&Read(cancelledData).Complete,"Stopped folder could not be recovered on the next pass.");
            var paged=SettingsFor(root,"inventory-page");string pagedData=Path.Combine(root,"inventory-page-data");Directory.CreateDirectory(Path.Combine(paged.ApiFolder,"bad"));for(int i=0;i<1001;i++)System.IO.File.WriteAllBytes(Path.Combine(paged.ApiFolder,"bad","f"+i+".bin"),new byte[0]);
            Run(pagedData,paged,new Api{Paged=true});Assert(!Read(pagedData).Complete,"A failed later page was marked complete.");
            var restart=new Api{Fail=false,Paged=true,Insert=true};Run(pagedData,paged,restart);var repairedPage=Read(pagedData);
            Assert(repairedPage.Complete&&repairedPage.Files.Count==1003&&repairedPage.Files.Select(f=>f.Path).Distinct().Count()==1003&&restart.Root==0&&restart.Healthy==0&&restart.Bad==2,"Failed folder did not restart from page one without duplicates.");
            Assert(PassHistory.ShouldRepeat(new Snapshot{Phase="completed",Finished=true,ScanErrors=1},true,false),"An empty download queue prevented retry of incomplete enumeration.");
            Assert(!PassHistory.ShouldRepeat(new Snapshot{Phase="completed",Finished=true},true,false),"Complete empty inventories repeat forever.");
            var transition=new PassTransition();Assert(transition.Prepare(new Snapshot{Phase="completed",Finished=true,ScanErrors=1})>0,"Enumeration-only retries have no backoff.");
        }
    }
}
