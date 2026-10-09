using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 고양이 키우기의 하트(0.5.17, 2026-10-10 사용자: 하트는 잠깐 보여 주기, 파티클 효과 가능하면).
/// 점수를 받으면(쓰다듬기·츄르·놀아 주기) 고양이 머리 위로 작은 하트들이 몽글몽글 떠올라 흩어지고, 그 위에 지금 단계 하트 줄(♥♥♥♡♡ — 하트 하나 =
/// 자람 한 단계, 다음 하트는 찬 만큼 채움)이 금속 딱지 위에 잠깐(약 2초) 보였다가 사라진다. 커질 때(단계가 올라갈 때)도 한 번 더.
/// 쿨타임이라 점수를 못 받았으면 하트 줄만(떠오르는 하트 없이).
/// 클릭이 통과하는 층 창(WS_EX_TRANSPARENT), 포커스를 가져가지 않는다. 고양이 동작의 일부라 Windows 애니메이션 효과 설정과 상관없이 움직인다.
/// </summary>
internal static unsafe class CatHearts
{
    public const string ClassName = "OneKeyCatHearts";
    private const double WinW = 120, WinH = 104;   // 논리 px
    private const int GaugeInMs = 180, FadeOutMs = 450, Particles = 7;
    private const double NoteH = 20, NoteGap = 4;   // 축하 한 줄(논리 px)
    private static int _totalMs = 2000;            // 한 줄이 있으면 읽을 시간만큼 길게
    private const nuint TimerFrame = 1;
    private const double ChipAlpha = 0.35;

    private struct P { public double X, Delay, Life, Rise, Size, Phase, Sway; }

    private static bool _registered;
    private static nint _hwnd, _mem, _dib, _old, _bits;
    private static int _cw, _ch, _x, _y;
    private static double _k = 1, _level;
    private static bool _dark, _burst, _still;
    private static long _start;
    private static P[] _p = Array.Empty<P>();
    private static string _name = "";   // 딱지의 글: 이름 · 단계 이름(없으면 하트 줄만)
    private static string _note = "";   // 축하 한 줄(보안 습관 보상 · 커졌을 때)
    private static double _noteW;
    private static bool _gauge;         // 하트 줄(자라는 고양이만)
    private static double _ox;      // 창 안 머리 가운데 x(물리 px)
    private static double _nameW;   // 물리 px
    private const double NamePx = 12;
    private static readonly Random _rnd = new();

