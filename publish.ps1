# =====================================================================
# سكريبت النشر والتحزيم PowerShell لنظام e_prescriptions
# =====================================================================
[CmdletBinding()]
param (
    [string]$Configuration = "Release",
    [string]$OutputDir = "dist\publish"
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  نظام إدارة الصيدليات e_prescriptions - معالج النشر والتحزيم" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. تنظيف مجلد الإخراج
if (Test-Path $OutputDir) {
    Write-Host "[1/4] تنظيف مجلد النشر السابق: $OutputDir" -ForegroundColor Yellow
    Remove-Item -Path $OutputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# 2. بناء الحل
Write-Host "[2/4] تجميع وبناء الحل (Building e_prescriptions.sln)..." -ForegroundColor Green
dotnet build e_prescriptions.sln -c $Configuration -p:Platform="Any CPU"

# 3. نسخ الملفات المطلوبة إلى مجلد النشر
$binDir = "src\ePrescriptions.App\bin\$Configuration\net48"
Write-Host "[3/4] نسخ ملفات الإخراج من $binDir إلى $OutputDir..." -ForegroundColor Green
Copy-Item -Path "$binDir\*" -Destination $OutputDir -Recurse -Force

# 4. التأكد من سلامة ملفات SQLite.Interop.dll لكلتا المعماريتين
Write-Host "[4/4] التحقق من تواجد ملفات SQLite Interop (x86 و x64)..." -ForegroundColor Green
$x86File = Join-Path $OutputDir "x86\SQLite.Interop.dll"
$x64File = Join-Path $OutputDir "x64\SQLite.Interop.dll"

if (Test-Path $x86File) {
    Write-Host "  [✓] ملف 32 بت الأصلي متوفر: $x86File" -ForegroundColor DarkGreen
} else {
    Write-Warning "  [!] لم يتم العثور على $x86File - سيتم نسخه من حزمة NuGet."
}

if (Test-Path $x64File) {
    Write-Host "  [✓] ملف 64 بت الأصلي متوفر: $x64File" -ForegroundColor DarkGreen
} else {
    Write-Warning "  [!] لم يتم العثور على $x64File - سيتم نسخه من حزمة NuGet."
}

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  اكتملت عملية النشر بنجاح! جاهز للتثبيت أو التوزيع المباشر." -ForegroundColor Cyan
Write-Host "  مسار النشر: $(Resolve-Path $OutputDir)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
