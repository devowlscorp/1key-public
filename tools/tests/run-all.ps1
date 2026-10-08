# Test runner: runs ONLY the tests for what changed (2026-10-05 user rule: "수정된 부분만 테스트해").
#   powershell -ExecutionPolicy Bypass -File tools\tests\run-all.ps1 [-Exe <1Key.exe>] [-Base <commit>] [-Only launch,a11y] [-ClipboardApproved]
#   -Base   compare the source with this commit (default: the latest v* tag, else HEAD~1) and run the scripts mapped to the changed files
#           (map below) plus the start check (startup, about 15 s). Uncommitted changes count too. A change that maps to nothing (docs,
#           comments-only files are still mapped by file - say so in the report) runs only the start check.
#   -Only   run exactly these scripts (plus the start check) instead of the map - when you know the area better than the file list.
#   -DryRun print the changed files and the scripts that would run, run nothing
#   -All    every mapped script once, plus the clipboard / Korean-input checks (clip, lockwidget\inputext - the user approved them for
#           the final integration run on 2026-10-05) - ONLY when the user asks for a full integration run.
# No-window checks (selfcheck, siteunit, i18ngen, psblocked, contrast-b) run in seconds and need no idle PC. Window tests need the PC to
# be free: before each one the runner checks the input idle time and stops if somebody used the PC after the previous test ended (keys
# a test pressed itself happen before its end and do not count).
# Never in this runner: one-off checks (launch.ps1 -Edge, tamper.ps1 old builds, mem/fxmem, lockwidget cycles/resources, shots,
# dw-compare - run them by hand when that code or the file format changes), installer/startuplink (autostart on hold), autolock
# (10 real idle minutes). clip and lockwidget\inputext only with -All or -ClipboardApproved (they use the real clipboard and IME and
# put the user's clipboard text back; each refuses to run if the clipboard holds anything but plain text).
# Logs: docs\test-logs\<date>_<version>\run-<HHmmss>\<script>.txt + summary.md. ASCII only.
param([string]$Exe = "", [string]$Base = "", [string[]]$Only = @(), [switch]$All, [switch]$ClipboardApproved, [switch]$DryRun, [int]$IdleSeconds = 30)
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }

# source file pattern -> test scripts. One line per area; keep it short.
$map = [ordered]@{
  '^src/OneKey/(Launch|Launcher|LaunchIcons|LaunchDrop|AppLaunch|AppWeb|WebIcon)\.cs$' = @("launch", "webfav")
  '^src/OneKey/(Uia|AppSite|FillButton|FillLog|SiteSelfTest)\.cs$'             = @("siteunit", "sitefill")
  '^src/OneKey/(Injector|Keys|AppTyping|Chip|AppLink)\.cs$'                     = @("inject")
  '^src/OneKey/(LockWidget|LockWidgetAcc|AppLock)\.cs$'                         = @("lockwidget\integration", "lockwidget\backdrop", "lockkbd", "closebox")
  '^src/OneKey/(Config|Crypto)\.cs$'                                           = @("siteunit", "master", "tamper")
  '^src/OneKey/Argon2\.cs$'                                                   = @("siteunit", "kdfcross")
  '^src/OneKey/(Backup|AppBackup)\.cs$'                                     = @("siteunit", "backup")
  '^src/OneKey/(Dialog|Help|Backdrop)\.cs$'                                    = @("dialog", "help")
  '^src/OneKey/(Theme|Controls|Toggle|Dw|DwStatic|AppUiKit|AppPaint|Fx|Toast|ThinScroll|Gdiplus)\.cs$' = @("layout", "dw", "printclient", "..\design\contrast-b")
  '^src/OneKey/(AppList|AppEdit|AppAdvanced|AppWndProc|AppTray)\.cs$'          = @("layout", "settingsro", "many", "addforms")
  '^src/OneKey/CtlAcc\.cs$'                                                    = @("a11y")
  '^src/OneKey/(i18n/strings\.tsv|L\.cs|Strings\.g\.cs)$'                      = @("i18ngen", "i18n")
  '^src/OneKey/(App|Program|Native|WorkArea|Diag|Autostart)\.cs$'             = @("startup", "handoff")
  '^installer/'                                                                = @("psblocked")
  '^tools/tests/lib/'                                                          = @("selfcheck")
}
$noWindow = @("selfcheck", "siteunit", "kdfcross", "i18ngen", "psblocked", "..\design\contrast-b")
$start = @("startup")

