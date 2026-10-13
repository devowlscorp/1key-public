# Cat on other windows (0.5.27, user 2026-10-13). No real mouse: the test-only message WM_TESTDROP (0x8034, to the cat window, wParam x,
# lParam y = where the hand lets go) picks the cat up and drops it there. Test windows are WinForms forms; the app perches only on
# windows whose class starts with ONEKEY_TEST_PERCH_CLASS in test mode (so the real windows on this PC are never used).
# PE00 cat shown on the taskbar, not perched
# PE01 dropped above form A: lands on A's top edge (perch = A, cat window above the edge, close to it)
# PE02 A moved by +120,+50: the cat moves with it (x and y within 4 px), still on A
# PE03 another form activated (A inactive, not covering): still on A
# PE04 form B moved over the cat's seat on A: the cat falls (not on A any more) and sits on the taskbar
# PE05 on A again, A minimized: falls to the taskbar
# PE06 on A again, A maximized: falls to the taskbar
# PE07 on A again, A closed: falls to the taskbar
# PE08 1Key window right under the drop point: the cat does not hide while it falls past it (OneKeyTestCatDucked stays 0 until it sits)
# PE09 on A again, the display changes (WM_DISPLAYCHANGE -> the cat is shown again): it comes back on the taskbar, not on A
# Own config folder and instance suffix; the real 1Key is not touched. Windows appear: only while nobody uses the PC.
#   powershell -ExecutionPolicy Bypass -File tools\tests\perch.ps1 [-Exe <1Key.exe>]
# ASCII only.
param([string]$Exe = "")
$ErrorActionPreference = "Continue"
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
. (Join-Path $PSScriptRoot "lib\Check.ps1")
$ids = @(); 0..9 | ForEach-Object { $ids += "PE{0:D2}" -f $_ }
Start-Checks -Required $ids

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$suffix = ".pe"
$root = Join-Path $sp ("perch-{0:yyyyMMdd-HHmmss}-{1}" -f (Get-Date), $PID); New-Item -ItemType Directory $root | Out-Null
$cfg = "$root\cfg"; New-Item -ItemType Directory $cfg | Out-Null
$log = "$root\cat.log"
$master = "Pe-Dummy-7316"
Add-Type @'
using System;using System.Runtime.InteropServices;using System.Text;
public class PEU {
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
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int size);
  public static RECT Frame(IntPtr h) { RECT r; if (DwmGetWindowAttribute(h, 9, out r, 16) != 0) GetWindowRect(h, out r); return r; }
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static IntPtr Find(uint pid, string cls, bool vis) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls && (!vis || IsWindowVisible(h))) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
}
'@
[void][PEU]::SetProcessDPIAware()
function Pump([int]$ms) { $t = [Environment]::TickCount; while ([Environment]::TickCount - $t -lt $ms) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 20 } }
function Start1Key() {
  $script:p = Start-Process $exe -PassThru; $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $script:m = [PEU]::Find([uint32]$script:p.Id, "OneKeyMainWindow$suffix", $false); if ($script:m -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 100 }
  if ($script:m -eq [IntPtr]::Zero) { throw "no main window" }
  Pump 900
}
function Quit1Key() { if ($script:p -and -not $script:p.HasExited) { [void][PEU]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i = 0; $i -lt 50 -and -not $script:p.HasExited; $i++) { Start-Sleep -Milliseconds 100 }; if (-not $script:p.HasExited) { Stop-Process -Id $script:p.Id -Force } } }
function Has($id) { [PEU]::GetDlgItem($m, $id) -ne [IntPtr]::Zero }
function Click($id, $ms = 700) { [void][PEU]::PostMessageW([PEU]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Pump $ms }
function SetText($id, $s) { [void][PEU]::SendMessageW([PEU]::GetDlgItem($m, $id), 0x000C, [IntPtr]::Zero, [string]$s) }
function Prop($n) { [int64][PEU]::GetPropW($m, $n) }
function CatWnd() { [PEU]::Find([uint32]$p.Id, "OneKeyCat", $true) }
function CatShown() { (Prop 'OneKeyTestWalker') -eq 1 -and (CatWnd) -ne [IntPtr]::Zero }
function Perch() { Prop 'OneKeyTestCatPerch' }
function Drop([int]$x, [int]$y) { [void][PEU]::PostMessageW((CatWnd), 0x8034, [IntPtr]$x, [IntPtr]$y) }
# waits until the cat sits (not falling: CatY steady for 600 ms) - returns "x|y|perch"
function WaitSettled([int]$ms = 8000) {
  $t = [Environment]::TickCount; $last = ""; $since = $t
  while ([Environment]::TickCount - $t -lt $ms) {
    Pump 100
    $now = "$(Prop 'OneKeyTestCatX')|$(Prop 'OneKeyTestCatY')|$(Perch)"
    if ($now -ne $last) { $last = $now; $since = [Environment]::TickCount } elseif ([Environment]::TickCount - $since -ge 600) { break }
  }
  $last
}
function NewForm([string]$title, [int]$x, [int]$y, [int]$w, [int]$h) {
  $f = New-Object System.Windows.Forms.Form
  $f.Text = $title; $f.StartPosition = "Manual"; $f.Left = $x; $f.Top = $y; $f.Width = $w; $f.Height = $h; $f.ShowInTaskbar = $false
  $f.Show(); Pump 300; $f
}
function OnA([string]$why) {
  $r = [PEU]::Frame($A.Handle)
  Drop ([int](($r.L + $r.R) / 2)) ([int]($r.T - 260))
  $s = WaitSettled
  if ((Perch) -ne [int64]$A.Handle) { Add-Failure "setup for ${why}: cat not on A ($s)" }
}

try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_LOCKWIDGET = "0"; $env:ONEKEY_TEST_NO_AUTOLOCK = "1"
  $env:ONEKEY_TEST_CAT_LOG = $log; $env:ONEKEY_TEST_PERCH_CLASS = "WindowsForms10"
  $wa = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
  Start1Key
  SetText 101 $master; SetText 102 $master; Click 103 1500
  if (-not (Has 203)) { throw "setup: no list after the master" }
  [void][PEU]::SetWindowPos($m, [IntPtr]::Zero, $wa.Left + 10, $wa.Top + 10, 0, 0, 0x0001 -bor 0x0004 -bor 0x0010)
  Click 220
  [void][PEU]::PostMessageW([PEU]::GetDlgItem($m, 2032), 0x0100, [IntPtr]0x20, [IntPtr]::Zero); Pump 300
  Click 2012 900
  [void][PEU]::PostMessageW($m, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero); Pump 1500   # to the tray
  $t0 = [Environment]::TickCount; while ([Environment]::TickCount - $t0 -lt 6000 -and -not (CatShown)) { Pump 100 }
  Pump 2500
  $groundY = Prop 'OneKeyTestCatY'
  Check PE00 "True|0" "$(CatShown)|$(Perch)" "cat shown on the taskbar (y $groundY), not perched"

  # form A in the middle of the work area
  $A = NewForm "perch A" ($wa.Left + 500) ($wa.Top + 420) 640 360
  $B = NewForm "perch B" ($wa.Left + 60) ($wa.Top + 120) 300 200
  $A.Activate(); Pump 300
  $r = [PEU]::Frame($A.Handle)
  Drop ([int](($r.L + $r.R) / 2)) ([int]($r.T - 260))
  $s1 = WaitSettled
  $y1 = Prop 'OneKeyTestCatY'; $x1 = Prop 'OneKeyTestCatX'; $cw = Prop 'OneKeyTestCatW'
  Check PE01 "True|True|True" "$((Perch) -eq [int64]$A.Handle)|$($y1 -lt $r.T)|$($y1 -gt $r.T - 2 * $cw)" "dropped above A: on A's top edge (A top $($r.T), cat y $y1, state $s1)"

  # PE02 move A
  $A.Left = $A.Left + 120; $A.Top = $A.Top + 50; Pump 800
  $x2 = Prop 'OneKeyTestCatX'; $y2 = Prop 'OneKeyTestCatY'
  Check PE02 "True|True|True" "$((Perch) -eq [int64]$A.Handle)|$([Math]::Abs($x2 - $x1 - 120) -le 4)|$([Math]::Abs($y2 - $y1 - 50) -le 4)" "A moved +120,+50: cat moved with it (x $x1 -> $x2, y $y1 -> $y2)"

  # PE03 another window active
  $B.Activate(); Pump 1200
  Check PE03 "True" "$((Perch) -eq [int64]$A.Handle)" "B active, A inactive and not covered: cat still on A"

  # PE04 B over the seat
  $r = [PEU]::Frame($A.Handle); $cx = (Prop 'OneKeyTestCatX') + [int]($cw / 2)
  $B.Left = $cx - 150; $B.Top = $r.T - 60; $B.Activate(); Pump 300
  $s4 = WaitSettled
  Check PE04 "0|True" "$(Perch)|$([Math]::Abs((Prop 'OneKeyTestCatY') - $groundY) -le 2)" "B covers the seat: cat fell to the taskbar ($s4)"
  $B.Left = $wa.Left + 60; $B.Top = $wa.Top + 120; Pump 300

  # PE05 minimized
  $A.Activate(); OnA "PE05"
  $A.WindowState = "Minimized"; $s5 = WaitSettled
  Check PE05 "0|True" "$(Perch)|$([Math]::Abs((Prop 'OneKeyTestCatY') - $groundY) -le 2)" "A minimized: cat fell to the taskbar ($s5)"
  $A.WindowState = "Normal"; Pump 600

  # PE06 maximized
  $A.Activate(); OnA "PE06"
  $A.WindowState = "Maximized"; $s6 = WaitSettled
  Check PE06 "0|True" "$(Perch)|$([Math]::Abs((Prop 'OneKeyTestCatY') - $groundY) -le 2)" "A maximized: cat fell to the taskbar ($s6)"
  $A.WindowState = "Normal"; Pump 600

  # PE09 display change -> back on the taskbar
  $A.Activate(); OnA "PE09"
  [void][PEU]::SendMessageW($m, 0x007E, [IntPtr]::Zero, [IntPtr]::Zero); Pump 300
  $s9 = WaitSettled
  Check PE09 "0|True|True" "$(Perch)|$(CatShown)|$([Math]::Abs((Prop 'OneKeyTestCatY') - $groundY) -le 2)" "display change: shown again on the taskbar, not on A ($s9)"

  # PE07 closed
  $A.Activate(); OnA "PE07"
  $A.Close(); $s7 = WaitSettled
  Check PE07 "0|True" "$(Perch)|$([Math]::Abs((Prop 'OneKeyTestCatY') - $groundY) -le 2)" "A closed: cat fell to the taskbar ($s7)"

  # PE08 1Key window under the drop point: no hiding while falling
  [void][PEU]::PostMessageW($m, 0x8001, [IntPtr]::Zero, [IntPtr]0x0202); Pump 1200   # tray icon click -> 1Key window
  $mr = [PEU]::Frame($m)
  $mx = [int](($mr.L + $mr.R) / 2)
  [void][PEU]::SetWindowPos($m, [IntPtr]::Zero, $mx - [int](($mr.R - $mr.L) / 2), $wa.Top + 200, 0, 0, 0x0001 -bor 0x0004 -bor 0x0010); Pump 500
  $mr = [PEU]::Frame($m)
  $ducks = 0
  Drop $mx ($wa.Top + 120)
  $t8 = [Environment]::TickCount
  while ([Environment]::TickCount - $t8 -lt 4000) { if ((Prop 'OneKeyTestCatDucked') -eq 1) { $ducks++ }; Pump 25 }
  $s8 = WaitSettled
  Check PE08 "0|0|True" "$ducks|$(Perch)|$([Math]::Abs((Prop 'OneKeyTestCatY') - $groundY) -le 2)" "dropped above the 1Key window: never hidden while falling past it, sits on the taskbar ($s8; window $($mr.L),$($mr.T),$($mr.R),$($mr.B))"
} catch { Add-Failure ("exception: " + $_) }
finally {
  foreach ($f in @($A, $B)) { if ($f) { try { $f.Close() } catch { } } }
  Quit1Key; Stop-TestInstances $suffix
  foreach ($v in "ONEKEY_TEST_CAT_LOG", "ONEKEY_TEST_LOCKWIDGET", "ONEKEY_TEST_NO_AUTOLOCK", "ONEKEY_TEST_PERCH_CLASS") { Remove-Item "env:$v" -ErrorAction Ignore }
  "log kept: $log"
}
Complete-Checks
