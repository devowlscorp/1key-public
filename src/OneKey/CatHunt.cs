namespace OneKey;

/// <summary>
/// 놀잇감 사냥(0.5.21 — CatGuest.cs 의 놀잇감과 함께): 걷기 그림(WT · WL · WI)과 같은 배율 · 자리로 만든 사냥 그림(tools/cat/make_hunt.py)을 이어 한 번의 재생으로.
/// 돌아서기(WT, 놀잇감 쪽) → 걸음 0~2 주기(WL) → 멈춰 웅크림(HC) → 엉덩이 실룩(HW, 놀잇감이 머무는 동안 되풀이) → [누르면] 덮치기(HP · 나비는 HJ, 앞으로 나아감과
/// 뛰는 높이는 창을 옮겨서) / [안 누르고 놀잇감이 떠나면] 일어남(HR) → 돌아서 앉기(WI).
/// 누를지 · 떠날지는 재생 중에 정해지므로, 실룩 한 번이 끝나는 장(HW 첫 장 = 웅크림)에서 남은 걸음을 바꿔 끼운다(걷기의 CutWalk 와 같은 방식).
/// 그림 줄(catclips.txt)의 k: = 핵심 자세가 오는 장(HP · HJ: 웅크림 0 · 공중 · 누름 · 다시 걷기) — 덮치는 거리 · 높이 · 잡는 순간을 그 장에 맞춘다.
/// </summary>
internal static unsafe partial class CatWidget
{
    private static readonly string? GuestTest = Program.IsTestMode ? Environment.GetEnvironmentVariable("ONEKEY_TEST_CAT_GUEST") is { Length: > 0 } g ? g : null : null;
    private static readonly long GuestStayMs = Program.IsTestMode && long.TryParse(Environment.GetEnvironmentVariable("ONEKEY_TEST_CAT_GUEST_STAY_MS"), out long gs) && gs > 0 ? gs : 20_000;
    /// <summary>시험: 놀잇감이 나타난 뒤 이만큼 지나면 누른 것으로(입력 없이 기록만으로 시험할 때). 0 = 안 누름(떠나는 길 시험).</summary>
    private static readonly long GuestAutoClickMs = Program.IsTestMode && long.TryParse(Environment.GetEnvironmentVariable("ONEKEY_TEST_CAT_GUEST_CLICK_MS"), out long gc) && gc > 0 ? gc : 0;
    private static readonly string[] GuestKinds = { "butterfly", "yarn", "bubble" };   // 0.5.21-D: 쥐 → 비눗방울(사용자: 쥐를 잡는 건 잔인해 보인다)
    private const int HuntWiggles = 12;   // 실룩 되풀이 최대(한 번 2초 — 놀잇감이 머무는 20초보다 길게)

    private static long _guestDueAt;
    private static bool _huntCatchReq, _huntGiveUp, _huntSpliced;
    private static long _huntGuestAt;
    private static string _huntKind = "";
    private static int _huntCatchStep = -1;
    private static double _huntGuestX;   // 놀잇감 쉬는 자리(화면 x)
    private static double _huntLeap;     // 덮치며 앞으로 나아가는 거리(px)
    // 사냥 재생의 그림 번호(StartPlay 의 infos 순서)
    private const int HArtTurn = 0, HArtLoop = 1, HArtCrouch = 2, HArtWiggle = 3, HArtPounce = 4, HArtRise = 5, HArtBack = 6;

    /// <summary>놀잇감이 올 때인가: 밝은 고양이(사냥 그림은 밝은 고양이뿐)이고 사냥 그림이 다 있을 때, 처음은 5~10분 뒤, 그 뒤로는 40~60분에 한 번(0.5.21-E 사용자: 한 시간에 한 번 쉬기를 권하니 불규칙해도 이 정도 텀).</summary>
    private static bool GuestDue(long now)
    {
        if (SeqTest || WalkTest || PeekTest || FullTest || !HasInteractArt(_light) || ClipIndex("HC") < 0) return false;
        if (GuestTest is not null) return true;   // 시험: 동작마다 놀잇감
        if (_guestDueAt == 0) { _guestDueAt = now + (5 + _rnd.Next(6)) * 60_000L; return false; }
        return now >= _guestDueAt;
    }

    private static void ScheduleGuest(long now) => _guestDueAt = now + (40 + _rnd.Next(21)) * 60_000L;

