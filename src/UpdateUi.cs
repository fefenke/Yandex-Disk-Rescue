using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Threading;
using Timer=System.Windows.Forms.Timer;

namespace DiskRescue {
    public sealed partial class MainForm {
        readonly Timer updateTimer=new Timer{Interval=6*60*60*1000};UpdateRelease pendingUpdate;bool checkingUpdate,installingUpdate,offeredUpdate;
        CheckBox autoCheck;Button checkRelease,installRelease,cancelRelease;Label releaseStatus;
        CancellationTokenSource updateCancellation;bool closeAfterUpdateDownload,updateReadyToClose;
        Control UpdateSettingsPanel(){
            TableLayoutPanel panel=Table(1);panel.Padding=new Padding(18,20,0,0);panel.RowStyles.Add(new RowStyle(SizeType.Absolute,28));panel.RowStyles.Add(new RowStyle(SizeType.Absolute,50));panel.RowStyles.Add(new RowStyle(SizeType.Absolute,26));
            autoCheck=new CheckBox{Dock=DockStyle.Fill,Margin=Padding.Empty,Checked=settings.AutoCheckUpdates};panel.Controls.Add(autoCheck,0,0);
            FlowLayoutPanel buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty};checkRelease=new SoftButton{Width=220,Height=36,Margin=new Padding(0,6,10,6)};installRelease=new SoftButton{Primary=true,Width=132,Height=36,Margin=new Padding(0,6,0,6),Enabled=false};buttons.Controls.Add(checkRelease);buttons.Controls.Add(installRelease);panel.Controls.Add(buttons,0,1);releaseStatus=Label("",9,false);panel.Controls.Add(releaseStatus,0,2);
            releaseStatus.Margin=Padding.Empty;CenterFlowRow(buttons);
            cancelRelease=new SoftButton{Width=180,Height=36,Margin=new Padding(10,6,0,6),Visible=false};buttons.Controls.Add(cancelRelease);
            cancelRelease.Click+=delegate{if(updateCancellation!=null)updateCancellation.Cancel();};
            autoCheck.CheckedChanged+=delegate{settings.AutoCheckUpdates=autoCheck.Checked;SaveSettings();if(autoCheck.Checked)updateTimer.Start();else{updateTimer.Stop();pendingUpdate=null;offeredUpdate=true;}TranslateUpdateSettings();};checkRelease.Click+=async delegate{await CheckUpdates(true);};installRelease.Click+=delegate{offeredUpdate=false;OfferUpdate();};TranslateUpdateSettings();return panel;
        }
        void TranslateUpdateSettings(){if(autoCheck==null)return;autoCheck.Text=T("Проверять обновления автоматически","Check for updates automatically");checkRelease.Text=T("Проверить обновления","Check for updates");installRelease.Text=T("Обновить","Update");checkRelease.Enabled=!checkingUpdate&&!installingUpdate;installRelease.Enabled=pendingUpdate!=null&&!busy&&!installingUpdate;autoCheck.Enabled=!installingUpdate;if(cancelRelease!=null){cancelRelease.Text=T("Отменить загрузку","Cancel download");cancelRelease.Visible=installingUpdate;cancelRelease.Enabled=installingUpdate&&updateCancellation!=null&&!updateCancellation.IsCancellationRequested;}releaseStatus.Text=checkingUpdate?T("Проверяем GitHub…","Checking GitHub…"):pendingUpdate==null?T("Текущая версия: ","Current version: ")+typeof(MainForm).Assembly.GetName().Version.ToString(3):T("Доступна версия ","Available version ")+pendingUpdate.Version;}
        void SetupUpdates(){Shown+=async delegate{if(settings.AutoCheckUpdates){updateTimer.Start();await CheckUpdates(false);}};updateTimer.Tick+=async delegate{await CheckUpdates(false);};FormClosing+=delegate(object sender,FormClosingEventArgs e){if(installingUpdate&&!updateReadyToClose){e.Cancel=true;closeAfterUpdateDownload=true;if(updateCancellation!=null)updateCancellation.Cancel();}};FormClosed+=delegate{updateTimer.Dispose();if(updateCancellation!=null)updateCancellation.Cancel();};}
        async Task CheckUpdates(bool manual){
            if(checkingUpdate||installingUpdate||(!manual&&!settings.AutoCheckUpdates))return;checkingUpdate=true;TranslateUpdateSettings();
            try{UpdateRelease found=await AppUpdater.Check();if(!manual&&!settings.AutoCheckUpdates)return;pendingUpdate=found;offeredUpdate=manual;if(pendingUpdate==null){if(manual)MessageBox.Show(this,T("Установлена актуальная версия.","You have the current version."),Text);}else if(busy){footer.Text=T("Доступно обновление. Остановите проход для установки.","Update available. Stop the pass to install it.");}else if(manual){MessageBox.Show(this,T("Новая версия найдена. Нажмите «Обновить» в настройках.","A new version is available. Click Update in Settings."),Text);}else OfferUpdate();}
            catch(Exception){if(manual)MessageBox.Show(this,T("Не удалось проверить GitHub. Попробуйте позже.","Could not check GitHub. Try again later."),Text);}
            finally{checkingUpdate=false;if(!IsDisposed)TranslateUpdateSettings();}
        }
        async void OfferUpdate(){
            if(pendingUpdate==null||offeredUpdate||busy||installingUpdate||closeWhenStopped||!Visible)return;offeredUpdate=true;UpdateRelease release=pendingUpdate;
            if(MessageBox.Show(this,String.Format(T("Доступна версия {0}. Скачать и установить?\nПриложение перезапустится. Ваши настройки и история загрузок сохранятся.","Version {0} is available. Download and install?\nThe app will restart. Your settings and download history will be preserved."),release.Version),Text,MessageBoxButtons.YesNo,MessageBoxIcon.Information)!=DialogResult.Yes)return;
            await DownloadUpdate(release,AppUpdater.Download);
        }
        internal async Task DownloadUpdate(UpdateRelease release,Func<UpdateRelease,CancellationToken,Task<string>> fetch){
            if(installingUpdate||busy)return;
            updateCancellation=new CancellationTokenSource();installingUpdate=true;updateReadyToClose=false;start.Enabled=false;TranslateUpdateSettings();
            string package=null;
            try{footer.Text=T("Скачиваем и проверяем обновление…","Downloading and verifying update…");package=await fetch(release,updateCancellation.Token);updateCancellation.Token.ThrowIfCancellationRequested();if(IsDisposed)return;AppUpdater.Launch(package);updateReadyToClose=true;Close();}
            catch(OperationCanceledException){if(!IsDisposed)footer.Text=updateCancellation.IsCancellationRequested?T("Загрузка обновления отменена.","Update download cancelled."):T("Истекло время ожидания обновления. Попробуйте ещё раз.","Update download timed out. Try again.");}
            catch(Exception ex){if(!IsDisposed){footer.Text=T("Обновление не установлено.","Update was not installed.");MessageBox.Show(this,T("Не удалось обновиться: ","Update failed: ")+ex.GetType().Name,Text);}}
            finally{if(package!=null&&!updateReadyToClose)AppUpdater.Discard(package);installingUpdate=false;updateCancellation.Dispose();updateCancellation=null;if(!IsDisposed){start.Enabled=!renderingFailed;TranslateUpdateSettings();if(closeAfterUpdateDownload){closeAfterUpdateDownload=false;Close();}}}
        }
    }
}
