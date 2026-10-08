using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 앱 스크롤 막대(2026-09-30 사용자 결정: 모든 스크롤 막대는 Windows 기본 모양이 아니라 앱 디자인으로).
/// 본창 본문·목록의 막대와 같은 모양이다: 오른쪽 가장자리의 가는 트랙(연하게)과 둥근 손잡이(진하게).
/// 여러 줄 읽기 전용 EDIT(긴 안내·라이선스 전문)에 붙인다. EDIT 는 WS_VSCROLL 없이 만들고, 오른쪽 여백(EM_SETMARGINS)에
/// 막대를 직접 그린다. 휠·손잡이 끌기·트랙 누르기(한 화면씩)·키보드(캐럿 이동)로 움직이며, 글 선택·복사는 EDIT 그대로다.
/// </summary>
internal static unsafe class ThinScroll
{
    /// <summary>막대가 차지하는 오른쪽 여백(논리 px). 글자는 이 안쪽에서 줄 바꿈된다.</summary>
    public const int Gutter = 14;
    private const int BarW = 4, Inset = 4, MinThumb = 28;

    private sealed class State
    {
        public uint Dpi = 96, Bg;
        public int LastFirst = -1, DragOffset = -1;
        public bool Hover;
    }
    private static readonly Dictionary<nint, State> _state = new();

    /// <summary>EDIT 에 막대를 붙인다. bg: EDIT 바탕색(막대 트랙을 그 위에 섞는다). dpi: 그 창의 배율.</summary>
    public static void Attach(nint edit, uint bg, uint dpi)
    {
        if (edit == 0) return;
        _state[edit] = new State { Dpi = dpi == 0 ? 96 : dpi, Bg = bg };
        Native.SendMessageW(edit, Native.EM_SETMARGINS, Native.EC_RIGHTMARGIN, (nint)(S(dpi, Gutter) << 16));
        Native.SetWindowSubclass(edit, &Proc, 1, 0);
    }

    private static int S(uint dpi, int v) => (int)((long)v * dpi / 96);

    /// <summary>(첫 줄, 스크롤 최댓값). 최댓값이 0 이면 넘치지 않는다(막대 없음).</summary>
    private static (int First, int Max, int Total) Metrics(nint edit)
    {
        int total = (int)Native.SendMessageW(edit, Native.EM_GETLINECOUNT, 0, 0);
        int first = (int)Native.SendMessageW(edit, Native.EM_GETFIRSTVISIBLELINE, 0, 0);
        Native.GetClientRect(edit, out Native.RECT rc);
        int lineH = Math.Max(1, Native.TextHeight(edit, Native.SendMessageW(edit, Native.WM_GETFONT, 0, 0)));
        int visible = Math.Max(1, (rc.bottom - rc.top) / lineH);
        return (first, Math.Max(0, total - visible), total);
    }

    /// <summary>트랙과 손잡이(EDIT 클라이언트 좌표). 넘치지 않으면 false.</summary>
    private static bool Geometry(nint edit, State st, out Native.RECT track, out Native.RECT thumb, out int max, out int first)
    {
        track = thumb = default;
        (first, max, int total) = Metrics(edit);
        if (max <= 0) return false;
        Native.GetClientRect(edit, out Native.RECT rc);
        int w = Math.Max(3, S(st.Dpi, BarW)), x = rc.right - S(st.Dpi, Inset) - w;
        int top = S(st.Dpi, 2), bottom = rc.bottom - S(st.Dpi, 2);
        if (bottom - top < S(st.Dpi, 24)) return false;
        track = new Native.RECT { left = x, top = top, right = x + w, bottom = bottom };
        int trackH = bottom - top;
        int thumbH = Math.Min(trackH, Math.Max(S(st.Dpi, MinThumb), (int)((long)trackH * (total - max) / Math.Max(1, total))));
        int thumbY = top + (int)((long)(trackH - thumbH) * Math.Clamp(first, 0, max) / max);
        thumb = new Native.RECT { left = x, top = thumbY, right = x + w, bottom = thumbY + thumbH };
        return true;
    }

