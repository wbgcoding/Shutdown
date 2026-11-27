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
DisableDirPage=no
; One shortcut goes straight into the Start menu, no folder of its own (Windows guideline for a single app).
DisableProgramGroupPage=yes
DisableReadyPage=yes
; Installing over an existing folder is always what the user wants (update, repair, portable copy).
DirExistsWarning=no
DisableWelcomePage=no
WizardStyle=modern
WizardImageFile=WizardImage-100.png,WizardImage-125.png,WizardImage-150.png,WizardImage-175.png,WizardImage-200.png,WizardImage-225.png,WizardImage-250.png
WizardSmallImageFile=WizardSmallImage-100.png,WizardSmallImage-125.png,WizardSmallImage-150.png,WizardSmallImage-175.png,WizardSmallImage-200.png,WizardSmallImage-225.png,WizardSmallImage-250.png
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
; An update keeps the scope of the installed copy without asking again.
UsePreviousPrivileges=yes
UsePreviousLanguage=yes
ArchitecturesInstallIn64BitMode=x64os or arm64
MinVersion=10.0.18362
ShowLanguageDialog=no
LanguageDetectionMethod=uilanguage
Compression=lzma2/ultra64
SolidCompression=yes
SetupIconFile=..\src\Shutdown\Assets\Shutdown.ico
UninstallDisplayIcon={app}\Shutdown.exe
; A running copy is asked to close in PrepareToInstall and CurUninstallStepChanged, so no AppMutex prompt.
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

[Messages]
; The ready page is off, so the tasks page starts the install: its text names that button.
en.SelectTasksLabel2=Select the additional tasks you would like Setup to perform while installing [name], then click Install.
de.SelectTasksLabel2=Wählen Sie die zusätzlichen Aufgaben aus, die das Setup während der Installation von [name] ausführen soll, und klicken Sie danach auf „Installieren“.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"

[InstallDelete]
; Older versions put the shortcut into a Start menu folder of its own.
Type: files; Name: "{autoprograms}\Shutdown\Shutdown.lnk"; Check: IsManagedInstall
Type: dirifempty; Name: "{autoprograms}\Shutdown"; Check: IsManagedInstall

[Files]
Source: "..\dist\Shutdown.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Shutdown"; Filename: "{app}\Shutdown.exe"; WorkingDir: "{app}"; Check: IsManagedInstall
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
  EVENT_MODIFY_STATE = $0002;
  InstanceMutex = 'Local\BGCoding.Shutdown.Instance';
  QuitEvent = 'Local\BGCoding.Shutdown.Quit';

var
  ExistingRoot: Integer;
  PortableBox: TNewCheckBox;
  ManagedDir, PortableDir: String;
  DeleteAppData: Boolean;
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

function OpenEvent(Access: Cardinal; Inherit: Boolean; Name: String): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(Handle: THandle): Boolean;
  external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

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
    // A damaged version entry must not block every install; the files are simply replaced.
    Comparison := -1;
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

// A running exe cannot be opened for writing, so this tells when its process has really exited.
function FileUnlocked(const Path: String): Boolean;
var
  Stream: TFileStream;
begin
  Result := True;
  if not FileExists(Path) then
    Exit;
  try
    Stream := TFileStream.Create(Path, fmOpenReadWrite or fmShareDenyNone);
    Stream.Free;
  except
    Result := False;
  end;
end;

function WaitUntilClosed(const Exe: String; Milliseconds: Integer): Boolean;
var
  Waited: Integer;
begin
  Waited := 0;
  while (CheckForMutexes(InstanceMutex) or not FileUnlocked(Exe)) and (Waited < Milliseconds) do
  begin
    Sleep(100);
    Waited := Waited + 100;
  end;
  Result := not CheckForMutexes(InstanceMutex) and FileUnlocked(Exe);
end;

// Asks a running Shutdown to close, the same way a newer version takes over. It stays open only while a hibernate
// or sleep countdown needs it (or a schedule could not be saved); then the user decides whether to end it.
function CloseRunningApp(const Exe: String): Boolean;
var
  Handle: THandle;
  ResultCode: Integer;
