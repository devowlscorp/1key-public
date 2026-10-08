# Translation table generator checks (Codex V55-7), no windows, no app: runs tools/i18n/gen-strings.ps1 on small temporary
# TSV files and judges its exit code. The real strings.tsv / Strings.g.cs are not touched (output goes to the test folder).
# IG01 a valid table (comment line, blank line, \n and \\ escapes, UTF-8 BOM, non-ASCII text) -> success
# IG02 a data row with 5 columns -> failure
# IG03 a data row with 7 columns (an extra tab, a shifted row) -> failure
# IG04 a placeholder missing in one language -> failure
# IG05 an unknown escape (\t written as text) -> failure
# IG06 a duplicate key -> failure
# IG07 the real strings.tsv still passes
# Judgement: tools\tests\lib\Check.ps1. ASCII only (non-ASCII test text is built from code points).
param()
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
Start-Checks -Required @('IG01','IG02','IG03','IG04','IG05','IG06','IG07')

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
$dir = "$sp\i18ngen"; New-Item -ItemType Directory -Force $dir | Out-Null
$gen = Join-Path $PSScriptRoot "..\i18n\gen-strings.ps1"
$T = "`t"
$ko = [string][char]0xD655 + [char]0xC778          # a Korean word
$header = "key${T}ko${T}en${T}zh-Hans${T}ja${T}vi"
function Gen([string]$name, [string[]]$rows) {
  $tsv = "$dir\$name.tsv"
  $text = ($header + "`r`n" + ($rows -join "`r`n") + "`r`n")
  [IO.File]::WriteAllText($tsv, $text, (New-Object Text.UTF8Encoding($true)))
  $o = & powershell -NoProfile -ExecutionPolicy Bypass -File $gen -Tsv $tsv -Out "$dir\$name.g.cs" 2>&1
  $LASTEXITCODE
}
try {
  $good = @("# section", "", "a.ok${T}$ko${T}OK${T}ok${T}ok${T}ok", "a.multi${T}line1\nline2 \\ x${T}a\nb${T}c${T}d${T}e", "a.hold${T}{n} $ko${T}{n} x${T}{n}${T}{n}${T}{n}")
  Check IG01 0 (Gen "good" $good) "valid table succeeds"
  Check IG02 1 (Gen "five" @("a.ok${T}$ko${T}OK${T}ok${T}ok")) "5 columns fail"
  Check IG03 1 (Gen "seven" @("a.ok${T}$ko${T}OK${T}ok${T}ok${T}ok${T}extra")) "7 columns fail"
  Check IG04 1 (Gen "hold" @("a.hold${T}{n} $ko${T}x${T}{n}${T}{n}${T}{n}")) "missing placeholder fails"
  Check IG05 1 (Gen "esc" @("a.esc${T}a\tb${T}x${T}x${T}x${T}x")) "unknown escape fails"
  Check IG06 1 (Gen "dup" @("a.ok${T}$ko${T}OK${T}ok${T}ok${T}ok", "a.ok${T}$ko${T}OK${T}ok${T}ok${T}ok")) "duplicate key fails"
  $real = Join-Path $PSScriptRoot "..\..\src\OneKey\i18n\strings.tsv"
  $o = & powershell -NoProfile -ExecutionPolicy Bypass -File $gen -Tsv $real -Out "$dir\real.g.cs" 2>&1
  Check IG07 0 $LASTEXITCODE "the real strings.tsv passes"
}
catch { Add-Failure ("exception: " + $_.Exception.Message) }
Complete-Checks
