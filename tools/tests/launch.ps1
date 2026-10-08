# Program/folder launching, stage 1 (0.3.3; design v2 + Codex 16:35 L2-1..L2-5). The launch list is written straight into the test's
# own config folder (launch.dat, DPAPI with the app's entropy) - the file pickers are checked by hand. Targets are made by this script:
# a small window program (optionally slow to show its window), a copy with a requireAdministrator manifest, a "run as administrator"
# shortcut, and a test folder. 1Key runs with normal rights here (the administrator -> Explorer path needs UAC: by hand / VM).
# The hotkey path is driven by posting WM_HOTKEY (the real registration is proved by the harness failing to register the same combo).
# LA01 a valid list is read while 1Key is LOCKED: both item hotkeys are registered (the harness cannot register them) and nothing ran
# LA02 hotkey while locked: the program starts once, 1Key stays locked (old lock screen field still there, no list)
# LA03 again after its window is up: no second process, its window comes to the front
# LA04 a target that shows its window after 2 s: requests at 0 / 0.6 / 1.0 s start ONE process (in-flight state), then focus
# LA05 the target file is gone: nothing starts, a warning toast
# LA06 folder item: one Explorer window on that folder; a second press reuses it (still one window on that folder)
# LA07 unknown format (launchv=2): no hotkey registered, a hotkey message runs nothing, the file is byte-for-byte unchanged, list note shown
# LA08 known format with a damaged item, an unknown field and a stray line: the good item is edited and saved through the screen;
#      the damaged item, the unknown field and the stray line are still in the file
# LA09 saving fails (test fault launch:save): an error box, the file is unchanged, the edit screen keeps the typed name
# LA10 (0.3.7) two items with the same id = structural damage: the whole list is read-only (no hotkey, nothing runs, file unchanged,
#      note + [Start a new list] shown) - Codex R34-2
# LA11 a program whose manifest requires administrator rights: refused at run time (nothing starts, warning toast)
# LA12 a shortcut marked "run as administrator": refused (nothing starts); a normal shortcut to the program starts it once
# LA13 icon strip: tiles in list order, Right arrow moves focus to the next tile, Enter on a tile starts its program
# LA14 (0.3.5/0.3.7) [+ Add] -> [Program] opens the installed-program list, read in the background from an isolated test Start menu:
#      same-named shortcuts in different subfolders both listed, the uninstall shortcut hidden; a search with no match shows no rows
# LA15 (0.3.5/0.3.6) two rows: the folder tile sits below the program tiles; by default folder tiles show the name, program tiles are icon only;
#      (0.3.18) both rows use the same column - same left edge and width - so the icons line up (2026-10-05 user)
# ---- 0.3.18 (2026-10-05 user, after installing 0.3.16)
# LA31 icons slower than the 3 s limit (icon:slow, 5 s each) still appear: none at first, both real icons later (before: default for good)
# LA32 strip box: same column in both rows, a round [Edit] badge over the box's top-right corner, a long program row is cut at the box
#      edge and scrolls on its own with the wheel (the folder row does not move), the last tile becomes fully shown;
#      (0.3.22) the row's chevrons: at the start only > shows, pressing it moves the row a screen and shows <, at the end > hides;
#      (0.3.23) the chevrons sit in their own room at both ends (they never cover a tile)
# LA33 [Edit], then drag the first tile past the third: saved order B C A, a drag is not a click (no edit screen), the hotkey stays
# LA34 a Microsoft Store app (Calculator, if installed and not running): the hotkey starts it, a second press brings the same window to
#      the front (still one window), its icon is read, and the program picker lists it (ONEKEY_TEST_STOREAPPS=1). Skipped otherwise
# LA35 dropped items (test message WM_APP+44 - the OLE / WM_DROPFILES plumbing is checked by hand): ignored while locked; a folder and
#      a program open the edit screen and save as folder / exe; two at once use the first and say so; an item without a path asks to
#      use the list
# LA36 a launcher-type program (LaSpawn starts LaChild from its own folder and exits - like an auto-updater that starts the real,
#      administrator program after UAC): LaChild's window shows up behind; 1Key brings it to the front; a second press finds that
#      window (no second LaSpawn run, still one LaChild). 1Key runs with normal rights here - the UAC step itself is checked by hand
# LA37 a program whose main window is OWNED by a visible 0x0 window (how Delphi programs such as the in-house banking Terminal01 make
#      their main form, 0.3.19): started once, a second press finds that window - no second process, the window in front
# ---- 0.3.29 (Codex C28-1..4)
# LA34b store app guards: an app id that is not installed opens nothing (not found); with the package manifest unreadable
#      (launch:appmanifest) Calculator is refused (nothing starts, reason "app-verify")
# LA36b a program in the SAME folder that the user starts after a launch is not the launch's window: after the launched program
#      is closed, the next press starts it again (one new launch call) instead of focusing the other program (C28-1: process
#      lineage, not the folder)
# LA36c the user moved to another window before the launcher's child appeared: the child is NOT pulled to the front (only flashed),
#      the user's window stays in front (C28-2)
# LA38 a shortcut with arguments (spaces, Korean, an embedded quote) and a working folder: the started program receives exactly
#      those arguments and runs in that folder (the 0.3.18-0.3.26 defect dropped both)
# ---- 0.3.31 (Codex reply to 0.3.29)
# (LA34b) the app id lookup fails (launch:appquery): cannot verify, nothing starts
# LA39 tiles cut by the strip box, as screen readers read them (MSAA): a fully hidden tile is OFFSCREEN; a whole tile is not and its
#      location is its window; a partly shown tile is not OFFSCREEN and its location is only the shown part; keyboard focus on a
#      hidden tile (accSelect TAKEFOCUS, what a screen reader does) scrolls it into view, OFFSCREEN clears and a state-change event
#      is sent for that tile
# (LA14, 0.3.21) a test taskbar folder (ONEKEY_TEST_TASKBAR) with one pinned shortcut: it is the FIRST row (taskbar group above all apps);
#      "choose a file" is its own row (4201) right below the search field, above the taskbar group
# (LA14, 0.3.19) the search field is the same window after typing - only the rows below it are rebuilt, so a Korean IME composition in
#      the field is not cut (2026-10-05 user: Korean search did not work)
# ---- 0.3.7 (Codex 17:45 R34, 18:27 R36)
# LA16 structural damage (duplicate target/mods/iconidx keys, bad mods/vk/iconidx, icon without iconidx, launchv=-1 then 1, missing name,
#      31 items): every case read-only - no hotkey, nothing runs, file byte-for-byte unchanged
# LA17 [Start a new list]: a failing move (test fault) changes nothing; [Yes] keeps the old file as launch.dat.bad, the list is empty and usable
# LA18 icon/iconidx: kept by a new-version save, kept when the previous version (0.3.4) renames and saves, still valid when read again;
#      a chosen icon file that was deleted leaves the item valid
# LA19 the check takes longer than the time limit (launch:slowlookup): the window stays responsive, nothing runs; deleting the item
#      while it is being checked also runs nothing and shows no result
# LA20 the launch call itself is slow (launch:slowrun): a second press meanwhile is refused (busy), exactly one process at the end
# LA21 rights unknown (launch:elevation) / compatibility unreadable (launch:compat) / folder windows unknown (launch:folderquery): nothing opens
# LA22 a target that shows its window after 10 s: a press after 8 s asks (confirm box) and [No] starts nothing; once the window is up a press
#      brings it to the front without asking
# LA23 a target without any window: a press after 8 s asks; [Yes] starts a second process (only on confirmation)
# LA24 window rules (R34-3): a large WS_EX_TRANSPARENT window and a WS_EX_NOACTIVATE window of the target are reused; a zero-size window of
#      an unqueryable program does not block; an unqueryable normal window blocks (cannot verify) unless the target's own window is found
# LA25 Settings > Quick launch: the four name-switch combinations change the tile shapes after [Save] and survive a restart
# LA26 installed-program list: more shortcuts than the scan limit -> 80 rows, scan stops (no freeze)
# LA27 icons read in the background (icon:slow): the window answers within 1 s while icons are slow
# ---- 0.3.12 (Codex 20:13 RW-1..3)
# LA28 the launch finished but its result message is late (launch:latedone, a 3 s barrier): a second press in that gap takes the
#      finished result first (no second process, one launch call); once the window is up a press brings it to the front
# LA29 folder windows: a failing step after reaching an Explorer window (launch:folderstep) opens nothing; control: with only a
#      virtual-folder window open (This PC) the folder opens normally
# LA30 [Start a new list] is decided by the error kind, not by the shown text: an unknown-format file offers no reset button, also
#      after the screen language was changed and saved
# Own config folder and instance suffix, test targets in the scratch folder; real Run/task registrations are never made. ASCII only.
# Default run = core LA01-LA15 (about 1.5 min). -Edge adds the one-off checks LA16-LA30 (damaged files, slow/failed COM, late result,
# window policy, old-version file compatibility, name switches, scan limits) - run them only when that launcher code changes.
param([string]$Exe = "", [string]$OldExe = "", [switch]$Edge)
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
$core = @("LA01","LA02","LA03","LA04","LA05","LA06","LA07","LA08","LA09","LA10","LA11","LA12","LA13","LA14","LA15","LA31","LA32","LA33","LA34","LA35","LA36","LA37","LA32b","LA34b","LA36b","LA36c","LA38","LA39")
$edgeIds = @("LA16","LA17","LA18","LA19","LA20","LA21","LA22","LA23","LA24","LA25","LA26","LA27","LA28","LA29","LA30")
Start-Checks -Required $(if ($Edge) { $core + $edgeIds } else { $core })
$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$suffix = ".la"
$work = Join-Path $sp "launch_work"; if (Test-Path $work) { Remove-Item $work -Recurse -Force -ErrorAction Ignore }; New-Item -ItemType Directory -Force $work | Out-Null
$cfg = Join-Path $sp "launch_cfg"
$master = "La-Dummy-6170"
Add-Type -AssemblyName System.Security, System.Windows.Forms
Add-Type @'
using System;using System.Runtime.InteropServices;using System.Text;using System.Collections.Generic;
public class LA {
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
  [DllImport("user32.dll")] public static extern IntPtr GetFocus();
  [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeoutW(IntPtr h, uint m, IntPtr w, IntPtr l, uint flags, uint ms, out IntPtr res);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr GetPropW(IntPtr h, string name);
  [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  public static void Press(IntPtr m, int id) { IntPtr c = GetDlgItem(m, id); if (c == IntPtr.Zero) return; RECT rc; GetClientRect(c, out rc); IntPtr lp = (IntPtr)((rc.R/2) | ((rc.B/2) << 16)); PostMessageW(c, 0x0201, (IntPtr)1, lp); System.Threading.Thread.Sleep(40); PostMessageW(c, 0x0202, IntPtr.Zero, lp); }
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, CB cb, IntPtr l);
  public static string ToastText(uint pid) { var r = new List<string>(); EnumWindows((h,l) => { uint q; GetWindowThreadProcessId(h, out q); if (q == pid && Cls(h) == "OneKeyToast" && IsWindowVisible(h)) EnumChildWindows(h, (c,x) => { r.Add(Title(c)); return true; }, IntPtr.Zero); return true; }, IntPtr.Zero); return string.Join(" / ", r); }
  public static bool Answers(IntPtr h, uint ms) { IntPtr r; return SendMessageTimeoutW(h, 0, IntPtr.Zero, IntPtr.Zero, 2, ms, out r) != IntPtr.Zero; }
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
  [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
  [DllImport("gdi32.dll")] public static extern IntPtr CreateRectRgn(int l, int t, int r, int b);
  [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr h);
  [DllImport("user32.dll")] public static extern int GetWindowRgn(IntPtr h, IntPtr rgn);
  // 0 = no region (whole window shown), 1 = empty, 2/3 = cut
  public static int RgnKind(IntPtr h) { IntPtr r = CreateRectRgn(0, 0, 0, 0); int k = GetWindowRgn(h, r); DeleteObject(r); return k; }
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  public static bool Front(IntPtr h) { keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero); BringWindowToTop(h); return SetForegroundWindow(h); }
  public static int CountTitle(string t) { int n = 0; EnumWindows((h,l) => { if (IsWindowVisible(h) && Title(h) == t) n++; return true; }, IntPtr.Zero); return n; }
  [StructLayout(LayoutKind.Sequential)] public struct GTI { public int cbSize, flags; public IntPtr active, focus, capture, menuOwner, moveSize, caret; public int l, t, r, b; }
  [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint tid, ref GTI g);
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static string Title(IntPtr h) { var t = new StringBuilder(512); GetWindowTextW(h, t, 512); return t.ToString(); }
  public static IntPtr MainWnd(uint pid, string cls) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
  public static int Count(uint pid, string cls) { int n = 0; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls && IsWindowVisible(h)) n++; return true; }, IntPtr.Zero); return n; }
  public static List<IntPtr> Explorer(string title) { var r = new List<IntPtr>(); EnumWindows((h,l) => { if (Cls(h) == "CabinetWClass" && IsWindowVisible(h) && Title(h).Contains(title)) r.Add(h); return true; }, IntPtr.Zero); return r; }
  public static IntPtr Focus(IntPtr w) { uint p; uint tid = GetWindowThreadProcessId(w, out p); var g = new GTI(); g.cbSize = Marshal.SizeOf(typeof(GTI)); return GetGUIThreadInfo(tid, ref g) ? g.focus : IntPtr.Zero; }
  // true if somebody else (1Key) already holds the combo
  public static bool Taken(uint mods, uint vk) { bool ok = RegisterHotKey(IntPtr.Zero, 0x7F01, mods, vk); if (ok) UnregisterHotKey(IntPtr.Zero, 0x7F01); return !ok; }
}
'@
# MSAA of the tiles (LA39), read as screen readers read them
Add-Type -ReferencedAssemblies Accessibility @'
using System;using System.Runtime.InteropServices;using Accessibility;
public class LAX {
  [DllImport("oleacc.dll")] static extern int AccessibleObjectFromWindow(IntPtr h, uint id, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object o);
  public static IAccessible Acc(IntPtr h) { Guid g = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71"); object o; return AccessibleObjectFromWindow(h, 0xFFFFFFFC, ref g, out o) >= 0 ? o as IAccessible : null; }
  public static int State(IntPtr h) { var a = Acc(h); try { return a == null ? -1 : Convert.ToInt32(a.get_accState(0)); } catch { return -1; } }
  public static string Loc(IntPtr h) { var a = Acc(h); if (a == null) return "none"; int x, y, w, hh; try { a.accLocation(out x, out y, out w, out hh, 0); } catch { return "err"; } return x + "," + y + "," + w + "," + hh; }
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] static extern int GetWindowRgnBox(IntPtr h, out RECT r);
  // the shown part of a window cut by its region (screen x,y,w,h)
  public static string Shown(IntPtr h) { RECT w; GetWindowRect(h, out w); RECT b; int k = GetWindowRgnBox(h, out b); if (k == 0) return w.L + "," + w.T + "," + (w.R - w.L) + "," + (w.B - w.T); int l = Math.Max(w.L, w.L + b.L), t = Math.Max(w.T, w.T + b.T), r = Math.Min(w.R, w.L + b.R), bt = Math.Min(w.B, w.T + b.B); return l + "," + t + "," + (r - l) + "," + (bt - t); }
  public static int Width(IntPtr h) { RECT w; GetWindowRect(h, out w); return w.R - w.L; }
  delegate void WinEventProc(IntPtr hook, uint e, IntPtr h, int obj, int child, uint tid, uint time);
  static WinEventProc proc = OnEv; static IntPtr watch; static int count;
  static void OnEv(IntPtr hook, uint e, IntPtr h, int obj, int child, uint tid, uint time) { if (h == watch && obj == -4 && e == 0x800A) count++; }
  [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc p, uint pid, uint tid, uint flags);
  [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr h; public uint m; public IntPtr w, l; public uint t; public int x, y; }
  [DllImport("user32.dll")] static extern bool PeekMessageW(out MSG m, IntPtr h, uint a, uint b, uint r);
  [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG m);
  [DllImport("user32.dll")] static extern IntPtr DispatchMessageW(ref MSG m);
  [DllImport("user32.dll")] static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  static void Pump(int ms) { var end = DateTime.Now.AddMilliseconds(ms); MSG m; while (DateTime.Now < end) { while (PeekMessageW(out m, IntPtr.Zero, 0, 0, 1)) { TranslateMessage(ref m); DispatchMessageW(ref m); } System.Threading.Thread.Sleep(10); } }
  // keyboard focus through MSAA (accSelect TAKEFOCUS): the number of state-change events that tile sent meanwhile (out of context hook)
  public static int FocusWatch(uint pid, IntPtr tile, IntPtr main) {
    watch = tile; count = 0;
    IntPtr hk = SetWinEventHook(0x800A, 0x800A, IntPtr.Zero, proc, pid, 0, 0);
    try { var a = Acc(tile); if (a == null) return -1; a.accSelect(1, 0); PostMessageW(main, 0, IntPtr.Zero, IntPtr.Zero); Pump(1000); }
    catch { return -2; }
    finally { UnhookWinEvent(hk); }
    return count;
  }
}
'@

