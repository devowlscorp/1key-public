using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 비밀번호를 다른 프로그램의 입력란에 실제로 "타이핑" 하는 부분.
/// 프로그램마다 받아들이는 입력 방식이 달라서 네 가지 경로를 모두 제공한다.
/// </summary>
internal static unsafe class Injector
{
    private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
    private const int VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1;
    private const int VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3;
    private const int VK_LMENU = 0xA4, VK_RMENU = 0xA5;
    private const int VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    private const int VK_RETURN = 0x0D;
    private const uint MAPVK_VK_TO_VSC = 0, MAPVK_VK_TO_CHAR = 2;

    private static readonly int[] ModifierKeys =
        { VK_LCONTROL, VK_RCONTROL, VK_LMENU, VK_RMENU, VK_LSHIFT, VK_RSHIFT, VK_LWIN, VK_RWIN };

    [DllImport("imm32.dll")]
    private static extern nint ImmGetDefaultIMEWnd(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageTimeoutW(nint hWnd, uint msg, nint wParam, nint lParam,
        uint fuFlags, uint uTimeout, out nint lpdwResult);

    private const uint WM_IME_CONTROL = 0x0283;
    private const nint IMC_SETOPENSTATUS = 0x0006;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    public enum Status { Ok, Empty, NeedsElevation, Error, Timeout }
    public readonly record struct Result(Status Status, string? Message);

    /// <summary>한 번에 하나의 입력만 진행하도록 막는 잠금. UI 스레드가 <see cref="TryBegin"/> 으로 잡고, <see cref="Send"/> 가 끝나며 놓는다.</summary>
    private static int _busy;
    public static bool Busy => Volatile.Read(ref _busy) == 1;

    /// <summary>입력 작업을 하나 수락한다. 이미 진행 중이면 false. 스레드를 만들기 전에 UI 스레드에서 원자적으로 결정한다.</summary>
    public static bool TryBegin() => Interlocked.Exchange(ref _busy, 1) == 0;
    /// <summary>TryBegin 으로 잡았는데 스레드를 못 만들었을 때 되돌린다.</summary>
    public static void Abandon() => Interlocked.Exchange(ref _busy, 0);

    /// <summary>
    /// 취소 세대. 잠금 등이 <see cref="Cancel"/> 을 부르면 세대가 올라가고, 그 전에 수락된 작업은 모두 무효가 된다.
    /// 작업은 수락 시점의 세대를 들고 다니며 글자 하나마다, 붙여넣기와 Enter 직전에 비교한다.
    /// (예전의 "시작할 때 플래그를 false 로" 방식은 늦게 시작한 작업이 잠금의 취소 신호를 지울 수 있었다.)
    /// </summary>
    private static int _generation;
    public static int Generation => Volatile.Read(ref _generation);
    public static void Cancel() => Interlocked.Increment(ref _generation);
    private static bool Cancelled(int gen) => Volatile.Read(ref _generation) != gen;

    /// <summary>
    /// 전송별 제한 시각(TickCount64, 0 = 없음). 사이트 채우기가 자기 전송에만 건다: 전역 세대를 올리지 않으므로 끝난 전송의 시간 제한이
    /// 뒤에 시작한 다른 입력을 멈추게 할 수 없다(Codex C61-1). 이 스레드의 전송이 제한 때문에 멈췄는지는 _expiredHit 로 구분한다.
    /// </summary>
    private static bool Expired(long deadline) => deadline != 0 && (TestExpireNow || Environment.TickCount64 >= deadline);

    /// <summary>검증 전용(ONEKEY_TEST, WM_TEST_SITE2 lParam 10): 제한 시각이 있는 전송은 지금 만료된 것으로 본다. 대기 경계에서 만료를 일으키는 시험(Codex R75-2).
    /// 시험 관찰값 초기화(lParam 4)에서 꺼진다. 제품 모드에서는 켜는 길이 없다.</summary>
    internal static volatile bool TestExpireNow;

    /// <summary>사이트 채우기 한 번의 결과 종류(Codex C62-1). 번역 문구가 아니라 이 값으로 구분한다. 숫자는 시험 관찰값과 같다.</summary>
    public enum FillOutcome { None = 0, Ok = 1, Timeout = 2, Stopped = 3, Failed = 4, NeedsElevation = 5 }

    /// <summary>
    /// 채우기가 멈춘 이유를 한 곳에서 정한다(Codex C62-2): 잠금·취소 → 제한 시각 → 대상(활성 창) 변경 → 그 밖의 오류.
    /// 사용권 대기·UIA 대기·Capture 직후·Ctrl+A·전송 어디서 멈춰도 같은 순서로 분류한다(Send 안의 StillOnTarget 도 취소 → 제한 순서).
    /// </summary>
    public static (FillOutcome Outcome, string Message) ClassifyStop(bool cancelledOrLocked, bool expired, bool targetChanged, string? other)
        => cancelledOrLocked ? (FillOutcome.Stopped, Stopped)
         : expired ? (FillOutcome.Timeout, T.FillTimeout)
         : targetChanged ? (FillOutcome.Failed, T.InjActiveChanged)
         : (FillOutcome.Failed, other ?? T.InjectFailed);

    /// <summary>검증 전용(ONEKEY_TEST): 대상 창이 더 높은 권한인 것처럼 처리한다(권한 부족 결과 분류 시험, C62-1). 한 번 쓰면 꺼진다.</summary>
    internal static volatile bool TestForceElevationOnce;
    [ThreadStatic] private static bool _expiredHit, _guardHit;

    private static string Stopped => T.InjStopped;

    /// <summary>입력 대상: 최상위 창, 그 스레드, 그리고 처음 커서가 있던 입력란. 세 가지가 다 같아야 다음 글자를 보낸다.</summary>
    private readonly record struct Target(nint Window, uint Tid, nint Focus, int Gen, long Deadline, Func<bool>? Guard);

    /// <summary>
    /// 다음 글자를 보내도 되는가: 취소되지 않았고, 처음의 창이 아직 활성 창이며, 그 창 안의 포커스 입력란도 그대로여야 한다.
    /// 같은 창 안에서 다른 입력란으로 옮겨 가도(예: 비밀번호 칸 → 검색 칸) 멈춘다. 브라우저처럼 여러 입력란이
    /// 하나의 창 핸들을 함께 쓰는 곳은 이 검사로 구분하지 못한다(README 에 한계로 적음).
    /// </summary>
    private static bool StillOnTarget(in Target t)
    {
        if (!Basic(t)) return false;
        if (t.Guard is null) return true;
        if (!TestHold("send.guard-in")) return false;   // 검증 전용 경계(R75-2 시험): 확인 직전·직후
        if (!t.Guard()) { _guardHit = true; return false; }   // 사이트 채우기: 페이지 안 포커스가 그 칸 그대로인가(UIA, 최대 1초)
        if (!TestHold("send.guard-out")) return false;
        return Basic(t);   // 긴 확인 뒤에는 앞의 판정을 쓰지 않고 다시 본다 (Codex R75-2)

        static bool Basic(in Target t)
        {
            if (Cancelled(t.Gen)) return false;   // 잠금·취소가 먼저: 시간 제한과 겹치면 "멈춤"으로 분류
            if (Expired(t.Deadline)) { _expiredHit = true; return false; }
            return Native.GetForegroundWindow() == t.Window && FocusedWindow(t.Tid, t.Window) == t.Focus;
        }
    }

    /// <summary>요청한 창·포커스·잠금 세대·제한 시각이 그대로인가: 오류면 이유, 아니면 null (Enter·Ctrl+A 직전 공용).</summary>
    private static string? CheckRequest(in Request req, int gen, long deadline)
    {
        if (Cancelled(gen)) return Stopped;
        if (Expired(deadline)) return T.FillTimeout;
        if (req.Window == 0 || Native.GetForegroundWindow() != req.Window) return T.InjActiveChanged;
        uint tid = Native.GetWindowThreadProcessId(req.Window, out _);
        if (req.Focus == 0 || FocusedWindow(tid, req.Window) != req.Focus) return T.InjFocusMoved;
        return null;
    }

    /// <summary>단축키를 받은 순간의 대상: 활성 창과 그 안에서 커서가 있는 입력란. UI 스레드가 읽어 작업에 넘긴다.</summary>
    public readonly record struct Request(nint Window, uint Tid, nint Focus);

    /// <summary>지금 활성 창과 그 포커스 입력란을 읽는다. 단축키(WM_HOTKEY)·테스트 타이머를 처리하는 UI 스레드에서 부른다.</summary>
    public static Request Capture()
    {
        nint w = Native.GetForegroundWindow();
        if (w == 0) return default;
        uint tid = Native.GetWindowThreadProcessId(w, out _);
        var gti = new Native.GUITHREADINFO { cbSize = (uint)sizeof(Native.GUITHREADINFO) };
        nint focus = tid != 0 && Native.GetGUIThreadInfo(tid, ref gti) ? gti.hwndFocus : 0;
        return new Request(w, tid, focus);
    }

    /// <summary>
    /// 검증 하네스와의 결정적 동기화 (T10). ONEKEY_TEST=1 이고 ONEKEY_TEST_HOLD_EVENT=&lt;이름&gt; 으로 시작했을 때만 동작한다.
    /// 하네스가 "Local\&lt;이름&gt;.arm.&lt;지점&gt;" 이벤트를 만들어 두면(무장) 입력은 그 지점에서 "…reached" 를 켜고 "…go" 를 기다린다.
    /// 5초 안에 계속 허가가 없으면 멈춘 것으로 본다(입력하지 않음). 지점: wait(시작 전 대기 뒤), char1(첫 글자 뒤), enter(Enter 직전).
    /// 무장하지 않은 지점은 그냥 지나간다. 지연만 늘리는 방식은 쓰지 않는다.
    /// </summary>
    private static readonly string? _holdName = Program.IsTestMode ? Environment.GetEnvironmentVariable("ONEKEY_TEST_HOLD_EVENT") : null;
    /// <summary>검증 전용 동기화 지점을 다른 파일(칩)에서 쓰기 위한 입구. 평소에는 아무것도 하지 않는다.</summary>
    public static bool TestHoldPoint(string point) => TestHold(point);

    /// <summary>검증 전용(ONEKEY_TEST=1): 입력 경로의 짧은 기록을 설정 폴더의 inject-trace.txt 에 덧붙인다(Enter 를 건너뛴 이유 등). 값은 적지 않는다.</summary>
    internal static void TestTrace(string line) => Trace(line);
    private static void Trace(string line)
    {
        if (!Program.IsTestMode) return;
        try { File.AppendAllText(Path.Combine(Config.Dir, "inject-trace.txt"), DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + Environment.NewLine); } catch { }
    }
    private static bool TestHold(string point)
    {
        if (string.IsNullOrEmpty(_holdName)) return true;
        nint arm = Native.OpenEventW(Native.SYNCHRONIZE, false, $"Local\\{_holdName}.arm.{point}");
        if (arm == 0) return true;
        nint reached = Native.CreateEventW(0, false, false, $"Local\\{_holdName}.reached");
        nint go = Native.CreateEventW(0, false, false, $"Local\\{_holdName}.go");
        try
        {
            Native.SetEvent(reached);
            return Native.WaitForSingleObject(go, 5000) == 0;
        }
        finally { Native.CloseHandle(arm); Native.CloseHandle(reached); Native.CloseHandle(go); }
    }

    public enum TargetCheck { Ok, NoWindow, OwnWindow, Shell, NoFocus, NeedsElevation }

    /// <summary>
    /// 목록 입력 칩(D안)에서 [입력]을 누른 순간의 대상이 넣을 만한 곳인지. 아니면 넣지 않고 칩에 안내한다.
    /// 1Key 자신(본창·칩), 바탕 화면·작업 표시줄, 포커스 입력란이 없는 창은 거절한다. 관리자 권한 창은 따로 구분한다.
    /// 새 활성 창을 대신 고르지 않는다. 이 검사를 통과해도 <see cref="Send"/> 가 입력 도중 같은 검사를 다시 한다.
    /// </summary>
    public static TargetCheck Check(in Request r)
    {
        if (r.Window == 0) return TargetCheck.NoWindow;
        if (IsOwnWindow(r.Window)) return TargetCheck.OwnWindow;
        string cls = Native.GetClassName(r.Window);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return TargetCheck.Shell;
        if (TargetNeedsElevation(r.Window)) return TargetCheck.NeedsElevation;
        if (r.Focus == 0) return TargetCheck.NoFocus;
        return TargetCheck.Ok;
    }

    /// <summary>
    /// 브라우저 창인가 (T1). Chromium 계열(Chrome·Edge·Whale 등)과 Firefox. 브라우저 안의 여러 입력란은 창 핸들 하나를 함께 써서
    /// 입력 도중 다른 칸(예: 검색창)으로 옮겨 가도 구분할 수 없다. 0.2.41 부터 Enter 는 기본으로 보내고, 항목에서
    /// "브라우저에서는 Enter 보내지 않기"를 켠 경우에만 이 판정으로 Enter 를 생략한다. 창 클래스로만 판정하므로
    /// 같은 클래스를 쓰는 웹뷰 데스크톱 앱도 브라우저로 분류된다.
    /// </summary>
    private static bool IsBrowserWindow(nint hwnd)
        => Native.GetClassName(hwnd) is "Chrome_WidgetWin_1" or "MozillaWindowClass";

    public static string BrowserEnterNotice => T.InjBrowserEnter;

    /// <summary>
    /// 슬롯의 비밀번호를 <paramref name="req"/>(단축키를 누른 순간 UI 스레드가 읽은 활성 창과 입력란)에 입력한다.
    /// 호출 전에 <see cref="TryBegin"/> 이 성공했어야 하며, 여기서 끝날 때 busy 를 놓는다.
    /// </summary>
    public static Result Send(Slot slot, Config cfg, Request req, int gen, long deadline = 0, Func<bool>? guard = null, bool replaceFirst = false)
    {
        nint requestedTarget = req.Window;
        _expiredHit = false; _guardHit = false;
        try
        {
            // 슬롯 값은 처음에 한 번만 읽는다. 입력하는 몇 초 사이에 UI 스레드가 슬롯을 고치거나 지워도 섞이지 않도록.
            // 입력이 여러 개인 항목(0.2.65): 순서대로, 칸마다 글을 모두 골라(이미 채워진 값 대신) 넣고 Tab 으로 다음 칸에 간다.
            string[] parts = slot.InputValues();
            InputMethod method = slot.Method;
            bool pressEnter = slot.PressEnter, noEnterInBrowser = slot.NoEnterInBrowser;
            if (parts.Length == 0) return new(Status.Empty, null);
            bool multi = parts.Length > 1;
            if (Cancelled(gen) || (cfg.HasMaster && !cfg.IsUnlocked)) return new(Status.Error, Stopped);   // 스레드가 뜨기 전에 잠겼다
            if (Expired(deadline)) return new(Status.Timeout, T.FillTimeout);

            // 대상은 단축키를 누른 순간의 활성 창이다. 스레드가 뜨는 사이 다른 창으로 옮겨 갔으면 그 창에는 넣지 않는다.
            nint target = requestedTarget;
            if (target == 0) return new(Status.Error, T.InjNoActive);
            if (IsOwnWindow(target)) return new(Status.Error, T.InjOwnWindow);
            if (Native.GetForegroundWindow() != target)
                return new(Status.Error, T.InjActiveChanged);

            // 대상 창이 더 높은 권한이면 어차피 입력이 차단된다. 부분 입력을 피하려고 먼저 판단한다.
            if (Program.IsTestMode && TestForceElevationOnce) { TestForceElevationOnce = false; return new(Status.NeedsElevation, null); }
            if (TargetNeedsElevation(target)) return new(Status.NeedsElevation, null);

            // 1) 사용자가 아직 누르고 있는 단축키 조합을 먼저 떼어낸다.
            //    이 과정을 건너뛰면 대상 창이 Ctrl+Alt+문자 로 인식해 아무 글자도 입력되지 않는다.
            PreventAltMenu();
            ReleaseModifiers();

            if (cfg.PreDelayMs > 0) Thread.Sleep(cfg.PreDelayMs);
            if (!TestHold("wait")) return new(Status.Error, Stopped);

            // 기다리는 동안(최대 4.5초) 활성 창이 바뀌었으면 엉뚱한 곳에 넣지 않도록 멈춘다.
            nint now = Native.GetForegroundWindow();
            if (Cancelled(gen)) return new(Status.Error, Stopped);
            if (Expired(deadline)) return new(Status.Timeout, T.FillTimeout);
            if (now != target)
                return new(Status.Error, IsOwnWindow(now) ? T.InjOwnCameFront
                                                          : T.InjActiveChangedWhile);

            // 2) 대상 스레드에 입력 큐를 붙여 포커스 정보를 공유한다.
            uint myTid = Native.GetCurrentThreadId();
            uint targetTid = Native.GetWindowThreadProcessId(target, out _);
            bool attached = targetTid != 0 && targetTid != myTid
                            && Native.AttachThreadInput(myTid, targetTid, true);
            string? enterNote = null;   // 검증 기록: Enter 보내기 호출의 결과. 상태는 입력 큐를 뗀 뒤에 읽는다(아래 finally)
            nint enterFocus = 0;
            try
            {
                // 입력란은 단축키를 누른 순간의 것(req.Focus)이다. 기다리는 동안 같은 창 안의 다른 칸으로 옮겨 갔으면 넣지 않는다.
                // 포커스를 알 수 없었던 창(다른 데스크톱·콘솔 등)은 창 전체를 입력란으로 삼지 않고 멈춘다.
                if (req.Focus == 0) return new(Status.Error, T.InjNoFocus);
                nint focusNow = FocusedWindow(targetTid, target);
                if (focusNow != req.Focus)
                    return new(Status.Error, T.InjFocusMoved);
                var t = new Target(target, targetTid, req.Focus, gen, deadline, guard);
                nint hkl = Native.GetKeyboardLayout(targetTid);   // 대상 창의 키보드 배열로 글자→키를 푼다
                string? error = null;
                for (int p = 0; p < parts.Length; p++)
                {
                    if (p > 0)
                    {
                        // 다음 칸으로 Tab. 같은 창 안에서 포커스가 옮겨 가는 것이 정상이므로, Tab 뒤의 포커스를 새 대상으로 삼는다.
                        // 창이 바뀌었거나 포커스를 알 수 없으면 멈춘다(남은 입력을 엉뚱한 곳에 넣지 않는다).
                        if (!StillOnTarget(t)) { error = Stopped; break; }
                        Thread.Sleep(Math.Max(cfg.KeyDelayMs, 30));
                        error = method == InputMethod.PostMessage ? PostTab(t.Focus) : TapVirtualKey(Native.VK_TAB);
                        if (error is not null) break;
                        Thread.Sleep(Math.Max(cfg.KeyDelayMs, 80));   // 포커스가 옮겨 갈 시간
                        if (Cancelled(gen) || Expired(deadline)) { error = Stopped; break; }
                        if (Native.GetForegroundWindow() != target) { error = T.InjActiveChangedWhile; break; }
                        nint next = FocusedWindow(targetTid, target);
                        if (next == 0) { error = T.InjNoFocus; break; }
                        t = t with { Focus = next };
                    }
                    // 입력이 여러 개면 칸마다 먼저 글을 모두 고른다: 브라우저·프로그램이 미리 채워 둔 값(쿠키로 기억한 아이디 등)을 바꿔 쓴다.
                    // replaceFirst: 프로그램 연결 채우기는 입력이 하나여도 첫 칸의 글을 바꿔 쓴다(기억된 아이디 등)
                    if ((multi || (replaceFirst && p == 0)) && SelectAllIn(t, method) is string selErr) { error = selErr; break; }
                    string text = parts[p];
                    error = method switch
                    {
                        InputMethod.Auto => TypeScanCode(t, hkl, text, cfg.KeyDelayMs, true, true),
                        InputMethod.ScanCode => TypeScanCode(t, hkl, text, cfg.KeyDelayMs, false, false),
                        InputMethod.Unicode => TypeUnicode(t, text, cfg.KeyDelayMs),
                        InputMethod.PostMessage => TypePostMessage(t, hkl, text, cfg.KeyDelayMs),
                        InputMethod.Clipboard => TypeClipboard(t, text),
                        _ => T.InjUnknownMethod,
                    };
                    if (error is not null) break;
                }
                if (error is not null) return _expiredHit ? new(Status.Timeout, T.FillTimeout) : _guardHit ? new(Status.Error, T.InjFocusMoved) : new(Status.Error, error);   // 글자가 다 안 들어갔으면 Enter 는 보내지 않는다

                if (pressEnter && noEnterInBrowser && IsBrowserWindow(target)) return new(Status.Ok, BrowserEnterNotice);   // 항목에서 "브라우저에서는 보내지 않기"를 켰을 때만 (0.2.41)
                if (pressEnter)
                {
                    Thread.Sleep(Math.Max(cfg.KeyDelayMs, 30));
                    long holdStart = Environment.TickCount64;
                    if (!TestHold("enter")) { Trace($"enter-held {Environment.TickCount64 - holdStart} ms"); return new(Status.Error, T.InjEnterHeld); }
                    bool same = StillOnTarget(t) && (method != InputMethod.PostMessage || Native.IsWindow(t.Focus));   // 모든 방식이 같은 규칙 (V35-4)
                    if (!same)
                    {
                        nint fgNow = Native.GetForegroundWindow();
                        Trace($"enter-skip target={Native.GetClassName(t.Window)} fg={Native.GetClassName(fgNow)} sameFg={fgNow == t.Window} focus={Native.GetClassName(FocusedWindow(t.Tid, t.Window))} expectedFocus={Native.GetClassName(t.Focus)} sameFocus={FocusedWindow(t.Tid, t.Window) == t.Focus} cancelled={Cancelled(t.Gen)}");
                        return _expiredHit ? new(Status.Timeout, T.FillTimeout) : new(Status.Error, T.InjEnterSkipped);
                    }
                    // 검증 기록(Codex 14:00): "enter-call" 은 보내기 직전, "enter-result" 는 보내기 호출의 결과와 그 순간의 상태다.
                    // 둘 다 1Key 가 보낸 것까지만 말한다. 대상 프로그램이 Enter 를 처리했는지는 알 수 없다.
                    // 보내기 전후(입력 큐가 붙어 있는 동안)에는 기록용 Win32 호출을 하지 않는다: 0.2.99 에서 보낸 직후 키 상태를 읽자
                    // 가짜 창이 Enter 를 한 번도 받지 못했다(IJ05 0/30, 0.2.98 은 5/5). 상태는 finally 에서 뗀 뒤에 읽는다.
                    Trace("enter-call " + Native.GetClassName(t.Window) + $" hold {Environment.TickCount64 - holdStart} ms");
                    enterFocus = t.Focus;
                    if (method == InputMethod.PostMessage)
                    {
                        nint focus = t.Focus;   // 글자를 보낸 바로 그 입력란
                        // Enter 는 키 누름·뗌만 보낸다. 대상의 TranslateMessage 가 '\r' 글자를 한 번 만든다(함께 보내면 두 번이 된다).
                        bool ok = Native.PostMessageW(focus, Native.WM_KEYDOWN, VK_RETURN, 0x001C0001)
                               && Native.PostMessageW(focus, Native.WM_KEYUP, VK_RETURN, unchecked((nint)0xC01C0001));
                        enterNote = "post=" + (ok ? "ok" : "failed");
                        if (!ok) return new(Status.Error, T.InjEnterClosed);
                    }
                    else if (TapVirtualKey(VK_RETURN) is string enterErr)
                    {
                        enterNote = "sendinput=failed " + enterErr.Replace('\n', ' ');
                        return new(Status.Error, T.InjEnterFailed + "\n" + enterErr);
                    }
                    else
                    {
                        enterNote = "sendinput=ok";
                        // 마지막 키(Enter) 뒤에는 글자와 달리 쉬는 틈이 없어, 대상이 받아 가기 전에 입력 큐를 떼면 Enter 가 사라질 수 있다
                        // (IJ05 간헐 실패: 1Key 는 보냈는데 대상 창에 키가 오지 않음, 보낸 뒤 1–2ms 지연만으로 0/30). 떼기 전에 잠깐 기다린다.
                        Thread.Sleep(Math.Max(cfg.KeyDelayMs, 50));
                    }
                }
                return new(Status.Ok, null);
            }
            finally
            {
                if (attached) Native.AttachThreadInput(myTid, targetTid, false);
                if (enterNote is not null && Program.IsTestMode) Trace("enter-result " + enterNote + EnterState(target, targetTid, enterFocus));
            }
        }
        catch (Exception ex)
        {
            return new(Status.Error, T.InjError(ex.Message));
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    /// <summary>1Key 자신의 창인가. 자기 창(예: 편집 화면의 이름 칸, 잠금 화면의 비밀번호 칸)에는 절대 넣지 않는다.</summary>
    private static bool IsOwnWindow(nint hwnd)
    {
        if (hwnd == 0) return false;
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == (uint)Environment.ProcessId;
    }

    /// <summary>대상 창이 1Key 보다 높은 권한(관리자 등)이라 키 입력이 차단되는지 판단한다.</summary>
    /// <summary>프로그램 연결 채우기가 칸을 누르기 전에: 대상이 더 높은 권한이면 누르기·입력 모두 막히므로 먼저 안다.</summary>
    public static bool NeedsElevationFor(nint target) => TargetNeedsElevation(target);
    /// <summary>그 스레드의 키보드 포커스 창(없으면 0).</summary>
    public static nint FocusOf(uint tid) => FocusedWindow(tid, 0);

    private static bool TargetNeedsElevation(nint target)
    {
        if (Native.IsElevated()) return false;   // 이미 관리자면 문제없음

        uint tid = Native.GetWindowThreadProcessId(target, out uint pid);
        if (tid == 0 || pid == 0) return false;

        nint hProc = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProc == 0) return true;   // 프로세스조차 열 수 없다 = 더 높은 무결성 수준

        try
        {
            if (!Native.OpenProcessToken(hProc, Native.TOKEN_QUERY, out nint token))
                return true;           // 토큰을 열 수 없다 = 더 높음
            try
            {
                uint elevated = 0;
                if (Native.GetTokenInformation(token, Native.TokenElevation, &elevated, sizeof(uint), out _) && elevated != 0)
                    return true;
                // 승격 토큰이 아니어도 무결성 수준이 더 높으면(시스템 서비스가 띄운 창, 보안 프로그램 등) Windows 가 입력을 조용히 버린다
                int mine = MyIntegrity(), theirs = IntegrityOf(token);
                return mine >= 0 && theirs > mine;
            }
            finally { Native.CloseHandle(token); }
        }
        finally { Native.CloseHandle(hProc); }
    }

    private static int _myIntegrity = -2;
    private static int MyIntegrity()
    {
        if (_myIntegrity != -2) return _myIntegrity;
        int v = -1;
        if (Native.OpenProcessToken(Native.GetCurrentProcess(), Native.TOKEN_QUERY, out nint t))
        {
            try { v = IntegrityOf(t); } finally { Native.CloseHandle(t); }
        }
        return _myIntegrity = v;
    }

    /// <summary>토큰의 무결성 수준 RID(0x2000 보통, 0x3000 높음, 0x4000 시스템). 읽지 못하면 -1.</summary>
    private static int IntegrityOf(nint token)
    {
        byte* buf = stackalloc byte[256];
        if (!Native.GetTokenInformation(token, 25 /* TokenIntegrityLevel */, buf, 256, out _)) return -1;
        nint sid = *(nint*)buf;   // TOKEN_MANDATORY_LABEL.Label.Sid
        byte n = *GetSidSubAuthorityCount(sid);
        return n == 0 ? -1 : (int)*GetSidSubAuthority(sid, (uint)(n - 1));
    }
    [DllImport("advapi32.dll")] private static extern byte* GetSidSubAuthorityCount(nint sid);
    [DllImport("advapi32.dll")] private static extern uint* GetSidSubAuthority(nint sid, uint n);

    // ---------------------------------------------------------------- 방식 1/2: 스캔코드

    /// <summary>
    /// 키보드 하드웨어가 보내는 것과 같은 스캔코드로 입력한다.
    /// 유니코드 주입을 무시하는 구형 Win32/MFC 계열 창(사내 뱅킹 결재창 등)에서 가장 잘 먹힌다.
    /// </summary>
    private const int VK_CAPITAL = 0x14;

    private static string? TypeScanCode(in Target target, nint hkl, string text, int delay, bool turnOffIme, bool unicodeFallback)
    {
        if (turnOffIme) TurnOffIme(target.Window);

        // CapsLock 이 켜져 있으면 알파벳의 대소문자가 뒤집힌다. 그만큼 Shift 를 보정한다.
        bool capsOn = (Native.GetKeyState(VK_CAPITAL) & 1) != 0;

        int n = 0;
        foreach (char ch in text)
        {
            if (n++ == 1 && !TestHold("char1")) return Stopped;   // 검증 전용: 첫 글자 뒤 (T10)
            if (!StillOnTarget(target)) return Stopped;   // 잠겼거나 다른 창·다른 입력란으로 갔다: 남은 글자를 엉뚱한 곳에 넣지 않는다

            // 대상 창의 키보드 배열(hkl)로 푼다. 1Key 와 대상의 배열이 다르면(예: 독일어 배열 창) y/z 가 뒤바뀌는 식으로
            // 틀린 글자가 들어가는데, 비밀번호는 틀린 줄도 모르고 계정이 잠길 수 있다.
            short scan = Native.VkKeyScanExW(ch, hkl);
            bool deadKey = scan != -1 && (Native.MapVirtualKeyExW((uint)(scan & 0xFF), MAPVK_VK_TO_CHAR, hkl) & 0x80000000) != 0;
            if (scan == -1 || deadKey)
            {
                // 그 배열로 칠 수 없는 글자(또는 조합용 dead key 라 혼자서는 글자가 안 되는 키). 어떤 글자인지는 알리지 않는다(비밀번호의 일부).
                if (!unicodeFallback) return T.InjLayout;
                if (SendUnicodeChar(ch) is string e) return e;
                Delay(delay);
                continue;
            }

            byte vk = (byte)(scan & 0xFF);
            int shiftState = (scan >> 8) & 0xFF;
            bool needShift = (shiftState & 1) != 0;
            bool needCtrl = (shiftState & 2) != 0;
            bool needAlt = (shiftState & 4) != 0;

            // CapsLock 은 알파벳 키에만 영향을 준다.
            if (capsOn && ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z')))
                needShift = !needShift;

            // 조합 키는 누른 것을 반드시 떼고 나간다. 중간에 실패해도 Shift/Ctrl 이 눌린 채 남지 않도록.
            bool shiftDown = false, ctrlDown = false, altDown = false;
            string? err;
            try
            {
                if (needShift) { if (KeyEvent(VK_SHIFT, true, false) is string e1) return e1; shiftDown = true; }
                if (needCtrl) { if (KeyEvent(VK_CONTROL, true, false) is string e2) return e2; ctrlDown = true; }
                if (needAlt) { if (KeyEvent(VK_MENU, true, false) is string e3) return e3; altDown = true; }
                err = KeyEvent(vk, true, true, hkl) ?? KeyEvent(vk, false, true, hkl);
            }
            finally
            {
                if (altDown) KeyEvent(VK_MENU, false, false);
                if (ctrlDown) KeyEvent(VK_CONTROL, false, false);
                if (shiftDown) KeyEvent(VK_SHIFT, false, false);
            }

            if (err is not null) return err;
            Delay(delay);
        }
        return null;
    }

    // ---------------------------------------------------------------- 방식 3: 유니코드

    /// <summary>글자 자체를 그대로 주입한다. 키보드 배열/한영 상태의 영향을 받지 않는다.</summary>
    private static string? TypeUnicode(in Target target, string text, int delay)
    {
        int n = 0;
        foreach (char ch in text)
        {
            if (n++ == 1 && !TestHold("char1")) return Stopped;
            if (!StillOnTarget(target)) return Stopped;
            if (SendUnicodeChar(ch) is string e) return e;
            Delay(delay);
        }
        return null;
    }

    private static string? SendUnicodeChar(char ch)
    {
        var inputs = stackalloc Native.INPUT[2];
        inputs[0] = MakeKey(0, ch, Native.KEYEVENTF_UNICODE);
        inputs[1] = MakeKey(0, ch, Native.KEYEVENTF_UNICODE | Native.KEYEVENTF_KEYUP);
        return Dispatch(inputs, 2);
    }

    // ---------------------------------------------------------------- 방식 4: 창 메시지

    /// <summary>
    /// 포커스를 가진 자식 컨트롤에 WM_CHAR 를 직접 보낸다.
    /// 시스템 입력 큐를 거치지 않으므로, 입력 큐가 막혀 있는 경우에 대안이 된다.
    /// </summary>
    private static string? TypePostMessage(in Target target, nint hkl, string text, int delay)
    {
        nint focus = target.Focus;
        if (focus == 0) return T.InjNoFocusCtl;

        int n = 0;
        foreach (char ch in text)
        {
            // 이 방식은 처음 잡은 컨트롤에 직접 보내지만, 사용자가 다른 창·다른 칸으로 옮기면 다른 방식과 똑같이 멈춘다 (Codex V35-4).
            // (예전에는 취소와 컨트롤 생존만 봐서, 옮겨 간 뒤에도 원래 칸에 남은 글자와 Enter 를 보냈다.)
            // 이미 보낸 메시지는 되돌릴 수 없으므로, 멈추는 것은 남은 글자와 Enter 다.
            if (n++ == 1 && !TestHold("char1")) return Stopped;
            if (!Native.IsWindow(focus)) return T.InjFieldGone;
            if (!StillOnTarget(target)) return Stopped;
            // 글자는 WM_CHAR 하나만 보낸다. 예전에는 WM_KEYDOWN·WM_CHAR·WM_KEYUP 을 함께 보냈는데, 대상 프로그램의 메시지 루프가
            // 보낸 WM_KEYDOWN 을 TranslateMessage 로 다시 글자로 바꿔 **모든 글자가 두 번씩** 들어갔다(0.2.36 T10 하네스에서 발견).
            short scan = Native.VkKeyScanExW(ch, hkl);
            uint sc = scan != -1 ? Native.MapVirtualKeyExW((uint)(scan & 0xFF), MAPVK_VK_TO_VSC, hkl) : 0;
            bool ok = Native.PostMessageW(focus, Native.WM_CHAR, (nint)ch, (nint)(1u | (sc << 16)));
            if (!ok) return T.InjWmFailed;
            Delay(delay);
        }
        return null;
    }

    /// <summary>검증 기록 전용: 보낸 뒤(입력 큐를 뗀 뒤)의 앞 창·포커스가 대상과 같은지(값은 적지 않음).</summary>
    private static string EnterState(nint window, uint tid, nint expectedFocus)
    {
        nint fg = Native.GetForegroundWindow(), focus = FocusedWindow(tid, window);
        return $" fg={Native.GetClassName(fg)} sameFg={fg == window} sameFocus={focus == expectedFocus} {KeyState()}";
    }

    /// <summary>검증 기록 전용: 그 순간 눌려 있는 보조 키(Ctrl·Alt·Shift·Win)와 입력 언어. Alt 가 눌린 채면 EDIT 는 Enter 로 줄을 바꾸지 않는다.</summary>
    private static string KeyState()
    {
        string m = "";
        if (Native.GetAsyncKeyState(0x11) < 0) m += "C";
        if (Native.GetAsyncKeyState(0x12) < 0) m += "A";
        if (Native.GetAsyncKeyState(0x10) < 0) m += "S";
        if (Native.GetAsyncKeyState(0x5B) < 0 || Native.GetAsyncKeyState(0x5C) < 0) m += "W";
        return $"mods={(m.Length == 0 ? "-" : m)} hkl={((long)Native.GetKeyboardLayout(0)):X8}";
    }

    private static nint FocusedWindow(uint targetTid, nint fallback)
    {
        var gti = new Native.GUITHREADINFO { cbSize = (uint)sizeof(Native.GUITHREADINFO) };
        if (targetTid != 0 && Native.GetGUIThreadInfo(targetTid, ref gti) && gti.hwndFocus != 0)
            return gti.hwndFocus;
        return fallback;
    }

    // ---------------------------------------------------------------- 방식 5: 클립보드

    private static string? TypeClipboard(in Target target, string text)
    {
        nint owner = CreateClipOwner();
        try
        {
            // 이전 내용 읽기 → 비우기 → 비밀번호와 소유 표식 쓰기를 **한 번 연 동안** 한다 (Codex C1).
            // 이전 내용이 글이면 되살린다. 그림·파일처럼 글이 아니면 되살릴 수 없어 비우게 된다(아래에서 알린다).
            if (SwapInClipboardText(owner, text, out string? saved, out bool hadOther, out string? swapError) is not byte[] token)
                return swapError ?? T.InjClipUnavailable;

            string? err = null;
            Thread.Sleep(60);
            if (!StillOnTarget(target)) err = Stopped;
            else
            {
                err = KeyEvent(VK_CONTROL, true, false);
                if (err is null)
                {
                    try { err = KeyEvent('V', true, true) ?? KeyEvent('V', false, true); }
                    finally { KeyEvent(VK_CONTROL, false, false); }
                }
                Thread.Sleep(220);
            }

            // 비밀번호를 클립보드에 남기지 않는다. 우리 소유 표식이 그대로일 때만 되돌리고, 다른 프로그램이 바꿨으면 그대로 둔다.
            ClipRestore r = RestoreClipboard(owner, token, saved);
            string left = "\n" + T.InjClipLeft;
            if (err is not null) return err + (r == ClipRestore.Failed ? left : "");
            if (r == ClipRestore.Failed) return T.InjClipRestoreFailed + left;
            if (r == ClipRestore.EmptiedOnly) return T.InjClipEmptied;
            if (hadOther) return T.InjClipOther;
            return null;
        }
        finally { if (owner != 0) Native.DestroyWindow(owner); }
    }

    /// <summary>
    /// 클립보드 소유자 창: OpenClipboard(0) 뒤 EmptyClipboard 를 하면 소유자가 NULL 이 되어 SetClipboardData 가 실패할 수 있다고
    /// 문서에 명시되어 있으므로, 이 스레드에 메시지 전용 창을 하나 만들어 소유자로 쓴다.
    /// </summary>
    private static nint CreateClipOwner()
    {
        fixed (char* cls = "STATIC") fixed (char* empty = "")
            return Native.CreateWindowExW(0, cls, empty, 0, 0, 0, 0, 0, Native.HWND_MESSAGE, 0, 0, 0);
    }

    /// <summary>
    /// 클립보드 소유 표식 형식 (Codex V35-1). 비밀번호를 넣을 때 쓰기마다 새로 만든 임의 토큰을 이 형식으로 함께 넣고,
    /// 되돌릴 때는 **이 토큰이 그대로 있을 때만** 우리 것으로 본다. 다른 프로그램이 한 번이라도 비우거나 쓰면 이 형식이 사라지므로
    /// 일련번호 증가량(CloseClipboard 의 형식 합성으로도 늘어난다)에 기대지 않고 구분된다.
    /// 토큰은 비밀이 아니고, 같은 계정의 악성 프로그램에 대한 인증 수단도 아니다(경쟁 조건 구분용).
    /// </summary>
    private static uint _ownerFormat;
    private static uint OwnerFormat
    {
        get
        {
            if (_ownerFormat == 0) fixed (char* n = "1Key.ClipOwner") _ownerFormat = Native.RegisterClipboardFormatW(n);
            return _ownerFormat;
        }
    }

    private static nint AllocBytes(byte[] data)
    {
        nint h = Native.GlobalAlloc(Native.GMEM_MOVEABLE, (nuint)data.Length);
        if (h == 0) return 0;
        nint p = Native.GlobalLock(h);
        if (p == 0) { Native.GlobalFree(h); return 0; }
        Marshal.Copy(data, 0, p, data.Length);
        Native.GlobalUnlock(h);
        return h;
    }

    /// <summary>
    /// 클립보드를 한 번 열고: 지금 글(있으면)을 읽어 두고, 비우고, 기록 제외 표시 뒤 비밀번호와 소유 표식을 넣는다.
    /// 성공하면 그 소유 토큰, 실패하면 null 이고 error 에 이유가 들어간다 (T17):
    ///  - 글 형식이 있는데 읽지 못하면 비우기 전에 멈춘다(클립보드 그대로).
    ///  - 비운 뒤 표시·쓰기·표식이 실패하면 **같은 열기 안에서** 읽어 둔 글을 되돌린다. 그것도 못 하면 "비워졌다"고 정확히 알린다.
    /// 되살릴 글·비밀번호·토큰의 메모리는 모두 비우기 전에 준비해, 비운 뒤에 할 일은 SetClipboardData 뿐이다.
    /// </summary>
    private static byte[]? SwapInClipboardText(nint owner, string text, out string? saved, out bool hadOther, out string? error)
    {
        saved = null; hadOther = false; error = null;
        uint fmt = OwnerFormat;
        if (fmt == 0) { error = T.InjClipMarker; return null; }
        byte[] token = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        nint hMem = AllocText(text), hTok = AllocBytes(token), hSaved = 0;
        bool opened = false;
        try
        {
            if (hMem == 0 || hTok == 0) { error = T.InjClipNoMem; return null; }
            if (!TryOpenClipboard(owner)) { error = T.InjClipBusy; return null; }
            opened = true;
            // 검증 전용(clip.ps1): 시험 도구가 넘긴 실행 표식이 그대로가 아니면 그 사이 다른 프로그램이 썼다. 아무것도 바꾸지 않는다 (Codex V38-1)
            if (!TestRunIntact()) { _testDisturbed = true; error = "Disturbed"; return null; }

            if (Native.IsClipboardFormatAvailable(Native.CF_UNICODETEXT))
            {
                nint h = TestFails("clip:read") ? 0 : Native.GetClipboardData(Native.CF_UNICODETEXT);
                nint q = h != 0 ? Native.GlobalLock(h) : 0;
                if (q != 0) { try { saved = Marshal.PtrToStringUni(q); } finally { Native.GlobalUnlock(h); } }
                if (saved is null)
                {
                    // 글이 있는데 읽지 못했다. 비우면 그 글을 잃으므로 여기서 멈춘다.
                    error = T.InjClipReadFailed;
                    return null;
                }
                hSaved = AllocText(saved);
                if (hSaved == 0) { error = T.InjClipNoMemKept; return null; }
            }
            hadOther = saved is null && Native.CountClipboardFormats() > TestRunFormatsPresent();   // 검증 전용 표식은 세지 않는다

            if (!Native.EmptyClipboard()) { error = T.InjClipEmptyFailed; return null; }
            // 보호 표시가 안 되면 비밀번호를 넣지 않는다. 표식을 못 넣으면 나중에 우리 것인지 알 수 없으므로 역시 넣지 않는다.
            bool ok = !TestFails("clip:mark") && MarkClipboardSecret();
            if (ok) ok = !TestFails("clip:set") && Native.SetClipboardData(Native.CF_UNICODETEXT, hMem) != 0;
            if (ok) hMem = 0;   // 이제 시스템 소유
            if (ok) ok = !TestFails("clip:token") && Native.SetClipboardData(fmt, hTok) != 0;
            if (ok) { hTok = 0; PutTestRun(_testRun); return token; }

            // 같은 열기 안에서 되돌린다. 비밀번호·표시 형식이 일부 들어갔을 수 있어 한 번 더 비운다.
            bool back;
            if (hSaved != 0)
            {
                back = Native.EmptyClipboard() && !TestFails("clip:restore") && Native.SetClipboardData(Native.CF_UNICODETEXT, hSaved) != 0;
                if (back) hSaved = 0;
            }
            else back = Native.EmptyClipboard() && !hadOther;   // 원래 비어 있었다면 비운 상태가 원래대로다
            PutTestRun(_testRun);
            error = back
                ? T.InjClipGuardRestored
                : T.InjClipGuardEmptied;
            return null;
        }
        finally
        {
            if (hMem != 0) Native.GlobalFree(hMem);
            if (hTok != 0) Native.GlobalFree(hTok);
            if (hSaved != 0) Native.GlobalFree(hSaved);
            if (opened) Native.CloseClipboard();
        }
    }

    private enum ClipRestore { Done, NotOurs, EmptiedOnly, Failed }

    /// <summary>
    /// 입력 뒤 되돌리기. 클립보드를 연 상태에서 우리 소유 표식이 있을 때만 비우고 예전 글을 넣는다. 표식이 없으면(다른 프로그램이
    /// 그 사이 비우거나 복사했으면) 아무것도 하지 않는다. 다른 프로그램이 잡고 있으면 최대 2초 재시도하며, 매 시도마다 다시 확인한다.
    /// </summary>
    private static ClipRestore RestoreClipboard(nint owner, byte[] token, string? saved)
    {
        for (int i = 0; i < 20; i++)
        {
            bool intrude = i == 0 && TestFails("clip:intrude-restore"), busy = intrude || (i == 0 && TestFails("clip:busy-then-ext"));
            ClipRestore? r = busy ? null : TryRestoreOnce(owner, token, saved);
            if (busy) SimulateExternalClipboard(copy: true, marker: intrude ? _testIntruder : _testRun);   // 검증 전용: 재시도 사이에 다른 프로그램이 복사
            if (r is ClipRestore done && done != ClipRestore.Failed) return done;
            Thread.Sleep(100);
        }
        return ClipRestore.Failed;
    }

    /// <summary>한 번 시도. 열지 못하면 null(재시도).</summary>
    private static ClipRestore? TryRestoreOnce(nint owner, byte[] token, string? saved)
    {
        nint hSaved = saved is not null ? AllocText(saved) : 0;
        if (saved is not null && hSaved == 0) return ClipRestore.Failed;
        if (!TryOpenClipboard(owner)) { if (hSaved != 0) Native.GlobalFree(hSaved); return null; }
        try
        {
            switch (CheckOwner(token))
            {
                case Owner.NotOurs: return ClipRestore.NotOurs;     // 다른 프로그램이 바꿨다: 그대로 둔다
                case Owner.ReadFailed: return ClipRestore.Failed;   // 확인하지 못했다: 비우지도 "보존"이라 하지도 않고 재시도 (V36-1)
            }
            if (!Native.EmptyClipboard()) return ClipRestore.Failed;
            PutTestRun(_testRun);
            if (hSaved == 0) return ClipRestore.Done;              // 원래 비어 있었다(또는 글이 아니었다)
            if (Native.SetClipboardData(Native.CF_UNICODETEXT, hSaved) == 0) return ClipRestore.EmptiedOnly;
            hSaved = 0;
            return ClipRestore.Done;
        }
        finally
        {
            if (hSaved != 0) Native.GlobalFree(hSaved);
            Native.CloseClipboard();
        }
    }

    private enum Owner { Ours, NotOurs, ReadFailed }
    private static int _ownerLockFailOnce;

    /// <summary>
    /// 열린 클립보드의 소유를 확인한다 (Codex V36-1). "우리 것이 아님"과 "확인하지 못함"을 구분한다:
    ///  - 표식 형식이 없음, 정상 조회된 크기가 토큰(16바이트)보다 짧음, 앞 16바이트가 토큰과 다름 → NotOurs (다른 프로그램의 내용: 건드리지 않음)
    ///  - 형식은 있는데 GetClipboardData·GlobalLock 이 실패하거나 GlobalSize 가 오류 값 0 → ReadFailed
    ///    (우리 비밀번호가 남았을 수 있다: 재시도하고, 끝내 못 하면 경고)
    ///  GlobalSize 는 요청보다 크게 나올 수 있으므로 "정확히 16"을 요구하지 않고 앞 16바이트만 비교한다.
    /// </summary>
    private static Owner CheckOwner(byte[] token)
    {
        uint fmt = OwnerFormat;
        if (fmt == 0) return Owner.ReadFailed;
        if (!Native.IsClipboardFormatAvailable(fmt)) return Owner.NotOurs;
        nint h = TestFails("clip:owner-read") ? 0 : Native.GetClipboardData(fmt);
        if (h == 0) return Owner.ReadFailed;
        nuint size = TestFails("clip:owner-size0") ? 0 : Native.GlobalSize(h);
        if (size == 0) return Owner.ReadFailed;                     // 0 은 GlobalSize 의 오류 값(잘못된·폐기된 메모리): 확인하지 못함 (V37-1)
        if (size < (nuint)token.Length) return Owner.NotOurs;       // 정상 조회한 크기가 토큰보다 짧다: 우리 것이 아니다
        bool failLock = TestFails("clip:owner-lock") || (TestFails("clip:owner-lock-once") && Interlocked.Exchange(ref _ownerLockFailOnce, 1) == 0);
        nint p = failLock ? 0 : Native.GlobalLock(h);
        if (p == 0) return Owner.ReadFailed;
        try
        {
            for (int i = 0; i < token.Length; i++) if (((byte*)p)[i] != token[i]) return Owner.NotOurs;
            return Owner.Ours;
        }
        finally { Native.GlobalUnlock(h); }
    }

    private enum Sim { Done, NotOurs, Failed }

    /// <summary>
    /// 검증 전용: 다른 프로그램이 클립보드를 비우거나 더미 글을 복사한 것처럼 만든다(다른 소유자 창, 소유 표식 없음).
    /// marker: 시험 도구가 알아보는 표식. 의도한 흉내는 실행 표식, 시험 도구의 끼어듦 검출을 확인할 때는 침입자 표식(글도 "1Key-test-intruder").
    /// clip:foreign-before-sim 이면 그 앞에 한 번 "제3의 프로그램"(세 번째 표식, 글 "1Key-test-foreign")이 먼저 쓴다.
    /// </summary>
    private static Sim SimulateExternalClipboard(bool copy, bool fakeOwner = false, byte[]? marker = null)
    {
        if (TestFails("clip:foreign-before-sim") && !_foreignDone)
        {
            _foreignDone = true;
            Sim f = SimWrite(copy: true, fakeOwner: false, _testForeign);
            if (f != Sim.Done) return f;
        }
        return SimWrite(copy, fakeOwner, marker);
    }

    /// <summary>
    /// 흉내 쓰기 한 번. 흉내도 이번 실행의 표식이 있을 때만, 같은 열기 안에서 확인한 뒤 비우고 쓴다 (Codex V39-1):
    /// 그 사이 실제 다른 프로그램이 썼다면 그 내용을 덮지 않고 NotOurs(시험 방해). 준비·비우기·쓰기가 하나라도 안 되면 Failed
    /// (끼어듦을 만들지 못했는데 시나리오가 진행된 것처럼 보이지 않도록). 비운 뒤에 실패하면 다시 비우고 실행 표식만 남긴다
    /// (시험 도구의 -5 와 같다: 반쪽 상태를 남기지 않고, 도구가 계속 알아볼 수 있게).
    /// </summary>
    private static Sim SimWrite(bool copy, bool fakeOwner, byte[]? marker)
    {
        string text = marker is not null && marker == _testIntruder ? "1Key-test-intruder"
                    : marker is not null && marker == _testForeign ? "1Key-test-foreign" : "1Key-test-external";
        nint other = CreateClipOwner();
        nint h = copy ? AllocText(text) : 0, t = copy && fakeOwner ? AllocBytes(new byte[16]) : 0, hm = marker is not null ? AllocBytes(marker) : 0;
        try
        {
            if (other == 0 || (copy && h == 0) || (copy && fakeOwner && t == 0) || (marker is not null && hm == 0)) { _simFailed = true; return Sim.Failed; }
            if (!TryOpenClipboard(other)) { _simFailed = true; return Sim.Failed; }
            try
            {
                if (!TestRunIntact()) { _testDisturbed = true; return Sim.NotOurs; }
                if (TestFails("clip:sim-fail") || !Native.EmptyClipboard()) { _simFailed = true; return Sim.Failed; }
                bool ok = !TestFails("clip:sim-write-fail") && (hm == 0 || GiveToClipboard(TestRunFormat, ref hm));
                // 사용자의 클립보드 기록을 어지럽히지 않도록 기록 제외를 먼저
                if (ok && copy) ok = SetDwordFormat(RegisterFormat("CanIncludeInClipboardHistory"), 0) && GiveToClipboard(Native.CF_UNICODETEXT, ref h);
                if (ok && t != 0) ok = GiveToClipboard(OwnerFormat, ref t);   // 같은 이름의 표식 형식이지만 다른 토큰(우리 것이 아님)
                if (!ok)
                {
                    _simFailed = true;
                    if (Native.EmptyClipboard()) PutTestRun(_testRun);
                    return Sim.Failed;
                }
                return Sim.Done;
            }
            finally { Native.CloseClipboard(); }
        }
        finally
        {
            if (h != 0) Native.GlobalFree(h);
            if (t != 0) Native.GlobalFree(t);
            if (hm != 0) Native.GlobalFree(hm);
            if (other != 0) Native.DestroyWindow(other);
        }
    }

    /// <summary>열린 클립보드에 한 형식을 쓴다. 성공하면 메모리는 시스템 소유가 되므로 h 를 0 으로 만든다.</summary>
    private static bool GiveToClipboard(uint format, ref nint h)
    {
        if (format == 0 || Native.SetClipboardData(format, h) == 0) return false;
        h = 0;
        return true;
    }

    private static uint RegisterFormat(string name) { fixed (char* n = name) return Native.RegisterClipboardFormatW(n); }

    /// <summary>
    /// 진단 글(최근 채우기 결과 등, 값 없음)을 클립보드에 평범한 글로 넣는다. 비밀번호를 넣을 때와 달리 보호 표시·되돌리기는 하지 않는다
    /// (사용자가 직접 [복사]를 누른 글이고, 다른 곳에 붙여 넣으라고 주는 것이다).
    /// </summary>
    public static bool CopyPlainText(nint owner, string text)
    {
        nint h = AllocText(text);
        if (h == 0) return false;
        if (!TryOpenClipboard(owner)) { Native.GlobalFree(h); return false; }
        try
        {
            if (!Native.EmptyClipboard() || Native.SetClipboardData(Native.CF_UNICODETEXT, h) == 0) { Native.GlobalFree(h); return false; }
            return true;   // 이제 시스템 소유
        }
        finally { Native.CloseClipboard(); }
    }

    private static nint AllocText(string text)
    {
        nuint bytes = (nuint)((text.Length + 1) * sizeof(char));
        nint hMem = Native.GlobalAlloc(Native.GMEM_MOVEABLE, bytes);
        if (hMem == 0) return 0;
        nint p = Native.GlobalLock(hMem);
        if (p == 0) { Native.GlobalFree(hMem); return 0; }
        fixed (char* src = text) Buffer.MemoryCopy(src, (void*)p, (long)bytes, text.Length * sizeof(char));
        ((char*)p)[text.Length] = '\0';
        Native.GlobalUnlock(hMem);
        return hMem;
    }

    /// <summary>
    /// 검증용 실패 주입(ONEKEY_TEST=1 일 때만). ONEKEY_TEST_FAIL=clip:read,clip:mark,clip:set,clip:token,clip:restore,
    /// clip:ext-copy,clip:ext-empty,clip:busy-then-ext,clip:owner-read,clip:owner-lock,clip:owner-lock-once,clip:owner-size0,
    /// clip:intrude-first,clip:intrude-restore (침입자 표식으로 다른 프로그램의 끼어듦을 흉내),
    /// clip:foreign-before-sim (흉내 바로 앞에 제3의 프로그램이 씀), clip:sim-fail (흉내가 비우기 전에 실패), clip:sim-write-fail (흉내가 비운 뒤
    /// 형식 쓰기에서 실패), clip:testmark-once (시험 표식 쓰기 한 번 실패)
    /// </summary>
    private static bool TestFails(string point) => Program.TestFails(point);

    // 검증 전용(clip.ps1, Codex V38-1): 시험 도구가 실행마다 만든 16바이트 표식(16진수 32자). 시험 중 앱이 클립보드에 쓸 때마다
    // 함께 넣고, 처음 열 때 그대로인지 확인한다. 다른 프로그램이 쓰면 클립보드가 비워져 표식이 사라지므로 끼어듦을 알 수 있다.
    // 침입자 표식은 시험 도구가 "끼어듦 검출"을 스스로 확인할 때 흉내 낸 다른 프로그램의 표시다. 둘 다 없으면 아무 일도 하지 않는다.
    // 제3 표식(ONEKEY_TEST_CLIP_FOREIGN): 흉내 직전에 끼어드는 "실제 다른 프로그램"의 대역. 실행 표식도 침입자 표식도 아니라서
    // 흉내와 시험 도구의 되찾기가 모두 거부해야 한다. 시험 도구는 그 사례 끝에서만 이 표식을 명시해 클립보드를 되찾는다.
    private static readonly byte[]? _testRun = TestBytes("ONEKEY_TEST_CLIP_RUN"), _testIntruder = TestBytes("ONEKEY_TEST_CLIP_INTRUDER"),
        _testForeign = TestBytes("ONEKEY_TEST_CLIP_FOREIGN");
    private static bool _testDisturbed, _simFailed, _testMarkFailed, _foreignDone;
    private static int _testMarkFailOnce;

    private static byte[]? TestBytes(string env)
    {
        if (!Program.IsTestMode) return null;
        try { byte[] b = Convert.FromHexString(Environment.GetEnvironmentVariable(env) ?? ""); return b.Length == 16 ? b : null; }
        catch (FormatException) { return null; }
    }

    private static uint TestRunFormat => RegisterFormat("1Key.TestRun");

    private static void PutTestRun(byte[]? marker)
    {
        if (marker is null) return;
        bool fail = TestFails("clip:testmark-once") && Interlocked.Exchange(ref _testMarkFailOnce, 1) == 0;
        nint h = fail ? 0 : AllocBytes(marker);
        if (h != 0 && GiveToClipboard(TestRunFormat, ref h)) return;
        if (h != 0) Native.GlobalFree(h);
        _testMarkFailed = true;   // 시험 표식을 못 넣었다: 시험 도구에는 "다른 프로그램이 끼어듦"과 구분해 알린다 (Codex V39 권고)
    }

    private static int TestRunFormatsPresent() => _testRun is not null && Native.IsClipboardFormatAvailable(TestRunFormat) ? 1 : 0;

    /// <summary>열린 클립보드에 이번 실행의 표식이 그대로 있는가. 시험 도구가 표식을 넘기지 않았으면 늘 참.</summary>
    private static bool TestRunIntact()
    {
        if (_testRun is null) return true;
        uint f = TestRunFormat;
        if (f == 0 || !Native.IsClipboardFormatAvailable(f)) return false;
        nint h = Native.GetClipboardData(f);
        if (h == 0 || Native.GlobalSize(h) < (nuint)_testRun.Length) return false;
        nint p = Native.GlobalLock(h);
        if (p == 0) return false;
        try
        {
            for (int i = 0; i < _testRun.Length; i++) if (((byte*)p)[i] != _testRun[i]) return false;
            return true;
        }
        finally { Native.GlobalUnlock(h); }
    }

    /// <summary>
    /// 검증 전용(ONEKEY_TEST=1): 키 입력 없이 클립보드 교체와 되돌리기만 실행한다. 대상 창에는 아무것도 보내지 않는다.
    /// 반환: 0 = 교체 뒤 되돌림(Done) 또는 다른 프로그램 내용 보존(NotOurs), 3 = 교체 거절, 4 = 되돌리기 실패, 5 = 비우기만 함,
    /// 6 = 시험 도구의 실행 표식이 없어졌다: 다른 프로그램이 끼어들었고, 그 내용은 덮지 않았다(Disturbed, V38-1·V39-1),
    /// 7 = 시험 자체를 만들지 못했다: 흉내 실패(SimulationFailed) 또는 시험 표식 쓰기 실패(TestMarkerFailed). 비밀번호 더미의 되돌리기는
    ///     **시도한 뒤**이고, 성공했다고 보장하지 않는다. 그 결과를 message 에 함께 적는다: "SimulationFailed; restore=Done|NotOurs|EmptiedOnly|Failed|none"
    ///     (none = 마지막의 RestoreClipboard 를 부르지 않았음. 교체 거절 안의 같은 열기 되돌리기 등은 이 값이 나타내지 않는다. Codex V40·V42).
    /// message 에는 거절 이유 또는 되돌리기 결과 이름. 6·7 은 결과보다 먼저 알린다.
    /// </summary>
    public static int SelfTestClipboard(string text, out string? message)
    {
        nint owner = CreateClipOwner();
        try
        {
            int code = SelfTestRun(owner, text, out message, out string restore);
            if (_testDisturbed) { message = "Disturbed"; return 6; }
            if (_simFailed) { message = "SimulationFailed; restore=" + restore; return 7; }
            if (_testMarkFailed) { message = "TestMarkerFailed; restore=" + restore; return 7; }
            return code;
        }
        finally { if (owner != 0) Native.DestroyWindow(owner); }
    }

    private static int SelfTestRun(nint owner, string text, out string? message, out string restore)
    {
        message = null; restore = "none";
        // 앱이 처음 읽기 전에 다른 프로그램이 복사
        if (TestFails("clip:intrude-first") && SimulateExternalClipboard(copy: true, marker: _testIntruder) == Sim.NotOurs) return 6;
        if (SwapInClipboardText(owner, text, out string? saved, out _, out string? error) is not byte[] token) { message = error; return 3; }
        Sim s = Sim.Done;
        if (TestFails("clip:ext-copy")) s = SimulateExternalClipboard(copy: true, marker: _testRun);                                     // 닫은 직후 다른 프로그램이 복사
        if (s == Sim.Done && TestFails("clip:ext-empty")) s = SimulateExternalClipboard(copy: false, marker: _testRun);                  // 닫은 직후 다른 프로그램이 비움
        if (s == Sim.Done && TestFails("clip:ext-fake-owner")) s = SimulateExternalClipboard(copy: true, fakeOwner: true, marker: _testRun);   // 표식은 있으나 토큰이 다름
        // 흉내 전에 실제로 다른 프로그램이 썼다: 우리 비밀번호도 그때 지워졌으므로 되돌릴 것이 없고, 그 내용은 건드리지 않는다
        if (s == Sim.NotOurs) return 6;
        ClipRestore r = RestoreClipboard(owner, token, saved);   // 흉내를 만들지 못했어도 비밀번호 더미의 되돌리기는 시도한다
        message = restore = r.ToString();
        return r switch { ClipRestore.Done or ClipRestore.NotOurs => 0, ClipRestore.EmptiedOnly => 5, _ => 4 };
    }

    /// <summary>클립보드 기록(Win+V)과 클라우드 동기화에서 제외되도록 표시한다. 하나라도 못 하면 false (비밀번호를 넣지 않는다).</summary>
    private static bool MarkClipboardSecret()
    {
        fixed (char* a = "ExcludeClipboardContentFromMonitorProcessing")
        fixed (char* b = "CanIncludeInClipboardHistory")
        fixed (char* c = "CanUploadToCloudClipboard")
        {
            uint fa = Native.RegisterClipboardFormatW(a), fb = Native.RegisterClipboardFormatW(b), fc = Native.RegisterClipboardFormatW(c);
            return fa != 0 && fb != 0 && fc != 0 && SetDwordFormat(fa, 1) && SetDwordFormat(fb, 0) && SetDwordFormat(fc, 0);
        }
    }

    private static bool SetDwordFormat(uint format, uint value)
    {
        nint h = Native.GlobalAlloc(Native.GMEM_MOVEABLE, sizeof(uint));
        if (h == 0) return false;
        nint p = Native.GlobalLock(h);
        if (p == 0) { Native.GlobalFree(h); return false; }
        *(uint*)p = value;
        Native.GlobalUnlock(h);
        if (Native.SetClipboardData(format, h) == 0) { Native.GlobalFree(h); return false; }
        return true;
    }

    private static bool TryOpenClipboard(nint owner)
    {
        for (int i = 0; i < 10; i++)
        {
            if (Native.OpenClipboard(owner)) return true;
            Thread.Sleep(20);
        }
        return false;
    }

    // ---------------------------------------------------------------- 공통

    /// <summary>단축키를 누르고 있는 상태를 해제한다. 이걸 안 하면 대부분의 창에서 입력이 씹힌다.</summary>
    /// <summary>
    /// 단축키에 Alt 가 들어 있으면, 마지막 키는 Windows 가 삼키기 때문에 대상 프로그램은
    /// "Alt 만 눌렀다 뗀 것"으로 보고 메뉴 모드(메뉴 표시줄 활성화)에 들어간다. 그러면 바로 뒤에 넣는
    /// 첫 글자가 메뉴 조작으로 먹혀 사라진다. Alt 가 아직 눌려 있는 동안 무해한 Ctrl 을 한 번 눌렀다 떼면
    /// "Alt 와 함께 다른 키가 눌렸다"가 되어 메뉴 모드가 켜지지 않는다. (AutoHotkey 와 같은 기법)
    /// </summary>
    public static void PreventAltMenu()
    {
        if ((Native.GetAsyncKeyState(VK_MENU) & 0x8000) == 0) return;
        // Alt 가 눌린 동안 왼쪽 Ctrl 을 한 번 눌렀다 뗀다. 글자 입력칸에서는 아무 일도 하지 않는 키다.
        KeyEvent(VK_CONTROL, true, false);
        KeyEvent(VK_CONTROL, false, false);
    }

    /// <summary>사이트 채우기가 칸을 누르기 전에: 사용자가 아직 누르고 있는 단축키 조합(Ctrl·Alt 등)을 뗀다(누름이 Ctrl+클릭이 되지 않게).</summary>
    public static void ReleaseHeldModifiers() => ReleaseModifiers();

    /// <summary>
    /// 사이트 채우기의 마지막 Enter(단축키로 연결 페이지를 채운 경우, 항목의 "입력 후 Enter" 를 따른다). 같은 잠금 세대·제한 시각,
    /// 같은 창·같은 포커스 핸들, 그리고 페이지 안 포커스가 마지막으로 채운 그 칸일 때만 보낸다.
    /// </summary>
    /// <summary>
    /// 사이트 채우기 전용(2026-10-03 Nexacro 업무 사이트): 방금 채운 칸(req, 포커스가 아직 그 칸일 때만 = guard)에서 Tab 을 한 번 보낸다. 화면 자신(Nexacro)이
    /// 다음 칸으로 커서를 옮기게 해, 프로그램이 옮긴 커서를 되돌리는 화면에서도 클릭 없이 다음 칸으로 간다. 그 뒤 커서가 연결한 칸인지는
    /// 부르는 쪽이 UIA 로 확인한 뒤에만 넣는다. Enter 와 같은 순서(보조 키 떼기 → 확인 → UIA 확인 → 다시 확인)로 보낸다.
    /// </summary>
    public static string? PressTabInField(in Request req, int gen, long deadline, Func<bool>? guard)
    {
        ReleaseModifiers();
        if (CheckRequest(req, gen, deadline) is string e1) return e1;
        if (guard is not null && !guard()) return T.InjFocusMoved;
        if (CheckRequest(req, gen, deadline) is string e2) return e2;
        return TapVirtualKey(Native.VK_TAB);
    }

    /// <summary>
    /// 사이트 채우기 전용(2026-10-03 Nexacro 업무 사이트): 커서가 이 항목의 **다음 입력** 칸에 있을 때(guard) Shift+Tab 한 번으로 앞 칸에 옮긴다. Nexacro 는
    /// 프로그램이 넣는 커서를 거부하고 원래 칸(비밀번호)을 붙잡아 두므로, 화면이 스스로 옮기게 한다. 옮긴 뒤 연결한 칸인지는 부르는 쪽이 확인한다.
    /// </summary>
    public static string? PressShiftTabInField(in Request req, int gen, long deadline, Func<bool>? guard)
    {
        ReleaseModifiers();
        if (CheckRequest(req, gen, deadline) is string e1) return e1;
        if (guard is not null && !guard()) return T.InjFocusMoved;
        if (CheckRequest(req, gen, deadline) is string e2) return e2;
        string? err = KeyEvent(Native.VK_SHIFT, true, false);
        if (err is not null) return err;
        string? up;
        try
        {
            // Shift 를 누른 뒤 Tab 직전에도 잠금·취소·제한 시각·앞의 창을 다시 본다(Codex 04:00 권고 3)
            err = CheckRequest(req, gen, deadline) ?? TapVirtualKey(Native.VK_TAB);
        }
        finally { up = KeyEvent(Native.VK_SHIFT, false, false); }
        // Shift 를 떼지 못했으면(Codex 04:00 권고 4) 이어 가지 않는다: 다음 글자가 대문자·다른 키가 될 수 있다
        if (up is not null) return ShiftReleaseFailed;
        return err;
    }

    /// <summary>잠금 화면 전용: CapsLock 키를 한 번 눌렀다 뗀다(SendInput 두 개가 모두 들어갔으면 true). 다시 보내 고치지 않는다(Codex 08:45-bb).</summary>
    public static bool TapCapsLock()
    {
        Native.INPUT* keys = stackalloc Native.INPUT[2];
        keys[0] = MakeKey(0x14, 0x3A, 0);
        keys[1] = MakeKey(0x14, 0x3A, Native.KEYEVENTF_KEYUP);
        return Native.SendInput(2, keys, sizeof(Native.INPUT)) == 2;
    }

    /// <summary><see cref="PressShiftTabInField"/> 가 Shift 를 떼지 못했을 때의 결과(부르는 쪽은 채우기를 멈춘다).</summary>
    public static string ShiftReleaseFailed => T.InjShiftReleaseFailed;

    public static string? PressEnterInField(in Request req, int gen, long deadline, Func<bool>? guard)
    {
        // 기다림(누르고 있는 Ctrl·Alt 떼기, 최대 1.5초)을 먼저 끝내고, 그 뒤의 확인만 믿는다 (Codex R75-2)
        ReleaseModifiers();
        if (!TestHold("enter.mods")) return Stopped;   // 검증 전용 경계(R75-2 시험): 떼기 대기 끝·UIA 확인 직전·직후
        if (CheckRequest(req, gen, deadline) is string e1) return e1;
        if (!TestHold("enter.guard-in")) return Stopped;
        if (guard is not null && !guard()) return T.InjFocusMoved;
        if (!TestHold("enter.guard-out")) return Stopped;
        if (CheckRequest(req, gen, deadline) is string e2) return e2;   // UIA 확인(최대 1초) 뒤 다시
        if (Program.IsTestMode) Trace("enter-call field");   // 보내기 전에는 키 상태를 읽지 않는다(0.2.99 IJ05)
        string? err = TapVirtualKey(VK_RETURN);
        if (Program.IsTestMode) Trace("enter-result field sendinput=" + (err is null ? "ok" : "failed") + " " + KeyState());
        return err;
    }

    private static void ReleaseModifiers()
    {
        for (int i = 0; i < 75 && AnyModifierDown(); i++) Thread.Sleep(20);   // 최대 1.5초 대기

        foreach (int vk in ModifierKeys)
            if ((Native.GetAsyncKeyState(vk) & 0x8000) != 0)
                KeyEvent(vk, false, false);

        Thread.Sleep(20);
    }

    private static bool AnyModifierDown()
    {
        foreach (int vk in ModifierKeys)
            if ((Native.GetAsyncKeyState(vk) & 0x8000) != 0) return true;
        return false;
    }

    private const nint IMC_GETOPENSTATUS = 0x0005;

    /// <summary>
    /// 대상 창의 IME 를 영문 상태로 돌린다. 실패해도 무시한다.
    /// 한글 상태였다면 전환이 끝날 시간을 준다. 전환 직후에 보낸 첫 글자들이 IME 조합에 먹혀
    /// 사라지는 일이 있었다(예: "P@ssw0rd" 가 "ssw0rd" 로 들어감).
    /// </summary>
    private static void TurnOffIme(nint target)
    {
        try
        {
            nint ime = ImmGetDefaultIMEWnd(target);
            if (ime == 0) return;
            SendMessageTimeoutW(ime, WM_IME_CONTROL, IMC_GETOPENSTATUS, 0, SMTO_ABORTIFHUNG, 200, out nint open);
            SendMessageTimeoutW(ime, WM_IME_CONTROL, IMC_SETOPENSTATUS, 0, SMTO_ABORTIFHUNG, 200, out _);
            Thread.Sleep(open != 0 ? 150 : 30);
        }
        catch { /* IME 가 없는 환경 */ }
    }

    private static string? TapVirtualKey(int vk) => KeyEvent(vk, true, true) ?? KeyEvent(vk, false, true);

    /// <summary>창 메시지 방식의 Tab: 누름·뗌만 보낸다(대상의 대화 상자 처리가 다음 칸으로 옮긴다).</summary>
    private static string? PostTab(nint focus)
        => Native.PostMessageW(focus, Native.WM_KEYDOWN, Native.VK_TAB, 0x000F0001) && Native.PostMessageW(focus, Native.WM_KEYUP, Native.VK_TAB, unchecked((nint)0xC00F0001))
            ? null : T.InjWmFailed;

    /// <summary>
    /// 입력 여러 개 항목: 지금 칸의 글을 모두 고른다(다음 글자가 그 자리를 바꿔 쓴다). 대상이 바뀌었으면 보내지 않는다.
    /// 에디트 계열 컨트롤(클래스 이름에 "edit": Edit, RichEdit, WindowsForms…EDIT, TEdit 등)은 EM_SETSEL 을 그 칸에 게시한다:
    /// 옛 EDIT 컨트롤은 Ctrl+A 를 모두 선택으로 처리하지 않는다(0.2.65 inject 시험 IJ36 에서 실제로 확인). 게시한 메시지는
    /// 뒤따르는 키 입력보다 먼저 처리된다. 창 메시지 방식도 EM_SETSEL. 그 밖(브라우저 등 한 창에 그리는 화면)은 Ctrl+A.
    /// </summary>
    private static string? SelectAllIn(in Target t, InputMethod method)
    {
        bool message = method == InputMethod.PostMessage || Native.GetClassName(t.Focus).Contains("edit", StringComparison.OrdinalIgnoreCase);
        if (!message) ReleaseModifiers();   // 기다림을 먼저 (R75-2)
        if (!StillOnTarget(t)) return Stopped;
        if (message) return Native.PostMessageW(t.Focus, Native.EM_SETSEL, 0, -1) ? null : T.InjWmFailed;
        string? err = KeyEvent(VK_CONTROL, true, false);
        if (err is not null) return err;
        try { err = TapVirtualKey(0x41); }   // A
        finally { KeyEvent(VK_CONTROL, false, false); }
        Thread.Sleep(30);
        return err;
    }

    /// <summary>
    /// 사이트 채우기 전용: 이미 UIA 로 확인한 그 입력란(req 의 창·포커스가 그대로일 때만)의 글을 Ctrl+A 로 모두 고른다.
    /// 브라우저가 미리 채워 둔 값이 있어도 이어 붙이지 않고 바꿔 쓰도록. 창·포커스가 바뀌었으면 아무 키도 보내지 않고 이유를 돌려준다.
    /// </summary>
    public static string? SelectAllInField(in Request req, int gen, long deadline = 0, Func<bool>? guard = null)
    {
        // 잠금·취소 세대(V55-4)·제한 시각(C61-1)·창·포커스, 사이트 채우기는 UIA 로 그 칸인지까지. 기다림을 먼저 끝내고 확인한다 (R75-2)
        ReleaseModifiers();
        if (!TestHold("selall.mods")) return Stopped;   // 검증 전용 경계(R75-2 시험)
        if (CheckRequest(req, gen, deadline) is string e1) return e1;
        if (!TestHold("selall.guard-in")) return Stopped;
        if (guard is not null && !guard()) return T.InjFocusMoved;
        if (!TestHold("selall.guard-out")) return Stopped;
        if (CheckRequest(req, gen, deadline) is string e2) return e2;
        string? err = KeyEvent(VK_CONTROL, true, false);
        if (err is not null) return err;
        try { err = TapVirtualKey(0x41); }   // A
        finally { KeyEvent(VK_CONTROL, false, false); }
        Thread.Sleep(30);
        return err;
    }

    /// <summary>키 하나를 누르거나 뗀다. useScanCode 가 true 면 하드웨어 스캔코드로 보낸다.</summary>
    private static string? KeyEvent(int vk, bool down, bool useScanCode, nint hkl = 0)
    {
        uint flags = down ? 0 : Native.KEYEVENTF_KEYUP;
        ushort scan = 0;
        ushort sendVk = (ushort)vk;

        if (useScanCode)
        {
            scan = (ushort)(hkl != 0 ? Native.MapVirtualKeyExW((uint)vk, MAPVK_VK_TO_VSC, hkl) : Native.MapVirtualKeyW((uint)vk, MAPVK_VK_TO_VSC));
            if (scan != 0)
            {
                // 스캔코드로 보낼 때 wVk 는 0 이어야 실제 키보드 입력과 동일하게 취급된다.
                flags |= Native.KEYEVENTF_SCANCODE;
                sendVk = 0;
            }
        }

        Native.INPUT input = MakeKey(sendVk, scan, flags);
        return Dispatch(&input, 1);
    }

    private static Native.INPUT MakeKey(ushort vk, ushort scan, uint flags) => new()
    {
        type = Native.INPUT_KEYBOARD,
        U = new Native.INPUTUNION
        {
            ki = new Native.KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags, time = 0, dwExtraInfo = 0 }
        }
    };

    /// <summary>SendInput 호출. 차단된 경우를 사용자에게 알려 줄 수 있도록 오류를 해석한다.</summary>
    /// <summary>검증 관찰값: 지금까지 SendInput 으로 들어간 입력 수(키 누름·뗌 하나씩, Codex 09:45-be: 제품이 보낸 키를 따로 센다).</summary>
    internal static int TestInputsSent;

    private static string? Dispatch(Native.INPUT* inputs, uint count)
    {
        uint sent = Native.SendInput(count, inputs, sizeof(Native.INPUT));
        Interlocked.Add(ref TestInputsSent, (int)sent);
        if (sent == count) return null;

        int err = Marshal.GetLastWin32Error();
        if (err == 5)   // ERROR_ACCESS_DENIED
            return T.InjBlockedElevated;
        return T.InjBlocked(err);
    }

    private static void Delay(int ms)
    {
        if (ms > 0) Thread.Sleep(ms);
    }
}