    /// <summary>
    /// 사냥 시작: 놀잇감 쪽(다닐 자리가 넓은 쪽)으로 돌아서 0~2 주기 걷고 웅크린다. 덮칠 거리만큼 자리가 없으면 false(다른 동작).
    /// 놀잇감 자리는 그림이 준비된 뒤(OnClipReady → HuntReady) 덮친 앞발이 닿는 곳으로 정한다.
    /// </summary>
    private static bool StartHunt()
    {
        string kind = GuestTest is string gt && Array.IndexOf(GuestKinds, gt) >= 0 ? gt : GuestKinds[_rnd.Next(GuestKinds.Length)];
        bool fly = kind == "butterfly";   // 위로 뛰어(HJ) — 털실 · 비눗방울은 앞으로 덮친다(HP)
        int it = ClipIndex("WT"), il = ClipIndex("WL"), ii = ClipIndex("WI"), hc = ClipIndex("HC"), hw = ClipIndex("HW"), hp = ClipIndex(fly ? "HJ" : "HP"), hr = ClipIndex("HR");
        if (it < 0 || il < 0 || ii < 0 || hc < 0 || hw < 0 || hp < 0 || hr < 0 || _sitTop < 0 || (kind != "bubble" && !HasPng($"guest_{kind}.png"))) return false;
        var all = Clips();
        var turn = all[it]; var loop = all[il]; var crouch = all[hc]; var wig = all[hw]; var back = all[ii];
        if (loop.Frames != WalkCycle || all[hp].Keys.Length < 4) return false;
        double k = (_sitBot - _sitTop) / (double)loop.SitH;
        double dx = WalkStripPx * k, cyclePx = dx * WalkCycle;
        int sitH = _sitBot - _sitTop;
        _huntLeap = Math.Round(sitH * (fly ? 0.45 : 0.85));
        int x = WinX;
        double roomL = x - MinX, roomR = HomeX - x;
        int dir = roomL >= roomR ? -1 : 1;
        double room = dir < 0 ? roomL : roomR;
        if (room < _huntLeap + dx) return false;
        int cycles = Math.Min((int)Math.Floor((room - _huntLeap - dx) / cyclePx), _rnd.Next(3));
        cycles = Math.Max(0, cycles);
        if (GuestTest is not null) cycles = Math.Min(cycles, 1);
        bool m = dir < 0;
        var steps = new List<PlayStep>();
        int lift = SitSinkPx, last = Math.Max(1, turn.Frames - 1);
        for (int i = 0; i < turn.Frames; i++) steps.Add(new PlayStep(HArtTurn, i, m, 0, lift * i / last));
        double moved = 0;
        for (int c = 0; c < cycles; c++)
            for (int i = 0; i < WalkCycle; i++) { moved += dir * dx; steps.Add(new PlayStep(HArtLoop, i, m, moved, lift)); }
        moved += dir * dx;   // 웅크림 첫 장 = 걸음 주기 첫 장(걷는 옆모습 W54)
        for (int i = 0; i < crouch.Frames; i++) steps.Add(new PlayStep(HArtCrouch, i, m, moved, lift));
        for (int r = 0; r < HuntWiggles; r++)
            for (int i = 1; i < wig.Frames; i++) steps.Add(new PlayStep(HArtWiggle, i == wig.Frames - 1 ? 0 : i, m, moved, lift));   // 끝 장 = 첫 장(웅크림)
        AppendRise(steps, all[hr], back, m, moved, lift);
        _huntKind = kind; _huntCatchReq = _huntGiveUp = _huntSpliced = false; _huntCatchStep = -1; _huntGuestAt = 0;
        LogLine($"huntplan {kind} dir {dir} cycles {cycles} leap {_huntLeap:0} from {x} min {MinX} home {HomeX}");
        StartPlay("hunt", new[] { turn, loop, crouch, wig, all[hp], all[hr], back }, steps.ToArray());
        return true;
    }

    /// <summary>일어나 다시 걷는 자세(HR) → 돌아서 앉기(WI). 둘 다 웅크림 다음 장부터.</summary>
    private static void AppendRise(List<PlayStep> steps, ClipInfo rise, ClipInfo back, bool m, double moved, int lift)
    {
        for (int i = 1; i < rise.Frames; i++) steps.Add(new PlayStep(HArtRise, i, m, moved, lift));
        AppendBack(steps, back, m, moved, lift);
    }

    private static void AppendBack(List<PlayStep> steps, ClipInfo back, bool m, double moved, int lift)
    {
        int lastB = Math.Max(1, back.Frames - 1);
        for (int i = 0; i < back.Frames; i++) steps.Add(new PlayStep(HArtBack, i, m, moved, lift * (lastB - i) / lastB));
    }

