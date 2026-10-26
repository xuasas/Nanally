; Build with Inno Setup 7. RuntimeDir must be a clean, self-contained win-x64 publish.
; Example: ISCC.exe /DRuntimeDir="D:\path\dist\runtime\Nanally" /DOutputDir="D:\path\dist\installers" Nanally.iss
; User data is deliberately outside {app}; there is no UninstallDelete section.

#ifndef SourceDir
  #define SourceDir AddBackslash(SourcePath) + ".."
#endif
#ifndef RuntimeDir
  #define RuntimeDir AddBackslash(SourceDir) + "dist\runtime\Nanally"
#endif
#ifndef OutputDir
  #define OutputDir AddBackslash(SourceDir) + "dist\installers"
#endif
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef InstallAppId
  #define InstallAppId "{{E3C5B304-3C21-4D91-82F7-94206465C828}"
#endif
#define AppName "娜娜莉桌宠"
#define AppExe "Nanally.DesktopPet.exe"

#if !FileExists(AddBackslash(RuntimeDir) + AppExe)
  #error RuntimeDir does not contain Nanally.DesktopPet.exe.
#endif
#if !FileExists(AddBackslash(RuntimeDir) + "Nanally.ico")
  #error RuntimeDir does not contain Nanally.ico.
#endif
#if !FileExists(AddBackslash(RuntimeDir) + "coreclr.dll")
  #error RuntimeDir must contain the self-contained .NET runtime (coreclr.dll).
#endif

[Setup]
AppId={#InstallAppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Nanally Desktop Pet
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName}安装程序
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\NanallyDesktopPet
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupArchitecture=x64
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename=Nanally-DesktopPet-{#AppVersion}-win-x64-Setup
SetupIconFile={#RuntimeDir}\Nanally.ico
UninstallDisplayIcon={app}\Nanally.ico
UninstallDisplayName={#AppName} {#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
LZMANumBlockThreads=2
WizardStyle=modern
CloseApplications=no
RestartApplications=no
AlwaysRestart=no
RestartIfNeededByRun=no
SetupLogging=yes

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："; Flags: unchecked

[Files]
; Keep runtime .json/.config and dialogue/*.md. The release pipeline validates the payload manifest.
Source: "{#RuntimeDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.bak,*.tmp,*.log,Backup\*,Backups\*,testing\*,tests\*,scripts\*,build-tools\*,.git\*,.aoci\*"

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; IconFilename: "{app}\Nanally.ico"
Name: "{group}\卸载{#AppName}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; IconFilename: "{app}\Nanally.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Description: "启动{#AppName}"; Flags: postinstall nowait unchecked skipifsilent

[Code]
function CheckPetClosed(var Reason: String): Boolean;
var
  Locator, Services, Processes: Variant;
begin
  Result := False;
  Reason := '';
  try
    { A read-only name query also detects an app running from a different install directory. }
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Services := Locator.ConnectServer('', 'root\CIMV2');
    Processes := Services.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name = ''{#AppExe}''');
    if Processes.Count > 0 then
      Reason := '娜娜莉桌宠仍在运行。请从桌宠菜单或托盘正常退出，保存完成后再重试安装或卸载。'
    else
      Result := True;
  except
    { Fail closed: never overwrite or delete app binaries if process detection is unavailable. }
    Reason := '无法检查娜娜莉桌宠是否正在运行，本次操作已停止。请确认 Windows 管理服务正常后重试。';
    Log('Nanally process check failed: ' + GetExceptionMessage);
  end;
end;

function IsUserDataPath(const Path: String): Boolean;
var
  Target, UserData: String;
begin
  Target := RemoveBackslashUnlessRoot(ExpandFileName(Path));
  UserData := RemoveBackslashUnlessRoot(ExpandConstant('{localappdata}\NanallyDesktopPet'));
  Result := (CompareText(Target, UserData) = 0) or
    (CompareText(Copy(Target, 1, Length(UserData) + 1), UserData + '\') = 0);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  NeedsRestart := False;
  Result := '';
  if IsUserDataPath(ExpandConstant('{app}')) then
  begin
    Result := '安装目录不能使用娜娜莉的用户数据目录或其子目录，请另选程序安装目录。';
    Exit;
  end;
  if not CheckPetClosed(Result) then
    Log('Installation blocked before application files were changed.');
end;

function InitializeUninstall: Boolean;
var
  Reason: String;
begin
  if IsUserDataPath(ExpandConstant('{app}')) then
  begin
    Result := False;
    Reason := '当前程序目录位于娜娜莉用户数据目录内，已停止卸载以保护存档和设置。';
  end
  else
    Result := CheckPetClosed(Reason);
  if not Result then
  begin
    Log('Uninstall blocked before application files were changed.');
    SuppressibleMsgBox(Reason, mbError, MB_OK, IDOK);
  end;
end;
