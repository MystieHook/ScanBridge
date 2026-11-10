using Microsoft.Win32;
using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace ScanBridge
{
    internal static class IntegrationSetup
    {
        internal static string DataDirectory => Path.GetDirectoryName(ProfileStore.SharedPath);
        internal static string InstallDirectory
        {
            get
            {
                using(var machine=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry32))
                using(var key=machine.OpenSubKey(@"SOFTWARE\ScanBridge"))
                {
                    var installed=key?.GetValue("InstallDir") as string;
                    if(!string.IsNullOrWhiteSpace(installed)&&Path.IsPathRooted(installed))return installed;
                }
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"ScanBridge");
            }
        }
        internal static string LinkagePath => Path.Combine(DataDirectory, "ScanBridge.ini");
        internal static string InstalledExe => Path.Combine(InstallDirectory, "ScanBridge.exe");
        internal const string ExtensionKey = @"SOFTWARE\PFU\ScanSnap Extension\ScanBridge_Desktop";

        internal static string BuildLinkage(Profile profile)
        {
            profile.Validate();
            int mode = profile.dpi == 150 ? 1 : profile.dpi == 200 ? 2 : profile.dpi == 300 ? 3 : 4;
            // Receive full-color JPEG pages, then apply host-side rotation/blank removal/review/PDF.
            // Home quality modes map to 150/200/300/600 dpi for color scanning.
            return "[Info]\r\nIFVersion=1\r\n[Launch]\r\nLabel=ScanBridge\r\nDescription=Shared computer scan profile\r\n" +
                "SupportFileFormat=2\r\nQuickMenu=0\r\nWithoutLaunch=0\r\nAlwaysAutoConnect=1\r\nAlwaysAutoConnectExe=ScanBridge.exe\r\n" +
                "[Application]\r\nAppSetting=0\r\nWait=0\r\nIgnoreScanButton=0\r\n" +
                "[Scanning]\r\nFormat=2\r\nColorMode=2\r\nScanMode=" + mode + "\r\nScanningSide=" + (profile.AcquireBothSides ? 0 : 1) +
                "\r\nBlankPageSkip=0\r\nRotation=0\r\nDeskew=0\r\nRename=0\r\nContinueScan=0\r\nPaperSize=0\r\n";
        }

        internal static void Install()
        {
            if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                throw new UnauthorizedAccessException("Setup must run as administrator.");
            Directory.CreateDirectory(InstallDirectory);
            Directory.CreateDirectory(DataDirectory);
            // Settings are writable by administrators/SYSTEM and readable by all authenticated users.
            var acl = new DirectorySecurity();
            acl.SetAccessRuleProtection(true, false);
            foreach (var sid in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
                acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), FileSystemRights.ReadAndExecute,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(DataDirectory).SetAccessControl(acl);
            string source = System.Reflection.Assembly.GetExecutingAssembly().Location;
            if (!string.Equals(source, InstalledExe, StringComparison.OrdinalIgnoreCase)) File.Copy(source, InstalledExe, true);
            var store = new ProfileStore(ProfileStore.SharedPath);
            var profile = store.Load();
            if (profile == null)
            {
                profile = new Profile { destination = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments), "ScanBridge") };
                Directory.CreateDirectory(profile.destination);
                // Output is shared by design; the administrator can later select a different destination.
                var outputAcl = new DirectoryInfo(profile.destination).GetAccessControl();
                outputAcl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), FileSystemRights.Modify,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                new DirectoryInfo(profile.destination).SetAccessControl(outputAcl);
                store.Save(profile);
            }
            File.WriteAllText(LinkagePath, BuildLinkage(profile), Encoding.Unicode);
            using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
            using (var key = machine.CreateSubKey(ExtensionKey))
            {
                key.SetValue("", InstalledExe); key.SetValue("Config", LinkagePath); key.SetValue("Path", InstallDirectory);
            }
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"))
                key.SetValue("ScanBridge", "\"" + InstalledExe + "\" --tray");
        }

        internal static bool IsRegistered()
        {
            try
            {
                using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
                using (var key = machine.OpenSubKey(ExtensionKey))
                    return key != null && string.Equals(key.GetValue("") as string, InstalledExe, StringComparison.OrdinalIgnoreCase) && File.Exists(InstalledExe);
            }
            catch { return false; }
        }
        internal static void SaveLinkage(Profile profile)
        {
            if (File.Exists(LinkagePath)) File.WriteAllText(LinkagePath, BuildLinkage(profile), Encoding.Unicode);
        }
    }
}
