# Website bookmarks window test (0.3.77; user decisions 2026-10-06, Codex 02:46 conditions). Own config folder and suffix, local test
# servers on 127.0.0.1 only, a recorder exe stands in for Edge (ONEKEY_TEST_BROWSER_EDGE); the user's browsers are never started.
# WF01 [+ Add] > Website saves a url item (target exactly as typed, browser default); the favicon is fetched once from the SAME
#      scheme/host/PORT at /favicon.ico (no path, query or fragment sent) and stored as a redrawn 64x64 PNG in icons\<id>.fav.png
# WF02 a same-origin redirect (302 -> /real.ico) is followed
# WF03 a redirect to another host is not followed: no icon file, the item is still saved
# WF04 a body over 64 KiB is refused: no icon file
# WF05 a server that answers after 8 s: given up within the 5 s limit (+ margin), no icon file, the window stays responsive
# WF06 the item is deleted while its favicon is still coming: the late result is dropped (no file written, counted as dropped)
# WF07 bad addresses are refused at save (other scheme, user:pass@, space, quote): a warning box, the edit screen stays, nothing saved
# WF08 an address typed without a scheme is saved with https:// in front
# WF09 third row: website tiles sit below the folder tile and share one row
# WF10 browser "Edge" (recorder exe): started once with exactly ONE argument equal to the address (& % # and non-ASCII kept)
# WF11 browser "Chrome" not installed: nothing starts (no launch call), a warning toast
# WF12 [Change] with a PNG: icons\<id>.user.png (64x64) is used; [Default] removes it and the fetched favicon stays;
#      an unsupported image (600x600 PNG / GIF) is refused with a box and changes nothing
# WF13 0.3.76 (previous version) opens the new file: pressing a website tile runs nothing; renaming the folder item and saving keeps
#      every website item's fields (kind, target, browser) byte for byte
# WF14 0.3.76: opening a website tile in edit mode and pressing [Save] is refused - the file is unchanged
# ---- Codex 04:34 (R78-1..4)
# WF15 the image picker returns after a synthetic lock arrived while it was open: the result is dropped, the edit screen is not
#      reopened, no user image is written (counted as dropped)
# WF16 a required WinHTTP setting fails (test fault webicon:option): no request reaches the server, the item is still saved
# WF17 a body sent one byte every 0.4 s: given up within the 5 s total limit, no icon
# WF18 slow same-origin redirects (1.8 s each, 3 hops) exceed the total limit; 5 quick redirects stop after 3 follows (4 requests)
# WF19 locked: the website's hotkey still opens it (recorder gets the address)
# WF21 (Codex 07:22) two image picks in a row: a slow image A, then a quick image B; A finishes last -> B is the one kept and saved
# WF20 (-DefaultBrowser only) browser "Default": the user's default browser opens a local test page (the server sees the request);
#      a tab stays open in that browser
# Run folder: a fresh <test root>\webfav-<time>-<pid> per run (nothing that existed is deleted), never inside the real settings folder.
# Not covered here: the administrator path (1Key elevated -> Explorer) - by hand; the real Edge / Chrome / default browser. ASCII only.
param([string]$Exe = "", [string]$OldExe = "", [switch]$DefaultBrowser)
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
$ids = @(); 1..19 + 21 | ForEach-Object { $ids += "WF{0:D2}" -f $_ }; if ($DefaultBrowser) { $ids += "WF20" }
Start-Checks -Required $ids
$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$old = if ($OldExe) { $OldExe } else { Join-Path $repo "build\0.3.76\1Key.exe" }
$suffix = ".wbf"
# R78-4: a fresh folder for this run only, inside the test root (same rule as backup.ps1 T44-4); nothing that existed is deleted
$base = [IO.Path]::GetFullPath($sp).TrimEnd('\')
$real = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA "1Key")).TrimEnd('\')
if ($base -eq $real -or $base.StartsWith($real + "\", [StringComparison]::OrdinalIgnoreCase)) { throw "test root is inside the real settings folder: $base" }
for ($d = $base; $d -and $d.Length -gt 3; $d = [IO.Path]::GetDirectoryName($d)) {
  if ((Get-Item -LiteralPath $d -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "test root path has a link: $d" }
}
$root = Join-Path $base ("webfav-{0:yyyyMMdd-HHmmss}-{1}" -f (Get-Date), $PID)
if (Test-Path -LiteralPath $root) { throw "run folder already exists: $root" }
New-Item -ItemType Directory $root -ErrorAction Stop | Out-Null
"run folder: $root"
$work = "$root\work"; $cfg = "$root\cfg"; $cfgOld = "$root\cfg_old"
New-Item -ItemType Directory $work -ErrorAction Stop | Out-Null
$master = "Wf-Dummy-5512"
Add-Type -AssemblyName System.Security, System.Drawing
Add-Type @'
using System;using System.Runtime.InteropServices;using System.Text;using System.Collections.Generic;
public class WF {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr GetPropW(IntPtr h, string name);
  [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeoutW(IntPtr h, uint m, IntPtr w, IntPtr l, uint flags, uint ms, out IntPtr res);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  public static void Press(IntPtr m, int id) { IntPtr c = GetDlgItem(m, id); if (c == IntPtr.Zero) return; RECT rc; GetClientRect(c, out rc); IntPtr lp = (IntPtr)((rc.R/2) | ((rc.B/2) << 16)); PostMessageW(c, 0x0201, (IntPtr)1, lp); System.Threading.Thread.Sleep(40); PostMessageW(c, 0x0202, IntPtr.Zero, lp); }
  public static bool Answers(IntPtr h, uint ms) { IntPtr r; return SendMessageTimeoutW(h, 0, IntPtr.Zero, IntPtr.Zero, 2, ms, out r) != IntPtr.Zero; }
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static string Title(IntPtr h) { var t = new StringBuilder(1024); GetWindowTextW(h, t, 1024); return t.ToString(); }
  public static IntPtr MainWnd(uint pid, string cls) { IntPtr f = IntPtr.Zero; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls && (cls.StartsWith("OneKeyMainWindow") || IsWindowVisible(h))) { f = h; return false; } return true; }, IntPtr.Zero); return f; }
  public static string ToastText(uint pid) { var r = new List<string>(); EnumWindows((h,l) => { uint q; GetWindowThreadProcessId(h, out q); if (q == pid && Cls(h) == "OneKeyToast" && IsWindowVisible(h)) EnumChildWindows(h, (c,x) => { r.Add(Title(c)); return true; }, IntPtr.Zero); return true; }, IntPtr.Zero); return string.Join(" / ", r); }
  public static int Count(uint pid, string cls) { int n = 0; EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls && IsWindowVisible(h)) n++; return true; }, IntPtr.Zero); return n; }
}
'@

