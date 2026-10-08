@echo off
setlocal

rem Ask Windows for elevation when launched by double-click.
set "ONEKEY_ADMIN_STARTUP_SCRIPT=%~f0"
powershell.exe -NoLogo -NoProfile -Command "$id = [Security.Principal.WindowsIdentity]::GetCurrent(); $p = New-Object Security.Principal.WindowsPrincipal($id); if ($p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { exit 0 }; exit 1"
if errorlevel 1 (
    powershell.exe -NoLogo -NoProfile -Command "try { $path = $env:ONEKEY_ADMIN_STARTUP_SCRIPT; $psi = New-Object System.Diagnostics.ProcessStartInfo; $psi.FileName = $path; $psi.UseShellExecute = $true; $psi.Verb = 'runas'; [void][System.Diagnostics.Process]::Start($psi); exit 0 } catch { Write-Host ('Administrator approval was cancelled or could not be requested: ' + $_.Exception.Message); exit 1 }"
    if errorlevel 1 pause
    exit /b
)

rem UAC approval is required. Use the same Windows account as 1Key.
rem Register an interactive logon task; do not disable Defender or UAC.
powershell.exe -NoLogo -NoProfile -Command "$ErrorActionPreference = 'Stop'; try { $identity = [Security.Principal.WindowsIdentity]::GetCurrent(); $principal = New-Object Security.Principal.WindowsPrincipal($identity); if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Right-click this CMD file and select Run as administrator. Use your usual Windows account.' }; $target = Join-Path $env:LOCALAPPDATA 'Programs\1Key\1Key.exe'; if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw ('1Key executable not found: ' + $target) }; $sid = $identity.User.Value; $name = '1Key-AdminStartup-' + $sid; if (Get-ScheduledTask -TaskName $name -TaskPath '\' -ErrorAction SilentlyContinue) { throw ('Task already exists; no changes made: ' + $name) }; $action = New-ScheduledTaskAction -Execute $target -WorkingDirectory (Split-Path -Parent $target); $trigger = New-ScheduledTaskTrigger -AtLogOn -User $sid; $taskPrincipal = New-ScheduledTaskPrincipal -UserId $sid -LogonType Interactive -RunLevel Highest; $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew; [void](Register-ScheduledTask -TaskName $name -TaskPath '\' -Action $action -Trigger $trigger -Principal $taskPrincipal -Settings $settings -Description 'Start installed 1Key at sign-in with highest available privileges'); Write-Host ('Registered: ' + $name); Write-Host ('Program: ' + $target); Write-Host 'The task will run at the next sign-in. It has not been started now.'; exit 0 } catch { Write-Host $_.Exception.Message -ForegroundColor Red; exit 1 }"

if errorlevel 1 (
    echo Registration failed. No existing task was replaced.
    pause
    exit /b 1
)

echo Registration complete.
pause
exit /b 0
