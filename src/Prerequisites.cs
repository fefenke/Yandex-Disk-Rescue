using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DiskRescue {
    public sealed class PrerequisiteStatus {
        public bool Windows, Architecture, Framework, CloudFiles, ClientInstalled, ClientRunning;
        public string ClientPath = "";
        public bool PlatformReady { get { return Windows && Architecture && Framework && CloudFiles; } }
    }
    public static class Prerequisites {
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr LoadLibrary(string name);
        [DllImport("kernel32.dll",CharSet=CharSet.Ansi)]static extern IntPtr GetProcAddress(IntPtr module,string name);
        [DllImport("kernel32.dll")]static extern bool FreeLibrary(IntPtr module);
        public static bool FrameworkSupported(int release) { return release >= 461308; }
        public static PrerequisiteStatus Check() {
            PrerequisiteStatus s=new PrerequisiteStatus();Version os=Environment.OSVersion.Version;
            s.Windows=os.Major>=10&&os.Build>=16299;s.Architecture=IntPtr.Size==4||Environment.Is64BitOperatingSystem;
            try {using(RegistryKey baseKey=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,Environment.Is64BitOperatingSystem?RegistryView.Registry64:RegistryView.Registry32))using(RegistryKey key=baseKey.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full")){s.Framework=key!=null&&FrameworkSupported(Convert.ToInt32(key.GetValue("Release",0)));}}catch{}
            IntPtr library=LoadLibrary(Path.Combine(Environment.SystemDirectory,"cldapi.dll"));
            if(library!=IntPtr.Zero){s.CloudFiles=GetProcAddress(library,"CfHydratePlaceholder")!=IntPtr.Zero&&GetProcAddress(library,"CfGetPlaceholderInfo")!=IntPtr.Zero;FreeLibrary(library);}
            foreach(string name in new[]{"YandexDisk4","YandexDisk2"})foreach(Process process in Process.GetProcessesByName(name)){
                try{s.ClientRunning=true;s.ClientInstalled=true;s.ClientPath=process.MainModule.FileName;}catch{}finally{process.Dispose();}
            }
            if(!s.ClientInstalled){
                List<string> candidates=new List<string>();
                foreach(string root in new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}){
                    candidates.Add(Path.Combine(root,"Yandex","YandexDisk4","YandexDisk4.exe"));candidates.Add(Path.Combine(root,"Yandex","YandexDisk2","YandexDisk2.exe"));
                }
                string oldRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Yandex","YandexDisk2");
                try{if(Directory.Exists(oldRoot))foreach(string version in Directory.GetDirectories(oldRoot))candidates.Add(Path.Combine(version,"YandexDisk2.exe"));}catch{}
                foreach(string path in candidates)if(File.Exists(path)){s.ClientInstalled=true;s.ClientPath=path;break;}
            }
            return s;
        }
    }
}
