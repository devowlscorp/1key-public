# Cat on more than one monitor (0.5.33, user 2026-10-14: "can it move to the extended monitor too?" - choices: on a monitor without
# a taskbar it sits on the screen bottom edge; the home monitor is remembered after a restart). Needs a second monitor; no real mouse:
# the test-only message WM_TESTDROP (0x8034) picks the cat up at (x, y) and drops it there.
# MM00 cat shown on the primary monitor's taskbar (its work area = the primary work area)
# MM01 dropped on the second monitor (high up): it moves there - work area = that monitor's, it sits at that work area's bottom
#      (on its taskbar, or on the screen bottom edge if it has none), inside that monitor, not perched
# MM02 1Key started again (same settings): the cat comes back on the second monitor (home monitor remembered)
# MM03 a form on the second monitor, dropped above it: perched on it, still on the second monitor
# MM04 the form moved to the primary monitor: the cat goes with it (still on the form, work area = primary, inside the form)
# MM05 the form closed: the cat falls to the primary taskbar (work area = primary) - its new home
# MM06 1Key started again: the cat is on the primary monitor
# Not covered (manual): a second monitor without a taskbar ("show my taskbar on all displays" off) - the expected place is the screen
# bottom edge = that monitor's work area bottom, which MM01 checks in the same way; different scale (DPI) per monitor - logged as info.
# Own config folder and instance suffix; the real 1Key is not touched. Windows appear: only while nobody uses the PC.
#   powershell -ExecutionPolicy Bypass -File tools\tests\multimon.ps1 [-Exe <1Key.exe>]
# ASCII only.
param([string]$Exe = "")
$ErrorActionPreference = "Continue"
Add-Type @'
using System;using System.Runtime.InteropServices;
public class MMD { [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c); [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
'@
# per-monitor coordinates like the app (a system-aware test would see scaled coordinates on a monitor with another scale)
if (-not [MMD]::SetProcessDpiAwarenessContext([IntPtr](-4))) { [void][MMD]::SetProcessDPIAware() }
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
. (Join-Path $PSScriptRoot "lib\Check.ps1")
$ids = @(); 0..6 | ForEach-Object { $ids += "MM{0:D2}" -f $_ }
Start-Checks -Required $ids

$second = [System.Windows.Forms.Screen]::AllScreens | Where-Object { -not $_.Primary } | Select-Object -First 1
if (-not $second) { Add-Failure "needs a second monitor (this PC has one) - run it on a PC with two"; Complete-Checks; exit }
$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$suffix = ".mm"
$root = Join-Path $sp ("multimon-{0:yyyyMMdd-HHmmss}-{1}" -f (Get-Date), $PID); New-Item -ItemType Directory $root | Out-Null
$cfg = "$root\cfg"; New-Item -ItemType Directory $cfg | Out-Null
$log = "$root\cat.log"
$master = "Mm-Dummy-5208"
Add-Type @'
using System;using System.Runtime.InteropServices;using System.Text;
public class MMU {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr GetPropW(IntPtr h, string name);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int size);
  public static RECT Frame(IntPtr h) { RECT r; if (DwmGetWindowAttribute(h, 9, out r, 16) != 0) GetWindowRect(h, out r); return r; }
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static IntPtr Find(uint pid, string cls, bool vis) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls && (!vis || IsWindowVisible(h))) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
}
'@
function Pump([int]$ms) { $t = [Environment]::TickCount; while ([Environment]::TickCount - $t -lt $ms) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 20 } }
function Start1Key() {
  $script:p = Start-Process $exe -PassThru; $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $script:m = [MMU]::Find([uint32]$script:p.Id, "OneKeyMainWindow$suffix", $false); if ($script:m -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 100 }
  if ($script:m -eq [IntPtr]::Zero) { throw "no main window" }
  Pump 900
}
function Quit1Key() { if ($script:p -and -not $script:p.HasExited) { [void][MMU]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i = 0; $i -lt 50 -and -not $script:p.HasExited; $i++) { Start-Sleep -Milliseconds 100 }; if (-not $script:p.HasExited) { Stop-Process -Id $script:p.Id -Force } } }
function Has($id) { [MMU]::GetDlgItem($m, $id) -ne [IntPtr]::Zero }
function Click($id, $ms = 700) { [void][MMU]::PostMessageW([MMU]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Pump $ms }
function SetText($id, $s) { [void][MMU]::SendMessageW([MMU]::GetDlgItem($m, $id), 0x000C, [IntPtr]::Zero, [string]$s) }
function Prop($n) { [int64][MMU]::GetPropW($m, $n) }
function CatWnd() { [MMU]::Find([uint32]$p.Id, "OneKeyCat", $true) }
function CatShown() { (Prop 'OneKeyTestWalker') -eq 1 -and (CatWnd) -ne [IntPtr]::Zero }
function Perch() { Prop 'OneKeyTestCatPerch' }
function Drop([int]$x, [int]$y) { [void][MMU]::PostMessageW((CatWnd), 0x8034, [IntPtr]$x, [IntPtr]$y) }
function CatRect() { $r = New-Object MMU+RECT; [void][MMU]::GetWindowRect((CatWnd), [ref]$r); $r }
function WaitSettled([int]$ms = 10000) {
  $t = [Environment]::TickCount; $last = ""; $since = $t
  Pump 200
  while ([Environment]::TickCount - $t -lt $ms) {
    Pump 100
    $now = "$(Prop 'OneKeyTestCatX')|$(Prop 'OneKeyTestCatY')|$(Perch)|$(Prop 'OneKeyTestCatWorkL')"
    if ($now -ne $last -or (Prop 'OneKeyTestCatBusy') -ne 0) { $last = $now; $since = [Environment]::TickCount } elseif ([Environment]::TickCount - $since -ge 500) { break }
  }
  $last
}
function Raise($f) { [void][MMU]::SetWindowPos($f.Handle, [IntPtr](-1), 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor 0x0010); Pump 200 }
function NewForm([string]$title, [int]$x, [int]$y, [int]$w, [int]$h) {
  $f = New-Object System.Windows.Forms.Form
  $f.Text = $title; $f.StartPosition = "Manual"; $f.Left = $x; $f.Top = $y; $f.Width = $w; $f.Height = $h; $f.ShowInTaskbar = $false
  $f.TopMost = $true; $f.Show(); Pump 300; $f
}
# 1Key again (same config: master and widget mode already set); cat shown
function Restart1Key() {
  Quit1Key; Pump 600
  Start1Key
  SetText 101 $master; Click 103 1500
  [void][MMU]::PostMessageW($m, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero); Pump 1200   # to the tray
  $t = [Environment]::TickCount; while ([Environment]::TickCount - $t -lt 6000 -and -not (CatShown)) { Pump 100 }
  Pump 1500
}
# where the cat is: "monitor-work-left|perched|inside the work area horizontally|sits at the bottom of that work area"
function CatPlace([System.Drawing.Rectangle]$wa) {   # not 'Where' - that is an alias of Where-Object and wins over a function
  $r = CatRect; $sink = $r.B - (Prop 'OneKeyTestCatWorkB')
  "$((Prop 'OneKeyTestCatWorkL') -eq $wa.Left)|$((Perch) -ne 0)|$($r.L -ge $wa.Left -and $r.R -le $wa.Right)|$((Prop 'OneKeyTestCatWorkB') -eq $wa.Bottom -and $sink -ge 0 -and $sink -le ($r.B - $r.T) / 3)"
}

try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_LOCKWIDGET = "0"; $env:ONEKEY_TEST_NO_AUTOLOCK = "1"
  $env:ONEKEY_TEST_CAT_LOG = $log; $env:ONEKEY_TEST_PERCH_CLASS = "WindowsForms10"
  $wa = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
  $wb = $second.WorkingArea
  "primary work $wa, second work $wb (bounds $($second.Bounds))"
  Start1Key
  SetText 101 $master; SetText 102 $master; Click 103 1500
  if (-not (Has 203)) { throw "setup: no list after the master" }
  [void][MMU]::SetWindowPos($m, [IntPtr]::Zero, $wa.Left + 10, $wa.Top + 10, 0, 0, 0x0001 -bor 0x0004 -bor 0x0010)
  Click 220
  [void][MMU]::PostMessageW([MMU]::GetDlgItem($m, 2032), 0x0100, [IntPtr]0x20, [IntPtr]::Zero); Pump 300
  Click 2012 900
  [void][MMU]::PostMessageW($m, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero); Pump 1500   # to the tray
  $t0 = [Environment]::TickCount; while ([Environment]::TickCount - $t0 -lt 6000 -and -not (CatShown)) { Pump 100 }
  Pump 2500
  $dpiA = Prop 'OneKeyTestCatDpi'; $catW1 = Prop 'OneKeyTestCatW'   # not $wA: PowerShell names ignore case ($wa)
  Check MM00 "True|True|False|True|True" "$(CatShown)|$(CatPlace $wa)" "cat on the primary monitor (dpi $dpiA, cat width $catW1)"

  # MM01 drop on the second monitor, high up
  Drop ([int](($wb.Left + $wb.Right) / 2)) ([int]($wb.Top + 300))
  $s1 = WaitSettled
  $dpiB = Prop 'OneKeyTestCatDpi'; $catW2 = Prop 'OneKeyTestCatW'
  Check MM01 "True|True|False|True|True" "$(CatShown)|$(CatPlace $wb)" "dropped on the second monitor ($s1; dpi $dpiA -> $dpiB, cat width $catW1 -> $catW2)"

  # MM02 restart: home monitor remembered
  Restart1Key
  Check MM02 "True|True|False|True|True" "$(CatShown)|$(CatPlace $wb)" "1Key started again: the cat is on the second monitor"

  # MM03 perch on a form on the second monitor
  $F = NewForm "multimon F" ($wb.Left + 100) ($wb.Top + 500) ([Math]::Min(700, $wb.Width - 200)) 360
  Raise $F
  $r = [MMU]::Frame($F.Handle)
  "F on the second monitor: $($r.L),$($r.T),$($r.R),$($r.B)"
  Drop ([int](($r.L + $r.R) / 2)) ([int]($r.T - 260))
  $s3 = WaitSettled
  Check MM03 "True|True" "$((Perch) -eq [int64]$F.Handle)|$((Prop 'OneKeyTestCatWorkL') -eq $wb.Left)" "dropped above F on the second monitor: on F ($s3)"

  # MM04 F moved to the primary monitor: the cat goes with it
  [void][MMU]::SetWindowPos($F.Handle, [IntPtr](-1), $wa.Left + 400, $wa.Top + 400, 0, 0, 0x0001 -bor 0x0010); Pump 800
  $s4 = WaitSettled
  $r = [MMU]::Frame($F.Handle); $c = CatRect; $cx = [int](($c.L + $c.R) / 2)
  Check MM04 "True|True|True" "$((Perch) -eq [int64]$F.Handle)|$((Prop 'OneKeyTestCatWorkL') -eq $wa.Left)|$($cx -ge $r.L -and $cx -le $r.R -and $c.B -le $r.T + ($c.B - $c.T) / 3)" "F moved to the primary monitor: the cat came along ($s4; F $($r.L)..$($r.R) top $($r.T), cat centre $cx bottom $($c.B))"

  # MM05 F closed: falls to the primary taskbar
  $F.Close(); $F = $null
  $s5 = WaitSettled
  Check MM05 "True|True|False|True|True" "$(CatShown)|$(CatPlace $wa)" "F closed: the cat fell to the primary taskbar ($s5)"

  # MM06 restart: on the primary monitor
  Restart1Key
  Check MM06 "True|True|False|True|True" "$(CatShown)|$(CatPlace $wa)" "1Key started again: the cat is on the primary monitor"
} catch { Add-Failure ("exception: " + $_) }
finally {
  if ($F -is [System.Windows.Forms.Form]) { try { $F.Close() } catch { } }
  Quit1Key; Stop-TestInstances $suffix
  foreach ($v in "ONEKEY_TEST_CAT_LOG", "ONEKEY_TEST_LOCKWIDGET", "ONEKEY_TEST_NO_AUTOLOCK", "ONEKEY_TEST_PERCH_CLASS") { Remove-Item "env:$v" -ErrorAction Ignore }
  "log kept: $log"
}
Complete-Checks
