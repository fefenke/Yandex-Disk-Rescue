using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DiskRescue {
    public sealed partial class MainForm {
        readonly Timer updateTimer=new Timer{Interval=6*60*60*1000};UpdateRelease pendingUpdate;bool checkingUpdate,installingUpdate,offeredUpdate;
        CheckBox autoCheck;Button checkRelease,installRelease;Label releaseStatus;
        Control UpdateSettingsPanel(){
            TableLayoutPanel panel=Table(1);panel.Padding=new Padding(18,20,0,0);panel.RowStyles.Add(new RowStyle(SizeType.Absolute,28));panel.RowStyles.Add(new RowStyle(SizeType.Absolute,50));panel.RowStyles.Add(new RowStyle(SizeType.Absolute,26));
            autoCheck=new CheckBox{Dock=DockStyle.Fill,Checked=settings.AutoCheckUpdates};panel.Controls.Add(autoCheck,0,0);
            FlowLayoutPanel buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty};checkRelease=new SoftButton{Width=220,Height=36,Margin=new Padding(0,6,10,6)};installRelease=new SoftButton{Primary=true,Width=132,Height=36,Margin=new Padding(0,6,0,6),Enabled=false};buttons.Controls.Add(checkRelease);buttons.Controls.Add(installRelease);panel.Controls.Add(buttons,0,1);releaseStatus=Label("",9,false);panel.Controls.Add(releaseStatus,0,2);
            autoCheck.CheckedChanged+=delegate{settings.AutoCheckUpdates=autoCheck.Checked;SaveSettings();if(autoCheck.Checked)updateTimer.Start();else{updateTimer.Stop();pendingUpdate=null;offeredUpdate=true;}TranslateUpdateSettings();};checkRelease.Click+=async delegate{await CheckUpdates(true);};installRelease.Click+=delegate{offeredUpdate=false;OfferUpdate();};TranslateUpdateSettings();return panel;
        }
        void TranslateUpdateSettings(){if(autoCheck==null)return;autoCheck.Text=T("Проверять обновления автоматически","Check for updates automatically");checkRelease.Text=T("Проверить обновления","Check for updates");installRelease.Text=T("Обновить","Update");checkRelease.Enabled=!checkingUpdate&&!installingUpdate;installRelease.Enabled=pendingUpdate!=null&&!busy&&!installingUpdate;releaseStatus.Text=checkingUpdate?T("Проверяем GitHub…","Checking GitHub…"):pendingUpdate==null?T("Текущая версия: ","Current version: ")+typeof(MainForm).Assembly.GetName().Version.ToString(3):T("Доступна версия ","Available version ")+pendingUpdate.Version;}
        void SetupUpdates(){Shown+=async delegate{if(settings.AutoCheckUpdates){updateTimer.Start();await CheckUpdates(false);}};updateTimer.Tick+=async delegate{await CheckUpdates(false);};FormClosed+=delegate{updateTimer.Dispose();};}
        async Task CheckUpdates(bool manual){
            if(checkingUpdate||installingUpdate||(!manual&&!settings.AutoCheckUpdates))return;checkingUpdate=true;TranslateUpdateSettings();
            try{UpdateRelease found=await AppUpdater.Check();if(!manual&&!settings.AutoCheckUpdates)return;pendingUpdate=found;offeredUpdate=manual;if(pendingUpdate==null){if(manual)MessageBox.Show(this,T("Установлена актуальная версия.","You have the current version."),Text);}else if(busy){footer.Text=T("Доступно обновление. Остановите проход для установки.","Update available. Stop the pass to install it.");}else if(manual){MessageBox.Show(this,T("Новая версия найдена. Нажмите «Обновить» в настройках.","A new version is available. Click Update in Settings."),Text);}else OfferUpdate();}
            catch(Exception){if(manual)MessageBox.Show(this,T("Не удалось проверить GitHub. Попробуйте позже.","Could not check GitHub. Try again later."),Text);}
            finally{checkingUpdate=false;if(!IsDisposed)TranslateUpdateSettings();}
        }
        async void OfferUpdate(){
            if(pendingUpdate==null||offeredUpdate||busy||installingUpdate||closeWhenStopped||!Visible)return;offeredUpdate=true;UpdateRelease release=pendingUpdate;
            if(MessageBox.Show(this,String.Format(T("Доступна версия {0}. Скачать и установить?\nПриложение перезапустится. Ваши настройки и история загрузок сохранятся.","Version {0} is available. Download and install?\nThe app will restart. Your settings and download history will be preserved."),release.Version),Text,MessageBoxButtons.YesNo,MessageBoxIcon.Information)!=DialogResult.Yes)return;
            installingUpdate=true;start.Enabled=false;TranslateUpdateSettings();
            try{footer.Text=T("Скачиваем и проверяем обновление…","Downloading and verifying update…");string package=await AppUpdater.Download(release);if(IsDisposed)return;AppUpdater.Launch(package);Close();}
            catch(Exception ex){if(!IsDisposed){footer.Text=T("Обновление не установлено.","Update was not installed.");MessageBox.Show(this,T("Не удалось обновиться: ","Update failed: ")+ex.GetType().Name,Text);}}
            finally{installingUpdate=false;if(!IsDisposed){start.Enabled=!renderingFailed;TranslateUpdateSettings();}}
        }
    }
}
