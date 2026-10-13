; 轻影播放器安装脚本。由 build.ps1 调用 Inno Setup 编译。

#define AppName "轻影播放器"
#define AppVersion "1.1.2"
#define AppExe "Qingying.exe"

[Setup]
AppId={{A7C3E1B2-5D44-4F0A-9C31-8E6B0F2A91D4}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Lite Shadow
AppPublisherURL=https://github.com/videolan/vlc
DefaultDirName={localappdata}\Programs\Qingying
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=QingyingSetup-{#AppVersion}
SetupIconFile=..\src\Qingying.Player\Assets\qingying.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
VersionInfoVersion={#AppVersion}
VersionInfoDescription=Lite Shadow 轻影播放器安装程序
AppMutex=Qingying.Player
CloseApplications=yes
RestartApplications=no
ChangesAssociations=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加选项:"; Flags: checkedonce
Name: "assoc"; Description: "加入“打开方式”，便于播放常见视频和音频（不更改当前默认程序）"; GroupDescription: "附加选项:"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; IconFilename: "{app}\{#AppExe}"; IconIndex: 0; AppUserModelID: "Qingying.Player"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; IconFilename: "{app}\{#AppExe}"; IconIndex: 0; Tasks: desktopicon; AppUserModelID: "Qingying.Player"

[Run]
Filename: "{app}\{#AppExe}"; Description: "启动轻影播放器"; Flags: nowait postinstall skipifsilent
Filename: "{sys}\ie4uinit.exe"; Parameters: "-show"; Flags: runhidden nowait skipifdoesntexist; StatusMsg: "正在更新快捷方式图标..."

[Registry]
Root: HKCU; Subkey: "Software\Classes\Qingying.media"; ValueType: string; ValueName: ""; ValueData: "轻影影音文件"; Flags: uninsdeletekey; Tasks: assoc
Root: HKCU; Subkey: "Software\Classes\Qingying.media\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"; Tasks: assoc
Root: HKCU; Subkey: "Software\Classes\Qingying.media\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: assoc
Root: HKCU; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "Qingying"; ValueData: "Software\Qingying\Capabilities"; Flags: uninsdeletevalue; Tasks: assoc
Root: HKCU; Subkey: "Software\Qingying\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "轻影播放器"; Tasks: assoc
Root: HKCU; Subkey: "Software\Qingying\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "播放常见本地视频和音频"; Tasks: assoc

Root: HKCU; Subkey: "Software\Qingying\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mp4"; ValueData: "Qingying.media"; Tasks: assoc
Root: HKCU; Subkey: "Software\Classes\.mp4\OpenWithProgids"; ValueType: string; ValueName: "Qingying.media"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assoc
Root: HKCU; Subkey: "Software\Qingying\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mkv"; ValueData: "Qingying.media"; Tasks: assoc
Root: HKCU; Subkey: "Software\Classes\.mkv\OpenWithProgids"; ValueType: string; ValueName: "Qingying.media"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assoc
Root: HKCU; Subkey: "Software\Qingying\Capabilities\FileAssociations"; ValueType: string; ValueName: ".avi"; ValueData: "Qingying.media"; Tasks: assoc
Root: HKCU; Subkey: "Software\Classes\.avi\OpenWithProgids"; ValueType: string; ValueName: "Qingying.media"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assoc
Root: HKCU; Subkey: "Software\Qingying\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mov"; ValueData: "Qingying.media"; Tasks: assoc
Root: HKCU; Subkey: "Software\Classes\.mov\OpenWithProgids"; ValueType: string; ValueName: "Qingying.media"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assoc
Root: HKCU; Subkey: "Software\Qingying\Capabilities\FileAssociations"; ValueType: string; ValueName: ".wmv"; ValueData: "Qingying.media"; Tasks: assoc
Root: HKCU; Subkey: "Software\Classes\.wmv\OpenWithProgids"; ValueType: string; ValueName: "Qingying.media"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assoc
Root: HKCU; Subkey: "Software\Qingying\Capabilities\FileAssociations"; ValueType: string; ValueName: ".flv"; ValueData: "Qingying.media"; Tasks: assoc
Root: HKCU; Subkey: "Software\Classes\.flv\OpenWithProgids"; ValueType: string; ValueName: "Qingying.media"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assoc
Root: HKCU; Subkey: "Software\Qingying\Capabilities\FileAssociations"; ValueType: string; ValueName: ".webm"; ValueData: "Qingying.media"; Tasks: assoc
Root: HKCU; Subkey: "Software\Classes\.webm\OpenWithProgids"; ValueType: string; ValueName: "Qingying.media"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assoc
Root: HKCU; Subkey: "Software\Qingying\Capabilities\FileAssociations"; ValueType: string; ValueName: ".rmvb"; ValueData: "Qingying.media"; Tasks: assoc
Root: HKCU; Subkey: "Software\Classes\.rmvb\OpenWithProgids"; ValueType: string; ValueName: "Qingying.media"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assoc
Root: HKCU; Subkey: "Software\Qingying\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mp3"; ValueData: "Qingying.media"; Tasks: assoc
Root: HKCU; Subkey: "Software\Classes\.mp3\OpenWithProgids"; ValueType: string; ValueName: "Qingying.media"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assoc
Root: HKCU; Subkey: "Software\Qingying\Capabilities\FileAssociations"; ValueType: string; ValueName: ".flac"; ValueData: "Qingying.media"; Tasks: assoc
Root: HKCU; Subkey: "Software\Classes\.flac\OpenWithProgids"; ValueType: string; ValueName: "Qingying.media"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assoc
Root: HKCU; Subkey: "Software\Qingying\Capabilities\FileAssociations"; ValueType: string; ValueName: ".wav"; ValueData: "Qingying.media"; Tasks: assoc
Root: HKCU; Subkey: "Software\Classes\.wav\OpenWithProgids"; ValueType: string; ValueName: "Qingying.media"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assoc
Root: HKCU; Subkey: "Software\Qingying\Capabilities\FileAssociations"; ValueType: string; ValueName: ".m4a"; ValueData: "Qingying.media"; Tasks: assoc
Root: HKCU; Subkey: "Software\Classes\.m4a\OpenWithProgids"; ValueType: string; ValueName: "Qingying.media"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assoc
Root: HKCU; Subkey: "Software\Qingying\Capabilities\FileAssociations"; ValueType: string; ValueName: ".ogg"; ValueData: "Qingying.media"; Tasks: assoc
Root: HKCU; Subkey: "Software\Classes\.ogg\OpenWithProgids"; ValueType: string; ValueName: "Qingying.media"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assoc
Root: HKCU; Subkey: "Software\Qingying\Capabilities\FileAssociations"; ValueType: string; ValueName: ".wma"; ValueData: "Qingying.media"; Tasks: assoc
Root: HKCU; Subkey: "Software\Classes\.wma\OpenWithProgids"; ValueType: string; ValueName: "Qingying.media"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assoc
