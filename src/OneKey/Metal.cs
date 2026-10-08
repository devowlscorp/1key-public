using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 새 디자인(2026-10-08, 금속 시계 질감)의 그리기. 시안(Design 캔버스 Main.dc.html · List-dark.dc.html)의 CSS 를 같은 식으로 픽셀마다 계산한다:
/// 둥근 사각형의 부호 거리(SDF)로 가장자리를 안티앨리어싱하고, box-shadow 는 가우스 흐림(σ = blur / 2)의 닫힌 식(erfc)으로 낸다.
/// GDI+ 로 겹쳐 그리던 0.5.0 방식은 그늘·빛 테두리가 1px 단위로 뭉개져 시안과 달랐다(2026-10-08 사용자: 시안과 너무 차이).
/// 색은 시안과 같은 0xRRGGBB, 길이는 물리 px(k = 배율). 표면은 위에서 아래로 놓인 32비트 DIB(BGRA).
/// </summary>
internal static unsafe class Metal
{
    public readonly struct Surf
    {
        public readonly uint* P;
        public readonly int W, H;
        public Surf(uint* p, int w, int h) { P = p; W = w; H = h; }
    }

    [DllImport("gdi32.dll")] public static extern bool GdiFlush();

    /// <summary>칠할 색(0xRRGGBB). 인자는 픽셀 가운데 좌표.</summary>
    public delegate uint Fill(double x, double y);

    // ---------------------------------------------------------------- 색

    public static uint Mix(uint a, uint b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        int ar = (int)(a >> 16 & 0xFF), ag = (int)(a >> 8 & 0xFF), ab = (int)(a & 0xFF);
        int br = (int)(b >> 16 & 0xFF), bg = (int)(b >> 8 & 0xFF), bb = (int)(b & 0xFF);
        return (uint)((int)Math.Round(ar + (br - ar) * t) << 16 | (int)Math.Round(ag + (bg - ag) * t) << 8 | (int)Math.Round(ab + (bb - ab) * t));
    }

    /// <summary>0xRRGGBB → COLORREF(0x00BBGGRR). 글자·GDI+ 쪽에 넘길 때.</summary>
    public static uint Ref(uint rgb) => (rgb & 0xFF) << 16 | (rgb & 0xFF00) | (rgb >> 16 & 0xFF);

    /// <summary>
    /// 투명 바탕(층 창 — 잠금 위젯)에 그릴 때: 표면이 미리 곱한 알파(PARGB)라 "위에 얹기"를 알파까지 계산한다. 그늘은 반투명 검정으로 남아 뒤의 화면 위에 비친다.
    /// Opacity 는 모든 칠에 곱한다(넓어지는 동안 서서히 나타나기).
    /// </summary>
    [ThreadStatic] public static bool Premul;
    [ThreadStatic] private static double _opacity;
    public static double Opacity { get => _opacity == 0 ? 1 : _opacity; set => _opacity = value <= 0 ? 1e-9 : value; }

    /// <summary>한 픽셀 얹기(그림의 그늘처럼 픽셀마다 알파가 다른 것).</summary>
    public static void Put(Surf s, int x, int y, uint rgb, double a) { if (x >= 0 && y >= 0 && x < s.W && y < s.H) Blend(s.P + y * s.W + x, rgb, a); }

    private static void Blend(uint* p, uint rgb, double a)
    {
        a *= Opacity;
        if (a <= 0.0005) return;
        if (Premul)
        {
            if (a > 1) a = 1;
            uint q = *p;
            double ia = 1 - a;
            double pr = (rgb >> 16 & 0xFF) * a + (q >> 16 & 0xFF) * ia, pg = (rgb >> 8 & 0xFF) * a + (q >> 8 & 0xFF) * ia, pb = (rgb & 0xFF) * a + (q & 0xFF) * ia;
            double pa = 255 * a + (q >> 24) * ia;
            *p = (uint)(pa + 0.5) << 24 | (uint)(pr + 0.5) << 16 | (uint)(pg + 0.5) << 8 | (uint)(pb + 0.5);
            return;
        }
        if (a >= 1) { *p = 0xFF000000u | rgb; return; }
        uint d = *p;
        double r = d >> 16 & 0xFF, g = d >> 8 & 0xFF, b = d & 0xFF;
        r += ((rgb >> 16 & 0xFF) - r) * a; g += ((rgb >> 8 & 0xFF) - g) * a; b += ((rgb & 0xFF) - b) * a;
        *p = 0xFF000000u | (uint)(r + 0.5) << 16 | (uint)(g + 0.5) << 8 | (uint)(b + 0.5);
    }

