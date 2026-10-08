using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>직접 그리는 컨트롤들의 공통 부분: 창 클래스 등록, 상태 비트, 마우스 추적, 더블 버퍼.</summary>
internal static unsafe class Ctl
{
    public const nint StHot = 1, StPressed = 2, StTracking = 4;
    public const int OffState = 0, OffFont = 8;

    /// <summary>포커스 링을 그릴지. Tab/화살표로 이동하면 켜지고, 마우스를 누르면 꺼진다 (Windows 의 기본 동작과 같다).</summary>
    public static bool ShowFocus;
    public static bool HasFocusRing(nint hwnd) => ShowFocus && Native.GetFocus() == hwnd;

    /// <summary>
    /// 마지막으로 마우스 클릭이 동작한 시각. 이 뒤 <see cref="BounceMs"/> 안에 오는 마우스 누름은 무시한다.
    /// 마우스 스위치가 낡으면 한 번 떼는 순간 13~30ms 뒤에 한 번 더 눌림이 들어오는데(실측), 그러면 설정 펼침 같은
    /// 토글이 두 번 실행되어 원래대로 돌아가 버린다. 사람은 80ms 안에 두 번 누를 수 없으므로 정상 입력에는 영향이 없다.
    /// </summary>
    public static long LastClickTick;
    public const int BounceMs = 80;
    public static bool IsBounce() => Environment.TickCount64 - LastClickTick < BounceMs;
    public static void MarkClick() => LastClickTick = Environment.TickCount64;

    public static void RegisterClass(nint hInst, string name, delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint> proc, int extra)
    {
        fixed (char* cls = name)
        {
            var wc = new Native.WNDCLASSEXW
            {
                cbSize = (uint)sizeof(Native.WNDCLASSEXW),
                lpfnWndProc = (nint)proc,
                cbWndExtra = extra,
                hInstance = hInst,
                hCursor = Native.LoadCursorW(0, 32512),
                lpszClassName = cls,
            };
            Native.RegisterClassExW(ref wc);
        }
    }

    public static nint State(nint h) => Native.GetWindowLongPtrW(h, OffState);
    public static bool IsHot(nint h) => (State(h) & StHot) != 0;
    /// <summary>
    /// 한 줄로 보이는 짝(목록 행 ↔ 그 행의 [입력] 버튼). 어느 쪽에 마우스가 있어도 줄 전체를 강조한다(2026-10-02 사용자: 강조가 버튼 칸 앞에서 끊김).
    /// </summary>
    public static readonly Dictionary<nint, nint> Partner = new();
    public static bool BandHot(nint h) => IsHot(h) || (Partner.TryGetValue(h, out nint p) && IsHot(p));
    /// <summary>호버가 바뀐 컨트롤(부모가 짝 사이의 바탕을 다시 그리게 한다).</summary>
    public static Action<nint>? HotChanged;
    private static void OnHotChanged(nint hwnd)
    {
        if (Partner.TryGetValue(hwnd, out nint p)) Native.InvalidateRect(p, 0, false);
        HotChanged?.Invoke(hwnd);
    }
    public static void SetState(nint h, nint v) => Native.SetWindowLongPtrW(h, OffState, v);
    public static uint Style(nint h) => (uint)(long)Native.GetWindowLongPtrW(h, Native.GWL_STYLE);
    public static int S(nint h, int v) { uint dpi = Native.GetDpiForWindow(h); if (dpi == 0) dpi = 96; return (int)((long)v * dpi / 96); }

    /// <summary>호버/눌림/포커스/키 입력처럼 모든 컨트롤이 같은 방식으로 처리하는 메시지. 처리했으면 true.</summary>
    public static bool Common(nint hwnd, uint msg, nint wParam, nint lParam, Action<nint> fire, out nint result)
    {
        result = 0;
        switch (msg)
        {
            case 0x003D:   // WM_GETOBJECT: 역할·상태·값·기본 동작(Codex QA-06)
                if (CtlAcc.Get(hwnd, wParam, lParam) is nint acc) { result = acc; return true; }
                return false;
            case Native.WM_NCDESTROY:
                CtlAcc.Forget(hwnd);
                return false;   // 각 컨트롤의 정리도 이어서
            case Native.WM_SETFONT:
                Native.SetWindowLongPtrW(hwnd, OffFont, wParam);
                if (lParam != 0) Native.InvalidateRect(hwnd, 0, false);
                return true;
            case Native.WM_GETFONT:
                result = Native.GetWindowLongPtrW(hwnd, OffFont); return true;
            case Native.WM_SETTEXT:
            case Native.WM_ENABLE:
            case Native.WM_SETFOCUS:
            case Native.WM_KILLFOCUS:
                result = Native.DefWindowProcW(hwnd, msg, wParam, lParam);
                Native.InvalidateRect(hwnd, 0, false);
                return true;
            case Native.WM_MOUSEMOVE:
            {
                nint st = State(hwnd);
                if ((st & StTracking) == 0)
                {
                    var tme = new Native.TRACKMOUSEEVENT { cbSize = (uint)sizeof(Native.TRACKMOUSEEVENT), dwFlags = Native.TME_LEAVE, hwndTrack = hwnd };
                    Native.TrackMouseEvent(ref tme);
                    SetState(hwnd, st | StTracking | StHot);
                    Native.InvalidateRect(hwnd, 0, false);
                    OnHotChanged(hwnd);
                }
                return true;
            }
            case Native.WM_MOUSELEAVE:
                SetState(hwnd, State(hwnd) & ~(StTracking | StHot));
                Native.InvalidateRect(hwnd, 0, false);
                OnHotChanged(hwnd);
                return true;
            case Native.WM_LBUTTONDOWN:
                if (!Native.IsWindowEnabled(hwnd) || IsBounce()) return true;   // 이중 눌림(스위치 바운스)은 버린다
                if ((Style(hwnd) & Native.WS_TABSTOP) != 0) Native.SetFocus(hwnd);
                Native.SetCapture(hwnd);
                SetState(hwnd, State(hwnd) | StPressed);
                Native.InvalidateRect(hwnd, 0, false);
                return true;
            case Native.WM_LBUTTONUP:
            {
                nint st = State(hwnd);
                if ((st & StPressed) == 0) return true;
                Native.ReleaseCapture();
                SetState(hwnd, st & ~StPressed);
                Native.GetClientRect(hwnd, out Native.RECT rc);
                int x = Native.LoWord(lParam), y = Native.HiWord(lParam);
                if (x >= 0 && y >= 0 && x < rc.right && y < rc.bottom) { MarkClick(); fire(hwnd); }
                else Native.InvalidateRect(hwnd, 0, false);
                return true;
            }
            case Native.WM_CAPTURECHANGED:
                SetState(hwnd, State(hwnd) & ~StPressed);
                Native.InvalidateRect(hwnd, 0, false);
                return true;
            case Native.WM_KEYDOWN:
                if ((State(hwnd) & StPressed) != 0) return true;   // 마우스를 누르고 있는 동안은 키로 또 누르지 않는다
                if ((wParam == Native.VK_SPACE || wParam == Native.VK_RETURN) && Native.IsWindowEnabled(hwnd)) { fire(hwnd); return true; }
                return false;
            case Native.BM_CLICK:
                if (Native.IsWindowEnabled(hwnd)) fire(hwnd);
                return true;
            case Native.WM_ERASEBKGND:
                result = 1; return true;
            case Native.WM_PRINTCLIENT:
                result = PrintClient(hwnd, wParam); return true;
        }
        return false;
    }

    public static void NotifyParent(nint hwnd)
    {
        nint parent = Native.GetParent(hwnd);
        int id = Native.GetDlgCtrlID(hwnd);
        if (parent != 0) Native.SendMessageW(parent, Native.WM_COMMAND, (nint)((Native.BN_CLICKED << 16) | (uint)id), hwnd);
    }

    /// <summary>WM_PRINTCLIENT 로 그릴 때 대상 DC. 0 이면 보통 그리기(BeginPaint).</summary>
    [ThreadStatic] public static nint PrintDc;

    /// <summary>WM_PRINTCLIENT: 같은 그리기 함수로 주어진 DC 에 그린다(캡처 경로, Codex v3 5절).</summary>
    public static nint PrintClient(nint hwnd, nint hdc)
    {
        nint keep = PrintDc;
        PrintDc = hdc;
        try { Native.SendMessageW(hwnd, Native.WM_PAINT, 0, 0); }
        finally { PrintDc = keep; }
        return 0;
    }

