#define MyAppName "Xbox Neighborhood"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Team Resurgent"
#define MyAppURL "https://github.com/Team-Resurgent/XboxNeighborhood"
#define MyAppId "F3A8C2E1-9B4D-4F6A-8E2C-1D5B7A9C0E3F"

#ifndef InstallerOutputDir
#define InstallerOutputDir "../out/bin/x64/Release"
#endif
#ifndef InstallerOutputBaseName
#define InstallerOutputBaseName "XboxNeighborhood-Setup"
#endif
#ifndef PayloadDir
#define PayloadDir "../out/bin/x64/Release"
#endif

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
OutputDir={#InstallerOutputDir}
OutputBaseFilename={#InstallerOutputBaseName}
SetupIconFile=Icon.ico
WizardImageFile=WizardImage.bmp
WizardSmallImageFile=WizardSmallImage.bmp
UninstallDisplayIcon={app}\console.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=
UsePreviousPrivileges=no
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

#define ClsidPublicGuid "DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44"
#define ClsidPublicBraced "{{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}}"
#define DotNetDesktopRuntimeExe "windowsdesktop-runtime-win-x64.exe"
#define DotNetDesktopRuntimeMajor "8"
#define DotNetReleaseMetadataUrl "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/8.0/releases.json"
#define DotNetDesktopRuntimeDownloadUrl "https://dotnet.microsoft.com/download/dotnet/8.0"

[InstallDelete]
Type: files; Name: "{app}\xbshlext.dll"
Type: files; Name: "{app}\xheader.bmp"
Type: files; Name: "{app}\xwmark.bmp"

[Files]
Source: "{#PayloadDir}/Rxdk.XbShellExt.Shell.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}/Rxdk.XbShellExt.comhost.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}/Rxdk.XbShellExt.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}/Rxdk.XbShellExt.UI.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}/RXDKNeighborhood.Core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}/Rxdk.Xbdm.KitServices.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}/Rxdk.Xbdm.Managed.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}/Rxdk.Xbdm.Abstractions.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}/Rxdk.XbShellExt.deps.json"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#PayloadDir}/Rxdk.XbShellExt.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#PayloadDir}/console.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}/xbox.ico"; DestDir: "{app}"; Flags: ignoreversion

[Registry]
; CC44 namespace shell extension (native proxy)
Root: HKCR; Subkey: "CLSID\{{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}}"; ValueType: string; ValueName: ""; ValueData: "Xbox Neighborhood"; Flags: uninsdeletekey
Root: HKCR; Subkey: "CLSID\{{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}}"; ValueType: string; ValueName: "ProgID"; ValueData: "Shellext.XboxFolder.1"
Root: HKCR; Subkey: "CLSID\{{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}}"; ValueType: string; ValueName: "VersionIndependentProgID"; ValueData: "Shellext.XboxFolder"
Root: HKCR; Subkey: "CLSID\{{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}}"; ValueType: dword; ValueName: "System.IsPinnedToNameSpaceTree"; ValueData: "1"
Root: HKCR; Subkey: "CLSID\{{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}}"; ValueType: dword; ValueName: "SortOrderIndex"; ValueData: "$50"
Root: HKCR; Subkey: "CLSID\{{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}}\InprocServer32"; ValueType: string; ValueName: ""; ValueData: "{app}\Rxdk.XbShellExt.Shell.dll"
Root: HKCR; Subkey: "CLSID\{{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}}\InprocServer32"; ValueType: string; ValueName: "ThreadingModel"; ValueData: "Apartment"
Root: HKCR; Subkey: "CLSID\{{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}}\ShellFolder"; ValueType: dword; ValueName: "Attributes"; ValueData: "$a0000004"
Root: HKCR; Subkey: "CLSID\{{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\xbox.ico"

Root: HKCR; Subkey: "Shellext.XboxFolder.1"; ValueType: string; ValueName: ""; ValueData: "Xbox Neighborhood"; Flags: uninsdeletekey
Root: HKCR; Subkey: "Shellext.XboxFolder.1"; ValueType: string; ValueName: "CLSID"; ValueData: "{#ClsidPublicBraced}"

Root: HKCR; Subkey: "Shellext.XboxFolder"; ValueType: string; ValueName: ""; ValueData: "Xbox Neighborhood"; Flags: uninsdeletekey
Root: HKCR; Subkey: "Shellext.XboxFolder"; ValueType: string; ValueName: "CLSID"; ValueData: "{#ClsidPublicBraced}"
Root: HKCR; Subkey: "Shellext.XboxFolder"; ValueType: string; ValueName: "CurVer"; ValueData: "Shellext.XboxFolder.1"

Root: HKCR; Subkey: "xbox"; ValueType: string; ValueName: ""; ValueData: "URL:Xbox Namespace Extension"; Flags: uninsdeletekey
Root: HKCR; Subkey: "xbox"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCR; Subkey: "xbox\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{sys}\rundll32.exe"" ""{app}\Rxdk.XbShellExt.Shell.dll"",LaunchExplorer %1"

Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\{{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}}"; ValueType: string; ValueName: ""; ValueData: "Xbox Neighborhood"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Explorer\MyComputer\NameSpace\{{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}}"; ValueType: string; ValueName: ""; ValueData: "Xbox Neighborhood"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved"; ValueType: string; ValueName: "{#ClsidPublicGuid}"; ValueData: "Xbox Namespace Shell Extension"; Flags: uninsdeletevalue

; CC45 managed coclass (also registered by regsvr32 on comhost.dll)
Root: HKCR; Subkey: "CLSID\{{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC45}}"; ValueType: string; ValueName: ""; ValueData: "Xbox Neighborhood (Managed)"; Flags: uninsdeletekey
Root: HKCR; Subkey: "CLSID\{{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC45}}\InprocServer32"; ValueType: string; ValueName: ""; ValueData: "{app}\Rxdk.XbShellExt.comhost.dll"
Root: HKCR; Subkey: "CLSID\{{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC45}}\InprocServer32"; ValueType: string; ValueName: "ThreadingModel"; ValueData: "Apartment"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{sys}\rundll32.exe"; Parameters: """{app}\Rxdk.XbShellExt.Shell.dll"",OpenNamespace"; WorkingDir: "{app}"; IconFilename: "{app}\xbox.ico"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{sys}\rundll32.exe"; Parameters: """{app}\Rxdk.XbShellExt.Shell.dll"",OpenNamespace"; WorkingDir: "{app}"; IconFilename: "{app}\xbox.ico"

[Code]
var
  NeedExplorerRestart: Boolean;
  DotNetDownloadPage: TDownloadWizardPage;
const
  UninstallRegRoot = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\';
  UninstallRegKey = '{#MyAppId}_is1';
  ShellExtClsidKey = 'SOFTWARE\Classes\CLSID\{{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}}\InprocServer32';
  ClsidPublicRegKey = 'CLSID\{{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}}';
  ClsidPublicInprocKey = 'CLSID\{{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}}\InprocServer32';
  ClsidPublicShellFolderKey = 'CLSID\{{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}}\ShellFolder';
  ClsidPublicDefaultIconKey = 'CLSID\{{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}}\DefaultIcon';
  ClsidPublicShellKey = 'CLSID\{{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}}\shell';
  ManagedInprocKey = 'CLSID\{{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC45}}\InprocServer32';
  DesktopNamespaceKey = 'Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\{{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}}';
  MyComputerNamespaceKey = 'Software\Microsoft\Windows\CurrentVersion\Explorer\MyComputer\NameSpace\{{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}}';
  DotNetDesktopRuntimeRegKey = 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';
  DotNetDesktopRuntimeFileName = '{#DotNetDesktopRuntimeExe}';
  UserClassesClsidKey = 'Software\Classes\CLSID\{{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}}';
  UserDesktopNamespaceKey = 'Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\{{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}}';
  HideDesktopIconsKey = 'Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel';
  ExplorerAdvancedKey = 'Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced';
  ManagedClsidKey = 'CLSID\{{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC45}}';
  XboxProtocolCommandKey = 'xbox\shell\open\command';
  MyAppDisplayName = '{#MyAppName}';
  PublicClsidBracedName = '{{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}}';

function DownloadFileToTmp(const Url, BaseFileName: String): Boolean;
begin
  DotNetDownloadPage.Clear;
  DotNetDownloadPage.Add(Url, BaseFileName, '');
  DotNetDownloadPage.Show;
  try
    try
      DotNetDownloadPage.Download;
      Result := True;
    except
      Log(Format('Download failed for %s: %s', [Url, GetExceptionMessage]));
      Result := False;
    end;
  finally
    DotNetDownloadPage.Hide;
  end;
end;

function ExtractQuotedJsonValue(const Content: AnsiString; StartPos: Integer): String;
var
  I: Integer;
begin
  Result := '';
  I := StartPos;
  while (I <= Length(Content)) and (Content[I] <> '"') do
    Inc(I);
  if I > Length(Content) then
    Exit;
  Inc(I);
  while (I <= Length(Content)) and (Content[I] <> '"') do
  begin
    Result := Result + Content[I];
    Inc(I);
  end;
end;

function ResolveDotNetDesktopRuntimeUrl: String;
var
  JsonPath: String;
  Content, Segment: AnsiString;
  NamePos, UrlKeyPos, UrlStart: Integer;
begin
  JsonPath := ExpandConstant('{tmp}\dotnet-release-metadata.json');
  if not DownloadFileToTmp('{#DotNetReleaseMetadataUrl}', 'dotnet-release-metadata.json') then
    RaiseException('Failed to download .NET release metadata.');

  if not LoadStringFromFile(JsonPath, Content) then
    RaiseException('Failed to read .NET release metadata.');

  NamePos := Pos(DotNetDesktopRuntimeFileName, Content);
  if NamePos = 0 then
    RaiseException('Could not find the .NET Desktop Runtime installer in release metadata.');

  Segment := Copy(Content, NamePos, 512);
  UrlKeyPos := Pos('"url"', Segment);
  if UrlKeyPos = 0 then
    RaiseException('Could not resolve the .NET Desktop Runtime download URL from release metadata.');

  UrlStart := NamePos + UrlKeyPos + Length('"url"') - 1;
  while (UrlStart <= Length(Content)) and
        ((Content[UrlStart] = ':') or (Content[UrlStart] = ' ') or (Content[UrlStart] = #9)) do
    Inc(UrlStart);

  Result := ExtractQuotedJsonValue(Content, UrlStart);
  if Result = '' then
    RaiseException('Could not parse the .NET Desktop Runtime download URL from release metadata.');

  Log('Resolved .NET Desktop Runtime URL: ' + Result);
end;

function VerifyNamespaceProxyRegistration: Boolean;
var
  Path: String;
begin
  Result :=
    RegQueryStringValue(HKCR, ClsidPublicInprocKey, '', Path) and
    (Path <> '') and
    FileExists(Path);
  if not Result then
    Log('CC44 namespace proxy is missing or points to a non-existent file: ' + Path);
end;

function DotNetDesktopSharedDir: String;
begin
  Result := ExpandConstant('{pf64}\dotnet\shared\Microsoft.WindowsDesktop.App');
end;

function IsDotNetDesktopRuntimeInstalled(const MajorVersion: String): Boolean;
var
  FindRec: TFindRec;
  SharedDir: String;
  Prefix: String;
  Names: TArrayOfString;
  I: Integer;
begin
  Result := False;
  Prefix := MajorVersion + '.';
  SharedDir := DotNetDesktopSharedDir;

  if DirExists(SharedDir) and FindFirst(SharedDir + '\*', FindRec) then
  try
    repeat
      if ((FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0) and
         (FindRec.Name <> '.') and (FindRec.Name <> '..') and
         (Copy(FindRec.Name, 1, Length(Prefix)) = Prefix) then
      begin
        Result := True;
        Exit;
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;

  if Result then
    Exit;

  if RegGetSubkeyNames(HKLM, DotNetDesktopRuntimeRegKey, Names) then
  begin
    for I := 0 to GetArrayLength(Names) - 1 do
    begin
      if Copy(Names[I], 1, Length(Prefix)) = Prefix then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;
end;

function WaitForDotNetDesktopRuntime(const MajorVersion: String): Boolean;
var
  Attempt: Integer;
begin
  Result := False;
  for Attempt := 0 to 20 do
  begin
    if IsDotNetDesktopRuntimeInstalled(MajorVersion) then
    begin
      Result := True;
      Exit;
    end;
    Sleep(500);
  end;
end;

procedure EnsureDotNetDesktopRuntime;
var
  ResultCode: Integer;
  InstallerPath, DownloadUrl: String;
begin
  if IsDotNetDesktopRuntimeInstalled('{#DotNetDesktopRuntimeMajor}') then
  begin
    Log('Microsoft .NET {#DotNetDesktopRuntimeMajor} Desktop Runtime (x64) is already installed.');
    Exit;
  end;

  Log('Microsoft .NET {#DotNetDesktopRuntimeMajor} Desktop Runtime (x64) not found; downloading prerequisite.');
  InstallerPath := ExpandConstant('{tmp}\{#DotNetDesktopRuntimeExe}');

  WizardForm.StatusLabel.Caption := 'Downloading Microsoft .NET {#DotNetDesktopRuntimeMajor} Desktop Runtime...';
  WizardForm.ProgressGauge.Style := npbstMarquee;
  try
    DownloadUrl := ResolveDotNetDesktopRuntimeUrl;
    if not DownloadFileToTmp(DownloadUrl, '{#DotNetDesktopRuntimeExe}') then
      RaiseException(
        'Failed to download the .NET Desktop Runtime.' + #13#10 + #13#10 +
        'Check your internet connection and try again, or install it manually from {#DotNetDesktopRuntimeDownloadUrl}.');

    if not FileExists(InstallerPath) then
      RaiseException('The .NET Desktop Runtime download did not produce an installer file.');

    WizardForm.StatusLabel.Caption := 'Installing Microsoft .NET {#DotNetDesktopRuntimeMajor} Desktop Runtime...';

    if not Exec(InstallerPath, '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      RaiseException('Failed to launch the .NET Desktop Runtime installer.');

    if (ResultCode <> 0) and (ResultCode <> 3010) and (ResultCode <> 1641) then
      RaiseException(Format('The .NET Desktop Runtime installer failed (exit %d).', [ResultCode]));

    if not WaitForDotNetDesktopRuntime('{#DotNetDesktopRuntimeMajor}') then
      RaiseException(
        'The .NET Desktop Runtime installer completed, but version {#DotNetDesktopRuntimeMajor}.x was not found under ' +
        DotNetDesktopSharedDir + '.');
  finally
    WizardForm.ProgressGauge.Style := npbstNormal;
  end;
end;

function UninstallRegPath(const SubKey: String): String;
begin
  Result := UninstallRegRoot + SubKey;
end;

function QueryInstallPath(const SubKey: String; var Path: String): Boolean;
begin
  Result :=
    RegQueryStringValue(HKLM, UninstallRegPath(SubKey), 'InstallLocation', Path) or
    RegQueryStringValue(HKLM, UninstallRegPath(SubKey), 'Inno Setup: App Path', Path);
  if Result then
    Path := RemoveBackslashUnlessRoot(Path);
end;

function ExistingInstallPath(): String;
var
  Path: String;
  LegacySubKeys: array[0..1] of String;
  I: Integer;
begin
  if QueryInstallPath(UninstallRegKey, Path) then
  begin
    Result := Path;
    Exit;
  end;

  LegacySubKeys[0] := '{{F3A8C2E1-9B4D-4F6A-8E2C-1D5B7A9C0E3F}}_is1';
  LegacySubKeys[1] := '{{F3A8C2E1-9B4D-4F6A-8E2C-1D5B7A9C0E3F}}}_is1';
  for I := 0 to 1 do
  begin
    if QueryInstallPath(LegacySubKeys[I], Path) then
    begin
      Result := Path;
      Exit;
    end;
  end;

  Result := '';
end;

procedure RequireAdminInstallMode;
begin
  if not IsAdminInstallMode then
    RaiseException('Administrator privileges are required.');
end;

procedure StopExplorer;
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/F /IM explorer.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(2000);
  NeedExplorerRestart := True;
end;

procedure StartExplorer;
var
  ResultCode: Integer;
begin
  Sleep(1000);
  if Exec(ExpandConstant('{cmd}'), '/c start "" explorer.exe', ExpandConstant('{win}'), SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    NeedExplorerRestart := False
  else
    Log('Failed to restart Explorer.');
end;

procedure EnsureExplorerRunning;
begin
  if NeedExplorerRestart then
    StartExplorer;
end;

procedure RestartExplorer;
begin
  StopExplorer;
  StartExplorer;
end;

function RunRegsvr32(const DllPath: String; Unregister: Boolean): Integer;
var
  ResultCode: Integer;
  Params: String;
begin
  Params := '/s';
  if Unregister then
    Params := Params + ' /u';
  Params := Params + ' "' + DllPath + '"';

  if Exec(ExpandConstant('{sys}\regsvr32.exe'), Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := ResultCode
  else
    Result := 1;
end;

function Regsvr32ErrorMessage(const Action: String; ExitCode: Integer): String;
begin
  if IsAdminInstallMode then
    Result := Format('%s failed (exit %d).', [Action, ExitCode])
  else
    Result := Format('%s failed (exit %d). Setup is not running with administrator privileges.', [Action, ExitCode]);
end;

procedure CleanupErroneousShellOpenKeys;
begin
  if RegKeyExists(HKCR, ClsidPublicShellKey) then
    RegDeleteKeyIncludingSubkeys(HKCR, ClsidPublicShellKey);
  if RegKeyExists(HKCR, 'Shellext.XboxFolder.1\shell') then
    RegDeleteKeyIncludingSubkeys(HKCR, 'Shellext.XboxFolder.1\shell');
end;

procedure RepairNavPaneUserState;
var
  Attrs: Cardinal;
  ExplorerClsidKey: String;
begin
  ExplorerClsidKey := 'Software\Microsoft\Windows\CurrentVersion\Explorer\CLSID\{{DB15FEDD-96B8-4DA9-97E0-7E5CCA05CC44}}';
  if RegQueryDWordValue(HKCU, ExplorerClsidKey + '\ShellFolder', 'Attributes', Attrs) then
  begin
    if (Attrs and $100000) <> 0 then
      RegDeleteKeyIncludingSubkeys(HKCU, ExplorerClsidKey + '\ShellFolder');
  end
  else if RegKeyExists(HKCU, ExplorerClsidKey + '\ShellFolder') then
    RegDeleteKeyIncludingSubkeys(HKCU, ExplorerClsidKey + '\ShellFolder');
end;

procedure RegisterPerUserExplorerKeys(const ShellDllPath: String);
begin
  RegWriteStringValue(HKCU, UserClassesClsidKey, '', 'Xbox Neighborhood');
  RegWriteDWordValue(HKCU, UserClassesClsidKey, 'System.IsPinnedToNameSpaceTree', 1);
  RegWriteDWordValue(HKCU, UserClassesClsidKey, 'SortOrderIndex', $50);
  RegWriteStringValue(HKCU, UserClassesClsidKey + '\InprocServer32', '', ShellDllPath);
  RegWriteStringValue(HKCU, UserClassesClsidKey + '\InprocServer32', 'ThreadingModel', 'Apartment');
  RegWriteStringValue(HKCU, UserDesktopNamespaceKey, '', 'Xbox Neighborhood');
  RegWriteDWordValue(HKCU, HideDesktopIconsKey, PublicClsidBracedName, 1);
  RegWriteDWordValue(HKCU, ExplorerAdvancedKey, 'NavPaneShowAllFolders', 1);
end;

procedure UnregisterPerUserExplorerKeys;
begin
  RegDeleteKeyIncludingSubkeys(HKCU, UserClassesClsidKey);
  RegDeleteKeyIncludingSubkeys(HKCU, UserDesktopNamespaceKey);
  RegDeleteValue(HKCU, HideDesktopIconsKey, PublicClsidBracedName);
end;

procedure RepairManagedShellExtensionRegistry(const ComHostPath: String);
begin
  RegWriteStringValue(HKCR, ManagedClsidKey, '', 'Xbox Neighborhood (Managed)');
  RegWriteStringValue(HKCR, ManagedInprocKey, '', ComHostPath);
  RegWriteStringValue(HKCR, ManagedInprocKey, 'ThreadingModel', 'Apartment');
end;

procedure RepairShellExtensionRegistry(const InstallDir, ShellDllPath: String);
var
  IconPath, XboxCommand, ClsidValue: String;
begin
  ClsidValue := PublicClsidBracedName;

  RegWriteStringValue(HKCR, ClsidPublicRegKey, '', 'Xbox Neighborhood');
  RegWriteStringValue(HKCR, ClsidPublicRegKey, 'ProgID', 'Shellext.XboxFolder.1');
  RegWriteStringValue(HKCR, ClsidPublicRegKey, 'VersionIndependentProgID', 'Shellext.XboxFolder');
  RegWriteDWordValue(HKCR, ClsidPublicRegKey, 'System.IsPinnedToNameSpaceTree', 1);
  RegWriteDWordValue(HKCR, ClsidPublicRegKey, 'SortOrderIndex', $50);
  RegWriteStringValue(HKCR, ClsidPublicInprocKey, '', ShellDllPath);
  RegWriteStringValue(HKCR, ClsidPublicInprocKey, 'ThreadingModel', 'Apartment');
  RegWriteDWordValue(HKCR, ClsidPublicShellFolderKey, 'Attributes', $A0000004);

  IconPath := InstallDir + '\xbox.ico';
  if FileExists(IconPath) then
    RegWriteStringValue(HKCR, ClsidPublicDefaultIconKey, '', IconPath)
  else
    RegWriteStringValue(HKCR, ClsidPublicDefaultIconKey, '', ShellDllPath + ',13');

  RegWriteStringValue(HKCR, 'Shellext.XboxFolder.1', '', 'Xbox Neighborhood');
  RegWriteStringValue(HKCR, 'Shellext.XboxFolder.1', 'CLSID', ClsidValue);
  RegWriteStringValue(HKCR, 'Shellext.XboxFolder', '', 'Xbox Neighborhood');
  RegWriteStringValue(HKCR, 'Shellext.XboxFolder', 'CLSID', ClsidValue);
  RegWriteStringValue(HKCR, 'Shellext.XboxFolder', 'CurVer', 'Shellext.XboxFolder.1');

  CleanupErroneousShellOpenKeys;

  RegWriteStringValue(HKLM, DesktopNamespaceKey, '', 'Xbox Neighborhood');
  RegWriteStringValue(HKLM, MyComputerNamespaceKey, '', 'Xbox Neighborhood');
  RegWriteStringValue(HKLM, 'Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved', '{#ClsidPublicGuid}', 'Xbox Namespace Shell Extension');

  RegWriteStringValue(HKCR, 'xbox', '', 'URL:Xbox Namespace Extension');
  RegWriteStringValue(HKCR, 'xbox', 'URL Protocol', '');
  XboxCommand := ExpandConstant('{sys}') + '\rundll32.exe' + ' "' + ShellDllPath + '",LaunchExplorer %1';
  RegWriteStringValue(HKCR, XboxProtocolCommandKey, '', XboxCommand);
end;

function ResolveComHostPath(const InstallDir: String): String;
var
  Dir: String;
begin
  Dir := RemoveBackslashUnlessRoot(InstallDir);
  Result := Dir + '\Rxdk.XbShellExt.comhost.dll';
  if FileExists(Result) then
    Exit;
  Result := Dir + '\xbshlext.dll';
  if FileExists(Result) then
    Exit;
  Result := '';
end;

procedure RegisterShellExtension(const InstallDir: String);
var
  ComHost, ShellDll: String;
  ResultCode: Integer;
begin
  ComHost := ResolveComHostPath(InstallDir);
  ShellDll := InstallDir + '\Rxdk.XbShellExt.Shell.dll';
  if ComHost = '' then
    RaiseException('Missing Rxdk.XbShellExt.comhost.dll in the install directory.');
  if not FileExists(ShellDll) then
    RaiseException('Missing Rxdk.XbShellExt.Shell.dll in the install directory.');

  StopExplorer;
  try
    { CC44 is pure registry (Shell.dll DllRegisterServer is a no-op). Write it
      before regsvr32 so a comhost failure cannot leave the namespace missing. }
    RepairShellExtensionRegistry(InstallDir, ShellDll);
    RepairNavPaneUserState;
    RegisterPerUserExplorerKeys(ShellDll);

    if not VerifyNamespaceProxyRegistration then
      RaiseException(
        'The namespace shell extension proxy (CC44) was not registered. ' +
        'Rxdk.XbShellExt.Shell.dll must be registered under HKCR\\CLSID\\' + PublicClsidBracedName + '\\InprocServer32.');

    ResultCode := RunRegsvr32(ComHost, False);
    if ResultCode <> 0 then
      RaiseException(Regsvr32ErrorMessage('regsvr32', ResultCode));

    RepairManagedShellExtensionRegistry(ComHost);
  finally
    StartExplorer;
  end;
end;

procedure UnregisterShellExtensionFromDir(const InstallDir: String);
var
  ComHost: String;
  ResultCode: Integer;
begin
  ComHost := ResolveComHostPath(InstallDir);
  if ComHost = '' then
    Exit;

  StopExplorer;
  try
    UnregisterPerUserExplorerKeys;
    ResultCode := RunRegsvr32(ComHost, True);
    if ResultCode <> 0 then
      Log(Regsvr32ErrorMessage('regsvr32 /u', ResultCode));
  finally
    StartExplorer;
  end;
end;

procedure UnregisterShellExtension(const DllPath: String);
var
  Dir: String;
begin
  if (DllPath = '') or (not FileExists(DllPath)) then
    Exit;
  Dir := ExtractFilePath(DllPath);
  if Dir <> '' then
    UnregisterShellExtensionFromDir(Copy(Dir, 1, Length(Dir) - 1));
end;

procedure DeleteMatchingFiles(const Directory, Pattern: String);
var
  FindRec: TFindRec;
  Path: String;
begin
  if not FindFirst(Directory + '\' + Pattern, FindRec) then
    Exit;
  try
    repeat
      if ((FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) = 0) then
      begin
        Path := Directory + '\' + FindRec.Name;
        DeleteFile(Path);
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

procedure RemoveXboxNeighborhoodShortcuts;
var
  Desktop, GroupDir: String;
begin
  Desktop := ExpandConstant('{commondesktop}');
  if DirExists(Desktop) then
  begin
    DeleteMatchingFiles(Desktop, MyAppDisplayName + '*.lnk');
    DeleteMatchingFiles(Desktop, MyAppDisplayName + '*.url');
  end;
  Desktop := ExpandConstant('{autodesktop}');
  if DirExists(Desktop) then
  begin
    DeleteMatchingFiles(Desktop, MyAppDisplayName + '*.lnk');
    DeleteMatchingFiles(Desktop, MyAppDisplayName + '*.url');
  end;
  GroupDir := ExpandConstant('{group}');
  if DirExists(GroupDir) then
  begin
    DeleteFile(GroupDir + '\' + MyAppDisplayName + '.url');
    DeleteFile(GroupDir + '\' + MyAppDisplayName + '.lnk');
  end;
end;

function GetExistingUninstaller(var UninstallExe: String): Boolean;
var
  SubKeys: array[0..2] of String;
  UninstallString: String;
  I: Integer;
begin
  SubKeys[0] := UninstallRegKey;
  SubKeys[1] := '{{F3A8C2E1-9B4D-4F6A-8E2C-1D5B7A9C0E3F}}_is1';
  SubKeys[2] := '{{F3A8C2E1-9B4D-4F6A-8E2C-1D5B7A9C0E3F}}}_is1';

  for I := 0 to 2 do
  begin
    if RegQueryStringValue(HKLM, UninstallRegPath(SubKeys[I]), 'UninstallString', UninstallString) then
    begin
      UninstallExe := RemoveQuotes(UninstallString);
      if FileExists(UninstallExe) then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;

  Result := False;
end;

function GetRegisteredDllPath: String;
begin
  Result := '';
  if RegQueryStringValue(HKLM, ShellExtClsidKey, '', Result) then
  begin
    if not FileExists(Result) then
      Result := '';
  end;
end;

function GetExistingDllPath: String;
var
  InstallPath: String;
begin
  Result := GetRegisteredDllPath();
  if Result <> '' then
    Exit;

  InstallPath := ExistingInstallPath();
  if InstallPath = '' then
    Exit;

  Result := InstallPath + '\Rxdk.XbShellExt.Shell.dll';
  if not FileExists(Result) then
    Result := InstallPath + '\Rxdk.XbShellExt.comhost.dll';
  if not FileExists(Result) then
    Result := InstallPath + '\xbshlext.dll';
  if not FileExists(Result) then
    Result := '';
end;

function IsExistingInstall: Boolean;
var
  UninstallExe: String;
begin
  Result :=
    GetExistingUninstaller(UninstallExe) or
    (GetRegisteredDllPath() <> '') or
    (ExistingInstallPath() <> '');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExistingPath: String;
  ExistingDll: String;
begin
  Result := '';
  NeedsRestart := False;

  RequireAdminInstallMode;

  if not IsExistingInstall then
    Exit;

  ExistingPath := ExistingInstallPath();
  if ExistingPath <> '' then
  begin
    UnregisterShellExtensionFromDir(ExistingPath);
    Exit;
  end;

  ExistingDll := GetExistingDllPath();
  if ExistingDll <> '' then
    UnregisterShellExtension(ExistingDll);

  EnsureExplorerRunning;
end;

function RestartElevated: Boolean;
var
  ResultCode: Integer;
begin
  Result := ShellExec('runas', ExpandConstant('{srcexe}'), '', '', SW_SHOW, ewNoWait, ResultCode);
end;

function InitializeUninstall(): Boolean;
begin
  if not IsAdminInstallMode then
  begin
    if RestartElevated then
    begin
      Result := False;
      Exit;
    end;
    MsgBox('Administrator privileges are required to uninstall {#MyAppName}.', mbCriticalError, MB_OK);
    Result := False;
    Exit;
  end;

  Result := True;
end;

function InitializeSetup(): Boolean;
begin
  if not IsWin64 then
  begin
    MsgBox('This installer requires 64-bit Windows.', mbCriticalError, MB_OK);
    Result := False;
    Exit;
  end;

  if not IsAdminInstallMode then
  begin
    if RestartElevated then
    begin
      Result := False;
      Exit;
    end;
    MsgBox('Administrator privileges are required to install {#MyAppName}.', mbCriticalError, MB_OK);
    Result := False;
    Exit;
  end;

  Result := True;
end;

procedure InitializeWizard;
var
  Path: String;
begin
  DotNetDownloadPage := CreateDownloadPage(
    SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
  DotNetDownloadPage.ShowBaseNameInsteadOfUrl := True;

  if IsExistingInstall then
    WizardForm.WelcomeLabel2.Caption :=
      'Setup will remove the previous version, then install ' + '{#MyAppName}' + '.';

  Path := ExistingInstallPath();
  if Path <> '' then
    WizardForm.DirEdit.Text := Path;
end;

procedure AlignWizardButtons;
var
  Margin, Gap: Integer;
begin
  Margin := ScaleX(15);
  Gap := ScaleX(8);

  WizardForm.NextButton.Top := WizardForm.CancelButton.Top;
  WizardForm.NextButton.Left :=
    WizardForm.ClientWidth - WizardForm.NextButton.Width - Margin;

  if WizardForm.CancelButton.Visible then
  begin
    WizardForm.CancelButton.Top := WizardForm.NextButton.Top;
    WizardForm.CancelButton.Left :=
      WizardForm.NextButton.Left - Gap - WizardForm.CancelButton.Width;
  end;

  if WizardForm.BackButton.Visible then
  begin
    WizardForm.BackButton.Top := WizardForm.NextButton.Top;
    WizardForm.BackButton.Left :=
      WizardForm.NextButton.Left - Gap - WizardForm.BackButton.Width;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  AlignWizardButtons;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = wpSelectDir) and IsExistingInstall;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    RequireAdminInstallMode;
    EnsureDotNetDesktopRuntime;
  end;

  if CurStep <> ssPostInstall then
    Exit;

  RequireAdminInstallMode;
  RegisterShellExtension(ExpandConstant('{app}'));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep <> usUninstall then
    Exit;

  RequireAdminInstallMode;
  UnregisterShellExtensionFromDir(ExpandConstant('{app}'));
  RemoveXboxNeighborhoodShortcuts;
end;
