# Autostart change that fails or is cancelled (Codex AS6-1/AS6-2, candidate 7 - 2026-10-12). The registration is simulated in test mode:
# ONEKEY_TEST_ADMIN_TASK_FILE = a marker file standing in for the administrator sign-in task, ONEKEY_TEST_STARTUP_DIR = the Startup folder
# (a real shortcut is written there), ONEKEY_TEST_AUTOSTART_CANCEL=1 = every call that needs administrator approval fails like UAC [No],
# ONEKEY_TEST_AUTOSTART_DELAY_MS = each registration call in the change takes that long (to lock / exit / save again while it runs). Texts in English (ONEKEY_TEST_LANG=en),
# toasts and tray balloons are read from <config>\notices.txt (test mode only).
# AF01 normal setup: autostart + "always run as administrator" on, risk box Yes -> task marker, no shortcut, header autostart=1 admin=1
# AF02 admin -> normal with the approval cancelled: the task stays, the shortcut this change made is removed again, the box says nothing changed
#      and both settings were set back; header admin=1, settings switches on/on (AS6-1, AS6-2)
# AF03 the result arrives while 1Key is locked: settings are not touched while locked (header admin=0 kept, unlock still works with it),
#      a balloon says they will be set back; after unlock they are (header admin=1) and a balloon says so
# AF04 exit requested while the change runs: 1Key waits, sets the settings back, then exits (header admin=1 after the process ended)
# AF05 locked + exit while the change runs: nothing can be saved, so after a restart the mismatch (admin task, setting off) is reported
#      at unlock and again when the settings are opened
# AF06 another setting saved while the change runs is kept when the two autostart settings are set back
# Own config folder and instance suffix; the real 1Key, its settings, its Startup folder and its tasks are not touched. ASCII only.
# Judgement: tools\tests\lib\Check.ps1. Exe: first argument, default build\<csproj version>\1Key.exe.
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
Start-Checks -Required @("AF01", "AF02", "AF03", "AF04", "AF05", "AF06")
$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($args.Count -gt 0) { $args[0] } else { Get-DefaultExe }
$suffix = ".asfail"
$cfg = "$sp\asfail_cfg"; if (Test-Path $cfg) { Get-ChildItem $cfg -Recurse | Remove-Item -Force -Recurse -ErrorAction Ignore }; New-Item -ItemType Directory -Force $cfg | Out-Null
$task = "$sp\asfail_task.marker"; Remove-Item $task -ErrorAction Ignore
$startup = "$sp\asfail_startup"; if (Test-Path $startup) { Remove-Item $startup -Recurse -Force }; New-Item -ItemType Directory -Force $startup | Out-Null
$lnk = Join-Path $startup "1Key$suffix.lnk"
$notices = "$cfg\notices.txt"
$master = "Af-Dummy-3307"
Add-Type @'
using System;using System.Runtime.InteropServices;using System.Text;using System.Threading;
public class AF {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, StringBuilder l);
  [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static IntPtr FindCls(uint pid, string cls) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls && IsWindowVisible(h)) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
  public static string Text(IntPtr h) { var sb = new StringBuilder(4096); SendMessageW(h, 0x000D, (IntPtr)4096, sb); return sb.ToString(); }
  public static void Press(IntPtr m, int id, int hold) {
    IntPtr c = GetDlgItem(m, id); if (c == IntPtr.Zero) return;
    RECT rc; GetClientRect(c, out rc); IntPtr lp = (IntPtr)((rc.R/2) | ((rc.B/2) << 16));
    PostMessageW(c, 0x0201, (IntPtr)1, lp); Thread.Sleep(hold); PostMessageW(c, 0x0202, IntPtr.Zero, lp);
  }
}
'@
function SetText($id, $s) { [void][AF]::SendMessageW([AF]::GetDlgItem($m, $id), 0x000C, [IntPtr]::Zero, [string]$s) }
function Click($id, $ms = 500) { [void][AF]::PostMessageW([AF]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds $ms }
function Has($id) { [AF]::GetDlgItem($m, $id) -ne [IntPtr]::Zero }
function Chk($id) { [int][AF]::SendMessageW([AF]::GetDlgItem($m, $id), 0x00F0, [IntPtr]::Zero, [IntPtr]::Zero) }
function HeaderVal($k) { ([regex]::Match((Get-TestHeader $cfg), "(?m)^$k=(\d+)")).Groups[1].Value }
function WaitBox($sec = 6) { for ($i = 0; $i -lt $sec * 10; $i++) { $b = [AF]::FindCls([uint32]$p.Id, 'OneKeyDialog'); if ($b -ne [IntPtr]::Zero) { Start-Sleep -Milliseconds 200; return $b }; Start-Sleep -Milliseconds 100 }; [IntPtr]::Zero }
function BoxText($b) { if ($b -eq [IntPtr]::Zero) { return "" }; [AF]::Text([AF]::GetDlgItem($b, 101)) }
function Answer($b, $cmd) { if ($b -ne [IntPtr]::Zero) { [void][AF]::PostMessageW($b, 0x0111, [IntPtr]$cmd, [IntPtr]::Zero); Start-Sleep -Milliseconds 600 } }
function Notices() { if (Test-Path $notices) { @(Get-Content $notices) } else { @() } }
function Start1Key([string]$cancel = "", [int]$delay = 0) {
  if ($cancel) { $env:ONEKEY_TEST_AUTOSTART_CANCEL = $cancel } else { Remove-Item Env:ONEKEY_TEST_AUTOSTART_CANCEL -ErrorAction Ignore }
  if ($delay -gt 0) { $env:ONEKEY_TEST_AUTOSTART_DELAY_MS = "$delay" } else { Remove-Item Env:ONEKEY_TEST_AUTOSTART_DELAY_MS -ErrorAction Ignore }
  $script:p = Start-Process $exe -PassThru
  $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $script:m = [AF]::FindCls([uint32]$script:p.Id, "OneKeyMainWindow$suffix"); if ($script:m -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 100 }
  if ($script:m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 600
}
function Unlock() { SetText 101 $master; Click 103 300; for ($i = 0; $i -lt 50 -and -not (Has 220); $i++) { Start-Sleep -Milliseconds 100 }; Start-Sleep -Milliseconds 300 }
function Quit1Key() { [void][AF]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i = 0; $i -lt 80 -and -not $p.HasExited; $i++) { Start-Sleep -Milliseconds 100 }; if (-not $p.HasExited) { Add-Failure "1Key did not exit after the quit message"; Stop-Process -Id $p.Id -Force -ErrorAction Ignore } }
# open the settings, turn "always run as administrator" (2003) off, save; back on the list, the change starts
function SaveAdminOff() { Click 220 600; if ((Chk 2003) -eq 1) { [AF]::Press($m, 2003, 40); Start-Sleep -Milliseconds 300 }; Click 2012 400 }

try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_WORKAREA = $TallScreen; $env:ONEKEY_TEST_LANG = "en"
  $env:ONEKEY_TEST_ADMIN_TASK_FILE = $task; $env:ONEKEY_TEST_STARTUP_DIR = $startup; $env:ONEKEY_TEST_NO_ELEVATE = "1"   # "always admin" is on in this test config: never relaunch with a real UAC prompt
  Remove-Item Env:ONEKEY_TEST_INSTALL_PATH -ErrorAction Ignore   # test mode: this exe is the installed copy (switch enabled)

  # setup: master, auto-lock off
  Start1Key
  SetText 101 $master; SetText 102 $master; Click 103 1500
  $b = WaitBox 2; Answer $b 1
  Click 220 600; [void][AF]::PostMessageW([AF]::GetDlgItem($m, 2005), 0x0100, [IntPtr]0x24, [IntPtr]::Zero); Start-Sleep -Milliseconds 300
  Click 2012 2500
  Confirm-AutoLockOff $cfg

  # AF01: autostart + admin on, risk box Yes
  Click 220 600
  if ((Chk 2001) -eq 0) { [AF]::Press($m, 2001, 40); Start-Sleep -Milliseconds 300 }
  if ((Chk 2003) -eq 0) { [AF]::Press($m, 2003, 40); Start-Sleep -Milliseconds 300 }
  Click 2012 400
  $b = WaitBox 4; $risk = BoxText $b; Answer $b 6
  Start-Sleep -Milliseconds 2000
  Check AF01 "True|True|False|1|1" "$($risk -match 'administrator')|$(Test-Path $task)|$(Test-Path $lnk)|$(HeaderVal 'autostart')|$(HeaderVal 'admin')" "admin start on: risk box, task marker, no shortcut, header autostart=1 admin=1"
  Quit1Key

  # AF02: admin -> normal, approval cancelled
  Start1Key -cancel "1"
  Unlock
  SaveAdminOff
  $b = WaitBox 6; $t2 = BoxText $b; Answer $b 1
  Start-Sleep -Milliseconds 400
  Click 220 600
  $sw = "$(Chk 2001)/$(Chk 2003)"
  Click 240 500
  Check AF02 "True|False|1|1|1/1|True|True" "$(Test-Path $task)|$(Test-Path $lnk)|$(HeaderVal 'autostart')|$(HeaderVal 'admin')|$sw|$($t2 -match 'was not changed')|$($t2 -match 'set back')" "cancelled: task kept, new shortcut removed, header 1/1, switches on/on; box: '$($t2 -replace "`r?`n", ' / ')'"
  Quit1Key

  # AF03: result while locked
  Remove-Item $notices -ErrorAction Ignore
  Start1Key -cancel "1" -delay 2500
  Unlock
  SaveAdminOff
  Click 2014 300   # lock now (list lock knob)
  Start-Sleep -Milliseconds 6500   # the delay applies to each registration call (shortcut, then task removal): ~5 s in all
  $lockedAdmin = HeaderVal 'admin'
  $n3 = (Notices) -join ' // '
  Unlock
  $unlocked = Has 203
  Start-Sleep -Milliseconds 800
  $n3b = (Notices) -join ' // '
  Check AF03 "0|True|True|1|True|True" "$lockedAdmin|$($n3 -match 'locked, so the settings were not set back')|$unlocked|$(HeaderVal 'admin')|$($n3b -match 'set back to match the current registration')|$(Test-Path $task)" "locked: header untouched (admin=0) until unlock, unlock works, then admin=1; notices: $n3b"
  Quit1Key

  # AF04: exit while the change runs
  Start1Key -cancel "1" -delay 2500
  Unlock
  SaveAdminOff
  [void][AF]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero)
  Start-Sleep -Milliseconds 800
  $aliveEarly = -not $p.HasExited
  for ($i = 0; $i -lt 80 -and -not $p.HasExited; $i++) { Start-Sleep -Milliseconds 100 }
  Check AF04 "True|True|1|1|True" "$aliveEarly|$($p.HasExited)|$(HeaderVal 'autostart')|$(HeaderVal 'admin')|$(Test-Path $task)" "exit waits for the change, sets the settings back (header 1/1), then exits"

  # AF05: locked + exit while the change runs -> mismatch reported after a restart
  Start1Key -cancel "1" -delay 2500
  Unlock
  SaveAdminOff
  Click 2014 300
  [void][AF]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero)
  for ($i = 0; $i -lt 80 -and -not $p.HasExited; $i++) { Start-Sleep -Milliseconds 100 }
  $leftAdmin = HeaderVal 'admin'
  Remove-Item $notices -ErrorAction Ignore
  Start1Key
  Unlock
  Start-Sleep -Milliseconds 600
  $atUnlock = ((Notices) -join ' // ') -match 'registered as administrator'
  Remove-Item $notices -ErrorAction Ignore
  Click 220 900
  $atSettings = ((Notices) -join ' // ') -match 'registered as administrator'
  Click 240 500
  Check AF05 "0|True|True|True" "$leftAdmin|$(Test-Path $task)|$atUnlock|$atSettings" "locked + exit: admin=0 left with the task in place; reported at unlock and when the settings open"
  # put the two settings back in line for AF06 (admin on again, the task is already there)
  Click 220 600; if ((Chk 2003) -eq 0) { [AF]::Press($m, 2003, 40); Start-Sleep -Milliseconds 300 }; Click 2012 400
  $b = WaitBox 2; Answer $b 6; Start-Sleep -Milliseconds 1500
  Quit1Key

  # AF06: another setting saved while the change runs is kept
  Start1Key -cancel "1" -delay 3000
  Unlock
  $sm0 = HeaderVal 'startmin'
  SaveAdminOff
  Click 220 600; [AF]::Press($m, 2002, 40); Start-Sleep -Milliseconds 300; Click 2012 400
  Start-Sleep -Milliseconds 3500
  $b = WaitBox 2; Answer $b 1
  Check AF06 "1|$(1 - [int]$sm0)|1" "$(HeaderVal 'admin')|$(HeaderVal 'startmin')|$(HeaderVal 'autostart')" "start-minimized saved during the change ($sm0 -> changed) is kept when admin/autostart are set back"
  Quit1Key
} catch { Add-Failure ("exception: " + $_) }
finally {
  Stop-TestInstances $suffix
  foreach ($v in 'ONEKEY_TEST_ADMIN_TASK_FILE','ONEKEY_TEST_STARTUP_DIR','ONEKEY_TEST_NO_ELEVATE','ONEKEY_TEST_AUTOSTART_CANCEL','ONEKEY_TEST_AUTOSTART_DELAY_MS','ONEKEY_TEST_LANG') { Remove-Item "Env:$v" -ErrorAction Ignore }
  Remove-Item $task -ErrorAction Ignore
}
Complete-Checks