# ---- recorder exe standing in for the browser: writes its working folder and arguments, then quits (no window)
$recSrc = @'
public static class P { public static void Main(string[] a) {
  var outp = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location), "args-out.txt");
  var lines = new System.Collections.Generic.List<string>(); lines.Add("argc=" + a.Length); lines.AddRange(a);
  System.IO.File.WriteAllLines(outp, lines.ToArray(), new System.Text.UTF8Encoding(false)); } }
'@
$rec = Join-Path $work "WfBrowser.exe"
Add-Type -TypeDefinition $recSrc -OutputAssembly $rec -OutputType WindowsApplication
$argsOut = Join-Path $work "args-out.txt"

# ---- images: a real 16x16 PNG (served), a 100x100 PNG (user image), a 600x600 PNG and a GIF (refused)
function PngBytes([int]$w, [int]$h, $color) { $b = New-Object Drawing.Bitmap $w, $h; $g = [Drawing.Graphics]::FromImage($b); $g.Clear($color); $g.Dispose(); $ms = New-Object IO.MemoryStream; $b.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $b.Dispose(); $ms.ToArray() }
$png16 = PngBytes 16 16 ([Drawing.Color]::OrangeRed)
$userPng = Join-Path $work "user.png"; [IO.File]::WriteAllBytes($userPng, (PngBytes 100 100 ([Drawing.Color]::SeaGreen)))
$bigPng = Join-Path $work "big.png"; [IO.File]::WriteAllBytes($bigPng, (PngBytes 600 600 ([Drawing.Color]::Navy)))
$slowA = Join-Path $work "slow-a.png"; [IO.File]::WriteAllBytes($slowA, (PngBytes 40 40 ([Drawing.Color]::Red)))
$quickB = Join-Path $work "b.png"; [IO.File]::WriteAllBytes($quickB, (PngBytes 40 40 ([Drawing.Color]::Blue)))
function CenterColor($f) { if (-not (Test-Path $f)) { return "none" }; $bm = [Drawing.Bitmap]::FromFile($f); try { $c = $bm.GetPixel([int]($bm.Width / 2), [int]($bm.Height / 2)); if ($c.B -gt 200 -and $c.R -lt 60) { "blue" } elseif ($c.R -gt 200 -and $c.B -lt 60) { "red" } else { "other" } } finally { $bm.Dispose() } }
$gifPath = Join-Path $work "pic.gif"; $gb = New-Object Drawing.Bitmap 8, 8; $gb.Save($gifPath, [Drawing.Imaging.ImageFormat]::Gif); $gb.Dispose()
$pickFile = Join-Path $work "pick.txt"

