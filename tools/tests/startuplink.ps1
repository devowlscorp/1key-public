# Start with Windows = a shortcut in the user's Startup folder (0.2.149, Codex 2026-10-03 20:44 D1-D9, R-A1/R-A2/R-A3). No windows:
# runs the exe with --selftest-apply / --selftest-state / --selftest-writelink. The app works only in a TEST startup folder
# (ONEKEY_TEST_STARTUP_DIR, or <config dir>\startup.test when it is not given) with the test name "1Key.sl.lnk".
# The real Startup folder, the real Run value "1Key" and the real StartupApproved values are read before and after (SL20) and
# never written. The only things written outside the test folders are test-named values ("1Key.sl" under HKCU Run and
# "1Key.sl.lnk" under StartupApproved\StartupFolder), removed at the end.
#
# R-A1 (shortcut ownership, our own shortcut only):
# SL01 nothing there: state Missing
# SL02 on: OK, state Ours; the shortcut is the exe, "--tray", working folder = exe folder, icon = exe
# SL03 on again: OK and the file is not rewritten (same write time, same bytes)
# SL04 our exe with other arguments: state OursDiffers; on rewrites it (OK, Ours, "--tray")
# SL05 off: OK, the shortcut is gone; off again with nothing there: OK
# SL06 same name, another target (notepad): state Foreign; on and off both report a problem and keep it as it was
# SL07 same name, damaged file (random bytes): state Unknown; on and off both report a problem and keep the bytes
# SL08 same name, read denied (ACL): state Unknown; off reports a problem and the file stays
# SL09 same name is a folder: state Unknown; on reports a problem, the folder stays
# SL10 Task Manager "disabled" (StartupApproved first byte 3): on keeps the shortcut and the value, reports it (not OK)
# SL11 StartupApproved with an unknown format: on reports "could not check" (not OK), value kept
# SL12 an old-style Run value is still there: on reports it (not OK); legacy state Present; the app does not remove it
# R-A2 (test folder isolation, negative controls):
# SL13 ONEKEY_TEST_STARTUP_DIR = the real Startup folder: refused ("not isolated"), nothing written there
# SL14 ... = a folder under the real Startup folder: refused
# SL15 ... = a relative path: refused
# SL16 no ONEKEY_TEST_STARTUP_DIR: the shortcut goes to <config dir>\startup.test (not the real folder); off removes it
# R-A3 (installed copy only):
# SL17 the exe is not the installed copy (ONEKEY_TEST_INSTALL_PATH elsewhere): on reports "installed only", nothing created
# SL18 state reads from a non-installed copy do not create anything either
# SL19 the test folder holds nothing at the end
# SL20 the real Startup folder (names, sizes, write times), the real Run value "1Key" and the real StartupApproved values are unchanged
# Judgement: tools\tests\lib\Check.ps1. ASCII only.
param([string]$Exe = "")
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
$ids = @(); 1..20 | ForEach-Object { $ids += "SL{0:D2}" -f $_ }
Start-Checks -Required $ids

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = (Resolve-Path $(if ($Exe) { $Exe } else { Get-DefaultExe })).Path
$cfg = Join-Path $sp "startuplink_cfg"
$sd = Join-Path $sp "startuplink_dir"
foreach ($d in $cfg, $sd) { if (Test-Path $d) { Remove-Item $d -Recurse -Force -ErrorAction Ignore }; New-Item -ItemType Directory -Force $d | Out-Null }
$lnk = Join-Path $sd "1Key.sl.lnk"
$realStartup = [Environment]::GetFolderPath("Startup")
$runkey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$apkey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder"
$np = "$env:WINDIR\System32\notepad.exe"
$wsh = New-Object -ComObject WScript.Shell

function RealState() {
  $files = if (Test-Path $realStartup) { (Get-ChildItem -LiteralPath $realStartup -Force | Sort-Object Name | ForEach-Object { "$($_.Name):$($_.Length):$($_.LastWriteTimeUtc.Ticks)" }) -join ";" } else { "-" }
  $r = (Get-ItemProperty $runkey -Name "1Key" -ErrorAction Ignore)."1Key"
  $ap = Get-ItemProperty $apkey -ErrorAction Ignore
  $aps = if ($ap) { ($ap.PSObject.Properties | Where-Object { $_.Name -notlike "PS*" -and $_.Name -ne "1Key.sl.lnk" } | Sort-Object Name | ForEach-Object { "$($_.Name)=$([BitConverter]::ToString([byte[]]$_.Value))" }) -join ";" } else { "-" }
  "$files|$r|$aps"
}
# run one self-test command with the given environment; returns the text the app wrote
function Self([string]$cmd, [string[]]$more = @(), [hashtable]$envv = @{}) {
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = ".sl"
  $env:ONEKEY_TEST_STARTUP_DIR = $sd; $env:ONEKEY_TEST_INSTALL_PATH = $null
  foreach ($k in $envv.Keys) { Set-Item "env:$k" $envv[$k] }
  $out = Join-Path $cfg ($cmd.TrimStart('-') + ".txt")
  Remove-Item $out -Force -ErrorAction Ignore
  $argl = @($cmd) + ($more | ForEach-Object { "`"$_`"" })
  $p = Start-Process $exe -ArgumentList $argl -PassThru -WindowStyle Hidden
  if (-not $p.WaitForExit(30000)) { Stop-Process -Id $p.Id -Force -ErrorAction Ignore; return "TIMEOUT" }
  if (Test-Path $out) { [IO.File]::ReadAllText($out, [Text.Encoding]::UTF8) } else { "NOFILE" }
}
function Apply([string]$onoff, [hashtable]$envv = @{}) { Self "--selftest-apply" @($onoff) $envv }
function State([hashtable]$envv = @{}) { (Self "--selftest-state" @() $envv).Split('|')[0] }
function Lnk([string]$path = $lnk) { $s = $wsh.CreateShortcut($path); "$($s.TargetPath)|$($s.Arguments)|$($s.WorkingDirectory)|$($s.IconLocation)" }
function Bytes([string]$path) { [BitConverter]::ToString([IO.File]::ReadAllBytes($path)) }
function SetApproval([byte]$first) { if (-not (Test-Path $apkey)) { New-Item $apkey -Force | Out-Null }; $b = New-Object byte[] 12; $b[0] = $first; New-ItemProperty $apkey -Name "1Key.sl.lnk" -PropertyType Binary -Value $b -Force | Out-Null }
function ClearApproval() { Remove-ItemProperty $apkey -Name "1Key.sl.lnk" -ErrorAction Ignore }

$real0 = RealState
$exeDir = Split-Path -Parent $exe
try {
  Remove-ItemProperty $runkey -Name "1Key.sl" -ErrorAction Ignore; ClearApproval

  Check SL01 "Missing" (State) "nothing there: Missing"

  $r = Apply "on"
  Check SL02 "OK|Ours|$exe|--tray|$exeDir|$exe,0" "$r|$(State)|$(Lnk)" "on: our shortcut (exe, --tray, folder, icon)"

  $t0 = (Get-Item $lnk).LastWriteTimeUtc.Ticks; $b0 = Bytes $lnk
  Start-Sleep -Milliseconds 1100
  $r = Apply "on"
  Check SL03 "OK|True|True" "$r|$((Get-Item $lnk).LastWriteTimeUtc.Ticks -eq $t0)|$((Bytes $lnk) -eq $b0)" "on again: not rewritten"

  $w = Self "--selftest-writelink" @($exe, "--other")
  $st = State
  $r = Apply "on"
  Check SL04 "OK|OursDiffers|OK|Ours|--tray" "$w|$st|$r|$(State)|$(($wsh.CreateShortcut($lnk)).Arguments)" "our exe with other arguments: rewritten"

  $r1 = Apply "off"; $gone = -not (Test-Path $lnk); $r2 = Apply "off"
  Check SL05 "OK|True|OK" "$r1|$gone|$r2" "off removes it; off with nothing there is OK"

  $w = Self "--selftest-writelink" @($np, "")
  $st = State; $b0 = Bytes $lnk
  $r1 = Apply "on"; $r2 = Apply "off"
  Check SL06 "OK|Foreign|True|True|True" "$w|$st|$($r1 -ne 'OK')|$($r2 -ne 'OK')|$((Bytes $lnk) -eq $b0)" "same name, other target: kept, both report"
  Remove-Item $lnk -Force

  $rnd = New-Object byte[] 300; (New-Object Random 7).NextBytes($rnd); [IO.File]::WriteAllBytes($lnk, $rnd); $b0 = Bytes $lnk
  $st = State; $r1 = Apply "on"; $r2 = Apply "off"
  Check SL07 "Unknown|True|True|True" "$st|$($r1 -ne 'OK')|$($r2 -ne 'OK')|$((Bytes $lnk) -eq $b0)" "damaged file: kept, both report"
  Remove-Item $lnk -Force

  [void](Apply "on")
  $me = [Security.Principal.WindowsIdentity]::GetCurrent().Name
  & icacls $lnk /deny "${me}:(R)" | Out-Null
  $st = State; $r = Apply "off"; $still = Test-Path $lnk
  & icacls $lnk /remove:d "$me" | Out-Null
  Check SL08 "Unknown|True|True" "$st|$($r -ne 'OK')|$still" "read denied: kept, off reports"
  Remove-Item $lnk -Force -ErrorAction Ignore

  New-Item -ItemType Directory -Force $lnk | Out-Null
  $st = State; $r = Apply "on"
  Check SL09 "Unknown|True|True" "$st|$($r -ne 'OK')|$(Test-Path $lnk -PathType Container)" "same name is a folder: kept, on reports"
  Remove-Item $lnk -Recurse -Force

  [void](Apply "on"); $b0 = Bytes $lnk
  SetApproval 3
  $st = (Self "--selftest-state").Split('|')[1]; $r = Apply "on"
  $v = [BitConverter]::ToString([byte[]](Get-ItemProperty $apkey -Name "1Key.sl.lnk")."1Key.sl.lnk")
  Check SL10 "Disabled|True|True|03-00-00-00-00-00-00-00-00-00-00-00" "$st|$($r -ne 'OK')|$((Bytes $lnk) -eq $b0)|$v" "Task Manager disabled: kept as is, reported"

  SetApproval 9
  $st = (Self "--selftest-state").Split('|')[1]; $r = Apply "on"
  Check SL11 "Unknown|True|True" "$st|$($r -ne 'OK')|$((Get-ItemProperty $apkey -Name '1Key.sl.lnk')."1Key.sl.lnk"[0] -eq 9)" "unknown approval format: reported, value kept"
  ClearApproval

  Set-ItemProperty $runkey -Name "1Key.sl" -Value "`"$exe`" --tray"
  $lg = (Self "--selftest-state").Split('|')[2]; $r = Apply "on"
  $kept = [bool]((Get-ItemProperty $runkey -Name "1Key.sl" -ErrorAction Ignore)."1Key.sl")
  Remove-ItemProperty $runkey -Name "1Key.sl" -ErrorAction Ignore
  $lg2 = (Self "--selftest-state").Split('|')[2]
  Check SL12 "Present|True|True|None" "$lg|$($r -ne 'OK')|$kept|$lg2" "old Run value: reported, not removed by the app"
  [void](Apply "off")

  $before = RealState
  $r = Apply "on" @{ ONEKEY_TEST_STARTUP_DIR = $realStartup }
  Check SL13 "True|False|$before" "$($r -like '*not isolated*')|$(Test-Path (Join-Path $realStartup '1Key.sl.lnk'))|$(RealState)" "test dir = real Startup: refused"
  $r = Apply "on" @{ ONEKEY_TEST_STARTUP_DIR = (Join-Path $realStartup "sub") }
  Check SL14 "True|False" "$($r -like '*not isolated*')|$(Test-Path (Join-Path $realStartup 'sub'))" "test dir under real Startup: refused"
  $r = Apply "on" @{ ONEKEY_TEST_STARTUP_DIR = "rel\dir" }
  Check SL15 "True" "$($r -like '*not isolated*')" "relative test dir: refused"

  $fb = Join-Path $cfg "startup.test\1Key.sl.lnk"
  $r1 = Apply "on" @{ ONEKEY_TEST_STARTUP_DIR = "" }
  $made = Test-Path $fb
  $r2 = Apply "off" @{ ONEKEY_TEST_STARTUP_DIR = "" }
  Check SL16 "OK|True|OK|False|False" "$r1|$made|$r2|$(Test-Path $fb)|$(Test-Path (Join-Path $realStartup '1Key.sl.lnk'))" "no test dir given: config dir\startup.test"

  $other = Join-Path $sp "not-installed\1Key.exe"
  $r = Apply "on" @{ ONEKEY_TEST_INSTALL_PATH = $other }
  Check SL17 "True|False" "$($r -ne 'OK')|$(Test-Path $lnk)" "not the installed copy: refused, nothing created"
  $st = State @{ ONEKEY_TEST_INSTALL_PATH = $other }
  Check SL18 "Missing|False" "$st|$(Test-Path $lnk)" "state read from a non-installed copy creates nothing"
}
catch { Add-Failure ("exception: " + $_.Exception.Message) }
finally {
  Remove-ItemProperty $runkey -Name "1Key.sl" -ErrorAction Ignore; ClearApproval
  foreach ($v in "ONEKEY_TEST", "ONEKEY_CONFIG_DIR", "ONEKEY_INSTANCE_SUFFIX", "ONEKEY_TEST_STARTUP_DIR", "ONEKEY_TEST_INSTALL_PATH") { Remove-Item "env:$v" -ErrorAction Ignore }
}
Check SL19 "0" "$(@(Get-ChildItem $sd -Force -ErrorAction Ignore).Count)" "test folder empty at the end"
Check SL20 $real0 (RealState) "real Startup folder, Run value and StartupApproved unchanged"
Remove-Item $cfg, $sd -Recurse -Force -ErrorAction Ignore
Complete-Checks
