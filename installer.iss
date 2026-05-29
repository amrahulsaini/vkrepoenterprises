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
; IconFilename pins the per-agency logo (app-icon.ico — copied into the publish
; folder by tools/build_wpf_local.py from THIS agency's favicon.ico) onto every
; shortcut. The EXE already embeds the same icon, but pointing the shortcuts at
; the loose .ico is a belt-and-suspenders guarantee that VK shows the VK logo
; and RK shows the RK logo even if the embedded icon ever regressed.
; Start Menu
Name: "{group}\{#AppName}";     Filename: "{app}\{#AppExe}"; IconFilename: "{app}\app-icon.ico"
Name: "{group}\Uninstall";      Filename: "{uninstallexe}"
; Desktop shortcut
Name: "{commondesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; IconFilename: "{app}\app-icon.ico"

[Run]
; Launch option on the finish page
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName} now"; Flags: nowait postinstall skipifsilent

[Messages]
FinishedLabel={#AppName} has been installed successfully.%n%nA Desktop shortcut was created and we tried to pin it to your taskbar. If you don't see it on the taskbar, right-click the Desktop shortcut and choose "Pin to taskbar".

[Code]
// Best-effort taskbar pin after install. Microsoft removed the public
// "pin to taskbar" API on Windows 10 1903+ / Windows 11, so there is no
// guaranteed programmatic way to pin. This tries the Shell verb (works on
// some builds / when policy allows) and silently no-ops otherwise — the
// Desktop + Start Menu shortcuts created in [Icons] are the reliable path.
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  Exe, Ps: String;
begin
  if CurStep = ssPostInstall then
  begin
    Exe := ExpandConstant('{app}\{#AppExe}');
    // PowerShell: locate the .exe via Shell.Application, find any verb whose
    // (ampersand-stripped) name contains "taskbar", and invoke it.
    Ps :=
      '$ErrorActionPreference=''SilentlyContinue'';' +
      '$exe=''' + Exe + ''';' +
      '$sh=New-Object -ComObject Shell.Application;' +
      '$dir=Split-Path $exe;' +
      '$leaf=Split-Path $exe -Leaf;' +
      '$item=$sh.Namespace($dir).ParseName($leaf);' +
      '$v=$item.Verbs()|Where-Object {($_.Name -replace ''&'','''') -match ''taskbar''}|Select-Object -First 1;' +
      'if($v){$v.DoIt()}';
    Exec('powershell.exe',
      '-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -Command "' + Ps + '"',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
