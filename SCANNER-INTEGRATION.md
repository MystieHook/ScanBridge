# Scanner integration findings — 2026-11-09

## Update: physical-button routing in 0.4

The user confirmed application-driven scanning worked but physical-button scans still went through Home's Quick Menu. Inspection confirmed the selected device profile was Quick Menu even though automatic-linkage notifications returned 0. Selecting the existing ScanBridge profile through Home's controls and closing the scan panel saved the correct device selection.

The 0.4 adapter performs that selection through Windows UI Automation in the background. It reads generated AppId/profile IDs only to identify the matching UI item and verify the result; no vendor XML is edited. The live regression test started from Quick Menu and succeeded in saving ScanBridge. Hardware-button acquisition should be retested by the user. Fresh-account initialization and compatibility with other Home versions are separate acceptance tests.

## Update: implemented integration and registration checks

ScanBridge 0.2 is installed under `C:\Program Files (x86)\ScanBridge`. The registered extension points to `C:\ProgramData\ScanBridge\ScanBridge.ini`. Home accepted Reserve and Release for the ScanBridge extension, and automatic-linkage start/end notifications returned status 0. No paper acquisition was performed because the user chose to test physical scanning afterward.

The application implements scan commands, callback import, image processing, PDF export, review/recovery, and all-user setup. Synthetic tests passed, including failure/retry of destination delivery; the sample PDF parsed and rendered correctly. Physical acquisition, physical-button routing, and fresh-account behavior remain acceptance tests rather than confirmed compatibility claims.

## Verified on this computer

ScanSnap Home 4.1.0.5 answers the legacy `GetScannerInfo` command described in Ricoh's public Interface Guide. The live response reports one connected scanner named `ScanSnap iX1400`. This is stronger evidence than USB registry enumeration.

Test: `tools/HomeConnectionProbe.cs`, compiled with Microsoft's C# compiler. It locates `ScanSnap Manager MainWndClass` and sends `WM_COPYDATA` with identifier 5, a 264-byte, 4-byte-aligned payload containing mode 0 and an ANSI output path. The response returned status 0 and an INI with ScannerCount=1. No scan was started and no application registration or profile was changed.

The sandbox blocks cross-process messages (Win32 error 5). Running the approved probe outside the sandbox succeeds. This restriction is separate from scanner connectivity. Normal deployment should run the application and Home in the same user's interactive session, with matching elevation.

The WPF application now includes this connection query. It runs asynchronously on load/refresh and reports a live scanner name separately from installation diagnostics. `ScanBridge.exe --connection-report <json-path>` runs the same backend without displaying a window. Device identifiers are omitted from the JSON.

## Most practical route with Home installed

Use the documented desktop extension interface, starting with the compatibility demonstrated above. The public guide describes machine-wide registration under the 32-bit registry view of `HKLM\Software\PFU\ScanSnap Extension`, a linkage INI containing scan restrictions, automatic application linkage, Reserve/Release, StartScan, and a result-file callback.

Source: https://origin.pfultd.com/downloads/IMAGE/manual/sv600/P2WW-3030-12ENZ0.pdf

This guide targets ScanSnap Manager, not current Home. Only the status-query command has been verified against Home so far. The installed `PfuSsMonSdk.exe` refers to the same window class; the existing Home Web SDK is itself registered under the same extension registry branch. Those observations support further testing; they do not prove every legacy feature remains supported.

Remaining proof steps:

1. Register a uniquely named ScanBridge extension with our own linkage INI and result receiver; do not modify existing extensions.
2. Confirm Home recognizes the extension after a normal restart.
3. Test activation and a programmatic scan, then a physical-button scan. Check resulting page data and requested scan settings.
4. Test automatic initialization in a fresh standard Windows account and after switching users. Do not assume machine-wide registration eliminates all per-user state.
5. Validate the iX2400 separately when hardware is available.

## Alternative routes researched

### SnapTwain

Vendor lists both iX1400 and iX2400, supports silent application-controlled acquisition, and offers a watermarked evaluation. It requires ScanSnap Home operational. Physical-button routing and unattended per-user initialization still require verification; TWAIN support alone does not establish them. A commercial fallback, rather than an independent driver replacement.

https://www.jse.de/scansnap-twain-driver.html

### holgerkrupp/Scan

MIT-licensed native USB implementation for macOS. The source recognizes iX1400 (PID 0x1630) using an iX1600-derived profile. Its iX1600 is hardware-validated, while iX1400 is explicitly unvalidated. This is a stronger USB reference than ScanSnack, but needs transport adaptation and hardware tests on Windows. iX2400 support is not established by the documented model table.

https://github.com/holgerkrupp/Scan
https://github.com/holgerkrupp/Scan/blob/main/LICENSE

### SANE fujitsu backend

Lists iX1400 USB VID/PID 04c5:1630 as untested. A possible protocol reference, not a validated native Windows integration for our hardware. No driver replacements have been installed.

https://www.sane-project.org/sane-backends.html

### ScanSnack

Network-only implementation, supports iX1500/iX500. Not a USB backend for either target model.

https://github.com/elte0/scansnack/blob/main/docs/models.md
