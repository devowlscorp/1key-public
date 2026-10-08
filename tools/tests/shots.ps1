# Final-candidate screen captures (Codex R166-C1 3/4, KeepCardBorders, 0.2.169): both themes x 100/125/150 % (simulated with
# ONEKEY_TEST_WORKAREA=<w>,<h>,<dpi>), each: old lock screen (first setup), list, settings (top and scrolled to the bottom), and the
# lock widget (narrow and wide). PNGs go to <out>\shots\<theme>-<dpi>-<screen>.png for a person to look at; this script judges only
# that every capture was made (SC01). Dummy master only, no items, own config folder and suffix; the real 1Key is not touched.
# The main window is captured with PrintWindow (no foreground needed); the widget is a layered window, so it is captured from the
# screen with a plain background window behind it. Run while nobody uses the PC (windows appear). ASCII only.
param([string]$Exe = "", [string]$Out = "", [switch]$Quick)   # -Quick: 100 % only (both themes, 12 captures) - a version-only candidate (Codex 14:08)
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
Start-Checks -Required @("SC01")
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
if (-not $Out) { $Out = $sp }
$dir = Join-Path $Out "shots"; New-Item -ItemType Directory -Force $dir | Out-Null
$suffix = ".shots"
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type -ReferencedAssemblies System.Drawing @'
using System;using System.Runtime.InteropServices;using System.Text;using System.Drawing;
public class SC {
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
function Click($id) { [void][SC]::PostMessageW([SC]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 700 }
function Start1Key() {
  $script:p = Start-Process $exe -PassThru; $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $script:m = [SC]::Find([uint32]$script:p.Id, "OneKeyMainWindow$suffix"); if ($script:m -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 100 }
  Start-Sleep -Milliseconds 900
}
function Quit1Key() { if ($script:p -and -not $script:p.HasExited) { [void][SC]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i = 0; $i -lt 40 -and -not $script:p.HasExited; $i++) { Start-Sleep -Milliseconds 100 }; if (-not $script:p.HasExited) { Stop-Process -Id $script:p.Id -Force } } }

$made = 0; $want = 0; $missing = @()
$bgForm = New-Object Windows.Forms.Form; $bgForm.FormBorderStyle = "None"; $bgForm.StartPosition = "Manual"; $scr = [Windows.Forms.Screen]::PrimaryScreen.Bounds; $bgForm.Left = $scr.Left; $bgForm.Top = $scr.Top; $bgForm.Width = $scr.Width; $bgForm.Height = $scr.Height;   # the whole primary screen: no real desktop content behind the widget (Codex 12:41) $bgForm.BackColor = [Drawing.Color]::FromArgb(40, 40, 40); $bgForm.ShowInTaskbar = $false
try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_NO_AUTOLOCK = "1"
  foreach ($theme in "dark", "light") {
    foreach ($dpi in $(if ($Quick) { @(96) } else { @(96, 120, 144) })) {
      $env:ONEKEY_TEST_THEME = $theme
      $env:ONEKEY_TEST_WORKAREA = "$([int](1600 * $dpi / 96)),$([int](1000 * $dpi / 96)),$dpi"
      $cfg = Join-Path $sp "shots_cfg"; if (Test-Path $cfg) { Remove-Item $cfg -Recurse -Force }; New-Item -ItemType Directory -Force $cfg | Out-Null
      $env:ONEKEY_CONFIG_DIR = $cfg
      $tag = "$theme-$dpi"
      # old lock screen (first setup), then create the dummy master there
      $env:ONEKEY_TEST_LOCKWIDGET = "0"
      Start1Key
      $want++; if ([SC]::Shot($m, "$dir\$tag-1-setup.png")) { $made++ } else { $missing += "$tag-1-setup" }
      [void][SC]::SendMessageW([SC]::GetDlgItem($m, 101), 0x000C, [IntPtr]::Zero, "Shots-Dummy-77"); [void][SC]::SendMessageW([SC]::GetDlgItem($m, 102), 0x000C, [IntPtr]::Zero, "Shots-Dummy-77"); Click 103
      Start-Sleep -Milliseconds 800
      $want++; if ([SC]::Shot($m, "$dir\$tag-2-list.png")) { $made++ } else { $missing += "$tag-2-list" }
      Click 220; Start-Sleep -Milliseconds 500
      $want++; if ([SC]::Shot($m, "$dir\$tag-3-settings.png")) { $made++ } else { $missing += "$tag-3-settings" }
      for ($i = 0; $i -lt 12; $i++) { [void][SC]::SendMessageW($m, 0x020A, [IntPtr]((-120) -shl 16), [IntPtr]0); Start-Sleep -Milliseconds 60 }   # WM_MOUSEWHEEL down
      Start-Sleep -Milliseconds 400
      $want++; if ([SC]::Shot($m, "$dir\$tag-4-settings-bottom.png")) { $made++ } else { $missing += "$tag-4-settings-bottom" }
      Quit1Key
      # lock widget (the same file, unlock screen): narrow, then wide after a key
      $env:ONEKEY_TEST_LOCKWIDGET = "1"
      $bgForm.Show(); [Windows.Forms.Application]::DoEvents()
      Start1Key
      $w = [SC]::Find([uint32]$p.Id, "OneKeyLockWidget")
      Start-Sleep -Milliseconds 600
      $want++; if ($w -ne [IntPtr]::Zero -and [SC]::ScreenShot($w, "$dir\$tag-5-widget-narrow.png", 8)) { $made++ } else { $missing += "$tag-5-widget-narrow" }
      $host0 = [SC]::Find([uint32]$p.Id, "OneKeyLockInput")
      $e = [IntPtr]::Zero; if ($host0 -ne [IntPtr]::Zero) { $e = [SC]::GetDlgItem($host0, 101) }
      if ($e -ne [IntPtr]::Zero) { [void][SC]::PostMessageW($e, 0x0100, [IntPtr]0x23, [IntPtr]0x014F0001); Start-Sleep -Milliseconds 700 }   # End key: widens without typing
      $want++; if ($w -ne [IntPtr]::Zero -and [SC]::ScreenShot($w, "$dir\$tag-6-widget-wide.png", 8)) { $made++ } else { $missing += "$tag-6-widget-wide" }
      Quit1Key
      $bgForm.Hide()
    }
  }
} catch { Add-Failure ("exception: " + $_.Exception.Message) }
finally { Quit1Key; Stop-TestInstances $suffix; $bgForm.Close(); $env:ONEKEY_TEST_THEME = $null; $env:ONEKEY_TEST_WORKAREA = $null; $env:ONEKEY_TEST_LOCKWIDGET = "0" }
Check SC01 "$(if ($Quick) { 12 } else { 36 })" "$made"   # 2 themes x 3 scales x 6 screens; a fixed number, so "0 of 0" cannot pass (0.2.170 run 1) "captures made ($made of $want) in $dir$(if ($missing) { '; missing: ' + ($missing -join ', ') })"
Complete-Checks
