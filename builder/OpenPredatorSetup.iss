; OpenPredator Windows Inno Setup Script
#define MyAppName "OpenPredator"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "KRWCLASSIC"
#define MyAppURL "https://github.com/KRWCLASSIC/OpenPredator"
#define MyAppExeName "openpredator.exe"

[Setup]
AppId={{E5D4B2A1-8890-4C32-B8E5-7A19F8D62001}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
AppContact={#MyAppPublisher}
AppCopyright=Copyright (C) 2026 {#MyAppPublisher}
VersionInfoDescription=OpenPredator - Hardware Control Suite for Acer Laptops
VersionInfoProductName={#MyAppName}
VersionInfoCompany={#MyAppPublisher}
VersionInfoCopyright=Copyright (C) 2026 {#MyAppPublisher}
VersionInfoVersion=1.0.0.1
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\dist\installer
OutputBaseFilename=OpenPredator-v{#MyAppVersion}-win-x64-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}
ChangesEnvironment=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Types]
Name: "full"; Description: "Full Installation (Service Daemon & CLI Utility) [Recommended]"
Name: "daemon"; Description: "Service Daemon Only (Background ACPI WMI Daemon)"
Name: "cli"; Description: "CLI Tool Only (Compatible with PSSvc, OpenPredator Daemon, or Direct Hardware Mode)"
Name: "custom"; Description: "Custom Installation"; Flags: iscustom

[Components]
Name: "daemon"; Description: "OpenPredator Background Service (Auto-starts on boot, 1:1 OEM IPC wire)"; Types: full daemon custom
Name: "cli"; Description: "OpenPredator CLI Utility (openpredator.exe, integrated with System PATH)"; Types: full cli custom

[Files]
Source: "..\dist\win-x64\openpredator-service.exe"; DestDir: "{app}"; Flags: ignoreversion; Components: daemon
Source: "..\dist\win-x64\openpredator.exe"; DestDir: "{app}"; Flags: ignoreversion; Components: cli
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; DestName: "README.txt"; Flags: ignoreversion

[Run]
; Stop any running OpenPredator service before configuring
Filename: "{sys}\sc.exe"; Parameters: "stop OpenPredator"; Flags: runhidden; Components: daemon
; Create and start Windows Service if daemon component is selected
Filename: "{sys}\sc.exe"; Parameters: "create OpenPredator binPath= ""{app}\openpredator-service.exe --run"" start= auto DisplayName= ""OpenPredator Service"""; Flags: runhidden; Components: daemon
Filename: "{sys}\sc.exe"; Parameters: "description OpenPredator ""High-performance, zero-bloat hardware control daemon for Acer gaming laptops"""; Flags: runhidden; Components: daemon
Filename: "{sys}\sc.exe"; Parameters: "start OpenPredator"; Flags: runhidden; Components: daemon

[UninstallRun]
; Stop and remove Windows Service
Filename: "{sys}\sc.exe"; Parameters: "stop OpenPredator"; Flags: runhidden; RunOnceId: "StopOpenPredator"
Filename: "{sys}\sc.exe"; Parameters: "delete OpenPredator"; Flags: runhidden; RunOnceId: "DeleteOpenPredator"

[Code]
const
  EnvironmentKey = 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment';

procedure AddPath(PathToAdd: string);
var
  CurrentPath: string;
begin
  if RegQueryStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path', CurrentPath) then
  begin
    if Pos(';' + UpperCase(PathToAdd) + ';', ';' + UpperCase(CurrentPath) + ';') = 0 then
    begin
      CurrentPath := CurrentPath + ';' + PathToAdd;
      RegWriteStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path', CurrentPath);
    end;
  end;
end;

procedure RemovePath(PathToRemove: string);
var
  CurrentPath: string;
  P: Integer;
begin
  if RegQueryStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path', CurrentPath) then
  begin
    P := Pos(';' + UpperCase(PathToRemove) + ';', ';' + UpperCase(CurrentPath) + ';');
    if P > 0 then
    begin
      Delete(CurrentPath, P - 1, Length(PathToRemove) + 1);
      RegWriteStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path', CurrentPath);
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if WizardIsComponentSelected('cli') then
      AddPath(ExpandConstant('{app}'));
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RemovePath(ExpandConstant('{app}'));
  end;
end;
