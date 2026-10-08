# Confirmation dialog accessibility and keyboard behaviour (T6/T16), using the exit confirmation (Yes/No, default No).
#  - title and body are real controls (STATIC; a read-only EDIT when the body must scroll) so screen readers can read them
#  - first focus is the default button (No); Tab moves between the buttons; Enter/Esc/Alt+F4 close as No
#  - the main window is disabled while the dialog is up; after closing, the focus returns to the previous control
#  - on a very short screen at 150 % (simulated) a long message becomes a scrolling body and the dialog stays inside the work area
#  - Space on "Yes" really exits
#  - (0.3.7 boundary case) a config folder with a very long path and no spaces (one unbreakable line in the message): the dialog
#    stays inside a 1366 px wide work area, the body is not wider than the dialog, and the whole path is in the body (DL15, DL16)
# Judgement: tools\tests\lib\Check.ps1 (T18). ASCII only.
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
Start-Checks -Required @('DL01','DL02','DL03','DL04','DL05','DL06','DL07','DL08','DL09','DL10','DL11','DL12','DL13','DL14','DL15','DL16')

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($args.Count -gt 0) { $args[0] } else { Get-DefaultExe }
$suffix = ".dl"
$cfg = "$sp\dialog_cfg"; if (Test-Path $cfg) { Get-ChildItem $cfg -Recurse | Remove-Item -Force -Recurse -ErrorAction Ignore }; New-Item -ItemType Directory -Force $cfg | Out-Null
Add-Type @'
using System;using System.Runtime.InteropServices;using System.Text;
public class U {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, StringBuilder l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int hh, uint f);
  [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint tid, ref GUITHREADINFO g);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  [StructLayout(LayoutKind.Sequential)] public struct GUITHREADINFO { public int cbSize; public int flags; public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret; public RECT rcCaret; }
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static string Text(IntPtr h) { var t = new StringBuilder(4096); SendMessageW(h, 0x000D, (IntPtr)4096, t); return t.ToString(); }
  public static IntPtr FindCls(uint pid, string cls) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls && IsWindowVisible(h)) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
  public static IntPtr Focus(IntPtr w) { uint pid; uint tid = GetWindowThreadProcessId(w, out pid); var g = new GUITHREADINFO(); g.cbSize = Marshal.SizeOf(g); return GetGUIThreadInfo(tid, ref g) ? g.hwndFocus : IntPtr.Zero; }
}
'@
function SetText($id, $s) { [void][U]::SendMessageW([U]::GetDlgItem($m, $id), 0x000C, [IntPtr]::Zero, [string]$s) }
function Click($id) { [void][U]::PostMessageW([U]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 600 }
function Dlg() { for ($i = 0; $i -lt 30; $i++) { $d = [U]::FindCls([uint32]$p.Id, "OneKeyDialog"); if ($d -ne [IntPtr]::Zero) { return $d }; Start-Sleep -Milliseconds 100 }; [IntPtr]::Zero }
function FocusId() { $f = [U]::Focus($m); if ($f -eq [IntPtr]::Zero) { 0 } else { [U]::GetDlgCtrlID($f) } }
function Key($vk, $sys = $false) { $f = [U]::Focus($m); $msg = if ($sys) { 0x0104 } else { 0x0100 }; $lp = if ($sys) { 0x203E0001 } else { 0x00000001 }; [void][U]::PostMessageW($f, $msg, [IntPtr]$vk, [IntPtr]$lp); Start-Sleep -Milliseconds 400 }
function OpenExit() { [void][U]::PostMessageW([U]::GetDlgItem($m, 2015), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Dlg }
function Launch() {
  $script:p = Start-Process $exe -PassThru
  $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $script:m = [U]::FindCls([uint32]$script:p.Id, "OneKeyMainWindow$suffix"); if ($script:m -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 100 }
  if ($script:m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 700
}

try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_WORKAREA = $null
  Launch
  [void][U]::SetWindowPos($m, [IntPtr]::Zero, 1400, 60, 0, 0, 0x0001 -bor 0x0004 -bor 0x0010)
  SetText 101 "Master1234"; SetText 102 "Master1234"; Click 103
  $before = FocusId

  $d = OpenExit
  Check DL01 "1Key" ([U]::Text([U]::GetDlgItem($d, 100))) "title is a control with the title text"
  $body = [U]::GetDlgItem($d, 101)
  Check DL02 "Static|True" ([U]::Cls($body) + "|" + ([U]::Text($body).Length -gt 20)) "body is a STATIC control carrying the message"
  Check DL03 7 (FocusId) "first focus is the default button 'No' (7)"
  Check DL04 $false ([U]::IsWindowEnabled($m)) "main window is disabled while the dialog is up"
  Key 0x09
  Check DL05 6 (FocusId) "Tab moves to 'Yes' (6)"
  Key 0x09
  Check DL06 7 (FocusId) "Tab wraps back to 'No'"
  Key 0x0D
  Check DL07 $true ((-not $p.HasExited) -and ([U]::FindCls([uint32]$p.Id, "OneKeyDialog") -eq [IntPtr]::Zero) -and [U]::IsWindowEnabled($m)) "Enter on 'No' closes without exiting; main window enabled again"
  Check DL08 $before (FocusId) "focus returns to the control that had it before ($before)"

  $d = OpenExit; Key 0x1B
  Check DL09 $true ((-not $p.HasExited) -and ([U]::FindCls([uint32]$p.Id, "OneKeyDialog") -eq [IntPtr]::Zero)) "Esc closes as 'No'"
  $d = OpenExit; Key 0x73 $true
  Check DL10 $true ((-not $p.HasExited) -and ([U]::FindCls([uint32]$p.Id, "OneKeyDialog") -eq [IntPtr]::Zero)) "Alt+F4 closes as 'No'"

  $d = OpenExit; Key 0x09
  Check DL11 6 (FocusId) "Tab to 'Yes'"
  Key 0x20
  for ($i = 0; $i -lt 30 -and -not $p.HasExited; $i++) { Start-Sleep -Milliseconds 100 }
  Check DL12 $true $p.HasExited "Space on 'Yes' exits"

  # long message on a very short simulated screen at 150 %: the unreadable-file warning appears at start-up
  [IO.File]::WriteAllBytes("$cfg\config.dat", [byte[]](1..64))
  $env:ONEKEY_TEST_WORKAREA = "1366,420,144"
  $p = Start-Process $exe -PassThru
  $d = [IntPtr]::Zero; for ($i = 0; $i -lt 50 -and $d -eq [IntPtr]::Zero; $i++) { $d = [U]::FindCls([uint32]$p.Id, "OneKeyDialog"); Start-Sleep -Milliseconds 100 }
  $r = New-Object 'U+RECT'; if ($d -ne [IntPtr]::Zero) { [void][U]::GetWindowRect($d, [ref]$r) }
  $workB = 420 - [int][Math]::Floor(40 * 144 / 96)
  Check DL13 $true (($d -ne [IntPtr]::Zero) -and $r.T -ge 0 -and $r.B -le $workB) "long dialog fits a 420 px high screen at 150 % ($($r.T)..$($r.B) <= $workB)"
  $body = if ($d -ne [IntPtr]::Zero) { [U]::GetDlgItem($d, 101) } else { [IntPtr]::Zero }
  Check DL14 "Edit|True" ([U]::Cls($body) + "|" + ([U]::Text($body).Length -gt 50)) "a body that does not fit becomes a scrolling read-only EDIT with the full text"
  if ($d -ne [IntPtr]::Zero) { [void][U]::PostMessageW($d, 0x0111, [IntPtr]1, [IntPtr]::Zero) }
  Start-Sleep -Milliseconds 800
  Stop-TestInstances $suffix; if ($p -and -not $p.HasExited) { Stop-Process $p.Id -Force -ErrorAction Ignore }

  # boundary: one long unbreakable path (no spaces) in the message, normal 1366x768 screen at 100 %
  $deep = Join-Path $sp ("dialog_long_" + ("x" * 60) + "\" + ("LongFolderNameWithoutAnySpaces" * 2) + "\" + ("y" * 30))
  New-Item -ItemType Directory -Force $deep | Out-Null
  [IO.File]::WriteAllBytes("$deep\config.dat", [byte[]](1..64))
  $env:ONEKEY_CONFIG_DIR = $deep; $env:ONEKEY_TEST_WORKAREA = "1366,728,96"
  $p = Start-Process $exe -PassThru
  $d = [IntPtr]::Zero; for ($i = 0; $i -lt 50 -and $d -eq [IntPtr]::Zero; $i++) { $d = [U]::FindCls([uint32]$p.Id, "OneKeyDialog"); Start-Sleep -Milliseconds 100 }
  $r = New-Object 'U+RECT'; $rb = New-Object 'U+RECT'
  $body = if ($d -ne [IntPtr]::Zero) { [U]::GetDlgItem($d, 101) } else { [IntPtr]::Zero }
  if ($d -ne [IntPtr]::Zero) { [void][U]::GetWindowRect($d, [ref]$r); [void][U]::GetWindowRect($body, [ref]$rb) }
  Check DL15 "True|True" "$(($d -ne [IntPtr]::Zero) -and $r.L -ge 0 -and $r.R -le 1366)|$($rb.R -le $r.R -and $rb.L -ge $r.L)" "long unbreakable path: dialog inside 1366 px ($($r.L)..$($r.R)), body inside the dialog"
  $txt = [U]::Text($body) -replace "[\r\n]", ""
  Check DL16 "True" "$($txt.Contains(("LongFolderNameWithoutAnySpaces" * 2)) -and $txt.Contains(("y" * 30)))" "the whole path is in the body (wrapped or scrolled, not cut)"
  if ($d -ne [IntPtr]::Zero) { [void][U]::PostMessageW($d, 0x0111, [IntPtr]1, [IntPtr]::Zero) }
  Start-Sleep -Milliseconds 800
  $env:ONEKEY_CONFIG_DIR = $cfg
} catch { Add-Failure ("exception: " + $_) }
finally { $env:ONEKEY_TEST_WORKAREA = $null; Stop-TestInstances $suffix; if ($p -and -not $p.HasExited) { Stop-Process $p.Id -Force -ErrorAction Ignore } }
Complete-Checks
