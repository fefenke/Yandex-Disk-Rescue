using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace DiskRescue {
    // Optional derived data. The append-only journal remains authoritative.
    internal static class HistoryIndex {
        const long Limit=128L*1024*1024;
        sealed class Image {
            public int Version{get;set;}
            public string Source{get;set;}
            public long Length{get;set;}public long WriteTicks{get;set;}
            public List<HistoryRecord> Records{get;set;}
        }
        static string IndexPath(string journal){return Path.Combine(Path.GetDirectoryName(journal),"history.latest");}
        static JavaScriptSerializer Serializer(){return new JavaScriptSerializer{MaxJsonLength=(int)Limit};}
        static string Hash(string text){using(SHA256 hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","");}
        public static bool TryLoad(string journal,Dictionary<string,HistoryRecord> records,CancellationToken token){
            token.ThrowIfCancellationRequested();
            try{
                string path=IndexPath(journal);DurablePartial.SafePath(path);var source=new FileInfo(journal);var index=new FileInfo(path);
                if(!source.Exists||!index.Exists||index.Length>Limit)return false;
                long length=source.Length,ticks=source.LastWriteTimeUtc.Ticks;
                string text=File.ReadAllText(path,Encoding.UTF8);token.ThrowIfCancellationRequested();int end=text.IndexOf('\n');if(end!=70||!text.StartsWith("YDRH1 ",StringComparison.Ordinal))return false;
                string body=text.Substring(end+1);if(text.Substring(6,64)!=Hash(body))return false;token.ThrowIfCancellationRequested();
                Image image=Serializer().Deserialize<Image>(body);token.ThrowIfCancellationRequested();
                if(image==null||image.Version!=1||image.Source!=Path.GetFullPath(journal)||image.Length!=length||image.WriteTicks!=ticks||image.Records==null)return false;
                var restored=new Dictionary<string,HistoryRecord>(records.Comparer);
                foreach(HistoryRecord record in image.Records){token.ThrowIfCancellationRequested();if(record==null||String.IsNullOrEmpty(record.path)||record.length<0||restored.ContainsKey(record.path))return false;restored.Add(record.path,record);}
                source.Refresh();if(!source.Exists||source.Length!=length||source.LastWriteTimeUtc.Ticks!=ticks)return false;
                foreach(var pair in restored)records.Add(pair.Key,pair.Value);return true;
            }catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}catch(FormatException){return false;}catch(OverflowException){return false;}
        }
        public static void Save(string journal,long expectedLength,Dictionary<string,HistoryRecord> records){
            try{
                DurablePartial.SafePath(journal);var source=new FileInfo(journal);if(!source.Exists||source.Length!=expectedLength)return;
                long ticks=source.LastWriteTimeUtc.Ticks;
                string body=Serializer().Serialize(new Image{Version=1,Source=Path.GetFullPath(journal),Length=expectedLength,WriteTicks=ticks,Records=records.Values.ToList()});
                source.Refresh();if(source.Length!=expectedLength||source.LastWriteTimeUtc.Ticks!=ticks)return;
                DurablePartial.AtomicText(IndexPath(journal),"YDRH1 "+Hash(body)+"\n"+body);
            }catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(InvalidOperationException){}
        }
    }
}
