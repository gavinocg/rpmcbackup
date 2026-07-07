; RPMC Backup - InnoSetup Installer
#define MyAppName "RPMC Backup"
#define MyAppVersion "1.1.2"
#define MyAppPublisher "RPMC"
#define MyAppURL "http://192.168.1.201:9001"
#define ServiceExe "RPMC_Backup.Service.exe"
#define ConfigExe "RPMC_Backup.Config.exe"
[Setup]
AppId={{B8B5F3E2-9F1A-4C2D-A1E3-8F9B7C6D5E4F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\RPMC\Backup
DefaultGroupName=RPMC Backup
DisableProgramGroupPage=yes
OutputDir=.
OutputBaseFilename=RPMC_Backup_v{#MyAppVersion}_Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
UninstallDisplayIcon={app}\icon.ico
DisableWelcomePage=no
DisableFinishedPage=no
CloseApplications=force
AppMutex=RPMCBackupMutex

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Files]
Source: "..\src\RPMC_Backup.Service\bin\Release\net8.0\publish\*"; DestDir: "{app}\service"; Flags: ignoreversion recursesubdirs; Excludes: "*.pdb"
Source: "..\src\RPMC_Backup.Config\bin\Release\net8.0-windows\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs; Excludes: "*.pdb"
Source: "..\src\RPMC_Backup.UninstallCheck\bin\Release\net8.0-windows\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs; Excludes: "*.pdb"
Source: "icon.ico"; DestDir: "{app}"; Flags: ignoreversion

[Dirs]
Name: "{commonappdata}\RPMC\Backup"; Flags: uninsalwaysuninstall

[Icons]
Name: "{group}\RPMC Backup"; Filename: "{app}\{#ConfigExe}"; IconFilename: "{app}\icon.ico"
Name: "{commondesktop}\RPMC Backup"; Filename: "{app}\{#ConfigExe}"; IconFilename: "{app}\icon.ico"
Name: "{group}\RPMC Backup Config"; Filename: "{app}\{#ConfigExe}"; IconFilename: "{app}\icon.ico"
Name: "{group}\Desinstalar RPMC Backup"; Filename: "{uninstallexe}"; IconFilename: "{app}\icon.ico"

[Run]
Filename: "{app}\{#ConfigExe}"; Flags: runasoriginaluser nowait postinstall; Description: "Abrir RPMC Backup Config"

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop rpmc-backup-service"; Flags: runhidden; StatusMsg: "Deteniendo servicio..."
Filename: "{sys}\sc.exe"; Parameters: "delete rpmc-backup-service"; Flags: runhidden; StatusMsg: "Eliminando servicio..."
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im {#ConfigExe}"; Flags: runhidden; StatusMsg: "Cerrando aplicación..."

[Code]
var
  ConfigFile: String;

procedure StopService;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop rpmc-backup-service', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\sc.exe'), 'delete rpmc-backup-service', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im RPMC_Backup.Config.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure StartService;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\sc.exe'), 'start rpmc-backup-service', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    StopService;
  end;
  if CurUninstallStep = usPostUninstall then
  begin
    DelTree(ExpandConstant('{commonappdata}\RPMC\Backup'), True, True, True);
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'RPMC Backup');
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopService;
  RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'RPMC Backup');
  Result := '';
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  ConfigFile := ExpandConstant('{commonappdata}\RPMC\Backup\config.dat');
end;

function InitializeUninstall: Boolean;
var
  ResultCode: Integer;
  ValidatorPath, Password: String;
begin
  Result := False;
  ValidatorPath := ExpandConstant('{app}\RPMC_Backup.UninstallCheck.exe');
  if FileExists(ValidatorPath) then
  begin
    if Exec(ValidatorPath, '', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
    begin
      if ResultCode = 0 then
        Result := True
      else
        MsgBox('Clave incorrecta. No se puede desinstalar.', mbError, MB_OK);
    end;
  end
  else
  begin
    if MsgBox('No se encontro el validador. Continuar desinstalacion?', mbConfirmation, MB_YESNO) = mrYes then
      Result := True;
  end;
end;

procedure RegisterService;
var
  ResultCode: Integer;
  ServicePath: String;
begin
  ServicePath := ExpandConstant('{app}\service\{#ServiceExe}');
  Exec(ExpandConstant('{sys}\sc.exe'), 
    Format('create rpmc-backup-service binPath="%s" start=auto DisplayName="RPMC Backup Service"', [ServicePath]), 
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure RegisterAutoStart;
begin
  RegWriteStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run',
    'RPMC Backup', ExpandConstant('"{app}\{#ConfigExe}" --tray'));
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    RegisterService;
    RegisterAutoStart;
    StartService;
  end;
end;
