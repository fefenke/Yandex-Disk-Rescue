using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace DiskRescue {
    internal sealed class SettingsLoadResult {
        public Settings Value;
        public bool RecoveredFromBackup;
        public string Warning;
    }

    internal static class SettingsStore {
        static readonly object gate=new object();
        static Settings Parse(string text){
            try{return new JavaScriptSerializer().Deserialize<Settings>(text);}
            catch(ArgumentException){return null;}
            catch(InvalidOperationException){return null;}
            catch(FormatException){return null;}
            catch(OverflowException){return null;}
        }
        static Settings Read(string path,out bool missing){
            missing=false;
            try{return Parse(File.ReadAllText(path));}
            catch(FileNotFoundException){missing=true;return null;}
            catch(DirectoryNotFoundException){missing=true;return null;}
            catch(IOException){return null;}
            catch(UnauthorizedAccessException){return null;}
        }
        public static SettingsLoadResult Load(string path){
            lock(gate){
                bool primaryMissing,backupMissing;
                Settings value=Read(path,out primaryMissing);
                if(value!=null)return new SettingsLoadResult{Value=value};
                value=Read(path+".bak",out backupMissing);
                if(value!=null)return new SettingsLoadResult{Value=value,RecoveredFromBackup=true,Warning="Settings were recovered from the backup."};
                return new SettingsLoadResult{Value=new Settings(),Warning=primaryMissing&&backupMissing?null:"Settings could not be read. Defaults are in use; original files have been retained."};
            }
        }
        public static void Save(string path,Settings value){
            if(value==null)throw new ArgumentNullException("value");
            lock(gate){
                path=Path.GetFullPath(path);DurablePartial.SafePath(path);
                string backup=null;
                if(File.Exists(path)){
                    // I/O errors must escape: an unreadable primary must not be
                    // mistaken for corrupt JSON and replaced with defaults.
                    bool valid=Parse(File.ReadAllText(path))!=null;
                    backup=valid?path+".bak":path+".corrupt-"+Guid.NewGuid().ToString("N");
                }
                AtomicFile.Write(path,new UTF8Encoding(false).GetBytes(new JavaScriptSerializer().Serialize(value)),backup);
            }
        }
    }

    // A temporary file belongs only to this write and stays on the same volume.
    // No delete-and-move fallback: failure must leave the existing file intact.
    internal static class AtomicFile {
        internal static void Write(string path,byte[] bytes,string backup){
            path=Path.GetFullPath(path);DurablePartial.SafePath(path);
            if(backup!=null)DurablePartial.SafePath(backup);
            if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReadOnly)!=0)throw new UnauthorizedAccessException("Destination is read-only.");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            bool created=false;
            try{
                using(FileStream stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
                    created=true;stream.Write(bytes,0,bytes.Length);stream.Flush(true);
                }
                DurablePartial.SafePath(path);
                if(File.Exists(path))File.Replace(temporary,path,backup);else File.Move(temporary,path);
            }finally{
                if(created)try{File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}
            }
        }
    }
}
