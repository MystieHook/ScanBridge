using System;
using System.IO;
using System.Runtime.InteropServices;

// Public Ricoh Interface Guide P2WW-3030-12ENZ0, pp. 137-138.
// This only queries device information. It does not reserve control or start a scan.
internal static class HomeConnectionProbe
{
    [DllImport("user32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    static extern IntPtr FindWindow(string className, string title);
    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wParam,
        ref CopyData data, uint flags, uint timeout, out UIntPtr result);
    [StructLayout(LayoutKind.Sequential)]
    struct CopyData { public IntPtr Id; public int Length; public IntPtr Data; }
    [StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Ansi)]
    struct GetInfo
    {
        public uint Mode;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string FileName;
    }
    static int Main(string[] args)
    {
        string output = args.Length > 0 ? Path.GetFullPath(args[0]) : @"C:\Scansnap\home-probe.ini";
        if (output.Length >= 260) throw new ArgumentException("Output path exceeds legacy interface limit.");
        if (File.Exists(output)) throw new IOException("Choose a new output file; probe will not overwrite existing files.");
        IntPtr window = FindWindow("ScanSnap Manager MainWndClass", null);
        Console.WriteLine("Home interface window found: " + (window != IntPtr.Zero));
        if (window == IntPtr.Zero) return 2;
        var request = new GetInfo { Mode = 0, FileName = output };
        int size = Marshal.SizeOf(typeof(GetInfo));
        Console.WriteLine("Request bytes: " + size);
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(request, buffer, false);
            var data = new CopyData { Id = new IntPtr(5), Length = size, Data = buffer };
            UIntPtr result;
            IntPtr sent = SendMessageTimeout(window, 0x004A, IntPtr.Zero, ref data, 0x0002, 5000, out result);
            Console.WriteLine("Message delivered: " + (sent != IntPtr.Zero));
            if (sent == IntPtr.Zero) { Console.WriteLine("Win32 error: " + Marshal.GetLastWin32Error()); return 3; }
            Console.WriteLine("Vendor result: " + result.ToUInt64());
            Console.WriteLine("Device information file created: " + File.Exists(output));
            // Device identifiers stay in the local INI; do not dump them to console.
            if (File.Exists(output))
                foreach (string line in File.ReadAllLines(output))
                    if (line.StartsWith("ScannerName=", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("ScannerCount=", StringComparison.OrdinalIgnoreCase)) Console.WriteLine(line);
            return result.ToUInt64() == 0 && File.Exists(output) ? 0 : 4;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
}
