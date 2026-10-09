using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 공개판의 작업 표시줄 마스코트: 고양이(Codex 시안, 9방향 시선 × 밝은·어두운 테마). 걷지 않고 작업 영역 오른쪽 아래(알림 영역 위)에 앉아
/// 마우스 커서 쪽을 쳐다본다. 커서가 고양이 위에 있거나 한동안 움직이지 않으면 정면, 정시 알림 동안은 위(머리 위의 시계)를 본다.
/// 시선이 바뀔 때는 두 그림을 잠깐 섞는다. 그림 색은 Windows 테마(밝음 = 회색 고양이, 어두움 = 검은 고양이)를 따른다.
/// 창·환경·클릭 규칙은 <see cref="Walker"/> 와 같다(층 창, 항상 위, 활성화 없음, 환경은 <see cref="EnvOk(out Native.RECT, out int, out int)"/>).
/// Walker 는 걷기 그림(mascot_walk)이 없고 고양이 그림이 있으면 이 클래스로 넘긴다(<see cref="Use"/>).
/// </summary>
internal static unsafe partial class CatWidget
{
    public const string ClassName = "OneKeyCat";
    private static readonly string[] Names = { "up-left", "up", "up-right", "left", "center", "right", "down-left", "down", "down-right" };
    private const int Center = 4, Up = 1;
    private const int HeightLogical = 52, MarginLogical = 18;
    private const int GazeMs = 100, FastMs = 16, FadeMs = 160, CheckMs = 2000, IdleMs = 20000;
    private const nuint TimerTick = 1, TimerCheck = 2;

