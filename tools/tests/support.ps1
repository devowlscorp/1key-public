# Support screen (0.5.14, user 2026-10-09: a support page in a place that does not get in the way). Entry only from Settings
# (last row "Support 1Key [Open]"); nothing on the list header or the tray menu, nothing opens by itself.
# SP01 the list has no support control; Settings has the row (2504)
# SP02 the row opens the support screen: back (2501), [Open] (2502), [Close] (2503), the title text, the KakaoPay label
# SP03 [Open] goes through the allowlist only: test mode records exactly SupportLinks.KakaoPay once (no browser is started)
# SP04 Esc on the support screen goes back to Settings
# SP05 the back knob goes back to Settings
# SP06 [Close] goes back to Settings
# SP07 no network: the test process has no TCP connection while the support screen is shown
# SP08 accessible name of [Open] says what it opens (CtlAcc name: "<browser label> — <page label>", so it names KakaoPay)
# 0.5.15-O (user 2026-10-09: affiliate links as buttons with a detail box, Korean screen only; GitHub star in every language):
# SP09 Korean screen: GitHub row (2507) and the shop card with Coupang (2505) / MyRealTrip (2506) [Details]
# SP10 GitHub [Open] records exactly SupportLinks.GitHub once (no box)
# SP11 Coupang [Details] opens a box with the exact Coupang Partners disclosure; [Cancel] opens nothing, [Open in browser] records SupportLinks.Coupang once
# SP12 MyRealTrip [Details]: the box has the disclosure with "수수료를 지급받습니다" (no conditional wording); [Open in browser] records SupportLinks.MyRealTrip once
# SP13 English screen: GitHub row present, no affiliate buttons
# -Shots: PrintWindow pictures of the screen (test window only) in 5 languages x light/dark into the test folder (looked at, not judged).
# Own config folder and instance suffix; the real 1Key and its settings are not touched. UTF-8 with BOM (Korean title strings).
param([string]$Exe = "", [switch]$Shots)
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
Start-Checks -Required @('SP01', 'SP02', 'SP03', 'SP04', 'SP05', 'SP06', 'SP07', 'SP08', 'SP09', 'SP10', 'SP11', 'SP12', 'SP13')
$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$suffix = ".support"
$cfg = "$sp\support_cfg"; if (Test-Path $cfg) { Get-ChildItem $cfg -Recurse | Remove-Item -Force -Recurse -ErrorAction Ignore }; New-Item -ItemType Directory -Force $cfg | Out-Null
$master = "Sp-Dummy-6630"
$linksSrc = Get-Content (Join-Path $PSScriptRoot "..\..\src\OneKey\SupportLinks.cs") -Raw
function LinkOf($n) { ([regex]::Match($linksSrc, "$n\s*=\s*""([^""]+)""")).Groups[1].Value }
$kakao = LinkOf 'KakaoPay'; $coupang = LinkOf 'Coupang'; $mrt = LinkOf 'MyRealTrip'; $github = LinkOf 'GitHub'
$coupangDisclosure = "이 포스팅은 쿠팡 파트너스 활동의 일환으로, 이에 따른 일정액의 수수료를 제공받습니다."
Add-Type -ReferencedAssemblies System.Drawing @'
using System;using System.Runtime.InteropServices;using System.Text;using System.Collections.Generic;using System.Drawing;
public class SPU {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr GetPropW(IntPtr h, string name);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  [DllImport("oleacc.dll")] public static extern int AccessibleObjectFromWindow(IntPtr h, int id, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object o);
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static IntPtr FindCls(uint pid, string cls) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
  public static List<string> Texts(IntPtr m) { var r = new List<string>(); EnumChildWindows(m, (h,l) => { var t = new StringBuilder(1024); GetWindowTextW(h, t, 1024); if (t.Length > 0) r.Add(t.ToString()); return true; }, IntPtr.Zero); return r; }
  public static bool Shot(IntPtr h, string path) { RECT r; if (!GetWindowRect(h, out r)) return false; using (var b = new Bitmap(r.R - r.L, r.B - r.T)) { using (var g = Graphics.FromImage(b)) { IntPtr dc = g.GetHdc(); try { PrintWindow(h, dc, 2); } finally { g.ReleaseHdc(dc); } } b.Save(path); } return true; }
}
'@
function SetText($id, $s) { [void][SPU]::SendMessageW([SPU]::GetDlgItem($m, $id), 0x000C, [IntPtr]::Zero, [string]$s) }
function Click($id, $ms = 700) { [void][SPU]::PostMessageW([SPU]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds $ms }
function Has($id) { [SPU]::GetDlgItem($m, $id) -ne [IntPtr]::Zero }
function Prop($n) { [int][SPU]::GetPropW($m, $n) }
function AccName($id) {
  $h = [SPU]::GetDlgItem($m, $id); if ($h -eq [IntPtr]::Zero) { return "" }
  $iid = [Guid]"618736E0-3C3D-11CF-810C-00AA00389B71"; $o = $null
  if ([SPU]::AccessibleObjectFromWindow($h, -4, [ref]$iid, [ref]$o) -ne 0 -or $null -eq $o) { return "" }
  try { [string]$o.GetType().InvokeMember("accName", [Reflection.BindingFlags]::GetProperty, $null, $o, @(0)) } catch { "" }
}
function Start1Key() {
  $script:p = Start-Process $exe -PassThru; $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $script:m = [SPU]::FindCls([uint32]$script:p.Id, "OneKeyMainWindow$suffix"); if ($script:m -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 100 }
  if ($script:m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 700
}
function Quit1Key() { if ($script:p -and -not $script:p.HasExited) { [void][SPU]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i = 0; $i -lt 50 -and -not $script:p.HasExited; $i++) { Start-Sleep -Milliseconds 100 }; if (-not $script:p.HasExited) { Stop-Process -Id $script:p.Id -Force } } }
function OpenSupport() { if (-not (Has 2504)) { Click 220 }; Click 2504 }
$supportTitles = @("후원하기", "Support 1Key", "支持 1Key", "1Keyを応援", "Ủng hộ 1Key")

try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_NO_AUTOLOCK = "1"; $env:ONEKEY_TEST_LANG = "ko"
  Start1Key
  SetText 101 $master; SetText 102 $master; Click 103 1500
  if (-not (Has 203)) { throw "setup: no list after creating the master" }

  $listTexts = [SPU]::Texts($m)
  $onList = @($listTexts | Where-Object { $supportTitles -contains $_ }).Count
  $rowOnList = Has 2504
  Click 220
  Check SP01 "0|False|True" "$onList|$rowOnList|$(Has 2504)" "list has no support control ($onList), settings has the row"

  Click 2504
  $t = [SPU]::Texts($m)
  Check SP02 "True|True|True|True|True" "$(Has 2501)|$(Has 2502)|$(Has 2503)|$($t -contains [string]$supportTitles[0])|$($t -contains [string]'카카오페이')" "support screen: back, [Open], [Close], title, KakaoPay label"

  $log = Join-Path $cfg "support-open.txt"; Remove-Item $log -ErrorAction Ignore
  $ns0 = @(Get-NetTCPConnection -OwningProcess $p.Id -ErrorAction Ignore).Count
  Click 2502 900
  $lines = if (Test-Path $log) { @(Get-Content $log) } else { @() }
  Check SP03 "1|$kakao|1" "$($lines.Count)|$($lines -join ';')|$(Prop 'OneKeyTestSupportOpen')" "[Open] records exactly the allowlisted KakaoPay link once"

  Start-Sleep -Seconds 2
  $ns = @(Get-NetTCPConnection -OwningProcess $p.Id -ErrorAction Ignore).Count
  Check SP07 "0|0" "$ns0|$ns" "no TCP connection of the test process while the support screen is shown"

  Check SP08 "True" "$((AccName 2502) -like '*카카오페이*')" "accessible name of [Open]: '$(AccName 2502)'"

  # 0.5.15-O: GitHub star (every language) and affiliate links behind a disclosure box (Korean only)
  $t9 = [SPU]::Texts($m)
  Check SP09 "True|True|True|True" "$(Has 2507)|$(Has 2505)|$(Has 2506)|$($t9 -contains [string]'필요한 걸 사실 때')" "Korean screen: GitHub row, Coupang and MyRealTrip [Details], shop heading"
  function OpenLog() { if (Test-Path $log) { @(Get-Content $log) } else { @() } }
  Remove-Item $log -ErrorAction Ignore
  Click 2507 900
  $l10 = OpenLog
  Check SP10 "1|$github|False" "$($l10.Count)|$($l10 -join ';')|$([SPU]::FindCls([uint32]$p.Id, 'OneKeyDialog') -ne [IntPtr]::Zero)" "GitHub [Open] records the allowlisted repository link once, no box"
  function Affiliate($id, $tag) {
    Remove-Item $log -ErrorAction Ignore
    Click $id 900
    $d = [SPU]::FindCls([uint32]$p.Id, "OneKeyDialog")
    $txt = if ($d -ne [IntPtr]::Zero) { ([SPU]::Texts($d)) -join "`n" } else { "" }
    if ($d -ne [IntPtr]::Zero) { [void][SPU]::PostMessageW($d, 0x0111, [IntPtr]2, [IntPtr]::Zero); Start-Sleep -Milliseconds 700 }
    $afterCancel = @(OpenLog).Count
    Click $id 900
    $d2 = [SPU]::FindCls([uint32]$p.Id, "OneKeyDialog")
    if ($d2 -ne [IntPtr]::Zero) { [void][SPU]::PostMessageW($d2, 0x0111, [IntPtr]1, [IntPtr]::Zero); Start-Sleep -Milliseconds 900 }
    return @{ Box = ($d -ne [IntPtr]::Zero); Text = $txt; Cancel = $afterCancel; Lines = @(OpenLog) }
  }
  $r11 = Affiliate 2505 "coupang"
  Check SP11 "True|True|0|1|$coupang" "$($r11.Box)|$($r11.Text.Contains($coupangDisclosure))|$($r11.Cancel)|$($r11.Lines.Count)|$($r11.Lines -join ';')" "Coupang [Details]: box with the exact disclosure; [Cancel] opens nothing; [Open in browser] records the Coupang link once"
  $r12 = Affiliate 2506 "mrt"
  $noCond = -not ($r12.Text -match "수 있")
  Check SP12 "True|True|True|0|1|$mrt" "$($r12.Box)|$($r12.Text.Contains('수수료를 지급받습니다'))|$noCond|$($r12.Cancel)|$($r12.Lines.Count)|$($r12.Lines -join ';')" "MyRealTrip [Details]: disclosure with a definite '수수료 지급'; [Cancel] opens nothing; [Open in browser] records the link once"

  # Esc: a key press in the focused control goes through the message loop (IsDialogMessage -> IDCANCEL)
  $pre4 = Has 2502
  [void][SPU]::PostMessageW([SPU]::GetDlgItem($m, 2502), 0x0100, [IntPtr]0x1B, [IntPtr]0x00010001); Start-Sleep -Milliseconds 80
  [void][SPU]::PostMessageW([SPU]::GetDlgItem($m, 2502), 0x0101, [IntPtr]0x1B, [IntPtr]0xC0010001); Start-Sleep -Milliseconds 700
  Check SP04 "True|True|False" "$pre4|$(Has 2504)|$(Has 2502)" "Esc on the support screen: back to Settings"

  Click 2504; $pre5 = Has 2501; Click 2501
  Check SP05 "True|True|False" "$pre5|$(Has 2504)|$(Has 2502)" "back knob on the support screen: back to Settings"
  Click 2504; $pre6 = Has 2503; Click 2503
  Check SP06 "True|True|False" "$pre6|$(Has 2504)|$(Has 2502)" "[Close] on the support screen: back to Settings"
  Quit1Key

  $env:ONEKEY_TEST_LANG = "en"
  Start1Key; SetText 101 $master; Click 103 1500
  OpenSupport
  Check SP13 "True|True|False|False" "$(Has 2502)|$(Has 2507)|$(Has 2505)|$(Has 2506)" "English screen: KakaoPay and GitHub rows, no affiliate buttons"
  Quit1Key

  if ($Shots) {
    $dir = Join-Path $sp ("support-shots-{0:yyyyMMdd-HHmmss}" -f (Get-Date)); New-Item -ItemType Directory $dir | Out-Null
    foreach ($lang in "ko", "en", "zh-Hans", "ja", "vi") {
      foreach ($th in "light", "dark") {
        $env:ONEKEY_TEST_LANG = $lang; $env:ONEKEY_TEST_THEME = $th
        Start1Key; SetText 101 $master; Click 103 1500
        OpenSupport; Start-Sleep -Milliseconds 400
        [void][SPU]::Shot($m, "$dir\support-$lang-$th.png")
        Quit1Key
      }
    }
    "pictures: $dir"
  }
} catch { Add-Failure ("exception: " + $_) }
finally {
  Quit1Key
  Stop-TestInstances $suffix
  foreach ($v in "ONEKEY_TEST_LANG", "ONEKEY_TEST_THEME", "ONEKEY_TEST_NO_AUTOLOCK") { Remove-Item "env:$v" -ErrorAction Ignore }
}
Complete-Checks
