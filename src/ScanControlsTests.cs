using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
namespace DiskRescue {
    static class ScanControlsTests {
        static void Assert(bool value,string message){if(!value)throw new Exception(message);}
        static object Field(MainForm form,string name){var f=typeof(MainForm).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic);Assert(f!=null,"Missing scan control: "+name);return f.GetValue(form);}
        public static void Run(string root){
            using(var form=new MainForm(Path.Combine(root,"scan-controls"))){
                var mode=(ComboBox)Field(form,"mode");Assert(mode.Items.Count==5,"Pass selector still exposes overlapping legacy modes.");
                var first=(CheckBox)Field(form,"scanCloudStart");var every=(CheckBox)Field(form,"scanCloudRepeats");
                Assert(!first.Checked&&!every.Checked&&!every.Enabled,"New scan controls do not default to the cached list.");
                first.Checked=true;Assert(every.Enabled,"Nested scan option stays disabled when its parent is checked.");every.Checked=true;
                first.Checked=false;Assert(!every.Enabled,"Nested scan option remains active without its parent.");first.Checked=true;Assert(every.Checked&&every.Enabled,"Toggling the parent erased the nested preference.");
                Assert(Convert.ToString(mode.Items[4]).Contains("хеш"),"Checksum retry is missing from the pass selector.");
                form.PreviewShellSection("settings");IntPtr handle=form.Handle;
                int visible=(int)typeof(Control).GetField("STATE_VISIBLE",BindingFlags.NonPublic|BindingFlags.Static).GetRawConstantValue();typeof(Control).GetMethod("SetState",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(form,new object[]{visible,true});
                typeof(Program).GetMethod("PrepareControls",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{form});
                typeof(MainForm).GetMethod("FitInitialWindow",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null);
                var pages=(Control[])Field(form,"shellPages");var settingsPage=(Panel)pages[4];var settingsContent=settingsPage.Controls[0];
                Assert(form.Height<=Screen.FromControl(form).WorkingArea.Height,"Initial window exceeds the usable screen height.");
                if(form.Height<Screen.FromControl(form).WorkingArea.Height)Assert(settingsContent.Height<=settingsPage.ClientSize.Height&&!settingsPage.VerticalScroll.Visible,"Initial settings require scrolling despite available screen space.");
                first.Checked=false;
                Assert(every.Visible&&!every.Enabled,"Nested checkbox must remain visible and disabled without its parent.");
                var navigation=(Button[])Field(form,"navigation");var brand=navigation[0].Parent.Controls["sidebarBrand"];
                Assert(brand!=null&&brand.Left+brand.Padding.Left==navigation[0].Left+((SidebarButton)navigation[0]).IconLeft&&brand.Font.Size==17,"Brand must retain its size and align with the menu icon column.");
                var browse=(Control)Field(form,"browse");var modeCaption=(Control)Field(form,"modeCaption");
                Assert(modeCaption.Font.Size==first.Font.Size,"Settings captions and checkboxes have inconsistent text sizes.");
                Assert(modeCaption.PointToScreen(System.Drawing.Point.Empty).Y-(browse.PointToScreen(System.Drawing.Point.Empty).Y+browse.Height)>=8,"Folder button crowds the next settings caption.");
                form.Scale(new System.Drawing.SizeF(1.25f,1.25f));
                foreach(var nav in navigation)Assert(nav.Left+((SidebarButton)nav).IconLeft==brand.Left+brand.Padding.Left,"Scaled navigation icons drifted away from the brand.");
                form.Scale(new System.Drawing.SizeF(0.8f,0.8f));
                var repeatBox=(Control)Field(form,"repeatProblems");var updateBox=(Control)Field(form,"autoCheck");
                Assert(repeatBox.PointToScreen(System.Drawing.Point.Empty).X==updateBox.PointToScreen(System.Drawing.Point.Empty).X,"Settings checkboxes have different left edges.");
                var interval=(Control)Field(form,"requestInterval");var links=(Control)Field(form,"linkConcurrency");
                Assert(interval.PointToScreen(System.Drawing.Point.Empty).Y==links.PointToScreen(System.Drawing.Point.Empty).Y,"API interval and link controls have different top edges.");
                var start=(Control)Field(form,"start");var language=(Control)Field(form,"language");
                Assert(start.Height==language.Height&&start.PointToScreen(System.Drawing.Point.Empty).Y==language.PointToScreen(System.Drawing.Point.Empty).Y,"Start and language buttons have different heights or top edges.");
                Assert(((Control)Field(form,"linkTimeout")).Width==((Control)Field(form,"idle")).Width,"Related timeout columns have different widths.");
                Assert(((Control)Field(form,"folder")).Font.Size==((Control)Field(form,"apiToken")).Font.Size,"Folder field uses an inconsistent text size.");
                Assert(((Control)Field(form,"browse")).Height==((Control)Field(form,"checkRelease")).Height,"Settings action buttons have different heights.");
                foreach(string pair in new[]{"mode,idle","folder,browse","linkTimeout,idle","requestInterval,linkConcurrency","linkConcurrency,downloadConcurrency"}){string[] names=pair.Split(',');var a=(Control)Field(form,names[0]);var b=(Control)Field(form,names[1]);Assert(Math.Abs(a.PointToScreen(System.Drawing.Point.Empty).Y+a.Height/2-b.PointToScreen(System.Drawing.Point.Empty).Y-b.Height/2)<=1,"Fields do not share a horizontal center line: "+pair+" ["+a.Bounds+" in "+a.Parent.Bounds+" vs "+b.Bounds+" in "+b.Parent.Bounds+"]");}
            }
            var type=typeof(MainForm).Assembly.GetType("DiskRescue.ScanSessionPolicy");Assert(type!=null,"Missing scan session policy.");
            var cached=type.GetMethod("UseCachedList");foreach(bool start in new[]{false,true})foreach(bool repeat in new[]{false,true}){
                var settings=new Settings();typeof(Settings).GetField("ScanCloudOnStart").SetValue(settings,start);typeof(Settings).GetField("ScanCloudOnRepeats").SetValue(settings,repeat);
                Assert((bool)cached.Invoke(null,new object[]{settings,false})==!start,"First pass scan preference was ignored.");
                Assert((bool)cached.Invoke(null,new object[]{settings,true})==!(start&&repeat),"Repeat scan preference was ignored.");
            }
            var next=type.GetMethod("NextMode");Assert((RunMode)next.Invoke(null,new object[]{RunMode.All,false})==RunMode.Remaining,"Full hashes would run again on every automatic pass.");
            Assert((RunMode)next.Invoke(null,new object[]{RunMode.PartialFiles,true})==RunMode.PartialFiles,"Partial-only mode would start new files.");
            var f=new DiskFile{Path="disk:/new",Modified="v2",Length=4};
            Assert(FileRules.Select(f,null,RunMode.ErrorsAndNew),"Refreshed automatic passes skip new files.");f.Status="integrity_failed";Assert(!FileRules.Select(f,null,RunMode.ErrorsAndNew),"Cloud refresh bypasses checksum-failure protection.");
            f.Status="pending";Assert(!FileRules.Select(f,null,RunMode.DownloadErrors),"Error-only first pass included a never-attempted file.");
            var old=new HistoryRecord{path=f.Path,length=f.Length,modified="old-version",status="failed"};Assert(FileRules.Select(f,old,RunMode.DownloadErrors),"Refreshing cloud metadata hid a previously failed file.");
            Assert((RunMode)next.Invoke(null,new object[]{RunMode.DownloadErrors,true})==RunMode.ErrorsAndNew,"Automatic cloud refresh does not include new files alongside errors.");
        }
    }
}
