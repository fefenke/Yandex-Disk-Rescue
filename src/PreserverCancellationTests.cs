using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;

namespace DiskRescue {
    internal static class PreserverCancellationTests {
        static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
        static string Hash(byte[] bytes){using(MD5 hash=MD5.Create())return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        static string HashFile(string path){using(MD5 hash=MD5.Create())using(Stream input=File.OpenRead(path))return BitConverter.ToString(hash.ComputeHash(input)).Replace("-","").ToLowerInvariant();}
        static byte[] Bytes(byte value){byte[] bytes=new byte[3*1024*1024];for(int i=0;i<bytes.Length;i++)bytes[i]=value;return bytes;}
        static bool Complete(FilePreserver preserver,DiskFile file,string payload,IntegrityResult check,CancellationToken token,Action<long,long> progress){
            MethodInfo method=typeof(FilePreserver).GetMethod("Complete",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new[]{typeof(DiskFile),typeof(string),typeof(IntegrityResult),typeof(CancellationToken),typeof(Action<long,long>)},null);
            Assert(method!=null,"File preservation has no cancellable verification boundary.");
            try{return (bool)method.Invoke(preserver,new object[]{file,payload,check,token,progress});}
            catch(TargetInvocationException error){throw error.InnerException;}
        }
        static void Cancelled(Action work){bool cancelled=false;try{work();}catch(OperationCanceledException){cancelled=true;}Assert(cancelled,"Preservation hash ignored cancellation.");}
        static void NoIntent(string data){Assert(Directory.GetFiles(Path.Combine(data,"preservation"),"intent-*.json").Length==0,"Cancellation wrote a preservation intent.");}
        public static void Run(string root){Existing(root);Payload(root);Duplicate(root);}
        public static void Existing(string root){
            string folder=Path.Combine(root,"cancel-existing"),data=Path.Combine(root,"cancel-existing-data");Directory.CreateDirectory(folder);
            byte[] wanted=Bytes(25),prior=Bytes(31);string destination=Path.Combine(folder,"file.bin"),payload=destination+".ydr-download";
            File.WriteAllBytes(destination,prior);File.WriteAllBytes(payload,wanted);
            var file=new DiskFile{Path="disk:/file.bin",Destination=destination,Length=wanted.Length,Md5=Hash(wanted),Modified="v1"};
            var preserver=new FilePreserver(data,folder,new[]{destination});IntegrityResult check=CloudIntegrity.Check(payload,file,CancellationToken.None);
            using(var stop=new CancellationTokenSource()){
                Cancelled(delegate{Complete(preserver,file,payload,check,stop.Token,delegate(long done,long total){if(done>0)stop.Cancel();});});
            }
            Assert(HashFile(destination)==Hash(prior)&&HashFile(payload)==Hash(wanted),"Cancellation during old-file verification lost a copy.");NoIntent(data);
            preserver=new FilePreserver(data,folder,new[]{destination});
            Assert(Complete(preserver,file,payload,check,CancellationToken.None,null),"Retry after cancelled old-file hash did not succeed.");
            Assert(HashFile(destination)==Hash(wanted)&&Directory.GetFiles(folder,"*",SearchOption.AllDirectories).Any(p=>p!=destination&&HashFile(p)==Hash(prior)),"Retry failed to archive the old bytes.");
        }
        public static void Payload(string root){
            string folder=Path.Combine(root,"cancel-payload"),data=Path.Combine(root,"cancel-payload-data");Directory.CreateDirectory(folder);
            byte[] bytes=Bytes(45);string destination=Path.Combine(folder,"file.bin"),payload=destination+".ydr-download";File.WriteAllBytes(payload,bytes);
            var file=new DiskFile{Path="disk:/file.bin",Destination=destination,Length=bytes.Length,Modified="v1"};
            var check=new IntegrityResult{Match=false,Actual="",Algorithm="sha256"};var preserver=new FilePreserver(data,folder,new[]{destination});
            using(var stop=new CancellationTokenSource())Cancelled(delegate{Complete(preserver,file,payload,check,stop.Token,delegate(long done,long total){if(done>0)stop.Cancel();});});
            Assert(File.Exists(payload)&&!File.Exists(destination)&&HashFile(payload)==Hash(bytes),"Cancellation during payload fingerprint lost the source.");NoIntent(data);
        }
        public static void Duplicate(string root){
            string folder=Path.Combine(root,"cancel-duplicate"),data=Path.Combine(root,"cancel-duplicate-data");Directory.CreateDirectory(folder);
            byte[] expected=Bytes(66),wrong=Bytes(67);string destination=Path.Combine(folder,"file.bin"),payload=destination+".ydr-download";File.WriteAllBytes(payload,wrong);
            var file=new DiskFile{Path="disk:/file.bin",Destination=destination,Length=expected.Length,Md5=Hash(expected),Modified="v1"};
            var preserver=new FilePreserver(data,folder,new[]{destination});IntegrityResult check=CloudIntegrity.Check(payload,file,CancellationToken.None);
            Assert(!Complete(preserver,file,payload,check,CancellationToken.None,null),"First mismatched payload was accepted.");
            string archived=file.PreservedPath;Assert(File.Exists(archived)&&HashFile(archived)==Hash(wrong),"First mismatched copy was not preserved.");
            File.WriteAllBytes(payload,wrong);
            using(var stop=new CancellationTokenSource())Cancelled(delegate{Complete(preserver,file,payload,check,stop.Token,delegate(long done,long total){if(done>0)stop.Cancel();});});
            Assert(File.Exists(payload)&&HashFile(payload)==Hash(wrong)&&HashFile(archived)==Hash(wrong),"Cancellation during duplicate comparison lost a copy.");NoIntent(data);
            preserver=new FilePreserver(data,folder,new[]{destination});
            Assert(!Complete(preserver,file,payload,check,CancellationToken.None,null)&&!File.Exists(payload),"Retry did not safely deduplicate an identical failed copy.");
            Assert(Directory.GetFiles(folder,"*",SearchOption.AllDirectories).Count(p=>p.EndsWith(".bin",StringComparison.OrdinalIgnoreCase))==1,"Retry created a second preserved copy.");
        }
    }
}
