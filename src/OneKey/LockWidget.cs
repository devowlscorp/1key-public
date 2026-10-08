using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 잠금·처음 설정 입력 화면의 마스코트 위젯(디자인 개편 B, docs/design/2026-10-03-B-상세/B_확정안.md). 2단계(Codex 16:14 R-W1~5):
/// App 이 잠긴 상태에서 본창 대신 이 위젯을 보인다(본창은 숨긴 채 기존 잠금 화면을 그대로 두고, 위젯이 실패하면 그 화면이 대체 화면이 된다).
/// 시험 모드 데모(`1Key.exe --lockwidget-demo`)도 그대로 있다.
///
/// 구조(Codex B-W01 권고: 보이는 실제 비밀번호 입력칸):
/// - 위젯 창(OneKeyLockWidget): 창 바탕이 없는 층 창(WS_EX_LAYERED + UpdateLayeredWindow, 픽셀별 알파). 마스코트·알약 외곽과 바탕·
///   화살표·− ×·말풍선·안내를 GDI+ 로 한 장에 그려 올린다. 알파 0 인 곳은 클릭이 뒤의 창으로 간다(창 전체 WS_EX_TRANSPARENT 는 쓰지 않음).
///   작업 표시줄·Alt+Tab 항목은 이 창 하나다.
/// - 입력 창(OneKeyLockInput): 위젯이 소유한 작은 직사각형 창. 안에 **보이는 실제 EDIT(ES_PASSWORD)** 가 있다. 알약의 글자 자리(둥근 끝
///   안쪽)에 겹쳐 놓고, 바탕은 알약 바탕과 같은 색·같은 불투명도(좁을 때 20%, 넓으면 100% — <see cref="PillAlpha"/>; 좁고 빈 칸이면 입력 창은 완전히 투명 — <see cref="HostHidden"/>)라 이음매가 보이지 않는다. 입력·IME·선택·붙여넣기·화면 읽기는 표준 EDIT 그대로.
/// - 상태: 좁음 ↔ 넓음 0.22초. 넓음 = 위젯·입력 창·위젯이 소유한 창이 앞의 창이고 **사용자가 입력을 시작함**(칸 클릭·키 입력·화살표).
///   위젯을 열면 커서는 칸에 있지만 좁게 시작한다(2026-10-03 사용자: 평소 가리는 면 최소). 다른 창으로 가면 다시 좁게. 넓음에서만 말풍선·한/영·Caps·안내가 보인다.
///   마스코트 인사는 넓음이거나 마스코트에 마우스가 있는 동안만 초당 10장(19장 1.9초 반복), 그 밖에는 첫 장면에서 멈추고 타이머가 없다.
///   Windows 애니메이션 끔과 상관없이 움직인다(사용자 결정 2026-10-03, 칩·알림과 같은 예외 — 숨김·최소화 중에는 멈춤).
/// - 마스코트는 위젯 안에서 늘 맨 앞에 그린다(그리기 순서일 뿐, 전역 최상위가 아님 — B-W02).
/// 비밀번호 값은 이 클래스가 읽지 않는다(그릴 때도 쓰지 않음). 잠금 해제는 부르는 쪽이 EDIT 에서 읽는다(기존 잠금 화면과 같은 방식 —
/// 검증·키 유도 중에는 그 문자열이 다른 메모리에도 있다. 이 화면이 복사를 더 만들지는 않는다).
/// 알림(WM_LOCKWIDGET)의 lParam 은 위젯 인스턴스 번호다(R-W3): 부르는 쪽은 <see cref="Instance"/> 와 다르면 늦게 온 알림으로 보고 버린다.
/// </summary>
internal static unsafe class LockWidget
{
    public const string ClassName = "OneKeyLockWidget", InputClass = "OneKeyLockInput";
    public const int IdEdit1 = 101, IdEdit2 = 102;      // 지금 잠금 화면 칸과 같은 ID(하네스·본창 규약)
    public const uint WM_LOCKWIDGET = 0x8000 + 60;       // 부르는 쪽에 알림: wParam 1 = 잠금 해제/시작 요청, 2 = × (종료 묻기), 3 = 칸 포커스 들어옴,
                                                         // 4 = 그리기 실패로 위젯을 닫음(부르는 쪽은 기존 잠금 화면을 연다 — B-W04)
    private const uint WM_RECALC = 0x8000 + 61, WM_FAILED = 0x8000 + 62, WM_ACT = 0x8000 + 63, WM_ONSCREEN = 0x8000 + 64;
    private const uint WM_TEST_FAIL_NOW = 0x8000 + 65;   // 검증 전용(ONEKEY_TEST=1): 다음 그리기를 실패시킨다(누른 채 실패 — Codex 12:41 배포 전 조건 2)
    /// <summary>시험 모드 주입: n 번째 그리기를 실패로 만든다(1 = 처음 보일 때 → Show 가 false, 그 뒤 = 보이는 중 실패 → 알림 4). 0 = 끔.</summary>
    public static int TestFailAt = Program.IsTestMode && int.TryParse(Environment.GetEnvironmentVariable("ONEKEY_TEST_LOCKWIDGET_FAIL"), out int tf) ? tf : 0;   // 통합 시험: 환경 변수로도
    private static int _renders;
    private static bool _failed, _live;
    private const nuint TimerAnim = 1, TimerGreet = 2, TimerBackdrop = 3;
    private const int GreetMs = MascotGreet.FrameMs, ExpandMs = 220;   // 0.3.121: 인사 16 fps(예전 19장 100 ms)
    private static int Frames = 19;   // 인사 그림 장 수(mascot_greet.txt)

    // ---- 상태
    private static bool _registered, _create, _dark;
    private static nint _hwnd, _notify;
    private static readonly nint[] _host = new nint[2], _edit = new nint[2], _oldEditProc = new nint[2];
    private static uint _dpi = 96;
    private static float _s = 1;                 // dpi / 96
    private static int _cw, _ch;                 // 캔버스(창) 크기, 실제 픽셀
    private static double _e, _eFrom, _eTo;       // 넓어짐 0..1
    private static long _animStart;
    private static bool _hover, _tracking, _pressed;
    private static bool _hoverDone;   // 마우스를 올려 시작한 인사가 한 바퀴 끝났다(올린 채로 있으면 서 있는다 — 다시 올리면 또 한 번, 0.3.123)
    // 넓어질 때의 야옹(공개판, 2026-10-08 사용자: 마스터 비밀번호를 넣도록 넓어지면 입을 벌리고 야옹): 한 번만 하고 정면으로 선다.
    // 좁아지면 다시 할 수 있다. 입 그림이 없으면(MeowN = 0) 예전처럼 넓은 동안 둘러보기를 되풀이한다
    private static bool _meowDone;
    private static bool Wide => _eTo > 0;
    private static bool Meowing => Wide && MascotGreet.MeowN > 0;
    private static int _pressedPart;             // 1 = −, 2 = ×, 3 = 화살표, 6 = 위젯 모드

    /// <summary>
    /// − 와 × 사이의 위젯 모드 단추(0.3.124, 2026-10-07 사용자: 잠긴 상태에서 설정의 위젯 사용 여부와 상관없이 누르면 위젯 모드로 내려가게).
    /// 누르면 잠금 위젯을 닫고 작업 표시줄 마스코트가 나온다. 마스코트를 누르면 다시 이 잠금 위젯. 처음 설정(마스터 만들기)에는 없다.
    /// </summary>
    internal static bool WidgetBtn => !_create;
    /// <summary>머리 위 둥근 단추의 가운데 x(논리 px): 1 = −, 6 = 위젯 모드, 2 = ×. 위젯 단추가 없으면 − × 둘이 가운데에 붙는다.</summary>
    private static float CtlCx(int part) => WidgetBtn
        ? part switch { 1 => W / 2 - CtlD - 4, 6 => W / 2, _ => W / 2 + CtlD + 4 }
        : part == 1 ? W / 2 - 2 - CtlD / 2 : W / 2 + 2 + CtlD / 2;
    private static int _pressX, _pressY;         // 누른 자리(클라이언트 픽셀): 넓어지는 중에는 버튼이 움직이므로 뗄 때 이 자리와 비교한다
    private static int _frame;
    private static string _kbShown = "";
    private static int _instance;                // Show 할 때마다 1 씩(알림의 lParam, R-W3)
    private static bool _modal;                  // 부르는 쪽의 확인 창이 떠 있는 동안: 입력 창·버튼·접근성 실행을 막는다
    private static bool _engaged;                // 사용자가 칸을 누르거나 키를 쳤다: 그때부터 넓게(2026-10-03 사용자 — 처음에는 좁게 시작)
    private static Native.POINT? _anchor;        // 위젯 가운데가 올 화면 좌표(잠글 때 본창 가운데, 위젯을 끌어 옮긴 자리). lockwidget.pos 에 저장

    // ---- 그리기 자원
    private static nint _mem, _dib, _oldBmp, _bits, _canvas, _sprite, _editFont;
    private static int _spriteW, _spriteH;
    private static nint _famRegular, _fontBody, _fontSmall, _fontBold, _fontTiny, _fmtCenter, _fmtLeft, _fmtRight;
    private static nint _famIcon, _fontIcon;   // 위젯 모드 단추: 본창 단추와 같은 Segoe Fluent Icons EaseOfAccess(E776)(0.3.124 사용자)
    private static nint _hostBrush;
    private static float _placeholderW;

    // ================================================================== 공개 API

