using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace DiskRescue {
    internal static class DestinationPaths {
        const string Conflict="Local path conflicts with another file, directory, or download state. Existing data retained.";
        sealed class Node {
            public readonly List<DiskFile> Files=new List<DiskFile>(),Children=new List<DiskFile>(),Reserved=new List<DiskFile>();
        }
        internal static IEnumerable<string> Artifacts(string path){
            foreach(string suffix in new[]{".ydr-download",".ydr-map",".ydr-map.tmp",".ydr-part",".ydr-part.ydr-state",".ydr-part.ydr-state.tmp",".ydr-segments"})yield return path+suffix;
        }
        static string Alternate(string path,string remote){
            string suffix;using(SHA256 sha=SHA256.Create())suffix=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(remote))).Replace("-","").Substring(0,8);
            return Path.Combine(Path.GetDirectoryName(path),Path.GetFileNameWithoutExtension(path)+"_"+suffix+Path.GetExtension(path));
        }
        // Reuse only paths produced by this assignment policy. A cached absolute
        // path must never redirect a download outside the selected root.
        internal static Dictionary<string,string> Saved(IEnumerable<DiskFile> files,string root){
            var saved=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(DiskFile file in files){
                if(file==null||String.IsNullOrEmpty(file.Path)||String.IsNullOrEmpty(file.Destination))continue;
                string natural=ApiEngine.LocalPath(root,file.Path),path;
                try{path=Path.GetFullPath(file.Destination);}catch(ArgumentException){continue;}catch(NotSupportedException){continue;}catch(PathTooLongException){continue;}
                if(String.Equals(path,natural,StringComparison.OrdinalIgnoreCase)||String.Equals(path,Alternate(natural,file.Path),StringComparison.OrdinalIgnoreCase))saved[file.Path]=path;
            }
            return saved;
        }
        internal static Dictionary<string,string> Saved(ApiEngine.InventoryCache cache,string root){
            // Old complete inventories had final Destination values. During an
            // old interrupted scan they were provisional, so cannot prove ownership.
            if(cache.DestinationAssignments!=null)return Saved(cache.DestinationAssignments.Select(p=>new DiskFile{Path=p.Key,Destination=p.Value}),root);
            return cache.Complete?Saved(cache.Files,root):new Dictionary<string,string>(StringComparer.Ordinal);
        }
        static bool HasData(string path){return File.Exists(path)||Directory.Exists(path)||Artifacts(path).Any(p=>File.Exists(p)||Directory.Exists(p));}
        static void Block(DiskFile file){file.DestinationError=Conflict;file.Status="failed";file.Message=Conflict;file.Local=false;file.DownloadedBytes=null;}
        static Node At(Dictionary<string,Node> tree,string path){Node node;if(!tree.TryGetValue(path,out node))tree[path]=node=new Node();return node;}
        internal static void Assign(List<DiskFile> files,string root,Dictionary<string,string> saved){
            string fullRoot=Path.GetFullPath(root).TrimEnd('\\');
            var ordered=files.OrderBy(f=>f.Path,StringComparer.Ordinal).ToList();
            var natural=ordered.ToDictionary(f=>f,f=>ApiEngine.LocalPath(root,f.Path));
            var naturalPaths=new HashSet<string>(natural.Values,StringComparer.OrdinalIgnoreCase);
            var used=new HashSet<string>(saved.Values,StringComparer.OrdinalIgnoreCase);
            var pinned=new HashSet<DiskFile>();
            foreach(DiskFile file in ordered){
                file.DestinationError="";file.Destination=natural[file];string previous;
                if(saved.TryGetValue(file.Path,out previous)){file.Destination=previous;pinned.Add(file);used.Add(previous);}
            }
            // Without a saved owner, bytes at a case-colliding path must not be
            // attributed to whichever cloud entry happens to sort first.
            var ambiguous=new HashSet<DiskFile>();
            foreach(var group in ordered.GroupBy(f=>natural[f],StringComparer.OrdinalIgnoreCase)){
                if(group.Count()<2||group.Any(f=>pinned.Contains(f)))continue;
                if(group.Any(f=>HasData(natural[f])||HasData(Alternate(natural[f],f.Path))))foreach(DiskFile file in group)ambiguous.Add(file);
            }
            foreach(DiskFile file in ordered){
                string path=file.Destination;
                if(ambiguous.Contains(file)){Block(file);used.Add(path);}
                else if(!pinned.Contains(file)){
                    if(used.Contains(path)){
                        string alternative=Alternate(path,file.Path);
                        if(used.Contains(alternative)||naturalPaths.Contains(alternative)||HasData(alternative))Block(file);
                        else path=alternative;
                    }
                    file.Destination=path;used.Add(path);
                }
                file.Folder=Path.GetDirectoryName(file.Destination);
            }
            var tree=new Dictionary<string,Node>(StringComparer.OrdinalIgnoreCase);
            var present=new HashSet<string>(ordered.Select(f=>f.Path),StringComparer.Ordinal);
            // A cloud entry can disappear while its local file or partial state
            // remains. Reserve those bytes until the user resolves the conflict.
            var retained=saved.Where(p=>!present.Contains(p.Key)&&HasData(p.Value)).Select(p=>new DiskFile{Path=p.Key,Destination=p.Value});
            foreach(DiskFile file in ordered.Concat(retained)){
                At(tree,file.Destination).Files.Add(file);
                for(string parent=Path.GetDirectoryName(file.Destination);!String.IsNullOrEmpty(parent)&&!String.Equals(parent,fullRoot,StringComparison.OrdinalIgnoreCase);parent=Path.GetDirectoryName(parent)){
                    At(tree,parent).Children.Add(file);
                    if(File.Exists(parent))Block(file);
                }
                if(Directory.Exists(file.Destination))Block(file);
                foreach(string artifact in Artifacts(file.Destination)){
                    At(tree,artifact).Reserved.Add(file);
                    if(artifact.EndsWith(".ydr-segments",StringComparison.Ordinal)?File.Exists(artifact):Directory.Exists(artifact))Block(file);
                }
            }
            foreach(Node node in tree.Values){
                if(node.Files.Count>1||(node.Files.Count>0&&node.Children.Count>0))foreach(DiskFile file in node.Files.Concat(node.Children))Block(file);
                if(node.Reserved.Count>0&&(node.Files.Count>0||node.Children.Count>0))foreach(DiskFile file in node.Reserved.Concat(node.Files).Concat(node.Children))Block(file);
            }
        }
    }
}
