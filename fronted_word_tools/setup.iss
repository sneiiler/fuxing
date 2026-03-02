; ==========================================================================
; 福星 Word 插件 — Inno Setup 安装脚本
; ==========================================================================
; 使用方法：
;   1. 先用 Release 配置编译项目（生成 bin\Release\FuXing.dll）
;   2. 用 Inno Setup Compiler 编译本脚本
;   3. 生成的安装包在 Output\ 目录
; ==========================================================================

#define MyAppName      "福星"
#define MyAppNameEn    "FuXing"
#define MyAppVersion   "2.0.0"
#define MyAppPublisher "FuXing"
#define MyAppURL       ""

; 源文件来自 Release 输出
#define BuildDir       "bin\Release"

[Setup]
AppId={{E7A3B1C5-4D2F-4E8A-B9C6-1F3D5A7E9B0C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={localappdata}\{#MyAppNameEn}
DefaultGroupName={#MyAppName}
; 不需要管理员权限，安装到用户目录
PrivilegesRequired=lowest
; 输出设置
OutputDir=Output
OutputBaseFilename=FuXing_Setup_{#MyAppVersion}
; 压缩设置
Compression=lzma2/ultra64
SolidCompression=yes
; UI 设置
WizardStyle=modern
; 禁用不需要的页面
DisableProgramGroupPage=yes
; 卸载时显示
UninstallDisplayName={#MyAppName} Word 插件
; 安装前关闭 Word
CloseApplications=force
CloseApplicationsFilter=WINWORD.EXE
; 架构
ArchitecturesAllowed=x64compatible or x86compatible
; 版本限制：需要 Windows 7+
MinVersion=6.1

[Languages]
Name: "default"; MessagesFile: "compiler:Default.isl"

[Files]
; 主程序集
Source: "{#BuildDir}\FuXing.dll";               DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\FuXing.dll.config";         DestDir: "{app}"; Flags: ignoreversion
; Ribbon 定义
Source: "{#BuildDir}\RibbonUI.xml";              DestDir: "{app}"; Flags: ignoreversion
; 依赖 DLL
Source: "{#BuildDir}\AntdUI.dll";                DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\AntdUI.EmojiFluentFlat.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\Markdown.dll";              DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\NetOffice.dll";             DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\Newtonsoft.Json.dll";       DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\OfficeApi.dll";             DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\stdole.dll";                DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\System.Drawing.Common.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\VBIDEApi.dll";              DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\WordApi.dll";               DestDir: "{app}"; Flags: ignoreversion
; 资源文件
Source: "{#BuildDir}\Resources\*";               DestDir: "{app}\Resources"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
; ============================================================
; COM 注册 — FuXing.Connect（插件入口）
; ============================================================
Root: HKCU; Subkey: "Software\Classes\CLSID\{{C9F68F90-E8C4-4A8B-9A8B-5E6F7D8E9F0A}";                                                    ValueType: string; ValueName: ""; ValueData: "FuXing.Connect";                                                               Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\CLSID\{{C9F68F90-E8C4-4A8B-9A8B-5E6F7D8E9F0A}\InprocServer32";                                     ValueType: string; ValueName: "";               ValueData: "mscoree.dll";                                                     Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\CLSID\{{C9F68F90-E8C4-4A8B-9A8B-5E6F7D8E9F0A}\InprocServer32";                                     ValueType: string; ValueName: "ThreadingModel";  ValueData: "Both"
Root: HKCU; Subkey: "Software\Classes\CLSID\{{C9F68F90-E8C4-4A8B-9A8B-5E6F7D8E9F0A}\InprocServer32";                                     ValueType: string; ValueName: "Class";           ValueData: "FuXing.Connect"
Root: HKCU; Subkey: "Software\Classes\CLSID\{{C9F68F90-E8C4-4A8B-9A8B-5E6F7D8E9F0A}\InprocServer32";                                     ValueType: string; ValueName: "Assembly";        ValueData: "FuXing, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"
Root: HKCU; Subkey: "Software\Classes\CLSID\{{C9F68F90-E8C4-4A8B-9A8B-5E6F7D8E9F0A}\InprocServer32";                                     ValueType: string; ValueName: "RuntimeVersion";  ValueData: "v4.0.30319"
Root: HKCU; Subkey: "Software\Classes\CLSID\{{C9F68F90-E8C4-4A8B-9A8B-5E6F7D8E9F0A}\InprocServer32";                                     ValueType: string; ValueName: "CodeBase";        ValueData: "file:///{code:ConvertBackslash|{app}\FuXing.dll}"
Root: HKCU; Subkey: "Software\Classes\CLSID\{{C9F68F90-E8C4-4A8B-9A8B-5E6F7D8E9F0A}\InprocServer32\1.0.0.0";                             ValueType: string; ValueName: "Class";           ValueData: "FuXing.Connect"
Root: HKCU; Subkey: "Software\Classes\CLSID\{{C9F68F90-E8C4-4A8B-9A8B-5E6F7D8E9F0A}\InprocServer32\1.0.0.0";                             ValueType: string; ValueName: "Assembly";        ValueData: "FuXing, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"
Root: HKCU; Subkey: "Software\Classes\CLSID\{{C9F68F90-E8C4-4A8B-9A8B-5E6F7D8E9F0A}\InprocServer32\1.0.0.0";                             ValueType: string; ValueName: "RuntimeVersion";  ValueData: "v4.0.30319"
Root: HKCU; Subkey: "Software\Classes\CLSID\{{C9F68F90-E8C4-4A8B-9A8B-5E6F7D8E9F0A}\InprocServer32\1.0.0.0";                             ValueType: string; ValueName: "CodeBase";        ValueData: "file:///{code:ConvertBackslash|{app}\FuXing.dll}"
Root: HKCU; Subkey: "Software\Classes\CLSID\{{C9F68F90-E8C4-4A8B-9A8B-5E6F7D8E9F0A}\ProgId";                                             ValueType: string; ValueName: "";               ValueData: "FuXing.Connect"
Root: HKCU; Subkey: "Software\Classes\CLSID\{{C9F68F90-E8C4-4A8B-9A8B-5E6F7D8E9F0A}\Implemented Categories\{{62C8FE65-4EBB-45E7-B440-6E39B2CDBF29}"; ValueType: none;                                                                                                        Flags: uninsdeletekey
; ProgId -> CLSID
Root: HKCU; Subkey: "Software\Classes\FuXing.Connect";                                                                                     ValueType: string; ValueName: "";               ValueData: "FuXing.Connect";                                                  Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\FuXing.Connect\CLSID";                                                                               ValueType: string; ValueName: "";               ValueData: "{{C9F68F90-E8C4-4A8B-9A8B-5E6F7D8E9F0A}"

; ============================================================
; COM 注册 — FuXing.TaskPaneControl
; ============================================================
Root: HKCU; Subkey: "Software\Classes\CLSID\{{03326A51-B257-3623-917E-25A086B271B0}";                                                      ValueType: string; ValueName: "";               ValueData: "FuXing.TaskPaneControl";                                          Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\CLSID\{{03326A51-B257-3623-917E-25A086B271B0}\InprocServer32";                                       ValueType: string; ValueName: "";               ValueData: "mscoree.dll";                                                     Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\CLSID\{{03326A51-B257-3623-917E-25A086B271B0}\InprocServer32";                                       ValueType: string; ValueName: "ThreadingModel";  ValueData: "Both"
Root: HKCU; Subkey: "Software\Classes\CLSID\{{03326A51-B257-3623-917E-25A086B271B0}\InprocServer32";                                       ValueType: string; ValueName: "Class";           ValueData: "FuXing.TaskPaneControl"
Root: HKCU; Subkey: "Software\Classes\CLSID\{{03326A51-B257-3623-917E-25A086B271B0}\InprocServer32";                                       ValueType: string; ValueName: "Assembly";        ValueData: "FuXing, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"
Root: HKCU; Subkey: "Software\Classes\CLSID\{{03326A51-B257-3623-917E-25A086B271B0}\InprocServer32";                                       ValueType: string; ValueName: "RuntimeVersion";  ValueData: "v4.0.30319"
Root: HKCU; Subkey: "Software\Classes\CLSID\{{03326A51-B257-3623-917E-25A086B271B0}\InprocServer32";                                       ValueType: string; ValueName: "CodeBase";        ValueData: "file:///{code:ConvertBackslash|{app}\FuXing.dll}"
Root: HKCU; Subkey: "Software\Classes\CLSID\{{03326A51-B257-3623-917E-25A086B271B0}\InprocServer32\1.0.0.0";                               ValueType: string; ValueName: "Class";           ValueData: "FuXing.TaskPaneControl"
Root: HKCU; Subkey: "Software\Classes\CLSID\{{03326A51-B257-3623-917E-25A086B271B0}\InprocServer32\1.0.0.0";                               ValueType: string; ValueName: "Assembly";        ValueData: "FuXing, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"
Root: HKCU; Subkey: "Software\Classes\CLSID\{{03326A51-B257-3623-917E-25A086B271B0}\InprocServer32\1.0.0.0";                               ValueType: string; ValueName: "RuntimeVersion";  ValueData: "v4.0.30319"
Root: HKCU; Subkey: "Software\Classes\CLSID\{{03326A51-B257-3623-917E-25A086B271B0}\InprocServer32\1.0.0.0";                               ValueType: string; ValueName: "CodeBase";        ValueData: "file:///{code:ConvertBackslash|{app}\FuXing.dll}"
Root: HKCU; Subkey: "Software\Classes\CLSID\{{03326A51-B257-3623-917E-25A086B271B0}\ProgId";                                               ValueType: string; ValueName: "";               ValueData: "FuXing.TaskPaneControl"
Root: HKCU; Subkey: "Software\Classes\CLSID\{{03326A51-B257-3623-917E-25A086B271B0}\Implemented Categories\{{62C8FE65-4EBB-45E7-B440-6E39B2CDBF29}"; ValueType: none;                                                                                                        Flags: uninsdeletekey
; ProgId -> CLSID
Root: HKCU; Subkey: "Software\Classes\FuXing.TaskPaneControl";                                                                             ValueType: string; ValueName: "";               ValueData: "FuXing.TaskPaneControl";                                          Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\FuXing.TaskPaneControl\CLSID";                                                                       ValueType: string; ValueName: "";               ValueData: "{{03326A51-B257-3623-917E-25A086B271B0}"

; ============================================================
; Office Word Add-in 注册
; ============================================================
Root: HKCU; Subkey: "Software\Microsoft\Office\Word\Addins\FuXing.Connect"; ValueType: string; ValueName: "Description";    ValueData: "福星插件 - AI文本纠错、标准校验、表格格式化";  Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Office\Word\Addins\FuXing.Connect"; ValueType: string; ValueName: "FriendlyName";   ValueData: "福星"
Root: HKCU; Subkey: "Software\Microsoft\Office\Word\Addins\FuXing.Connect"; ValueType: dword;  ValueName: "LoadBehavior";   ValueData: "3"

[Icons]
Name: "{group}\卸载{#MyAppName}"; Filename: "{uninstallexe}"

[Code]
// 将反斜杠路径转换为 CodeBase 需要的正斜杠格式
function ConvertBackslash(Param: String): String;
begin
  Result := Param;
  StringChangeEx(Result, '\', '/', True);
end;

// 安装前检查 .NET Framework 4.7 是否已安装
function IsDotNetInstalled(): Boolean;
var
  Release: Cardinal;
begin
  Result := False;
  if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) then
  begin
    // 460798 = .NET 4.7
    Result := (Release >= 460798);
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not IsDotNetInstalled() then
  begin
    MsgBox('福星插件需要 .NET Framework 4.7 或更高版本。'#13#10#13#10
           '请先从 Microsoft 官网下载安装 .NET Framework 4.7，然后重新运行安装程序。',
           mbCriticalError, MB_OK);
    Result := False;
  end;
end;

// 安装前关闭 Word（如果正在运行）
procedure CloseWordIfRunning();
var
  ResultCode: Integer;
begin
  Exec('taskkill', '/f /im WINWORD.EXE', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  // 等待 Word 完全退出
  Sleep(1000);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    CloseWordIfRunning();
  end;
end;

// 卸载前关闭 Word
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    Exec('taskkill', '/f /im WINWORD.EXE', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(1000);
  end;
end;
