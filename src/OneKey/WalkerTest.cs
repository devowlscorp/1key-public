namespace OneKey;

/// <summary>
/// 시험 빌드(0.3.131-A, 2026-10-08 사용자): 상황 그림을 무작위가 아니라 목록 순서(1번 → 마지막)대로 재생하고, 마스코트 위에 지금 무엇을
/// 보여 주는지 큰 글자로 띄운다 — 흰 글자 = 동작(상황 그림 번호 / G 인사 / R·L 오른쪽·왼쪽 걷기 / P 매달리기 / S 서 있기), 노란 글자 = 그
/// 그림 띠의 장 번호(Assets 그림의 칸 번호, 걷기는 돌아서기부터 센 번호, 돌아서는 중이면 T 장). 사용자가 이상한 장을 번호로 알려 줄 수 있게.
/// 배포 빌드에서는 SeqTest = false(글자도 순서 재생도 없다).
/// </summary>
internal static unsafe partial class Walker
{
    internal const bool SeqTest = false;

    private static int _seqNext;
    private static string _clipLabel = "";
    // 늦게 온 틱(0.3.131-C): 틱 간격이 정한 간격보다 크게(20 ms 또는 절반 이상) 늦으면 센다 — 사용자가 끊김을 볼 때 이 수가 함께 늘면 앱 안의
    // 지연, 그대로면 화면 전송(원격 화면 등) 쪽이다. 둘째 줄 빨간 글자: !늦은 횟수 마지막 늦음(ms)
    private static long _lastTickTs;
    private static int _tickMsSeen, _lateN, _lateMs;

