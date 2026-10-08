using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 잠깐 떴다가 저절로 사라지는 알림 (2026-09-29 사용자 요청: 확인 버튼이 필요 없는 알림은 버튼으로 닫을 필요가 없다).
/// 소유 창 아래쪽 가운데에 뜨고, 포커스를 가져가지 않으며(WS_EX_NOACTIVATE), 웹 토스트처럼 서서히 떠오르고(0.15초)
/// 정해진 시간 뒤 서서히 흐려지며 아래로 미끄러져 사라진다(0.3초). 누르면 바로 사라지기 시작한다(오른쪽 위 × 는 그 표시).
/// 테두리는 연결 칩과 같이 숨쉰다(2026-10-03 사용자).
/// 확인이 필요한 경고·오류·질문에는 쓰지 않는다(그건 <see cref="Dialog"/>). 글은 STATIC(id 101)이라 화면 읽기 프로그램·하네스가 읽을 수 있다.
/// </summary>
internal static unsafe class Toast
{
    public const string ClassName = "OneKeyToast";
    public const int IdText = 101, IdHead = 102;   // 102 = 경고 알림의 머리 "⚠ 안내"(B 4단계, 확정안 2장 7)
    private const int PadX = 18, PadY = 12, Bottom = 24, MaxW = 360;
    private const nuint TimerTick = 1;
    private const int FadeInMs = 150, FadeOutMs = 220, Slide = 12;
    private const byte FullAlpha = 245;
    private static bool _registered;

    private sealed class State { public long Start; public int HoldMs, X, Y, SlidePx, Thick, Radius, CloseSize, CloseMargin; public bool Leaving, Still; public long LeaveStart; }
    private static nint _current;
    /// <summary>지금까지 띄운 토스트 수. 검증 전용 메시지가 읽는다(생성 순번: 창 핸들은 다시 쓰일 수 있어 순번으로 구분한다).</summary>
    public static int ShownCount { get; private set; }
    private static readonly Dictionary<nint, State> _state = new();

