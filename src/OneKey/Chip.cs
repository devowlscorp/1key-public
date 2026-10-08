using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 목록에서 넣기(D안)의 입력 칩. 화면 위쪽 가운데에 떠 있는 작은 창: 항목 이름 · 안내 · [입력] · [취소].
///
/// 이 창은 **활성화되지 않는다** (WS_EX_NOACTIVATE + WM_MOUSEACTIVATE → MA_NOACTIVATE). 그래서 사용자가 넣을 칸을 클릭한 뒤
/// 칩의 [입력]을 눌러도 그 칸의 프로그램이 활성 창으로 남고 커서도 그 칸에 그대로 있다. 버튼은 WS_TABSTOP 이 없어 누를 때
/// SetFocus 도 하지 않는다. 작업 표시줄과 Alt+Tab 에는 나오지 않는다(WS_EX_TOOLWINDOW, 소유 창 없음).
///
/// 칩은 아무것도 직접 하지 않는다. 버튼을 누르면 본창에 WM_CHIP_COMMAND 를 **보내 두기만(Post)** 하고, 대상 확인·입력·정리는
/// 본창이 한다. (버튼 알림을 처리하는 도중에 칩을 부수지 않기 위해서다.)
/// 비밀번호·길이는 칩 어디에도 없다. 이름과 안내 글자는 STATIC 이라 화면 읽기 프로그램이 읽는다.
/// 검증 하네스: 클래스 "OneKeyChip", 버튼 id 11 = 입력, 12 = 취소, 안내 STATIC id 14.
/// </summary>
internal static unsafe class Chip
{
    public const string ClassName = "OneKeyChip";
    public const int IdInput = 11, IdCancel = 12, IdName = 13, IdHint = 14;
    private const int W = 520, H = 74, Pad = 14, BtnW = 60, BtnH = 32, Gap = 8;   // 안내는 두 줄까지(2026-10-01: 잘려서 뒤가 안 보임)

    private static bool _registered;
    private static nint _notify;          // 본창
    private static int _gen;              // 이 칩의 대기 세대 (본창이 준다). 늦게 도착한 이전 칩의 명령을 가려낸다.
    private static Injector.Request _pressed; private static int _pressedGen = -1;   // [입력]을 누른 그 순간의 대상 (V35-3)

    /// <summary>[입력]을 누른 순간 잡아 둔 대상을 꺼낸다. 세대가 다르거나(이전 칩) 이미 꺼냈으면 false.</summary>
    public static bool TakePressed(int gen, out Injector.Request target)
    {
        target = _pressed;
        bool ok = _pressedGen == gen;
        _pressedGen = -1; _pressed = default;
        return ok;
    }
    private static nint _hint;            // 안내 STATIC
    private static bool _hintWarn;        // 안내를 경고색으로
    // 눈에 띄게(2026-10-03 사용자: 어두운 화면에서 칩이 잘 안 보임 → 숨쉬듯 밝아졌다 어두워지는 강조색 테두리).
    // 칩은 사용자의 다음 동작(칸 클릭 → [연결]/[입력])을 기다린다는 신호라, Windows "애니메이션 효과"가 꺼져 있어도 숨쉬기를 한다
    // (2026-10-03 사용자 결정: 칩만 예외. 다른 움직임 — 토스트·화면 전환 — 은 그 설정을 그대로 따른다, Codex QA-06).
    // 움직임을 줄이려고 주기를 길게(1.6초), 밝기 변화만(크기·위치 변화 없음) 둔다.
    private const nuint TimerGlow = 1;
    private const int GlowMs = 33, GlowPeriodMs = 1800;
    private static long _glowStart;
    private static int _glowThick = 3, _glowRadius = 8;
    private static bool _glowAnim;

    // 단계 칩(2026-10-03 사용자: 아이디 연결 → 그 줄은 [연결 완료]로 바뀌고 아래에 비밀번호 줄이 생겨 절차가 보이게).
    // 줄마다 설명(STATIC) + 버튼. 지금 차례 줄의 버튼만 id 11(IdInput, 하네스·본창 규약 그대로), 끝난 줄은 id 40+k·비활성·"연결 완료".
    private const int StepTop = 38, StepRowH = 36, StepBtnW = 92, StepBottom = 10;
    private static bool _steps;
    private static readonly List<(nint Label, nint Button)> _rows = new();
    private static readonly List<bool> _rowDone = new();
    private static uint _dpi = 96;
    private static int _w;
    // B 4단계(확정안 2장 6): 제목 앞 "1Key" 꼬리표(창 바탕에 그림), 끝난 단계 줄 앞 ✓(STATIC id 50+k, 초록)
    private const string TagText = "1Key";
    private static Native.RECT _tag;
    private static readonly List<nint> _checks = new();

