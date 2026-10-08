# Resources of the modal effects and toasts (Codex reviews 2026-09-29 22:26 section 6 and 23:26 V47-2; 0.2.48).
# On the settings screen (tallest screen): warm-up, then -Batches batches of -PerBatch help-window openings (each with
# the blurred backdrop), and -Batches batches of -ToastPerBatch "settings saved" toasts. A new save comes while the
# previous toast is still up (it replaces it); each save is only counted when it is known to be done: back on the list,
# the saved file has the new "start minimized" value, and the app's toast serial number (test-mode message WM_APP+10)
# went up by exactly one, with a toast visible (an old toast or a reused window handle cannot pass).
# Records Private Bytes, Working Set, GDI / USER objects and handles at the start, while a box / toast is up (every
# 10th time: the SAMPLED maximum, not every moment) and at the end of every batch; also the time from the click to the
# visible box (measured from outside by polling, about 10 ms resolution) and the backdrop's pure pixel size (client
# width x height x 4 bytes, not the process total).
# Two modes, reported separately (Codex V47 3):
#   default  "cleanup diagnosis": at the end of each batch the app is asked for a full garbage collection (test-mode
#            message WM_APP+9, answers with the managed heap in KiB). Growth that stays after it is objects or resources
#            still held. This changes when the runtime collects, so it says nothing about normal peak use.
#   -NoGc    "normal use": no forced collection. Records the batch-end values, the sampled maximum and the natural GC
#            counts (test-mode message WM_APP+11, generation 0/1/2) so growth can be related to collections.
# Judgement (Check.ps1): FM00 the test hooks answer and there are at least 2 batches; every opening shows the box with
# exactly one backdrop, every save a new toast; GDI / USER / handles after the last batch are within 3 / 3 / 10 of the
# first batch; default mode only: private bytes grow at most 8 MB and the managed heap after GC at most 256 KiB (in -NoGc
# mode memory is a report, not judged); no box, backdrop or toast window is left at the end. The absolute numbers and
# times are a report, not a pass mark. CSV next to the scratch config. Own config folder and test suffix; the real 1Key
# is not touched. ASCII only. About 6 minutes with the defaults.
param([string]$Exe = "", [int]$Batches = 3, [int]$PerBatch = 50, [int]$ToastPerBatch = 30, [string]$WorkArea = "", [switch]$NoGc)
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
$req = @("FM00", "FM01", "FM02", "FM04", "FM05", "FM07"); if (-not $NoGc) { $req += @("FM03", "FM06") }
Start-Checks -Required $req

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$suffix = ".fm"
$cfg = "$sp\fxmem_cfg"; if (Test-Path $cfg) { Get-ChildItem $cfg -Recurse | Remove-Item -Force -Recurse -ErrorAction Ignore }; New-Item -ItemType Directory -Force $cfg | Out-Null
$mode = if ($NoGc) { "nogc" } else { "gc" }
$csv = "$sp\fxmem_${mode}_" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".csv"
Add-Type @'
using System;using System.Collections.Generic;using System.Runtime.InteropServices;using System.Text;
public class MU {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr hProcess, uint flags);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int l, t, r, b; }
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static List<IntPtr> All(uint pid, string cls, bool visibleOnly) { var f = new List<IntPtr>(); EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls && (!visibleOnly || IsWindowVisible(h))) f.Add(h); return true; }, IntPtr.Zero); return f; }
}
'@
function SetText($id, $s) { [void][MU]::SendMessageW([MU]::GetDlgItem($m, $id), 0x000C, [IntPtr]::Zero, [string]$s) }
function Click($id, $ms = 600) { [void][MU]::PostMessageW([MU]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); if ($ms -gt 0) { Start-Sleep -Milliseconds $ms } }
function Key($id, $vk) { [void][MU]::PostMessageW([MU]::GetDlgItem($m, $id), 0x0100, [IntPtr]$vk, [IntPtr]::Zero); Start-Sleep -Milliseconds 300 }
function Has($id) { [MU]::GetDlgItem($m, $id) -ne [IntPtr]::Zero }
function Count($cls, $vis = $true) { @([MU]::All([uint32]$p.Id, $cls, $vis)).Count }
function FirstBox() { $b = @([MU]::All([uint32]$p.Id, "OneKeyDialog", $true)); if ($b.Count -gt 0) { $b[0] } else { [IntPtr]::Zero } }
function Answer($b, $cmd) { if ($b -ne [IntPtr]::Zero) { [void][MU]::PostMessageW($b, 0x0111, [IntPtr]$cmd, [IntPtr]::Zero) } }
function WaitBox($ms) { $sw = [Diagnostics.Stopwatch]::StartNew(); while ($sw.ElapsedMilliseconds -lt $ms) { $b = FirstBox; if ($b -ne [IntPtr]::Zero) { return $b }; Start-Sleep -Milliseconds 5 }; [IntPtr]::Zero }
function WaitGone($ms) { $sw = [Diagnostics.Stopwatch]::StartNew(); while ($sw.ElapsedMilliseconds -lt $ms -and (Count "OneKeyDialog" $false) -gt 0) { Start-Sleep -Milliseconds 10 } }
# test-mode messages: 0x8009 full GC -> managed KiB, 0x800A toast serial, 0x800B natural GC counts (gen0 | gen1<<20 | gen2<<40, bit 60 = answered)
function Ask([uint32]$msg) { [int64][MU]::SendMessageW($m, $msg, [IntPtr]::Zero, [IntPtr]::Zero) }
function ToastSerial() { Ask 0x800A }
function GcCounts() { $v = Ask 0x800B; [pscustomobject]@{ G0 = $v -band 0xFFFFF; G1 = ($v -shr 20) -band 0xFFFFF; G2 = ($v -shr 40) -band 0xFFFFF } }
function StartMin() { try { ([regex]::Match((Get-TestHeader $cfg), '(?m)^startmin=(\d)')).Groups[1].Value } catch { "?" } }
$ExeVersion = (Get-Item $exe).VersionInfo.FileVersion; $ExeSha = (Get-FileHash $exe -Algorithm SHA256).Hash
function Row($phase, $metric, $value, $unit) {
  [pscustomobject]@{ Mode = $mode; Phase = $phase; Time = (Get-Date -Format "HH:mm:ss"); Metric = $metric; Value = $value; Unit = $unit; ExeVersion = $ExeVersion; ExeSha256 = $ExeSha } |
    Export-Csv -Path $csv -Append -NoTypeInformation -Encoding ASCII
}
# -End: a batch end. Default mode forces a GC first (managed heap after GC); -NoGc mode only reads the natural GC counts.
function Sample($phase, [bool]$quiet = $false, [switch]$End) {
  $mk = -1; $gcText = ""
  if ($End -and -not $NoGc) { $mk = Ask 0x8009; Row $phase "ManagedHeapAfterForcedGc" $mk "KiB"; $gcText = "  managed $mk KiB (after forced GC)"; Start-Sleep -Milliseconds 300 }
  if ($End -and $NoGc) { $g = GcCounts; Row $phase "NaturalGcGen0" $g.G0 "count"; Row $phase "NaturalGcGen1" $g.G1 "count"; Row $phase "NaturalGcGen2" $g.G2 "count"; $gcText = "  natural GCs gen0 $($g.G0) gen1 $($g.G1) gen2 $($g.G2)" }
  $p.Refresh()
  $o = [pscustomobject]@{ Phase = $phase; Managed = $mk; WS = [Math]::Round($p.WorkingSet64 / 1MB, 2); Private = [Math]::Round($p.PrivateMemorySize64 / 1MB, 2)
    Gdi = [MU]::GetGuiResources($p.Handle, 0); User = [MU]::GetGuiResources($p.Handle, 1); Handles = $p.HandleCount }
  foreach ($k in @(@("WorkingSet", $o.WS, "MiB"), @("PrivateBytes", $o.Private, "MiB"), @("GdiObjects", $o.Gdi, "count"), @("UserObjects", $o.User, "count"), @("Handles", $o.Handles, "count"))) { Row $phase $k[0] $k[1] $k[2] }
  if (-not $quiet) { "   {0,-28} WS {1,7} MiB  private {2,7} MiB  GDI {3,4}  USER {4,4}  handles {5,4}{6}" -f $phase, $o.WS, $o.Private, $o.Gdi, $o.User, $o.Handles, $gcText | Out-Host }
  $o
}
function MaxOf($list, $name) { ($list | Measure-Object -Property $name -Maximum).Maximum }
function Launch() {
  $script:p = Start-Process $exe -PassThru
  $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $w = @([MU]::All([uint32]$script:p.Id, "OneKeyMainWindow$suffix", $true)); if ($w.Count -gt 0) { $script:m = $w[0]; break }; Start-Sleep -Milliseconds 100 }
  if ($script:m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 700
}
function Quit() { [void][MU]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i=0; $i -lt 50 -and -not $p.HasExited; $i++) { Start-Sleep -Milliseconds 100 } }

