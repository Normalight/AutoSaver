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
Source: "..\bin\Release\app-icon.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\Release\VERSION"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\Release\CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app-icon.ico"
