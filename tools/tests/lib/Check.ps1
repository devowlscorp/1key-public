# Common judgement for the 1Key test scripts (T18). Dot-source it: . "$PSScriptRoot\lib\Check.ps1"
#
#   Start-Checks -Required @('A01','A02',...)   declare every check ID the script must produce, once each
#   Check <ID> <expected> <actual> [label]       record one check; pass when "$expected" -ceq "$actual"
#   Add-Failure <message>                         record an unexpected failure (catch blocks, missing windows)
#   Clear-ExpectedError                           drop the newest $Error entry when a script provoked it on purpose
#   Complete-Checks                               judge and exit: 0 only when every required ID passed exactly once,
#                                                 no unknown/duplicate IDs, no Add-Failure, and no ErrorRecord in $Error
#
# One test run at a time (0.3.7): Start-Checks takes a machine-wide lock "Local\OneKeyTestRun"; a second script started while one
# runs stops at once with "another test run is in progress" (two runs would steal each other's focus and keys).
# First failure picture (0.3.7): the first failing Check saves a picture of the test instance's own main window ($m, PrintWindow - never
# the whole screen, so nothing else on the PC is captured) to <scratch>\fail-shots\<script>-<ID>-<time>.png and prints the path.
# Output (the ok/FAIL lines) is only a report. The exit code comes from the recorded results, not from parsing text.
# Use -ErrorAction Ignore (not SilentlyContinue) for cleanup commands whose failure is harmless:
# SilentlyContinue still appends to $Error and would fail the run.
# ASCII only.

$script:CK = @{ Required = @(); Results = [ordered]@{}; Dup = New-Object System.Collections.ArrayList; Extra = New-Object System.Collections.ArrayList; Started = $false; Shot = $false }

function Lock-TestRun {
  if ($script:CkRunLock) { return }
  if ($env:ONEKEY_TEST_RUN_LOCKED -eq "1") { return }   # started by a run that already holds the lock (e.g. dw-compare -> shots)
  $mx = New-Object System.Threading.Mutex($false, "Local\OneKeyTestRun")
  $got = $false
  try { $got = $mx.WaitOne(2000) } catch [System.Threading.AbandonedMutexException] { $got = $true }   # an earlier run died: the lock is ours now
  if (-not $got) { "FAIL another 1Key test run is in progress - not starting (one run at a time)"; "fail=1"; exit 1 }
  $script:CkRunLock = $mx
  $env:ONEKEY_TEST_RUN_LOCKED = "1"   # child scripts of this run inherit it
}

# A picture of the test instance's main window only (PrintWindow, PW_RENDERFULLCONTENT). Nothing else on the screen is captured.
function Save-FailShot([string]$Id) {
  try {
    $w = Get-Variable m -Scope Script -ValueOnly -ErrorAction Ignore
    if (-not $w -or $w -eq [IntPtr]::Zero) { return }
    if (-not ('CkShot' -as [type])) {
      Add-Type -ReferencedAssemblies System.Drawing @'
using System;using System.Drawing;using System.Runtime.InteropServices;
public class CkShot { [StructLayout(LayoutKind.Sequential)] struct R { public int L, T, Rt, B; }
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out R r); [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
  public static bool Save(IntPtr h, string path) { if (!IsWindow(h)) return false; R r; if (!GetWindowRect(h, out r) || r.Rt - r.L < 2 || r.B - r.T < 2) return false;
    using (var bmp = new Bitmap(r.Rt - r.L, r.B - r.T)) { using (var g = Graphics.FromImage(bmp)) { IntPtr dc = g.GetHdc(); try { PrintWindow(h, dc, 2); } finally { g.ReleaseHdc(dc); } } bmp.Save(path); } return true; } }
'@
    }
    $root = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
    $dir = Join-Path $root "fail-shots"; New-Item -ItemType Directory -Force $dir | Out-Null
    $top = (Get-PSCallStack | Where-Object { $_.ScriptName } | Select-Object -Last 1).ScriptName
    $name = if ($top) { [IO.Path]::GetFileNameWithoutExtension($top) } else { "test" }
    $file = Join-Path $dir ("{0}-{1}-{2:HHmmss}.png" -f $name, $Id, (Get-Date))
    if ([CkShot]::Save([IntPtr]$w, $file)) { "  (first failure: picture of the test window $file)" }
  } catch { }
}

