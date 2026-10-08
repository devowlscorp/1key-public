# Switch-bounce test (the user's mouse re-presses 13-30 ms after a release): a click followed by a second press/release
# 13-60 ms later must flip a toggle switch ONCE; clicks separated by >= 150 ms must flip it every time.
# Since 0.2.35 the settings open on their own screen. Since 0.2.155 "start with Windows" (2001) is read-only (autostart paused),
# so the toggle under test is "after starting, hide to the tray when unlocked" (2002) there,
# and a bounced press on the settings row / the back button must change the screen only once.
# Judgement: tools\tests\lib\Check.ps1 (T18). ASCII only.
# Scratch folder: %TEMP%\1Key-tests (override with ONEKEY_TEST_DIR). Exe: first argument, default build\<csproj version>\1Key.exe.
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
$BounceGaps = 13, 15, 20, 30, 45, 60; $BounceHolds = 5, 45, 90
$ClickGaps = 150, 250, 600; $ClickHolds = 30, 90, 400, 1500
$req = @('BO00')
foreach ($g in $BounceGaps) { foreach ($h in $BounceHolds) { $req += "BA-$g-$h" } }
foreach ($g in $ClickGaps) { foreach ($h in $ClickHolds) { $req += "BB-$g-$h" } }
$req += 'BC01', 'BC02'
Start-Checks -Required $req   # 1 + 18 + 12 + 2 = 33

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($args.Count -gt 0) { $args[0] } else { Get-DefaultExe }
$suffix = ".bounce"
$cfg = "$sp\bounce_cfg"; if (Test-Path $cfg) { Get-ChildItem $cfg -Recurse | Remove-Item -Force -Recurse -ErrorAction Ignore }; New-Item -ItemType Directory -Force $cfg | Out-Null
Add-Type @'
using System;using System.Runtime.InteropServices;using System.Text;using System.Threading;
public class U {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int hh, uint f);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static IntPtr FindCls(uint pid, string cls) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
  public static int ClientH(IntPtr h) { RECT r; GetClientRect(h, out r); return r.B - r.T; }
  // press/release on the control with the given id, re-resolving the hwnd for the second press (the page may have been rebuilt)
  public static void Press(IntPtr m, int id, int hold) {
    IntPtr c = GetDlgItem(m, id); if (c == IntPtr.Zero) return;
    RECT rc; GetClientRect(c, out rc); IntPtr lp = (IntPtr)((rc.R/2) | ((rc.B/2) << 16));
    PostMessageW(c, 0x0201, (IntPtr)1, lp); Thread.Sleep(hold); PostMessageW(c, 0x0202, IntPtr.Zero, lp);
  }
}
'@
function SetText($h, $s) { [void][U]::SendMessageW($h, 0x000C, [IntPtr]::Zero, [string]$s) }
function Click($id) { [void][U]::PostMessageW([U]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 500 }
function Has($id) { [U]::GetDlgItem($m, $id) -ne [IntPtr]::Zero }
function Chk($id) { [int][U]::SendMessageW([U]::GetDlgItem($m, $id), 0x00F0, [IntPtr]::Zero, [IntPtr]::Zero) }
# what the window shows, for failure messages: list row / settings cancel / lock field / a box / toggle 2002 vs saved
function State() { "list=$(Has 203) settings=$(Has 241) lock=$(Has 101) box=$([U]::FindCls([uint32]$p.Id, 'OneKeyDialog') -ne [IntPtr]::Zero) toggle=$(Chk 2002)/saved=$saved" }
function Bounced($id, $gap, $hold) { [U]::Press($m, $id, $hold); Start-Sleep -Milliseconds $gap; [U]::Press($m, $id, $hold); Start-Sleep -Milliseconds 350 }

try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_WORKAREA = $TallScreen
  $p = Start-Process $exe -PassThru
  $m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $m = [U]::FindCls([uint32]$p.Id, "OneKeyMainWindow$suffix"); if ($m -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 100 }
  if ($m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 600
  [void][U]::SetWindowPos($m, [IntPtr]::Zero, 1400, 60, 0, 0, 0x0001 -bor 0x0004 -bor 0x0010)
  SetText ([U]::GetDlgItem($m,101)) "Master1234"; SetText ([U]::GetDlgItem($m,102)) "Master1234"; Click 103
  # auto-lock off first: auto-lock reads the SYSTEM idle time (posted messages are not input), so on a PC nobody has
  # touched for 10 minutes the test instance locks in the middle (2026-09-29: bounce/many/master/help failed that way
  # in one full run and passed on the rerun). Settings > auto-lock slider Home = off, save (toast), wait for it to go.
  Click 220; [void][U]::PostMessageW([U]::GetDlgItem($m, 2005), 0x0100, [IntPtr]0x24, [IntPtr]::Zero); Start-Sleep -Milliseconds 300
  Click 2012; Start-Sleep -Milliseconds 3500
  Confirm-AutoLockOff $cfg
  Click 220; Start-Sleep -Milliseconds 300
  Check BO00 $true ((Has 2002) -and (Has 241)) "settings screen open with the tray toggle (2002)"
  $saved = Chk 2002   # the saved value; C) needs the screen without unsaved changes

  # A) bounce pairs on the toggle: must flip exactly once
  foreach ($gap in $BounceGaps) {
    foreach ($hold in $BounceHolds) {
      $before = Chk 2002
      Bounced 2002 $gap $hold
      Check "BA-$gap-$hold" (1 - $before) (Chk 2002) "bounce gap=$gap hold=$hold flips once"
    }
  }
  # B) two real clicks >= 150 ms apart: flip twice (back to the start)
  foreach ($gap in $ClickGaps) {
    foreach ($hold in $ClickHolds) {
      $before = Chk 2002
      [U]::Press($m, 2002, $hold); Start-Sleep -Milliseconds $gap; $mid = Chk 2002; [U]::Press($m, 2002, $hold); Start-Sleep -Milliseconds 350
      # the value between the clicks too (Codex 2026-10-04 06:41): "flipped twice" vs "both ignored" end the same
      Check "BB-$gap-$hold" "$(1 - $before)|$before" "$mid|$(Chk 2002)" "two clicks gap=$gap hold=$hold flip twice (after the first | after the second)"
    }
  }
  # C) bounce on buttons that change the screen: one screen change each.
  # Since 0.2.44 the back button asks first when there are unsaved changes, so put the toggle back to its saved value
  # (A/B flip it many times; a single missed flip would otherwise turn C into a test of the confirmation).
  if ((Chk 2002) -ne $saved) { [U]::Press($m, 2002, 30); Start-Sleep -Milliseconds 350 }
  $pre = State
  Bounced 240 15 40
  Check BC01 $true ((Has 203) -and -not (Has 241)) "bounced back button: back on the list once (before: $pre; after: $(State))"
  Bounced 220 15 40
  Check BC02 $true ((Has 241) -and -not (Has 203)) "bounced settings row: settings screen once (after: $(State))"
  Click 241   # cancel: the toggles were not saved
  [void][U]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i=0; $i -lt 50 -and -not $p.HasExited; $i++) { Start-Sleep -Milliseconds 100 }
} catch { Add-Failure ("exception: " + $_) }
finally { Stop-TestInstances $suffix }
Complete-Checks
