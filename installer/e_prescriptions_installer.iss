; =====================================================================
; سكريبت تثبيت برنامج e_prescriptions لإدارة الصيدليات والوصفات الطبية
; متوافق مع Inno Setup 6.x
; يدعم Windows 7 SP1، 8.1، 10، و 11 بنسختي 32 بت و 64 بت (AnyCPU)
; =====================================================================

#define MyAppName "e_prescriptions"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Maged Fayez"
#define MyAppDisplayName "E-Prescriptions by Maged Fayez"
#define MyAppCopyright "(C) 2026 Maged Fayez. All rights reserved."
#define MyAppExeName "ePrescriptions.exe"
#define MyAppAssocName "ePrescriptions Data"
#define MyAppAssocExt ".eprx"

; مثبّت .NET Framework 4.8 الكامل (Offline) اختياري: ضع الملف ndp48-x86-x64-allos-enu.exe في installer\redist قبل البناء
; فيتضمّنه المثبّت ويثبّته تلقائياً عند غياب 4.8 (مفيد لويندوز 7 والأجهزة بدون إنترنت). لو الملف غير موجود، المثبّت يعمل كالسابق.
#define Net48RedistName "ndp48-x86-x64-allos-enu.exe"
#ifexist "redist\ndp48-x86-x64-allos-enu.exe"
  #define HaveNet48Redist
#endif