# ---- local servers: one listener per behaviour, each in its own runspace (a slow one does not block the others)
$log = [Collections.ArrayList]::Synchronized((New-Object Collections.ArrayList))
$servers = @{}
$serverScript = {
  param($listener, $mode, $png, $log, $name)
  while ($true) {
    try { $c = $listener.AcceptTcpClient() } catch { break }
    try {
      $s = $c.GetStream(); $s.ReadTimeout = 5000
      $buf = New-Object byte[] 8192; $n = 0; $req = ""
      while ($req.IndexOf("`r`n`r`n") -lt 0 -and $n -lt 8192) { $k = $s.Read($buf, $n, 8192 - $n); if ($k -le 0) { break }; $n += $k; $req = [Text.Encoding]::ASCII.GetString($buf, 0, $n) }
      $line = ($req -split "`r`n")[0]
      [void]$log.Add("$name|$line|cookie=$($req -match '(?im)^cookie:')|auth=$($req -match '(?im)^authorization:')")
      $path = ($line -split ' ')[1]
      $port = $listener.LocalEndpoint.Port
      function Send($status, $headers, [byte[]]$body) { $h = "HTTP/1.1 $status`r`n" + $headers + "Content-Length: $($body.Length)`r`nConnection: close`r`n`r`n"; $hb = [Text.Encoding]::ASCII.GetBytes($h); $s.Write($hb, 0, $hb.Length); if ($body.Length -gt 0) { $s.Write($body, 0, $body.Length) }; $s.Flush() }
      switch ($mode) {
        "png"        { Send "200 OK" "Content-Type: image/png`r`n" $png }
        "redirsame"  { if ($path -eq "/real.ico") { Send "200 OK" "Content-Type: image/png`r`n" $png } else { Send "302 Found" "Location: /real.ico`r`n" ([byte[]]@()) } }
        "redirother" { Send "302 Found" "Location: http://localhost:$port/favicon.ico`r`n" ([byte[]]@()) }
        "big"        { Send "200 OK" "Content-Type: image/png`r`n" (New-Object byte[] 70000) }
        "slow"       { Start-Sleep -Seconds 8; try { Send "200 OK" "Content-Type: image/png`r`n" $png } catch { } }
        "late"       { Start-Sleep -Milliseconds 4500; try { Send "200 OK" "Content-Type: image/png`r`n" $png } catch { } }
        "optfail"    { Send "200 OK" "Content-Type: image/png`r`n" $png }
        "drip"       { $hb = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 200 OK`r`nContent-Type: image/png`r`nContent-Length: 2000`r`nConnection: close`r`n`r`n"); $s.Write($hb, 0, $hb.Length); $s.Flush()
                       try { for ($q = 0; $q -lt 30; $q++) { Start-Sleep -Milliseconds 400; $s.WriteByte(0x41); $s.Flush() } } catch { } }
        "redirslow"  { Start-Sleep -Milliseconds 1800; if ($path -eq "/r3") { Send "200 OK" "Content-Type: image/png`r`n" $png } else { $nx = if ($path -match '^/r(\d)$') { [int]$Matches[1] + 1 } else { 1 }; try { Send "302 Found" "Location: /r$nx`r`n" ([byte[]]@()) } catch { } } }
        "redir5"     { $nx = if ($path -match '^/q(\d+)$') { [int]$Matches[1] + 1 } else { 1 }; Send "302 Found" "Location: /q$nx`r`n" ([byte[]]@()) }
      }
    } catch { } finally { try { $c.Close() } catch { } }
  }
}
$runspaces = @()
foreach ($mode in "png", "redirsame", "redirother", "big", "slow", "late", "optfail", "drip", "redirslow", "redir5") {
  $l = New-Object Net.Sockets.TcpListener ([Net.IPAddress]::Loopback), 0; $l.Start()
  $servers[$mode] = $l.LocalEndpoint.Port
  $ps = [PowerShell]::Create(); [void]$ps.AddScript($serverScript).AddArgument($l).AddArgument($mode).AddArgument($png16).AddArgument($log).AddArgument($mode)
  $runspaces += @{ PS = $ps; L = $l; H = $ps.BeginInvoke() }
}

