# Cat stays while the 1Key window is open, click opens 1Key, drag moves it, a high drop falls and lands, the spot is remembered;
# only the cat of the current theme loses points (0.5.24, user 2026-10-13).
# CD00 environment: taskbar at the bottom, cat shown after closing the window, at the default home (right end)
# CD01 the 1Key window shown (tray click): the cat stays shown
# CD02 a plain click on the cat (real mouse, no move) opens the 1Key window; the cat stays; the name / stage / hearts label shows (0.5.25)
# CD03 drag up-left and let go high: the cat falls (log: drag start, drop, fallplan, clipstart fall) and ends at the drop x; the click did not open 1Key
# CD04 drag a little along the taskbar and let go low: no fall, the cat sits at the new x at once
# CD05 the cat record keeps the spot (P line) and a new 1Key run shows the cat there
# CD06 points fade only for the cat of the current theme (light theme, fast fade: the light cat drops, the dark cat keeps its points)
# Real mouse input: before every press the window under the cursor must be the test cat (otherwise it stops - nothing else is clicked).
# Own config folder and instance suffix; the real 1Key is not touched. Run it while nobody uses the PC.
#   powershell -ExecutionPolicy Bypass -File tools\tests\catdrag.ps1 [-Exe <1Key.exe>]
# ASCII only.
param([string]$Exe = "")
$ErrorActionPreference = "Continue"
Add-Type -AssemblyName System.Windows.Forms
. (Join-Path $PSScriptRoot "lib\Check.ps1")
$ids = @(); 0..6 | ForEach-Object { $ids += "CD{0:D2}" -f $_ }
Start-Checks -Required $ids

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
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr GetPropW(IntPtr h, string name);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct PT { public int X, Y; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out PT p);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(PT p);
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint f);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, UIntPtr e);
  [StructLayout(LayoutKind.Sequential)] public struct ABD { public uint cbSize; public IntPtr hWnd; public uint cb, uEdge; public RECT rc; public IntPtr lParam; }
  [DllImport("shell32.dll")] public static extern UIntPtr SHAppBarMessage(uint msg, ref ABD d);
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static IntPtr Find(uint pid, string cls, bool vis) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls && (!vis || IsWindowVisible(h))) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
  public static bool Under(int x, int y, IntPtr want) { var p = new PT { X = x, Y = y }; IntPtr h = WindowFromPoint(p); return h != IntPtr.Zero && GetAncestor(h, 2) == want; }
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
function WaitShown([int]$ms = 6000) { $t = [Environment]::TickCount; while ([Environment]::TickCount - $t -lt $ms) { if (CatShown) { return $true }; Start-Sleep -Milliseconds 100 }; $false }
# a point on the cat's body: the window centre at 55 % height, retried until the cat window is what is under it (a layered window lets
# clicks through its transparent pixels - during a motion the window is bigger)
function CatCenter() {
  for ($k = 0; $k -lt 12; $k++) {
    $w = CatWnd
    if ($w -ne [IntPtr]::Zero) {
      $r = New-Object CDU+RECT; [void][CDU]::GetWindowRect($w, [ref]$r)
      $x = [int](($r.L + $r.R) / 2); $y = [int]($r.T + ($r.B - $r.T) * 0.55)
      if ([CDU]::Under($x, $y, $w)) { return @($x, $y, $w) }
    }
    Start-Sleep -Milliseconds 400
  }
  $null
}
# (not "Move" - that is a PowerShell alias of Move-Item and silently wins over a function of the same name)
function Set-TestCursor($x, $y) { [void][CDU]::SetCursorPos($x, $y); [CDU]::mouse_event(0x0001, 0, 0, 0, [UIntPtr]::Zero) }
# guard before any press: the REAL cursor must be at (x, y) and the window under the real cursor must be the test cat
function Assert-OnCat($x, $y, $w) {
  $cp = New-Object CDU+PT; [void][CDU]::GetCursorPos([ref]$cp)
  if ([Math]::Abs($cp.X - $x) -gt 1 -or [Math]::Abs($cp.Y - $y) -gt 1) { throw "the cursor is at $($cp.X),$($cp.Y), not on the cat at $x,$y - stopped before pressing" }
  if (-not [CDU]::Under($cp.X, $cp.Y, $w)) { throw "the window under the cursor is not the test cat - stopped before pressing" }
}
# press on the cat (guarded), move to (tx, ty) in steps, let go
function DragTo($tx, $ty, [int]$steps = 16) {
  $c = CatCenter; if (-not $c) { throw "no cat to press" }
  Set-TestCursor $c[0] $c[1]; Start-Sleep -Milliseconds 120
  Assert-OnCat $c[0] $c[1] $c[2]
  [CDU]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 80
  for ($i = 1; $i -le $steps; $i++) { Set-TestCursor ([int]($c[0] + ($tx - $c[0]) * $i / $steps)) ([int]($c[1] + ($ty - $c[1]) * $i / $steps)); Start-Sleep -Milliseconds 30 }
  Start-Sleep -Milliseconds 150
  [CDU]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
  $c
}
function ClickCat() {
  $c = CatCenter; if (-not $c) { throw "no cat to click" }
  Set-TestCursor $c[0] $c[1]; Start-Sleep -Milliseconds 150
  Assert-OnCat $c[0] $c[1] $c[2]
  [CDU]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [CDU]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
}
function WaitX([int]$want, [int]$tol, [int]$ms) { $t = [Environment]::TickCount; while ([Environment]::TickCount - $t -lt $ms) { $x = Prop 'OneKeyTestCatX'; if ([Math]::Abs($x - $want) -le $tol) { return $x }; Start-Sleep -Milliseconds 100 }; Prop 'OneKeyTestCatX' }
function Setup1Key([bool]$fresh) {
  Start1Key
  if ($fresh) { SetText 101 $master; SetText 102 $master; Click 103 1500 } else { SetText 101 $master; Click 103 1500 }
  if (-not (Has 203)) { throw "setup: no list after the master" }
  if ($fresh) {
    Click 220
    $cur = [int][CDU]::SendMessageW([CDU]::GetDlgItem($m, 2032), 0x00F0, [IntPtr]::Zero, [IntPtr]::Zero) -eq 1
    if (-not $cur) { [void][CDU]::PostMessageW([CDU]::GetDlgItem($m, 2032), 0x0100, [IntPtr]0x20, [IntPtr]::Zero); Start-Sleep -Milliseconds 300 }
    Click 2012 900
  }
}

