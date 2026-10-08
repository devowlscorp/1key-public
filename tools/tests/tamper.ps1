# Storage tests. Since 0.3.39 every save is secret format 10 (Argon2id; user decision 2026-10-05: old builds cannot open it),
# so the "drops back to an older format number" expectations of 0.2.x-0.3.32 became "secretv=10" (0.3.46 update, results
# of the first 0.3.46 run kept in docs/test-logs/2026-10-05_0.3.46/run-185149).
#  A)  legacy file made by 0.2.22 (no AAD) must still unlock with the new build and be re-saved as secretv=10 / kdf=argon2id.
#  A2) file made by 0.2.25 (secretv=2, AAD over 3 slots) must open in the 99-slot build and be re-saved as secretv=10.
#  B)  flipping "autolock=" in the DPAPI-protected header (same account, no master) must make unlock FAIL
#      (header is bound as AES-GCM AAD); restoring the original file makes unlock work again; a wrong master is rejected.
#  C)  a file whose secret= is corrupted must NOT be treated as a silent first run.
#  D)  master policy: <4 chars rejected; weak values only warn (No keeps the screen, Yes accepts); strong values pass silently.
#  F)  T7: 0.2.22 and 0.2.25 given a v3 file + the right master: unlock fails, no create screen, file byte-identical.
#  G)  T9: secretv=1 and v=9 -> "new version needed" screen (104), s01.vk and a duplicate key -> corrupt warning,
#      a payload with more lines than slots -> rejected at unlock; the file is byte-identical after each run.
#  H)  "no Enter in browsers" (user decision 2026-09-29, 0.2.41; Enter is sent in browsers by default): only selectable
#      while Enter is on; saved as secretv=10 with s<i>.noenterbrowser=0/1 on every slot (format 5+ always writes it);
#      flipping the flag or lowering secretv makes unlock fail; a flag injected into a v10 file is refused (AAD); older builds
#      (0.2.38, 0.2.40) refuse a v5 file cleanly; a secretv=4 file written by 0.2.40 ("also in browsers") opens and is
#      moved to secretv=10 with every Enter item sending in browsers (the new default); if that re-save fails, the v4
#      file still opens, opens again after locking, and is moved on the next unlock once it can be written (Codex V42).
#  J)  (0.3.7, TODO "old exe vs v6 file") a file marked with a newer secret format (secretv=7: program links, 0.2.9x+) given to older
#      builds: 0.2.21 (the build coworkers still had, no format check) does not unlock and offers no create screen; 0.2.40 and 0.2.55
#      show 'new version needed'; the file is byte-identical after each, and no config.*.bak is made.
#      0.3.32 (the released build before Argon2id) given a real format-10 file: 'new version needed', file unchanged, no .bak.
# Judgement: tools\tests\lib\Check.ps1 (T18). ASCII only.
# Args: tamper.ps1 [new exe] [0.2.22 exe] [0.2.25 exe] [0.2.38 exe] [0.2.40 exe]. Scratch folder: %TEMP%\1Key-tests (override with ONEKEY_TEST_DIR).
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
Start-Checks -Required @(
  'TA01','TA02','TA03','TA04','TA05','TA06','TA07',
  'TB01','TB02','TB03','TB04','TB05','TB06','TB07',
  'TC01','TC02','TC03','TC04','TC05',
  'TD01','TD02','TD03',
  'TE01','TE02','TE03','TE04','TE05','TE06',
  'TF01','TF02','TF03','TF04','TF05','TF06','TF07','TF08',
  'TG01','TG02','TG03','TG04','TG05','TG06','TG07','TG08','TG09','TG10','TG11','TG12','TG13','TG14','TG15',
  'TH01','TH02','TH03','TH04','TH05','TH06','TH07','TH08','TH09','TH10','TH11','TH12','TH13','TH14','TH15','TH16','TH17','TH18','TH19','TH20','TH21',
  'TJ01','TJ02','TJ03','TJ04','TJ05','TJ06','TJ07','TJ08')

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$new = if ($args.Count -gt 0) { $args[0] } else { Get-DefaultExe }
$old = if ($args.Count -gt 1) { $args[1] } else { "$repo\build\0.2.22\1Key.exe" }   # last build without AAD (legacy format)
$mid = if ($args.Count -gt 2) { $args[2] } else { "$repo\build\0.2.25\1Key.exe" }   # last secretv=2 build
$prev = if ($args.Count -gt 3) { $args[3] } else { "$repo\build\0.2.38\1Key.exe" }   # last build that knows only secretv 2/3
$v40 = if ($args.Count -gt 4) { $args[4] } else { "$repo\build\0.2.40\1Key.exe" }    # last build writing secretv=4 ("also in browsers")
$suffix = ".tp"
$cfg = "$sp\tamper_cfg"; if (Test-Path $cfg) { Get-ChildItem $cfg -Recurse | Remove-Item -Force -Recurse -ErrorAction Ignore }; New-Item -ItemType Directory -Force $cfg | Out-Null
Add-Type -AssemblyName System.Security
Add-Type @'
using System;using System.Runtime.InteropServices;using System.Text;
public class U {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, StringBuilder l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int hh, uint f);
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static string Title(IntPtr h) { var t = new StringBuilder(256); GetWindowTextW(h, t, 256); return t.ToString(); }
  public static IntPtr FindCls(uint pid, string cls) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
  public static IntPtr FindBox(uint pid) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && (Cls(h) == "#32770" || Cls(h) == "OneKeyDialog") && IsWindowVisible(h)) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
}
'@
function SetText($h, $s) { [void][U]::SendMessageW($h, 0x000C, [IntPtr]::Zero, [string]$s) }
function Click($id) { [void][U]::PostMessageW([U]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 700 }
function Has($id) { [U]::GetDlgItem($m, $id) -ne [IntPtr]::Zero }
# toggles have no BM_CLICK: Space flips them like a user would (and does nothing while the toggle is disabled)
function Flip($id) { [void][U]::PostMessageW([U]::GetDlgItem($m, $id), 0x0100, [IntPtr]0x20, [IntPtr]::Zero); Start-Sleep -Milliseconds 300 }
function IsOn($id) { [U]::SendMessageW([U]::GetDlgItem($m, $id), 0x00F0, [IntPtr]::Zero, [string]$null) -eq [IntPtr]1 }
function EnterIdx() { [int][U]::SendMessageW([U]::GetDlgItem($m, 305), 0x00F0, [IntPtr]::Zero, [string]$null) }
function SetEnter($i) { [void][U]::SendMessageW([U]::GetDlgItem($m, 305), 0x00F1, [IntPtr]$i, [string]$null); Start-Sleep -Milliseconds 200 }
function IsEnabled($id) { [U]::IsWindowEnabled([U]::GetDlgItem($m, $id)) }
function Unlock() { SetText ([U]::GetDlgItem($m,101)) "Master1234"; Click 103 }
function GetEdit($id) { $sb = New-Object System.Text.StringBuilder 512; [void][U]::SendMessageW([U]::GetDlgItem($m,$id), 0x000D, [IntPtr]512, $sb); $sb.ToString() }
function BoxTitleAndClose($cmd = 1) { for ($i=0;$i -lt 20;$i++) { $b = [U]::FindBox([uint32]$p.Id); if ($b -ne [IntPtr]::Zero) { $t = [U]::Title($b); [void][U]::PostMessageW($b, 0x0111, [IntPtr]$cmd, [IntPtr]::Zero); Start-Sleep -Milliseconds 300; return $t }; Start-Sleep -Milliseconds 100 }; return "" }
function Launch($exe) {
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix
  $script:p = Start-Process $exe -PassThru
  $script:m = [IntPtr]::Zero; $script:startupBox = ""
  for ($i = 0; $i -lt 150; $i++) {
    if ($script:p.HasExited) { break }
    $script:m = [U]::FindCls([uint32]$script:p.Id, "OneKeyMainWindow$suffix"); if ($script:m -ne [IntPtr]::Zero) { break }
    $b = [U]::FindBox([uint32]$script:p.Id)   # a warning box before the main window (e.g. unreadable config)
    if ($b -ne [IntPtr]::Zero) { $script:startupBox = [U]::Title($b); [void][U]::PostMessageW($b, 0x0111, [IntPtr]2, [IntPtr]::Zero) }   # IDCANCEL closes an unowned MB_OK box reliably
    Start-Sleep -Milliseconds 100 }
  if ($script:m -eq [IntPtr]::Zero) { throw "no main window (exited=$($script:p.HasExited) startupBox='$($script:startupBox)')" }
  Start-Sleep -Milliseconds 700
  [void][U]::SetWindowPos($script:m, [IntPtr]::Zero, 1400, 60, 0, 0, 0x0001 -bor 0x0004 -bor 0x0010)
}
function Quit() { [void][U]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i=0; $i -lt 50 -and -not $p.HasExited; $i++) { Start-Sleep -Milliseconds 100 }; if (-not $p.HasExited) { "  (quit timed out, killing)"; Stop-Process $p.Id -Force -ErrorAction Ignore }; Start-Sleep -Milliseconds 500 }
$entropy = [Text.Encoding]::UTF8.GetBytes("1Key/approval-password/v1")
function ReadHeader() { $blob = [IO.File]::ReadAllBytes("$cfg\config.dat"); [Text.Encoding]::UTF8.GetString([Security.Cryptography.ProtectedData]::Unprotect($blob, $entropy, 'CurrentUser')) }
function Hash() { (Get-FileHash "$cfg\config.dat" -Algorithm SHA256).Hash }
function WriteHeader($text) { $plain = [Text.Encoding]::UTF8.GetBytes($text); [IO.File]::WriteAllBytes("$cfg\config.dat", [Security.Cryptography.ProtectedData]::Protect($plain, $entropy, 'CurrentUser')) }

