using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace DiskRescue {
    internal static class ModePolicyTests {
        static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
        public static void Run(string root){Selection(root);Session(root);RefreshedErrors(root);MetadataChanged(root);ManualIntegrity(root);LegacyHistory(root);}
        public static void Selection(string root){
            var file=new DiskFile{Path="disk:/changed",Modified="v2",Length=4};
            var previous=new HistoryRecord{path=file.Path,modified="v1",length=4,status="failed"};
            Assert(!FileRules.Select(file,previous,RunMode.DownloadErrors),"A previous version's error selected the new cloud version.");
            Assert(!FileRules.Select(file,null,RunMode.DownloadErrors),"DownloadErrors selected a new file.");
            previous.modified=file.Modified;
            foreach(string status in new[]{"failed","timeout","cloud_error"}){previous.status=status;Assert(FileRules.Select(file,previous,RunMode.DownloadErrors),"DownloadErrors omitted a same-version error: "+status);}
            previous.status="cancelled_user";Assert(!FileRules.Select(file,previous,RunMode.DownloadErrors),"User cancellation became a download error.");
            Assert(!FileRules.Select(file,null,(RunMode)0)&&!FileRules.Select(file,null,(RunMode)10),"A retired numeric mode still executes a selection policy.");
            Assert(Enum.GetValues(typeof(RunMode)).Length==5&&(int)RunMode.All==1&&(int)RunMode.Remaining==7&&(int)RunMode.PartialFiles==8&&(int)RunMode.IntegrityFailures==9&&(int)RunMode.DownloadErrors==11,"The five supported mode identities changed.");
        }
        public static void Session(string root){
            foreach(bool refresh in new[]{false,true}){
                Assert(ScanSessionPolicy.NextMode(RunMode.DownloadErrors,refresh)==RunMode.DownloadErrors,"Refreshing expanded the selected DownloadErrors mode.");
                Assert(ScanSessionPolicy.NextMode(RunMode.All,refresh)==RunMode.Remaining,"All was not narrowed to Remaining for automatic retries.");
                Assert(ScanSessionPolicy.NextMode(RunMode.PartialFiles,refresh)==RunMode.PartialFiles,"Refreshing expanded PartialFiles.");
            }
            MethodInfo repeat=typeof(ScanSessionPolicy).GetMethod("ShouldRepeat");Assert(repeat!=null,"Session repeat decisions are not exposed by the shared policy.");
            var result=new Snapshot{Phase="completed",Finished=true,Selected=1,OtherErrors=1};
            Assert(!(bool)repeat.Invoke(null,new object[]{RunMode.IntegrityFailures,result,true,false}),"IntegrityFailures started a second automatic pass.");
            Assert((bool)repeat.Invoke(null,new object[]{RunMode.DownloadErrors,result,true,false}),"An ordinary error mode lost automatic retries.");
            Assert(!(bool)repeat.Invoke(null,new object[]{RunMode.DownloadErrors,result,true,true}),"Stop did not stop session retries.");
        }
        sealed class Api:HttpMessageHandler {
            public readonly System.Collections.Generic.List<string> Links=new System.Collections.Generic.List<string>();
            public string[] Names={"failed","changed","new","partial"};public bool NewMetadata;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
                string body;
                if(request.RequestUri.AbsolutePath.EndsWith("/download")){Links.Add(Uri.UnescapeDataString(request.RequestUri.Query.Split('&')[0].Substring(6)));body="{\"href\":\"https://download.example/mode\"}";}
                else if(request.RequestUri.AbsolutePath.EndsWith("/resources")){
                    string hash=SafeDownloadTests.Hash("test",false);
                    body=new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new{type="dir",_embedded=new{items=Names.Select(n=>new{type="file",path="disk:/"+n,size=4,md5=hash,modified=n=="changed"?"v2":"v1"}).ToArray()}});
                    if(!Uri.UnescapeDataString(request.RequestUri.Query).StartsWith("?path=disk:/&",StringComparison.Ordinal)){
                        string name=Uri.UnescapeDataString(request.RequestUri.Query.Split('&')[0].Substring(6));body=new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new{type="file",path=name,size=4,md5=hash,modified=NewMetadata||name.EndsWith("changed",StringComparison.Ordinal)?"v2":"v1"});
                    }
                }else body="{\"user\":{\"uid\":\"mode-test\"}}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body)});
            }
        }
        sealed class Download:HttpMessageHandler {
            public string Payload="test";
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Payload)});}
        }
        public static void MetadataChanged(string root){
            foreach(bool partial in new[]{false,true}){
                string folder=Path.Combine(root,"mode-metadata-change-"+partial),data=folder+"-data";Directory.CreateDirectory(folder);
                string hash=SafeDownloadTests.Hash("test",false),context="mode-test|disk:/|"+Path.GetFullPath(folder).ToUpperInvariant();
                using(var history=new HistoryStore(data,folder,context))history.SaveApi(new DiskFile{Path="disk:/failed",Modified="v1|"+hash,Length=4},"failed","old error");
                if(partial)File.WriteAllText(Path.Combine(folder,"failed.ydr-part"),"te");
                var api=new Api{Names=new[]{"failed"},NewMetadata=true};Snapshot last=null;
                new ApiEngine(data,s=>last=s,api,new Download()).Run(new Settings{UseApi=true,ApiToken="test-secret",ApiFolder=folder,UseCachedApiList=false,ApiRequestIntervalMs=25},RunMode.DownloadErrors);
                Assert(last.Phase=="completed"&&last.Downloaded==0&&api.Links.Count==0&&!File.Exists(Path.Combine(folder,"failed")),"A cloud version changed during link preparation but the error-only pass downloaded it.");
                if(partial)Assert(File.ReadAllText(Path.Combine(folder,"failed.ydr-part"))=="te","A newly excluded version moved partial bytes.");
                using(var history=new HistoryStore(data,folder,context))Assert(history.Find("disk:/failed").modified=="v1|"+hash&&history.Find("disk:/failed").message=="old error","Excluding a changed version overwrote prior error evidence.");
            }
        }
        public static void ManualIntegrity(string root){
            string folder=Path.Combine(root,"mode-manual-integrity"),data=folder+"-data";Snapshot last=null;
            var settings=new Settings{UseApi=true,ApiToken="test-secret",ApiFolder=folder,UseCachedApiList=false,ApiRequestIntervalMs=25,StreamsPerFile=1};
            new ApiEngine(data,s=>last=s,new Api{Names=new[]{"failed"}},new Download{Payload="evil"}).Run(settings,RunMode.Remaining);
            Assert(last.OtherErrors==1,"The integrity retry fixture did not record a mismatch.");
            for(int manual=0;manual<2;manual++){
                var api=new Api{Names=new[]{"failed"}};new ApiEngine(data,s=>last=s,api,new Download{Payload="evil"}).Run(settings,RunMode.IntegrityFailures);
                Assert(last.Selected==1&&api.Links.Count==1&&last.OtherErrors==1&&!ScanSessionPolicy.ShouldRepeat(RunMode.IntegrityFailures,last,true,false),"A manual integrity pass was blocked or permitted automatic repetition.");
            }
            new ApiEngine(data,s=>last=s,new Api{Names=new[]{"failed"}},new Download()).Run(settings,RunMode.IntegrityFailures);
            Assert(last.Downloaded==1&&last.OtherErrors==0,"A new manual integrity launch could not recover a valid payload.");
        }
        public static void RefreshedErrors(string root){
            string folder=Path.Combine(root,"mode-refresh"),data=folder+"-data";Directory.CreateDirectory(folder);
            string hash=SafeDownloadTests.Hash("test",false);
            using(var history=new HistoryStore(data,folder,"mode-test|disk:/|"+Path.GetFullPath(folder).ToUpperInvariant())){
                history.SaveApi(new DiskFile{Path="disk:/failed",Modified="v1|"+hash,Length=4},"timeout","");
                history.SaveApi(new DiskFile{Path="disk:/changed",Modified="v1|"+hash,Length=4},"failed","");
            }
            File.WriteAllText(Path.Combine(folder,"partial.ydr-part"),"te");
            var api=new Api();Snapshot last=null;var settings=new Settings{UseApi=true,ApiToken="test-secret",ApiFolder=folder,UseCachedApiList=false,ApiRequestIntervalMs=25,StreamsPerFile=1};
            new ApiEngine(data,s=>last=s,api,new Download()).Run(settings,RunMode.DownloadErrors);
            Assert(last.Phase=="completed"&&last.Selected==1&&last.Downloaded==1&&api.Links.SequenceEqual(new[]{"disk:/failed"}),"A refreshed error pass downloaded new or changed cloud files.");
            Assert(!File.Exists(Path.Combine(folder,"new"))&&!File.Exists(Path.Combine(folder,"changed"))&&File.ReadAllText(Path.Combine(folder,"partial.ydr-part"))=="te","The error-only pass modified an excluded file.");
            api=new Api();new ApiEngine(data,s=>last=s,api,new Download()).Run(settings,RunMode.PartialFiles);
            Assert(last.Selected==1&&last.Downloaded==1&&api.Links.SequenceEqual(new[]{"disk:/partial"}),"A refreshed partial-only pass started a new file.");
        }
        public static void LegacyHistory(string root){
            string data=Path.Combine(root,"mode-history");Directory.CreateDirectory(data);string path=Path.Combine(data,"passes.jsonl");
            string original="{\"Mode\":\"Continue\",\"Method\":\"Desktop\"}\n{\"Mode\":\"unknown-old-mode\",\"Method\":\"API\"}\n{\"Mode\":\"All\",\"Method\":\"API\"}\n";File.WriteAllText(path,original);
            using(var form=new MainForm(data)){
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;MethodInfo name=typeof(MainForm).GetMethod("PassModeName",flags);
                foreach(string legacy in new[]{"Continue","ErrorsAndNew","unknown-old-mode"})Assert((string)name.Invoke(form,new object[]{legacy})=="Ранее существовавший режим","A retired mode was presented as an executable mode.");
                typeof(MainForm).GetField("ru",flags).SetValue(form,false);
                Assert((string)name.Invoke(form,new object[]{"Continue"})=="Legacy pass mode","The legacy mode label was not localized.");
                Assert((string)name.Invoke(form,new object[]{"All"})=="All files","A supported mode lost its display name.");
            }
            Assert(PassHistory.Load(path).Select(p=>p.Mode).SequenceEqual(new[]{"Continue","unknown-old-mode","All"})&&File.ReadAllText(path)==original,"Reading legacy modes rewrote the history.");
        }
    }
}
