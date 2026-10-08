param([string]$Exe = "")
# MSAA of the lock widget (what UIA's Win32 proxy and Narrator read). Read-only; dummy windows only; values are never read.
# MA01 unlock widget: one field id 101, role text (42), protected (password), focusable, MSAA bounds = the real EDIT window
# MA02 first-setup widget: two fields 101/102, both text + protected, MSAA bounds = their EDIT windows
# MA03 field names equal the old in-window first-setup screen's names (same labels for screen readers)
# MA04 widget client object: 3 push-button children (role 43) with non-empty names and a default action; no extra edit exposed
# Codex R-W5: compares with expected values and exits 1 on any failure (was a print-only diagnostic). ASCII only.
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "..\lib\Check.ps1")
Start-Checks -Required @("MA01", "MA02", "MA03", "MA04")
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
Add-Type -ReferencedAssemblies Accessibility @'
using System; using System.Text; using System.Runtime.InteropServices; using System.Collections.Generic; using Accessibility;
public class A1 { public delegate bool CB(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] static extern bool EnumWindows(CB cb, IntPtr l);
 [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, CB cb, IntPtr l);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
 [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr h);
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("oleacc.dll")] static extern int AccessibleObjectFromWindow(IntPtr h, uint id, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object o);
 static string Cls(IntPtr h) { var sb = new StringBuilder(64); GetClassNameW(h, sb, 64); return sb.ToString(); }
 public static List<IntPtr> Edits(uint pid){ var r=new List<IntPtr>(); EnumWindows((h,l)=>{uint p;GetWindowThreadProcessId(h,out p); if(p==pid) EnumChildWindows(h,(c,l2)=>{ if(Cls(c)=="Edit") r.Add(c); return true;},IntPtr.Zero); return true;},IntPtr.Zero); r.Sort((a,b)=>GetDlgCtrlID(a).CompareTo(GetDlgCtrlID(b))); return r; }
 public static IntPtr Top(uint pid, string cls){ IntPtr f=IntPtr.Zero; EnumWindows((h,l)=>{uint p;GetWindowThreadProcessId(h,out p); if(p==pid && Cls(h)==cls){f=h; return false;} return true;},IntPtr.Zero); return f; }
 static IAccessible Acc(IntPtr h){ Guid g=new Guid("618736E0-3C3D-11CF-810C-00AA00389B71"); object o; return AccessibleObjectFromWindow(h,0xFFFFFFFC,ref g,out o)==0 ? (IAccessible)o : null; }
 // "id|role|protected|focusable|boundsMatch" for one EDIT
 public static string Field(IntPtr h){ var a=Acc(h); if(a==null) return "no-msaa"; int role=Convert.ToInt32(a.get_accRole(0)); int st=Convert.ToInt32(a.get_accState(0)); int x,y,w,hh; a.accLocation(out x,out y,out w,out hh,0); RECT r; GetWindowRect(h,out r);
  bool same = x==r.L && y==r.T && w==r.R-r.L && hh==r.B-r.T; return GetDlgCtrlID(h)+"|"+role+"|"+((st&0x20000000)!=0)+"|"+((st&0x100000)!=0)+"|"+same; }
 public static string Name(IntPtr h){ var a=Acc(h); return a==null ? null : a.get_accName(0); }
 // "count|roles|namesNonEmpty|actionsNonEmpty" for the widget client object
 public static string Buttons(IntPtr h){ var a=Acc(h); if(a==null) return "no-msaa"; int n=a.accChildCount; string roles=""; bool names=true, acts=true;
  for(int c=1;c<=n;c++){ roles+=(c>1?"/":"")+Convert.ToString(a.get_accRole(c)); if(string.IsNullOrEmpty(a.get_accName(c))) names=false; if(string.IsNullOrEmpty(a.get_accDefaultAction(c))) acts=false; }
  return n+"|"+roles+"|"+names+"|"+acts; } }
'@
$env:ONEKEY_TEST = "1"
$names = @{}
try {
  foreach ($mode in "", "create") {
    $a = @("--lockwidget-demo"); if ($mode) { $a += $mode }
    $p = Start-Process $exe -ArgumentList $a -PassThru; Start-Sleep -Milliseconds 1800
    $e = @([A1]::Edits([uint32]$p.Id))
    if ($mode -eq "") {
      Check MA01 "1|101|42|True|True|True" ("$($e.Count)|" + $(if ($e.Count -gt 0) { [A1]::Field($e[0]) } else { "" })) "unlock widget field: text, protected, focusable, bounds = EDIT"
      $w = [A1]::Top([uint32]$p.Id, "OneKeyLockWidget")
      Check MA04 "3|43/43/43|True|True|1" ("$([A1]::Buttons($w))|$($e.Count)") "widget client: 3 named push buttons with a default action, no extra edit"
    } else {
      $f = @($e | ForEach-Object { [A1]::Field($_) }) -join ";"
      Check MA02 "101|42|True|True|True;102|42|True|True|True" $f "first-setup widget fields: text, protected, focusable, bounds = EDIT"
      $names.Widget = @($e | ForEach-Object { [A1]::Name($_) }) -join "/"
    }
    $p | Stop-Process -Force; Start-Sleep -Milliseconds 300
  }
  $cfg = "$env:TEMP\onekey_lockwidget_cfg_old"
  if (Test-Path $cfg) { Remove-Item -Recurse -Force $cfg }; New-Item -ItemType Directory $cfg | Out-Null
  $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = ".msaaold"; $env:ONEKEY_TEST_LOCKWIDGET = "0"
  $p = Start-Process $exe -PassThru; Start-Sleep -Milliseconds 2500
  $names.Old = @([A1]::Edits([uint32]$p.Id) | ForEach-Object { [A1]::Name($_) }) -join "/"
  $p | Stop-Process -Force
  Check MA03 "True" "$($names.Widget -eq $names.Old -and $names.Old.Length -gt 3)" "widget field names = old first-setup screen names"
}
catch { Add-Failure ("aborted: " + $_.Exception.Message) }
finally { Get-Process -Id $p.Id -ErrorAction Ignore | Stop-Process -Force -ErrorAction Ignore }
Complete-Checks
