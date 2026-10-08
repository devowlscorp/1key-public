using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 공개판의 작업 표시줄 마스코트: 고양이(Codex 시안, 9방향 시선 × 밝은·어두운 테마). 걷지 않고 작업 영역 오른쪽 아래(알림 영역 위)에 앉아
/// 마우스 커서 쪽을 쳐다본다. 커서가 고양이 위에 있거나 한동안 움직이지 않으면 정면, 정시 알림 동안은 위(머리 위의 시계)를 본다.
/// 시선이 바뀔 때는 두 그림을 잠깐 섞는다. 그림 색은 Windows 테마(밝음 = 회색 고양이, 어두움 = 검은 고양이)를 따른다.
/// 창·환경·클릭 규칙은 <see cref="Walker"/> 와 같다(층 창, 항상 위, 활성화 없음, 환경은 <see cref="Walker.EnvOk(out Native.RECT, out int, out int)"/>).
/// Walker 는 걷기 그림(mascot_walk)이 없고 고양이 그림이 있으면 이 클래스로 넘긴다(<see cref="Use"/>).
/// </summary>
internal static unsafe class CatWidget
{
    public const string ClassName = "OneKeyCat";
    private static readonly string[] Names = { "up-left", "up", "up-right", "left", "center", "right", "down-left", "down", "down-right" };
    private const int Center = 4, Up = 1;
    private const int HeightLogical = 52, MarginLogical = 18;
    private const int GazeMs = 100, FastMs = 16, FadeMs = 160, CheckMs = 2000, IdleMs = 20000;
    private const nuint TimerTick = 1, TimerCheck = 2;

    /// <summary>고양이 그림이 들어 있고 걷기 그림은 없는가(공개판).</summary>
    public static bool Use { get; } = Has("cat_light_center.png") && Has("cat_dark_center.png") && !Has("mascot_walk.png");
    private static bool Has(string name) => typeof(CatWidget).Assembly.GetManifestResourceInfo(name) is not null;

    private static nint _hwnd, _owner;
    private static uint _clickMsg;
    private static bool _registered, _wanted, _shown, _pressed, _light;
    private static int _showGen, _why = 1, _dpi, _tickMs;
    // 그림 띠: 9칸(_w × _h), 미리 곱한 알파
    private static nint _mem, _dib, _old, _bits;
    private static int _w, _h;
    // 내보낼 그림(_w × _h)
    private static nint _xmem, _xdib, _xold, _xbits;
    private static Native.RECT _work;
    private static int _gaze = Center, _from = Center, _pending = -1;
    private static long _fadeStart, _lastMove, _lastCheck;
    private static Native.POINT _lastCursor;

    // 정시 알림: :59:54 에 오른쪽 아래 모서리 뒤에서 시계가 곡선으로 올라와 :59 를 보이다가 정시에 :00 으로 넘어가고, 5초 뒤 같은 길로 작아지며 들어간다
    private enum ClockPhase { None, Show, Leave }
    private static ClockPhase _clock;
    private static DateTime _clockHour, _clockDoneHour, _clockFlipAt;
    private static long _clockAt;
    private const int ClockAppearMs = 750, ClockLeaveMs = 600, ClockPreFlipMs = 700, ClockFlipMs = 600, ClockHoldMs = 5000;

    public static bool IsShown => _shown;
    public static void Init(nint owner, uint clickMsg) { _owner = owner; _clickMsg = clickMsg; }
    public static bool AcceptClick(int gen) => _shown && gen == _showGen && !_pressed && Walker.EnvOk(out _, out _);

    public static void SetWanted(bool want)
    {
        if (want == _wanted && (!want || _shown || _hwnd != 0)) { if (want) Evaluate(); return; }
        _wanted = want;
        if (!want) { HideAll(); return; }
        Evaluate();
    }

    public static void EnvChanged()
    {
        if (!_wanted) return;
        if (_shown) Hide(keepCheck: true);
        FreeArt();
        Evaluate();
    }

