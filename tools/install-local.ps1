# 1Key 를 고정 위치에 설치하거나 새 버전으로 바꿔 끼운다 (2026-10-02, Defender 탐지 대응 B안).
# 빌드 폴더(build\0.2.xx\)에서 바로 실행하면 버전마다 경로가 달라, 1Key 가 자동 실행 등록을 매번 새 경로로 고쳐 쓴다
# (2026-10-02 09:09 Behavior:Win32/Persistence.A!ml 탐지·격리). 고정 위치에서 실행하면 자동 실행은 처음 한 번만 등록되고,
# 이후 업데이트는 이 파일만 바꾼다(등록 경로가 그대로라 다시 등록하지 않는다).
#
#   powershell -ExecutionPolicy Bypass -File tools\install-local.ps1 [설치할 exe 경로]
#
# exe 를 주지 않으면 build\ 아래 가장 높은 버전 폴더의 1Key.exe. 설치 위치: %LOCALAPPDATA%\Programs\1Key\1Key.exe
# 설치 위치의 1Key 가 실행 중이면 바꾸지 않는다(트레이 › 종료 뒤 다시). 설정 파일(%LOCALAPPDATA%\1Key)은 건드리지 않는다.
# 파일 이름을 바꾸거나 다른 프로세스를 끄지 않는다.
param([string]$Exe = "")
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
if (-not $Exe) {
  $Exe = Get-ChildItem "$repo\build" -Directory |
    Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' -and (Test-Path "$($_.FullName)\1Key.exe") } |
    Sort-Object { [version]$_.Name } | Select-Object -Last 1 | ForEach-Object { "$($_.FullName)\1Key.exe" }
}
if (-not $Exe -or -not (Test-Path -LiteralPath $Exe)) { "설치할 1Key.exe 가 없습니다: $Exe"; exit 1 }
$dir = Join-Path $env:LOCALAPPDATA "Programs\1Key"
$dst = Join-Path $dir "1Key.exe"
$running = @(Get-CimInstance Win32_Process -Filter "Name='1Key.exe'" |
  Where-Object { $_.ExecutablePath -and ([IO.Path]::GetFullPath($_.ExecutablePath) -ieq [IO.Path]::GetFullPath($dst)) })
if ($running.Count -gt 0) { "설치 위치의 1Key 가 실행 중입니다. 트레이 메뉴 › 종료로 끈 뒤 다시 실행하세요."; exit 1 }
New-Item -ItemType Directory -Force $dir | Out-Null
try { Copy-Item -LiteralPath $Exe -Destination $dst -Force }
catch { "복사하지 못했습니다(실행 중이거나 권한 문제): $($_.Exception.Message)"; exit 1 }
$a = (Get-FileHash -LiteralPath $Exe -Algorithm SHA256).Hash
$b = (Get-FileHash -LiteralPath $dst -Algorithm SHA256).Hash
if ($a -ne $b) { "복사 확인 실패: 해시가 다릅니다."; exit 1 }
$ver = (Get-Item -LiteralPath $dst).VersionInfo.ProductVersion
"설치했습니다: $dst (버전 $ver)"
"수정 시각은 원본 빌드 시각 그대로입니다(복사는 수정 시각을 바꾸지 않음): $((Get-Item -LiteralPath $dst).LastWriteTime)"
"원본: $Exe"
"SHA-256 $b"
