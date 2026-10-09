# Taskbar cat hide-and-peek (0.5.15-G, user 2026-10-09: moving motions like the in-house edition; the hang-over-the-edge spot, drawn
# with the gaze pictures so the black cat can do it too). 1Key in test mode with ONEKEY_TEST_CAT_PEEK=1 only peeks (0.6 s apart) and logs
#   "peekstart depth D hold H look at:gaze,..." / "peek t T sink S gaze G" (on every change) / "peekend"
# Run for the light and the dark 1Key theme. Own config folder and instance suffix; the real 1Key is not touched.
#   powershell -ExecutionPolicy Bypass -File tools\tests\catpeek.ps1 [-Exe <1Key.exe>] [-Peeks 3]
# CP00 environment, cat shown, -Peeks complete peeks per theme
# CP01 never deeper than the planned depth (cut line below the eyes - user: the eyes must stay visible), never above 0, back to 0
# CP02 while down the cat looks left and right (gaze 3 and 5), leaning that way, and is front (4) again before coming up
# CP03 each peek lasts crouch+drop+bounce + hold + rise+land (planned) within 150 ms
param([string]$Exe = "", [int]$Peeks = 3, [int]$MaxMinutes = 3, [string]$AnalyzeOnly = "")
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
$ids = @(); foreach ($t in "light", "dark") { 0..3 | ForEach-Object { $ids += "CP{0:D2}[{1}]" -f $_, $t } }
Start-Checks -Required $ids

function Analyze([string]$log, [string]$tag) {
  $plist = New-Object System.Collections.Generic.List[object]; $cur = $null
  foreach ($l in [IO.File]::ReadAllLines($log)) {
    if ($l -match ' peekstart depth (\d+) hold (\d+) eyebottom (\d+) sitbot (\d+) downms (\d+) upms (\d+) ') { $cur = [pscustomobject]@{ depth = [int]$Matches[1]; hold = [int]$Matches[2]; eye = [int]$Matches[3]; bot = [int]$Matches[4]; down = [int]$Matches[5]; up = [int]$Matches[6]; ev = New-Object System.Collections.Generic.List[object]; ended = $false }; $plist.Add($cur) }
    elseif ($l -match ' peek t (\d+) sink (-?\d+) gaze (\d+) lean (-?\d+) squash (\S+)' -and $cur) { $cur.ev.Add([pscustomobject]@{ t = [int]$Matches[1]; s = [int]$Matches[2]; g = [int]$Matches[3]; lean = [int]$Matches[4]; q = [double]$Matches[5] }) }
    elseif ($l -match ' peekend' -and $cur) { $cur.ended = $true }
  }
  $done = @($plist | Where-Object { $_.ended })
  $b1 = @(); $b2 = @(); $b3 = @(); $b4 = @()
  for ($i = 0; $i -lt $done.Count; $i++) {
    $k = $done[$i]; $ev = $k.ev.ToArray(); $max = ($ev | Measure-Object -Property s -Maximum).Maximum; $min = ($ev | Measure-Object -Property s -Minimum).Minimum
    if ($max -ne $k.depth -or $min -lt 0 -or $ev[$ev.Count - 1].s -ne 0) { $b1 += "peek ${i}: sink max $max depth $($k.depth) min $min last $($ev[$ev.Count - 1].s)" }
    if ($k.bot - $k.depth -le $k.eye) { $b4 += "peek ${i}: cut line $($k.bot - $k.depth) not below the eyes ($($k.eye))" }
    $hold = @($ev | Where-Object { $_.s -eq $k.depth })
    $gz = @($hold | ForEach-Object g)
    $leanBad = @($hold | Where-Object { ($_.g -eq 3 -and $_.lean -ge 0) -or ($_.g -eq 5 -and $_.lean -le 0) -or ($_.g -eq 4 -and $_.lean -ne 0) })
    if (-not (($gz -contains 3) -and ($gz -contains 5) -and $hold[$hold.Count - 1].g -eq 4) -or $leanBad.Count) { $b2 += "peek ${i}: gazes while down $(($gz | Select-Object -Unique) -join ',') last $($hold[$hold.Count - 1].g), lean mismatches $($leanBad.Count)" }
    $want = $k.down + $k.hold + $k.up; $got = $ev[$ev.Count - 1].t
    if ([math]::Abs($got - $want) -gt 150) { $b3 += "peek ${i}: $got ms want $want" }
  }
  Check "CP00[$tag]" "True" "$($done.Count -ge $Peeks)" "complete peeks $($done.Count)"
  Check "CP01[$tag]" "0" "$($b1.Count + $b4.Count)" ("never deeper than planned, back to 0; cut line below the eyes (depth $(($done | ForEach-Object depth) -join ','), eye bottom $(($done | Select-Object -First 1).eye), feet $(($done | Select-Object -First 1).bot))" + $(if ($b1 -or $b4) { " - " + (($b1 + $b4) -join ' | ') }))
  Check "CP02[$tag]" "0" "$($b2.Count)" ("looks left and right while down and leans that way, front before coming up" + $(if ($b2) { " - " + ($b2 -join ' | ') }))
  Check "CP03[$tag]" "0" "$($b3.Count)" ("duration crouch+drop+bounce + hold + rise+land within 150 ms $(($done | ForEach-Object { $_.ev[$_.ev.Count - 1].t }) -join ',')" + $(if ($b3) { " - " + ($b3 -join ' | ') }))
}

