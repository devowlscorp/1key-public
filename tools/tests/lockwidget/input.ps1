param([string]$Exe = "$PSScriptRoot\..\..\..\src\OneKey\bin\Release\net8.0-windows\win-x64\1Key.exe")
# Lock widget input checks: typing, Backspace, Ctrl+A replace, paste, Enter (demo shows only the length), create-mode Tab.
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices; using System.Collections.Generic;
public class T3 { public delegate bool CB(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] static extern bool EnumWindows(CB cb, IntPtr l);
 [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, CB cb, IntPtr l);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
 [DllImport("user32.dll")] static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
 public static int GetWindowTextLengthW(IntPtr h){ return (int)SendMessageW(h, 0x00C1, IntPtr.Zero, IntPtr.Zero); }  // EM_LINELENGTH: a password EDIT hides its text from other processes
 [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr h);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
 [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
 [StructLayout(LayoutKind.Sequential)] public struct GTI { public int cbSize, flags; public IntPtr a, focus, c, d, e, f; public int l, t, r, b; }
 [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint tid, ref GTI g);
 public static IntPtr Focus(IntPtr w){ uint pid; uint tid=GetWindowThreadProcessId(w,out pid); var g=new GTI(); g.cbSize=Marshal.SizeOf(typeof(GTI)); return GetGUIThreadInfo(tid,ref g)?g.focus:IntPtr.Zero; }
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 public static uint DemoPid;
 public static void Guard(){ uint pid; GetWindowThreadProcessId(GetForegroundWindow(), out pid); if (DemoPid == 0 || pid != DemoPid) throw new Exception("foreground is not the demo widget - aborting so no key reaches another program"); }
 public static void Key(byte vk){ Guard(); keybd_event(vk,0,0,UIntPtr.Zero); keybd_event(vk,0,2,UIntPtr.Zero); System.Threading.Thread.Sleep(40); }
 public static void ChordExt(byte m, byte vk){ Guard(); keybd_event(m,0,0,UIntPtr.Zero); keybd_event(vk,0,1,UIntPtr.Zero); keybd_event(vk,0,3,UIntPtr.Zero); keybd_event(m,0,2,UIntPtr.Zero); System.Threading.Thread.Sleep(40); }
 public static void Chord(byte m, byte vk){ Guard(); keybd_event(m,0,0,UIntPtr.Zero); keybd_event(vk,0,0,UIntPtr.Zero); keybd_event(vk,0,2,UIntPtr.Zero); keybd_event(m,0,2,UIntPtr.Zero); System.Threading.Thread.Sleep(40); }
 public static List<IntPtr> All(uint pid, string cls){ var r=new List<IntPtr>(); EnumWindows((h,l)=>{uint p;GetWindowThreadProcessId(h,out p); var sb=new StringBuilder(64); GetClassNameW(h,sb,64); if(p==pid && sb.ToString()==cls) r.Add(h); return true;},IntPtr.Zero); return r; }
 public static IntPtr Child(IntPtr p){ IntPtr found=IntPtr.Zero; EnumChildWindows(p,(h,l)=>{var sb=new StringBuilder(64); GetClassNameW(h,sb,64); if(sb.ToString()=="Edit"){found=h; return false;} return true;},IntPtr.Zero); return found; } }
'@
$ErrorActionPreference = "Stop"
$res = @()
function Check($n, $ok, $i) { $script:res += "{0} {1}  {2}" -f $(if ($ok) { "ok  " } else { "FAIL" }), $n, $i }
function Open-Demo([string]$mode) {
  $env:ONEKEY_TEST = "1"
  $a = @("--lockwidget-demo"); if ($mode) { $a += $mode }
  $script:p = Start-Process -FilePath $Exe -ArgumentList $a -PassThru; Start-Sleep -Milliseconds 1500
  if (-not $script:p -or $script:p.HasExited) { throw "demo did not start" }
  [T3]::DemoPid = [uint32]$script:p.Id
  $script:w = @([T3]::All([uint32]$p.Id, "OneKeyLockWidget"))[0]
  if (-not $script:w) { throw "no demo widget window" }
  $script:edits = @([T3]::All([uint32]$p.Id, "OneKeyLockInput") | ForEach-Object { [T3]::Child($_) })
  [T3]::keybd_event(0x12,0,0,[UIntPtr]::Zero); [T3]::keybd_event(0x12,0,2,[UIntPtr]::Zero); [void][T3]::SetForegroundWindow($w); Start-Sleep -Milliseconds 500
}
function Close-Demo() { [void][T3]::PostMessageW($w, 0x0112, [IntPtr]0xF060, [IntPtr]::Zero); Start-Sleep -Milliseconds 700; if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force } }

