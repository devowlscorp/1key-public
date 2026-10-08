# Screen and help captures for a person to review (2026-10-05 user: check every help text against the screens, colors in dark and
# light). Both themes at 100 %: list (one dummy item), add kind, edit (cursor / linked), settings (top / bottom), master change,
# backup, restore, advanced, lock widget (narrow / wide); and every help window - first section, then each section opened one by one.
# PNGs: <out>\shots-help\<theme>-<nn>-<name>.png. Judges only that captures were made (SH01). Dummy master, own config folder and
# suffix; the real 1Key is not touched. Windows appear: run while nobody uses the PC. ASCII only.
param([string]$Exe = "", [string]$Out = "")
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
Start-Checks -Required @("SH01")
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
if (-not $Out) { $Out = $sp }
$dir = Join-Path $Out "shots-help"; if (Test-Path $dir) { Get-ChildItem $dir -Filter *.png | Remove-Item -Force }; New-Item -ItemType Directory -Force $dir | Out-Null
$suffix = ".shh"
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type -ReferencedAssemblies System.Drawing @'
using System;using System.Runtime.InteropServices;using System.Text;using System.Drawing;
public class SH {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static IntPtr Find(uint pid, string cls) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls && IsWindowVisible(h)) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
  public static bool Shot(IntPtr h, string path) {
    RECT r; if (!GetWindowRect(h, out r)) return false; int w = r.R - r.L, hh = r.B - r.T; if (w <= 0 || hh <= 0) return false;
    using (var b = new Bitmap(w, hh)) { using (var g = Graphics.FromImage(b)) { IntPtr dc = g.GetHdc(); bool ok = PrintWindow(h, dc, 2); g.ReleaseHdc(dc); if (!ok) return false; } b.Save(path); }
    return true;
  }
  public static bool ScreenShot(IntPtr h, string path, int pad) {
    RECT r; if (!GetWindowRect(h, out r)) return false; int w = r.R - r.L + pad * 2, hh = r.B - r.T + pad * 2;
    using (var b = new Bitmap(w, hh)) { using (var g = Graphics.FromImage(b)) g.CopyFromScreen(r.L - pad, r.T - pad, 0, 0, new Size(w, hh)); b.Save(path); }
    return true;
  }
}
'@
$Z = [IntPtr]::Zero
function Click($id, $ms = 700) { [void][SH]::PostMessageW([SH]::GetDlgItem($m, $id), 0x00F5, $Z, $Z); Start-Sleep -Milliseconds $ms }
function SetText($id, $s) { [void][SH]::SendMessageW([SH]::GetDlgItem($m, $id), 0x000C, $Z, [string]$s) }
function Box() { for ($i = 0; $i -lt 30; $i++) { $b = [SH]::Find([uint32]$p.Id, "OneKeyDialog"); if ($b -ne $Z) { Start-Sleep -Milliseconds 400; return $b }; Start-Sleep -Milliseconds 100 }; $Z }
function Answer($b, $cmd) { if ($b -ne $Z) { [void][SH]::PostMessageW($b, 0x0111, [IntPtr]$cmd, $Z); Start-Sleep -Milliseconds 500 } }
$script:n = 0; $script:made = 0; $script:want = 0
function Snap($h, $name) { $script:n++; $script:want++; $f = "{0}\{1}-{2:D2}-{3}.png" -f $dir, $theme, $script:n, $name; if ($h -ne $Z -and [SH]::Shot($h, $f)) { $script:made++ } else { "missing: $name" } }
function Start1Key() {
  $script:p = Start-Process $exe -PassThru; $script:m = $Z
  for ($i = 0; $i -lt 80; $i++) { $script:m = [SH]::Find([uint32]$script:p.Id, "OneKeyMainWindow$suffix"); if ($script:m -ne $Z) { break }; Start-Sleep -Milliseconds 100 }
  Start-Sleep -Milliseconds 900
}
function Quit1Key() { if ($script:p -and -not $script:p.HasExited) { [void][SH]::PostMessageW($m, 0x8005, $Z, $Z); for ($i = 0; $i -lt 40 -and -not $script:p.HasExited; $i++) { Start-Sleep -Milliseconds 100 }; if (-not $script:p.HasExited) { Stop-Process -Id $script:p.Id -Force } } }
# help window: first section as opened, then each further section opened on its own (sections are buttons 110 + k)
function HelpShots($name) {
  Click 250 900
  $b = Box; if ($b -eq $Z) { "missing help: $name"; $script:want++; return }
  Snap $b "help-$name-1"
  for ($k = 1; $k -lt 12; $k++) {
    $head = [SH]::GetDlgItem($b, 110 + $k); if ($head -eq $Z) { break }
    [void][SH]::PostMessageW($head, 0x00F5, $Z, $Z); Start-Sleep -Milliseconds 500
    Snap $b ("help-$name-" + ($k + 1))
    [void][SH]::PostMessageW($head, 0x00F5, $Z, $Z); Start-Sleep -Milliseconds 300   # close it again so the next one fits
  }
  Answer $b 1
}

