#define AppName "CryptoTicker"
#define AppVersion "1.0.0"
#define AppPublisher "CryptoTicker"
#define AppExeName "CryptoTicker.Desktop.exe"

[Setup]
AppId={{17C32E42-747A-46D7-9A31-AD2D3DC39771}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
OutputDir=..\publish\installer
OutputBaseFilename=CryptoTicker-Setup
Compression=lzma
SolidCompression=yes

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "建立桌面捷徑"; Flags: unchecked
Name: "autostart"; Description: "開機時啟動"; Flags: unchecked

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#AppExeName}"""; Tasks: autostart
