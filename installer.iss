; VK Enterprises — Inno Setup installer script
; Build: open in Inno Setup Compiler → Compile (Ctrl+F9)
; Output: installer-output\VKEnterprises_Setup.exe

#define AppName    "VK Enterprises"
#define AppVersion "1.0"
#define AppExe     "VRASDesktopApp.exe"
#define PublishDir "VKdesktopapp\publish-fresh"

[Setup]
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=VK Enterprises
AppPublisherURL=https://vkenterprises.com
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir=installer-output
OutputBaseFilename=VKEnterprises_Setup
SetupIconFile=VKdesktopapp\public\favicon.ico
UninstallDisplayIcon={app}\{#AppExe}
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
FinishedLabel=VK Enterprises has been installed successfully.%n%nTo pin it to your taskbar: right-click the Desktop shortcut and choose "Pin to taskbar".
