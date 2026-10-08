# 1Key - NativeAOT 빌드 스크립트 (결과물: build\<버전>\1Key.exe, 약 1.7MB 순수 네이티브)
#
# 사전 준비(이 PC 에만, 한 번만): Visual Studio 2022 Build Tools + "C++ 데스크톱 개발" + Windows 11 SDK
#   winget install --id Microsoft.VisualStudio.2022.BuildTools --override "--quiet --wait --add Microsoft.VisualStudio.Workload.VCTools --add Microsoft.VisualStudio.Component.Windows11SDK.22621 --includeRecommended" --accept-source-agreements --accept-package-agreements
#
# 결과물은 .NET 런타임도 빌드 도구도 필요 없는 순수 네이티브 exe 입니다.
# 배포 대상 PC 에는 이 exe 하나만 복사하면 됩니다.
#
# 실행:  powershell -ExecutionPolicy Bypass -File build-aot.ps1

$ErrorActionPreference = "Stop"

# 1) Visual Studio(BuildTools 포함) 위치 찾기 — BuildTools 는 -products * 가 필요하다.
$vswhere = "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) { throw "vswhere.exe 를 찾을 수 없습니다. VS Build Tools 를 먼저 설치하세요." }
$vsPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vsPath) { throw "C++ 도구(VC.Tools)가 설치돼 있지 않습니다. Build Tools 설치 시 'C++ 데스크톱 개발' 워크로드를 포함하세요." }
Write-Host "Visual Studio: $vsPath" -ForegroundColor Cyan

# 2) MSVC 개발자 환경 진입 (LIB/INCLUDE/PATH 설정). AOT 링크 타겟이 vswhere 를 PATH 에서 찾으므로 함께 추가.
$env:PATH = "C:\Program Files (x86)\Microsoft Visual Studio\Installer;$env:PATH"
Import-Module (Join-Path $vsPath "Common7\Tools\Microsoft.VisualStudio.DevShell.dll")
Enter-VsDevShell -VsInstallPath $vsPath -SkipAutomaticLocation -DevCmdArguments "-arch=x64 -host_arch=x64" | Out-Null

# 3) NativeAOT 빌드
#    결과물은 build\<버전>\1Key.exe 로 버전별 폴더에 따로 둔다.
#    이전 버전 exe 를 실행 중이어도 파일 잠금 때문에 빌드가 실패하지 않는다.
$proj = Join-Path $PSScriptRoot "src\OneKey\OneKey.csproj"

