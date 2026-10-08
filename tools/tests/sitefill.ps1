# Site fill end to end (0.2.55) with a real browser. NOT part of the default regression: run it only with -Foreground on a
# PC nobody is using. It opens Microsoft Edge with a throw-away profile (its own --user-data-dir under the test folder; the
# user's browser profile is not touched) on a local test page served by this script (http://localhost:18765), brings it to
# the front and lets 1Key really type dummy strings into it.
#
#   powershell -ExecutionPolicy Bypass -File tools\tests\sitefill.ps1 -Foreground [exe]
#
# The test page shows what was typed in its title ("u=<user>|q=<search>|p=<password>"), so the check reads the Edge window
# title; nothing is read from the user's windows. Focus inside the page is moved with UI Automation (SetFocus), not keys.
# SF01 environment: the page opened in Edge and UIA sees its fields (user, search, password)
# SF02 linking: edit a new item, [Link] shows the chip; with the user field focused in Edge the chip's [Link] captures
#      localhost:18765/login and the user field; [Save]
# SF03 linking the password field for a second item
# SF04 the file is format 6 (links inside the encrypted block)
# SF05 Edge in front on /login: the [Fill] button appears next to the fields; both links are found
# SF06 [Fill] types both items (a prefilled value in the user field is replaced, the search field is untouched), no Enter
# SF07 another path (/other): the button goes away (exact address match only)
# SF08 back on /login it appears again; after a lock it goes away and does not come back while locked
# SF09 the read-only diagnostics list the page's fields (names/ids) and never their values
# SF10 the same path with a query (/login?x=1) is another page: no button (Codex V55-1)
# SF11 a third item linked to the same user field: [Fill] refuses and types nothing (Codex: one field, one value)
# Second phase, fresh config, page checks delayed 1.5 s (ONEKEY_TEST_UIA_DELAY_MS, Codex R60/C61 conditions, 0.2.62). The checks read
# 1Key's own test observations (WM_APP+22, test mode only) instead of guessing from timing:
# SF12 a site diagnostic that 1Key starts only while a delayed page check is pending (a test barrier, not a lucky overlap): the
#      overlap and the diagnostic report are counted, then new page checks keep completing, the button hides on /other and comes
#      back on /login (R60-1, C61-4)
# SF13 a [Fill] click carrying the button token from before a lock -> unlock is ignored (nothing typed, no result); the current
#      token types and reports success (R60-3)
# SF14 linking on a page with one iframe works (control: UIA shows 1 nested document, the check is complete); on a page with 33
#      iframes UIA shows 33 nested documents, the check is incomplete (too many) and linking is refused with "not a field on this
#      page", the draft stays empty (R60-2, C61-3)
# SF15 one field linked, key delay 500 ms (saved), a 66-char dummy: typing starts, 1Key reports a timeout after 14.5-17 s, only a
#      non-empty exact prefix went in, nothing more arrives afterwards; a blocked start (token 0) fails the same judgement (C61-2)
# SF16 failure injection on the one-iframe page: nested document enumeration fails / a nested document cannot be read / element
#      comparison fails -> linking is refused every time (reasons 2, 3 and excluded fields), the draft stays empty (R60-2)
# SF17 after that timeout a new fill with a normal key delay types the whole value (the old limit does not stop new work, C61-1)
# SF18 a real lock (the Windows lock notification) and an expired time limit made to coincide at three points of a fill - before
#      taking the typing slot, before the UIA wait, right after capturing the target: every time the result is "stopped" (lock
#      first), 1Key is locked and nothing reaches the page (0.2.63, C62-2)
# SF19 a target that needs elevation (test injection): the result is "needs elevation" (not success), the elevation offer appears
#      (answered No) and nothing is typed (0.2.63, C62-1)
# Remaining Codex conditions (0.2.64):
# SF20 a late old page check (made 8 s slow once) finishes after lock -> unlock started a new check: it does not clear the new
#      check's pending mark (counted), the button comes back and checks keep completing (R60-1, reverse-order callback)
# SF21 a real lock notification in the middle of slow typing (natural order, no test stage): result "stopped", 1Key locked, only a
#      non-empty exact prefix typed, nothing more afterwards, no button while locked
# SF22 during slow typing the page focus moves to another field of the same page (UI Automation SetFocus): typing stops (result
#      "failed"), the other field gets nothing, nothing more afterwards (0.2.64: per-character check of the focused element)
# SF23 during slow typing another tab opens in the same Edge window: typing stops, the new tab is untouched, the filled field keeps
#      its prefix and gets nothing more
# SF24 the page replaces the field with a new element after 5 characters (like some frameworks do): typing stops at 5
# SF25 CPU and memory for 30 s with the watch running (Edge in front, button shown) and 30 s locked (no watch): 1Key under 5% of
#      one core; Edge's extra CPU with the watch under 25% of one core; 1Key private memory grows < 5 MB (numbers are reported)
# SF26 one item with two inputs (0.2.65, user decision 2026-10-01): input 1 linked to the user field, input 2 to the password
#      field (each with its own [Link]); one [Fill] types both (the prefilled user value is replaced), next to the other item on q
# 0.2.67 (2026-10-01 user report on a Nexacro intranet site: the cursor starts in the password field, [Fill] did nothing, the shortcut put the ID into
# the password field). Page /nexa imitates it: the password field is focused on load, and once "armed" the user field sends focus
# back to the password field unless it was really clicked (as some frameworks do):
# SF27 [Fill] still fills both: the cursor gets into the user field by the default action or a real click (method 2 or 3); the item
#      has "Enter after typing" on, so [Fill] presses Enter at the end too (user decision 2026-10-01; SF06 items have it off: no Enter)
# SF28 the item's shortcut on that page (cursor in the password field) fills the linked fields - ID into the ID field, password into
#      the password field - and presses Enter as the item says
# SF29 (0.2.70) the same shortcut while a page that is not linked is in front (/login, cursor in the user field) and the linked page
#      is not shown anywhere: nothing is typed (a linked item types only into its linked page; 2026-10-01 user report: the shortcut
#      typed an ID and password into the window that happened to be in front and sent them with Enter)
# SF30 (0.2.70) 1Key's own window in front, the linked page open in Edge: the shortcut brings Edge to the front and fills it
# SF31 (0.2.71) the list's [Type] button of a linked item does the same (no chip, no "type where the cursor is"; user report on another intranet site)
# 0.2.88: an item is linked only after choosing "Type into: a linked site or program field" in the edit screen (LinkKind)
# R75 dedicated failure-injection tests (Codex 15:11 section 5, 17:20 section 3). A [Fill] of the two-input Enter item on /nexa
# (fresh tab each time) is stopped at a test barrier (ONEKEY_TEST_HOLD_EVENT, test mode only); the harness first sees the barrier
# reached, changes one thing, then lets 1Key go on. Judged by the page itself: nothing more typed after the barrier (title the
# same), no Enter ("|sent"), and 1Key's result kind (3 stopped, 2 time limit, 4 failed). Events: lock (the Windows lock
# notification), expire (the fill's time limit made to expire, test hook), window (the desktop comes to the front), field (the page
# focus moves to another element of the same page: the password field, or the submit button before Enter).
# SF32 selall.mods     after the Ctrl/Alt release wait of the Ctrl+A step
# SF33 selall.guard-in right before the UIA check of the Ctrl+A step
# SF34 selall.guard-out right after it
# SF35 send.guard-in   right before the per-character UIA check
# SF36 send.guard-out  right after it
# SF37 enter.mods      after the release wait of the final Enter
# SF38 enter.guard-in  right before the UIA check of the final Enter
# SF39 enter.guard-out right after it
# R75-3: the cursor is moved by a real click (methods 1-2 skipped by a test hook) and the fill stops at "click.mods", after the
# modifier wait and before 1Key reads the field's rectangle and hit-tests it. The page records mouse presses on the field ("|md"),
# on a decoy left at the field's old place ("|decoy") and on a cover ("|cover"):
# SF40 control: nothing changes -> the field is clicked, both inputs typed, Enter
# SF41 the field moves away and a decoy takes its old place: the decoy is never pressed (a click at the new place is allowed)
# SF42 a cover is laid over the field: the cover is never pressed, nothing typed
# SF43 the field is replaced by a new element: no press on it, nothing typed
# SF44 1Key is locked: no press, nothing typed, locked
# 0.2.90 (Codex 06:48 R90-1/2): /nexa counts every beforeinput, input, Tab, Enter, Ctrl+A, submit and mouse press (field, decoy,
# cover) in an element read by UIA; the counts are cumulative, so a later input cannot hide an earlier Enter. SF32-SF44 take
# the counts at the barrier and require them unchanged afterwards (and the field values unchanged), and require that the change
# really happened before 1Key goes on (lock seen, limit hook answered, the desktop in front, the page focus on the other element,
# the field moved with the decoy at its old place / covered at its centre / replaced by a new element). A fill still running at
# the end fails the check.
# SF45 judge sensitivity: a character, Tab, Enter, Ctrl+A typed on purpose and a real press on the decoy are all counted
# 0.2.92 the item's "click the field if the cursor does not go in" switched off (SiteNexa). Off means only the coordinate click
# (method 3) is skipped; SetFocus and the accessibility default action (method 2) are still used (Codex 08:58 R92-1):
# SF46 /nexa as it is: never method 3; either filled exactly (ID, password, then submitted) by method 1/2 with result 1, or a
#      focus failure (result 4) with no input event
# SF47 methods 1-2 skipped by the test hook: the focus-failure path (result 4, method 0), no press, no input event
#      (SF40 is the same case with the switch on: clicked and typed)
# SF48 required control with the switch off on a page where SetFocus works (SiteBoth on /other): filled exactly, method 1
# 0.2.113-0.2.115 (sitefill simplification, Codex 05:45 / 06:00 / 06:30): a two-input item is linked with ONE [Link] on input 1;
# 1Key looks for the opposite field inside the nearest form boundary (Nexacro "Form" class or an HTML form landmark = a <form>
# with an accessible name). /nexa's form is now named, /login and /other keep an unnamed form (no landmark):
# SF26 /other (unnamed form): the first [Link] leaves the chip open asking for the password field; one more click links it
# SF27 /nexa (named form): one [Link] links both fields (pair); the item's advanced click switch is turned on (new items: off)
# SF49 per-input mode regression: Advanced > "Link each input separately" on, [Link] on input 1 and on input 2 as before
# SF50 ambiguous pair (/two: a named form with one text field and two password fields): no pair, the chip stays open
# SF51 a new linked item starts with the click switch off (Codex 05:45-D)
# 0.2.122 MainFrame start contrasts (Codex 06:30-at, 07:00 R115-1/2). Page /mf imitates a Nexacro login: a focusable group
# id "mainframe" class "MainFrame" around a named form with nexainput fields whose ids are Nexacro paths; once armed the user
# field refuses programmatic focus (focus goes back where it came from); Shift+Tab on the group itself goes to the password
# field (like Nexacro's own order from the "remember ID" check box). The page counts Shift+Tab (st). Every run is a fresh tab,
# the focus is put on the group (or elsewhere), then [Fill]; 1Key's last MainFrame start reason is read (observations 24-27).
# (the form element itself carries the id "mainframe.VFS.CF.form" like the Nexacro site's "mainframe.VFS_MAIN.CF_LOGIN.form": not a child frame)
# SF52 normal: Shift+Tab twice (group -> password -> user), both filled, reason 0
# SF53 a dialog (role=dialog) that does not cover the user field: no key at all (st 0), nothing typed, reason 8
# SF54 a visible Nexacro field of another frame (a popup form) that does not cover the user field: st 0, reason 8
# SF55 reading the linked field's own identity fails (test hook 5): st 0, reason 7
# SF56 the dialog check cannot be made (test hook 6): st 0, reason 8
# SF57 the focus is on another group (not MainFrame): st 0, reason 4
# SF58 the first Shift+Tab leaves the focus on the group: no second key (st 1), nothing typed
# SF59 the first Shift+Tab lands on another element (not the next linked field): no second key (st 1), nothing typed
# SF60 (0.2.123, real Nexacro site read survey: the "alert" popup has no input, no dialog signal, focus stays on MainFrame) a child
#      frame of the login frame without any input (id mainframe.VFS.CF.alertX.form.…) is shown: st 0, nothing typed, reason 8
# 0.2.127 (Codex 07:45-ay): SiteMf has "Enter after typing" on and /mf counts Enter (ent) and Space (sp) itself, so SF52 must
#      show ent 1 and every stopped run ent 0 sp 0 (measured, not assumed from the item setting)
# SF61 a browser alert() is open over /mf with the focus on MainFrame: MainFrame start not allowed (reason not 0), nothing typed,
#      no Shift+Tab reached the page after the alert is closed
# SF62-SF65 barrier "mf.second" (after the first Shift+Tab, before the second): lock / expire / window switch / page replaced
#      there -> no second Shift+Tab, nothing typed, no Enter, Shift released
# SF67 (Codex 09:45-bf) real Chromium list detection: an <input list> with a <datalist> opened by ArrowDown shows Chromium's own
#      autofill list; 1Key's list check (the same one as MainFrame start reason 9, test message lParam 13) must see it, and
#      must not see it after Esc closed it. (The saved-login list itself needs a saved password; the datalist uses the same popup.)
# SF66 (Codex 08:45-ba) the browser's saved-login list is reported open (test hook 8; the real list needs a saved password):
#      MainFrame start not allowed (reason 9), st 0, nothing typed, no Enter/Space
# Every run must have a visible button, a non-zero token and a finished request with a non-zero result of this request
# (results are reset first; 0 = no request ran). A run with token 0 is checked to fail the same judgement (Codex 09:08 R93-1/2).
# run091b findings (0.2.91): Edge updates the window title and the UIA tree a little after the page changes, so the baseline is
# taken only once the title is stable, and a change is "made" only once UIA shows it too (1Key can only see the page through UIA).
# A change of the page focus at a guard-out barrier (after the last UIA check, before the key) is the residual race of any
# check-then-act: it is printed as a NOTE (what reached the page), not judged; lock / expiry / window at those points are judged.
# Judgement: tools\tests\lib\Check.ps1. ASCII only.
param([switch]$Foreground, [string]$Exe = "")
$ErrorActionPreference = "Continue"
if (-not $Foreground) { "sitefill.ps1 opens Edge in the foreground and types into it. Run it with -Foreground on a PC nobody is using."; exit 2 }
. (Join-Path $PSScriptRoot "lib\Check.ps1")
$req = @(); for ($i = 1; $i -le 67; $i++) { $req += ("SF{0:D2}" -f $i) }
Start-Checks -Required $req