function Start-Checks([string[]]$Required) {
  $script:CK.Required = @($Required)
  $script:CK.Results = [ordered]@{}
  $script:CK.Dup.Clear(); $script:CK.Extra.Clear()
  $script:CK.Started = $true; $script:CK.Shot = $false
  Lock-TestRun
  $dupDecl = @($Required | Group-Object | Where-Object { $_.Count -gt 1 } | ForEach-Object { $_.Name })
  foreach ($d in $dupDecl) { [void]$script:CK.Extra.Add("required ID declared twice: $d") }
  $global:Error.Clear()
}

function Check([string]$Id, $Expected, $Actual, [string]$Label = "") {
  $ok = ("$Expected" -ceq "$Actual")
  if ($script:CK.Results.Contains($Id)) {
    [void]$script:CK.Dup.Add($Id)
    $script:CK.Results[$Id] = $script:CK.Results[$Id] -and $ok
  } else { $script:CK.Results[$Id] = $ok }
  if ($ok) { "ok   [$Id] $Label" } else {
    "FAIL [$Id] $Label  expected=<$Expected> actual=<$Actual>"
    if (-not $script:CK.Shot) { $script:CK.Shot = $true; Save-FailShot $Id }
  }
}

function Add-Failure([string]$Message) { [void]$script:CK.Extra.Add($Message); "FAIL unexpected: $Message" }

function Clear-ExpectedError { if ($global:Error.Count -gt 0) { $global:Error.RemoveAt(0) } }

function Complete-Checks {
  $problems = New-Object System.Collections.ArrayList
  if (-not $script:CK.Started) { [void]$problems.Add("Start-Checks was never called") }
  foreach ($id in $script:CK.Required) {
    if (-not $script:CK.Results.Contains($id)) { [void]$problems.Add("missing check $id") }
    elseif (-not $script:CK.Results[$id]) { [void]$problems.Add("failed check $id") }
  }
  foreach ($id in $script:CK.Results.Keys) { if ($script:CK.Required -notcontains $id) { [void]$problems.Add("undeclared check $id") } }
  foreach ($id in ($script:CK.Dup | Select-Object -Unique)) { [void]$problems.Add("duplicate check $id") }
  foreach ($x in $script:CK.Extra) { [void]$problems.Add($x) }
  foreach ($e in @($global:Error)) {
    $where = if ($e.InvocationInfo) { " (line " + $e.InvocationInfo.ScriptLineNumber + ")" } else { "" }
    [void]$problems.Add("error record: " + ("$e" -replace "\s+", " ") + $where)
  }
  # which build was tested: version and SHA-256, so a rebuild with the same version number cannot be confused (Codex V36-3)
  $target = if (Get-Variable exe -ErrorAction Ignore) { $exe } elseif (Get-Variable new -ErrorAction Ignore) { $new } else { $null }
  if ($target -and (Test-Path $target)) { "---- exe: $target  version " + (Get-Item $target).VersionInfo.FileVersion + "  SHA-256 " + (Get-FileHash $target -Algorithm SHA256).Hash }
  # how long nobody has touched this PC (system-wide input idle; auto-lock uses the same value). Recorded so that a failure
  # on an unattended PC can be told apart later (Codex V47 4). Posted test messages do not count as input.
  "---- system input idle at the end: {0:N1} min" -f ((Get-SystemIdleMs) / 60000)
  $passed = @($script:CK.Required | Where-Object { $script:CK.Results.Contains($_) -and $script:CK.Results[$_] }).Count
  "---- judgement: $passed/$($script:CK.Required.Count) required checks passed, $($problems.Count) problem(s)"
  foreach ($pr in $problems) { "PROBLEM $pr" }
  "fail=$($problems.Count)"
  if ($problems.Count -gt 0) { exit 1 } else { exit 0 }
}

