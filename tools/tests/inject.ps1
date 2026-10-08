# Foreground input harness (T10). NOT part of the default regression: run it only with -Foreground, on a PC nobody is
# typing on. It brings its own fake windows to the front and 1Key really types a dummy string into them.
#
#   powershell -ExecutionPolicy Bypass -File tools\tests\inject.ps1 -Foreground [exe]
#
# Fake windows (all in this harness process): A (two multi-line EDITs A1, A2), B (one EDIT), and C whose window class is
# "Chrome_WidgetWin_1" (one EDIT) to stand in for a browser. Only dummy strings are used. 1Key runs isolated (test suffix,
# own config folder). Synchronisation is deterministic: ONEKEY_TEST_HOLD_EVENT + "arm" events make 1Key stop at a point
# (wait / char1 / enter) until the harness has changed the window or focus, then continue (no timing guesses).
# Hotkeys are simulated by posting WM_HOTKEY to 1Key (FireSlot then reads the real foreground window as usual).
# At the end the fake windows are destroyed and the window that was in front before is brought back.
# Items with several inputs (0.2.65, user decision 2026-10-01): fake window D has two single-line fields (a login form; its
# message loop does dialog navigation, so Tab moves to the next field) that already contain old values:
# IJ36 ID -> Tab -> password: each field's old value is replaced (select all first), no Enter
# IJ37 the same with the window-message method (EM_SETSEL + posted Tab)
# IJ38 the window changes after the first character of the first input: stops, the second field keeps its old value, B gets nothing
# Program link (0.2.76, user request 2026-10-01): an item whose input 1 is linked to field D1 of the fake program window D
# IJ39 linking: the edit screen shows the program and the field class (the field focused when the chip's [Link] was pressed);
#      input 2's [Link] stays visible but disabled, its row says why (it follows with Tab, 0.2.85)
# IJ40 with window A in front, the item's shortcut brings D to the front, puts input 1 into D1 (old value replaced), Tab, input 2
# IJ41 with D hidden, the shortcut types nothing anywhere (A in front stays empty)
# Program identity (0.2.83, Codex R82): fake window E behaves like the in-house login. Its inputs E1/E2 sit in host panels
# H1/H2 (class OneKeyFakeHost); "logged out", every input is moved out of the window and clicking a host brings its input back
# (and moves the others out). Slot 8 links input 1 to E1. Every case starts with A in front (A1 focused): A1 must stay empty.
# IJ42 the link stores the full path of the program
# IJ43 control: logged out, the shortcut clicks the linked place once (host click), ID, Tab, password
# IJ44 R82-1: a button covers the linked place inside the same host: no click on it, nothing typed
# IJ45 R82-2: a same-class field is added in front of the linked one: nothing typed into any field
# IJ46 control: both inputs on screen in the original order: ID, Tab, password
# IJ47 R82-2: the same-class fields change order: nothing typed
# IJ48 the link's path is changed to another folder (test hook): nothing typed, the notice shows both paths
# IJ49 the link is turned into an older one (file name only, test hook): nothing typed, link-again notice
# Edit screen "Type into" (0.2.88, user proposal 2026-10-01): the [Link] rows exist only for "a linked site or program field"
# IJ56 a new item has no [Link]; after choosing the linked kind it has; saving it without a link is refused with a warning
# R75-1 dedicated test (Codex 15:11/17:20): links made incomplete in memory by a test hook (bypassing the save check) on the
# two-input item; D (login form, old values) in front with the cursor in D1:
# IJ50 shortcut, IJ51 list [Type], IJ52 tray item (the tray menu's entry point): input 1 only / input 2 only / two pages ->
#      nothing typed into D (no characters, Tab or Enter), no chip (the confirm key afterwards types nothing either)
# IJ53 control: links removed again -> the shortcut types ID, Tab, password as before
# R75-3 for the program place click (barrier "appplace.mods", after the modifier wait, before the place check):
# IJ54 a button appears on the place at the barrier: not clicked, nothing typed
# IJ55 1Key is locked at the barrier: no click, nothing typed
# 0.2.90 (Codex 06:48): the fake windows count every key, character and select-all message and mouse press they receive
# (cumulative); the "nothing typed" checks above also require these counts to stay the same, not only the final text.
# IJ57 judge sensitivity: a key, a character and a select-all posted on purpose to D1, a key to A1 and a press to E1 are counted
# Edit screen "Use" (0.2.90) on the program-linked item (slot 7); switching to "type at the cursor" asks first:
# IJ58 1Key is locked while that question is open: after unlocking the item is still linked (nothing removed on disk)
# IJ59 answering No: still "fill a site or program", still linked
# IJ60 answering Yes and then leaving without saving: the item is still linked on disk
# IJ61 (0.2.92, Codex 08:01) the item's "click the field if the cursor does not go in" switched off and saved: logged out, the
#      shortcut neither clicks the place nor types anything (the user clicks the ID field first); switched back on afterwards
# IJ62 control with the switch off: both inputs on screen (SetFocus works) -> typed as usual, no click
# -Repeat05 <n> (Codex 14:00, IJ05 failed once on 0.2.98 although 1Key logged "enter-sent"): after IJ00-IJ05 run the IJ05
# case n more times and stop. Each round records how long the Enter took, the foreground and focused window, the trace lines,
# and how many Enter key-downs reached A (the target) and any other fake window; a failed round waits 5 s more and looks again
# for a late Enter in A1 or anywhere else (A2, B1, C1). Each round also records A's keyboard layout and IME open state
# (Codex 14:30: to pin the environment). Rounds go to <test dir>\ij05-repeat.txt with the exe and harness SHA-256 at the top
# and a totals line at the end. Limit: only the harness's own fake windows are counted, not other processes.
# IJ05R all rounds got text + Enter in A1, and no Enter reached any other field, late or not
# Judgement: tools\tests\lib\Check.ps1 (T18). ASCII only.
param([switch]$Foreground, [string]$Exe = "", [int]$Repeat05 = 0)
$ErrorActionPreference = "Continue"
if (-not $Foreground) { "inject.ps1 types into windows in the foreground. Run it with -Foreground on a PC nobody is using."; exit 2 }
. (Join-Path $PSScriptRoot "lib\Check.ps1")
Start-Checks -Required @(
  'IJ00','IJ01','IJ02','IJ03','IJ04','IJ05','IJ06','IJ07','IJ08','IJ09','IJ10','IJ11',
  'IJ12','IJ13','IJ14','IJ15','IJ16','IJ17','IJ18','IJ19','IJ20','IJ21','IJ22','IJ23','IJ24','IJ25','IJ26','IJ27',
  'IJ28','IJ29','IJ30','IJ31','IJ32','IJ33','IJ34','IJ35','IJ36','IJ37','IJ38','IJ39','IJ40','IJ41',
  'IJ42','IJ43','IJ44','IJ45','IJ46','IJ47','IJ48','IJ49','IJ50','IJ51','IJ52','IJ53','IJ54','IJ55','IJ56','IJ57','IJ58','IJ59','IJ60','IJ61','IJ62')