    /// <summary>더블 버퍼로 그린다. draw(memDC, w, h) 안에서 그리면 된다.</summary>
    public static void Paint(nint hwnd, nint bgBrush, Action<nint, int, int> draw)
    {
        nint printDc = PrintDc;
        Native.PAINTSTRUCT ps = default;
        nint hdc = printDc != 0 ? printDc : Native.BeginPaint(hwnd, out ps);
        Native.GetClientRect(hwnd, out Native.RECT rc);
        int w = rc.right, h = rc.bottom;
        if (w > 0 && h > 0)
        {
            nint mem = Native.CreateCompatibleDC(hdc);
            nint bmp = Native.CreateCompatibleBitmap(hdc, w, h);
            nint old = Native.SelectObject(mem, bmp);
            if (bgBrush == Theme.BgBrush) Theme.AlignBg(mem, hwnd);   // B 바탕 그림: 본창의 같은 자리를 이어 그린다
            Native.FillRect(mem, ref rc, bgBrush);
            Native.SetBkMode(mem, Native.TRANSPARENT);
            draw(mem, w, h);
            Native.BitBlt(hdc, 0, 0, w, h, mem, 0, 0, Native.SRCCOPY);
            Native.SelectObject(mem, old);
            Native.DeleteObject(bmp);
            Native.DeleteDC(mem);
        }
        if (printDc == 0) Native.EndPaint(hwnd, ref ps);
    }

    /// <summary>한 줄 글자. DirectWrite(Pretendard)로 그리고, 꺼져 있거나 아이콘 글꼴·실패면 GDI 로 그린다.</summary>
    public static void Text(nint hdc, nint font, string s, uint color, int l, int t, int r, int b, uint fmt)
    {
        if (Dw.Text(hdc, font, s, color, l, t, r, b, fmt | Native.DT_NOPREFIX | Native.DT_SINGLELINE)) return;
        nint old = font != 0 ? Native.SelectObject(hdc, font) : 0;
        Native.SetTextColor(hdc, color);
        var rc = new Native.RECT { left = l, top = t, right = r, bottom = b };
        Native.DrawText(hdc, s, ref rc, fmt | Native.DT_NOPREFIX | Native.DT_SINGLELINE);
        if (old != 0) Native.SelectObject(hdc, old);
    }
}

/// <summary>
/// 버튼. 창 스타일 하위 비트로 종류를 고른다.
///   0 Bordered(연한 채움, 강조색 글자) · 1 Prominent(강조색 채움, 흰 글자) · 2 Borderless(글자만)
///   3 Destructive(빨간 글자만) · 4 Icon(아이콘 글꼴 글리프만, 정사각) · 5 IconBordered(연한 채움 위 강조색 글리프) · 6 DangerBordered(Bordered 틀에 빨간 글자)
///   + 0x10 OnCard(카드 배경 위) · + 0x20 Default(Enter 로 눌림)
/// </summary>
internal static unsafe class Btn
{
    public const string ClassName = "OneKeyButton";
    public const uint Bordered = 0, Prominent = 1, Borderless = 2, Destructive = 3, Icon = 4, IconBordered = 5, DangerBordered = 6, KindMask = 0x0F;
    /// <summary>창 글자가 이것이면 아이콘 글꼴 대신 마스코트 윤곽 그림(사용자 영역 문자 — 접근성 이름은 Tip 글).</summary>
    /// <summary>iOS 식 뒤로 버튼(2026-09-29 사용자 요청): 왼쪽 꺾쇠 아이콘 + 강조색 글자, 배경 없음. 창 글자는 돌아갈 화면 이름(화면 읽기 프로그램이 읽는다).</summary>
    public const uint Back = 7;
    /// <summary>연한 강조 채움 + 강조색 글자(목록 행의 [입력]). 진한 Prominent 는 화면의 주 동작에만.</summary>
    public const uint Tinted = 8;
    /// <summary>사이트 채우기의 떠 있는 [채우기](B 시안 .fillbtn): 남색 알약 + 옅은 파랑 테두리 + 열쇠 아이콘 + 흰 굵은 글자. 두 테마 같은 색.</summary>
    public const uint Fill = 9;
    /// <summary>접는 섹션 제목(도움말, D6): 왼쪽 꺾쇠(펼침 ▾ / 접힘 ▸) + 굵은 본문색 글, 배경 없음(올림·누름만). <see cref="Expanded"/> 비트가 펼침.</summary>
    public const uint Section = 10;
    public const uint Expanded = 0x40;
    /// <summary>
    /// 둥근 배지 버튼(바로 실행 띠의 [편집], 2026-10-05 사용자): 평소는 [+ 추가]와 같은 Tinted(옅은 강조 채움 + 강조색 연필),
    /// <see cref="Expanded"/> 비트면 편집 중 — [확인]과 같은 Prominent(강조색 채움 + 흰 체크).
    /// 만든 쪽이 창을 동그라미 영역으로 자른다. 창 글은 화면 읽기 이름.
    /// </summary>
    public const uint Badge = 11;
    /// <summary>띠 끝의 넘기기 띠(‹ ›, 2026-10-05 사용자): 칸에 마우스를 올렸을 때와 같은 옅은 바탕의 좁고 긴 둥근 사각형 + 작은 꺾쇠(테두리 없음).</summary>
    public const uint Nudge = 12;
    private const string FillKey = "\uE8D7";   // Segoe Fluent Icons: Permissions(열쇠)
    private const string BackChevron = "\uE76B";   // Segoe Fluent Icons: ChevronLeft
    public const uint OnCard = 0x10, Default = 0x20;
    /// <summary>
    /// 새 디자인(2026-10-08 금속 질감, <see cref="MetalUi"/>): 13 머리줄의 둥근 단추(지름 30, 글자가 아이콘 글리프면 아이콘) ·
    /// 14 판 위 조각의 [입력](조각 오른쪽 끝을 이어 그리고 그 위에 알약) · 15 [+ 추가] 알약. 창은 그늘이 들어갈 만큼 단추보다 크다.
    /// </summary>
    public const uint Knob = 13, PillInput = 14, PillMain = 15;
    /// <summary>Borderless 와 함께: 글 링크(설정 ›) — 시안의 회색 글 + 꺾쇠.</summary>
    public const uint Link = 0x40;
    public const int Radius = 6;

    public static void Register(nint hInst) => Ctl.RegisterClass(hInst, ClassName, &WndProc, 16);

