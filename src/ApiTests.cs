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
    public static class ApiTests {
        static void Assert(bool value,string message){if(!value)throw new Exception(message);}
        static string Md5(string text){using(MD5 md5=MD5.Create())return BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();}
        sealed class ApiMock:HttpMessageHandler {
            public bool Unauthorized;
            public int ListingFailure;
            public bool SpecialNames;
            public int ListingCalls;
            public string Account="test-account";
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation){
                Assert(request.RequestUri.Host=="cloud-api.yandex.net","Token sent outside API host.");Assert(request.Headers.Authorization!=null&&request.Headers.Authorization.Parameter=="test-secret","OAuth header missing.");
                if(Unauthorized)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
                string json;
                if(request.RequestUri.AbsolutePath.EndsWith("/download"))json="{\"href\":\"https://download.example/"+Uri.EscapeDataString(request.RequestUri.Query)+"\"}";
                else if(request.RequestUri.AbsolutePath.EndsWith("/resources")){
                    ListingCalls++;
                    if(ListingFailure!=0&&Uri.UnescapeDataString(request.RequestUri.Query).Contains("disk:/unreadable"))return Task.FromResult(new HttpResponseMessage((HttpStatusCode)ListingFailure));
                    string[] names={"a/same.jpg","b/same.jpg","Case.JPG","case.JPG","bad.jpg","stall.mov","server.png"};
                    object[] entries=names.Select(n=>(object)new{path="disk:/"+n,type="file",size=4,md5=Md5("test"),modified="2026-01-01T00:00:00Z"}).ToArray();
                    if(ListingFailure!=0)entries=entries.Concat(new object[]{new{path="disk:/unreadable",type="dir"}}).ToArray();
                    if(SpecialNames)entries=entries.Concat(new object[]{new{path="disk:/Фото/фото \"1\".jpg",type="file",size=4,md5=Md5("test"),modified="v"},new{path="disk:/Фото/a|b.png",type="file",size=4,md5=Md5("test"),modified="v"}}).ToArray();
                    json=new JavaScriptSerializer().Serialize(new{type="dir",_embedded=new{items=entries}});
                }else json=new JavaScriptSerializer().Serialize(new{user=new{uid=Account}});
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json)});
            }
        }
        sealed class DownloadMock:HttpMessageHandler {
            int active;public int Peak;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation){
                Assert(request.Headers.Authorization==null,"OAuth leaked to signed download URL.");int now=Interlocked.Increment(ref active);Peak=Math.Max(Peak,now);
                try{string url=Uri.UnescapeDataString(request.RequestUri.AbsoluteUri);if(url.Contains("stall.mov"))await Task.Delay(10000,cancellation);else await Task.Delay(200,cancellation);if(url.Contains("server.png"))return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(url.Contains("bad.jpg")?"evil":"test")};}finally{Interlocked.Decrement(ref active);}
            }
        }
        sealed class PagedMock:HttpMessageHandler {
            public Action Interrupt;
            public bool RequestedFirstPage;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation){
                string json;
                if(request.RequestUri.AbsolutePath.EndsWith("/resources")){
                    bool first=request.RequestUri.Query.Contains("offset=0");RequestedFirstPage|=first;
                    if(!first&&Interrupt!=null){Interrupt();cancellation.ThrowIfCancellationRequested();}
                    object[] entries=Enumerable.Range(first?0:1000,first?1000:1).Select(i=>(object)new{path="disk:/f"+i+".txt",type="file",size=0,modified="v"}).ToArray();json=new JavaScriptSerializer().Serialize(new{type="dir",_embedded=new{items=entries}});
                }else json="{\"user\":{\"uid\":\"paged-account\"}}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json)});
            }
        }
        public static void Run(string root){
            string folder=Path.Combine(root,"api-downloads"),data=Path.Combine(root,"api-history");Directory.CreateDirectory(folder);
            Settings s=new Settings{UseApi=true,UseCachedApiList=false,ApiFolder=folder,ApiToken="test-secret",IdleSeconds=1};
            Assert(ApiEngine.Validation(new Settings{UseApi=true})=="token","Missing token accepted.");Assert(ApiEngine.Validation(new Settings{UseApi=true,ApiToken="x"})=="destination","Missing destination accepted.");
            Assert(!new JavaScriptSerializer().Serialize(s).Contains("test-secret"),"Token serialized in settings.");
            Assert(FileRules.Category("disk:/Фото/фото \"1\".jpg")=="photos"&&FileRules.Category("disk:/Фото/a|b.png")=="photos","Cloud filename rejected by category detection.");
            bool rejected=false;try{ApiEngine.LocalPath(folder,"disk:/../outside.jpg");}catch(IOException){rejected=true;}Assert(rejected,"Traversal accepted.");
            Snapshot last=null;DownloadMock downloads=new DownloadMock();ApiEngine engine=new ApiEngine(data,x=>last=x,new ApiMock(),downloads);engine.Run(s,RunMode.Continue);
            Assert(last.Finished&&last.Phase=="completed"&&last.Total==7&&last.Downloaded==4,"API pass results incorrect.");Assert(last.CloudErrorsThisPass==1&&last.Deferred==2,"Timeout, HTTP error or integrity failure missing.");Assert(downloads.Peak<=5&&downloads.Peak>1,"Download concurrency incorrect.");
            Assert(File.Exists(Path.Combine(folder,"a","same.jpg"))&&File.Exists(Path.Combine(folder,"b","same.jpg")),"Same-name files collided.");Assert(Directory.GetFiles(folder,"*",SearchOption.AllDirectories).Count(x=>Path.GetFileName(x).StartsWith("case",StringComparison.OrdinalIgnoreCase)&&!x.EndsWith(".ydr-part"))==2,"Case-sensitive cloud files collided.");
            Assert(!File.Exists(Path.Combine(folder,"bad.jpg")),"Corrupt download promoted to final file.");
            Assert(!File.ReadAllText(Path.Combine(last.Report,"summary.json")).Contains("test-secret"),"Token leaked into report.");
            engine=new ApiEngine(data,x=>last=x,new ApiMock(),new DownloadMock());engine.Run(s,RunMode.ZeroDownload);Assert(last.Selected==1&&last.Processed==1,"Zero retry selected untested/partial/server-error files.");
            engine=new ApiEngine(data,x=>last=x,new ApiMock(),new DownloadMock());engine.Run(s,RunMode.Continue);Assert(last.Skipped==7&&last.Selected==0&&last.ProgressChecked==7&&last.ProgressTotal==7,"Continue repeated previous attempts or reset cumulative progress.");
            using(HistoryStore history=new HistoryStore(data,folder,"case-test")){DiskFile a=new DiskFile{Path="disk:/A.jpg",Length=1,Modified="v"},b=new DiskFile{Path="disk:/a.jpg",Length=1,Modified="v"};history.SaveApi(a,"success","");history.SaveApi(b,"timeout","");Assert(history.Records.Count==2,"API history lost case-sensitive identity.");}
            engine=new ApiEngine(data,x=>last=x,new ApiMock{Unauthorized=true},new DownloadMock());engine.Run(s,RunMode.Continue);Assert(last.Phase=="error"&&last.Error.Contains("token"),"Invalid OAuth was not reported.");
            engine=new ApiEngine(data,x=>last=x,new ApiMock{ListingFailure=401},new DownloadMock());engine.Run(s,RunMode.Continue);Assert(last.Phase=="error"&&last.Total==7&&last.Error.Contains("token")&&File.Exists(Path.Combine(last.Report,"summary.json")),"Partial scan report hid the original error.");
            engine=new ApiEngine(data,x=>last=x,new ApiMock{ListingFailure=500},new DownloadMock());engine.Run(s,RunMode.All);Assert(last.Phase=="completed"&&last.ScanErrors==1&&last.Processed>0,"Unreadable folder blocked all remaining downloads.");
            engine=new ApiEngine(data,x=>last=x,new ApiMock{SpecialNames=true},new DownloadMock());engine.Run(s,RunMode.Continue);Assert(last.Phase=="completed"&&last.Total==9&&last.Downloaded==2,"Special cloud names stopped the pass.");Assert(File.Exists(ApiEngine.LocalPath(folder,"disk:/Фото/фото \"1\".jpg"))&&File.Exists(ApiEngine.LocalPath(folder,"disk:/Фото/a|b.png")),"Special cloud names were not saved safely.");
            ManualResetEvent running=new ManualResetEvent(false);engine=new ApiEngine(data,x=>{last=x;if(x.Phase=="running")running.Set();},new ApiMock(),new DownloadMock());Task run=Task.Run(()=>engine.Run(s,RunMode.All));Assert(running.WaitOne(5000),"API stop test never started.");engine.Stop();Assert(run.Wait(5000)&&last.Phase=="stopped"&&last.Active==0,"Stop left active requests.");running.Dispose();
            TokenVault.Save(data,"test-secret");Assert(TokenVault.Load(data)=="test-secret","Encrypted token cannot be loaded.");Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(data,"api-token.bin"))).Contains("test-secret"),"Token stored in plaintext.");TokenVault.Save(data,"");
            string cacheData=Path.Combine(root,"cache-tests");s.ApiFolder=Path.Combine(root,"cache-downloads");Directory.CreateDirectory(s.ApiFolder);s.UseCachedApiList=true;
            ApiMock mock=new ApiMock();engine=new ApiEngine(cacheData,x=>last=x,mock,new DownloadMock());engine.Run(s,RunMode.Continue);Assert(last.Phase=="completed"&&mock.ListingCalls==1,"First cached run did not scan.");
            mock=new ApiMock{SpecialNames=true};engine=new ApiEngine(cacheData,x=>last=x,mock,new DownloadMock());engine.Run(s,RunMode.Continue);Assert(last.Total==7&&mock.ListingCalls==0,"Saved-list mode rescanned cloud folders.");
            s.UseCachedApiList=false;mock=new ApiMock{SpecialNames=true};engine=new ApiEngine(cacheData,x=>last=x,mock,new DownloadMock());engine.Run(s,RunMode.Continue);Assert(last.Total==9&&mock.ListingCalls==1,"Refresh did not discover new files.");
            s.UseCachedApiList=true;mock=new ApiMock{Account="another-account"};engine=new ApiEngine(cacheData,x=>last=x,mock,new DownloadMock());engine.Run(s,RunMode.Continue);Assert(mock.ListingCalls==1&&last.Total==7,"Different account reused cached inventory.");
            s.RemoteFolder="disk:/Another";mock=new ApiMock();engine=new ApiEngine(cacheData,x=>last=x,mock,new DownloadMock());engine.Run(s,RunMode.Continue);Assert(mock.ListingCalls==1,"Different cloud folder reused cached inventory.");
            string resumeData=Path.Combine(root,"resume-history");s.ApiFolder=Path.Combine(root,"resume-downloads");Directory.CreateDirectory(s.ApiFolder);s.RemoteFolder="disk:/";for(int i=0;i<1001;i++)File.WriteAllBytes(Path.Combine(s.ApiFolder,"f"+i+".txt"),new byte[0]);
            PagedMock page=new PagedMock();engine=new ApiEngine(resumeData,x=>last=x,page,new DownloadMock());page.Interrupt=()=>engine.Stop();engine.Run(s,RunMode.Continue);Assert(last.Phase=="stopped"&&last.Total==1000,"Partial scan was not checkpointed.");
            string checkpoint=Directory.GetFiles(resumeData,"inventory.json",SearchOption.AllDirectories).Single();ApiEngine.InventoryCache saved=new JavaScriptSerializer{MaxJsonLength=128*1024*1024}.Deserialize<ApiEngine.InventoryCache>(File.ReadAllText(checkpoint));Assert(!saved.Complete&&saved.NextOffset==1000,"Scan checkpoint lost pagination offset.");
            page=new PagedMock();engine=new ApiEngine(resumeData,x=>last=x,page,new DownloadMock());engine.Run(s,RunMode.Continue);Assert(last.Phase=="completed"&&last.Total==1001&&!page.RequestedFirstPage,"Scan restarted instead of resuming.");
        }
    }
}
