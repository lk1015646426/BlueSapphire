; ==========================================================
;   BlueSapphire 通用安装脚本模板 (publish-only)
; ==========================================================

#ifndef MyAppName
  #define MyAppName "BlueSapphire"
#endif
#ifndef MyAppVersion
  #define MyAppVersion "1.0.3"
#endif
#ifndef MyAppPublisher
  #define MyAppPublisher "BlueSapphire Team"
#endif
#ifndef MyAppId
  #define MyAppId "{{8D43FBFA-A424-4FED-BDE6-6C586D7D13EE}"
#endif
#ifndef MySetupIconFile
  #define MySetupIconFile "BS.ico"
#endif
#ifndef SourcePath
  #error "SourcePath must be provided and must point to a dotnet publish output directory."
#endif

#if SourcePath == ""
  #error "SourcePath must not be empty."
#endif

#if !DirExists(SourcePath)
  #error "SourcePath does not exist."
#endif

#define NormalizedSourcePath LowerCase(AddBackslash(SourcePath))

#if !FileExists(AddBackslash(SourcePath) + MyAppName + ".exe")
  #error "SourcePath must contain the published application executable."
#endif

#if FileExists(AddBackslash(SourcePath) + "BlueSapphire.csproj")
  #error "SourcePath points to the repository root instead of a publish directory."
#endif

#if DirExists(AddBackslash(SourcePath) + "BlueSapphire.Tests")
  #error "SourcePath contains the test project and is not a valid publish directory."
#endif

#if DirExists(AddBackslash(SourcePath) + "TestData")
  #error "SourcePath contains test assets and is not a valid publish directory."
#endif

#if DirExists(AddBackslash(SourcePath) + ".git")
  #error "SourcePath contains repository metadata and is not a valid publish directory."
#endif

#if DirExists(AddBackslash(SourcePath) + "obj")
  #error "SourcePath contains obj and is not a valid publish directory."
#endif

#if Pos("\bin\debug\", NormalizedSourcePath) > 0
  #error "SourcePath must not point to a Debug output directory."
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
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
AppMutex=BlueSapphire-8D43FBFA-A424-4FED-BDE6-6C586D7D13EE
CloseApplications=yes
RestartApplications=no
PrivilegesRequired=admin
SetupLogging=yes
#if MySetupIconFile != ""
SetupIconFile={#MySetupIconFile}
#endif
UninstallDisplayIcon={app}\{#MyAppName}.exe

[Languages]
Name: "chinesesimplified"; MessagesFile: "Chinese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourcePath}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.xml,*.config"

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppName}.exe"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppName}.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppName}.exe"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