    /// <summary>배지 버튼의 바탕(부모가 매끈한 가장자리를 같은 색으로 깐다). 평소 = [+ 추가](Tinted), 켜짐 = [확인](Prominent).</summary>
    public static uint BadgeFill(bool on) => on ? Theme.Accent : Theme.TintFill;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            // 대화상자 규칙과 같게: Enter 는 포커스가 있는 버튼을 누르고, 포커스가 버튼에 없으면 기본 버튼을 누른다.
            if (msg == Native.WM_GETDLGCODE)
                return (nint)(Native.DLGC_BUTTON | ((Ctl.Style(hwnd) & Default) != 0 || Native.GetFocus() == hwnd ? Native.DLGC_DEFPUSHBUTTON : Native.DLGC_UNDEFPUSHBUTTON));
            if (Ctl.Common(hwnd, msg, wParam, lParam, Ctl.NotifyParent, out nint r)) return r;
            if (msg == Native.WM_PAINT) { Paint(hwnd); return 0; }
        }
        catch { }
        return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private static void Paint(nint hwnd)
    {
        uint style = Ctl.Style(hwnd);
        uint kind = style & KindMask;
        if (kind is Knob or PillInput or PillMain || (kind == Borderless && (style & Link) != 0) || (kind is Bordered or Prominent or DangerBordered or Icon && MetalUi.On(hwnd))) { MetalUi.PaintButton(hwnd, style, kind); return; }
        bool onCard = (style & OnCard) != 0;
        // 목록 행의 짝 버튼이면 줄이 강조될 때 버튼 둘레도 같은 색으로(줄 강조가 버튼 칸에서 끊기지 않게)
        bool band = onCard && Ctl.Partner.ContainsKey(hwnd) && Ctl.BandHot(hwnd);
        uint bandBg = Theme.RowFill(false);
        nint bandBrush = band ? Native.CreateSolidBrush(bandBg) : 0;
        try {
        Ctl.Paint(hwnd, band ? bandBrush : onCard ? Theme.CardBrush : Theme.BgBrush, (dc, w, h) =>
        {
            nint st = Ctl.State(hwnd);
            bool enabled = Native.IsWindowEnabled(hwnd);
            bool hot = (st & Ctl.StHot) != 0 && enabled, pressed = (st & Ctl.StPressed) != 0 && enabled;
            bool focus = Ctl.HasFocusRing(hwnd);
            uint bg = band ? bandBg : onCard ? Theme.CardBg : Theme.WindowBg;
            int r = h / 2;   // B: 모든 버튼은 알약(양 끝 반지름 = 높이의 절반)
            uint text;
            bool defRing = kind == Prominent && enabled && (style & Default) != 0;

            switch (kind)
            {
                case Prominent:
                {
                    // B 주요 버튼: 어두움 = 흰 바탕 + 남색 글자, 밝음 = 남색 바탕 + 흰 글자. 기본 버튼은 이중 선(바깥 초점색 2px, 안쪽 틈 2px — 시안 .btn.pri.def)
                    uint fill = Theme.Accent;
                    // 누름·올림: 어두운 테마는 파랑을 더 어둡게(흰 글자 대비가 오른다 — 밝게 섞으면 누른 동안 3.97:1, R166-C1 4), 밝은 테마는 그대로
                    uint toward = Theme.IsDark ? 0x000000u : Theme.AccentText;
                    if (pressed) fill = Theme.Mix(fill, toward, 0.16); else if (hot) fill = Theme.Mix(fill, toward, 0.08);
                    int i = 0;
                    if (defRing)
                    {
                        Gdiplus.FillRoundRect(dc, 0, 0, w, h, r, Theme.AccentInk);
                        int g = Ctl.S(hwnd, 2);
                        Gdiplus.FillRoundRect(dc, g, g, w - g * 2, h - g * 2, (h - g * 2) / 2, Theme.RingGap);
                        i = g * 2;
                    }
                    Gdiplus.FillRoundRect(dc, i, i, w - i * 2, h - i * 2, (h - i * 2) / 2, fill, enabled ? (byte)255 : (byte)110);
                    text = Theme.AccentText;
                    break;
                }
                case Bordered:
                case IconBordered:
                case DangerBordered:
                {
                    // B 보조 버튼(.sec): 글자색을 옅게 깐 채움 + 본문 글자. 위험 버튼(.dan): 옅은 빨강 채움 + 빨간 글자. 테두리 없음.
                    uint baseBg = onCard ? Theme.CardBg : Theme.WindowBg;
                    uint fill = kind == DangerBordered ? Theme.DangerFill : onCard ? Theme.SecFill : Theme.TopFill;   // 창 바탕 위(목록 윗줄): [+ 추가]와 같은 채움
                    // 밝은 테마의 위험 버튼은 누름을 약하게(빨간 글자 4.5:1 유지 — 0.16 이면 4.07:1, R166-C1 4)
                    double press = kind == DangerBordered && !Theme.IsDark ? 0.10 : Theme.PressMix;
                    if (pressed) fill = Theme.Mix(fill, Theme.ControlText, press); else if (hot) fill = Theme.Mix(fill, Theme.ControlText, Theme.HoverMix);
                    Gdiplus.FillRoundRect(dc, 0, 0, w, h, r, fill);
                    text = !enabled ? Theme.DisabledText : kind == DangerBordered ? Theme.DangerText : Theme.ControlText;
                    break;
                }
                case Fill:
                {
                    uint fill = Theme.FillPill;
                    if (pressed) fill = Theme.Mix(fill, 0x000000, 0.18); else if (hot) fill = Theme.Mix(fill, 0xFFFFFF, 0.10);
                    Gdiplus.FillRoundRect(dc, 0, 0, w, h, r, fill);
                    Gdiplus.DrawRoundRect(dc, 0, 0, w, h, r, Theme.FillRing, Ctl.S(hwnd, 1));
                    int iw = Ctl.S(hwnd, 16), gap = Ctl.S(hwnd, 5);
                    nint f = Native.GetWindowLongPtrW(hwnd, Ctl.OffFont);
                    string label = Native.GetWindowText(hwnd);
                    nint mdc = Native.GetDC(hwnd); nint o = Native.SelectObject(mdc, f); int tw = Native.TextWidth(mdc, label); Native.SelectObject(mdc, o); Native.ReleaseDC(hwnd, mdc);
                    int x0 = Math.Max(0, (w - iw - gap - tw) / 2);
                    Ctl.Text(dc, Theme.FontIcon, FillKey, 0xFFFFFF, x0, 0, x0 + iw, h, Native.DT_CENTER | Native.DT_VCENTER);
                    Ctl.Text(dc, f, label, 0xFFFFFF, x0 + iw + gap, 0, w, h, Native.DT_LEFT | Native.DT_VCENTER);
                    return;
                }
                case Nudge:
                {
                    // 어두운 테마: 띠의 칸 올림과 같은 색. 밝은 테마: 그 회색이 진해 보여 훨씬 옅게(2026-10-05 사용자)
                    double rest = Theme.IsDark ? Theme.HoverMix + 0.04 : 0.05, hov = Theme.IsDark ? Theme.HoverMix + 0.10 : 0.10, prs = Theme.IsDark ? Theme.PressMix + 0.04 : 0.16;
                    uint fill = Theme.Mix(bg, Theme.ControlText, pressed ? prs : hot ? hov : rest);
                    Gdiplus.FillRoundRect(dc, 0, 0, w, h, Ctl.S(hwnd, 6), fill);
                    if (focus) Gdiplus.DrawRoundRect(dc, 0, 0, w, h, Ctl.S(hwnd, 6), Theme.AccentInk, Ctl.S(hwnd, 1));
                    Ctl.Text(dc, Theme.FontIconSmall, Native.GetWindowText(hwnd), enabled ? Theme.ControlText : Theme.DisabledText, 0, 0, w, h, Native.DT_CENTER | Native.DT_VCENTER);
                    return;
                }
                case Badge:
                {
                    bool on = (style & Expanded) != 0;
                    uint fill = BadgeFill(on);
                    if (on)
                    {
                        // Prominent 와 같은 누름·올림(어두운 테마는 더 어둡게 — 흰 글자 대비)
                        uint toward = Theme.IsDark ? 0x000000u : Theme.AccentText;
                        if (pressed) fill = Theme.Mix(fill, toward, 0.16); else if (hot) fill = Theme.Mix(fill, toward, 0.08);
                    }
                    else
                    {
                        // Tinted 와 같은 누름·올림
                        double tp = Theme.IsDark ? 0.18 : 0.12, th = Theme.IsDark ? 0.09 : 0.06;
                        if (pressed) fill = Theme.Mix(fill, Theme.AccentInk, tp); else if (hot) fill = Theme.Mix(fill, Theme.AccentInk, th);
                    }
                    Gdiplus.FillEllipse(dc, 0, 0, w, h, fill);
                    if (focus) { int g = Ctl.S(hwnd, 2); Gdiplus.DrawRoundRect(dc, g, g, w - 2 * g, h - 2 * g, (h - 2 * g) / 2, on ? Theme.AccentText : Theme.AccentInk, Ctl.S(hwnd, 1)); }
                    Ctl.Text(dc, Theme.FontIconSmall, on ? "\uE73E" : "\uE70F", on ? Theme.AccentText : Theme.AccentLabel, 0, 0, w, h, Native.DT_CENTER | Native.DT_VCENTER);   // CheckMark / Edit — 작은 버튼이라 12px
                    return;
                }
                case Tinted:
                {
                    uint fill = enabled ? (onCard || Theme.IsDark ? Theme.TintFill : Theme.TopFill) : Theme.Mix(bg, Theme.ControlText, 0.05);   // 밝은 테마 창 바탕 위([+ 추가])는 윗줄 아이콘 버튼과 같은 채움
                    // 밝은 테마는 누름을 약하게(강조 글자 4.5:1 유지 — 0.18 이면 4.38:1, R166-C1 4)
                    double tp = Theme.IsDark ? 0.18 : 0.12, th = Theme.IsDark ? 0.09 : 0.06;
                    if (pressed) fill = Theme.Mix(fill, Theme.AccentInk, tp); else if (hot) fill = Theme.Mix(fill, Theme.AccentInk, th);
                    Gdiplus.FillRoundRect(dc, 0, 0, w, h, r, fill);
                    text = enabled ? Theme.AccentLabel : Theme.DisabledText;
                    break;
                }
                default:   // Borderless / Destructive / Icon
                {
                    if (hot || pressed)
                        Gdiplus.FillRoundRect(dc, 0, 0, w, h, r, Theme.Mix(bg, Theme.ControlText, pressed ? Theme.PressMix : Theme.HoverMix));
                    text = !enabled ? Theme.DisabledText
                         : kind == Destructive ? Theme.DangerText
                         : kind == Icon ? Theme.SecondaryText
                         : Theme.AccentLabel;
                    break;
                }
            }

            if (focus && !defRing)   // 포커스 표시는 모든 컨트롤이 같은 강조색(2026-10-01 사용자: 까만색·파란색이 섞여 보임). 기본 버튼은 이중 선이 그 역할
                Gdiplus.DrawRoundRect(dc, 0, 0, w, h, r, Theme.AccentInk, Ctl.S(hwnd, 1));

            nint font = kind is Icon or IconBordered ? Theme.FontIcon : Native.GetWindowLongPtrW(hwnd, Ctl.OffFont);
            if (kind == Section)
            {
                bool open = (Ctl.Style(hwnd) & Expanded) != 0;
                int x0 = Ctl.S(hwnd, 2), iw = Ctl.S(hwnd, 20);
                uint ink = enabled ? Theme.ControlText : Theme.DisabledText;
                Ctl.Text(dc, Theme.FontIcon, open ? "\uE70D" : "\uE76C", Theme.SecondaryText, x0, 0, x0 + iw, h, Native.DT_LEFT | Native.DT_VCENTER);   // ChevronDown / ChevronRight
                Ctl.Text(dc, font, Native.GetWindowText(hwnd), ink, x0 + iw, 0, w, h, Native.DT_LEFT | Native.DT_VCENTER | Native.DT_END_ELLIPSIS);
                return;
            }
            if (kind == Back)
            {
                int x0 = Ctl.S(hwnd, 4), iw = Ctl.S(hwnd, 20);
                Ctl.Text(dc, Theme.FontIcon, BackChevron, text, x0, 0, x0 + iw, h, Native.DT_LEFT | Native.DT_VCENTER);
                Ctl.Text(dc, font, Native.GetWindowText(hwnd), text, x0 + iw, 0, w, h, Native.DT_LEFT | Native.DT_VCENTER);
                return;
            }
            string caption = Native.GetWindowText(hwnd);
            Ctl.Text(dc, font, caption, text, 0, 0, w, h, Native.DT_CENTER | Native.DT_VCENTER);
        });
        } finally { if (bandBrush != 0) Native.DeleteObject(bandBrush); }
    }
}

