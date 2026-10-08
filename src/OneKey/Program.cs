namespace OneKey;

internal static unsafe class Program
{
    private const string MutexBase = @"Local\1Key.SingleInstance.v1";
    private const string WindowClassBase = "OneKeyMainWindow";

    /// <summary>
    /// 검증용 접미사. ONEKEY_TEST=1 이면서 ONEKEY_INSTANCE_SUFFIX 가 있을 때만 붙는다.
    /// 실제 사용자의 1Key 와 겹치지 않는 별도 이름으로 두 인스턴스를 띄워 시험하기 위한 것이다.
    /// </summary>
    public static readonly bool IsTestMode = Environment.GetEnvironmentVariable("ONEKEY_TEST") == "1";
    public static readonly string InstanceSuffix =
        IsTestMode ? Environment.GetEnvironmentVariable("ONEKEY_INSTANCE_SUFFIX") ?? string.Empty : string.Empty;

    /// <summary>
    /// 검증용 실패 주입(ONEKEY_TEST=1 일 때만). ONEKEY_TEST_FAIL 에 쉼표로 나열한 지점에서 실패를 흉내 낸다.
    /// 지점 목록은 쓰는 곳에 적는다(Injector: clip:*, Backdrop: backdrop:*, Dialog: dialog:*).
    /// </summary>
    private static readonly string[] _testFail = IsTestMode
        ? (Environment.GetEnvironmentVariable("ONEKEY_TEST_FAIL") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        : Array.Empty<string>();
    public static bool TestFails(string point) => _testFail.Length > 0 && Array.IndexOf(_testFail, point) >= 0;

    private static string MutexName => MutexBase + InstanceSuffix;

    /// <summary>메인 창의 윈도우 클래스 이름.</summary>
    public static string WindowClass => WindowClassBase + InstanceSuffix;

    private static nint _mutex;
    private static bool _skipSingle;

    /// <summary>이번 실행에서 예전 버전의 자리를 이어받았는가(자동 실행을 새 경로로 다시 등록해야 한다).</summary>
    public static bool TookOver { get; private set; }

    [STAThread]
    private static int Main(string[] args)
    {
        // 검증 전용(ONEKEY_TEST=1): 클립보드 교체·복원만 실행하고 끝낸다. 키 입력은 보내지 않는다 (T17).
        if (IsTestMode && args.Length >= 2 && args[0] == "--selftest-clip")
        {
            int code = Injector.SelfTestClipboard(args[1], out string? msg);
            // 하네스가 "앱의 마지막 쓰기 뒤 누가 클립보드를 바꿨는지" 알 수 있도록, 끝낸 직후의 일련번호를 함께 남긴다 (V37-2)
            uint seqAfter = Native.GetClipboardSequenceNumber();
            string? dir = Environment.GetEnvironmentVariable("ONEKEY_CONFIG_DIR");
            if (!string.IsNullOrEmpty(dir))
                try { File.WriteAllText(Path.Combine(dir, "selftest-clip.txt"), $"{code}|{seqAfter}|{msg}"); } catch { }
            return code;
        }

        // 검증 전용(ONEKEY_TEST=1): 자동 실행 바로 가기(0.2.149). 시험 폴더(ONEKEY_TEST_STARTUP_DIR, 없으면 설정 폴더 아래)만 쓴다.
        //   --selftest-apply on|off  : 실제 Apply 경로 → selftest-apply.txt ("OK" 또는 실패 문구)
        //   --selftest-state         : 지금 상태 → selftest-state.txt ("링크|승인|예전|폴더오류|바로 가기 경로")
        //   --selftest-writelink <대상> <인자> : 시험 폴더에 바로 가기를 그대로 쓴다(다른 대상·인자 준비)
        if (IsTestMode && args.Length >= 1 && args[0] is "--selftest-apply" or "--selftest-state" or "--selftest-writelink")
        {
            string r = args[0] switch
            {
                "--selftest-apply" when args.Length >= 2 => Autostart.SelfTestApply(args[1] == "on"),
                "--selftest-state" => Autostart.SelfTestState(),
                "--selftest-writelink" when args.Length >= 3 => Autostart.SelfTestWriteLink(args[1], args[2]) ? "OK" : "FAIL",
                _ => "BADARGS",
            };
            string? dir = Environment.GetEnvironmentVariable("ONEKEY_CONFIG_DIR");
            if (!string.IsNullOrEmpty(dir))
                try { File.WriteAllText(Path.Combine(dir, args[0][2..] + ".txt"), r); } catch { }
            return 0;
        }

        // 검증 전용(ONEKEY_TEST=1): 본창 바탕 그림의 실제 픽셀 대비(Codex R166-C1 3). --selftest-background <w> <h> <dpi> → selftest-background.txt
        if (IsTestMode && args.Length >= 4 && args[0] == "--selftest-background" && int.TryParse(args[1], out int bw) && int.TryParse(args[2], out int bh) && uint.TryParse(args[3], out uint bd))
        {
            string r = Theme.SelfTestBackground(bw, bh, bd);
            string? dir = Environment.GetEnvironmentVariable("ONEKEY_CONFIG_DIR");
            if (!string.IsNullOrEmpty(dir)) try { File.WriteAllText(Path.Combine(dir, "selftest-background.txt"), r); } catch { }
            return 0;
        }

        // 검증 전용(ONEKEY_TEST=1): 사이트 채우기의 순수한 부분(주소 정규화·입력란 맞추기·암호 블록 v6)을 창 없이 시험한다.
        if (IsTestMode && args.Length >= 3 && args[0] == "--walker-dump")   // 시험: 작업 표시줄 마스코트 그림을 PNG 로(모양 확인용)
        {
            Gdiplus.Init();
            return CatWidget.DumpForTest(args[1], int.TryParse(args[2], out int wd) ? wd : 96) ? 0 : 3;
        }
        if (IsTestMode && args.Length >= 2 && args[0] == "--clock-dump")   // 시험: 정시 알림 플립시계 그림을 PNG 로(밝음·어두움, 넘어가는 단계별)
            return FlipClock.DumpForTest(args[1]) ? 0 : 3;
        if (IsTestMode && args.Length >= 1 && args[0] == "--lockwidget-demo")
        {
            // 디자인 개편 B 잠금 위젯 기술 검증(1단계, Codex 13:28): 잠금 경로와 무관한 데모. 인자: create(처음 설정) / light / dark
            Theme.InitProcessDarkMode();
            if (args.Contains("light")) Theme.Mode = 1; else if (args.Contains("dark")) Theme.Mode = 2;
            Theme.Detect();
            Gdiplus.Init();
            Theme.CreateFonts(Native.GetDpiForSystem() is var dd && dd != 0 ? dd : 96);
            // 그리기 실패 주입: fail=N 이면 N 번째 그리기가 실패(1 = 처음부터 → 종료 코드 3, 그 뒤 = 보이는 중 → 위젯을 닫고 종료 코드 4)
            if (args.FirstOrDefault(a => a.StartsWith("fail=")) is string fa && int.TryParse(fa.AsSpan(5), out int fn)) LockWidget.TestFailAt = fn;
            // 자원 누수 확인: cycle=N 이면 보였다 숨기기 N회(만들고 지우기) 뒤 다시 보인다
            int cycles = args.FirstOrDefault(a => a.StartsWith("cycle=")) is string cy && int.TryParse(cy.AsSpan(6), out int cn) ? cn : 0;
            for (int c = 0; c < cycles; c++)
                {
                    if (!LockWidget.Show(0, (c & 1) == 1, activate: false)) return 3;
                    while (Native.PeekMessageW(out Native.MSG pm, 0, 0, 0, 1)) { Native.TranslateMessage(ref pm); Native.DispatchMessageW(ref pm); }
                    LockWidget.Hide();
                }
            if (!LockWidget.Show(0, args.Contains("create"), activate: true)) return 3;
            Native.MSG m;
            while (Native.GetMessageW(out m, 0, 0, 0) > 0) { Native.TranslateMessage(ref m); Native.DispatchMessageW(ref m); }
            return (int)m.wParam;   // WM_QUIT 의 코드(데모: 0 = × 로 닫음, 4 = 그리기 실패로 닫힘)
        }
        if (IsTestMode && args.Length >= 1 && args[0] == "--selftest-site")
        {
            string r = SiteSelfTest.Run();
            string? dir = Environment.GetEnvironmentVariable("ONEKEY_CONFIG_DIR");
            if (!string.IsNullOrEmpty(dir))
                try { File.WriteAllText(Path.Combine(dir, "selftest-site.txt"), r); } catch { }
            return 0;
        }
        // 검증 전용(ONEKEY_TEST=1): 독립 구현과의 교차 비교(tools/tests/kdfcross) — 입력 파일의 경우마다 BLAKE2b·Argon2id 결과를 적는다
        if (IsTestMode && args.Length >= 3 && args[0] == "--selftest-kdf")
            return SiteSelfTest.KdfVectors(args[1], args[2]);

        bool fromAutostart = args.Any(a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase));

        // 승격 재시작으로 넘어온 경우에도 "이어받음" 상태를 잃지 않도록 인자로 전달받는다.
        if (args.Any(a => string.Equals(a, "--took-over", StringComparison.OrdinalIgnoreCase)))
            TookOver = true;

        // 개발/검증용: 단일 인스턴스 검사를 건너뛴다(평상시엔 설정하지 않는다).
        // ONEKEY_TEST=1 은 단일 인스턴스 검사를 건너뛴다. 단, 접미사를 함께 준 경우에는
        // 그 이름 공간 안에서 단일 인스턴스 동작을 그대로 시험할 수 있도록 검사를 유지한다.
        _skipSingle = Environment.GetEnvironmentVariable("ONEKEY_TEST") == "1" && InstanceSuffix.Length == 0;

        // 관리자 권한 재시작으로 넘어왔다면(T2) 부모가 끝나기를 먼저 기다린다. 끝나지 않으면 시작하지 않는다(둘이 동시에 돌지 않도록).
        uint handoff = ParseHandoff(args);
        if (handoff != 0 && !_skipSingle && !WaitForHandoffParent(handoff))
        {
            Notice(
                () => T.ProgHandoffTimeout,
                () => T.ProgHandoffTitle,
                Native.MB_OK | Native.MB_ICONWARNING | Native.MB_SETFOREGROUND);
            return 0;
        }

        // 이미 실행 중이면 그 창을 띄우고 끝낸다.
        if (!_skipSingle)
        {
            fixed (char* name = MutexName)
            {
                _mutex = Native.CreateMutexW(0, true, name);
                int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                // 183 = 이미 있음. 5 = 관리자 권한 인스턴스가 만든 뮤텍스는 일반 권한에서 열 수 없다 → 역시 실행 중이다.
                if ((_mutex != 0 && err == 183) || (_mutex == 0 && err == 5))
                {
                    // 이미 실행 중이다. 그게 예전 버전이면 끝내고 자리를 이어받는다.
                    if (!TakeOverOldVersion()) return 0;
                }
            }
        }

        return new App().Run(fromAutostart);
    }