$bgForm = New-Object Windows.Forms.Form; $bgForm.FormBorderStyle = "None"; $bgForm.StartPosition = "Manual"; $scr = [Windows.Forms.Screen]::PrimaryScreen.Bounds
$bgForm.Left = $scr.Left; $bgForm.Top = $scr.Top; $bgForm.Width = $scr.Width; $bgForm.Height = $scr.Height; $bgForm.BackColor = [Drawing.Color]::FromArgb(40, 40, 40); $bgForm.ShowInTaskbar = $false
try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_NO_AUTOLOCK = "1"; $env:ONEKEY_TEST_LANG = "ko"
  $env:ONEKEY_TEST_WORKAREA = "1600,1000,96"
  foreach ($theme in "dark", "light") {
    $script:n = 0
    $env:ONEKEY_TEST_THEME = $theme
    $cfg = Join-Path $sp "shots_help_cfg"; if (Test-Path $cfg) { Remove-Item $cfg -Recurse -Force }; New-Item -ItemType Directory -Force $cfg | Out-Null
    $env:ONEKEY_CONFIG_DIR = $cfg
    $env:ONEKEY_TEST_LOCKWIDGET = "0"
    Start1Key
    Snap $m "setup"
    SetText 101 "Shots-Dummy-77"; SetText 102 "Shots-Dummy-77"; Click 103 1500
    Click 203; Snap $m "addkind"
    Click 4001; SetText 301 "Sample"; SetText 302 "sample-pw"; Snap $m "edit-cursor"
    HelpShots "edit"
    [void][SH]::PostMessageW([SH]::GetDlgItem($m, 348), 0x0100, [IntPtr]0x28, $Z); Start-Sleep -Milliseconds 800   # put-where: linked
    Snap $m "edit-linked"
    [void][SH]::PostMessageW([SH]::GetDlgItem($m, 348), 0x0100, [IntPtr]0x26, $Z); Start-Sleep -Milliseconds 800
    Click 310 900; $b = [SH]::Find([uint32]$p.Id, "OneKeyDialog"); if ($b -ne $Z) { Answer $b 2 }
    Snap $m "list"
    HelpShots "list"
    Click 220 900; Snap $m "settings"
    HelpShots "settings"
    for ($i = 0; $i -lt 12; $i++) { [void][SH]::SendMessageW($m, 0x020A, [IntPtr]((-120) -shl 16), $Z); Start-Sleep -Milliseconds 60 }
    Start-Sleep -Milliseconds 400; Snap $m "settings-bottom"
    Click 213 900; Snap $m "master"
    SetText 502 "Newer-Master-88"; SetText 503 "Newer-Master-89"; Start-Sleep -Milliseconds 300; Snap $m "master-mismatch"
    HelpShots "master"
    Click 506 900
    if (-not ([SH]::GetDlgItem($m, 216) -ne $Z)) { Click 220 900 }
    Click 216 900; Snap $m "backup"
    HelpShots "backup"
    Click 525 900
    if (-not ([SH]::GetDlgItem($m, 217) -ne $Z)) { Click 220 900 }
    Click 217 900; $b = Box; Snap $b "restore-question"; Answer $b 7
    Snap $m "restore"
    Click 533 900
    if (-not ([SH]::GetDlgItem($m, 212) -ne $Z)) { Click 220 900 }
    Click 212 900; Snap $m "advanced"
    HelpShots "advanced"
    Quit1Key
    # lock widget: narrow (cue text over a dark background), then wide
    $env:ONEKEY_TEST_LOCKWIDGET = "1"
    $bgForm.Show(); [Windows.Forms.Application]::DoEvents()
    Start1Key
    $w = [SH]::Find([uint32]$p.Id, "OneKeyLockWidget"); Start-Sleep -Milliseconds 600
    $script:want++; $script:n++; if ($w -ne $Z -and [SH]::ScreenShot($w, ("{0}\{1}-{2:D2}-widget-narrow.png" -f $dir, $theme, $script:n), 8)) { $script:made++ }
    $host0 = [SH]::Find([uint32]$p.Id, "OneKeyLockInput"); $e = $Z; if ($host0 -ne $Z) { $e = [SH]::GetDlgItem($host0, 101) }
    if ($e -ne $Z) { [void][SH]::PostMessageW($e, 0x0100, [IntPtr]0x23, [IntPtr]0x014F0001); Start-Sleep -Milliseconds 700 }
    $script:want++; $script:n++; if ($w -ne $Z -and [SH]::ScreenShot($w, ("{0}\{1}-{2:D2}-widget-wide.png" -f $dir, $theme, $script:n), 8)) { $script:made++ }
    Quit1Key
    $bgForm.Hide()
  }
} catch { Add-Failure ("exception: " + $_.Exception.Message) }
finally { Quit1Key; Stop-TestInstances $suffix; $bgForm.Close(); $env:ONEKEY_TEST_THEME = $null; $env:ONEKEY_TEST_WORKAREA = $null; $env:ONEKEY_TEST_LOCKWIDGET = "0" }
Check SH01 "True" "$($script:made -ge 40 -and $script:made -eq $script:want)" "captures made ($($script:made) of $($script:want)) in $dir"
Complete-Checks