$changed = @()
if ($Only.Count -gt 0) { $list = @($start + ($Only | ForEach-Object { $_ -split "," }) | Where-Object { $_ } | Select-Object -Unique) }
elseif ($All) { $list = @($start + ($map.Values | ForEach-Object { $_ }) | Select-Object -Unique) }
else {
  if (-not $Base) { $Base = (git -C $repo describe --tags --abbrev=0 --match "v*" 2>$null); if (-not $Base) { $Base = "HEAD~1" } }
  $changed = @(git -C $repo diff --name-only $Base 2>$null) + @(git -C $repo diff --name-only 2>$null) | Where-Object { $_ } | Select-Object -Unique
  $hit = foreach ($f in $changed) { foreach ($k in $map.Keys) { if ($f -match $k) { $map[$k] } } }
  $list = @($start + $hit | Select-Object -Unique)
}
if ($All -or $ClipboardApproved) { $list = @($list + @("clip", "lockwidget\inputext") | Select-Object -Unique) }
# inject refuses to start within 2 minutes of any input, and sitefill types for ~15 minutes: run inject before sitefill
# (2026-10-05 0.3.32 full run: the map order put inject right after sitefill and it did not run)
$si = [array]::IndexOf($list, "sitefill"); $ii = [array]::IndexOf($list, "inject")
if ($si -ge 0 -and $ii -gt $si) { $list = @($list[0..($si - 1)] + "inject" + ($list[$si..($list.Count - 1)] | Where-Object { $_ -ne "inject" })) }
# no-window checks first (seconds), then window tests
$list = @($list | Where-Object { $noWindow -contains $_ }) + @($list | Where-Object { $noWindow -notcontains $_ })
"base: $(if ($Base) { $Base } else { '-' })  changed files: $($changed.Count)  scripts: $($list -join ', ')"
if ($DryRun) { $changed | ForEach-Object { "  changed: $_" }; return }   # selection only, nothing runs

$ver = (Get-Item $exe).VersionInfo.FileVersion -replace '\.0$', ''
$out = Join-Path $repo ("docs\test-logs\{0:yyyy-MM-dd}_{1}\run-{0:HHmmss}" -f (Get-Date), $ver)
New-Item -ItemType Directory -Force $out | Out-Null
$hash = (Get-FileHash $exe).Hash

$rows = @(); $stopped = $false; $prevEnd = $null
foreach ($name in $list) {
  $script = Join-Path $PSScriptRoot "$name.ps1"
  $tag = ($name -replace '[\\.]+', '_').Trim('_')
  if ($stopped) { $rows += "| $name | not run | | (stopped: somebody used the PC) |"; continue }
  if ($noWindow -notcontains $name) {
    Start-Sleep -Seconds 4
    $idle = (Get-SystemIdleMs) / 1000
    $since = if ($prevEnd) { ((Get-Date) - $prevEnd).TotalSeconds } else { [double]::MaxValue }
    $firstWindow = -not $prevEnd
    if ($idle -ge 0 -and (($firstWindow -and $idle -lt $IdleSeconds) -or (-not $firstWindow -and $idle -lt $since - 1))) {
      $stopped = $true; $rows += "| $name | not run | | (input $([int]$idle) s ago, after the previous test - stopped) |"; continue
    }
  }
  $log = Join-Path $out "$tag.txt"
  $args2 = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$script`"")
  if ($name -eq "lockwidget\inputext") { $args2 += @("-Exe", "`"$exe`"", "-UserApproved") }
  elseif ($name -eq "clip") { $args2 += "`"$exe`"" }
  elseif ($name -in @("inject", "sitefill")) { $args2 += @("-Foreground", "-Exe", "`"$exe`"") }   # they type into windows in front
  elseif ($noWindow -notcontains $name -or $name -eq "psblocked") { $args2 += "`"$exe`"" }
  $t0 = Get-Date
  $pr = Start-Process powershell -ArgumentList $args2 -NoNewWindow -PassThru -RedirectStandardOutput $log -RedirectStandardError "$log.err"
  $null = $pr.Handle   # keeps the handle so ExitCode is readable after WaitForExit
  if (-not $pr.WaitForExit(30 * 60 * 1000)) { Stop-Process -Id $pr.Id -Force -ErrorAction Ignore; $code = "timeout" } else { $code = $pr.ExitCode }
  $min = [Math]::Round(((Get-Date) - $t0).TotalMinutes, 1)
  if ($noWindow -notcontains $name) { $prevEnd = Get-Date }
  $judge = (Get-Content $log -ErrorAction Ignore | Where-Object { $_ -like "---- judgement*" -or $_ -like "fail=*" } | Select-Object -Last 1)
  if ((Test-Path "$log.err") -and (Get-Item "$log.err").Length -eq 0) { Remove-Item "$log.err" -Force -ErrorAction Ignore }
  $rows += "| $name | $code | $min | $judge |"
  "{0,-28} exit={1,-7} {2,5} min  {3}" -f $name, $code, $min, $judge
}
$md = @("# Test run - $([IO.Path]::GetFileName($exe)) $ver", "", "- exe: ``$exe``", "- SHA-256: ``$hash``", "- started: $(Get-Date -Format 'yyyy-MM-dd HH:mm')",
        "- base: $(if ($Base) { $Base } else { '-' }), changed files: $($changed.Count)", "",
        "| script | exit | min | judgement |", "|---|---|---|---|") + $rows
Set-Content (Join-Path $out "summary.md") -Value $md -Encoding UTF8
"summary: $out\summary.md"
