$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Document IDE Release Artifact Build   " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# 1. Environment setup
$env:DOTNET_ROOT = "C:\Users\User\AppData\Local\Programs\FlyEnv-Data\app\dotnet\10.0.302"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$webDir = Join-Path $repoRoot "src\DocumentIDE.Web"
$nodeBinDir = Join-Path $webDir "node_modules\.bin"
$env:PATH = "$nodeBinDir;C:\Users\User\.dotnet\tools;$env:DOTNET_ROOT;$env:PATH"

Set-Location $repoRoot

# 2. Prepare artifacts
Write-Host "`n[1/3] Preparing artifacts output directories..." -ForegroundColor Yellow
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

# 3. Publish the shared payload (web bundle + Desktop + ShareHost + build-info)
Write-Host "`n[2/3] Publishing the shared payload..." -ForegroundColor Yellow
& (Join-Path $PSScriptRoot "publish-payload.ps1") `
    -RepoRoot $repoRoot `
    -OutputDir $artifactsDesktopDir `
    -Version "1.0.2.0"
if ($LASTEXITCODE -ne 0) {
    Write-Host "[FAIL] publish-payload.ps1 failed" -ForegroundColor Red
    exit 1
}

# 4. Build Windows Installer Package via WiX v7
Write-Host "`nBuilding Windows Installer Package (DocumentIDE.wxs)..." -ForegroundColor Yellow
$wxsPath = Join-Path $repoRoot "installer\DocumentIDE.wxs"
$msiOutputPath = Join-Path $artifactsInstallerDir "DocumentIDE-Setup.msi"

& wix build $wxsPath -arch x64 -d "SourceDir=$artifactsDesktopDir" -o $msiOutputPath
if ($LASTEXITCODE -ne 0) {
    Write-Host "[FAIL] WiX installer build failed" -ForegroundColor Red
    exit 1
}
Write-Host "[PASS] WiX Windows Installer package created successfully: $msiOutputPath" -ForegroundColor Green

# 5. Verify artifact completeness
Write-Host "`n[3/3] Verifying release artifact completeness..." -ForegroundColor Yellow
$exeDesktopPath = Join-Path $artifactsDesktopDir "DocumentIDE.Desktop.exe"
$exeShareHostPath = Join-Path $artifactsDesktopDir "DocumentIDE.ShareHost.exe"
$buildInfoPath = Join-Path $artifactsDesktopDir "build-info.json"
$indexHtmlPath = Join-Path $artifactsDesktopDir "wwwroot\index.html"

$required = @($exeDesktopPath, $exeShareHostPath, $buildInfoPath, $indexHtmlPath, $msiOutputPath)
foreach ($path in $required) {
    if (-not (Test-Path $path)) {
        Write-Host "[FAIL] Release artifact missing: $path" -ForegroundColor Red
        exit 1
    }
}

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "  RELEASE BUILD COMPLETED SUCCESSFULLY!  " -ForegroundColor Green
Write-Host "  Desktop Artifacts: $artifactsDesktopDir " -ForegroundColor Green
Write-Host "  MSI Installer: $msiOutputPath " -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
exit 0
