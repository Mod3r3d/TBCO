# ========================================================================
# Script tu dong dong goi TBCO thanh goi phat hanh (Tuong thich Smart App Control)
# ========================================================================

$ErrorActionPreference = "Stop"

$workspaceRoot = $PSScriptRoot
$projectPath = Join-Path $workspaceRoot "TranslateBot\TranslateBot.csproj"
$releaseDir = Join-Path $workspaceRoot "release"
$packageDir = Join-Path $releaseDir "TBCO-win-x64"
$appDir = Join-Path $packageDir "app"
$zipPath = Join-Path $releaseDir "TBCO-win-x64.zip"

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "       BAT DAU DONG GOI TBCO CHO WINDOWS (X64)" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

# 1. Don dep thu muc release cu
if (Test-Path $packageDir) { Remove-Item -Recurse -Force $packageDir }
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
if (Test-Path (Join-Path $releaseDir "TBCO.exe")) { Remove-Item -Force (Join-Path $releaseDir "TBCO.exe") }

# 2. Bien dich va xuat ban dang Self-Contained folder vao thu muc app/
Write-Host "`n[1/4] Dang bien dich va dong goi bang .NET 8 SDK vao app/..." -ForegroundColor Yellow
dotnet publish $projectPath -c Release -o $appDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "[LOI] Qua trinh dong goi that bai!" -ForegroundColor Red
    exit 1
}

# 3. Don dep file debug .pdb
Write-Host "[2/4] Dang toi uu hoa tep..." -ForegroundColor Yellow
Get-ChildItem -Path $appDir -Filter "*.pdb" | Remove-Item -Force -ErrorAction SilentlyContinue

# 4. Tao Trinh khoi chay TBCO.exe tai thu muc goc (Root Launcher co Icon dep)
Write-Host "[3/4] Dang tao trinh khoi chay TBCO.exe tai thu muc goc..." -ForegroundColor Yellow

$launcherSrc = @"
using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

class Program
{
    [STAThread]
    static void Main()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string targetExe = Path.Combine(baseDir, "app", "TBCO.exe");
        string workingDir = Path.Combine(baseDir, "app");

        if (!File.Exists(targetExe))
        {
            MessageBox.Show("Khong tim thay: app\\TBCO.exe! Vui long khong xoa hoac doi ten thu muc 'app'.", "TBCO Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = targetExe,
            WorkingDirectory = workingDir,
            UseShellExecute = true
        };

        try
        {
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Khong the khoi chay TBCO: " + ex.Message, "TBCO Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
"@

$tempCs = Join-Path $env:TEMP "TBCO_RootLauncher.cs"
[System.IO.File]::WriteAllText($tempCs, $launcherSrc)
$iconPath = Join-Path $workspaceRoot "TranslateBot\TBCO-Icon.ico"
$rootExe = Join-Path $packageDir "TBCO.exe"

$cscPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (Test-Path $cscPath) {
    & $cscPath /target:winexe /win32icon:$iconPath /out:$rootExe $tempCs | Out-Null
} else {
    Copy-Item (Join-Path $appDir "TBCO.exe") $rootExe -Force
}
Remove-Item -Force $tempCs -ErrorAction SilentlyContinue

# Tao file khoi chay nhanh .bat du phong
$batContent = "@echo off`r`nstart `"`" `"%~dp0app\TBCO.exe`""
[System.IO.File]::WriteAllText((Join-Path $packageDir "KHOI_CHAY_TBCO.bat"), $batContent)

# Copy file huong dan su dung ra thu muc goc
$readmeFile = Join-Path $workspaceRoot "HUONG_DAN_SU_DUNG.txt"
if (Test-Path $readmeFile) {
    Copy-Item $readmeFile $packageDir -Force
}

# 5. Nen thanh file ZIP (giu nguyen thu muc goc TBCO-win-x64 de giai nen gon gang)
Write-Host "[4/4] Dang tao tep ZIP phat hanh..." -ForegroundColor Yellow
Compress-Archive -Path $packageDir -DestinationPath $zipPath -Force

$zipSizeMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)

Write-Host "`n============================================================" -ForegroundColor Green
Write-Host "   DONG GOI TBCO THANH CONG!" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
Write-Host ("- File ZIP tron goi phat hanh: " + $zipPath + " [" + $zipSizeMB + " MB]") -ForegroundColor White
Write-Host ("- Thu muc san pham da san sang: " + $packageDir) -ForegroundColor White
Write-Host "- File khoi chay noi bat:       $(Join-Path $packageDir 'TBCO.exe')" -ForegroundColor Yellow
Write-Host "============================================================`n" -ForegroundColor Green
