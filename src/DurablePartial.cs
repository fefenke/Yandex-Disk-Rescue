using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace DiskRescue {
    // The data flush MUST precede the journal flush. Recovery trusts hashes,
    // never the allocated file length or the last UI/history progress value.
    internal sealed class DurablePartial:Stream {
        internal const long CheckpointBytes=32L*1024*1024;
        readonly FileStream output;
        readonly string state,header;
        readonly SHA256 hash=SHA256.Create();
        readonly Stopwatch elapsed=Stopwatch.StartNew();
        long committed,pending;bool disposed;
        sealed class Record {public long Start,End;public string Hash;}
        static string Key(string identity){using(SHA256 sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-","");}
        static string Header(string identity){return "YDR1 "+Key(identity);}
        internal static void SafePath(string path){
            for(string current=path;!String.IsNullOrEmpty(current);current=Path.GetDirectoryName(current))if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new IOException("Checkpoint destination contains a link or cloud placeholder.");
        }
        internal static void AtomicText(string path,string text){
            SafePath(path);SafePath(path+".tmp");byte[] bytes=new UTF8Encoding(false).GetBytes(text);
            using(FileStream stream=new FileStream(path+".tmp",FileMode.Create,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
            if(File.Exists(path))File.Replace(path+".tmp",path,null);else File.Move(path+".tmp",path);
        }
        static void Append(string state,string text){
            SafePath(state);byte[] bytes=Encoding.UTF8.GetBytes(text+"\n");
            using(FileStream journal=new FileStream(state,FileMode.Append,FileAccess.Write,FileShare.Read)){journal.Write(bytes,0,bytes.Length);journal.Flush(true);}
        }
        static string HashRange(FileStream data,long count,CancellationToken token){
            using(SHA256 sha=SHA256.Create()){
                byte[] buffer=new byte[65536];while(count>0){token.ThrowIfCancellationRequested();int n=data.Read(buffer,0,(int)Math.Min(count,buffer.Length));if(n==0)throw new EndOfStreamException();sha.TransformBlock(buffer,0,n,buffer,0);count-=n;}
                sha.TransformFinalBlock(new byte[0],0,0);return BitConverter.ToString(sha.Hash).Replace("-","");
            }
        }
        public static long Recover(string path,string identity,bool allowLegacy,CancellationToken token){
            token.ThrowIfCancellationRequested();SafePath(path);SafePath(path+".ydr-state");
            string state=path+".ydr-state",header=Header(identity);bool legacy=!File.Exists(state);long confirmed=0;bool rewrite=legacy;
            StringBuilder valid=new StringBuilder(header+"\n");JavaScriptSerializer json=new JavaScriptSerializer();
            using(FileStream data=new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.Read)){
                if(legacy&&allowLegacy){
                    // Old releases did not have checkpoints. Their bytes remain
                    // provisional until the final cloud MD5 succeeds.
                    data.Flush(true);while(confirmed<data.Length){long end=Math.Min(data.Length,confirmed+CheckpointBytes);string digest=HashRange(data,end-confirmed,token);valid.Append(json.Serialize(new Record{Start=confirmed,End=end,Hash=digest})).Append('\n');confirmed=end;}
                }else if(!legacy){
                    using(StreamReader input=new StreamReader(state,Encoding.UTF8)){
                        if(input.ReadLine()!=header)rewrite=true;
                        else {string line;while((line=input.ReadLine())!=null){
                            token.ThrowIfCancellationRequested();Record record=null;try{record=json.Deserialize<Record>(line);}catch{rewrite=true;break;}
                            if(record==null||record.Start!=confirmed||record.End<=confirmed||record.End-confirmed>CheckpointBytes||record.End>data.Length||String.IsNullOrEmpty(record.Hash)){rewrite=true;break;}
                            if(HashRange(data,record.End-confirmed,token)!=record.Hash){rewrite=true;break;}
                            valid.Append(json.Serialize(record)).Append('\n');confirmed=record.End;
                        }}
                    }
                }
                token.ThrowIfCancellationRequested();
                if(data.Length!=confirmed){data.SetLength(confirmed);data.Flush(true);}
                // A crash may leave a whole JSON object but omit its newline.
                // It is valid, but must be separated from the next appended row.
                if(!rewrite)using(FileStream tail=File.OpenRead(state)){if(tail.Length==0)rewrite=true;else{tail.Position=tail.Length-1;rewrite=tail.ReadByte()!=10;}}
                if(rewrite)AtomicText(state,valid.ToString());
            }
            return confirmed;
        }
        public DurablePartial(string path,string identity,CancellationToken token){
            state=path+".ydr-state";header=Header(identity);
            committed=Recover(path,identity,true,token);
            output=new FileStream(path,FileMode.Open,FileAccess.Write,FileShare.Read,65536,true);output.Position=committed;
        }
        // Preparation has already verified this offset before the HTTP timeout.
        internal DurablePartial(string path,string identity,long verifiedOffset){
            state=path+".ydr-state";header=Header(identity);SafePath(path);SafePath(state);
            output=new FileStream(path,FileMode.Open,FileAccess.Write,FileShare.Read,65536,true);
            if(output.Length!=verifiedOffset||!File.Exists(state)){output.Dispose();throw new IOException("Partial changed after checkpoint verification.");}
            committed=verifiedOffset;output.Position=committed;
        }
        void Added(byte[] buffer,int offset,int count){hash.TransformBlock(buffer,offset,count,buffer,offset);pending+=count;if(pending>=CheckpointBytes||elapsed.Elapsed.TotalSeconds>=15)Flush();}
        public override async Task WriteAsync(byte[] buffer,int offset,int count,CancellationToken token){
            // Bound each journal extent even when a caller writes a huge buffer.
            while(count>0){int n=(int)Math.Min(count,CheckpointBytes-pending);await output.WriteAsync(buffer,offset,n,token).ConfigureAwait(false);Added(buffer,offset,n);offset+=n;count-=n;}
        }
        public override void Write(byte[] buffer,int offset,int count){while(count>0){int n=(int)Math.Min(count,CheckpointBytes-pending);output.Write(buffer,offset,n);Added(buffer,offset,n);offset+=n;count-=n;}}
        public override void Flush(){
            if(pending==0){output.Flush(true);return;}
            // A failed WriteAsync can have written an unaccounted tail.
            output.SetLength(committed+pending);output.Flush(true);
            hash.TransformFinalBlock(new byte[0],0,0);
            Append(state,new JavaScriptSerializer().Serialize(new Record{Start=committed,End=committed+pending,Hash=BitConverter.ToString(hash.Hash).Replace("-","")}));
            committed+=pending;pending=0;hash.Initialize();elapsed.Restart();
        }
        protected override void Dispose(bool disposing){if(disposing&&!disposed){disposed=true;try{Flush();}finally{output.Dispose();hash.Dispose();}}base.Dispose(disposing);}
        internal static void Forget(string path){SafePath(path+".ydr-state");SafePath(path+".ydr-state.tmp");File.Delete(path+".ydr-state");File.Delete(path+".ydr-state.tmp");}
        internal static void Reset(string path,string identity){
            SafePath(path);AtomicText(path+".ydr-state",Header(identity)+"\n");
            using(FileStream data=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.Read))data.Flush(true);
        }
        public override bool CanRead{get{return false;}}public override bool CanSeek{get{return false;}}public override bool CanWrite{get{return !disposed;}}
        public override long Length{get{return output.Length;}}public override long Position{get{return output.Position;}set{throw new NotSupportedException();}}
        public override int Read(byte[] buffer,int offset,int count){throw new NotSupportedException();}public override long Seek(long offset,SeekOrigin origin){throw new NotSupportedException();}
        public override void SetLength(long length){if(length!=0)throw new NotSupportedException();AtomicText(state,header+"\n");output.SetLength(0);output.Position=0;output.Flush(true);committed=pending=0;hash.Initialize();elapsed.Restart();}
    }
}
