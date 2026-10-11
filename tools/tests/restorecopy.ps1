# Restore over unreadable settings keeps a copy first or does not restore (R07, Codex pre-release review 0.5.22: the copy result was
# ignored, so a failed copy still let the restore replace the old file). Dummy settings in a fresh test folder only.
#  A backup file is made from a test instance (master + one item). Then, per case, a test instance starts on a config.dat whose
#  bytes were damaged (load failed -> first setup), the tray restore command opens the restore screen, and [Restore] is pressed:
#   RC01 control: the copy can be made -> restored (lock screen), a config.*.bak with exactly the damaged bytes, no commit mark
#   RC02 the old file cannot be read (held open by this script without read sharing, delete sharing allowed)
#   RC03 the folder refuses new files (a deny "create files" entry for this user on the test config folder, removed afterwards)
#   RC04 every copy name is taken (config.<stamp>.bak and -1..-99 created for the next few minutes)
#  RC02-RC04 expect: the "could not restore ... config.*.bak" box, config.dat byte-identical, no restore.commit mark, no new copy,
#  the restore screen still open (no lock screen = no "restored").
# What this does NOT prove: a real full disk, another user's process holding the file, network folders.
# Test hooks (ONEKEY_TEST=1 only): ONEKEY_TEST_PICK (file dialog answer), ONEKEY_TEST_LANG=en (box texts matched in English).
# Judgement: tools\tests\lib\Check.ps1. ASCII only.
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
Start-Checks -Required @("RC01", "RC02", "RC03", "RC04")

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($args.Count -gt 0) { $args[0] } else { Get-DefaultExe }
$suffix = ".rc"
$base = [IO.Path]::GetFullPath($sp).TrimEnd('\')
$real = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA "1Key")).TrimEnd('\')
if ($base -eq $real -or $base.StartsWith($real + "\", [StringComparison]::OrdinalIgnoreCase)) { throw "test root is inside the real settings folder: $base" }
$root = Join-Path $base ("restorecopy-{0:yyyyMMdd-HHmmss}-{1}" -f (Get-Date), $PID)
New-Item -ItemType Directory $root -ErrorAction Stop | Out-Null
"run folder: $root"
$pick = "$root\pick.txt"; $bak = "$root\one.1keybak"
Add-Type @'
using System;using System.Runtime.InteropServices;using System.Text;
public class RcU {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int hh, uint f);
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static IntPtr FindCls(uint pid, string cls) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
  public static IntPtr FindBox(uint pid) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && (Cls(h) == "#32770" || Cls(h) == "OneKeyDialog") && IsWindowVisible(h)) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
  // the box's whole text: its own caption plus every child's text
  public static bool TryWrite(string f) { try { System.IO.File.WriteAllText(f, "x"); System.IO.File.Delete(f); return true; } catch { return false; } }
  public static string Text(IntPtr h) { var t = new StringBuilder(4096); GetWindowTextW(h, t, 4096); return t.ToString(); }
  // body text like backup.ps1: system message box static 0xFFFF, 1Key dialog body 101 (Dialog.IdBody)
  public static string BoxText(IntPtr b) { return Cls(b) == "#32770" ? Text(GetDlgItem(b, 0xFFFF)) : Text(GetDlgItem(b, 101)); }
}
'@
$env:ONEKEY_TEST = "1"; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_LANG = "en"; $env:ONEKEY_TEST_PICK = $pick
$env:ONEKEY_TEST_LOCKWIDGET = "0"; $env:ONEKEY_TEST_NO_AUTOLOCK = "1"
$Z = [IntPtr]::Zero
function SetText($id, $s) { [void][RcU]::SendMessageW([RcU]::GetDlgItem($m, $id), 0x000C, $Z, [string]$s) }
function Click($id, $ms = 700) { [void][RcU]::PostMessageW([RcU]::GetDlgItem($m, $id), 0x00F5, $Z, $Z); Start-Sleep -Milliseconds $ms }
function Has($id) { [RcU]::GetDlgItem($m, $id) -ne $Z }
function WaitHas($id, $ms = 15000) { $t = [Environment]::TickCount; while ([Environment]::TickCount - $t -lt $ms) { if (Has $id) { Start-Sleep -Milliseconds 300; return $true }; Start-Sleep -Milliseconds 100 }; $false }
function WaitBox($ms = 20000) { $t = [Environment]::TickCount; while ([Environment]::TickCount - $t -lt $ms) { $b = [RcU]::FindBox([uint32]$p.Id); if ($b -ne $Z) { Start-Sleep -Milliseconds 300; return $b }; Start-Sleep -Milliseconds 100 }; $Z }
# [Restore] first asks "The current settings ... will be replaced with this backup. Continue?" - Yes; then the result box (or none)
function Confirm() { $b = WaitBox 25000; $t = if ($b -ne $Z) { [RcU]::BoxText($b) } else { "" }; if ($t -match 'will be replaced') { Answer $b 6; return $true }; $false }
function Answer($b, $cmd = 1) { if ($b -ne $Z) { [void][RcU]::PostMessageW($b, 0x0111, [IntPtr]$cmd, $Z); Start-Sleep -Milliseconds 500 } }
function Launch($cfg) {
  $env:ONEKEY_CONFIG_DIR = $cfg
  $script:p = Start-Process $exe -PassThru; $script:m = $Z
  for ($i = 0; $i -lt 100; $i++) { $script:m = [RcU]::FindCls([uint32]$script:p.Id, "OneKeyMainWindow$suffix"); if ($script:m -ne $Z -or $script:p.HasExited) { break }; Start-Sleep -Milliseconds 100 }
  if ($script:m -eq $Z) { throw "no main window" }
  Start-Sleep -Milliseconds 600
}
function Quit() { [void][RcU]::PostMessageW($m, 0x8005, $Z, $Z); for ($i = 0; $i -lt 80 -and -not $p.HasExited; $i++) { Start-Sleep -Milliseconds 100 }; if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force } }
function Hash($f) { if (Test-Path -LiteralPath $f) { (Get-FileHash -LiteralPath $f -Algorithm SHA256).Hash } else { "none" } }
function Baks($cfg) { @(Get-ChildItem -LiteralPath $cfg -Filter "config.*.bak" -File -ErrorAction Ignore) }