    public static void Destroy()
    {
        _wanted = false;
        HideAll();
        if (_hwnd != 0) { Native.DestroyWindow(_hwnd); _hwnd = 0; }
        FreeArt();
    }

    // ------------------------------------------------------------------ 보이기·숨기기

    private static void Evaluate()
    {
        if (!_wanted) { HideAll(); return; }
        if (!Walker.EnvOk(out Native.RECT work, out int dpi, out int why))
        {
            if (_shown) Hide(keepCheck: true);
            _why = why;
            RetryLater();
            return;
        }
        bool light = FlipClock.WindowsLight();
        if (_shown && dpi == _dpi && light == _light && work.left == _work.left && work.right == _work.right && work.bottom == _work.bottom) return;
        if (_shown) Hide(keepCheck: true);
        if (!EnsureWindow()) { _why = 9; SetProps(); return; }
        if (_mem == 0 || dpi != _dpi || light != _light) { FreeArt(); _dpi = dpi; _light = light; if (!BuildArt()) { FreeArt(); _why = 8; RetryLater(); return; } }
        _work = work;
        _gaze = _from = GazeNow(Environment.TickCount64); _pending = -1; _fadeStart = 0;
        if (!Render()) { FreeArt(); _why = 11; RetryLater(); return; }
        _shown = true; _why = 0; _showGen++;
        Native.ShowWindow(_hwnd, 4 /* SW_SHOWNOACTIVATE */);
        KeepOnTop();
        Native.KillTimer(_hwnd, TimerCheck);
        _lastCheck = Environment.TickCount64;
        SetTick(GazeMs);
        SetProps();
    }

    private static void Hide(bool keepCheck)
    {
        CancelPress();
        if (_clock != ClockPhase.None) { FlipClock.Hide(); _clock = ClockPhase.None; }
        if (_hwnd != 0)
        {
            Native.KillTimer(_hwnd, TimerTick); _tickMs = 0;
            if (!keepCheck) Native.KillTimer(_hwnd, TimerCheck);
            Native.ShowWindow(_hwnd, Native.SW_HIDE);
        }
        _shown = false;
        SetProps();
    }

    private static void HideAll() { Hide(keepCheck: false); if (!_wanted) { _why = 1; SetProps(); } }

    private static void RetryLater()
    {
        if (EnsureWindow()) Native.SetTimer(_hwnd, TimerCheck, CheckMs, 0);
        else _why = 9;
        SetProps();
    }

    private static bool EnsureWindow()
    {
        if (_hwnd != 0) return true;
        nint hInst = Native.GetModuleHandleW(null);
        if (!_registered) { Ctl.RegisterClass(hInst, ClassName, &WndProc, 0); _registered = true; }
        fixed (char* cls = ClassName) fixed (char* cap = "1Key")
            _hwnd = Native.CreateWindowExW(WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST, cls, cap, Native.WS_POPUP,
                0, 0, 1, 1, 0, 0, hInst, 0);
        return _hwnd != 0;
    }

    private static void SetTick(int ms)
    {
        if (ms == _tickMs) return;
        _tickMs = ms;
        Native.SetTimer(_hwnd, TimerTick, (uint)ms, 0);
    }

    private static void KeepOnTop() => Native.SetWindowPos(_hwnd, (nint)(-1) /* HWND_TOPMOST */, 0, 0, 0, 0, Native.SWP_NOMOVE_ | Native.SWP_NOSIZE_ | Native.SWP_NOACTIVATE);

    // ------------------------------------------------------------------ 그림