$sp = if ($env:ONEKEY_TEST_DIR) { $env:ONEKEY_TEST_DIR } else { Join-Path $env:TEMP "1Key-tests" }
New-Item -ItemType Directory -Force $sp | Out-Null
$exe = if ($Exe) { $Exe } else { Get-DefaultExe }
$suffix = ".site"
$cfg = "$sp\sitefill_cfg"; $profileDir = "$sp\sitefill_edge"
$edge = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
$port = 18765; $base = "http://localhost:$port"
$UserVal = "dummy-site-user"; $PwVal = "dummy-site-pw"

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, WindowsBase
Add-Type @'
using System;using System.Collections.Generic;using System.Runtime.InteropServices;using System.Text;
public class SU {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, StringBuilder l);
  [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
  [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, UIntPtr e);
  public static string Cls(IntPtr h) { var t = new StringBuilder(256); GetClassNameW(h, t, 256); return t.ToString(); }
  public static string Title(IntPtr h) { var t = new StringBuilder(512); GetWindowTextW(h, t, 512); return t.ToString(); }
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, CB cb, IntPtr l);
  // what 1Key said in its toast (diagnosis only - e.g. why a fill stopped)
  public static string ToastText(uint pid) { var r = new List<string>(); EnumWindows((h,l) => { uint q; GetWindowThreadProcessId(h, out q); if (q == pid && Cls(h) == "OneKeyToast" && IsWindowVisible(h)) { string own = Title(h); if (own.Length > 0) r.Add(own); EnumChildWindows(h, (c,x) => { string s = Title(c); if (s.Length > 0) r.Add(s); return true; }, IntPtr.Zero); } return true; }, IntPtr.Zero); return string.Join(" / ", r); }
  public static string ChildTexts(IntPtr h) { var r = new List<string>(); EnumChildWindows(h, (c,x) => { string s = Title(c); if (s.Length > 0) r.Add(s); return true; }, IntPtr.Zero); return string.Join(" | ", r); }
  public static List<IntPtr> All(uint pid, string cls) { var f = new List<IntPtr>(); EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && Cls(h) == cls && IsWindowVisible(h)) f.Add(h); return true; }, IntPtr.Zero); return f; }
  public static List<IntPtr> ByClass(string cls) { var f = new List<IntPtr>(); EnumWindows((h,l) => { if (Cls(h) == cls && IsWindowVisible(h)) f.Add(h); return true; }, IntPtr.Zero); return f; }
  public static string Text(IntPtr h) { var sb = new StringBuilder(8192); SendMessageW(h, 0x000D, (IntPtr)8192, sb); return sb.ToString(); }
  // bring a window to the front the documented way for a background process: a harmless Alt tap first
  public static bool Front(IntPtr h) { keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero); ShowWindow(h, 9); BringWindowToTop(h); return SetForegroundWindow(h); }
}
'@

# ---- local test page (HttpListener in its own runspace)
$page = @"
<!doctype html><html><head><meta charset="utf-8"><title>login</title></head><body>
<form onsubmit="return false">
<input id="user" aria-label="User ID" oninput="t()"> <input id="q" aria-label="Search" oninput="t()"> <input type="password" id="pw" aria-label="Password" oninput="t()">
</form><a id="go" href="/other">other</a> <a id="qv" href="/login?x=1">query</a>
<script>function t(){document.title='u='+user.value+'|q='+q.value+'|p='+pw.value}</script></body></html>
"@
$other = $page.Replace('<title>login</title>', '<title>other</title>').Replace('href="/other">other', 'href="/login">login')
$iframes = (1..33 | ForEach-Object { "<iframe srcdoc='<input id=f$_ aria-label=F$_>' width=40 height=20></iframe>" }) -join ""
$frames = "<!doctype html><html><head><meta charset=`"utf-8`"><title>frames</title></head><body><input id=`"fuser`" aria-label=`"Frame user`"> $iframes <a id=`"back`" href=`"/login`">login</a></body></html>"
$two = '<!doctype html><html><head><meta charset="utf-8"><title>two</title></head><body><form aria-label="Change"><input id="tu" aria-label="User"> <input type="password" id="tp1" aria-label="Old"> <input type="password" id="tp2" aria-label="New"></form></body></html>'
$swap = @"
<!doctype html><html><head><meta charset="utf-8"><title>swap</title></head><body>
<input id="q" aria-label="Search"> <a id="go" href="/login">login</a>
<script>function t(){document.title='s|q='+document.getElementById('q').value+'|'}
function h(){var e=document.getElementById('q');t();if(e.value.length==5&&!window.done){window.done=1;var n=e.cloneNode();n.value=e.value;n.addEventListener('input',h);e.replaceWith(n);n.focus();}}
document.getElementById('q').addEventListener('input',h);</script></body></html>
"@
$nexa = @"
<!doctype html><html><head><meta charset="utf-8"><title>nexa</title></head><body>
<form aria-label="Login" onsubmit="document.title+='|sent';return false">
<input id="nu" aria-label="User ID" oninput="t()"> <input type="password" id="np" aria-label="Password" oninput="t()"> <button type="submit" id="sb">go</button>
</form><a id="go" href="/login">login</a> <button id="arm" onclick="localStorage.setItem('armed','1');armed=1">arm</button>
<p><button id="rec" onclick="rec=1">rec</button> <button id="mv" onclick="mv()">mv</button> <button id="cv" onclick="cv()">cv</button> <button id="rp" onclick="rp()">rp</button></p>
<div id="ev" role="status" aria-label="-">-</div>
<script>var armed=localStorage.getItem('armed')=='1'?1:0,md=0,rec=0,hits='';function t(){document.title='u='+nu.value+'|p='+np.value+hits}
var E={bi:0,inp:0,tab:0,ent:0,ca:0,sub:0,md:0,dec:0,cov:0,mv:0,cv:0,rp:0};
function shw(){var s='';for(var k in E)s+=k+'='+E[k]+';';ev.setAttribute('aria-label',s);ev.textContent=s}
document.addEventListener('beforeinput',function(){E.bi++;shw()},true);
document.addEventListener('input',function(){E.inp++;shw()},true);
document.addEventListener('keydown',function(e){if(e.key=='Tab')E.tab++;if(e.key=='Enter')E.ent++;if(e.ctrlKey&&(e.key=='a'||e.key=='A'))E.ca++;shw()},true);
document.addEventListener('submit',function(){E.sub++;shw()},true);
function hook(e){e.addEventListener('mousedown',function(){md=Date.now();E.md++;shw();if(rec){hits+='|md';t()}});e.addEventListener('focus',function(){if(armed&&Date.now()-md>1500)setTimeout(function(){np.focus()},0)})}
hook(nu);
function box(id,tag,k){var r=nu.getBoundingClientRect(),d=document.createElement(tag);d.id=id;d.textContent=id;d.style.cssText='position:absolute;margin:0;padding:0;left:'+(r.left+scrollX)+'px;top:'+(r.top+scrollY)+'px;width:'+r.width+'px;height:'+r.height+'px;background:#fc8';d.addEventListener('mousedown',function(ev){E[k]++;shw();hits+='|'+id;t();ev.preventDefault()});document.body.appendChild(d);return r}
function mv(){var r=box('decoy','button','dec');nu.style.marginLeft='240px';var q=nu.getBoundingClientRect();if(q.left>r.left+100&&document.getElementById('decoy'))E.mv++;shw()}
function cv(){box('cover','div','cov');var r=nu.getBoundingClientRect(),h=document.elementFromPoint(r.left+r.width/2,r.top+r.height/2);if(h&&h.id=='cover')E.cv++;shw()}
function rp(){var o=nu,n=nu.cloneNode();o.replaceWith(n);hook(n);if(!o.isConnected&&document.getElementById('nu')===n)E.rp++;shw()}
shw();np.focus();</script></body></html>
"@
$mf = @"
<!doctype html><html><head><meta charset="utf-8"><title>mf</title></head><body>
<div id="mainframe" class="MainFrame" role="group" aria-label="MainFrame" tabindex="0" style="padding:8px;border:1px solid #888">
<form id="mainframe.VFS.CF.form" aria-label="Login" onsubmit="return false">
<input id="mainframe.VFS.CF.form.edtUserId:input" class="nexainput" aria-label="User ID" oninput="t()"> <input type="password" id="mainframe.VFS.CF.form.edtPassword:input" class="nexainput" aria-label="Password" oninput="t()">
</form></div>
<p><button id="armf">arm</button> <button id="dlgb">dialog</button> <button id="popb">pop</button> <button id="stuckb">stuck</button> <button id="sideb">side</button></p>
<div id="og" role="group" aria-label="Other" tabindex="0" style="padding:4px;border:1px solid #888">other</div>
<div id="dlg" role="dialog" aria-modal="true" aria-label="Find ID" style="display:none;margin-top:40px;border:1px solid #888;padding:8px">find id</div>
<div id="pop" style="display:none;margin-top:40px"><input id="mainframe.POP.form.edtName:input" class="nexainput" aria-label="Name"></div>
<div id="mainframe.VFS.CF.alertX.form" role="group" aria-label="Alert" style="display:none;margin-top:40px;border:1px solid #888"><span id="mainframe.VFS.CF.alertX.form.st_msg" role="note">message</span> <button id="mainframe.VFS.CF.alertX.form.btn_ok">ok</button></div>
<button id="alrb">alert</button> <input id="dlx" list="dl1" aria-label="Pick"><datalist id="dl1"><option value="alpha"><option value="beta"><option value="gamma"></datalist> <button id="jsab">jsalert</button> <a id="gol" href="/login">login</a>
<div id="ev" role="status" aria-label="-">-</div>
<script>var mf=document.getElementById('mainframe'),u=document.getElementById('mainframe.VFS.CF.form.edtUserId:input'),pw=document.getElementById('mainframe.VFS.CF.form.edtPassword:input'),armed=0,kb=0,mode='';
var E={st:0,tab:0,bounce:0,ent:0,sp:0};
function shw(){var s='';for(var k in E)s+=k+'='+E[k]+';';ev.setAttribute('aria-label',s);ev.textContent=s}
function t(){document.title='u='+u.value+'|p='+pw.value}
document.addEventListener('keydown',function(e){if(e.key=='Tab'){kb=Date.now();if(e.shiftKey)E.st++;else E.tab++;shw()}if(e.key=='Enter'){E.ent++;shw()}if(e.key==' '){E.sp++;shw()}},true);
mf.addEventListener('keydown',function(e){if(e.key=='Tab'&&e.shiftKey&&document.activeElement===mf){e.preventDefault();if(mode=='stuck')return;if(mode=='side'){og.focus();return}pw.focus()}});
u.addEventListener('focus',function(e){if(armed&&Date.now()-kb>500){E.bounce++;shw();(e.relatedTarget||mf).focus()}});
armf.onclick=function(){armed=1};dlgb.onclick=function(){dlg.style.display='block'};popb.onclick=function(){pop.style.display='block'};
stuckb.onclick=function(){mode='stuck'};jsab.onclick=function(){setTimeout(function(){alert('x')},300)};alrb.onclick=function(){document.getElementById('mainframe.VFS.CF.alertX.form').style.display='block'};sideb.onclick=function(){mode='side'};
shw();</script></body></html>
"@
$frame1 = "<!doctype html><html><head><meta charset=`"utf-8`"><title>frame1</title></head><body><input id=`"fuser`" aria-label=`"Frame user`"> <iframe srcdoc='<input id=f1 aria-label=F1>' width=120 height=40></iframe> <a id=`"back`" href=`"/login`">login</a></body></html>"
$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("$base/")
$rs = [runspacefactory]::CreateRunspace(); $rs.Open()
$ps = [powershell]::Create(); $ps.Runspace = $rs
[void]$ps.AddScript({
  param($l, $login, $other, $frames, $frame1, $swap, $nexa, $two, $mf)
  while ($l.IsListening) {
    try {
      $c = $l.GetContext()
      $path = $c.Request.Url.AbsolutePath
      $body = if ($path -eq "/other") { $other } elseif ($path -eq "/frames") { $frames } elseif ($path -eq "/frame1") { $frame1 } elseif ($path -eq "/swap") { $swap } elseif ($path -eq "/nexa") { $nexa } elseif ($path -eq "/two") { $two } elseif ($path -eq "/mf") { $mf } else { $login }
      $b = [Text.Encoding]::UTF8.GetBytes($body)
      $c.Response.ContentType = "text/html; charset=utf-8"; $c.Response.OutputStream.Write($b, 0, $b.Length); $c.Response.Close()
    } catch { }
  }
}).AddArgument($listener).AddArgument($page).AddArgument($other).AddArgument($frames).AddArgument($frame1).AddArgument($swap).AddArgument($nexa).AddArgument($two).AddArgument($mf)

