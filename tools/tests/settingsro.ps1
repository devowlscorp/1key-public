# Read-only "start with Windows" switch (2001) while autostart is paused (0.2.155) - Codex R157-2 (0.2.159):
# the switch only SHOWS whether the Startup shortcut exists; the saved setting (autostart= in the config header) is not an edit.
# Setup: a test config is made, then its header is edited to autostart=1 while no shortcut exists (the test instance's Startup
# shortcut is never created), so the switch shows OFF and the saved value is ON - the mismatch the fix is about.
# SR01 the mismatch is really there: switch disabled and OFF, saved autostart=1 (so SR02/SR03 are not vacuous)
# SR02 open settings and go back without touching anything: no "discard changes?" question, back on the list
# SR03 change another setting (2002) and save: autostart=1 kept, startmin changed
# SR04 a press on the disabled switch does not change it
# Other "display false" causes (old Run value / task only, lookup failure) take the same path: ReadSettings no longer reads 2001.
# Own config folder and instance suffix; the real 1Key, its settings and its autostart are not touched. ASCII only.
# Judgement: tools\tests\lib\Check.ps1. Exe: first argument, default build\<csproj version>\1Key.exe.
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
Start-Checks -Required @('SR01', 'SR02', 'SR03', 'SR04')

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($args.Count -gt 0) { $args[0] } else { Get-DefaultExe }
$suffix = ".settingsro"
$cfg = "$sp\settingsro_cfg"; if (Test-Path $cfg) { Get-ChildItem $cfg -Recurse | Remove-Item -Force -Recurse -ErrorAction Ignore }; New-Item -ItemType Directory -Force $cfg | Out-Null
Add-Type @'
using System;using System.Runtime.InteropServices;using System.Text;using System.Threading;
public class SR {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static IntPtr FindCls(uint pid, string cls) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
  public static void Press(IntPtr m, int id, int hold) {
    IntPtr c = GetDlgItem(m, id); if (c == IntPtr.Zero) return;
    RECT rc; GetClientRect(c, out rc); IntPtr lp = (IntPtr)((rc.R/2) | ((rc.B/2) << 16));
    PostMessageW(c, 0x0201, (IntPtr)1, lp); Thread.Sleep(hold); PostMessageW(c, 0x0202, IntPtr.Zero, lp);
  }
}
'@
function SetText($h, $s) { [void][SR]::SendMessageW($h, 0x000C, [IntPtr]::Zero, [string]$s) }
function Click($id) { [void][SR]::PostMessageW([SR]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 500 }
function Has($id) { [SR]::GetDlgItem($m, $id) -ne [IntPtr]::Zero }
function Chk($id) { [int][SR]::SendMessageW([SR]::GetDlgItem($m, $id), 0x00F0, [IntPtr]::Zero, [IntPtr]::Zero) }
function Box() { [SR]::FindCls([uint32]$p.Id, 'OneKeyDialog') -ne [IntPtr]::Zero }
function HeaderVal($k) { ([regex]::Match((Get-TestHeader $cfg), "(?m)^$k=(\d+)")).Groups[1].Value }
function Start1Key() {
  $script:p = Start-Process $exe -PassThru
  $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $script:m = [SR]::FindCls([uint32]$script:p.Id, "OneKeyMainWindow$suffix"); if ($script:m -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 100 }
  if ($script:m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 600
}
function Quit1Key() { [void][SR]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i = 0; $i -lt 50 -and -not $p.HasExited; $i++) { Start-Sleep -Milliseconds 100 } }

try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_WORKAREA = $TallScreen
  Start1Key
  SetText ([SR]::GetDlgItem($m,101)) "Master1234"; SetText ([SR]::GetDlgItem($m,102)) "Master1234"; Click 103
  # auto-lock off (system idle time would lock the test instance on an idle PC)
  Click 220; [void][SR]::PostMessageW([SR]::GetDlgItem($m, 2005), 0x0100, [IntPtr]0x24, [IntPtr]::Zero); Start-Sleep -Milliseconds 300
  Click 2012; Start-Sleep -Milliseconds 3500
  Confirm-AutoLockOff $cfg
  Quit1Key

  # saved autostart=1 (header only; autostart is not part of the password AAD). The header is never printed.
  Add-Type -AssemblyName System.Security
  $ent = [Text.Encoding]::UTF8.GetBytes("1Key/approval-password/v1")
  $h = Get-TestHeader $cfg
  if ($h -notmatch "(?m)^autostart=0$") { throw "setup: no autostart=0 line in the test header" }
  $h = [regex]::Replace($h, "(?m)^autostart=0$", "autostart=1")
  [IO.File]::WriteAllBytes("$cfg\config.dat", [Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes($h), $ent, 'CurrentUser'))
  $h = $null
  $startmin0 = HeaderVal "startmin"

  Start1Key
  SetText ([SR]::GetDlgItem($m,101)) "Master1234"; Click 103; Start-Sleep -Milliseconds 600
  Click 220; Start-Sleep -Milliseconds 300
  Check SR01 "True|False|0|1" "$(Has 241)|$([SR]::IsWindowEnabled([SR]::GetDlgItem($m, 2001)))|$(Chk 2001)|$(HeaderVal 'autostart')" "settings open; switch disabled and OFF while saved autostart=1"
  Click 240; Start-Sleep -Milliseconds 600
  Check SR02 "False|True|False" "$(Box)|$(Has 203)|$(Has 241)" "back without changes: no discard question, on the list"
  if (Box) { $b = [SR]::FindCls([uint32]$p.Id, 'OneKeyDialog'); [void][SR]::PostMessageW($b, 0x0111, [IntPtr]7, [IntPtr]::Zero); Start-Sleep -Milliseconds 600 }   # No: stay
  if (-not (Has 241)) { Click 220; Start-Sleep -Milliseconds 300 }
  $c0 = Chk 2001
  [SR]::Press($m, 2001, 40); Start-Sleep -Milliseconds 400
  Check SR04 "$c0" "$(Chk 2001)" "a press on the disabled switch changes nothing"
  [SR]::Press($m, 2002, 40); Start-Sleep -Milliseconds 400
  Click 2012; Start-Sleep -Milliseconds 2500
  Check SR03 "1|$(1 - [int]$startmin0)" "$(HeaderVal 'autostart')|$(HeaderVal 'startmin')" "saving another setting keeps autostart=1 (startmin $startmin0 -> changed)"
  Quit1Key
} catch { Add-Failure ("exception: " + $_) }
finally { Stop-TestInstances $suffix }
Complete-Checks
