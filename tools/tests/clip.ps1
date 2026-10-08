# Clipboard failure/restore test (T17, Codex V35-1 / V36-1 / V37 / V38 / V39). Runs "1Key.exe --selftest-clip <dummy>" in test
# mode: the app swaps a dummy secret into the clipboard and restores it, WITHOUT sending any keystroke to any window.
# Failures are injected with ONEKEY_TEST_FAIL = clip:read|mark|set|token|restore, simulated other programs
# (clip:ext-copy / clip:ext-empty right after our close, clip:busy-then-ext during the restore retries, clip:ext-fake-owner
# = same owner format with another token), owner-mark read failures (clip:owner-read|owner-lock|owner-lock-once|owner-size0),
# interference with the harness itself (clip:intrude-first|intrude-restore), a third program writing right before one of
# the app's simulations (clip:foreign-before-sim), and failures of the test machinery (clip:sim-fail|testmark-once).
#
# This test uses the real Windows clipboard for a few seconds. Run it when nobody is using the PC.
#  - If the clipboard holds anything besides plain text (picture, files, HTML or rich text next to the text) it refuses to
#    run and changes nothing, because only the text could be put back.
#  - OWNERSHIP (Codex V38-1): every write during the run - the harness's and the app's (the app gets the marker through
#    ONEKEY_TEST_CLIP_RUN) - also puts a random 16-byte RUN MARKER (format "1Key.TestRun", new for each run). Another
#    program's copy or clear empties the clipboard and so removes the marker. Before every write, inside ONE
#    OpenClipboard session, the harness checks that the marker is still this run's AND that the sequence number is the one
#    last seen; the app checks the marker when it first opens the clipboard (else it changes nothing and exits 6). On any
#    mismatch the run stops, leaves the clipboard as it is and fails as "DISTURBED". The first write (over the user's
#    text) is checked by the sequence number seen while saving that text.
#  - The app's own simulations of other programs (clip:ext-*, intrude-*) also check the run marker in the same open
#    session before emptying (Codex V39-1); if it is gone they do nothing and the app exits 6. If a simulation or a
#    run-marker write fails, the app TRIES to put the dummy secret back and exits 7 (a test error, not interference) with
#    the restore result in the message ("SimulationFailed; restore=Failed"); the restore is not guaranteed (Codex V40).
#  - What this protects against: ordinary copies and clears by other programs (they empty the clipboard and so remove
#    the marker), at any time during the run. NOT covered: a program that changes formats WITHOUT emptying the
#    clipboard (the marker survives). The sequence number catches that only where it is known and compared - the
#    harness's writes while the number is known; not the app's first open (marker only), not the writes after a -5
#    failure or when taking the clipboard back after a simulated intrusion (marker only), and not the instant between a
#    CloseClipboard and the following number read. Full isolation needs a separate Windows session.
#  - WRITES ARE CHECKED (Codex V38-2): memory is prepared before the clipboard is opened; emptying and every format write
#    are checked. A write that fails after emptying empties again and leaves only the run marker (never text without its
#    history/cloud flags), is reported with its code and stops the run. The final restore says "put back" only when the
#    text write succeeded.
#  - Self checks of the harness: deterministic interference (CL35-CL44) with an INTRUDER marker, so the simulated other
#    program is recognised and the run can take the clipboard back; injected write failures (CL45-CL58); a third program
#    (FOREIGN marker, accepted by nothing but the explicit take-back at the end of its scenario) right before the app's
#    simulations, and failures of the simulation / run marker (CL59-CL68).
# Dummy strings the harness writes, and the restored user text, are marked "not for clipboard history / cloud".
# Judgement: tools\tests\lib\Check.ps1 (T18). Contains Korean: keep UTF-8 WITH BOM.
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
$req = @(); for ($i = 1; $i -le 72; $i++) { $req += ("CL{0:D2}" -f $i) }
Start-Checks -Required $req

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($args.Count -gt 0) { $args[0] } else { Get-DefaultExe }
$cfg = "$sp\clip_cfg"; New-Item -ItemType Directory -Force $cfg | Out-Null
Add-Type -AssemblyName System.Windows.Forms
Add-Type -ReferencedAssemblies System.Windows.Forms @'
using System;using System.Runtime.InteropServices;
public class ClipH {
  [DllImport("user32.dll")] static extern bool OpenClipboard(IntPtr h); [DllImport("user32.dll")] static extern bool CloseClipboard();
  [DllImport("user32.dll")] static extern bool EmptyClipboard(); [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
  [DllImport("user32.dll")] static extern IntPtr SetClipboardData(uint f, IntPtr h); [DllImport("user32.dll")] static extern IntPtr GetClipboardData(uint f);
  [DllImport("user32.dll")] static extern bool IsClipboardFormatAvailable(uint f);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern uint RegisterClipboardFormatW(string n);
  [DllImport("kernel32.dll")] static extern IntPtr GlobalAlloc(uint f, UIntPtr n); [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr h);
  [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr h); [DllImport("kernel32.dll")] static extern IntPtr GlobalFree(IntPtr h);
  [DllImport("kernel32.dll")] static extern UIntPtr GlobalSize(IntPtr h);

  // Test injection for the harness's own write path (V38-2): "" (none), alloc, empty, marker, flags, text
  public static string FailAt = "";
  public static string LastReason = "";

  static System.Windows.Forms.NativeWindow _owner;
  // a message-only owner window: with owner NULL, EmptyClipboard makes SetClipboardData fail (documented)
  static IntPtr Owner() { if (_owner == null) { _owner = new System.Windows.Forms.NativeWindow(); var cp = new System.Windows.Forms.CreateParams(); cp.Parent = (IntPtr)(-3); _owner.CreateHandle(cp); } return _owner.Handle; }
  static uint Fmt(string n) { return RegisterClipboardFormatW(n); }
  static IntPtr Mem(byte[] b) {
    IntPtr h = GlobalAlloc(2, (UIntPtr)b.Length); if (h == IntPtr.Zero) return h;
    IntPtr p = GlobalLock(h); if (p == IntPtr.Zero) { GlobalFree(h); return IntPtr.Zero; }
    Marshal.Copy(b, 0, p, b.Length); GlobalUnlock(h); return h;
  }
  static void Free(IntPtr h) { if (h != IntPtr.Zero) GlobalFree(h); }
  // one format write; on success the system owns the memory
  static bool Put(uint f, ref IntPtr h, string step) {
    if (FailAt == step || f == 0) return false;
    if (SetClipboardData(f, h) == IntPtr.Zero) return false;
    h = IntPtr.Zero; return true;
  }
  static bool OpenRetry() { IntPtr o = Owner(); for (int i = 0; i < 40; i++) { if (OpenClipboard(o)) return true; System.Threading.Thread.Sleep(50); } return false; }
  static bool Same(byte[] a, byte[] b) { if (a == null || b == null || a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
  // the run marker on the OPEN clipboard, or null
  static byte[] ReadMarker() {
    uint f = Fmt("1Key.TestRun"); if (f == 0 || !IsClipboardFormatAvailable(f)) return null;
    IntPtr h = GetClipboardData(f); if (h == IntPtr.Zero || GlobalSize(h).ToUInt64() < 16) return null;
    IntPtr p = GlobalLock(h); if (p == IntPtr.Zero) return null;
    byte[] b = new byte[16]; Marshal.Copy(p, b, 0, 16); GlobalUnlock(h); return b;
  }

  // Marker on the clipboard: 0 none, 1 this run's, 2 the intruder's, 3 another one, -1 could not open
  public static int MarkerState(byte[] run, byte[] intruder) {
    if (!OpenRetry()) return -1;
    try { byte[] m = ReadMarker(); return m == null ? 0 : Same(m, run) ? 1 : Same(m, intruder) ? 2 : 3; }
    finally { CloseClipboard(); }
  }

  // Checked write, all inside one open session.
  //   run         this run's marker (checked when checkMarker; also what is left behind after a failed write)
  //   alsoAccept  a second marker accepted by the check (the intruder's, only when taking the clipboard back), or null
  //   expectedSeq the sequence number last seen, or -1 when not known
  //   hasText/text the text to write (with CanIncludeInClipboardHistory=0 / CanUploadToCloudClipboard=0, flags first)
  //   writeMarker the marker to write with it, or null (final restore of the user's text)
  // Returns the sequence number after closing (>= 0), or
  //   -1 could not open (nothing changed), -2 not ours: marker or sequence number differ (nothing changed),
  //   -3 could not prepare memory (nothing changed), -4 EmptyClipboard failed (nothing changed as far as Windows says),
  //   -5 a write failed after emptying: emptied again, only this run's marker left,
  //   -6 a write failed after emptying and leaving the marker failed too: state unknown.
  public static long Write(byte[] run, byte[] alsoAccept, bool checkMarker, long expectedSeq, bool hasText, string text, byte[] writeMarker) {
    IntPtr hMark = IntPtr.Zero, hTxt = IntPtr.Zero, hF1 = IntPtr.Zero, hF2 = IntPtr.Zero, hRescue = IntPtr.Zero;
    try {
      bool ready = FailAt != "alloc";
      if (ready) { hRescue = Mem(run); ready = hRescue != IntPtr.Zero; }
      if (ready && writeMarker != null) { hMark = Mem(writeMarker); ready = hMark != IntPtr.Zero; }
      if (ready && hasText) {
        hTxt = Mem(System.Text.Encoding.Unicode.GetBytes(text + (char)0)); hF1 = Mem(new byte[4]); hF2 = Mem(new byte[4]);
        ready = hTxt != IntPtr.Zero && hF1 != IntPtr.Zero && hF2 != IntPtr.Zero;
      }
      if (!ready) { LastReason = "could not prepare memory; nothing changed"; return -3; }
      if (!OpenRetry()) { LastReason = "could not open the clipboard; nothing changed"; return -1; }
      try {
        if (expectedSeq >= 0 && GetClipboardSequenceNumber() != (uint)expectedSeq) { LastReason = "the sequence number moved"; return -2; }
        if (checkMarker) {
          byte[] m = ReadMarker();
          if (!(Same(m, run) || Same(m, alsoAccept))) { LastReason = m == null ? "the run marker is gone" : "another marker is there"; return -2; }
        }
        if (FailAt == "empty" || !EmptyClipboard()) { LastReason = "EmptyClipboard failed; nothing changed"; return -4; }
        bool ok = true;
        if (writeMarker != null) ok = Put(Fmt("1Key.TestRun"), ref hMark, "marker");
        if (ok && hasText) ok = Put(Fmt("CanIncludeInClipboardHistory"), ref hF1, "flags") && Put(Fmt("CanUploadToCloudClipboard"), ref hF2, "flags") && Put(13, ref hTxt, "text");
        if (!ok) {
          // never leave half a write (e.g. text without its history flags): empty again and leave only this run's marker
          bool left = EmptyClipboard() && SetClipboardData(Fmt("1Key.TestRun"), hRescue) != IntPtr.Zero;
          if (left) hRescue = IntPtr.Zero;
          LastReason = left ? "a write failed after emptying; emptied again, only the run marker left" : "a write failed after emptying and the run marker could not be left; state unknown";
          return left ? -5 : -6;
        }
      } finally { CloseClipboard(); }
      LastReason = "";
      return GetClipboardSequenceNumber();
    } finally { Free(hMark); Free(hTxt); Free(hF1); Free(hF2); Free(hRescue); }
  }
}
'@

$Before = "1Key-test-clipboard-before"
$Secret = "1Key-test-dummy-secret"
$External = "1Key-test-external"   # what the app writes when it simulates another program (clip:ext-*)
$Intruder = "1Key-test-intruder"   # what a simulated interfering program writes (clip:intrude-*, harness-level intrusion)
$Foreign = "1Key-test-foreign"     # what the simulated third program writes (clip:foreign-before-sim)
function NewMarker() { $b = New-Object byte[] 16; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); ,$b }
$Run = NewMarker; $IntrMark = NewMarker; $ForeignMark = NewMarker
function Hex($b) { -join ($b | ForEach-Object { $_.ToString("X2") }) }
$RunHex = Hex $Run; $IntrHex = Hex $IntrMark; $ForeignHex = Hex $ForeignMark

$script:Seq = [long]-1     # sequence number last seen after our (or the app's) write; -1 = not known
$script:Ours = $false      # the clipboard holds something this run wrote (so the final restore may run)
function Disturbed([string]$where) { $script:Ours = $false; throw "DISTURBED: someone else changed the clipboard ($($where): $([ClipH]::LastReason)); the test stopped and left it as it is" }
# act on a Write result: remember the number, or stop the run
function Took([long]$r, [string]$what) {
  if ($r -ge 0) { $script:Seq = $r; $script:Ours = $true; return }
  if ($r -eq -2) { Disturbed $what }
  if ($r -eq -5) { $script:Ours = $true; $script:Seq = -1 }   # emptied by us, marker left: still ours, number unknown
  if ($r -eq -6) { $script:Ours = $false }
  throw "WRITE FAILED ($what): code $r, $([ClipH]::LastReason)"
}
# the only way scenario code writes: marker + number checked, marker written again
function WriteClip($text) {
  $has = $null -ne $text
  Took ([ClipH]::Write($Run, $null, $true, $script:Seq, $has, $(if ($has) { [string]$text } else { "" }), $Run)) "a test write"
}
function SetClip([string]$text) { WriteClip $text }
function EmptyClip() { WriteClip $null }
# take the clipboard back after a DELIBERATE intrusion (the intruder marker is accepted, nothing else)
function TakeBack() { Took ([ClipH]::Write($Run, $IntrMark, $true, -1, $true, $Before, $Run)) "taking back after a simulated intrusion" }
# ... and after the simulated third program (only at the end of its own scenario)
function TakeBackForeign() { Took ([ClipH]::Write($Run, $ForeignMark, $true, -1, $true, $Before, $Run)) "taking back after the simulated third program" }
# reading does not change the sequence number; "<no text>" is only a label for content checks
function GetClip() { if ([System.Windows.Forms.Clipboard]::ContainsText()) { [System.Windows.Forms.Clipboard]::GetText() } else { "<no text>" } }
function Formats() { $o = [System.Windows.Forms.Clipboard]::GetDataObject(); if ($o) { @($o.GetFormats()) } else { @() } }
function Marker() { [ClipH]::MarkerState($Run, $IntrMark) }
# run the self test; returns @(exitCode, message). -Intrusion: the scenario makes a simulated program interfere (exit 6
# expected). -TestError: the scenario makes the test machinery fail on purpose (exit 7 expected).
function RunSelfTest([string]$fail, [switch]$Intrusion, [switch]$TestError) {
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = ".clip"; $env:ONEKEY_TEST_FAIL = $fail
  $env:ONEKEY_TEST_CLIP_RUN = $RunHex; $env:ONEKEY_TEST_CLIP_INTRUDER = $IntrHex; $env:ONEKEY_TEST_CLIP_FOREIGN = $ForeignHex
  Remove-Item "$cfg\selftest-clip.txt" -Force -ErrorAction Ignore
  $pr = Start-Process $exe -ArgumentList "--selftest-clip", $Secret -PassThru -Wait
  $txt = if (Test-Path "$cfg\selftest-clip.txt") { [IO.File]::ReadAllText("$cfg\selftest-clip.txt", [Text.Encoding]::UTF8) } else { "" }
  $env:ONEKEY_TEST_FAIL = $null
  $parts = $txt.Split([char]'|', 3)
  if ($parts.Count -lt 3) { throw "unexpected self-test output: '$txt'" }
  $msg = $parts[2]
  "   selftest fail=[$fail] exit=$($pr.ExitCode) message='$msg'" | Out-Host
  # 6 = the app found the run marker gone when it first opened the clipboard: someone else wrote before it (it changed nothing)
  if ($pr.ExitCode -eq 6 -and -not $Intrusion) { [ClipH]::LastReason = "the app found the run marker gone"; Disturbed "while the app ran" }
  # 7 = the app could not build the scenario (simulation or run-marker write failed): the result means nothing
  if ($pr.ExitCode -eq 7 -and -not $TestError) { $script:Seq = [long]$parts[1]; throw "TEST ERROR: the app could not set up the scenario [$fail]: $msg" }
  # the number right after the app's last write. It is not proof of ownership on its own: the next write also checks the marker.
  $script:Seq = [long]$parts[1]
  return @($pr.ExitCode, $msg)
}

# ---- save the user's clipboard (plain text only), and make sure nothing changed while we looked
$seqStart = [ClipH]::GetClipboardSequenceNumber()
$userHadText = [System.Windows.Forms.Clipboard]::ContainsText()
$userText = if ($userHadText) { [System.Windows.Forms.Clipboard]::GetText() } else { "" }
# formats that are plain text or bookkeeping; anything else (HTML, RTF, images, files...) cannot be restored from text alone
$TextFamily = @("DataObject", "Ole Private Data", "UnicodeText", "Text", "OEMText", "Locale", "System.String",
  "CanIncludeInClipboardHistory", "CanUploadToCloudClipboard", "ExcludeClipboardContentFromMonitorProcessing")
$userFormats = @(Formats | Where-Object { $TextFamily -notcontains $_ })
if ($userFormats.Count -gt 0) {
  "The clipboard holds more than plain text (" + ($userFormats -join ", ") + "). Not touching it. Copy some plain text (or nothing) and rerun."
  exit 2
}
if ([ClipH]::GetClipboardSequenceNumber() -ne $seqStart) { "The clipboard changed while it was being saved. Not touching it; rerun when nobody is using the PC."; exit 2 }

try {
  # first write, over the user's text: checked by the number seen while saving it (there is no marker yet)
  Took ([ClipH]::Write($Run, $null, $false, $seqStart, $true, $Before, $Run)) "the first test write"

  # 1) success: swap in, restore; the clipboard ends as before
  SetClip $Before
  $r = RunSelfTest ""
  Check CL01 0 $r[0] "success path returns 0"
  Check CL02 $Before (GetClip) "success path restores the previous text"

  # 2) read fails while text exists: stop before emptying
  SetClip $Before
  $r = RunSelfTest "clip:read"
  Check CL03 3 $r[0] "read failure refuses"
  Check CL04 $Before (GetClip) "read failure leaves the clipboard untouched"
  Check CL05 $true ($r[1].Contains("그대로")) "read failure message says the clipboard is unchanged"

  # 3) marking fails after emptying: restore the saved text inside the same open
  SetClip $Before
  $r = RunSelfTest "clip:mark"
  Check CL06 3 $r[0] "mark failure refuses"
  Check CL07 $Before (GetClip) "mark failure restores the previous text"
  Check CL08 $true ($r[1].Contains("원래대로")) "mark failure message says restored"

  # 4) SetClipboardData fails: same
  SetClip $Before
  $r = RunSelfTest "clip:set"
  Check CL09 3 $r[0] "set failure refuses"
  Check CL10 $Before (GetClip) "set failure restores the previous text"
  Check CL11 $false ((GetClip) -eq $Secret) "the dummy secret never stays on the clipboard"

  # 5) mark fails AND the restore fails: the message must say the clipboard was emptied
  SetClip $Before
  $r = RunSelfTest "clip:mark,clip:restore"
  Check CL12 "<no text>" (GetClip) "double failure: clipboard is empty (not the secret)"
  Check CL13 $true ($r[1].Contains("비워졌습니다")) "double failure message says the clipboard was emptied"

  # 6) the clipboard was empty to begin with (only the run marker): a mark failure leaves it empty and says restored
  EmptyClip
  $r = RunSelfTest "clip:mark"
  Check CL14 "<no text>" (GetClip) "empty clipboard stays empty"
  Check CL15 $true ($r[1].Contains("원래대로")) "empty clipboard: message says restored"

  # 7) V35-1: ownership is decided by the app's owner token, not by how far the sequence number moved
  SetClip $Before
  $r = RunSelfTest ""
  Check CL16 "0|Done" ("$($r[0])|$($r[1])") "normal case (only Windows format synthesis): restored as ours"
  SetClip $Before
  $r = RunSelfTest "clip:ext-copy"
  Check CL17 "0|NotOurs" ("$($r[0])|$($r[1])") "another program copied right after our close: recognised as not ours"
  Check CL18 $External (GetClip) "... and its copy is kept (not overwritten with the old text)"
  SetClip $Before
  $r = RunSelfTest "clip:ext-empty"
  Check CL19 "0|NotOurs" ("$($r[0])|$($r[1])") "another program emptied the clipboard right after our close: not ours"
  Check CL20 "<no text>" (GetClip) "... and the old text is not written back over its action"
  SetClip $Before
  $r = RunSelfTest "clip:busy-then-ext"
  Check CL21 "0|NotOurs" ("$($r[0])|$($r[1])") "clipboard busy, then another program copied during the retries: not ours"
  Check CL22 $External (GetClip) "... and its copy is kept"
  SetClip $Before
  $r = RunSelfTest "clip:token"
  Check CL23 3 $r[0] "owner token could not be written: refuse (the secret must not stay unowned)"
  Check CL24 $Before (GetClip) "... and the previous text is restored"

  # 8) V36-1 / V37-1: "not ours" and "could not read the owner mark" are different results
  SetClip $Before
  $r = RunSelfTest "clip:ext-fake-owner"
  Check CL25 "0|NotOurs" ("$($r[0])|$($r[1])") "same owner format but another token: not ours"
  Check CL26 $External (GetClip) "... and that content is kept"
  SetClip $Before
  $r = RunSelfTest "clip:owner-lock-once"
  Check CL27 "0|Done" ("$($r[0])|$($r[1])") "owner mark unreadable once: retried, then restored"
  Check CL28 $Before (GetClip) "... previous text back"
  SetClip $Before
  $r = RunSelfTest "clip:owner-lock"
  Check CL29 "4|Failed" ("$($r[0])|$($r[1])") "owner mark never readable (lock fails): reported as a failure, not as 'not ours'"
  Check CL30 $Secret (GetClip) "... the (dummy) secret is left and the product warns instead of claiming success"
  SetClip $Before
  $r = RunSelfTest "clip:owner-read"
  Check CL31 "4|Failed" ("$($r[0])|$($r[1])") "owner mark never readable (GetClipboardData fails): failure"
  Check CL32 $Secret (GetClip) "... likewise"
  SetClip $Before
  $r = RunSelfTest "clip:owner-size0"
  Check CL33 "4|Failed" ("$($r[0])|$($r[1])") "GlobalSize returns its error value 0: failure, not 'not ours' (V37-1)"
  Check CL34 $Secret (GetClip) "... likewise"

  # 9) V38-1: interference is caught before anything is overwritten. A simulated program writes with the INTRUDER marker;
  #    the harness must refuse its normal write, keep the intruder's content, and only then take the clipboard back.
  SetClip $Before
  $r = RunSelfTest "clip:intrude-first" -Intrusion
  Check CL35 "6|Disturbed" ("$($r[0])|$($r[1])") "another program copied before the app's first read: the app changes nothing"
  Check CL36 "$Intruder|2" ("$(GetClip)|$(Marker)") "... the intruder's content is there"
  Check CL37 -2 ([ClipH]::Write($Run, $null, $true, $script:Seq, $true, $Before, $Run)) "... and the harness refuses to write over it"
  TakeBack
  SetClip $Before
  $r = RunSelfTest "clip:intrude-restore" -Intrusion
  Check CL38 "0|NotOurs" ("$($r[0])|$($r[1])") "another program copied during the app's restore retries: the app keeps it"
  Check CL39 "$Intruder|2" ("$(GetClip)|$(Marker)") "... the intruder's content is there"
  Check CL40 -2 ([ClipH]::Write($Run, $null, $true, $script:Seq, $true, $Before, $Run)) "... and the harness refuses to write over it (number unchanged since the app's report, marker not ours)"
  TakeBack
  # after the app closed / before the next setup / before the final restore: the harness itself simulates the program
  SetClip $Before
  $r = RunSelfTest ""
  $seqApp = $script:Seq
  [void]([ClipH]::Write($Run, $null, $true, $seqApp, $true, $Intruder, $IntrMark))   # the "other program" (only over our own content)
  Check CL41 -2 ([ClipH]::Write($Run, $null, $true, $seqApp, $true, $Before, $Run)) "intrusion after the app closed: the next setup write refuses (number moved)"
  Check CL42 -2 ([ClipH]::Write($Run, $null, $true, -1, $true, "1Key-test-final-probe", $null)) "... a final-restore write refuses too (marker not ours)"
  Check CL43 "$Intruder|2" ("$(GetClip)|$(Marker)") "... the intruder's content is kept"
  TakeBack
  Check CL44 "$Before|1" ("$(GetClip)|$(Marker)") "taking back works only because the intruder marker was named"

  # 10) V38-2: the harness's own writes are checked; a failure is reported, never taken for success
  $cases = @(
    @{ At = "alloc";  Code = -3; After = "$Before|1" },
    @{ At = "empty";  Code = -4; After = "$Before|1" },
    @{ At = "marker"; Code = -5; After = "<no text>|1" },
    @{ At = "flags";  Code = -5; After = "<no text>|1" },
    @{ At = "text";   Code = -5; After = "<no text>|1" })
  $n = 45
  foreach ($c in $cases) {
    SetClip $Before
    [ClipH]::FailAt = $c.At
    $w = [ClipH]::Write($Run, $null, $true, $script:Seq, $true, "1Key-test-setup", $Run)
    [ClipH]::FailAt = ""
    Check ("CL{0:D2}" -f $n) $c.Code $w ("setup write, failure at '" + $c.At + "': reported as code " + $c.Code); $n++
    Check ("CL{0:D2}" -f $n) $c.After ("$(GetClip)|$(Marker)") ("... then the clipboard is " + $c.After + " (no half write, still ours)"); $n++
    if ($w -eq -5) { $script:Seq = -1 }   # emptied by us: continue on the marker alone
  }
  # the final-restore kind of write (user text, no marker) fails at the text: not "put back", and still recognisably ours
  SetClip $Before
  [ClipH]::FailAt = "text"
  $w = [ClipH]::Write($Run, $null, $true, $script:Seq, $true, "1Key-test-final-probe", $null)
  [ClipH]::FailAt = ""
  Check CL55 -5 $w "final-restore write failing at the text: reported as -5 (the harness then says it was NOT put back)"
  Check CL56 "<no text>|1" ("$(GetClip)|$(Marker)") "... the clipboard is empty with the run marker"
  $script:Seq = -1
  # a failing scenario write stops the run (the wrapper throws); the marker left behind lets the run continue here
  [ClipH]::FailAt = "text"
  $threw = $false
  try { SetClip $Before } catch { $threw = "$_".StartsWith("WRITE FAILED"); Clear-ExpectedError }
  [ClipH]::FailAt = ""
  Check CL57 $true $threw "a failed test write stops the run (WRITE FAILED), it is never taken for success"
  SetClip $Before
  Check CL58 "$Before|1" ("$(GetClip)|$(Marker)") "after an emptied-and-marked failure the next write is accepted on the marker alone"

  # 11) V39-1: the app's simulations are checked too. A third program (FOREIGN marker) writes right before a simulation:
  #     the simulation must not run over it, its content stays, and the harness's normal take-back refuses it.
  SetClip $Before
  $r = RunSelfTest "clip:foreign-before-sim,clip:intrude-first" -Intrusion
  Check CL59 "6|Disturbed" ("$($r[0])|$($r[1])") "a third program wrote right before the app's intruder simulation: reported as interference"
  Check CL60 "$Foreign|3" ("$(GetClip)|$(Marker)") "... its content is kept (the simulation did not empty the clipboard)"
  Check CL61 -2 ([ClipH]::Write($Run, $IntrMark, $true, -1, $true, $Before, $Run)) "... and the harness's take-back (run or intruder marker) refuses it"
  TakeBackForeign
  SetClip $Before
  $r = RunSelfTest "clip:foreign-before-sim,clip:ext-copy" -Intrusion
  Check CL62 "6|Disturbed" ("$($r[0])|$($r[1])") "a third program wrote after the swap, right before the 'external copy' simulation: interference"
  Check CL63 "$Foreign|3" ("$(GetClip)|$(Marker)") "... its content is kept (no simulated copy, no restore over it)"
  Check CL64 -2 ([ClipH]::Write($Run, $IntrMark, $true, -1, $true, $Before, $Run)) "... and the harness's take-back refuses it"
  TakeBackForeign
  # failures of the test machinery are reported as 7 (not as interference), after the dummy secret was put back
  SetClip $Before
  $r = RunSelfTest "clip:ext-copy,clip:sim-fail" -TestError
  Check CL65 "7|SimulationFailed; restore=Done" ("$($r[0])|$($r[1])") "the 'external copy' simulation could not be made: test error, with the restore result"
  Check CL66 "$Before|1" ("$(GetClip)|$(Marker)") "... and the previous text is back (the secret was restored anyway)"
  SetClip $Before
  $r = RunSelfTest "clip:testmark-once" -TestError
  Check CL67 "7|TestMarkerFailed; restore=Done" ("$($r[0])|$($r[1])") "the app could not write the run marker once: test error (not interference), with the restore result"
  Check CL68 "$Before|1" ("$(GetClip)|$(Marker)") "... and the previous text is back with the run marker"
  # V40: error 7 does not mean the restore worked - the result is reported with it
  SetClip $Before
  $r = RunSelfTest "clip:ext-copy,clip:sim-fail,clip:owner-lock" -TestError
  Check CL69 "7|SimulationFailed; restore=Failed" ("$($r[0])|$($r[1])") "simulation failed AND the restore failed: 7 names both (not 'restored')"
  Check CL70 "$Secret|1" ("$(GetClip)|$(Marker)") "... the dummy secret is still there (the restore really failed)"
  SetClip $Before
  $r = RunSelfTest "clip:ext-copy,clip:sim-write-fail" -TestError
  Check CL71 "7|SimulationFailed; restore=NotOurs" ("$($r[0])|$($r[1])") "simulation failed after emptying: the owner token is gone, so the restore finds 'not ours'"
  Check CL72 "<no text>|1" ("$(GetClip)|$(Marker)") "... the simulation left only the run marker (no half write), so the run can go on"
} catch { Add-Failure ("exception: " + $_) }
finally {
  $env:ONEKEY_TEST_FAIL = $null; $env:ONEKEY_TEST_CLIP_RUN = $null; $env:ONEKEY_TEST_CLIP_INTRUDER = $null; $env:ONEKEY_TEST_CLIP_FOREIGN = $null
  [ClipH]::FailAt = ""
  # Put the user's text back only while the clipboard still holds this run's marker (and the last seen number, when
  # known), decided and written in one open session. No marker is left: the user's text (or nothing) is all that remains.
  if ($script:Ours) {
    $r = [ClipH]::Write($Run, $null, $true, $script:Seq, $userHadText, $userText, $null)
    if ($r -ge 0) { if ($userHadText) { "   (the user's clipboard text was put back)" } else { "   (the clipboard was emptied, as it was before)" } }
    elseif ($r -eq -2) { Add-Failure ("DISTURBED: someone else changed the clipboard at the end (" + [ClipH]::LastReason + "); left as it is, the user's text was NOT put back") }
    else { Add-Failure ("the user's clipboard text was NOT put back: code $r, " + [ClipH]::LastReason) }
  } else { "   (clipboard left as it is: the run never wrote to it, or someone else changed it during the run)" }
  Stop-TestInstances ".clip"
}
Complete-Checks
