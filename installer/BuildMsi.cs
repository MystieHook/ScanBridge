// Standalone MSI authoring tool using the Windows Installer API and makecab.exe.
// No downloaded installer SDK, PowerShell runtime, or build-time network access is required.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

internal static class BuildMsi
{
    public const string ProductCode="{BD693DF7-64AA-4C43-A056-48EA5550A684}";
    const string UpgradeCode="{5327CD0F-5768-409B-964B-31B6E4037F13}";
    static uint database;
    static Dictionary<string,string[]> columns=new Dictionary<string,string[]>();
    [DllImport("msi.dll",CharSet=CharSet.Unicode)] static extern uint MsiOpenDatabase(string path,IntPtr mode,out uint handle);
    [DllImport("msi.dll",CharSet=CharSet.Unicode)] static extern uint MsiDatabaseOpenView(uint db,string sql,out uint view);
    [DllImport("msi.dll")] static extern uint MsiViewExecute(uint view,uint record);
    [DllImport("msi.dll")] static extern uint MsiViewFetch(uint view,out uint record);
    [DllImport("msi.dll")] static extern uint MsiCreateRecord(uint fields);
    [DllImport("msi.dll",CharSet=CharSet.Unicode)] static extern uint MsiRecordSetString(uint record,uint field,string value);
    [DllImport("msi.dll")] static extern uint MsiRecordSetInteger(uint record,uint field,int value);
    [DllImport("msi.dll",CharSet=CharSet.Unicode)] static extern uint MsiRecordSetStream(uint record,uint field,string path);
    [DllImport("msi.dll")] static extern uint MsiDatabaseCommit(uint db);
    [DllImport("msi.dll")] static extern uint MsiCloseHandle(uint handle);
    [DllImport("msi.dll")] static extern uint MsiGetLastErrorRecord();
    [DllImport("msi.dll",CharSet=CharSet.Unicode)] static extern uint MsiFormatRecord(uint install,uint record,StringBuilder result,ref uint size);
    [DllImport("msi.dll",CharSet=CharSet.Unicode)] static extern uint MsiGetSummaryInformation(uint db,string path,uint count,out uint summary);
    [DllImport("msi.dll",CharSet=CharSet.Unicode)] static extern uint MsiSummaryInfoSetProperty(uint summary,uint property,uint type,int integer,IntPtr fileTime,string value);
    [DllImport("msi.dll")] static extern uint MsiSummaryInfoPersist(uint summary);
    [DllImport("msi.dll",CharSet=CharSet.Unicode)] static extern uint MsiVerifyPackage(string path);
    sealed class StreamValue{public string Path;public StreamValue(string path){Path=path;}}