    /// <summary>
    /// headCx·headTop = 고양이 머리 가운데·맨 위(화면 좌표 — 동작 중 가장 높은 자리). 그 바로 위에 고양이와 겹치지 않게 띄운다(0.5.17-K, 사용자:
    /// 놀아 주기 뒤 하트가 왼쪽에 붙고 화면 끝에서 잘린다 — 고양이 창 가운데에 맞췄다). 창은 작업 영역 안으로 밀어 넣고 하트는 머리 쪽에서 나온다.
    /// level = 0..5(찬 하트 수, 소수는 다음 하트를 그만큼, 음수 = 하트 줄 없음). burst = 떠오르는 하트. label = 딱지 글(이름 · 단계 이름),
    /// note = 그 위의 축하 한 줄(0.5.18 — "백업 고마워요! +20", "통식빵으로 부풀었어요!").
    /// </summary>
    public static void Show(int headCx, int headTop, Native.RECT work, int dpi, bool dark, double level, bool burst, string label = "", string note = "")
    {
        Close();
        nint hInst = Native.GetModuleHandleW(null);
        if (!_registered) { Ctl.RegisterClass(hInst, ClassName, &WndProc, 0); _registered = true; }
        _k = (dpi is >= 48 and <= 480 ? dpi : 96) / 96.0;
        _dark = dark; _gauge = level >= 0; _level = Math.Clamp(level, 0, CatGrowth.Steps); _still = false; _burst = burst;
        // 고양이 동작(걷기·쉬는 동작)처럼 Windows 애니메이션 설정과 상관없이 움직인다(사용자 PC 는 애니메이션 효과가 꺼져 있다)
        _name = (label ?? "").Trim();
        _nameW = _name.Length > 0 ? MeasureName(_name) : 0;
        _note = (note ?? "").Trim();
        _noteW = _note.Length > 0 ? MeasureName(_note) : 0;
        _totalMs = _note.Length > 0 ? 3000 : 2000;
        double noteRow = _note.Length > 0 ? (NoteH + NoteGap) * _k : 0;
        _cw = (int)Math.Ceiling(Math.Max(WinW * _k, Math.Max(_nameW + (5 * 11 + 4 * 4 + 16 + 8 + 12) * _k, _noteW + 28 * _k)));
        _ch = (int)Math.Ceiling(WinH * _k + noteRow);
        _x = Math.Clamp(headCx - _cw / 2, work.left, Math.Max(work.left, work.right - _cw));
        _y = Math.Max(work.top, headTop - _ch - (int)Math.Round(3 * _k));   // 머리 바로 위(겹치지 않게)
        _ox = headCx - _x;                                                    // 창 안에서 머리 가운데(창을 밀어 넣었으면 가운데가 아니다)
        var bih = new BIH { biSize = (uint)sizeof(BIH), biWidth = _cw, biHeight = -_ch, biPlanes = 1, biBitCount = 32 };
        nint screen = Native.GetDC(0);
        _mem = Fx.CreateCompatibleDC(screen);
        Native.ReleaseDC(0, screen);
        _dib = CreateDIBSection(_mem, ref bih, 0, out _bits, 0, 0);
        if (_mem == 0 || _dib == 0 || _bits == 0) { Free(); return; }
        _old = Native.SelectObject(_mem, _dib);
        _p = new P[_burst ? Particles : 0];
        for (int i = 0; i < _p.Length; i++)
            _p[i] = new P
            {
                X = (_rnd.NextDouble() - 0.5) * 26, Delay = i * 85 + _rnd.Next(40), Life = 950 + _rnd.Next(400),
                Rise = 44 + _rnd.Next(22), Size = 7 + _rnd.NextDouble() * 5, Phase = _rnd.NextDouble() * Math.PI * 2, Sway = 3 + _rnd.NextDouble() * 4,
            };
        fixed (char* cls = ClassName) fixed (char* cap = "1Key")
            _hwnd = Native.CreateWindowExW(Native.WS_EX_TOOLWINDOW | 0x08000000 /* NOACTIVATE */ | Native.WS_EX_TOPMOST | Fx.WS_EX_LAYERED | 0x20 /* TRANSPARENT */,
                cls, cap, Native.WS_POPUP, _x, _y, _cw, _ch, 0, 0, hInst, 0);
        if (_hwnd == 0) { Free(); return; }
        _start = Environment.TickCount64;
        Frame();
        Native.ShowWindow(_hwnd, 4 /* SW_SHOWNOACTIVATE */);
        Native.SetTimer(_hwnd, TimerFrame, 16, 0);
        if (Program.IsTestMode) Native.SetPropW(_hwnd, "OneKeyTestHearts", (nint)(int)Math.Round(_level * 100) + (_burst ? 100000 : 0));
    }

    public static void Close()
    {
        if (_hwnd != 0) { nint h = _hwnd; _hwnd = 0; Native.DestroyWindow(h); }
        Free();
    }

    private static void Free()
    {
        if (_mem != 0 && _old != 0) Native.SelectObject(_mem, _old);
        if (_dib != 0) Native.DeleteObject(_dib);
        if (_mem != 0) Native.DeleteDC(_mem);
        _mem = _dib = _old = _bits = 0;
    }

    // ------------------------------------------------------------------ 그리기

    private static double _nameX, _nameY, _nameH, _gaugeA, _noteX, _noteY;

    private static (nint Fam, nint Font, nint Fmt) NameFont()
    {
        string face = L.Current switch { Lang.Ja => "Yu Gothic UI", Lang.ZhHans => "Microsoft YaHei UI", Lang.En or Lang.Vi => "Segoe UI", _ => "Malgun Gothic" };
        nint fam = 0, font = 0, fmt = 0;
        fixed (char* f = face) GdipCreateFontFamilyFromName(f, 0, out fam);
        if (fam != 0) GdipCreateFont(fam, (float)(NamePx * _k), 1 /* Bold */, 2, out font);
        GdipCreateStringFormat(0x00001000 /* NoWrap */, 0, out fmt);
        if (fmt != 0) GdipSetStringFormatLineAlign(fmt, 1);
        return (fam, font, fmt);
    }

    private static void FreeName((nint Fam, nint Font, nint Fmt) f)
    {
        if (f.Fmt != 0) GdipDeleteStringFormat(f.Fmt);
        if (f.Font != 0) GdipDeleteFont(f.Font);
        if (f.Fam != 0) GdipDeleteFontFamily(f.Fam);
    }

