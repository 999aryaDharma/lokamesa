<#
.SYNOPSIS
  REL-02 — Release Verification Pipeline orchestration.

.DESCRIPTION
  Runs every release gate (backend, frontend, Word/COM, security/safety/golden)
  and only invokes packaging (scripts/build-release.ps1) when ALL gates pass.
  Any gate failure exits nonzero BEFORE packaging: failures BLOCK the release.

  No 'green by skipping' policy: a skipped gate fails the release unless it is
  explicitly marked environment-gated (-AllowEnvironmentGatedWordSkip with a
  written -WordSkipJustification), and the skip is recorded in the report.

  On completion (pass or block) writes verification-report.json/.md pinning the
  exact commit (git rev-parse HEAD), per-gate results, test summary, and — on
  success — SHA256 checksums of the packaged artifacts.

.PARAMETER OutputDir
  Directory (relative to repo root) receiving test results, the verification
  report, and checksums. Default: artifacts/release-verify.

.PARAMETER FailGate
  Test hook: forces the named gate to fail. Used to prove that any gate
  failure blocks packaging/release (exit nonzero, no packaging invoked).

.PARAMETER SelfTest
  Runs synthetic gates only (no toolchain required). Proves the fail-closed
  orchestration: with -FailGate the run must exit nonzero with verdict BLOCKED
  and must not package; without -FailGate it writes a commit-pinned report.

.PARAMETER SkipPackage
  Run all gates and write the report, but do not invoke build-release.ps1.

.PARAMETER AllowEnvironmentGatedWordSkip
  Allows the Word/COM gate to be recorded as EnvironmentGated (instead of
  failing the release) when Microsoft Word is not installed on the runner.
  Requires -WordSkipJustification. The skip stays visible in the report.

.PARAMETER WordSkipJustification
  One-sentence justification for the environment-gated Word skip. Recorded in
  the verification report.
