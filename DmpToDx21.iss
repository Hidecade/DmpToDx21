[Setup]
AppName=DmpToDx21 Converter
AppVersion=1.0.1
DefaultDirName={pf}\DmpToDx21
DefaultGroupName=DmpToDx21
OutputBaseFilename=Setup_DmpToDx21
Compression=lzma
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64

[Files]
Source: "D:\Visual Studio\DmpToDx21\bin\Release\net8.0-windows\DmpToDx21.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "D:\Visual Studio\DmpToDx21\bin\Release\net8.0-windows\DmpToDx21.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "D:\Visual Studio\DmpToDx21\bin\Release\net8.0-windows\DmpToDx21.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "D:\Visual Studio\DmpToDx21\bin\Release\net8.0-windows\DmpToDx21.deps.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "D:\Visual Studio\DmpToDx21\bin\Release\net8.0-windows\NAudio*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "D:\Visual Studio\DmpToDx21\icon\dx21.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\DmpToDx21 Converter"; Filename: "{app}\DmpToDx21.exe"; IconFilename: "{app}\dx21.ico"
Name: "{userdesktop}\DmpToDx21 Converter"; Filename: "{app}\DmpToDx21.exe"; IconFilename: "{app}\dx21.ico"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "デスクトップにアイコンを作成する"; GroupDescription: "追加オプション"

[Run]
Filename: "{app}\DmpToDx21.exe"; Description: "DmpToDx21 Converter を起動"; Flags: nowait postinstall skipifsilent
