using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace ScanBridge
{
    internal static class ScanController
    {
        internal static async Task ScanAsync(Profile profile)
        {
            profile.Validate();
            if (!IntegrationSetup.IsRegistered()) throw new InvalidOperationException("Run Set up scanning once before scanning.");
            await HomeMonitor.EnsureRunningAsync();
            // Destination availability is checked before feeding paper. The receiver also retains raw pages on delivery failure.
            Directory.CreateDirectory(profile.destination);
            string check = Path.Combine(profile.destination, ".scanbridge-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (File.Create(check)) { } File.Delete(check);
            await Task.Run(() =>
            {
                bool created;
                var security = new MutexSecurity();
                security.AddAccessRule(new MutexAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), MutexRights.Synchronize | MutexRights.Modify, AccessControlType.Allow));
                using (var mutex = new Mutex(false, @"Global\ScanBridge_ScanAcquisition", out created, security))
                {
                    bool acquired;
                    try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) throw new InvalidOperationException("Another Windows session is scanning. Wait for it to finish.");
                    bool reserved = false;
                    try
                    {
                        ScanSnapCommands.RequireSuccess(ScanSnapCommands.Reserve(), "Could not select ScanBridge");
                        reserved = true;
                        ScanSnapCommands.RequireSuccess(ScanSnapCommands.StartScan(profile.AcquireBothSides), "Scan did not complete");
                    }
                    finally
                    {
                        try { if (reserved) ScanSnapCommands.Release(); }
                        finally { mutex.ReleaseMutex(); }
                    }
                }
            });
        }
        internal static string CheckRegistration()
        {
            if (!IntegrationSetup.IsRegistered()) return "ScanBridge extension is not installed.";
            ScanSnapCommands.RequireSuccess(ScanSnapCommands.Reserve(), "Registration check");
            try { return "Home accepted ScanBridge registration. No scan was started."; }
            finally { ScanSnapCommands.RequireSuccess(ScanSnapCommands.Release(), "Releasing test control"); }
        }
    }
}
