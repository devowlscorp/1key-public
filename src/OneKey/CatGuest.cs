using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 깜짝 놀잇감(0.5.21, 2026-10-10 사용자: 게임 요소 "가끔 찾아오는 손님" — 0.5.21-F 에 이름을 깜짝 놀잇감으로 — 벌칙 없이, 크기와 단순하게 이어지게).
/// 고양이가 보이는 동안 40~60분에 한 번쯤(처음은 5~10분 뒤) 작업 표시줄 위 고양이 근처에 놀잇감(나비 · 털실 공 — Codex 그림, 비눗방울 — 그릴 때 만든다)이 찾아온다.
/// 0.5.21-D(2026-10-10 사용자: 쥐를 잡는 건 너무 잔인해 보인다 — 다른 걸로): 쥐 대신 비눗방울(앞발로 치면 톡 터진다), 나비는 잡히지 않고 앞발 사이로 팔랑 날아간다.
/// 고양이는 놀잇감 쪽으로 돌아서 몇 걸음 다가가 몸을 낮추고 엉덩이를 실룩거리며 노린다(HC · HW). 사용자가 놀잇감을 누르면 고양이가 덮쳐서(땅 놀잇감 HP,
/// 나비는 위로 뛰어 HJ) 잡는다 → 하트 + 점수 +5. 누르지 않으면 20초쯤 뒤 놀잇감은 그냥 떠나고(날아가거나 굴러가거나 둥실 떠오른다) 고양이는 일어나 앉는다. 손해 없음.
/// 놀잇감 그림은 한 장씩(Assets/cat/guest_*.png) — 날갯짓 · 구르기는 그릴 때 만든다(비눗방울은 그림부터 앱이 그린다)(작업 표시줄에서 13~26 px 라 한 장을 비틀어도 충분).
/// 놀잇감 창: 층 창, 포커스를 가져가지 않고, 누를 수 있다(작은 그림 둘레에 거의 투명한 누름 자리를 둔다 — 13 px 를 정확히 누르기는 어렵다).
/// </summary>
internal static unsafe partial class CatWidget
{
    private const string GuestClass = "OneKeyCatGuest";
    private const nuint TimerGuest = 1;
    private const int GuestEnterMs = 1000, GuestLeaveMs = 900, GuestCatchMs = 320, GuestFlyAwayMs = 1100;
    private enum GuestPhase { None, Enter, Idle, Leave, Catch }

    private static bool _gRegistered;
    private static nint _gHwnd, _gMem, _gDib, _gOld, _gBits;
    private static int _gWin;                        // 창 한 변(정사각, 물리 px)
    private static uint[] _gSprite = Array.Empty<uint>();
    private static int _gS;                          // 놀잇감 그림 칸 한 변(물리 px, 미리 곱한 _gSprite)
    private static double _gCx, _gCy;                // 그림 칸 안 돌리는 가운데(털실 공 가운데 · 나비 몸)
    private static string _gKind = "";
    private static double _gX, _gGround, _gHover;    // 쉬는 자리: 그림 칸 가운데 x, 바닥 y(화면), 나비가 떠 있는 높이(가운데, 바닥에서)
    private static int _gOut;                        // 놀잇감이 고양이의 어느 쪽에 있나(+1 오른쪽) — 들어오고 나가는 쪽
    private static GuestPhase _gPhase;
    private static long _gStart, _gPhaseAt;
    private static double _gLastX, _gLastY;
    private static Action? _gOnClick;

    internal static bool GuestOpen => _gHwnd != 0;

