using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace DiskRescue {
    static class Program {
        [STAThread]
        static int Main(string[] args) {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            if(args.Length>2&&args[0]=="--probe"){
                File.WriteAllText(args[2],new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(YandexCloudNative.Probe(args[1])));return 0;
            }
            if(args.Length>0&&args[0]=="--self-test")return Tests.Run();
            if(args.Length>0&&args[0]=="--render-preview"){
                string data=Path.Combine(Path.GetTempPath(),"disk-rescue-preview-"+Guid.NewGuid().ToString("N"));
                using(MainForm form=new MainForm(data)){
                    form.Preview(args.Length>2&&args[2]=="en");
                    // Render offscreen: create a hidden native handle first, then
                    // set managed visibility for DrawToBitmap without showing it.
                    IntPtr handle=form.Handle;
                    System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
                    int visible=(int)typeof(Control).GetField("STATE_VISIBLE",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).GetRawConstantValue();
                    typeof(Control).GetMethod("SetState",flags).Invoke(form,new object[]{visible,true});
                    PrepareControls(form);
                    using(Bitmap bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(args.Length>1?args[1]:"preview.png");}
                }
                return 0;
            }
            bool created;
            using(Mutex mutex=new Mutex(true,@"Local\YandexDiskRescueDesktop",out created)){
                if(!created){MessageBox.Show("Yandex Disk Rescue is already running / Приложение уже запущено.");return 1;}
                string data=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data");
                try {
                    PrerequisiteStatus requirements=Prerequisites.Check();
                    if(!requirements.PlatformReady){MessageBox.Show("Windows 10 1709+ / Windows 11 x64 and .NET Framework 4.7.1+ with Cloud Files are required.\n\nТребуются Windows 10 1709+ / Windows 11 x64, .NET Framework 4.7.1+ и Cloud Files.\n\nUse / Используйте: Start Yandex Disk Rescue.cmd", "Yandex Disk Rescue", MessageBoxButtons.OK,MessageBoxIcon.Information);return 1;}
                    Directory.CreateDirectory(data);Application.Run(new MainForm(data));
                }
                catch(Exception ex){MessageBox.Show(ex.Message,"Yandex Disk Rescue",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
            }
            return 0;
        }
        static void PrepareControls(Control control){control.CreateControl();control.PerformLayout();foreach(Control child in control.Controls)PrepareControls(child);}
    }
}