/// <summary>
/// 목록 행. 제목(창 텍스트) + 부제 + 왼쪽 아이콘 + 오른쪽 꺾쇠. 누르면 BN_CLICKED.
/// 스타일 비트: 0x01 Chevron · 0x02 Last(카드의 마지막 행: 구분선 없음, 아래 모서리 둥글게) · 0x04 AccentTitle(추가 행처럼 강조색 글자)
///             · 0x08 PlainIcon(배경 없는 아이콘) · 0x10 First(카드의 첫 행: 위 모서리 둥글게)
/// </summary>
internal static unsafe class Row
{
    public const string ClassName = "OneKeyRow";
    public const uint Chevron = 0x01, Last = 0x02, AccentTitle = 0x04, PlainIcon = 0x08, First = 0x10, ChevronDown = 0x20, ChevronUp = 0x40;
    /// <summary>부제를 제목 아래가 아니라 제목 오른쪽 같은 줄에 작게 쓴다 (제목 위치가 부제 유무와 무관하게 고정된다).</summary>
    public const uint InlineSubtitle = 0x80;
    /// <summary>오른쪽 모서리를 둥글게 하지 않는다. 행 오른쪽에 다른 컨트롤(목록의 [입력] 버튼)이 카드 위에 따로 놓일 때.</summary>
    public const uint SquareRight = 0x100;
    /// <summary>마우스를 올리거나 키보드 포커스가 있을 때 오른쪽에 "편집" 힌트(연필 + 글)를 보인다: 행을 누르면 편집, [입력]은 입력 칩 (Codex QA-04).</summary>
    public const uint EditHint = 0x200;
    /// <summary>EditHint 의 글. 언어에 따라 바꾼다.</summary>
    public static string EditHintText => T.RowEditHint;
    public const int Height = 42, PadX = 14, IconBox = 28, CardRadius = Theme.CardRadius;
    /// <summary>새 디자인: 판 위 조각(목록 항목) · 판 안의 자동 잠금 칸(누르면 설정). 그리기는 <see cref="MetalUi"/>.</summary>
    public const uint Tile = 0x400, LockInfo = 0x800;
    /// <summary>새 디자인 화면의 고르기 조각(추가 메뉴): 왼쪽 둥근 표식(Icon — 아이콘 글리프면 아이콘, 아니면 짧은 글) + 이름 + 설명 + ›. 창은 그늘 자리만큼 크다.</summary>
    public const uint Choice = 0x1000;

    private sealed class Data { public string Subtitle = ""; public string Icon = ""; public string Mod = ""; public string Key = ""; }
    private static readonly Dictionary<nint, Data> _data = new();

    public static void Register(nint hInst) => Ctl.RegisterClass(hInst, ClassName, &WndProc, 16);

    public static void Set(nint hwnd, string title, string subtitle, string icon)
    {
        _data[hwnd] = new Data { Subtitle = subtitle, Icon = icon };
        Native.SetText(hwnd, title);
        Native.InvalidateRect(hwnd, 0, false);
    }

    /// <summary>
    /// 조각(<see cref="Tile"/>): 이름(창 글), 위 작은 글(종류·넣는 방법), 단축키 칸(조합 작게 · 키 크게).
    /// 자동 잠금 칸(<see cref="LockInfo"/>): mod = 이름표, key = 큰 숫자, subtitle = 단위.
    /// </summary>
    public static void SetPlate(nint hwnd, string title, string subtitle, string mod, string key)
    {
        _data[hwnd] = new Data { Subtitle = subtitle, Mod = mod, Key = key };
        Native.SetText(hwnd, title);
        Native.InvalidateRect(hwnd, 0, false);
    }

