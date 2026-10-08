# Taskbar cat motion sequence and timing check (0.5.15, user 2026-10-09: "join every motion like the in-house edition, check every
# frame, then test like 0.3.131-A..L that no frame is missing or late"). 1Key plays every motion of Assets\cat\catclips.txt in list
# order in test mode and writes a per-tick log; this script judges the log only (no screen picture):
#   ONEKEY_TEST=1 ONEKEY_TEST_CAT_SEQ=1 (list order, 0.6 s apart) ONEKEY_TEST_CAT_LOG=<file>
# Own config folder and instance suffix; the real 1Key is not touched. The cat appears on the taskbar edge: run it while nobody uses
# the PC.
#   powershell -ExecutionPolicy Bypass -File tools\tests\catseq.ps1 [-Exe <1Key.exe>] [-Laps 2] [-MaxMinutes 10]
#   powershell -ExecutionPolicy Bypass -File tools\tests\catseq.ps1 -AnalyzeOnly <timing.log>
# CS00 environment: taskbar at the bottom, not auto-hidden; widget mode on and the window closed shows the cat; the log has ticks
# CS01 order: every motion of catclips.txt plays, in list order, -Laps times
# CS02 frames: every frame of every motion is shown on exactly one tick, in order (+1 per tick), to the last frame; then the front pose
# CS03 pushes: every tick pushes the picture once, none fails; the picture hash changes from frame to frame (at most 2 % repeats)
# CS04 late ticks: none (interval over the wanted 62 ms by more than 31 ms)
# CS05 tick accuracy: 99 % of the intervals inside a motion within +-2 ms of 62.5 ms (the first tick of each motion is CS06)
# CS06 motion start: the first tick comes 62.5 ms after the art is ready, within +5 ms (no start stall - art is prepared elsewhere)
# CS07 work per tick: at most 16 ms, 99th percentile at most 8 ms
param([string]$Exe = "", [int]$Laps = 2, [int]$MaxMinutes = 10, [string]$AnalyzeOnly = "")
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
$ids = @(); 0..7 | ForEach-Object { $ids += "CS{0:D2}" -f $_ }
Start-Checks -Required $ids
$clipNames = @(Get-Content (Join-Path $PSScriptRoot "..\..\src\OneKey\Assets\cat\catclips.txt") | Where-Object { $_ -match '^\S+\s+\d+' } | ForEach-Object { ($_ -split '\s+')[0] })

