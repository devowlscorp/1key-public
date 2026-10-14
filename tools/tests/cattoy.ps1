# Surprise toy (0.5.31, user 2026-10-14: a missed click on the toy opened the 1Key window; the "click me" hint was not seen).
# ONEKEY_TEST_CAT_GUEST=yarn brings the toy soon; no real mouse - the test-only message WM_TESTCLICK (0x8036, to the cat window) runs
# the same branch as a click on the cat.
# TY00 the toy appears (OneKeyCatGuest window) while the 1Key window is in the tray
# TY01 the hint bubble (OneKeyCatHearts) shows when the toy appears and again while it can be clicked (at least twice in 12 s)
# TY02 a click on the cat while the toy is there plays with it: the 1Key window stays closed, the log has "cat click during toy -> play"
#      and the catch ("hunt caught")
# TY03 after the toy, a click on the cat opens the 1Key window as before
# Own config folder and instance suffix; the real 1Key is not touched. Windows appear: only while nobody uses the PC.
#   powershell -ExecutionPolicy Bypass -File tools\tests\cattoy.ps1 [-Exe <1Key.exe>]
# ASCII only.
param([string]$Exe = "")
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
$ids = @(); 0..3 | ForEach-Object { $ids += "TY{0:D2}" -f $_ }
Start-Checks -Required $ids

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$suffix = ".ty"
$root = Join-Path $sp ("cattoy-{0:yyyyMMdd-HHmmss}-{1}" -f (Get-Date), $PID); New-Item -ItemType Directory $root | Out-Null
$cfg = "$root\cfg"; New-Item -ItemType Directory $cfg | Out-Null
$log = "$root\cat.log"
$master = "Ty-Dummy-5517"
Add-Type @'
using System;using System.Runtime.InteropServices;using System.Text;
public class TYU {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr GetPropW(IntPtr h, string name);
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static IntPtr Find(uint pid, string cls, bool vis) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls && (!vis || IsWindowVisible(h))) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
}
'@
function Start1Key() {
  $script:p = Start-Process $exe -PassThru; $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $script:m = [TYU]::Find([uint32]$script:p.Id, "OneKeyMainWindow$suffix", $false); if ($script:m -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 100 }
  if ($script:m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 900
}
function Quit1Key() { if ($script:p -and -not $script:p.HasExited) { [void][TYU]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i = 0; $i -lt 50 -and -not $script:p.HasExited; $i++) { Start-Sleep -Milliseconds 100 }; if (-not $script:p.HasExited) { Stop-Process -Id $script:p.Id -Force } } }
function Has($id) { [TYU]::GetDlgItem($m, $id) -ne [IntPtr]::Zero }
function Click($id, $ms = 700) { [void][TYU]::PostMessageW([TYU]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds $ms }
function SetText($id, $s) { [void][TYU]::SendMessageW([TYU]::GetDlgItem($m, $id), 0x000C, [IntPtr]::Zero, [string]$s) }
function Win($cls) { [TYU]::Find([uint32]$p.Id, $cls, $true) }
function LogText() { if (Test-Path $log) { Get-Content $log -Raw } else { "" } }

try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_LOCKWIDGET = "0"; $env:ONEKEY_TEST_NO_AUTOLOCK = "1"
  $env:ONEKEY_TEST_CAT_LOG = $log; $env:ONEKEY_TEST_CAT_GUEST = "yarn"; $env:ONEKEY_TEST_CAT_GUEST_STAY_MS = "30000"
  Start1Key
  SetText 101 $master; SetText 102 $master; Click 103 1500
  if (-not (Has 203)) { throw "setup: no list after the master" }
  Click 220
  [void][TYU]::PostMessageW([TYU]::GetDlgItem($m, 2032), 0x0100, [IntPtr]0x20, [IntPtr]::Zero); Start-Sleep -Milliseconds 300
  Click 2012 900
  [void][TYU]::PostMessageW($m, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 1200   # to the tray

  # TY00 the toy appears
  $t = [Environment]::TickCount; $guest = [IntPtr]::Zero
  while ([Environment]::TickCount - $t -lt 90000) { $guest = Win "OneKeyCatGuest"; if ($guest -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 100 }
  $mainHidden = -not [TYU]::IsWindowVisible($m)
  Check TY00 "True|True" "$($guest -ne [IntPtr]::Zero)|$mainHidden" "the toy appeared (after $([int](([Environment]::TickCount - $t) / 1000)) s), 1Key window in the tray"

  # TY01 the hint shows, and shows again while the toy can be clicked
  $shows = 0; $was = $false; $t1 = [Environment]::TickCount
  while ([Environment]::TickCount - $t1 -lt 12000) {
    $now = (Win "OneKeyCatHearts") -ne [IntPtr]::Zero
    if ($now -and -not $was) { $shows++ }
    $was = $now; Start-Sleep -Milliseconds 50
  }
  Check TY01 "True" "$($shows -ge 2)" "hint bubble shown $shows times in 12 s while the toy waits"

  # TY02 click on the cat while the toy is there
  $cat = Win "OneKeyCat"
  [void][TYU]::PostMessageW($cat, 0x8036, [IntPtr]::Zero, [IntPtr]::Zero)
  $t2 = [Environment]::TickCount; $opened = $false
  while ([Environment]::TickCount - $t2 -lt 8000) { if ([TYU]::IsWindowVisible($m)) { $opened = $true }; Start-Sleep -Milliseconds 100 }
  Start-Sleep -Seconds 3   # the log is written every 2 s during a motion
  $lt = LogText
  Check TY02 "False|True|True" "$opened|$($lt.Contains('cat click during toy -> play'))|$($lt.Contains('hunt caught'))" "cat clicked while the toy waits: played (caught), the 1Key window stayed closed"

  # TY03 a cat click after the toy opens the 1Key window as before
  $t3 = [Environment]::TickCount
  while ([Environment]::TickCount - $t3 -lt 20000 -and (Win "OneKeyCatGuest") -ne [IntPtr]::Zero) { Start-Sleep -Milliseconds 200 }
  Start-Sleep -Seconds 6   # the hunt motion ends with the cat sitting again
  $cat = Win "OneKeyCat"
  [void][TYU]::PostMessageW($cat, 0x8036, [IntPtr]::Zero, [IntPtr]::Zero)
  $t3 = [Environment]::TickCount; $opened3 = $false
  while ([Environment]::TickCount - $t3 -lt 5000) { if ([TYU]::IsWindowVisible($m)) { $opened3 = $true; break }; Start-Sleep -Milliseconds 100 }
  Check TY03 "True" "$opened3" "after the toy: a cat click opens the 1Key window"
} catch { Add-Failure ("exception: " + $_) }
finally {
  Quit1Key; Stop-TestInstances $suffix
  foreach ($v in "ONEKEY_TEST_CAT_LOG", "ONEKEY_TEST_LOCKWIDGET", "ONEKEY_TEST_NO_AUTOLOCK", "ONEKEY_TEST_CAT_GUEST", "ONEKEY_TEST_CAT_GUEST_STAY_MS") { Remove-Item "env:$v" -ErrorAction Ignore }
  "log kept: $log"
}
Complete-Checks