    /// <summary>테마에 맞는 9장을 표시 크기(높이 HeightLogical)로 줄여 한 띠에. 원본은 바로 해제.</summary>
    private static bool BuildArt()
    {
        nint bmp = 0, g = 0;
        var imgs = new nint[Names.Length];
        bool ok = false;
        try
        {
            Gdiplus.Init();
            string th = _light ? "light" : "dark";
            for (int i = 0; i < Names.Length; i++) if ((imgs[i] = LoadPng($"cat_{th}_{Names[i]}.png")) == 0) return false;
            GdipGetImageWidth(imgs[0], out uint sw); GdipGetImageHeight(imgs[0], out uint sh);
            if (sw == 0 || sh == 0) return false;
            _h = (int)Math.Round(HeightLogical * _dpi / 96.0);
            _w = Math.Max(4, (int)Math.Round(_h * (double)sw / sh));
            int W = _w * Names.Length;
            if (!MakeDib(W, _h, out _mem, out _dib, out _old, out _bits)) return false;
            if (GdipCreateBitmapFromScan0(W, _h, W * 4, 0xE200B /* PixelFormat32bppPARGB */, _bits, out bmp) != 0 || bmp == 0) return false;
            if (GdipGetImageGraphicsContext(bmp, out g) != 0) return false;
            GdipSetInterpolationMode(g, 7 /* HighQualityBicubic */);
            GdipSetPixelOffsetMode(g, 2 /* HighQuality */);
            GdipGraphicsClear(g, 0);
            for (int i = 0; i < Names.Length; i++)
            {
                GdipGetImageWidth(imgs[i], out uint iw); GdipGetImageHeight(imgs[i], out uint ih);
                GdipDrawImageRectRectI(g, imgs[i], i * _w, 0, _w, _h, 0, 0, (int)iw, (int)ih, 2 /* UnitPixel */, 0, 0, 0);
            }
            GdipDeleteGraphics(g); g = 0;
            GdipDisposeImage(bmp); bmp = 0;
            if (!MakeDib(_w, _h, out _xmem, out _xdib, out _xold, out _xbits)) return false;
            ok = true;
            return true;
        }
        catch { return false; }
        finally
        {
            if (g != 0) GdipDeleteGraphics(g);
            if (bmp != 0) GdipDisposeImage(bmp);
            foreach (nint im in imgs) if (im != 0) GdipDisposeImage(im);
            if (!ok) FreeArt();
        }
    }

    private static bool MakeDib(int w, int h, out nint mem, out nint dib, out nint old, out nint bits)
    {
        dib = old = bits = 0;
        var bih = new BIH { biSize = (uint)sizeof(BIH), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        nint screen = Native.GetDC(0);
        mem = Native.CreateCompatibleDC(screen);
        Native.ReleaseDC(0, screen);
        if (mem == 0) return false;
        dib = CreateDIBSection(mem, ref bih, 0, out bits, 0, 0);
        if (dib == 0 || bits == 0) return false;
        old = Native.SelectObject(mem, dib);
        return true;
    }

    private static void FreeArt()
    {
        if (_xmem != 0 && _xold != 0) Native.SelectObject(_xmem, _xold);
        if (_xdib != 0) Native.DeleteObject(_xdib);
        if (_xmem != 0) Native.DeleteDC(_xmem);
        _xmem = _xdib = _xold = _xbits = 0;
        if (_mem != 0 && _old != 0) Native.SelectObject(_mem, _old);
        if (_dib != 0) Native.DeleteObject(_dib);
        if (_mem != 0) Native.DeleteDC(_mem);
        _mem = _dib = _old = _bits = 0;
        _dpi = 0;
    }

    private static int WinX => _work.right - _w - (int)Math.Round(MarginLogical * _dpi / 96.0);
    private static int WinY => _work.bottom - _h;

    /// <summary>지금 시선 그림(시선이 막 바뀌었으면 앞 그림과 섞어서)을 제자리에 내보낸다.</summary>
    private static bool Render()
    {
        if (_hwnd == 0 || !Compose()) return false;
        var dst = new Native.POINT { x = WinX, y = WinY };
        var size = new SIZE { cx = _w, cy = _h };
        var zero = new Native.POINT();
        var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 /* AC_SRC_ALPHA */ };
        return UpdateLayeredWindow(_hwnd, 0, ref dst, ref size, _xmem, ref zero, 0, ref blend, 2 /* ULW_ALPHA */);
    }

