; Hermes English Learning — Inno Setup installer (Windows 7+)
; Build:  powershell -File Installer\build-installer.ps1

#define MyAppName "Hermes English Learning"
#define MyAppVersion "1.1.3"
#define MyAppPublisher "DebuggerPlus"
#define MyAppExeName "Hermes.EnglishLearning.exe"
#ifndef SourceDir
  #define SourceDir "..\Hermes.EnglishLearning\bin\Release\net48"
#endif
#ifndef OutDir
  #define OutDir "output"
#endif

[Setup]
AppId={{A8E7C3F1-9B42-4D6E-9F21-7C8E2A1B0D55}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Hermes.EnglishLearning
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir={#OutDir}
OutputBaseFilename=HermesEnglishLearning-Setup-{#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
; Windows 7
MinVersion=6.1sp1
PrivilegesRequired=lowest
ArchitecturesAllowed=x86compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupLogging=yes
CloseApplications=force
RestartApplications=no
; Soft close often fails when the app is hung / waiting for JIT debugger.

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Запускать English Learning при входе в Windows / Start with Windows"; GroupDescription: "Автозагрузка:"; Flags: checkedonce

[Files]
; Staged publish folder prepared by build-installer.ps1 (no logs/cache/secrets)
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
; userdesktop — без админа; commondesktop (Public) даёт 0x80070005 на Win7
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
; Per-user Startup folder (works on Win7 without admin)
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: autostart

[Registry]
; Also register Run key when autostart task selected (backup for Startup folder)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "HermesEnglishLearning"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
; Launch immediately after install
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\logs"
Type: filesandordirs; Name: "{app}\tts-cache"

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
end;

procedure ForceKillApp;
var
  ResultCode: Integer;
begin
  { /F = force, /T = kill child processes. Safe if app is not running. }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#MyAppExeName} /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(800);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  ForceKillApp;
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  ForceKillApp;
  Result := True;
end;
