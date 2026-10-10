# Taskbar cat walking (0.5.15-D, user 2026-10-09: "a cat that walks around with various motions, like the in-house edition").
# 1Key in test mode with ONEKEY_TEST_CAT_WALK=1 only walks (0.6 s apart, at most 2 walk cycles each) and writes a per-tick log:
#   "walkplan dir D cycles C dx X from F min M home H", "walksteps art:cell[m]:dx,...", "clipstart walk steps N ...",
#   "tick dt .. want 62 work .. walk <i> s<i> c<art>:<cell>[m] x<window left> ..."
# The 1Key theme is set to light (the walk art is the grey cat; the theme decides the cat colour since 0.5.15-C).
# Own config folder and instance suffix; the real 1Key is not touched. The cat walks on the taskbar: run it while nobody uses the PC.
#   powershell -ExecutionPolicy Bypass -File tools\tests\catwalk.ps1 [-Exe <1Key.exe>] [-Walks 6] [-MaxMinutes 6]
#   powershell -ExecutionPolicy Bypass -File tools\tests\catwalk.ps1 -AnalyzeOnly <timing.log>
# CW00 environment: taskbar at the bottom, cat shown, -Walks walks logged
# CW01 every planned step is shown on exactly one tick, in order (turn out, walk cycles, turn back), then the front pose
# CW02 while walking the window moves every tick by the planned step (+-1 px) in the walk direction; turning does not move it
# CW03 each walk starts where the last one ended, inside the range (right fifth of the taskbar), and moves the cat
# CW04 left walks are mirrored, right walks are not; both directions happen (judged from 5 complete walks)
# CW05 late ticks: none   CW06 99 % of intervals within +-2 ms of 46.875 ms   CW07 work per tick at most 16 ms, p99 at most 8 ms
param([string]$Exe = "", [int]$Walks = 6, [int]$MaxMinutes = 6, [string]$AnalyzeOnly = "")
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
$ids = @(); 0..7 | ForEach-Object { $ids += "CW{0:D2}" -f $_ }
Start-Checks -Required $ids

