param(
    [string]$RepoRoot = "",
    [switch]$Uninstall,
    [ValidateSet("Auto", "ScheduledTask", "Startup")]
    [string]$Mode = "Auto",
    [string]$StartupFolder = ""
)

# DEV-UPD — registers the local auto-update watcher as a per-user logon entry.
# No administrator rights are required. The default tries Task Scheduler and,
# when the machine denies non-admin task creation, falls back to a shortcut in
# the current user's Startup folder. Both run as the current user at the
# limited run level and only touch %LOCALAPPDATA%.
#
# Run this only after the auto-update branch is merged to master. The installed
# app must already have the /api/app/* control surface and runtime.json
# handshake; otherwise a running app cannot be stopped safely.

$ErrorActionPreference = "Stop"
$taskName = "DocumentIDE Dev Auto-Update"
$startup = if ([string]::IsNullOrWhiteSpace($StartupFolder)) {
    [Environment]::GetFolderPath("Startup")
}
else {
    $StartupFolder
}
$shortcutPath = Join-Path $startup "$taskName.lnk"

function Remove-StartupEntry {
    if (Test-Path -LiteralPath $shortcutPath) {
        Remove-Item -LiteralPath $shortcutPath -Force
        Write-Host "Removed Startup shortcut '$shortcutPath'."
    }
}

if ($Uninstall) {
    if ($Mode -ne "Startup") {
        $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        if ($null -ne $task) {
            try {
                Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
            }
            catch {
                # Not running, or already stopped.
            }

            Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
            Write-Host "Removed scheduled task '$taskName'."
        }
        elseif ($Mode -eq "ScheduledTask") {
            Write-Host "Task '$taskName' is not registered."
        }
    }

    if ($Mode -ne "ScheduledTask") {
        Remove-StartupEntry
    }

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

function Install-StartupEntry {
    New-Item -ItemType Directory -Path $startup -Force | Out-Null

    $shell = New-Object -ComObject WScript.Shell
    try {
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = $pwsh
        $shortcut.Arguments = "-NoProfile -WindowStyle Hidden -File `"$watcher`""
        $shortcut.WorkingDirectory = $repo
        $shortcut.WindowStyle = 7
        $shortcut.Description = "Rebuilds and restarts Document IDE when origin/master advances (developer machine only)."
        $shortcut.Save()
    }
    finally {
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
    }

    Write-Host "Registered per-user Startup shortcut '$shortcutPath' for $identity."
    Write-Host "  watcher: $watcher"
    Write-Host "  log:     $env:LOCALAPPDATA\DocumentIDE\logs\autoupdate.log"
    Write-Host "  remove:  scripts\install-dev-autoupdate.ps1 -Uninstall"
}

if ($Mode -ne "Startup") {
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

    try {
        Register-ScheduledTask `
            -TaskName $taskName `
            -Action $action `
            -Trigger $trigger `
            -Settings $settings `
            -Principal $principal `
            -Description "Rebuilds and restarts Document IDE when origin/master advances (developer machine only)." `
            -Force `
            -ErrorAction Stop | Out-Null

        if ($Mode -eq "Auto") {
            Remove-StartupEntry
        }

        Write-Host "Registered scheduled task '$taskName' for $identity."
        Write-Host "  watcher: $watcher"
        Write-Host "  log:     $env:LOCALAPPDATA\DocumentIDE\logs\autoupdate.log"
        Write-Host "  remove:  scripts\install-dev-autoupdate.ps1 -Uninstall"
        exit 0
    }
    catch {
        if ($Mode -eq "ScheduledTask") {
            throw
        }

        Write-Warning "Scheduled task registration failed: $($_.Exception.Message)"
        Write-Warning "Falling back to the per-user Startup folder (no administrator rights required)."
    }
}

Install-StartupEntry
exit 0
