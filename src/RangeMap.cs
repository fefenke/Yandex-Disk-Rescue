using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32.SafeHandles;

namespace DiskRescue {
    internal sealed class RangeMap {
        public sealed class Header {public int Version=1,Parts;public long Length;public string Identity;public long[] Boundaries;}
        internal sealed class SparseUnavailable:IOException {public SparseUnavailable():base("Sparse files are not supported by the destination."){}}
        sealed class Extent {public int Index;public long Start,End;public string Hash;}
        readonly object sync=new object();readonly Header header;readonly string payload,journal;
        readonly List<Extent> records=new List<Extent>();readonly long[] confirmed;readonly bool[] active;
        readonly JavaScriptSerializer json=new JavaScriptSerializer();
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool DeviceIoControl(SafeFileHandle handle,uint code,IntPtr input,int inputSize,IntPtr output,int outputSize,out int returned,IntPtr overlapped);
        public int Parts{get{return header.Parts;}}public long Length{get{return header.Length;}}
        public long Start(int index){return header.Boundaries==null?(header.Length/header.Parts)*index+Math.Min(index,header.Length%header.Parts):header.Boundaries[index];}
        public long End(int index){return Start(index+1);}
        public long Offset(int index){lock(sync)return confirmed[index];}
        public long Bytes{get{lock(sync){long bytes=0;for(int i=0;i<Parts;i++)bytes+=confirmed[i]-Start(i);return bytes;}}}
        public static bool Exists(string destination){return File.Exists(destination+".ydr-map")||File.Exists(destination+".ydr-download");}
        static Header ParseHeader(string value){try{return new JavaScriptSerializer().Deserialize<Header>(value);}catch(ArgumentException){return null;}catch(InvalidOperationException){return null;}catch(FormatException){return null;}catch(OverflowException){return null;}catch(Exception ex){if(ex.InnerException is FormatException||ex.InnerException is OverflowException)return null;throw;}}
        static bool Valid(Header h){return h!=null&&h.Version==1&&h.Length>=0&&h.Parts>=1&&h.Parts<=8&&(h.Boundaries==null||(h.Boundaries.Length==h.Parts+1&&h.Boundaries[0]==0&&h.Boundaries[h.Parts]==h.Length&&Enumerable.Range(0,h.Parts).All(i=>h.Boundaries[i]>=0&&h.Boundaries[i]<=h.Boundaries[i+1])));}
        static long Boundary(Header h,int i){return h.Boundaries==null?(h.Length/h.Parts)*i+Math.Min(i,h.Length%h.Parts):h.Boundaries[i];}
        public static bool Matches(string destination,string identity,long length){try{DurablePartial.SafePath(destination+".ydr-map");using(StreamReader s=File.OpenText(destination+".ydr-map")){Header h=ParseHeader(s.ReadLine());return Valid(h)&&h.Identity==identity&&h.Length==length;}}catch(FileNotFoundException){return false;}catch(ArgumentException){return false;}catch(FormatException){return false;}catch(OverflowException){return false;}catch(InvalidOperationException){return false;}}
        public static long Progress(string destination){
            try{DurablePartial.SafePath(destination+".ydr-map");using(StreamReader input=new StreamReader(new FileStream(destination+".ydr-map",FileMode.Open,FileAccess.Read,FileShare.ReadWrite))){var json=new JavaScriptSerializer();Header h=ParseHeader(input.ReadLine());if(!Valid(h))return 0;long[] offsets=new long[h.Parts];for(int i=0;i<h.Parts;i++)offsets[i]=Boundary(h,i);long bytes=0;string line;while((line=input.ReadLine())!=null){Extent r;try{r=json.Deserialize<Extent>(line);}catch{break;}if(r==null||r.Index<0||r.Index>=h.Parts)break;long end=Boundary(h,r.Index+1);if(r.Start!=offsets[r.Index]||r.End<=r.Start||r.End>end)break;bytes+=r.End-r.Start;offsets[r.Index]=r.End;}return bytes;}}
            catch(IOException){return 0;}catch(ArgumentException){return 0;}catch(FormatException){return 0;}catch(OverflowException){return 0;}catch(InvalidOperationException){return 0;}
        }
        public static RangeMap Open(string destination,string identity,long length,int parts,CancellationToken token){return new RangeMap(destination,identity,length,parts,null,token);}
        public static RangeMap OpenPlanned(string destination,string identity,long length,int parts,long[] boundaries,CancellationToken token){return new RangeMap(destination,identity,length,parts,boundaries,token);}
        public static RangeMap Single(string destination,string identity,long length,CancellationToken token){DurablePartial.AtomicText(destination+".ydr-map",new JavaScriptSerializer().Serialize(new Header{Identity=identity,Length=length,Parts=1})+"\n");return Open(destination,identity,length,1,token);}
        RangeMap(string destination,string identity,long length,int parts,long[] boundaries,CancellationToken token){
            token.ThrowIfCancellationRequested();payload=destination+".ydr-download";journal=destination+".ydr-map";DurablePartial.SafePath(payload);DurablePartial.SafePath(journal);
            bool hadMap=File.Exists(journal),hadData=File.Exists(payload);
            if(hadMap){using(StreamReader reader=File.OpenText(journal)){try{header=ParseHeader(reader.ReadLine());}catch(ArgumentException){throw new IOException("Unreadable range map.");}}
                if(!Valid(header)||header.Identity!=identity||header.Length!=length)throw new IOException("Range map does not match cloud version.");
            }else{if(hadData)throw new IOException("Range data exists without its map.");header=new Header{Identity=identity,Length=length,Parts=Math.Max(1,Math.Min(8,(int)Math.Min(parts,Math.Max(1,length)))),Boundaries=boundaries};}
            if(header.Boundaries!=null&&(header.Boundaries.Length!=header.Parts+1||header.Boundaries[0]!=0||header.Boundaries[header.Parts]!=length||Enumerable.Range(0,header.Parts).Any(i=>header.Boundaries[i]>header.Boundaries[i+1])))throw new IOException("Invalid saved range boundaries.");
            confirmed=new long[Parts];active=new bool[Parts];for(int i=0;i<Parts;i++)confirmed[i]=Start(i);
            using(FileStream data=new FileStream(payload,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.ReadWrite)){
                if(!hadData){int returned;if(!DeviceIoControl(data.SafeFileHandle,0x900C4,IntPtr.Zero,0,IntPtr.Zero,0,out returned,IntPtr.Zero))throw new SparseUnavailable();}
                bool rewrite=!hadMap||!hadData;
                if(hadMap&&hadData)using(StreamReader input=File.OpenText(journal)){input.ReadLine();string line;while((line=input.ReadLine())!=null){
                    token.ThrowIfCancellationRequested();Extent r;try{r=json.Deserialize<Extent>(line);}catch(Exception){rewrite=true;break;}
                    if(r==null||r.Index<0||r.Index>=Parts){rewrite=true;break;}
                    if(r.Start!=confirmed[r.Index]||r.End<=r.Start||r.End>End(r.Index)||r.End-r.Start>DurablePartial.CheckpointBytes||r.End>data.Length||String.IsNullOrEmpty(r.Hash)){rewrite=true;continue;}
                    data.Position=r.Start;if(Hash(data,r.End-r.Start,token)!=r.Hash){rewrite=true;continue;}records.Add(r);confirmed[r.Index]=r.End;
                }}
                token.ThrowIfCancellationRequested();if(data.Length!=length){data.SetLength(length);data.Flush(true);}
                if(!rewrite)using(FileStream tail=File.OpenRead(journal)){tail.Position=Math.Max(0,tail.Length-1);rewrite=tail.ReadByte()!=10;}
                if(rewrite)Save();
            }
        }
        static string Hash(FileStream input,long count,CancellationToken token){using(SHA256 hash=SHA256.Create()){byte[] buffer=new byte[65536];while(count>0){token.ThrowIfCancellationRequested();int n=input.Read(buffer,0,(int)Math.Min(count,buffer.Length));if(n==0)throw new EndOfStreamException();hash.TransformBlock(buffer,0,n,buffer,0);count-=n;}hash.TransformFinalBlock(new byte[0],0,0);return BitConverter.ToString(hash.Hash).Replace("-","");}}
        void Save(){var text=new StringBuilder(json.Serialize(header)+"\n");foreach(Extent r in records)text.Append(json.Serialize(r)).Append('\n');DurablePartial.AtomicText(journal,text.ToString());}
        public Stream Writer(int index){lock(sync){if(index<0||index>=Parts||active[index])throw new IOException("Range already has a writer.");active[index]=true;try{return new RangeWriter(this,index);}catch{active[index]=false;throw;}}}
        void Commit(int index,long start,long end,string hash){lock(sync){if(start!=confirmed[index]||end>End(index))throw new IOException("Invalid range checkpoint order.");Extent record=new Extent{Index=index,Start=start,End=end,Hash=hash};byte[] bytes=Encoding.UTF8.GetBytes(json.Serialize(record)+"\n");DurablePartial.SafePath(journal);using(FileStream output=new FileStream(journal,FileMode.Append,FileAccess.Write,FileShare.Read)){output.Write(bytes,0,bytes.Length);output.Flush(true);}records.Add(record);confirmed[index]=end;}}
        public void Reset(int index){lock(sync){if(active[index])throw new IOException("Cannot reset a live range.");records.RemoveAll(r=>r.Index==index);confirmed[index]=Start(index);Save();}}
        public void Import(string source,long sourceOffset,long offset,long count,CancellationToken token){
            DurablePartial.SafePath(source);using(FileStream input=File.OpenRead(source)){
                long end=offset+count;for(int i=0;i<Parts;i++){long start=Math.Max(offset,Offset(i)),finish=Math.Min(end,End(i));if(start>=finish)continue;if(start!=Offset(i))throw new IOException("Legacy import would leave an unconfirmed gap.");input.Position=sourceOffset+start-offset;using(Stream output=Writer(i)){byte[] buffer=new byte[65536];while(start<finish){token.ThrowIfCancellationRequested();int n=input.Read(buffer,0,(int)Math.Min(buffer.Length,finish-start));if(n==0)throw new EndOfStreamException();output.Write(buffer,0,n);start+=n;}}}
            }
        }
        sealed class RangeWriter:Stream {
            readonly RangeMap map;readonly int index;readonly FileStream output;readonly SHA256 hash=SHA256.Create();readonly Stopwatch clock=Stopwatch.StartNew();long committed,pending;bool disposed;
            public RangeWriter(RangeMap map,int index){this.map=map;this.index=index;committed=map.confirmed[index];DurablePartial.SafePath(map.payload);output=new FileStream(map.payload,FileMode.Open,FileAccess.Write,FileShare.ReadWrite,65536,true);output.Position=committed;}
            void Added(byte[] b,int o,int n){hash.TransformBlock(b,o,n,b,o);pending+=n;if(pending>=DurablePartial.CheckpointBytes||clock.Elapsed.TotalSeconds>=15)Flush();}
            int Slice(int count){long left=map.End(index)-committed-pending;if(count>left)throw new IOException("Write exceeded the mapped range.");return (int)Math.Min(count,DurablePartial.CheckpointBytes-pending);}
            public override void Write(byte[] b,int o,int c){while(c>0){int n=Slice(c);output.Write(b,o,n);Added(b,o,n);o+=n;c-=n;}}
            public override async Task WriteAsync(byte[] b,int o,int c,CancellationToken token){while(c>0){int n=Slice(c);await output.WriteAsync(b,o,n,token).ConfigureAwait(false);Added(b,o,n);o+=n;c-=n;}}
            public override void Flush(){if(pending==0)return;output.Flush(true);hash.TransformFinalBlock(new byte[0],0,0);map.Commit(index,committed,committed+pending,BitConverter.ToString(hash.Hash).Replace("-",""));committed+=pending;pending=0;hash.Initialize();clock.Restart();}
            protected override void Dispose(bool disposing){if(disposing&&!disposed){disposed=true;try{Flush();}finally{output.Dispose();hash.Dispose();lock(map.sync)map.active[index]=false;}}base.Dispose(disposing);}
            public override bool CanRead{get{return false;}}public override bool CanSeek{get{return false;}}public override bool CanWrite{get{return !disposed;}}public override long Length{get{return map.End(index)-map.Start(index);}}public override long Position{get{return committed+pending;}set{throw new NotSupportedException();}}
            public override int Read(byte[] b,int o,int c){throw new NotSupportedException();}public override long Seek(long p,SeekOrigin o){throw new NotSupportedException();}public override void SetLength(long l){throw new NotSupportedException();}
        }
    }
}
