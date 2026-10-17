using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;

namespace DiskRescue {
    // Rename only our configured former-copy archive into an absent or empty
    // ordinary-name directory. The intent survives every rename/config boundary.
    internal static class ArchiveNameMigration {
        internal sealed class Rename {public string Source,Target;}
        const string Title="Сохранённые прежние файлы";
        static bool Owner(string value){return value!=null&&value.Length==28&&value.StartsWith("api-",StringComparison.Ordinal)&&value.Substring(4).All(Uri.IsHexDigit);}
        static bool Reserved(string path,IEnumerable<string> destinations){string prefix=path.TrimEnd('\\')+"\\";return destinations.Any(d=>String.Equals(d,path,StringComparison.OrdinalIgnoreCase)||d.StartsWith(prefix,StringComparison.OrdinalIgnoreCase));}
        static bool Marked(string path,string owner){DurablePartial.SafePath(path);string marker=Path.Combine(path,".ydr-owner");DurablePartial.SafePath(marker);return Directory.Exists(path)&&File.Exists(marker)&&File.ReadAllText(marker)==owner;}
        static bool EmptyTarget(string path){
            DurablePartial.SafePath(path);if(!Directory.Exists(path))return !File.Exists(path);
            string[] entries=Directory.GetFileSystemEntries(path);if(entries.Length==0)return true;
            string marker=Path.Combine(path,".ydr-owner");DurablePartial.SafePath(marker);
            return entries.Length==1&&String.Equals(entries[0],marker,StringComparison.OrdinalIgnoreCase)&&File.Exists(marker)&&Owner(File.ReadAllText(marker).Trim());
        }
        public static void Run(string state,string root,string owner,IEnumerable<string> destinations,CancellationToken token){
            string intent=Path.Combine(state,"archive-rename.pending"),alias=Path.Combine(state,"archive-rename.json"),config=Path.Combine(state,Title+".root");
            var json=new JavaScriptSerializer();
            try{
                token.ThrowIfCancellationRequested();DurablePartial.SafePath(intent);DurablePartial.SafePath(alias);DurablePartial.SafePath(config);
                if(Directory.GetFiles(state,"intent-*.json").Length!=0)return;
                Rename move;
                if(File.Exists(intent))move=json.Deserialize<Rename>(File.ReadAllText(intent));
                else{
                    if(!File.Exists(config))return;string source=File.ReadAllText(config),target=Path.Combine(root,Title);
                    if(String.Equals(source,target,StringComparison.OrdinalIgnoreCase))return;
                    move=new Rename{Source=source,Target=target};
                }
                if(move==null||!String.Equals(move.Target,Path.Combine(root,Title),StringComparison.OrdinalIgnoreCase)||!String.Equals(Path.GetDirectoryName(move.Source),root,StringComparison.OrdinalIgnoreCase))return;
                string name=Path.GetFileName(move.Source),prefix=Title+" (YDR-";
                if(!name.StartsWith(prefix,StringComparison.Ordinal)||!name.EndsWith(")",StringComparison.Ordinal)||name.Length!=prefix.Length+9||!name.Substring(prefix.Length,8).All(Uri.IsHexDigit))return;
                DurablePartial.SafePath(move.Source);DurablePartial.SafePath(move.Target);
                if(Reserved(move.Source,destinations)||Reserved(move.Target,destinations))return;
                if(Directory.Exists(move.Source)){
                    if(!Marked(move.Source,owner)||!EmptyTarget(move.Target))return;
                    token.ThrowIfCancellationRequested();if(!File.Exists(intent))DurablePartial.AtomicText(intent,json.Serialize(move));
                    if(Directory.Exists(move.Target)){
                        string marker=Path.Combine(move.Target,".ydr-owner");if(File.Exists(marker))File.Delete(marker);
                        Directory.Delete(move.Target,false);
                    }
                    Directory.Move(move.Source,move.Target);
                }else if(!File.Exists(intent)||!Marked(move.Target,owner))return;
                // Do not observe cancellation inside this small commit sequence.
                // Any I/O failure leaves the durable intent for the next start.
                foreach(string path in Directory.GetFiles(state,"api-*.json")){
                    DurablePartial.SafePath(path);var ledger=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));object raw;bool changed=false;
                    if(ledger!=null&&ledger.TryGetValue("Copies",out raw)){
                        var copies=raw as System.Collections.IEnumerable;if(copies!=null)foreach(object item in copies){var copy=item as Dictionary<string,object>;object value;if(copy!=null&&copy.TryGetValue("Path",out value)){string before=Convert.ToString(value),after=Resolve(before,move);if(before!=after){copy["Path"]=after;changed=true;}}}
                    }
                    if(changed)DurablePartial.AtomicText(path,json.Serialize(ledger));
                }
                DurablePartial.AtomicText(alias,json.Serialize(move));DurablePartial.AtomicText(config,move.Target);File.Delete(intent);
            }catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(InvalidOperationException){}
        }
        internal static string Resolve(string path,Rename move){return !String.IsNullOrEmpty(path)&&path.StartsWith(move.Source.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase)?move.Target+path.Substring(move.Source.TrimEnd('\\').Length):path;}
        internal static Rename Load(string state){string path=Path.Combine(state,"archive-rename.json");DurablePartial.SafePath(path);return File.Exists(path)?new JavaScriptSerializer().Deserialize<Rename>(File.ReadAllText(path)):null;}
    }
}
