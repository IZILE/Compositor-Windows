#ifndef PublishDir
  #error Supply /DPublishDir with the verified installer payload directory.
#endif
#ifndef DeliveryDir
  #error Supply /DDeliveryDir with the outputs directory.
#endif
#ifndef InstallIdentity
  #define InstallIdentity "{D652136C-E4B4-4194-98F7-7264ABAF317E}"
#endif
#ifndef AppVersion
  #define AppVersion "0.6.4"
#endif

[Setup]
AppId={{#InstallIdentity}
AppName=Compositor
AppVersion={#AppVersion}
AppPublisher=Compositor Windows Community
AppPublisherURL=https://github.com/robbietilton/Compositor
AppComments=Independent Windows community port, English and Simplified Chinese
DefaultDirName={localappdata}\Programs\Compositor
DefaultGroupName=Compositor
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
DisableDirPage=no
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
UsePreviousTasks=yes
UninstallDisplayName=Compositor
UninstallDisplayIcon={app}\Compositor.ico
OutputDir={#DeliveryDir}
OutputBaseFilename=Compositor-Setup
SetupIconFile={#PublishDir}\Compositor.ico
LicenseFile={#PublishDir}\LICENSE
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
SetupLogging=yes
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName=Compositor Windows Community

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
#ifndef TestInstall
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce
#endif

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,data\*"

[Icons]
#ifndef TestInstall
Name: "{autoprograms}\Compositor\Compositor"; Filename: "{app}\Compositor.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Compositor.ico"
Name: "{autodesktop}\Compositor"; Filename: "{app}\Compositor.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Compositor.ico"; Tasks: desktopicon
#endif

[Run]
#ifndef TestInstall
Filename: "{app}\Compositor.exe"; Description: "{cm:LaunchProgram,Compositor}"; Flags: nowait postinstall skipifsilent
#endif

; User settings and editable projects are owned by the application. The uninstaller removes installed files only.

[Code]
procedure SHChangeNotify(EventId: Integer; Flags: Cardinal; const Item1: String; Item2: NativeInt);
  external 'SHChangeNotify@shell32.dll stdcall';

procedure RefreshInstalledIcon(const Path: String);
begin
  if FileExists(Path) then begin
    { SHCNE_UPDATEITEM and SHCNF_PATHW refresh this item without restarting Explorer. }
    SHChangeNotify($00002000, $0005, Path, 0);
    Log('Refreshed installed icon: ' + Path);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then begin
    RefreshInstalledIcon(ExpandConstant('{app}\Compositor.ico'));
    RefreshInstalledIcon(ExpandConstant('{app}\Compositor.exe'));
#ifndef TestInstall
    RefreshInstalledIcon(ExpandConstant('{autodesktop}\Compositor.lnk'));
    RefreshInstalledIcon(ExpandConstant('{autoprograms}\Compositor\Compositor.lnk'));
#endif
  end;
end;