function Analyze([string]$log, [bool]$shown) {
  $reTick = '^(\S+) tick dt (\S+) want (\d+) work (\S+) (.+?)( LATE)? n(\d+) push (\d+) fail (\d+) h ([0-9a-f]{8})$'
  $reStart = '^(\S+) clipstart (\S+) steps (\d+) '
  $ticks = New-Object System.Collections.Generic.List[object]; $starts = New-Object System.Collections.Generic.List[object]
  foreach ($l in [IO.File]::ReadAllLines($log)) {
    if ($l -match $reTick) { $ticks.Add([pscustomobject]@{ i = $ticks.Count; dt = [double]$Matches[2]; want = [int]$Matches[3]; work = [double]$Matches[4]; what = $Matches[5]; late = [bool]$Matches[6]; push = [int]$Matches[8]; fail = [int]$Matches[9]; h = $Matches[10] }) }
    elseif ($l -match $reStart) { $starts.Add([pscustomobject]@{ at = $ticks.Count; label = $Matches[2]; steps = [int]$Matches[3] }) }
  }
  "log: $log - $($ticks.Count) ticks, $($starts.Count) motion starts"
  Check CS00 "True|True" "$shown|$($ticks.Count -gt 100)" "environment, cat shown, ticks in the log ($($ticks.Count))"
  $labels = @($starts | ForEach-Object { $_.label })
  $ok1 = $labels.Count -ge $clipNames.Count * $Laps; $bad1 = ""
  for ($k = 0; $k -lt $labels.Count -and $ok1; $k++) { $e = $clipNames[$k % $clipNames.Count]; if ($labels[$k] -ne $e) { $ok1 = $false; $bad1 = "start $k is $($labels[$k]), expected $e" } }
  Check CS01 "True" "$ok1" "motions in list order x $Laps ($($labels.Count) starts: $($labels -join ' ')) $bad1"
  $frameBad = @(); $pushBad = @(); $same = 0; $changes = 0; $firstDt = @(); $inner = New-Object System.Collections.Generic.List[double]; $judged = 0; $cut = ""
  for ($c = 0; $c -lt $starts.Count; $c++) {
    $s = $starts[$c]; $end = if ($c + 1 -lt $starts.Count) { $starts[$c + 1].at } else { $ticks.Count }
    $seg = @($ticks | Select-Object -Skip $s.at -First ($end - $s.at))
    $frames = @($seg | Where-Object { $_.what -match ('^' + [regex]::Escape($s.label) + ' (\d+) s\d+( c\S+ x-?\d+)?$') })
    $endTick = @($seg | Where-Object { $_.what -eq 'S' })
    if ($c + 1 -eq $starts.Count -and $endTick.Count -eq 0) { $cut = " (last start $($s.label) cut off by the end of the run, not judged)"; break }
    $judged++
    $idx = @($frames | ForEach-Object { [void]($_.what -match ' (\d+) s'); [int]$Matches[1] })
    $want = 0..($s.steps - 1)
    $okSeq = ($idx.Count -eq $s.steps) -and (-not (Compare-Object $idx $want -SyncWindow 0))
    if (-not $okSeq) { $frameBad += "$($s.label)[$c]: $($idx.Count)/$($s.steps) frames, first gap at $(for ($q = 0; $q -lt [Math]::Min($idx.Count, $s.steps); $q++) { if ($idx[$q] -ne $q) { $q; break } })" }
    if ($endTick.Count -ne 1) { $frameBad += "$($s.label)[$c]: $($endTick.Count) end ticks" }
    for ($q = 0; $q -lt $frames.Count; $q++) {
      $tk = $frames[$q]
      if ($tk.push -ne 1 -or $tk.fail -ne 0) { $pushBad += "$($s.label)@$q(push $($tk.push) fail $($tk.fail))" }
      if ($q -gt 0) { $changes++; if ($frames[$q - 1].h -eq $tk.h) { $same++ }; $inner.Add([math]::Abs($tk.dt - 62.5)) }
      else { $firstDt += [pscustomobject]@{ label = $s.label; dt = $tk.dt } }
    }
  }
  Check CS02 "0" "$($frameBad.Count)" ("$judged motions: every frame on exactly one tick, in order, to the end, then back to the front pose$cut" + $(if ($frameBad.Count) { " - " + (($frameBad | Select-Object -First 5) -join ' | ') } else { "" }))
  $pct = if ($changes) { [math]::Round(100.0 * $same / $changes, 2) } else { 0 }
  Check CS03 "0|True" "$($pushBad.Count)|$($pct -le 2)" "frame ticks: missing or failed pushes $($pushBad.Count) $(($pushBad | Select-Object -First 5) -join ' '), unchanged picture on a frame change $same of $changes ($pct %)"
  $late = @($ticks | Where-Object { $_.late })
  Check CS04 "0" "$($late.Count)" "late ticks $(($late | Select-Object -First 5 | ForEach-Object { "#$($_.i) $($_.dt) $($_.what)" }) -join ', ')"
  $within = @($inner | Where-Object { $_ -le 2 }).Count; $p5 = if ($inner.Count) { [math]::Round(100.0 * $within / $inner.Count, 2) } else { 0 }
  $worst = if ($inner.Count) { [math]::Round(($inner | Measure-Object -Maximum).Maximum, 1) } else { 0 }
  Check CS05 "True" "$($p5 -ge 99)" "intervals within +-2 ms of 62.5: $p5 % of $($inner.Count) (worst off by $worst ms)"
  $slow = @($firstDt | Where-Object { $_.dt -gt 67.5 })
  Check CS06 "0" "$($slow.Count)" "first tick of each motion within 62.5 + 5 ms of its start ($($firstDt.Count) starts) $(($slow | Select-Object -First 5 | ForEach-Object { "$($_.label) $($_.dt)" }) -join ', ')"
  $w = @($ticks | ForEach-Object { $_.work } | Sort-Object); $wmax = if ($w.Count) { $w[-1] } else { 0 }; $w99 = if ($w.Count) { $w[[int][math]::Floor(($w.Count - 1) * 0.99)] } else { 0 }
  Check CS07 "True" "$($wmax -le 16 -and $w99 -le 8)" "work per tick: max $wmax ms, 99th percentile $w99 ms"
}

