using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Collections.Generic;

namespace ScanBridge
{
    public sealed class MainWindow : Window
    {
        private readonly ProfileStore store;
        private readonly FrameworkElement root;
        private System.Windows.Forms.NotifyIcon tray;
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        private readonly DispatcherTimer showTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private List<ScanJob> jobs = new List<ScanJob>();
        private bool scanning;
        private bool exiting;
        private bool linkNotified;
        private bool linking;
        internal bool StartInTray { get; set; }
        internal System.Threading.EventWaitHandle ShowSignal { get; set; }
        private TextBox Destination => Find<TextBox>("DestinationBox");
        private ComboBox Dpi => Find<ComboBox>("DpiBox");
        private ComboBox Rotation => Find<ComboBox>("RotationBox");
        private T Find<T>(string name) where T : FrameworkElement => (T)root.FindName(name);

        public MainWindow(ProfileStore store)
        {
            this.store = store;
            Title = "ScanBridge"; Width = 1000; Height = 960; MinWidth = 800; MinHeight = 640;
            Icon=AppVisuals.WindowIcon();
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
            using (var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("ScanBridge.MainWindow.xaml"))
                root = (FrameworkElement)XamlReader.Load(stream);
            Content = root;
            foreach (int value in new[] { 150, 200, 300, 600 }) Dpi.Items.Add(value);
            foreach (int value in new[] { 0, 90, 180, 270 }) Rotation.Items.Add(value);
            Find<ComboBox>("ColorBox").Items.Add("Color"); Find<ComboBox>("ColorBox").Items.Add("Grayscale");
            Find<ComboBox>("SimplexSideBox").Items.Add("Away from you (standard)");Find<ComboBox>("SimplexSideBox").Items.Add("Toward you (opposite side)");
            Find<CheckBox>("DuplexBox").Checked+=(s,e)=>UpdatePlacement();Find<CheckBox>("DuplexBox").Unchecked+=(s,e)=>UpdatePlacement();
            foreach (string label in OutputNaming.Labels) Find<ComboBox>("NamingBox").Items.Add(label);
            Apply(new Profile());
            Find<ComboBox>("NamingBox").SelectionChanged += (s,e) => UpdateFilenamePreview();
            foreach (string name in new[] { "PrefixBox", "OutputNameBox", "TemplateBox" }) Find<TextBox>(name).TextChanged += (s,e) => UpdateFilenamePreview();
            Find<TextBlock>("ConfigLocation").Text = "Profile: " + store.FilePath;
            Find<Button>("SaveButton").Click += (s, e) => Save();
            Find<Button>("BrowseButton").Click += (s, e) => Browse();
            Find<Button>("RefreshButton").Click += async (s, e) => { RefreshDiagnostics(); await RefreshConnection(); };
            Find<Button>("SetupButton").Click += async (s,e) => await RunSetup();
            Find<Button>("ScanButton").Click += async (s,e) => await ScanNow();
            Find<Button>("OpenFolderButton").Click += (s,e) => { try { var p=store.Load();if(p!=null && Directory.Exists(p.destination)) Process.Start("explorer.exe", "\""+p.destination+"\""); } catch(Exception ex){Status(ex.Message);} };
            Find<Button>("ReviewJobButton").Click += (s,e) => { int i=Find<ListBox>("JobsList").SelectedIndex;if(i>=0 && i<jobs.Count) new ReviewWindow(jobs[i]).ShowDialog();RefreshJobs(); };
            Loaded += async (s, e) => { InitializeTray();RefreshJobs();timer.Start();showTimer.Start();if(StartInTray)HideToTray();await RefreshConnection();await NotifyLink(); };
            showTimer.Tick+=(s,e)=>{try{HomeMonitor.SuppressHomeWindows();}catch{}if(ShowSignal!=null && ShowSignal.WaitOne(0))ShowFromTray();};
            timer.Tick += async(s,e)=>{RefreshJobs();if(!scanning)await NotifyLink();};
            Closing += (s,e)=>{ if(!exiting && tray!=null){e.Cancel=true;HideToTray();} };
            Closed += (s,e)=>{timer.Stop();showTimer.Stop();if(tray!=null){tray.Icon?.Dispose();tray.Dispose();}};
            try
            {
                var profile = store.Load();
                if (profile != null) { Apply(profile); Status("Shared profile loaded."); }
                else Status("Choose a destination and save your first profile.");
            }
            catch (Exception ex) { Status("Profile could not be loaded: " + ex.Message); }
            RefreshDiagnostics();
        }
        private void Apply(Profile profile)
        {
            Destination.Text = profile.destination; Dpi.SelectedItem = profile.dpi; Rotation.SelectedItem = profile.rotation;
            Find<CheckBox>("DuplexBox").IsChecked = profile.duplex;
            Find<CheckBox>("BlankBox").IsChecked = profile.removeBlankPages;
            Find<CheckBox>("ReviewBox").IsChecked = profile.reviewBeforeSaving;
            Find<CheckBox>("DeskewBox").IsChecked = profile.deskew;
            Find<ComboBox>("ColorBox").SelectedIndex = profile.colorMode == "gray" ? 1 : 0;
            Find<ComboBox>("SimplexSideBox").SelectedIndex=profile.simplexSide=="back"?1:0;UpdatePlacement();
            Find<ComboBox>("NamingBox").SelectedIndex=Array.IndexOf(OutputNaming.Styles,profile.outputNaming);
            Find<TextBox>("PrefixBox").Text=profile.outputPrefix;
            Find<TextBox>("OutputNameBox").Text=profile.outputName;
            Find<TextBox>("TemplateBox").Text=profile.outputTemplate;
            UpdateFilenamePreview();
        }
        private void UpdateFilenamePreview()
        {
            int index=Find<ComboBox>("NamingBox").SelectedIndex;
            Find<StackPanel>("CustomNamePanel").Visibility=index>=4?Visibility.Visible:Visibility.Collapsed;
            Find<StackPanel>("TemplatePanel").Visibility=index==5?Visibility.Visible:Visibility.Collapsed;
            try { Find<TextBlock>("FilenamePreview").Text="Preview: "+OutputNaming.Stem(ReadForm(),DateTime.Now)+".pdf"; }
            catch(Exception ex) { Find<TextBlock>("FilenamePreview").Text=ex.Message; }
        }
        private void Status(string text) { Find<TextBlock>("SaveStatus").Text = text; }
        private void UpdatePlacement(){Find<StackPanel>("SimplexPlacement").Visibility=Find<CheckBox>("DuplexBox").IsChecked==true?Visibility.Collapsed:Visibility.Visible;}
        private async Task RefreshConnection()
        {
            if(scanning)return;
            var button = Find<Button>("RefreshButton");
            button.IsEnabled = false;
            Find<TextBlock>("DeviceStatus").Text = "Querying ScanSnap Home…";
            try
            {
                Find<TextBlock>("DeviceStatus").Text = "Preparing the scanner connection…";
                await HomeMonitor.EnsureRunningAsync();
                var report = await Task.Run(() => ScanSnapHomeConnection.Query());
                Find<TextBlock>("DeviceStatus").Text = report.querySucceeded && report.scannerCount > 0
                    ? "Connected: " + string.Join(", ", report.scannerNames) : report.detail;
                if (report.querySucceeded) Find<TextBlock>("RuntimeStatus").Text = report.detail;
            }
            catch(Exception ex){Find<TextBlock>("DeviceStatus").Text=ex.Message;}
            finally { button.IsEnabled = true; }
        }
        private Profile ReadForm() => new Profile
        {
            destination=Destination.Text.Trim(),dpi=(int)Dpi.SelectedItem,rotation=(int)Rotation.SelectedItem,
            duplex=Find<CheckBox>("DuplexBox").IsChecked==true,removeBlankPages=Find<CheckBox>("BlankBox").IsChecked==true,
            reviewBeforeSaving=Find<CheckBox>("ReviewBox").IsChecked==true,deskew=Find<CheckBox>("DeskewBox").IsChecked==true,
            colorMode=Find<ComboBox>("ColorBox").SelectedIndex==1?"gray":"color"
            ,simplexSide=Find<ComboBox>("SimplexSideBox").SelectedIndex==1?"back":"front"
            ,outputNaming=Find<ComboBox>("NamingBox").SelectedIndex>=0?OutputNaming.Styles[Find<ComboBox>("NamingBox").SelectedIndex]:"dateTime"
            ,outputPrefix=Find<TextBox>("PrefixBox").Text.Trim(),outputName=Find<TextBox>("OutputNameBox").Text.Trim(),outputTemplate=Find<TextBox>("TemplateBox").Text
        };
        private async void Save()
        {
            try
            {
                var profile=ReadForm();profile.Validate();
                if(store.FilePath!=ProfileStore.SharedPath){store.Save(profile);Status("Preview profile saved; installed scanning uses the shared profile.");return;}
                string draft=Path.Combine(Path.GetTempPath(),"ScanBridge-profile-"+Guid.NewGuid().ToString("N")+".json");
                new ProfileStore(draft).Save(profile);
                try
                {
                    await RunElevated("--apply-profile \""+draft+"\"");
                    if(!scanning)
                    {
                        try{await HomeMonitor.EnsureRunningAsync();linkNotified=false;await NotifyLink();Status("Shared profile saved.");}
                        catch(Exception ex){Status("Profile saved. Connection reload needs attention: "+ex.Message);}
                    }
                    else Status("Profile saved. Restart Home after the current scan to apply acquisition changes.");
                }
                finally{if(File.Exists(draft))File.Delete(draft);}
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show(this, "Administrator permission is needed to change this computer's shared profile. Reopen ScanBridge as administrator to save settings.", "Shared profile", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not save profile", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
        private async Task RunElevated(string arguments)
        {
            var info=new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,arguments){UseShellExecute=true,Verb="runas"};
            using(var process=Process.Start(info)){await Task.Run(()=>process.WaitForExit());if(process.ExitCode!=0)throw new InvalidOperationException("Setup did not complete. See setup-error.txt beside the application, or the Windows permission prompt.");}
        }
        private async Task RunSetup()
        {
            try{await RunElevated("--install");Status("Reloading the scanner connection…");await Task.Run(()=>ScanSnapCommands.RestartMonitor());Apply(new ProfileStore(ProfileStore.SharedPath).Load());linkNotified=false;await NotifyLink();Status("Setup complete. Load a test sheet and choose Scan now.");}
            catch(Exception ex){MessageBox.Show(this,ex.Message,"Setup",MessageBoxButton.OK,MessageBoxImage.Warning);}
        }
        private async Task ScanNow()
        {
            if(scanning)return;
            try
            {
                var profile=new ProfileStore(ProfileStore.SharedPath).Load();
                if(profile==null)throw new InvalidOperationException("Run setup and save the shared profile first.");
                scanning=true;Find<Button>("ScanButton").IsEnabled=false;Find<Button>("RefreshButton").IsEnabled=false;Find<Button>("SaveButton").IsEnabled=false;
                Status("Scanning… use the scanner's stop control to interrupt a scan.");
                await ScanController.ScanAsync(profile);Status("Acquisition completed. Pages are being processed or opened for review.");
            }
            catch(Exception ex){MessageBox.Show(this,ex.Message,"Scan",MessageBoxButton.OK,MessageBoxImage.Warning);Status("Scan needs attention.");}
            finally
            {
                scanning=false;Find<Button>("ScanButton").IsEnabled=true;Find<Button>("RefreshButton").IsEnabled=true;Find<Button>("SaveButton").IsEnabled=true;RefreshJobs();
                // Reserve/Release ends automatic linkage in the documented interface.
                linkNotified=false;await NotifyLink();
            }
        }
        private async Task NotifyLink()
        {
            if(linking || !IntegrationSetup.IsRegistered())return;
            linking=true;
            try
            {
                await HomeMonitor.EnsureRunningAsync();
                await ButtonRouting.EnsureSelectedAsync();
                ulong code=await Task.Run(()=>ScanSnapCommands.NotifyRunning(true));
                if(code==0){linkNotified=true;Find<TextBlock>("IntegrationStatus").Text="The scanner button is set to ScanBridge. Press it to scan using the shared computer profile.";}
                else Find<TextBlock>("IntegrationStatus").Text="Home did not accept automatic linkage (status "+code+"). Scan now uses explicit application selection.";
            }
            catch(Exception ex){Find<TextBlock>("IntegrationStatus").Text=ex.Message;}
            finally{linking=false;}
        }
        private void InitializeTray()
        {
            tray=new System.Windows.Forms.NotifyIcon{Icon=AppVisuals.TrayIcon(),Text="ScanBridge",Visible=true};
            var menu=new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Open ScanBridge",null,(s,e)=>ShowFromTray());
            menu.Items.Add("Scan now",null,async(s,e)=>{ShowFromTray();await ScanNow();});
            menu.Items.Add("Exit",null,async(s,e)=>{if(scanning){Status("Wait for scanning to finish before exiting.");ShowFromTray();return;}if(linkNotified)try{await Task.Run(()=>ScanSnapCommands.NotifyRunning(false));}catch{}exiting=true;Close();});
            tray.ContextMenuStrip=menu;tray.DoubleClick+=(s,e)=>ShowFromTray();
        }
        private void HideToTray(){ShowInTaskbar=false;Hide();}
        private void ShowFromTray(){ShowInTaskbar=true;Show();WindowState=WindowState.Normal;Activate();}
        private void RefreshJobs()
        {
            try
            {
                jobs=ScanJobs.List().Take(30).ToList();var list=Find<ListBox>("JobsList");int selected=list.SelectedIndex;
                list.Items.Clear();foreach(var job in jobs)list.Items.Add(job.created.ToString("MMM d, HH:mm")+"   ·   "+job.pages.Count+" pages   ·   "+job.state);
                if(jobs.Count>0)list.SelectedIndex=Math.Max(0,Math.Min(selected,jobs.Count-1));
                Find<TextBlock>("JobStatus").Text=jobs.Count==0?"Your scanned pages will appear here.":"Raw pages are kept for recovery. Select a scan to review or save again.";
            }catch(Exception ex){Find<TextBlock>("JobStatus").Text=ex.Message;}
        }
        private void Browse()
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Choose the scan destination";
                if (Directory.Exists(Destination.Text)) dialog.SelectedPath = Destination.Text;
                var owner = new System.Windows.Forms.NativeWindow();
                owner.AssignHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
                try { if (dialog.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK) Destination.Text = dialog.SelectedPath; }
                finally { owner.ReleaseHandle(); }
            }
        }
        private void RefreshDiagnostics()
        {
            try
            {
                string homeFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"PFU\ScanSnap\Home");
                string homeExecutable = Path.Combine(homeFolder, "PfuSshMain.exe");
                string version = File.Exists(homeExecutable) ? FileVersionInfo.GetVersionInfo(homeExecutable).ProductVersion : null;
                try {
                foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
                using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (var uninstall = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                {
                    if (uninstall == null) continue;
                    foreach (var name in uninstall.GetSubKeyNames())
                    {
                        try {
                            using (var item = uninstall.OpenSubKey(name))
                                if ((string)item?.GetValue("DisplayName") == "ScanSnap Home") version = item.GetValue("DisplayVersion") as string;
                        } catch (System.Security.SecurityException) { } catch (UnauthorizedAccessException) { }
                    }
                }
                } catch (System.Security.SecurityException) { } catch (UnauthorizedAccessException) { }
                Find<TextBlock>("HomeStatus").Text = version == null ? "ScanSnap Home installation not found" : "ScanSnap Home " + version + " installed";
                bool registered = false;
                try {
                using (var usb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB"))
                if (usb != null)
                    foreach (var name in usb.GetSubKeyNames().Where(n => n.StartsWith("VID_04C5", StringComparison.OrdinalIgnoreCase)))
                    using (var device = usb.OpenSubKey(name))
                    {
                        if (device == null) continue;
                        foreach (var instance in device.GetSubKeyNames())
                        using (var entry = device.OpenSubKey(instance))
                            if (((string)entry?.GetValue("DeviceDesc") ?? "").IndexOf("iX1400", StringComparison.OrdinalIgnoreCase) >= 0) registered = true;
                    }
                Find<TextBlock>("DeviceStatus").Text = registered ? "iX1400 driver registered · live connection not yet verified" : "Live scanner detection awaits SDK integration";
                } catch (Exception) { Find<TextBlock>("DeviceStatus").Text = "Device registration check unavailable · live detection awaits SDK integration"; }
                string sdk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"PFU\ScanSnap\Home\PfuSsMonSdk.exe");
                Find<TextBlock>("RuntimeStatus").Text = File.Exists(sdk) ? "Vendor SDK runtime found · developer integration pending" : "Vendor SDK runtime not found in its standard location";
            }
            catch (Exception ex) { Find<TextBlock>("HomeStatus").Text = "Could not inspect installation: " + ex.Message; }
        }
    }
}
