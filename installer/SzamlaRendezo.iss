#ifndef PublishDir
  #error "Compile with /DPublishDir=<publish directory>"
#endif

[Setup]
AppId={{B1B26D41-808A-4E36-84DF-63D94D7519B0}
AppName=Szamlarendezo
AppVersion=1.0.0
AppVerName=Szamlarendezo 1.0.0
AppPublisher=Szamlarendezo
DefaultDirName={autopf}\Szamlarendezo
DefaultGroupName=Szamlarendezo
UninstallDisplayName=Szamlarendezo
UninstallDisplayIcon={app}\InvoiceManager.exe
SetupIconFile=..\Assets\InvoiceManager.ico
OutputDir=output
OutputBaseFilename=SzamlaRendezoSetup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=yes
RestartApplications=no
LanguageDetectionMethod=none

[Languages]
Name: "hungarian"; MessagesFile: "compiler:Languages\Hungarian.isl"

[Tasks]
Name: "desktopicon"; Description: "Asztali ikon letrehozasa"; GroupDescription: "Tovabbi ikonok:"
Name: "installollama"; Description: "Ollama telepitese (opcionalis, AI-elemzeshez)"; GroupDescription: "Opcionális osszetevok:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Szamlarendezo"; Filename: "{app}\InvoiceManager.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Szamlarendezo"; Filename: "{app}\InvoiceManager.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\InvoiceManager.exe"; Description: "Szamlarendezo inditasa"; Flags: postinstall nowait skipifsilent

[Code]
const
  WebView2RuntimeGuid = '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  WebView2BootstrapperUrl = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703';
  OllamaInstallerUrl = 'https://ollama.com/download/OllamaSetup.exe';

function IsRuntimeVersionInstalled(const RootKey: Integer; const SubKey: String): Boolean;
var
  Version: String;
begin
  Result :=
    RegQueryStringValue(RootKey, SubKey, 'pv', Version) and
    (Version <> '') and (Version <> '0.0.0.0');
end;

function IsWebView2RuntimeInstalled: Boolean;
begin
  Result :=
    IsRuntimeVersionInstalled(HKLM64, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\' + WebView2RuntimeGuid) or
    IsRuntimeVersionInstalled(HKCU64, 'Software\Microsoft\EdgeUpdate\Clients\' + WebView2RuntimeGuid) or
    IsRuntimeVersionInstalled(HKLM32, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\' + WebView2RuntimeGuid) or
    IsRuntimeVersionInstalled(HKCU32, 'Software\Microsoft\EdgeUpdate\Clients\' + WebView2RuntimeGuid);
end;

function IsOllamaInstalled: Boolean;
var
  ResultCode: Integer;
begin
  Result :=
    FileExists(ExpandConstant('{localappdata}\Programs\Ollama\ollama.exe')) or
    FileExists(ExpandConstant('{autopf}\Ollama\ollama.exe')) or
    FileExists(ExpandConstant('{pf32}\Ollama\ollama.exe'));
  if Result then
    Exit;

  Result := Exec('ollama.exe', '--version', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and
    (ResultCode = 0);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  BootstrapperPath: String;
  ResultCode: Integer;
begin
  Result := '';
  if IsWebView2RuntimeInstalled then
    Exit;

  BootstrapperPath := ExpandConstant('{tmp}\MicrosoftEdgeWebView2Setup.exe');
  try
    DownloadTemporaryFile(WebView2BootstrapperUrl, 'MicrosoftEdgeWebView2Setup.exe', '', nil);
  except
    Result := 'A WebView2 Runtime hianyzik, es a telepito nem tudta letolteni a Microsoft telepitot.' +
      Chr(13) + Chr(10) + GetExceptionMessage;
    Exit;
  end;

  if not Exec(BootstrapperPath, '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := 'A WebView2 Runtime telepitoje nem indithato el.';
    Exit;
  end;

  if not IsWebView2RuntimeInstalled then
    Result := 'A WebView2 Runtime telepitese sikertelen. Telepito visszateresi kod: ' +
      IntToStr(ResultCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  OllamaInstallerPath: String;
  ResultCode: Integer;
begin
  if (CurStep <> ssPostInstall) or
     not WizardIsTaskSelected('installollama') or
     IsOllamaInstalled then
    Exit;

  OllamaInstallerPath := ExpandConstant('{tmp}\OllamaSetup.exe');
  try
    DownloadTemporaryFile(OllamaInstallerUrl, 'OllamaSetup.exe', '', nil);
  except
    MsgBox('Az Ollama telepito letoltese sikertelen. Az AI-funkciok az Ollama kezi telepitese utan hasznalhatok.' +
      Chr(13) + Chr(10) + GetExceptionMessage, mbError, MB_OK);
    Exit;
  end;

  if not Exec(OllamaInstallerPath, '', '', SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode) then
  begin
    MsgBox('Az Ollama telepitoje nem indithato el. Az AI-funkciok az Ollama kezi telepitese utan hasznalhatok.',
      mbError, MB_OK);
    Exit;
  end;

  if not IsOllamaInstalled then
    MsgBox('Az Ollama telepitese nem fejezodott be sikeresen. Telepito visszateresi kod: ' +
      IntToStr(ResultCode), mbError, MB_OK);
end;
