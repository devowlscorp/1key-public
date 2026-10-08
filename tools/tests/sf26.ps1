# SF26 input-loss measurement (Codex 15:11 section 6, user decision 2026-10-05 "run it"). A MEASUREMENT, not part of any default
# run: it repeats real typing into a real browser field and keeps every result. Run it only with -Foreground on a PC nobody uses.
#
#   powershell -ExecutionPolicy Bypass -File tools\tests\sf26.ps1 -Foreground [-Exe <1Key.exe>] [-Reps 4]
#
# What it separates (Codex): length x key interval x input method x load, a fixed number of repeats each, the first failure kept.
# - The typed strings are dummy and position-coded ("q000q001q002...": lower case and digits only, so every character is one
#   key and a missing one shows its position).
# - Edge (throw-away profile, its own --user-data-dir) shows a local page (http://localhost:18766/sf26) with one text field. The
#   page counts keydown / keyup / beforeinput / input on that field and shows "sf26|v=<value>|kd=|ku=|bi=|in=" in its title, so
#   the harness reads only the Edge window title. That splits: 1Key's attempt (the string it was asked to type), what the page
#   received (keydown count, beforeinput / input counts) and the final value.
# - 1Key runs isolated (suffix .s26, own config folder), one item per (method, length); the key interval is the global
#   Advanced setting (5 / 20 / 50 ms); load = every core busy in this harness (background runspaces) during the typing.
# - Methods: Auto and Unicode (scan code alone is the same path as Auto for these characters; the window-message method does
#   not reach a browser; the clipboard method is a different mechanism and is not part of this measurement).
# Safety: before every trial the Edge test window must be in front and the mouse must not have moved since the last trial;
# otherwise the run stops (somebody is using the PC). Nothing is read from the user's windows; no real secrets.
# Output: docs\test-logs\<date>_<version>\sf26-<HHmmss>.txt (one line per trial + a table). ASCII only.
param([string]$Exe = "", [int]$Reps = 4, [switch]$Foreground)
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
if (-not $Foreground) { "sf26.ps1 types into a real browser window in front. Run it with -Foreground on a PC nobody is using."; exit 2 }
$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$suffix = ".s26"
$cfg = "$sp\sf26_cfg"; $profileDir = "$sp\sf26_edge"
$edge = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
$port = 18766; $base = "http://localhost:$port"
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, WindowsBase
Add-Type @'
using System;using System.Collections.Generic;using System.Runtime.InteropServices;using System.Text;
public class S6 {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; }
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static string Title(IntPtr h) { var t = new StringBuilder(1024); GetWindowTextW(h, t, 1024); return t.ToString(); }
  public static IntPtr Find(uint pid, string cls) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
  public static IntPtr ByTitle(string cls, string start) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { if (Cls(h) == cls && IsWindowVisible(h) && Title(h).StartsWith(start)) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
  public static string Cursor() { POINT p; GetCursorPos(out p); return p.x + "," + p.y; }
  // bring a window to the front the documented way for a background process: a harmless Alt tap first
  public static bool Front(IntPtr h) { keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero); ShowWindow(h, 9); BringWindowToTop(h); return SetForegroundWindow(h); }
}
'@

$page = @"
<!doctype html><html><head><meta charset="utf-8"><title>sf26|v=|kd=0|ku=0|bi=0|in=0</title></head><body>
<input id="t" aria-label="Field" autocomplete="off" spellcheck="false" style="width:700px"> <button id="reset" aria-label="Reset">reset</button>
<script>var t=document.getElementById('t'),c={kd:0,ku:0,bi:0,inp:0};
function show(){document.title='sf26|v='+t.value+'|kd='+c.kd+'|ku='+c.ku+'|bi='+c.bi+'|in='+c.inp}
t.addEventListener('keydown',function(){c.kd++});t.addEventListener('keyup',function(){c.ku++;show()});
t.addEventListener('beforeinput',function(){c.bi++});t.addEventListener('input',function(){c.inp++;show()});
document.getElementById('reset').addEventListener('click',function(){t.value='';c={kd:0,ku:0,bi:0,inp:0};show();t.focus()});
show();t.focus();</script></body></html>
"@
$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("$base/")
$rs = [runspacefactory]::CreateRunspace(); $rs.Open()
$ps = [powershell]::Create(); $ps.Runspace = $rs
[void]$ps.AddScript({ param($l, $body) while ($l.IsListening) { try { $c = $l.GetContext(); $b = [Text.Encoding]::UTF8.GetBytes($body); $c.Response.ContentType = "text/html; charset=utf-8"; $c.Response.OutputStream.Write($b, 0, $b.Length); $c.Response.Close() } catch { } } }).AddArgument($listener).AddArgument($page)

