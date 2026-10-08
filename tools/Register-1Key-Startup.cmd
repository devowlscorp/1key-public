@echo off
setlocal

rem Register a shortcut for the current user's Windows sign-in.
rem This does not grant administrator privileges or bypass UAC.
powershell.exe -NoLogo -NoProfile -Command "$ErrorActionPreference = 'Stop'; try { $target = Join-Path $env:LOCALAPPDATA 'Programs\1Key\1Key.exe'; if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw ('1Key executable not found: ' + $target) }; $startup = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup'; [void][System.IO.Directory]::CreateDirectory($startup); $link = Join-Path $startup '1Key.lnk'; if (Test-Path -LiteralPath $link) { throw ('Shortcut already exists. Remove it first if you want to replace it: ' + $link) }; $shell = New-Object -ComObject WScript.Shell; $shortcut = $shell.CreateShortcut($link); $shortcut.TargetPath = $target; $shortcut.WorkingDirectory = Split-Path -Parent $target; $shortcut.IconLocation = $target + ',0'; $shortcut.Description = 'Start 1Key at Windows sign-in'; $shortcut.Save(); Write-Host ('Created: ' + $link); exit 0 } catch { Write-Host $_.Exception.Message -ForegroundColor Red; exit 1 }"

if errorlevel 1 (
    echo Startup registration failed.
    pause
    exit /b 1
)

echo Startup registration complete.
pause
exit /b 0
