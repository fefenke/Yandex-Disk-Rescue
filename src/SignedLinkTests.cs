using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace DiskRescue {
    static class SignedLinkTests {
        const string Original="abcdefghijkl",Changed="mnopqrstuvwx";
        static void Assert(bool value,string message){if(!value)throw new Exception(message);}
        static string Digest(string value){using(MD5 hash=MD5.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.ASCII.GetBytes(value))).Replace("-","").ToLowerInvariant();}
        sealed class Api:HttpMessageHandler {
            public string MetadataPayload=Original,MetadataVersion="v1",Link="https://download.example/lease-a";
            public int MetadataCalls,LinkCalls,MetadataCode,MetadataDelay;public bool MissingDigest,OverrideSize,ChangeOnRefresh;public object SizeOverride;public Action<int> OnMetadata;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
                string json;
                if(request.RequestUri.AbsolutePath.EndsWith("/download")){LinkCalls++;json=new JavaScriptSerializer().Serialize(new{href=Link});}
                else if(request.RequestUri.AbsolutePath.EndsWith("/resources")){
                    string path=Uri.UnescapeDataString(request.RequestUri.Query.Split('&')[0].Substring(6));
                    if(path=="disk:/a.txt"){
                        MetadataCalls++;if(OnMetadata!=null)OnMetadata(MetadataCalls);if(ChangeOnRefresh&&MetadataCalls>1){MetadataPayload=Changed;MetadataVersion="v2";}if(MetadataDelay>0)await Task.Delay(MetadataDelay,token).ConfigureAwait(false);
                        if(MetadataCode!=0)return new HttpResponseMessage((HttpStatusCode)MetadataCode);
                        json=new JavaScriptSerializer().Serialize(new{path=path,type="file",size=OverrideSize?SizeOverride:(object)MetadataPayload.Length,md5=MissingDigest?"":Digest(MetadataPayload),modified=MetadataVersion});
                    }else json=new JavaScriptSerializer().Serialize(new{type="dir",_embedded=new{items=new[]{new{path="disk:/a.txt",type="file",size=Original.Length,md5=Digest(Original),modified="v1"}}}});
                }else json="{\"user\":{\"uid\":\"link-account\"}}";
                return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json)};
            }
        }
        sealed class Download:HttpMessageHandler {
            public string Payload=Original;public long Requested=-1,RejectFrom;public int Calls,Code,Step=2;public bool BreakEveryAttempt,ZeroOnSecond,RejectOldOnly;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
                Assert(request.Headers.Authorization==null,"Cached link leaked OAuth.");int call=Interlocked.Increment(ref Calls);long requested=request.Headers.Range==null?0:request.Headers.Range.Ranges.Single().From.Value;Requested=requested;
                if(Code!=0&&requested>=RejectFrom&&(!RejectOldOnly||request.RequestUri.AbsolutePath=="/lease-a"))return Task.FromResult(new HttpResponseMessage((HttpStatusCode)Code));
                bool range=request.Headers.Range!=null;long end=range?request.Headers.Range.Ranges.Single().To??Payload.Length-1:Payload.Length-1;
                HttpResponseMessage response=new HttpResponseMessage(range?HttpStatusCode.PartialContent:HttpStatusCode.OK){Content=new StreamContent(new Body(Payload,(int)requested,(int)(end-requested+1),BreakEveryAttempt?(ZeroOnSecond&&call==2?0:Step):-1))};
                response.Content.Headers.ContentLength=end-requested+1;if(range)response.Content.Headers.ContentRange=new ContentRangeHeaderValue(requested,end,Payload.Length);
                return Task.FromResult(response);
            }
        }
        sealed class Body:Stream {
            readonly string payload;readonly int start,length,failAfter;int position;
            public Body(string payload,int start,int length,int failAfter){this.payload=payload;this.start=start;this.length=length;this.failAfter=failAfter;}
            public override Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken token){
                token.ThrowIfCancellationRequested();if(failAfter>=0&&position>=failAfter)throw new IOException("Synthetic connection interruption");
                int n=Math.Min(count,length-position);if(failAfter>=0)n=Math.Min(n,failAfter-position);for(int i=0;i<n;i++)buffer[offset+i]=(byte)payload[start+position+i];position+=n;return Task.FromResult(n);
            }
            public override int Read(byte[] b,int o,int c){return ReadAsync(b,o,c,CancellationToken.None).GetAwaiter().GetResult();}
            public override bool CanRead{get{return true;}}public override bool CanWrite{get{return false;}}public override bool CanSeek{get{return false;}}public override long Length{get{return length;}}public override long Position{get{return position;}set{throw new NotSupportedException();}}
            public override void Flush(){}public override long Seek(long p,SeekOrigin o){throw new NotSupportedException();}public override void SetLength(long l){throw new NotSupportedException();}public override void Write(byte[] b,int o,int c){throw new NotSupportedException();}
        }
        static Settings SettingsFor(string root,string name){return new Settings{UseApi=true,UseCachedApiList=false,ApiFolder=Path.Combine(root,name+"-files"),ApiToken="test-secret",ApiRequestIntervalMs=25,LinkTimeoutSeconds=1,IdleSeconds=1};}
        static void Seed(string data,Settings settings){
            Directory.CreateDirectory(settings.ApiFolder);string destination=Path.Combine(settings.ApiFolder,"a.txt");File.WriteAllText(destination+".ydr-part","ab");
            using(HistoryStore history=new HistoryStore(data,settings.ApiFolder,"link-account|disk:/|"+settings.ApiFolder.ToUpperInvariant()))history.SaveApi(new DiskFile{Path="disk:/a.txt",Destination=destination,Length=Original.Length,Modified="v1|"+Digest(Original),Md5=Digest(Original),DownloadedBytes=2},"timeout","");
        }
        public static void Run(string root){
            int malformed=0;foreach(object size in new object[]{null,true,1.5,-1,"12"}){
                Settings invalidSettings=SettingsFor(root,"invalid-size-"+(malformed++));string invalidData=Path.Combine(root,"invalid-data-"+malformed);Seed(invalidData,invalidSettings);
                Api invalidApi=new Api{OverrideSize=true,SizeOverride=size};Download unused=new Download();new ApiEngine(invalidData,x=>{},invalidApi,unused).Run(invalidSettings,RunMode.PartialFiles);
                Assert(unused.Calls==0&&invalidApi.LinkCalls==0&&File.ReadAllText(Path.Combine(invalidSettings.ApiFolder,"a.txt.ydr-part"))=="ab","Malformed metadata size discarded saved data.");
            }
            Settings settings=SettingsFor(root,"changed-link-version");string data=Path.Combine(root,"changed-link-data");Seed(data,settings);
            Api api=new Api{MetadataPayload=Changed,MetadataVersion="v2"};Download download=new Download{Payload=Changed};Snapshot last=null;
            new ApiEngine(data,x=>last=x,api,download).Run(settings,RunMode.PartialFiles);
            Assert(api.MetadataCalls==1,"Partial data was reused without fresh cloud metadata.");
            Assert(download.Requested==0&&last.Downloaded==1&&File.ReadAllText(Path.Combine(settings.ApiFolder,"a.txt"))==Changed,"Changed cloud content was mixed with old partial bytes.");
            settings.UseCachedApiList=true;Download alreadyDone=new Download{Payload=Changed};
            new ApiEngine(data,x=>last=x,new Api(),alreadyDone).Run(settings,RunMode.Remaining);
            Assert(alreadyDone.Calls==0&&last.Available==1,"Refreshed metadata was not preserved in the saved inventory.");
            foreach(string failure in new[]{"timeout","http","missing-digest"}){
                settings=SettingsFor(root,"metadata-"+failure);data=Path.Combine(root,"metadata-data-"+failure);Seed(data,settings);
                api=new Api{MetadataDelay=failure=="timeout"?10000:0,MetadataCode=failure=="http"?404:0,MissingDigest=failure=="missing-digest"};download=new Download();
                new ApiEngine(data,x=>last=x,api,download).Run(settings,RunMode.PartialFiles);
                Assert(download.Calls==0&&api.LinkCalls==0&&File.ReadAllText(Path.Combine(settings.ApiFolder,"a.txt.ydr-part"))=="ab",failure+": failed metadata validation consumed or discarded partial data.");
            }
            foreach(bool segmented in new[]{false,true}){
                settings=SettingsFor(root,segmented?"cached-segments":"cached-single");data=Path.Combine(root,segmented?"cached-segment-data":"cached-single-data");
                if(segmented){Seed(data,settings);string destination=Path.Combine(settings.ApiFolder,"a.txt");File.WriteAllText(destination+".ydr-part","");Directory.CreateDirectory(destination+".ydr-segments");File.WriteAllText(Path.Combine(destination+".ydr-segments","plan.json"),new JavaScriptSerializer().Serialize(new{Identity="disk:/a.txt\nv1|"+Digest(Original)+"\n"+Original.Length,Offset=0,Length=Original.Length,Parts=2}));settings.StreamsPerFile=2;}
                SignedLinkCache cache=new SignedLinkCache();ApiPacer pacer=new ApiPacer();api=new Api();download=new Download{BreakEveryAttempt=true,Step=segmented?1:2};
                new ApiEngine(data,x=>last=x,api,download,pacer,cache).Run(settings,RunMode.Remaining);
                Assert(last.Downloaded==0&&download.Calls==(segmented?6:3),"Initial progressive interruptions did not retain partial data: segmented="+segmented+", calls="+download.Calls+", downloaded="+last.Downloaded+", phase="+last.Phase+", error="+last.Error);
                string interrupted=Path.Combine(settings.ApiFolder,"a.txt.ydr-download");
                using(FileStream torn=new FileStream(interrupted,FileMode.Open,FileAccess.Write)){torn.Position=segmented?3:6;torn.Write(Encoding.ASCII.GetBytes("BAD"),0,3);}File.AppendAllText(Path.Combine(settings.ApiFolder,"a.txt.ydr-map"),"{torn record");
                Api nextApi=new Api();Download nextDownload=new Download();
                new ApiEngine(data,x=>last=x,nextApi,nextDownload,pacer,cache).Run(settings,RunMode.PartialFiles);
                Assert(nextApi.MetadataCalls==1&&nextApi.LinkCalls==0&&last.Downloaded==1&&File.ReadAllText(Path.Combine(settings.ApiFolder,"a.txt"))==Original,"Working signed link was not reused safely across passes.");
                foreach(string file in Directory.EnumerateFiles(data,"*",SearchOption.AllDirectories))Assert(!File.ReadAllText(file).Contains("https://download.example/lease-a"),"Signed link leaked into persisted data.");
            }
            settings=SettingsFor(root,"corrupt-confirmed-prefix");data=Path.Combine(root,"corrupt-confirmed-prefix-data");Seed(data,settings);
            string damaged=Path.Combine(settings.ApiFolder,"a.txt.ydr-part");File.WriteAllText(damaged,"");
            using(DurablePartial output=new DurablePartial(damaged,"disk:/a.txt\nv1|"+Digest(Original)+"\n"+Original.Length,CancellationToken.None)){
                byte[] bytes=Encoding.ASCII.GetBytes(Original);output.Write(bytes,0,4);output.Flush();output.Write(bytes,4,4);
            }
            using(FileStream corrupt=new FileStream(damaged,FileMode.Open,FileAccess.Write)){corrupt.Position=5;corrupt.WriteByte(0);}
            download=new Download();new ApiEngine(data,x=>last=x,new Api(),download).Run(settings,RunMode.PartialFiles);
            Assert(download.Requested==4&&last.Downloaded==1&&File.ReadAllText(Path.Combine(settings.ApiFolder,"a.txt"))==Original,"Corrupt confirmed extent was not redownloaded from its last valid boundary.");
            foreach(int rejected in new[]{0,401,403,404,410}){
                settings=SettingsFor(root,"rejected-"+rejected);data=Path.Combine(root,"rejected-data-"+rejected);SignedLinkCache cache=new SignedLinkCache();ApiPacer pacer=new ApiPacer();
                new ApiEngine(data,x=>last=x,new Api(),new Download{BreakEveryAttempt=true,ZeroOnSecond=rejected==0},pacer,cache).Run(settings,RunMode.Remaining);
                if(rejected!=0){Api expiredApi=new Api();Download expired=new Download{Code=rejected};new ApiEngine(data,x=>last=x,expiredApi,expired,pacer,cache).Run(settings,RunMode.PartialFiles);bool refresh=rejected!=404;Assert(expiredApi.LinkCalls==(refresh?1:0)&&expired.Calls==(refresh?2:1)&&last.Downloaded==0,"Rejected link refresh was missing, unbounded or retried HTTP 404.");}
                Api freshApi=new Api{Link="https://download.example/lease-b"};download=new Download();new ApiEngine(data,x=>last=x,freshApi,download,pacer,cache).Run(settings,RunMode.PartialFiles);
                Assert(freshApi.LinkCalls==1&&last.Downloaded==1&&download.Requested==(rejected==0?2:6),"Failed signed link was reused or saved bytes were lost on refresh.");
            }
            foreach(int rejected in new[]{401,403,410}){
                settings=SettingsFor(root,"refresh-current-"+rejected);data=Path.Combine(root,"refresh-current-data-"+rejected);
                var cache=new SignedLinkCache();var pacer=new ApiPacer();
                new ApiEngine(data,x=>last=x,new Api(),new Download{BreakEveryAttempt=true},pacer,cache).Run(settings,RunMode.Remaining);
                api=new Api{Link="https://download.example/lease-b"};download=new Download{Code=rejected,RejectOldOnly=true};
                new ApiEngine(data,x=>last=x,api,download,pacer,cache).Run(settings,RunMode.PartialFiles);
                Assert(api.LinkCalls==1&&api.MetadataCalls==2&&download.Calls==2&&download.Requested==6&&last.Downloaded==1,"Expired URL did not resume in the same pass with one refresh and version validation.");
            }
            settings=SettingsFor(root,"refresh-changed");data=Path.Combine(root,"refresh-changed-data");Seed(data,settings);
            api=new Api{ChangeOnRefresh=true};download=new Download{Code=403};
            new ApiEngine(data,x=>last=x,api,download).Run(settings,RunMode.PartialFiles);
            Assert(api.MetadataCalls==2&&api.LinkCalls==1&&download.Calls==1&&last.Downloaded==0&&!File.Exists(Path.Combine(settings.ApiFolder,"a.txt")),"URL refresh mixed cloud versions or requested a link after version change.");
            settings=SettingsFor(root,"refresh-stop");data=Path.Combine(root,"refresh-stop-data");Seed(data,settings);api=new Api();download=new Download{Code=403};
            ApiEngine stopping=new ApiEngine(data,x=>last=x,api,download);api.OnMetadata=n=>{if(n==2)stopping.Stop();};stopping.Run(settings,RunMode.PartialFiles);
            Assert(api.LinkCalls==1&&download.Calls==1&&last.Phase=="stopped","Stop during refresh started another link/download request.");
            settings=SettingsFor(root,"refresh-multiple-ranges");data=Path.Combine(root,"refresh-multiple-data");Seed(data,settings);
            string segmentedDestination=Path.Combine(settings.ApiFolder,"a.txt");File.WriteAllText(segmentedDestination+".ydr-part","");Directory.CreateDirectory(segmentedDestination+".ydr-segments");
            File.WriteAllText(Path.Combine(segmentedDestination+".ydr-segments","plan.json"),new JavaScriptSerializer().Serialize(new{Identity="disk:/a.txt\nv1|"+Digest(Original)+"\n"+Original.Length,Offset=0,Length=Original.Length,Parts=2}));settings.StreamsPerFile=2;
            var multiCache=new SignedLinkCache();var multiPacer=new ApiPacer();
            new ApiEngine(data,x=>last=x,new Api(),new Download{BreakEveryAttempt=true,Step=1},multiPacer,multiCache).Run(settings,RunMode.Remaining);
            api=new Api{Link="https://download.example/lease-b"};download=new Download{Code=403,RejectOldOnly=true,RejectFrom=6};
            new ApiEngine(data,x=>last=x,api,download,multiPacer,multiCache).Run(settings,RunMode.PartialFiles);
            Assert(api.LinkCalls==1&&download.Calls==3&&download.Requested==9&&last.Downloaded==1&&File.ReadAllText(segmentedDestination)==Original,"Shared refresh redownloaded finished ranges, lost progress or requested multiple links.");
            // Recover a valid new-version local file after a crash before inventory/history updates.
            settings=SettingsFor(root,"completed-before-inventory");data=Path.Combine(root,"completed-before-inventory-data");Directory.CreateDirectory(settings.ApiFolder);File.WriteAllText(Path.Combine(settings.ApiFolder,"a.txt"),Changed);
            api=new Api{MetadataPayload=Changed,MetadataVersion="v2"};download=new Download();new ApiEngine(data,x=>last=x,api,download).Run(settings,RunMode.Remaining);
            Assert(api.MetadataCalls==1&&api.LinkCalls==0&&download.Calls==0&&last.Available==1,"A valid local file was downloaded again after stale inventory recovery.");
            SignedLinkCache direct=new SignedLinkCache();var lease=direct.Offer("scope/a","v1",new Uri("https://download.example/private"));Assert(direct.Get("scope/a","v1")==null,"Unproven signed link was reused.");direct.Confirm(lease);Assert(direct.Get("other/a","v1")==null&&direct.Get("scope/A","v1")==null,"Link cache crossed account/path scope.");
            direct.Invalidate(lease);direct.Confirm(lease);Assert(direct.Get("scope/a","v1")==null,"A concurrent segment revived an invalidated link.");
        }
    }
}
