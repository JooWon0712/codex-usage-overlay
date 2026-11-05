; Build only through setup.ps1, with an already validated portable payload.
#if Ver != EncodeVer(6, 7, 4)
  #error Inno Setup 6.7.4 is required.
#endif
#ifndef PayloadDirectory
  #error setup.ps1 must supply PayloadDirectory.
#endif
#ifndef AppVersionFull
  #error setup.ps1 must supply AppVersionFull.
#endif

[Setup]
; This identity must remain unchanged across upgrades.
AppId={{18D4E149-28A8-4F05-B98E-10B66B725C54}
AppName=Codex Usage Overlay
AppVersion={#AppVersionFull}
AppVerName=Codex Usage Overlay {#AppVersionFull}
AppPublisher=Codex Usage Overlay contributors
AppPublisherURL=https://github.com/JooWon0712/codex-usage-overlay
VersionInfoVersion={#AppVersionFull}
DefaultDirName={localappdata}\Programs\CodexUsageOverlay
UsePreviousAppDir=yes
DefaultGroupName=Codex Usage Overlay
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
SetupMutex=Local\PersonalCodexUsageOverlaySetup
CloseApplications=no
RestartApplications=no
RestartIfNeededByRun=no
OutputBaseFilename=codex-usage-overlay-setup-windows-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile={#RepositoryRoot}\assets\app.ico
UninstallDisplayIcon={app}\UsageOverlay.exe
InfoBeforeFile={#RepositoryRoot}\installer\requirements.txt
Uninstallable=yes
CreateUninstallRegKey=yes
UninstallDisplayName=Codex Usage Overlay
DisableWelcomePage=no

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; The helper is extracted from these same payload bytes before changing files.
Source: "{#PayloadDirectory}\UsageOverlay.exe"; DestDir: "{app}"; Flags: ignoreversion; Hash: "{#PayloadHash0}"
Source: "{#PayloadDirectory}\runtime-paths.json"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall; Hash: "{#PayloadHash1}"
Source: "{#PayloadDirectory}\worker.mjs"; DestDir: "{app}"; Flags: ignoreversion; Hash: "{#PayloadHash2}"
Source: "{#PayloadDirectory}\probe.mjs"; DestDir: "{app}"; Flags: ignoreversion; Hash: "{#PayloadHash3}"
Source: "{#PayloadDirectory}\usage-state.mjs"; DestDir: "{app}"; Flags: ignoreversion; Hash: "{#PayloadHash4}"
Source: "{#PayloadDirectory}\README.md"; DestDir: "{app}"; Flags: ignoreversion; Hash: "{#PayloadHash5}"
Source: "{#PayloadDirectory}\사용안내.txt"; DestDir: "{app}"; Flags: ignoreversion; Hash: "{#PayloadHash6}"

[Icons]
Name: "{autoprograms}\Codex Usage Overlay"; Filename: "{app}\UsageOverlay.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Codex Usage Overlay"; Filename: "{app}\UsageOverlay.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[CustomMessages]
english.ClosePrompt=Codex Usage Overlay is running. Close it normally and wait for its owned background work to exit before continuing? It will not be restarted automatically.
korean.ClosePrompt=Codex Usage Overlay가 실행 중입니다. 정상 종료를 요청하고 이 앱이 실행한 작업의 종료를 기다린 뒤 계속할까요? 설치 후 자동으로 다시 실행하지 않습니다.
english.Busy=Close Codex Usage Overlay with its X or Exit button, then retry. Silent setup may close a verified instance only when /ALLOWAPPCLOSE=1 is supplied. No application files were changed.
korean.Busy=Codex Usage Overlay의 X 또는 종료 버튼으로 앱을 닫은 뒤 다시 시도하세요. 무인 설치에서 확인된 앱의 정상 종료를 허용하려면 /ALLOWAPPCLOSE=1을 명시해야 합니다. 앱 파일은 변경하지 않았습니다.
english.Legacy=The running instance cannot be safely identified or does not support verified shutdown. Close Codex Usage Overlay with its X or Exit button, then retry. Other applications will not be stopped.
korean.Legacy=실행 중인 버전의 신원 또는 종료 여부를 안전하게 확인할 수 없습니다. Codex Usage Overlay의 X 또는 종료 버튼으로 닫은 뒤 다시 시도하세요. 다른 앱을 종료하지 않습니다.
english.PrepareFailed=The application could not be verified as fully stopped. No installation or removal was started. Close the app and retry. Details are in the setup log.
korean.PrepareFailed=앱과 소유 작업의 완전 종료를 확인하지 못했습니다. 설치 또는 제거를 시작하지 않았습니다. 앱을 닫은 뒤 다시 시도하세요. 자세한 내용은 설치 로그에 있습니다.
english.LockFailed=Another application startup, setup or update is in progress. Wait for it to finish, then retry.
korean.LockFailed=앱 시작, 설치 또는 업데이트가 진행 중입니다. 완료된 뒤 다시 시도하세요.
english.LocationChanged=Update in the existing installation folder. To choose another folder, uninstall the existing copy first, then install again. No application files were changed.
korean.LocationChanged=기존 설치 폴더에서 업데이트하세요. 다른 폴더로 옮기려면 기존 앱을 제거한 뒤 다시 설치하세요. 앱 파일은 변경하지 않았습니다.

[Code]
const
  UpdateMutexName = 'Local\PersonalCodexUsageOverlayUpdateLock';
  WaitObject = 0;
  WaitAbandoned = $80;
var
  UpdateMutex: THandle;
  UpdateOwned: Boolean;
  HelperExtracted: Boolean;
  UninstallPrepared: Boolean;

function NativeCreateMutex(Attributes: Integer; InitialOwner: Boolean; Name: String): THandle;
  external 'CreateMutexW@kernel32.dll stdcall';
function NativeWait(Handle: THandle; Milliseconds: LongWord): LongWord;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function NativeReleaseMutex(Handle: THandle): Boolean;
  external 'ReleaseMutex@kernel32.dll stdcall';
function NativeCloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

procedure ReleaseUpdateLock;
begin
  if UpdateOwned then begin
    NativeReleaseMutex(UpdateMutex);
    UpdateOwned := False;
  end;
  if UpdateMutex <> 0 then begin
    NativeCloseHandle(UpdateMutex);
    UpdateMutex := 0;
  end;
end;

function AcquireUpdateLock: Boolean;
var WaitResult: LongWord;
begin
  Result := UpdateOwned;
  if Result then exit;
  UpdateMutex := NativeCreateMutex(0, False, UpdateMutexName);
  if UpdateMutex = 0 then exit;
  WaitResult := NativeWait(UpdateMutex, 5000);
  UpdateOwned := (WaitResult = WaitObject) or (WaitResult = WaitAbandoned);
  Result := UpdateOwned;
  if not Result then ReleaseUpdateLock;
end;

function RunPrepare(const Helper, Target: String; NoClose: Boolean): Integer;
var Params: String;
begin
  Params := '--prepare-install --target "' + Target + '" --wait 30000';
  if NoClose then Params := Params + ' --no-close';
  if not Exec(Helper, Params, ExtractFileDir(Helper), SW_HIDE,
      ewWaitUntilTerminated, Result) then begin
    Log(Format('Could not execute prepare helper: %d', [Result]));
    Result := -1;
  end else Log(Format('Prepare helper returned %d (no-close=%d)', [Result, Ord(NoClose)]));
end;

function PrepareApplication(const Helper, Target: String; SilentMode: Boolean): String;
var Code: Integer;
begin
  Result := CustomMessage('PrepareFailed');
  Code := RunPrepare(Helper, Target, True);
  if Code = 0 then begin Result := ''; exit; end;
  if Code = 3 then begin Result := CustomMessage('Legacy'); exit; end;
  if Code <> 2 then exit;
  Result := CustomMessage('Busy');
  if SilentMode then begin
    if ExpandConstant('{param:ALLOWAPPCLOSE|0}') <> '1' then exit;
  end else if MsgBox(CustomMessage('ClosePrompt'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) <> IDYES then exit;
  Code := RunPrepare(Helper, Target, False);
  if Code = 0 then Result := ''
  else if Code = 3 then Result := CustomMessage('Legacy')
  else Result := CustomMessage('PrepareFailed');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var PreviousDirectory: String;
begin
  NeedsRestart := False;
  Result := CustomMessage('LocationChanged');
  if RegKeyExists(HKCU64, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{18D4E149-28A8-4F05-B98E-10B66B725C54}_is1') then begin
    if not RegQueryStringValue(HKCU64, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{18D4E149-28A8-4F05-B98E-10B66B725C54}_is1', 'InstallLocation', PreviousDirectory) then exit;
    if (PreviousDirectory = '') or (CompareText(RemoveBackslashUnlessRoot(PreviousDirectory), RemoveBackslashUnlessRoot(ExpandConstant('{app}'))) <> 0) then exit;
  end;
  Result := CustomMessage('LockFailed');
  if not AcquireUpdateLock then exit;
  try
    if not HelperExtracted then begin
      ExtractTemporaryFile('UsageOverlay.exe');
      HelperExtracted := True;
    end;
    Result := PrepareApplication(ExpandConstant('{tmp}\UsageOverlay.exe'), ExpandConstant('{app}'), WizardSilent);
  except
    Log(GetExceptionMessage);
    Result := CustomMessage('PrepareFailed');
  end;
  if Result <> '' then ReleaseUpdateLock;
end;

procedure DeinitializeSetup;
begin
  ReleaseUpdateLock;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Error: String; Helper: String;
begin
  if (CurUninstallStep <> usAppMutexCheck) and (CurUninstallStep <> usUninstall) then exit;
  if UninstallPrepared then exit;
  Error := CustomMessage('LockFailed');
  if AcquireUpdateLock then begin
    Helper := ExpandConstant('{tmp}\UsageOverlay-prepare-uninstall.exe');
    if FileCopy(ExpandConstant('{app}\UsageOverlay.exe'), Helper, False) then
      if CompareText(GetSHA256OfFile(Helper), '{#PayloadHash0}') = 0 then
        Error := PrepareApplication(Helper, ExpandConstant('{app}'), UninstallSilent)
      else Error := CustomMessage('PrepareFailed')
    else Error := CustomMessage('PrepareFailed');
  end;
  if Error <> '' then begin
    Log(Error);
    if not UninstallSilent then MsgBox(Error, mbError, MB_OK);
    ReleaseUpdateLock;
    Abort;
  end;
  UninstallPrepared := True;
end;

procedure DeinitializeUninstall;
begin
  ReleaseUpdateLock;
end;
