; ==========================================================
;   BlueSapphire 通用安装脚本模板 (v0.6.0 Pro)
; ==========================================================

#ifndef MyAppName
  #define MyAppName "BlueSapphire"
#endif
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef MyAppPublisher
  #define MyAppPublisher "BlueSapphire Team"
#endif
#ifndef MyAppId
  #define MyAppId "{{8D43FBFA-A424-4FED-BDE6-6C586D7D13EE}"
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
Compression=lzma2
SolidCompression=yes

; 🔥【修正1】使用新版架构标识符，消除 x64 警告
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; 现代风格向导界面
WizardStyle=modern

[Languages]
; ✅ 修改为：只写文件名，代表强制使用当前目录下的文件
Name: "chinesesimplified"; MessagesFile: "Chinese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; 忽略各种临时文件和垃圾文件
Source: "{#SourcePath}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.xml,*.config"

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppName}.exe"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppName}.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppName}.exe"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent