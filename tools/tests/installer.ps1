# Installer / uninstaller test (0.2.96, Codex 11:20). Uses a TEST VARIANT of installer\1Key.nsi built with /DTESTSUFFIX=.t, so every
# name gets ".t": install folder %LOCALAPPDATA%\Programs\1Key.t, Settings > Apps entry "1Key.t", start-menu shortcut 1Key.t.lnk,
# Run value "1Key.t", task "1Key_AutoStart.t", settings folder %LOCALAPPDATA%\1Key.t (a dummy file only). The real 1Key install,
# its settings, its Run value and its task are never touched (checked before and after: IN15). Silent (/S): no window, no focus.
#
#   powershell -ExecutionPolicy Bypass -File tools\tests\installer.ps1 [exe]
#
# IN01 the test setup is built from the exe (makensis, TESTSUFFIX=.t)
# IN02 silent install: 1Key.exe (same SHA-256 as the source) and Uninstall.exe in the install folder
# IN03 the Settings > Apps entry: name 1Key.t, the version, install location, uninstall command
# IN04 the start-menu shortcut exists; no desktop shortcut (silent install does not show the finish page)
# Round 1 - registrations that point exactly at the installed exe (Run value quoted with --tray, task action unquoted):
# IN05 silent uninstall removes the program files and the install folder
# IN06 ... the Settings > Apps entry and the shortcut
# IN07 ... the Run value
# IN08 ... the scheduled task
# IN09 ... but keeps the settings folder (silent uninstall never deletes settings)
# Round 2 - decoys that only CONTAIN the install path (Codex 11:20: exact comparison, not substring):
# IN10 a Run value "cmd.exe /c <installed exe>" is kept
# IN11 a task whose program is notepad.exe with the installed exe as an argument is kept
# Round 3:
# IN12 a Run value pointing at the installed exe in UPPER CASE is removed (paths compare case-insensitively)
# IN13 a reinstall over an existing install keeps the same folder and updates the entry (upgrade in place)
# IN14 everything with ".t" is gone at the end (cleanup)
# IN15 the real install (exe hash, Settings > Apps entry, Run value, task action) is the same before and after
# Old single-file copies (0.2.97, user decision: the setup moves them to the recycle bin; installer\cleanup-old.ps1, run on test folders):
# IN16 find: two copies of the old 0.2.21 exe (1Key.exe, "1Key (1).exe") are listed; a renamed other program (1Key-other.exe,
#      not product 1Key) and the kept install are not
# IN17 recycle: the listed copy goes to the recycle bin (gone from its folder); the other program and the kept install stay
# Run value migration (0.2.98, Codex 13:05: only a value that points at a real 1Key file is rewritten; anything else is kept):
# Since 0.2.149 the migration moves a 1Key Run value to the Startup-folder shortcut (Codex 2026-10-03 20:44 D4/D8/R-A4). The test build gets
# its own Startup folder (makensis /DTESTSTARTUP=<test folder>, shortcut 1Key.t.lnk); the real Startup folder is never written (IN15, IN29).
# "Old 1Key copy" = a copy of the tested exe in another folder (file info says 1Key; build\0.2.21 is not needed any more).
# Since 0.2.155 autostart is paused: the setup never moves, rewrites or creates a registration; existing ones are kept as they are
# (Codex 2026-10-04 07:51 6: test the current policy, keep the old move expectations only in git history for a later re-enable).
# IN18 Run value -> an old 1Key copy in another folder: kept as it was, no shortcut, the old file stays
# IN19 Run value -> notepad.exe (another program): kept as it was, notepad still there, no shortcut
# IN20 Run value -> a 1Key.exe path that does not exist: kept as it was, no shortcut
# IN21 Run value -> a "1Key.exe" with no version info (a text file): kept as it was, the file stays, no shortcut
# IN22 Run value -> notepad copied as "1Key.exe" (name says 1Key, file info does not): kept as it was, the file stays, no shortcut
# Startup-folder shortcut (0.2.149):
# IN23 an install with no old registration creates no shortcut
# IN24 Run value -> the installed exe (the 0.2.96-0.2.148 state): a reinstall keeps it as it was and makes no shortcut
# IN25 a task (made without admin rights, test name) -> the installed exe: a reinstall keeps the task as it was, no shortcut
# IN26 a same-name shortcut that points at notepad + a 1Key Run value: the shortcut is kept and so is the Run value (never overwritten)
# IN27 uninstall removes our shortcut (one left by 0.2.150-0.2.154: made by the test here, installed exe --tray)
# IN28 uninstall keeps a same-name shortcut that points elsewhere
# IN29 a test setup built with TESTSTARTUP = the real Startup folder refuses to run (nothing installed, nothing in the real folder)
# Autostart targets kept while autostart is paused (0.2.159, Codex R157-1; -Protect stands in for the registrations):
# IN30 find skips the file a registration points at, lists an unrelated old copy
# IN31 recycle re-checks right before moving: a stale list naming the target leaves it in place, the unrelated copy moves
# IN32 an unsure registration lookup ("?"): find writes "hold", recycle moves nothing
# IN33 Run commands through the real parser (quoted / unquoted + argument) protect their target; a missing target file is still kept
# IN34 a target that cannot be resolved (unclosed quote, no .exe, glued argument, relative, empty, bad path, unquoted path with a space
#      - also after expanding %ProgramFiles%; Codex 08:10) holds the whole cleanup
# Not covered here: an administrator task that a normal-rights setup cannot delete (needs elevation; checked by hand in the VM, D9).
# Judgement: tools\tests\lib\Check.ps1. ASCII only.
param([string]$Exe = "")
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
$ids = @(); 1..34 | ForEach-Object { $ids += "IN{0:D2}" -f $_ }
Start-Checks -Required $ids

