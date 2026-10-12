using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace DiskRescue {
    public sealed class RoundPanel : Panel {
        public Color Fill = Color.White;
        public RoundPanel() { DoubleBuffered = true; Padding = new Padding(18); }
        protected override void OnPaintBackground(PaintEventArgs e) {
            e.Graphics.Clear(BackgroundOf(Parent));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Rounded(r, 16)) using (SolidBrush brush = new SolidBrush(Fill)) e.Graphics.FillPath(brush, p);
        }
        public static GraphicsPath Rounded(Rectangle r, int radius) {
            int d = radius * 2; GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X,r.Y,d,d,180,90); p.AddArc(r.Right-d,r.Y,d,d,270,90);
            p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); p.AddArc(r.X,r.Bottom-d,d,d,90,90); p.CloseFigure(); return p;
        }
        public static Color BackgroundOf(Control control){while(control!=null){if(control.BackColor.A==255)return control.BackColor;control=control.Parent;}return Color.FromArgb(244,246,251);}
    }
    public sealed class FlatButton : Button {
        public Color Fill = Color.FromArgb(89, 101, 233);
        public FlatButton() { FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand; ForeColor = Color.White; Font = new Font("Segoe UI", 10, FontStyle.Bold); }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.Clear(RoundPanel.BackgroundOf(Parent)); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = RoundPanel.Rounded(new Rectangle(0,0,Width-1,Height-1),10)) using (SolidBrush b = new SolidBrush(Enabled ? Fill : Color.FromArgb(183,189,207))) e.Graphics.FillPath(b,p);
            TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,ForeColor,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
        }
    }
    public sealed class MainForm : Form {
        readonly Color ink = Color.FromArgb(30,38,59), muted = Color.FromArgb(110,120,143), accent = Color.FromArgb(89,101,233);
        readonly string dataFolder, settingsPath;
        Settings settings;
        Engine engine;
        Task work;
        bool ru, busy, closeWhenStopped;
        readonly Timer timer = new Timer();
        TextBox folder;
        ComboBox mode;
        NumericUpDown idle;
        FlatButton browse, start, update, support;
        Button language;
        Label subtitle, guideTitle, guideText, folderCaption, modeCaption, idleCaption, phase, detail, available, footer;
        Label modeReadout;
        Label[] metricTitle = new Label[4], metricValue = new Label[4];
        DataGridView categories;
        TabControl tabs;
        TextBox log;
        ProgressBar progress;
        ToolTip tip = new ToolTip();
        public MainForm(string data) {
            dataFolder=data; settingsPath=Path.Combine(data,"settings.json"); Directory.CreateDirectory(data);
            try { settings = File.Exists(settingsPath) ? new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(settingsPath)) : new Settings(); } catch { settings=new Settings(); }
            settings.SupportUrl=new Settings().SupportUrl;
            settings.GitHubReleasesUrl=new Settings().GitHubReleasesUrl;
            ru=settings.Language!="en";
            Text="Yandex Disk Rescue"; Font=new Font("Segoe UI",10); ForeColor=ink; BackColor=Color.FromArgb(244,246,251);
            Size=new Size(1120,850); MinimumSize=new Size(1000,790); StartPosition=FormStartPosition.CenterScreen;
            AutoScaleMode=AutoScaleMode.Dpi;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Build(); Translate(); engine=new Engine(dataFolder);
            timer.Interval=400; timer.Tick += delegate { Render(engine.Latest); };
            FormClosing += delegate(object sender, FormClosingEventArgs args) {
                if (busy) { args.Cancel=true; closeWhenStopped=true; engine.Stop(); start.Enabled=false; phase.Text=T("Останавливаем запросы перед выходом…","Stopping requests before exit…"); }
            };
        }
        string T(string r,string e) { return ru?r:e; }
        Label Label(string text, float size, bool bold) { return new Label { Text=text, AutoSize=false, Dock=DockStyle.Fill, Font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular), ForeColor=ink, BackColor=Color.Transparent, TextAlign=ContentAlignment.MiddleLeft }; }
        TableLayoutPanel Table(int columns) { return new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=columns, Margin=Padding.Empty, BackColor=Color.Transparent }; }
        RoundPanel Card() { return new RoundPanel { Dock=DockStyle.Fill, Margin=new Padding(0,0,0,12) }; }
        void Build() {
            TableLayoutPanel page=Table(1); page.Padding=new Padding(24,14,24,12); page.RowCount=7;
            foreach(float h in new float[]{72,118,150,90,76}) page.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
            page.RowStyles.Add(new RowStyle(SizeType.Percent,100)); page.RowStyles.Add(new RowStyle(SizeType.Absolute,34)); Controls.Add(page);
            TableLayoutPanel header=Table(2); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100));
            header.RowCount=1;header.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            TableLayoutPanel brand=Table(1); brand.RowCount=2; brand.RowStyles.Add(new RowStyle(SizeType.Absolute,38)); brand.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            Label title=Label("Yandex Disk Rescue",23,true); brand.Controls.Add(title,0,0); subtitle=Label("",10,false); subtitle.ForeColor=muted; brand.Controls.Add(subtitle,0,1);
            language=new Button { Dock=DockStyle.Fill, Margin=new Padding(6,12,0,16), FlatStyle=FlatStyle.Flat, BackColor=Color.White, Cursor=Cursors.Hand }; language.FlatAppearance.BorderColor=Color.FromArgb(220,225,237);
            language.Click += delegate { ru=!ru; settings.Language=ru?"ru":"en"; SaveSettings(); Translate(); Render(engine.Latest); };
            header.Controls.Add(brand,0,0); header.Controls.Add(language,1,0); page.Controls.Add(header,0,0);
            RoundPanel guide=Card(); guide.Fill=Color.FromArgb(231,236,255); guide.Padding=new Padding(18,10,18,10);
            TableLayoutPanel guideRows=Table(1); guideRows.RowCount=2; guideRows.RowStyles.Add(new RowStyle(SizeType.Absolute,26)); guideRows.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            guideTitle=Label("",11,true); guideText=Label("",9,false); guideText.ForeColor=Color.FromArgb(60,73,118);
            guideRows.Controls.Add(guideTitle,0,0); guideRows.Controls.Add(guideText,0,1); guide.Controls.Add(guideRows); page.Controls.Add(guide,0,1);
            RoundPanel setup=Card(); setup.Padding=new Padding(18,10,18,10); TableLayoutPanel config=Table(3); config.RowCount=4;
            config.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); config.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,210)); config.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,170));
            config.RowStyles.Add(new RowStyle(SizeType.Absolute,21)); config.RowStyles.Add(new RowStyle(SizeType.Absolute,36)); config.RowStyles.Add(new RowStyle(SizeType.Absolute,22)); config.RowStyles.Add(new RowStyle(SizeType.Absolute,40));
            folderCaption=Label("",9,true); config.Controls.Add(folderCaption,0,0); config.SetColumnSpan(folderCaption,2);
            folder=new TextBox { Dock=DockStyle.Fill, Text=settings.Folder, BorderStyle=BorderStyle.FixedSingle, Font=new Font("Segoe UI",11), Margin=new Padding(0,4,10,0) }; config.Controls.Add(folder,0,1); config.SetColumnSpan(folder,2);
            browse=new FlatButton { Dock=DockStyle.Fill, Fill=Color.FromArgb(234,237,247), ForeColor=ink, Margin=new Padding(0,0,0,2) }; browse.Click += ChooseFolder; config.Controls.Add(browse,2,1);
            modeCaption=Label("",9,true); config.Controls.Add(modeCaption,0,2);
            idleCaption=Label("",9,true); config.Controls.Add(idleCaption,1,2);
            Panel modeBox=new Panel{Dock=DockStyle.Fill,Margin=new Padding(0,3,10,0),BackColor=Color.White};config.Controls.Add(modeBox,0,3);
            mode=new ComboBox { Dock=DockStyle.Fill, DropDownStyle=ComboBoxStyle.DropDownList, Font=new Font("Segoe UI",10) };modeBox.Controls.Add(mode);
            modeReadout=Label("",10,false);modeReadout.Padding=new Padding(5,0,5,0);modeReadout.BorderStyle=BorderStyle.None;modeReadout.BackColor=Color.FromArgb(244,246,251);modeReadout.ForeColor=muted;modeReadout.Visible=false;modeBox.Controls.Add(modeReadout);
            mode.DrawMode=DrawMode.OwnerDrawFixed;mode.ItemHeight=24;mode.DrawItem+=delegate(object sender,DrawItemEventArgs args){if(args.Index<0)return;args.DrawBackground();TextRenderer.DrawText(args.Graphics,Convert.ToString(mode.Items[args.Index]),mode.Font,args.Bounds,ink,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);args.DrawFocusRectangle();};
            idle=new NumericUpDown { Minimum=1,Maximum=60,Value=Math.Max(1,Math.Min(60,settings.IdleSeconds)),Dock=DockStyle.Fill,Margin=new Padding(0,3,10,0) }; config.Controls.Add(idle,1,3);
            start=new FlatButton { Dock=DockStyle.Fill,Margin=new Padding(0,0,0,1) }; start.Click += StartStop; config.Controls.Add(start,2,3);
            setup.Controls.Add(config); page.Controls.Add(setup,0,2);
            TableLayoutPanel metrics=Table(4); for(int i=0;i<4;i++) metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));
            metrics.RowCount=1;metrics.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            for(int i=0;i<4;i++) {
                RoundPanel card=Card(); card.Margin=new Padding(i==0?0:6,0,i==3?0:6,10); card.Padding=new Padding(16,8,16,8);
                TableLayoutPanel rows=Table(1); rows.RowCount=2;rows.RowStyles.Add(new RowStyle(SizeType.Absolute,22)); rows.RowStyles.Add(new RowStyle(SizeType.Percent,100));
                metricTitle[i]=Label("",9,false); metricTitle[i].ForeColor=muted; metricValue[i]=Label("0",24,true);
                metricValue[i].ForeColor=i==1?Color.FromArgb(24,142,117):i==3?Color.FromArgb(198,96,91):ink;
                rows.Controls.Add(metricTitle[i],0,0); rows.Controls.Add(metricValue[i],0,1); card.Controls.Add(rows); metrics.Controls.Add(card,i,0);
            }
            page.Controls.Add(metrics,0,3);
            TableLayoutPanel status=Table(2); status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); status.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,265));
            status.RowCount=1;status.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            TableLayoutPanel statusLeft=Table(1); statusLeft.RowStyles.Add(new RowStyle(SizeType.Absolute,24)); statusLeft.RowStyles.Add(new RowStyle(SizeType.Absolute,19)); statusLeft.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            phase=Label("",11,true); detail=Label("",8.8f,false); detail.ForeColor=muted;
            progress=new ProgressBar { Dock=DockStyle.Fill,Height=5,Margin=new Padding(0,5,18,10),Style=ProgressBarStyle.Continuous };
            statusLeft.Controls.Add(phase,0,0); statusLeft.Controls.Add(detail,0,1); statusLeft.Controls.Add(progress,0,2); status.Controls.Add(statusLeft,0,0);
            TableLayoutPanel options=Table(1); options.RowStyles.Add(new RowStyle(SizeType.Absolute,27)); options.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            available=Label("",9,false); available.TextAlign=ContentAlignment.MiddleRight; available.ForeColor=muted; options.Controls.Add(available,0,1); status.Controls.Add(options,1,0); page.Controls.Add(status,0,4);
            RoundPanel body=Card(); body.Margin=Padding.Empty; body.Padding=new Padding(12,8,12,8);
            tabs=new TabControl { Dock=DockStyle.Fill,Font=new Font("Segoe UI",10),Padding=new Point(16,6) };
            categories=Grid(); log=new TextBox { Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,BorderStyle=BorderStyle.None,BackColor=Color.White,ForeColor=muted,Font=new Font("Consolas",9) };
            TabPage cat=new TabPage();cat.BackColor=Color.White;cat.Controls.Add(categories);tabs.TabPages.Add(cat);
            TabPage events=new TabPage();events.BackColor=Color.White;events.Controls.Add(log);tabs.TabPages.Add(events);
            body.Controls.Add(tabs);page.Controls.Add(body,0,5);
            TableLayoutPanel bottom=Table(3);bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,150));bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,190));
            bottom.RowCount=1;bottom.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            footer=Label("",8.5f,false);footer.ForeColor=muted;
            update=new FlatButton { Dock=DockStyle.Fill,Fill=Color.FromArgb(234,237,247),ForeColor=ink,Margin=new Padding(0,5,8,0),Font=new Font("Segoe UI",9,FontStyle.Bold) };
            support=new FlatButton { Dock=DockStyle.Fill,Fill=Color.FromArgb(234,237,247),ForeColor=ink,Margin=new Padding(0,5,0,0),Font=new Font("Segoe UI",9,FontStyle.Bold) };
            update.Click+=delegate { if(!busy&&!String.IsNullOrEmpty(settings.GitHubReleasesUrl))System.Diagnostics.Process.Start(settings.GitHubReleasesUrl); };
            support.Click+=delegate { if(!String.IsNullOrEmpty(settings.SupportUrl))System.Diagnostics.Process.Start(settings.SupportUrl); };
            bottom.Controls.Add(footer,0,0);bottom.Controls.Add(update,1,0);bottom.Controls.Add(support,2,0);page.Controls.Add(bottom,0,6);
            Render(new Snapshot());
        }
        DataGridView Grid() {
            DataGridView g=new DataGridView { Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,RowHeadersVisible=false,BackgroundColor=Color.White,BorderStyle=BorderStyle.None,CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal,ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,EnableHeadersVisualStyles=false };
            g.ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle { BackColor=Color.FromArgb(246,247,251),ForeColor=muted,Font=new Font("Segoe UI",9,FontStyle.Bold),Padding=new Padding(6) };
            g.DefaultCellStyle=new DataGridViewCellStyle { BackColor=Color.White,ForeColor=ink,SelectionBackColor=Color.FromArgb(232,236,255),SelectionForeColor=ink,Font=new Font("Segoe UI",9),Padding=new Padding(6,2,6,2) };
            g.RowTemplate.Height=31;g.ColumnHeadersHeight=34;g.GridColor=Color.FromArgb(241,243,249);return g;
        }
        void Translate() {
            int selectedMode=mode.SelectedIndex<0?0:mode.SelectedIndex;
            subtitle.Text=T("Скачать доступные файлы. Сохранить результат. Повторить остальные.","Download available files. Keep results. Retry the rest.");
            language.Text=ru?"RU  /  EN":"EN  /  RU";
            PrerequisiteStatus environment=Prerequisites.Check();
            guideTitle.Text=environment.ClientRunning?T("Перед запуском · клиент Диска запущен","Before you start · Disk client is running"):environment.ClientInstalled?T("Перед запуском · запустите клиент Диска","Before you start · start the Disk client"):T("Перед запуском · установите клиент Диска","Before you start · install the Disk client");
            guideText.Text=T("Установите Яндекс Диск и войдите в аккаунт. Перед запуском дождитесь окончания синхронизации Диска и остановите другие загрузчики.\nВо время работы приложение будет перезапускать Яндекс Диск при ошибках синхронизации.\nБезлимитная автозагрузка не поддерживается: нужны файлы из раздела «Файлы», видимые в локальной папке Диска.","Install Yandex Disk and sign in. Wait for Disk synchronization to finish before starting, and stop other download tools.\nDuring the pass, the app will restart Yandex Disk whenever sync errors occur.\nUnlimited photo/video auto-upload is not supported: files must be in the Files section and visible in the local Disk folder.");
            folderCaption.Text=T("Папка Яндекс Диска · включая все подпапки","Yandex Disk folder · including all subfolders");
            browse.Text=T("Выбрать папку","Browse folder");modeCaption.Text=T("Режим прохода","Pass mode");idleCaption.Text=T("Без прогресса, сек","Idle timeout, sec");
            mode.Items.Clear();mode.Items.AddRange(new object[]{T("Продолжить · пропускать успешные","Continue · skip successful"),T("Проверить всё заново","Recheck everything"),T("Только ошибки синхронизации","Retry sync errors only"),T("Только файлы с нулевой загрузкой","Only files with zero downloaded bytes")});mode.SelectedIndex=selectedMode;
            modeReadout.Text=Convert.ToString(mode.SelectedItem);
            tip.SetToolTip(mode,T("Каждый проход заново сканирует папку. Одинаковые имена в разных папках — разные файлы. «Всё заново» проверяет локальные файлы, но не удаляет их и не скачивает повторно.","Every pass rescans the folder. Same names in different folders remain separate. Recheck everything verifies local files without deleting or downloading them again."));
            start.Text=busy?T("Остановить","Stop"):T("Начать проход","Start pass");update.Text=T("Обновление","Updates");support.Text=T("Поддержать автора","Support the author");support.Enabled=!String.IsNullOrEmpty(settings.SupportUrl);
            metricTitle[0].Text=T("Найдено файлов","Files found");metricTitle[1].Text=T("Скачано за проход","Downloaded this pass");metricTitle[2].Text=T("Осталось скачать","Remaining to download");metricTitle[3].Text=T("Ошибки синхронизации · за проход","Sync errors · this pass");
            tabs.TabPages[0].Text=T("Категории","Categories");tabs.TabPages[1].Text=T("Журнал","Activity log");
            categories.Columns.Clear();foreach(string c in new string[]{T("Тип","Type"),T("Всего","Total"),T("Скачаны","Downloaded total"),T("Скачано за текущий проход","Downloaded this pass"),T("Осталось","Remaining"),T("Нулевая загрузка","Zero download"),T("Ошибки синхронизации","Sync errors")})categories.Columns.Add(c,c);
            categories.Columns[0].FillWeight=145;categories.Columns[3].FillWeight=155;categories.Columns[5].FillWeight=110;categories.Columns[6].FillWeight=145;categories.ColumnHeadersDefaultCellStyle.WrapMode=DataGridViewTriState.True;categories.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.EnableResizing;categories.ColumnHeadersHeight=48;
            footer.Text="";
        }
        void ChooseFolder(object sender,EventArgs args) {
            using(FolderBrowserDialog d=new FolderBrowserDialog()) {
                d.Description=T("Выберите локальную папку Яндекс Диска","Choose the local Yandex Disk folder");d.SelectedPath=folder.Text;
                if(d.ShowDialog(this)==DialogResult.OK){folder.Text=d.SelectedPath;settings.Folder=d.SelectedPath;SaveSettings();}
            }
        }
        void SaveSettings() { try { File.WriteAllText(settingsPath,new JavaScriptSerializer().Serialize(settings)); } catch { } }
        void StartStop(object sender,EventArgs args) {
            if(busy){engine.Stop();start.Enabled=false;phase.Text=T("Завершаем и отменяем текущие запросы…","Finishing and cancelling current requests…");return;}
            if(!Directory.Exists(folder.Text)){MessageBox.Show(this,T("Выберите существующую локальную папку Диска.","Choose an existing local Disk folder."),Text,MessageBoxButtons.OK,MessageBoxIcon.Information);return;}
            PrerequisiteStatus environment=Prerequisites.Check();
            if(!environment.PlatformReady){MessageBox.Show(this,T("Системный механизм Cloud Files недоступен. Требуются Windows 10 1709+ / Windows 11 x64 и .NET Framework 4.7.1+.","Cloud Files is unavailable. Windows 10 1709+ / Windows 11 x64 and .NET Framework 4.7.1+ are required."),Text);return;}
            if(!environment.ClientRunning){
                string message=environment.ClientInstalled?T("Яндекс Диск установлен, но не запущен. Запустить клиент? Войдите в аккаунт, затем повторите запуск прохода.","Yandex Disk is installed but not running. Start the client? Sign in, then start the pass again."):T("Клиент Яндекс Диска не найден. Открыть официальную страницу установки? После установки войдите в аккаунт.","Yandex Disk client was not found. Open the official download page? Install it and sign in first.");
                if(MessageBox.Show(this,message,Text,MessageBoxButtons.YesNo,MessageBoxIcon.Information)==DialogResult.Yes)System.Diagnostics.Process.Start(environment.ClientInstalled?environment.ClientPath:"https://disk.yandex.ru/download");
                return;
            }
            settings.Folder=Path.GetFullPath(folder.Text);settings.Concurrency=5;settings.IdleSeconds=(int)idle.Value;settings.RestartClient=true;SaveSettings();
            busy=true;SetBusy(true);phase.Text=T("Подготовка…","Preparing…");progress.Style=ProgressBarStyle.Marquee;timer.Start();
            RunMode runMode=(RunMode)mode.SelectedIndex;
            work=Task.Run(delegate { engine.Run(settings,runMode); });
            work.ContinueWith(delegate(Task completed){
                if(!IsDisposed)BeginInvoke(new Action(delegate {
                    timer.Stop();busy=false;SetBusy(false);Render(engine.Latest);
                    if(completed.IsFaulted)MessageBox.Show(this,completed.Exception.GetBaseException().Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Error);
                    if(closeWhenStopped)Close();
                }));
            });
        }
        void SetBusy(bool value) {
            folder.ReadOnly=value;browse.Enabled=mode.Enabled=idle.Enabled=!value;
            mode.Visible=!value;modeReadout.Visible=value;modeReadout.Text=Convert.ToString(mode.SelectedItem);if(value)modeReadout.BringToFront();
            start.Enabled=true;start.Fill=value?Color.FromArgb(217,94,108):accent;start.Text=value?T("Остановить","Stop"):T("Начать проход","Start pass");start.Invalidate();
        }
        string Category(string key) {
            switch(key){case"photos":return T("Фото и изображения","Photos & images");case"videos":return T("Видео","Videos");case"documents":return T("Документы","Documents");case"archives":return T("Архивы и образы","Archives & images");case"audio":return T("Аудио","Audio");default:return T("Другое","Other");}
        }
        string Phase(string key) {
            switch(key){case"preparing":return T("Готовим историю и список файлов…","Preparing history and file list…");case"scanning":return T("Сканируем папки…","Scanning folders…");case"running":return T("Проход выполняется","Pass in progress");case"restarting":return T("Перезапускаем Яндекс Диск…","Restarting Yandex Disk…");case"stopping":return T("Завершаем запросы…","Finishing requests…");case"completed":return T("Проход завершён","Pass completed");case"stopped":return T("Остановлено · история сохранена","Stopped · history saved");case"error":return T("Остановлено с ошибкой","Stopped with an error");default:return T("Готово к запуску","Ready to start");}
        }
        static string Bytes(long value){if(value>=1073741824)return(value/1073741824.0).ToString("0.0")+" GB";if(value>=1048576)return(value/1048576.0).ToString("0.0")+" MB";return(value/1024.0).ToString("0")+" KB";}
        void Render(Snapshot s) {
            metricValue[0].Text=s.Total.ToString("N0");metricValue[1].Text=s.Downloaded.ToString("N0");metricValue[2].Text=Math.Max(0,s.Total-s.Available).ToString("N0");metricValue[3].Text=s.CloudErrorsThisPass.ToString("N0");
            phase.Text=Phase(s.Phase);phase.ForeColor=s.Phase=="error"?Color.FromArgb(193,75,85):ink;
            if(!String.IsNullOrEmpty(s.Error))detail.Text=s.Error;
            else if(s.Phase=="scanning"||s.Phase=="preparing")detail.Text=s.Current;
            else if(s.Selected>0){string eta=s.PerHour>0?String.Format("{0}:{1:00}",(int)(s.RemainingSeconds/3600),(int)((s.RemainingSeconds%3600)/60)):"—";detail.Text=String.Format(T("Проверено {0}/{1} · скачано всего {2:N0} · {3:N0} файлов/ч · осталось ≈ {4}","Checked {0}/{1} · total downloaded {2:N0} · {3:N0} files/h · remaining ≈ {4}"),s.Processed,s.Selected,s.Available,s.PerHour,eta);}
            else if(s.Finished&&s.Selected==0)detail.Text=T("Для этого режима нет файлов. Для новых файлов выберите «Продолжить».","No files match this mode. Choose Continue to scan new files.");
            else detail.Text=T("Мониторинг начнётся автоматически вместе с проходом.","Monitoring starts automatically with the pass.");
            available.Text=String.Format(T("Скачано за проход {0:N0} · пропущено {1:N0}","Downloaded this pass {0:N0} · skipped {1:N0}"),s.Downloaded,s.Skipped);
            if(s.Phase=="scanning"||s.Phase=="preparing")progress.Style=ProgressBarStyle.Marquee;
            else{progress.Style=ProgressBarStyle.Continuous;progress.Maximum=Math.Max(1,s.Selected);progress.Value=Math.Min(progress.Maximum,s.Processed);}
            categories.Rows.Clear();foreach(CategoryStats c in s.Categories)categories.Rows.Add(Category(c.Category),c.Total,c.Available,c.Downloaded,c.Remaining,c.ZeroDownload,c.CloudErrors);
            string activity=String.Join(Environment.NewLine,s.Events.ToArray());if(log.Text!=activity){log.Text=activity;log.SelectionStart=log.TextLength;log.ScrollToCaret();}
            update.Enabled=!busy&&!String.IsNullOrEmpty(settings.GitHubReleasesUrl);
            if(s.RecoveryRequired){start.Enabled=false;detail.Text=T("Отмена не подтверждена. Перезапустите Яндекс Диск и это приложение перед новым проходом.","Cancellation not acknowledged. Restart Yandex Disk and this application before another pass.");}
            if(s.ScanErrors>0)footer.Text=String.Format(T("Недоступных папок/файлов при сканировании: {0}. Подробности в журнале.","Scan access errors: {0}. See the activity log."),s.ScanErrors);
        }
        public void Preview(){
            folder.Text=@"E:\Yandex.Disk";
            Snapshot s=new Snapshot{Phase="running",Total=30510,Selected=26797,Processed=1204,Downloaded=202,Available=704,Deferred=997,CloudErrors=5,CloudErrorsThisPass=5,Skipped=502,Active=3,PerHour=2677,RemainingSeconds=9.3*3600};
            s.Categories.Add(new CategoryStats{Category="photos",Total=21840,Available=528,Downloaded=168,Deferred=720,CloudErrors=3});s.Categories.Add(new CategoryStats{Category="videos",Total=7294,Available=140,Downloaded=27,Deferred=231,CloudErrors=2});s.Categories.Add(new CategoryStats{Category="documents",Total=620,Available=28,Downloaded=5,Deferred=32});s.Categories.Add(new CategoryStats{Category="archives",Total=412,Available=7,Downloaded=1,Deferred=9});s.Categories.Add(new CategoryStats{Category="audio",Total=208,Available=1,Downloaded=1,Deferred=5});s.Categories.Add(new CategoryStats{Category="other",Total=136});
            foreach(CategoryStats c in s.Categories){c.Remaining=c.Total-c.Available;c.ZeroDownload=c.Deferred;}busy=true;SetBusy(true);Render(s);
        }
        public void Preview(bool english){ru=!english;Translate();Preview();}
    }
}
