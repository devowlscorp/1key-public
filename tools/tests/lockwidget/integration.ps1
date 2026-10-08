# Lock widget integration (B redesign stage 2, Codex 16:14 R-W1..5). When locked, 1Key shows the floating mascot widget
# instead of the main window; the in-window lock screen stays hidden as the fallback. ONEKEY_TEST_LOCKWIDGET=1 here.
# LW01 first launch (no file): widget in create mode (two fields), main window hidden
# LW02 create through the widget (Enter in field 2): file written, widget closed, main window (list) shown
# LW03 list [lock]: main window hidden, widget in unlock mode (one field)
# LW04 wrong master: question owned by the widget, input windows disabled while it is open, a second Enter does not open a
#      second question; after OK the field is empty and the input windows work again
# LW05 a stale widget notice (instance 0) is ignored: no unlock with the right master in the field
# LW06 MSAA of the drawn buttons: 3 push buttons named unlock / minimize / exit, default action, locations inside the widget,
#      hit test on the arrow -> child 1
# LW07 accessible "press" on the unlock button with the right master -> real unlock (main window shown, widget gone)
# LW08 session-lock notice while unlocked: main window hidden and no widget; tray click shows the widget
# LW09 accessible "press" on exit -> question owned by the widget; No keeps 1Key running with the widget
# LW10 accessible "press" on minimize -> widget minimized, timers stopped (CPU ~0); tray click restores it
# LW11 exit question open + session-lock notice -> question cancelled, widget hidden, 1Key keeps running
# LW12 WM_CLOSE to the input window (Alt+F4 there) -> input window survives, exit question instead (No)
# LW13 widget pushed off screen + WM_DISPLAYCHANGE -> back inside the work area
# LW14 render failure while shown (ONEKEY_TEST_LOCKWIDGET_FAIL): widget closed, old lock screen with an EMPTY field, still
#      locked; the same master unlocks there
# LW15 render failure during first setup: no file written; the old first-setup screen creates it
# LW16 master of 257 chars made on the old screen: the 256-char prefix is refused, the full 257 chars TYPED into the widget unlock
# LW17 arrow clicked with the real mouse during the 0.22 s widening -> exactly one submit (one question)
# LW18 shown while another window is in front (posted show request): the other window keeps its keyboard focus and text, CapsLock
#      unchanged (B-W03 contrast)
# LW19 opened from the tray: the caret is in the field but the widget stays narrow; the first key typed widens it (0.2.146 user
#      decision: start narrow, type right away)
# LW20 widened widget + reopen request (tray click / tray [lock] while locked): back to narrow, typed text kept (0.2.147)
# LW21 list [lock] with the main window moved: the widget appears centred where the main window was (0.2.147 user decision)
# LW22 widget moved (end of a drag) -> remembered across a restart: lockwidget.pos written, widget opens centred there
# LW23 unlock from the moved widget: the main window opens centred where the widget was (0.2.157 user decision)
# LW24 late failure notice from an older widget (hidden, then reopened): ignored, the shown widget stays
# LW25 failure notice after the widget was hidden (session lock): the main window stays hidden; reopening shows the old lock screen
# LW26 failure notice after unlock: ignored; the next lock uses the widget again (Codex R148-3, 0.2.159)
# LW27 bad/extreme lockwidget.pos (garbage, empty, >128 bytes, overflow, int.MinValue, over the limit, far, a folder): the widget
#      still opens inside the work area and 1Key keeps running (Codex R148-1, 0.2.159)
# LW28 a question over the widget opens without the blurred backdrop window (a layered owner captures as a black box; 0.2.163 user)
# R148-2 (Codex 2026-10-03 19:39 3, 0.2.167) - real keys / real mouse, the foreground process checked before each:
# LW29 narrow widget, first REAL key: one character (no loss, no doubling) and the widget widens; then B C D, Home, Delete, End, Backspace
#      move the caret and remove exactly one character each (length 4 -> 3 -> 2, caret 0 after Home, at the end after End)
# LW30 first setup, narrow: A in field 1, Tab -> field 2, B, Shift+Tab -> field 1; one character in each, nothing lost
# LW31 wrong master + Enter: the error box is open, the input windows are disabled; a reopen request (tray click) while it is open does
#      NOT narrow the widget (input and arrow stay where they are); closing the box gives the field back
# R167-T1 (Codex 11:50, 0.2.168): LW32-34 hold the RIGHT master so a wrong submit would unlock; LW36 is the control that the same
# value and press/release path unlocks. Each checks the press really captured the mouse before the cancel.
# LW32 right master, press the arrow, drag off it and release: still locked, no box, the capture is released
# LW33 right master, press the arrow, minimized from outside while held (ShowWindowAsync - not the real Win+D key): the capture is
#      released at once; release: still locked; restore works. Before the change the release could submit
#      capture released; restore works
# LW34 right master, press the arrow, a session lock while held (0.3.18: the widget is made again - "gone" = the held one is gone and a
#      new one is shown), release: nothing submitted; reopened and
#      still locked a second later (the window is destroyed, which releases the capture - the owner is not read after that)
# LW35 a 60-character value, then narrowed (reopen request): the caret stays at the end and the last character is inside the narrow
#      field (the EDIT scrolled), length unchanged
# LW36 control for LW32-34: the right master and a press/release on the arrow unlock (then [lock] in the list brings the widget back)
# LW37 (R-W3a) a real foreground refusal: the harness window in front calls LockSetForegroundWindow(LSFW_LOCK); a show request to 1Key
#      is refused - the harness window keeps the foreground, its text field keeps focus and text, and the keys typed while 1Key handles
#      the request (a before it as a control, x y z during it; real keys guarded for the harness process) all land there; CapsLock
#      unchanged. The text box runs on its own thread with a real message loop (0.2.172: a DoEvents-pumped box lost keys without 1Key)
# LW40 (0.3.18, 2026-10-05 user) unlocked with the main window on screen, session lock: the narrow widget appears where the main window
#      was (centres within 80 px), the main window is hidden, 1Key is locked
# LW41 the widget on screen with typed text, session lock: a widget is still on screen but a NEW one - the field is empty
# Since 0.3.18 a session lock keeps an on-screen 1Key as the widget; LockNotice below is the lock while 1Key is in the tray (it is put
# away first the way a user does: widget minimized, main window hidden), LockNoticeShown the lock while it is on screen.
# LW39 right master, arrow held, the widget's rendering fails (test-only message WM_TEST_FAIL_NOW), then release: the widget closes,
#      one notice, still locked on the old lock screen with an empty field - no late submit (Codex 12:41 condition 2, 0.2.172)
# LW38 (R-W2a) 255 / 256 / 257-character dummy masters: made in the widget with real keys (both fields, lengths read), restart and
#      unlock in the widget with real keys, then unlock on the old lock screen with the same value - paste / IME not covered
# Not covered: Ctrl+V (tests never touch the clipboard), IME composition Enter and the Korean/Caps indicator change (would change the
# user's keyboard state), Narrator / Magnifier tracking.
# Real key presses (LW16) and real clicks (LW17) check the foreground process before every key. No clipboard use.
# Own config folders and test suffix; the real 1Key and its settings are not touched. ASCII only. Run only while the PC is idle.
param([string]$Exe = "")
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "..\lib\Check.ps1")
Start-Checks -Required @("LW01","LW02","LW03","LW04","LW05","LW06","LW07","LW08","LW09","LW10","LW11","LW12","LW13","LW14","LW15","LW16","LW17","LW18","LW19","LW20","LW21","LW22","LW23","LW24","LW25","LW26","LW27","LW28","LW29","LW30","LW31","LW32","LW33","LW34","LW35","LW36","LW37","LW38","LW39","LW40","LW41")

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$suffix = ".lw"
function NewCfg($name) { $d = "$sp\$name"; if (Test-Path $d) { Get-ChildItem $d -Recurse | Remove-Item -Force -Recurse -ErrorAction Ignore }; New-Item -ItemType Directory -Force $d | Out-Null; $d }
Add-Type -AssemblyName System.Windows.Forms
Add-Type -ReferencedAssemblies Accessibility, System.Windows.Forms, System.Drawing @'
using System;using System.Collections.Generic;using System.Runtime.InteropServices;using System.Text;using Accessibility;
public class LW {
  public delegate bool EnumCb(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumCb cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EnumCb cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern short GetKeyState(int vk);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct GTI { public int cbSize, flags; public IntPtr active, focus, capture, menuOwner, moveSize, caret; public int l, t, r, b; }
  [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint tid, ref GTI g);
  [DllImport("oleacc.dll")] static extern int AccessibleObjectFromWindow(IntPtr h, uint id, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object o);
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static List<IntPtr> All(uint pid, string cls) { var f = new List<IntPtr>(); EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls && IsWindowVisible(h)) f.Add(h); return true; }, IntPtr.Zero); return f; }
  public static IntPtr ChildOf(IntPtr p, string cls) { IntPtr r = IntPtr.Zero; EnumChildWindows(p, (h,l) => { if (Cls(h) == cls) { r = h; return false; } return true; }, IntPtr.Zero); return r; }
  public static bool Front(IntPtr h) { keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero); return SetForegroundWindow(h); }
  public static uint FgPid() { uint p; GetWindowThreadProcessId(GetForegroundWindow(), out p); return p; }
  public static IntPtr Focus(IntPtr w) { uint p; uint tid = GetWindowThreadProcessId(w, out p); var g = new GTI(); g.cbSize = Marshal.SizeOf(typeof(GTI)); return GetGUIThreadInfo(tid, ref g) ? g.focus : IntPtr.Zero; }
  public static int Len(IntPtr e) { return (int)SendMessageW(e, 0x00C1, IntPtr.Zero, IntPtr.Zero); }   // EM_LINELENGTH (a password EDIT hides its text from other processes)
  public static IAccessible Acc(IntPtr h) { Guid g = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71"); object o; return AccessibleObjectFromWindow(h, 0xFFFFFFFC, ref g, out o) == 0 ? (IAccessible)o : null; }
  // PowerShell calls COM objects through IDispatch, which 1Key's IAccessible does not implement: call the interface from C#.
  public static int AccCount(IntPtr h) { var a = Acc(h); return a == null ? -1 : a.accChildCount; }
  public static string AccName(IntPtr h, int c) { var a = Acc(h); return a == null ? null : a.get_accName(c); }
  public static string AccRole(IntPtr h, int c) { var a = Acc(h); return a == null ? null : Convert.ToString(a.get_accRole(c)); }
  public static string AccAction(IntPtr h, int c) { var a = Acc(h); return a == null ? null : a.get_accDefaultAction(c); }
  public static int[] AccLoc(IntPtr h, int c) { var a = Acc(h); if (a == null) return new int[] { 0, 0, 0, 0 }; int x, y, w, hh; a.accLocation(out x, out y, out w, out hh, c); return new int[] { x, y, w, hh }; }
  public static string AccHit(IntPtr h, int x, int y) { var a = Acc(h); return a == null ? null : Convert.ToString(a.accHitTest(x, y)); }
  public static void AccPress(IntPtr h, int c) { var a = Acc(h); if (a != null) a.accDoDefaultAction(c); }
  public static uint Guarded; // pid that must be in front before each key
  public static void Key(byte vk) { if (FgPid() != Guarded) throw new Exception("foreground is not 1Key - stopped before a key reached another program"); keybd_event(vk, 0, 0, UIntPtr.Zero); keybd_event(vk, 0, 2, UIntPtr.Zero); System.Threading.Thread.Sleep(25); }
  // Click on an accessible child at the position it has right now (no pause between reading and pressing): during the 0.22 s
  // widening the arrow moves tens of pixels per 50 ms, so a stale position would miss it (the app hits what it currently draws).
  public static int[] ClickChildNow(IntPtr w, int child) { if (FgPid() != Guarded) throw new Exception("foreground is not 1Key - stopped before a click"); int[] l = AccLoc(w, child); int x = l[0] + l[2] / 2, y = l[1] + l[3] / 2; SetCursorPos(x, y); mouse_event(2, 0, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(30); mouse_event(4, 0, 0, 0, UIntPtr.Zero); return l; }
  public static void Click(int x, int y) { if (FgPid() != Guarded) throw new Exception("foreground is not 1Key - stopped before a click"); SetCursorPos(x, y); System.Threading.Thread.Sleep(40); mouse_event(2, 0, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(50); mouse_event(4, 0, 0, 0, UIntPtr.Zero); }
  // R148-2 (0.2.167): modifier chords, a held press on a child, the release elsewhere, the capture owner, caret and character positions
  public static void Chord(byte mod, byte vk) { if (FgPid() != Guarded) throw new Exception("foreground is not 1Key - stopped before a key reached another program"); keybd_event(mod, 0, 0, UIntPtr.Zero); keybd_event(vk, 0, 0, UIntPtr.Zero); keybd_event(vk, 0, 2, UIntPtr.Zero); keybd_event(mod, 0, 2, UIntPtr.Zero); System.Threading.Thread.Sleep(25); }
  public static void KeyX(byte vk) { if (FgPid() != Guarded) throw new Exception("foreground is not 1Key - stopped before a key reached another program"); keybd_event(vk, 0, 1, UIntPtr.Zero); keybd_event(vk, 0, 3, UIntPtr.Zero); System.Threading.Thread.Sleep(25); }   // extended key (Home/End/Delete)
  public static int[] HoldChild(IntPtr w, int child) { if (FgPid() != Guarded) throw new Exception("foreground is not 1Key - stopped before a click"); int[] l = AccLoc(w, child); int x = l[0] + l[2] / 2, y = l[1] + l[3] / 2; SetCursorPos(x, y); System.Threading.Thread.Sleep(40); mouse_event(2, 0, 0, 0, UIntPtr.Zero); return new int[] { x, y }; }
  [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool LockSetForegroundWindow(uint code);
  public static void KeyOwn(byte vk) { if (FgPid() != (uint)System.Diagnostics.Process.GetCurrentProcess().Id) throw new Exception("foreground is not the harness window - stopped before a key reached another program"); keybd_event(vk, 0, 0, UIntPtr.Zero); keybd_event(vk, 0, 2, UIntPtr.Zero); System.Threading.Thread.Sleep(25); }
  public static void MoveTo(int x, int y) { SetCursorPos(x, y); System.Threading.Thread.Sleep(60); }
  public static void Release() { mouse_event(4, 0, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(60); }
  public static IntPtr Capture(IntPtr w) { uint p; uint tid = GetWindowThreadProcessId(w, out p); var g = new GTI(); g.cbSize = Marshal.SizeOf(typeof(GTI)); return GetGUIThreadInfo(tid, ref g) ? g.capture : IntPtr.Zero; }
  public static int SelEnd(IntPtr e) { return (int)(((long)SendMessageW(e, 0x00B0, IntPtr.Zero, IntPtr.Zero) >> 16) & 0xFFFF); }   // EM_GETSEL (return value: no pointers cross processes)
  public static int PosX(IntPtr e, int i) { return (short)((long)SendMessageW(e, 0x00D6, (IntPtr)i, IntPtr.Zero) & 0xFFFF); }      // EM_POSFROMCHAR
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  public static int ClientW(IntPtr h) { RECT r; GetClientRect(h, out r); return r.R - r.L; }
}
// LW37 typing target (0.2.172): a text box on its OWN thread with a real message loop. Typing into a WinForms window that PowerShell
// pumps now and then with DoEvents lost or reordered keys even with no 1Key running (keepxy, zxy), so the old way could not judge.
public class Target {
  static System.Windows.Forms.Form F; static System.Windows.Forms.TextBox T; static uint Tid;
  static System.Threading.ManualResetEvent Ready = new System.Threading.ManualResetEvent(false);
  [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool on);
  [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
  public static IntPtr Handle;
  public static IntPtr Start(string text) {
    Ready.Reset();
    var th = new System.Threading.Thread(() => {
      Tid = GetCurrentThreadId();
      F = new System.Windows.Forms.Form(); F.StartPosition = System.Windows.Forms.FormStartPosition.Manual; F.Left = 10; F.Top = 10; F.Width = 260; F.Height = 90;
      T = new System.Windows.Forms.TextBox(); T.Width = 200; T.Text = text; F.Controls.Add(T);
      F.Shown += (s, e) => { T.Focus(); T.SelectionStart = T.Text.Length; T.SelectionLength = 0; Handle = F.Handle; Ready.Set(); };
      System.Windows.Forms.Application.Run(F);
    });
    th.SetApartmentState(System.Threading.ApartmentState.STA); th.IsBackground = true; th.Start();
    Ready.WaitOne(5000);
    return Handle;
  }
  // foreground without injecting keys (an Alt tap puts the form in menu mode and eats the next key)
  public static bool Front() { uint p; uint fgTid = GetWindowThreadProcessId(GetForegroundWindow(), out p); bool att = fgTid != Tid && AttachThreadInput(Tid, fgTid, true); bool ok = SetForegroundWindow(Handle); if (att) AttachThreadInput(Tid, fgTid, false); return ok; }
  public static string Text() { return (string)F.Invoke(new Func<string>(() => T.Text)); }
  public static bool Focused() { return (bool)F.Invoke(new Func<bool>(() => T.Focused)); }
  public static void Close() { try { F.Invoke(new Action(() => F.Close())); } catch { } }
}
'@
$WM_LOCKWIDGET = 0x8000 + 60
function Launch() {
  $script:p = Start-Process $exe -PassThru
  $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) {
    $w = @(); [LW]::EnumWindows({ param($h, $l) $pp = 0; [void][LW]::GetWindowThreadProcessId($h, [ref]$pp); if ($pp -eq $script:p.Id -and [LW]::Cls($h) -eq "OneKeyMainWindow$suffix") { $script:m = $h }; $true }, [IntPtr]::Zero) | Out-Null
    if ($script:m -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 100
  }
  if ($script:m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 1200
  [LW]::Guarded = [uint32]$script:p.Id
}
function Quit() { if ($script:p -and -not $script:p.HasExited) { [void][LW]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 1200; if (-not $script:p.HasExited) { Stop-Process -Id $script:p.Id -Force } } }
function Widget() { @([LW]::All([uint32]$p.Id, "OneKeyLockWidget")) | Select-Object -First 1 }
function Hosts() { @([LW]::All([uint32]$p.Id, "OneKeyLockInput")) }
Add-Type @'
using System;using System.Runtime.InteropServices;
public class LW2 { [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr h); }
'@
function Field($n) { foreach ($h in Hosts) { $e = [LW]::ChildOf($h, "Edit"); if ([LW2]::GetDlgCtrlID($e) -eq (100 + $n)) { return $e } }; [IntPtr]::Zero }
function SetW($n, $s) { [void][LW]::SendMessageW((Field $n), 0x000C, [IntPtr]::Zero, [string]$s) }
function EnterIn($n) { $e = Field $n; [void][LW]::PostMessageW($e, 0x0100, [IntPtr]0x0D, [IntPtr]0x001C0001); Start-Sleep -Milliseconds 900 }   # WM_KEYDOWN VK_RETURN to the widget field
function Box() { for ($i = 0; $i -lt 25; $i++) { $b = @([LW]::All([uint32]$p.Id, "OneKeyDialog")); if ($b.Count -gt 0) { return $b[0] }; Start-Sleep -Milliseconds 100 }; [IntPtr]::Zero }
function Boxes() { @([LW]::All([uint32]$p.Id, "OneKeyDialog")).Count }
function Answer($b, $cmd) { if ($b -ne [IntPtr]::Zero) { [void][LW]::PostMessageW($b, 0x0111, [IntPtr]$cmd, [IntPtr]::Zero); Start-Sleep -Milliseconds 800 } }
function Owner($h) { [LW]::GetWindow($h, 4) }
function MainShown() { [LW]::IsWindowVisible($m) -and -not [LW]::IsIconic($m) }
function OnOldLock() { [LW]::GetDlgItem($m, 101) -ne [IntPtr]::Zero }
function Unlocked() { [LW]::GetDlgItem($m, 101) -eq [IntPtr]::Zero -and [LW]::GetDlgItem($m, 2014) -ne [IntPtr]::Zero }
function Alive() { -not $p.HasExited }
function LockNoticeShown() { [void][LW]::PostMessageW($m, 0x02B1, [IntPtr]7, [IntPtr]::Zero); Start-Sleep -Milliseconds 1200 }
function LockNotice() {
  $w = Widget; if ($w) { [void][LW]::ShowWindowAsync($w, 6) }   # widget minimized (the user's [-])
  if ([LW]::IsWindowVisible($m)) { [void][LW]::ShowWindowAsync($m, 0) }   # main window put away (closing it hides it to the tray)
  Start-Sleep -Milliseconds 400
  LockNoticeShown
}
function TrayClick() { [void][LW]::PostMessageW($m, 0x8001, [IntPtr]::Zero, [IntPtr]0x0202); Start-Sleep -Milliseconds 1200 }
function ListLock() { [void][LW]::PostMessageW([LW]::GetDlgItem($m, 2014), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 1300 }
function Press($child) { $w = Widget; if ($w) { [LW]::AccPress($w, [int]$child) }; Start-Sleep -Milliseconds 900 }
# Since 0.2.146 the widget opens narrow and only widens when the user starts typing; the injected render failure needs the
# widening/greeting renders, so the failure tests "start typing" with a posted End key (no character, no real input).
function Widen($n) { [void][LW]::PostMessageW((Field $n), 0x0100, [IntPtr]0x23, [IntPtr]0x014F0001); Start-Sleep -Milliseconds 300 }
function Cpu($sec) { $p.Refresh(); $t0 = $p.TotalProcessorTime.TotalMilliseconds; Start-Sleep -Seconds $sec; $p.Refresh(); ($p.TotalProcessorTime.TotalMilliseconds - $t0) / ($sec * 10) }
$master = "Master1234"

try {
  Stop-TestInstances $suffix
  $cfg = NewCfg "lockwidget_cfg"
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_WORKAREA = $null; $env:ONEKEY_TEST_FAIL = $null
  $env:ONEKEY_TEST_LOCKWIDGET = "1"; $env:ONEKEY_TEST_LOCKWIDGET_FAIL = $null
  Launch

  # LW01
  $w = Widget
  Check LW01 "True|2|False" "$($w -ne $null)|$(@(Hosts).Count)|$([LW]::IsWindowVisible($m))" "first launch: create-mode widget (two fields), main window hidden"

  # LW02
  SetW 1 $master; SetW 2 $master; EnterIn 2
  $b = Box; if ($b -ne [IntPtr]::Zero -and (Owner $b) -eq $m) { Answer $b 1 }   # a first-run notice on the main window, if any
  Check LW02 "True|True|False|True" "$(Test-Path "$cfg\config.dat")|$(MainShown)|$((Widget) -ne $null)|$(Unlocked)" "create through the widget: file written, widget closed, list shown"

  # LW03
  ListLock
  $w = Widget
  Check LW03 "False|True|1" "$([LW]::IsWindowVisible($m))|$($w -ne $null)|$(@(Hosts).Count)" "list [lock]: main hidden, unlock-mode widget"

  # LW04
  SetW 1 "nope"; EnterIn 1
  $b = Box; $own = ($b -ne [IntPtr]::Zero) -and ((Owner $b) -eq (Widget))
  $hostOff = -not [LW]::IsWindowEnabled((Hosts)[0])
  EnterIn 1; $count = Boxes
  Answer $b 1
  $after = "$([LW]::Len((Field 1)))|$([LW]::IsWindowEnabled((Hosts)[0]))"
  Check LW04 "True|True|1|0|True" "$own|$hostOff|$count|$after" "wrong master: question owned by the widget, input disabled, second Enter ignored, field emptied, input back"

  # LW05
  SetW 1 $master
  [void][LW]::PostMessageW($m, $WM_LOCKWIDGET, [IntPtr]1, [IntPtr]0); Start-Sleep -Milliseconds 900
  Check LW05 "True|False|0" "$((Widget) -ne $null)|$(Unlocked)|$(Boxes)" "stale widget notice (instance 0) ignored"

  # LW06
  $w = Widget
  $n = [LW]::AccCount($w)
  $names = (1..3 | ForEach-Object { [LW]::AccName($w, $_) }) -join "/"
  $roles = (1..3 | ForEach-Object { [LW]::AccRole($w, $_) }) -join "/"
  $act = [LW]::AccAction($w, 1)
  $wr = New-Object LW+RECT; [void][LW]::GetWindowRect($w, [ref]$wr)
  $inside = $true; $cx = 0; $cy = 0
  foreach ($c in 1..3) { $l = [LW]::AccLoc($w, $c); if ($l[2] -le 0 -or $l[0] -lt $wr.L -or $l[1] -lt $wr.T -or $l[0] + $l[2] -gt $wr.R -or $l[1] + $l[3] -gt $wr.B) { $inside = $false }; if ($c -eq 1) { $cx = $l[0] + [int]($l[2] / 2); $cy = $l[1] + [int]($l[3] / 2) } }
  $hit = [LW]::AccHit($w, $cx, $cy)
  Check LW06 ("3|" + [char]0xC7A0 + [char]0xAE08 + " " + [char]0xD574 + [char]0xC81C + "/" + [char]0xCD5C + [char]0xC18C + [char]0xD654 + "/" + [char]0xC885 + [char]0xB8CC + "|43/43/43|" + [char]0xB204 + [char]0xB974 + [char]0xAE30 + "|True|1") "$n|$names|$roles|$act|$inside|$hit" "MSAA: 3 push buttons, names, default action, locations, hit test"

  # LW07
  Press 1
  Check LW07 "True|True|False" "$(Unlocked)|$(MainShown)|$((Widget) -ne $null)" "accessible press on unlock with the right master: unlocked"

  # LW40: unlocked, main window on screen, session lock
  $mr = New-Object LW+RECT; [void][LW]::GetWindowRect($m, [ref]$mr)
  LockNoticeShown
  $w = Widget; $wr = New-Object LW+RECT; if ($w) { [void][LW]::GetWindowRect($w, [ref]$wr) }
  $near = ($w -ne $null) -and ([Math]::Abs((($wr.L + $wr.R) / 2) - (($mr.L + $mr.R) / 2)) -lt 80) -and ([Math]::Abs((($wr.T + $wr.B) / 2) - (($mr.T + $mr.B) / 2)) -lt 80)
  Check LW40 "True|False|False|True" "$($w -ne $null)|$(MainShown)|$(Unlocked)|$near" "main window on screen, session lock: the narrow widget where the main window was, main window hidden, locked"

  # LW41: the widget on screen with typed text, session lock
  SetW 1 "abc"; $len0 = [LW]::Len((Field 1))
  LockNoticeShown
  Check LW41 "3|True|0|False" "$len0|$((Widget) -ne $null)|$([LW]::Len((Field 1)))|$(MainShown)" "widget on screen with typed text, session lock: a widget is still shown, a new one with an empty field; main window hidden"

  # LW08
  LockNotice
  $hidden = (-not [LW]::IsWindowVisible($m)) -and ((Widget) -eq $null)
  TrayClick
  Check LW08 "True|True|False" "$hidden|$((Widget) -ne $null)|$([LW]::IsWindowVisible($m))" "session lock: everything hidden; tray click shows the widget"

  # LW09
  Press 3
  $b = Box; $own = ($b -ne [IntPtr]::Zero) -and ((Owner $b) -eq (Widget))
  Answer $b 7
  Check LW09 "True|True|True" "$own|$(Alive)|$((Widget) -ne $null)" "accessible press on exit: question owned by the widget, No keeps running"

  # LW10
  Press 2
  $w = Widget; $iconic = $w -and [LW]::IsIconic($w)
  Start-Sleep -Milliseconds 500; $cpu = Cpu 3
  TrayClick
  $w = Widget; $back = $w -and -not [LW]::IsIconic($w)
  Check LW10 "True|True|True" "$iconic|$($cpu -lt 0.5)|$back" ("accessible press on minimize: minimized (cpu {0:N2}%), tray restores" -f $cpu)

  # LW11
  Press 3
  $b = Box; $asked = $b -ne [IntPtr]::Zero
  $backdrops = @([LW]::All([uint32]$p.Id, "OneKeyBackdrop")).Count
  Check LW28 "True|0" "$asked|$backdrops" "exit question over the widget: no blurred backdrop (a layered window captures as a black box, 0.2.163)"
  LockNotice
  Check LW11 "True|0|True|True" "$asked|$(Boxes)|$((Widget) -eq $null)|$(Alive)" "exit question + session lock: question cancelled, widget hidden, running"

  # LW12
  TrayClick
  $h = (Hosts)[0]
  [void][LW]::PostMessageW($h, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 900
  $b = Box; $asked = $b -ne [IntPtr]::Zero; Answer $b 7
  Check LW12 "True|True|True" "$([LW]::IsWindow($h))|$asked|$(Alive)" "WM_CLOSE to the input window: it survives, exit question instead"

  # LW13
  $w = Widget
  [void][LW]::SetWindowPos($w, [IntPtr]::Zero, -3000, -3000, 0, 0, 0x0001 -bor 0x0004 -bor 0x0010)
  Start-Sleep -Milliseconds 300
  [void][LW]::PostMessageW($w, 0x007E, [IntPtr]32, [IntPtr]::Zero); Start-Sleep -Milliseconds 800
  $wr = New-Object LW+RECT; [void][LW]::GetWindowRect($w, [ref]$wr)
  $wa = [System.Windows.Forms.Screen]::FromHandle($w).WorkingArea
  $inArea = $wr.L -ge $wa.Left -and $wr.T -ge $wa.Top -and $wr.R -le $wa.Right -and $wr.B -le $wa.Bottom
  Check LW13 "True" "$inArea" "off-screen widget + WM_DISPLAYCHANGE: back inside the work area ($($wr.L),$($wr.T))"

  # LW21: lock from the list with the main window moved
  SetW 1 $master; EnterIn 1
  [void][LW]::SetWindowPos($m, [IntPtr]::Zero, 220, 140, 0, 0, 0x0001 -bor 0x0004 -bor 0x0010); Start-Sleep -Milliseconds 400
  $mr = New-Object LW+RECT; [void][LW]::GetWindowRect($m, [ref]$mr)
  ListLock
  $wr = New-Object LW+RECT; [void][LW]::GetWindowRect((Widget), [ref]$wr)
  $dx = [Math]::Abs((($wr.L + $wr.R) / 2) - (($mr.L + $mr.R) / 2)); $dy = [Math]::Abs((($wr.T + $wr.B) / 2) - (($mr.T + $mr.B) / 2))
  $clamped = $wr.T -eq [System.Windows.Forms.Screen]::FromHandle((Widget)).WorkingArea.Top
  Check LW21 "True" "$($dx -le 3 -and ($dy -le 3 -or $clamped))" "list [lock]: widget centred on the main window (dx $dx, dy $dy, clamped to the top=$clamped)"

  # LW22: move the widget (end of a drag), restart, it opens there
  $w = Widget
  [void][LW]::SetWindowPos($w, [IntPtr]::Zero, 500, 260, 0, 0, 0x0001 -bor 0x0004 -bor 0x0010); Start-Sleep -Milliseconds 200
  [void][LW]::PostMessageW($w, 0x0232, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 400   # WM_EXITSIZEMOVE (what a mascot drag ends with)
  $want = New-Object LW+RECT; [void][LW]::GetWindowRect($w, [ref]$want)
  Quit
  Launch
  $wr = New-Object LW+RECT; [void][LW]::GetWindowRect((Widget), [ref]$wr)
  Check LW22 "True|True" "$(Test-Path "$cfg\lockwidget.pos")|$($wr.L -eq $want.L -and $wr.T -eq $want.T)" "widget moved, restart: opens at the same place ($($want.L),$($want.T) -> $($wr.L),$($wr.T))"

  # LW23: unlock -> the main window centred on the widget
  SetW 1 $master; EnterIn 1
  $mr = New-Object LW+RECT; [void][LW]::GetWindowRect($m, [ref]$mr)
  $dx = [Math]::Abs((($mr.L + $mr.R) / 2) - (($wr.L + $wr.R) / 2)); $dy = [Math]::Abs((($mr.T + $mr.B) / 2) - (($wr.T + $wr.B) / 2))
  $wa = [System.Windows.Forms.Screen]::FromHandle($m).WorkingArea
  $clamped = $mr.T -eq $wa.Top -or $mr.B -eq $wa.Bottom -or $mr.L -eq $wa.Left -or $mr.R -eq $wa.Right
  Check LW23 "True|True" "$(Unlocked)|$($dx -le 3 -and ($dy -le 3 -or $clamped))" "unlock: main window centred on the widget (dx $dx, dy $dy, clamped=$clamped)"
  Quit

  # LW14: render failure while shown (unlock)
  $env:ONEKEY_TEST_LOCKWIDGET_FAIL = "25"
  Launch
  $w0 = (Widget) -ne $null
  if ($w0) { [void][LW]::Front((Widget)); SetW 1 "partial"; Widen 1 }
  for ($i = 0; $i -lt 60 -and (Widget) -ne $null; $i++) { Start-Sleep -Milliseconds 200 }
  Start-Sleep -Milliseconds 600
  $b = Box; if ($b -ne [IntPtr]::Zero) { Answer $b 1 }
  $fieldEmpty = [LW]::Len([LW]::GetDlgItem($m, 101)) -eq 0
  $state = "$w0|$((Widget) -eq $null)|$(MainShown)|$(OnOldLock)|$fieldEmpty|$(-not (Unlocked))"
  [void][LW]::SendMessageW([LW]::GetDlgItem($m, 101), 0x000C, [IntPtr]::Zero, [string]$master); [void][LW]::PostMessageW([LW]::GetDlgItem($m, 103), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 1200
  Check LW14 "True|True|True|True|True|True|True" "$state|$(Unlocked)" "render failure while shown: old lock screen, empty field, still locked; same master unlocks"
  Quit

  # LW15: render failure during first setup
  $cfg2 = NewCfg "lockwidget_cfg2"; $env:ONEKEY_CONFIG_DIR = $cfg2
  Launch
  $w0 = (Widget) -ne $null -and @(Hosts).Count -eq 2
  if ($w0) { [void][LW]::Front((Widget)); SetW 1 "Setup5678x"; Widen 1 }   # in front: the greeting renders, so the injected failure comes
  for ($i = 0; $i -lt 60 -and (Widget) -ne $null; $i++) { Start-Sleep -Milliseconds 200 }
  Start-Sleep -Milliseconds 600
  $b = Box; if ($b -ne [IntPtr]::Zero) { Answer $b 1 }
  $noFile = -not (Test-Path "$cfg2\config.dat")
  $oldCreate = [LW]::GetDlgItem($m, 102) -ne [IntPtr]::Zero
  [void][LW]::SendMessageW([LW]::GetDlgItem($m, 101), 0x000C, [IntPtr]::Zero, "Setup5678x"); [void][LW]::SendMessageW([LW]::GetDlgItem($m, 102), 0x000C, [IntPtr]::Zero, "Setup5678x")
  [void][LW]::PostMessageW([LW]::GetDlgItem($m, 103), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 1200
  $b = Box; if ($b -ne [IntPtr]::Zero) { Answer $b 1 }
  Check LW15 "True|True|True|True" "$w0|$noFile|$oldCreate|$(Test-Path "$cfg2\config.dat")" "render failure during first setup: no file; the old first-setup screen creates it"
  Quit
  $env:ONEKEY_TEST_LOCKWIDGET_FAIL = $null

  # LW16: 257-char master made on the old screen, typed into the widget
  $chars = "abcdefghij0123456789"
  $long = -join (0..256 | ForEach-Object { $chars[$_ % $chars.Length] })
  $cfg3 = NewCfg "lockwidget_cfg3"; $env:ONEKEY_CONFIG_DIR = $cfg3
  $env:ONEKEY_TEST_LOCKWIDGET = "0"
  Launch
  [void][LW]::SendMessageW([LW]::GetDlgItem($m, 101), 0x000C, [IntPtr]::Zero, $long); [void][LW]::SendMessageW([LW]::GetDlgItem($m, 102), 0x000C, [IntPtr]::Zero, $long)
  [void][LW]::PostMessageW([LW]::GetDlgItem($m, 103), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 1500
  # the repeated pattern contains '123456', which the weak-master check (0.3.36) warns about: answer Yes (advisory only), then
  # wait for the key computation (async since 0.3.40)
  $b = Box; if ($b -ne [IntPtr]::Zero) { Answer $b 6; Start-Sleep -Milliseconds 1500 }
  $made = Unlocked
  Quit
  $env:ONEKEY_TEST_LOCKWIDGET = "1"
  Launch
  SetW 1 $long.Substring(0, 256); EnterIn 1
  $b = Box; $refused = $b -ne [IntPtr]::Zero; Answer $b 1
  $typed = $false
  if ((Widget) -ne $null -and [LW]::Front((Widget))) {
    Start-Sleep -Milliseconds 500
    foreach ($ch in $long.ToCharArray()) { $vk = if ($ch -match '[a-z]') { [byte][char]([string]$ch).ToUpper() } else { [byte][char]$ch }; [LW]::Key($vk) }
    $typed = [LW]::Len((Field 1)) -eq 257
    [LW]::Key(0x0D); Start-Sleep -Milliseconds 1500
  }
  Check LW16 "True|True|True|True" "$made|$refused|$typed|$(Unlocked)" "257-char master: 256-char prefix refused, all 257 typed into the widget unlock"
  Quit

  # LW17: arrow clicked during the widening animation
  $env:ONEKEY_CONFIG_DIR = $cfg
  Launch
  $form = New-Object Windows.Forms.Form; $form.StartPosition = "Manual"; $form.Left = 10; $form.Top = 10; $form.Width = 160; $form.Height = 80; $form.FormBorderStyle = "None"; $form.Show(); [Windows.Forms.Application]::DoEvents()
  SetW 1 "wrong"
  [void][LW]::Front($form.Handle); Start-Sleep -Milliseconds 700; [Windows.Forms.Application]::DoEvents()
  $w = Widget; $wr = New-Object LW+RECT; [void][LW]::GetWindowRect($w, [ref]$wr)
  $s = ($wr.R - $wr.L) / 380.0
  [void][LW]::Front($w); Start-Sleep -Milliseconds 300   # in front but still narrow (0.2.146): pressing the arrow starts the 0.22 s widening
  $l = [LW]::ClickChildNow($w, 1); $lEnd = $null
  Start-Sleep -Milliseconds 1200
  $lEnd = [LW]::AccLoc($w, 1)
  $moving = "$($l -join ',') -> $($lEnd -join ',')"   # the arrow was still moving when pressed if these differ
  $count = Boxes
  $b = Box; Answer $b 1
  Check LW17 "1|True" "$count|$($l[0] -ne $lEnd[0])" "arrow clicked while still moving (arrow at press -> at rest: $moving): exactly one submit"
  $form.Close()

  # LW18: shown while another window is in front
  $form = New-Object Windows.Forms.Form; $form.StartPosition = "Manual"; $form.Left = 10; $form.Top = 10; $form.Width = 260; $form.Height = 90
  $tb = New-Object Windows.Forms.TextBox; $tb.Width = 200; $tb.Text = "keep"; $form.Controls.Add($tb); $form.Show(); [Windows.Forms.Application]::DoEvents()
  LockNotice   # everything hidden
  [void][LW]::Front($form.Handle); $tb.Focus() | Out-Null; [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 500
  $caps0 = [LW]::GetKeyState(0x14) -band 1
  [void][LW]::PostMessageW($m, 0x8003, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 1200   # WM_SHOWME (no user input in 1Key)
  [Windows.Forms.Application]::DoEvents()
  $fgForm = [LW]::GetForegroundWindow() -eq $form.Handle
  $capsSame = (([LW]::GetKeyState(0x14) -band 1) -eq $caps0)
  # Two outcomes, both judged: Windows refused the foreground (the other window keeps focus and text), or granted it (then the
  # caret must be in the widget field - never split between apps). In both: the other window's text and CapsLock unchanged.
  if ($fgForm) { $res = "refused|$($tb.Focused)|$($tb.Text)|$capsSame"; $want = "refused|True|keep|True" }
  else { $f = [LW]::Focus((Widget)); $res = "granted|$($f -ne [IntPtr]::Zero -and $f -eq (Field 1))|$($tb.Text)|$capsSame"; $want = "granted|True|keep|True" }
  Check LW18 $want $res "posted show request: widget shown=$((Widget) -ne $null); foreground $(if ($fgForm) { 'refused - contrast measured' } else { 'granted - refusal NOT exercised this run' })"
  $form.Close()

  # LW19: start narrow with the caret in the field; the first key widens it
  LockNotice; TrayClick
  $w = Widget; [void][LW]::Front($w); Start-Sleep -Milliseconds 600
  $hr0 = New-Object LW+RECT; [void][LW]::GetWindowRect((Hosts)[0], [ref]$hr0)
  $caret = [LW]::Focus($w) -eq (Field 1)
  $e = Field 1
  [void][LW]::PostMessageW($e, 0x0100, [IntPtr]0x41, [IntPtr]0x001E0001); Start-Sleep -Milliseconds 700   # key only: 1Key's TranslateMessage makes the character
  $hr1 = New-Object LW+RECT; [void][LW]::GetWindowRect((Hosts)[0], [ref]$hr1)
  $w0 = $hr0.R - $hr0.L; $w1 = $hr1.R - $hr1.L
  Check LW19 "True|True|1" "$caret|$($w1 -gt $w0 + 40)|$([LW]::Len($e))" "opened: caret in the field, narrow ($w0 px); the first key widens it ($w1 px)"
  TrayClick
  $hr2 = New-Object LW+RECT; [void][LW]::GetWindowRect((Hosts)[0], [ref]$hr2); $w2 = $hr2.R - $hr2.L
  Check LW20 "True|1" "$($w2 -le $w0 + 4)|$([LW]::Len((Field 1)))" "widened + reopen request: narrow again ($w2 px), typed text kept"
  Quit

  # LW24-LW26 (0.2.159, Codex R148-3): a late render-failure notice (WM_LOCKWIDGET code 4, lParam = widget number) is posted by the
  # harness. A fresh 1Key shows widget #1; every Show adds one, a hide keeps the number.
  Launch
  $first = (Widget) -ne $null
  LockNotice; TrayClick   # hidden (#1), reopened (#2)
  [void][LW]::PostMessageW($m, 0x8000 + 60, [IntPtr]4, [IntPtr]1); Start-Sleep -Milliseconds 900
  Check LW24 "True|True|False|0" "$first|$((Widget) -ne $null)|$(MainShown)|$(Boxes)" "late failure notice from widget #1 while #2 is shown: ignored"
  LockNotice   # #2 hidden by a session lock
  [void][LW]::PostMessageW($m, 0x8000 + 60, [IntPtr]4, [IntPtr]2); Start-Sleep -Milliseconds 900
  $quiet = "$((Widget) -eq $null)|$(MainShown)|$(Boxes)"
  TrayClick   # the failure was taken: this display session now uses the old lock screen
  Check LW25 "True|False|0|True|True|True" "$quiet|$((Widget) -eq $null)|$(MainShown)|$(OnOldLock)" "failure notice after the widget was hidden: main window stays hidden; reopening shows the old lock screen"
  [void][LW]::SendMessageW([LW]::GetDlgItem($m, 101), 0x000C, [IntPtr]::Zero, [string]$master); [void][LW]::PostMessageW([LW]::GetDlgItem($m, 103), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 1200
  $un = Unlocked
  [void][LW]::PostMessageW($m, 0x8000 + 60, [IntPtr]4, [IntPtr]2); Start-Sleep -Milliseconds 900
  $after = "$(Unlocked)|$(Boxes)"
  ListLock
  Check LW26 "True|True|0|True" "$un|$after|$((Widget) -ne $null)" "failure notice after unlock: ignored; the next lock uses the widget again"
  Quit

  # LW27 (0.2.159, Codex R148-1): a bad or extreme lockwidget.pos is ignored or clamped - the widget still opens inside the work area
  $pos = Join-Path $cfg "lockwidget.pos"
  $cases = [ordered]@{ garbage = "abc"; empty = ""; long = ("1,1" + (" " * 200)); overflow = "99999999999,5"; intmin = "-2147483648,-2147483648"; limit = "2000000,0"; far = "900000,900000"; dir = $null }
  $bad = @()
  foreach ($k in $cases.Keys) {
    Remove-Item $pos -Recurse -Force -ErrorAction Ignore
    if ($k -eq "dir") { New-Item -ItemType Directory -Force $pos | Out-Null } else { [IO.File]::WriteAllText($pos, $cases[$k]) }
    Launch
    $w = Widget; $ok = $false
    if ($w) {
      $r = New-Object LW+RECT; [void][LW]::GetWindowRect($w, [ref]$r)
      $wa = [System.Windows.Forms.Screen]::FromHandle($w).WorkingArea
      $ok = $r.L -ge $wa.Left -and $r.T -ge $wa.Top -and $r.R -le $wa.Right -and $r.B -le $wa.Bottom
    }
    if (-not ($ok -and (Alive))) { $bad += $k }
    Quit
  }
  Remove-Item $pos -Recurse -Force -ErrorAction Ignore
  Check LW27 "" ($bad -join ",") "bad/extreme lockwidget.pos ($($cases.Keys -join ', ')): widget opens inside the work area, 1Key keeps running"

  # ---- R148-2 (0.2.167)
  $env:ONEKEY_CONFIG_DIR = $cfg
  Launch
  LockNotice; TrayClick   # opened from the tray: narrow, caret in the field
  $w = Widget; [void][LW]::Front($w); Start-Sleep -Milliseconds 600
  $hr0 = New-Object LW+RECT; [void][LW]::GetWindowRect((Hosts)[0], [ref]$hr0)
  $e = Field 1
  [LW]::Key(0x41); Start-Sleep -Milliseconds 500
  $hr1 = New-Object LW+RECT; [void][LW]::GetWindowRect((Hosts)[0], [ref]$hr1)
  $first = "$([LW]::Len($e))|$(($hr1.R - $hr1.L) -gt ($hr0.R - $hr0.L) + 40)"
  [LW]::Key(0x42); [LW]::Key(0x43); [LW]::Key(0x44)
  $l4 = [LW]::Len($e)
  [LW]::KeyX(0x24); $atHome = [LW]::SelEnd($e)   # not $home: $HOME is read-only
  [LW]::KeyX(0x2E); $afterDel = "$([LW]::Len($e)),$([LW]::SelEnd($e))"
  [LW]::KeyX(0x23); $end = [LW]::SelEnd($e)
  [LW]::Key(0x08); $afterBs = [LW]::Len($e)
  Check LW29 "1|True|4|0|3,0|3|2" "$first|$l4|$atHome|$afterDel|$end|$afterBs" "narrow + first real key: one char, widens; Home/Delete/End/Backspace exact"

  # LW31: wrong master + Enter, reopen request while the error box is open
  SetW 1 ""; [LW]::Key(0x58); [LW]::Key(0x59)   # "XY" - wrong
  $wide = New-Object LW+RECT; [void][LW]::GetWindowRect((Hosts)[0], [ref]$wide)
  [LW]::Key(0x0D); $b = Box
  $disabled = -not [LW]::IsWindowEnabled((Hosts)[0])
  TrayClick
  $during = New-Object LW+RECT; [void][LW]::GetWindowRect((Hosts)[0], [ref]$during)
  $still = [Math]::Abs(($during.R - $during.L) - ($wide.R - $wide.L)) -le 2 -and [Math]::Abs($during.L - $wide.L) -le 2
  Answer $b 1; Start-Sleep -Milliseconds 500
  $back = [LW]::IsWindowEnabled((Hosts)[0]) -and ([LW]::Focus((Widget)) -eq (Field 1))
  Check LW31 "True|True|True|True" "$($b -ne [IntPtr]::Zero)|$disabled|$still|$back" "error box open: inputs disabled; reopen request keeps the widget wide (width $($wide.R - $wide.L) -> $($during.R - $during.L)); box closed: field back"

  # R167-T1 (Codex 11:50): the cancel tests hold the RIGHT master, so a wrong submit would unlock and be caught. LW36 is the control:
  # the same value and the same press/release path on the arrow unlocks.
  # LW36: control - right master, press and release on the arrow = unlocked
  $w = Widget; [void][LW]::Front($w); Start-Sleep -Milliseconds 400
  SetW 1 $master
  $pt = [LW]::HoldChild($w, 1); Start-Sleep -Milliseconds 80; [LW]::Release(); Start-Sleep -Milliseconds 1300
  $ctl = Unlocked
  $b = Box; if ($b -ne [IntPtr]::Zero) { Answer $b 1 }
  ListLock   # back to the widget for the cancel tests
  Check LW36 "True|True" "$ctl|$((Widget) -ne $null)" "control: right master + press/release on the arrow unlocks (the cancel tests below can see a wrong submit)"

  # LW32: right master, press the arrow, drag off, release = cancelled
  $w = Widget; [void][LW]::Front($w); Start-Sleep -Milliseconds 400
  SetW 1 $master
  $pt = [LW]::HoldChild($w, 1); Start-Sleep -Milliseconds 80
  $held = [LW]::Capture($w) -eq $w
  [LW]::MoveTo($pt[0] - 160, $pt[1] - 120); [LW]::Release(); Start-Sleep -Milliseconds 1300
  Check LW32 "True|0|True|True" "$held|$(Boxes)|$([LW]::Capture((Widget)) -eq [IntPtr]::Zero)|$(-not (Unlocked))" "right master, arrow pressed (captured), dragged off, released: still locked, capture released"

  # LW33: right master, press the arrow, minimized while held, release
  $w = Widget; [void][LW]::Front($w); Start-Sleep -Milliseconds 400
  SetW 1 $master
  $pt = [LW]::HoldChild($w, 1); Start-Sleep -Milliseconds 80
  $held = [LW]::Capture($w) -eq $w
  [void][LW]::ShowWindowAsync($w, 6); Start-Sleep -Milliseconds 600   # minimized from outside while held (ShowWindowAsync - not the real Win+D key)
  $min = [LW]::IsIconic($w)
  $capAfterMin = [LW]::Capture($w) -eq [IntPtr]::Zero
  [LW]::Release(); Start-Sleep -Milliseconds 1300
  $res = "$held|$min|$capAfterMin|$(Boxes)|$(-not (Unlocked))"
  TrayClick
  Check LW33 "True|True|True|0|True|True" "$res|$(-not [LW]::IsIconic((Widget)))" "right master, arrow held (captured), minimized: capture released at once; released: still locked; tray restores"

  # LW34: right master, press the arrow, session lock while held, release; no late submit after reopening
  $w = Widget; [void][LW]::Front($w); Start-Sleep -Milliseconds 400
  SetW 1 $master
  $pt = [LW]::HoldChild($w, 1); Start-Sleep -Milliseconds 80
  $held = [LW]::Capture($w) -eq $w
  LockNoticeShown   # on screen: the widget is made again (0.3.18) - the held press is dropped with the old one
  $gone = ((Widget) -ne $null) -and ((Widget) -ne $w)
  [LW]::Release(); Start-Sleep -Milliseconds 700
  $res = "$held|$gone|$(Boxes)|$(-not (Unlocked))|$(-not (MainShown))"
  TrayClick; Start-Sleep -Milliseconds 1000
  Check LW34 "True|True|0|True|True|True|True" "$res|$((Widget) -ne $null)|$(-not (Unlocked))" "right master, arrow held (captured), session lock, released: nothing submitted or shown; reopened and still locked a second later (window gone = capture gone; the capture owner is not read after the window is destroyed)"

  # LW35: a long value, then narrowed
  $w = Widget; [void][LW]::Front($w); Start-Sleep -Milliseconds 400
  $long = -join (1..60 | ForEach-Object { [char](97 + ($_ % 26)) })
  SetW 1 $long; $e = Field 1
  [void][LW]::SendMessageW($e, 0x00B1, [IntPtr]60, [IntPtr]60)   # EM_SETSEL caret at the end
  TrayClick; Start-Sleep -Milliseconds 600   # reopen request: narrow
  $e = Field 1
  $x = [LW]::PosX($e, 59); $cw = [LW]::ClientW($e)
  Check LW35 "60|60|True" "$([LW]::Len($e))|$([LW]::SelEnd($e))|$($x -ge 0 -and $x -lt $cw)" "60 chars then narrowed: caret at the end, last char inside the field (x $x of $cw)"

  # LW39: right master, arrow held, the widget's rendering fails (test message: fail the next render), then release
  $w = Widget; [void][LW]::Front($w); Start-Sleep -Milliseconds 400
  SetW 1 $master
  $pt = [LW]::HoldChild($w, 1); Start-Sleep -Milliseconds 80
  $held = [LW]::Capture($w) -eq $w
  [void][LW]::PostMessageW($w, 0x8000 + 65, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 900   # WM_TEST_FAIL_NOW
  $gone = (Widget) -eq $null
  [LW]::Release(); Start-Sleep -Milliseconds 900
  $b = Box; $info = $b -ne [IntPtr]::Zero; if ($info) { Answer $b 1 }
  $fieldEmpty = [LW]::Len([LW]::GetDlgItem($m, 101)) -eq 0
  Check LW39 "True|True|True|True|True|True" "$held|$gone|$info|$(-not (Unlocked))|$(OnOldLock)|$fieldEmpty" "right master, arrow held, render failure: widget closed, one notice, still locked on the old lock screen with an empty field (no late submit)"
  Quit

  # LW30: first setup, narrow, Tab / Shift+Tab
  $cfg5 = NewCfg "lockwidget_cfg5"; $env:ONEKEY_CONFIG_DIR = $cfg5
  Launch
  $w = Widget; [void][LW]::Front($w); Start-Sleep -Milliseconds 600
  $ok2 = @(Hosts).Count -eq 2
  [LW]::Key(0x41); [LW]::Key(0x09); $f2 = [LW]::Focus($w) -eq (Field 2)
  [LW]::Key(0x42); [LW]::Chord(0x10, 0x09); $f1 = [LW]::Focus($w) -eq (Field 1)
  Check LW30 "True|True|True|1|1" "$ok2|$f2|$f1|$([LW]::Len((Field 1)))|$([LW]::Len((Field 2)))" "first setup narrow: A, Tab, B, Shift+Tab - one char in each field"
  Quit
  $env:ONEKEY_CONFIG_DIR = $cfg

  # LW37 (R-W3a): the foreground refusal made for real - while the harness's own window is in front it calls
  # LockSetForegroundWindow(LSFW_LOCK), so 1Key's SetForegroundWindow on a show request must be refused. The typing target runs on its
  # own thread (Target); "a" is typed before the request as a control that the harness delivers keys at all, x y z while 1Key handles it.
  Launch
  LockNotice   # everything hidden
  $hf = [Target]::Start("keep")
  $fr = [Target]::Front(); Start-Sleep -Milliseconds 500
  $caps0 = [LW]::GetKeyState(0x14) -band 1
  $lockOk = [LW]::LockSetForegroundWindow(1)
  [LW]::KeyOwn(0x41); Start-Sleep -Milliseconds 300   # control: a
  [void][LW]::PostMessageW($m, 0x8003, [IntPtr]::Zero, [IntPtr]::Zero)   # WM_SHOWME
  foreach ($k in 0x58, 0x59, 0x5A) { [LW]::KeyOwn([byte]$k); Start-Sleep -Milliseconds 120 }
  Start-Sleep -Milliseconds 900
  $fgForm = [LW]::GetForegroundWindow() -eq $hf
  $res = "$lockOk|$fgForm|$([Target]::Focused())|$([Target]::Text().ToLowerInvariant())|$((([LW]::GetKeyState(0x14) -band 1) -eq $caps0))"
  [void][LW]::LockSetForegroundWindow(2)
  [Target]::Close()
  Check LW37 "True|True|True|keepaxyz|True" $res "foreground refusal made with LockSetForegroundWindow: show request refused, the other window keeps focus, its text, the control key (a) and every key typed meanwhile (x y z), CapsLock unchanged (widget shown=$((Widget) -ne $null), not activated; front=$fr)"
  Quit

  # LW38 (R-W2a): 255 / 256 / 257-character dummy masters - made in the widget with real keys, 1Key restarted, unlocked in the widget
  # with real keys, then unlocked again on the old lock screen (WM_SETTEXT) - all with the same value. Paste and IME are not covered.
  $lenRes = @()
  foreach ($n in 255, 256, 257) {
    $cn = NewCfg "lockwidget_len$n"; $env:ONEKEY_CONFIG_DIR = $cn
    $v = -join (0..($n - 1) | ForEach-Object { $chars[$_ % $chars.Length] })
    $made = $false; $un = $false; $old = $false
    Launch
    $w = Widget
    if ($w -and [LW]::Front($w)) {
      Start-Sleep -Milliseconds 500
      foreach ($ch in $v.ToCharArray()) { $vk = if ($ch -match '[a-z]') { [byte][char]([string]$ch).ToUpper() } else { [byte][char]$ch }; [LW]::Key($vk) }
      [LW]::Key(0x09)
      foreach ($ch in $v.ToCharArray()) { $vk = if ($ch -match '[a-z]') { [byte][char]([string]$ch).ToUpper() } else { [byte][char]$ch }; [LW]::Key($vk) }
      $lens = "$([LW]::Len((Field 1)))/$([LW]::Len((Field 2)))"
      [LW]::Key(0x0D); Start-Sleep -Milliseconds 1500
      $b = Box; if ($b -ne [IntPtr]::Zero) { Answer $b 6 }   # a weak-master advice ("use anyway?" - yes)
      Start-Sleep -Milliseconds 800
      $made = (Test-Path "$cn\config.dat") -and (Unlocked)
    }
    Quit
    Launch
    $w = Widget
    if ($w -and [LW]::Front($w)) {
      Start-Sleep -Milliseconds 500
      foreach ($ch in $v.ToCharArray()) { $vk = if ($ch -match '[a-z]') { [byte][char]([string]$ch).ToUpper() } else { [byte][char]$ch }; [LW]::Key($vk) }
      [LW]::Key(0x0D); Start-Sleep -Milliseconds 1500
      $un = Unlocked
    }
    Quit
    $env:ONEKEY_TEST_LOCKWIDGET = "0"
    Launch
    [void][LW]::SendMessageW([LW]::GetDlgItem($m, 101), 0x000C, [IntPtr]::Zero, $v); [void][LW]::PostMessageW([LW]::GetDlgItem($m, 103), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 1500
    $old = Unlocked
    Quit
    $env:ONEKEY_TEST_LOCKWIDGET = "1"
    $lenRes += "${n}:$made/$un/$old ($lens)"
  }
  $env:ONEKEY_CONFIG_DIR = $cfg
  Check LW38 "255:True/True/True (255/255)|256:True/True/True (256/256)|257:True/True/True (257/257)" ($lenRes -join "|") "255/256/257-char masters: made in the widget, restart + widget unlock, old lock screen unlock"
}
catch { Add-Failure ("aborted: " + $_.Exception.Message) }
finally {
  if ($script:p -and -not $script:p.HasExited) { Stop-Process -Id $script:p.Id -Force -ErrorAction Ignore }
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST_LOCKWIDGET = "0"; $env:ONEKEY_TEST_LOCKWIDGET_FAIL = $null
}
Complete-Checks
