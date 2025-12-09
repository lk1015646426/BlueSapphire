; ==========================================================
;   BlueSapphire 通用安装脚本模板 (本地汉化版)
; ==========================================================

#ifndef MyAppName
  #define MyAppName "UnknownApp"
#endif
#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#ifndef MyAppPublisher
  #define MyAppPublisher "UnknownPublisher"
#endif
#ifndef MyAppId
  #define MyAppId "{{00000000-0000-0000-0000-000000000000}"
#endif
#ifndef SourcePath
  #define SourcePath "."
#endif

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DisableDirPage=no
OutputBaseFilename={#MyAppName}_Setup_v{#MyAppVersion}
Compression=lzma
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

[Languages]
; === 重点修改在这里 ===
; 我们直接引用当前文件夹下的 Chinese.isl
; 这里的 Source: "Chinese.isl" 指的是 Inno Setup 编译器同级，
; 但为了保险，我们通常只写文件名，只要它和 .iss 在一起，编译器一般能找到。
; 如果报错，说明它没去当前目录找，我们下面会用 Source 路径修正。

Name: "chinesesimplified"; MessagesFile: "Chinese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourcePath}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppName}.exe"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppName}.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppName}.exe"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent