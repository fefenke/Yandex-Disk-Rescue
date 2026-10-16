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
            public int StartupFailures,TransientListingFailures,StartupCalls;
            public string Account="test-account";
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation){
                Assert(request.RequestUri.Host=="cloud-api.yandex.net","Token sent outside API host.");Assert(request.Headers.Authorization!=null&&request.Headers.Authorization.Parameter=="test-secret","OAuth header missing.");
                if(Unauthorized)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
                if(request.RequestUri.AbsolutePath.EndsWith("/disk")){StartupCalls++;if(StartupFailures-->0)throw new HttpRequestException("Synthetic connection failure");}
                string json;
                if(request.RequestUri.AbsolutePath.EndsWith("/download"))json="{\"href\":\"https://download.example/"+Uri.EscapeDataString(request.RequestUri.Query)+"\"}";
                else if(request.RequestUri.AbsolutePath.EndsWith("/resources")){
                    string requested=Uri.UnescapeDataString(request.RequestUri.Query.Split('&')[0].Substring(6));
                    if(requested!="disk:/"&&requested!="disk:/unreadable"&&requested!="disk:/Another")return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(new JavaScriptSerializer().Serialize(new{path=requested,type="file",size=4,md5=Md5("test"),modified="2026-01-01T00:00:00Z"}))});
                    ListingCalls++;
                    if(TransientListingFailures-->0)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
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
            public bool RequestedFirstPage,InsertFirst,Nested;public int RootRequests,CompletedRequests;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation){
                string json;
                if(request.RequestUri.AbsolutePath.EndsWith("/resources")){
                    string requested=Uri.UnescapeDataString(request.RequestUri.Query.Split('&')[0].Substring(6));
                    if(Nested&&requested=="disk:/"){RootRequests++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(new JavaScriptSerializer().Serialize(new{type="dir",_embedded=new{items=new[]{new{path="disk:/active",type="dir"},new{path="disk:/done",type="dir"}}}}))});}
                    if(Nested&&requested=="disk:/done"){CompletedRequests++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(new JavaScriptSerializer().Serialize(new{type="dir",_embedded=new{items=new[]{new{path="disk:/done/done.txt",type="file",size=0,modified="v"}}}}))});}
                    bool first=request.RequestUri.Query.Contains("offset=0");RequestedFirstPage|=first;
                    if(!first&&Interrupt!=null){Interrupt();cancellation.ThrowIfCancellationRequested();}
                    int offset=Int32.Parse(request.RequestUri.Query.TrimStart('?').Split('&').Single(x=>x.StartsWith("offset=",StringComparison.Ordinal)).Substring(7));
                    string prefix=Nested?"disk:/active/":"disk:/";var paths=Enumerable.Range(0,1001).Select(i=>prefix+"f"+i+".txt");if(InsertFirst)paths=new[]{prefix+"added-before-offset.txt"}.Concat(paths);
                    object[] entries=paths.Skip(offset).Take(1000).Select(path=>(object)new{path=path,type="file",size=0,modified="v"}).ToArray();json=new JavaScriptSerializer().Serialize(new{type="dir",_embedded=new{items=entries}});
                }else json="{\"user\":{\"uid\":\"paged-account\"}}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json)});
            }
        }
        sealed class ResumeMock:HttpMessageHandler {
            public bool IgnoreRange,WrongRange;public long Requested=-1;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation){
                Assert(request.Headers.Authorization==null,"Resume leaked OAuth.");
                Requested=request.Headers.Range==null?-1:request.Headers.Range.Ranges.First().From.Value;
                HttpResponseMessage response=new HttpResponseMessage(Requested>=0&&!IgnoreRange?HttpStatusCode.PartialContent:HttpStatusCode.OK);
                response.Content=new ByteArrayContent(Encoding.UTF8.GetBytes(Requested>=0&&!IgnoreRange?"st":"test"));
                if(response.StatusCode==HttpStatusCode.PartialContent)response.Content.Headers.ContentRange=new System.Net.Http.Headers.ContentRangeHeaderValue(WrongRange?0:Requested,3,4);
                return Task.FromResult(response);
            }
        }
        static void ResumeTests(string root){
            for(int variant=0;variant<5;variant++){
                string folder=Path.Combine(root,"resume-"+variant),data=Path.Combine(root,"resume-data-"+variant);Directory.CreateDirectory(Path.Combine(folder,"a"));
                string destination=Path.Combine(folder,"a","same.jpg");File.WriteAllText(destination+".ydr-part","te");
                DiskFile file=new DiskFile{Path="disk:/a/same.jpg",Destination=destination,Length=4,Modified=(variant==3?"different":"2026-01-01T00:00:00Z|"+Md5("test")),DownloadedBytes=2};
                using(HistoryStore history=new HistoryStore(data,folder,"test-account|disk:/|"+folder.ToUpperInvariant()))history.SaveApi(file,"timeout","");
                ResumeMock mock=new ResumeMock{IgnoreRange=variant==1,WrongRange=variant==2};Snapshot result=null;
                new ApiEngine(data,x=>result=x,new ApiMock(),mock).Run(new Settings{UseApi=true,UseCachedApiList=false,ApiFolder=folder,ApiToken="test-secret",IdleSeconds=5},RunMode.PartialFiles);
                Assert(result.Selected==1,"Partial mode included unrelated files.");
                if(variant==2){Assert(!File.Exists(destination)&&RangeMap.Progress(destination)==2&&File.ReadAllText(destination+".ydr-download").StartsWith("te",StringComparison.Ordinal),"Invalid range corrupted existing partial data.");}
                else{Assert(File.ReadAllText(destination)=="test"&&result.Downloaded==1,"Resume or full-response fallback failed.");Assert(mock.Requested==(variant==3?-1:2),"Resume ignored cloud version or byte offset.");}
            }
        }
        sealed class PipelineApiMock:HttpMessageHandler {
            public int DelayMs,Active,Peak;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation){
                if(request.RequestUri.AbsolutePath.EndsWith("/download")){
                    int active=Interlocked.Increment(ref Active);int previous;do{previous=Peak;if(previous>=active)break;}while(Interlocked.CompareExchange(ref Peak,active,previous)!=previous);
                    try{await Task.Delay(DelayMs,cancellation);return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"href\":\"https://download.example/file\"}")};}finally{Interlocked.Decrement(ref Active);}
                }
                string text=request.RequestUri.AbsolutePath.EndsWith("/resources")?new JavaScriptSerializer().Serialize(new{type="dir",_embedded=new{items=Enumerable.Range(0,30).Select(i=>new{path="disk:/pipeline/f"+i+".txt",type="file",size=4,md5=Md5("test"),modified="v"}).ToArray()}}):"{\"user\":{\"uid\":\"pipeline-account\"}}";
                return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(text)};
            }
        }
        sealed class PipelineDownloadMock:HttpMessageHandler {
            public int DelayMs,Active,Peak;public Action Started;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation){
                Assert(request.Headers.Authorization==null,"Pipeline sent OAuth to download host.");
                int active=Interlocked.Increment(ref Active);int previous;do{previous=Peak;if(previous>=active)break;}while(Interlocked.CompareExchange(ref Peak,active,previous)!=previous);
                try{if(Started!=null)Started();await Task.Delay(DelayMs,cancellation);return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("test")};}finally{Interlocked.Decrement(ref Active);}
            }
        }
        public static void PipelineTests(string root){
            // Each 1.2-second stage fits its own 2-second budget; their sum does not.
            // Leave scheduling margin on shared CI machines without merging budgets.
            for(int variant=0;variant<4;variant++){
                string folder=Path.Combine(root,"pipeline-"+variant),data=Path.Combine(root,"pipeline-data-"+variant);Directory.CreateDirectory(folder);
                PipelineApiMock api=new PipelineApiMock{DelayMs=variant==4?2000:variant==0?1200:200};PipelineDownloadMock downloads=new PipelineDownloadMock{DelayMs=variant==0?1200:variant==3?350:1800};
                Snapshot last=null;ApiEngine engine=new ApiEngine(data,x=>last=x,api,downloads);if(variant==2)downloads.Started=engine.Stop;
                engine.Run(new Settings{UseApi=true,UseCachedApiList=false,ApiFolder=folder,ApiToken="test-secret",LinkTimeoutSeconds=variant==4?3:variant==0?2:1,IdleSeconds=variant==0?2:3,ApiLinkConcurrency=variant==4?30:variant==3?2:7,ApiDownloadConcurrency=variant==4?30:variant==3?3:20},RunMode.Remaining);
                Assert(api.Peak<=(variant==4?30:variant==3?2:ApiEngine.LinkConcurrency)&&api.Peak>1&&downloads.Peak<=(variant==4?30:variant==3?3:ApiEngine.DownloadConcurrency),"Configured pipeline limits exceeded.");
                if(variant==4)Assert(api.Peak>20&&downloads.Peak>20,"30 + 30 settings remained capped at the old hardcoded limits.");
                Assert(api.Active==0&&downloads.Active==0,"Stop or completion left requests running.");
                if(variant==2){Assert(last.Phase=="stopped"&&last.Finished,"Pipeline Stop did not finish cleanly.");using(HistoryStore history=new HistoryStore(data,folder,"pipeline-account|disk:/|"+folder.ToUpperInvariant()))Assert(history.Records.Count>0&&history.Records.Values.All(x=>x.status=="cancelled_user"),"Stopped pipeline lost or misclassified history.");}
                else{Assert(last.Phase=="completed"&&last.Downloaded==30&&last.Processed==30,"Pipeline variant "+variant+": phase="+last.Phase+", downloaded="+last.Downloaded+", processed="+last.Processed+"; "+String.Join(", ",last.Catalog.Where(f=>!f.Ready).Take(5).Select(f=>f.Status))+". Queue wait consumed API timeout, download reused API budget, or pipeline lost files.");Assert(Directory.GetFiles(Path.Combine(folder,"pipeline")).Length==30,"Pipeline left missing or partial files.");if(variant==1)Assert(downloads.Peak>ApiEngine.LinkConcurrency,"Link and download queues did not run independently.");}
            }
        }
        public static void ScanResumeTests(string root){
            foreach(bool nested in new[]{false,true}){
                Settings settings=new Settings{UseApi=true,ApiToken="test-secret",UseCachedApiList=true,RemoteFolder="disk:/"};Snapshot last=null;ApiEngine engine=null;
                string data=Path.Combine(root,"resume-history-"+nested);settings.ApiFolder=Path.Combine(root,"resume-downloads-"+nested);string active=nested?Path.Combine(settings.ApiFolder,"active"):settings.ApiFolder;Directory.CreateDirectory(active);
                for(int i=0;i<1001;i++)File.WriteAllBytes(Path.Combine(active,"f"+i+".txt"),new byte[0]);
                if(nested){Directory.CreateDirectory(Path.Combine(settings.ApiFolder,"done"));File.WriteAllBytes(Path.Combine(settings.ApiFolder,"done","done.txt"),new byte[0]);}
                PagedMock page=new PagedMock{Nested=nested};engine=new ApiEngine(data,x=>last=x,page,new DownloadMock());page.Interrupt=()=>engine.Stop();engine.Run(settings,RunMode.Continue);
                Assert(last.Phase=="stopped"&&last.Total==(nested?1001:1000),"Partial scan was not checkpointed.");
                string checkpoint=Directory.GetFiles(data,"inventory.json",SearchOption.AllDirectories).Single();var serializer=new JavaScriptSerializer{MaxJsonLength=128*1024*1024};ApiEngine.InventoryCache saved=serializer.Deserialize<ApiEngine.InventoryCache>(File.ReadAllText(checkpoint));Assert(!saved.Complete&&saved.NextOffset==1000,"Scan checkpoint lost its diagnostic offset.");
                page=new PagedMock{Nested=nested,InsertFirst=true};engine=new ApiEngine(data,x=>last=x,page,new DownloadMock());engine.Run(settings,RunMode.Continue);int expected=nested?1003:1002;
                Assert(last.Phase=="completed"&&last.Total==expected&&page.RequestedFirstPage,"Interrupted folder did not restart from its first page or missed the inserted file.");
                Assert(File.Exists(Path.Combine(active,"added-before-offset.txt")),"File inserted before the saved offset was not recovered.");
                Assert(!nested||page.RootRequests==0&&page.CompletedRequests==0,"Resuming one folder rescanned completed folders.");
                saved=serializer.Deserialize<ApiEngine.InventoryCache>(File.ReadAllText(checkpoint));Assert(saved.Files.Count==expected&&saved.Files.Select(f=>f.Path).Distinct(StringComparer.Ordinal).Count()==expected,"Restarting the folder duplicated saved entries.");
            }
        }
        public static void Run(string root){
            TransferRetryTests.Run(root);
            SegmentTests.Run(root);
            PipelineTests(root);
            ResumeTests(root);
            string folder=Path.Combine(root,"api-downloads"),data=Path.Combine(root,"api-history");Directory.CreateDirectory(folder);
            Settings s=new Settings{UseApi=true,UseCachedApiList=false,ApiFolder=folder,ApiToken="test-secret",IdleSeconds=5};
            Assert(ApiEngine.Validation(new Settings{UseApi=true})=="token","Missing token accepted.");Assert(ApiEngine.Validation(new Settings{UseApi=true,ApiToken="x"})=="destination","Missing destination accepted.");
            Assert(!new JavaScriptSerializer().Serialize(s).Contains("test-secret"),"Token serialized in settings.");
            DiskFile retryFile=new DiskFile{Path="disk:/retry.jpg",Modified="v",Length=10,DownloadedBytes=0};HistoryRecord retryRecord=new HistoryRecord{path=retryFile.Path,modified="v",length=10,status="cloud_error"};
            Assert(FileRules.Select(retryFile,retryRecord,RunMode.ErrorsAndZero)&&!FileRules.Select(retryFile,retryRecord,RunMode.NewFiles),"Combined retry or new-file filter selected incorrectly.");
            retryRecord.status="timeout";Assert(FileRules.Select(retryFile,retryRecord,RunMode.ErrorsAndZero)&&!FileRules.Select(retryFile,retryRecord,RunMode.CloudErrors),"Zero download missing from combined retry.");
            retryFile.DownloadedBytes=5;Assert(FileRules.Select(retryFile,retryRecord,RunMode.ErrorsAndZero),"Partial timeout missing from incomplete downloads.");
            Assert(FileRules.Select(retryFile,null,RunMode.NewFiles)&&!FileRules.Select(retryFile,null,RunMode.ErrorsAndZero),"New file counted as a prior failure.");
            Assert(FileRules.Select(retryFile,null,RunMode.Remaining)&&FileRules.Select(retryFile,retryRecord,RunMode.Remaining),"All remaining skipped untested or failed files.");retryFile.Local=true;Assert(!FileRules.Select(retryFile,retryRecord,RunMode.Remaining),"All remaining included a valid local file.");retryFile.Local=false;
            retryRecord.status="failed";Assert(FileRules.Select(retryFile,retryRecord,RunMode.ProblemFiles),"Automatic retry missed an integrity failure.");retryFile.Local=true;Assert(!FileRules.Select(retryFile,retryRecord,RunMode.ProblemFiles),"Automatic retry selected a local file.");
            Snapshot repeatSnapshot=new Snapshot{Phase="completed",Finished=true,Selected=5,Downloaded=2,Available=3,Total=7,CloudErrors=1,CloudErrorsThisPass=1};
            Assert(PassHistory.ShouldRepeat(repeatSnapshot,true,false)&&!PassHistory.ShouldRepeat(repeatSnapshot,true,true)&&!PassHistory.ShouldRepeat(repeatSnapshot,false,false),"Repeat ignored Stop or the checkbox.");
            repeatSnapshot.RecoveryRequired=true;Assert(!PassHistory.ShouldRepeat(repeatSnapshot,true,false),"Repeat ignored uncertain cancellation.");repeatSnapshot.RecoveryRequired=false;repeatSnapshot.Phase="error";Assert(!PassHistory.ShouldRepeat(repeatSnapshot,true,false),"Repeat retried a fatal error.");repeatSnapshot.Phase="completed";repeatSnapshot.Selected=0;Assert(PassHistory.ShouldRepeat(repeatSnapshot,true,false),"Empty initial pass failed to retry existing problem files.");
            repeatSnapshot.Phase="error";repeatSnapshot.RetryableError=true;Assert(PassHistory.ShouldRepeat(repeatSnapshot,true,false)&&!PassHistory.ShouldRepeat(repeatSnapshot,true,true),"Transient startup error interrupted automatic retry or ignored Stop.");repeatSnapshot.RetryableError=false;Assert(!PassHistory.ShouldRepeat(repeatSnapshot,true,false),"Fatal error was retried.");repeatSnapshot.Phase="completed";
            string passHistoryPath=Path.Combine(root,"passes.jsonl");PassHistory.Append(passHistoryPath,PassResult.From(repeatSnapshot,DateTime.UtcNow,true,RunMode.ProblemFiles));PassResult passResult=PassHistory.Load(passHistoryPath).Single();Assert(passResult.Downloaded==2&&passResult.Remaining==4&&passResult.Errors==1,"Pass statistics were lost.");
            Assert(FileRules.Category("disk:/Фото/фото \"1\".jpg")=="photos"&&FileRules.Category("disk:/Фото/a|b.png")=="photos","Cloud filename rejected by category detection.");
            bool rejected=false;try{ApiEngine.LocalPath(folder,"disk:/../outside.jpg");}catch(IOException){rejected=true;}Assert(rejected,"Traversal accepted.");
            Snapshot last=null;DownloadMock downloads=new DownloadMock();ApiEngine engine=new ApiEngine(data,x=>last=x,new ApiMock(),downloads);engine.Run(s,RunMode.Continue);
            Assert(last.Finished&&last.Phase=="completed"&&last.Total==7&&last.Downloaded==4,"API pass results incorrect.");Assert(last.CloudErrorsThisPass==1&&last.Deferred==1&&last.OtherErrors==1,"Timeout, HTTP error or integrity failure missing.");Assert(downloads.Peak<=ApiEngine.DownloadConcurrency&&downloads.Peak>1,"Download concurrency incorrect.");
            System.Collections.Generic.List<BrowserFile> savedBrowser=BrowserCatalog.Load(data,true,folder,"disk:/");Assert(savedBrowser.Count==7&&savedBrowser.Count(f=>f.Ready)==4,"Offline dashboard ignored saved list/history.");Assert(savedBrowser.Single(f=>f.Path=="Case.JPG").LocalPath!=savedBrowser.Single(f=>f.Path=="case.JPG").LocalPath,"Dashboard mixed case-colliding download locations.");
            Assert(File.Exists(Path.Combine(folder,"a","same.jpg"))&&File.Exists(Path.Combine(folder,"b","same.jpg")),"Same-name files collided.");Assert(Directory.GetFiles(folder,"*",SearchOption.AllDirectories).Count(x=>Path.GetFileName(x).StartsWith("case",StringComparison.OrdinalIgnoreCase)&&!x.EndsWith(".ydr-part"))==2,"Case-sensitive cloud files collided.");
            Assert(!File.Exists(Path.Combine(folder,"bad.jpg")),"Corrupt download promoted to final file.");
            string verifiedPath=Path.Combine(folder,"a","same.jpg");FileInfo verifiedInfo=new FileInfo(verifiedPath);
            string progressPath=Path.Combine(root,"hash-progress.bin");byte[] progressData=new byte[3*1024*1024];File.WriteAllBytes(progressPath,progressData);string expectedHash;using(System.Security.Cryptography.MD5 md5=System.Security.Cryptography.MD5.Create())expectedHash=BitConverter.ToString(md5.ComputeHash(progressData)).Replace("-","");long lastBytes=-1;int progressCalls=0;
            Assert(ApiEngine.Verified(progressPath,progressData.Length,expectedHash,CancellationToken.None,delegate(long done,long total){Assert(done>=lastBytes&&done<=total&&total==progressData.Length,"Hash progress was invalid.");lastBytes=done;progressCalls++;}),"Progress-enabled hash changed verification result.");Assert(lastBytes==progressData.Length&&progressCalls>=4,"Hash progress did not report initial and intermediate bytes.");
            using(CancellationTokenSource cancelHash=new CancellationTokenSource()){bool cancelledHash=false;try{ApiEngine.Verified(progressPath,progressData.Length,expectedHash,cancelHash.Token,delegate(long done,long total){if(done>0)cancelHash.Cancel();});}catch(OperationCanceledException){cancelledHash=true;}Assert(cancelledHash,"Hash progress ignored cancellation.");}
            DiskFile verifiedFile=new DiskFile{Path="disk:/a/same.jpg",Destination=verifiedPath,Length=verifiedInfo.Length,Modified="v1"};
            HistoryRecord verifiedRecord=new HistoryRecord{path=verifiedFile.Path,length=verifiedFile.Length,modified="v1",status="success",localWriteTicks=verifiedInfo.LastWriteTimeUtc.Ticks};
            Assert(ApiEngine.PreviouslyVerified(verifiedFile,verifiedRecord),"Unchanged verified file was not recognized.");
            verifiedRecord.localWriteTicks=null;Assert(!ApiEngine.PreviouslyVerified(verifiedFile,verifiedRecord),"Legacy history bypassed integrity verification.");
            verifiedRecord.localWriteTicks=verifiedInfo.LastWriteTimeUtc.Ticks+1;Assert(!ApiEngine.PreviouslyVerified(verifiedFile,verifiedRecord),"Changed local timestamp bypassed verification.");
            verifiedRecord.localWriteTicks=verifiedInfo.LastWriteTimeUtc.Ticks;verifiedRecord.modified="v2";Assert(!ApiEngine.PreviouslyVerified(verifiedFile,verifiedRecord),"Changed cloud version bypassed verification.");
            Assert(!File.ReadAllText(Path.Combine(last.Report,"summary.json")).Contains("test-secret"),"Token leaked into report.");
            engine=new ApiEngine(data,x=>last=x,new ApiMock(),new DownloadMock());engine.Run(s,RunMode.ZeroDownload);Assert(last.Selected==1&&last.Processed==1,"Incomplete retry did not exclude a blocked integrity failure.");
            engine=new ApiEngine(data,x=>last=x,new ApiMock(),new DownloadMock());engine.Run(s,RunMode.Continue);Assert(last.Skipped==7&&last.Selected==0&&last.ProgressChecked==7&&last.ProgressTotal==7,"Continue repeated previous attempts or reset cumulative progress.");
            using(HistoryStore history=new HistoryStore(data,folder,"case-test")){DiskFile a=new DiskFile{Path="disk:/A.jpg",Length=1,Modified="v"},b=new DiskFile{Path="disk:/a.jpg",Length=1,Modified="v"};history.SaveApi(a,"success","");history.SaveApi(b,"timeout","");Assert(history.Records.Count==2,"API history lost case-sensitive identity.");}
            engine=new ApiEngine(data,x=>last=x,new ApiMock{Unauthorized=true},new DownloadMock());engine.Run(s,RunMode.Continue);engine.Stop();Assert(last.Phase=="error"&&last.Error.Contains("token"),"Invalid OAuth was not reported.");
            engine=new ApiEngine(data,x=>last=x,new ApiMock{ListingFailure=401},new DownloadMock());engine.Run(s,RunMode.Continue);Assert(last.Phase=="error"&&last.Total==7&&last.Error.Contains("token")&&File.Exists(Path.Combine(last.Report,"summary.json")),"Partial scan report hid the original error.");
            engine=new ApiEngine(data,x=>last=x,new ApiMock{ListingFailure=404},new DownloadMock());engine.Run(s,RunMode.All);Assert(last.Phase=="completed"&&last.ScanErrors==1&&last.Processed>0,"Unreadable folder blocked all remaining downloads.");
            ApiMock temporary=new ApiMock{StartupFailures=1,TransientListingFailures=1};engine=new ApiEngine(data,x=>last=x,temporary,new DownloadMock());engine.Run(s,RunMode.All);Assert(last.Phase=="completed"&&temporary.StartupCalls==2&&temporary.ListingCalls==2&&last.ScanErrors==0,"Temporary API error interrupted connection or scanning.");
            engine=new ApiEngine(data,x=>{last=x;if(x.Phase=="api_retry")engine.Stop();},new ApiMock{ListingFailure=503},new DownloadMock());engine.Run(s,RunMode.All);Assert(last.Phase=="stopped"&&last.Total==7,"Stop did not interrupt scan retry at its current position.");
            engine=new ApiEngine(data,x=>last=x,new ApiMock{SpecialNames=true},new DownloadMock());engine.Run(s,RunMode.Continue);Assert(last.Phase=="completed"&&last.Total==9&&last.Downloaded==2,"Special cloud names stopped the pass: phase="+last.Phase+", total="+last.Total+", downloaded="+last.Downloaded+", deferred="+last.Deferred);Assert(File.Exists(ApiEngine.LocalPath(folder,"disk:/Фото/фото \"1\".jpg"))&&File.Exists(ApiEngine.LocalPath(folder,"disk:/Фото/a|b.png")),"Special cloud names were not saved safely.");
            ManualResetEvent running=new ManualResetEvent(false);engine=new ApiEngine(data,x=>{last=x;if(x.Phase=="running")running.Set();},new ApiMock(),new DownloadMock());Task run=Task.Run(()=>engine.Run(s,RunMode.All));Assert(running.WaitOne(5000),"API stop test never started.");engine.Stop();Assert(run.Wait(5000)&&last.Phase=="stopped"&&last.Active==0,"Stop left active requests.");running.Dispose();
            TokenVault.Save(data,"test-secret");Assert(TokenVault.Load(data)=="test-secret","Encrypted token cannot be loaded.");Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(data,"api-token.bin"))).Contains("test-secret"),"Token stored in plaintext.");TokenVault.Save(data,"");
            string cacheData=Path.Combine(root,"cache-tests");s.ApiFolder=Path.Combine(root,"cache-downloads");Directory.CreateDirectory(s.ApiFolder);s.UseCachedApiList=true;
            ApiMock mock=new ApiMock();engine=new ApiEngine(cacheData,x=>last=x,mock,new DownloadMock());engine.Run(s,RunMode.Continue);Assert(last.Phase=="completed"&&mock.ListingCalls==1,"First cached run did not scan.");
            mock=new ApiMock{SpecialNames=true};engine=new ApiEngine(cacheData,x=>last=x,mock,new DownloadMock());engine.Run(s,RunMode.Continue);Assert(last.Total==7&&mock.ListingCalls==0,"Saved-list mode rescanned cloud folders.");
            s.UseCachedApiList=false;mock=new ApiMock{SpecialNames=true};engine=new ApiEngine(cacheData,x=>last=x,mock,new DownloadMock());engine.Run(s,RunMode.Continue);Assert(last.Total==9&&mock.ListingCalls==1,"Refresh did not discover new files.");
            s.UseCachedApiList=true;mock=new ApiMock{Account="another-account"};engine=new ApiEngine(cacheData,x=>last=x,mock,new DownloadMock());engine.Run(s,RunMode.Continue);Assert(mock.ListingCalls==1&&last.Total==7,"Different account reused cached inventory.");
            s.RemoteFolder="disk:/Another";mock=new ApiMock();engine=new ApiEngine(cacheData,x=>last=x,mock,new DownloadMock());engine.Run(s,RunMode.Continue);Assert(mock.ListingCalls==1,"Different cloud folder reused cached inventory.");
            ScanResumeTests(root);
        }
    }
}
