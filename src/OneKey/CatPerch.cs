using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 다른 창 위에 앉기(0.5.27, 2026-10-13 사용자 결정): 끌어다 놓으면 그 자리 바로 아래에 보이는 창의 윗변(제목 표시줄 윗선)이 있으면 떨어지다 거기에
/// 착지한다(없으면 예전처럼 작업 표시줄까지). 창 위에서는 그 창 폭 안에서 평소처럼 쉬고 · 걷고 · 놀잇감을 쫓는다(빼꼼 숨기는 작업 표시줄에서만).
/// 그 창이 비활성이 돼도 그대로, 창을 옮기면 같이 움직이고, 최소화 · 닫힘 · 최대화 · 숨김이 되거나 앉은 자리가 다른 창에 덮이면 그 아래 면
/// (다른 창의 윗변 또는 작업 표시줄)으로 떨어진다. 창 위 자리는 저장하지 않는다 — 숨거나 다시 나오면 작업 표시줄 집에서(사용자 결정).
/// 작업 표시줄에 떨어져 앉은 자리는 끌어다 놓을 때처럼 새 집이 된다(다니는 범위가 그 자리를 따라가게).
/// 1Key 자신의 창 · 도구 창 · 전체 화면 크기 창 · 고양이보다 좁은 창에는 앉지 않는다.
/// </summary>
internal static unsafe partial class CatWidget
{
    private static nint _perch;              // 앉아 있는 창(0 = 작업 표시줄)
    private static Native.RECT _perchRect;   // 그 창의 보이는 테두리(DWM — 보이지 않는 크기 조절 테두리를 뺀 것)
    private static nint _perchHook;
    private const nuint TimerPerch = 4;
    private const uint PerchCheckMs = 100;

    /// <summary>고양이가 앉는 면의 y(화면): 앉은 창의 윗변, 아니면 작업 표시줄 위 선.</summary>
    private static int SurfaceY => _perch != 0 ? _perchRect.top : _work.bottom;

    /// <summary>앉은 고양이 몸의 반폭(창 안 가운데 기준) — 정면 그림의 폭은 앉은 키의 약 0.94배.</summary>
    private static int BodyHalf => _sitTop < 0 ? _w / 3 : (int)Math.Round((_sitBot - _sitTop) * 0.47);

    /// <summary>창 위에서 다니는 범위(앉은 고양이 창 왼쪽): 몸이 창 윗변 안에 있게, 작업 영역 안으로.</summary>
    private static (int Min, int Max) PerchRange(Native.RECT r)
    {
        int min = Math.Max(_work.left, r.left - (_sitCx - BodyHalf));
        int max = Math.Min(_work.right - _w, r.right - (_sitCx + BodyHalf));
        return (min, max);
    }

    private static void SetPerch(nint h, Native.RECT r)
    {
        ClearPerch();
        _perch = h; _perchRect = r;
        GetWindowThreadProcessId(h, out uint pid);
        uint tid = GetWindowThreadProcessId(h, out _);
        // 그 창이 움직이거나(끌기 · 크기) 숨거나 없어지면 바로(0x8001 OBJECT_DESTROY ~ 0x800B OBJECT_LOCATIONCHANGE). 덮임은 0.1초 점검과 앞 창 알림
        _perchHook = SetWinEventHook(0x8001, 0x800B, 0, &OnPerchEvent, pid, tid, 0 /* WINEVENT_OUTOFCONTEXT */);
        if (_hwnd != 0) Native.SetTimer(_hwnd, TimerPerch, PerchCheckMs, 0);
        LogLine($"perch on {Native.GetClassName(h)} {r.left},{r.top},{r.right},{r.bottom}");
    }