    /// <summary>칩을 만든다(아직 보이지 않음). work = 칩을 띄울 모니터의 작업 영역. 실패하면 0.</summary>
    /// <summary>confirmText: 확정 버튼 글(기본 [입력], 사이트 채우기의 입력란 연결은 [연결]).</summary>
    public static nint Create(nint notify, int gen, string name, string hint, uint dpi, Native.RECT work, string? confirmText = null, bool steps = false)
    {
        nint hInst = Native.GetModuleHandleW(null);
        if (!_registered)
        {
            Ctl.RegisterClass(hInst, ClassName, &WndProc, 0);
            _registered = true;
        }
        int S(int v) => (int)((long)v * dpi / 96);
        _dpi = dpi;
        _steps = steps;
        _rows.Clear(); _rowDone.Clear(); _checks.Clear();
        int w = Math.Min(S(W), work.right - work.left), h = steps ? S(StepTop + StepRowH + StepBottom) : S(H);
        _w = w;
        int x = work.left + (work.right - work.left - w) / 2, y = work.top + S(12);

        nint hwnd;
        fixed (char* cls = ClassName) fixed (char* cap = T.ChipWindowTitle)
            hwnd = Native.CreateWindowExW(Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST,
                cls, cap, Native.WS_POPUP | Native.WS_CLIPCHILDREN, x, y, w, h, 0, 0, hInst, 0);
        if (hwnd == 0) return 0;
        _notify = notify;
        _gen = gen; _pressedGen = -1;
        _hintWarn = false;

        uint corner = (uint)Native.DWMWCP_ROUND; Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, &corner, sizeof(uint));
        // Windows 11 의 창 테두리(DWM, 1px)는 창 안쪽 가장자리 위에 덧그려진다: 1px 로 줄인 숨쉬는 줄이 그 아래에 완전히 가려졌다
        // (0.2.130–0.2.132 사용자: 숨쉬기가 전혀 안 보임). DWM 테두리를 그리지 않게(DWMWA_COLOR_NONE) 하고 우리 줄이 맨 바깥이 되게 한다.
        uint border = 0xFFFFFFFE /* DWMWA_COLOR_NONE */; Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_BORDER_COLOR, &border, sizeof(uint));
        _glowThick = GlowThickness(dpi);
        _glowRadius = S(8);   // Windows 11 둥근 창 모서리(DWMWCP_ROUND)의 반지름과 맞춘다
        _glowStart = Environment.TickCount64;
        _glowAnim = true;   // 칩만 예외(사용자 결정 2026-10-03)
        if (_glowAnim) Native.SetTimer(hwnd, TimerGlow, GlowMs, 0);

        // 제목 앞 "1Key" 꼬리표: 어느 프로그램의 칩인지 바로 보이게(시안 .tag1). 이름은 그 오른쪽부터.
        nint mdc = Native.GetDC(0); nint mo = Native.SelectObject(mdc, Theme.FontSmallStrong);
        int tagW = Native.TextWidth(mdc, TagText) + S(14);
        Native.SelectObject(mdc, mo); Native.ReleaseDC(0, mdc);
        int tagY = steps ? S(11) : S(10), tagH = S(18);
        _tag = new Native.RECT { left = S(Pad), top = tagY, right = S(Pad) + tagW, bottom = tagY + tagH };
        int nameX = _tag.right + S(8);

        if (steps)
        {
            // 단계 칩: 제목 줄(이름 + [취소]) 아래에 단계 줄. 첫 줄의 설명은 hint, 버튼은 [연결](IdInput)
            Child(hwnd, "STATIC", name, Native.SS_LEFT | Native.SS_NOPREFIX | Native.SS_ENDELLIPSIS, nameX, S(10), w - nameX - S(Pad) - S(BtnW) - S(Gap), S(22), IdName, Theme.FontStrong);
            Child(hwnd, Btn.ClassName, T.CommonCancel, Btn.Bordered | Btn.OnCard, w - S(Pad) - S(BtnW), S(6), S(BtnW), S(28), IdCancel, Theme.FontBody);
            AddRow(hwnd, hint, confirmText ?? T.CommonInput);
            _hint = _rows[0].Label;
            return hwnd;
        }
        int textW = w - S(Pad) * 2 - S(BtnW) * 2 - S(Gap) - S(12);
        Child(hwnd, "STATIC", name, Native.SS_LEFT | Native.SS_NOPREFIX | Native.SS_ENDELLIPSIS, nameX, S(9), textW - (nameX - S(Pad)), S(22), IdName, Theme.FontStrong);
        _hint = Child(hwnd, "STATIC", hint, Native.SS_LEFT | Native.SS_NOPREFIX | 0x2000 /* SS_EDITCONTROL: 줄 바꿈 */, S(Pad), S(31), textW, S(36), IdHint, Theme.FontSmall);
        int by = (h - S(BtnH)) / 2, bx = w - S(Pad) - S(BtnW);
        Child(hwnd, Btn.ClassName, T.CommonCancel, Btn.Bordered | Btn.OnCard, bx, by, S(BtnW), S(BtnH), IdCancel, Theme.FontBody);
        bx -= S(BtnW) + S(Gap);
        Child(hwnd, Btn.ClassName, confirmText ?? T.CommonInput, Btn.Prominent | Btn.OnCard, bx, by, S(BtnW), S(BtnH), IdInput, Theme.FontBody);
        return hwnd;
    }

    private static int Sd(int v) => (int)((long)v * _dpi / 96);

    private static void AddRow(nint hwnd, string text, string button)
    {
        int k = _rows.Count, y = Sd(StepTop) + k * Sd(StepRowH);
        nint label = Child(hwnd, "STATIC", text, Native.SS_LEFT | Native.SS_NOPREFIX | Native.SS_ENDELLIPSIS | 0x0200 /* SS_CENTERIMAGE: 세로 가운데 */,
                           Sd(Pad), y, _w - Sd(Pad) * 2 - Sd(StepBtnW) - Sd(Gap), Sd(StepRowH - 6), 30 + k, Theme.FontBody);
        nint btn = Child(hwnd, Btn.ClassName, button, Btn.Prominent | Btn.OnCard, _w - Sd(Pad) - Sd(StepBtnW), y, Sd(StepBtnW), Sd(StepRowH - 6), IdInput, Theme.FontBody);
        _rows.Add((label, btn));
        _rowDone.Add(false);
    }

    /// <summary>단계 칩: 지금 차례 줄을 끝낸다(설명은 doneText, 버튼은 "연결 완료"·비활성·id 40+k). 단계 칩이 아니면 아무것도 하지 않는다.</summary>
    public static void CompleteStep(nint chip, string doneText)
    {
        if (chip == 0 || !_steps || _rows.Count == 0) return;
        int k = _rows.Count - 1;
        if (_rowDone[k]) return;
        var (label, btn) = _rows[k];
        _rowDone[k] = true;
        _hintWarn = false;
        // 끝난 줄: 앞에 초록 ✓(시안 .step.done), 설명은 옅게. ✓ 자리만큼 설명을 오른쪽으로.
        Native.GetWindowRect(label, out Native.RECT lr);
        var lt = new Native.POINT { x = lr.left, y = lr.top }; Native.ScreenToClient(chip, ref lt);
        int cw = Sd(18);
        nint check = Child(chip, "STATIC", "\u2713", Native.SS_LEFT | Native.SS_NOPREFIX | 0x0200 /* SS_CENTERIMAGE */, lt.x, lt.y, cw, lr.bottom - lr.top, 50 + k, Theme.FontStrong);
        if (check != 0) _checks.Add(check);
        Native.SetWindowPos(label, 0, lt.x + cw, lt.y, lr.right - lr.left - cw, lr.bottom - lr.top, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        Native.SetText(label, doneText);
        Native.SetWindowLongPtrW(btn, -12 /* GWLP_ID */, 40 + k);   // 지금 차례 버튼만 IdInput
        Native.SetText(btn, T.ChipStepDone);
        Native.EnableWindow(btn, false);
        Native.InvalidateRect(label, 0, true); Native.InvalidateRect(btn, 0, true);
    }

    /// <summary>단계 칩: 아래에 새 줄(설명 + [연결])을 더하고 칩을 그만큼 늘린다. 새 줄이 지금 차례다.</summary>
    public static void AddStep(nint chip, string text, string button)
    {
        if (chip == 0 || !_steps) return;
        Native.GetWindowRect(chip, out Native.RECT r);
        Native.SetWindowPos(chip, 0, 0, 0, r.right - r.left, r.bottom - r.top + Sd(StepRowH), Native.SWP_NOMOVE_ | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        AddRow(chip, text, button);
        _hint = _rows[^1].Label;
        _hintWarn = false;
        Native.InvalidateRect(chip, 0, true);
    }

    /// <summary>안내 글자를 바꾼다. warn 이면 경고색.</summary>
    public static void SetHint(nint chip, string text, bool warn)
    {
        if (chip == 0 || _hint == 0) return;
        _hintWarn = warn;
        Native.SetText(_hint, text);
        Native.InvalidateRect(_hint, 0, true);
    }

    /// <summary>숨쉬는 테두리 굵기: 3px 의 1/4(2026-10-03 사용자: 절반으로, 다시 절반으로). 100%·150% 배율 1px, 200% 2px. 알림(Toast)도 같다.</summary>
    internal static int GlowThickness(uint dpi) => Math.Max(1, (int)Math.Round(3.0 * dpi / 96 / 4));

    /// <summary>숨쉬기 밝기 0~1(1.8초 주기, 양 끝에서 천천히). start 부터 잰다. 알림(Toast)도 같이 쓴다.</summary>
    internal static double Breath(long start, int periodMs = GlowPeriodMs)
    {
        double x = 0.5 + 0.5 * Math.Sin(2 * Math.PI * ((Environment.TickCount64 - start) % periodMs) / periodMs);
        return x * x * (3 - 2 * x);
    }

    /// <summary>테두리 색: level 0(옅음) ~ 1(가장 밝음). 어두운 화면에서는 강조색을 더 밝혀 바탕과 차이를 키운다.</summary>
    internal static uint GlowColor(double level, uint baseColor, double lo, double hi)
    {
        uint bright = Theme.IsDark ? Theme.Mix(Theme.AccentInk, 0xFFFFFF, 0.15) : Theme.AccentInk;
        return Theme.Mix(baseColor, bright, lo + (hi - lo) * level);
    }
    // 칩: 굵을 때(2px 이상)는 강조색의 30–75%(2026-10-03 사용자: 너무 밝음), 1px 일 때는 바탕에 가까운 색(10%)에서 강조색 75%까지 오가야 숨쉬기가 보인다(사용자 결정 10%→75%)
    // (0.2.130 사용자: 1px 로 줄인 뒤 숨쉬기가 안 보임). 바깥 DWM 테두리는 그리지 않는다(위 Create 참고).
    private static uint GlowColor(double level) => _glowThick <= 1 ? GlowColor(level, Theme.BorderColor, 0.10, 0.75) : GlowColor(level, Theme.BorderColor, 0.30, 0.75);
    private static double GlowLevel()
    {
        if (!_glowAnim) return 1;
        return Breath(_glowStart);
    }

    private static nint Child(nint parent, string cls, string text, uint style, int x, int y, int w, int h, int id, nint font)
    {
        nint c;
        fixed (char* pc = cls) fixed (char* pt = text)
            c = Native.CreateWindowExW(0, pc, pt, style | Native.WS_CHILD | Native.WS_VISIBLE, x, y, w, h, parent, id, Native.GetModuleHandleW(null), 0);
        if (c != 0) { Native.SendMessageW(c, Native.WM_SETFONT, font, 1); if (cls == "STATIC") DwStatic.Attach(c); }
        return c;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            switch (msg)
            {
                case Native.WM_MOUSEACTIVATE:
                    return Native.MA_NOACTIVATE;   // 눌러도 활성 창을 빼앗지 않는다 (자식 버튼의 클릭도 여기로 올라온다)
                case Native.WM_COMMAND:
                {
                    int id = (int)(wParam & 0xFFFF);
                    if (id is IdInput or IdCancel && _notify != 0)
                    {
                        // 대상은 누른 **이 순간** 잡는다 (Codex V35-3). 창 정리와 입력만 본창이 메시지를 받은 뒤에 한다.
                        if (id == IdInput) { _pressed = Injector.Capture(); _pressedGen = _gen; Injector.TestHoldPoint("chipqueued"); }
                        Native.PostMessageW(_notify, Native.WM_CHIP_COMMAND, id == IdInput ? 1 : 0, _gen);
                    }
                    return 0;
                }
                case Native.WM_CLOSE:
                    if (_notify != 0) Native.PostMessageW(_notify, Native.WM_CHIP_COMMAND, 0, _gen);
                    return 0;
                case Native.WM_CTLCOLORSTATIC:
                    Native.SetBkColor(wParam, Theme.CardBg);
                    bool doneRow = _steps && _rows.FindIndex(r => r.Label == lParam) is int ri && ri >= 0 && _rowDone[ri];
                    Native.SetTextColor(wParam, _checks.Contains(lParam) ? Theme.DoneInk
                                              : lParam == _hint && _hintWarn ? Theme.DangerText
                                              : doneRow ? Theme.SecondaryText
                                              : _steps ? Theme.ControlText
                                              : lParam == _hint ? Theme.SecondaryText : Theme.ControlText);
                    return Theme.CardBrush;
                case Native.WM_ERASEBKGND:
                {
                    Native.GetClientRect(hwnd, out Native.RECT rc);
                    // 강조색 테두리: 바탕 전체를 테두리 색으로 칠한 뒤 안쪽을 둥근 카드로 덮는다(바깥은 창의 둥근 모서리, 안쪽도 둥글게
                    // — 2026-10-03 사용자: 네모로 그린 안쪽 모서리가 각지게 잘려 보임). 자식(글·버튼)은 WS_CLIPCHILDREN 이라 덮이지 않는다.
                    // 끊김·깜빡임을 줄이려고 메모리에 먼저 그려 한 번에 옮긴다.
                    int cw = rc.right - rc.left, ch = rc.bottom - rc.top;
                    nint mem = Native.CreateCompatibleDC(wParam), bmp = Native.CreateCompatibleBitmap(wParam, cw, ch), old = Native.SelectObject(mem, bmp);
                    try
                    {
                        nint br = Native.CreateSolidBrush(GlowColor(GlowLevel()));
                        try { Native.FillRect(mem, ref rc, br); }
                        finally { Native.DeleteObject(br); }
                        int t = _glowThick;
                        Gdiplus.FillRoundRect(mem, t, t, cw - 2 * t, ch - 2 * t, Math.Max(2, _glowRadius - t), Theme.CardBg);
                        // "1Key" 꼬리표(알약)
                        Gdiplus.FillRoundRect(mem, _tag.left, _tag.top, _tag.right - _tag.left, _tag.bottom - _tag.top, (_tag.bottom - _tag.top) / 2f, Theme.TagFill);
                        Ctl.Text(mem, Theme.FontSmallStrong, TagText, Theme.TagText, _tag.left, _tag.top, _tag.right, _tag.bottom, Native.DT_CENTER | Native.DT_VCENTER);
                        // 단계 줄 사이 구분선(시안 .step + .step)
                        if (_steps)
                            for (int k = 1; k < _rows.Count; k++)
                            {
                                int ly = Sd(StepTop) + k * Sd(StepRowH) - Sd(3);
                                Gdiplus.DrawLine(mem, Sd(Pad), ly, cw - Sd(Pad), ly, Theme.Mix(Theme.CardBg, Theme.ControlText, 0.10), Math.Max(1, Sd(1)));
                            }
                        Native.BitBlt(wParam, 0, 0, cw, ch, mem, 0, 0, 0x00CC0020 /* SRCCOPY */);
                    }
                    finally { Native.SelectObject(mem, old); Native.DeleteObject(bmp); Native.DeleteDC(mem); }
                    return 1;
                }
                case Native.WM_TIMER:
                    if (wParam == (nint)TimerGlow)
                    {
                        Native.InvalidateRect(hwnd, 0, true);   // 바깥 DWM 테두리 색은 고정(원격 화면에서 아래가 두꺼워 보이던 것, 2026-10-03)
                        return 0;
                    }
                    break;
                case Native.WM_NCDESTROY:
                    Native.KillTimer(hwnd, TimerGlow);
                    _hint = 0;
                    break;
            }
        }
        catch { }
        return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
