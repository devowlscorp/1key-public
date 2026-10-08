# Backup / restore window test (0.3.45; Codex 17:49 test step 2, 18:20 T44-1..4). Dummy settings in a fresh test folder only.
#  A: make a backup (wrong current master; back / lock while the key computation really runs; success)
#  B: first-setup restore from the tray command (wrong backup password, success, new master after a restart).
#     A is quit first: one test suffix = one single-instance mutex (T44-1), A is started again afterwards.
#  A: restore over existing settings - the pre-backup question (lock / Esc key message / Yes), lock during the overwrite
#     question, lock / back while the computation really runs: the result is discarded, nothing published
#  A: unfinished publish (the launch.dat move kept failing): recovery-only state - hotkeys, unlock, tray, commands and the
#     session-lock notice do nothing (call counters unchanged); retry after the fault is gone finishes the new pair
#  A: unfinished publish + quit, start with the fault (startup box: cancel quits without reading), start again, retry
#  Since 0.3.6x a restore from an unlocked session keeps the current master (user decision 2026-10-05): no new-master fields,
#     the restored settings open with the master in use before (BW31, BW32 = Codex 23:06 R68-1 path, BW22/26/30 expectations).
#  C: backup without a program list (damaged launch.dat): restore asks keep / empty / cancel; cancel and lock publish nothing;
#     "empty" leaves a valid list with 0 items
# What this does NOT prove: the real Windows file dialog (ONEKEY_TEST_PICK answers it), a physical Esc key (a WM_KEYDOWN Esc is
# posted to the box's focused button - the box's own key path), a real Win+L (a WM_WTSSESSION_CHANGE lock notice is posted),
# another Windows account / PC.
# Test hooks (ONEKEY_TEST=1 only): ONEKEY_TEST_PICK (file dialog answer read from a text file), ONEKEY_TEST_KDF_DELAY
# (wait before every key computation so back / lock can land while it runs), <config dir>\test-fail-move (launch.dat move
# fails), window properties OneKeyTestKdfStarted / Done / Discarded / Failed (key computations started / applied / dropped / threw).
# Judgement: tools\tests\lib\Check.ps1. ASCII only.
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
$ids = @(); 1..32 | ForEach-Object { $ids += "BW{0:D2}" -f $_ }
Start-Checks -Required $ids

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($args.Count -gt 0) { $args[0] } else { Get-DefaultExe }
$suffix = ".bk"