try {
  Stop-TestInstances $suffix
  foreach ($e in $new, $old, $mid, $prev, $v40) { if (-not (Test-Path $e)) { throw "exe not found: $e" } }
  "new=" + (Get-Item $new).VersionInfo.FileVersion + "  legacy=" + (Get-Item $old).VersionInfo.FileVersion + "  v2=" + (Get-Item $mid).VersionInfo.FileVersion

  # ---- A) legacy file from 0.2.22
  Launch $old
  SetText ([U]::GetDlgItem($m,101)) "Master1234"; SetText ([U]::GetDlgItem($m,102)) "Master1234"; Click 103
  Check TA01 $true (Has 203) "A: legacy build created config (list screen)"
  # a slot whose content has backslashes and quotes (escape round-trip through the legacy -> v3 migration)
  $slotText = 'P@ss\word "q" ''s'' \n-not-a-newline'
  Click 203; Click 4001
  SetText ([U]::GetDlgItem($m,301)) "Legacy Slot"; SetText ([U]::GetDlgItem($m,302)) $slotText
  Click 310; [void](BoxTitleAndClose)
  Check TA02 $true (Has 200) "A: legacy slot saved (legacy build uses row id 200)"
  Quit
  $h = ReadHeader
  Check TA03 $true (($h -notmatch "secretv=") -and ($h -match "secret=")) "A: legacy header has no secretv"
  Launch $new
  Check TA04 $true (Has 101) "A: new build shows unlock screen for legacy file"
  SetText ([U]::GetDlgItem($m,101)) "Master1234"; Click 103
  Check TA05 $true (Has 203) "A: legacy file unlocked with new build"
  Click 1000   # row click = edit (0.2.28)
  Click 303    # reveal the content (WM_GETTEXT is blocked on a masked password edit from another process)
  Check TA06 $slotText (GetEdit 302) "A: slot content identical after migration (escapes preserved)"
  Click 311
  Quit
  $h = ReadHeader
  Check TA07 $true (($h -match "(?m)^secretv=10$") -and ($h -match "(?m)^kdf=argon2id$")) "A: migrated to secretv=10 / kdf=argon2id on unlock"
  Copy-Item "$cfg\config.dat" "$cfg\good.dat" -Force

  # ---- A2) file written by 0.2.25 (secretv=2: AAD over exactly 3 slots)
  Check TB01 $true (Test-Path $mid) "A2: 0.2.25 build present for the v2 scenario"
  Remove-Item "$cfg\config.dat" -Force
  Launch $mid
  SetText ([U]::GetDlgItem($m,101)) "Master1234"; SetText ([U]::GetDlgItem($m,102)) "Master1234"; Click 103
  Click 203; Click 4001
  SetText ([U]::GetDlgItem($m,301)) "V2 Slot"; SetText ([U]::GetDlgItem($m,302)) "v2content"
  Click 310; [void](BoxTitleAndClose)
  Check TB02 $true (Has 200) "A2: 0.2.25 saved a slot (row 200)"
  Quit
  $h = ReadHeader
  Check TB03 $true ($h -match "secretv=2") "A2: file is secretv=2 (3-slot AAD)"
  Launch $new
  SetText ([U]::GetDlgItem($m,101)) "Master1234"; Click 103
  Check TB04 $true (Has 1000) "A2: v2 file unlocked by the 99-slot build"
  Click 1000; Click 303
  Check TB05 "v2content" (GetEdit 302) "A2: slot content intact after v2 -> v3"
  Click 311
  Quit
  $h = ReadHeader
  Check TB06 $true ($h -match "(?m)^secretv=10$") "A2: re-saved as secretv=10"
  Launch $new
  SetText ([U]::GetDlgItem($m,101)) "Master1234"; Click 103
  Check TB07 $true (Has 1000) "A2: v10 file reopens"
  Quit

  # ---- B) tamper autolock in header (same account, no master)
  $autolockLine = ([regex]::Match($h, "autolock=\d+")).Value
  Check TC01 $true ($autolockLine -ne "") "B: header contains $autolockLine"
  WriteHeader ($h -replace "autolock=\d+", "autolock=0")
  Launch $new
  Check TC02 $true (Has 101) "B: tampered file still shows unlock screen (not create)"
  SetText ([U]::GetDlgItem($m,101)) "Master1234"; Click 103
  $t = BoxTitleAndClose
  Check TC03 $true ((Has 101) -and ($t -ne "")) "B: unlock REJECTED after header tamper (box='$t')"
  Quit
  Copy-Item "$cfg\good.dat" "$cfg\config.dat" -Force
  Launch $new
  SetText ([U]::GetDlgItem($m,101)) "Master1234"; Click 103
  Check TC04 $true (Has 203) "B: original file unlocks again"
  Quit
  Launch $new
  SetText ([U]::GetDlgItem($m,101)) "WrongMaster99"; Click 103
  $t = BoxTitleAndClose
  Check TC05 $true ((Has 101) -and ($t -ne "")) "B: wrong master on a good file is rejected"
  Quit

  # ---- C) corrupted secret= must not look like first run
  $h = ReadHeader
  WriteHeader ($h -replace "secret=[A-Za-z0-9+/=]+", "secret=!!notbase64!!")
  Launch $new
  # the warning is a themed modal (OneKeyDialog) shown after the (still hidden) main window exists
  $t = BoxTitleAndClose 1
  Check TD01 $true ($t -ne "") "C: load-failure warning shown (box='$t')"
  Start-Sleep -Milliseconds 700
  Check TD02 $true ((Has 101) -and (Has 102)) "C: create screen shown after warning (file unreadable) - fields 101/102"
  Quit
  Check TD03 0 @(Get-ChildItem "$cfg\config*.bak" -ErrorAction Ignore).Count "C: no .bak yet (nothing saved)"
  Copy-Item "$cfg\good.dat" "$cfg\config.dat" -Force

  # ---- D) master policy
  Remove-Item "$cfg\config.dat" -Force
  Launch $new
  SetText ([U]::GetDlgItem($m,101)) "123"; SetText ([U]::GetDlgItem($m,102)) "123"; Click 103
  $t = BoxTitleAndClose 1
  Check TE01 $true ((Has 102) -and ($t -ne "")) "D: 3-char master rejected (hard minimum 4)"
  $k = 2
  foreach ($weak in "12345678", "aaaaaaaa", "password1") {
    SetText ([U]::GetDlgItem($m,101)) $weak; SetText ([U]::GetDlgItem($m,102)) $weak; Click 103
    $t = BoxTitleAndClose 7   # IDNO: keep the recommendation
    Check ("TE0$k") $true ((Has 102) -and ($t -ne "")) "D: weak master '$weak' warned, No keeps the create screen"
    $k++
  }
  SetText ([U]::GetDlgItem($m,101)) "1234"; SetText ([U]::GetDlgItem($m,102)) "1234"; Click 103
  $t = BoxTitleAndClose 6   # IDYES: the user insists
  Check TE05 $true ((Has 203) -and ($t -ne "")) "D: weak master accepted after Yes (advisory only)"
  Quit
  Remove-Item "$cfg\config.dat" -Force
  Launch $new
  SetText ([U]::GetDlgItem($m,101)) "Blue-Kettle-42"; SetText ([U]::GetDlgItem($m,102)) "Blue-Kettle-42"; Click 103
  Check TE06 $true ((Has 203) -and ([U]::FindBox([uint32]$p.Id) -eq [IntPtr]::Zero)) "D: strong master accepted without a warning"
  Quit

  # ---- F) T7: old builds must not open (or touch) a v3 file
  $k = 1
  foreach ($oe in $old, $mid) {
    Copy-Item "$cfg\good.dat" "$cfg\config.dat" -Force
    $h0 = Hash
    Launch $oe
    Check ("TF0$k") $true ((Has 101) -and -not (Has 102)) "F: $(Split-Path (Split-Path $oe) -Leaf) shows the unlock screen for a v3 file (not create)"; $k++
    SetText ([U]::GetDlgItem($m,101)) "Master1234"; Click 103
    $t = BoxTitleAndClose
    Check ("TF0$k") $true ((Has 101) -and ($t -ne "")) "F: right master is still rejected by the old build"; $k++
    Check ("TF0$k") $false (Has 203) "F: old build never reaches the list"; $k++
    Quit
    Check ("TF0$k") $h0 (Hash) "F: file unchanged after the old build ran"; $k++
  }

  # ---- G) T9: unsupported and corrupt headers
  $good = [IO.File]::ReadAllBytes("$cfg\good.dat")
  Copy-Item "$cfg\good.dat" "$cfg\config.dat" -Force
  $gh = ReadHeader
  $cases = @(
    @{ Id = 'TG01'; Kind = 'unsupported'; Text = ($gh -replace "(?m)^secretv=\d+$", "secretv=1"); Label = "secretv=1" },
    @{ Id = 'TG04'; Kind = 'unsupported'; Text = ($gh -replace "(?m)^v=2$", "v=9"); Label = "v=9" },
    @{ Id = 'TG07'; Kind = 'corrupt';     Text = ($gh -replace "(?m)^s1\.vk=", "s01.vk="); Label = "s01.vk" },
    @{ Id = 'TG10'; Kind = 'corrupt';     Text = ($gh -replace "(?m)^(autolock=\d+)$", "`$1`nautolock=0"); Label = "duplicate autolock" }
  )
  foreach ($c in $cases) {
    $n = [int]$c.Id.Substring(2)
    WriteHeader $c.Text
    $h0 = Hash
    Launch $new
    if ($c.Kind -eq 'unsupported') {
      Check $c.Id $true ((Has 104) -and -not (Has 101)) ("G: " + $c.Label + " -> 'new version needed' screen, no password field")
      Check ("TG{0:D2}" -f ($n + 1)) $false (Has 102) ("G: " + $c.Label + " -> no create-master screen")
    } else {
      $t = BoxTitleAndClose 1
      Check $c.Id $true ($t -ne "") ("G: " + $c.Label + " -> load-failure warning shown")
      Start-Sleep -Milliseconds 500
      Check ("TG{0:D2}" -f ($n + 1)) $true ((Has 101) -and -not (Has 203)) ("G: " + $c.Label + " -> not opened")
    }
    Quit
    Check ("TG{0:D2}" -f ($n + 2)) $h0 (Hash) ("G: " + $c.Label + " -> file unchanged")
  }
  # payload with one line more than there are slots (written by the test-only switch), then opened normally
  Remove-Item "$cfg\config.dat" -Force
  $env:ONEKEY_TEST_FAIL = "save:extra-payload"
  Launch $new
  SetText ([U]::GetDlgItem($m,101)) "Blue-Kettle-42"; SetText ([U]::GetDlgItem($m,102)) "Blue-Kettle-42"; Click 103
  Quit
  $env:ONEKEY_TEST_FAIL = $null
  $h0 = Hash
  Launch $new
  SetText ([U]::GetDlgItem($m,101)) "Blue-Kettle-42"; Click 103
  $t = BoxTitleAndClose
  Check TG13 $true ((Has 101) -and ($t -ne "")) "G: payload longer than the slot count is rejected at unlock"
  Check TG14 $false (Has 203) "G: payload excess never reaches the list"
  Quit
  Check TG15 $h0 (Hash) "G: payload excess -> file unchanged"
  [IO.File]::WriteAllBytes("$cfg\config.dat", $good)

  # ---- H) "no Enter in browsers": since 0.3.5x the third choice of the one Enter send row (305: 0 off, 1 press, 2 except browsers);
  # the separate switch (312) is gone. BM_SETCHECK / BM_GETCHECK on the row set / read the choice.
  Remove-Item "$cfg\config.dat" -Force
  Launch $new
  SetText ([U]::GetDlgItem($m,101)) "Master1234"; SetText ([U]::GetDlgItem($m,102)) "Master1234"; Click 103
  Click 203; Click 4001
  SetText ([U]::GetDlgItem($m,301)) "Web"; SetText ([U]::GetDlgItem($m,302)) "dummy-web"
  Check TH01 "0|False" ("$(EnterIdx)|$(Has 312)") "H: a new item starts with Enter send 'off'; no separate 'no Enter in browsers' switch"
  SetEnter 2
  Check TH02 "2" "$(EnterIdx)" "H: 'except browsers' chosen on the Enter row"
  Click 310; [void](BoxTitleAndClose)
  Quit
  $h5 = ReadHeader
  Check TH03 "True|True" ("$($h5 -match '(?m)^secretv=10$')|$($h5 -match '(?m)^s0\.noenterbrowser=1$')") "H: saved as secretv=10 with s0.noenterbrowser=1"
  Copy-Item "$cfg\config.dat" "$cfg\v5.dat" -Force
  Launch $new; Unlock; Click 1000
  Check TH04 "2" "$(EnterIdx)" "H: reopened: 'except browsers' kept"
  Quit

  # tampering with the flag or with the format number must make unlock fail
  foreach ($t in @(@('TH05', ($h5 -replace '(?m)^s0\.noenterbrowser=1$', 's0.noenterbrowser=0'), "flag flipped to 0"),
                   @('TH06', ($h5 -replace '(?m)^secretv=10$', 'secretv=5'), "secretv lowered to 5"))) {
    WriteHeader $t[1]
    Launch $new; Unlock; [void](BoxTitleAndClose)
    Check $t[0] "True|False" ("$(Has 101)|$(Has 203)") ("H: " + $t[2] + " -> unlock fails")
    Quit
  }

  # switching it off again removes the flag lines (the format stays 10)
  Copy-Item "$cfg\v5.dat" "$cfg\config.dat" -Force
  Launch $new; Unlock; Click 1000; SetEnter 1; Click 310; [void](BoxTitleAndClose); Quit
  $h3 = ReadHeader
  Check TH07 "True|False" ("$($h3 -match '(?m)^secretv=10$')|$($h3 -match '(?m)^s\d+\.(noenterbrowser=1|enterbrowser=)')") "H: option off -> secretv=10, every slot noenterbrowser=0 (format 5+ writes the line), no legacy enterbrowser line"
  # a flag slipped into a v10 file is covered by its AAD: it does not open, and the file is left as it was
  Copy-Item "$cfg\config.dat" "$cfg\v10off.dat" -Force
  WriteHeader ($h3 -replace '(?m)^(s0\.enter=1)$', "`$1`ns0.noenterbrowser=1")
  $h0 = Hash
  Launch $new; [void](BoxTitleAndClose 1); Unlock; [void](BoxTitleAndClose)
  Check TH08 "False" "$(Has 203)" "H: v10 file with an injected noenterbrowser line is refused (header bound by AAD)"
  Quit
  Check TH09 $h0 (Hash) "H: ... and the tampered file is left unchanged"
  Copy-Item "$cfg\v10off.dat" "$cfg\config.dat" -Force

  # older builds meet a v5 file: 'new version needed', nothing written
  foreach ($t in @(@('TH10', $prev, "0.2.38"), @('TH12', $v40, "0.2.40"))) {
    Copy-Item "$cfg\v5.dat" "$cfg\config.dat" -Force
    $h0 = Hash
    Launch $t[1]
    Check $t[0] "True|False" ("$(Has 104)|$(Has 101)") ("H: " + $t[2] + " shows 'new version needed' for a v5 file")
    Quit
    Check ("TH{0:D2}" -f ([int]$t[0].Substring(2) + 1)) $h0 (Hash) ("H: " + $t[2] + " left the v5 file unchanged")
  }

  # 'except browsers' -> 'off': saved as off (format 10)
  Copy-Item "$cfg\v5.dat" "$cfg\config.dat" -Force
  Launch $new; Unlock; Click 1000; SetEnter 0
  Check TH14 "0" "$(EnterIdx)" "H: Enter row set from 'except browsers' to 'off'"
  Click 310; [void](BoxTitleAndClose); Quit
  $hx = ReadHeader
  Check TH15 "True|True" ("$($hx -match '(?m)^secretv=10$')|$($hx -match '(?m)^s0\.enter=0$')") "H: saved with Enter off -> secretv=10, enter=0"

  # a secretv=4 file from 0.2.40: slot 0 Enter + 'also in browsers', slot 1 Enter only
  Remove-Item "$cfg\config.dat" -Force
  Launch $v40
  SetText ([U]::GetDlgItem($m,101)) "Master1234"; SetText ([U]::GetDlgItem($m,102)) "Master1234"; Click 103
  Click 203; Click 4001; SetText ([U]::GetDlgItem($m,301)) "Web4"; SetText ([U]::GetDlgItem($m,302)) "dummy-web4"; Flip 305; Flip 312; Click 310; [void](BoxTitleAndClose)
  Click 203; Click 4001; SetText ([U]::GetDlgItem($m,301)) "App4"; SetText ([U]::GetDlgItem($m,302)) "dummy-app4"; Flip 305; Click 310; [void](BoxTitleAndClose)
  Quit
  $h4 = ReadHeader
  Check TH16 "True|True" ("$($h4 -match '(?m)^secretv=4$')|$($h4 -match '(?m)^s0\.enterbrowser=1$')") "H: 0.2.40 wrote a secretv=4 file (setup)"
  Copy-Item "$cfg\config.dat" "$cfg\v4.dat" -Force
  Launch $new; Unlock
  $opened = Has 203
  Quit
  $hm = ReadHeader
  Check TH17 "True|True|False" ("$opened|$($hm -match '(?m)^secretv=10$')|$($hm -match '(?m)^s\d+\.(noenterbrowser=1|enterbrowser=)')") "H: the v4 file opens and is moved to secretv=10 on unlock (no legacy enterbrowser line, no noenterbrowser=1)"
  Launch $new; Unlock; Click 1000; $a = "$(EnterIdx)"; Click 311; Click 1001; $b = "$(EnterIdx)"
  Check TH18 "1/1" "$a/$b" "H: both Enter items now send in browsers (the new default), 'no Enter in browsers' off"
  Quit

  # the move to secretv=10 cannot be written (config.dat read-only): the v4 file still opens, stays v4, and after
  # locking it unlocks again (the old values needed for its check are kept until a save succeeds)
  Copy-Item "$cfg\v4.dat" "$cfg\config.dat" -Force
  Set-ItemProperty "$cfg\config.dat" -Name IsReadOnly -Value $true
  Launch $new; Unlock
  $opened = Has 203
  $hr = ReadHeader
  Check TH19 "True|True" ("$opened|$($hr -match '(?m)^secretv=4$')") "H: re-save fails (file read-only): the v4 file still opens and stays v4"
  Click 2014; Unlock
  Check TH20 $true (Has 203) "H: after locking, the same v4 file unlocks again"
  Quit
  Set-ItemProperty "$cfg\config.dat" -Name IsReadOnly -Value $false
  Remove-Item "$cfg\config.dat.tmp" -Force -ErrorAction Ignore
  Launch $new; Unlock; Quit
  $hw = ReadHeader
  Check TH21 "True|False" ("$($hw -match '(?m)^secretv=10$')|$($hw -match '(?m)^s\d+\.(noenterbrowser=1|enterbrowser=)')") "H: once the file can be written, the next unlock moves it to secretv=10 (no legacy enterbrowser line, no noenterbrowser=1)"

  # ---- J) newer-format files given to older builds: first the real format-10 file to 0.3.32, then secretv=7 to 0.2.x
  Copy-Item "$cfg\config.dat" "$cfg\v10.dat" -Force
  Remove-Item "$cfg\config.*.bak" -Force -ErrorAction Ignore
  $v332 = "$repo\build\0.3.32\1Key.exe"
  if (-not (Test-Path $v332)) { throw "exe not found: $v332" }
  $h0 = Hash
  Launch $v332
  Check TJ07 "True|False|False" ("$(Has 104)|$(Has 101)|$(Has 102)") "J: 0.3.32 shows 'new version needed' for a real format-10 file (no unlock, no create screen)"
  Quit
  Check TJ08 "$h0|0" ("$(Hash)|$(@(Get-ChildItem "$cfg\config.*.bak" -ErrorAction Ignore).Count)") "J: 0.3.32 left the format-10 file unchanged, no .bak"
  WriteHeader ($hw -replace '(?m)^secretv=10$', 'secretv=7')
  Remove-Item "$cfg\config.*.bak" -Force -ErrorAction Ignore
  $v21 = "$repo\build\0.2.21\1Key.exe"; $v55 = "$repo\build\0.2.55\1Key.exe"
  foreach ($e in $v21, $v55) { if (-not (Test-Path $e)) { throw "exe not found: $e" } }
  $h0 = Hash
  Launch $v21
  Unlock; Start-Sleep -Milliseconds 800; [void](BoxTitleAndClose)
  $r21 = "$(Has 203)|$(Has 102)"
  Quit
  Check TJ01 "False|False" $r21 "J: 0.2.21 with a secretv=7 file: not unlocked, no create screen"
  Check TJ02 $h0 (Hash) "J: 0.2.21 left the file unchanged"
  foreach ($t in @(@('TJ03', $v40, "0.2.40"), @('TJ05', $v55, "0.2.55"))) {
    Launch $t[1]
    Check $t[0] "True|False" ("$(Has 104)|$(Has 101)") ("J: " + $t[2] + " shows 'new version needed' for a secretv=7 file")
    Quit
    Check ("TJ{0:D2}" -f ([int]$t[0].Substring(2) + 1)) "$h0|0" ("$(Hash)|$(@(Get-ChildItem "$cfg\config.*.bak" -ErrorAction Ignore).Count)") ("J: " + $t[2] + " left the file unchanged, no .bak")
  }
} catch { Add-Failure ("exception: " + $_) }
finally { Stop-TestInstances $suffix }
Complete-Checks
