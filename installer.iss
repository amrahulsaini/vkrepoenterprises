; CRMS — Inno Setup installer script
; Build: open in Inno Setup Compiler → Compile (Ctrl+F9)
; Output: installer-output\CRMS_Setup.exe

#define AppName    "CRMS"
#define AppVersion "1.0"
#define AppExe     "VRASDesktopApp.exe"
#define PublishDir "VKdesktopapp\publish-fresh"

[Setup]
; Stable GUID — keep across future versions so upgrades replace the same install.
AppId={{9F4A1B2C-5E3D-4F8A-B7C2-1D2E3F4A5B6C}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=CRMS
AppPublisherURL=https://crmrecoverysoftware.com
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir=installer-output
OutputBaseFilename=CRMS_Setup
SetupIconFile=VKdesktopapp\public\favicon.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

[Files]
; Whole self-contained publish folder — app exe, bundled .NET runtime, all
; DLLs and the public/ HTML assets. No separate .NET install needed.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Start Menu
Name: "{group}\{#AppName}";     Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall";      Filename: "{uninstallexe}"
; Desktop shortcut
Name: "{commondesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"

[Run]
; Launch option on the finish page
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName} now"; Flags: nowait postinstall skipifsilent

[Messages]
FinishedLabel=CRMS has been installed successfully.%n%nTo pin it to your taskbar: right-click the Desktop shortcut and choose "Pin to taskbar".
