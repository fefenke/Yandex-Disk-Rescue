using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Collections.Generic;

public static class YandexCloudNative {
    [DllImport("ntdll.dll",ExactSpelling=true)]static extern sbyte RtlSetThreadPlaceholderCompatibilityMode(sbyte mode);
    public static void EnablePlaceholdersForCurrentThread(){
        // Non-system executables can receive disguised cloud reparse points.
        // Opt in on the worker thread before directory enumeration or probing.
        if(RtlSetThreadPlaceholderCompatibilityMode(2)<0)throw new InvalidOperationException("Could not expose Windows Cloud Files placeholders to the application.");
    }
    [StructLayout(LayoutKind.Sequential)] struct AttributeTag { public uint Attributes, Tag; }
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError=true)]
    static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int infoClass, out AttributeTag info, uint size);
    [DllImport("cldapi.dll")] static extern uint CfGetPlaceholderStateFromAttributeTag(uint attributes, uint tag);
    [DllImport("cldapi.dll")] static extern int CfHydratePlaceholder(SafeFileHandle file, long offset, long length, uint flags, IntPtr overlapped);
    [DllImport("cldapi.dll")] static extern int CfGetPlaceholderInfo(SafeFileHandle file, int infoClass, IntPtr buffer, uint size, out uint returned);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool GetOverlappedResult(SafeFileHandle file, IntPtr overlapped, out uint transferred, bool wait);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool CancelIoEx(SafeFileHandle file, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr CreateEvent(IntPtr security, bool manualReset, bool initialState, string name);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [StructLayout(LayoutKind.Sequential)] struct Overlapped {
        public UIntPtr Internal, InternalHigh;
        public uint Offset, OffsetHigh;
        public IntPtr Event;
    }
    // Keep unmanaged OVERLAPPED memory alive if the provider never acknowledges
    // cancellation. Do not free it while Windows might still be using it.
    static readonly List<Operation> Unfinished = new List<Operation>();

    public static long HydratedBytes(string path) {
        EnablePlaceholdersForCurrentThread();
        using(SafeFileHandle handle=CreateFile(path,0x80,7,IntPtr.Zero,3,0x00200000|0x02000000,IntPtr.Zero)) {
            if(handle.IsInvalid)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            IntPtr buffer=Marshal.AllocHGlobal(65536);
            try { uint returned;int result=CfGetPlaceholderInfo(handle,1,buffer,65536,out returned);if(result<0)Marshal.ThrowExceptionForHR(result);if(returned<8)throw new IOException("Missing hydration information.");return Marshal.ReadInt64(buffer); }
            finally { Marshal.FreeHGlobal(buffer); }
        }
    }
    public sealed class ProbeResult {
        public string Path; public uint Attributes, Tag, State;
        public bool Supported { get { return State != 0xffffffff && (State & 1) != 0; } }
    }
    public static ProbeResult Probe(string path) {
        EnablePlaceholdersForCurrentThread();
        // Metadata only: opening a reparse point does not request file content.
        using(var file=CreateFile(path,0x80,7,IntPtr.Zero,3,0x00200000|0x02000000,IntPtr.Zero)) {
            if(file.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            AttributeTag info;
            if(!GetFileInformationByHandleEx(file,9,out info,8)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return new ProbeResult { Path=path, Attributes=info.Attributes, Tag=info.Tag, State=CfGetPlaceholderStateFromAttributeTag(info.Attributes,info.Tag) };
        }
    }
    public sealed class Operation : IDisposable {
        SafeFileHandle file;
        IntPtr overlapped, signal, info;
        bool pending, disposed;
        public string Path { get; private set; }
        public bool Completed { get; private set; }
        public bool CancelRequested { get; private set; }
        public int ErrorCode { get; private set; }
        public string Status { get; private set; }
        public long ProgressBytes { get; private set; }
        public DateTime LastProgressUtc { get; private set; }
        public DateTime CancelRequestedUtc { get; private set; }
        internal Operation(string path) {
            Path=path;
            try {
                var probe=Probe(path);
                if(!probe.Supported) throw new NotSupportedException("Not a Cloud Files placeholder: "+path);
                file=CreateFile(path,0x80,7,IntPtr.Zero,3,0x00200000|0x40000000,IntPtr.Zero);
                if(file.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                signal=CreateEvent(IntPtr.Zero,true,false,null);
                if(signal==IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                overlapped=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Overlapped)));
                Marshal.StructureToPtr(new Overlapped { Event=signal },overlapped,false);
                info=Marshal.AllocHGlobal(65536);
                ProgressBytes=ReadProgress();
                LastProgressUtc=DateTime.UtcNow;
                int result=CfHydratePlaceholder(file,0,-1,0,overlapped);
                if(result==unchecked((int)0x800703e5)) { pending=true; Status="pending"; }
                else { Finish(result); }
            } catch { Dispose(); throw; }
        }
        long ReadProgress() {
            uint returned;
            int result=CfGetPlaceholderInfo(file,1,info,65536,out returned);
            if(result<0) Marshal.ThrowExceptionForHR(result);
            if(returned<8) throw new IOException("Cloud placeholder metadata is incomplete.");
            return Marshal.ReadInt64(info);
        }
        void Finish(int error) {
            pending=false; Completed=true; ErrorCode=error;
            Status=error==0 ? "success" : error==unchecked((int)0x800703e3) ? "cancelled" : "failed";
        }
        public void Poll() {
            if(Completed) return;
            uint transferred;
            if(GetOverlappedResult(file,overlapped,out transferred,false)) { Finish(0); return; }
            int error=Marshal.GetLastWin32Error();
            if(error!=996) { Finish(unchecked((int)(0x80070000u|(uint)error))); return; }
            long bytes=ReadProgress();
            if(bytes>ProgressBytes) { ProgressBytes=bytes; LastProgressUtc=DateTime.UtcNow; }
        }
        public void RequestCancel() {
            if(Completed || CancelRequested) return;
            bool accepted=CancelIoEx(file,overlapped);
            int error=accepted ? 0 : Marshal.GetLastWin32Error();
            if(!accepted && error!=1168) throw new System.ComponentModel.Win32Exception(error);
            CancelRequested=true; CancelRequestedUtc=DateTime.UtcNow;
            Poll();
        }
        public void Dispose() {
            if(disposed) return;
            if(pending) {
                try { RequestCancel(); Poll(); } catch {}
                if(pending) {
                    lock(Unfinished) { if(!Unfinished.Contains(this)) Unfinished.Add(this); }
                    return;
                }
            }
            disposed=true;
            if(file!=null) file.Dispose();
            if(overlapped!=IntPtr.Zero) Marshal.FreeHGlobal(overlapped);
            if(info!=IntPtr.Zero) Marshal.FreeHGlobal(info);
            if(signal!=IntPtr.Zero) CloseHandle(signal);
        }
    }
    public static Operation Start(string path) { return new Operation(path); }
}