# ---- shared helpers

function Get-SystemIdleMs {
  if (-not ('CkIdle' -as [type])) {
    Add-Type @'
using System;using System.Runtime.InteropServices;
public class CkIdle { [StructLayout(LayoutKind.Sequential)] struct L { public uint cb; public uint t; } [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref L l);
  public static long Ms() { var l = new L(); l.cb = 8; if (!GetLastInputInfo(ref l)) return -1; return unchecked((uint)Environment.TickCount - l.t); } }
'@
  }
  [CkIdle]::Ms()
}

# The decrypted header of a test instance's own config (DPAPI with the app's entropy). Only for scratch config folders.
function Get-TestHeader([string]$Dir) {
  Add-Type -AssemblyName System.Security
  [Text.Encoding]::UTF8.GetString([Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes("$Dir\config.dat"), [Text.Encoding]::UTF8.GetBytes("1Key/approval-password/v1"), 'CurrentUser'))
}

# Scripts that switch auto-lock off in their setup confirm it was really saved (Codex V47 4); throws otherwise, which the
# script's catch records as a failure.
function Confirm-AutoLockOff([string]$Dir) {
  $v = ([regex]::Match((Get-TestHeader $Dir), '(?m)^autolock=(\d+)')).Groups[1].Value
  if ($v -ne "0") { throw "setup: auto-lock was not saved as off (autolock=$v)" }
}

# A tall simulated screen (ONEKEY_TEST_WORKAREA) for scripts that test behaviour, not layout: the settings then always
# expand inline under the list, whatever the real monitor is. Small screens are covered by layout.ps1.
$TallScreen = "2560,2400,96"

# Screen language for every test launch (multi-language, 0.2.54): the tests read Korean texts and use the Korean layout, so
# they pin the "Windows display language" 1Key follows to Korean. i18n.ps1 sets its own value per launch.
$env:ONEKEY_TEST_LANG = "ko"
# Auto-lock paused for test launches (0.2.60): on an unattended PC the system idle time passes 10 minutes and the default
# auto-lock locked test instances mid-run (inject/mem 2026-09-30). autolock.ps1 clears this to test auto-lock itself.
$env:ONEKEY_TEST_NO_AUTOLOCK = "1"
# Old in-window lock screen for test launches (B redesign stage 2, 2026-10-03): when locked, 1Key now shows the floating
# mascot widget instead of the main window. The existing suites drive the in-window lock screen by control id, and that
# screen is also the widget's failure fallback, so they keep testing it. tools/tests/lockwidget/integration.ps1 sets "1".
$env:ONEKEY_TEST_LOCKWIDGET = "0"

# Default exe: build\<version from OneKey.csproj>\1Key.exe
function Get-DefaultExe {
  $repo = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")   # this file lives in tools\tests\lib
  $ver = ([xml](Get-Content "$repo\src\OneKey\OneKey.csproj")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
  "$repo\build\$ver\1Key.exe"
}

# Kill every process that owns a main window of this test suffix (never the user's own 1Key: its class has no suffix).
function Stop-TestInstances([string]$Suffix) {
  if (-not ('CkWin' -as [type])) {
    Add-Type @'
using System;using System.Runtime.InteropServices;using System.Text;
public class CkWin { public delegate bool CB(IntPtr h, IntPtr l); [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l); [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid); [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
 public static uint[] Pids(string cls) { var l = new System.Collections.Generic.List<uint>(); EnumWindows((h,x) => { var c = new StringBuilder(256); GetClassNameW(h, c, 256); if (c.ToString() == cls) { uint p; GetWindowThreadProcessId(h, out p); l.Add(p); } return true; }, IntPtr.Zero); return l.ToArray(); } }
'@
  }
  if (-not $Suffix) { return }   # never match the real, unsuffixed class
  foreach ($tp in [CkWin]::Pids("OneKeyMainWindow$Suffix")) { "  (cleanup: killing leftover test instance pid $tp)"; Stop-Process -Id $tp -Force -ErrorAction Ignore }
}