    static int Main(string[] args)
    {
        try
        {
            if(args.Length!=2)throw new ArgumentException("Usage: BuildMsi.exe <ScanBridge.exe> <output.msi>");
            Build(Path.GetFullPath(args[0]),Path.GetFullPath(args[1]));
            Console.WriteLine("Built and verified: "+args[1]);return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        finally{if(database!=0)MsiCloseHandle(database);}
    }
    static void Check(uint result,string action)
    {
        if(result==0)return;
        string detail="";uint error=MsiGetLastErrorRecord();
        if(error!=0){uint size=4095;var text=new StringBuilder(4096);MsiFormatRecord(0,error,text,ref size);detail=text.ToString();MsiCloseHandle(error);}
        throw new InvalidOperationException(action+" (MSI "+result+"): "+detail);
    }
    static void Sql(string sql)
    {
        uint view;Check(MsiDatabaseOpenView(database,sql,out view),sql);
        try{Check(MsiViewExecute(view,0),sql);}finally{MsiCloseHandle(view);}
    }
    static void Table(string name,string[] definitions,string[] names,string primary)
    {
        Sql("CREATE TABLE `"+name+"` ("+string.Join(", ",definitions)+" PRIMARY KEY "+primary+")");columns[name]=names;
    }
    static void Row(string table,params object[] values)
    {
        var names=columns[table];if(names.Length!=values.Length)throw new Exception("Column count: "+table);
        string[] quoted=new string[names.Length],marks=new string[names.Length];
        for(int i=0;i<names.Length;i++){quoted[i]="`"+names[i]+"`";marks[i]="?";}
        uint view;Check(MsiDatabaseOpenView(database,"INSERT INTO `"+table+"` ("+string.Join(",",quoted)+") VALUES ("+string.Join(",",marks)+")",out view),"Insert "+table);
        uint record=MsiCreateRecord((uint)values.Length);
        try
        {
            for(int i=0;i<values.Length;i++)
            {
                object value=values[i];if(value==null)continue;
                var stream=value as StreamValue;
                if(stream!=null)Check(MsiRecordSetStream(record,(uint)i+1,stream.Path),"Stream "+stream.Path);
                else if(value is int)Check(MsiRecordSetInteger(record,(uint)i+1,(int)value),"Integer");
                else Check(MsiRecordSetString(record,(uint)i+1,value.ToString()),"String");
            }
            Check(MsiViewExecute(view,record),"Insert "+table);
        }
        finally{MsiCloseHandle(record);MsiCloseHandle(view);}
    }
    static void Build(string exe,string output)
    {
        if(!File.Exists(exe))throw new FileNotFoundException("Build the C# app first.",exe);
        if(File.Exists(output))throw new IOException("Choose a new MSI output path rather than overwriting an existing release.");
        string folder=Path.GetDirectoryName(output);Directory.CreateDirectory(folder);
        string work=Path.Combine(folder,"msi-build-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
        string cab=Path.Combine(work,"ScanBridge.cab"),ddf=Path.Combine(work,"payload.ddf");
        File.WriteAllText(ddf,".OPTION EXPLICIT\r\n.Set CabinetNameTemplate=ScanBridge.cab\r\n.Set DiskDirectoryTemplate=\""+work+"\"\r\n.Set CompressionType=LZX\r\n.Set Cabinet=on\r\n.Set Compress=on\r\n.Set MaxDiskSize=0\r\n\""+exe+"\" ScanBridgeExe\r\n",Encoding.ASCII);
        var makecab=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"makecab.exe"),"/F \""+ddf+"\"") {UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=work};
        using(var process=Process.Start(makecab)){process.WaitForExit();if(process.ExitCode!=0)throw new Exception("makecab failed.");}
        Check(MsiOpenDatabase(output,new IntPtr(3),out database),"Create MSI");
        Table("Property",new[]{"`Property` CHAR(72) NOT NULL","`Value` CHAR(0) LOCALIZABLE"},new[]{"Property","Value"},"`Property`");
        Row("Property","ProductCode",ProductCode);Row("Property","UpgradeCode",UpgradeCode);
        Row("Property","ProductName","ScanBridge");Row("Property","ProductVersion","0.5.2");Row("Property","ProductLanguage","1033");
        Row("Property","Manufacturer","ScanBridge");Row("Property","ALLUSERS","1");Row("Property","INSTALLLEVEL","1");
        Row("Property","ARPNOMODIFY","1");Row("Property","ARPINSTALLLOCATION","[INSTALLDIR]");Row("Property","ARPCOMMENTS","ScanSnap companion. Requires ScanSnap Home and .NET Framework 4.8 or later.");
        Row("Property","SecureCustomProperties","OLDPRODUCTS;NEWERPRODUCTS");
        Table("Icon",new[]{"`Name` CHAR(72) NOT NULL","`Data` OBJECT NOT NULL"},new[]{"Name","Data"},"`Name`");
        Row("Icon","ScanBridge.ico",new StreamValue(Path.Combine(Path.GetDirectoryName(exe),"ScanBridge.ico")));
        Row("Property","ARPPRODUCTICON","ScanBridge.ico");
        Table("Directory",new[]{"`Directory` CHAR(72) NOT NULL","`Directory_Parent` CHAR(72)","`DefaultDir` CHAR(255) NOT NULL LOCALIZABLE"},new[]{"Directory","Directory_Parent","DefaultDir"},"`Directory`");
        Row("Directory","TARGETDIR",null,"SourceDir");Row("Directory","ProgramFilesFolder","TARGETDIR",".");Row("Directory","INSTALLDIR","ProgramFilesFolder","ScanBridge");
        Row("Directory","ProgramMenuFolder","TARGETDIR",".");Row("Directory","StartMenuDir","ProgramMenuFolder","ScanBridge");
        Table("Component",new[]{"`Component` CHAR(72) NOT NULL","`ComponentId` CHAR(38)","`Directory_` CHAR(72) NOT NULL","`Attributes` SHORT NOT NULL","`Condition` CHAR(255)","`KeyPath` CHAR(72)"},new[]{"Component","ComponentId","Directory_","Attributes","Condition","KeyPath"},"`Component`");
        Row("Component","App","{E81E9156-0CAB-491E-B96E-6432C7EC6A21}","INSTALLDIR",0,null,"ScanBridgeExe");
        Row("Component","Integration","{DA2C3671-14D9-467E-9887-A540FC588C3A}","INSTALLDIR",4,null,"InstallDirectory");
        Row("Component","Startup","{AA9BC06E-7097-43C5-A872-924C2F69A8F1}","INSTALLDIR",4,null,"StartupRun");
        Table("Feature",new[]{"`Feature` CHAR(38) NOT NULL","`Feature_Parent` CHAR(38)","`Title` CHAR(64) LOCALIZABLE","`Description` CHAR(255) LOCALIZABLE","`Display` SHORT","`Level` SHORT NOT NULL","`Directory_` CHAR(72)","`Attributes` SHORT NOT NULL"},new[]{"Feature","Feature_Parent","Title","Description","Display","Level","Directory_","Attributes"},"`Feature`");
        Row("Feature","Complete",null,"ScanBridge","Scanner application and shared-computer integration",1,1,"INSTALLDIR",0);
        Table("FeatureComponents",new[]{"`Feature_` CHAR(38) NOT NULL","`Component_` CHAR(72) NOT NULL"},new[]{"Feature_","Component_"},"`Feature_`, `Component_`");
        foreach(string component in new[]{"App","Integration","Startup"})Row("FeatureComponents","Complete",component);
        Table("File",new[]{"`File` CHAR(72) NOT NULL","`Component_` CHAR(72) NOT NULL","`FileName` CHAR(255) NOT NULL LOCALIZABLE","`FileSize` LONG NOT NULL","`Version` CHAR(72)","`Language` CHAR(20)","`Attributes` SHORT","`Sequence` SHORT NOT NULL"},new[]{"File","Component_","FileName","FileSize","Version","Language","Attributes","Sequence"},"`File`");
        Row("File","ScanBridgeExe","App","ScanBridge.exe",checked((int)new FileInfo(exe).Length),"0.5.2.0","0",16384,1);
        Table("Media",new[]{"`DiskId` SHORT NOT NULL","`LastSequence` LONG NOT NULL","`DiskPrompt` CHAR(64) LOCALIZABLE","`Cabinet` CHAR(255)","`VolumeLabel` CHAR(32)","`Source` CHAR(72)"},new[]{"DiskId","LastSequence","DiskPrompt","Cabinet","VolumeLabel","Source"},"`DiskId`");
        Row("Media",1,1,null,"#ScanBridge.cab",null,null);
        columns["_Streams"]=new[]{"Name","Data"};Row("_Streams","ScanBridge.cab",new StreamValue(cab));
        Table("Registry",new[]{"`Registry` CHAR(72) NOT NULL","`Root` SHORT NOT NULL","`Key` CHAR(255) NOT NULL","`Name` CHAR(255)","`Value` CHAR(0) LOCALIZABLE","`Component_` CHAR(72) NOT NULL"},new[]{"Registry","Root","Key","Name","Value","Component_"},"`Registry`");
        Row("Registry","InstallDirectory",2,@"SOFTWARE\ScanBridge","InstallDir","[INSTALLDIR]","Integration");
        Row("Registry","ControlPanelIcon",2,@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\"+ProductCode,"DisplayIcon","[INSTALLDIR]ScanBridge.exe,0","Integration");
        string extension=@"SOFTWARE\PFU\ScanSnap Extension\ScanBridge_Desktop";
        Row("Registry","ExtensionExe",2,extension,null,"[INSTALLDIR]ScanBridge.exe","Integration");
        Row("Registry","ExtensionConfig",2,extension,"Config",@"[CommonAppDataFolder]ScanBridge\ScanBridge.ini","Integration");
        Row("Registry","ExtensionPath",2,extension,"Path","[INSTALLDIR]","Integration");
        Row("Registry","StartupRun",2,@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run","ScanBridge","\"[INSTALLDIR]ScanBridge.exe\" --tray","Startup");
        Table("Shortcut",new[]{"`Shortcut` CHAR(72) NOT NULL","`Directory_` CHAR(72) NOT NULL","`Name` CHAR(128) NOT NULL LOCALIZABLE","`Component_` CHAR(72) NOT NULL","`Target` CHAR(255) LOCALIZABLE","`Arguments` CHAR(255) LOCALIZABLE","`Description` CHAR(255) LOCALIZABLE","`Hotkey` SHORT","`Icon_` CHAR(72)","`IconIndex` SHORT","`ShowCmd` SHORT","`WkDir` CHAR(72)","`DisplayResourceDLL` CHAR(255)","`DisplayResourceId` LONG","`DescriptionResourceDLL` CHAR(255)","`DescriptionResourceId` LONG"},new[]{"Shortcut","Directory_","Name","Component_","Target","Arguments","Description","Hotkey","Icon_","IconIndex","ShowCmd","WkDir","DisplayResourceDLL","DisplayResourceId","DescriptionResourceDLL","DescriptionResourceId"},"`Shortcut`");
        Row("Shortcut","StartMenu","StartMenuDir","ScanBridge","App","[INSTALLDIR]ScanBridge.exe",null,"Open ScanBridge",null,"ScanBridge.ico",0,1,"INSTALLDIR",null,null,null,null);
        Table("RemoveFile",new[]{"`FileKey` CHAR(72) NOT NULL","`Component_` CHAR(72) NOT NULL","`FileName` CHAR(255) LOCALIZABLE","`DirProperty` CHAR(72) NOT NULL","`InstallMode` SHORT NOT NULL"},new[]{"FileKey","Component_","FileName","DirProperty","InstallMode"},"`FileKey`");
        Row("RemoveFile","RemoveStartMenuFolder","App",null,"StartMenuDir",2);Row("RemoveFile","RemoveEmptyAppFolder","App",null,"INSTALLDIR",2);
        Table("CustomAction",new[]{"`Action` CHAR(72) NOT NULL","`Type` SHORT NOT NULL","`Source` CHAR(72)","`Target` CHAR(255) LOCALIZABLE"},new[]{"Action","Type","Source","Target"},"`Action`");
        Row("CustomAction","ProvisionSharedSettings",3090,"ScanBridgeExe","--install");
        Table("LaunchCondition",new[]{"`Condition` CHAR(255) NOT NULL","`Description` CHAR(0) NOT NULL LOCALIZABLE"},new[]{"Condition","Description"},"`Condition`");
        Row("LaunchCondition","Privileged","Administrator permission is required to install ScanBridge for all users.");
        Table("AppSearch",new[]{"`Property` CHAR(72) NOT NULL","`Signature_` CHAR(72) NOT NULL"},new[]{"Property","Signature_"},"`Property`, `Signature_`");
        Table("RegLocator",new[]{"`Signature_` CHAR(72) NOT NULL","`Root` SHORT NOT NULL","`Key` CHAR(255) NOT NULL","`Name` CHAR(255)","`Type` SHORT"},new[]{"Signature_","Root","Key","Name","Type"},"`Signature_`");
        Table("Signature",new[]{"`Signature` CHAR(72) NOT NULL","`FileName` CHAR(255) NOT NULL","`MinVersion` CHAR(20)","`MaxVersion` CHAR(20)","`MinSize` LONG","`MaxSize` LONG","`MinDate` LONG","`MaxDate` LONG","`Languages` CHAR(255)"},new[]{"Signature","FileName","MinVersion","MaxVersion","MinSize","MaxSize","MinDate","MaxDate","Languages"},"`Signature`");
        Row("Signature","Net48Clr","clr.dll","4.8.0.0",null,null,null,null,null,null);
        Row("RegLocator","Net48Clr",2,@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full","InstallPath",0);
        Row("AppSearch","NET48FILE","Net48Clr");
        Row("LaunchCondition","Installed OR NET48FILE","Install .NET Framework 4.8 or later before installing ScanBridge.");
        Table("Upgrade",new[]{"`UpgradeCode` CHAR(38) NOT NULL","`VersionMin` CHAR(20)","`VersionMax` CHAR(20)","`Language` CHAR(255)","`Attributes` LONG NOT NULL","`Remove` CHAR(255)","`ActionProperty` CHAR(72) NOT NULL"},new[]{"UpgradeCode","VersionMin","VersionMax","Language","Attributes","Remove","ActionProperty"},"`UpgradeCode`, `VersionMin`, `VersionMax`, `Language`, `Attributes`");
        Row("Upgrade",UpgradeCode,"0.0.0","0.5.2",null,256,null,"OLDPRODUCTS");
        Row("Upgrade",UpgradeCode,"0.5.2",null,null,2,null,"NEWERPRODUCTS");
        Row("LaunchCondition","NOT NEWERPRODUCTS","A newer version of ScanBridge is already installed.");
        foreach(string sequence in new[]{"InstallExecuteSequence","InstallUISequence"})
            Table(sequence,new[]{"`Action` CHAR(72) NOT NULL","`Condition` CHAR(255)","`Sequence` SHORT"},new[]{"Action","Condition","Sequence"},"`Action`");
        string[] actions={"FindRelatedProducts","ValidateProductID","CostInitialize","FileCost","CostFinalize","MigrateFeatureStates","InstallValidate","InstallInitialize","RemoveExistingProducts","ProcessComponents","UnpublishFeatures","RemoveRegistryValues","RemoveShortcuts","RemoveFiles","RemoveFolders","CreateFolders","InstallFiles","CreateShortcuts","WriteRegistryValues","ProvisionSharedSettings","RegisterUser","RegisterProduct","PublishFeatures","PublishProduct","InstallFinalize"};
        int[] order={25,700,800,900,1000,1200,1400,1500,1510,1600,1800,2600,3200,3500,3600,3700,4000,4500,5000,5100,6000,6100,6300,6400,6600};
        for(int i=0;i<actions.Length;i++)Row("InstallExecuteSequence",actions[i],actions[i]=="ProvisionSharedSettings"?"NOT (REMOVE ~= \"ALL\")":null,order[i]);
        Row("InstallExecuteSequence","LaunchConditions",null,100);
        Row("InstallExecuteSequence","AppSearch",null,50);
        foreach(string action in new[]{"FindRelatedProducts","CostInitialize","FileCost","CostFinalize"})
            Row("InstallUISequence",action,null,action=="FindRelatedProducts"?25:action=="CostInitialize"?800:action=="FileCost"?900:1000);
        Row("InstallUISequence","LaunchConditions",null,100);Row("InstallUISequence","ExecuteAction",null,1300);
        Row("InstallUISequence","AppSearch",null,50);
        uint summary;Check(MsiGetSummaryInformation(database,null,20,out summary),"Summary");
        try
        {
            SetSummary(summary,1,2,1252,null);SetSummary(summary,2,30,0,"ScanBridge Setup");SetSummary(summary,3,30,0,"ScanBridge Windows scanner application");
            SetSummary(summary,4,30,0,"ScanBridge");SetSummary(summary,7,30,0,"Intel;1033");SetSummary(summary,9,30,0,Guid.NewGuid().ToString("B").ToUpperInvariant());
            SetSummary(summary,14,3,500,null);SetSummary(summary,15,3,2,null);SetSummary(summary,19,3,2,null);
            Check(MsiSummaryInfoPersist(summary),"Save summary");
        }
        finally{MsiCloseHandle(summary);}
        Check(MsiDatabaseCommit(database),"Commit");MsiCloseHandle(database);database=0;
        Check(MsiVerifyPackage(output),"Verify MSI format");
    }
    static void SetSummary(uint handle,uint property,uint type,int value,string text)
    {Check(MsiSummaryInfoSetProperty(handle,property,type,value,IntPtr.Zero,text),"Set summary");}
}



