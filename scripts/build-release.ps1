$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Document IDE Release Artifact Build   " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# 1. Environment Setup
$env:DOTNET_ROOT = "C:\Users\User\AppData\Local\Programs\FlyEnv-Data\app\dotnet\10.0.302"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$webDir = Join-Path $repoRoot "src\DocumentIDE.Web"
$nodeBinDir = Join-Path $webDir "node_modules\.bin"
$env:PATH = "$nodeBinDir;C:\Users\User\.dotnet\tools;$env:DOTNET_ROOT;$env:PATH"

Set-Location $repoRoot

# 2. Build Frontend Production Web Bundle
Write-Host "`n[1/6] Building production React bundle (npm run build)..." -ForegroundColor Yellow
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
Write-Host "[PASS] Production web bundle built successfully" -ForegroundColor Green

Set-Location $repoRoot

# 3. Copy production static dist assets into WPF desktop wwwroot folder
Write-Host "`n[2/6] Embedding static web assets into DocumentIDE.Desktop/wwwroot..." -ForegroundColor Yellow
$distDir = Join-Path $webDir "dist"
$desktopWwwroot = Join-Path $repoRoot "src\DocumentIDE.Desktop\wwwroot"

if (Test-Path $desktopWwwroot) {
    Remove-Item -Path $desktopWwwroot -Recurse -Force
}
New-Item -ItemType Directory -Path $desktopWwwroot | Out-Null
Copy-Item -Path "$distDir\*" -Destination $desktopWwwroot -Recurse -Force
Write-Host "[PASS] Static web assets copied to $desktopWwwroot" -ForegroundColor Green

# 4. Clean artifacts output directories
Write-Host "`n[3/6] Preparing artifacts output directory..." -ForegroundColor Yellow
$artifactsDesktopDir = Join-Path $repoRoot "artifacts\desktop"
$artifactsInstallerDir = Join-Path $repoRoot "artifacts\installer"

if (Test-Path $artifactsDesktopDir) {
    Remove-Item -Path $artifactsDesktopDir -Recurse -Force
}
New-Item -ItemType Directory -Path $artifactsDesktopDir | Out-Null

if (Test-Path $artifactsInstallerDir) {
    Remove-Item -Path $artifactsInstallerDir -Recurse -Force
}
New-Item -ItemType Directory -Path $artifactsInstallerDir | Out-Null

# 5. Publish WPF Desktop Application & ShareHost win-x64
Write-Host "`n[4/6] Publishing DocumentIDE.Desktop Release build (win-x64)..." -ForegroundColor Yellow
& dotnet publish src/DocumentIDE.Desktop/DocumentIDE.Desktop.csproj -c Release -r win-x64 --self-contained true -o $artifactsDesktopDir -m:1 --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "[FAIL] dotnet publish DocumentIDE.Desktop failed" -ForegroundColor Red
    exit 1
}

Write-Host "`nPublishing DocumentIDE.ShareHost Release build (win-x64)..." -ForegroundColor Yellow
& dotnet publish src/DocumentIDE.ShareHost/DocumentIDE.ShareHost.csproj -c Release -r win-x64 --self-contained true -o $artifactsDesktopDir -m:1 --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "[FAIL] dotnet publish DocumentIDE.ShareHost failed" -ForegroundColor Red
    exit 1
}

$artifactsWwwroot = Join-Path $artifactsDesktopDir "wwwroot"
if (-not (Test-Path $artifactsWwwroot)) {
    New-Item -ItemType Directory -Path $artifactsWwwroot | Out-Null
}
Copy-Item -Path "$distDir\*" -Destination $artifactsWwwroot -Recurse -Force

# 6. Build Windows Installer Package via WiX v7
Write-Host "`n[5/6] Building Windows Installer Package (DocumentIDE.wxs)..." -ForegroundColor Yellow
$wxsPath = Join-Path $repoRoot "installer\DocumentIDE.wxs"
$msiOutputPath = Join-Path $artifactsInstallerDir "DocumentIDE-Setup.msi"

& wix build $wxsPath -arch x64 -d "SourceDir=$artifactsDesktopDir" -o $msiOutputPath
if ($LASTEXITCODE -ne 0) {
    Write-Host "[FAIL] WiX installer build failed" -ForegroundColor Red
    exit 1
}
Write-Host "[PASS] WiX Windows Installer package created successfully: $msiOutputPath" -ForegroundColor Green

# 7. Verify Artifact Completeness
Write-Host "`n[6/6] Verifying release artifact completeness..." -ForegroundColor Yellow
$exeDesktopPath = Join-Path $artifactsDesktopDir "DocumentIDE.Desktop.exe"
$exeShareHostPath = Join-Path $artifactsDesktopDir "DocumentIDE.ShareHost.exe"
$indexHtmlPath = Join-Path $artifactsDesktopDir "wwwroot\index.html"

if (-not (Test-Path $exeDesktopPath)) {
    Write-Host "[FAIL] Release artifact desktop executable missing: $exeDesktopPath" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $exeShareHostPath)) {
    Write-Host "[FAIL] Release artifact ShareHost executable missing: $exeShareHostPath" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $indexHtmlPath)) {
    Write-Host "[FAIL] Embedded web bundle missing in release artifact: $indexHtmlPath" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $msiOutputPath)) {
    Write-Host "[FAIL] Release installer MSI missing: $msiOutputPath" -ForegroundColor Red
    exit 1
}

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "  RELEASE BUILD COMPLETED SUCCESSFULLY!  " -ForegroundColor Green
Write-Host "  Desktop Artifacts: $artifactsDesktopDir " -ForegroundColor Green
Write-Host "  MSI Installer: $msiOutputPath " -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
exit 0