# test barriers (ONEKEY_TEST_HOLD_EVENT): arm a point, wait until 1Key stops there, change something, then let it go on
$hold = "OneKeyTestHold_" + [Guid]::NewGuid().ToString("N").Substring(0, 8)
function Arm($point) { @{ Arm = New-Object System.Threading.EventWaitHandle($false, 'ManualReset', "Local\$hold.arm.$point"); Reached = New-Object System.Threading.EventWaitHandle($false, 'AutoReset', "Local\$hold.reached"); Go = New-Object System.Threading.EventWaitHandle($false, 'AutoReset', "Local\$hold.go") } }
function WaitReached($h) { [bool]$h.Reached.WaitOne(8000) }
function Release($h) { $h.Arm.Dispose(); [void]$h.Go.Set(); Start-Sleep -Milliseconds 500; $h.Reached.Dispose(); $h.Go.Dispose() }
# "Use" (id 348): Down selects "a linked site or program field" (0.2.88); the screen is rebuilt after the notification
function LinkKind() { [void][SU]::PostMessageW([SU]::GetDlgItem($m, 348), 0x0100, [IntPtr]0x28, [IntPtr]::Zero); Start-Sleep -Milliseconds 700 }
function SetText($id, $s) { [void][SU]::SendMessageW([SU]::GetDlgItem($m, $id), 0x000C, [IntPtr]::Zero, [string]$s) }
function Text($id) { $h = [SU]::GetDlgItem($m, $id); if ($h -eq [IntPtr]::Zero) { "(none)" } else { [SU]::Text($h) } }
function Click($id, $ms = 700) { [void][SU]::PostMessageW([SU]::GetDlgItem($m, $id), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds $ms }
function Key($id, $vk) { [void][SU]::PostMessageW([SU]::GetDlgItem($m, $id), 0x0100, [IntPtr]$vk, [IntPtr]::Zero); Start-Sleep -Milliseconds 400 }
function Box() { for ($i=0;$i -lt 15;$i++) { $b = @([SU]::All([uint32]$p.Id, "OneKeyDialog")); if ($b.Count -gt 0) { return $b[0] }; Start-Sleep -Milliseconds 100 }; [IntPtr]::Zero }
function Answer($b, $cmd) { if ($b -ne [IntPtr]::Zero) { [void][SU]::PostMessageW($b, 0x0111, [IntPtr]$cmd, [IntPtr]::Zero); Start-Sleep -Milliseconds 600 } }
function Site() { $v = [int64][SU]::SendMessageW($m, 0x8013, [IntPtr]::Zero, [IntPtr]::Zero); if ((($v -shr 60) -band 1) -ne 1) { "no answer" } else { "links=$($v -band 0xFF) watch=$(($v -shr 8) -band 1) button=$(($v -shr 9) -band 1) page=$(($v -shr 10) -band 1) hits=$(($v -shr 16) -band 0xFF)" } }
function Token() { $v = [int64][SU]::SendMessageW($m, 0x8013, [IntPtr]::Zero, [IntPtr]::Zero); ($v -shr 32) -band 0xFFFF }
function Running() { $v = [int64][SU]::SendMessageW($m, 0x8013, [IntPtr]::Zero, [IntPtr]::Zero); ($v -shr 24) -band 1 }
function Invoke([string]$id) { $e = Field $id; if ($e) { ([Windows.Automation.InvokePattern]$e.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke(); $true } else { $false } }
function Navigate([string]$path) {
  # through the page's own links: /login -> /other -> /login gives a fresh /login (title "login")
  if ($path -eq "/login") { [void](Invoke "go"); Start-Sleep -Milliseconds 1500; [void](Invoke "go"); Start-Sleep -Milliseconds 1500 }
}
# 1Key test observations (WM_APP+22 = 0x8016, test mode only). lParam 0: order, 1: results, 2: inject (wParam), 3: UIA boundary, 4: reset results
function S2([int]$sel, [int]$w = 0) { [int64][SU]::SendMessageW($m, 0x8016, [IntPtr]$w, [IntPtr]$sel) }
function Order() { $v = S2 0; [pscustomobject]@{ Probes = $v -band 0xFFFF; Pending = ($v -shr 16) -band 1; Overlap = ($v -shr 18) -band 63; Diag = ($v -shr 24) -band 63; Stale = ($v -shr 32) -band 0xFF } }
function Results() { $v = S2 1; [pscustomobject]@{ KeyDelay = $v -band 0xFFFF; FillMs = ($v -shr 16) -band 0xFFFF; Fill = ($v -shr 32) -band 0xF; Link = ($v -shr 36) -band 0xFF; Draft = ($v -shr 44) -band 1; FocusMethod = ($v -shr 45) -band 7; HotkeyLinked = ($v -shr 48) -band 0xFF } }
function Boundary() { $v = S2 3; "docs=$(($v -band 0xFF) - 1) reason=$(($v -shr 8) -band 0xF) excl=$([int]((($v -shr 12) -band 0xFF) -gt 0))" }
function Inject([int]$mode) { [void](S2 2 $mode) }
function OpenPage([string]$path) {
  Start-Process $edge -ArgumentList @("--user-data-dir=`"$profileDir`"", "$base$path") | Out-Null
  $name = $path.TrimStart('/'); $script:edgeWnd = [IntPtr]::Zero
  [void](WaitFor { foreach ($h in [SU]::ByClass("Chrome_WidgetWin_1")) { if ([SU]::Title($h) -like "$name*") { $script:edgeWnd = $h } }; $script:edgeWnd -ne [IntPtr]::Zero } 10000)
  Start-Sleep -Milliseconds 1500
}
# a link attempt that is never saved: new item, [Link], focus the field, chip [Link]; wait for 1Key's own link result
# (1 linked, 13 = the focused element is not a field of this page, 2 = UIA did not answer); then leave without saving
function TryLink([string]$fieldId, [string]$name) {
  Click 203; Click 4001; SetText 301 $name; SetText 302 "dummy-frame"; LinkKind
  [void](S2 4)
  Click 323 1000
  $res = "no chip"
  $chip = @([SU]::All([uint32]$p.Id, "OneKeyChip"))
  if ($chip.Count -gt 0) {
    if (FocusField $fieldId) {
      [void][SU]::PostMessageW($chip[0], 0x0111, [IntPtr]11, [IntPtr]::Zero)
      [void](WaitFor { $c = (Results).Link; $c -ne 0 -and $c -ne 255 } 8000)
      Start-Sleep -Milliseconds 400
      $r = Results; $res = "link=$($r.Link) draft=$($r.Draft)"
    } else { $res = "no field $fieldId" }
    if (@([SU]::All([uint32]$p.Id, "OneKeyChip")).Count -gt 0) { [void][SU]::PostMessageW($chip[0], 0x0111, [IntPtr]12, [IntPtr]::Zero) }
    [void](WaitFor { [SU]::IsWindowVisible($m) } 4000)
  }
  Click 309 700; Answer (Box) 6          # leave the edit screen without saving
  $res
}
function QOf([string]$t) { $mm = [regex]::Match($t, '\|q=([^|]*)\|'); if ($mm.Success) { $mm.Groups[1].Value } else { $null } }
# one fill with a given token on the current page; what 1Key reports and what the page shows
function Run15([int]$tok) {
  [void](S2 4)
  [void][SU]::PostMessageW($m, 0x8011, [IntPtr]$tok, [IntPtr]::Zero)
  $started = WaitFor { (Running) -eq 1 } 3000
  if ($started) { [void](WaitFor { (Running) -eq 0 } 25000) }
  Start-Sleep -Milliseconds 300
  $r = Results
  $q1 = QOf ([SU]::Title($script:edgeWnd)); Start-Sleep -Milliseconds 3000; $q2 = QOf ([SU]::Title($script:edgeWnd))
  [pscustomobject]@{ Started = $started; Fill = $r.Fill; Ms = $r.FillMs; Q = $q1; Stable = ($q1 -eq $q2) }
}
function Judge15($r, [string]$full) {
  [bool]($r.Started -and $r.Fill -eq 2 -and $r.Ms -ge 14500 -and $r.Ms -le 17000 -and $null -ne $r.Q -and $r.Q.Length -gt 0 -and $r.Q.Length -lt $full.Length -and $full.StartsWith($r.Q) -and $r.Stable)
}
# a fresh load of /login or /other through the page's own "go" link (its text names the target page)
function ToPage([string]$want) {
  for ($k = 0; $k -lt 2; $k++) { $g = Field "go"; if (-not $g) { break }; $dest = $g.Current.Name; [void](Invoke "go"); Start-Sleep -Milliseconds 1500; if ($dest -eq $want) { break } }
  $script:edgeWnd = EdgeWindow
}
function UOf([string]$t) { $mm = [regex]::Match($t, '^u=([^|]*)\|'); if ($mm.Success) { $mm.Groups[1].Value } else { "" } }
function Prefix($q, [string]$full) { [bool]($null -ne $q -and $q.Length -gt 0 -and $q.Length -lt $full.Length -and $full.StartsWith($q)) }
function StartFill() { [void](S2 4); [void][SU]::PostMessageW($m, 0x8011, [IntPtr](Token), [IntPtr]::Zero); WaitFor { (Running) -eq 1 } 3000 }
function CtrlKey([byte]$vk) { [SU]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero); [SU]::keybd_event($vk, 0, 0, [UIntPtr]::Zero); [SU]::keybd_event($vk, 0, 2, [UIntPtr]::Zero); [SU]::keybd_event(0x11, 0, 2, [UIntPtr]::Zero) }
function Usage($ids) {
  $cpu = 0.0; $ws = 0
  foreach ($i in $ids) { $pp = Get-Process -Id $i -ErrorAction Ignore; if ($pp) { $cpu += $pp.TotalProcessorTime.TotalSeconds; $ws += $pp.WorkingSet64 } }
  [pscustomobject]@{ Cpu = $cpu; Ws = $ws }
}
# link one input of the item being edited: its [Link] button, then the page field, then the chip's [Link]
function LinkNow([int]$btn, [string]$fieldId) {
  Click $btn 1000
  $chip = @([SU]::All([uint32]$p.Id, "OneKeyChip"))
  if ($chip.Count -eq 0) { return $false }
  if (-not (FocusField $fieldId)) { return $false }
  [void][SU]::PostMessageW($chip[0], 0x0111, [IntPtr]11, [IntPtr]::Zero)
  [bool](WaitFor { [SU]::IsWindowVisible($m) } 5000)
}
# the chip is still open after a [Link] (pair not decided): click the remaining field and press the chip's [Link]
function LinkMore([string]$fieldId) {
  $chip = @([SU]::All([uint32]$p.Id, "OneKeyChip"))
  if ($chip.Count -eq 0) { return $false }
  if (-not (FocusField $fieldId)) { return $false }
  [void][SU]::PostMessageW($chip[0], 0x0111, [IntPtr]11, [IntPtr]::Zero)
  [bool](WaitFor { [SU]::IsWindowVisible($m) } 5000)
}
function ChipOpen() { @([SU]::All([uint32]$p.Id, "OneKeyChip")).Count -gt 0 }
function CloseChip() { $c = @([SU]::All([uint32]$p.Id, "OneKeyChip")); if ($c.Count -gt 0) { [void][SU]::PostMessageW($c[0], 0x0111, [IntPtr]12, [IntPtr]::Zero) }; [void](WaitFor { [SU]::IsWindowVisible($m) } 3000) }
# one [Fill] on a fresh /mf tab: arm, optional page buttons, focus (the MainFrame group unless another id), optional test hook
function MfRun([string[]]$buttons = @(), [string]$focusId = "mainframe", [int]$hook = 0) {
  OpenPage "/mf"
  [void](Invoke "armf"); foreach ($b in $buttons) { [void](Invoke $b) }
  Start-Sleep -Milliseconds 300
  $f = Field $focusId; if ($f) { $f.SetFocus() }
  [void][SU]::Front($script:edgeWnd); $vis = WaitFor { FillVisible } 8000
  Inject $hook; [void](S2 4)
  [void][SU]::PostMessageW($m, 0x8011, [IntPtr](Token), [IntPtr]::Zero)
  [void](WaitFor { (Running) -eq 1 } 3000); [void](WaitFor { (Running) -eq 0 } 15000)
  Start-Sleep -Milliseconds 600
  $ev = Ev; $obs = S2 3; $r = Results
  Inject 0
  [pscustomobject]@{ Vis = $vis; Title = ([SU]::Title($script:edgeWnd)) -replace '^(\S+).*$', '$1'; St = [int]$ev['st']; Bounce = [int]$ev['bounce']; Reason = (($obs -shr 24) -band 0xF) - 1; Fill = $r.Fill
                     Ent = [int]$ev['ent']; Sp = [int]$ev['sp'] }
}
function FillVisible() { @([SU]::All([uint32]$p.Id, "OneKeyFill")).Count -gt 0 }
function WaitFor([scriptblock]$cond, $ms = 5000) { $t = 0; while ($t -lt $ms) { if (& $cond) { return $true }; Start-Sleep -Milliseconds 200; $t += 200 }; $false }
function EdgeWindow() { foreach ($h in [SU]::ByClass("Chrome_WidgetWin_1")) { $t = [SU]::Title($h); if ($t -like "*login*" -or $t -like "u=*" -or $t -like "*other*") { return $h } }; [IntPtr]::Zero }
# an element of the page in the visible tab: with several tabs open (OpenPage adds tabs) background tabs can also be in the UIA tree,
# so search only the on-screen document (0.2.71 run: "arm" pressed in a background /nexa tab, SF27 never saw a refused focus)
function Field([string]$id) {
  $root = [Windows.Automation.AutomationElement]::FromHandle($script:edgeWnd)
  $docCond = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Document)
  $scope = $root
  foreach ($d in $root.FindAll([Windows.Automation.TreeScope]::Descendants, $docCond)) { if (-not $d.Current.IsOffscreen) { $scope = $d; break } }
  $cond = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
  $scope.FindFirst([Windows.Automation.TreeScope]::Descendants, $cond)
}
# the Edge window title once it stopped changing (Edge shows a new page title a little later)
function StableTitle() { $a = [SU]::Title($script:edgeWnd); for ($i = 0; $i -lt 10; $i++) { Start-Sleep -Milliseconds 200; $b = [SU]::Title($script:edgeWnd); if ($b -eq $a) { return $b }; $a = $b }; $a }
# /nexa's cumulative event counters (element "ev", read by UIA): a hashtable name -> count
function Ev() { $h = @{}; $e = Field "ev"; if ($e) { foreach ($kv in ($e.Current.Name -split ';')) { if ($kv -match '^(\w+)=(\d+)$') { $h[$Matches[1]] = [int]$Matches[2] } } }; $h }
function EvDelta($a, $b, [string[]]$names) { ($names | ForEach-Object { "$_=$([int]$b[$_] - [int]$a[$_])" }) -join ' ' }
function FocusField([string]$id) { [void][SU]::Front($script:edgeWnd); Start-Sleep -Milliseconds 300; $f = Field $id; if ($f) { $f.SetFocus(); Start-Sleep -Milliseconds 300; $true } else { $false } }
function Launch() {
  $script:p = Start-Process $exe -PassThru
  $script:m = [IntPtr]::Zero
  for ($i = 0; $i -lt 80; $i++) { $w = @([SU]::All([uint32]$script:p.Id, "OneKeyMainWindow$suffix")); if ($w.Count -gt 0) { $script:m = $w[0]; break }; Start-Sleep -Milliseconds 100 }
  if ($script:m -eq [IntPtr]::Zero) { throw "no main window" }
  Start-Sleep -Milliseconds 700
}
function Quit() { [void][SU]::PostMessageW($m, 0x8005, [IntPtr]::Zero, [IntPtr]::Zero); for ($i=0; $i -lt 50 -and -not $p.HasExited; $i++) { Start-Sleep -Milliseconds 100 } }
function LinkItem([string]$name, [string]$content, [string]$fieldId) {
  Click 203; Click 4001; SetText 301 $name; SetText 302 $content; LinkKind
  Click 323 1000
  $chip = @([SU]::All([uint32]$p.Id, "OneKeyChip"))
  if ($chip.Count -eq 0) { return "no chip" }
  if (-not (FocusField $fieldId)) { return "no field $fieldId" }
  [void][SU]::PostMessageW($chip[0], 0x0111, [IntPtr]11, [IntPtr]::Zero)   # chip [Link] (id 11) -> 1Key reads the focused field
  if (-not (WaitFor { [SU]::IsWindowVisible($m) } 5000)) { return "main window did not come back" }
  Start-Sleep -Milliseconds 500
  $t = Text 322
  Click 310 1200; Answer (Box) 1
  $t
}

$before = [SU]::GetForegroundWindow()
$edgeProc = $null
try {
  Stop-TestInstances $suffix
  if (-not (Test-Path $edge)) { throw "Edge not found: $edge" }
  foreach ($d in @($cfg, $profileDir)) { if (Test-Path $d) { Get-ChildItem $d -Recurse -Force | Remove-Item -Force -Recurse -ErrorAction Ignore }; New-Item -ItemType Directory -Force $d | Out-Null }
  $listener.Start(); $handle = $ps.BeginInvoke()

  $edgeProc = Start-Process $edge -ArgumentList @("--user-data-dir=`"$profileDir`"", "--no-first-run", "--no-default-browser-check", "--disable-sync", "--new-window", "$base/login") -PassThru
  $script:edgeWnd = [IntPtr]::Zero
  [void](WaitFor { $script:edgeWnd = EdgeWindow; $script:edgeWnd -ne [IntPtr]::Zero } 15000)
  Start-Sleep -Milliseconds 1500
  $ok1 = ($script:edgeWnd -ne [IntPtr]::Zero) -and (Field "user") -and (Field "q") -and (Field "pw")
  Check SF01 "True" "$([bool]$ok1)" "Edge shows the test page and UIA sees user/search/password"

  $env:ONEKEY_TEST = "1"; $env:ONEKEY_CONFIG_DIR = $cfg; $env:ONEKEY_INSTANCE_SUFFIX = $suffix; $env:ONEKEY_TEST_WORKAREA = $TallScreen; $env:ONEKEY_TEST_FAIL = $null; $env:ONEKEY_TEST_HOLD_EVENT = $hold
  Launch
  SetText 101 "Master1234"; SetText 102 "Master1234"; Click 103; Answer (Box) 1
  Click 220; Key 2005 0x24; Click 2012 3500
  Confirm-AutoLockOff $cfg

  # ---- SF02 / SF03 link two items
  $t2 = LinkItem "SiteUser" $UserVal "user"
  Check SF02 "User ID (user)|localhost:18765/login" (($t2 -replace "\r?\n", "|") -replace ' \u00B7 ', '|' -replace 'http://', '') "user field linked (field name and id, then the address)"
  $t3 = LinkItem "SitePw" $PwVal "pw"
  Check SF03 "Password (pw)|localhost:18765/login" (($t3 -replace "\r?\n", "|") -replace ' \u00B7 ', '|' -replace 'http://', '') "password field linked"

  # ---- SF04 file format
  $sv = ([regex]::Match((Get-TestHeader $cfg), '(?m)^secretv=(\d+)')).Groups[1].Value
  Check SF04 "10" $sv "links are stored in format 10 (format 8 fields + Argon2id key since 0.3.4x; new items keep their click switch, off by default)"

  # ---- SF05 the button appears on /login
  [void][SU]::Front($script:edgeWnd)
  $seen = WaitFor { FillVisible } 5000
  Check SF05 "True|links=2 watch=1 button=1 page=1 hits=2" ("$seen|" + (Site)) "Edge in front on /login: [Fill] next to the fields, both found"

  # ---- SF06 fill (the user field has a prefilled value that must be replaced; search stays empty)
  $uf = Field "user"; ([Windows.Automation.ValuePattern]$uf.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)).SetValue("old")
  [void][SU]::Front($script:edgeWnd); Start-Sleep -Milliseconds 1200
  $fb = @([SU]::All([uint32]$p.Id, "OneKeyFill"))
  if ($fb.Count -gt 0) { [void][SU]::PostMessageW([SU]::GetDlgItem($fb[0], 11), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) }
  $want = "u=$UserVal|q=|p=$PwVal"
  [void](WaitFor { [SU]::Title($script:edgeWnd) -like "$want*" } 8000)
  Check SF06 $want (([SU]::Title($script:edgeWnd)) -replace ' - .*$', '' -replace ' .\s*Microsoft.*$', '') "[Fill] typed both items; prefilled text replaced; search untouched; no Enter"

  # ---- SF10 same path with a query: another page, no button
  $qv = Field "qv"; if ($qv) { ([Windows.Automation.InvokePattern]$qv.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke() }
  Start-Sleep -Milliseconds 2000
  [void][SU]::Front($script:edgeWnd)
  $goneQ = WaitFor { -not (FillVisible) } 4000
  Check SF10 "True|page=0" ("$goneQ|" + ((Site) -replace '^.*(page=\d).*$', '$1')) "/login?x=1 is not /login: no button (exact address incl. query)"

  # ---- SF07 another path: gone
  $go = Field "go"; if ($go) { ([Windows.Automation.InvokePattern]$go.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke() }
  Start-Sleep -Milliseconds 2000
  $gone = WaitFor { -not (FillVisible) } 4000
  Check SF07 "True|page=0" ("$gone|" + ((Site) -replace '^.*(page=\d).*$', '$1')) "/other: no button (exact address only)"

  # ---- SF08 back, then lock
  $back = Field "go"; if ($back) { ([Windows.Automation.InvokePattern]$back.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke() }
  [void][SU]::Front($script:edgeWnd)
  $again = WaitFor { FillVisible } 5000
  [void][SU]::PostMessageW($m, 0x02B1, [IntPtr]7, [IntPtr]::Zero)   # Windows lock notification -> 1Key locks
  Start-Sleep -Milliseconds 800
  [void][SU]::Front($script:edgeWnd); Start-Sleep -Milliseconds 2500
  Check SF08 "True|False|links=0 watch=0" ("$again|$(FillVisible)|" + ((Site) -replace ' button.*$', '')) "back on /login it appears; after a lock it is gone and stays gone"

  # ---- SF09 diagnostics: names/ids, never values (unlock, open advanced, run the check on Edge)
  SetText 101 "Master1234"; Click 103 1500
  Click 220; Click 212
  Click 214 900; Answer (Box) 1          # guide box: OK -> 3 s
  [void][SU]::Front($script:edgeWnd)
  $rep = ""
  if (WaitFor { (Box) -ne [IntPtr]::Zero } 8000) { $b = Box; $rep = [SU]::Text([SU]::GetDlgItem($b, 101)); Answer $b 1 }
  $hasIds = ($rep -match 'id=user') -and ($rep -match 'id=pw') -and ($rep -match 'localhost:18765/login')
  $noValues = -not ($rep -match [regex]::Escape($UserVal)) -and -not ($rep -match [regex]::Escape($PwVal))
  Check SF09 "True|True" "$hasIds|$noValues" "diagnostics list field ids and the address, never the typed values"

  # ---- SF11 two items linked to one field: nothing is typed
  Click 405 900; Click 240 900   # advanced -> back (settings, 0.3.75) -> back (list)
  $t11 = LinkItem "SiteUser2" "dummy-other-user" "user"
  $toOther = Field "go"; if ($toOther) { ([Windows.Automation.InvokePattern]$toOther.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke() }   # /login -> /other
  Start-Sleep -Milliseconds 1500
  $back2 = Field "go"; if ($back2) { ([Windows.Automation.InvokePattern]$back2.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke() }   # /other -> /login (fresh page)
  [void][SU]::Front($script:edgeWnd)
  $vis = WaitFor { FillVisible } 5000
  $fb = @([SU]::All([uint32]$p.Id, "OneKeyFill"))
  if ($fb.Count -gt 0) { [void][SU]::PostMessageW([SU]::GetDlgItem($fb[0], 11), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) }
  Start-Sleep -Milliseconds 2500
  $title = ([SU]::Title($script:edgeWnd)) -replace ' - .*$', ''
  Check SF11 "True|login" ("$vis|$title") "two items on one field: [Fill] refuses, the page stays untouched"

  # ================= second phase: fresh config, delayed page checks (R60, C61)
  Quit
  if (Test-Path $cfg) { Get-ChildItem $cfg -Recurse -Force | Remove-Item -Force -Recurse -ErrorAction Ignore }
  $env:ONEKEY_TEST_UIA_DELAY_MS = "1500"
  Launch
  SetText 101 "Master1234"; SetText 102 "Master1234"; Click 103; Answer (Box) 1
  Click 220; Key 2005 0x24; Click 2012 3500
  Confirm-AutoLockOff $cfg
  [void](LinkItem "SiteUser" $UserVal "user")
  [void](LinkItem "SitePw" $PwVal "pw")

  # ---- SF12 a diagnostic that starts while a delayed page check is pending; the watch keeps running afterwards
  [void][SU]::Front($script:edgeWnd)
  $vis0 = WaitFor { FillVisible } 8000
  $a0 = Order
  Click 220; Click 212
  Click 214 300; Answer (Box) 1          # the guide: OK -> 3 s -> (waits for a pending page check) -> report
  [void][SU]::Front($script:edgeWnd)
  $report = WaitFor { (Box) -ne [IntPtr]::Zero } 15000
  $a1 = Order
  if ($report) { Answer (Box) 1 }
  Click 405 700; Click 240 700
  [void][SU]::Front($script:edgeWnd)
  $p1 = (Order).Probes
  $more = WaitFor { ((Order).Probes - $p1) -ge 2 } 12000
  $shown = WaitFor { FillVisible } 8000
  [void](Invoke "go"); Start-Sleep -Milliseconds 1000; [void][SU]::Front($script:edgeWnd)   # /login -> /other (nothing linked there)
  $hid = WaitFor { -not (FillVisible) } 8000
  [void](Invoke "go"); [void][SU]::Front($script:edgeWnd)                                    # /other -> /login
  $back12 = WaitFor { FillVisible } 10000
  $got12 = "$vis0|overlap=$($a1.Overlap - $a0.Overlap) diag=$($a1.Diag - $a0.Diag) report=$report|more=$more|shown=$shown|hid=$hid|back=$back12"
  Check SF12 "True|overlap=1 diag=1 report=True|more=True|shown=True|hid=True|back=True" $got12 "diagnostic started during a pending check (barrier), report shown, checks continue, button hides and returns (R60-1, C61-4)"

  # ---- SF13 an old click token after lock -> unlock does nothing; the current token types
  [void](WaitFor { FillVisible } 8000)
  $old = Token
  [void][SU]::PostMessageW($m, 0x02B1, [IntPtr]7, [IntPtr]::Zero); Start-Sleep -Milliseconds 800   # lock
  SetText 101 "Master1234"; Click 103 1500                                                       # unlock
  [void][SU]::Front($script:edgeWnd); [void](WaitFor { FillVisible } 10000)
  $new = Token
  [void](S2 4)
  [void][SU]::PostMessageW($m, 0x8011, [IntPtr]$old, [IntPtr]::Zero); Start-Sleep -Milliseconds 3000   # the stale click
  $t13a = ([SU]::Title($script:edgeWnd)) -replace ' - .*$', ''
  $r13a = (Results).Fill
  [void][SU]::PostMessageW($m, 0x8011, [IntPtr]$new, [IntPtr]::Zero)                                  # the current click
  [void](WaitFor { [SU]::Title($script:edgeWnd) -like "u=$UserVal|*" } 8000)
  [void](WaitFor { (Running) -eq 0 } 5000)
  $t13b = ([SU]::Title($script:edgeWnd)) -replace ' - .*$', ''
  $r13b = (Results).Fill
  Check SF13 "True|login|0|u=$UserVal|q=|p=$PwVal|1" ("$($old -ne $new -and $old -ne 0)|$t13a|$r13a|$t13b|$r13b") "stale token ignored after lock/unlock (no result), current token types (R60-3)"

  # ---- SF14 one iframe: linking works (control); 33 iframes: refused because the check is incomplete
  OpenPage "/frame1"
  Inject 0
  $c14 = TryLink "fuser" "FrameCtl"; $b14c = Boundary
  OpenPage "/frames"
  Inject 0
  $r14 = TryLink "fuser" "FrameMany"; $b14 = Boundary
  $ctlDocs = if ($b14c -match 'docs=(-?\d+)') { [int]$Matches[1] } else { -1 }
  $ctlReason = if ($b14c -match 'reason=(\d+)') { $Matches[1] } else { "?" }
  $manyPart = $b14 -replace ' excl=\d', ''
  Check SF14 "link=1 draft=1 docs>=1:True reason=0|link=13 draft=0 docs=33 reason=1" "$c14 docs>=1:$($ctlDocs -ge 1) reason=$ctlReason|$r14 $manyPart" "control page links ($b14c); 33 nested documents -> incomplete -> refused, draft empty (R60-2, C61-3)"

  # ---- SF16 failure injection on the one-iframe page
  OpenPage "/frame1"
  $g16 = @()
  foreach ($mode in 1, 2, 3) { Inject $mode; $r = TryLink "fuser" "Inject$mode"; $bd = Boundary; $g16 += "m${mode}:$r " + ($bd -replace '^docs=-?\d+ ', '') }
  Inject 0
  Check SF16 "m1:link=13 draft=0 reason=2 excl=0|m2:link=13 draft=0 reason=3 excl=0|m3:link=13 draft=0 reason=0 excl=1" ($g16 -join '|') "nested enumeration / nested read / comparison failure -> linking refused, draft empty (R60-2)"
  [void](Invoke "back"); Start-Sleep -Milliseconds 1500   # -> /login
  $script:edgeWnd = EdgeWindow

  # ---- SF15 the 15 s limit stops typing in progress (one field linked on /other)
  $long = "dummy-" + ("x" * 60)
  Click 220; Click 212
  SetText 401 "500"; Click 404 900; Click 240 900       # key delay 500 ms (maximum), saved -> settings -> back to the list
  $kd = (Results).KeyDelay
  [void](Invoke "go"); Start-Sleep -Milliseconds 1500      # /login -> /other
  $script:edgeWnd = EdgeWindow
  [void](LinkItem "SiteLong" $long "q")
  [void](Invoke "go"); Start-Sleep -Milliseconds 1500; [void](Invoke "go"); Start-Sleep -Milliseconds 1500   # /other -> /login -> fresh /other
  $script:edgeWnd = EdgeWindow
  [void][SU]::Front($script:edgeWnd); $vis15 = WaitFor { FillVisible } 10000
  $hits15 = (Site) -replace '^.*hits=(\d+).*$', '$1'
  $ctl = Run15 0                                            # negative control: a blocked start
  $ctlJudged = Judge15 $ctl $long
  [void][SU]::Front($script:edgeWnd); [void](WaitFor { FillVisible } 8000)
  $run = Run15 (Token)
  $judged = Judge15 $run $long
  Check SF15 "500|True|1|False|True" "$kd|$vis15|$hits15|$ctlJudged|$judged" ("15 s limit: started=$($run.Started) result=$($run.Fill) (2=timeout) after $($run.Ms) ms, typed $(if ($null -ne $run.Q) { $run.Q.Length } else { 'n/a' })/$($long.Length) exact prefix, nothing after; control (token 0) started=$($ctl.Started) (C61-2)")

  # ---- SF17 afterwards a new fill with a normal key delay types the whole value
  Click 220; Click 212
  SetText 401 "5"; Click 404 900; Click 240 900
  [void](Invoke "go"); Start-Sleep -Milliseconds 1500; [void](Invoke "go"); Start-Sleep -Milliseconds 1500   # fresh /other
  $script:edgeWnd = EdgeWindow
  [void][SU]::Front($script:edgeWnd)
  # wait for the /other page's own [Fill] (one linked field there, hits=1) - not a /login button still up from the page before
  # (0.3.109 run: the token of the /login button was used on /other -> "0/2 fields, the page address changed")
  $pre17 = "$([SU]::Title($script:edgeWnd) -replace ' - .*$', '') $(Site)"
  [void](WaitFor { (FillVisible) -and ((Site) -match 'hits=1$') -and ([SU]::Title($script:edgeWnd) -notlike 'u=*') } 10000)
  $at17 = "$([SU]::Title($script:edgeWnd) -replace ' - .*$', '') $(Site)"
  [void](S2 4)
  [void][SU]::PostMessageW($m, 0x8011, [IntPtr](Token), [IntPtr]::Zero)
  [void](WaitFor { (QOf ([SU]::Title($script:edgeWnd))) -eq $long } 10000)
  [void](WaitFor { (Running) -eq 0 } 5000)
  $got17 = ("$((QOf ([SU]::Title($script:edgeWnd))) -eq $long)|$((Results).Fill)")
  $q17 = QOf ([SU]::Title($script:edgeWnd)); Start-Sleep -Milliseconds 300
  $why17 = ""
  if ($got17 -ne "True|1") {   # diagnosis only: the recent-fill log (advanced > recent fill results; no values in it)
    Click 220; Click 212; Click 215 900
    $b17 = @([SU]::All([uint32]$p.Id, "OneKeyDialog")); if ($b17.Count -gt 0) { $why17 = [SU]::ChildTexts($b17[0]); [void][SU]::PostMessageW($b17[0], 0x0111, [IntPtr]1, [IntPtr]::Zero); Start-Sleep -Milliseconds 400 }
    Click 405 600; Click 240 600
  }
  Check SF17 "True|1" $got17 "after the timeout a new fill types the whole value and reports success (C61-1) [typed $(if ($null -ne $q17) { $q17.Length } else { 'n/a' })/$($long.Length); before wait: $pre17; at click: $at17$(if ($why17) { "; log: $why17" })]"

  # ---- SF18 lock + expired limit at three points: always "stopped", locked, nothing typed
  [void](Invoke "go"); Start-Sleep -Milliseconds 1500      # /other -> fresh /login (user + password linked)
  $script:edgeWnd = EdgeWindow
  $g18 = @()
  foreach ($stage in 1, 2, 3) {
    [void][SU]::Front($script:edgeWnd); $vis = WaitFor { FillVisible } 10000
    [void](S2 4); [void](S2 5 $stage)
    [void][SU]::PostMessageW($m, 0x8011, [IntPtr](Token), [IntPtr]::Zero)
    [void](WaitFor { (Results).Fill -ne 0 } 8000)
    Start-Sleep -Milliseconds 800
    $f = (Results).Fill
    $lk = (Site) -like "links=0 watch=0*"
    $t = ([SU]::Title($script:edgeWnd)) -replace '^(\S+).*$', '$1'   # the page title only: Edge adds the browser name and, with more tabs, "and N more pages" (localised)
    $g18 += "s${stage}:vis=$vis fill=$f locked=$lk title=$t"
    SetText 101 "Master1234"; Click 103 1500                # unlock for the next round
  }
  Check SF18 "s1:vis=True fill=3 locked=True title=login|s2:vis=True fill=3 locked=True title=login|s3:vis=True fill=3 locked=True title=login" ($g18 -join '|') "lock + time limit together: stopped (3) at every point, locked, page untouched (C62-2)"

  # ---- SF19 needs elevation: its own result, the offer appears, nothing typed
  [void][SU]::Front($script:edgeWnd); $vis19 = WaitFor { FillVisible } 10000
  [void](S2 4); [void](S2 5 4)
  [void][SU]::PostMessageW($m, 0x8011, [IntPtr](Token), [IntPtr]::Zero)
  $box19 = WaitFor { (Box) -ne [IntPtr]::Zero } 8000
  $rec19 = WaitFor { (Results).Fill -ne 0 -and (Running) -eq 0 } 8000   # the result is written when the fill thread ends (Codex 19:07)
  $f19 = (Results).Fill
  if ($box19) { Answer (Box) 7 }                            # the elevation offer: [No]
  $t19 = ([SU]::Title($script:edgeWnd)) -replace '^(\S+).*$', '$1'
  [void](S2 5 0)                                            # clear the one-shot injection if the fill never started (else the next fill gets it)
  Check SF19 "True|True|True|5|login" "$vis19|$box19|$rec19|$f19|$t19" "needs elevation: result 5 (not success), the offer appears, nothing typed (C62-1)"

  # ---- SF20 a late old check does not clear the new check's pending mark
  [void][SU]::Front($script:edgeWnd); [void](WaitFor { FillVisible } 10000)
  $k0 = (Order).Stale
  [void](S2 6 8000)                                         # the next page check takes 8 s longer (once)
  Start-Sleep -Milliseconds 3500                            # it is running now
  [void][SU]::PostMessageW($m, 0x02B1, [IntPtr]7, [IntPtr]::Zero); Start-Sleep -Milliseconds 800   # lock: watch off, new generation
  SetText 101 "Master1234"; Click 103 1500                  # unlock: the watch posts a new check while the old one still runs
  [void][SU]::Front($script:edgeWnd)
  $kept = WaitFor { (Order).Stale -gt $k0 } 15000
  $back20 = WaitFor { FillVisible } 12000
  $p20 = (Order).Probes; $more20 = WaitFor { ((Order).Probes - $p20) -ge 2 } 12000
  Check SF20 "True|True|True" "$kept|$back20|$more20" "a late old check kept the new pending mark ($((Order).Stale - $k0) time(s)); the button came back and checks continue (R60-1)"
  [void](S2 7 0)                                            # page checks at normal speed from here

  # slow typing for SF21-SF24
  Click 220; Click 212
  SetText 401 "500"; Click 404 900; Click 240 900

  # ---- SF21 a lock in the middle of slow typing
  ToPage "other"; [void][SU]::Front($script:edgeWnd); [void](WaitFor { FillVisible } 8000)
  $st21 = StartFill
  Start-Sleep -Milliseconds 3000
  [void][SU]::PostMessageW($m, 0x02B1, [IntPtr]7, [IntPtr]::Zero)
  $end21 = WaitFor { (Running) -eq 0 } 4000
  Start-Sleep -Milliseconds 300
  $r21 = (Results).Fill
  $q21 = QOf ([SU]::Title($script:edgeWnd)); Start-Sleep -Milliseconds 3000; $q21b = QOf ([SU]::Title($script:edgeWnd))
  $nob21 = -not (FillVisible); $lk21 = (Site) -like "links=0 watch=0*"
  Check SF21 "True|True|3|True|True|True|True" "$st21|$end21|$r21|$(Prefix $q21 $long)|$($q21 -eq $q21b)|$nob21|$lk21" "lock during typing: stopped (3), locked, typed $(if ($q21) { $q21.Length } else { 0 })/$($long.Length) exact prefix, nothing after, no button"
  SetText 101 "Master1234"; Click 103 1500

  # ---- SF22 the page focus moves to another field of the same page during typing
  ToPage "other"; [void][SU]::Front($script:edgeWnd); [void](WaitFor { FillVisible } 8000)
  $st22 = StartFill
  Start-Sleep -Milliseconds 2500
  $uf22 = Field "user"; if ($uf22) { $uf22.SetFocus() }
  $end22 = WaitFor { (Running) -eq 0 } 4000
  Start-Sleep -Milliseconds 300
  $r22 = (Results).Fill
  $t22 = [SU]::Title($script:edgeWnd); $q22 = QOf $t22; $u22 = UOf $t22
  Start-Sleep -Milliseconds 3000; $t22b = [SU]::Title($script:edgeWnd)
  Check SF22 "True|True|4|True|True|u=" "$st22|$end22|$r22|$(Prefix $q22 $long)|$($t22 -eq $t22b)|u=$u22" "focus moved to another field: typing stopped (4), $(if ($q22) { $q22.Length } else { 0 }) chars in the linked field, the other field empty, nothing after"

  # ---- SF23 another tab opens during typing
  ToPage "other"; [void][SU]::Front($script:edgeWnd); [void](WaitFor { FillVisible } 8000)
  $st23 = StartFill
  Start-Sleep -Milliseconds 2500
  Start-Process $edge -ArgumentList @("--user-data-dir=`"$profileDir`"", "$base/login?tab=2") | Out-Null
  $end23 = WaitFor { (Running) -eq 0 } 5000
  Start-Sleep -Milliseconds 1500
  $r23 = (Results).Fill
  $new23 = ([SU]::Title($script:edgeWnd)) -replace '^(\S+).*$', '$1'
  [void][SU]::Front($script:edgeWnd); Start-Sleep -Milliseconds 300; CtrlKey 0x57; Start-Sleep -Milliseconds 1200   # Ctrl+W: close the new tab
  $q23 = QOf ([SU]::Title($script:edgeWnd)); Start-Sleep -Milliseconds 2500; $q23b = QOf ([SU]::Title($script:edgeWnd))
  Check SF23 "True|True|4|login|True|True" "$st23|$end23|$r23|$new23|$(Prefix $q23 $long)|$($q23 -eq $q23b)" "a new tab during typing: stopped (4), new tab untouched, the filled field kept $(if ($q23) { $q23.Length } else { 0 }) chars and got nothing more"

  # ---- SF24 the page replaces the field element after 5 characters
  OpenPage "/swap"
  [void](LinkItem "SiteSwap" $long "q")
  OpenPage "/swap"                                           # a fresh copy (new tab)
  [void][SU]::Front($script:edgeWnd); $vis24 = WaitFor { FillVisible } 8000
  $st24 = StartFill
  $end24 = WaitFor { (Running) -eq 0 } 10000
  Start-Sleep -Milliseconds 300
  $r24 = (Results).Fill
  $q24 = QOf ([SU]::Title($script:edgeWnd)); Start-Sleep -Milliseconds 2500; $q24b = QOf ([SU]::Title($script:edgeWnd))
  $len24 = if ($q24) { $q24.Length } else { 0 }
  Check SF24 "True|True|True|4|True|True" "$vis24|$st24|$end24|$r24|$($len24 -ge 5 -and $len24 -le 6 -and $long.StartsWith($q24))|$($q24 -eq $q24b)" "field replaced after 5 chars: stopped (4) with $len24 chars, nothing after"

  # ---- SF25 CPU and memory: 30 s watching, 30 s locked
  Click 220; Click 212
  SetText 401 "5"; Click 404 900; Click 240 900
  [void](Invoke "go"); Start-Sleep -Milliseconds 1500       # /swap -> /login
  $script:edgeWnd = EdgeWindow
  [void][SU]::Front($script:edgeWnd); $vis25 = WaitFor { FillVisible } 8000
  $eids = @(Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" | Where-Object { $_.CommandLine -like "*$profileDir*" } | ForEach-Object { $_.ProcessId })
  $p.Refresh(); $k1 = $p.TotalProcessorTime.TotalSeconds; $pm1 = $p.PrivateMemorySize64; $h1 = $p.HandleCount; $e1 = Usage $eids
  Start-Sleep -Seconds 30
  $p.Refresh(); $k2 = $p.TotalProcessorTime.TotalSeconds; $pm2 = $p.PrivateMemorySize64; $h2 = $p.HandleCount; $e2 = Usage $eids
  $probes25 = (Order).Probes
  [void][SU]::PostMessageW($m, 0x02B1, [IntPtr]7, [IntPtr]::Zero); Start-Sleep -Milliseconds 800
  [void][SU]::Front($script:edgeWnd); Start-Sleep -Milliseconds 500
  $p.Refresh(); $k3 = $p.TotalProcessorTime.TotalSeconds; $e3 = Usage $eids
  Start-Sleep -Seconds 30
  $p.Refresh(); $k4 = $p.TotalProcessorTime.TotalSeconds; $e4 = Usage $eids
  SetText 101 "Master1234"; Click 103 1500
  $kA = [math]::Round(($k2 - $k1) / 30 * 100, 2); $kB = [math]::Round(($k4 - $k3) / 30 * 100, 2)
  $eA = [math]::Round(($e2.Cpu - $e1.Cpu) / 30 * 100, 2); $eB = [math]::Round(($e4.Cpu - $e3.Cpu) / 30 * 100, 2)
  $pmGrow = [math]::Round(($pm2 - $pm1) / 1MB, 2)
  $msg25 = "1Key CPU $kA% watching / $kB% locked, Edge CPU $eA% / $eB% (of one core), 1Key private $([math]::Round($pm1/1MB,1))->$([math]::Round($pm2/1MB,1)) MB, handles $h1->$h2, Edge working set $([math]::Round($e2.Ws/1MB)) MB, checks so far $probes25"
  "SF25 numbers: $msg25"
  Check SF25 "True|True|True|True" "$vis25|$($kA -lt 5)|$(($eA - $eB) -lt 25)|$($pmGrow -lt 5)" $msg25

  # ---- SF26 one item, two inputs, two linked fields
  ToPage "other"
  Click 203; Click 4007; SetText 301 "SiteBoth"; SetText 302 "both-id"; LinkKind; Click 339 900; SetText 331 "both-pw"
  [void](LinkNow 323 "user")                                 # unnamed form: no pair, the chip stays open and asks for the password field
  $l1 = ChipOpen
  $l2 = LinkMore "pw"; Start-Sleep -Milliseconds 500
  $rows26 = (Text 322) + " / " + (Text 341)   # input 2 row = 341 (was 342 by mistake)
  Click 310 1200; Answer (Box) 1
  ToPage "other"
  $uf26 = Field "user"; if ($uf26) { ([Windows.Automation.ValuePattern]$uf26.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)).SetValue("old") }
  [void][SU]::Front($script:edgeWnd); $vis26 = WaitFor { FillVisible } 8000
  [void](S2 4); [void][SU]::PostMessageW($m, 0x8011, [IntPtr](Token), [IntPtr]::Zero)
  [void](WaitFor { [SU]::Title($script:edgeWnd) -like "u=both-id|*|p=both-pw*" } 10000)
  [void](WaitFor { (Running) -eq 0 } 5000)
  $t26 = ([SU]::Title($script:edgeWnd)) -replace '^(\S+).*$', '$1'
  Check SF26 "True|True|True|u=both-id|q=$long|p=both-pw|1" "$l1|$l2|$vis26|$t26|$((Results).Fill)" "one item with two inputs: [Link] on an unnamed form asks for the password field once more (chip open), then one [Fill] types both, old value replaced ($rows26)"

  # ---- SF27 a page that sends programmatic focus back to the password field: [Fill] still fills both
  OpenPage "/nexa"
  Click 203; Click 4007; SetText 301 "SiteNexa"; SetText 302 "nexa-id"; LinkKind; Click 339 900; SetText 331 "nexa-pw"
  [void][SU]::SendMessageW([SU]::GetDlgItem($m, 305), 0x00F1, [IntPtr]1, [IntPtr]::Zero)   # Enter after input: on
  $n1 = LinkNow 323 "nu"; Start-Sleep -Milliseconds 500    # named form: one [Link] links the pair
  $n2 = (-not (ChipOpen)) -and ((Text 341) -like "*np*")
  [void][SU]::SendMessageW([SU]::GetDlgItem($m, 349), 0x00F1, [IntPtr]1, [IntPtr]::Zero)   # Advanced: click allowed (a new item starts with it off)
  Click 310 1200; Answer (Box) 1
  [void](Invoke "arm"); Start-Sleep -Milliseconds 300       # from now on (stored for the site) the user field refuses programmatic focus
  OpenPage "/nexa"                                          # fresh copy: armed, password field focused
  $npf = Field "np"; if ($npf) { $npf.SetFocus() }
  [void][SU]::Front($script:edgeWnd); $vis27 = WaitFor { FillVisible } 8000
  [void](S2 4); [void][SU]::PostMessageW($m, 0x8011, [IntPtr](Token), [IntPtr]::Zero)
  [void](WaitFor { [SU]::Title($script:edgeWnd) -like "u=nexa-id|p=nexa-pw|sent*" } 10000)
  [void](WaitFor { (Running) -eq 0 } 5000)
  $r27 = Results
  $t27 = ([SU]::Title($script:edgeWnd)) -replace '^(\S+).*$', '$1'
  Check SF27 "True|True|True|u=nexa-id|p=nexa-pw|sent|1|True" "$n1|$n2|$vis27|$t27|$($r27.Fill)|$($r27.FocusMethod -ge 2)" "[Fill] on a page that refuses programmatic focus: both fields filled, then Enter as the item says (cursor moved by method $($r27.FocusMethod): 2 default action, 3 click)"

  # ---- SF28 the shortcut on that page fills the linked fields even though the cursor is in the password field
  OpenPage "/nexa"
  [void](Invoke "arm"); Start-Sleep -Milliseconds 300
  $npf = Field "np"; if ($npf) { $npf.SetFocus() }
  [void][SU]::Front($script:edgeWnd); Start-Sleep -Milliseconds 500
  $h0 = (Results).HotkeyLinked
  [void](S2 4); [void][SU]::PostMessageW($m, 0x0312, [IntPtr]5, [IntPtr]::Zero)   # WM_HOTKEY for slot 5 (SiteNexa)
  [void](WaitFor { [SU]::Title($script:edgeWnd) -like "u=nexa-id|p=nexa-pw|sent*" } 12000)
  [void](WaitFor { (Running) -eq 0 } 5000)
  $t28 = ([SU]::Title($script:edgeWnd)) -replace '^(\S+).*$', '$1'
  Check SF28 "u=nexa-id|p=nexa-pw|sent|1|1" "$t28|$((Results).Fill)|$((Results).HotkeyLinked - $h0)" "shortcut on the linked page, cursor in the password field: ID into the ID field, password into the password field, then Enter"

  # ---- SF29 the same shortcut on a page that is not linked: from the cursor, ID -> Tab -> next field (as before)
  [void](Invoke "go"); Start-Sleep -Milliseconds 1500       # /nexa -> /login
  $script:edgeWnd = EdgeWindow
  [void](FocusField "user"); Start-Sleep -Milliseconds 300
  $h1 = (Results).HotkeyLinked
  [void][SU]::PostMessageW($m, 0x0312, [IntPtr]5, [IntPtr]::Zero)
  Start-Sleep -Milliseconds 3000
  $t29 = ([SU]::Title($script:edgeWnd)) -replace '^(\S+).*$', '$1'
  Check SF29 "login|0" "$t29|$((Results).HotkeyLinked - $h1)" "linked item, linked page not shown anywhere: nothing typed into the page in front"

  # ---- SF30 1Key's own window in front; the linked page is open in Edge: Edge comes to the front and is filled
  OpenPage "/nexa"
  [void][SU]::PostMessageW($m, 0x8003, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 800   # show 1Key's window
  [void][SU]::Front($m); Start-Sleep -Milliseconds 300     # and really put it in front (a background process cannot by a message alone)
  $front30 = [SU]::GetForegroundWindow() -eq $m
  $h2 = (Results).HotkeyLinked
  [void][SU]::PostMessageW($m, 0x0312, [IntPtr]5, [IntPtr]::Zero)
  [void](WaitFor { [SU]::Title($script:edgeWnd) -like "u=nexa-id|p=nexa-pw|sent*" } 12000)
  [void](WaitFor { (Running) -eq 0 } 5000)
  $t30 = ([SU]::Title($script:edgeWnd)) -replace '^(\S+).*$', '$1'
  Check SF30 "True|u=nexa-id|p=nexa-pw|sent|True|1" "$front30|$t30|$([SU]::GetForegroundWindow() -eq $script:edgeWnd)|$((Results).HotkeyLinked - $h2)" "linked page open behind 1Key's window: brought to the front and filled (then Enter)"

  # ---- SF31 the list's [Type] button (row 5 = SiteNexa) with the linked page open behind 1Key
  OpenPage "/nexa"
  [void][SU]::PostMessageW($m, 0x8003, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 800
  $h3 = (Results).HotkeyLinked
  Click 1105 300                                            # row [Type]
  $chip31 = @([SU]::All([uint32]$p.Id, "OneKeyChip")).Count
  [void](WaitFor { [SU]::Title($script:edgeWnd) -like "u=nexa-id|p=nexa-pw|sent*" } 12000)
  [void](WaitFor { (Running) -eq 0 } 5000)
  $t31 = ([SU]::Title($script:edgeWnd)) -replace '^(\S+).*$', '$1'
  Check SF31 "0|u=nexa-id|p=nexa-pw|sent|1" "$chip31|$t31|$((Results).HotkeyLinked - $h3)" "list [Type] of a linked item: no chip; the linked page is found and filled (then Enter)"

  # ================= R75 dedicated failure injection (0.2.88)
  function CloseTab() { [void][SU]::Front($script:edgeWnd); Start-Sleep -Milliseconds 300; CtrlKey 0x57; Start-Sleep -Milliseconds 900; $script:edgeWnd = EdgeWindow }
  # one [Fill] of SiteNexa on a fresh /nexa tab, stopped at $point, $ev applied there
  function R75([string]$point, [string]$ev) {
    OpenPage "/nexa"
    [void][SU]::Front($script:edgeWnd); [void](WaitFor { FillVisible } 8000)
    $h = Arm $point
    [void](StartFill)
    $ok = WaitReached $h
    $t0 = StableTitle; $e0 = Ev
    $made = $false
    if ($ok) {
      switch ($ev) {
        "lock"   { [void][SU]::PostMessageW($m, 0x02B1, [IntPtr]7, [IntPtr]::Zero); $made = WaitFor { (Site) -like "links=0 watch=0*" } 3000 }
        "expire" { $made = ((((S2 10 1) -shr 60) -band 1) -eq 1) }
        "window" { [void][SU]::Front([SU]::GetShellWindow()); $made = WaitFor { [SU]::GetForegroundWindow() -ne $script:edgeWnd } 3000 }
        "field"  { $id = $(if ($point -like "enter.*") { "sb" } else { "np" }); $o = Field $id; if ($o) { $o.SetFocus() }
                   $made = WaitFor { try { [Windows.Automation.AutomationElement]::FocusedElement.Current.AutomationId -eq $id } catch { $false } } 3000 }
      }
    }
    Release $h
    $end = WaitFor { (Running) -eq 0 } 12000
    Start-Sleep -Milliseconds 300
    $r = (Results).Fill
    Start-Sleep -Milliseconds 1500
    $t1 = StableTitle; $e1 = Ev
    $lk = (Site) -like "links=0 watch=0*"
    [void](S2 10 0)
    if ($ev -eq "lock") { SetText 101 "Master1234"; Click 103 1500 }
    CloseTab
    "${ev}:made=$made/ok=$ok/end=$end/r=$r/" + (EvDelta $e0 $e1 "bi","inp","tab","ent","ca","sub") + "/same=$($t1 -eq $t0)/lk=$lk"
  }
  $z = "bi=0 inp=0 tab=0 ent=0 ca=0 sub=0"
  $want3 = "lock:made=True/ok=True/end=True/r=3/$z/same=True/lk=True|expire:made=True/ok=True/end=True/r=2/$z/same=True/lk=False|window:made=True/ok=True/end=True/r=4/$z/same=True/lk=False"
  $want75 = "$want3|field:made=True/ok=True/end=True/r=4/$z/same=True/lk=False"
  $n = 32
  foreach ($pt in "selall.mods", "selall.guard-in", "selall.guard-out", "send.guard-in", "send.guard-out", "enter.mods", "enter.guard-in", "enter.guard-out") {
    $g = @(); foreach ($ev in "lock", "expire", "window") { $g += R75 $pt $ev }
    $f = R75 $pt "field"
    if ($pt -like "*.guard-out") {
      Check ("SF{0:D2}" -f $n) $want3 ($g -join '|') "R75-2 barrier $pt (made/reached/ended/result/events after the barrier/values same/locked) per event"
      "NOTE [SF{0:D2}-field] residual race after the last UIA check (not judged): $f" -f $n
    } else {
      Check ("SF{0:D2}" -f $n) $want75 (($g + $f) -join '|') "R75-2 barrier $pt (made/reached/ended/result/events after the barrier/values same/locked) per event"
    }
    $n++
  }

  # R75-3: real click path (test hook skips SetFocus and the default action), barrier before the hit test
  function R75c([string]$ev) {
    OpenPage "/nexa"
    [void](Invoke "rec"); Start-Sleep -Milliseconds 200
    $npf = Field "np"; if ($npf) { $npf.SetFocus() }
    [void][SU]::Front($script:edgeWnd); [void](WaitFor { FillVisible } 8000)
    Inject 4
    $h = Arm "click.mods"
    [void](StartFill)
    $ok = WaitReached $h
    $e0 = Ev; $t0 = StableTitle
    $made = $true
    $nu0 = Field "nu"; $rc0 = $nu0.Current.BoundingRectangle; $pt0 = New-Object System.Windows.Point(($rc0.X + $rc0.Width / 2), ($rc0.Y + $rc0.Height / 2))
    $rid0 = ($nu0.GetRuntimeId() -join '.')
    function AtCentre() { try { [Windows.Automation.AutomationElement]::FromPoint($pt0).Current.AutomationId } catch { "" } }
    switch ($ev) {
      "move"    { [void](Invoke "mv"); $made = WaitFor { (Ev).mv -ge 1 -and (AtCentre) -eq "decoy" } 3000 }
      "cover"   { [void](Invoke "cv"); $made = WaitFor { (Ev).cv -ge 1 -and (AtCentre) -eq "cover" } 3000 }
      "replace" { [void](Invoke "rp"); $made = WaitFor { (Ev).rp -ge 1 -and ($n2 = Field "nu") -and (($n2.GetRuntimeId() -join '.') -ne $rid0) } 3000 }
      "lock"    { [void][SU]::PostMessageW($m, 0x02B1, [IntPtr]7, [IntPtr]::Zero); $made = WaitFor { (Site) -like "links=0 watch=0*" } 3000 }
    }
    $e0 = Ev                                                   # the change itself (Invoke) is not 1Key's doing
    Release $h
    $end = WaitFor { (Running) -eq 0 } 12000
    Start-Sleep -Milliseconds 1500
    Inject 0
    $r = (Results).Fill
    $t = StableTitle; $e1 = Ev
    $lk = (Site) -like "links=0 watch=0*"
    if ($ev -eq "lock") { SetText 101 "Master1234"; Click 103 1500 }
    CloseTab
    [pscustomobject]@{ Ok = $ok; Made = $made; End = $end; Fill = $r; Keys = (EvDelta $e0 $e1 "bi","inp","tab","ent","ca","sub"); Md = $e1.md - $e0.md; Decoy = $e1.dec - $e0.dec; Cover = $e1.cov - $e0.cov
                       Typed = ($t -like 'u=nexa-id*'); Same = ($t -eq $t0); Locked = $lk; Title = ($t -replace '^(\S+).*$', '$1') }
  }
  $c = R75c "none"
  Check SF40 "True|True|1|1|True|True" "$($c.Ok)|$($c.End)|$($c.Fill)|$($c.Md)|$($c.Typed)|$($c.Keys -notlike '*sub=0*')" "R75-3 control: the field pressed once, both inputs typed, then submitted ($($c.Keys); $($c.Title))"
  $c = R75c "move"
  Check SF41 "True|True|True|0" "$($c.Ok)|$($c.Made)|$($c.End)|$($c.Decoy)" "R75-3 field moved, decoy at its old place: decoy never pressed (result $($c.Fill), field presses $($c.Md), $($c.Keys))"
  $c = R75c "cover"
  Check SF42 "True|True|True|0|0|$z|True" "$($c.Ok)|$($c.Made)|$($c.End)|$($c.Cover)|$($c.Md)|$($c.Keys)|$($c.Same)" "R75-3 field covered: no press on the cover or the field, no input event, values unchanged (result $($c.Fill))"
  $c = R75c "replace"
  Check SF43 "True|True|True|0|$z|True" "$($c.Ok)|$($c.Made)|$($c.End)|$($c.Md)|$($c.Keys)|$($c.Same)" "R75-3 field replaced by a new element: no press, no input event, values unchanged (result $($c.Fill))"
  $c = R75c "lock"
  Check SF44 "True|True|True|0|$z|True|3|True" "$($c.Ok)|$($c.Made)|$($c.End)|$($c.Md)|$($c.Keys)|$($c.Same)|$($c.Fill)|$($c.Locked)" "R75-3 1Key locked at the barrier: no press, no input event, stopped, locked"

  # SF45: the counters do see what they must not miss (done on purpose by the harness)
  OpenPage "/nexa"
  [void][SU]::Front($script:edgeWnd); Start-Sleep -Milliseconds 300
  $npf = Field "np"; if ($npf) { $npf.SetFocus() }; Start-Sleep -Milliseconds 300
  $s0 = Ev
  [SU]::keybd_event(0x58, 0, 0, [UIntPtr]::Zero); [SU]::keybd_event(0x58, 0, 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 200   # x
  CtrlKey 0x41; Start-Sleep -Milliseconds 200                                                                                        # Ctrl+A
  [SU]::keybd_event(0x0D, 0, 0, [UIntPtr]::Zero); [SU]::keybd_event(0x0D, 0, 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 300   # Enter (submits)
  $npf = Field "np"; if ($npf) { $npf.SetFocus() }; Start-Sleep -Milliseconds 200
  [SU]::keybd_event(0x09, 0, 0, [UIntPtr]::Zero); [SU]::keybd_event(0x09, 0, 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 200   # Tab
  [void](Invoke "mv"); Start-Sleep -Milliseconds 300
  $dc = Field "decoy"
  if ($dc) { $rc = $dc.Current.BoundingRectangle; [void][SU]::SetCursorPos([int]($rc.X + $rc.Width / 2), [int]($rc.Y + $rc.Height / 2)); Start-Sleep -Milliseconds 100
             [SU]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [SU]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero) }
  Start-Sleep -Milliseconds 500
  $s1 = Ev
  $sens = ($s1.bi -gt $s0.bi) -and ($s1.inp -gt $s0.inp) -and ($s1.ca -gt $s0.ca) -and ($s1.ent -gt $s0.ent) -and ($s1.sub -gt $s0.sub) -and ($s1.tab -gt $s0.tab) -and ($s1.dec -gt $s0.dec)
  CloseTab
  # SF46/SF47: the switch off on SiteNexa (slot 5)
  function AllowClick([int]$slot, $on) { [void][SU]::PostMessageW($m, 0x8003, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 600; Click (1000 + $slot) 900
    [void][SU]::SendMessageW([SU]::GetDlgItem($m, 349), 0x00F1, [IntPtr]$(if ($on) { 1 } else { 0 }), [IntPtr]::Zero); Start-Sleep -Milliseconds 200; Click 310 1200; Answer (Box) 1 }
  # one [Fill] on /nexa; $tokenZero sends token 0 instead (a request that must not count as anything)
  function NoClickRun([int]$inject, [bool]$tokenZero = $false) {
    OpenPage "/nexa"
    $npf = Field "np"; if ($npf) { $npf.SetFocus() }
    [void][SU]::Front($script:edgeWnd); $vis = WaitFor { FillVisible } 8000
    $tok = Token
    Inject $inject
    $e0 = Ev
    [void](S2 4)                                               # this request's result starts at 0
    [void][SU]::PostMessageW($m, 0x8011, [IntPtr]$(if ($tokenZero) { 0 } else { $tok }), [IntPtr]::Zero)
    $done = WaitFor { (Results).Fill -ne 0 -and (Running) -eq 0 } 12000   # finished with a non-zero result of this request
    Start-Sleep -Milliseconds 1200
    Inject 0
    $r = Results; $e1 = Ev; $t = (StableTitle) -replace '^(\S+).*$', '$1'
    CloseTab
    [pscustomobject]@{ Valid = ($vis -and $tok -ne 0 -and $done); Fill = $r.Fill; Method = $r.FocusMethod; Keys = (EvDelta $e0 $e1 "bi","inp","tab","ent","ca","sub")
                       Md = $e1.md - $e0.md; Exact = ($t -eq "u=nexa-id|p=nexa-pw|sent"); Ent = $e1.ent - $e0.ent; Sub = $e1.sub - $e0.sub; Title = $t }
  }
  function Judge46($c) { [bool]($c.Valid -and $c.Method -ne 3 -and (($c.Fill -eq 1 -and $c.Method -in 1, 2 -and $c.Exact -and $c.Ent -eq 1 -and $c.Sub -eq 1) -or ($c.Fill -eq 4 -and $c.Method -eq 0 -and $c.Keys -match '^bi=0 inp=0 tab=[01] ent=0 ca=0 sub=0$'))) }   # 0.2.109: one Shift+Tab with an arrival check when the cursor is in the next linked field; nothing typed
  function Judge47($c) { [bool]($c.Valid -and $c.Fill -eq 4 -and $c.Method -eq 0 -and $c.Md -eq 0 -and $c.Keys -match '^bi=0 inp=0 tab=[01] ent=0 ca=0 sub=0$') }   # as SF46: one arrival-checked Shift+Tab allowed
  AllowClick 5 $false
  $c46 = NoClickRun 0
  $c46z = NoClickRun 0 $true                                   # token 0: must fail the same judgement
  Check SF46 "True|False" "$(Judge46 $c46)|$(Judge46 $c46z)" "switch off, /nexa as it is: result $($c46.Fill), method $($c46.Method), title $($c46.Title), $($c46.Keys); token-0 run judged as fail"
  $c47 = NoClickRun 4
  $c47z = NoClickRun 4 $true
  Check SF47 "True|False" "$(Judge47 $c47)|$(Judge47 $c47z)" "switch off, methods 1-2 skipped: result $($c47.Fill), method $($c47.Method), presses $($c47.Md), $($c47.Keys); token-0 run judged as fail"
  AllowClick 5 $true
  # SF48: required normal control with the switch off where SetFocus works (SiteBoth, slot 4, on /other; SiteLong fills q there too)
  AllowClick 4 $false
  ToPage "other"
  $uf48 = Field "user"; if ($uf48) { ([Windows.Automation.ValuePattern]$uf48.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)).SetValue("old") }
  [void][SU]::Front($script:edgeWnd); $vis48 = WaitFor { FillVisible } 8000; $tok48 = Token
  [void](S2 4); [void][SU]::PostMessageW($m, 0x8011, [IntPtr]$tok48, [IntPtr]::Zero)
  $done48 = WaitFor { (Results).Fill -ne 0 -and (Running) -eq 0 } 12000
  Start-Sleep -Milliseconds 800
  $r48 = Results; $t48 = (StableTitle) -replace '^(\S+).*$', '$1'
  # ---- SF49 per-input mode (Advanced > Link each input separately): [Link] on input 1 and input 2 as before
  ToPage "other"
  Click 203; Click 4007; SetText 301 "PerInput"; SetText 302 "pi-id"; LinkKind; Click 339 900; SetText 331 "pi-pw"
  [void][SU]::SendMessageW([SU]::GetDlgItem($m, 353), 0x00F1, [IntPtr]1, [IntPtr]::Zero)
  [void][SU]::PostMessageW($m, 0x0111, [IntPtr]353, [IntPtr]::Zero); Start-Sleep -Milliseconds 900   # BN_CLICKED: the screen is rebuilt with a [Link] per input
  $b49 = [SU]::GetDlgItem($m, 345) -ne [IntPtr]::Zero
  $p1 = LinkNow 323 "user"; Start-Sleep -Milliseconds 400
  $p2 = LinkNow 345 "pw"; Start-Sleep -Milliseconds 400
  $rows49 = (Text 322) + " / " + (Text 341)
  Check SF49 "True|True|True|True|True" "$b49|$p1|$p2|$((Text 322) -like '*user*')|$((Text 341) -like '*pw*')" "per-input mode: a [Link] per input, each links its own field ($rows49)"
  Click 311 800; Answer (Box) 6

  # ---- SF50 ambiguous pair: a named form with one text field and two password fields -> no pair, the chip stays open
  OpenPage "/two"
  Click 203; Click 4007; SetText 301 "TwoPw"; SetText 302 "t-id"; LinkKind; Click 339 900; SetText 331 "t-pw"
  [void](LinkNow 323 "tu")
  $open50 = ChipOpen
  CloseChip
  $rows50 = (Text 322) + " / " + (Text 341)
  Check SF50 "True|True|False" "$open50|$((Text 322) -like '*tu*')|$((Text 341) -like '*tp*')" "two password fields in one form: input 1 linked, no automatic password field, the chip asked for it ($rows50)"
  Click 311 800; Answer (Box) 6

  # ---- SF51 a new linked item starts with the click switch off
  Click 203; Click 4001; LinkKind
  $c51 = [SU]::SendMessageW([SU]::GetDlgItem($m, 349), 0x00F0, [IntPtr]::Zero, [IntPtr]::Zero)   # BM_GETCHECK
  Check SF51 "0" "$c51" "new linked item: Advanced > click the field directly is off"
  Click 311 800; Answer (Box) 6

  # ---- SF52-SF59 MainFrame start contrasts on /mf (one item, pair-linked; click off; no Enter)
  OpenPage "/mf"
  Click 203; Click 4007; SetText 301 "SiteMf"; SetText 302 "mf-id"; LinkKind; Click 339 900; SetText 331 "mf-pw"
  [void][SU]::SendMessageW([SU]::GetDlgItem($m, 305), 0x00F1, [IntPtr]1, [IntPtr]::Zero)   # Enter after input: on (Enter is measured by the page)
  $mfl1 = LinkNow 323 "mainframe.VFS.CF.form.edtUserId:input"; Start-Sleep -Milliseconds 1500
  $mfl2 = (-not (ChipOpen)) -and ((Text 341) -like "*edtPassword*")
  Click 310 1200; Answer (Box) 1
  $r52 = MfRun
  Check SF52 "True|True|True|u=mf-id|p=mf-pw|2|True|0|1|0" "$mfl1|$mfl2|$($r52.Vis)|$($r52.Title)|$($r52.St)|$($r52.Bounce -ge 1)|$($r52.Reason)|$($r52.Ent)|$($r52.Sp)" "MainFrame focused, user field refuses programmatic focus: Shift+Tab twice (via the password field), both filled (fill $($r52.Fill))"
  $r53 = MfRun @("dlgb")
  Check SF53 "mf|0|8|0|0" "$($r53.Title)|$($r53.St)|$($r53.Reason)|$($r53.Ent)|$($r53.Sp)" "a dialog that does not cover the user field: no Shift+Tab, nothing typed, reason 8 (fill $($r53.Fill))"
  $r54 = MfRun @("popb")
  Check SF54 "mf|0|8|0|0" "$($r54.Title)|$($r54.St)|$($r54.Reason)|$($r54.Ent)|$($r54.Sp)" "a visible field of another Nexacro frame (popup form): no Shift+Tab, nothing typed, reason 8 (fill $($r54.Fill))"
  $r55 = MfRun -hook 5
  Check SF55 "mf|0|7|0|0" "$($r55.Title)|$($r55.St)|$($r55.Reason)|$($r55.Ent)|$($r55.Sp)" "the linked field's own identity cannot be read (hook 5): no Shift+Tab, nothing typed, reason 7 (fill $($r55.Fill))"
  $r56 = MfRun -hook 6
  Check SF56 "mf|0|8|0|0" "$($r56.Title)|$($r56.St)|$($r56.Reason)|$($r56.Ent)|$($r56.Sp)" "the dialog check cannot be made (hook 6): no Shift+Tab, nothing typed, reason 8 (fill $($r56.Fill))"
  $r57 = MfRun -focusId "og"
  Check SF57 "mf|0|4|0|0" "$($r57.Title)|$($r57.St)|$($r57.Reason)|$($r57.Ent)|$($r57.Sp)" "focus on another group, not MainFrame: no Shift+Tab, nothing typed, reason 4 (fill $($r57.Fill))"
  $r58 = MfRun @("stuckb")
  Check SF58 "mf|1|0|0" "$($r58.Title)|$($r58.St)|$($r58.Ent)|$($r58.Sp)" "the first Shift+Tab leaves the focus on MainFrame: no second key, nothing typed (reason $($r58.Reason), fill $($r58.Fill))"
  $r59 = MfRun @("sideb")
  $r60 = MfRun @("alrb")
  Check SF60 "mf|0|8|0|0" "$($r60.Title)|$($r60.St)|$($r60.Reason)|$($r60.Ent)|$($r60.Sp)" "a child frame of the login frame without inputs (like the Nexacro site's alert) is shown: no Shift+Tab, nothing typed, reason 8 (fill $($r60.Fill))"
  Check SF59 "mf|1|0|0" "$($r59.Title)|$($r59.St)|$($r59.Ent)|$($r59.Sp)" "the first Shift+Tab lands on another element: no second key, nothing typed (reason $($r59.Reason), fill $($r59.Fill))"

  # ---- SF61 browser alert() open, focus on MainFrame (Codex 09:45-be: a NEW request must be handled - its own probe count and
  # result -, the product's own key inputs are counted (the harness Enter that closes the alert is not one of them), late input
  # after the alert is closed is looked for, and a control run (an item without links: no request) must fail the same judgement)
  function Obs12() { $v = S2 12; [pscustomobject]@{ Inputs = [int]($v -band [int64]4294967295); Probed = [int](($v -shr 32) -band 0xFFFF); Notice = [int](($v -shr 48) -band 0xF) } }
  function AlertRun([int]$slot) {
    OpenPage "/mf"
    [void](Invoke "armf"); Start-Sleep -Milliseconds 200
    $f = Field "mainframe"; if ($f) { $f.SetFocus() }
    [void][SU]::Front($script:edgeWnd); $vis = WaitFor { FillVisible } 8000
    [void](Invoke "jsab"); Start-Sleep -Milliseconds 1500                        # alert opens 0.3 s later
    Inject 0; [void](S2 4)
    $o0 = Obs12; $hk0 = (Results).HotkeyLinked
    $toast0 = [int64][SU]::SendMessageW($m, 0x800A, [IntPtr]::Zero, [IntPtr]::Zero)   # WM_TEST_TOAST_COUNT
    [void][SU]::PostMessageW($m, 0x0312, [IntPtr]$slot, [IntPtr]::Zero)
    [void](WaitFor { (Obs12).Probed -gt $o0.Probed -and (Running) -eq 0 } 10000)
    Start-Sleep -Milliseconds 800
    $o1 = Obs12; $fill = (Results).Fill; $runs = (Results).HotkeyLinked - $hk0
    $toast1 = [int64][SU]::SendMessageW($m, 0x800A, [IntPtr]::Zero, [IntPtr]::Zero)
    $obs = S2 3; $reason = (($obs -shr 24) -band 0xF) - 1
    [void][SU]::Front($script:edgeWnd); Start-Sleep -Milliseconds 300
    [SU]::keybd_event(0x0D, 0, 0, [UIntPtr]::Zero); [SU]::keybd_event(0x0D, 0, 2, [UIntPtr]::Zero)   # close the alert (harness key, not counted)
    Start-Sleep -Milliseconds 2500                                               # late input after the alert is gone
    $o2 = Obs12; $ev = Ev; $t = ([SU]::Title($script:edgeWnd)) -replace '^(\S+).*$', '$1'
    CloseTab
    $newReq = $o1.Probed - $o0.Probed
    $handled = if ($newReq -ne 1) { "no-new-request($newReq)" }
               elseif ($fill -ne 0) { "fill:mainframe-blocked=$($reason -ne 0)" }
               elseif ($o1.Notice -in 2, 4, 5 -and $runs -eq 0 -and $toast1 -gt $toast0) { "notice:$(@{2='fields-not-found';4='many-pages';5='page-not-open'}[$o1.Notice])" }
               else { "other(notice=$($o1.Notice) runs=$runs toast+$($toast1 - $toast0))" }
    [pscustomobject]@{ Vis = $vis; Handled = $handled; InAlert = $o1.Inputs - $o0.Inputs; Late = $o2.Inputs - $o1.Inputs; Title = $t; St = [int]$ev['st']; Sp = [int]$ev['sp'] }
  }
  function Judge61($r) { [bool]($r.Handled -in 'fill:mainframe-blocked=True', 'notice:fields-not-found', 'notice:many-pages', 'notice:page-not-open' -and $r.InAlert -eq 0 -and $r.Late -eq 0 -and $r.Title -eq 'mf' -and $r.St -eq 0 -and $r.Sp -eq 0) }
  $r61 = AlertRun 6                                                             # slot 6 = SiteMf (SiteUser 0, SitePw 1, SiteLong 2, SiteSwap 3, SiteBoth 4, SiteNexa 5)
  $r61z = AlertRun 40                                                           # control: an empty slot, no request is made
  Check SF61 "True|True|False" "$($r61.Vis)|$(Judge61 $r61)|$(Judge61 $r61z)" "browser alert open, focus on MainFrame, the item's shortcut: a new request handled without MainFrame keys ($($r61.Handled)), product inputs during the alert $($r61.InAlert) / after it closed $($r61.Late), page st $($r61.St) sp $($r61.Sp), title $($r61.Title); control with an empty slot judged as fail ($($r61z.Handled))"

  # ---- SF62-SF65 barrier after the first Shift+Tab
  function MfBarrier([string]$evName) {
    OpenPage "/mf"
    [void](Invoke "armf"); Start-Sleep -Milliseconds 200
    $f = Field "mainframe"; if ($f) { $f.SetFocus() }
    [void][SU]::Front($script:edgeWnd); [void](WaitFor { FillVisible } 8000)
    Inject 0
    $h = Arm "mf.second"
    [void](StartFill)
    $ok = WaitReached $h
    $e0 = Ev; $made = $false
    if ($ok) {
      switch ($evName) {
        "lock"   { [void][SU]::PostMessageW($m, 0x02B1, [IntPtr]7, [IntPtr]::Zero); $made = WaitFor { (Site) -like "links=0 watch=0*" } 3000 }
        "expire" { $made = ((((S2 10 1) -shr 60) -band 1) -eq 1) }
        "window" { [void][SU]::Front([SU]::GetShellWindow()); $made = WaitFor { [SU]::GetForegroundWindow() -ne $script:edgeWnd } 3000 }
        "page"   { [void](Invoke "gol"); $made = WaitFor { ([SU]::Title($script:edgeWnd)) -like "login*" } 4000 }
      }
    }
    Release $h
    $end = WaitFor { (Running) -eq 0 } 12000
    Start-Sleep -Milliseconds 1200
    [void](S2 10 0)
    $shiftUp = ([SU]::GetAsyncKeyState(0x10) -band 0x8000) -eq 0
    $t = ([SU]::Title($script:edgeWnd)) -replace '^(\S+).*$', '$1'
    $after = if ($evName -eq "page") { "page" } else { $e1 = Ev; "st+$([int]$e1['st'] - [int]$e0['st'])/ent$([int]$e1['ent'])/sp$([int]$e1['sp'])" }
    $lk = (Site) -like "links=0 watch=0*"
    if ($evName -eq "lock") { SetText 101 "Master1234"; Click 103 1500 }
    CloseTab
    "${evName}:ok=$ok/made=$made/end=$end/st1=$([int]$e0['st'])/$after/typed=$($t -like 'u=*')/shiftUp=$shiftUp"
  }
  $n = 62
  foreach ($evName in "lock", "expire", "window", "page") {
    $want = if ($evName -eq "page") { "page:ok=True/made=True/end=True/st1=1/page/typed=False/shiftUp=True" } else { "${evName}:ok=True/made=True/end=True/st1=1/st+0/ent0/sp0/typed=False/shiftUp=True" }
    Check ("SF{0:D2}" -f $n) $want (MfBarrier $evName) "after the first Shift+Tab (barrier mf.second), $evName`: no second Shift+Tab, nothing typed, no Enter/Space, Shift released"
    $n++
  }
  # ---- SF67 real Chromium list detection
  OpenPage "/mf"
  [void][SU]::Front($script:edgeWnd); Start-Sleep -Milliseconds 300
  $dl = Field "dlx"; if ($dl) { $dl.SetFocus() }; Start-Sleep -Milliseconds 400
  [SU]::keybd_event(0x28, 0, 0, [UIntPtr]::Zero); [SU]::keybd_event(0x28, 0, 2, [UIntPtr]::Zero)   # ArrowDown opens the list
  Start-Sleep -Milliseconds 900
  $lv1 = (([int64][SU]::SendMessageW($m, 0x8016, $script:edgeWnd, [IntPtr]13)) -band 0xF) - 2
  [SU]::keybd_event(0x1B, 0, 0, [UIntPtr]::Zero); [SU]::keybd_event(0x1B, 0, 2, [UIntPtr]::Zero)   # Esc closes it
  Start-Sleep -Milliseconds 900
  $lv0 = (([int64][SU]::SendMessageW($m, 0x8016, $script:edgeWnd, [IntPtr]13)) -band 0xF) - 2
  CloseTab
  Check SF67 "1|0" "$lv1|$lv0" "real Chromium autofill list (datalist) open: 1Key's list check sees it (1); after Esc it does not (0) (-1 = could not check)"

  $r66 = MfRun -hook 8
  Check SF66 "mf|0|9|0|0" "$($r66.Title)|$($r66.St)|$($r66.Reason)|$($r66.Ent)|$($r66.Sp)" "the browser's saved-login list open (hook 8): no Shift+Tab, nothing typed, no Enter/Space, reason 9 (fill $($r66.Fill))"

  Check SF48 "True|1|1|u=both-id|q=$long|p=both-pw" "$($vis48 -and $tok48 -ne 0 -and $done48)|$($r48.Fill)|$($r48.FocusMethod)|$t48" "switch off, SetFocus works: filled exactly (old value replaced) by method 1"
  AllowClick 4 $true

  Check SF45 "True" "$sens" ("judge sensitivity: a character, Ctrl+A, Enter (submit), Tab and a press on the decoy are all counted (" + (EvDelta $s0 $s1 "bi","inp","tab","ent","ca","sub","dec") + ")")
  $env:ONEKEY_TEST_UIA_DELAY_MS = $null
}
catch { Add-Failure ("exception: " + $_.Exception.Message) }
finally {
  Remove-Item Env:ONEKEY_TEST_HOLD_EVENT -ErrorAction Ignore
  if ($m -and $m -ne [IntPtr]::Zero -and $p -and -not $p.HasExited) { Quit }
  Stop-TestInstances $suffix
  if ($edgeProc) { Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" | Where-Object { $_.CommandLine -like "*$profileDir*" } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction Ignore } }
  try { $listener.Stop(); $listener.Close() } catch { }
  try { $ps.Dispose(); $rs.Close() } catch { }
  if ($before -ne [IntPtr]::Zero) { [void][SU]::Front($before) }
}
Complete-Checks
