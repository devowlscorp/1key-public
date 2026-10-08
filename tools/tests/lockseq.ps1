param(
  [string]$Exe = "$PSScriptRoot\..\..\src\OneKey\bin\Release\net8.0-windows\win-x64\publish\1Key.exe",
  [int]$Laps = 2,
  [string]$Theme = "",          # light | dark | "" (both)
  [string]$AnalyzeOnly = ""     # judge an existing log only
)
# Lock widget cat motions (0.5.15-A): frame-by-frame sequence/timing check, the same way as catseq.ps1 (taskbar cat).
# The demo lock widget (--lockwidget-demo, test mode) with ONEKEY_TEST_LOCK_SEQ=1 plays its wide-state motions in a fixed order
# (chain -> blink -> blep -> ears, 600 ms rests) and logs every tick:
#   "clipstart <name> steps N cells a,b,..."  /  "tick dt X work Y cell C step S act A"
# LS00 log + motions present   LS01 order   LS02 every step exactly once, in order, then back to cell 0
# LS03 no late tick (> 1.5 periods)   LS04 interval within +-2 ms for 99 %   LS05 work <= 16 ms, p99 <= 8 ms
# Window: the widget comes to the front (it needs focus to widen). Run when the user is away.
$ErrorActionPreference = "Stop"
$results = @()
function Check($id, $ok, $info) { $script:results += "{0} {1} {2}" -f $(if ($ok) { "ok  " } else { "FAIL" }), $id, $info }

Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices; using System.Collections.Generic;
public class LS { public delegate bool CB(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] static extern bool EnumWindows(CB cb, IntPtr l);
 [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, CB cb, IntPtr l);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
 [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
 public static bool Front(IntPtr h) { keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero); return SetForegroundWindow(h); }
 static string Cls(IntPtr h){ var sb=new StringBuilder(64); GetClassNameW(h,sb,64); return sb.ToString(); }
 public static IntPtr Top(uint pid, string cls){ IntPtr r=IntPtr.Zero; EnumWindows((h,l)=>{uint p;GetWindowThreadProcessId(h,out p); if(p==pid && Cls(h)==cls){ r=h; return false;} return true;},IntPtr.Zero); return r; }
 public static IntPtr Child(IntPtr parent, string cls){ IntPtr r=IntPtr.Zero; EnumChildWindows(parent,(h,l)=>{ if(Cls(h)==cls){ r=h; return false;} return true;},IntPtr.Zero); return r; } }
'@

function Judge($log, $tag) {
  $lines = @(Get-Content $log)
  $clips = @(); $cur = $null
  foreach ($l in $lines) {
    if ($l -match 'clipstart (\w+) steps (\d+) cells ([\d,]+)') { $cur = [pscustomobject]@{ Name = $Matches[1]; Steps = [int]$Matches[2]; Cells = @($Matches[3].Split(',') | ForEach-Object { [int]$_ }); Ticks = New-Object System.Collections.ArrayList }; $clips += $cur }
    elseif ($l -match 'tick dt ([\d.]+) work ([\d.]+) cell (\d+) step (\d+) act (\w+)' -and $cur) { [void]$cur.Ticks.Add([pscustomobject]@{ Dt = [double]$Matches[1]; Work = [double]$Matches[2]; Cell = [int]$Matches[3]; Step = [int]$Matches[4] }) }
  }
  $names = @($clips | ForEach-Object Name)
  Check "LS00[$tag]" ($clips.Count -ge 4 -and ($names -contains 'blink') -and ($names -contains 'blep') -and ($names -contains 'ears')) "clips $($clips.Count): $(($clips | ForEach-Object { "$($_.Name)/$($_.Steps)" }) -join ' ')"
  $order = @('chain', 'blink', 'blep', 'ears'); $badOrder = 0
  for ($i = 0; $i -lt $clips.Count; $i++) { if ($clips[$i].Name -ne $order[$i % 4]) { $badOrder++ } }
  Check "LS01[$tag]" ($badOrder -eq 0) "order mismatches $badOrder"
  # the last clip may be cut off by closing the widget: judge only complete ones
  $bad = @(); $judged = 0
  foreach ($c in $clips) {
    $want = @($c.Cells[1..($c.Steps - 1)]) + @(0)
    $got = @($c.Ticks | ForEach-Object Cell)
    if ($got.Count -lt $want.Count) { if ($c -ne $clips[-1]) { $bad += "$($c.Name): $($got.Count)/$($want.Count) ticks" }; continue }
    $judged++
    for ($k = 0; $k -lt $want.Count; $k++) { if ($got[$k] -ne $want[$k]) { $bad += "$($c.Name) step $($k + 1): cell $($got[$k]) want $($want[$k])"; break } }
    if ($got.Count -gt $want.Count) { $bad += "$($c.Name): $($got.Count - $want.Count) extra ticks" }
  }
  Check "LS02[$tag]" ($bad.Count -eq 0 -and $judged -ge 4) "complete clips $judged; $(if ($bad) { $bad -join '; ' } else { 'every step once, in order, then cell 0' })"
  $dts = @($clips | ForEach-Object { $_.Ticks | Select-Object -Skip 1 } | ForEach-Object Dt | Where-Object { $_ -gt 0 })
  $late = @($dts | Where-Object { $_ -gt 62.5 * 1.5 })
  Check "LS03[$tag]" ($late.Count -eq 0 -and $dts.Count -gt 50) "intervals $($dts.Count), late $($late.Count)$(if ($late) { ' max ' + ($late | Measure-Object -Maximum).Maximum })"
  $near = @($dts | Where-Object { [math]::Abs($_ - 62.5) -le 2 })
  $pct = if ($dts.Count) { 100.0 * $near.Count / $dts.Count } else { 0 }
  $mm = $dts | Measure-Object -Minimum -Maximum -Average
  Check "LS04[$tag]" ($pct -ge 99) ("+-2 ms {0:0.0} %  min {1:0.0} avg {2:0.00} max {3:0.0}" -f $pct, $mm.Minimum, $mm.Average, $mm.Maximum)
  $works = @($clips | ForEach-Object { $_.Ticks } | ForEach-Object Work | Sort-Object)
  $p99 = if ($works.Count) { $works[[int][math]::Floor(($works.Count - 1) * 0.99)] } else { 0 }
  $wmax = ($works | Measure-Object -Maximum).Maximum
  Check "LS05[$tag]" ($wmax -le 16 -and $p99 -le 8) ("work max {0:0.00} ms p99 {1:0.00} ms (n {2})" -f $wmax, $p99, $works.Count)
}

