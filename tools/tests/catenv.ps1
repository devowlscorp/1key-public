# Cat re-fit on environment signals (0.5.23, Codex 2026-10-12: check the changed signal path itself).
# With the cat shown and the work area, scale and colour unchanged:
# CE00 environment: taskbar at the bottom, cat shown (OneKeyTestWalker=1), the show count read
# CE01 a plain WM_SETTINGCHANGE (no area name) does not rebuild: still shown, show count the same
# CE02 WM_DISPLAYCHANGE (0x007E) rebuilds: the show count goes up, shown again with reason 0
# CE03 the registered TaskbarCreated message rebuilds the same way
# CE04 the cat log (flushed when 1Key exits) has "envchanged same" for CE01 and two "envchanged redo" after it, in that order -
#      only together with CE01-CE03 (the log line is written before the rebuild, so it is no proof of a show by itself)
# Does not change the screen mode or restart Explorer: the messages go to the test 1Key's own window only.
# Own config folder and instance suffix; the real 1Key is not touched. The cat is shown on the taskbar: run it while nobody uses the PC.
#   powershell -ExecutionPolicy Bypass -File tools\tests\catenv.ps1 [-Exe <1Key.exe>]
# ASCII only.
param([string]$Exe = "")
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
$ids = @(); 0..4 | ForEach-Object { $ids += "CE{0:D2}" -f $_ }
Start-Checks -Required $ids

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$suffix = ".ce"
$root = Join-Path $sp ("catenv-{0:yyyyMMdd-HHmmss}-{1}" -f (Get-Date), $PID); New-Item -ItemType Directory $root | Out-Null
$cfg = "$root\cfg"; New-Item -ItemType Directory $cfg | Out-Null
$log = "$root\cat.log"
$master = "Ce-Dummy-5183"
Add-Type @'
using System;using System.Runtime.InteropServices;using System.Text;
public class CEU {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr GetPropW(IntPtr h, string name);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern uint RegisterWindowMessageW(string s);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct ABD { public uint cbSize; public IntPtr hWnd; public uint cb, uEdge; public RECT rc; public IntPtr lParam; }
  [DllImport("shell32.dll")] public static extern UIntPtr SHAppBarMessage(uint msg, ref ABD d);
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static IntPtr Find(uint pid, string cls, bool vis) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls && (!vis || IsWindowVisible(h))) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
}
'@
function Start1Key() {
  $script:p = Start-Process $exe -PassThru; $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $script:m = [CEU]::Find([uint32]$script:p.Id, "OneKeyMainWindow$suffix", $false); if ($script:m -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 100 }
  if ($script:m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 900
}
function Quit1Key() { if ($script:p -and -not $script:p.HasExited) { [void][CEU]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i = 0; $i -lt 50 -and -not $script:p.HasExited; $i++) { Start-Sleep -Milliseconds 100 }; if (-not $script:p.HasExited) { Stop-Process -Id $script:p.Id -Force } } }
function Has($id) { [CEU]::GetDlgItem($m, $id) -ne [IntPtr]::Zero }
function Click($id, $ms = 700) { [void][CEU]::PostMessageW([CEU]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds $ms }
function SetText($id, $s) { [void][CEU]::SendMessageW([CEU]::GetDlgItem($m, $id), 0x000C, [IntPtr]::Zero, [string]$s) }
function Prop($n) { [int][CEU]::GetPropW($m, $n) }
function Cat() { [CEU]::Find([uint32]$p.Id, "OneKeyCat", $true) -ne [IntPtr]::Zero }
function State() { "$(Prop 'OneKeyTestWalker')/$(Prop 'OneKeyTestWalkerShows')/$(Prop 'OneKeyWalkerWhy')/$(Cat)" }
# waits until the show count is above $gen and the cat is shown again (prop and window); "" on timeout
function WaitReshown([int]$gen, [int]$ms = 6000) {
  $t = [Environment]::TickCount
  while ([Environment]::TickCount - $t -lt $ms) { if ((Prop 'OneKeyTestWalkerShows') -gt $gen -and (Prop 'OneKeyTestWalker') -eq 1 -and (Cat)) { return $true }; Start-Sleep -Milliseconds 100 }
  $false
}

try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_LOCKWIDGET = "0"; $env:ONEKEY_TEST_NO_AUTOLOCK = "1"
  $env:ONEKEY_TEST_CAT_LOG = $log
  $abd = New-Object CEU+ABD; $abd.cbSize = [Runtime.InteropServices.Marshal]::SizeOf([type][CEU+ABD]); [void][CEU]::SHAppBarMessage(5, [ref]$abd)
  $st = New-Object CEU+ABD; $st.cbSize = $abd.cbSize; $auto = ([uint64][CEU]::SHAppBarMessage(4, [ref]$st)) -band 1
  $envOk = ($abd.uEdge -eq 3) -and ($auto -eq 0)
  if (-not $envOk) { "environment: taskbar edge $($abd.uEdge) (3 = bottom), auto-hide $auto" }
  Start1Key
  SetText 101 $master; SetText 102 $master; Click 103 1500
  if (-not (Has 203)) { throw "setup: no list after creating the master" }
  Click 220
  $cur = [int][CEU]::SendMessageW([CEU]::GetDlgItem($m, 2032), 0x00F0, [IntPtr]::Zero, [IntPtr]::Zero) -eq 1
  if (-not $cur) { [void][CEU]::PostMessageW([CEU]::GetDlgItem($m, 2032), 0x0100, [IntPtr]0x20, [IntPtr]::Zero); Start-Sleep -Milliseconds 300 }
  Click 2012 900
  [void][CEU]::PostMessageW($m, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)   # WM_CLOSE -> to the tray, the cat appears
  $t0 = [Environment]::TickCount; while ([Environment]::TickCount - $t0 -lt 6000 -and -not ((Prop 'OneKeyTestWalker') -eq 1 -and (Cat))) { Start-Sleep -Milliseconds 100 }
  Start-Sleep -Seconds 3   # settle: intro done, work area and scale recorded
  $s0 = State
  Check CE00 "True|1|0|True" "$envOk|$(Prop 'OneKeyTestWalker')|$(Prop 'OneKeyWalkerWhy')|$(Cat)" "taskbar at the bottom, cat shown with reason 0 (state $s0)"

  # CE01 plain WM_SETTINGCHANGE: same work area / scale / colour -> no rebuild
  $g1 = Prop 'OneKeyTestWalkerShows'
  [void][CEU]::SendMessageW($m, 0x001A, [IntPtr]::Zero, [IntPtr]::Zero)
  Start-Sleep -Seconds 2
  $s1 = State
  Check CE01 "1|$g1|True" "$(Prop 'OneKeyTestWalker')|$(Prop 'OneKeyTestWalkerShows')|$(Cat)" "plain WM_SETTINGCHANGE: still shown, show count unchanged (before $s0, after $s1)"

  # CE02 WM_DISPLAYCHANGE -> forced re-fit
  $g2 = Prop 'OneKeyTestWalkerShows'
  [void][CEU]::SendMessageW($m, 0x007E, [IntPtr]::Zero, [IntPtr]::Zero)
  $r2 = WaitReshown $g2
  Start-Sleep -Milliseconds 500; $s2 = State
  Check CE02 "True|0" "$r2|$(Prop 'OneKeyWalkerWhy')" "WM_DISPLAYCHANGE: show count above $g2 and shown again, reason 0 (after $s2)"

  # CE03 TaskbarCreated -> forced re-fit
  Start-Sleep -Seconds 2
  $g3 = Prop 'OneKeyTestWalkerShows'
  $tbc = [CEU]::RegisterWindowMessageW("TaskbarCreated")
  [void][CEU]::SendMessageW($m, $tbc, [IntPtr]::Zero, [IntPtr]::Zero)
  $r3 = WaitReshown $g3
  Start-Sleep -Milliseconds 500; $s3 = State
  Check CE03 "True|0" "$r3|$(Prop 'OneKeyWalkerWhy')" "TaskbarCreated: show count above $g3 and shown again, reason 0 (after $s3)"

  Quit1Key; Start-Sleep -Milliseconds 500
  $seq = if (Test-Path $log) { @(Select-String -Path $log -Pattern 'envchanged (same|redo)' | ForEach-Object { $_.Matches[0].Groups[1].Value }) } else { @() }
  "cat log envchanged lines: $($seq -join ', ')"
  $i = [Array]::IndexOf($seq, "same")
  $after = if ($i -ge 0) { @($seq[($i + 1)..($seq.Count)] | Where-Object { $_ -eq "redo" }).Count } else { 0 }
  Check CE04 "True|2" "$($i -ge 0)|$after" "cat log: envchanged same for CE01, then two envchanged redo (CE02, CE03)"
} catch { Add-Failure ("exception: " + $_) }
finally {
  Quit1Key; Stop-TestInstances $suffix
  foreach ($v in "ONEKEY_TEST_CAT_LOG", "ONEKEY_TEST_LOCKWIDGET", "ONEKEY_TEST_NO_AUTOLOCK") { Remove-Item "env:$v" -ErrorAction Ignore }
  "log kept: $log"
}
Complete-Checks