try {
Open-Demo ""
$e = $edits[0]
Check "focus starts in the password field (id 101)" ([T3]::Focus($w) -eq $e -and [T3]::GetDlgCtrlID($e) -eq 101) "focus=$([T3]::Focus($w)) edit=$e"
foreach ($k in 0x41,0x42,0x43,0x44) { [T3]::Key($k) }; Start-Sleep -Milliseconds 200
$l1 = [T3]::GetWindowTextLengthW($e)
[T3]::Key(0x08); $l2 = [T3]::GetWindowTextLengthW($e)
[T3]::Chord(0x11, 0x41); [T3]::Key(0x5A); $l3 = [T3]::GetWindowTextLengthW($e)
foreach ($k in 0x31,0x32,0x33,0x34,0x35,0x36,0x37,0x38) { [T3]::Key($k) }; $l4 = [T3]::GetWindowTextLengthW($e)   # (no clipboard: paste is not tested)
foreach ($i in 1..60) { [T3]::Key(0x58) }; $l5 = [T3]::GetWindowTextLengthW($e)
Check "typing / Backspace / Ctrl+A replace / more typing / long input" ($l1 -eq 4 -and $l2 -eq 3 -and $l3 -eq 1 -and $l4 -eq 9 -and $l5 -eq 69) "lengths $l1 $l2 $l3 $l4 $l5"
[T3]::Key(0x24); [T3]::Key(0x2E); $m1 = [T3]::GetWindowTextLengthW($e)            # Home, Delete
[T3]::Key(0x23); [T3]::Key(0x08); $m2 = [T3]::GetWindowTextLengthW($e)            # End, Backspace
foreach ($i in 1..5) { [T3]::ChordExt(0x10, 0x25) }; [T3]::Key(0x51); $m3 = [T3]::GetWindowTextLengthW($e)   # Shift+Left x5, type -> replaces 5
Check "Home+Delete / End+Backspace / Shift-select replace" ($m1 -eq 68 -and $m2 -eq 67 -and $m3 -eq 63) "lengths $m1 $m2 $m3"
$t0 = @([T3]::All([uint32]$p.Id, "OneKeyToast")).Count
[T3]::Key(0x0D); Start-Sleep -Milliseconds 500
$t1 = @([T3]::All([uint32]$p.Id, "OneKeyToast")).Count
Check "Enter submits (demo shows the length only)" ($t1 -gt $t0) "toasts $t0 -> $t1"
Close-Demo

Open-Demo "create"
Check "create: two fields 101 / 102" ($edits.Count -eq 2 -and [T3]::GetDlgCtrlID($edits[0]) -eq 101 -and [T3]::GetDlgCtrlID($edits[1]) -eq 102) "count $($edits.Count)"
[T3]::Key(0x41); [T3]::Key(0x09); $f2 = [T3]::Focus($w); [T3]::Key(0x42); [T3]::Chord(0x10, 0x09); $f1 = [T3]::Focus($w)
Check "create: Tab / Shift+Tab move between fields" ($f2 -eq $edits[1] -and $f1 -eq $edits[0] -and [T3]::GetWindowTextLengthW($edits[0]) -eq 1 -and [T3]::GetWindowTextLengthW($edits[1]) -eq 1) "f2 ok=$($f2 -eq $edits[1]) f1 ok=$($f1 -eq $edits[0])"
$t0 = @([T3]::All([uint32]$p.Id, "OneKeyToast")).Count
[T3]::Key(0x0D); Start-Sleep -Milliseconds 400; $fAfter = [T3]::Focus($w); $t1 = @([T3]::All([uint32]$p.Id, "OneKeyToast")).Count
Check "create: Enter in field 1 moves to field 2 (no submit)" ($fAfter -eq $edits[1] -and $t1 -eq $t0) "focus field2=$($fAfter -eq $edits[1]) toasts $t0->$t1"
Close-Demo
} catch { "ABORTED: $($_.Exception.Message)"; if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force } }
$res
# same record as tools\tests\lib\Check.ps1 (Codex 2026-10-04 06:41): which exe, and a judgement line + exit code
if (Test-Path $Exe) { "---- exe: $Exe  version " + (Get-Item $Exe).VersionInfo.FileVersion + "  SHA-256 " + (Get-FileHash $Exe -Algorithm SHA256).Hash }
$nFail = @($res | Where-Object { "$_" -like "FAIL*" }).Count; $nAll = @($res).Count
"---- judgement: $($nAll - $nFail)/$nAll checks passed, $nFail failed"
if ($nFail -gt 0 -or $nAll -eq 0) { exit 1 } else { exit 0 }
