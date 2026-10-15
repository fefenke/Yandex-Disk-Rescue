using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Diagnostics;

namespace DiskRescue {
    static class DurablePartialTests {
        static void Assert(bool value,string message){if(!value)throw new Exception(message);}
        // Invoked only by the isolated test child; intentionally no Dispose.
        public static void CrashProbe(string path){
            Stream output=new DurablePartial(path,"crash-probe",CancellationToken.None);
            byte[] buffer=new byte[1024*1024];for(int i=0;i<buffer.Length;i++)buffer[i]=(byte)(i%251);
            for(int i=0;i<32;i++)output.Write(buffer,0,buffer.Length);
            output.Write(buffer,0,buffer.Length);File.WriteAllText(path+".ready","ready");
            Thread.Sleep(Timeout.Infinite);GC.KeepAlive(output);
        }
        static void KillAndRecover(string root){
            string path=Path.Combine(root,"killed-process.part");
            string assembly=typeof(ApiEngine).Assembly.Location.Replace("'","''"),literal=path.Replace("'","''");
            string code="$a=[Reflection.Assembly]::LoadFrom('"+assembly+"');$p=New-Object object[] 1;$p[0]='"+literal+"';$a.GetType('DiskRescue.DurablePartialTests').GetMethod('CrashProbe').Invoke($null,$p)";
            ProcessStartInfo start=new ProcessStartInfo("powershell.exe","-NoProfile -NonInteractive -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(code))){UseShellExecute=false,CreateNoWindow=true};
            using(Process child=Process.Start(start)){
                try{Stopwatch timer=Stopwatch.StartNew();while(!File.Exists(path+".ready")&&!child.HasExited&&timer.Elapsed.TotalSeconds<30)Thread.Sleep(20);Assert(File.Exists(path+".ready"),"Crash test child did not reach checkpoint.");}
                finally{if(!child.HasExited)child.Kill();child.WaitForExit();}
            }
            long restored=DurablePartial.Recover(path,"crash-probe",true,CancellationToken.None);
            Assert(restored==32L*1024*1024&&new FileInfo(path).Length==restored,"Abrupt process termination retained the unconfirmed tail.");
            using(FileStream input=File.OpenRead(path)){byte[] buffer=new byte[65536];long position=0;int n;while((n=input.Read(buffer,0,buffer.Length))>0){for(int i=0;i<n;i++)Assert(buffer[i]==(byte)(((position+i)%(1024*1024))%251),"Crash recovery changed confirmed bytes.");position+=n;}}
        }
        public static void Run(string root){
            Type type=typeof(ApiEngine).Assembly.GetType("DiskRescue.DurablePartial");
            Assert(type!=null,"Partial downloads have no durable checkpoint implementation.");
            string path=Path.Combine(root,"durable.part");
            MethodInfo recover=type.GetMethod("Recover",BindingFlags.Static|BindingFlags.Public);
            Func<string,long> restore=identity=>(long)recover.Invoke(null,new object[]{path,identity,true,CancellationToken.None});
            Func<string,Stream> open=identity=>(Stream)Activator.CreateInstance(type,new object[]{path,identity,CancellationToken.None});
            byte[] first=Encoding.ASCII.GetBytes("abcdefgh"),second=Encoding.ASCII.GetBytes("ijklmnop");
            using(Stream output=open("v1")){output.Write(first,0,first.Length);output.Flush();}
            using(Stream output=open("v1")){output.Write(second,0,second.Length);output.Flush();}
            File.WriteAllText(path+".ydr-state",File.ReadAllText(path+".ydr-state").TrimEnd('\n'));
            Assert(restore("v1")==16,"Complete journal record without newline was lost.");
            using(Stream output=open("v1")){output.Write(first,0,first.Length);}
            Assert(restore("v1")==24,"Missing final newline merged successive checkpoint records.");
            // Restore the two-extent fixture for the corruption cases below.
            using(FileStream truncated=new FileStream(path,FileMode.Open,FileAccess.Write))truncated.SetLength(16);
            Assert(restore("v1")==16,"Valid earlier extents were lost after truncation.");
            File.AppendAllText(path,"UNCONFIRMED TAIL");
            Assert(restore("v1")==16&&File.ReadAllText(path)=="abcdefghijklmnop","Uncommitted crash tail was trusted.");
            // A torn journal record must retain the preceding durable checkpoints.
            File.AppendAllText(path+".ydr-state","{\"start\":16");File.AppendAllText(path,"MORE TAIL");
            Assert(restore("v1")==16,"Torn checkpoint discarded earlier confirmed data.");
            using(FileStream corrupt=new FileStream(path,FileMode.Open,FileAccess.Write)){corrupt.Position=10;corrupt.WriteByte(0);}
            Assert(restore("v1")==8&&File.ReadAllText(path)=="abcdefgh","Corrupt checkpoint bytes were trusted or earlier good bytes lost.");
            Assert(restore("v2")==0&&new FileInfo(path).Length==0,"Checkpoint crossed cloud versions.");
            File.WriteAllText(path,"legacy");File.Delete(path+".ydr-state");
            Assert(restore("v2")==6,"Existing MD5-protected partial was discarded during migration.");
            File.WriteAllText(path+".ydr-state","damaged header");
            Assert(restore("v2")==0,"Unreadable checkpoint silently adopted unconfirmed bytes.");
            File.WriteAllText(path,"no-checksum");File.Delete(path+".ydr-state");
            Assert((long)recover.Invoke(null,new object[]{path,"v2",false,CancellationToken.None})==0,"Legacy bytes without cloud digest were trusted.");
            using(Stream output=open("v2")){output.Write(first,0,first.Length);}
            Assert(restore("v2")==8,"Normal disposal failed to checkpoint final progress.");
            File.WriteAllText(path+".ydr-state.tmp","interrupted replacement");
            Assert(restore("v2")==8,"Orphan temporary checkpoint replaced the committed state.");
            CancellationTokenSource stopped=new CancellationTokenSource();stopped.Cancel();
            try{recover.Invoke(null,new object[]{path,"v2",true,stopped.Token});throw new Exception("Recovery ignored cancellation.");}catch(TargetInvocationException ex){Assert(ex.InnerException is OperationCanceledException,"Unexpected cancellation failure.");}
            KillAndRecover(root);
        }
    }
}