    // 기록 파일(0.3.131-D, 2026-10-08 사용자: 끊길 때 빨간 수가 오르는지 파일로 남겨 확인): %TEMP%\1Key-walker-timing.log 에 틱마다 한 줄 —
    // 시각, 앞 틱과의 간격(ms), 정한 간격, 이 틱에서 일한 시간(ms), 동작(번호 띠와 같은 글자)과 장, 늦음 표시. 2초 환경 점검(다른 스레드)의 걸린 시간도.
    // 쓰기는 다른 스레드에서 2초마다 모아서(그리기를 막지 않게). 20 MB 를 넘으면 처음부터 다시
    private static readonly System.Collections.Concurrent.ConcurrentQueue<string> _logQ = new();
    private static long _logLastFlush;
    private static int _logFlushing;
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "1Key-walker-timing.log");

    private static void LogLine(string s)
    {
        if (!SeqTest) return;
        _logQ.Enqueue(DateTime.Now.ToString("HH:mm:ss.fff") + " " + s);
    }

    private static void LogTick(long t0)
    {
        if (!SeqTest) return;
        long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        double work = (t1 - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        string what = _act switch
        {
            Act.Clip => _clipLabel + " " + _frame,
            Act.Greet => "G " + MascotGreet.Cell(_frame, _greetN),
            Act.Walk => (_dir > 0 ? "R " : "L ") + (TurnN + Math.Clamp(_frame, 0, LeadN + LoopN - 1)),
            Act.TurnOut or Act.TurnIn => (_dir > 0 ? "R T" : "L T") + _frame,
            Act.Peek => "P",
            _ => "S",
        };
        LogLine($"tick dt {_lastDt:0.0} want {_tickMs} work {work:0.0} {what}{(_lastLate ? " LATE" : "")} n{_lateN}");
        if (t1 - _logLastFlush > 2 * System.Diagnostics.Stopwatch.Frequency && Interlocked.CompareExchange(ref _logFlushing, 1, 0) == 0)
        {
            _logLastFlush = t1;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var sb = new System.Text.StringBuilder();
                    while (_logQ.TryDequeue(out var l)) sb.Append(l).Append("\r\n");
                    var fi = new FileInfo(LogPath);
                    if (fi.Exists && fi.Length > 20_000_000) File.Delete(LogPath);
                    File.AppendAllText(LogPath, sb.ToString());
                }
                catch { }
                finally { Interlocked.Exchange(ref _logFlushing, 0); }
            });
        }
    }

    private static void LogEnv(long e0, bool redo)
    {
        if (!SeqTest) return;
        double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - e0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        LogLine($"env {ms:0.0} ms{(redo ? " redo" : "")} (other thread)");
    }

    private static double _lastDt;
    private static bool _lastLate;

    private static void NoteTickTiming()
    {
        long ts = System.Diagnostics.Stopwatch.GetTimestamp();
        _lastDt = _lastTickTs != 0 ? (ts - _lastTickTs) * 1000.0 / System.Diagnostics.Stopwatch.Frequency : 0;
        _lastLate = false;
        if (_lastTickTs != 0 && _tickMsSeen == _tickMs)
        {
            double late = _lastDt - _tickMs;
            if (late > Math.Max(20, _tickMs * 0.5)) { _lateN++; _lateMs = (int)Math.Round(late); _lastLate = true; }
        }
        _lastTickTs = ts; _tickMsSeen = _tickMs;
    }

    /// <summary>글자 띠 높이(창 위쪽에 이만큼 더 둔다). 시험 빌드가 아니면 0.</summary>
    private static int LabelRoom() => SeqTest ? 2 * 7 * LabelScale() + 3 * LabelScale() + 2 : 0;   // 두 줄(둘째 줄: 늦은 틱)

    private static int LabelScale() => Math.Max(2, _fh / 16);

    /// <summary>글자 n 개 띠의 폭(px).</summary>
    private static int LabelWidth(int n) => n * 6 * LabelScale() + LabelScale();

    /// <summary>목록 순서대로 다음 그림(끝나면 처음부터). 특별 동작(x:1 — 매달리기·자전거)도 순서에 넣는다.</summary>
    private static int SeqPick(int count)
    {
        int i = _seqNext % Math.Max(1, count);
        _seqNext = i + 1;
        return i;
    }

    // 5×7 글자(한 줄 5비트, 왼쪽이 높은 비트)
    private const string FontChars = "0123456789GLRPST!";
    private static readonly byte[] FontBits =
    {
        0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E,   // 0
        0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E,   // 1
        0x0E, 0x11, 0x01, 0x02, 0x04, 0x08, 0x1F,   // 2
        0x1F, 0x02, 0x04, 0x02, 0x01, 0x11, 0x0E,   // 3
        0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02,   // 4
        0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x0E,   // 5
        0x06, 0x08, 0x10, 0x1E, 0x11, 0x11, 0x0E,   // 6
        0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08,   // 7
        0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E,   // 8
        0x0E, 0x11, 0x11, 0x0F, 0x01, 0x02, 0x0C,   // 9
        0x0E, 0x11, 0x10, 0x17, 0x11, 0x11, 0x0F,   // G
        0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x1F,   // L
        0x1E, 0x11, 0x11, 0x1E, 0x14, 0x12, 0x11,   // R
        0x1E, 0x11, 0x11, 0x1E, 0x10, 0x10, 0x10,   // P
        0x0F, 0x10, 0x10, 0x0E, 0x01, 0x01, 0x1E,   // S
        0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04,   // T
        0x04, 0x04, 0x04, 0x04, 0x04, 0x00, 0x04,   // !
    };

    /// <summary>내보낼 그림(_xbits) 맨 위에 글자 띠를 그린다(반투명 검은 바탕 + 글자). winX = 창 왼쪽(화면 좌표).</summary>
    private static void DrawLabel(int winX)
    {
        if (!SeqTest || _xbits == 0 || _vw == 0) return;
        string big, small;
        switch (_act)
        {
            case Act.Clip: big = _clipLabel; small = _frame.ToString(); break;
            case Act.Greet: big = "G"; small = MascotGreet.Cell(_frame, _greetN).ToString(); break;
            case Act.Walk: big = _dir > 0 ? "R" : "L"; small = (TurnN + Math.Clamp(_frame, 0, LeadN + LoopN - 1)).ToString(); break;
            case Act.TurnOut or Act.TurnIn: big = _dir > 0 ? "R" : "L"; small = "T" + Math.Clamp(_frame, 0, TurnN - 1); break;
            case Act.Peek: big = "P"; small = ""; break;
            default: big = "S"; small = ""; break;
        }
        string text = small.Length > 0 ? big + " " + small : big;
        string late = _lateN > 0 ? "!" + _lateN + " " + _lateMs : "";
        int s = LabelScale(), pad = s;
        int tw = Math.Max(text.Length, late.Length) * 6 * s - s, bw = tw + 2 * pad, bh = (late.Length > 0 ? 15 * s : 7 * s) + 2 * pad;
        int mid = (int)Math.Round(_x + _cw / 2.0) - winX;
        int bx = Math.Clamp(mid - bw / 2, 0, Math.Max(0, _vw - bw)), by = 0;
        uint* o = (uint*)_xbits;
        for (int y = by; y < by + bh && y < _vh; y++)
            for (int x = bx; x < bx + bw && x < _vw; x++) o[y * _vw + x] = 0xC0000000;   // 미리 곱한 알파: 75 % 검정
        string all = text + "\n" + late;
        int line = 0, col = 0;
        for (int c = 0; c < all.Length; c++)
        {
            if (all[c] == '\n') { line = 1; col = 0; continue; }
            int gi = FontChars.IndexOf(all[c]);
            int at = col++;
            if (gi < 0) continue;
            uint color = line == 1 ? 0xFFFF5050 : c < big.Length ? 0xFFFFFFFF : 0xFFFFE040;   // 늦은 틱 = 빨강
            int gx = bx + pad + at * 6 * s, gy = by + pad + line * 8 * s;
            for (int r = 0; r < 7; r++)
                for (int k = 0; k < 5; k++)
                {
                    if ((FontBits[gi * 7 + r] & (0x10 >> k)) == 0) continue;
                    for (int yy = 0; yy < s; yy++)
                        for (int xx = 0; xx < s; xx++)
                        {
                            int px = gx + k * s + xx, py = gy + r * s + yy;
                            if (px >= 0 && px < _vw && py >= 0 && py < _vh) o[py * _vw + px] = color;
                        }
                }
        }
    }
}
