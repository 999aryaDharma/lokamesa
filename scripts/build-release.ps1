$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Document IDE Release Artifact Build   " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# 1. Environment Setup
$env:DOTNET_ROOT = "C:\Users\User\AppData\Local\Programs\FlyEnv-Data\app\dotnet\10.0.302"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

# 2. Build Frontend Production Web Bundle
Write-Host "`n[1/4] Building production React bundle (npm run build)..." -ForegroundColor Yellow
$webDir = Join-Path $repoRoot "src\DocumentIDE.Web"
Set-Location $webDir

& npm ci
if ($LASTEXITCODE -ne 0) {
    Write-Host "[FAIL] npm ci failed in src/DocumentIDE.Web" -ForegroundColor Red
    exit 1
}

& npm run build
if ($LASTEXITCODE -ne 0) {
    Write-Host "[FAIL] npm run build failed in src/DocumentIDE.Web" -ForegroundColor Red
    exit 1
}
Write-Host "[PASS] Production web bundle built successfully" -ForegroundColor Green

Set-Location $repoRoot

# 3. Copy production static dist assets into WPF desktop wwwroot folder
Write-Host "`n[2/4] Embedding static web assets into DocumentIDE.Desktop/wwwroot..." -ForegroundColor Yellow
$distDir = Join-Path $webDir "dist"
$desktopWwwroot = Join-Path $repoRoot "src\DocumentIDE.Desktop\wwwroot"

if (Test-Path $desktopWwwroot) {
    Remove-Item -Path $desktopWwwroot -Recurse -Force
}
New-Item -ItemType Directory -Path $desktopWwwroot | Out-Null
Copy-Item -Path "$distDir\*" -Destination $desktopWwwroot -Recurse -Force
Write-Host "[PASS] Static web assets copied to $desktopWwwroot" -ForegroundColor Green

# 4. Clean artifacts directory
Write-Host "`n[3/4] Preparing artifacts output directory..." -ForegroundColor Yellow
$artifactsDir = Join-Path $repoRoot "artifacts\desktop"
if (Test-Path $artifactsDir) {
    Remove-Item -Path $artifactsDir -Recurse -Force
}
New-Item -ItemType Directory -Path $artifactsDir | Out-Null

# 5. Publish WPF Desktop Application
Write-Host "`n[4/4] Publishing DocumentIDE.Desktop Release build..." -ForegroundColor Yellow
& dotnet publish src/DocumentIDE.Desktop/DocumentIDE.Desktop.csproj -c Release -r win-x64 --self-contained true -o $artifactsDir --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "[FAIL] dotnet publish failed" -ForegroundColor Red
    exit 1
}

$artifactsWwwroot = Join-Path $artifactsDir "wwwroot"
if (-not (Test-Path $artifactsWwwroot)) {
    New-Item -ItemType Directory -Path $artifactsWwwroot | Out-Null
}
Copy-Item -Path "$distDir\*" -Destination $artifactsWwwroot -Recurse -Force

# 6. Verify Artifact Completeness
$exePath = Join-Path $artifactsDir "DocumentIDE.Desktop.exe"
$indexHtmlPath = Join-Path $artifactsDir "wwwroot\index.html"

if (-not (Test-Path $exePath)) {
    Write-Host "[FAIL] Release artifact executable missing: $exePath" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $indexHtmlPath)) {
    Write-Host "[FAIL] Embedded web bundle missing in release artifact: $indexHtmlPath" -ForegroundColor Red
    exit 1
}

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "  RELEASE BUILD COMPLETED SUCCESSFULLY!  " -ForegroundColor Green
Write-Host "  Artifacts published to: $artifactsDir " -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
exit 0