    /// <summary>
    /// 그림이 준비됐다(OnClipReady): 놀잇감을 덮친 앞발이 닿는 자리에 띄운다 — 웅크린 자리 + 덮치는 거리 + 잡는 장에서 앞발 끝까지(그림에서 잰다) − 놀잇감 칸의 3분의 1.
    /// </summary>
    private static void HuntReady()
    {
        if (_playName != "hunt" || _arts.Length <= HArtPounce) return;
        var crouchStep = Array.Find(_steps, s => s.Art == HArtCrouch);
        int dir = crouchStep.Mirror ? -1 : 1;
        var pa = _arts[HArtPounce];
        bool fly = _huntKind == "butterfly", bub = _huntKind == "bubble";
        int catchCell = pa.Info.Keys[fly || bub ? 1 : 2];   // 나비 · 비눗방울은 공중에서 앞발이 닿는 장, 털실은 누르는 장
        var (e, lift) = LeapAt(catchCell, pa.Info.Keys[2], fly, crouchStep.Lift);   // 잡는 장에서 앞으로 간 몫 · 올린 높이(SpliceHunt 와 같은 식)
        double front = FrontOf(pa, catchCell, out int frontY);
        int sitH = _sitBot - _sitTop, size = Math.Max((int)Math.Round(14 * _dpi / 96.0), (int)Math.Round(sitH * (bub ? 0.75 : 0.62)));   // 0.5.21-E 사용자: 25 % 크게, 비눗방울은 속이 비어 조금 더
        double anchor = _baseX + _sitCx + crouchStep.Dx;
        _huntGuestX = anchor + dir * (_huntLeap * e + front - size * 0.33);
        double ground = WinY + _sitBot - SitSinkPx;   // 서 있는 고양이 발 줄 = 작업 표시줄 위 선
        // 나비: 뛰어오른 고양이의 앞발 끝(그 장의 맨 위)쯤에 떠 있다
        double kk = pa.CellW / (double)pa.Info.CellW;
        double pawTop = WinY + _sitBot - pa.Info.FeetY * kk - lift + TopOf(pa, catchCell);
        double pawFront = WinY + _sitBot - pa.Info.FeetY * kk - lift + frontY;   // 앞으로 뻗은 앞발 끝(덮치는 장)
        double hover = fly ? Math.Max(size * 0.9, ground - pawTop - size * 0.1) : bub ? Math.Max(size * 0.6, ground - pawFront) : 0;
        if (!GuestShow(_huntKind, _huntGuestX, ground, size, dir, hover, OnGuestClick)) { LogLine("guest show failed"); return; }
        _huntGuestAt = Environment.TickCount64;
    }

    /// <summary>덮치기 j 번째 장: 앞으로 간 몫(0..1, 웅크림에서 두 장 뒤 땅을 차고 누르는 장 land 에 닿는다)과 올린 높이(px — 나비는 높이 뛴다).</summary>
    private static (double E, int Lift) LeapAt(int j, int land, bool fly, int baseLift)
    {
        const int air = 2;
        double u = Math.Clamp((j - air) / (double)Math.Max(1, land - air), 0, 1), e = u * u * (3 - 2 * u);
        double jumpH = (_sitBot - _sitTop) * (fly ? 0.42 : 0.12);
        return (e, baseLift + (int)Math.Round(jumpH * Math.Sin(Math.PI * u)));
    }

    /// <summary>그 장에서 고양이 그림의 맨 위 줄(칸 안 y).</summary>
    private static int TopOf(ClipArt a, int cell)
    {
        int cw = a.CellW, ch = a.CellH, TW = cw * a.Info.Frames;
        cell = Math.Clamp(cell, 0, a.Info.Frames - 1);
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
                if ((a.Px[y * TW + cell * cw + x] >> 24) > 96) return y;
        return 0;
    }

    /// <summary>그 장에서 고양이 그림의 앞쪽 끝(오른쪽을 본 그림 기준)이 고양이 가운데(앉은 고양이 가운데 x 와 같은 자리)에서 얼마나 앞인가(px).</summary>
    private static double FrontOf(ClipArt a, int cell, out int frontY)
    {
        int cw = a.CellW, ch = a.CellH, TW = cw * a.Info.Frames, right = -1;
        frontY = ch / 2;
        cell = Math.Clamp(cell, 0, a.Info.Frames - 1);
        for (int y = 0; y < ch; y++)
            for (int x = cw - 1; x > right; x--)
                if ((a.Px[y * TW + cell * cw + x] >> 24) > 96) { right = x; frontY = y; break; }
        double anchor = a.Info.AnchorX * (cw / (double)a.Info.CellW);
        return right < 0 ? 0 : right + 1 - anchor;
    }

    private static void OnGuestClick()
    {
        if (!_clipOn || _playName != "hunt" || _huntSpliced) return;
        _huntCatchReq = true;
    }

