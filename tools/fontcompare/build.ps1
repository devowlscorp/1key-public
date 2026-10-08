# Font comparison mockup (Codex proposal section 7, 2026-09-30). Builds build\fontcompare\FontCompare.exe with NativeAOT
# and copies the Pretendard files (and their OFL licence) next to it into Fonts\. Separate from 1Key: it does not touch
# 1Key's build folder, its settings or the fonts installed on this PC. ASCII only.
$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$vswhere = "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"
$vsPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vsPath) { throw "C++ tools (VC.Tools) not found" }
$env:PATH = "C:\Program Files (x86)\Microsoft Visual Studio\Installer;$env:PATH"
Import-Module (Join-Path $vsPath "Common7\Tools\Microsoft.VisualStudio.DevShell.dll")
Enter-VsDevShell -VsInstallPath $vsPath -SkipAutomaticLocation -DevCmdArguments "-arch=x64 -host_arch=x64" | Out-Null

$out = Join-Path $repo "build\fontcompare"
New-Item -ItemType Directory -Force $out | Out-Null
dotnet publish (Join-Path $PSScriptRoot "FontCompare.csproj") -c Release -r win-x64 -p:PublishAot=true -o $out --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
Get-ChildItem $out -File | Where-Object { $_.Name -ne "FontCompare.exe" } | Remove-Item -Force -ErrorAction Ignore
$fonts = Join-Path $out "Fonts"
New-Item -ItemType Directory -Force $fonts | Out-Null
foreach ($f in "Pretendard-Regular.ttf", "Pretendard-SemiBold.ttf", "Pretendard-Bold.ttf", "OFL-Pretendard.txt") { Copy-Item (Join-Path $repo "src\OneKey\Fonts\$f") $fonts -Force }
$exe = Join-Path $out "FontCompare.exe"
"built: $exe  SHA-256 " + (Get-FileHash $exe -Algorithm SHA256).Hash
