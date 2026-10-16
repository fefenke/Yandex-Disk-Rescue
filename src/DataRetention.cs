using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
namespace DiskRescue {
    internal static class DataRetention {
        public const int ReportLimit=50;
        static readonly string[] Files={"files.csv","categories.csv","folders.csv","summary.json"};
        static bool Generated(string path){
            string name=Path.GetFileName(path);DateTime date;
            return Regex.IsMatch(name,@"^\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}_[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$")&&DateTime.TryParseExact(name.Substring(0,19),"yyyy-MM-dd_HH-mm-ss",CultureInfo.InvariantCulture,DateTimeStyles.None,out date);
        }
        static bool MatchesReport(string recorded,string directory){
            if(String.IsNullOrEmpty(recorded)||!Path.IsPathRooted(recorded))return false;
            string original=Path.GetFullPath(recorded).TrimEnd(Path.DirectorySeparatorChar),actual=Path.GetFullPath(directory);
            if(String.Equals(original,actual,StringComparison.OrdinalIgnoreCase))return true;
            string oldParent=Path.GetDirectoryName(original),newParent=Path.GetDirectoryName(actual);
            return String.Equals(Path.GetFileName(original),Path.GetFileName(actual),StringComparison.OrdinalIgnoreCase)
                &&String.Equals(Path.GetFileName(oldParent),"reports",StringComparison.OrdinalIgnoreCase)
                &&String.Equals(Path.GetFileName(Path.GetDirectoryName(oldParent)),Path.GetFileName(Path.GetDirectoryName(newParent)),StringComparison.OrdinalIgnoreCase);
        }
        public static void PruneReports(string currentDirectory){
            try{
                string current=Path.GetFullPath(currentDirectory).TrimEnd(Path.DirectorySeparatorChar),parent=Path.GetDirectoryName(current);
                if(!String.Equals(Path.GetFileName(parent),"reports",StringComparison.OrdinalIgnoreCase))return;
                DurablePartial.SafePath(parent);
                var reports=Directory.GetDirectories(parent).Where(Generated).OrderByDescending(x=>Path.GetFileName(x),StringComparer.Ordinal).ToList();
                foreach(string path in reports.Skip(ReportLimit)){
                    if(String.Equals(path,current,StringComparison.OrdinalIgnoreCase))continue;
                    try{PruneOne(path);}catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(InvalidOperationException){}
                }
            }catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}
        }
        static void PruneOne(string directory){
            DurablePartial.SafePath(directory);
            string[] entries=Directory.GetFileSystemEntries(directory);
            if(entries.Length!=Files.Length||entries.Any(x=>!Files.Contains(Path.GetFileName(x),StringComparer.Ordinal)))return;
            foreach(string path in entries){DurablePartial.SafePath(path);if((File.GetAttributes(path)&(FileAttributes.Directory|FileAttributes.ReadOnly))!=0)return;}
            string summary=Path.Combine(directory,"summary.json");if(new FileInfo(summary).Length>16L*1024*1024)return;
            var values=new JavaScriptSerializer{MaxJsonLength=16*1024*1024}.Deserialize<Dictionary<string,object>>(File.ReadAllText(summary));object report;
            if(values==null||!values.TryGetValue("Report",out report)||!(report is string)||!MatchesReport((string)report,directory))return;
            var held=new List<FileStream>();
            try{
                // Acquire every file before deleting any, so a busy report stays complete.
                foreach(string path in entries)held.Add(new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.Delete));
                foreach(string path in entries)File.Delete(path);
            }finally{foreach(var stream in held)stream.Dispose();}
            Directory.Delete(directory,false);
        }
    }
}
