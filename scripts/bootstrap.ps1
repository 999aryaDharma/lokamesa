$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "   Document IDE Repository Bootstrap    " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# 1. Environment Setup
$env:DOTNET_ROOT = "C:\Users\User\AppData\Local\Programs\FlyEnv-Data\app\dotnet\10.0.302"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

# 2. Check Prerequisites
Write-Host "`n[1/4] Checking .NET SDK..." -ForegroundColor Yellow
$dotnetVer = & dotnet --version
Write-Host "dotnet version: $dotnetVer" -ForegroundColor Green
if ($dotnetVer -ne "10.0.302") {
    Write-Error "Expected dotnet 10.0.302, got $dotnetVer"
    exit 1
}

Write-Host "`n[2/4] Checking Node.js & npm..." -ForegroundColor Yellow
$nodeVer = & node --version
$npmVer = & npm --version
Write-Host "node version: $nodeVer | npm version: $npmVer" -ForegroundColor Green

# 3. Restore .NET solution dependencies
Write-Host "`n[3/4] Restoring .NET solution dependencies..." -ForegroundColor Yellow
& dotnet restore DocumentIDE.sln --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "[FAIL] dotnet restore failed" -ForegroundColor Red
    exit 1
}
Write-Host "[PASS] dotnet restore DocumentIDE.sln" -ForegroundColor Green

# 4. Install Frontend dependencies
Write-Host "`n[4/4] Installing frontend dependencies (npm ci)..." -ForegroundColor Yellow
$webDir = Join-Path $repoRoot "src\DocumentIDE.Web"
Set-Location $webDir
& npm ci
if ($LASTEXITCODE -ne 0) {
    Write-Host "[FAIL] npm ci failed in src/DocumentIDE.Web" -ForegroundColor Red
    exit 1
}
Write-Host "[PASS] npm ci in src/DocumentIDE.Web" -ForegroundColor Green

Set-Location $repoRoot

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "  BOOTSTRAP COMPLETED SUCCESSFULLY!     " -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
exit 0
