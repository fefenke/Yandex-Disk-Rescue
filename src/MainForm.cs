using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
    public sealed partial class MainForm : Form {
        readonly Color ink = Color.FromArgb(30,38,59), muted = Color.FromArgb(110,120,143), accent = Color.FromArgb(89,101,233);
        readonly string dataFolder, settingsPath;
        Settings settings;
        bool settingsReady,settingsRecovered;
        string settingsLoadWarning,settingsSaveWarning;
        Label settingsNotice;
        Engine engine;
        Task work;
        bool ru, busy, closeWhenStopped;
        readonly Timer timer = new Timer();
        System.Threading.CancellationTokenSource sessionStop;
        bool waitingForRetry,renderingFailed;
        PassTransition passTransition;
        CheckBox repeatProblems;NumericUpDown streamsPerFile,multiStreamThreshold;Label streamsCaption,thresholdCaption;
        DataGridView passes;
        readonly System.Collections.Generic.List<PassResult> passRows=new System.Collections.Generic.List<PassResult>();
        TextBox folder;
        ComboBox mode, method;
        TextBox apiToken, remoteFolder;
        Label methodCaption, apiCaption, remoteCaption;
        TableLayoutPanel setupGrid, pageGrid, credentials;
        bool translating;
        NumericUpDown idle,linkTimeout,linkConcurrency,downloadConcurrency,requestInterval;
        Label parallelCaption,linkConcurrencyCaption,downloadConcurrencyCaption;
        FlatButton browse, start, support, guideButton;
        Button language;
        Label subtitle, guideTitle, guideText, unlimitedWarning, folderCaption, modeCaption, idleCaption, linkTimeoutCaption, phase, detail, available, footer;
        Label modeReadout, methodReadout;
        LinkLabel tokenHelp;
        Label[] metricTitle = new Label[5], metricValue = new Label[5];
        DataGridView categories;
        TabControl tabs;
        TextBox log;FileDashboard dashboard;
        ProgressBar progress;
        ToolTip tip = new ToolTip();
        public MainForm(string data) {
            dataFolder=data; settingsPath=Path.Combine(data,"settings.json"); Directory.CreateDirectory(data);
            SettingsLoadResult loaded=SettingsStore.Load(settingsPath);settings=loaded.Value;
            settingsLoadWarning=loaded.Warning;settingsRecovered=loaded.RecoveredFromBackup;
            settings=settings??new Settings();settings.ApplyTransferDefaults();settings.UseApi=true;
            settings.SupportUrl=new Settings().SupportUrl;
            settings.GitHubReleasesUrl=new Settings().GitHubReleasesUrl;
            ru=settings.Language!="en";
            Text="Yandex Disk Rescue"; Font=new Font("Segoe UI",10); ForeColor=ink; BackColor=Color.FromArgb(244,246,251);
            Size=new Size(1120,1000); MinimumSize=new Size(1000,950); StartPosition=FormStartPosition.CenterScreen;
            AutoScaleMode=AutoScaleMode.Dpi;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Build(); BuildShell(); CenterSettingsFields(); Translate(); engine=new Engine(dataFolder);SetupUpdates();
            settingsReady=true;RefreshSettingsNotice();
            timer.Interval=400; timer.Tick += delegate { Render(engine.Latest); };
            FormClosing += delegate(object sender, FormClosingEventArgs args) {
                if (busy) { args.Cancel=true; closeWhenStopped=true; if(sessionStop!=null)sessionStop.Cancel(); engine.Stop(); start.Enabled=false; phase.Text=T("Останавливаем запросы перед выходом…","Stopping requests before exit…"); }
            };
        }
        string T(string r,string e) { return ru?r:e; }
        Label Label(string text, float size, bool bold) { return new Label { Text=text, AutoSize=false, Dock=DockStyle.Fill, Font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular), ForeColor=ink, BackColor=Color.Transparent, TextAlign=ContentAlignment.MiddleLeft }; }
        TableLayoutPanel Table(int columns) { return new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=columns, Margin=Padding.Empty, BackColor=Color.Transparent }; }
        RoundPanel Card() { return new RoundPanel { Dock=DockStyle.Fill, Margin=new Padding(0,0,0,12) }; }
        void Build() {
            TableLayoutPanel page=Table(1);pageGrid=page; page.Padding=new Padding(24,14,24,12); page.RowCount=7;
            foreach(float h in new float[]{72,76,150,90,100}) page.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
            page.RowStyles.Add(new RowStyle(SizeType.Percent,100)); page.RowStyles.Add(new RowStyle(SizeType.Absolute,34)); Controls.Add(page);
            TableLayoutPanel header=Table(2); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100));
            header.RowCount=1;header.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            TableLayoutPanel brand=Table(1); brand.RowCount=2; brand.RowStyles.Add(new RowStyle(SizeType.Absolute,28)); brand.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            Label title=Label("Yandex Disk Rescue",23,true); brand.Controls.Add(title,0,0); subtitle=Label("",10,false); subtitle.ForeColor=muted; brand.Controls.Add(subtitle,0,1);
            language=new Button { Dock=DockStyle.Fill, Margin=new Padding(6,12,0,16), FlatStyle=FlatStyle.Flat, BackColor=Color.White, Cursor=Cursors.Hand }; language.FlatAppearance.BorderColor=Color.FromArgb(220,225,237);
            language.Click += delegate { ru=!ru; settings.Language=ru?"ru":"en"; SaveSettings(); Translate(); Render(engine.Latest); };
            header.Controls.Add(brand,0,0); header.Controls.Add(language,1,0); page.Controls.Add(header,0,0);
            RoundPanel guide=Card(); guide.Fill=Color.FromArgb(231,236,255); guide.Padding=new Padding(18,10,18,10);
            TableLayoutPanel guideRows=Table(2);guideRows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));guideRows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,180));guideRows.RowCount=1;guideRows.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            guideTitle=Label("",11,true);guideText=Label("",9,false);unlimitedWarning=Label("",10,true);
            Label guidePrompt=Label("",11,true);guidePrompt.Name="guidePrompt";guideRows.Controls.Add(guidePrompt,0,0);
            guideButton=new FlatButton{Dock=DockStyle.Fill,Fill=accent,ForeColor=Color.White,Margin=new Padding(12,2,0,2)};guideButton.Click+=delegate{ShowGuide();};guideRows.Controls.Add(guideButton,1,0);
            guide.Controls.Add(guideRows);page.Controls.Add(guide,0,1);
            RoundPanel setup=Card(); setup.Padding=new Padding(18,10,18,10); TableLayoutPanel config=Table(3);setupGrid=config; config.RowCount=8;
            config.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); config.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,210)); config.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,170));
            config.RowStyles.Add(new RowStyle(SizeType.Absolute,21));config.RowStyles.Add(new RowStyle(SizeType.Absolute,36));config.RowStyles.Add(new RowStyle(SizeType.Absolute,60));config.RowStyles.Add(new RowStyle(SizeType.Absolute,21)); config.RowStyles.Add(new RowStyle(SizeType.Absolute,36)); config.RowStyles.Add(new RowStyle(SizeType.Absolute,22)); config.RowStyles.Add(new RowStyle(SizeType.Absolute,40));config.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
                        methodCaption=Label("",9,true);config.Controls.Add(methodCaption,0,0);config.SetColumnSpan(methodCaption,2);
            Panel methodBox=new Panel{Dock=DockStyle.Fill,Margin=new Padding(0,3,10,0),BackColor=Color.White};config.Controls.Add(methodBox,0,1);config.SetColumnSpan(methodBox,2);method=new ComboBox{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};methodBox.Controls.Add(method);methodReadout=Label("",10,false);methodReadout.Padding=new Padding(5,0,5,0);methodReadout.BackColor=Color.FromArgb(244,246,251);methodReadout.ForeColor=muted;methodReadout.Visible=false;methodBox.Controls.Add(methodReadout);
            tokenHelp=new LinkLabel{Text="OAuth ↗",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter};
            tokenHelp.LinkClicked+=delegate{System.Diagnostics.Process.Start("https://yandex.ru/dev/disk/poligon/");};config.Controls.Add(tokenHelp,2,1);
            credentials=Table(2);credentials.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,60));credentials.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,40));credentials.RowStyles.Add(new RowStyle(SizeType.Absolute,22));credentials.RowStyles.Add(new RowStyle(SizeType.Absolute,32));credentials.RowStyles.Add(new RowStyle(SizeType.Absolute,22));credentials.RowStyles.Add(new RowStyle(SizeType.Absolute,32));config.Controls.Add(credentials,0,2);config.SetColumnSpan(credentials,3);
            apiCaption=Label("",9,true);remoteCaption=Label("",9,true);credentials.Controls.Add(apiCaption,0,0);credentials.Controls.Add(remoteCaption,1,0);
            apiToken=new TextBox{Dock=DockStyle.Fill,UseSystemPasswordChar=true,Text=TokenVault.Load(dataFolder),Margin=new Padding(0,3,10,0)};
            ContextMenuStrip tokenMenu=new ContextMenuStrip();tokenMenu.Items.Add("Очистить токен / Clear token",null,delegate{if(busy)return;apiToken.Clear();settings.ApiToken="";try{TokenVault.Save(dataFolder,"");}catch{MessageBox.Show(this,T("Не удалось очистить сохранённый токен.","Could not clear the saved token."),Text);}});apiToken.ContextMenuStrip=tokenMenu;
            remoteFolder=new TextBox{Dock=DockStyle.Fill,Text=settings.RemoteFolder,Margin=new Padding(0,3,0,0)};
            credentials.Controls.Add(apiToken,0,1);credentials.Controls.Add(remoteFolder,1,1);
            BuildScanOptions();
            method.SelectedIndexChanged+=delegate{
                if(translating||method.SelectedIndex<0)return;
                if(settings.UseApi)settings.ApiFolder=folder.Text;else settings.Folder=folder.Text;
                settings.UseApi=true;folder.Text=settings.UseApi?settings.ApiFolder:settings.Folder;
                Translate();SaveSettings();
            };
            folderCaption=Label("",9,true); config.Controls.Add(folderCaption,0,3); config.SetColumnSpan(folderCaption,2);
            folder=new TextBox { Dock=DockStyle.Fill, Text=settings.UseApi?settings.ApiFolder:settings.Folder, BorderStyle=BorderStyle.FixedSingle, Font=new Font("Segoe UI",11), Margin=new Padding(0,4,10,0) }; config.Controls.Add(folder,0,4); config.SetColumnSpan(folder,2);
            browse=new FlatButton { Dock=DockStyle.Fill, Fill=Color.FromArgb(234,237,247), ForeColor=ink, Margin=new Padding(0,0,0,2) }; browse.Click += ChooseFolder; config.Controls.Add(browse,2,4);
            modeCaption=Label("",9,true); config.Controls.Add(modeCaption,0,5);
            linkTimeoutCaption=Label("",9,true);config.Controls.Add(linkTimeoutCaption,1,5);idleCaption=Label("",9,true);config.Controls.Add(idleCaption,2,5);
            Panel modeBox=new Panel{Dock=DockStyle.Fill,Margin=new Padding(0,3,10,0),BackColor=Color.White};config.Controls.Add(modeBox,0,6);
            mode=new ComboBox { Dock=DockStyle.Fill, DropDownStyle=ComboBoxStyle.DropDownList, Font=new Font("Segoe UI",10) };modeBox.Controls.Add(mode);
            modeReadout=Label("",10,false);modeReadout.Padding=new Padding(5,0,5,0);modeReadout.BorderStyle=BorderStyle.None;modeReadout.BackColor=Color.FromArgb(244,246,251);modeReadout.ForeColor=muted;modeReadout.Visible=false;modeBox.Controls.Add(modeReadout);
            mode.DrawMode=DrawMode.OwnerDrawFixed;mode.ItemHeight=24;mode.DrawItem+=delegate(object sender,DrawItemEventArgs args){if(args.Index<0)return;args.DrawBackground();TextRenderer.DrawText(args.Graphics,Convert.ToString(mode.Items[args.Index]),mode.Font,args.Bounds,ink,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);args.DrawFocusRectangle();};
            idle=new NumericUpDown { Minimum=1,Maximum=60,Value=Math.Max(1,Math.Min(60,settings.IdleSeconds)),Dock=DockStyle.Fill,Margin=new Padding(0,3,10,0) }; config.Controls.Add(idle,2,6);
            linkTimeout=new NumericUpDown{Minimum=1,Maximum=60,Value=Math.Max(1,Math.Min(60,settings.LinkTimeoutSeconds)),Dock=DockStyle.Fill,Margin=new Padding(0,3,10,0)};config.Controls.Add(linkTimeout,1,6);
            idle.ValueChanged+=delegate{if(!busy){settings.IdleSeconds=(int)idle.Value;SaveSettings();}};linkTimeout.ValueChanged+=delegate{if(!busy){settings.LinkTimeoutSeconds=(int)linkTimeout.Value;SaveSettings();}};
            config.RowCount=10;config.RowStyles[7].Height=22;config.RowStyles.Add(new RowStyle(SizeType.Absolute,32));config.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
            parallelCaption=Label("",9,true);config.Controls.Add(parallelCaption,0,7);
            TableLayoutPanel launchOptions=Table(2);launchOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,120));launchOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));config.Controls.Add(launchOptions,0,8);
            requestInterval=new NumericUpDown{Minimum=25,Maximum=1000,Increment=25,Value=Math.Max(25,Math.Min(1000,settings.ApiRequestIntervalMs)),Dock=DockStyle.Fill,Margin=new Padding(0,3,10,0)};launchOptions.Controls.Add(requestInterval,0,0);
            requestInterval.ValueChanged+=delegate{if(!busy){settings.ApiRequestIntervalMs=(int)requestInterval.Value;SaveSettings();}};
            linkConcurrencyCaption=Label("",9,true);downloadConcurrencyCaption=Label("",9,true);config.Controls.Add(linkConcurrencyCaption,1,7);config.Controls.Add(downloadConcurrencyCaption,2,7);
            linkConcurrency=new NumericUpDown{Minimum=1,Maximum=30,Value=Math.Max(1,Math.Min(30,settings.ApiLinkConcurrency)),Dock=DockStyle.Fill,Margin=new Padding(0,3,10,0)};
            downloadConcurrency=new NumericUpDown{Minimum=1,Maximum=30,Value=Math.Max(1,Math.Min(30,settings.ApiDownloadConcurrency)),Dock=DockStyle.Fill,Margin=new Padding(0,3,10,0)};
            config.Controls.Add(linkConcurrency,1,8);config.Controls.Add(downloadConcurrency,2,8);
            linkConcurrency.ValueChanged+=delegate{if(!busy){settings.ApiLinkConcurrency=(int)linkConcurrency.Value;SaveSettings();}};downloadConcurrency.ValueChanged+=delegate{if(!busy){settings.ApiDownloadConcurrency=(int)downloadConcurrency.Value;SaveSettings();}};
            start=new FlatButton { Dock=DockStyle.Fill,Margin=new Padding(0,0,0,1) }; start.Click += StartStop; config.Controls.Add(start,2,9);
            setup.Controls.Add(config); page.Controls.Add(setup,0,2);
            TableLayoutPanel metrics=Table(5); for(int i=0;i<5;i++) metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,20));
            metrics.RowCount=1;metrics.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            for(int i=0;i<5;i++) {
                RoundPanel card=Card(); card.Margin=new Padding(i==0?0:6,0,i==4?0:6,10); card.Padding=new Padding(16,8,16,8);
                TableLayoutPanel rows=Table(1); rows.RowCount=2;rows.RowStyles.Add(new RowStyle(SizeType.Absolute,22)); rows.RowStyles.Add(new RowStyle(SizeType.Percent,100));
                metricTitle[i]=Label("",9,false); metricTitle[i].ForeColor=muted; metricValue[i]=Label("0",24,true);
                metricValue[i].ForeColor=(i==1||i==2)?Color.FromArgb(24,142,117):i==4?Color.FromArgb(198,96,91):ink;
                rows.Controls.Add(metricTitle[i],0,0); rows.Controls.Add(metricValue[i],0,1); card.Controls.Add(rows); metrics.Controls.Add(card,i,0);
            }
            page.Controls.Add(metrics,0,3);
            TableLayoutPanel status=Table(2); status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); status.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,265));
            status.RowCount=1;status.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            TableLayoutPanel statusLeft=Table(1); statusLeft.RowStyles.Add(new RowStyle(SizeType.Absolute,24)); statusLeft.RowStyles.Add(new RowStyle(SizeType.Absolute,19)); statusLeft.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            phase=Label("",11,true); detail=Label("",8.8f,false); detail.ForeColor=muted;
            progress=new ProgressBar { Dock=DockStyle.Fill,Height=5,Margin=new Padding(0,5,18,10),Style=ProgressBarStyle.Continuous };
            statusLeft.Controls.Add(phase,0,0); statusLeft.Controls.Add(detail,0,1); statusLeft.Controls.Add(progress,0,2); status.Controls.Add(statusLeft,0,0);
            TableLayoutPanel options=Table(1); options.RowStyles.Add(new RowStyle(SizeType.Absolute,27));options.RowStyles.Add(new RowStyle(SizeType.Absolute,27)); options.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            repeatProblems=new CheckBox{Checked=settings.RepeatProblemFiles,Dock=DockStyle.Fill,AutoSize=false,Font=new Font("Segoe UI",9)};options.Controls.Add(repeatProblems,0,0);repeatProblems.CheckedChanged+=delegate{if(!busy){settings.RepeatProblemFiles=repeatProblems.Checked;SaveSettings();}};
            available=Label("",9,false); available.TextAlign=ContentAlignment.MiddleRight; available.ForeColor=muted; options.Controls.Add(available,0,2); status.Controls.Add(options,1,0); page.Controls.Add(status,0,4);
            RoundPanel body=Card(); body.Margin=Padding.Empty; body.Padding=new Padding(12,8,12,8);
            tabs=new TabControl { Dock=DockStyle.Fill,Font=new Font("Segoe UI",10),Padding=new Point(16,6) };
            categories=Grid(); log=new StableLog { Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,BorderStyle=BorderStyle.None,BackColor=Color.White,ForeColor=muted,Font=new Font("Consolas",9) };
            TabPage cat=new TabPage();cat.BackColor=Color.White;cat.Controls.Add(categories);tabs.TabPages.Add(cat);
            TabPage events=new TabPage();events.BackColor=Color.White;events.Controls.Add(log);tabs.TabPages.Add(events);
            passes=Grid();TabPage passTab=new TabPage();passTab.BackColor=Color.White;passTab.Controls.Add(passes);tabs.TabPages.Add(passTab);passRows.AddRange(PassHistory.Load(Path.Combine(dataFolder,"passes.jsonl")));
            dashboard=new FileDashboard();TabPage fileTab=new TabPage();fileTab.BackColor=Color.White;fileTab.Controls.Add(dashboard);tabs.TabPages.Add(fileTab);tabs.SelectedIndexChanged+=delegate{if(tabs.SelectedIndex==3){if(engine!=null&&busy&&engine.Latest.Catalog!=null)dashboard.Live(engine.Latest.Catalog,settings.UseApi?settings.ApiFolder:settings.Folder);else dashboard.LoadSaved(dataFolder,settings.UseApi,folder.Text,remoteFolder.Text);}};
            body.Controls.Add(tabs);page.Controls.Add(body,0,5);
            TableLayoutPanel bottom=Table(2);bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,190));
            bottom.RowCount=1;bottom.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            footer=Label("",8.5f,false);footer.ForeColor=muted;
            support=new FlatButton { Dock=DockStyle.Fill,Fill=Color.FromArgb(234,237,247),ForeColor=ink,Margin=new Padding(0,5,0,0),Font=new Font("Segoe UI",9,FontStyle.Bold) };
            support.Click+=delegate { if(!String.IsNullOrEmpty(settings.SupportUrl))System.Diagnostics.Process.Start(settings.SupportUrl); };
            bottom.Controls.Add(footer,0,0);bottom.Controls.Add(support,1,0);page.Controls.Add(bottom,0,6);
            Render(new Snapshot());
        }
        DataGridView Grid() {
            DataGridView g=new BufferedGrid { Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,RowHeadersVisible=false,BackgroundColor=Color.White,BorderStyle=BorderStyle.None,CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal,ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,EnableHeadersVisualStyles=false };
            g.ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle { BackColor=Color.FromArgb(246,247,251),ForeColor=muted,Font=new Font("Segoe UI",9,FontStyle.Bold),Padding=new Padding(6) };
            g.DefaultCellStyle=new DataGridViewCellStyle { BackColor=Color.White,ForeColor=ink,SelectionBackColor=Color.FromArgb(232,236,255),SelectionForeColor=ink,Font=new Font("Segoe UI",9),Padding=new Padding(6,2,6,2) };
            g.RowTemplate.Height=31;g.ColumnHeadersHeight=34;g.GridColor=Color.FromArgb(241,243,249);return g;
        }
        void Translate() {
            unlimitedWarning.Text=T("Файлы из безлимитной автозагрузки фото и видео НЕ УЧИТЫВАЮТСЯ И НЕ СКАЧИВАЮТСЯ.\nПоддерживаются только файлы из обычного раздела «Файлы».","Unlimited photo/video auto-upload files are NOT COUNTED OR DOWNLOADED.\nOnly files in the regular Files section are supported.");
            translating=true;method.Items.Clear();method.Items.AddRange(new object[]{T("Через API · OAuth-токен","Via API · OAuth token")});method.SelectedIndex=0;translating=false;
            methodCaption.Text=T("Способ скачивания","Download method");apiCaption.Text=T("OAuth-токен · хранится зашифрованно Windows","OAuth token · encrypted by Windows");remoteCaption.Text=T("Облачная папка · disk:/ — весь Диск","Cloud folder · disk:/ — whole Disk");
            TranslateScanOptions();
            int selectedMode=mode.SelectedIndex<0?1:mode.SelectedIndex;
            subtitle.Text=T("Скачать доступные файлы. Сохранить результат. Повторить остальные.","Download available files. Keep results. Retry the rest.");
            guideButton.Text=T("Открыть гайд","Open guide");Control[] prompts=Controls.Find("guidePrompt",true);if(prompts.Length>0)prompts[0].Text=T("Перед началом работы ознакомьтесь с гайдом","Before you start, please read the guide");language.Text=ru?"RU  /  EN":"EN  /  RU";
            PrerequisiteStatus environment=Prerequisites.Check();
            guideTitle.Text=environment.ClientRunning?T("Перед запуском · клиент Диска запущен","Before you start · Disk client is running"):environment.ClientInstalled?T("Перед запуском · запустите клиент Диска","Before you start · start the Disk client"):T("Перед запуском · установите клиент Диска","Before you start · install the Disk client");
            guideText.Text=T("1. Выберите способ ниже: клиент Диска на компьютере или API с токеном.\n2. Для клиента: установите Яндекс Диск, войдите в аккаунт и дождитесь окончания синхронизации.\n3. Укажите локальную папку Диска со всеми подпапками и нажмите «Начать проход». При ошибках клиент перезапускается.\nБезлимитная автозагрузка не поддерживается. «Продолжить» пропускает завершённые попытки. Ошибки повторяются отдельными режимами.","1. Choose desktop Disk client or API with an OAuth token below.\n2. For the client: install Yandex Disk, sign in and wait for synchronization to finish.\n3. Select the local Disk folder, including subfolders, then Start pass. The client restarts on sync errors.\nUnlimited auto-upload is unsupported. Continue skips completed attempts. Use retry modes for errors.");
            folderCaption.Text=T("Папка Яндекс Диска · включая все подпапки","Yandex Disk folder · including all subfolders");
            browse.Text=T("Выбрать папку","Browse folder");modeCaption.Text=T("Режим прохода","Pass mode");idleCaption.Text=T("Без данных файла, сек","File idle timeout, sec");linkTimeoutCaption.Text=T("Ожидание ссылки, сек","Link timeout, sec");
            mode.Items.Clear();mode.Items.AddRange(new object[]{T("Проверить все файлы","Check all files"),T("Скачать всё недокачанное","Download all remaining files"),T("Скачать файлы с ошибками загрузки","Download files with download errors"),T("Докачать неполные файлы","Resume partial files"),T("Скачать файлы с несовпавшим хешем","Download files with checksum mismatches")});mode.SelectedIndex=Math.Max(0,Math.Min(4,selectedMode));
            modeReadout.Text=busy&&integrityPass?T("Файлы с несовпавшим хешем","Files with checksum mismatches"):Convert.ToString(mode.SelectedItem);
            tip.SetToolTip(mode,T("«Проверить все файлы» заново читает локальные файлы и сверяет доступный облачный хеш. Остальные режимы используют историю для неизменённых готовых файлов. Обновление облачного списка настраивается отдельными галочками.","Check all files rereads local files against available cloud hashes. Other modes reuse history for unchanged completed files. Cloud listing is controlled separately."));
            tip.SetToolTip(linkTimeout,T("Ожидание ответа API после отправки запроса. Очередь и выбранный интервал запуска не учитываются. Другие файлы скачиваются одновременно.","API response timeout after sending the request. Queueing and the selected launch interval do not count. Other files download concurrently."));tip.SetToolTip(idle,T("Ожидание ответа скачивания или новых данных. Начинается при запуске скачивания и сбрасывается при получении заголовков и новых байтов. Это не предел общего времени загрузки.","Wait for download response or new data. Starts when downloading begins and resets on response headers and new bytes. Not a total download duration limit."));
            parallelCaption.Text=T("Интервал запросов API, мс","API launch interval, ms");linkConcurrencyCaption.Text=T("Запросов ссылок","Link requests");downloadConcurrencyCaption.Text=T("Соединений скачивания","Download connections");
            tip.SetToolTip(requestInterval,T("Базовый интервал 25–1000 мс, по умолчанию 50. При ответах API 429, 408 или 5xx программа учитывает Retry-After и временно замедляет новые запросы, затем постепенно восстанавливает частоту. Настройка не меняется. Очередь и пауза не расходуют таймаут ответа; скачивания по готовым ссылкам не отменяются.","Base interval 25–1000 ms; default 50. API HTTP 429, 408 or 5xx triggers Retry-After cooldown and temporary slowdown, followed by gradual recovery. The setting is unchanged. Queueing and cooldown do not consume response timeout; ready-link downloads are not cancelled."));
            tip.SetToolTip(linkConcurrency,T("От 1 до 30 одновременных запросов ссылок. По умолчанию 7. Это независимая очередь; увеличение может ускорить подготовку ссылок, но не гарантирует меньше таймаутов.","1–30 simultaneous link requests. Default 7. Independent queue; more requests can prepare links faster but do not guarantee fewer timeouts."));
            tip.SetToolTip(downloadConcurrency,T("От 1 до 30 соединений скачивания на все файлы и их части вместе. По умолчанию 20. Один крупный файл может использовать несколько соединений. На медленном интернете можно уменьшить.","1–30 download connections shared by all files and segments. Default 20. A large file can use multiple connections. Try fewer on a slow connection."));
            start.Text=busy?T("Остановить","Stop"):T("Начать проход","Start pass");support.Text=T("Поддержать автора","Support the author");support.Enabled=!String.IsNullOrEmpty(settings.SupportUrl);
            metricTitle[0].Text=T("Найдено файлов","Files found");metricTitle[1].Text=T("Скачано всего","Downloaded total");metricTitle[2].Text=T("Скачано за проход","Downloaded this pass");metricTitle[3].Text=T("Осталось скачать","Remaining to download");metricTitle[4].Text=T("Ошибки синхронизации · за проход","Sync errors · this pass");
            repeatProblems.Text=T("Бесконечные проходы","Continuous passes");tip.SetToolTip(repeatProblems,T("Следующий проход готовится без фиксированной паузы. Временная ошибка всего прохода вызывает замедление 1–30 секунд. Стоп отменяет и проход, и ожидание.","Prepare the next pass without a fixed pause. Temporary whole-pass failures back off for 1–30 seconds. Stop cancels both the pass and the wait."));tabs.TabPages[2].Text=T("Проходы","Pass history");RenderPasses();
            tabs.TabPages[3].Text=T("Файлы и папки","Files and folders");dashboard.Translate(ru);tabs.TabPages[0].Text=T("Категории","Categories");tabs.TabPages[1].Text=T("Журнал","Activity log");
            categories.Columns.Clear();foreach(string c in new string[]{T("Тип","Type"),T("Всего","Total"),T("Скачаны","Downloaded total"),T("Скачано за текущий проход","Downloaded this pass"),T("Осталось","Remaining"),T("Нет ответа / сбой загрузки","Timeout / download failure"),T("Ошибки синхронизации","Sync errors")})categories.Columns.Add(c,c);
            categories.Columns[0].FillWeight=145;categories.Columns[3].FillWeight=155;categories.Columns[5].FillWeight=110;categories.Columns[6].FillWeight=145;categories.ColumnHeadersDefaultCellStyle.WrapMode=DataGridViewTriState.True;categories.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.EnableResizing;categories.ColumnHeadersHeight=48;
            TranslateStreams();footer.Text="";ApplyMethod();TranslateShell();RefreshSettingsNotice();
        }
                void ApplyMethod(){
            bool api=settings.UseApi;credentials.Visible=api;tokenHelp.Visible=api;methodReadout.Text=Convert.ToString(method.SelectedItem);setupGrid.RowStyles[2].Height=api?120:0;pageGrid.RowStyles[2].Height=api?407:241;linkTimeout.Visible=linkTimeoutCaption.Visible=api;linkTimeout.Enabled=api&&!busy;
            parallelCaption.Visible=linkConcurrencyCaption.Visible=downloadConcurrencyCaption.Visible=linkConcurrency.Visible=downloadConcurrency.Visible=api;
            SetStreamsVisible(api);requestInterval.Parent.Visible=api;requestInterval.Enabled=linkConcurrency.Enabled=downloadConcurrency.Enabled=api&&!busy;setupGrid.RowStyles[7].Height=api?26:0;setupGrid.RowStyles[8].Height=api?32:0;setupGrid.SetColumnSpan(mode.Parent,api?1:2);
            TableLayoutPanel preferences=settingsCard==null?null:settingsCard.Parent as TableLayoutPanel;if(preferences!=null)preferences.RowStyles[1].Height=api?420:228;
            folderCaption.Text=api?T("Папка для сохранения · структура облачных папок сохраняется","Download folder · preserves cloud folder structure"):T("Папка Яндекс Диска · включая все подпапки","Yandex Disk folder · including all subfolders");
            if(api){metricTitle[4].Text=T("Ошибки за проход","Errors this pass");categories.Columns[6].HeaderText=T("Ошибки сервера","Server errors");guideTitle.Text=T("Перед запуском · API","Before you start · API");guideText.Text=T("Выберите локальную папку и режим. Галочки обновления облака определяют, когда искать новые и изменённые файлы. Если сохранённого списка нет, он будет получен автоматически.","Choose a local folder and a pass mode. Cloud refresh options control discovery of new and changed files. A missing saved list is fetched automatically.");}
        }
        void ShowGuide(){
            using(Form window=new Form{Text=T("Как пользоваться Yandex Disk Rescue","Yandex Disk Rescue guide"),Size=new Size(820,760),MinimumSize=new Size(640,500),StartPosition=FormStartPosition.CenterParent,Font=Font,BackColor=BackColor}){
                RichTextBox text=new RichTextBox{Dock=DockStyle.Fill,ReadOnly=true,BorderStyle=BorderStyle.None,BackColor=Color.White,Font=new Font("Segoe UI",11),ScrollBars=RichTextBoxScrollBars.Vertical};
                text.DetectUrls=true;text.LinkClicked+=delegate(object sender,LinkClickedEventArgs link){OpenGuideLink(link.LinkText);};
                text.Text=ru?GuideText.Ru:GuideText.En;
                int warningStart=text.Text.IndexOf(T("ВАЖНО:","IMPORTANT:"),StringComparison.Ordinal);int warningEnd=warningStart<0?-1:text.Text.IndexOf("\n\n",warningStart,StringComparison.Ordinal);if(warningEnd>warningStart&&warningStart>=0){text.Select(warningStart,warningEnd-warningStart);text.SelectionColor=Color.FromArgb(145,43,58);text.SelectionFont=new Font("Segoe UI",11,FontStyle.Bold);text.Select(0,0);}window.Padding=new Padding(20);window.Controls.Add(text);
                var links=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=36,Padding=new Padding(0,10,0,0),WrapContents=false};
                string[] names={T("Решения проекта","Project design"),T("Проверки и ограничения","Tests and limitations")};string[] documents={"DESIGN-DECISIONS.md","TESTING.md"};
                for(int i=0;i<names.Length;i++){string url="https://github.com/fefenke/Yandex-Disk-Rescue/blob/main/"+documents[i];var link=new LinkLabel{Text=names[i],AutoSize=true,Margin=new Padding(0,0,20,0)};link.LinkClicked+=delegate{OpenGuideLink(url);};links.Controls.Add(link);}
                window.Controls.Add(links);window.ShowDialog(this);
            }
        }
        void OpenGuideLink(string value){Uri url;if(!Uri.TryCreate(value,UriKind.Absolute,out url)||(url.Scheme!=Uri.UriSchemeHttps&&url.Scheme!=Uri.UriSchemeHttp))return;try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url.AbsoluteUri){UseShellExecute=true});}catch(Exception error){MessageBox.Show(this,T("Не удалось открыть ссылку: ","Could not open link: ")+error.Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Information);}}
        void ChooseFolder(object sender,EventArgs args) {
            using(FolderBrowserDialog d=new FolderBrowserDialog()) {
                d.Description=T("Выберите локальную папку Яндекс Диска","Choose the local Yandex Disk folder");d.SelectedPath=folder.Text;
                if(d.ShowDialog(this)==DialogResult.OK){folder.Text=d.SelectedPath;if(settings.UseApi)settings.ApiFolder=d.SelectedPath;else settings.Folder=d.SelectedPath;SaveSettings();}
            }
        }
        bool SaveSettings() {
            if(!settingsReady)return false;
            try{SettingsStore.Save(settingsPath,settings);settingsSaveWarning=null;settingsLoadWarning=null;RefreshSettingsNotice();return true;}
            catch(UnauthorizedAccessException){settingsSaveWarning="permission";}
            catch(IOException){settingsSaveWarning="io";}
            catch(Exception){settingsSaveWarning="other";}
            RefreshSettingsNotice();return false;
        }
        void RefreshSettingsNotice(){
            if(settingsNotice==null)return;
            string message="";
            if(settingsSaveWarning=="permission")message=T("Настройки не сохранены. Проверьте права записи и атрибут «Только чтение».","Settings not saved. Check write permissions and the read-only attribute.");
            else if(settingsSaveWarning=="io")message=T("Настройки не сохранены. Проверьте свободное место и не занят ли файл другой программой.","Settings not saved. Check free disk space and whether another program has locked the file.");
            else if(settingsSaveWarning!=null)message=T("Настройки не сохранены. Повторите изменение после устранения ошибки.","Settings not saved. Retry the change after resolving the error.");
            else if(settingsLoadWarning!=null)message=settingsRecovered?T("Настройки восстановлены из резервной копии. Основной файл пока не изменён.","Settings recovered from the backup. The primary file has not been changed."):T("Не удалось прочитать настройки. Используются значения по умолчанию; исходные файлы сохранены.","Settings could not be read. Defaults are in use; original files have been retained.");
            settingsNotice.Text=message;settingsNotice.Visible=message.Length>0;
            TableLayoutPanel parent=settingsNotice.Parent as TableLayoutPanel;
            if(parent!=null)parent.RowStyles[parent.GetRow(settingsNotice)].Height=message.Length>0?48:0;
        }
        void StartStop(object sender,EventArgs args) {
            if(busy){if(sessionStop!=null)sessionStop.Cancel();engine.Stop();start.Enabled=false;phase.Text=T("Завершаем и отменяем текущие запросы…","Finishing and cancelling current requests…");return;}
            if(settings.UseApi&&String.IsNullOrWhiteSpace(apiToken.Text)){MessageBox.Show(this,T("Укажите OAuth-токен.","Enter an OAuth token."),Text);return;}
            if(String.IsNullOrWhiteSpace(folder.Text)){MessageBox.Show(this,settings.UseApi?T("Укажите папку для сохранения.","Choose a download folder."):T("Укажите локальную папку Яндекс Диска.","Choose the local Yandex Disk folder."),Text);return;}
            if(!Directory.Exists(folder.Text)){MessageBox.Show(this,T("Укажите существующую локальную папку.","Choose an existing local folder."),Text);return;}
            PrerequisiteStatus environment=Prerequisites.Check();
            if(!environment.PlatformReady){MessageBox.Show(this,T("Системный механизм Cloud Files недоступен. Требуются Windows 10 1709+ / Windows 11 (x64/x86 build) и .NET Framework 4.7.1+.","Cloud Files is unavailable. Windows 10 1709+ / Windows 11 (x64/x86 build) and .NET Framework 4.7.1+ are required."),Text);return;}
            if(!settings.UseApi&&!environment.ClientRunning){
                string message=environment.ClientInstalled?T("Яндекс Диск установлен, но не запущен. Запустить клиент? Войдите в аккаунт, затем повторите запуск прохода.","Yandex Disk is installed but not running. Start the client? Sign in, then start the pass again."):T("Клиент Яндекс Диска не найден. Открыть официальную страницу установки? После установки войдите в аккаунт.","Yandex Disk client was not found. Open the official download page? Install it and sign in first.");
                if(MessageBox.Show(this,message,Text,MessageBoxButtons.YesNo,MessageBoxIcon.Information)==DialogResult.Yes)System.Diagnostics.Process.Start(environment.ClientInstalled?environment.ClientPath:"https://disk.yandex.ru/download");
                return;
            }
            if(settings.UseApi){settings.ScanCloudOnStart=scanCloudStart.Checked;settings.ScanCloudOnRepeats=scanCloudRepeats.Checked;settings.ApiFolder=Path.GetFullPath(folder.Text);settings.ApiToken=apiToken.Text.Trim();try{settings.RemoteFolder=ApiEngine.NormalizeRemote(remoteFolder.Text);TokenVault.Save(dataFolder,settings.ApiToken);}catch(Exception){MessageBox.Show(this,T("Проверьте облачный путь и доступ к хранилищу токена.","Check the cloud path and token storage permissions."),Text);return;}}else settings.Folder=Path.GetFullPath(folder.Text);settings.Concurrency=5;settings.IdleSeconds=(int)idle.Value;settings.LinkTimeoutSeconds=(int)linkTimeout.Value;settings.ApiLinkConcurrency=(int)linkConcurrency.Value;settings.ApiDownloadConcurrency=(int)downloadConcurrency.Value;settings.ApiRequestIntervalMs=(int)requestInterval.Value;settings.RepeatProblemFiles=repeatProblems.Checked;settings.RestartClient=true;if(!SaveSettings())return;

            integrityPass=mode.SelectedIndex==4;busy=true;SetBusy(true);phase.Text=T("Подготовка…","Preparing…");progress.Style=ProgressBarStyle.Marquee;timer.Start();
            RunMode runMode=integrityPass?RunMode.IntegrityFailures:new[]{RunMode.All,RunMode.Remaining,RunMode.DownloadErrors,RunMode.PartialFiles}[mode.SelectedIndex];
            sessionStop=new System.Threading.CancellationTokenSource();work=RunSession(runMode);
            work.ContinueWith(delegate(Task completed){
                if(!IsDisposed)BeginInvoke(new Action(delegate {
                    timer.Stop();waitingForRetry=false;busy=false;SetBusy(false);Render(engine.Latest);sessionStop.Dispose();sessionStop=null;
                    if(completed.IsFaulted){string diagnostic=CrashDiagnostics.Save(completed.Exception.GetBaseException());MessageBox.Show(this,completed.Exception.GetBaseException().GetType().Name+"\n"+diagnostic,Text,MessageBoxButtons.OK,MessageBoxIcon.Error);}
                    if(closeWhenStopped)Close();
                }));
            });
        }
        async Task RunSession(RunMode runMode) {
            bool repeat=repeatProblems.Checked,originalCache=settings.UseCachedApiList,automaticPass=false;int initialIdle=settings.IdleSeconds;RunMode initialMode=runMode;
            passTransition=new PassTransition();
            ApiPacer sessionPacer=new ApiPacer();
            SignedLinkCache sessionLinks=new SignedLinkCache();
            try {
                while(!sessionStop.IsCancellationRequested){
                    if(settings.UseApi)settings.UseCachedApiList=ScanSessionPolicy.UseCachedList(settings,automaticPass);
                    engine=new Engine(dataFolder,sessionPacer,sessionLinks);DateTime started=DateTime.UtcNow;
                    await Task.Run(delegate{if(!sessionStop.IsCancellationRequested)engine.Run(settings,runMode);});
                    Snapshot result=engine.Latest;
                    if(result.Finished){PassResult row=PassResult.From(result,started,settings.UseApi,runMode);row.IdleSeconds=settings.IdleSeconds;row.LinkTimeoutSeconds=settings.UseApi?settings.LinkTimeoutSeconds:0;row.ApiLinkConcurrency=settings.UseApi?settings.ApiLinkConcurrency:0;row.ApiDownloadConcurrency=settings.UseApi?settings.ApiDownloadConcurrency:0;row.ApiRequestIntervalMs=settings.UseApi?settings.ApiRequestIntervalMs:0;PassHistory.Append(Path.Combine(dataFolder,"passes.jsonl"),row);passRows.Add(row);if(passRows.Count>PassHistory.MaximumEntries)passRows.RemoveRange(0,passRows.Count-PassHistory.MaximumEntries);RenderPasses();}
                    Render(result);
                    if(runMode==RunMode.IntegrityFailures||!PassHistory.ShouldRepeat(result,repeat,sessionStop.IsCancellationRequested)||(result.Phase=="completed"&&result.Selected==0&&result.ScanErrors==0))break;
                    automaticPass=true;if(result.Phase=="completed")runMode=ScanSessionPolicy.NextMode(initialMode,!ScanSessionPolicy.UseCachedList(settings,true));
                    passTransition.Prepare(result);
                    waitingForRetry=true;
                    Render(result);
                    try{await passTransition.WaitAsync(sessionStop.Token);}catch(OperationCanceledException){break;}finally{waitingForRetry=false;}
                }
            } finally {waitingForRetry=false;settings.UseCachedApiList=originalCache;settings.IdleSeconds=initialIdle;idleCaption.Text=T("Без данных файла, сек","File idle timeout, sec");linkTimeoutCaption.Text=T("Ожидание ссылки, сек","Link timeout, sec");}
        }
        void RenderPasses(){
            if(passes==null)return;passes.Columns.Clear();
            foreach(string title in new string[]{T("Начало","Started"),T("Способ / режим","Method / mode"),T("Итог","Outcome"),T("Ссылка / данные, сек","Link / data, sec"),T("Минут","Minutes"),T("Обработано","Processed"),T("Скачано за проход","Downloaded this pass"),T("Скачано всего","Downloaded total"),T("Осталось","Remaining"),T("Не завершены","Incomplete"),T("Осталось ошибок","Remaining errors")})passes.Columns.Add(title,title);
            passes.Columns[0].FillWeight=160;passes.Columns[1].FillWeight=200;passes.ColumnHeadersHeight=55;
            foreach(PassResult row in passRows.AsEnumerable().Reverse())passes.Rows.Add(row.StartedUtc.ToLocalTime().ToString("dd.MM HH:mm:ss"),row.Method+(row.ApiLinkConcurrency>0?" "+row.ApiLinkConcurrency+"+"+row.ApiDownloadConcurrency:"")+" / "+PassModeName(row.Mode),Phase(row.Outcome),row.IdleSeconds>0?row.IdleSeconds.ToString():"—",(row.Seconds/60).ToString("0.0"),row.Processed,row.Downloaded,row.Available,row.Remaining,row.ZeroDownload,row.Errors);
        }
        string PassModeName(string modeName){switch(modeName){case"Remaining":return T("Всё недокачанное","All remaining");case"PartialFiles":return T("Неполные файлы","Partial files");case"DownloadErrors":return T("Ошибки загрузки","Download errors");case"ErrorsAndNew":return T("Ошибки и новые файлы","Errors and new files");case"IntegrityFailures":return T("Ошибки проверки","Checksum failures");case"Continue":return T("Продолжить","Resume");case"All":return T("Все файлы","All files");case"CloudErrors":return T("Ошибки","Errors");case"ZeroDownload":return T("Нет ответа / сбой загрузки","Timeout / download failures");case"ErrorsAndZero":return T("Ошибки + незавершённые","Errors + incomplete");case"NewFiles":return T("Новые / изменённые","New / changed");case"ProblemFiles":return T("Автоповтор проблемных","Automatic retry");default:return modeName;}}
        void SetBusy(bool value) {
            if(settingsCard!=null)settingsCard.Enabled=!value;
            repeatProblems.Enabled=!value;if(streamsPerFile!=null)streamsPerFile.Enabled=multiStreamThreshold.Enabled=!value&&settings.UseApi;
            folder.ReadOnly=value;apiToken.ReadOnly=remoteFolder.ReadOnly=value;browse.Enabled=mode.Enabled=method.Enabled=idle.Enabled=!value;linkTimeout.Enabled=!value&&settings.UseApi;
            requestInterval.Enabled=linkConcurrency.Enabled=downloadConcurrency.Enabled=!value&&settings.UseApi;
            UpdateScanOptions();method.Visible=!value;methodReadout.Visible=value;methodReadout.Text=Convert.ToString(method.SelectedItem);if(value)methodReadout.BringToFront();mode.Visible=!value;modeReadout.Visible=value;modeReadout.Text=busy&&integrityPass?T("Файлы с несовпавшим хешем","Files with checksum mismatches"):Convert.ToString(mode.SelectedItem);if(value)modeReadout.BringToFront();
            start.Enabled=!renderingFailed;start.Fill=value?Color.FromArgb(217,94,108):accent;start.Text=value?T("Остановить","Stop"):T("Начать проход","Start pass");start.Invalidate();
        }
        string Category(string key) {
            switch(key){case"photos":return T("Фото и изображения","Photos & images");case"videos":return T("Видео","Videos");case"documents":return T("Документы","Documents");case"archives":return T("Архивы и образы","Archives & images");case"audio":return T("Аудио","Audio");default:return T("Другое","Other");}
        }
        string Phase(string key) {
            switch(key){case"api_retry":return T("API не отвечает · повторяем текущий запрос…","API unavailable · retrying the current request…");case"preparing":return T("Готовим историю и список файлов…","Preparing history and file list…");case"checking_local":return T("Сверяем историю и локальные файлы…","Checking history and local files…");case"scanning":return T("Сканируем папки…","Scanning folders…");case"running":return T("Проход выполняется","Pass in progress");case"restarting":return T("Проход выполняется","Pass in progress");case"stopping":return T("Завершаем запросы…","Finishing requests…");case"completed":return T("Проход завершён","Pass completed");case"stopped":return T("Остановлено · история сохранена","Stopped · history saved");case"error":return T("Остановлено с ошибкой","Stopped with an error");default:return T("Готово к запуску","Ready to start");}
        }
        static string Bytes(long value){if(value>=1073741824)return(value/1073741824.0).ToString("0.0")+" GB";if(value>=1048576)return(value/1048576.0).ToString("0.0")+" MB";return(value/1024.0).ToString("0")+" KB";}
        internal void HandleUiFailure(string path){
            renderingFailed=true;start.Enabled=false;timer.Stop();if(sessionStop!=null)sessionStop.Cancel();if(engine!=null)engine.Stop();
            phase.Text=T("Ошибка интерфейса · загрузка останавливается","Interface error · stopping downloads");
            MessageBox.Show(this,T("Произошла ошибка интерфейса. Загрузка останавливается. История и скачанные файлы не удаляются.\nДиагностика сохранена локально:\n","An interface error occurred. Downloads are stopping. History and downloaded files are retained.\nLocal diagnostic file:\n")+path,Text,MessageBoxButtons.OK,MessageBoxIcon.Error);
        }
        void Render(Snapshot s) {
            if(renderingFailed)return;
            try{RenderCore(s);}catch(Exception ex){HandleUiFailure(CrashDiagnostics.Save(ex));}
        }
        void RenderCore(Snapshot s) {
            if(!busy&&s.Phase=="idle"&&s.Total==0&&idleView!=null)s=idleView;
            RenderShell(s);
            if(dashboard!=null&&(section==1||dashboard.Detached)&&s.Catalog!=null&&(s.Phase=="running"||s.Phase=="completed"||s.Phase=="stopped")&&(s.Catalog.Count>0||s.Phase=="completed"))dashboard.Live(s.Catalog,settings.UseApi?settings.ApiFolder:settings.Folder);
            if(waitingForRetry){
                int remaining=passTransition==null?0:(int)Math.Max(0,Math.Ceiling((passTransition.ReadyUtc-DateTime.UtcNow).TotalSeconds));
                phase.Text=remaining>0?String.Format(T("Временная ошибка · следующий проход через {0} с","Temporary error · next pass in {0} s"),remaining):T("Подготовка к следующему проходу…","Preparing the next pass…");
                detail.Text=remaining>0?T("Замедляем повтор после временной ошибки всего прохода. Стоп отменяет ожидание.","Slowing retries after a temporary whole-pass failure. Stop cancels this wait."):T("Переходим к подготовке следующего прохода без фиксированной паузы.","Starting preparation of the next pass without a fixed pause.");
                phase.ForeColor=ink;return;
            }
            metricValue[0].Text=s.Total.ToString("N0");metricValue[1].Text=s.Available.ToString("N0");metricValue[2].Text=s.Downloaded.ToString("N0");metricValue[3].Text=Math.Max(0,s.Total-s.Available).ToString("N0");metricValue[4].Text=s.CloudErrorsThisPass.ToString("N0");
            phase.Text=Phase(s.Phase);phase.ForeColor=s.Phase=="error"?Color.FromArgb(193,75,85):ink;
            if(!String.IsNullOrEmpty(s.Error))detail.Text=s.Error;
            else if(s.Phase=="checking_local"){
                string hash=s.CheckFileLength>0?String.Format(T(" · MD5 {0:0}% · {1:N1}/{2:N1} МБ · {3:0} с"," · MD5 {0:0}% · {1:N1}/{2:N1} MB · {3:0} s"),100.0*s.CheckFileBytes/s.CheckFileLength,s.CheckFileBytes/1048576.0,s.CheckFileLength/1048576.0,Math.Max(0,(DateTime.UtcNow-s.CheckFileStartedUtc).TotalSeconds)):"";
                detail.Text=String.Format(T("Проверено {0:N0}/{1:N0}{2} · {3}","Checked {0:N0}/{1:N0}{2} · {3}"),s.CheckFilesChecked,s.CheckFilesTotal,hash,s.Current);
            }
            else if(s.Phase=="api_retry"||s.Phase=="scanning"||s.Phase=="preparing")detail.Text=s.Current;
            else if(s.Selected>0||s.ProgressTotal>0){string eta=s.PerHour>0?String.Format("{0}:{1:00}",(int)(s.RemainingSeconds/3600),(int)((s.RemainingSeconds%3600)/60)):"—";detail.Text=String.Format(T("Обработано {0}/{1} · {3:N0} файлов/ч · осталось ≈ {4}","Processed {0}/{1} · {3:N0} files/h · remaining ≈ {4}"),s.ProgressTotal>0?s.ProgressChecked:s.Processed,s.ProgressTotal>0?s.ProgressTotal:s.Selected,s.Available,s.PerHour,eta);}
            else if(s.Finished&&s.Selected==0)detail.Text=T("Для этого режима нет файлов. Для поиска новых включите обновление облачного списка и выберите «Скачать всё недокачанное».","No files match this mode. Enable cloud-list refresh and choose Download all remaining files to find new files.");
            else detail.Text=T("Мониторинг начнётся автоматически вместе с проходом.","Monitoring starts automatically with the pass.");
            if(!s.Finished&&s.ApiEffectiveIntervalMs>0){
                double apiWait=Math.Max(0,s.ApiCooldownSeconds-(DateTime.UtcNow-s.ApiStatusCapturedUtc).TotalSeconds);
                if(apiWait>0)detail.Text+=String.Format(T(" · API HTTP {0}: пауза ещё {1:0} с; уже начатые скачивания не приостанавливаются"," · API HTTP {0}: paused for {1:0} more s; already-started downloads are not paused"),s.ApiCooldownCode,Math.Ceiling(apiWait));
                else if(s.ApiEffectiveIntervalMs>settings.ApiRequestIntervalMs)detail.Text+=String.Format(T(" · API временно: интервал {0} мс"," · API temporarily: {0} ms interval"),s.ApiEffectiveIntervalMs);
            }
            available.Text=String.Format(T("Скачано за проход {0:N0} · пропущено {1:N0}","Downloaded this pass {0:N0} · skipped {1:N0}"),s.Downloaded,s.Skipped);
            if(s.Phase=="api_retry"||s.Phase=="scanning"||s.Phase=="preparing")progress.Style=ProgressBarStyle.Marquee;
            else{progress.Style=ProgressBarStyle.Continuous;progress.Maximum=Math.Max(1,s.ProgressTotal>0?s.ProgressTotal:s.Selected);progress.Value=Math.Min(progress.Maximum,s.ProgressTotal>0?s.ProgressChecked:s.Processed);}
            var visibleCategories=s.Categories.Where(c=>c.Total>0).ToList();
            if(categories.Rows.Count!=visibleCategories.Count){categories.Rows.Clear();foreach(CategoryStats c in visibleCategories)categories.Rows.Add(Category(c.Category),c.Total,c.Available,c.Downloaded,c.Remaining,c.ZeroDownload,c.CloudErrors);}else for(int i=0;i<visibleCategories.Count;i++){CategoryStats c=visibleCategories[i];object[] values={Category(c.Category),c.Total,c.Available,c.Downloaded,c.Remaining,c.ZeroDownload,c.CloudErrors};for(int j=0;j<values.Length;j++)if(!Object.Equals(categories.Rows[i].Cells[j].Value,values[j]))categories.Rows[i].Cells[j].Value=values[j];}
            ((StableLog)log).UpdateEvents(s.Events);
            if(!busy)OfferUpdate();
            if(s.RecoveryRequired){start.Enabled=false;detail.Text=T("Отмена не подтверждена. Перезапустите Яндекс Диск и это приложение перед новым проходом.","Cancellation not acknowledged. Restart Yandex Disk and this application before another pass.");}
            if(s.ScanErrors>0)footer.Text=String.Format(T("Недоступных папок/файлов при сканировании: {0}. Подробности в журнале.","Scan access errors: {0}. See the activity log."),s.ScanErrors);
        }
        public void PreviewDashboard(){PreviewDashboard(false);}
        public void PreviewDashboard(bool english){
            PreviewApi(english);SelectSection(1);
            var sample=new System.Collections.Generic.List<BrowserFile>();
            int[] counts={21840,7294,620,412,208,136},ready={19680,5920,588,365,196,117},newly={936,248,32,16,12,4};
            string[] categories={"photos","videos","documents","archives","audio","other"};
            string[] names=english?new[]{"Photos","Videos","Documents","Archives","Music","Other"}:new[]{"Фотографии","Видео","Документы","Архивы","Музыка","Разное"};
            string[] extensions={".jpg",".mp4",".pdf",".zip",".flac",".dat"};
            for(int category=0;category<counts.Length;category++)for(int i=0;i<counts[category];i++){
                string subfolder=category==0?(2021+i%5)+"/"+(english?"Travel":"Путешествия"):category==1?(i%3==0?(english?"Travel":"Путешествия"):i%3==1?(english?"Family":"Семья"):(english?"Projects":"Проекты")):"";
                string name=category==0?"IMG_":category==1?(english?"Trip_":"Поездка_"):category==2?(english?"Document_":"Документ_"):category==3?(english?"Backup_":"Резервная_копия_"):category==4?(english?"Track_":"Композиция_"):"File_";
                string path=names[category]+"/"+(subfolder==""?"":subfolder+"/")+name+(i+1).ToString("D5")+extensions[category];
                bool complete=i*997%counts[category]<ready[category];
                string status=complete?"success":i%7==0?"cloud_error":i%3==0?"downloading":i%3==1?"timeout":"pending";
                long length=category==1?80000000L+(i%31)*17000000L:category==3?1200000000L+(i%11)*81000000L:3000000L+(i%19)*135700L;
                sample.Add(new BrowserFile{Path=path,LocalPath=System.IO.Path.Combine(settings.ApiFolder,path.Replace('/','\\')),Length=length,Bytes=complete?length:status=="downloading"||status=="timeout"?length*(25+i%65)/100:0,Status=status,Verification=complete?"md5_verified":""});
            }
            dashboard.Live(sample,settings.ApiFolder);dashboard.SelectFolder(names[1]+"/"+(english?"Travel":"Путешествия"));
            Snapshot demo=new Snapshot{Phase="running",Total=counts.Sum(),Selected=4892,Processed=1680,Downloaded=newly.Sum(),Available=ready.Sum(),Active=20,CloudErrorsThisPass=12,PerHour=3420,RemainingSeconds=3381,Catalog=sample};
            for(int i=0;i<counts.Length;i++)demo.Categories.Add(new CategoryStats{Category=categories[i],Total=counts[i],Available=ready[i],Downloaded=newly[i],Remaining=counts[i]-ready[i]});
            demo.Events.Add("12:00:01  Trip_00042.mp4  ·  success");demo.Events.Add("12:00:02  IMG_01758.jpg  ·  success");Render(demo);
        }
        public void Preview(){
            folder.Text=@"E:\Yandex.Disk";
            Snapshot s=new Snapshot{Phase="running",Total=30510,Selected=26797,Processed=1204,Downloaded=202,Available=704,Deferred=997,CloudErrors=5,CloudErrorsThisPass=5,Skipped=502,Active=3,PerHour=2677,RemainingSeconds=9.3*3600};
            s.Categories.Add(new CategoryStats{Category="photos",Total=21840,Available=528,Downloaded=168,Deferred=720,CloudErrors=3});s.Categories.Add(new CategoryStats{Category="videos",Total=7294,Available=140,Downloaded=27,Deferred=231,CloudErrors=2});s.Categories.Add(new CategoryStats{Category="documents",Total=620,Available=28,Downloaded=5,Deferred=32});s.Categories.Add(new CategoryStats{Category="archives",Total=412,Available=7,Downloaded=1,Deferred=9});s.Categories.Add(new CategoryStats{Category="audio",Total=208,Available=1,Downloaded=1,Deferred=5});s.Categories.Add(new CategoryStats{Category="other",Total=136});
            foreach(CategoryStats c in s.Categories){c.Remaining=c.Total-c.Available;c.ZeroDownload=c.Deferred;}integrityPass=false;busy=true;SetBusy(true);Render(s);
        }
        public void Preview(bool english){ru=!english;Translate();Preview();}
        public void PreviewApi(bool english){ru=!english;settings.UseApi=true;settings.ApiFolder=@"E:\Downloads\Yandex";apiToken.Text="demo-token";Translate();Preview();folder.Text=settings.ApiFolder;ApplyMethod();}
    }
}
