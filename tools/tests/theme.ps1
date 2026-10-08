# Theme setting (user request 2026-09-29): settings > Screen > Theme = follow Windows / light / dark (dropdown 2008).
# Checks: choosing a theme previews it at once (user request 2026-09-29), [Cancel] and leaving without saving go back to the
# saved theme, it is written to the header as theme=0|1|2 only by [Save], it is applied on the lock screen after a restart
# (the header is readable while locked), and "follow Windows" goes back to the system app mode. Leaving the settings with
# unsaved changes (theme or anything else) asks first: No stays, Yes discards. A preview is dropped while another screen is
# shown and comes back with the draft. The applied theme is read from the window's DWMWA_USE_IMMERSIVE_DARK_MODE (the app sets it with its colours).
# Own config folder and test suffix; the real 1Key and its settings are not touched. The window is never foregrounded.
# Judgement: tools\tests\lib\Check.ps1 (T18). ASCII only.
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
$req = @(); for ($i = 1; $i -le 16; $i++) { $req += ("TM{0:D2}" -f $i) }
Start-Checks -Required $req

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($args.Count -gt 0) { $args[0] } else { Get-DefaultExe }
$suffix = ".th"
$cfg = "$sp\theme_cfg"; if (Test-Path $cfg) { Get-ChildItem $cfg -Recurse | Remove-Item -Force -Recurse -ErrorAction Ignore }; New-Item -ItemType Directory -Force $cfg | Out-Null
Add-Type -AssemblyName System.Security
Add-Type @'
using System;using System.Runtime.InteropServices;using System.Text;
public class TU {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int size);
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static IntPtr FindCls(uint pid, string cls) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
  public static IntPtr FindBox(uint pid) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && (Cls(h) == "#32770" || Cls(h) == "OneKeyDialog") && IsWindowVisible(h)) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
  public static int Dark(IntPtr h) { int v; return DwmGetWindowAttribute(h, 20, out v, 4) == 0 ? v : -1; }
}
'@
function SetText($id, $s) { [void][TU]::SendMessageW([TU]::GetDlgItem($m, $id), 0x000C, [IntPtr]::Zero, [string]$s) }
function Click($id) { [void][TU]::PostMessageW([TU]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 600 }
function Key($id, $vk) { [void][TU]::PostMessageW([TU]::GetDlgItem($m, $id), 0x0100, [IntPtr]$vk, [IntPtr]::Zero); Start-Sleep -Milliseconds 500 }
function Box() { for ($i=0;$i -lt 15;$i++) { $b = [TU]::FindBox([uint32]$p.Id); if ($b -ne [IntPtr]::Zero) { return $b }; Start-Sleep -Milliseconds 100 }; [IntPtr]::Zero }
function Answer($b, $cmd) { if ($b -ne [IntPtr]::Zero) { [void][TU]::PostMessageW($b, 0x0111, [IntPtr]$cmd, [IntPtr]::Zero); Start-Sleep -Milliseconds 600 } }
function Has($id) { [TU]::GetDlgItem($m, $id) -ne [IntPtr]::Zero }
function CloseBox() { for ($i=0;$i -lt 20;$i++) { $b = [TU]::FindBox([uint32]$p.Id); if ($b -ne [IntPtr]::Zero) { [void][TU]::PostMessageW($b, 0x0111, [IntPtr]1, [IntPtr]::Zero); Start-Sleep -Milliseconds 300; return }; Start-Sleep -Milliseconds 100 } }
function Launch() {
  $script:p = Start-Process $exe -PassThru
  $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $script:m = [TU]::FindCls([uint32]$script:p.Id, "OneKeyMainWindow$suffix"); if ($script:m -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 100 }
  if ($script:m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 700
}
function Quit() { [void][TU]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i=0; $i -lt 50 -and -not $p.HasExited; $i++) { Start-Sleep -Milliseconds 100 } }
function HeaderTheme() {
  $h = [Text.Encoding]::UTF8.GetString([Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes("$cfg\config.dat"), [Text.Encoding]::UTF8.GetBytes("1Key/approval-password/v1"), 'CurrentUser'))
  ([regex]::Match($h, '(?m)^theme=(\d+)$')).Groups[1].Value
}
# the Windows app mode this PC uses now (1 = dark), to check "follow Windows"
$sysLight = (Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize" -ErrorAction Ignore).AppsUseLightTheme
$sysDark = if ($sysLight -eq 0) { 1 } else { 0 }

try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_WORKAREA = $TallScreen; $env:ONEKEY_TEST_THEME = $null
  Launch
  SetText 101 "Master1234"; SetText 102 "Master1234"; Click 103; CloseBox
  Check TM01 "$sysDark" "$([TU]::Dark($m))" "new config follows Windows (app mode dark=$sysDark)"

  # dark: previewed at once, not saved; [Cancel] goes back
  Click 220; Key 2008 0x28; Key 2008 0x28
  Check TM02 "1|0" ("$([TU]::Dark($m))|" + (HeaderTheme)) "choosing 'dark' previews it at once; nothing is saved yet"
  Click 241
  Check TM03 "$sysDark|0" ("$([TU]::Dark($m))|" + (HeaderTheme)) "[Cancel] goes back to the saved theme"

  # dark, then [Save]
  Click 220; Key 2008 0x28; Key 2008 0x28; Click 2012; CloseBox
  Check TM04 "1|2" ("$([TU]::Dark($m))|" + (HeaderTheme)) "[Save] with 'dark': applied, header theme=2"
  Quit
  Launch
  Check TM05 "True|1" ("$([TU]::GetDlgItem($m, 101) -ne [IntPtr]::Zero)|$([TU]::Dark($m))") "after a restart the lock screen is already dark"
  SetText 101 "Master1234"; Click 103

  # light
  Click 220; Key 2008 0x26; Click 2012; CloseBox
  Check TM06 "0|1" ("$([TU]::Dark($m))|" + (HeaderTheme)) "[Save] with 'light': applied, header theme=1"
  # follow Windows
  Click 220; Key 2008 0x26; Click 2012; CloseBox
  Check TM07 "$sysDark|0" ("$([TU]::Dark($m))|" + (HeaderTheme)) "[Save] with 'follow Windows': back to the system app mode, header theme=0"
  Quit
  Launch
  Check TM08 "$sysDark" "$([TU]::Dark($m))" "after a restart: follows Windows again"
  SetText 101 "Master1234"; Click 103

  # leaving with an unsaved theme preview asks first (the other of light/dark than the system, so the preview is visible)
  $other = if ($sysDark -eq 1) { 1 } else { 2 }   # 1 = light, 2 = dark
  Click 220; for ($k = 0; $k -lt $other; $k++) { Key 2008 0x28 }
  $preview = "$([TU]::Dark($m))"
  Click 240
  $b = Box
  Check TM09 "True|$(1 - $sysDark)" ("$($b -ne [IntPtr]::Zero)|$preview") "back with a previewed theme: the preview was shown and a confirmation appears"
  Answer $b 7
  Check TM10 "True|$(1 - $sysDark)" ("$(Has 2012)|$([TU]::Dark($m))") "No: stays on the settings screen with the preview"
  Click 240
  Answer (Box) 6
  Check TM11 "True|$sysDark|0" ("$(Has 203)|$([TU]::Dark($m))|" + (HeaderTheme)) "Yes: back to the list, the saved theme, nothing saved"

  # no change: no question
  Click 220; Click 240
  $b = Box
  Check TM12 "False|True" ("$($b -ne [IntPtr]::Zero)|$(Has 203)") "back without changes: no confirmation, straight to the list"
  Answer $b 7

  # another setting changed (start minimized): also asks
  Click 220; Key 2002 0x20; Click 240
  $b = Box
  Check TM13 $true ($b -ne [IntPtr]::Zero) "back with a changed switch: the confirmation appears too"
  Answer $b 6
  Check TM14 $true (Has 203) "Yes: back to the list"

  # a preview is dropped while another screen is shown, and comes back with the draft
  Click 220; for ($k = 0; $k -lt $other; $k++) { Key 2008 0x28 }
  Click 212
  Check TM15 "$sysDark" "$([TU]::Dark($m))" "opening Advanced from the settings: the saved theme is shown there"
  Click 405; Click 220
  Check TM16 "$(1 - $sysDark)" "$([TU]::Dark($m))" "back in the settings with the draft: the preview is shown again"
  Click 241
  Quit
} catch { Add-Failure ("exception: " + $_) }
finally { Stop-TestInstances $suffix }
Complete-Checks
