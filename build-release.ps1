# ========================================================================
# Script tu dong dong goi TBCO thanh goi phat hanh (Tuong thich Smart App Control)
# ========================================================================

$ErrorActionPreference = "Stop"

$workspaceRoot = $PSScriptRoot
$projectPath = Join-Path $workspaceRoot "TranslateBot\TranslateBot.csproj"
$releaseDir = Join-Path $workspaceRoot "release"
$packageDir = Join-Path $releaseDir "TBCO-win-x64"
$zipPath = Join-Path $releaseDir "TBCO-win-x64.zip"

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "       BAT DAU DONG GOI TBCO CHO WINDOWS (X64)" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

# 1. Don dep thu muc release cu
if (Test-Path $packageDir) { Remove-Item -Recurse -Force $packageDir }
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
if (Test-Path (Join-Path $releaseDir "TBCO.exe")) { Remove-Item -Force (Join-Path $releaseDir "TBCO.exe") }

# 2. Bien dich va xuat ban dang Self-Contained folder (tranh bi Smart App Control chan vi packed single-file)
Write-Host "`n[1/3] Dang bien dich va dong goi bang .NET 8 SDK..." -ForegroundColor Yellow
dotnet publish $projectPath -c Release -o $packageDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "[LOI] Qua trinh dong goi that bai!" -ForegroundColor Red
    exit 1
}

# 3. Don dep file debug .pdb va them huong dan
Write-Host "[2/3] Dang toi uu hoa tep va them huong dan..." -ForegroundColor Yellow
Get-ChildItem -Path $packageDir -Filter "*.pdb" | Remove-Item -Force -ErrorAction SilentlyContinue

$readmeFile = Join-Path $workspaceRoot "HUONG_DAN_SU_DUNG.txt"
if (Test-Path $readmeFile) {
    Copy-Item $readmeFile $packageDir -Force
}

# 4. Nen thanh file ZIP
Write-Host "[3/3] Dang tao tep ZIP phat hanh..." -ForegroundColor Yellow
Compress-Archive -Path "$packageDir\*" -DestinationPath $zipPath -Force

$zipSizeMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)

Write-Host "`n============================================================" -ForegroundColor Green
Write-Host "   DONG GOI TBCO THANH CONG!" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
Write-Host ("- File ZIP tron goi phat hanh: " + $zipPath + " [" + $zipSizeMB + " MB]") -ForegroundColor White
Write-Host ("- Thu muc san pham da san sang: " + $packageDir) -ForegroundColor White
Write-Host "- File khoi chay:               $(Join-Path $packageDir 'TBCO.exe')" -ForegroundColor White
Write-Host "============================================================`n" -ForegroundColor Green