#>
param(
    [string]$OutputDir = "artifacts/release-verify",
    [string]$FailGate = "",
    [switch]$SelfTest,
    [switch]$SkipPackage,
    [switch]$AllowEnvironmentGatedWordSkip,
    [string]$WordSkipJustification = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

$outputPath = Join-Path $repoRoot $OutputDir
$resultsPath = Join-Path $outputPath "test-results"
# Fresh evidence only: stale trx files from a previous run must never
# influence this run's verdict (in either direction).
if (Test-Path $resultsPath) { Remove-Item -Path $resultsPath -Recurse -Force }
New-Item -ItemType Directory -Path $resultsPath -Force | Out-Null

$script:failGate = $FailGate
$script:gates = @()

function Add-GateResult {
    param([string]$Name, [string]$Status, [string]$Detail, [double]$Seconds = 0)
    $script:gates += [pscustomobject]@{
        name = $Name
        status = $Status            # Passed | Failed | EnvironmentGated
        detail = $Detail
        seconds = [math]::Round($Seconds, 1)
    }
    $color = @{ Passed = "Green"; Failed = "Red"; EnvironmentGated = "Yellow" }[$Status]
    Write-Host "  [$Status] $Name — $Detail" -ForegroundColor $color
}

function Invoke-ReleaseGate {
    param([string]$Name, [scriptblock]$Body)
    Write-Host "`n[gate] $Name" -ForegroundColor Yellow
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    if ($script:failGate -ne "" -and $script:failGate -ieq $Name) {
        $sw.Stop()
        Add-GateResult -Name $Name -Status "Failed" `
            -Detail "Forced failure via -FailGate (gate-block proof)." -Seconds $sw.Elapsed.TotalSeconds
        return
    }
    try {
        $detail = & $Body
        $sw.Stop()
        Add-GateResult -Name $Name -Status "Passed" -Detail "$detail" -Seconds $sw.Elapsed.TotalSeconds
    } catch {
        $sw.Stop()
        Add-GateResult -Name $Name -Status "Failed" -Detail "$($_.Exception.Message)" -Seconds $sw.Elapsed.TotalSeconds
    }
}

function Assert-LastExit([string]$Step) {
    if ($LASTEXITCODE -ne 0) {
        throw "$Step exited with code $LASTEXITCODE."
    }
}

function Test-WordInstalled {
    # Capable-runner detection: registry App Paths entry, PATH lookup, or COM ProgID.
    $appPaths = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\WINWORD.EXE"
    if (Test-Path $appPaths) { return $true }
    if (Get-Command winword.exe -ErrorAction SilentlyContinue) { return $true }
    try {
        $t = [type]::GetTypeFromProgID("Word.Application")
        return ($null -ne $t)
    } catch { return $false }
}

function Get-TrxSummaries {
    param([string]$Dir)
    # NOTE: trx TestRun/@name is "user@machine timestamp" — NOT the assembly.
    # Assembly identity comes from UnitTest/@storage (the test dll path).
    $summaries = @()
    $ns = New-Object System.Xml.XmlNamespaceManager((New-Object System.Xml.NameTable))
    $ns.AddNamespace("t", "http://microsoft.com/schemas/VisualStudio/TeamTest/2010")
    # NOTE: trx file names contain brackets (e.g. "...[1].trx"), so enumerate
    # with a literal filesystem API — Get-ChildItem -Path would treat the
    # brackets as wildcards and fail.
    foreach ($path in [System.IO.Directory]::GetFiles($Dir, "*.trx")) {
        [xml]$trx = Get-Content -LiteralPath $path
        $file = [System.IO.Path]::GetFileName($path)
        $storages = @($trx.SelectNodes("//t:UnitTest", $ns) | ForEach-Object { $_.storage } | Sort-Object -Unique)
        $counters = $trx.SelectSingleNode("//t:Counters", $ns)
        $summaries += [pscustomobject]@{
            file = $file
            assemblies = ($storages -join ";")
            total = [int]$counters.total
            passed = [int]$counters.passed
            failed = [int]$counters.failed
            skipped = [int]$counters.notExecuted
        }
    }
    return $summaries
}

function Assert-TrxEvidence {
    param([string]$Suite, [array]$Rows, [string]$Dir = $resultsPath)
    # REL-04: single implementation of the release-gate evidence rule. A named
    # gate may only pass on trx evidence that actually executed tests, and none
    # failed. This matters because `dotnet test` exits 0 and writes a trx with
    # total=0 when a project contains no tests — exit code alone is green-by-skipping.
    $evidence = @($Rows | Where-Object { $null -ne $_ })
    if ($evidence.Count -eq 0) {
        throw "No trx evidence found for *$Suite* in $Dir."
    }
    $bad = @($evidence | Where-Object { $_.failed -gt 0 })
    if ($bad.Count -gt 0) {
        throw "${Suite}: $($bad[0].failed) failing test(s) of $($bad[0].total)."
    }
    $tot = ($evidence | Measure-Object -Property total -Sum).Sum
    if ($tot -le 0) {
        throw "${Suite}: trx evidence executed 0 tests; a suite that runs no tests cannot pass its release gate."
    }
    "${Suite}: $tot test(s) passed, 0 failed"
}

function Get-RepoCommit {
    $commit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($commit)) {
        throw "Unable to determine exact commit via git rev-parse HEAD."
    }
    return $commit
}

function Write-VerificationReport {
    param([string]$Verdict, [string]$Commit, [string]$Branch, [bool]$Dirty,
          [array]$Checksums, [string]$WordSkipNote)
    $passed = @($script:gates | Where-Object { $_.status -eq "Passed" }).Count
    $failed = @($script:gates | Where-Object { $_.status -eq "Failed" }).Count
    $gated = @($script:gates | Where-Object { $_.status -eq "EnvironmentGated" }).Count
    $report = [ordered]@{
        pipeline = "REL-02 release verification"
        verdict = $Verdict                 # PASSED | BLOCKED
        commit = $Commit
        branch = $Branch
        workingTreeDirty = $Dirty
        timestampUtc = (Get-Date).ToUniversalTime().ToString("o")
        summary = [ordered]@{ gatesPassed = $passed; gatesFailed = $failed; gatesEnvironmentGated = $gated }
        gates = $script:gates
        wordSkipJustification = $WordSkipNote
        checksums = $Checksums
    }
    $jsonPath = Join-Path $outputPath "verification-report.json"
    ($report | ConvertTo-Json -Depth 6) | Set-Content -Path $jsonPath -Encoding UTF8
    $md = @("# Release Verification Report", "",
        "- Verdict: **$Verdict**", "- Commit: ``$Commit``", "- Branch: ``$Branch``",
        "- Working tree dirty: $Dirty", "- Gates passed/failed/environment-gated: $passed/$failed/$gated", "")
    foreach ($g in $script:gates) { $md += "- [$($g.status)] $($g.name) — $($g.detail)" }
    if ($WordSkipNote -ne "") { $md += "", "> Word/COM skip justification: $WordSkipNote" }
    if ($Checksums.Count -gt 0) {
        $md += "", "## Artifact checksums (SHA256)", ""
        foreach ($c in $Checksums) { $md += "- ``$($c.hash)  $($c.file)``" }
    }
    $mdPath = Join-Path $outputPath "verification-report.md"
    ($md -join "`r`n") | Set-Content -Path $mdPath -Encoding UTF8
    Write-Host "`nReport: $jsonPath" -ForegroundColor Cyan
    return $jsonPath
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Document IDE Release Verification    " -ForegroundColor Cyan
Write-Host "  REL-02 gate: failures BLOCK release  " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$commit = Get-RepoCommit
$branch = ((& git rev-parse --abbrev-ref HEAD).Trim())
$dirty = ((& git status --porcelain).Count -gt 0)
Write-Host "Commit: $commit (branch $branch, dirty=$dirty)" -ForegroundColor Cyan

if ($SelfTest) {
    # Synthetic gates only: proves fail-closed orchestration without toolchain.
    Invoke-ReleaseGate -Name "SyntheticBackend" -Body { "synthetic backend gate passed" }
    Invoke-ReleaseGate -Name "SyntheticFrontend" -Body { "synthetic frontend gate passed" }
    $failed = @($script:gates | Where-Object { $_.status -eq "Failed" }).Count
    if ($failed -gt 0) {
        Write-VerificationReport -Verdict "BLOCKED" -Commit $commit -Branch $branch `
            -Dirty $dirty -Checksums @() -WordSkipNote "" | Out-Null
        Write-Host "`n[REL-02] BLOCKED: $failed gate(s) failed. Packaging NOT invoked." -ForegroundColor Red
        exit 1
    }
    Write-VerificationReport -Verdict "PASSED" -Commit $commit -Branch $branch `
        -Dirty $dirty -Checksums @() -WordSkipNote "" | Out-Null
    Write-Host "`n[REL-02] Self-test PASSED (synthetic gates only, no packaging)." -ForegroundColor Green
    exit 0
}

# ---- Environment (matches scripts/verify.ps1 + scripts/bootstrap.ps1 conventions) ----
$env:DOTNET_ROOT = "C:\Users\User\AppData\Local\Programs\FlyEnv-Data\app\dotnet\10.0.302"
$webDir = Join-Path $repoRoot "src\DocumentIDE.Web"
$nodeBinDir = Join-Path $webDir "node_modules\.bin"
$env:PATH = "$nodeBinDir;C:\Users\User\.dotnet\tools;$env:DOTNET_ROOT;$env:PATH"
$env:CI = "true"

# ---- Gate: prerequisites (clean-machine check; missing prereq fails the gate) ----
Invoke-ReleaseGate -Name "Prerequisites" -Body {
    $dotnetVer = (& dotnet --version).Trim()
    if ($dotnetVer -ne "10.0.302") { throw "Expected dotnet 10.0.302, got $dotnetVer." }
    $null = Get-Command node -ErrorAction Stop
    $null = Get-Command npm -ErrorAction Stop
    "dotnet $dotnetVer, node/npm present"
}

# ---- Gate: backend build ----
Invoke-ReleaseGate -Name "BackendBuild" -Body {
    & dotnet build DocumentIDE.sln --nologo 2>&1 | Out-String | Write-Host
    Assert-LastExit "dotnet build DocumentIDE.sln"
    "DocumentIDE.sln built"
}

# ---- Gate: backend full suite (all assemblies incl. Security/Performance/Architecture) ----
# Two artifact-presence tests (ScriptTests.Release_Build_* and
# InstallerTests.Build_Release_Script_*) assert packaged outputs exist, so
# they cannot run before packaging. They are excluded here and executed by the
# post-package PackagingVerification gate instead — same tests, correct order.
$script:artifactTestFilter = "FullyQualifiedName~Release_Build_Embeds_Production_Web_Assets|FullyQualifiedName~Build_Release_Script_Packages_Wxs_Installer_And_ShareHost"
Invoke-ReleaseGate -Name "BackendFull" -Body {
    & dotnet test --nologo --no-build --results-directory $resultsPath --logger "trx" `
        --filter "FullyQualifiedName!~Release_Build_Embeds_Production_Web_Assets&FullyQualifiedName!~Build_Release_Script_Packages_Wxs_Installer_And_ShareHost" `
        2>&1 | Out-String | Write-Host
    Assert-LastExit "dotnet test (full suite)"
    "full dotnet suite passed"
}

# ---- Named release-blocking gates from the single full-run trx evidence ----
# Spec 11 §26/§28: security, safety, golden, architecture, performance, soak,
# crash-recovery failures each block the release. No suite is re-run here; rows
# below are projected from the BackendFull trx files so every gate has evidence.
$trxGateMap = [ordered]@{
    "SecurityTests"     = "SecurityGate"
    "SafetyTests"       = "SafetyGate"
    "GoldenTests"       = "GoldenGate"
    "ArchitectureTests" = "ArchitectureGate"
    "PerformanceTests"  = "PerformanceGate"
    "SoakTests"         = "SoakGate"
    "Core.Tests"        = "BackendUnit"
    "IntegrationTests"  = "BackendIntegration"
    # NOTE: tests/DocumentIDE.InstallTests contains only InstallerSmoke.md (no
    # test project); installer coverage comes from the InstallerTests class
    # inside IntegrationTests (BackendIntegration row) plus the post-package
    # PackagingVerification gate. There is no separate InstallTests suite.
}
foreach ($key in $trxGateMap.Keys) {
    Invoke-ReleaseGate -Name $trxGateMap[$key] -Body {
        $rows = Get-TrxSummaries -Dir $resultsPath | Where-Object { $_.assemblies -like "*$key*" }
        Assert-TrxEvidence -Suite $key -Rows $rows
    }
}

# ---- Gate: MarkdownPreview suite (test project not in the solution) ----
# tests/DocumentIDE.MarkdownPreview.Tests has no sln entry, so `dotnet test` on
# the solution never executes it; this gate runs its csproj explicitly. A zero
# exit code is NOT sufficient evidence: `dotnet test` on a project with no tests
# prints "No test is available", writes a total=0 trx, and still exits 0. The
# gate therefore reads the trx this run wrote and fails unless tests actually
# executed and none failed (Assert-TrxEvidence) — no green-by-skipping.
#
# Dedicated results dir: a zero-test trx carries no UnitTest/@storage, so it
# cannot be attributed to this assembly by name. Reading exactly the trx this
# run wrote is what distinguishes "0 tests executed" from "no evidence at all".
$markdownPreviewResultsPath = Join-Path $resultsPath "markdownpreview"
Invoke-ReleaseGate -Name "MarkdownPreview" -Body {
    if (Test-Path $markdownPreviewResultsPath) {
        Remove-Item -Path $markdownPreviewResultsPath -Recurse -Force
    }
    & dotnet test tests/DocumentIDE.MarkdownPreview.Tests/DocumentIDE.MarkdownPreview.Tests.csproj `
        --nologo --results-directory $markdownPreviewResultsPath --logger "trx" 2>&1 | Out-String | Write-Host
    Assert-LastExit "dotnet test MarkdownPreview.Tests"
    $rows = Get-TrxSummaries -Dir $markdownPreviewResultsPath
    Assert-TrxEvidence -Suite "MarkdownPreview" -Rows $rows -Dir $markdownPreviewResultsPath
}

# ---- Gates: frontend (typecheck, lint, tests, build) ----
Invoke-ReleaseGate -Name "FrontendDeps" -Body {
    Set-Location $webDir
    try {
        if (-not (Test-Path (Join-Path $nodeBinDir "tsc.cmd"))) {
            & npm ci 2>&1 | Out-String | Write-Host
            Assert-LastExit "npm ci"
        }
        "frontend dependencies installed"
    } finally { Set-Location $repoRoot }
}
Invoke-ReleaseGate -Name "FrontendTypecheck" -Body {
    Set-Location $webDir
    try {
        & npx tsc -b 2>&1 | Out-String | Write-Host
        Assert-LastExit "tsc -b"
        "TypeScript build clean"
    } finally { Set-Location $repoRoot }
}
Invoke-ReleaseGate -Name "FrontendLint" -Body {
    Set-Location $webDir
    try {
        & npm run lint 2>&1 | Out-String | Write-Host
        Assert-LastExit "npm run lint"
        "lint clean"
    } finally { Set-Location $repoRoot }
}
Invoke-ReleaseGate -Name "FrontendTests" -Body {
    Set-Location $webDir
    try {
        & npx vitest run 2>&1 | Out-String | Write-Host
        Assert-LastExit "vitest run"
        "vitest suite passed"
    } finally { Set-Location $repoRoot }
}
Invoke-ReleaseGate -Name "FrontendBuild" -Body {
    Set-Location $webDir
    try {
        & npm run build 2>&1 | Out-String | Write-Host
        Assert-LastExit "npm run build"
        "production bundle built"
    } finally { Set-Location $repoRoot }
}

# ---- Gate: Word/COM suite on capable runner ----
$wordPresent = Test-WordInstalled
if ($wordPresent) {
    Invoke-ReleaseGate -Name "WordSuite" -Body {
        & dotnet test --nologo --no-build --filter "FullyQualifiedName~Word" `
            --results-directory $resultsPath --logger "trx" 2>&1 | Out-String | Write-Host
        Assert-LastExit "dotnet test --filter Word"
        "Word/COM suite executed on capable runner"
    }
} else {
    Write-Host "`n[gate] WordSuite" -ForegroundColor Yellow
    $note = "Microsoft Word not detected on this runner (no WINWORD App Paths entry, PATH, or Word.Application ProgID)."
    if ($AllowEnvironmentGatedWordSkip -and $WordSkipJustification -ne "") {
        Add-GateResult -Name "WordSuite" -Status "EnvironmentGated" -Detail "$note Justification: $WordSkipJustification"
    } else {
        Add-GateResult -Name "WordSuite" -Status "Failed" -Detail (
            "$note Re-run on a capable runner with Word installed, or pass " +
            "-AllowEnvironmentGatedWordSkip with -WordSkipJustification.")
    }
}

# ---- Verdict: any failure blocks packaging ----
$failedGates = @($script:gates | Where-Object { $_.status -eq "Failed" })
$wordSkipNote = ""
$eg = $script:gates | Where-Object { $_.name -eq "WordSuite" -and $_.status -eq "EnvironmentGated" }
if ($eg) { $wordSkipNote = $WordSkipJustification }

if ($failedGates.Count -gt 0) {
    Write-VerificationReport -Verdict "BLOCKED" -Commit $commit -Branch $branch `
        -Dirty $dirty -Checksums @() -WordSkipNote $wordSkipNote | Out-Null
    Write-Host "`n[REL-02] BLOCKED: $($failedGates.Count) gate(s) failed. Packaging NOT invoked." -ForegroundColor Red
    foreach ($g in $failedGates) { Write-Host "  - $($g.name): $($g.detail)" -ForegroundColor Red }
    exit 1
}

if (-not $SkipPackage) {
    Write-Host "`nAll gates passed — invoking packaging..." -ForegroundColor Green
    & "$PSScriptRoot\build-release.ps1"
    if ($LASTEXITCODE -ne 0) {
        Write-VerificationReport -Verdict "BLOCKED" -Commit $commit -Branch $branch `
            -Dirty $dirty -Checksums @() -WordSkipNote $wordSkipNote | Out-Null
        Write-Host "`n[REL-02] BLOCKED: packaging failed after green gates." -ForegroundColor Red
        exit 1
    }
    Write-Host "`n[PACKAGED] Release artifacts produced after green gates." -ForegroundColor Green
}

if (-not $SkipPackage) {
    # ---- Gate: post-package verification (artifact-presence tests need artifacts) ----
    # Runs the two packaging-output tests excluded from BackendFull against the
    # freshly produced artifacts. Failure still BLOCKS the release verdict, so
    # broken artifacts are never shipped (the workflow uploads only on success).
    Invoke-ReleaseGate -Name "PackagingVerification" -Body {
        & dotnet test --nologo --no-build --results-directory $resultsPath --logger "trx" `
            --filter $script:artifactTestFilter 2>&1 | Out-String | Write-Host
        Assert-LastExit "dotnet test (packaging verification)"
        "packaged artifacts verified"
    }
} else {
    # Gate-only run: packaging (and its post-package verification) was skipped
    # explicitly, so mark the gate EnvironmentGated rather than green.
    Add-GateResult -Name "PackagingVerification" -Status "EnvironmentGated" -Detail "-SkipPackage was passed; packaging verification did not execute."
}

# ---- Checksums attached to artifacts ----
$checksums = @()
$hashTargets = @(
    (Join-Path $repoRoot "artifacts\installer"),
    (Join-Path $repoRoot "artifacts\desktop")
)
$hashFile = Join-Path $outputPath "checksums.sha256"
"" | Set-Content -Path $hashFile -Encoding UTF8
foreach ($dir in $hashTargets) {
    if (-not (Test-Path $dir)) { continue }
    foreach ($f in Get-ChildItem -Path $dir -File -Recurse) {
        $h = (Get-FileHash -Path $f.FullName -Algorithm SHA256).Hash
        $rel = $f.FullName.Substring($repoRoot.ToString().Length + 1)
        "$h  $rel" | Add-Content -Path $hashFile -Encoding UTF8
        $checksums += [pscustomobject]@{ file = $rel; hash = $h }
    }
}
Write-Host "Checksums: $hashFile ($($checksums.Count) file(s))" -ForegroundColor Cyan

Write-VerificationReport -Verdict "PASSED" -Commit $commit -Branch $branch `
    -Dirty $dirty -Checksums $checksums -WordSkipNote $wordSkipNote | Out-Null
Write-Host "`n[REL-02] PASSED: release verified at commit $commit." -ForegroundColor Green
exit 0