    private static bool _noticeReady;

    /// <summary>
    /// 시작 단계(본창을 만들기 전)의 안내: "이미 실행 중" 등. 예전에는 시스템 MessageBox 라 1Key 테마·글꼴을 따르지 않았다
    /// (2026-09-30 사용자 지적: 확인 창에 테마가 적용되지 않음). 설정 파일 머리(테마, 잠금과 무관하게 읽힘)와 글꼴·버튼만 준비해
    /// 앱 상자(<see cref="Dialog"/>)로 띄운다. 파일은 읽기만 한다. 준비나 표시가 실패하면 예전처럼 시스템 상자로.
    /// 글은 설정 파일의 화면 언어를 읽은 뒤 만든다(그래서 글 대신 글을 만드는 함수를 받는다).
    /// </summary>
    private static void Notice(Func<string> textOf, Func<string> titleOf, uint flags)
    {
        string? text = null, title = null;
        try
        {
            if (!_noticeReady)
            {
                var cfg = Config.Load();
                L.Apply(cfg.Language);   // 사용자가 고른 화면 언어(없으면 Windows 언어)
                Theme.InitProcessDarkMode();
                Theme.Mode = cfg.ThemeMode;
                Theme.Detect();
                Dw.Init();   // 본창과 같은 글자(Pretendard). 실패 복구는 Dw 의 메시지 전용 창이 맡아 본창 없이도 동작한다
                Theme.CreateFonts(WorkArea.DpiFor(0));
                var icc = new Native.INITCOMMONCONTROLSEX { dwSize = (uint)sizeof(Native.INITCOMMONCONTROLSEX), dwICC = Native.ICC_STANDARD_CLASSES | Native.ICC_WIN95_CLASSES };
                Native.InitCommonControlsEx(ref icc);
                Gdiplus.Init();
                Btn.Register(Native.GetModuleHandleW(null));
                _noticeReady = true;
            }
            text = textOf(); title = titleOf();
            Dialog.Show(0, text, title, flags & ~Native.MB_SETFOREGROUND);   // 앱 상자는 스스로 앞으로 나온다
        }
        catch { Native.MsgBox(0, text ?? textOf(), title ?? titleOf(), flags); }
    }

