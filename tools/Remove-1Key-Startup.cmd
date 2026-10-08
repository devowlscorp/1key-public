@echo off
setlocal

rem Remove only the shortcut and task created by the companion registration scripts.
rem Run as administrator with your usual Windows account to remove the elevated task.
powershell.exe -NoLogo -NoProfile -Command "$ErrorActionPreference = 'Stop'; try { $identity = [Security.Principal.WindowsIdentity]::GetCurrent(); $sid = $identity.User.Value; $name = '1Key-AdminStartup-' + $sid; $target = Join-Path $env:LOCALAPPDATA 'Programs\1Key\1Key.exe'; $link = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup\1Key.lnk'; $task = Get-ScheduledTask -TaskName $name -TaskPath '\' -ErrorAction SilentlyContinue; if ($task) { $principal = New-Object Security.Principal.WindowsPrincipal($identity); if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Right-click this CMD file and select Run as administrator, using your usual Windows account. Nothing has been removed.' }; if ($task.Description -ne 'Start installed 1Key at sign-in with highest available privileges' -or @($task.Actions).Count -ne 1 -or $task.Actions[0].Execute -ne $target) { throw ('Task differs from the companion registration script; not removed: ' + $name) } }; if (Test-Path -LiteralPath $link) { $shell = New-Object -ComObject WScript.Shell; $shortcut = $shell.CreateShortcut($link); if ($shortcut.TargetPath -ne $target) { throw ('Shortcut points to another program; not removed: ' + $link) } }; if ($task) { Unregister-ScheduledTask -TaskName $name -TaskPath '\' -Confirm:$false; Write-Host ('Removed task: ' + $name) } else { Write-Host 'No companion startup task found.' }; if (Test-Path -LiteralPath $link) { Remove-Item -LiteralPath $link -Force; Write-Host ('Removed shortcut: ' + $link) } else { Write-Host 'No companion startup shortcut found.' }; Write-Host 'Program files, settings, and the running application were not changed.'; exit 0 } catch { Write-Host $_.Exception.Message -ForegroundColor Red; exit 1 }"

if errorlevel 1 (
    echo Removal did not complete. See the message above.
    pause
    exit /b 1
)

echo Startup removal complete.
pause
exit /b 0
