using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ScanBridge
{
    public sealed class ConnectionReport
    {
        public bool interfaceFound { get; set; }
        public bool querySucceeded { get; set; }
        public int scannerCount { get; set; }
        public string[] scannerNames { get; set; } = new string[0];
        public string detail { get; set; }
    }

    // Public Ricoh Interface Guide P2WW-3030-12ENZ0, pp. 137-138.
    // This legacy interface has been verified for GetScannerInfo on Home 4.1.0.5.
    // It does not establish that registration, scan settings or button routing work.
    public static class ScanSnapHomeConnection
    {
        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wParam,
            ref CopyData data, uint flags, uint timeout, out UIntPtr result);
        [StructLayout(LayoutKind.Sequential)]
        private struct CopyData { public IntPtr Id; public int Length; public IntPtr Data; }
        [StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Ansi)]
        private struct GetInfo
        {
            public uint Mode;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string FileName;
        }

        public static ConnectionReport Query()
        {
            var report = new ConnectionReport();
            IntPtr window = FindWindow("ScanSnap Manager MainWndClass", null);
            report.interfaceFound = window != IntPtr.Zero;
            if (!report.interfaceFound) { report.detail = "ScanSnap Home's scanner monitor is not running in this session."; return report; }
            string output = Path.Combine(Path.GetTempPath(), "ScanBridge-" + Guid.NewGuid().ToString("N") + ".ini");
            // Legacy API uses MAX_PATH ANSI, not Unicode. Fail clearly rather than truncate.
            if (Encoding.Default.GetByteCount(output) >= 260 || Encoding.Default.GetString(Encoding.Default.GetBytes(output)) != output)
            { report.detail = "The temporary folder cannot be represented by the legacy scanner interface."; return report; }
            IntPtr buffer = IntPtr.Zero;
            try
            {
                var request = new GetInfo { Mode = 0, FileName = output };
                int size = Marshal.SizeOf(typeof(GetInfo));
                buffer = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(request, buffer, false);
                var data = new CopyData { Id = new IntPtr(5), Length = size, Data = buffer };
                UIntPtr result;
                IntPtr delivered = SendMessageTimeout(window, 0x004A, IntPtr.Zero, ref data, 0x0002, 5000, out result);
                if (delivered == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    report.detail = error == 5 ? "Windows blocked communication with Home. Run both apps under the same Windows user and elevation level." : "Home did not answer: " + new Win32Exception(error).Message;
                    return report;
                }
                if (result.ToUInt64() != 0)
                {
                    switch (result.ToUInt64())
                    {
                        case 1: report.detail = "Home is busy scanning or displaying a settings window."; break;
                        case 5: report.detail = "Home rejected the legacy query parameters."; break;
                        case 6: report.detail = "Home reports that scanning is unavailable. Check scanner power and USB connection."; break;
                        default: report.detail = "Home returned status " + result.ToUInt64() + "."; break;
                    }
                    return report;
                }
                if (!File.Exists(output)) { report.detail = "Home accepted the query but did not produce device information."; return report; }
                // Keep device serial numbers and counters out of exported diagnostics.
                var values = ReadIni(File.ReadAllLines(output, Encoding.Default));
                int count;
                if (!values.ContainsKey("INFO/ScannerCount") || !int.TryParse(values["INFO/ScannerCount"], out count) || count < 0 || count > 100)
                { report.detail = "Home returned an unexpected device-information file."; return report; }
                report.scannerCount = count;
                report.scannerNames = values.Where(p => p.Key.StartsWith("SCANNER", StringComparison.OrdinalIgnoreCase) && p.Key.EndsWith("/ScannerName", StringComparison.OrdinalIgnoreCase)).Select(p => p.Value.Trim()).ToArray();
                report.querySucceeded = true;
                report.detail = count > 0 ? "Live scanner query succeeded. Scan acquisition and button routing still need validation." : "Home reports no connected scanners.";
                return report;
            }
            catch (Exception ex) { report.detail = "Connection query failed: " + ex.Message; return report; }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                // Only remove the unique diagnostic file created for this request.
                try { if (File.Exists(output)) File.Delete(output); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        internal static Dictionary<string, string> ReadIni(IEnumerable<string> lines)
        {
            string section = "";
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in lines)
            {
                string line = raw.Trim().TrimStart('\uFEFF');
                if (line.StartsWith(";") || line.Length == 0) continue;
                if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2); continue; }
                int separator = line.IndexOf('=');
                if (separator > 0) values[section + "/" + line.Substring(0, separator).Trim()] = line.Substring(separator + 1).Trim();
            }
            return values;
        }
    }
}
