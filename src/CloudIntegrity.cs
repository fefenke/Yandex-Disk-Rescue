using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace DiskRescue {
    internal sealed class IntegrityResult {
        public bool Match;public string Kind,Algorithm,Expected,Actual;
    }
    internal static class CloudIntegrity {
        public static bool HasHash(DiskFile f){return !String.IsNullOrEmpty(f.Md5)||!String.IsNullOrEmpty(f.Sha256);}
        public static string Version(DiskFile f){return f.Path+"\n"+f.Modified+"\n"+f.Length+"\n"+(String.IsNullOrEmpty(f.Md5)?f.Sha256??"":"");}
        internal static bool LegacyVersion(string saved,string current){return saved!=null&&current.EndsWith("\n",StringComparison.Ordinal)&&saved.StartsWith(current,StringComparison.Ordinal)&&saved.Length==current.Length+64&&System.Linq.Enumerable.All(saved.Substring(current.Length),Uri.IsHexDigit);}
        public static void Validate(string value,int length){if(!String.IsNullOrEmpty(value)&&(value.Length!=length||System.Linq.Enumerable.Any(value,c=>!Uri.IsHexDigit(c))))throw new IOException("Invalid cloud checksum metadata.");}
        public static IntegrityResult Check(string path,DiskFile f,CancellationToken token,Action<long,long> progress=null){
            token.ThrowIfCancellationRequested();Validate(f.Md5,32);Validate(f.Sha256,64);
            var result=new IntegrityResult{Kind="missing",Algorithm=!String.IsNullOrEmpty(f.Md5)?"md5":!String.IsNullOrEmpty(f.Sha256)?"sha256":"",Expected=!String.IsNullOrEmpty(f.Md5)?f.Md5:f.Sha256,Actual=""};
            if(!File.Exists(path))return result;
            if(new FileInfo(path).Length!=f.Length){result.Kind="size_mismatch";return result;}
            if(result.Algorithm==""){result.Kind="size_only";result.Match=true;return result;}
            using(HashAlgorithm hash=result.Algorithm=="md5"?(HashAlgorithm)MD5.Create():SHA256.Create())using(FileStream input=File.OpenRead(path)){
                byte[] buffer=new byte[1024*1024];long done=0;int n;if(progress!=null)progress(0,f.Length);
                while(true){token.ThrowIfCancellationRequested();n=input.Read(buffer,0,buffer.Length);if(n==0)break;hash.TransformBlock(buffer,0,n,buffer,0);done+=n;if(progress!=null)progress(done,f.Length);}
                hash.TransformFinalBlock(new byte[0],0,0);result.Actual=BitConverter.ToString(hash.Hash).Replace("-","").ToLowerInvariant();
            }
            result.Match=String.Equals(result.Actual,result.Expected,StringComparison.OrdinalIgnoreCase);result.Kind=result.Match?result.Algorithm+"_verified":"hash_mismatch";return result;
        }
        public static bool CheckAndRecord(string path,DiskFile f,CancellationToken token,Action<long,long> progress=null){var r=Check(path,f,token,progress);f.Verification=r.Kind;f.ActualHash=r.Actual;return r.Match;}
    }
}
