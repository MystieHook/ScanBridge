using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Text;
using System.Collections.Generic;

namespace ScanBridge
{
    // Start only the tray/scanner monitor, never PfuSshMain.exe or a connection wizard.
    internal static class HomeMonitor
    {
        private static readonly object sync = new object();
        private static Task startup;
        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr FindWindow(string className, string title);
        private delegate bool WindowVisitor(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumWindows(WindowVisitor visitor, IntPtr parameter);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window,StringBuilder text,int size);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window,int command);
        internal static bool IsReady => FindWindow("ScanSnap Manager MainWndClass", null) != IntPtr.Zero;

        internal static Task EnsureRunningAsync()
        {
            if (IsReady) return Task.CompletedTask;
            lock (sync)
            {
                if (IsReady) return Task.CompletedTask;
                if (startup == null || startup.IsCompleted)
                    startup = WaitForReadyAsync(() => { SuppressHomeWindows(); return IsReady; }, IsStarting, StartHidden,
                        () => Task.Delay(250), 240);
                // All concurrent callers await one attempt, not a queue of minute-long retries.
                return startup;
            }
        }

        internal static void SuppressHomeWindows()
        {
            // Hide only the known library/scan windows. Never click through or dismiss vendor error dialogs.
            var ids = new HashSet<uint>();
            int session = Process.GetCurrentProcess().SessionId;
            foreach (var process in Process.GetProcessesByName("PfuSshMain"))
            using (process)
                try { if (process.SessionId == session) ids.Add((uint)process.Id); }
                catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            if (ids.Count == 0) return;
            EnumWindows((window, parameter) =>
            {
                uint process; GetWindowThreadProcessId(window,out process);
                if (!ids.Contains(process) || !IsWindowVisible(window)) return true;
                var title = new StringBuilder(512); GetWindowText(window,title,title.Capacity);
                if (title.ToString() == "ScanSnap Home" || title.ToString() == "ScanSnap Home - Scan")
                    ShowWindowAsync(window,0);
                return true;
            },IntPtr.Zero);
        }

        internal static async Task WaitForReadyAsync(Func<bool> ready, Func<bool> starting,
            Action start, Func<Task> delay, int attempts)
        {
            if (ready()) return;
            // A monitor already starting in this session must not be launched again.
            if (!starting()) start();
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                if (ready()) return;
                await delay();
            }
            if (ready()) return;
            throw new InvalidOperationException("The scanner connection did not become ready automatically. ScanBridge will keep trying in the background. Check that ScanSnap Home is installed and the scanner is connected.");
        }

        private static bool IsStarting()
        {
            int session = Process.GetCurrentProcess().SessionId;
            foreach (var process in Process.GetProcessesByName("PfuSsMon"))
            using (process)
            {
                try { if (process.SessionId == session && !process.HasExited) return true; }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
            return false;
        }

        private static void StartHidden()
        {
            string folder = FindFolder();
            if (folder == null) throw new FileNotFoundException("ScanSnap Home's scanner monitor is not installed. Install ScanSnap Home once to provide the scanner driver.");
            var info = new ProcessStartInfo(Path.Combine(folder, "PfuSsMon.exe"))
            {
                WorkingDirectory = folder,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            // Inherit this interactive user's token. Never elevate the vendor monitor.
            using (var process = Process.Start(info)) { }
        }

        private static string FindFolder()
        {
            foreach (var baseFolder in new[] {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetEnvironmentVariable("ProgramW6432") })
            {
                if (string.IsNullOrEmpty(baseFolder)) continue;
                string folder = Path.Combine(baseFolder, @"PFU\ScanSnap\Home");
                if (File.Exists(Path.Combine(folder, "PfuSsMon.exe"))) return folder;
            }
            return null;
        }
    }
}
