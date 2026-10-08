# Multi-language (0.2.54, user decision 2026-09-30: ko / en / zh-Hans / ja / vi first).
# I01 "follow Windows" with an English Windows (ONEKEY_TEST_LANG=en): the first-run lock screen is English ([Get started]).
# I02 after creating the master the list is English ([+ Add], [?] help knob since 0.5.2; "shortcut" dropped 0.2.161, user); the header has lang= (empty = follow Windows).
# I03 settings > Display > Language: choosing Japanese previews it at once (settings title and [Advanced settings] row in
#     Japanese) and the DirectWrite policy switches to the system Japanese font (lang=3, system font found).
# I04 [Cancel] drops the preview: back to the English list, header still lang= (nothing saved).
# I05 choosing Chinese (Simplified) previews it with the system Chinese font (lang=2, system font found).
# I06 choosing Vietnamese and [Save]: header lang=vi, the list is Vietnamese; Vietnamese uses the embedded Pretendard.
# I07 after a restart the lock screen (before unlocking) is Vietnamese ([Mo khoa]): the language is a header value.
# I08 the help window opens in Vietnamese and every text drawn so far went through DirectWrite without a failed frame
#     (a mixed-script item name: Korean, Chinese, Japanese and Vietnamese letters, the fallback fills missing glyphs).
# Own config folder and test suffix; the real 1Key is not touched. Boxes can take the focus: run when nobody uses the PC.
# Judgement: tools\tests\lib\Check.ps1. ASCII only (expected non-ASCII texts are written as \u escapes).
param([string]$Exe = "")
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
$req = @(); for ($i = 1; $i -le 8; $i++) { $req += ("I{0:D2}" -f $i) }
Start-Checks -Required $req

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$suffix = ".i18n"
$cfg = "$sp\i18n_cfg"
Add-Type @'
using System;using System.Collections.Generic;using System.Runtime.InteropServices;using System.Text;
public class IU {
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
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static List<IntPtr> All(uint pid, string cls) { var f = new List<IntPtr>(); EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls && IsWindowVisible(h)) f.Add(h); return true; }, IntPtr.Zero); return f; }
  public static string Text(IntPtr h) { var sb = new StringBuilder(8192); SendMessageW(h, 0x000D, (IntPtr)8192, sb); return sb.ToString(); }
}
'@
function U([string]$s) { [regex]::Unescape($s) }
function SetText($id, $s) { [void][IU]::SendMessageW([IU]::GetDlgItem($m, $id), 0x000C, [IntPtr]::Zero, [string]$s) }
function Text($id) { $h = [IU]::GetDlgItem($m, $id); if ($h -eq [IntPtr]::Zero) { "(none)" } else { [IU]::Text($h) } }
function Click($id, $ms = 700) { [void][IU]::PostMessageW([IU]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds $ms }
function Key($id, $vk) { [void][IU]::PostMessageW([IU]::GetDlgItem($m, $id), 0x0100, [IntPtr]$vk, [IntPtr]::Zero); Start-Sleep -Milliseconds 500 }
function Boxes() { @([IU]::All([uint32]$p.Id, "OneKeyDialog")) }
function Box() { for ($i=0;$i -lt 15;$i++) { $b = @(Boxes); if ($b.Count -gt 0) { return $b[0] }; Start-Sleep -Milliseconds 100 }; [IntPtr]::Zero }
function Answer($b, $cmd) { if ($b -ne [IntPtr]::Zero) { [void][IU]::PostMessageW($b, 0x0111, [IntPtr]$cmd, [IntPtr]::Zero); Start-Sleep -Milliseconds 600 } }
function Dw() { $v = [int64][IU]::SendMessageW($m, 0x800E, [IntPtr]::Zero, [IntPtr]::Zero); if ((($v -shr 60) -band 1) -ne 1) { "no answer" } else { "on=$($v -band 1) lang=$(($v -shr 16) -band 0xFF) sys=$(($v -shr 7) -band 1)" } }
function DwFrames() { $v = [int64][IU]::SendMessageW($m, 0x800E, [IntPtr]::Zero, [IntPtr]::Zero); "failed=$(($v -shr 1) -band 1) frames=$(($v -shr 8) -band 0xFF)" }
function HeaderLang() { $h = Get-TestHeader $cfg; $mm = [regex]::Match($h, '(?m)^lang=(.*)$'); if ($mm.Success) { $mm.Groups[1].Value.TrimEnd("`r") } else { "(missing)" } }
function Launch() {
  $script:p = Start-Process $exe -PassThru
  $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $w = @([IU]::All([uint32]$script:p.Id, "OneKeyMainWindow$suffix")); if ($w.Count -gt 0) { $script:m = $w[0]; break }; Start-Sleep -Milliseconds 100 }
  if ($script:m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 700
}
function Quit() { [void][IU]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i=0; $i -lt 50 -and -not $p.HasExited; $i++) { Start-Sleep -Milliseconds 100 } }

