; ============================================================
; SegaAI Windows Installer
; ============================================================

#define MyAppName "SegaAI"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "SegaAI"
#define MyAppExeName "SegaAI.exe"

[Setup]

AppId={{A7F7C9C5-8B7A-4E3F-B6A4-SEGAAI10001}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}

DefaultDirName={autopf}\SegaAI

DefaultGroupName=SegaAI

OutputDir=installer
OutputBaseFilename=SegaAI-Setup

Compression=lzma
SolidCompression=yes

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

PrivilegesRequired=admin

SetupIconFile=.\SegaAgent.UI\Assets\SegaAi.ico

UninstallDisplayIcon={app}\{#MyAppExeName}

WizardStyle=modern

[Files]

Source: ".\SegaAgent.UI\bin\Release\net10.0-windows\win-x64\publish\*"; \
    DestDir: "{app}"; \
    Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]

Name: "{autodesktop}\SegaAI"; \
    Filename: "{app}\{#MyAppExeName}"; \
    IconFilename: "{app}\{#MyAppExeName}"

Name: "{group}\SegaAI"; \
    Filename: "{app}\{#MyAppExeName}"; \
    IconFilename: "{app}\{#MyAppExeName}"

Name: "{group}\Uninstall SegaAI"; \
    Filename: "{uninstallexe}"

[Run]

Filename: "{app}\{#MyAppExeName}"; \
    Description: "Launch SegaAI"; \
    Flags: nowait postinstall skipifsilent