    /// <summary>
    /// 관리자 권한으로 다시 시작할 때 자식에게 넘기는 인자 (T2). 부모는 단일 인스턴스 잠금을 쥔 채로 자식을 띄우고,
    /// 자식은 이 번호의 프로세스가 끝날 때까지 기다린 뒤 잠금을 잡는다. (예전에는 부모가 잠금을 먼저 풀어, 그 사이 다른 실행이 끼어들 수 있었다.)
    /// </summary>
    public static string HandoffArg => "--handoff " + Environment.ProcessId;
    private const int HandoffWaitMs = 10_000;

    /// <summary>
    /// --handoff 로 받은 부모가 끝나기를 기다린다. 끝났거나(이미 없음 포함) 같은 실행 파일이 아니면(번호 재사용) true,
    /// 10초 안에 끝나지 않으면 false. 단일 인스턴스 검사는 건너뛰지 않는다: 기다린 뒤 평소처럼 잠금을 잡는다.
    /// </summary>
    private static bool WaitForHandoffParent(uint pid)
    {
        nint h = Native.OpenProcess(Native.SYNCHRONIZE | Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == 0) return true;   // 이미 끝났다
        try
        {
            string theirs = Native.GetProcessImagePath(h), mine = Environment.ProcessPath ?? string.Empty;
            if (theirs.Length > 0 && !string.Equals(theirs, mine, StringComparison.OrdinalIgnoreCase)) return true;   // 다른 프로그램이 번호를 이어 씀
            return Native.WaitForSingleObject(h, HandoffWaitMs) == 0;
        }
        finally { Native.CloseHandle(h); }
    }

