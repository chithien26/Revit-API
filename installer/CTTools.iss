; Inno Setup script - compiled by build.ps1 (https://jrsoftware.org/isinfo.php)
; Installs per-user (%APPDATA%) by default, or for all users (%ProgramData%) when run as admin.

#define MyAppName "CTTools"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef RevitVersion
  #define RevitVersion "2025"
#endif
#ifndef StageDir
  #define StageDir "..\dist\CTTools-" + RevitVersion
#endif

#define AddinsDir "{autoappdata}\Autodesk\Revit\Addins\" + RevitVersion

[Setup]
AppId={{3cb599b2-069b-4203-8ede-b332e3e2573f}
AppName={#MyAppName} for Revit {#RevitVersion}
AppVersion={#MyAppVersion}
AppPublisher=ChiThien
AppPublisherURL=https://github.com/chithien26/Revit-API
DefaultDirName={#AddinsDir}\{#MyAppName}
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename={#MyAppName}-Revit{#RevitVersion}-{#MyAppVersion}-Setup
UninstallDisplayName={#MyAppName} for Revit {#RevitVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Files]
Source: "{#StageDir}\CTTools.addin"; DestDir: "{#AddinsDir}"; Flags: ignoreversion
Source: "{#StageDir}\CTTools\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
