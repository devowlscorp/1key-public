# Single instance and elevation handoff (T2), without UAC: the child side is exercised with "--handoff <pid>".
#  1) --handoff to a live 1Key that does not exit: the new process waits 10 s, shows a notice and exits without a window
#  2) --handoff to a live 1Key that exits after ~1.5 s: the new process starts and owns the instance
#  3) --handoff to a dead pid: no waiting; the normal "already running" path applies
#  4) the instance mutex exists but no window appears within 3 s: the new process refuses to start (no second instance)
#  Since 0.2.51 these start-up notices are the app's own box (class OneKeyDialog, 1Key theme and font), not a system
#  MessageBox (HO12; user report 2026-09-30: the "already running" box did not follow the theme).
# Judgement: tools\tests\lib\Check.ps1 (T18). ASCII only. Takes about 30 s.
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
Start-Checks -Required @('HO01','HO02','HO03','HO04','HO05','HO06','HO07','HO08','HO09','HO10','HO11','HO12')

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($args.Count -gt 0) { $args[0] } else { Get-DefaultExe }
$suffix = ".ho"
$cfg = "$sp\handoff_cfg"; if (Test-Path $cfg) { Get-ChildItem $cfg -Recurse | Remove-Item -Force -Recurse -ErrorAction Ignore }; New-Item -ItemType Directory -Force $cfg | Out-Null
Add-Type @'
using System;using System.Runtime.InteropServices;using System.Text;
public class U {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int hh, uint f);
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static IntPtr FindCls(uint pid, string cls) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
  public static IntPtr FindBox(uint pid) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && (Cls(h) == "#32770" || Cls(h) == "OneKeyDialog") && IsWindowVisible(h)) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
}
'@
$cls = "OneKeyMainWindow$suffix"
function Win($proc) { [U]::FindCls([uint32]$proc.Id, $cls) }
function WaitWin($proc, $ms) { for ($i = 0; $i -lt $ms / 100; $i++) { $w = Win $proc; if ($w -ne [IntPtr]::Zero) { return $w }; if ($proc.HasExited) { break }; Start-Sleep -Milliseconds 100 }; [IntPtr]::Zero }
function WaitBox($proc, $ms) { for ($i = 0; $i -lt $ms / 100; $i++) { $b = [U]::FindBox([uint32]$proc.Id); if ($b -ne [IntPtr]::Zero) { return $b }; if ($proc.HasExited) { break }; Start-Sleep -Milliseconds 100 }; [IntPtr]::Zero }
function CloseBox($b) { [void][U]::PostMessageW($b, 0x0111, [IntPtr]2, [IntPtr]::Zero) }   # IDCANCEL closes an unowned MB_OK box
function WaitExit($proc, $ms) { for ($i = 0; $i -lt $ms / 100 -and -not $proc.HasExited; $i++) { Start-Sleep -Milliseconds 100 }; $proc.HasExited }
function Park($w) { [void][U]::SetWindowPos($w, [IntPtr]::Zero, 1400, 60, 0, 0, 0x0001 -bor 0x0004 -bor 0x0010) }
$mutex = $null

try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix
  $a = Start-Process $exe -PassThru
  $wa = WaitWin $a 8000
  Check HO01 $true ($wa -ne [IntPtr]::Zero) "instance A is running"
  Park $wa

  # 1) A does not exit: B gives up after 10 s
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $b = Start-Process $exe -ArgumentList "--handoff", $a.Id -PassThru
  Start-Sleep -Milliseconds 3000
  Check HO02 $true (((Win $b) -eq [IntPtr]::Zero) -and -not $b.HasExited) "B waits for A without creating a window"
  $box = WaitBox $b 12000
  $waited = $sw.ElapsedMilliseconds
  Check HO03 $true (($box -ne [IntPtr]::Zero) -and ($waited -ge 9500)) "B shows a notice after about 10 s (waited $waited ms)"
  if ($box -ne [IntPtr]::Zero) { CloseBox $box }
  Check HO04 $true ((WaitExit $b 5000) -and ((Win $b) -eq [IntPtr]::Zero)) "B exits without ever creating a window"
  Check HO05 $true ((-not $a.HasExited) -and ((Win $a) -ne [IntPtr]::Zero)) "A is still the only instance"

  # 2) A exits after 1.5 s: B2 takes over
  $b2 = Start-Process $exe -ArgumentList "--handoff", $a.Id -PassThru
  Start-Sleep -Milliseconds 1500
  [void][U]::PostMessageW((Win $a), 0x8005, [IntPtr]::Zero, [IntPtr]::Zero)   # WM_ONEKEY_QUIT
  $w2 = WaitWin $b2 8000
  Check HO06 $true ($w2 -ne [IntPtr]::Zero) "B2 starts once A has exited"
  Check HO07 $true $a.HasExited "A has exited"
  if ($w2 -ne [IntPtr]::Zero) { Park $w2 }

  # 3) dead pid: no 10 s wait; B3 meets the running B2 and stops at the normal "already running" notice
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $b3 = Start-Process $exe -ArgumentList "--handoff", $a.Id -PassThru
  $box = WaitBox $b3 8000
  Check HO08 $true (($box -ne [IntPtr]::Zero) -and ($sw.ElapsedMilliseconds -lt 7000) -and ((Win $b3) -eq [IntPtr]::Zero)) "dead pid: no waiting, 'already running' notice, no second window ($($sw.ElapsedMilliseconds) ms)"
  Check HO12 "OneKeyDialog" $(if ($box -ne [IntPtr]::Zero) { [U]::Cls($box) } else { "no box" }) "the 'already running' notice is the app's own box (theme and font), not a system MessageBox"
  if ($box -ne [IntPtr]::Zero) { CloseBox $box }
  Check HO09 $true (WaitExit $b3 5000) "B3 exits"

  # 4) mutex without a window: refuse instead of starting a second instance
  [void][U]::PostMessageW($w2, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); [void](WaitExit $b2 5000)
  $mutex = New-Object System.Threading.Mutex($true, "Local\1Key.SingleInstance.v1$suffix")
  $c = Start-Process $exe -PassThru
  $box = WaitBox $c 8000
  Check HO10 $true (($box -ne [IntPtr]::Zero) -and ((Win $c) -eq [IntPtr]::Zero)) "mutex held but no window: notice instead of a second instance"
  if ($box -ne [IntPtr]::Zero) { CloseBox $box }
  Check HO11 $true (WaitExit $c 5000) "C exits"
} catch { Add-Failure ("exception: " + $_) }
finally {
  if ($mutex) { try { $mutex.ReleaseMutex() } catch { Clear-ExpectedError }; $mutex.Dispose() }
  Stop-TestInstances $suffix
  foreach ($pp in @($b, $b3, $c)) { if ($pp -and -not $pp.HasExited) { Stop-Process $pp.Id -Force -ErrorAction Ignore } }
}
Complete-Checks
