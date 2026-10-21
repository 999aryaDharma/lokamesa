param(
    [string]$RepoRoot = "",
    [string]$WorktreeRoot = "",
    [int]$IntervalSeconds = 60,
    [switch]$Once,
    [switch]$Force,
    [switch]$SelfTest,
    [string]$SelfTestRoot = "",
    [switch]$NoLaunch
)

# DEV-UPD — local auto-update watcher for the developer machine.
#
# Polls origin/master, builds a dedicated worktree, stages a self-contained
# payload, then stops the app through its control surface, swaps the install
# directory, relaunches and health-checks. A failed build, failed swap or
# failing new build leaves the previous install working. It never touches user
# documents, the database, history or the WebView profile.
#
# Enable this scheduled task only after the branch that adds /api/app/* and
# runtime.json is on master: without that handshake a running app cannot be
# stopped safely.
#
# The control token from runtime.json is never logged.

$ErrorActionPreference = "Stop"

$script:DataRoot = Join-Path $env:LOCALAPPDATA "DocumentIDE"
$script:LogDir = Join-Path $script:DataRoot "logs"
$script:LogPath = Join-Path $script:LogDir "autoupdate.log"
$script:RuntimePath = Join-Path $script:DataRoot "runtime.json"
$script:LastBuiltPath = Join-Path $script:DataRoot "last-built.sha"
$script:BadShasPath = Join-Path $script:DataRoot "bad-shas.txt"
$script:LockPath = Join-Path $script:DataRoot "updater.lock"
$script:MissingInstallLogged = $false
$script:StagingRoot = Join-Path $script:DataRoot "staging"
$script:InstallDir = Join-Path $env:LOCALAPPDATA "Programs\Document IDE"

function Write-Log {
    param(
        [Parameter(Mandatory)][string]$Message,
        [ValidateSet("INFO", "WARN", "ERROR")][string]$Level = "INFO"
    )

    try {
        New-Item -ItemType Directory -Path $script:LogDir -Force | Out-Null
        $line = "{0} [{1}] {2}" -f (Get-Date).ToString("yyyy-MM-dd HH:mm:ss"), $Level, $Message
        Add-Content -LiteralPath $script:LogPath -Value $line -Encoding UTF8
    }
    catch {
        # Logging must never take the watcher down.
    }
}

function Invoke-AppApi {
    param(
        [Parameter(Mandatory)][int]$Port,
        [Parameter(Mandatory)][string]$ControlToken,
        [Parameter(Mandatory)][ValidateSet("GET", "POST")][string]$Method,
        [Parameter(Mandatory)][string]$Path,
        [switch]$AllowFailure
    )

    $headers = @{ "X-DocumentIDE-Control-Token" = $ControlToken }
    $uri = "http://127.0.0.1:$Port$Path"
    try {
        return Invoke-RestMethod -Uri $uri -Method $Method -Headers $headers -TimeoutSec 30
    }
    catch {
        if ($AllowFailure) { return $null }
        throw
    }
}

function Get-MasterSha {
    param([Parameter(Mandatory)][string]$Worktree)

    & git -C $Worktree fetch origin master --quiet
    if ($LASTEXITCODE -ne 0) { throw "git fetch origin master failed in $Worktree" }

    $sha = (& git -C $Worktree rev-parse origin/master).Trim()
    if ($LASTEXITCODE -ne 0 -or $sha -notmatch '^[0-9a-f]{40}$') {
        throw "git rev-parse origin/master returned '$sha'"
    }
    return $sha
}

function Get-RepoVersion {
    param([Parameter(Mandatory)][string]$RepoRoot)

    $csprojPath = Join-Path $RepoRoot "src\DocumentIDE.Desktop\DocumentIDE.Desktop.csproj"
    if (-not (Test-Path -LiteralPath $csprojPath)) { throw "Project file missing: $csprojPath" }

    [xml]$project = Get-Content -LiteralPath $csprojPath -Raw
    $versionNode = @($project.Project.PropertyGroup.Version) |
        Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
        Select-Object -First 1

    if ($null -eq $versionNode) { throw "No <Version> element in $csprojPath" }
    return ([string]$versionNode).Trim()
}

function Test-BadSha {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Sha
    )

    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    $lines = @(Get-Content -LiteralPath $Path | ForEach-Object { $_.Trim() })
    return $lines -contains $Sha
}

function Add-BadSha {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Sha
    )

    if (Test-BadSha -Path $Path -Sha $Sha) { return }
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    Add-Content -LiteralPath $Path -Value $Sha -Encoding ASCII
}

function Remove-BadSha {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Sha
    )

    if (-not (Test-Path -LiteralPath $Path)) { return }

    $remaining = @(Get-Content -LiteralPath $Path |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -and $_ -ne $Sha })

    if ($remaining.Count -eq 0) {
        Remove-Item -LiteralPath $Path -Force
    }
    else {
        Set-Content -LiteralPath $Path -Value $remaining -Encoding ASCII
    }
}