# ---- test targets
$targetSrc = @'
using System; using System.Windows.Forms; using System.Threading; using System.Drawing; using System.Diagnostics;
public class F : Form { public int Ex; protected override CreateParams CreateParams { get { var c = base.CreateParams; c.ExStyle |= Ex; return c; } } }
public static class P { [STAThread] public static void Main(string[] a) {
  if (System.IO.Path.GetFileNameWithoutExtension(Application.ExecutablePath) == "LaArgs") {   // records what it was started with, then quits
    var outp = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.ExecutablePath), "args-out.txt");
    var lines = new System.Collections.Generic.List<string>(); lines.Add(Environment.CurrentDirectory); lines.AddRange(a);
    System.IO.File.WriteAllLines(outp, lines.ToArray(), new System.Text.UTF8Encoding(false)); return; }
  if (System.IO.Path.GetFileNameWithoutExtension(Application.ExecutablePath) == "LaSpawn") {   // launcher: start the sibling LaChild (shows after 2.5 s) and quit
    var psi = new ProcessStartInfo(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.ExecutablePath), "LaChild.exe")) { UseShellExecute = false };
    psi.EnvironmentVariables["ONEKEY_LAUNCH_TARGET_DELAY"] = "2500"; psi.EnvironmentVariables["ONEKEY_LAUNCH_TARGET_MODE"] = "";
    Process.Start(psi); return; }
  int d; if (int.TryParse(Environment.GetEnvironmentVariable("ONEKEY_LAUNCH_TARGET_DELAY") ?? "", out d) && d > 0) Thread.Sleep(d);
  string mode = Environment.GetEnvironmentVariable("ONEKEY_LAUNCH_TARGET_MODE") ?? "";
  if (mode == "nowindow") { Thread.Sleep(120000); return; }
  var f = new F(); f.Text = "1Key launch target " + System.IO.Path.GetFileNameWithoutExtension(Application.ExecutablePath); f.Width = 320; f.Height = 160;
  if (mode == "transparent") { f.Ex = 0x20 | 0x80000; f.Opacity = 0.95; }
  if (mode == "noactivate") f.Ex = 0x08000000;
  if (mode == "owned") {   // Delphi style: a visible 0x0 owner window, the real window owned by it
    var o = new Form(); o.FormBorderStyle = FormBorderStyle.None; o.ShowInTaskbar = true; o.StartPosition = FormStartPosition.Manual; o.Location = new Point(-32000, -32000);
    o.MinimumSize = new Size(0, 0); o.Load += (s, e) => o.Size = new Size(0, 0); o.Show(); f.ShowInTaskbar = false; f.Owner = o; }
  if (mode == "zero") { f.FormBorderStyle = FormBorderStyle.None; f.MinimumSize = new Size(0, 0); f.Load += (s, e) => f.Size = new Size(0, 0); }
  Application.Run(f); } }
