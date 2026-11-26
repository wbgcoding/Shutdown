#ifndef AppVersion
  #error AppVersion is required
#endif
#define AppName "Shutdown"
#define AppGuid "{03870C1B-3769-4707-8325-757EFA963233}"
#define EscapedAppId "{" + AppGuid
#define AppUrl "https://github.com/wbgcoding/Shutdown"
#define AppCopyright "© 2026 BGCoding"

[Setup]
AppId={#EscapedAppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=BGCoding
AppCopyright={#AppCopyright}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\Shutdown
DefaultGroupName=Shutdown
AllowNoIcons=yes
DisableDirPage=no
DisableProgramGroupPage=no
DisableReadyPage=yes
DisableWelcomePage=no
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UsePreviousPrivileges=no
UsePreviousLanguage=yes
ArchitecturesInstallIn64BitMode=x64os or arm64
MinVersion=10.0.18362
ShowLanguageDialog=no
LanguageDetectionMethod=uilanguage
Compression=lzma2/ultra64
SolidCompression=yes
SetupIconFile=..\src\Shutdown\Assets\Shutdown.ico
UninstallDisplayIcon={app}\Shutdown.exe
AppMutex=Local\BGCoding.Shutdown.Instance
CloseApplications=yes
CloseApplicationsFilter=Shutdown.exe
RestartApplications=no
RestartIfNeededByRun=no
SetupLogging=yes
UninstallLogging=yes
Uninstallable=IsManagedInstall
CreateUninstallRegKey=IsManagedInstall
OutputDir=..\dist\release
OutputBaseFilename=Shutdown-Setup-{#AppVersion}

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

#include "..\dist\installer-messages.iss"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"

[Files]
Source: "..\dist\Shutdown.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Shutdown"; Filename: "{app}\Shutdown.exe"; WorkingDir: "{app}"; Check: IsManagedInstall
Name: "{autodesktop}\Shutdown"; Filename: "{app}\Shutdown.exe"; WorkingDir: "{app}"; Tasks: desktopicon; Check: IsManagedInstall

[Run]
Filename: "{app}\Shutdown.exe"; WorkingDir: "{app}"; Description: "{cm:InstallerRun}"; Flags: nowait postinstall skipifsilent runasoriginaluser; Check: IsManagedInstall

[Code]
const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppGuid}_is1';
  ASFW_ANY = $FFFFFFFF;
  HWND_TOPMOST = -1;
  HWND_NOTOPMOST = -2;
  SWP_NOACTIVATE = $0010;
  SWP_NOMOVE = $0002;
  SWP_NOSIZE = $0001;
  GWL_ID = -12;

var
  ExistingPage: TInputOptionWizardPage;
  ExistingRoot: Integer;
  ExistingComparison: Integer;
  PortableBox: TNewCheckBox;
  ManagedDir, PortableDir: String;
  DeleteAppData, ExistingUninstalled: Boolean;
  PreviousWizardOnShow: TNotifyEvent;

function AllowSetForegroundWindow(ProcessId: Cardinal): Boolean;
  external 'AllowSetForegroundWindow@user32.dll stdcall';
function GetForegroundWindow: HWND;
  external 'GetForegroundWindow@user32.dll stdcall';
function SetForegroundWindow(Window: HWND): Boolean;
  external 'SetForegroundWindow@user32.dll stdcall';
function SetWindowPos(Window, InsertAfter: HWND; X, Y, W, H: Integer; Flags: Cardinal): Boolean;
  external 'SetWindowPos@user32.dll stdcall';
function SetWindowLong(Window: HWND; Index, Value: Integer): Integer;
  external 'SetWindowLongW@user32.dll stdcall';

procedure RaiseWizard;
var
  Window: HWND;
  Attempt: Integer;
begin
  if WizardSilent then
    Exit;
  Window := WizardForm.Handle;
  for Attempt := 1 to 20 do
  begin
    SetWindowPos(Window, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOACTIVATE or SWP_NOMOVE or SWP_NOSIZE);
    SetWindowPos(Window, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOACTIVATE or SWP_NOMOVE or SWP_NOSIZE);
    SetForegroundWindow(Window);
    if (GetForegroundWindow = Window) or not WizardForm.Visible then
      Exit;
    Sleep(25);
  end;
end;

procedure WizardShown(Sender: TObject);
begin
  if PreviousWizardOnShow <> nil then
    PreviousWizardOnShow(Sender);
  RaiseWizard;
end;

function HasSwitch(const Name: String): Boolean;
var
  I: Integer;
begin
  Result := True;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), Name) = 0 then
      Exit;
  Result := False;