    private static bool Compose()
    {
        if (_bits == 0 || _xbits == 0) return false;
        long since = Environment.TickCount64 - _fadeStart;
        bool fade = _from != _gaze && since < FadeMs;
        uint t = fade ? (uint)(since * 256 / FadeMs) : 256, u = 256 - t;
        int W = _w * Names.Length;
        uint* src = (uint*)_bits, o = (uint*)_xbits;
        for (int y = 0; y < _h; y++)
        {
            uint* b = src + y * W + _gaze * _w, a = src + y * W + _from * _w, d = o + y * _w;
            if (!fade) { for (int x = 0; x < _w; x++) d[x] = b[x]; continue; }
            for (int x = 0; x < _w; x++)
            {
                uint p = a[x], q = b[x];
                d[x] = (((p >> 24) * u + (q >> 24) * t) >> 8 << 24) | ((((p >> 16) & 255) * u + ((q >> 16) & 255) * t) >> 8 << 16)
                     | ((((p >> 8) & 255) * u + ((q >> 8) & 255) * t) >> 8 << 8) | (((p & 255) * u + (q & 255) * t) >> 8);
            }
        }
        return true;
    }

    // ------------------------------------------------------------------ 시선

    /// <summary>
    /// 지금 쳐다볼 쪽(0..8, 4 = 정면). 기준점은 고양이 눈 높이(위에서 42%). 커서가 고양이 위·바로 옆이면 정면, 한동안 움직이지 않으면 정면,
    /// 정시 알림 중이면 위. 그 밖에는 커서 방향을 8쪽으로(각 쪽 45°).
    /// </summary>
    private static int GazeNow(long now)
    {
        if (_clock != ClockPhase.None) return Up;
        if (!Native.GetCursorPos(out Native.POINT p)) return Center;
        if (p.x != _lastCursor.x || p.y != _lastCursor.y) { _lastCursor = p; _lastMove = now; }
        if (now - _lastMove > IdleMs) return Center;
        double ex = WinX + _w / 2.0, ey = WinY + _h * 0.42;
        double dx = p.x - ex, dy = p.y - ey, dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist < _h * 0.75) return Center;
        double c = 0.383 * dist;   // sin 22.5°
        int col = dx < -c ? 0 : dx > c ? 2 : 1, row = dy < -c ? 0 : dy > c ? 2 : 1;
        return row * 3 + col;
    }

    private static void Tick()
    {
        if (!_shown) return;
        long now = Environment.TickCount64;
        // 2초마다 환경을 다시 본다(전체 화면·시스템 패널·작업 표시줄·테마 바뀜). 시계가 움직이는 동안은 미룬다(끊기지 않게)
        if (_clock == ClockPhase.None && now - _lastCheck >= CheckMs)
        {
            _lastCheck = now;
            bool redo = !Walker.EnvOk(out Native.RECT work, out int dpi) || dpi != _dpi || work.bottom != _work.bottom || work.left != _work.left
                || work.right != _work.right || FlipClock.WindowsLight() != _light;
            if (redo) { Evaluate(); return; }
            KeepOnTop();
        }
        if (_pressed) return;
        var dt = DateTime.Now;
        if (_clock == ClockPhase.None && ClockDue(dt)) StartClock(dt);
        if (_clock != ClockPhase.None) ClockTick(now, dt);
        // 시선: 같은 쪽이 두 번 이어서 나와야 바꾼다(경계에서 왔다 갔다 하지 않게). 시계가 나오면 바로 위
        int g = GazeNow(now);
        bool fading = _from != _gaze;   // 섞는 중에는 새 시선을 받지 않는다(끝나면 다음 틱에)
        if (g == _gaze) _pending = -1;
        else if (!fading && (g == _pending || _clock != ClockPhase.None)) { _from = _gaze; _gaze = g; _fadeStart = now; _pending = -1; }
        else if (!fading) _pending = g;
        bool anim = _from != _gaze;
        if (anim)
        {
            Render();   // 섞기가 끝난 시각이면 Compose 가 새 그림만 그린다
            if (now - _fadeStart >= FadeMs) _from = _gaze;
        }
        SetTick(anim || _clock != ClockPhase.None ? FastMs : GazeMs);
    }

    // ------------------------------------------------------------------ 정시 알림

    private static bool ClockDue(DateTime now)
    {
        DateTime next = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0).AddHours(1);
        if (Program.IsTestMode && Environment.GetEnvironmentVariable("ONEKEY_TEST_CLOCK") == "1" && _clockDoneHour == default) return true;
        return now.Minute == 59 && now.Second >= 54 && next != _clockDoneHour;
    }

    private static void StartClock(DateTime now)
    {
        _clockHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0).AddHours(now.Minute == 59 ? 1 : 0);
        _clockDoneHour = _clockHour;
        _clock = ClockPhase.Show;
        _clockAt = Environment.TickCount64;
        var earliest = now.AddMilliseconds(ClockAppearMs + ClockPreFlipMs);   // 늘 :59 로 나타나 :00 으로 넘어간다
        _clockFlipAt = earliest > _clockHour ? earliest : _clockHour;
    }

    private static void ClockTick(long now, DateTime t)
    {
        if (_clock == ClockPhase.Show)
        {
            double a = Math.Min(1, (now - _clockAt) / (double)ClockAppearMs);
            double pa = EaseOutCubic(a);
            ClockDraw(t, pa, 0.3 + 0.7 * pa);
            if (t >= _clockFlipAt.AddMilliseconds(ClockFlipMs + ClockHoldMs)) { _clock = ClockPhase.Leave; _clockAt = now; }
        }
        else
        {
            double v = Math.Min(1, (now - _clockAt) / (double)ClockLeaveMs);
            double p = 1 - v * v;
            ClockDraw(t, p, 0.3 + 0.7 * p);
            if (v >= 1) { FlipClock.Hide(); _clock = ClockPhase.None; }
        }
    }

    private static double EaseOutCubic(double a) { a = Math.Clamp(a, 0, 1); return 1 - Math.Pow(1 - a, 3); }

    /// <summary>시계: pos 0 = 작업 영역 오른쪽 아래 모서리 뒤(24° 기움), 1 = 고양이 머리 위. 2차 곡선으로 솟았다가 왼쪽으로 들어온다(Walker 와 같은 길).</summary>
    private static void ClockDraw(DateTime t, double pos, double k)
    {
        var to = (_clockHour.Hour, _clockHour.Minute);
        var before = _clockHour.AddMinutes(-1);
        (int, int) from = (before.Hour, before.Minute);
        double flip;
        if (t < _clockFlipAt) { to = from; flip = 1; }
        else flip = Math.Min(1, (t - _clockFlipAt).TotalMilliseconds / ClockFlipMs);
        float s = _dpi / 96f;
        double fw = FlipClock.LogicalW * s, fhh = FlipClock.LogicalH * s;
        double tx = Math.Clamp(WinX + _w / 2.0, _work.left + fw / 2, _work.right - fw / 2), ty = _work.bottom - _h - 8 * s - fhh / 2;
        double u = Math.Clamp(pos, 0, 1);
        double ox = _work.right + fw * 0.30, oy = _work.bottom + fhh * 0.42;
        double bx = ox + (tx - ox) * u * u;
        double by = ty + (oy - ty) * (1 - u) * (1 - u);
        float tilt = (float)(24 * (1 - u));
        float ss = (float)(s * Math.Clamp(k, 0.05, 1));
        FlipClock.ShowTilted(bx, by, ss, from, to, flip, _light, tilt, _work.right, _work.bottom, below: _hwnd);
    }

    // ------------------------------------------------------------------ 클릭·시험

    private static void CancelPress()
    {
        if (!_pressed) return;
        _pressed = false;
        if (_hwnd != 0 && Native.GetCapture() == _hwnd) Native.ReleaseCapture();
    }

    private static void SetProps()
    {
        if (_owner == 0) return;
        Native.SetPropW(_owner, "OneKeyWalkerWhy", _why);
        if (!Program.IsTestMode) return;
        Native.SetPropW(_owner, "OneKeyTestWalker", _shown ? 1 : 0);
        Native.SetPropW(_owner, "OneKeyTestWalkerShows", _showGen);
        Native.SetPropW(_owner, "OneKeyTestWalkerTimer", _hwnd != 0 && _shown ? 1 : 0);
        Native.SetPropW(_owner, "OneKeyTestCatGaze", _gaze);
    }

    /// <summary>시험: 지정 배율로 밝은·어두운 띠(9장)와, 커서 9자리에 대한 시선 표를 dir 에 남긴다.</summary>
    internal static bool DumpForTest(string dir, int dpi)
    {
        string log = Path.Combine(dir, "cat-dump.txt");
        var lines = new List<string>();
        foreach (bool light in new[] { true, false })
        {
            FreeArt(); _dpi = dpi; _light = light;
            if (!BuildArt()) { File.WriteAllText(log, "build-art " + light); return false; }
            if (!SaveDib(_bits, _w * Names.Length, _h, Path.Combine(dir, $"cat-{(light ? "light" : "dark")}.png"))) { File.WriteAllText(log, "save"); return false; }
            lines.Add($"{(light ? "light" : "dark")} cell {_w}x{_h}");
        }
        _work = new Native.RECT { left = 0, top = 0, right = 1920, bottom = 1040 };
        long now = Environment.TickCount64;
        int cx = WinX + _w / 2, cy = (int)(WinY + _h * 0.42);
        foreach (var (px, py) in new[] { (100, 100), (cx, 100), (1919, 100), (100, cy), (cx, cy), (1919, cy), (100, 1079), (cx, 1079), (1919, 1079) })
        {
            _lastMove = now; _lastCursor = new Native.POINT { x = px, y = py };
            // GazeNow 는 실제 커서를 읽으므로 같은 식을 여기서 다시 쓴다
            double dx = px - (WinX + _w / 2.0), dy = py - (WinY + _h * 0.42), dist = Math.Sqrt(dx * dx + dy * dy);
            int g = Center;
            if (dist >= _h * 0.75) { double c = 0.383 * dist; g = (dy < -c ? 0 : dy > c ? 2 : 1) * 3 + (dx < -c ? 0 : dx > c ? 2 : 1); }
            lines.Add($"cursor {px},{py} -> {Names[g]}");
        }
        File.WriteAllLines(log, lines);
        FreeArt();
        return true;
    }

    private static bool SaveDib(nint bits, int w, int h, string path)
    {
        if (GdipCreateBitmapFromScan0(w, h, w * 4, 0xE200B, bits, out nint bmp) != 0 || bmp == 0) return false;
        try { Guid enc = new("557CF406-1A04-11D3-9A73-0000F81EF32E"); fixed (char* p = path) return GdipSaveImageToFile(bmp, p, &enc, 0) == 0; }
        finally { GdipDisposeImage(bmp); }
    }

    /// <summary>실행 파일에 넣은 PNG 를 GDI+ 이미지로(복사본). 실패하면 0. 받은 쪽이 GdipDisposeImage.</summary>
    internal static nint LoadPng(string name)
    {
        nint img = 0, stream = 0;
        try
        {
            using var s = typeof(CatWidget).Assembly.GetManifestResourceStream(name);
            if (s is null) return 0;
            byte[] data = new byte[s.Length];
            s.ReadExactly(data);
            fixed (byte* p = data) stream = SHCreateMemStream(p, (uint)data.Length);
            if (stream == 0 || GdipCreateBitmapFromStream(stream, out img) != 0 || img == 0) return 0;
            if (GdipCloneImage(img, out nint copy) == 0 && copy != 0) { GdipDisposeImage(img); img = copy; }
            nint r = img; img = 0; return r;
        }
        catch { return 0; }
        finally { if (img != 0) GdipDisposeImage(img); if (stream != 0) Marshal.Release(stream); }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            switch (msg)
            {
                case 0x0021: return 3;   // WM_MOUSEACTIVATE → MA_NOACTIVATE
                case 0x0020:             // WM_SETCURSOR: 손 모양
                    Native.SetCursor(Native.LoadCursorW(0, (nint)32649 /* IDC_HAND */));
                    return 1;
                case 0x0201:             // WM_LBUTTONDOWN
                    if (_shown) { _pressed = true; Native.SetCapture(hwnd); }
                    return 0;
                case 0x0202:             // WM_LBUTTONUP: 같은 누름을 고양이 안에서 떼었을 때만 클릭
                {
                    bool was = _pressed && Native.GetCapture() == hwnd;
                    int x = (short)(lParam & 0xFFFF), y = (short)((lParam >> 16) & 0xFFFF);
                    bool inside = x >= 0 && y >= 0 && x < _w && y < _h;
                    _pressed = false;
                    if (Native.GetCapture() == hwnd) Native.ReleaseCapture();
                    if (was && inside && _shown && _owner != 0)
                    {
                        if (Walker.EnvOk(out _, out _)) Native.PostMessageW(_owner, _clickMsg, _showGen, 0);
                        else Evaluate();
                    }
                    return 0;
                }
                case 0x0215:             // WM_CAPTURECHANGED
                    _pressed = false;
                    return 0;
                case 0x0113:             // WM_TIMER
                    if (wParam == (nint)TimerTick) Tick();
                    else if (wParam == (nint)TimerCheck) { if (_wanted && !_shown) Evaluate(); else Native.KillTimer(hwnd, TimerCheck); }
                    return 0;
                case 0x0084:             // WM_NCHITTEST: 보이지 않을 때는 통과
                    if (!_shown) return -1;
                    break;
            }
        }
        catch { }
        return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    // ------------------------------------------------------------------ P/Invoke

    private const uint WS_EX_LAYERED = 0x00080000, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x00000008;
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential)] private struct BIH { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biX, biY; public uint biClrUsed, biClrImportant; }

    [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(nint hwnd, nint hdcDst, ref Native.POINT pptDst, ref SIZE psize, nint hdcSrc, ref Native.POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint hdc, ref BIH bmi, uint usage, out nint bits, nint section, uint offset);
    [DllImport("shlwapi.dll", EntryPoint = "#12")] private static extern nint SHCreateMemStream(byte* pInit, uint cbInit);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateBitmapFromScan0(int w, int h, int stride, int format, nint scan0, out nint bitmap);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateBitmapFromStream(nint stream, out nint bitmap);
    [DllImport("gdiplus.dll")] private static extern int GdipGetImageGraphicsContext(nint image, out nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteGraphics(nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipDisposeImage(nint image);
    [DllImport("gdiplus.dll")] private static extern int GdipGetImageWidth(nint image, out uint w);
    [DllImport("gdiplus.dll")] private static extern int GdipGetImageHeight(nint image, out uint h);
    [DllImport("gdiplus.dll")] private static extern int GdipGraphicsClear(nint graphics, uint argb);
    [DllImport("gdiplus.dll")] private static extern int GdipSetInterpolationMode(nint graphics, int mode);
    [DllImport("gdiplus.dll")] private static extern int GdipSetPixelOffsetMode(nint graphics, int mode);
    [DllImport("gdiplus.dll")] private static extern int GdipCloneImage(nint image, out nint clone);
    [DllImport("gdiplus.dll")] private static extern int GdipSaveImageToFile(nint image, char* file, Guid* encoder, nint parameters);
    [DllImport("gdiplus.dll")] private static extern int GdipDrawImageRectRectI(nint graphics, nint image, int dx, int dy, int dw, int dh, int sx, int sy, int sw, int sh, int unit, nint attrs, nint cb, nint cbData);
}
