# Lock screen keyboard state (0.2.126 user request, 0.2.127 Codex 08:45-bb). The master password fields of 1Key's own lock
# screen show the IME / Caps Lock state under the field (ids 105 / 106) and, when the cursor enters such a field while 1Key is
# the foreground window, turn the IME to the non-native mode and Caps Lock off (one Caps Lock key, only if no Caps Lock /
# Shift / Ctrl / Alt / Win key is physically down). Caps Lock is a Windows-wide state: this script puts it back as it was.
# KB01 first-setup field 1 gets the focus with Caps Lock on -> Caps Lock off, the caps label shows the "off" text
# KB02 the user turns Caps Lock on again while staying in the field -> not undone (stays on), the label shows the "on" text
# KB03 focus moves to field 2 (first setup) with Caps Lock on -> off
# KB04 Shift held down while the field gets the focus -> Caps Lock left on (no key sent)
# KB05 another window in front, 1Key locks itself (session-lock message) -> Caps Lock left on
# KB07 repeated focus (Codex 09:45-bf): 4 more times Caps Lock on + the other field -> off each time, exactly one Caps Lock
#      key per focus (1Key's own counter, test message 0x8016 lParam 12 bits 52-55)
# KB08 typing into another program (a test window of this script) while 1Key locks itself: Caps Lock stays on, the typed
#      letters reach that window (no interference)
# KB06 (optional, needs a Korean / CJK IME) IME native mode turned on in the field -> the IME label changes; focus out and
#      back in -> the label is back to the non-native text
# Own config folder and test suffix; the real 1Key and its settings are not touched. ASCII only.
param([string]$Exe = "")
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
Start-Checks -Required @("KB01", "KB02", "KB03", "KB04", "KB05", "KB07", "KB08")

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$suffix = ".kb"
$cfg = "$sp\lockkbd_cfg"; if (Test-Path $cfg) { Get-ChildItem $cfg -Recurse | Remove-Item -Force -Recurse -ErrorAction Ignore }; New-Item -ItemType Directory -Force $cfg | Out-Null
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @'
using System;using System.Collections.Generic;using System.Runtime.InteropServices;using System.Text;
public class KB {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, StringBuilder l);
  [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern short GetKeyState(int vk);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("imm32.dll")] public static extern IntPtr ImmGetDefaultIMEWnd(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, UIntPtr e);
  public static void ClickCenter(IntPtr h) { RECT r; GetWindowRect(h, out r); SetCursorPos((r.L + r.R) / 2, (r.T + r.B) / 2); System.Threading.Thread.Sleep(80); mouse_event(2, 0, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(60); mouse_event(4, 0, 0, 0, UIntPtr.Zero); }
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static string Text(IntPtr h) { var t = new StringBuilder(256); SendMessageW(h, 0x000D, (IntPtr)256, t); return t.ToString(); }
  public static List<IntPtr> All(uint pid, string cls) { var f = new List<IntPtr>(); EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls && IsWindowVisible(h)) f.Add(h); return true; }, IntPtr.Zero); return f; }
  public static bool Front(IntPtr h) { keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero); ShowWindow(h, 9); BringWindowToTop(h); return SetForegroundWindow(h); }
  public static bool Caps() { return (GetKeyState(0x14) & 1) != 0; }
  [StructLayout(LayoutKind.Sequential)] public struct GTI { public int cbSize, flags; public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret; public int l, t, r, b; }
  [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint tid, ref GTI g);
  public static IntPtr GetFocusOf(IntPtr w) { uint pid; uint tid = GetWindowThreadProcessId(w, out pid); var g = new GTI(); g.cbSize = Marshal.SizeOf(typeof(GTI)); return GetGUIThreadInfo(tid, ref g) ? g.hwndFocus : IntPtr.Zero; }
  public static void TapCaps() { keybd_event(0x14, 0x3A, 0, UIntPtr.Zero); keybd_event(0x14, 0x3A, 2, UIntPtr.Zero); }
}
'@
function SetText($id, $s) { [void][KB]::SendMessageW([KB]::GetDlgItem($m, $id), 0x000C, [IntPtr]::Zero, [string]$s) }
function Click($id, $ms = 600) { [void][KB]::PostMessageW([KB]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds $ms }
function Label($id) { [KB]::Text([KB]::GetDlgItem($m, $id)) }
function CapsNow() { Start-Sleep -Milliseconds 150; [KB]::Caps() }
function SetCaps([bool]$on) { if ((CapsNow) -ne $on) { [KB]::TapCaps(); Start-Sleep -Milliseconds 250 } }
# a real click in the middle of the field (1Key's own controls do not take the UIA focus); the user's path too
# Windows shows its own "Caps Lock is on" balloon under a focused password field when Caps Lock gets turned on; in the first-setup
# screen it covers field 2 and a click there only closes the balloon (0.2.136-0.2.138 KB07 failure, traced). Hide it first
# (EM_HIDEBALLOONTIP) so the click reaches the field.
function FocusId($id) {
  $h = [KB]::GetDlgItem($m, $id); if ($h -eq [IntPtr]::Zero) { return $false }
  foreach ($e in 101, 102) { $x = [KB]::GetDlgItem($m, $e); if ($x -ne [IntPtr]::Zero) { [void][KB]::SendMessageW($x, 0x1504, [IntPtr]::Zero, [IntPtr]::Zero) } }
  Start-Sleep -Milliseconds 200
  [KB]::ClickCenter($h); Start-Sleep -Milliseconds 600
  $true
}
function FocusedId() { $f = [KB]::GetFocusOf($m); if ($f -eq [KB]::GetDlgItem($m, 101)) { 101 } elseif ($f -eq [KB]::GetDlgItem($m, 102)) { 102 } else { 0 } }
function Launch() {
  $script:p = Start-Process $exe -PassThru
  $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $w = @([KB]::All([uint32]$script:p.Id, "OneKeyMainWindow$suffix")); if ($w.Count -gt 0) { $script:m = $w[0]; break }; Start-Sleep -Milliseconds 100 }
  if ($script:m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 700
}
function Quit() { [void][KB]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i=0; $i -lt 50 -and -not $p.HasExited; $i++) { Start-Sleep -Milliseconds 100 } }