$cursor0 = New-Object CDU+PT; [void][CDU]::GetCursorPos([ref]$cursor0)
try {
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

  ShowMain
  $main1 = MainShown; Start-Sleep -Seconds 2
  Check CD01 "True|True" "$main1|$(CatShown)" "1Key window shown: the cat stays"
  CloseMain; Start-Sleep -Seconds 4

  ClickCat; Start-Sleep -Milliseconds 500
  $hearts2 = [CDU]::Find([uint32]$p.Id, "OneKeyCatHearts", $true) -ne [IntPtr]::Zero
  Start-Sleep -Milliseconds 1000
  Check CD02 "True|True|True" "$(MainShown)|$(CatShown)|$hearts2" "plain click on the cat opens the 1Key window; the cat stays; the hearts label shows"
  CloseMain; Start-Sleep -Seconds 5

  # CD03 drag up-left, let go high
  $w = Prop 'OneKeyTestCatW'
  $tx = [int]($wa.Left + $wa.Width * 0.45); $ty = [int]($wa.Top + $wa.Height * 0.45)
  [void](DragTo $tx $ty)
  $want = [Math]::Max($wa.Left, [Math]::Min($tx - [int]($w / 2), $wa.Right - $w))
  $x3 = WaitX $want 4 9000
  $main3 = MainShown
  Check CD03 "True|True|False" "$([Math]::Abs($x3 - $want) -le 4)|$(CatShown)|$main3" "dropped high at x ${want}: settled at x $x3, cat shown, the drag did not open 1Key"
  Start-Sleep -Seconds 1

  # CD04 drag a little along the taskbar, let go low (no fall)
  $c = CatCenter; $tx4 = $c[0] + 160; $ty4 = $c[1]
  [void](DragTo $tx4 $ty4 8)
  $want4 = [Math]::Max($wa.Left, [Math]::Min($tx4 - [int]($w / 2), $wa.Right - $w))
  $x4 = WaitX $want4 4 1500
  Check CD04 "True|True" "$([Math]::Abs($x4 - $want4) -le 4)|$(CatShown)" "dropped low at x ${want4}: sits at x $x4 at once"
  Start-Sleep -Seconds 1
  Quit1Key; Start-Sleep -Milliseconds 600

  # CD05 the spot is kept: P line, and a new run shows the cat there
  $rec = Get-ChildItem $cfg -File | Where-Object { (Get-Content $_.FullName -ErrorAction Ignore) -match '^P \d+$' } | Select-Object -First 1
  Setup1Key $false; CloseMain
  $shown5 = WaitShown; Start-Sleep -Milliseconds 800
  $x5 = Prop 'OneKeyTestCatX'
  Check CD05 "True|True|True" "$($null -ne $rec)|$shown5|$([Math]::Abs($x5 - $x4) -le 3)" "cat record has a P line ($($rec.Name)); new run shows the cat at x $x5 (was $x4)"
  Quit1Key; Start-Sleep -Milliseconds 600

  $logText = if (Test-Path $log) { Get-Content $log -Raw } else { "" }
  "log: drag start $([regex]::Matches($logText, 'drag start').Count), drop $([regex]::Matches($logText, ' drop x ').Count), fallplan $([regex]::Matches($logText, 'fallplan').Count), clipstart fall $([regex]::Matches($logText, 'clipstart fall').Count)"
  if (-not ($logText -match 'fallplan height \d+' -and $logText -match 'clipstart fall')) { Add-Failure "CD03 log: no fallplan / clipstart fall" }

  # CD06 fade only for the cat of the current theme
  $cfg6 = "$root\cfg6"; New-Item -ItemType Directory $cfg6 | Out-Null
  $env:ONEKEY_CONFIG_DIR = $cfg6; $env:ONEKEY_TEST_THEME = "light"; $env:ONEKEY_TEST_CAT_SCORE = "60"; $env:ONEKEY_TEST_CAT_DECAY_MS = "400"
  Setup1Key $true; CloseMain
  [void](WaitShown); Start-Sleep -Seconds 7
  Quit1Key; Start-Sleep -Milliseconds 600
  $lines = Get-ChildItem $cfg6 -File | ForEach-Object { Get-Content $_.FullName -ErrorAction Ignore } | Where-Object { $_ -match '^[LD] \d+ ' }
  $L = ($lines | Where-Object { $_ -match '^L ' } | Select-Object -First 1); $D = ($lines | Where-Object { $_ -match '^D ' } | Select-Object -First 1)
  $ls = if ($L) { [int]($L -split ' ')[1] } else { -1 }; $ds = if ($D) { [int]($D -split ' ')[1] } else { -1 }
  Check CD06 "True|60" "$($ls -ge 0 -and $ls -lt 60)|$ds" "light theme, 1 point per 0.4 s: light cat $ls (below 60), dark cat $ds (kept)"
} catch { Add-Failure ("exception: " + $_) }
finally {
  Quit1Key; Stop-TestInstances $suffix
  [void][CDU]::SetCursorPos($cursor0.X, $cursor0.Y)
  foreach ($v in "ONEKEY_TEST_CAT_LOG", "ONEKEY_TEST_LOCKWIDGET", "ONEKEY_TEST_NO_AUTOLOCK", "ONEKEY_TEST_THEME", "ONEKEY_TEST_CAT_SCORE", "ONEKEY_TEST_CAT_DECAY_MS") { Remove-Item "env:$v" -ErrorAction Ignore }
  "log kept: $log"
}
Complete-Checks