if ($AnalyzeOnly) { Analyze $AnalyzeOnly $true; Complete-Checks; return }

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$suffix = ".cs"
$root = Join-Path $sp ("catseq-{0:yyyyMMdd-HHmmss}-{1}" -f (Get-Date), $PID); New-Item -ItemType Directory $root | Out-Null
$cfg = "$root\cfg"; New-Item -ItemType Directory $cfg | Out-Null
$log = "$root\timing.log"
$master = "Cs-Dummy-7741"
Add-Type @'
using System;using System.Runtime.InteropServices;using System.Text;
public class CSU {
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
function Start1Key() {
  $script:p = Start-Process $exe -PassThru; $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $script:m = [CSU]::Find([uint32]$script:p.Id, "OneKeyMainWindow$suffix", $false); if ($script:m -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 100 }
  if ($script:m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 900
}
function Quit1Key() { if ($script:p -and -not $script:p.HasExited) { [void][CSU]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i = 0; $i -lt 50 -and -not $script:p.HasExited; $i++) { Start-Sleep -Milliseconds 100 }; if (-not $script:p.HasExited) { Stop-Process -Id $script:p.Id -Force } } }
function Has($id) { [CSU]::GetDlgItem($m, $id) -ne [IntPtr]::Zero }
function Click($id, $ms = 700) { [void][CSU]::PostMessageW([CSU]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds $ms }
function SetText($id, $s) { [void][CSU]::SendMessageW([CSU]::GetDlgItem($m, $id), 0x000C, [IntPtr]::Zero, [string]$s) }

$shown = $false
try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_LOCKWIDGET = "0"; $env:ONEKEY_TEST_NO_AUTOLOCK = "1"
  $env:ONEKEY_TEST_CAT_SEQ = "1"; $env:ONEKEY_TEST_CAT_LOG = $log
  $abd = New-Object CSU+ABD; $abd.cbSize = [Runtime.InteropServices.Marshal]::SizeOf([type][CSU+ABD]); [void][CSU]::SHAppBarMessage(5, [ref]$abd)
  $st = New-Object CSU+ABD; $st.cbSize = $abd.cbSize; $auto = ([uint64][CSU]::SHAppBarMessage(4, [ref]$st)) -band 1
  $envOk = ($abd.uEdge -eq 3) -and ($auto -eq 0)
  Start1Key
  SetText 101 $master; SetText 102 $master; Click 103 1500
  if (-not (Has 203)) { throw "setup: no list after creating the master" }
  Click 220
  $cur = [int][CSU]::SendMessageW([CSU]::GetDlgItem($m, 2032), 0x00F0, [IntPtr]::Zero, [IntPtr]::Zero) -eq 1
  if (-not $cur) { [void][CSU]::PostMessageW([CSU]::GetDlgItem($m, 2032), 0x0100, [IntPtr]0x20, [IntPtr]::Zero); Start-Sleep -Milliseconds 300 }
  Click 2012 900
  [void][CSU]::PostMessageW($m, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
  $t0 = [Environment]::TickCount; while ([Environment]::TickCount - $t0 -lt 5000 -and [CSU]::Find([uint32]$p.Id, "OneKeyCat", $true) -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 100 }
  $shown = $envOk -and ([CSU]::Find([uint32]$p.Id, "OneKeyCat", $true) -ne [IntPtr]::Zero)
  if (-not $envOk) { "environment: taskbar edge $($abd.uEdge) (3 = bottom), auto-hide $auto" }
  $need = $clipNames.Count * $Laps + 1
  $deadline = (Get-Date).AddMinutes($MaxMinutes)
  $cpu0 = (Get-Process -Id $p.Id).TotalProcessorTime.TotalMilliseconds; $wall0 = [Environment]::TickCount; $mem = 0
  while ((Get-Date) -lt $deadline -and $shown) {
    Start-Sleep -Seconds 3
    try { $mem = [Math]::Max($mem, (Get-Process -Id $p.Id).PrivateMemorySize64) } catch { }
    if (Test-Path $log) { if (@(Select-String -Path $log -Pattern ' clipstart ' -SimpleMatch).Count -ge $need) { break } }
    if ($p.HasExited) { "1Key exited"; break }
  }
  try { $cpu = ((Get-Process -Id $p.Id).TotalProcessorTime.TotalMilliseconds - $cpu0) / ([Environment]::TickCount - $wall0) * 100; "measured (not judged): CPU {0:N2} % of one core while the motions play, private memory peak {1:N1} MB" -f $cpu, ($mem / 1MB) } catch { }
  Start-Sleep -Seconds 3
  Quit1Key; Start-Sleep -Milliseconds 500
  if (Test-Path $log) { Analyze $log $shown } else { Check CS00 "True" "False" "no log written ($log)" }
} catch { Add-Failure ("exception: " + $_) }
finally {
  Quit1Key; Stop-TestInstances $suffix
  foreach ($v in "ONEKEY_TEST_CAT_SEQ", "ONEKEY_TEST_CAT_LOG", "ONEKEY_TEST_LOCKWIDGET", "ONEKEY_TEST_NO_AUTOLOCK") { Remove-Item "env:$v" -ErrorAction Ignore }
  "log kept: $log"
}
Complete-Checks
