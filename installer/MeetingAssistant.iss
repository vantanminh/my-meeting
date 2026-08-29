#define AppName "Meeting Assistant"
#define AppVersion "1.0.0"
#define AppPublisher "Meeting Assistant"
#define AppExeName "MeetingAssistant.exe"
#define PublishDir "..\\src\\MeetingAssistant\\bin\\Release\\net8.0-windows\\win-x64\\publish"

[Setup]
AppId={{A0DF4380-79BF-4AAE-BA2F-0C342EE81252}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\Meeting Assistant
DefaultGroupName={#AppName}
OutputDir=..\dist
OutputBaseFilename=MeetingAssistant-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\{#AppExeName}
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
