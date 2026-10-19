#ifndef PublishDir
  #define PublishDir "..\artifacts\packages\publish-win-x64"
#endif
#ifndef InstallerOutput
  #define InstallerOutput "..\artifacts\installers"
#endif
#ifndef SetupBaseFilename
  #define SetupBaseFilename "NekoProxy-Setup"
#endif
#define AppName "NekoProxy"
#define AppVersion "0.6.0"
#define AppExe "NekoProxy.Desktop.exe"

[Setup]
AppId={{0934FAD5-6396-4A77-85A6-31C9C8C2B544}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=NekoProxy contributors
DefaultDirName={localappdata}\Programs\NekoProxy
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
DefaultGroupName=NekoProxy
OutputDir={#InstallerOutput}
OutputBaseFilename={#SetupBaseFilename}
SetupIconFile=..\src\NekoProxy.Desktop\Assets\NekoProxy.ico
UninstallDisplayIcon={app}\{#AppExe}
LicenseFile=..\LICENSE
InfoBeforeFile=UsageNotice.rtf
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
CloseApplications=no
RestartApplications=no

[Tasks]
Name: "startmenu"; Description: "Create a Start Menu shortcut"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autodesktop}\NekoProxy"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExe}"
Name: "{autoprograms}\NekoProxy"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExe}"; Tasks: startmenu

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch NekoProxy"; Flags: nowait postinstall skipifsilent

; Inno removes installed files/shortcuts. User data, keyrings and exports are
; deliberately outside {app}, and there are no network/registry/proxy hooks.