    /// <summary>부제(접근성 설명).</summary>
    public static string? Subtitle(nint hwnd) => _data.TryGetValue(hwnd, out Data? d) ? d.Subtitle : null;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            // 포커스가 있을 때 Enter 를 누르면 이 행이 열리도록 (없으면 IsDialogMessage 가 기본 버튼을 누른다)
            if (msg == Native.WM_GETDLGCODE) return (nint)(Native.DLGC_BUTTON | (Native.GetFocus() == hwnd ? Native.DLGC_DEFPUSHBUTTON : 0));
            if (msg == Native.WM_NCDESTROY) _data.Remove(hwnd);
            if (Ctl.Common(hwnd, msg, wParam, lParam, Ctl.NotifyParent, out nint r)) return r;
            if (msg == Native.WM_PAINT) { Paint(hwnd); return 0; }
        }
        catch { }
        return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private static int MeasureGdi(nint dc, nint font, string s)
    {
        nint old = Native.SelectObject(dc, font);
        int w = Native.TextWidth(dc, s);
        Native.SelectObject(dc, old);
        return w;
    }

    private static void Paint(nint hwnd)
    {
        uint style = Ctl.Style(hwnd);
        _data.TryGetValue(hwnd, out Data? d); d ??= new Data();
        if ((style & (Tile | LockInfo)) != 0) { MetalUi.PaintRow(hwnd, style, d.Subtitle, d.Mod, d.Key); return; }
        if ((style & Choice) != 0 && MetalUi.On(hwnd)) { MetalUi.PaintChoice(hwnd, d.Subtitle, d.Icon); return; }
        if (MetalUi.On(hwnd)) { MetalUi.PaintPlainRow(hwnd, style, d.Subtitle, d.Icon); return; }   // 새 디자인 화면: 조각 위 행
        // 행은 카드 위에 놓이므로, 바탕색으로 지운 뒤 카드 모양(첫/마지막 행이면 모서리 둥글게)을 직접 그린다.
        Ctl.Paint(hwnd, Theme.BgBrush, (dc, w, h) =>
        {
            nint st = Ctl.State(hwnd);
            bool enabled = Native.IsWindowEnabled(hwnd);
            bool hot = Ctl.BandHot(hwnd) && enabled, pressed = (st & Ctl.StPressed) != 0 && enabled;   // 짝 버튼 위에 있어도 줄 강조
            bool focus = Ctl.HasFocusRing(hwnd);
            int S(int v) => Ctl.S(hwnd, v);

            int r = S(CardRadius);
            int top = (style & First) != 0 ? 0 : -r * 2;          // 둥글지 않은 쪽은 바깥으로 늘려 직각이 되게
            int bottom = (style & Last) != 0 ? h : h + r * 2;
            uint fill = hot || pressed ? Theme.RowFill(pressed) : Theme.CardBg;
            int rightExt = (style & SquareRight) != 0 ? r * 2 : 0;   // 오른쪽 둥근 모서리를 바깥으로 밀어 직각으로
            Gdiplus.FillRoundRect(dc, 0, top, w + rightExt, bottom - top, r, fill);
            // 카드 1px 테두리(B) 가운데 이 행이 덮는 몫: 왼쪽(과 오른쪽), 첫 행이면 위, 마지막 행이면 아래. 같은 모양을 늘려 그리면 잘린 쪽은 보이지 않는다.
            Gdiplus.DrawRoundRect(dc, 0, top, w + rightExt, bottom - top, r, Theme.CardBorder, Math.Max(1, S(1)));

            int x = S(PadX);
            if (d.Icon.Length > 0)
            {
                int box = S(IconBox), by = (h - box) / 2;
                if ((style & PlainIcon) == 0)
                    Gdiplus.FillEllipse(dc, x, by, box, box, Theme.TileFill);   // 아이콘 원(B .tile)
                Ctl.Text(dc, Theme.FontIcon, d.Icon, (style & AccentTitle) != 0 ? Theme.AccentInk : (style & PlainIcon) == 0 ? Theme.TileInk : Theme.SecondaryText,
                    x, by, x + box, by + box, Native.DT_CENTER | Native.DT_VCENTER);
                x += box + S(12);
            }

            int right = w - S(PadX);
            if ((style & (Chevron | ChevronDown | ChevronUp)) != 0)
            {
                string glyph = (style & ChevronDown) != 0 ? "" : (style & ChevronUp) != 0 ? "" : "";
                Ctl.Text(dc, Theme.FontIcon, glyph, Theme.TertiaryText, right - S(14), 0, right, h, Native.DT_RIGHT | Native.DT_VCENTER);
                right -= S(22);
            }

            if ((style & EditHint) != 0 && enabled && (hot || focus || Native.GetFocus() == hwnd))
            {
                // "✎ 편집": 글 폭만큼 오른쪽에 두고 제목은 그 앞에서 말줄임
                nint small = Theme.FontSmall;
                int tw = Dw.Width(small, EditHintText) ?? MeasureGdi(dc, small, EditHintText);
                Ctl.Text(dc, small, EditHintText, Theme.SecondaryText, right - tw, 0, right, h, Native.DT_RIGHT | Native.DT_VCENTER);
                Ctl.Text(dc, Theme.FontIcon, "\uE70F", Theme.SecondaryText, right - tw - S(20), 0, right - tw - S(4), h, Native.DT_RIGHT | Native.DT_VCENTER);
                right -= tw + S(26);
            }

            uint titleColor = !enabled ? Theme.DisabledText : (style & AccentTitle) != 0 ? Theme.AccentLabel : Theme.ControlText;
            nint font = Native.GetWindowLongPtrW(hwnd, Ctl.OffFont);
            if (d.Subtitle.Length > 0 && (style & InlineSubtitle) != 0)
            {
                string title = Native.GetWindowText(hwnd);
                // 부제 위치는 제목을 그린 것과 같은 방식으로 잰 폭으로 정한다(DirectWrite 면 DirectWrite 로)
                int tw;
                if (Dw.Width(font, title) is int dwW) tw = dwW;
                else
                {
                    nint oldF = font != 0 ? Native.SelectObject(dc, font) : 0;
                    tw = Native.TextWidth(dc, title);
                    if (oldF != 0) Native.SelectObject(dc, oldF);
                }
                Ctl.Text(dc, font, title, titleColor, x, 0, right, h, Native.DT_LEFT | Native.DT_VCENTER);
                Ctl.Text(dc, Theme.FontSmall, d.Subtitle, Theme.SecondaryText, x + tw + S(10), 0, right, h, Native.DT_LEFT | Native.DT_VCENTER | Native.DT_END_ELLIPSIS);
            }
            else if (d.Subtitle.Length > 0)
            {
                Ctl.Text(dc, font, Native.GetWindowText(hwnd), titleColor, x, S(6), right, S(24), Native.DT_LEFT | Native.DT_VCENTER | Native.DT_END_ELLIPSIS);
                Ctl.Text(dc, Theme.FontSmall, d.Subtitle, Theme.SecondaryText, x, S(23), right, h - S(4), Native.DT_LEFT | Native.DT_VCENTER | Native.DT_END_ELLIPSIS);
            }
            else
                Ctl.Text(dc, font, Native.GetWindowText(hwnd), titleColor, x, 0, right, h, Native.DT_LEFT | Native.DT_VCENTER | Native.DT_END_ELLIPSIS);

            if ((style & Last) == 0)
            {
                var sep = new Native.RECT { left = S(PadX), top = h - S(1), right = w, bottom = h };
                Native.FillRect(dc, ref sep, Theme.BorderBrush);
            }
            if (focus)
                Gdiplus.DrawRoundRect(dc, S(2), S(2), w - S(4), h - S(4), S(6), Theme.AccentInk, S(1));
        });
    }
}

/// <summary>
/// 단축키 녹음 칸. 칸을 누르고(포커스) 원하는 키 조합을 실제로 누르면 기록된다.
/// Backspace / Delete 로 지운다. 값이 바뀌면 BN_CLICKED 를 보낸다.
/// </summary>
internal static unsafe class HotkeyBox
{
    public const string ClassName = "OneKeyHotkey";
    private const int OffMods = 16, OffVk = 24;

    public static void Register(nint hInst) => Ctl.RegisterClass(hInst, ClassName, &WndProc, 32);

    public static (uint Mods, uint Vk) Get(nint h)
        => ((uint)(long)Native.GetWindowLongPtrW(h, OffMods), (uint)(long)Native.GetWindowLongPtrW(h, OffVk));

    public static void Set(nint h, uint mods, uint vk)
    {
        Native.SetWindowLongPtrW(h, OffMods, (nint)mods);
        Native.SetWindowLongPtrW(h, OffVk, (nint)vk);
        Native.InvalidateRect(h, 0, false);
    }

