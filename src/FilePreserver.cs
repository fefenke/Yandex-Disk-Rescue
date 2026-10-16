using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;

namespace DiskRescue {
    // Durable intent precedes every move. A missing quarantined artifact never
    // clears the per-cloud-version failure record.
    internal sealed class FilePreserver {
        readonly object sync=new object();readonly string state,root,owner;readonly string[] destinations;
        readonly HashSet<string> recoveryBlocked=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string,List<Ledger>> legacy=new Dictionary<string,List<Ledger>>(StringComparer.Ordinal);
        readonly JavaScriptSerializer json=new JavaScriptSerializer();
        sealed class Artifact {public string Path,Hash;public long Length;}
        sealed class Ledger {public string Version;public bool Blocked;public List<Artifact> Copies=new List<Artifact>();}
        sealed class Operation {public string LedgerPath,Version,Source,Target,OldSource,OldTarget,Hash,Destination,Expected,Algorithm;public long Length;public bool Blocked,Directory;}
        public FilePreserver(string directory,string root,IEnumerable<string> destinations,CancellationToken token=default(CancellationToken)){this.root=Path.GetFullPath(root);state=Path.Combine(directory,"preservation");owner=HistoryStore.IdentityKey(directory);this.destinations=destinations.ToArray();DurablePartial.SafePath(state);Directory.CreateDirectory(state);Recover();if(recoveryBlocked.Count==0)PreservedCleanup.Run(state,this.root,directory,owner,token);foreach(string path in Directory.GetFiles(state,"api-*.json")){Ledger l=Read(path);if(l.Version==null)continue;int end=l.Version.LastIndexOf('\n');if(end<0)continue;string key=l.Version.Substring(0,end+1);if(!CloudIntegrity.LegacyVersion(l.Version,key))continue;List<Ledger> entries;if(!legacy.TryGetValue(key,out entries))legacy[key]=entries=new List<Ledger>();entries.Add(l);}}
        string LedgerPath(DiskFile f){return Path.Combine(state,HistoryStore.IdentityKey(CloudIntegrity.Version(f))+".json");}
        Ledger Read(string path){DurablePartial.SafePath(path);return File.Exists(path)?json.Deserialize<Ledger>(File.ReadAllText(path)):new Ledger();}
        Ledger Read(DiskFile f){string path=LedgerPath(f);if(File.Exists(path))return Read(path);string version=CloudIntegrity.Version(f);List<Ledger> entries;if(!String.IsNullOrEmpty(f.Md5)&&legacy.TryGetValue(version,out entries))return new Ledger{Version=version,Blocked=entries.Any(l=>l.Blocked),Copies=entries.SelectMany(l=>l.Copies).ToList()};return new Ledger();}
        public bool Blocked(DiskFile f){lock(sync){Ledger l=Read(f);if(!l.Blocked||l.Version!=CloudIntegrity.Version(f))return false;f.PreservedPath=l.Copies.Count==0?"":l.Copies.Last().Path;return true;}}
        string ArchiveRoot(string title){
            string config=Path.Combine(state,title+".root");DurablePartial.SafePath(config);if(File.Exists(config)){string saved=File.ReadAllText(config);EnsureInside(saved,root);DurablePartial.SafePath(saved);if(!Directory.Exists(saved)){Directory.CreateDirectory(saved);DurablePartial.AtomicText(Path.Combine(saved,".ydr-owner"),owner);return saved;}string marker=Path.Combine(saved,".ydr-owner");DurablePartial.SafePath(marker);if(File.Exists(marker)&&File.ReadAllText(marker)==owner)return saved;}
            string candidate=Path.Combine(root,title);
            for(int i=0;;i++){
                if(i>0)candidate=Path.Combine(root,title+" (YDR-"+Guid.NewGuid().ToString("N").Substring(0,8)+")");
                DurablePartial.SafePath(candidate);string prefix=candidate.TrimEnd('\\')+"\\";
                if(destinations.Any(d=>String.Equals(d,candidate,StringComparison.OrdinalIgnoreCase)||d.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)))continue;
                if(Directory.Exists(candidate)){string marker=Path.Combine(candidate,".ydr-owner");DurablePartial.SafePath(marker);if(!File.Exists(marker)||File.ReadAllText(marker)!=owner)continue;}else Directory.CreateDirectory(candidate);
                DurablePartial.AtomicText(Path.Combine(candidate,".ydr-owner"),owner);DurablePartial.AtomicText(config,candidate);return candidate;
            }
        }
        static void EnsureInside(string path,string parent){if(!Path.GetFullPath(path).StartsWith(Path.GetFullPath(parent).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("Preservation path outside destination.");}
        public void CheckDestination(string path){lock(sync){if(recoveryBlocked.Contains(path))throw new IOException("Pending preservation operation requires recovery.");foreach(string config in Directory.GetFiles(state,"*.root")){string prefix=File.ReadAllText(config).TrimEnd('\\')+"\\";if(path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||String.Equals(path,prefix.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))throw new IOException("Cloud destination conflicts with preserved files.");}}}
        string ArchivePath(DiskFile f,string title,string hash){
            string relative=Path.GetFullPath(f.Destination).Substring(root.TrimEnd('\\').Length).TrimStart('\\');string directory=Path.Combine(ArchiveRoot(title),Path.GetDirectoryName(relative));DurablePartial.SafePath(directory);Directory.CreateDirectory(directory);
            return Path.Combine(directory,Path.GetFileNameWithoutExtension(relative)+"_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmss")+"_"+Guid.NewGuid().ToString("N").Substring(0,10)+Path.GetExtension(relative));
        }
        public bool Complete(DiskFile f,string payload,IntegrityResult check){lock(sync){
            if(!check.Match&&String.IsNullOrEmpty(check.Actual)){check.Actual=Fingerprint(payload,"sha256");check.Algorithm="sha256";f.ActualHash=check.Actual;}
            CheckDestination(f.Destination);string ledgerPath=LedgerPath(f);Ledger ledger=Read(f);
            Operation op=new Operation{LedgerPath=ledgerPath,Version=CloudIntegrity.Version(f),Source=payload,Target=f.Destination,Destination=f.Destination,Expected=check.Expected,Algorithm=check.Algorithm,Blocked=!check.Match,Hash=check.Actual,Length=new FileInfo(payload).Length};
            if(File.Exists(f.Destination)){
                IntegrityResult old=CloudIntegrity.Check(f.Destination,f,CancellationToken.None);
                if(!old.Match){op.OldSource=f.Destination;op.OldTarget=ArchivePath(f,old.Kind=="size_mismatch"?"Сохранённые прежние файлы":"Файлы с ошибкой проверки",old.Actual);}
                else if(check.Match&&old.Kind!="size_only"){File.Delete(payload);return true;}
                else if(check.Match){op.OldSource=f.Destination;op.OldTarget=ArchivePath(f,"Сохранённые прежние файлы",old.Actual);}
            }
            if(op.Blocked){
                Artifact duplicate=ledger.Copies.FirstOrDefault(a=>a.Hash==check.Actual&&a.Length==op.Length&&File.Exists(a.Path)&&new FileInfo(a.Path).Length==a.Length&&Fingerprint(a.Path,check.Algorithm)==a.Hash);
                op.Target=duplicate==null?ArchivePath(f,"Файлы с ошибкой проверки",check.Actual):duplicate.Path;
            }
            string intent=Path.Combine(state,"intent-"+Guid.NewGuid().ToString("N")+".json");DurablePartial.AtomicText(intent,json.Serialize(op));Apply(op);File.Delete(intent);f.PreservedPath=op.Blocked?op.Target:op.OldTarget;return !op.Blocked;
        }}
        public void PreservePartial(DiskFile f,string source,string sidecar){lock(sync){
            if(!File.Exists(source)&&!System.IO.Directory.Exists(source)){if(!String.IsNullOrEmpty(sidecar)&&File.Exists(sidecar))PreservePartial(f,sidecar,null);return;}
            string target=ArchivePath(f,"Сохранённые прежние файлы","");Operation op=new Operation{LedgerPath=Path.Combine(state,"aux-"+Guid.NewGuid().ToString("N")+".json"),Version=CloudIntegrity.Version(f),Source=source,Target=target,Destination=f.Destination,Directory=System.IO.Directory.Exists(source)};
            if(!String.IsNullOrEmpty(sidecar)&&File.Exists(sidecar)){op.OldSource=sidecar;op.OldTarget=target+".map";}
            string intent=Path.Combine(state,"intent-"+Guid.NewGuid().ToString("N")+".json");DurablePartial.AtomicText(intent,json.Serialize(op));Apply(op);File.Delete(intent);
        }}
        static string Fingerprint(string path,string algorithm){DurablePartial.SafePath(path);using(HashAlgorithm h=algorithm=="md5"?(HashAlgorithm)MD5.Create():SHA256.Create())using(Stream input=File.OpenRead(path))return BitConverter.ToString(h.ComputeHash(input)).Replace("-","").ToLowerInvariant();}
        static void MoveOnce(string source,string target){
            DurablePartial.SafePath(source);DurablePartial.SafePath(target);
            if(File.Exists(target)){
                if(File.Exists(source)){if(new FileInfo(source).Length!=new FileInfo(target).Length||Fingerprint(source,"sha256")!=Fingerprint(target,"sha256"))throw new IOException("Preservation target contains a different file.");File.Delete(source);}return;
            }
            if(!File.Exists(source))throw new IOException("Preservation source and target are missing.");Directory.CreateDirectory(Path.GetDirectoryName(target));File.Move(source,target);
        }
        void Apply(Operation op){
            EnsureInside(op.Source,root);EnsureInside(op.Target,root);EnsureInside(op.LedgerPath,state);
            if(!String.IsNullOrEmpty(op.OldSource)){EnsureInside(op.OldSource,root);EnsureInside(op.OldTarget,root);if(!File.Exists(op.OldTarget)&&File.Exists(op.OldSource)&&(File.Exists(op.Source)||!File.Exists(op.Target)))MoveOnce(op.OldSource,op.OldTarget);}
            if(op.Directory){DurablePartial.SafePath(op.Source);DurablePartial.SafePath(op.Target);if(!Directory.Exists(op.Target)){if(!Directory.Exists(op.Source))throw new IOException("Missing preserved directory.");Directory.Move(op.Source,op.Target);}}else if(!op.Blocked||File.Exists(op.Source)||File.Exists(op.Target))MoveOnce(op.Source,op.Target);
            Ledger ledger=Read(op.LedgerPath);ledger.Version=op.Version;ledger.Blocked=op.Blocked;
            if(op.Blocked&&!ledger.Copies.Any(a=>a.Path==op.Target))ledger.Copies.Add(new Artifact{Path=op.Target,Hash=op.Hash,Length=op.Length});
            DurablePartial.AtomicText(op.LedgerPath,json.Serialize(ledger));
        }
        void Recover(){foreach(string path in Directory.GetFiles(state,"intent-*.json")){DurablePartial.SafePath(path);Operation op=json.Deserialize<Operation>(File.ReadAllText(path));if(op==null)throw new IOException("Invalid preservation intent.");try{Apply(op);File.Delete(path);}catch(IOException){if(!String.IsNullOrEmpty(op.Destination))recoveryBlocked.Add(op.Destination);else if(op.Source.EndsWith(".ydr-download",StringComparison.OrdinalIgnoreCase))recoveryBlocked.Add(op.Source.Substring(0,op.Source.Length-13));recoveryBlocked.Add(op.Source);recoveryBlocked.Add(op.Target);if(!String.IsNullOrEmpty(op.OldSource))recoveryBlocked.Add(op.OldSource);}}}
    }
}
