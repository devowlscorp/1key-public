# Widget screen (0.5.26, user 2026-10-13: a way into the cat things from the main window - [Widget] left of [Settings]).
# WG00 Tray widget mode off: [Widget] (221) is on the list bar but disabled; Settings has no cat name fields any more (2033/2034)
# WG01 widget mode on and saved: [Widget] enabled
# WG02 [Widget] opens the screen: back 242, save 2013, name fields 2033/2034, play pills 244-246, the name line 251 filled, 4 pictures (OneKeyHelpArt)
# WG03 names: typed names saved with [Save] -> back on the list (203), and the screen shows them again when reopened
# WG04 back (242) with a changed name asks; [No] keeps the screen; [Cancel] (243) leaves without asking and the saved name stays
# WG05 a play pill (244 Pet) makes the cat do it: the cat log (flushed when 1Key exits) has "interact I1"
# WG06 the cat menu's [Notebook] path (WM_APP+80) with the window in the tray: the window comes back on the widget screen
# Screenshots of the screen (light and dark, top and scrolled) are kept next to the log for a look - no check reads them.
# Own config folder and instance suffix; the real 1Key is not touched. Windows and the cat are shown: run it while nobody uses the PC.
#   powershell -ExecutionPolicy Bypass -File tools\tests\widget.ps1 [-Exe <1Key.exe>]
# ASCII only.
param([string]$Exe = "")
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
$ids = @(); 0..6 | ForEach-Object { $ids += "WG{0:D2}" -f $_ }
Start-Checks -Required $ids

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$suffix = ".wg"
$root = Join-Path $sp ("widget-{0:yyyyMMdd-HHmmss}-{1}" -f (Get-Date), $PID); New-Item -ItemType Directory $root | Out-Null
$cfg = "$root\cfg"; New-Item -ItemType Directory $cfg | Out-Null
$log = "$root\cat.log"
$master = "Wg-Dummy-6271"
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing @'
using System;using System.Runtime.InteropServices;using System.Text;using System.Drawing;
public class WGU {
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
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr h);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, StringBuilder l);
  public static string Text(IntPtr h) { var t = new StringBuilder(512); SendMessageW(h, 0x000D, (IntPtr)512, t); return t.ToString(); }   // WM_GETTEXT: GetWindowText does not read an EDIT of another process
  public static IntPtr Find(uint pid, string cls, bool vis) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls && (!vis || IsWindowVisible(h))) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
  public static int CountChildren(IntPtr p, string cls) { int n = 0; EnumChildWindows(p, (h,l) => { if (Cls(h) == cls && IsWindowVisible(h)) n++; return true; }, IntPtr.Zero); return n; }
  public static bool Shot(IntPtr h, string path) {
    RECT r; if (!GetWindowRect(h, out r)) return false; int w = r.R - r.L, hh = r.B - r.T; if (w <= 0 || hh <= 0) return false;
    using (var b = new Bitmap(w, hh)) { using (var g = Graphics.FromImage(b)) { IntPtr dc = g.GetHdc(); bool ok = PrintWindow(h, dc, 2); g.ReleaseHdc(dc); if (!ok) return false; } b.Save(path, System.Drawing.Imaging.ImageFormat.Png); }
    return true;
  }
}
'@
[void][WGU]::SetProcessDPIAware()
function Start1Key() {
  $script:p = Start-Process $exe -PassThru; $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $script:m = [WGU]::Find([uint32]$script:p.Id, "OneKeyMainWindow$suffix", $false); if ($script:m -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 100 }
  if ($script:m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 900
}
function Quit1Key() { if ($script:p -and -not $script:p.HasExited) { [void][WGU]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i = 0; $i -lt 50 -and -not $script:p.HasExited; $i++) { Start-Sleep -Milliseconds 100 }; if (-not $script:p.HasExited) { Stop-Process -Id $script:p.Id -Force } } }
function Has($id) { [WGU]::GetDlgItem($m, $id) -ne [IntPtr]::Zero }
function Enabled($id) { $h = [WGU]::GetDlgItem($m, $id); ($h -ne [IntPtr]::Zero) -and [WGU]::IsWindowEnabled($h) }
function Click($id, $ms = 700) { [void][WGU]::PostMessageW([WGU]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds $ms }
function SetText($id, $s) { [void][WGU]::SendMessageW([WGU]::GetDlgItem($m, $id), 0x000C, [IntPtr]::Zero, [string]$s) }
function GetText($id) { $h = [WGU]::GetDlgItem($m, $id); if ($h -eq [IntPtr]::Zero) { "" } else { [WGU]::Text($h) } }
function FindBox() { for ($i = 0; $i -lt 20; $i++) { $b = [WGU]::Find([uint32]$p.Id, "OneKeyDialog", $true); if ($b -ne [IntPtr]::Zero) { return $b }; Start-Sleep -Milliseconds 100 }; [IntPtr]::Zero }
function AnswerBox($cmd) { $b = FindBox; if ($b -ne [IntPtr]::Zero) { [void][WGU]::PostMessageW($b, 0x0111, [IntPtr]$cmd, [IntPtr]::Zero); Start-Sleep -Milliseconds 500 }; $b -ne [IntPtr]::Zero }
function Shots($tag) {
  if ([WGU]::Shot($m, "$root\widget-$tag-top.png")) { "shot: widget-$tag-top.png" }
  for ($k = 1; $k -le 3; $k++) {
    [void][WGU]::SendMessageW($m, 0x020A, [IntPtr]((-480) -shl 16), [IntPtr]::Zero); Start-Sleep -Milliseconds 400   # WM_MOUSEWHEEL down
    if ([WGU]::Shot($m, "$root\widget-$tag-$k.png")) { "shot: widget-$tag-$k.png" }
  }
}

try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_LOCKWIDGET = "0"; $env:ONEKEY_TEST_NO_AUTOLOCK = "1"
  $env:ONEKEY_TEST_CAT_LOG = $log; $env:ONEKEY_TEST_THEME = "light"
  Start1Key
  SetText 101 $master; SetText 102 $master; Click 103 1500
  if (-not (Has 203)) { throw "setup: no list after creating the master" }

  # WG00 widget mode off: [Widget] disabled; Settings has no name fields
  $w0 = "$(Has 221)|$(Enabled 221)"
  Click 220
  $walkerOn = [int][WGU]::SendMessageW([WGU]::GetDlgItem($m, 2032), 0x00F0, [IntPtr]::Zero, [IntPtr]::Zero) -eq 1
  Check WG00 "True|False|False|False|False" "$w0|$walkerOn|$(Has 2033)|$(Has 2034)" "[Widget] present and disabled while widget mode is off; Settings: widget mode off, no cat name fields"

  # WG01 turn widget mode on and save
  [void][WGU]::PostMessageW([WGU]::GetDlgItem($m, 2032), 0x0100, [IntPtr]0x20, [IntPtr]::Zero); Start-Sleep -Milliseconds 300
  Click 2012 1200
  Check WG01 "True|True" "$(Has 203)|$(Enabled 221)" "widget mode saved: back on the list, [Widget] enabled"

  # WG02 open the widget screen
  Click 221 1200
  $pics = [WGU]::CountChildren($m, "OneKeyHelpArt")
  $nameLine = GetText 251
  Check WG02 "True|True|True|True|True|True|True|True|4" "$(Has 242)|$(Has 2013)|$(Has 2033)|$(Has 2034)|$(Has 244)|$(Has 245)|$(Has 246)|$($nameLine.Length -gt 0)|$pics" "widget screen: back, save, two name fields, three play pills, name line '$nameLine', pictures $pics"
  "pills: $(GetText 244) / $(GetText 245) / $(GetText 246); score: $(GetText 253); next: $(GetText 254); album: $(GetText 256)"
  Shots "light"

  # WG03 names saved
  Click 243 800; Click 221 1200   # the wheel moved the page: leave and reopen from the top
  SetText 2033 "Wg-Shiro"; SetText 2034 "Wg-Kuro"
  Click 2013 1200
  $onList = Has 203
  Click 221 1200
  Check WG03 "True|Wg-Shiro|Wg-Kuro" "$onList|$(GetText 2033)|$(GetText 2034)" "[Save] goes back to the list; reopened screen shows the saved names"
  $line3 = GetText 251; "name line now: $line3"

  # WG04 back with a change asks; [No] stays; [Cancel] leaves without asking
  SetText 2033 "Wg-Changed"
  Click 242 300
  $asked = AnswerBox 7   # IDNO
  $stay = Has 2033
  Click 243 1000
  $left = Has 203
  Click 221 1200
  Check WG04 "True|True|True|Wg-Shiro" "$asked|$stay|$left|$(GetText 2033)" "back asks (answered No -> stays); [Cancel] leaves; saved name unchanged"

  # WG05 play pill: Pet
  $pet = "$(Has 244)|$(Enabled 244)"
  Click 244 6000
  "pet pill after the click: $(GetText 244)"

  # WG06 [Notebook] path with the window in the tray
  Click 243 800
  [void][WGU]::PostMessageW($m, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 1200   # WM_CLOSE -> tray
  $hidden = -not [WGU]::IsWindowVisible($m)
  [void][WGU]::PostMessageW($m, 0x8050, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 1500   # WM_APP+80: what the cat menu's [Notebook] posts
  Check WG06 "True|True|True" "$hidden|$([WGU]::IsWindowVisible($m))|$(Has 2013)" "window in the tray, the [Notebook] message brings it back on the widget screen"
  Quit1Key; Start-Sleep -Milliseconds 500
  $hit = if (Test-Path $log) { @(Select-String -Path $log -Pattern 'interact I1').Count } else { 0 }
  Check WG05 "True|True|True" "$pet|$($hit -gt 0)" "Pet pill present and enabled; the cat log has 'interact I1' ($hit)"

  # dark screenshots (no checks)
  $env:ONEKEY_TEST_THEME = "dark"
  Start1Key
  SetText 101 $master; Click 103 1500
  if (Has 221) { Click 221 1200; Shots "dark" }
  Quit1Key
} catch { Add-Failure ("exception: " + $_) }
finally {
  Quit1Key; Stop-TestInstances $suffix
  foreach ($v in "ONEKEY_TEST_CAT_LOG", "ONEKEY_TEST_LOCKWIDGET", "ONEKEY_TEST_NO_AUTOLOCK", "ONEKEY_TEST_THEME") { Remove-Item "env:$v" -ErrorAction Ignore }
  "log kept: $log"
}
Complete-Checks
