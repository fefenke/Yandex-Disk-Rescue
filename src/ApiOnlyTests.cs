using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace DiskRescue {
    internal static class ApiOnlyTests {
        static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
        public static void Run(string root){Platform(root);Validation(root);Facade(root);}
        public static void Platform(string root){
            var status=new PrerequisiteStatus{Windows=true,Architecture=true,Framework=true};
            Assert(status.PlatformReady,"API startup still requires Cloud Files or the desktop client.");
            status.Windows=false;Assert(!status.PlatformReady,"Unsupported Windows accepted.");status.Windows=true;
            status.Architecture=false;Assert(!status.PlatformReady,"Unsupported architecture accepted.");status.Architecture=true;
            status.Framework=false;Assert(!status.PlatformReady,"Missing Framework accepted.");
        }
        public static void Validation(string root){
            var settings=new Settings{UseApi=false,Folder=root};
            Assert(ApiEngine.Validation(settings)=="token","Legacy UseApi=false bypassed API token validation.");
            settings.ApiToken="test-secret";Assert(ApiEngine.Validation(settings)=="destination","Legacy folder substituted for the API destination.");
            var engine=new Engine(Path.Combine(root,"validation-data"));settings.ApiToken="";engine.Run(settings,RunMode.Remaining);
            Assert(engine.Latest.Finished&&engine.Latest.Phase=="error"&&engine.Latest.Error.Contains("token"),"Legacy settings selected the native engine.");
        }
        sealed class Api:HttpMessageHandler {
            public bool Block;public readonly ManualResetEventSlim Entered=new ManualResetEventSlim();
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
                Entered.Set();if(Block)await Task.Delay(Timeout.Infinite,token);
                string body=request.RequestUri.AbsolutePath.EndsWith("/download")?"{\"href\":\"https://download.example/test\"}":request.RequestUri.AbsolutePath.EndsWith("/resources")?"{\"type\":\"dir\",\"_embedded\":{\"items\":[{\"type\":\"file\",\"path\":\"disk:/a/same.jpg\",\"size\":4,\"modified\":\"v1\",\"md5\":\"098f6bcd4621d373cade4e832627b4f6\"},{\"type\":\"file\",\"path\":\"disk:/b/same.jpg\",\"size\":4,\"modified\":\"v1\",\"md5\":\"098f6bcd4621d373cade4e832627b4f6\"}]}}":"{\"user\":{\"uid\":\"api-only\"}}";
                return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body)};
            }
        }
        sealed class Download:HttpMessageHandler {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){throw new Exception("A verified local file was downloaded again.");}
        }
        static Engine EngineFor(string data,Api api){
            var ctor=typeof(Engine).GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(string),typeof(ApiPacer),typeof(SignedLinkCache),typeof(HttpMessageHandler),typeof(HttpMessageHandler)},null);
            Assert(ctor!=null,"Missing API facade transport seam.");
            return (Engine)ctor.Invoke(new object[]{data,new ApiPacer(),new SignedLinkCache(),api,new Download()});
        }
        public static void Facade(string root){
            string folder=Path.Combine(root,"facade-files"),data=Path.Combine(root,"facade-data");
            Directory.CreateDirectory(Path.Combine(folder,"a"));Directory.CreateDirectory(Path.Combine(folder,"b"));
            string first=Path.Combine(folder,"a","same.jpg"),second=Path.Combine(folder,"b","same.jpg");File.WriteAllText(first,"test");File.WriteAllText(second,"test");
            string legacy;
            using(var history=new HistoryStore(data,folder)){history.Save(new DiskFile{Path=first,Length=4,Modified="legacy"},"cloud_error","old client evidence");legacy=Path.Combine(history.DirectoryPath,"history.jsonl");}
            byte[] old=File.ReadAllBytes(legacy);DateTime stamp=File.GetLastWriteTimeUtc(first);
            var settings=new Settings{UseApi=false,RestartClient=true,Folder=Path.Combine(root,"missing-client-folder"),ApiFolder=folder,ApiToken="test-secret",UseCachedApiList=false,ApiRequestIntervalMs=25};
            foreach(RunMode mode in new[]{RunMode.Remaining,RunMode.All}){
                var engine=EngineFor(data,new Api());engine.Run(settings,mode);var last=engine.Latest;
                Assert(engine.DataFolder==data&&last.Finished&&last.Phase=="completed"&&last.Available==2,"API facade did not verify same-name local files.");
                Assert(File.Exists(Path.Combine(last.Report,"folders.csv")),"API folder report missing.");
                Assert(last.ProgressChecked==last.Processed&&last.ProgressTotal==last.Selected,"Facade counters do not describe this pass.");
                Assert(File.ReadAllText(first)=="test"&&File.ReadAllText(second)=="test"&&File.GetLastWriteTimeUtc(first)==stamp,"API verification rewrote a valid local file.");
            }
            Assert(Convert.ToBase64String(old)==Convert.ToBase64String(File.ReadAllBytes(legacy)),"API pass rewrote client history.");
            var blocked=new Api{Block=true};var stopping=EngineFor(data,blocked);Task run=Task.Run(()=>stopping.Run(settings,RunMode.Remaining));
            try{Assert(blocked.Entered.Wait(5000),"Facade did not enter API request.");stopping.Stop();Assert(run.Wait(5000)&&stopping.Latest.Phase=="stopped","Facade Stop did not cancel API request.");}finally{stopping.Stop();run.Wait(5000);}
            var before=new Api();var cancelled=EngineFor(data,before);cancelled.Stop();cancelled.Run(settings,RunMode.Remaining);
            Assert(cancelled.Latest.Phase=="stopped"&&!before.Entered.IsSet,"Stop just before Run was lost.");
        }
    }
}
