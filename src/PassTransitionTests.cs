using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DiskRescue {
    static class PassTransitionTests {
        static void Assert(bool value,string message){if(!value)throw new Exception(message);}
        public static void Run(string root){
            BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            using(MainForm form=new MainForm(Path.Combine(root,"transition-ui"))){
                typeof(MainForm).GetField("waitingForRetry",flags).SetValue(form,true);
                typeof(MainForm).GetMethod("Render",flags).Invoke(form,new object[]{new Snapshot{Phase="completed",Finished=true}});
                Label label=(Label)typeof(MainForm).GetField("phase",flags).GetValue(form);
                Assert(label.Text.Contains("Подготовка к следующему проходу"),"Completed pass still displays a fixed 30-second pause instead of preparation.");
            }
            Type policy=typeof(PassHistory).Assembly.GetType("DiskRescue.PassTransition");
            Assert(policy!=null,"Pass transition policy is missing.");
            object transition=Activator.CreateInstance(policy);
            MethodInfo prepare=policy.GetMethod("Prepare"),wait=policy.GetMethod("WaitAsync");
            Snapshot completed=new Snapshot{Phase="completed",Finished=true,Deferred=1};
            Assert((int)prepare.Invoke(transition,new object[]{completed})==0,"Normal pass retained an artificial delay.");
            Assert(((Task)wait.Invoke(transition,new object[]{CancellationToken.None})).IsCompleted,"Normal transition does not complete immediately.");
            Snapshot error=new Snapshot{Phase="error",Finished=true,RetryableError=true};
            foreach(int expected in new[]{1,2,4,8,16,30,30})Assert((int)prepare.Invoke(transition,new object[]{error})==expected,"Transient whole-pass failures do not back off with a bounded delay.");
            using(CancellationTokenSource cancel=new CancellationTokenSource()){
                Task waiting=(Task)wait.Invoke(transition,new object[]{cancel.Token});cancel.Cancel();
                try{waiting.GetAwaiter().GetResult();throw new Exception("Stop did not cancel pass-transition waiting.");}catch(OperationCanceledException){}
            }
            Assert((int)prepare.Invoke(transition,new object[]{completed})==0,"A completed pass inherited an error cooldown.");
            Assert((int)prepare.Invoke(transition,new object[]{error})==1,"A completed pass did not reset error backoff.");
            using(CancellationTokenSource cancel=new CancellationTokenSource()){
                prepare.Invoke(transition,new object[]{completed});cancel.Cancel();
                Task waiting=(Task)wait.Invoke(transition,new object[]{cancel.Token});
                try{waiting.GetAwaiter().GetResult();throw new Exception("Stop was ignored during immediate preparation.");}catch(OperationCanceledException){}
            }
        }
    }
}
