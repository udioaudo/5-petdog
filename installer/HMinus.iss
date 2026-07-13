#define MyAppName "小狗效率屋"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "HMinus"
#define MyAppExeName "HMinus.DesktopSpike.exe"
#ifndef MySourceDir
  #error MySourceDir is required
#endif
#ifndef MyOutputDir
  #error MyOutputDir is required
#endif
#ifndef MyOutputBaseFilename
  #define MyOutputBaseFilename "小狗效率屋-Setup-1.0.0-x64"
#endif
#ifndef MyAppDataDir
  #define MyAppDataDir "{localappdata}\HMinus\DesktopSpike"
#endif

[Setup]
AppId={{5DFEAE73-9D75-4C6F-B8FA-2E89FAB075BF}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion=1.0.0.0
VersionInfoProductVersion=1.0.0.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} 安装程序
VersionInfoProductName={#MyAppName}
DefaultDirName={localappdata}\Programs\HMinus\DesktopEfficiency
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
UsedUserAreasWarning=no
ArchitecturesAllowed=x64compatible
MinVersion=10.0.17763
OutputDir={#MyOutputDir}
OutputBaseFilename={#MyOutputBaseFilename}
SetupIconFile=..\prototypes\DesktopSpike\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName}
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "其他选项："; Flags: unchecked

[Files]
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "立即运行{#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
var
  DeleteLocalData: Boolean;

function HasCommandLineParameter(const Expected: String): Boolean;
var
  Index: Integer;
begin
  Result := False;
  for Index := 1 to ParamCount do
  begin
    if CompareText(ParamStr(Index), Expected) = 0 then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  DeleteLocalData := False;

  if UninstallSilent then
    DeleteLocalData := HasCommandLineParameter('/DELETEAPPDATA')
  else
    DeleteLocalData :=
      MsgBox(
        '是否同时删除这台电脑上的待办、剪贴板历史和设置？' + #13#10 + #13#10 +
        '选择“否”（推荐）将保留本机数据，重新安装后仍可继续使用。' + #13#10 +
        '只有选择“是”才会删除：' + #13#10 +
        ExpandConstant('{#MyAppDataDir}'),
        mbConfirmation,
        MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ManagedDataDirectory: String;
begin
  if (CurUninstallStep = usPostUninstall) and DeleteLocalData then
  begin
    ManagedDataDirectory := ExpandConstant('{#MyAppDataDir}');
    DelTree(ManagedDataDirectory, True, True, True);
    if DirExists(ManagedDataDirectory) then
      MsgBox(
        '程序已经卸载，但部分本机数据未能删除。' + #13#10 +
        '剩余位置：' + ManagedDataDirectory,
        mbInformation,
        MB_OK);
  end;
end;
