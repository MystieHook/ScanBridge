using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Xml;

namespace ScanBridge
{
    internal sealed class ButtonRoute
    {
        internal string Scanner;
        internal int Index;
        internal int Count;
        internal bool Selected;
    }

    internal static class ButtonRouting
    {
        private static readonly SemaphoreSlim gate = new SemaphoreSlim(1,1);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr FindWindow(string name,string title);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
        private static string HomeData => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"PFU\ScanSnap Home");

        internal static async Task EnsureSelectedAsync()
        {
            await gate.WaitAsync();
            try
            {
                await HomeMonitor.EnsureRunningAsync();
                await Task.Run(()=>SelectProfile());
            }
            finally{gate.Release();}
        }

        private static XmlDocument ReadXml(string path)
        {
            var document=new XmlDocument { XmlResolver=null };
            using(var reader=XmlReader.Create(path,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null})) document.Load(reader);
            return document;
        }

        internal static ButtonRoute ReadRoute(XmlDocument profiles,XmlDocument registry,string scanner)
        {
            if(profiles.SelectSingleNode("/ProfileRoot/XMLConfigMajorVersion")?.InnerText!="1")
                throw new InvalidDataException("This Home profile format is not supported for automatic button setup.");
            var registration=registry.SelectNodes("/SdkAppRegistryConfigs/SdkAppRegistrySettings/SdkAppRegistrySetting").Cast<XmlNode>()
                .SingleOrDefault(n=>n.Attributes?["RegistryKey"]?.Value==ScanSnapCommands.AppKey);
            string app=registration?.Attributes?["AppId"]?.Value;
            if(string.IsNullOrEmpty(app))throw new InvalidDataException("Home has not registered ScanBridge for this Windows user yet.");
            var targets=profiles.SelectNodes("/ProfileRoot/Profiles/Profile").Cast<XmlNode>()
                .Where(n=>n.SelectSingleNode("SbAppID")?.InnerText==app).ToList();
            if(targets.Count!=1)throw new InvalidDataException("Could not identify a unique ScanBridge profile. Existing profiles were not changed.");
            string id=targets[0].SelectSingleNode("ProfileID")?.InnerText;
            var devices=profiles.SelectNodes("/ProfileRoot/Devices/Device").Cast<XmlNode>().ToList();
            var device=devices.SingleOrDefault(n=>n.SelectSingleNode("DeviceSerialNo")?.InnerText==scanner);
            if(device==null && devices.Count==1)device=devices[0];
            if(device==null)throw new InvalidDataException("Could not identify the active scanner for button setup.");
            string serial=device.SelectSingleNode("DeviceSerialNo")?.InnerText;
            if(string.IsNullOrEmpty(serial)||!Regex.IsMatch(serial,@"^[A-Za-z0-9_-]+$"))throw new InvalidDataException("Invalid scanner identifier.");
            var order=device.SelectNodes("Profiles/Profile").Cast<XmlNode>()
                .Where(n=>n.SelectSingleNode("LCDDisplay")?.InnerText!="0").Select(n=>n.SelectSingleNode("ID")?.InnerText).ToList();
            int index=order.IndexOf(id);
            if(index<0)throw new InvalidDataException("ScanBridge is not in the scanner's profile list yet.");
            return new ButtonRoute{Scanner=serial,Index=index,Count=order.Count,Selected=device.SelectSingleNode("Profiles/Selected")?.InnerText==id};
        }

        private static ButtonRoute ReadCurrent()
        {
            string scanner=null;
            string scanConfig=Path.Combine(HomeData,@"Configs\PfuSsHomeScanWindow.config");
            if(File.Exists(scanConfig))scanner=ReadXml(scanConfig).SelectSingleNode("/ScanWindowConfigs/Scanners")?.Attributes?["SelectedScanner"]?.Value;
            return ReadRoute(ReadXml(Path.Combine(HomeData,@"Managed\Profiles\PfuSsHome.xml")),
                ReadXml(Path.Combine(HomeData,@"Configs\PfuSsHomeSdkAppRegistry.config")),scanner);
        }

        private static void SelectProfile()
        {
            ButtonRoute route;
            try{route=ReadCurrent();}
            catch(Exception ex) when(ex is FileNotFoundException || ex is InvalidDataException)
            {
                // Home creates the current user's extension/profile on first activation.
                // Register using the vendor interface, then reread that user's generated identity.
                ScanController.CheckRegistration();route=ReadCurrent();
            }
            if(route.Selected)return;
            IntPtr window=FindWindow(null,"ScanSnap Home - Scan");
            try
            {
                if(window==IntPtr.Zero)
                {
                    IntPtr main=FindWindow(null,"ScanSnap Home");
                    if(main==IntPtr.Zero)
                    {
                        string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),@"PFU\ScanSnap\Home");
                        string executable=Path.Combine(folder,"PfuSshMain.exe");
                        if(!File.Exists(executable))throw new FileNotFoundException("Home's profile controls are unavailable.");
                        using(var process=Process.Start(new ProcessStartInfo(executable)
                        {WorkingDirectory=folder,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})){}
                        var launch=DateTime.UtcNow.AddSeconds(20);
                        while(main==IntPtr.Zero && DateTime.UtcNow<launch)
                        {HomeMonitor.SuppressHomeWindows();Thread.Sleep(200);main=FindWindow(null,"ScanSnap Home");}
                    }
                    if(main==IntPtr.Zero)throw new InvalidOperationException("Home's profile controls could not be opened.");
                    AssertHomeWindow(main);
                    var mainRoot=AutomationElement.FromHandle(main);
                    var open=mainRoot.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,"scanBtnID"));
                    object openPattern;
                    if(open==null||!open.TryGetCurrentPattern(InvokePattern.Pattern,out openPattern))
                        throw new InvalidOperationException("Home does not expose its open-scan-window control.");
                    ((InvokePattern)openPattern).Invoke();
                    var deadline=DateTime.UtcNow.AddSeconds(20);
                    while(window==IntPtr.Zero && DateTime.UtcNow<deadline)
                    {HomeMonitor.SuppressHomeWindows();Thread.Sleep(200);window=FindWindow(null,"ScanSnap Home - Scan");}
                }
                if(window==IntPtr.Zero)throw new InvalidOperationException("Home's profile controls did not become ready. Button setup will retry.");
                AssertHomeWindow(window);
                AutomationElement root=AutomationElement.FromHandle(window);
                AutomationElement list=null;
                var ready=DateTime.UtcNow.AddSeconds(10);
                while(list==null && DateTime.UtcNow<ready)
                {
                    HomeMonitor.SuppressHomeWindows();
                    list=root.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,"ProfileListViewID"));
                    if(list==null)Thread.Sleep(200);
                }
                if(list==null)throw new InvalidOperationException("This Home version does not expose the expected profile controls.");
                var items=list.FindAll(TreeScope.Children,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.ListItem));
                // Never guess a target if Home's UI and its generated profile order disagree.
                if(items.Count!=route.Count||route.Index>=items.Count)throw new InvalidOperationException("Home's visible profile order changed. No profile was selected.");
                var radio=items[route.Index].FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,"ProfileButtonID"));
                object pattern;
                if(radio==null||!radio.Current.IsEnabled||!radio.TryGetCurrentPattern(SelectionItemPattern.Pattern,out pattern))
                    throw new InvalidOperationException("Home is busy or cannot select the button profile right now.");
                ((SelectionItemPattern)pattern).Select();
                if(!((SelectionItemPattern)pattern).Current.IsSelected)throw new InvalidOperationException("Home did not confirm the profile selection.");
                // Normal close saves Home's selected profile. Do not rewrite vendor XML files.
                var close=root.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,"PART_Close"));
                object invoke;
                if(close==null||!close.TryGetCurrentPattern(InvokePattern.Pattern,out invoke))throw new InvalidOperationException("Home could not save the selected profile.");
                ((InvokePattern)invoke).Invoke();
                var saved=DateTime.UtcNow.AddSeconds(5);
                while(DateTime.UtcNow<saved){if(ReadCurrent().Selected)return;Thread.Sleep(200);}
                throw new InvalidOperationException("Home has not saved ScanBridge as the button profile yet.");
            }
            finally{HomeMonitor.SuppressHomeWindows();}
        }

        private static void AssertHomeWindow(IntPtr window)
        {
            uint pid;GetWindowThreadProcessId(window,out pid);
            using(var process=Process.GetProcessById((int)pid))
                if(!string.Equals(process.ProcessName,"PfuSshMain",StringComparison.OrdinalIgnoreCase)||process.SessionId!=Process.GetCurrentProcess().SessionId)
                    throw new InvalidOperationException("The profile window does not belong to Home in this session.");
        }
    }
}
