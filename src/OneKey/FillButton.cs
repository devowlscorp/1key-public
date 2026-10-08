using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 사이트 채우기의 떠 있는 [채우기] 버튼(2026-09-30 사용자 결정: 연결한 주소와 정확히 같은 페이지에서만, 누를 때만 넣는다).
/// 연결한 입력란 오른쪽에 붙어 뜬다. 입력 칩처럼 **활성화되지 않는다**(WS_EX_NOACTIVATE + MA_NOACTIVATE): 눌러도 브라우저가
/// 활성 창으로 남는다. 버튼은 아무것도 직접 하지 않고 본창에 WM_FILL_COMMAND 를 보내 두기만 한다(확인·입력은 본창이).
/// 비밀번호는 이 창 어디에도 없다. 마우스를 올리면 넣을 항목 이름이 툴팁으로 보인다.
/// 검증 하네스: 클래스 "OneKeyFill", 버튼 id 11.
/// 모양(0.3.1, 2026-10-04 사용자: 둥근 테두리가 거칠다): 창을 알약 영역(SetWindowRgn)으로 자르면 가장자리가 계단처럼 보여서,
/// 층 창(WS_EX_LAYERED + UpdateLayeredWindow)에 그린다. 알약·테두리 가장자리는 픽셀 중심의 거리로 덮임 비율을 직접 계산하고(0.3.2 —
/// 0.3.1 의 GDI+ 그림 + 바탕 차이 방식은 아직 픽셀이 보였다), 글자·아이콘만 검은/흰 바탕 차이로 투명도를 구해 얹는다. 자식 버튼(id 11)은 누르기·키보드·툴팁·접근성을 그대로 맡고,
/// 그 그림은 층 창에서 보이지 않는다 — 누름/올림 상태는 짧은 타이머로 읽어 다시 그린다.
/// </summary>
internal static unsafe class FillButton
{
    public const string ClassName = "OneKeyFill";
    public const int IdFill = 11;
    private const int W = 88, H = 28;   // B 시안 .fillbtn: 열쇠 아이콘 + "채우기", 높이 28 알약

    private static bool _registered;
    private static nint _hwnd, _notify, _tip, _btn;
    private static uint _dpi;
    private static string _names = "";
    private static int _token;   // 지금 보이는 버튼의 토큰(본창이 정함). 클릭 메시지가 싣고 간다 (R60-3)
    private static nint _drawnState = -1;   // 마지막으로 그린 자식 버튼 상태(올림·누름)
    private const nuint TimerState = 1;

    public static bool Visible => _hwnd != 0 && Native.IsWindowVisible(_hwnd);

    /// <summary>입력란 사각형(화면 물리 px) 오른쪽에 보인다. 창 안쪽이 모자라면 입력란 오른쪽 끝 안에 둔다. 이미 떠 있으면 옮기기만.</summary>
    public static void ShowAt(nint notify, Native.RECT field, Native.RECT window, string names, int token)
    {
        _token = token;
        uint dpi = WorkArea.DpiFor(notify);
        if (_hwnd == 0 || dpi != _dpi || names != _names) { Destroy(); Create(notify, dpi, names); }
        if (_hwnd == 0) return;
        int S(int v) => (int)((long)v * _dpi / 96);
        int w = S(W), h = S(H);
        int x = field.right + S(6), y = field.top + (field.bottom - field.top - h) / 2;
        if (x + w > window.right - S(4)) x = field.right - w - S(4);   // 창 밖으로 나가면 입력란 안쪽 오른쪽에
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE_ | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        _drawnState = -1; Render();
        Native.SetTimer(_hwnd, TimerState, 40, 0);
    }

    public static void Hide()
    {
        _token = 0;
        if (_hwnd != 0) { Native.KillTimer(_hwnd, TimerState); Native.ShowWindow(_hwnd, Native.SW_HIDE); }
    }

    public static void Destroy()
    {
        nint h = _hwnd;
        _hwnd = 0; _btn = 0;
        if (_tip != 0) { Native.DestroyWindow(_tip); _tip = 0; }
        if (h != 0) Native.DestroyWindow(h);
    }