    private static nint _hwnd, _owner;
    private static uint _clickMsg;
    private static bool _registered, _wanted, _shown, _pressed, _light;
    private static int _showGen, _why = 1, _dpi, _tickMs;
    // 그림 띠: 9칸(_w × _h), 미리 곱한 알파
    private static nint _mem, _dib, _old, _bits;
    private static int _w, _h;
    // 내보낼 그림(_w × _h)
    private static nint _xmem, _xdib, _xold, _xbits;
    // 고개 돌리기 사이 장(0.5.15-R, 2026-10-09 사용자: "고개를 돌릴 때 딱딱 끊어지고 잔상도 있다" — 두 시선 그림을 0.16초 겹쳐 바꿨다):
    // 이웃한 두 시선(가로·세로·대각 한 칸) 20쌍마다 RIFE 사이 장 3장(1/4·2/4·3/4) = 60칸 띠(cat_<테마>_tw.png, work/cat-motions/flf/gaze_tweens.py).
    // 멀리 돌 때는 한 칸씩 지나간다(왼쪽 → 정면 → 오른쪽). 한 칸 = 사이 3장 + 도착 그림, 장마다 TweenStepMs. 띠가 없으면 예전처럼 섞기
    private static nint _tmem, _tdib, _told, _tbits;
    // 띠(cat_<테마>_tw.jpg + _tw_a.png, 192 높이)는 66칸: 0~59 = 시선 20쌍, 60~65 = 입 모양 2쌍(잠금 위젯의 야옹 — MascotGreet)
    internal const int TweenCells = 60, TweenStripCells = 66, TweenStepMs = 40;
    private static int[] _seq = Array.Empty<int>();   // 0..8 = 시선 그림, -1-i = 사이 장 i
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
    public static bool AcceptClick(int gen) => _shown && gen == _showGen && !_pressed && EnvOk(out _, out _);

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
        // 0.5.15-K: Windows 는 설정 변경 알림(WM_SETTINGCHANGE)을 환경 변수·지역 설정 등 아무 설정에나 보낸다. 보이는 중이고 작업 영역·배율·색이
        // 그대로면 다시 맞추지 않는다 — 하던 동작(걷기·쉬는 동작·숨기)이 중간에 끊겼다(catseq 에서 79/98 장에서 끊김)
        if (_shown && EnvOk(out Native.RECT w, out int d) && d == _dpi && w.left == _work.left && w.right == _work.right && w.bottom == _work.bottom
            && FlipClock.MascotLight() == _light) { LogLine("envchanged same"); KeepOnTop(); return; }
        LogLine("envchanged redo");
        if (_shown) Hide(keepCheck: true);
        FreeArt();
        Evaluate();
    }

    public static void Destroy()
    {
        FlushTestLog();
        _wanted = false;
        HideAll();
        if (_hwnd != 0) { Native.DestroyWindow(_hwnd); _hwnd = 0; }
        FreeArt();
    }

    // ------------------------------------------------------------------ 보이기·숨기기

    private static void Evaluate()
    {
        if (!_wanted) { HideAll(); return; }
        if (!EnvOk(out Native.RECT work, out int dpi, out int why))
        {
            _why = why;   // 숨기 전에(시험 기록에 이유가 남게)
            if (_shown) Hide(keepCheck: true);
            RetryLater();
            return;
        }
        bool light = FlipClock.MascotLight();
        if (_shown && dpi == _dpi && light == _light && work.left == _work.left && work.right == _work.right && work.bottom == _work.bottom) return;
        if (_shown) Hide(keepCheck: true);
        if (!EnsureWindow()) { _why = 9; SetProps(); return; }
        if (_mem == 0 || dpi != _dpi || light != _light) { FreeArt(); _dpi = dpi; _light = light; if (!BuildArt()) { FreeArt(); _why = 8; RetryLater(); return; } }
        _work = work;
        _posX = int.MinValue;   // 보일 때마다 집에서
        _gaze = _from = GazeNow(Environment.TickCount64); _pending = -1; _fadeStart = 0;
        if (!Render()) { FreeArt(); _why = 11; RetryLater(); return; }
        _shown = true; _why = 0; _showGen++;
        Native.ShowWindow(_hwnd, 4 /* SW_SHOWNOACTIVATE */);
        KeepOnTop();
        Native.KillTimer(_hwnd, TimerCheck);
        _lastCheck = Environment.TickCount64;
        SetTick(GazeMs);
        ScheduleNextClip(Environment.TickCount64);
        SetProps();
    }

    private static void Hide(bool keepCheck)
    {
        if (_clipOn || _clipLoading || _peekOn) LogLine($"hide during motion why {_why} wanted {_wanted}");   // 시험 기록: 동작이 중간에 끊긴 이유
        CancelPress();
        StopClip();
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

    // ------------------------------------------------------------------ 환경

    /// <summary>
    /// 보여도 되는 환경인가. 주 모니터의 작업 영역·배율을 돌려준다. 알림을 받는 상태가 아니거나(전체 화면·발표·바쁨·확인 실패), 작업 표시줄이
    /// 아래가 아니거나 자동 숨김이거나 주 모니터가 아니거나, 시스템 패널이 앞에 있거나, 무엇이든 확인하지 못하면 false(숨김 쪽, Codex Q1·Q2·Q3).
    /// </summary>
    internal static bool EnvOk(out Native.RECT work, out int dpi) => EnvOk(out work, out dpi, out _);

    internal static bool EnvOk(out Native.RECT work, out int dpi, out int why)
    {
        work = default; dpi = 0; why = 10;
        try
        {
            if (Program.IsTestMode)
            {
                string dir = Config.Dir;
                if (File.Exists(Path.Combine(dir, "test-walker-busy"))) { why = 2; return false; }   // 합성 상태 주입
                if (File.Exists(Path.Combine(dir, "test-walker-panel"))) { why = 6; return false; }
            }
            why = 2;
            if (SHQueryUserNotificationState(out int qs) != 0 || qs != 5 /* QUNS_ACCEPTS_NOTIFICATIONS */) return false;
            why = 3;
            var abd = new APPBARDATA { cbSize = (uint)sizeof(APPBARDATA) };
            if (SHAppBarMessage(5 /* ABM_GETTASKBARPOS */, ref abd) == 0 || abd.uEdge != 3 /* ABE_BOTTOM */) return false;
            var st = new APPBARDATA { cbSize = (uint)sizeof(APPBARDATA) };
            if ((SHAppBarMessage(4 /* ABM_GETSTATE */, ref st) & 1 /* ABS_AUTOHIDE */) != 0) return false;
            why = 4;
            nint primary = MonitorFromPoint(new Native.POINT { x = 0, y = 0 }, 1 /* MONITOR_DEFAULTTOPRIMARY */);
            if (primary == 0 || MonitorFromRect(ref abd.rc, 0 /* NULL */) != primary) return false;
            var mi = new Native.MONITORINFO { cbSize = (uint)sizeof(Native.MONITORINFO) };
            if (!Native.GetMonitorInfoW(primary, ref mi)) return false;
            if (Math.Abs(mi.rcWork.bottom - abd.rc.top) > 2) return false;   // 작업 영역 아래 끝이 작업 표시줄 위 가장자리가 아니면(예상 밖 배치)
            why = 5;
            if (GetDpiForMonitor(primary, 0 /* MDT_EFFECTIVE_DPI */, out uint dx, out _) != 0 || dx is < 48 or > 480) return false;
            why = 6;
            if (SystemPanelUp()) return false;
            why = 7;
            work = mi.rcWork; dpi = (int)dx;
            int fh = (int)Math.Round(HeightLogical * dpi / 96.0);
            if ((work.right - work.left) / 5 < fh * 5 || work.bottom - work.top < fh * 3) return false;   // 좁은 작업 영역
            why = 0;
            return true;
        }
        catch { why = 10; return false; }
    }

    /// <summary>시작 메뉴·검색·알림/빠른 설정·작업 표시줄 메뉴·넘침 영역 등이 앞에 있으면 true. 앞 창이 없어도 true(모르면 숨김).</summary>
    [StructLayout(LayoutKind.Sequential)] private struct LASTINPUTINFO { public uint cbSize, dwTime; }
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    private static bool SystemPanelUp()
    {
        nint fg = Native.GetForegroundWindow();
        if (fg == 0) return true;
        string cls = Native.GetClassName(fg);
        // 작업 표시줄 자체가 앞이면 숨지 않는다(0.5.15-Z, 2026-10-09 사용자: 작업 표시줄을 누르면 고양이가 사라진다 — 예전에는 누른 뒤 5초 숨었다).
        // 작업 표시줄의 메뉴·점프 목록·미리보기·넘침 영역·달력은 아래의 자기 창 이름으로 숨는다. 눌러서 작업 표시줄이 고양이 위로 올라오면 KeepOnTop 이 되돌린다
        if (cls is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        if (cls is "Windows.UI.Core.CoreWindow" or "NotifyIconOverflowWindow" or "TopLevelWindowForOverflowXamlIsland"
            or "Xaml_WindowedPopupClass" or "#32768" or "XamlExplorerHostIslandWindow" or "MultitaskingViewFrame" or "ForegroundStaging" or "TaskListThumbnailWnd"
            or "Windows.UI.Input.InputSite.WindowClass" or "ControlCenterWindow" or "LauncherTipWnd") return true;
        // 떠 있는 팝업 메뉴(#32768)가 하나라도 보이면(다른 앱의 메뉴 포함 — 아래쪽을 가릴 수 있다). 1Key 자신의 메뉴(고양이 오른쪽 클릭·트레이)는 빼고
        nint menu = FindWindowW("#32768", null);
        if (menu == 0 || !Native.IsWindowVisible(menu)) return false;
        GetWindowThreadProcessId(menu, out uint pid);
        return pid != (uint)Environment.ProcessId;
    }

    // ------------------------------------------------------------------ 그림

    /// <summary>쌍 번호: a &lt; b 이고 두 칸이 체비쇼프 거리 1 인 쌍을 (a, b) 차례로 센 번호(gaze_tweens.py 의 PAIRS 와 같다). 이웃이 아니면 -1.</summary>
    private static readonly int[] TweenPair = MakeTweenPairs();
    private static int[] MakeTweenPairs()
    {
        var t = new int[81]; Array.Fill(t, -1); int n = 0;
        for (int a = 0; a < 9; a++)
            for (int b = a + 1; b < 9; b++)
                if (Math.Max(Math.Abs(a / 3 - b / 3), Math.Abs(a % 3 - b % 3)) == 1) { t[a * 9 + b] = t[b * 9 + a] = n++; }
        return t;
    }

    /// <summary>from → to 로 고개를 돌리는 장들: 한 칸씩(가로·세로 함께 움직여 대각도 한 칸) 사이 3장 + 도착 시선. 사이 장 띠가 없으면 빈 배열.</summary>
    private static int[] TweenPath(int from, int to)
    {
        if (_tbits == 0 || from == to) return Array.Empty<int>();
        var seq = new List<int>();
        int cur = from;
        while (cur != to)
        {
            int r = cur / 3 + Math.Sign(to / 3 - cur / 3), c = cur % 3 + Math.Sign(to % 3 - cur % 3), next = r * 3 + c;
            int p = TweenPair[cur * 9 + next];
            if (p < 0) return Array.Empty<int>();
            for (int j = 0; j < 3; j++) seq.Add(-1 - (3 * p + (cur < next ? j : 2 - j)));
            seq.Add(next);
            cur = next;
        }
        return seq.ToArray();
    }

    /// <summary>시선을 바꾸는 데 걸리는 시간: 사이 장이 있으면 장 수 × TweenStepMs, 없으면 섞기 FadeMs.</summary>
    private static int TransMs => _seq.Length > 0 ? _seq.Length * TweenStepMs : FadeMs;

    /// <summary>사이 장 띠(60칸)를 표시 크기(_w × _h)로 줄여 따로 둔다. 없거나 실패하면 _tbits = 0(섞기로 돌아간다).</summary>
    private static void BuildTweens(string th)
    {
        nint img = 0, bmp = 0, g = 0;
        try
        {
            if ((img = LoadPremulStrip($"cat_{th}_tw")) == 0) return;
            GdipGetImageWidth(img, out uint sw); GdipGetImageHeight(img, out uint sh);
            if (sw < TweenStripCells || sh == 0) return;
            int cw = (int)(sw / TweenStripCells), TW = _w * TweenCells;
            if (!MakeDib(TW, _h, out _tmem, out _tdib, out _told, out _tbits)) { _tbits = 0; return; }
            if (GdipCreateBitmapFromScan0(TW, _h, TW * 4, 0xE200B /* PixelFormat32bppPARGB */, _tbits, out bmp) != 0 || bmp == 0) { FreeTweens(); return; }
            if (GdipGetImageGraphicsContext(bmp, out g) != 0) { FreeTweens(); return; }
            GdipSetInterpolationMode(g, 7 /* HighQualityBicubic */);
            GdipSetPixelOffsetMode(g, 2 /* HighQuality */);
            GdipGraphicsClear(g, 0);
            for (int i = 0; i < TweenCells; i++)
                GdipDrawImageRectRectI(g, img, i * _w, 0, _w, _h, i * cw, 0, cw, (int)sh, 2 /* UnitPixel */, 0, 0, 0);
        }
        catch { FreeTweens(); }
        finally
        {
            if (g != 0) GdipDeleteGraphics(g);
            if (bmp != 0) GdipDisposeImage(bmp);
            if (img != 0) GdipDisposeImage(img);
        }
    }

    private static void FreeTweens()
    {
        if (_tmem != 0 && _told != 0) Native.SelectObject(_tmem, _told);
        if (_tdib != 0) Native.DeleteObject(_tdib);
        if (_tmem != 0) Native.DeleteDC(_tmem);
        _tmem = _tdib = _told = _tbits = 0;
        _seq = Array.Empty<int>();
    }

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
            BuildTweens(th);
            MeasureSit();   // 동작 그림을 앉은 고양이에 맞춘다(CatClips.cs)
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
        StopClip(); _sitTop = -1;
        if (_xmem != 0 && _xold != 0) Native.SelectObject(_xmem, _xold);
        if (_xdib != 0) Native.DeleteObject(_xdib);
        if (_xmem != 0) Native.DeleteDC(_xmem);
        _xmem = _xdib = _xold = _xbits = 0;
        if (_mem != 0 && _old != 0) Native.SelectObject(_mem, _old);
        if (_dib != 0) Native.DeleteObject(_dib);
        if (_mem != 0) Native.DeleteDC(_mem);
        _mem = _dib = _old = _bits = 0;
        FreeTweens();
        _dpi = 0;
    }

    /// <summary>앉은 고양이 창의 왼쪽: 집(작업 표시줄 오른쪽 끝) 또는 걷기로 간 자리(다니는 범위 안 — CatClips.cs).</summary>
    private static int WinX => _posX == int.MinValue ? HomeX : Math.Clamp(_posX, MinX, HomeX);
    /// <summary>
    /// 앉은 고양이 창의 위: 작업 표시줄 위 선보다 <see cref="SitSinkPx"/> 만큼 아래까지(0.5.15-J, 2026-10-09 사용자: 정면에서 보면 뒷다리(옆다리)가 앞발보다
    /// 높아 앞발을 선에 맞추면 옆다리가 떠 보인다 — 살짝 내려 앞발이 작업 표시줄과 겹치게). 9방향 그림에서 잰 차이는 앉은 키의 10~14 %.
    /// </summary>
    private static int WinY => _work.bottom - _h + SitSinkPx;
    private static int SitSinkPx => _sitTop < 0 ? 0 : (int)Math.Round((_sitBot - _sitTop) * 0.11);

    /// <summary>지금 시선 그림(시선이 막 바뀌었으면 앞 그림과 섞어서)을 제자리에 내보낸다.</summary>
    private static bool Render()
    {
        if (_hwnd == 0 || !Compose()) return false;
        var dst = new Native.POINT { x = WinX, y = WinY };
        var size = new SIZE { cx = _w, cy = _h };
        var zero = new Native.POINT();
        var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 /* AC_SRC_ALPHA */ };
        bool done = UpdateLayeredWindow(_hwnd, 0, ref dst, ref size, _xmem, ref zero, 0, ref blend, 2 /* ULW_ALPHA */);
        if (FullTest)
        {
            long since = Environment.TickCount64 - _fadeStart;
            int code = _from != _gaze && _seq.Length > 0 ? _seq[(int)Math.Min(_seq.Length - 1, Math.Max(0, since / TweenStepMs))] : _gaze;
            DumpWin(_xbits, _w, _h, _w, WinX, WinY, $"still gaze {_gaze} from {_from} code {code} sink {_sink} squash {_squash:0.000} lean {_lean}");
        }
        return done;
    }

    private static bool Compose()
    {
        if (_bits == 0 || _xbits == 0) return false;
        long since = Environment.TickCount64 - _fadeStart;
        // 고개 돌리기: 사이 장이 있으면 지금 장 하나를 그대로(섞지 않는다 — 두 얼굴이 겹쳐 보이던 잔상), 없으면 예전처럼 두 그림을 섞는다
        int cell = _gaze, stride = _w * Names.Length;
        uint* src = (uint*)_bits, o = (uint*)_xbits;
        bool tween = _from != _gaze && _seq.Length > 0 && _tbits != 0;
        if (tween)
        {
            int code = _seq[(int)Math.Min(_seq.Length - 1, Math.Max(0, since / TweenStepMs))];
            if (code < 0) { src = (uint*)_tbits; stride = _w * TweenCells; cell = -1 - code; }
            else cell = code;
        }
        bool fade = !tween && _from != _gaze && since < FadeMs;
        uint t = fade ? (uint)(since * 256 / FadeMs) : 256, u = 256 - t;
        int W = stride;
        uint* gz = (uint*)_bits; int GW = _w * Names.Length;
        // 숨는 동안 자르는 선: 처음에는 창 아래 끝(앞발이 작업 표시줄과 겹친 그대로), 내려가기 시작하면 작업 표시줄 위 선까지 올라온다(그 밑으로 숨는다)
        int cutY = PeekCutLine();   // 0.5.15-Y: 앞발 아래(창 아래 끝)에서 자른다 — CatClips.PeekCutLine
        for (int y = 0; y < _h; y++)
        {
            // 숨기(Peek): 그림을 _sink 만큼 아래로(창 아래 끝 = 작업 표시줄 위 선 밑으로 들어간 줄은 그리지 않는다), 발을 기준으로 세로 _squash 배
            // (1 = 그대로, 웅크리면 1 보다 작게), 옆으로 _lean 만큼(두리번거릴 때 그쪽으로 몸을 살짝)
            int sy = _squash == 1.0 ? y - _sink : _h - 1 - (int)Math.Round((_h - 1 - (y - _sink)) / _squash);
            if (sy < 0 || sy >= _h || y >= cutY) { new Span<uint>(o + y * _w, _w).Clear(); continue; }
            uint* b = src + sy * W + cell * _w, a = gz + sy * GW + _from * _w, d = o + y * _w;
            if (_lean != 0)
            {
                for (int x = 0; x < _w; x++)
                {
                    int sx = x - _lean;
                    if (sx < 0 || sx >= _w) { d[x] = 0; continue; }
                    uint p = a[sx], q = b[sx];
                    d[x] = !fade ? q : (((p >> 24) * u + (q >> 24) * t) >> 8 << 24) | ((((p >> 16) & 255) * u + ((q >> 16) & 255) * t) >> 8 << 16)
                         | ((((p >> 8) & 255) * u + ((q >> 8) & 255) * t) >> 8 << 8) | (((p & 255) * u + (q & 255) * t) >> 8);
                }
                continue;
            }
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
        if (_peekOn) return PeekGaze(now);
        if (FullGazeNow(now) is int fg) return fg;   // 전체 시험의 고개 돌리기 차례
        if (_wantFront) return Center;   // 다음 동작을 하려고 정면을 본다(CatClips.MaybeStartClip)
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
        if (_clipOn || _clipLoading) return;   // 동작 중에는 시선·환경 점검을 쉰다(틱 스레드가 그린다, CatClips.cs)
        long now = Environment.TickCount64;
        // 2초마다 환경을 다시 본다(전체 화면·시스템 패널·작업 표시줄·테마 바뀜). 시계가 움직이는 동안은 미룬다(끊기지 않게)
        if (_clock == ClockPhase.None && now - _lastCheck >= CheckMs)
        {
            _lastCheck = now;
            bool redo = !EnvOk(out Native.RECT work, out int dpi) || dpi != _dpi || work.bottom != _work.bottom || work.left != _work.left
                || work.right != _work.right || FlipClock.MascotLight() != _light;
            if (redo) { Evaluate(); return; }
            KeepOnTop();
        }
        if (_pressed) return;
        var dt = DateTime.Now;
        if (_clock == ClockPhase.None && ClockDue(dt)) { if (_peekOn) EndPeek(); StartClock(dt); }
        if (_clock != ClockPhase.None) ClockTick(now, dt);
        bool peekMoved = _peekOn && PeekTick(now);
        // 시선: 같은 쪽이 두 번 이어서 나와야 바꾼다(경계에서 왔다 갔다 하지 않게). 시계가 나오면 바로 위
        int g = GazeNow(now);
        bool fading = _from != _gaze;   // 섞는 중에는 새 시선을 받지 않는다(끝나면 다음 틱에)
        if (g == _gaze) _pending = -1;
        else if (!fading && (g == _pending || _clock != ClockPhase.None)) { _from = _gaze; _gaze = g; _fadeStart = now; _pending = -1; _seq = TweenPath(_from, _gaze); }
        else if (!fading) _pending = g;
        bool anim = _from != _gaze;
        if (anim || peekMoved)
        {
            Render();   // 섞기가 끝난 시각이면 Compose 가 새 그림만 그린다
            if (anim && now - _fadeStart >= TransMs) _from = _gaze;
        }
        SetTick(anim || _peekOn || _clock != ClockPhase.None ? FastMs : GazeMs);
        if (!anim && !_peekOn && _clock == ClockPhase.None) MaybeStartClip(now);   // 정시 시계 앞(:59:20 부터)에는 시작하지 않는다(MaybeStartClip)
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
        Native.SetPropW(_owner, "OneKeyTestCatLight", _shown ? (_light ? 2 : 1) : 0);
        Native.SetPropW(_owner, "OneKeyTestCatX", _shown ? WinX : 0);
        Native.SetPropW(_owner, "OneKeyTestCatPeeks", _peekCount);   // 앉은 고양이 창 왼쪽(걷기 뒤 자리 — catwalk.ps1)   // 그린 고양이 색: 2 = 밝은 회색, 1 = 검은 고양이(cattheme.ps1)
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
        // 잠금 위젯 인사 띠(둘러보기 + 야옹, 지금 테마)
        nint greet = MascotGreet.Load(out int gn, FlipClock.MascotLight());
        if (greet == 0) { File.WriteAllText(log, "greet"); return false; }
        Guid png = new("557CF406-1A04-11D3-9A73-0000F81EF32E");
        fixed (char* gp = Path.Combine(dir, "cat-greet.png")) GdipSaveImageToFile(greet, gp, &png, 0);
        GdipDisposeImage(greet);
        lines.Add($"greet frames {gn} pad {MascotGreet.Pad} look {MascotGreet.Look.Length} meow {MascotGreet.Meow.Length} chain {MascotGreet.Chain.Length} blink {MascotGreet.Blink.Length} blep {MascotGreet.Blep.Length} ears {MascotGreet.Ears.Length}");
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

    /// <summary>실행 파일에 그 PNG 가 들어 있는가.</summary>
    /// <summary>
    /// 미리 곱한 색 JPEG(&lt;name&gt;.jpg) + 투명도 PNG(&lt;name&gt;_a.png)를 한 장의 PARGB GDI+ 비트맵으로(동작 그림 띠·사이 장 띠와 같은 저장 —
    /// RGBA PNG 보다 3~4배 작다). 없거나 크기가 다르면 0. 받은 쪽이 GdipDisposeImage.
    /// </summary>
    internal static nint LoadPremulStrip(string name)
    {
        nint col = 0, alp = 0, bmp = 0;
        try
        {
            if ((col = LoadPng($"{name}.jpg")) == 0 || (alp = LoadPng($"{name}_a.png")) == 0) return 0;
            GdipGetImageWidth(col, out uint w); GdipGetImageHeight(col, out uint h);
            GdipGetImageWidth(alp, out uint aw); GdipGetImageHeight(alp, out uint ah);
            if (w == 0 || h == 0 || aw != w || ah != h) return 0;
            if (GdipCreateBitmapFromScan0((int)w, (int)h, 0, 0xE200B /* PARGB */, 0, out bmp) != 0 || bmp == 0) return 0;
            var rc = new GpRect { X = 0, Y = 0, Width = (int)w, Height = (int)h };
            var cd = new BitmapData(); var ad = new BitmapData(); var dd = new BitmapData();
            if (GdipBitmapLockBits(col, ref rc, 1, 0x26200A, &cd) != 0) return 0;
            if (GdipBitmapLockBits(alp, ref rc, 1, 0x26200A, &ad) != 0) { GdipBitmapUnlockBits(col, &cd); return 0; }
            if (GdipBitmapLockBits(bmp, ref rc, 2 /* WriteOnly */, 0xE200B, &dd) != 0) { GdipBitmapUnlockBits(col, &cd); GdipBitmapUnlockBits(alp, &ad); return 0; }
            for (int y = 0; y < (int)h; y++)
            {
                uint* c = (uint*)((byte*)cd.Scan0 + y * cd.Stride), a = (uint*)((byte*)ad.Scan0 + y * ad.Stride), d = (uint*)((byte*)dd.Scan0 + y * dd.Stride);
                for (int x = 0; x < (int)w; x++)
                {
                    uint av = (a[x] >> 16) & 255, cv = c[x];
                    if (av < 8) { d[x] = 0; continue; }
                    uint r = Math.Min((cv >> 16) & 255, av), gg = Math.Min((cv >> 8) & 255, av), bb = Math.Min(cv & 255, av);   // JPEG 오차로 투명도를 넘지 않게
                    d[x] = (av << 24) | (r << 16) | (gg << 8) | bb;
                }
            }
            GdipBitmapUnlockBits(col, &cd); GdipBitmapUnlockBits(alp, &ad); GdipBitmapUnlockBits(bmp, &dd);
            nint ok = bmp; bmp = 0;
            return ok;
        }
        catch { return 0; }
        finally
        {
            if (col != 0) GdipDisposeImage(col);
            if (alp != 0) GdipDisposeImage(alp);
            if (bmp != 0) GdipDisposeImage(bmp);
        }
    }

    internal static bool HasPng(string name) => typeof(CatWidget).Assembly.GetManifestResourceInfo(name) is not null;

    /// <summary>LoadPng 로 받은 이미지를 놓는다.</summary>
    internal static void FreeImage(nint img) { if (img != 0) GdipDisposeImage(img); }

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
                    bool inside = x >= 0 && y >= 0 && x < CurW && y < CurH;
                    _pressed = false;
                    if (Native.GetCapture() == hwnd) Native.ReleaseCapture();
                    if (was && inside && _shown && _owner != 0)
                    {
                        if (EnvOk(out _, out _)) Native.PostMessageW(_owner, _clickMsg, _showGen, 0);
                        else Evaluate();
                    }
                    return 0;
                }
                case 0x0204:             // WM_RBUTTONDOWN
                    return 0;
                case 0x0205:             // WM_RBUTTONUP: 오른쪽 클릭 메뉴(0.5.15-V, 2026-10-09 사용자) — 앱이 메뉴를 띄운다(lParam 1)
                    if (_shown && _owner != 0 && !_pressed && EnvOk(out _, out _)) Native.PostMessageW(_owner, _clickMsg, _showGen, 1);
                    return 0;
                case 0x0215:             // WM_CAPTURECHANGED
                    _pressed = false;
                    return 0;
                case (int)WM_ANIMTICK:   // 동작 틱(CatClips.cs의 틱 스레드)
                    Interlocked.Exchange(ref _tickPending, 0);
                    if ((int)wParam == Volatile.Read(ref _tickGen)) ClipTick();
                    return 0;
                case (int)WM_CLIPREADY:
                    OnClipReady((int)wParam);
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

    [StructLayout(LayoutKind.Sequential)] private struct APPBARDATA { public uint cbSize; public nint hWnd; public uint uCallbackMessage, uEdge; public Native.RECT rc; public nint lParam; }
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
    [DllImport("shell32.dll")] private static extern nuint SHAppBarMessage(uint msg, ref APPBARDATA data);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(Native.POINT pt, uint flags);
    [DllImport("user32.dll")] private static extern nint MonitorFromRect(ref Native.RECT rc, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowW(string? cls, string? title);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
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