    private static uint ParseHandoff(string[] args)
    {
        for (int i = 0; i + 1 < args.Length; i++)
            if (string.Equals(args[i], "--handoff", StringComparison.OrdinalIgnoreCase) && uint.TryParse(args[i + 1], out uint pid)) return pid;
        return 0;
    }

    /// <summary>
    /// 이미 실행 중인 1Key 를 어떻게 할지 정한다.
    /// 예전 버전이면 종료시키고 true(계속 시작), 같거나 더 새 버전이면 그 창을 띄우고 false(여기서 끝).
    /// </summary>
    private static bool TakeOverOldVersion()
    {
        // 상대가 막 시작하는 중이면(자동 실행 직후 바로 클릭 등) 창이 아직 없을 수 있다. 3초까지 기다려 본다.
        nint hwnd = 0;
        for (int i = 0; i < 30 && hwnd == 0; i++)
        {
            fixed (char* cls = WindowClass) hwnd = Native.FindWindowW(cls, null);
            if (hwnd == 0) Thread.Sleep(100);
        }

        // 잠금은 있는데 3초가 지나도 창이 없다(아주 느리게 시작 중이거나 종료가 덜 된 프로세스). 예전에는 새로 시작했는데,
        // 그러면 둘이 함께 돌 수 있다(T2). 새로 시작하지 않고 안내한다.
        if (hwnd == 0)
        {
            Notice(
                () => T.ProgNoWindow,
                () => T.ProgRunningTitle,
                Native.MB_OK | Native.MB_ICONWARNING | Native.MB_SETFOREGROUND);
            return false;
        }

        // 버전을 묻는다. 상대가 바쁘면(설정 저장 중 schtasks 실행 등) 잠시 답을 못 할 수 있으므로 최대 10초쯤 재시도한다.
        int theirs = QueryVersion(hwnd);
        for (int i = 0; i < 6 && theirs == NoAnswer; i++) { Thread.Sleep(200); theirs = QueryVersion(hwnd); }
        if (theirs == NoAnswer && !Native.IsHungAppWindow(hwnd))
        {
            // 살아 있는데 답이 없다(권한 차이로 메시지가 막혔거나 아주 바쁘다). 멀쩡한 인스턴스를 죽이지는 않는다.
            Notice(
                () => T.ProgNoAnswer,
                () => T.ProgRunningTitle,
                Native.MB_OK | Native.MB_ICONWARNING | Native.MB_SETFOREGROUND);
            return false;
        }

        // 같거나 더 새 버전이 돌고 있다 → 그 창을 불러오고 이 프로세스는 끝낸다.
        if (theirs >= App.VersionCode)
        {
            Native.PostMessageW(hwnd, Native.WM_SHOWME, 0, 0);
            // 소유자로 다른 프로세스의 창을 넘기면 안내 창이 뜨지 않는다. 소유자 없이 띄운다.
            Notice(
                () => T.ProgAlreadyRunning,
                () => T.ProgRunningTitle,
                Native.MB_OK | Native.MB_ICONINFORMATION | Native.MB_SETFOREGROUND);
            return false;
        }

        // 예전 버전이다 → 끝내고 자리를 이어받는다.
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid != 0)
        {
            nint h = Native.OpenProcess(Native.PROCESS_TERMINATE | Native.SYNCHRONIZE, false, pid);
            if (h != 0)
            {
                bool stopped;
                try { stopped = StopOldInstance(hwnd, h, graceful: theirs >= 0); }   // NoAnswer(멈춘 창)는 바로 강제 종료
                finally { Native.CloseHandle(h); }

                if (stopped)
                {
                    TookOver = true;
                    return true;
                }
            }
        }

