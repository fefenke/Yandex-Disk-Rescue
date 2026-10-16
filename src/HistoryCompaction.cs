using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace DiskRescue {
    // The journal is authoritative; the index is always regenerated after replacement.
    internal static class HistoryCompaction {
        internal const long MinimumBytes=16L*1024*1024;
        internal static long TryCompact(string journal,long expectedLength,Dictionary<string,HistoryRecord> records,CancellationToken token){
            if(expectedLength<MinimumBytes||token.IsCancellationRequested)return expectedLength;
            string temporary=journal+".compact.tmp";bool ownTemporary=false;
            try{
                DurablePartial.SafePath(journal);DurablePartial.SafePath(temporary);
                var source=new FileInfo(journal);
                if(!source.Exists||source.Length!=expectedLength)return expectedLength;
                long ticks=source.LastWriteTimeUtc.Ticks,compactedLength;
                var json=new JavaScriptSerializer();
                using(var stream=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None)){
                    ownTemporary=true;
                    using(var writer=new StreamWriter(stream,new UTF8Encoding(false),65536,true)){
                        foreach(var record in records.Values){
                            token.ThrowIfCancellationRequested();writer.WriteLine(json.Serialize(record));
                            // Stop early when replacement cannot halve the journal.
                            if(stream.Position>=expectedLength/2)return expectedLength;
                        }
                        writer.Flush();compactedLength=stream.Length;
                    }
                    if(compactedLength>=expectedLength/2)return expectedLength;
                    stream.Flush(true);
                }
                token.ThrowIfCancellationRequested();
                // Deny concurrent journal writers through final validation and replacement.
                using(var guard=new FileStream(journal,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete)){
                    source.Refresh();
                    if(!source.Exists||guard.Length!=expectedLength||source.LastWriteTimeUtc.Ticks!=ticks)return expectedLength;
                    File.Replace(temporary,journal,null);ownTemporary=false;
                }
                return compactedLength;
            }catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(InvalidOperationException){}catch(OperationCanceledException){}
            finally{if(ownTemporary)try{File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}}
            return expectedLength;
        }
    }
}