    /// <summary>
    /// 놀잇감을 띄운다. x = 쉬는 자리의 가운데(화면), ground = 바닥 y(작업 표시줄 위 선), size = 그림 칸 한 변(물리 px), outSide = 고양이의 어느 쪽(+1 오른쪽),
    /// hover = 나비의 가운데가 바닥에서 얼마나 위인가(뛰어오른 앞발이 닿는 높이).
    /// </summary>
    private static bool GuestShow(string kind, double x, double ground, int size, int outSide, double hover, Action onClick)
    {
        GuestClose();
        if (!PrepareGuest(kind, size)) return false;
        nint hInst = Native.GetModuleHandleW(null);
        if (!_gRegistered) { Ctl.RegisterClass(hInst, GuestClass, &GuestProc, 0); _gRegistered = true; }
        double k = _dpi / 96.0;
        _gWin = (int)Math.Ceiling(Math.Max(_gS * (kind == "bubble" ? 2.0 : 1.5), 34 * k));   // 돌리기 · 톡 커지기 · 물방울 여유 + 누름 자리
        if (!MakeDib(_gWin, _gWin, out _gMem, out _gDib, out _gOld, out _gBits)) { GuestFree(); return false; }
        _gKind = kind; _gX = x; _gGround = ground; _gHover = hover; _gOut = outSide >= 0 ? 1 : -1; _gOnClick = onClick;
        fixed (char* cls = GuestClass) fixed (char* cap = "1Key")
            _gHwnd = Native.CreateWindowExW(WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST, cls, cap, Native.WS_POPUP,
                0, 0, _gWin, _gWin, 0, 0, hInst, 0);
        if (_gHwnd == 0) { GuestFree(); return false; }
        _gStart = _gPhaseAt = Environment.TickCount64; _gPhase = GuestPhase.Enter;
        GuestFrame();
        Native.ShowWindow(_gHwnd, 4 /* SW_SHOWNOACTIVATE */);
        Native.SetTimer(_gHwnd, TimerGuest, 16, 0);
        if (Program.IsTestMode) Native.SetPropW(_owner, "OneKeyTestCatGuest", _gHwnd);
        LogLine($"guest show {kind} x {x:0} ground {ground:0} size {size} side {_gOut}");
        return true;
    }

    /// <summary>놀잇감이 떠난다(날아가거나 굴러가거나 달아나고, 다 가면 창을 닫는다).</summary>
    private static void GuestLeave()
    {
        if (_gHwnd == 0 || _gPhase is GuestPhase.Leave or GuestPhase.Catch) return;
        _gPhase = GuestPhase.Leave; _gPhaseAt = Environment.TickCount64;
        LogLine("guest leave");
    }

    /// <summary>잡혔다: 톡 커졌다가 작아지며 사라진다.</summary>
    private static void GuestCatch()
    {
        if (_gHwnd == 0 || _gPhase == GuestPhase.Catch) return;
        _gPhase = GuestPhase.Catch; _gPhaseAt = Environment.TickCount64;
        LogLine("guest caught");
    }

    private static void GuestClose()
    {
        if (_gHwnd != 0) { nint h = _gHwnd; _gHwnd = 0; Native.DestroyWindow(h); if (Program.IsTestMode) Native.SetPropW(_owner, "OneKeyTestCatGuest", 0); }
        GuestFree();
        _gPhase = GuestPhase.None; _gOnClick = null;
    }

    private static void GuestFree()
    {
        if (_gMem != 0 && _gOld != 0) Native.SelectObject(_gMem, _gOld);
        if (_gDib != 0) Native.DeleteObject(_gDib);
        if (_gMem != 0) Native.DeleteDC(_gMem);
        _gMem = _gDib = _gOld = _gBits = 0;
    }

