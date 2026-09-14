#ifndef AppVersion
  #define AppVersion "0.2.7"
#endif

#define AppName "超智能 Harness"
#define AppPublisher "超智能战斗轮椅有限公司"
#define AppExecutable "CompanyHarness.exe"
#define StageDir "..\artifacts\windows-installer-stage"

[Setup]
AppId={{5EA7A052-5994-4502-B66D-84D559813C14}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} 安装程序
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
VersionInfoVersion={#AppVersion}.0
DefaultDirName={localappdata}\Programs\SmartWheelchairHarness
DefaultGroupName={#AppName}
DisableProgramGroupPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\artifacts\installer
OutputBaseFilename=CompanyHarness-Setup-{#AppVersion}-win-x64
SetupIconFile={#StageDir}\CompanyHarness.ico
UninstallDisplayIcon={app}\{#AppExecutable}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
CloseApplications=yes
CloseApplicationsFilter={#AppExecutable}
RestartApplications=no
RestartIfNeededByRun=no
ExtraDiskSpaceRequired=209715200
UsePreviousAppDir=yes
UsePreviousGroup=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加选项："; Flags: unchecked

[Files]
Source: "{#StageDir}\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#StageDir}\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; DestName: "MicrosoftEdgeWebview2Setup.exe"; Flags: deleteafterinstall; Check: not IsWebView2Installed

[InstallDelete]
Type: filesandordirs; Name: "{app}\runtime"
Type: filesandordirs; Name: "{app}\runtime-*"

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExecutable}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExecutable}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "正在安装 Microsoft Edge WebView2 Runtime…"; Flags: runhidden waituntilterminated; Check: not IsWebView2Installed
Filename: "{app}\{#AppExecutable}"; Parameters: "{code:GetPostInstallParameters}"; Description: "启动 {#AppName}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\{#AppExecutable}"; Parameters: "--clear-user-data"; Flags: runhidden waituntilterminated; Check: ShouldClearUserData; RunOnceId: "ClearCurrentUserData"

[Code]
var
  ClearUserData: Boolean;

function GetCurrentProcessId(): DWORD;
  external 'GetCurrentProcessId@kernel32.dll stdcall';

function GetPostInstallParameters(Param: String): String;
begin
  Result := Format('--post-install --wait-for-process %d', [GetCurrentProcessId()]);
end;

function IsUsableWebView2Version(const Version: String): Boolean;
begin
  Result := (Version <> '') and (Version <> '0.0.0.0');
end;

function IsWebView2Installed(): Boolean;
var
  Version: String;
  ClientKey: String;
begin
  ClientKey := 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  Result :=
    (RegQueryStringValue(HKLM32, ClientKey, 'pv', Version) and IsUsableWebView2Version(Version)) or
    (RegQueryStringValue(HKCU, ClientKey, 'pv', Version) and IsUsableWebView2Version(Version));
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  ClearUserData := False;
  if not UninstallSilent then
    ClearUserData := MsgBox(
      '是否同时删除当前 Windows 用户的激活信息、Harness 会话、WebView2 数据和外观设置？' + #13#10 + #13#10 +
      '选择“否”将保留这些数据，重新安装后可以继续使用。选择“是”后无法恢复。',
      mbConfirmation,
      MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

function ShouldClearUserData(): Boolean;
begin
  Result := ClearUserData;
end;
