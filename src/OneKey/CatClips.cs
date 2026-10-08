using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 작업 표시줄 고양이의 동작(2026-10-09 사용자: 사내판처럼 모든 장을 검사하고 이어 붙인 뒤 앱에 넣고, 131-A~L 처럼 장 누락·지연이 없는지 시험).
/// 그림: Assets/cat/catclip_&lt;이름&gt;.jpg(미리 곱한 색) + _a.png(투명도) — tools/cat/make_clips.py 가 검사한 장들로 만든다. catclips.txt 한 줄 =
/// 이름 장수 칸폭 칸높이 앉은고양이가운데x 발선y 앉은키 [x:1 = 쉬는 동작이 아님(걷기 부품)].
/// 0.5.15-D(2026-10-09 사용자: "아티 버전처럼 다양한 동작으로 돌아다니는 고양이"): 쉬었다가(5~14초, 커서 쪽을 쳐다본다) 걷기 55 % · 쉬는 동작 45 %.
/// 걷기 = 돌아서기(WT, 앉기 → 서기 → 옆) → 걸음 주기(WL 16장)를 몇 번 되풀이하며 창을 옮김 → 돌아서기를 거꾸로 → 새 자리에 앉아 다시 쳐다본다.
/// 다니는 범위는 작업 표시줄 오른쪽 5분의 1(사내판과 같음). 그림은 오른쪽을 보는 것뿐이라 왼쪽으로 갈 때는 좌우를 뒤집는다.
/// 재생: 고해상도 대기 타이머 스레드가 62.5 ms(16 fps)마다 창에 틱을 보내고, 틱마다 정확히 한 걸음(시간으로 장을 고르지 않는다 — 사내판 131-B 의
/// 한 장 두 번·다음 장 건너뜀이 없게). 그림 띠는 다른 스레드에서 풀고 줄인 뒤 시작한다(시작 멈춤 없음, 사내판 131-D/E).
/// 밝은 고양이만(검은 고양이 동작 그림은 아직 없다 — 어두우면 쳐다보기만).
/// 시험 모드: ONEKEY_TEST_CAT_SEQ=1 이면 목록 순서대로 모든 그림(x:1 포함)을 0.6초 사이를 두고 되풀이하고(catseq.ps1), ONEKEY_TEST_CAT_WALK=1 이면
/// 걷기만 0.6초 사이로 되풀이한다(catwalk.ps1). 둘 다 ONEKEY_TEST_CAT_LOG 파일에 틱마다 간격·일한 시간·걸음·창 자리를 적는다.
/// </summary>
internal static unsafe partial class CatWidget
{
    private sealed class ClipInfo { public string Name = ""; public int Frames, CellW, CellH, AnchorX, FeetY, SitH; public bool SeqOnly; }
    private sealed class ClipArt { public ClipInfo Info = null!; public int Gen, CellW, CellH; public uint[] Px = Array.Empty<uint>(); }
    /// <summary>재생 한 걸음: 어느 그림의 몇 번째 장, 좌우 뒤집기, 시작 자리에서 옆으로 간 거리(px, 걷기).</summary>
    private readonly record struct PlayStep(int Art, int Cell, bool Mirror, double Dx);

    internal static readonly bool SeqTest = Program.IsTestMode && Environment.GetEnvironmentVariable("ONEKEY_TEST_CAT_SEQ") == "1";
    internal static readonly bool WalkTest = Program.IsTestMode && !SeqTest && Environment.GetEnvironmentVariable("ONEKEY_TEST_CAT_WALK") == "1";
    private const int ClipTickMs = 62, BlendFrames = 3, SeqGapMs = 600;
    private const uint WM_ANIMTICK = 0x8032, WM_CLIPREADY = 0x8033;
    /// <summary>걷기 빠르기: 그림 띠 px(앉은 키 80 기준)로 한 장에 이만큼. 앞발이 땅을 미는 빠르기(약 4.5)와 거의 움직이지 않는 뒷발 사이.</summary>
    private const double WalkStripPx = 3.6;
    private const int WalkCycle = 16;

