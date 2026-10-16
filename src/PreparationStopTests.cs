using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace DiskRescue {
    static class PreparationStopTests {
        sealed class Api:HttpMessageHandler {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){token.ThrowIfCancellationRequested();return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(request.RequestUri.AbsolutePath.EndsWith("/resources")?"{\"type\":\"dir\",\"_embedded\":{\"items\":[{\"path\":\"disk:/a.bin\",\"type\":\"file\",\"size\":12,\"modified\":\"v1\"}]}}":"{\"user\":{\"uid\":\"preparation-test\"}}")});}
        }
        static IEnumerable<string> Lines(CancellationTokenSource cancel){yield return "{\"path\":\"disk:/first.bin\",\"status\":\"success\"}";cancel.Cancel();yield return "{broken";throw new Exception("History reading continued after Stop.");}
        public static void Run(string root){
            MethodInfo read=typeof(HistoryStore).GetMethod("ReadRecords",BindingFlags.Static|BindingFlags.NonPublic);
            if(read==null)throw new Exception("History preparation cannot be cancelled between journal records.");
            var records=new Dictionary<string,HistoryRecord>();using(var cancel=new CancellationTokenSource()){
                bool stopped=false;try{read.Invoke(null,new object[]{records,Lines(cancel),cancel.Token});}catch(TargetInvocationException ex){if(!(ex.InnerException is OperationCanceledException))throw;stopped=true;}
                if(!stopped||records.Count!=1)throw new Exception("History cancellation was swallowed or lost the already read record.");
            }
            string data=Path.Combine(root,"preparation-stop-data"),folder=Path.Combine(root,"preparation-stop-files");Directory.CreateDirectory(folder);string historyDirectory;
            using(var history=new HistoryStore(data,folder,"preparation-test|disk:/|"+Path.GetFullPath(folder).ToUpperInvariant())){historyDirectory=history.DirectoryPath;history.SaveApi(new DiskFile{Path="disk:/old.bin",Length=7},"pending","");}
            string journal=Path.Combine(historyDirectory,"history.jsonl"),before=File.ReadAllText(journal);ApiEngine engine=null;Snapshot last=null;
            engine=new ApiEngine(data,s=>{last=s;if(s.Phase=="checking_local")engine.Stop();},new Api(),new Api());
            engine.Run(new Settings{UseApi=true,ApiToken="test-secret",ApiFolder=folder,ApiRequestIntervalMs=25},RunMode.Remaining);
            if(last==null||last.Phase!="stopped"||!last.Finished||File.ReadAllText(journal)!=before)throw new Exception("Stop during preparation failed or altered existing history.");
            if(Directory.Exists(last.Report))throw new Exception("Stop during preparation generated unnecessary full reports.");
        }
    }
}
