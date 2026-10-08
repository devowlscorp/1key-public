# Help and short notes (user decision 2026-09-29, Codex proposal C): screens show only short state lines that follow the
# current choice; the long how-to text is in the [Help] window (button 250, or F1 inside the 1Key window).
# Checks: the edit screen's Enter note follows the two switches (off / sends in browsers / not in browsers), the method
# note appears only for clipboard paste, the settings notes follow auto-lock and admin, the help window opens from the
# button and from F1 with the right topic, closes with Esc, keeps what was typed, and F1 in the hotkey box does NOT open
# help (recording first). The list screen shows no how-to text once there are items. Texts are read with WM_GETTEXT.
# 0.2.44: section titles in the help window are separate (bold) controls from id 110; "settings saved" is a toast
# (class OneKeyToast) that closes by itself, not a box that has to be clicked.
# 0.2.45: a modal covers the main window with a blurred, dimmed backdrop (class OneKeyBackdrop) that goes away with it.
# Own config folder and test suffix; the window is never foregrounded. Judgement: tools\tests\lib\Check.ps1. ASCII only.
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
$req = @(); for ($i = 1; $i -le 22; $i++) { $req += ("HP{0:D2}" -f $i) }
Start-Checks -Required $req

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($args.Count -gt 0) { $args[0] } else { Get-DefaultExe }
$suffix = ".hp"
$cfg = "$sp\help_cfg"; if (Test-Path $cfg) { Get-ChildItem $cfg -Recurse | Remove-Item -Force -Recurse -ErrorAction Ignore }; New-Item -ItemType Directory -Force $cfg | Out-Null
Add-Type @'
using System;using System.Runtime.InteropServices;using System.Text;
public class HU {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, StringBuilder l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static IntPtr FindCls(uint pid, string cls) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls && IsWindowVisible(h)) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
  public static string Text(IntPtr h) { var sb = new StringBuilder(4096); SendMessageW(h, 0x000D, (IntPtr)4096, sb); return sb.ToString(); }
  // all static texts of a window without an id (footers), joined
  public static string Unnamed(IntPtr p) { var sb = new StringBuilder(); EnumChildWindows(p, (h,l) => { if (GetDlgCtrlID(h) == 0 && Cls(h) == "Static") sb.Append(Text(h)).Append('|'); return true; }, IntPtr.Zero); return sb.ToString(); }
}
'@
# unnamed static texts (footers) longer than 30 characters, joined; "" when there are none
function Long($w) { (([HU]::Unnamed($w)).Split([char]'|') | Where-Object { $_.Length -gt 30 }) -join "|" }
function Has2($id) { [HU]::GetDlgItem($m, $id) -ne [IntPtr]::Zero }
function T($id) { [HU]::Text([HU]::GetDlgItem($m, $id)) }
function SetText($id, $s) { [void][HU]::SendMessageW([HU]::GetDlgItem($m, $id), 0x000C, [IntPtr]::Zero, [string]$s) }
function Click($id) { [void][HU]::PostMessageW([HU]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 600 }
function Key($id, $vk) { [void][HU]::PostMessageW([HU]::GetDlgItem($m, $id), 0x0100, [IntPtr]$vk, [IntPtr]::Zero); Start-Sleep -Milliseconds 300 }
function Box() { for ($i=0;$i -lt 15;$i++) { $b = [HU]::FindCls([uint32]$p.Id, "OneKeyDialog"); if ($b -ne [IntPtr]::Zero) { return $b }; Start-Sleep -Milliseconds 100 }; [IntPtr]::Zero }
function BoxTitle($b) { if ($b -eq [IntPtr]::Zero) { "" } else { [HU]::Text([HU]::GetDlgItem($b, 100)) } }
function Answer($b, $cmd) { if ($b -ne [IntPtr]::Zero) { [void][HU]::PostMessageW($b, 0x0111, [IntPtr]$cmd, [IntPtr]::Zero); Start-Sleep -Milliseconds 600 } }
function CloseBox($b, $cmd = 1) { if ($b -ne [IntPtr]::Zero) { [void][HU]::PostMessageW($b, 0x0111, [IntPtr]$cmd, [IntPtr]::Zero); Start-Sleep -Milliseconds 400 } }
function Launch() {
  $script:p = Start-Process $exe -PassThru
  $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $script:m = [HU]::FindCls([uint32]$script:p.Id, "OneKeyMainWindow$suffix"); if ($script:m -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 100 }
  if ($script:m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 700
}
function Quit() { [void][HU]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i=0; $i -lt 50 -and -not $p.HasExited; $i++) { Start-Sleep -Milliseconds 100 } }

try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_WORKAREA = $TallScreen; $env:ONEKEY_TEST_THEME = $null
  Launch
  SetText 101 "Master1234"; SetText 102 "Master1234"; Click 103; CloseBox (Box)
  # auto-lock off first: auto-lock reads the SYSTEM idle time (posted messages are not input), so on a PC nobody has
  # touched for 10 minutes the test instance locks in the middle (2026-09-29: bounce/many/master/help failed that way
  # in one full run and passed on the rerun). Settings > auto-lock slider Home = off, save (toast), wait for it to go.
  Click 220; [void][HU]::PostMessageW([HU]::GetDlgItem($m, 2005), 0x0100, [IntPtr]0x24, [IntPtr]::Zero); Start-Sleep -Milliseconds 300
  Click 2012; Start-Sleep -Milliseconds 3500
  Confirm-AutoLockOff $cfg

  # ---- edit screen notes
  Click 203; Click 4001
  SetText 301 "HelpTest"; SetText 302 "dummy"
  # Enter send is one 3-way row (0.3.5x): off / press / except browsers - no note line under it any more
  function Seg($id) { [int][HU]::SendMessageW([HU]::GetDlgItem($m, $id), 0x00F0, [IntPtr]::Zero, [string]$null) }
  Check HP01 "False|0" "$(([HU]::GetDlgItem($m, 320)) -ne [IntPtr]::Zero)|$(Seg 305)" "Enter send: one 3-way row starting at 'off', no note line"
  Key 305 0x27
  Check HP02 "1" "$(Seg 305)" "Right on the Enter row: 'press'"
  Key 305 0x27
  Check HP03 "2" "$(Seg 305)" "Right again: 'except browsers'"
  Key 305 0x25; Key 305 0x25
  Check HP04 "0" "$(Seg 305)" "Left twice: back to 'off'"
  Check HP05 "" (T 321) "method auto: no method note"
  for ($k = 0; $k -lt 4; $k++) { Key 306 0x28 }
  Check HP06 $true ((T 321).Length -gt 0) "method clipboard paste: the method note appears"
  for ($k = 0; $k -lt 4; $k++) { Key 306 0x26 }
  Check HP07 "" (T 321) "method back to auto: the note is gone"
  Check HP08 "" (Long $m) "edit screen: no fixed how-to text (no unnamed text over 30 characters)"

  # ---- help from the button and F1; typed text survives; Esc closes
  SetText 301 "Typed-Before-Help"
  Click 250
  $b = Box
  $editTitle = BoxTitle $b
  $bd = [HU]::FindCls([uint32]$p.Id, "OneKeyBackdrop")
  Check HP21 $true ($bd -ne [IntPtr]::Zero) "while the help window is open, the main window is covered by the blurred backdrop"
  $heads = @(110..117 | ForEach-Object { [HU]::Text([HU]::GetDlgItem($b, $_)) }) -join "|"
  Check HP17 $true (($heads.Split([char]'|') | Where-Object { $_.Length -gt 0 }).Count -ge 3 -and $heads -match "Enter") "help window: section titles as their own controls (bold), one of them about Enter"
  Check HP09 $true ($editTitle.Length -gt 0 -and $editTitle -ne "1Key") "[Help] opens the help window with a topic title"
  CloseBox $b 2
  Check HP22 $false ([HU]::FindCls([uint32]$p.Id, "OneKeyBackdrop") -ne [IntPtr]::Zero) "... and the backdrop goes away with it"
  Check HP10 "Typed-Before-Help|False" ((T 301) + "|" + ((Box) -ne [IntPtr]::Zero)) "Esc closes it and what was typed is still there"
  Key 301 0x70
  $b = Box
  Check HP11 $true ($b -ne [IntPtr]::Zero) "F1 in the name field opens the help window"
  CloseBox $b 2
  Key 304 0x70
  $b = Box
  Check HP12 $false ($b -ne [IntPtr]::Zero) "F1 in the hotkey box does not open help (recording first)"
  CloseBox $b 2
  Key 304 0x08   # the F1 above was recorded in the hotkey box (recording first); clear it before saving
  Click 310; CloseBox (Box)

  # ---- list with an item: no how-to footer
  Check HP13 "" (Long $m) "list with items: no how-to text (no unnamed text over 30 characters)"

  # ---- settings notes
  Click 220
  # since 0.3.6x (user 2026-10-05: explanations go to [Help], no note lines under settings) the auto-lock and admin notes are gone:
  # moving the slider (End = longest, Home = off) and switching admin on add no note line
  Key 2005 0x23; $n1 = Has2 2021
  Key 2005 0x24; $n2 = Has2 2021
  Check HP14 "False|False" "$n1|$n2" "auto-lock: no note line under the slider at any position (explained in [Help])"
  Key 2003 0x20; Start-Sleep -Milliseconds 500
  Check HP15 "False" "$(Has2 2022)" "admin switch on: no note line (explained in [Help])"
  Key 2003 0x20; Start-Sleep -Milliseconds 300
  Click 250
  $b = Box
  $st = BoxTitle $b
  CloseBox $b 2
  Click 241
  Answer (Box) 6   # the admin switch was changed: confirm discarding
  Check HP16 $true ($st.Length -gt 0 -and $st -ne $editTitle) "[Help] on the settings screen opens another topic than on the edit screen"
  # settings saved: a toast, no box to click, gone after a few seconds
  Click 220; Key 2002 0x20; Click 2012
  Start-Sleep -Milliseconds 500
  $toast = [HU]::FindCls([uint32]$p.Id, "OneKeyToast")
  Check HP18 "True|" ("$($toast -ne [IntPtr]::Zero)|" + $(if ($toast -ne [IntPtr]::Zero) { "" } else { "no toast" })) "saving the settings shows a toast"
  Check HP19 $false ((Box) -ne [IntPtr]::Zero) "... and no box that has to be clicked"
  Start-Sleep -Milliseconds 3500
  Check HP20 $false ([HU]::FindCls([uint32]$p.Id, "OneKeyToast") -ne [IntPtr]::Zero) "... the toast has closed by itself after about 3 seconds"
  Quit
} catch { Add-Failure ("exception: " + $_) }
finally { Stop-TestInstances $suffix }
Complete-Checks
