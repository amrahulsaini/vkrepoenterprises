; VK Enterprises — Inno Setup installer script
; Build: open in Inno Setup Compiler → Compile (Ctrl+F9)
; Output: installer-output\VKEnterprises_Setup.exe

#define AppName    "VK Enterprises"
#define AppVersion "1.0"
#define AppExe     "VRASDesktopApp.exe"
#define PublishDir "VKdesktopapp\publish-output"

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
; Self-contained app exe (includes .NET runtime — no separate install needed)
Source: "{#PublishDir}\{#AppExe}";          DestDir: "{app}"; Flags: ignoreversion
; WebView2 HTML pages used inside the app
Source: "{#PublishDir}\public\*";            DestDir: "{app}\public"; Flags: ignoreversion recursesubdirs createallsubdirs

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
