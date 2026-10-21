param(
    [string]$RepoRoot = "",
    [switch]$Uninstall
)

# DEV-UPD — registers the local auto-update watcher as a per-user logon task.
# No administrator rights are required: the task runs as the current user at
# the limited run level and only touches %LOCALAPPDATA%.
#
# Run this only after the auto-update branch is merged to master. The installed
# app must already have the /api/app/* control surface and runtime.json
# handshake; otherwise a running app cannot be stopped safely.

$ErrorActionPreference = "Stop"
$taskName = "DocumentIDE Dev Auto-Update"

if ($Uninstall) {
    $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if ($null -eq $task) {
        Write-Host "Task '$taskName' is not registered."
        exit 0
    }

    try {
        Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    }
    catch {
        # Not running, or already stopped.
    }

    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    Write-Host "Removed scheduled task '$taskName'."
    exit 0
}

$repo = if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}
else {
    (Resolve-Path $RepoRoot).Path
}

$watcher = Join-Path $repo "scripts\dev-autoupdate.ps1"
if (-not (Test-Path -LiteralPath $watcher)) {
    throw "Watcher script missing: $watcher"
}

$pwsh = (Get-Command pwsh -ErrorAction SilentlyContinue).Source
if ([string]::IsNullOrWhiteSpace($pwsh)) {
    throw "PowerShell 7 (pwsh) is required on PATH."
}

$identity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name

$action = New-ScheduledTaskAction `
    -Execute $pwsh `
    -Argument "-NoProfile -WindowStyle Hidden -File `"$watcher`"" `
    -WorkingDirectory $repo

$trigger = New-ScheduledTaskTrigger -AtLogOn

# Unlimited runtime; never start a second watcher (the script also holds
# updater.lock as a second line of defense).
$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -MultipleInstances IgnoreNew

$principal = New-ScheduledTaskPrincipal `
    -UserId $identity `
    -LogonType Interactive `
    -RunLevel Limited

Register-ScheduledTask `
    -TaskName $taskName `
    -Action $action `
    -Trigger $trigger `
    -Settings $settings `
    -Principal $principal `
    -Description "Rebuilds and restarts Document IDE when origin/master advances (developer machine only)." `
    -Force | Out-Null

Write-Host "Registered scheduled task '$taskName' for $identity."
Write-Host "  watcher: $watcher"
Write-Host "  log:     $env:LOCALAPPDATA\DocumentIDE\logs\autoupdate.log"
Write-Host "  remove:  scripts\install-dev-autoupdate.ps1 -Uninstall"
exit 0