end;

function IsManagedInstall: Boolean;
begin
  Result := True;
  if PortableBox <> nil then
    Result := not PortableBox.Checked;
end;

function TryPackVersion(Version: String; var Packed: Int64): Boolean;
var
  Parts: array[0..3] of Word;
  Part: String;
  I, Dot, Number: Integer;
begin
  Result := False;
  for I := 0 to 3 do
  begin
    Parts[I] := 0;
    if Version = '' then
      Continue;
    Dot := Pos('.', Version);
    if Dot = 0 then
      Dot := Length(Version) + 1
    else if Dot = Length(Version) then
      Exit;
    Part := Copy(Version, 1, Dot - 1);
    Number := StrToIntDef(Part, -1);
    if (Number < 0) or (Number > 65535) or (IntToStr(Number) <> Part) then
      Exit;
    Parts[I] := Number;
    Delete(Version, 1, Dot);
  end;
  if Version <> '' then
    Exit;
  Packed := PackVersionComponents(Parts[0], Parts[1], Parts[2], Parts[3]);
  Result := True;
end;

function ReadInstalledVersion(Root: Integer; var Installed: String; var Comparison: Integer): Boolean;
var
  InstalledVersion, OfferedVersion: Int64;
begin
  Result := RegQueryStringValue(Root, UninstallKey, 'DisplayVersion', Installed);
  if not Result then
    Exit;
  if (Installed <> '') and TryPackVersion(Installed, InstalledVersion) and
     TryPackVersion('{#AppVersion}', OfferedVersion) then
    Comparison := ComparePackedVersion(InstalledVersion, OfferedVersion)
  else
    Comparison := 1;
end;

function CheckInstalledVersion(Root: Integer; var Installed: String; var Comparison: Integer;
  var Found: Boolean): Boolean;
begin
  Found := ReadInstalledVersion(Root, Installed, Comparison);
  Result := True;
#ifndef AllowDowngrade
  if Found and (Comparison > 0) then
  begin
    SuppressibleMsgBox(FmtMessage(CustomMessage('InstallerDowngrade'), [Installed, '{#AppVersion}']),
      mbError, MB_OK, IDOK);
    Result := False;
  end;
#endif
end;

function InitializeSetup: Boolean;
var
  UserVersion, MachineVersion: String;
  UserComparison, MachineComparison: Integer;
  UserFound, MachineFound: Boolean;
begin
  AllowSetForegroundWindow(ASFW_ANY);
  Result := CheckInstalledVersion(HKCU, UserVersion, UserComparison, UserFound) and
    CheckInstalledVersion(HKLM, MachineVersion, MachineComparison, MachineFound);
  if not Result then
    Exit;
  if UserFound and (not MachineFound or not IsAdminInstallMode) then
  begin
    ExistingRoot := HKCU;
    ExistingComparison := UserComparison;
  end
  else if MachineFound then
  begin
    ExistingRoot := HKLM;
    ExistingComparison := MachineComparison;
  end;
end;

function RemoveExisting(Interactive: Boolean): Boolean;
var
  Command, Executable, Args: String;
  ExitCode: Integer;
