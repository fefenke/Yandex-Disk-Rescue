using System;
using System.Collections.Generic;
using System.IO;

namespace DiskRescue {
    public sealed partial class ApiEngine {
        static void ValidatePublicName(string name){if(String.IsNullOrWhiteSpace(name)||name=="."||name==".."||name.IndexOfAny(new[]{'/','\\'})>=0)throw new IOException("Invalid public resource name.");}
        void ValidatePublicChild(string folder,string path){
            string prefix=folder==inventoryRoot?"/":folder+"/";
            if(!path.StartsWith(prefix,StringComparison.Ordinal)||path.Length<=prefix.Length||path.Substring(prefix.Length).IndexOf('/')>=0)throw new IOException("Public listing contains a resource outside its folder.");
            LocalPath(settings.ApiFolder,path);
        }
        string ApiResourcePath(DiskFile file){return settings.Source==DownloadSource.PublicLink?(file.PublicRoot?"/":file.ApiPath??file.Path):file.Path;}
        void ValidateStoredResource(DiskFile file){
            if(file==null||String.IsNullOrEmpty(file.Path)||file.Length<0)throw new IOException("Saved API file identity is invalid. Refresh the list.");
            if(settings.Source==DownloadSource.Account){if(!file.Path.StartsWith("disk:/",StringComparison.Ordinal))throw new IOException("Saved account file identity is invalid. Refresh the list.");return;}
            if(file.PublicRoot){if(file.ApiPath!="/"||!file.Path.StartsWith("/",StringComparison.Ordinal))throw new IOException("Saved public root identity is invalid. Refresh the list.");ValidatePublicName(file.Path.Substring(1));}
            else if(file.ApiPath!=file.Path||!file.Path.StartsWith("/",StringComparison.Ordinal))throw new IOException("Saved public file identity is invalid. Refresh the list.");
            LocalPath(settings.ApiFolder,file.Path);CloudIntegrity.Validate(file.Md5,32);CloudIntegrity.Validate(file.Sha256,64);
        }
        bool SameResource(DiskFile file,IDictionary<string,object> remote){
            string path=Text(remote,"path");
            if(settings.Source!=DownloadSource.PublicLink)return path==file.Path;
            if(file.PublicRoot)return (path==""||path=="/")&&Text(remote,"name")==file.Path.Substring(1);
            return path==file.Path&&path==ApiResourcePath(file);
        }
    }
}
