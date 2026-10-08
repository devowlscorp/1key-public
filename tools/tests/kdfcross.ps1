# Cross-check of 1Key's own BLAKE2b / Argon2id against independent implementations (security follow-up B1, Codex 15:19 Q3).
# No window, no user files: 1Key.exe --selftest-kdf in test mode with a scratch config folder.
# Independent references (already on this PC, nothing downloaded): Node.js crypto.argon2Sync (OpenSSL) and Python hashlib.blake2b.
# Cases are deterministic (fixed seed in kdfcross.mjs).
# KX01 BLAKE2b: inputs of 0..1000 bytes around the 128-byte block, outputs of 1/32/63/64 bytes - all equal to Python's hashlib
# KX02 Argon2id, the supported profile (m=64 MiB, t=3, p=1): empty, short, long, UTF-8 non-ASCII passwords, same password with two salts
# KX03 Argon2id, other memory / passes / lanes 1-4 / tag lengths across H' boundaries (4..1024) / secret and associated data
# KX04 invalid arguments (short salt, memory below 8p, tag below 4, zero passes): 1Key refuses with no output, like the reference
# Judgement: tools\tests\lib\Check.ps1. ASCII only.
param([string]$Exe = "")
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
Start-Checks -Required @('KX01','KX02','KX03','KX04')
$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
$dir = "$sp\kdfcross"; New-Item -ItemType Directory -Force $dir | Out-Null
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$node = @("$env:ProgramFiles\nodejs\node.exe", (Get-Command node -ErrorAction Ignore).Source) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
try {
  if (-not $node) { throw "Node.js not found (the reference Argon2id)" }
  $cases = "$dir\cases.txt"; $exp = "$dir\expected.txt"; $got = "$dir\got.txt"
  & $node (Join-Path $PSScriptRoot "kdfcross.mjs") $cases $exp
  if ($LASTEXITCODE -ne 0) { throw "kdfcross.mjs failed ($LASTEXITCODE)" }
  # Python fills the BLAKE2b lines
  $py = @'
import hashlib, sys
cases = open(sys.argv[1]).read().split('\n'); exp = open(sys.argv[2]).read().split('\n')
out = []
for c, e in zip(cases, exp):
    if e == 'PY':
        f = c.split('|'); out.append(hashlib.blake2b(bytes.fromhex(f[1]), digest_size=int(f[2])).hexdigest())
    else: out.append(e)
open(sys.argv[2], 'w').write('\n'.join(out))
'@
  $pyFile = "$dir\fill.py"; [IO.File]::WriteAllText($pyFile, $py)
  python $pyFile $cases $exp
  if ($LASTEXITCODE -ne 0) { throw "python failed ($LASTEXITCODE)" }
  Remove-Item $got -ErrorAction Ignore
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $dir; $env:ONEKEY_INSTANCE_SUFFIX = ".kx"
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $p = Start-Process $exe -ArgumentList @("--selftest-kdf", "`"$cases`"", "`"$got`"") -PassThru -Wait -WindowStyle Hidden
  $ms = $sw.ElapsedMilliseconds
  $env:ONEKEY_TEST = $null; $env:ONEKEY_CONFIG_DIR = $null; $env:ONEKEY_INSTANCE_SUFFIX = $null
  if (-not (Test-Path $got)) { throw "1Key wrote no results (exit $($p.ExitCode))" }
  $c = @(Get-Content $cases | Where-Object { $_ }); $e = @(Get-Content $exp | Where-Object { $_ }); $g = @(Get-Content $got | Where-Object { $_ })
  if ($c.Count -ne $e.Count -or $c.Count -ne $g.Count) { throw "line counts differ: cases $($c.Count) expected $($e.Count) got $($g.Count)" }
  $groups = @{ KX01 = @(); KX02 = @(); KX03 = @(); KX04 = @() }
  for ($i = 0; $i -lt $c.Count; $i++) {
    $f = $c[$i].Split('|')
    $k = if ($f[0] -eq 'B') { 'KX01' } elseif ($e[$i] -eq 'ERR') { 'KX04' } elseif ($f[3] -eq '3' -and $f[4] -eq '65536' -and $f[5] -eq '1' -and $f[6] -eq '32') { 'KX02' } else { 'KX03' }
    $ok = if ($e[$i] -eq 'ERR') { $g[$i].StartsWith('ERR:') } else { $g[$i] -eq $e[$i] }
    $groups[$k] += ,@($i, $ok)
  }
  foreach ($k in 'KX01','KX02','KX03','KX04') {
    $n = $groups[$k].Count; $bad = @($groups[$k] | Where-Object { -not $_[1] } | ForEach-Object { $_[0] })
    Check $k "$n/$n" "$($n - $bad.Count)/$n" ("matches the independent implementation" + $(if ($bad.Count) { " - differing case lines: " + ($bad -join ',') } else { "" }) + " (1Key run $ms ms)")
  }
} catch { Add-Failure ("exception: " + $_) }
Complete-Checks
