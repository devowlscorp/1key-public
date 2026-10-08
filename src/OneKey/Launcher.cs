using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 프로그램·폴더 실행(설계 v2 3·4장, Codex 16:35 L2-2·L2-3·L2-4). 사용자 단축키·아이콘 누름에서만 부른다(읽기만으로 실행하지 않음).
/// - 대상 검사는 실행 **직전**에 다시 한다. 바로 가기는 같은 순간에 읽은 대상·인자·작업 폴더(해석 대상)를 실행하고, 원래 바로 가기를 셸에 맡기지 않는다.
/// - 검사할 수 없으면(매니페스트를 못 읽음 등) "관리자 요구 없음"으로 보지 않고 거절한다.
/// - 1Key 가 보통 권한이면 ShellExecuteEx(명시적 "open"). 관리자 권한이면 같은 사용자·세션·무결성 Medium 인 Explorer 를 실행 주체로
///   IShellDispatch2.ShellExecute 를 쓴다. 확인이 안 되면 실행하지 않는다 — 관리자 토큰으로 직접 실행하지 않는다.
/// - 이미 열린 창: 보이는 최상위 창의 실행 파일 경로가 같으면 그 창을 앞으로. 일치 창이 없는데 조회하지 못한 창이 있으면 원인 불명으로
///   보고 새로 실행하지 않고 안내(조회 실패를 관리자 창의 증거로 보지 않는다). 앞으로 가져오기가 거절되면 새로 실행하지 않는다.
/// - 실행 요청 상태(App 이 대상별로 기억): 실행을 요청한 뒤 창을 아직 못 봤으면 PendingMs 안에는 새로 실행하지 않고 창을 찾기만 하고,
///   그 뒤에도 창이 없으면 사용자에게 다시 실행할지 묻는다(NeedsConfirm, 2026-10-04 사용자 결정 D1 — 자동으로 두 번 실행하지 않는다).
/// - 탐색·검사·실행은 작업 스레드 하나에서(UI 가 멈추지 않게, Codex R34-1). 부작용이 있는 호출이 진행 중이면 그 스레드가 끝날 때까지
///   새 요청을 받지 않는다(시간이 지났다고 풀지 않는다). 실행 호출 직전에 취소·만료를 다시 본다. 앞으로 가져오기는 UI 스레드에서 한다.
/// 로그에 대상·인자를 남기지 않는다.
/// </summary>
internal static unsafe class Launcher
{
    public enum Result { Opened, Focused, FocusRefused, NotFound, Unsupported, CannotVerify, ShellUnavailable, Pending, Failed, ReadOnly, Busy, NeedsConfirm, RightsUnknown, Window, Cancelled, WebOpened, BrowserMissing }

    public const int PendingMs = 8000;
    private const int BudgetMs = 15000;   // 누른 뒤 이 시간이 지나면 실행 호출을 하지 않는다(확인이 오래 걸린 요청이 늦게 열리지 않게)

    /// <summary>실행 요청 하나. UI 가 만들고 작업 스레드가 채운다. 작업 스레드는 LaunchItem(UI 소유)을 보지 않고 이 사본만 본다.</summary>
    internal sealed class Job
    {
        public int No;
        public string Id = "", Key = "", Name = "", Kind = "", Target = "";
        public string Browser = "";    // 웹사이트: default | edge | chrome
        public long OpenedAt;          // 이 대상으로 실행을 요청했고 아직 창을 못 본 시각(0 = 없음)
        public bool Force;             // 사용자가 확인 창에서 [예](다시 실행)
        public long Deadline;
        public volatile bool Cancelled, Finished;
        public Result Outcome;
        public nint Window;            // Outcome == Window: UI 가 앞으로 가져올 창
        public bool IsFolder;          // 해석된 대상(창 확인·앞으로 가져오기용)
        public string Path = "";
        public string App = "";        // 스토어·패키지 앱이면 앱 ID(창은 앱 ID 로 찾는다)
        public string Related = "";    // UI 가 넘김: 이 대상을 실행한 뒤 실제 창을 띄운 다른 실행 파일(실행기형 프로그램, 이번 실행 동안 기억)
        public string FocusPath = "";  // Outcome == Window: 그 창의 실행 파일(대상 자신 또는 Related)
        public HashSet<nint>? Before;  // 실행 직전에 있던 창들(감시는 그 뒤 새로 뜬 창만 본다)
        public uint RootPid;           // 보통 권한 실행에서 셸이 돌려준 프로세스(관리자 경로는 0 — 감시가 실행 파일·생성 시각으로 찾는다)
        public long RootCreated;       // 그 프로세스의 생성 시각(핸들로 바로 읽음 — 실행기가 곧 끝나도 자식을 이을 수 있게)
        public nint RootHandle;        // 그 프로세스 핸들: 감시가 넘겨받아 감시 동안 쥔다(PID 재사용 불가 — R29-1). 감시로 넘기지 않으면 Take 가 닫는다
        public long ReqFileTime;       // 실행 요청 시각(UTC FILETIME): 그 뒤에 생긴 프로세스만 이 실행의 프로세스로 본다
    }

    private static Job? _job;          // UI 스레드만 바꾼다
    private static int _jobNo;
    /// <summary>시험용: 실제 실행 호출(ShellExecuteEx·IShellDispatch2.ShellExecute)에 들어간 횟수.</summary>
    public static int ExecCalls;

    /// <summary>앞선 요청의 작업 스레드가 아직 끝나지 않았다.</summary>
    public static bool Busy => _job is { Finished: false };

    /// <summary>작업 스레드는 끝났지만 UI 가 아직 결과를 받지 않은 요청 번호(없으면 0). 요청 슬롯은 UI 가 <see cref="Take"/> 할 때까지 비우지 않는다:
    /// 결과 메시지보다 다음 누름이 먼저 처리되면 UI 가 이 결과를 먼저 받아(실행 요청 기록) 그 뒤에 새 요청을 만든다(Codex 20:13 RW-1).</summary>
    public static int Unconsumed => _job is { Finished: true } j ? j.No : 0;

    /// <summary>대상 세대: 같은 id 라도 종류·대상이 바뀌면 다른 대상(이전 요청의 실행 중 상태를 새 대상에 적용하지 않는다).</summary>
    public static string KeyOf(LaunchItem it) => it.Id + "\n" + it.Kind + "\n" + it.Target;

