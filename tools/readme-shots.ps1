# README 스크린샷: 시험용 설정 폴더(ONEKEY_TEST=1, 따로 된 접미사)에 예시 항목을 넣고 밝음·어두움으로 목록·설정·잠금 위젯을,
# Windows 테마로 작업 표시줄 고양이와 정시 시계를 찍는다. 사용자의 1Key 와 설정은 건드리지 않는다. 창이 뜨므로 PC 를 쓰지 않을 때만.
#   powershell -ExecutionPolicy Bypass -File tools\readme-shots.ps1 [-Exe <1Key.exe>] [-Out docs\images]
param([string]$Exe = "", [string]$Out = "")
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "tests\lib\Check.ps1")
$repo = Resolve-Path (Join-Path $PSScriptRoot "..")
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
if (-not $Out) { $Out = Join-Path $repo "docs\images" }
New-Item -ItemType Directory -Force $Out | Out-Null
$sp = Join-Path $env:TEMP "1Key-readme-shots"; New-Item -ItemType Directory -Force $sp | Out-Null
$suffix = ".readme"
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type -ReferencedAssemblies System.Drawing @'
using System;using System.Runtime.InteropServices;using System.Text;using System.Drawing;
public class RS {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
  public static void Top(IntPtr h) { if (h != IntPtr.Zero) SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); }
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static IntPtr Find(uint pid, string cls) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls && IsWindowVisible(h)) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
  public static bool Shot(IntPtr h, string path) {
    RECT r; if (!GetWindowRect(h, out r)) return false; int w = r.R - r.L, hh = r.B - r.T; if (w <= 0 || hh <= 0) return false;
    using (var b = new Bitmap(w, hh)) { using (var g = Graphics.FromImage(b)) { IntPtr dc = g.GetHdc(); bool ok = PrintWindow(h, dc, 2); g.ReleaseHdc(dc); if (!ok) return false; } b.Save(path); }
    return true;
  }
  public static Color Bg;
  static bool Near(Color a, Color b) { return Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) < 12; }
  public static bool Screen(int x, int y, int w, int h, string path) { return Screen(x, y, w, h, h, path); }
  // checkH: 위에서부터 가림 바탕이어야 하는 높이(그 아래는 작업 표시줄 등 - 검사하지 않는다)
  public static bool Screen(int x, int y, int w, int h, int checkH, string path) {
    using (var b = new Bitmap(w, h)) {
      using (var g = Graphics.FromImage(b)) g.CopyFromScreen(x, y, 0, 0, new Size(w, h));
      // 왼쪽 위·왼쪽 아래 모서리와 왼쪽 가장자리 가운데는 가림 바탕이어야 한다 - 아니면 다른 창이 비친 것: 저장하지 않는다
      if (!Near(b.GetPixel(1, 1), Bg) || !Near(b.GetPixel(1, checkH - 2), Bg) || !Near(b.GetPixel(1, checkH / 2), Bg)) return false;
      b.Save(path);
    }
    return true;
  }
  public static bool ScreenOf(IntPtr h, string path, int pad) { RECT r; if (!GetWindowRect(h, out r)) return false; return Screen(r.L - pad, r.T - pad, r.R - r.L + pad * 2, r.B - r.T + pad * 2, path); }
}
'@
[void][RS]::SetProcessDPIAware()
# 예시 항목(가짜 값)
$items = @(
  @{ Form = 4006; Name = "메일 로그인"; Id = "cat@example.com"; Pw = "example-pw-1" },
  @{ Form = 4006; Name = "쇼핑몰"; Id = "nyang"; Pw = "example-pw-2" },
  @{ Form = 4001; Name = "결재 비밀번호"; Text = "example-pw-3" },
  @{ Form = 4001; Name = "자주 쓰는 인사말"; Text = "안녕하세요. 확인 후 회신드리겠습니다." },
  @{ Form = 4001; Name = "택배 받는 주소"; Text = "서울시 고양구 냥냥로 12" }
)
function Click($id, $ms = 700) { [void][RS]::PostMessageW([RS]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds $ms }
function SetText($id, $s) { [void][RS]::SendMessageW([RS]::GetDlgItem($m, $id), 0x000C, [IntPtr]::Zero, [string]$s) }
function CloseBox() { for ($i = 0; $i -lt 12; $i++) { $b = [RS]::Find([uint32]$p.Id, "OneKeyDialog"); if ($b -ne [IntPtr]::Zero) { [void][RS]::PostMessageW($b, 0x0111, [IntPtr]1, [IntPtr]::Zero); Start-Sleep -Milliseconds 400; return }; Start-Sleep -Milliseconds 100 } }
function Start1Key() {
  $script:p = Start-Process $exe -PassThru; $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $script:m = [RS]::Find([uint32]$script:p.Id, "OneKeyMainWindow$suffix"); if ($script:m -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 100 }
  Start-Sleep -Milliseconds 900
}
function Quit1Key() { if ($script:p -and -not $script:p.HasExited) { [void][RS]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i = 0; $i -lt 40 -and -not $script:p.HasExited; $i++) { Start-Sleep -Milliseconds 100 }; if (-not $script:p.HasExited) { Stop-Process -Id $script:p.Id -Force } } }
function Bg($color) {
  $f = New-Object Windows.Forms.Form; $f.FormBorderStyle = "None"; $f.StartPosition = "Manual"; $wa = [Windows.Forms.Screen]::PrimaryScreen.WorkingArea
  $f.Left = $wa.Left; $f.Top = $wa.Top; $f.Width = $wa.Width; $f.Height = $wa.Height; $f.BackColor = $color; $f.ShowInTaskbar = $false
  $f.TopMost = $true; [RS]::Bg = $color
  $f.Show(); [Windows.Forms.Application]::DoEvents(); [void][RS]::SetForegroundWindow($f.Handle); [Windows.Forms.Application]::DoEvents(); $f
}
$made = @()
try {
  Stop-TestInstances $suffix
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_NO_AUTOLOCK = "1"
  $winLight = ((Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' -ErrorAction Ignore).SystemUsesLightTheme -eq 1)
  foreach ($theme in "light", "dark") {
    $env:ONEKEY_TEST_THEME = $theme
    $cfg = Join-Path $sp "cfg-$theme"; if (Test-Path $cfg) { Remove-Item $cfg -Recurse -Force }; New-Item -ItemType Directory -Force $cfg | Out-Null
    $env:ONEKEY_CONFIG_DIR = $cfg
    $env:ONEKEY_TEST_LOCKWIDGET = "0"
    $taskbar = ($theme -eq "light") -eq $winLight   # 작업 표시줄 고양이는 Windows 테마 색: 그 테마 차례에만 찍는다
    $env:ONEKEY_TEST_CLOCK = $(if ($taskbar) { "1" } else { $null })
    Start1Key
    SetText 101 "Readme-Shots-77"; SetText 102 "Readme-Shots-77"; Click 103 1500; CloseBox
    foreach ($it in $items) {
      Click 203; Click $it.Form
      if ($it.Form -eq 4006) { [void][RS]::PostMessageW([RS]::GetDlgItem($m, 348), 0x0100, [IntPtr]0x25, [IntPtr]::Zero); Start-Sleep -Milliseconds 900 }   # 용도: 커서 자리
      SetText 301 $it.Name
      if ($it.Form -eq 4006) { SetText 302 $it.Id; SetText 331 $it.Pw } else { SetText 302 $it.Text }
      Click 310 900; CloseBox
      if ([RS]::GetDlgItem($m, 310) -ne [IntPtr]::Zero) { "save refused: $($it.Name)"; Click 311 700; CloseBox }   # 저장이 안 되면 취소하고 다음
      if (-not ([RS]::GetDlgItem($m, 203) -ne [IntPtr]::Zero)) { Click 240 }
    }
    Start-Sleep -Milliseconds 600
    if ([RS]::Shot($m, "$Out\list-$theme.png")) { $made += "list-$theme" }
    Click 220 900
    if ([RS]::Shot($m, "$Out\settings-$theme.png")) { $made += "settings-$theme" }
    if ($taskbar) {
      [void][RS]::PostMessageW([RS]::GetDlgItem($m, 2032), 0x0100, [IntPtr]0x20, [IntPtr]::Zero); Start-Sleep -Milliseconds 400   # 트레이 위젯 모드: 스페이스로 켬
      Click 2012 1500; CloseBox
      $bg = Bg ([Drawing.Color]::FromArgb($(if ($winLight) { 226 } else { 42 }), $(if ($winLight) { 226 } else { 42 }), $(if ($winLight) { 230 } else { 46 })))
      [void][RS]::PostMessageW($m, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)   # WM_CLOSE: 트레이로 → 고양이가 나온다(정시 시계는 시험 모드라 바로)
      $wa = [Windows.Forms.Screen]::PrimaryScreen.WorkingArea
      $s = [Math]::Max(1.0, (Get-ItemProperty 'HKCU:\Control Panel\Desktop\WindowMetrics' -ErrorAction Ignore).AppliedDPI / 96.0)
      $w = [int](460 * $s); $h = [int](260 * $s)
      $sb = [Windows.Forms.Screen]::PrimaryScreen.Bounds; $tb = $sb.Bottom - $wa.Bottom   # 작업 표시줄 높이: 함께 찍는다(시스템 트레이는 사용자가 비움)
      foreach ($t in @(@(1400, "a"), @(900, "b"), @(2600, "c"))) {
        Start-Sleep -Milliseconds $t[0]; [Windows.Forms.Application]::DoEvents()
        [RS]::Top([RS]::Find([uint32]$p.Id, "OneKeyFlipClock")); [RS]::Top([RS]::Find([uint32]$p.Id, "OneKeyCat"))
        if ([RS]::Screen($wa.Right - $w, $wa.Bottom - $h, $w, $h + $tb, $h, "$Out\taskbar-$($t[1]).png")) { $made += "taskbar-$($t[1])" }
      }
      Start-Sleep -Milliseconds 9000
      [RS]::Top([RS]::Find([uint32]$p.Id, "OneKeyCat"))
      if ([RS]::Screen($wa.Right - $w, $wa.Bottom - $h, $w, $h + $tb, $h, "$Out\taskbar-cat.png")) { $made += "taskbar-cat" }
      $bg.Close()
    }
    Quit1Key
    # 잠금 위젯: 좁게, 넓어진 뒤 야옹(입을 연 때), 야옹 뒤
    $env:ONEKEY_TEST_LOCKWIDGET = "1"; $env:ONEKEY_TEST_CLOCK = $null
    $bg = Bg ([Drawing.Color]::FromArgb($(if ($theme -eq "light") { 236 } else { 32 }), $(if ($theme -eq "light") { 236 } else { 32 }), $(if ($theme -eq "light") { 240 } else { 36 })))
    Start1Key
    $wd = [RS]::Find([uint32]$p.Id, "OneKeyLockWidget"); [RS]::Top($wd); Start-Sleep -Milliseconds 800
    if ($wd -ne [IntPtr]::Zero -and [RS]::ScreenOf($wd, "$Out\lock-$theme-narrow.png", 10)) { $made += "lock-$theme-narrow" }
    $hostw = [RS]::Find([uint32]$p.Id, "OneKeyLockInput"); $e = [IntPtr]::Zero; if ($hostw -ne [IntPtr]::Zero) { $e = [RS]::GetDlgItem($hostw, 101) }
    [void][RS]::SetForegroundWindow($wd); Start-Sleep -Milliseconds 300   # 넓어지려면 위젯이 앞 창이어야 한다
    if ($e -ne [IntPtr]::Zero) { [void][RS]::PostMessageW($e, 0x0100, [IntPtr]0x23, [IntPtr]0x014F0001) }   # End 키: 글을 넣지 않고 넓어진다
    Start-Sleep -Milliseconds 520
    if ($wd -ne [IntPtr]::Zero -and [RS]::ScreenOf($wd, "$Out\lock-$theme-meow.png", 10)) { $made += "lock-$theme-meow" }
    Start-Sleep -Milliseconds 1500
    if ($wd -ne [IntPtr]::Zero -and [RS]::ScreenOf($wd, "$Out\lock-$theme-wide.png", 10)) { $made += "lock-$theme-wide" }
    Quit1Key; $bg.Close()
  }
} catch { "exception: " + $_.Exception.Message }
finally { Quit1Key; Stop-TestInstances $suffix; foreach ($n in 'ONEKEY_TEST_THEME','ONEKEY_TEST_LOCKWIDGET','ONEKEY_TEST_CLOCK','ONEKEY_CONFIG_DIR','ONEKEY_INSTANCE_SUFFIX','ONEKEY_TEST','ONEKEY_TEST_NO_AUTOLOCK') { Remove-Item "Env:$n" -ErrorAction Ignore } }
"made: " + ($made -join ", ")