$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$exe = (Resolve-Path $(if ($Exe) { $Exe } else { Get-DefaultExe })).Path
$ver = ((Get-Item $exe).VersionInfo.ProductVersion -split '\+')[0]   # "0.2.150+<commit>" when built in a git checkout
$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$setup = Join-Path $sp "1Key-Setup.t.exe"
$inst = Join-Path $env:LOCALAPPDATA "Programs\1Key.t"
$iexe = Join-Path $inst "1Key.exe"
$cfg = Join-Path $env:LOCALAPPDATA "1Key.t"
$unkey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\1Key.t"
$runkey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$lnk = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\1Key.t.lnk"
$desk = Join-Path ([Environment]::GetFolderPath("Desktop")) "1Key.t.lnk"
$task = "1Key_AutoStart.t"
$sdir = Join-Path $sp "installer_startup"                  # the test build's Startup folder (never the real one)
$slnk = Join-Path $sdir "1Key.t.lnk"
$realStartup = [Environment]::GetFolderPath("Startup")
$wsh = New-Object -ComObject WScript.Shell

function RunValue() { (Get-ItemProperty $runkey -Name "1Key.t" -ErrorAction Ignore)."1Key.t" }
# the test Startup shortcut: "target|arguments", or "" when there is none
function SLnk() { if (Test-Path $slnk) { $s = $wsh.CreateShortcut($slnk); "$($s.TargetPath)|$($s.Arguments)" } else { "" } }
function ForeignLnk() { New-Item -ItemType Directory -Force $sdir | Out-Null; $s = $wsh.CreateShortcut($slnk); $s.TargetPath = "$env:WINDIR\System32\notepad.exe"; $s.Save() }
# how the installed 1Key itself reads the shortcut (test mode, the same test Startup folder): Missing / Ours / OursDiffers / Foreign / Unknown
function AppState() {
  $c = Join-Path $sp "installer_appstate"; New-Item -ItemType Directory -Force $c | Out-Null
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $c; $env:ONEKEY_INSTANCE_SUFFIX = ".t"; $env:ONEKEY_TEST_STARTUP_DIR = $sdir; $env:ONEKEY_TEST_INSTALL_PATH = $iexe
  try {
    $p = Start-Process $iexe -ArgumentList "--selftest-state" -PassThru -WindowStyle Hidden
    if (-not $p.WaitForExit(30000)) { Stop-Process -Id $p.Id -Force -ErrorAction Ignore; return "TIMEOUT" }
    $f = Join-Path $c "selftest-state.txt"
    if (Test-Path $f) { ([IO.File]::ReadAllText($f)).Split('|')[0] } else { "NOFILE" }
  } finally {
    foreach ($v in "ONEKEY_TEST", "ONEKEY_CONFIG_DIR", "ONEKEY_INSTANCE_SUFFIX", "ONEKEY_TEST_STARTUP_DIR", "ONEKEY_TEST_INSTALL_PATH") { Remove-Item "env:$v" -ErrorAction Ignore }
    Remove-Item $c -Recurse -Force -ErrorAction Ignore
  }
}
function TaskAction() { $t = Get-ScheduledTask -TaskName $task -ErrorAction Ignore; if ($t) { "$($t.Actions[0].Execute)|$($t.Actions[0].Arguments)" } else { "" } }
function NewTask([string]$program, [string]$arguments) {
  $a = if ($arguments) { New-ScheduledTaskAction -Execute $program -Argument $arguments } else { New-ScheduledTaskAction -Execute $program }
  $t = New-ScheduledTaskTrigger -Once -At (Get-Date).AddYears(30)
  [void](Register-ScheduledTask -TaskName $task -Action $a -Trigger $t -Force)
}
function Install() { Start-Process $setup -ArgumentList "/S" -Wait; Start-Sleep -Milliseconds 300 }
# the uninstaller copies itself to TEMP and returns at once: wait until the install folder is gone
function Uninstall() {
  if (-not (Test-Path (Join-Path $inst "Uninstall.exe"))) { return $false }
  Start-Process (Join-Path $inst "Uninstall.exe") -ArgumentList "/S" -Wait
  for ($i = 0; $i -lt 100 -and (Test-Path $inst); $i++) { Start-Sleep -Milliseconds 200 }
  Start-Sleep -Milliseconds 500
  -not (Test-Path $inst)
}
function CleanAll() {
  Remove-ItemProperty $runkey -Name "1Key.t" -ErrorAction Ignore
  Unregister-ScheduledTask -TaskName $task -Confirm:$false -ErrorAction Ignore
  if (Test-Path (Join-Path $inst "Uninstall.exe")) { [void](Uninstall) }
  foreach ($p in $inst, $cfg) { if (Test-Path $p) { Remove-Item $p -Recurse -Force -ErrorAction Ignore } }
  foreach ($p in $lnk, $desk, $slnk) { if (Test-Path $p) { Remove-Item $p -Force -ErrorAction Ignore } }
  if (Test-Path $unkey) { Remove-Item $unkey -Recurse -Force -ErrorAction Ignore }
}
# the real install, read only
function RealState() {
  $re = Join-Path $env:LOCALAPPDATA "Programs\1Key\1Key.exe"
  $h = if (Test-Path $re) { (Get-FileHash $re -Algorithm SHA256).Hash } else { "-" }
  $u = Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\1Key" -ErrorAction Ignore
  $r = (Get-ItemProperty $runkey -Name "1Key" -ErrorAction Ignore)."1Key"
  $t = Get-ScheduledTask -TaskName "1Key_AutoStart" -ErrorAction Ignore
  $sf = if (Test-Path $realStartup) { (Get-ChildItem -LiteralPath $realStartup -Force | Sort-Object Name | ForEach-Object { "$($_.Name):$($_.LastWriteTimeUtc.Ticks)" }) -join ";" } else { "-" }
  "$h|$($u.DisplayVersion)|$($u.InstallLocation)|$r|$(if ($t) { $t.Actions[0].Execute } else { '-' })|$sf"
}
function BuildSetup([string]$out, [string]$startDir) {
  if (Test-Path $out) { Remove-Item $out -Force }
  if ($nsis) { & $nsis -V1 "-DVERSION=$ver" "-DTESTSUFFIX=.t" "-DTESTSTARTUP=$startDir" "-DSRC=$exe" "-DOUT=$out" ("-DICON=" + (Join-Path $repo "src\OneKey\app.ico")) (Join-Path $repo "installer\1Key.nsi") | Out-Null }
}

