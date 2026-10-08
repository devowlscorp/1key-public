param([string]$Exe = "$PSScriptRoot\..\..\..\src\OneKey\bin\Release\net8.0-windows\win-x64\1Key.exe")
Add-Type @'
using System; using System.Runtime.InteropServices;
public class T6 { [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr p, uint f); }
'@
$env:ONEKEY_TEST = "1"
foreach ($n in 0, 200, 1000, 3000) {
  $q = Start-Process $Exe -ArgumentList "--lockwidget-demo","cycle=$n" -PassThru
  $t = [Diagnostics.Stopwatch]::StartNew()
  Start-Sleep -Seconds 1
  do { $q.Refresh(); $c0 = $q.TotalProcessorTime.TotalMilliseconds; Start-Sleep -Seconds 1; $q.Refresh(); $d = $q.TotalProcessorTime.TotalMilliseconds - $c0 } while ($d -gt 150 -and $t.Elapsed.TotalSeconds -lt 300)
  Start-Sleep -Seconds 1; $q.Refresh()
  "cycle={0,-5} {1:N1}s  GDI {2} USER {3} handles {4} private {5:N1}MB" -f $n, $t.Elapsed.TotalSeconds, [T6]::GetGuiResources($q.Handle,0), [T6]::GetGuiResources($q.Handle,1), $q.HandleCount, ($q.PrivateMemorySize64/1MB)
  Stop-Process -Id $q.Id -Force
}