    private static double MeasureName(string s)
    {
        Gdiplus.Init();
        var f = NameFont();
        nint dc = Native.GetDC(0), g = 0;
        try
        {
            if (f.Font == 0 || GdipCreateFromHDC(dc, out g) != 0) return 0;
            var rc = new RECTF { Width = 10000, Height = 1000 };
            RECTF b;
            fixed (char* p = s) GdipMeasureString(g, p, s.Length, f.Font, ref rc, f.Fmt, out b, out _, out _);
            return Math.Ceiling(b.Width);
        }
        finally { if (g != 0) GdipDeleteGraphics(g); Native.ReleaseDC(0, dc); FreeName(f); }
    }

    /// <summary>하트 줄 왼쪽의 이름(먹색 굵게, 하트 줄과 같이 나타나고 흐려진다).</summary>
    private static void DrawName()
    {
        if (GdipCreateBitmapFromScan0(_cw, _ch, _cw * 4, 0x000E200B /* PARGB */, _bits, out nint bmp) != 0 || bmp == 0) return;
        var f = NameFont();
        nint g = 0, brush = 0;
        try
        {
            if (f.Font == 0 || GdipGetImageGraphicsContext(bmp, out g) != 0) return;
            GdipSetTextRenderingHint(g, 4 /* AntiAlias */);
            GdipCreateSolidFill(((uint)Math.Round(255 * _gaugeA) << 24) | Metal.Ink(_dark), out brush);
            if (_name.Length > 0)
            {
                var rc = new RECTF { X = (float)_nameX - 2, Y = (float)_nameY, Width = (float)_nameW + 8, Height = (float)_nameH };
                fixed (char* p = _name) GdipDrawString(g, p, _name.Length, f.Font, ref rc, f.Fmt, brush);
            }
            if (_note.Length > 0)
            {
                var rn = new RECTF { X = (float)_noteX - 2, Y = (float)_noteY, Width = (float)_noteW + 8, Height = (float)(NoteH * _k) };
                fixed (char* p = _note) GdipDrawString(g, p, _note.Length, f.Font, ref rn, f.Fmt, brush);
            }
        }
        finally
        {
            if (brush != 0) GdipDeleteBrush(brush);
            if (g != 0) GdipDeleteGraphics(g);
            GdipDisposeImage(bmp);
            FreeName(f);
        }
    }

    private static uint Pink(bool dark) => dark ? 0xFF7A90u : 0xF0607Au;