    /// <summary>막대 영역(여백 전체)을 다시 그리게 한다. EDIT 가 ScrollWindow 로 밀어 올린 막대 조각이 남지 않도록 늘 여백째.</summary>
    private static void InvalidateGutter(nint edit, State st)
    {
        Native.GetClientRect(edit, out Native.RECT rc);
        var g = new Native.RECT { left = rc.right - S(st.Dpi, Gutter), top = 0, right = rc.right, bottom = rc.bottom };
        Native.InvalidateRect(edit, (nint)(&g), true);
    }

    private static void Draw(nint edit, State st)
    {
        if (!Geometry(edit, st, out Native.RECT tr, out Native.RECT th, out _, out _)) return;
        nint dc = Native.GetDC(edit);
        if (dc == 0) return;
        try
        {
            int r = Math.Max(2, (tr.right - tr.left) / 2);
            double thumbMix = st.DragOffset >= 0 ? 0.55 : st.Hover ? 0.50 : 0.40;
            Gdiplus.FillRoundRect(dc, tr.left, tr.top, tr.right - tr.left, tr.bottom - tr.top, r, Theme.Mix(st.Bg, Theme.ControlText, 0.08));
            Gdiplus.FillRoundRect(dc, th.left, th.top, th.right - th.left, th.bottom - th.top, r, Theme.Mix(st.Bg, Theme.ControlText, thumbMix));
        }
        finally { Native.ReleaseDC(edit, dc); }
    }

    private static bool InGutter(nint edit, State st, int x)
    {
        Native.GetClientRect(edit, out Native.RECT rc);
        return x >= rc.right - S(st.Dpi, Gutter);
    }

    /// <summary>EDIT 의 첫 줄이 바뀔 수 있는 메시지(키보드, 선택 끌기, 스크롤 요청, 글 바꾸기, 크기).</summary>
    private static bool MayScroll(uint msg) => msg is Native.WM_KEYDOWN or Native.WM_KEYUP or Native.WM_CHAR or Native.WM_MOUSEMOVE or Native.WM_LBUTTONUP
        or Native.WM_SETTEXT or Native.EM_SETSEL or Native.EM_SCROLL or Native.EM_LINESCROLL or 0x00B7 /* EM_SCROLLCARET */ or 0x0115 /* WM_VSCROLL */ or 0x0005 /* WM_SIZE */;

