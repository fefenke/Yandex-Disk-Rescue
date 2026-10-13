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
        System.Threading.CancellationTokenSource sessionStop;
        bool waitingForRetry;
        CheckBox repeatProblems, increaseIdle;
        DataGridView passes;
        readonly System.Collections.Generic.List<PassResult> passRows=new System.Collections.Generic.List<PassResult>();
        TextBox folder;
        ComboBox mode, method, apiList;
        TextBox apiToken, remoteFolder;
        Label methodCaption, apiCaption, remoteCaption;
        TableLayoutPanel setupGrid, pageGrid, credentials;
        bool translating;
        NumericUpDown idle;
        FlatButton browse, start, update, support, guideButton;
        Button language;
        Label subtitle, guideTitle, guideText, unlimitedWarning, folderCaption, modeCaption, idleCaption, phase, detail, available, footer;
        Label modeReadout, methodReadout;
        LinkLabel tokenHelp;
        Label apiListCaption, apiListReadout;
        Label[] metricTitle = new Label[5], metricValue = new Label[5];
        DataGridView categories;
        TabControl tabs;
        TextBox log;
        ProgressBar progress;
        ToolTip tip = new ToolTip();
        public MainForm(string data) {
            dataFolder=data; settingsPath=Path.Combine(data,"settings.json"); Directory.CreateDirectory(data);
            try { settings = File.Exists(settingsPath) ? new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(settingsPath)) : new Settings(); } catch { settings=new Settings(); }
            settings.UseApi=true;settings.UseCachedApiList=true;
            settings.SupportUrl=new Settings().SupportUrl;
            settings.GitHubReleasesUrl=new Settings().GitHubReleasesUrl;
            ru=settings.Language!="en";
            Text="Yandex Disk Rescue"; Font=new Font("Segoe UI",10); ForeColor=ink; BackColor=Color.FromArgb(244,246,251);
            Size=new Size(1120,1000); MinimumSize=new Size(1000,950); StartPosition=FormStartPosition.CenterScreen;
            AutoScaleMode=AutoScaleMode.Dpi;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Build(); Translate(); engine=new Engine(dataFolder);
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
            TableLayoutPanel brand=Table(1); brand.RowCount=2; brand.RowStyles.Add(new RowStyle(SizeType.Absolute,38)); brand.RowStyles.Add(new RowStyle(SizeType.Percent,100));
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
            RoundPanel setup=Card(); setup.Padding=new Padding(18,10,18,10); TableLayoutPanel config=Table(3);setupGrid=config; config.RowCount=7;
            config.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); config.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,210)); config.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,170));
            config.RowStyles.Add(new RowStyle(SizeType.Absolute,21));config.RowStyles.Add(new RowStyle(SizeType.Absolute,36));config.RowStyles.Add(new RowStyle(SizeType.Absolute,60));config.RowStyles.Add(new RowStyle(SizeType.Absolute,21)); config.RowStyles.Add(new RowStyle(SizeType.Absolute,36)); config.RowStyles.Add(new RowStyle(SizeType.Absolute,22)); config.RowStyles.Add(new RowStyle(SizeType.Absolute,40));
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
            apiListCaption=Label("",9,true);credentials.Controls.Add(apiListCaption,0,2);credentials.SetColumnSpan(apiListCaption,2);Panel apiListBox=new Panel{Dock=DockStyle.Fill,Margin=new Padding(0,2,0,0),BackColor=Color.White};credentials.Controls.Add(apiListBox,0,3);credentials.SetColumnSpan(apiListBox,2);apiList=new ComboBox{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};apiListBox.Controls.Add(apiList);apiListReadout=Label("",10,false);apiListReadout.Padding=new Padding(5,0,5,0);apiListReadout.BackColor=Color.FromArgb(244,246,251);apiListReadout.ForeColor=muted;apiListReadout.Visible=false;apiListBox.Controls.Add(apiListReadout);
            method.SelectedIndexChanged+=delegate{
                if(translating||method.SelectedIndex<0)return;
                if(settings.UseApi)settings.ApiFolder=folder.Text;else settings.Folder=folder.Text;
                settings.UseApi=method.SelectedIndex==1;folder.Text=settings.UseApi?settings.ApiFolder:settings.Folder;
                Translate();SaveSettings();
            };
            folderCaption=Label("",9,true); config.Controls.Add(folderCaption,0,3); config.SetColumnSpan(folderCaption,2);
            folder=new TextBox { Dock=DockStyle.Fill, Text=settings.UseApi?settings.ApiFolder:settings.Folder, BorderStyle=BorderStyle.FixedSingle, Font=new Font("Segoe UI",11), Margin=new Padding(0,4,10,0) }; config.Controls.Add(folder,0,4); config.SetColumnSpan(folder,2);
            browse=new FlatButton { Dock=DockStyle.Fill, Fill=Color.FromArgb(234,237,247), ForeColor=ink, Margin=new Padding(0,0,0,2) }; browse.Click += ChooseFolder; config.Controls.Add(browse,2,4);
            modeCaption=Label("",9,true); config.Controls.Add(modeCaption,0,5);
            idleCaption=Label("",9,true); config.Controls.Add(idleCaption,1,5);
            Panel modeBox=new Panel{Dock=DockStyle.Fill,Margin=new Padding(0,3,10,0),BackColor=Color.White};config.Controls.Add(modeBox,0,6);
            mode=new ComboBox { Dock=DockStyle.Fill, DropDownStyle=ComboBoxStyle.DropDownList, Font=new Font("Segoe UI",10) };modeBox.Controls.Add(mode);
            modeReadout=Label("",10,false);modeReadout.Padding=new Padding(5,0,5,0);modeReadout.BorderStyle=BorderStyle.None;modeReadout.BackColor=Color.FromArgb(244,246,251);modeReadout.ForeColor=muted;modeReadout.Visible=false;modeBox.Controls.Add(modeReadout);
            mode.DrawMode=DrawMode.OwnerDrawFixed;mode.ItemHeight=24;mode.DrawItem+=delegate(object sender,DrawItemEventArgs args){if(args.Index<0)return;args.DrawBackground();TextRenderer.DrawText(args.Graphics,Convert.ToString(mode.Items[args.Index]),mode.Font,args.Bounds,ink,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);args.DrawFocusRectangle();};
            idle=new NumericUpDown { Minimum=1,Maximum=60,Value=Math.Max(1,Math.Min(60,settings.IdleSeconds)),Dock=DockStyle.Fill,Margin=new Padding(0,3,10,0) }; config.Controls.Add(idle,1,6);
            start=new FlatButton { Dock=DockStyle.Fill,Margin=new Padding(0,0,0,1) }; start.Click += StartStop; config.Controls.Add(start,2,6);
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
            repeatProblems=new CheckBox{Checked=true,Dock=DockStyle.Fill,AutoSize=false,Font=new Font("Segoe UI",9)};options.Controls.Add(repeatProblems,0,0);increaseIdle=new CheckBox{Checked=settings.IncreaseIdleTimeout,Dock=DockStyle.Fill,Font=new Font("Segoe UI",9)};options.Controls.Add(increaseIdle,0,1);increaseIdle.Enabled=repeatProblems.Checked;repeatProblems.CheckedChanged+=delegate{increaseIdle.Enabled=!busy&&repeatProblems.Checked;};
            available=Label("",9,false); available.TextAlign=ContentAlignment.MiddleRight; available.ForeColor=muted; options.Controls.Add(available,0,2); status.Controls.Add(options,1,0); page.Controls.Add(status,0,4);
            RoundPanel body=Card(); body.Margin=Padding.Empty; body.Padding=new Padding(12,8,12,8);
            tabs=new TabControl { Dock=DockStyle.Fill,Font=new Font("Segoe UI",10),Padding=new Point(16,6) };
            categories=Grid(); log=new TextBox { Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,BorderStyle=BorderStyle.None,BackColor=Color.White,ForeColor=muted,Font=new Font("Consolas",9) };
            TabPage cat=new TabPage();cat.BackColor=Color.White;cat.Controls.Add(categories);tabs.TabPages.Add(cat);
            TabPage events=new TabPage();events.BackColor=Color.White;events.Controls.Add(log);tabs.TabPages.Add(events);
            passes=Grid();TabPage passTab=new TabPage();passTab.BackColor=Color.White;passTab.Controls.Add(passes);tabs.TabPages.Add(passTab);passRows.AddRange(PassHistory.Load(Path.Combine(dataFolder,"passes.jsonl")));
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
            unlimitedWarning.Text=T("Файлы из безлимитной автозагрузки фото и видео НЕ УЧИТЫВАЮТСЯ И НЕ СКАЧИВАЮТСЯ.\nВ обоих режимах поддерживаются только файлы из обычного раздела «Файлы».","Unlimited photo/video auto-upload files are NOT COUNTED OR DOWNLOADED.\nBoth modes support only files in the regular Files section.");
            translating=true;method.Items.Clear();method.Items.AddRange(new object[]{T("Через клиент Яндекс Диска","Via Yandex Disk client"),T("Через API · OAuth-токен","Via API · OAuth token")});method.SelectedIndex=settings.UseApi?1:0;translating=false;
            methodCaption.Text=T("Способ скачивания · Диск на компьютере или API","Download method · desktop Disk or API");apiCaption.Text=T("OAuth-токен · хранится зашифрованно Windows","OAuth token · encrypted by Windows");remoteCaption.Text=T("Облачная папка · disk:/ — весь Диск","Cloud folder · disk:/ — whole Disk");
            int selectedList=apiList.SelectedIndex<0?(settings.UseCachedApiList?1:0):apiList.SelectedIndex;apiList.Items.Clear();apiList.Items.AddRange(new object[]{T("Обновить список с Диска · найти новые и изменённые файлы","Refresh list from Disk · find new and changed files"),T("Продолжить по сохранённому списку · без повторного сканирования","Continue with saved list · skip rescanning")});apiList.SelectedIndex=selectedList;apiListCaption.Text=T("Список файлов · только для API","File list · API only");
            int selectedMode=mode.SelectedIndex<0?3:mode.SelectedIndex;
            subtitle.Text=T("Скачать доступные файлы. Сохранить результат. Повторить остальные.","Download available files. Keep results. Retry the rest.");
            guideButton.Text=T("Открыть гайд","Open guide");Control[] prompts=Controls.Find("guidePrompt",true);if(prompts.Length>0)prompts[0].Text=T("Перед началом работы ознакомьтесь с гайдом","Before you start, please read the guide");language.Text=ru?"RU  /  EN":"EN  /  RU";
            PrerequisiteStatus environment=Prerequisites.Check();
            guideTitle.Text=environment.ClientRunning?T("Перед запуском · клиент Диска запущен","Before you start · Disk client is running"):environment.ClientInstalled?T("Перед запуском · запустите клиент Диска","Before you start · start the Disk client"):T("Перед запуском · установите клиент Диска","Before you start · install the Disk client");
            guideText.Text=T("1. Выберите способ ниже: клиент Диска на компьютере или API с токеном.\n2. Для клиента: установите Яндекс Диск, войдите в аккаунт и дождитесь окончания синхронизации.\n3. Укажите локальную папку Диска со всеми подпапками и нажмите «Начать проход». При ошибках клиент перезапускается.\nБезлимитная автозагрузка не поддерживается. «Продолжить» пропускает завершённые попытки. Ошибки повторяются отдельными режимами.","1. Choose desktop Disk client or API with an OAuth token below.\n2. For the client: install Yandex Disk, sign in and wait for synchronization to finish.\n3. Select the local Disk folder, including subfolders, then Start pass. The client restarts on sync errors.\nUnlimited auto-upload is unsupported. Continue skips completed attempts. Use retry modes for errors.");
            folderCaption.Text=T("Папка Яндекс Диска · включая все подпапки","Yandex Disk folder · including all subfolders");
            browse.Text=T("Выбрать папку","Browse folder");modeCaption.Text=T("Режим прохода","Pass mode");idleCaption.Text=T("Без прогресса, сек","Idle timeout, sec");
            mode.Items.Clear();mode.Items.AddRange(new object[]{T("Продолжить незавершённый проход","Resume unfinished pass"),T("Повторить ошибки синхронизации","Retry sync errors"),T("Повторить таймауты и сбои","Retry timeouts and failures"),T("Повторить все нескачанные","Retry all files not downloaded"),T("Начать проход с нуля","Start pass from the beginning")});mode.SelectedIndex=selectedMode;
            modeReadout.Text=Convert.ToString(mode.SelectedItem);
            tip.SetToolTip(mode,T("Каждый проход заново сканирует папку. Одинаковые имена в разных папках — разные файлы. «Всё заново» проверяет локальные файлы, но не удаляет их и не скачивает повторно.","Every pass rescans the folder. Same names in different folders remain separate. Recheck all files verifies local files without deleting or downloading them again."));
            start.Text=busy?T("Остановить","Stop"):T("Начать проход","Start pass");update.Text=T("Обновление","Updates");support.Text=T("Поддержать автора","Support the author");support.Enabled=!String.IsNullOrEmpty(settings.SupportUrl);
            metricTitle[0].Text=T("Найдено файлов","Files found");metricTitle[1].Text=T("Скачано всего","Downloaded total");metricTitle[2].Text=T("Скачано за проход","Downloaded this pass");metricTitle[3].Text=T("Осталось скачать","Remaining to download");metricTitle[4].Text=T("Ошибки синхронизации · за проход","Sync errors · this pass");
            increaseIdle.Text=T("Увеличивать ожидание до 5 сек","Increase idle wait up to 5 sec");repeatProblems.Text=T("Повторять проблемные файлы","Repeat problem files");tip.SetToolTip(repeatProblems,T("Повтор ошибок и таймаутов с паузой 30 секунд. Стоп отменяет и проход, и ожидание.","Retry errors and timeouts with a 30-second pause. Stop cancels both the pass and the wait."));tabs.TabPages[2].Text=T("Проходы","Pass history");RenderPasses();
            tabs.TabPages[0].Text=T("Категории","Categories");tabs.TabPages[1].Text=T("Журнал","Activity log");
            categories.Columns.Clear();foreach(string c in new string[]{T("Тип","Type"),T("Всего","Total"),T("Скачаны","Downloaded total"),T("Скачано за текущий проход","Downloaded this pass"),T("Осталось","Remaining"),T("Нет ответа / сбой загрузки","Timeout / download failure"),T("Ошибки синхронизации","Sync errors")})categories.Columns.Add(c,c);
            categories.Columns[0].FillWeight=145;categories.Columns[3].FillWeight=155;categories.Columns[5].FillWeight=110;categories.Columns[6].FillWeight=145;categories.ColumnHeadersDefaultCellStyle.WrapMode=DataGridViewTriState.True;categories.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.EnableResizing;categories.ColumnHeadersHeight=48;
            footer.Text="";ApplyMethod();
        }
                void ApplyMethod(){
            bool api=settings.UseApi;credentials.Visible=api;tokenHelp.Visible=api;methodReadout.Text=Convert.ToString(method.SelectedItem);setupGrid.RowStyles[2].Height=api?112:0;pageGrid.RowStyles[2].Height=api?317:205;
            folderCaption.Text=api?T("Папка для сохранения · структура облачных папок сохраняется","Download folder · preserves cloud folder structure"):T("Папка Яндекс Диска · включая все подпапки","Yandex Disk folder · including all subfolders");
            if(api){mode.Items[0]=T("Продолжить незавершённый проход","Resume unfinished pass");metricTitle[4].Text=T("Ошибки сервера · за проход","Server errors · this pass");categories.Columns[6].HeaderText=T("Ошибки сервера","Server errors");mode.Items[1]=T("Повторить ошибки сервера","Retry server errors");mode.Items[3]=T("Повторить все нескачанные","Retry all files not downloaded");guideTitle.Text=T("Перед запуском · API","Before you start · API");guideText.Text=T("1. Получите токен по ссылке OAuth справа и вставьте его. Укажите локальную папку для сохранения.\n2. Облачный путь disk:/ — весь Диск; disk:/Фото — выбранная папка. Клиент Диска не нужен.\n3. «Продолжить по сохранённому списку» — без нового обхода; первый или незавершённый обход будет выполнен/продолжен.\nДля новых файлов обновите список. Проверяются размер и MD5; безлимит не поддерживается. Токен хранится зашифрованно.","1. Use the OAuth link on the right to obtain a token, then enter it and choose a local download folder.\n2. disk:/ is the whole Disk; disk:/Photos is a specific folder. The desktop client is not needed.\n3. Continue with saved list skips enumeration; a missing or unfinished scan is started or resumed.\nRefresh to find new files. Size and available MD5 are checked; unlimited auto-upload is unsupported. Tokens are encrypted.");}
        }
        void ShowGuide(){
            using(Form window=new Form{Text=T("Как пользоваться Yandex Disk Rescue","Yandex Disk Rescue guide"),Size=new Size(820,760),MinimumSize=new Size(640,500),StartPosition=FormStartPosition.CenterParent,Font=Font,BackColor=BackColor}){
                RichTextBox text=new RichTextBox{Dock=DockStyle.Fill,ReadOnly=true,BorderStyle=BorderStyle.None,BackColor=Color.White,Font=new Font("Segoe UI",11),ScrollBars=RichTextBoxScrollBars.Vertical};
                text.Text=T(
"ВАЖНО: файлы безлимитной автозагрузки НЕ учитываются и НЕ скачиваются. Только обычный раздел «Файлы».\n\nПОДГОТОВКА\nРаспакуйте ZIP целиком в постоянную папку. История и отчёты сохраняются рядом с программой в data. Не удаляйте эту папку при обновлении.\n\nЧЕРЕЗ API\nПолучите OAuth-токен по ссылке в приложении, вставьте его и выберите папку сохранения. Токен хранится зашифрованно. disk:/ означает весь Диск; disk:/Фото — конкретную папку. Клиент Яндекс Диска не нужен. Структура подпапок сохраняется.\n\nЧЕРЕЗ КЛИЕНТ ДИСКА\nУстановите клиент, войдите в аккаунт и дождитесь окончания синхронизации. Выберите локальную папку Диска. При ошибках синхронизации клиент может перезапускаться.\n\nВЕРХНИЙ ВЫБОР СПИСКА API\n«Обновить список» — найти новые и изменённые файлы в облаке.\n«Продолжить по сохранённому списку» — использовать готовый список без повторного обхода. Незавершённый скан продолжается с сохранённой позиции.\n\nРЕЖИМЫ ПРОХОДА\n• Продолжить незавершённый проход — непроверенные файлы и прерванные пользователем попытки; завершённые ошибки пропускаются.\n• Повторить ошибки сервера / синхронизации — файлы с такими записанными ошибками.\n• Повторить таймауты и сбои — нет ответа/прогресса, нулевые и частичные загрузки, другие неуспешные попытки.\n• Повторить все нескачанные — все отсутствующие или не скачанные файлы, включая непроверенные, ошибки и таймауты.\n• Начать проход с нуля — пройти весь список с начала, включая прежние попытки. Уже скачанные корректные файлы не скачиваются повторно.\n\nПОЛЕЗНЫЕ СОЧЕТАНИЯ\nПо умолчанию: сохранённый список + повторить все нескачанные + автоповтор. Если списка ещё нет, он создаётся один раз.\nПосле остановки: сохранённый список + продолжить.\nПовтор проблемных: сохранённый список + повторить все нескачанные.\nФайлы добавились или изменились на Диске: обновить список + повторить все нескачанные.\nНачать заново: сохранённый список + начать проход с нуля. Автоповтор не обновляет список заново на каждом проходе.\n\nАВТОПОВТОР\nС галкой сначала выполняется выбранный режим, затем проблемные файлы повторяются с паузой 30 секунд. Даже если «Продолжить» не нашёл непроверенных файлов, цикл перейдёт к оставшимся проблемным. Временные ошибки соединения и сканирования API повторяются внутри текущего запроса, независимо от проходов. В API последующие проходы используют сохранённый список. Если проблемных файлов нет, цикл завершается. Неверный токен, отсутствие прав, критические локальные ошибки и неподтверждённая отмена требуют исправления. «Стоп» отменяет запросы и ожидание.\n\nПОВТОРЫ СОЕДИНЕНИЯ И СКАНА API\nВременные ошибки сети и HTTP 408/429/5xx повторяются внутри текущего запроса с сохранением папки и страницы. Первая попытка сразу, далее паузы 1, 2 и до 5 секунд; при Retry-After учитывается ожидание сервера. Это действует независимо от автоповтора файлов и поля «Без прогресса». Скан не обрывается из-за временной ошибки. Стоп отменяет ожидание, неверный токен и отсутствие прав требуют исправления.\n\nПОСТЕПЕННОЕ УВЕЛИЧЕНИЕ ОЖИДАНИЯ\nРаботает только с галкой автоповтора. Начальное значение берётся из поля «Без прогресса». После каждых двух завершённых непустых проходов оно увеличивается на 2 секунды до 5: 1 → 1 → 3 → 3 → 5; при старте с 3: 3 → 3 → 5. Значение 5 или больше остаётся неизменным. Ошибки подключения, незавершённый скан и пустые проходы не повышают ожидание. Новый запуск начинает последовательность заново с выбранного значения. Фактическое время отображается над полем и записывается в «Проходы».\n\nБЕЗ ПРОГРЕССА, СЕК\nЭто ожидание без новых данных, а не ограничение времени скачивания всего файла. Для быстрого прохода можно оставить 1 секунду. При повторении таймаутов попробуйте 3–5 секунд: слишком короткое ожидание может отменять медленные, но рабочие загрузки. Большее ожидание замедляет перебор зависших файлов. Чтобы изменить значение во время автоповтора, нажмите «Стоп», задайте новое значение и начните снова. Без галки увеличения ожидание постоянно. Это практические ориентиры, а не гарантия скачивания.\n\nСТАТИСТИКА\n«Осталось» — все ещё не скачанные файлы; число выбранных для прохода зависит от режима. «Скачано за проход» — новые успешные загрузки. Вкладка «Проходы» хранит итоги каждого запуска, «Категории» показывает группы файлов. Подробные отчёты сохраняются автоматически в data. Одинаковые имена в разных папках учитываются отдельно.",
"IMPORTANT: unlimited auto-upload files are NOT counted or downloaded. Only regular Disk files are supported.\n\nSETUP\nExtract the complete ZIP to a permanent folder. Keep data beside the app when updating: it stores history and reports.\n\nAPI\nUse the OAuth link to obtain a token, enter it and choose a download folder. Tokens are encrypted. disk:/ means the whole Disk; disk:/Photos selects a folder. The desktop client is not required. Folder structure is preserved.\n\nDESKTOP CLIENT\nInstall Yandex Disk, sign in and wait for synchronization to finish. Select the local Disk folder. The client may restart on synchronization errors.\n\nUPPER API LIST SELECTOR\nRefresh list: discover new and changed cloud files.\nSaved list: reuse inventory without rescanning; unfinished scans resume from their checkpoint.\n\nPASS MODES\n• Resume unfinished pass: unchecked files and user-interrupted attempts; completed errors are skipped.\n• Retry server/sync errors: retry recorded server or synchronization failures.\n• Retry timeouts and failures: no response/progress, zero-byte and partial transfers, other failures.\n• Retry all unsuccessful downloads: every file not downloaded, including unchecked files, errors and timeouts.\n• Start pass from the beginning: process the entire list again; valid local files are not downloaded again.\n\nCOMBINATIONS\nDefault: saved list + retry all files not downloaded + automatic retry. A missing list is created once.\nAfter Stop: saved list + resume.\nRetry problems: saved list + retry all unsuccessful downloads.\nCloud files were added or changed: refresh list + retry all files not downloaded.\nStart again: saved list + start pass from the beginning. Automatic retries never refresh the inventory on every pass.\n\nAUTOMATIC RETRIES\nThe selected mode runs first, followed by problem-file passes with a 30-second pause. An empty initial Resume still proceeds to existing problem files. Temporary API connection and scan errors retry within the current request, independently of file passes. Later API passes use cached inventory. No remaining problems ends the loop. Invalid tokens, denied access, critical local errors or uncertain cancellation need attention. Stop cancels downloads and waiting.\n\nAPI CONNECTION AND SCAN RETRIES\nTemporary network errors and HTTP 408/429/5xx retry the same request, folder and page. First retry is immediate, then delays of 1, 2 and up to 5 seconds; server Retry-After is respected. Independent of automatic file passes and download idle timeout. Stop cancels waiting; invalid tokens and denied access require attention.\n\nINCREASING DOWNLOAD WAIT\nOnly active with automatic file retries. Every two completed nonempty passes increases initial idle wait by 2 seconds up to 5: starting at 1 gives 1,1,3,3,5; starting at 3 gives 3,3,5. Values of 5 or more are unchanged. Connection errors, incomplete scans and empty passes do not count. Each new session restarts from your initial value. Actual wait appears above the field and in Pass history.\n\nIDLE TIMEOUT, SEC\nThis limits waiting without new data, not the total download duration. Use 1 second for a quick pass. For retrying timeouts, try 3–5 seconds: short waits can cancel slow but working downloads. Longer waits slow down attempts on stalled files. To change this during automatic retries, press Stop, adjust the value and start again. Without the increase checkbox, download waiting stays constant. These are practical starting points, not a download guarantee.\n\nSTATISTICS\nRemaining means all files not yet downloaded; the pass denominator depends on its mode. Downloaded this pass counts new successful downloads. Pass history stores each run's results. Categories groups file types; detailed reports are generated in data. Identical names in different folders remain separate.");
                int warningEnd=text.Text.IndexOf("\n\n",StringComparison.Ordinal);if(warningEnd>0){text.Select(0,warningEnd);text.SelectionColor=Color.FromArgb(145,43,58);text.SelectionFont=new Font("Segoe UI",11,FontStyle.Bold);text.Select(0,0);}window.Padding=new Padding(20);window.Controls.Add(text);window.ShowDialog(this);
            }
        }
        void ChooseFolder(object sender,EventArgs args) {
            using(FolderBrowserDialog d=new FolderBrowserDialog()) {
                d.Description=T("Выберите локальную папку Яндекс Диска","Choose the local Yandex Disk folder");d.SelectedPath=folder.Text;
                if(d.ShowDialog(this)==DialogResult.OK){folder.Text=d.SelectedPath;if(settings.UseApi)settings.ApiFolder=d.SelectedPath;else settings.Folder=d.SelectedPath;SaveSettings();}
            }
        }
        void SaveSettings() { try { File.WriteAllText(settingsPath,new JavaScriptSerializer().Serialize(settings)); } catch { } }
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
            if(settings.UseApi){settings.UseCachedApiList=apiList.SelectedIndex==1;settings.ApiFolder=Path.GetFullPath(folder.Text);settings.ApiToken=apiToken.Text.Trim();try{settings.RemoteFolder=ApiEngine.NormalizeRemote(remoteFolder.Text);TokenVault.Save(dataFolder,settings.ApiToken);}catch(Exception){MessageBox.Show(this,T("Проверьте облачный путь и доступ к хранилищу токена.","Check the cloud path and token storage permissions."),Text);return;}}else settings.Folder=Path.GetFullPath(folder.Text);settings.Concurrency=5;settings.IdleSeconds=(int)idle.Value;settings.RestartClient=true;settings.IncreaseIdleTimeout=increaseIdle.Checked;SaveSettings();
            busy=true;SetBusy(true);phase.Text=T("Подготовка…","Preparing…");progress.Style=ProgressBarStyle.Marquee;timer.Start();
            RunMode runMode=new[]{RunMode.Continue,RunMode.CloudErrors,RunMode.ZeroDownload,RunMode.Remaining,RunMode.All}[mode.SelectedIndex];
            sessionStop=new System.Threading.CancellationTokenSource();work=RunSession(runMode);
            work.ContinueWith(delegate(Task completed){
                if(!IsDisposed)BeginInvoke(new Action(delegate {
                    timer.Stop();waitingForRetry=false;busy=false;SetBusy(false);Render(engine.Latest);sessionStop.Dispose();sessionStop=null;
                    if(completed.IsFaulted)MessageBox.Show(this,completed.Exception.GetBaseException().Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Error);
                    if(closeWhenStopped)Close();
                }));
            });
        }
        async Task RunSession(RunMode runMode) {
            bool repeat=repeatProblems.Checked,increase=increaseIdle.Checked,originalCache=settings.UseCachedApiList;int initialIdle=settings.IdleSeconds,completedPasses=0;
            try {
                while(!sessionStop.IsCancellationRequested){
                    settings.IdleSeconds=PassHistory.IdleForPass(initialIdle,completedPasses,repeat,increase);idleCaption.Text=String.Format(T("Сейчас {0} сек · старт {1}","Now {0} sec · start {1}"),settings.IdleSeconds,initialIdle);engine=new Engine(dataFolder);DateTime started=DateTime.UtcNow;
                    await Task.Run(delegate{if(!sessionStop.IsCancellationRequested)engine.Run(settings,runMode);});
                    Snapshot result=engine.Latest;
                    if(result.Finished){PassResult row=PassResult.From(result,started,settings.UseApi,runMode);row.IdleSeconds=settings.IdleSeconds;PassHistory.Append(Path.Combine(dataFolder,"passes.jsonl"),row);passRows.Add(row);RenderPasses();}
                    Render(result);if(result.Phase=="completed"&&result.Selected>0)completedPasses++;
                    if(!PassHistory.ShouldRepeat(result,repeat,sessionStop.IsCancellationRequested)||(runMode==RunMode.ProblemFiles&&result.Phase=="completed"&&result.Selected==0))break;
                    waitingForRetry=true;
                    try{await Task.Delay(30000,sessionStop.Token);}catch(OperationCanceledException){break;}finally{waitingForRetry=false;}
                    if(result.Phase=="completed")runMode=RunMode.ProblemFiles;if(settings.UseApi)settings.UseCachedApiList=true;
                }
            } finally {waitingForRetry=false;settings.UseCachedApiList=originalCache;settings.IdleSeconds=initialIdle;idleCaption.Text=T("Без прогресса, сек","Idle timeout, sec");}
        }
        void RenderPasses(){
            if(passes==null)return;passes.Columns.Clear();
            foreach(string title in new string[]{T("Начало","Started"),T("Способ / режим","Method / mode"),T("Итог","Outcome"),T("Ожидание, сек","Idle, sec"),T("Минут","Minutes"),T("Обработано","Processed"),T("Скачано за проход","Downloaded this pass"),T("Скачано всего","Downloaded total"),T("Осталось","Remaining"),T("Не завершены","Incomplete"),T("Осталось ошибок","Remaining errors")})passes.Columns.Add(title,title);
            passes.Columns[0].FillWeight=160;passes.Columns[1].FillWeight=200;passes.ColumnHeadersHeight=55;
            foreach(PassResult row in passRows.AsEnumerable().Reverse())passes.Rows.Add(row.StartedUtc.ToLocalTime().ToString("dd.MM HH:mm:ss"),row.Method+" / "+PassModeName(row.Mode),Phase(row.Outcome),row.IdleSeconds>0?row.IdleSeconds.ToString():"—",(row.Seconds/60).ToString("0.0"),row.Processed,row.Downloaded,row.Available,row.Remaining,row.ZeroDownload,row.Errors);
        }
        string PassModeName(string modeName){switch(modeName){case"Continue":return T("Продолжить","Resume");case"All":return T("Все файлы","All files");case"CloudErrors":return T("Ошибки","Errors");case"ZeroDownload":return T("Нет ответа / сбой загрузки","Timeout / download failures");case"ErrorsAndZero":return T("Ошибки + незавершённые","Errors + incomplete");case"NewFiles":return T("Новые / изменённые","New / changed");case"ProblemFiles":return T("Автоповтор проблемных","Automatic retry");default:return modeName;}}
        void SetBusy(bool value) {
            repeatProblems.Enabled=!value;increaseIdle.Enabled=!value&&repeatProblems.Checked;
            folder.ReadOnly=value;apiToken.ReadOnly=remoteFolder.ReadOnly=value;browse.Enabled=mode.Enabled=method.Enabled=apiList.Enabled=idle.Enabled=!value;
            apiList.Visible=!value;apiListReadout.Visible=value;apiListReadout.Text=Convert.ToString(apiList.SelectedItem);if(value)apiListReadout.BringToFront();method.Visible=!value;methodReadout.Visible=value;methodReadout.Text=Convert.ToString(method.SelectedItem);if(value)methodReadout.BringToFront();mode.Visible=!value;modeReadout.Visible=value;modeReadout.Text=Convert.ToString(mode.SelectedItem);if(value)modeReadout.BringToFront();
            start.Enabled=true;start.Fill=value?Color.FromArgb(217,94,108):accent;start.Text=value?T("Остановить","Stop"):T("Начать проход","Start pass");start.Invalidate();
        }
        string Category(string key) {
            switch(key){case"photos":return T("Фото и изображения","Photos & images");case"videos":return T("Видео","Videos");case"documents":return T("Документы","Documents");case"archives":return T("Архивы и образы","Archives & images");case"audio":return T("Аудио","Audio");default:return T("Другое","Other");}
        }
        string Phase(string key) {
            switch(key){case"api_retry":return T("API не отвечает · повторяем текущий запрос…","API unavailable · retrying the current request…");case"preparing":return T("Готовим историю и список файлов…","Preparing history and file list…");case"checking_local":return T("Сверяем историю и локальные файлы…","Checking history and local files…");case"scanning":return T("Сканируем папки…","Scanning folders…");case"running":return T("Проход выполняется","Pass in progress");case"restarting":return T("Проход выполняется","Pass in progress");case"stopping":return T("Завершаем запросы…","Finishing requests…");case"completed":return T("Проход завершён","Pass completed");case"stopped":return T("Остановлено · история сохранена","Stopped · history saved");case"error":return T("Остановлено с ошибкой","Stopped with an error");default:return T("Готово к запуску","Ready to start");}
        }
        static string Bytes(long value){if(value>=1073741824)return(value/1073741824.0).ToString("0.0")+" GB";if(value>=1048576)return(value/1048576.0).ToString("0.0")+" MB";return(value/1024.0).ToString("0")+" KB";}
        void Render(Snapshot s) {
            if(waitingForRetry){phase.Text=T("Пауза перед повтором · 30 секунд","Waiting to retry · 30 seconds");return;}
            metricValue[0].Text=s.Total.ToString("N0");metricValue[1].Text=s.Available.ToString("N0");metricValue[2].Text=s.Downloaded.ToString("N0");metricValue[3].Text=Math.Max(0,s.Total-s.Available).ToString("N0");metricValue[4].Text=s.CloudErrorsThisPass.ToString("N0");
            phase.Text=Phase(s.Phase);phase.ForeColor=s.Phase=="error"?Color.FromArgb(193,75,85):ink;
            if(!String.IsNullOrEmpty(s.Error))detail.Text=s.Error;
            else if(s.Phase=="api_retry"||s.Phase=="scanning"||s.Phase=="preparing"||s.Phase=="checking_local")detail.Text=s.Current;
            else if(s.Selected>0||s.ProgressTotal>0){string eta=s.PerHour>0?String.Format("{0}:{1:00}",(int)(s.RemainingSeconds/3600),(int)((s.RemainingSeconds%3600)/60)):"—";detail.Text=String.Format(T("Обработано {0}/{1} · скачано всего {2:N0} · {3:N0} файлов/ч · осталось ≈ {4}","Processed {0}/{1} · total downloaded {2:N0} · {3:N0} files/h · remaining ≈ {4}"),s.ProgressTotal>0?s.ProgressChecked:s.Processed,s.ProgressTotal>0?s.ProgressTotal:s.Selected,s.Available,s.PerHour,eta);}
            else if(s.Finished&&s.Selected==0)detail.Text=T("Для этого режима нет файлов. Для новых файлов обновите список и выберите «Продолжить незавершённый проход».","No files match this mode. Refresh the list and choose Continue for new files.");
            else detail.Text=T("Мониторинг начнётся автоматически вместе с проходом.","Monitoring starts automatically with the pass.");
            available.Text=String.Format(T("Скачано за проход {0:N0} · пропущено {1:N0}","Downloaded this pass {0:N0} · skipped {1:N0}"),s.Downloaded,s.Skipped);
            if(s.Phase=="api_retry"||s.Phase=="scanning"||s.Phase=="preparing"||s.Phase=="checking_local")progress.Style=ProgressBarStyle.Marquee;
            else{progress.Style=ProgressBarStyle.Continuous;progress.Maximum=Math.Max(1,s.ProgressTotal>0?s.ProgressTotal:s.Selected);progress.Value=Math.Min(progress.Maximum,s.ProgressTotal>0?s.ProgressChecked:s.Processed);}
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
        public void PreviewApi(bool english){ru=!english;settings.UseApi=true;settings.ApiFolder=@"E:\Downloads\Yandex";apiToken.Text="demo-token";Translate();Preview();folder.Text=settings.ApiFolder;ApplyMethod();}
    }
}