if ($AnalyzeOnly) { Judge $AnalyzeOnly "log" }
else {
  if (-not (Test-Path $Exe)) { throw "exe missing: $Exe" }
  $themes = if ($Theme) { @($Theme) } else { @('light', 'dark') }
  $lapMs = 12500
  foreach ($th in $themes) {
    $log = Join-Path $env:TEMP "1Key-lockseq-$th.log"; Remove-Item $log -ErrorAction SilentlyContinue
    $env:ONEKEY_TEST = "1"; $env:ONEKEY_TEST_LOCK_SEQ = "1"; $env:ONEKEY_TEST_LOCK_LOG = $log
    $env:ONEKEY_CONFIG_DIR = Join-Path $env:TEMP "1Key-lockseq-cfg"; $env:ONEKEY_INSTANCE_SUFFIX = ".lockseq"
    $p = Start-Process $Exe -ArgumentList "--lockwidget-demo $th" -PassThru
    $w = [IntPtr]::Zero
    for ($i = 0; $i -lt 40 -and $w -eq [IntPtr]::Zero; $i++) { Start-Sleep -Milliseconds 150; $w = [LS]::Top([uint32]$p.Id, "OneKeyLockWidget") }
    if ($w -eq [IntPtr]::Zero) { Check "LS00[$th]" $false "no widget"; continue }
    $host_ = [LS]::Top([uint32]$p.Id, "OneKeyLockInput"); $edit = [LS]::Child($host_, "Edit")
    [void][LS]::Front($w); Start-Sleep -Milliseconds 400
    [void][LS]::PostMessageW($edit, 0x0100, [IntPtr]0x23, [IntPtr]0x014F0001)   # WM_KEYDOWN End: "start typing" -> widens (no character)
    $pp = Get-Process -Id $p.Id
    Start-Sleep -Milliseconds 800; $pp.Refresh(); $c0 = $pp.TotalProcessorTime.TotalMilliseconds; $t0 = [Environment]::TickCount
    $peak = 0
    $end = [Environment]::TickCount + $lapMs * $Laps
    while ([Environment]::TickCount -lt $end) { Start-Sleep -Milliseconds 500; $pp.Refresh(); $peak = [math]::Max($peak, $pp.WorkingSet64) }
    $pp.Refresh(); $cpu = ($pp.TotalProcessorTime.TotalMilliseconds - $c0) / ([Environment]::TickCount - $t0) * 100
    [void][LS]::PostMessageW($w, 0x0010, [IntPtr]0, [IntPtr]0)   # WM_CLOSE -> demo quits (log flushed on WM_DESTROY)
    if (-not $p.WaitForExit(5000)) { Stop-Process -Id $p.Id -Force }
    Remove-Item Env:ONEKEY_TEST_LOCK_SEQ, Env:ONEKEY_TEST_LOCK_LOG
    $script:results += ("info [{0}] CPU {1:0.00} % of one core while playing, memory peak {2:0.0} MB" -f $th, $cpu, ($peak / 1MB))
    if (-not (Test-Path $log)) { Check "LS00[$th]" $false "no log"; continue }
    Judge $log $th
  }
}
$results
$fail = @($results | Where-Object { $_ -like 'FAIL*' }).Count
"lockseq: $(@($results | Where-Object { $_ -like 'ok*' }).Count) ok, $fail fail"
exit $fail
