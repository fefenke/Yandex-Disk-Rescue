using System;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace DiskRescue {
    static class SettingsStoreTests {
        static void Assert(bool value,string message){if(!value)throw new Exception(message);}
        static void Fails(Action action){bool failed=false;try{action();}catch(IOException){failed=true;}catch(UnauthorizedAccessException){failed=true;}Assert(failed,"Write failure was swallowed.");}
        static void NoTemps(string directory){Assert(Directory.GetFiles(directory,"*.tmp").Length==0,"Failed write left a temporary file.");}
        public static void Run(string root){Storage(root);UiFailure(root);UiRecovery(root);Tokens(root);}
        public static void Storage(string root){
            string directory=Path.Combine(root,"settings-store");Directory.CreateDirectory(directory);
            string path=Path.Combine(directory,"settings.json"),backup=path+".bak";
            SettingsLoadResult fresh=SettingsStore.Load(path);
            Assert(fresh.Value!=null&&String.IsNullOrEmpty(fresh.Warning)&&!File.Exists(path),"First load wrote a file or warned unnecessarily.");
            Settings value=new Settings{Language="en",ApiFolder="C:\\Фото",ApiToken="do-not-serialize-this",ApiDownloadConcurrency=17};
            SettingsStore.Save(path,value);string first=File.ReadAllText(path);
            Assert(!first.Contains(value.ApiToken)&&!first.Contains("ApiToken"),"Settings exposed the token.");
            Assert(SettingsStore.Load(path).Value.ApiFolder==value.ApiFolder,"Unicode settings did not roundtrip.");
            value.ApiDownloadConcurrency=19;SettingsStore.Save(path,value);string second=File.ReadAllText(path);
            Assert(File.ReadAllText(backup)==first&&SettingsStore.Load(path).Value.ApiDownloadConcurrency==19,"Save did not retain the previous correct version.");
            using(FileStream locked=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))Fails(delegate{SettingsStore.Save(path,value);});
            Assert(File.ReadAllText(path)==second&&File.ReadAllText(backup)==first,"Locked save damaged settings or backup.");NoTemps(directory);
            File.SetAttributes(path,FileAttributes.ReadOnly);
            try{Fails(delegate{SettingsStore.Save(path,value);});}finally{File.SetAttributes(path,FileAttributes.Normal);}
            Assert(File.ReadAllText(path)==second&&File.ReadAllText(backup)==first,"Read-only save damaged stored data.");NoTemps(directory);
            // Only the final replace fails: backup cannot be replaced while open.
            using(FileStream locked=new FileStream(backup,FileMode.Open,FileAccess.Read,FileShare.Read))Fails(delegate{SettingsStore.Save(path,value);});
            Assert(File.ReadAllText(path)==second&&File.ReadAllText(backup)==first,"Failed commit damaged the original or backup.");NoTemps(directory);
            const string broken="{\"Language\":\"secret-like-fragment";
            File.WriteAllText(path,broken);SettingsLoadResult recovered=SettingsStore.Load(path);
            Assert(recovered.RecoveredFromBackup&&recovered.Value.ApiDownloadConcurrency==17&&!String.IsNullOrEmpty(recovered.Warning),"Torn settings were not recovered with a warning.");
            Assert(File.ReadAllText(path)==broken&&File.ReadAllText(backup)==first&&!recovered.Warning.Contains("secret-like"),"Load modified data or disclosed corrupt contents.");
            SettingsStore.Save(path,value);
            Assert(File.ReadAllText(backup)==first&&Directory.GetFiles(directory,"settings.json.corrupt-*").Any(f=>File.ReadAllText(f)==broken),"Saving after recovery lost the good backup or damaged original.");
            File.WriteAllText(path,"null");File.WriteAllText(backup,"[");
            SettingsLoadResult defaults=SettingsStore.Load(path);
            Assert(defaults.Value!=null&&!defaults.RecoveredFromBackup&&!String.IsNullOrEmpty(defaults.Warning),"Both corrupt files did not yield defaults with a warning.");
            Assert(File.ReadAllText(path)=="null"&&File.ReadAllText(backup)=="[","Default recovery overwrote corrupt files.");
            File.Delete(path);File.WriteAllText(backup,first);
            Assert(SettingsStore.Load(path).RecoveredFromBackup,"Missing primary ignored the backup.");
            File.WriteAllText(path,second);
            using(FileStream locked=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){
                Assert(SettingsStore.Load(path).RecoveredFromBackup,"Unreadable primary ignored the backup.");
                Fails(delegate{SettingsStore.Save(path,value);});
            }
            Assert(File.ReadAllText(path)==second,"Unreadable primary was overwritten.");NoTemps(directory);
        }
        public static void UiRecovery(string root){
            string directory=Path.Combine(root,"settings-ui-recovery");Directory.CreateDirectory(directory);
            string path=Path.Combine(directory,"settings.json"),original="{broken";
            File.WriteAllText(path,original);File.WriteAllText(path+".bak","{\"Language\":\"en\",\"AutoCheckUpdates\":false}");
            using(MainForm form=new MainForm(directory)){
                Label notice=(Label)typeof(MainForm).GetField("settingsNotice",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
                Assert(notice.Text.Contains("backup"),"Recovery was not shown in the window.");
                Assert(File.ReadAllText(path)==original,"Constructing the window overwrote damaged settings.");
            }
        }
        public static void Tokens(string root){
            string directory=Path.Combine(root,"settings-token");Directory.CreateDirectory(directory);
            string path=Path.Combine(directory,"api-token.bin");
            TokenVault.Save(directory,"previous-secret");byte[] previous=File.ReadAllBytes(path);
            using(FileStream locked=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))Fails(delegate{TokenVault.Save(directory,"replacement-secret");});
            Assert(previous.SequenceEqual(File.ReadAllBytes(path))&&TokenVault.Load(directory)=="previous-secret","Failed token save lost the previous token.");NoTemps(directory);
            File.SetAttributes(path,FileAttributes.ReadOnly);
            try{Fails(delegate{TokenVault.Save(directory,"replacement-secret");});}finally{File.SetAttributes(path,FileAttributes.Normal);}
            Assert(previous.SequenceEqual(File.ReadAllBytes(path)),"Read-only token save changed the token.");NoTemps(directory);
            TokenVault.Save(directory,"replacement-secret");Assert(TokenVault.Load(directory)=="replacement-secret","Token replacement failed.");
            Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("replacement-secret"),"Token was stored as plaintext.");
            Assert(Directory.GetFiles(directory).Length==1,"Token save retained another token file.");
            TokenVault.Save(directory,"");Assert(!File.Exists(path)&&TokenVault.Load(directory)=="","Explicit token removal failed.");
        }
        public static void UiFailure(string root){
            string directory=Path.Combine(root,"settings-ui");Directory.CreateDirectory(directory);
            string path=Path.Combine(directory,"settings.json");
            File.WriteAllText(path,"{\"Language\":\"en\",\"AutoCheckUpdates\":false}");
            using(MainForm form=new MainForm(directory)){
                BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
                using(FileStream locked=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){
                    object saved=typeof(MainForm).GetMethod("SaveSettings",flags).Invoke(form,null);
                    Assert(saved is bool&&!(bool)saved,"Settings save failure was not reported to the caller.");
                    Label notice=(Label)typeof(MainForm).GetField("settingsNotice",flags).GetValue(form);
                    Assert(notice.Text.Contains("not saved"),"Settings save failure is invisible to the user.");
                    typeof(MainForm).GetMethod("Translate",flags).Invoke(form,null);
                    Assert(notice.Text.Contains("not saved"),"Translation erased the save failure.");
                }
                Assert((bool)typeof(MainForm).GetMethod("SaveSettings",flags).Invoke(form,null),"Saving did not recover after releasing the file.");
                Assert(((Label)typeof(MainForm).GetField("settingsNotice",flags).GetValue(form)).Text=="","Stale save warning remains after success.");
            }
        }
    }
}
