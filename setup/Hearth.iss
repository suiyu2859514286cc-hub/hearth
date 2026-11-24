; Hearth 安装包脚本（Inno Setup）
; 编译：ISCC.exe Hearth.iss

#define AppName "Hearth"
#define AppVersion "0.2.0-beta"
#define AppPublisher "Hearth"
#define AppExe "Hearth.exe"

[Setup]
AppId={{7C4E9A21-8B3D-4F62-9E15-3A7B2D8C5F10}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppComments=Minecraft Java 版启动器（测试版）
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Hearth
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\dist-setup
OutputBaseFilename=Hearth-{#AppVersion}-Setup
SetupIconFile=..\src\hearth.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
; 只留中文：只有一种语言时 Inno 不会弹语言选择框
Name: "chinese"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："; Flags: checkedonce

[Files]
Source: "..\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "立即启动 Hearth"; Flags: nowait postinstall skipifsilent