# one damaged-settings folder: a real config.dat made by a test instance, then bytes in the middle flipped (load fails)
function DamagedFolder($name) {
  $cfg = "$root\$name"; New-Item -ItemType Directory $cfg | Out-Null
  Launch $cfg
  SetText 101 "Old-Master-55"; SetText 102 "Old-Master-55"; Click 103 1500
  Quit
  $bytes = [IO.File]::ReadAllBytes("$cfg\config.dat")
  for ($i = [int]($bytes.Length / 2); $i -lt [Math]::Min($bytes.Length, [int]($bytes.Length / 2) + 64); $i++) { $bytes[$i] = $bytes[$i] -bxor 0x5A }
  [IO.File]::WriteAllBytes("$cfg\config.dat", $bytes)
  $cfg
}
# start on the damaged folder, answer the load-failed box, open the tray restore screen and fill it (not pressed yet)
function RestoreReady($cfg) {
  Launch $cfg
  $b = WaitBox 8000; $loadBox = if ($b -ne $Z) { [RcU]::BoxText($b) } else { "" }; Answer $b 1
  [void][RcU]::PostMessageW($m, 0x0111, [IntPtr]3005, $Z); Start-Sleep -Milliseconds 800
  if (-not (WaitHas 528 5000)) { throw "restore screen did not open" }
  [IO.File]::WriteAllText($pick, $bak); Click 527; SetText 528 "Backup-Pw-9"; SetText 529 "New-Master-77"; SetText 530 "New-Master-77"
  $loadBox
}

