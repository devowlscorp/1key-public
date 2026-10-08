# 예전에 받아 쓰던 1Key 단일 파일을 찾아 휴지통으로 옮긴다(설치 파일 installer\1Key.nsi 가 설치 중에 부른다, 2026-10-02 사용자 결정).
# 디스크 전체를 뒤지지 않는다. 찾는 곳: 다운로드, 바탕 화면, %LOCALAPPDATA%\1Key(예전 안내의 위치), 그리고 지금 자동 실행(Run 값 "1Key",
# 예약 작업 "1Key_AutoStart")이 가리키는 파일의 폴더. 대상: 그 폴더 바로 아래의 1Key*.exe 가운데 파일 정보의 제품명·회사가 1Key 인 것,
# 새 설치본(-Keep)은 제외. 지우지 않고 휴지통으로 옮긴다(되살릴 수 있다). 설정 파일(config.dat 등)은 건드리지 않는다.
#
#   -Keep <설치본 경로> -Out <목록 파일>     찾기만: 목록 파일 첫 줄 = 개수, 다음 줄부터 경로
#   -Recycle <목록 파일>                     그 목록의 파일을 휴지통으로(다시 같은 조건을 확인한 뒤). 옮긴 개수를 출력
#   -Dirs <폴더;폴더>                         시험용: 찾는 곳을 이 폴더들로만 바꾼다
#   -StartLink <바로 가기 경로>               찾기·옮기기 때 시작 프로그램 폴더의 자동 실행 바로 가기(대상 보존에 씀)
#   -Protect <항목;항목>                      시험용(-Dirs 와 함께): 실제 등록 조회 대신 이 항목들을 자동 실행 대상으로 본다. "?" = 조회 실패,
#                                            "run:<명령>" = Run 값의 명령 문자열(실제와 같은 해석 함수를 거친다), 그 밖 = 대상 경로(같은 경로 해석)
# 자동 실행 대상 보존(0.2.159, Codex R157-1): 자동 실행을 잠시 중단한 동안 설치는 예전 등록을 옮기지 않으므로, Run 값·예약 작업·시작
# 프로그램 바로 가기가 가리키는 파일은 찾기와 옮기기 모두에서 뺀다(옮기기 직전에 다시 조회). 조회가 불확실하면 찾기는 "hold" 를 내고
# 옮기기는 아무것도 옮기지 않는다 — 등록은 남고 실행 파일만 없어지는 상태를 만들지 않는다. 등록을 읽었어도 그 대상 경로를 해석하지
# 못하면(빈 값, 닫히지 않은 따옴표, 따옴표 없는 명령이 .exe 로 끝나지 않음, 상대 경로, 경로 정규화 실패) 역시 불확실로 본다(Codex R160-1).
# 대상 파일이 없어도 해석한 경로는 보존 목록에 둔다.
#   -Check <파일 경로>                       자동 실행(Run 값) 이전 전 확인: 파일이 있고 제품명·회사가 1Key 이며 설치본(-Keep)이 아니면 "1Key",
#                                            아니면(다른 프로그램, 없는 파일, 파일 정보를 읽을 수 없음) "other" 를 출력(2026-10-02 Codex 13:05)
#   -Link <바로 가기 경로>                    시작 프로그램 폴더의 자동 실행 바로 가기 확인(0.2.149, 읽기만): 없으면 "missing", 대상이 설치본(-Keep)이고
#                                            인자가 --tray 면 "ours", 대상만 설치본이면 "oursdiff", 다른 대상이면 "other", 읽지 못하면 "unknown"
param([string]$Keep = "", [string]$Out = "", [string]$Recycle = "", [string]$Dirs = "", [string]$Check = "", [string]$Link = "",
      [string]$StartLink = "", [string]$Protect = "")
$ErrorActionPreference = "Stop"