if ($Repeat05 -gt 0) { Start-Checks -Required @('IJ00','IJ01','IJ02','IJ03','IJ04','IJ05','IJ05R') }

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$suffix = ".ij"; $hold = "OneKeyTestHold_" + [Guid]::NewGuid().ToString("N").Substring(0, 8)
$cfg = "$sp\inject_cfg"; if (Test-Path $cfg) { Get-ChildItem $cfg -Recurse | Remove-Item -Force -Recurse -ErrorAction Ignore }; New-Item -ItemType Directory -Force $cfg | Out-Null
$Plain = "dummy123"; $WithEnter = "dummy456"; $Pm = "dummy789"; $PmEnter = "dummy790"; $BrEnter = "dummy791"
$MiId = "dummy-id"; $MiPw = "dummy-pw"; $PmId = "pm-id"; $PmPw = "pm-pw"
Add-Type @'
using System;using System.Runtime.InteropServices;using System.Text;using System.Threading;
public class U {
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
  [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
  [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool f);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint tid, ref GTI g);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
  [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; }
  [StructLayout(LayoutKind.Sequential)] public struct GTI { public int cbSize; public int flags; public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret; public RECT rcCaret; }
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static string Text(IntPtr h) { var t = new StringBuilder(1024); SendMessageW(h, 0x000D, (IntPtr)1024, t); return t.ToString(); }
  public static IntPtr FindCls(uint pid, string cls) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls && IsWindowVisible(h)) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
  public static IntPtr Focus(IntPtr w) { uint pid; uint tid = GetWindowThreadProcessId(w, out pid); var g = new GTI(); g.cbSize = Marshal.SizeOf(g); return GetGUIThreadInfo(tid, ref g) ? g.hwndFocus : IntPtr.Zero; }
  // Bring a window to the front even though this process is not in the foreground: attach to the foreground thread first.
  public static bool Front(IntPtr h) {
    for (int i = 0; i < 5; i++) {
      IntPtr fg = GetForegroundWindow(); uint pid; uint fgTid = GetWindowThreadProcessId(fg, out pid), me = GetCurrentThreadId();
      bool att = fgTid != 0 && fgTid != me && AttachThreadInput(me, fgTid, true);
      try { ShowWindow(h, 5); BringWindowToTop(h); SetForegroundWindow(h); } finally { if (att) AttachThreadInput(me, fgTid, false); }
      Thread.Sleep(150);
      if (GetForegroundWindow() == h) return true;
    }
    return false;
  }
  public static void ClickAt(int x, int y) { mouse_event(0x0002, 0, 0, 0, IntPtr.Zero); Thread.Sleep(60); mouse_event(0x0004, 0, 0, 0, IntPtr.Zero); }
}
// Fake target windows on their own thread with a message loop.
public class Fake {
  delegate IntPtr WndProcD(IntPtr h, uint m, IntPtr w, IntPtr l);
  static WndProcD _proc = Proc;
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] struct WNDCLASSEX { public int cbSize; public uint style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string lpszMenuName, lpszClassName; public IntPtr hIconSm; }
  [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern ushort RegisterClassExW(ref WNDCLASSEX wc);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr CreateWindowExW(uint ex, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr id, IntPtr inst, IntPtr p);
  [DllImport("user32.dll")] static extern IntPtr DefWindowProcW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] static extern int GetMessageW(out MSG m, IntPtr h, uint a, uint b);
  [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG m);
  [DllImport("user32.dll")] static extern bool IsDialogMessageW(IntPtr h, ref MSG m);
  [DllImport("user32.dll")] static extern IntPtr DispatchMessageW(ref MSG m);
  [DllImport("user32.dll")] static extern IntPtr SetFocus(IntPtr h);
  [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr h);
  [DllImport("user32.dll")] static extern void PostQuitMessage(int c);
  [DllImport("user32.dll")] static extern bool PostThreadMessageW(uint tid, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandleW(IntPtr n);
  [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
  public static IntPtr E, H0, H1, H2, E0, E1, E2, EBtn; public static int HostClicks, ButtonClicks;
  // every key / character / select-all message and mouse press that reaches a fake window, by top-level window (cumulative)
  public static int AKeys, DKeys, EKeys, EClicks;
  public static int AEnter, OtherEnter;   // Enter key-downs that reached A / any other fake window (IJ05R)
  [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint tid);
  [DllImport("imm32.dll")] static extern IntPtr ImmGetDefaultIMEWnd(IntPtr h);
  [DllImport("user32.dll")] static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  // keyboard layout of the fake windows' thread and the IME open state of field h (0 = closed/English, 1 = open), IJ05R
  public static string Kbd(IntPtr h) {
    string hkl = GetKeyboardLayout(_tid).ToInt64().ToString("X8");
    IntPtr ime = ImmGetDefaultIMEWnd(h);
    string open = ime == IntPtr.Zero ? "-" : SendMessageW(ime, 0x0283, (IntPtr)5, IntPtr.Zero).ToInt64().ToString();
    return "hkl=" + hkl + " ime=" + open;
  }
  [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint f);
  static void Count(ref MSG m) {
    uint g = m.message;
    if (g == 0x0100 && (long)m.wParam == 0x0D) { if (GetAncestor(m.hwnd, 2) == A) AEnter++; else OtherEnter++; }
    if (g == 0x0100 || g == 0x0104 || g == 0x0102 || g == 0x00B1) { IntPtr r = GetAncestor(m.hwnd, 2); if (r == A) AKeys++; else if (r == D) DKeys++; else if (r == E) EKeys++; }
    else if (g == 0x0201) { IntPtr r = GetAncestor(m.hwnd, 2); if (r == E) EClicks++; }
  }
  public static IntPtr A, B, C, D, A1, A2, B1, C1, D1, D2; static uint _tid; static ManualResetEvent _ready = new ManualResetEvent(false);
  public static bool Start() { var t = new Thread(Run); t.SetApartmentState(ApartmentState.STA); t.IsBackground = true; t.Start(); return _ready.WaitOne(5000) && A != IntPtr.Zero && C != IntPtr.Zero; }
  static void Reg(string name) { var wc = new WNDCLASSEX(); wc.cbSize = Marshal.SizeOf(wc); wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc); wc.hInstance = GetModuleHandleW(IntPtr.Zero); wc.hbrBackground = (IntPtr)6; wc.lpszClassName = name; RegisterClassExW(ref wc); }
  static IntPtr Top(string cls, string title, int x) { return CreateWindowExW(0, cls, title, 0x10CF0000, x, 120, 360, 220, IntPtr.Zero, IntPtr.Zero, GetModuleHandleW(IntPtr.Zero), IntPtr.Zero); }
  static IntPtr Line(IntPtr parent, int i) { return CreateWindowExW(0x200, "EDIT", "", 0x50810080, 12, 12 + i * 40, 320, 28, parent, (IntPtr)(100 + i), GetModuleHandleW(IntPtr.Zero), IntPtr.Zero); }
  static IntPtr Edit(IntPtr parent, int i) { return CreateWindowExW(0x200, "EDIT", "", 0x50810044, 12, 12 + i * 90, 320, 80, parent, (IntPtr)(100 + i), GetModuleHandleW(IntPtr.Zero), IntPtr.Zero); }
  static IntPtr Host(IntPtr parent, int y) { return CreateWindowExW(0x10000, "OneKeyFakeHost", "", 0x50000000, 12, y, 300, 44, parent, IntPtr.Zero, GetModuleHandleW(IntPtr.Zero), IntPtr.Zero); }
  static IntPtr HostEdit(IntPtr host, int id) { return CreateWindowExW(0x200, "EDIT", "", 0x50810080, 8, 8, 280, 28, host, (IntPtr)id, GetModuleHandleW(IntPtr.Zero), IntPtr.Zero); }
  static void Away(IntPtr ed) { if (ed != IntPtr.Zero) SetWindowPos(ed, IntPtr.Zero, -3000, 8, 0, 0, 0x0015); }
  static void Back(IntPtr ed) { if (ed != IntPtr.Zero) SetWindowPos(ed, IntPtr.Zero, 8, 8, 0, 0, 0x0015); }
  // a click on a host: its input comes back and takes the focus, the other inputs go out of the window (like the in-house login)
  static void HostClick(IntPtr h) { HostClicks++; IntPtr ed = h == H1 ? E1 : h == H2 ? E2 : E0; foreach (var o in new[] { E0, E1, E2 }) if (o != ed) Away(o); Back(ed); SetFocus(ed); }
  static void EMsg(uint m) {
    if (m == 0x8010) { Away(E0); Away(E1); Away(E2); }                                                   // logged out
    else if (m == 0x8011) { EBtn = CreateWindowExW(0, "BUTTON", "Fake action", 0x50000000, 8, 8, 280, 28, H1, (IntPtr)310, GetModuleHandleW(IntPtr.Zero), IntPtr.Zero); SetWindowPos(EBtn, IntPtr.Zero, 0, 0, 0, 0, 0x0013); }   // a button on the linked place
    else if (m == 0x8012) { H0 = Host(E, 116); E0 = HostEdit(H0, 300); U.SendMessageW(E0, 0x000C, IntPtr.Zero, "old-x"); SetWindowPos(H0, IntPtr.Zero, 0, 0, 0, 0, 0x0013); }   // a same-class field first in z-order
    else if (m == 0x8013) { SetWindowPos(H2, IntPtr.Zero, 0, 0, 0, 0, 0x0013); }                        // H2 before H1
    else if (m == 0x8015) {                                                                              // reset: both inputs on screen, H1 then H2
      if (EBtn != IntPtr.Zero) DestroyWindow(EBtn); if (H0 != IntPtr.Zero) DestroyWindow(H0); EBtn = H0 = E0 = IntPtr.Zero;
      SetWindowPos(H1, (IntPtr)1, 0, 0, 0, 0, 0x0013); SetWindowPos(H2, (IntPtr)1, 0, 0, 0, 0, 0x0013); Back(E1); Back(E2); HostClicks = 0; ButtonClicks = 0; }
  }
  static void Run() {
    _tid = GetCurrentThreadId();
    Reg("OneKeyFakeTargetA"); Reg("OneKeyFakeTargetB"); Reg("Chrome_WidgetWin_1"); Reg("OneKeyFakeTargetD"); Reg("OneKeyFakeTargetE"); Reg("OneKeyFakeHost");
    A = Top("OneKeyFakeTargetA", "1Key test target A", 80); A1 = Edit(A, 0); A2 = Edit(A, 1);
    B = Top("OneKeyFakeTargetB", "1Key test target B", 460); B1 = Edit(B, 0);
    C = Top("Chrome_WidgetWin_1", "1Key test target C (browser class)", 840); C1 = Edit(C, 0);
    D = Top("OneKeyFakeTargetD", "1Key test target D (login form)", 460); D1 = Line(D, 0); D2 = Line(D, 1);
    E = Top("OneKeyFakeTargetE", "1Key test target E (program login)", 460); H1 = Host(E, 12); E1 = HostEdit(H1, 301); H2 = Host(E, 64); E2 = HostEdit(H2, 302);
    _ready.Set();
    MSG m; while (GetMessageW(out m, IntPtr.Zero, 0, 0) > 0) { Count(ref m); if (IsDialogMessageW(D, ref m) || IsDialogMessageW(E, ref m)) continue; TranslateMessage(ref m); DispatchMessageW(ref m); }
  }
  static IntPtr Proc(IntPtr h, uint m, IntPtr w, IntPtr l) {
    if (h == E && h != IntPtr.Zero && m >= 0x8010 && m <= 0x8015) { EMsg(m); return IntPtr.Zero; }
    if (h == E && h != IntPtr.Zero && m == 0x8001) { SetFocus(w == IntPtr.Zero ? E1 : E2); return IntPtr.Zero; }
    if (m == 0x0201 && h != IntPtr.Zero && (h == H0 || h == H1 || h == H2)) { HostClick(h); return IntPtr.Zero; }
    if (m == 0x0111 && EBtn != IntPtr.Zero && l == EBtn) { ButtonClicks++; return IntPtr.Zero; }
    if (m == 0x8001) { SetFocus(GetDlgItem(h, 100 + (int)w)); return IntPtr.Zero; }   // focus the i-th EDIT
    if (m == 0x8002) { DestroyWindow(h); return IntPtr.Zero; }
    if (m == 0x0010) return IntPtr.Zero;                                              // ignore WM_CLOSE from outside
    return DefWindowProcW(h, m, w, l);
  }
  public static void Stop() { foreach (var h in new[] { A, B, C, D, E }) if (h != IntPtr.Zero) U.SendMessageW(h, 0x8002, IntPtr.Zero, IntPtr.Zero); PostThreadMessageW(_tid, 0x0012, IntPtr.Zero, IntPtr.Zero); }
}
'@
function SetText($id, $s) { [void][U]::SendMessageW([U]::GetDlgItem($m, $id), 0x000C, [IntPtr]::Zero, [string]$s) }
function Click($id) { [void][U]::PostMessageW([U]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 500 }
function CloseBox($cmd = 2) { for ($i=0;$i -lt 10;$i++) { $b = [U]::FindCls([uint32]$p.Id, "OneKeyDialog"); if ($b -ne [IntPtr]::Zero) { [void][U]::PostMessageW($b, 0x0111, [IntPtr]$cmd, [IntPtr]::Zero); Start-Sleep -Milliseconds 300; return $true }; Start-Sleep -Milliseconds 100 }; $false }
# 1Key's own notice window (0.2.67+ shows failures there too): its text, for the record when a check fails
function ToastText() { $t = [U]::FindCls([uint32]$p.Id, "OneKeyToast"); if ($t -eq [IntPtr]::Zero) { "-" } else { $c = [U]::GetDlgItem($t, 101); if ($c -eq [IntPtr]::Zero) { [U]::Text($t) } else { [U]::Text($c) } } }
function Prefill() { [void][U]::SendMessageW([Fake]::D1, 0x000C, [IntPtr]::Zero, "old-id"); [void][U]::SendMessageW([Fake]::D2, 0x000C, [IntPtr]::Zero, "old-pw") }
function ClearAll() { foreach ($e in [Fake]::A1, [Fake]::A2, [Fake]::B1, [Fake]::C1) { [void][U]::SendMessageW($e, 0x000C, [IntPtr]::Zero, "") } }
function Target($win, $idx) { if (-not [U]::Front($win)) { throw "could not bring the fake window to the front (foreground lock)" }; [void][U]::SendMessageW($win, 0x8001, [IntPtr]$idx, [IntPtr]::Zero); Start-Sleep -Milliseconds 150 }
function Hotkey($slot) { [void][U]::PostMessageW($m, 0x0312, [IntPtr]$slot, [IntPtr]::Zero) }
function Settle() { Start-Sleep -Milliseconds 900 }
# arm a hold point; returns the handles to pass to Release
function Arm($point) { @{ Arm = New-Object System.Threading.EventWaitHandle($false, 'ManualReset', "Local\$hold.arm.$point"); Reached = New-Object System.Threading.EventWaitHandle($false, 'AutoReset', "Local\$hold.reached"); Go = New-Object System.Threading.EventWaitHandle($false, 'AutoReset', "Local\$hold.go") } }
function WaitReached($h) { $h.Reached.WaitOne(5000) }
# disarm first (so the next action cannot stop at this point again), then let 1Key continue
function Release($h) { $h.Arm.Dispose(); [void]$h.Go.Set(); Start-Sleep -Milliseconds 700; $h.Reached.Dispose(); $h.Go.Dispose() }
function StartChip($slot) { [void][U]::PostMessageW([U]::GetDlgItem($m, 1100 + $slot), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); for ($i = 0; $i -lt 20; $i++) { $c = [U]::FindCls([uint32]$p.Id, "OneKeyChip"); if ($c -ne [IntPtr]::Zero) { return $c }; Start-Sleep -Milliseconds 100 }; [IntPtr]::Zero }
function ChipInput($chip) { [void][U]::PostMessageW([U]::GetDlgItem($chip, 11), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) }
# "Use" (id 348): Down selects the next entry = "a linked site or program field"; the screen is rebuilt after the notification
function LinkKind() { [void][U]::PostMessageW([U]::GetDlgItem($m, 348), 0x0100, [IntPtr]0x28, [IntPtr]::Zero); Start-Sleep -Milliseconds 700 }
# key/char/select messages reaching A, D, E and presses on E (see Fake.Count)
function Keys() { "$([Fake]::AKeys)/$([Fake]::DKeys)/$([Fake]::EKeys)/$([Fake]::EClicks)" }
function KeyDelta($k0) { $a = $k0.Split('/'); $b = (Keys).Split('/'); "dA=$([int]$b[0]-[int]$a[0]) dD=$([int]$b[1]-[int]$a[1]) dE=$([int]$b[2]-[int]$a[2]) cE=$([int]$b[3]-[int]$a[3])" }
$NoKeys = "dA=0 dD=0 dE=0 cE=0"
function Show1Key() { [void][U]::PostMessageW($m, 0x8003, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 500 }

Add-Type @'
using System;using System.Runtime.InteropServices;
public class Idle { [StructLayout(LayoutKind.Sequential)] struct LII { public uint cbSize; public uint dwTime; }
 [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LII l);
 public static long Ms() { var l = new LII(); l.cbSize = 8; GetLastInputInfo(ref l); return (long)(uint)Environment.TickCount - l.dwTime; } }
'@
# someone typed or moved the mouse in the last 2 minutes: do not take over the screen
if ([Idle]::Ms() -lt 120000) { "The PC was used in the last 2 minutes (idle $([Math]::Round([Idle]::Ms()/1000)) s). Not running; try again when nobody is using it."; exit 2 }

$userFront = [U]::GetForegroundWindow()
try {
  Stop-TestInstances $suffix
  if (-not [Fake]::Start()) { throw "fake windows could not be created" }
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix
  $env:ONEKEY_TEST_WORKAREA = $TallScreen; $env:ONEKEY_TEST_HOLD_EVENT = $hold
  $p = Start-Process $exe -PassThru
  $m = [IntPtr]::Zero; for ($i = 0; $i -lt 80 -and $m -eq [IntPtr]::Zero; $i++) { $m = [U]::FindCls([uint32]$p.Id, "OneKeyMainWindow$suffix"); Start-Sleep -Milliseconds 100 }
  if ($m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 600
  SetText 101 "Master1234"; SetText 102 "Master1234"; Click 103
  # slot 0: plain dummy; slot 1: dummy + Enter. No global hotkeys (WM_HOTKEY is posted directly).
  Click 203; Click 4001; SetText 301 "Plain"; SetText 302 $Plain; [void][U]::PostMessageW([U]::GetDlgItem($m, 304), 0x0100, [IntPtr]0x08, [IntPtr]::Zero); Click 310; [void](CloseBox)
  Click 203; Click 4001; SetText 301 "WithEnter"; SetText 302 $WithEnter; [void][U]::PostMessageW([U]::GetDlgItem($m, 304), 0x0100, [IntPtr]0x08, [IntPtr]::Zero); [void][U]::SendMessageW([U]::GetDlgItem($m, 305), 0x00F1, [IntPtr]1, [IntPtr]::Zero); Click 310; [void](CloseBox)
  # confirm key -> bare F24 (the harness can also occupy it for the conflict case)
  # ... and auto-lock off (0.2.59 run, 2026-09-30: on a PC idle for 10+ minutes the default 10-minute auto-lock locked the test
  # instance right after start, so nothing could be typed and no chip appeared)
  Click 220; [void][U]::PostMessageW([U]::GetDlgItem($m, 2006), 0x0100, [IntPtr]0x87, [IntPtr]::Zero); [void][U]::PostMessageW([U]::GetDlgItem($m, 2005), 0x0100, [IntPtr]0x24, [IntPtr]::Zero); Start-Sleep -Milliseconds 200; Click 2012; [void](CloseBox)   # saving returns to the list
  Confirm-AutoLockOff $cfg
  # slots 2 and 3: window-message method (index 3 in the method list), the second with Enter (Codex V35-4)
  foreach ($x in @(@("PmPlain", $Pm, 0), @("PmEnter", $PmEnter, 1))) {
    Click 203; Click 4001; SetText 301 $x[0]; SetText 302 $x[1]; [void][U]::PostMessageW([U]::GetDlgItem($m, 304), 0x0100, [IntPtr]0x08, [IntPtr]::Zero)
    for ($k = 0; $k -lt 3; $k++) { [void][U]::PostMessageW([U]::GetDlgItem($m, 306), 0x0100, [IntPtr]0x28, [IntPtr]::Zero) }
    if ($x[2] -eq 1) { [void][U]::SendMessageW([U]::GetDlgItem($m, 305), 0x00F1, [IntPtr]1, [IntPtr]::Zero) }
    Start-Sleep -Milliseconds 200; Click 310; [void](CloseBox)
  }
  # slot 4: Enter with "no Enter in browsers" (user decision 2026-09-29, 0.2.41)
  Click 203; Click 4001; SetText 301 "BrowserEnter"; SetText 302 $BrEnter; [void][U]::PostMessageW([U]::GetDlgItem($m, 304), 0x0100, [IntPtr]0x08, [IntPtr]::Zero)
  [void][U]::SendMessageW([U]::GetDlgItem($m, 305), 0x00F1, [IntPtr]2, [IntPtr]::Zero)   # Enter send row (3-way since 0.3.5x): 2 = except browsers
  Start-Sleep -Milliseconds 200; Click 310; [void](CloseBox)
  # slots 5 and 6: two inputs each (ID, password), [+ add input] = 339, second input field = 331; slot 6 uses the window-message method
  foreach ($x in @(@("Login", $MiId, $MiPw, 0), @("PmLogin", $PmId, $PmPw, 3))) {
    Click 203; Click 4007; SetText 301 $x[0]; SetText 302 $x[1]; Click 339; SetText 331 $x[2]
    [void][U]::PostMessageW([U]::GetDlgItem($m, 304), 0x0100, [IntPtr]0x08, [IntPtr]::Zero)
    for ($k = 0; $k -lt $x[3]; $k++) { [void][U]::PostMessageW([U]::GetDlgItem($m, 306), 0x0100, [IntPtr]0x28, [IntPtr]::Zero) }
    Start-Sleep -Milliseconds 200; Click 310; [void](CloseBox)
  }
  Check IJ00 $true (([U]::GetDlgItem($m, 1000) -ne [IntPtr]::Zero) -and ([U]::GetDlgItem($m, 1001) -ne [IntPtr]::Zero)) "two dummy slots prepared"

  # ---- hotkey path
  ClearAll; Target ([Fake]::A) 0; Hotkey 0; Settle
  Check IJ01 $Plain ([U]::Text([Fake]::A1)) "baseline: hotkey types the dummy into the focused field A1"

  ClearAll; Target ([Fake]::A) 0; $h = Arm "wait"; Hotkey 0
  $ok = WaitReached $h; [void][U]::Front([Fake]::B); Release $h
  Check IJ02 "True||" ("$ok|" + [U]::Text([Fake]::A1) + "|" + [U]::Text([Fake]::B1)) "window changed while waiting: nothing typed in A or B"

  ClearAll; Target ([Fake]::A) 0; $h = Arm "char1"; Hotkey 0
  $ok = WaitReached $h; [void][U]::Front([Fake]::B); Release $h
  Check IJ03 "True|d|" ("$ok|" + [U]::Text([Fake]::A1) + "|" + [U]::Text([Fake]::B1)) "window changed after the first character: stops, B gets nothing"

  ClearAll; Target ([Fake]::A) 0; $h = Arm "enter"; Hotkey 1
  $ok = WaitReached $h; [void][U]::SendMessageW([Fake]::A, 0x8001, [IntPtr]1, [IntPtr]::Zero); Release $h
  Check IJ04 "True|$WithEnter|" ("$ok|" + [U]::Text([Fake]::A1) + "|" + [U]::Text([Fake]::A2)) "focus moved to A2 right before Enter: text in A1, no Enter anywhere"

  ClearAll; Target ([Fake]::A) 0; Hotkey 1
  $sw05 = [Diagnostics.Stopwatch]::StartNew(); while ($sw05.ElapsedMilliseconds -lt 3000 -and ([U]::Text([Fake]::A1)) -ne "$WithEnter`r`n") { Start-Sleep -Milliseconds 100 }   # Enter came 30 ms after the old 0.9 s check (0.2.74 trace)
  Check IJ05 "$WithEnter`r`n|" ([U]::Text([Fake]::A1) + "|" + [U]::Text([Fake]::A2)) "Enter slot in a normal window: text followed by Enter (A1|A2)"

  if ($Repeat05 -gt 0) {
    $rf = Join-Path $sp "ij05-repeat.txt"
    "exe $exe SHA-256 $((Get-FileHash $exe -Algorithm SHA256).Hash)" | Out-File $rf -Encoding utf8
    "harness $PSCommandPath SHA-256 $((Get-FileHash $PSCommandPath -Algorithm SHA256).Hash)" | Out-File $rf -Append -Encoding utf8
    "round|ok|ms|A1|A2|B1|C1|fg|focus|kbd|AEnter|OtherEnter|late|trace" | Out-File $rf -Append -Encoding utf8
    $good = 0; $stray = 0; $fails = 0; $sumA = 0; $sumO = 0; $otherText = 0; $lateHits = 0
    for ($r = 1; $r -le $Repeat05; $r++) {
      ClearAll; Target ([Fake]::A) 0
      $tr0 = @(Get-Content "$cfg\inject-trace.txt" -ErrorAction Ignore).Count
      $e0 = [Fake]::AEnter; $o0 = [Fake]::OtherEnter
      Hotkey 1
      $sw = [Diagnostics.Stopwatch]::StartNew(); while ($sw.ElapsedMilliseconds -lt 3000 -and ([U]::Text([Fake]::A1)) -ne "$WithEnter`r`n") { Start-Sleep -Milliseconds 50 }
      $ms = $sw.ElapsedMilliseconds
      $fg = [U]::Cls([U]::GetForegroundWindow()); $fo = [U]::Cls([U]::Focus([Fake]::A)); $kb = [Fake]::Kbd([Fake]::A1)
      $ok = ([U]::Text([Fake]::A1)) -eq "$WithEnter`r`n"
      $late = "-"
      $aMid = [Fake]::AEnter
      if (-not $ok) { $fails++; Start-Sleep -Seconds 5; $late = "A1=" + ([U]::Text([Fake]::A1) -replace "`r", '\r' -replace "`n", '\n') + " lateEnterA=" + ([Fake]::AEnter - $aMid); if ([Fake]::AEnter -ne $aMid) { $lateHits++ } }
      else { Start-Sleep -Milliseconds 600 }   # a late second Enter would land now
      $others = [U]::Text([Fake]::A2) + [U]::Text([Fake]::B1) + [U]::Text([Fake]::C1)
      $de = [Fake]::AEnter - $e0; $do = [Fake]::OtherEnter - $o0
      if ($ok -and $de -eq 1) { $good++ }
      if ($others -ne "" -or $do -ne 0) { $stray++ }
      $sumA += $de; $sumO += $do; if ($others -ne "") { $otherText++ }
      $tr = (@(Get-Content "$cfg\inject-trace.txt" -ErrorAction Ignore) | Select-Object -Skip $tr0) -join " / "
      "$r|$ok|$ms|$(([U]::Text([Fake]::A1)) -replace "`r", '\r' -replace "`n", '\n')|$([U]::Text([Fake]::A2))|$([U]::Text([Fake]::B1))|$([U]::Text([Fake]::C1))|$fg|$fo|$kb|$de|$do|$late|$tr" | Out-File $rf -Append -Encoding utf8
      Start-Sleep -Milliseconds 300
    }
    $tot = "rounds=$Repeat05 ok=$good failed=$fails AEnter=$sumA OtherEnter=$sumO roundsWithTextElsewhere=$otherText lateEnterAfterFail=$lateHits"
    "totals: $tot" | Out-File $rf -Append -Encoding utf8
    "---- IJ05R $tot"
    Check IJ05R "$Repeat05/$Repeat05|0" "$good/$Repeat05|$stray" "IJ05 repeated $Repeat05 times: text + exactly one Enter in A1 each time, no Enter or text elsewhere (rounds: $rf)"
    throw "repeat-done"
  }

  ClearAll; Target ([Fake]::C) 0; Hotkey 1
  $sw06 = [Diagnostics.Stopwatch]::StartNew(); while ($sw06.ElapsedMilliseconds -lt 3000 -and ([U]::Text([Fake]::C1)) -ne "$WithEnter`r`n") { Start-Sleep -Milliseconds 100 }   # Enter may come after the usual 0.9 s (two runs on 0.2.71)
  $fg06 = [U]::Cls([U]::GetForegroundWindow()); $toast06 = ToastText
  Check IJ06 "$WithEnter`r`n" ([U]::Text([Fake]::C1)) "browser-class window (Chrome_WidgetWin_1), Enter item: text followed by Enter (default since 0.2.41) [$($sw06.ElapsedMilliseconds) ms; front: $fg06; 1Key notice (may be left from IJ04): $toast06; trace: $((Get-Content "$cfg\inject-trace.txt" -ErrorAction Ignore | Select-Object -Last 3) -join ' / ')]"

  ClearAll; Target ([Fake]::C) 0; Hotkey 4; Settle
  Check IJ35 $BrEnter ([U]::Text([Fake]::C1)) "browser-class window, item with 'no Enter in browsers': text but no Enter"

  # own window: 1Key in front with its edit screen open; the hotkey must not type into 1Key itself
  Click 1000; $nameBefore = [U]::Text([U]::GetDlgItem($m, 301))
  [void][U]::Front($m); [void][U]::SendMessageW([U]::GetDlgItem($m, 301), 0x00B1, [IntPtr]0, [IntPtr]-1)
  Hotkey 0; Settle
  Check IJ07 $nameBefore ([U]::Text([U]::GetDlgItem($m, 301))) "own window in front: nothing typed into 1Key"
  Click 311

  # ---- input chip (D)
  ClearAll; $chip = StartChip 0
  Check IJ08 $true ($chip -ne [IntPtr]::Zero) "chip shown"
  Target ([Fake]::A) 0; ChipInput $chip; Settle
  Check IJ09 $Plain ([U]::Text([Fake]::A1)) "D1: field clicked, then chip [input]: dummy in that field"
  Check IJ10 $true ([U]::FindCls([uint32]$p.Id, "OneKeyChip") -eq [IntPtr]::Zero) "D1: chip gone after one input"

  Show1Key; ClearAll; $chip = StartChip 0
  [void][U]::Front([U]::GetShellWindow()); Start-Sleep -Milliseconds 200
  $hintBefore = [U]::Text([U]::GetDlgItem($chip, 14))
  ChipInput $chip; Start-Sleep -Milliseconds 500
  Check IJ11 $true (([U]::FindCls([uint32]$p.Id, "OneKeyChip") -ne [IntPtr]::Zero) -and ([U]::Text([U]::GetDlgItem($chip, 14)) -ne $hintBefore)) "D2: no field (desktop in front): chip stays and shows a warning"
  Check IJ12 "||" ([U]::Text([Fake]::A1) + "|" + [U]::Text([Fake]::B1) + "|" + [U]::Text([Fake]::C1)) "D2: nothing typed anywhere"

  Target ([Fake]::A) 0; $h = Arm "wait"; ChipInput $chip
  $ok = WaitReached $h; [void][U]::Front([Fake]::B); Release $h
  Check IJ13 "True||" ("$ok|" + [U]::Text([Fake]::A1) + "|" + [U]::Text([Fake]::B1)) "D3: window changed after the chip press: no input into the new window"

  Show1Key; ClearAll; $chip = StartChip 1
  Target ([Fake]::A) 0; $h = Arm "char1"; ChipInput $chip
  $ok = WaitReached $h; [void][U]::SendMessageW([Fake]::A, 0x8001, [IntPtr]1, [IntPtr]::Zero); Release $h
  Check IJ14 "True|d|" ("$ok|" + [U]::Text([Fake]::A1) + "|" + [U]::Text([Fake]::A2)) "D4: focus changed while typing: stops, no Enter"

  # D5: a real mouse click on the chip must not take the foreground or the focus away from the field
  Show1Key; ClearAll; $chip = StartChip 0
  Target ([Fake]::A) 0
  $r = New-Object 'U+RECT'; [void][U]::GetWindowRect([U]::GetDlgItem($chip, 11), [ref]$r)
  $cur = New-Object 'U+POINT'; [void][U]::GetCursorPos([ref]$cur)
  $h = Arm "wait"
  [void][U]::SetCursorPos([int](($r.L + $r.R) / 2), [int](($r.T + $r.B) / 2)); Start-Sleep -Milliseconds 100
  [U]::ClickAt(0, 0)
  $ok = WaitReached $h
  $fgDuring = [U]::GetForegroundWindow(); $focusDuring = [U]::Focus([Fake]::A)
  Release $h
  [void][U]::SetCursorPos($cur.X, $cur.Y)
  Check IJ15 $true ($ok -and $fgDuring -eq [Fake]::A) "D5: after a real click on the chip, A is still the foreground window"
  Check IJ16 $true ($focusDuring -eq [Fake]::A1) "D5: and A1 still has the keyboard focus"
  Check IJ17 $Plain ([U]::Text([Fake]::A1)) "D5: the dummy went into A1"

  # D6: after cancel, neither the chip nor the confirm key can type
  Show1Key; ClearAll; $chip = StartChip 0
  [void][U]::PostMessageW([U]::GetDlgItem($chip, 12), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 500
  Target ([Fake]::A) 0; [void][U]::PostMessageW($m, 0x0312, [IntPtr]0xB000, [IntPtr]::Zero); Settle
  Check IJ18 $true ([U]::FindCls([uint32]$p.Id, "OneKeyChip") -eq [IntPtr]::Zero) "D6: chip gone after cancel"
  Check IJ19 "" ([U]::Text([Fake]::A1)) "D6: confirm key after cancel types nothing"

  # D7: the confirm key does what [input] does
  Show1Key; ClearAll; $chip = StartChip 0
  Target ([Fake]::A) 0; [void][U]::PostMessageW($m, 0x0312, [IntPtr]0xB000, [IntPtr]::Zero); Settle
  Check IJ20 $Plain ([U]::Text([Fake]::A1)) "D7: confirm key types into the focused field"
  Check IJ21 $true ([U]::FindCls([uint32]$p.Id, "OneKeyChip") -eq [IntPtr]::Zero) "D7: chip gone"

  # D7: confirm key taken by another program -> warning before the chip; "No" -> no chip
  Show1Key
  $taken = [U]::RegisterHotKey([IntPtr]::Zero, 7001, 0x4000, 0x87)
  Check IJ22 $true $taken "harness occupies the confirm key (F24)"
  [void][U]::PostMessageW([U]::GetDlgItem($m, 1100), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
  Check IJ23 $true (CloseBox 7) "confirm key taken: a warning appears before any chip"
  Start-Sleep -Milliseconds 400
  Check IJ24 $true ([U]::FindCls([uint32]$p.Id, "OneKeyChip") -eq [IntPtr]::Zero) "answering No shows no chip"
  if ($taken) { [void][U]::UnregisterHotKey([IntPtr]::Zero, 7001) }

  # V35-3: the target is taken when the chip's [input] is pressed. Switch A -> B after the press, before 1Key handles it:
  # nothing may go into B (and A is no longer in front, so Send refuses it too)
  Show1Key; ClearAll; $chip = StartChip 0
  Target ([Fake]::A) 0; $h = Arm "chipqueued"; ChipInput $chip
  $ok = WaitReached $h; [void][U]::Front([Fake]::B); Release $h; Settle
  Check IJ28 "True||" ("$ok|" + [U]::Text([Fake]::A1) + "|" + [U]::Text([Fake]::B1)) "V35-3: window changed after the chip press: B gets nothing"

  # a chip command from an earlier chip (wrong generation) is ignored
  Show1Key; ClearAll; $chip = StartChip 0
  Target ([Fake]::A) 0; [void][U]::PostMessageW($m, 0x8008, [IntPtr]1, [IntPtr]987654); Settle
  Check IJ29 "" ([U]::Text([Fake]::A1)) "V35-3: stale chip command types nothing"
  Check IJ30 $true ([U]::FindCls([uint32]$p.Id, "OneKeyChip") -ne [IntPtr]::Zero) "V35-3: and the current chip keeps waiting"
  [void][U]::PostMessageW([U]::GetDlgItem($chip, 12), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 500

  # V35-4: the window-message method stops like the others
  ClearAll; Target ([Fake]::A) 0; Hotkey 2; Settle
  Check IJ31 $Pm ([U]::Text([Fake]::A1)) "V35-4: window-message method baseline"
  ClearAll; Target ([Fake]::A) 0; $h = Arm "char1"; Hotkey 2
  $ok = WaitReached $h; [void][U]::Front([Fake]::B); Release $h
  Check IJ32 "True|d|" ("$ok|" + [U]::Text([Fake]::A1) + "|" + [U]::Text([Fake]::B1)) "V35-4: window changed after the first character: stops (no more characters into A)"
  ClearAll; Target ([Fake]::A) 0; $h = Arm "enter"; Hotkey 3
  $ok = WaitReached $h; [void][U]::SendMessageW([Fake]::A, 0x8001, [IntPtr]1, [IntPtr]::Zero); Release $h
  Check IJ33 "True|$PmEnter|" ("$ok|" + [U]::Text([Fake]::A1) + "|" + [U]::Text([Fake]::A2)) "V35-4: focus moved before Enter: no Enter"
  ClearAll; Target ([Fake]::A) 0; Hotkey 3; Settle; Start-Sleep -Milliseconds 400
  Check IJ34 "$PmEnter`r`n" ([U]::Text([Fake]::A1)) "V35-4: window-message method with Enter still works when nothing moves"

  # ---- items with several inputs: ID -> Tab -> password into a login form whose fields already have values
  ClearAll; Prefill; Target ([Fake]::D) 0; Hotkey 5; Start-Sleep -Milliseconds 1500
  Check IJ36 "$MiId|$MiPw" ([U]::Text([Fake]::D1) + "|" + [U]::Text([Fake]::D2)) "two inputs: ID, Tab, password; old values replaced, no Enter"
  Prefill; Target ([Fake]::D) 0; Hotkey 6; Start-Sleep -Milliseconds 1500
  Check IJ37 "$PmId|$PmPw" ([U]::Text([Fake]::D1) + "|" + [U]::Text([Fake]::D2)) "two inputs with the window-message method: old values replaced"
  ClearAll; Prefill; Target ([Fake]::D) 0; $h = Arm "char1"; Hotkey 5
  $ok = WaitReached $h; [void][U]::Front([Fake]::B); Release $h; Settle
  Check IJ38 "True|d|old-pw|" ("$ok|" + [U]::Text([Fake]::D1) + "|" + [U]::Text([Fake]::D2) + "|" + [U]::Text([Fake]::B1)) "window changed during the first input: stops, the password field and B untouched"

  # ---- edit screen "Type into" (IJ56)
  Click 203; Click 4001; SetText 301 "NoLink"; SetText 302 "x"; [void][U]::PostMessageW([U]::GetDlgItem($m, 304), 0x0100, [IntPtr]0x08, [IntPtr]::Zero); Start-Sleep -Milliseconds 200
  $b56a = [U]::GetDlgItem($m, 323) -ne [IntPtr]::Zero
  LinkKind
  $b56b = [U]::GetDlgItem($m, 323) -ne [IntPtr]::Zero
  Click 310; $warn56 = CloseBox 1
  $still56 = [U]::GetDlgItem($m, 301) -ne [IntPtr]::Zero
  Click 311; [void](CloseBox 6)                              # cancel: discard the draft
  Check IJ56 "False|True|True|True" "$b56a|$b56b|$warn56|$still56" "cursor kind: no [Link]; linked kind: [Link]; saving the linked kind without a link: a warning, still editing"

  # ---- R75-1: incomplete links on the two-input item (slot 5), injected in memory
  function Incomplete($mode) { [void][U]::SendMessageW($m, 0x8016, [IntPtr](5 * 16 + $mode), [IntPtr]9) }
  function TextD() { [U]::Text([Fake]::D1) + "/" + [U]::Text([Fake]::D2) }
  function ChipThenConfirm() { $c = [U]::FindCls([uint32]$p.Id, "OneKeyChip") -ne [IntPtr]::Zero; Target ([Fake]::D) 0; [void][U]::PostMessageW($m, 0x0312, [IntPtr]0xB000, [IntPtr]::Zero); Start-Sleep -Milliseconds 900; $c }
  $g50 = @(); $g51 = @(); $g52 = @()
  foreach ($mode in 1, 2, 3) {
    Incomplete $mode
    ClearAll; Prefill; Target ([Fake]::D) 0; $k = Keys; Hotkey 5; Start-Sleep -Milliseconds 1500
    $g50 += "m${mode}:" + (TextD) + "/" + [U]::Text([Fake]::A1) + " " + (KeyDelta $k)
    Show1Key; Prefill; $k = Keys; Click 1105; Start-Sleep -Milliseconds 300   # the list's [Type] of slot 5
    $c51 = ChipThenConfirm
    $g51 += "m${mode}:chip=$c51 " + (TextD) + " " + (KeyDelta $k)
    Prefill; $k = Keys; [void][U]::SendMessageW($m, 0x8016, [IntPtr]5, [IntPtr]11); Start-Sleep -Milliseconds 300   # the tray menu's item for slot 5
    $c52 = ChipThenConfirm
    $g52 += "m${mode}:chip=$c52 " + (TextD) + " " + (KeyDelta $k)
  }
  Incomplete 0
  $want5 = "m1:{0}|m2:{0}|m3:{0}"
  Check IJ50 ($want5 -f "old-id/old-pw/ $NoKeys") ($g50 -join '|') "R75-1 shortcut: input 1 only / input 2 only / two pages: nothing typed into D or A [notice: $(ToastText)]"
  Check IJ51 ($want5 -f "chip=False old-id/old-pw $NoKeys") ($g51 -join '|') "R75-1 list [Type]: no chip, nothing typed (also not by the confirm key)"
  Check IJ52 ($want5 -f "chip=False old-id/old-pw $NoKeys") ($g52 -join '|') "R75-1 tray item: no chip, nothing typed (also not by the confirm key)"
  ClearAll; Prefill; Target ([Fake]::D) 0; Hotkey 5; Start-Sleep -Milliseconds 1500
  Check IJ53 "$MiId/$MiPw" (TextD) "control: links removed -> ID, Tab, password into D as before"

  # ---- program link (slot 7): link input 1 to D1, then fill from another window
  Click 203; Click 4007; SetText 301 "AppLogin"; SetText 302 "app-id"; Click 339; SetText 331 "app-pw"
  [void][U]::PostMessageW([U]::GetDlgItem($m, 304), 0x0100, [IntPtr]0x08, [IntPtr]::Zero); Start-Sleep -Milliseconds 200
  LinkKind
  Click 323; Start-Sleep -Milliseconds 500                  # input 1 [Link] -> chip
  $lc = [U]::FindCls([uint32]$p.Id, "OneKeyChip")
  Target ([Fake]::D) 0                                       # the user clicks the program's ID field
  if ($lc -ne [IntPtr]::Zero) { [void][U]::PostMessageW($lc, 0x0111, [IntPtr]11, [IntPtr]::Zero) }
  Start-Sleep -Milliseconds 1200
  $row39 = [U]::Text([U]::GetDlgItem($m, 322))
  $b39 = [U]::GetDlgItem($m, 345); $btn39 = "$([U]::IsWindowVisible($b39))/$([U]::IsWindowEnabled($b39))"
  Check IJ39 "True|True|False/False" ("$($row39 -like '*powershell.exe*')|$($row39 -like '*Edit*')|$btn39") "program link shown in the edit screen; a two-input item has one [Link] (input 1) since 0.2.113, input 2's row has no button (visible/enabled) ($($row39 -replace "`r?`n", ' / '))"
  Click 310; [void](CloseBox)
  ClearAll; Prefill; Target ([Fake]::A) 0; Hotkey 7
  $sw40 = [Diagnostics.Stopwatch]::StartNew(); while ($sw40.ElapsedMilliseconds -lt 5000 -and ([U]::Text([Fake]::D2)) -ne "app-pw") { Start-Sleep -Milliseconds 100 }
  Check IJ40 "app-id|app-pw|" ([U]::Text([Fake]::D1) + "|" + [U]::Text([Fake]::D2) + "|" + [U]::Text([Fake]::A1)) "shortcut from window A: program window D brought to the front, ID field filled (old value replaced), Tab, password"
  ClearAll; Prefill; [void][U]::ShowWindow([Fake]::D, 0); Target ([Fake]::A) 0; Hotkey 7; Start-Sleep -Milliseconds 2000
  $t41 = [U]::Text([Fake]::A1) + "|" + [U]::Text([Fake]::D1) + "|" + [U]::Text([Fake]::D2)
  [void][U]::ShowWindow([Fake]::D, 5)
  Check IJ41 "|old-id|old-pw" $t41 "program window hidden: nothing typed (not into A in front, not into D)"

  # IJ57: the counters really see what they must not miss (posted on purpose)
  $k = Keys
  [void][U]::PostMessageW([Fake]::D1, 0x0100, [IntPtr]0x71, [IntPtr]::Zero); [void][U]::PostMessageW([Fake]::D1, 0x0102, [IntPtr]0x78, [IntPtr]::Zero)
  [void][U]::PostMessageW([Fake]::D1, 0x00B1, [IntPtr]0, [IntPtr]-1); [void][U]::PostMessageW([Fake]::A1, 0x0100, [IntPtr]0x71, [IntPtr]::Zero)
  [void][U]::PostMessageW([Fake]::E1, 0x0201, [IntPtr]1, [IntPtr]::Zero); [void][U]::PostMessageW([Fake]::E1, 0x0202, [IntPtr]0, [IntPtr]::Zero)
  Start-Sleep -Milliseconds 400
  Check IJ57 "dA=1 dD=3 dE=0 cE=1" (KeyDelta $k) "judge sensitivity: a key + a character + a select-all to D, a key to A, a press on E are all counted"
  ClearAll; Prefill

  # IJ58-60: the "Use" question on the program-linked item (slot 7)
  function UseCursor() { [void][U]::PostMessageW([U]::GetDlgItem($m, 348), 0x0100, [IntPtr]0x26, [IntPtr]::Zero) }   # Up = "type at the cursor"
  function WaitBox() { for ($i = 0; $i -lt 20; $i++) { if ([U]::FindCls([uint32]$p.Id, "OneKeyDialog") -ne [IntPtr]::Zero) { return $true }; Start-Sleep -Milliseconds 100 }; $false }
  function Linked7() { Show1Key; Click 1007; $t = [U]::Text([U]::GetDlgItem($m, 322)); Click 311; [void](CloseBox 6); $t -like '*powershell.exe*' }
  Show1Key; Click 1007; UseCursor; $q58 = WaitBox
  [void][U]::PostMessageW($m, 0x02B1, [IntPtr]7, [IntPtr]::Zero); Start-Sleep -Milliseconds 800   # lock while the question is open
  $gone58 = [U]::FindCls([uint32]$p.Id, "OneKeyDialog") -eq [IntPtr]::Zero
  SetText 101 "Master1234"; Click 103; Start-Sleep -Milliseconds 800
  Check IJ58 "True|True|True" "$q58|$gone58|$(Linked7)" "lock while 'remove the links?' is open: the question closes, after unlocking the item is still linked"
  Show1Key; Click 1007; UseCursor; $q59 = WaitBox; [void](CloseBox 7); Start-Sleep -Milliseconds 500
  $row59 = [U]::Text([U]::GetDlgItem($m, 322)) -like '*powershell.exe*'
  Click 311; [void](CloseBox 6)
  Check IJ59 "True|True|True" "$q59|$row59|$(Linked7)" "answer No: the item stays 'fill a site or program' with its link"
  Show1Key; Click 1007; UseCursor; $q60 = WaitBox; [void](CloseBox 6); Start-Sleep -Milliseconds 700
  $row60 = [U]::GetDlgItem($m, 322) -eq [IntPtr]::Zero
  Click 311; [void](CloseBox 6)                               # leave without saving
  Check IJ60 "True|True|True" "$q60|$row60|$(Linked7)" "answer Yes, then leave without saving: the link rows go away in the draft, the saved item is still linked"

  # ---- program identity (slot 8, 0.2.83, Codex R82): fake window E with host panels
  function EMsg($msg) { [void][U]::SendMessageW([Fake]::E, $msg, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 100 }
  function PrefillE() { [void][U]::SendMessageW([Fake]::E1, 0x000C, [IntPtr]::Zero, "old-id"); [void][U]::SendMessageW([Fake]::E2, 0x000C, [IntPtr]::Zero, "old-pw") }
  function TextE() { [U]::Text([Fake]::E1) + "|" + [U]::Text([Fake]::E2) + "|" + [U]::Text([Fake]::A1) }
  function WaitE2($want, $ms) { $sw = [Diagnostics.Stopwatch]::StartNew(); while ($sw.ElapsedMilliseconds -lt $ms -and ([U]::Text([Fake]::E2)) -ne $want) { Start-Sleep -Milliseconds 100 } }
  function AppTest($setup) { ClearAll; EMsg 0x8015; foreach ($x in $setup) { EMsg $x }; PrefillE; Target ([Fake]::A) 0; Hotkey 8 }
  Click 203; Click 4007; SetText 301 "AppHost"; SetText 302 "app-id"; Click 339; SetText 331 "app-pw"
  [void][U]::PostMessageW([U]::GetDlgItem($m, 304), 0x0100, [IntPtr]0x08, [IntPtr]::Zero); Start-Sleep -Milliseconds 200
  LinkKind
  Click 323; Start-Sleep -Milliseconds 500
  $lc = [U]::FindCls([uint32]$p.Id, "OneKeyChip")
  if (-not [U]::Front([Fake]::E)) { throw "could not bring the fake window E to the front" }
  EMsg 0x8015; EMsg 0x8010; [void][U]::SendMessageW([Fake]::H1, 0x0201, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 150   # the user clicks the ID place
  if ($lc -ne [IntPtr]::Zero) { [void][U]::PostMessageW($lc, 0x0111, [IntPtr]11, [IntPtr]::Zero) }
  Start-Sleep -Milliseconds 1200
  $row42 = [U]::Text([U]::GetDlgItem($m, 322))
  Check IJ42 "True" "$($row42 -like '*:\*powershell.exe*')" "program link keeps the full path ($($row42 -replace "`r?`n", ' / '))"
  [void][U]::SendMessageW([U]::GetDlgItem($m, 349), 0x00F1, [IntPtr]1, [IntPtr]::Zero)   # Advanced: click the linked place allowed (new items start with it off since 0.2.113)
  Click 310; [void](CloseBox)

  AppTest @(0x8010); WaitE2 "app-pw" 5000
  Check IJ43 "app-id|app-pw||1" ((TextE) + "|" + [Fake]::HostClicks) "logged out: one click on the linked place brings the ID input back, ID, Tab, password [notice: $(ToastText)]"
  $k = Keys; AppTest @(0x8010, 0x8011); Start-Sleep -Milliseconds 2500
  Check IJ44 "old-id|old-pw||0|0|$NoKeys" ((TextE) + "|" + [Fake]::ButtonClicks + "|" + [Fake]::HostClicks + "|" + (KeyDelta $k)) "R82-1: a button covers the linked place in the same host: not clicked, nothing typed [notice: $(ToastText)]"
  $k = Keys; AppTest @(0x8012); Start-Sleep -Milliseconds 2500
  Check IJ45 "old-x|old-id|old-pw||0|$NoKeys" ([U]::Text([Fake]::E0) + "|" + (TextE) + "|" + [Fake]::HostClicks + "|" + (KeyDelta $k)) "R82-2: a same-class field added in front of the linked one: nothing typed into any field [notice: $(ToastText)]"
  AppTest @(); WaitE2 "app-pw" 5000
  Check IJ46 "app-id|app-pw||0" ((TextE) + "|" + [Fake]::HostClicks) "control: both inputs on screen in the original order: ID, Tab, password, no place click [notice: $(ToastText)]"
  $k = Keys; AppTest @(0x8013); Start-Sleep -Milliseconds 2500
  Check IJ47 "old-id|old-pw||0|$NoKeys" ((TextE) + "|" + [Fake]::HostClicks + "|" + (KeyDelta $k)) "R82-2: the same-class fields changed order: nothing typed [notice: $(ToastText)]"
  # IJ61: the item's "click the field" switch (349) off -> no place click, nothing typed; then back on
  # (slot 8 is below the visible list rows, so its edit screen is opened by posting the row command, not by clicking the row)
  function AllowClick8($on) { Show1Key; [void][U]::PostMessageW($m, 0x0111, [IntPtr]1008, [IntPtr]::Zero); Start-Sleep -Milliseconds 700; [void][U]::SendMessageW([U]::GetDlgItem($m, 349), 0x00F1, [IntPtr]$(if ($on) { 1 } else { 0 }), [IntPtr]::Zero); Start-Sleep -Milliseconds 200; Click 310; [void](CloseBox) }
  AllowClick8 $false
  $k = Keys; AppTest @(0x8010); Start-Sleep -Milliseconds 2500
  Check IJ61 "old-id|old-pw||0|$NoKeys" ((TextE) + "|" + [Fake]::HostClicks + "|" + (KeyDelta $k)) "no-click item, logged out: no click on the place, nothing typed [notice: $(ToastText)]"
  AppTest @(); WaitE2 "app-pw" 5000
  Check IJ62 "app-id|app-pw||0" ((TextE) + "|" + [Fake]::HostClicks) "no-click item, fields on screen: SetFocus is enough, typed as usual (control)"
  AllowClick8 $true

  # R75-3 program place click: change things at the barrier right after the modifier wait, before the place check
  ClearAll; EMsg 0x8015; EMsg 0x8010; PrefillE; Target ([Fake]::A) 0; $k = Keys; $h = Arm "appplace.mods"; Hotkey 8
  $ok54 = WaitReached $h; EMsg 0x8011; $made54 = [Fake]::EBtn -ne [IntPtr]::Zero; Release $h; Start-Sleep -Milliseconds 2000
  Check IJ54 "True|True|old-id|old-pw||0|0|$NoKeys" ("$ok54|$made54|" + (TextE) + "|" + [Fake]::ButtonClicks + "|" + [Fake]::HostClicks + "|" + (KeyDelta $k)) "a button covers the place at the barrier: not clicked, nothing typed [notice: $(ToastText)]"
  ClearAll; EMsg 0x8015; EMsg 0x8010; PrefillE; Target ([Fake]::A) 0; $k = Keys; $h = Arm "appplace.mods"; Hotkey 8
  $ok55 = WaitReached $h; [void][U]::PostMessageW($m, 0x02B1, [IntPtr]7, [IntPtr]::Zero); Start-Sleep -Milliseconds 400
  $made55 = [U]::FindCls([uint32]$p.Id, "OneKeyMainWindow$suffix") -ne [IntPtr]::Zero -and [U]::GetDlgItem($m, 1000) -eq [IntPtr]::Zero   # the lock screen replaced the list
  Release $h; Start-Sleep -Milliseconds 1500
  Check IJ55 "True|True|old-id|old-pw||0|$NoKeys" ("$ok55|$made55|" + (TextE) + "|" + [Fake]::HostClicks + "|" + (KeyDelta $k)) "1Key locked at the barrier: no click, nothing typed"
  SetText 101 "Master1234"; Click 103; Start-Sleep -Milliseconds 800   # unlock (the links come back from the file)
  ClearAll; EMsg 0x8015; PrefillE; $hook48 = [U]::SendMessageW($m, 0x8016, [IntPtr](8 * 16 + 1), [IntPtr]8)
  Target ([Fake]::A) 0; $k = Keys; Hotkey 8; Start-Sleep -Milliseconds 2000; $toast48 = ToastText
  Check IJ48 "True|old-id|old-pw||True|$NoKeys" ("$($hook48 -ne [IntPtr]::Zero)|" + (TextE) + "|" + ($toast48 -like '*1Key-test-other*') + "|" + (KeyDelta $k)) "same file name and window class in another folder: nothing typed, the notice shows both paths [notice: $toast48]"
  ClearAll; EMsg 0x8015; PrefillE; $hook49 = [U]::SendMessageW($m, 0x8016, [IntPtr](8 * 16 + 2), [IntPtr]8)
  Target ([Fake]::A) 0; $k = Keys; Hotkey 8; Start-Sleep -Milliseconds 2000; $toast49 = ToastText
  Check IJ49 "True|old-id|old-pw||True|$NoKeys" ("$($hook49 -ne [IntPtr]::Zero)|" + (TextE) + "|" + ($toast49 -ne '-' -and $toast49 -notlike '*1Key-test-other*') + "|" + (KeyDelta $k)) "older link (file name only): nothing typed, link-again notice [notice: $toast49]"

  # lock while a chip waits (the session-lock message), then the confirm key types nothing
  ClearAll; $chip = StartChip 0
  [void][U]::PostMessageW($m, 0x02B1, [IntPtr]7, [IntPtr]::Zero); Start-Sleep -Milliseconds 500
  Target ([Fake]::A) 0; [void][U]::PostMessageW($m, 0x0312, [IntPtr]0xB000, [IntPtr]::Zero); Settle
  Check IJ25 $true ([U]::FindCls([uint32]$p.Id, "OneKeyChip") -eq [IntPtr]::Zero) "lock removes the chip"
  Check IJ26 "" ([U]::Text([Fake]::A1)) "after the lock the confirm key types nothing"
  Hotkey 0; Settle
  Check IJ27 "" ([U]::Text([Fake]::A1)) "locked: the slot hotkey types nothing either"
} catch { if ("$_" -eq "repeat-done") { Clear-ExpectedError } else { Add-Failure ("exception: " + $_) } }
finally {
  foreach ($v in "ONEKEY_TEST_HOLD_EVENT", "ONEKEY_TEST_WORKAREA") { Remove-Item "Env:$v" -ErrorAction Ignore }
  Stop-TestInstances $suffix
  try { [Fake]::Stop() } catch { Add-Failure ("fake windows: " + $_) }
  if ($userFront -ne [IntPtr]::Zero -and [U]::IsWindow($userFront)) { [void][U]::Front($userFront) }
}
Complete-Checks
