using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 정시 알림의 플립시계(2026-10-07 사용자 결정, Codex 16:27 시안 docs/design/mascot-clips-next/round3/flip-clock.html).
/// 마스코트 위에 따로 떠 있는 큰 시계: 시·분 두 개의 검은 판, 큰 흰 숫자, 가운데 접힘선과 옆 경첩. 마스코트 키 44px 일 때 170×84(논리 px).
/// Windows 가 밝으면(작업 표시줄 테마 SystemUsesLightTheme) 밝은 겉판, 어두우면 어두운 겉판과 밝은 테두리. 숫자판은 늘 검정·흰 글자.
/// 숫자는 그림이 아니라 그리는 순간의 실제 시각이다. 분이 바뀌면 판이 반쪽씩 넘어간다(<see cref="Draw"/> 의 flip).
/// 창은 층 창(눌러도 통과, 활성화 안 함, 항상 위) — 마스코트처럼 포커스를 가져오지 않는다.
/// </summary>
internal static unsafe class FlipClock
{
    public const float LogicalW = 170, LogicalH = 84;
    private const string ClassName = "OneKeyFlipClock";
    private static nint _hwnd, _mem, _dib, _old, _bits;
    private static int _w, _h;
    private static bool _registered;

    /// <summary>
    /// 작업 표시줄 고양이·정시 시계(·그 둘이 띄우는 그림)의 색이 밝은가(0.5.15-C, 2026-10-09 사용자: 1Key 테마를 따로 둔 것은 Windows 와 별도로 쓰려는 것):
    /// 1Key 설정의 테마가 밝게/어둡게면 그것, "시스템 따름"이면 Windows 작업 표시줄 테마(<see cref="WindowsLight"/>). 시험의 ONEKEY_TEST_THEME 도 따른다.
    /// </summary>
    public static bool MascotLight() => Theme.Mode switch
    {
        1 => true,
        2 => false,
        _ => Program.IsTestMode && Environment.GetEnvironmentVariable("ONEKEY_TEST_THEME") is "light" or "dark" ? Environment.GetEnvironmentVariable("ONEKEY_TEST_THEME") == "light" : WindowsLight(),
    };