function Is1Key([string]$path) {
  try {
    $i = (Get-Item -LiteralPath $path).VersionInfo
    return ($i.ProductName -eq "1Key" -and $i.CompanyName -eq "1Key")
  } catch { return $false }
}
function ExeOf([string]$cmd) {
  $cmd = $cmd.Trim()
  if ($cmd.StartsWith('"')) { $q = $cmd.IndexOf('"', 1); if ($q -gt 0) { return $cmd.Substring(1, $q - 1) } else { return $cmd.Trim('"') } }
  $sp = $cmd.IndexOf(" --"); if ($sp -gt 0) { return $cmd.Substring(0, $sp) } else { return $cmd }
}
# Run 값 명령 → 실행 파일 경로. 해석할 수 없으면 $null(= 불확실). 받는 꼴: "경로"[ 인자] 또는 따옴표 없는 ...\x.exe[ 인자]
function ParseRunCmd([string]$cmd) {
  if ($null -eq $cmd) { return $null }
  $cmd = $cmd.Trim()
  if (-not $cmd) { return $null }
  if ($cmd.StartsWith('"')) {
    $q = $cmd.IndexOf('"', 1)
    if ($q -lt 2) { return $null }                                   # 닫히지 않았거나 빈 따옴표
    $rest = $cmd.Substring($q + 1)
    if ($rest -and -not $rest.StartsWith(' ')) { return $null }       # "경로"인자 처럼 붙은 꼴
    return $cmd.Substring(1, $q - 1)
  }
  if ($cmd.Contains('"')) { return $null }
  $m = [regex]::Match($cmd, '^(.+?\.exe)(\s.*)?$', 'IgnoreCase')
  if (-not $m.Success) { return $null }                               # .exe 로 끝나지 않는 따옴표 없는 명령
  # 따옴표 없는 공백 포함 경로는 Windows 의 해석(C:\Program.exe 먼저 등)과 어긋날 수 있다 — 추측하지 않고 보류(Codex 08:10). 환경 변수 확장 뒤에도 본다
  if ($m.Groups[1].Value.Contains(' ') -or [Environment]::ExpandEnvironmentVariables($m.Groups[1].Value).Contains(' ')) { return $null }
  return $m.Groups[1].Value
}
# 대상 경로 → 정규화한 전체 경로. 빈 값·상대 경로·정규화 실패면 $null(= 불확실). 파일이 없어도 된다.
function ResolveTarget([string]$path) {
  if ($null -eq $path) { return $null }
  $path = [Environment]::ExpandEnvironmentVariables($path.Trim())
  if (-not $path -or $path.Contains('"')) { return $null }
  try {
    if (-not [IO.Path]::IsPathRooted($path)) { return $null }
    $root = [IO.Path]::GetPathRoot($path)
    if ($root -notmatch '^[A-Za-z]:\\$' -and $root -notmatch '^\\\\[^\\]+\\[^\\]+') { return $null }   # C:\ 또는 \\서버\공유 만
    return [IO.Path]::GetFullPath($path)
  } catch { return $null }
}
function Same([string]$a, [string]$b) {
  if (-not $a -or -not $b) { return $false }
  try { return ([IO.Path]::GetFullPath($a) -ieq [IO.Path]::GetFullPath($b)) } catch { return $false }
}

# 자동 실행 등록이 가리키는 파일들. Ok = 모든 조회가 "있음"(대상 경로까지 해석) 또는 "없음"으로 확실히 끝났다.
function Targets {
  $r = @{ Ok = $true; Paths = New-Object System.Collections.Generic.List[string] }
  $add = { param($resolved) if ($resolved) { $r.Paths.Add($resolved) } else { $r.Ok = $false } }
  if ($Dirs) {   # 시험: 실제 등록은 보지 않는다. 해석은 실제와 같은 함수로
    foreach ($p in $Protect.Split(';')) {
      if (-not $p) { continue }
      if ($p -eq '?') { $r.Ok = $false }
      elseif ($p.StartsWith('run:')) { & $add (ResolveTarget (ParseRunCmd $p.Substring(4))) }
      else { & $add (ResolveTarget $p) }
    }
    return $r
  }
  try {
    $k = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
    if ($k) {
      try { $v = $k.GetValue('1Key') } finally { $k.Close() }
      if ($null -ne $v) { & $add (ResolveTarget (ParseRunCmd ([string]$v))) }
    }
  } catch { $r.Ok = $false }
  try {
    $t = Get-ScheduledTask -TaskName '1Key_AutoStart' -ErrorAction Stop
    foreach ($a in @($t.Actions)) {
      $x = [string]$a.Execute
      if ($x.StartsWith('"') -and $x.EndsWith('"') -and $x.Length -gt 2) { $x = $x.Substring(1, $x.Length - 2) }   # 작업의 프로그램 칸은 경로만(인자는 따로)
      & $add (ResolveTarget $x)
    }
  } catch { if ($_.FullyQualifiedErrorId -notlike 'CmdletizationQuery_NotFound*') { $r.Ok = $false } }   # 작업 없음만 "없음"
  if ($StartLink) {
    try {
      if (Test-Path -LiteralPath $StartLink) {
        $s = (New-Object -ComObject WScript.Shell).CreateShortcut($StartLink)
        & $add (ResolveTarget ([string]$s.TargetPath))
      }
    } catch { $r.Ok = $false }
  }
  return $r
}
function IsTarget([string]$path, $tg) {
  foreach ($x in $tg.Paths) { if (Same $path $x) { return $true } }
  return $false
}