# expected texts (see src/OneKey/i18n/strings.tsv)
$jaSettings = U '\u8a2d\u5b9a'                                   # set: "settings" (list.settings, ja)
$jaAdvanced = U '\u958b\u304f'                                   # set.btnOpen (ja) - the [Open] button on the advanced settings row
$zhAdvanced = U '\u6253\u5f00'                                   # set.btnOpen (zh-Hans)
$viAdd = '+ ' + (U 'Th\u00eam')                           # list.add (vi); one space since 0.5.2 (the [+ Add] pill)
$viUnlock = U 'M\u1edf kh\u00f3a'                                # lock.unlock (vi)
$viHelpTitle = U 'Tr\u1ee3 gi\u00fap \u00b7 Danh s\u00e1ch ph\u00edm t\u1eaft'   # help.listTitle (vi)
$mixed = U '\uacb0\uc7ac \u7ed3\u7b97 \u6c7a\u6e08 Thanh to\u00e1n'   # a dummy name in four scripts

try {
  Stop-TestInstances $suffix
  if (Test-Path $cfg) { Get-ChildItem $cfg -Recurse | Remove-Item -Force -Recurse -ErrorAction Ignore }; New-Item -ItemType Directory -Force $cfg | Out-Null
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_WORKAREA = $TallScreen
  $env:ONEKEY_TEST_THEME = $null; $env:ONEKEY_TEST_FAIL = $null
  $env:ONEKEY_TEST_LANG = "en"   # this run's "Windows display language"
  Launch

  # ---- I01 first run in English
  Check I01 "Get started" (Text 103) "first-run lock screen follows the (simulated) English Windows"

  # ---- I02 list in English, header lang= empty
  SetText 101 "Master1234"; SetText 102 "Master1234"; Click 103; Answer (Box) 1
  Click 220; Key 2005 0x24; Click 2012 3500
  Confirm-AutoLockOff $cfg
  Check I02 "+ Add|?||" ((Text 203) + "|" + (Text 250) + "|" + (HeaderLang) + "|") "English list; header lang= empty (follow Windows)"

  # an item with a mixed-script dummy name (glyph fallback: Pretendard has Hangul/Latin, not Han)
  Click 203; Click 4001; SetText 301 $mixed; SetText 302 "dummy-i18n"; Click 310 900; Answer (Box) 1

  # ---- I03 preview Japanese (index: 0 follow, 1 ko, 2 en, 3 zh-Hans, 4 ja, 5 vi)
  Click 220
  for ($k = 0; $k -lt 4; $k++) { Key 2009 0x28 }
  Start-Sleep -Milliseconds 500
  Check I03 "$jaAdvanced|on=1 lang=3 sys=1" ((Text 212) + "|" + (Dw)) "Japanese previews at once with the system Japanese font"

  # ---- I04 [Cancel] drops the preview
  Click 241
  Check I04 "+ Add|" ((Text 203) + "|" + (HeaderLang)) "[Cancel]: back to English, nothing saved"

  # ---- I05 preview Chinese (Simplified)
  Click 220
  for ($k = 0; $k -lt 3; $k++) { Key 2009 0x28 }
  Start-Sleep -Milliseconds 500
  Check I05 "$zhAdvanced|on=1 lang=2 sys=1" ((Text 212) + "|" + (Dw)) "Chinese (Simplified) previews with the system Chinese font"

  # ---- I06 Vietnamese + [Save]
  Key 2009 0x28; Key 2009 0x28   # zh-Hans -> ja -> vi
  Click 2012 3500
  Check I06 "vi|$viAdd|on=1 lang=4 sys=0" ((HeaderLang) + "|" + (Text 203) + "|" + (Dw)) "[Save]: header lang=vi, Vietnamese list with the embedded Pretendard"

  # ---- I07 restart: the lock screen is Vietnamese before unlocking
  Quit
  Launch
  Check I07 $viUnlock (Text 103) "after a restart the lock screen is already Vietnamese"

  # ---- I08 help in Vietnamese, no failed DirectWrite frame so far (mixed-script row drawn)
  SetText 101 "Master1234"; Click 103 1200
  Click 250
  $b = Box
  $title = if ($b -ne [IntPtr]::Zero) { [IU]::Text([IU]::GetDlgItem($b, 100)) } else { "(no box)" }
  Answer $b 1
  Check I08 "$viHelpTitle|failed=0 frames=0" ($title + "|" + (DwFrames)) "Vietnamese help; every text so far drawn with DirectWrite without a failed frame"
}
catch { Add-Failure ("exception: " + $_.Exception.Message) }
finally {
  if ($m -ne [IntPtr]::Zero -and $p -and -not $p.HasExited) { Quit }
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST_LANG = "ko"
}
Complete-Checks