begin
  Result := False;
  if not RegQueryStringValue(ExistingRoot, UninstallKey, 'UninstallString', Command) then
    Exit;
  Executable := RemoveQuotes(Command);
  if not FileExists(Executable) then
    Exit;

  Args := '/NORESTART';
  if not Interactive then
    Args := '/VERYSILENT /SUPPRESSMSGBOXES ' + Args;

  if (ExistingRoot = HKLM) and not IsAdmin then
    Result := ShellExec('runas', Executable, Args, '', SW_SHOWNORMAL, ewWaitUntilTerminated, ExitCode)
  else if (ExistingRoot = HKCU) and IsAdmin then
    Exit
  else
    Result := Exec(Executable, Args, '', SW_SHOWNORMAL, ewWaitUntilTerminated, ExitCode);
  if Result then
    Result := (ExitCode = 0) and not RegKeyExists(ExistingRoot, UninstallKey);
end;

procedure PortableChanged(Sender: TObject);
begin
  if PortableBox.Checked then
  begin
    ManagedDir := WizardForm.DirEdit.Text;
    WizardForm.DirEdit.Text := PortableDir;
    WizardForm.NextButton.Caption := SetupMessage(msgButtonInstall);
  end
  else
  begin
    PortableDir := WizardForm.DirEdit.Text;
    WizardForm.DirEdit.Text := ManagedDir;
    WizardForm.NextButton.Caption := SetupMessage(msgButtonNext);
  end;
end;

procedure InitializeWizard;
var
  Description: TNewStaticText;
  PageDescription: String;
begin
  PreviousWizardOnShow := WizardForm.OnShow;
  WizardForm.OnShow := @WizardShown;
  WizardForm.BeveledLabel.Caption := '{#AppCopyright}';
  WizardForm.BeveledLabel.Visible := True;
  // By default the label sits on the footer line, which then runs through the text: center it in the button row.
  WizardForm.BeveledLabel.Top := WizardForm.NextButton.Top +
    (WizardForm.NextButton.Height - WizardForm.BeveledLabel.Height) div 2;
  WizardForm.WelcomeLabel1.Caption := FmtMessage(CustomMessage('InstallerWelcome'), ['{#AppVersion}']);
  WizardForm.WelcomeLabel2.Caption := CustomMessage('InstallerWelcomeDescription');

  if (ExistingRoot <> 0) and not (WizardSilent or HasSwitch('/UPDATE')) then
  begin
    if ExistingComparison = 0 then
      PageDescription := CustomMessage('InstallerSameVersion')
    else
      PageDescription := CustomMessage('InstallerReplaceVersion');
    ExistingPage := CreateInputOptionPage(wpWelcome, CustomMessage('InstallerExisting'),
      PageDescription, '', True, False);
    SetWindowLong(ExistingPage.CheckListBox.Handle, GWL_ID, 1001);
    if ExistingComparison = 0 then
    begin
      ExistingPage.Add(CustomMessage('InstallerReinstall'));
      ExistingPage.Add(CustomMessage('InstallerUninstall'));
    end
    else
    begin
      ExistingPage.Add(CustomMessage('InstallerUninstallFirst'));
      ExistingPage.Add(CustomMessage('InstallerKeepInstalled'));
    end;
    ExistingPage.Values[0] := True;
  end;

  PortableDir := ExpandConstant('{src}');
  PortableBox := TNewCheckBox.Create(WizardForm);
  PortableBox.Parent := WizardForm.DirEdit.Parent;
  PortableBox.SetBounds(WizardForm.DirEdit.Left, WizardForm.DirEdit.Top + ScaleY(40),
    WizardForm.DirEdit.Width, ScaleY(20));
  PortableBox.Caption := CustomMessage('InstallerPortable');
  PortableBox.OnClick := @PortableChanged;
  SetWindowLong(PortableBox.Handle, GWL_ID, 1002);

  Description := TNewStaticText.Create(WizardForm);
  Description.Parent := PortableBox.Parent;
  Description.AutoSize := False;
  Description.WordWrap := True;
  Description.SetBounds(PortableBox.Left + ScaleX(18), PortableBox.Top + PortableBox.Height,
    PortableBox.Width - ScaleX(18), ScaleY(38));
  Description.Caption := CustomMessage('InstallerPortableDescription');
  if WizardSilent or HasSwitch('/UPDATE') then
  begin
    PortableBox.Visible := False;
    Description.Visible := False;
  end;
  RaiseWizard;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpSelectDir) and not IsManagedInstall then
    WizardForm.NextButton.Caption := SetupMessage(msgButtonInstall);
  RaiseWizard;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := not IsManagedInstall and ((PageID = wpSelectProgramGroup) or (PageID = wpSelectTasks));
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if ExistingPage = nil then
    Exit;
  if (CurPageID = ExistingPage.ID) and (ExistingComparison = 0) and ExistingPage.Values[1] then
  begin
    Result := False;
    if RemoveExisting(True) then
    begin
      ExistingUninstalled := True;
      WizardForm.Close;
    end
    else if RegKeyExists(ExistingRoot, UninstallKey) then
      MsgBox(CustomMessage('InstallerPreviousUninstallFailed'), mbError, MB_OK);
  end;
