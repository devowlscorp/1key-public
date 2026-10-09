using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 마스코트 인사 그림(잠금 위젯이 쓴다). 한 띠에 서로 다른 그림을 한 장씩(0번 장 = 정면) 두고, 동작마다 "걸음 → 장 번호" 차례를 둔다:
/// <see cref="Look"/> = 둘러보기(정면 → 왼쪽 → 위 → 오른쪽 → 정면 — 마우스를 올렸을 때), <see cref="Meow"/> = 야옹(입을 반쯤 → 크게 벌렸다 닫기),
/// <see cref="Chain"/> = 넓어질 때의 이어진 동작(야옹으로 시작), 그리고 0.5.15-A 의 작은 동작(2026-10-09 사용자 "b>f>g>e" — 넓은 동안 쉬었다가 하나씩):
/// <see cref="Blink"/> 천천히 눈 깜빡임, <see cref="Blep"/> 작은 혀 메롱, <see cref="Ears"/> 귀 내렸다 올리기(마스터 비밀번호가 틀렸을 때).
/// 작은 동작은 장마다 다른 그림(16 fps 실제 장면 — 예전 둘러보기는 정지 그림 11장을 2~8걸음씩 붙여 뚝뚝 끊겼다).
/// 그림은 작업 표시줄 고양이(CatWidget)의 시선 그림 + 입 그림 + 작은 동작의 바뀐 네모(lockclip_*.jpg + _a.png, 정면 그림 위에 덮는다 —
/// work/cat-motions/lock/lock_build.py). 귀를 옆으로 펴면 그림 칸 밖으로 나가므로 칸 양옆에 <see cref="Pad"/> 만큼 여백을 둔다.
/// 색은 위젯(앱) 테마를 따른다. 입 그림이 없으면 야옹·이어진 동작은 비고 부르는 쪽은 둘러보기를 쓴다.
/// </summary>
internal static unsafe class MascotGreet
{
    /// <summary>16 fps — 한 장 62 ms(틱은 62.5 ms).</summary>
    public const int FrameMs = 62;

    /// <summary>인사 그림 띠(가로로 frames 장, GDI+ 32bpp ARGB 비트맵)를 만든다. 실패하면 0. 받은 쪽이 GdipDisposeImage.</summary>
    /// <param name="light">밝은 회색 고양이(밝은 화면) / 검은 고양이(어두운 화면) — 그림을 놓을 화면의 테마.</param>
    public static nint Load(out int frames, bool light) { Gdiplus.Init(); return LoadCat(out frames, light); }

    // 공개판 고양이의 "인사": 정면 → 왼쪽 → 왼쪽 위 → 위 → 오른쪽 위 → 오른쪽 → 정면으로 둘러본다(장마다 몇 걸음씩, 16 fps 로 약 2초).
    private static readonly (string Name, int Repeat)[] CatLook =
        { ("center", 6), ("left", 5), ("up-left", 4), ("up", 4), ("up-right", 4), ("right", 5), ("center", 4) };
    // 야옹(약 1초): 입을 반쯤 → 크게 벌려 잠깐 → 반쯤 → 닫기
    private static readonly (string Name, int Repeat)[] CatMeow =
        { ("center", 1), ("meow-half", 2), ("meow-open", 8), ("meow-half", 2), ("center", 3) };

    /// <summary>
    /// 넓은 잠금 위젯의 이어진 동작(2026-10-08 사용자: 야옹부터 시작해 여러 동작을 이어서 되풀이 — 한 번 하고 가만히 있으면 단조롭다):
    /// 야옹 → 왼쪽부터 위·오른쪽·아래로 한 바퀴 둘러보기 → 위를 잠깐 → 작게 야옹 → 오른쪽 아래를 보고 정면. 약 7초. 장마다 몇 걸음(16 fps).
    /// </summary>
    private static readonly (string Name, int Repeat)[] CatChain =
    {
        ("center", 2), ("meow-half", 2), ("meow-open", 8), ("meow-half", 2), ("center", 6),
        ("left", 6), ("up-left", 4), ("up", 5), ("up-right", 4), ("right", 6), ("down-right", 4), ("down", 5), ("down-left", 4), ("left", 4), ("center", 8),
        ("up", 3), ("center", 3), ("meow-half", 3), ("center", 6),
        ("right", 5), ("down-right", 3), ("center", 6),
    };

    /// <summary>동작마다 걸음 → 띠의 장 번호(0 = 정면). 그림이 없는 동작은 빈 배열.</summary>
    public static int[] Look { get; private set; } = Array.Empty<int>();
    public static int[] Meow { get; private set; } = Array.Empty<int>();
    public static int[] Chain { get; private set; } = Array.Empty<int>();
    public static int[] Blink { get; private set; } = Array.Empty<int>();
    public static int[] Blep { get; private set; } = Array.Empty<int>();
    public static int[] Ears { get; private set; } = Array.Empty<int>();
    /// <summary>칸 양옆 여백(그림 px, 256 높이 기준 — lockclips.txt).</summary>
    public static int Pad { get; private set; }
    /// <summary>작은 동작의 첫 장 번호(그 앞은 정지 그림). 작은 동작은 몸의 윤곽이 거의 같아 그늘은 정면(0번) 것을 쓴다.</summary>
    public static int ClipStart { get; private set; }