try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix
  $env:ONEKEY_TEST_WORKAREA = if ($WorkArea) { $WorkArea } else { $TallScreen }; $env:ONEKEY_TEST_FAIL = $null; $env:ONEKEY_TEST_THEME = $null
  Launch
  SetText 101 "Master1234"; SetText 102 "Master1234"; Click 103; Answer (WaitBox 1500) 1; Start-Sleep -Milliseconds 500
  Click 220; Key 2005 0x24; Click 2012 3500   # auto-lock off: nobody uses the PC during the run
  Confirm-AutoLockOff $cfg
  # the measurement is only meaningful when the test hooks answer (0 = a build without them) and batches can be compared
  $hooks = if ($NoGc) { (ToastSerial) -gt 0 -and (((Ask 0x800B) -shr 60) -band 1) -eq 1 } else { (ToastSerial) -gt 0 -and (Ask 0x8009) -gt 0 }
  Check FM00 "True|True" ("$hooks|$($Batches -ge 2)") "the test-mode hooks answer (toast serial, $(if ($NoGc) { 'GC counts' } else { 'forced GC' })) and there are at least 2 batches to compare ($Batches)"
  if (-not $hooks -or $Batches -lt 2) { throw "cannot measure: test hooks missing or fewer than 2 batches" }
  "   mode: $(if ($NoGc) { 'normal use (no forced GC)' } else { 'cleanup diagnosis (forced GC at each batch end)' })" | Out-Host
  Click 220
  $rc = New-Object MU+RECT; [void][MU]::GetClientRect($m, [ref]$rc)
  $pix = [Math]::Round($rc.r * $rc.b * 4 / 1MB, 2)
  "   settings screen client {0} x {1} px: backdrop pixels {2} MiB (width x height x 4, not the process total)" -f $rc.r, $rc.b, $pix | Out-Host
  Row "setup" "BackdropPixels" $pix "MiB"; Row "setup" "ClientWidth" $rc.r "px"; Row "setup" "ClientHeight" $rc.b "px"

  # ---- help window + backdrop
  for ($r = 0; $r -lt 10; $r++) { Click 250 0; Answer (WaitBox 2000) 2; WaitGone 1000 }   # warm-up
  Start-Sleep -Seconds 2; $start = Sample "modal start" -End
  $after = @(); $open = @(); $times = @(); $missing = 0
  for ($bt = 1; $bt -le $Batches; $bt++) {
    for ($r = 1; $r -le $PerBatch; $r++) {
      $sw = [Diagnostics.Stopwatch]::StartNew(); Click 250 0
      $b = WaitBox 3000; $ms = $sw.ElapsedMilliseconds
      if ($b -eq [IntPtr]::Zero -or (Count "OneKeyBackdrop") -ne 1) { $missing++ } else { $times += $ms }
      if ($r % 10 -eq 0) { $open += Sample "modal open b$bt r$r" $true }
      Answer $b 2; WaitGone 1000
    }
    Start-Sleep -Seconds 2
    $after += Sample "modal after batch $bt" -End
  }
  $st = $times | Sort-Object
  $med = if ($st.Count -gt 0) { $st[[int]($st.Count / 2)] } else { -1 }; $mx = if ($st.Count -gt 0) { $st[-1] } else { -1 }
  Row "modal" "OpenTimeMedian" $med "ms"; Row "modal" "OpenTimeMax" $mx "ms"
  "   modal open: {0} times, median {1} ms, max {2} ms (click to visible box, outside polling)" -f $st.Count, $med, $mx | Out-Host
  "   modal open, sampled max (every 10th opening): private {0} MiB, WS {1} MiB, GDI {2}, USER {3}, handles {4}" -f (MaxOf $open Private), (MaxOf $open WS), (MaxOf $open Gdi), (MaxOf $open User), (MaxOf $open Handles) | Out-Host
  $a1 = $after[0]; $aN = $after[-1]
  Check FM01 0 $missing "every one of $($Batches * $PerBatch) openings showed the box with exactly one backdrop"
  Check FM02 $true (($aN.Gdi - $a1.Gdi) -le 3 -and ($aN.User - $a1.User) -le 3 -and ($aN.Handles - $a1.Handles) -le 10) "modal batches: GDI $($a1.Gdi)->$($aN.Gdi), USER $($a1.User)->$($aN.User), handles $($a1.Handles)->$($aN.Handles) flat from batch 1 to $Batches"
  if (-not $NoGc) { Check FM03 $true (($aN.Private - $a1.Private) -le 8 -and ($aN.Managed - $a1.Managed) -le 256) "modal batches after forced GC: private bytes $($a1.Private) -> $($aN.Private) MiB (at most +8), managed heap $($a1.Managed) -> $($aN.Managed) KiB (at most +256)" }

  # ---- toasts ("settings saved"); the settings screen is open, each save goes back to the list
  Click 241
  $tStart = Sample "toast start" -End
  $tAfter = @(); $tOpen = @(); $bad = @()
  $want = StartMin
  for ($bt = 1; $bt -le $Batches; $bt++) {
    for ($r = 1; $r -le $ToastPerBatch; $r++) {
      $want = if ($want -eq "1") { "0" } else { "1" }
      $serial = ToastSerial
      Click 220 500; Key 2002 0x20; Click 2012 0
      # done = back on the list, the file has the new value, exactly one new toast, and a toast is visible
      $sw = [Diagnostics.Stopwatch]::StartNew(); $state = ""
      while ($sw.ElapsedMilliseconds -lt 3000) {
        $state = "list=$(Has 203) startmin=$(StartMin)/$want serial+$((ToastSerial) - $serial) visible=$(Count 'OneKeyToast')"
        if ($state -eq "list=True startmin=$want/$want serial+1 visible=1") { break }
        Start-Sleep -Milliseconds 20
      }
      if ($state -ne "list=True startmin=$want/$want serial+1 visible=1") { $bad += "b$bt r$r $state" }
      if ($r % 10 -eq 0) { $tOpen += Sample "toast up b$bt r$r" $true }
      Start-Sleep -Milliseconds 300   # the next save comes while this toast is still up: it replaces it
    }
    Start-Sleep -Seconds 4   # the last toast has faded out
    $tAfter += Sample "toast after batch $bt" -End
  }
  "   toast up, sampled max (every 10th save): private {0} MiB, GDI {1}, USER {2}, handles {3}" -f (MaxOf $tOpen Private), (MaxOf $tOpen Gdi), (MaxOf $tOpen User), (MaxOf $tOpen Handles) | Out-Host
  $t1 = $tAfter[0]; $tN = $tAfter[-1]
  Check FM04 "" (($bad | Select-Object -First 5) -join "; ") "every one of $($Batches * $ToastPerBatch) saves was done (list, saved value) and showed exactly one NEW toast (toast serial +1)"
  Check FM05 $true (($tN.Gdi - $t1.Gdi) -le 3 -and ($tN.User - $t1.User) -le 3 -and ($tN.Handles - $t1.Handles) -le 10) "toast batches: GDI $($t1.Gdi)->$($tN.Gdi), USER $($t1.User)->$($tN.User), handles $($t1.Handles)->$($tN.Handles) flat"
  if (-not $NoGc) { Check FM06 $true (($tN.Private - $t1.Private) -le 8 -and ($tN.Managed - $t1.Managed) -le 256) "toast batches after forced GC: private bytes $($t1.Private) -> $($tN.Private) MiB (at most +8), managed heap $($t1.Managed) -> $($tN.Managed) KiB (at most +256)" }
  Check FM07 "0|0|0" ("$(Count 'OneKeyDialog' $false)|$(Count 'OneKeyBackdrop' $false)|$(Count 'OneKeyToast' $false)") "nothing left at the end: no box, backdrop or toast window (also hidden ones)"
  Quit
  "CSV: $csv"
} catch { Add-Failure ("exception: " + $_) }
finally { $env:ONEKEY_TEST_WORKAREA = $null; Stop-TestInstances $suffix }
Complete-Checks
