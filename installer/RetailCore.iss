#if (VER < 0x06030000) || (VER >= 0x07000000)
  #error Use Inno Setup 6.3 or newer in the 6.x series (UTF-8 sources and x64compatible architecture).
#endif
#ifndef PublishDir
  #error PublishDir is required. Run scripts/build-installer.ps1.
#endif
#ifndef AppVersion
  #define AppVersion "0.1.8"
#endif
[Setup]
AppId={{2960C36C-7043-4232-A3E3-65E5BA9DD9E0}
AppName=RetailCore POS
AppVersion={#AppVersion}
AppPublisher=RetailCore
DefaultDirName={localappdata}\Programs\RetailCore
DefaultGroupName=RetailCore POS
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
AppMutex=Local\RetailCore.SingleCheckout
CloseApplications=no
RestartApplications=no
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
OutputBaseFilename=RetailCore-{#AppVersion}-win-x64-setup
OutputDir=..\artifacts\installer
UninstallDisplayIcon={app}\RetailCore.Desktop.exe
SetupLogging=yes
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "sinhala"; MessagesFile: "compiler:Default.isl,Sinhala.isl"
[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Register-DailyBackup.ps1"; DestDir: "{app}"; Flags: ignoreversion
[Icons]
Name: "{group}\RetailCore POS"; Filename: "{app}\RetailCore.Desktop.exe"
Name: "{autodesktop}\RetailCore POS"; Filename: "{app}\RetailCore.Desktop.exe"
[Run]
Filename: "{app}\RetailCore.Desktop.exe"; Description: "Open RetailCore POS / RetailCore POS විවෘත කරන්න"; Flags: nowait postinstall skipifsilent
[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Register-DailyBackup.ps1"" -Remove"; Flags: runhidden waituntilterminated
[Code]
function InitializeSetup(): Boolean;
begin
  if WizardSilent then begin Result := True; exit; end;
  Result := MsgBox('Install/update RetailCore POS? Close and reconcile the current shift first. The application verifies a safety backup before any database upgrade. Business data and backups are kept outside the installation and remain on uninstall.' + #13#10 + #13#10 + 'RetailCore POS ස්ථාපනය/යාවත්කාලීන කරන්නද? පළමුව වැඩ මුරය වසා මුදල් සසඳන්න. දත්ත යාවත්කාලීනයට පෙර ආරක්ෂිත උපස්ථයක් සත්‍යාපනය කෙරේ. යෙදුම ඉවත් කළත් දත්ත හා උපස්ථ රැකේ.', mbConfirmation, MB_OKCANCEL) = IDOK;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var Code: Integer;
begin
  Result := '';
  if FileExists(ExpandConstant('{app}\RetailCore.Desktop.exe')) then begin
    if not Exec(ExpandConstant('{app}\RetailCore.Desktop.exe'), '--prepare-upgrade', '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
      Result := 'Close the shift and application, reconnect backup storage, then retry. No program files were replaced. / වැඩ මුරය හා යෙදුම වසා උපස්ථ ගබඩාව නැවත සම්බන්ධ කර උත්සාහ කරන්න. යෙදුම් ගොනු ප්‍රතිස්ථාපනය කර නැත.';
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var Code: Integer;
begin
  if CurStep = ssPostInstall then
    if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\Register-DailyBackup.ps1') + '" -Application "' + ExpandConstant('{app}\RetailCore.Desktop.exe') + '"', '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
      SuppressibleMsgBox('The daily task could not be registered. Backups still run while the app is open. Contact support to repair the scheduled task. / දෛනික කාර්යය සකස් කළ නොහැක. යෙදුම විවෘත විට උපස්ථ ක්‍රියාත්මක වේ. නියමිත කාර්යය නිවැරදි කිරීමට සහාය ලබාගන්න.', mbError, MB_OK, IDOK);
end;
