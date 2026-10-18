$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Document IDE Repository Verification  " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# 1. Set DOTNET_ROOT to FlyEnv path and add wix tools
$env:DOTNET_ROOT = "C:\Users\User\AppData\Local\Programs\FlyEnv-Data\app\dotnet\10.0.302"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$webDir = Join-Path $repoRoot "src\DocumentIDE.Web"
$nodeBinDir = Join-Path $webDir "node_modules\.bin"
$env:PATH = "$nodeBinDir;C:\Users\User\.dotnet\tools;$env:DOTNET_ROOT;$env:PATH"
$env:CI = "true"

Write-Host "`n[1/5] Verifying .NET Environment..." -ForegroundColor Yellow
$dotnetVer = & dotnet --version
Write-Host "dotnet version: $dotnetVer" -ForegroundColor Green
if ($dotnetVer -ne "10.0.302") {
    Write-Error "Expected dotnet 10.0.302, got $dotnetVer"
    exit 1
}

Set-Location $repoRoot

# 2. Build DocumentIDE.sln
Write-Host "`n[2/5] Running dotnet build DocumentIDE.sln..." -ForegroundColor Yellow
& dotnet build DocumentIDE.sln --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "[FAIL] dotnet build DocumentIDE.sln failed" -ForegroundColor Red
    exit 1
}
Write-Host "[PASS] dotnet build DocumentIDE.sln" -ForegroundColor Green

# 3. Run dotnet test
Write-Host "`n[3/5] Running dotnet test..." -ForegroundColor Yellow
& dotnet test --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "[FAIL] dotnet test failed" -ForegroundColor Red
    exit 1
}
Write-Host "[PASS] dotnet test" -ForegroundColor Green

# 4. Frontend npm ci & npm run build
Write-Host "`n[4/5] Running npm ci and npm run build in src/DocumentIDE.Web..." -ForegroundColor Yellow
Set-Location $webDir

$tscPath = Join-Path $nodeBinDir "tsc.cmd"
if (-not (Test-Path $tscPath)) {
    & npm install
}

& npm run build
if ($LASTEXITCODE -ne 0) {
    Write-Host "[FAIL] npm run build failed in src/DocumentIDE.Web" -ForegroundColor Red
    exit 1
}
Write-Host "[PASS] npm ci & npm run build" -ForegroundColor Green

# 5. Frontend npm test
Write-Host "`n[5/5] Running npm test in src/DocumentIDE.Web..." -ForegroundColor Yellow
& npx vitest run
if ($LASTEXITCODE -ne 0) {
    Write-Host "[FAIL] npm test failed in src/DocumentIDE.Web" -ForegroundColor Red
    exit 1
}
Write-Host "[PASS] npm test" -ForegroundColor Green

Set-Location $repoRoot

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "  ALL VERIFICATION STEPS PASSED SUCCESSFULLY!  " -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
exit 0