begin
  Result := True;
  if not CheckForMutexes(InstanceMutex) and FileUnlocked(Exe) then
    Exit;
  Handle := OpenEvent(EVENT_MODIFY_STATE, False, QuitEvent);
  if Handle <> 0 then
  begin
    SetEvent(Handle);
    CloseHandle(Handle);
  end;
  if WaitUntilClosed(Exe, 5000) then
    Exit;
  Result := SuppressibleMsgBox(CustomMessage('InstallerCloseAnyway'), mbConfirmation, MB_YESNO, IDNO) = IDYES;
  if not Result then
    Exit;
  // Only this account's copies: an elevated setup must not end Shutdown in another user's session.
  if not Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM Shutdown.exe /FI "USERNAME eq ' + GetUserNameString + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    ResultCode := -1;
  Log('taskkill exit code ' + IntToStr(ResultCode));
  Result := WaitUntilClosed(Exe, 5000);
  // A schedule the app could not save would otherwise keep its Windows timer with nothing left to show or cancel it,
  // so it is cancelled as the message box said.
  if Result then
    Exec(ExpandConstant('{sys}\shutdown.exe'), '/a', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
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
    ExistingRoot := HKCU
  else if MachineFound then
    ExistingRoot := HKLM;
end;

// Removes a copy installed for the other scope (all users or only me), so the two never sit side by side.
function RemoveExisting: Boolean;
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

  Args := '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART';

  if (ExistingRoot = HKLM) and not IsAdmin then
    Result := ShellExec('runas', Executable, Args, '', SW_SHOWNORMAL, ewWaitUntilTerminated, ExitCode)
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
begin
  PreviousWizardOnShow := WizardForm.OnShow;
  WizardForm.OnShow := @WizardShown;
  WizardForm.BeveledLabel.Caption := '{#AppCopyright}';
  WizardForm.BeveledLabel.Visible := True;
  // By default the label sits on the footer line, which then runs through the text, and touches the left window edge:
  // center it in the button row and line it up with the page content, at every display scale.
  WizardForm.BeveledLabel.Top := WizardForm.NextButton.Top +
    (WizardForm.NextButton.Height - WizardForm.BeveledLabel.Height) div 2;
  WizardForm.BeveledLabel.Left := WizardForm.InnerNotebook.Left;
  WizardForm.WelcomeLabel1.Caption := CustomMessage('InstallerWelcome');
  WizardForm.WelcomeLabel2.Caption := FmtMessage(CustomMessage('InstallerWelcomeDescription'), ['{#AppVersion}']);

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
  // Without a ready page the last page before copying starts the install, so its button says so.
  if ((CurPageID = wpSelectDir) and not IsManagedInstall) or (CurPageID = wpSelectTasks) then
    WizardForm.NextButton.Caption := SetupMessage(msgButtonInstall);
  RaiseWizard;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := not IsManagedInstall and (PageID = wpSelectTasks);
end;

function InstallRoot: Integer;
begin
  if IsAdminInstallMode then
    Result := HKLM
  else
    Result := HKCU;
end;

// Runs once the user clicks Install: an update of the same scope simply replaces the files and keeps the settings.
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not CloseRunningApp(ExpandConstant('{app}\Shutdown.exe')) then
  begin
    Result := CustomMessage('InstallerCloseFailed');
    Exit;
  end;
  if not IsManagedInstall or (ExistingRoot = 0) or (ExistingRoot = InstallRoot) then
    Exit;
  // A per-user copy has a user-writable uninstaller; it never runs with admin rights, so say what to do instead.
  if ExistingRoot = HKCU then
  begin
    Result := CustomMessage('InstallerPerUserFirst');
    Exit;
  end;
  if not RemoveExisting then
    Result := CustomMessage('InstallerPreviousUninstallFailed');
end;

procedure DeleteDataFile(const Path: String; var Failed: Boolean);
begin
  if FileExists(Path) and not DeleteFile(Path) then
    Failed := True;
end;

// Shutdown and restart run on the Windows timer. After an interactive uninstall nothing could cancel a pending one,
// so it is cancelled here. A silent uninstall is skipped: setup runs it for an update, and the new version keeps the schedule.
procedure CancelPendingTimer(const DataDir: String);
var
  Json: AnsiString;
  Text: String;
  ResultCode: Integer;
begin
  if not LoadStringFromFile(DataDir + '\Shutdown.settings.json', Json) then
    Exit;
  Text := String(Json);
  StringChangeEx(Text, ' ', '', True);
  if (Pos('"PendingAction":"Shutdown"', Text) > 0) or (Pos('"PendingAction":"Restart"', Text) > 0) then
    Exec(ExpandConstant('{sys}\shutdown.exe'), '/a', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

// The files cannot be removed while the app runs, so close it before anything is removed or stop here.
function InitializeUninstall: Boolean;
begin
  Result := CloseRunningApp(ExpandConstant('{app}\Shutdown.exe'));
  if not Result then
    SuppressibleMsgBox(CustomMessage('InstallerCloseFailed'), mbError, MB_OK, IDOK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
  Failed: Boolean;
begin
  DataDir := ExpandConstant('{userappdata}\Shutdown');
  if (CurUninstallStep = usUninstall) and not UninstallSilent then
  begin
    CancelPendingTimer(DataDir);
    // Asked after the standard confirmation, which Inno Setup always shows; data stays unless the user opts in.
    DeleteAppData := MsgBox(CustomMessage('InstallerDeleteAppData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
  end;
  if (CurUninstallStep <> usPostUninstall) or not DeleteAppData then
    Exit;
  // Only the app's own files: the folder name is generic, so anything else in it stays.
  Failed := False;
  DeleteDataFile(DataDir + '\Shutdown.settings.json', Failed);
  DeleteDataFile(DataDir + '\Shutdown.settings.json.new', Failed);
  DeleteDataFile(DataDir + '\Shutdown.settings.json.bad', Failed);
  DeleteDataFile(DataDir + '\logs\shutdown.log', Failed);
  DeleteDataFile(DataDir + '\logs\shutdown.log.1', Failed);
  RemoveDir(DataDir + '\logs');
  RemoveDir(DataDir);
  if Failed then
    MsgBox(CustomMessage('InstallerDeleteAppDataFailed'), mbError, MB_OK);
end;
