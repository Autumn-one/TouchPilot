#ifndef AppVersion
  #error AppVersion must be provided by the release build.
#endif
#ifndef NumericVersion
  #error NumericVersion must be provided by the release build.
#endif
#ifndef SourceDir
  #error SourceDir must be provided by the release build.
#endif
#ifndef OutputDir
  #error OutputDir must be provided by the release build.
#endif
#ifndef OutputBaseFilename
  #error OutputBaseFilename must be provided by the release build.
#endif
#ifndef SetupIconPath
  #error SetupIconPath must be provided by the release build.
#endif

#define AppName "TouchPilot"
#define AppExecutable "GestureSign.exe"

[Setup]
AppId={{6E5935F1-FF63-4DD2-BD7B-8144A8E21637}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=TouchPilot
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
SetupIconFile={#SetupIconPath}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern dynamic
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=force
RestartApplications=no
UninstallDisplayIcon={app}\{#AppExecutable}
VersionInfoDescription={#AppName} Setup
VersionInfoProductName={#AppName}
VersionInfoVersion={#NumericVersion}
VersionInfoProductTextVersion={#AppVersion}

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExecutable}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExecutable}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExecutable}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
