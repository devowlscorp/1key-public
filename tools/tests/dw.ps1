# DirectWrite text (stage 2a, 0.2.53; design v2/v3 agreed with Codex 2026-09-30).
# DW01 renderer state from the test-mode message WM_APP+14: DirectWrite on, the embedded Pretendard family found,
#      400 and 600 are the exact faces (not simulated). Natural measuring and ClearType are fixed in the code (bits 32/64).
# DW02 ONEKEY_TEST_FAIL=dw:init: starts with GDI (off) and still works: create the master, add an item, open settings.
# DW03 dw:recreate-once: one EndDraw asks for a new target; the next frames succeed, DirectWrite stays on, 0 failed frames.
# DW04 dw:recreate-always: the target is created but every EndDraw fails -> after 3 failed frames it switches to GDI
#      (failed bit), once, and the app keeps working (screens change, text is read back).
# DW05-DW09 the state-keeping relayout (WM_APP+15, the same code the switch uses): settings scrolled at a simulated 150 %
#      keeps its scroll; edit screen keeps the focused name field and its selection; the shown password stays shown; with
#      the help window open the relayout does not take the focus from it; after a lock nothing typed comes back.
# DW10 lock screen F1: the "1Key info" box with an extra [license] button (id 3); pressing it opens the OFL text in a
#      scrollable read-only EDIT (id 101) containing the licence title.
# DW11 list help also has the [license] button.
# Own config folder and test suffix; the real 1Key is not touched. Boxes can take the focus: run when nobody uses the PC.
# Judgement: tools\tests\lib\Check.ps1. ASCII only.
param([string]$Exe = "")
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
$req = @(); for ($i = 1; $i -le 11; $i++) { $req += ("DW{0:D2}" -f $i) }
Start-Checks -Required $req

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$suffix = ".dw"
$cfg = "$sp\dw_cfg"
Add-Type @'
using System;using System.Collections.Generic;using System.Runtime.InteropServices;using System.Text;
public class DU {
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
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint f);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int l, t, r, b; }
  [StructLayout(LayoutKind.Sequential)] public struct GTI { public int cb; public int flags; public IntPtr active, focus, capture, menu, move, caret; public RECT rc; }
  [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint tid, ref GTI g);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static List<IntPtr> All(uint pid, string cls) { var f = new List<IntPtr>(); EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls && IsWindowVisible(h)) f.Add(h); return true; }, IntPtr.Zero); return f; }
  public static IntPtr Focus(IntPtr w) { uint pid; uint tid = GetWindowThreadProcessId(w, out pid); var g = new GTI(); g.cb = Marshal.SizeOf(typeof(GTI)); return GetGUIThreadInfo(tid, ref g) ? g.focus : IntPtr.Zero; }
  public static string Text(IntPtr h) { var sb = new StringBuilder(8192); SendMessageW(h, 0x000D, (IntPtr)8192, sb); return sb.ToString(); }
  public static int Top(IntPtr h) { RECT r; GetWindowRect(h, out r); return r.t; }
  public static string Sel(IntPtr h) { int a = 0, b = 0; IntPtr pa = Marshal.AllocHGlobal(8); try { SendMessageW(h, 0x00B0, pa, pa + 4); a = Marshal.ReadInt32(pa); b = Marshal.ReadInt32(pa + 4); } finally { Marshal.FreeHGlobal(pa); } return a + "-" + b; }
}
'@
function SetText($id, $s) { [void][DU]::SendMessageW([DU]::GetDlgItem($m, $id), 0x000C, [IntPtr]::Zero, [string]$s) }
function Click($id, $ms = 600) { [void][DU]::PostMessageW([DU]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds $ms }
function Key($id, $vk) { [void][DU]::PostMessageW([DU]::GetDlgItem($m, $id), 0x0100, [IntPtr]$vk, [IntPtr]::Zero); Start-Sleep -Milliseconds 400 }
function Has($id) { [DU]::GetDlgItem($m, $id) -ne [IntPtr]::Zero }
function Boxes() { @([DU]::All([uint32]$p.Id, "OneKeyDialog")) }
function Box() { for ($i=0;$i -lt 15;$i++) { $b = @(Boxes); if ($b.Count -gt 0) { return $b[0] }; Start-Sleep -Milliseconds 100 }; [IntPtr]::Zero }
function Answer($b, $cmd) { if ($b -ne [IntPtr]::Zero) { [void][DU]::PostMessageW($b, 0x0111, [IntPtr]$cmd, [IntPtr]::Zero); Start-Sleep -Milliseconds 600 } }
function Status() { $v = [int64][DU]::SendMessageW($m, 0x800E, [IntPtr]::Zero, [IntPtr]::Zero); if ((($v -shr 60) -band 1) -ne 1) { "no answer" } else { "on=$($v -band 1) failed=$(($v -shr 1) -band 1) family=$(($v -shr 2) -band 1) 400=$(($v -shr 3) -band 1) 600=$(($v -shr 4) -band 1) frames=$(($v -shr 8) -band 0xFF)" } }
function Relayout() { [void][DU]::PostMessageW($m, 0x800F, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 800 }
function Lock() { [void][DU]::PostMessageW($m, 0x02B1, [IntPtr]7, [IntPtr]::Zero); Start-Sleep -Milliseconds 700 }
function Launch() {
  $script:p = Start-Process $exe -PassThru
  $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $w = @([DU]::All([uint32]$script:p.Id, "OneKeyMainWindow$suffix")); if ($w.Count -gt 0) { $script:m = $w[0]; break }; Start-Sleep -Milliseconds 100 }
  if ($script:m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 700
}
function Quit() { [void][DU]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i=0; $i -lt 50 -and -not $p.HasExited; $i++) { Start-Sleep -Milliseconds 100 } }
# fresh config, master, auto-lock off, one item (the whole setup goes through the screens, so it also exercises the renderer)
function Fresh([string]$fail) {
  if (Test-Path $cfg) { Get-ChildItem $cfg -Recurse | Remove-Item -Force -Recurse -ErrorAction Ignore }; New-Item -ItemType Directory -Force $cfg | Out-Null
  $env:ONEKEY_TEST_FAIL = $fail
  Launch
  SetText 101 "Master1234"; SetText 102 "Master1234"; Click 103; Answer (Box) 1
  Click 220; Key 2005 0x24; Click 2012 3500
  Confirm-AutoLockOff $cfg
  Click 203; Click 4001; SetText 301 "DwItem"; SetText 302 "dummy-dw"; Click 310 900; Answer (Box) 1
}

try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_WORKAREA = $TallScreen; $env:ONEKEY_TEST_THEME = $null

  # ---- DW01 normal
  Fresh $null
  Check DW01 "on=1 failed=0 family=1 400=1 600=1 frames=0" (Status) "DirectWrite on with the embedded Pretendard: family found, 400 and 600 exact faces, no failed frames"
  # ---- DW10/11 licence (lock screen F1, list help)
  Click 250; $b = Box
  $extra = if ($b -ne [IntPtr]::Zero) { [DU]::GetDlgItem($b, 3) -ne [IntPtr]::Zero } else { $false }
  Answer $b 2
  Check DW11 $true $extra "list help has the [license] button (id 3)"
  Lock
  Key 101 0x70
  $b = Box
  $hasExtra = $b -ne [IntPtr]::Zero -and [DU]::GetDlgItem($b, 3) -ne [IntPtr]::Zero
  Answer $b 3
  $lb = Box
  $body = if ($lb -ne [IntPtr]::Zero) { [DU]::GetDlgItem($lb, 101) } else { [IntPtr]::Zero }
  $bodyCls = if ($body -ne [IntPtr]::Zero) { [DU]::Cls($body) } else { "none" }
  $bodyTxt = if ($body -ne [IntPtr]::Zero) { [DU]::Text($body) } else { "" }
  Check DW10 "True|Edit|True|True" ("$hasExtra|$bodyCls|$($bodyTxt.Contains('SIL OPEN FONT LICENSE'))|$($bodyTxt.Contains('Reserved Font Name Pretendard'))") "lock screen F1: info box with [license]; it opens the full OFL text (copyright line included) in a scrollable read-only EDIT"
  Answer $lb 1
  SetText 101 "Master1234"; Click 103 900

  # ---- DW05..DW09 state-keeping relayout (normal renderer)
  Quit
  $env:ONEKEY_TEST_WORKAREA = "1366,768,144"
  Launch; SetText 101 "Master1234"; Click 103 900
  Click 220
  [void][DU]::PostMessageW($m, 0x020A, [IntPtr](-120 -shl 16), [IntPtr]::Zero); Start-Sleep -Milliseconds 300   # wheel down
  [void][DU]::PostMessageW($m, 0x020A, [IntPtr](-120 -shl 16), [IntPtr]::Zero); Start-Sleep -Milliseconds 300
  $t0 = [DU]::Top([DU]::GetDlgItem($m, 2002))
  Relayout
  $t1 = [DU]::Top([DU]::GetDlgItem($m, 2002))
  Check DW05 $true ($t0 -eq $t1 -and $t0 -lt 400) "settings scrolled at 150 %: the relayout keeps the scroll position ($t0 -> $t1)"
  Click 241
  Click 1000
  $name = [DU]::GetDlgItem($m, 301)
  [void][DU]::SendMessageW($name, 0x00B1, [IntPtr]2, [IntPtr]4)
  $f0 = [DU]::Focus($m) -eq $name
  Click 303 400   # show the password
  $pw0 = [int][DU]::SendMessageW([DU]::GetDlgItem($m, 302), 0x00D2, [IntPtr]::Zero, [IntPtr]::Zero)
  [void][DU]::SendMessageW($name, 0x00B1, [IntPtr]2, [IntPtr]4)
  [void][DU]::PostMessageW($m, 0x0028, $name, [IntPtr]1); Start-Sleep -Milliseconds 300   # WM_NEXTDLGCTL to the name field
  Relayout
  $name2 = [DU]::GetDlgItem($m, 301)
  Check DW06 "True|2-4" ("$([DU]::Focus($m) -eq $name2)|$([DU]::Sel($name2))") "edit screen: the relayout keeps the focus on the name field and its selection"
  $pw1 = [int][DU]::SendMessageW([DU]::GetDlgItem($m, 302), 0x00D2, [IntPtr]::Zero, [IntPtr]::Zero)
  Check DW07 "0|0" "$pw0|$pw1" "edit screen: the shown password stays shown (no password char before and after)"
  Click 250; $hb = Box
  Relayout
  $fb = [DU]::Focus($m)
  Check DW08 $true ($hb -ne [IntPtr]::Zero -and [DU]::GetAncestor($fb, 2) -eq $hb) "with the help window open the relayout does not take the focus from it"
  Answer $hb 2
  SetText 301 "Draft-Not-Saved"
  Lock
  Relayout
  Check DW09 "True|" ("$(Has 101)|" + [DU]::Text([DU]::GetDlgItem($m, 101))) "after a lock the relayout brings nothing typed back (lock field empty)"
  Quit
  $env:ONEKEY_TEST_WORKAREA = $TallScreen

  # ---- DW02 init failure -> GDI from the start
  Fresh "dw:init"
  Click 220 600; Click 241 600
  Check DW02 "on=0 failed=0 family=0 400=0 600=0 frames=0|True" ((Status) + "|" + (Has 1000)) "dw:init: starts with GDI and works (master, item, settings)"
  Quit
  # ---- DW03 one recreate -> stays on
  Fresh "dw:recreate-once"
  Click 220 600; Click 241 600
  Check DW03 "on=1 failed=0 frames=0" ((Status) -replace ' family=1 400=1 600=1', '') "dw:recreate-once: one failed frame, then it draws again and stays on"
  Quit
  # ---- DW04 always failing EndDraw -> switches to GDI once
  Fresh "dw:recreate-always"
  Click 220 600; Click 241 600; Click 1000 600; Click 311 600
  $st = Status
  Check DW04 "on=0 failed=1|True|True" (($st -replace ' family=1 400=1 600=1 frames=\d+', '') + "|" + (Has 1000) + "|" + (-not $p.HasExited)) "dw:recreate-always: switches to GDI after 3 failed frames and keeps working ($st)"
  Quit
} catch { Add-Failure ("exception: " + $_) }
finally { $env:ONEKEY_TEST_WORKAREA = $null; $env:ONEKEY_TEST_FAIL = $null; Stop-TestInstances $suffix }
Complete-Checks