try {
  Stop-TestInstances $suffix
  # ---------------------------------------------------------------- the backup file
  $cfgA = "$root\a"; New-Item -ItemType Directory $cfgA | Out-Null
  Launch $cfgA
  SetText 101 "Master1234"; SetText 102 "Master1234"; Click 103 1500
  Click 203; Click 4001; SetText 301 "S1"; SetText 302 "content-1"; Click 310
  $b = [RcU]::FindBox([uint32]$p.Id); if ($b -ne $Z) { Answer $b 2 }
  [IO.File]::WriteAllText($pick, $bak)
  if (-not (Has 216)) { Click 220 }
  Click 216; SetText 520 "Master1234"; SetText 521 "Backup-Pw-9"; SetText 522 "Backup-Pw-9"; Click 524
  [void](WaitHas 216 20000)
  Quit
  if (-not (Test-Path $bak)) { throw "setup: no backup file" }

  # ---------------------------------------------------------------- RC01 control
  $cfg = DamagedFolder "c1"; $h0 = Hash "$cfg\config.dat"
  $lb = RestoreReady $cfg
  Click 532; $conf = Confirm
  $ok = WaitHas 101 25000
  $bx = [RcU]::FindBox([uint32]$p.Id); if ($bx -ne $Z) { "  box after restore: $([RcU]::BoxText($bx))" }
  $copies = Baks $cfg
  "  copies: $(($copies | ForEach-Object { $_.Name + ' ' + $_.Length }) -join ', ')  config.dat changed: $((Hash "$cfg\config.dat") -ne $h0)"
  $copyHash = if ($copies.Count -eq 1) { Hash $copies[0].FullName } else { "count $($copies.Count)" }
  Check RC01 "True|True|True|False" "$ok|$($copyHash -eq $h0)|$((Hash "$cfg\config.dat") -ne $h0)|$(Test-Path "$cfg\restore.commit")" "control: the copy can be made - restored (lock screen), one config.*.bak with exactly the damaged bytes, new config.dat, no commit mark (load box: $($lb.Substring(0, [Math]::Min(60, $lb.Length))))"
  Quit

  # ---------------------------------------------------------------- RC02 the old file cannot be read
  $cfg = DamagedFolder "c2"; $h0 = Hash "$cfg\config.dat"
  [void](RestoreReady $cfg)
  $hold = [IO.File]::Open("$cfg\config.dat", 'Open', 'Read', 'Delete')   # no read sharing: the copy cannot read it
  try {
    Click 532; $conf = Confirm
    $b = WaitBox 25000; $txt = if ($b -ne $Z) { [RcU]::BoxText($b) } else { "" }; "  box: $($txt -replace "`r?`n", ' / ')"
  } finally { $hold.Dispose() }
  $screen = Has 528; Answer $b 1
  Check RC02 "True|True|True|False|0|False" "$($b -ne $Z)|$($txt -match 'Could not restore' -and $txt -match 'config\.\*\.bak')|$((Hash "$cfg\config.dat") -eq $h0)|$(Test-Path "$cfg\restore.commit")|$((Baks $cfg).Count)|$(Has 101)" "the old file cannot be read: 'could not restore (config.*.bak)', config.dat unchanged, no mark, no copy, not restored (restore screen kept: $screen)"
  Quit

  # ---------------------------------------------------------------- RC03 the folder refuses new files
  $cfg = DamagedFolder "c3"; $h0 = Hash "$cfg\config.dat"
  [void](RestoreReady $cfg)
  $me = [Security.Principal.WindowsIdentity]::GetCurrent().User
  $di = Get-Item -LiteralPath $cfg; $acl = $di.GetAccessControl('Access')   # access rules only (Set-Acl also writes the audit part: needs SeSecurityPrivilege)
  $deny = New-Object Security.AccessControl.FileSystemAccessRule($me, [Security.AccessControl.FileSystemRights]::CreateFiles, 'None', 'None', 'Deny')
  $acl.AddAccessRule($deny); $di.SetAccessControl($acl)
  try {
    $probe = [RcU]::TryWrite("$cfg\probe.txt")
    Click 532; $conf = Confirm
    $b = WaitBox 25000; $txt = if ($b -ne $Z) { [RcU]::BoxText($b) } else { "" }; "  box: $($txt -replace "`r?`n", ' / ')"
  } finally { $acl = $di.GetAccessControl('Access'); [void]$acl.RemoveAccessRule($deny); $di.SetAccessControl($acl) }
  Answer $b 1
  Check RC03 "False|True|True|True|False|0|False" "$probe|$($b -ne $Z)|$($txt -match 'Could not restore' -and $txt -match 'config\.\*\.bak')|$((Hash "$cfg\config.dat") -eq $h0)|$(Test-Path "$cfg\restore.commit")|$((Baks $cfg).Count)|$(Has 101)" "the folder refuses new files (probe write refused first): 'could not restore', config.dat unchanged, no mark, no copy, not restored"
  Quit

  # ---------------------------------------------------------------- RC04 every copy name is taken
  $cfg = DamagedFolder "c4"; $h0 = Hash "$cfg\config.dat"
  [void](RestoreReady $cfg)
  $t0 = Get-Date
  for ($s = -2; $s -le 180; $s++) {
    $stamp = $t0.AddSeconds($s).ToString("yyyyMMdd-HHmmss")
    for ($n = 0; $n -lt 100; $n++) { $f = if ($n -eq 0) { "$cfg\config.$stamp.bak" } else { "$cfg\config.$stamp-$n.bak" }; [IO.File]::WriteAllBytes($f, [byte[]]@()) }
  }
  $taken = (Baks $cfg).Count
  Click 532; $conf = Confirm
  $b = WaitBox 25000; $txt = if ($b -ne $Z) { [RcU]::BoxText($b) } else { "" }; "  box: $($txt -replace "`r?`n", ' / ')"
  Answer $b 1
  $nonEmpty = @(Baks $cfg | Where-Object Length -gt 0).Count
  Check RC04 "True|True|True|False|$taken|0|False" "$($b -ne $Z)|$($txt -match 'Could not restore' -and $txt -match 'config\.\*\.bak')|$((Hash "$cfg\config.dat") -eq $h0)|$(Test-Path "$cfg\restore.commit")|$((Baks $cfg).Count)|$nonEmpty|$(Has 101)" "every copy name is taken ($taken names for 3 minutes): 'could not restore', config.dat unchanged, no mark, no extra copy, not restored"
  Quit
} catch { Add-Failure ("exception: " + $_) }
finally {
  if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction Ignore }
  Stop-TestInstances $suffix
  foreach ($v in "ONEKEY_TEST_LANG", "ONEKEY_TEST_PICK", "ONEKEY_TEST_LOCKWIDGET", "ONEKEY_TEST_NO_AUTOLOCK") { Remove-Item "env:$v" -ErrorAction Ignore }
}
Complete-Checks
