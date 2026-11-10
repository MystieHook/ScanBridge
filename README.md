# ScanBridge 0.5.2

Native C# / WPF application for ScanSnap Home, targeting .NET Framework 4.8. No PowerShell or Python is needed by the delivered application.

## Run and test

Install `dist\ScanBridge-0.5.2.msi`, then open ScanBridge from the Start menu. The installed executable is `C:\Program Files (x86)\ScanBridge\ScanBridge.exe`. The MSI registers all-user startup, scanner integration, and Control Panel uninstall. Shared settings under `C:\ProgramData\ScanBridge` are preserved on upgrade and uninstall. ScanSnap Home must be installed separately. See `installer\README.md` for deployment commands.

### Physical-button routing fix

Home's legacy automatic-linkage notification can return success while its active physical-button profile remains Quick Menu. ScanBridge now identifies its profile through Home's generated extension AppId, reads the current scanner's profile order, and selects that entry using Home's existing UI Automation controls. It closes the hidden scan panel normally to persist selection, and verifies the saved selection. It never rewrites Home's profile XML, replaces another user's identity, or holds Reserve indefinitely. Ambiguous profile mapping or an unexpected UI structure fails explicitly instead of selecting an arbitrary profile.

The check runs on startup, after scans, and periodically while the tray application is running. Current-user profile initialization uses a brief vendor Reserve/Release if Home has not yet generated that user's ScanBridge profile. This adapter is validated against the installed Home UI; substantially changed Home versions may require an adapter update.

Live routing test: temporarily selected Quick Menu, closed the panel to save it, ran the background routing helper, and verified that Home selected and saved ScanBridge. No scan was started in that test. The next acceptance step is a physical-button scan into the configured output folder.

ScanBridge checks Home's scanner-monitor readiness on startup, refresh, and before scanning. If needed it launches only `PfuSsMon.exe` in the current user's session with a hidden window, no shell, and the correct working directory. It does not launch Home's main interface or connection wizard. It waits asynchronously up to 60 seconds and shares concurrent startup attempts. The tray heartbeat reconnects if the monitor stops. While ScanBridge runs, its window guard hides the known Home library and Scan windows in this session, including ones Home opens automatically. It does not click through or dismiss vendor error/update/first-run dialogs; those exceptional vendor flows are not guaranteed to be unattended.

The live startup diagnostic reported `wasRunning=false`, `monitorReady=true`, and one connected iX1400. No scan was started during this test.

First-time extension setup may gracefully reload Home's monitor so it discovers the registration. Normal app startup and scanning do not restart an already-running monitor. Saving the shared profile no longer restarts Home. Home and ScanBridge must run under the same signed-in user; the settings helper alone runs elevated.

Choose a destination in ScanBridge and **Save shared profile**. Standard users can read the profile; changing it requests administrator permission. Each intended user must have access to the destination. A per-user OneDrive path or mapped drive may not be accessible to other accounts; use a shared local folder or UNC network path for shared destinations.

Load a non-sensitive test sheet and choose **Scan now**. Check the output PDF. Test the physical scanner button separately with ScanBridge in the tray. Automatic linkage is requested through the vendor API, but physical-button routing and fresh-user initialization still require hardware/session acceptance tests.

Closing the window keeps the application in the tray. Use its tray menu to exit. A single UI instance runs per Windows session. Application-driven acquisitions use a cross-session mutex, and release vendor control afterward.

## Implemented

- All-user registration/startup and administrator-controlled shared profile.
- Asynchronous live scanner query, vendor Reserve / StartScan / Release workflow, and automatic-linkage notifications.
- JPEG result-file receiver, multipage PDF export, unique filenames, duplex/quality acquisition settings, host-side grayscale, conservative blank-page filtering, rotation, and basic bounded deskew.
- Review window with page preview, rotate, hide/include, and export actions.
- Per-user recent scans and recoverable raw pages. Destination failures keep the job for retry.
- Setup and settings changes occur in our application, without editing undocumented per-user Home XML.

## Recovery and data retention

Jobs and raw pages are under `%LOCALAPPDATA%\ScanBridge\Jobs`. The shared profile is not a shared document archive. Users do not automatically see each other's recovery files. Raw pages and vendor temporary inputs are retained during the integration pilot; automatic retention cleanup is not implemented.

All-blank batches open Review instead of silently producing an empty PDF. Hidden pages remain recoverable. Import failures retain vendor callback metadata in the job directory.

## Validation and limits

The live iX1400 status query was confirmed. Home also accepted ScanBridge's registration (Reserve/Release) and automatic-linkage start/end notifications, all without starting a scan. Synthetic tests exercise import, page processing, PDF generation, source preservation, configuration validation, and recovery after a blocked destination. The generated PDF is independently parsed and rendered.

The user confirmed scanning and physical-button operation with the iX1400. Arbitrary Home versions, iX2400 hardware, fresh-user initialization, and the new opposite-side simplex option still need acceptance tests. The public legacy API guide targets ScanSnap Manager; compatibility with Home is established command by command.

## Development

Open ScanBridge.sln in Visual Studio with .NET desktop development and .NET Framework 4.8 targeting tools. There are no NuGet dependencies.

Useful diagnostic commands:

- `ScanBridge.exe --self-test`
- `ScanBridge.exe --connection-report C:\Scansnap\connection-report.json`
- `ScanBridge.exe --button-route-report C:\Scansnap\button-route-report.txt` (selects/saves the ScanBridge button profile using Home's hidden controls; does not scan)
- `ScanBridge.exe --ensure-home-report C:\Scansnap\startup-report.json` (starts the monitor hidden if necessary, then queries it; does not scan)
- `ScanBridge.exe --integration-check C:\Scansnap\integration-check.txt` (briefly reserves/releases control; does not scan)
- `ScanBridge.exe --link-check C:\Scansnap\link-check.txt` (sends start/end linkage notification; does not scan)
- `ScanBridge.exe --render C:\Scansnap\preview.png`

See SCANNER-INTEGRATION.md for the researched integration sources. Developer tools are retained as C# source under installer and tools; compile them before use. Obsolete builds, prototype scripts, and generated diagnostics have been removed.

## Version 0.5.2

Includes a multi-size scanner/bridge icon for the executable, windows, tray, Start menu and installer. For single-sided scanning, disable Scan both sides and choose printed-side placement. The toward-you option acquires both sensors and retains only back-side pages; verify front/back ordering and orientation with a test sheet before deployment. Original pages remain recoverable. The MSI is currently unsigned.




## Output filenames

The shared profile now stores filename format, optional prefix, custom name, and template. Presets include yyyy-MM-dd-HH-mm-ss, yyyy-MM-dd_HH-mm-ss, yyyyMMdd-HHmmss, and date only. Templates support {prefix}, {name}, {yyyy}, {MM}, {dd}, {HH}, {mm}, {ss}. Times use the local scan import timestamp, preserved when reviewing or recovering a job. Windows filename separators / and : are rejected; use hyphens and underscores. Duplicate output names add (2), (3), etc. Atomic moves prevent overwriting existing documents, including concurrent exports. Older profiles load default naming values.

Version 0.5.2 validation passed: naming presets/templates, legacy-profile defaults, profile persistence, invalid/reserved filename rejection, concurrent duplicate naming without overwrite, PDF export and recovery, WPF render, and MSI upgrade/install/uninstall/reinstall with shared configuration preserved. No hardware scan was started for this change.