    public static string Text(uint mods, uint vk)
    {
        if (vk == 0) return "";
        var sb = new System.Text.StringBuilder();
        if ((mods & 2) != 0) sb.Append("Ctrl + ");
        if ((mods & 1) != 0) sb.Append("Alt + ");
        if ((mods & 4) != 0) sb.Append("Shift + ");
        if ((mods & 8) != 0) sb.Append("Win + ");
        sb.Append(Keys.NameOf(vk));
        return sb.ToString();
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            switch (msg)
            {
                case Native.WM_GETDLGCODE:
                {
                    // 조합 키와 함께 누른 Enter(예: 목록 입력 확정 키 Ctrl+Alt+Enter)는 기본 버튼([저장])으로 넘기지 않고 기록한다.
                    // 조합 키 없는 Enter 는 예전처럼 대화상자 규칙(기본 버튼)으로 간다.
                    uint code = Native.DLGC_WANTCHARS | Native.DLGC_WANTARROWS;
                    var m = (Native.MSG*)lParam;
                    if (m != null && (m->message == Native.WM_KEYDOWN || m->message == Native.WM_SYSKEYDOWN) && m->wParam == Native.VK_RETURN
                        && (Native.GetKeyState(Native.VK_CONTROL) < 0 || Native.GetKeyState(Native.VK_MENU) < 0 || Native.GetKeyState(Native.VK_SHIFT) < 0
                            || Native.GetKeyState(Native.VK_LWIN) < 0 || Native.GetKeyState(Native.VK_RWIN) < 0))
                        code |= Native.DLGC_WANTALLKEYS;
                    return (nint)code;
                }
                case Native.WM_KEYDOWN:
                case Native.WM_SYSKEYDOWN:
                    if (OnKey(hwnd, (int)wParam)) return 0;
                    break;   // Alt+F4, Alt+Space 는 기록하지 않고 Windows 에 넘긴다 (창 닫기 / 시스템 메뉴)
                case Native.WM_SYSCHAR:
                case Native.WM_CHAR:
                    return 0;   // 삑 소리 / 메뉴 활성화 방지
                case Native.WM_SYSKEYUP:
                    return 0;
            }
            if (Ctl.Common(hwnd, msg, wParam, lParam, _ => { }, out nint r)) return r;
            if (msg == Native.WM_PAINT) { Paint(hwnd); return 0; }
        }
        catch { }
        return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    /// <summary>키를 처리했으면 true. false 면 Windows 의 기본 처리(Alt+F4 등)에 맡긴다.</summary>
    private static bool OnKey(nint hwnd, int vk)
    {
        if (vk is Native.VK_SHIFT or Native.VK_CONTROL or Native.VK_MENU or Native.VK_LWIN or Native.VK_RWIN) return true;   // 조합 키만 눌린 상태
        if (vk == Native.VK_TAB || vk == Native.VK_ESCAPE) return true;

        uint mods = 0;
        if ((Native.GetKeyState(Native.VK_CONTROL) & 0x8000) != 0) mods |= 2;
        if ((Native.GetKeyState(Native.VK_MENU) & 0x8000) != 0) mods |= 1;
        if ((Native.GetKeyState(Native.VK_SHIFT) & 0x8000) != 0) mods |= 4;
        if ((Native.GetKeyState(Native.VK_LWIN) & 0x8000) != 0 || (Native.GetKeyState(Native.VK_RWIN) & 0x8000) != 0) mods |= 8;

        // Alt+F4(창 닫기) / Alt+Space(시스템 메뉴)를 전역 단축키로 잡으면 모든 프로그램의 그 키가 먹통이 된다.
        if (mods == 1 && (vk == 0x73 || vk == Native.VK_SPACE)) return false;

        if (mods == 0 && (vk == Native.VK_BACK || vk == Native.VK_DELETE)) { Set(hwnd, 0, 0); CtlAcc.Changed(hwnd, value: true); Ctl.NotifyParent(hwnd); return true; }
        if (Keys.IndexOf((uint)vk) == 0) return true;   // 목록에 없는 키는 받지 않는다

        Set(hwnd, mods, (uint)vk);
        CtlAcc.Changed(hwnd, value: true);
        Ctl.NotifyParent(hwnd);
        return true;
    }

    private static void Paint(nint hwnd)
    {
        if (MetalUi.On(hwnd)) { (uint mm, uint mv) = Get(hwnd); MetalUi.PaintHotkey(hwnd, Text(mm, mv), Native.GetFocus() == hwnd ? T.HkPress : T.HkNone); return; }
        Ctl.Paint(hwnd, Theme.CardBrush, (dc, w, h) =>
        {
            bool focus = Native.GetFocus() == hwnd;
            bool enabled = Native.IsWindowEnabled(hwnd);
            int S(int v) => Ctl.S(hwnd, v);
            (uint mods, uint vk) = Get(hwnd);
            string text = Text(mods, vk);

            Gdiplus.FillRoundRect(dc, 0, 0, w, h, S(Theme.FieldRadius), Theme.EditBg);
            Gdiplus.DrawRoundRect(dc, 0, 0, w, h, S(Theme.FieldRadius), focus ? Theme.AccentInk : Theme.FieldBorder, S(1));

            nint font = Native.GetWindowLongPtrW(hwnd, Ctl.OffFont);
            if (text.Length > 0)
                Ctl.Text(dc, font, text, enabled ? Theme.ControlText : Theme.DisabledText, S(10), 0, w - S(10), h, Native.DT_LEFT | Native.DT_VCENTER | Native.DT_END_ELLIPSIS);
            else
                Ctl.Text(dc, font, focus ? T.HkPress : T.HkNone, Theme.SecondaryText, S(10), 0, w - S(10), h, Native.DT_LEFT | Native.DT_VCENTER | Native.DT_END_ELLIPSIS);
        });
    }
}

/// <summary>
/// 단계형 슬라이더. 정해진 몇 개의 값 중 하나를 고른다 (눈금 + 손잡이 + 눈금마다 아래에 작은 값 글자 — 늘 보인다).
/// 마우스로 끌거나 누르고, 키보드 좌우/Home/End 로 움직인다. 값이 바뀌면 BN_CLICKED 를 보낸다.
/// </summary>
internal static unsafe class Slider
{
    public const string ClassName = "OneKeySlider";
    private const int OffIndex = 16, OffCount = 24;
    private static readonly Dictionary<nint, string[]> _labels = new();

    public static void Register(nint hInst) => Ctl.RegisterClass(hInst, ClassName, &WndProc, 32);

    public static void Set(nint h, string[] labels, int index)
    {
        _labels[h] = labels;
        Native.SetWindowLongPtrW(h, OffCount, labels.Length);
        Native.SetWindowLongPtrW(h, OffIndex, Math.Clamp(index, 0, Math.Max(0, labels.Length - 1)));
        Native.InvalidateRect(h, 0, false);
    }

    public static int Get(nint h) => (int)(long)Native.GetWindowLongPtrW(h, OffIndex);
    private static int Count(nint h) => (int)(long)Native.GetWindowLongPtrW(h, OffCount);
    /// <summary>지금 고른 칸의 글(접근성 값).</summary>
    public static string Text(nint h) => _labels.TryGetValue(h, out string[]? l) && Get(h) is int i && i >= 0 && i < l.Length ? l[i] : "";

    private static void Move(nint h, int index, bool notify)
    {
        int n = Count(h); if (n <= 0) return;
        index = Math.Clamp(index, 0, n - 1);
        if (index == Get(h)) return;
        Native.SetWindowLongPtrW(h, OffIndex, index);
        Native.InvalidateRect(h, 0, false);
        CtlAcc.Changed(h, value: true);
        if (notify) Ctl.NotifyParent(h);
    }

    private static (int x0, int x1, int ty) Geometry(nint h, int w, int hh)
    {
        int pad = Ctl.S(h, 14);
        return (pad, w - pad, hh / 2 - Ctl.S(h, 6));   // 트랙은 위쪽, 아래에 눈금 글자(2026-10-05 사용자: 위의 "사용 안 함"이 묶음 제목처럼 보임)
    }