$verText = ([xml](Get-Content $proj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $verText) { throw "csproj 에서 <Version> 을 읽지 못했습니다." }
$stage = Join-Path $PSScriptRoot "build\$verText"

New-Item -ItemType Directory -Force $stage | Out-Null
$built = Join-Path $stage "1Key.exe"

# 같은 버전을 이미 실행 중이면 그 파일을 덮어쓸 수 없다. 다만 실행 중인 exe 도 "이름 바꾸기"는
# 되므로, 옆으로 밀어 두고 새 파일을 쓴다. 밀어 둔 파일은 4) 에서 build\superseded 로 옮겨 보관한다.
if (Test-Path $built) {
    try { Rename-Item $built -NewName ("1Key.old-{0}.exe" -f (Get-Date -Format "yyyyMMdd-HHmmss")) -ErrorAction Stop }
    catch { throw "기존 $built 를 옆으로 밀어 두지 못했습니다: $($_.Exception.Message)" }
}

Write-Host "NativeAOT 빌드 중 (v$verText)..." -ForegroundColor Cyan
dotnet publish $proj -c Release -r win-x64 -p:PublishAot=true -o $stage --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 실패 (exit $LASTEXITCODE)" }

if (-not (Test-Path $built)) { throw "빌드 결과 exe 를 찾지 못했습니다: $built" }
$ver = (Get-Item $built).VersionInfo.FileVersion
$mb  = [math]::Round((Get-Item $built).Length / 1MB, 2)

# 4) 배포에 필요한 것은 exe 하나뿐이다. 밀어 둔 옛 exe 는 지우지 않고 build\superseded 로 옮겨 보관한다
#    (같은 버전으로 다시 빌드하면 검토·시험한 exe 가 사라진다. 2026-09-29 에는 같은 소스로 다시 빌드해도 해시가 달라 원본을 되살리지 못했다).
#    나머지(pdb 등)는 지운다. 아직 실행 중인 옛 파일은 옮기지도 지우지도 못하므로 그냥 남겨 둔다.
$keep = Join-Path $PSScriptRoot "build\superseded"
Get-ChildItem $stage -File | Where-Object { $_.Name -ne "1Key.exe" } | ForEach-Object {
    if ($_.Name -like "1Key.old-*.exe") {
        New-Item -ItemType Directory -Force $keep | Out-Null
        $full = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
        $dest = Join-Path $keep ("1Key-{0}-{1}-{2}.exe" -f $verText, ($_.BaseName -replace '^1Key\.old-', ''), $full.Substring(0, 8))
        try {
            Move-Item $_.FullName $dest -Force -ErrorAction Stop
            Write-Host "이전 exe 보관: $dest" -ForegroundColor Yellow
            # 파일 이름은 해시 앞 8자라 겹칠 수 있다: 전체 해시는 기록 파일에 남긴다
            Add-Content (Join-Path $PSScriptRoot "build\build-log.txt") ("{0}  superseded  v{1}  SHA-256 {2}  {3}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $verText, $full, (Split-Path $dest -Leaf)) -Encoding UTF8
        } catch { }
    }
    else { try { Remove-Item $_.FullName -Force -ErrorAction Stop } catch { } }
}

# 만든 exe 의 전체 SHA-256 을 보여 주고 기록한다(검토·시험 보고는 버전이 아니라 이 해시로 가리킨다)
$sha = (Get-FileHash $built -Algorithm SHA256).Hash
Add-Content (Join-Path $PSScriptRoot "build\build-log.txt") ("{0}  built       v{1}  SHA-256 {2}  build\{3}\1Key.exe" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $verText, $sha, $verText) -Encoding UTF8
Write-Host "`n완료: $built  (v$ver, $mb MB)" -ForegroundColor Green
Write-Host "      SHA-256 $sha  (build\build-log.txt 에 기록)" -ForegroundColor Green
Write-Host "      이 파일 하나만 배포하면 됩니다. 런타임 설치가 필요 없습니다." -ForegroundColor Green

# 5) 설치 파일(2026-10-02): NSIS 가 있으면 build\<버전>\1Key-Setup-<버전>.exe 도 만든다. 동료 배포는 이 설치 파일로 한다
#    (고정 위치 %LOCALAPPDATA%\Programs\1Key 에 설치해 자동 실행 경로가 늘 같게, 설정 › 앱에 제거 항목). 없으면 건너뛴다.
$nsis = @("${env:ProgramFiles(x86)}\NSIS\makensis.exe", "$env:ProgramFiles\NSIS\makensis.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($nsis) {
    $setup = Join-Path $stage "1Key-Setup-$verText.exe"
    & $nsis -V2 "-DVERSION=$verText" ("-DVERNUM=" + ($verText -replace '-.*$', '')) "-DSRC=$built" "-DOUT=$setup" ("-DICON=" + (Join-Path $PSScriptRoot "src\OneKey\app.ico")) (Join-Path $PSScriptRoot "installer\1Key.nsi")
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $setup)) { throw "설치 파일을 만들지 못했습니다 (makensis exit $LASTEXITCODE)" }
    $ssha = (Get-FileHash $setup -Algorithm SHA256).Hash
    Add-Content (Join-Path $PSScriptRoot "build\build-log.txt") ("{0}  setup       v{1}  SHA-256 {2}  build\{3}\1Key-Setup-{3}.exe" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $verText, $ssha, $verText) -Encoding UTF8
    Write-Host "      설치 파일: $setup" -ForegroundColor Green
    Write-Host "      SHA-256 $ssha" -ForegroundColor Green
}
else { Write-Host "      NSIS(makensis) 가 없어 설치 파일은 만들지 않았습니다." -ForegroundColor Yellow }
