param([string]$Exe = "$PSScriptRoot\..\..\..\src\OneKey\bin\Release\net8.0-windows\win-x64\1Key.exe")
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public class T5 { [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr p, uint f);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra); }
'@
function Res($p) { $p.Refresh(); "GDI {0} USER {1} handles {2} private {3:N1}MB" -f [T5]::GetGuiResources($p.Handle,0), [T5]::GetGuiResources($p.Handle,1), $p.HandleCount, ($p.PrivateMemorySize64/1MB) }
function Cpu($p, $sec) { $p.Refresh(); $t0 = $p.TotalProcessorTime.TotalMilliseconds; Start-Sleep -Seconds $sec; $p.Refresh(); "{0:N2}%" -f (($p.TotalProcessorTime.TotalMilliseconds - $t0) / ($sec*1000) * 100) }
$env:ONEKEY_TEST = "1"
$p = Start-Process $Exe -ArgumentList "--lockwidget-demo" -PassThru; Start-Sleep -Seconds 2
"expanded start : $(Res $p)"
"expanded CPU   : $(Cpu $p 5)"
Start-Sleep -Seconds 25
"expanded +30s  : $(Res $p)"
$form = New-Object Windows.Forms.Form; $form.StartPosition = "Manual"; $form.Left = 20; $form.Top = 20; $form.Width = 120; $form.Height = 60; $form.FormBorderStyle = "None"; $form.Show(); [Windows.Forms.Application]::DoEvents()
[T5]::keybd_event(0x12,0,0,[UIntPtr]::Zero); [T5]::keybd_event(0x12,0,2,[UIntPtr]::Zero); [void][T5]::SetForegroundWindow($form.Handle); Start-Sleep -Seconds 2; [Windows.Forms.Application]::DoEvents()
"narrow CPU     : $(Cpu $p 10)"
"narrow         : $(Res $p)"
$form.Close(); Stop-Process -Id $p.Id -Force
$q = Start-Process $Exe -ArgumentList "--lockwidget-demo","cycle=200" -PassThru; Start-Sleep -Seconds 6
"after 200 show/hide: $(Res $q)"
Stop-Process -Id $q.Id -Force
