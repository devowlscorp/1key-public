# Modal effects, locking and the page scroll bar (Codex review 2026-09-29 22:26, R1/R2 and follow-ups; 0.2.47).
# A) Locking drops an unsaved settings draft (R1): change the theme and a switch, lock (plain, with the "discard changes?"
#    box open, with the help window open), unlock, open the settings again: only the saved values, no preview, and the
#    back button does not ask. Locking also removes the blurred backdrop (class OneKeyBackdrop) behind an open box.
#    Since 0.2.49 (user decision 2026-09-30) locking also closes every open box as "cancelled by the lock" (not OK/Yes):
#    FX04-FX08 expect the box to be GONE at once (up to 0.2.48 they expected it to stay - a policy change, not a fix of
#    the test). Pending follow-up work is read with the test-mode message WM_APP+12 (test input / diagnosis / open boxes).
# B) Nested boxes: a second box on top of the first one keeps the main window disabled when it closes; only the last
#    one enables it again.
# C) Failure injection (ONEKEY_TEST_FAIL, test mode only) while opening the help window 20 times: backdrop:after-dib,
#    backdrop:blur, backdrop:after-window, backdrop:rgn, dialog:before-loop, and a run without failures. The box still
#    works (or, for dialog:before-loop, does not appear and the app goes on), no backdrop window is left behind, and GDI /
#    USER objects and handles stay flat.
# E) Lock while other boxes are open (0.2.49): the delete question (the item stays), the test-input notice (no test
#    input is armed and none happens), the diagnosis notice (no diagnosis armed, no report box), nested help windows (all
#    closed, the main window enabled, the lock screen works).
#    FE09 (0.2.50): lock while a diagnosis is already waiting (OK pressed): it is cancelled, no report box later.
#    FE10 (0.2.52): the item is named "Fx&Item"; the edit screen title shows the name with its "&" (STATIC with
#    SS_NOPREFIX, so "&" is not turned into a mnemonic underline).
# G) System MessageBox fallback (0.2.50, Codex 0.2.49 review): ONEKEY_TEST_FAIL=dialog:use-msgbox makes every box a
#    system MessageBox (class #32770), which the lock cannot close; admin:dry-run counts the admin restart instead of
#    raising UAC (count read with WM_APP+12 bits 16-23). A [Yes] pressed after a lock (also after lock + unlock) must not
#    exit or restart; without a lock the same [Yes] does (controls: restart count 1, the process exits).
# D) Page scroll bar at 125 % and 150 % (simulated 1366x768 screen): drag the thumb past the end (clamps), release outside
#    the window (capture released, no more scrolling), page back with track clicks, a capture change ends the drag, and
#    locking during a drag ends it too (nothing carried over to the next settings screen). Mouse input is posted to the
#    test window only; the capture state is read with GetGUIThreadInfo.
# Own config folder and test suffix; the real 1Key and its settings are not touched. The window is never foregrounded
# on purpose. Judgement: tools\tests\lib\Check.ps1. ASCII only.
param([string]$Exe = "", [int]$Reps = 20)
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
$points = @("none", "backdrop:after-dib", "backdrop:blur", "backdrop:after-window", "backdrop:rgn", "dialog:before-loop")
$dpis = @(120, 144)
$req = @(); for ($i = 1; $i -le 11; $i++) { $req += ("FX{0:D2}" -f $i) }; for ($i = 1; $i -le 10; $i++) { $req += ("FE{0:D2}" -f $i) }; for ($i = 1; $i -le 5; $i++) { $req += ("FG{0:D2}" -f $i) }
for ($k = 0; $k -lt $points.Count; $k++) { $req += "FC-$k-1"; $req += "FC-$k-2" }
foreach ($d in $dpis) { for ($i = 1; $i -le 9; $i++) { $req += "FD-$d-$i" } }
Start-Checks -Required $req

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$suffix = ".fx"
$cfg = "$sp\fx_cfg"; if (Test-Path $cfg) { Get-ChildItem $cfg -Recurse | Remove-Item -Force -Recurse -ErrorAction Ignore }; New-Item -ItemType Directory -Force $cfg | Out-Null
Add-Type @'
using System;using System.Collections.Generic;using System.Runtime.InteropServices;using System.Text;
public class FU {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr hProcess, uint flags);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int l, t, r, b; }
  [StructLayout(LayoutKind.Sequential)] public struct GTI { public int cb; public int flags; public IntPtr active, focus, capture, menu, move, caret; public RECT rc; }
  [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint tid, ref GTI g);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int size);
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  // all top-level windows of a process with this class; visibleOnly = false also counts hidden ones
  public static List<IntPtr> All(uint pid, string cls, bool visibleOnly) { var f = new List<IntPtr>(); EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls && (!visibleOnly || IsWindowVisible(h))) f.Add(h); return true; }, IntPtr.Zero); return f; }
  public static IntPtr Capture(IntPtr w) { uint pid; uint tid = GetWindowThreadProcessId(w, out pid); var g = new GTI(); g.cb = Marshal.SizeOf(typeof(GTI)); return GetGUIThreadInfo(tid, ref g) ? g.capture : new IntPtr(-1); }
  public static int Top(IntPtr h) { RECT r; GetWindowRect(h, out r); return r.t; }
  public static int Dark(IntPtr h) { int v; return DwmGetWindowAttribute(h, 20, out v, 4) == 0 ? v : -1; }
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, CB cb, IntPtr l);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern int GetWindowLongW(IntPtr h, int i);
  // style of the first Static child whose text is exactly t (-1 = none)
  public static int StaticStyle(IntPtr p, string t) { int st = -1; EnumChildWindows(p, (h,l) => { var sb = new StringBuilder(256); GetWindowTextW(h, sb, 256); if (Cls(h) == "Static" && sb.ToString() == t) { st = GetWindowLongW(h, -16); return false; } return true; }, IntPtr.Zero); return st; }
}
'@
function SetText($id, $s) { [void][FU]::SendMessageW([FU]::GetDlgItem($m, $id), 0x000C, [IntPtr]::Zero, [string]$s) }
function Click($id, $ms = 600) { [void][FU]::PostMessageW([FU]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds $ms }
function Key($id, $vk) { [void][FU]::PostMessageW([FU]::GetDlgItem($m, $id), 0x0100, [IntPtr]$vk, [IntPtr]::Zero); Start-Sleep -Milliseconds 400 }
function Has($id) { [FU]::GetDlgItem($m, $id) -ne [IntPtr]::Zero }
function Chk($id) { [int][FU]::SendMessageW([FU]::GetDlgItem($m, $id), 0x00F0, [IntPtr]::Zero, [IntPtr]::Zero) }
function Boxes([bool]$vis = $true) { [FU]::All([uint32]$p.Id, "OneKeyDialog", $vis) }
function Box() { for ($i=0;$i -lt 15;$i++) { $b = @(Boxes); if ($b.Count -gt 0) { return $b[0] }; Start-Sleep -Milliseconds 100 }; [IntPtr]::Zero }
function Answer($b, $cmd) { if ($b -ne [IntPtr]::Zero) { [void][FU]::PostMessageW($b, 0x0111, [IntPtr]$cmd, [IntPtr]::Zero); Start-Sleep -Milliseconds 600 } }
function Backdrops([bool]$visibleOnly = $false) { @([FU]::All([uint32]$p.Id, "OneKeyBackdrop", $visibleOnly)).Count }
function Lock() { [void][FU]::PostMessageW($m, 0x02B1, [IntPtr]7, [IntPtr]::Zero); Start-Sleep -Milliseconds 700 }   # WM_WTSSESSION_CHANGE / WTS_SESSION_LOCK
function Unlock() { SetText 101 "Master1234"; Click 103 900 }
# test-mode message WM_APP+12: bit 0 test input armed, bit 1 diagnosis armed, bits 8-15 open boxes, bit 60 answered
function Pending() { $v = [int64][FU]::SendMessageW($m, 0x800C, [IntPtr]::Zero, [IntPtr]::Zero); if ((($v -shr 60) -band 1) -ne 1) { "no answer" } else { "test=$($v -band 1) diag=$(($v -shr 1) -band 1) boxes=$(($v -shr 8) -band 0xFF)" } }
function Restarts() { $v = [int64][FU]::SendMessageW($m, 0x800C, [IntPtr]::Zero, [IntPtr]::Zero); if ((($v -shr 60) -band 1) -ne 1) { -1 } else { ($v -shr 16) -band 0xFF } }
function SysBox() { for ($i=0;$i -lt 20;$i++) { $b = @([FU]::All([uint32]$p.Id, "#32770", $true)); if ($b.Count -gt 0) { return $b[0] }; Start-Sleep -Milliseconds 100 }; [IntPtr]::Zero }
$none = "test=0 diag=0 boxes=0"
function Launch() {
  $script:p = Start-Process $exe -PassThru
  $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $w = @([FU]::All([uint32]$script:p.Id, "OneKeyMainWindow$suffix", $true)); if ($w.Count -gt 0) { $script:m = $w[0]; break }; Start-Sleep -Milliseconds 100 }
  if ($script:m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 700
}
function Quit() { [void][FU]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i=0; $i -lt 50 -and -not $p.HasExited; $i++) { Start-Sleep -Milliseconds 100 } }
function Res() { $p.Refresh(); [pscustomobject]@{ Gdi = [FU]::GetGuiResources($p.Handle, 0); User = [FU]::GetGuiResources($p.Handle, 1); Handles = $p.HandleCount } }
function MakeLParam([int]$x, [int]$y) { [IntPtr](([int64]($y -band 0xFFFF) -shl 16) -bor ($x -band 0xFFFF)) }
function PostMouse([uint32]$code, [int]$btn, [int]$x, [int]$y) { [void][FU]::PostMessageW($m, $code, [IntPtr]$btn, (MakeLParam $x $y)); Start-Sleep -Milliseconds 250 }
$sysLight = (Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize" -ErrorAction Ignore).AppsUseLightTheme
$sysDark = if ($sysLight -eq 0) { 1 } else { 0 }
$other = if ($sysDark -eq 1) { 1 } else { 2 }   # dropdown steps to the theme that differs from Windows (1 = light, 2 = dark)
# change the theme and "start minimized" without saving (the preview is visible)
function Dirty() { for ($k = 0; $k -lt $other; $k++) { Key 2008 0x28 }; Key 2002 0x20 }
# the settings as saved: Windows theme, the saved switch, and back does not ask
function Clean($saved) { "$([FU]::Dark($m))|$(Chk 2002)" -eq "$sysDark|$saved" }

try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_WORKAREA = $TallScreen
  $env:ONEKEY_TEST_THEME = $null; $env:ONEKEY_TEST_FAIL = $null
  Launch
  SetText 101 "Master1234"; SetText 102 "Master1234"; Click 103; Answer (Box) 1
  # auto-lock off: this runs while nobody uses the PC, and an auto-lock in the middle hides the window (seen in the first
  # run: the help button was gone from rep 14 of one failure point on)
  Click 220; Key 2005 0x24; Click 2012 3500
  Confirm-AutoLockOff $cfg

  # ---- A) locking drops the settings draft (R1)
  Click 220
  $saved = Chk 2002
  Dirty
  $pre = "$([FU]::Dark($m))|$(Chk 2002)"
  Lock
  Check FX01 "True|True|$sysDark" ("$(Has 101)|" + ($pre -eq "$(1 - $sysDark)|$(1 - $saved)") + "|$([FU]::Dark($m))") "lock with an unsaved theme preview and switch: lock screen with the saved theme"
  Unlock; Click 220
  Check FX02 $true (Clean $saved) "after unlocking, the settings show only the saved values (no draft, no preview)"
  Click 240; $b = Box
  Check FX03 "False|True" ("$($b -ne [IntPtr]::Zero)|$(Has 203)") "... and back does not ask (nothing unsaved)"
  Answer $b 7

  # lock while the "discard changes?" box is open
  Click 220; Dirty; Click 240
  $b = Box
  $bdBefore = Backdrops $true
  Lock
  Check FX04 "1|True|0|0|True" ("$bdBefore|$($b -ne [IntPtr]::Zero)|$(@(Boxes $false).Count)|$(Backdrops)|$(Has 101)") "lock with the confirmation box open: the box and the backdrop are gone at once (cancelled, also no hidden box), the lock screen is up (before|had box|boxes|backdrops|lock)"
  Check FX05 "True|True|$none" ("$(Has 101)|$([FU]::IsWindowEnabled($m))|$(Pending)") "still locked, the main window is enabled again, nothing pending"
  Unlock; Click 220
  $c = Clean $saved
  Click 240; $b = Box
  Check FX06 "True|False" ("$c|$($b -ne [IntPtr]::Zero)") "after unlocking: saved values only, back does not ask"
  Answer $b 7

  # lock while the help window is open
  Click 220; Dirty; Click 250
  $b = Box
  $bdBefore = Backdrops $true
  Lock
  Check FX07 "1|0|0|True" ("$bdBefore|$(@(Boxes $false).Count)|$(Backdrops)|$(Has 101)") "lock with the help window open: the help window and the backdrop are gone, the lock screen is up"
  Unlock; Click 220
  $c = Clean $saved
  Click 240; $b = Box
  Check FX08 "True|False" ("$c|$($b -ne [IntPtr]::Zero)") "after unlocking: saved values only, back does not ask"
  Answer $b 7

  # ---- B) nested boxes keep the owner disabled until the last one closes
  Click 250
  $outer = Box
  [void][FU]::PostMessageW($m, 0x0111, [IntPtr]250, [FU]::GetDlgItem($m, 250)); Start-Sleep -Milliseconds 800
  $inner = @(Boxes | Where-Object { $_ -ne $outer })
  Check FX09 "True|1" ("$($outer -ne [IntPtr]::Zero)|$($inner.Count)") "a second help window can open on top of the first"
  if ($inner.Count -gt 0) { Answer $inner[0] 2 }
  Check FX10 "False|1" ("$([FU]::IsWindowEnabled($m))|$(@(Boxes).Count)") "closing the inner one keeps the main window disabled while the outer one is open"
  Answer $outer 2
  Check FX11 "True|0|0" ("$([FU]::IsWindowEnabled($m))|$(@(Boxes).Count)|$(Backdrops)") "closing the outer one enables it again, no box or backdrop left"

  # ---- E) lock while other boxes are open (0.2.49: they are cancelled)
  # an item to work on
  Click 203; Click 4001; SetText 301 "Fx&Item"; SetText 302 "dummy-fx"; Click 310 900; Answer (Box) 1
  Check FE01 $true (Has 1000) "an item exists for the next checks"
  # the edit screen title shows the user's name with its "&" (SS_NOPREFIX 0x80)
  Click 1000
  $st = [FU]::StaticStyle($m, "Fx&Item")
  Check FE10 "True|True" ("$($st -ne -1)|$(($st -band 0x80) -ne 0)") "edit screen title: the item name 'Fx&Item' is a Static with SS_NOPREFIX (found|noprefix)"
  # delete question
  Click 308
  $b = Box
  Lock
  Check FE02 "True|0|0|True" ("$($b -ne [IntPtr]::Zero)|$(@(Boxes $false).Count)|$(Backdrops)|$(Has 101)") "lock with the delete question open: the question is gone (not answered Yes), lock screen"
  Unlock
  Check FE03 "True" "$(Has 1000)" "after unlocking the item is still there (nothing was deleted)"
  # test-input notice: no test input is armed, none happens
  Click 1000; Click 307
  $b = Box
  Lock
  $p1 = Pending
  Start-Sleep -Milliseconds 3500
  Check FE04 "True|$none|$none|0" ("$($b -ne [IntPtr]::Zero)|$p1|$(Pending)|$(@(Boxes $false).Count)") "lock with the test-input notice open: the notice is gone and no test input is armed, also 3.5 s later (had notice|pending|pending later|boxes)"
  Unlock
  # diagnosis notice: advanced screen, "why does it not type" row
  Click 220; Click 212; Click 211
  $b = Box
  Lock
  $p1 = Pending
  Start-Sleep -Milliseconds 3500
  Check FE05 "True|$none|$none|0" ("$($b -ne [IntPtr]::Zero)|$p1|$(Pending)|$(@(Boxes $false).Count)") "lock with the diagnosis notice open: the notice is gone, no diagnosis is armed and no report box appears (had notice|pending|pending later|boxes)"
  Unlock
  # nested help windows
  Click 250
  $outer = Box
  [void][FU]::PostMessageW($m, 0x0111, [IntPtr]250, [FU]::GetDlgItem($m, 250)); Start-Sleep -Milliseconds 800
  $two = @(Boxes).Count
  Lock
  Check FE06 "2|0|0|True" ("$two|$(@(Boxes $false).Count)|$(Backdrops)|$([FU]::IsWindowEnabled($m))") "lock with two help windows open: both are gone, no backdrop, the main window is enabled (open before|boxes|backdrops|enabled)"
  Check FE07 "True|$none" ("$(Has 101)|$(Pending)") "lock screen, nothing pending"
  Unlock
  Check FE08 "True|False" ("$(Has 1000)|$(Has 101)") "the lock screen works: unlocking shows the list"
  # a diagnosis already waiting (OK pressed) is cancelled by the lock
  Click 220; Click 212; Click 211
  Answer (Box) 1
  $p0 = Pending
  Lock
  $p1 = Pending
  Start-Sleep -Milliseconds 3500
  Check FE09 "test=0 diag=1 boxes=0|$none|$none|0" ("$p0|$p1|$(Pending)|$(@(Boxes $false).Count)") "lock while a diagnosis waits: it is cancelled, no report box 3.5 s later (before|after|later|boxes)"
  Unlock
  Quit

  # ---- G) system MessageBox fallback: the lock cannot close it, but its answer is dropped
  $env:ONEKEY_TEST_FAIL = "dialog:use-msgbox,admin:dry-run"
  Launch; Unlock
  # exit question -> lock -> Yes: must not exit
  Click 2015 300
  $sb = SysBox
  Lock
  if ($sb -ne [IntPtr]::Zero) { [void][FU]::PostMessageW($sb, 0x0111, [IntPtr]6, [IntPtr]::Zero); Start-Sleep -Milliseconds 1200 }
  Check FG01 "True|True|True|0" ("$($sb -ne [IntPtr]::Zero)|$(-not $p.HasExited)|$(Has 101)|$(@([FU]::All([uint32]$p.Id, '#32770', $true)).Count)") "system box: exit question, lock, then Yes -> the app does not exit, lock screen (had box|alive|lock|boxes)"
  # admin restart question -> lock -> Yes: no restart
  Unlock
  [void][FU]::PostMessageW($m, 0x800D, [IntPtr]::Zero, [IntPtr]::Zero); $sb = SysBox
  Lock
  if ($sb -ne [IntPtr]::Zero) { [void][FU]::PostMessageW($sb, 0x0111, [IntPtr]6, [IntPtr]::Zero); Start-Sleep -Milliseconds 1200 }
  Check FG02 "True|0" ("$($sb -ne [IntPtr]::Zero)|$(Restarts)") "system box: admin restart question, lock, then Yes -> no restart (had box|restarts)"
  # lock AND unlock while the question is up, then Yes: still dropped (a lock happened in between)
  Unlock
  [void][FU]::PostMessageW($m, 0x800D, [IntPtr]::Zero, [IntPtr]::Zero); $sb = SysBox
  Lock; Unlock
  $unlockedBeforeYes = (Has 1000) -and -not (Has 101)
  if ($sb -ne [IntPtr]::Zero) { [void][FU]::PostMessageW($sb, 0x0111, [IntPtr]6, [IntPtr]::Zero); Start-Sleep -Milliseconds 1200 }
  Check FG03 "True|True|0" ("$($sb -ne [IntPtr]::Zero)|$unlockedBeforeYes|$(Restarts)") "system box: lock and unlock while the question is up, then Yes -> still no restart (had box|unlocked again before Yes|restarts)"
  # controls without a lock: the same Yes does restart (dry run) and exit
  [void][FU]::PostMessageW($m, 0x800D, [IntPtr]::Zero, [IntPtr]::Zero); $sb = SysBox
  if ($sb -ne [IntPtr]::Zero) { [void][FU]::PostMessageW($sb, 0x0111, [IntPtr]6, [IntPtr]::Zero); Start-Sleep -Milliseconds 1200 }
  Check FG04 "True|1" ("$($sb -ne [IntPtr]::Zero)|$(Restarts)") "control, no lock: Yes on the admin question restarts (dry run count 1)"
  Click 2015 300
  $sb = SysBox
  if ($sb -ne [IntPtr]::Zero) { [void][FU]::PostMessageW($sb, 0x0111, [IntPtr]6, [IntPtr]::Zero) }
  for ($i = 0; $i -lt 50 -and -not $p.HasExited; $i++) { Start-Sleep -Milliseconds 100 }
  Check FG05 "True|True" ("$($sb -ne [IntPtr]::Zero)|$($p.HasExited)") "control, no lock: Yes on the exit question exits"
  $env:ONEKEY_TEST_FAIL = $null

  # ---- C) failure injection while opening the help window
  for ($k = 0; $k -lt $points.Count; $k++) {
    $pt = $points[$k]
    $env:ONEKEY_TEST_FAIL = if ($pt -eq "none") { $null } else { $pt }
    Launch; Unlock
    $wantBox = $pt -ne "dialog:before-loop"
    $wantBd = $pt -in @("none", "backdrop:rgn")
    for ($r = 0; $r -lt 3; $r++) { Click 250 400; $b = Box; Answer $b 2 }   # warm-up
    Start-Sleep -Milliseconds 500; $r0 = Res
    $bad = @()
    for ($r = 1; $r -le $Reps; $r++) {
      Click 250 150
      $b = if ($wantBox) { Box } else { Start-Sleep -Milliseconds 500; [IntPtr]::Zero }
      $bx = @(Boxes).Count; $bd = Backdrops $true
      Answer $b 2
      $state = "$bx|$bd"
      $want = "$(if ($wantBox) { 1 } else { 0 })|$(if ($wantBd) { 1 } else { 0 })"
      if ($state -ne $want) { $bad += "rep $r box|backdrop=$state" }
    }
    Start-Sleep -Milliseconds 700; $r1 = Res
    $alive = -not $p.HasExited
    $left = "$(@(Boxes).Count)|$(Backdrops)|$([FU]::IsWindowEnabled($m))"
    Check "FC-$k-1" "True||0|0|True" ("$alive|" + ($bad -join ",") + "|$left") "$pt : box and backdrop as expected every time, nothing left afterwards, main window enabled (alive|problems|boxes|backdrops|enabled)"
    $dg = $r1.Gdi - $r0.Gdi; $du = $r1.User - $r0.User; $dh = $r1.Handles - $r0.Handles
    Check "FC-$k-2" $true ($dg -le 3 -and $du -le 3 -and $dh -le 10) "$pt : $Reps openings leave GDI $($r0.Gdi)->$($r1.Gdi), USER $($r0.User)->$($r1.User), handles $($r0.Handles)->$($r1.Handles) flat"
    Quit
  }
  $env:ONEKEY_TEST_FAIL = $null

  # ---- D) page scroll bar drag at 125 / 150 %
  foreach ($d in $dpis) {
    $env:ONEKEY_TEST_WORKAREA = "1366,768,$d"
    Launch; Unlock; Click 220
    function S([int]$v) { [int][Math]::Floor($v * $d / 96) }
    $rc = New-Object FU+RECT; [void][FU]::GetClientRect($m, [ref]$rc)
    $cw = $rc.r; $ch = $rc.b
    $px = $cw - (S 6); $ty = (S 6) + (S 10)
    $probe = [FU]::GetDlgItem($m, 2002)
    $y0 = [FU]::Top($probe)
    PostMouse 0x0201 1 $px $ty
    Check "FD-$d-1" $true ([FU]::Capture($m) -eq $m) "$d dpi: pressing the thumb captures the mouse"
    PostMouse 0x0200 1 $px ($ch + 300); $y1 = [FU]::Top($probe)
    PostMouse 0x0200 1 $px ($ch + 900); $y2 = [FU]::Top($probe)
    Check "FD-$d-2" $true ($y1 -lt $y0 -and $y2 -eq $y1) "$d dpi: dragging past the end scrolls to the end and stops there ($y0 -> $y1 -> $y2)"
    PostMouse 0x0202 0 -40 ($ch + 900)
    Check "FD-$d-3" $true ([FU]::Capture($m) -eq [IntPtr]::Zero) "$d dpi: releasing outside the window releases the capture"
    PostMouse 0x0200 1 $px 0
    Check "FD-$d-4" $y1 ([FU]::Top($probe)) "$d dpi: after the release, moving the mouse does not scroll"
    $n = 0; while ([FU]::Top($probe) -ne $y0 -and $n -lt 12) { PostMouse 0x0201 1 $px ((S 6) + 2); PostMouse 0x0202 0 $px ((S 6) + 2); $n++ }
    Check "FD-$d-5" $y0 ([FU]::Top($probe)) "$d dpi: clicks on the track page back to the top ($n clicks)"
    PostMouse 0x0201 1 $px $ty
    PostMouse 0x0215 0 0 0   # WM_CAPTURECHANGED: another window took the mouse
    PostMouse 0x0200 1 $px ($ch + 300)
    Check "FD-$d-6" $y0 ([FU]::Top($probe)) "$d dpi: after a capture change the drag has ended (no scrolling)"
    PostMouse 0x0202 0 $px ($ch + 300)
    Check "FD-$d-7" $true ([FU]::Capture($m) -eq [IntPtr]::Zero) "$d dpi: ... and the button release lets go of the mouse"
    PostMouse 0x0201 1 $px $ty
    Lock
    Check "FD-$d-8" "True|True" ("$(Has 101)|" + ([FU]::Capture($m) -eq [IntPtr]::Zero)) "$d dpi: locking during a drag ends it and releases the capture"
    PostMouse 0x0200 1 $px ($ch + 300)
    Unlock; Click 220
    $probe = [FU]::GetDlgItem($m, 2002); $ya = [FU]::Top($probe)
    PostMouse 0x0200 1 $px ($ch + 300)
    Check "FD-$d-9" $ya ([FU]::Top($probe)) "$d dpi: the next settings screen does not continue the old drag"
    Click 241
    Quit
  }
} catch { Add-Failure ("exception: " + $_) }
finally { $env:ONEKEY_TEST_WORKAREA = $null; $env:ONEKEY_TEST_FAIL = $null; Stop-TestInstances $suffix }
Complete-Checks
