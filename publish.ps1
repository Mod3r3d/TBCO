<#
.SYNOPSIS
    TBCO 3.0 Production Build & Packaging Script
.DESCRIPTION
    Tự động hóa toàn bộ quy trình: Chạy kiểm thử -> Biên dịch Release -> Đóng gói thư mục -> Nén ZIP.
.PARAMETER SelfContained
    Nếu bật, sẽ đóng gói kèm toàn bộ .NET 8 Runtime (chạy không cần cài trước .NET 8).
.EXAMPLE
    .\publish.ps1
    .\publish.ps1 -SelfContained
#>

param(
    [switch]$SelfContained = $false,
    [switch]$SkipTests = $false
)

$ErrorActionPreference = "Stop"
$sw = [System.Diagnostics.Stopwatch]::StartNew()

Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "          TBCO 3.0 - PRODUCTION RELEASE PACKAGING               " -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan

$WorkspaceRoot = $PSScriptRoot
$ProjectFile = Join-Path $WorkspaceRoot "TranslateBot\TranslateBot.csproj"
$TestProject = Join-Path $WorkspaceRoot "TranslateBot.Tests\TranslateBot.Tests.csproj"
$PublishDir = Join-Path $WorkspaceRoot "publish\TBCO-3.0-win-x64"
$ZipFile = Join-Path $WorkspaceRoot "publish\TBCO-3.0-win-x64.zip"

# 1. Chạy Unit Tests
if (-not $SkipTests) {
    Write-Host "`n[STEP 1/4] Đang chạy kiểm thử tự động (Unit Test Suite)..." -ForegroundColor Yellow
    dotnet test $TestProject -c Release --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Kiểm thử thất bại! Hủy bỏ quá trình đóng gói phát hành."
        exit 1
    }
    Write-Host "-> Toàn bộ bài kiểm thử đã vượt qua thành công!" -ForegroundColor Green
} else {
    Write-Host "`n[STEP 1/4] Đã bỏ qua kiểm thử (-SkipTests)." -ForegroundColor DarkGray
}

# 2. Dọn dẹp thư mục xuất bản cũ
Write-Host "`n[STEP 2/4] Chuẩn bị thư mục phát hành: $PublishDir" -ForegroundColor Yellow
if (Test-Path $PublishDir) {
    Remove-Item $PublishDir -Recurse -Force
}
if (Test-Path $ZipFile) {
    Remove-Item $ZipFile -Force
}
New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null

# 3. Biên dịch và Publish ứng dụng
Write-Host "`n[STEP 3/4] Đang biên dịch Release win-x64 (SelfContained=$SelfContained)..." -ForegroundColor Yellow
$publishArgs = @(
    "publish", $ProjectFile,
    "-c", "Release",
    "-r", "win-x64",
    "-o", $PublishDir,
    "--nologo"
)

if ($SelfContained) {
    $publishArgs += "--self-contained", "true"
} else {
    $publishArgs += "--self-contained", "false"
}

dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "Biên dịch dotnet publish thất bại!"
    exit 1
}

# 4. Kiểm tra và bổ sung các thư mục tài nguyên mặc định
Write-Host "`n[STEP 4/4] Kiểm tra các tệp phân phối và tài nguyên..." -ForegroundColor Yellow

$ProfilesSrc = Join-Path $WorkspaceRoot "TranslateBot\profiles"
$ProfilesDest = Join-Path $PublishDir "profiles"
if (Test-Path $ProfilesSrc) {
    Copy-Item $ProfilesSrc $PublishDir -Recurse -Force
    Write-Host "-> Đã đóng gói thư mục profiles/" -ForegroundColor Green
}

$TessdataDest = Join-Path $PublishDir "tessdata"
if (-not (Test-Path $TessdataDest)) {
    New-Item -ItemType Directory -Path $TessdataDest -Force | Out-Null
}
$TessdataReadmeSrc = Join-Path $WorkspaceRoot "TranslateBot\tessdata\README.txt"
if (Test-Path $TessdataReadmeSrc) {
    Copy-Item $TessdataReadmeSrc $TessdataDest -Force
}

# Nén thư mục phát hành thành ZIP
Write-Host "-> Đang tạo tệp nén phân phối: $ZipFile ..." -ForegroundColor Cyan
Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipFile -Force

$sw.Stop()
$totalSizeMb = [math]::Round(((Get-ChildItem $PublishDir -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB), 2)
$zipSizeMb = [math]::Round(((Get-Item $ZipFile).Length / 1MB), 2)

Write-Host "`n================================================================" -ForegroundColor Green
Write-Host "          HOÀN TẤT ĐÓNG GÓI TBCO 3.0 PRODUCTION!                " -ForegroundColor Green
Write-Host "================================================================" -ForegroundColor Green
Write-Host " Thư mục xuất bản : $PublishDir" -ForegroundColor White
Write-Host " Tệp nén phân phối: $ZipFile" -ForegroundColor White
Write-Host " Dung lượng thư mục: $totalSizeMb MB" -ForegroundColor White
Write-Host " Dung lượng tệp ZIP: $zipSizeMb MB" -ForegroundColor White
Write-Host " Thời gian hoàn tất: $([math]::Round($sw.Elapsed.TotalSeconds, 1)) giây" -ForegroundColor White
Write-Host "================================================================" -ForegroundColor Green