    private sealed class Clip { public string Motion = ""; public int Frames, X, Y, W, H; }

    private static (int Pad, List<Clip> Clips) ReadClips(string th)
    {
        var list = new List<Clip>();
        int pad = 0;
        try
        {
            using var s = typeof(MascotGreet).Assembly.GetManifestResourceStream("lockclips.txt");
            if (s is null) return (0, list);
            using var r = new StreamReader(s);
            for (string? line; (line = r.ReadLine()) is not null;)
            {
                var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (p.Length == 2 && p[0] == "pad" && int.TryParse(p[1], out int pv)) pad = Math.Clamp(pv, 0, 64);
                else if (p.Length == 7 && p[1] == th && int.TryParse(p[2], out int n) && int.TryParse(p[3], out int x) && int.TryParse(p[4], out int y)
                         && int.TryParse(p[5], out int w) && int.TryParse(p[6], out int h) && n > 0 && w > 0 && h > 0
                         && CatWidget.HasPng($"lockclip_{p[0]}_{th}.jpg") && CatWidget.HasPng($"lockclip_{p[0]}_{th}_a.png"))
                    list.Add(new Clip { Motion = p[0], Frames = n, X = x, Y = y, W = w, H = h });
            }
        }
        catch { list.Clear(); }
        return (pad, list);
    }

