# Memory and handle measurement (T12). For 3, 30 and 99 dummy items:
#   locked idle -> unlock (timed) -> unlocked idle -> 100x (search+clear, open edit+back, settings open+close) -> lock
# sampling Working Set, Private Bytes, GDI objects, USER objects and handles every -SampleSeconds after a warm-up.
# Also times the master change (full re-encryption) once per size: the time until the SUCCESS UI is seen (the toast
# "... changed" (Korean) while the list is shown, and no box); an error box is a failure (Codex V47-1). After it, in the
# SAME process, the app is locked - the lock screen must really be up (lock field, no list row) before anything is typed -
# and unlocked with the new master (ME-n-7; the same process unlocks from the secret kept in memory, so this does not
# prove the file on disk). Then the app is restarted and unlocked with the new master from the file on disk (ME-n-8).
# -SkipLockForSelfTest leaves the lock click out: ME-n-7 must then FAIL (checks the check, Codex 0.2.48 review). A negative case
# (ONEKEY_TEST_FAIL=save:fail, first size only, ME-neg) checks that a failed save is NOT counted as a finished change.
# Writes a CSV next to the scratch config.
# Uses its own config folder and test suffix; the real 1Key and its settings are not touched.
# Judgement: tools\tests\lib\Check.ps1 (T18): per size, GDI/USER objects and handles must not keep rising across the
# 100 repetitions (end of the repetitions vs. right after the first 10), and private bytes must not grow by more than 8 MB.
# The absolute numbers are a report, not a pass mark. ASCII only. Default run: about 40 minutes.
param([double]$IdleMinutes = 5, [int]$SampleSeconds = 30, [int[]]$Sizes = @(3, 30, 99), [string]$Exe = "", [int]$ExtraRounds = 0, [switch]$SkipLockForSelfTest)
# -ExtraRounds N: after the first 100 repetitions, N more rounds of 100 with a sample after each (plateau or steady growth?)
$ErrorActionPreference = "Continue"
Add-Type -AssemblyName System.Security
. (Join-Path $PSScriptRoot "lib\Check.ps1")
$req = @("ME-neg"); foreach ($n in $Sizes) { foreach ($k in 0..8) { $req += "ME-$n-$k" } }
Start-Checks -Required $req

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$suffix = ".me"
$csv = "$sp\mem_" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".csv"
Add-Type @'
using System;using System.Runtime.InteropServices;using System.Text;
public class U {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int hh, uint f);
  [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr hProcess, uint flags);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, StringBuilder l);
  public static string Text(IntPtr h) { var sb = new StringBuilder(1024); SendMessageW(h, 0x000D, (IntPtr)1024, sb); return sb.ToString(); }
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static IntPtr FindCls(uint pid, string cls) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls && IsWindowVisible(h)) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
}
'@
function SetText($id, $s) { [void][U]::SendMessageW([U]::GetDlgItem($m, $id), 0x000C, [IntPtr]::Zero, [string]$s) }
function Click($id, $ms = 250) { [void][U]::PostMessageW([U]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds $ms }
function Has($id) { [U]::GetDlgItem($m, $id) -ne [IntPtr]::Zero }
function WaitFor($id, $ms) { $sw = [Diagnostics.Stopwatch]::StartNew(); while (-not (Has $id) -and $sw.ElapsedMilliseconds -lt $ms) { Start-Sleep -Milliseconds 20 }; $sw.ElapsedMilliseconds }
function CloseBox($cmd = 2) { for ($i=0;$i -lt 20;$i++) { $b = [U]::FindCls([uint32]$p.Id, "OneKeyDialog"); if ($b -ne [IntPtr]::Zero) { [void][U]::PostMessageW($b, 0x0111, [IntPtr]$cmd, [IntPtr]::Zero); Start-Sleep -Milliseconds 200; return $true }; Start-Sleep -Milliseconds 50 }; $false }
# CSV: one row per metric (Metric/Value/Unit), with the exe version and SHA-256 so the numbers can be tied to a build.
# Sizes are MiB (1024*1024). Codex review 2026-09-29.
$ExeVersion = (Get-Item $exe).VersionInfo.FileVersion; $ExeSha = (Get-FileHash $exe -Algorithm SHA256).Hash
function Row($size, $phase, $metric, $value, $unit) {
  [pscustomobject]@{ Size = $size; Phase = $phase; Time = (Get-Date -Format "HH:mm:ss"); Metric = $metric; Value = $value; Unit = $unit; ExeVersion = $ExeVersion; ExeSha256 = $ExeSha } |
    Export-Csv -Path $csv -Append -NoTypeInformation -Encoding ASCII
}
function Sample($size, $phase) {
  $p.Refresh()
  $o = [pscustomobject]@{ Size = $size; Phase = $phase; Time = (Get-Date -Format "HH:mm:ss")
    WorkingSetMB = [Math]::Round($p.WorkingSet64 / 1MB, 2); PrivateMB = [Math]::Round($p.PrivateMemorySize64 / 1MB, 2)
    Gdi = [U]::GetGuiResources($p.Handle, 0); User = [U]::GetGuiResources($p.Handle, 1); Handles = $p.HandleCount }
  foreach ($k in @(@("WorkingSet", $o.WorkingSetMB, "MiB"), @("PrivateBytes", $o.PrivateMB, "MiB"), @("GdiObjects", $o.Gdi, "count"), @("UserObjects", $o.User, "count"), @("Handles", $o.Handles, "count"))) { Row $size $phase $k[0] $k[1] $k[2] }
  "   {0,3} {1,-22} WS {2,7} MiB  private {3,7} MiB  GDI {4,4}  USER {5,4}  handles {6,4}" -f $size, $phase, $o.WorkingSetMB, $o.PrivateMB, $o.Gdi, $o.User, $o.Handles | Out-Host
  $o
}
function Idle($size, $phase) { $n = [Math]::Max(1, [int]($IdleMinutes * 60 / $SampleSeconds)); $last = $null; for ($i = 1; $i -le $n; $i++) { Start-Sleep -Seconds $SampleSeconds; $last = Sample $size "$phase $i/$n" }; $last }
function Launch() {
  $script:p = Start-Process $exe -PassThru
  $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $script:m = [U]::FindCls([uint32]$script:p.Id, "OneKeyMainWindow$suffix"); if ($script:m -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 100 }
  if ($script:m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 600
  [void][U]::SetWindowPos($script:m, [IntPtr]::Zero, 1400, 60, 0, 0, 0x0001 -bor 0x0004 -bor 0x0010)
}
# Master change from the list: settings > security > fields > change. Result "ok" only when the success toast (text
# contains the Korean for "changed", U+BC14 U+AFE8 U+C2B5 U+B2C8 U+B2E4) is up while the settings are shown (0.3.76: back where it was opened) and no box is;
# "box: <text>" when a box appears (error); "timeout: ..." after 10 s. Ms = time until that was seen (not the pure
# re-encryption time: it includes message delivery and 20 ms polling).
function MasterChange($old, $new) {
  Click 220 300; Click 213 400
  SetText 501 $old; SetText 502 $new; SetText 503 $new
  $sw = [Diagnostics.Stopwatch]::StartNew(); Click 505 0
  $res = $null
  while (-not $res -and $sw.ElapsedMilliseconds -lt 10000) {
    $b = [U]::FindCls([uint32]$p.Id, "OneKeyDialog")
    $t = [U]::FindCls([uint32]$p.Id, "OneKeyToast")
    if ($b -ne [IntPtr]::Zero) { $res = "box: " + [U]::Text([U]::GetDlgItem($b, 101)); [void][U]::PostMessageW($b, 0x0111, [IntPtr]1, [IntPtr]::Zero) }
    elseif ($t -ne [IntPtr]::Zero -and ([U]::Text([U]::GetDlgItem($t, 101)) -match '\uBC14\uAFE8\uC2B5\uB2C8\uB2E4') -and (Has 213) -and -not (Has 501)) { $res = "ok" }
    else { Start-Sleep -Milliseconds 20 }
  }
  $ms = $sw.ElapsedMilliseconds
  if (-not $res) { $res = "timeout: list=$(Has 1000) master screen=$(Has 501) lock=$(Has 101)" }
  Start-Sleep -Milliseconds 400
  [pscustomobject]@{ Result = $res; Ms = $ms }
}
function Quit() { [void][U]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i=0; $i -lt 50 -and -not $p.HasExited; $i++) { Start-Sleep -Milliseconds 100 } }
function Once() {
  SetText 230 "Item 1"; Start-Sleep -Milliseconds 120; if (Has 232) { Click 232 150 }
  Click 1000 200; Click 311 200
  Click 220 250; Click 240 250   # settings screen and back
}

try {
  Stop-TestInstances $suffix
  foreach ($n in $Sizes) {
    $cfg = "$sp\mem_cfg_$n"; if (Test-Path $cfg) { Get-ChildItem $cfg -Recurse | Remove-Item -Force -Recurse -ErrorAction Ignore }; New-Item -ItemType Directory -Force $cfg | Out-Null
    $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_WORKAREA = $TallScreen
    # fill n dummy items through the UI, then restart so the measurement starts from a fresh process
    Launch
    SetText 101 "Master1234"; SetText 102 "Master1234"; Click 103 600
    # auto-lock off BEFORE filling: filling 30/99 items takes minutes, and on an unattended PC (system idle already > 10 min)
    # the default 10-minute auto-lock locked the app mid-fill (0.2.58 run, 2026-09-30: "fill stopped at item 9 ... lock field=True")
    Click 220 400; [void][U]::PostMessageW([U]::GetDlgItem($m, 2005), 0x0100, [IntPtr]0x24, [IntPtr]::Zero); Start-Sleep -Milliseconds 200
    Click 2012 400; [void](CloseBox)
    for ($k = 1; $k -le $n; $k++) {
      Click 203 250; Click 4001 250
      if (-not (Has 301)) { throw ("fill stopped at item $k" + ": edit screen did not open (add row=" + (Has 203) + ", lock field=" + (Has 101) + ", window visible=" + [U]::IsWindowVisible($m) + ", process exited=" + $p.HasExited + ")") }
      SetText 301 ("Item {0:D2}" -f $k); SetText 302 ("dummy-$k"); Click 310 350; [void](CloseBox)
      if ($k % 10 -eq 0) { "   fill $k/$n" | Out-Host }
    }
    # (auto-lock was switched off before filling; the measurement also needs it off: a locked app would make the
    # repetitions and the master change run on the lock screen, seen on 2026-09-29: USER 28, master change timed out)
    Quit
    # the setup itself is checked from the saved file: exactly n items with content, auto-lock off
    $hdr = [Text.Encoding]::UTF8.GetString([Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes("$cfg\config.dat"), [Text.Encoding]::UTF8.GetBytes("1Key/approval-password/v1"), 'CurrentUser'))
    $has = @($hdr -split "`n" | Where-Object { $_ -match '^s\d+\.has=1' }).Count
    $al = ([regex]::Match($hdr, '(?m)^autolock=(\d+)')).Groups[1].Value
    Check "ME-$n-0" "$n|0" "$has|$al" "$n items: setup saved n items with auto-lock off (items|autolock)"
    if ("$has|$al" -ne "$n|0") { throw "setup for $n items is incomplete; not measuring" }
    if ($n -eq $Sizes[0]) {
      # negative case: the save fails (test-mode failure injection), so the change must NOT count as finished and the
      # file must stay as it was
      $before = (Get-FileHash "$cfg\config.dat").Hash
      $env:ONEKEY_TEST_FAIL = "save:fail"
      Launch; SetText 101 "Master1234"; Click 103 0; [void](WaitFor 1000 10000)
      $neg = MasterChange "Master1234" "NewMaster-77"
      Quit
      $env:ONEKEY_TEST_FAIL = $null
      $same = (Get-FileHash "$cfg\config.dat").Hash -eq $before
      Check "ME-neg" "box|True" ("$(($neg.Result -split ':')[0])|$same") "a failed save is reported as a failure, not as a finished change, and the file is unchanged ($($neg.Result))"
    }
    Launch
    Start-Sleep -Seconds 5; [void](Sample $n "locked warm-up")
    [void](Idle $n "locked idle")
    SetText 101 "Master1234"
    # wait for the first row: the add row 203 is gone when all 99 slots are used
    $sw = [Diagnostics.Stopwatch]::StartNew(); Click 103 0; [void](WaitFor 1000 10000); $unlockMs = $sw.ElapsedMilliseconds
    "   {0,3} unlock took {1} ms" -f $n, $unlockMs | Out-Host
    Row $n "unlock" "Duration" $unlockMs "ms"
    Start-Sleep -Seconds 5; [void](Sample $n "unlocked warm-up")
    [void](Idle $n "unlocked idle")
    for ($r = 1; $r -le 10; $r++) { Once }
    $a = Sample $n "after 10 repetitions"
    for ($r = 11; $r -le 100; $r++) { Once }
    Start-Sleep -Seconds 3
    $b = Sample $n "after 100 repetitions"
    Check "ME-$n-5" $true ((Has 1000) -and -not (Has 101)) "$n items: still unlocked on the list after the repetitions (measurement valid)"
    Check "ME-$n-1" $true (($b.Gdi - $a.Gdi) -le 10) "$n items: GDI objects stay flat over 90 repetitions ($($a.Gdi) -> $($b.Gdi))"
    Check "ME-$n-2" $true (($b.User - $a.User) -le 10) "$n items: USER objects stay flat ($($a.User) -> $($b.User))"
    Check "ME-$n-3" $true (($b.Handles - $a.Handles) -le 20) "$n items: handles stay flat ($($a.Handles) -> $($b.Handles))"
    Check "ME-$n-4" $true (($b.PrivateMB - $a.PrivateMB) -le 8) "$n items: private bytes grow at most 8 MB ($($a.PrivateMB) -> $($b.PrivateMB) MB)"
    for ($x = 1; $x -le $ExtraRounds; $x++) { for ($r = 1; $r -le 100; $r++) { Once }; Start-Sleep -Seconds 3; [void](Sample $n "after $(100 + 100 * $x) repetitions") }
    # master change (full re-encryption): time until the success UI is seen
    $mc = MasterChange "Master1234" "NewMaster-77"
    Check "ME-$n-6" "ok" $mc.Result "$n items: master change shows the success toast on the list, no error box ($($mc.Ms) ms)"
    "   {0,3} master change until the success UI: {1} ms" -f $n, $mc.Ms | Out-Host
    Row $n "master change" "UntilSuccessUi" $mc.Ms "ms"
    # same process: lock, make sure the lock screen is really up, then unlock with the new master
    if (-not $SkipLockForSelfTest) { Click 2014 600 }
    $locked = (Has 101) -and -not (Has 1000)
    if ($locked) { SetText 101 "NewMaster-77"; Click 103 0; [void](WaitFor 1000 10000) }
    Check "ME-$n-7" "True|True|False" ("$locked|$(Has 1000)|$(Has 101)") "$n items: the lock screen came up (lock field, no list) and the new master unlocks it in the same process (locked|list|lock field)"
    Click 2014 400
    Start-Sleep -Seconds 3; [void](Sample $n "locked again")
    Quit
    # restart: the new master opens the re-encrypted file from disk
    Launch
    $fresh = (Has 101) -and -not (Has 1000)
    if ($fresh) { SetText 101 "NewMaster-77"; Click 103 0; [void](WaitFor 1000 10000) }
    Check "ME-$n-8" "True|True|False" ("$fresh|$(Has 1000)|$(Has 101)") "$n items: after a restart the new master opens the file from disk (lock screen first|list|lock field)"
    Quit
  }
  "CSV: $csv"
} catch { Add-Failure ("exception: " + $_) }
finally { $env:ONEKEY_TEST_WORKAREA = $null; Stop-TestInstances $suffix }
Complete-Checks
