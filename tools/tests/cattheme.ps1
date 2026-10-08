# Taskbar cat colour follows the 1Key theme (0.5.15-C, user 2026-10-09): settings > Screen > Theme = light / dark is used for the
# cat (and the hourly clock) whatever Windows uses; only "follow Windows" uses the Windows taskbar theme (SystemUsesLightTheme).
# CT01 theme light -> grey cat   CT02 theme dark -> black cat   CT03 follow -> the Windows taskbar theme
# CT04 the run had a Windows theme that differs from at least one of light/dark (otherwise CT01/CT02 prove nothing)
# Reads the test property OneKeyTestCatLight on the main window (2 = grey cat drawn, 1 = black cat, 0 = not shown).
# Own config folder and test suffix; the real 1Key and its settings are not touched. The cat appears on the taskbar: run it while
# nobody uses the PC. Judgement: tools\tests\lib\Check.ps1 (T18). ASCII only.
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
Start-Checks -Required @("CT01", "CT02", "CT03", "CT04")

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($args.Count -gt 0) { $args[0] } else { Get-DefaultExe }
$suffix = ".ct"
$cfg = "$sp\cattheme_cfg"; if (Test-Path $cfg) { Get-ChildItem $cfg -Recurse | Remove-Item -Force -Recurse -ErrorAction Ignore }; New-Item -ItemType Directory -Force $cfg | Out-Null
$master = "Ct-Dummy-5512"
Add-Type @'
using System;using System.Runtime.InteropServices;using System.Text;
public class CTU {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr GetPropW(IntPtr h, string name);
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static IntPtr Find(uint pid, string cls, bool vis) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls && (!vis || IsWindowVisible(h))) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
  public static IntPtr FindBox(uint pid) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && (Cls(h) == "#32770" || Cls(h) == "OneKeyDialog") && IsWindowVisible(h)) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
}
'@
function Launch() {
  $script:p = Start-Process $exe -PassThru; $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $script:m = [CTU]::Find([uint32]$script:p.Id, "OneKeyMainWindow$suffix", $false); if ($script:m -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 100 }
  if ($script:m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 900
}
function Quit() { if ($script:p -and -not $script:p.HasExited) { [void][CTU]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i = 0; $i -lt 50 -and -not $script:p.HasExited; $i++) { Start-Sleep -Milliseconds 100 }; if (-not $script:p.HasExited) { Stop-Process -Id $script:p.Id -Force } } }
function Click($id, $ms = 700) { [void][CTU]::PostMessageW([CTU]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds $ms }
function Key($id, $vk) { [void][CTU]::PostMessageW([CTU]::GetDlgItem($m, $id), 0x0100, [IntPtr]$vk, [IntPtr]::Zero); Start-Sleep -Milliseconds 500 }
function SetText($id, $s) { [void][CTU]::SendMessageW([CTU]::GetDlgItem($m, $id), 0x000C, [IntPtr]::Zero, [string]$s) }
function CloseBox() { for ($i = 0; $i -lt 20; $i++) { $b = [CTU]::FindBox([uint32]$p.Id); if ($b -ne [IntPtr]::Zero) { [void][CTU]::PostMessageW($b, 0x0111, [IntPtr]1, [IntPtr]::Zero); Start-Sleep -Milliseconds 300; return }; Start-Sleep -Milliseconds 100 } }
# settings: walker (widget mode) on, theme moved by $keys (0x28 down / 0x26 up on dropdown 2008), save; then x -> tray -> cat
function SetThemeAndShowCat($keys) {
  Click 220
  if ([int][CTU]::SendMessageW([CTU]::GetDlgItem($m, 2032), 0x00F0, [IntPtr]::Zero, [IntPtr]::Zero) -ne 1) { [void][CTU]::PostMessageW([CTU]::GetDlgItem($m, 2032), 0x0100, [IntPtr]0x20, [IntPtr]::Zero); Start-Sleep -Milliseconds 300 }
  foreach ($k in $keys) { Key 2008 $k }
  Click 2012 900; CloseBox
  [void][CTU]::PostMessageW($m, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
  $t0 = [Environment]::TickCount
  while ([Environment]::TickCount - $t0 -lt 6000 -and [int64][CTU]::GetPropW($m, "OneKeyTestCatLight") -eq 0) { Start-Sleep -Milliseconds 150 }
  Start-Sleep -Milliseconds 300
  "{0}|why {1}" -f [int64][CTU]::GetPropW($m, "OneKeyTestCatLight"), [int64][CTU]::GetPropW($m, "OneKeyWalkerWhy")
}
function Restart() { Quit; Launch; SetText 101 $master; Click 103 1500; CloseBox }

$winLight = (Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize" -ErrorAction Ignore).SystemUsesLightTheme
$winCat = if ($winLight -eq 1) { 2 } else { 1 }
try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_LOCKWIDGET = "0"; $env:ONEKEY_TEST_NO_AUTOLOCK = "1"; $env:ONEKEY_TEST_THEME = $null
  Launch
  SetText 101 $master; SetText 102 $master; Click 103 1500; CloseBox
  $r = SetThemeAndShowCat @(0x28)               # follow -> light
  Check CT01 "2" ($r.Split('|')[0]) "theme 'light' -> grey cat (Windows taskbar light=$winLight; $r)"
  Restart
  $r = SetThemeAndShowCat @(0x28)               # light -> dark
  Check CT02 "1" ($r.Split('|')[0]) "theme 'dark' -> black cat (Windows taskbar light=$winLight; $r)"
  Restart
  $r = SetThemeAndShowCat @(0x26, 0x26)         # dark -> follow
  Check CT03 "$winCat" ($r.Split('|')[0]) "theme 'follow Windows' -> the Windows taskbar theme ($r)"
  Check CT04 "True" "$($winLight -eq 0 -or $winLight -eq 1)" "Windows taskbar theme read (SystemUsesLightTheme=$winLight): one of CT01/CT02 differs from Windows"
} catch { Add-Failure ("exception: " + $_) }
finally {
  Quit; Stop-TestInstances $suffix
  foreach ($v in "ONEKEY_TEST_LOCKWIDGET", "ONEKEY_TEST_NO_AUTOLOCK") { Remove-Item "env:$v" -ErrorAction Ignore }
}
Complete-Checks
