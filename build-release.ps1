<#
    يبني حزمة التوزيع كاملة:

      dist\PrayTimeSetup.exe        مثبّت بملف واحد، بلا أي متطلبات مسبقة
      dist\PrayTime-portable.zip    نسخة محمولة: فك الضغط وشغّل

    الاستخدام:  powershell -ExecutionPolicy Bypass -File build-release.ps1
#>

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
$stage = Join-Path $root 'publish\app'
$payload = Join-Path $root 'installer\PrayTimeSetup\payload.zip'

function Step($text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }

# التطبيق قد يكون قيد التشغيل من بناء سابق فيقفل الملفات.
Get-Process PrayTime -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400

Step 'الاختبارات'
dotnet test (Join-Path $root 'tests\PrayTime.Core.Tests') --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'فشلت الاختبارات — أُوقف البناء.' }

Step 'نشر التطبيق (مستقل، ملف واحد)'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
dotnet publish (Join-Path $root 'src\PrayTime.App') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $stage --nologo
if ($LASTEXITCODE -ne 0) { throw 'فشل نشر التطبيق.' }

Get-ChildItem $stage -Filter *.pdb -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue

if (-not (Test-Path (Join-Path $stage 'PrayTime.exe'))) { throw 'الملف التنفيذي غير موجود بعد النشر.' }
$audio = Get-ChildItem (Join-Path $stage 'audio') -Filter *.mp3 -ErrorAction SilentlyContinue
if (-not $audio) { throw 'مجلد الصوت فارغ — لن يعمل الأذان.' }
Write-Host "ملفات الصوت: $($audio.Count)"

Step 'تجهيز حمولة المثبّت'
if (Test-Path $payload) { Remove-Item $payload -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $payload -CompressionLevel Optimal

Step 'بناء المثبّت'
$setupOut = Join-Path $root 'publish\setup'
if (Test-Path $setupOut) { Remove-Item $setupOut -Recurse -Force }
dotnet publish (Join-Path $root 'installer\PrayTimeSetup') `
    -c Release -o $setupOut --nologo
if ($LASTEXITCODE -ne 0) { throw 'فشل بناء المثبّت.' }

Step 'تجميع مجلد التوزيع'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $dist | Out-Null

Copy-Item (Join-Path $setupOut 'PrayTimeSetup.exe') (Join-Path $dist 'PrayTimeSetup.exe')
Compress-Archive -Path (Join-Path $stage '*') `
    -DestinationPath (Join-Path $dist 'PrayTime-portable.zip') -CompressionLevel Optimal

Remove-Item $payload -Force -ErrorAction SilentlyContinue

Step 'تم'
Get-ChildItem $dist | ForEach-Object {
    '{0,-30} {1,8:N1} م.ب' -f $_.Name, ($_.Length / 1MB)
}
Write-Host "`nالمخرجات في: $dist" -ForegroundColor Green