function Test-NewBuild {
    param(
        [Parameter(Mandatory)][string]$Sha,
        [Parameter(Mandatory)][string]$LastBuiltPath,
        [string]$BadShasPath = "",
        [switch]$ForceBuild
    )

    if ($ForceBuild) { return $true }

    # A SHA whose build or health check already failed is not retried until a
    # new master SHA arrives or the operator passes -Force.
    if (-not [string]::IsNullOrWhiteSpace($BadShasPath) -and
        (Test-BadSha -Path $BadShasPath -Sha $Sha)) {
        return $false
    }

    if (-not (Test-Path -LiteralPath $LastBuiltPath)) { return $true }

    $raw = Get-Content -LiteralPath $LastBuiltPath -Raw
    if ([string]::IsNullOrWhiteSpace($raw)) { return $true }
    return $raw.Trim() -ne $Sha
}

function Test-AppProcessRunning {
    param([Parameter(Mandatory)][string]$InstallDir)

    $prefix = $InstallDir.TrimEnd('\') + '\'

    foreach ($process in @(Get-Process -Name "DocumentIDE.Desktop" -ErrorAction SilentlyContinue)) {
        try {
            if (-not [string]::IsNullOrWhiteSpace($process.Path) -and
                $process.Path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
                return $true
            }
        }
        catch {
            # Access to Path is best effort; another process is not our app.
        }
    }

    return $false
}

function Test-StagedPayload {
    param(
        [Parameter(Mandatory)][string]$StagingDir,
        [Parameter(Mandatory)][string]$Sha
    )

    $manifestPath = Join-Path $StagingDir "update-manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath)) { return $false }

    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    }
    catch {
        return $false
    }

    if ($manifest.sha -ne $Sha) { return $false }

    foreach ($relative in @("DocumentIDE.Desktop.exe", "DocumentIDE.ShareHost.exe", "wwwroot\index.html")) {
        if (-not (Test-Path -LiteralPath (Join-Path $StagingDir $relative))) { return $false }
    }
    return $true
}

function Invoke-Build {
    param(
        [Parameter(Mandatory)][string]$Worktree,
        [Parameter(Mandatory)][string]$Sha,
        [Parameter(Mandatory)][string]$StagingDir,
        [string]$Version = "",
        [Parameter(Mandatory)][string]$PublisherPath,
        [switch]$SkipGit
    )

    if (-not $SkipGit) {
        & git -C $Worktree reset --hard origin/master --quiet
        if ($LASTEXITCODE -ne 0) { throw "git reset --hard origin/master failed in $Worktree" }
    }

    # Read the version after the checkout matches origin/master: a version bump
    # in the commit being installed must be part of that build.
    if ([string]::IsNullOrWhiteSpace($Version)) {
        $Version = Get-RepoVersion -RepoRoot $Worktree
    }

    if (Test-Path -LiteralPath $StagingDir) {
        Remove-Item -LiteralPath $StagingDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $StagingDir -Force | Out-Null

    # The publisher emits native build output on the success stream. Capture it
    # so Invoke-Build returns exactly one value: the staging path.
    $null = & $PublisherPath -RepoRoot $Worktree -OutputDir $StagingDir -Version $Version -Sha $Sha
    if ($LASTEXITCODE -ne 0) { throw "publish-payload.ps1 failed for $Sha" }

    $manifest = [ordered]@{
        sha = $Sha
        version = $Version
        builtAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    } | ConvertTo-Json -Compress

    [System.IO.File]::WriteAllText(
        (Join-Path $StagingDir "update-manifest.json"),
        $manifest,
        (New-Object System.Text.UTF8Encoding($false)))

    return $StagingDir
}

function Read-Runtime {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) { return $null }

    try {
        $payload = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    }
    catch {
        return $null
    }

    $pidValue = 0
    if (-not [int]::TryParse([string]$payload.pid, [ref]$pidValue) -or $pidValue -le 0) { return $null }

    $portValue = 0
    if (-not [int]::TryParse([string]$payload.port, [ref]$portValue) -or $portValue -le 0 -or $portValue -gt 65535) {
        return $null
    }

    if ([string]::IsNullOrWhiteSpace([string]$payload.controlToken)) { return $null }

    try {
        $process = Get-Process -Id $pidValue -ErrorAction Stop
    }
    catch {
        return $null
    }

    if ($null -eq $process -or $process.HasExited) { return $null }

    # PID reuse must not point the watcher (and its control token) at an
    # unrelated process.
    if ($process.ProcessName -ne "DocumentIDE.Desktop") { return $null }

    try {
        $fileTime = (Get-Item -LiteralPath $Path).LastWriteTimeUtc
        $processStart = $process.StartTime.ToUniversalTime()
    }
    catch {
        return $null
    }

    # A crash can leave runtime.json behind. runtime.json is written after the
    # process starts, so allow only a tiny clock skew: a crash artifact from an
    # earlier process with a reused PID must never contribute its port or token.
    if ($fileTime -lt $processStart.AddSeconds(-2)) { return $null }

    return [pscustomobject]@{
        pid = $pidValue
        port = $portValue
        version = [string]$payload.version
        sha = [string]$payload.sha
        controlToken = [string]$payload.controlToken
    }
}

