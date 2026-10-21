using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32.SafeHandles;

namespace DiskRescue {
    // One-time, conservative repair of legacy and current empty sparse archives.
    // Handles stay exclusive from validation through deletion; no content scan.
    internal static class PreservedCleanup {
        [StructLayout(LayoutKind.Sequential)]struct AllocatedRange {public long Offset,Length;}
        sealed class SavedLedger {public string Version{get;set;}public bool Blocked{get;set;}}
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool DeviceIoControl(SafeFileHandle file,uint code,ref AllocatedRange input,int inputSize,out AllocatedRange output,int outputSize,out int returned,IntPtr overlapped);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetFileInformationByHandle(SafeFileHandle file,int infoClass,ref byte info,uint size);
        static readonly Regex ArchiveName=new Regex(@"_\d{8}_\d{6}_[0-9a-f]{10}(?:\.[^\\]+)?\.map$",RegexOptions.CultureInvariant);
        static bool Inside(string path,string parent){return Path.GetFullPath(path).StartsWith(Path.GetFullPath(parent).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase);}
        static SafeFileHandle Open(string path){var handle=CreateFile(path,0x80010000,0,IntPtr.Zero,3,0,IntPtr.Zero);if(handle.IsInvalid){handle.Dispose();throw new IOException("Archive candidate is inaccessible.");}return handle;}
        static bool Empty(SafeFileHandle file,long length){AllocatedRange input=new AllocatedRange{Length=length},output;int returned;return DeviceIoControl(file,0x940CF,ref input,16,out output,16,out returned,IntPtr.Zero)&&returned==0;}
        static void Delete(SafeFileHandle file){byte yes=1;if(!SetFileInformationByHandle(file,4,ref yes,1))throw new IOException("Archive candidate could not be removed.");}
        internal static bool DiscardEmptyMapped(DiskFile f,string source,string sidecar){
            if(source!=f.Destination+".ydr-download"||sidecar!=f.Destination+".ydr-map"||!File.Exists(source)||!File.Exists(sidecar))return false;
            try{
                DurablePartial.SafePath(source);DurablePartial.SafePath(sidecar);
                if((File.GetAttributes(source)&FileAttributes.SparseFile)==0||(File.GetAttributes(source)&FileAttributes.ReadOnly)!=0||(File.GetAttributes(sidecar)&FileAttributes.ReadOnly)!=0)return false;
                using(SafeFileHandle mapHandle=Open(sidecar))using(FileStream mapStream=new FileStream(mapHandle,FileAccess.Read)){
                    RangeMap.Header header=RangeMap.EmptyHeader(mapStream);
                    if(header==null||header.Length!=f.Length||!ModernIdentity(header.Identity,CloudIntegrity.Version(f)))return false;
                    using(SafeFileHandle payloadHandle=Open(source))using(FileStream payloadStream=new FileStream(payloadHandle,FileAccess.Read)){
                        if(payloadStream.Length!=header.Length||!Empty(payloadHandle,header.Length))return false;
                        Delete(payloadHandle);Delete(mapHandle);return true;
                    }
                }
            }catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}
        }
        static bool ModernIdentity(string identity,string version){
            if(identity==null)return false;int end=identity.IndexOf('\n');
            return end>0&&Regex.IsMatch(identity.Substring(0,end),@"\Aydr-context:api-[0-9a-fA-F]{24}\z",RegexOptions.CultureInvariant)&&identity.Substring(end+1)==version;
        }
        public static void Run(string state,string root,string context,string owner,CancellationToken token){
            CleanFiles(state,root,context,owner,token);
            CleanDirectories(state,root,owner,token);
        }
        static void CleanDirectories(string state,string root,string owner,CancellationToken token){
            string done=Path.Combine(state,"empty-archive-directories-v2.done"),filesDone=Path.Combine(state,"empty-sparse-v2.done"),gate=Path.Combine(state,"empty-sparse-105.lock");
            try{
                token.ThrowIfCancellationRequested();DurablePartial.SafePath(done);DurablePartial.SafePath(filesDone);
                if(File.Exists(done)||!File.Exists(filesDone))return;DurablePartial.SafePath(gate);
                using(FileStream held=new FileStream(gate,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
                    if(File.Exists(done)||Directory.GetFiles(state,"intent-*.json").Length!=0)return;
                    string config=Path.Combine(state,"Сохранённые прежние файлы.root");DurablePartial.SafePath(config);
                    if(!File.Exists(config)){DurablePartial.AtomicText(done,"1\n");return;}
                    string archive=File.ReadAllText(config);if(!Inside(archive,root))return;DurablePartial.SafePath(archive);
                    if(!Directory.Exists(archive)){DurablePartial.AtomicText(done,"1\n");return;}
                    string marker=Path.Combine(archive,".ydr-owner");DurablePartial.SafePath(marker);if(!File.Exists(marker)||File.ReadAllText(marker)!=owner)return;
                    var pending=new Stack<string>();var visited=new List<string>();pending.Push(archive);
                    while(pending.Count>0){
                        token.ThrowIfCancellationRequested();string current=pending.Pop();DurablePartial.SafePath(current);
                        foreach(string child in Directory.GetDirectories(current))if((File.GetAttributes(child)&FileAttributes.ReparsePoint)==0){pending.Push(child);visited.Add(child);}
                    }
                    bool complete=true;
                    for(int i=visited.Count-1;i>=0;i--){
                        token.ThrowIfCancellationRequested();string candidate=visited[i];if(!Inside(candidate,archive))throw new IOException("Directory outside archive.");
                        try{DurablePartial.SafePath(candidate);if(!Directory.EnumerateFileSystemEntries(candidate).Any())Directory.Delete(candidate,false);}
                        catch(IOException){complete=false;}catch(UnauthorizedAccessException){complete=false;}
                    }
                    token.ThrowIfCancellationRequested();if(complete)DurablePartial.AtomicText(done,"1\n");
                }
            }catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(InvalidOperationException){}
        }
        static void CleanFiles(string state,string root,string context,string owner,CancellationToken token){
            string done=Path.Combine(state,"empty-sparse-v2.done"),gate=Path.Combine(state,"empty-sparse-105.lock");
            try{
                token.ThrowIfCancellationRequested();DurablePartial.SafePath(done);if(File.Exists(done))return;DurablePartial.SafePath(gate);
                using(FileStream held=new FileStream(gate,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
                    if(File.Exists(done)||Directory.GetFiles(state,"intent-*.json").Length!=0)return;
                    string config=Path.Combine(state,"Сохранённые прежние файлы.root");DurablePartial.SafePath(config);
                    if(!File.Exists(config)){DurablePartial.AtomicText(done,"1\n");return;}
                    string archive=File.ReadAllText(config);if(!Inside(archive,root))return;DurablePartial.SafePath(archive);
                    if(!Directory.Exists(archive)){DurablePartial.AtomicText(done,"1\n");return;}
                    string marker=Path.Combine(archive,".ydr-owner");DurablePartial.SafePath(marker);if(!File.Exists(marker)||File.ReadAllText(marker)!=owner)return;
                    var versions=new HashSet<string>(StringComparer.Ordinal);var json=new JavaScriptSerializer();
                    foreach(string ledger in Directory.GetFiles(state,"aux-*.json")){
                        token.ThrowIfCancellationRequested();
                        DurablePartial.SafePath(ledger);SavedLedger saved;try{saved=json.Deserialize<SavedLedger>(File.ReadAllText(ledger));}catch(ArgumentException){continue;}catch(InvalidOperationException){continue;}
                        if(saved==null||saved.Blocked||saved.Version==null)continue;int end=saved.Version.LastIndexOf('\n');if(end<0)continue;
                        string canonical=saved.Version.Substring(0,end+1);string[] fields=canonical.Split('\n');
                        if(fields.Length!=4||fields[1].Length<33||fields[1][fields[1].Length-33]!='|'||!fields[1].Substring(fields[1].Length-32).All(Uri.IsHexDigit)||(saved.Version!=canonical&&!CloudIntegrity.LegacyVersion(saved.Version,canonical)))continue;
                        versions.Add(context+"\n"+canonical);
                        versions.Add(canonical);
                    }
                    bool complete=true;var directories=new Stack<string>();directories.Push(archive);
                    while(directories.Count>0){
                        token.ThrowIfCancellationRequested();
                        string directory=directories.Pop();DurablePartial.SafePath(directory);
                        foreach(string child in Directory.GetDirectories(directory))if((File.GetAttributes(child)&FileAttributes.ReparsePoint)==0)directories.Push(child);
                        foreach(string map in Directory.GetFiles(directory,"*.map")){
                            token.ThrowIfCancellationRequested();
                            if(!ArchiveName.IsMatch(map))continue;
                            try{
                                string payload=map.Substring(0,map.Length-4);DurablePartial.SafePath(map);DurablePartial.SafePath(payload);
                                if(!File.Exists(payload))continue;FileAttributes attributes=File.GetAttributes(payload);
                                if((attributes&FileAttributes.SparseFile)==0||(attributes&FileAttributes.ReadOnly)!=0||(File.GetAttributes(map)&FileAttributes.ReadOnly)!=0)continue;
                                using(SafeFileHandle mapHandle=Open(map))using(FileStream mapStream=new FileStream(mapHandle,FileAccess.Read)){
                                    RangeMap.Header header=RangeMap.EmptyHeader(mapStream);
                                    if(header==null)continue;
                                    int split=header.Identity.IndexOf('\n');string version=split<0?null:header.Identity.Substring(split+1);
                                    if(!versions.Contains(header.Identity)&&!(version!=null&&versions.Contains(version)&&ModernIdentity(header.Identity,version)))continue;
                                    using(SafeFileHandle payloadHandle=Open(payload))using(FileStream payloadStream=new FileStream(payloadHandle,FileAccess.Read)){
                                        if(payloadStream.Length!=header.Length||!Empty(payloadHandle,header.Length))continue;
                                        // Both files are still locked and unchanged since validation.
                                        Delete(payloadHandle);Delete(mapHandle);
                                    }
                                }
                            }catch(IOException){complete=false;}catch(UnauthorizedAccessException){complete=false;}
                        }
                    }
                    token.ThrowIfCancellationRequested();if(complete)DurablePartial.AtomicText(done,"1\n");
                }
            }catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(InvalidOperationException){}catch(FormatException){}catch(OverflowException){}
        }
    }
}
