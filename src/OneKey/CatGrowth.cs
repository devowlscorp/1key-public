using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 작업 표시줄 고양이 키우기(0.5.17, 2026-10-10 사용자: 다마고치처럼 — 기본은 지금의 절반 크기, 쓰다듬기·츄르·놀아 주기에 점수를 주어 쌓인 만큼 커지고,
/// 시간이 지나면 점수가 단계적으로 줄며 작아진다).
/// - 고양이마다(흰 고양이 · 검은 고양이 — 이름도 따로, 설정) 점수 0~100, 20점마다 한 단계(0~5) → 크기 50·60·70·80·90·100 %.
///   그림은 단계가 바뀔 때만 다시 만든다(CatWidget.ApplyGrowth).
/// - 점수: 츄르 +10, 놀아 주기 +8, 쓰다듬기 +6. 같은 동작은 3분에 한 번만 점수(사용자: 3분 — 동작은 언제든 한다).
/// - 줄기: 1Key 가 켜져 있는 동안 10분마다 1점(사용자: 10분, 켜져 있는 시간만). 꺼져 있던 시간·PC 가 잠든 시간은 세지 않는다(QueryUnbiasedInterruptTime).
///   벌칙은 없다 — 아프거나 사라지지 않고 50 %에서 멈춘다.
/// - 상호작용 그림이 있는 고양이만 자란다(사용자: 검은 고양이도 그림이 다 만들어지면 같은 규칙). 지금은 흰 고양이뿐 — 검은 고양이는 그림이 들어올 때까지 늘 100 %.
/// - 보안 습관 보상(0.5.18, 2026-10-10 사용자: 게임 요소 — 보안 습관 보상부터, 벌칙 없이, 크기와 단순하게): 같은 점수에 더한다.
///   백업 파일 만들기 +20(일주일에 한 번) · 마스터 비밀번호 바꾸기 +20(30일에 한 번) · 저장된 비밀번호 바꾸기 +10(하루 세 번까지) ·
///   자동 잠금이 켜진 채 그날 처음 잠금 풀기 +5(하루 한 번). 그때는 1Key 창이 열려 고양이가 없으므로, 다음에 고양이가 보일 때 하트와 한 줄로 알린다(TakePending).
/// - 출근 도장(0.5.19, 사용자: 게임 요소 — 출근 도장): 그날 처음 잠금을 풀 때(또는 잠금 없이 켜 둔 채 날이 바뀐 뒤 처음 점검 때) +5, 연속 일수(주말은 건너뛰어도
///   이어진다 — 끊기면 1일째부터, 벌칙 없음). 고양이가 앞발 인사를 하며 발도장이 찍히고 "발도장 쾅! 3일째 함께 출근 +5".
/// - 단계 이름(사용자 B안): 콩떡 → 찹쌀떡 → 모닝빵 → 식빵 → 통식빵 → 대왕식빵(웅크린 고양이 = 식빵).
/// - 저장: 설정 폴더의 cat-growth.txt, 고양이마다 한 줄("L|D 점수 남은 줄기 시간 동작별 마지막 점수 시각 3개"). 비밀이 아니다. 못 읽으면 0점에서 시작.
/// </summary>
internal static class CatGrowth
{
    public const int MaxScore = 100, StepScore = 20, Steps = MaxScore / StepScore;   // 단계 0..5
    private static readonly string[] Actions = { "I1", "I2", "I3" };          // 쓰다듬기 · 츄르 · 놀아 주기
    private static readonly int[] Points = { 6, 10, 8 };
    private const long CooldownMs = 3 * 60_000;
    private static readonly long DecayMs = Program.IsTestMode && long.TryParse(Environment.GetEnvironmentVariable("ONEKEY_TEST_CAT_DECAY_MS"), out long d) && d > 0 ? d : 10 * 60_000;

    private sealed class Cat { public int Score; public long Carry; public readonly long[] LastAward = new long[3]; }
    private static readonly Cat[] _cats = { new(), new() };   // 0 = 흰(밝은) 고양이, 1 = 검은 고양이
    private static Cat Of(bool light) => _cats[light ? 0 : 1];

    private static bool _loaded;
    private static ulong _lastUnbiased;  // 100 ns(잠든 시간 제외)

    private static string FilePath => Path.Combine(Config.Dir, "cat-growth.txt");

    public enum Habit { Backup, Master, Password, AutoLock, Stamp, Break }
    private static int _stampDay, _streak, _stampTotal;
    private static int _introCount, _introDay; private static bool _clicked;   // 처음 말풍선(0.5.20): 보인 날 수 · 마지막 날 · 오른쪽 클릭을 해 봤나

    /// <summary>"오른쪽 클릭하면 같이 놀 수 있어요" 를 오늘 보일까: 아직 한 번도 오른쪽 클릭하지 않았고, 서로 다른 날 세 번까지.</summary>
    // 앨범(0.5.20, 2026-10-10 사용자 · Codex 그림 12장): 고양이가 그 장면을 처음 보여 준 날
    public static readonly string[] AlbumKeys = { "wave", "groom", "belly", "meow", "wall", "stretch", "walk", "peek", "pet", "treat", "play", "stamp" };
    private static readonly Dictionary<string, int> _seen = new();
    public static bool IsSeen(string key) { Load(); return _seen.ContainsKey(key); }
    public static int SeenCount { get { Load(); return AlbumKeys.Count(_seen.ContainsKey); } }
    /// <summary>처음 본 장면이면 적어 두고 true.</summary>
    public static bool MarkSeen(string key) { Load(); if (_seen.ContainsKey(key) || Array.IndexOf(AlbumKeys, key) < 0) return false; _seen[key] = TodayNumber(); Save(); return true; }
    public static string? AlbumKeyOfClip(string clip) => clip switch
    {
        "M04" => "wave", "M05" => "groom", "M07" => "belly", "M08" => "meow", "M12" => "wall", "M17" => "stretch", "walk" => "walk", "I1" => "pet", "I2" => "treat", "I3" => "play", _ => null,
    };
    public static string AlbumName(string key) => key switch
    {
        "wave" => T.CatAlbumWave, "groom" => T.CatAlbumGroom, "belly" => T.CatAlbumBelly, "meow" => T.CatAlbumMeow, "wall" => T.CatAlbumWall, "stretch" => T.CatAlbumStretch,
        "walk" => T.CatAlbumWalk, "peek" => T.CatAlbumPeek, "pet" => T.CatMenuPet, "treat" => T.CatMenuTreat, "play" => T.CatMenuPlay, "stamp" => T.CatAlbumStamp, _ => key,
    };

    public static bool IntroDue { get { Load(); return !_clicked && _introCount < 3 && _introDay != Today(); } }
    public static void IntroShown() { Load(); _introCount++; _introDay = Today(); Save(); }
    /// <summary>고양이를 오른쪽 클릭했다(말풍선은 다시 안 나온다).</summary>
    public static void MarkClicked() { Load(); if (_clicked) return; _clicked = true; Save(); }

    // 수첩(0.5.20): 지금 상태
    public static int StampTotal { get { Load(); return _stampTotal; } }
    public static int PasswordToday { get { Load(); return _pwDay == Today() ? _pwCount : 0; } }
    /// <summary>그 보안 습관 점수를 다시 받을 때까지 남은 날(0 = 지금).</summary>
    public static int WaitDays(Habit h)
    {
        Load();
        long last = h == Habit.Backup ? _habBackup : h == Habit.Master ? _habMaster : 0, span = h == Habit.Backup ? 7 * Day : 30 * Day;
        long left = last + span - NowMs();
        return last == 0 || left <= 0 ? 0 : (int)Math.Ceiling(left / (double)Day);
    }   // 마지막 도장 날(DateOnly.DayNumber) · 연속 일수 · 모두 몇 번

    /// <summary>오늘 출근 도장을 아직 안 찍었다.</summary>
    public static bool StampDue { get { Load(); return _stampDay != TodayNumber(); } }
    public static int Streak { get { Load(); return _streak; } }

    private static int TodayNumber() => DateOnly.FromDateTime(DateTime.Now).DayNumber;

    /// <summary>연속인가: 마지막 도장 다음 날부터 어제까지가 모두 주말이면(또는 없으면) 이어진다.</summary>
    private static bool Continues(int last, int today)
    {
        if (last <= 0 || last >= today) return false;
        for (int d = last + 1; d < today; d++)
        {
            var w = DateOnly.FromDayNumber(d).DayOfWeek;
            if (w != DayOfWeek.Saturday && w != DayOfWeek.Sunday) return false;
        }
        return true;
    }
    private const long Day = 24L * 3600_000;
    private static long _habBackup, _habMaster;   // 마지막으로 점수를 준 때(UTC 밀리초)
    private static int _pwDay, _pwCount, _alDay;  // 날짜(yyyymmdd, 이 PC 시간) · 그날 비밀번호 바꾸기 점수 횟수 · 자동 잠금 점수 날짜
    private static readonly List<(int Points, Habit What)> _pending = new();

    /// <summary>보안 습관 보상: 받은 점수(이번 기간에 이미 받았으면 0). light = 지금 테마의 고양이(그 고양이 점수에 더한다).</summary>
    public static int AwardHabit(bool light, Habit h)
    {
        Update();
        long now = NowMs(); int today = Today();
        int pts = 0;
        switch (h)
        {
            case Habit.Backup: if (now - _habBackup >= 7 * Day) { _habBackup = now; pts = 20; } break;
            case Habit.Master: if (now - _habMaster >= 30 * Day) { _habMaster = now; pts = 20; } break;
            case Habit.Password:
                if (_pwDay != today) { _pwDay = today; _pwCount = 0; }
                if (_pwCount < 3) { _pwCount++; pts = 10; }
                break;
            case Habit.AutoLock: if (_alDay != today) { _alDay = today; pts = 5; } break;
            case Habit.Break: pts = 5; break;   // 쉬는 시간 친구(CatWidget.BreakTick — 한 시간에 한 번까지는 그쪽이 막는다)
            case Habit.Stamp:
            {
                int t = TodayNumber();
                if (_stampDay == t) break;
                _streak = Continues(_stampDay, t) ? _streak + 1 : 1;
                _stampDay = t; _stampTotal++;
                pts = 5;
                break;
            }
        }
        if (pts == 0) return 0;
        var c = Of(light);
        c.Score = Math.Min(MaxScore, c.Score + pts);
        _pending.Add((pts, h));
        Save();
        return pts;
    }

    /// <summary>고양이가 아직 알리지 않은 보상이 있다(다시 보일 때 예전 크기로 나왔다가 알린 뒤 커지게 — CatWidget.Evaluate).</summary>
    public static bool HasPending => _pending.Count > 0;

    /// <summary>아직 고양이가 알리지 않은 보상이 있으면 한 줄(예: "백업 고마워요! +20" — 여럿이면 합쳐서)을 내주고 비운다.</summary>
    public static bool TakePending(out string note) => TakePending(out note, out _);

    /// <summary>stamp = 출근 도장이 들어 있다(발도장을 찍고 앞발 인사 — 한 줄은 도장 글, 점수는 모두 더해서).</summary>
    public static bool TakePending(out string note, out bool stamp)
    {
        note = ""; stamp = false;
        if (_pending.Count == 0) return false;
        int sum = 0; foreach (var p in _pending) { sum += p.Points; if (p.What == Habit.Stamp) stamp = true; }
        string msg = stamp ? T.CatStampNote(_streak) : _pending.Count > 1 ? T.CatHabitMany : _pending[0].What switch
        {
            Habit.Backup => T.CatHabitBackup, Habit.Master => T.CatHabitMaster, Habit.Password => T.CatHabitPassword, Habit.Break => T.CatBreakBack, _ => T.CatHabitAutolock,
        };
        _pending.Clear();
        note = $"{msg} +{sum}";
        return true;
    }

    /// <summary>단계 이름(0..5): 콩떡 · 찹쌀떡 · 모닝빵 · 식빵 · 통식빵 · 대왕식빵.</summary>
    public static string StageName(int step) => Math.Clamp(step, 0, Steps) switch
    {
        0 => T.CatStage0, 1 => T.CatStage1, 2 => T.CatStage2, 3 => T.CatStage3, 4 => T.CatStage4, _ => T.CatStage5,
    };

    private static int Today() { var d = DateTime.Now; return d.Year * 10000 + d.Month * 100 + d.Day; }

    public static int Score(bool light) { Update(); return Of(light).Score; }
    /// <summary>0..5</summary>
    public static int Step(bool light) => Math.Min(Steps, Score(light) / StepScore);
    /// <summary>하트 줄의 찬 정도(0..5 — 소수는 다음 하트를 그만큼).</summary>
    public static double Level(bool light) => Math.Min(Steps, Score(light) / (double)StepScore);
    /// <summary>표시 크기 배율. canGrow = 그 고양이에게 상호작용 그림이 있다(CatWidget.HasInteractArt) — 없으면 늘 100 %.</summary>
    public static double Scale(bool light, bool canGrow) => canGrow ? 0.5 + 0.5 * Step(light) / Steps : 1.0;

    /// <summary>그 동작이 지금 점수를 받을 수 있으면 0, 아니면 남은 분(올림).</summary>
    public static int WaitMinutes(bool light, string action)
    {
        Load();
        int i = Array.IndexOf(Actions, action);
        if (i < 0) return 0;
        long left = Of(light).LastAward[i] + CooldownMs - NowMs();
        return left <= 0 ? 0 : (int)Math.Ceiling(left / 60_000.0);
    }

    /// <summary>동작을 시작했다: 쿨타임이 지났으면 점수. 점수가 바뀌었으면 true.</summary>
    public static bool Award(bool light, string action)
    {
        Update();
        int i = Array.IndexOf(Actions, action);
        if (i < 0 || WaitMinutes(light, action) > 0) return false;
        var c = Of(light);
        c.LastAward[i] = NowMs();
        int before = c.Score;
        c.Score = Math.Min(MaxScore, c.Score + Points[i]);
        Save();
        return c.Score != before;
    }

    /// <summary>켜져 있던 시간만큼 줄인다(두 고양이 모두).</summary>
    public static void Tick() => Update();

    private static void Update()
    {
        Load();
        ulong now = Unbiased();
        long el = (long)((now - _lastUnbiased) / 10_000);
        _lastUnbiased = now;
        if (el <= 0) return;
        bool changed = false;
        foreach (var c in _cats)
        {
            if (c.Score == 0) { c.Carry = 0; continue; }
            c.Carry += el;
            if (c.Carry < DecayMs) continue;
            int drop = (int)(c.Carry / DecayMs);
            c.Carry -= drop * DecayMs;
            c.Score = Math.Max(0, c.Score - drop);
            changed = true;
        }
        if (changed) Save();
    }

    private static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        _lastUnbiased = Unbiased();
        try
        {
            if (File.Exists(FilePath))
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string[] v = line.Trim().Split(' ');
                    if (v.Length >= 1 && v[0] == "A")   // 앨범: A 이름:날 …
                    {
                        foreach (string kv in v.Skip(1)) { string[] p = kv.Split(':'); if (p.Length == 2 && int.TryParse(p[1], out int dn) && Array.IndexOf(AlbumKeys, p[0]) >= 0) _seen[p[0]] = dn; }
                        continue;
                    }
                    if (v.Length >= 4 && v[0] == "T")   // 처음 말풍선: T 보인 수 마지막날 클릭함
                    {
                        int.TryParse(v[1], out _introCount); int.TryParse(v[2], out _introDay); _clicked = v[3] == "1";
                        continue;
                    }
                    if (v.Length >= 4 && v[0] == "S")   // 출근 도장: S 마지막날 연속 모두
                    {
                        int.TryParse(v[1], out _stampDay); int.TryParse(v[2], out _streak); int.TryParse(v[3], out _stampTotal);
                        if (_stampDay > TodayNumber()) _stampDay = 0;
                        continue;
                    }
                    if (v.Length >= 6 && v[0] == "H")   // 보안 습관: H 백업 마스터 비밀번호날짜 그날횟수 자동잠금날짜
                    {
                        long.TryParse(v[1], out _habBackup); long.TryParse(v[2], out _habMaster);
                        int.TryParse(v[3], out _pwDay); int.TryParse(v[4], out _pwCount); int.TryParse(v[5], out _alDay);
                        if (_habBackup > NowMs()) _habBackup = 0;
                        if (_habMaster > NowMs()) _habMaster = 0;
                        continue;
                    }
                    if (v.Length < 6 || v[0] is not ("L" or "D")) continue;
                    var c = Of(v[0] == "L");
                    if (!int.TryParse(v[1], out int s) || !long.TryParse(v[2], out long carry)) continue;
                    c.Score = Math.Clamp(s, 0, MaxScore);
                    c.Carry = Math.Clamp(carry, 0, DecayMs);
                    for (int i = 0; i < 3; i++) c.LastAward[i] = long.TryParse(v[3 + i], out long t) && t <= NowMs() ? t : 0;
                }
        }
        catch { foreach (var c in _cats) { c.Score = 0; c.Carry = 0; } }
        if (Program.IsTestMode && int.TryParse(Environment.GetEnvironmentVariable("ONEKEY_TEST_CAT_SCORE"), out int ts))   // 시험: 처음 점수(두 고양이)
            foreach (var c in _cats) c.Score = Math.Clamp(ts, 0, MaxScore);
        if (Program.IsTestMode && int.TryParse(Environment.GetEnvironmentVariable("ONEKEY_TEST_CAT_STREAK"), out int tsk) && tsk > 0)   // 시험: 어제까지 tsk 일 연속
        {
            int y = TodayNumber() - 1;
            while (DateOnly.FromDayNumber(y).DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) y--;
            _stampDay = y; _streak = tsk;
        }
        if (Program.IsTestMode && Environment.GetEnvironmentVariable("ONEKEY_TEST_CAT_SEEN") is { Length: > 0 } ts2)   // 시험: 이미 본 앨범 장면(쉼표로)
            foreach (string k in ts2.Split(',')) if (Array.IndexOf(AlbumKeys, k.Trim()) >= 0) _seen[k.Trim()] = TodayNumber();
        if (Program.IsTestMode && Environment.GetEnvironmentVariable("ONEKEY_TEST_CAT_HABIT") is { Length: > 0 } th)   // 시험: 보안 습관 보상을 처음에 받은 것처럼(쉼표로 여럿)
            foreach (string hn in th.Split(',')) if (Enum.TryParse(hn.Trim(), out Habit hh)) AwardHabit(true, hh);
    }

    private static void Save()
    {
        try
        {
            if (!Directory.Exists(Config.Dir)) return;
            string Line(string k, Cat c) => $"{k} {c.Score} {c.Carry} {c.LastAward[0]} {c.LastAward[1]} {c.LastAward[2]}";
            File.WriteAllText(FilePath, Line("L", _cats[0]) + "\n" + Line("D", _cats[1]) + "\n"
                + $"H {_habBackup} {_habMaster} {_pwDay} {_pwCount} {_alDay}\n" + $"S {_stampDay} {_streak} {_stampTotal}\n" + $"T {_introCount} {_introDay} {(_clicked ? 1 : 0)}\n" + "A" + string.Concat(_seen.Select(kv => $" {kv.Key}:{kv.Value}")) + "\n");
        }
        catch { }   // 못 써도 이번 실행 동안은 기억한다
    }

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static ulong Unbiased() => QueryUnbiasedInterruptTime(out ulong t) ? t : (ulong)Environment.TickCount64 * 10_000;

    [DllImport("kernel32.dll")] private static extern bool QueryUnbiasedInterruptTime(out ulong time);
}
