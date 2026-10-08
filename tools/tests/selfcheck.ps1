# Self-test of the judgement library (T18): each broken mini-script below must exit 1, the clean one must exit 0.
# Does not start 1Key. ASCII only.
$ErrorActionPreference = "Continue"
$lib = Join-Path $PSScriptRoot "lib\Check.ps1"
$tmp = Join-Path $env:TEMP ("1Key-selfcheck-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $tmp | Out-Null

$cases = [ordered]@{
  'clean'          = @{ Want = 0; Body = "Start-Checks -Required @('X1','X2'); Check X1 1 1 'a'; Check X2 'b' 'b' 'b'; Complete-Checks" }
  'failed-check'   = @{ Want = 1; Body = "Start-Checks -Required @('X1','X2'); Check X1 1 1; Check X2 1 2; Complete-Checks" }
  'missing-id'     = @{ Want = 1; Body = "Start-Checks -Required @('X1','X2'); Check X1 1 1; Complete-Checks" }
  'duplicate-id'   = @{ Want = 1; Body = "Start-Checks -Required @('X1'); Check X1 1 1; Check X1 1 1; Complete-Checks" }
  'undeclared-id'  = @{ Want = 1; Body = "Start-Checks -Required @('X1'); Check X1 1 1; Check X9 1 1; Complete-Checks" }
  'declared-twice' = @{ Want = 1; Body = "Start-Checks -Required @('X1','X1'); Check X1 1 1; Complete-Checks" }
  'write-error'    = @{ Want = 1; Body = "Start-Checks -Required @('X1'); Check X1 1 1; Write-Error 'boom'; Complete-Checks" }
  'silent-error'   = @{ Want = 1; Body = "Start-Checks -Required @('X1'); Check X1 1 1; Get-Item 'Z:\no\such\path' -ErrorAction SilentlyContinue; Complete-Checks" }
  'add-failure'    = @{ Want = 1; Body = "Start-Checks -Required @('X1'); Check X1 1 1; Add-Failure 'no window'; Complete-Checks" }
  'caught-throw'   = @{ Want = 1; Body = "Start-Checks -Required @('X1'); try { throw 'x' } catch { }; Check X1 1 1; Complete-Checks" }
  'case-sensitive' = @{ Want = 1; Body = "Start-Checks -Required @('X1'); Check X1 'Abc' 'abc'; Complete-Checks" }
  'ignored-error'  = @{ Want = 0; Body = "Start-Checks -Required @('X1'); Get-Item 'Z:\no\such\path' -ErrorAction Ignore; Check X1 1 1; Complete-Checks" }
  'expected-error' = @{ Want = 0; Body = "Start-Checks -Required @('X1'); Get-Item 'Z:\no\such\path' -ErrorAction SilentlyContinue; Clear-ExpectedError; Check X1 1 1; Complete-Checks" }
}

$bad = 0
try {
  foreach ($name in $cases.Keys) {
    $c = $cases[$name]
    $f = Join-Path $tmp "$name.ps1"
    Set-Content -Path $f -Encoding ASCII -Value ("`$ErrorActionPreference = 'Continue'`r`n. '$lib'`r`n" + $c.Body)
    $null = & powershell -NoProfile -ExecutionPolicy Bypass -File $f 2>&1
    $code = $LASTEXITCODE
    if ($code -eq $c.Want) { "ok   $name exit=$code" } else { $bad++; "FAIL $name exit=$code want=$($c.Want)" }
  }
} finally { Remove-Item $tmp -Recurse -Force -ErrorAction Ignore }
"fail=$bad"
if ($bad -gt 0) { exit 1 } else { exit 0 }
