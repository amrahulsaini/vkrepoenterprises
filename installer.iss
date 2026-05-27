; CRMS — Inno Setup installer script
; Build: open in Inno Setup Compiler → Compile (Ctrl+F9)
; Output: installer-output\<OutputBaseFilename>.exe
;
; Defaults to the generic CRMS build. tools/build_wpf_all.py compiles this
; once per agency with /D defines so each tenant gets a side-by-side install:
;
;   ISCC.exe installer.iss ^
;     /DAppName="V K Enterprises" ^
;     /DAgencyGuid=ABCDEF12-... ^
;     /DPublishDir=VKdesktopapp\publish\v_k_enterprises ^
;     /DOutputBaseFilename=VKEnterprises_Setup
;
; AgencyGuid is a deterministic GUID derived from the slug — same slug
; always produces the same GUID so re-runs reinstall over the same entry
; in Add/Remove Programs, but different slugs do NOT collide.

#ifndef AppName
  #define AppName    "CRMS"
#endif
#ifndef AppVersion
  #define AppVersion "1.0"
#endif
#ifndef AppExe
  #define AppExe     "CRMRS.exe"
#endif
#ifndef PublishDir
  #define PublishDir "VKdesktopapp\publish-fresh"
#endif
#ifndef AgencyGuid
  ; Default GUID — used when no /DAgencyGuid is passed (generic CRMS build).
  #define AgencyGuid "9F4A1B2C-5E3D-4F8A-B7C2-1D2E3F4A5B6C"
#endif
#ifndef OutputBaseFilename
  #define OutputBaseFilename "CRMS_Setup"
#endif

[Setup]
; AgencyGuid -> AppId. Side-by-side installs across tenants because each
; tenant's slug hashes to its own GUID.
;
; Inno parses `{{` as a literal `{`, and `}` is literal when not closing a
; constant. The preprocessor expression `{#AgencyGuid}` consumes a `{` and a
; `}`, so we need 3 `{` and 2 `}` to emit the final `{GUID}` AppId value.
AppId={{{#AgencyGuid}}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=CRMS
AppPublisherURL=https://crmrecoverysoftware.com
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir=installer-output
OutputBaseFilename={#OutputBaseFilename}
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
; DLLs, public/ HTML assets, and Resources/branding.json (when present).
; No separate .NET install needed.
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
FinishedLabel={#AppName} has been installed successfully.%n%nTo pin it to your taskbar: right-click the Desktop shortcut and choose "Pin to taskbar".
