# WM_PRINTCLIENT common drawing (TODO line "code done, real capture not checked"): a picture made by PrintWindow without
# PW_RENDERFULLCONTENT (the window and its children draw themselves through WM_PRINT / WM_PRINTCLIENT) must match what is on the
# screen for the same window. Only the test instance's own window area is compared; no picture is kept unless a check fails
# (then lib\Check.ps1 saves the test window only).
# PC01 list screen: at most 3 % of client-area pixels differ by more than 48 (sum of R,G,B differences)
# PC02 settings screen (switches, dropdowns, cards): same rule
# PC03 item edit screen (fields, hotkey box, buttons): same rule
# Own config folder and instance suffix. ASCII only.
param([string]$Exe = "")
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
Start-Checks -Required @("PC01","PC02","PC03")
$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$suffix = ".pc"
$cfg = Join-Path $sp "printclient_cfg"
$master = "Pc-Dummy-4417"
Add-Type -ReferencedAssemblies System.Drawing @'
using System;using System.Drawing;using System.Drawing.Imaging;using System.Runtime.InteropServices;using System.Text;
public class PC {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Rt, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out R r, int size);
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static IntPtr MainWnd(uint pid, string cls) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
  [StructLayout(LayoutKind.Sequential)] public struct PT { public int X, Y; }
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out R r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref PT p);
  // percent of CLIENT-area pixels (8 px margin) whose R+G+B difference is over 48. PW_CLIENTONLY: the window and its children draw the
  // client area through WM_PRINT/WM_PRINTCLIENT; the frame/caption is not compared (PrintWindow draws it in the classic style).
  public static double Diff(IntPtr h) {
    R c; GetClientRect(h, out c); PT o = new PT(); ClientToScreen(h, ref o);
    int W = c.Rt - c.L, H = c.B - c.T;
    using (var a = new Bitmap(W, H, PixelFormat.Format32bppArgb)) using (var b = new Bitmap(W, H, PixelFormat.Format32bppArgb)) {
      using (var g = Graphics.FromImage(a)) { IntPtr dc = g.GetHdc(); try { PrintWindow(h, dc, 1); } finally { g.ReleaseHdc(dc); } }
      using (var g = Graphics.FromImage(b)) g.CopyFromScreen(o.X, o.Y, 0, 0, b.Size);
      int n = 0, bad = 0;
      for (int y = 8; y < H - 8; y += 2) for (int x = 8; x < W - 8; x += 2) {
        Color p = a.GetPixel(x, y), q = b.GetPixel(x, y); n++;
        if (Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B) > 48) bad++; }
      return n == 0 ? 100 : 100.0 * bad / n; } }
}
'@
function Click($id) { [void][PC]::PostMessageW([PC]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 700 }
function SetText($id, $s) { [void][PC]::SendMessageW([PC]::GetDlgItem($m, $id), 0x000C, [IntPtr]::Zero, $s) }
function MeasureDiff() { [void][PC]::SetForegroundWindow($m); Start-Sleep -Milliseconds 900; if ([PC]::GetForegroundWindow() -ne $m) { return -1 }; [Math]::Round([PC]::Diff($m), 2) }
try {
  Stop-TestInstances $suffix
  if (Test-Path $cfg) { Remove-Item $cfg -Recurse -Force }; New-Item -ItemType Directory -Force $cfg | Out-Null
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix
  $script:p = Start-Process $exe -PassThru; $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80 -and $m -eq [IntPtr]::Zero; $i++) { $script:m = [PC]::MainWnd([uint32]$p.Id, "OneKeyMainWindow$suffix"); Start-Sleep -Milliseconds 100 }
  if ($m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 800
  SetText 101 $master; SetText 102 $master; Click 103; Start-Sleep -Milliseconds 600
  Click 203; Click 4001; SetText 301 "Print one"; SetText 302 "pc-dummy-1"; Click 310; Start-Sleep -Milliseconds 600
  $d1 = MeasureDiff
  Check PC01 "True" "$($d1 -ge 0 -and $d1 -le 3)" "list screen: PrintWindow(0) vs screen differ on $d1 % of pixels"
  Click 220; Start-Sleep -Milliseconds 500
  $d2 = MeasureDiff
  Check PC02 "True" "$($d2 -ge 0 -and $d2 -le 3)" "settings screen: $d2 %"
  Click 240; Start-Sleep -Milliseconds 500; Click 1000; Start-Sleep -Milliseconds 500
  $d3 = MeasureDiff
  Check PC03 "True" "$($d3 -ge 0 -and $d3 -le 3)" "edit screen: $d3 %"
} catch { Add-Failure ("exception: " + $_.Exception.Message) }
finally { if ($p -and -not $p.HasExited) { [void][PC]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 800; if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force } }; Stop-TestInstances $suffix }
Complete-Checks