function Wait-UntilIdle {
    param(
        [Parameter(Mandatory)][int]$Port,
        [Parameter(Mandatory)][string]$ControlToken,
        [int]$TimeoutSeconds = 300
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $status = Invoke-AppApi -Port $Port -ControlToken $ControlToken -Method GET -Path "/api/app/status" -AllowFailure
        if ($null -ne $status -and -not [bool]$status.busy) { return $true }
        Start-Sleep -Seconds 5
    }
    return $false
}

function Request-PrepareRestart {
    param(
        [Parameter(Mandatory)][int]$Port,
        [Parameter(Mandatory)][string]$ControlToken
    )

    try {
        $response = Invoke-AppApi -Port $Port -ControlToken $ControlToken -Method POST -Path "/api/app/prepare-restart"
    }
    catch {
        return [pscustomobject]@{ ok = $false; reason = $_.Exception.Message }
    }

    if ($null -eq $response) {
        return [pscustomobject]@{ ok = $false; reason = "no-response" }
    }

    return [pscustomobject]@{
        ok = [bool]$response.ok
        reason = [string]$response.reason
    }
}

function Request-Quit {
    param(
        [Parameter(Mandatory)][int]$Port,
        [Parameter(Mandatory)][string]$ControlToken,
        [Parameter(Mandatory)][int]$ProcessId,
        [int]$TimeoutSeconds = 30
    )

    try {
        $null = Invoke-AppApi -Port $Port -ControlToken $ControlToken -Method POST -Path "/api/app/quit"
    }
    catch {
        Write-Log "quit request failed: $($_.Exception.Message)" "WARN"
    }

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
        if ($null -eq $process) { return $true }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

function Invoke-Swap {
    param(
        [Parameter(Mandatory)][string]$InstallDir,
        [Parameter(Mandatory)][string]$StagingDir,
        [switch]$SimulateStagingMoveFailure
    )

    if (-not (Test-Path -LiteralPath $StagingDir)) {
        throw "Staging directory missing: $StagingDir"
    }

    $parent = Split-Path -Parent $InstallDir
    if (-not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    $leaf = Split-Path -Leaf $InstallDir
    $suffix = [Guid]::NewGuid().ToString("N").Substring(0, 8)
    $oldDir = Join-Path $parent "$leaf.old-$suffix"
    $hadInstall = Test-Path -LiteralPath $InstallDir

    if ($hadInstall) {
        Move-Item -LiteralPath $InstallDir -Destination $oldDir
    }

    try {
        if ($SimulateStagingMoveFailure) {
            throw "simulated staging move failure"
        }
        Move-Item -LiteralPath $StagingDir -Destination $InstallDir
    }
    catch {
        if ($hadInstall -and -not (Test-Path -LiteralPath $InstallDir) -and (Test-Path -LiteralPath $oldDir)) {
            Move-Item -LiteralPath $oldDir -Destination $InstallDir
        }
        throw
    }

    if ($hadInstall) {
        return $oldDir
    }
    return $null
}

function Invoke-Rollback {
    param(
        [Parameter(Mandatory)][string]$InstallDir,
        [string]$OldDir = "",
        [int]$NewProcessId = 0,
        [string]$DesktopExe = "",
        [string]$RuntimePath = "",
        [switch]$NoLaunch
    )

    if ($NewProcessId -gt 0) {
        $graceful = $false
        if (-not [string]::IsNullOrWhiteSpace($RuntimePath)) {
            $runtime = Read-Runtime -Path $RuntimePath
            if ($null -ne $runtime -and $runtime.pid -eq $NewProcessId) {
                $prepared = Request-PrepareRestart -Port $runtime.port -ControlToken $runtime.controlToken
                if ($prepared.ok) {
                    $graceful = Request-Quit -Port $runtime.port -ControlToken $runtime.controlToken -ProcessId $NewProcessId
                }
                else {
                    Write-Log "rollback: the new build could not flush ($($prepared.reason)); stopping it" "WARN"
                }
            }
        }

        if (-not $graceful) {
            $process = Get-Process -Id $NewProcessId -ErrorAction SilentlyContinue
            if ($null -ne $process) {
                Stop-Process -Id $NewProcessId -Force -ErrorAction SilentlyContinue
                $null = $process.WaitForExit(10000)
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($OldDir) -or -not (Test-Path -LiteralPath $OldDir)) {
        Write-Log "rollback: no previous install to restore at $InstallDir" "ERROR"
        return
    }

    for ($attempt = 0; $attempt -lt 20 -and (Test-Path -LiteralPath $InstallDir); $attempt++) {
        try {
            Remove-Item -LiteralPath $InstallDir -Recurse -Force -ErrorAction Stop
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }

    if (Test-Path -LiteralPath $InstallDir) {
        Write-Log "rollback: could not clear the new install at $InstallDir" "ERROR"
        return
    }

    try {
        Move-Item -LiteralPath $OldDir -Destination $InstallDir -ErrorAction Stop
        Write-Log "rollback: restored the previous install at $InstallDir" "WARN"
    }
    catch {
        Write-Log "rollback: could not restore $OldDir to $InstallDir : $($_.Exception.Message)" "ERROR"
        return
    }

    if (-not $NoLaunch -and -not [string]::IsNullOrWhiteSpace($DesktopExe) -and (Test-Path -LiteralPath $DesktopExe)) {
        try {
            $null = Start-Process -FilePath $DesktopExe -WorkingDirectory $InstallDir -PassThru
        }
        catch {
            Write-Log "rollback: could not relaunch $DesktopExe : $($_.Exception.Message)" "ERROR"
        }
    }
}

function Start-App {
    param([Parameter(Mandatory)][string]$ExePath)

    if (-not (Test-Path -LiteralPath $ExePath)) {
        throw "Desktop executable missing: $ExePath"
    }

    return Start-Process -FilePath $ExePath -WorkingDirectory (Split-Path -Parent $ExePath) -PassThru
}

function Test-Health {
    param(
        [Parameter(Mandatory)][int]$ExpectedProcessId,
        [Parameter(Mandatory)][string]$RuntimePath,
        [string]$ExpectedSha = "",
        [int]$TimeoutSeconds = 60
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $process = Get-Process -Id $ExpectedProcessId -ErrorAction SilentlyContinue
        if ($null -eq $process -or $process.HasExited) { return $false }

        $runtime = Read-Runtime -Path $RuntimePath
        if ($null -ne $runtime -and $runtime.pid -eq $ExpectedProcessId) {
            $status = Invoke-AppApi -Port $runtime.port -ControlToken $runtime.controlToken -Method GET -Path "/api/app/status" -AllowFailure
            if ($null -ne $status) {
                if ([string]::IsNullOrWhiteSpace($ExpectedSha) -or $status.sha -eq $ExpectedSha) {
                    return $true
                }
            }
        }

        Start-Sleep -Seconds 2
    }
    return $false
}

function Restore-InterruptedSwap {
    param([Parameter(Mandatory)][string]$InstallDir)

    if (Test-Path -LiteralPath $InstallDir) { return }

    $parent = Split-Path -Parent $InstallDir
    if (-not (Test-Path -LiteralPath $parent)) { return }

    $leaf = Split-Path -Leaf $InstallDir
    $old = Get-ChildItem -LiteralPath $parent -Directory -Filter "$leaf.old-*" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1

    if ($null -ne $old) {
        try {
            Move-Item -LiteralPath $old.FullName -Destination $InstallDir -ErrorAction Stop
            Write-Log "recovered an interrupted swap from $($old.FullName)" "WARN"
        }
        catch {
            # Recovery is best effort; the caller still needs to try a relaunch
            # from the .old-* sibling.
            Write-Log "could not recover the interrupted swap from $($old.FullName): $($_.Exception.Message)" "ERROR"
        }
    }
}

function Initialize-Worktree {
    param(
        [Parameter(Mandatory)][string]$Repo,
        [Parameter(Mandatory)][string]$Worktree
    )

    if (Test-Path -LiteralPath $Worktree) {
        if (-not (Test-Path -LiteralPath (Join-Path $Worktree ".git"))) {
            throw "WorktreeRoot exists but is not a git worktree: $Worktree"
        }
        return
    }

    & git -C $Repo worktree prune
    & git -C $Repo fetch origin master --quiet
    if ($LASTEXITCODE -ne 0) { throw "git fetch origin master failed in $Repo" }

    # Detached HEAD: the watcher never competes with the developer's checkout
    # for the local master branch.
    & git -C $Repo worktree add --detach $Worktree origin/master
    if ($LASTEXITCODE -ne 0) { throw "git worktree add failed for $Worktree" }
}

function Enter-UpdaterLock {
    param([Parameter(Mandatory)][string]$Path)

    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    try {
        return [System.IO.File]::Open(
            $Path,
            [System.IO.FileMode]::OpenOrCreate,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None)
    }
    catch {
        return $null
    }
}

function Invoke-UpdateCycle {
    param(
        [Parameter(Mandatory)][string]$Repo,
        [Parameter(Mandatory)][string]$Worktree,
        [Parameter(Mandatory)][string]$InstallDir,
        [Parameter(Mandatory)][string]$StagingRoot,
        [Parameter(Mandatory)][string]$RuntimePath,
        [Parameter(Mandatory)][string]$LastBuiltPath,
        [Parameter(Mandatory)][string]$BadShasPath,
        [string]$Version = "",
        [switch]$ForceBuild,
        [switch]$NoLaunchApp
    )

    Restore-InterruptedSwap -InstallDir $InstallDir

    if (-not (Test-Path -LiteralPath $InstallDir)) {
        if (-not $script:MissingInstallLogged) {
            Write-Log "install directory missing at $InstallDir; install the per-user MSI once" "WARN"
            $script:MissingInstallLogged = $true
        }
        return
    }

    $sha = Get-MasterSha -Worktree $Worktree
    if (-not (Test-NewBuild -Sha $sha -LastBuiltPath $LastBuiltPath -BadShasPath $BadShasPath -ForceBuild:([bool]$ForceBuild))) {
        if (Test-BadSha -Path $BadShasPath -Sha $sha) {
            Write-Log "master $sha is marked bad; waiting for a new SHA (or -Force)" "WARN"
        }
        else {
            Write-Log "master $sha is already installed"
        }
        return
    }

    New-Item -ItemType Directory -Path $StagingRoot -Force | Out-Null
    $stagingDir = Join-Path $StagingRoot $sha
    if (-not (Test-StagedPayload -StagingDir $stagingDir -Sha $sha)) {
        Write-Log "building master $sha in $Worktree"

        $publisher = Join-Path $Worktree "scripts\publish-payload.ps1"
        if (-not (Test-Path -LiteralPath $publisher)) {
            $publisher = Join-Path $Repo "scripts\publish-payload.ps1"
        }
        if (-not (Test-Path -LiteralPath $publisher)) {
            throw "publisher missing: $publisher"
        }

        try {
            $stagingDir = Invoke-Build -Worktree $Worktree -Sha $sha -StagingDir $stagingDir -Version $Version -PublisherPath $publisher
        }
        catch {
            Add-BadSha -Path $BadShasPath -Sha $sha
            Write-Log "build failed for $sha; marked bad (use -Force to retry): $($_.Exception.Message)" "ERROR"
            return
        }
    }
    else {
        Write-Log "reusing the staged payload for $sha"
    }

    # The payload is the source of truth for the version, fresh or reused.
    $buildInfoPath = Join-Path $stagingDir "build-info.json"
    if (Test-Path -LiteralPath $buildInfoPath) {
        try {
            $Version = [string](Get-Content -LiteralPath $buildInfoPath -Raw | ConvertFrom-Json).version
        }
        catch {
            # Keep the derived version for the log.
        }
    }

    $runtime = Read-Runtime -Path $RuntimePath

    # A live installed process without a usable runtime.json cannot be stopped
    # safely; never swap its files. This is also what happens with a pre-1.0.2
    # install that predates the handshake.
    if ($null -eq $runtime -and (Test-AppProcessRunning -InstallDir $InstallDir)) {
        Write-Log "DocumentIDE is running without a usable runtime.json; update deferred" "WARN"
        return
    }

    $wasRunning = $null -ne $runtime

    if ($wasRunning) {
        if (-not (Wait-UntilIdle -Port $runtime.port -ControlToken $runtime.controlToken)) {
            Write-Log "app stayed busy; update deferred" "WARN"
            return
        }

        $prepared = Request-PrepareRestart -Port $runtime.port -ControlToken $runtime.controlToken
        if (-not $prepared.ok) {
            $reason = if ([string]::IsNullOrWhiteSpace($prepared.reason)) { "unknown" } else { $prepared.reason }
            Write-Log "prepare-restart refused ($reason); update deferred" "WARN"
            return
        }

        if (-not (Request-Quit -Port $runtime.port -ControlToken $runtime.controlToken -ProcessId $runtime.pid)) {
            Write-Log "app did not exit; update deferred" "WARN"
            return
        }
    }

    $oldDir = $null
    try {
        $oldDir = Invoke-Swap -InstallDir $InstallDir -StagingDir $stagingDir
    }
    catch {
        Write-Log "swap failed: $($_.Exception.Message); marked $sha bad (fix the lock, then use -Force)" "ERROR"
        Add-BadSha -Path $BadShasPath -Sha $sha

        # The app was stopped before the swap. Restore an interrupted rename and
        # relaunch the previous build so a failed swap never leaves it closed.
        if ($wasRunning -and -not $NoLaunchApp) {
            Restore-InterruptedSwap -InstallDir $InstallDir
            $previousExe = Join-Path $InstallDir "DocumentIDE.Desktop.exe"

            if (-not (Test-Path -LiteralPath $previousExe)) {
                $leaf = Split-Path -Leaf $InstallDir
                $oldCandidate = Get-ChildItem -LiteralPath (Split-Path -Parent $InstallDir) `
                    -Directory -Filter "$leaf.old-*" -ErrorAction SilentlyContinue |
                    Sort-Object LastWriteTimeUtc -Descending |
                    Select-Object -First 1

                if ($null -ne $oldCandidate) {
                    $previousExe = Join-Path $oldCandidate.FullName "DocumentIDE.Desktop.exe"
                }
            }

            if (Test-Path -LiteralPath $previousExe) {
                try {
                    $null = Start-App -ExePath $previousExe
                    Write-Log "relaunched the previous build after the failed swap" "WARN"
                }
                catch {
                    Write-Log "relaunch after the failed swap failed: $($_.Exception.Message)" "ERROR"
                }
            }
            else {
                Write-Log "no previous executable found after the failed swap; manual relaunch required" "ERROR"
            }
        }
        return
    }

    Remove-Item -LiteralPath $RuntimePath -Force -ErrorAction SilentlyContinue

    $newProcess = $null
    if ($wasRunning -and -not $NoLaunchApp) {
        try {
            $newProcess = Start-App -ExePath (Join-Path $InstallDir "DocumentIDE.Desktop.exe")
        }
        catch {
            Write-Log "relaunch failed: $($_.Exception.Message)" "ERROR"
            Add-BadSha -Path $BadShasPath -Sha $sha
            Invoke-Rollback -InstallDir $InstallDir -OldDir $oldDir -DesktopExe (Join-Path $InstallDir "DocumentIDE.Desktop.exe")
            Write-Log "marked $sha bad; waiting for a new master SHA" "ERROR"
            return
        }

        $healthy = Test-Health -ExpectedProcessId $newProcess.Id -RuntimePath $RuntimePath -ExpectedSha $sha
        if (-not $healthy) {
            Write-Log "new build $sha failed its health check; rolling back" "ERROR"
            Add-BadSha -Path $BadShasPath -Sha $sha
            Invoke-Rollback `
                -InstallDir $InstallDir `
                -OldDir $oldDir `
                -NewProcessId $newProcess.Id `
                -DesktopExe (Join-Path $InstallDir "DocumentIDE.Desktop.exe") `
                -RuntimePath $RuntimePath
            Write-Log "marked $sha bad; waiting for a new master SHA" "ERROR"
            return
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($oldDir) -and (Test-Path -LiteralPath $oldDir)) {
        Remove-Item -LiteralPath $oldDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    # Remove leftover .old-* directories from an interrupted run. Locked ones
    # fail silently and are retried on the next successful install.
    $installParent = Split-Path -Parent $InstallDir
    $installLeaf = Split-Path -Leaf $InstallDir
    Get-ChildItem -LiteralPath $installParent -Directory -Filter "$installLeaf.old-*" -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

    Remove-BadSha -Path $BadShasPath -Sha $sha
    Set-Content -LiteralPath $LastBuiltPath -Value $sha -Encoding ASCII
    Write-Log "installed master $sha (version $Version)"

    Get-ChildItem -LiteralPath $StagingRoot -Directory -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

function Invoke-SelfTest {
    param([string]$Root)

    $providedRoot = -not [string]::IsNullOrWhiteSpace($Root)
    if (-not $providedRoot) {
        $Root = Join-Path ([System.IO.Path]::GetTempPath()) ("documentide-autoupdate-selftest-" + [Guid]::NewGuid().ToString("N"))
    }
    New-Item -ItemType Directory -Path $Root -Force | Out-Null

    $failures = New-Object System.Collections.Generic.List[string]

    try {
        $runtimePath = Join-Path $Root "runtime.json"
        $lastBuilt = Join-Path $Root "last-built.sha"

        # 1. A runtime.json left by a dead process is ignored.
        $dead = [ordered]@{
            pid = 999999
            port = 51234
            version = "1.0.2.0"
            sha = "dead"
            controlToken = "not-logged"
        } | ConvertTo-Json -Compress
        [System.IO.File]::WriteAllText($runtimePath, $dead, (New-Object System.Text.UTF8Encoding($false)))
        if ($null -ne (Read-Runtime -Path $runtimePath)) {
            $failures.Add("Read-Runtime accepted a dead PID")
        }

        # A live PID that is not DocumentIDE.Desktop is rejected (PID identity).
        $live = [ordered]@{
            pid = $PID
            port = 51234
            version = "1.0.2.0"
            sha = "live"
            controlToken = "not-logged"
        } | ConvertTo-Json -Compress
        [System.IO.File]::WriteAllText($runtimePath, $live, (New-Object System.Text.UTF8Encoding($false)))
        if ($null -ne (Read-Runtime -Path $runtimePath)) {
            $failures.Add("Read-Runtime accepted a non-Desktop process")
        }

        # 2. Build detection compares the SHA.
        [System.IO.File]::WriteAllText($lastBuilt, "aaaa", (New-Object System.Text.UTF8Encoding($false)))
        if (-not (Test-NewBuild -Sha "bbbb" -LastBuiltPath $lastBuilt)) {
            $failures.Add("Test-NewBuild did not detect a new SHA")
        }
        if (Test-NewBuild -Sha "aaaa" -LastBuiltPath $lastBuilt) {
            $failures.Add("Test-NewBuild rebuilt the same SHA")
        }

        [System.IO.File]::WriteAllText($lastBuilt, "", (New-Object System.Text.UTF8Encoding($false)))
        if (-not (Test-NewBuild -Sha "cccc" -LastBuiltPath $lastBuilt)) {
            $failures.Add("Test-NewBuild failed on an empty last-built file")
        }

        # A SHA marked bad is not retried unless -Force is passed.
        $badShas = Join-Path $Root "bad-shas.txt"
        Add-BadSha -Path $badShas -Sha "bad1"
        if (-not (Test-BadSha -Path $badShas -Sha "bad1")) {
            $failures.Add("Add-BadSha did not persist the SHA")
        }
        if (Test-NewBuild -Sha "bad1" -LastBuiltPath $lastBuilt -BadShasPath $badShas) {
            $failures.Add("Test-NewBuild retried a bad SHA")
        }
        if (-not (Test-NewBuild -Sha "bad1" -LastBuiltPath $lastBuilt -BadShasPath $badShas -ForceBuild)) {
            $failures.Add("-Force did not bypass the bad SHA list")
        }
        Remove-BadSha -Path $badShas -Sha "bad1"
        if (Test-BadSha -Path $badShas -Sha "bad1") {
            $failures.Add("Remove-BadSha did not forget the SHA")
        }

        # 3. Swap replaces install/ and leaves sibling data directories alone.
        $install = Join-Path $Root "install"
        $staging = Join-Path $Root "staging"
        $data = Join-Path $Root "DocumentIDE"
        $documents = Join-Path $Root "documents"
        New-Item -ItemType Directory -Path $install, $staging, $data, $documents -Force | Out-Null
        [System.IO.File]::WriteAllText((Join-Path $install "version.txt"), "old")
        [System.IO.File]::WriteAllText((Join-Path $staging "version.txt"), "new")
        [System.IO.File]::WriteAllText((Join-Path $data "keep.txt"), "data")
        [System.IO.File]::WriteAllText((Join-Path $documents "keep.md"), "doc")

        $oldDir = Invoke-Swap -InstallDir $install -StagingDir $staging
        if ((Get-Content -LiteralPath (Join-Path $install "version.txt") -Raw).Trim() -ne "new") {
            $failures.Add("Invoke-Swap did not install the staging payload")
        }
        if (-not (Test-Path -LiteralPath $oldDir) -or (Get-Content -LiteralPath (Join-Path $oldDir "version.txt") -Raw).Trim() -ne "old") {
            $failures.Add("Invoke-Swap did not preserve the previous install")
        }
        if (-not (Test-Path -LiteralPath (Join-Path $data "keep.txt")) -or -not (Test-Path -LiteralPath (Join-Path $documents "keep.md"))) {
            $failures.Add("Invoke-Swap touched sibling directories")
        }

        # 4. A failure while moving staging in restores the original install.
        $staging2 = Join-Path $Root "staging2"
        New-Item -ItemType Directory -Path $staging2 -Force | Out-Null
        [System.IO.File]::WriteAllText((Join-Path $staging2 "version.txt"), "new2")

        $beforeFailure = (Get-Content -LiteralPath (Join-Path $install "version.txt") -Raw).Trim()
        $threw = $false
        try {
            $null = Invoke-Swap -InstallDir $install -StagingDir $staging2 -SimulateStagingMoveFailure
        }
        catch {
            $threw = $true
        }
        if (-not $threw) {
            $failures.Add("Invoke-Swap did not propagate the injected failure")
        }
        if (-not (Test-Path -LiteralPath (Join-Path $install "version.txt")) -or
            (Get-Content -LiteralPath (Join-Path $install "version.txt") -Raw).Trim() -ne $beforeFailure) {
            $failures.Add("Invoke-Swap left the install missing after a failed move")
        }

        # 5. Rollback after a simulated failed health check restores the old install.
        $beforeRollback = (Get-Content -LiteralPath (Join-Path $install "version.txt") -Raw).Trim()
        $oldDir2 = Invoke-Swap -InstallDir $install -StagingDir $staging2
        if ((Get-Content -LiteralPath (Join-Path $install "version.txt") -Raw).Trim() -ne "new2") {
            $failures.Add("second swap did not install the staging payload")
        }
        Invoke-Rollback -InstallDir $install -OldDir $oldDir2 -NoLaunch
        if (-not (Test-Path -LiteralPath (Join-Path $install "version.txt")) -or
            (Get-Content -LiteralPath (Join-Path $install "version.txt") -Raw).Trim() -ne $beforeRollback) {
            $failures.Add("rollback did not restore the previous install")
        }

        # 6. An interrupted swap (install missing, .old-* sibling present) is
        #    recovered on the next run.
        $recoverInstall = Join-Path $Root "recover-install"
        $recoverOld = Join-Path $Root "recover-install.old-deadbeef"
        New-Item -ItemType Directory -Path $recoverOld -Force | Out-Null
        [System.IO.File]::WriteAllText((Join-Path $recoverOld "version.txt"), "recovered")
        Restore-InterruptedSwap -InstallDir $recoverInstall
        if (-not (Test-Path -LiteralPath (Join-Path $recoverInstall "version.txt")) -or
            (Get-Content -LiteralPath (Join-Path $recoverInstall "version.txt") -Raw).Trim() -ne "recovered") {
            $failures.Add("Restore-InterruptedSwap did not restore the .old-* directory")
        }

        # 7. Invoke-Build returns exactly one value even when the publisher
        #    writes native build output. This guards the first-install path.
        $fakePublisher = Join-Path $Root "fake-publisher.ps1"
        $fakePublisherScript = @'
param(
    [string]$RepoRoot,
    [string]$OutputDir,
    [string]$Version,
    [string]$Sha
)
Write-Output "fake publish line one"
Write-Output "fake publish line two"
New-Item -ItemType Directory -Path (Join-Path $OutputDir "wwwroot") -Force | Out-Null
Set-Content -LiteralPath (Join-Path $OutputDir "DocumentIDE.Desktop.exe") -Value "x"
Set-Content -LiteralPath (Join-Path $OutputDir "DocumentIDE.ShareHost.exe") -Value "x"
Set-Content -LiteralPath (Join-Path $OutputDir "wwwroot\index.html") -Value "<html></html>"
Set-Content -LiteralPath (Join-Path $OutputDir "version.txt") -Value $Version
exit 0
'@
        [System.IO.File]::WriteAllText($fakePublisher, $fakePublisherScript, (New-Object System.Text.UTF8Encoding($false)))

        # The version is derived after the checkout, so the fake worktree has a
        # project file but no git history (SkipGit).
        $fakeProjectDir = Join-Path $Root "src\DocumentIDE.Desktop"
        New-Item -ItemType Directory -Path $fakeProjectDir -Force | Out-Null
        [System.IO.File]::WriteAllText(
            (Join-Path $fakeProjectDir "DocumentIDE.Desktop.csproj"),
            '<Project><PropertyGroup><Version>9.9.9.9</Version></PropertyGroup></Project>')

        $buildSha = "0123456789abcdef0123456789abcdef01234567"
        $buildStaging = Join-Path $Root "build-staging"
        $built = Invoke-Build -Worktree $Root -Sha $buildSha -StagingDir $buildStaging -Version "" -PublisherPath $fakePublisher -SkipGit

        if ($built -isnot [string]) {
            $failures.Add("Invoke-Build returned a collection instead of one staging path")
        }
        if ([string]$built -ne $buildStaging) {
            $failures.Add("Invoke-Build returned '$built' instead of '$buildStaging'")
        }
        if (-not (Test-StagedPayload -StagingDir $buildStaging -Sha $buildSha)) {
            $failures.Add("Invoke-Build did not stage a valid payload")
        }
        $builtVersionPath = Join-Path $buildStaging "version.txt"
        if (-not (Test-Path -LiteralPath $builtVersionPath) -or
            (Get-Content -LiteralPath $builtVersionPath -Raw).Trim() -ne "9.9.9.9") {
            $failures.Add("Invoke-Build did not derive the version from the checkout")
        }
    }
    finally {
        if (-not $providedRoot) {
            Remove-Item -LiteralPath $Root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    if ($failures.Count -gt 0) {
        foreach ($failure in $failures) {
            Write-Host "SELFTEST FAIL: $failure"
        }
        throw "self-test failed with $($failures.Count) mismatch(es)"
    }

    Write-Host "SELFTEST PASSED"
}

if ($SelfTest) {
    try {
        Invoke-SelfTest -Root $SelfTestRoot
        exit 0
    }
    catch {
        Write-Host "SELFTEST FAILED: $($_.Exception.Message)"
        exit 1
    }
}

if ($IntervalSeconds -lt 10) { $IntervalSeconds = 10 }

$repo = if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}
else {
    (Resolve-Path $RepoRoot).Path
}

$worktree = if ([string]::IsNullOrWhiteSpace($WorktreeRoot)) {
    "D:\document-ide-master"
}
else {
    $WorktreeRoot
}

$lock = Enter-UpdaterLock -Path $script:LockPath
if ($null -eq $lock) {
    Write-Log "another updater holds $($script:LockPath); exiting" "WARN"
    exit 0
}

$cycleFailed = $false
$forceNext = [bool]$Force
try {
    Write-Log "watcher started (repo $repo, worktree $worktree)"

    while ($true) {
        try {
            # Initialization is retried with the cycle: a transient fetch or a
            # pruned worktree must not kill the watcher until the next logon.
            Initialize-Worktree -Repo $repo -Worktree $worktree

            Invoke-UpdateCycle `
                -Repo $repo `
                -Worktree $worktree `
                -InstallDir $script:InstallDir `
                -StagingRoot $script:StagingRoot `
                -RuntimePath $script:RuntimePath `
                -LastBuiltPath $script:LastBuiltPath `
                -BadShasPath $script:BadShasPath `
                -ForceBuild:$forceNext `
                -NoLaunchApp:([bool]$NoLaunch)
        }
        catch {
            $cycleFailed = $true
            Write-Log "update cycle failed: $($_.Exception.Message)" "ERROR"
        }

        # -Force means "rebuild once even if this SHA is recorded"; without this
        # reset a continuous watcher would rebuild and restart every interval.
        $forceNext = $false

        if ($Once) { break }
        Start-Sleep -Seconds $IntervalSeconds
    }
}
finally {
    $lock.Dispose()
}

if ($cycleFailed) { exit 1 }
exit 0
