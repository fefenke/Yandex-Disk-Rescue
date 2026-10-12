using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
namespace DiskRescue {
    public static class TokenVault {
        public static string Load(string directory){try{string path=Path.Combine(directory,"api-token.bin");if(!File.Exists(path))return "";return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path),null,DataProtectionScope.CurrentUser));}catch{return "";}}
        public static void Save(string directory,string token){Directory.CreateDirectory(directory);string path=Path.Combine(directory,"api-token.bin");if(String.IsNullOrEmpty(token)){if(File.Exists(path))File.Delete(path);return;}File.WriteAllBytes(path,ProtectedData.Protect(Encoding.UTF8.GetBytes(token),null,DataProtectionScope.CurrentUser));}
    }
}