[Setup]
; الهوية الأساسية
AppId={{9F82A41C-328E-45A7-A279-99E8F8B4C011}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppCopyright={#MyAppCopyright}
AppVerName={#MyAppDisplayName} {#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoCopyright={#MyAppCopyright}
VersionInfoProductName={#MyAppDisplayName}
VersionInfoDescription={#MyAppDisplayName}
VersionInfoVersion={#MyAppVersion}.0
DefaultDirName={autopf}\e_prescriptions
DefaultGroupName=e_prescriptions
UninstallDisplayIcon={app}\app.ico
SetupIconFile=..\src\ePrescriptions.App\Resources\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
OutputDir=..\dist\installer
OutputBaseFilename=e_prescriptions_Setup_v1.0.0

ChangesAssociations=yes

; متطلبات نظام التشغيل الصارمة: Windows 7 Service Pack 1 فما فوق
MinVersion=6.1sp1

; دعم معماريتي 32 بت و 64 بت
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x86 x64

; الواجهة واللغة العربية
WizardStyle=modern
ShowLanguageDialog=no

; صلاحيات المسؤول لتثبيت الخطوط والخدمات في Program Files
PrivilegesRequired=admin

[Languages]
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"

[Messages]
arabic.WelcomeLabel1=مرحباً بك في مثبت برنامج {#MyAppDisplayName}
arabic.WelcomeLabel2=سيقوم هذا المعالج بتثبيت نظام إدارة الصيدليات المتكامل {#MyAppDisplayName} الإصدار [version] على جهازك.%n%nالبرنامج يعمل محلياً بالكامل بدون إنترنت، ويدعم طباعة الإيصالات والباركود.%n%n{#MyAppCopyright}

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[InstallDelete]
; حذف كافة اختصارات سطح المكتب وقائمة البرامج القديمة لضمان تحديث كاش الأيقونة في ويندوز فورياً
Type: files; Name: "{autodesktop}\{#MyAppName}.lnk"
Type: files; Name: "{userdesktop}\{#MyAppName}.lnk"
Type: files; Name: "{commondesktop}\{#MyAppName}.lnk"
Type: files; Name: "{autodesktop}\{#MyAppDisplayName}.lnk"
Type: files; Name: "{userdesktop}\{#MyAppDisplayName}.lnk"
Type: files; Name: "{commondesktop}\{#MyAppDisplayName}.lnk"
Type: files; Name: "{autodesktop}\ePrescriptions.lnk"
Type: files; Name: "{userdesktop}\ePrescriptions.lnk"
Type: files; Name: "{commondesktop}\ePrescriptions.lnk"
Type: files; Name: "{group}\{#MyAppName}.lnk"
Type: files; Name: "{group}\*.lnk"
Type: files; Name: "{app}\app.ico"

[Files]
; 1) الملف التنفيذي الرئيسي ومكتبات المشروع
Source: "..\src\ePrescriptions.App\bin\Release\net48\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.xml"

; ملف أيقونة البرنامج المعتمد (الكبسولة) لضمان ظهوره على سطح المكتب بدون مشاكل
Source: "..\src\ePrescriptions.App\Resources\app.ico"; DestDir: "{app}"; Flags: ignoreversion

; 2) ملفات SQLite الأصلية (Native Interop DLLs) - حاسمة للعمل في AnyCPU على 32 و 64 بت
; يتم نسخها بجانب البرنامج داخل مجلدي x86 و x64 حتى يتمكن محرك SQLite من تحميل المعمارية الصحيحة تلقائياً
Source: "..\src\ePrescriptions.App\bin\Release\net48\x86\SQLite.Interop.dll"; DestDir: "{app}\x86"; Flags: ignoreversion
Source: "..\src\ePrescriptions.App\bin\Release\net48\x64\SQLite.Interop.dll"; DestDir: "{app}\x64"; Flags: ignoreversion; Check: Is64BitInstallMode

#ifdef HaveNet48Redist
; مثبّت .NET 4.8 (يُستخرج مؤقتاً عند الحاجة فقط ولا يُنسخ لمجلد البرنامج)
Source: "redist\{#Net48RedistName}"; DestDir: "{tmp}"; Flags: dontcopy
#endif

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\app.ico"; IconIndex: 0
Name: "{group}\إلغاء تثبيت {#MyAppName}"; Filename: "{uninstallexe}"; IconFilename: "{app}\app.ico"; IconIndex: 0
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\app.ico"; IconIndex: 0; Tasks: desktopicon

[Dirs]
; مجلد البيانات المشترك (قاعدة البيانات والنسخ الاحتياطية والسجلات والتصدير) في ProgramData
; بصلاحيات تعديل لكل مستخدمي الجهاز؛ لا يُحذف عند إلغاء التثبيت حفاظاً على البيانات
Name: "{commonappdata}\ePrescriptions"; Permissions: users-modify; Flags: uninsneveruninstall
Name: "{commonappdata}\ePrescriptions\backups"; Permissions: users-modify; Flags: uninsneveruninstall
Name: "{commonappdata}\ePrescriptions\logs"; Permissions: users-modify; Flags: uninsneveruninstall
Name: "{commonappdata}\ePrescriptions\exports"; Permissions: users-modify; Flags: uninsneveruninstall

[Run]
Filename: "{sys}\ie4uinit.exe"; Parameters: "-show"; Flags: runhidden skipifdoesntexist; StatusMsg: "تحديث كاش أيقونات النظام..."
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
// التحقق من تثبيت .NET Framework 4.8 على نظام التشغيل
// ملاحظة: البرنامج مبني على 4.8 (وليس 4.8.1). النسخة 4.8.1 لا تعمل على ويندوز 7 ولا مطلوبة.
function IsNet48Installed(): Boolean;
var
  ReleaseValue: Cardinal;
  Success: Boolean;
begin
  Result := False;
  // فحص سجل النظام لنظام .NET Framework 4.5+
  Success := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', ReleaseValue);
  if Success then
  begin
    // رقم الإصدار 528040 يمثل .NET Framework 4.8
    if ReleaseValue >= 528040 then
      Result := True;
  end;
end;

function IsWindows7(): Boolean;
var
  V: TWindowsVersion;
begin
  GetWindowsVersionEx(V);
  Result := (V.Major = 6) and (V.Minor = 1);
end;

// رسالة متطلبات .NET 4.8 على ويندوز 7 (الدعم يحتاج تحديثين قبل 4.8)
function Windows7Net48Message(): String;
begin
  Result :=
    'يتطلب البرنامج .NET Framework 4.8 (وليس 4.8.1؛ النسخة 4.8.1 لا تعمل على ويندوز 7 أصلاً).' + #13#10 + #13#10 +
    'على Windows 7 SP1 نفّذ بالترتيب:' + #13#10 +
    '1) ثبّت التحديث KB4474419 (دعم توقيع SHA-2) ثم KB4490628 (تحديث الخدمة).' + #13#10 +
    '2) ثبّت .NET Framework 4.8 من المثبّت الكامل (Offline Installer) بعد تنزيله من جهاز آخر على فلاشة ' +
    '(متصفح ويندوز 7 القديم قد لا يفتح موقع مايكروسوفت لعدم تفعيل TLS 1.2).' + #13#10 +
    '3) أعد تشغيل هذا المثبّت.' + #13#10 + #13#10 +
    'ملاحظة: دعم مايكروسوفت لويندوز 7 انتهى منذ 2020، ويُنصح بويندوز 10 أو 11 لجهاز نقطة البيع.';
end;

// منع التثبيت فوق نسخة تعمل حالياً عبر فحص الـ Mutex
function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;

  // التحقق من عدم تشغيل البرنامج حالياً
  if CheckForMutexes('e_prescriptions_SingleInstance_Mutex_9F82A41C') then
  begin
    MsgBox('برنامج e_prescriptions يعمل حالياً في الخلفية.' + #13#10 +
           'يرجى إغلاق البرنامج أولاً قبل متابعة التثبيت أو التحديث.', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  // التحقق من توفر .NET Framework 4.8
  if not IsNet48Installed() then
  begin
#ifdef HaveNet48Redist
    // المثبّت يحمل .NET 4.8 بداخله، وبيتثبّت تلقائياً قبل نسخ ملفات البرنامج (PrepareToInstall)
    Exit;
#else
    if IsWindows7() then
    begin
      MsgBox(Windows7Net48Message(), mbInformation, MB_OK);
    end
    else if MsgBox('يتطلب تشغيل نظام e_prescriptions توفر .NET Framework 4.8 على هذا الجهاز.' + #13#10 +
              'هل ترغب في تنزيله وتثبيته الآن من مايكروسوفت؟', mbConfirmation, MB_YESNO) = IDYES then
    begin
      // فتح رابط التحميل الرسمي لـ .NET Framework 4.8
      ShellExec('open', 'https://go.microsoft.com/fwlink/?linkid=2088631', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    end;
    Result := False;
    Exit;
#endif
  end;
end;

#ifdef HaveNet48Redist
// تثبيت .NET 4.8 الكامل صامتاً قبل نسخ الملفات؛ أي فشل يوقف التثبيت برسالة واضحة
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  if IsNet48Installed() then Exit;

  ExtractTemporaryFile('{#Net48RedistName}');
  if not Exec(ExpandConstant('{tmp}\{#Net48RedistName}'), '/q /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := 'تعذر تشغيل مثبّت .NET Framework 4.8.';
    Exit;
  end;

  if (ResultCode = 3010) or (ResultCode = 1641) then
  begin
    // تم التثبيت ويحتاج إعادة تشغيل؛ المثبّت يكمل بعدها
    NeedsRestart := True;
  end
  else if ResultCode <> 0 then
  begin
    Result := 'فشل تثبيت .NET Framework 4.8 (رمز الخطأ ' + IntToStr(ResultCode) + ').';
    if IsWindows7() then
      Result := Result + #13#10 + #13#10 + Windows7Net48Message();
  end;
end;
#endif

// إيقاف البرنامج عند الرغبة في إزالة التثبيت
function InitializeUninstall(): Boolean;
begin
  Result := True;
  if CheckForMutexes('e_prescriptions_SingleInstance_Mutex_9F82A41C') then
  begin
    MsgBox('برنامج e_prescriptions يعمل حالياً.' + #13#10 +
           'يرجى إغلاق البرنامج بالكامل قبل البدء في إزالة التثبيت.', mbError, MB_OK);
    Result := False;
  end;
end;

// بعد الإزالة: سؤال اختياري عن حذف بيانات الصيدلية (الافتراضي: لا). الإزالة الصامتة لا تحذف البيانات أبداً.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep <> usPostUninstall then Exit;
  if UninstallSilent() then Exit;

  DataDir := ExpandConstant('{commonappdata}\ePrescriptions');
  if not DirExists(DataDir) then Exit;

  if MsgBox('تمت إزالة البرنامج، لكن بيانات الصيدلية (قاعدة البيانات والنسخ الاحتياطية والسجلات) ما زالت محفوظة في:' + #13#10 +
            DataDir + #13#10 + #13#10 +
            'هل تريد حذفها أيضاً؟' + #13#10 +
            'اختر "لا" للإبقاء عليها (الأسلم: إعادة التثبيت لاحقاً ستجد بياناتك كما هي).',
            mbConfirmation, MB_YESNO or MB_DEFBUTTON2) <> IDYES then Exit;

  // تأكيد ثانٍ لأن الحذف نهائي ولا يمكن التراجع عنه
  if MsgBox('تحذير: سيتم حذف كل بيانات الصيدلية نهائياً (المبيعات والمخزون والعملاء والنسخ الاحتياطية)، ولا يمكن استرجاعها.' + #13#10 + #13#10 +
            'هل أنت متأكد؟', mbError, MB_YESNO or MB_DEFBUTTON2) = IDYES then
  begin
    DelTree(DataDir, True, True, True);
  end;
end;