    /// <summary>놀잇감 그림(64 px)을 표시 크기 칸(size × size)으로 줄여 미리 곱한 픽셀로. 돌리는 가운데도 잰다.</summary>
    private static bool PrepareGuest(string kind, int size)
    {
        if (kind == "bubble") { _gS = Math.Max(8, size); _gSprite = BubbleSprite(_gS); _gCx = _gCy = _gS / 2.0; return true; }
        nint img = 0, bmp = 0, g = 0;
        try
        {
            if ((img = LoadPng($"guest_{kind}.png")) == 0) return false;
            GdipGetImageWidth(img, out uint sw); GdipGetImageHeight(img, out uint sh);
            _gS = Math.Max(8, size);
            var px = new uint[_gS * _gS];
            fixed (uint* p = px)
            {
                if (GdipCreateBitmapFromScan0(_gS, _gS, _gS * 4, 0xE200B /* PARGB */, (nint)p, out bmp) != 0 || bmp == 0) return false;
                if (GdipGetImageGraphicsContext(bmp, out g) != 0) return false;
                GdipSetInterpolationMode(g, 7 /* HighQualityBicubic */);
                GdipSetPixelOffsetMode(g, 2 /* HighQuality */);
                GdipGraphicsClear(g, 0);
                GdipDrawImageRectRect(g, img, 0, 0, _gS, _gS, 0, 0, sw, sh, 2 /* UnitPixel */, 0, 0, 0);
                GdipDeleteGraphics(g); g = 0;
            }
            _gSprite = px;
            // 돌리는 가운데: 짙은 부분(알파 > 160)의 테두리 상자 가운데 — 털실 공은 공 가운데(늘어진 실은 가늘어 빠진다), 나비는 몸 쪽
            int x0 = _gS, y0 = _gS, x1 = -1, y1 = -1;
            for (int y = 0; y < _gS; y++)
                for (int x = 0; x < _gS; x++)
                    if ((px[y * _gS + x] >> 24) > 160) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
            if (x1 < 0) { _gCx = _gCy = _gS / 2.0; } else { _gCx = (x0 + x1 + 1) / 2.0; _gCy = (y0 + y1 + 1) / 2.0; }
            return true;
        }
        catch { return false; }
        finally
        {
            if (g != 0) GdipDeleteGraphics(g);
            if (bmp != 0) GdipDisposeImage(bmp);
            if (img != 0) GdipDisposeImage(img);
        }
    }

    // 비눗방울 테두리의 무지갯빛(파스텔 하늘 · 분홍 · 민트 · 보라 — 밝은 · 어두운 작업 표시줄 둘 다에서 보이게 테두리는 짙게, 안은 거의 투명)
    private static readonly uint[] BubbleHues = { 0x7FC4FF, 0xFF9AD5, 0x8EEBC8, 0xB99CFF };

    private static uint BubbleHue(double ang)
    {
        double f = (ang / (Math.PI * 2) + 1) % 1 * BubbleHues.Length;
        int i = (int)f; double u = f - i;
        return Metal.Mix(BubbleHues[i % BubbleHues.Length], BubbleHues[(i + 1) % BubbleHues.Length], u);
    }

