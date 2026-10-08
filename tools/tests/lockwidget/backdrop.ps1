# Lock widget placeholder colour follows what is behind it (0.3.100, 2026-10-06 user: white text vanished on a white background).
# A plain window of one colour is put right under the widget (white, then black); the widget samples the screen under its first field
# once a second and leaves its result in the test property OneKeyTestBackdrop on the widget window (luminance 0-255, +1000 = light).
# BD01 white behind: judged light, luminance about 255 (the widget's own layered window is not in the sample)
# BD02 black behind: judged dark, luminance about 0 (again nothing of the widget itself - its white text would raise it)
# Own config folder and test suffix; the real 1Key and its settings are not touched. Run only while the PC is idle.
param([string]$Exe = "")
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "..\lib\Check.ps1")
Start-Checks -Required @("BD01", "BD02")

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$suffix = ".bd"
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @'
using System;using System.Runtime.InteropServices;using System.Text;
public class BD {
  public delegate bool EnumCb(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumCb cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr GetPropW(IntPtr h, string name);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static IntPtr Find(uint pid, string cls) { IntPtr r = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls && IsWindowVisible(h)) { r = h; return false; } return true; }, IntPtr.Zero); return r; }
}
'@
$cfg = Join-Path $sp ("backdrop-{0:yyyyMMdd-HHmmss}-{1}" -f (Get-Date), $PID)
New-Item -ItemType Directory -Force $cfg | Out-Null
$form = $null; $p = $null
function Pump($ms) { $t = [Environment]::TickCount; while ([Environment]::TickCount - $t -lt $ms) { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 30 } }
function Backdrop($w) { [int][BD]::GetPropW($w, "OneKeyTestBackdrop") }
try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_LOCKWIDGET = "1"
  $p = Start-Process $exe -PassThru
  $w = [IntPtr]::Zero
  for ($i = 0; $i -lt 80 -and $w -eq [IntPtr]::Zero; $i++) { Start-Sleep -Milliseconds 150; $w = [BD]::Find([uint32]$p.Id, "OneKeyLockWidget") }
  if ($w -eq [IntPtr]::Zero) { throw "no lock widget" }
  $r = New-Object BD+RECT; [void][BD]::GetWindowRect($w, [ref]$r)
  $form = New-Object Windows.Forms.Form
  $form.FormBorderStyle = 'None'; $form.ShowInTaskbar = $false; $form.StartPosition = 'Manual'
  $form.Location = New-Object Drawing.Point ($r.L - 40), ($r.T - 40); $form.Size = New-Object Drawing.Size ($r.R - $r.L + 80), ($r.B - $r.T + 80)
  $results = @()
  foreach ($c in @([Drawing.Color]::White, [Drawing.Color]::Black)) {
    $form.BackColor = $c
    if (-not $form.Visible) { $form.Show() }
    $form.Refresh()
    [void][BD]::SetWindowPos($w, [IntPtr]::Zero, 0, 0, 0, 0, 0x0013)   # widget back on top of the form (NOSIZE|NOMOVE|NOACTIVATE)
    Pump 2600   # two samples
    $results += Backdrop $w
  }
  $white = $results[0]; $black = $results[1]
  "  backdrop values: white $white, black $black"
  Check BD01 "True|True" "$($white -ge 1000)|$((($white - 1000) -ge 240))" "white behind the widget: judged light, luminance about 255 (value $white)"
  Check BD02 "True|True" "$($black -lt 1000)|$($black -le 15)" "black behind the widget: judged dark, luminance about 0 - the widget itself is not sampled (value $black)"
} catch { Add-Failure ("exception: " + $_) }
finally {
  if ($form) { $form.Close(); $form.Dispose() }
  if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force }
  Stop-TestInstances $suffix
}
Complete-Checks
