# Taskbar mascot window test (0.3.82; user decisions 2026-10-06, Codex 09:43 conditions). Own config folder and suffix; the real 1Key is
# not touched. Needs: primary monitor taskbar at the bottom, not auto-hidden (otherwise WK00 fails and says why). Windows appear: run
# while nobody uses the PC.
# WK00 environment: taskbar at the bottom of the primary monitor, not auto-hidden (the feature's stage-1 scope)
# WK01 setting off (default): closing the window shows no mascot
# WK02 setting on + window closed: the mascot window appears, its bottom sits on the work-area bottom (taskbar top edge), inside the
#      work area, TOPMOST + NOACTIVATE + TOOLWINDOW, it is not the foreground window and has no focus
# WK03 it walks: its position changes over time (resting phases are waited out)
# WK04 a click (down + up inside): the 1Key window opens and the mascot hides
# WK05 a cancelled click (down inside, up outside) opens nothing; the mascot stays
# WK06 lock (synthetic session lock notice): the mascot hides; after unlocking and closing again it is back
# WK07 synthetic "busy / full screen" state: hidden within the 2 s check; state gone: back by itself (low-rate check)
# WK08 synthetic "system panel open" state: hidden; gone: back
# WK09 TaskbarCreated (Explorer restart) and WM_SETTINGCHANGE: still on the work-area bottom afterwards
# WK10 setting off again: closing shows no mascot
# WK11 resources: GDI / USER object counts back to the same after 20 show/hide cycles (memory and CPU are measured and printed, not judged)
# ---- 0.3.84 (Codex 11:00 R83-1 / R83-2, user 2026-10-06)
# WK12 art build fails (injected: DIB / drawing setup / layered push): not shown, reason code kept, GDI/USER counts stable while it
#      retries every 2 s; the fault removed: shown again by itself
# WK13 the button is held down when the environment turns unsuitable (synthetic busy): hidden within the check, and releasing the
#      button afterwards opens nothing
# WK14 list top bar: with widget mode on, the tray button is the Accessibility glyph (U+E776, off: Download U+E896) and pressing it sends 1Key to the
#      tray so the mascot appears; with it off the button is (-) again
# ---- 0.3.87 (user 2026-10-06: more actions, walk toward the heading, left quarter only)
# WK15 range: over 20 s every observed position stays inside the right fifth of the work area (0.3.91 user: over the tray area)
# WK16 situations: within 90 s at least one situation clip (Assets/clips) is played
param([string]$Exe = "", [switch]$NoMeasure, [switch]$Shot)
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
$ids = @(); 0..16 | ForEach-Object { $ids += "WK{0:D2}" -f $_ }
Start-Checks -Required $ids
$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$suffix = ".wk"
$base = [IO.Path]::GetFullPath($sp).TrimEnd('\')
$real = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA "1Key")).TrimEnd('\')
if ($base -eq $real -or $base.StartsWith($real + "\", [StringComparison]::OrdinalIgnoreCase)) { throw "test root is inside the real settings folder: $base" }
$root = Join-Path $base ("walker-{0:yyyyMMdd-HHmmss}-{1}" -f (Get-Date), $PID)
New-Item -ItemType Directory $root -ErrorAction Stop | Out-Null
$cfg = "$root\cfg"; New-Item -ItemType Directory $cfg | Out-Null
$master = "Wk-Dummy-4412"
Add-Type @'
using System;using System.Runtime.InteropServices;using System.Text;
public class WKU {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr GetPropW(IntPtr h, string name);
  [DllImport("user32.dll")] public static extern int GetWindowLongW(IntPtr h, int i);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern uint RegisterWindowMessageW(string s);
  [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr proc, uint flags);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct ABD { public uint cbSize; public IntPtr hWnd; public uint cb, uEdge; public RECT rc; public IntPtr lParam; }
  [DllImport("shell32.dll")] public static extern UIntPtr SHAppBarMessage(uint msg, ref ABD d);
  [StructLayout(LayoutKind.Sequential)] public struct GTI { public int cbSize, flags; public IntPtr active, focus, capture, menuOwner, moveSize, caret; public int l, t, r, b; }
  [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint tid, ref GTI g);
  public static IntPtr Focus(IntPtr w) { uint p; uint tid = GetWindowThreadProcessId(w, out p); var g = new GTI(); g.cbSize = Marshal.SizeOf(typeof(GTI)); return GetGUIThreadInfo(tid, ref g) ? g.focus : IntPtr.Zero; }
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static IntPtr Find(uint pid, string cls, bool visibleOnly) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls && (!visibleOnly || IsWindowVisible(h))) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
}
'@
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;using System.Runtime.InteropServices;using System.Text;
public class WKU2 { [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n); }
"@
function Start1Key() {
  $script:p = Start-Process $exe -PassThru; $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $script:m = [WKU]::Find([uint32]$script:p.Id, "OneKeyMainWindow$suffix", $false); if ($script:m -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 100 }
  if ($script:m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 900
}
function Quit1Key() { if ($script:p -and -not $script:p.HasExited) { [void][WKU]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i = 0; $i -lt 40 -and -not $script:p.HasExited; $i++) { Start-Sleep -Milliseconds 100 }; if (-not $script:p.HasExited) { Stop-Process -Id $script:p.Id -Force } } }
function Has($id) { [WKU]::GetDlgItem($m, $id) -ne [IntPtr]::Zero }
function Click($id, $ms = 600) { [void][WKU]::PostMessageW([WKU]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds $ms }
function SetText($id, $s) { [void][WKU]::SendMessageW([WKU]::GetDlgItem($m, $id), 0x000C, [IntPtr]::Zero, [string]$s) }
function Prop($n) { [int][WKU]::GetPropW($m, $n) }
function Walker() { [WKU]::Find([uint32]$p.Id, "OneKeyWalker", $true) }
function WaitWalker([bool]$want, $ms = 4000) { $t = [Environment]::TickCount; while ([Environment]::TickCount - $t -lt $ms) { if ((Walker) -ne [IntPtr]::Zero -eq $want) { return $true }; Start-Sleep -Milliseconds 100 }; $false }
function CloseMain() { [void][WKU]::PostMessageW($m, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 800 }   # WM_CLOSE -> to the tray
function ShowMain() { [void][WKU]::PostMessageW($m, 0x8001, [IntPtr]::Zero, [IntPtr]0x0202) }   # tray icon left click
function SetWalkerOption([bool]$on) {
  if (-not (Has 2012)) { Click 220 }
  $cur = [int][WKU]::SendMessageW([WKU]::GetDlgItem($m, 2032), 0x00F0, [IntPtr]::Zero, [IntPtr]::Zero) -eq 1
  if ($cur -ne $on) { [void][WKU]::PostMessageW([WKU]::GetDlgItem($m, 2032), 0x0100, [IntPtr]0x20, [IntPtr]::Zero); Start-Sleep -Milliseconds 300 }
  Click 2012 900
}
function WorkBottom() { [Windows.Forms.Screen]::PrimaryScreen.WorkingArea.Bottom }
function Rect($h) { $r = New-Object WKU+RECT; [void][WKU]::GetWindowRect($h, [ref]$r); $r }
function Gui() { $pr = Get-Process -Id $p.Id; "$([WKU]::GetGuiResources($pr.Handle, 0))/$([WKU]::GetGuiResources($pr.Handle, 1))" }

try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_LOCKWIDGET = "0"; $env:ONEKEY_TEST_NO_AUTOLOCK = "1"
  # WK00 environment
  $abd = New-Object WKU+ABD; $abd.cbSize = [Runtime.InteropServices.Marshal]::SizeOf([type][WKU+ABD]); [void][WKU]::SHAppBarMessage(5, [ref]$abd)
  $st = New-Object WKU+ABD; $st.cbSize = $abd.cbSize; $auto = ([uint64][WKU]::SHAppBarMessage(4, [ref]$st)) -band 1
  Check WK00 "3|0" "$($abd.uEdge)|$auto" "environment: taskbar at the bottom (edge 3), not auto-hidden"

  Start1Key
  SetText 101 $master; SetText 102 $master; Click 103 1500
  if (-not (Has 203)) { throw "setup: no list after creating the master" }

  # WK01 off by default
  CloseMain
  Check WK01 "False|0" "$(WaitWalker $true 2500)|$(Prop 'OneKeyTestWalker')" "setting off (default): no mascot after closing the window"
  ShowMain; Start-Sleep -Milliseconds 800

  # baseline for the measurement: setting off, window closed
  $base0 = ""
  if (-not $NoMeasure) {
    CloseMain
    $c0 = (Get-Process -Id $p.Id).TotalProcessorTime.TotalMilliseconds; Start-Sleep -Seconds 30
    $base0 = "off+closed $([math]::Round((((Get-Process -Id $p.Id).TotalProcessorTime.TotalMilliseconds) - $c0) / 300, 2)) % of one core, private $([math]::Round((Get-Process -Id $p.Id).PrivateMemorySize64 / 1MB, 1)) MB"
    ShowMain; Start-Sleep -Milliseconds 800
  }

  # WK02 on + closed
  SetWalkerOption $true
  $fg0 = [WKU]::GetForegroundWindow()
  CloseMain
  $seen = WaitWalker $true 4000
  $w = Walker
  $r = Rect $w; $wb = WorkBottom; $sa = [Windows.Forms.Screen]::PrimaryScreen.WorkingArea
  $ex = [WKU]::GetWindowLongW($w, -20)
  $styles = "$((($ex -band 0x8) -ne 0))/$((($ex -band 0x08000000) -ne 0))/$((($ex -band 0x80) -ne 0))"
  $notFg = [WKU]::GetForegroundWindow() -ne $w
  $noFocus = [WKU]::Focus($w) -ne $w
  $bottomOk = ($r.B -le $wb) -and ($r.B -ge $wb - 4)
  Check WK02 "True|True|True|True/True/True|True|True" "$seen|$bottomOk|$($r.L -ge $sa.Left -and $r.R -le $sa.Right)|$styles|$notFg|$noFocus" "on + window closed: mascot on the work-area bottom (rect $($r.L),$($r.T)-$($r.R),$($r.B), work bottom $wb), inside, TOPMOST/NOACTIVATE/TOOLWINDOW, not foreground, no focus"

  # WK03 walks
  $m0 = Prop 'OneKeyTestWalkerMoves'; $x0 = (Rect (Walker)).L; $moved = $false
  for ($i = 0; $i -lt 150 -and -not $moved; $i++) { Start-Sleep -Milliseconds 100; $moved = (Prop 'OneKeyTestWalkerMoves') -gt $m0 + 5 -and (Rect (Walker)).L -ne $x0 }
  Check WK03 "True" "$moved" "the mascot walks (position changes within 15 s)"
  if ($Shot) {   # a picture of the taskbar strip around the mascot, for a person to look at (not judged)
    Add-Type -AssemblyName System.Drawing
    $wr = Rect (Walker); $bw = 700; $sx = [Math]::Max(0, [Math]::Min($wr.L - 300, [Windows.Forms.Screen]::PrimaryScreen.Bounds.Right - $bw)); $sy = $wr.T - 40; $bh = ($wr.B - $sy) + 50
    $bm = New-Object Drawing.Bitmap $bw, $bh; $gg = [Drawing.Graphics]::FromImage($bm); $gg.CopyFromScreen($sx, $sy, 0, 0, (New-Object Drawing.Size $bw, $bh)); $gg.Dispose()
    $bm.Save("$root\walker-shot.png"); $bm.Dispose(); "picture: $root\walker-shot.png"
  }

  # WK15 right fifth only
  $sa15 = [Windows.Forms.Screen]::PrimaryScreen.WorkingArea; $limit = $sa15.Right - $sa15.Width / 5; $out15 = 0
  for ($i = 0; $i -lt 40; $i++) { $r15 = Rect (Walker); if (($r15.L + $r15.R) / 2 -lt $limit - 1 -or $r15.R -gt $sa15.Right) { $out15++ }; Start-Sleep -Milliseconds 500 }
  Check WK15 "0" "$out15" "20 s of positions all inside the right fifth (window centre >= $limit, right edge on screen; the window is wider than the mascot for lying clips)"
  # WK16 a situation clip is played
  $c16 = Prop 'OneKeyTestWalkerClips'; $played = $false
  for ($i = 0; $i -lt 180 -and -not $played; $i++) { Start-Sleep -Milliseconds 500; $played = (Prop 'OneKeyTestWalkerClips') -gt $c16 }
  Check WK16 "True" "$played" "a situation clip plays within 90 s"

  # WK05 cancelled click first (down inside, up outside)
  $w = Walker; $rr = Rect $w; $cx = [int](($rr.R - $rr.L) / 2); $cy = [int](($rr.B - $rr.T) / 2)
  [void][WKU]::SendMessageW($w, 0x0201, [IntPtr]1, [IntPtr]($cx -bor ($cy -shl 16))); Start-Sleep -Milliseconds 150
  [void][WKU]::SendMessageW($w, 0x0202, [IntPtr]0, [IntPtr]((-30 -band 0xFFFF) -bor ($cy -shl 16))); Start-Sleep -Milliseconds 700
  Check WK05 "False|True" "$([WKU]::IsWindowVisible($m))|$((Walker) -ne [IntPtr]::Zero)" "down inside, up outside: nothing opens, the mascot stays"

  # WK04 click
  $w = Walker; $rr = Rect $w; $cx = [int](($rr.R - $rr.L) / 2); $cy = [int](($rr.B - $rr.T) / 2)
  [void][WKU]::SendMessageW($w, 0x0201, [IntPtr]1, [IntPtr]($cx -bor ($cy -shl 16))); Start-Sleep -Milliseconds 150
  [void][WKU]::SendMessageW($w, 0x0202, [IntPtr]0, [IntPtr]($cx -bor ($cy -shl 16))); Start-Sleep -Milliseconds 900
  Check WK04 "True|True" "$([WKU]::IsWindowVisible($m))|$(WaitWalker $false 2000)" "click on the mascot: the 1Key window opens, the mascot hides"

  # WK06 lock / unlock
  CloseMain; [void](WaitWalker $true 4000)
  [void][WKU]::PostMessageW($m, 0x02B1, [IntPtr]7, [IntPtr]::Zero); Start-Sleep -Milliseconds 1200   # synthetic session lock notice
  $hidLock = WaitWalker $false 2000
  ShowMain; Start-Sleep -Milliseconds 600
  SetText 101 $master; Click 103 1500
  $back = $false; if (Has 203) { CloseMain; $back = WaitWalker $true 4000 }
  Check WK06 "True|True" "$hidLock|$back" "lock hides the mascot; unlocked and closed again: it is back"

  # WK07 / WK08 synthetic busy and panel states
  foreach ($k in @(@("WK07", "test-walker-busy", "busy / full screen"), @("WK08", "test-walker-panel", "system panel open"))) {
    New-Item -ItemType File "$cfg\$($k[1])" -Force | Out-Null
    $hid = WaitWalker $false 3500
    Remove-Item "$cfg\$($k[1])" -Force
    $ret = WaitWalker $true 4500
    Check $k[0] "True|True" "$hid|$ret" "synthetic $($k[2]): hidden within the check, back by itself when it is gone"
  }

  # WK09 TaskbarCreated / settings change
  $tbc = [WKU]::RegisterWindowMessageW("TaskbarCreated")
  [void][WKU]::PostMessageW($m, $tbc, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 300
  [void][WKU]::SendMessageW($m, 0x001A, [IntPtr]0x2F, [IntPtr]::Zero); Start-Sleep -Milliseconds 300
  $ok9 = WaitWalker $true 4500; $r9 = Rect (Walker); $wb = WorkBottom
  Check WK09 "True|True" "$ok9|$(($r9.B -le $wb) -and ($r9.B -ge $wb - 4))" "after TaskbarCreated and a work-area change: shown again on the work-area bottom"

  # WK13 held button + environment change
  $w = Walker; $rr = Rect $w; $cx = [int](($rr.R - $rr.L) / 2); $cy = [int](($rr.B - $rr.T) / 2)
  [void][WKU]::SendMessageW($w, 0x0201, [IntPtr]1, [IntPtr]($cx -bor ($cy -shl 16))); Start-Sleep -Milliseconds 150
  New-Item -ItemType File "$cfg\test-walker-busy" -Force | Out-Null
  $hid13 = WaitWalker $false 3500
  [void][WKU]::SendMessageW($w, 0x0202, [IntPtr]0, [IntPtr]($cx -bor ($cy -shl 16))); Start-Sleep -Milliseconds 900
  $opened13 = [WKU]::IsWindowVisible($m)
  Remove-Item "$cfg\test-walker-busy" -Force
  $back13 = WaitWalker $true 4500
  Check WK13 "True|False|True" "$hid13|$opened13|$back13" "held down while the environment turns busy: hidden, release opens nothing; back when the state is gone"

  # WK12 art build failures (EnvChanged rebuilds the art)
  $r12 = @()
  foreach ($f in @(@("test-walker-fail-dib", 8), @("test-walker-fail-draw", 8), @("test-walker-fail-push", 11))) {
    New-Item -ItemType File "$cfg\$($f[0])" -Force | Out-Null
    [void][WKU]::SendMessageW($m, 0x001A, [IntPtr]0x2F, [IntPtr]::Zero)   # WM_SETTINGCHANGE -> rebuild
    $hid = WaitWalker $false 3000; Start-Sleep -Milliseconds 2500
    $ga = Gui; Start-Sleep -Milliseconds 4500; $gb = Gui   # two more retries
    $why = Prop 'OneKeyWalkerWhy'
    Remove-Item "$cfg\$($f[0])" -Force
    $ret = WaitWalker $true 4500
    $r12 += "$hid/$why/$($ga -eq $gb)/$ret"
  }
  Check WK12 "True/8/True/True,True/8/True/True,True/11/True/True" "$($r12 -join ',')" "art build / push failures: hidden with reason 8 / 8 / 11, GDI/USER stable while retrying, shown again once the fault is gone"

  # WK14 widget button in the list top bar
  ShowMain; Start-Sleep -Milliseconds 900
  if (-not (Has 203)) { Click 240 }
  $sb = New-Object Text.StringBuilder 16; [void][WKU2]::GetWindowTextW([WKU]::GetDlgItem($m, 2016), $sb, 16); $glyph = [int][char]($sb.ToString() + " ")[0]
  Click 2016 900
  $hiddenMain = -not [WKU]::IsWindowVisible($m)
  $shown14 = WaitWalker $true 4000
  Check WK14 "59254|True|True" "$glyph|$hiddenMain|$shown14" "widget mode on: the top-bar button is the Accessibility glyph (U+E776); pressing it sends 1Key to the tray and the mascot appears"

  # WK11 resources (before WK10 switches it off)
  $g0 = Gui
  for ($i = 0; $i -lt 20; $i++) {
    New-Item -ItemType File "$cfg\test-walker-busy" -Force | Out-Null; [void](WaitWalker $false 3500)
    Remove-Item "$cfg\test-walker-busy" -Force; [void](WaitWalker $true 4500)
  }
  Start-Sleep -Milliseconds 500
  $g1 = Gui
  Check WK11 "$g0" "$g1" "GDI/USER objects (GDI/USER) the same after 20 hide/show cycles"
  if (-not $NoMeasure) {
    function Cpu() { (Get-Process -Id $p.Id).TotalProcessorTime.TotalMilliseconds }
    function Priv() { [math]::Round((Get-Process -Id $p.Id).PrivateMemorySize64 / 1MB, 1) }
    $n = [Environment]::ProcessorCount
    $c0 = Cpu; Start-Sleep -Seconds 30; $walk = ((Cpu) - $c0) / 300 / $n   # % of all logical cores over 30 s
    $c0 = Cpu; Start-Sleep -Seconds 30; $walk1 = ((Cpu) - $c0) / 300   # % of one core
    New-Item -ItemType File "$cfg\test-walker-busy" -Force | Out-Null; [void](WaitWalker $false 3500)
    $c0 = Cpu; Start-Sleep -Seconds 30; $hidden1 = ((Cpu) - $c0) / 300
    Remove-Item "$cfg\test-walker-busy" -Force
    "measure: $base0"
    "measure: walking/resting $([math]::Round($walk, 3)) % of all $n cores, $([math]::Round($walk1, 2)) % of one core; hidden (busy) $([math]::Round($hidden1, 2)) % of one core; private $(Priv) MB; GDI/USER $(Gui)"
  }

  # WK10 off again
  ShowMain; Start-Sleep -Milliseconds 800
  SetWalkerOption $false
  CloseMain
  Check WK10 "False" "$(WaitWalker $true 2500)" "setting off: no mascot after closing"
} catch { Add-Failure ("exception: " + $_) }
finally { Quit1Key; Stop-TestInstances $suffix; "kept run folder: $root" }
Complete-Checks