function SetText($id, $s) { [void][S6]::SendMessageW([S6]::GetDlgItem($m, $id), 0x000C, [IntPtr]::Zero, [string]$s) }
function Click($id, $ms = 600) { [void][S6]::PostMessageW([S6]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds $ms }
function CloseBox($cmd = 2) { for ($i = 0; $i -lt 10; $i++) { $b = [S6]::Find([uint32]$p.Id, "OneKeyDialog"); if ($b -ne [IntPtr]::Zero) { [void][S6]::PostMessageW($b, 0x0111, [IntPtr]$cmd, [IntPtr]::Zero); Start-Sleep -Milliseconds 300; return $true }; Start-Sleep -Milliseconds 100 }; $false }
function UiaById([string]$id) {
  $root = [Windows.Automation.AutomationElement]::FromHandle($script:edgeWnd)
  $root.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty, $id)))
}
function PageState() {
  $t = [S6]::Title($script:edgeWnd)
  $i = $t.IndexOf("sf26|"); if ($i -lt 0) { return $null }
  $t = $t.Substring($i); $j = $t.IndexOf(" - "); if ($j -gt 0) { $t = $t.Substring(0, $j) }   # the value has no " - "; Edge adds " - <profile> - Microsoft Edge"
  $h = @{}; foreach ($part in $t.Split('|')) { $k = $part.IndexOf('='); if ($k -gt 0) { $h[$part.Substring(0, $k)] = $part.Substring($k + 1) } }
  $h
}
function MakeString([int]$len) { $s = ""; for ($i = 0; $s.Length -lt $len; $i++) { $s += "q{0:D3}" -f $i }; $s.Substring(0, $len) }
function Missing([string]$want, [string]$got) {
  # first index where they differ (the first lost or extra character), "-" if equal
  for ($i = 0; $i -lt $want.Length; $i++) { if ($i -ge $got.Length -or $want[$i] -ne $got[$i]) { return "$i" } }
  if ($got.Length -gt $want.Length) { return "extra@$($want.Length)" }
  "-"
}
function SetDelay([int]$ms) {
  Click 220; Click 212
  SetText 401 "$ms"; Click 404 900; Click 240 900   # [Save] returns to the settings (0.3.76), back -> list
}
function StartLoad() {
  $script:busy = @()
  for ($i = 0; $i -lt [Environment]::ProcessorCount; $i++) {
    $r = [runspacefactory]::CreateRunspace(); $r.Open(); $q = [powershell]::Create(); $q.Runspace = $r
    [void]$q.AddScript({ $end = [DateTime]::UtcNow.AddMinutes(30); $x = 0; while ([DateTime]::UtcNow -lt $end) { $x++ } })
    $script:busy += @{ Ps = $q; Rs = $r; H = $q.BeginInvoke() }
  }
  Start-Sleep -Milliseconds 800
}
function StopLoad() { foreach ($b in $script:busy) { try { $b.Ps.Stop(); $b.Ps.Dispose(); $b.Rs.Close() } catch { } }; $script:busy = @() }

