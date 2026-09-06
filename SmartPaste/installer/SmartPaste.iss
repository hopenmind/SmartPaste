; Inno Setup script for SmartPaste. Built in CI (see .github/workflows/build.yml):
;   ISCC.exe installer\SmartPaste.iss /DMyAppVersion=x.y.z
; It packages the self-contained publish/ output into a branded Windows installer.

#define MyAppName "SmartPaste"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "Hope 'n Mind"
#define MyAppURL "https://www.hopenmind.com"
#define MyAppExeName "SmartPaste.exe"

[Setup]
AppId={{B7E9C3A2-4F1D-4E8A-9C2B-1A2B3C4D5E6F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=SmartPaste-Setup-x64
SetupIconFile=..\assets\icon.ico
WizardImageFile=wizard-large.bmp,wizard-large-2x.bmp
WizardSmallImageFile=wizard-small.bmp,wizard-small-2x.bmp
DisableWelcomePage=no
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupicon"; Description: "Launch SmartPaste automatically at Windows startup"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "..\..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SmartPaste"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