    private static void ClearPerch()
    {
        if (_perchHook != 0) { UnhookWinEvent(_perchHook); _perchHook = 0; }
        if (_hwnd != 0) Native.KillTimer(_hwnd, TimerPerch);
        _perch = 0;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static void OnPerchEvent(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try { if (hwnd == _perch && idObject == 0 /* OBJID_WINDOW */) PerchTick(); } catch { }
    }

    /// <summary>0.1초마다 · 그 창의 움직임 알림 · 앞 창이 바뀔 때: 아직 앉을 수 있나, 옮겨졌나, 덮였나.</summary>
    private static void PerchTick()
    {
        if (_perch == 0 || !_shown || _dragOn || _pressed) return;
        if (!Perchable(_perch, out Native.RECT r, out string why)) { LogLine($"perch lost {why}"); FallFromPerch(); return; }
        var (min, max) = PerchRange(r);
        if (max < min) { LogLine("perch lost narrow"); FallFromPerch(); return; }
        if (r.left != _perchRect.left || r.top != _perchRect.top || r.right != _perchRect.right || r.bottom != _perchRect.bottom)
        {
            int dx = r.left - _perchRect.left;   // 창과 함께 옮긴다(왼쪽 가장자리 기준 — 크기만 바꾸면 그대로)
            if (r.right - r.left != _perchRect.right - _perchRect.left && r.right == _perchRect.right) dx = 0;
            _perchRect = r;
            if (_posX != int.MinValue) _posX += dx;
            if (_clipOn) _baseX += dx;
            if (_holdAir) _dragX = Math.Clamp(_dragX + dx, min, max);
            if (_posX != int.MinValue) _posX = Math.Clamp(_posX, min, max);
            // 0.5.28(Codex C27-2): 창 폭이 줄어 하던 동작(걷기 · 사냥)의 남은 걸음이 새 범위 밖으로 나가면 지금 걸음 자리에서 멈추고 앉는다
            // (끝날 때만 범위 안으로 맞추면 그동안 창 밖에서 걸었다). 떨어지는 중이면 떨어지는 자리만 범위 안으로
            if (_clipOn && _playName == "fall") _baseX = Math.Clamp(_baseX, min, max);
            else if (_clipOn && PathLeaves(_steps, _clipFrame, _baseX, min, max))
            {
                int cur = Math.Clamp(CurrentSitX(), min, max);
                StopClip();
                _posX = cur; _from = _gaze = Center; _pending = -1;
                LogLine($"perch narrowed: motion stopped at {cur} (range {min}..{max})");
                ScheduleNextClip(Environment.TickCount64);
            }
            if (!_clipOn && !_holdAir) Render();
            if (_clock != ClockPhase.None) ClockTick(Environment.TickCount64, DateTime.Now);
            SetProps();
        }
        if (SeatCovered()) { LogLine("perch covered"); FallFromPerch(); }
    }

    /// <summary>앉은 창에서 내려온다(그 창이 사라졌거나 덮였다): 지금 자리에서 그 아래 면으로 떨어진다.</summary>
    private static void FallFromPerch()
    {
        nint old = _perch;
        // 0.5.28(Codex C27-3): 지금 실제 자리 — 걷던 중이면 지금 걸음, 그 창으로 떨어지던 중이면 지금 공중(목적지 바닥이 아니라)
        int x = CurrentSitX(), y = CurrentSitY();
        // 정시 시계가 떠 있으면 닫는다(끌기와 같다 — 그 정시는 이미 보인 것으로). 닫지 않으면 떨어지기가 시계 때문에 거절되어 새 면으로 순간 이동했다
        if (_clock != ClockPhase.None) { FlipClock.Hide(); _clock = ClockPhase.None; LogLine("clock closed by fall"); }
        if (_clipOn || _clipLoading || _peekOn) StopClip();
        if (_holdAir) { _holdAir = false; }
        LogLine($"fall from perch at {x},{y}");
        ClearPerch();
        _from = _gaze = Center; _pending = -1;
        LandFrom(x, y, old);
    }

    /// <summary>
    /// (x, y = 고양이 창 왼쪽 위)에서 아래로 떨어져 앉을 면을 찾는다: 보이는 창의 윗변이면 그 창에, 아니면 작업 표시줄(그 자리가 새 집).
    /// 높이가 있으면 떨어지기 동작, 거의 그 자리면 바로 앉는다.
    /// </summary>
    private static void LandFrom(int x, int y, nint exclude)
    {
        int feet = y + _h - SitSinkPx;
        if (FindSurface(x + _sitCx, feet, exclude, out nint h, out Native.RECT r))
        {
            SetPerch(h, r);
            var (min, max) = PerchRange(r);
            _posX = Math.Clamp(x, min, max);
        }
        else
        {
            int cx = Math.Clamp(x, _work.left, Math.Max(_work.left, _work.right - _w));
            int room = Math.Max(1, _work.right - _work.left - _w);
            CatGrowth.SetHome((cx - _work.left) / (double)room);
            _posX = Math.Clamp(cx, MinX, MaxX);
        }
        int lift = GroundY - y;
        LogLine($"land x {_posX} perch {(_perch != 0 ? Native.GetClassName(_perch) : "taskbar")} lift {lift}");
        _dragX = _posX; _dragY = y;
        if (lift > _h / 4 && StartFall(lift)) { SetProps(); return; }
        Render();
        _nextClipAt = Math.Max(_nextClipAt, Environment.TickCount64 + 1500);
        SetProps();
    }

    /// <summary>
    /// 고양이 가운데(cx) 아래로 떨어질 때 처음 닿는 창 윗변: 앉을 수 있는 창이고, 윗변이 발 줄(feet)보다 아래(놓은 자리가 제목 표시줄 안이면 조금 위도 —
    /// 그 창에 올려놓은 것으로)이고, 그 윗변 자리가 다른 창에 덮이지 않았을 때. 없으면 false(작업 표시줄).
    /// </summary>
    private static bool FindSurface(int cx, int feet, nint exclude, out nint found, out Native.RECT foundRect)
    {
        found = 0; foundRect = default;
        var wins = ZWindows();
        int tol = (int)Math.Round(14 * _dpi / 96.0), best = int.MaxValue;
        for (int i = 0; i < wins.Count; i++)
        {
            var (h, r, ok) = wins[i];
            if (!ok || h == exclude) continue;
            if (cx < r.left || cx >= r.right || r.top < feet - tol || r.top >= best) continue;
            if (r.top >= _work.bottom - (_sitBot - _sitTop) / 2) continue;   // 작업 표시줄 바로 위면 작업 표시줄에 앉는다
            var (min, max) = PerchRange(r);
            if (max < min) continue;
            if (Covered(wins, i, cx - BodyHalf / 2, cx + BodyHalf / 2, r.top)) continue;
            best = r.top; found = h; foundRect = r;
        }
        return found != 0;
    }

    /// <summary>앉은 자리(창 윗변 중 고양이 몸 아래)가 그 창보다 위(z 차례)에 있는 창에 덮였나.</summary>
    private static bool SeatCovered()
    {
        var wins = ZWindows();
        int i = wins.FindIndex(w => w.H == _perch);
        if (i < 0) return false;   // 목록에 없으면(잠깐 상태가 바뀌는 중) 다음 점검에서 Perchable 이 본다
        int cx = CurrentSitX() + _sitCx;   // 0.5.28(Codex C27-2): 걷는 중이면 지금 걸음 자리(시작 자리가 아니라)
        return Covered(wins, i, cx - BodyHalf / 2, cx + BodyHalf / 2, _perchRect.top);
    }

    // ---- 지금 실제 자리(0.5.28, Codex C27-2 · C27-3): 정지 · 걷기 · 사냥 · 떨어지기 모두 같은 식. 걸음 자리는 동작이 끝날 때 새 자리로 쓰는 것과 같다
    // (EndClip: _baseX + Dx), 높이는 그 걸음의 Lift 만큼 바닥 위(RenderStep: _cy = WinY + … - Lift)
    private static int SitXOf(int posX, int baseX, bool clipOn, PlayStep[] steps, int frame)
        => clipOn && frame >= 0 && frame < steps.Length ? baseX + (int)Math.Round(steps[frame].Dx) : posX;
    private static int SitYOf(int winY, bool clipOn, PlayStep[] steps, int frame)
        => clipOn && frame >= 0 && frame < steps.Length ? winY - steps[frame].Lift : winY;
    private static int CurrentSitX() => SitXOf(WinX, _baseX, _clipOn, _steps, _clipFrame);
    private static int CurrentSitY() => SitYOf(WinY, _clipOn, _steps, _clipFrame);

    /// <summary>남은 걸음(frame 부터) 가운데 범위 밖으로 나가는 것이 있나.</summary>
    private static bool PathLeaves(PlayStep[] steps, int frame, int baseX, int min, int max)
    {
        for (int j = Math.Max(0, frame); j < steps.Length; j++)
        {
            int x = baseX + (int)Math.Round(steps[j].Dx);
            if (x < min || x > max) return true;
        }
        return false;
    }

    /// <summary>위(z 차례)에 있는 창들 가운데 앉은 자리 띠(가로 l..r, 윗변 top 부터 band)와 겹치는 것이 있나.</summary>
    private static bool CoveredBy(IEnumerable<Native.RECT> above, int l, int r, int top, int band)
    {
        foreach (var o in above)
            if (o.left < r && o.right > l && o.top <= top + band && o.bottom > top) return true;
        return false;
    }

    /// <summary>
    /// siteunit 자체 시험(PC01, Codex C27-2 · C27-3): 화면 없이 계산하는 자리 판단. 결과 "a|b|…" 를 SiteSelfTest 가 기대값과 비교한다.
    /// 반례(Codex): 시작 500 · 걸은 Dx 120 · 고양이 가운데 40 · 반폭 24 → 실제 가운데 660. 그 자리만 덮으면 덮임, 떠난 시작 자리만 덮으면 아님.
    /// </summary>
    internal static string PerchSelfTest()
    {
        var walk = new PlayStep[] { new(1, 0, false, 0), new(1, 1, false, 60), new(1, 2, false, 120), new(1, 3, false, 180), new(1, 4, false, 240) };
        int sx = SitXOf(500, 500, true, walk, 2);                       // 660 - 40 = 620
        int cx = sx + 40;
        bool walkedSpot = CoveredBy(new[] { new Native.RECT { left = 650, top = 300, right = 675, bottom = 600 } }, cx - 12, cx + 12, 400, 4);
        bool startSpot = CoveredBy(new[] { new Native.RECT { left = 520, top = 300, right = 560, bottom = 600 } }, cx - 12, cx + 12, 400, 4);
        bool leaves = PathLeaves(walk, 2, 500, 300, 700);                  // 남은 걸음 620 · 680 · 740 → 740 이 범위(300..700) 밖
        bool stays = PathLeaves(walk, 2, 500, 300, 800);
        var fall = new PlayStep[] { new(0, 14, false, 0, 300), new(0, 15, false, 0, 120), new(0, 16, false, 0, 0) };
        int airY = SitYOf(900, true, fall, 1);                            // 바닥 900 위 120 = 780(목적지 900 이 아니라)
        int idleX = SitXOf(432, 999, false, walk, 2), idleY = SitYOf(900, false, fall, 1);
        return $"{sx}|{walkedSpot}|{startSpot}|{leaves}|{stays}|{airY}|{idleX}|{idleY}";
    }

    private static bool Covered(List<(nint H, Native.RECT R, bool Ok)> wins, int i, int l, int r, int top)
    {
        int band = Math.Max(2, (int)Math.Round(4 * _dpi / 96.0));
        for (int j = 0; j < i; j++)
        {
            var o = wins[j].R;
            if (o.left < r && o.right > l && o.top <= top + band && o.bottom > top) return true;
        }
        return false;
    }

    /// <summary>
    /// 화면에 보이는 다른 프로그램의 맨 위 창들(z 차례, 위부터)과 그 보이는 테두리. Ok = 고양이가 앉을 수 있는 창.
    /// 1Key 자신의 창 · 숨김 · 최소화 · 클로킹(다른 가상 데스크톱 · 숨은 스토어 앱) · 투명 통과 창 · 작업 표시줄 · 바탕 화면은 뺀다.
    /// </summary>
    private static List<(nint H, Native.RECT R, bool Ok)> ZWindows()
    {
        _zList.Clear();
        EnumWindows(&ZProc, 0);
        return _zList;
    }
    private static readonly List<(nint H, Native.RECT R, bool Ok)> _zList = new();

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int ZProc(nint h, nint l)
    {
        try
        {
            if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) return 1;
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == (uint)Environment.ProcessId) return 1;
            long ex = (long)Native.GetWindowLongPtrW(h, -20 /* GWL_EXSTYLE */);
            if ((ex & 0x20 /* WS_EX_TRANSPARENT */) != 0) return 1;   // 누름이 통과하는 덮개(화면 녹화 · 그래픽 드라이버 표시 등)는 덮개로도 치지 않는다
            if (Cloaked(h)) return 1;
            string cls = Native.GetClassName(h);
            if (cls is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW" or "NotifyIconOverflowWindow") return 1;
            if (!Frame(h, out Native.RECT r) || r.right - r.left < 8 || r.bottom - r.top < 8) return 1;
            bool ok = PerchableStyle(h, ex, r);
            // 시험: ONEKEY_TEST_PERCH_CLASS 의 창에만 앉는다(없으면 어디에도) — 시험하는 PC 에 열려 있는 실제 창에 앉아 다른 시험(catdrag)이 흔들리지 않게
            if (ok && Program.IsTestMode) ok = Environment.GetEnvironmentVariable("ONEKEY_TEST_PERCH_CLASS") is string tc && tc.Length > 0 && cls.StartsWith(tc, StringComparison.Ordinal);
            _zList.Add((h, r, ok));
        }
        catch { }
        return 1;
    }

