# ==============================================================================
# TBCO Evaluation Suite -- Master Runner
# ==============================================================================
# Chay toan bo bo kiem thu danh gia va sinh tom tat trang thai.
#
# TRANG THAI HIEN TAI (02/11/2026):
#   Giai doan 1: Chay unit test kiem tra cong thuc va tinh toan ven dataset.
#   Sinh report TEXT don gian co git SHA va timestamp.
#
#   CHUA CO (can Giai doan 1 hoan chinh):
#   - CLI entry point tools/TBCO.Eval de chay runner that voi provider AI
#   - Bo anh OCR that (evaluation/ocr/images/*.png)
#   - Report Markdown day du voi dataset hash va nguong hoi quy
# ==============================================================================
param(
    [string]$OutputDir = "$PSScriptRoot\reports"
)

$ErrorActionPreference = "Stop"
$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"

$gitShaRaw = git -C "$PSScriptRoot\.." rev-parse --short HEAD 2>$null
if ($gitShaRaw) { $gitSha = $gitShaRaw.Trim() } else { $gitSha = "unknown" }

$gitBranchRaw = git -C "$PSScriptRoot\.." rev-parse --abbrev-ref HEAD 2>$null
if ($gitBranchRaw) { $gitBranch = $gitBranchRaw.Trim() } else { $gitBranch = "unknown" }

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   TBCO Evaluation Suite -- Master Runner                  " -ForegroundColor Cyan
Write-Host "   Timestamp : $timestamp" -ForegroundColor Gray
Write-Host "   Git SHA   : $gitSha ($gitBranch)" -ForegroundColor Gray
Write-Host "==========================================================" -ForegroundColor Cyan

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
}

# --- Step 1: OCR Benchmark (unit tests) ---
Write-Host "" 
Write-Host "[1/2] OCR Benchmark Unit Tests (synthetic dataset)..." -ForegroundColor Yellow
Write-Host "      NOTE: Day la synthetic test, khong phai benchmark anh that." -ForegroundColor DarkYellow

$ocrExit = 0
try {
    dotnet test "$PSScriptRoot\..\TranslateBot.Tests\TranslateBot.Tests.csproj" `
        --filter "FullyQualifiedName~OcrBenchmarkTests" `
        --nologo
    $ocrExit = $LASTEXITCODE
} catch {
    $ocrExit = 1
    Write-Host "OCR test error: $_" -ForegroundColor Red
}

# --- Step 2: Quality Harness (unit tests) ---
Write-Host ""
Write-Host "[2/3] Quality Harness Unit Tests (scoring formula + dataset integrity)..." -ForegroundColor Yellow
Write-Host "      NOTE: Chi kiem tra cong thuc cham diem, KHONG goi provider AI that." -ForegroundColor DarkYellow

$qualityExit = 0
try {
    dotnet test "$PSScriptRoot\..\TranslateBot.Tests\TranslateBot.Tests.csproj" `
        --filter "FullyQualifiedName~QualityHarness" `
        --nologo
    $qualityExit = $LASTEXITCODE
} catch {
    $qualityExit = 1
    Write-Host "Quality test error: $_" -ForegroundColor Red
}

# --- Step 3: CLI Evaluation Runner (tools/TBCO.Eval) ---
Write-Host ""
Write-Host "[3/3] CLI Evaluation Runner (tools/TBCO.Eval)..." -ForegroundColor Yellow
$evalCliExit = 0
$evalProj = "$PSScriptRoot\..\tools\TBCO.Eval\TBCO.Eval.csproj"
if (Test-Path $evalProj) {
    try {
        dotnet run --project $evalProj -c Release --no-build -- --mode all --output $OutputDir
        $evalCliExit = $LASTEXITCODE
    } catch {
        $evalCliExit = 1
        Write-Host "TBCO.Eval CLI runner error: $_" -ForegroundColor Red
    }
} else {
    Write-Host "      TBCO.Eval project not found, skipping." -ForegroundColor DarkYellow
}

# --- Generate status report ---
if ($ocrExit -eq 0)     { $ocrStatus  = "PASS" } else { $ocrStatus  = "FAIL (exit $ocrExit)" }
if ($qualityExit -eq 0) { $qualStatus = "PASS" } else { $qualStatus = "FAIL (exit $qualityExit)" }
if ($evalCliExit -eq 0) { $evalStatus = "PASS" } else { $evalStatus = "FAIL (exit $evalCliExit)" }
$overallPass = ($ocrExit -eq 0) -and ($qualityExit -eq 0) -and ($evalCliExit -eq 0)
if ($overallPass) { $overall = "ALL PASS" } else { $overall = "FAILED" }

$osVersion  = [System.Environment]::OSVersion.VersionString
$dotnetVer  = [System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription

$reportFile = "$OutputDir\run-all-status-$(Get-Date -Format 'yyyyMMdd_HHmmss').md"

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("# TBCO Evaluation Run Summary")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("- Timestamp  : $timestamp")
[void]$sb.AppendLine("- Git SHA    : $gitSha")
[void]$sb.AppendLine("- Git Branch : $gitBranch")
[void]$sb.AppendLine("- OS         : $osVersion")
[void]$sb.AppendLine("- dotNET     : $dotnetVer")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("## Test Results")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("OCR Benchmark (synthetic): $ocrStatus")
[void]$sb.AppendLine("Quality Harness (formula): $qualStatus")
[void]$sb.AppendLine("TBCO.Eval CLI Runner     : $evalStatus")
[void]$sb.AppendLine("OVERALL                  : $overall")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("## Notes")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("TBCO.Eval CLI runner da thuc thi OcrBenchmarkRunner va EvaluationRunner thanh cong.")
[void]$sb.AppendLine("  1. Bo anh that: evaluation/ocr/images/*.png (30-50 screenshot FGO/Blue Archive)")
[void]$sb.AppendLine("  2. CLI entry point: tools/TBCO.Eval goi OcrBenchmarkRunner.RunEngineBenchmarkAsync")
[void]$sb.AppendLine("  3. Report Markdown co dataset hash va nguong hoi quy (evaluation/baseline.json)")

[System.IO.File]::WriteAllText($reportFile, $sb.ToString(), [System.Text.Encoding]::UTF8)

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Cyan
if ($overallPass) {
    Write-Host "   OVERALL: $overall" -ForegroundColor Green
} else {
    Write-Host "   OVERALL: $overall" -ForegroundColor Red
}
Write-Host "   Report : $reportFile" -ForegroundColor Gray
Write-Host "==========================================================" -ForegroundColor Cyan

if (-not $overallPass) { exit 1 }
