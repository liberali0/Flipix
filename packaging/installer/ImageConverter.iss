; Flipix(플리픽스) 설치 마법사 (Inno Setup 6)
; 컴파일: ISCC /DAppVersion=1.0.0 /DSourceDir=<publish 폴더> ImageConverter.iss
#ifndef AppVersion
  #define AppVersion "0.0.1"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\publish"
#endif
#ifndef OutDir
  #define OutDir "..\out"
#endif
#define AppName "Flipix"
#define AppExe "ImageConverter.exe"
#define ShellDll "ImageConverter.ShellExtension.dll"
#define KoreanIsl AddBackslash(CompilerPath) + "Languages\Korean.isl"

[Setup]
; 고정 GUID: 업데이트 때 같은 앱으로 인식하는 데 쓰입니다. 바꾸지 마세요.
AppId={{6B0E5F52-3C1B-4D52-9E58-4F1C2A7D9A10}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Flipix
DefaultDirName={autopf}\QuickImageConverter
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
SetupIconFile=..\..\assets\branding\flipix.ico
OutputDir={#OutDir}
OutputBaseFilename=Flipix-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; 기본은 현재 사용자 전용 설치(관리자 권한 불필요). 마법사에서 모든 사용자 설치도 고를 수 있습니다.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DisableProgramGroupPage=yes
CloseApplications=yes
; 업데이트할 때 닫을 프로그램은 앱·CLI(exe)만 찾습니다. 우클릭 메뉴 DLL은 탐색기가 물고 있어서
; 이 필터가 없으면 "탐색기를 닫을까요?"가 뜹니다. DLL은 아래 PrepareToInstall에서 이름을 바꿔 교체합니다.
CloseApplicationsFilter=*.exe
RestartApplications=no
WizardStyle=modern

[Languages]
#if FileExists(KoreanIsl)
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
#endif
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
english.TaskContextMenu=Add "Quick image format convert" to the right-click menu
english.TaskContextMenuNote=On Windows 11 it appears at the top of the menu (one administrator confirmation trusts the package certificate; if declined, it appears under "Show more options").
english.TaskDesktopIcon=Create a desktop shortcut
english.TaskPath=Add the command-line tool (imgconv) to PATH
english.RunApp=Launch {#AppName}
#if FileExists(KoreanIsl)
korean.TaskContextMenu=우클릭 메뉴에 "빠른 이미지 포맷 변환" 추가
korean.TaskContextMenuNote=Windows 11에서는 첫 화면에 나타납니다(인증서 신뢰 등록에 관리자 확인 한 번 필요, 거절하면 "더 많은 옵션 표시" 안).
korean.TaskDesktopIcon=바탕 화면에 바로 가기 만들기
korean.TaskPath=명령줄 도구(imgconv)를 PATH에 추가
korean.RunApp={#AppName} 실행
#endif

[Tasks]
Name: "contextmenu"; Description: "{cm:TaskContextMenu}"; GroupDescription: "{cm:TaskContextMenuNote}"
Name: "desktopicon"; Description: "{cm:TaskDesktopIcon}"; Flags: unchecked
Name: "addtopath"; Description: "{cm:TaskPath}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Win11 첫 화면 메뉴 등록 도우미. 메뉴 패키지(ImageConverter.ContextMenu.msix)와 인증서(.cer)는 release.yml이 publish 폴더에 넣습니다.
Source: "ContextMenu.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; 사용자 PATH에 설치 폴더 추가(현재 사용자 설치일 때). 모든 사용자 설치면 시스템 PATH.
Root: HKA; Subkey: "{code:EnvKey}"; ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}"; Tasks: addtopath; Check: NeedsPath(ExpandConstant('{app}'))

[Run]
; 우클릭 메뉴 등록은 [Code]의 CurStepChanged에서 합니다(Win11 첫 화면 메뉴 또는 기존 방식).
Filename: "{app}\{#AppExe}"; Description: "{cm:RunApp}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallDelete]
; 업데이트 때 이름을 바꿔 둔 예전 우클릭 메뉴 DLL
Type: files; Name: "{app}\*.dll.old"

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File ""{app}\ContextMenu.ps1"" -Action Unregister"; Flags: runhidden; RunOnceId: "UnregMenuPackage"
Filename: "{sys}\regsvr32.exe"; Parameters: "/u /s ""{app}\{#ShellDll}"""; Flags: runhidden; RunOnceId: "UnregShell"

[Code]
function EnvKey(Param: string): string;
begin
  if IsAdminInstallMode then
    Result := 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment'
  else
    Result := 'Environment';
end;

function NeedsPath(Dir: string): Boolean;
var
  Orig: string;
  Root: Integer;
begin
  if IsAdminInstallMode then Root := HKEY_LOCAL_MACHINE else Root := HKEY_CURRENT_USER;
  if not RegQueryStringValue(Root, EnvKey(''), 'Path', Orig) then
  begin
    Result := True;
    exit;
  end;
  Result := Pos(';' + Uppercase(Dir) + ';', ';' + Uppercase(Orig) + ';') = 0;
end;

// 탐색기가 불러 둔 우클릭 메뉴 DLL은 덮어쓸 수 없지만 이름은 바꿀 수 있습니다.
// 업데이트 전에 예전 DLL을 *.dll.old로 옮겨 두면 새 DLL을 바로 설치할 수 있고, 탐색기를 닫을 필요가 없습니다.
// 예전 DLL은 탐색기가 놓아준 뒤 다음 업데이트나 제거 때 지워집니다. 사용자 설정(%LOCALAPPDATA%)은 건드리지 않습니다.
procedure DeleteOldShellDlls(Dir: string);
var
  Rec: TFindRec;
begin
  if FindFirst(Dir + '\*.dll.old', Rec) then
  begin
    try
      repeat
        DeleteFile(Dir + '\' + Rec.Name); // 아직 쓰이는 중이면 실패하고 다음에 다시 시도
      until not FindNext(Rec);
    finally
      FindClose(Rec);
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Dir, Dll: string;
begin
  Result := '';
  Dir := ExpandConstant('{app}');
  Dll := Dir + '\{#ShellDll}';
  DeleteOldShellDlls(Dir);
  if FileExists(Dll) and not DeleteFile(Dll) then
    RenameFile(Dll, Dll + '.' + GetDateTimeString('yyyymmddhhnnss', #0, #0) + '.old');
end;

// ---- 우클릭 메뉴 등록 ----
// Windows 11 첫 화면 메뉴는 패키지 신원이 있어야 나옵니다. 설치 폴더를 외부 위치로 하는 작은 메뉴 패키지를 등록해서
// 신원을 얻고, 그 패키지의 서명 인증서는 관리자 확인 한 번으로 이 PC에서 신뢰하도록 등록합니다.
// 확인을 거절하거나 Windows 10이면 기존 방식(regsvr32, Win11에선 "더 많은 옵션 표시" 안)으로 등록합니다.
// 두 방식이 함께 등록되면 메뉴가 두 번 보이므로 한쪽만 남깁니다.
function PowerShellExe: string;
begin
  Result := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
end;

// 관리자 권한 없이 현재 사용자 권한으로만 실행합니다. 설치 폴더(기본값 %LOCALAPPDATA%\Programs)는
// 사용자가 쓸 수 있는 곳이라, 그 안의 스크립트는 절대 관리자 권한으로 실행하지 않습니다.
function RunMenuScript(Action: string): Boolean;
var
  Params: string;
  Code: Integer;
begin
  Params := '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' +
    ExpandConstant('{app}\ContextMenu.ps1') + '" -Action ' + Action;
  Result := ExecAsOriginalUser(PowerShellExe, Params, '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
end;

// 메뉴 패키지 인증서를 이 PC에서 신뢰하도록 등록합니다(관리자 확인 한 번).
// 보안: 관리자 권한으로 도는 명령에는 디스크의 파일을 하나도 쓰지 않습니다. 인증서 자체가 빌드할 때
// 명령 안에 그대로 들어가 있으므로(build-installer.ps1의 TrustCommand), 누가 설치 폴더의 .cer나 .ps1을
// 바꿔치기해도 다른 인증서가 신뢰 목록에 들어가거나 다른 코드가 관리자 권한으로 실행되지 않습니다.
function TrustMenuCertificate: Boolean;
var
  Code: Integer;
begin
#ifdef TrustCommand
  // ShellExec는 프로세스 종료 코드를 돌려주지 않으므로 성공 여부는 호출한 쪽에서 다시 확인합니다.
  Result := ShellExec('runas', PowerShellExe,
    '-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {#TrustCommand}',
    '', SW_HIDE, ewWaitUntilTerminated, Code);
#else
  Result := False;
#endif
end;

procedure RunRegsvr32(Params: string);
var
  Code: Integer;
begin
  ExecAsOriginalUser(ExpandConstant('{sys}\regsvr32.exe'), Params + ' "' + ExpandConstant('{app}\{#ShellDll}') + '"',
    '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

function IsWindows11: Boolean;
var
  V: TWindowsVersion;
begin
  GetWindowsVersionEx(V);
  Result := (V.Major = 10) and (V.Build >= 22000);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  TopMenu: Boolean;
begin
  if CurStep <> ssPostInstall then exit;

  if not WizardIsTaskSelected('contextmenu') then
  begin
    // 이번 설치에서 메뉴를 끄면 예전 버전이 남긴 등록도 지웁니다.
    RunMenuScript('Unregister');
    RunRegsvr32('/u /s');
    exit;
  end;

  TopMenu := False;
  if IsWindows11 and FileExists(ExpandConstant('{app}\ImageConverter.ContextMenu.msix')) then
  begin
    if not RunMenuScript('IsTrusted') then
      TrustMenuCertificate; // 관리자 확인 창(UAC). 거절하면 아래 확인에서 걸러짐
    if RunMenuScript('IsTrusted') then
      TopMenu := RunMenuScript('Register');
  end;

  if TopMenu then
    RunRegsvr32('/u /s')
  else
  begin
    RunMenuScript('Unregister');
    RunRegsvr32('/s');
  end;
end;
