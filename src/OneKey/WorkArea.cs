namespace OneKey;

/// <summary>
/// 창을 놓을 수 있는 영역(작업 표시줄을 뺀 모니터 영역)과 배율을 한곳에서 정한다 (T5/T13).
/// 검증 모드(ONEKEY_TEST=1)에서는 ONEKEY_TEST_WORKAREA=가로,세로,DPI (예: 1366,768,120) 로 작은 화면을 흉내 낸다.
/// 이때 작업 영역은 (0,0)-(가로, 세로 - 작업 표시줄 40 논리 px) 이고, 앱의 배율도 그 DPI 로 계산한다.
/// </summary>
internal static unsafe class WorkArea
{
    private static readonly (int W, int H, uint Dpi)? Override = Parse();

    private static (int, int, uint)? Parse()
    {
        if (!Program.IsTestMode) return null;
        string[]? p = Environment.GetEnvironmentVariable("ONEKEY_TEST_WORKAREA")?.Split(',');
        if (p is not { Length: 3 } || !int.TryParse(p[0], out int w) || !int.TryParse(p[1], out int h) || !uint.TryParse(p[2], out uint d)) return null;
        if (w < 320 || h < 240 || d < 96 || d > 480) return null;
        return (w, h, d);
    }

    public static bool IsOverridden => Override is not null;

    /// <summary>hwnd 가 있는 모니터의 작업 영역(물리 px).</summary>
    public static Native.RECT For(nint hwnd)
    {
        if (Override is { } o)
            return new Native.RECT { left = 0, top = 0, right = o.W, bottom = o.H - (int)(40L * o.Dpi / 96) };
        var mi = new Native.MONITORINFO { cbSize = (uint)sizeof(Native.MONITORINFO) };
        nint mon = Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST);
        if (mon != 0 && Native.GetMonitorInfoW(mon, ref mi)) return mi.rcWork;
        return new Native.RECT { left = 0, top = 0, right = 1280, bottom = 720 };
    }

    /// <summary>화면 좌표 p 가 있는(없으면 가장 가까운) 모니터의 작업 영역(물리 px).</summary>
    public static Native.RECT ForPoint(Native.POINT p)
    {
        if (Override is { } o)
            return new Native.RECT { left = 0, top = 0, right = o.W, bottom = o.H - (int)(40L * o.Dpi / 96) };
        var mi = new Native.MONITORINFO { cbSize = (uint)sizeof(Native.MONITORINFO) };
        nint mon = Native.MonitorFromPoint(p, Native.MONITOR_DEFAULTTONEAREST);
        if (mon != 0 && Native.GetMonitorInfoW(mon, ref mi)) return mi.rcWork;
        return new Native.RECT { left = 0, top = 0, right = 1280, bottom = 720 };
    }

    /// <summary>화면 좌표 p 가 있는(없으면 가장 가까운) 모니터의 배율. 흉내 중이면 그 값.</summary>
    public static uint DpiForPoint(Native.POINT p)
    {
        if (Override is { } o) return o.Dpi;
        nint mon = Native.MonitorFromPoint(p, Native.MONITOR_DEFAULTTONEAREST);
        if (mon != 0 && Native.GetDpiForMonitor(mon, 0 /* MDT_EFFECTIVE_DPI */, out uint dx, out _) == 0 && dx != 0) return dx;
        uint d = Native.GetDpiForSystem();
        return d == 0 ? 96 : d;
    }

    /// <summary>hwnd 의 배율(DPI). 흉내 중이면 그 값.</summary>
    public static uint DpiFor(nint hwnd)
    {
        if (Override is { } o) return o.Dpi;
        // 창이 없으면(시작 단계의 안내 상자 등) 시스템 배율. 예전에는 96 이라 배율이 높은 화면에서 글자가 작았다.
        uint d = hwnd != 0 ? Native.GetDpiForWindow(hwnd) : Native.GetDpiForSystem();
        return d == 0 ? 96 : d;
    }
}
