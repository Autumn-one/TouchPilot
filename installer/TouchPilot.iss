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
#define AppExecutable "TouchPilot.exe"

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

[Code]
function RemoveSurroundingQuotes(const Value: string): string;
begin
  Result := Trim(Value);
  if (Length(Result) >= 2) and (Result[1] = '"') and
     (Result[Length(Result)] = '"') then
    Result := Copy(Result, 2, Length(Result) - 2);
end;

function IsCurrentInstallationTarget(const TargetPath: string): Boolean;
var
  NormalizedTarget: string;
begin
  NormalizedTarget := RemoveSurroundingQuotes(TargetPath);
  Result := (NormalizedTarget <> '') and
    ((CompareText(ExpandFileName(NormalizedTarget),
        ExpandConstant('{app}\TouchPilot.exe')) = 0) or
     (CompareText(ExpandFileName(NormalizedTarget),
        ExpandConstant('{app}\GestureSign.exe')) = 0));
end;

procedure DeleteOwnedStartupLink(const LinkName: string);
var
  LinkPath: string;
  TargetPath: string;
  Shell: Variant;
  Shortcut: Variant;
begin
  LinkPath := AddBackslash(ExpandConstant('{userstartup}')) + LinkName;
  if not FileExists(LinkPath) then
    Exit;

  try
    Shell := CreateOleObject('WScript.Shell');
    Shortcut := Shell.CreateShortcut(LinkPath);
    TargetPath := Shortcut.TargetPath;
    if IsCurrentInstallationTarget(TargetPath) then
    begin
      if DeleteFile(LinkPath) then
        Log('Removed owned startup shortcut: ' + LinkPath)
      else
        Log('Could not remove owned startup shortcut: ' + LinkPath);
    end
    else
      Log('Preserved startup shortcut with a different target: ' + LinkPath);
  except
    Log('Could not inspect startup shortcut ' + LinkPath + ': ' +
      GetExceptionMessage);
  end;
end;

procedure DeleteOwnedStartupTask(const TaskName: string);
var
  Scheduler: Variant;
  RootFolder: Variant;
  RegisteredTask: Variant;
  TaskAction: Variant;
  TargetPath: string;
begin
  try
    Scheduler := CreateOleObject('Schedule.Service');
    Scheduler.Connect;
    RootFolder := Scheduler.GetFolder('\');
    try
      RegisteredTask := RootFolder.GetTask(TaskName);
    except
      Log('Startup task is not registered: ' + TaskName);
      Exit;
    end;

    if RegisteredTask.Definition.Actions.Count < 1 then
    begin
      Log('Preserved startup task without an executable action: ' + TaskName);
      Exit;
    end;

    TaskAction := RegisteredTask.Definition.Actions.Item(1);
    TargetPath := TaskAction.Path;
    if IsCurrentInstallationTarget(TargetPath) then
    begin
      RootFolder.DeleteTask(TaskName, 0);
      Log('Removed owned startup task: ' + TaskName);
    end
    else
      Log('Preserved startup task with a different target: ' + TaskName);
  except
    Log('Could not inspect startup task ' + TaskName + ': ' +
      GetExceptionMessage);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    DeleteOwnedStartupLink('TouchPilot.lnk');
    DeleteOwnedStartupLink('GestureSign.lnk');
    DeleteOwnedStartupTask('TouchPilot Startup');
    DeleteOwnedStartupTask('StartGestureSign');
  end;
end;