    private static void Frame()
    {
        if (_hwnd == 0 || _bits == 0) return;
        double t = Environment.TickCount64 - _start;
        if (t >= _totalMs) { Close(); return; }
        double k = _k;
        var s = new Metal.Surf((uint*)_bits, _cw, _ch);
        new Span<uint>((void*)_bits, _cw * _ch).Clear();
        bool prem = Metal.Premul; double op = Metal.Opacity;
        Metal.Premul = true;
        try
        {
            // 하트 줄(딱지 위): 위쪽에. 들어올 때 살짝 내려오며 나타나고, 끝에 흐려진다
            int outAt = _totalMs - FadeOutMs;
            double ga = t < GaugeInMs ? t / GaugeInMs : t > outAt ? Math.Max(0, 1 - (t - outAt) / FadeOutMs) : 1;
            if (_still) ga = 1;
            double drop = t < GaugeInMs && !_still ? (1 - ga) * -4 * k : 0;
            double noteRow = _note.Length > 0 ? (NoteH + NoteGap) * k : 0;
            double gy = 6 * k + noteRow + drop;
            double hs = 11 * k, gap = 4 * k, n = _gauge ? CatGrowth.Steps : 0, rowW = n > 0 ? n * hs + (n - 1) * gap : 0, padX = 8 * k, chipH = hs + 9 * k;
            double nameGap = _nameW > 0 && n > 0 ? 7 * k : 0;
            double chipW = rowW + _nameW + nameGap + 2 * padX;
            double chipX = Math.Clamp(_ox - chipW / 2, 2 * k, Math.Max(2 * k, _cw - chipW - 2 * k));   // 머리 위 가운데(창 끝에서는 안쪽으로)
            double gx = chipX + padX + _nameW + nameGap;   // 이름이 있으면 하트 줄 왼쪽에(설정에서 지은 이름)
            // 바탕 딱지는 옅게(사용자: 흰 바탕은 없어도 된다 — 필요하면 투명도) — 어떤 바탕 위에서도 하트·이름이 보일 만큼만
            Metal.Opacity = ga * ChipAlpha;
            if (rowW > 0 || _nameW > 0) Metal.Chip(s, chipX, gy, chipW, chipH, k, _dark);
            if (_note.Length > 0)
            {
                double nw = _noteW + 2 * padX, nx = Math.Clamp(_ox - nw / 2, 2 * k, Math.Max(2 * k, _cw - nw - 2 * k));
                Metal.Chip(s, nx, 6 * k + drop, nw, NoteH * k, k, _dark);
                _noteX = nx + padX; _noteY = 6 * k + drop;
            }
            Metal.Opacity = ga;
            _nameX = gx - _nameW - nameGap; _nameY = gy; _nameH = chipH; _gaugeA = ga;
            for (int i = 0; i < n; i++)
            {
                double hx = gx + i * (hs + gap), hy = gy + (chipH - hs) / 2;
                double fill = Math.Clamp(_level - i, 0, 1);
                Heart(s, hx + hs / 2, hy, hs, _dark ? 0x77787Du : 0xADADB2u, 1, double.MaxValue);   // 빈 하트(딱지가 옅어 조금 진하게)
                if (fill > 0) Heart(s, hx + hs / 2, hy, hs, Pink(_dark), 1, hx + fill * hs);         // 찬 만큼
            }
            // 떠오르는 하트: 머리(창 아래쪽)에서 위로, 좌우로 살랑, 처음에 톡 커지고 끝에 흐려진다
            Metal.Opacity = 1;
            foreach (var p in _p)
            {
                double u = (t - p.Delay) / p.Life;
                if (u <= 0 || u >= 1) continue;
                double e = 1 - (1 - u) * (1 - u);
                double cx = _ox + (p.X + Math.Sin(p.Phase + u * Math.PI * 2.2) * p.Sway) * k;
                double top = _ch - 2 * k - e * p.Rise * k;   // 머리 바로 위에서 나온다
                double pop = u < 0.15 ? 0.45 + 0.55 * (u / 0.15) : 1, a = u > 0.6 ? 1 - (u - 0.6) / 0.4 : 1;
                double size = p.Size * k * pop;
                Heart(s, cx, top - size / 2, size, Pink(_dark), a, double.MaxValue);
            }
        }
        finally { Metal.Premul = prem; Metal.Opacity = op; }
        if ((_name.Length > 0 || _note.Length > 0) && _gaugeA > 0.01) DrawName();
        Push();
        Dump(t);
    }

    private static int _dumpN;

    /// <summary>시험 전용(ONEKEY_TEST_CATHEARTS_DUMP = 폴더): 그린 장을 그대로(미리 곱한 BGRA) — 화면을 찍지 않는다. 한 번 띄울 때 6장마다 하나.</summary>
    private static void Dump(double t)
    {
        if (!Program.IsTestMode || Environment.GetEnvironmentVariable("ONEKEY_TEST_CATHEARTS_DUMP") is not string dir || dir.Length == 0 || _bits == 0) return;
        if (_dumpN++ % 6 != 0) return;
        try
        {
            Directory.CreateDirectory(dir);
            using var f = File.Create(Path.Combine(dir, $"h{_dumpN:000}_{(int)t:0000}.bgra"));
            f.Write(System.Text.Encoding.ASCII.GetBytes($"{_cw} {_ch} {_level:0.00} {(_burst ? 1 : 0)} {(_dark ? 1 : 0)}\n"));
            f.Write(new ReadOnlySpan<byte>((void*)_bits, _cw * _ch * 4));
        }
        catch { }
    }

