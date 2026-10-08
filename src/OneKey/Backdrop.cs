using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 확인 상자·도움말처럼 앞에 뜨는 모달 뒤에서 본창을 흐리고 살짝 어둡게 덮는 막 (2026-09-29 사용자 요청: 웹 모달처럼 전경이 잘 보이게).
/// 모달이 뜨는 순간 본창 본문을 찍어 흐리게 만든 그림 한 장을 소유 팝업으로 덮는다. 모달 중에도 타이머(자동 잠금 등)는 돌기 때문에
/// 본창이 그 뒤에 바뀔 수 있다: 잠글 때는 CloseAll 로 막과 그림을 버린다(오래된 화면이 잠금 화면을 덮거나 남지 않게, Codex).
/// Windows 공식 API 만 쓴다(PrintWindow + GDI). 포커스를 가져가지 않는다. 창 클래스 OneKeyBackdrop (검증 하네스가 찾는다).
/// 토스트처럼 작업을 막지 않는 알림에는 쓰지 않는다.
/// </summary>
internal static unsafe class Backdrop
{
    public const string ClassName = "OneKeyBackdrop";
    private static bool _registered;
    private sealed class Img { public nint Dc, Bmp, Old; public int W, H; }
    private static readonly Dictionary<nint, Img> _img = new();

