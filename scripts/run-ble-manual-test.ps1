# Run headed Playwright test: select ESP32-S3-WROOM when the Bluetooth dialog appears.
$ErrorActionPreference = "Stop"
$slnDir = Split-Path $PSScriptRoot -Parent
$testProj = Join-Path $slnDir "NanoFrameTest1.Tests\NanoFrameTest1.Tests.csproj"
if (-not (Test-Path $testProj)) { throw "Tests not found: $testProj" }
Write-Host "Running BLE manual test (Category=BLEManual)…" -ForegroundColor Cyan
Write-Host ">>> When Chrome opens, click Connect and select your ESP32-S3-WROOM." -ForegroundColor Yellow
dotnet test $testProj --filter "Category=BLEManual" @args
