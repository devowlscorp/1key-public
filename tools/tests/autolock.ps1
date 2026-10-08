# Auto-lock while a box is open, then reopen from the tray and close the box (Codex review 2026-09-30 00:23, section 4;
# 0.2.48). NOT part of the default regression: the real auto-lock reads the SYSTEM input idle time, so this script
# needs the PC untouched for at least 5 minutes (the shortest auto-lock step). It sets auto-lock to 5 minutes in its own
# test config, changes the theme and a switch in the settings, presses back so the "discard changes?" box is open with
# the blurred backdrop, and then waits (up to -WaitMinutes) until the app locks by itself (LockAndHide hides the window).
# Checks: the lock came with the window hidden, the lock screen, no backdrop and - since 0.2.49 (user decision
# 2026-09-30) - the box cancelled and gone too (up to 0.2.48 the box stayed on screen and AL03 closed it with No: the
# expectation changed with the product policy); reopening from the tray icon (WM_TRAY + WM_LBUTTONUP, like a click)
# shows a usable lock screen with nothing left to close; unlocking and opening the settings shows only the saved values
# and back does not ask.
# Own config folder and test suffix; the real 1Key and its settings are not touched. The tray reopen asks Windows to
# bring the TEST window forward (that is the real path); it only happens right after 5 idle minutes. ASCII only.
param([string]$Exe = "", [double]$WaitMinutes = 30)
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
Start-Checks -Required @("AL00", "AL01", "AL02", "AL03", "AL04")

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$suffix = ".al"
$cfg = "$sp\autolock_cfg"; if (Test-Path $cfg) { Get-ChildItem $cfg -Recurse | Remove-Item -Force -Recurse -ErrorAction Ignore }; New-Item -ItemType Directory -Force $cfg | Out-Null
Add-Type @'
using System;using System.Collections.Generic;using System.Runtime.InteropServices;using System.Text;
public class AU {
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
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int size);
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static List<IntPtr> All(uint pid, string cls, bool visibleOnly) { var f = new List<IntPtr>(); EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls && (!visibleOnly || IsWindowVisible(h))) f.Add(h); return true; }, IntPtr.Zero); return f; }
  public static int Dark(IntPtr h) { int v; return DwmGetWindowAttribute(h, 20, out v, 4) == 0 ? v : -1; }
}
'@
function SetText($id, $s) { [void][AU]::SendMessageW([AU]::GetDlgItem($m, $id), 0x000C, [IntPtr]::Zero, [string]$s) }
function Click($id, $ms = 600) { [void][AU]::PostMessageW([AU]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds $ms }
function Key($id, $vk) { [void][AU]::PostMessageW([AU]::GetDlgItem($m, $id), 0x0100, [IntPtr]$vk, [IntPtr]::Zero); Start-Sleep -Milliseconds 400 }
function Has($id) { [AU]::GetDlgItem($m, $id) -ne [IntPtr]::Zero }
function Chk($id) { [int][AU]::SendMessageW([AU]::GetDlgItem($m, $id), 0x00F0, [IntPtr]::Zero, [IntPtr]::Zero) }
function Boxes([bool]$vis = $true) { @([AU]::All([uint32]$p.Id, "OneKeyDialog", $vis)) }
function Box() { for ($i=0;$i -lt 15;$i++) { $b = @(Boxes); if ($b.Count -gt 0) { return $b[0] }; Start-Sleep -Milliseconds 100 }; [IntPtr]::Zero }
function Answer($b, $cmd) { if ($b -ne [IntPtr]::Zero) { [void][AU]::PostMessageW($b, 0x0111, [IntPtr]$cmd, [IntPtr]::Zero); Start-Sleep -Milliseconds 700 } }
function Backdrops() { @([AU]::All([uint32]$p.Id, "OneKeyBackdrop", $false)).Count }
function Launch() {
  $script:p = Start-Process $exe -PassThru
  $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $w = @([AU]::All([uint32]$script:p.Id, "OneKeyMainWindow$suffix", $true)); if ($w.Count -gt 0) { $script:m = $w[0]; break }; Start-Sleep -Milliseconds 100 }
  if ($script:m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 700
}
function Quit() { [void][AU]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i=0; $i -lt 50 -and -not $p.HasExited; $i++) { Start-Sleep -Milliseconds 100 } }
$sysLight = (Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize" -ErrorAction Ignore).AppsUseLightTheme
$sysDark = if ($sysLight -eq 0) { 1 } else { 0 }
$other = if ($sysDark -eq 1) { 1 } else { 2 }

try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_WORKAREA = $TallScreen
  $env:ONEKEY_TEST_NO_AUTOLOCK = $null   # this script tests auto-lock itself (lib/Check.ps1 pauses it for the others)
  $env:ONEKEY_TEST_THEME = $null; $env:ONEKEY_TEST_FAIL = $null
  Launch
  SetText 101 "Master1234"; SetText 102 "Master1234"; Click 103; Answer (Box) 1
  # auto-lock 5 minutes: slider Home (off) then one step right (5)
  Click 220; Key 2005 0x24; Key 2005 0x27; Click 2012 3500
  $al = ([regex]::Match((Get-TestHeader $cfg), '(?m)^autolock=(\d+)')).Groups[1].Value
  Check AL00 "5" $al "setup: auto-lock saved as 5 minutes"
  if ($al -ne "5") { throw "setup failed" }

  # unsaved theme + switch, back -> the "discard changes?" box with the backdrop
  Click 220
  $saved = Chk 2002
  for ($k = 0; $k -lt $other; $k++) { Key 2008 0x28 }; Key 2002 0x20
  Click 240
  $b = Box
  $pre = "box=$($b -ne [IntPtr]::Zero) backdrop=$(Backdrops)"
  "   box open: $pre; now waiting for the auto-lock (needs the PC untouched for 5 minutes)" | Out-Host

  $sw = [Diagnostics.Stopwatch]::StartNew(); $lastNote = 0; $maxIdle = 0
  while (-not (Has 101) -and $sw.Elapsed.TotalMinutes -lt $WaitMinutes) {
    $idle = (Get-SystemIdleMs) / 60000; if ($idle -gt $maxIdle) { $maxIdle = $idle }
    if ($sw.Elapsed.TotalMinutes -ge $lastNote + 1) { $lastNote = [int]$sw.Elapsed.TotalMinutes; "   waiting {0:N0} min, system idle {1:N1} min" -f $sw.Elapsed.TotalMinutes, $idle | Out-Host }
    Start-Sleep -Seconds 2
  }
  if (-not (Has 101)) { throw ("no auto-lock within $WaitMinutes minutes (largest system idle seen {0:N1} min; the PC was in use)" -f $maxIdle) }
  "   auto-lock after {0:N1} min" -f $sw.Elapsed.TotalMinutes | Out-Host
  Start-Sleep -Seconds 1
  Check AL01 "False|True|0|0|box=True backdrop=1" ("$([AU]::IsWindowVisible($m))|$(Has 101)|$(@(Boxes $false).Count)|$(Backdrops)|$pre") "auto-lock with the box open: window hidden, lock screen, the box cancelled and gone (also no hidden one), no backdrop (hidden|lock|boxes|backdrops|before)"

  # reopen from the tray icon (a click on it)
  [void][AU]::PostMessageW($m, 0x8001, [IntPtr]::Zero, [IntPtr]0x0202); Start-Sleep -Milliseconds 800
  Check AL02 "True|True|0" ("$([AU]::IsWindowVisible($m))|$(Has 101)|$(Backdrops)") "reopened from the tray: window shown, lock screen, no backdrop"

  # nothing is left to close: the box was cancelled by the lock
  Check AL03 "True|True|0|0" ("$(Has 101)|$([AU]::IsWindowEnabled($m))|$(@(Boxes $false).Count)|$(Backdrops)") "after reopening: still locked, main window enabled (usable), no box or backdrop to close (lock|enabled|boxes|backdrops)"

  # unlock: only the saved settings, back does not ask
  SetText 101 "Master1234"; Click 103 900
  Click 220
  $c = "$([AU]::Dark($m))|$(Chk 2002)"
  Click 240; $b = Box
  Check AL04 "$sysDark|$saved|False|True" ("$c|$($b -ne [IntPtr]::Zero)|$(Has 203)") "after unlocking: the saved theme and switch, back does not ask, back on the list"
  Answer $b 7
  Quit
} catch { Add-Failure ("exception: " + $_) }
finally { $env:ONEKEY_TEST_WORKAREA = $null; Stop-TestInstances $suffix }
Complete-Checks