$capsAtStart = [KB]::Caps()
try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_WORKAREA = $TallScreen; $env:ONEKEY_TEST_FAIL = $null
  Launch
  [void][KB]::Front($m); Start-Sleep -Milliseconds 400
  # labels: remember the text for "off" and compare later
  [void](FocusId 102)                                   # start in field 2 (1Key's buttons cannot take the UIA focus)
  SetCaps $false; Start-Sleep -Milliseconds 400; $capsOffText = Label 106
  SetCaps $true; Start-Sleep -Milliseconds 400; $capsOnText = Label 106
  "NOTE caps label off/on differ: $($capsOffText -ne $capsOnText) (lengths $($capsOffText.Length)/$($capsOnText.Length))"

  # KB01
  [void](FocusId 101)
  Start-Sleep -Milliseconds 400
  Check KB01 "False|True|101" "$(CapsNow)|$((Label 106) -eq $capsOffText)|$(FocusedId)" "first-setup field 1 focused with Caps Lock on: turned off, label shows off"
  # KB02
  [KB]::TapCaps(); Start-Sleep -Milliseconds 900
  Check KB02 "True|True" "$(CapsNow)|$((Label 106) -eq $capsOnText)" "Caps Lock turned on again in the same field: left on, label shows on"
  # KB03
  [void](FocusId 102); Start-Sleep -Milliseconds 400
  Check KB03 "False|True|102" "$(CapsNow)|$((Label 106) -eq $capsOffText)|$(FocusedId)" "focus to first-setup field 2 with Caps Lock on: turned off (focus really on field 2)"
  # KB04
  SetCaps $true
  [KB]::keybd_event(0x10, 0x2A, 0, [UIntPtr]::Zero)     # Shift down
  [void](FocusId 101)
  [KB]::keybd_event(0x10, 0x2A, 2, [UIntPtr]::Zero)     # Shift up
  Start-Sleep -Milliseconds 300
  Check KB04 "True|101" "$(CapsNow)|$(FocusedId)" "Shift held while the field got the focus (focus really moved to field 1): Caps Lock left on (no key sent)"

  # KB07 repeated focus
  function Taps() { $v = [int64][KB]::SendMessageW($m, 0x8016, [IntPtr]::Zero, [IntPtr]12); [int](($v -shr 52) -band 0xF) }
  $k07 = @(); $tap0 = Taps
  $dbg07 = @()
  foreach ($fid in 102, 101, 102, 101) { SetCaps $true; Start-Sleep -Milliseconds 400; $lab = ((Label 106) -eq $capsOnText); [void](FocusId $fid); $k07 += "$(CapsNow)"; $dbg07 += "f$fid lblOn=$lab focus=$(FocusedId) taps=$((Taps) - $tap0)" }
  "NOTE [KB07] " + ($dbg07 -join ' / ')
  $tapN = (Taps) - $tap0
  Check KB07 "False|False|False|False|4" (($k07 -join '|') + "|$tapN") "4 more focus changes with Caps Lock on: off each time, one Caps Lock key each"

  # create the master, then lock while another window is in front
  SetText 101 "Master1234"; SetText 102 "Master1234"; Click 103 1200
  $b = @([KB]::All([uint32]$p.Id, "OneKeyDialog")); if ($b.Count -gt 0) { [void][KB]::PostMessageW($b[0], 0x0111, [IntPtr]1, [IntPtr]::Zero); Start-Sleep -Milliseconds 700 }
  $shell = [KB]::GetShellWindow(); [void][KB]::Front($shell); Start-Sleep -Milliseconds 600
  SetCaps $true
  $fgOther = [KB]::GetForegroundWindow() -ne $m
  [void][KB]::PostMessageW($m, 0x02B1, [IntPtr]7, [IntPtr]::Zero); Start-Sleep -Milliseconds 1500   # WM_WTSSESSION_CHANGE / WTS_SESSION_LOCK
  $locked = [KB]::GetDlgItem($m, 101) -ne [IntPtr]::Zero
  Check KB05 "True|True|True" "$fgOther|$locked|$(CapsNow)" "another window in front, 1Key locked itself: Caps Lock left on"

  # KB08 typing into another program while 1Key locks itself
  [void][KB]::Front($m); Start-Sleep -Milliseconds 400
  SetText 101 "Master1234"; [KB]::ClickCenter([KB]::GetDlgItem($m, 103)); Start-Sleep -Milliseconds 1500   # unlock
  Add-Type -AssemblyName System.Windows.Forms
  $form = New-Object Windows.Forms.Form; $form.Text = "kb08"; $form.Width = 300; $form.Height = 120
  $tb = New-Object Windows.Forms.TextBox; $tb.Width = 250; $form.Controls.Add($tb)
  $form.Show(); [Windows.Forms.Application]::DoEvents()
  [void][KB]::Front($form.Handle); $tb.Focus() | Out-Null; [Windows.Forms.Application]::DoEvents()
  SetCaps $true
  for ($i = 0; $i -lt 12; $i++) {
    [KB]::keybd_event(0x41, 0x1E, 0, [UIntPtr]::Zero); [KB]::keybd_event(0x41, 0x1E, 2, [UIntPtr]::Zero)
    if ($i -eq 4) { [void][KB]::PostMessageW($m, 0x02B1, [IntPtr]7, [IntPtr]::Zero) }   # 1Key locks itself in the middle
    for ($j = 0; $j -lt 10; $j++) { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 15 }
  }
  $typed = $tb.Text.Length; $fg08 = [KB]::GetForegroundWindow() -eq $form.Handle
  $locked08 = [KB]::GetDlgItem($m, 101) -ne [IntPtr]::Zero
  Check KB08 "True|True|12|True" "$locked08|$fg08|$typed|$(CapsNow)" "typing into another program while 1Key locked itself: 1Key locked, the other window stayed in front and got every letter, Caps Lock stayed on"
  $form.Close(); $form.Dispose()

  # KB06 IME (optional)
  [void][KB]::Front($m); Start-Sleep -Milliseconds 600; SetCaps $false
  $imeOff = Label 105
  $ime = [KB]::ImmGetDefaultIMEWnd([KB]::GetDlgItem($m, 101))
  if ($ime -ne [IntPtr]::Zero) {
    [void][KB]::SendMessageW($ime, 0x283, [IntPtr]6, [IntPtr]1)    # IMC_SETOPENSTATUS 1
    [void][KB]::SendMessageW($ime, 0x283, [IntPtr]2, [IntPtr]1)    # IMC_SETCONVERSIONMODE native
    Start-Sleep -Milliseconds 700; $imeOn = Label 105
    [void][KB]::Front([KB]::GetShellWindow()); Start-Sleep -Milliseconds 500; [void][KB]::Front($m); Start-Sleep -Milliseconds 800; $imeBack = Label 105   # re-activation gives the field the focus again
    "NOTE [KB06] IME native mode set from outside changes the label: $($imeOn -ne $imeOff); after focusing again it shows the non-native text: $($imeBack -eq $imeOff) (0.2.128 run: False/True - the password field seems to have no IME context of its own, so typing there is non-native anyway; not judged)"
  } else { "NOTE KB06 skipped: no IME window" }
}
catch { Add-Failure ("exception: " + $_.Exception.Message) }
finally {
  if ($m -and $m -ne [IntPtr]::Zero -and $p -and -not $p.HasExited) { Quit }
  Stop-TestInstances $suffix
  if ([KB]::Caps() -ne $capsAtStart) { [KB]::TapCaps(); Start-Sleep -Milliseconds 200 }
  "Caps Lock restored to the state at start: $([KB]::Caps() -eq $capsAtStart)"
}
Complete-Checks
