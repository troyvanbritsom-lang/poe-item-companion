#define AppName "Path of Exile Companion"
#define AppVersion Trim(FileRead(FileOpen("..\VERSION")))
#define Executable "Poe2ItemCompanion.exe"

[Setup]
AppId={{32B6D653-B474-4E34-A27B-855CE884BBDB}
AppName={#AppName}
AppVersion={#AppVersion}
VersionInfoVersion={#AppVersion}.0
AppPublisher=PoE Item Companion contributors
AppPublisherURL=https://github.com/troyvanbritsom-lang/poe-item-companion
AppUpdatesURL=https://github.com/troyvanbritsom-lang/poe-item-companion/releases
DefaultDirName={localappdata}\Programs\PoE Item Companion
DefaultGroupName={#AppName}
UsePreviousAppDir=yes
DisableDirPage=auto
DisableProgramGroupPage=yes
DisableWelcomePage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
UninstallDisplayIcon={app}\{#Executable}
AppMutex=PoeItemCompanion.Installed
CloseApplications=no
RestartApplications=no
OutputDir=..\bin\installer
OutputBaseFilename=PoE-Item-Companion-Setup-v{#AppVersion}-x64
WizardStyle=modern dark hidebevels
WizardImageFile=..\bin\installer-assets\wizard.bmp
WizardSmallImageFile=..\bin\installer-assets\sigil.bmp
WizardImageBackColor=$0013100E
WizardSmallImageBackColor=$0013100E
Compression=lzma2
SolidCompression=yes

[Files]
Source: "..\bin\{#Executable}"; DestDir: "{app}"; Flags: ignoreversion
Source: "installed.mode"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\NOTICE.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\scripts\pob_wrapper.lua"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "..\scripts\install-pob-bridge.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "..\scripts\pob_import_bridge.lua"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "..\data\ggg_passive_ids.tsv"; DestDir: "{app}\data"; Flags: ignoreversion
Source: "..\data\poe1_gem_acquisition.json"; DestDir: "{app}\data"; Flags: ignoreversion
Source: "..\data\poenavi-LICENSE.txt"; DestDir: "{app}\data"; Flags: ignoreversion

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#Executable}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#Executable}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#Executable}"; Description: "Open {#AppName}"; Flags: nowait postinstall skipifsilent unchecked

[Code]
var
  PathsPage: TWizardPage;
  PathEdits: array[0..1] of TNewEdit;
  ResultsText: String;
  GameDirs: array[0..1] of String;
  IncompleteBridge: Boolean;

function GameLabel(Index: Integer): String;
begin
  if Index = 0 then Result := 'PoB for PoE1' else Result := 'PoB for PoE2';
end;

function IsUnder(const Child, Parent: String): Boolean;
begin
  Result := Pos(Lowercase(AddBackslash(Parent)), Lowercase(AddBackslash(Child))) = 1;
end;

function Bridge(Index: Integer; Validate: Boolean; var Detail: String): Boolean;
var
  ScriptDir, ArgName, Params: String;
  Output: TExecOutput;
  ExitCode, I: Integer;
begin
  Result := False;
  Detail := 'Windows PowerShell did not complete successfully. See the Setup log.';
  if Validate then ScriptDir := ExpandConstant('{tmp}')
  else ScriptDir := ExpandConstant('{app}\scripts');
  if Index = 0 then ArgName := '-Poe1Path' else ArgName := '-Poe2Path';
  Params := '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' +
    ScriptDir + '\install-pob-bridge.ps1" ' + ArgName + ' "' + GameDirs[Index] + '"';
  if Validate then Params := Params + ' -ValidateOnly';
  try
    if ExecAndCaptureOutput(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
      Params, ScriptDir, SW_HIDE, ewWaitUntilTerminated, ExitCode, Output) then
    begin
      for I := 0 to GetArrayLength(Output.StdOut) - 1 do Log(Output.StdOut[I]);
      for I := 0 to GetArrayLength(Output.StdErr) - 1 do Log(Output.StdErr[I]);
      if GetArrayLength(Output.StdErr) > 0 then Detail := Trim(Output.StdErr[0]);
      Result := (ExitCode = 0) and not Output.Error;
    end;
  except
    Log(GetExceptionMessage);
  end;
end;

procedure BrowsePoB(Sender: TObject);
var
  I: Integer;
  Folder: String;
begin
  I := TNewButton(Sender).Tag;
  Folder := PathEdits[I].Text;
  if BrowseForFolder('Select the installation folder for ' + GameLabel(I), Folder, False) then
    PathEdits[I].Text := Folder;
end;

procedure InitializeWizard;
var
  I, Y, ButtonWidth: Integer;
  Instructions, PathLabel: TNewStaticText;
  Browse: TNewButton;
begin
  PathsPage := CreateCustomPage(wpSelectDir, 'Path of Building bridges (optional)',
    'Connect either game, both games, or skip this step.');
  Instructions := TNewStaticText.Create(PathsPage);
  Instructions.Parent := PathsPage.Surface;
  Instructions.Width := PathsPage.SurfaceWidth;
  Instructions.AutoSize := False;
  Instructions.WordWrap := True;
  Instructions.Caption := 'Save your builds and close Path of Building before continuing. Choose the installation folder for each game, or leave its field blank to skip it and keep existing settings.';
  Y := ScaleY(66);
  ButtonWidth := ScaleX(82);
  for I := 0 to 1 do
  begin
    PathLabel := TNewStaticText.Create(PathsPage);
    PathLabel.Parent := PathsPage.Surface;
    PathLabel.Top := Y;
    PathLabel.Caption := GameLabel(I) + ':';
    PathEdits[I] := TNewEdit.Create(PathsPage);
    PathEdits[I].Parent := PathsPage.Surface;
    PathEdits[I].SetBounds(0, Y + ScaleY(18), PathsPage.SurfaceWidth - ButtonWidth - ScaleX(8), ScaleY(23));
    if I = 0 then PathEdits[I].Text := ExpandConstant('{param:POB1|}')
    else PathEdits[I].Text := ExpandConstant('{param:POB2|}');
    Browse := TNewButton.Create(PathsPage);
    Browse.Parent := PathsPage.Surface;
    Browse.SetBounds(PathsPage.SurfaceWidth - ButtonWidth, Y + ScaleY(17), ButtonWidth, ScaleY(25));
    Browse.Caption := 'Browse...';
    Browse.Tag := I;
    Browse.OnClick := @BrowsePoB;
    Y := Y + ScaleY(66);
  end;
  Instructions.Height := ScaleY(58);
end;

function ValidateSelections(var ErrorText: String): Boolean;
var
  I: Integer;
  Choice, Detail: String;
begin
  Result := False;
  for I := 0 to 1 do
  begin
    Choice := Trim(PathEdits[I].Text);
    GameDirs[I] := '';
    if Choice <> '' then
    begin
      if (Length(Choice) < 4) or (Choice[2] <> ':') or (Choice[3] <> '\') or
        (Pos('"', Choice) <> 0) then
      begin ErrorText := GameLabel(I) + ': choose an absolute local folder.'; Exit; end;
      GameDirs[I] := RemoveBackslashUnlessRoot(ExpandFileName(Choice));
      if IsUnder(GameDirs[I], WizardDirValue) or IsUnder(WizardDirValue, GameDirs[I]) then
      begin ErrorText := 'Setup and Path of Building must be in separate folders.'; Exit; end;
    end;
  end;
  if (GameDirs[0] <> '') and (CompareText(GameDirs[0], GameDirs[1]) = 0) then
  begin ErrorText := 'PoB1 and PoB2 must be in separate folders.'; Exit; end;
  if (GameDirs[0] <> '') or (GameDirs[1] <> '') then
  begin
    if not FileExists(ExpandConstant('{tmp}\install-pob-bridge.ps1')) then
    begin
      ExtractTemporaryFile('install-pob-bridge.ps1');
      ExtractTemporaryFile('pob_import_bridge.lua');
    end;
    for I := 0 to 1 do
      if GameDirs[I] <> '' then
        if not Bridge(I, True, Detail) then
        begin
          ErrorText := GameLabel(I) + ': ' + Detail + #13#10#13#10 +
            'Choose a compatible folder or clear this field to skip this bridge.';
          Exit;
        end;
  end;
  Result := True;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Detail: String;
begin
  Result := True;
  if (CurPageID = PathsPage.ID) and not WizardSilent then
  begin
    Result := ValidateSelections(Detail);
    if not Result then MsgBox(Detail, mbError, MB_OK);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  if ValidateSelections(Result) then Result := '';
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
var
  I: Integer;
  Choice: String;
begin
  Result := MemoDirInfo + NewLine + NewLine;
  for I := 0 to 1 do
  begin
    Choice := GameDirs[I];
    if Choice = '' then Choice := 'Skipped';
    Result := Result + GameLabel(I) + ':' + NewLine + Space + Choice + NewLine;
  end;
  if MemoTasksInfo <> '' then Result := Result + NewLine + MemoTasksInfo;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  I, ExitCode: Integer;
  Game, Detail, Status: String;
  Failed: Boolean;
begin
  if CurStep <> ssPostInstall then Exit;
  Failed := False;
  for I := 0 to 1 do
  begin
    Status := 'skipped';
    if GameDirs[I] <> '' then
    begin
      WizardForm.StatusLabel.Caption := 'Setting up ' + GameLabel(I) + '...';
      if I = 0 then Game := 'poe1' else Game := 'poe2';
      if Bridge(I, False, Detail) then
        Status := 'bridge installed. Restart PoB to activate it.'
      else
      begin
        Failed := True;
        Status := 'bridge not installed. ' + Detail;
      end;
      if not Exec(ExpandConstant('{app}\{#Executable}'),
        '--game ' + Game + ' --setup-pob-profile "' + GameDirs[I] + '"',
        ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ExitCode) or (ExitCode <> 0) then
      begin
        Failed := True;
        Status := Status + ' The selected path could not be saved; set it in Companion Settings.';
      end;
    end;
    Log(GameLabel(I) + ': ' + Status);
    ResultsText := ResultsText + #13#10 + GameLabel(I) + ': ' + Status;
  end;
  IncompleteBridge := Failed;
  if Failed then
    SuppressibleMsgBox('The companion was installed, but PoB setup needs attention.' + #13#10 + ResultsText
      + #13#10#13#10 + 'Use a writable PoB installation under the same Windows account or leave the bridge disabled.',
      mbInformation, MB_OK, IDOK);
end;

function GetCustomSetupExitCode: Integer;
begin
  Result := 0;
  if IncompleteBridge then Result := 10;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpFinished) and (ResultsText <> '') then
    WizardForm.FinishedLabel.Caption := 'The companion is installed.' + #13#10 + ResultsText;
end;
