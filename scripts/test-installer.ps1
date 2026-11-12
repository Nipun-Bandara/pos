param([string]$Version = '0.1.8')
$ErrorActionPreference = 'Stop'
# This script intentionally refuses to run on a shop PC: CI only, with an empty user profile.
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Installer smoke checks must run in the disposable Windows CI profile.' }
$root = Split-Path $PSScriptRoot -Parent
$setup = Join-Path $root "artifacts/installer/RetailCore-$Version-win-x64-setup.exe"
$install = Join-Path $env:LOCALAPPDATA 'Programs/RetailCore'
$data = Join-Path $env:LOCALAPPDATA 'RetailCore'
if (Test-Path $data) { throw 'Refusing to touch an existing data directory.' }
function Run-Setup {
    $p = Start-Process $setup -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "Install failed: $($p.ExitCode)" }
}
Run-Setup
$app = Join-Path $install 'RetailCore.Desktop.exe'
if (-not (Test-Path $app)) { throw 'Installed executable missing.' }
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$task = Get-ScheduledTask -TaskName "RetailCore-DailyBackup-$sid"
if ($task.Actions.Arguments -ne '--daily-backup') { throw 'Daily backup task is missing or incorrect.' }
# Starting the installed UI creates/migrates the actual per-user database; it must not be under {app}.
$p = Start-Process $app -PassThru
try {
    $limit = (Get-Date).AddSeconds(30)
    while (-not (Test-Path (Join-Path $data 'retailcore.db'))) {
        if ((Get-Date) -gt $limit) { throw 'Installed app did not initialize its data path.' }
        Start-Sleep -Milliseconds 200
    }
    Start-Sleep -Seconds 2
} finally { if (-not $p.HasExited) { $null = $p.CloseMainWindow(); if (-not $p.WaitForExit(10000)) { $p.Kill(); $p.WaitForExit() } } }
if (Test-Path (Join-Path $install 'retailcore.db')) { throw 'Database was written inside installation files.' }
$marker = Join-Path $data 'preserve-user-data.txt'
Set-Content $marker 'CI preservation marker'
Run-Setup # exercises the installed version's --prepare-upgrade safety backup before replacement
if ((Get-Content $marker) -ne 'CI preservation marker') { throw 'Upgrade modified user data.' }
$u = Start-Process (Join-Path $install 'unins000.exe') -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -Wait -PassThru
if ($u.ExitCode -ne 0) { throw 'Uninstall failed.' }
if (-not (Test-Path $marker) -or -not (Test-Path (Join-Path $data 'retailcore.db'))) { throw 'Uninstall deleted user data.' }
if (Get-ScheduledTask -TaskName "RetailCore-DailyBackup-$sid" -ErrorAction SilentlyContinue) { throw 'Uninstall retained scheduled task.' }
