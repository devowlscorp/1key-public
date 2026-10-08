# Startup test: (1) create master + one slot with a bare hotkey, quit. (2) relaunch with --tray:
# window must be visible on the lock screen, hotkey must already be held by the app while locked,
# after unlock the window goes to tray (default "start minimized") and the hotkey is still held.
# Judgement: tools\tests\lib\Check.ps1 (T18). ASCII only.
# Scratch folder: %TEMP%\1Key-tests (override with ONEKEY_TEST_DIR). Exe: first argument, default build\<csproj version>\1Key.exe.
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
Start-Checks -Required @('SU01','SU02','SU03','SU04','SU05','SU06','SU07','SU08','SU09','SU10','SU11','SU12','SU13','SU14')

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($args.Count -gt 0) { $args[0] } else { Get-DefaultExe }
$suffix = ".su"
$cfg = "$sp\startup_cfg"; if (Test-Path $cfg) { Get-ChildItem $cfg -Recurse | Remove-Item -Force -Recurse -ErrorAction Ignore }; New-Item -ItemType Directory -Force $cfg | Out-Null
Add-Type @'
using System;using System.Runtime.InteropServices;using System.Text;
public class U {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
  [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int hh, uint f);
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static string Title(IntPtr h) { var t = new StringBuilder(256); GetWindowTextW(h, t, 256); return t.ToString(); }
  public static IntPtr FindCls(uint pid, string cls) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
  public static IntPtr FindBox(uint pid) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && (Cls(h) == "#32770" || Cls(h) == "OneKeyDialog") && IsWindowVisible(h)) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
  // true when some other process already holds the hotkey (our registration fails)
  public static bool HeldByOther(uint mods, uint vk) { if (RegisterHotKey(IntPtr.Zero, 4242, mods | 0x4000, vk)) { UnregisterHotKey(IntPtr.Zero, 4242); return false; } return true; }
}
'@
function SetText($h, $s) { [void][U]::SendMessageW($h, 0x000C, [IntPtr]::Zero, [string]$s) }
function Click($id) { [void][U]::PostMessageW([U]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 500 }
function Has($id) { [U]::GetDlgItem($m, $id) -ne [IntPtr]::Zero }
function CloseBox() { for ($i=0;$i -lt 30;$i++) { $b = [U]::FindBox([uint32]$p.Id); if ($b -ne [IntPtr]::Zero) { "   (box '" + [U]::Title($b) + "' closed)"; [void][U]::PostMessageW($b, 0x0111, [IntPtr]1, [IntPtr]::Zero); Start-Sleep -Milliseconds 300; return }; Start-Sleep -Milliseconds 100 } }
function Launch($argList) {
  $script:p = if ($argList) { Start-Process $exe -ArgumentList $argList -PassThru } else { Start-Process $exe -PassThru }
  $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $script:m = [U]::FindCls([uint32]$script:p.Id, "OneKeyMainWindow$suffix"); if ($script:m -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 100 }
  if ($script:m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 700
  [void][U]::SetWindowPos($script:m, [IntPtr]::Zero, 1400, 60, 0, 0, 0x0001 -bor 0x0004 -bor 0x0010)
}
function Quit() { [void][U]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i=0; $i -lt 50 -and -not $p.HasExited; $i++) { Start-Sleep -Milliseconds 100 }; if (-not $p.HasExited) { Stop-Process $p.Id -Force -ErrorAction Ignore } }

try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix
  # pick a bare-allowed hotkey nobody on this PC holds right now: Pause, ScrollLock or PrintScreen
  $VK = 0
  foreach ($cand in 0x13, 0x91, 0x2C) { if (-not [U]::HeldByOther(0, $cand)) { $VK = $cand; break } }
  Check SU01 $true ($VK -ne 0) ("precondition: a free bare hotkey exists (using 0x{0:X})" -f $VK)
  if ($VK -eq 0) { throw "no free bare hotkey" }

  # --- 1) first run: create master, add a slot with the bare hotkey, save, quit
  Launch $null
  SetText ([U]::GetDlgItem($m,101)) "Master1234"; SetText ([U]::GetDlgItem($m,102)) "Master1234"; Click 103
  Check SU02 $true (Has 203) "first run: list screen"
  Click 203; Click 4001
  SetText ([U]::GetDlgItem($m,301)) "TestSlot"; SetText ([U]::GetDlgItem($m,302)) "Secret1!"
  [void][U]::PostMessageW([U]::GetDlgItem($m,304), 0x0100, [IntPtr]$VK, [IntPtr]::Zero); Start-Sleep -Milliseconds 300
  Click 310; CloseBox
  Check SU03 $true (Has 1000) "slot saved (row 1000)"
  Check SU04 $true ([U]::HeldByOther(0, $VK)) "hotkey held by app right after save (unlocked)"
  Quit
  Check SU05 $false ([U]::HeldByOther(0, $VK)) "hotkey released after quit"

  # --- 2) autostart-style launch (--tray): locked, window visible, hotkey already held; unlock -> tray, still held
  Launch "--tray"
  Check SU06 $true ([U]::IsWindowVisible($m)) "--tray: window visible at start"
  Check SU07 $true (Has 101) "--tray: lock screen shown"
  Check SU08 $true ([U]::HeldByOther(0, $VK)) "--tray: hotkey held while locked (HasContent)"
  SetText ([U]::GetDlgItem($m,101)) "Master1234"; Click 103
  Check SU09 $false ([U]::IsWindowVisible($m)) "--tray: hidden to tray after unlock (start-minimized default)"
  Check SU10 $true ([U]::HeldByOther(0, $VK)) "--tray: hotkey held after unlock"
  Check SU11 $true (Has 1000) "--tray: list built behind the scenes"
  Quit

  # --- 3) normal launch: locked, visible, stays visible after unlock
  Launch $null
  Check SU12 $true ([U]::IsWindowVisible($m)) "normal: window visible at start"
  SetText ([U]::GetDlgItem($m,101)) "Master1234"; Click 103
  Check SU13 $true ([U]::IsWindowVisible($m)) "normal: still visible after unlock"
  Check SU14 $true ([U]::HeldByOther(0, $VK)) "normal: hotkey held after unlock"
  Quit
} catch { Add-Failure ("exception: " + $_) }
finally {
  Stop-TestInstances $suffix
  # only the test-suffixed autostart value; the real "1Key" value is never touched
  Remove-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "1Key$suffix" -ErrorAction Ignore
}
Complete-Checks