function Analyze([string]$log, [bool]$shown) {
  $reTick = '^(\S+) tick dt (\S+) want (\d+) work (\S+) (.+?)( LATE)? n(\d+) push (\d+) fail (\d+) h ([0-9a-f]{8})$'
  $wlist = New-Object System.Collections.Generic.List[object]; $cur = $null; $all = New-Object System.Collections.Generic.List[object]
  foreach ($l in [IO.File]::ReadAllLines($log)) {
    if ($l -match ' walkplan dir (-?\d+) cycles (\d+) dx (\S+) from (-?\d+) min (-?\d+) home (-?\d+)') {
      $cur = [pscustomobject]@{ dir = [int]$Matches[1]; cycles = [int]$Matches[2]; dx = [double]$Matches[3]; from = [int]$Matches[4]; min = [int]$Matches[5]; home = [int]$Matches[6]; plan = @(); ticks = New-Object System.Collections.Generic.List[object]; ended = $false }
      $wlist.Add($cur)
    }
    elseif ($l -match ' walksteps (\S+)$' -and $cur) { $cur.plan = @($Matches[1].Split(',') | ForEach-Object { $p = $_.Split(':'); [pscustomobject]@{ art = [int]$p[0]; cell = $p[1]; dx = [double]$p[2] } }) }
    elseif ($l -match $reTick) {
      $t = [pscustomobject]@{ dt = [double]$Matches[2]; work = [double]$Matches[4]; what = $Matches[5]; late = [bool]$Matches[6]; push = [int]$Matches[8]; fail = [int]$Matches[9] }
      $all.Add($t)
      if ($cur -and -not $cur.ended) { if ($t.what -eq 'S') { $cur.ended = $true } else { $cur.ticks.Add($t) } }
    }
  }
  $done = @($wlist | Where-Object { $_.ended })
  "log: $log - $($all.Count) ticks, $($wlist.Count) walks ($($done.Count) complete)"
  Check CW00 "True|True" "$shown|$($done.Count -ge [Math]::Min($Walks, 3))" "environment, cat shown, complete walks $($done.Count)"
  $bad1 = @(); $bad2 = @(); $bad3 = @(); $mirBad = @(); $dirs = @{}
  for ($w = 0; $w -lt $done.Count; $w++) {
    $k = $done[$w]
    $seen = @($k.ticks | ForEach-Object { if ($_.what -match '^walk (\d+) s\d+ c(\d+):(\d+m?) x(-?\d+)$') { [pscustomobject]@{ i = [int]$Matches[1]; art = [int]$Matches[2]; cell = $Matches[3]; x = [int]$Matches[4] } } })
    $ok = $seen.Count -eq $k.plan.Count
    for ($q = 0; $q -lt $seen.Count -and $ok; $q++) { if ($seen[$q].i -ne $q -or $seen[$q].art -ne $k.plan[$q].art -or $seen[$q].cell -ne $k.plan[$q].cell) { $ok = $false; $bad1 += "walk ${w} step ${q}: got $($seen[$q].art):$($seen[$q].cell) want $($k.plan[$q].art):$($k.plan[$q].cell)" } }
    if ($seen.Count -ne $k.plan.Count) { $bad1 += "walk ${w}: $($seen.Count)/$($k.plan.Count) steps" }
    # movement: consecutive steps of the same art: x changes by the planned dx change (+-1 px, the window is clamped to the work area)
    for ($q = 1; $q -lt [Math]::Min($seen.Count, $k.plan.Count); $q++) {
      if ($seen[$q].art -ne $seen[$q - 1].art) { continue }
      $want = [Math]::Round($k.plan[$q].dx) - [Math]::Round($k.plan[$q - 1].dx); $got = $seen[$q].x - $seen[$q - 1].x
      if ([Math]::Abs($got - $want) -gt 1) { $bad2 += "walk ${w} step ${q}: x moved $got want $want"; break }
    }
    $mir = @($seen | Where-Object { $_.cell -like '*m' }).Count
    if (($k.dir -lt 0 -and $mir -ne $seen.Count) -or ($k.dir -gt 0 -and $mir -ne 0)) { $mirBad += "walk ${w} dir $($k.dir): mirrored $mir of $($seen.Count)" }
    $dirs[$k.dir] = $true
    $end = $k.from + [Math]::Round($k.plan[-1].dx)
    if ($end -lt $k.min - 1 -or $end -gt $k.home + 1 -or [Math]::Abs($end - $k.from) -lt 5) { $bad3 += "walk ${w} from $($k.from) to $end outside $($k.min)..$($k.home) or not moved" }
    if ($w + 1 -lt $wlist.Count -and [Math]::Abs($wlist[$w + 1].from - [Math]::Max($k.min, [Math]::Min($k.home, $end))) -gt 1) { $bad3 += "walk $($w + 1) starts at $($wlist[$w + 1].from), last ended at $end" }
  }
  Check CW01 "0" "$($bad1.Count)" ("$($done.Count) walks: every planned step on exactly one tick, in order" + $(if ($bad1) { " - " + (($bad1 | Select-Object -First 4) -join ' | ') }))
  Check CW02 "0" "$($bad2.Count)" ("window moves by the planned step while walking, stays while turning" + $(if ($bad2) { " - " + (($bad2 | Select-Object -First 4) -join ' | ') }))
  Check CW03 "0" "$($bad3.Count)" ("walks chain inside the range and move the cat ($(($done | ForEach-Object { "$($_.from)->$($_.from + [Math]::Round($_.plan[-1].dx))" }) -join ' '))" + $(if ($bad3) { " - " + (($bad3 | Select-Object -First 4) -join ' | ') }))
  Check CW04 "0|True" "$($mirBad.Count)|$(($dirs.ContainsKey(1) -and $dirs.ContainsKey(-1)) -or $done.Count -lt 5)" ("mirroring by direction; directions seen: $(($dirs.Keys | Sort-Object) -join ',')" + $(if ($mirBad) { " - " + ($mirBad -join ' | ') }))
  $late = @($all | Where-Object { $_.late })
  Check CW05 "0" "$($late.Count)" "late ticks $(($late | Select-Object -First 5 | ForEach-Object { "$($_.dt) $($_.what)" }) -join ', ')"
  $inner = @($done | ForEach-Object { $_.ticks | Select-Object -Skip 1 } | ForEach-Object { [math]::Abs($_.dt - 46.875) })
  $p = if ($inner.Count) { [math]::Round(100.0 * @($inner | Where-Object { $_ -le 2 }).Count / $inner.Count, 2) } else { 0 }
  Check CW06 "True" "$($p -ge 99)" "intervals within +-2 ms of 46.875: $p % of $($inner.Count)"
  $wk = @($all | ForEach-Object { $_.work } | Sort-Object); $wmax = if ($wk.Count) { $wk[-1] } else { 0 }; $w99 = if ($wk.Count) { $wk[[int][math]::Floor(($wk.Count - 1) * 0.99)] } else { 0 }
  Check CW07 "True" "$($wmax -le 16 -and $w99 -le 8)" "work per tick: max $wmax ms, p99 $w99 ms"
}

