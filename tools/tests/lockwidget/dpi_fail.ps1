param([string]$Exe = "")
# Lock widget demo: render-failure fallback signals and a simulated DPI change (WM_DPICHANGED injection; real monitor moves are not
# covered here). Codex R-W5: each value is compared and a mismatch or timeout fails the run (exit 1). ASCII only.
# DF01 failure at the first render -> no widget, exit code 3 (the caller would use the old lock screen)
# DF02 failure while shown -> the widget was shown, then closed, exit code 4
# DF03 simulated 96 -> 144 dpi: widget and input window 1.5x (+-2 px), still alive
# DF04 back to 96 dpi: the original sizes again, still running
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "..\lib\Check.ps1")
Start-Checks -Required @("DF01", "DF02", "DF03", "DF04")
if (-not $Exe) { $Exe = Get-DefaultExe }
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices; using System.Collections.Generic;
public class T7 { public delegate bool CB(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] static extern bool EnumWindows(CB cb, IntPtr l);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, ref RECT l);
 [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
 [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
 [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, CB cb, IntPtr l);
 public static IntPtr Child(IntPtr p){ IntPtr f=IntPtr.Zero; EnumChildWindows(p,(h,l)=>{var sb=new StringBuilder(64); GetClassNameW(h,sb,64); if(sb.ToString()=="Edit"){f=h; return false;} return true;},IntPtr.Zero); return f; }
 public static List<IntPtr> All(uint pid, string cls){ var r=new List<IntPtr>(); EnumWindows((h,l)=>{uint p;GetWindowThreadProcessId(h,out p); var sb=new StringBuilder(64); GetClassNameW(h,sb,64); if(p==pid && sb.ToString()==cls) r.Add(h); return true;},IntPtr.Zero); return r; } }
'@
$env:ONEKEY_TEST = "1"
function Rect($h) { $r = New-Object T7+RECT; [void][T7]::GetWindowRect($h, [ref]$r); $r }
function Shot($r, $path) { $b = New-Object Drawing.Bitmap ($r.R-$r.L), ($r.B-$r.T); $g = [Drawing.Graphics]::FromImage($b); $g.CopyFromScreen($r.L, $r.T, 0, 0, $b.Size); $g.Dispose(); $b.Save($path); $b.Dispose() }
function Near($a, $b) { [Math]::Abs($a - $b) -le 2 }
try {
  $env:ONEKEY_TEST = "1"
  $q = Start-Process $Exe -ArgumentList "--lockwidget-demo","fail=1" -PassThru
  $done = $q.WaitForExit(8000)
  Check DF01 "True|3" "$done|$(if ($done) { $q.ExitCode } else { 'timeout' })" "failure at the first render: exit 3, no widget"
  if (-not $q.HasExited) { Stop-Process -Id $q.Id -Force }

  $q = Start-Process $Exe -ArgumentList "--lockwidget-demo","fail=20" -PassThru; Start-Sleep -Milliseconds 800
  $seen = @([T7]::All([uint32]$q.Id, "OneKeyLockWidget")).Count
  # since 0.2.146 the widget opens narrow (no greeting renders): "start typing" with a posted End key so it widens and renders
  $host1 = @([T7]::All([uint32]$q.Id, "OneKeyLockInput"))[0]
  if ($host1) { $ed = [T7]::Child($host1); if ($ed -ne [IntPtr]::Zero) { [void][T7]::PostMessageW($ed, 0x0100, [IntPtr]0x23, [IntPtr]0x014F0001) } }
  $done = $q.WaitForExit(15000)
  Check DF02 "1|True|4" "$seen|$done|$(if ($done) { $q.ExitCode } else { 'timeout' })" "failure while shown: shown first, then closed with exit 4"
  if (-not $q.HasExited) { Stop-Process -Id $q.Id -Force }

  $p = Start-Process $Exe -ArgumentList "--lockwidget-demo" -PassThru; Start-Sleep -Milliseconds 1500
  $w = @([T7]::All([uint32]$p.Id, "OneKeyLockWidget"))[0]; $h = @([T7]::All([uint32]$p.Id, "OneKeyLockInput"))[0]
  $r0 = Rect $w; $h0 = Rect $h
  $s = New-Object T7+RECT; $s.L = 300; $s.T = 80; $s.R = 300 + 570; $s.B = 80 + 500
  [void][T7]::SendMessageW($w, 0x02E0, [IntPtr](144 + (144 -shl 16)), [ref]$s); Start-Sleep -Milliseconds 600
  $r1 = Rect $w; $h1 = Rect $h
  $ok3 = (Near ($r1.R - $r1.L) (1.5 * ($r0.R - $r0.L))) -and (Near ($r1.B - $r1.T) (1.5 * ($r0.B - $r0.T))) -and (Near ($h1.R - $h1.L) (1.5 * ($h0.R - $h0.L))) -and (Near ($h1.B - $h1.T) (1.5 * ($h0.B - $h0.T)))
  Check DF03 "True|True" "$ok3|$([T7]::IsWindow($w))" ("96->144: widget {0}x{1} -> {2}x{3}, input {4}x{5} -> {6}x{7}" -f ($r0.R-$r0.L), ($r0.B-$r0.T), ($r1.R-$r1.L), ($r1.B-$r1.T), ($h0.R-$h0.L), ($h0.B-$h0.T), ($h1.R-$h1.L), ($h1.B-$h1.T))
  Shot $r1 "$env:TEMP\onekey_lockwidget_dpi144.png"
  $s.L = $r0.L; $s.T = $r0.T; $s.R = $r0.R; $s.B = $r0.B
  [void][T7]::SendMessageW($w, 0x02E0, [IntPtr](96 + (96 -shl 16)), [ref]$s); Start-Sleep -Milliseconds 600
  $r2 = Rect $w; $h2 = Rect $h
  $ok4 = ($r2.R - $r2.L) -eq ($r0.R - $r0.L) -and ($r2.B - $r2.T) -eq ($r0.B - $r0.T) -and ($h2.R - $h2.L) -eq ($h0.R - $h0.L) -and ($h2.B - $h2.T) -eq ($h0.B - $h0.T)
  Check DF04 "True|True" "$ok4|$(-not $p.HasExited)" "back to 96: original sizes, still running"
  [void][T7]::PostMessageW($w, 0x0112, [IntPtr]0xF060, [IntPtr]::Zero); Start-Sleep -Milliseconds 700
}
catch { Add-Failure ("aborted: " + $_.Exception.Message) }
finally { foreach ($x in @($q, $p)) { if ($x -and -not $x.HasExited) { Stop-Process -Id $x.Id -Force -ErrorAction Ignore } } }
Complete-Checks
