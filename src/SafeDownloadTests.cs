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
    static class SafeDownloadTests {
        internal const string Content="abcdefghijkl",Wrong="mnopqrstuvwx";
        internal static string Hash(string text,bool sha){using(HashAlgorithm h=sha?(HashAlgorithm)SHA256.Create():MD5.Create())return BitConverter.ToString(h.ComputeHash(Encoding.ASCII.GetBytes(text))).Replace("-","").ToLowerInvariant();}
        static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
        sealed class Api:HttpMessageHandler {
            public string Payload=Content,Version="v1",ExtraName;public bool NoMd5,NoSha;public int Links;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken token){
                object file=new{path="disk:/a.bin",type="file",size=Payload.Length,modified=Version,md5=NoMd5?"":Hash(Payload,false),sha256=NoSha?"":Hash(Payload,true)};
                string value;if(r.RequestUri.AbsolutePath.EndsWith("/download")){Links++;value="{\"href\":\"https://download.example/safe\"}";}else if(r.RequestUri.AbsolutePath.EndsWith("/resources")){string path=Uri.UnescapeDataString(r.RequestUri.Query.Split('&')[0].Substring(6));value=new JavaScriptSerializer().Serialize(path=="disk:/a.bin"?file:(object)new{type="dir",_embedded=new{items=String.IsNullOrEmpty(ExtraName)?new[]{file}:new[]{file,(object)new{path="disk:/a.bin"+ExtraName,type="file",size=Payload.Length,modified=Version,md5=Hash(Payload,false),sha256=Hash(Payload,true)}}}});}else value="{\"user\":{\"uid\":\"safe-test\"}}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(value)});
            }
        }
        sealed class Download:HttpMessageHandler {
            public string Payload=Content;public int Calls;public int Code;public Action OnRequest;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken token){Calls++;if(OnRequest!=null)OnRequest();if(Code!=0)return Task.FromResult(new HttpResponseMessage((HttpStatusCode)Code){Content=new StringContent("")});long start=r.Headers.Range==null?0:r.Headers.Range.Ranges.Single().From.Value,end=r.Headers.Range==null?Payload.Length-1:r.Headers.Range.Ranges.Single().To??Payload.Length-1;var response=new HttpResponseMessage(r.Headers.Range==null?HttpStatusCode.OK:HttpStatusCode.PartialContent){Content=new ByteArrayContent(Encoding.ASCII.GetBytes(Payload.Substring((int)start,(int)(end-start+1))))};if(r.Headers.Range!=null)response.Content.Headers.ContentRange=new ContentRangeHeaderValue(start,end,Payload.Length);return Task.FromResult(response);}
        }
        public static void Run(string root){
            MetadataRegression(root);
            string recoveryRoot=Path.Combine(root,"lost-quarantine"),recoveryData=Path.Combine(root,"lost-quarantine-data");Directory.CreateDirectory(recoveryRoot);
            string state=Path.Combine(recoveryData,"preservation");Directory.CreateDirectory(state);var missing=new DiskFile{Path="disk:/lost.bin",Destination=Path.Combine(recoveryRoot,"lost.bin"),Length=8,Modified="v1"};
            string ledger=Path.Combine(state,HistoryStore.IdentityKey(CloudIntegrity.Version(missing))+".json");
            File.WriteAllText(Path.Combine(state,"intent-lost.json"),new JavaScriptSerializer().Serialize(new{LedgerPath=ledger,Version=CloudIntegrity.Version(missing),Source=missing.Destination+".ydr-download",Target=Path.Combine(recoveryRoot,"lost-copy.bin"),Blocked=true,Length=8,Hash="bad"}));
            Assert(new FilePreserver(recoveryData,recoveryRoot,new[]{missing.Destination}).Blocked(missing),"Deleted copy during crash recovery lost its blocked version.");
            var hashFile=new DiskFile{Length=Content.Length,Md5=Hash(Wrong,false),Sha256=Hash(Content,true)};string hashPath=Path.Combine(root,"hash-priority.bin");File.WriteAllText(hashPath,Content);
            Assert(!CloudIntegrity.Check(hashPath,hashFile,CancellationToken.None).Match,"SHA256 overrode a failed MD5.");
            foreach(int stage in new[]{0,1,2,3}){
                string dir=Path.Combine(root,"transaction-"+stage),dataDir=Path.Combine(root,"transaction-data-"+stage);Directory.CreateDirectory(dir);string journal=Path.Combine(dataDir,"preservation");Directory.CreateDirectory(journal);
                string normal=Path.Combine(dir,"a.bin"),payload=normal+".ydr-download",saved=Path.Combine(dir,"saved.bin");File.WriteAllText(normal,Wrong);File.WriteAllText(payload,Content);
                if(stage>=1)File.Move(normal,saved);if(stage>=2)File.Move(payload,normal);if(stage==3)File.Delete(saved);
                File.WriteAllText(Path.Combine(journal,"intent-test.json"),new JavaScriptSerializer().Serialize(new{LedgerPath=Path.Combine(journal,"operation.json"),Version="transaction",Source=payload,Target=normal,OldSource=normal,OldTarget=saved,Blocked=false,Length=Content.Length}));
                new FilePreserver(dataDir,dir,new[]{normal});Assert(File.ReadAllText(normal)==Content&&(stage==3?!File.Exists(saved):File.ReadAllText(saved)==Wrong),"Recovery lost a copy at move stage "+stage);
            }
            string conflict=Path.Combine(root,"pending-conflict"),conflictData=Path.Combine(root,"pending-conflict-data");Directory.CreateDirectory(conflict);string intentDir=Path.Combine(conflictData,"preservation");Directory.CreateDirectory(intentDir);
            string finalPath=Path.Combine(conflict,"a.bin"),sourcePath=finalPath+".ydr-download",errorPath=Path.Combine(conflict,"error.bin");File.WriteAllText(sourcePath,Content);File.WriteAllText(errorPath,Wrong);
            File.WriteAllText(Path.Combine(intentDir,"intent-conflict.json"),new JavaScriptSerializer().Serialize(new{Destination=finalPath,Source=sourcePath,Target=errorPath,LedgerPath=Path.Combine(intentDir,"conflict.json"),Version="v",Blocked=true}));
            var pending=new FilePreserver(conflictData,conflict,new[]{finalPath});bool blocked=false;try{pending.CheckDestination(finalPath);}catch(IOException){blocked=true;}Assert(blocked,"Unresolved quarantine did not protect its original destination.");pending.CheckDestination(Path.Combine(conflict,"unrelated.bin"));
            foreach(string suffix in new[]{".ydr-download",".ydr-map"}){
                string folder=Path.Combine(root,"collision"+suffix),data=Path.Combine(root,"collision-data"+suffix);Directory.CreateDirectory(folder);string original=Path.Combine(folder,"a.bin"+suffix);File.WriteAllText(original,Content);
                var download=new Download();Snapshot view=null;new ApiEngine(data,x=>view=x,new Api{ExtraName=suffix},download).Run(new Settings{UseApi=true,ApiToken="test-secret",ApiFolder=folder,UseCachedApiList=false},RunMode.Remaining);
                Assert(download.Calls==0&&File.ReadAllText(original)==Content&&view.Available==1,"Temporary suffix overwrote another cloud destination.");
            }
            foreach(string scenario in new[]{"sha","bad-md5","bad-size","bad-new","no-hashes"}){
                string folder=Path.Combine(root,"safe-"+scenario),data=Path.Combine(root,"safe-data-"+scenario);Directory.CreateDirectory(folder);string destination=Path.Combine(folder,"a.bin");
                string old=scenario=="bad-size"?"old":Wrong;if(scenario!="no-hashes")File.WriteAllText(destination,old);
                var api=new Api{NoMd5=scenario=="sha"||scenario=="no-hashes",NoSha=scenario=="no-hashes"};var download=new Download{Payload=scenario=="bad-new"?Wrong:Content};Snapshot last=null;
                download.OnRequest=delegate{if(scenario!="no-hashes")Assert(File.ReadAllText(destination)==old,"Existing file moved before the new download completed.");};
                Settings settings=new Settings{UseApi=true,ApiToken="test-secret",ApiFolder=folder,UseCachedApiList=false,ApiRequestIntervalMs=25,StreamsPerFile=1};
                new ApiEngine(data,x=>last=x,api,download).Run(settings,RunMode.Remaining);
                Assert(download.Calls==1,scenario+": file was incorrectly skipped.");
                string archive=Path.Combine(folder,scenario=="bad-size"?"Сохранённые прежние файлы":"Файлы с ошибкой проверки");
                if(scenario!="no-hashes")Assert(Directory.Exists(archive)&&Directory.GetFiles(archive,"*",SearchOption.AllDirectories).Any(p=>File.ReadAllText(p)==old),scenario+": old file was not preserved.");
                if(scenario=="bad-new"){
                    Assert(!File.Exists(destination)&&last.Downloaded==0,"Corrupt new copy became a ready file.");
                    string[] before=Directory.GetFiles(archive,"*",SearchOption.AllDirectories);var manual=new Download{Payload=Wrong};new ApiEngine(data,x=>last=x,new Api(),manual).Run(settings,RunMode.IntegrityFailures);Assert(manual.Calls==1&&Directory.GetFiles(archive,"*",SearchOption.AllDirectories).Length==before.Length,"Explicit retry duplicated an identical failed copy.");var retry=new Download();new ApiEngine(data,x=>last=x,new Api(),retry).Run(settings,RunMode.All);Assert(retry.Calls==0,"Full rescan retried a blocked cloud version.");
                    foreach(string p in before.Where(p=>File.ReadAllText(p)==Wrong))File.Delete(p);
                    retry=new Download();new ApiEngine(data,x=>last=x,new Api(),retry).Run(settings,RunMode.Remaining);Assert(retry.Calls==0,"Deleted quarantine triggered repeated download.");
                    retry=new Download();new ApiEngine(data,x=>last=x,new Api{Version="v2"},retry).Run(settings,RunMode.Remaining);Assert(retry.Calls==1&&File.ReadAllText(destination)==Content,"New cloud version remained blocked.");
                }else Assert(last.Downloaded==1&&File.ReadAllText(destination)==Content,scenario+": correct copy did not complete.");
            }
        }
        public static void MetadataRegression(string root){
            var f=new DiskFile{Path="disk:/same.bin",Modified="v1|"+Hash(Content,false),Length=Content.Length,Md5=Hash(Content,false)};
            string without=CloudIntegrity.Version(f);f.Sha256=Hash(Content,true);
            Assert(CloudIntegrity.Version(f)==without,"Secondary SHA256 appearing changed an unchanged MD5 cloud version.");
            string mapPath=Path.Combine(root,"legacy-md5-map.bin"),identity="context\n"+without;
            RangeMap map=RangeMap.Open(mapPath,identity+f.Sha256,f.Length,1,CancellationToken.None);
            using(Stream output=map.Writer(0)){output.Write(Encoding.ASCII.GetBytes(Content),0,4);}
            Assert(!RangeMap.Matches(mapPath,identity,f.Length),"Legacy compatibility silently enabled without MD5.");
            Assert(RangeMap.Matches(mapPath,identity,f.Length,true),"1.0.5 MD5 map rejected after secondary hash normalization.");
            map=RangeMap.OpenPlanned(mapPath,identity,f.Length,1,null,CancellationToken.None,true);
            Assert(map.Bytes==4&&RangeMap.Matches(mapPath,identity,f.Length),"Legacy map migration lost confirmed bytes or failed to normalize identity.");
            Assert(!RangeMap.Matches(mapPath,"other-context\n"+without,f.Length,true),"Legacy map migration ignored changed account context.");
            string legacyData=Path.Combine(root,"legacy-ledger"),legacyState=Path.Combine(legacyData,"preservation");Directory.CreateDirectory(legacyState);
            string legacyVersion=without+f.Sha256;
            File.WriteAllText(Path.Combine(legacyState,HistoryStore.IdentityKey(legacyVersion)+".json"),new JavaScriptSerializer().Serialize(new{Version=legacyVersion,Blocked=true,Copies=new object[0]}));
            Assert(new FilePreserver(legacyData,root,new string[0]).Blocked(f),"Legacy failure record was lost after hash normalization.");
            f.Md5="";string shaVersion=CloudIntegrity.Version(f);f.Sha256=Hash(Wrong,true);Assert(CloudIntegrity.Version(f)!=shaVersion,"SHA-only version stopped detecting changed content.");
            string retryFolder=Path.Combine(root,"metadata-retry"),retryData=Path.Combine(root,"metadata-retry-data");
            var settings=new Settings{UseApi=true,ApiToken="test-secret",ApiFolder=retryFolder,UseCachedApiList=true,ApiRequestIntervalMs=25,StreamsPerFile=1};
            new ApiEngine(retryData,x=>{},new Api{NoSha=true},new Download{Code=503}).Run(settings,RunMode.Remaining);
            string retryDestination=Path.Combine(retryFolder,"a.bin");Assert(RangeMap.Exists(retryDestination),"Failed first attempt did not create a map.");
            new ApiEngine(retryData,x=>{},new Api(),new Download()).Run(settings,RunMode.Remaining);
            Assert(File.ReadAllText(retryDestination)==Content&&!Directory.Exists(Path.Combine(retryFolder,"Сохранённые прежние файлы")),"Fresh SHA256 metadata archived unchanged partial data on the next pass.");
            string folder=Path.Combine(root,"size-only-replacement"),data=Path.Combine(root,"size-only-data");Directory.CreateDirectory(folder);
            f=new DiskFile{Path="disk:/same.bin",Destination=Path.Combine(folder,"same.bin"),Modified="v1",Length=Content.Length};
            File.WriteAllText(f.Destination,Wrong);string payload=f.Destination+".ydr-download";File.WriteAllText(payload,Content);
            Assert(new FilePreserver(data,folder,new[]{f.Destination}).Complete(f,payload,CloudIntegrity.Check(payload,f,CancellationToken.None)),"Size-only replacement failed.");
            Assert(File.ReadAllText(f.Destination)==Content,"Downloaded new content was discarded based only on old file size.");
            Assert(Directory.GetFiles(Path.Combine(folder,"Сохранённые прежние файлы"),"*",SearchOption.AllDirectories).Any(p=>File.ReadAllText(p)==Wrong),"Unverified old content was not preserved.");
        }
    }
}
