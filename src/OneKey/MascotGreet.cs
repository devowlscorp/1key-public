using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 마스코트 인사 그림(잠금 화면 위젯·작업 표시줄 마스코트가 함께 씀). 0.3.121(2026-10-07 사용자: 테두리 자글자글, 눈이 하얗게 번쩍, 더 자연스럽게):
/// 영상의 모든 장면(16 fps, 약 96장)을 쓰고, 팔레트 PNG(알파 52단계 — 계단 테두리) 대신 색은 JPEG(mascot_greet.jpg), 투명도는 8비트 회색 PNG
/// (mascot_greet_a.png)로 나눠 넣었다가 여기서 32비트 ARGB 한 장으로 합친다(RGBA PNG 4 MB → 약 1.2 MB). 장 수는 mascot_greet.txt("frames N cellW W").
/// 만드는 곳: docs/design/mascot-clips-next/round2/flf_greet16.py
/// </summary>
internal static unsafe class MascotGreet
{
    /// <summary>16 fps 그림 — 한 장 62 ms(작업 표시줄 마스코트의 ClipTickMs 와 같다).</summary>
    public const int FrameMs = 62;

    /// <summary>
    /// 앞으로 재생한 뒤 거꾸로 되돌아오는가(greet.txt "pingpong 1", 0.3.123 — 손을 올렸다가 같은 길로 내린다. 예전 "되돌아오기" 영상은
    /// 팔이 길게 늘어진 채 1초 넘게 서 있었다, 2026-10-07 사용자). 한 바퀴 = <see cref="Period"/> 걸음.
    /// </summary>
    public static bool PingPong { get; private set; }
    public static int Period(int frames) => PingPong && frames > 1 ? 2 * frames - 2 : frames;
    /// <summary>한 바퀴 안의 걸음 → 그림 장(0 = 서 있기).</summary>
    public static int Cell(int step, int frames)
    {
        int p = Period(frames); step = ((step % p) + p) % p;
        return step < frames ? step : 2 * frames - 2 - step;
    }

    /// <summary>인사 그림 띠(가로로 frames 장, GDI+ 32bpp ARGB 비트맵)를 만든다. 실패하면 0. 받은 쪽이 GdipDisposeImage.</summary>
    public static nint Load(out int frames)
    {
        frames = 0;
        nint rgb = 0, alpha = 0, dst = 0;
        try
        {
            Gdiplus.Init();
            int n = ReadFrames();
            if (n <= 0) return CatWidget.Use ? LoadCat(out frames) : 0;   // 공개판: 고양이 둘러보기
            rgb = FromResource("mascot_greet.jpg"); alpha = FromResource("mascot_greet_a.png");
            if (rgb == 0 || alpha == 0) return 0;
            GdipGetImageWidth(rgb, out uint w); GdipGetImageHeight(rgb, out uint h);
            GdipGetImageWidth(alpha, out uint aw); GdipGetImageHeight(alpha, out uint ah);
            if (w == 0 || h == 0 || aw != w || ah != h || w / (uint)n == 0) return 0;
            if (GdipCreateBitmapFromScan0((int)w, (int)h, 0, Argb, 0, out dst) != 0 || dst == 0) return 0;
            var r = new GpRect { Width = (int)w, Height = (int)h };
            BitmapData bc = default, ba = default, bd = default;
            bool lc = false, la = false, ld = false;
            try
            {
                lc = GdipBitmapLockBits(rgb, ref r, 1 /* read */, Argb, ref bc) == 0;
                la = lc && GdipBitmapLockBits(alpha, ref r, 1, Argb, ref ba) == 0;
                ld = la && GdipBitmapLockBits(dst, ref r, 2 /* write */, Argb, ref bd) == 0;
                if (!ld) return 0;
                for (int y = 0; y < (int)h; y++)
                {
                    uint* c = (uint*)(bc.Scan0 + y * bc.Stride), a = (uint*)(ba.Scan0 + y * ba.Stride), d = (uint*)(bd.Scan0 + y * bd.Stride);
                    for (int x = 0; x < (int)w; x++) d[x] = ((a[x] & 0xFF) << 24) | (c[x] & 0xFFFFFF);   // 회색 = 투명도
                }
            }
            finally
            {
                if (ld) GdipBitmapUnlockBits(dst, ref bd);
                if (la) GdipBitmapUnlockBits(alpha, ref ba);
                if (lc) GdipBitmapUnlockBits(rgb, ref bc);
            }
            frames = n;
            nint ok = dst; dst = 0;
            return ok;
        }
        catch { return 0; }
        finally
        {
            if (rgb != 0) GdipDisposeImage(rgb);
            if (alpha != 0) GdipDisposeImage(alpha);
            if (dst != 0) GdipDisposeImage(dst);
        }
    }

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

    private static int ReadFrames()
    {
        using var s = typeof(MascotGreet).Assembly.GetManifestResourceStream("mascot_greet.txt");
        if (s is null) return 0;
        string[] p = new StreamReader(s).ReadToEnd().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        PingPong = false;
        for (int i = 0; i + 1 < p.Length; i += 2) if (p[i] == "pingpong") PingPong = p[i + 1] == "1";
        return p.Length >= 2 && p[0] == "frames" && int.TryParse(p[1], out int n) && n is > 0 and <= 400 ? n : 0;
    }

    private static nint FromResource(string name)
    {
        using var s = typeof(MascotGreet).Assembly.GetManifestResourceStream(name);
        if (s is null) return 0;
        byte[] data = new byte[s.Length];
        s.ReadExactly(data);
        nint stream, img = 0;
        fixed (byte* p = data) stream = SHCreateMemStream(p, (uint)data.Length);
        if (stream == 0) return 0;
        try { return GdipCreateBitmapFromStream(stream, out img) == 0 ? img : 0; }
        finally { Marshal.Release(stream); }
    }

    private const int Argb = 0x0026200A;   // PixelFormat32bppARGB
    [StructLayout(LayoutKind.Sequential)] private struct GpRect { public int X, Y, Width, Height; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapData { public uint Width, Height; public int Stride, PixelFormat; public nint Scan0, Reserved; }
    [DllImport("shlwapi.dll", EntryPoint = "#12")] private static extern nint SHCreateMemStream(byte* pInit, uint cbInit);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateBitmapFromStream(nint stream, out nint bitmap);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateBitmapFromScan0(int w, int h, int stride, int format, nint scan0, out nint bitmap);
    [DllImport("gdiplus.dll")] private static extern int GdipGetImageWidth(nint image, out uint w);
    [DllImport("gdiplus.dll")] private static extern int GdipGetImageHeight(nint image, out uint h);
    [DllImport("gdiplus.dll")] private static extern int GdipBitmapLockBits(nint bitmap, ref GpRect rect, uint flags, int format, ref BitmapData data);
    [DllImport("gdiplus.dll")] private static extern int GdipBitmapUnlockBits(nint bitmap, ref BitmapData data);
    [DllImport("gdiplus.dll")] private static extern int GdipDisposeImage(nint image);
    [DllImport("gdiplus.dll")] private static extern int GdipGetImageGraphicsContext(nint image, out nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteGraphics(nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipGraphicsClear(nint graphics, uint argb);
    [DllImport("gdiplus.dll")] private static extern int GdipDrawImageRectI(nint graphics, nint image, int x, int y, int w, int h);
}