'@
$tgt = Join-Path $work "LaTarget.exe"
Add-Type -TypeDefinition $targetSrc -OutputAssembly $tgt -OutputType WindowsApplication -ReferencedAssemblies System.Windows.Forms, System.Drawing
$slow = Join-Path $work "LaSlow.exe"; Copy-Item $tgt $slow
$gone = Join-Path $work "LaGone.exe"; Copy-Item $tgt $gone
$late = Join-Path $work "LaLate.exe"; Copy-Item $tgt $late
$nowin = Join-Path $work "LaNoWin.exe"; Copy-Item $tgt $nowin
$blind = Join-Path $work "LaBlind.exe"; Copy-Item $tgt $blind
$blindZero = Join-Path $work "LaBlindZero.exe"; Copy-Item $tgt $blindZero
$spawn = Join-Path $work "LaSpawn.exe"; Copy-Item $tgt $spawn
$child = Join-Path $work "LaChild.exe"; Copy-Item $tgt $child
$otherExe = Join-Path $work "LaOther.exe"; Copy-Item $tgt $otherExe
$argsExe = Join-Path $work "LaArgs.exe"; Copy-Item $tgt $argsExe
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$old = if ($OldExe) { $OldExe } else { Join-Path $repo "build\0.3.4\1Key.exe" }
$manifest = Join-Path $work "admin.manifest"
Set-Content $manifest -Encoding UTF8 -Value '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0"><trustInfo xmlns="urn:schemas-microsoft-com:asm.v3"><security><requestedPrivileges><requestedExecutionLevel level="requireAdministrator" uiAccess="false"/></requestedPrivileges></security></trustInfo></assembly>'
$cp = New-Object System.CodeDom.Compiler.CompilerParameters
$cp.GenerateExecutable = $true; $cp.OutputAssembly = (Join-Path $work "LaAdmin.exe"); $cp.CompilerOptions = "/target:winexe /win32manifest:`"$manifest`""
[void]$cp.ReferencedAssemblies.Add("System.Windows.Forms.dll"); [void]$cp.ReferencedAssemblies.Add("System.dll"); [void]$cp.ReferencedAssemblies.Add("System.Drawing.dll")
$res = (New-Object Microsoft.CSharp.CSharpCodeProvider).CompileAssemblyFromSource($cp, $targetSrc)
$adminExe = $cp.OutputAssembly
$folder = Join-Path $work "LaFolder"; New-Item -ItemType Directory -Force $folder | Out-Null
$wsh = New-Object -ComObject WScript.Shell
$lnkOk = Join-Path $work "LaLink.lnk"; $s = $wsh.CreateShortcut($lnkOk); $s.TargetPath = $tgt; $s.Save()
$lnkAdm = Join-Path $work "LaLinkAdmin.lnk"; $s = $wsh.CreateShortcut($lnkAdm); $s.TargetPath = $tgt; $s.Save()
$bytes = [IO.File]::ReadAllBytes($lnkAdm); $bytes[0x15] = $bytes[0x15] -bor 0x20; [IO.File]::WriteAllBytes($lnkAdm, $bytes)   # LinkFlags bit 13 RunAsUser (0x2000)

# ---- helpers
$ent = [Text.Encoding]::UTF8.GetBytes("1Key/launch-list/v1")
function WriteList([string]$text) { [IO.File]::WriteAllBytes("$cfg\launch.dat", [Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes($text), $ent, 'CurrentUser')) }
function ReadList() { [Text.Encoding]::UTF8.GetString([Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes("$cfg\launch.dat"), $ent, 'CurrentUser')) }
function Item([int]$i, [string]$id, [string]$kind, [string]$name, [string]$target, [int]$mods = 0, [int]$vk = 0) { "l$i.id=$id`nl$i.kind=$kind`nl$i.name=$name`nl$i.target=$($target.Replace('\','\\'))`nl$i.mods=$mods`nl$i.vk=$vk`n" }
function Procs([string]$path) { @(Get-Process -ErrorAction Ignore | Where-Object { try { $_.Path -eq $path } catch { $false } }) }
function KillTargets() { foreach ($x in @($tgt, $slow, $gone, $adminExe, $late, $nowin, $blind, $blindZero, $spawn, $child, $otherExe)) { Procs $x | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction Ignore } }; foreach ($h in [LA]::Explorer("LaFolder")) { [void][LA]::PostMessageW($h, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) } }
function Start1Key() {
  $script:p = Start-Process $exe -PassThru; $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $script:m = [LA]::MainWnd([uint32]$script:p.Id, "OneKeyMainWindow$suffix"); if ($script:m -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 100 }
  if ($script:m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 900
}
function Quit1Key() { if ($script:p -and -not $script:p.HasExited) { [void][LA]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i = 0; $i -lt 40 -and -not $script:p.HasExited; $i++) { Start-Sleep -Milliseconds 100 }; if (-not $script:p.HasExited) { Stop-Process -Id $script:p.Id -Force } } }
function Has($id) { [LA]::GetDlgItem($m, $id) -ne [IntPtr]::Zero }
function Click($id) { [void][LA]::PostMessageW([LA]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 600 }
function Hot([int]$i) { [void][LA]::PostMessageW($m, 0x0312, [IntPtr](200 + $i), [IntPtr]::Zero) }   # WM_HOTKEY for launch item i
function Toasts() { [LA]::Count([uint32]$p.Id, "OneKeyToast") }
function Unlock() { [void][LA]::SendMessageW([LA]::GetDlgItem($m, 101), 0x000C, [IntPtr]::Zero, $master); Click 103; Start-Sleep -Milliseconds 600 }
function ItemIcon([int]$i, [string]$icon, [string]$idx) { "l$i.icon=$($icon.Replace('\','\\'))`nl$i.iconidx=$idx`n" }
function TT() { "[toast: " + [LA]::ToastText([uint32]$p.Id) + "]" }   # what 1Key said (diagnosis only, not judged)
function ExecCalls() { [int][LA]::GetPropW($m, "OneKeyTestLaunchExec") }
function Dialog() { [LA]::MainWnd([uint32]$p.Id, "OneKeyDialog") }
function Answer([int]$id) { $b = Dialog; if ($b -ne [IntPtr]::Zero) { [void][LA]::PostMessageW($b, 0x0111, [IntPtr]$id, [IntPtr]::Zero); Start-Sleep -Milliseconds 500 } }   # 1 OK, 6 Yes, 7 No
function StartWith([string]$path, [string]$mode) { $env:ONEKEY_LAUNCH_TARGET_MODE = $mode; $pr = Start-Process $path -PassThru; $env:ONEKEY_LAUNCH_TARGET_MODE = $null; Start-Sleep -Milliseconds 1500; $pr }
function WaitProc([string]$path, [int]$ms) { for ($i = 0; $i -lt ($ms / 100); $i++) { if ((Procs $path).Count -gt 0) { break }; Start-Sleep -Milliseconds 100 } }
function Icons() { [int][LA]::SendMessageW($m, 0x8000 + 41, [IntPtr]::Zero, [IntPtr]::Zero) }   # real icons received (test mode)
function Rect($id) { $r = New-Object LA+RECT; [void][LA]::GetWindowRect([LA]::GetDlgItem($m, $id), [ref]$r); $r }
function Rgn($id) { [LA]::RgnKind([LA]::GetDlgItem($m, $id)) }
function Wheel($r, [int]$notches) {   # WM_MOUSEWHEEL to the main window with the screen point in the middle of rect r (negative = down)
  $d = (120 * $notches) -band 0xFFFF; $x = [int](($r.L + $r.R) / 2); $y = [int](($r.T + $r.B) / 2)
  [void][LA]::SendMessageW($m, 0x020A, [IntPtr]([long]$d -shl 16), [IntPtr](($x -band 0xFFFF) -bor ($y -shl 16))); Start-Sleep -Milliseconds 400
}
function DragTile($h, [int]$toX) {   # press in the middle of tile h, move to screen x toX in 10 steps, release (sent, so each step sees the moved tile)
  $r = New-Object LA+RECT; [void][LA]::GetWindowRect($h, [ref]$r)
  $cx = [int](($r.R - $r.L) / 2); $cy = [int](($r.B - $r.T) / 2); $sx = $r.L + $cx
  [void][LA]::SendMessageW($h, 0x0201, [IntPtr]1, [IntPtr]($cx -bor ($cy -shl 16))); Start-Sleep -Milliseconds 120
  for ($k = 1; $k -le 10; $k++) {
    $now = New-Object LA+RECT; [void][LA]::GetWindowRect($h, [ref]$now)
    $lx = [int]($sx + ($toX - $sx) * $k / 10) - $now.L
    [void][LA]::SendMessageW($h, 0x0200, [IntPtr]1, [IntPtr](($lx -band 0xFFFF) -bor ($cy -shl 16))); Start-Sleep -Milliseconds 40
  }
  $now = New-Object LA+RECT; [void][LA]::GetWindowRect($h, [ref]$now)
  [void][LA]::SendMessageW($h, 0x0202, [IntPtr]::Zero, [IntPtr]((($toX - $now.L) -band 0xFFFF) -bor ($cy -shl 16)))
}
function DropTest([string[]]$lines) { [IO.File]::WriteAllLines("$cfg\drop-test.txt", $lines); [void][LA]::PostMessageW($m, 0x8000 + 44, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 1000 }
$CtrlAlt = 0x0003

try {
  Stop-TestInstances $suffix
  KillTargets
  if (Test-Path $cfg) { Remove-Item $cfg -Recurse -Force }; New-Item -ItemType Directory -Force $cfg | Out-Null
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_LOCKWIDGET = "0"; $env:ONEKEY_TEST_NO_AUTOLOCK = "1"
  $env:ONEKEY_LAUNCH_TARGET_DELAY = $null; $env:ONEKEY_TEST_FAIL = $null
  # master on the old screen, then quit
  Start1Key
  [void][LA]::SendMessageW([LA]::GetDlgItem($m, 101), 0x000C, [IntPtr]::Zero, $master); [void][LA]::SendMessageW([LA]::GetDlgItem($m, 102), 0x000C, [IntPtr]::Zero, $master); Click 103
  Start-Sleep -Milliseconds 800
  Quit1Key

  # ---- LA01..LA06 + LA13: exe, slow exe, gone exe, folder (F7..F10 with Ctrl+Alt)
  WriteList ("launchv=1`n" + (Item 0 "a0000001" "exe" "Target" $tgt $CtrlAlt 0x76) + (Item 1 "a0000002" "exe" "Slow" $slow $CtrlAlt 0x77) + (Item 2 "a0000003" "exe" "Gone" $gone $CtrlAlt 0x78) + (Item 3 "a0000004" "folder" "Folder" $folder $CtrlAlt 0x79))
  $sm = Join-Path $work "StartMenu"; New-Item -ItemType Directory -Force "$sm\Sub1", "$sm\Sub2" | Out-Null
  foreach ($l in @("$sm\Alpha.lnk", "$sm\Sub1\Same.lnk", "$sm\Sub2\Same.lnk", "$sm\Uninstall Alpha.lnk")) { $s = $wsh.CreateShortcut($l); $s.TargetPath = $tgt; $s.Save() }
  $env:ONEKEY_TEST_STARTMENU = $sm
  $tb = Join-Path $work "Taskbar"; New-Item -ItemType Directory -Force $tb | Out-Null
  $s = $wsh.CreateShortcut("$tb\Pinned One.lnk"); $s.TargetPath = $tgt; $s.Save()
  $env:ONEKEY_TEST_TASKBAR = $tb
  Start1Key
  $locked = Has 101
  Check LA01 "True|True|True|0" "$locked|$([LA]::Taken($CtrlAlt, 0x76))|$([LA]::Taken($CtrlAlt, 0x79))|$((Procs $tgt).Count)" "list read while locked: hotkeys registered, nothing ran"
  Hot 0; WaitProc $tgt 4000; Start-Sleep -Milliseconds 1200
  Check LA02 "1|True|False" "$((Procs $tgt).Count)|$(Has 101)|$(Has 203)" "hotkey while locked: program started once, 1Key still locked"
  Start-Sleep -Milliseconds 600
  Hot 0; Start-Sleep -Milliseconds 1200
  $fg = [LA]::GetForegroundWindow()
  $fgPid = 0; [void][LA]::GetWindowThreadProcessId($fg, [ref]$fgPid)
  $fgCls = [LA]::Cls($fg)
  Check LA03 "1|True" "$((Procs $tgt).Count)|$($fgPid -eq (Procs $tgt)[0].Id)" "again: no second process, its window in front [front: $fgCls] $(TT)"

  # LA04: slow target (needs a new 1Key so the delay variable reaches the launched program)
  Quit1Key
  $env:ONEKEY_LAUNCH_TARGET_DELAY = "2000"
  Start1Key
  Hot 1; Start-Sleep -Milliseconds 600; Hot 1; Start-Sleep -Milliseconds 400; Hot 1
  Start-Sleep -Milliseconds 3000
  $n1 = (Procs $slow).Count
  Start-Sleep -Milliseconds 600; Hot 1; Start-Sleep -Milliseconds 1000
  Check LA04 "1|1" "$n1|$((Procs $slow).Count)" "window after 2 s: requests at 0 / 0.6 / 1.0 s and after start one process"
  $env:ONEKEY_LAUNCH_TARGET_DELAY = $null

  # LA05: target gone
  Remove-Item $gone -Force
  $t0 = Toasts
  Hot 2; Start-Sleep -Milliseconds 900
  Check LA05 "0|True" "$((Procs $gone).Count)|$((Toasts) -gt $t0 -or (Toasts) -gt 0)" "target gone: nothing starts, warning toast"

  # LA06: folder
  Hot 3; Start-Sleep -Milliseconds 2500
  $w1 = ([LA]::Explorer("LaFolder")).Count
  $t6 = TT; $cab = @(Get-Process explorer -ErrorAction Ignore).Count
  Start-Sleep -Milliseconds 600; Hot 3; Start-Sleep -Milliseconds 2000
  $w2 = ([LA]::Explorer("LaFolder")).Count
  Check LA06 "1|1" "$w1|$w2" "folder: one Explorer window, reused on the second press $t6 [explorer processes: $cab]"

  # LA13: strip (unlock first)
  KillTargets; Start-Sleep -Milliseconds 800
  Unlock
  $tiles = "$(Has 4100)|$(Has 4101)|$(Has 4102)|$(Has 4103)|$(Has 4142)"
  $t0h = [LA]::GetDlgItem($m, 4100)
  [void][LA]::SendMessageW($m, 0x0028, $t0h, [IntPtr]1)   # WM_NEXTDLGCTL to tile 0
  Start-Sleep -Milliseconds 200
  [void][LA]::PostMessageW($t0h, 0x0100, [IntPtr]0x27, [IntPtr]::Zero); Start-Sleep -Milliseconds 400   # Right
  $f1 = [LA]::Focus($m) -eq [LA]::GetDlgItem($m, 4101)
  [void][LA]::PostMessageW($t0h, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)   # press tile 0 (BM_CLICK = Enter path)
  WaitProc $tgt 4000; Start-Sleep -Milliseconds 800
  Check LA13 "True|True|True|True|True|True|1" "$tiles|$f1|$((Procs $tgt).Count)" "strip: 4 tiles + [Edit], Right moves to the next tile, pressing a tile starts its program"
  $r0 = New-Object LA+RECT; [void][LA]::GetWindowRect([LA]::GetDlgItem($m, 4100), [ref]$r0)
  $r3 = New-Object LA+RECT; [void][LA]::GetWindowRect([LA]::GetDlgItem($m, 4103), [ref]$r3)
  Check LA15 "True|True|True|True" "$($r3.T -ge $r0.B)|$($r3.L -eq $r0.L)|$(($r3.R - $r3.L) -eq ($r0.R - $r0.L))|$(($r0.B - $r0.T) -lt ($r3.B - $r3.T))" "folder tile on the row below; both rows in the same column (left edge, width); folder tiles show the name (taller), program tiles are icon only (shorter)"
  # LA14: the installed-program list (isolated Start menu, ONEKEY_TEST_STARTMENU set before this 1Key started)
  Click 203; Click 4002
  for ($i = 0; $i -lt 40 -and -not (Has 4300); $i++) { Start-Sleep -Milliseconds 100 }
  $rows = "$(Has 4300)|$(Has 4301)|$(Has 4302)|$(Has 4303)|$(-not (Has 4304))|$([LA]::Title([LA]::GetDlgItem($m, 4300)) -eq 'Pinned One')"
  $listed = (Has 4202) -and (Has 4201)
  $sf0 = [LA]::GetDlgItem($m, 4202)
  [void][LA]::SendMessageW($sf0, 0x000C, [IntPtr]::Zero, "zzzz-no-such-program-1key")
  Start-Sleep -Milliseconds 1200
  Check LA14 "True|True|True|True|True|True|True|False|True|True" "$listed|$rows|$(Has 4300)|$(Has 4202)|$([LA]::GetDlgItem($m, 4202) -eq $sf0)" "program picker: the pinned taskbar app first, then Alpha + two same-named shortcuts (uninstall hidden); a search with no match leaves no rows; the search field is the same window (not remade)"
  Click 4200; Click 4000
  KillTargets
  Quit1Key

  # ---- LA07: unknown format
  $unk = "launchv=2`n" + (Item 0 "b0000001" "exe" "Target" $tgt $CtrlAlt 0x76)
  WriteList $unk
  $h0 = (Get-FileHash "$cfg\launch.dat").Hash
  Start1Key; Unlock
  $note = Has 4143
  Hot 0; Start-Sleep -Milliseconds 1500
  Check LA07 "False|0|True|True" "$([LA]::Taken($CtrlAlt, 0x76))|$((Procs $tgt).Count)|$($h0 -eq (Get-FileHash "$cfg\launch.dat").Hash)|$note" "unknown format: no hotkey, nothing runs, file unchanged, note shown"
  Quit1Key

  # ---- LA08: damaged item + unknown field + stray line preserved through an edit of the good item
  WriteList ("launchv=1`n" + (Item 0 "c0000001" "exe" "Good" $tgt) + "l0.future=keep-me`n" + (Item 1 "c0000002" "weird" "Bad" $tgt) + "stray line kept`n")
  Start1Key; Unlock
  Click 4142            # [Edit] mode
  Click 4100            # tile 0 -> edit screen
  $onEdit = Has 4011
  [void][LA]::SendMessageW([LA]::GetDlgItem($m, 4011), 0x000C, [IntPtr]::Zero, "Good2")
  Click 4018            # save
  Start-Sleep -Milliseconds 600
  $txt = ReadList
  Check LA08 "True|True|True|True|True" "$onEdit|$($txt.Contains('name=Good2'))|$($txt.Contains('kind=weird'))|$($txt.Contains('future=keep-me'))|$($txt.Contains('stray line kept'))" "edit of the good item keeps the damaged item, the unknown field and the stray line"
  Quit1Key

  # ---- LA09: save fails
  $h0 = (Get-FileHash "$cfg\launch.dat").Hash
  $env:ONEKEY_TEST_FAIL = "launch:save"
  Start1Key; Unlock
  Click 4142; Click 4100
  [void][LA]::SendMessageW([LA]::GetDlgItem($m, 4011), 0x000C, [IntPtr]::Zero, "Good3")
  Click 4018; Start-Sleep -Milliseconds 600
  $box = [LA]::Count([uint32]$p.Id, "OneKeyDialog") -gt 0
  foreach ($bx in 1..2) { $b = [LA]::MainWnd([uint32]$p.Id, "OneKeyDialog"); if ($b -ne [IntPtr]::Zero) { [void][LA]::PostMessageW($b, 0x0111, [IntPtr]1, [IntPtr]::Zero); Start-Sleep -Milliseconds 500 } }
  $kept = (Has 4011) -and ([LA]::SendMessageW([LA]::GetDlgItem($m, 4011), 0x000E, [IntPtr]::Zero, [IntPtr]::Zero) -eq 5)   # WM_GETTEXTLENGTH "Good3"
  Check LA09 "True|True|True" "$box|$($h0 -eq (Get-FileHash "$cfg\launch.dat").Hash)|$kept" "save fails: error box, file unchanged, typed name kept"
  $env:ONEKEY_TEST_FAIL = $null
  Quit1Key

  # ---- LA10: duplicate id = the whole list read-only
  WriteList ("launchv=1`n" + (Item 0 "d0000001" "exe" "First" $tgt $CtrlAlt 0x76) + (Item 1 "d0000001" "exe" "Dup" $tgt $CtrlAlt 0x77))
  $h0 = (Get-FileHash "$cfg\launch.dat").Hash
  Start1Key
  Hot 0; Hot 1; Start-Sleep -Milliseconds 1500
  $reg = "$([LA]::Taken($CtrlAlt, 0x76))|$([LA]::Taken($CtrlAlt, 0x77))"
  Unlock
  Check LA10 "False|False|0|True|True|True" "$reg|$((Procs $tgt).Count)|$($h0 -eq (Get-FileHash "$cfg\launch.dat").Hash)|$(Has 4143)|$(Has 4146)" "duplicate id: no hotkey, nothing runs, file unchanged, note + [Start a new list]"
  Quit1Key

  # ---- LA11..LA12: admin manifest, shortcuts
  WriteList ("launchv=1`n" + (Item 0 "d0000001" "exe" "First" $tgt $CtrlAlt 0x76) + (Item 2 "d0000003" "exe" "Admin" $adminExe $CtrlAlt 0x78) + (Item 3 "d0000004" "lnk" "LinkAdmin" $lnkAdm $CtrlAlt 0x79) + (Item 4 "d0000005" "lnk" "Link" $lnkOk $CtrlAlt 0x7A))
  Start1Key
  $t0 = Toasts
  Hot 1; Start-Sleep -Milliseconds 1500
  Check LA11 "True|0|True" "$(Test-Path $adminExe)|$((Procs $adminExe).Count)|$((Toasts) -gt 0)" "requireAdministrator manifest: refused, nothing starts, warning toast"
  Hot 2; Start-Sleep -Milliseconds 1500
  $na = (Procs $tgt).Count
  Hot 3; WaitProc $tgt 4000; Start-Sleep -Milliseconds 800
  Check LA12 "0|1" "$na|$((Procs $tgt).Count)" "run-as-administrator shortcut refused; a normal shortcut starts the program once"
  Quit1Key; KillTargets

  # ---- LA31: slow icons still arrive
  WriteList ("launchv=1`n" + (Item 0 "f0000001" "exe" "Target" $tgt) + (Item 1 "f0000002" "folder" "Folder" $folder))
  $env:ONEKEY_TEST_FAIL = "icon:slow"
  Start1Key; Unlock
  Start-Sleep -Milliseconds 1000
  $early = Icons
  for ($i = 0; $i -lt 40 -and (Icons) -lt 2; $i++) { Start-Sleep -Milliseconds 500 }
  Check LA31 "0|2" "$early|$(Icons)" "slow icons (5 s each, limit 3 s): none at first, both real icons arrive late and are used"
  $env:ONEKEY_TEST_FAIL = $null
  Quit1Key

  # ---- LA32: strip box, badge, per-row horizontal scroll
  WriteList ("launchv=1`n" + (-join (0..13 | ForEach-Object { Item $_ ("f1{0:x6}" -f $_) "exe" "P$_" $tgt })) + (Item 14 "f1000100" "folder" "Folder" $folder))
  Start1Key; Unlock
  $a = Rect 4100; $f = Rect 4114; $e = Rect 4142
  $col = ($a.L -eq $f.L) -and (($a.R - $a.L) -eq ($f.R - $f.L))
  $badge = ($e.T -lt $a.T) -and ($e.R -gt $a.R) -and (($e.R - $e.L) -eq ($e.B - $e.T)) -and ((Rgn 4142) -ge 2)
  $cut = (Rgn 4113) -ge 1; $folderWhole = (Rgn 4114) -eq 0
  $vis = { param($id) [LA]::IsWindowVisible([LA]::GetDlgItem($m, $id)) }
  $chev0 = "$(& $vis 4140)/$(& $vis 4141)/$((Has 4144))/$((Rect 4140).R -le $a.L)"   # < hidden, > shown, no chevrons on the folder row, < has its own room (left of the first tile)
  Click 4141
  $chev1 = "$((Rect 4100).L -lt $a.L)/$(& $vis 4140)"
  for ($i = 0; $i -lt 10 -and (& $vis 4141); $i++) { Click 4141 }
  $chev2 = "$(& $vis 4141)/$((Rgn 4113) -eq 0)"
  Click 4140; Start-Sleep -Milliseconds 200
  for ($i = 0; $i -lt 10 -and (& $vis 4140); $i++) { Click 4140 }
  $back = (Rect 4100).L -eq $a.L
  Check LA32 "True|True|True|True|False/True/False/True|True/True|False/True|True" "$col|$badge|$cut|$folderWhole|$chev0|$chev1|$chev2|$back" "same column in both rows, round badge over the box corner, the 14th program cut at the box edge, folder row whole; chevrons: at the start only >, > moves a screen and shows <, at the end > hides and the last tile is whole, < brings it back"
  Wheel $a -12
  $moved = (Rect 4100).L -lt $a.L
  Check LA32b "True|True|True" "$moved|$((Rgn 4113) -eq 0)|$((Rect 4114).L -eq $f.L)" "the wheel scrolls only the program row and shows the last tile"
  Quit1Key

  # ---- LA39: cut tiles as screen readers read them (MSAA)
  WriteList ("launchv=1`n" + (-join (0..29 | ForEach-Object { Item $_ ("f5{0:x6}" -f $_) "exe" "P$_" $tgt })))
  Start1Key; Unlock
  $kinds = @(0..29 | ForEach-Object { Rgn (4100 + $_) })
  $hi = [array]::IndexOf($kinds, 1); $pi = -1; for ($i = 0; $i -lt 30; $i++) { if ($kinds[$i] -ge 2) { $pi = $i; break } }
  $off = 0x10000
  $hidH = [LA]::GetDlgItem($m, 4100 + [Math]::Max(0, $hi)); $wholeH = [LA]::GetDlgItem($m, 4100)
  $hidOk = ($hi -ge 0) -and (([LAX]::State($hidH) -band $off) -ne 0)
  $wholeOk = (([LAX]::State($wholeH) -band $off) -eq 0) -and ([LAX]::Loc($wholeH) -eq [LAX]::Shown($wholeH)) -and ($kinds[0] -eq 0)
  if ($pi -ge 0) {
    $partH = [LA]::GetDlgItem($m, 4100 + $pi); $pw = [int]([LAX]::Shown($partH).Split(',')[2])
    $partOk = "$((([LAX]::State($partH) -band $off) -eq 0) -and ([LAX]::Loc($partH) -eq [LAX]::Shown($partH)) -and ($pw -gt 0) -and ($pw -lt [LAX]::Width($partH)))"
  } else { $partOk = "no partly shown tile (kinds $($kinds -join ''))" }
  $events = if ($hi -ge 0) { [LAX]::FocusWatch([uint32]$p.Id, $hidH, $m) } else { -9 }
  Start-Sleep -Milliseconds 300
  $after = "$((([LAX]::State($hidH) -band $off) -eq 0) -and ([LA]::RgnKind($hidH) -eq 0))|$([LA]::Focus($m) -eq $hidH)|$($events -ge 1)"
  Check LA39 "True|True|True|True|True|True" "$hidOk|$wholeOk|$partOk|$after" "MSAA of cut tiles: hidden = OFFSCREEN; whole = not, location = window; partly shown = not, location = shown part; focus on the hidden tile scrolls it in, OFFSCREEN clears, state-change event sent (events $events, region kinds $($kinds -join ''))"
  Quit1Key

  # ---- LA33: drag to reorder
  WriteList ("launchv=1`n" + (Item 0 "f2000001" "exe" "A" $tgt $CtrlAlt 0x76) + (Item 1 "f2000002" "exe" "B" $slow) + (Item 2 "f2000003" "exe" "C" $late))
  Start1Key; Unlock
  Click 4142
  $c2 = Rect 4102
  DragTile ([LA]::GetDlgItem($m, 4100)) ([int](($c2.L + $c2.R) / 2) + 10)
  Start-Sleep -Milliseconds 900
  $order = ([regex]::Matches((ReadList), 'l\d+\.name=(\w+)') | ForEach-Object { $_.Groups[1].Value }) -join ""
  $noEdit = -not (Has 4011)
  Hot 2; WaitProc $tgt 4000; Start-Sleep -Milliseconds 600
  Check LA33 "BCA|True|True|1" "$order|$noEdit|$([LA]::Taken($CtrlAlt, 0x76))|$((Procs $tgt).Count)" "drag A past C in edit mode: saved order B C A, no edit screen, A's hotkey still held; A (now third) starts"
  Quit1Key; KillTargets

  # ---- LA34: Microsoft Store app (Calculator)
  $calcId = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"
  $calcName = (Get-StartApps | Where-Object { $_.AppID -eq $calcId } | Select-Object -First 1).Name
  function CalcProcs() { @(Get-Process CalculatorApp, Calculator -ErrorAction Ignore) }
  if (-not $calcName -or (CalcProcs).Count -gt 0) {
    Check LA34 "skip" "skip" "Calculator not installed or already running - the store app path is not checked here"
    Check LA34b "skip" "skip" "(with LA34)"
  } else {
    WriteList ("launchv=1`n" + (Item 0 "f3000001" "app" "Calc" $calcId $CtrlAlt 0x76))
    $env:ONEKEY_TEST_STOREAPPS = "1"
    Start1Key
    $valid = [LA]::Taken($CtrlAlt, 0x76)
    Hot 0
    for ($i = 0; $i -lt 80 -and [LA]::CountTitle($calcName) -eq 0; $i++) { Start-Sleep -Milliseconds 100 }
    Start-Sleep -Milliseconds 2500
    $n1 = [LA]::CountTitle($calcName)
    Hot 0; Start-Sleep -Milliseconds 2000
    $n2 = [LA]::CountTitle($calcName)
    $front = [LA]::Title([LA]::GetForegroundWindow()) -eq $calcName
    Unlock
    for ($i = 0; $i -lt 30 -and (Icons) -lt 1; $i++) { Start-Sleep -Milliseconds 200 }
    $icon = (Icons) -ge 1
    Click 203; Click 4002
    for ($i = 0; $i -lt 80 -and -not (Has 4300); $i++) { Start-Sleep -Milliseconds 100 }
    [void][LA]::SendMessageW([LA]::GetDlgItem($m, 4202), 0x000C, [IntPtr]::Zero, [string]$calcName)
    Start-Sleep -Milliseconds 1200
    $listed = Has 4300
    Click 4200; Click 4000
    Check LA34 "True|1|1|True|True|True" "$valid|$n1|$n2|$front|$icon|$listed" "store app: hotkey starts Calculator (one window), a second press brings it to the front without a second window, icon read, listed in the picker"
    CalcProcs | Stop-Process -Force -ErrorAction Ignore
    $env:ONEKEY_TEST_STOREAPPS = $null
    Quit1Key
    # LA34b: not installed; manifest unreadable
    WriteList ("launchv=1`n" + (Item 0 "f3000002" "app" "Missing" "OneKey.TestMissing_0000000000000!App" $CtrlAlt 0x76) + (Item 1 "f3000001" "app" "Calc" $calcId $CtrlAlt 0x77))
    Remove-Item "$cfg\launch-why.txt" -ErrorAction Ignore
    $env:ONEKEY_TEST_FAIL = "launch:appmanifest"
    Start1Key
    $c0 = ExecCalls
    Hot 0; Start-Sleep -Milliseconds 1500
    $missing = "$((Toasts) -gt 0)|$((ExecCalls) - $c0)"
    Hot 1; Start-Sleep -Milliseconds 2500
    $why = (Get-Content "$cfg\launch-why.txt" -ErrorAction Ignore) -join ","
    $manifestCase = "$((ExecCalls) - $c0)|$((CalcProcs).Count)|$($why -like '*app-verify*')"
    Quit1Key; CalcProcs | Stop-Process -Force -ErrorAction Ignore
    $env:ONEKEY_TEST_FAIL = "launch:appquery"   # the app id lookup itself fails
    Start1Key
    $c1 = ExecCalls
    Hot 1; Start-Sleep -Milliseconds 2500
    $queryCase = "$((ExecCalls) - $c1)|$((CalcProcs).Count)"
    Check LA34b "True|0|0|0|True|0|0" "$missing|$manifestCase|$queryCase" "store app guards: a not-installed app id opens nothing (notice); manifest unreadable -> Calculator refused, nothing starts; app id lookup fails -> nothing starts $(TT)"
    $env:ONEKEY_TEST_FAIL = $null
    Quit1Key; CalcProcs | Stop-Process -Force -ErrorAction Ignore
  }

  # ---- LA35: dropped items
  WriteList "launchv=1`n"
  Start1Key
  DropTest @($folder)
  $lockedIgnored = -not (Has 4011)
  Unlock
  DropTest @($folder)
  $folderEdit = Has 4011; Click 4018
  DropTest @($tgt, $slow)
  $exeEdit = Has 4011; $said = (Toasts) -gt 0; Click 4018
  $txt = ReadList
  DropTest @("|Something")
  $asked = (Dialog) -ne [IntPtr]::Zero; Answer 1
  Check LA35 "True|True|True|True|True|True|False|True" "$lockedIgnored|$folderEdit|$exeEdit|$said|$($txt.Contains('kind=folder'))|$($txt.Contains('kind=exe'))|$($txt.Contains('LaSlow'))|$asked" "drop: ignored while locked; folder and program open the edit screen and save; two at once - the first only, with a notice; no path - asks to use the list"
  Quit1Key; KillTargets

  # ---- LA36: launcher-type program
  WriteList ("launchv=1`n" + (Item 0 "f4000001" "exe" "Spawn" $spawn $CtrlAlt 0x76))
  Start1Key
  Hot 0
  for ($i = 0; $i -lt 80 -and (Procs $child).Count -eq 0; $i++) { Start-Sleep -Milliseconds 100 }
  Start-Sleep -Milliseconds 5500   # LaChild shows its window after 2.5 s; the watch looks every second
  $fgPid = 0; [void][LA]::GetWindowThreadProcessId([LA]::GetForegroundWindow(), [ref]$fgPid)
  $childFront = ((Procs $child).Count -eq 1) -and ($fgPid -eq (Procs $child)[0].Id)
  $calls1 = ExecCalls
  Start-Sleep -Milliseconds 800   # past the press debounce
  Hot 0; Start-Sleep -Milliseconds 2000
  $fgPid2 = 0; [void][LA]::GetWindowThreadProcessId([LA]::GetForegroundWindow(), [ref]$fgPid2)
  Check LA36 "True|1|0|True" "$childFront|$((Procs $child).Count)|$((ExecCalls) - $calls1)|$($fgPid2 -eq (Procs $child)[0].Id)" "launcher program: the child's window is brought to the front; a second press runs nothing new and focuses the child's window"
  Quit1Key; KillTargets

  # ---- LA36b: an unrelated program from the same folder is not taken as the launch's window
  WriteList ("launchv=1`n" + (Item 0 "f4000002" "exe" "Target" $tgt $CtrlAlt 0x76))
  Start1Key
  Hot 0; WaitProc $tgt 4000
  $o = Start-Process $otherExe -PassThru; Start-Sleep -Milliseconds 3500   # the user starts another program of the same folder meanwhile
  Procs $tgt | ForEach-Object { Stop-Process -Id $_.Id -Force }; Start-Sleep -Milliseconds 800
  $c36 = ExecCalls
  Start-Sleep -Milliseconds 600; Hot 0; WaitProc $tgt 4000; Start-Sleep -Milliseconds 800
  Check LA36b "1|1|1" "$((ExecCalls) - $c36)|$((Procs $tgt).Count)|$((Procs $otherExe).Count)" "same folder is not identity: the next press starts the target again (one launch call) and does not settle for the other program"
  Stop-Process -Id $o.Id -Force -ErrorAction Ignore
  Quit1Key; KillTargets

  # ---- LA36c: the user is in another window when the launcher's child appears: no foreground steal
  WriteList ("launchv=1`n" + (Item 0 "f4000003" "exe" "Spawn" $spawn $CtrlAlt 0x76))
  Start1Key
  $form = New-Object Windows.Forms.Form; $form.Text = "1Key test - user window"; $form.Width = 300; $form.Height = 120; $form.Show(); [Windows.Forms.Application]::DoEvents()
  Hot 0; Start-Sleep -Milliseconds 400
  [void][LA]::Front($form.Handle); [Windows.Forms.Application]::DoEvents()   # the user goes to another window before the child shows (2.5 s)
  for ($i = 0; $i -lt 60 -and (Procs $child).Count -eq 0; $i++) { Start-Sleep -Milliseconds 100; [Windows.Forms.Application]::DoEvents() }
  for ($i = 0; $i -lt 50; $i++) { Start-Sleep -Milliseconds 100; [Windows.Forms.Application]::DoEvents() }
  $userFront = [LA]::GetForegroundWindow() -eq $form.Handle
  $form.Close()
  Check LA36c "1|True" "$((Procs $child).Count)|$userFront" "the user moved to another window before the launcher's child appeared: the child is not pulled in front (the user's window stays)"
  Quit1Key; KillTargets

  # ---- LA38: shortcut arguments and working folder reach the program
  $wd = Join-Path $work "Work Dir"; New-Item -ItemType Directory -Force $wd | Out-Null
  $ko = [string]([char]0xD55C) + [char]0xAE00
  $lnkArgs = Join-Path $work "LaArgs.lnk"; $s = $wsh.CreateShortcut($lnkArgs); $s.TargetPath = $argsExe; $s.Arguments = "`"two words`" $ko `"q\`"x`" plain"; $s.WorkingDirectory = $wd; $s.Save()
  $outArgs = Join-Path $work "args-out.txt"; Remove-Item $outArgs -ErrorAction Ignore
  WriteList ("launchv=1`n" + (Item 0 "f6000001" "lnk" "Args" $lnkArgs $CtrlAlt 0x76))
  Start1Key
  Hot 0
  for ($i = 0; $i -lt 60 -and -not (Test-Path $outArgs); $i++) { Start-Sleep -Milliseconds 100 }
  Start-Sleep -Milliseconds 300
  $got = if (Test-Path $outArgs) { [IO.File]::ReadAllLines($outArgs, [Text.Encoding]::UTF8) } else { @() }
  $argsOk = ($got.Count -eq 5) -and ($got[1] -eq "two words") -and ($got[2] -eq $ko) -and ($got[3] -eq 'q"x') -and ($got[4] -eq "plain")
  $dirOk = ($got.Count -gt 0) -and ($got[0].TrimEnd('\') -eq $wd.TrimEnd('\'))
  Check LA38 "True|True" "$argsOk|$dirOk" "shortcut arguments (spaces, Korean, a quote) and its working folder reach the started program (got $($got.Count) lines)"
  Quit1Key; KillTargets

  # ---- LA37: main window owned by a visible 0x0 window (Delphi style)
  WriteList ("launchv=1`n" + (Item 0 "f5000001" "exe" "Owned" $tgt $CtrlAlt 0x76))
  $env:ONEKEY_LAUNCH_TARGET_MODE = "owned"
  Start1Key
  $env:ONEKEY_LAUNCH_TARGET_MODE = $null
  Hot 0; WaitProc $tgt 4000; Start-Sleep -Milliseconds 2500
  $n37 = (Procs $tgt).Count
  Hot 0; Start-Sleep -Milliseconds 2000
  $fgPid = 0; [void][LA]::GetWindowThreadProcessId([LA]::GetForegroundWindow(), [ref]$fgPid)
  Check LA37 "1|1|True" "$n37|$((Procs $tgt).Count)|$($fgPid -eq (Procs $tgt)[0].Id)" "owned main window (Delphi style): one process, a second press finds the window and brings it to the front"
  Quit1Key; KillTargets
  if ($Edge) {
  # ---- LA16: structural damage cases
  $good = (Item 0 "e0000001" "exe" "Target" $tgt $CtrlAlt 0x76)
  $cases = [ordered]@{
    dupTarget = "launchv=1`n" + $good + "l0.target=C:\\x.exe`n"
    dupMods   = "launchv=1`n" + $good + "l0.mods=0`n"
    dupIdx    = "launchv=1`n" + $good + (ItemIcon 0 $tgt "0") + "l0.iconidx=1`n"
    badMods   = "launchv=1`n" + $good.Replace("l0.mods=3", "l0.mods=3x")
    badVk     = "launchv=1`n" + $good.Replace("l0.vk=118", "l0.vk=-1")
    badIdx    = "launchv=1`n" + $good + (ItemIcon 0 $tgt "abc")
    iconOnly  = "launchv=1`n" + $good + "l0.icon=$($tgt.Replace('\','\\'))`n"
    version2  = "launchv=-1`nlaunchv=1`n" + $good
    noName    = "launchv=1`n" + ($good -replace "l0\.name=Target`n", "")
    over30    = "launchv=1`n" + $good + (-join (1..30 | ForEach-Object { Item $_ ("e1{0:x6}" -f $_) "exe" "N$_" $tgt }))
  }
  $res16 = @()
  foreach ($k in $cases.Keys) {
    WriteList $cases[$k]
    $h0 = (Get-FileHash "$cfg\launch.dat").Hash
    Start1Key
    Hot 0; Start-Sleep -Milliseconds 1200
    $ok = (-not [LA]::Taken($CtrlAlt, 0x76)) -and ((Procs $tgt).Count -eq 0) -and ($h0 -eq (Get-FileHash "$cfg\launch.dat").Hash)
    if (-not $ok) { $res16 += $k }
    Quit1Key; KillTargets
  }
  Check LA16 "" ($res16 -join ",") "every structural damage case: read-only (no hotkey, nothing runs, file unchanged) - failing cases listed"

  # ---- LA17: [Start a new list]
  WriteList $cases["dupMods"]
  $h0 = (Get-FileHash "$cfg\launch.dat").Hash
  Remove-Item "$cfg\launch.dat.bad", "$cfg\launch-*.dat.bad" -ErrorAction Ignore
  $env:ONEKEY_TEST_FAIL = "launch:reset"
  Start1Key; Unlock
  Click 4146; Answer 6; Start-Sleep -Milliseconds 400; Answer 1
  $failKept = ($h0 -eq (Get-FileHash "$cfg\launch.dat").Hash) -and -not (Test-Path "$cfg\launch.dat.bad") -and (Has 4143)
  Quit1Key; $env:ONEKEY_TEST_FAIL = $null
  Start1Key; Unlock
  Click 4146; Answer 6; Start-Sleep -Milliseconds 600
  $bad = (Test-Path "$cfg\launch.dat.bad") -and ((Get-FileHash "$cfg\launch.dat.bad").Hash -eq $h0)
  $note = Has 4143
  Click 203
  $enabled = (Has 4002) -and [LA]::IsWindowEnabled([LA]::GetDlgItem($m, 4002))
  Click 4000
  Check LA17 "True|True|False|True|False" "$failKept|$bad|$note|$enabled|$(Test-Path "$cfg\launch.dat")" "reset: a failed move changes nothing; [Yes] keeps the old file as .bad, note gone, [Program] usable, no launch.dat yet"
  Quit1Key

  # ---- LA18: icon fields across versions; deleted icon file
  $iconFile = Join-Path $work "LaIcon.exe"; Copy-Item $tgt $iconFile
  WriteList ("launchv=1`n" + (Item 0 "f0000001" "exe" "Target" $tgt $CtrlAlt 0x76) + (ItemIcon 0 $iconFile "0") + (Item 1 "f0000002" "folder" "Folder" $folder $CtrlAlt 0x79))
  Start1Key; Unlock
  Click 4142; Click 4101
  [void][LA]::SendMessageW([LA]::GetDlgItem($m, 4011), 0x000C, [IntPtr]::Zero, "Folder2"); Click 4018; Start-Sleep -Milliseconds 400
  $afterNew = ReadList
  Quit1Key
  $oldOk = Test-Path $old
  if ($oldOk) {
    $exeSave = $exe; $exe = $old
    Start1Key; Unlock
    Click 4142; Click 4101
    [void][LA]::SendMessageW([LA]::GetDlgItem($m, 4011), 0x000C, [IntPtr]::Zero, "Folder3"); Click 4018; Start-Sleep -Milliseconds 400
    Quit1Key
    $exe = $exeSave
  }
  $afterOld = ReadList
  Remove-Item $iconFile -Force
  Start1Key
  $valid = [LA]::Taken($CtrlAlt, 0x76)
  Unlock; $alive = [LA]::Answers($m, 1000)
  Check LA18 "True|True|True|True|True|True" "$($afterNew.Contains('l0.icon=') -and $afterNew.Contains('l0.iconidx=0'))|$oldOk|$($afterOld.Contains('name=Folder3'))|$($afterOld.Contains('l0.icon=') -and $afterOld.Contains('l0.iconidx=0'))|$valid|$alive" "icon fields kept by a new save and by 0.3.4's save; still valid after reading again with the icon file deleted"
  Quit1Key

  # ---- LA19: slow check (time limit) + delete while checking
  WriteList ("launchv=1`n" + (Item 0 "71000001" "exe" "Target" $tgt $CtrlAlt 0x76) + (Item 1 "71000002" "exe" "Late" $late $CtrlAlt 0x77))
  $env:ONEKEY_TEST_FAIL = "launch:slowlookup"
  Start1Key; Unlock
  Hot 1; Start-Sleep -Milliseconds 800
  $resp = [LA]::Answers($m, 1000)
  Click 4142; Click 4101; Click 4016; Answer 6   # delete "Late" while it is being checked
  Start-Sleep -Milliseconds 17000
  $t0 = Toasts
  Check LA19 "True|0|0|0" "$resp|$((Procs $late).Count)|$(ExecCalls)|$t0" "slow check: window answers, the deleted item runs nothing and shows no result"
  # ---- LA20 (same 1Key): the time limit alone
  Hot 0; Start-Sleep -Milliseconds 17500
  Check LA20 "0|0|True" "$((Procs $tgt).Count)|$(ExecCalls)|$((Toasts) -gt 0)" ("check over the time limit: nothing runs, a warning " + (TT))
  Quit1Key

  # ---- LA21 (renumbered from the plan): slow launch call -> busy
  $env:ONEKEY_TEST_FAIL = "launch:slowrun"
  Start1Key
  Hot 0; Start-Sleep -Milliseconds 900; Hot 0
  Start-Sleep -Milliseconds 300; $busy = (Toasts) -gt 0
  $tt21 = TT
  WaitProc $tgt 6000; Start-Sleep -Milliseconds 3000
  $tt21 += TT
  $resultsA = "$busy|$((Procs $tgt).Count)|$(ExecCalls)"
  Quit1Key; KillTargets
  # rights / compatibility / folder windows unknown
  $env:ONEKEY_TEST_FAIL = "launch:elevation"
  Start1Key; Hot 0; Start-Sleep -Milliseconds 2000
  $e1 = "$((Procs $tgt).Count)|$(ExecCalls)"
  Quit1Key
  WriteList ("launchv=1`n" + (Item 0 "71000001" "exe" "Target" $tgt $CtrlAlt 0x76) + (Item 1 "71000003" "folder" "Folder" $folder $CtrlAlt 0x79))
  $env:ONEKEY_TEST_FAIL = "launch:compat,launch:folderquery"
  Start1Key; Hot 0; Start-Sleep -Milliseconds 2000; Hot 1; Start-Sleep -Milliseconds 2500
  $e2 = "$((Procs $tgt).Count)|$(([LA]::Explorer('LaFolder')).Count)|$(ExecCalls)"
  Quit1Key; KillTargets
  $env:ONEKEY_TEST_FAIL = $null
  Check LA21 "True|1|1|0|0|0|0|0" "$resultsA|$e1|$e2" ("slow launch call: second press busy, one process, one call; rights unknown / compat unreadable / folder windows unknown: nothing opens " + $tt21)

  # ---- LA22: window after 10 s; LA23: no window at all
  WriteList ("launchv=1`n" + (Item 0 "81000001" "exe" "Late" $late $CtrlAlt 0x76) + (Item 1 "81000002" "exe" "NoWin" $nowin $CtrlAlt 0x77))
  $env:ONEKEY_LAUNCH_TARGET_DELAY = "10000"
  Start1Key
  $env:ONEKEY_LAUNCH_TARGET_DELAY = $null
  Hot 0; Start-Sleep -Milliseconds 1500; $tt22 = TT; Start-Sleep -Milliseconds 7500
  Hot 0; Start-Sleep -Milliseconds 1200
  $asked = (Dialog) -ne [IntPtr]::Zero
  Answer 7
  Start-Sleep -Milliseconds 3000   # the window is up now
  Hot 0; Start-Sleep -Milliseconds 1500
  $asked2 = (Dialog) -ne [IntPtr]::Zero; if ($asked2) { Answer 7 }
  $fg = [LA]::GetForegroundWindow(); $fgPid = 0; [void][LA]::GetWindowThreadProcessId($fg, [ref]$fgPid)
  $lp = Procs $late
  Check LA22 "True|False|1|True" "$asked|$asked2|$($lp.Count)|$($lp.Count -eq 1 -and $fgPid -eq $lp[0].Id)" ("window after 10 s: a press after 8 s asks, [No] starts nothing; once up, a press brings it front without asking " + $tt22)
  Quit1Key; KillTargets
  $env:ONEKEY_LAUNCH_TARGET_MODE = "nowindow"
  Start1Key
  $env:ONEKEY_LAUNCH_TARGET_MODE = $null
  Hot 1; WaitProc $nowin 4000; Start-Sleep -Milliseconds 8500
  Hot 1; Start-Sleep -Milliseconds 1200
  $asked = (Dialog) -ne [IntPtr]::Zero
  $n1 = (Procs $nowin).Count
  Answer 6; Start-Sleep -Milliseconds 2500
  Check LA23 "True|1|2" "$asked|$n1|$((Procs $nowin).Count)" "no window: a press after 8 s asks; [Yes] starts a second process"
  Quit1Key; KillTargets

  # ---- LA24: window rules
  WriteList ("launchv=1`n" + (Item 0 "91000001" "exe" "Target" $tgt $CtrlAlt 0x76))
  $env:ONEKEY_TEST_FAIL = "launch:blind"
  Start1Key
  $r = @()
  foreach ($mode in @("transparent", "noactivate")) {
    $null = StartWith $tgt $mode
    Hot 0; Start-Sleep -Milliseconds 1500
    $r += (Procs $tgt).Count
    KillTargets; Start-Sleep -Milliseconds 700
  }
  $z = StartWith $blindZero "zero"
  Hot 0; WaitProc $tgt 4000; Start-Sleep -Milliseconds 800
  $r += (Procs $tgt).Count
  KillTargets; Start-Sleep -Milliseconds 9000   # past the 8 s state of the run above
  $bl = StartWith $blind ""
  Hot 0; Start-Sleep -Milliseconds 1500
  $r += (Procs $tgt).Count
  $null = StartWith $tgt ""
  Hot 0; Start-Sleep -Milliseconds 1500
  $r += (Procs $tgt).Count
  $env:ONEKEY_TEST_FAIL = $null
  Check LA24 "1|1|1|0|1" ($r -join "|") "TRANSPARENT and NOACTIVATE windows reused; zero-size unqueryable window does not block; unqueryable window blocks unless the target's window is found"
  Quit1Key; KillTargets

  # ---- LA25: name switches
  WriteList ("launchv=1`n" + (Item 0 "a1000001" "exe" "Target" $tgt $CtrlAlt 0x76) + (Item 1 "a1000002" "folder" "Folder" $folder $CtrlAlt 0x79))
  Start1Key; Unlock
  function Shapes() { $a = New-Object LA+RECT; [void][LA]::GetWindowRect([LA]::GetDlgItem($m, 4100), [ref]$a); $b = New-Object LA+RECT; [void][LA]::GetWindowRect([LA]::GetDlgItem($m, 4101), [ref]$b); "$(if (($a.B - $a.T) -gt ($a.R - $a.L)) {'N'} else {'I'})$(if (($b.B - $b.T) -gt ($b.R - $b.L)) {'N'} else {'I'})" }
  $seq = @(Shapes)
  foreach ($id in @(2030, 2031, 2030, 2031)) {
    Click 220; Start-Sleep -Milliseconds 300; [LA]::Press($m, $id); Start-Sleep -Milliseconds 300; Click 2012; Start-Sleep -Milliseconds 1500
    if (-not (Has 4100)) { Click 240; Start-Sleep -Milliseconds 600 }
    $seq += Shapes
  }
  Click 220; Start-Sleep -Milliseconds 300; [LA]::Press($m, 2030); Start-Sleep -Milliseconds 300; Click 2012; Start-Sleep -Milliseconds 1500
  Quit1Key
  Start1Key; Unlock
  $seq += Shapes
  Check LA25 "IN|NN|NI|II|IN|NN" ($seq -join "|") "name switches: default, +programs, -folders, -programs, +folders, then programs on survives a restart (N = name shown, I = icon only)"
  Quit1Key

  # ---- LA26: scan limit
  $big = Join-Path $work "StartMenuBig"; New-Item -ItemType Directory -Force $big | Out-Null
  1..2100 | ForEach-Object { [IO.File]::WriteAllBytes((Join-Path $big ("P{0:d4}.lnk" -f $_)), [byte[]]@()) }
  $env:ONEKEY_TEST_STARTMENU = $big
  WriteList ("launchv=1`n" + (Item 0 "b1000001" "exe" "Target" $tgt $CtrlAlt 0x76))
  Start1Key; Unlock
  Click 203; Click 4002
  for ($i = 0; $i -lt 60 -and -not (Has 4300); $i++) { Start-Sleep -Milliseconds 100 }
  Check LA26 "True|True|False|True" "$(Has 4300)|$(Has 4379)|$(Has 4380)|$([LA]::Answers($m, 1000))" "more shortcuts than the scan limit: 80 rows shown, window answers"
  Click 4200; Click 4000
  Quit1Key
  $env:ONEKEY_TEST_STARTMENU = $null

  # ---- LA27: slow icons
  WriteList ("launchv=1`n" + (Item 0 "c1000001" "exe" "Target" $tgt $CtrlAlt 0x76) + (Item 1 "c1000002" "folder" "Folder" $folder $CtrlAlt 0x79) + (Item 2 "c1000003" "exe" "Late" $late))
  $env:ONEKEY_TEST_FAIL = "icon:slow"
  Start1Key; Unlock
  $a1 = [LA]::Answers($m, 1000); Start-Sleep -Milliseconds 1500; $a2 = [LA]::Answers($m, 1000)
  Check LA27 "True|True|True|True" "$a1|$a2|$(Has 4100)|$(Has 4102)" "icons slow: the window answers within 1 s, tiles are there"
  $env:ONEKEY_TEST_FAIL = $null
  Quit1Key
  # ---- LA28: result message late (RW-1)
  WriteList ("launchv=1`n" + (Item 0 "d1000001" "exe" "Slow" $slow $CtrlAlt 0x76))
  $env:ONEKEY_LAUNCH_TARGET_DELAY = "5000"; $env:ONEKEY_TEST_FAIL = "launch:latedone"
  Start1Key
  $env:ONEKEY_LAUNCH_TARGET_DELAY = $null; $env:ONEKEY_TEST_FAIL = $null
  Hot 0; Start-Sleep -Milliseconds 1200; $tt28 = TT
  Hot 0; Start-Sleep -Milliseconds 7000   # the second press falls into the 3 s gap; the window comes up at ~5 s
  $n1 = (Procs $slow).Count; $c1 = ExecCalls
  Hot 0; Start-Sleep -Milliseconds 1500
  $fg = [LA]::GetForegroundWindow(); $fgPid = 0; [void][LA]::GetWindowThreadProcessId($fg, [ref]$fgPid)
  $sp2 = Procs $slow
  Check LA28 "1|1|1|True" "$n1|$c1|$($sp2.Count)|$($sp2.Count -eq 1 -and $fgPid -eq $sp2[0].Id)" ("late result message: second press takes the finished result first - one process, one call, then reuse " + $tt28)
  Quit1Key; KillTargets

  # ---- LA29: folder query step failure (RW-2) and a virtual-folder control
  $other = Join-Path $work "LaOther"; New-Item -ItemType Directory -Force $other | Out-Null
  WriteList ("launchv=1`n" + (Item 0 "e1000001" "folder" "Folder" $folder $CtrlAlt 0x79))
  $before29 = @([LA]::Explorer(""))
  Start-Process explorer.exe $other; Start-Sleep -Milliseconds 2500
  $env:ONEKEY_TEST_FAIL = "launch:folderstep"
  Start1Key; $env:ONEKEY_TEST_FAIL = $null
  Hot 0; Start-Sleep -Milliseconds 3000
  $w1 = ([LA]::Explorer("LaFolder")).Count
  Quit1Key
  foreach ($h in [LA]::Explorer("LaOther")) { [void][LA]::PostMessageW($h, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) }
  Start-Process explorer.exe "shell:MyComputerFolder"; Start-Sleep -Milliseconds 2500
  Start1Key
  Hot 0; Start-Sleep -Milliseconds 3000
  $w2 = ([LA]::Explorer("LaFolder")).Count
  Quit1Key; KillTargets
  foreach ($h in [LA]::Explorer("")) { if ($before29 -notcontains $h) { [void][LA]::PostMessageW($h, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) } }   # close the windows this check opened
  Check LA29 "0|1" "$w1|$w2" "a failing folder-query step opens nothing; with only a virtual folder window open the folder opens"

  # ---- LA30: reset button by error kind, also after a language change
  WriteList ("launchv=2`n" + (Item 0 "f1000001" "exe" "Target" $tgt $CtrlAlt 0x76))
  Start1Key; Unlock
  $r0 = Has 4146
  Click 220; Start-Sleep -Milliseconds 300
  [void][LA]::PostMessageW([LA]::GetDlgItem($m, 2009), 0x0100, [IntPtr]0x28, [IntPtr]::Zero); Start-Sleep -Milliseconds 300   # language: next entry
  Click 2012; Start-Sleep -Milliseconds 1500
  if (-not (Has 4143)) { Click 240; Start-Sleep -Milliseconds 600 }
  $r1 = Has 4146; $note = Has 4143
  Check LA30 "False|False|True" "$r0|$r1|$note" "unknown format: no [Start a new list] before or after a language change (note still shown)"
  Quit1Key
  }
} catch { Add-Failure ("exception: " + $_.Exception.Message) }
finally { Quit1Key; KillTargets; Stop-TestInstances $suffix; $env:ONEKEY_TEST_LOCKWIDGET = "0"; $env:ONEKEY_TEST_FAIL = $null; $env:ONEKEY_LAUNCH_TARGET_DELAY = $null; $env:ONEKEY_LAUNCH_TARGET_MODE = $null; $env:ONEKEY_TEST_STARTMENU = $null }
Complete-Checks