    public static uint Grad(double t, (double At, uint Rgb)[] stops)
    {
        if (t <= stops[0].At) return stops[0].Rgb;
        for (int i = 1; i < stops.Length; i++)
            if (t <= stops[i].At) return Mix(stops[i - 1].Rgb, stops[i].Rgb, (t - stops[i - 1].At) / (stops[i].At - stops[i - 1].At));
        return stops[^1].Rgb;
    }

    /// <summary>CSS radial-gradient(ellipse|circle at fx fy, …) — 크기는 기본값 farthest-corner.</summary>
    public static Fill RadialAt(double x, double y, double w, double h, double fx, double fy, bool circle, (double, uint)[] stops)
    {
        double cx = x + fx * w, cy = y + fy * h, mx = Math.Max(fx, 1 - fx) * w, my = Math.Max(fy, 1 - fy) * h;
        double rx, ry;
        if (circle) rx = ry = Math.Sqrt(mx * mx + my * my);
        else { rx = mx * Math.Sqrt(2); ry = my * Math.Sqrt(2); }
        return (px, py) => { double ex = (px - cx) / rx, ey = (py - cy) / ry; return Grad(Math.Sqrt(ex * ex + ey * ey), stops); };
    }

    // ---------------------------------------------------------------- 도형

    /// <summary>둥근 사각형까지의 부호 거리(안 음수).</summary>
    public static double Sd(double px, double py, double x, double y, double w, double h, double r)
    {
        double hw = w / 2, hh = h / 2;
        r = Math.Max(0, Math.Min(r, Math.Min(hw, hh)));
        double qx = Math.Abs(px - x - hw) - (hw - r), qy = Math.Abs(py - y - hh) - (hh - r);
        double ox = Math.Max(qx, 0), oy = Math.Max(qy, 0);
        return Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0) - r;
    }

    private static double Erfc(double x)
    {
        double z = Math.Abs(x), t = 1 / (1 + 0.3275911 * z);
        double r = t * (0.254829592 + t * (-0.284496736 + t * (1.421413741 + t * (-1.453152027 + t * 1.061405429)))) * Math.Exp(-z * z);
        return x >= 0 ? r : 2 - r;
    }

    private static void Box(Surf s, double x, double y, double w, double h, double ext, out int x0, out int y0, out int x1, out int y1)
    {
        x0 = (int)Math.Max(0, Math.Floor(x - ext)); y0 = (int)Math.Max(0, Math.Floor(y - ext));
        x1 = (int)Math.Min(s.W, Math.Ceiling(x + w + ext)); y1 = (int)Math.Min(s.H, Math.Ceiling(y + h + ext));
    }

    public static void RRect(Surf s, double x, double y, double w, double h, double r, Fill fill, double alpha = 1)
    {
        if (w <= 0 || h <= 0) return;
        Box(s, x, y, w, h, 1, out int x0, out int y0, out int x1, out int y1);
        for (int j = y0; j < y1; j++)
            for (int i = x0; i < x1; i++)
            {
                double cx = i + 0.5, cy = j + 0.5;
                double cov = Math.Clamp(0.5 - Sd(cx, cy, x, y, w, h, r), 0, 1);
                if (cov > 0) Blend(s.P + j * s.W + i, fill(cx, cy), cov * alpha);
            }
    }

    public static void RRect(Surf s, double x, double y, double w, double h, double r, uint rgb, double alpha = 1) => RRect(s, x, y, w, h, r, (_, _) => rgb, alpha);

    /// <summary>CSS box-shadow(바깥). 상자 안쪽에는 그리지 않는다(CSS 와 같게 — 반투명한 위 칠이 그늘을 겹쳐 보이지 않게).</summary>
    public static void Shadow(Surf s, double x, double y, double w, double h, double r, double dx, double dy, double blur, double spread, uint rgb, double alpha)
    {
        double sg = blur / 2;
        double sx = x + dx - spread, sy = y + dy - spread, sw = w + 2 * spread, sh = h + 2 * spread, sr = Math.Max(0, r + spread);
        Box(s, sx, sy, sw, sh, 3 * sg + 1, out int x0, out int y0, out int x1, out int y1);
        for (int j = y0; j < y1; j++)
            for (int i = x0; i < x1; i++)
            {
                double cx = i + 0.5, cy = j + 0.5;
                double d = Sd(cx, cy, sx, sy, sw, sh, sr);
                double a = sg < 0.3 ? Math.Clamp(0.5 - d, 0, 1) : 0.5 * Erfc(d / (sg * Math.Sqrt(2)));
                if (a < 0.002) continue;
                a *= 1 - Math.Clamp(0.5 - Sd(cx, cy, x, y, w, h, r), 0, 1);
                Blend(s.P + j * s.W + i, rgb, a * alpha);
            }
    }

    /// <summary>CSS box-shadow inset. 안쪽 상자(옮기고 spread 만큼 줄인)의 바깥이 흐려져 상자 안에 비친다.</summary>
    public static void Inset(Surf s, double x, double y, double w, double h, double r, double dx, double dy, double blur, double spread, uint rgb, double alpha)
    {
        double sg = blur / 2;
        double ix = x + dx + spread, iy = y + dy + spread, iw = w - 2 * spread, ih = h - 2 * spread, ir = Math.Max(0, r - spread);
        Box(s, x, y, w, h, 0, out int x0, out int y0, out int x1, out int y1);
        double reach = 3 * sg + 1 + Math.Abs(dx) + Math.Abs(dy) + spread + r;
        for (int j = y0; j < y1; j++)
            for (int i = x0; i < x1; i++)
            {
                double cx = i + 0.5, cy = j + 0.5;
                // 가장자리에서 먼 안쪽은 건너뛴다(넓은 창 바탕에서 빠르게)
                if (cx - x > reach && x + w - cx > reach && cy - y > reach && y + h - cy > reach) { i = Math.Max(i, (int)Math.Ceiling(x + w - reach) - 1); continue; }
                double cov = Math.Clamp(0.5 - Sd(cx, cy, x, y, w, h, r), 0, 1);
                if (cov <= 0) continue;
                double d = Sd(cx, cy, ix, iy, iw, ih, ir);
                double a = sg < 0.3 ? Math.Clamp(0.5 + d, 0, 1) : 0.5 * Erfc(-d / (sg * Math.Sqrt(2)));
                if (a < 0.002) continue;
                Blend(s.P + j * s.W + i, rgb, a * cov * alpha);
            }
    }

    // ---------------------------------------------------------------- 시안의 부품

    /// <summary>
    /// 창 몸체: linear-gradient(152deg, 네 색 0/30/68/100%) + 안쪽 빛·그늘(시안의 inset 그늘 셋). 맨 위 band px 는 제목 표시줄 색(첫 색)으로 모은다
    /// — Windows 제목 표시줄은 단색이라 이음매가 보이지 않게(2026-10-04 사용자). 시안의 위쪽 2px 흰 선은 제목 표시줄과 맞닿아 뺀다(왼쪽만).
    /// </summary>
    public static void Body(Surf s, double k, bool dark, double band)
    {
        uint c0 = dark ? 0x4C4D52u : 0xFFFFFFu, c1 = dark ? 0x3C3D41u : 0xF3F3F3u, c2 = dark ? 0x2E2F32u : 0xE6E6E7u, c3 = dark ? 0x232427u : 0xD8D8DAu;
        int w = s.W, h = s.H;
        double a = 152 * Math.PI / 180, ux = Math.Sin(a), uy = -Math.Cos(a);
        double len = Math.Abs(w * ux) + Math.Abs(h * uy);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double t = Math.Clamp(((x + 0.5 - w / 2.0) * ux + (y + 0.5 - h / 2.0) * uy) / len + 0.5, 0, 1);
                uint c = t < 0.30 ? Mix(c0, c1, t / 0.30) : t < 0.68 ? Mix(c1, c2, (t - 0.30) / 0.38) : Mix(c2, c3, (t - 0.68) / 0.32);
                if (y < band) { double f = 1 - (y + 0.5) / band; c = Mix(c, c0, f * f * (3 - 2 * f)); }
                s.P[y * w + x] = 0xFF000000u | c;
            }
        Inset(s, 0, 0, w, h, 0, 2 * k, 0, 0, 0, 0xFFFFFF, dark ? 0.13 : 0.95);
        Inset(s, 0, 0, w, h, 0, -3 * k, -4 * k, 8 * k, 0, 0x000000, dark ? 0.35 : 0.11);
        Inset(s, 0, 0, w, h, 0, 0, 0, 22 * k, 0, 0xFFFFFF, dark ? 0.04 : 0.45);
    }

    /// <summary>시계판처럼 파인 판: 타원 빛 번짐(95%·75% at 38% 22%) 위에 3px 결 + 안쪽 그늘(위) + 안쪽 빛 1px(아래) + 바깥 빛·그늘 1px.</summary>
    public static void Dial(Surf s, double x, double y, double w, double h, double k, bool dark)
    {
        double r = 20 * k;
        Shadow(s, x, y, w, h, r, 0, 1 * k, 0, 0, 0xFFFFFF, dark ? 0.08 : 0.95);
        Shadow(s, x, y, w, h, r, 0, -1 * k, 0, 0, 0x000000, dark ? 0.30 : 0.05);
        (double, uint)[] stops = dark ? new[] { (0.0, 0x36373Cu), (0.55, 0x2A2B2Fu), (1.0, 0x202124u) } : new[] { (0.0, 0xF8F8F8u), (0.55, 0xE9E9E9u), (1.0, 0xDBDBDCu) };
        double cx = x + 0.38 * w, cy = y + 0.22 * h, rx = 0.95 * w, ry = 0.75 * h, bottom = y + h;
        double aLine = dark ? 0.025 : 0.05, aGap = dark ? 0.04 : 0.018;
        RRect(s, x, y, w, h, r, (px, py) =>
        {
            double ex = (px - cx) / rx, ey = (py - cy) / ry;
            uint c = Grad(Math.Sqrt(ex * ex + ey * ey), stops);
            double m = ((bottom - py) / k) % 3;   // repeating-linear-gradient(0deg, 1px 빛, 2px 그늘) — 아래에서 위로
            return m < 1 ? Mix(c, 0xFFFFFF, aLine) : Mix(c, 0x000000, aGap);
        });
        Inset(s, x, y, w, h, r, 0, 2 * k, (dark ? 7 : 6) * k, 0, 0x000000, dark ? 0.55 : 0.16);
        Inset(s, x, y, w, h, r, 0, -1 * k, 0, 0, 0xFFFFFF, dark ? 0.06 : 0.85);
    }

    /// <summary>판 위 조각의 위·아래 색. state: 0 보통 · 1 올림 · 2 누름.</summary>
    public static (uint Top, uint Bottom) TileColors(bool dark, int state)
    {
        uint t = dark ? 0x3C3D42u : 0xFFFFFFu, b = dark ? 0x333438u : 0xF5F5F5u;
        if (state == 1) { uint toward = dark ? 0xFFFFFFu : 0x2F3E8Fu; double f = dark ? 0.05 : 0.045; t = Mix(t, toward, f); b = Mix(b, toward, f); }
        else if (state == 2) { double f = dark ? 0.10 : 0.05; t = Mix(t, 0, f); b = Mix(b, 0, f); }
        return (t, b);
    }

    /// <summary>판 위 조각(높이 58, 반지름 12): 위→아래 그라데이션 + 그늘(밝음: 1px 2px 10% 와 1px 테 4% / 어두움: 1px 3px 45% + 위 안쪽 빛).</summary>
    public static void Tile(Surf s, double x, double y, double w, double h, double k, bool dark, int state)
    {
        double r = 12 * k;
        if (dark) Shadow(s, x, y, w, h, r, 0, 1 * k, 3 * k, 0, 0x000000, 0.45);
        else { Shadow(s, x, y, w, h, r, 0, 1 * k, 2 * k, 0, 0x000000, 0.10); Shadow(s, x, y, w, h, r, 0, 0, 0, 1 * k, 0x000000, 0.04); }
        (uint top, uint bot) = TileColors(dark, state);
        RRect(s, x, y, w, h, r, (_, py) => Mix(top, bot, (py - y) / h));
        if (dark) Inset(s, x, y, w, h, r, 0, 1 * k, 0, 0, 0xFFFFFF, 0.07);
    }

    private static (double, uint)[] Hover((double, uint)[] stops, bool dark, int state)
    {
        if (state == 0) return stops;
        var o = new (double, uint)[stops.Length];
        for (int i = 0; i < stops.Length; i++)
            o[i] = (stops[i].Item1, state == 1 ? Mix(stops[i].Item2, dark ? 0xFFFFFFu : 0x2F3E8Fu, dark ? 0.07 : 0.05) : Mix(stops[i].Item2, 0x000000, dark ? 0.12 : 0.07));
        return o;
    }

    /// <summary>
    /// 머리줄의 둥근 단추(지름 30): radial-gradient(circle at 35% 30%) + 아래 그늘 + 안쪽 1px. state: 0 · 1 올림 · 2 누름(그늘 없이 눌린 면).
    /// lift 2 = 설정 자동 잠금의 ‹ ›(지름 34, 그늘 0 2px 4px).
    /// </summary>
    public static void Knob(Surf s, double x, double y, double d, double k, bool dark, int state, double lift = 1)
    {
        double r = d / 2;
        if (state != 2) Shadow(s, x, y, d, d, r, 0, lift * k, 2 * lift * k, 0, 0x000000, dark ? (lift > 1 ? 0.55 : 0.5) : 0.22);
        var stops = Hover(dark ? new[] { (0.0, 0x5E5F64u), (0.55, 0x44454Au), (1.0, 0x2C2D31u) } : new[] { (0.0, 0xFFFFFFu), (0.55, 0xEDEDEDu), (1.0, 0xCFCFD1u) }, dark, state);
        RRect(s, x, y, d, d, r, RadialAt(x, y, d, d, 0.35, state == 2 ? 0.45 : 0.30, true, stops));
        if (dark) Inset(s, x, y, d, d, r, 0, 1 * k, 0, 0, 0xFFFFFF, 0.12);
        else Inset(s, x, y, d, d, r, 0, -1 * k, 1 * k, 0, 0x000000, 0.08);
    }

    /// <summary>
    /// 알약 단추. main = [+ 추가](높이 36: 세 색, 그늘 2px 4px), 아니면 조각 위 [입력](높이 28: 두 색, 그늘 1px 2px). radial-gradient(ellipse at 40% 25%).
    /// </summary>
    public static void Pill(Surf s, double x, double y, double w, double h, double k, bool dark, bool main, int state)
    {
        double r = h / 2;
        if (state != 2)
        {
            if (main) Shadow(s, x, y, w, h, r, 0, 2 * k, (dark ? 5 : 4) * k, 0, 0x000000, dark ? 0.5 : 0.18);
            else Shadow(s, x, y, w, h, r, 0, 1 * k, 2 * k, 0, 0x000000, dark ? 0.5 : 0.18);
        }
        (double, uint)[] stops = main
            ? (dark ? new[] { (0.0, 0x5E5F64u), (0.6, 0x44454Au), (1.0, 0x303135u) } : new[] { (0.0, 0xFFFFFFu), (0.6, 0xEDEDEDu), (1.0, 0xD5D5D7u) })
            : (dark ? new[] { (0.0, 0x55565Bu), (1.0, 0x3A3B40u) } : new[] { (0.0, 0xFFFFFFu), (1.0, 0xE9E9E9u) });
        RRect(s, x, y, w, h, r, RadialAt(x, y, w, h, 0.40, state == 2 ? 0.45 : 0.25, false, Hover(stops, dark, state)));
        if (dark) Inset(s, x, y, w, h, r, 0, 1 * k, 0, 0, 0xFFFFFF, main ? 0.12 : 0.10);
        else if (main) Inset(s, x, y, w, h, r, 0, -1 * k, 1 * k, 0, 0x000000, 0.08);
    }

    // ---------------------------------------------------------------- 설정 화면의 부품(시안 Settings-light/dark.dc.html)

    /// <summary>강조색(시안 기본값): 밝음 #2F3E8F, 어두움 #4F5FC8.</summary>
    public static uint Accent(bool dark) => dark ? 0x4F5FC8u : 0x2F3E8Fu;

    /// <summary>
    /// 스위치(42×24, 반지름 12): 켬 = 강조색 + 안쪽 그늘 0 1px 3px, 끔 = 위→아래 회색. 손잡이 18 이 3px 안쪽에 — radial(circle at 35% 30%) + 그늘 0 1px 2px.
    /// state: 0 · 1 올림(조금 밝게) · 2 누름.
    /// </summary>
    public static void Switch(Surf s, double x, double y, double k, bool dark, bool on, int state)
    {
        double w = 42 * k, h = 24 * k, r = h / 2;
        if (on)
        {
            uint c = Accent(dark);
            if (state == 1) c = Mix(c, dark ? 0xFFFFFFu : 0x000000u, 0.08); else if (state == 2) c = Mix(c, 0x000000, 0.16);
            RRect(s, x, y, w, h, r, c);
        }
        else
        {
            uint t = dark ? 0x232427u : 0xCACACCu, b = dark ? 0x2E2F33u : 0xDADADBu;
            if (state != 0) { double f = state == 1 ? 0.05 : 0.10; t = Mix(t, dark ? 0xFFFFFFu : 0x000000u, f); b = Mix(b, dark ? 0xFFFFFFu : 0x000000u, f); }
            RRect(s, x, y, w, h, r, (_, py) => Mix(t, b, (py - y) / h));
        }
        Inset(s, x, y, w, h, r, 0, 1 * k, 3 * k, 0, 0x000000, on ? (dark ? 0.5 : 0.35) : (dark ? 0.6 : 0.25));
        double d = 18 * k, kx = on ? x + w - 3 * k - d : x + 3 * k, ky = y + 3 * k;
        Shadow(s, kx, ky, d, d, d / 2, 0, 1 * k, 2 * k, 0, 0x000000, dark ? 0.6 : 0.35);
        (uint c0, uint c1) = dark ? (on ? (0xF6F6F6u, 0xCFCFCFu) : (0xE6E6E6u, 0xB8B8B8u)) : (0xFFFFFFu, 0xE2E2E2u);
        RRect(s, kx, ky, d, d, d / 2, RadialAt(kx, ky, d, d, 0.35, 0.30, true, new[] { (0.0, c0), (1.0, c1) }));
    }

    /// <summary>파인 홈(테마 고르기 묶음 · 키 칸): 위→아래(밝음 #E2E2E3→#EDEDED, 어두움 #1E1F22→#26272A) + 안쪽 그늘 0 1px 3px.</summary>
    public static void Well(Surf s, double x, double y, double w, double h, double r, double k, bool dark, int state = 0)
    {
        uint t = dark ? 0x1E1F22u : 0xE2E2E3u, b = dark ? 0x26272Au : 0xEDEDEDu;
        if (state == 1) { t = Mix(t, dark ? 0xFFFFFFu : 0x2F3E8Fu, 0.04); b = Mix(b, dark ? 0xFFFFFFu : 0x2F3E8Fu, 0.04); }
        RRect(s, x, y, w, h, r, (_, py) => Mix(t, b, (py - y) / h));
        Inset(s, x, y, w, h, r, 0, 1 * k, 3 * k, 0, 0x000000, dark ? 0.6 : 0.16);
    }

    /// <summary>홈 안의 고른 칸(반지름 8): radial(ellipse at 40% 25%) + 그늘 0 1px 3px (+ 어두움은 위 안쪽 빛 1px).</summary>
    public static void SegChip(Surf s, double x, double y, double w, double h, double k, bool dark)
    {
        double r = 8 * k;
        Shadow(s, x, y, w, h, r, 0, 1 * k, 3 * k, 0, 0x000000, dark ? 0.5 : 0.2);
        var stops = dark ? new[] { (0.0, 0x55565Bu), (1.0, 0x3E3F44u) } : new[] { (0.0, 0xFFFFFFu), (1.0, 0xF0F0F0u) };
        RRect(s, x, y, w, h, r, RadialAt(x, y, w, h, 0.40, 0.25, false, stops));
        if (dark) Inset(s, x, y, w, h, r, 0, 1 * k, 0, 0, 0xFFFFFF, 0.10);
    }

    /// <summary>자동 잠금 단계 점(5px, 파인 점) · 고른 단계는 22×5 강조색 막대.</summary>
    public static void StepDot(Surf s, double x, double y, double k, bool dark, bool sel)
    {
        double h = 5 * k;
        if (sel) { RRect(s, x, y, 22 * k, h, 3 * k, Accent(dark)); return; }
        RRect(s, x, y, h, h, h / 2, dark ? 0x232427u : 0xCCCCCAu);
        Inset(s, x, y, h, h, h / 2, 0, 1 * k, 1 * k, 0, 0x000000, dark ? 0.6 : 0.2);
    }

    /// <summary>[저장] 알약: 강조색 + 그늘(밝음 0 2px 6px 강조색 35% / 어두움 0 2px 8px 검정 50%) + 위 안쪽 빛 1px.</summary>
    public static void AccentPill(Surf s, double x, double y, double w, double h, double k, bool dark, int state)
    {
        double r = h / 2;
        if (state != 2)
        {
            if (dark) Shadow(s, x, y, w, h, r, 0, 2 * k, 8 * k, 0, 0x000000, 0.5);
            else Shadow(s, x, y, w, h, r, 0, 2 * k, 6 * k, 0, 0x2F3E8F, 0.35);
        }
        uint c = Accent(dark);
        if (state == 1) c = dark ? Mix(c, 0xFFFFFF, 0.08) : Mix(c, 0x000000, 0.14);
        else if (state == 2) c = Mix(c, 0x000000, 0.22);
        RRect(s, x, y, w, h, r, c);
        Inset(s, x, y, w, h, r, 0, 1 * k, 0, 0, 0xFFFFFF, dark ? 0.2 : 0.25);
    }

    /// <summary>조각 안 가로 구분선 1px(밝음 #ECECEA / 어두움 검정 35% 아래 흰 5% 1px).</summary>
    public static void Sep(Surf s, double x, double y, double w, double k, bool dark)
    {
        double t = Math.Max(1, Math.Round(k));
        if (!dark) { RRect(s, x, y, w, t, 0, 0xECECEA); return; }
        RRect(s, x, y, w, t, 0, 0x000000, 0.35);
        RRect(s, x, y + t, w, t, 0, 0xFFFFFF, 0.05);
    }

    // ---------------------------------------------------------------- 잠금 위젯의 부품(시안 Lock-light/dark.dc.html)

    /// <summary>CSS linear-gradient(각도, …): 상자 가운데를 지나는 각도 방향의 거리로 0..1.</summary>
    public static Fill LinearAt(double x, double y, double w, double h, double deg, (double, uint)[] stops)
    {
        double a = deg * Math.PI / 180, ux = Math.Sin(a), uy = -Math.Cos(a), len = Math.Abs(w * ux) + Math.Abs(h * uy);
        double cx = x + w / 2, cy = y + h / 2;
        return (px, py) => Grad(Math.Clamp(((px - cx) * ux + (py - cy) * uy) / len + 0.5, 0, 1), stops);
    }

    /// <summary>
    /// 떠 있는 금속 판(말풍선 · 입력 알약의 테): 152° 세 색 + 안쪽 빛 1.5px(왼쪽 위) · 안쪽 그늘 -2 -3 6 + 바깥 그늘 0 12px 26px(말풍선은 0 2px 6px 하나 더).
    /// mid = 가운데 색의 자리(말풍선 .35, 알약 .45).
    /// </summary>
    public static void Plate(Surf s, double x, double y, double w, double h, double r, double k, bool dark, bool bubble)
    {
        Shadow(s, x, y, w, h, r, 0, 12 * k, 26 * k, 0, dark ? 0x000000u : 0x28282Du, dark ? 0.5 : 0.18);
        if (bubble) Shadow(s, x, y, w, h, r, 0, 2 * k, 6 * k, 0, dark ? 0x000000u : 0x28282Du, dark ? 0.3 : 0.10);
        var stops = bubble
            ? (dark ? new[] { (0.0, 0x4C4D52u), (0.35, 0x3C3D41u), (1.0, 0x2B2C2Fu) } : new[] { (0.0, 0xFFFFFFu), (0.35, 0xF3F3F3u), (1.0, 0xE4E4E5u) })
            : (dark ? new[] { (0.0, 0x4C4D52u), (0.45, 0x393A3Eu), (1.0, 0x27282Bu) } : new[] { (0.0, 0xFFFFFFu), (0.45, 0xEDEDEDu), (1.0, 0xD9D9DBu) });
        RRect(s, x, y, w, h, r, LinearAt(x, y, w, h, 152, stops));
        Inset(s, x, y, w, h, r, 1.5 * k, 1.5 * k, 0, 0, 0xFFFFFF, dark ? 0.13 : 0.95);
        Inset(s, x, y, w, h, r, -2 * k, -3 * k, 6 * k, 0, 0x000000, dark ? 0.35 : 0.10);
    }

    /// <summary>입력 알약 안의 파인 칸(높이 48, 반지름 25): radial(ellipse 95% 120% at 35% 20%) + 안쪽 그늘 + 아래 빛 1px + 강조색 테 1.5px(ring 이면).</summary>
    public static void LockWell(Surf s, double x, double y, double w, double h, double k, bool dark, bool ring)
    {
        double r = h / 2;
        if (ring) Shadow(s, x, y, w, h, r, 0, 0, 0, 1.5 * k, Accent(dark), 1);
        var stops = dark ? new[] { (0.0, 0x34353Au), (0.6, 0x28292Du), (1.0, 0x1F2023u) } : new[] { (0.0, 0xFAFAFAu), (0.6, 0xEBEBEBu), (1.0, 0xDEDEDFu) };
        double cx = x + 0.35 * w, cy = y + 0.20 * h, rx = 0.95 * w, ry = 1.20 * h;
        RRect(s, x, y, w, h, r, (px, py) => { double ex = (px - cx) / rx, ey = (py - cy) / ry; return Grad(Math.Sqrt(ex * ex + ey * ey), stops); });
        Inset(s, x, y, w, h, r, 0, 2 * k, (dark ? 6 : 5) * k, 0, 0x000000, dark ? 0.6 : 0.16);
        Inset(s, x, y, w, h, r, 0, -1 * k, 0, 0, 0xFFFFFF, dark ? 0.06 : 0.85);
    }

    /// <summary>버전 딱지(반지름 10): 위→아래 두 색 + 그늘 0 1px 2px (+ 어두움은 위 안쪽 빛).</summary>
    public static void Chip(Surf s, double x, double y, double w, double h, double k, bool dark)
    {
        double r = Math.Min(10 * k, h / 2);
        Shadow(s, x, y, w, h, r, 0, 1 * k, 2 * k, 0, 0x000000, dark ? 0.45 : 0.12);
        uint t = dark ? 0x3E3F44u : 0xFFFFFFu, b = dark ? 0x2E2F33u : 0xECECECu;
        RRect(s, x, y, w, h, r, (_, py) => Mix(t, b, (py - y) / h));
        if (dark) Inset(s, x, y, w, h, r, 0, 1 * k, 0, 0, 0xFFFFFF, 0.08);
    }

    /// <summary>지금 픽셀을 떠 둔다(흐리게 할 때 되돌릴 바탕).</summary>
    public static uint[] Snap(Surf s)
    {
        var a = new uint[s.W * s.H];
        for (int i = 0; i < a.Length; i++) a[i] = s.P[i];
        return a;
    }

    /// <summary>그린 것을 떠 둔 바탕 쪽으로 t 만큼 되돌린다(쓸 수 없는 컨트롤을 흐리게).</summary>
    public static void Fade(Surf s, uint[] snap, double t)
    {
        for (int i = 0; i < snap.Length; i++) s.P[i] = 0xFF000000u | Mix(s.P[i] & 0xFFFFFF, snap[i] & 0xFFFFFF, t);
    }

    // ---------------------------------------------------------------- 글자 색(시안)

    public static uint Ink(bool dark) => dark ? 0xECECECu : 0x1F1F1Fu;          // 본문
    public static uint InkSub(bool dark) => dark ? 0xABABABu : 0x6E6E6Eu;       // 조각의 작은 글
    public static uint InkLabel(bool dark) => dark ? 0xABABABu : 0x5F5F5Fu;     // 판의 이름표(자동 잠금)
    public static uint InkIcon(bool dark) => dark ? 0xD6D6D6u : 0x4A4A4Au;      // 단추 아이콘
    public static uint InkAccent(bool dark) => dark ? 0xB6C0F8u : 0x2F3E8Fu;    // [입력]
    public static uint InkLink(bool dark) => dark ? 0xC8C8C8u : 0x4A4A4Au;      // 설정 ›
    public static uint Divider(bool dark) => dark ? 0x4A4B50u : 0xE2E2E0u;      // 조각 안 세로선
    public static uint InkNote(bool dark) => dark ? 0xABABABu : 0x646464u;      // 설정의 작은 설명(마지막으로 쓴 뒤)
    public static uint InkSeg(bool dark) => dark ? 0xB8B8B8u : 0x4A4A4Au;       // 고르지 않은 테마 칸
}
