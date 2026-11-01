; Inno Setup script for the TTI HTXB Tools & Utilities suite.
;
; Build with:  build-installer.ps1   (or)
;              "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" installer\installer.iss
;
; The payload is dist\ : the nine per-tool EXEs at the root, the shared data2
; folder and the configuration seed. Everything the tools need sits next to the
; EXEs, so the install folder is self-contained (`AppContext.BaseDirectory`).
;
; Modeled on ..\_Proj_BidfGen2\deploy\installer.iss: same AppId/registry
; conventions, same "never overwrite the site's settings" rule, same silent
; switches. Differences: no services or logon tasks (nothing runs in the
; background), and the uninstaller removes the program files because this suite
; keeps no site data of its own apart from configuration\ and data2\assets\dut_package.

; Version shown in "Apps & features" - injected by build-installer.ps1 as the
; NEWEST tool version in .data2\manifest.json (each EXE keeps its own version,
; so the suite number is the highest one it contains).
#ifndef AppVer
  #define AppVer "V0.0.000"
#endif
#ifndef AppNum
  #define AppNum "0.0.0.0"
#endif
#ifndef AppGroup
  #define AppGroup "Tools & Utilities"
#endif
#ifndef OutName
  #define OutName "TTI_Utilities_Setup_" + AppVer
#endif
#ifndef AppId
  #define AppId "{{B1C7A4E2-5D38-4F06-9A7B-3E2C8D1F4A60}"
#endif
; Elevated by default: the default folder is under C:\ and the install is
; machine-wide. /DPriv=lowest builds a per-user test copy.
#ifndef Priv
  #define Priv "admin"
#endif
; Silence + switch names for the uninstall-then-install reinstall path.
#ifndef InstallDir
  #define InstallDir "C:\TTI_PostDataProcessor\_TTI_Utilities"
#endif

#define MyAppName "TTI " + AppGroup + " " + AppVer
#define MyAppGroupName "TTI HTXB " + AppGroup
#define MyAppPublisher "Trio-Tech International"
#define MyAppExeName "TTI_Utils_Launcher.exe"
#define PayloadDir "..\dist"

[Setup]
AppId={#AppId}
AppName={#MyAppName}
AppVersion={#AppVer}
AppVerName={#MyAppName}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#AppNum}
DefaultDirName={#InstallDir}
DefaultGroupName={#MyAppGroupName}
DisableProgramGroupPage=yes
OutputDir=..\installer\out
OutputBaseFilename={#OutName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired={#Priv}
UninstallDisplayIcon={app}\{#MyAppExeName}
; The launcher is a resident, auto-hiding corner window; closing the wizard
; leaves it running.
AllowNoIcons=yes
SetupLogging=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut for the launcher"; GroupDescription: "Shortcuts:"; Flags: checkedonce

[Files]
; Payload: the nine EXEs at the root (no DLLs - single-file publish) plus the
; shared data2 folder. overwritereadonly keeps republished single-file EXEs
; replaceable even after WebView2 marked one read-only.
Source: "{#PayloadDir}\*.exe"; DestDir: "{app}"; Flags: ignoreversion overwritereadonly
Source: "{#PayloadDir}\data2\*"; DestDir: "{app}\data2"; Flags: ignoreversion recursesubdirs createallsubdirs overwritereadonly

; ---- Site settings: seeded once, never overwritten afterwards --------------
; configuration\tools.ini remembers each tool's last folder (LastDir=);
; configuration\default_dir.ini is written by the operator and only ships when
; the repo has one (skipifsourcedoesntexist keeps a bare checkout buildable).
Source: "{#PayloadDir}\configuration\tools.ini";       DestDir: "{app}\configuration"; Flags: onlyifdoesntexist skipifsourcedoesntexist
Source: "{#PayloadDir}\configuration\default_dir.ini"; DestDir: "{app}\configuration"; Flags: onlyifdoesntexist skipifsourcedoesntexist

; ---- User data: DUT package images are the site's, not the release's -------
; Seeded only when missing so a republish can never delete a custom image.
; (AGENTS.md "DUT package image rule".)
Source: "{#PayloadDir}\data2\assets\dut_package\*"; DestDir: "{app}\data2\assets\dut_package"; Flags: onlyifdoesntexist recursesubdirs createallsubdirs skipifsourcedoesntexist

[Icons]
Name: "{group}\{#MyAppGroupName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppGroupName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppGroupName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Start {#MyAppGroupName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Close a running launcher/tool first: Windows locks a running EXE and the
; uninstaller must be able to delete it.
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#MyAppExeName} /F"; Flags: runhidden; RunOnceId: "StopLauncher"
Filename: "{sys}\taskkill.exe"; Parameters: "/IM TTI_BiDFAnalysis.exe /F"; Flags: runhidden; RunOnceId: "StopBiDF"
Filename: "{sys}\taskkill.exe"; Parameters: "/IM TTI_LotExtractor.exe /F"; Flags: runhidden; RunOnceId: "StopLot"
Filename: "{sys}\taskkill.exe"; Parameters: "/IM TTI_TempUniformity.exe /F"; Flags: runhidden; RunOnceId: "StopTemp"
Filename: "{sys}\taskkill.exe"; Parameters: "/IM TTI_RecipeTranslator.exe /F"; Flags: runhidden; RunOnceId: "StopRecipe"
Filename: "{sys}\taskkill.exe"; Parameters: "/IM TTI_RcpStdTable.exe /F"; Flags: runhidden; RunOnceId: "StopRcpStd"
Filename: "{sys}\taskkill.exe"; Parameters: "/IM TTI_P2DataAnalysis.exe /F"; Flags: runhidden; RunOnceId: "StopP2"
Filename: "{sys}\taskkill.exe"; Parameters: "/IM TTI_P2DataAnalysisTime.exe /F"; Flags: runhidden; RunOnceId: "StopP2Time"
Filename: "{sys}\taskkill.exe"; Parameters: "/IM TTI_BarcodeGenerator.exe /F"; Flags: runhidden; RunOnceId: "StopBarcode"

[Code]
// Stop every suite EXE before the files are replaced. An upgrade lands on top
// of a folder whose EXEs are usually idle, but the launcher is resident by
// design and a tool may be open, so this runs on install as well as uninstall.
procedure StopSuite;
var
  Rc: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#MyAppExeName} /F', '', SW_HIDE, ewWaitUntilTerminated, Rc);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  NeedsRestart := False;
  StopSuite;
end;