end;

procedure CancelButtonClick(CurPageID: Integer; var Cancel, Confirm: Boolean);
begin
  if ExistingUninstalled then
    Confirm := False;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if ExistingPage = nil then
    Exit;
  if IsManagedInstall and (ExistingComparison <> 0) and ExistingPage.Values[0] and not ExistingUninstalled then
  begin
    ExistingUninstalled := RemoveExisting(False);
    if not ExistingUninstalled then
      Result := CustomMessage('InstallerPreviousUninstallFailed');
  end;
end;

function InitializeUninstall: Boolean;
var
  Dialog: TSetupForm;
  DeleteBox: TNewCheckBox;
  ConfirmButton, CancelButton: TNewButton;
begin
  Result := True;
  if UninstallSilent then
    Exit;

  Dialog := CreateCustomForm(ScaleX(350), ScaleY(84), False, False);
  try
    Dialog.Caption := CustomMessage('InstallerUninstall');
    Dialog.Position := poScreenCenter;

    DeleteBox := TNewCheckBox.Create(Dialog);
    DeleteBox.Parent := Dialog;
    DeleteBox.SetBounds(ScaleX(16), ScaleY(12), ScaleX(318), ScaleY(22));
    DeleteBox.Caption := CustomMessage('InstallerDeleteAppData');
    SetWindowLong(DeleteBox.Handle, GWL_ID, 1003);

    ConfirmButton := TNewButton.Create(Dialog);
    ConfirmButton.Parent := Dialog;
    ConfirmButton.SetBounds(ScaleX(144), ScaleY(46), ScaleX(92), ScaleY(24));
    ConfirmButton.Caption := CustomMessage('InstallerUninstall');
    ConfirmButton.Default := True;
    ConfirmButton.ModalResult := mrOk;
    SetWindowLong(ConfirmButton.Handle, GWL_ID, 1004);

    CancelButton := TNewButton.Create(Dialog);
    CancelButton.Parent := Dialog;
    CancelButton.SetBounds(ScaleX(244), ScaleY(46), ScaleX(90), ScaleY(24));
    CancelButton.Caption := SetupMessage(msgButtonCancel);
    CancelButton.Cancel := True;
    CancelButton.ModalResult := mrCancel;
    SetWindowLong(CancelButton.Handle, GWL_ID, 1005);

    Result := Dialog.ShowModal = mrOk;
    DeleteAppData := Result and DeleteBox.Checked;
  finally
    Dialog.Free;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if (CurUninstallStep <> usPostUninstall) or not DeleteAppData then
    Exit;
  DataDir := ExpandConstant('{userappdata}\Shutdown');
  if DirExists(DataDir) and not DelTree(DataDir, True, True, True) then
    MsgBox(CustomMessage('InstallerDeleteAppDataFailed'), mbError, MB_OK);
end;