    /// <summary>요청을 작업 스레드로 보낸다. 끝나면 notify 에 msg(wParam = 요청 번호). 앞선 요청이 끝나지 않았으면 null.</summary>
    public static Job? Start(LaunchItem it, long openedAt, bool force, string related, nint notify, uint msg)
    {
        if (_job is not null) return null;   // 진행 중이거나, 끝났지만 UI 가 아직 결과를 받지 않음(RW-1)
        var j = new Job
        {
            No = ++_jobNo, Id = it.Id, Key = KeyOf(it), Name = it.Name, Kind = it.Kind, Target = it.Target, Browser = it.Browser,
            OpenedAt = openedAt, Force = force, Deadline = Environment.TickCount64 + BudgetMs, Related = related, ReqFileTime = DateTime.UtcNow.ToFileTimeUtc(),
        };
        _job = j;
        var t = new Thread(() =>
        {
            int hr = CoInitializeEx(0, 2 /* COINIT_APARTMENTTHREADED */);
            try { j.Outcome = Work(j); }
            catch { j.Outcome = Result.Failed; }
            finally
            {
                if (hr >= 0) CoUninitialize();
                j.Finished = true;
                if (Program.TestFails("launch:latedone")) Thread.Sleep(3000);   // 시험: 끝난 뒤 결과 메시지가 늦게 도착(RW-1 장벽)
                Native.PostMessageW(notify, msg, j.No, 0);
            }
        }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return j;
    }

    /// <summary>끝난 요청을 꺼낸다(UI). 번호가 다르거나 아직이면 null.</summary>
    public static Job? Take(int no)
    {
        if (_job is { Finished: true } j && j.No == no)
        {
            _job = null;
            if ((j.Outcome != Result.Opened || j.Cancelled) && j.RootHandle != 0) { CloseHandle(j.RootHandle); j.RootHandle = 0; }   // 감시하지 않을 결과
            return j;
        }
        return null;
    }

    /// <summary>요청의 프로세스 핸들을 넘겨받는다(감시에 넘길 때 — 받은 쪽이 닫는다).</summary>
    public static nint TakeRootHandle(Job j) { nint h = j.RootHandle; j.RootHandle = 0; return h; }

    /// <summary>그 항목의 진행 중 요청을 취소(삭제·대상 변경·목록 새로 시작·종료). 이미 실행 호출에 들어갔으면 결과만 버린다.</summary>
    public static void Cancel(string? id)
    {
        if (_job is { } j && (id is null || j.Id == id)) j.Cancelled = true;
    }

    private static bool Expired(Job j) => j.Cancelled || Environment.TickCount64 > j.Deadline;

    private static Result Work(Job j)
    {
        if (Program.TestFails("launch:slowlookup")) Thread.Sleep(BudgetMs + 1000);   // 시험: 확인이 제한 시간을 넘김 → 실행 0
        if (j.Kind == "url") return WorkUrl(j);
        // 1) 실행할 대상을 지금 다시 해석·검사
        var it = new LaunchItem { Id = j.Id, Kind = j.Kind, Target = j.Target };
        if (!Resolve(it, out Target t, out Result bad)) return bad;
        j.IsFolder = t.IsFolder; j.Path = t.Path; j.App = t.App;

        // 2) 이미 열린 창
        nint found; bool uncertain;
        if (t.IsFolder) (found, uncertain) = FindFolderWindow(t.Path);
        else if (t.App.Length > 0) (found, uncertain) = FindAppWindow(t.App);
        else (found, uncertain) = FindExeWindow(t.Path);
        j.FocusPath = t.Path;
        // 실행기형 프로그램(예: 자동 업데이트 실행기가 관리자 권한 본 프로그램을 띄우고 끝남): 지난 실행에서 실제 창을 띄운 실행 파일의 창도 그 대상의 창
        if (found == 0 && !t.IsFolder && j.Related.Length > 0)
        {
            var (rf, _) = FindExeWindow(j.Related);
            if (rf != 0) { found = rf; j.FocusPath = j.Related; }
        }
        if (found != 0) { j.Window = found; return Result.Window; }
        // 실행을 요청했는데 아직 창을 못 봤다: 기다리는 중이면 새로 실행하지 않고, 오래됐으면 사용자에게 묻는다
        if (j.OpenedAt != 0 && !j.Force) return Environment.TickCount64 - j.OpenedAt < PendingMs ? Result.Pending : Result.NeedsConfirm;
        if (uncertain) return Result.CannotVerify;

        // 3) 실행 — 권한을 확인하지 못하면 실행하지 않는다(Codex R34-4)
        bool? elevated = ElevationState();
        if (elevated is null) return Result.RightsUnknown;
        if (Expired(j)) return j.Cancelled ? Result.Cancelled : Result.CannotVerify;
        j.Before = Snapshot();
        return elevated.Value ? RunViaExplorer(t, j) : RunDirect(t, j);
    }

    /// <summary>
    /// 웹사이트(Codex 02:46): 주소·브라우저를 실행 직전에 다시 검사하고 고른 브라우저로 연다. 열린 창 찾기·8초 다시 실행 확인·실행기 추적·창 감시는 하지 않는다
    /// (브라우저 창은 탭을 함께 써서 "그 사이트의 창"을 가릴 수 없다). 관리자 권한이면 프로그램과 같이 보통 권한 Explorer 를 거친다 — 확인이 안 되면 열지 않는다.
    /// </summary>
    private static Result WorkUrl(Job j)
    {
        if (!ResolveUrl(j.Target, j.Browser, out Target t, out Result bad)) return bad;
        j.IsFolder = false; j.Path = t.Path; j.App = "";
        bool? elevated = ElevationState();
        if (elevated is null) return Result.RightsUnknown;
        if (Expired(j)) return j.Cancelled ? Result.Cancelled : Result.CannotVerify;
        Result r = elevated.Value ? RunViaExplorer(t, j) : RunDirect(t, j);
        if (j.RootHandle != 0) { CloseHandle(j.RootHandle); j.RootHandle = 0; }   // 감시하지 않는다
        j.RootPid = 0;
        return r == Result.Opened ? Result.WebOpened : r;
    }

    /// <summary>
    /// 웹사이트 실행 대상: 기본 브라우저면 주소 자체(셸이 기본 브라우저로 연다), Edge/Chrome 이면 App Paths 에서 찾은 실행 파일 + 주소 인자 하나.
    /// 주소는 공백·따옴표·제어 문자가 없게 검사됐으므로 따옴표로 감싼 한 인자로 갈라지지 않는다(cmd·PowerShell 을 거치지 않는다).
    /// 고른 브라우저가 없으면 BrowserMissing — 기본 브라우저로 몰래 바꾸지 않는다.
    /// </summary>
    internal static bool ResolveUrl(string url, string browser, out Target t, out Result bad)
    {
        t = default; bad = Result.Unsupported;
        if (!LaunchStore.IsWebUrl(url)) return false;
        if (browser == "default") { t = new Target(false, url, "", ""); return true; }
        if (browser is not ("edge" or "chrome")) return false;
        string? exe = BrowserExe(browser);
        if (exe is null) { bad = Result.BrowserMissing; return false; }
        string? why = CheckExe(exe);   // 고정 드라이브·관리자 요구 없음·호환성 설정 확인
        if (Program.IsTestMode && why is not null) { try { File.AppendAllText(Path.Combine(Config.Dir, "launch-why.txt"), "browser-" + why + Environment.NewLine); } catch { } }
        if (why == "missing") { bad = Result.BrowserMissing; return false; }
        if (why is not null) return false;
        t = new Target(false, exe, "\"" + url + "\"", Path.GetDirectoryName(exe) ?? "");
        return true;
    }

    /// <summary>Edge/Chrome 실행 파일: App Paths(HKCU 먼저, 그다음 HKLM)의 기본 값. 절대 경로·그 이름의 .exe·있는 파일만. 시험 모드는 ONEKEY_TEST_BROWSER_&lt;이름&gt; 로 바꿀 수 있다.</summary>
    internal static string? BrowserExe(string browser)
    {
        string name = browser == "edge" ? "msedge.exe" : browser == "chrome" ? "chrome.exe" : "";
        if (name.Length == 0) return null;
        if (Program.IsTestMode && Environment.GetEnvironmentVariable("ONEKEY_TEST_BROWSER_" + browser.ToUpperInvariant()) is string tv)
            return tv.Length > 0 && LaunchStore.IsRootedPath(tv) && File.Exists(tv) ? tv : null;   // 시험: 다른 실행 파일·"없음"(빈 값)
        foreach (var hive in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
        {
            try
            {
                using var k = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\" + name);
                if (k?.GetValue("") is not string v) continue;
                v = Environment.ExpandEnvironmentVariables(v.Trim().Trim('"'));
                if (LaunchStore.IsRootedPath(v) && v.EndsWith("\\" + name, StringComparison.OrdinalIgnoreCase) && File.Exists(v)) return v;
            }
            catch { }
        }
        return null;
    }

    // ------------------------------------------------------------------ 대상 해석·검사

    /// <summary>해석한 대상. App = 스토어·패키지 앱 ID(그때 Path 는 "shell:AppsFolder\앱 ID" — 셸이 연다).</summary>
    internal readonly record struct Target(bool IsFolder, string Path, string Args, string Dir, string App = "");

    internal const string AppsFolderPrefix = @"shell:AppsFolder\";

    /// <summary>실행할 대상을 해석하고 지원 범위를 검사한다. 바로 가기는 지금 읽은 대상·인자·작업 폴더로.</summary>
    internal static bool Resolve(LaunchItem it, out Target t, out Result bad)
    {
        t = default; bad = Result.Unsupported;
        if (it.Kind == "folder")
        {
            if (!ExistsWithin(it.Target, true, 3000, out bool timedOut)) { bad = timedOut ? Result.CannotVerify : Result.NotFound; return false; }
            t = new Target(true, it.Target, "", "");
            return true;
        }
        if (it.Kind == "app")
        {
            // 스토어·패키지 앱: 매니페스트 검사 대상이 아니다(패키지 앱은 관리자 권한을 요구할 수 없다). 지금 설치돼 있는지만 셸에 묻는다
            if (!LaunchStore.IsAumid(it.Target)) return false;
            bool? installed = AppInstalled(it.Target);
            if (installed != true) { bad = installed is null ? Result.CannotVerify : Result.NotFound; return false; }
            string? appWhy = CheckApp(it.Target);
            if (Program.IsTestMode && appWhy is not null) { try { File.AppendAllText(Path.Combine(Config.Dir, "launch-why.txt"), "app-" + appWhy + Environment.NewLine); } catch { } }
            if (appWhy is not null) { bad = Result.Unsupported; return false; }   // 관리자 요구 가능·검사 불명은 열지 않는다
            t = new Target(false, AppsFolderPrefix + it.Target, "", "", it.Target);
            return true;
        }
        string exe, args = "", dir = "";
        if (it.Kind == "lnk")
        {
            if (!File.Exists(it.Target)) { bad = Result.NotFound; return false; }
            var link = ReadShortcut(it.Target);
            if (link is null) { bad = Result.Unsupported; return false; }   // 읽지 못함 = 검사 불명 → 거절
            var l = link.Value;
            if (l.RunAs) { bad = Result.Unsupported; return false; }       // "관리자 권한으로 실행" 바로 가기
            if (l.TargetIsFolder)
            {
                if (!ExistsWithin(l.Target, true, 3000, out bool to)) { bad = to ? Result.CannotVerify : Result.NotFound; return false; }
                t = new Target(true, l.Target, "", "");
                return true;
            }
            exe = l.Target; args = l.Args; dir = l.Dir;
        }
        else exe = it.Target;
        string? why = CheckExe(exe);
        if (Program.IsTestMode && why is not null) { try { File.AppendAllText(Path.Combine(Config.Dir, "launch-why.txt"), why + Environment.NewLine); } catch { } }   // 시험 진단: 거절 까닭 코드만(경로 없음)
        if (why == "missing") { bad = Result.NotFound; return false; }
        if (why is not null) { bad = Result.Unsupported; return false; }
        if (dir.Length == 0 || !LaunchStore.IsRootedPath(dir) || !Directory.Exists(dir)) dir = Path.GetDirectoryName(exe) ?? "";   // 작업 폴더: 바로 가기 값, 없으면 대상 폴더
        t = new Target(false, exe, args, dir);
        return true;
    }

    /// <summary>실행 파일 검사: 없으면 "missing", 지원 범위 밖이면 이유 문자열, 괜찮으면 null.</summary>
    internal static string? CheckExe(string exe)
    {
        if (!LaunchStore.IsRootedPath(exe) || exe.StartsWith(@"\\", StringComparison.Ordinal)) return "path";
        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return "type";
        fixed (char* r0 = exe[..3]) if (GetDriveTypeW(r0) != 3 /* DRIVE_FIXED */) return "network";   // 매핑 드라이브·이동식은 파일을 보기 전에 거절(기다리지 않게)
        if (!File.Exists(exe)) return "missing";
        string? final = FinalPath(exe);
        if (final is null) return "final";                                         // 실제 위치를 확인하지 못함
        if (final.StartsWith(@"\\", StringComparison.Ordinal) || final.StartsWith(@"\\?\UNC", StringComparison.OrdinalIgnoreCase)) return "network";
        string root = final.StartsWith(@"\\?\", StringComparison.Ordinal) ? final.Substring(4, 3) : final[..3];
        fixed (char* r = root) if (GetDriveTypeW(r) != 3 /* DRIVE_FIXED */) return "network";   // 매핑 드라이브·이동식은 지원 범위 밖
        int level = ManifestLevel(exe);
        if (level != 0) return "admin";                                            // requireAdministrator/highestAvailable, 또는 읽지 못함(-1)
        bool? compat = CompatRunAsAdmin(exe);
        if (compat is null) return "verify";                                       // 호환성 설정을 읽지 못함 = 검사 불명(Codex R34-4)
        if (compat.Value) return "admin";
        return null;
    }

    /// <summary>0 = 관리자 요구 없음(매니페스트 없음 포함), 1 = 관리자 요구, -1 = 확인하지 못함.</summary>
    internal static int ManifestLevel(string exe)
    {
        nint mod;
        fixed (char* p = exe) mod = LoadLibraryExW(p, 0, 0x00000002 | 0x00000020 /* LOAD_LIBRARY_AS_DATAFILE | AS_IMAGE_RESOURCE */);
        if (mod == 0) return -1;
        try
        {
            nint res = FindResourceW(mod, (nint)1, (nint)24 /* RT_MANIFEST */);
            if (res == 0) res = FindResourceW(mod, (nint)2, (nint)24);
            if (res == 0) return 0;   // 매니페스트 없음 = 관리자 요구 없음(로드는 됐으므로 확인한 결과)
            uint size = SizeofResource(mod, res);
            nint h = LoadResource(mod, res);
            byte* data = h != 0 ? (byte*)LockResource(h) : null;
            if (data == null || size == 0 || size > 1 << 20) return -1;
            string xml = System.Text.Encoding.UTF8.GetString(data, (int)size);
            int at = xml.IndexOf("requestedExecutionLevel", StringComparison.OrdinalIgnoreCase);
            if (at < 0) return 0;
            int end = xml.IndexOf('>', at);
            string tag = end > at ? xml[at..end] : xml[at..];
            return tag.Contains("requireAdministrator", StringComparison.OrdinalIgnoreCase) || tag.Contains("highestAvailable", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }
        catch { return -1; }
        finally { FreeLibrary(mod); }
    }

    /// <summary>호환성 설정의 "관리자 권한으로 실행"(AppCompatFlags\Layers 에 RUNASADMIN). 키·값이 없으면 false, 읽다가 실패하면 null(검사 불명).
    /// 이 검사 범위 밖의 Windows 상승 정책까지 막는다고 보장하지 않는다.</summary>
    internal static bool? CompatRunAsAdmin(string exe)
    {
        if (Program.TestFails("launch:compat")) return null;
        foreach (var hive in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
        {
            try
            {
                using var k = hive.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers");
                if (k is null) continue;   // 키 없음 = 설정 없음
                object? v = k.GetValue(exe);
                if (v is null) continue;
                if (v is not string s) return null;   // 예상하지 못한 형식
                if (s.Contains("RUNASADMIN", StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { return null; }   // 읽기 실패 ≠ 없음
        }
        return false;
    }

    /// <summary>고정 드라이브의 로컬 경로인가(아이콘 읽기 전). UNC·매핑 드라이브·이동식, 실제 위치가 네트워크·다른 종류 드라이브면 false.
    /// exists = 그 경로가 있다(없는 로컬 경로는 true·exists false).</summary>
    internal static bool IsLocalFixed(string path, out bool exists)
    {
        exists = false;
        if (!LaunchStore.IsRootedPath(path) || path.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        string root0 = path[..3];
        fixed (char* r = root0) if (GetDriveTypeW(r) != 3 /* DRIVE_FIXED */) return false;
        exists = File.Exists(path) || Directory.Exists(path);
        if (!exists) return true;
        string? final = FinalPath(path);
        if (final is null || final.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        string root = final.Length >= 3 ? final[..3] : final;
        fixed (char* r = root) return GetDriveTypeW(r) == 3;
    }

    private static string? FinalPath(string path)
    {
        nint h;
        fixed (char* p = path) h = CreateFileW(p, 0, 7 /* share all */, 0, 3 /* OPEN_EXISTING */, 0x02000000 /* BACKUP_SEMANTICS */, 0);
        if (h == -1 || h == 0) return null;
        try
        {
            char* buf = stackalloc char[1024];
            uint n = GetFinalPathNameByHandleW(h, buf, 1024, 0);
            if (n == 0 || n >= 1024) return null;
            string s = new string(buf, 0, (int)n);
            return s.StartsWith(@"\\?\", StringComparison.Ordinal) && !s.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ? s[4..] : s;
        }
        finally { CloseHandle(h); }
    }

    /// <summary>폴더·파일이 있는지를 시간 상한 안에서(네트워크 위치가 오래 기다리게 하지 않게).</summary>
    private static bool ExistsWithin(string path, bool folder, int ms, out bool timedOut)
    {
        bool result = false;
        var t = new Thread(() => { try { result = folder ? Directory.Exists(path) : File.Exists(path); } catch { } }) { IsBackground = true };
        t.Start();
        timedOut = !t.Join(ms);
        return !timedOut && result;
    }

    // ------------------------------------------------------------------ 바로 가기 읽기

    internal readonly record struct Shortcut(string Target, string Args, string Dir, bool RunAs, bool TargetIsFolder);

    private static readonly Guid CLSID_ShellLink = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid IID_IShellLinkW = new("000214F9-0000-0000-C000-000000000046");
    private static readonly Guid IID_IPersistFile = new("0000010b-0000-0000-C000-000000000046");
    private static readonly Guid IID_IShellLinkDataList = new("45e2b4ae-b1c3-11d0-b92f-00a0c90312e1");

    /// <summary>바로 가기의 대상·인자·작업 폴더·관리자 실행 표시를 한 번에 읽는다. 읽지 못하면 null.</summary>
    internal static Shortcut? ReadShortcut(string lnk) => OnComThread(() => ReadShortcutCom(lnk), null);

    private static Shortcut? ReadShortcutCom(string lnk)
    {
        nint link = 0, pf = 0, dl = 0;
        Guid clsid = CLSID_ShellLink, iid = IID_IShellLinkW, iidPf = IID_IPersistFile, iidDl = IID_IShellLinkDataList;
        try
        {
            if (CoCreateInstance(ref clsid, 0, 1, ref iid, out link) < 0 || link == 0) return null;
            if (((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)V(link)[0])(link, &iidPf, &pf) < 0 || pf == 0) return null;
            int hr;
            fixed (char* p = lnk) hr = ((delegate* unmanaged[Stdcall]<nint, char*, uint, int>)V(pf)[5])(pf, p, 0);   // IPersistFile::Load
            if (hr < 0) return null;
            const int N = 1024;
            char* buf = stackalloc char[N];
            buf[0] = '\0';
            if (((delegate* unmanaged[Stdcall]<nint, char*, int, void*, uint, int>)V(link)[3])(link, buf, N, null, 0) < 0) return null;   // GetPath (확장된 경로)
            string target = new string(buf);
            if (target.Length == 0) return null;                                                      // 파일 시스템 대상이 아님(URL 등)
            buf[0] = '\0'; ((delegate* unmanaged[Stdcall]<nint, char*, int, int>)V(link)[10])(link, buf, N); string args = new string(buf);   // GetArguments
            buf[0] = '\0'; ((delegate* unmanaged[Stdcall]<nint, char*, int, int>)V(link)[8])(link, buf, N); string dir = Environment.ExpandEnvironmentVariables(new string(buf));   // GetWorkingDirectory
            bool runAs = true;   // 읽지 못하면 관리자 표시가 있는 것으로 본다(검사 불명은 거절)
            if (((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)V(link)[0])(link, &iidDl, &dl) >= 0 && dl != 0)
            {
                uint flags = 0;
                if (((delegate* unmanaged[Stdcall]<nint, uint*, int>)V(dl)[6])(dl, &flags) >= 0) runAs = (flags & 0x00002000 /* SLDF_RUNAS_USER */) != 0;   // GetFlags
            }
            bool isFolder = Directory.Exists(target);
            if (!isFolder && !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;   // bat·cmd·ps1·msc 등은 지원하지 않는다
            if (args.Length > LaunchStore.ArgMax || dir.Length > LaunchStore.ArgMax) return null;
            return new Shortcut(target, isFolder ? "" : args, isFolder ? "" : dir, runAs, isFolder);
        }
        catch { return null; }
        finally { Release(dl); Release(pf); Release(link); }
    }

    // ------------------------------------------------------------------ 이미 열린 창

    /// <summary>실행 파일 경로가 같은 보이는 최상위 창(Z 순서 맨 위). uncertain = 실행 파일을 확인하지 못한 창이 있었다.</summary>
    internal static (nint hwnd, bool uncertain) FindExeWindow(string exe)
    {
        string want = Norm(exe);
        nint found = 0; bool uncertain = false;
        uint mySession = 0; ProcessIdToSessionId((uint)Environment.ProcessId, out mySession);
        for (nint h = GetTopWindow(0); h != 0; h = GetWindow(h, 2 /* GW_HWNDNEXT */))
        {
            if (!IsCandidate(h)) continue;
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == 0 || pid == (uint)Environment.ProcessId) continue;
            if (ProcessIdToSessionId(pid, out uint sess) && sess != mySession) continue;
            string? img = ImageOf(pid);
            if (img is null) { uncertain = true; continue; }   // 조회 실패: 원인 불명(관리자 창이라고 단정하지 않는다)
            if (Norm(img) == want) { found = h; break; }
        }
        return (found, found == 0 && uncertain);
    }

    /// <summary>
    /// 그 앱 ID 의 보이는 최상위 창(Z 순서 맨 위). 스토어 앱(UWP)은 ApplicationFrameHost 가 띄운 틀 창에 앱 ID 가 창 속성으로 붙고,
    /// 패키지 데스크톱 앱(MSIX)은 프로세스의 앱 ID 로 안다. uncertain = 앱 ID 를 확인하지 못한 창이 있었다(Codex C28-3: "ID 없음"과
    /// "조회 실패"를 나눈다 — 일치 창이 없는데 모르는 창이 있으면 새로 실행하지 않는다).
    /// </summary>
    internal static (nint hwnd, bool uncertain) FindAppWindow(string aumid)
    {
        uint mySession = 0; ProcessIdToSessionId((uint)Environment.ProcessId, out mySession);
        bool uncertain = false;
        for (nint h = GetTopWindow(0); h != 0; h = GetWindow(h, 2 /* GW_HWNDNEXT */))
        {
            if (!IsCandidate(h)) continue;
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == 0 || pid == (uint)Environment.ProcessId) continue;
            if (ProcessIdToSessionId(pid, out uint sess) && sess != mySession) continue;
            string? id = AppIdOf(h, pid, out bool unknown);
            if (unknown) { uncertain = true; continue; }
            if (string.Equals(id, aumid, StringComparison.OrdinalIgnoreCase)) return (h, false);
        }
        return (0, uncertain);
    }

    private static readonly Guid IID_IPropertyStore = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    private static readonly Guid FMTID_AppUserModel = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    [StructLayout(LayoutKind.Sequential)] private struct PROPERTYKEY { public Guid fmtid; public uint pid; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PROPVARIANT { [FieldOffset(0)] public ushort vt; [FieldOffset(8)] public nint p; }

    /// <summary>
    /// 창의 앱 ID: 프로세스가 패키지 앱이면 그 ID, 아니면 창 속성(PKEY_AppUserModel_ID). 확인한 결과 ID 가 없으면 null(unknown=false),
    /// 프로세스도 창 속성도 읽지 못하면 null(unknown=true) — "없음"과 "모름"을 합치지 않는다(Codex C28-3).
    /// </summary>
    internal static string? AppIdOf(nint h, uint pid, out bool unknown)
    {
        unknown = false;
        if (Program.TestFails("launch:appid")) { unknown = true; return null; }   // 시험: 앱 ID 조회 실패
        bool processKnown = false;
        nint ph = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (ph != 0)
        {
            try
            {
                uint n = 260;
                char* buf = stackalloc char[260];
                int rc = GetApplicationUserModelId(ph, &n, buf);
                if (rc == 0 && n > 1) return new string(buf, 0, (int)n - 1);
                processKnown = rc == 15703 /* APPMODEL_ERROR_NO_APPLICATION */ || rc == 15700 /* APPMODEL_ERROR_NO_PACKAGE */;
            }
            finally { CloseHandle(ph); }
        }
        nint ps = 0;
        Guid iid = IID_IPropertyStore;
        try
        {
            if (SHGetPropertyStoreForWindow(h, &iid, &ps) < 0 || ps == 0) { unknown = !processKnown; return null; }
            var key = new PROPERTYKEY { fmtid = FMTID_AppUserModel, pid = 5 };
            var v = new PROPVARIANT();
            if (((delegate* unmanaged[Stdcall]<nint, PROPERTYKEY*, PROPVARIANT*, int>)V(ps)[5])(ps, &key, &v) < 0) { unknown = !processKnown; return null; }   // IPropertyStore::GetValue
            try { return v.vt == 31 /* VT_LPWSTR */ && v.p != 0 ? new string((char*)v.p) : null; }   // 읽었고 값이 없음 = 확인한 "없음"
            finally { PropVariantClear(&v); }
        }
        catch { unknown = !processKnown; return null; }
        finally { Release(ps); }
    }

    private static readonly Guid IID_IShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    /// <summary>
    /// 그 앱이 지금 설치돼 있나(셸의 앱 목록에 있나). "찾을 수 없음" 계열 HRESULT 만 false, 그 밖의 실패(권한·셸 오류)와 시간 초과는 null —
    /// 없음과 모름을 합치지 않는다(Codex C28-3).
    /// </summary>
    internal static bool? AppInstalled(string aumid)
    {
        if (Program.TestFails("launch:appquery")) return null;
        int r = OnComThread(() =>
        {
            nint item = 0;
            Guid iid = IID_IShellItem;
            try
            {
                int hr;
                fixed (char* p = AppsFolderPrefix + aumid) hr = SHCreateItemFromParsingName(p, 0, &iid, &item);
                if (hr >= 0 && item != 0) return 1;
                return hr is unchecked((int)0x80070002) or unchecked((int)0x80070003) or unchecked((int)0x80070490) ? 0 : -1;   // FILE_NOT_FOUND / PATH_NOT_FOUND / ERROR_NOT_FOUND
            }
            catch { return -1; }
            finally { Release(item); }
        }, -1);
        return r < 0 ? null : r == 1;
    }

    /// <summary>
    /// 패키지 앱을 보통 권한으로 열 수 있는가(Codex C28-3: 패키지 데스크톱 앱도 allowElevation 으로 관리자 권한을 요구할 수 있다).
    /// 패키지 패밀리의 설치 폴더에서 AppxManifest.xml 을 읽어, 그 앱(Application Id)이 있고 패키지가 allowElevation 을 선언하지 않았으면 null.
    /// allowElevation 이면 "admin"(관리자 요구 거절과 같은 정책), 패키지·매니페스트·앱 항목을 확인하지 못하면 "verify"(검사 불명은 거절).
    /// </summary>
    internal static string? CheckApp(string aumid)
    {
        if (Program.TestFails("launch:appmanifest")) return "verify";
        int bang = aumid.IndexOf('!');
        if (bang <= 0) return "verify";
        string family = aumid[..bang], appId = aumid[(bang + 1)..];
        List<string>? dirs = PackageDirsOf(family);
        if (dirs is null || dirs.Count == 0) return "verify";
        // 같은 패밀리에 설치된 패키지가 여럿이면(버전·구성 패키지) 셸이 실제로 어느 것을 여는지 여기서 확정하지 않는다: 모두 읽어서 모두 통과해야 연다(R29-2)
        var xmls = new List<byte[]?>();
        foreach (string dir in dirs)
        {
            try
            {
                var fi = new FileInfo(Path.Combine(dir, "AppxManifest.xml"));
                xmls.Add(fi.Length is > 0 and <= ManifestMax ? File.ReadAllBytes(fi.FullName) : null);
            }
            catch { xmls.Add(null); }
        }
        return CheckManifests(xmls, appId);
    }

    private const int ManifestMax = 4 << 20;

    /// <summary>
    /// 패밀리의 매니페스트들 판정(단위 시험 MF01–): 하나라도 못 읽거나 XML 로 해석하지 못하면 "verify", 하나라도 allowElevation 이면 "admin",
    /// 그 앱(Application Id)을 가진 패키지가 없으면 "verify", 아니면 null(열어도 됨).
    /// </summary>
    internal static string? CheckManifests(IReadOnlyList<byte[]?> xmls, string appId)
    {
        bool hasApp = false, admin = false;
        foreach (byte[]? x in xmls)
        {
            if (x is null || ReadManifest(x, appId) is not { } m) return "verify";
            hasApp |= m.App; admin |= m.Elevation;
        }
        if (admin) return "admin";
        return hasApp ? null : "verify";
    }

    /// <summary>
    /// 매니페스트 하나를 XML 로 읽는다 — Windows 의 XmlLite(xmllite.dll: DTD 금지·외부 자원 해석 없음, 깊이·노드 수·크기 제한). 해석하지 못하면 null.
    /// (System.Xml 은 실행 파일을 3MB 넘게 키워서 운영체제의 파서를 쓴다.)
    /// App = Applications 아래 Application 요소의 Id 속성이 appId 와 같음(대소문자 그대로). Elevation = 이름이 Capability 인 요소(접두사·이름공간 무관,
    /// 따옴표·공백 표기 무관)의 Name 속성이 allowElevation(대소문자 무시) — 다른 이름공간의 같은 이름도 보수적으로 관리자 요구로 본다. 주석 속 글자는 보지 않는다.
    /// </summary>
    internal static (bool App, bool Elevation)? ReadManifest(byte[] xml, string appId)
    {
        if (xml.Length == 0 || xml.Length > ManifestMax) return null;
        nint stream = 0, reader = 0;
        try
        {
            fixed (byte* p = xml) stream = SHCreateMemStream(p, (uint)xml.Length);
            if (stream == 0) return null;
            Guid iid = IID_IXmlReader;
            if (CreateXmlReader(&iid, &reader, 0) < 0 || reader == 0) return null;
            nint* v = V(reader);
            var setProp = (delegate* unmanaged[Stdcall]<nint, uint, nint, int>)v[5];
            if (setProp(reader, 4 /* XmlReaderProperty_DtdProcessing */, 0 /* DtdProcessing_Prohibit */) < 0) return null;
            if (setProp(reader, 6 /* XmlReaderProperty_MaxElementDepth */, 64) < 0) return null;
            if (((delegate* unmanaged[Stdcall]<nint, nint, int>)v[3])(reader, stream) < 0) return null;   // SetInput
            bool app = false, elev = false, sawPackage = false;
            var path = new List<string>();
            for (int nodes = 0; ; nodes++)
            {
                int nt = 0;
                int hr = ((delegate* unmanaged[Stdcall]<nint, int*, int>)v[6])(reader, &nt);   // Read
                if (hr < 0 || nodes > 200_000) return null;   // 잘못된 XML·DTD·너무 큼
                if (hr == 1 /* S_FALSE: 끝 */) break;
                if (nt == 15 /* XmlNodeType_EndElement */) { if (path.Count > 0) path.RemoveAt(path.Count - 1); continue; }
                if (nt != 1 /* XmlNodeType_Element */) continue;
                string name = XStr(reader, 14 /* GetLocalName */);
                bool empty = ((delegate* unmanaged[Stdcall]<nint, int>)v[20])(reader) != 0;   // IsEmptyElement
                if (path.Count == 0) sawPackage = name == "Package";
                if (name == "Application" && path.Count > 0 && path[^1] == "Applications" && XAttr(reader, "Id") == appId) app = true;
                if (name == "Capability" && string.Equals(XAttr(reader, "Name"), "allowElevation", StringComparison.OrdinalIgnoreCase)) elev = true;
                if (!empty) path.Add(name);
            }
            return sawPackage ? (app, elev) : null;
        }
        catch { return null; }
        finally { Release(reader); Release(stream); }
    }

    /// <summary>IXmlReader 의 (글자 포인터, 길이) 를 돌려주는 메서드(GetLocalName 14, GetValue 16).</summary>
    private static string XStr(nint r, int slot)
    {
        char* s = null; uint n = 0;
        return ((delegate* unmanaged[Stdcall]<nint, char**, uint*, int>)V(r)[slot])(r, &s, &n) >= 0 && s != null ? new string(s, 0, (int)n) : "";
    }

    /// <summary>지금 요소의 접두사 없는 속성 값(없으면 null). 읽은 뒤 요소로 돌아간다.</summary>
    private static string? XAttr(nint r, string localName)
    {
        int hr;
        fixed (char* nm = localName) fixed (char* ns = "") hr = ((delegate* unmanaged[Stdcall]<nint, char*, char*, int>)V(r)[10])(r, nm, ns);   // MoveToAttributeByName
        if (hr != 0) return null;
        string val = XStr(r, 16 /* GetValue */);
        ((delegate* unmanaged[Stdcall]<nint, int>)V(r)[11])(r);   // MoveToElement
        return val;
    }

    private static readonly Guid IID_IXmlReader = new("7279FC81-709D-4095-B63D-69FE4B0D9030");
    [DllImport("xmllite.dll")] private static extern int CreateXmlReader(Guid* riid, nint* ppv, nint malloc);
    [DllImport("shlwapi.dll", EntryPoint = "#12")] private static extern nint SHCreateMemStream(byte* pInit, uint cbInit);

    /// <summary>패키지 패밀리 이름 → 이 사용자에게 설치된 그 패밀리의 모든 패키지 설치 폴더. 못 읽으면 null.</summary>
    private static List<string>? PackageDirsOf(string family)
    {
        try
        {
            uint count = 0, bufLen = 0;
            fixed (char* f = family)
            {
                int rc = GetPackagesByPackageFamily(f, &count, null, &bufLen, null);
                if (rc != 122 /* ERROR_INSUFFICIENT_BUFFER */ || count == 0 || count > 64 || bufLen == 0 || bufLen > 1 << 16) return null;
                nint* names = stackalloc nint[(int)count];
                char* buf = stackalloc char[(int)bufLen];
                if (GetPackagesByPackageFamily(f, &count, names, &bufLen, buf) != 0 || count == 0) return null;
                var r = new List<string>();
                char* path = stackalloc char[1024];
                for (int i = 0; i < count; i++)
                {
                    uint len = 1024;
                    if (GetPackagePathByFullName((char*)names[i], &len, path) != 0 || len == 0) return null;
                    r.Add(new string(path, 0, (int)len - 1));
                }
                return r;
            }
        }
        catch { return null; }
    }

    private static bool IsCandidate(nint h)
    {
        if (!IsWindowVisible(h)) return false;
        long ex = GetWindowLongPtrW(h, -20);
        // 주인 창이 있는 창: 보이는 창에 딸린 대화 상자 등은 뺀다. 주인이 숨었거나 크기 없는 창이면(Delphi 프로그램의 본창 — 주인은 TApplication 의
        // 0×0 창, 2026-10-05 인하우스 뱅킹 Terminal01) 그 프로그램의 본창이다. WS_EX_APPWINDOW 는 작업 표시줄에 나오는 창이라 후보.
        nint owner = GetWindow(h, 4 /* GW_OWNER */);
        if (owner != 0 && (ex & 0x00040000 /* WS_EX_APPWINDOW */) == 0 && IsWindowVisible(owner)
            && !(GetWindowRect(owner, out RECT orc) && (orc.Right - orc.Left <= 2 || orc.Bottom - orc.Top <= 2))) return false;
        if ((ex & 0x80 /* WS_EX_TOOLWINDOW */) != 0) return false;
        // 재사용 대상이 아닌 창(정책, Codex R34-3 — OS 가 보장하는 "활성화 불가" 기준이 아니다): 후보도 조회 실패도 아니다.
        //  (1) 넓이나 높이가 2px 이하 — 사람이 볼 수도 누를 수도 없는 도우미 창(0.3.3·0.3.13 실측: 원격 접속 프로그램의 1×1 창이 조회 거부로
        //      모든 실행을 "확인 불가"로 막았다)
        //  (2) NOACTIVATE + TRANSPARENT + LAYERED 를 모두 가진 창 — 보조 창으로 보고 빼는 제품 정책(SetForegroundWindow 가 절대 못 한다는 Windows 보장이 아니다).
        //      그래서 세 비트를 모두 가진 큰 창은 재사용 대상에서 빠진다(인계 문서 6장)
        // 스타일 하나만으로는 빼지 않는다: 큰 TRANSPARENT 창, NOACTIVATE 만 있는 창은 정상 창일 수 있어 후보로 둔다(LA24).
        // 크기를 읽지 못한 창은 작은 창으로 보지 않고 후보로 둔다.
        if (GetWindowRect(h, out RECT r) && (r.Right - r.Left <= 2 || r.Bottom - r.Top <= 2)) return false;
        const long ClickThroughHidden = 0x08000000 /* NOACTIVATE */ | 0x20 /* TRANSPARENT */ | 0x00080000 /* LAYERED */;
        if ((ex & ClickThroughHidden) == ClickThroughHidden) return false;
        int cloaked = 0;
        if (DwmGetWindowAttribute(h, 14 /* DWMWA_CLOAKED */, &cloaked, 4) == 0 && cloaked != 0) return false;
        return true;
    }

    private static string? ImageOf(uint pid)
    {
        nint ph = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (ph == 0) return null;
        try
        {
            char* buf = stackalloc char[1024];
            uint n = 1024;
            if (!QueryFullProcessImageNameW(ph, 0, buf, &n)) return null;
            string img = new string(buf, 0, (int)n);
            // 시험: 이름에 LaBlind 가 든 프로그램은 조회 실패처럼(관리자 창 등 조회 거부를 일반 권한 시험에서 흉내)
            if (Program.TestFails("launch:blind") && img.Contains("LaBlind", StringComparison.OrdinalIgnoreCase)) return null;
            return img;
        }
        finally { CloseHandle(ph); }
    }

    private static string Norm(string p) { try { return Path.GetFullPath(p).TrimEnd('\\').ToLowerInvariant(); } catch { return p.ToLowerInvariant(); } }

    /// <summary>현재 위치가 그 폴더인 탐색기 창. 확인된 현재 위치만(다른 탭에 있는 같은 폴더는 찾지 못한다).
    /// uncertain = 창 목록을 읽지 못했거나 시간이 지났거나 어떤 창의 위치를 묻다 실패했다(못 찾음과 다르다 — 새로 열지 않는다, Codex R34-1).</summary>
    internal static (nint hwnd, bool uncertain) FindFolderWindow(string folder)
    {
        if (Program.TestFails("launch:folderquery")) return (0, true);
        string want = Norm(folder);
        var (h, ok) = OnComThread(() => FindFolderCom(want), ((nint)0, false));
        return (h, h == 0 && !ok);
    }

    private static readonly Guid CLSID_ShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid IID_IShellWindows = new("85CB6900-4D95-11CF-960C-0080C7F4EE85");
    private static readonly Guid IID_IServiceProvider = new("6D5140C1-7436-11CE-8034-00AA006009FA");
    private static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid IID_IShellBrowser = new("000214E2-0000-0000-C000-000000000046");
    private static readonly Guid IID_IFolderView = new("CDE725B0-CCC9-4519-917E-325D72FAB4CE");
    private static readonly Guid IID_IPersistFolder2 = new("1AC3D9F0-175C-11D1-95BE-00609797EA4F");
    private static readonly Guid IID_IDispatch = new("00020400-0000-0000-C000-000000000046");
    private static readonly Guid IID_IShellFolderViewDual = new("E7A1AF80-4D96-11CF-960C-0080C7F4EE85");
    private static readonly Guid IID_IShellDispatch2 = new("A4C6892C-3BA9-11D2-9DEA-00C04FB16162");

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct VARIANT { [FieldOffset(0)] public ushort vt; [FieldOffset(8)] public nint p; [FieldOffset(8)] public int i4; }

    /// <summary>(찾은 창, 끝까지 확인함). 확인함 = 창 목록을 읽었고 모든 창의 위치를 물었다.</summary>
    private static (nint, bool) FindFolderCom(string want)
    {
        nint sw = 0;
        Guid clsid = CLSID_ShellWindows, iid = IID_IShellWindows;
        try
        {
            if (CoCreateInstance(ref clsid, 0, 4 /* CLSCTX_LOCAL_SERVER */, ref iid, out sw) < 0 || sw == 0) return (0, false);
            int count = 0;
            if (((delegate* unmanaged[Stdcall]<nint, int*, int>)V(sw)[7])(sw, &count) < 0) return (0, false);   // get_Count
            if (count > 200) return (0, false);
            bool ok = true;
            for (int i = 0; i < count; i++)
            {
                var idx = new VARIANT { vt = 3 /* VT_I4 */, i4 = i };
                nint disp = 0;
                if (((delegate* unmanaged[Stdcall]<nint, VARIANT, nint*, int>)V(sw)[8])(sw, idx, &disp) < 0 || disp == 0) { ok = false; continue; }   // Item
                try
                {
                    nint frame = BrowserOf(disp, out string? path, out bool asked);
                    if (frame != 0 && path is not null && Norm(path) == want) return (frame, true);
                    if (!asked) ok = false;
                }
                finally { Release(disp); }
            }
            return (0, ok);
        }
        catch { return (0, false); }
        finally { Release(sw); }
    }

    /// <summary>창 디스패치 → 최상위 창과 지금 보이는 폴더 경로. asked = 그 창의 브라우저까지 닿았다(폴더 보기가 없거나 파일 시스템 폴더가 아니어서 경로가 없는 것은 물은 것).</summary>
    private static nint BrowserOf(nint disp, out string? path, out bool asked)
    {
        path = null; asked = false;
        nint sp = 0, sb = 0, sv = 0, fv = 0, pf = 0;
        Guid iidSp = IID_IServiceProvider, sid = SID_STopLevelBrowser, iidSb = IID_IShellBrowser, iidFv = IID_IFolderView, iidPf = IID_IPersistFolder2;
        try
        {
            if (((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)V(disp)[0])(disp, &iidSp, &sp) < 0 || sp == 0) return 0;
            if (((delegate* unmanaged[Stdcall]<nint, Guid*, Guid*, nint*, int>)V(sp)[3])(sp, &sid, &iidSb, &sb) < 0 || sb == 0) return 0;   // QueryService
            // 모든 단계가 성공해야 "물은 것". 어느 단계든 실패하면 모름(Unknown) — 새 창을 열지 않는다(Codex 20:13 RW-2).
            // 경로가 없는 것은 그 위치가 파일 시스템 폴더가 아님을 확인했을 때만 "다른 폴더"로 본다(이 PC·홈 같은 가상 위치).
            nint hwnd = 0;
            if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)V(sb)[3])(sb, &hwnd) < 0 || hwnd == 0) return 0;                         // IOleWindow::GetWindow
            // 창 목록에는 다른 프로그램 안에 든 웹 브라우저 컨트롤("Shell Embedding")도 나온다. 맨 위 창이 탐색기(CabinetWClass)가 아니면
            // 폴더 창이 아님을 확인한 것(0.3.14 실측: 보이지 않는 Shell Embedding 두 개가 모든 폴더 실행을 "확인 불가"로 막았다)
            nint root = GetAncestor(hwnd, 2 /* GA_ROOT */);
            char* cls = stackalloc char[64];
            int cn = GetClassNameW(root, cls, 64);
            if (cn > 0 && !new ReadOnlySpan<char>(cls, cn).SequenceEqual("CabinetWClass")) { asked = true; return 0; }
            // 숨은 탐색기 창(보이지 않게 열린 창)은 앞으로 가져올 수 없다 — 그 폴더의 창이 아닌 것으로 보고 새로 연다(0.3.27: 0.3.18–0.3.26 결함이 남긴 숨은 창)
            if (!IsWindowVisible(root)) { asked = true; return 0; }
            if (Program.TestFails("launch:folderstep")) return 0;
            if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)V(sb)[15])(sb, &sv) < 0 || sv == 0) return 0;                             // QueryActiveShellView
            if (((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)V(sv)[0])(sv, &iidFv, &fv) < 0 || fv == 0) return 0;
            if (((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)V(fv)[5])(fv, &iidPf, &pf) < 0 || pf == 0) return 0;               // IFolderView::GetFolder
            nint pidl = 0;
            if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)V(pf)[5])(pf, &pidl) < 0 || pidl == 0) return 0;                          // IPersistFolder2::GetCurFolder
            try
            {
                char* buf = stackalloc char[1024];
                if (SHGetPathFromIDListEx(pidl, buf, 1024, 0)) path = new string(buf);
                else
                {
                    // 경로를 못 받음: 파일 시스템 위치가 아니라고 확인될 때만 물은 것으로(SFGAO_FILESYSTEM 없음)
                    var sfi = new SHFILEINFOW();
                    uint attrs = 0x40000000; // SFGAO_FILESYSTEM
                    sfi.dwAttributes = attrs;
                    if (SHGetFileInfoW((char*)pidl, 0, ref sfi, (uint)sizeof(SHFILEINFOW), 0x8 /* PIDL */ | 0x800 /* ATTRIBUTES */ | 0x20000 /* ATTR_SPECIFIED */) == 0) return 0;
                    if ((sfi.dwAttributes & attrs) != 0) return 0;   // 파일 시스템인데 경로를 못 받음 = 모름
                }
            }
            finally { CoTaskMemFree(pidl); }
            asked = true;
            return GetAncestor(hwnd, 2 /* GA_ROOT */);
        }
        catch { return 0; }
        finally { Release(pf); Release(fv); Release(sv); Release(sb); Release(sp); }
    }

    // ------------------------------------------------------------------ 앞으로 가져오기

    /// <summary>찾은 창을 앞으로(UI 스레드에서 — 전경 권한은 입력을 받은 쪽에 있다). 거절되면 깜박임만, 새로 실행하지 않는다.</summary>
    public static Result Focus(nint h, bool isFolder, string path, string app = "")
    {
        // 바로 직전에 다시 확인: 같은 창이 아직 있고, 실행 파일(또는 폴더 창·앱 ID)이 그대로인가
        if (!IsWindow(h)) return Result.Failed;
        if (app.Length > 0)
        {
            GetWindowThreadProcessId(h, out uint apid);
            if (!string.Equals(AppIdOf(h, apid, out bool unk), app, StringComparison.OrdinalIgnoreCase) || unk) return Result.Failed;
        }
        else if (!isFolder)
        {
            GetWindowThreadProcessId(h, out uint pid);
            string? img = ImageOf(pid);
            if (img is null || Norm(img) != Norm(path)) return Result.Failed;
        }
        if (IsIconic(h)) ShowWindow(h, 9 /* SW_RESTORE */);
        SetForegroundWindow(h);
        if (GetForegroundWindow() == h) return Result.Focused;
        var fi = new FLASHWINFO { cbSize = (uint)sizeof(FLASHWINFO), hwnd = h, dwFlags = 3 | 0xC /* FLASHW_ALL | FLASHW_TIMERNOFG */, uCount = 3 };
        FlashWindowEx(ref fi);
        return Result.FocusRefused;   // 거절돼도 새로 실행하지 않는다
    }

    // ------------------------------------------------------------------ 실행

    /// <summary>1Key 가 보통 권한일 때: ShellExecuteEx 명시적 "open"(runas 없음).</summary>
    private static Result RunDirect(Target t, Job j)
    {
        if (Program.TestFails("launch:run")) return Result.Failed;
        if (Expired(j)) return j.Cancelled ? Result.Cancelled : Result.CannotVerify;   // 실행 호출 직전
        Interlocked.Increment(ref ExecCalls);
        if (Program.TestFails("launch:slowrun")) Thread.Sleep(3000);   // 시험: 실행 호출이 오래 걸림(그동안 새 요청은 Busy)
        fixed (char* verb = "open") fixed (char* file = t.Path) fixed (char* args = t.Args) fixed (char* dir = t.Dir)
        {
            var sei = new SHELLEXECUTEINFOW
            {
                cbSize = (uint)sizeof(SHELLEXECUTEINFOW), fMask = 0x00000100 /* SEE_MASK_NOASYNC */ | 0x00000400 /* SEE_MASK_FLAG_NO_UI */ | 0x00000040 /* SEE_MASK_NOCLOSEPROCESS */,
                // 앱은 셸의 기본 동작(앱 목록 항목에는 "open" 이 없을 수 있다).
                // 0.3.18–0.3.26 결함: 이 주석을 같은 줄 끝에 붙여 뒤의 인자·작업 폴더·nShow 가 주석이 되었다(nShow 0 = 숨김 — 보통 권한 1Key 의 창·폴더가 안 보임)
                lpVerb = t.App.Length > 0 ? null : verb, lpFile = file,
                lpParameters = t.Args.Length > 0 ? args : null, lpDirectory = t.Dir.Length > 0 ? dir : null, nShow = 1,
            };
            if (!ShellExecuteExW(ref sei)) return Result.Failed;
            if (sei.hProcess != 0)   // 감시가 이 프로세스의 자식을 그 실행의 프로세스로 본다(C28-1). 핸들은 감시가 쥐었다 닫는다(R29-1)
            {
                j.RootPid = GetProcessId(sei.hProcess);
                if (GetProcessTimes(sei.hProcess, out long created, out _, out _, out _)) j.RootCreated = created;
                j.RootHandle = sei.hProcess;
            }
            return Result.Opened;
        }
    }

    /// <summary>1Key 가 관리자 권한일 때: 같은 사용자·세션·Medium 의 Explorer 로 실행. 확인이 안 되면 실행하지 않는다.</summary>
    private static Result RunViaExplorer(Target t, Job j)
    {
        nint shell = GetShellWindow();
        if (shell == 0) return Result.ShellUnavailable;
        GetWindowThreadProcessId(shell, out uint shellPid);
        if (shellPid == 0 || !ShellIsOurUserMedium(shellPid)) return Result.ShellUnavailable;
        AllowSetForegroundWindow(shellPid);   // 실행된 창이 앞으로 올 수 있게
        return ExplorerExecute(t, shellPid, j);   // 작업 스레드(STA)에서 바로: 부작용 호출을 시간 제한으로 버리지 않는다
    }

    /// <summary>Explorer 프로세스가 1Key 와 같은 사용자 SID·같은 세션·무결성 Medium(상승 아님)인가.</summary>
    internal static bool ShellIsOurUserMedium(uint pid)
    {
        if (!ProcessIdToSessionId(pid, out uint s1) || !ProcessIdToSessionId((uint)Environment.ProcessId, out uint s2) || s1 != s2) return false;
        byte[]? shellSid = TokenUserSid(pid, out int integrity);
        byte[]? mySid = TokenUserSid((uint)Environment.ProcessId, out _);
        if (shellSid is null || mySid is null || !shellSid.AsSpan().SequenceEqual(mySid)) return false;
        return integrity == 0x2000;   // SECURITY_MANDATORY_MEDIUM_RID
    }

    private static byte[]? TokenUserSid(uint pid, out int integrity)
    {
        integrity = -1;
        nint ph = OpenProcess(0x1000, false, pid);
        if (ph == 0) return null;
        nint tok = 0;
        try
        {
            if (!OpenProcessToken(ph, 0x0008 /* TOKEN_QUERY */, out tok)) return null;
            byte* buf = stackalloc byte[512];
            if (GetTokenInformation(tok, 25 /* TokenIntegrityLevel */, buf, 512, out _))
            {
                nint sid = *(nint*)buf;   // TOKEN_MANDATORY_LABEL.Label.Sid
                byte n = *GetSidSubAuthorityCount(sid);
                integrity = (int)*GetSidSubAuthority(sid, (uint)(n - 1));
            }
            if (!GetTokenInformation(tok, 1 /* TokenUser */, buf, 512, out _)) return null;
            nint userSid = *(nint*)buf;
            int len = GetLengthSid(userSid);
            var r = new byte[len];
            Marshal.Copy(userSid, r, 0, len);
            return r;
        }
        finally { if (tok != 0) CloseHandle(tok); CloseHandle(ph); }
    }

    private static Result ExplorerExecute(Target t, uint shellPid, Job j)
    {
        nint sw = 0, disp = 0, sp = 0, sb = 0, sv = 0, bg = 0, fvd = 0, app = 0, sd = 0;
        Guid clsid = CLSID_ShellWindows, iid = IID_IShellWindows, iidSp = IID_IServiceProvider, sid = SID_STopLevelBrowser, iidSb = IID_IShellBrowser,
             iidDisp = IID_IDispatch, iidFvd = IID_IShellFolderViewDual, iidSd = IID_IShellDispatch2;
        nint bFile = 0, bOp = 0, bArgs = 0, bDir = 0;
        try
        {
            if (CoCreateInstance(ref clsid, 0, 4, ref iid, out sw) < 0 || sw == 0) return Result.ShellUnavailable;
            var loc = new VARIANT { vt = 3, i4 = 0 /* CSIDL_DESKTOP */ };
            var root = new VARIANT();
            int hwnd = 0;
            if (((delegate* unmanaged[Stdcall]<nint, VARIANT*, VARIANT*, int, int*, int, nint*, int>)V(sw)[15])(sw, &loc, &root, 8 /* SWC_DESKTOP */, &hwnd, 1 /* SWFO_NEEDDISPATCH */, &disp) < 0 || disp == 0)
                return Result.ShellUnavailable;
            // 사전에 확인한 셸과 같은 Explorer 인가(L2-4)
            GetWindowThreadProcessId((nint)hwnd, out uint pid);
            if (pid != shellPid) return Result.ShellUnavailable;
            if (((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)V(disp)[0])(disp, &iidSp, &sp) < 0 || sp == 0) return Result.ShellUnavailable;
            if (((delegate* unmanaged[Stdcall]<nint, Guid*, Guid*, nint*, int>)V(sp)[3])(sp, &sid, &iidSb, &sb) < 0 || sb == 0) return Result.ShellUnavailable;
            if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)V(sb)[15])(sb, &sv) < 0 || sv == 0) return Result.ShellUnavailable;
            if (((delegate* unmanaged[Stdcall]<nint, uint, Guid*, nint*, int>)V(sv)[15])(sv, 0 /* SVGIO_BACKGROUND */, &iidDisp, &bg) < 0 || bg == 0) return Result.ShellUnavailable;   // GetItemObject
            if (((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)V(bg)[0])(bg, &iidFvd, &fvd) < 0 || fvd == 0) return Result.ShellUnavailable;
            if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)V(fvd)[7])(fvd, &app) < 0 || app == 0) return Result.ShellUnavailable;                // get_Application
            if (((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)V(app)[0])(app, &iidSd, &sd) < 0 || sd == 0) return Result.ShellUnavailable;
            fixed (char* f = t.Path) bFile = SysAllocString(f);
            fixed (char* o = t.App.Length > 0 ? "" : "open") bOp = SysAllocString(o);   // 앱: 빈 동작 = 기본
            fixed (char* a = t.Args) bArgs = SysAllocString(a);
            fixed (char* d = t.Dir) bDir = SysAllocString(d);
            var vArgs = new VARIANT { vt = 8 /* VT_BSTR */, p = bArgs };
            var vDir = new VARIANT { vt = 8, p = bDir };
            var vOp = new VARIANT { vt = 8, p = bOp };
            var vShow = new VARIANT { vt = 3, i4 = 1 /* SW_SHOWNORMAL */ };
            if (Expired(j)) return j.Cancelled ? Result.Cancelled : Result.CannotVerify;   // 실행 호출 직전
            Interlocked.Increment(ref ExecCalls);
            if (Program.TestFails("launch:slowrun")) Thread.Sleep(3000);
            int hr = ((delegate* unmanaged[Stdcall]<nint, nint, VARIANT, VARIANT, VARIANT, VARIANT, int>)V(sd)[31])(sd, bFile, vArgs, vDir, vOp, vShow);   // IShellDispatch2::ShellExecute
            return hr >= 0 ? Result.Opened : Result.Failed;
        }
        catch { return Result.ShellUnavailable; }
        finally
        {
            if (bFile != 0) SysFreeString(bFile); if (bOp != 0) SysFreeString(bOp); if (bArgs != 0) SysFreeString(bArgs); if (bDir != 0) SysFreeString(bDir);
            Release(sd); Release(app); Release(fvd); Release(bg); Release(sv); Release(sb); Release(sp); Release(disp); Release(sw);
        }
    }

    /// <summary>1Key 가 관리자 권한인가. 확인하지 못하면 null — 보통 권한으로 보지 않는다(Codex R34-4).</summary>
    internal static bool? ElevationState()
    {
        if (Program.TestFails("launch:elevation")) return null;
        if (!OpenProcessToken(GetCurrentProcess(), 0x0008, out nint tok)) return null;
        try { int e = 0; return GetTokenInformation(tok, 20 /* TokenElevation */, &e, 4, out _) ? e != 0 : null; }
        finally { CloseHandle(tok); }
    }

    // ------------------------------------------------------------------ 실행 뒤 창 확인

    /// <summary>감시가 본 창 하나(UI 가 앞으로 가져오기·기억 전에 지금 상태와 다시 견준다, Codex C28-2).</summary>
    internal sealed record SeenInfo(int No, nint Hwnd, uint Pid, string Image, string App, bool Related);
    private static readonly System.Collections.Concurrent.ConcurrentQueue<SeenInfo> _seen = new();

    /// <summary>감시 번호·창이 같은 관찰을 꺼낸다(UI). 없으면 null. 같은 감시의 다른 관찰은 남긴다.</summary>
    public static SeenInfo? TakeSeen(int no, nint hwnd)
    {
        SeenInfo? hit = null;
        int n = _seen.Count;
        for (int k = 0; k < n && _seen.TryDequeue(out var s); k++)
        {
            if (hit is null && s.No == no && s.Hwnd == hwnd) hit = s;
            else _seen.Enqueue(s);   // 다른 관찰은 되돌린다
        }
        return hit;
    }

    /// <summary>끝난 감시의 남은 관찰을 버린다(UI 가 감시 번호를 정리할 때).</summary>
    public static void DropSeen(int no)
    {
        int n = _seen.Count;
        for (int k = 0; k < n && _seen.TryDequeue(out var s); k++) if (s.No != no) _seen.Enqueue(s);
    }

    /// <summary>관찰한 창이 지금도 같은 창인가: 창이 있고, 같은 프로세스이고, 실행 파일(또는 앱 ID)이 그대로(핸들 재사용·교체 방지).</summary>
    public static bool StillSame(SeenInfo s)
    {
        if (!IsWindow(s.Hwnd)) return false;
        GetWindowThreadProcessId(s.Hwnd, out uint pid);
        if (pid != s.Pid) return false;
        if (s.App.Length > 0) { string? id = AppIdOf(s.Hwnd, pid, out bool unk); return SeenSame(s, true, pid, null, id, unk); }
        return SeenSame(s, true, pid, ImageOf(pid), null, false);
    }

    /// <summary>StillSame 의 판단(지금 값을 넣어 — 단위 시험 OB01–): 창이 살아 있고 같은 프로세스이고, 앱이면 앱 ID 가 확실히 같고, 아니면 실행 파일이 같다.</summary>
    internal static bool SeenSame(SeenInfo s, bool alive, uint pidNow, string? imageNow, string? appNow, bool appUnknown)
    {
        if (!alive || pidNow != s.Pid) return false;
        if (s.App.Length > 0) return !appUnknown && string.Equals(appNow, s.App, StringComparison.OrdinalIgnoreCase);
        return imageNow is not null && Norm(imageNow) == Norm(s.Image);
    }

    /// <summary>감시 관찰을 받은 UI 가 할 일(Codex C28-2).</summary>
    internal enum SeenStep { Ignore, Keep, Front, Flash }

    /// <summary>
    /// 감시 관찰 처리 판단(UI 상태를 넣어 — 단위 시험 OB01–). Ignore = 옛 감시(같은 대상의 더 새 요청이 있음)·지운 항목·바뀐 창: 기억도 하지 않는다.
    /// Keep = 기억만(이 요청에서 이미 한 번 앞으로 가져왔다·요청 뒤 1분이 지났다·이미 맨 앞). Front = 앞으로(사용자가 누를 때의 창·1Key·바탕 화면에
    /// 그대로 있음). Flash = 깜박이기만(사용자가 다른 창으로 옮겨 일하는 중).
    /// </summary>
    internal static SeenStep SeenPlan(long watchAt, long? latestAt, bool itemExists, bool same, bool firstFront, long nowTick, nint fg, nint hwnd, nint fgAtPress, bool fgOwnOrDesktop)
    {
        if (latestAt != watchAt || !itemExists || !same) return SeenStep.Ignore;
        if (!firstFront || nowTick - watchAt > 60_000 || fg == hwnd) return SeenStep.Keep;
        return fg == 0 || fg == fgAtPress || fgOwnOrDesktop ? SeenStep.Front : SeenStep.Flash;
    }

    /// <summary>
    /// 실행을 요청한 뒤 창이 나타나는지 백그라운드에서 본다(WatchMs 동안 — 처음 5초는 0.25초마다, 그 뒤 1초마다). 읽기만 한다.
    /// - 폴더: 그 폴더 창이 보이면 notify 에 msg(wParam = no, lParam = 창)를 한 번 보내고 끝.
    /// - 프로그램·앱: 실행 직전(before)에 없던 창 가운데 그 대상의 창을 새로 뜰 때마다 알린다(최대 4개, 관찰은 TakeSeen 으로).
    ///   그 대상의 창 = 같은 실행 파일·같은 앱 ID, 또는 **이 실행이 만든 프로세스의 자식·손자**(실행기형: 업데이트 실행기가 UAC 를 거쳐 관리자 권한
    ///   본 프로그램을 띄우는 경우 — Windows 는 올린 프로세스의 부모를 요청한 프로세스로 기록한다). 같은 폴더라는 것만으로는 보지 않는다(Codex C28-1).
    ///   이 실행의 프로세스 = 셸이 돌려준 프로세스(rootPid), 또는 요청 시각(since) 뒤에 생긴 그 실행 파일의 프로세스. 자식은 부모보다 늦게 생겼어야 한다.
    /// 끝나면 lParam = 0 으로 한 번 더(UI 가 감시 번호를 정리). 동시에 WatchMax 개까지, 넘으면 false(감시 없이 둔다).
    /// rootHandle(셸이 돌려준 프로세스 핸들, 0 = 없음)은 넘겨받는다: 감시 동안 쥐고(그 PID 가 재사용되지 않게) 끝나면 닫는다.
    /// </summary>
    public static bool Watch(int no, bool isFolder, string path, string app, HashSet<nint>? before, uint rootPid, long rootCreated, nint rootHandle, long since, nint notify, uint msg)
    {
        if (Interlocked.Increment(ref _watching) > WatchMax) { Interlocked.Decrement(ref _watching); if (rootHandle != 0) CloseHandle(rootHandle); return false; }
        var seenSet = before is null ? new HashSet<nint>() : new HashSet<nint>(before);
        Lineage? tree = null;
        if (isFolder || app.Length > 0) { if (rootHandle != 0) CloseHandle(rootHandle); }
        else tree = new Lineage(path, rootPid, rootCreated, rootHandle, since);
        var t = new Thread(() =>
        {
            int hr = CoInitializeEx(0, 2);
            int posted = 0;
            try
            {
                long start = Environment.TickCount64, end = start + WatchMs;
                while (Environment.TickCount64 < end && posted < 4)
                {
                    Thread.Sleep(Environment.TickCount64 - start < 5000 ? 250 : 1000);
                    if (isFolder)
                    {
                        nint fh = FindFolderWindow(path).hwnd;
                        if (fh != 0) { Native.PostMessageW(notify, msg, no, fh); posted++; break; }
                        continue;
                    }
                    tree?.Refresh();
                    foreach (SeenInfo si in NewWindowsOf(no, path, app, tree, seenSet))
                    {
                        _seen.Enqueue(si);
                        Native.PostMessageW(notify, msg, no, si.Hwnd);
                        if (++posted >= 4) break;
                    }
                }
            }
            catch { }
            finally
            {
                tree?.Dispose();
                if (hr >= 0) CoUninitialize();
                Interlocked.Decrement(ref _watching);
                Native.PostMessageW(notify, msg, no, 0);
            }
        }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return true;
    }

    /// <summary>지금 보이는 후보 창들(실행 직전 기록).</summary>
    internal static HashSet<nint> Snapshot()
    {
        var r = new HashSet<nint>();
        for (nint h = GetTopWindow(0); h != 0; h = GetWindow(h, 2)) if (IsCandidate(h)) r.Add(h);
        return r;
    }

    /// <summary>seen 에 없던 후보 창 가운데 그 대상의 창(찾은 것은 seen 에 더한다).</summary>
    private static List<SeenInfo> NewWindowsOf(int no, string path, string app, Lineage? tree, HashSet<nint> seen)
    {
        var r = new List<SeenInfo>();
        string want = Norm(path);
        for (nint h = GetTopWindow(0); h != 0; h = GetWindow(h, 2))
        {
            if (seen.Contains(h) || !IsCandidate(h)) continue;
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == 0 || pid == (uint)Environment.ProcessId) continue;
            if (app.Length > 0)
            {
                string? id = AppIdOf(h, pid, out bool unknown);
                if (unknown || !string.Equals(id, app, StringComparison.OrdinalIgnoreCase)) continue;   // 모르는 창은 다음 회차에 다시
                seen.Add(h); r.Add(new SeenInfo(no, h, pid, "", app, false));
                continue;
            }
            string? img = ImageOf(pid);
            if (img is null) continue;   // 볼 수 없는 창(다른 권한): 다음 회차에 다시
            bool own = Norm(img) == want, child = !own && tree is not null && tree.IsDescendant(pid, CreatedAt(pid));
            if (!own && !child) continue;
            seen.Add(h);
            r.Add(new SeenInfo(no, h, pid, img, "", child));
        }
        return r;
    }

    /// <summary>
    /// 한 실행이 만든 프로세스들(Codex C28-1·R29-1 — 판단은 <see cref="LaunchLineage"/>). 여기서는 실제 스냅샷·생성 시각·실행 파일을 읽고,
    /// 구성원 프로세스의 핸들을 감시 동안 쥔다(쥐고 있는 동안 그 PID 는 다른 프로세스에 재사용되지 않는다 — 실행기가 곧 끝나도 그 PID 로
    /// 기록된 자식을 확실히 잇는다). 셸이 돌려준 핸들(rootHandle)은 넘겨받아 같이 닫는다.
    /// 2초 여유(요청 시각 −2초 뒤에 생긴 같은 실행 파일을 뿌리 후보로)는 관리자 1Key 의 탐색기 경로를 위한 지원 휴리스틱이다 — 후보가 둘이면 모름.
    /// </summary>
    private sealed class Lineage : IDisposable
    {
        private readonly LaunchLineage _core;
        private readonly List<nint> _held = new();

        public Lineage(string exe, uint rootPid, long rootCreated, nint rootHandle, long since)
        {
            if (rootHandle != 0) _held.Add(rootHandle);
            _core = new LaunchLineage(Norm(exe), Path.GetFileName(exe), since - 20_000_000, rootPid, rootCreated, rootHandle != 0 && rootCreated != 0,
                DateTime.UtcNow.ToFileTimeUtc());
        }

        public bool IsDescendant(uint pid, long? createdNow) => _core.IsDescendant(pid, createdNow);

        public void Refresh()
        {
            if (_core.Ambiguous) return;
            var procs = new List<LaunchLineage.Proc>();
            nint snap = CreateToolhelp32Snapshot(2 /* TH32CS_SNAPPROCESS */, 0);
            if (snap == -1 || snap == 0) return;
            try
            {
                var e = new PROCESSENTRY32W { dwSize = (uint)sizeof(PROCESSENTRY32W) };
                for (bool ok = Process32FirstW(snap, ref e); ok; ok = Process32NextW(snap, ref e))
                    procs.Add(new LaunchLineage.Proc(e.th32ProcessID, e.th32ParentProcessID, new string(e.szExeFile)));
            }
            finally { CloseHandle(snap); }
            long now = DateTime.UtcNow.ToFileTimeUtc();   // 스냅샷 뒤, 생성 시각을 읽기 전
            var created = new Dictionary<uint, long?>();
            _core.Update(now, procs,
                pid => created.TryGetValue(pid, out long? c) ? c : created[pid] = CreatedAt(pid),
                pid => ImageOf(pid) is string img ? Norm(img) : null,
                Pin);
        }

        /// <summary>그 PID 의 핸들을 열어 생성 시각이 같으면(같은 신원) 감시 동안 쥔다.</summary>
        private bool Pin(uint pid, long created)
        {
            if (_held.Count >= LaunchLineage.MaxMembers) return false;
            nint ph = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
            if (ph == 0) return false;
            if (GetProcessTimes(ph, out long c, out _, out _, out _) && c == created) { _held.Add(ph); return true; }
            CloseHandle(ph);
            return false;
        }

        public void Dispose()
        {
            foreach (nint h in _held) CloseHandle(h);
            _held.Clear();
        }
    }

    /// <summary>프로세스 생성 시각(FILETIME). 열지 못하면 null.</summary>
    private static long? CreatedAt(uint pid)
    {
        nint ph = OpenProcess(0x1000, false, pid);
        if (ph == 0) return null;
        try { return GetProcessTimes(ph, out long created, out _, out _, out _) ? created : null; }
        finally { CloseHandle(ph); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32W
    {
        public uint dwSize, cntUsage, th32ProcessID; public nuint th32DefaultHeapID; public uint th32ModuleID, cntThreads, th32ParentProcessID; public int pcPriClassBase; public uint dwFlags;
        public fixed char szExeFile[260];
    }

    /// <summary>
    /// 실행 뒤 새로 뜬 그 대상의 창이 뒤에 있을 때 앞으로(UI 스레드, 부르는 쪽이 신원·요청·사용자 위치를 이미 다시 확인했다 — C28-2).
    /// UAC 를 거쳐 뜬 관리자 프로그램은 Windows 가 앞으로 오지 못하게 막으므로, 보통 방법이 거절되면 지금 앞 창의 입력 큐에 잠시 붙어서 한 번 더
    /// (2026-10-05 사용자 결정 "조건을 좁혀 유지": 눌린 키가 하나도 없을 때만, 키는 보내지 않고 붙었다 바로 뗀다). 그래도 안 되면 깜박임.
    /// </summary>
    public static bool BringForward(nint h)
    {
        if (!IsWindow(h)) return false;
        if (IsIconic(h)) ShowWindow(h, 9 /* SW_RESTORE */);
        if (SetForegroundWindow(h) && GetForegroundWindow() == h) return true;
        nint fg = GetForegroundWindow();
        uint fgTid = fg != 0 ? GetWindowThreadProcessId(fg, out _) : 0, me = Native.GetCurrentThreadId();
        if (fgTid != 0 && fgTid != me && !AnyKeyDown() && Native.AttachThreadInput(me, fgTid, true))
        {
            try { BringWindowToTop(h); SetForegroundWindow(h); }
            finally { Native.AttachThreadInput(me, fgTid, false); }
        }
        if (GetForegroundWindow() == h) return true;
        var fi = new FLASHWINFO { cbSize = (uint)sizeof(FLASHWINFO), hwnd = h, dwFlags = 3 | 0xC, uCount = 3 };
        FlashWindowEx(ref fi);
        return false;
    }

    /// <summary>작업 표시줄에서 깜박이기만(앞으로 가져오지 않는다).</summary>
    public static void Flash(nint h)
    {
        if (!IsWindow(h)) return;
        var fi = new FLASHWINFO { cbSize = (uint)sizeof(FLASHWINFO), hwnd = h, dwFlags = 3 | 0xC, uCount = 3 };
        FlashWindowEx(ref fi);
    }

    /// <summary>지금 눌려 있는 키·마우스 단추가 있나(입력 큐에 붙기 전 확인 — 사용자가 누르고 있는 수정 키 등의 상태를 건드리지 않게).</summary>
    private static bool AnyKeyDown()
    {
        for (int vk = 1; vk < 255; vk++) if ((GetAsyncKeyState(vk) & 0x8000) != 0) return true;
        return false;
    }

    /// <summary>창의 실행 파일 경로(조회하지 못하면 null).</summary>
    internal static string? ImageOfWindow(nint h)
    {
        GetWindowThreadProcessId(h, out uint pid);
        return pid == 0 ? null : ImageOf(pid);
    }

    private const int WatchMs = 60000, WatchMax = 16;   // 0.3.18: 창을 본 뒤에도 1분 동안 지켜보므로(실행기형) 동시 개수를 넉넉히
    private static int _watching;

    // ------------------------------------------------------------------ COM 도우미

    private static T OnComThread<T>(Func<T> work, T fallback)
    {
        T result = fallback;
        var t = new Thread(() =>
        {
            int hr = CoInitializeEx(0, 2 /* COINIT_APARTMENTTHREADED */);
            try { result = work(); }
            catch { result = fallback; }
            finally { if (hr >= 0) CoUninitialize(); }
        }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return t.Join(8000) ? result : fallback;
    }

    private static nint* V(nint p) => *(nint**)p;
    private static void Release(nint p) { if (p != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)V(p)[2])(p); }

    // ------------------------------------------------------------------ P/Invoke

    [StructLayout(LayoutKind.Sequential)]
    private struct SHELLEXECUTEINFOW
    {
        public uint cbSize, fMask; public nint hwnd; public char* lpVerb, lpFile, lpParameters, lpDirectory; public int nShow; public nint hInstApp, lpIDList; public char* lpClass;
        public nint hkeyClass; public uint dwHotKey; public nint hIconOrMonitor, hProcess;
    }
    [StructLayout(LayoutKind.Sequential)] private struct FLASHWINFO { public uint cbSize; public nint hwnd; public uint dwFlags, uCount, dwTimeout; }

    [DllImport("shell32.dll")] private static extern bool ShellExecuteExW(ref SHELLEXECUTEINFOW sei);
    [DllImport("shell32.dll")] private static extern bool SHGetPathFromIDListEx(nint pidl, char* path, int cch, uint flags);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFOW { public nint hIcon; public int iIcon; public uint dwAttributes; public fixed char szDisplayName[260]; public fixed char szTypeName[80]; }
    [DllImport("shell32.dll")] private static extern nint SHGetFileInfoW(char* path, uint attr, ref SHFILEINFOW sfi, uint size, uint flags);
    [DllImport("ole32.dll")] private static extern void CoTaskMemFree(nint p);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint coinit);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, nint outer, uint ctx, ref Guid iid, out nint obj);
    [DllImport("oleaut32.dll")] private static extern nint SysAllocString(char* s);
    [DllImport("oleaut32.dll")] private static extern void SysFreeString(nint s);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint LoadLibraryExW(char* path, nint file, uint flags);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(nint mod);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint FindResourceW(nint mod, nint name, nint type);
    [DllImport("kernel32.dll")] private static extern uint SizeofResource(nint mod, nint res);
    [DllImport("kernel32.dll")] private static extern nint LoadResource(nint mod, nint res);
    [DllImport("kernel32.dll")] private static extern void* LockResource(nint h);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint CreateFileW(char* name, uint access, uint share, nint sa, uint disp, uint flags, nint tmpl);
    [DllImport("kernel32.dll")] private static extern uint GetFinalPathNameByHandleW(nint h, char* buf, uint cch, uint flags);
    [DllImport("kernel32.dll")] private static extern uint GetDriveTypeW(char* root);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint h);
    [DllImport("kernel32.dll")] private static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern bool QueryFullProcessImageNameW(nint ph, uint flags, char* buf, uint* size);
    [DllImport("kernel32.dll")] private static extern bool ProcessIdToSessionId(uint pid, out uint session);
    [DllImport("advapi32.dll")] private static extern bool OpenProcessToken(nint ph, uint access, out nint tok);
    [DllImport("advapi32.dll")] private static extern bool GetTokenInformation(nint tok, int cls, void* info, uint len, out uint ret);
    [DllImport("advapi32.dll")] private static extern byte* GetSidSubAuthorityCount(nint sid);
    [DllImport("advapi32.dll")] private static extern uint* GetSidSubAuthority(nint sid, uint n);
    [DllImport("advapi32.dll")] private static extern int GetLengthSid(nint sid);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint h, out RECT r);
    [DllImport("user32.dll")] private static extern nint GetTopWindow(nint h);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint h, uint cmd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint h);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint h);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint h, int cmd);
    [DllImport("user32.dll")] private static extern long GetWindowLongPtrW(nint h, int idx);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint h, out uint pid);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint h);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint pid);
    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint h, uint flags);
    [DllImport("user32.dll")] private static extern int GetClassNameW(nint h, char* name, int max);
    [DllImport("user32.dll")] private static extern bool FlashWindowEx(ref FLASHWINFO fi);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(nint h);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint h, uint attr, void* value, uint size);
    [DllImport("kernel32.dll")] private static extern int GetApplicationUserModelId(nint ph, uint* len, char* buf);
    [DllImport("shell32.dll")] private static extern int SHGetPropertyStoreForWindow(nint h, Guid* iid, nint* ps);
    [DllImport("shell32.dll")] private static extern int SHCreateItemFromParsingName(char* name, nint bc, Guid* iid, nint* item);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(PROPVARIANT* v);
    [DllImport("kernel32.dll")] private static extern uint GetProcessId(nint ph);
    [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(nint ph, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll")] private static extern bool Process32FirstW(nint snap, ref PROCESSENTRY32W e);
    [DllImport("kernel32.dll")] private static extern bool Process32NextW(nint snap, ref PROCESSENTRY32W e);
    [DllImport("kernel32.dll")] private static extern int GetPackagesByPackageFamily(char* family, uint* count, nint* names, uint* bufLen, char* buf);
    [DllImport("kernel32.dll")] private static extern int GetPackagePathByFullName(char* fullName, uint* len, char* path);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
}
