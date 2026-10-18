using System;
using Microsoft.Win32;

namespace DiskRescue {
    public sealed class PrerequisiteStatus {
        public bool Windows, Architecture, Framework;
        public bool PlatformReady { get { return Windows && Architecture && Framework; } }
    }
    public static class Prerequisites {
        public static bool FrameworkSupported(int release) { return release >= 461308; }
        public static PrerequisiteStatus Check() {
            PrerequisiteStatus s=new PrerequisiteStatus();Version os=Environment.OSVersion.Version;
            s.Windows=os.Major>=10&&os.Build>=16299;s.Architecture=IntPtr.Size==4||Environment.Is64BitOperatingSystem;
            try {using(RegistryKey baseKey=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,Environment.Is64BitOperatingSystem?RegistryView.Registry64:RegistryView.Registry32))using(RegistryKey key=baseKey.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full")){s.Framework=key!=null&&FrameworkSupported(Convert.ToInt32(key.GetValue("Release",0)));}}catch{}
            return s;
        }
    }
}
