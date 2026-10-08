# Full-flow regression: create master -> list (empty) -> settings -> add/edit -> save -> list -> lock -> wrong/right master
# -> advanced -> edit existing -> quit -> relaunch keeps the item. Captures PNGs of each screen into the scratch folder.
# The window is never brought to the foreground, so hotkey recording is not exercised here.
# Judgement: tools\tests\lib\Check.ps1 (T18). Replaces the old wrapper that counted True/False tokens in a log.
# Contains Korean test data: keep this file UTF-8 WITH BOM (PowerShell 5.1).
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
Start-Checks -Required @('SH01','SH02','SH03','SH04','SH05','SH06','SH07','SH08','SH09','SH10','SH11','SH12','SH13','SH14','SH15','SH16','SH17','SH18','SH19','SH20','SH21')

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($args.Count -gt 0) { $args[0] } else { Get-DefaultExe }
$suffix = ".v2"
$cfg = "$sp\v2_cfg"; if (Test-Path $cfg) { Get-ChildItem $cfg -Recurse | Remove-Item -Force -Recurse -ErrorAction Ignore }; New-Item -ItemType Directory -Force $cfg | Out-Null
Add-Type -AssemblyName System.Drawing
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
  [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int hh, uint f);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static string Title(IntPtr h) { var t = new StringBuilder(256); GetWindowTextW(h, t, 256); return t.ToString(); }
  public static IntPtr FindCls(uint pid, string cls) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
  public static IntPtr FindBox(uint pid) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && (Cls(h) == "#32770" || Cls(h) == "OneKeyDialog") && IsWindowVisible(h)) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
}
'@
function GetText($id) { $sb = New-Object System.Text.StringBuilder 512; [void][U]::SendMessageW([U]::GetDlgItem($m,$id), 0x000D, [IntPtr]512, $sb); $sb.ToString() }
function SetText($h, $s) { [void][U]::SendMessageW($h, 0x000C, [IntPtr]::Zero, [string]$s) }
function Click($id) { [void][U]::PostMessageW([U]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 500 }
function Chk($id) { [int][U]::SendMessageW([U]::GetDlgItem($m, $id), 0x00F0, [IntPtr]::Zero, [IntPtr]::Zero) }
function Has($id) { [U]::GetDlgItem($m, $id) -ne [IntPtr]::Zero }
function CloseBox() { for ($i=0;$i -lt 30;$i++) { $b = [U]::FindBox([uint32]$p.Id); if ($b -ne [IntPtr]::Zero) { "   (box '" + [U]::Title($b) + "' closed)"; [void][U]::PostMessageW($b, 0x0111, [IntPtr]1, [IntPtr]::Zero); Start-Sleep -Milliseconds 300; return }; Start-Sleep -Milliseconds 100 } }
function Cap($name) {
  Start-Sleep -Milliseconds 350
  [U+RECT]$r = New-Object 'U+RECT'; [void][U]::GetWindowRect($m, [ref]$r)
  $bmp = New-Object System.Drawing.Bitmap(($r.R-$r.L), ($r.B-$r.T)); $g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
  [void][U]::PrintWindow($m, $dc, 2); $g.ReleaseHdc($dc); $g.Dispose()
  $bmp.Save("$sp\$name.png", [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  "   capture $name  ($($r.R-$r.L)x$($r.B-$r.T))"
}
function Launch() {
  $script:p = Start-Process $exe -PassThru
  $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $script:m = [U]::FindCls([uint32]$script:p.Id, "OneKeyMainWindow$suffix"); if ($script:m -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 100 }
  if ($script:m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 600
  [void][U]::SetWindowPos($script:m, [IntPtr]::Zero, 1400, 60, 0, 0, 0x0001 -bor 0x0004 -bor 0x0010)
}
$name = "자금이체"

try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_WORKAREA = $TallScreen
  Launch
  Check SH01 $true ((Has 101) -and (Has 102)) "1) first run: create fields 101/102"
  Cap "v2_create"
  SetText ([U]::GetDlgItem($m,101)) "Master1234"; SetText ([U]::GetDlgItem($m,102)) "Master1234"; Click 103
  Check SH02 $true (Has 203) "2) empty list: add row"
  Check SH03 $false (Has 2012) "2) list has no settings controls (settings open on their own screen)"
  Cap "v2_list_collapsed"
  Click 220; Start-Sleep -Milliseconds 400
  Check SH04 $true ((Has 2012) -and (Has 241) -and -not (Has 203)) "   settings screen: save and cancel, list gone"
  Check SH05 $true (Has 2005) "   settings screen: slider"
  Cap "v2_settings"
  Click 240
  Click 203; Click 4001
  Check SH06 $true (Has 301) "3) edit screen: name field"
  Check SH07 $true (Has 304) "3) edit screen: hotkey box"
  Check SH08 $false (Has 308) "3) new item has no delete button"
  SetText ([U]::GetDlgItem($m,301)) $name; SetText ([U]::GetDlgItem($m,302)) "P@ssw0rd!#`$%"
  [void][U]::SendMessageW([U]::GetDlgItem($m,305), 0x00F1, [IntPtr]1, [IntPtr]::Zero)     # Enter toggle on
  Cap "v2_edit"
  Click 310; CloseBox
  Check SH09 $true (Has 1000) "4) after save: slot row 1000"
  Check SH10 $true (Has 203) "4) after save: add row"
  Cap "v2_list_one"
  Click 2014
  Check SH11 $true (Has 101) "5) lock: unlock field 101"
  Cap "v2_lock"
  SetText ([U]::GetDlgItem($m,101)) "wrong"; Click 103; CloseBox
  Check SH12 $true (Has 101) "   wrong master keeps the lock screen"
  SetText ([U]::GetDlgItem($m,101)) "Master1234"; Click 103
  Check SH13 $true (Has 203) "   right master returns to the list"
  Click 220; Click 212
  Check SH14 $true ((Has 401) -and (Has 402)) "6) advanced screen: fields 401/402"
  Cap "v2_advanced"
  Click 405
  Check SH15 $true (Has 216) "   back to the settings (0.3.75)"
  Click 240
  Click 1000   # row click = edit
  Check SH16 $true (Has 308) "7) existing item has a delete button"
  Check SH17 $name (GetText 301) "7) existing item name"
  Cap "v2_edit_existing"
  Click 311
  Check SH18 $true (Has 203) "   back without changes"
  [void][U]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i=0; $i -lt 50 -and -not $p.HasExited; $i++) { Start-Sleep -Milliseconds 100 }
  Check SH19 $true $p.HasExited "8) WM_ONEKEY_QUIT exits"
  Launch
  SetText ([U]::GetDlgItem($m,101)) "Master1234"; Click 103
  Click 1000
  Check SH20 $name (GetText 301) "9) item kept after relaunch"
  Check SH21 1 (Chk 305) "9) Enter toggle kept after relaunch"
  Stop-Process $p.Id -Force -ErrorAction Ignore
} catch { Add-Failure ("exception: " + $_) }
finally {
  Stop-TestInstances $suffix
  Remove-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "1Key$suffix" -ErrorAction Ignore   # test-suffixed value only
}
Complete-Checks
