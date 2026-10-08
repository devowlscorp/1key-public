using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// iOS 스타일 토글 스위치. 창 클래스 "OneKeyToggle".
/// 체크박스와 같은 메시지(BM_SETCHECK / BM_GETCHECK)를 받고, 바뀌면 부모에 WM_COMMAND(BN_CLICKED) 를 보낸다.
/// 왼쪽에 스위치(44×22), 오른쪽에 글자. 항상 카드 배경색 위에 놓인다고 가정한다.
/// </summary>
internal static unsafe class Toggle
{
    public const string ClassName = "OneKeyToggle";
    public const int SwitchW = 44, SwitchH = 22, Gap = 8;
    /// <summary>창 스타일 하위 비트: 스위치를 오른쪽 끝에, 글자를 왼쪽에 (iOS 목록 행 방식).</summary>
    public const uint StyleTrailing = 0x0001;
    /// <summary>창 스타일 하위 비트: 아래쪽에 구분선을 그린다 (카드 안의 행일 때. 컨트롤이 행을 덮어 부모의 선이 가려지므로).</summary>
    public const uint StyleSeparator = 0x0002;

    // 창 추가 메모리: [0] 상태 비트, [8] 글꼴 핸들
    private const int OffState = 0, OffFont = 8;
    private const nint StChecked = 1, StHot = 2, StPressed = 4, StTracking = 8;

    private static bool _registered;

    public static void Register(nint hInst)
    {
        if (_registered) return;
        fixed (char* cls = ClassName)
        {
            var wc = new Native.WNDCLASSEXW
            {
                cbSize = (uint)sizeof(Native.WNDCLASSEXW),
                style = 0,
                lpfnWndProc = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&WndProc,
                cbWndExtra = 16,
                hInstance = hInst,
                hCursor = Native.LoadCursorW(0, 32512),
                hbrBackground = 0,
                lpszClassName = cls,
            };
            Native.RegisterClassExW(ref wc);
        }
        _registered = true;
    }