    private static nint LoadCat(out int frames, bool light)
    {
        frames = 0;
        Look = Meow = Chain = Blink = Blep = Ears = Array.Empty<int>();
        Pad = 0; ClipStart = int.MaxValue;
        string th = light ? "light" : "dark";
        bool hasMeow = CatWidget.HasPng($"cat_{th}_meow-half.png") && CatWidget.HasPng($"cat_{th}_meow-open.png");
        // 띠의 정지 그림(이름마다 한 장): 정면이 0번
        var names = new List<string> { "center" };
        void Want(IEnumerable<(string Name, int Repeat)> seq) { foreach (var c in seq) if (!names.Contains(c.Name) && CatWidget.HasPng($"cat_{th}_{c.Name}.png")) names.Add(c.Name); }
        Want(CatLook);
        if (hasMeow) { Want(CatMeow); Want(CatChain); }
        var (pad, clips) = ReadClips(th);
        int clipCells = 0; foreach (var c in clips) clipCells += c.Frames;
        int n = names.Count + clipCells;
        var imgs = new nint[names.Count];
        nint dst = 0, g = 0;
        try
        {
            for (int i = 0; i < names.Count; i++) if ((imgs[i] = CatWidget.LoadPng($"cat_{th}_{names[i]}.png")) == 0) return 0;
            GdipGetImageWidth(imgs[0], out uint w0); GdipGetImageHeight(imgs[0], out uint h0);
            if (w0 == 0 || h0 == 0) return 0;
            int cw = (int)w0 + 2 * pad, ch = (int)h0;
            if (GdipCreateBitmapFromScan0(cw * n, ch, 0, Argb, 0, out dst) != 0 || dst == 0) return 0;
            if (GdipGetImageGraphicsContext(dst, out g) != 0) return 0;
            GdipGraphicsClear(g, 0);
            for (int i = 0; i < names.Count; i++) GdipDrawImageRectI(g, imgs[i], i * cw + pad, 0, (int)w0, ch);
            for (int c = names.Count; c < n; c++) GdipDrawImageRectI(g, imgs[0], c * cw + pad, 0, (int)w0, ch);   // 작은 동작의 장: 정면 위에 바뀐 네모를 덮는다(아래)
            GdipDeleteGraphics(g); g = 0;
            // 바뀐 네모 덮기: 띠 = 미리 곱한 색 JPEG + 투명도 PNG(작업 표시줄 동작과 같은 저장) → 이 띠(곧은 ARGB)에 그대로 바꿔 넣는다(투명도 포함 — 귀가 움직인 자리)
            var cellOf = new Dictionary<string, int[]>();
            int at = names.Count;
            foreach (var c in clips)
            {
                if (c.X < 0 || c.Y < 0 || c.X + c.W > cw || c.Y + c.H > ch || !Overlay(dst, cw, ch, at, c, th)) continue;
                var idx = new int[c.Frames];
                for (int k = 0; k < c.Frames; k++) idx[k] = at + k;
                cellOf[c.Motion] = idx;
                at += c.Frames;
            }
            frames = n; Pad = pad; ClipStart = names.Count;
            int Cell(string name) => Math.Max(0, names.IndexOf(name));
            int[] Steps(IEnumerable<(string Name, int Repeat)> seq) { var l = new List<int>(); foreach (var c in seq) for (int r = 0; r < c.Repeat; r++) l.Add(Cell(c.Name)); return l.ToArray(); }
            Look = Steps(CatLook);
            if (hasMeow) { Meow = Steps(CatMeow); Chain = Steps(CatChain); }
            Blink = cellOf.GetValueOrDefault("B") ?? Array.Empty<int>();
            Blep = cellOf.GetValueOrDefault("F") ?? Array.Empty<int>();
            Ears = cellOf.GetValueOrDefault("G") ?? Array.Empty<int>();
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

    /// <summary>작은 동작 하나의 장들을 띠의 first 번 칸부터 덮는다. 그림이 맞지 않으면 false(그 동작은 빼고 — 칸은 정면 그대로).</summary>
    private static bool Overlay(nint dst, int cw, int ch, int first, Clip c, string th)
    {
        nint col = 0, alp = 0;
        try
        {
            if ((col = CatWidget.LoadPng($"lockclip_{c.Motion}_{th}.jpg")) == 0 || (alp = CatWidget.LoadPng($"lockclip_{c.Motion}_{th}_a.png")) == 0) return false;
            GdipGetImageWidth(col, out uint sw); GdipGetImageHeight(col, out uint sh);
            GdipGetImageWidth(alp, out uint aw); GdipGetImageHeight(alp, out uint ah);
            if (sw != c.W * c.Frames || sh != c.H || aw != sw || ah != sh) return false;
            var cd = new BitmapData(); var ad = new BitmapData(); var dd = new BitmapData();
            var src = new GpRect { X = 0, Y = 0, Width = (int)sw, Height = (int)sh };
            var dstRc = new GpRect { X = first * cw, Y = 0, Width = cw * c.Frames, Height = ch };
            if (GdipBitmapLockBits(col, ref src, 1 /* ReadOnly */, Argb, &cd) != 0) return false;
            if (GdipBitmapLockBits(alp, ref src, 1, Argb, &ad) != 0) { GdipBitmapUnlockBits(col, &cd); return false; }
            if (GdipBitmapLockBits(dst, ref dstRc, 3 /* ReadWrite */, Argb, &dd) != 0) { GdipBitmapUnlockBits(col, &cd); GdipBitmapUnlockBits(alp, &ad); return false; }
            for (int f = 0; f < c.Frames; f++)
                for (int y = 0; y < c.H; y++)
                {
                    uint* pc = (uint*)((byte*)cd.Scan0 + y * cd.Stride) + f * c.W, pa = (uint*)((byte*)ad.Scan0 + y * ad.Stride) + f * c.W;
                    uint* pd = (uint*)((byte*)dd.Scan0 + (c.Y + y) * dd.Stride) + f * cw + c.X;
                    for (int x = 0; x < c.W; x++)
                    {
                        uint a = (pa[x] >> 16) & 255, cv = pc[x];
                        if (a < 8) { pd[x] = 0; continue; }
                        // 미리 곱한 색 → 곧은 색(JPEG 오차로 투명도를 넘은 값은 자른다)
                        uint r = Math.Min((cv >> 16) & 255, a) * 255 / a, gg = Math.Min((cv >> 8) & 255, a) * 255 / a, b = Math.Min(cv & 255, a) * 255 / a;
                        pd[x] = (a << 24) | (r << 16) | (gg << 8) | b;
                    }
                }
            GdipBitmapUnlockBits(dst, &dd); GdipBitmapUnlockBits(col, &cd); GdipBitmapUnlockBits(alp, &ad);
            return true;
        }
        catch { return false; }
        finally
        {
            if (col != 0) GdipDisposeImage(col);
            if (alp != 0) GdipDisposeImage(alp);
        }
    }

    private const int Argb = 0x0026200A;   // PixelFormat32bppARGB
    [StructLayout(LayoutKind.Sequential)] private struct GpRect { public int X, Y, Width, Height; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapData { public uint Width, Height; public int Stride, PixelFormat; public nint Scan0, Reserved; }
    [DllImport("gdiplus.dll")] private static extern int GdipBitmapLockBits(nint bitmap, ref GpRect rect, uint flags, int format, BitmapData* data);
    [DllImport("gdiplus.dll")] private static extern int GdipBitmapUnlockBits(nint bitmap, BitmapData* data);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateBitmapFromScan0(int w, int h, int stride, int format, nint scan0, out nint bitmap);
    [DllImport("gdiplus.dll")] private static extern int GdipGetImageWidth(nint image, out uint w);
    [DllImport("gdiplus.dll")] private static extern int GdipGetImageHeight(nint image, out uint h);
    [DllImport("gdiplus.dll")] private static extern int GdipDisposeImage(nint image);
    [DllImport("gdiplus.dll")] private static extern int GdipGetImageGraphicsContext(nint image, out nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteGraphics(nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipGraphicsClear(nint graphics, uint argb);
    [DllImport("gdiplus.dll")] private static extern int GdipDrawImageRectI(nint graphics, nint image, int x, int y, int w, int h);
}
