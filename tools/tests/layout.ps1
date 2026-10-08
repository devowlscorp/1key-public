# Small screens and scaling (T5/T13, plus chip position from T11 and dialog height from T16).
# Simulates a 1366x768 screen with a 40-logical-px taskbar at 100 / 125 / 150 % through ONEKEY_TEST_WORKAREA=w,h,dpi.
# For each: the main window, the list's required controls, the settings (inline or the settings screen), the edit screen,
# the input chip and a confirmation dialog must lie inside the work area; every required control must be reachable
# with Tab, and when it has the focus it must be fully visible (not under the pinned bottom bar).
# The simulated work area starts at the top-left of the primary monitor, so test windows appear there briefly.
# Judgement: tools\tests\lib\Check.ps1 (T18). ASCII only.
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
$Dpis = 96, 120, 144
$req = @('LY00')
foreach ($d in $Dpis) { foreach ($n in 1..9) { $req += "LY-$d-$n" } }
Start-Checks -Required $req

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($args.Count -gt 0) { $args[0] } else { Get-DefaultExe }
$suffix = ".ly"
$cfg = "$sp\layout_cfg"; if (Test-Path $cfg) { Get-ChildItem $cfg -Recurse | Remove-Item -Force -Recurse -ErrorAction Ignore }; New-Item -ItemType Directory -Force $cfg | Out-Null
Add-Type @'
using System;using System.Runtime.InteropServices;using System.Text;
public class U {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint tid, ref GUITHREADINFO g);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; }
  [StructLayout(LayoutKind.Sequential)] public struct GUITHREADINFO { public int cbSize; public int flags; public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret; public RECT rcCaret; }
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static IntPtr FindCls(uint pid, string cls) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls && IsWindowVisible(h)) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
  public static IntPtr Focus(IntPtr w) { uint pid; uint tid = GetWindowThreadProcessId(w, out pid); var g = new GUITHREADINFO(); g.cbSize = Marshal.SizeOf(g); return GetGUIThreadInfo(tid, ref g) ? g.hwndFocus : IntPtr.Zero; }
  public static RECT Client(IntPtr w) { RECT r; GetClientRect(w, out r); var p = new POINT(); ClientToScreen(w, ref p); r.L += p.X; r.R += p.X; r.T += p.Y; r.B += p.Y; return r; }
}
'@
function SetText($id, $s) { [void][U]::SendMessageW([U]::GetDlgItem($m, $id), 0x000C, [IntPtr]::Zero, [string]$s) }
function Click($id) { [void][U]::PostMessageW([U]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 600 }
function Has($id) { [U]::GetDlgItem($m, $id) -ne [IntPtr]::Zero }
function CloseBox($cmd = 2) { for ($i=0;$i -lt 10;$i++) { $b = [U]::FindCls([uint32]$p.Id, "OneKeyDialog"); if ($b -ne [IntPtr]::Zero) { [void][U]::PostMessageW($b, 0x0111, [IntPtr]$cmd, [IntPtr]::Zero); Start-Sleep -Milliseconds 300; return }; Start-Sleep -Milliseconds 100 } }
function RectOf($h) { $r = New-Object 'U+RECT'; [void][U]::GetWindowRect($h, [ref]$r); $r }
function Inside($r, $o) { ($r.L -ge $o.L) -and ($r.T -ge $o.T) -and ($r.R -le $o.R) -and ($r.B -le $o.B) }
function Txt($r) { "($($r.L),$($r.T))-($($r.R),$($r.B))" }
# every id in $ids exists and lies inside the work area; returns "" or the offenders
function AllInside($ids) { $bad = @(); foreach ($id in $ids) { $h = [U]::GetDlgItem($m, $id); if ($h -eq [IntPtr]::Zero) { $bad += "$id(missing)" } elseif (-not (Inside (RectOf $h) $work)) { $bad += "$id" + (Txt (RectOf $h)) } }; $bad -join " " }
# press Tab repeatedly; each focused control must be fully visible (inside the client area and, unless it is part of the
# bottom bar, above the bar). Returns the list of problems ("" when all required ids were reached and visible).
function TabWalk($required, $barIds, $barRef) {
  $seen = @{}; $problems = @()
  for ($i = 0; $i -lt 70; $i++) {
    $f = [U]::Focus($m)
    if ($f -ne [IntPtr]::Zero) {
      $ctl = $f; if ([U]::GetParent($ctl) -ne $m) { $pp = [U]::GetParent($ctl); if ($pp -ne [IntPtr]::Zero -and [U]::GetParent($pp) -eq $m) { $ctl = $pp } }
      $id = [U]::GetDlgCtrlID($ctl)
      if ($required -contains $id -and -not $seen.ContainsKey($id)) {
        $seen[$id] = 1
        $r = RectOf $ctl; $cl = [U]::Client($m)
        $ok = Inside $r $cl
        if ($ok -and $barRef -ne 0 -and -not ($barIds -contains $id)) { $bar = RectOf ([U]::GetDlgItem($m, $barRef)); $ok = $r.B -le $bar.T }
        if (-not $ok) { $problems += "$id hidden " + (Txt $r) }
      }
      [void][U]::PostMessageW($f, 0x0100, [IntPtr]0x09, [IntPtr]0x000F0001); Start-Sleep -Milliseconds 90
    } else { Start-Sleep -Milliseconds 90 }
  }
  foreach ($id in $required) { if (-not $seen.ContainsKey($id)) { $problems += "$id not reached" } }
  $problems -join "; "
}
function Launch() {
  $script:p = Start-Process $exe -PassThru
  $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $script:m = [U]::FindCls([uint32]$script:p.Id, "OneKeyMainWindow$suffix"); if ($script:m -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 100 }
  if ($script:m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 700
}
function Quit() { [void][U]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i=0; $i -lt 50 -and -not $p.HasExited; $i++) { Start-Sleep -Milliseconds 100 } }

try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_WORKAREA = $null
  # prepare 12 items once (normal screen); every scaled run reuses this file
  Launch
  SetText 101 "Master1234"; SetText 102 "Master1234"; Click 103
  # auto-lock off first: it follows the SYSTEM input idle time, and posted messages are not input, so on an unattended
  # PC the app locked itself about 15 s into the fill (seen 2026-09-29, 19 min idle: LY00 failed after item 6)
  Click 220; [void][U]::PostMessageW([U]::GetDlgItem($m, 2005), 0x0100, [IntPtr]0x24, [IntPtr]::Zero); Start-Sleep -Milliseconds 200
  Click 2012; CloseBox
  for ($k = 1; $k -le 12; $k++) { Click 203; Click $(if ($k -eq 1) { 4007 } else { 4001 }); SetText 301 ("Item {0:D2}" -f $k); SetText 302 "x$k"; Click 310; CloseBox }
  Check LY00 $true (Has 1000) "12 items prepared"
  Quit

  foreach ($d in $Dpis) {
    $env:ONEKEY_TEST_WORKAREA = "1366,768,$d"
    $work = New-Object 'U+RECT'; $work.L = 0; $work.T = 0; $work.R = 1366; $work.B = 768 - [int][Math]::Floor(40 * $d / 96)
    Launch
    SetText 101 "Master1234"; Click 103
    Check "LY-$d-1" $true (Inside (RectOf $m) $work) ("$d dpi: main window inside the work area " + (Txt (RectOf $m)) + " / " + (Txt $work))
    Check "LY-$d-2" "" (AllInside @(230, 1000, 1100, 203, 220, 2014, 2015)) "$d dpi: list controls inside the work area"
    Check "LY-$d-3" "" (TabWalk @(230, 1000, 1100, 203, 220) @() 0) "$d dpi: list controls reachable with Tab"

    # settings: inline when it fits, otherwise the settings screen with a pinned [cancel]/[save] bar
    Click 220
    for ($i = 0; $i -lt 30 -and -not (Has 2012); $i++) { Start-Sleep -Milliseconds 100 }
    $screen = if (Has 241) { "settings screen" } else { "inline" }
    Check "LY-$d-4" "" (AllInside @(2012)) "$d dpi: settings ($screen) save button inside the work area"
    $setIds = @(2002, 2008, 2030, 2031, 2005, 213, 2006, 2007, 2003, 210, 212, 2012)
    $barIds = @(2012, 241); $barRef = if (Has 241) { 2012 } else { 0 }
    Check "LY-$d-5" "" (TabWalk $setIds $barIds $barRef) "$d dpi: settings ($screen) controls reachable with Tab and visible when focused"
    if (Has 241) { Click 241 } else { Click 220 }

    # edit screen
    Click 1000
    Check "LY-$d-6" "" (AllInside @(310, 309)) "$d dpi: edit screen save/cancel inside the work area"
    # Enter send: Right on the 3-way row (0.3.5x: one segmented row replaced the Enter / "no Enter in browsers" switches) - a change,
    # so leaving asks to discard. Tab order: name, use (348), input, show, + (339), shortcut, Enter (305), method (306), bar buttons
    [void][U]::PostMessageW([U]::GetDlgItem($m, 305), 0x0100, [IntPtr]0x27, [IntPtr]::Zero); Start-Sleep -Milliseconds 300
    Check "LY-$d-7" "" (TabWalk @(301, 348, 302, 303, 339, 304, 305, 306, 307, 308, 309, 310) @(307, 308, 309, 310) 310) "$d dpi: edit controls reachable with Tab and visible when focused"
    Click 311; CloseBox 6   # the Enter change is not saved: "discard?" -> Yes

    # input chip position
    Click 1100; Start-Sleep -Milliseconds 300
    $chip = [U]::FindCls([uint32]$p.Id, "OneKeyChip")
    Check "LY-$d-8" $true (($chip -ne [IntPtr]::Zero) -and (Inside (RectOf $chip) $work)) ("$d dpi: input chip inside the work area " + $(if ($chip -ne [IntPtr]::Zero) { Txt (RectOf $chip) } else { "(none)" }))
    if ($chip -ne [IntPtr]::Zero) { [void][U]::PostMessageW([U]::GetDlgItem($chip, 12), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 500 }

    # a confirmation dialog
    [void][U]::PostMessageW([U]::GetDlgItem($m, 2015), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 600
    $dlg = [U]::FindCls([uint32]$p.Id, "OneKeyDialog")
    Check "LY-$d-9" $true (($dlg -ne [IntPtr]::Zero) -and (Inside (RectOf $dlg) $work)) ("$d dpi: confirmation dialog inside the work area " + $(if ($dlg -ne [IntPtr]::Zero) { Txt (RectOf $dlg) } else { "(none)" }))
    if ($dlg -ne [IntPtr]::Zero) { [void][U]::PostMessageW($dlg, 0x0111, [IntPtr]7, [IntPtr]::Zero); Start-Sleep -Milliseconds 300 }
    Quit
  }
} catch { Add-Failure ("exception: " + $_) }
finally { $env:ONEKEY_TEST_WORKAREA = $null; Stop-TestInstances $suffix }
Complete-Checks