$methods = @(@{ Name = "auto"; Down = 0 }, @{ Name = "unicode"; Down = 2 })   # index in the "method" list (0 Auto, 1 scan code, 2 Unicode, 3 window message)
$lengths = @(32, 128)
$delays = @(5, 20, 50)
$loads = @("idle", "cpu")
$rows = @(); $lines = @(); $stopped = ""; $edgeProc = $null; $p = $null
$ver = (Get-Item $exe).VersionInfo.FileVersion -replace '\.0$', ''
$outDir = Join-Path $repo ("docs\test-logs\{0:yyyy-MM-dd}_{1}" -f (Get-Date), $ver); New-Item -ItemType Directory -Force $outDir | Out-Null
$out = Join-Path $outDir ("sf26-{0:HHmmss}.txt" -f (Get-Date))
try {
  Stop-TestInstances $suffix
  if (Test-Path $cfg) { Remove-Item $cfg -Recurse -Force }; New-Item -ItemType Directory -Force $cfg | Out-Null
  $listener.Start(); [void]$ps.BeginInvoke()
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_LOCKWIDGET = "0"; $env:ONEKEY_TEST_NO_AUTOLOCK = "1"
  $p = Start-Process $exe -PassThru
  $m = [IntPtr]::Zero; for ($i = 0; $i -lt 80 -and $m -eq [IntPtr]::Zero; $i++) { $m = [S6]::Find([uint32]$p.Id, "OneKeyMainWindow$suffix"); Start-Sleep -Milliseconds 100 }
  if ($m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 600
  SetText 101 "Master1234"; SetText 102 "Master1234"; Click 103 900; [void](CloseBox 6)
  # items: slot = method index * 2 + length index (no hotkey registered - WM_HOTKEY is posted directly)
  $want = @{}
  $slot = 0
  foreach ($mt in $methods) { foreach ($len in $lengths) {
    $s = MakeString $len
    Click 203; Click 4001; SetText 301 "sf26-$($mt.Name)-$len"; SetText 302 $s
    [void][S6]::PostMessageW([S6]::GetDlgItem($m, 304), 0x0100, [IntPtr]0x08, [IntPtr]::Zero)
    for ($k = 0; $k -lt $mt.Down; $k++) { [void][S6]::PostMessageW([S6]::GetDlgItem($m, 306), 0x0100, [IntPtr]0x28, [IntPtr]::Zero) }
    Start-Sleep -Milliseconds 200; Click 310 900; [void](CloseBox 6)
    $want[$slot] = @{ Text = $s; Method = $mt.Name; Len = $len }; $slot++
  } }
  $edgeProc = Start-Process $edge -ArgumentList @("--user-data-dir=`"$profileDir`"", "--no-first-run", "--no-default-browser-check", "--disable-sync", "--new-window", "$base/sf26") -PassThru
  $script:edgeWnd = [IntPtr]::Zero
  for ($i = 0; $i -lt 100 -and $script:edgeWnd -eq [IntPtr]::Zero; $i++) { Start-Sleep -Milliseconds 200; $script:edgeWnd = [S6]::ByTitle("Chrome_WidgetWin_1", "sf26|") }
  if ($script:edgeWnd -eq [IntPtr]::Zero) { throw "the test page did not open in Edge" }
  Start-Sleep -Milliseconds 1500
  $field = UiaById "t"; $reset = UiaById "reset"
  if (-not $field -or -not $reset) { throw "UIA does not see the page's field" }
  $cursor = [S6]::Cursor()
  foreach ($load in $loads) {
    if ($load -eq "cpu") { StartLoad }
    foreach ($d in $delays) {
      SetDelay $d
      foreach ($slot in 0..($want.Count - 1)) {
        $w = $want[$slot]; $fails = 0; $first = ""
        for ($r = 1; $r -le $Reps; $r++) {
          if ([S6]::Cursor() -ne $cursor) { $stopped = "the mouse moved (somebody is using the PC)"; break }
          [void][S6]::Front($script:edgeWnd); Start-Sleep -Milliseconds 300
          ([Windows.Automation.InvokePattern]$reset.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke(); Start-Sleep -Milliseconds 200
          $field.SetFocus(); Start-Sleep -Milliseconds 200
          if ([S6]::GetForegroundWindow() -ne $script:edgeWnd) { $stopped = "the test page is not in front (somebody is using the PC)"; break }
          $cursor = [S6]::Cursor()
          [void][S6]::PostMessageW($m, 0x0312, [IntPtr]$slot, [IntPtr]::Zero)   # WM_HOTKEY for the slot: 1Key types into the window in front
          $budget = [int]($w.Len * ($d + 15) + 3000); $t0 = [Environment]::TickCount; $last = ""; $stable = 0
          while ([Environment]::TickCount - $t0 -lt $budget) {
            Start-Sleep -Milliseconds 250
            $st = PageState; $cur = if ($st) { $st["v"] } else { "" }
            if ($cur -eq $last -and $cur.Length -gt 0) { $stable++ } else { $stable = 0 }
            $last = $cur
            if ($cur -eq $w.Text -or $stable -ge 6) { break }
          }
          $st = PageState
          $v = if ($st) { $st["v"] } else { "" }
          $ok = $v -eq $w.Text
          $line = "{0,-4} {1,2}ms {2,-7} len={3,3} rep={4} ok={5} got={6,3} kd={7,3} ku={8,3} bi={9,3} in={10,3} firstDiff={11}" -f $load, $d, $w.Method, $w.Len, $r, $ok, $v.Length, $st["kd"], $st["ku"], $st["bi"], $st["in"], (Missing $w.Text $v)
          $lines += $line; $line
          if (-not $ok) { $fails++; if (-not $first) { $first = "rep $r got $($v.Length)/$($w.Len) kd=$($st['kd']) bi=$($st['bi']) in=$($st['in']) firstDiff=$(Missing $w.Text $v)" } }
        }
        $rows += "| $load | $d | $($w.Method) | $($w.Len) | $Reps | $fails | $first |"
        if ($stopped) { break }
      }
      if ($stopped) { break }
    }
    if ($load -eq "cpu") { StopLoad }
    if ($stopped) { break }
  }
} catch { $stopped = "exception: " + $_.Exception.Message }
finally {
  StopLoad
  if ($p -and -not $p.HasExited) { [void][S6]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 1200; if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction Ignore } }
  if ($edgeProc) { Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" | Where-Object { $_.CommandLine -like "*$profileDir*" } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction Ignore } }
  try { $listener.Stop(); $listener.Close() } catch { }
  try { $ps.Dispose(); $rs.Close() } catch { }
}
$failTotal = 0; foreach ($l in $lines) { if ($l -match 'ok=False') { $failTotal++ } }
$report = @("SF26 input-loss measurement - 1Key $ver ($exe)", "SHA-256 $((Get-FileHash $exe).Hash)", "trials: $($lines.Count), not exact: $failTotal" + $(if ($stopped) { ", STOPPED: $stopped" } else { "" }), "",
            "| load | key interval ms | method | length | repeats | not exact | first failure |", "|---|---|---|---|---|---|---|") + $rows + @("", "trials:") + $lines
Set-Content $out -Value $report -Encoding ASCII
"---- sf26: trials $($lines.Count), not exact $failTotal$(if ($stopped) { ", stopped: $stopped" })"
"---- log: $out"
if ($stopped) { exit 2 }
exit $(if ($failTotal -gt 0) { 1 } else { 0 })
