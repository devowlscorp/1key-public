using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 마스코트 인사 그림(잠금 위젯이 쓴다): 고양이가 정면 → 왼쪽 → 위 → 오른쪽 → 정면으로 둘러본다. 그림은 작업 표시줄 고양이(CatWidget)의
/// 9방향 시선 그림, 색은 Windows 테마를 따른다.
/// </summary>
internal static unsafe class MascotGreet
{
    /// <summary>16 fps — 한 장 62 ms.</summary>
    public const int FrameMs = 62;

    /// <summary>앞으로 재생한 뒤 거꾸로 되돌아오는가(고양이 둘러보기는 아님). 한 바퀴 = <see cref="Period"/> 걸음.</summary>
    public static bool PingPong { get; private set; }
    public static int Period(int frames) => PingPong && frames > 1 ? 2 * frames - 2 : frames;
    /// <summary>한 바퀴 안의 걸음 → 그림 장(0 = 서 있기).</summary>
    public static int Cell(int step, int frames)
    {
        int p = Period(frames); step = ((step % p) + p) % p;
        return step < frames ? step : 2 * frames - 2 - step;
    }

    /// <summary>인사 그림 띠(가로로 frames 장, GDI+ 32bpp ARGB 비트맵)를 만든다. 실패하면 0. 받은 쪽이 GdipDisposeImage.</summary>
    public static nint Load(out int frames) { Gdiplus.Init(); return LoadCat(out frames); }

    // 공개판 고양이의 "인사": 정면 → 왼쪽 → 왼쪽 위 → 위 → 오른쪽 위 → 오른쪽 → 정면으로 둘러본다(장마다 몇 번씩, 16 fps 로 약 2초).
    // 그림은 9방향 시선(CatWidget 과 같은 것), 색은 Windows 테마를 따른다
    private static readonly (string Name, int Repeat)[] CatLook =
        { ("center", 6), ("left", 5), ("up-left", 4), ("up", 4), ("up-right", 4), ("right", 5), ("center", 4) };

    private static nint LoadCat(out int frames)
    {
        frames = 0;
        PingPong = false;
        string th = FlipClock.WindowsLight() ? "light" : "dark";
        var imgs = new nint[CatLook.Length];
        nint dst = 0, g = 0;
        try
        {
            for (int i = 0; i < CatLook.Length; i++) if ((imgs[i] = CatWidget.LoadPng($"cat_{th}_{CatLook[i].Name}.png")) == 0) return 0;
            GdipGetImageWidth(imgs[0], out uint w); GdipGetImageHeight(imgs[0], out uint h);
            int n = 0; foreach (var c in CatLook) n += c.Repeat;
            if (w == 0 || h == 0) return 0;
            if (GdipCreateBitmapFromScan0((int)w * n, (int)h, 0, Argb, 0, out dst) != 0 || dst == 0) return 0;
            if (GdipGetImageGraphicsContext(dst, out g) != 0) return 0;
            GdipGraphicsClear(g, 0);
            int x = 0;
            for (int i = 0; i < CatLook.Length; i++)
                for (int r = 0; r < CatLook[i].Repeat; r++, x++)
                    GdipDrawImageRectI(g, imgs[i], x * (int)w, 0, (int)w, (int)h);
            GdipDeleteGraphics(g); g = 0;
            frames = n;
            nint ok = dst; dst = 0;
            return ok;
        }
        catch { return 0; }
        finally
        {
            if (g != 0) GdipDeleteGraphics(g);
            foreach (nint im in imgs) if (im != 0) GdipDisposeImage(im);
            if (dst != 0) GdipDisposeImage(dst);
        }
    }

    private const int Argb = 0x0026200A;   // PixelFormat32bppARGB
    [DllImport("gdiplus.dll")] private static extern int GdipCreateBitmapFromScan0(int w, int h, int stride, int format, nint scan0, out nint bitmap);
    [DllImport("gdiplus.dll")] private static extern int GdipGetImageWidth(nint image, out uint w);
    [DllImport("gdiplus.dll")] private static extern int GdipGetImageHeight(nint image, out uint h);
    [DllImport("gdiplus.dll")] private static extern int GdipDisposeImage(nint image);
    [DllImport("gdiplus.dll")] private static extern int GdipGetImageGraphicsContext(nint image, out nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteGraphics(nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipGraphicsClear(nint graphics, uint argb);
    [DllImport("gdiplus.dll")] private static extern int GdipDrawImageRectI(nint graphics, nint image, int x, int y, int w, int h);
}
