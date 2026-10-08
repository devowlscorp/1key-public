# Master password change (T3): settings -> security -> change master.
#  - wrong current / mismatched new / too short / weak + "No" / cancel: nothing changes (file byte-identical)
#  - write failure (config.dat.tmp is a directory): the old master and file stay valid
#  - success: the new master unlocks after a relaunch, the old one does not, the content is intact
# Judgement: tools\tests\lib\Check.ps1 (T18). ASCII only.
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
Start-Checks -Required @('MS01','MS02','MS03','MS04','MS05','MS06','MS07','MS08','MS09','MS10','MS11','MS12','MS13','MS14','MS15')

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($args.Count -gt 0) { $args[0] } else { Get-DefaultExe }
$suffix = ".ms"
$cfg = "$sp\master_cfg"; if (Test-Path $cfg) { Get-ChildItem $cfg -Recurse | Remove-Item -Force -Recurse -ErrorAction Ignore }; New-Item -ItemType Directory -Force $cfg | Out-Null
Add-Type @'
using System;using System.Runtime.InteropServices;using System.Text;
public class U {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, StringBuilder l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int hh, uint f);
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static string Title(IntPtr h) { var t = new StringBuilder(256); GetWindowTextW(h, t, 256); return t.ToString(); }
  public static IntPtr FindCls(uint pid, string cls) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
  public static IntPtr FindBox(uint pid) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && (Cls(h) == "#32770" || Cls(h) == "OneKeyDialog") && IsWindowVisible(h)) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
}
'@
function SetText($id, $s) { [void][U]::SendMessageW([U]::GetDlgItem($m, $id), 0x000C, [IntPtr]::Zero, [string]$s) }
function GetEdit($id) { $sb = New-Object System.Text.StringBuilder 512; [void][U]::SendMessageW([U]::GetDlgItem($m,$id), 0x000D, [IntPtr]512, $sb); $sb.ToString() }
function Click($id) { [void][U]::PostMessageW([U]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 700 }
function Has($id) { [U]::GetDlgItem($m, $id) -ne [IntPtr]::Zero }
function Box($cmd = 1) { for ($i=0;$i -lt 30;$i++) { $b = [U]::FindBox([uint32]$p.Id); if ($b -ne [IntPtr]::Zero) { [void][U]::PostMessageW($b, 0x0111, [IntPtr]$cmd, [IntPtr]::Zero); Start-Sleep -Milliseconds 300; return $true }; Start-Sleep -Milliseconds 100 }; return $false }
function Hash() { (Get-FileHash "$cfg\config.dat" -Algorithm SHA256).Hash }
function Launch() {
  $script:p = Start-Process $exe -PassThru
  $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $script:m = [U]::FindCls([uint32]$script:p.Id, "OneKeyMainWindow$suffix"); if ($script:m -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 100 }
  if ($script:m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 600
  [void][U]::SetWindowPos($script:m, [IntPtr]::Zero, 1400, 60, 0, 0, 0x0001 -bor 0x0004 -bor 0x0010)
}
function Quit() { [void][U]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i=0; $i -lt 50 -and -not $p.HasExited; $i++) { Start-Sleep -Milliseconds 100 } }
function Attempt($cur, $n1, $n2) { SetText 501 $cur; SetText 502 $n1; SetText 503 $n2; Click 505 }

try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix
  Launch
  SetText 101 "Master1234"; SetText 102 "Master1234"; Click 103
  # auto-lock off first: auto-lock reads the SYSTEM idle time (posted messages are not input), so on a PC nobody has
  # touched for 10 minutes the test instance locks in the middle (2026-09-29: bounce/many/master/help failed that way
  # in one full run and passed on the rerun). Settings > auto-lock slider Home = off, save (toast), wait for it to go.
  Click 220; [void][U]::PostMessageW([U]::GetDlgItem($m, 2005), 0x0100, [IntPtr]0x24, [IntPtr]::Zero); Start-Sleep -Milliseconds 300
  Click 2012; Start-Sleep -Milliseconds 3500
  Confirm-AutoLockOff $cfg
  Click 203; Click 4001; SetText 301 "S1"; SetText 302 "content-1"; Click 310; [void](Box 2)
  Click 220; Start-Sleep -Milliseconds 400
  Click 213
  Check MS01 $true ((Has 501) -and (Has 502) -and (Has 503)) "security -> change master opens the master screen"
  $h0 = Hash

  Attempt "wrong-master" "NewMaster-77" "NewMaster-77"
  Check MS02 $true ((Box) -and (Has 501)) "wrong current master is rejected, screen stays"
  Check MS03 $h0 (Hash) "wrong current: file unchanged"
  Attempt "Master1234" "NewMaster-77" "Different-88"
  Check MS04 $true ((Box) -and ((Hash) -eq $h0)) "mismatched new values rejected, file unchanged"
  Attempt "Master1234" "abc" "abc"
  Check MS05 $true ((Box) -and ((Hash) -eq $h0)) "3-char new master rejected, file unchanged"
  Attempt "Master1234" "12345678" "12345678"
  Check MS06 $true ((Box 7) -and (Has 501) -and ((Hash) -eq $h0)) "weak new master + No keeps the old one"

  # write failure: a directory where the temporary file must go
  New-Item -ItemType Directory -Force "$cfg\config.dat.tmp" | Out-Null
  Attempt "Master1234" "NewMaster-77" "NewMaster-77"
  Check MS07 $true ((Box) -and (Has 501)) "write failure is reported, screen stays"
  Check MS08 $h0 (Hash) "write failure: file unchanged"
  Remove-Item "$cfg\config.dat.tmp" -Force -Recurse
  Click 504
  Check MS09 $true (Has 213) "cancel returns to the settings (the screen it was opened from, 0.3.75)"
  Check MS10 $h0 (Hash) "cancel: file unchanged"

  # success (the settings part stays open after returning from the master screen)
  if (-not (Has 213)) { Click 220; Start-Sleep -Milliseconds 400 }
  Click 213
  Attempt "Master1234" "NewMaster-77" "NewMaster-77"
  # 0.2.45: the success notice is a toast (closes by itself), not a box
  $toast = $false; for ($i = 0; $i -lt 30 -and -not $toast; $i++) { $toast = [U]::FindCls([uint32]$p.Id, "OneKeyToast") -ne [IntPtr]::Zero; if (-not $toast) { Start-Sleep -Milliseconds 100 } }
  $boxed = [U]::FindBox([uint32]$p.Id) -ne [IntPtr]::Zero
  Check MS11 "toast=True box=False list=True master=False changed=True" ("toast=$toast box=$boxed list=$(Has 213) master=$(Has 501) changed=$((Hash) -ne $h0)") "change succeeds: toast notice (no box), back to the settings (0.3.76), file rewritten"
  Quit

  Launch
  SetText 101 "Master1234"; Click 103; [void](Box)
  Check MS12 $true (Has 101) "after the change the old master is rejected"
  SetText 101 "NewMaster-77"; Click 103
  Check MS13 $true (Has 203) "the new master unlocks after a relaunch"
  Click 1000; Click 303
  Check MS14 "content-1" (GetEdit 302) "content intact after re-encryption"
  Click 311
  Quit
  Check MS15 $false (Test-Path "$cfg\config.dat.tmp") "no temporary file left behind"
} catch { Add-Failure ("exception: " + $_) }
finally { Stop-TestInstances $suffix; Remove-Item "$cfg\config.dat.tmp" -Force -Recurse -ErrorAction Ignore }
Complete-Checks