    /// <summary>
    /// 하트 하나: 가운데 위 cx, 위 끝 top, 높이 size. 위가 조금 밝은 세로 그라데이션 + 왼쪽 위 작은 빛(입체). clipX 보다 오른쪽은 그리지 않는다(반쯤 찬 하트).
    /// 모양은 부호 거리(iq 의 sdHeart: 아래 끝 (0,0), 위 끝 약 1.1, 폭 약 ±0.6)로 안티앨리어싱.
    /// </summary>
    private static void Heart(Metal.Surf s, double cx, double top, double size, uint rgb, double alpha, double clipX)
    {
        double sc = size / 1.1, bottom = top + size;
        int x0 = Math.Max(0, (int)Math.Floor(cx - 0.62 * sc) - 1), x1 = Math.Min(s.W, (int)Math.Ceiling(Math.Min(clipX, cx + 0.62 * sc)) + 1);
        int y0 = Math.Max(0, (int)Math.Floor(top) - 1), y1 = Math.Min(s.H, (int)Math.Ceiling(bottom) + 1);
        uint hi = Metal.Mix(rgb, 0xFFFFFF, 0.28), lo = Metal.Mix(rgb, 0x000000, 0.10);
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                double px = x + 0.5, py = y + 0.5;
                double cover = Math.Clamp(0.5 - SdHeart((px - cx) / sc, (bottom - py) / sc) * sc, 0, 1);
                if (clipX < double.MaxValue) cover *= Math.Clamp(clipX - x, 0, 1);
                if (cover <= 0) continue;
                double v = (py - top) / size;
                uint c = Metal.Mix(hi, lo, v);
                double dx = (px - (cx - 0.28 * sc)) / (0.16 * sc), dy = (py - (top + 0.30 * sc)) / (0.12 * sc);
                double spec = Math.Max(0, 1 - (dx * dx + dy * dy));
                if (spec > 0 && size >= 7) c = Metal.Mix(c, 0xFFFFFF, 0.55 * spec);
                Metal.Put(s, x, y, c, cover * alpha);
            }
    }

    private static double SdHeart(double x, double y)
    {
        x = Math.Abs(x);
        if (y + x > 1.0) { double dx = x - 0.25, dy = y - 0.75; return Math.Sqrt(dx * dx + dy * dy) - Math.Sqrt(2) / 4; }
        double ay = y - 1.0, d1 = x * x + ay * ay;
        double m = 0.5 * Math.Max(x + y, 0.0), bx = x - m, by = y - m, d2 = bx * bx + by * by;
        return Math.Sqrt(Math.Min(d1, d2)) * Math.Sign(x - y);
    }

    private static void Push()
    {
        var size = new SIZE { cx = _cw, cy = _ch };
        var src = new Native.POINT();
        var dst = new Native.POINT { x = _x, y = _y };
        var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
        nint screen = Native.GetDC(0);
        UpdateLayeredWindow(_hwnd, screen, ref dst, ref size, _mem, ref src, 0, ref blend, 2 /* ULW_ALPHA */);
        Native.ReleaseDC(0, screen);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            switch (msg)
            {
                case 0x0113:   // WM_TIMER
                    if (hwnd == _hwnd) Frame(); else Native.KillTimer(hwnd, TimerFrame);
                    return 0;
                case 0x0084:   // WM_NCHITTEST: 누를 수 없다(아래 창으로)
                    return -1;
                case Native.WM_MOUSEACTIVATE:
                    return Native.MA_NOACTIVATE;
                case Native.WM_DESTROY:
                    Native.KillTimer(hwnd, TimerFrame);
                    break;
            }
        }
        catch { }
        return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential)] private struct BIH { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biX, biY; public uint biClrUsed, biClrImportant; }
    [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(nint hwnd, nint hdcDst, ref Native.POINT pptDst, ref SIZE psize, nint hdcSrc, ref Native.POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint hdc, ref BIH bmi, uint usage, out nint bits, nint section, uint offset);
    [StructLayout(LayoutKind.Sequential)] private struct RECTF { public float X, Y, Width, Height; }
    [DllImport("gdiplus.dll")] private static extern int GdipCreateBitmapFromScan0(int w, int h, int stride, int format, nint scan0, out nint bitmap);
    [DllImport("gdiplus.dll")] private static extern int GdipGetImageGraphicsContext(nint image, out nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateFromHDC(nint hdc, out nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteGraphics(nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipDisposeImage(nint image);
    [DllImport("gdiplus.dll")] private static extern int GdipSetTextRenderingHint(nint graphics, int mode);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateSolidFill(uint argb, out nint brush);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteBrush(nint brush);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateFontFamilyFromName(char* name, nint collection, out nint family);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteFontFamily(nint family);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateFont(nint family, float emSize, int style, int unit, out nint font);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteFont(nint font);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateStringFormat(int flags, int lang, out nint format);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteStringFormat(nint format);
    [DllImport("gdiplus.dll")] private static extern int GdipSetStringFormatLineAlign(nint format, int align);
    [DllImport("gdiplus.dll")] private static extern int GdipDrawString(nint graphics, char* text, int len, nint font, ref RECTF layout, nint format, nint brush);
    [DllImport("gdiplus.dll")] private static extern int GdipMeasureString(nint graphics, char* text, int len, nint font, ref RECTF layout, nint format, out RECTF bound, out int cp, out int lines);
}