# T44-4: a fresh folder for this run only, inside the test root; nothing that existed before is deleted
$base = [IO.Path]::GetFullPath($sp).TrimEnd('\')
$real = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA "1Key")).TrimEnd('\')
if ($base -eq $real -or $base.StartsWith($real + "\", [StringComparison]::OrdinalIgnoreCase)) { throw "test root is inside the real settings folder: $base" }
for ($d = $base; $d -and $d.Length -gt 3; $d = [IO.Path]::GetDirectoryName($d)) {
  if ((Get-Item -LiteralPath $d -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "test root path has a link: $d" }
}
$root = Join-Path $base ("backup-{0:yyyyMMdd-HHmmss}-{1}" -f (Get-Date), $PID)
if (Test-Path -LiteralPath $root) { throw "run folder already exists: $root" }
New-Item -ItemType Directory $root -ErrorAction Stop | Out-Null
"run folder: $root"
$cfgA = "$root\a"; $cfgB = "$root\b"; $cfgC = "$root\c"; $files = "$root\files"
foreach ($d in @($cfgA, $cfgB, $cfgC, $files)) { New-Item -ItemType Directory $d | Out-Null }
$pick = "$root\pick.txt"
$bak1 = "$files\one.1keybak"; $bak2 = "$files\nolist.1keybak"; $bak3 = "$files\before-restore.1keybak"
Add-Type -AssemblyName System.Security
Add-Type @'
using System;using System.Runtime.InteropServices;using System.Text;
public class BkU {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, StringBuilder l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr GetPropW(IntPtr h, string name);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int hh, uint f);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int l, t, r, b; }
  [StructLayout(LayoutKind.Sequential)] public struct GUITHREADINFO { public int cbSize; public int flags; public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret; public RECT rcCaret; }
  [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint tid, ref GUITHREADINFO g);
  public static IntPtr Focus(IntPtr w) { uint pid; uint tid = GetWindowThreadProcessId(w, out pid); var g = new GUITHREADINFO(); g.cbSize = Marshal.SizeOf(g); return GetGUIThreadInfo(tid, ref g) ? g.hwndFocus : IntPtr.Zero; }
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static string Text(IntPtr h) { var t = new StringBuilder(2048); GetWindowTextW(h, t, 2048); return t.ToString(); }
  public static IntPtr FindCls(uint pid, string cls) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
  public static IntPtr FindBox(uint pid) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && (Cls(h) == "#32770" || Cls(h) == "OneKeyDialog") && IsWindowVisible(h)) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
}
'@
$env:ONEKEY_TEST = "1"; $env:ONEKEY_INSTANCE_SUFFIX = $suffix
$env:ONEKEY_TEST_LANG = "en"           # the box texts below are matched in English
$env:ONEKEY_TEST_PICK = $pick
$env:ONEKEY_TEST_KDF_DELAY = "2500"
$Z = [IntPtr]::Zero

function SetPick($path) { [IO.File]::WriteAllText($pick, $path) }
function SetText($id, $s) { [void][BkU]::SendMessageW([BkU]::GetDlgItem($m, $id), 0x000C, $Z, [string]$s) }
function GetEdit($id) { $sb = New-Object System.Text.StringBuilder 512; [void][BkU]::SendMessageW([BkU]::GetDlgItem($m,$id), 0x000D, [IntPtr]512, $sb); $sb.ToString() }
function Click($id) { [void][BkU]::PostMessageW([BkU]::GetDlgItem($m, $id), 0x00F5, $Z, $Z); Start-Sleep -Milliseconds 700 }
function Has($id) { [BkU]::GetDlgItem($m, $id) -ne $Z }
function Prop($n) { [int64][BkU]::GetPropW($m, $n) }
function Post($msg, $w, $l = 0) { [void][BkU]::PostMessageW($m, $msg, [IntPtr]$w, [IntPtr]$l) }
function LockNotice() { Post 0x02B1 7; Start-Sleep -Milliseconds 900 }   # synthetic WM_WTSSESSION_CHANGE / WTS_SESSION_LOCK (not a real Win+L)
function NoBox() { [BkU]::FindBox([uint32]$p.Id) -eq $Z }
function BoxText($b) {
  if ($b -eq $Z) { return "" }
  if ([BkU]::Cls($b) -eq "#32770") { return [BkU]::Text([BkU]::GetDlgItem($b, 0xFFFF)) }   # system message box body
  [BkU]::Text([BkU]::GetDlgItem($b, 101))                                                  # 1Key dialog body (Dialog.IdBody)
}
# T44-3: wait for a box and answer only the expected one (its body must match); anything else is recorded and left alone
$script:lastBox = ""
function Expect($pattern, $ms = 15000) {
  $t = [Environment]::TickCount
  while ([Environment]::TickCount - $t -lt $ms) {
    $b = [BkU]::FindBox([uint32]$p.Id)
    if ($b -ne $Z) { Start-Sleep -Milliseconds 250; $script:lastBox = BoxText $b; if ($script:lastBox -match $pattern) { return $b }; [void](Add-Failure "unexpected box (wanted /$pattern/): $($script:lastBox -replace '\s+', ' ')"); return $Z }
    if ($p.HasExited) { $script:lastBox = "(process exited)"; return $Z }
    Start-Sleep -Milliseconds 100
  }
  $script:lastBox = "(no box)"; $Z
}
function Answer($b, $cmd) { if ($b -ne $Z) { [void][BkU]::PostMessageW($b, 0x0111, [IntPtr]$cmd, $Z); Start-Sleep -Milliseconds 500 } }
function EscKey($b) { if ($b -ne $Z) { $f = [BkU]::Focus($b); if ($f -eq $Z) { $f = $b }; [void][BkU]::PostMessageW($f, 0x0100, [IntPtr]0x1B, [IntPtr]1); Start-Sleep -Milliseconds 500 } }
function WaitHas($id, $ms = 15000) { $t = [Environment]::TickCount; while ([Environment]::TickCount - $t -lt $ms) { if (Has $id) { Start-Sleep -Milliseconds 300; return $true }; if (-not (NoBox)) { return $false }; Start-Sleep -Milliseconds 100 }; $false }
function Hash($f) { if (Test-Path -LiteralPath $f) { (Get-FileHash -LiteralPath $f -Algorithm SHA256).Hash } else { "none" } }
function Marker($cfg) { Test-Path -LiteralPath "$cfg\restore.commit" }
# T44-2: key computations started / applied / discarded / threw (test-only window properties set on the UI thread)
function Kdf() { [pscustomobject]@{ S = (Prop "OneKeyTestKdfStarted"); D = (Prop "OneKeyTestKdfDone"); X = (Prop "OneKeyTestKdfDiscarded"); F = (Prop "OneKeyTestKdfFailed") } }
function Running($k0) { $k = Kdf; ($k.S -eq $k0.S + 1) -and ($k.D + $k.X + $k.F -eq $k0.D + $k0.X + $k0.F) }
function WaitKdfEnd($k0, $ms = 15000) { $t = [Environment]::TickCount; while ([Environment]::TickCount - $t -lt $ms) { $k = Kdf; if ($k.D + $k.X + $k.F -gt $k0.D + $k0.X + $k0.F) { Start-Sleep -Milliseconds 400; return }; Start-Sleep -Milliseconds 100 } }
function KdfDelta($k0) { $k = Kdf; "started+$($k.S - $k0.S) done+$($k.D - $k0.D) dropped+$($k.X - $k0.X) threw+$($k.F - $k0.F)" }
function LaunchText($cfg) { try { [Text.Encoding]::UTF8.GetString([Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes("$cfg\launch.dat"), [Text.Encoding]::UTF8.GetBytes("1Key/launch-list/v1"), 'CurrentUser')) } catch { "(unreadable)" } }
function Launch($cfg) {
  $env:ONEKEY_CONFIG_DIR = $cfg
  $script:p = Start-Process $exe -PassThru
  WaitMain
}
function WaitMain() {
  $script:m = $Z
  for ($i = 0; $i -lt 100; $i++) { $script:m = [BkU]::FindCls([uint32]$script:p.Id, "OneKeyMainWindow$suffix"); if ($script:m -ne $Z) { break }; if ($script:p.HasExited) { break }; Start-Sleep -Milliseconds 100 }
  if ($script:m -eq $Z) { throw "no main window (exited=$($script:p.HasExited))" }
  Start-Sleep -Milliseconds 600
  [void][BkU]::SetWindowPos($script:m, $Z, 1400, 60, 0, 0, 0x0001 -bor 0x0004 -bor 0x0010)
}
function Quit() { Post 0x8005 0; for ($i = 0; $i -lt 80 -and -not $p.HasExited; $i++) { Start-Sleep -Milliseconds 100 }; if (-not $p.HasExited) { throw "setup: test instance pid $($p.Id) did not exit within 8 s" } }
function WaitExit() { for ($i = 0; $i -lt 80 -and -not $p.HasExited; $i++) { Start-Sleep -Milliseconds 100 }; $p.HasExited }
function Unlock($pw) { SetText 101 $pw; Click 103; WaitHas 203 }
function Refused($pw) { SetText 101 $pw; Click 103; $b = Expect 'master password is incorrect'; Answer $b 1; ($b -ne $Z) -and (Has 101) -and -not (Has 203) }
function Create($pw) { SetText 101 $pw; SetText 102 $pw; Click 103; WaitHas 203 }
function Settings() { if (-not (Has 216)) { Click 220; Start-Sleep -Milliseconds 400 } }
function BackupForm($cur, $pw) { Settings; Click 216; SetText 520 $cur; SetText 521 $pw; SetText 522 $pw }
# restore from the unlocked settings: pre-backup question -> No -> restore screen with the file and the backup password
# (the current master is kept, so there are no new-master fields; $new is typed only if they exist)
function RestoreForm($file, $pw, $new) {
  Settings; Click 217
  Answer (Expect 'backup file first' 5000) 7
  if (-not (WaitHas 528 5000)) { throw "restore screen did not open (box: $($script:lastBox))" }
  SetPick $file; Click 527; SetText 528 $pw; if (Has 529) { SetText 529 $new; SetText 530 $new }
}

try {
  Stop-TestInstances $suffix

  # ---------------------------------------------------------------- A: make
  Launch $cfgA
  if (-not (Create "Master1234")) { throw "A: setup did not reach the list" }
  Click 203; Click 4001; SetText 301 "S1"; SetText 302 "content-1"; Click 310
  $b = [BkU]::FindBox([uint32]$p.Id); if ($b -ne $Z) { Answer $b 2 }   # setup only (not judged): a hint box after the first item, if any
  SetPick $bak1
  BackupForm "wrong-master" "Backup-Pw-9"; Click 524
  $b = Expect 'current master password is incorrect'; Answer $b 1
  Check BW01 "True|False|True" "$($b -ne $Z)|$(Test-Path $bak1)|$(Has 520)" "make: a wrong current master is refused after the computation; no file; screen stays"
  $k0 = Kdf; SetText 520 "Master1234"; Click 524; $run = Running $k0; Click 525; WaitKdfEnd $k0
  Check BW02 "True|started+1 done+0 dropped+1 threw+0|False|True|False" "$run|$(KdfDelta $k0)|$(Test-Path $bak1)|$(NoBox)|$(Has 520)" "make: back while the computation runs - the result is dropped, no file, no box"
  BackupForm "Master1234" "Backup-Pw-9"; $k0 = Kdf; Click 524; $run = Running $k0; LockNotice; WaitKdfEnd $k0
  Check BW03 "True|started+1 done+0 dropped+1 threw+0|False|True|True" "$run|$(KdfDelta $k0)|$(Test-Path $bak1)|$(NoBox)|$(Has 101)" "make: synthetic lock notice while the computation runs - dropped, no file, lock screen"
  if (-not (Unlock "Master1234")) { throw "A: unlock after BW03 failed" }
  BackupForm "Master1234" "Backup-Pw-9"; Click 524
  $ok = WaitHas 216
  $head = if (Test-Path $bak1) { [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($bak1), 0, 8) } else { "" }
  Check BW04 "True|1KEYBAK1|False" "$ok|$head|$(Test-Path "$bak1.tmp")" "make: success writes the backup file (header 1KEYBAK1, no temporary file) and returns to the settings (0.3.76)"

  # control for the counters used in BW18: while locked, the password hotkey and the program hotkey reach their handlers
  LockNotice
  $f0 = Prop "OneKeyTestFireCalls"; $l0 = Prop "OneKeyTestLaunchCalls"
  Post 0x0312 0; Post 0x0312 200; Start-Sleep -Milliseconds 1200
  Check BW05 "1|1|True" "$((Prop 'OneKeyTestFireCalls') - $f0)|$((Prop 'OneKeyTestLaunchCalls') - $l0)|$(NoBox)" "control: a posted password hotkey and program hotkey are counted by their handlers (locked: nothing typed)"
  Quit   # T44-1: one suffix = one instance; A is started again after B

  # ---------------------------------------------------------------- B: first-setup restore (tray command)
  Launch $cfgB
  Post 0x0111 3005; Start-Sleep -Milliseconds 800
  Check BW06 "True|True|True" "$(Has 528)|$(Has 529)|$(Has 530)" "first setup: the tray restore command opens the restore screen with the new-master fields"
  SetPick $bak1; Click 527; SetText 528 "wrong-backup-pw"; SetText 529 "NewMaster-88"; SetText 530 "NewMaster-88"; Click 532
  $b = Expect 'Wrong backup password'; Answer $b 1
  Check BW07 "True|none|True" "$($b -ne $Z)|$(Hash "$cfgB\config.dat")|$(Has 528)" "first setup: a wrong backup password is refused, nothing written"
  SetText 528 "Backup-Pw-9"; Click 532
  $ok = WaitHas 101 20000
  Check BW08 "True|True|False|0" "$ok|$(Test-Path "$cfgB\config.dat")|$(Marker $cfgB)|$(Prop 'OneKeyTestRecovery')" "first setup: restore publishes and shows the lock screen; no commit mark left"
  $ok = Unlock "NewMaster-88"
  Click 1000; Click 303; $c = GetEdit 302; Click 311
  Check BW09 "True|content-1" "$ok|$c" "first setup: the new master unlocks the restored settings; content intact"
  Quit; Launch $cfgB
  $rej = Refused "Master1234"
  $ok = Unlock "NewMaster-88"
  Check BW10 "True|True" "$rej|$ok" "after a restart: the source PC master is refused, the new master opens"
  Quit

  # ---------------------------------------------------------------- A: restore over existing settings
  Launch $cfgA
  if (-not (Unlock "Master1234")) { throw "A: unlock after the restart failed" }
  $h0 = Hash "$cfgA\config.dat"
  Settings; Click 217; $b = Expect 'backup file first' 5000; LockNotice
  Check BW11 "True|True|False|True" "$($b -ne $Z)|$(NoBox)|$(Has 528)|$(Has 101)" "pre-backup question closed by a synthetic lock notice: no restore screen, lock screen"
  if (-not (Unlock "Master1234")) { throw "A: unlock after BW11 failed" }
  Settings; Click 217; $b = Expect 'backup file first' 5000; EscKey $b
  Check BW12 "True|True|True" "$($b -ne $Z)|$(NoBox)|$(Has 528)" "pre-backup question, Esc key message (= No in a yes/no box): only the restore screen opens, nothing changed"
  Click 533; Start-Sleep -Milliseconds 400
  Settings; Click 217; Answer (Expect 'backup file first' 5000) 6
  Check BW13 "True" "$(Has 520)" "pre-backup question Yes: the backup screen opens first"
  # Codex 23:06 R68-1: after that backup the restore screen opens for the same unlocked session - it keeps the current master
  SetPick $bak3; SetText 520 "Master1234"; SetText 521 "Backup-Pw-9"; SetText 522 "Backup-Pw-9"; Click 524
  $ok = WaitHas 528 20000
  Check BW32 "True|True|False|False" "$ok|$(Test-Path $bak3)|$(Has 529)|$(Has 530)" "pre-backup Yes -> backup made -> restore screen opens keeping the current master (no new-master fields)"
  Click 533; Start-Sleep -Milliseconds 400
  RestoreForm $bak1 "Backup-Pw-9" "Master-A2-77"
  Check BW31 "True|False|False" "$(Has 528)|$(Has 529)|$(Has 530)" "restore from the unlocked settings: backup password only, the current master is kept (no new-master fields)"
  $k0 = Kdf; Click 532
  $b = Expect 'will be replaced' 5000; LockNotice; Start-Sleep -Seconds 4
  Check BW14 "True|started+0 done+0 dropped+0 threw+0|$h0|False|True" "$($b -ne $Z)|$(KdfDelta $k0)|$(Hash "$cfgA\config.dat")|$(Marker $cfgA)|$(Has 101)" "overwrite question closed by a synthetic lock notice: no computation started, nothing published"
  if (-not (Unlock "Master1234")) { throw "A: unlock after BW14 failed" }
  RestoreForm $bak1 "Backup-Pw-9" "Master-A2-77"; Click 532
  $b = Expect 'will be replaced' 5000; $k0 = Kdf; Answer $b 6; $run = Running $k0; LockNotice; WaitKdfEnd $k0
  Check BW15 "True|started+1 done+0 dropped+1 threw+0|$h0|False|True|True" "$run|$(KdfDelta $k0)|$(Hash "$cfgA\config.dat")|$(Marker $cfgA)|$(NoBox)|$(Has 101)" "synthetic lock notice while the restore computes: dropped, nothing published, no box"
  if (-not (Unlock "Master1234")) { throw "A: unlock after BW15 failed (old master must still work)" }
  RestoreForm $bak1 "Backup-Pw-9" "Master-A2-77"; Click 532
  $b = Expect 'will be replaced' 5000; $k0 = Kdf; Answer $b 6; $run = Running $k0; Click 533; WaitKdfEnd $k0
  Check BW16 "True|started+1 done+0 dropped+1 threw+0|$h0|False|True|False" "$run|$(KdfDelta $k0)|$(Hash "$cfgA\config.dat")|$(Marker $cfgA)|$(NoBox)|$(Has 528)" "back while the restore computes: dropped, nothing published, no box"

  # ---------------------------------------------------------------- A: unfinished publish -> recovery-only state
  New-Item -ItemType File "$cfgA\test-fail-move" | Out-Null
  RestoreForm $bak1 "Backup-Pw-9" "Master-A2-77"; Click 532
  Answer (Expect 'will be replaced' 5000) 6
  $b = Expect 'did not finish' 20000
  Check BW17 "True|True|1|True" "$($b -ne $Z)|$(Marker $cfgA)|$(Prop 'OneKeyTestRecovery')|$(Has 101)" "second move keeps failing: recovery box, commit mark kept, recovery-only state, old session locked"
  $f0 = Prop "OneKeyTestFireCalls"; $l0 = Prop "OneKeyTestLaunchCalls"; $s0 = Prop "OneKeyTestSubmitCalls"; $e0 = Prop "OneKeyTestLaunchExec"; $r0 = Prop "OneKeyTestRecoveryBlocked"
  Post 0x0312 0; Post 0x0312 200            # password hotkey, program hotkey
  SetText 101 "Master1234"; Post 0x0111 103  # unlock button with the old master
  Post 0x0111 3005; Post 0x0111 217         # tray restore, settings restore row
  Post 0x8001 0 0x0202                      # tray left click (show window)
  Post 0x02B1 7                             # synthetic session lock notice
  Start-Sleep -Milliseconds 1500
  $d = "$((Prop 'OneKeyTestFireCalls') - $f0)|$((Prop 'OneKeyTestLaunchCalls') - $l0)|$((Prop 'OneKeyTestSubmitCalls') - $s0)|$((Prop 'OneKeyTestLaunchExec') - $e0)|$(((Prop 'OneKeyTestRecoveryBlocked') - $r0) -ge 7)"
  Check BW18 "0|0|0|0|True" $d "recovery-only: hotkeys, unlock, tray, commands and the lock notice reach no handler (fire|launch|submit|exec|7+ blocked - edit notices count too)"
  $b2 = [BkU]::FindBox([uint32]$p.Id)
  Check BW19 "True|False|True|False" "$(($b2 -eq $b) -and ($b -ne $Z))|$($p.HasExited)|$(Marker $cfgA)|$(Has 528)" "recovery-only: the same box stays up, the app is running, nothing published or opened"
  Answer $b 1; $b = Expect 'did not finish' 5000
  Check BW20 "True|True" "$($b -ne $Z)|$(Marker $cfgA)" "retry while the fault is still there: asked again, mark kept"
  Remove-Item -LiteralPath "$cfgA\test-fail-move" -Force
  Answer $b 1
  $ok = WaitHas 101 10000
  Check BW21 "True|False|0|True" "$ok|$(Marker $cfgA)|$(Prop 'OneKeyTestRecovery')|$(NoBox)" "retry after the fault is gone: the new pair is finished, recovery-only ends, lock screen"
  $rej = Refused "Master-A2-77"
  $ok = Unlock "Master1234"
  Click 1000; Click 303; $c = GetEdit 302; Click 311
  Check BW22 "True|True|content-1" "$rej|$ok|$c" "after recovery: the master in use before the restore opens the restored settings (another password is refused), content intact"

  # ---------------------------------------------------------------- A: unfinished publish + quit, then startup recovery
  New-Item -ItemType File "$cfgA\test-fail-move" | Out-Null
  RestoreForm $bak1 "Backup-Pw-9" "Master-A3-55"; Click 532
  Answer (Expect 'will be replaced' 5000) 6
  Answer (Expect 'did not finish' 20000) 2
  Check BW23 "True|True" "$(WaitExit)|$(Marker $cfgA)" "recovery box Cancel quits 1Key, the commit mark stays for the next start"
  $env:ONEKEY_CONFIG_DIR = $cfgA
  $p = Start-Process $exe -PassThru
  $b = Expect 'did not finish' 10000
  $main = [BkU]::FindCls([uint32]$p.Id, "OneKeyMainWindow$suffix")
  Check BW24 "True|True|#32770" "$($b -ne $Z)|$($main -eq $Z)|$([BkU]::Cls($b))" "start with the fault: the recovery box (system box) comes before any window (settings not read)"
  Answer $b 2
  Check BW25 "True|True" "$(WaitExit)|$(Marker $cfgA)" "startup box Cancel quits; mark kept"
  $p = Start-Process $exe -PassThru
  $b = Expect 'did not finish' 10000
  Remove-Item -LiteralPath "$cfgA\test-fail-move" -Force
  Answer $b 4   # IDRETRY
  WaitMain
  $ok = Unlock "Master1234"
  Check BW26 "True|True|False" "$($b -ne $Z)|$ok|$(Marker $cfgA)" "start again, retry after the fault is gone: the new pair opens with the kept master"
  Quit

  # ---------------------------------------------------------------- C: backup without a program list
  [IO.File]::WriteAllBytes("$cfgC\launch.dat", [Text.Encoding]::ASCII.GetBytes("GARBAGE-LAUNCH"))
  $lh = Hash "$cfgC\launch.dat"
  Launch $cfgC
  $b = [BkU]::FindBox([uint32]$p.Id); if ($b -ne $Z) { "setup: start box: $(BoxText $b)"; Answer $b 1 }   # setup only (not judged)
  if (-not (Create "Master-C1-11")) { throw "C: setup did not reach the list" }
  SetPick $bak2
  BackupForm "Master-C1-11" "Backup-Pw-9"; Click 524
  $b = Expect 'cannot be read'; Answer $b 6
  $ok = WaitHas 216 20000
  Check BW27 "True|True|True" "$($b -ne $Z)|$ok|$(Test-Path $bak2)" "unreadable program list: asked, then the backup is made without the list"
  $hc = Hash "$cfgC\config.dat"
  RestoreForm $bak2 "Backup-Pw-9" "Master-C2-22"; Click 532
  Answer (Expect 'will be replaced' 5000) 6
  $b = Expect 'no program/folder list' 20000; EscKey $b
  Start-Sleep -Milliseconds 800
  Check BW28 "True|True|$hc|$lh|False" "$($b -ne $Z)|$(NoBox)|$(Hash "$cfgC\config.dat")|$(Hash "$cfgC\launch.dat")|$(Marker $cfgC)" "no list in the backup: Esc key message on the yes/no/cancel box stops the restore, nothing published"
  if (-not (Has 528)) { RestoreForm $bak2 "Backup-Pw-9" "Master-C2-22" } else { SetText 528 "Backup-Pw-9"; SetText 529 "Master-C2-22"; SetText 530 "Master-C2-22" }
  Click 532
  Answer (Expect 'will be replaced' 5000) 6
  $b = Expect 'no program/folder list' 20000; LockNotice
  $locked = Has 101
  $ok = Unlock "Master-C1-11"
  Check BW29 "True|True|$hc|$lh|False|True" "$($b -ne $Z)|$locked|$(Hash "$cfgC\config.dat")|$(Hash "$cfgC\launch.dat")|$(Marker $cfgC)|$ok" "no-list question closed by a synthetic lock notice: nothing published (the old master still opens)"
  RestoreForm $bak2 "Backup-Pw-9" "Master-C2-22"; Click 532
  Answer (Expect 'will be replaced' 5000) 6
  Answer (Expect 'no program/folder list' 20000) 6
  $ok = WaitHas 101 10000
  $keep = "$ok|$((Hash "$cfgC\config.dat") -ne $hc)|$(Hash "$cfgC\launch.dat")|$(Unlock 'Master-C1-11')"
  RestoreForm $bak2 "Backup-Pw-9" "Master-C3-33"; Click 532
  Answer (Expect 'will be replaced' 5000) 6
  Answer (Expect 'no program/folder list' 20000) 7
  $ok = WaitHas 101 10000
  $lt = (LaunchText $cfgC) -replace "`r", "\r" -replace "`n", "\n"
  $empty = "$ok|$lt|$(Unlock 'Master-C1-11')"
  Check BW30 "True|True|$lh|True/True|launchv=1\n|True" "$keep/$empty" "no list in the backup: Yes keeps the current list file byte for byte, No writes a valid empty list (exactly 'launchv=1', 0 items); each restore opens with the kept master"
  Quit
} catch { Add-Failure ("exception: " + $_) }
finally {
  Stop-TestInstances $suffix
  # T44-4 / Codex 18:43 2: this bundle always keeps the run folder as evidence (dummy settings only; nothing else is deleted)
  "kept run folder: $root"
}
Complete-Checks
