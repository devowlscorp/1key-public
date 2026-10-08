# Lock widget input with the CLIPBOARD and the KOREAN IME (Codex 2026-10-04 12:41 condition 1, 0.2.172). These change state the user
# owns, so the script runs only with -UserApproved (2026-10-04: the user approved clipboard and Korean-input checks) and restores:
#   - the clipboard: saved before, put back after (text and other formats Windows can copy; the dummy value is marked
#     ExcludeClipboardContentFromMonitorProcessing so it stays out of the clipboard history), nothing of it is printed or logged
#   - CapsLock and the Korean/English mode of the widget field: put back to what they were
# IX01 Ctrl+V of the dummy master into the narrow widget field: the whole value arrives once (length), the widget widens; Enter unlocks
# IX02 CapsLock: the drawn indicator follows the real CapsLock (off -> on -> off) - read from a test-only window property
# IX03 Korean IME: the field starts in English (1Key switches it on focus); on this PC the Hangul key did not switch the widget's
#      password field to Korean (an observation of this Windows/IME, not a rule for every IME), the indicator stays English, two
#      characters reach the field, Enter makes exactly one processed unlock request (test-only counter) and one error box; the mode is
#      put back if it ever changed
# IX02 also requires the real flip: CapsLock c0 -> 1-c0 -> c0 (Codex 13:38 R172-T1)
# Real keys only after checking 1Key is the foreground process. Own config folder and suffix; the real 1Key is not touched. ASCII only.
param([string]$Exe = "", [switch]$UserApproved)
$ErrorActionPreference = "Continue"
if (-not $UserApproved) { "inputext.ps1 uses the clipboard and the Korean IME. Run it only with -UserApproved after the user agreed."; exit 2 }
. (Join-Path $PSScriptRoot "..\lib\Check.ps1")
Start-Checks -Required @("IX01", "IX02", "IX03")
$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$suffix = ".ix"
$cfg = "$sp\inputext_cfg"; if (Test-Path $cfg) { Remove-Item $cfg -Recurse -Force }; New-Item -ItemType Directory -Force $cfg | Out-Null
$master = "Ix-Dummy-4821"
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;using System.Collections.Generic;using System.Runtime.InteropServices;using System.Text;
public class IX {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern short GetKeyState(int vk);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr GetPropW(IntPtr h, string name);
  [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint tid);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static IntPtr MainWnd(uint pid, string cls) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls) { f = h; return false; } return true; }, IntPtr.Zero); return f; }   // hidden too (widget mode hides it)
  public static List<IntPtr> All(uint pid, string cls) { var f = new List<IntPtr>(); EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls && IsWindowVisible(h)) f.Add(h); return true; }, IntPtr.Zero); return f; }
  public static IntPtr ChildOf(IntPtr p, string cls) { IntPtr r = IntPtr.Zero; EnumChildWindows(p, (h,l) => { if (Cls(h) == cls) { r = h; return false; } return true; }, IntPtr.Zero); return r; }
  public static bool Front(IntPtr h) { keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero); return SetForegroundWindow(h); }
  public static uint FgPid() { uint p; GetWindowThreadProcessId(GetForegroundWindow(), out p); return p; }
  public static uint Guarded;
  static void G() { if (FgPid() != Guarded) throw new Exception("foreground is not 1Key - stopped before a key reached another program"); }
  public static void Key(byte vk) { G(); keybd_event(vk, 0, 0, UIntPtr.Zero); keybd_event(vk, 0, 2, UIntPtr.Zero); System.Threading.Thread.Sleep(40); }
  public static void KeyX(byte vk) { G(); keybd_event(vk, 0, 1, UIntPtr.Zero); keybd_event(vk, 0, 3, UIntPtr.Zero); System.Threading.Thread.Sleep(40); }
  public static void Chord(byte mod, byte vk) { G(); keybd_event(mod, 0, 0, UIntPtr.Zero); keybd_event(vk, 0, 0, UIntPtr.Zero); keybd_event(vk, 0, 2, UIntPtr.Zero); keybd_event(mod, 0, 2, UIntPtr.Zero); System.Threading.Thread.Sleep(60); }
  public static int Len(IntPtr e) { return (int)SendMessageW(e, 0x00C1, IntPtr.Zero, IntPtr.Zero); }
  public static int Kb(IntPtr w) { return (int)GetPropW(w, "OneKeyTestKb"); }   // 4 = valid, +1 Korean, +2 Caps
  public static int LangOf(IntPtr w) { uint p; uint tid = GetWindowThreadProcessId(w, out p); return (int)((long)GetKeyboardLayout(tid) & 0xFFFF); }
}
'@
function Launch() {
  $script:p = Start-Process $exe -PassThru; $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $script:m = [IX]::MainWnd([uint32]$script:p.Id, "OneKeyMainWindow$suffix"); if ($script:m -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 100 }
  if ($script:m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 1200
  [IX]::Guarded = [uint32]$script:p.Id
}
function Quit() { if ($script:p -and -not $script:p.HasExited) { [void][IX]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 1200; if (-not $script:p.HasExited) { Stop-Process -Id $script:p.Id -Force } } }
function Widget() { @([IX]::All([uint32]$p.Id, "OneKeyLockWidget")) | Select-Object -First 1 }
function Field() { foreach ($h in [IX]::All([uint32]$p.Id, "OneKeyLockInput")) { $e = [IX]::ChildOf($h, "Edit"); if ([IX]::GetDlgCtrlID($e) -eq 101) { return $e } }; [IntPtr]::Zero }
function HostW() { $h = @([IX]::All([uint32]$p.Id, "OneKeyLockInput"))[0]; $r = New-Object IX+RECT; [void][IX]::GetWindowRect($h, [ref]$r); $r.R - $r.L }
function Boxes() { @([IX]::All([uint32]$p.Id, "OneKeyDialog")).Count }
function Unlocked() { [IX]::GetDlgItem($m, 101) -eq [IntPtr]::Zero -and [IX]::GetDlgItem($m, 2014) -ne [IntPtr]::Zero }

$savedClip = $null; $clipSaved = $false; $caps0 = [IX]::GetKeyState(0x14) -band 1; $imeToggled = $false; $w = [IntPtr]::Zero
try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_NO_AUTOLOCK = "1"
  # the dummy master on the old screen
  $env:ONEKEY_TEST_LOCKWIDGET = "0"
  Launch
  [void][IX]::SendMessageW([IX]::GetDlgItem($m, 101), 0x000C, [IntPtr]::Zero, $master); [void][IX]::SendMessageW([IX]::GetDlgItem($m, 102), 0x000C, [IntPtr]::Zero, $master)
  [void][IX]::PostMessageW([IX]::GetDlgItem($m, 103), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 1500
  $b = @([IX]::All([uint32]$p.Id, "OneKeyDialog")); if ($b.Count) { [void][IX]::PostMessageW($b[0], 0x0111, [IntPtr]6, [IntPtr]::Zero); Start-Sleep -Milliseconds 800 }
  Quit
  $env:ONEKEY_TEST_LOCKWIDGET = "1"

  # IX01: paste
  # a COPY of what is on the clipboard now (GetDataObject alone is a live view that would show the dummy value later)
  $live = [Windows.Forms.Clipboard]::GetDataObject(); $savedClip = $null
  if ($live) {
    $savedClip = New-Object Windows.Forms.DataObject
    foreach ($f in $live.GetFormats($false)) { try { $d = $live.GetData($f, $false); if ($null -ne $d) { $savedClip.SetData($f, $d) } } catch { } }
    if ($savedClip.GetFormats().Count -eq 0) { $savedClip = $null }
  }
  $clipSaved = $true
  $do = New-Object Windows.Forms.DataObject
  $do.SetData([Windows.Forms.DataFormats]::UnicodeText, $master)
  $do.SetData("ExcludeClipboardContentFromMonitorProcessing", (New-Object IO.MemoryStream (, [byte[]](0))))
  [Windows.Forms.Clipboard]::SetDataObject($do, $true)
  Launch
  $w = Widget; [void][IX]::Front($w); Start-Sleep -Milliseconds 600
  $w0 = HostW
  [IX]::Chord(0x11, 0x56); Start-Sleep -Milliseconds 600
  $len = [IX]::Len((Field)); $w1 = HostW
  [IX]::Key(0x0D); Start-Sleep -Milliseconds 1500
  $b = @([IX]::All([uint32]$p.Id, "OneKeyDialog")); if ($b.Count) { [void][IX]::PostMessageW($b[0], 0x0111, [IntPtr]1, [IntPtr]::Zero); Start-Sleep -Milliseconds 600 }
  Check IX01 "$($master.Length)|True|True" "$len|$($w1 -gt $w0 + 40)|$(Unlocked)" "Ctrl+V into the narrow widget: whole value once, widens ($w0 -> $w1 px); Enter unlocks"
  [Windows.Forms.Clipboard]::Clear()
  if ($savedClip) { [Windows.Forms.Clipboard]::SetDataObject($savedClip, $true) }; $clipSaved = $false

  # back to the widget
  [void][IX]::PostMessageW([IX]::GetDlgItem($m, 2014), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 1300
  $w = Widget; [void][IX]::Front($w); Start-Sleep -Milliseconds 600
  [IX]::KeyX(0x23); Start-Sleep -Milliseconds 700   # End: widens (indicator shown)

  # IX02: CapsLock indicator
  $k0 = [IX]::Kb($w); $c0 = [IX]::GetKeyState(0x14) -band 1
  [IX]::Key(0x14); Start-Sleep -Milliseconds 700
  $k1 = [IX]::Kb($w); $c1 = [IX]::GetKeyState(0x14) -band 1
  [IX]::Key(0x14); Start-Sleep -Milliseconds 700
  $k2 = [IX]::Kb($w); $c2 = [IX]::GetKeyState(0x14) -band 1
  $show = { param($k, $c) "$(($k -band 4) -ne 0)/$((($k -band 2) -ne 0) -eq ($c -eq 1))" }
  # R172-T1 (Codex 13:38): the key must really flip CapsLock (c1 = 1 - c0) and flip it back (c2 = c0), and the drawn indicator must match
  Check IX02 "$c0|$(1 - $c0)|$c0|True/True|True/True|True/True" "$c0|$c1|$c2|$(& $show $k0 $c0)|$(& $show $k1 $c1)|$(& $show $k2 $c2)" "CapsLock really flips and returns ($c0 -> $c1 -> $c2) and the drawn indicator matches each time"

  # IX03: Korean IME
  $lang = [IX]::LangOf($w)
  if ($lang -ne 0x0412) {
    Check IX03 "korean layout" "layout 0x$($lang.ToString('X4'))" "Korean IME check needs the Korean keyboard layout on this PC - NOT RUN"
  } else {
    $kA = [IX]::Kb($w)
    [IX]::Key(0x15); $imeToggled = $true; Start-Sleep -Milliseconds 700   # Hangul key
    $kB = [IX]::Kb($w)
    $n0 = Boxes
    $s0 = [int][IX]::GetPropW($m, "OneKeyTestSubmits")
    [IX]::Key(0x52); [IX]::Key(0x4B)   # r k
    $typed = [IX]::Len((Field))
    [IX]::Key(0x0D); Start-Sleep -Milliseconds 1500
    $boxes = (Boxes) - $n0
    $subs = [int][IX]::GetPropW($m, "OneKeyTestSubmits") - $s0   # unlock requests 1Key actually processed (test-only counter)
    $b = @([IX]::All([uint32]$p.Id, "OneKeyDialog")); if ($b.Count) { [void][IX]::PostMessageW($b[0], 0x0111, [IntPtr]1, [IntPtr]::Zero); Start-Sleep -Milliseconds 700 }
    $w = Widget; if ($w) { [void][IX]::Front($w); Start-Sleep -Milliseconds 500 }
    $kC = if ($w) { [IX]::Kb($w) } else { 0 }
    if ($w -and ($kC -band 1)) { [IX]::Key(0x15); Start-Sleep -Milliseconds 500 }   # put the mode back
    $imeToggled = $false
    $kD = if ($w) { [IX]::Kb($w) } else { 0 }
    # Observed on this PC (Windows 11 + Korean IME): the Hangul key did not put the widget's password field into Korean mode, so no
    # composition was pending at Enter. This is an observation of this environment, not a guarantee for every IME (Codex 13:38).
    # The submit count comes from a test-only counter in 1Key (OneKeyTestSubmits), not from the number of boxes.
    Check IX03 "0|0|2|1|1|True|0" "$($kA -band 1)|$($kB -band 1)|$typed|$subs|$boxes|$(-not (Unlocked))|$($kD -band 1)" "field starts in English; after the Hangul key the indicator stays English (this PC); two characters reached the field; Enter: 1 unlock request processed, 1 error box, still locked"
  }
  Quit
}
catch { Add-Failure ("aborted: " + $_.Exception.Message) }
finally {
  try { if ($clipSaved) { [Windows.Forms.Clipboard]::Clear(); if ($savedClip) { [Windows.Forms.Clipboard]::SetDataObject($savedClip, $true) } } } catch { }
  if (([IX]::GetKeyState(0x14) -band 1) -ne $caps0) { [IX]::keybd_event(0x14, 0, 0, [UIntPtr]::Zero); [IX]::keybd_event(0x14, 0, 2, [UIntPtr]::Zero) }
  if ($script:p -and -not $script:p.HasExited) { Stop-Process -Id $script:p.Id -Force -ErrorAction Ignore }
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST_LOCKWIDGET = "0"
}
"CapsLock at the end: $([IX]::GetKeyState(0x14) -band 1) (start $caps0)"
Complete-Checks
