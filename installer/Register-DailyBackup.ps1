param([string]$Application, [switch]$Remove)
$ErrorActionPreference = 'Stop'
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$name = "RetailCore-DailyBackup-$sid"
if ($Remove) {
    Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction SilentlyContinue
    exit 0
}
$action = New-ScheduledTaskAction -Execute $Application -Argument '--daily-backup'
$trigger = New-ScheduledTaskTrigger -Daily -At '21:00'
$principal = New-ScheduledTaskPrincipal -UserId $sid -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Minutes 15)
Register-ScheduledTask -TaskName $name -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'Verified local RetailCore daily backup; no stored password.' -Force | Out-Null