$real0 = RealState
try {
  CleanAll
  $nsis = @("${env:ProgramFiles(x86)}\NSIS\makensis.exe", "$env:ProgramFiles\NSIS\makensis.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
  BuildSetup $setup $sdir
  Check IN01 "True" "$([bool]($nsis -and (Test-Path $setup)))" "test setup built ($setup)"

  Install
  Check IN23 "" (SLnk) "an install with no old registration creates no Startup shortcut"
  $srcHash = (Get-FileHash $exe -Algorithm SHA256).Hash
  $okFiles = (Test-Path $iexe) -and (Test-Path (Join-Path $inst "Uninstall.exe")) -and ((Get-FileHash $iexe -Algorithm SHA256).Hash -eq $srcHash)
  Check IN02 "True" "$okFiles" "silent install: 1Key.exe (same hash) and Uninstall.exe in $inst"
  $u = Get-ItemProperty $unkey -ErrorAction Ignore
  Check IN03 "1Key.t|$ver|$inst|True" "$($u.DisplayName)|$($u.DisplayVersion)|$($u.InstallLocation)|$($u.UninstallString -like '*Uninstall.exe*')" "Settings > Apps entry"
  Check IN04 "True|False" "$(Test-Path $lnk)|$(Test-Path $desk)" "start-menu shortcut yes, desktop shortcut no (silent)"

  # round 1: exact registrations
  New-Item -ItemType Directory -Force $cfg | Out-Null; Set-Content (Join-Path $cfg "dummy.txt") "not a real setting" -Encoding ASCII
  Set-ItemProperty $runkey -Name "1Key.t" -Value "`"$iexe`" --tray"
  NewTask $iexe ""
  $pre1 = "$([bool](RunValue))|$([bool](TaskAction))"
  $gone = Uninstall
  Check IN05 "True|True|True" "$pre1|$gone" "silent uninstall removed the program files and the folder (registrations were present: Run/task)"
  Check IN06 "False|False" "$(Test-Path $unkey)|$(Test-Path $lnk)" "Settings > Apps entry and shortcut removed"
  Check IN07 "" "$(RunValue)" "the Run value pointing exactly at the installed exe was removed"
  Check IN08 "" "$(TaskAction)" "the task whose program is exactly the installed exe was removed"
  Check IN09 "True" "$(Test-Path (Join-Path $cfg 'dummy.txt'))" "the settings folder was kept (silent uninstall)"

  # round 2: decoys that only contain the path
  Install
  Set-ItemProperty $runkey -Name "1Key.t" -Value "`"C:\Windows\System32\cmd.exe`" /c `"$iexe`""
  NewTask "C:\Windows\System32\notepad.exe" "`"$iexe`""
  [void](Uninstall)
  Check IN10 "True" "$((RunValue) -like '*cmd.exe*')" "a Run value that only contains the installed path was kept ($(RunValue))"
  Check IN11 "True" "$((TaskAction) -like '*notepad.exe*')" "a task whose program is another exe was kept ($(TaskAction))"
  Remove-ItemProperty $runkey -Name "1Key.t" -ErrorAction Ignore
  Unregister-ScheduledTask -TaskName $task -Confirm:$false -ErrorAction Ignore

  # round 3: case, upgrade in place
  Install
  Install                                                    # a second install over the first (upgrade)
  $u2 = Get-ItemProperty $unkey -ErrorAction Ignore
  Check IN13 "True|$inst|True" "$(Test-Path $iexe)|$($u2.InstallLocation)|$(@(Get-ChildItem (Split-Path $inst) -Directory -Filter '1Key.t*').Count -eq 1)" "reinstall over an install: same folder, entry updated"
  Set-ItemProperty $runkey -Name "1Key.t" -Value ("`"" + $iexe.ToUpperInvariant() + "`" --tray")   # set after the installs (an install would move it)
  [void](Uninstall)
  Check IN12 "" "$(RunValue)" "a Run value with the installed path in upper case was removed"

  # IN18-IN22: the setup keeps every Run value as it was and makes no shortcut (0.2.155 policy)
  $rd = Join-Path $sp "runold"; if (Test-Path $rd) { Remove-Item $rd -Recurse -Force }
  New-Item -ItemType Directory -Force (Join-Path $rd "a"), (Join-Path $rd "b"), (Join-Path $rd "c") | Out-Null
  $r1 = Join-Path $rd "a\1Key.exe"; Copy-Item $exe $r1          # an "old" 1Key copy in another folder (file info 1Key)
  $r4 = Join-Path $rd "b\1Key.exe"; Set-Content $r4 -Value "not a program" -Encoding ASCII
  $r5 = Join-Path $rd "c\1Key.exe"; Copy-Item "$env:WINDIR\System32\notepad.exe" $r5
  $np = "$env:WINDIR\System32\notepad.exe"
  # expected: "<Run value after>|<shortcut target|arguments, or empty>"
  $cases = @(
    @("IN18", "`"$r1`" --tray", "`"$r1`" --tray|", $r1, "old 1Key copy in another folder: kept, no shortcut (autostart paused)"),
    @("IN19", "`"$np`"", "`"$np`"|", $np, "notepad.exe: kept, no shortcut"),
    @("IN20", "`"C:\Old place\1Key.exe`" --tray", "`"C:\Old place\1Key.exe`" --tray|", "", "missing 1Key.exe path: kept, no shortcut"),
    @("IN21", "`"$r4`" --tray", "`"$r4`" --tray|", $r4, "1Key.exe without version info: kept, no shortcut"),
    @("IN22", "`"$r5`" --tray", "`"$r5`" --tray|", $r5, "notepad copied as 1Key.exe: kept, no shortcut"))
  foreach ($c in $cases) {
    Set-ItemProperty $runkey -Name "1Key.t" -Value $c[1]
    Install
    $after = "$(RunValue)|$(SLnk)"
    $file = if ($c[3]) { "$(Test-Path -LiteralPath $c[3])" } else { "-" }
    Check $c[0] "$($c[2])|$(if ($c[3]) { 'True' } else { '-' })" "$after|$file" "$($c[4]) (target file still there)"
    [void](Uninstall)
    Remove-ItemProperty $runkey -Name "1Key.t" -ErrorAction Ignore
    Remove-Item $slnk -Force -ErrorAction Ignore
  }
  Remove-Item $rd -Recurse -Force -ErrorAction Ignore

  # IN24: Run value -> the installed exe (what 0.2.96-0.2.148 wrote): moved, and the installed 1Key reads the shortcut as its own
  Install
  Set-ItemProperty $runkey -Name "1Key.t" -Value "`"$iexe`" --tray"
  Install
  Check IN24 "`"$iexe`" --tray|" "$(RunValue)|$(SLnk)" "Run value -> installed exe: a reinstall keeps it, no shortcut"

  # IN27: uninstall removes our shortcut (and the Run value that points exactly at the install, IN07 rule)
  New-Item -ItemType Directory -Force $sdir | Out-Null; $s = $wsh.CreateShortcut($slnk); $s.TargetPath = $iexe; $s.Arguments = "--tray"; $s.Save()
  [void](Uninstall)
  Check IN27 "False" "$(Test-Path $slnk)" "uninstall removed our Startup shortcut"

  # IN25: a task (normal rights, test name) -> the installed exe: moved
  Install
  NewTask $iexe "--tray"
  $pre = TaskAction
  Install
  Check IN25 "True|$pre|" "$([bool]$pre)|$(TaskAction)|$(SLnk)" "task -> installed exe: a reinstall keeps the task, no shortcut"
  [void](Uninstall)
  Unregister-ScheduledTask -TaskName $task -Confirm:$false -ErrorAction Ignore

  # IN26: a same-name shortcut pointing at notepad is never overwritten; the Run value stays because the move could not be confirmed
  $r1 = Join-Path $sp "runold2\1Key.exe"; New-Item -ItemType Directory -Force (Split-Path $r1) | Out-Null; Copy-Item $exe $r1
  ForeignLnk
  $fl = SLnk                                                 # as stored (the shell may change the path's case)
  Set-ItemProperty $runkey -Name "1Key.t" -Value "`"$r1`" --tray"
  Install
  Check IN26 "$fl|`"$r1`" --tray" "$(SLnk)|$(RunValue)" "foreign same-name shortcut kept, Run value kept"

  # IN28: uninstall keeps the foreign shortcut
  [void](Uninstall)
  Check IN28 "$fl" "$(SLnk)" "uninstall kept the same-name shortcut that points elsewhere"
  Remove-ItemProperty $runkey -Name "1Key.t" -ErrorAction Ignore
  Remove-Item $slnk -Force -ErrorAction Ignore
  Remove-Item (Split-Path $r1) -Recurse -Force -ErrorAction Ignore

  # IN29: a test setup whose Startup folder is the real one refuses to run
  $bad = Join-Path $sp "1Key-Setup.t-real.exe"
  $realBefore = RealState
  BuildSetup $bad $realStartup
  $built = Test-Path $bad
  if ($built) { Start-Process $bad -ArgumentList "/S" -Wait; Start-Sleep -Milliseconds 300 }
  Check IN29 "True|False|False" "$built|$(Test-Path $iexe)|$(Test-Path (Join-Path $realStartup '1Key.t.lnk'))" "TESTSTARTUP = real Startup folder: the setup stops (nothing installed, nothing in the real folder)"
  Remove-Item $bad -Force -ErrorAction Ignore

  # IN16/IN17: the old-copy finder and the recycle step on test folders only
  $cl = Join-Path $sp "cleanup"; if (Test-Path $cl) { Remove-Item $cl -Recurse -Force }
  $ca = Join-Path $cl "a"; $cb = Join-Path $cl "b"; New-Item -ItemType Directory -Force $ca, $cb | Out-Null
  $old = $exe                                              # "old" copies = copies of the tested exe (file info 1Key)
  Copy-Item $old (Join-Path $ca "1Key.exe"); Copy-Item $old (Join-Path $ca "1Key (1).exe")
  Copy-Item "$env:WINDIR\System32\notepad.exe" (Join-Path $ca "1Key-other.exe"); Copy-Item $exe (Join-Path $cb "1Key.exe")
  $ps1 = Join-Path $repo "installer\cleanup-old.ps1"; $list = Join-Path $cl "old.txt"
  & powershell -NoProfile -ExecutionPolicy Bypass -File $ps1 -Keep (Join-Path $cb "1Key.exe") -Out $list -Dirs "$ca;$cb" | Out-Null
  $lines = @(Get-Content $list -Encoding Unicode)
  $names = ($lines | Select-Object -Skip 1 | ForEach-Object { Split-Path $_ -Leaf } | Sort-Object) -join ","
  Check IN16 "2|1Key (1).exe,1Key.exe" "$($lines[0])|$names" "old copies found, other program and kept install not"
  Set-Content $list -Value @("1", (Join-Path $ca "1Key (1).exe")) -Encoding Unicode   # recycle only one copy (keeps the bin small)
  $moved = & powershell -NoProfile -ExecutionPolicy Bypass -File $ps1 -Keep (Join-Path $cb "1Key.exe") -Recycle $list -Dirs "$ca;$cb"
  Check IN17 "1|False|True|True|True" "$(($moved | Select-Object -Last 1).Trim())|$(Test-Path (Join-Path $ca '1Key (1).exe'))|$(Test-Path (Join-Path $ca '1Key.exe'))|$(Test-Path (Join-Path $ca '1Key-other.exe'))|$(Test-Path (Join-Path $cb '1Key.exe'))" "the listed copy went to the recycle bin; the others stayed"

  # IN30-IN32 (0.2.159, Codex R157-1): a file an autostart registration points at is never listed or recycled; an unsure lookup holds everything.
  # -Protect stands in for the real Run value / task / Startup shortcut (test folders only; real registrations are not read or touched).
  Copy-Item $old (Join-Path $ca "1Key (2).exe")
  $reg = Join-Path $ca "1Key.exe"
  & powershell -NoProfile -ExecutionPolicy Bypass -File $ps1 -Keep (Join-Path $cb "1Key.exe") -Out $list -Dirs "$ca;$cb" -Protect $reg | Out-Null
  $lines = @(Get-Content $list -Encoding Unicode)
  $names = ($lines | Select-Object -Skip 1 | ForEach-Object { Split-Path $_ -Leaf } | Sort-Object) -join ","
  Check IN30 "1|1Key (2).exe" "$($lines[0])|$names" "find: the registered target is not listed, an unrelated old copy is"
  Set-Content $list -Value @("2", $reg, (Join-Path $ca "1Key (2).exe")) -Encoding Unicode   # a stale list that still names the target
  $moved = & powershell -NoProfile -ExecutionPolicy Bypass -File $ps1 -Keep (Join-Path $cb "1Key.exe") -Recycle $list -Dirs "$ca;$cb" -Protect $reg
  Check IN31 "1|True|False" "$(($moved | Select-Object -Last 1).Trim())|$(Test-Path $reg)|$(Test-Path (Join-Path $ca '1Key (2).exe'))" "recycle re-checks: the registered target stays, only the unrelated copy moved"
  & powershell -NoProfile -ExecutionPolicy Bypass -File $ps1 -Keep (Join-Path $cb "1Key.exe") -Out $list -Dirs "$ca;$cb" -Protect "?" | Out-Null
  $hold = (@(Get-Content $list -Encoding Unicode))[0]
  Set-Content $list -Value @("1", $reg) -Encoding Unicode
  $moved = & powershell -NoProfile -ExecutionPolicy Bypass -File $ps1 -Keep (Join-Path $cb "1Key.exe") -Recycle $list -Dirs "$ca;$cb" -Protect "?"
  Check IN32 "hold|0|True" "$hold|$(($moved | Select-Object -Last 1).Trim())|$(Test-Path $reg)" "unsure registration lookup: find holds, recycle moves nothing"

  # IN33/IN34 (0.2.161, Codex R160-1): a Run command goes through the real parser. Called in-process (a -File call would strip the quotes).
  $other = Join-Path $ca "1Key (3).exe"; Copy-Item $old $other
  $res = foreach ($v in @(('run:"' + $reg + '" --tray'), ('run:' + $reg + ' --tray'), ($ca + '\gone.exe'))) {
    & $ps1 -Keep (Join-Path $cb "1Key.exe") -Out $list -Dirs "$ca;$cb" -Protect $v | Out-Null
    (@(Get-Content $list -Encoding Unicode)[0])
  }
  Check IN33 "1|1|2" ($res -join "|") "quoted / unquoted Run command protects its target; a well-formed path to a missing file is kept as a target (nothing else held)"
  $res = foreach ($v in @(('run:"' + $reg + ' --tray'), ('run:' + $ca + '\1Key --tray'), ('run:"' + $reg + '"--tray'), 'run:1Key.exe', 'run:', ($ca + '\1Key<>.exe'), 'run:C:\Program Files\1Key\1Key.exe --tray', 'run:%ProgramFiles%\1Key\1Key.exe --tray')) {
    & $ps1 -Keep (Join-Path $cb "1Key.exe") -Out $list -Dirs "$ca;$cb" -Protect $v | Out-Null
    (@(Get-Content $list -Encoding Unicode)[0])
  }
  Check IN34 "hold|hold|hold|hold|hold|hold|hold|hold" ($res -join "|") "unclosed quote / no .exe / glued argument / relative / empty / bad path / unquoted path with a space (literal, after %ProgramFiles%): the whole cleanup holds"
  Remove-Item $cl -Recurse -Force -ErrorAction Ignore
}
catch { Add-Failure ("exception: " + $_.Exception.Message) }
finally {
  CleanAll
  if (Test-Path $setup) { Remove-Item $setup -Force -ErrorAction Ignore }
}
$left = @($inst, $cfg, $lnk, $desk, $slnk) | Where-Object { Test-Path $_ }
Remove-Item $sdir -Recurse -Force -ErrorAction Ignore
Check IN14 "0||" "$($left.Count)|$(RunValue)|$(TaskAction)" "nothing with .t left ($($left -join ', '))"
Check IN15 $real0 (RealState) "the real install, its entry, Run value and task are unchanged"
Complete-Checks