    /// <summary>
    /// 본창 본문을 흐려 덮는다. 보이지 않거나 찍지 못하면 0 (효과 없이 모달만 뜬다).
    /// 자원 소유(Codex R2): 그림(DC·DIB)은 창에 넘기기 전까지 이 함수가, 넘긴 뒤에는 창이 가진다(WM_NCDESTROY 에서 푼다).
    /// 창은 돌려주기 전까지 이 함수가 가진다. 어느 단계에서 실패하거나 예외가 나도 finally 가 아직 가진 것만 푼다.
    /// 검증용 실패 주입: backdrop:after-dib, backdrop:blur, backdrop:after-window, backdrop:rgn (Program.TestFails).
    /// </summary>
    public static nint Show(nint owner)
    {
        nint mem = 0, bmp = 0, old = 0, hwnd = 0, round = 0, top = 0;
        bool imgOwnedByWindow = false;
        try
        {
            if (owner == 0 || !Native.IsWindowVisible(owner) || Native.IsIconic(owner)) return 0;
            // 층 창(잠금 위젯)은 투명한 부분이 캡처에서 검게 나와 검은 사각형이 된다(2026-10-04 사용자 화면). 흐림 막 없이 확인 창만 띄운다
            if ((Native.GetWindowLongPtrW(owner, -20 /* GWL_EXSTYLE */) & 0x00080000 /* WS_EX_LAYERED */) != 0) return 0;
            if (!Native.GetClientRect(owner, out Native.RECT rc)) return 0;
            int w = rc.right - rc.left, h = rc.bottom - rc.top;
            if (w <= 0 || h <= 0) return 0;
            if (!_registered) { Ctl.RegisterClass(Native.GetModuleHandleW(null), ClassName, &WndProc, 0); _registered = true; }

            var bih = new Fx.BITMAPINFOHEADER { biSize = (uint)sizeof(Fx.BITMAPINFOHEADER), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            nint screen = Native.GetDC(0);
            mem = Fx.CreateCompatibleDC(screen);
            Native.ReleaseDC(0, screen);
            if (mem == 0) return 0;
            bmp = Fx.CreateDIBSection(mem, ref bih, 0, out nint bits, 0, 0);
            if (bmp == 0 || bits == 0) return 0;
            old = Native.SelectObject(mem, bmp);
            if (Program.TestFails("backdrop:after-dib")) throw new InvalidOperationException("test: backdrop:after-dib");
            if (!Fx.PrintWindow(owner, mem, Fx.PW_CLIENTONLY | Fx.PW_RENDERFULLCONTENT)) return 0;

            uint dpi = WorkArea.DpiFor(owner);
            int radius = Math.Max(2, (int)(6L * dpi / 96));
            uint* px = (uint*)bits;
            int passes = Fx.Transparency ? 3 : 0;   // Windows 투명 효과가 꺼져 있으면 흐리지 않고 어둡게만 (Codex QA-06)
            for (int pass = 0; pass < passes; pass++)   // 박스 흐림 3번 ≈ 가우스
            {
                BlurRows(px, w, h, radius);
                if (Program.TestFails("backdrop:blur")) throw new InvalidOperationException("test: backdrop:blur");
                BlurCols(px, w, h, radius);
            }
            Dim(px, w * h, Theme.IsDark ? 0.40 : 0.22);

            var pt = new Native.POINT();
            Native.ClientToScreen(owner, ref pt);
            fixed (char* cls = ClassName) fixed (char* cap = "")
                hwnd = Native.CreateWindowExW(Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE, cls, cap, Native.WS_POPUP,
                    pt.x, pt.y, w, h, owner, 0, Native.GetModuleHandleW(null), 0);
            if (hwnd == 0) return 0;
            _img[hwnd] = new Img { Dc = mem, Bmp = bmp, Old = old, W = w, H = h };
            imgOwnedByWindow = true;   // 이제 그림은 창이 가진다: 창이 없어질 때(WM_NCDESTROY) 푼다
            if (Program.TestFails("backdrop:after-window")) throw new InvalidOperationException("test: backdrop:after-window");

            // Windows 11 은 본창 아래 모서리가 둥글다: 막도 아래 모서리를 같게 깎아 삐져나오지 않게. 실패하면 네모난 막으로 그냥 쓴다.
            if (Environment.OSVersion.Version.Build >= 22000)
            {
                int r = (int)(8L * dpi / 96);
                round = Fx.CreateRoundRectRgn(0, 0, w + 1, h + 1, 2 * r, 2 * r);
                top = Native.CreateRectRgn(0, 0, w, h - r);
                if (round != 0 && top != 0 && Fx.CombineRgn(round, round, top, Fx.RGN_OR) != 0
                    && !Program.TestFails("backdrop:rgn") && Native.SetWindowRgn(hwnd, round, false) != 0)
                    round = 0;   // 성공하면 영역은 시스템 소유(지우면 안 된다). 실패하면 finally 가 지운다.
            }
            Native.ShowWindow(hwnd, Native.SW_SHOWNOACTIVATE);
            Fx.UpdateWindow(hwnd);
            nint result = hwnd;
            hwnd = 0;   // 호출자에게 넘긴다(Close 로 닫는다)
            return result;
        }
        catch { return 0; }
        finally
        {
            if (top != 0) Native.DeleteObject(top);
            if (round != 0) Native.DeleteObject(round);
            if (hwnd != 0) Native.DestroyWindow(hwnd);   // 넘기지 못한 창. 그림은 WM_NCDESTROY 가 푼다.
            if (!imgOwnedByWindow) FreeImage(mem, bmp, old);
        }
    }

    /// <summary>막을 닫는다. 이미 닫혔거나(잠금의 CloseAll, 소유 창 파괴) 0 이면 아무것도 하지 않는다.</summary>
    public static void Close(nint hwnd)
    {
        if (hwnd != 0 && _img.ContainsKey(hwnd)) Native.DestroyWindow(hwnd);
    }

    /// <summary>떠 있는 막을 모두 닫고 흐린 그림을 버린다. 잠글 때 쓴다(모달 뒤의 오래된 화면 그림을 남기지 않는다).</summary>
    public static void CloseAll()
    {
        foreach (nint h in _img.Keys.ToArray()) Native.DestroyWindow(h);
    }

    /// <summary>검증용: 지금 가지고 있는 흐린 그림 수(떠 있는 막 수).</summary>
    public static int LiveCount => _img.Count;

    private static void FreeImage(nint mem, nint bmp, nint old)
    {
        if (mem != 0 && old != 0) Native.SelectObject(mem, old);
        if (bmp != 0) Native.DeleteObject(bmp);
        if (mem != 0) Fx.DeleteDC(mem);
    }

    /// <summary>가로 박스 흐림(채널별 이동 합). 가장자리는 끝 픽셀을 늘려 쓴다.</summary>
    private static void BlurRows(uint* px, int w, int h, int r)
    {
        var line = new uint[w];
        int n = 2 * r + 1;
        for (int y = 0; y < h; y++)
        {
            uint* row = px + (long)y * w;
            for (int x = 0; x < w; x++) line[x] = row[x];
            int b = 0, g = 0, rr = 0;
            for (int k = -r; k <= r; k++) { uint c = line[Math.Clamp(k, 0, w - 1)]; b += (int)(c & 0xFF); g += (int)((c >> 8) & 0xFF); rr += (int)((c >> 16) & 0xFF); }
            for (int x = 0; x < w; x++)
            {
                row[x] = 0xFF000000u | ((uint)(rr / n) << 16) | ((uint)(g / n) << 8) | (uint)(b / n);
                uint cOut = line[Math.Clamp(x - r, 0, w - 1)], cIn = line[Math.Clamp(x + r + 1, 0, w - 1)];
                b += (int)(cIn & 0xFF) - (int)(cOut & 0xFF);
                g += (int)((cIn >> 8) & 0xFF) - (int)((cOut >> 8) & 0xFF);
                rr += (int)((cIn >> 16) & 0xFF) - (int)((cOut >> 16) & 0xFF);
            }
        }
    }

    private static void BlurCols(uint* px, int w, int h, int r)
    {
        var col = new uint[h];
        int n = 2 * r + 1;
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++) col[y] = px[(long)y * w + x];
            int b = 0, g = 0, rr = 0;
            for (int k = -r; k <= r; k++) { uint c = col[Math.Clamp(k, 0, h - 1)]; b += (int)(c & 0xFF); g += (int)((c >> 8) & 0xFF); rr += (int)((c >> 16) & 0xFF); }
            for (int y = 0; y < h; y++)
            {
                px[(long)y * w + x] = 0xFF000000u | ((uint)(rr / n) << 16) | ((uint)(g / n) << 8) | (uint)(b / n);
                uint cOut = col[Math.Clamp(y - r, 0, h - 1)], cIn = col[Math.Clamp(y + r + 1, 0, h - 1)];
                b += (int)(cIn & 0xFF) - (int)(cOut & 0xFF);
                g += (int)((cIn >> 8) & 0xFF) - (int)((cOut >> 8) & 0xFF);
                rr += (int)((cIn >> 16) & 0xFF) - (int)((cOut >> 16) & 0xFF);
            }
        }
    }

    /// <summary>검정 쪽으로 a 만큼 어둡게.</summary>
    private static void Dim(uint* px, int count, double a)
    {
        int k = (int)Math.Round((1 - a) * 256);
        for (int i = 0; i < count; i++)
        {
            uint c = px[i];
            uint b = (c & 0xFF) * (uint)k >> 8, g = ((c >> 8) & 0xFF) * (uint)k >> 8, r = ((c >> 16) & 0xFF) * (uint)k >> 8;
            px[i] = 0xFF000000u | (r << 16) | (g << 8) | b;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            switch (msg)
            {
                case Native.WM_MOUSEACTIVATE: return Native.MA_NOACTIVATE;
                case Native.WM_ERASEBKGND: return 1;
                case Native.WM_NCDESTROY:   // 창이 어떤 길로 없어지든(Close, CloseAll, 소유 창 파괴) 그림은 여기서 한 번만 푼다
                    if (_img.Remove(hwnd, out Img? gone)) FreeImage(gone.Dc, gone.Bmp, gone.Old);
                    break;
                case Native.WM_PAINT:
                {
                    nint hdc = Native.BeginPaint(hwnd, out Native.PAINTSTRUCT ps);
                    if (_img.TryGetValue(hwnd, out Img? img)) Fx.BitBlt(hdc, 0, 0, img.W, img.H, img.Dc, 0, 0, Fx.SRCCOPY);
                    Native.EndPaint(hwnd, ref ps);
                    return 0;
                }
            }
        }
        catch { }
        return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
