#ifndef AppVersion
  #define AppVersion "0.2.0"
#endif
#ifndef BinaryDirectory
  #define BinaryDirectory AddBackslash(SourcePath) + "..\..\dist\windows\app"
#endif
#ifndef AppIdOverride
  #define AppIdOverride "{{D7113995-7A5F-4D2D-9C6B-82A314667E5B}"
#endif
#ifndef AppMutexOverride
  #define AppMutexOverride "Local\Trazo.ScreenCapture.v1"
#endif
#ifndef OutputBaseName
  #define OutputBaseName "Trazo-v" + AppVersion + "-setup"
#endif

[Setup]
AppId={#AppIdOverride}
AppName=Trazo
AppVersion={#AppVersion}
AppVerName=Trazo {#AppVersion}
AppPublisher=Racknar0
AppPublisherURL=https://github.com/Racknar0/trazo-capture
AppSupportURL=https://github.com/Racknar0/trazo-capture/issues
AppUpdatesURL=https://github.com/Racknar0/trazo-capture/releases
AppCopyright=Copyright (c) 2026 Racknar0
DefaultDirName={localappdata}\Programs\Trazo
DefaultGroupName=Trazo
PrivilegesRequired=lowest
DisableDirPage=no
DisableProgramGroupPage=yes
AllowNoIcons=yes
DisableWelcomePage=no
LicenseFile=..\..\LICENSE
MinVersion=10.0
WizardStyle=modern
WizardSizePercent=110
SetupIconFile=Trazo.ico
WizardImageFile=wizard-large.bmp
WizardSmallImageFile=wizard-small.bmp
UninstallDisplayIcon={app}\Trazo.exe
UninstallDisplayName=Trazo
OutputDir=..\..\dist\windows
OutputBaseFilename={#OutputBaseName}
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName=Trazo
VersionInfoProductVersion={#AppVersion}
VersionInfoDescription=Instalador de Trazo
VersionInfoCopyright=Copyright (c) 2026 Racknar0
Compression=lzma2
SolidCompression=yes
AppMutex={#AppMutexOverride}
CloseApplications=yes
CloseApplicationsFilter=Trazo.exe
RestartApplications=no
ChangesAssociations=yes
Uninstallable=yes
CreateUninstallRegKey=yes
UsePreviousTasks=yes

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
spanish.DesktopTask=Crear un acceso directo en el escritorio
spanish.InstallOptions=Opciones de instalación:
spanish.StartupTask=Iniciar Trazo al entrar en Windows
spanish.AssociationTask=Abrir proyectos .trazo con Trazo
spanish.LaunchTrazo=Abrir Trazo
spanish.ProjectType=Proyecto editable de Trazo
spanish.NetRequired=Trazo necesita .NET Framework 4.8 o posterior.%nInstálalo desde https://dotnet.microsoft.com/download/dotnet-framework y vuelve a ejecutar este instalador.
spanish.FinishedInfo=Trazo está listo para capturar y anotar.%n%nTus capturas y preferencias se guardan en tu perfil de usuario y se conservarán al desinstalar la aplicación.
english.DesktopTask=Create a desktop shortcut
english.InstallOptions=Installation options:
english.StartupTask=Start Trazo when signing in to Windows
english.AssociationTask=Open .trazo projects with Trazo
english.LaunchTrazo=Launch Trazo
english.ProjectType=Trazo editable project
english.NetRequired=Trazo requires .NET Framework 4.8 or later.%nInstall it from https://dotnet.microsoft.com/download/dotnet-framework and run this installer again.
english.FinishedInfo=Trazo is ready to capture and annotate.%n%nYour captures and preferences are stored in your user profile and are preserved when the application is uninstalled.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopTask}"; GroupDescription: "{cm:InstallOptions}"
Name: "startup"; Description: "{cm:StartupTask}"; GroupDescription: "{cm:InstallOptions}"
Name: "fileassoc"; Description: "{cm:AssociationTask}"; GroupDescription: "{cm:InstallOptions}"; Flags: unchecked

[Files]
Source: "{#BinaryDirectory}\Trazo.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BinaryDirectory}\Trazo.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\CONTRIBUTING.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\docs\preview.png"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\..\docs\dashboard.png"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\..\docs\settings.png"; DestDir: "{app}\docs"; Flags: ignoreversion

[Icons]
Name: "{group}\Trazo"; Filename: "{app}\Trazo.exe"; WorkingDir: "{app}"; AppUserModelID: "Racknar0.Trazo"
Name: "{group}\Desinstalar Trazo"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Trazo"; Filename: "{app}\Trazo.exe"; WorkingDir: "{app}"; AppUserModelID: "Racknar0.Trazo"; Tasks: desktopicon

[Run]
Filename: "{app}\Trazo.exe"; Description: "{cm:LaunchTrazo}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  ClassesKey = 'Software\Classes\';
  ProjectId = 'Trazo.Project';

function StartupCommand: String;
begin
  Result := '"' + ExpandConstant('{app}\Trazo.exe') + '" --background';
end;

function ProjectCommand: String;
begin
  Result := '"' + ExpandConstant('{app}\Trazo.exe') + '" --open "%1"';
end;

function SameCommand(const A, B: String): Boolean;
begin
  Result := CompareText(Trim(A), Trim(B)) = 0;
end;

function InitializeSetup: Boolean;
var
  Release: Cardinal;
begin
  Release := 0;
  if not RegQueryDWordValue(HKLM32, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) then
    if IsWin64 then
      RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release);
  Result := Release >= 528040;
  if not Result then
    SuppressibleMsgBox(CustomMessage('NetRequired'), mbError, MB_OK, IDOK);
end;

procedure RemoveOwnStartup;
var
  Existing: String;
begin
  if RegQueryStringValue(HKCU, RunKey, 'Trazo', Existing) and
    (SameCommand(Existing, StartupCommand) or
     SameCommand(Existing, '"' + ExpandConstant('{app}\Trazo.exe') + '"')) then
    RegDeleteValue(HKCU, RunKey, 'Trazo');
end;

procedure ConfigureProjectAssociation;
var
  Existing: String;
begin
  RegWriteStringValue(HKCU, ClassesKey + ProjectId, '', CustomMessage('ProjectType'));
  RegWriteStringValue(HKCU, ClassesKey + ProjectId + '\DefaultIcon', '', '"' + ExpandConstant('{app}\Trazo.exe') + '",0');
  RegWriteStringValue(HKCU, ClassesKey + ProjectId + '\shell\open\command', '', ProjectCommand);
  RegWriteStringValue(HKCU, ClassesKey + '.trazo\OpenWithProgids', ProjectId, '');
  if not RegQueryStringValue(HKCU, ClassesKey + '.trazo', '', Existing) or (Existing = '') then
    RegWriteStringValue(HKCU, ClassesKey + '.trazo', '', ProjectId);
end;

procedure RemoveOwnProjectAssociation;
var
  Existing: String;
begin
  if RegQueryStringValue(HKCU, ClassesKey + ProjectId + '\shell\open\command', '', Existing) and SameCommand(Existing, ProjectCommand) then
  begin
    RegDeleteKeyIncludingSubkeys(HKCU, ClassesKey + ProjectId);
    RegDeleteValue(HKCU, ClassesKey + '.trazo\OpenWithProgids', ProjectId);
    RegDeleteKeyIfEmpty(HKCU, ClassesKey + '.trazo\OpenWithProgids');
    if RegQueryStringValue(HKCU, ClassesKey + '.trazo', '', Existing) and (Existing = ProjectId) then
      RegDeleteValue(HKCU, ClassesKey + '.trazo', '');
    RegDeleteKeyIfEmpty(HKCU, ClassesKey + '.trazo');
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if WizardIsTaskSelected('startup') then
      RegWriteStringValue(HKCU, RunKey, 'Trazo', StartupCommand)
    else
      RemoveOwnStartup;
    if WizardIsTaskSelected('fileassoc') then
      ConfigureProjectAssociation;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    RemoveOwnStartup;
    RemoveOwnProjectAssociation;
  end;
end;

procedure InitializeWizard;
begin
  WizardForm.FinishedLabel.Caption := CustomMessage('FinishedInfo');
end;
