using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace DiskRescue {
    internal static class DestinationPathsTests {
        static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
        sealed class Api:HttpMessageHandler {
            public readonly Dictionary<string,string> Files=new Dictionary<string,string>(StringComparer.Ordinal);
            public readonly List<string> Links=new List<string>();
            object Entry(string path){return new{path=path,type="file",size=Files[path].Length,modified="v1",md5=SafeDownloadTests.Hash(Files[path],false)};}
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
                string body;
                if(request.RequestUri.AbsolutePath.EndsWith("/resources")){
                    string path=Uri.UnescapeDataString(request.RequestUri.Query.Split('&')[0].Substring(6));
                    body=new JavaScriptSerializer().Serialize(path=="disk:/"?(object)new{type="dir",_embedded=new{items=Files.Keys.Select(Entry).ToArray()}}:Entry(path));
                }else if(request.RequestUri.AbsolutePath.EndsWith("/download")){
                    string path=Uri.UnescapeDataString(request.RequestUri.Query.Split('&')[0].Substring(6));Links.Add(path);
                    body=new JavaScriptSerializer().Serialize(new{href="https://download.example/?path="+Uri.EscapeDataString(path)});
                }else body="{\"user\":{\"uid\":\"destination-test\"}}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body)});
            }
        }
        sealed class Download:HttpMessageHandler {
            public Api Api;public bool Fail;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
                string path=Uri.UnescapeDataString(request.RequestUri.Query.Substring(6));
                return Task.FromResult(new HttpResponseMessage(Fail?HttpStatusCode.ServiceUnavailable:HttpStatusCode.OK){Content=new StringContent(Fail?"":Api.Files[path])});
            }
        }
        static Snapshot Pass(string data,string folder,Api api,bool fail,bool cached=false){
            Snapshot last=null;new ApiEngine(data,s=>last=s,api,new Download{Api=api,Fail=fail}).Run(new Settings{UseApi=true,ApiToken="test-secret",ApiFolder=folder,UseCachedApiList=cached,ApiRequestIntervalMs=25,StreamsPerFile=1},RunMode.Remaining);return last;
        }
        public static void Run(string root){TreeConflicts(root);Refresh(root);PartialRefresh(root);InterruptedRefresh(root);Ambiguous(root);LegacyAndUntrusted(root);RetainedState(root);PhysicalDirectory(root);CachedResolution(root);}
        public static void CachedResolution(string root){
            foreach(bool locked in new[]{false,true}){
                string folder=Path.Combine(root,"cached-resolution-"+locked),data=folder+"-data";Directory.CreateDirectory(folder);
                string ambiguous=Path.Combine(folder,"a.bin.ydr-part");File.WriteAllText(ambiguous,"personal");
                var first=new Api();first.Files.Add("disk:/A.bin","UPPR");first.Files.Add("disk:/a.bin","lowr");
                Assert(Pass(data,folder,first,false).Catalog.All(f=>f.Status=="failed"),"Ambiguous fixture unexpectedly downloaded files.");
                // The user resolves the conflict by moving their bytes aside.
                string retained=Path.Combine(folder,"retained-personal");File.Move(ambiguous,retained);
                string cache=Directory.GetFiles(Path.Combine(data,"api-inventory"),"inventory.json",SearchOption.AllDirectories).Single();
                var next=new Api();next.Files.Add("disk:/A.bin","UPPR");next.Files.Add("disk:/a.bin","lowr");Snapshot last;
                if(locked){
                    using(var hold=new FileStream(cache,FileMode.Open,FileAccess.Read,FileShare.Read))last=Pass(data,folder,next,false,true);
                    Assert(last.Phase=="error"&&next.Links.Count==0,"A cached pass downloaded before persisting new destination ownership.");
                }else{
                    last=Pass(data,folder,next,false,true);Assert(last.Downloaded==2,"Resolved cached files were not downloaded.");
                    var third=new Api();third.Files.Add("disk:/A.bin","UPPR");third.Files.Add("disk:/a.bin","lowr");last=Pass(data,folder,third,false,true);
                    Assert(last.Phase=="completed"&&last.Available==2&&third.Links.Count==0,"A cached pass forgot resolved destination ownership and blocked completed files.");
                    Assert(File.ReadAllText(Path.Combine(folder,"A.bin"))=="UPPR"&&last.Catalog.Any(f=>File.ReadAllText(f.LocalPath)=="lowr"),"Case-sensitive file contents changed.");
                    using(var hold=new FileStream(cache,FileMode.Open,FileAccess.Read,FileShare.Read))last=Pass(data,folder,third,false,true);
                    Assert(last.Phase=="completed"&&last.Available==2,"An unchanged cached pass unnecessarily rewrote destination assignments.");
                }
                Assert(File.ReadAllText(retained)=="personal","Resolving the cached conflict damaged retained bytes.");
            }
        }
        public static void PhysicalDirectory(string root){
            string folder=Path.Combine(root,"directory-at-map"),data=folder+"-data";Directory.CreateDirectory(Path.Combine(folder,"a.bin.ydr-map"));
            string personal=Path.Combine(folder,"a.bin.ydr-map","personal.txt");File.WriteAllText(personal,"keep");
            var api=new Api();api.Files.Add("disk:/a.bin","test");api.Files.Add("disk:/safe.bin","test");Snapshot last=Pass(data,folder,api,false);
            Assert(last.Phase=="completed"&&api.Links.SequenceEqual(new[]{"disk:/safe.bin"})&&File.ReadAllText(personal)=="keep","A directory at a reserved state-file path was not rejected before download.");
        }
        public static void RetainedState(string root){
            string folder=Path.Combine(root,"retired-cloud-state"),data=folder+"-data";
            var first=new Api();first.Files.Add("disk:/a.bin","test");Pass(data,folder,first,false);
            string partial=Path.Combine(folder,"a.bin.ydr-download");File.WriteAllText(partial,"keep");
            var next=new Api();next.Files.Add("disk:/a.bin.ydr-download","test");next.Files.Add("disk:/safe.bin","test");Snapshot last=Pass(data,folder,next,false);
            Assert(last.Phase=="completed"&&next.Links.SequenceEqual(new[]{"disk:/safe.bin"})&&File.ReadAllText(partial)=="keep","A removed cloud entry's retained download state was reused by another file.");
        }
        public static void LegacyAndUntrusted(string root){
            string folder=Path.Combine(root,"legacy-destinations"),data=folder+"-data";
            var first=new Api();first.Files.Add("disk:/A.bin","UPPR");first.Files.Add("disk:/a.bin","lowr");Pass(data,folder,first,false);
            string path=Directory.GetFiles(Path.Combine(data,"api-inventory"),"inventory.json",SearchOption.AllDirectories).Single();var json=new JavaScriptSerializer();
            var cache=json.Deserialize<ApiEngine.InventoryCache>(File.ReadAllText(path));cache.DestinationAssignments=null;File.WriteAllText(path,json.Serialize(cache));
            var next=new Api();next.Files.Add("disk:/a.bin","lowr");next.Files.Add("disk:/A.bin","UPPR");Snapshot last=Pass(data,folder,next,false);
            Assert(last.Phase=="completed"&&next.Links.Count==0,"A complete legacy inventory lost its established case-sensitive assignments.");
            string outside=Path.Combine(root,"outside-destination.bin");File.WriteAllText(outside,"keep");
            cache=json.Deserialize<ApiEngine.InventoryCache>(File.ReadAllText(path));cache.DestinationAssignments["disk:/A.bin"]=outside;File.WriteAllText(path,json.Serialize(cache));
            var injected=new Api();injected.Files.Add("disk:/A.bin","UPPR");injected.Files.Add("disk:/a.bin","lowr");last=Pass(data,folder,injected,false,true);
            Assert(File.ReadAllText(outside)=="keep"&&last.Catalog.All(f=>f.LocalPath.StartsWith(Path.GetFullPath(folder)+"\\",StringComparison.OrdinalIgnoreCase)),"An untrusted cached destination escaped the selected root.");
        }
        public static void InterruptedRefresh(string root){
            string folder=Path.Combine(root,"interrupted-case-refresh"),data=folder+"-data";
            var first=new Api();first.Files.Add("disk:/a.bin","lowr");Pass(data,folder,first,false);
            string path=Directory.GetFiles(Path.Combine(data,"api-inventory"),"inventory.json",SearchOption.AllDirectories).Single();
            var json=new JavaScriptSerializer();var cache=json.Deserialize<ApiEngine.InventoryCache>(File.ReadAllText(path));
            Assert(cache.DestinationAssignments!=null&&cache.DestinationAssignments.ContainsKey("disk:/a.bin"),"The checkpoint does not retain established assignments independently of the scan list.");
            cache.Files=new List<DiskFile>{new DiskFile{Path="disk:/A.bin",Destination=Path.Combine(folder,"A.bin"),Length=4,SizeKnown=true,Category="other",Md5=SafeDownloadTests.Hash("UPPR",false),Modified="v1|"+SafeDownloadTests.Hash("UPPR",false)}};
            cache.Complete=false;cache.PendingFolders=new List<string>{"disk:/"};cache.ActiveFolder=null;cache.NextOffset=0;File.WriteAllText(path,json.Serialize(cache));
            var resumed=new Api();resumed.Files.Add("disk:/A.bin","UPPR");resumed.Files.Add("disk:/a.bin","lowr");Snapshot last=Pass(data,folder,resumed,false,true);
            Assert(last.Phase=="completed"&&File.ReadAllText(Path.Combine(folder,"a.bin"))=="lowr"&&resumed.Links.SequenceEqual(new[]{"disk:/A.bin"}),"A provisional destination stole the old path after interrupted scanning.");
        }
        public static void TreeConflicts(string root){
            foreach(string scenario in new[]{"file-directory","service-file","service-directory"}){
                string folder=Path.Combine(root,scenario),data=folder+"-data";Directory.CreateDirectory(folder);
                string parent=scenario=="file-directory"?"A":"a.bin";
                string child=scenario=="file-directory"?"a/child.txt":scenario=="service-file"?"a.bin.ydr-download/child.txt":"a.bin.ydr-segments/plan.json";
                string original=scenario=="file-directory"?Path.Combine(folder,"A"):Path.Combine(folder,child.Replace('/','\\'));
                Directory.CreateDirectory(Path.GetDirectoryName(original));File.WriteAllText(original,"keep");
                var api=new Api();api.Files.Add("disk:/"+parent,"test");api.Files.Add("disk:/"+child,"test");api.Files.Add("disk:/safe.bin","test");
                Snapshot last=Pass(data,folder,api,false);
                Assert(last.Phase=="completed"&&api.Links.SequenceEqual(new[]{"disk:/safe.bin"}),scenario+": a conflicting path reached the download pipeline.");
                Assert(File.ReadAllText(original)=="keep"&&File.ReadAllText(Path.Combine(folder,"safe.bin"))=="test",scenario+": existing bytes changed or unrelated file was blocked.");
                Assert(last.Catalog.Count(f=>f.Status=="failed")==2,scenario+": conflicts did not remain visible as per-file failures.");
            }
        }
        public static void Refresh(string root){
            string folder=Path.Combine(root,"case-refresh"),data=folder+"-data";
            var first=new Api();first.Files.Add("disk:/a.bin","lowr");Assert(Pass(data,folder,first,false).Downloaded==1,"Initial case fixture failed.");
            string original=BrowserCatalog.Load(data,true,folder,"disk:/").Single().LocalPath;
            var second=new Api();second.Files.Add("disk:/A.bin","UPPR");second.Files.Add("disk:/a.bin","lowr");
            Snapshot last=Pass(data,folder,second,false);var catalog=BrowserCatalog.Load(data,true,folder,"disk:/");
            Assert(last.Phase=="completed"&&catalog.Single(f=>f.Path=="a.bin").LocalPath==original,"Refresh reassigned the existing lowercase file to a different path.");
            Assert(File.ReadAllText(original)=="lowr"&&File.ReadAllText(catalog.Single(f=>f.Path=="A.bin").LocalPath)=="UPPR","Refresh mixed the bytes of case-sensitive cloud identities.");
            Assert(second.Links.SequenceEqual(new[]{"disk:/A.bin"}),"Refresh unnecessarily downloaded the existing lowercase file.");
            var third=new Api();third.Files.Add("disk:/a.bin","lowr");third.Files.Add("disk:/A.bin","UPPR");Pass(data,folder,third,false);
            Assert(third.Links.Count==0&&BrowserCatalog.Load(data,true,folder,"disk:/").Single(f=>f.Path=="a.bin").LocalPath==original,"Repeated refresh changed an established assignment.");
        }
        public static void PartialRefresh(string root){
            string folder=Path.Combine(root,"partial-case-refresh"),data=folder+"-data";
            var first=new Api();first.Files.Add("disk:/a.bin","lowr");Pass(data,folder,first,false);
            string destination=Path.Combine(folder,"a.bin");File.Delete(destination);
            var file=new DiskFile{Path="disk:/a.bin",Destination=destination,Length=4,Md5=SafeDownloadTests.Hash("lowr",false),Modified="v1|"+SafeDownloadTests.Hash("lowr",false)};
            string identity;
            using(var history=new HistoryStore(data,folder,"destination-test|disk:/|"+Path.GetFullPath(folder).ToUpperInvariant()))identity="ydr-context:"+Path.GetFileName(history.DirectoryPath)+"\n"+CloudIntegrity.Version(file);
            RangeMap map=RangeMap.Open(destination,identity,4,1,CancellationToken.None);
            using(Stream stream=map.Writer(0)){byte[] bytes=Encoding.ASCII.GetBytes("lo");stream.Write(bytes,0,bytes.Length);}
            var next=new Api();next.Files.Add("disk:/A.bin","UPPR");next.Files.Add("disk:/a.bin","lowr");Pass(data,folder,next,true);
            Assert(RangeMap.Matches(destination,identity,4)&&RangeMap.Progress(destination)==2&&File.ReadAllText(destination+".ydr-download").StartsWith("lo",StringComparison.Ordinal),"A newly listed case collision moved or reset existing partial data.");
            Assert(!Directory.Exists(Path.Combine(folder,"Сохранённые прежние файлы")),"A refresh archived unchanged partial data because its local assignment changed.");
        }
        public static void Ambiguous(string root){
            string folder=Path.Combine(root,"ambiguous-case");Directory.CreateDirectory(folder);
            string partial=Path.Combine(folder,"a.bin.ydr-part");File.WriteAllText(partial,"keep");
            var files=new List<DiskFile>{new DiskFile{Path="disk:/A.bin"},new DiskFile{Path="disk:/a.bin"},new DiskFile{Path="disk:/safe.bin"}};
            ApiEngine.AssignDestinations(files,folder);
            Assert(files.Take(2).All(f=>f.Status=="failed"&&!String.IsNullOrEmpty(f.Message)),"Ambiguous existing partial data was assigned to one of two cloud files.");
            Assert(files[2].Status=="pending"&&File.ReadAllText(partial)=="keep","Ambiguous-path handling modified data or blocked an unrelated file.");
        }
    }
}