    /// <summary>
    /// 위젯을 만들어 보인다. notify = 알림을 받을 창(0 이면 데모: 스스로 처리). create = 처음 설정(칸 두 개). activate = 사용자 동작으로 연 경우만
    /// true(앞으로 가져오고 칸에 커서). 그리기 자원·층 창을 만들지 못하면 false(부르는 쪽은 기존 잠금 화면을 쓴다 — B-W04).
    /// </summary>
    public static bool Show(nint notify, bool create, bool activate, nint nearWindow = 0)
    {
        Hide();
        _instance++;
        _notify = notify; _create = create; _dark = Theme.IsDark; _modal = false; _engaged = false;
        nint hInst = Native.GetModuleHandleW(null);
        if (!_registered)
        {
            Ctl.RegisterClass(hInst, ClassName, &WndProc, 0);
            Ctl.RegisterClass(hInst, InputClass, &HostProc, 0);
            _registered = true;
        }
        // 기억한 자리가 있으면 그 자리 모니터의 작업 영역·배율로 처음부터 만든다(Codex R148-1: 본창과 배율이 다른 모니터에 옮겨 둔 경우)
        Native.POINT? anchor = notify != 0 ? Anchor() : null;
        Native.RECT work = anchor is Native.POINT ap ? WorkArea.ForPoint(ap) : WorkArea.For(nearWindow);
        _dpi = anchor is Native.POINT ap2 ? WorkArea.DpiForPoint(ap2) : nearWindow != 0 ? WorkArea.DpiFor(nearWindow) : Native.GetDpiForSystem(); if (_dpi == 0) _dpi = 96;
        _s = _dpi / 96f;
        if (!LoadResources()) { FreeResources(); return false; }
        Layout();
        int x = work.left + (work.right - work.left - _cw) / 2;
        int y = work.top + Math.Max(0, (work.bottom - work.top) / 8);   // 기준점이 없을 때(처음): 화면 위쪽 가운데
        if (anchor is Native.POINT a) { x = a.x - _cw / 2; y = a.y - _ch / 2; }   // 본창이 있던 자리·옮겨 둔 자리(2026-10-03 사용자)
        fixed (char* cls = ClassName) fixed (char* cap = "1Key")
            _hwnd = Native.CreateWindowExW(WS_EX_LAYERED | Native.WS_EX_APPWINDOW, cls, cap, Native.WS_POPUP | WS_SYSMENU | WS_MINIMIZEBOX,
                x, y, _cw, _ch, 0, 0, hInst, 0);
        if (_hwnd == 0 || !CreateCanvas()) { Hide(); return false; }
        KeepOnScreen();   // 기억한 자리가 지금 모니터 구성에서 화면 밖이면 안으로
        for (int i = 0; i < (create ? 2 : 1); i++)
        {
            fixed (char* cls = InputClass)
                _host[i] = Native.CreateWindowExW(Native.WS_EX_TOOLWINDOW | WS_EX_LAYERED, cls, null, Native.WS_POPUP, 0, 0, 10, 10, _hwnd, 0, hInst, 0);   // 층 창: 좁을 때 칸과 같은 반투명(PillAlpha)
            // 화면 읽기용 이름: EDIT 바로 앞의 STATIC 이 이름이 된다(MSAA 관례, 기존 잠금 화면과 같은 이름). 크기 0 이라 보이지 않는다.
            fixed (char* sc = "STATIC") fixed (char* lt = i == 0 ? T.LockLabelMaster : T.LockLabelAgain)
                Native.CreateWindowExW(0, sc, lt, Native.WS_CHILD | Native.WS_VISIBLE, 0, 0, 0, 0, _host[i], 0, hInst, 0);
            fixed (char* ec = "EDIT")
                _edit[i] = Native.CreateWindowExW(0, ec, null, Native.WS_CHILD | Native.WS_VISIBLE | Native.WS_TABSTOP | Native.ES_PASSWORD | Native.ES_AUTOHSCROLL,
                    0, 0, 10, 10, _host[i], i == 0 ? IdEdit1 : IdEdit2, hInst, 0);
            if (_host[i] == 0 || _edit[i] == 0) { Hide(); return false; }
            Native.SendMessageW(_edit[i], Native.WM_SETFONT, _editFont, 0);   // 위젯의 배율로 만든 글꼴(모니터를 옮기면 다시 만든다)
            // 길이 제한은 두지 않는다: 기존 잠금 화면의 칸(EDIT 기본값)과 같은 정책이어야 기존에 만든 긴 마스터도 그대로 들어간다(R-W2).
            // 화면 읽기 프로그램용 이름은 칸의 자리표시 글(포커스 중에는 숨김)과 위젯 제목으로 준다
            Native.SetCueBanner(_edit[i], i == 0 ? (create ? T.LockCueNew : T.LockLabelMaster) : T.LockLabelAgain);
            _oldEditProc[i] = Native.SetWindowLongPtrW(_edit[i], -4 /* GWLP_WNDPROC */, (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&EditProc);
        }
        _e = _eFrom = _eTo = 0;   // 늘 좁게 시작한다. 커서는 칸에 있어 바로 칠 수 있고, 치거나 누르면 넓어진다
        _frame = 0;
        _failed = false; _renders = 0;
        Render();
        if (_failed) { Hide(); return false; }   // 처음 그림부터 실패하면 위젯을 쓰지 않는다(부르는 쪽이 기존 잠금 화면으로)
        _live = true;
        PlaceHosts();
        Native.ShowWindow(_hwnd, activate ? Native.SW_SHOW : Native.SW_SHOWNOACTIVATE);
        for (int i = 0; i < 2; i++) if (_host[i] != 0) Native.ShowWindow(_host[i], Native.SW_SHOWNOACTIVATE);
        if (activate)
        {
            // 사용자 동작으로 연 경우만 앞으로 가져온다. 거부되면 그대로 둔다(B-W03·R-W3: 다른 앱의 커서·입력을 바꾸지 않는다).
            // 칸에 커서를 두는 것은 실제로 앞의 창이 됐을 때만이다(위젯 WM_ACTIVATE → 칸).
            Native.SetForegroundWindow(_hwnd);
            if (Working()) Native.SetFocus(_edit[0]);
        }
        Recalc();
        return true;
    }

    /// <summary>위젯·입력 창을 모두 없앤다(잠금 해제·종료·자동 잠금 숨김). 타이머도 함께 멈춘다.</summary>
    public static void Hide()
    {
        _live = false;
        _modal = false;
        LockWidgetAcc.Disconnect();
        for (int i = 0; i < 2; i++)
        {
            if (_host[i] != 0 && Native.IsWindow(_host[i])) Native.DestroyWindow(_host[i]);
            _host[i] = _edit[i] = _oldEditProc[i] = 0;
        }
        if (_hwnd != 0 && Native.IsWindow(_hwnd)) Native.DestroyWindow(_hwnd);
        _hwnd = 0;
        FreeResources();
    }

    public static bool IsShown => _hwnd != 0 && Native.IsWindow(_hwnd);
    public static nint Handle => _hwnd;
    public static nint Edit(int i) => _edit[i];
    /// <summary>지금 위젯의 번호. 알림의 lParam 이 이것과 다르면 이미 닫힌 위젯의 늦은 알림이다(R-W3).</summary>
    public static int Instance => _instance;
    public static bool IsCreate => _create;
    /// <summary>마지막 그리기 실패 때 위젯(또는 그 입력 창)이 앞의 창이었나(알림 4 와 함께 읽는다).</summary>
    public static bool FailedWhileFront { get; private set; }
    public static bool IsModal => _modal;
    public static bool IsMinimized => _hwnd != 0 && Native.IsIconic(_hwnd);
    internal static bool Alive => _live && _hwnd != 0;
    /// <summary>앞의 창이 위젯·입력 창·위젯이 소유한 창(확인 상자)인가. CapsLock·한/영 바꾸기 전에 확인한다(B-W03).</summary>
    public static bool OwnsForeground() => Working();

    /// <summary>
    /// 부르는 쪽이 위젯을 소유자로 확인 창을 띄우는 동안 true: 입력 창을 비활성으로 두고(반복 Enter 로 두 번 제출되지 않게) 버튼·접근성 실행을 막는다.
    /// false 로 돌아오면 입력 창을 다시 쓸 수 있다(커서는 <see cref="FocusEdit"/> 로).
    /// </summary>
    public static void SetModal(bool on)
    {
        _modal = on;
        for (int i = 0; i < 2; i++) if (_host[i] != 0) Native.EnableWindow(_host[i], !on);
        if (_hwnd != 0) Render();
    }

    /// <summary>그 칸에 커서를 둔다. 위젯(또는 그 확인 창)이 앞의 창일 때만(다른 앱을 쓰는 중이면 건드리지 않는다).</summary>
    public static void FocusEdit(int i)
    {
        if (_edit[i] == 0 || !Working()) return;
        Native.SetFocus(_edit[i]);
    }

    /// <summary>다시 열기·다시 잠금 요청으로 이미 떠 있는 위젯을 앞으로 가져올 때: 좁은 상태로 되돌린다(입력한 글은 그대로, 2026-10-03 사용자).</summary>
    public static void Collapse()
    {
        if (!Alive) return;
        if (_modal) return;   // 확인 창이 떠 있는 동안에는 좁히지 않는다: 입력칸·버튼이 움직여 조작을 방해하지 않게(Codex R148-2)
        _engaged = false;
        Recalc();
    }

    /// <summary>칸을 비운다(잘못된 마스터·처음 설정 불일치).</summary>
    public static void ClearEdit(int i)
    {
        if (_edit[i] != 0) Native.SetText(_edit[i], "");
    }

    /// <summary>테마가 바뀌었을 때: 같은 자리·같은 배율로 색만 다시(입력 중인 글과 커서는 그대로).</summary>
    public static void Retheme()
    {
        if (!Alive) return;
        _dark = Theme.IsDark;
        Native.GetWindowRect(_hwnd, out Native.RECT wr);
        Rescale(_dpi, wr.left, wr.top);
        for (int i = 0; i < 2; i++) if (_host[i] != 0) Native.InvalidateRect(_host[i], 0, true);
    }

    /// <summary>접근성 실행: 마우스로 그 버튼을 누른 것과 같은 명령(1 = −, 2 = ×, 3 = 화살표). 메시지로 보내 지금 호출이 끝난 뒤 처리한다.</summary>
    internal static void Act(int part)
    {
        if (_hwnd != 0) Native.PostMessageW(_hwnd, WM_ACT, part, 0);
    }

    /// <summary>그림 부분(1 = −, 2 = ×, 3 = 화살표)의 지금 화면 좌표(애니메이션·배율 반영). 접근성 위치용.</summary>
    internal static bool PartScreenRect(int part, out Native.RECT r)
    {
        r = default;
        if (_hwnd == 0) return false;
        Native.GetWindowRect(_hwnd, out Native.RECT wr);
        var g = GetGeo();
        float cx, cy, d;
        if (part is 1 or 2 || (part == 6 && WidgetBtn)) { cx = CtlCx(part); cy = CtlY + CtlD / 2; d = CtlD; }
        else if (part == 3) { cx = g.ArrowCx; cy = g.ArrowCy; d = g.ArrowD; }
        else return false;
        r.left = wr.left + S(cx - d / 2); r.top = wr.top + S(cy - d / 2);
        r.right = wr.left + S(cx + d / 2); r.bottom = wr.top + S(cy + d / 2);
        return true;
    }

    /// <summary>화면 좌표의 그림 부분 번호(<see cref="PartAt"/> 와 같음).</summary>
    internal static int PartAtScreen(int x, int y)
    {
        if (_hwnd == 0) return 0;
        Native.GetWindowRect(_hwnd, out Native.RECT wr);
        return PartAt(x - wr.left, y - wr.top);
    }

    // ---- 자리 기억(2026-10-03 사용자: 잠글 때 본창이 있던 자리에서 위젯으로, 옮긴 자리는 다시 켜도 기억)

    private static string PosFile => Path.Combine(Config.Dir, "lockwidget.pos");

    private const int PosFileMax = 128, PosLimit = 1_000_000;   // 파일 크기 한도, 좌표 절댓값 한도(가상 화면보다 훨씬 크고 넘침은 없음)

    /// <summary>
    /// 위젯 가운데가 올 자리. 메모리에 없으면 설정 폴더의 lockwidget.pos("x,y", 화면 픽셀)에서 읽는다. 없으면 null.
    /// 128바이트를 넘거나 비었거나 형식이 다르거나 좌표가 한도 밖이면 무시한다(Codex R148-1) — 처음처럼 화면 위쪽 가운데.
    /// </summary>
    private static Native.POINT? Anchor()
    {
        if (_anchor is null)
        {
            try
            {
                var fi = new FileInfo(PosFile);
                if (fi.Exists && fi.Length > 0 && fi.Length <= PosFileMax)
                {
                    string[] v = File.ReadAllText(PosFile).Trim().Split(',');
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    const System.Globalization.NumberStyles ns = System.Globalization.NumberStyles.AllowLeadingSign;
                    if (v.Length == 2 && int.TryParse(v[0], ns, inv, out int ax) && int.TryParse(v[1], ns, inv, out int ay) && InRange(ax, ay))
                        _anchor = new Native.POINT { x = ax, y = ay };
                }
            }
            catch { }
        }
        return _anchor;
    }

    private static bool InRange(int x, int y) => x > -PosLimit && x < PosLimit && y > -PosLimit && y < PosLimit;

    private static void SaveAnchor(int x, int y)
    {
        if (!InRange(x, y)) return;
        _anchor = new Native.POINT { x = x, y = y };
        try { if (Directory.Exists(Config.Dir)) File.WriteAllText(PosFile, x + "," + y); } catch { }   // 비밀이 아닌 자리 정보뿐. 못 써도 이번 실행 동안은 기억한다
    }

    /// <summary>잠글 때 본창이 보이고 있었으면: 다음 위젯은 그 창의 가운데에 뜬다(창이 위젯으로 바뀌는 느낌).</summary>
    public static void AnchorAtWindow(nint hwnd)
    {
        if (hwnd == 0 || !Native.GetWindowRect(hwnd, out Native.RECT r)) return;
        SaveAnchor((r.left + r.right) / 2, (r.top + r.bottom) / 2);
    }

    /// <summary>해상도·작업 영역이 바뀐 뒤 위젯이 화면 밖에 있으면 안으로 옮긴다(R-W5).</summary>
    private static void KeepOnScreen()
    {
        if (_hwnd == 0 || Native.IsIconic(_hwnd)) return;
        Native.GetWindowRect(_hwnd, out Native.RECT wr);
        Native.RECT work = WorkArea.For(_hwnd);
        int w = wr.right - wr.left, h = wr.bottom - wr.top;
        int x = Math.Clamp(wr.left, work.left, Math.Max(work.left, work.right - w));
        int y = Math.Clamp(wr.top, work.top, Math.Max(work.top, work.bottom - h));
        if (x != wr.left || y != wr.top) Native.SetWindowPos(_hwnd, 0, x, y, 0, 0, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | 0x0001 /* SWP_NOSIZE */);
    }

    // ================================================================== 배치

    private static int S(float v) => (int)Math.Round(v * _s);
    private const float W = 380, CtlY = 2, CtlD = 22, MasTop = 32, MasH = 140, BubH = 50, PillH = 44, WideW = 340, HintH = 22;
    private static float MasW => MasH * _spriteW / Math.Max(1, _spriteH);
    private static float NarrowW => 14 + _placeholderW + 14 + 32 + 6;
    private static float BaseTop => MasTop + MasH - 2;

    private static void Layout()
    {
        float h = BaseTop + BubH + 10 + PillH + (_create ? 8 + PillH : 0) + 10 + HintH + 8;
        _cw = S(W); _ch = S(h);
    }

    private readonly record struct Geo(float PillL, float PillW, float PillTop, float Pill2Top, float ArrowD, float ArrowCx, float ArrowCy, float KbRight, float HintTop, float BubTop, float BubW);

    private static Geo GetGeo()
    {
        double e = _e;
        float pw = (float)(NarrowW + (WideW - NarrowW) * e);
        float pl = (W - pw) / 2;
        float pillTop = (float)(BaseTop + (BubH + 10) * e);
        float pill2 = pillTop + PillH + 8;
        float ad = (float)(32 + 4 * e);
        float arrowRowTop = _create ? pill2 : pillTop;
        float acx = pl + pw - 6 - ad / 2, acy = arrowRowTop + PillH / 2;
        float kbRight = pl + pw - 6 - 36 - 8;
        float hintTop = (_create ? pill2 : pillTop) + PillH + 10;
        return new Geo(pl, pw, pillTop, pill2, ad, acx, acy, kbRight, hintTop, MasTop + MasH - 6, 0);
    }

    /// <summary>
    /// 칸(알약) 바탕의 불투명도: 좁을 때 80%, 넓어지면(입력 시작) 100%(2026-10-05 사용자: 흰 칸이 너무 하얗다 — "평소만 반투명", 80%).
    /// 입력 창도 같은 비율(층 창 LWA_ALPHA)로 맞추고, 위젯 그림은 입력 창 자리를 비워 두 겹이 겹치지 않게 한다(겹치면 그 자리만 진해진다).
    /// </summary>
    // 2026-10-05 사용자(0.3.48 설치 뒤): 입력 전 흰 바탕은 투명도 80%(불투명 20%), 입력이 들어가면 불투명. 0.3.35 의 80% 불투명에서 바꿈
    private static double PillAlpha => 0.2 + 0.8 * Math.Clamp(_e, 0, 1);

    /// <summary>
    /// 좁고 빈 칸(입력 전): 입력 창(층 창)은 완전히 투명하게 두고, 알약 바탕(20%)과 자리표시 글은 위젯 그림이 그린다 — 입력 창 전체를 20% 로
    /// 낮추면 자리표시 글·커서까지 흐려져 읽히지 않는다. 입력 창은 보이지 않아도 포커스·입력을 그대로 받고, 치거나 누르면 넓어지며 다시 보인다.
    /// </summary>
    private static bool HostHidden(int i) => _e < 0.05 && _edit[i] != 0 && GetWindowTextLengthW(_edit[i]) == 0;

    /// <summary>입력 창 자리(위젯 창 왼쪽 위 기준 픽셀). 그리기(빈자리)와 배치가 같은 정수 계산을 쓴다.</summary>
    private static (int X, int Y, int W, int H) HostRect(Geo g, int i)
    {
        float top = i == 0 ? g.PillTop : g.Pill2Top;
        bool hasArrow = !_create || i == 1;
        bool hasKb = i == 0;
        float right = g.PillL + g.PillW - 6 - (hasArrow ? 36 : 0) - 14 - (hasKb ? (float)(64 * _e) : 0);
        float left = g.PillL + 14;
        return (S(left), S(top + 10), Math.Max(S(20), S(right - left)), S(PillH - 20));
    }

    /// <summary>입력 창을 알약의 글자 자리에 맞춘다(애니메이션 중에도 지금 그림과 같은 자리 — B-W04).</summary>
    private static void PlaceHosts()
    {
        if (_hwnd == 0) return;
        Native.GetWindowRect(_hwnd, out Native.RECT wr);
        var g = GetGeo();
        for (int i = 0; i < 2; i++)
        {
            if (_host[i] == 0) continue;
            var (hx, hy, ew, eh) = HostRect(g, i);
            int ex = wr.left + hx, ey = wr.top + hy;
            Fx.SetWindowPos(_host[i], 0, ex, ey, ew, eh, 0x0010 /* SWP_NOACTIVATE */ | 0x0004 /* SWP_NOZORDER */);
            // 숨길 때도 0 이 아니라 1: 눈에는 보이지 않지만 클릭은 받는다(0 이면 클릭이 칸을 지나쳐 버렸다 — 2026-10-05 사용자 "칸을 눌러도 반응 없음")
            SetLayeredWindowAttributes(_host[i], 0, HostHidden(i) ? (byte)1 : (byte)Math.Round(255 * PillAlpha), 2 /* LWA_ALPHA */);
            int lineH = S(18);
            Fx.SetWindowPos(_edit[i], 0, 0, Math.Max(0, (eh - lineH) / 2), ew, lineH, 0x0010 | 0x0004);
            Native.SendMessageW(_edit[i], 0x00B7 /* EM_SCROLLCARET */, 0, 0);   // 좁아져도 커서가 보이게 가로 스크롤(긴 마스킹 값, Codex R148-2)
        }
    }

    // ================================================================== 상태

    /// <summary>입력 작업 중인가: 앞의 창이 위젯·입력 창이거나 위젯이 소유한 창(확인 상자)이면 넓게(B-W04: 칸 사이 이동·확인 창으로 줄어들지 않음).</summary>
    private static bool Working()
    {
        nint fg = Native.GetForegroundWindow();
        if (fg == 0 || _hwnd == 0) return false;
        if (fg == _hwnd || fg == _host[0] || fg == _host[1]) return true;
        for (nint o = Native.GetWindow(fg, 4 /* GW_OWNER */); o != 0; o = Native.GetWindow(o, 4)) if (o == _hwnd) return true;
        return false;
    }

    /// <summary>사용자가 입력을 시작함(칸 클릭·키 입력·화살표): 넓어진다. 다른 창으로 가면 다시 좁게(Recalc).</summary>
    private static void Engage(bool click = false)
    {
        if (_hwnd == 0) return;
        if (click) _clickAt = Environment.TickCount64;   // 누른 직후에는 활성화가 아직 옮겨 가는 중이어도 넓힌다(아래 Recalc)
        else if (_engaged) return;
        _engaged = true;
        Recalc();
    }

    private static long _clickAt;

    /// <summary>지금 그릴 인사 띠의 칸: 야옹 중이면 띠 뒤쪽(LookN 부터), 아니면 둘러보기. 0 = 정면.</summary>
    private static int GreetCell() => _frame <= 0 ? 0 : Meowing ? Math.Min(Frames - 1, MascotGreet.LookN + _frame) : Math.Min(Frames - 1, _frame);

    private static void Recalc()
    {
        if (_hwnd == 0) return;
        bool minimized = Native.IsIconic(_hwnd), visible = Native.IsWindowVisible(_hwnd);
        // 칸을 막 눌렀으면(0.5초 안) 활성화가 아직 위젯·입력 창으로 오지 않았어도 쓰는 중으로 본다 — 누름이 무시되지 않게
        bool working = Working() || (Environment.TickCount64 - _clickAt < 500 && visible && !minimized);
        if (!working) _engaged = false;   // 다른 창으로 갔다 오면 다시 좁게 시작
        double target = working && _engaged ? 1 : 0;
        if (target != _eTo)
        {
            _eFrom = _e; _eTo = target; _animStart = Environment.TickCount64;
            Native.SetTimer(_hwnd, TimerAnim, 15, 0);
            _frame = 0;   // 둘러보기 ↔ 야옹: 처음부터
            if (target <= 0) _meowDone = false;
        }
        bool greet = visible && !minimized && (Meowing ? !_meowDone : _eTo > 0 || (_hover && !_hoverDone));
        if (visible && !minimized) Native.SetTimer(_hwnd, TimerBackdrop, BackdropMs, 0); else Native.KillTimer(_hwnd, TimerBackdrop);
        if (greet) Native.SetTimer(_hwnd, TimerGreet, GreetMs, 0);
        else { Native.KillTimer(_hwnd, TimerGreet); if (_frame != 0) { _frame = 0; Render(); } }
    }

    private static void TickAnim()
    {
        double t = Math.Min(1.0, (Environment.TickCount64 - _animStart) / (double)ExpandMs);
        double k = 1 - Math.Pow(1 - t, 3);   // 감속(시안의 cubic-bezier(.2,.8,.2,1) 근사)
        _e = _eFrom + (_eTo - _eFrom) * k;
        if (t >= 1) { _e = _eTo; Native.KillTimer(_hwnd, TimerAnim); }
        Render();
        PlaceHosts();
    }

    private static string KbText(out bool warnIme, out bool warnCaps)
    {
        nint f = _edit[0];
        nint focus = Native.GetFocus();
        if (focus == _edit[1] && _edit[1] != 0) f = _edit[1];
        bool native = false;
        nint h = Native.ImmGetContext(f);
        if (h != 0)
        {
            try { native = Native.ImmGetOpenStatus(h) && Native.ImmGetConversionStatus(h, out uint conv, out _) && (conv & 1) != 0; }
            finally { Native.ImmReleaseContext(f, h); }
        }
        bool caps = (Native.GetKeyState(0x14) & 1) != 0;
        warnIme = native; warnCaps = caps;
        return (native ? T.LockKbdNative : T.LockKbdEnglish) + "\n" + (caps ? T.LockCapsOn : T.LockCapsOff);
    }

    // ================================================================== 그리기

    private static uint A(uint rgb, double a) => ((uint)Math.Round(a * 255) << 24) | (rgb & 0xFFFFFF);
    /// <summary>Theme 토큰(COLORREF, 0x00BBGGRR) → GDI+ 의 0xRRGGBB.</summary>
    private static uint Hex(uint colorref) => ((colorref & 0xFF) << 16) | (colorref & 0xFF00) | ((colorref >> 16) & 0xFF);
    private static uint Rgb(uint bgr) => ((bgr & 0xFF) << 16) | (bgr & 0xFF00) | ((bgr >> 16) & 0xFF);   // Theme 의 COLORREF(BGR) → ARGB 용 RGB

    private static void Render()
    {
        if (_canvas == 0 || _hwnd == 0) return;
        nint g;
        _renders++;
        if (Program.IsTestMode && TestFailAt > 0 && _renders == TestFailAt) { TestFailAt = 0; Fail(); return; }   // 주입한 실패는 한 번만(같은 실행의 다음 위젯은 정상)
        if (GdipGetImageGraphicsContext(_canvas, out g) != 0) { Fail(); return; }
        try
        {
            GdipSetSmoothingMode(g, 4); GdipSetPixelOffsetMode(g, 4); GdipSetInterpolationMode(g, 7); GdipSetTextRenderingHint(g, 4);
            GdipGraphicsClear(g, 0);
            var geo = GetGeo();
            double e = _e;
            bool d = _dark;
            // 색은 본창과 같은 Theme 토큰(2026-10-04 사용자: 잠금 위젯과 본창의 색을 통일). 말풍선·−×·안내 = 카드, 칸 = 입력칸, 글자 = 본문/보조 글자
            uint ink = Hex(Theme.ControlText), sub = Hex(Theme.SecondaryText);
            uint chipBg = Hex(Theme.CardBg), ring = Hex(Theme.CardBorder), focus = Hex(Theme.AccentInk);
            uint pillBg = Hex(Theme.EditBg), pillRing = Hex(Theme.FieldBorder);

            // − (위젯) ×  (머리 위 가운데, 22px)
            DrawCircleBtn(g, CtlCx(1), CtlY + CtlD / 2, 1, d, chipBg, ring, ink);
            if (WidgetBtn) DrawCircleBtn(g, CtlCx(6), CtlY + CtlD / 2, 6, d, chipBg, ring, ink);
            DrawCircleBtn(g, CtlCx(2), CtlY + CtlD / 2, 2, d, chipBg, ring, ink);

            // 말풍선(넓음에서만)
            if (e > 0.01)
            {
                string t1 = _create ? T.LockWidgetHelloNew : T.LockWidgetHello, t2 = _create ? T.LockWidgetSubNew : T.LockLockedSub;
                float w1 = Measure(g, t1, _fontBold), w2 = Measure(g, t2, _fontSmall);
                // 말풍선도 입력칸·버튼과 같은 알약 모양(양 끝 반지름 = 높이의 절반, 2026-10-03 사용자 결정). 둥근 끝 안쪽까지 글이 닿지 않게 좌우 여백을 넉넉히.
                float bw = Math.Max(w1, w2) + 48, bh = (float)(BubH * Math.Min(1, e * 1.2));
                float bx = (W - bw) / 2, by = geo.BubTop;
                double fade = Math.Clamp((e - 0.35) / 0.65, 0, 1);
                FillRound(g, bx, by, bw, BubH, BubH / 2, A(chipBg, fade));
                StrokeRound(g, bx, by, bw, BubH, BubH / 2, A(ring, fade), 1);
                DrawText(g, t1, _fontBold, bx, by + 8, bw, 18, A(ink, fade), _fmtCenter);
                DrawText(g, t2, _fontSmall, bx, by + 27, bw, 16, A(sub, fade), _fmtCenter);
                _ = bh;
            }

            // 알약(들)
            for (int i = 0; i < (_create ? 2 : 1); i++)
            {
                float top = i == 0 ? geo.PillTop : geo.Pill2Top;
                FillRound(g, geo.PillL, top, geo.PillW, PillH, PillH / 2, A(pillBg, PillAlpha));   // 입력 창과 같은 바탕·같은 불투명도(좁을 때 20%)
                StrokeRound(g, geo.PillL, top, geo.PillW, PillH, PillH / 2, e > 0.5 ? A(focus, 1.0) : A(pillRing, 1.0), e > 0.5 ? 1.5f : 1f);
                // 좁을 때는 입력 창 대신 자리표시 글을 그린다(칸은 넓을 때만 보임)
                if (e < 0.05 && GetWindowTextLengthW(_edit[i]) == 0)
                {
                    // 바탕이 20% 라 위젯 뒤의 창이 비친다 — 안내 글은 흰색(2026-10-05 사용자: "그냥 글자를 흰색으로"; 0.3.53 의 테두리 글자는 버림).
                    // 뒤가 밝으면(흰 창·밝은 바탕 화면) 흰 글자가 안 보이므로 진한 글자로(2026-10-06 사용자) — 뒤 밝기는 CheckBackdrop
                    if (i == 0 && _cueCheckedAt == 0) CheckBackdrop();
                    string cue = i == 0 ? (_create ? T.LockCueNew : T.LockLabelMaster) : T.LockLabelAgain;
                    uint cueInk = PillAlpha >= 0.999 ? A(Hex(Theme.SecondaryText), 1.0) : _backdropLight ? 0xE6202020u : 0xFFFFFFFFu;
                    DrawText(g, cue, _fontBody, geo.PillL + 14, top + 13, _placeholderW + 4, 20, cueInk, _fmtLeft);
                }
            }
            // 반투명일 때는 입력 창 자리를 비운다: 입력 창(같은 불투명도)이 바로 바탕 위에 놓여, 알약 양 끝과 같은 진하기로 보인다
            if (PillAlpha < 0.999)
                for (int i = 0; i < 2; i++)
                {
                    if (_host[i] == 0 || HostHidden(i)) continue;   // 숨긴 입력 창 자리는 비우지 않는다: 알약 바탕·자리표시 글을 위젯이 그린다
                    var (hx, hy, hw, hh) = HostRect(geo, i);
                    GdipSetClipRectI(g, hx, hy, hw, hh, 0 /* CombineModeReplace */);
                    GdipGraphicsClear(g, 0);
                    GdipResetClip(g);
                }
            // 한/영·Caps (넓음에서만, 첫 칸 오른쪽)
            if (e > 0.3)
            {
                string kb = KbText(out bool wIme, out bool wCaps);
                _kbShown = kb;
                if (Program.IsTestMode) SetPropW(_hwnd, "OneKeyTestKb", (nint)(4 | (wIme ? 1 : 0) | (wCaps ? 2 : 0)));   // 검증 전용: 그린 한/영·Caps 표시를 시험이 읽는다(inputext.ps1)
                string[] lines = kb.Split('\n');
                double fade = Math.Clamp((e - 0.3) / 0.7, 0, 1);
                uint warn = Hex(Theme.DangerText);
                float kr = _create ? geo.PillL + geo.PillW - 14 : geo.KbRight;
                DrawText(g, lines[0], _fontTiny, kr - 70, geo.PillTop + 8, 70, 14, A(wIme ? warn : sub, 0.85 * fade), _fmtRight);
                DrawText(g, lines[1], _fontTiny, kr - 70, geo.PillTop + 22, 70, 14, A(wCaps ? warn : sub, 0.85 * fade), _fmtRight);
            }
            // 화살표(늘 있음)
            {
                // 본창 주 버튼([저장] 등)과 같은 색(2026-10-04 사용자 결정 — 10-03 의 "어두움 강조 파랑 원"을 대신함): 어두움 흰 원 + 남색 화살표, 밝음 남색 원 + 흰 화살표
                // 어두움: 본창 [입력]·[+ 추가] 같은 옅은 강조 버튼(Tinted — 흰 원은 어두운 화면에서 칸보다 눈에 띄었다, 2026-10-04 사용자). 밝음: 주 버튼(남색 원 + 흰 화살표)
                uint fill = Hex(Theme.IsDark ? Theme.TintFill : Theme.Accent), arrow = Hex(Theme.IsDark ? Theme.AccentLabel : Theme.AccentText);
                FillEllipse(g, geo.ArrowCx - geo.ArrowD / 2, geo.ArrowCy - geo.ArrowD / 2, geo.ArrowD, geo.ArrowD, A(fill, _pressed && _pressedPart == 3 ? 0.8 : 1.0));
                float a = 6.5f;
                DrawLine(g, geo.ArrowCx - a, geo.ArrowCy, geo.ArrowCx + a, geo.ArrowCy, A(arrow, 1), 2.2f);
                DrawLine(g, geo.ArrowCx + a - 5, geo.ArrowCy - 5, geo.ArrowCx + a, geo.ArrowCy, A(arrow, 1), 2.2f);
                DrawLine(g, geo.ArrowCx + a - 5, geo.ArrowCy + 5, geo.ArrowCx + a, geo.ArrowCy, A(arrow, 1), 2.2f);
            }
            // 안내(넓음에서만)
            if (e > 0.3)
            {
                double fade = Math.Clamp((e - 0.3) / 0.7, 0, 1);
                string hint = "v" + App.Version;   // ×·− 설명 줄은 뺐다(2026-10-04 사용자: 불필요). 버전만 남긴다
                float hw = Measure(g, hint, _fontTiny) + 24;
                FillRound(g, (W - hw) / 2, geo.HintTop, hw, HintH, HintH / 2, A(chipBg, fade));
                StrokeRound(g, (W - hw) / 2, geo.HintTop, hw, HintH, HintH / 2, A(ring, fade), 1);
                DrawText(g, hint, _fontTiny, (W - hw) / 2, geo.HintTop + 4, hw, 16, A(sub, fade), _fmtCenter);
            }
            // 마스코트(맨 마지막 = 늘 맨 앞)
            {
                float mw = MasW;
                int dx = S(W / 2 - mw / 2), dy = S(MasTop), dw = S(mw), dh = S(MasH);
                GdipDrawImageRectRectI(g, _sprite, dx, dy, dw, dh, GreetCell() * _spriteW, 0, _spriteW, _spriteH, 2, 0, 0, 0);
            }
        }
        finally { GdipDeleteGraphics(g); }
        Push();
    }

    /// <summary>그리기 실패: 처음 보이기 전이면 표시만 하고(Show 가 false), 보이는 중이면 위젯을 닫고 부르는 쪽에 알린다(B-W04).</summary>
    private static void Fail()
    {
        if (_failed) return;
        _failed = true;
        if (_live && _hwnd != 0) Native.PostMessageW(_hwnd, WM_FAILED, 0, 0);   // 그리는 도중이므로 창은 메시지에서 닫는다
    }

    /// <summary>다른 배율의 모니터로 옮겨졌을 때: 글꼴·캔버스·배치를 새 배율로 다시 만든다(입력 중인 글과 포커스는 그대로).</summary>
    private static void Rescale(uint dpi, int x, int y)
    {
        _dpi = dpi; _s = dpi / 96f;
        FreeResources();
        if (!LoadResources() || !LayoutAndCanvas()) { Fail(); return; }
        for (int i = 0; i < 2; i++) if (_edit[i] != 0) Native.SendMessageW(_edit[i], Native.WM_SETFONT, _editFont, 1);
        Native.SetWindowPos(_hwnd, 0, x, y, _cw, _ch, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        Render();
        PlaceHosts();
    }

    private static bool LayoutAndCanvas() { Layout(); return CreateCanvas(); }

    private static void DrawCircleBtn(nint g, float cx, float cy, int kind, bool dark, uint bg, uint ring, uint ink)
    {
        float r = CtlD / 2;
        bool x = kind == 2;
        bool pressed = _pressed && _pressedPart == kind;
        // 본창 위쪽 둥근 버튼(−·잠금·종료, Btn.IconBordered)과 같은 모양: 테두리 없이 옅은 채움(2026-10-04 사용자 화면 비교)
        uint fill = Theme.Mix(Theme.WindowBg, Theme.ControlText, Theme.IsDark ? 0.12 : 0.07);
        if (pressed) fill = Theme.Mix(fill, Theme.ControlText, Theme.PressMix);
        FillEllipse(g, cx - r, cy - r, CtlD, CtlD, A(Hex(fill), 1.0));
        _ = bg; _ = ring;
        float a = 3.2f;
        if (kind == 6)
        {
            // 본창 목록 위의 위젯 모드 단추와 같은 아이콘(Segoe Fluent Icons EaseOfAccess, 2026-10-07 사용자: 마스코트 윤곽은 커 보임)
            // 0.3.125: 글자로 그리면 글꼴의 위 여백 때문에 위로 치우쳤다(2026-10-07 사용자) → 글자 모양(경로)의 실제 경계를 재서 원 한가운데에,
            // 높이는 × 표시(끝에서 끝 6.4px + 선 1.4px)와 같게. 아이콘 글꼴이 없으면 작은 마스코트 윤곽
            if (_famIcon == 0 || !DrawGlyphCentered(g, "\uE776", cx, cy, 2 * a + 1.4f, A(ink, 0.95)))
                Gdiplus.DrawMascotOutline(g, (cx - 4) * _s, (cy - 4) * _s, 8 * _s, A(ink, 0.95), 1.1f * _s);
        }
        else if (x) { DrawLine(g, cx - a, cy - a, cx + a, cy + a, A(ink, 0.95), 1.4f); DrawLine(g, cx + a, cy - a, cx - a, cy + a, A(ink, 0.95), 1.4f); }
        else DrawLine(g, cx - a, cy, cx + a, cy, A(ink, 0.95), 1.4f);
    }

    // 위젯 뒤(첫 칸의 안내 글 자리)가 밝은가. 화면 DC 를 CAPTUREBLT 없이 복사하면 층 창(이 위젯·입력 창)은 빠지므로 바로 뒤의 화면이 나온다.
    // 1초마다(보이는 동안·좁을 때만) 본다. 경계에서 깜빡이지 않게 밝음 150 이상 → 밝음, 110 이하 → 어두움, 그 사이는 그대로.
    private const int BackdropMs = 1000;
    private static bool _backdropLight;
    private static long _cueCheckedAt;

    /// <summary>뒤 밝기를 다시 본다. 판단이 바뀌었으면 true(다시 그려야 함).</summary>
    private static bool CheckBackdrop()
    {
        _cueCheckedAt = Environment.TickCount64;
        if (_hwnd == 0 || !Native.GetWindowRect(_hwnd, out Native.RECT wr)) return false;
        var geo = GetGeo();
        int x = wr.left + S(geo.PillL + 10), y = wr.top + S(geo.PillTop + 8), w = Math.Max(1, S(_placeholderW + 12)), h = Math.Max(1, S(PillH - 16));
        nint screen = Native.GetDC(0), mem = 0, dib = 0, old = 0;
        try
        {
            if (screen == 0) return false;
            mem = Native.CreateCompatibleDC(screen);
            var bih = new BIH { biSize = (uint)sizeof(BIH), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            dib = CreateDIBSection(mem, ref bih, 0, out nint bits, 0, 0);
            if (mem == 0 || dib == 0 || bits == 0) return false;
            old = Native.SelectObject(mem, dib);
            if (!Native.BitBlt(mem, 0, 0, w, h, screen, x, y, 0x00CC0020 /* SRCCOPY, CAPTUREBLT 없음 */)) return false;
            long sum = 0;
            uint* p = (uint*)bits;
            for (int i = 0; i < w * h; i++) { uint c = p[i]; sum += (((c >> 16) & 255) * 299 + ((c >> 8) & 255) * 587 + (c & 255) * 114) / 1000; }
            double lum = sum / (double)(w * h);
            bool light = lum >= 150 ? true : lum <= 110 ? false : _backdropLight;
            if (Program.IsTestMode && _hwnd != 0) Native.SetPropW(_hwnd, "OneKeyTestBackdrop", (nint)(int)Math.Round(lum) + (light ? 1000 : 0));
            if (light == _backdropLight) return false;
            _backdropLight = light;
            return true;
        }
        catch { return false; }
        finally
        {
            if (old != 0) Native.SelectObject(mem, old);
            if (dib != 0) Native.DeleteObject(dib);
            if (mem != 0) Native.DeleteDC(mem);
            if (screen != 0) Native.ReleaseDC(0, screen);
        }
    }

    private static void Push()
    {
        var size = new SIZE { cx = _cw, cy = _ch };
        var src = new Native.POINT();
        Native.GetWindowRect(_hwnd, out Native.RECT wr);
        var dst = new Native.POINT { x = wr.left, y = wr.top };
        var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 /* AC_SRC_ALPHA */ };
        nint screen = Native.GetDC(0);
        bool ok = UpdateLayeredWindow(_hwnd, screen, ref dst, ref size, _mem, ref src, 0, ref blend, 2 /* ULW_ALPHA */);
        Native.ReleaseDC(0, screen);
        if (!ok) Fail();
    }

    // ---- GDI+ 도형(논리 좌표 → 픽셀)
    private static nint RoundPath(float x, float y, float w, float h, float r)
    {
        x *= _s; y *= _s; w *= _s; h *= _s; r = Math.Min(r * _s, Math.Min(w, h) / 2);
        GdipCreatePath(0, out nint p);
        float d = r * 2;
        GdipAddPathArc(p, x, y, d, d, 180, 90);
        GdipAddPathArc(p, x + w - d, y, d, d, 270, 90);
        GdipAddPathArc(p, x + w - d, y + h - d, d, d, 0, 90);
        GdipAddPathArc(p, x, y + h - d, d, d, 90, 90);
        GdipClosePathFigure(p);
        return p;
    }
    private static void FillRound(nint g, float x, float y, float w, float h, float r, uint argb)
    {
        if ((argb >> 24) == 0) return;
        nint p = RoundPath(x, y, w, h, r); GdipCreateSolidFill(argb, out nint b); GdipFillPath(g, b, p); GdipDeleteBrush(b); GdipDeletePath(p);
    }
    private static void StrokeRound(nint g, float x, float y, float w, float h, float r, uint argb, float width)
    {
        if ((argb >> 24) == 0) return;
        float inset = width / 2 / _s;
        nint p = RoundPath(x + inset, y + inset, w - 2 * inset, h - 2 * inset, r - inset); GdipCreatePen1(argb, width * _s, 2, out nint pen); GdipDrawPath(g, pen, p); GdipDeletePen(pen); GdipDeletePath(p);
    }
    private static void FillEllipse(nint g, float x, float y, float w, float h, uint argb)
    {
        GdipCreateSolidFill(argb, out nint b); GdipFillEllipse(g, b, x * _s, y * _s, w * _s, h * _s); GdipDeleteBrush(b);
    }
    private static void StrokeEllipse(nint g, float x, float y, float w, float h, uint argb, float width)
    {
        GdipCreatePen1(argb, width * _s, 2, out nint pen); GdipDrawEllipse(g, pen, x * _s + 0.5f, y * _s + 0.5f, w * _s - 1, h * _s - 1); GdipDeletePen(pen);
    }
    private static void DrawLine(nint g, float x1, float y1, float x2, float y2, uint argb, float width)
    {
        GdipCreatePen1(argb, width * _s, 2, out nint pen); GdipSetPenStartCap(pen, 2); GdipSetPenEndCap(pen, 2);
        GdipDrawLine(g, pen, x1 * _s, y1 * _s, x2 * _s, y2 * _s); GdipDeletePen(pen);
    }
    /// <summary>
    /// 아이콘 글꼴(_famIcon)의 글자 하나를 모양 그대로(경로) 그린다: 실제 경계 상자의 가운데를 (cx, cy)에, 경계 높이를 height(논리 px)로.
    /// 글줄 높이·위 여백과 상관없이 가운데에 온다. 실패하면 false.
    /// </summary>
    private static bool DrawGlyphCentered(nint g, string glyph, float cx, float cy, float height, uint argb)
    {
        nint path = 0, brush = 0;
        try
        {
            if (GdipCreatePath(0, out path) != 0) return false;
            var layout = new RECTF { X = 0, Y = 0, Width = 1000, Height = 1000 };
            fixed (char* p = glyph) if (GdipAddPathString(path, p, glyph.Length, _famIcon, 0, 100f, ref layout, 0) != 0) return false;
            if (GdipGetPathWorldBounds(path, out RECTF b, 0, 0) != 0 || b.Height <= 0) return false;
            float k = height * _s / b.Height;
            GdipResetWorldTransform(g);
            GdipTranslateWorldTransform(g, cx * _s, cy * _s, 0);
            GdipScaleWorldTransform(g, k, k, 0);
            GdipTranslateWorldTransform(g, -(b.X + b.Width / 2), -(b.Y + b.Height / 2), 0);
            if (GdipCreateSolidFill(argb, out brush) != 0) return false;
            return GdipFillPath(g, brush, path) == 0;
        }
        finally
        {
            GdipResetWorldTransform(g);
            if (brush != 0) GdipDeleteBrush(brush);
            if (path != 0) GdipDeletePath(path);
        }
    }

    private static void DrawText(nint g, string text, nint font, float x, float y, float w, float h, uint argb, nint fmt)
    {
        if ((argb >> 24) == 0) return;
        var rc = new RECTF { X = x * _s, Y = y * _s, Width = w * _s, Height = h * _s };
        GdipCreateSolidFill(argb, out nint b);
        fixed (char* p = text) GdipDrawString(g, p, text.Length, font, ref rc, fmt, b);
        GdipDeleteBrush(b);
    }
    private static float Measure(nint g, string text, nint font)
    {
        var rc = new RECTF { Width = 10000, Height = 1000 };
        RECTF bound;
        fixed (char* p = text) GdipMeasureString(g, p, text.Length, font, ref rc, _fmtLeft, out bound, out _, out _);
        return bound.Width / _s;
    }

    // ================================================================== 자원

    private static bool LoadResources()
    {
        // 마스코트 인사 프레임(가로 묶음, 280px 높이 — 0.3.121: 16 fps 약 96장, 색 JPEG + 투명도 회색 PNG 를 합친 32비트, MascotGreet)
        {
            _sprite = MascotGreet.Load(out int n, !_dark);   // 고양이 색은 위젯(앱) 테마를 따른다
            if (_sprite == 0) return false;
            Frames = n;
            GdipGetImageWidth(_sprite, out uint w); GdipGetImageHeight(_sprite, out uint h);
            _spriteW = (int)(w / Frames); _spriteH = (int)h;
        }
        string face = L.Current switch { Lang.Ja => "Yu Gothic UI", Lang.ZhHans => "Microsoft YaHei UI", Lang.En or Lang.Vi => "Segoe UI", _ => "Malgun Gothic" };
        fixed (char* f = face) if (GdipCreateFontFamilyFromName(f, 0, out _famRegular) != 0) return false;
        GdipCreateFont(_famRegular, 13 * _s, 0, 2, out _fontBody);
        GdipCreateFont(_famRegular, 12 * _s, 0, 2, out _fontSmall);
        GdipCreateFont(_famRegular, 14 * _s, 1, 2, out _fontBold);
        GdipCreateFont(_famRegular, 11 * _s, 0, 2, out _fontTiny);
        foreach (string icon in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })   // Windows 10 에는 MDL2 에 같은 글자가 있다
        {
            fixed (char* f = icon) if (GdipCreateFontFamilyFromName(f, 0, out _famIcon) == 0 && _famIcon != 0) break;
            _famIcon = 0;
        }
        if (_famIcon != 0) GdipCreateFont(_famIcon, 12 * _s, 0, 2, out _fontIcon);
        GdipCreateStringFormat(0x00001000 /* NoWrap */, 0, out _fmtLeft);
        GdipCreateStringFormat(0x00001000, 0, out _fmtCenter); GdipSetStringFormatAlign(_fmtCenter, 1);
        GdipCreateStringFormat(0x00001000, 0, out _fmtRight); GdipSetStringFormatAlign(_fmtRight, 2);
        if (_fontBody == 0 || _fontBold == 0 || _fmtLeft == 0) return false;
        // 좁은 알약 폭 = 자리표시 글 폭(실제 글꼴로 잼, 언어마다 다름)
        nint dc = Native.GetDC(0);
        GdipCreateFromHDC(dc, out nint mg);
        string ph = _create ? T.LockCueNew : T.LockLabelMaster;
        var rc = new RECTF { Width = 10000, Height = 1000 };
        RECTF bound, b2 = default;
        fixed (char* p = ph) GdipMeasureString(mg, p, ph.Length, _fontBody, ref rc, _fmtLeft, out bound, out _, out _);
        if (_create) { string ph2 = T.LockLabelAgain; fixed (char* p = ph2) GdipMeasureString(mg, p, ph2.Length, _fontBody, ref rc, _fmtLeft, out b2, out _, out _); }
        _placeholderW = (float)Math.Ceiling(Math.Max(bound.Width, b2.Width) / _s);
        GdipDeleteGraphics(mg); Native.ReleaseDC(0, dc);
        _hostBrush = Native.CreateSolidBrush(Theme.EditBg);   // 그린 칸 바탕과 같은 본창 입력칸 색
        _editFont = Native.MakeFont(face, (int)Math.Round(13 * _s), Native.FW_NORMAL);
        return _editFont != 0;
    }

    private static bool CreateCanvas()
    {
        var bih = new BIH { biSize = (uint)sizeof(BIH), biWidth = _cw, biHeight = -_ch, biPlanes = 1, biBitCount = 32 };
        nint screen = Native.GetDC(0);
        _mem = Fx.CreateCompatibleDC(screen);
        Native.ReleaseDC(0, screen);
        _dib = CreateDIBSection(_mem, ref bih, 0, out _bits, 0, 0);
        if (_mem == 0 || _dib == 0 || _bits == 0) return false;
        _oldBmp = Native.SelectObject(_mem, _dib);
        return GdipCreateBitmapFromScan0(_cw, _ch, _cw * 4, 0x000E200B /* PixelFormat32bppPARGB */, _bits, out _canvas) == 0 && _canvas != 0;
    }

    private static void FreeResources()
    {
        if (_canvas != 0) { GdipDisposeImage(_canvas); _canvas = 0; }
        if (_mem != 0) { if (_oldBmp != 0) Native.SelectObject(_mem, _oldBmp); Fx.DeleteDC(_mem); _mem = 0; _oldBmp = 0; }
        if (_dib != 0) { Native.DeleteObject(_dib); _dib = 0; _bits = 0; }
        if (_sprite != 0) { GdipDisposeImage(_sprite); _sprite = 0; }
        foreach (nint f in new[] { _fontBody, _fontSmall, _fontBold, _fontTiny, _fontIcon }) if (f != 0) GdipDeleteFont(f);
        _fontBody = _fontSmall = _fontBold = _fontTiny = _fontIcon = 0;
        if (_famIcon != 0) { GdipDeleteFontFamily(_famIcon); _famIcon = 0; }
        foreach (nint f in new[] { _fmtLeft, _fmtCenter, _fmtRight }) if (f != 0) GdipDeleteStringFormat(f);
        _fmtLeft = _fmtCenter = _fmtRight = 0;
        if (_famRegular != 0) { GdipDeleteFontFamily(_famRegular); _famRegular = 0; }
        if (_hostBrush != 0) { Native.DeleteObject(_hostBrush); _hostBrush = 0; }
        if (_editFont != 0) { Native.DeleteObject(_editFont); _editFont = 0; }
    }

    // ================================================================== 창 처리

    private static int PartAt(int px, int py)
    {
        float x = px / _s, y = py / _s;
        var g = GetGeo();
        float cy = CtlY + CtlD / 2;
        if (Dist(x, y, CtlCx(1), cy) <= CtlD / 2 + 1) return 1;
        if (Dist(x, y, CtlCx(2), cy) <= CtlD / 2 + 1) return 2;
        if (WidgetBtn && Dist(x, y, CtlCx(6), cy) <= CtlD / 2 + 1) return 6;
        if (Dist(x, y, g.ArrowCx, g.ArrowCy) <= g.ArrowD / 2 + 1) return 3;
        for (int i = 0; i < (_create ? 2 : 1); i++)
        {
            float top = i == 0 ? g.PillTop : g.Pill2Top;
            if (x >= g.PillL && x <= g.PillL + g.PillW && y >= top && y <= top + PillH) return 10 + i;
        }
        float mw = MasW;
        if (x >= W / 2 - mw / 2 && x <= W / 2 + mw / 2 && y >= MasTop && y <= MasTop + MasH) return 5;
        return 0;
    }
    private static float Dist(float x1, float y1, float x2, float y2) => MathF.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2));

    private static void Notify(int code)
    {
        if (_notify != 0) { Native.PostMessageW(_notify, WM_LOCKWIDGET, code, _instance); return; }
        // 데모: 스스로 처리(값은 읽지 않고 길이만)
        if (code == 1) Toast.Show(_hwnd, "데모: 입력 길이 " + GetWindowTextLengthW(_edit[0]) + (_create ? " / " + GetWindowTextLengthW(_edit[1]) : ""), 1800);
        if (code == 2) { Hide(); Native.PostQuitMessage(0); }
        if (code == 4) Native.PostQuitMessage(4);   // 데모: 그리기 실패 → 종료 코드 4(실제 잠금에서는 기존 잠금 화면으로)
    }

    /// <summary>버튼 명령(마우스·접근성 공통): 1 = 최소화, 2 = × (종료 묻기), 3 = 화살표(잠금 해제·시작). 확인 창이 떠 있으면 무시.</summary>
    private static void DoPart(int part)
    {
        if (_modal || !_live) return;
        if (part == 1) Native.ShowWindow(_hwnd, 6 /* SW_MINIMIZE */);
        else if (part == 2) Notify(2);
        else if (part == 6 && WidgetBtn) Notify(5);   // 위젯 모드로(본창이 위젯을 닫고 작업 표시줄 마스코트를 띄운다)
        else if (part == 3)
        {
            if (!Working()) Native.SetForegroundWindow(_hwnd);   // 활성화 없이 눌렀으면 이제 앞으로(사용자가 누른 직후라 허용됨). 칸에는 WM_ACTIVATE 가 커서를 둔다
            Notify(1);
        }
    }

    private static void SetHover(bool on)
    {
        if (_hover == on) return;
        _hover = on;
        if (!on) _hoverDone = false;
        Recalc();
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            switch (msg)
            {
                case 0x0084: // WM_NCHITTEST
                {
                    Native.GetWindowRect(hwnd, out Native.RECT wr);
                    int px = (short)(lParam & 0xFFFF) - wr.left, py = (short)((lParam >> 16) & 0xFFFF) - wr.top;
                    int part = PartAt(px, py);
                    return part == 5 ? 2 /* HTCAPTION: 마스코트를 끌면 위젯이 움직임 */ : 1 /* HTCLIENT */;
                }
                case Native.WM_MOUSEMOVE:
                case 0x00A0: // WM_NCMOUSEMOVE
                {
                    if (!_tracking)
                    {
                        var tme = new Native.TRACKMOUSEEVENT { cbSize = (uint)sizeof(Native.TRACKMOUSEEVENT), dwFlags = 0x2 /* TME_LEAVE */ | (msg == 0x00A0 ? 0x10u /* TME_NONCLIENT */ : 0), hwndTrack = hwnd };
                        _tracking = Native.TrackMouseEvent(ref tme);
                    }
                    Native.GetCursorPos(out Native.POINT pt);
                    Native.GetWindowRect(hwnd, out Native.RECT wr);
                    SetHover(PartAt(pt.x - wr.left, pt.y - wr.top) == 5);
                    break;
                }
                case Native.WM_MOUSELEAVE:
                case 0x02A2: // WM_NCMOUSELEAVE
                    _tracking = false; SetHover(false); break;
                case 0x0201: // WM_LBUTTONDOWN
                {
                    int part = PartAt((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF));
                    if (part is 1 or 2 or 3 or 6)
                    {
                        if (_modal) return 0;
                        _pressed = true; _pressedPart = part; _pressX = (short)(lParam & 0xFFFF); _pressY = (short)((lParam >> 16) & 0xFFFF);
                        if (part == 3) Engage();
                        Native.SetCapture(hwnd); Render(); return 0;
                    }
                    if (part is 10 or 11) { Native.SetForegroundWindow(hwnd); Native.SetFocus(_edit[part - 10]); Engage(click: true); return 0; }   // 알약의 여백을 눌러도 그 칸으로
                    break;
                }
                case 0x0202: // WM_LBUTTONUP
                    if (_pressed)
                    {
                        int ux = (short)(lParam & 0xFFFF), uy = (short)((lParam >> 16) & 0xFFFF);
                        int part = PartAt(ux, uy);
                        int was = _pressedPart;
                        _pressed = false; _pressedPart = 0; Native.ReleaseCapture(); Render();
                        // 같은 버튼 위에서 뗐거나, 포인터가 거의 움직이지 않았으면(넓어지는 0.22초 동안 버튼이 포인터 밑에서 움직인 경우) 누른 것으로 친다.
                        // 버튼 밖으로 끌어내 떼면 취소(보통 버튼과 같음). R-W5: 애니메이션 중에도 보이는 자리를 누르면 딱 한 번 제출.
                        bool still = Math.Abs(ux - _pressX) <= S(4) && Math.Abs(uy - _pressY) <= S(4);
                        if (part == was || still) DoPart(was);
                        return 0;
                    }
                    break;
                case Native.WM_CAPTURECHANGED:
                    if (_pressed) { _pressed = false; _pressedPart = 0; Render(); }
                    break;
                case 0x0021: // WM_MOUSEACTIVATE
                {
                    // − × 는 누를 때 활성화하지 않는다: 활성화가 위젯 → 입력 창으로 옮겨 가는 사이에 누름이 끊겨 − 가 듣지 않았다.
                    Native.GetCursorPos(out Native.POINT mp);
                    Native.GetWindowRect(hwnd, out Native.RECT mr);
                    // 화살표도 같다: 누르는 동안 활성화가 옮겨 가 누름이 끊기지 않게 하고, 제출할 때 앞으로 가져온다(DoPart).
                    if (PartAt(mp.x - mr.left, mp.y - mr.top) is 1 or 2 or 3 or 6) return 3; // MA_NOACTIVATE
                    break;
                }
                case Native.WM_ACTIVATE:
                    Native.PostMessageW(hwnd, WM_RECALC, 0, 0);
                    if ((wParam & 0xFFFF) != 0 && ((wParam >> 16) & 0xFFFF) == 0 && !Native.IsIconic(hwnd) && _edit[0] != 0)
                    {
                        // 최소화 중(HIWORD != 0)에는 넘기지 않는다: 숨은 입력 창에 포커스를 주면 위젯이 다시 펼쳐진다.
                        // 위젯이 활성화되면 커서는 늘 비밀번호 칸으로(위젯 자체는 키를 받지 않는다). 기본 처리(DefWindowProc)는 포커스를
                        // 위젯에 두므로 부르지 않고 끝낸다(0.3.0 시제품: 키가 위젯으로 가서 칸에 들어가지 않던 것).
                        nint f = Native.GetFocus();
                        Native.SetFocus(f == _edit[1] && _edit[1] != 0 ? _edit[1] : _edit[0]);
                        return 0;
                    }
                    break;
                case Native.WM_SETFOCUS:
                    if (_edit[0] != 0) { Native.SetFocus(_edit[0]); return 0; }
                    break;
                case WM_RECALC:
                    Recalc(); return 0;
                case Native.WM_TIMER:
                    if (wParam == (nint)TimerAnim) TickAnim();
                    else if (wParam == (nint)TimerGreet)
                    {
                        _frame = (_frame + 1) % (Meowing ? MascotGreet.MeowN : Math.Max(1, MascotGreet.LookN));
                        if (_frame == 0 && Meowing) { _meowDone = true; Recalc(); }                       // 야옹은 한 번만
                        else if (_frame == 0 && _eTo <= 0 && _hover) { _hoverDone = true; Recalc(); }   // 마우스로 시작한 인사는 한 바퀴만
                        Render();
                    }
                    else if (wParam == (nint)TimerBackdrop) { if (_e < 0.05 && CheckBackdrop()) Render(); }
                    return 0;
                case 0x0003: // WM_MOVE: 입력 창도 함께
                    PlaceHosts(); break;
                case 0x0232: // WM_EXITSIZEMOVE: 마스코트를 끌어 옮긴 자리를 기억(데모는 저장하지 않음 — 실제 설정 폴더를 건드리지 않게)
                    if (_notify != 0 && Native.GetWindowRect(hwnd, out Native.RECT mr2)) SaveAnchor((mr2.left + mr2.right) / 2, (mr2.top + mr2.bottom) / 2);
                    break;
                case 0x0005: // WM_SIZE (최소화·복원)
                    if (wParam == 1 /* SIZE_MINIMIZED */)
                    {
                        // 최소화 중에는 넓어지기·인사 타이머를 모두 멈춘다(R-W5). 복원하면 Recalc 가 지금 상태로 다시.
                        Native.KillTimer(hwnd, TimerAnim); Native.KillTimer(hwnd, TimerGreet); Native.KillTimer(hwnd, TimerBackdrop);
                        _e = _eFrom = _eTo = 0; _frame = 0;
                        // 버튼을 누른 채 최소화되면(Win+D 등) 그 누름은 취소: 숨은 위젯에서 손을 떼 제출·종료 묻기가 일어나지 않게(Codex R148-2)
                        if (_pressed) { _pressed = false; _pressedPart = 0; Native.ReleaseCapture(); }
                    }
                    Native.PostMessageW(hwnd, WM_RECALC, 0, 0); break;
                case WM_ACT:
                    DoPart((int)wParam); return 0;
                case 0x003D: // WM_GETOBJECT: 그린 버튼의 접근성(R-W1)
                    if ((int)lParam == -4 /* OBJID_CLIENT */ && _live)
                    {
                        nint r = LockWidgetAcc.Lresult(wParam);
                        if (r > 0) return r;
                    }
                    break;
                case 0x007E: // WM_DISPLAYCHANGE
                    Native.PostMessageW(hwnd, WM_ONSCREEN, 0, 0); break;
                case Native.WM_SETTINGCHANGE:
                    if (wParam == 0x002F /* SPI_SETWORKAREA */) Native.PostMessageW(hwnd, WM_ONSCREEN, 0, 0);
                    break;
                case WM_TEST_FAIL_NOW:
                    if (Program.IsTestMode) { TestFailAt = _renders + 1; Render(); }
                    return 0;
                case WM_ONSCREEN:
                    KeepOnScreen(); PlaceHosts(); return 0;
                case 0x0010: // WM_CLOSE
                    Notify(2); return 0;
                case WM_FAILED:
                    FailedWhileFront = Working();   // 부르는 쪽이 대체 화면을 앞으로 가져올지 정한다
                    Hide(); Notify(4); return 0;
                case Native.WM_DPICHANGED:
                {
                    uint nd = (uint)(wParam & 0xFFFF);
                    var sug = (Native.RECT*)lParam;
                    if (nd != 0 && nd != _dpi) Rescale(nd, sug->left, sug->top);
                    return 0;
                }
                case Native.WM_SYSCOMMAND:
                    if ((wParam & 0xFFF0) == 0xF060 /* SC_CLOSE */) { Notify(2); return 0; }   // Alt+F4 도 × 와 같게
                    break;
                case Native.WM_DESTROY:
                    Native.KillTimer(hwnd, TimerAnim); Native.KillTimer(hwnd, TimerGreet); Native.KillTimer(hwnd, TimerBackdrop);
                    break;
            }
        }
        catch { }
        return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint HostProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            switch (msg)
            {
                case Native.WM_ERASEBKGND:
                {
                    Native.GetClientRect(hwnd, out Native.RECT rc);
                    Native.FillRect(wParam, ref rc, _hostBrush);
                    return 1;
                }
                case Native.WM_CTLCOLOREDIT:
                    Native.SetBkColor(wParam, Theme.EditBg);
                    Native.SetTextColor(wParam, Theme.EditText);
                    return _hostBrush;
                case Native.WM_ACTIVATE:
                    if (_hwnd != 0) Native.PostMessageW(_hwnd, WM_RECALC, 0, 0);
                    break;
                case Native.WM_COMMAND:
                    if (((wParam >> 16) & 0xFFFF) == 0x0100 /* EN_SETFOCUS */) Notify(3);
                    if (((wParam >> 16) & 0xFFFF) == 0x0300 /* EN_CHANGE */ && _e < 0.05 && _hwnd != 0) Render();
                    break;
                case 0x0021: // WM_MOUSEACTIVATE: 입력 창을 눌러 활성화
                    break;
                case 0x0010: // WM_CLOSE: 입력 창에서 Alt+F4 → 입력 창만 닫히지 않게, × 와 같은 처리(종료 묻기)
                    if (!_modal) Notify(2);
                    return 0;
            }
        }
        catch { }
        return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint EditProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        int i = hwnd == _edit[1] && _edit[1] != 0 ? 1 : 0;
        nint old = _oldEditProc[i];
        try
        {
            // 입력 시작 = 넓어짐: 칸을 누르거나, 수정 키(Shift·Ctrl·Alt·Win·CapsLock)가 아닌 키를 치거나, 조합·붙여넣기를 시작했을 때
            if (msg == 0x0201 /* WM_LBUTTONDOWN */ || msg == 0x0102 /* WM_CHAR */ || msg == 0x010D /* WM_IME_STARTCOMPOSITION */ || msg == 0x0302 /* WM_PASTE */
                || (msg == Native.WM_KEYDOWN && wParam is not (0x10 or 0x11 or 0x12 or 0x14 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5)))
                Engage(click: msg == 0x0201);
            if (msg == Native.WM_KEYDOWN)
            {
                if (wParam == 0x0D) { if (!_create || i == 1) Notify(1); else Native.SetFocus(_edit[1]); return 0; }   // Enter: 잠금 해제(처음 설정은 둘째 칸에서)
                if (wParam == 0x09 && _create) { Native.SetFocus(_edit[1 - i]); return 0; }                               // Tab/Shift+Tab: 칸 사이
            }
            if (msg == 0x0102 /* WM_CHAR */ && (wParam == 0x0D || wParam == 0x09)) return 0;   // 삑 소리 없이
        }
        catch { }
        return CallWindowProcW(old, hwnd, msg, wParam, lParam);
    }

    // ================================================================== P/Invoke

    private const uint WS_EX_LAYERED = 0x00080000, WS_SYSMENU = 0x00080000, WS_MINIMIZEBOX = 0x00020000;
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential)] private struct BIH { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biX, biY; public uint biClrUsed, biClrImportant; }
    [StructLayout(LayoutKind.Sequential)] private struct RECTF { public float X, Y, Width, Height; }

    [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(nint hwnd, nint hdcDst, ref Native.POINT pptDst, ref SIZE psize, nint hdcSrc, ref Native.POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);
    [DllImport("user32.dll")] private static extern int GetWindowTextLengthW(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetPropW(nint hwnd, string name, nint data);
    [DllImport("user32.dll")] private static extern nint CallWindowProcW(nint prev, nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint hdc, ref BIH bmi, uint usage, out nint bits, nint section, uint offset);
    [DllImport("shlwapi.dll", EntryPoint = "#12")] private static extern nint SHCreateMemStream(byte* pInit, uint cbInit);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateBitmapFromScan0(int w, int h, int stride, int format, nint scan0, out nint bitmap);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateBitmapFromStream(nint stream, out nint bitmap);
    [DllImport("gdiplus.dll")] private static extern int GdipGetImageGraphicsContext(nint image, out nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateFromHDC(nint hdc, out nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteGraphics(nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipDisposeImage(nint image);
    [DllImport("gdiplus.dll")] private static extern int GdipGetImageWidth(nint image, out uint w);
    [DllImport("gdiplus.dll")] private static extern int GdipGetImageHeight(nint image, out uint h);
    [DllImport("gdiplus.dll")] private static extern int GdipGraphicsClear(nint graphics, uint argb);
    [DllImport("gdiplus.dll")] private static extern int GdipSetClipRectI(nint graphics, int x, int y, int w, int h, int combineMode);
    [DllImport("gdiplus.dll")] private static extern int GdipResetClip(nint graphics);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(nint hwnd, uint crKey, byte alpha, uint flags);
    [DllImport("gdiplus.dll")] private static extern int GdipSetSmoothingMode(nint graphics, int mode);
    [DllImport("gdiplus.dll")] private static extern int GdipSetPixelOffsetMode(nint graphics, int mode);
    [DllImport("gdiplus.dll")] private static extern int GdipSetInterpolationMode(nint graphics, int mode);
    [DllImport("gdiplus.dll")] private static extern int GdipSetTextRenderingHint(nint graphics, int mode);
    [DllImport("gdiplus.dll")] private static extern int GdipDrawImageRectRectI(nint graphics, nint image, int dx, int dy, int dw, int dh, int sx, int sy, int sw, int sh, int unit, nint attr, nint cb, nint cbData);
    [DllImport("gdiplus.dll")] private static extern int GdipCreatePath(int fillMode, out nint path);
    [DllImport("gdiplus.dll")] private static extern int GdipAddPathString(nint path, char* text, int length, nint family, int style, float emSize, ref RECTF layout, nint format);
    [DllImport("gdiplus.dll")] private static extern int GdipGetPathWorldBounds(nint path, out RECTF bounds, nint matrix, nint pen);
    [DllImport("gdiplus.dll")] private static extern int GdipResetWorldTransform(nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipTranslateWorldTransform(nint graphics, float dx, float dy, int order);
    [DllImport("gdiplus.dll")] private static extern int GdipScaleWorldTransform(nint graphics, float sx, float sy, int order);
    [DllImport("gdiplus.dll")] private static extern int GdipDeletePath(nint path);
    [DllImport("gdiplus.dll")] private static extern int GdipAddPathArc(nint path, float x, float y, float w, float h, float start, float sweep);
    [DllImport("gdiplus.dll")] private static extern int GdipClosePathFigure(nint path);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateSolidFill(uint argb, out nint brush);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteBrush(nint brush);
    [DllImport("gdiplus.dll")] private static extern int GdipFillPath(nint graphics, nint brush, nint path);
    [DllImport("gdiplus.dll")] private static extern int GdipFillEllipse(nint graphics, nint brush, float x, float y, float w, float h);
    [DllImport("gdiplus.dll")] private static extern int GdipCreatePen1(uint argb, float width, int unit, out nint pen);
    [DllImport("gdiplus.dll")] private static extern int GdipDeletePen(nint pen);
    [DllImport("gdiplus.dll")] private static extern int GdipSetPenStartCap(nint pen, int cap);
    [DllImport("gdiplus.dll")] private static extern int GdipSetPenEndCap(nint pen, int cap);
    [DllImport("gdiplus.dll")] private static extern int GdipDrawPath(nint graphics, nint pen, nint path);
    [DllImport("gdiplus.dll")] private static extern int GdipDrawEllipse(nint graphics, nint pen, float x, float y, float w, float h);
    [DllImport("gdiplus.dll")] private static extern int GdipDrawLine(nint graphics, nint pen, float x1, float y1, float x2, float y2);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateFontFamilyFromName(char* name, nint collection, out nint family);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteFontFamily(nint family);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateFont(nint family, float emSize, int style, int unit, out nint font);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteFont(nint font);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateStringFormat(int flags, int lang, out nint format);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteStringFormat(nint format);
    [DllImport("gdiplus.dll")] private static extern int GdipSetStringFormatAlign(nint format, int align);
    [DllImport("gdiplus.dll")] private static extern int GdipDrawString(nint graphics, char* text, int len, nint font, ref RECTF layout, nint format, nint brush);
    [DllImport("gdiplus.dll")] private static extern int GdipMeasureString(nint graphics, char* text, int len, nint font, ref RECTF layout, nint format, out RECTF bound, out int cp, out int lines);
}
