; OpenPredator Windows Inno Setup Script
#define MyAppName "OpenPredator"
#ifndef MyAppVersion
#define MyAppVersion "Undefined"
#endif
#ifndef VersionInfoVersion
#define VersionInfoVersion "0.0.0.0"
#endif
#ifndef SourceDir
#define SourceDir "..\dist\release\win-x64"
#endif
#ifndef OutputFilename
#define OutputFilename "OpenPredator-v" + MyAppVersion + "-win-x64-Setup"
#endif
#ifndef IsDev
#define IsDev "0"
#endif
#define MyAppPublisher "KRWCLASSIC"
#define MyAppURL "https://github.com/KRWCLASSIC/OpenPredator"
#define MyAppExeName "openpredator-gui.exe"

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
VersionInfoVersion={#VersionInfoVersion}
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousSetupType=no
UsePreviousTasks=no
LicenseFile=..\LICENSE
OutputDir=..\dist\installer
OutputBaseFilename={#OutputFilename}
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
Name: "full"; Description: "Full Suite (OpenSense Control Center, Service Daemon & CLI) [Recommended]"
Name: "gui_only"; Description: "OpenSense Control Center Only"
Name: "headless"; Description: "Headless Suite (Service Daemon & CLI)"
Name: "custom"; Description: "Custom Installation"; Flags: iscustom

[Components]
Name: "gui"; Description: "OpenSense (Native C# Desktop Control Center)"; Types: full gui_only custom
Name: "daemon"; Description: "OpenPredator Background Service (Auto-starts on boot, 1:1 OEM IPC wire)"; Types: full headless custom
Name: "cli"; Description: "OpenPredator CLI Utility (openpredator.exe, integrated with System PATH)"; Types: full headless custom
#if IsDev == "1"
Name: "tools"; Description: "OpenPredator Testing & Diagnostics Suite (Interactive TUI)"; Types: full custom; Flags: dontinheritcheck
Name: "symbols"; Description: "Debug Symbols (.pdb) for Crash Analysis"; Flags: dontinheritcheck
#endif
Name: "ifeo_bridge"; Description: "Stock NitroSense / PredatorSense Bridge Plugin (via IFEOPredatorPatcher) [Coming Soon]"; Flags: dontinheritcheck
Name: "webview_ui"; Description: "OpenPredatorUI WebView Edition (Experimental Web App) [Coming Soon]"; Flags: dontinheritcheck

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut for OpenSense"; GroupDescription: "Additional shortcuts:"; Flags: unchecked; Components: gui
Name: "pssvc_manage"; Description: "Manage OEM Acer PSSvc background service:"; GroupDescription: "OEM PredatorSense / NitroSense Service Options:"
Name: "pssvc_manage\disable_pssvc"; Description: "Disable OEM PredatorSense / NitroSense service autostart (PSSvc)"; Flags: exclusive unchecked
Name: "pssvc_manage\reenable_pssvc"; Description: "Restore / Re-enable OEM PredatorSense / NitroSense service autostart (PSSvc)"; Flags: exclusive unchecked

[Files]
Source: "{srcexe}"; DestDir: "{app}"; DestName: "OpenPredator-Setup.exe"; Flags: external ignoreversion; Check: ShouldCopySetupExe
Source: "{#SourceDir}\openpredator-gui.exe"; DestDir: "{app}"; Flags: ignoreversion; Components: gui
Source: "{#SourceDir}\openpredator-service.exe"; DestDir: "{app}"; Flags: ignoreversion; Components: daemon
Source: "{#SourceDir}\openpredator.exe"; DestDir: "{app}"; Flags: ignoreversion; Components: cli
#if IsDev == "1"
Source: "{#SourceDir}\openpredator-testsuite.exe"; DestDir: "{app}"; Flags: ignoreversion; Components: tools
Source: "{#SourceDir}\Intermediate\*.pdb"; DestDir: "{app}\Intermediate"; Flags: ignoreversion; Components: symbols
#endif
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; DestName: "README.txt"; Flags: ignoreversion

[Dirs]
Name: "{commonappdata}\{#MyAppName}"; Permissions: users-modify

[Registry]
; Register ModifyPath in native 64-bit & 32-bit Uninstall registry keys to light up the 'Modify' button in Windows Settings
Root: HKLM64; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{{E5D4B2A1-8890-4C32-B8E5-7A19F8D62001}_is1"; ValueType: string; ValueName: "ModifyPath"; ValueData: """{app}\OpenPredator-Setup.exe"" /MODIFY"; Flags: uninsdeletekeyifempty
Root: HKLM64; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{{E5D4B2A1-8890-4C32-B8E5-7A19F8D62001}_is1"; ValueType: dword; ValueName: "NoModify"; ValueData: 0
Root: HKLM64; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{{E5D4B2A1-8890-4C32-B8E5-7A19F8D62001}_is1"; ValueType: dword; ValueName: "NoRepair"; ValueData: 1

Root: HKLM32; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{{E5D4B2A1-8890-4C32-B8E5-7A19F8D62001}_is1"; ValueType: string; ValueName: "ModifyPath"; ValueData: """{app}\OpenPredator-Setup.exe"" /MODIFY"; Flags: uninsdeletekeyifempty
Root: HKLM32; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{{E5D4B2A1-8890-4C32-B8E5-7A19F8D62001}_is1"; ValueType: dword; ValueName: "NoModify"; ValueData: 0
Root: HKLM32; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{{E5D4B2A1-8890-4C32-B8E5-7A19F8D62001}_is1"; ValueType: dword; ValueName: "NoRepair"; ValueData: 1

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\openpredator-gui.exe"; Components: gui
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\openpredator-gui.exe"; Tasks: desktopicon; Components: gui

[Run]
; Disable & stop OEM PSSvc if task is selected to prevent pipe conflict
Filename: "{sys}\sc.exe"; Parameters: "stop PSSvc"; Flags: runhidden; Tasks: pssvc_manage\disable_pssvc
Filename: "{sys}\sc.exe"; Parameters: "config PSSvc start= disabled"; Flags: runhidden; Tasks: pssvc_manage\disable_pssvc

; Restore & start OEM PSSvc if task is selected
Filename: "{sys}\sc.exe"; Parameters: "config PSSvc start= auto"; Flags: runhidden; Tasks: pssvc_manage\reenable_pssvc
Filename: "{sys}\sc.exe"; Parameters: "start PSSvc"; Flags: runhidden; Tasks: pssvc_manage\reenable_pssvc

; Stop any running OpenPredator service before configuring
Filename: "{sys}\sc.exe"; Parameters: "stop OpenPredator"; Flags: runhidden; Components: daemon
; Create and start Windows Service if daemon component is selected
Filename: "{sys}\sc.exe"; Parameters: "create OpenPredator binPath= ""{app}\openpredator-service.exe --run"" start= auto DisplayName= ""OpenPredator Service"""; Flags: runhidden; Components: daemon
Filename: "{sys}\sc.exe"; Parameters: "description OpenPredator ""High-performance, zero-bloat hardware control daemon for Acer gaming laptops"""; Flags: runhidden; Components: daemon
Filename: "{sys}\sc.exe"; Parameters: "failure OpenPredator reset= 86400 actions= restart/5000/restart/10000/restart/30000"; Flags: runhidden; Components: daemon
Filename: "{sys}\sc.exe"; Parameters: "start OpenPredator"; Flags: runhidden; Components: daemon
; Launch GUI if component installed
Filename: "{app}\openpredator-gui.exe"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent; Components: gui

[UninstallRun]
; Stop and remove Windows Service
Filename: "{sys}\sc.exe"; Parameters: "stop OpenPredator"; Flags: runhidden; RunOnceId: "StopOpenPredator"
Filename: "{sys}\sc.exe"; Parameters: "delete OpenPredator"; Flags: runhidden; RunOnceId: "DeleteOpenPredator"

[UninstallDelete]
Type: filesandordirs; Name: "{commonappdata}\{#MyAppName}"

[Code]
const
  EnvironmentKey = 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment';
  UninstallRegKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{E5D4B2A1-8890-4C32-B8E5-7A19F8D62001}_is1';

var
  MaintenancePage: TInputOptionWizardPage;
  ComponentsPrepopulated: Boolean;

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

function GetInstalledDir(): string;
var
  InstalledPath: string;
begin
  Result := '';
  // 1. Check native 64-bit registry uninstall key
  if RegQueryStringValue(HKEY_LOCAL_MACHINE_64, UninstallRegKey, 'InstallLocation', InstalledPath) then
  begin
    InstalledPath := Trim(InstalledPath);
    if (InstalledPath <> '') and DirExists(InstalledPath) then
    begin
      Result := InstalledPath;
      Exit;
    end;
  end;
  if RegQueryStringValue(HKEY_LOCAL_MACHINE_64, UninstallRegKey, 'Inno Setup: App Path', InstalledPath) then
  begin
    InstalledPath := Trim(InstalledPath);
    if (InstalledPath <> '') and DirExists(InstalledPath) then
    begin
      Result := InstalledPath;
      Exit;
    end;
  end;

  // 2. Check 32-bit registry uninstall key
  if RegQueryStringValue(HKEY_LOCAL_MACHINE_32, UninstallRegKey, 'InstallLocation', InstalledPath) then
  begin
    InstalledPath := Trim(InstalledPath);
    if (InstalledPath <> '') and DirExists(InstalledPath) then
    begin
      Result := InstalledPath;
      Exit;
    end;
  end;
  if RegQueryStringValue(HKEY_LOCAL_MACHINE_32, UninstallRegKey, 'Inno Setup: App Path', InstalledPath) then
  begin
    InstalledPath := Trim(InstalledPath);
    if (InstalledPath <> '') and DirExists(InstalledPath) then
    begin
      Result := InstalledPath;
      Exit;
    end;
  end;

  // 3. Check if current running installer is inside app directory
  InstalledPath := ExtractFilePath(ExpandConstant('{srcexe}'));
  if FileExists(InstalledPath + '\openpredator-service.exe') or
     FileExists(InstalledPath + '\openpredator-gui.exe') or
     FileExists(InstalledPath + '\openpredator.exe') then
  begin
    Result := InstalledPath;
    Exit;
  end;

  // 4. Check default installation location
  InstalledPath := ExpandConstant('{autopf}\{#MyAppName}');
  if FileExists(InstalledPath + '\openpredator-service.exe') or
     FileExists(InstalledPath + '\openpredator-gui.exe') or
     FileExists(InstalledPath + '\openpredator.exe') then
  begin
    Result := InstalledPath;
    Exit;
  end;
end;

function IsModifyMode(): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
  begin
    if (CompareText(ParamStr(I), '/MODIFY') = 0) or (CompareText(ParamStr(I), '-MODIFY') = 0) then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function ShouldCopySetupExe(): Boolean;
begin
  Result := (CompareText(ExpandConstant('{srcexe}'), ExpandConstant('{app}\OpenPredator-Setup.exe')) <> 0) and
            FileExists(ExpandConstant('{srcexe}'));
end;

procedure DisableComingSoonComponents();
var
  I: Integer;
begin
  for I := 0 to WizardForm.ComponentsList.Items.Count - 1 do
  begin
    if Pos('Coming Soon', WizardForm.ComponentsList.ItemCaption[I]) > 0 then
    begin
      WizardForm.ComponentsList.ItemEnabled[I] := False;
      WizardForm.ComponentsList.Checked[I] := False;
    end;
  end;
end;

procedure PrepopulateInstalledComponents();
var
  I: Integer;
  InstalledDir: string;
begin
  if ComponentsPrepopulated then Exit;
  ComponentsPrepopulated := True;

  InstalledDir := GetInstalledDir();
  if InstalledDir = '' then
    InstalledDir := ExpandConstant('{app}');

  for I := 0 to WizardForm.ComponentsList.Items.Count - 1 do
  begin
    if Pos('Desktop Control Center', WizardForm.ComponentsList.ItemCaption[I]) > 0 then
      WizardForm.ComponentsList.Checked[I] := FileExists(InstalledDir + '\openpredator-gui.exe')
    else if Pos('Background Service', WizardForm.ComponentsList.ItemCaption[I]) > 0 then
      WizardForm.ComponentsList.Checked[I] := FileExists(InstalledDir + '\openpredator-service.exe')
    else if Pos('CLI Utility', WizardForm.ComponentsList.ItemCaption[I]) > 0 then
      WizardForm.ComponentsList.Checked[I] := FileExists(InstalledDir + '\openpredator.exe')
    else if Pos('Coming Soon', WizardForm.ComponentsList.ItemCaption[I]) > 0 then
    begin
      WizardForm.ComponentsList.ItemEnabled[I] := False;
      WizardForm.ComponentsList.Checked[I] := False;
    end;
  end;
end;

procedure InitializeWizard();
begin
  ComponentsPrepopulated := False;

  if IsModifyMode() then
  begin
    MaintenancePage := CreateInputOptionPage(
      wpWelcome,
      'OpenPredator Maintenance',
      'Select a maintenance action to perform',
      'OpenPredator is installed on this system. Please choose what you want to do:',
      True, False
    );
    MaintenancePage.Add('&Modify / Edit Components' + #13#10 + '   Add or remove individual OpenPredator components (GUI, Background Service, CLI).');
    MaintenancePage.Add('&Repair Installation' + #13#10 + '   Reinstall all components and restore default service configurations.');
    MaintenancePage.Add('&Uninstall OpenPredator' + #13#10 + '   Completely remove OpenPredator and all its components from this computer.');
    MaintenancePage.SelectedValueIndex := 0;
  end;

  DisableComingSoonComponents();
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;

  // In Modify mode:
  if IsModifyMode() then
  begin
    // Skip welcome, license agreement, and install directory selection
    if (PageID = wpWelcome) or (PageID = wpLicense) or (PageID = wpSelectDir) or (PageID = wpSelectProgramGroup) then
    begin
      Result := True;
      Exit;
    end;

    // If Repair was selected on Maintenance page, skip components and tasks selection
    if (MaintenancePage <> nil) and (MaintenancePage.SelectedValueIndex = 1) then
    begin
      if (PageID = wpSelectComponents) or (PageID = wpSelectTasks) then
      begin
        Result := True;
        Exit;
      end;
    end;
  end
  else
  begin
    // Normal install/update flow: skip the MaintenancePage if it was created
    if (MaintenancePage <> nil) and (PageID = MaintenancePage.ID) then
      Result := True;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  UninstExe: string;
  ResultCode: Integer;
begin
  Result := True;

  if (MaintenancePage <> nil) and (CurPageID = MaintenancePage.ID) then
  begin
    if MaintenancePage.SelectedValueIndex = 2 then // Uninstall
    begin
      if MsgBox('Are you sure you want to completely remove OpenPredator and all of its components?', mbConfirmation, MB_YESNO) = IDYES then
      begin
        UninstExe := GetInstalledDir() + '\unins000.exe';
        if FileExists(UninstExe) then
        begin
          Exec(UninstExe, '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
          WizardForm.Close;
          Result := False;
        end
        else
        begin
          MsgBox('Uninstaller was not found at: ' + UninstExe, mbError, MB_OK);
          Result := False;
        end;
      end
      else
      begin
        Result := False;
      end;
    end
    else if MaintenancePage.SelectedValueIndex = 1 then // Repair
    begin
      WizardSelectComponents('gui,daemon,cli');
    end
    else if MaintenancePage.SelectedValueIndex = 0 then // Modify
    begin
      PrepopulateInstalledComponents();
    end;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if IsModifyMode() then
  begin
    if CurPageID = wpSelectComponents then
    begin
      WizardForm.Caption := 'OpenPredator Maintenance - Component Management';
      WizardForm.PageNameLabel.Caption := 'Edit Installed Components';
      WizardForm.PageDescriptionLabel.Caption := 'Check components to keep or install; uncheck components to remove them:';
      PrepopulateInstalledComponents();
    end
    else if CurPageID = wpSelectTasks then
    begin
      WizardForm.Caption := 'OpenPredator Maintenance - Service & Shortcut Options';
      WizardForm.PageNameLabel.Caption := 'Service & Shortcut Options';
      WizardForm.PageDescriptionLabel.Caption := 'Configure OEM background service startup behavior and shortcut preferences:';
    end
    else if CurPageID = wpReady then
    begin
      if (MaintenancePage <> nil) and (MaintenancePage.SelectedValueIndex = 1) then
      begin
        WizardForm.Caption := 'OpenPredator Maintenance - Ready to Repair';
        WizardForm.PageNameLabel.Caption := 'Ready to Repair Installation';
        WizardForm.PageDescriptionLabel.Caption := 'Click Repair to reinstall and restore all OpenPredator components:';
        WizardForm.NextButton.Caption := '&Repair';
      end
      else
      begin
        WizardForm.Caption := 'OpenPredator Maintenance - Ready to Apply';
        WizardForm.PageNameLabel.Caption := 'Ready to Apply Modifications';
        WizardForm.PageDescriptionLabel.Caption := 'Click Apply Changes to update your OpenPredator installation:';
        WizardForm.NextButton.Caption := '&Apply Changes';
      end;
    end
    else if CurPageID = wpFinished then
    begin
      WizardForm.Caption := 'OpenPredator Maintenance - Finished';
      WizardForm.FinishedHeadingLabel.Caption := 'OpenPredator Modifications Applied';
      WizardForm.FinishedLabel.Caption := 'The selected OpenPredator components and service configurations have been successfully updated.';
    end;
  end
  else
  begin
    if CurPageID = wpSelectComponents then
    begin
      DisableComingSoonComponents();
    end;
  end;
end;

procedure ProcessComponentModifications();
var
  InstalledDir, GuiExe, SvcExe, CliExe, StartLnk, DeskLnk: string;
  ResultCode: Integer;
begin
  InstalledDir := GetInstalledDir();
  if InstalledDir = '' then
    InstalledDir := ExpandConstant('{app}');

  GuiExe := InstalledDir + '\openpredator-gui.exe';
  SvcExe := InstalledDir + '\openpredator-service.exe';
  CliExe := InstalledDir + '\openpredator.exe';
  StartLnk := ExpandConstant('{autoprograms}\{#MyAppName}.lnk');
  DeskLnk := ExpandConstant('{autodesktop}\{#MyAppName}.lnk');

  // 1. If GUI is NOT selected, remove GUI binary & shortcuts if they were previously installed
  if not WizardIsComponentSelected('gui') then
  begin
    if FileExists(GuiExe) then DeleteFile(GuiExe);
    if FileExists(StartLnk) then DeleteFile(StartLnk);
    if FileExists(DeskLnk) then DeleteFile(DeskLnk);
  end;

  // 2. If Daemon is NOT selected, stop & remove Windows Service and delete binary if previously installed
  if not WizardIsComponentSelected('daemon') then
  begin
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop OpenPredator', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(ExpandConstant('{sys}\sc.exe'), 'delete OpenPredator', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    if FileExists(SvcExe) then DeleteFile(SvcExe);
  end;

  // 3. If CLI is NOT selected, remove CLI binary & remove from System PATH
  if not WizardIsComponentSelected('cli') then
  begin
    if FileExists(CliExe) then DeleteFile(CliExe);
    RemovePath(InstalledDir);
  end
  else
  begin
    AddPath(InstalledDir);
  end;
end;

procedure RegisterModifySupport();
var
  AppDir, SetupExeCopy, ModifyCmd: string;
begin
  AppDir := ExpandConstant('{app}');
  SetupExeCopy := AppDir + '\OpenPredator-Setup.exe';
  ModifyCmd := '"' + SetupExeCopy + '" /MODIFY';

  // Copy installer binary into {app} so Windows Settings / Control Panel can launch it when clicking 'Modify'
  try
    if (CompareText(ExpandConstant('{srcexe}'), SetupExeCopy) <> 0) and (Length(ExpandConstant('{srcexe}')) > 0) and FileExists(ExpandConstant('{srcexe}')) then
    begin
      CopyFile(ExpandConstant('{srcexe}'), SetupExeCopy, False);
    end;
  except
  end;

  if FileExists(SetupExeCopy) then
  begin
    RegWriteStringValue(HKEY_LOCAL_MACHINE_64, UninstallRegKey, 'ModifyPath', ModifyCmd);
    RegWriteDWordValue(HKEY_LOCAL_MACHINE_64, UninstallRegKey, 'NoModify', 0);
    RegWriteDWordValue(HKEY_LOCAL_MACHINE_64, UninstallRegKey, 'NoRepair', 1);

    RegWriteStringValue(HKEY_LOCAL_MACHINE_32, UninstallRegKey, 'ModifyPath', ModifyCmd);
    RegWriteDWordValue(HKEY_LOCAL_MACHINE_32, UninstallRegKey, 'NoModify', 0);
    RegWriteDWordValue(HKEY_LOCAL_MACHINE_32, UninstallRegKey, 'NoRepair', 1);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    ProcessComponentModifications();
    RegisterModifySupport();
  end
  else if CurStep = ssDone then
  begin
    RegisterModifySupport();
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RemovePath(ExpandConstant('{app}'));
    DeleteFile(ExpandConstant('{app}\OpenPredator-Setup.exe'));
  end;
end;
