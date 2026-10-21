param(
    [Parameter(Mandatory = $true)][string]$RepoRoot,
    [Parameter(Mandatory = $true)][string]$OutputDir,
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Sha = ""
)

# DEV-UPD — the one publish contract shared by the release build and the local
# auto-updater. Produces a self-contained win-x64 payload plus build-info.json
# so the installed app can report exactly which commit it is running.
$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path $RepoRoot).Path
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$OutputDir = (Resolve-Path $OutputDir).Path

$webDir = Join-Path $RepoRoot "src\DocumentIDE.Web"
$desktopProject = Join-Path $RepoRoot "src\DocumentIDE.Desktop\DocumentIDE.Desktop.csproj"
$shareHostProject = Join-Path $RepoRoot "src\DocumentIDE.ShareHost\DocumentIDE.ShareHost.csproj"

if (-not (Test-Path (Join-Path $webDir "package.json"))) {
    throw "Web package not found under $webDir"
}

if (-not (Test-Path (Join-Path $webDir "node_modules\.bin\tsc.cmd"))) {
    Write-Host "[publish] installing web dependencies..." -ForegroundColor Yellow
    Push-Location $webDir
    try {
        & npm install
        if ($LASTEXITCODE -ne 0) { throw "npm install failed" }
    }
    finally {
        Pop-Location
    }
}

Write-Host "[publish] npm run build" -ForegroundColor Yellow
Push-Location $webDir
try {
    & npm run build
    if ($LASTEXITCODE -ne 0) { throw "npm run build failed" }
}
finally {
    Pop-Location
}

$distDir = Join-Path $webDir "dist"
if (-not (Test-Path (Join-Path $distDir "index.html"))) {
    throw "Production web bundle missing at $distDir\index.html"
}

# Keep the gitignored project wwwroot current for `dotnet run` from the repo;
# the published payload gets its own copy below.
$desktopWwwroot = Join-Path $RepoRoot "src\DocumentIDE.Desktop\wwwroot"
if (Test-Path $desktopWwwroot) {
    Remove-Item -Path $desktopWwwroot -Recurse -Force
}
New-Item -ItemType Directory -Path $desktopWwwroot -Force | Out-Null
Copy-Item -Path (Join-Path $distDir "*") -Destination $desktopWwwroot -Recurse -Force

Write-Host "[publish] dotnet publish DocumentIDE.Desktop (win-x64)" -ForegroundColor Yellow
& dotnet publish $desktopProject -c Release -r win-x64 --self-contained true -o $OutputDir -m:1 --nologo "-p:Version=$Version"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish DocumentIDE.Desktop failed" }

Write-Host "[publish] dotnet publish DocumentIDE.ShareHost (win-x64)" -ForegroundColor Yellow
& dotnet publish $shareHostProject -c Release -r win-x64 --self-contained true -o $OutputDir -m:1 --nologo "-p:Version=$Version"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish DocumentIDE.ShareHost failed" }

$payloadWwwroot = Join-Path $OutputDir "wwwroot"
if (Test-Path $payloadWwwroot) {
    Remove-Item -Path $payloadWwwroot -Recurse -Force
}
New-Item -ItemType Directory -Path $payloadWwwroot -Force | Out-Null
Copy-Item -Path (Join-Path $distDir "*") -Destination $payloadWwwroot -Recurse -Force

if ([string]::IsNullOrWhiteSpace($Sha)) {
    Push-Location $RepoRoot
    try {
        $Sha = (& git rev-parse HEAD).Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($Sha)) {
            throw "git rev-parse HEAD failed"
        }
    }
    finally {
        Pop-Location
    }
}

$buildInfo = [ordered]@{ version = $Version; sha = $Sha } | ConvertTo-Json -Compress
$buildInfoPath = Join-Path $OutputDir "build-info.json"
[System.IO.File]::WriteAllText($buildInfoPath, $buildInfo, (New-Object System.Text.UTF8Encoding($false)))

$required = @(
    "DocumentIDE.Desktop.exe",
    "DocumentIDE.ShareHost.exe",
    "build-info.json",
    "wwwroot\index.html"
)
foreach ($relative in $required) {
    $full = Join-Path $OutputDir $relative
    if (-not (Test-Path $full)) {
        throw "Payload artifact missing: $full"
    }
}

Write-Host "[publish] payload ready: $OutputDir (version $Version, sha $Sha)" -ForegroundColor Green
exit 0
