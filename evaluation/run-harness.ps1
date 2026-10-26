param(
    [string]$DatasetPath = "$PSScriptRoot\translation\samples\evaluation_samples.json",
    [string]$OutputDir = "$PSScriptRoot\reports",
    [string]$RunName = "quality_run_" + (Get-Date -Format "yyyyMMdd_HHmmss")
)

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   TBCO Translation Quality Harness -- Replay Runner      " -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan

if (-not (Test-Path $DatasetPath)) {
    Write-Error "Dataset file not found: $DatasetPath"
    exit 1
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

Write-Host "[1/2] Executing automated scoring and harness tests..." -ForegroundColor Green
dotnet test "$PSScriptRoot\..\TranslateBot.Tests\TranslateBot.Tests.csproj" --filter "FullyQualifiedName~QualityHarness" --logger "console;verbosity=normal"

if ($LASTEXITCODE -ne 0) {
    Write-Error "QualityHarness tests failed!"
    exit $LASTEXITCODE
}

Write-Host "[2/2] Harness validation passed successfully 100%!" -ForegroundColor Green
Write-Host "Dataset: $DatasetPath" -ForegroundColor Gray
Write-Host "Reports: $OutputDir" -ForegroundColor Gray
Write-Host "==========================================================" -ForegroundColor Cyan