    private static int IndexAt(nint h, int x)
    {
        Native.GetClientRect(h, out Native.RECT rc);
        (int x0, int x1, _) = Geometry(h, rc.right, rc.bottom);
        int n = Count(h); if (n <= 1) return 0;
        double t = (x - x0) / (double)Math.Max(1, x1 - x0);
        return (int)Math.Round(Math.Clamp(t, 0, 1) * (n - 1));
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            switch (msg)
            {
                case Native.WM_GETDLGCODE: return (nint)Native.DLGC_WANTARROWS;
                case Native.WM_NCDESTROY: _labels.Remove(hwnd); break;
                case Native.WM_LBUTTONDOWN:
                    if (!Native.IsWindowEnabled(hwnd)) return 0;
                    Native.SetFocus(hwnd); Native.SetCapture(hwnd);
                    Ctl.SetState(hwnd, Ctl.State(hwnd) | Ctl.StPressed);
                    if (MetalUi.On(hwnd))
                    {
                        // 새 디자인: ‹ › 를 누르면 한 단계, 아래 점을 누르거나 끌면 그 단계
                        int to = MetalUi.StepHit(hwnd, Native.LoWord(lParam), Native.HiWord(lParam), Count(hwnd), Get(hwnd), false);
                        if (to >= 0) Move(hwnd, to, true);
                        Native.InvalidateRect(hwnd, 0, false);
                        return 0;
                    }
                    Move(hwnd, IndexAt(hwnd, Native.LoWord(lParam)), true);
                    return 0;
                case Native.WM_MOUSEMOVE:
                    if ((Ctl.State(hwnd) & Ctl.StPressed) != 0)
                    {
                        if (MetalUi.On(hwnd)) { int to = MetalUi.StepHit(hwnd, (short)Native.LoWord(lParam), (short)Native.HiWord(lParam), Count(hwnd), Get(hwnd), true); if (to >= 0) Move(hwnd, to, true); }
                        else Move(hwnd, IndexAt(hwnd, Native.LoWord(lParam)), true);
                        return 0;
                    }
                    break;   // 호버 추적은 공통 처리로
                case Native.WM_LBUTTONUP:
                    if ((Ctl.State(hwnd) & Ctl.StPressed) != 0) { Native.ReleaseCapture(); Ctl.SetState(hwnd, Ctl.State(hwnd) & ~Ctl.StPressed); MetalUi.StepRelease(); Native.InvalidateRect(hwnd, 0, false); }
                    return 0;
                case Native.WM_KEYDOWN:
                {
                    int i = Get(hwnd);
                    switch ((int)wParam)
                    {
                        case 0x25: case 0x28: Move(hwnd, i - 1, true); return 0;   // ←, ↓
                        case 0x27: case 0x26: Move(hwnd, i + 1, true); return 0;   // →, ↑
                        case 0x24: Move(hwnd, 0, true); return 0;                  // Home
                        case 0x23: Move(hwnd, Count(hwnd) - 1, true); return 0;    // End
                    }
                    break;
                }
            }
            if (Ctl.Common(hwnd, msg, wParam, lParam, _ => { }, out nint r)) return r;
            if (msg == Native.WM_PAINT) { Paint(hwnd); return 0; }
        }
        catch { }
        return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private static void Paint(nint hwnd)
    {
        if (MetalUi.On(hwnd)) { MetalUi.PaintStepper(hwnd, _labels.TryGetValue(hwnd, out string[]? ml) ? ml : Array.Empty<string>(), Get(hwnd)); return; }
        Ctl.Paint(hwnd, Theme.CardBrush, (dc, w, h) =>
        {
            int S(int v) => Ctl.S(hwnd, v);
            nint st = Ctl.State(hwnd);
            bool enabled = Native.IsWindowEnabled(hwnd);
            bool active = enabled && ((st & (Ctl.StHot | Ctl.StPressed)) != 0 || Ctl.HasFocusRing(hwnd));
            int n = Count(hwnd), idx = Get(hwnd);
            (int x0, int x1, int ty) = Geometry(hwnd, w, h);
            int tx = n <= 1 ? x0 : x0 + (int)Math.Round((x1 - x0) * (idx / (double)(n - 1)));
            int th = Math.Max(2, S(3));

            // 트랙: 왼쪽(선택된 구간)은 강조색, 오른쪽은 회색
            Gdiplus.FillRoundRect(dc, x0, ty - th / 2, x1 - x0, th, th / 2f, Theme.ToggleOff, 255);
            if (tx > x0) Gdiplus.FillRoundRect(dc, x0, ty - th / 2, tx - x0, th, th / 2f, Theme.AccentInk, enabled ? (byte)255 : (byte)110);

            // 눈금
            for (int i = 0; i < n; i++)
            {
                int x = n <= 1 ? x0 : x0 + (int)Math.Round((x1 - x0) * (i / (double)(n - 1)));
                var tick = new Native.RECT { left = x - Math.Max(1, S(1)) / 2, top = ty - S(5), right = x + Math.Max(1, S(1)), bottom = ty + S(5) };
                nint b = Native.CreateSolidBrush(i <= idx ? Theme.Mix(Theme.AccentInk, Theme.CardBg, 0.35) : Theme.Mix(Theme.ToggleOff, Theme.ControlText, 0.25));
                Native.FillRect(dc, ref tick, b); Native.DeleteObject(b);
            }

            // 손잡이
            int d = S(18);
            Gdiplus.FillEllipse(dc, tx - d / 2f, ty - d / 2f, d, d, Theme.CardBg);
            Gdiplus.DrawRoundRect(dc, tx - d / 2, ty - d / 2, d, d, d / 2f, active ? Theme.AccentInk : Theme.Mix(Theme.BorderColor, Theme.ControlText, 0.25), Math.Max(1, S(1)));

            // 눈금마다 아래에 값 글자(작게, 늘 보임). 고른 칸은 본문 색, 나머지는 보조 색
            if (_labels.TryGetValue(hwnd, out string[]? labels))
                for (int i = 0; i < n && i < labels.Length; i++)
                {
                    int x = n <= 1 ? x0 : x0 + (int)Math.Round((x1 - x0) * (i / (double)(n - 1)));
                    int tw;
                    if (Dw.Width(Theme.FontSmall, labels[i]) is int dwW) tw = dwW + S(6);   // 그리는 방식과 같은 방식으로 잰다
                    else
                    {
                        nint old = Native.SelectObject(dc, Theme.FontSmall);
                        tw = Native.TextWidth(dc, labels[i]) + S(6);
                        Native.SelectObject(dc, old);
                    }
                    int lx = Math.Clamp(x - tw / 2, 0, Math.Max(0, w - tw));
                    uint c = !enabled ? Theme.DisabledText : i == idx ? Theme.ControlText : Theme.SecondaryText;
                    Ctl.Text(dc, Theme.FontSmall, labels[i], c, lx, ty + S(11), lx + tw, h, Native.DT_CENTER);
                }
        });
    }
}

/// <summary>
/// 직접 그리는 드롭다운. 둥근 상자에 현재 값과 아래 꺾쇠. 누르면 항목 목록이 바로 아래에 펼쳐진다 (Windows 팝업 메뉴).
/// 값이 바뀌면 BN_CLICKED 를 보낸다. 키보드: Space/Enter 로 열기, 위·아래로 바로 바꾸기.
/// </summary>
internal static unsafe class Dropdown
{
    public const string ClassName = "OneKeyDropdown";
    /// <summary>
    /// 창 스타일 비트: 펼치는 목록 대신 고를 것을 모두 나란히 보이는 버튼 묶음(2026-10-05 사용자 B안 — 한 줄에서 바로 고르기).
    /// 누른 자리의 것을 고른다. 키보드는 ← → ↑ ↓, Space·Enter 는 다음 것. 고르면 BN_CLICKED. 접근성은 드롭다운과 같다(값 = 고른 글).
    /// </summary>
    public const uint Seg = 0x0001;
    private const int OffIndex = 16;
    private static readonly Dictionary<nint, string[]> _items = new();

    public static void Register(nint hInst) => Ctl.RegisterClass(hInst, ClassName, &WndProc, 24);

    public static void Set(nint h, string[] items, int index)
    {
        _items[h] = items;
        Native.SetWindowLongPtrW(h, OffIndex, Math.Clamp(index, 0, Math.Max(0, items.Length - 1)));
        Native.InvalidateRect(h, 0, false);
    }

    public static int Get(nint h) => (int)(long)Native.GetWindowLongPtrW(h, OffIndex);
    /// <summary>지금 고른 항목의 글(접근성 값).</summary>
    public static string Text(nint h) => _items.TryGetValue(h, out string[]? it) && Get(h) is int i && i >= 0 && i < it.Length ? it[i] : "";

    private static void Select(nint h, int index)
    {
        if (!_items.TryGetValue(h, out string[]? items) || items.Length == 0) return;
        index = Math.Clamp(index, 0, items.Length - 1);
        if (index == Get(h)) return;
        Native.SetWindowLongPtrW(h, OffIndex, index);
        Native.InvalidateRect(h, 0, false);
        CtlAcc.Changed(h, value: true);
        Ctl.NotifyParent(h);
    }

    private static bool IsSeg(nint h) => ((long)Native.GetWindowLongPtrW(h, -16 /* GWL_STYLE */) & Seg) != 0;

