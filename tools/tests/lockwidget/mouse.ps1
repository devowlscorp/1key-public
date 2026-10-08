param([string]$Exe = "$PSScriptRoot\..\..\..\src\OneKey\bin\Release\net8.0-windows\win-x64\1Key.exe")
# Lock widget spike checks (1Key --lockwidget-demo, test mode). A test form sits behind the widget and counts clicks.
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices; using System.Collections.Generic;
public class T2 { public delegate bool CB(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] static extern bool EnumWindows(CB cb, IntPtr l);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, UIntPtr e);
 [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
 public static void Click(int x, int y){ SetCursorPos(x,y); System.Threading.Thread.Sleep(80); mouse_event(2,0,0,0,UIntPtr.Zero); System.Threading.Thread.Sleep(60); mouse_event(4,0,0,0,UIntPtr.Zero); }
 public static void Drag(int x, int y, int dx, int dy){ SetCursorPos(x,y); System.Threading.Thread.Sleep(100); mouse_event(2,0,0,0,UIntPtr.Zero); for(int i=1;i<=10;i++){ System.Threading.Thread.Sleep(20); SetCursorPos(x+dx*i/10,y+dy*i/10);} System.Threading.Thread.Sleep(100); mouse_event(4,0,0,0,UIntPtr.Zero); }
 public static List<IntPtr> All(uint pid, string cls){ var r=new List<IntPtr>(); EnumWindows((h,l)=>{uint p;GetWindowThreadProcessId(h,out p); var sb=new StringBuilder(64); GetClassNameW(h,sb,64); if(p==pid && sb.ToString()==cls) r.Add(h); return true;},IntPtr.Zero); return r; } }
'@
function Rect($h) { $r = New-Object T2+RECT; [void][T2]::GetWindowRect($h, [ref]$r); $r }
function Snap($r, $x, $y, $w, $h) { $b = New-Object Drawing.Bitmap $w, $h; $g = [Drawing.Graphics]::FromImage($b); $g.CopyFromScreen($r.L + $x, $r.T + $y, 0, 0, $b.Size); $g.Dispose(); $b }
function Same($a, $b) { $d = 0; for ($y = 0; $y -lt $a.Height; $y += 3) { for ($x = 0; $x -lt $a.Width; $x += 3) { if ($a.GetPixel($x,$y) -ne $b.GetPixel($x,$y)) { $d++ } } }; $d }
$results = @()
function Check($name, $ok, $info) { $script:results += "{0} {1}  {2}" -f $(if ($ok) { "ok  " } else { "FAIL" }), $name, $info }

# background test form (behind the widget)
$form = New-Object Windows.Forms.Form; $form.Text = "lwbg"; $form.StartPosition = "Manual"; $form.Left = 700; $form.Top = 400; $form.Width = 100; $form.Height = 60; $form.FormBorderStyle = "None"   # overlaps only the widget's transparent left edge
$script:clicks = 0; $form.Add_MouseDown({ $script:clicks++ }); $form.BackColor = [Drawing.Color]::FromArgb(240, 240, 240)
$form.Show(); [Windows.Forms.Application]::DoEvents()

$env:ONEKEY_TEST = "1"
$p = Start-Process $Exe -ArgumentList "--lockwidget-demo" -PassThru
Start-Sleep -Milliseconds 1500; [Windows.Forms.Application]::DoEvents()
$w = @([T2]::All([uint32]$p.Id, "OneKeyLockWidget"))[0]
$r = Rect $w; $s = ($r.R - $r.L) / 380.0
"widget $($r.L),$($r.T) scale $s"
function P($lx, $ly) { @([int]($r.L + $lx * $s), [int]($r.T + $ly * $s)) }