# ---- helpers
$ent = [Text.Encoding]::UTF8.GetBytes("1Key/launch-list/v1")
function ReadList($dir = $cfg) { [Text.Encoding]::UTF8.GetString([Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes("$dir\launch.dat"), $ent, 'CurrentUser')) }
function Items($dir = $cfg) {   # index -> hashtable of fields
  $r = @{}; foreach ($ln in ((ReadList $dir) -split "`n")) { if ($ln -match '^l(\d+)\.([^=]+)=(.*)$') { $i = [int]$Matches[1]; if (-not $r.ContainsKey($i)) { $r[$i] = @{} }; $r[$i][$Matches[2]] = $Matches[3] } }; $r }
function IndexOf($name, $dir = $cfg) { $it = Items $dir; foreach ($k in $it.Keys) { if ($it[$k]["name"] -eq $name) { return $k } }; -1 }
function IdOf($name, $dir = $cfg) { $it = Items $dir; foreach ($k in $it.Keys) { if ($it[$k]["name"] -eq $name) { return $it[$k]["id"] } }; "" }
function Start1Key($path) {
  $script:p = Start-Process $path -PassThru; $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $script:m = [WF]::MainWnd([uint32]$script:p.Id, "OneKeyMainWindow$suffix"); if ($script:m -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 100 }
  if ($script:m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 900
}
function Quit1Key() { if ($script:p -and -not $script:p.HasExited) { [void][WF]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i = 0; $i -lt 40 -and -not $script:p.HasExited; $i++) { Start-Sleep -Milliseconds 100 }; if (-not $script:p.HasExited) { Stop-Process -Id $script:p.Id -Force } } }
function Has($id) { [WF]::GetDlgItem($m, $id) -ne [IntPtr]::Zero }
function Click($id, $ms = 600) { [void][WF]::PostMessageW([WF]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds $ms }
function SetText($id, $s) { [void][WF]::SendMessageW([WF]::GetDlgItem($m, $id), 0x000C, [IntPtr]::Zero, [string]$s) }
function Prop($n) { [int][WF]::GetPropW($m, $n) }
function Dialog() { [WF]::MainWnd([uint32]$p.Id, "OneKeyDialog") }
function Answer([int]$id) { $b = Dialog; if ($b -ne [IntPtr]::Zero) { [void][WF]::PostMessageW($b, 0x0111, [IntPtr]$id, [IntPtr]::Zero); Start-Sleep -Milliseconds 500 } }
function TT() { "[toast: " + [WF]::ToastText([uint32]$p.Id) + "]" }
function ExecCalls() { Prop "OneKeyTestLaunchExec" }
function Unlock() { SetText 101 $master; Click 103 900 }
function Fav() { "$(Prop 'OneKeyTestFavOk')|$(Prop 'OneKeyTestFavFail')|$(Prop 'OneKeyTestFavDropped')" }
function WaitFavChange($before, $ms = 9000) { $t = [Environment]::TickCount; while ([Environment]::TickCount - $t -lt $ms) { if ((Fav) -ne $before) { return ([Environment]::TickCount - $t) }; Start-Sleep -Milliseconds 100 }; -1 }
# [+ Add] > Website, fill, [Save]. browser: 0 default, 1 Edge, 2 Chrome
function AddWeb($name, $url, [int]$browser = 0) {
  if (-not (Has 203)) { Click 240 }
  Click 203; Click 4005
  SetText 4011 $name; SetText 4022 $url
  if ($browser -ne 0) { [void][WF]::SendMessageW([WF]::GetDlgItem($m, 4023), 0x00F1, [IntPtr]$browser, [IntPtr]::Zero) }
  Click 4018 900
}
function FavFile($id) { Join-Path $cfg "icons\$($id.ToLower()).fav.png" }
function UserFile($id) { Join-Path $cfg "icons\$($id.ToLower()).user.png" }
function PngSize($f) { if (-not (Test-Path $f)) { return "none" }; $b = [IO.File]::ReadAllBytes($f); if ($b.Length -lt 24) { return "short" }; $w = ($b[16] -shl 24) -bor ($b[17] -shl 16) -bor ($b[18] -shl 8) -bor $b[19]; $h = ($b[20] -shl 24) -bor ($b[21] -shl 16) -bor ($b[22] -shl 8) -bor $b[23]; "${w}x$h" }
function Log($name) { @($log | Where-Object { $_.StartsWith("$name|") }) }
# the strip's [Edit] badge toggles edit mode; its text tells the state (captured while off)
function StripTxt() { [WF]::Title([WF]::GetDlgItem($m, 4142)) }
function OpenTileEdit($index) {   # strip edit mode on, then the tile -> its edit screen
  if (-not (Has 203)) { Click 240 }
  if (-not $script:stripNormal) { $script:stripNormal = StripTxt }
  if ((StripTxt) -eq $script:stripNormal) { Click 4142 }
  [WF]::Press($m, 4100 + $index); Start-Sleep -Milliseconds 900
}
function StripOff() { if ((Has 4142) -and $script:stripNormal -and (StripTxt) -ne $script:stripNormal) { Click 4142 } }
function Rect($id) { $r = New-Object WF+RECT; [void][WF]::GetWindowRect([WF]::GetDlgItem($m, $id), [ref]$r); $r }

try {
  Stop-TestInstances $suffix
  New-Item -ItemType Directory -Force $cfg | Out-Null
  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_LOCKWIDGET = "0"; $env:ONEKEY_TEST_NO_AUTOLOCK = "1"
  $env:ONEKEY_TEST_BROWSER_EDGE = $rec
  $env:ONEKEY_TEST_BROWSER_CHROME = (Join-Path $work "NoChrome.exe")
  $env:ONEKEY_TEST_PICK = $pickFile
  Start1Key $exe
  SetText 101 $master; SetText 102 $master; Click 103 1500
  if (-not (Has 203)) { throw "setup: no list after creating the master" }

  # WF01 favicon from the same origin, port kept, nothing but /favicon.ico sent
  $f0 = Fav
  AddWeb "SiteA" "http://127.0.0.1:$($servers.png)/some/page?x=1#frag"
  $waited = WaitFavChange $f0
  $ia = IndexOf "SiteA"; $it = (Items)[$ia]; $idA = $it["id"]
  $la = @(Log "png")   # @(): one line must stay an array
  Check WF01 "url|http://127.0.0.1:$($servers.png)/some/page?x=1#frag|default|1|GET /favicon.ico HTTP/1.1|cookie=False|auth=False|64x64" "$($it['kind'])|$($it['target'])|$($it['browser'])|$($la.Count)|$(($la[0] -split '\|')[1])|$(($la[0] -split '\|')[2])|$(($la[0] -split '\|')[3])|$(PngSize (FavFile $idA))" "website saved as typed; favicon fetched once from the same port, only /favicon.ico (no path/query, no cookie/auth), stored as a 64x64 PNG (waited $waited ms)"

  # WF02 same-origin redirect
  $f0 = Fav; AddWeb "SiteB" "http://127.0.0.1:$($servers.redirsame)/"; [void](WaitFavChange $f0)
  $lb = (Log "redirsame" | ForEach-Object { ($_ -split '\|')[1] }) -join ","
  Check WF02 "GET /favicon.ico HTTP/1.1,GET /real.ico HTTP/1.1|64x64" "$lb|$(PngSize (FavFile (IdOf 'SiteB')))" "a same-origin redirect is followed"

  # WF03 redirect to another host: not followed
  $f0 = Fav; AddWeb "SiteC" "http://127.0.0.1:$($servers.redirother)/"; [void](WaitFavChange $f0)
  Check WF03 "1|none|url" "$((Log 'redirother').Count)|$(PngSize (FavFile (IdOf 'SiteC')))|$((Items)[(IndexOf 'SiteC')]['kind'])" "a redirect to another host is not followed (one request), no icon, the item is saved"

  # WF04 too big
  $f0 = Fav; AddWeb "SiteD" "http://127.0.0.1:$($servers.big)/"; [void](WaitFavChange $f0)
  Check WF04 "none|True" "$(PngSize (FavFile (IdOf 'SiteD')))|$((IndexOf 'SiteD') -ge 0)" "a body over 64 KiB is refused, the item is saved"

  # WF05 slow server: given up within the limit, window responsive meanwhile
  $f0 = Fav; AddWeb "SiteE" "http://127.0.0.1:$($servers.slow)/"
  $answers = [WF]::Answers($m, 1000)
  $tE = WaitFavChange $f0 12000
  Check WF05 "True|True|none" "$answers|$($tE -ge 0 -and $tE -le 7500)|$(PngSize (FavFile (IdOf 'SiteE')))" "a server answering after 8 s is given up within about 5 s (took $tE ms after save), no icon; the window answered meanwhile"

  # WF06 delete while the favicon is coming: dropped
  $before = [int](Prop 'OneKeyTestFavDropped')
  # strip edit mode on first, so the tile opens its edit screen right after saving (the server answers after 4.5 s, inside the 5 s limit)
  if (-not $script:stripNormal) { $script:stripNormal = StripTxt }
  if ((StripTxt) -eq $script:stripNormal) { Click 4142 }
  AddWeb "SiteF" "http://127.0.0.1:$($servers.late)/"
  $idF = IdOf "SiteF"; $iF = IndexOf "SiteF"
  [WF]::Press($m, 4100 + $iF); Start-Sleep -Milliseconds 900
  Click 4016; Answer 6
  Start-Sleep -Seconds 5
  StripOff
  Check WF06 "1|-1|none|none" "$([int](Prop 'OneKeyTestFavDropped') - $before)|$(IndexOf 'SiteF')|$(PngSize (FavFile $idF))|$(PngSize (UserFile $idF))" "deleted while its favicon was coming: the late result is dropped, no file written"

  # WF07 bad addresses
  $bad = @()
  if (-not (Has 203)) { Click 240 }
  Click 203; Click 4005; SetText 4011 "Bad"
  foreach ($u in "ftp://a.example/", "https://u:p@a.example/", "https://a.example/x y", "https://a.example/`"q") {
    SetText 4022 $u; Click 4018 900
    $b = Dialog; $bad += "$($b -ne [IntPtr]::Zero)/$(Has 4022)"; Answer 1
  }
  Click 4017
  Check WF07 "True/True,True/True,True/True,True/True|-1" "$($bad -join ',')|$(IndexOf 'Bad')" "other scheme, user:pass@, space, quote: refused with a box, the edit screen stays, nothing saved"

  # WF08 no scheme typed
  AddWeb "SiteG" "example.invalid/path"
  Check WF08 "https://example.invalid/path" "$((Items)[(IndexOf 'SiteG')]['target'])" "an address without a scheme is saved with https:// in front"

  # WF09 third row: a folder (drop test hook) above the website tiles
  $folder = Join-Path $work "WfFolder"; New-Item -ItemType Directory -Force $folder | Out-Null
  [IO.File]::WriteAllLines("$cfg\drop-test.txt", @("$folder|WfFolder")); [void][WF]::PostMessageW($m, 0x8000 + 44, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 1200
  Click 4018 900
  $iFo = IndexOf "WfFolder"
  $fr = Rect (4100 + $iFo); $ra = Rect (4100 + (IndexOf 'SiteA')); $rb = Rect (4100 + (IndexOf 'SiteB'))
  Check WF09 "True|True|True" "$($iFo -ge 0)|$($ra.T -gt $fr.B - 2)|$($ra.T -eq $rb.T)" "website tiles are on their own row below the folder row, all on one row"

  # WF10 Edge (recorder): one argument equal to the address
  $url10 = "https://a.example/" + [char]0xD55C + [char]0xAE00 + "?q=1&r=%20x#frag"
  AddWeb "SiteEdge" $url10 1
  Remove-Item $argsOut -ErrorAction Ignore
  $e0 = ExecCalls
  [WF]::Press($m, 4100 + (IndexOf 'SiteEdge'))
  for ($i = 0; $i -lt 50 -and -not (Test-Path $argsOut); $i++) { Start-Sleep -Milliseconds 100 }
  Start-Sleep -Milliseconds 300
  $got = if (Test-Path $argsOut) { [IO.File]::ReadAllLines($argsOut, [Text.Encoding]::UTF8) } else { @() }
  Check WF10 "edge|1|argc=1|True" "$((Items)[(IndexOf 'SiteEdge')]['browser'])|$((ExecCalls) - $e0)|$($got[0])|$($got.Count -ge 2 -and $got[1] -eq $url10)" "browser Edge: started once with exactly one argument equal to the address $(TT)"

  # WF11 Chrome missing
  AddWeb "SiteChrome" "https://b.example/" 2
  Remove-Item $argsOut -ErrorAction Ignore
  $e0 = ExecCalls
  [WF]::Press($m, 4100 + (IndexOf 'SiteChrome')); Start-Sleep -Milliseconds 1500
  $toast = [WF]::ToastText([uint32]$p.Id)
  Check WF11 "chrome|0|False|True" "$((Items)[(IndexOf 'SiteChrome')]['browser'])|$((ExecCalls) - $e0)|$(Test-Path $argsOut)|$($toast.Length -gt 0)" "browser Chrome not found: nothing starts, a warning toast ($toast)"

  # WF12 user image, [Default], refused images
  [IO.File]::WriteAllText($pickFile, $userPng)
  OpenTileEdit (IndexOf 'SiteA'); Click 4019 900; Click 4018 900
  $u1 = PngSize (UserFile $idA)
  OpenTileEdit (IndexOf 'SiteA'); Click 4020 900; Click 4018 900
  $u2 = PngSize (UserFile $idA); $fv = PngSize (FavFile $idA)
  $refused = @()
  foreach ($img in $bigPng, $gifPath) {
    [IO.File]::WriteAllText($pickFile, $img)
    OpenTileEdit (IndexOf 'SiteA'); Click 4019 900
    $refused += "$((Dialog) -ne [IntPtr]::Zero)"; Answer 1
    Click 4017 600
  }
  StripOff
  Check WF12 "64x64|none|64x64|True,True|none" "$u1|$u2|$fv|$($refused -join ',')|$(PngSize (UserFile $idA))" "[Change] PNG -> user.png 64x64; [Default] removes it, the favicon stays; 600x600 PNG and GIF refused with a box"

  # WF21 slow A then quick B: B is kept
  $pd0 = [int](Prop 'OneKeyTestPickDropped')
  OpenTileEdit (IndexOf 'SiteA')
  [IO.File]::WriteAllText($pickFile, $slowA); Click 4019 300
  [IO.File]::WriteAllText($pickFile, $quickB); Click 4019 300
  Start-Sleep -Milliseconds 3500
  $onEdit = Has 4022
  Click 4018 900
  StripOff
  Check WF21 "True|blue|1" "$onEdit|$(CenterColor (UserFile $idA))|$([int](Prop 'OneKeyTestPickDropped') - $pd0)" "slow image A then quick image B: A's late result is dropped, B is saved"

  # WF17 a body that drips: the total limit ends it
  $f0 = Fav; AddWeb "SiteDrip" "http://127.0.0.1:$($servers.drip)/"
  $tD = WaitFavChange $f0 14000
  Check WF17 "True|none" "$($tD -ge 0 -and $tD -le 7500)|$(PngSize (FavFile (IdOf 'SiteDrip')))" "a body sent one byte every 0.4 s is given up within about 5 s (took $tD ms), no icon"

  # WF18 slow redirect chain (total limit) and too many redirects (3 follows)
  $f0 = Fav; AddWeb "SiteRS" "http://127.0.0.1:$($servers.redirslow)/"
  $tR = WaitFavChange $f0 14000
  $f0 = Fav; AddWeb "SiteR5" "http://127.0.0.1:$($servers.redir5)/"; [void](WaitFavChange $f0)
  Check WF18 "True|none|4|none" "$($tR -ge 0 -and $tR -le 7500)|$(PngSize (FavFile (IdOf 'SiteRS')))|$((Log 'redir5').Count)|$(PngSize (FavFile (IdOf 'SiteR5')))" "slow same-origin redirects stop at the total limit ($tR ms); 5 quick redirects: 4 requests (3 follows), no icon"

  # WF19 locked: the website hotkey still opens it
  [void][WF]::PostMessageW($m, 0x02B1, [IntPtr]7, [IntPtr]::Zero); Start-Sleep -Milliseconds 1200   # synthetic session lock notice
  $locked = Has 101
  Remove-Item $argsOut -ErrorAction Ignore
  [void][WF]::PostMessageW($m, 0x0312, [IntPtr](200 + (IndexOf 'SiteEdge')), [IntPtr]::Zero)
  for ($i = 0; $i -lt 50 -and -not (Test-Path $argsOut); $i++) { Start-Sleep -Milliseconds 100 }
  Start-Sleep -Milliseconds 300
  $got19 = if (Test-Path $argsOut) { [IO.File]::ReadAllLines($argsOut, [Text.Encoding]::UTF8) } else { @() }
  $still = Has 101
  Unlock
  Check WF19 "True|argc=1|True|True" "$locked|$($got19[0])|$($got19.Count -ge 2 -and $got19[1] -eq $url10)|$still" "locked: the website hotkey opens it in the chosen browser, 1Key stays locked"

  # WF15 the image picker returns after a lock: dropped
  StripOff
  $pd0 = [int](Prop 'OneKeyTestPickDropped')
  $uh0 = if (Test-Path (UserFile $idA)) { (Get-FileHash (UserFile $idA)).Hash } else { "none" }   # WF21 may have left B there: compare, do not expect none
  [IO.File]::WriteAllText($pickFile, "delay=2500|$userPng")
  OpenTileEdit (IndexOf 'SiteA')
  [void][WF]::PostMessageW([WF]::GetDlgItem($m, 4019), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 700
  [void][WF]::PostMessageW($m, 0x02B1, [IntPtr]7, [IntPtr]::Zero); Start-Sleep -Milliseconds 3500
  $uh1 = if (Test-Path (UserFile $idA)) { (Get-FileHash (UserFile $idA)).Hash } else { "none" }
  Check WF15 "True|False|$uh0|1" "$(Has 101)|$(Has 4022)|$uh1|$([int](Prop 'OneKeyTestPickDropped') - $pd0)" "picker returned after a lock: result dropped, edit screen not reopened, the user image file unchanged"
  Unlock
  StripOff

  # WF20 (opt-in) default browser opens a local page
  if ($DefaultBrowser) {
    AddWeb "SiteDefault" "http://127.0.0.1:$($servers.png)/opened-by-default"
    [WF]::Press($m, 4100 + (IndexOf 'SiteDefault'))
    $seen = $false; for ($i = 0; $i -lt 150 -and -not $seen; $i++) { $seen = @(Log "png" | Where-Object { $_ -like "*GET /opened-by-default *" }).Count -gt 0; if (-not $seen) { Start-Sleep -Milliseconds 100 } }
    Check WF20 "True" "$seen" "browser Default: the default browser opened the local test page"
  }

  # WF16 a required WinHTTP setting fails: no request at all
  Quit1Key
  $env:ONEKEY_TEST_FAIL = "webicon:option"
  Start1Key $exe; Unlock
  $f0 = Fav; AddWeb "SiteOpt" "http://127.0.0.1:$($servers.optfail)/"; [void](WaitFavChange $f0)
  Check WF16 "0|1|True" "$((Log 'optfail').Count)|$([int](Prop 'OneKeyTestFavFail'))|$((IndexOf 'SiteOpt') -ge 0)" "a required WinHTTP setting failing: no request reaches the server, the item is saved"
  Quit1Key
  $env:ONEKEY_TEST_FAIL = $null

  # WF13 / WF14 previous version on a copy of the new files
  if (-not (Test-Path $old)) { Add-Failure "previous version not found: $old" }
  else {
    Copy-Item $cfg $cfgOld -Recurse
    $env:ONEKEY_CONFIG_DIR = $cfgOld
    $webBefore = @{}; $itB = Items $cfgOld; foreach ($k in $itB.Keys) { if ($itB[$k]['kind'] -eq 'url') { $webBefore[$itB[$k]['id']] = (($itB[$k].GetEnumerator() | Sort-Object Name | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join ';') } }
    $script:stripNormal = $null
    Start1Key $old
    Unlock
    $h0 = (Get-FileHash "$cfgOld\launch.dat").Hash
    # WF14 first: edit a website tile and save -> refused, file unchanged
    OpenTileEdit (IndexOf 'SiteA' $cfgOld)
    $onEdit = Has 4018
    Click 4018 900
    $box = (Dialog) -ne [IntPtr]::Zero; Answer 1
    Click 4017 600
    StripOff
    Check WF14 "True|True|$h0" "$onEdit|$box|$((Get-FileHash "$cfgOld\launch.dat").Hash)" "0.3.76: a website item opened in edit mode cannot be saved (box), the file is unchanged"
    # WF13: pressing a website tile runs nothing; renaming the folder keeps the website fields
    $e0 = ExecCalls
    [WF]::Press($m, 4100 + (IndexOf 'SiteEdge' $cfgOld)); Start-Sleep -Milliseconds 1200
    $ran = (ExecCalls) - $e0
    OpenTileEdit (IndexOf 'WfFolder' $cfgOld); SetText 4011 "WfFolder2"; Click 4018 900
    StripOff
    Quit1Key
    $itA = Items $cfgOld; $same = 0; $renamed = $false
    foreach ($k in $itA.Keys) {
      if ($itA[$k]['name'] -eq 'WfFolder2') { $renamed = $true }
      if ($itA[$k]['kind'] -eq 'url' -and $webBefore.ContainsKey($itA[$k]['id']) -and $webBefore[$itA[$k]['id']] -eq (($itA[$k].GetEnumerator() | Sort-Object Name | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join ';')) { $same++ }
    }
    Check WF13 "0|True|$($webBefore.Count)" "$ran|$renamed|$same" "0.3.76: a website tile runs nothing; after renaming the folder and saving, all $($webBefore.Count) website items keep every field"
  }
} catch { Add-Failure ("exception: " + $_) }
finally {
  Quit1Key; Stop-TestInstances $suffix
  foreach ($r in $runspaces) { try { $r.L.Stop() } catch { }; try { $r.PS.Stop(); $r.PS.Dispose() } catch { } }
  $env:ONEKEY_TEST_BROWSER_EDGE = $null; $env:ONEKEY_TEST_BROWSER_CHROME = $null; $env:ONEKEY_TEST_PICK = $null; $env:ONEKEY_CONFIG_DIR = $null; $env:ONEKEY_TEST_FAIL = $null
  "kept run folder: $root"
}
Complete-Checks
