using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

// Installs, uninstalls and reinstalls ONLY the ScanBridge product code below.
internal static class TestMsi
{
    const string Product="{BD693DF7-64AA-4C43-A056-48EA5550A684}";
    static string folder;
    static StringBuilder report=new StringBuilder();
    static int Main(string[] args)
    {
        if(args.Length!=2)return 2;
        string package=Path.GetFullPath(args[0]);folder=Path.GetFullPath(args[1]);Directory.CreateDirectory(folder);
        string configuration=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),@"ScanBridge\profiles.json");
        string before=Hash(configuration);bool removed=false;
        try
        {
            Execute("/i \""+package+"\"", "install");CheckInstalled();
            Assert(Hash(configuration)==before,"Existing shared profile preserved after install");
            Execute("/x "+Product,"uninstall");removed=true;
            using(var machine=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry32))
            {
                using(var key=machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\"+Product))Assert(key==null,"Control Panel registration removed");
                using(var key=machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"))Assert(key==null||key.GetValue("ScanBridge")==null,"Startup registration removed");
                using(var key=machine.OpenSubKey(@"SOFTWARE\PFU\ScanSnap Extension\ScanBridge_Desktop"))Assert(key==null||key.GetValue("")==null,"Scanner application registration removed");
            }
            string app=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),@"ScanBridge\ScanBridge.exe");
            Assert(!File.Exists(app),"MSI-owned executable removed");
            Assert(Hash(configuration)==before,"Shared profile preserved after uninstall");
            Execute("/i \""+package+"\"","reinstall");removed=false;CheckInstalled();
            Assert(Hash(configuration)==before,"Shared profile preserved after reinstall");
            report.AppendLine("PASS: install -> uninstall -> reinstall. No scan was started.");return 0;
        }
        catch(Exception ex){report.AppendLine("FAIL: "+ex);return 1;}
        finally
        {
            if(removed)try{Execute("/i \""+package+"\"","restore-after-failure");}catch(Exception ex){report.AppendLine("RESTORE NEEDS ATTENTION: "+ex.Message);}
            File.WriteAllText(Path.Combine(folder,"result.txt"),report.ToString());
        }
    }
    static string Hash(string path)
    {if(!File.Exists(path))return "missing";using(var hash=SHA256.Create())return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path)));}
    static void Assert(bool value,string label){if(!value)throw new Exception(label);report.AppendLine("OK: "+label);}
    static void Execute(string operation,string stage)
    {
        var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"msiexec.exe"),operation+" /qn /norestart /L*v \""+Path.Combine(folder,stage+".log")+"\"")
        {UseShellExecute=false,CreateNoWindow=true};
        using(var process=Process.Start(info))
        {process.WaitForExit();report.AppendLine(stage+" exit: "+process.ExitCode);if(process.ExitCode!=0&&process.ExitCode!=3010)throw new Exception(stage+" failed; inspect "+stage+".log");}
    }
    static void CheckInstalled()
    {
        using(var machine=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry32))
        {
            using(var key=machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\"+Product))
            {Assert(key!=null&&key.GetValue("DisplayName") as string=="ScanBridge","Control Panel / Installed Apps entry exists");Assert(key.GetValue("DisplayVersion") as string=="0.5.2","Installed version is 0.5.2");Assert(!string.IsNullOrWhiteSpace(key.GetValue("DisplayIcon") as string),"Control Panel icon is registered");}
            using(var key=machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"))
                Assert(key!=null&&(key.GetValue("ScanBridge") as string??"").Contains("--tray"),"Task Manager startup Run entry exists");
            using(var key=machine.OpenSubKey(@"SOFTWARE\ScanBridge"))
            {
                string path=key==null?null:key.GetValue("InstallDir") as string;
                Assert(path!=null&&File.Exists(Path.Combine(path,"ScanBridge.exe")),"Application executable is installed");
                Assert(FileVersionInfo.GetVersionInfo(Path.Combine(path,"ScanBridge.exe")).FileVersion=="0.5.2.0","Installed file metadata is correct");
            }
        }
        string shortcut=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),@"ScanBridge\ScanBridge.lnk");
        Assert(File.Exists(shortcut),"All-user Start menu shortcut exists");
    }
}

