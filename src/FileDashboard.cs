using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace DiskRescue {
    public sealed class BrowserFile {
        public string Path,LocalPath,Status,Verification,PreservedPath;public long Length,Bytes;
        public bool Ready {get{return FileRules.Success(Status);}}
    }
    public static class BrowserCatalog {
        public static List<BrowserFile> Capture(IEnumerable<DiskFile> files,string root){
            return files.Select(f=>new BrowserFile{Path=f.Path.StartsWith("disk:/",StringComparison.Ordinal)?f.Path.Substring(6):f.Path.Substring(Math.Min(f.Path.Length,root.TrimEnd('\\').Length)).TrimStart('\\').Replace('\\','/'),LocalPath=String.IsNullOrEmpty(f.Destination)?f.Path:f.Destination,Length=f.Length,Bytes=f.DownloadedBytes??0,Status=f.Status,Verification=f.Verification,PreservedPath=f.PreservedPath}).ToList();
        }
        public static List<BrowserFile> Load(string data,bool api,string root,string remote){
            List<DiskFile> files=new List<DiskFile>();string historyPath;
            JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=int.MaxValue};
            if(api){
                string cache=Path.Combine(data,"api-inventory");if(!Directory.Exists(cache))return new List<BrowserFile>();
                string account=null;
                foreach(string path in Directory.GetFiles(cache,"inventory.json",SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc)){
                    ApiEngine.InventoryCache saved=json.Deserialize<ApiEngine.InventoryCache>(File.ReadAllText(path));
                    if(saved==null||saved.Root!=ApiEngine.NormalizeRemote(remote)||saved.Files==null)continue;
                    files=saved.Files;account=saved.Account;DestinationPaths.Assign(files,root,DestinationPaths.Saved(saved,root));break;
                }
                if(account==null)return new List<BrowserFile>();

                historyPath=Path.Combine(data,HistoryStore.IdentityKey(account+"|"+ApiEngine.NormalizeRemote(remote)+"|"+Path.GetFullPath(root).ToUpperInvariant()),"history.jsonl");
            }else historyPath=Path.Combine(data,HistoryStore.RootKey(root),"history.jsonl");
            Dictionary<string,HistoryRecord> records=new Dictionary<string,HistoryRecord>(api?StringComparer.Ordinal:StringComparer.OrdinalIgnoreCase);
            if(!HistoryIndex.TryLoad(historyPath,records,System.Threading.CancellationToken.None)&&File.Exists(historyPath))using(StreamReader reader=new StreamReader(new FileStream(historyPath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))){string line;while((line=reader.ReadLine())!=null){try{HistoryRecord r=json.Deserialize<HistoryRecord>(line);if(r!=null&&r.path!=null)records[r.path]=r;}catch(ArgumentException){} }}
            if(!api)files=records.Values.Select(r=>new DiskFile{Path=r.path,Destination=r.path,Length=r.length,Modified=r.modified}).ToList();
            foreach(DiskFile f in files){if(!String.IsNullOrEmpty(f.DestinationError))continue;HistoryRecord r;if(records.TryGetValue(f.Path,out r)&&FileRules.SameVersion(f,r)){f.Status=FileRules.NormalizeStatus(r);f.DownloadedBytes=r.downloadedBytes;f.Verification=r.verification;f.PreservedPath=r.preservedPath;}if(FileRules.Success(f.Status)&&!File.Exists(f.Destination))f.Status="pending";string part=f.Destination+".ydr-part";if(RangeMap.Exists(f.Destination))f.DownloadedBytes=RangeMap.Progress(f.Destination);else if(File.Exists(part))try{f.DownloadedBytes=new FileInfo(part).Length;}catch(IOException){} }
            return Capture(files,root);
        }
    }
    public sealed class FileDashboard:UserControl {
        [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="SendMessageW")]static extern IntPtr Cue(IntPtr window,int message,IntPtr param,string text);
        sealed class Folder {
            public string Path;public int Total,Ready;public TreeNode Node;
            public List<BrowserFile> Files=new List<BrowserFile>();public List<Folder> Children=new List<Folder>();
        }
        readonly Font counterFont=new Font("Segoe UI",8);
        readonly ImageList icons=new ImageList();readonly Dictionary<string,Image> glyphs=new Dictionary<string,Image>();
        readonly TreeView tree=new BufferedTree();readonly DataGridView grid=new BufferedGrid();
        readonly TextBox search=new TextBox();readonly CheckBox remaining=new CheckBox();
        readonly Button expand=new Button();Form detached;Control home;
        readonly Label breadcrumb=new Label();readonly Label summary=new Label();readonly Button open=new Button(),reveal=new Button();
        Dictionary<string,Folder> folders=new Dictionary<string,Folder>(StringComparer.Ordinal);
        List<BrowserFile> last;string selected="",root="",loadedContext="";bool ru=true,loading;int generation;
        public bool Detached {get{return detached!=null;}}
        public event Action<int,int> CountsChanged;public event Action<List<BrowserFile>> CatalogChanged;
        protected override void Dispose(bool disposing){base.Dispose(disposing);if(disposing){foreach(Image image in glyphs.Values)image.Dispose();icons.Dispose();counterFont.Dispose();}}
        public FileDashboard(){
            Dock=DockStyle.Fill;BackColor=Color.White;Font=new Font("Segoe UI",9);
            icons.ImageSize=new Size(16,16);icons.ColorDepth=ColorDepth.Depth32Bit;foreach(string kind in new[]{"folder","photo","video","document"})foreach(bool ready in new[]{false,true})icons.Images.Add(kind+(ready?"-ready":""),BrowserIcons.Make(kind,ready));foreach(string key in icons.Images.Keys)glyphs[key]=icons.Images[key];tree.ImageList=icons;
            TableLayoutPanel layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(6)};layout.RowStyles.Add(new RowStyle(SizeType.Absolute,28));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,34));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,28));Controls.Add(layout);breadcrumb.Dock=DockStyle.Fill;breadcrumb.Font=new Font("Segoe UI",10,FontStyle.Bold);breadcrumb.ForeColor=Color.FromArgb(35,49,75);breadcrumb.AutoEllipsis=true;breadcrumb.TextAlign=ContentAlignment.MiddleLeft;layout.Controls.Add(breadcrumb,0,0);
            TableLayoutPanel tools=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3};tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,210));search.Dock=DockStyle.Fill;search.Margin=new Padding(0,4,12,4);remaining.Dock=DockStyle.Fill;tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,0));tools.Controls.Add(search);tools.Controls.Add(remaining,1,0);expand.Visible=false;expand.Dock=DockStyle.Fill;tools.Controls.Add(expand,2,0);expand.Click+=delegate{if(detached!=null){detached.Close();return;}home=Parent;home.Controls.Remove(this);detached=new Form{Text=T("Файлы и папки","Files and folders"),Size=new Size(1100,750),MinimumSize=new Size(800,500),StartPosition=FormStartPosition.CenterParent};detached.Controls.Add(this);expand.Text=T("Свернуть","Restore");detached.FormClosing+=delegate{detached.Controls.Remove(this);if(home!=null&&!home.IsDisposed)home.Controls.Add(this);detached=null;expand.Text=T("Развернуть","Expand");};detached.Show(home.FindForm());};layout.Controls.Add(tools,0,1);
            SplitContainer split=new SplitContainer{Dock=DockStyle.Fill,SplitterWidth=6,BackColor=Color.FromArgb(241,244,251)};split.Size=new Size(900,400);split.SplitterDistance=350;split.Panel1MinSize=150;split.Panel2MinSize=200;layout.Controls.Add(split,0,2);
            tree.Dock=DockStyle.Fill;tree.BorderStyle=BorderStyle.None;tree.HideSelection=false;tree.FullRowSelect=true;tree.ItemHeight=36;tree.DrawMode=TreeViewDrawMode.OwnerDrawAll;tree.Scrollable=true;tree.BackColor=Color.White;split.Panel1.Controls.Add(tree);
            tree.ShowNodeToolTips=true;tree.ShowLines=false;tree.Indent=16;tree.DrawNode+=delegate(object sender,DrawTreeNodeEventArgs e){
                Folder f=e.Node.Tag as Folder;if(f==null)return;bool chosen=(e.State&TreeNodeStates.Selected)!=0;
                int x=e.Node.Bounds.X,width=Math.Max(20,Math.Max(tree.ClientSize.Width-x-14,TextRenderer.MeasureText(FolderName(f),Font).Width));Rectangle row=new Rectangle(0,e.Bounds.Y,tree.ClientSize.Width,tree.ItemHeight);
                using(SolidBrush bg=new SolidBrush(chosen?Color.FromArgb(232,235,255):Color.White))e.Graphics.FillRectangle(bg,row);
                e.Graphics.DrawImage(glyphs["folder"],x-20,e.Bounds.Y+(tree.ItemHeight-16)/2,16,16);
                if(e.Node.Nodes.Count>0){Rectangle box=new Rectangle(x-35,e.Bounds.Y+tree.ItemHeight/2-5,10,10);using(Pen border=new Pen(Color.FromArgb(131,144,163))){e.Graphics.DrawRectangle(border,box);e.Graphics.DrawLine(border,box.X+2,box.Y+5,box.Right-2,box.Y+5);if(!e.Node.IsExpanded)e.Graphics.DrawLine(border,box.X+5,box.Y+2,box.X+5,box.Bottom-2);}}
                TextRenderer.DrawText(e.Graphics,FolderName(f),Font,new Rectangle(x,e.Bounds.Y+1,width,16),Color.FromArgb(30,44,70),TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix|TextFormatFlags.PreserveGraphicsClipping);
                TextRenderer.DrawText(e.Graphics,FolderProgress(f),counterFont,new Rectangle(x,e.Bounds.Y+17,width,13),Color.FromArgb(75,87,109),TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
                using(SolidBrush track=new SolidBrush(Color.FromArgb(236,239,246)))e.Graphics.FillRectangle(track,x,e.Bounds.Y+32,width,2);
                if(f.Total>0)using(SolidBrush fill=new SolidBrush(Color.FromArgb(16,155,131)))e.Graphics.FillRectangle(fill,x,e.Bounds.Y+32,(int)(width*(double)f.Ready/f.Total),2);
            };
            grid.Dock=DockStyle.Fill;grid.ReadOnly=true;grid.AllowUserToAddRows=false;grid.AllowUserToDeleteRows=false;grid.RowHeadersVisible=false;grid.BackgroundColor=Color.White;grid.BorderStyle=BorderStyle.None;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;grid.MultiSelect=false;grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;grid.RowTemplate.Height=30;grid.ShowCellToolTips=true;grid.GridColor=Color.FromArgb(237,240,246);grid.CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal;grid.ColumnHeadersHeight=30;grid.ColumnHeadersDefaultCellStyle.Font=new Font("Segoe UI",9,FontStyle.Bold);grid.DefaultCellStyle.Font=new Font("Segoe UI",9);grid.EnableHeadersVisualStyles=false;grid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(244,246,251);grid.DefaultCellStyle.SelectionBackColor=Color.FromArgb(232,235,255);grid.DefaultCellStyle.SelectionForeColor=Color.FromArgb(30,44,70);grid.Columns.Add("name","");grid.Columns[0].FillWeight=150;grid.Columns.Add("state","");grid.Columns[1].FillWeight=150;grid.Columns.Add("size","");DataGridViewImageColumn iconColumn=new DataGridViewImageColumn{Name="icon",HeaderText="",Width=28,AutoSizeMode=DataGridViewAutoSizeColumnMode.None,ImageLayout=DataGridViewImageCellLayout.Normal};grid.Columns.Insert(0,iconColumn);grid.Columns[1].FillWeight=220;grid.Columns[1].DefaultCellStyle.WrapMode=DataGridViewTriState.True;grid.Columns[2].FillWeight=135;grid.Columns[2].DefaultCellStyle.WrapMode=DataGridViewTriState.True;grid.Columns[3].FillWeight=70;grid.ScrollBars=ScrollBars.Both;grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;grid.Columns[1].MinimumWidth=190;grid.Columns[2].MinimumWidth=150;grid.Columns[3].MinimumWidth=80;
            grid.CellPainting+=delegate(object sender,DataGridViewCellPaintingEventArgs e){if(e.RowIndex<0||e.ColumnIndex<1)return;e.PaintBackground(e.CellBounds,true);e.Paint(e.CellBounds,DataGridViewPaintParts.Border);System.Drawing.Drawing2D.GraphicsState saved=e.Graphics.Save();try{Rectangle clip=Rectangle.Intersect(e.CellBounds,e.ClipBounds);e.Graphics.SetClip(clip);Rectangle text=new Rectangle(e.CellBounds.X+5,e.CellBounds.Y+2,Math.Max(1,e.CellBounds.Width-10),Math.Max(1,e.CellBounds.Height-4));TextRenderer.DrawText(e.Graphics,Convert.ToString(e.FormattedValue),e.CellStyle.Font??grid.Font,text,e.CellStyle.ForeColor,TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix|TextFormatFlags.PreserveGraphicsClipping|TextFormatFlags.VerticalCenter);}finally{e.Graphics.Restore(saved);}e.Handled=true;};split.Panel2.Controls.Add(grid);
            FlowLayoutPanel bottom=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};open.AutoSize=true;reveal.AutoSize=true;summary.AutoSize=true;summary.Padding=new Padding(8,6,0,0);bottom.Controls.Add(summary);layout.Controls.Add(bottom,0,3);
            tree.AfterSelect+=delegate{Folder f=tree.SelectedNode==null?null:tree.SelectedNode.Tag as Folder;if(f!=null){selected=f.Path;ShowFiles();}};
            search.HandleCreated+=delegate{Cue(search.Handle,0x1501,new IntPtr(1),T("Поиск в выбранной папке…","Search this folder…"));};search.TextChanged+=delegate{ShowFiles();};remaining.CheckedChanged+=delegate{ShowFiles();};grid.SelectionChanged+=delegate{Actions();};
            open.Click+=delegate{OpenSelected();};reveal.Click+=delegate{RevealSelected();};grid.CellDoubleClick+=delegate(object sender,DataGridViewCellEventArgs e){if(e.RowIndex>=0){grid.CurrentCell=grid.Rows[e.RowIndex].Cells[1];OpenSelected();}};
            ContextMenuStrip menu=new ContextMenuStrip{ForeColor=Color.FromArgb(29,40,59),Renderer=new DarkMenuRenderer()};menu.Opening+=delegate{menu.Items.Clear();BrowserFile f=Current();Folder folder=CurrentFolder();ToolStripItem item=menu.Items.Add(T("Открыть файл","Open file"),null,delegate{OpenSelected();});item.Enabled=f!=null&&f.Ready&&File.Exists(f.LocalPath);item.Visible=item.Enabled;menu.Items.Add(folder!=null?T("Открыть в Проводнике","Open in Explorer"):T("Показать в Проводнике","Show in Explorer"),null,delegate{RevealSelected();}).Enabled=f!=null||folder!=null;};grid.ContextMenuStrip=menu;
            grid.CellMouseDown+=delegate(object sender,DataGridViewCellMouseEventArgs e){if(e.Button==MouseButtons.Right&&e.RowIndex>=0)grid.CurrentCell=grid.Rows[e.RowIndex].Cells[0];};
            ContextMenuStrip folderMenu=new ContextMenuStrip{ForeColor=Color.FromArgb(29,40,59),Renderer=new DarkMenuRenderer()};folderMenu.Items.Add("",null,delegate{if(tree.SelectedNode!=null)OpenFolder(((Folder)tree.SelectedNode.Tag).Path);});folderMenu.Opening+=delegate{folderMenu.Items[0].Text=T("Открыть в Проводнике","Open in Explorer");folderMenu.Items[0].Enabled=tree.SelectedNode!=null&&Directory.Exists(FolderPath(((Folder)tree.SelectedNode.Tag).Path));};tree.ContextMenuStrip=folderMenu;tree.NodeMouseClick+=delegate(object sender,TreeNodeMouseClickEventArgs e){if(e.Button==MouseButtons.Right)tree.SelectedNode=e.Node;};
            Translate(true);
        }
        string T(string a,string b){return ru?a:b;}
        public void Translate(bool russian){ru=russian;if(search.IsHandleCreated)Cue(search.Handle,0x1501,new IntPtr(1),T("Поиск в выбранной папке…","Search this folder…"));expand.Text=detached==null?T("Развернуть","Expand"):T("Свернуть","Restore");remaining.Text=T("Только нескачанные","Only remaining");open.Text=T("Открыть файл","Open file");reveal.Text=T("Показать в Проводнике","Show in Explorer");grid.Columns[1].HeaderText=T("Имя","Name");grid.Columns[2].HeaderText=T("Состояние","Status");grid.Columns[3].HeaderText=T("Размер","Size");if(last!=null){List<BrowserFile> saved=last;last=null;UpdateCatalog(saved,root);}}
        public async void LoadSaved(string data,bool api,string destination,string remote){
            string context=data+"|"+api+"|"+destination+"|"+remote;
            if(loading||String.IsNullOrWhiteSpace(destination)||(last!=null&&context==loadedContext))return;loading=true;int ticket=++generation;if(context!=loadedContext)UpdateCatalog(new List<BrowserFile>(),destination);summary.Text=T("Читаем сохранённую историю…","Reading saved history…");
            try{List<BrowserFile> saved=await Task.Run(()=>BrowserCatalog.Load(data,api,destination,remote));if(!IsDisposed&&ticket==generation){loadedContext=context;UpdateCatalog(saved,destination);}}
            catch(Exception ex){if(!IsDisposed)summary.Text=T("Не удалось прочитать историю: ","Could not read history: ")+ex.GetType().Name;}
            finally{loading=false;}
        }
        public void Live(List<BrowserFile> files,string destination){if(files==null)return;generation++;UpdateCatalog(files,destination);}
        Folder Ensure(string path){Folder f;if(folders.TryGetValue(path,out f))return f;f=new Folder{Path=path};folders[path]=f;if(path!=""){int index=path.LastIndexOf('/');Folder parent=Ensure(index<0?"":path.Substring(0,index));parent.Children.Add(f);}return f;}
        public void UpdateCatalog(List<BrowserFile> files,string destination){
            if(ReferenceEquals(last,files)&&root==destination)return;last=files;root=destination;HashSet<string> expanded=new HashSet<string>(folders.Values.Where(f=>f.Node!=null&&f.Node.IsExpanded).Select(f=>f.Path));
            Dictionary<string,Folder> old=folders;folders=new Dictionary<string,Folder>(StringComparer.Ordinal);Ensure("");
            foreach(BrowserFile file in files){int index=file.Path.LastIndexOf('/');Folder parent=Ensure(index<0?"":file.Path.Substring(0,index));parent.Files.Add(file);for(Folder f=parent;;){f.Total++;if(file.Ready)f.Ready++;if(f.Path=="")break;index=f.Path.LastIndexOf('/');f=folders[index<0?"":f.Path.Substring(0,index)];}}
            try{
                foreach(Folder removed in old.Values.Where(f=>!folders.ContainsKey(f.Path)))if(removed.Node!=null)removed.Node.Remove();
                foreach(Folder f in folders.Values.OrderBy(f=>f.Path.Length)){
                    Folder before;if(old.TryGetValue(f.Path,out before)&&before.Node!=null){f.Node=before.Node;f.Node.Tag=f;if(before.Ready!=f.Ready||before.Total!=f.Total){f.Node.ToolTipText=FolderName(f)+Environment.NewLine+FolderProgress(f);if(f.Node.IsVisible)tree.Invalidate(new Rectangle(0,f.Node.Bounds.Top,tree.ClientSize.Width,tree.ItemHeight));}}
                    else{TreeNodeCollection nodes=f.Path==""?tree.Nodes:folders[f.Path.LastIndexOf('/')<0?"":f.Path.Substring(0,f.Path.LastIndexOf('/'))].Node.Nodes;f.Node=new TreeNode(FolderName(f)){Tag=f,ImageKey="folder",SelectedImageKey="folder",ToolTipText=FolderName(f)+Environment.NewLine+FolderProgress(f)};int index=0;while(index<nodes.Count&&StringComparer.CurrentCultureIgnoreCase.Compare(nodes[index].Text,f.Node.Text)<0)index++;nodes.Insert(index,f.Node);if(expanded.Contains(f.Path)||f.Path=="")f.Node.Expand();}
                }
                Folder chosen;if(!folders.TryGetValue(selected,out chosen))chosen=folders[""];selected=chosen.Path;if(tree.SelectedNode!=chosen.Node)tree.SelectedNode=chosen.Node;
            }finally{}ShowFiles();if(CountsChanged!=null)CountsChanged(files.Count,files.Count(f=>f.Ready));if(CatalogChanged!=null)CatalogChanged(files);
        }
        internal Action<string> FileOpener=path=>Process.Start(new ProcessStartInfo(path){UseShellExecute=true});
        internal void SelectFolder(string path){Folder f;if(folders.TryGetValue(path,out f)){selected=path;tree.SelectedNode=f.Node;ShowFiles();}}
        string FolderName(Folder f){return f.Path==""?T("Весь список","All listed files"):f.Path.Substring(f.Path.LastIndexOf('/')+1);}
        string FolderProgress(Folder f){return f.Ready.ToString("N0")+" / "+f.Total.ToString("N0")+" · "+(f.Total==0?0:100.0*f.Ready/f.Total).ToString("0")+"%";}
        string FolderText(Folder f){return (f.Path==""?T("Весь список","All listed files"):f.Path.Substring(f.Path.LastIndexOf('/')+1))+"   "+f.Ready.ToString("N0")+" / "+f.Total.ToString("N0")+"   "+(f.Total==0?0:100.0*f.Ready/f.Total).ToString("0")+"%";}
        void BuildNode(Folder f,TreeNodeCollection nodes){f.Node=nodes.Add(FolderText(f));f.Node.ImageKey=f.Node.SelectedImageKey="folder";f.Node.Tag=f;foreach(Folder child in f.Children.OrderBy(c=>c.Path,StringComparer.CurrentCultureIgnoreCase))BuildNode(child,f.Node.Nodes);}
        static string FormatSize(long bytes){return bytes>=1073741824?(bytes/1073741824.0).ToString("0.00")+" GB":bytes>=1048576?(bytes/1048576.0).ToString("0.0")+" MB":(bytes/1024.0).ToString("0.0")+" KB";}
        string State(BrowserFile f){if(f.Ready)return f.Verification=="size_only"?(f.Status=="local"?T("✓  Совпал размер · нет облачного хеша","✓  Size matched · no cloud hash"):T("✓  Скачан · без облачного хеша","✓  Downloaded · no cloud hash")):f.Verification=="history"?T("✓  Скачан · проверен ранее","✓  Downloaded · previously checked"):T("✓  Скачан","✓  Downloaded");if(f.Status=="integrity_failed")return !String.IsNullOrEmpty(f.PreservedPath)&&File.Exists(f.PreservedPath)?T("Хеш не совпал · копия сохранена","Checksum mismatch · copy preserved"):T("Хеш не совпал · копия отсутствует","Checksum mismatch · copy missing");string state=f.Status=="downloading"?T("Скачивается","Downloading"):f.Status=="cloud_error"?T("Ошибка сервера / синхронизации","Server / sync error"):f.Status=="timeout"?T("Таймаут","Timeout"):f.Status=="failed"?T("Сбой загрузки","Download failed"):f.Status=="cancelled_user"?T("Остановлен","Stopped"):T("Не скачан","Not downloaded");return f.Bytes>0?String.Format("{0} / {1} · {2:0}% · {3}",FormatSize(f.Bytes),FormatSize(f.Length),f.Length>0?100.0*f.Bytes/f.Length:0,state):state;}
        string CompactState(BrowserFile file){string value=State(file);if(file.Bytes>0&&!file.Ready){int split=value.IndexOf(" · ",StringComparison.Ordinal);if(split>=0)value=value.Substring(split+3);}return value.Replace(T("Ошибка сервера / синхронизации","Server / sync error"),T("Ошибка сервера / Диска","Server / Disk error"));}
        object CurrentTag(){return grid.CurrentRow==null?null:grid.CurrentRow.Tag;}BrowserFile Current(){return CurrentTag() as BrowserFile;}Folder CurrentFolder(){return CurrentTag() as Folder;}
        void ShowFiles(){
            Folder folder;if(!folders.TryGetValue(selected,out folder))return;breadcrumb.Text=T("Диск","Disk")+(selected.Length>0?"   ›   "+selected.Replace("/","   ›   "):"");string query=search.Text.Trim();List<object> rows=new List<object>();rows.AddRange(folder.Children.Where(f=>(!remaining.Checked||f.Ready<f.Total)&&f.Path.Substring(f.Path.LastIndexOf('/')+1).IndexOf(query,StringComparison.CurrentCultureIgnoreCase)>=0).OrderBy(f=>f.Path).Cast<object>());rows.AddRange(folder.Files.Where(f=>(!remaining.Checked||!f.Ready)&&Path.GetFileName(f.Path).IndexOf(query,StringComparison.CurrentCultureIgnoreCase)>=0).OrderBy(f=>f.Path).Cast<object>());
            string key=CurrentKey();int top=grid.FirstDisplayedScrollingRowIndex;bool same=grid.Rows.Count==rows.Count&&rows.Select(Key).SequenceEqual(grid.Rows.Cast<DataGridViewRow>().Select(r=>Key(r.Tag)));
            if(!same){grid.Rows.Clear();foreach(object item in rows)grid.Rows.Add(glyphs["document"],"","","");}
            for(int i=0;i<rows.Count;i++){BrowserFile file=rows[i] as BrowserFile;Folder child=rows[i] as Folder;DataGridViewRow row=grid.Rows[i];row.Tag=rows[i];row.Cells[1].ToolTipText=file==null?FolderName(child):file.Path.Substring(file.Path.LastIndexOf('/')+1);row.Cells[2].ToolTipText=file==null?FolderProgress(child):State(file);object[] values=file==null?new object[]{child.Path.Substring(child.Path.LastIndexOf('/')+1),FolderProgress(child),""}:new object[]{file.Path.Substring(file.Path.LastIndexOf('/')+1),CompactState(file),FormatSize(file.Length)};for(int j=0;j<3;j++)if(!Object.Equals(row.Cells[j+1].Value,values[j]))row.Cells[j+1].Value=values[j];Image icon=glyphs[file==null?"folder":BrowserIcons.Kind(file.Path)+(file.Ready?"-ready":"")];if(!ReferenceEquals(row.Cells[0].Value,icon))row.Cells[0].Value=icon;row.DefaultCellStyle.ForeColor=file==null||file.Ready?Color.FromArgb(29,40,59):Color.FromArgb(104,114,132);row.Cells[2].Style.ForeColor=file!=null&&file.Ready?Color.FromArgb(16,142,106):file!=null&&file.Status=="cloud_error"?Color.FromArgb(193,74,77):Color.FromArgb(75,112,177);if(!same&&Key(rows[i])==key)grid.CurrentCell=row.Cells[0];}
            if(!same&&top>=0&&top<grid.Rows.Count)grid.FirstDisplayedScrollingRowIndex=top;
            summary.Text=String.Format(T("{0:N0} / {1:N0} скачано · {2:0}%","{0:N0} / {1:N0} downloaded · {2:0}%"),folder.Ready,folder.Total,folder.Total==0?0:100.0*folder.Ready/folder.Total);Actions();
        }
        static string Key(object item){BrowserFile f=item as BrowserFile;Folder d=item as Folder;return f!=null?"f:"+f.Path:d!=null?"d:"+d.Path:"";}string CurrentKey(){return Key(CurrentTag());}
        string FolderPath(string path){return Path.GetFullPath(Path.Combine(root,path.Replace('/','\\')));}
        void Actions(){reveal.Text=CurrentFolder()!=null?T("Открыть в Проводнике","Open in Explorer"):T("Показать в Проводнике","Show in Explorer");BrowserFile file=Current();open.Enabled=file!=null&&file.Ready;open.Visible=file!=null;reveal.Enabled=CurrentTag()!=null;}
        void Guard(Action action){try{action();}catch(Exception ex){MessageBox.Show(this,T("Не удалось открыть: ","Could not open: ")+ex.GetType().Name);}}
        void OpenFolder(string path){Guard(delegate{string local=FolderPath(path);if(Directory.Exists(local))Process.Start(new ProcessStartInfo(local){UseShellExecute=true});else MessageBox.Show(this,T("Папка ещё не создана на компьютере.","The folder has not been created locally yet."));});}
        void OpenSelected(){Folder folder=CurrentFolder();if(folder!=null){SelectFolder(folder.Path);folder.Node.EnsureVisible();return;}BrowserFile file=Current();if(file==null||!file.Ready)return;Guard(delegate{if(File.Exists(file.LocalPath))FileOpener(file.LocalPath);else MessageBox.Show(this,T("Файл отсутствует на компьютере.","The local file is missing."));});}
        void RevealSelected(){Folder folder=CurrentFolder();if(folder!=null){OpenFolder(folder.Path);return;}BrowserFile file=Current();if(file==null)return;Guard(delegate{string path=File.Exists(file.LocalPath)?file.LocalPath:file.LocalPath+".ydr-part";if(File.Exists(path))Process.Start("explorer.exe","/select,\""+path+"\"");else MessageBox.Show(this,T("Локальный файл ещё не создан.","No local file exists yet."));});}
    }
}