        Notice(
            () => T.ProgOldRunning,
            () => T.ProgOldTitle,
            Native.MB_OK | Native.MB_ICONWARNING | Native.MB_SETFOREGROUND);
        return false;
    }

    private const int NoAnswer = -2;

    /// <summary>실행 중인 인스턴스의 버전 코드. 그 메시지를 모르는 예전 버전(0.1.5 미만)이면 -1, 응답 자체가 없으면 NoAnswer.</summary>
    private static int QueryVersion(nint hwnd)
    {
        nint r = Native.SendMessageTimeoutW(hwnd, Native.WM_ONEKEY_VERSION, 0, 0,
            Native.SMTO_ABORTIFHUNG, 1500, out nint result);
        if (r == 0) return NoAnswer;           // 시간 초과 / 멈춤 / 권한으로 막힘 — 예전 버전이라는 뜻이 아니다
        return result > 0 ? (int)result : -1;  // 0 이면 그 메시지를 모르는 예전 버전 (DefWindowProc 이 바로 0 을 돌려준다)
    }

    /// <summary>
    /// 예전 인스턴스를 끝낸다. graceful 이면 정상 종료를 먼저 요청하고(트레이 아이콘까지 정리된다),
    /// 그래도 남아 있으면 강제로 끝낸다.
    /// </summary>
    private static bool StopOldInstance(nint hwnd, nint hProcess, bool graceful)
    {
        if (graceful)
        {
            Native.PostMessageW(hwnd, Native.WM_ONEKEY_QUIT, 0, 0);
            if (Native.WaitForSingleObject(hProcess, 4000) == 0) return true;
        }

        if (!Native.TerminateProcess(hProcess, 0)) return false;
        Native.WaitForSingleObject(hProcess, 4000);
        return true;
    }

    // 예전 버전의 실행 파일 이름을 바꾸거나 자동 실행 등록을 지우는 일은 하지 않는다.
    // (백신이 "다른 실행 파일을 조작하고 자동 실행을 건드리는 프로그램"으로 오탐하기 때문.)
    // 대신 자동 실행이 켜져 있으면 App.Run 에서 이 실행 파일 경로로 다시 등록한다.
}
