# Cat stays while the 1Key window is open, click opens 1Key, drag moves it, a high drop falls and lands, the spot is remembered;
# only the cat of the current theme loses points (0.5.24, user 2026-10-13); 0.5.25: Codex D24-1..3 (own windows first, clock vs drag,
# fall failure, real mouse safety).
# CD00 environment: taskbar at the bottom, cat shown after closing the window, at the default home (right end)
# CD01 the 1Key window shown (tray click, away from the cat): the cat stays shown
# CD02 a plain click on the cat (real mouse, no move) opens the 1Key window; the cat stays; the name / stage / hearts label shows
# CD03 drag up-left and let go high: the cat falls (log: drag start, drop, fallplan, clipstart fall) and ends at the drop x; no 1Key window
# CD04 drag a little along the taskbar and let go low: no fall, the cat sits at the new x at once
# CD05 the cat record keeps the spot (P line) and a new 1Key run shows the cat there
# CD06 points fade only for the cat of the current theme: light run - the light cat drops, the dark cat keeps 60; then a dark run -
#      the dark cat loses only its own few seconds, the light cat keeps its points
# CD07 hourly clock showing (test clock): drag high closes the clock and the cat still falls and lands
# CD08 the fall pictures fail to prepare (injected): the cat does not hang in the air - it sits at the drop x on the ground
# CD09 a 1Key window over the cat (0.5.30, user: the cat should stay visible): it stays shown on top but lets clicks through while they
#      overlap (OneKeyTestCatDucked 1, WS_EX_TRANSPARENT), and takes clicks again when the window moves away
# CD10 a 1Key box (help) is open: clicking the cat brings the box forward and does not start anything in the 1Key window
# REAL MOUSE (lib\RealMouse.ps1): runs only with -RealMouseApproved (the runner passes it only with its own -RealMouseApproved).
# Never starts while a button is down, always releases what it pressed, stops when the cursor is moved by someone else or the capture
# is not on the test cat, and presses only when the window under the real cursor is the test cat.
# Own config folder and instance suffix; the real 1Key is not touched. Run it while nobody uses the PC.
#   powershell -ExecutionPolicy Bypass -File tools\tests\catdrag.ps1 [-Exe <1Key.exe>] -RealMouseApproved
# ASCII only.
param([string]$Exe = "", [switch]$RealMouseApproved)
$ErrorActionPreference = "Continue"
Add-Type -AssemblyName System.Windows.Forms
. (Join-Path $PSScriptRoot "lib\Check.ps1")
$ids = @(); 0..10 | ForEach-Object { $ids += "CD{0:D2}" -f $_ }
Start-Checks -Required $ids
if (-not $RealMouseApproved) { Add-Failure "not run: real mouse test - needs -RealMouseApproved (PC unused, user approval)"; Complete-Checks; exit 2 }
. (Join-Path $PSScriptRoot "lib\RealMouse.ps1")

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$suffix = ".cd"
$root = Join-Path $sp ("catdrag-{0:yyyyMMdd-HHmmss}-{1}" -f (Get-Date), $PID); New-Item -ItemType Directory $root | Out-Null
$cfg = "$root\cfg"; New-Item -ItemType Directory $cfg | Out-Null
$log = "$root\cat.log"
$master = "Cd-Dummy-6230"
Add-Type @'
using System;using System.Runtime.InteropServices;using System.Text;
public class CDU {
  [DllImport("user32.dll")] public static extern IntPtr GetWindowLongPtrW(IntPtr h, int i);
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
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr GetPropW(IntPtr h, string name);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct PT { public int X, Y; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out PT p);
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(PT p);
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint f);
  [StructLayout(LayoutKind.Sequential)] public struct GTI { public int cbSize, flags; public IntPtr active, focus, capture, menuOwner, moveSize, caret; public int l, t, r, b; }
  [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint tid, ref GTI g);
  [StructLayout(LayoutKind.Sequential)] public struct ABD { public uint cbSize; public IntPtr hWnd; public uint cb, uEdge; public RECT rc; public IntPtr lParam; }
  [DllImport("shell32.dll")] public static extern UIntPtr SHAppBarMessage(uint msg, ref ABD d);
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static IntPtr Find(uint pid, string cls, bool vis) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls && (!vis || IsWindowVisible(h))) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
  public static bool UnderCursor(IntPtr want) { PT p; GetCursorPos(out p); IntPtr h = WindowFromPoint(p); return h != IntPtr.Zero && GetAncestor(h, 2) == want; }
  public static bool HasCapture(IntPtr w) { uint pid; uint tid = GetWindowThreadProcessId(w, out pid); var g = new GTI(); g.cbSize = Marshal.SizeOf(typeof(GTI)); return GetGUIThreadInfo(tid, ref g) && g.capture == w; }
  public static IntPtr Under(int x, int y) { var p = new PT { X = x, Y = y }; IntPtr h = WindowFromPoint(p); return h == IntPtr.Zero ? h : GetAncestor(h, 2); }
}
'@
function Start1Key() {
  $script:p = Start-Process $exe -PassThru; $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $script:m = [CDU]::Find([uint32]$script:p.Id, "OneKeyMainWindow$suffix", $false); if ($script:m -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 100 }
  if ($script:m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 900
}
function Quit1Key() { if ($script:p -and -not $script:p.HasExited) { [void][CDU]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i = 0; $i -lt 50 -and -not $script:p.HasExited; $i++) { Start-Sleep -Milliseconds 100 }; if (-not $script:p.HasExited) { Stop-Process -Id $script:p.Id -Force } } }
function Has($id) { [CDU]::GetDlgItem($m, $id) -ne [IntPtr]::Zero }
function Click($id, $ms = 700) { [void][CDU]::PostMessageW([CDU]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds $ms }
function SetText($id, $s) { [void][CDU]::SendMessageW([CDU]::GetDlgItem($m, $id), 0x000C, [IntPtr]::Zero, [string]$s) }
function Prop($n) { [int][CDU]::GetPropW($m, $n) }
function CatWnd() { [CDU]::Find([uint32]$p.Id, "OneKeyCat", $true) }
function CatShown() { (Prop 'OneKeyTestWalker') -eq 1 -and (CatWnd) -ne [IntPtr]::Zero }
function MainShown() { [CDU]::IsWindowVisible($m) }
function CloseMain() { [void][CDU]::PostMessageW($m, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 800 }   # WM_CLOSE -> tray
function ShowMain() { [void][CDU]::PostMessageW($m, 0x8001, [IntPtr]::Zero, [IntPtr]0x0202); Start-Sleep -Milliseconds 900 }   # tray icon left click
# keep the 1Key window in the top-left corner, away from the cat (and away from the cat's drop spots)
function ParkMain() { $wa = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea; [void][CDU]::SetWindowPos($m, [IntPtr]::Zero, $wa.Left + 10, $wa.Top + 10, 0, 0, 0x0001 -bor 0x0004 -bor 0x0010) }
function WaitShown([int]$ms = 6000) { $t = [Environment]::TickCount; while ([Environment]::TickCount - $t -lt $ms) { if (CatShown) { return $true }; Start-Sleep -Milliseconds 100 }; $false }
# a point on the cat's body (the window centre at 55 % height) that the cat window itself answers to
function CatPoint() {
  for ($k = 0; $k -lt 12; $k++) {
    $w = CatWnd
    if ($w -ne [IntPtr]::Zero) {
      $r = New-Object CDU+RECT; [void][CDU]::GetWindowRect($w, [ref]$r)
      $x = [int](($r.L + $r.R) / 2); $y = [int]($r.T + ($r.B - $r.T) * 0.55)
      if ([CDU]::Under($x, $y) -eq $w) { return @($x, $y, $w) }
    }
    Start-Sleep -Milliseconds 400
  }
  $null
}
function DragTo($tx, $ty, [int]$steps = 16) {
  $c = CatPoint; if (-not $c) { throw "no cat to press" }
  $w = $c[2]; $proc = $script:p
  Invoke-RmDrag $c[0] $c[1] $tx $ty $steps { [CDU]::UnderCursor($w) }.GetNewClosure() { -not $proc.HasExited -and [CDU]::HasCapture($w) }.GetNewClosure()
}
function ClickCat() {
  $c = CatPoint; if (-not $c) { throw "no cat to click" }
  $w = $c[2]
  Invoke-RmClick $c[0] $c[1] { [CDU]::UnderCursor($w) }.GetNewClosure()
}
# 0.5.27: the place is set when the cat is let go (before the 4 s fall) - also wait until the fall / motion is over (OneKeyTestCatBusy 0),
# otherwise the next drag pressed where the cat was going to sit while it was still in the air (the cursor guard stopped the test)
function WaitX([int]$want, [int]$tol, [int]$ms) { $t = [Environment]::TickCount; while ([Environment]::TickCount - $t -lt $ms) { $x = Prop 'OneKeyTestCatX'; if ([Math]::Abs($x - $want) -le $tol -and (Prop 'OneKeyTestCatBusy') -eq 0) { return $x }; Start-Sleep -Milliseconds 100 }; Prop 'OneKeyTestCatX' }
function Setup1Key([bool]$fresh) {
  Start1Key
  if ($fresh) { SetText 101 $master; SetText 102 $master; Click 103 1500 } else { SetText 101 $master; Click 103 1500 }
  if (-not (Has 203)) { throw "setup: no list after the master" }
  ParkMain
  if ($fresh) {
    Click 220
    $cur = [int][CDU]::SendMessageW([CDU]::GetDlgItem($m, 2032), 0x00F0, [IntPtr]::Zero, [IntPtr]::Zero) -eq 1
    if (-not $cur) { [void][CDU]::PostMessageW([CDU]::GetDlgItem($m, 2032), 0x0100, [IntPtr]0x20, [IntPtr]::Zero); Start-Sleep -Milliseconds 300 }
    Click 2012 900
  }
}
function LogText() { if (Test-Path $log) { Get-Content $log -Raw } else { "" } }
function ScoreOf([string]$dir, [string]$k) { $l = Get-ChildItem $dir -File | ForEach-Object { Get-Content $_.FullName -ErrorAction Ignore } | Where-Object { $_ -match "^$k \d+ " } | Select-Object -First 1; if ($l) { [int]($l -split ' ')[1] } else { -1 } }

try {
  Start-RealMouse
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_LOCKWIDGET = "0"; $env:ONEKEY_TEST_NO_AUTOLOCK = "1"
  $env:ONEKEY_TEST_CAT_LOG = $log
  $abd = New-Object CDU+ABD; $abd.cbSize = [Runtime.InteropServices.Marshal]::SizeOf([type][CDU+ABD]); [void][CDU]::SHAppBarMessage(5, [ref]$abd)
  $st = New-Object CDU+ABD; $st.cbSize = $abd.cbSize; $auto = ([uint64][CDU]::SHAppBarMessage(4, [ref]$st)) -band 1
  $envOk = ($abd.uEdge -eq 3) -and ($auto -eq 0)
  $wa = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
  Setup1Key $true
  CloseMain
  $shown0 = WaitShown; Start-Sleep -Seconds 3
  $x0 = Prop 'OneKeyTestCatX'
  Check CD00 "True|True|True" "$envOk|$shown0|$($x0 -gt $wa.Left + $wa.Width * 0.7)" "taskbar at the bottom, cat shown after closing the window, at the right end (x $x0)"

  ShowMain; ParkMain
  $main1 = MainShown; Start-Sleep -Seconds 2
  Check CD01 "True|True" "$main1|$(CatShown)" "1Key window shown (away from the cat): the cat stays"
  CloseMain; Start-Sleep -Seconds 4

  ClickCat; Start-Sleep -Milliseconds 500
  $hearts2 = [CDU]::Find([uint32]$p.Id, "OneKeyCatHearts", $true) -ne [IntPtr]::Zero
  ParkMain; Start-Sleep -Milliseconds 1000
  Check CD02 "True|True|True" "$(MainShown)|$(CatShown)|$hearts2" "plain click on the cat opens the 1Key window; the cat stays; the hearts label shows"
  CloseMain; Start-Sleep -Seconds 5

  $w = Prop 'OneKeyTestCatW'
  $tx = [int]($wa.Left + $wa.Width * 0.45); $ty = [int]($wa.Top + $wa.Height * 0.45)
  DragTo $tx $ty
  $want = [Math]::Max($wa.Left, [Math]::Min($tx - [int]($w / 2), $wa.Right - $w))
  $x3 = WaitX $want 4 9000
  Check CD03 "True|True|False" "$([Math]::Abs($x3 - $want) -le 4)|$(CatShown)|$(MainShown)" "dropped high at x ${want}: settled at x $x3, cat shown, the drag did not open 1Key"
  Start-Sleep -Seconds 1

  $c = CatPoint; $tx4 = $c[0] + 160; $ty4 = $c[1]
  DragTo $tx4 $ty4 8
  $want4 = [Math]::Max($wa.Left, [Math]::Min($tx4 - [int]($w / 2), $wa.Right - $w))
  $x4 = WaitX $want4 4 1500
  Check CD04 "True|True" "$([Math]::Abs($x4 - $want4) -le 4)|$(CatShown)" "dropped low at x ${want4}: sits at x $x4 at once"
  Start-Sleep -Seconds 1

  # CD09 a 1Key window over the cat: still shown, clicks pass through; moved away, clicks reach the cat again
  ShowMain
  $cr = New-Object CDU+RECT; $cw = CatWnd; [void][CDU]::GetWindowRect($cw, [ref]$cr)
  # the window's bottom goes 12 px below the cat's bottom so it really covers the cat (0.5.26: with "top = cat top - 300" a short window
  # ended 3 px above the cat - the cover log showed no overlap at all)
  $mr = New-Object CDU+RECT; [void][CDU]::GetWindowRect($m, [ref]$mr)
  [void][CDU]::SetWindowPos($m, [IntPtr]::Zero, $cr.L - 120, $cr.B + 12 - ($mr.B - $mr.T), 0, 0, 0x0001 -bor 0x0004 -bor 0x0010)
  Start-Sleep -Milliseconds 800
  $duck = Prop 'OneKeyTestCatDucked'; $vis1 = [CDU]::IsWindowVisible($cw); $tr1 = ([int64][CDU]::GetWindowLongPtrW($cw, -20) -band 0x20) -ne 0
  ParkMain; Start-Sleep -Milliseconds 800
  $back = Prop 'OneKeyTestCatDucked'; $vis = [CDU]::IsWindowVisible($cw); $tr2 = ([int64][CDU]::GetWindowLongPtrW($cw, -20) -band 0x20) -ne 0
  Check CD09 "1|True|True|0|True|False" "$duck|$vis1|$tr1|$back|$vis|$tr2" "1Key window over the cat: shown and click-through while they overlap, clickable again when the window moves away"

  # CD10 a 1Key box open: a click on the cat brings the box forward, nothing else
  Click 250 1200   # help (a 1Key box)
  $box = [CDU]::Find([uint32]$p.Id, "OneKeyDialog", $true)
  if ($box -ne [IntPtr]::Zero) {
    $wa2 = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    [void][CDU]::SetWindowPos($box, [IntPtr]::Zero, $wa2.Left + 20, $wa2.Top + 20, 0, 0, 0x0001 -bor 0x0004 -bor 0x0010)   # away from the cat
    Start-Sleep -Milliseconds 800
  }
  $disabledBefore = -not [CDU]::IsWindowEnabled($m)
  ClickCat; Start-Sleep -Milliseconds 900
  $fg = [CDU]::GetForegroundWindow()
  $stillBox = $box -ne [IntPtr]::Zero -and [CDU]::IsWindowVisible($box)
  Check CD10 "True|True|True|True" "$($box -ne [IntPtr]::Zero)|$disabledBefore|$stillBox|$($fg -eq $box)" "1Key box open: a cat click brings the box forward (foreground) and starts nothing else"
  if ($box -ne [IntPtr]::Zero) { [void][CDU]::PostMessageW($box, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 600 }
  CloseMain; Start-Sleep -Seconds 1
  Quit1Key; Start-Sleep -Milliseconds 600

  $rec = Get-ChildItem $cfg -File | Where-Object { (Get-Content $_.FullName -ErrorAction Ignore) -match '^P \d+$' } | Select-Object -First 1
  Setup1Key $false; CloseMain
  $shown5 = WaitShown; Start-Sleep -Milliseconds 800
  $x5 = Prop 'OneKeyTestCatX'
  Check CD05 "True|True|True" "$($null -ne $rec)|$shown5|$([Math]::Abs($x5 - $x4) -le 3)" "cat record has a P line ($($rec.Name)); new run shows the cat at x $x5 (was $x4)"
  Quit1Key; Start-Sleep -Milliseconds 600
  $lt = LogText
  "log: drag start $([regex]::Matches($lt, 'drag start').Count), drop $([regex]::Matches($lt, ' drop x ').Count), fallplan $([regex]::Matches($lt, 'fallplan').Count), clipstart fall $([regex]::Matches($lt, 'clipstart fall').Count), ducked $([regex]::Matches($lt, 'ducked under own window').Count)"
  if (-not ($lt -match 'fallplan height \d+' -and $lt -match 'clipstart fall')) { Add-Failure "CD03 log: no fallplan / clipstart fall" }

  # CD07 hourly clock showing: drag high closes it and the cat still falls
  $cfg7 = "$root\cfg7"; New-Item -ItemType Directory $cfg7 | Out-Null; $log7 = "$root\cat7.log"
  $env:ONEKEY_CONFIG_DIR = $cfg7; $env:ONEKEY_TEST_CAT_LOG = $log7; $env:ONEKEY_TEST_CLOCK = "1"
  Setup1Key $true; CloseMain
  [void](WaitShown); Start-Sleep -Milliseconds 1500
  $clockUp = [CDU]::Find([uint32]$p.Id, "OneKeyFlipClock", $true) -ne [IntPtr]::Zero
  $w7 = Prop 'OneKeyTestCatW'; $tx7 = [int]($wa.Left + $wa.Width * 0.5); $ty7 = [int]($wa.Top + $wa.Height * 0.4)
  DragTo $tx7 $ty7
  $want7 = [Math]::Max($wa.Left, [Math]::Min($tx7 - [int]($w7 / 2), $wa.Right - $w7))
  $x7 = WaitX $want7 4 9000
  $clockGone = [CDU]::Find([uint32]$p.Id, "OneKeyFlipClock", $true) -eq [IntPtr]::Zero
  Quit1Key; Start-Sleep -Milliseconds 600
  $l7 = if (Test-Path $log7) { Get-Content $log7 -Raw } else { "" }
  Check CD07 "True|True|True|True|True" "$clockUp|$($l7 -match 'clock closed by drag')|$($l7 -match 'clipstart fall')|$clockGone|$([Math]::Abs($x7 - $want7) -le 4)" "clock showing: drag high closes it, the cat falls and sits at x $x7"
  Remove-Item env:ONEKEY_TEST_CLOCK -ErrorAction Ignore

  # CD08 fall pictures fail to prepare: no hanging in the air
  $cfg8 = "$root\cfg8"; New-Item -ItemType Directory $cfg8 | Out-Null; $log8 = "$root\cat8.log"
  $env:ONEKEY_CONFIG_DIR = $cfg8; $env:ONEKEY_TEST_CAT_LOG = $log8; $env:ONEKEY_TEST_FAIL = "cat:fall-prepare"
  Setup1Key $true; CloseMain
  [void](WaitShown); Start-Sleep -Seconds 3
  $w8 = Prop 'OneKeyTestCatW'; $tx8 = [int]($wa.Left + $wa.Width * 0.4); $ty8 = [int]($wa.Top + $wa.Height * 0.4)
  DragTo $tx8 $ty8
  $want8 = [Math]::Max($wa.Left, [Math]::Min($tx8 - [int]($w8 / 2), $wa.Right - $w8))
  Start-Sleep -Milliseconds 1500
  $cr8 = New-Object CDU+RECT; [void][CDU]::GetWindowRect((CatWnd), [ref]$cr8)
  $onGround = $cr8.B -ge $wa.Bottom - 2
  $x8 = Prop 'OneKeyTestCatX'
  Quit1Key; Start-Sleep -Milliseconds 600
  $l8 = if (Test-Path $log8) { Get-Content $log8 -Raw } else { "" }
  Check CD08 "True|True|False|True" "$($l8 -match 'fallplan')|$onGround|$($l8 -match 'clipstart fall')|$([Math]::Abs($x8 - $want8) -le 4)" "fall pictures fail: sits on the ground at once (bottom $($cr8.B), work bottom $($wa.Bottom)), x $x8"
  Remove-Item env:ONEKEY_TEST_FAIL -ErrorAction Ignore

  # CD06 fade only for the cat of the current theme, light run then dark run
  $cfg6 = "$root\cfg6"; New-Item -ItemType Directory $cfg6 | Out-Null
  $env:ONEKEY_CONFIG_DIR = $cfg6; $env:ONEKEY_TEST_CAT_LOG = "$root\cat6.log"; $env:ONEKEY_TEST_THEME = "light"; $env:ONEKEY_TEST_CAT_SCORE = "60"; $env:ONEKEY_TEST_CAT_DECAY_MS = "400"
  Setup1Key $true; CloseMain
  [void](WaitShown); Start-Sleep -Seconds 7
  Quit1Key; Start-Sleep -Milliseconds 600
  $l1 = ScoreOf $cfg6 'L'; $d1 = ScoreOf $cfg6 'D'
  Remove-Item env:ONEKEY_TEST_CAT_SCORE -ErrorAction Ignore
  $env:ONEKEY_TEST_THEME = "dark"
  Setup1Key $false; CloseMain
  [void](WaitShown); Start-Sleep -Seconds 3
  Quit1Key; Start-Sleep -Milliseconds 600
  $l2 = ScoreOf $cfg6 'L'; $d2 = ScoreOf $cfg6 'D'
  Check CD06 "True|60|True|True" "$($l1 -ge 0 -and $l1 -lt 60)|$d1|$($l2 -eq $l1)|$($d2 -lt 60 -and $d2 -ge 60 - 35)" "light run: light $l1 (below 60), dark $d1; dark run: light $l2 (kept), dark $d2 (only its own few seconds)"
} catch { Add-Failure ("exception: " + $_) }
finally {
  Stop-RealMouse
  Quit1Key; Stop-TestInstances $suffix
  foreach ($v in "ONEKEY_TEST_CAT_LOG", "ONEKEY_TEST_LOCKWIDGET", "ONEKEY_TEST_NO_AUTOLOCK", "ONEKEY_TEST_THEME", "ONEKEY_TEST_CAT_SCORE", "ONEKEY_TEST_CAT_DECAY_MS", "ONEKEY_TEST_CLOCK", "ONEKEY_TEST_FAIL") { Remove-Item "env:$v" -ErrorAction Ignore }
  "log kept: $log"
}
Complete-Checks
