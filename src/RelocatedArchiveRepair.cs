using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32.SafeHandles;

namespace DiskRescue {
    // Archived originals always survive imports. No completion marker: a later
    // inventory or a temporarily locked file can safely be reconsidered.
    internal static class RelocatedArchiveRepair {
        const string Title="Сохранённые прежние файлы";
        static readonly Regex ArchiveName=new Regex(@"_\d{8}_\d{6}_[0-9a-f]{10}(?:\.[^\\]+)?\.map$",RegexOptions.CultureInvariant);
        sealed class Record {public int Index{get;set;}public long Start{get;set;}public long End{get;set;}public string Hash{get;set;}}
        [StructLayout(LayoutKind.Sequential)]struct Allocation {public long Offset,Length;}
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool DeviceIoControl(SafeFileHandle file,uint code,ref Allocation input,int inputSize,out Allocation output,int outputSize,out int returned,IntPtr overlapped);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetFileInformationByHandle(SafeFileHandle file,int infoClass,ref byte value,uint size);
        static bool Inside(string path,string parent){return Path.GetFullPath(path).StartsWith(Path.GetFullPath(parent).TrimEnd('\\','/')+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);}
        static long Boundary(RangeMap.Header h,int i){return h.Boundaries==null?h.Length/h.Parts*i+Math.Min(i,h.Length%h.Parts):h.Boundaries[i];}
        static bool Valid(RangeMap.Header h){return h!=null&&h.Version==1&&h.Length>0&&h.Parts>0&&h.Parts<=8&&h.Identity!=null&&(h.Boundaries==null||h.Boundaries.Length==h.Parts+1&&h.Boundaries[0]==0&&h.Boundaries[h.Parts]==h.Length&&Enumerable.Range(0,h.Parts).All(i=>h.Boundaries[i]>=0&&h.Boundaries[i]<=h.Boundaries[i+1]));}
        static SafeFileHandle OpenDelete(string path){var h=CreateFile(path,0x80010000,0,IntPtr.Zero,3,0,IntPtr.Zero);if(h.IsInvalid){h.Dispose();throw new IOException("Archive is locked.");}return h;}
        static void Delete(SafeFileHandle h){byte yes=1;if(!SetFileInformationByHandle(h,4,ref yes,1))throw new IOException("Archive deletion failed.");}
        static string Hash(FileStream stream,long length,CancellationToken token){using(var hash=SHA256.Create()){byte[] buffer=new byte[65536];while(length>0){token.ThrowIfCancellationRequested();int n=stream.Read(buffer,0,(int)Math.Min(buffer.Length,length));if(n==0)throw new EndOfStreamException();hash.TransformBlock(buffer,0,n,buffer,0);length-=n;}hash.TransformFinalBlock(new byte[0],0,0);return BitConverter.ToString(hash.Hash).Replace("-","");}}
        public static void Run(string context,string root,IEnumerable<DiskFile> files,CancellationToken token,Func<DiskFile,bool> canImport=null){
            token.ThrowIfCancellationRequested();
            try {
                string state=Path.Combine(context,"preservation"),config=Path.Combine(state,Title+".root"),ownerPath=Path.Combine(state,"owner.id");
                DurablePartial.SafePath(config);DurablePartial.SafePath(ownerPath);
                if(!File.Exists(config)||!File.Exists(ownerPath)||Directory.GetFiles(state,"intent-*.json").Length!=0)return;
                string archive=File.ReadAllText(config),owner=File.ReadAllText(ownerPath).Trim();
                if(owner.Length!=28||!owner.StartsWith("api-",StringComparison.Ordinal)||!owner.Substring(4).All(Uri.IsHexDigit)||!Inside(archive,root))return;
                DurablePartial.SafePath(archive);string marker=Path.Combine(archive,".ydr-owner");DurablePartial.SafePath(marker);
                if(!Directory.Exists(archive)||!File.Exists(marker)||File.ReadAllText(marker)!=owner)return;
                string key=Path.GetFileName(context.TrimEnd('\\','/'));var inventory=files.GroupBy(f=>"ydr-context:"+key+"\n"+CloudIntegrity.Version(f)).Where(g=>g.Count()==1).ToDictionary(g=>g.Key,g=>g.Single(),StringComparer.Ordinal);
                var pending=new Stack<string>();var visited=new List<string>();pending.Push(archive);
                while(pending.Count>0){
                    token.ThrowIfCancellationRequested();string directory=pending.Pop();DurablePartial.SafePath(directory);
                    foreach(string child in Directory.GetDirectories(directory))if((File.GetAttributes(child)&FileAttributes.ReparsePoint)==0){pending.Push(child);visited.Add(child);}
                    foreach(string map in Directory.GetFiles(directory,"*.map")){
                        token.ThrowIfCancellationRequested();try{Repair(map,context,root,archive,key,inventory,token,canImport);}
                        catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(InvalidOperationException){}catch(FormatException){}catch(OverflowException){}
                    }
                }
                for(int i=visited.Count-1;i>=0;i--){
                    token.ThrowIfCancellationRequested();string child=visited[i];
                    try{if(!Inside(child,archive))continue;DurablePartial.SafePath(child);if((File.GetAttributes(child)&FileAttributes.ReparsePoint)!=0)continue;
                        if(!Directory.EnumerateFileSystemEntries(child).Any())Directory.Delete(child,false);
                    }catch(IOException){}catch(UnauthorizedAccessException){}
                }
            }catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(InvalidOperationException){}
        }
        static void Repair(string map,string context,string root,string archive,string key,Dictionary<string,DiskFile> inventory,CancellationToken token,Func<DiskFile,bool> canImport){
            if(!ArchiveName.IsMatch(map))return;
            string payload=map.Substring(0,map.Length-4);if(!Inside(payload,archive))return;
            DurablePartial.SafePath(map);DurablePartial.SafePath(payload);if(!File.Exists(payload))return;
            using(var mapHandle=OpenDelete(map))using(var mapStream=new FileStream(mapHandle,FileAccess.Read))using(var reader=new StreamReader(mapStream)){
                var json=new JavaScriptSerializer();token.ThrowIfCancellationRequested();var header=json.Deserialize<RangeMap.Header>(reader.ReadLine());if(!Valid(header))return;
                int newline=header.Identity.IndexOf('\n');if(newline<0)return;string oldContext=header.Identity.Substring(0,newline);
                if(!Path.IsPathRooted(oldContext)||String.Equals(oldContext,context,StringComparison.OrdinalIgnoreCase)||Path.GetFileName(oldContext.TrimEnd('\\','/'))!=key)return;
                string identity="ydr-context:"+key+header.Identity.Substring(newline);DiskFile file;
                if(!inventory.TryGetValue(identity,out file)||file.Length!=header.Length||!Inside(file.Destination,root)||Inside(file.Destination,archive))return;
                var records=new List<Record>();long[] offsets=Enumerable.Range(0,header.Parts).Select(i=>Boundary(header,i)).ToArray();string line;
                while((line=reader.ReadLine())!=null){token.ThrowIfCancellationRequested();Record r=json.Deserialize<Record>(line);
                    if(r==null||r.Index<0||r.Index>=header.Parts||r.Start!=offsets[r.Index]||r.End<=r.Start||r.End>Boundary(header,r.Index+1)||r.End-r.Start>DurablePartial.CheckpointBytes||r.Hash==null||r.Hash.Length!=64||!r.Hash.All(Uri.IsHexDigit))return;
                    offsets[r.Index]=r.End;records.Add(r);
                }
                if(records.Count==0){
                    using(var handle=OpenDelete(payload))using(var data=new FileStream(handle,FileAccess.Read)){
                        if(data.Length!=header.Length||(File.GetAttributes(payload)&FileAttributes.SparseFile)==0)return;
                        Allocation input=new Allocation{Length=header.Length},output;int returned;
                        token.ThrowIfCancellationRequested();if(!DeviceIoControl(handle,0x940CF,ref input,16,out output,16,out returned,IntPtr.Zero)||returned!=0)return;
                        token.ThrowIfCancellationRequested();Delete(handle);Delete(mapHandle);
                    }return;
                }
                if(File.Exists(file.Destination))return;
                if(canImport!=null&&!canImport(file))return;
                DurablePartial.SafePath(file.Destination);if(Directory.Exists(file.Destination+".parts"))return;
                RangeMap current=null;
                if(RangeMap.Exists(file.Destination)){
                    if(!RangeMap.Matches(file.Destination,identity,header.Length))return;
                    current=RangeMap.OpenPlanned(file.Destination,identity,header.Length,header.Parts,header.Boundaries,token);
                    bool covered=true;foreach(Record r in records)for(int i=0;i<current.Parts;i++)if(Math.Max(r.Start,current.Start(i))<Math.Min(r.End,current.End(i))&&current.Offset(i)<Math.Min(r.End,current.End(i)))covered=false;
                    if(covered)return;
                }
                using(var data=new FileStream(payload,FileMode.Open,FileAccess.Read,FileShare.Read)){
                    if(data.Length!=header.Length)return;
                    foreach(Record r in records){token.ThrowIfCancellationRequested();data.Position=r.Start;if(Hash(data,r.End-r.Start,token)!=r.Hash)return;}
                    token.ThrowIfCancellationRequested();Directory.CreateDirectory(Path.GetDirectoryName(file.Destination));
                    if(current==null)current=RangeMap.OpenPlanned(file.Destination,identity,header.Length,header.Parts,header.Boundaries,token);
                    foreach(Record r in records){
                        token.ThrowIfCancellationRequested();
                        // Only append contiguous missing bytes; existing confirmed ranges win.
                        bool gap=false;for(int i=0;i<current.Parts;i++){long start=Math.Max(r.Start,current.Start(i)),end=Math.Min(r.End,current.End(i));if(start<end&&start>current.Offset(i)){gap=true;break;}}
                        if(!gap)current.Import(payload,r.Start,r.Start,r.End-r.Start,token);
                    }
                }
            }
        }
    }
}