if ($Check) {
  if ((Test-Path -LiteralPath $Check -PathType Leaf) -and (Is1Key $Check) -and -not (Same $Check $Keep)) { "1Key" } else { "other" }
  exit 0
}

if ($Link) {
  try {
    if (-not (Test-Path -LiteralPath $Link)) { "missing"; exit 0 }
    if (Test-Path -LiteralPath $Link -PathType Container) { "unknown"; exit 0 }
    $s = (New-Object -ComObject WScript.Shell).CreateShortcut($Link)
    if (-not (Same ([Environment]::ExpandEnvironmentVariables($s.TargetPath)) $Keep)) { "other"; exit 0 }
    if ($s.Arguments -ceq "--tray") { "ours" } else { "oursdiff" }
  } catch { "unknown" }
  exit 0
}

if ($Recycle) {
  Add-Type -AssemblyName Microsoft.VisualBasic
  $n = 0
  $tg = Targets   # 옮기기 직전에 다시 조회
  if (-not $tg.Ok) { "0"; exit 0 }
  foreach ($p in (Get-Content -LiteralPath $Recycle -Encoding Unicode | Select-Object -Skip 1)) {
    if (-not $p -or -not (Test-Path -LiteralPath $p) -or -not (Is1Key $p) -or (Same $p $Keep) -or (IsTarget $p $tg)) { continue }
    try { [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile($p, 'OnlyErrorDialogs', 'SendToRecycleBin'); $n++ } catch { }
  }
  "$n"
  exit 0
}

$where = New-Object System.Collections.Generic.List[string]
if ($Dirs) { foreach ($d in $Dirs.Split(';')) { if ($d) { $where.Add($d) } } }
else {
  $dl = $null
  try { $dl = (New-Object -ComObject Shell.Application).NameSpace('shell:Downloads').Self.Path } catch { }
  if (-not $dl) { $dl = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'Downloads' }
  $where.Add($dl)
  $where.Add([Environment]::GetFolderPath('Desktop'))
  $where.Add((Join-Path $env:LOCALAPPDATA '1Key'))
  try { $run = (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name '1Key' -ErrorAction Stop).'1Key'; if ($run) { $where.Add((Split-Path -Parent (ExeOf $run))) } } catch { }
  try { $t = Get-ScheduledTask -TaskName '1Key_AutoStart' -ErrorAction Stop; if ($t) { $where.Add((Split-Path -Parent (ExeOf ('"' + $t.Actions[0].Execute.Trim('"') + '"')))) } } catch { }
}
$tg = Targets
if (-not $tg.Ok) {
  if ($Out) { Set-Content -LiteralPath $Out -Value @("hold") -Encoding Unicode } else { "hold" }
  exit 0
}
$found = New-Object System.Collections.Generic.List[string]
$seen = @{}
foreach ($d in $where) {
  if (-not $d -or -not (Test-Path -LiteralPath $d -PathType Container)) { continue }
  $key = try { [IO.Path]::GetFullPath($d).TrimEnd('\').ToLowerInvariant() } catch { $d }
  if ($seen.ContainsKey($key)) { continue }
  $seen[$key] = $true
  foreach ($f in (Get-ChildItem -LiteralPath $d -Filter '1Key*.exe' -File -ErrorAction SilentlyContinue)) {
    if ((Same $f.FullName $Keep) -or -not (Is1Key $f.FullName) -or (IsTarget $f.FullName $tg)) { continue }
    $found.Add($f.FullName)
  }
}
$lines = @("$($found.Count)") + $found
# 목록 파일은 UTF-16LE: 설치 파일이 FileReadUTF16LE 로 읽는다(한글 경로)
if ($Out) { Set-Content -LiteralPath $Out -Value $lines -Encoding Unicode } else { $lines }
exit 0
