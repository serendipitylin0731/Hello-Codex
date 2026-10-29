#ifndef MyAppVersion
  #define MyAppVersion "3.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

#define MyAppName "Hello! Codex"
#define MyAppExeName "HelloCodex.exe"

[Setup]
AppId={{A721F4B8-2059-4C4B-9EF2-1E56D0DE52A8}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=serendipitylin0731
AppPublisherURL=https://github.com/serendipitylin0731/Hello-Codex
AppSupportURL=https://github.com/serendipitylin0731/Hello-Codex/issues
AppUpdatesURL=https://github.com/serendipitylin0731/Hello-Codex/releases
DefaultDirName={localappdata}\Programs\Hello! Codex
DisableDirPage=no
UsePreviousAppDir=yes
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=HelloCodexSetup
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
LicenseFile=..\LICENSE.txt
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern dynamic
CloseApplications=yes
RestartApplications=no
ChangesAssociations=no
ChangesEnvironment=no
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
VersionInfoDescription=为 ChatGPT Plus 订阅用户设计的 Codex 额度启动器

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "随 Windows 登录自动启动（隐藏到系统托盘）"; GroupDescription: "其他选项："; Flags: unchecked
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "其他选项："; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "HelloCodex"; ValueData: """{app}\{#MyAppExeName}"" --background"; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--post-install {code:InstallerProcessId}"; Description: "启动 {#MyAppName}"; WorkingDir: "{app}"; Flags: postinstall nowait runasoriginaluser skipifsilent

[Code]
function GetCurrentProcessId: Integer;
  external 'GetCurrentProcessId@kernel32.dll stdcall';

function InstallerProcessId(Param: String): String;
begin
  Result := IntToStr(GetCurrentProcessId());
end;
