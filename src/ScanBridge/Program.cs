using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace ScanBridge
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try{return Run(args);}
            catch(Exception ex)
            {
                string log=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"setup-error.txt");
                try{File.WriteAllText(log,ex.ToString());}catch{log=Path.Combine(Path.GetTempPath(),"ScanBridge-error.txt");try{File.WriteAllText(log,ex.ToString());}catch{}}
                if(!args.Contains("--install") && !args.Contains("--apply-profile") && !args.Contains("--integration-check") && !args.Contains("--restart-monitor") && !args.Contains("--link-check"))
                    MessageBox.Show(ex.Message+"\n\nDetails: "+log,"ScanBridge",MessageBoxButton.OK,MessageBoxImage.Error);
                return 1;
            }
        }
        private static int Run(string[] args)
        {
            if (args.Contains("--self-test")) return SelfTest();
            int buttonIndex=Array.IndexOf(args,"--button-route-report");
            if(buttonIndex>=0)
            {
                if(buttonIndex+1>=args.Length)return 2;
                try{ButtonRouting.EnsureSelectedAsync().GetAwaiter().GetResult();File.WriteAllText(args[buttonIndex+1],"Physical-button profile selected and saved: ScanBridge. No scan was started.");return 0;}
                catch(Exception ex){File.WriteAllText(args[buttonIndex+1],ex.ToString());return 1;}
            }
            int ensureIndex=Array.IndexOf(args,"--ensure-home-report");
            if(ensureIndex>=0)
            {
                if(ensureIndex+1>=args.Length)return 2;
                bool wasRunning=HomeMonitor.IsReady;
                try
                {
                    HomeMonitor.EnsureRunningAsync().GetAwaiter().GetResult();
                    HomeMonitor.SuppressHomeWindows();
                    var report=ScanSnapHomeConnection.Query();
                    File.WriteAllText(Path.GetFullPath(args[ensureIndex+1]),new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new{wasRunning,monitorReady=HomeMonitor.IsReady,report}));
                    return report.querySucceeded?0:1;
                }
                catch(Exception ex){File.WriteAllText(Path.GetFullPath(args[ensureIndex+1]),ex.ToString());return 1;}
            }
            if(args.Contains("--install")){IntegrationSetup.Install();return 0;}
            if(args.Contains("--restart-monitor")){ScanSnapCommands.RestartMonitor();return 0;}
            int applyIndex=Array.IndexOf(args,"--apply-profile");
            if(applyIndex>=0)
            {
                if(applyIndex+1>=args.Length)return 2;
                var profile=new ProfileStore(Path.GetFullPath(args[applyIndex+1])).Load();
                if(profile==null)throw new InvalidDataException("Profile draft is missing.");
                // Prepare linkage before changing the shared settings, so validation failures cannot partially apply.
                string linkage=IntegrationSetup.BuildLinkage(profile);
                new ProfileStore(ProfileStore.SharedPath).Save(profile);
                File.WriteAllText(IntegrationSetup.LinkagePath,linkage,System.Text.Encoding.Unicode);return 0;
            }
            int checkIndex=Array.IndexOf(args,"--integration-check");
            if(checkIndex>=0)
            {
                if(checkIndex+1>=args.Length)return 2;
                string result=ScanController.CheckRegistration();
                File.WriteAllText(Path.GetFullPath(args[checkIndex+1]),result);return 0;
            }
            int connectionIndex = Array.IndexOf(args, "--connection-report");
            if (connectionIndex >= 0)
            {
                if (connectionIndex + 1 >= args.Length) return 2;
                var report = ScanSnapHomeConnection.Query();
                File.WriteAllText(Path.GetFullPath(args[connectionIndex + 1]), new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(report));
                return report.querySucceeded && report.scannerCount > 0 ? 0 : 1;
            }
            int notifyIndex=Array.IndexOf(args,"--link-check");
            if(notifyIndex>=0)
            {
                if(notifyIndex+1>=args.Length)return 2;
                ulong code=ScanSnapCommands.NotifyRunning(true);
                try{File.WriteAllText(args[notifyIndex+1],"Automatic linkage request status: "+code+". Physical-button routing must still be tested.");}
                finally{ScanSnapCommands.NotifyRunning(false);}
                return code==0?0:1;
            }
            string path = ProfileStore.SharedPath;
            int index = Array.IndexOf(args, "--config");
            if (index >= 0)
            {
                if (index + 1 >= args.Length) return 2;
                path = Path.GetFullPath(args[index + 1]);
            }
            if(args.Length==1 && File.Exists(args[0]) && Path.GetExtension(args[0]).Equals(".ini",StringComparison.OrdinalIgnoreCase))
                return Receive(args[0]);
            var app = new Application();
            var window = new MainWindow(new ProfileStore(path));
            window.StartInTray=args.Contains("--tray");
            int renderIndex = Array.IndexOf(args, "--render");
            if (renderIndex >= 0 && renderIndex + 1 < args.Length)
            {
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(1000, 960)); content.Arrange(new Rect(0, 0, 1000, 960)); content.UpdateLayout();
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1000, 960, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(content);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using (var output = File.Create(args[renderIndex + 1])) encoder.Save(output);
                return 0;
            }
            bool firstInstance;
            string instanceName="Local\\ScanBridge_UI_v052_"+System.Diagnostics.Process.GetCurrentProcess().SessionId;
            using(var mutex=new System.Threading.Mutex(true,instanceName,out firstInstance))
            using(var signal=new System.Threading.EventWaitHandle(false,System.Threading.EventResetMode.AutoReset,instanceName+"_Show"))
            {
                if(!firstInstance){signal.Set();return 0;}
                window.ShowSignal=signal;
                app.Run(window);
                mutex.ReleaseMutex();
            }
            return 0;
        }

        private static int Receive(string result)
        {
            var profile=new ProfileStore(ProfileStore.SharedPath).Load();
            if(profile==null)throw new InvalidOperationException("The shared scan profile is missing. Source pages have not been removed.");
            ScanJob job=ScanJobs.Import(result,profile);
            if(profile.reviewBeforeSaving || !job.pages.Any(p=>!p.excluded))
            { new Application().Run(new ReviewWindow(job));return 0; }
            try{ScanJobs.Export(job);return 0;}
            catch(Exception ex)
            {
                MessageBox.Show("Your scanned pages are kept for recovery. "+ex.Message,"ScanBridge",MessageBoxButton.OK,MessageBoxImage.Warning);
                new Application().Run(new ReviewWindow(job));return 1;
            }
        }

        private static int SelfTest()
        {
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ScanBridge-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new ProfileStore(Path.Combine(folder, "profiles.json"));
                var routingProfiles=new System.Xml.XmlDocument();
                routingProfiles.LoadXml("<ProfileRoot><XMLConfigMajorVersion>1</XMLConfigMajorVersion><Profiles><Profile><ProfileID>bridge</ProfileID><SbAppID>app</SbAppID></Profile></Profiles><Devices><Device><DeviceSerialNo>TEST1</DeviceSerialNo><Profiles><Selected>quick</Selected><Profile><ID>quick</ID><LCDDisplay>1</LCDDisplay></Profile><Profile><ID>bridge</ID><LCDDisplay>1</LCDDisplay></Profile></Profiles></Device></Devices></ProfileRoot>");
                var routingRegistry=new System.Xml.XmlDocument();routingRegistry.LoadXml("<SdkAppRegistryConfigs><SdkAppRegistrySettings><SdkAppRegistrySetting RegistryKey='ScanBridge_Desktop' AppId='app'/></SdkAppRegistrySettings></SdkAppRegistryConfigs>");
                var route=ButtonRouting.ReadRoute(routingProfiles,routingRegistry,"TEST1");
                if(route.Selected||route.Index!=1||route.Count!=2)throw new Exception("Button route detection failed.");
                routingProfiles.SelectSingleNode("/ProfileRoot/Devices/Device/Profiles/Selected").InnerText="bridge";
                if(!ButtonRouting.ReadRoute(routingProfiles,routingRegistry,"TEST1").Selected)throw new Exception("Selected button profile was not recognized.");
                routingProfiles.SelectSingleNode("/ProfileRoot/Profiles").AppendChild(routingProfiles.SelectSingleNode("/ProfileRoot/Profiles/Profile").CloneNode(true));
                bool ambiguous=false;try{ButtonRouting.ReadRoute(routingProfiles,routingRegistry,"TEST1");}catch(InvalidDataException){ambiguous=true;}
                if(!ambiguous)throw new Exception("Ambiguous button profiles were accepted.");
                int launches=0,waits=0;
                HomeMonitor.WaitForReadyAsync(()=>true,()=>false,()=>launches++,()=>System.Threading.Tasks.Task.CompletedTask,3).GetAwaiter().GetResult();
                if(launches!=0)throw new Exception("An already-ready monitor was started again.");
                HomeMonitor.WaitForReadyAsync(()=>waits>=2,()=>false,()=>launches++,()=>{waits++;return System.Threading.Tasks.Task.CompletedTask;},3).GetAwaiter().GetResult();
                if(launches!=1||waits!=2)throw new Exception("Background startup readiness handling failed.");
                waits=0;
                HomeMonitor.WaitForReadyAsync(()=>waits>=1,()=>true,()=>launches++,()=>{waits++;return System.Threading.Tasks.Task.CompletedTask;},3).GetAwaiter().GetResult();
                if(launches!=1)throw new Exception("A starting monitor was launched twice.");
                bool timedOut=false;
                try{HomeMonitor.WaitForReadyAsync(()=>false,()=>true,()=>launches++,()=>System.Threading.Tasks.Task.CompletedTask,2).GetAwaiter().GetResult();}catch(InvalidOperationException){timedOut=true;}
                if(!timedOut)throw new Exception("Unready startup was reported as successful.");
                var ini = ScanSnapHomeConnection.ReadIni(new[] { "; comment", "[INFO]", "ScannerCount=1", "[SCANNER1]", "ScannerName=ScanSnap iX1400", "Path=C:\\a=b.pdf" });
                if (ini["info/scannercount"] != "1" || ini["scanner1/Path"] != @"C:\a=b.pdf") throw new Exception("INI parsing failed.");
                if (store.Load() != null) throw new Exception("Missing profile test failed.");
                var namingTime=new DateTime(2026,11,9,14,5,7);
                var namingProfile=new Profile { destination=folder,outputPrefix="Invoices" };
                string[] expectedNames={"Invoices-2026-11-09-14-05-07","Invoices-2026-11-09_14-05-07","Invoices-20261109-140507","Invoices-2026-11-09","Invoices-Document","Invoices-Document-2026-11-09_14-05-07"};
                for(int n=0;n<OutputNaming.Styles.Length;n++)
                {
                    namingProfile.outputNaming=OutputNaming.Styles[n];
                    if(OutputNaming.Stem(namingProfile,namingTime)!=expectedNames[n])throw new Exception("Filename format failed: "+OutputNaming.Styles[n]);
                }
                namingProfile.outputNaming="custom";namingProfile.outputPrefix="";namingProfile.outputName="Résumé.pdf";
                if(OutputNaming.Stem(namingProfile,namingTime)!="Résumé")throw new Exception("Custom filename / PDF extension failed.");
                foreach(string bad in new[]{"../escape","bad/name","bad:name","CON","nul.pdf","LPT1","bad.","",new string('x',141)})
                {
                    namingProfile.outputName=bad;bool invalid=false;
                    try{OutputNaming.Stem(namingProfile,namingTime);}catch(InvalidDataException){invalid=true;}
                    if(!invalid)throw new Exception("Unsafe filename accepted: "+bad);
                }
                namingProfile.outputNaming="template";namingProfile.outputName="Document";namingProfile.outputTemplate="{unknown}";
                bool badTemplate=false;try{OutputNaming.Stem(namingProfile,namingTime);}catch(InvalidDataException){badTemplate=true;}
                if(!badTemplate)throw new Exception("Unknown filename token accepted.");
                Directory.CreateDirectory(folder);
                File.WriteAllText(store.FilePath,"{\"destination\":\"C:\\\\Scans\"}");
                if(store.Load().outputNaming!="dateTime"||store.Load().outputPrefix!="Scan")throw new Exception("Older profile naming defaults failed.");
                string collisionFolder=Path.Combine(folder,"names");Directory.CreateDirectory(collisionFolder);
                File.WriteAllText(Path.Combine(collisionFolder,"Invoice.pdf"),"original");
                System.Threading.Tasks.Parallel.For(0,4,n=>
                {
                    string temporary=Path.Combine(collisionFolder,Guid.NewGuid().ToString("N")+".partial");File.WriteAllText(temporary,"new");
                    OutputNaming.Commit(temporary,collisionFolder,"Invoice");
                });
                if(Directory.GetFiles(collisionFolder,"*.pdf").Length!=5||File.ReadAllText(Path.Combine(collisionFolder,"Invoice.pdf"))!="original"||!File.Exists(Path.Combine(collisionFolder,"Invoice (5).pdf")))throw new Exception("Concurrent duplicate filename protection failed.");
                var profile = new Profile { destination = @"C:\Scans", rotation = 90 };
                profile.outputNaming="custom";profile.outputName="Receipt";profile.outputPrefix="Accounts";
                store.Save(profile);
                if (store.Load().rotation != 90 || store.Load().outputName!="Receipt" || store.Load().outputPrefix!="Accounts" || store.Load().outputNaming!="custom") throw new Exception("Round trip failed.");
                profile.destination = @"\\server\scans"; profile.dpi = 600;
                store.Save(profile);
                if (store.Load().dpi != 600 || store.Load().destination != profile.destination) throw new Exception("Replacement failed.");
                profile.destination = "relative";
                bool rejected = false;
                try { store.Save(profile); } catch (InvalidDataException) { rejected = true; }
                if (!rejected || store.Load().dpi != 600) throw new Exception("Invalid path protection failed.");
                File.WriteAllText(store.FilePath, "{broken");
                rejected = false;
                try { store.Load(); } catch { rejected = true; }
                if (!rejected) throw new Exception("Corrupt profile accepted.");
                var scanProfile=new Profile{destination=Path.Combine(folder,"output"),dpi=300,deskew=false};
                string blank=Path.Combine(folder,"blank.jpg"),printed=Path.Combine(folder,"printed.jpg");
                using(var bitmap=new System.Drawing.Bitmap(600,800))
                using(var graphics=System.Drawing.Graphics.FromImage(bitmap))
                {
                    graphics.Clear(System.Drawing.Color.White);
                    if(!ImageProcessing.IsBlank(bitmap))throw new Exception("Blank page detection failed.");
                    bitmap.Save(blank,System.Drawing.Imaging.ImageFormat.Jpeg);
                    graphics.FillRectangle(System.Drawing.Brushes.Black,100,100,250,40);
                    graphics.DrawString("ScanBridge integration test",new System.Drawing.Font("Arial",18),System.Drawing.Brushes.Black,60,200);
                    if(ImageProcessing.IsBlank(bitmap))throw new Exception("Printed page was marked blank.");
                    bitmap.Save(printed,System.Drawing.Imaging.ImageFormat.Jpeg);
                }
                string result=Path.Combine(folder,"result.ini");
                File.WriteAllText(result,"[INFO]\nFileDesc=PFUFILELISTFORMAT\n[FILES]\nFileCount=2\nFile1="+printed+"\nFile2="+blank+"\n");
                var job=ScanJobs.Import(result,scanProfile,Path.Combine(folder,"jobs"));
                if(job.pages.Count!=2 || job.pages[0].excluded || !job.pages[1].excluded)throw new Exception("Page import/blank filtering failed.");
                var backProfile=new Profile{destination=Path.Combine(folder,"back-output"),duplex=false,simplexSide="back",deskew=false};
                string backResult=Path.Combine(folder,"back-result.ini");
                File.WriteAllText(backResult,"[INFO]\nFileDesc=PFUFILELISTFORMAT\n[SCAN]\nDuplex=1\n[FILES]\nFileCount=2\nFile1="+blank+"\nFile2="+printed+"\n");
                var backJob=ScanJobs.Import(backResult,backProfile,Path.Combine(folder,"jobs"));
                if(!backJob.pages[0].otherSide||!backJob.pages[0].excluded||backJob.pages[1].excluded||!backProfile.AcquireBothSides)
                    throw new Exception("Opposite-side single-sided filtering failed.");
                if(!IntegrationSetup.BuildLinkage(backProfile).Contains("ScanningSide=0"))throw new Exception("Opposite-side acquisition did not request both sensors.");
                backProfile.simplexSide="front";if(backProfile.AcquireBothSides)throw new Exception("Standard single-sided capture incorrectly enabled duplex.");
                job.pages[0].rotation=90;job.profile.colorMode="gray";
                string pdf=ScanJobs.Export(job);
                string pdfText=System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(pdf));
                if(!pdfText.StartsWith("%PDF-1.4") || !pdfText.Contains("/Count 1") || !pdfText.EndsWith("%%EOF\n"))throw new Exception("PDF export failed.");
                if(!File.Exists(printed)||!File.Exists(blank))throw new Exception("Source pages were removed.");
                // Preserve a non-sensitive sample PDF for independent rendering checks.
                File.Copy(pdf,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test-scan.pdf"),true);
                var loaded=ScanJob.Load(job.Folder);if(loaded.pages.Count!=2||loaded.state!="Saved")throw new Exception("Job recovery failed.");
                string blocked=Path.Combine(folder,"blocked");File.WriteAllText(blocked,"not a folder");
                job.profile.destination=blocked;rejected=false;
                try{ScanJobs.Export(job);}catch(IOException){rejected=true;}
                if(!rejected||ScanJob.Load(job.Folder).state!="Needs attention"||!File.Exists(job.PagePath(job.pages[0])))throw new Exception("Failed delivery did not preserve recoverable pages.");
                job.profile.destination=scanProfile.destination=Path.Combine(folder,"recovered");
                if(!File.Exists(ScanJobs.Export(job)))throw new Exception("Delivery recovery failed.");
                var linkage=IntegrationSetup.BuildLinkage(scanProfile);
                if(!linkage.Contains("ScanMode=3") || !linkage.Contains("Format=2"))throw new Exception("Scan settings mapping failed.");
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self-test-result.txt"), "PASS: physical-button profile mapping/selection detection/ambiguity checks; monitor startup cases; INI parsing; profile persistence/validation; page import/blank detection; rotation/grayscale PDF export; source preservation; failed-delivery recovery; linkage generation.");
                return 0;
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self-test-result.txt"), ex.ToString());
                return 1;
            }
            finally
            {
                // Delete only known files inside this test's unique temporary directory.
                if (Directory.Exists(folder))
                {
                    string absolute=Path.GetFullPath(folder),parent=Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                    if(!absolute.StartsWith(parent,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(absolute).StartsWith("ScanBridge-test-"))throw new InvalidOperationException("Unsafe test cleanup path.");
                    Directory.Delete(absolute,true);
                }
            }
        }
    }
}