    private static void ScrollTo(nint edit, int line)
    {
        (int first, int max, _) = Metrics(edit);
        line = Math.Clamp(line, 0, max);
        if (line != first) Native.SendMessageW(edit, Native.EM_LINESCROLL, 0, line - first);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint Proc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint data)
    {
        try
        {
            if (!_state.TryGetValue(hwnd, out State? st)) return Native.DefSubclassProc(hwnd, msg, wParam, lParam);
            int x = (short)(lParam & 0xFFFF), y = (short)((lParam >> 16) & 0xFFFF);
            switch (msg)
            {
                case Native.WM_MOUSEWHEEL:
                {
                    // WS_VSCROLL 이 없는 EDIT 는 휠을 스스로 처리하지 않을 수 있어 직접 한다(한 칸에 3줄, 본창 목록과 같은 느낌)
                    int delta = (short)((wParam >> 16) & 0xFFFF);
                    int step = delta / 120; if (step == 0) step = delta > 0 ? 1 : -1;
                    (int first, _, _) = Metrics(hwnd);
                    ScrollTo(hwnd, first - step * 3);
                    break;   // 아래에서 막대를 다시 그린다
                }
                case Native.WM_SETCURSOR:
                {
                    Native.GetCursorPos(out Native.POINT pt);
                    Native.ScreenToClient(hwnd, ref pt);
                    if (InGutter(hwnd, st, pt.x) || st.DragOffset >= 0) { Native.SetCursor(Native.LoadCursorW(0, 32512)); return 1; }   // 화살표(글자 I 대신)
                    return Native.DefSubclassProc(hwnd, msg, wParam, lParam);
                }
                case Native.WM_LBUTTONDOWN:
                case Native.WM_LBUTTONDBLCLK:
                {
                    if (!InGutter(hwnd, st, x)) goto default;
                    if (Geometry(hwnd, st, out _, out Native.RECT th, out _, out _))
                    {
                        if (y >= th.top && y < th.bottom) { st.DragOffset = y - th.top; Native.SetCapture(hwnd); }
                        else Native.SendMessageW(hwnd, Native.EM_SCROLL, y < th.top ? Native.SB_PAGEUP : Native.SB_PAGEDOWN, 0);
                    }
                    Native.SetFocus(hwnd);
                    break;
                }   // EDIT 에 넘기지 않는다: 여백을 눌러 글 선택이 시작되지 않도록
                case Native.WM_MOUSEMOVE:
                    if (st.DragOffset >= 0)
                    {
                        if (Geometry(hwnd, st, out Native.RECT tr, out Native.RECT th2, out int max, out _))
                        {
                            int room = (tr.bottom - tr.top) - (th2.bottom - th2.top);
                            if (room > 0) ScrollTo(hwnd, (int)Math.Round((double)(y - st.DragOffset - tr.top) * max / room));
                        }
                        break;
                    }
                    {
                        bool hover = InGutter(hwnd, st, x);
                        if (hover != st.Hover)
                        {
                            st.Hover = hover;
                            InvalidateGutter(hwnd, st);
                            if (hover) { var tme = new Native.TRACKMOUSEEVENT { cbSize = (uint)sizeof(Native.TRACKMOUSEEVENT), dwFlags = Native.TME_LEAVE, hwndTrack = hwnd }; Native.TrackMouseEvent(ref tme); }
                        }
                    }
                    goto default;
                case Native.WM_MOUSELEAVE:
                    if (st.Hover) { st.Hover = false; InvalidateGutter(hwnd, st); }
                    goto default;
                case Native.WM_LBUTTONUP:
                    if (st.DragOffset >= 0) { st.DragOffset = -1; Native.ReleaseCapture(); InvalidateGutter(hwnd, st); break; }
                    goto default;
                case Native.WM_CAPTURECHANGED:
                    if (st.DragOffset >= 0) { st.DragOffset = -1; InvalidateGutter(hwnd, st); }
                    goto default;
                case Native.WM_PAINT:
                {
                    nint r = Native.DefSubclassProc(hwnd, msg, wParam, lParam);
                    Draw(hwnd, st);
                    st.LastFirst = (int)Native.SendMessageW(hwnd, Native.EM_GETFIRSTVISIBLELINE, 0, 0);
                    return r;
                }
                case Native.WM_NCDESTROY:
                    _state.Remove(hwnd);
                    Native.RemoveWindowSubclass(hwnd, &Proc, 1);
                    return Native.DefSubclassProc(hwnd, msg, wParam, lParam);
                default:
                {
                    nint r = Native.DefSubclassProc(hwnd, msg, wParam, lParam);
                    // 키보드·선택 끌기·EM_* 로 스크롤되었으면 막대를 새 위치로. 스크롤이 바뀔 수 있는 메시지 뒤에서만 묻는다:
                    // WM_DESTROY 뒤의 EDIT 는 내부 자료를 이미 풀어, 그때 EM_* 를 보내면 comctl32/user32 안에서 죽는다(0.2.55 시험에서 발견).
                    if (MayScroll(msg) && _state.ContainsKey(hwnd) && Native.IsWindow(hwnd)
                        && (int)Native.SendMessageW(hwnd, Native.EM_GETFIRSTVISIBLELINE, 0, 0) != st.LastFirst) InvalidateGutter(hwnd, st);
                    return r;
                }
            }
            // 직접 처리한 메시지(휠·막대 누르기·끌기): 위치가 바뀌었을 수 있다
            InvalidateGutter(hwnd, st);
            return 0;
        }
        catch { return Native.DefSubclassProc(hwnd, msg, wParam, lParam); }
    }
}