    private static void Create(nint notify, uint dpi, string names)
    {
        nint inst = Native.GetModuleHandleW(null);
        if (!_registered) { Ctl.RegisterClass(inst, ClassName, &WndProc, 0); _registered = true; }
        _notify = notify; _dpi = dpi; _names = names;
        int S(int v) => (int)((long)v * dpi / 96);
        fixed (char* cls = ClassName) fixed (char* cap = "1Key")
            _hwnd = Native.CreateWindowExW(Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST | WS_EX_LAYERED, cls, cap,
                Native.WS_POPUP | Native.WS_CLIPCHILDREN, 0, 0, S(W), S(H), 0, 0, inst, 0);
        if (_hwnd == 0) return;
        // 알약 바깥은 투명(층 창의 픽셀 알파). DWM 둥근 모서리는 끈다.
        uint corner = 1 /* DWMWCP_DONOTROUND */; Native.DwmSetWindowAttribute(_hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, &corner, sizeof(uint));
        fixed (char* bc = Btn.ClassName) fixed (char* bt = T.FillButton)
            _btn = Native.CreateWindowExW(0, bc, bt, Btn.Fill | Native.WS_CHILD | Native.WS_VISIBLE, 0, 0, S(W), S(H), _hwnd, IdFill, inst, 0);
        if (_btn != 0) Native.SendMessageW(_btn, Native.WM_SETFONT, Theme.FontSmallStrong, 1);
        // 툴팁: 넣을 항목 이름(비밀번호는 없다)
        fixed (char* tc = "tooltips_class32")
            _tip = Native.CreateWindowExW(Native.WS_EX_TOPMOST, tc, null, Native.WS_POPUP | Native.TTS_ALWAYSTIP | Native.TTS_NOPREFIX, 0, 0, 0, 0, _hwnd, 0, inst, 0);
        if (_tip != 0 && _btn != 0)
        {
            Theme.ApplyControl(_tip, "TOOLTIP");
            string text = names;   // 툴팁 글 전체(App 이 Enter 여부까지 넣어 만든다)
            fixed (char* p = text)
            {
                var ti = new Native.TOOLINFOW { cbSize = (uint)sizeof(Native.TOOLINFOW), uFlags = Native.TTF_IDISHWND | Native.TTF_SUBCLASS, hwnd = _hwnd, uId = (nuint)_btn, lpszText = p };
                Native.SendMessageTI(_tip, Native.TTM_ADDTOOLW, 0, ref ti);
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            switch (msg)
            {
                case Native.WM_MOUSEACTIVATE:
                    return Native.MA_NOACTIVATE;
                case Native.WM_COMMAND:
                    if ((int)(wParam & 0xFFFF) == IdFill && _notify != 0 && _token != 0) Native.PostMessageW(_notify, Native.WM_FILL_COMMAND, _token, 0);
                    return 0;
                case Native.WM_ERASEBKGND:
                    return 1;
                case Native.WM_TIMER:
                    if (wParam == (nint)TimerState && _btn != 0 && (Ctl.State(_btn) & (Ctl.StHot | Ctl.StPressed)) != _drawnState) Render();
                    return 0;
            }
        }
        catch { }
        return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    /// <summary>알약을 층 창에 그린다: 검은 바탕·흰 바탕 두 번 그려 픽셀마다 투명도와 (미리 곱한) 색을 구해 UpdateLayeredWindow.</summary>
    private static void Render()
    {
        if (_hwnd == 0) return;
        int w = (int)((long)W * _dpi / 96), h = (int)((long)H * _dpi / 96);
        nint st = _btn != 0 ? Ctl.State(_btn) & (Ctl.StHot | Ctl.StPressed) : 0;
        nint screen = Native.GetDC(0);
        nint dcB = Fx.CreateCompatibleDC(screen), dcW = Fx.CreateCompatibleDC(screen), dcOut = Fx.CreateCompatibleDC(screen);
        var bih = new Fx.BITMAPINFOHEADER { biSize = (uint)sizeof(Fx.BITMAPINFOHEADER), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        nint bmB = Fx.CreateDIBSection(screen, ref bih, 0, out nint pB, 0, 0);
        nint bmW = Fx.CreateDIBSection(screen, ref bih, 0, out nint pW, 0, 0);
        nint bmO = Fx.CreateDIBSection(screen, ref bih, 0, out nint pO, 0, 0);
        try
        {
            if (dcB == 0 || dcW == 0 || dcOut == 0 || bmB == 0 || bmW == 0 || bmO == 0) return;
            nint oB = Native.SelectObject(dcB, bmB), oW = Native.SelectObject(dcW, bmW), oO = Native.SelectObject(dcOut, bmO);
            // 글자·아이콘만 검은/흰 바탕에 그려 글자의 투명도를 얻는다(흰 글자라 색은 늘 흰색)
            DrawLabel(dcB, w, h, 0x000000);
            DrawLabel(dcW, w, h, 0xFFFFFF);
            uint* b = (uint*)pB, wh = (uint*)pW, o = (uint*)pO;
            // 알약·테두리는 픽셀 중심에서 경계까지의 거리로 직접 덮임 비율을 구한다(1px 부드러운 가장자리 — GDI+ 자르기보다 고르다)
            uint fill = Theme.FillPill;
            if ((st & Ctl.StPressed) != 0) fill = Theme.Mix(fill, 0x000000, 0.18); else if ((st & Ctl.StHot) != 0) fill = Theme.Mix(fill, 0xFFFFFF, 0.10);
            uint ring = Theme.FillRing;
            float rad = h / 2f, lw = Math.Max(1f, _dpi / 96f), x1 = rad, x2 = w - rad;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float cx = Math.Clamp(px, x1, x2);
                    float d = MathF.Sqrt((px - cx) * (px - cx) + (py - rad) * (py - rad)) - rad;   // 0 = 경계, 음수 = 안쪽
                    float cov = Math.Clamp(0.5f - d, 0f, 1f);
                    float inner = Math.Clamp(0.5f - (d + lw), 0f, 1f);                             // 테두리 안쪽(채움)
                    float ringPart = cov - inner;
                    float r0 = (fill & 0xFF) * inner + (ring & 0xFF) * ringPart;                   // COLORREF: R 이 아래 바이트
                    float g0 = ((fill >> 8) & 0xFF) * inner + ((ring >> 8) & 0xFF) * ringPart;
                    float b0 = ((fill >> 16) & 0xFF) * inner + ((ring >> 16) & 0xFF) * ringPart;
                    float a0 = cov * 255f;
                    // 글자(흰색) 투명도: 흰 바탕 − 검은 바탕 = 255 × (1 − 알파)
                    int i = y * w + x;
                    uint cb = b[i], cw = wh[i];
                    int diff = ((int)((cw >> 16) & 0xFF) - (int)((cb >> 16) & 0xFF) + (int)((cw >> 8) & 0xFF) - (int)((cb >> 8) & 0xFF) + (int)(cw & 0xFF) - (int)(cb & 0xFF)) / 3;
                    float ta = Math.Clamp(255 - diff, 0, 255) / 255f * cov;                         // 알약 밖으로는 글자를 내지 않는다
                    float R = r0 * (1 - ta) + 255f * ta, G = g0 * (1 - ta) + 255f * ta, B = b0 * (1 - ta) + 255f * ta;   // 미리 곱한 값
                    float A = a0;
                    o[i] = ((uint)Math.Round(A) << 24) | ((uint)Math.Round(Math.Min(R, A)) << 16) | ((uint)Math.Round(Math.Min(G, A)) << 8) | (uint)Math.Round(Math.Min(B, A));
                }
            Native.GetWindowRect(_hwnd, out Native.RECT wr);
            var dst = new Native.POINT { x = wr.left, y = wr.top };
            var size = new SIZE { cx = w, cy = h };
            var src = new Native.POINT();
            var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 /* AC_SRC_ALPHA */ };
            UpdateLayeredWindow(_hwnd, screen, ref dst, ref size, dcOut, ref src, 0, ref blend, 2 /* ULW_ALPHA */);
            _drawnState = st;
            Native.SelectObject(dcB, oB); Native.SelectObject(dcW, oW); Native.SelectObject(dcOut, oO);
        }
        finally
        {
            if (bmB != 0) Native.DeleteObject(bmB); if (bmW != 0) Native.DeleteObject(bmW); if (bmO != 0) Native.DeleteObject(bmO);
            if (dcB != 0) Fx.DeleteDC(dcB); if (dcW != 0) Fx.DeleteDC(dcW); if (dcOut != 0) Fx.DeleteDC(dcOut);
            Native.ReleaseDC(0, screen);
        }
    }

    /// <summary>열쇠 아이콘 + "채우기"(흰 글자)만 바탕 bg 위에 그린다. 알약 자체는 Render 가 거리로 계산한다(B 시안 .fillbtn).</summary>
    private static void DrawLabel(nint dc, int w, int h, uint bg)
    {
        var all = new Native.RECT { left = 0, top = 0, right = w, bottom = h };
        nint br = Native.CreateSolidBrush(bg); Native.FillRect(dc, ref all, br); Native.DeleteObject(br);
        int iw = (int)((long)16 * _dpi / 96), gap = (int)((long)5 * _dpi / 96);
        string label = T.FillButton;
        nint font = Theme.FontSmallStrong;
        nint o = Native.SelectObject(dc, font); int tw = Native.TextWidth(dc, label); Native.SelectObject(dc, o);
        int x0 = Math.Max(0, (w - iw - gap - tw) / 2);
        Native.SetBkMode(dc, 1 /* TRANSPARENT */);
        Ctl.Text(dc, Theme.FontIcon, "\uE8D7", 0xFFFFFF, x0, 0, x0 + iw, h, Native.DT_CENTER | Native.DT_VCENTER);   // 열쇠(Segoe Fluent Icons)
        Ctl.Text(dc, font, label, 0xFFFFFF, x0 + iw + gap, 0, w, h, Native.DT_LEFT | Native.DT_VCENTER);
    }

    private const uint WS_EX_LAYERED = 0x00080000;
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(nint hwnd, nint hdcDst, ref Native.POINT pptDst, ref SIZE psize, nint hdcSrc, ref Native.POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);
}
