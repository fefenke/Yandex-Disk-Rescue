using System;
using System.Drawing;
using System.Windows.Forms;
namespace DiskRescue {
    internal static class ScanSessionPolicy {
        public static bool UseCachedList(Settings settings,bool automaticPass){return !(settings.ScanCloudOnStart&&(!automaticPass||settings.ScanCloudOnRepeats));}
        public static RunMode NextMode(RunMode initial,bool refreshing){return initial==RunMode.All?RunMode.Remaining:initial;}
        public static bool ShouldRepeat(RunMode mode,Snapshot result,bool repeat,bool stopped){return mode!=RunMode.IntegrityFailures&&PassHistory.ShouldRepeat(result,repeat,stopped)&&!(result.Phase=="completed"&&result.Selected==0&&result.ScanErrors==0);}
    }
    public sealed partial class MainForm {
        CheckBox scanCloudStart,scanCloudRepeats;
        bool integrityPass;
        protected override void OnLoad(EventArgs e){base.OnLoad(e);FitInitialWindow();}
        void FitInitialWindow(){
            if(shellPages==null)return;
            var preferences=(Panel)shellPages[4];var content=preferences.Controls[0];
            PerformLayout();preferences.PerformLayout();content.PerformLayout();
            Rectangle area=Screen.FromControl(this).WorkingArea;
            int required=Height+Math.Max(0,content.Height-preferences.ClientSize.Height)+12;
            MinimumSize=new Size(Math.Min(MinimumSize.Width,area.Width),Math.Min(MinimumSize.Height,area.Height));
            Size=new Size(Math.Min(Width,area.Width),Math.Min(required,area.Height));
            Location=new Point(area.Left+(area.Width-Width)/2,area.Top+(area.Height-Height)/2);
            PerformLayout();preferences.PerformLayout();
        }
        void BuildScanOptions(){
            scanCloudStart=new CheckBox{Dock=DockStyle.Fill,Margin=Padding.Empty,Checked=settings.ScanCloudOnStart,TextAlign=ContentAlignment.MiddleLeft};
            scanCloudRepeats=new CheckBox{Dock=DockStyle.Fill,Margin=new Padding(24,0,0,0),Checked=settings.ScanCloudOnRepeats,TextAlign=ContentAlignment.MiddleLeft};
            credentials.Controls.Add(scanCloudStart,0,2);credentials.SetColumnSpan(scanCloudStart,2);credentials.Controls.Add(scanCloudRepeats,0,3);credentials.SetColumnSpan(scanCloudRepeats,2);
            scanCloudStart.CheckedChanged+=delegate{UpdateScanOptions();SaveScanOptions();};scanCloudRepeats.CheckedChanged+=delegate{SaveScanOptions();};UpdateScanOptions();
        }
        void SaveScanOptions(){if(busy||translating)return;settings.ScanCloudOnStart=scanCloudStart.Checked;settings.ScanCloudOnRepeats=scanCloudRepeats.Checked;SaveSettings();}
        void UpdateScanOptions(){scanCloudStart.Enabled=!busy;scanCloudRepeats.Enabled=!busy&&scanCloudStart.Checked;scanCloudRepeats.Visible=true;}
        void TranslateScanOptions(){
            scanCloudStart.Text=T("Обновлять список файлов из облака при запуске","Refresh cloud file list when starting");
            scanCloudRepeats.Text=T("Также обновлять перед каждым автоматическим проходом","Also refresh before every automatic pass");
            tip.SetToolTip(scanCloudStart,T("Без галочки используется сохранённый список. Если его нет, облако сканируется автоматически. Каждое нажатие «Начать» после остановки — новый запуск.","Unchecked uses the saved list. A missing list is scanned automatically. Starting after Stop begins a new session."));
            tip.SetToolTip(scanCloudRepeats,T("Доступно при включённой первой галочке. Ищет новые и изменённые файлы перед каждым автоматическим повтором. Увеличивает время подготовки и число запросов API.","Requires the parent option. Finds new and changed files before every automatic retry, adding preparation time and API requests."));
        }
        void CenterSettingsFields(){
            SetSettingsTypography(setupGrid);repeatProblems.Font=new Font("Segoe UI",10);
            foreach(int row in new[]{0,3,5,7})setupGrid.RowStyles[row].Height=26;
            setupGrid.RowStyles[4].Height=56;
            credentials.RowStyles[0].Height=26;credentials.RowStyles[2].Height=26;
            folder.Font=new Font("Segoe UI",10);mode.ItemHeight=mode.Font.Height;
            setupGrid.ColumnStyles[1].Width=190;setupGrid.ColumnStyles[2].Width=190;
            foreach(Control field in new Control[]{apiToken,remoteFolder,folder,idle,linkTimeout,requestInterval,linkConcurrency,downloadConcurrency}){
                field.Dock=DockStyle.None;field.Anchor=AnchorStyles.Left|AnchorStyles.Right;field.Margin=new Padding(field.Margin.Left,0,field.Margin.Right,0);
            }
            foreach(ComboBox field in new[]{method,mode}){
                var panel=field.Parent;panel.Margin=new Padding(panel.Margin.Left,0,panel.Margin.Right,0);field.Dock=DockStyle.None;field.Anchor=AnchorStyles.Left|AnchorStyles.Right;
                Action layout=delegate{field.SetBounds(0,0,panel.ClientSize.Width,field.PreferredHeight);field.Top=Math.Max(0,(panel.ClientSize.Height-field.Height)/2);};panel.SizeChanged+=delegate{layout();};field.FontChanged+=delegate{layout();};layout();
            }
            browse.Margin=new Padding(0,0,10,0);browse.Dock=DockStyle.None;browse.Anchor=AnchorStyles.Left|AnchorStyles.Right;browse.Height=36;
            var intervalPanel=requestInterval.Parent as TableLayoutPanel;
            intervalPanel.RowCount=1;intervalPanel.RowStyles.Clear();intervalPanel.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            language.Dock=DockStyle.Top;language.Height=start.Height;language.Margin=new Padding(4,start.Margin.Top,0,start.Margin.Bottom);
        }
        void SetSettingsTypography(Control parent){foreach(Control child in parent.Controls){var label=child as Label;if(label!=null&&label.Font.Bold){label.Font=new Font("Segoe UI",10,FontStyle.Bold);label.Margin=Padding.Empty;}SetSettingsTypography(child);}}
        void CenterFlowRow(FlowLayoutPanel panel){
            bool arranging=false;panel.Layout+=delegate{if(arranging)return;arranging=true;try{foreach(Control control in panel.Controls){int top=Math.Max(0,(panel.ClientSize.Height-panel.Padding.Vertical-control.Height)/2);Padding margin=control.Margin;if(margin.Top!=top||margin.Bottom!=0)control.Margin=new Padding(margin.Left,top,margin.Right,0);}}finally{arranging=false;}};
        }
    }
}