    /// <summary>
    /// 사냥 한 걸음(ClipTick, 장을 그리기 전): 웅크림 장(HW 첫 장)에서 눌렀으면 덮치기로, 놀잇감이 머문 시간이 지났거나 메뉴 동작을 시켰으면 일어나기로
    /// 남은 걸음을 바꾼다. 잡는 장에 오면 놀잇감이 사라지고 하트 + 점수.
    /// </summary>
    private static void HuntTick(int i)
    {
        if (_playName != "hunt" || i < 0 || i >= _steps.Length) return;
        var s = _steps[i];
        long now = Environment.TickCount64;
        if (GuestAutoClickMs > 0 && _huntGuestAt > 0 && !_huntCatchReq && now - _huntGuestAt >= GuestAutoClickMs) { LogLine("guest autoclick"); _huntCatchReq = true; }
        // 바꿔 끼우는 자리 = 웅크린 장: 실룩 한 번이 끝난 장(HW 첫 장), 또는 웅크림이 끝난 장(다가가는 중에 눌렀으면 실룩 없이 바로 덮친다)
        bool crouchEnd = s.Art == HArtCrouch && s.Cell == _arts[HArtCrouch].Info.Frames - 1;
        if (!_huntSpliced && crouchEnd && _huntCatchReq && GuestOpen) SpliceHunt(i, true);
        if (!_huntSpliced && s.Art == HArtWiggle && s.Cell == 0)
        {
            bool timeUp = _huntGiveUp || (_huntGuestAt > 0 && now - _huntGuestAt >= GuestStayMs) || (_huntGuestAt > 0 && !GuestOpen);
            if (_huntCatchReq && GuestOpen) SpliceHunt(i, true);
            else if (timeUp) SpliceHunt(i, false);
        }
        if (!_huntSpliced && s.Art == HArtRise && GuestOpen) GuestLeave();   // 실룩을 다 하도록 아무 일도 없었다
        if (i == _huntCatchStep)
        {
            _huntCatchStep = -1;
            GuestCatch();
            int pts = CatGrowth.AwardGuest(_light);
            LogLine($"hunt caught {_huntKind} +{pts} score {CatGrowth.Score(_light)}");
            int dir = s.Mirror ? -1 : 1;
            string what = _huntKind switch { "butterfly" => T.CatGuestFly, "bubble" => T.CatGuestPop, _ => T.CatGuestCatch };   // 나비는 팔랑 날아가고 비눗방울은 톡
            int top = _cy + TopOf(_arts[s.Art], s.Cell);   // 지금 장의 고양이 머리 위(동작 칸 위 여백이 아니라)
            ShowHearts(true, Math.Max(_work.top, top), note: $"{what} +{pts}", headCx: (int)Math.Round(_huntGuestX - dir * _gS * 0.2));
        }
    }

    /// <summary>i 번째 걸음(웅크림) 뒤를 덮치기(catchIt) 또는 일어나기로 바꾼다.</summary>
    private static void SpliceHunt(int i, bool catchIt)
    {
        var s = _steps[i];
        bool m = s.Mirror; int dir = m ? -1 : 1, lift = s.Lift;
        double moved = s.Dx;
        var tail = new List<PlayStep>();
        if (catchIt)
        {
            var pa = _arts[HArtPounce].Info;
            bool fly = _huntKind == "butterfly";
            // 공중(땅을 찬 장 ~ 누르는 장)은 한 장 걸러(0.5.21-G 사용자: 슬로 모션 같다 — 덮치는 순간은 "확" 빠르게, 땅을 차고 누를 때까지 약 0.6초)
            int target = _huntKind == "yarn" ? pa.Keys[2] : pa.Keys[1];   // 나비 · 비눗방울은 공중에서 앞발이 닿는 장, 털실은 누르는 장
            for (int j = 1; j < pa.Frames; j++)
            {
                if (j > 2 && j < pa.Keys[2] && ((j - 2) & 1) == 1 && j != target) continue;
                var (e, lf) = LeapAt(j, pa.Keys[2], fly, lift);
                if (j == target) _huntCatchStep = i + 1 + tail.Count;
                tail.Add(new PlayStep(HArtPounce, j, m, moved + dir * _huntLeap * e, lf));
            }
            AppendBack(tail, _arts[HArtBack].Info, m, moved + dir * _huntLeap, lift);
        }
        else
        {
            GuestLeave();
            AppendRise(tail, _arts[HArtRise].Info, _arts[HArtBack].Info, m, moved, lift);
        }
        _steps = _steps[..(i + 1)].Concat(tail).ToArray();
        _huntSpliced = true;
        LogLine($"hunt {(catchIt ? "pounce" : "giveup")} at {i} steps {_steps.Length} catchstep {_huntCatchStep}");
    }
}
