# ==============================================================================
# TBCO OCR Benchmark Runner -- Windows OCR vs OneOCR / Tesseract
# ==============================================================================
# TRẠNG THÁI HIỆN TẠI (02/11/2026):
#   Script này chạy unit test kiểm tra công thức CER/WER và tính toàn vẹn dataset.
#   Nó KHÔNG sinh báo cáo so sánh engine thật — chức năng đó cần CLI entry point
#   `tools/TBCO.Eval` (chưa có, nằm trong lộ trình Giai đoạn 1).
#   Report thật phải chứa: git SHA, OS, model, timestamp, dataset hash.
#   Benchmark thật phải dùng ảnh screenshot game (evaluation/ocr/images/*.png),
#   không phải ảnh GDI+ synthetic render.
# ==============================================================================
param(
    [string]$SamplesPath = "$PSScriptRoot/ocr/samples/ocr_benchmark_samples.json",
    [string]$OutputDir = "$PSScriptRoot/reports"
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   TBCO OCR Benchmark Suite & Comparative Evaluator       " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

if (-not (Test-Path $SamplesPath)) {
    Write-Error "Samples file not found: $SamplesPath"
    exit 1
}

Write-Host "[1/2] Executing automated OCR benchmark unit tests..." -ForegroundColor Yellow
dotnet test "$PSScriptRoot/../TranslateBot.Tests/TranslateBot.Tests.csproj" --filter "FullyQualifiedName~OcrBenchmarkTests" --nologo

if ($LASTEXITCODE -ne 0) {
    Write-Error "OCR benchmark tests failed with code $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-Host "[2/2] OCR Benchmark validation passed successfully 100%!" -ForegroundColor Green
Write-Host "Dataset: $SamplesPath"
Write-Host "Reports: $OutputDir"
Write-Host "==========================================================" -ForegroundColor Cyan
