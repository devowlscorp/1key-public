using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 마스코트 인사 그림(잠금 위젯이 쓴다). 한 띠에 두 동작: 앞쪽 <see cref="LookN"/> 장 = 둘러보기(정면 → 왼쪽 → 위 → 오른쪽 → 정면 —
/// 마우스를 올렸을 때), 그 뒤 <see cref="MeowN"/> 장 = 야옹(입을 반쯤 → 크게 벌렸다 닫기 — 칸이 넓어져 마스터 비밀번호를 넣기 시작할 때,
/// 2026-10-08 사용자). 0번 장 = 정면(서 있기). 그림은 작업 표시줄 고양이(CatWidget)의 시선 그림 + 입 그림, 색은 위젯(앱) 테마를 따른다.
/// 입 그림(cat_*_meow-half / meow-open)이 없으면 야옹은 0장이고 부르는 쪽은 둘러보기를 쓴다.
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
    /// <param name="light">밝은 회색 고양이(밝은 화면) / 검은 고양이(어두운 화면) — 그림을 놓을 화면의 테마.</param>
    public static nint Load(out int frames, bool light) { Gdiplus.Init(); return LoadCat(out frames, light); }

    // 공개판 고양이의 "인사": 정면 → 왼쪽 → 왼쪽 위 → 위 → 오른쪽 위 → 오른쪽 → 정면으로 둘러본다(장마다 몇 번씩, 16 fps 로 약 2초).
    // 그림은 9방향 시선(CatWidget 과 같은 것), 색은 부르는 쪽이 정한다(잠금 위젯 = 앱 테마)
    private static readonly (string Name, int Repeat)[] CatLook =
        { ("center", 6), ("left", 5), ("up-left", 4), ("up", 4), ("up-right", 4), ("right", 5), ("center", 4) };
    // 야옹(약 1초): 입을 반쯤 → 크게 벌려 잠깐 → 반쯤 → 닫기
    private static readonly (string Name, int Repeat)[] CatMeow =
        { ("center", 1), ("meow-half", 2), ("meow-open", 8), ("meow-half", 2), ("center", 3) };

    /// <summary>
    /// 넓은 잠금 위젯의 이어진 동작(2026-10-08 사용자: 야옹부터 시작해 여러 동작을 이어서 되풀이 — 한 번 하고 가만히 있으면 단조롭다):
    /// 야옹 → 왼쪽부터 위·오른쪽·아래로 한 바퀴 둘러보기 → 위를 잠깐 → 작게 야옹 → 오른쪽 아래를 보고 정면. 약 7초. 장마다 몇 걸음(16 fps).
    /// 띠에 없는 시선(아래쪽 셋)은 띠 끝에 한 장씩 덧붙이고, 걸음마다 띠의 몇 번째 장인지만 <see cref="Chain"/> 에 둔다(띠가 커지지 않게).
    /// </summary>
    private static readonly (string Name, int Repeat)[] CatChain =
    {
        ("center", 2), ("meow-half", 2), ("meow-open", 8), ("meow-half", 2), ("center", 6),
        ("left", 6), ("up-left", 4), ("up", 5), ("up-right", 4), ("right", 6), ("down-right", 4), ("down", 5), ("down-left", 4), ("left", 4), ("center", 8),
        ("up", 3), ("center", 3), ("meow-half", 3), ("center", 6),
        ("right", 5), ("down-right", 3), ("center", 6),
    };

    /// <summary>띠 앞쪽 둘러보기 장 수 / 그 뒤 야옹 장 수(입 그림이 없으면 0).</summary>
    public static int LookN { get; private set; }
    public static int MeowN { get; private set; }
    /// <summary>이어진 동작의 걸음 → 띠의 장 번호(입 그림이 없으면 빈 배열). 0번 걸음 = 정면.</summary>
    public static int[] Chain { get; private set; } = Array.Empty<int>();

    private static nint LoadCat(out int frames, bool light)
    {
        frames = 0;
        PingPong = false;
        LookN = MeowN = 0; Chain = Array.Empty<int>();
        string th = light ? "light" : "dark";
        var seq = new List<(string Name, int Repeat)>(CatLook);
        int look = 0; foreach (var c in CatLook) look += c.Repeat;
        int meow = 0; foreach (var c in CatMeow) meow += c.Repeat;
        bool hasMeow = CatWidget.HasPng($"cat_{th}_meow-half.png") && CatWidget.HasPng($"cat_{th}_meow-open.png");
        if (hasMeow) seq.AddRange(CatMeow);
        // 이어진 동작에만 있는 시선(띠에 아직 없는 이름)은 한 장씩 덧붙인다
        var extra = new List<string>();
        if (hasMeow)
            foreach (var c in CatChain)
                if (!seq.Exists(e => e.Name == c.Name) && !extra.Contains(c.Name) && CatWidget.HasPng($"cat_{th}_{c.Name}.png")) extra.Add(c.Name);
        foreach (string name in extra) seq.Add((name, 1));
        var imgs = new nint[seq.Count];
        nint dst = 0, g = 0;
        try
        {
            for (int i = 0; i < seq.Count; i++) if ((imgs[i] = CatWidget.LoadPng($"cat_{th}_{seq[i].Name}.png")) == 0) return 0;
            GdipGetImageWidth(imgs[0], out uint w); GdipGetImageHeight(imgs[0], out uint h);
            int n = look + (hasMeow ? meow : 0) + extra.Count;
            if (w == 0 || h == 0) return 0;
            if (GdipCreateBitmapFromScan0((int)w * n, (int)h, 0, Argb, 0, out dst) != 0 || dst == 0) return 0;
            if (GdipGetImageGraphicsContext(dst, out g) != 0) return 0;
            GdipGraphicsClear(g, 0);
            int x = 0;
            for (int i = 0; i < seq.Count; i++)
                for (int r = 0; r < seq[i].Repeat; r++, x++)
                    GdipDrawImageRectI(g, imgs[i], x * (int)w, 0, (int)w, (int)h);
            GdipDeleteGraphics(g); g = 0;
            frames = n; LookN = look; MeowN = hasMeow ? meow : 0;
            if (hasMeow)
            {
                // 이름 → 띠에서 그 그림이 처음 나오는 장
                var cellOf = new Dictionary<string, int>();
                int at = 0;
                foreach (var e in seq) { if (!cellOf.ContainsKey(e.Name)) cellOf[e.Name] = at; at += e.Repeat; }
                var chain = new List<int>();
                foreach (var c in CatChain) { int cell = cellOf.TryGetValue(c.Name, out int ci) ? ci : 0; for (int r = 0; r < c.Repeat; r++) chain.Add(cell); }
                Chain = chain.ToArray();
            }
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
