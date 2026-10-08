# Accessibility of 1Key's own-drawn controls (Codex QA-06, 0.3.11): IAccessible (MSAA) per control, read here exactly as screen
# readers and UI Automation's MSAA proxy read it. Narrator itself is not run (user decision: Narrator check excluded).
# AX01 list + settings screens: every own-drawn control (button, row, tile, hotkey box, slider, dropdown, switch) has a name
# AX02 roles: switch = check button (44), hotkey box = editable text (42), slider = slider (51), dropdown = combo box (46), button = 43
# AX03 switch: the checked state follows the switch, and the default action flips it (same path as Space)
# AX04 button default action = a press: [+ Add] (203) opens the add-kind screen
# AX05 help window (D6): the first section title is "expanded", the others "collapsed"; the default action on the second title
#      expands it (its body appears, the window grows) and the box stays open
# AX06 native UI Automation (IUIAutomation, what Narrator uses - the managed System.Windows.Automation client uses its own Win32 proxies
#      and only sees panes) sees a switch as a CheckBox and the hotkey box as an Edit, both named
# ---- 0.3.12 (Codex 20:13 RW-3)
# AX07 an IAccessible reference kept after its control is destroyed: name / state / default action fail (disconnected) even after a new
#      control was created on the same screen, and the new control's default action works
# AX08 lifetime: 30 settings open/close cycles do not pile up accessibility objects (test count WM_APP+40 back near the start)
# AX09 list row default action opens the item's edit screen (role list item + the real action)
# AX10 help window after expanding a section: Esc still closes the box
# Own config folder and suffix. ASCII only.
param([string]$Exe = "")
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
Start-Checks -Required @("AX01","AX02","AX03","AX04","AX05","AX06","AX07","AX08","AX09","AX10")
$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$suffix = ".ax"
$cfg = Join-Path $sp "a11y_cfg"
$master = "Ax-Dummy-5521"
$cpNu = @'
using System;using System.Runtime.InteropServices;
public class NU {
  [DllImport("ole32.dll")] static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint ctx, ref Guid iid, out IntPtr o);
  [DllImport("oleaut32.dll")] static extern void SysFreeString(IntPtr s);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FromHandle(IntPtr self, IntPtr h, out IntPtr el);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetInt(IntPtr self, out int v);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetPtr(IntPtr self, out IntPtr v);
  static T Fn<T>(IntPtr o, int i) { IntPtr vt = Marshal.ReadIntPtr(o); return (T)(object)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt, i * IntPtr.Size), typeof(T)); }
  // control type id and name through the native UI Automation core (IUIAutomation - what Narrator uses)
  public static string Probe(IntPtr hwnd) {
    Guid clsid = new Guid("ff48dba4-60ef-4201-aa87-54103eef594e"), iid = new Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee");
    IntPtr uia; if (CoCreateInstance(ref clsid, IntPtr.Zero, 1, ref iid, out uia) < 0) return "no uia";
    IntPtr el; int hr = Fn<FromHandle>(uia, 6)(uia, hwnd, out el);
    if (hr < 0 || el == IntPtr.Zero) { Marshal.Release(uia); return "no element"; }
    int ct; Fn<GetInt>(el, 21)(el, out ct);
    IntPtr b; Fn<GetPtr>(el, 23)(el, out b);
    string name = b == IntPtr.Zero ? "" : Marshal.PtrToStringBSTR(b); if (b != IntPtr.Zero) SysFreeString(b);
    Marshal.Release(el); Marshal.Release(uia);
    return ct + "|" + (name.Length > 0);
  }
}
'@
Add-Type -TypeDefinition $cpNu
Add-Type -ReferencedAssemblies Accessibility @'
using System;using System.Runtime.InteropServices;using System.Text;using System.Collections.Generic;using Accessibility;
public class AX {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("oleacc.dll")] static extern int AccessibleObjectFromWindow(IntPtr h, uint id, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object o);
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static IntPtr Find(uint pid, string cls) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
  public static IAccessible Acc(IntPtr h) { Guid g = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71"); object o; return AccessibleObjectFromWindow(h, 0xFFFFFFFC, ref g, out o) >= 0 ? o as IAccessible : null; }
  public static string Name(IntPtr h) { var a = Acc(h); try { return a == null ? null : a.get_accName(0); } catch { return null; } }
  public static int Role(IntPtr h) { var a = Acc(h); try { return a == null ? -1 : Convert.ToInt32(a.get_accRole(0)); } catch { return -1; } }
  public static int State(IntPtr h) { var a = Acc(h); try { return a == null ? -1 : Convert.ToInt32(a.get_accState(0)); } catch { return -1; } }
  public static string Value(IntPtr h) { var a = Acc(h); try { return a == null ? null : a.get_accValue(0); } catch { return null; } }
  public static bool Act(IntPtr h) { var a = Acc(h); try { a.accDoDefaultAction(0); return true; } catch { return false; } }
  // the same calls on an object we already hold
  public static string NameOf(object o) { var a = o as IAccessible; if (a == null) return "NULL"; try { return a.get_accName(0) ?? ""; } catch { return "ERR"; } }
  public static string ActOn(object o) { var a = o as IAccessible; if (a == null) return "NULL"; try { a.accDoDefaultAction(0); return "OK"; } catch { return "ERR"; } }
  public static string StateOf(object o) { var a = o as IAccessible; if (a == null) return "NULL"; try { return Convert.ToString(a.get_accState(0)); } catch { return "ERR"; } }
  public static object AccObj(IntPtr h) { return Acc(h); }
  static readonly string[] Own = { "OneKeyButton", "OneKeyRow", "OneKeyTile", "OneKeyHotkey", "OneKeySlider", "OneKeyDropdown", "OneKeyToggle" };
  // visible own-drawn children without a name: "class:id" list
  public static string Unnamed(IntPtr m) { var bad = new List<string>(); EnumChildWindows(m, (h,l) => { string c = Cls(h); if (Array.IndexOf(Own, c) >= 0 && IsWindowVisible(h)) { string n = Name(h); if (string.IsNullOrEmpty(n)) bad.Add(c + ":" + GetDlgCtrlID(h)); } return true; }, IntPtr.Zero); return string.Join(",", bad); }
  public static int Count(IntPtr m) { int n = 0; EnumChildWindows(m, (h,l) => { if (Array.IndexOf(Own, Cls(h)) >= 0 && IsWindowVisible(h)) n++; return true; }, IntPtr.Zero); return n; }
  [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr h);
}
'@
function Click($id) { [void][AX]::PostMessageW([AX]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 700 }
function SetText($id, $s) { [void][AX]::SendMessageW([AX]::GetDlgItem($m, $id), 0x000C, [IntPtr]::Zero, $s) }
function Has($id) { [AX]::GetDlgItem($m, $id) -ne [IntPtr]::Zero }
try {
  Stop-TestInstances $suffix
  if (Test-Path $cfg) { Remove-Item $cfg -Recurse -Force }; New-Item -ItemType Directory -Force $cfg | Out-Null
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_WORKAREA = $TallScreen
  $script:p = Start-Process $exe -PassThru; $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80 -and $m -eq [IntPtr]::Zero; $i++) { $script:m = [AX]::Find([uint32]$p.Id, "OneKeyMainWindow$suffix"); Start-Sleep -Milliseconds 100 }
  if ($m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 800
  SetText 101 $master; SetText 102 $master; Click 103; Start-Sleep -Milliseconds 600
  Click 203; Click 4001; SetText 301 "Ax one"; SetText 302 "ax-dummy-1"; Click 310; Start-Sleep -Milliseconds 600
  $bad1 = [AX]::Unnamed($m); $n1 = [AX]::Count($m)
  Click 220; Start-Sleep -Milliseconds 500
  $bad2 = [AX]::Unnamed($m); $n2 = [AX]::Count($m)
  Check AX01 "True||" "$($n1 -gt 3 -and $n2 -gt 8)|$bad1|$bad2" "list ($n1 controls) and settings ($n2): every own-drawn control has a name (unnamed listed)"

  $tg = [AX]::GetDlgItem($m, 2002); $hk = [AX]::GetDlgItem($m, 2006); $sl = [AX]::GetDlgItem($m, 2005); $dd = [AX]::GetDlgItem($m, 2008); $bt = [AX]::GetDlgItem($m, 2012)
  Check AX02 "44|42|51|46|43" "$([AX]::Role($tg))|$([AX]::Role($hk))|$([AX]::Role($sl))|$([AX]::Role($dd))|$([AX]::Role($bt))" "roles: switch, hotkey box (editable text), slider, combo box, push button"

  $c0 = [int][AX]::SendMessageW($tg, 0x00F0, [IntPtr]::Zero, [IntPtr]::Zero); $s0 = ([AX]::State($tg) -band 0x10) -ne 0
  $acted = [AX]::Act($tg); Start-Sleep -Milliseconds 500
  $c1 = [int][AX]::SendMessageW($tg, 0x00F0, [IntPtr]::Zero, [IntPtr]::Zero); $s1 = ([AX]::State($tg) -band 0x10) -ne 0
  Check AX03 "True|True|True|True" "$($s0 -eq ($c0 -eq 1))|$acted|$($c1 -ne $c0)|$($s1 -eq ($c1 -eq 1))" "switch: checked state follows it, default action flips it"
  [void][AX]::Act($tg); Start-Sleep -Milliseconds 300   # back as it was

  Check AX06 "50002|True|50004|True" "$([NU]::Probe($tg))|$([NU]::Probe($hk))" "native UI Automation: switch = CheckBox (50002), hotkey box = Edit (50004), both named"

  Click 241; Start-Sleep -Milliseconds 500   # settings: cancel back to the list
  if (-not (Has 203)) { Click 240; Start-Sleep -Milliseconds 500 }
  $actAdd = [AX]::Act([AX]::GetDlgItem($m, 203)); Start-Sleep -Milliseconds 700
  Check AX04 "True|True" "$actAdd|$(Has 4001)" "[+ Add] default action opens the add-kind screen"
  Click 4000; Start-Sleep -Milliseconds 400

  # help window on the edit screen (three sections)
  Click 1000; Start-Sleep -Milliseconds 500
  [void][AX]::PostMessageW([AX]::GetDlgItem($m, 250), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 900
  $b = [AX]::Find([uint32]$p.Id, "OneKeyDialog")
  $h0 = [AX]::GetDlgItem($b, 110); $h1 = [AX]::GetDlgItem($b, 111)
  $st0 = [AX]::State($h0); $st1 = [AX]::State($h1)
  $r0 = New-Object AX+RECT; [void][AX]::GetWindowRect($b, [ref]$r0)
  $before = [AX]::Count($b)
  [void][AX]::Act($h1); Start-Sleep -Milliseconds 700
  $st1b = [AX]::State($h1); $r1 = New-Object AX+RECT; [void][AX]::GetWindowRect($b, [ref]$r1)
  $open = [AX]::Find([uint32]$p.Id, "OneKeyDialog") -ne [IntPtr]::Zero
  Check AX05 "True|True|True|True|True" "$(($st0 -band 0x200) -ne 0)|$(($st1 -band 0x400) -ne 0)|$(($st1b -band 0x200) -ne 0)|$(($r1.B - $r1.T) -gt ($r0.B - $r0.T))|$open" "help: first section expanded, second collapsed; its default action expands it, the window grows, the box stays open"
  if ($b -ne [IntPtr]::Zero) { [void][AX]::PostMessageW($b, 0x0111, [IntPtr]1, [IntPtr]::Zero); Start-Sleep -Milliseconds 500 }
  # ---- AX07..AX10 (RW-3 and action checks) - back to the list first (AX05 left the edit screen open)
  Click 311; Start-Sleep -Milliseconds 500; if (-not (Has 203)) { Click 309; Start-Sleep -Milliseconds 500 }
  Click 220; Start-Sleep -Milliseconds 500
  $oldBtn = [AX]::GetDlgItem($m, 2012)
  $old = [AX]::AccObj($oldBtn); $oldName = [AX]::NameOf($old)
  Click 241; Start-Sleep -Milliseconds 500; if (-not (Has 203)) { Click 240; Start-Sleep -Milliseconds 500 }   # settings closed: its controls are gone
  Click 220; Start-Sleep -Milliseconds 500                                                                     # a new settings screen
  $n7 = [AX]::NameOf($old); $s7 = [AX]::StateOf($old); $a7 = [AX]::ActOn($old); Start-Sleep -Milliseconds 400
  $stillSettings = Has 2012
  $newName = [AX]::Name([AX]::GetDlgItem($m, 2012))
  Check AX07 "True|ERR|ERR|ERR|True|True" "$($oldName.Length -gt 0)|$n7|$s7|$a7|$stillSettings|$($newName.Length -gt 0)" "old reference after its control was destroyed: name/state/action disconnected, nothing pressed on the new screen, new control fine"
  $old = $null; [GC]::Collect(); [GC]::WaitForPendingFinalizers()
  Click 241; Start-Sleep -Milliseconds 500; if (-not (Has 203)) { Click 240; Start-Sleep -Milliseconds 500 }
  $live0 = [int][AX]::SendMessageW($m, 0x8000 + 40, [IntPtr]::Zero, [IntPtr]::Zero)
  for ($k = 0; $k -lt 30; $k++) {
    Click 220; [void][AX]::Name([AX]::GetDlgItem($m, 2002)); [void][AX]::Name([AX]::GetDlgItem($m, 2012))
    Click 241; if (-not (Has 203)) { Click 240 }
  }
  [GC]::Collect(); [GC]::WaitForPendingFinalizers(); [GC]::Collect(); Start-Sleep -Milliseconds 1500
  $live1 = [int][AX]::SendMessageW($m, 0x8000 + 40, [IntPtr]::Zero, [IntPtr]::Zero)
  Check AX08 "True" "$($live1 -le $live0 + 6)" "30 settings cycles: live accessibility objects $live0 -> $live1 (no pile-up)"
  $row = [AX]::GetDlgItem($m, 1000)
  $role9 = [AX]::Role($row)   # before the action: the action replaces the screen (the row goes away)
  $act9 = [AX]::Act($row); Start-Sleep -Milliseconds 700
  Check AX09 "34|True|True" "$role9|$act9|$(Has 301)" "list row: role list item, default action opens the edit screen"
  [void][AX]::PostMessageW([AX]::GetDlgItem($m, 250), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 900
  $b = [AX]::Find([uint32]$p.Id, "OneKeyDialog")
  [void][AX]::Act([AX]::GetDlgItem($b, 111)); Start-Sleep -Milliseconds 600
  [void][AX]::PostMessageW([AX]::GetDlgItem($b, 111), 0x0100, [IntPtr]0x1B, [IntPtr]::Zero); Start-Sleep -Milliseconds 600   # Esc
  Check AX10 "False" "$([AX]::Find([uint32]$p.Id, 'OneKeyDialog') -ne [IntPtr]::Zero)" "help: after expanding a section, Esc closes the box"
} catch { Add-Failure ("exception: " + $_.Exception.Message) }
finally { $env:ONEKEY_TEST_WORKAREA = $null; if ($p -and -not $p.HasExited) { [void][AX]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 800; if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force } }; Stop-TestInstances $suffix }
Complete-Checks
