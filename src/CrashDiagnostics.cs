using System;
using System.IO;
using System.Text;

namespace DiskRescue {
    internal static class CrashDiagnostics {
        // Do not log exception messages: HTTP messages may contain signed URLs.
        internal static string Describe(Exception error){
            StringBuilder text=new StringBuilder();int depth=0;
            while(error!=null&&depth++<8){text.AppendLine(error.GetType().FullName+" (0x"+error.HResult.ToString("X8")+")");text.AppendLine(error.StackTrace??"(no stack)");error=error.InnerException;}
            return text.ToString();
        }
        internal static string Save(Exception error){
            string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","diagnostics");
            try{Directory.CreateDirectory(folder);string path=Path.Combine(folder,"last-error.txt");File.WriteAllText(path,DateTimeOffset.Now.ToString("o")+Environment.NewLine+"Process: "+(IntPtr.Size*8)+" bit"+Environment.NewLine+Describe(error),Encoding.UTF8);return path;}catch{return "";}
        }
    }
}
