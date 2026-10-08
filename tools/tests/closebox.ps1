# Close button (x) on the lock screen (0.2.135 user request, Codex 10:15 close contrasts). On the lock screen x asks whether to
# exit 1Key (default No); unlocked, x hides the window to the tray as before; - minimizes.
# CB01 unlocked: x hides the window to the tray, no question, the process keeps running
# CB02 locked (session-lock notice, then reopened from the tray icon): x asks; No keeps the lock screen open, process alive
# CB03 locked: x asks; Esc on the question keeps the lock screen open, process alive
# CB04 locked: - minimizes; a click on the tray icon brings the lock screen back
# CB05 locked: x asks; a lock notice arrives while the question is open -> 1Key does NOT exit (the question may stay open; No keeps it)
# CB06 locked: x asks; Yes -> the process exits
# Own config folder and test suffix; the real 1Key and its settings are not touched. ASCII only.
param([string]$Exe = "")
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
Start-Checks -Required @("CB01", "CB02", "CB03", "CB04", "CB05", "CB06")

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$suffix = ".cb"
$cfg = "$sp\closebox_cfg"; if (Test-Path $cfg) { Get-ChildItem $cfg -Recurse | Remove-Item -Force -Recurse -ErrorAction Ignore }; New-Item -ItemType Directory -Force $cfg | Out-Null
Add-Type @'
using System;using System.Collections.Generic;using System.Runtime.InteropServices;using System.Text;
public class CB {
  public delegate bool EnumCb(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumCb cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static List<IntPtr> All(uint pid, string cls) { var f = new List<IntPtr>(); EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls && IsWindowVisible(h)) f.Add(h); return true; }, IntPtr.Zero); return f; }
  public static bool Front(IntPtr h) { keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero); return SetForegroundWindow(h); }
}
'@
function SetText($id, $s) { [void][CB]::SendMessageW([CB]::GetDlgItem($m, $id), 0x000C, [IntPtr]::Zero, [string]$s) }
function Click($id, $ms = 700) { [void][CB]::PostMessageW([CB]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds $ms }
function Box() { for ($i = 0; $i -lt 20; $i++) { $b = @([CB]::All([uint32]$p.Id, "OneKeyDialog")); if ($b.Count -gt 0) { return $b[0] }; Start-Sleep -Milliseconds 100 }; [IntPtr]::Zero }
function NoBox() { Start-Sleep -Milliseconds 300; @([CB]::All([uint32]$p.Id, "OneKeyDialog")).Count -eq 0 }
function Answer($b, $cmd) { if ($b -ne [IntPtr]::Zero) { [void][CB]::PostMessageW($b, 0x0111, [IntPtr]$cmd, [IntPtr]::Zero); Start-Sleep -Milliseconds 700 } }
function CloseX() { [void][CB]::PostMessageW($m, 0x0112, [IntPtr]0xF060, [IntPtr]::Zero); Start-Sleep -Milliseconds 300 }   # WM_SYSCOMMAND SC_CLOSE (the caption x)
function LockNotice() { [void][CB]::PostMessageW($m, 0x02B1, [IntPtr]7, [IntPtr]::Zero); Start-Sleep -Milliseconds 1200 }   # WM_WTSSESSION_CHANGE / WTS_SESSION_LOCK
function TrayClick() { [void][CB]::PostMessageW($m, 0x8001, [IntPtr]::Zero, [IntPtr]0x0202); Start-Sleep -Milliseconds 1200 }   # WM_TRAY + WM_LBUTTONUP
function Visible() { [CB]::IsWindowVisible($m) -and -not [CB]::IsIconic($m) }
function OnLock() { [CB]::GetDlgItem($m, 101) -ne [IntPtr]::Zero }
function Alive() { -not $p.HasExited }
function Launch() {
  $script:p = Start-Process $exe -PassThru
  $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $w = @([CB]::All([uint32]$script:p.Id, "OneKeyMainWindow$suffix")); if ($w.Count -gt 0) { $script:m = $w[0]; break }; Start-Sleep -Milliseconds 100 }
  if ($script:m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 700
}

try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_WORKAREA = $TallScreen; $env:ONEKEY_TEST_FAIL = $null
  Launch
  SetText 101 "Master1234"; SetText 102 "Master1234"; Click 103 1200; Answer (Box) 1
  # CB01 unlocked x
  CloseX; Start-Sleep -Milliseconds 500
  Check CB01 "False|True|True" "$([CB]::IsWindowVisible($m))|$(NoBox)|$(Alive)" "unlocked: x hides the window to the tray, no question, still running"

  # lock while hidden, reopen from the tray
  LockNotice; TrayClick
  $ready = (Visible) -and (OnLock)
  # CB02 x -> No
  CloseX; $b = Box; $asked = $b -ne [IntPtr]::Zero; Answer $b 7
  Check CB02 "True|True|True|True|True" "$ready|$asked|$(Visible)|$(OnLock)|$(Alive)" "locked (after a lock notice, reopened from the tray): x asks, No keeps the lock screen open"
  # CB03 x -> Esc
  CloseX; $b = Box; $asked = $b -ne [IntPtr]::Zero
  if ($asked) { [void][CB]::Front($b); Start-Sleep -Milliseconds 300; [CB]::keybd_event(0x1B, 0, 0, [UIntPtr]::Zero); [CB]::keybd_event(0x1B, 0, 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 800 }
  Check CB03 "True|True|True|True|True" "$asked|$(NoBox)|$(Visible)|$(OnLock)|$(Alive)" "locked: x asks, Esc closes the question and keeps the lock screen open"
  # CB04 minimize, back from the tray
  [void][CB]::PostMessageW($m, 0x0112, [IntPtr]0xF020, [IntPtr]::Zero); Start-Sleep -Milliseconds 800   # SC_MINIMIZE
  $min = [CB]::IsIconic($m) -or -not [CB]::IsWindowVisible($m)
  $noAsk = NoBox
  TrayClick
  Check CB04 "True|True|True|True|True" "$min|$noAsk|$(Visible)|$(OnLock)|$(Alive)" "locked: - minimizes without a question; the tray icon brings the lock screen back"
  # CB05 question open + lock notice
  CloseX; $b = Box; $asked = $b -ne [IntPtr]::Zero
  LockNotice; Start-Sleep -Milliseconds 2000
  $alive1 = Alive
  $b2 = @([CB]::All([uint32]$p.Id, "OneKeyDialog")); $still = $b2.Count -gt 0
  if ($still) { Answer $b2[0] 7 }                     # the question may stay (1Key was already locked): the user's own No
  Start-Sleep -Milliseconds 800
  "NOTE [CB05] question still open after the lock notice: $still"
  Check CB05 "True|True|True" "$asked|$alive1|$(Alive)" "locked: a lock notice while the exit question is open does not exit 1Key (no late Yes); after the user's No it keeps running"
  if (-not (Visible)) { TrayClick }
  # CB06 x -> Yes
  CloseX; $b = Box; $asked = $b -ne [IntPtr]::Zero; Answer $b 6
  $exited = $false; for ($i = 0; $i -lt 50; $i++) { if ($p.HasExited) { $exited = $true; break }; Start-Sleep -Milliseconds 100 }
  Check CB06 "True|True" "$asked|$exited" "locked: x asks, Yes exits 1Key"
}
catch { Add-Failure ("exception: " + $_.Exception.Message) }
finally {
  if ($p -and -not $p.HasExited) { [void][CB]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i=0; $i -lt 50 -and -not $p.HasExited; $i++) { Start-Sleep -Milliseconds 100 } }
  Stop-TestInstances $suffix
}
Complete-Checks
