using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace DiskRescue {
    internal sealed class SourceTabs:UserControl {
        readonly RadioButton account,link;bool updating;int selected;
        public event EventHandler SelectedIndexChanged;
        public SourceTabs(){
            DoubleBuffered=true;AccessibleRole=AccessibleRole.PageTabList;BackColor=Color.FromArgb(239,242,250);Padding=new Padding(3);
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty,Padding=Padding.Empty};
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));Controls.Add(layout);
            account=Tab("Мой Яндекс Диск","account");link=Tab("Публичная ссылка","link");layout.Controls.Add(account,0,0);layout.Controls.Add(link,1,0);
            account.CheckedChanged+=delegate{if(account.Checked&&!updating)SelectedIndex=0;};link.CheckedChanged+=delegate{if(link.Checked&&!updating)SelectedIndex=1;};Apply();
        }
        RadioButton Tab(string text,string glyph){return new SourceTabButton{Text=text,Glyph=glyph,Appearance=Appearance.Button,Dock=DockStyle.Fill,Margin=Padding.Empty,Font=new Font("Segoe UI",10,FontStyle.Bold)};}
        void Apply(){updating=true;try{account.Checked=selected==0;link.Checked=selected==1;account.Invalidate();link.Invalidate();}finally{updating=false;}}
        public int SelectedIndex{get{return selected;}set{if(value<0||value>1)throw new ArgumentOutOfRangeException("value");if(selected==value)return;selected=value;Apply();if(SelectedIndexChanged!=null)SelectedIndexChanged(this,EventArgs.Empty);}}
        public void SetLabels(string first,string second){if(account.Text!=first)account.Text=first;if(link.Text!=second)link.Text=second;}
        protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);if(Width<24||Height<24)return;using(var path=RoundPanel.Rounded(new Rectangle(0,0,Width,Height),11)){var previous=Region;Region=new Region(path);if(previous!=null)previous.Dispose();}}
    }
    public sealed partial class MainForm {
        Label sourceTitle,sourceHint;Button sourceHelpButton;ToolStripDropDown sourceHelp;SourceCard sourceCard;Control sourceAuthFields;SourcePublicWarning publicWarning;
        readonly object settingsWriteGate=new object();
        readonly System.Windows.Forms.Timer sourceSaveTimer=new System.Windows.Forms.Timer{Interval=300};
        long sourceSaveVersion;
        void BuildSourceHeader(TableLayoutPanel config){
            var header=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};
            header.Paint+=delegate(object sender,PaintEventArgs e){SourceArtwork.Draw(e.Graphics,"cloud",new Rectangle(0,5,24,24),Color.FromArgb(48,85,247));};
            sourceTitle=Label("",12,true);sourceTitle.Dock=DockStyle.None;sourceTitle.AutoSize=true;sourceTitle.Location=new Point(34,5);header.Controls.Add(sourceTitle);
            sourceHelpButton=new SourceInfoButton{Text="?",Size=new Size(26,26),TabStop=true,Font=new Font("Segoe UI",10,FontStyle.Bold)};
            header.Controls.Add(sourceHelpButton);sourceHelpButton.Click+=delegate{ShowSourceHelp();};
            header.Layout+=delegate{sourceHelpButton.Location=new Point(sourceTitle.Right+7,3);};config.Controls.Add(header,0,0);config.SetColumnSpan(header,3);
            sourceSaveTimer.Tick+=async delegate{sourceSaveTimer.Stop();await SaveSourceSettingsAsync();};
            Disposed+=delegate{Interlocked.Increment(ref sourceSaveVersion);sourceSaveTimer.Dispose();if(sourceHelp!=null)sourceHelp.Dispose();};
        }
        void BuildSourceCard(TableLayoutPanel config){
            var header=sourceTitle.Parent;config.Controls.Remove(header);config.Controls.Remove(sourceMode);config.Controls.Remove(credentials);
            sourceCard=new SourceCard{Dock=DockStyle.Fill,Padding=new Padding(18,12,18,12),Margin=new Padding(0,0,10,14)};
            var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty,Padding=Padding.Empty,BackColor=Color.White};
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));body.RowStyles.Add(new RowStyle(SizeType.Absolute,36));body.RowStyles.Add(new RowStyle(SizeType.Absolute,48));body.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            sourceMode.Margin=new Padding(0,4,0,8);credentials.Margin=new Padding(0,4,0,0);
            body.Controls.Add(header,0,0);body.Controls.Add(sourceMode,0,1);body.Controls.Add(credentials,0,2);sourceCard.Controls.Add(body);config.Controls.Add(sourceCard,0,0);config.SetColumnSpan(sourceCard,3);config.SetRowSpan(sourceCard,3);
        }
        void RequestSourceSave(){if(!settingsReady)return;Interlocked.Increment(ref sourceSaveVersion);sourceSaveTimer.Stop();sourceSaveTimer.Start();}
        void BuildAuthArea(Control caption,Control input){
            credentials.Controls.Remove(caption);credentials.Controls.Remove(input);
            var area=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};var fields=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));fields.RowStyles.Add(new RowStyle(SizeType.Absolute,26));fields.RowStyles.Add(new RowStyle(SizeType.Percent,100));fields.Controls.Add(caption,0,0);fields.Controls.Add(input,0,1);sourceAuthFields=fields;
            publicWarning=new SourcePublicWarning{Dock=DockStyle.Fill,Margin=new Padding(0,2,0,3)};area.Controls.Add(fields);area.Controls.Add(publicWarning);credentials.Controls.Add(area,0,3);credentials.SetColumnSpan(area,2);credentials.SetRowSpan(area,2);
        }
        async Task SaveSourceSettingsAsync(){
            long version=Interlocked.Read(ref sourceSaveVersion);
            // Capture on the UI thread; ScriptIgnore excludes the OAuth token.
            var json=new JavaScriptSerializer();Settings snapshot=json.Deserialize<Settings>(json.Serialize(settings));Exception failure=null;
            await Task.Run(delegate{lock(settingsWriteGate){if(version!=Interlocked.Read(ref sourceSaveVersion))return;try{SettingsStore.Save(settingsPath,snapshot);}catch(Exception ex){failure=ex;}}});
            if(IsDisposed||version!=Interlocked.Read(ref sourceSaveVersion))return;
            settingsSaveWarning=failure==null?null:failure is UnauthorizedAccessException?"permission":failure is System.IO.IOException?"io":"other";
            if(failure==null)settingsLoadWarning=null;RefreshSettingsNotice();
        }
        string SourceAccountHelp(){return T("Скачивание ваших файлов через API. Укажите OAuth-токен и путь в формате disk:/.","Download your files through the API. Enter an OAuth token and a path such as disk:/.");}
        string SourcePublicHelp(){return T("Скачивание по открытой ссылке Яндекс Диска. Поддерживаются папки и отдельные файлы. OAuth-токен не требуется.","Download through a public Yandex Disk link. Folders and individual files are supported. No OAuth token is required.");}
        string SourceHelpText(){return T("Мой Яндекс Диск","My Yandex Disk")+"\n"+SourceAccountHelp()+"\n\n"+T("Публичная ссылка","Public link")+"\n"+SourcePublicHelp();}
        void ShowSourceHelp(){
            if(sourceHelp!=null&&sourceHelp.Visible){sourceHelp.Close();return;}if(sourceHelp!=null)sourceHelp.Dispose();
            int width=Math.Min(480,Screen.FromControl(this).WorkingArea.Width-40);
            var panel=new SourceCard{Size=new Size(width,310),Padding=Padding.Empty};
            var title=new Label{Text=T("Откуда можно скачивать файлы?","Where can files be downloaded from?"),Location=new Point(22,20),Size=new Size(width-72,28),Font=new Font("Segoe UI",12,FontStyle.Bold),BackColor=Color.White};
            var close=new SourceInfoButton{CloseIcon=true,AccessibleName=T("Закрыть справку","Close help"),Size=new Size(28,28),Location=new Point(width-40,14)};
            close.Click+=delegate{sourceHelp.Close();};panel.Controls.Add(title);panel.Controls.Add(close);
            int top=68;string[] headings={T("Мой Яндекс Диск","My Yandex Disk"),T("Публичная ссылка","Public link")};string[] descriptions={SourceAccountHelp(),SourcePublicHelp()};
            for(int i=0;i<2;i++){
                var icon=new SourceHelpIcon{Glyph=i==0?"account":"link",Location=new Point(22,top)};panel.Controls.Add(icon);
                var heading=new Label{Text=headings[i],Location=new Point(74,top),Size=new Size(width-98,24),Font=new Font("Segoe UI",10.5f,FontStyle.Bold),BackColor=Color.White};
                var body=new Label{Text=descriptions[i],Location=new Point(74,top+28),Size=new Size(width-98,60),Font=new Font("Segoe UI",10),BackColor=Color.White};
                body.Height=TextRenderer.MeasureText(body.Text,body.Font,new Size(body.Width,Int32.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.TextBoxControl).Height+6;
                panel.Controls.Add(heading);panel.Controls.Add(body);top=body.Bottom+24;
            }
            panel.Height=top;sourceHelp=new SourceHelpPopup();sourceHelp.Items.Add(new ToolStripControlHost(panel){AutoSize=false,Size=panel.Size,Margin=Padding.Empty,Padding=Padding.Empty});
            sourceHelp.Show(sourceHelpButton,new Point(sourceHelpButton.Width+8,-18));
        }
        void UpdateSourceHint(){
            if(sourceHint==null||changingSource)return;
            bool isPublic=settings.Source==DownloadSource.PublicLink,empty=String.IsNullOrWhiteSpace(remoteFolder.Text),valid=ApiEngine.IsPublicUrl(remoteFolder.Text);
            sourceHint.ForeColor=isPublic&&!empty?(valid?Color.FromArgb(24,120,50):Color.FromArgb(160,65,50)):muted;
            sourceHint.Text=!isPublic?T("disk:/ — весь Диск; например: disk:/Фото, disk:/Архив.","disk:/ — whole Disk; for example: disk:/Photos, disk:/Archive."):empty?T("Вставьте открытую HTTPS-ссылку Яндекс Диска.","Paste a public HTTPS Yandex Disk link."):valid?T("Ссылка распознана · OAuth-токен не требуется","Link recognized · no OAuth token required"):T("Нужна публичная HTTPS-ссылка Яндекс Диска вида /d/… или /i/… .","Use a public HTTPS Yandex Disk link with /d/… or /i/… .");
        }
    }
}
