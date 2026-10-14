namespace OneKey;

/// <summary>
/// 여러 모니터(0.5.33, 2026-10-14 사용자: 확장 모니터로도 옮길 수 있게): 고양이는 한 번에 한 모니터에서 지낸다(<see cref="_work"/> = 그 모니터의 작업 영역).
/// 보일 때는 집 모니터(CatGrowth.HomeMonitor — 다시 켜도 기억, 없거나 그 모니터를 뺐으면 주 모니터)에서 시작한다. 다른 모니터에 끌어다 놓거나
/// 앉은 창이 다른 모니터로 옮겨지면 그 모니터로 간다(배율이 다르면 그림을 그 배율로 다시 만든다). 그 모니터의 작업 표시줄에 앉으면 그 모니터가 새 집.
/// 작업 표시줄이 없는 모니터(모든 디스플레이에 작업 표시줄 표시 끔)에서는 화면 아래 끝(= 작업 영역 아래 끝)에 앉는다(사용자 결정).
/// </summary>
internal static unsafe partial class CatWidget
{
    // 지금 쓰는 모니터: _monOther 면 _monAt(그 모니터 한가운데 점, 가상 화면 좌표)이 있는 모니터, 아니면 주 모니터
    private static Native.POINT _monAt;
    private static bool _monOther;

    private static nint PrimaryMonitor() => MonitorFromPoint(new Native.POINT { x = 0, y = 0 }, 1 /* MONITOR_DEFAULTTOPRIMARY */);

    /// <summary>지금 쓰는 모니터. 그 점에 모니터가 없으면(뺐거나 배치가 바뀜) 주 모니터.</summary>
    private static nint CurMonitor(nint primary)
    {
        if (!_monOther) return primary;
        nint m = MonitorFromPoint(_monAt, 0 /* MONITOR_DEFAULTTONULL */);
        return m != 0 ? m : primary;
    }

    /// <summary>집 모니터로(보이기 전 — 숨었다 다시 나오면 집에서).</summary>
    private static void UseHomeMonitor()
    {
        _monOther = CatGrowth.HomeMonitor(out int x, out int y);
        _monAt = new Native.POINT { x = x, y = y };
    }

    /// <summary>점 p 가 있는(가장 가까운) 모니터의 작업 영역 — 끄는 동안 그 모니터 안으로만. 모르면 지금 작업 영역.</summary>
    private static Native.RECT WorkAt(Native.POINT p)
    {
        nint m = MonitorFromPoint(p, Native.MONITOR_DEFAULTTONEAREST);
        var mi = new Native.MONITORINFO { cbSize = (uint)sizeof(Native.MONITORINFO) };
        return m != 0 && Native.GetMonitorInfoW(m, ref mi) && mi.rcWork.right > mi.rcWork.left ? mi.rcWork : _work;
    }

    /// <summary>
    /// 고양이를 모니터 to 로 옮긴다(작업 영역 · 배율). 같은 모니터면 그대로. 그 모니터에서 보일 수 없으면(환경 점검 실패) 옮기지 않는다 — 원래 모니터 안에
    /// 내려앉는다. 배율이 다르면 그림을 다시 만들고 가운데 · 발 자리를 그대로 둔다. 그림을 만들지 못하면 숨기고 false(다시 나오면 집에서).
    /// </summary>
    private static bool ToMonitor(nint to)
    {
        nint primary = PrimaryMonitor();
        if (to == 0 || to == CurMonitor(primary)) return true;
        var mi = new Native.MONITORINFO { cbSize = (uint)sizeof(Native.MONITORINFO) };
        if (!Native.GetMonitorInfoW(to, ref mi)) return true;
        var keepAt = _monAt; bool keepOther = _monOther;
        _monOther = to != primary;
        _monAt = new Native.POINT { x = (mi.rcMonitor.left + mi.rcMonitor.right) / 2, y = (mi.rcMonitor.top + mi.rcMonitor.bottom) / 2 };
        if (!EnvOk(out Native.RECT work, out int dpi, out int why))
        {
            _monAt = keepAt; _monOther = keepOther;
            LogLine($"monitor refused why {why}");
            return true;
        }
        LogLine($"monitor {work.left},{work.top},{work.right},{work.bottom} dpi {dpi} (was {_work.left},{_work.top},{_work.right},{_work.bottom} dpi {_dpi})");
        if (dpi != _dpi)
        {
            if (_clipOn || _clipLoading || _peekOn) { int cur = CurrentSitX(); StopClip(); if (_posX != int.MinValue) _posX = cur; }
            int oldW = _w, oldH = _h;
            double s = _scale; bool light = _light;
            FreeArt(); _dpi = dpi; _light = light; _scale = s;
            if (!BuildArt()) { FreeArt(); _why = 8; Hide(keepCheck: true); RetryLater(); return false; }
            _dragX += (oldW - _w) / 2; _dragY += oldH - _h;   // 가운데와 발 자리를 그대로
            if (_posX != int.MinValue) _posX += (oldW - _w) / 2;
            _from = _gaze; _pending = -1; _seq = Array.Empty<int>();
        }
        _work = work;
        return true;
    }

    /// <summary>
    /// 고양이를 보일 수 없을 만큼 좁은 작업 영역인가(0.5.35, Codex 0.5.34 회신 1): 보이는 데 필요한 공간만 본다 — 앉은 고양이 한 마리(기본 키)와 양옆 여백,
    /// 높이는 키 3배(하트 · 이름표 자리). 걷기 · 사냥 공간은 그 동작이 직접 본다(모자라면 그 동작을 하지 않고 제자리 — 걷기 · 사냥 계획이 false).
    /// 예전 규칙(주 모니터: 폭의 5분의 1 ≥ 키 5배)은 1920×1080 을 150% 로 쓰면(키 78px, 384 &lt; 390) 고양이를 숨겼고, 0.5.34 의 보조 모니터 규칙(폭 ≥ 키 10배)과 둘이었다.
    /// </summary>
    internal static bool NarrowWork(int width, int height, int dpi)
    {
        int fh = (int)Math.Round(HeightLogical * dpi / 96.0), m = (int)Math.Round(MarginLogical * dpi / 96.0);
        return width < fh * 2 + m * 2 || height < fh * 3;
    }

    /// <summary>siteunit PC02: 1920×1032 를 100 · 125 · 150 · 200 %, 세로 1080×1872, 1280×720 150 %, 너무 좁은 폭 · 너무 낮은 높이. Y = 보임, N = 좁음.</summary>
    internal static string SpaceSelfTest()
    {
        var cases = new[] { (1920, 1032, 96), (1920, 1032, 120), (1920, 1032, 144), (1920, 1032, 192), (1080, 1872, 96), (1280, 720, 144), (130, 1000, 96), (200, 120, 96) };
        return string.Join("|", cases.Select(c => NarrowWork(c.Item1, c.Item2, c.Item3) ? "N" : "Y"));
    }

    /// <summary>작업 표시줄에 앉은 자리를 집으로(그 모니터도 — 다시 켜도 거기서).</summary>
    private static void SaveHomeAt(int x)
    {
        int cx = Math.Clamp(x, _work.left, Math.Max(_work.left, _work.right - _w));
        int room = Math.Max(1, _work.right - _work.left - _w);
        nint primary = PrimaryMonitor();
        bool prim = CurMonitor(primary) == primary;
        CatGrowth.SetHome((cx - _work.left) / (double)room, prim, _monAt.x, _monAt.y);
    }
}
