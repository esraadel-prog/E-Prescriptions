@echo off
chcp 65001 > nul
setlocal enabledelayedexpansion

echo ======================================================================
echo   e_prescriptions - سكريبت البناء والتحزيم الرسمي لنظام الصيدليات
echo ======================================================================
echo.

:: 1) التحقق من وجود أدوات البناء
where msbuild >nul 2>nul
if %errorlevel% neq 0 (
    where dotnet >nul 2>nul
    if %errorlevel% neq 0 (
        echo [خطأ] لم يتم العثور على msbuild أو dotnet في متغيرات البيئة PATH.
        echo يرجى تشغيل السكريبت من داخل Developer Command Prompt for Visual Studio.
        pause
        exit /b 1
    )
    set BUILD_TOOL=dotnet
) else (
    set BUILD_TOOL=msbuild
)

echo [*] أداة البناء المستخدمة: %BUILD_TOOL%
echo.

:: 2) استعادة حزم NuGet
echo [1/5] استعادة حزم NuGet (NuGet Restore)...
if "%BUILD_TOOL%"=="dotnet" (
    dotnet restore e_prescriptions.sln
) else (
    nuget restore e_prescriptions.sln
)
if %errorlevel% neq 0 (
    echo [خطأ] فشلت استعادة حزم NuGet.
    pause
    exit /b 1
)

:: 3) بناء الحل في نمط Release AnyCPU
echo.
echo [2/5] ترجمة المشروع (Building Solution in Release AnyCPU)...
if "%BUILD_TOOL%"=="dotnet" (
    dotnet build e_prescriptions.sln -c Release -p:Platform="Any CPU" --no-restore
) else (
    msbuild e_prescriptions.sln /p:Configuration=Release /p:Platform="Any CPU" /m /v:m
)
if %errorlevel% neq 0 (
    echo [خطأ] حدث خطأ أثناء ترجمة المشروع.
    pause
    exit /b 1
)

:: 4) تشغيل الاختبارات الآلية (xUnit)
echo.
echo [3/5] تشغيل الاختبارات الآلية (Running Unit Tests)...
if "%BUILD_TOOL%"=="dotnet" (
    dotnet test tests/ePrescriptions.Tests/ePrescriptions.Tests.csproj -c Release --no-build
) else (
    vstest.console.exe tests/ePrescriptions.Tests/bin/Release/net48/ePrescriptions.Tests.dll
)
if %errorlevel% neq 0 (
    echo.
    echo [خطأ] فشل اختبار آلي واحد على الأقل ^(أو تعذّر تشغيل الاختبارات، مثلاً vstest.console.exe غير موجود في PATH^).
    echo تم إيقاف البناء والتحزيم. راجع تقرير الاختبارات أعلاه وأصلح الكود ^(مش الاختبار^) ثم أعد التشغيل.
    pause
    exit /b 1
)

:: 5) التأكد من وجود ملفات SQLite.Interop.dll الأصلية في مسارات x86 و x64
echo.
echo [4/5] التحقق من ملفات SQLite Interop الأصلية...
set APP_BIN=src\ePrescriptions.App\bin\Release\net48

if not exist "%APP_BIN%\x86\SQLite.Interop.dll" (
    echo [تنبيه] نسخ SQLite.Interop.dll إلى مجلد x86...
    mkdir "%APP_BIN%\x86" 2>nul
    copy /y "packages\Stub.System.Data.SQLite.Core.NetFramework.1.0.118.0\build\net48\x86\SQLite.Interop.dll" "%APP_BIN%\x86\" >nul 2>nul
)

if not exist "%APP_BIN%\x64\SQLite.Interop.dll" (
    echo [تنبيه] نسخ SQLite.Interop.dll إلى مجلد x64...
    mkdir "%APP_BIN%\x64" 2>nul
    copy /y "packages\Stub.System.Data.SQLite.Core.NetFramework.1.0.118.0\build\net48\x64\SQLite.Interop.dll" "%APP_BIN%\x64\" >nul 2>nul
)

:: 6) تجميع ملف التثبيت بواسطة Inno Setup إن وجد
echo.
echo [5/5] تجميع ملف التثبيت (Compiling Inno Setup Installer)...
set ISCC_PATH="C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if exist %ISCC_PATH% (
    mkdir "dist\installer" 2>nul
    %ISCC_PATH% installer\e_prescriptions_installer.iss
    if %errorlevel% equ 0 (
        echo.
        echo [نجاح] تم بناء ملف التثبيت بنجاح في: dist\installer\e_prescriptions_Setup_v1.0.0.exe
    ) else (
        echo [خطأ] فشل تجميع ملف التثبيت عبر Inno Setup.
    )
) else (
    echo [ملاحظة] لم يتم العثور على Inno Setup في المسار الافتراضي.
    echo يمكنك تجميع ملف installer\e_prescriptions_installer.iss يدوياً عبر Inno Setup Compiler.
)

echo.
echo ======================================================================
echo   اكتمل البناء بنجاح! مجلد الإخراج: %APP_BIN%
echo ======================================================================
pause
