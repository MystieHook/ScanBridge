using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace ScanBridge
{
    internal static class ScanSnapCommands
    {
        internal const string AppKey = "ScanBridge_Desktop";
        [DllImport("user32.dll", CharSet = CharSet.Ansi)] static extern IntPtr FindWindow(string name, string title);
        [DllImport("user32.dll",SetLastError=true)] static extern bool PostMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, ref CopyData data, uint flags, uint timeout, out UIntPtr result);
        [StructLayout(LayoutKind.Sequential)] struct CopyData { public IntPtr Id; public int Length; public IntPtr Data; }
        [StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Ansi)]
        struct Notify { public uint Mode; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 255)] public string AppName; }
        [StructLayout(LayoutKind.Sequential, Pack = 4)] struct Scan { public uint Mode; public int Side; }
        [StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Ansi)]
        struct AutoConnect
        {
            public uint Mode;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 255)] public string AppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 255)] public string ExeName;
        }
        internal static ulong Reserve() => Send(2, new Notify { Mode = 0, AppName = AppKey }, 5000);
        internal static ulong Release() => Send(2, new Notify { Mode = 1, AppName = AppKey }, 5000);
        internal static ulong StartScan(bool duplex) => Send(3, new Scan { Mode = 0, Side = duplex ? 0 : 1 }, 600000);
        internal static ulong NotifyRunning(bool running) => Send(1, new AutoConnect { Mode = running ? 0u : 1u, AppName = AppKey, ExeName = "ScanBridge.exe" }, 5000);
        internal static void RestartMonitor()
        {
            string executable=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),@"PFU\ScanSnap\Home\PfuSsMon.exe");
            if(!File.Exists(executable))throw new FileNotFoundException("ScanSnap Home's scanner monitor was not found.",executable);
            IntPtr window=FindWindow("ScanSnap Manager MainWndClass",null);
            if(window!=IntPtr.Zero)
            {
                if(!PostMessage(window,0x0010,IntPtr.Zero,IntPtr.Zero))throw new Win32Exception(Marshal.GetLastWin32Error());
                var deadline=DateTime.UtcNow.AddSeconds(10);
                while(FindWindow("ScanSnap Manager MainWndClass",null)!=IntPtr.Zero && DateTime.UtcNow<deadline)System.Threading.Thread.Sleep(200);
                if(FindWindow("ScanSnap Manager MainWndClass",null)!=IntPtr.Zero)throw new InvalidOperationException("Home is busy or has an open window. Close its settings and restart Home normally. No process was forcibly stopped.");
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(executable){UseShellExecute=true,WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden});
            var ready=DateTime.UtcNow.AddSeconds(45);
            while(FindWindow("ScanSnap Manager MainWndClass",null)==IntPtr.Zero && DateTime.UtcNow<ready)System.Threading.Thread.Sleep(250);
            if(FindWindow("ScanSnap Manager MainWndClass",null)==IntPtr.Zero)throw new InvalidOperationException("Home has not finished starting. Try Refresh in a moment.");
        }
        static ulong Send<T>(int id, T value, uint timeout)
        {
            IntPtr window = FindWindow("ScanSnap Manager MainWndClass", null);
            if (window == IntPtr.Zero) throw new InvalidOperationException("The scanner connection stopped. ScanBridge will reconnect automatically; retry the scan when it is ready.");
            int size = Marshal.SizeOf(typeof(T));
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(value, buffer, false);
                var data = new CopyData { Id = new IntPtr(id), Length = size, Data = buffer };
                UIntPtr result;
                if (SendMessageTimeout(window, 0x004A, IntPtr.Zero, ref data, 2, timeout, out result) == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Home did not complete the request. Check that both apps run under the same user. A timed-out scan may still be finishing; do not start another scan yet.");
                return result.ToUInt64();
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        internal static void RequireSuccess(ulong result, string action)
        {
            if (result == 0) return;
            string explanation;
            switch (result)
            {
                case 1: explanation = "Home is busy or a settings window is open."; break;
                case 2: explanation = "Home has not recognized ScanBridge's registration. Restart Home after setup."; break;
                case 3: explanation = "Another application already controls the scanner."; break;
                case 4: explanation = "Scanner control was already released."; break;
                case 5: explanation = "Home rejected the interface parameters."; break;
                case 6: explanation = "The scanner is disconnected, empty, or unavailable."; break;
                default: explanation = "Vendor status " + result + "."; break;
            }
            throw new InvalidOperationException(action + ": " + explanation);
        }
    }
}
