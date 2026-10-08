# Installer helper when PowerShell is restricted (Codex 13:05 "not checked: PowerShell blocked"). No window, no install, nothing is
# moved to the Recycle Bin (the -Recycle path is not run here). The helper runs from a scratch folder with -Dirs/-Protect test options.
# PB01 execution policy that refuses unsigned scripts (AllSigned, like a company policy): the helper exits non-zero and writes no
#      list - the installer then prints "skipped (PowerShell cannot be used)" and moves nothing
# PB02 Constrained Language Mode (__PSLockdownPolicy=4): finding works and the list is well formed (count line + that many paths)
# PB03 Constrained Language Mode: -Link answers one of the known words, -Check answers 1Key/other
# PB04 the installer script handles a failed call at every helper call site: after each `nsExec::ExecToStack 'powershell` the exit
#      value is popped and a non-zero value is handled within the next lines (static check of installer\1Key.nsi)
# ASCII only.
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
Start-Checks -Required @("PB01","PB02","PB03","PB04")
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$helper = Join-Path $repo "installer\cleanup-old.ps1"
$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
$w = Join-Path $sp "psblocked"; if (Test-Path $w) { Remove-Item $w -Recurse -Force -ErrorAction Ignore }
New-Item -ItemType Directory -Force "$w\a" | Out-Null
$exe = Get-DefaultExe
Copy-Item $exe "$w\a\1Key-old.exe"; Copy-Item $exe "$w\keep.exe"
function Run([string]$policy, [bool]$clm, [string[]]$rest) {
  $old = $env:__PSLockdownPolicy
  if ($clm) { $env:__PSLockdownPolicy = "4" } else { $env:__PSLockdownPolicy = $null }
  try {
    $n = $global:Error.Count
    $out = & powershell -NoProfile -ExecutionPolicy $policy -File $helper @rest 2>$null
    $code = $LASTEXITCODE
    while ($global:Error.Count -gt $n) { $global:Error.RemoveAt(0) }   # the refused run's stderr lines are expected here
    @{ Code = $code; Out = (@($out) -join "|") }
  } finally { $env:__PSLockdownPolicy = $old }
}
try {
  $r1 = Run "AllSigned" $false @("-Keep", "$w\keep.exe", "-Out", "$w\o1.txt", "-Dirs", "$w\a", "-Protect", "$w\none.exe")
  Check PB01 "True|False" "$($r1.Code -ne 0)|$(Test-Path "$w\o1.txt")" "AllSigned policy: helper refused (exit $($r1.Code)), no list file"

  $r2 = Run "Bypass" $true @("-Keep", "$w\keep.exe", "-Out", "$w\o2.txt", "-Dirs", "$w\a", "-Protect", "$w\none.exe")
  $lines = if (Test-Path "$w\o2.txt") { @(Get-Content "$w\o2.txt" -Encoding Unicode | Where-Object { $_ }) } else { @() }
  $wellFormed = $lines.Count -ge 1 -and ($lines[0] -eq "hold" -or ($lines[0] -match '^\d+$' -and [int]$lines[0] -eq $lines.Count - 1))
  Check PB02 "0|True|1" "$($r2.Code)|$wellFormed|$(if ($lines.Count) { $lines[0] })" "constrained language: list well formed (the one old copy found)"

  $r3 = Run "Bypass" $true @("-Keep", "$w\keep.exe", "-Link", "$w\x.lnk")
  $r4 = Run "Bypass" $true @("-Keep", "$w\keep.exe", "-Check", "$w\a\1Key-old.exe")
  Check PB03 "missing|1Key" "$($r3.Out)|$($r4.Out)" "constrained language: -Link and -Check answer known words"

  $nsi = Get-Content (Join-Path $repo "installer\1Key.nsi") -Encoding UTF8
  $bad = @()
  for ($i = 0; $i -lt $nsi.Count; $i++) {
    if ($nsi[$i] -match "nsExec::ExecToStack 'powershell") {
      $win = ($nsi[($i + 1)..([Math]::Min($nsi.Count - 1, $i + 8))] -join "`n")
      if ($win -notmatch 'Pop \$(\d)' ) { $bad += $i + 1; continue }
      $reg = $Matches[1]
      if ($win -notmatch ('\$\{If\} \$' + $reg + ' != 0|\$\{If\} \$' + $reg + ' == 0')) { $bad += $i + 1 }
    }
  }
  Check PB04 "" ($bad -join ",") "every helper call site checks the exit value (lines without a check listed)"
} catch { Add-Failure ("exception: " + $_.Exception.Message) }
finally { Remove-Item $w -Recurse -Force -ErrorAction Ignore }
Complete-Checks