    /// <summary>Windows(작업 표시줄) 테마가 밝은가. 읽지 못하면 어두움으로 본다(작업 표시줄 기본).</summary>
    public static bool WindowsLight()
    {
        nint key = 0;
        try
        {
            fixed (char* sub = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")
                if (RegOpenKeyExW(unchecked((nint)0x80000001) /* HKCU */, sub, 0, 0x20019 /* KEY_READ */, out key) != 0) return false;
            uint v = 0, size = 4, type;
            fixed (char* name = "SystemUsesLightTheme")
                if (RegQueryValueExW(key, name, 0, out type, &v, ref size) != 0) return false;
            return v != 0;
        }
        catch { return false; }
        finally { if (key != 0) RegCloseKey(key); }
    }

    /// <summary>
    /// 시계를 bits(미리 곱한 32비트, w × h)에 그린다. s = 배율(1 = 마스코트 44px 기준). from → to 로 넘어가는 중이면 flip 0..1(1 이상 = 다 넘어감).
    /// 시·분 판마다 숫자가 다를 때만 넘어간다.
    /// </summary>
    public static bool Draw(nint bits, int w, int h, float s, (int H, int M) from, (int H, int M) to, double flip, bool light)
    {
        nint bmp = 0, g = 0;
        try
        {
            Gdiplus.Init();
            if (GdipCreateBitmapFromScan0(w, h, w * 4, 0xE200B /* PARGB */, bits, out bmp) != 0 || bmp == 0) return false;
            if (GdipGetImageGraphicsContext(bmp, out g) != 0) return false;
            GdipSetSmoothingMode(g, 4 /* AntiAlias */);
            GdipSetPixelOffsetMode(g, 4 /* Half */);
            GdipGraphicsClear(g, 0);
            float W = LogicalW * s, H = LogicalH * s, pad = 3 * s;
            // 그림자(두세 겹 옅게) → 겉판 → 테두리
            for (int i = 3; i >= 1; i--) FillRound(g, pad - i * 0.6f * s, pad + i * 1.1f * s, W - 2 * pad + i * 1.2f * s, H - 2 * pad, 13 * s, (uint)(0x12 * (4 - i)) << 24);
            uint body = light ? 0xFFF1F1F3u : 0xFF3A3A41u, edge = light ? 0xFF8E8E94u : 0xFFB4B4BEu;
            FillRound(g, pad, pad, W - 2 * pad, H - 2 * pad, 12 * s, body);
            StrokeRound(g, pad, pad, W - 2 * pad, H - 2 * pad, 12 * s, edge, 1.6f * s);
            _light = light;
            // 판 두 개 + 가운데 점
            float cw = 64 * s, ch = 58 * s, cy = (H - ch) / 2, gap = 12 * s, cx1 = (W - 2 * cw - gap) / 2, cx2 = cx1 + cw + gap;
            Card(g, cx1, cy, cw, ch, s, $"{from.H:00}", $"{to.H:00}", from.H == to.H ? 1 : flip);
            Card(g, cx2, cy, cw, ch, s, $"{from.M:00}", $"{to.M:00}", from.M == to.M ? 1 : flip);
            uint dot = light ? 0xFF55555Cu : 0xFFC8C8D0u;
            FillEllipse(g, W / 2 - 1.8f * s, H / 2 - 9 * s, 3.6f * s, 3.6f * s, dot);
            FillEllipse(g, W / 2 - 1.8f * s, H / 2 + 5.4f * s, 3.6f * s, 3.6f * s, dot);
            return true;
        }
        catch { return false; }
        finally
        {
            if (g != 0) GdipDeleteGraphics(g);
            if (bmp != 0) GdipDisposeImage(bmp);
        }
    }

    /// <summary>숫자 판 하나. 넘어가는 중이면 위 반쪽(옛 숫자)이 접힘선으로 접혀 내려가고, 이어서 아래 반쪽(새 숫자)이 펼쳐진다.</summary>
    private static void Card(nint g, float x, float y, float w, float h, float s, string a, string b, double t)
    {
        float hinge = y + h / 2;
        if (t >= 1) { Half(g, x, y, w, h, s, b, top: true, 1); Half(g, x, y, w, h, s, b, top: false, 1); }
        else
        {
            Half(g, x, y, w, h, s, b, top: true, 1);    // 위: 새 숫자가 이미 뒤에 있다
            Half(g, x, y, w, h, s, a, top: false, 1);   // 아래: 아직 옛 숫자
            if (t < 0.5) Half(g, x, y, w, h, s, a, top: true, (float)(1 - t / 0.5), shade: true);          // 옛 위 반쪽이 접혀 내려감
            else Half(g, x, y, w, h, s, b, top: false, (float)((t - 0.5) / 0.5), shade: true);             // 새 아래 반쪽이 펼쳐짐
        }
        // 접힘선과 옆 경첩
        FillRect(g, x, hinge - 0.5f * s, w, 1.0f * s, 0xFF000000);
        FillRect(g, x, hinge + 0.5f * s, w, 0.6f * s, 0x28FFFFFF);
        FillRect(g, x - 2.2f * s, hinge - 4 * s, 2.6f * s, 8 * s, 0xFF6E6E76);
        FillRect(g, x + w - 0.4f * s, hinge - 4 * s, 2.6f * s, 8 * s, 0xFF6E6E76);
    }

    /// <summary>판의 위·아래 반쪽(배경 + 숫자 그 반쪽). k = 세로 배율(접힘선을 축으로, 1 = 그대로).</summary>
    private static void Half(nint g, float x, float y, float w, float h, float s, string digits, bool top, float k, bool shade = false)
    {
        if (k <= 0.01f) return;
        float hinge = y + h / 2;
        GdipResetWorldTransform(g);
        GdipTranslateWorldTransform(g, 0, hinge, 0);
        GdipScaleWorldTransform(g, 1, k, 0);
        GdipTranslateWorldTransform(g, 0, -hinge, 0);
        GdipSetClipRect(g, x - 1, top ? y - 1 : hinge, w + 2, h / 2 + 1, 0 /* Replace */);
        // 숫자판: 밝은 겉판에서는 진회색, 어두운 겉판에서는 더 검게(겉판과 구분되게)
        FillRound(g, x, y, w, h, 6 * s, _light ? (top ? 0xFF2E2E31u : 0xFF242427u) : (top ? 0xFF161618u : 0xFF0F0F11u));
        DrawDigits(g, digits, x + w / 2, hinge, 34 * s, 0xFFFFFFFF);
        if (shade) FillRect(g, x, top ? y : hinge, w, h / 2, (uint)(Math.Clamp((1 - k) * 0.55, 0, 0.55) * 255) << 24);   // 접힐수록 어둡게
        GdipResetClip(g);
        GdipResetWorldTransform(g);
    }

    private static nint _fam;
    private static bool _light;
    /// <summary>두 숫자를 글자 모양(경로)의 실제 경계로 가운데 맞춤 — 글꼴 줄 여백과 상관없이 판 가운데. capH = 숫자 높이(px).</summary>
    private static void DrawDigits(nint g, string text, float cx, float cy, float capH, uint argb)
    {
        nint path = 0, brush = 0, state = 0;
        try
        {
            if (_fam == 0)
                foreach (string face in new[] { "Segoe UI", "Arial" })
                {
                    fixed (char* f = face) if (GdipCreateFontFamilyFromName(f, 0, out _fam) == 0 && _fam != 0) break;
                    _fam = 0;
                }
            if (_fam == 0 || GdipCreatePath(0, out path) != 0) return;
            var layout = new RECTF { Width = 1000, Height = 1000 };
            fixed (char* p = text) if (GdipAddPathString(path, p, text.Length, _fam, 1 /* Bold */, 100f, ref layout, 0) != 0) return;
            // 높이 기준은 "0"(숫자마다 경계가 달라 판마다 크기가 흔들리지 않게 늘 같은 높이로 맞춘다)
            nint probe = 0;
            RECTF b, b0;
            if (GdipGetPathWorldBounds(path, out b, 0, 0) != 0 || b.Height <= 0) return;
            if (GdipCreatePath(0, out probe) == 0)
            {
                fixed (char* p = "00") GdipAddPathString(probe, p, 2, _fam, 1, 100f, ref layout, 0);
                if (GdipGetPathWorldBounds(probe, out b0, 0, 0) != 0 || b0.Height <= 0) b0 = b;
                GdipDeletePath(probe);
            }
            else b0 = b;
            float k = capH / b0.Height;
            GdipSaveGraphics(g, out state);
            GdipTranslateWorldTransform(g, cx, cy, 0);
            GdipScaleWorldTransform(g, k, k, 0);
            GdipTranslateWorldTransform(g, -(b.X + b.Width / 2), -(b0.Y + b0.Height / 2), 0);
            if (GdipCreateSolidFill(argb, out brush) == 0) GdipFillPath(g, brush, path);
        }
        finally
        {
            if (state != 0) GdipRestoreGraphics(g, state);
            if (brush != 0) GdipDeleteBrush(brush);
            if (path != 0) GdipDeletePath(path);
        }
    }

    // ------------------------------------------------------------------ 창

    public static bool IsShown => _hwnd != 0 && Native.IsWindowVisible(_hwnd);

    /// <summary>시계를 화면 (x, y)(왼쪽 위, 물리 px)에 보이거나 고친다. s = 배율, alpha = 전체 불투명도(0~255, 나타나고 사라질 때).</summary>
    /// below: 이 창(마스코트) 바로 아래 층에 둔다(0 = 맨 위). 마스코트 뒤로 지나가게(0.3.131-G 사용자).
    public static bool Show(int x, int y, float s, (int H, int M) from, (int H, int M) to, double flip, bool light, byte alpha = 255, nint below = 0)
    {
        int w = (int)Math.Ceiling(LogicalW * s), h = (int)Math.Ceiling(LogicalH * s);
        if (!Ensure(w, h)) return false;
        if (!Draw(_bits, w, h, s, from, to, flip, light)) return false;
        var dst = new Native.POINT { x = x, y = y };
        var size = new SIZE { cx = w, cy = h };
        var zero = new Native.POINT();
        var blend = new BLENDFUNCTION { SourceConstantAlpha = alpha, AlphaFormat = 1 };
        bool ok = UpdateLayeredWindow(_hwnd, 0, ref dst, ref size, _mem, ref zero, 0, ref blend, 2);
        if (ok && !Native.IsWindowVisible(_hwnd))
        {
            Native.ShowWindow(_hwnd, 4 /* SW_SHOWNOACTIVATE */);
            Native.SetWindowPos(_hwnd, (nint)(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);   // 항상 위(활성화 없이) — 보일 때 한 번(움직이는 동안 매 장마다 하지 않게)
            if (below != 0) Native.SetWindowPos(_hwnd, below, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);   // 그다음 마스코트 바로 아래로(둘 다 항상 위 층)
        }
        return ok;
    }

    /// <summary>
    /// 기울인 시계(0.3.131-H, 2026-10-08 사용자 그림: 화면 오른쪽 아래 모서리 뒤에서 기운 채 나와 반듯하게 서고, 거꾸로 들어간다).
    /// (cx, cy) = 시계 가운데(화면 물리 px), angle = 시계 방향 기울기(도). clipRight·clipBottom 밖(작업 영역 밖)은 지운다 — 모서리 뒤에 숨은 것처럼,
    /// 옆 모니터·작업 표시줄 위로 비치지 않게. below = 이 창 바로 아래 층(마스코트 뒤로).
    /// </summary>
    public static bool ShowTilted(double cx, double cy, float s, (int H, int M) from, (int H, int M) to, double flip, bool light, float angle,
        int clipRight, int clipBottom, nint below = 0)
    {
        int w = (int)Math.Ceiling(LogicalW * s), h = (int)Math.Ceiling(LogicalH * s);
        double rad = angle * Math.PI / 180, ca = Math.Abs(Math.Cos(rad)), sa = Math.Abs(Math.Sin(rad));
        int W = (int)Math.Ceiling(w * ca + h * sa) + 4, H = (int)Math.Ceiling(w * sa + h * ca) + 4;
        int x = (int)Math.Round(cx - W / 2.0), y = (int)Math.Round(cy - H / 2.0);
        if (x >= clipRight || y >= clipBottom) { Hide(); return true; }   // 모두 모서리 뒤
        nint tmp = (nint)NativeMemory.AllocZeroed((nuint)((long)w * h * 4));
        nint src = 0, bmp = 0, g = 0;
        try
        {
            if (!Draw(tmp, w, h, s, from, to, flip, light)) return false;
            if (!Ensure(W, H)) return false;
            if (GdipCreateBitmapFromScan0(w, h, w * 4, 0xE200B /* PARGB */, tmp, out src) != 0 || src == 0) return false;
            if (GdipCreateBitmapFromScan0(W, H, W * 4, 0xE200B, _bits, out bmp) != 0 || bmp == 0) return false;
            if (GdipGetImageGraphicsContext(bmp, out g) != 0) return false;
            GdipGraphicsClear(g, 0);
            GdipSetInterpolationMode(g, 7 /* HighQualityBicubic */);
            GdipSetPixelOffsetMode(g, 4 /* Half */);
            GdipTranslateWorldTransform(g, (float)(cx - x), (float)(cy - y), 0);
            GdipRotateWorldTransform(g, angle, 0);
            GdipDrawImageRect(g, src, -w / 2f, -h / 2f, w, h);
            GdipDeleteGraphics(g); g = 0;
            // 작업 영역 밖(오른쪽·아래) 화소를 지운다
            uint* p = (uint*)_bits;
            int cr = clipRight - x, cb = clipBottom - y;
            for (int yy = 0; yy < H; yy++)
                for (int xx = 0; xx < W; xx++)
                    if (xx >= cr || yy >= cb) p[yy * W + xx] = 0;
        }
        finally
        {
            if (g != 0) GdipDeleteGraphics(g);
            if (bmp != 0) GdipDisposeImage(bmp);
            if (src != 0) GdipDisposeImage(src);
            NativeMemory.Free((void*)tmp);
        }
        var dst = new Native.POINT { x = x, y = y };
        var size = new SIZE { cx = W, cy = H };
        var zero = new Native.POINT();
        var blend = new BLENDFUNCTION { SourceConstantAlpha = 255, AlphaFormat = 1 };
        bool ok = UpdateLayeredWindow(_hwnd, 0, ref dst, ref size, _mem, ref zero, 0, ref blend, 2);
        if (ok && !Native.IsWindowVisible(_hwnd))
        {
            Native.ShowWindow(_hwnd, 4 /* SW_SHOWNOACTIVATE */);
            Native.SetWindowPos(_hwnd, (nint)(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            if (below != 0) Native.SetWindowPos(_hwnd, below, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        }
        return ok;
    }

    public static void Hide()
    {
        if (_hwnd != 0) Native.ShowWindow(_hwnd, 0);
    }

    public static void Destroy()
    {
        if (_hwnd != 0) { Native.DestroyWindow(_hwnd); _hwnd = 0; }
        FreeDib();
    }

    private static bool Ensure(int w, int h)
    {
        if (_hwnd == 0)
        {
            nint inst = Native.GetModuleHandleW(null);
            if (!_registered) { Ctl.RegisterClass(inst, ClassName, &WndProc, 0); _registered = true; }
            fixed (char* cls = ClassName)
                _hwnd = Native.CreateWindowExW(0x00080000 /* LAYERED */ | 0x08000000 /* NOACTIVATE */ | 0x00000080 /* TOOLWINDOW */ | 0x00000008 /* TOPMOST */ | 0x00000020 /* TRANSPARENT */,
                    cls, cls, 0x80000000 /* WS_POPUP */, 0, 0, w, h, 0, 0, inst, 0);
            if (_hwnd == 0) return false;
        }
        if (_mem != 0 && _w == w && _h == h) return true;
        FreeDib();
        var bih = new BIH { biSize = (uint)sizeof(BIH), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        nint screen = Native.GetDC(0);
        _mem = Native.CreateCompatibleDC(screen);
        Native.ReleaseDC(0, screen);
        if (_mem == 0) return false;
        _dib = CreateDIBSection(_mem, ref bih, 0, out _bits, 0, 0);
        if (_dib == 0 || _bits == 0) { FreeDib(); return false; }
        _old = Native.SelectObject(_mem, _dib);
        _w = w; _h = h;
        return true;
    }

    private static void FreeDib()
    {
        if (_mem != 0 && _old != 0) Native.SelectObject(_mem, _old);
        if (_dib != 0) Native.DeleteObject(_dib);
        if (_mem != 0) Native.DeleteDC(_mem);
        _mem = _dib = _old = _bits = 0; _w = _h = 0;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == 0x0021) return 3;            // WM_MOUSEACTIVATE → MA_NOACTIVATE
        if (msg == 0x0084) return -1;           // WM_NCHITTEST → 통과
        return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    /// <summary>시험 전용: 시계 그림을 PNG 로(밝음·어두움, 넘어가기 전·중간·후, 배율 1·1.5). 성공하면 true.</summary>
    internal static bool DumpForTest(string dir)
    {
        Directory.CreateDirectory(dir);
        bool ok = true;
        foreach (float s in new[] { 1f, 1.5f })
            foreach (bool light in new[] { true, false })
                foreach (double t in new[] { 0, 0.25, 0.5, 0.75, 1 })
                {
                    int w = (int)Math.Ceiling(LogicalW * s), h = (int)Math.Ceiling(LogicalH * s);
                    nint buf = Marshal.AllocHGlobal(w * h * 4);
                    nint bmp = 0;
                    try
                    {
                        ok &= Draw(buf, w, h, s, (10, 59), (11, 0), t, light);
                        if (GdipCreateBitmapFromScan0(w, h, w * 4, 0xE200B, buf, out bmp) != 0) { ok = false; continue; }
                        Guid png = new("557CF406-1A04-11D3-9A73-0000F81EF32E");
                        fixed (char* f = Path.Combine(dir, $"clock-{(light ? "light" : "dark")}-{s:0.0}-{t:0.00}.png")) ok &= GdipSaveImageToFile(bmp, f, &png, 0) == 0;
                    }
                    finally { if (bmp != 0) GdipDisposeImage(bmp); Marshal.FreeHGlobal(buf); }
                }
        return ok;
    }

    // ------------------------------------------------------------------ 도우미

    private static void FillRound(nint g, float x, float y, float w, float h, float r, uint argb)
    {
        nint path = RoundPath(x, y, w, h, r);
        if (path == 0) return;
        if (GdipCreateSolidFill(argb, out nint b) == 0) { GdipFillPath(g, b, path); GdipDeleteBrush(b); }
        GdipDeletePath(path);
    }

    private static void StrokeRound(nint g, float x, float y, float w, float h, float r, uint argb, float width)
    {
        nint path = RoundPath(x, y, w, h, r);
        if (path == 0) return;
        if (GdipCreatePen1(argb, width, 2, out nint pen) == 0) { GdipDrawPath(g, pen, path); GdipDeletePen(pen); }
        GdipDeletePath(path);
    }

    private static nint RoundPath(float x, float y, float w, float h, float r)
    {
        if (GdipCreatePath(0, out nint path) != 0) return 0;
        float d = Math.Min(2 * r, Math.Min(w, h));
        GdipAddPathArc(path, x, y, d, d, 180, 90);
        GdipAddPathArc(path, x + w - d, y, d, d, 270, 90);
        GdipAddPathArc(path, x + w - d, y + h - d, d, d, 0, 90);
        GdipAddPathArc(path, x, y + h - d, d, d, 90, 90);
        GdipClosePathFigure(path);
        return path;
    }

    private static void FillRect(nint g, float x, float y, float w, float h, uint argb)
    {
        if (GdipCreateSolidFill(argb, out nint b) == 0) { GdipFillRectangle(g, b, x, y, w, h); GdipDeleteBrush(b); }
    }

    private static void FillEllipse(nint g, float x, float y, float w, float h, uint argb)
    {
        if (GdipCreateSolidFill(argb, out nint b) == 0) { GdipFillEllipse(g, b, x, y, w, h); GdipDeleteBrush(b); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct RECTF { public float X, Y, Width, Height; }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential)] private struct BIH { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biX, biY; public uint biClrUsed, biClrImportant; }

    [DllImport("advapi32.dll")] private static extern int RegOpenKeyExW(nint key, char* sub, uint opt, uint sam, out nint result);
    [DllImport("advapi32.dll")] private static extern int RegQueryValueExW(nint key, char* name, nint reserved, out uint type, uint* data, ref uint size);
    [DllImport("advapi32.dll")] private static extern int RegCloseKey(nint key);
    [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(nint hwnd, nint hdcDst, ref Native.POINT pptDst, ref SIZE psize, nint hdcSrc, ref Native.POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint hdc, ref BIH bmi, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateBitmapFromScan0(int w, int h, int stride, int format, nint scan0, out nint bitmap);
    [DllImport("gdiplus.dll")] private static extern int GdipGetImageGraphicsContext(nint image, out nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteGraphics(nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipDisposeImage(nint image);
    [DllImport("gdiplus.dll")] private static extern int GdipGraphicsClear(nint graphics, uint argb);
    [DllImport("gdiplus.dll")] private static extern int GdipSetSmoothingMode(nint graphics, int mode);
    [DllImport("gdiplus.dll")] private static extern int GdipSetPixelOffsetMode(nint graphics, int mode);
    [DllImport("gdiplus.dll")] private static extern int GdipCreatePath(int fillMode, out nint path);
    [DllImport("gdiplus.dll")] private static extern int GdipDeletePath(nint path);
    [DllImport("gdiplus.dll")] private static extern int GdipAddPathArc(nint path, float x, float y, float w, float h, float start, float sweep);
    [DllImport("gdiplus.dll")] private static extern int GdipClosePathFigure(nint path);
    [DllImport("gdiplus.dll")] private static extern int GdipAddPathString(nint path, char* text, int length, nint family, int style, float emSize, ref RECTF layout, nint format);
    [DllImport("gdiplus.dll")] private static extern int GdipGetPathWorldBounds(nint path, out RECTF bounds, nint matrix, nint pen);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateFontFamilyFromName(char* name, nint collection, out nint family);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateSolidFill(uint argb, out nint brush);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteBrush(nint brush);
    [DllImport("gdiplus.dll")] private static extern int GdipFillPath(nint graphics, nint brush, nint path);
    [DllImport("gdiplus.dll")] private static extern int GdipFillRectangle(nint graphics, nint brush, float x, float y, float w, float h);
    [DllImport("gdiplus.dll")] private static extern int GdipFillEllipse(nint graphics, nint brush, float x, float y, float w, float h);
    [DllImport("gdiplus.dll")] private static extern int GdipCreatePen1(uint argb, float width, int unit, out nint pen);
    [DllImport("gdiplus.dll")] private static extern int GdipDeletePen(nint pen);
    [DllImport("gdiplus.dll")] private static extern int GdipDrawPath(nint graphics, nint pen, nint path);
    [DllImport("gdiplus.dll")] private static extern int GdipSetClipRect(nint graphics, float x, float y, float w, float h, int mode);
    [DllImport("gdiplus.dll")] private static extern int GdipResetClip(nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipResetWorldTransform(nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipTranslateWorldTransform(nint graphics, float dx, float dy, int order);
    [DllImport("gdiplus.dll")] private static extern int GdipScaleWorldTransform(nint graphics, float sx, float sy, int order);
    [DllImport("gdiplus.dll")] private static extern int GdipRotateWorldTransform(nint graphics, float angle, int order);
    [DllImport("gdiplus.dll")] private static extern int GdipSetInterpolationMode(nint graphics, int mode);
    [DllImport("gdiplus.dll")] private static extern int GdipDrawImageRect(nint graphics, nint image, float x, float y, float w, float h);
    [DllImport("gdiplus.dll")] private static extern int GdipSaveGraphics(nint graphics, out nint state);
    [DllImport("gdiplus.dll")] private static extern int GdipRestoreGraphics(nint graphics, nint state);
    [DllImport("gdiplus.dll")] private static extern int GdipSaveImageToFile(nint image, char* file, Guid* encoder, nint parameters);
}
