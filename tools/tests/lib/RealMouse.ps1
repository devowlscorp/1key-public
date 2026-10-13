# Real mouse input for window tests (catdrag) - Codex D24-3, 2026-10-13. ASCII only.
# Rules: never start while a mouse button is already down; every LEFTDOWN this test sent is released (Rm-Up in a finally, and
# Rm-ReleaseAll in the script's own finally); stop as soon as the real cursor is not where this test put it (the user moved the
# mouse); a caller check (capture still on the test window, test process alive) runs after the press and between moves - false
# stops the gesture and releases the button; the cursor is put back only if it is still where this test left it.
# $script:RmSink: a scriptblock ($what, $a, $b) used instead of the real mouse - tools\tests\realmouse.ps1 checks these rules without
# touching the mouse. what = "cursor" (returns @(x, y)), "buttons" (returns $true if a button is down), "set" (x, y), "event" (flags).
if (-not ("RMU" -as [type])) {
Add-Type @'
using System;using System.Runtime.InteropServices;
public class RMU {
  [StructLayout(LayoutKind.Sequential)] public struct PT { public int X, Y; }
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out PT p);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, UIntPtr e);
}
'@
}
$script:RmDown = $false
$script:RmLast = $null
$script:RmStart = $null
if (-not (Test-Path variable:script:RmSink)) { $script:RmSink = $null }

function Get-RmCursor() {
  if ($script:RmSink) { return @(& $script:RmSink "cursor" 0 0) }
  $p = New-Object RMU+PT; [void][RMU]::GetCursorPos([ref]$p); @($p.X, $p.Y)
}
function Test-RmButtonsDown() {
  if ($script:RmSink) { return [bool](& $script:RmSink "buttons" 0 0) }
  (([RMU]::GetAsyncKeyState(0x01) -band 0x8000) -ne 0) -or (([RMU]::GetAsyncKeyState(0x02) -band 0x8000) -ne 0) -or (([RMU]::GetAsyncKeyState(0x04) -band 0x8000) -ne 0)
}
function Send-RmEvent([uint32]$flags) {
  if ($script:RmSink) { [void](& $script:RmSink "event" $flags 0); return }
  [RMU]::mouse_event($flags, 0, 0, 0, [UIntPtr]::Zero)
}
# first call: remember where the user's cursor was
function Start-RealMouse() {
  if (Test-RmButtonsDown) { throw "a mouse button is already down - real mouse test not started" }
  $script:RmStart = Get-RmCursor; $script:RmLast = $null; $script:RmDown = $false
}
# move the cursor; if it is not where this test last put it, the user moved the mouse: stop
function Set-RmCursor([int]$x, [int]$y) {
  if ($script:RmLast) {
    $c = Get-RmCursor
    if ([Math]::Abs($c[0] - $script:RmLast[0]) -gt 1 -or [Math]::Abs($c[1] - $script:RmLast[1]) -gt 1) { throw "the cursor moved (user input?) - stopped" }
  }
  if ($script:RmSink) { [void](& $script:RmSink "set" $x $y) } else { [void][RMU]::SetCursorPos($x, $y) }
  $script:RmLast = @($x, $y)
  Send-RmEvent 0x0001
}
function Send-RmDown() {
  if (Test-RmButtonsDown) { throw "a mouse button is already down - not pressing" }
  Send-RmEvent 0x0002; $script:RmDown = $true
}
function Send-RmUp() { if ($script:RmDown) { $script:RmDown = $false; Send-RmEvent 0x0004 } }
# for the script's finally: release whatever this test pressed, put the cursor back only if the user did not move it meanwhile
function Stop-RealMouse() {
  Send-RmUp
  if ($script:RmStart -and $script:RmLast) {
    $c = Get-RmCursor
    if ([Math]::Abs($c[0] - $script:RmLast[0]) -le 1 -and [Math]::Abs($c[1] - $script:RmLast[1]) -le 1) {
      if ($script:RmSink) { [void](& $script:RmSink "set" $script:RmStart[0] $script:RmStart[1]) } else { [void][RMU]::SetCursorPos($script:RmStart[0], $script:RmStart[1]) }
    }
  }
}
# press at (x, y), move to (tx, ty) in steps, let go. $ok: a scriptblock that must return $true after the press and before every move
# (capture on the test window, test process alive); $onTarget: must return $true at (x, y) before pressing (window under the cursor).
function Invoke-RmDrag([int]$x, [int]$y, [int]$tx, [int]$ty, [int]$steps, [scriptblock]$onTarget, [scriptblock]$ok) {
  Set-RmCursor $x $y; Start-Sleep -Milliseconds 120
  if (-not (& $onTarget)) { throw "the window under the cursor is not the test window - stopped before pressing" }
  try {
    Send-RmDown; Start-Sleep -Milliseconds 80
    if (-not (& $ok)) { throw "after the press: capture not on the test window or the test process is gone - stopped" }
    for ($i = 1; $i -le $steps; $i++) {
      if (-not (& $ok)) { throw "while moving: capture not on the test window or the test process is gone - stopped" }
      Set-RmCursor ([int]($x + ($tx - $x) * $i / $steps)) ([int]($y + ($ty - $y) * $i / $steps)); Start-Sleep -Milliseconds 30
    }
    Start-Sleep -Milliseconds 150
  } finally { Send-RmUp }
}
function Invoke-RmClick([int]$x, [int]$y, [scriptblock]$onTarget) {
  Set-RmCursor $x $y; Start-Sleep -Milliseconds 150
  if (-not (& $onTarget)) { throw "the window under the cursor is not the test window - stopped before clicking" }
  try { Send-RmDown; Start-Sleep -Milliseconds 60 } finally { Send-RmUp }
}
