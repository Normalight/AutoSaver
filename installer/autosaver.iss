#define MyAppName "AutoSaver"
#define MyAppPublisher "Normalight"
#define MyAppURL "https://github.com/Normalight/AutoSaver"
#define MyAppExeName "autosaver.exe"

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif

[Setup]
AppId={{B5E7F2A1-9C3D-4E8B-9F2A-1D6E8C4B7A90}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; 曾安装过同一 AppId 时沿用上次目录并覆盖文件（配置在 %AppData%，不在安装目录）
UsePreviousAppDir=yes
; 安装/覆盖前尝试关闭仍打开安装目录下文件的进程（与下方 taskkill 互补）
CloseApplications=yes
RestartApplications=no
CloseApplicationsFilter={#MyAppExeName}
OutputDir=..\dist
; Installer output basename (Inno appends .exe). MUST match the GitHub release asset name
; built in UpdateService.GetExpectedInstallerAssetFileName — see Services/UpdateService.cs
; (InstallerAssetFileNameFormat = "AutoSaver-{0}-Setup.exe").
OutputBaseFilename=AutoSaver-{#MyAppVersion}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\app-icon.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\bin\Release\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
; Ship icon from repo (MSBuild may not copy Content items to output on some CI runners).
Source: "..\Resources\app-icon.ico"; DestDir: "{app}"; DestName: "app-icon.ico"; Flags: ignoreversion
Source: "..\bin\Release\VERSION"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app-icon.ico"

[Run]
; postinstall：选项仅出现在最后的「安装完成」界面（符合常见安装向导习惯），默认勾选（不显式 unchecked）
Filename: "{app}\{#MyAppExeName}"; Description: "安装完成后立即启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command ""$ql = Join-Path $env:APPDATA 'Microsoft\Internet Explorer\Quick Launch'; New-Item -ItemType Directory -Force -Path $ql | Out-Null; $w = New-Object -ComObject WScript.Shell; $s = $w.CreateShortcut((Join-Path $ql '{#MyAppName}.lnk')); $s.TargetPath = '{app}\{#MyAppExeName}'; $s.WorkingDirectory = '{app}'; $s.IconLocation = '{app}\app-icon.ico'; $s.Save()"""; Description: "将快捷方式添加到「快速启动」栏"; Flags: postinstall skipifsilent runhidden

[UninstallDelete]
Type: files; Name: "{userappdata}\Microsoft\Internet Explorer\Quick Launch\{#MyAppName}.lnk"

[UninstallRun]
; taskkill 返回码非 0（进程不存在）时仍需卸载继续，故由 cmd 吞掉错误
Filename: "{sys}\cmd.exe"; Parameters: "/c taskkill /F /IM {#MyAppExeName} /T >nul 2>&1 & exit /b 0"; Flags: runhidden

[Code]

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  // 覆盖安装前强制结束进程，避免 exe 被占用导致更新失败
  Exec(ExpandConstant('{sys}\taskkill.exe'), ExpandConstant('/F /IM {#MyAppExeName} /T'), '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;
