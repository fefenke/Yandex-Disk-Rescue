using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DiskRescue {
    static class RangeMapTests {
        static void Assert(bool value,string message){if(!value)throw new Exception(message);}
        public static void CrashProbe(string path){
            RangeMap map=RangeMap.Open(path,"crash-map",40L*1024*1024,1,CancellationToken.None);Stream output=map.Writer(0);byte[] buffer=new byte[1024*1024];for(int i=0;i<buffer.Length;i++)buffer[i]=(byte)(i%251);
            for(int i=0;i<33;i++)output.Write(buffer,0,buffer.Length);File.WriteAllText(path+".ready","ready");Thread.Sleep(Timeout.Infinite);GC.KeepAlive(output);
        }
        static void KillAndRecover(string root){
            string path=Path.Combine(root,"killed-map.bin");string assembly=typeof(ApiEngine).Assembly.Location.Replace("'","''"),literal=path.Replace("'","''");
            string code="$a=[Reflection.Assembly]::LoadFrom('"+assembly+"');$p=New-Object object[] 1;$p[0]='"+literal+"';$a.GetType('DiskRescue.RangeMapTests').GetMethod('CrashProbe').Invoke($null,$p)";
            var start=new ProcessStartInfo("powershell.exe","-NoProfile -NonInteractive -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(code))){UseShellExecute=false,CreateNoWindow=true};
            using(Process child=Process.Start(start)){try{Stopwatch timer=Stopwatch.StartNew();while(!File.Exists(path+".ready")&&!child.HasExited&&timer.Elapsed.TotalSeconds<30)Thread.Sleep(20);Assert(File.Exists(path+".ready"),"Map crash child did not reach checkpoint.");}finally{if(!child.HasExited)child.Kill();child.WaitForExit();}}
            RangeMap recovered=RangeMap.Open(path,"crash-map",40L*1024*1024,1,CancellationToken.None);Assert(recovered.Bytes==32L*1024*1024,"Map trusted unconfirmed bytes after process termination.");
        }
        public static void Run(string root){
            KillAndRecover(root);
            Type type=typeof(ApiEngine).Assembly.GetType("DiskRescue.RangeMap");Assert(type!=null,"Single-payload range map is not implemented.");
            string path=Path.Combine(root,"mapped.bin");MethodInfo open=type.GetMethod("Open",BindingFlags.Static|BindingFlags.Public);
            Func<string,object> load=identity=>open.Invoke(null,new object[]{path,identity,16L,2,CancellationToken.None});
            object map=load("v1");Func<long> bytes=()=>Convert.ToInt64(type.GetProperty("Bytes").GetValue(map,null));
            Func<int,Stream> writer=i=>(Stream)type.GetMethod("Writer").Invoke(map,new object[]{i});
            Assert(new FileInfo(path+".ydr-download").Length==16&&bytes()==0,"Allocated file length was trusted as downloaded bytes.");
            Task.WaitAll(Task.Run(delegate{using(Stream s=writer(1)){byte[] b=Encoding.ASCII.GetBytes("ijkl");s.Write(b,0,b.Length);}}),Task.Run(delegate{using(Stream s=writer(0)){byte[] b=Encoding.ASCII.GetBytes("abcd");s.Write(b,0,b.Length);}}));
            Assert(bytes()==8,"Concurrent range progress was lost.");
            using(FileStream s=new FileStream(path+".ydr-download",FileMode.Open,FileAccess.Write,FileShare.ReadWrite)){s.Position=4;s.Write(Encoding.ASCII.GetBytes("BAD!"),0,4);s.Flush(true);}
            File.AppendAllText(path+".ydr-map","{torn");map=load("v1");Assert(bytes()==8,"Unconfirmed bytes or torn map were trusted.");
            using(Stream s=writer(0)){byte[] b=Encoding.ASCII.GetBytes("efgh");s.Write(b,0,4);}using(Stream s=writer(1)){byte[] b=Encoding.ASCII.GetBytes("mnop");s.Write(b,0,4);}
            Assert(bytes()==16&&File.ReadAllText(path+".ydr-download")=="abcdefghijklmnop","Positional writes overwrote another range.");
            using(FileStream s=new FileStream(path+".ydr-download",FileMode.Open,FileAccess.Write)){s.Position=1;s.WriteByte(0);}
            map=load("v1");Assert(bytes()==8,"Corrupt range did not rewind independently of the healthy range.");
            string custom=Path.Combine(root,"custom.bin");RangeMap planned=RangeMap.OpenPlanned(custom,"custom",12,2,new long[]{0,10,12},CancellationToken.None);
            using(Stream output=planned.Writer(0)){output.Write(new byte[8],0,8);}Assert(RangeMap.Progress(custom)==8&&FileRules.HasPartialData(custom),"Custom boundaries lost durable progress or partial selection.");
            File.WriteAllText(custom+".ydr-map","{\"Version\":1,\"Parts\":0,\"Length\":12,\"Identity\":\"custom\"}\n");Assert(!RangeMap.Matches(custom,"custom",12),"Invalid header accepted for reuse.");
            File.WriteAllText(custom+".ydr-map","{\"Version\":1,\"Parts\":2,\"Length\":12,\"Identity\":\"custom\",\"Boundaries\":[0,13,12]}\n");Assert(!RangeMap.Matches(custom,"custom",12)&&RangeMap.Progress(custom)==0,"Invalid custom boundaries accepted.");
            File.WriteAllText(custom+".ydr-map","{\"Version\":1,\"Parts\":\"invalid\",\"Length\":12,\"Identity\":\"custom\"}\n");Assert(!RangeMap.Matches(custom,"custom",12)&&RangeMap.Progress(custom)==0,"Malformed header value prevented recovery.");
            bool refused=false;try{load("v2");}catch(TargetInvocationException ex){refused=ex.InnerException is IOException;}Assert(refused,"Map accepted a changed cloud identity.");
        }
    }
}
