using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// GDI+ 평면 API 의 아주 작은 부분집합. 안티앨리어싱된 둥근 사각형과 원을 그리는 데만 쓴다.
/// (WinForms 의 System.Drawing 은 NativeAOT 에서 크기가 커서 쓰지 않는다.)
/// </summary>
internal static unsafe class Gdiplus
{
    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInput { public uint GdiplusVersion; public nint DebugEventCallback; public int SuppressBackgroundThread, SuppressExternalCodecs; }

    [DllImport("gdiplus.dll")] private static extern int GdiplusStartup(out nint token, ref StartupInput input, nint output);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateFromHDC(nint hdc, out nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteGraphics(nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipSetSmoothingMode(nint graphics, int mode);
    [DllImport("gdiplus.dll")] private static extern int GdipSetPixelOffsetMode(nint graphics, int mode);
    [DllImport("gdiplus.dll")] private static extern int GdipCreatePath(int fillMode, out nint path);
    [DllImport("gdiplus.dll")] private static extern int GdipDeletePath(nint path);
    [DllImport("gdiplus.dll")] private static extern int GdipAddPathArc(nint path, float x, float y, float w, float h, float start, float sweep);
    [DllImport("gdiplus.dll")] private static extern int GdipClosePathFigure(nint path);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateSolidFill(uint argb, out nint brush);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteBrush(nint brush);
    [DllImport("gdiplus.dll")] private static extern int GdipFillPath(nint graphics, nint brush, nint path);
    [DllImport("gdiplus.dll")] private static extern int GdipFillEllipse(nint graphics, nint brush, float x, float y, float w, float h);
    [DllImport("gdiplus.dll")] private static extern int GdipCreatePen1(uint argb, float width, int unit, out nint pen);
    [DllImport("gdiplus.dll")] private static extern int GdipDeletePen(nint pen);
    [DllImport("gdiplus.dll")] private static extern int GdipDrawPath(nint graphics, nint pen, nint path);
    [DllImport("gdiplus.dll")] private static extern int GdipDrawEllipse(nint graphics, nint pen, float x, float y, float w, float h);
    [DllImport("gdiplus.dll")] private static extern int GdipDrawLine(nint graphics, nint pen, float x1, float y1, float x2, float y2);

    private const int SmoothingModeAntiAlias = 4, PixelOffsetModeHalf = 4, UnitPixel = 2;
    private static nint _token;

    private static readonly object _initLock = new();

    /// <summary>한 번만 시작한다. 웹사이트 아이콘(WebIcon)이 작업 스레드에서도 부르므로 잠금 안에서.</summary>
    public static void Init()
    {
        if (_token != 0) return;
        lock (_initLock)
        {
            if (_token != 0) return;
            var input = new StartupInput { GdiplusVersion = 1 };
            GdiplusStartup(out nint t, ref input, 0);
            _token = t;
        }
    }

    /// <summary>COLORREF(0x00BBGGRR) + 알파 → GDI+ ARGB.</summary>
    public static uint Argb(uint colorref, byte alpha = 255)
        => ((uint)alpha << 24) | ((colorref & 0xFF) << 16) | (colorref & 0xFF00) | ((colorref >> 16) & 0xFF);

    private static nint Begin(nint hdc)
    {
        GdipCreateFromHDC(hdc, out nint g);
        GdipSetSmoothingMode(g, SmoothingModeAntiAlias);
        GdipSetPixelOffsetMode(g, PixelOffsetModeHalf);
        return g;
    }

    private static nint RoundPath(float x, float y, float w, float h, float r)
    {
        GdipCreatePath(0, out nint path);
        float d = Math.Min(r * 2, Math.Min(w, h));
        if (d <= 0)
        {
            GdipAddPathArc(path, x, y, 0.01f, 0.01f, 180, 90);
            GdipAddPathArc(path, x + w, y, 0.01f, 0.01f, 270, 90);
            GdipAddPathArc(path, x + w, y + h, 0.01f, 0.01f, 0, 90);
            GdipAddPathArc(path, x, y + h, 0.01f, 0.01f, 90, 90);
        }
        else
        {
            GdipAddPathArc(path, x, y, d, d, 180, 90);
            GdipAddPathArc(path, x + w - d, y, d, d, 270, 90);
            GdipAddPathArc(path, x + w - d, y + h - d, d, d, 0, 90);
            GdipAddPathArc(path, x, y + h - d, d, d, 90, 90);
        }
        GdipClosePathFigure(path);
        return path;
    }

    public static void FillRoundRect(nint hdc, int x, int y, int w, int h, float radius, uint colorref, byte alpha = 255)
    {
        nint g = Begin(hdc);
        nint path = RoundPath(x, y, w, h, radius);
        GdipCreateSolidFill(Argb(colorref, alpha), out nint brush);
        GdipFillPath(g, brush, path);
        GdipDeleteBrush(brush); GdipDeletePath(path); GdipDeleteGraphics(g);
    }

    public static void DrawRoundRect(nint hdc, int x, int y, int w, int h, float radius, uint colorref, float width, byte alpha = 255)
    {
        nint g = Begin(hdc);
        // 펜 굵기의 절반만큼 안쪽으로 들여 그려야 테두리가 잘리지 않는다.
        float inset = width / 2f;
        nint path = RoundPath(x + inset, y + inset, w - width, h - width, Math.Max(0, radius - inset));
        GdipCreatePen1(Argb(colorref, alpha), width, UnitPixel, out nint pen);
        GdipDrawPath(g, pen, path);
        GdipDeletePen(pen); GdipDeletePath(path); GdipDeleteGraphics(g);
    }

    public static void DrawLine(nint hdc, float x1, float y1, float x2, float y2, uint colorref, float width, byte alpha = 255)
    {
        nint g = Begin(hdc);
        GdipCreatePen1(Argb(colorref, alpha), width, UnitPixel, out nint pen);
        GdipDrawLine(g, pen, x1, y1, x2, y2);
        GdipDeletePen(pen); GdipDeleteGraphics(g);
    }

    [DllImport("gdiplus.dll")] private static extern int GdipAddPathBezier(nint path, float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4);
    [DllImport("gdiplus.dll")] private static extern int GdipAddPathLine(nint path, float x1, float y1, float x2, float y2);
    [DllImport("gdiplus.dll")] private static extern int GdipStartPathFigure(nint path);
    [DllImport("gdiplus.dll")] private static extern int GdipSetPenLineJoin(nint pen, int join);
    [DllImport("gdiplus.dll")] private static extern int GdipSetPenStartCap(nint pen, int cap);
    [DllImport("gdiplus.dll")] private static extern int GdipSetPenEndCap(nint pen, int cap);

    /// <summary>
    /// 위젯 모드 단추의 마스코트 윤곽(Codex 안 A, docs/design/widget-icon/mascot-ghost.svg — 64×64 viewBox 의 선을 그대로): 고양이 귀 둘, 머리 윤곽 하나(안쪽 얼굴 선은 뺌), 눈·입.
    /// size × size 칸에 한 색, 둥근 끝·이음.
    /// </summary>
    /// <param name="width">선 굵기(화면 픽셀). GDI+ 는 2px 보다 가는 펜을 모두 약 1px 로 그리므로, 4배 크기 비트맵에 그린 뒤 줄여서 실제로 가늘게 한다(0.3.97).</param>
    /// <param name="face">false 면 눈·입을 빼고 귀와 머리 윤곽만(작은 크기에서 덜 빽빽하게).</param>
    /// <summary>
    /// 같은 마스코트 윤곽을 이미 있는 GDI+ 그래픽에 바로 그린다(잠금 위젯의 위젯 모드 단추, 0.3.124). argb 는 미리 곱하지 않은 ARGB.
    /// 크기가 작아 선이 1px 근처면 GDI+ 가 굵게 그리므로 width 는 1.3px 이상을 권한다.
    /// </summary>
    public static void DrawMascotOutline(nint g, float x, float y, float size, uint argb, float width, bool face = false)
    {
        nint path = 0, pen = 0;
        try
        {
            float k = size / 64f;
            float X(float v) => x + v * k;
            float Y(float v) => y + v * k;
            void C(float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3) => GdipAddPathBezier(path, X(x0), Y(y0), X(x1), Y(y1), X(x2), Y(y2), X(x3), Y(y3));
            if (GdipCreatePath(0, out path) != 0) return;
            GdipStartPathFigure(path);
            C(13, 31, 13, 31, 15, 9, 15, 9); C(15, 9, 15, 9, 27, 19, 27, 19); GdipStartPathFigure(path); C(37, 19, 37, 19, 49, 9, 49, 9); C(49, 9, 49, 9, 51, 31, 51, 31);   // 고양이 귀 둘(공개판)
            GdipStartPathFigure(path);
            C(32, 19, 19, 19, 11, 27, 11, 38); C(11, 38, 11, 49, 19, 56, 32, 56); C(32, 56, 45, 56, 53, 49, 53, 38); C(53, 38, 53, 27, 45, 19, 32, 19);
            GdipClosePathFigure(path);
            if (face)
            {
                GdipStartPathFigure(path); GdipAddPathLine(path, X(25), Y(36), X(25), Y(38));
                GdipStartPathFigure(path); GdipAddPathLine(path, X(39), Y(36), X(39), Y(38));
                GdipStartPathFigure(path);
                C(28, 43, 28 + 2f / 3 * 4, 43 + 2f / 3 * 4, 36 - 2f / 3 * 4, 43 + 2f / 3 * 4, 36, 43);
            }
            if (GdipCreatePen1(argb, width, UnitPixel, out pen) != 0) return;
            GdipSetPenLineJoin(pen, 2); GdipSetPenStartCap(pen, 2); GdipSetPenEndCap(pen, 2);
            GdipDrawPath(g, pen, path);
        }
        finally
        {
            if (pen != 0) GdipDeletePen(pen);
            if (path != 0) GdipDeletePath(path);
        }
    }

    [DllImport("gdiplus.dll")] private static extern int GdipCreateBitmapFromScan0(int w, int h, int stride, int format, byte* scan0, out nint bitmap);
    [DllImport("gdiplus.dll")] private static extern int GdipDisposeImage(nint image);
    [DllImport("gdiplus.dll")] private static extern int GdipDrawImageRectI(nint graphics, nint image, int x, int y, int w, int h);
    [DllImport("gdiplus.dll")] private static extern int GdipSetInterpolationMode(nint graphics, int mode);
    [DllImport("gdiplus.dll")] private static extern int GdipGetImageGraphicsContext(nint image, out nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipGraphicsClear(nint graphics, uint argb);
    [StructLayout(LayoutKind.Sequential)] private struct ICONINFO { public int fIcon, xHotspot, yHotspot; public nint hbmMask, hbmColor; }
    [StructLayout(LayoutKind.Sequential)] private struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public nint bmBits; }
    [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFOHEADER { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant; }
    [DllImport("user32.dll")] private static extern bool GetIconInfo(nint icon, ICONINFO* info);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(nint dc, int x, int y, nint icon, int cx, int cy, uint step, nint brush, uint flags);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll")] private static extern int GetObjectW(nint h, int size, void* obj);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(nint dc, nint bmp, uint start, uint lines, void* bits, BITMAPINFOHEADER* bi, uint usage);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint h);

    /// <summary>
    /// 아이콘을 size×size 로 매끄럽게 그린다. DrawIconEx 는 크기를 바꿀 때 픽셀을 그냥 고르므로 글자 아이콘(64px)의 둥근 테두리가 계단처럼
    /// 보인다(0.3.91 사용자). 32비트 알파 아이콘이고 크기가 다르면 GDI+ 고품질 바이큐빅으로, 아니면(같은 크기·알파 없는 옛 아이콘·실패) DrawIconEx.
    /// </summary>
    public static void DrawIconSmooth(nint hdc, int x, int y, int size, nint icon)
    {
        if (!TryDrawIconSmooth(hdc, x, y, size, icon)) DrawIconEx(hdc, x, y, icon, size, size, 0, 0, 3 /* DI_NORMAL */);
    }

    private static bool TryDrawIconSmooth(nint hdc, int x, int y, int size, nint icon)
    {
        ICONINFO ii;
        if (!GetIconInfo(icon, &ii)) return false;
        nint g = 0, bmp = 0;
        try
        {
            BITMAP bm;
            if (ii.hbmColor == 0 || GetObjectW(ii.hbmColor, sizeof(BITMAP), &bm) == 0 || bm.bmBitsPixel != 32) return false;
            int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
            if (w <= 0 || h <= 0 || w > 512 || h > 512 || (w == size && h == size)) return false;
            byte[] px = new byte[w * h * 4];
            var bi = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            nint sdc = GetDC(0);
            int got;
            fixed (byte* p = px) got = GetDIBits(sdc, ii.hbmColor, 0, (uint)h, p, &bi, 0 /* DIB_RGB_COLORS */);
            ReleaseDC(0, sdc);
            if (got != h) return false;
            bool alpha = false;
            for (int i = 3; i < px.Length; i += 4) if (px[i] != 0) { alpha = true; break; }
            if (!alpha) return false;   // 마스크로 투명을 내는 옛 아이콘
            Init();
            if (GdipCreateFromHDC(hdc, out g) != 0) return false;
            GdipSetInterpolationMode(g, 7 /* HighQualityBicubic */);
            GdipSetPixelOffsetMode(g, 2 /* HighQuality */);
            fixed (byte* p = px)
            {
                if (GdipCreateBitmapFromScan0(w, h, w * 4, 0x26200A /* PixelFormat32bppARGB */, p, out bmp) != 0 || bmp == 0) return false;
                return GdipDrawImageRectI(g, bmp, x, y, size, size) == 0;
            }
        }
        catch { return false; }
        finally
        {
            if (bmp != 0) GdipDisposeImage(bmp);
            if (g != 0) GdipDeleteGraphics(g);
            if (ii.hbmColor != 0) DeleteObject(ii.hbmColor);
            if (ii.hbmMask != 0) DeleteObject(ii.hbmMask);
        }
    }

    public static void FillEllipse(nint hdc, float x, float y, float w, float h, uint colorref, byte alpha = 255)
    {
        nint g = Begin(hdc);
        GdipCreateSolidFill(Argb(colorref, alpha), out nint brush);
        GdipFillEllipse(g, brush, x, y, w, h);
        GdipDeleteBrush(brush); GdipDeleteGraphics(g);
    }
}