if ($AnalyzeOnly) { foreach ($t in "light", "dark") { $f = $AnalyzeOnly -replace "\{tag\}", $t; Analyze $f $t }; Complete-Checks; return }

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$suffix = ".cp"
$root = Join-Path $sp ("catpeek-{0:yyyyMMdd-HHmmss}-{1}" -f (Get-Date), $PID); New-Item -ItemType Directory $root | Out-Null
$cfg = "$root\cfg"; New-Item -ItemType Directory $cfg | Out-Null
$log = "$root\timing.log"
$master = "Cp-Dummy-4471"
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

foreach ($tag in "light", "dark") {
  $downs = if ($tag -eq "light") { 1 } else { 2 }
  $log = "$root\timing-$tag.log"; $cfg = "$root\cfg-$tag"; New-Item -ItemType Directory $cfg | Out-Null
$shown = $false
  try {
    $env:ONEKEY_TEST_CAT_LOG = $log
    Stop-TestInstances $suffix
    $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_LOCKWIDGET = "0"; $env:ONEKEY_TEST_NO_AUTOLOCK = "1"
    $env:ONEKEY_TEST_CAT_PEEK = "1"
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
    for ($kk = 0; $kk -lt $downs; $kk++) { [void][CWU]::PostMessageW([CWU]::GetDlgItem($m, 2008), 0x0100, [IntPtr]0x28, [IntPtr]::Zero); Start-Sleep -Milliseconds 500 }   # theme: follow -> light (1) / dark (2)
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
      if (Test-Path $log) { if (@(Select-String -Path $log -Pattern ' peekend' -SimpleMatch).Count -ge $Peeks) { break } }
      if ($p.HasExited) { "1Key exited"; break }
    }
    try { $cpu = ((Get-Process -Id $p.Id).TotalProcessorTime.TotalMilliseconds - $cpu0) / ([Environment]::TickCount - $wall0) * 100; "measured (not judged): CPU {0:N2} % of one core while peeking, private memory peak {1:N1} MB" -f $cpu, ($mem / 1MB) } catch { }
    Start-Sleep -Seconds 2
    Quit1Key; Start-Sleep -Milliseconds 500
    if (Test-Path $log) { Analyze $log $tag } else { Check "CP00[$tag]" "True" "False" "no log written ($log)" }
  } catch { Add-Failure ("exception: " + $_) }
  finally {
    Quit1Key; Stop-TestInstances $suffix
    foreach ($v in "ONEKEY_TEST_CAT_PEEK", "ONEKEY_TEST_CAT_LOG", "ONEKEY_TEST_LOCKWIDGET", "ONEKEY_TEST_NO_AUTOLOCK") { Remove-Item "env:$v" -ErrorAction Ignore }
    "log kept: $log"
  }
  
}
Complete-Checks