# 1) click-through on a transparent corner (logical 10,300) -> the form gets the click
$c0 = $script:clicks; $pt = P 10 300; [T2]::Click($pt[0], $pt[1]); Start-Sleep -Milliseconds 300; [Windows.Forms.Application]::DoEvents()
Check "click-through (transparent area -> window behind)" ($script:clicks -gt $c0) "form clicks +$($script:clicks - $c0)"
Start-Sleep -Milliseconds 600
# after the form took focus the widget should be narrow: hosts are narrower
$h = @([T2]::All([uint32]$p.Id, "OneKeyLockInput"))[0]; $hr = Rect $h
$narrowW = $hr.R - $hr.L
# 2) click the pill (centre) -> focus goes to the widget input, pill widens
$pt = P 190 ((32 + 140 - 2) + 22); [T2]::Click($pt[0], $pt[1]); Start-Sleep -Milliseconds 700
$fg = [T2]::GetForegroundWindow(); $hr2 = Rect $h; $wideW = $hr2.R - $hr2.L
Check "pill click -> input focused and wider" (($fg -eq $h -or $fg -eq $w) -and $wideW -gt $narrowW + 40) "host width $narrowW -> $wideW, fg host=$($fg -eq $h)"
# 3) greeting frames change while expanded
$r = Rect $w; $a = Snap $r ([int](145*$s)) ([int](32*$s)) ([int](90*$s)) ([int](140*$s)); Start-Sleep -Milliseconds 350; $b = Snap $r ([int](145*$s)) ([int](32*$s)) ([int](90*$s)) ([int](140*$s))
$diff = Same $a $b; Check "greeting animates while expanded" ($diff -gt 20) "changed samples $diff"
# 4) CPU expanded vs narrow
$pp = Get-Process -Id $p.Id; $t0 = $pp.TotalProcessorTime.TotalMilliseconds; Start-Sleep -Seconds 4; $pp.Refresh(); $cpuWide = ($pp.TotalProcessorTime.TotalMilliseconds - $t0) / 4000 * 100
$form.Activate(); [void][T2]::SetForegroundWindow($form.Handle); Start-Sleep -Milliseconds 800; [Windows.Forms.Application]::DoEvents()
$r = Rect $w; $a = Snap $r ([int](145*$s)) ([int](32*$s)) ([int](90*$s)) ([int](140*$s)); Start-Sleep -Milliseconds 350; $b = Snap $r ([int](145*$s)) ([int](32*$s)) ([int](90*$s)) ([int](140*$s))
$diffN = Same $a $b
$pp.Refresh(); $t0 = $pp.TotalProcessorTime.TotalMilliseconds; Start-Sleep -Seconds 4; $pp.Refresh(); $cpuNarrow = ($pp.TotalProcessorTime.TotalMilliseconds - $t0) / 4000 * 100
Check "narrow: greeting stopped" ($diffN -le 2) "changed samples $diffN"
Check "CPU narrow ~0" ($cpuNarrow -lt 0.3) ("narrow {0:N2}% / expanded {1:N2}% of one core" -f $cpuNarrow, $cpuWide)
# 5) drag the mascot 60px right
$r1 = Rect $w; $pt = P 190 110; [T2]::Drag($pt[0], $pt[1], 60, 0); Start-Sleep -Milliseconds 400; $r2 = Rect $w; $hr3 = Rect $h
Check "drag mascot moves widget (and input follows)" (($r2.L - $r1.L) -ge 50 -and ($hr3.L - $hr2.L) -ge 40) "widget dx $($r2.L - $r1.L), host dx $($hr3.L - $hr2.L)"
$r = $r2
# 6) minimize with the − button (logical x = 190-13, y = 13)
$pt = P 160 15;   # since 0.3.124 the widget-mode button sits between - and x: - is at 190-26-4 (26 px buttons since 0.5.6) [T2]::Click($pt[0], $pt[1]); Start-Sleep -Milliseconds 700
Check "− minimizes" ([T2]::IsIconic($w)) "iconic=$([T2]::IsIconic($w))"
[void][T2]::ShowWindow($w, 9); Start-Sleep -Milliseconds 600
# 7) close via SC_CLOSE -> demo exits
[void][T2]::PostMessageW($w, 0x0112, [IntPtr]0xF060, [IntPtr]::Zero); Start-Sleep -Milliseconds 800
Check "close -> exits" ($p.HasExited) ""
if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
$form.Close()
$results
# same record as tools\tests\lib\Check.ps1 (Codex 2026-10-04 06:41): which exe, and a judgement line + exit code
if (Test-Path $Exe) { "---- exe: $Exe  version " + (Get-Item $Exe).VersionInfo.FileVersion + "  SHA-256 " + (Get-FileHash $Exe -Algorithm SHA256).Hash }
$nFail = @($results | Where-Object { "$_" -like "FAIL*" }).Count; $nAll = @($results).Count
"---- judgement: $($nAll - $nFail)/$nAll checks passed, $nFail failed"
if ($nFail -gt 0 -or $nAll -eq 0) { exit 1 } else { exit 0 }
