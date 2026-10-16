; WinAirPlay Inno Setup installer script
; Build the app first with:  tools\build.ps1
; Then compile this script with Inno Setup Compiler (https://jrsoftware.org/isdl.php)
; The distributable folder is expected at:  ..\dist\WinAirPlay\

#define MyAppName "WinAirPlay"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Aiden6652"
#define MyAppURL "https://github.com/Aiden6652/WinAirPlay"
#define MySourceDir "..\dist\WinAirPlay"

[Setup]
AppId={{A1B2C3D4-0001-4455-8899-AABBCCDDEEFF}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=installer\out
OutputBaseFilename=WinAirPlay-Setup-{#MyAppVersion}
SetupIconFile=
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Files]
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\WinAirPlay.exe"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\WinAirPlay.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务:"; Flags: unchecked

[Run]
Filename: "{app}\WinAirPlay.exe"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall runascurrentuser
