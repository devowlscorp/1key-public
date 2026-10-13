# Real mouse safety (lib\RealMouse.ps1, Codex D24-3) without touching the mouse: a recorder takes the place of the real mouse.
# RM01 a button already down at the start: the test does not start and presses nothing
# RM02 an error after the press (while moving): the button is released (LEFTUP after LEFTDOWN)
# RM03 the cursor moved by someone else during the drag: stops and releases
# RM04 the check after the press fails (capture lost / test process gone): stops and releases, no move after it
# RM05 the window under the cursor is not the test window: nothing is pressed
# RM06 a normal drag: down, moves, up - in that order, exactly one down and one up
# RM07 Stop-RealMouse puts the cursor back only if it is still where the test left it
# No windows, does not start 1Key. ASCII only.
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "lib\Check.ps1")
$ids = @(); 1..7 | ForEach-Object { $ids += "RM{0:D2}" -f $_ }
Start-Checks -Required $ids
$script:Ev = New-Object System.Collections.Generic.List[string]
$script:Cur = @(100, 100); $script:Btn = $false; $script:Bump = $null
$script:RmSink = {
  param($what, $a, $b)
  switch ($what) {
    "cursor" { if ($script:Bump) { $c = $script:Bump; $script:Bump = $null; $script:Cur = $c }; return $script:Cur }
    "buttons" { return $script:Btn }
    "set" { $script:Cur = @($a, $b); $script:Ev.Add("set $a,$b") }
    "event" { if ($a -eq 2) { $script:Ev.Add("down") } elseif ($a -eq 4) { $script:Ev.Add("up") } }
  }
}
. (Join-Path $PSScriptRoot "lib\RealMouse.ps1")
function Reset() { $script:Ev.Clear(); $script:Cur = @(100, 100); $script:Btn = $false; $script:Bump = $null }
function Downs() { @($script:Ev | Where-Object { $_ -eq "down" }).Count }
function Ups() { @($script:Ev | Where-Object { $_ -eq "up" }).Count }
$yes = { $true }; $no = { $false }
try {
  Reset; $script:Btn = $true; $err = ""
  try { Start-RealMouse } catch { $err = "$_"; Clear-ExpectedError }
  Check RM01 "True|0" "$($err -like '*already down*')|$(Downs)" "button down at the start: not started, nothing pressed"

  Reset; Start-RealMouse; $n = 0; $err = ""
  $boom = { $script:n2++; if ($script:n2 -ge 3) { throw "boom" }; $true }; $script:n2 = 0
  try { Invoke-RmDrag 100 100 300 300 8 $yes $boom } catch { $err = "$_"; Clear-ExpectedError }
  Check RM02 "True|1|1|up" "$($err -like '*boom*')|$(Downs)|$(Ups)|$($script:Ev[$script:Ev.Count - 1])" "error while moving: released (last event is up)"

  Reset; Start-RealMouse; $err = ""
  $moveAway = { if ($script:Ev.Count -ge 4) { $script:Bump = @(900, 900) }; $true }
  try { Invoke-RmDrag 100 100 300 300 8 $yes $moveAway } catch { $err = "$_"; Clear-ExpectedError }
  Check RM03 "True|1|1" "$($err -like '*cursor moved*')|$(Downs)|$(Ups)" "cursor moved by someone else: stopped and released"

  Reset; Start-RealMouse; $err = ""
  try { Invoke-RmDrag 100 100 300 300 8 $yes $no } catch { $err = "$_"; Clear-ExpectedError }
  $after = @($script:Ev | Select-Object -Skip ([array]::IndexOf($script:Ev.ToArray(), "down") + 1) | Where-Object { $_ -like "set *" }).Count
  Check RM04 "True|1|1|0" "$($err -like '*after the press*')|$(Downs)|$(Ups)|$after" "check fails after the press: released, no move after the press"

  Reset; Start-RealMouse; $err = ""
  try { Invoke-RmDrag 100 100 300 300 8 $no $yes } catch { $err = "$_"; Clear-ExpectedError }
  try { Invoke-RmClick 100 100 $no } catch { Clear-ExpectedError }
  Check RM05 "True|0|0" "$($err -like '*not the test window*')|$(Downs)|$(Ups)" "not the test window under the cursor: nothing pressed"

  Reset; Start-RealMouse; $err = ""
  try { Invoke-RmDrag 100 100 300 300 4 $yes $yes } catch { $err = "$_"; Clear-ExpectedError }
  $order = ($script:Ev | Where-Object { $_ -in "down", "up" }) -join ","
  Check RM06 "|down,up|300,300" "$err|$order|$($script:Cur -join ',')" "normal drag: one down, moves, one up, ends at the target"

  Reset; $script:Cur = @(50, 60); Start-RealMouse; Set-RmCursor 200 200; Stop-RealMouse; $back = $script:Cur -join ','
  Reset; $script:Cur = @(50, 60); Start-RealMouse; Set-RmCursor 200 200; $script:Bump = @(700, 700); Stop-RealMouse; $stay = $script:Cur -join ','
  Check RM07 "50,60|700,700" "$back|$stay" "cursor put back only if the user did not move it"
} catch { Add-Failure ("exception: " + $_) }
Complete-Checks