    /// <summary>ms: 머무는 시간. 기본 1.8초(2026-09-30 사용자 요청으로 2.5초에서 줄임). warn: 실패·주의 알림이면 머리에 "⚠ 안내"(정보 알림은 아이콘 없음).</summary>
    public static void Show(nint owner, string text, int ms = 1800, bool warn = false)
    {
        if (!_registered)
        {
            Ctl.RegisterClass(Native.GetModuleHandleW(null), ClassName, &WndProc, 0);
            _registered = true;
        }
        if (_current != 0 && Native.IsWindow(_current)) Native.DestroyWindow(_current);   // 하나만

        uint dpi = WorkArea.DpiFor(owner);
        int S(int v) => (int)((long)v * dpi / 96);

        // 글 크기에 맞춘다 (한 줄이 넘으면 줄 바꿈)
        nint dc = Native.GetDC(0);
        nint old = Native.SelectObject(dc, Theme.FontBody);
        var rc = new Native.RECT { left = 0, top = 0, right = S(MaxW - 2 * PadX), bottom = 0 };
        Native.DrawText(dc, text, ref rc, Native.DT_WORDBREAK | Native.DT_CALCRECT | Native.DT_NOPREFIX);
        Native.SelectObject(dc, old);
        Native.ReleaseDC(0, dc);
        int tw = rc.right, th = Math.Max(rc.bottom, S(18));
        if (Dw.Measure(Theme.FontBody, text, S(MaxW - 2 * PadX)) is (int, int) dm) { tw = Math.Max(tw, dm.Item1); th = Math.Max(th, dm.Item2); }   // 2b: 두 방식 중 큰 쪽
        int headH = warn ? S(18) : 0;
        if (warn)
        {
            nint dc2 = Native.GetDC(0); nint o2 = Native.SelectObject(dc2, Theme.FontSmallStrong);
            tw = Math.Max(tw, Native.TextWidth(dc2, T.ToastWarnHead));
            if (Dw.Width(Theme.FontSmallStrong, T.ToastWarnHead) is int hw) tw = Math.Max(tw, hw);
            Native.SelectObject(dc2, o2); Native.ReleaseDC(0, dc2);
        }
        int w = tw + 2 * S(PadX), h = th + headH + 2 * S(PadY);

        Native.RECT work = WorkArea.For(owner);
        int x, y;
        if (owner != 0 && Native.IsWindowVisible(owner) && !Native.IsIconic(owner) && Native.GetWindowRect(owner, out Native.RECT or))
        { x = or.left + (or.right - or.left - w) / 2; y = or.bottom - h - S(Bottom); }
        else
        { x = work.left + (work.right - work.left - w) / 2; y = work.bottom - h - S(Bottom); }
        x = Math.Clamp(x, work.left, Math.Max(work.left, work.right - w));
        y = Math.Clamp(y, work.top, Math.Max(work.top, work.bottom - h - S(Slide)));

        nint hwnd;
        fixed (char* cls = ClassName) fixed (char* cap = text)
            hwnd = Native.CreateWindowExW(Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST | Fx.WS_EX_LAYERED, cls, cap,
                Native.WS_POPUP | Native.WS_CLIPCHILDREN, x, y + S(Slide) / 2, w, h, owner, 0, Native.GetModuleHandleW(null), 0);
        if (hwnd == 0) return;   // 못 만들면 알림 없이 넘어간다(작업은 이미 끝났다)
        _current = hwnd;
        ShownCount++;
        // Windows 의 애니메이션 효과가 꺼져 있으면(Codex QA-06) 떠오르기·흐려지기·미끄러지기 없이 제자리에 떴다가 그대로 사라진다
        bool still = !Fx.Animations;
        _state[hwnd] = new State { Start = Environment.TickCount64, HoldMs = ms, X = x, Y = y, SlidePx = still ? 0 : S(Slide), Still = still,
                                   Thick = Chip.GlowThickness(dpi), Radius = S(8), CloseSize = S(7), CloseMargin = S(8) };
        Fx.SetLayeredWindowAttributes(hwnd, 0, still ? FullAlpha : (byte)0, Fx.LWA_ALPHA);
        if (still) Fx.SetWindowPos(hwnd, 0, x, y, 0, 0, Fx.SWP_NOSIZE | Fx.SWP_NOZORDER | Fx.SWP_NOACTIVATE);
        uint corner = (uint)Native.DWMWCP_ROUND; Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, &corner, sizeof(uint));
        uint border = 0xFFFFFFFE /* DWMWA_COLOR_NONE */; Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_BORDER_COLOR, &border, sizeof(uint));   // DWM 테두리는 그리지 않는다: 1px 숨쉬는 줄을 덮는다(칩과 같음)

        nint s;
        if (warn)
        {
            nint hd;
            fixed (char* sc = "STATIC") fixed (char* st = T.ToastWarnHead)
                hd = Native.CreateWindowExW(0, sc, st, Native.SS_CENTER | Native.SS_NOPREFIX | Native.WS_CHILD | Native.WS_VISIBLE,
                    S(PadX), S(PadY), tw, headH, hwnd, IdHead, Native.GetModuleHandleW(null), 0);
            if (hd != 0) { Native.SendMessageW(hd, Native.WM_SETFONT, Theme.FontSmallStrong, 1); DwStatic.Attach(hd); }
        }
        fixed (char* sc = "STATIC") fixed (char* st = text)
            s = Native.CreateWindowExW(0, sc, st, Native.SS_CENTER | Native.SS_NOPREFIX | Native.WS_CHILD | Native.WS_VISIBLE,
                S(PadX), S(PadY) + headH, tw, th, hwnd, IdText, Native.GetModuleHandleW(null), 0);
        if (s != 0) { Native.SendMessageW(s, Native.WM_SETFONT, Theme.FontBody, 1); DwStatic.Attach(s); }

        Native.ShowWindow(hwnd, Native.SW_SHOWNOACTIVATE);
        Native.SetTimer(hwnd, TimerTick, 15, 0);
    }

    /// <summary>한 프레임: 떠오르기 → 머물기 → 흐려지며 아래로 → 닫기.</summary>
    private static void Tick(nint hwnd)
    {
        if (!_state.TryGetValue(hwnd, out State? st)) return;
        long now = Environment.TickCount64, t = now - st.Start;
        Native.InvalidateRect(hwnd, 0, true);   // 숨쉬는 테두리: 칩처럼 늘 숨쉰다(Windows 애니메이션 설정과 무관, 사용자 결정 2026-10-03)
        if (st.Still)
        {
            if (st.Leaving || t >= FadeInMs + st.HoldMs) { Native.KillTimer(hwnd, TimerTick); Native.DestroyWindow(hwnd); }
            return;
        }
        if (!st.Leaving && t >= FadeInMs + st.HoldMs) { st.Leaving = true; st.LeaveStart = now; }
        double a, dy;
        if (!st.Leaving)
        {
            double p = Math.Min(1.0, t / (double)FadeInMs), e = 1 - (1 - p) * (1 - p);   // 감속
            a = e; dy = (1 - e) * st.SlidePx / 2.0;
        }
        else
        {
            double p = Math.Min(1.0, (now - st.LeaveStart) / (double)FadeOutMs), e = p * p;   // 가속
            a = 1 - e; dy = e * st.SlidePx;
            if (p >= 1) { Native.KillTimer(hwnd, TimerTick); Native.DestroyWindow(hwnd); return; }
        }
        Fx.SetLayeredWindowAttributes(hwnd, 0, (byte)Math.Round(a * FullAlpha), Fx.LWA_ALPHA);
        Fx.SetWindowPos(hwnd, 0, st.X, st.Y + (int)Math.Round(dy), 0, 0, Fx.SWP_NOSIZE | Fx.SWP_NOZORDER | Fx.SWP_NOACTIVATE);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            switch (msg)
            {
                case Native.WM_TIMER:
                    Tick(hwnd);
                    return 0;
                case Native.WM_LBUTTONDOWN:   // 누르면 바로 사라지기 시작
                    if (_state.TryGetValue(hwnd, out State? st) && !st.Leaving) { st.Leaving = true; st.LeaveStart = Environment.TickCount64; }
                    return 0;
                case Native.WM_DESTROY:
                    Native.KillTimer(hwnd, TimerTick);
                    _state.Remove(hwnd);
                    if (_current == hwnd) _current = 0;
                    break;
                case Native.WM_MOUSEACTIVATE:
                    return Native.MA_NOACTIVATE;
                case Native.WM_ERASEBKGND:
                {
                    // 칩과 같은 숨쉬는 테두리: 바탕 전체를 테두리 색으로 칠하고 안쪽을 둥근 알림 바탕으로 덮는다(메모리에 그려 한 번에 옮김).
                    // 오른쪽 위에 작은 × (2026-10-03 사용자). 알림 어디를 눌러도 닫히고, ×는 닫을 수 있다는 표시다.
                    Native.GetClientRect(hwnd, out Native.RECT rc);
                    if (!_state.TryGetValue(hwnd, out State? gs)) { Native.FillRect(wParam, ref rc, Theme.ToastBrush); return 1; }
                    int cw = rc.right - rc.left, ch = rc.bottom - rc.top, t = gs.Thick;
                    nint mem = Native.CreateCompatibleDC(wParam), bmp = Native.CreateCompatibleBitmap(wParam, cw, ch), old = Native.SelectObject(mem, bmp);
                    try
                    {
                        // 알림 바탕(어두움)에 가까운 색 10% ↔ 강조색 75%(사용자 결정), 1.2초 주기(알림이 떠 있는 동안 한 번 이상 숨쉬게, 0.2.131)
                        nint br = Native.CreateSolidBrush(Chip.GlowColor(Chip.Breath(gs.Start, 1200), Theme.ToastBg, 0.10, 0.75));
                        try { Native.FillRect(mem, ref rc, br); }
                        finally { Native.DeleteObject(br); }
                        Gdiplus.FillRoundRect(mem, t, t, cw - 2 * t, ch - 2 * t, Math.Max(2, gs.Radius - t), Theme.ToastBg);
                        float x2 = cw - t - gs.CloseMargin, x1 = x2 - gs.CloseSize, y1 = t + gs.CloseMargin, y2 = y1 + gs.CloseSize;
                        uint xc = Theme.Mix(Theme.ToastBg, Theme.ToastText, 0.7);
                        Gdiplus.DrawLine(mem, x1, y1, x2, y2, xc, Math.Max(1f, gs.CloseSize / 6f));
                        Gdiplus.DrawLine(mem, x1, y2, x2, y1, xc, Math.Max(1f, gs.CloseSize / 6f));
                        Native.BitBlt(wParam, 0, 0, cw, ch, mem, 0, 0, 0x00CC0020 /* SRCCOPY */);
                    }
                    finally { Native.SelectObject(mem, old); Native.DeleteObject(bmp); Native.DeleteDC(mem); }
                    return 1;
                }
                case Native.WM_CTLCOLORSTATIC:
                    Native.SetBkColor(wParam, Theme.ToastBg);
                    Native.SetTextColor(wParam, Native.GetDlgCtrlID(lParam) == IdHead ? Theme.WarnInk : Theme.ToastText);
                    return Theme.ToastBrush;
            }
        }
        catch { }
        return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