    /// <summary>Seg 모양의 칸 경계: 글 너비에 비례해 나눈다(긴 글이 잘리지 않게).</summary>
    private static int[] SegEdges(nint h, int w)
    {
        string[] items = _items.TryGetValue(h, out string[]? it) ? it : Array.Empty<string>();
        if (MetalUi.On(h)) return MetalUi.SegEdges(h, items, w).Select(e => (int)Math.Round(e)).ToArray();   // 새 디자인: 그리는 칸과 같은 경계
        int n = Math.Max(1, items.Length);
        var edges = new int[n + 1];
        nint dc = Native.GetDC(h);
        nint old = Native.SelectObject(dc, Native.GetWindowLongPtrW(h, Ctl.OffFont));
        var want = new int[n];
        int sum = 0, pad = Ctl.S(h, 16);
        for (int i = 0; i < n; i++) { want[i] = (i < items.Length ? Native.TextWidth(dc, items[i]) : 0) + pad; sum += want[i]; }
        Native.SelectObject(dc, old);
        Native.ReleaseDC(h, dc);
        int acc = 0;
        for (int i = 0; i < n; i++) { edges[i] = (int)((long)w * acc / Math.Max(1, sum)); acc += want[i]; }
        edges[n] = w;
        return edges;
    }

    private static void Open(nint h)
    {
        if (IsSeg(h))   // 키보드(Space·Enter): 다음 것으로
        {
            int n = _items.TryGetValue(h, out string[]? its) ? its.Length : 0;
            if (n > 0) Select(h, (Get(h) + 1) % n);
            return;
        }
        if (!_items.TryGetValue(h, out string[]? items) || items.Length == 0) return;
        nint menu = Native.CreatePopupMenu();
        if (menu == 0) return;
        try
        {
            int cur = Get(h);
            for (int i = 0; i < items.Length; i++)
                fixed (char* p = items[i]) Native.AppendMenuW(menu, Native.MF_STRING | (i == cur ? Native.MF_CHECKED : 0), (nuint)(i + 1), p);
            Native.GetClientRect(h, out Native.RECT rc);
            var pt = new Native.POINT { x = 0, y = rc.bottom + Ctl.S(h, 4) };
            Native.ClientToScreen(h, ref pt);
            Native.SetFocus(h);
            int cmd = Native.TrackPopupMenu(menu, Native.TPM_LEFTALIGN | Native.TPM_TOPALIGN | Native.TPM_RETURNCMD, pt.x, pt.y, 0, h, 0);
            if (cmd > 0) Select(h, cmd - 1);
        }
        finally { Native.DestroyMenu(menu); }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            switch (msg)
            {
                case Native.WM_GETDLGCODE: return (nint)(Native.DLGC_BUTTON | Native.DLGC_WANTARROWS);
                case Native.WM_NCDESTROY: _items.Remove(hwnd); break;
                case Native.WM_KEYDOWN:
                    switch ((int)wParam)
                    {
                        case 0x26: case 0x25: Select(hwnd, Get(hwnd) - 1); return 0;   // ↑ ←
                        case 0x28: case 0x27: Select(hwnd, Get(hwnd) + 1); return 0;   // ↓ →
                    }
                    break;
                case 0x00F1: Select(hwnd, (int)wParam); return 0;   // BM_SETCHECK: 번호로 고르기(시험·접근성)
                case 0x00F0: return Get(hwnd);                     // BM_GETCHECK: 고른 번호
                case Native.WM_LBUTTONDOWN:
                case 0x0203: // WM_LBUTTONDBLCLK
                    if (IsSeg(hwnd))
                    {
                        Native.SetFocus(hwnd);
                        Native.GetClientRect(hwnd, out Native.RECT sr);
                        int[] ed = SegEdges(hwnd, sr.right);
                        int mx = (short)(lParam & 0xFFFF);
                        for (int i = 0; i < ed.Length - 1; i++) if (mx >= ed[i] && mx < ed[i + 1]) { Select(hwnd, i); break; }
                        return 0;
                    }
                    break;
                case Native.WM_LBUTTONUP:
                    if (IsSeg(hwnd)) return 0;
                    break;
            }
            if (Ctl.Common(hwnd, msg, wParam, lParam, Open, out nint r)) return r;
            if (msg == Native.WM_PAINT) { if (IsSeg(hwnd)) PaintSeg(hwnd); else Paint(hwnd); return 0; }
        }
        catch { }
        return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private static void Paint(nint hwnd)
    {
        if (MetalUi.On(hwnd)) { MetalUi.PaintDropdown(hwnd, _items.TryGetValue(hwnd, out string[]? mi) && mi.Length > 0 ? mi[Math.Clamp(Get(hwnd), 0, mi.Length - 1)] : ""); return; }
        Ctl.Paint(hwnd, Theme.CardBrush, (dc, w, h) =>
        {
            int S(int v) => Ctl.S(hwnd, v);
            nint st = Ctl.State(hwnd);
            bool enabled = Native.IsWindowEnabled(hwnd);
            bool hot = (st & (Ctl.StHot | Ctl.StPressed)) != 0 && enabled;
            bool focus = Native.GetFocus() == hwnd;   // 입력 컨트롤(드롭다운)은 입력칸·키 조합 칸과 같이 포커스면 늘 강조 테두리
            uint fill = hot ? Theme.Mix(Theme.EditBg, Theme.ControlText, Theme.HoverMix / 2) : Theme.EditBg;
            Gdiplus.FillRoundRect(dc, 0, 0, w, h, S(Theme.FieldRadius), fill);
            Gdiplus.DrawRoundRect(dc, 0, 0, w, h, S(Theme.FieldRadius), focus ? Theme.AccentInk : Theme.FieldBorder, S(1));

            string text = _items.TryGetValue(hwnd, out string[]? items) && items.Length > 0 ? items[Get(hwnd)] : "";
            nint font = Native.GetWindowLongPtrW(hwnd, Ctl.OffFont);
            Ctl.Text(dc, font, text, enabled ? Theme.ControlText : Theme.DisabledText, S(10), 0, w - S(30), h, Native.DT_LEFT | Native.DT_VCENTER | Native.DT_END_ELLIPSIS);
            Ctl.Text(dc, Theme.FontIcon, "", Theme.SecondaryText, w - S(26), 0, w - S(8), h, Native.DT_RIGHT | Native.DT_VCENTER);
        });
    }

    private static void PaintSeg(nint hwnd)
    {
        if (MetalUi.On(hwnd)) { MetalUi.PaintSeg(hwnd, _items.TryGetValue(hwnd, out string[]? mi) ? mi : Array.Empty<string>(), Get(hwnd)); return; }
        Ctl.Paint(hwnd, Theme.CardBrush, (dc, w, h) =>
        {
            int S(int v) => Ctl.S(hwnd, v);
            bool enabled = Native.IsWindowEnabled(hwnd);
            bool focus = Native.GetFocus() == hwnd;
            int r = S(Theme.FieldRadius), cur = Get(hwnd);
            string[] items = _items.TryGetValue(hwnd, out string[]? it) ? it : Array.Empty<string>();
            int[] ed = SegEdges(hwnd, w);
            Gdiplus.FillRoundRect(dc, 0, 0, w, h, r, Theme.EditBg);
            nint font = Native.GetWindowLongPtrW(hwnd, Ctl.OffFont);
            for (int i = 0; i < items.Length; i++)
            {
                int x0 = ed[i], x1 = ed[i + 1];
                bool sel = i == cur;
                if (sel)   // 고른 칸: 강조색을 옅게 깔고 강조색 글자(둥근 끝은 바깥 칸만)
                {
                    int sv = Native.SaveDC(dc);
                    Native.IntersectClipRect(dc, x0, 0, x1, h);
                    Gdiplus.FillRoundRect(dc, i == 0 ? 0 : x0 - r * 2, 0, (i == 0 ? x1 : x1 - x0 + r * 2) + (i == items.Length - 1 ? 0 : r * 2), h, r, Theme.Mix(Theme.EditBg, Theme.Accent, Theme.IsDark ? 0.35 : 0.16));
                    Native.RestoreDC(dc, sv);
                }
                else if (i > 0 && i - 1 != cur) Gdiplus.DrawLine(dc, x0, S(6), x0, h - S(6), Theme.FieldBorder, Math.Max(1, S(1)));
                Ctl.Text(dc, font, items[i], !enabled ? Theme.DisabledText : sel ? Theme.AccentLabel : Theme.ControlText, x0 + S(4), 0, x1 - S(4), h,
                         Native.DT_CENTER | Native.DT_VCENTER | Native.DT_SINGLELINE | Native.DT_END_ELLIPSIS);
            }
            Gdiplus.DrawRoundRect(dc, 0, 0, w, h, r, focus ? Theme.AccentInk : Theme.FieldBorder, S(1));
        });
    }
}
