# Many-slot list test: register 12 items through the edit screen, then check
#  - only VisibleRows (8) row controls exist, a search field (230) appears
#  - mouse wheel scrolls the virtual list, the scroll track is drawn (checked indirectly via row width)
#  - typing in the search field filters rows by name
#  - the "add" button (203, list header since 0.2.158) stays outside the list; row click (1000+i) opens the edit screen of slot i
#  - T15: deleting slot 4 leaves every other slot at its index; the next "add" reuses index 4; both survive a relaunch
#  - T14: while searching, "n of m" count (231) and a clear button (232); zero results also show the count
#  - T11 (input chip): each row has an [input] button (1100+i), disabled for an item without content; the chip is a
#    non-activating TOPMOST tool window; cancel / lock / reopen / expiry each remove it and release the confirm key.
#    The chip's [input] is NEVER clicked here: it would type into whatever window the user has active. That path is
#    covered by the explicit foreground harness (T10). The confirm key is set to bare F24 first so this test never holds
#    Ctrl+Alt+Enter while the user may be typing.
# Judgement: tools\tests\lib\Check.ps1 (T18). ASCII only.
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
Start-Checks -Required @(
  'MA01','MA02','MA03','MA04','MA05','MA06','MA07',
  'MB01','MB02','MB03','MB04',
  'MC01','MC02','MC03','MC04','MC05','MC06',
  'MD01','MD02',
  'ME01','ME02','ME03','ME04','ME05','ME06',
  'MF01','MF02','MF03','MF04',
  'MH01','MH02','MH03','MH04','MH05','MH06','MH07',
  'MG00','MG01','MG02','MG03','MG04','MG05','MG06','MG07','MG08','MG09','MG10','MG11','MG12','MG13','MG14','MG15','MG16','MG17','MG18','MG19','MG20','MG21','MG22')

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($args.Count -gt 0) { $args[0] } else { Get-DefaultExe }
$suffix = ".many"
$cfg = "$sp\many_cfg"; if (Test-Path $cfg) { Get-ChildItem $cfg -Recurse | Remove-Item -Force -Recurse -ErrorAction Ignore }; New-Item -ItemType Directory -Force $cfg | Out-Null
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
  [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int hh, uint f);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static string Title(IntPtr h) { var t = new StringBuilder(256); GetWindowTextW(h, t, 256); return t.ToString(); }
  public static IntPtr FindCls(uint pid, string cls) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
  public static IntPtr FindBox(uint pid) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && (Cls(h) == "#32770" || Cls(h) == "OneKeyDialog") && IsWindowVisible(h)) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
  public static int Width(IntPtr h) { RECT r; GetWindowRect(h, out r); return r.R - r.L; }
  public static int Right(IntPtr h) { RECT r; GetWindowRect(h, out r); return r.R; }
  [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern IntPtr GetWindowLongPtrW(IntPtr h, int i);
  [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
  [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
  public static bool HeldByOther(uint mods, uint vk) { if (RegisterHotKey(IntPtr.Zero, 4243, mods | 0x4000, vk)) { UnregisterHotKey(IntPtr.Zero, 4243); return false; } return true; }
  public static string AllChildText(IntPtr parent) { if (parent == IntPtr.Zero) return "<no window>"; var sb = new StringBuilder(); EnumChildWindows(parent, (h,l) => { var t = new StringBuilder(512); GetWindowTextW(h, t, 512); sb.Append(t.ToString()).Append('|'); return true; }, IntPtr.Zero); return sb.ToString(); }
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, CB cb, IntPtr l);
}
'@
function SetText($h, $s) { [void][U]::SendMessageW($h, 0x000C, [IntPtr]::Zero, [string]$s) }
function Click($id) { [void][U]::PostMessageW([U]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 450 }
function Has($id) { [U]::GetDlgItem($m, $id) -ne [IntPtr]::Zero }
function EditText($id) { $sb = New-Object System.Text.StringBuilder 512; [void][U]::SendMessageW([U]::GetDlgItem($m, $id), 0x000D, [IntPtr]512, $sb); $sb.ToString() }
function CloseBox($cmd = 2) { for ($i=0;$i -lt 10;$i++) { $b = [U]::FindBox([uint32]$p.Id); if ($b -ne [IntPtr]::Zero) { [void][U]::PostMessageW($b, 0x0111, [IntPtr]$cmd, [IntPtr]::Zero); Start-Sleep -Milliseconds 300; return }; Start-Sleep -Milliseconds 100 } }
function VisibleSlotRows() { $n = 0; for ($i = 0; $i -lt 99; $i++) { if (Has (1000 + $i)) { $n++ } }; $n }
function FirstVisibleSlot() { for ($i = 0; $i -lt 99; $i++) { if (Has (1000 + $i)) { return $i } }; -1 }
function Wheel([int]$notches) { [void][U]::PostMessageW($m, 0x020A, [IntPtr](($notches * 120 * 65536) -band 0xFFFFFFFF), [IntPtr]::Zero); Start-Sleep -Milliseconds 300 }
# name shown in the edit screen of slot $i (opens it and goes back); scrolls so that row is visible first
function NameAt($i) {
  Wheel 20   # to the top
  for ($k = 0; $k -lt 12 -and -not (Has (1000 + $i)); $k++) { Wheel -1 }
  if (-not (Has (1000 + $i))) { return "<row $i not visible>" }
  Click (1000 + $i); $n = EditText 301; Click 311; $n
}
function Launch() {
  $script:p = Start-Process $exe -PassThru
  $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $script:m = [U]::FindCls([uint32]$script:p.Id, "OneKeyMainWindow$suffix"); if ($script:m -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 100 }
  if ($script:m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 600
  [void][U]::SetWindowPos($script:m, [IntPtr]::Zero, 1400, 60, 0, 0, 0x0001 -bor 0x0004 -bor 0x0010)
}
function Quit() { [void][U]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i=0; $i -lt 50 -and -not $p.HasExited; $i++) { Start-Sleep -Milliseconds 100 } }

try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_CHIP_MS = "3000"; $env:ONEKEY_TEST_WORKAREA = $TallScreen
  Launch
  SetText ([U]::GetDlgItem($m,101)) "Master1234"; SetText ([U]::GetDlgItem($m,102)) "Master1234"; Click 103
  # auto-lock off first: auto-lock reads the SYSTEM idle time (posted messages are not input), so on a PC nobody has
  # touched for 10 minutes the test instance locks in the middle (2026-09-29: bounce/many/master/help failed that way
  # in one full run and passed on the rerun). Settings > auto-lock slider Home = off, save (toast), wait for it to go.
  Click 220; [void][U]::PostMessageW([U]::GetDlgItem($m, 2005), 0x0100, [IntPtr]0x24, [IntPtr]::Zero); Start-Sleep -Milliseconds 300
  Click 2012; Start-Sleep -Milliseconds 3500
  Confirm-AutoLockOff $cfg
  Check MA01 $true (Has 203) "list screen (add row 203)"
  Check MA02 $false (Has 230) "no search field with an empty list"

  # register 12 items: Slot 01 .. Slot 12 (no hotkeys, so nothing global is registered)
  for ($k = 1; $k -le 12; $k++) {
    Click 203; Click 4001
    if (-not (Has 301)) { throw "edit screen did not open for item $k" }
    SetText ([U]::GetDlgItem($m,301)) ("Slot {0:D2}" -f $k); SetText ([U]::GetDlgItem($m,302)) "x$k"
    Click 310; CloseBox
  }
  Check MA03 8 (VisibleSlotRows) "12 registered, 8 row controls exist (virtual list)"
  Check MA04 $true (Has 230) "search field appears when more than 8 items"
  Check MA05 0 (FirstVisibleSlot) "first visible row is slot 0"
  Check MA06 $true (Has 203) "add row stays outside the list"
  # since 0.2.158 the add button (203) sits in the header and is small, so the reference is the card width (CardW 388 at 96 DPI, $TallScreen)
  $rowW = [U]::Width([U]::GetDlgItem($m, 1000)); $cardW = 388
  Check MA07 $true ($rowW -lt $cardW) "list rows are narrower than the card (room for the input button and scroll track): $rowW < $cardW"
  $btnRightScrolled = [U]::Right([U]::GetDlgItem($m, 1100))

  # wheel
  Wheel -1; Wheel -1
  Check MB01 2 (FirstVisibleSlot) "after wheel down x2 the first visible row is slot 2"
  Check MB02 8 (VisibleSlotRows) "still 8 row controls after scrolling"
  Wheel 5
  Check MB03 0 (FirstVisibleSlot) "wheel up clamps back to slot 0"
  Wheel -20
  Check MB04 4 (FirstVisibleSlot) "large wheel down clamps at 12 - 8 = 4"

  # filter: "Slot 1" matches Slot 10, 11, 12 -> exactly 3 rows, no scroll (full width)
  SetText ([U]::GetDlgItem($m,230)) "Slot 1"; Start-Sleep -Milliseconds 500
  Check MC01 3 (VisibleSlotRows) "filter 'Slot 1' shows 3 rows"
  Check MC02 $true ((Has 1009) -and (Has 1010) -and (Has 1011)) "filtered rows are slots 9,10,11 (Slot 10..12)"
  $btnRightFit = [U]::Right([U]::GetDlgItem($m, 1109))
  # since 0.5.2 the scroll track sits in the dial's right padding: rows keep their width whether or not the list scrolls
  Check MC03 $true ($btnRightFit -ge $btnRightScrolled) "rows keep their width when everything fits (input button right: $btnRightFit >= $btnRightScrolled)"
  Check MC04 $true (Has 230) "search field survives the rebuild"
  Check MH01 $true ((EditText 231) -match "^12\D+3\D*$") ("count shows 12 total / 3 shown: '" + (EditText 231) + "'")
  Check MH02 $true (Has 232) "clear button present while searching"
  SetText ([U]::GetDlgItem($m,230)) "zzz"; Start-Sleep -Milliseconds 500
  Check MC05 0 (VisibleSlotRows) "filter with no match shows no rows"
  Check MH03 $true ((EditText 231) -match "^12\D+0\D*$") ("zero results still show the count: '" + (EditText 231) + "'")
  Click 232
  Check MH04 "" (EditText 230) "clear button empties the search field"
  Check MH05 8 (VisibleSlotRows) "clear button restores the list"
  Check MH06 $false (Has 231) "no count label without a search"
  Check MH07 $false (Has 232) "no clear button without a search"
  SetText ([U]::GetDlgItem($m,230)) ""; Start-Sleep -Milliseconds 500
  Check MC06 8 (VisibleSlotRows) "empty filter keeps the full list"

  # row click opens the right slot
  Wheel -20; Wheel 20
  Click 1007
  Check MD01 "Slot 08" (EditText 301) "row 1007 opens the edit screen of Slot 08"
  Click 311
  Check MD02 $true (Has 203) "back to the list"

  # T15: delete slot 4 (Slot 05); all other indexes stay; the next add reuses index 4
  Click 1004
  Check ME01 "Slot 05" (EditText 301) "row 1004 is Slot 05 before the delete"
  Click 308; CloseBox 6   # IDYES
  Start-Sleep -Milliseconds 300
  Check ME02 $false (Has 1004) "after delete, no row for slot 4"
  Check ME03 "Slot 06" (NameAt 5) "slot 5 is still Slot 06 (no renumbering)"
  Check ME04 "Slot 12" (NameAt 11) "slot 11 is still Slot 12"
  Click 203; Click 4001
  SetText ([U]::GetDlgItem($m,301)) "Slot NEW"; SetText ([U]::GetDlgItem($m,302)) "xnew"
  Click 310; CloseBox
  Check ME05 "Slot NEW" (NameAt 4) "the next add reuses the freed index 4"
  Check ME06 "Slot 04" (NameAt 3) "slot 3 is untouched"

  # persistence: quit, relaunch, unlock -> 12 items at the same indexes
  Quit
  Launch
  SetText ([U]::GetDlgItem($m,101)) "Master1234"; Click 103
  Check MF01 $true ((Has 230) -and ((VisibleSlotRows) -eq 8)) "after relaunch: 12 items reloaded (search field + 8 rows)"
  Check MF02 "Slot 12" (NameAt 11) "slot 11 (Slot 12) survived the s11.* header round-trip"
  Check MF03 "Slot NEW" (NameAt 4) "slot 4 (reused) survived the relaunch"
  Check MF04 "Slot 06" (NameAt 5) "slot 5 survived the relaunch"

  # ---- T11 input chip
  # confirm key -> bare F24 through the settings UI (never Ctrl+Alt+Enter in this test)
  Click 220; Start-Sleep -Milliseconds 400
  [void][U]::PostMessageW([U]::GetDlgItem($m, 2006), 0x0100, [IntPtr]0x87, [IntPtr]::Zero); Start-Sleep -Milliseconds 200
  Click 2012; CloseBox   # saving returns to the list
  Wheel 20
  Check MG00 $false ([U]::HeldByOther(0, 0x87)) "confirm key F24 is not held while no chip is shown"
  Check MG01 $true ((Has 1100) -and [U]::IsWindowEnabled([U]::GetDlgItem($m, 1100))) "row 0 has an enabled [input] button (1100)"
  Click 1100; Start-Sleep -Milliseconds 300
  $chip = [U]::FindCls([uint32]$p.Id, "OneKeyChip")
  Check MG02 $true ($chip -ne [IntPtr]::Zero) "chip window appears"
  Check MG03 $false ([U]::IsWindowVisible($m)) "main window is hidden while the chip waits"
  Check MG04 $true ([U]::HeldByOther(0, 0x87)) ("confirm key (F24) is registered while the chip waits (diagnostics: dialog open=" + ([U]::FindBox([uint32]$p.Id) -ne [IntPtr]::Zero) + ", chip=" + ([U]::FindCls([uint32]$p.Id, "OneKeyChip") -ne [IntPtr]::Zero) + ", app alive=" + (-not $p.HasExited) + ")")
  $ex = [int64][U]::GetWindowLongPtrW($chip, -20)
  Check MG05 $true ((($ex -band 0x08000000) -ne 0) -and (($ex -band 0x80) -ne 0) -and (($ex -band 0x8) -ne 0)) ("chip is NOACTIVATE | TOOLWINDOW | TOPMOST (ex=0x{0:X})" -f $ex)
  Check MG06 $false ([U]::GetForegroundWindow() -eq $chip) "chip is not the foreground window"
  $chipText = [U]::AllChildText($chip)
  Check MG07 $true ($chipText.StartsWith("Slot 01|")) ("chip shows the item name: '$chipText'")
  Check MG08 $false ($chipText.Contains("x1|") -or $chipText.Contains("x1 ")) "chip does not show the content"
  [void][U]::PostMessageW([U]::GetDlgItem($chip, 12), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 500
  Check MG09 $true ([U]::FindCls([uint32]$p.Id, "OneKeyChip") -eq [IntPtr]::Zero) "cancel removes the chip"
  Check MG10 $true ([U]::IsWindowVisible($m)) "cancel brings the list back"
  Check MG11 $false ([U]::HeldByOther(0, 0x87)) "cancel releases the confirm key"

  # lock (Windows session lock message) releases the chip
  Click 1100; Start-Sleep -Milliseconds 300
  [void][U]::PostMessageW($m, 0x02B1, [IntPtr]7, [IntPtr]::Zero); Start-Sleep -Milliseconds 500
  Check MG12 $true ([U]::FindCls([uint32]$p.Id, "OneKeyChip") -eq [IntPtr]::Zero) "session lock removes the chip"
  Check MG13 $false ([U]::HeldByOther(0, 0x87)) "session lock releases the confirm key"
  Check MG14 $true (Has 101) "session lock shows the lock screen"
  SetText ([U]::GetDlgItem($m,101)) "Master1234"; Click 103

  # reopening the main window (second launch / tray) releases the chip
  Click 1100; Start-Sleep -Milliseconds 300
  [void][U]::PostMessageW($m, 0x8003, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 500
  Check MG15 $true ([U]::FindCls([uint32]$p.Id, "OneKeyChip") -eq [IntPtr]::Zero) "reopening the main window removes the chip"
  Check MG16 $false ([U]::HeldByOther(0, 0x87)) "reopening releases the confirm key"

  # expiry (ONEKEY_TEST_CHIP_MS=3000 instead of 60 s)
  Click 1100; Start-Sleep -Milliseconds 300
  Check MG17 $true ([U]::FindCls([uint32]$p.Id, "OneKeyChip") -ne [IntPtr]::Zero) "chip shown again for the expiry check"
  Start-Sleep -Milliseconds 3800
  Check MG18 $true ([U]::FindCls([uint32]$p.Id, "OneKeyChip") -eq [IntPtr]::Zero) "chip expires by itself"
  Check MG19 $false ([U]::HeldByOther(0, 0x87)) "expiry releases the confirm key"
  [void][U]::PostMessageW($m, 0x8003, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 400

  # tray menu item (same command id 3100+i): starts the chip instead of typing at once (0.2.35)
  [void][U]::PostMessageW($m, 0x0111, [IntPtr]3100, [IntPtr]::Zero); Start-Sleep -Milliseconds 500
  $chip = [U]::FindCls([uint32]$p.Id, "OneKeyChip")
  Check MG22 $true ($chip -ne [IntPtr]::Zero) "tray item starts the input chip"
  if ($chip -ne [IntPtr]::Zero) { [void][U]::PostMessageW([U]::GetDlgItem($chip, 12), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 500 }

  # an item with a name but no content: its [input] button is disabled
  Click 203; Click 4001
  SetText ([U]::GetDlgItem($m,301)) "Name Only"
  Click 310; CloseBox
  Check MG20 "Name Only" (NameAt 12) "the name-only item went to the first free index 12"
  Check MG21 $true ((Has 1112) -and -not [U]::IsWindowEnabled([U]::GetDlgItem($m, 1112))) "[input] (1112) is disabled for an item without content"
  Quit
} catch { Add-Failure ("exception: " + $_) }
finally { Stop-TestInstances $suffix }
Complete-Checks