if ($AnalyzeOnly) { Analyze $AnalyzeOnly $true; Complete-Checks; return }

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$suffix = ".cw"
$root = Join-Path $sp ("catwalk-{0:yyyyMMdd-HHmmss}-{1}" -f (Get-Date), $PID); New-Item -ItemType Directory $root | Out-Null
$cfg = "$root\cfg"; New-Item -ItemType Directory $cfg | Out-Null
$log = "$root\timing.log"
$master = "Cw-Dummy-3318"
Add-Type @'
using System;using System.Runtime.InteropServices;using System.Text;
public class CWU {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct ABD { public uint cbSize; public IntPtr hWnd; public uint cb, uEdge; public RECT rc; public IntPtr lParam; }
  [DllImport("shell32.dll")] public static extern UIntPtr SHAppBarMessage(uint msg, ref ABD d);
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static IntPtr Find(uint pid, string cls, bool vis) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls && (!vis || IsWindowVisible(h))) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
}
'@
function Quit1Key() { if ($script:p -and -not $script:p.HasExited) { [void][CWU]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i = 0; $i -lt 50 -and -not $script:p.HasExited; $i++) { Start-Sleep -Milliseconds 100 }; if (-not $script:p.HasExited) { Stop-Process -Id $script:p.Id -Force } } }
function Click($id, $ms = 700) { [void][CWU]::PostMessageW([CWU]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds $ms }
function SetText($id, $s) { [void][CWU]::SendMessageW([CWU]::GetDlgItem($m, $id), 0x000C, [IntPtr]::Zero, [string]$s) }

$shown = $false
try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_LOCKWIDGET = "0"; $env:ONEKEY_TEST_NO_AUTOLOCK = "1"
  $env:ONEKEY_TEST_CAT_WALK = "1"; $env:ONEKEY_TEST_CAT_LOG = $log
  $abd = New-Object CWU+ABD; $abd.cbSize = [Runtime.InteropServices.Marshal]::SizeOf([type][CWU+ABD]); [void][CWU]::SHAppBarMessage(5, [ref]$abd)
  $st = New-Object CWU+ABD; $st.cbSize = $abd.cbSize; $auto = ([uint64][CWU]::SHAppBarMessage(4, [ref]$st)) -band 1
  $envOk = ($abd.uEdge -eq 3) -and ($auto -eq 0)
  $script:p = Start-Process $exe -PassThru; $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $script:m = [CWU]::Find([uint32]$p.Id, "OneKeyMainWindow$suffix", $false); if ($m -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 100 }
  if ($m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 900
  SetText 101 $master; SetText 102 $master; Click 103 1500
  Click 220
  if ([int][CWU]::SendMessageW([CWU]::GetDlgItem($m, 2032), 0x00F0, [IntPtr]::Zero, [IntPtr]::Zero) -ne 1) { [void][CWU]::PostMessageW([CWU]::GetDlgItem($m, 2032), 0x0100, [IntPtr]0x20, [IntPtr]::Zero); Start-Sleep -Milliseconds 300 }
  [void][CWU]::PostMessageW([CWU]::GetDlgItem($m, 2008), 0x0100, [IntPtr]0x28, [IntPtr]::Zero); Start-Sleep -Milliseconds 500   # theme: follow -> light
  Click 2012 900
  [void][CWU]::PostMessageW($m, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
  $t0 = [Environment]::TickCount; while ([Environment]::TickCount - $t0 -lt 5000 -and [CWU]::Find([uint32]$p.Id, "OneKeyCat", $true) -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 100 }
  $shown = $envOk -and ([CWU]::Find([uint32]$p.Id, "OneKeyCat", $true) -ne [IntPtr]::Zero)
  if (-not $envOk) { "environment: taskbar edge $($abd.uEdge) (3 = bottom), auto-hide $auto" }
  $deadline = (Get-Date).AddMinutes($MaxMinutes)
  $cpu0 = (Get-Process -Id $p.Id).TotalProcessorTime.TotalMilliseconds; $wall0 = [Environment]::TickCount; $mem = 0
  while ((Get-Date) -lt $deadline -and $shown) {
    Start-Sleep -Seconds 3
    try { $mem = [Math]::Max($mem, (Get-Process -Id $p.Id).PrivateMemorySize64) } catch { }
    if (Test-Path $log) { if (@(Select-String -Path $log -Pattern ' walkplan ' -SimpleMatch).Count -ge $Walks + 1) { break } }
    if ($p.HasExited) { "1Key exited"; break }
  }
  try { $cpu = ((Get-Process -Id $p.Id).TotalProcessorTime.TotalMilliseconds - $cpu0) / ([Environment]::TickCount - $wall0) * 100; "measured (not judged): CPU {0:N2} % of one core while walking, private memory peak {1:N1} MB" -f $cpu, ($mem / 1MB) } catch { }
  Start-Sleep -Seconds 2
  Quit1Key; Start-Sleep -Milliseconds 500
  if (Test-Path $log) { Analyze $log $shown } else { Check CW00 "True" "False" "no log written ($log)" }
} catch { Add-Failure ("exception: " + $_) }
finally {
  Quit1Key; Stop-TestInstances $suffix
  foreach ($v in "ONEKEY_TEST_CAT_WALK", "ONEKEY_TEST_CAT_LOG", "ONEKEY_TEST_LOCKWIDGET", "ONEKEY_TEST_NO_AUTOLOCK") { Remove-Item "env:$v" -ErrorAction Ignore }
  "log kept: $log"
}
Complete-Checks
