# MSI packaging

`BuildMsi.cs` builds a real, embedded-cabinet Windows Installer database using the Windows Installer API and Windows makecab. No third-party installer compiler or network access is required. Build the application first with Visual Studio / MSBuild, compile the builder with the .NET Framework C# compiler, then pass the application executable and a new output MSI path to the builder.

The package installs per machine to Program Files (x86)\ScanBridge, includes an all-user Start menu shortcut, registers the ScanSnap extension and startup Run entry, and uses Windows Installer's Control Panel/Installed Apps registration. Shared settings are provisioned by the app's noninteractive administrator helper. Uninstall removes MSI-owned files, shortcuts and registry values, while preserving ProgramData configuration and per-user recovery jobs/scans.

Product version: 0.5.2

Product code: `{BD693DF7-64AA-4C43-A056-48EA5550A684}`

Upgrade code: `{5327CD0F-5768-409B-964B-31B6E4037F13}`

Keep component GUIDs stable for the same resource paths. A future major upgrade must use a new ProductCode, higher ProductVersion and the same UpgradeCode. Rebuilds of the same version retain ProductCode and receive a new package code.

The MSI checks for .NET Framework 4.8+ through the installed CLR file version. ScanSnap Home must be installed separately; proprietary vendor drivers are not bundled. The package has not been digitally signed.

Install:

```text
msiexec /i ScanBridge-0.5.2.msi
```

Silent deployment:

```text
msiexec /i ScanBridge-0.5.2.msi /qn /norestart /L*v ScanBridge-install.log
```

Uninstall:

```text
msiexec /x {BD693DF7-64AA-4C43-A056-48EA5550A684}
```

Exit code 0 means success; 3010 means success with a restart required. Close ScanBridge and finish any scan/review callbacks before uninstalling or replacing the application. No user document directories are recursively removed by this package. Older manually installed version folders are preserved because Windows Installer does not own them.


