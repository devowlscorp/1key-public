# Whole cat run in one go (0.5.15-X, user 2026-10-09: join every motion and the left/right walks into one and test all frames for size or
# proportion distortion, moments where the body changes shape, delays, cuts and fur colour; also "the front paws come out in front of the
# taskbar, disappear for a moment and come back when the cat sits down").
# ONEKEY_TEST_CAT_FULL=1: head turns through the 9 gazes (near and far) -> the 6 rest motions -> walk left, right, left -> peek. Every frame
# the app pushes to the screen (gaze picture, motion step, peek) is saved with its window position (ONEKEY_TEST_CAT_DUMP), then
# tools/cat/catfull_check.py measures each frame and writes report.md, a contact sheet and a video (the cat at its screen place over a
# drawn taskbar line). Own config folder and instance suffix; the real 1Key is not touched. Windows appear: only when the PC is not in use.
#   powershell -ExecutionPolicy Bypass -File tools\tests\catfull.ps1 [-Exe <1Key.exe>] [-MaxMinutes 4]
param([string]$Exe = "", [int]$MaxMinutes = 4, [string]$Theme = "light")
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$suffix = ".cf"
$root = Join-Path $sp ("catfull-{0:yyyyMMdd-HHmmss}" -f (Get-Date)); New-Item -ItemType Directory $root | Out-Null
$cfg = "$root\cfg"; New-Item -ItemType Directory $cfg | Out-Null
$dump = "$root\frames"; $log = "$root\timing.log"
$master = "Cf-Dummy-4471"
Add-Type @'
using System;using System.Runtime.InteropServices;using System.Text;
public class CFU {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr GetPropW(IntPtr h, string n);
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static IntPtr Find(uint pid, string cls, bool vis) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls && (!vis || IsWindowVisible(h))) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
}
'@
function Quit1Key() { if ($script:p -and -not $script:p.HasExited) { [void][CFU]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i = 0; $i -lt 50 -and -not $script:p.HasExited; $i++) { Start-Sleep -Milliseconds 100 }; if (-not $script:p.HasExited) { Stop-Process -Id $script:p.Id -Force } } }
function Click($id, $ms = 700) { [void][CFU]::PostMessageW([CFU]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds $ms }
function SetText($id, $s) { [void][CFU]::SendMessageW([CFU]::GetDlgItem($m, $id), 0x000C, [IntPtr]::Zero, [string]$s) }
try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_LOCKWIDGET = "0"; $env:ONEKEY_TEST_NO_AUTOLOCK = "1"
  $env:ONEKEY_TEST_CAT_FULL = "1"; $env:ONEKEY_TEST_CAT_DUMP = $dump; $env:ONEKEY_TEST_CAT_LOG = $log; $env:ONEKEY_TEST_THEME = $Theme
  $script:p = Start-Process $exe -PassThru; $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $script:m = [CFU]::Find([uint32]$p.Id, "OneKeyMainWindow$suffix", $false); if ($m -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 100 }
  if ($m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 900
  SetText 101 $master; SetText 102 $master; Click 103 1500
  Click 220
  if ([int][CFU]::SendMessageW([CFU]::GetDlgItem($m, 2032), 0x00F0, [IntPtr]::Zero, [IntPtr]::Zero) -ne 1) { [void][CFU]::PostMessageW([CFU]::GetDlgItem($m, 2032), 0x0100, [IntPtr]0x20, [IntPtr]::Zero); Start-Sleep -Milliseconds 300 }
  Click 2012 900
  [void][CFU]::PostMessageW($m, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
  $deadline = (Get-Date).AddMinutes($MaxMinutes)
  while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
    if ([int][CFU]::GetPropW($m, "OneKeyTestCatFullDone") -eq 1) { "run complete"; break }
    if ($p.HasExited) { "1Key exited"; break }
  }
  Start-Sleep -Seconds 2
  Quit1Key
  "frames: $dump"
  & py -3.12 (Join-Path $PSScriptRoot "..\cat\catfull_check.py") $root
} catch { Add-Failure ("exception: " + $_) }
finally {
  Quit1Key; Stop-TestInstances $suffix
  foreach ($v in "ONEKEY_TEST_CAT_FULL", "ONEKEY_TEST_CAT_DUMP", "ONEKEY_TEST_CAT_LOG", "ONEKEY_TEST_THEME", "ONEKEY_TEST_LOCKWIDGET", "ONEKEY_TEST_NO_AUTOLOCK") { Remove-Item "env:$v" -ErrorAction Ignore }
}
