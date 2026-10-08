# Site fill, pure parts (0.2.55), no windows: runs the exe with --selftest-site (test mode) and judges its lines.
# SN01-SN20 address normalisation (0.2.58, Codex V55-1): only scheme/host case, default port, user info and empty path are
#           normalised; path case, trailing slash, query and fragment are kept; pairs that must differ; diagnostics masking
# SM01-SM10 finding the linked field (same kind only; unique id, then unique name; never by order - Codex V55-2; ambiguous = not found)
# SP01-SP13 format-6 link lines that must be rejected (bad escapes, duplicates, ranges, extra/missing columns, garbage) - V55-8
# SV01-SV11 encrypted block format 6: links saved inside the encrypted block with escaping, read back, cleared on lock;
#           removing every link saves format 3 again (older versions can open the file).
# SC01-SC06 why a fill stopped, one order on every path (0.2.63, Codex C62): lock/cancel, then the time limit, then a changed
#           window, then the path's own error; needs-elevation is its own result, never success
# MI01-MI15 an item with several inputs (0.2.65): format-7 lines accepted/rejected, saved as 7, every input and its link come
#           back in order, lock clears them, one input again saves as 6
# Own config folder; the real 1Key and its config are not touched. Safe to run while the PC is in use (no window).
# Judgement: tools\tests\lib\Check.ps1. ASCII only.
param([string]$Exe = "")
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
$ids = @(); 1..21 | ForEach-Object { $ids += "SN{0:D2}" -f $_ }; 1..15 | ForEach-Object { $ids += "SM{0:D2}" -f $_ }; 1..13 | ForEach-Object { $ids += "SP{0:D2}" -f $_ }; 1..11 | ForEach-Object { $ids += "SV{0:D2}" -f $_ }; 1..6 | ForEach-Object { $ids += "SC{0:D2}" -f $_ }; 1..6 | ForEach-Object { $ids += "SR{0:D2}" -f $_ }; 1..18 | ForEach-Object { $ids += "MI{0:D2}" -f $_ }; 1..5 | ForEach-Object { $ids += "AP{0:D2}" -f $_ }; 1..10 | ForEach-Object { $ids += "NC{0:D2}" -f $_ }; 1..3 | ForEach-Object { $ids += "SA{0:D2}" -f $_ }; 1..6 | ForEach-Object { $ids += "FA{0:D2}" -f $_ }; 1..9 | ForEach-Object { $ids += "AN{0:D2}" -f $_ }; 1..6 | ForEach-Object { $ids += "PL{0:D2}" -f $_ }
1..8 | ForEach-Object { $ids += "CI{0:D2}" -f $_ }   # drop list (CIDA) bounds, Codex C28-4 (0.3.29)
1..10 | ForEach-Object { $ids += "LL{0:D2}" -f $_ }  # launcher lineage: pid + creation time identity, pid reuse, two roots (Codex R29-1, 0.3.30)
1..5 | ForEach-Object { $ids += "OB{0:D2}" -f $_ }   # seen-window checks: old watch, changed window, front once / flash (Codex C28-2, 0.3.30)
1..11 | ForEach-Object { $ids += "MF{0:D2}" -f $_ }  # package manifest read as XML: quotes, namespaces, comment, DTD, several packages (Codex R29-2, 0.3.30)
1..8 | ForEach-Object { $ids += "AG{0:D2}" -f $_ }   # BLAKE2b / Argon2id against published test values (security follow-up B1, not wired to the file format yet)
1..11 | ForEach-Object { $ids += "KY{0:D2}" -f $_ }   # key kept instead of the master, owned buffers, all-or-nothing unlock, old .bak copies (security follow-up A)
1..14 | ForEach-Object { $ids += "BK{0:D2}" -f $_ }   # backup file: round trip, header/section damage, profile before computing, two-file commit stop and recovery, temp names as links
1..8 | ForEach-Object { $ids += "DA{0:D2}" -f $_ }   # settings folder rights: user + SYSTEM only, existing files, junction skipped, failure reported
1..5 | ForEach-Object { $ids += "MG{0:D2}" -f $_ }   # format 10 / Argon2id: migration of formats 3-9, exact profile, header damage, AAD (security follow-up B1)
1..13 | ForEach-Object { $ids += "WB{0:D2}" -f $_ }   # website bookmarks: address check, favicon origin, redirect rule, launch.dat keeps url fields, image header, decode (Codex 02:46)
Start-Checks -Required $ids

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$cfg = "$sp\siteunit_cfg"; if (Test-Path $cfg) { Get-ChildItem $cfg -Recurse | Remove-Item -Force -Recurse -ErrorAction Ignore }; New-Item -ItemType Directory -Force $cfg | Out-Null

try {
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = ".siteunit"; $env:ONEKEY_TEST_FAIL = $null
  $p = Start-Process $exe -ArgumentList "--selftest-site" -PassThru
  if (-not $p.WaitForExit(30000)) { Stop-Process -Id $p.Id -Force -ErrorAction Ignore; throw "selftest did not finish in 30 s" }
  $out = "$cfg\selftest-site.txt"
  if (-not (Test-Path $out)) { throw "no selftest-site.txt" }
  foreach ($line in [IO.File]::ReadAllLines($out, [Text.Encoding]::UTF8)) {
    $m = [regex]::Match($line, '^(ok  |FAIL) (\S+) (.*)$')
    if (-not $m.Success) { continue }
    $id = $m.Groups[2].Value
    if ($id -eq "SV00" -or $id -eq "MI00") { Add-Failure $line; continue }
    Check $id "ok" ($(if ($m.Groups[1].Value -eq "ok  ") { "ok" } else { "FAIL" })) $m.Groups[3].Value
  }
}
catch { Add-Failure ("exception: " + $_.Exception.Message) }
Complete-Checks