    private static ClipInfo[]? _clips;
    private static bool _clipOn, _clipLoading;
    private static int _clipGen, _clipFrame, _seqNext, _clipLast = -1;
    private static ClipArt[] _arts = Array.Empty<ClipArt>();
    private static PlayStep[] _steps = Array.Empty<PlayStep>();
    private static string _playName = "";
    private static long _nextClipAt;
    private static bool _wantFront;               // 다음 동작을 하려고 정면을 본다(GazeNow)
    private static readonly Random _rnd = new();
    // 지금 동작 창(화면 좌표·크기)과 그 그림(_cxbits, 한 줄 _cxStride 픽셀 — 그림마다 칸 크기가 달라 가장 큰 칸으로 만든다)
    private static int _cx, _cy, _ccw, _cch, _cxStride;
    private static nint _cxmem, _cxdib, _cxold, _cxbits;
    // 정면 그림의 앉은 고양이(칸 안 좌표): 동작 그림을 같은 키·발선·가운데에 맞춘다
    private static int _sitTop = -1, _sitBot, _sitCx;
    private static volatile ClipArt[]? _ready;
    // 앉은 고양이 창의 왼쪽(화면 좌표). int.MinValue = 집(작업 표시줄 오른쪽 끝, 보일 때마다 여기서 시작). 걷기가 끝나면 간 자리
    private static int _posX = int.MinValue, _baseX;

    private static int CurW => _clipOn ? _ccw : _w;
    private static int CurH => _clipOn ? _cch : _h;
    private static int HomeX => _work.right - _w - (int)Math.Round(MarginLogical * _dpi / 96.0);
    /// <summary>다니는 범위의 왼쪽 끝(앉은 고양이 창 왼쪽): 작업 표시줄 오른쪽 5분의 1(사내판과 같음 — 너무 넓지 않게).</summary>
    private static int MinX => Math.Min(HomeX, _work.right - (_work.right - _work.left) / 5);

    /// <summary>catclips.txt(실행 파일에 든 목록). 없으면 빈 목록.</summary>
    private static ClipInfo[] Clips()
    {
        if (_clips is not null) return _clips;
        var list = new List<ClipInfo>();
        try
        {
            using var s = typeof(CatWidget).Assembly.GetManifestResourceStream("catclips.txt");
            if (s is not null)
            {
                using var r = new StreamReader(s);
                while (r.ReadLine() is string line)
                {
                    var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length < 7 || !HasPng($"catclip_{p[0]}.jpg") || !HasPng($"catclip_{p[0]}_a.png")) continue;
                    list.Add(new ClipInfo { Name = p[0], Frames = int.Parse(p[1]), CellW = int.Parse(p[2]), CellH = int.Parse(p[3]), AnchorX = int.Parse(p[4]),
                                            FeetY = int.Parse(p[5]), SitH = int.Parse(p[6]), SeqOnly = Array.IndexOf(p, "x:1") >= 0 });
                }
            }
        }
        catch { list.Clear(); }
        return _clips = list.ToArray();
    }

    private static int ClipIndex(string name) => Array.FindIndex(Clips(), c => c.Name == name);

    /// <summary>정면(가운데) 그림에서 앉은 고양이의 위·아래·가운데를 잰다(BuildArt 뒤).</summary>
    private static void MeasureSit()
    {
        _sitTop = -1;
        if (_bits == 0) return;
        int W = _w * Names.Length, top = -1, bot = -1, left = int.MaxValue, right = -1;
        uint* src = (uint*)_bits;
        for (int y = 0; y < _h; y++)
            for (int x = 0; x < _w; x++)
                if ((src[y * W + Center * _w + x] >> 24) > 24) { if (top < 0) top = y; bot = y; left = Math.Min(left, x); right = Math.Max(right, x); }
        if (top < 0) return;
        _sitTop = top; _sitBot = bot + 1; _sitCx = (left + right + 1) / 2;
    }

    /// <summary>
    /// 다음 동작을 시작할 때인가(Tick 에서, 시선 모드일 때). 때가 되면 먼저 정면을 보고(시선 섞기 0.16초), 정면이 되면 걷기 55 % · 쉬는 동작 45 %.
    /// 정시 시계 앞(:59:20 부터)에는 시작하지 않는다 — 걷기는 길면 20초 가까이 걸린다.
    /// </summary>
    private static void MaybeStartClip(long now)
    {
        if (_clipOn || _clipLoading || _clock != ClockPhase.None || _pressed || _hwnd == 0) { _wantFront = false; return; }
        if (SeqTest) { if (now >= _nextClipAt) StartClip(NextSeq()); return; }
        var dt = DateTime.Now;
        if (!_light || now < _nextClipAt || (dt.Minute == 59 && dt.Second >= 20)) { _wantFront = false; return; }
        _wantFront = true;
        if (_gaze != Center || _from != _gaze) return;
        _wantFront = false;
        if ((WalkTest || _rnd.Next(100) < 55) && StartWalk()) return;
        if (WalkTest) { ScheduleNextClip(now); return; }
        var all = Clips();
        var rest = Enumerable.Range(0, all.Length).Where(i => !all[i].SeqOnly && i != _clipLast).ToArray();
        if (rest.Length == 0) { ScheduleNextClip(now); return; }
        StartClip(rest[_rnd.Next(rest.Length)]);
    }

    private static int NextSeq() { var all = Clips(); if (all.Length == 0) return -1; int i = _seqNext % all.Length; _seqNext = i + 1; return i; }

    private static void ScheduleNextClip(long now) => _nextClipAt = now + (SeqTest || WalkTest ? SeqGapMs : 5_000 + _rnd.Next(9_000));

    /// <summary>쉬는 동작(또는 차례 시험의 그림) 하나: 그 그림의 모든 장을 차례로, 제자리에서.</summary>
    private static void StartClip(int index)
    {
        var all = Clips();
        if (index < 0 || index >= all.Length || _sitTop < 0) { ScheduleNextClip(Environment.TickCount64); return; }
        _clipLast = index;
        var steps = new PlayStep[all[index].Frames];
        for (int i = 0; i < steps.Length; i++) steps[i] = new PlayStep(0, i, false, 0);
        StartPlay(all[index].Name, new[] { all[index] }, steps);
    }

    /// <summary>
    /// 걷기: 방향·걸음 주기 수는 매번 무작위(사내판처럼 — 짧게 2~3번 20 %, 보통 4~6번 50 %, 길게 7~9번 30 %, 한 번 = 1초), 범위 끝 가까이면 안쪽으로.
    /// 갈 자리가 한 주기만큼도 없으면 false(쉬는 동작을 한다).
    /// </summary>
    private static bool StartWalk()
    {
        int it = ClipIndex("WT"), il = ClipIndex("WL");
        if (it < 0 || il < 0 || _sitTop < 0) return false;
        var all = Clips();
        var turn = all[it]; var loop = all[il];
        if (loop.Frames != WalkCycle) return false;
        double k = (_sitBot - _sitTop) / (double)loop.SitH;
        double dx = WalkStripPx * k, cyclePx = dx * WalkCycle;
        int x = WinX;
        double roomL = x - MinX, roomR = HomeX - x;
        int dir;
        if (roomL < cyclePx && roomR < cyclePx) return false;
        if (roomL < cyclePx) dir = 1;
        else if (roomR < cyclePx) dir = -1;
        else
        {
            double pos = (x - MinX) / Math.Max(1.0, HomeX - MinX);   // 0 = 왼쪽 끝, 1 = 집
            dir = pos > 0.8 ? (_rnd.Next(4) == 0 ? 1 : -1) : pos < 0.2 ? (_rnd.Next(4) == 0 ? -1 : 1) : _rnd.Next(2) == 0 ? -1 : 1;
            if ((dir < 0 ? roomL : roomR) < cyclePx) dir = -dir;
        }
        int r = _rnd.Next(100);
        int cycles = r < 20 ? 2 + _rnd.Next(2) : r < 70 ? 4 + _rnd.Next(3) : 7 + _rnd.Next(3);   // 돌아서기·돌아오기가 3.4초씩이라 걷는 쪽을 길게
        cycles = Math.Max(1, Math.Min(cycles, (int)Math.Floor((dir < 0 ? roomL : roomR) / cyclePx)));
        if (WalkTest) cycles = Math.Min(cycles, 2);
        bool m = dir < 0;
        var steps = new List<PlayStep>();
        for (int i = 0; i < turn.Frames; i++) steps.Add(new PlayStep(0, i, m, 0));               // 앉기 → 서기 → 옆
        double moved = 0;
        for (int c = 0; c < cycles; c++)
            for (int i = 0; i < WalkCycle; i++) { moved += dir * dx; steps.Add(new PlayStep(1, i, m, moved)); }
        moved += dir * dx; steps.Add(new PlayStep(1, 0, m, moved));                                // 주기 첫 장(= 끝 다음 장)에서 멈춘다
        for (int i = turn.Frames - 1; i >= 0; i--) steps.Add(new PlayStep(0, i, m, moved));     // 거꾸로: 옆 → 서기 → 앉기
        LogLine($"walkplan dir {dir} cycles {cycles} dx {dx:0.000} from {x} min {MinX} home {HomeX}");
        StartPlay("walk", new[] { turn, loop }, steps.ToArray());
        return true;
    }

    /// <summary>그림 띠들을 다른 스레드에서 지금 배율로 준비한다. 다 되면 WM_CLIPREADY 로 시작(그동안 시선 그림 그대로).</summary>
    private static void StartPlay(string name, ClipInfo[] infos, PlayStep[] steps)
    {
        double k = (_sitBot - _sitTop) / (double)infos[0].SitH;
        int gen = ++_clipGen;
        nint hwnd = _hwnd;
        _clipLoading = true;
        _playName = name; _steps = steps;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var arts = new ClipArt[infos.Length];
            for (int i = 0; i < infos.Length; i++)
                if ((arts[i] = PrepareClip(infos[i], k, gen)!) is null) { arts = null!; break; }
            _ready = arts;
            Native.PostMessageW(hwnd, WM_CLIPREADY, gen, 0);
        });
    }

    /// <summary>(다른 스레드) 색 JPEG + 투명도 PNG → 미리 곱한 띠 → 칸마다 고품질로 줄인 픽셀(칸 폭 × 장 수, 칸 높이). 실패하면 null.</summary>
    private static ClipArt? PrepareClip(ClipInfo info, double k, int gen)
    {
        nint col = 0, alp = 0, src = 0, dst = 0, g = 0;
        try
        {
            if ((col = LoadPng($"catclip_{info.Name}.jpg")) == 0 || (alp = LoadPng($"catclip_{info.Name}_a.png")) == 0) return null;
            GdipGetImageWidth(col, out uint sw); GdipGetImageHeight(col, out uint sh);
            if (sw != info.CellW * info.Frames || sh != info.CellH) return null;
            int W = (int)sw, H = (int)sh;
            var pre = new uint[W * H];
            var cd = new BitmapData(); var ad = new BitmapData();
            var rc = new GpRect { X = 0, Y = 0, Width = W, Height = H };
            if (GdipBitmapLockBits(col, ref rc, 1 /* ReadOnly */, 0x26200A /* 32bppARGB */, &cd) != 0) return null;
            if (GdipBitmapLockBits(alp, ref rc, 1, 0x26200A, &ad) != 0) { GdipBitmapUnlockBits(col, &cd); return null; }
            for (int y = 0; y < H; y++)
            {
                uint* c = (uint*)((byte*)cd.Scan0 + y * cd.Stride), a = (uint*)((byte*)ad.Scan0 + y * ad.Stride);
                for (int x = 0; x < W; x++)
                {
                    uint av = (a[x] >> 16) & 255, cv = c[x];
                    if (av < 8) { pre[y * W + x] = 0; continue; }
                    uint r = Math.Min((cv >> 16) & 255, av), gg = Math.Min((cv >> 8) & 255, av), b = Math.Min(cv & 255, av);   // 미리 곱한 색은 투명도를 넘을 수 없다(JPEG 오차)
                    pre[y * W + x] = (av << 24) | (r << 16) | (gg << 8) | b;
                }
            }
            GdipBitmapUnlockBits(col, &cd); GdipBitmapUnlockBits(alp, &ad);
            int tcw = Math.Max(1, (int)Math.Round(info.CellW * k)), tch = Math.Max(1, (int)Math.Round(info.CellH * k)), TW = tcw * info.Frames;
            var outPx = new uint[TW * tch];
            fixed (uint* ps = pre) fixed (uint* pd = outPx)
            {
                if (GdipCreateBitmapFromScan0(W, H, W * 4, 0xE200B /* PARGB */, (nint)ps, out src) != 0 || src == 0) return null;
                if (GdipCreateBitmapFromScan0(TW, tch, TW * 4, 0xE200B, (nint)pd, out dst) != 0 || dst == 0) return null;
                if (GdipGetImageGraphicsContext(dst, out g) != 0) return null;
                GdipSetInterpolationMode(g, 7 /* HighQualityBicubic */);
                GdipSetPixelOffsetMode(g, 2 /* HighQuality */);
                GdipGraphicsClear(g, 0);
                for (int i = 0; i < info.Frames; i++)   // 칸마다 따로(옆 칸이 번져 들어오지 않게)
                    GdipDrawImageRectRectI(g, src, i * tcw, 0, tcw, tch, i * info.CellW, 0, info.CellW, info.CellH, 2 /* UnitPixel */, 0, 0, 0);
                GdipDeleteGraphics(g); g = 0;
                GdipDisposeImage(dst); dst = 0;   // 픽셀은 outPx 에 남는다(Scan0 비트맵)
            }
            return new ClipArt { Info = info, Gen = gen, CellW = tcw, CellH = tch, Px = outPx };
        }
        catch { return null; }
        finally
        {
            if (g != 0) GdipDeleteGraphics(g);
            if (dst != 0) GdipDisposeImage(dst);
            if (src != 0) GdipDisposeImage(src);
            if (col != 0) GdipDisposeImage(col);
            if (alp != 0) GdipDisposeImage(alp);
        }
    }

    /// <summary>준비가 끝났다: 동작 창(가장 큰 칸)을 만들고 틱을 시작한다(첫 그림은 첫 틱에).</summary>
    private static void OnClipReady(int gen)
    {
        var arts = _ready; _ready = null;
        _clipLoading = false;
        if (arts is null || arts.Length == 0 || arts[0].Gen != gen || gen != _clipGen || !_shown || _clock != ClockPhase.None || _pressed) { ScheduleNextClip(Environment.TickCount64); return; }
        FreeClipDib();
        int mw = 0, mh = 0;
        foreach (var a in arts) { mw = Math.Max(mw, a.CellW); mh = Math.Max(mh, a.CellH); }
        if (!MakeDib(mw, mh, out _cxmem, out _cxdib, out _cxold, out _cxbits)) { FreeClipDib(); ScheduleNextClip(Environment.TickCount64); return; }
        _cxStride = mw;
        _arts = arts; _baseX = WinX;
        _clipOn = true; _clipFrame = -1;
        LogClipStart(_playName, _steps);
        if (!StartTicks(ClipTickMs)) { EndClip(); return; }
        _lastTickTs = Stopwatch.GetTimestamp();   // 첫 틱의 간격 = 시작부터(시작 멈춤 재기)
    }

    /// <summary>틱 하나 = 다음 한 걸음. 마지막 걸음 다음 틱에 정면 그림으로 돌아간다(걷기면 간 자리에서).</summary>
    private static void ClipTick()
    {
        long t0 = Stopwatch.GetTimestamp();
        NoteTickTiming();
        if (!_clipOn || _arts.Length == 0) { StopTicks(); return; }
        _clipFrame++;
        if (_clipFrame >= _steps.Length) { EndClip(); LogTick(t0, "S"); return; }
        var s = _steps[_clipFrame];
        bool ok = RenderStep(_clipFrame);
        NotePush(ok);
        if (DumpDir is not null && (_clipFrame < 4 || _clipFrame % 6 == 0 || _clipFrame >= _steps.Length - 4)) DumpClipFrame(_playName, _clipFrame);
        LogTick(t0, $"{_playName} {_clipFrame} s{_clipFrame} c{s.Art}:{s.Cell}{(s.Mirror ? "m" : "")} x{_cx}");
    }

    /// <summary>동작이 끝났다: 걷기면 간 자리가 앉은 고양이의 새 자리(범위 안으로).</summary>
    private static void EndClip()
    {
        StopTicks();
        if (_clipOn && _steps.Length > 0 && _steps[^1].Dx != 0)
            _posX = Math.Clamp(_baseX + (int)Math.Round(_steps[^1].Dx), MinX, HomeX);
        _clipOn = false; _arts = Array.Empty<ClipArt>();
        FreeClipDib();
        if (_shown) { _from = _gaze = Center; Render(); }
        ScheduleNextClip(Environment.TickCount64);
        SetProps();
    }

    private static void StopClip()
    {
        _clipGen++; _clipLoading = false; _ready = null; _wantFront = false;
        if (_clipOn) { StopTicks(); _clipOn = false; _arts = Array.Empty<ClipArt>(); }
        FreeClipDib();
    }

    private static void FreeClipDib()
    {
        if (_cxmem != 0 && _cxold != 0) Native.SelectObject(_cxmem, _cxold);
        if (_cxdib != 0) Native.DeleteObject(_cxdib);
        if (_cxmem != 0) Native.DeleteDC(_cxmem);
        _cxmem = _cxdib = _cxold = _cxbits = 0;
    }

    /// <summary>
    /// 한 걸음을 내보낸다: 그 그림의 장(왼쪽으로 갈 때는 좌우를 뒤집어)을 앉은 고양이의 가운데·발선에 맞춘 자리(+ 간 거리)에.
    /// 처음·끝 몇 걸음은 정면 그림과 섞는다(같은 자세라 이음매가 보이지 않게 — 끝은 간 자리의 정면).
    /// </summary>
    private static bool RenderStep(int i)
    {
        if (_cxbits == 0) return false;
        var s = _steps[i];
        var art = _arts[s.Art];
        int cw = art.CellW, chh = art.CellH, TW = cw * art.Info.Frames, S = _cxStride;
        double k = art.CellW / (double)art.Info.CellW;
        double anchor = art.Info.AnchorX * k;
        if (s.Mirror) anchor = cw - anchor;
        _ccw = cw; _cch = chh;
        _cx = _baseX + _sitCx + (int)Math.Round(s.Dx) - (int)Math.Round(anchor);
        _cy = WinY + _sitBot - (int)Math.Round(art.Info.FeetY * k);
        _cx = Math.Clamp(_cx, _work.left, Math.Max(_work.left, _work.right - _ccw));
        uint* o = (uint*)_cxbits;
        fixed (uint* px = art.Px)
            for (int y = 0; y < chh; y++)
            {
                uint* src = px + y * TW + s.Cell * cw, d = o + y * S;
                if (s.Mirror) for (int x = 0; x < cw; x++) d[x] = src[cw - 1 - x];
                else for (int x = 0; x < cw; x++) d[x] = src[x];
            }
        int n = _steps.Length, fromEnd = n - 1 - i;
        uint t = i < BlendFrames ? (uint)((i + 1) * 256 / (BlendFrames + 1)) : fromEnd < BlendFrames ? (uint)((fromEnd + 1) * 256 / (BlendFrames + 1)) : 256;
        if (t < 256 && _bits != 0 && _light)   // 섞을 정면 그림도 밝은 고양이일 때만(동작 그림은 밝은 고양이뿐 — 어두운 테마의 차례 시험은 섞지 않는다)
        {
            // 정면 그림(시선 띠의 가운데 칸)을 동작 창 안 같은 자리에 놓고 섞는다: 결과 = 정면 × (1 − t) + 동작 × t. 끝 쪽은 간 자리의 정면
            int gx0 = i < BlendFrames ? _baseX : _baseX + (int)Math.Round(s.Dx);
            int ox = gx0 - _cx, oy = WinY - _cy, W = _w * Names.Length;
            uint* gsrc = (uint*)_bits;
            uint u = 256 - t;
            for (int y = 0; y < chh; y++)
                for (int x = 0; x < cw; x++)
                {
                    int gx = x - ox, gy = y - oy;
                    uint p = gx >= 0 && gy >= 0 && gx < _w && gy < _h ? gsrc[gy * W + Center * _w + gx] : 0, q = o[y * S + x];
                    o[y * S + x] = (((p >> 24) * u + (q >> 24) * t) >> 8 << 24) | ((((p >> 16) & 255) * u + ((q >> 16) & 255) * t) >> 8 << 16)
                                 | ((((p >> 8) & 255) * u + ((q >> 8) & 255) * t) >> 8 << 8) | (((p & 255) * u + (q & 255) * t) >> 8);
                }
        }
        var dst = new Native.POINT { x = _cx, y = _cy };
        var size = new SIZE { cx = cw, cy = chh };
        var zero = new Native.POINT();
        var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
        return UpdateLayeredWindow(_hwnd, 0, ref dst, ref size, _cxmem, ref zero, 0, ref blend, 2);
    }

    // ------------------------------------------------------------------ 틱 스레드(사내판 WalkerPrep 과 같은 방식)

    private static int _tickGen, _tickPending;
    private static long _tickPeriod;
    private static bool _tickThreadOn;

    private static bool StartTicks(int ms)
    {
        double per = ms >= 200 ? ms : Math.Round(ms / 15.625) * 15.625;   // 62 → 62.5 ms
        Interlocked.Exchange(ref _tickPeriod, (long)(per * Stopwatch.Frequency / 1000));
        if (_tickThreadOn) return true;
        nint timer = CreateWaitableTimerExW(0, null, 2 /* CREATE_WAITABLE_TIMER_HIGH_RESOLUTION */, 0x1F0003);
        if (timer == 0) return false;
        int gen = Interlocked.Increment(ref _tickGen);
        nint hwnd = _hwnd;
        Interlocked.Exchange(ref _tickPending, 0);
        var th = new Thread(() => TickLoop(timer, hwnd, gen)) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "1Key cat tick" };
        try { th.Start(); } catch { CloseHandle(timer); return false; }
        _tickThreadOn = true;
        return true;
    }

    private static void StopTicks()
    {
        if (!_tickThreadOn) return;
        Interlocked.Increment(ref _tickGen);
        _tickThreadOn = false;
    }

    private static void TickLoop(nint timer, nint hwnd, int gen)
    {
        try
        {
            long next = Stopwatch.GetTimestamp() + Interlocked.Read(ref _tickPeriod);
            while (Volatile.Read(ref _tickGen) == gen)
            {
                long wait = next - Stopwatch.GetTimestamp();
                if (wait > 0)
                {
                    long due = -Math.Max(1, wait * 10_000_000 / Stopwatch.Frequency);
                    if (SetWaitableTimerEx(timer, ref due, 0, 0, 0, 0, 0)) WaitForSingleObject(timer, 0xFFFFFFFF);
                    else Thread.Sleep((int)Math.Max(1, wait * 1000 / Stopwatch.Frequency));
                }
                if (Volatile.Read(ref _tickGen) != gen) break;
                if (Interlocked.Exchange(ref _tickPending, 1) == 0 && !Native.PostMessageW(hwnd, WM_ANIMTICK, gen, 0)) Interlocked.Exchange(ref _tickPending, 0);
                long per = Interlocked.Read(ref _tickPeriod);
                next += per;
                long now = Stopwatch.GetTimestamp();
                if (now - next > per * 3) next = now + per;
            }
        }
        catch { }
        finally { CloseHandle(timer); }
    }

    // ------------------------------------------------------------------ 시험 기록(사내판 WalkerTest 와 같은 줄 모양 — catseq.ps1 이 읽는다)

    private static readonly string? LogPath = SeqTest || WalkTest ? Environment.GetEnvironmentVariable("ONEKEY_TEST_CAT_LOG") is { Length: > 0 } lp ? lp : Path.Combine(Path.GetTempPath(), "1Key-cat-timing.log") : null;
    private static readonly System.Collections.Concurrent.ConcurrentQueue<string> _logQ = new();
    private static long _logLastFlush, _lastTickTs;
    private static int _logFlushing, _pushN, _pushFail, _lateN;
    private static uint _pushHash;
    private static double _lastDt;
    private static bool _lastLate;

    private static void LogLine(string s) { if (LogPath is not null) _logQ.Enqueue(DateTime.Now.ToString("HH:mm:ss.fff") + " " + s); }

    private static void LogClipStart(string name, PlayStep[] steps)
    {
        if (LogPath is null) return;
        int n = steps.Length;
        LogLine($"clipstart {name} steps {n} frames {n} fps 16 ticks {string.Join(",", Enumerable.Repeat(1, n))} air {new string('0', n)}");
        if (name == "walk") LogLine($"walksteps {string.Join(",", steps.Select(t => $"{t.Art}:{t.Cell}{(t.Mirror ? "m" : "")}:{t.Dx:0.0}"))}");
    }

    private static void NoteTickTiming()
    {
        long ts = Stopwatch.GetTimestamp();
        _lastDt = _lastTickTs != 0 ? (ts - _lastTickTs) * 1000.0 / Stopwatch.Frequency : 0;
        _lastLate = _lastTickTs != 0 && _lastDt - ClipTickMs > Math.Max(20, ClipTickMs * 0.5);
        if (_lastLate) _lateN++;
        _lastTickTs = ts;
    }

    private static void NotePush(bool ok)
    {
        if (LogPath is null) return;
        _pushN++;
        if (!ok) { _pushFail++; return; }
        uint h = 2166136261;
        uint* o = (uint*)_cxbits;
        int n = _ccw * _cch;
        for (int i = 0; i < 256 && n > 0; i++) h = (h ^ o[(int)((long)i * 7919 % n)]) * 16777619;
        _pushHash = h;
    }

    private static void LogTick(long t0, string what)
    {
        if (LogPath is null) return;
        long t1 = Stopwatch.GetTimestamp();
        double work = (t1 - t0) * 1000.0 / Stopwatch.Frequency;
        LogLine($"tick dt {_lastDt:0.0} want {ClipTickMs} work {work:0.0} {what}{(_lastLate ? " LATE" : "")} n{_lateN} push {_pushN} fail {_pushFail} h {_pushHash:x8}");
        _pushN = 0; _pushFail = 0;
        if (t1 - _logLastFlush > 2 * Stopwatch.Frequency && Interlocked.CompareExchange(ref _logFlushing, 1, 0) == 0)
        {
            _logLastFlush = t1;
            string path = LogPath;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var sb = new System.Text.StringBuilder();
                    while (_logQ.TryDequeue(out var l)) sb.Append(l).Append("\r\n");
                    File.AppendAllText(path, sb.ToString());
                }
                catch { }
                finally { Interlocked.Exchange(ref _logFlushing, 0); }
            });
        }
    }

    /// <summary>시험: 남은 기록을 바로 쓴다(끝내기 전).</summary>
    internal static void FlushTestLog()
    {
        if (LogPath is null) return;
        try { var sb = new System.Text.StringBuilder(); while (_logQ.TryDequeue(out var l)) sb.Append(l).Append("\r\n"); File.AppendAllText(LogPath, sb.ToString()); } catch { }
    }

    /// <summary>시험(ONEKEY_TEST_CAT_DUMP=폴더): 내보낸 그림(화면이 아니라 동작 창의 그림 자체)을 PNG 로 — 사람이 눈으로 볼 때만, 시간 시험과 따로 돌린다.</summary>
    private static readonly string? DumpDir = SeqTest || WalkTest ? Environment.GetEnvironmentVariable("ONEKEY_TEST_CAT_DUMP") : null;

    private static void DumpClipFrame(string name, int f)
    {
        if (DumpDir is null || _cxbits == 0) return;
        nint bmp = 0;
        try
        {
            Directory.CreateDirectory(DumpDir);
            if (GdipCreateBitmapFromScan0(_ccw, _cch, _cxStride * 4, 0xE200B, _cxbits, out bmp) != 0 || bmp == 0) return;
            Guid png = new("557CF406-1A04-11D3-9A73-0000F81EF32E");
            fixed (char* fp = Path.Combine(DumpDir, $"{name}_{f:000}.png")) GdipSaveImageToFile(bmp, fp, &png, 0);
            File.AppendAllText(Path.Combine(DumpDir, "frames.txt"), $"{name} {f} win {_cx},{_cy} {_ccw}x{_cch} gaze {WinX},{WinY} {_w}x{_h} sit {_sitTop}-{_sitBot} cx {_sitCx}" + Environment.NewLine);
        }
        catch { }
        finally { if (bmp != 0) GdipDisposeImage(bmp); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct GpRect { public int X, Y, Width, Height; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapData { public uint Width, Height; public int Stride, PixelFormat; public nint Scan0, Reserved; }
    [DllImport("gdiplus.dll")] private static extern int GdipBitmapLockBits(nint bitmap, ref GpRect rect, uint flags, int format, BitmapData* data);
    [DllImport("gdiplus.dll")] private static extern int GdipBitmapUnlockBits(nint bitmap, BitmapData* data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWaitableTimerExW(nint attributes, string? name, uint flags, uint access);
    [DllImport("kernel32.dll")] private static extern bool SetWaitableTimerEx(nint timer, ref long dueTime, int period, nint completion, nint arg, nint wakeContext, uint tolerableDelay);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(nint handle, uint ms);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
}