    /// <summary>비눗방울 한 장(n × n, 미리 곱한 색): 거의 투명한 안 + 무지갯빛 테두리 + 왼쪽 위 반짝임.</summary>
    private static uint[] BubbleSprite(int n)
    {
        var px = new uint[n * n];
        double c = n / 2.0, R = n * 0.44;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                double dx = x + 0.5 - c, dy = y + 0.5 - c, dist = Math.Sqrt(dx * dx + dy * dy);
                double cover = Math.Clamp(R - dist + 0.5, 0, 1);
                if (cover <= 0) continue;
                double d = dist / R, edge = Math.Exp(-Math.Pow((1 - d) / 0.13, 2));
                uint col = BubbleHue(Math.Atan2(dy, dx) + d * 1.4);
                double a = 0.08 + 0.85 * edge;                                                            // 안 8 %, 테두리 93 %(밝은 작업 표시줄에서도 보이게)
                double hx = (dx + R * 0.36) / (R * 0.24), hy = (dy + R * 0.36) / (R * 0.17);               // 반짝임(비스듬한 타원)
                double hl = Math.Max(0, 1 - (hx * hx + hy * hy));
                double h2x = (dx - R * 0.32) / (R * 0.09), h2y = (dy - R * 0.38) / (R * 0.09);
                hl = Math.Max(hl, 0.6 * Math.Max(0, 1 - (h2x * h2x + h2y * h2y)));
                if (hl > 0) { col = Metal.Mix(col, 0xFFFFFF, Math.Min(1, hl * 1.6)); a = Math.Max(a, 0.9 * Math.Min(1, hl * 1.6)); }
                a *= cover;
                uint A = (uint)Math.Round(a * 255);
                px[y * n + x] = (A << 24) | ((((col >> 16) & 255) * A / 255) << 16) | ((((col >> 8) & 255) * A / 255) << 8) | ((col & 255) * A / 255);
            }
        return px;
    }

    /// <summary>창 그림 위에 작은 물방울(가운데 cx·cy, 반지름 r, 색, 투명도) — 비눗방울이 터질 때.</summary>
    private static void GDot(Span<uint> o, int W, double cx, double cy, double r, uint rgb, double a)
    {
        int x0 = Math.Max(0, (int)(cx - r - 1)), x1 = Math.Min(W - 1, (int)(cx + r + 1)), y0 = Math.Max(0, (int)(cy - r - 1)), y1 = Math.Min(W - 1, (int)(cy + r + 1));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                double dd = Math.Sqrt((x + 0.5 - cx) * (x + 0.5 - cx) + (y + 0.5 - cy) * (y + 0.5 - cy));
                double cov = Math.Clamp(r - dd + 0.5, 0, 1) * a;
                if (cov <= 0) continue;
                uint A = (uint)Math.Round(cov * 255);
                uint c = (A << 24) | ((((rgb >> 16) & 255) * A / 255) << 16) | ((((rgb >> 8) & 255) * A / 255) << 8) | ((rgb & 255) * A / 255);
                o[y * W + x] = GOver(c, o[y * W + x]);
            }
    }

    private static double GEaseOut(double t) => 1 - (1 - t) * (1 - t) * (1 - t);
    private static double GEaseIn(double t) => t * t;

    /// <summary>
    /// 한 장: 지금 단계의 자리 · 모양을 정하고 그림 칸을 비틀어(가로 배율 · 돌리기 · 좌우 뒤집기 · 투명도) 창에 그린다.
    /// 칸 아래 끝이 바닥(땅 놀잇감), 나비는 고양이 머리쯤 높이에 떠 있다.
    /// </summary>
    private static void GuestFrame()
    {
        if (_gHwnd == 0 || _gBits == 0) return;
        long now = Environment.TickCount64;
        double t = now - _gStart, tp = now - _gPhaseAt, S = _gS, k = _dpi / 96.0;
        double dx = 0, dy = 0, sx = 1, sy = 1, rot = 0, alpha = 1;
        int face = -_gOut;                    // 쉬는 동안은 고양이 쪽을 본다(그림은 오른쪽을 본다)
        bool fly = _gKind == "butterfly", roll = _gKind == "yarn", bub = _gKind == "bubble", floaty = fly || bub;
        double popU = -1;   // 비눗방울이 터지는 중(0..1)
        double far = S * 3.2;
        switch (_gPhase)
        {
            case GuestPhase.Enter:
            {
                double u = Math.Min(1, tp / GuestEnterMs), e = GEaseOut(u);
                dx = _gOut * far * (1 - e);
                if (fly) dy = -S * 2.4 * (1 - e) + Math.Sin(u * Math.PI * 2) * S * 0.12;
                if (bub) dy = S * 0.6 * (1 - e) - Math.Sin(u * Math.PI) * S * 0.25;   // 옆에서 둥실 떠 들어온다
                if (u >= 1) { _gPhase = GuestPhase.Idle; _gPhaseAt = now; }
                break;
            }
            case GuestPhase.Idle:
                if (fly) { dx = Math.Sin(tp / 2300.0 * Math.PI * 2) * S * 0.18; dy = Math.Sin(tp / 1600.0 * Math.PI * 2) * S * 0.14; }
                else if (bub) { dx = Math.Sin(tp / 2600.0 * Math.PI * 2) * S * 0.12; dy = Math.Sin(tp / 1900.0 * Math.PI * 2) * S * 0.10; }
                else if (roll) dx = Math.Sin(tp / 2400.0 * Math.PI * 2) * S * 0.08;   // 살짝 앞뒤로 흔들
                else dy = -Math.Abs(Math.Sin(tp / 700.0 * Math.PI)) * S * 0.03;           // 킁킁(작게 들썩)
                break;
            case GuestPhase.Leave:
            {
                double u = Math.Min(1, tp / GuestLeaveMs), e = GEaseIn(u);
                dx = _gOut * far * (bub ? 0.5 : 1.3) * e;
                if (fly) dy = -S * 3.0 * e;
                if (bub) dy = -S * 2.6 * e;   // 위로 둥실 날아간다
                face = _gOut;                     // 달아나는 쪽
                alpha = u > 0.6 ? 1 - (u - 0.6) / 0.4 : 1;
                if (u >= 1) { GuestClose(); return; }
                break;
            }
            case GuestPhase.Catch:
            {
                dx = _gLastX - _gX; dy = _gLastY;
                if (fly)   // 나비: 잡히지 않고 앞발 사이로 빠져나가 팔랑팔랑 위로(바깥쪽으로) 날아간다
                {
                    double u = Math.Min(1, tp / GuestFlyAwayMs), e = GEaseIn(u);
                    dx += _gOut * S * 2.2 * e + Math.Sin(u * Math.PI * 3) * S * 0.25;
                    dy -= S * 3.4 * (0.35 * u + 0.65 * e);
                    face = _gOut;
                    alpha = u > 0.7 ? 1 - (u - 0.7) / 0.3 : 1;
                    if (u >= 1) { GuestClose(); return; }
                    break;
                }
                {
                    double u = Math.Min(1, tp / GuestCatchMs);
                    if (bub) { popU = u; sx = sy = 1 + 0.35 * u; alpha = Math.Max(0, 1 - u * 2.5); }   // 톡: 살짝 부풀며 사라지고 물방울이 튄다
                    else { double pop = u < 0.35 ? 1 + 0.25 * (u / 0.35) : 1.25 * (1 - (u - 0.35) / 0.65); sx = sy = Math.Max(0.01, pop); alpha = 1 - u * u; }
                    if (u >= 1) { GuestClose(); return; }
                }
                break;
            }
        }
        if (_gPhase != GuestPhase.Catch) { _gLastX = _gX + dx; _gLastY = dy; }
        // 움직임: 나비 날갯짓(가로로 접혔다 펴짐), 털실 구르기(간 거리만큼 돈다), 비눗방울 말랑 흔들(터질 때 물방울)
        if (fly) sx *= 0.25 + 0.75 * Math.Abs(Math.Cos(t / (_gPhase == GuestPhase.Catch ? 120.0 : 190.0) * Math.PI));   // 날아갈 때는 빠르게
        if (bub && popU < 0) { double w = Math.Sin(t / 170.0) * 0.045; sx *= 1 + w; sy *= 1 - w; }                         // 말랑말랑 흔들
        double moveX = _gLastX - _gX;
        if (roll) rot = moveX / Math.Max(1, S * 0.42);                                          // 공 반지름 ≈ 칸의 0.42
        if (_gPhase == GuestPhase.Enter && !fly) face = -_gOut;
        // 화면 자리: 그림 칸의 돌리는 가운데가 갈 곳
        double baseCy = floaty ? _gGround - _gHover : _gGround - S + _gCy;   // 땅 놀잇감: 칸 아래 끝이 바닥, 나비 · 비눗방울은 떠 있다
        double px = _gX - S / 2 + _gCx + dx, py = baseCy + dy;
        int wx = (int)Math.Round(px - _gWin / 2.0), wy = (int)Math.Round(py - _gWin / 2.0);
        double ox = px - wx, oy = py - wy;   // 창 안 가운데
        var o = new Span<uint>((void*)_gBits, _gWin * _gWin);
        o.Clear();
        // 누름 자리: 그림 둘레 원(알파 1/255 — 거의 안 보이지만 누를 수 있다). 떠나거나 잡힐 때는 없다
        if (_gPhase is GuestPhase.Enter or GuestPhase.Idle)
        {
            double r = Math.Max(S * 0.62, 15 * k), r2 = r * r;
            for (int y = 0; y < _gWin; y++)
                for (int x = 0; x < _gWin; x++)
                {
                    double ddx = x + 0.5 - ox, ddy = y + 0.5 - oy;
                    if (ddx * ddx + ddy * ddy <= r2) o[y * _gWin + x] = 0x01000000;
                }
        }
        // 그림: 거꾸로 따라가기(창 픽셀 → 그림 칸 좌표) + 쌍선형
        double cr = Math.Cos(-rot), sr = Math.Sin(-rot), fx = face < 0 ? -1 : 1;
        uint a8 = (uint)Math.Round(Math.Clamp(alpha, 0, 1) * 256);
        fixed (uint* sp = _gSprite)
            for (int y = 0; y < _gWin; y++)
                for (int x = 0; x < _gWin; x++)
                {
                    double qx = x + 0.5 - ox, qy = y + 0.5 - oy;
                    double rx = qx * cr - qy * sr, ry = qx * sr + qy * cr;
                    double u = rx / (sx * fx) + _gCx - 0.5, v = ry / sy + _gCy - 0.5;
                    if (u < -1 || v < -1 || u >= _gS || v >= _gS) continue;
                    uint c = GBilinear(sp, _gS, u, v);
                    if (c == 0) continue;
                    if (a8 < 256) c = GScale(c, a8);
                    uint d = o[y * _gWin + x], ca = c >> 24;
                    o[y * _gWin + x] = ca >= 255 ? c : GOver(c, d);
                }
        if (popU >= 0)   // 터진 물방울 일곱: 테두리에서 바깥으로 튀며 옅어진다
            for (int i = 0; i < 7; i++)
            {
                double ang = i * Math.PI * 2 / 7 + 0.4, rr = S * 0.44 * (1 + 0.9 * popU);
                GDot(o, _gWin, ox + Math.Cos(ang) * rr, oy + Math.Sin(ang) * rr + popU * popU * S * 0.2, Math.Max(0.9, S * 0.05), BubbleHue(ang), 1 - popU);
            }
        var dst = new Native.POINT { x = wx, y = wy };
        var size = new SIZE { cx = _gWin, cy = _gWin };
        var zero = new Native.POINT();
        var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
        UpdateLayeredWindow(_gHwnd, 0, ref dst, ref size, _gMem, ref zero, 0, ref blend, 2);
        if (DumpDir is not null && (_gDumpN++ & 1) == 0) DumpGuest(wx, wy);
        if ((now / 500) != ((now - 16) / 500)) Native.SetWindowPos(_gHwnd, (nint)(-1), 0, 0, 0, 0, Native.SWP_NOMOVE_ | Native.SWP_NOSIZE_ | Native.SWP_NOACTIVATE);
    }

    private static int _gDumpN;

    /// <summary>시험(ONEKEY_TEST_CAT_DUMP): 놀잇감 창 그림을 시각 · 자리와 함께(고양이 동작 장과 맞춰 한 장면으로 다시 그려 본다 — 화면을 찍지 않는다).</summary>
    private static void DumpGuest(int wx, int wy)
    {
        nint bmp = 0;
        try
        {
            Directory.CreateDirectory(DumpDir!);
            if (GdipCreateBitmapFromScan0(_gWin, _gWin, _gWin * 4, 0xE200B, _gBits, out bmp) != 0 || bmp == 0) return;
            Guid png = new("557CF406-1A04-11D3-9A73-0000F81EF32E");
            int n = _gDumpN;
            fixed (char* fp = Path.Combine(DumpDir!, $"guest_{n:00000}.png")) GdipSaveImageToFile(bmp, fp, &png, 0);
            File.AppendAllText(Path.Combine(DumpDir!, "frames.txt"), $"guest {n} win {wx},{wy} {_gWin}x{_gWin} phase {_gPhase} t {Environment.TickCount64}" + Environment.NewLine);
        }
        catch { }
        finally { if (bmp != 0) GdipDisposeImage(bmp); }
    }

    /// <summary>미리 곱한 그림의 쌍선형 표본(칸 밖은 투명).</summary>
    private static uint GBilinear(uint* p, int n, double u, double v)
    {
        int x0 = (int)Math.Floor(u), y0 = (int)Math.Floor(v);
        double fu = u - x0, fv = v - y0;
        uint At(int x, int y) => x < 0 || y < 0 || x >= n || y >= n ? 0u : p[y * n + x];
        uint c00 = At(x0, y0), c10 = At(x0 + 1, y0), c01 = At(x0, y0 + 1), c11 = At(x0 + 1, y0 + 1);
        if ((c00 | c10 | c01 | c11) == 0) return 0;
        uint r = 0;
        for (int sh = 0; sh < 32; sh += 8)
        {
            double a = ((c00 >> sh) & 255) * (1 - fu) * (1 - fv) + ((c10 >> sh) & 255) * fu * (1 - fv) + ((c01 >> sh) & 255) * (1 - fu) * fv + ((c11 >> sh) & 255) * fu * fv;
            r |= (uint)Math.Clamp((int)Math.Round(a), 0, 255) << sh;
        }
        return r;
    }

    private static uint GScale(uint c, uint a8) =>
        ((((c >> 24) * a8) >> 8) << 24) | (((((c >> 16) & 255) * a8) >> 8) << 16) | (((((c >> 8) & 255) * a8) >> 8) << 8) | (((c & 255) * a8) >> 8);

    /// <summary>미리 곱한 c 를 d 위에.</summary>
    private static uint GOver(uint c, uint d)
    {
        uint ia = 255 - (c >> 24);
        uint a = (c >> 24) + (((d >> 24) * ia) / 255), r = ((c >> 16) & 255) + ((((d >> 16) & 255) * ia) / 255);
        uint g = ((c >> 8) & 255) + ((((d >> 8) & 255) * ia) / 255), b = (c & 255) + (((d & 255) * ia) / 255);
        return (Math.Min(a, 255u) << 24) | (Math.Min(r, 255u) << 16) | (Math.Min(g, 255u) << 8) | Math.Min(b, 255u);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint GuestProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            switch (msg)
            {
                case 0x0113:   // WM_TIMER
                    if (hwnd == _gHwnd) GuestFrame(); else Native.KillTimer(hwnd, TimerGuest);
                    return 0;
                case Native.WM_MOUSEACTIVATE:
                    return Native.MA_NOACTIVATE;
                case 0x0020:   // WM_SETCURSOR: 손 모양
                    Native.SetCursor(Native.LoadCursorW(0, (nint)32649 /* IDC_HAND */));
                    return 1;
                case 0x0201:   // WM_LBUTTONDOWN: 잡아!
                    if (hwnd == _gHwnd && _gPhase is GuestPhase.Enter or GuestPhase.Idle) { LogLine("guest click"); _gOnClick?.Invoke(); }
                    return 0;
                case Native.WM_DESTROY:
                    Native.KillTimer(hwnd, TimerGuest);
                    break;
            }
        }
        catch { }
        return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