    /// <summary>앉을 수 있는 종류의 창인가(모양만): 도구 창 · 자식 창 · 최대화 · 화면 전체 크기 · 너무 작은 창은 아니다.</summary>
    private static bool PerchableStyle(nint h, long ex, Native.RECT r)
    {
        if ((ex & 0x80 /* WS_EX_TOOLWINDOW */) != 0) return false;
        long st = (long)Native.GetWindowLongPtrW(h, -16 /* GWL_STYLE */);
        if ((st & 0x40000000 /* WS_CHILD */) != 0) return false;
        if (IsZoomed(h)) return false;
        int ch = _sitTop < 0 ? _h : _sitBot - _sitTop;
        if (r.bottom - r.top < ch * 2) return false;
        if (r.left <= _work.left && r.right >= _work.right && r.top <= _work.top) return false;   // 화면 전체(최대화와 같은 모양 · 전체 화면)
        if (r.top - ch < _work.top) return false;   // 윗변이 화면 맨 위에 붙어 있으면 고양이가 올라갈 자리가 없다
        return true;
    }

    /// <summary>앉아 있는 창이 아직 앉을 수 있는 상태인가(보임 · 최소화 아님 · 최대화 아님 · 클로킹 아님 · 자리 앎).</summary>
    private static bool Perchable(nint h, out Native.RECT r, out string why)
    {
        r = default;
        why = "closed";
        if (!IsWindow(h)) return false;
        why = "hidden";
        if (!Native.IsWindowVisible(h) || Cloaked(h)) return false;
        why = "minimized";
        if (Native.IsIconic(h)) return false;
        why = "maximized";
        if (IsZoomed(h)) return false;
        why = "rect";
        if (!Frame(h, out r)) return false;
        why = "shape";
        long ex = (long)Native.GetWindowLongPtrW(h, -20);
        if (!PerchableStyle(h, ex, r)) return false;
        if (r.top >= _work.bottom - (_sitBot - _sitTop) / 2) { why = "low"; return false; }
        why = "";
        return true;
    }

    private static bool Frame(nint h, out Native.RECT r)
    {
        Native.RECT f;
        if (DwmGetWindowAttribute(h, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, &f, (uint)sizeof(Native.RECT)) == 0 && f.right > f.left) { r = f; return true; }
        return Native.GetWindowRect(h, out r) && r.right > r.left;
    }

    private static bool Cloaked(nint h)
    {
        int c = 0;
        return DwmGetWindowAttribute(h, 14 /* DWMWA_CLOAKED */, &c, sizeof(int)) == 0 && c != 0;
    }

    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, uint attr, void* value, uint size);
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hWnd);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] private static extern bool EnumWindows(delegate* unmanaged[Stdcall]<nint, nint, int> proc, nint lParam);
}