    private static nint State(nint h) => Native.GetWindowLongPtrW(h, OffState);
    private static void SetState(nint h, nint v) => Native.SetWindowLongPtrW(h, OffState, v);

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try { return Proc(hwnd, msg, wParam, lParam); }
        catch { return Native.DefWindowProcW(hwnd, msg, wParam, lParam); }
    }

    private static nint Proc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case Native.WM_GETDLGCODE:
                return (nint)Native.DLGC_BUTTON;

            case 0x003D:   // WM_GETOBJECT: 켬/끔 상태·기본 동작(Codex QA-06)
                if (CtlAcc.Get(hwnd, wParam, lParam) is nint acc) return acc;
                break;

            case Native.WM_NCDESTROY:
                CtlAcc.Forget(hwnd);
                break;

            case Native.WM_SETFONT:
                Native.SetWindowLongPtrW(hwnd, OffFont, wParam);
                if (lParam != 0) Native.InvalidateRect(hwnd, 0, false);
                return 0;

            case Native.WM_GETFONT:
                return Native.GetWindowLongPtrW(hwnd, OffFont);

            case Native.WM_PRINTCLIENT:
                return Ctl.PrintClient(hwnd, wParam);

            case Native.BM_SETCHECK:
            {
                nint st = State(hwnd);
                st = wParam != 0 ? (st | StChecked) : (st & ~StChecked);
                SetState(hwnd, st);
                Native.InvalidateRect(hwnd, 0, false);
                return 0;
            }

            case Native.BM_GETCHECK:
                return (State(hwnd) & StChecked) != 0 ? Native.BST_CHECKED : Native.BST_UNCHECKED;

            case Native.WM_SETTEXT:
            case Native.WM_ENABLE:
            case Native.WM_SETFOCUS:
            case Native.WM_KILLFOCUS:
            {
                nint r = Native.DefWindowProcW(hwnd, msg, wParam, lParam);
                Native.InvalidateRect(hwnd, 0, false);
                return r;
            }

            case Native.WM_MOUSEMOVE:
            {
                nint st = State(hwnd);
                if ((st & StTracking) == 0)
                {
                    var tme = new Native.TRACKMOUSEEVENT
                    {
                        cbSize = (uint)sizeof(Native.TRACKMOUSEEVENT),
                        dwFlags = Native.TME_LEAVE,
                        hwndTrack = hwnd,
                    };
                    Native.TrackMouseEvent(ref tme);
                    SetState(hwnd, st | StTracking | StHot);
                    Native.InvalidateRect(hwnd, 0, false);
                }
                return 0;
            }

            case Native.WM_MOUSELEAVE:
                SetState(hwnd, State(hwnd) & ~(StTracking | StHot));
                Native.InvalidateRect(hwnd, 0, false);
                return 0;

            case Native.WM_LBUTTONDOWN:
                if (!Native.IsWindowEnabled(hwnd) || Ctl.IsBounce()) return 0;   // 이중 눌림은 버린다 (Ctl.BounceMs)
                Native.SetFocus(hwnd);
                Native.SetCapture(hwnd);
                SetState(hwnd, State(hwnd) | StPressed);
                Native.InvalidateRect(hwnd, 0, false);
                return 0;

            case Native.WM_LBUTTONUP:
            {
                nint st = State(hwnd);
                if ((st & StPressed) == 0) return 0;
                Native.ReleaseCapture();
                SetState(hwnd, st & ~StPressed);
                Native.GetClientRect(hwnd, out Native.RECT rc);
                int x = Native.LoWord(lParam), y = Native.HiWord(lParam);
                if (x >= 0 && y >= 0 && x < rc.right && y < rc.bottom) { Ctl.MarkClick(); Flip(hwnd); }
                else Native.InvalidateRect(hwnd, 0, false);
                return 0;
            }

            case Native.WM_CAPTURECHANGED:
                SetState(hwnd, State(hwnd) & ~StPressed);
                Native.InvalidateRect(hwnd, 0, false);
                return 0;

            case Native.WM_KEYDOWN:
                if ((State(hwnd) & StPressed) != 0) return 0;   // 마우스를 누르고 있는 동안은 키로 또 바꾸지 않는다
                if (wParam == Native.VK_SPACE && Native.IsWindowEnabled(hwnd)) { Flip(hwnd); return 0; }
                break;

            case Native.WM_ERASEBKGND:
                return 1;

            case Native.WM_PAINT:
                Paint(hwnd);
                return 0;
        }
        return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private static void Flip(nint hwnd)
    {
        nint st = State(hwnd);
        SetState(hwnd, st ^ StChecked);
        Native.InvalidateRect(hwnd, 0, false);
        CtlAcc.Changed(hwnd, value: false);
        nint parent = Native.GetParent(hwnd);
        int id = Native.GetDlgCtrlID(hwnd);
        if (parent != 0)
            Native.SendMessageW(parent, Native.WM_COMMAND, (nint)((Native.BN_CLICKED << 16) | (uint)id), hwnd);
    }

    private static void Paint(nint hwnd)
    {
        if (MetalUi.On(hwnd))
        {
            // 새 디자인(설정 화면): 스위치 42×24 금속 모양. 줄 아래 구분선은 바탕 그림에 있다(이 창이 그 자리를 그대로 깐다)
            nint ms = State(hwnd);
            bool en = Native.IsWindowEnabled(hwnd);
            MetalUi.PaintToggle(hwnd, Native.GetWindowLongPtrW(hwnd, OffFont), (ms & StChecked) != 0, (ms & StHot) != 0 && en, (ms & StPressed) != 0 && en, en,
                                Ctl.HasFocusRing(hwnd), ((long)Native.GetWindowLongPtrW(hwnd, Native.GWL_STYLE) & StyleTrailing) != 0);
            return;
        }
        nint printDc = Ctl.PrintDc;   // WM_PRINTCLIENT 면 그 DC 에
        Native.PAINTSTRUCT ps = default;
        nint hdc = printDc != 0 ? printDc : Native.BeginPaint(hwnd, out ps);
        Native.GetClientRect(hwnd, out Native.RECT rc);
        int w = rc.right, h = rc.bottom;
        if (w <= 0 || h <= 0) { if (printDc == 0) Native.EndPaint(hwnd, ref ps); return; }

        // 깜박임 방지: 메모리 DC 에 그린 뒤 한 번에 복사
        nint mem = Native.CreateCompatibleDC(hdc);
        nint bmp = Native.CreateCompatibleBitmap(hdc, w, h);
        nint oldBmp = Native.SelectObject(mem, bmp);

        Native.FillRect(mem, ref rc, Theme.CardBrush);

        uint dpi = Native.GetDpiForWindow(hwnd); if (dpi == 0) dpi = 96;
        int S(int v) => (int)((long)v * dpi / 96);
        int tw = S(SwitchW), th = S(SwitchH);
        int ty = (h - th) / 2;
        bool trailing = ((long)Native.GetWindowLongPtrW(hwnd, Native.GWL_STYLE) & StyleTrailing) != 0;
        int tx = trailing ? w - tw : 0;

        nint st = State(hwnd);
        bool on = (st & StChecked) != 0;
        bool enabled = Native.IsWindowEnabled(hwnd);
        bool focus = Ctl.HasFocusRing(hwnd);
        byte alpha = enabled ? (byte)255 : (byte)110;

        uint track = on ? Theme.ToggleOn : Theme.ToggleOff;
        if ((st & (StHot | StPressed)) != 0 && enabled)
            track = Theme.Mix(track, on ? Theme.ControlText : Theme.WindowBg, 0.10);
        Gdiplus.FillRoundRect(mem, tx, ty, tw, th, th / 2f, track, alpha);

        // 손잡이: 지름 = 높이 - 4, 좌우 여백 2 (키트: 손잡이 여백 2)
        int pad = S(2);
        int kd = th - pad * 2;
        int kx = tx + (on ? (tw - pad - kd) : pad);
        Gdiplus.FillEllipse(mem, kx, ty + pad, kd, kd, 0xFFFFFF, alpha);

        if (focus)
            Gdiplus.DrawRoundRect(mem, tx - S(2), ty - S(2), tw + S(4), th + S(4), (th + S(4)) / 2f, Theme.AccentInk, S(1));

        // 글자
        nint font = Native.GetWindowLongPtrW(hwnd, OffFont);
        nint oldFont = font != 0 ? Native.SelectObject(mem, font) : 0;
        Native.SetBkMode(mem, Native.TRANSPARENT);
        Native.SetTextColor(mem, enabled ? Theme.ControlText : Theme.DisabledText);
        var tr = trailing ? new Native.RECT { left = 0, top = 0, right = w - tw - S(Gap), bottom = h }
                           : new Native.RECT { left = tw + S(Gap), top = 0, right = w, bottom = h };
        const uint fmt = Native.DT_LEFT | Native.DT_VCENTER | Native.DT_SINGLELINE | Native.DT_NOPREFIX | Native.DT_END_ELLIPSIS;
        string label = Native.GetWindowText(hwnd);
        if (!Dw.Text(mem, font, label, enabled ? Theme.ControlText : Theme.DisabledText, tr.left, tr.top, tr.right, tr.bottom, fmt))
            Native.DrawText(mem, label, ref tr, fmt);
        if (oldFont != 0) Native.SelectObject(mem, oldFont);

        if (((long)Native.GetWindowLongPtrW(hwnd, Native.GWL_STYLE) & StyleSeparator) != 0)
        {
            var sep = new Native.RECT { left = 0, top = h - Math.Max(1, S(1)), right = w, bottom = h };
            Native.FillRect(mem, ref sep, Theme.BorderBrush);
        }

        Native.BitBlt(hdc, 0, 0, w, h, mem, 0, 0, Native.SRCCOPY);
        Native.SelectObject(mem, oldBmp);
        Native.DeleteObject(bmp);
        Native.DeleteDC(mem);
        if (printDc == 0) Native.EndPaint(hwnd, ref ps);
    }
}
