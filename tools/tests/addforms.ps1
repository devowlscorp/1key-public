# [+ Add] menu and the three text forms (0.3.107, 2026-10-06 user: coworkers found adding confusing).
# AF01 the add menu has two groups: frequently used text / site-app login / several fields (4001 4006 4007), then program /
#      folder / website (4002 4003 4005); the gap between the groups is larger than between rows of one group
# AF02 frequently used text (4001): one input (302 masked), no second input, no [+] (339), eye (303); use = cursor (348 index 0)
# AF03 site/app login (4006): two inputs, ID (302) not masked, PW (331) masked, eye (303) on the PW row, no [+], no [x] (336),
#      [Link] (323) on the ID row; use = site/app (348 index 1). Eye shows the PW and leaves the ID as it is
# AF04 text for several fields (4007): the old screen - [+] (339) present; use = cursor
# AF05 a login item saved (use switched to cursor, so no link is needed) and opened again: still the login form (ID plain,
#      PW masked, no [+]) - the form is remembered (s{i}.form)
# AF06 a frequently-used-text item saved and opened again: still one input, no [+]
# AF07 an empty PW in the login form is refused (both fields are required)
# Own config folder and test suffix; the real 1Key is not touched. Run only while nobody uses the PC. ASCII only.
param([string]$Exe = "")
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
Start-Checks -Required @("AF01", "AF02", "AF03", "AF04", "AF05", "AF06", "AF07")
$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$suffix = ".af"
$cfg = "$sp\addforms_cfg"; if (Test-Path $cfg) { Get-ChildItem $cfg -Recurse | Remove-Item -Force -Recurse -ErrorAction Ignore }; New-Item -ItemType Directory -Force $cfg | Out-Null
Add-Type @'
using System;using System.Runtime.InteropServices;using System.Text;
public class AF {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetWindowLongPtrW(IntPtr h, int i);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static IntPtr FindCls(uint pid, string cls) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls && IsWindowVisible(h)) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
}
'@
function Has($id) { [AF]::GetDlgItem($m, $id) -ne [IntPtr]::Zero }
function Click($id, $ms = 700) { [void][AF]::PostMessageW([AF]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds $ms }
function SetText($id, $s) { [void][AF]::SendMessageW([AF]::GetDlgItem($m, $id), 0x000C, [IntPtr]::Zero, [string]$s) }
function Masked($id) { $h = [AF]::GetDlgItem($m, $id); if ($h -eq [IntPtr]::Zero) { return "none" }; [int][AF]::SendMessageW($h, 0x00D2, [IntPtr]::Zero, [IntPtr]::Zero) -ne 0 }   # EM_GETPASSWORDCHAR
function ModeIdx() { $h = [AF]::GetDlgItem($m, 348); if ($h -eq [IntPtr]::Zero) { return -1 }; [int][AF]::GetWindowLongPtrW($h, 16) }
function Top($id) { $r = New-Object AF+RECT; [void][AF]::GetWindowRect([AF]::GetDlgItem($m, $id), [ref]$r); $r.T }
function Bottom($id) { $r = New-Object AF+RECT; [void][AF]::GetWindowRect([AF]::GetDlgItem($m, $id), [ref]$r); $r.B }
function Box() { for ($i = 0; $i -lt 15; $i++) { $b = [AF]::FindCls([uint32]$p.Id, "OneKeyDialog"); if ($b -ne [IntPtr]::Zero) { return $b }; Start-Sleep -Milliseconds 100 }; [IntPtr]::Zero }
function CloseBox() { $b = Box; if ($b -ne [IntPtr]::Zero) { [void][AF]::PostMessageW($b, 0x0111, [IntPtr]1, [IntPtr]::Zero); Start-Sleep -Milliseconds 400; return $true }; $false }
function List() { if (-not (Has 203)) { Click 240 } }

try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_LOCKWIDGET = "0"; $env:ONEKEY_TEST_NO_AUTOLOCK = "1"
  $p = Start-Process $exe -PassThru; $m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80 -and $m -eq [IntPtr]::Zero; $i++) { Start-Sleep -Milliseconds 100; $m = [AF]::FindCls([uint32]$p.Id, "OneKeyMainWindow$suffix") }
  if ($m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 800
  SetText 101 "Master1234"; SetText 102 "Master1234"; Click 103 1200; [void](CloseBox)

  # AF01
  Click 203
  $rows = @(4001, 4006, 4007, 4002, 4003, 4005) | ForEach-Object { Has $_ }
  $gapIn = (Top 4006) - (Bottom 4001); $gapGroups = (Top 4002) - (Bottom 4007)
  Check AF01 "True,True,True,True,True,True|True" "$($rows -join ',')|$($gapGroups -gt $gapIn + 4)" "add menu: three text forms, then three shortcuts, in two groups (gap $gapIn inside, $gapGroups between)"

  # AF02 frequently used text
  Click 4001
  Check AF02 "True|False|False|True|0" "$(Masked 302)|$(Has 331)|$(Has 339)|$(Has 303)|$(ModeIdx)" "frequently used text: one masked input, no [+], eye, use = cursor"
  Click 309; [void](CloseBox); List

  # AF03 login
  Click 203; Click 4006
  $eyeRow = [Math]::Abs((Top 303) - (Top 331)) -lt 20
  $before = "$(Masked 302)|$(Masked 331)|$eyeRow|$(Has 339)|$(Has 336)|$(Has 323)|$(ModeIdx)"
  Click 303 400
  $after = "$(Masked 302)|$(Masked 331)"
  Check AF03 "False|True|True|False|False|True|1/False|False" "$before/$after" "login: ID plain, PW masked, eye on the PW row, no [+]/[x], [Link] on the ID row, use = site/app; eye shows the PW"
  Click 309; [void](CloseBox); List

  # AF04 several fields
  Click 203; Click 4007
  Check AF04 "True|0" "$(Has 339)|$(ModeIdx)" "text for several fields: [+] present, use = cursor"
  Click 309; [void](CloseBox); List

  # AF07 + AF05 login item saved and opened again (slot 0)
  Click 203; Click 4006
  [void][AF]::PostMessageW([AF]::GetDlgItem($m, 348), 0x0100, [IntPtr]0x25, [IntPtr]::Zero); Start-Sleep -Milliseconds 900   # use: Left -> cursor (no link needed)
  SetText 301 "Login A"; SetText 302 "user-a"
  Click 310 900
  $refused = CloseBox
  $stillEdit = Has 310
  Check AF07 "True|True" "$refused|$stillEdit" "login with an empty PW is refused and the edit screen stays"
  SetText 331 "pw-a"; Click 310 900; [void](CloseBox); List
  Click 1000 900
  Check AF05 "False|True|False|True" "$(Masked 302)|$(Masked 331)|$(Has 339)|$(Has 331)" "saved login item opens again in the login form"
  Click 309; [void](CloseBox); List

  # AF06 phrase item saved and opened again (slot 1)
  Click 203; Click 4001; SetText 301 "Phrase B"; SetText 302 "text-b"; Click 310 900; [void](CloseBox); List
  Click 1001 900
  Check AF06 "True|False|False" "$(Masked 302)|$(Has 331)|$(Has 339)" "saved frequently-used-text item opens again with one input and no [+]"
  Click 309; [void](CloseBox)
} catch { Add-Failure ("exception: " + $_) }
finally { if ($p -and -not $p.HasExited) { [void][AF]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 1200; if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force } }; Stop-TestInstances $suffix }
Complete-Checks
