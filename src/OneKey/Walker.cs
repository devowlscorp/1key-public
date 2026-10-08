using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 작업 표시줄 위를 걷는 마스코트(선택 기능, 기본 꺼짐 — 2026-10-06 사용자 결정, Codex 09:43 조건).
/// - 창: 마스코트 크기만 한 층 창(픽셀 알파, 투명 픽셀은 클릭이 뒤로 통과), 항상 위, 포커스를 받지 않음(WS_EX_NOACTIVATE + MA_NOACTIVATE), 작업 표시줄 단추 없음.
/// - 자리: 주 모니터, 작업 표시줄이 아래쪽이고 자동 숨김이 아닐 때만. 발이 작업 영역 아래 끝(작업 표시줄 위 가장자리)에 닿는다.
/// - 보이는 때는 App 이 정한다(<see cref="SetWanted"/>: 켜 둠·잠금 풀림·창 닫힘). 여기서는 환경(<see cref="EnvOk"/>)을 더 본다 — 알림을 받는 상태
///   (SHQueryUserNotificationState 가 ACCEPTS_NOTIFICATIONS)일 때만, 시작 메뉴·작업 표시줄 메뉴·알림 창 같은 시스템 패널이 앞에 있으면 숨김, 모르면 숨김.
/// - 움직임: 천천히 걷다가(약 20 논리 px/s) 몇 초씩 쉬고, 쉴 때 가끔 인사 한 번. 걷기 그림이 없어 인사 그림 첫 장을 들썩이며 옮긴다(임시).
/// - 클릭: 마스코트 안에서 누르고 그 안에서 떼었을 때만(누르는 동안 멈춤). 밖에서 떼기·캡처 잃음·숨김이면 취소. App 에 clickMsg 를 보낸다.
/// - 자원: 그림은 보일 때 한 번 줄여 그린 띠(DIB 한 장, 오른쪽·왼쪽 두 줄)만 두고 원본은 바로 해제. 숨으면 애니메이션·그리기 타이머를 멈춘다
///   (보고 싶은 상태인데 환경 때문에 숨었으면 2초 점검 타이머만). 모든 자원은 이 클래스가 만들고 <see cref="Destroy"/>/<see cref="FreeArt"/> 한 곳에서 해제한다.
/// </summary>
internal static unsafe partial class Walker
{
    public const string ClassName = "OneKeyWalker";
    private const string HangClip = "18";   // 담벼락 내려가 매달리기(Codex 3차 기준 그림 K1~K4 사이 영상, 앞으로·거꾸로)
    private static int _greetN = 19;   // 인사 그림 장 수(mascot_greet.txt, 0.3.121 부터 16 fps 약 96장)
    private const int HeightLogical = 44, BobLogical = 2, SpeedLogical = 18;
    // 걷기 그림(Assets/walk.png, tools/design/mascot_walk.py): 앞쪽 _turnN 장 = 정면에서 오른쪽으로 돌아서기, 뒤쪽 _loopN 장 = 오른쪽 보고 걷기 반복
    // 상황 그림(Assets/clips) 재생 속도. 0.3.119: 6 → 16(영상 원래 속도, 무작위 없음) — 6 fps 는 100 ms(AnimMs)마다 그리면서 장마다 100/200 ms 로
    // 번갈아 뚝뚝 끊겨 보였고, 빠른 동작은 10 fps 도 끊겼다(2026-10-07 사용자 — 비교 미리보기 뒤 16 fps 선택). 상황 그림을 재생하는 동안만
    // ClipTickMs 마다 그린다(걷기·인사는 그대로 AnimMs). 그림은 16 fps 로 뽑았다(docs/design/mascot-clips-next/round2/flf_extract2.py)
    private const int ClipFps = 16, ClipTickMs = AnimMs;
    private static int _tickMs;   // 지금 TimerAnim 간격
    private const nuint TimerAnim = 1, TimerCheck = 2;
    // 0.3.122: 100 → 62 ms(16 fps) — 걷기도 영상의 한 걸음 주기를 16 fps 그대로(2026-10-07 사용자: 발이 순간이동, 다리 앞뒤 구분 없음)
    private const int AnimMs = 62, RestTickMs = 500, CheckMs = 2000;

    private static nint _hwnd, _owner;
    private static uint _clickMsg;
    private static bool _registered, _wanted, _shown, _pressed;
    // 기본 그림 띠: 칸 크기 _cw × _fh(모든 그림을 칸 아래 가운데에), 세 줄 — 0 오른쪽으로 걷기 4장, 1 왼쪽으로 걷기 4장, 2 인사 19장(0번 = 서 있기)
    private static nint _mem, _dib, _oldBmp, _bits;
    private static int _fw, _cw, _fh, _bob, _dpi, _turnN, _loopN, _baseCells;
    // 왼쪽 걷기 그림의 장 수(0.3.122 — 오른쪽과 다를 수 있다: 영상마다 한 걸음 주기 길이가 다르다). 뒤집어 쓸 때는 오른쪽과 같다
    private static int _turnL, _loopL;
    // 돌아서기와 반복 사이의 "걸어 나가기" 장(0.3.123): 걷는 영상에서 반복이 시작되기 전 장면. 한 번만, 걸으면서(앞으로 가면서) 보인다 —
    // 0.3.122 는 이 장면을 돌아서기에 넣어 제자리걸음을 하다가 튀어 나갔다(2026-10-07 사용자)
    private static int _leadN, _leadL;
    private static int TurnN => _dir > 0 ? _turnN : _turnL;
    private static int LoopN => _dir > 0 ? _loopN : _loopL;
    private static int LeadN => _dir > 0 ? _leadN : _leadL;
    /// <summary>걸음 수 → 걷기 줄의 칸(돌아서기 뒤부터): 처음 LeadN 장은 한 번, 그다음은 LoopN 장 반복.</summary>
    private static int WalkCell(int step) => step < LeadN ? step : LeadN + (step - LeadN) % LoopN;
    // 지금 재생 중인 상황 그림(한 번에 하나만 읽고 끝나면 해제): 한 줄 _clipFrames 장
    private static nint _cmem, _cdib, _coldBmp, _cbits;
    private static int _clipFrames, _clipLast = -1, _ccw, _cdh;   // _ccw·_cdh = 이 상황 그림의 칸 폭·높이(누워 있기는 넓고, 점프는 서 있을 때보다 높다)
    private static int _clipFps = ClipFps;
    private static readonly List<int> _clipBag = new();   // 아직 안 나온 상황 그림(다 나오면 새로 섞는다 — 같은 것이 몰려 나오지 않게)
    private static long _clipStart;
    private static ClipInfo[]? _clipList;
    /// <summary>
    /// clips.txt 한 줄. Jump(j:1) = 발이 뜬 구간을 점프로(원래 높이 + 포물선, 빠르게). Sit(s:첫장-끝장:px) = 그 장들을 원본 px 만큼 더 내려
    /// 엉덩이를 선에(발끝은 선 아래에서 잘린다). Move(m:첫장-끝장:px) = 그 장들이 재생되는 동안 원본 px 만큼 옆으로 간다(구르기).
    /// Dir(d:1 / d:-1) = 그 방향으로만 가는 그림(좌우를 따로 그린 짝 — 뒤집지 않는다, 같은 g: 묶음의 반대쪽 짝과 바꿔 쓴다).
    /// </summary>
    private readonly record struct ClipInfo(string Name, int Frames, int CellW, int H, string Group, int Hold, int HoldMs, int Prob,
        bool Jump, int SitA, int SitB, int SitPx, int MoveA, int MoveB, int MovePx, int Dir, bool PingPong = false, bool Special = false);
    // 창 폭: 걷기 칸(_cw)과 가장 넓은 상황 그림 칸 중 큰 것. 그림은 늘 이 폭의 칸에 마스코트 가운데를 맞춰 내보낸다(0.3.93 — 누운 그림을 줄이지 않게)
    private static int _vw, _vh, _baseH;   // 창 폭·높이(가장 넓은·높은 그림에 맞춤, 발은 아래 끝), _baseH = 그림 원본의 서 있는 키(걷기 그림 높이)
    // 무작위(2026-10-06 사용자: 반복된다는 느낌이 없게, 예측되지 않게): 걸음마다 속도 배수, 걷기 그림 진행(소수), 쉬기를 이어서 한 번 더 했는지
    private static double _speedMul = 1, _walkPhase;
    // 걷기 빠르기: 틱마다 정확히 한 장씩 넘기고, 빠르기는 틱 간격으로 정한다(0.3.131-B — 예전에는 0.8~1.25배를 장 번호에 더해
    // 0.8배면 다섯 틱마다 같은 장이 두 번, 1.25배면 네 틱마다 한 장을 건너뛰어 다리가 뚝뚝 끊겼다: 2026-10-08 사용자).
    // 간격은 Windows 기본 타이머 눈금(15.625 ms)의 3·4·5배에 맞춘다 — 눈금 사이 값이면 틱이 들쭉날쭉하다.
    private static readonly int[] WalkTicks = { 78, 62, 46 };   // 0.8배·1배·1.33배
    private static int _walkTickMs = AnimMs;
    // 상황 그림 장 넘기기: 틱 수로 센다(0.3.131-B). 예전에는 TickCount64(15.6 ms 눈금)로 지난 시간을 재서, 장 길이(62.5 ms)와 틱 간격이
    // 같은 탓에 눈금이 장 경계에 걸리면 한 장이 두 배로 머물고 다음 장을 건너뛰었다(동작 중간에 장 간격이 길어 끊김: 2026-10-08 사용자)
    private static long _clipTicks, _clipTickKey = -1;
    // 2초 환경 점검은 다른 스레드에서(0.3.131-C): SHAppBarMessage 는 작업 표시줄(탐색기)에 보내는 메시지라 탐색기가 잠깐 바쁘면 그동안
    // 그리기 틱이 막혀 동작 중간에 장이 늦게 넘어갔다 — 같은 동작인데 끊기는 자리가 매번 달랐다(2026-10-08 사용자)
    private const uint WM_ENVDONE = 0x8031;   // WM_APP + 0x31: 점검 끝(wParam 1 = 다시 맞춰야 함)
    private static int _envBusy;
    private static bool _timerFine;           // timeBeginPeriod(1) 중(보이는 동안만)
    private static bool _chained;
    private enum Act { Walk, Stand, Greet, Clip, TurnOut, TurnIn, Peek }
    // 매달리기(2026-10-07 사용자: 눕기 대신 작업 표시줄 아래로 천천히 내려가 두 손으로 매달렸다가 다시 올라오기). 그림은 손을 얼굴 옆에 든
    // 원본 한 장(Assets/mascot_peek.png, tools/design/mascot_peek.py)을 PeekDepth 만큼 내려 그리고 창 아래 끝(작업 표시줄 위 가장자리)에서
    // 자른다 — 그 선이 손 바로 아래를 지나 머리와 두 손만 걸쳐 보인다(0.26 = 미리보기로 고른 값).
    // 0.3.115(사용자 피드백 — 손까지 함께 움직이면 매달린 것이 아니라 숨는 것): 손만 뗀 그림(mascot_peek_hands.png, 넷째 줄 둘째 칸)을 따로 그린다.
    // 손이 가장자리에 닿은 뒤로는 손은 그 자리에 두고 몸만 조금 더 내려가고(턱이 선 가까이)·흔들리고·끌어 올려진다. 몸의 손은 선 아래로 내려가 잘린다
    // 0.3.111: 0.26 → 0.24, 두 손이 표시줄 위로 조금 더 보이게. 0.3.117: Codex 새 그림은 손이 더 위(얼굴 옆)에 있어 0.29 — 선이 손 바로 아래를 지난다
    private const double PeekDepth = 0.29;
    private const int PeekDownMs = 1400, PeekGripMs = 500, PeekDipMs = 260, PeekPullMs = 450, PeekUpMs = 900;
    private static bool _peekOk, _peekHands;   // 매달리기 그림이 띠 넷째 줄에 있다 / 손만 뗀 그림이 그 옆 칸에 있다
    private static int _peekHoldMs, _sink, _sway;   // 매달려 있는 시간, 지금 내려간 깊이(px — Compose 가 그만큼 내려 그리고 아래를 자른다), 좌우 흔들림(px)
    // 상황 그림을 장마다 위아래로 옮겨 그릴 양(px, + 아래). 모든 장은 가장 아래 불투명 줄을 작업 표시줄 선에 붙인다(0.3.115 사용자 피드백 —
    // 점프가 아닌 동작도 발이 떠 보였다: 커피의 떨어지는 컵 한 장이 땅이 되어 나머지 장이 6px 떠 있었고, 그 뜬 장면을 다시 띄웠다).
    // 점프(j:1)만 발이 뜬 첫 장~마지막 장을 한 구간으로 원래 높이 + 포물선만큼 위로(0.3.111 — 원본 점프가 작아 44px 에서 안 보였다; 0.3.114 —
    // 같은 높이로 더하니 공중에 걸린 듯 보여 포물선 + 공중 장면 빠르게). 앉기(s:)는 엉덩이가 선에 오게 더 아래로
    private static int[] _clipDy = Array.Empty<int>();
    private static bool[] _clipAir = Array.Empty<bool>();   // 점프의 공중 장면: 보통 장의 AirFrameScale 배 시간만
    // 옆으로 가는 동작(m: — 구르기, 0.3.115): 이번 재생의 시작 자리·갈 거리(px, - = 왼쪽). 왼쪽이면 그림을 좌우로 뒤집는다(오른쪽으로 구르는 그림)
    private static double _clipX0, _clipTravel;
    private static int _clipMoveA = -1, _clipMoveB = -1, _clipDir;   // _clipDir: 0 = 어느 쪽이든(왼쪽이면 뒤집기), ±1 = 그 쪽으로만(짝 그림)
    private static bool _clipMirror;
    // 장마다 시작 시각(ms, 마지막 칸 = 끝): 재생 속도·특징 자세 멈춤·공중 장면 빠르게(0.3.114 — 공중에 걸린 듯 보이지 않게)를 한 표로
    private static long[] _clipT = Array.Empty<long>();
    private static int _clipSteps;   // 재생 걸음 수: 보통 = 장 수, pp:1 = 앞으로 + 거꾸로(2N−1)
    /// <summary>재생 걸음 → 그림 장(pp:1 이면 끝에서 되돌아온다).</summary>
    private static int ClipFrameAt(int step) => step < _clipFrames ? step : Math.Max(0, 2 * _clipFrames - 2 - step);
    private const double AirFrameScale = 0.6;   // 공중 장면은 보통 장의 0.6배 시간만(떨어지는 느낌)
    private static int _clipHold = -1, _clipHoldMs;   // 이 상황 그림에서 멈춰 보여 줄 장(특징 자세, clips.txt "h:장:ms")
    private static bool _reverseAfterTurn;   // 정면으로 돌아선 뒤 반대쪽으로 다시 돌아선다(끝에 닿음)
    private static Act _act;
    private static int _testClips;
    // 전환: 바뀌기 직전 그림을 복사해 두고 FadeMs 동안 새 그림과 섞는다(미리 곱한 알파라 채널별 선형 섞기면 된다)
    private const int FadeMs = 300;
    private static uint[]? _fromPx;
    private static long _fadeStart;
    private static nint _xmem, _xdib, _xold, _xbits;   // 내보낼 그림(창 _vw × _vh): 지금 칸을 제자리(아래 끝)에 놓고, 전환 중이면 앞 그림과 섞는다
    private static bool _clipRelease;                 // 재생이 끝난 상황 그림: 섞기가 끝난 뒤 해제
    // 자리·움직임
    private static Native.RECT _work;
    private static double _x;
    private static int _dir = 1, _frame, _ticks;
    private static bool _resting;
    private static long _phaseEnd;
    private static readonly Random _rnd = new();
    private static int _testMoves, _testShows;
    /// <summary>
    /// 지금 보이지 않는 이유(진단용, 비밀 없음 — 시험 모드가 아니어도 본창 속성 OneKeyWalkerWhy 로 남긴다):
    /// 0 보임, 1 보고 싶지 않음(앱 상태), 2 알림 상태 아님(전체 화면·방해 금지 등), 3 작업 표시줄 위치·자동 숨김, 4 주 모니터·작업 영역 확인 실패,
    /// 5 배율 확인 실패, 6 시스템 패널·앞 창 없음, 7 좁은 작업 영역, 8 그림 준비 실패, 9 창 만들기 실패, 10 예외, 11 화면 내보내기 실패.
    /// </summary>
    private static int _why = 1;
    // 보일 때마다 1 씩 는다: 클릭 메시지에 담아, 그사이 숨었다 다시 보인 뒤의 낡은 클릭을 받지 않는다(Codex R83-2)
    private static int _showGen;

    /// <summary>App 이 클릭 메시지를 받았을 때: 그 클릭이 지금 보이는 마스코트의 것이고 지금도 보여도 되는 환경인가.</summary>
    public static bool AcceptClick(int gen) => CatWidget.Use ? CatWidget.AcceptClick(gen) : _shown && gen == _showGen && !_pressed && EnvOk(out _, out _);

    /// <summary>App 이 한 번 부른다: 알림을 받을 창과 클릭 메시지.</summary>
    public static void Init(nint owner, uint clickMsg) { _owner = owner; _clickMsg = clickMsg; CatWidget.Init(owner, clickMsg); }

    /// <summary>App 이 보고 싶은지(켜 둠·잠금 풀림·창 닫힘·복구 상태 아님). 아니면 바로 숨고 모든 타이머를 멈춘다.</summary>
    public static void SetWanted(bool want)
    {
        if (CatWidget.Use) { CatWidget.SetWanted(want); return; }   // 공개판: 걷기 그림 대신 고양이
        if (want == _wanted && (!want || _shown || _hwnd != 0)) { if (want) Evaluate(); return; }
        _wanted = want;
        if (!want) { HideAll(); return; }
        Evaluate();
    }

    /// <summary>화면 구성·작업 영역·배율·탐색기 재시작: 보이는 중이면 숨긴 상태로 다시 맞춘 뒤 다시 판단한다(예전 자리에 잠깐 보이지 않게).</summary>
    public static void EnvChanged()
    {
        if (CatWidget.Use) { CatWidget.EnvChanged(); return; }
        if (!_wanted) return;
        if (_shown) Hide(keepCheck: true);
        FreeArt();
        Evaluate();
    }

    public static bool IsShown => CatWidget.Use ? CatWidget.IsShown : _shown;

    /// <summary>끝낼 때: 타이머·창·그림 모두.</summary>
    public static void Destroy()
    {
        if (CatWidget.Use) { CatWidget.Destroy(); return; }
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
            if (_shown) Hide(keepCheck: true);
            _why = why;
            RetryLater();   // 환경이 돌아오면 다시 보이게(저빈도 점검만)
            return;
        }
        if (_shown && dpi == _dpi && work.left == _work.left && work.right == _work.right && work.bottom == _work.bottom) return;
        if (_shown) Hide(keepCheck: true);
        if (!EnsureWindow()) { _why = 9; SetTestProps(); return; }
        if (_mem == 0 || dpi != _dpi) { FreeArt(); _dpi = dpi; if (!BuildArt()) { FreeArt(); _why = 8; RetryLater(); return; } }   // 실패해도 멈추지 않고 2초 뒤 다시(0.3.83 결함)
        _work = work;
        var (rmin, rmax) = Range();
        if (_x == 0 || _x < rmin || _x > rmax) _x = rmin + _rnd.Next(Math.Max(1, (int)(rmax - rmin)));
        StartWalk();
        if (!Render()) { FreeArt(); _why = 11; RetryLater(); return; }   // 내보내기 실패는 보임으로 치지 않는다(R83-1)
        _shown = true; _testShows++; _why = 0; _showGen++;
        Native.ShowWindow(_hwnd, 4 /* SW_SHOWNOACTIVATE */);
        KeepOnTop();
        Native.KillTimer(_hwnd, TimerCheck);
        // 틱: 고해상도 대기 타이머 스레드(WalkerPrep.cs, 0.3.131-E). 만들 수 없으면 예전 SetTimer
        _tickMs = AnimMs;
        if (!StartTicks(AnimMs)) Native.SetTimer(_hwnd, TimerAnim, AnimMs, 0);
        _lastTickTs = 0;
        PrefetchNext();
        LogLine($"show dpi {_dpi} timer1ms {_timerFine} work {_work.left}..{_work.right}");
        SetTestProps();
    }

    // ------------------------------------------------------------------ 정시 알림 (0.3.126)

    // 2026-10-07 사용자 콘티 + Codex 16:27: 정시 10초 전에 하던 것을 멈추고 오른쪽 끝까지 빠르게(걷기의 2배) 간 뒤 화면 오른쪽 밖으로 나갔다가
    // 다시 걸어 들어와 정면을 보면, 머리 위에 큰 플립시계(FlipClock, 따로 떠 있는 창)가 나타나 :59 → :00 으로 넘어가고 5초 뒤 사라진다.
    // 시계 숫자는 늘 그 순간의 실제 시각(늦게 도착하면 넘어가는 동작 없이 지금 시각). 자전거 그림이 준비되면 "빠르게 가기"를 자전거로 바꾼다.
    // Pull = 작업 표시줄 아래에서 자전거를 꺼내 올라타기(clips 19), Ride = 페달 반복(clips 20)으로 오른쪽 끝을 지나 화면 밖까지(사용자: 내리지 않고
    // 탄 채 나간다). 자전거 그림이 없으면 Go/Exit = 빠른 걸음.
    private enum ClockPhase { None, Go, Exit, Pull, Ride, Away, Enter, Turn, Show, Leave }
    private const string BikePullClip = "19", BikeRideClip = "20";
    // 시계를 띄우는 손짓(0.3.131-K, 2026-10-08 사용자: 마법사처럼 두 손으로 시계를 따라가며 팔을 위로 올려 띄운 듯, 사라질 때도): clips 21 —
    // h: 장 앞까지 = 팔을 들어 올림(시계가 올라오는 만큼), h: 장부터 끝까지 = 든 채로 손짓(앞뒤로 되풀이). 사라질 때는 올리던 장을 거꾸로.
    // 지금 그림은 박수 응원(10)의 0~44장을 임시로 쓴다 — Codex 가 새로 그린 뒤 바꾼다
    private const string MagicClip = "21";
    private static bool _magic;
    private static ClockPhase _clock;
    private static DateTime _clockHour, _clockDoneHour, _clockShowFrom;
    private static long _clockAt;
    private static bool _offscreen;   // 화면 오른쪽 밖으로 나가는 중: 창을 작업 영역 안으로 밀지 않고, 작업 영역 오른쪽 밖은 지운다(옆 모니터에 보이지 않게)
    private const double ClockSpeed = 2.0;
    // 자전거 속도(걷기의 배수): 정시에 맞춰 도착하도록 남은 시간으로 정한다(0.3.128 — 사용자 녹화: 걷기 2배로는 늦어 정시가 지나 도착)
    private const double BikeMinSpeed = 3.0, BikeMaxSpeed = 9.0;
    private const int ClockArriveMs = 2600;   // 화면 밖에서 돌아와 돌아서기까지(Away + Enter + Turn) 어림
    private static double _bikeSpeed = BikeMinSpeed;
    private const int ClockHoldMs = 5000, ClockFlipMs = 600, ClockAwayMs = 600;   // 넘어가기 0.45 → 0.6초(0.3.131-F, 잘 보이게)
    // 푯말(0.3.131-F, 2026-10-08 사용자): 오른쪽 아래 모서리에서 곡선을 그리며 올라와(ClockAppearMs) :59 를 보이다가 정시에 :00 으로 넘어가고(늦게
    // 왔으면 자리 잡고 ClockPreFlipMs 뒤), 5초 뒤 같은 곡선으로 모서리로 내려가 사라진다(ClockLeaveMs). 그동안 틱은 ClockAnimMs(약 64 번/초)
    private const int ClockAppearMs = 750, ClockLeaveMs = 600, ClockPreFlipMs = 700, ClockAnimMs = 16;   // 0.3.131-G: 포물선이 보이게 조금 길게
    private const int ClockLeadMs = 6500;   // 정시 몇 ms 전에 돌아와 :59 를 띄울지 — 자전거 빠르기를 여기에 맞춘다
    private static DateTime _clockFlipAt;
    private static long _clockTicks;
    private static bool _clockLight;

    /// <summary>정시 20초 전(:59:40~:59:59)이고 이 시각을 아직 안 했으면 true. 시험: ONEKEY_TEST_CLOCK=1 이면 처음 한 번 바로.</summary>
    private static bool ClockDue(DateTime now)
    {
        DateTime next = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0).AddHours(1);
        if (Program.IsTestMode && Environment.GetEnvironmentVariable("ONEKEY_TEST_CLOCK") == "1" && _clockDoneHour == default) return true;
        return now.Minute == 59 && now.Second >= 40 && next != _clockDoneHour;   // 0.3.131-F: :59:40 출발(정시 전에 돌아와 :59 를 보이게)
    }

    private static void StartClock(DateTime now)
    {
        _clockHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0).AddHours(now.Minute == 59 ? 1 : 0);
        _clockDoneHour = _clockHour;
        if (_act == Act.Clip) { BeginFade(); FreeClip(); }
        _resting = false; _chained = false;
        _speedMul = ClockSpeed;
        AnimTick(AnimMs);
        _dir = 1;
        if (LoadClip(BikePullClip))
        {
            BeginFade();
            _act = Act.Clip; _clipStart = NowMs(); _clipFps = ClipFps; _frame = 0; _clockTicks = 0;
            BuildClipTimes();
            _clock = ClockPhase.Pull;
            return;
        }
        if (_act == Act.Walk) { }                        // 이미 걷는 중: 그대로 빨라진다
        else { _act = Act.TurnOut; _frame = 0; _walkPhase = 0; }
        _clock = ClockPhase.Go;
    }

    private static void EndClock()
    {
        FlipClock.Hide();
        if (_magic) { _magic = false; if (_act == Act.Clip) FreeClip(); }   // 손짓 그림을 내려놓는다(FreeClip 이 서 있기로)
        _clock = ClockPhase.None; _offscreen = false; _speedMul = 1;
    }

    /// <summary>정시 알림 중의 한 틱(now = 고해상도 ms, <see cref="NowMs"/>). 다 끝나면 쉬기로 돌아간다.</summary>
    private static void ClockTick(long now)
    {
        double step = SpeedLogical * _speedMul * _dpi / 96.0 * AnimMs / 1000.0;
        var (_, max) = Range();
        switch (_clock)
        {
            case ClockPhase.Go or ClockPhase.Exit or ClockPhase.Enter:
                if (_act == Act.TurnOut) { _frame++; if (_frame >= TurnN) { _act = Act.Walk; _frame = 0; _walkPhase = 0; } Render(); return; }
                _x += _dir * step;
                _walkPhase += _speedMul; _frame = WalkCell((int)_walkPhase); _testMoves++;
                if (_clock == ClockPhase.Go && _x >= max) { _clock = ClockPhase.Exit; _offscreen = true; }
                else if (_clock == ClockPhase.Exit && _x > _work.right + 2) { _clock = ClockPhase.Away; _clockAt = now; return; }
                else if (_clock == ClockPhase.Enter && _x <= max - _cw * 0.4)
                {
                    _offscreen = false;
                    _act = Act.TurnIn; _frame = TurnN - 1; _clock = ClockPhase.Turn;
                }
                Render();
                return;
            case ClockPhase.Pull:
            {
                // 틱 수로 장을 센다(0.3.131-F — 쉬기의 상황 그림과 같게: 시계로 재면 장 경계에서 한 장이 두 배로 머물고 다음 장을 건너뛴다)
                long ct = (long)Math.Round(_clockTicks++ * 1000.0 / ClipFps);
                int f = 0, steps = Math.Max(_clipFrames, _clipSteps);
                while (f < steps && f + 1 < _clipT.Length && _clipT[f + 1] <= ct) f++;
                if (f < steps) { if (f != _frame) { _frame = f; Render(); } return; }
                if (LoadClip(BikeRideClip))
                {
                    _act = Act.Clip; _clipStart = now; _frame = 0; _clock = ClockPhase.Ride; _clockTicks = 0;
                    // 꺼내는 동안 오른쪽 끝에서 안으로 밀려 그려졌으면 그 자리에서 출발한다. 그다음부터는 끝에서 밀지 않고(_offscreen)
                    // 그대로 화면 밖으로 — 0.3.127 은 끝에서 그림이 멈췄다가 밖으로 튀어 나갔다(2026-10-07 사용자: 끝에서 끊김)
                    double mid = Math.Clamp(_x + _cw / 2.0, _work.left + _ccw / 2.0, _work.right - _ccw / 2.0);
                    _x = mid - _cw / 2.0;
                    _offscreen = true;
                    double dist = _work.right + _ccw - _x;                                   // 뒷바퀴까지 화면 밖
                    // 정시 ClockLeadMs 전에 돌아와 :59 를 띄우도록(0.3.131-F 사용자: 59 가 떠 있다가 정시에 00)
                    double left = (_clockHour - DateTime.Now).TotalMilliseconds - ClockArriveMs - ClockLeadMs;
                    double walkPxPerMs = SpeedLogical * _dpi / 96.0 / 1000.0;
                    _bikeSpeed = left > 300 ? Math.Clamp(dist / left / walkPxPerMs, BikeMinSpeed, BikeMaxSpeed) : BikeMaxSpeed;
                }
                else { FreeClip(); _act = Act.TurnOut; _frame = 0; _walkPhase = 0; _clock = ClockPhase.Go; }   // 페달 그림이 없으면 걸어서
                Render();
                return;
            }
            case ClockPhase.Ride:
            {
                // 빨리 달리면 빨리 밟는다: 틱마다 한 장, 빠르기는 틱 간격(62.5 / 46.875 / 31.25 ms — 장 번호에 배수를 더하면 같은 장이 반복되거나 건너뛴다)
                double pedal = Math.Min(2.0, _bikeSpeed / BikeMinSpeed);
                int per = pedal >= 1.75 ? 31 : pedal >= 1.2 ? 46 : 62;
                AnimTick(per);
                _frame = (int)(_clockTicks++ % Math.Max(1, _clipFrames));
                _x += SpeedLogical * _bikeSpeed * _dpi / 96.0 * PeriodMs(per) / 1000.0; _testMoves++;
                if (_x > _work.right + _ccw) { FreeClip(); AnimTick(AnimMs); _clock = ClockPhase.Away; _clockAt = now; return; }   // 자전거 뒷바퀴까지 화면 밖
                Render();
                return;
            }
            case ClockPhase.Away:
                if (now - _clockAt < ClockAwayMs) return;
                _dir = -1; _act = Act.Walk; _walkPhase = 0; _frame = WalkCell(0);   // 들어올 때도 빠른 걸음(정시에 맞춰)
                _clock = ClockPhase.Enter;
                return;
            case ClockPhase.Turn:
                _frame--;
                if (_frame < 0)
                {
                    _act = Act.Stand; _frame = 0; _clock = ClockPhase.Show; _clockAt = now; _clockShowFrom = DateTime.Now;
                    // 늘 :59 로 나타나 :00 으로 넘어간다(0.3.131-F 사용자): 정시 전에 왔으면 정시에, 늦게 왔으면 자리 잡은 뒤 0.7초에
                    var earliest = _clockShowFrom.AddMilliseconds(ClockAppearMs + ClockPreFlipMs);
                    _clockFlipAt = earliest > _clockHour ? earliest : _clockHour;
                    _clockLight = FlipClock.WindowsLight();
                    AnimTick(ClockAnimMs);   // 푯말이 날아오고 넘어가는 동안은 촘촘히(약 64 번/초)
                    _magic = LoadClip(MagicClip);
                    if (_magic) { _act = Act.Clip; _frame = 0; }
                }
                Render();
                if (_clock == ClockPhase.Show) ClockDraw(DateTime.Now, 0, 0.3);
                return;
            case ClockPhase.Show:
            {
                AnimTick(ClockAnimMs);
                double a = Math.Min(1, (now - _clockAt) / (double)ClockAppearMs);
                var t = DateTime.Now;
                double pa = EaseOutCubic(a);
                ClockDraw(t, pa, 0.3 + 0.7 * pa);   // 제자리에 다가갈수록 느려지고, 작게 나와 커진다(0.3.131-J 사용자)
                if (_magic)
                {
                    int rise = Math.Clamp(_clipHold, 1, _clipFrames - 1), loop = _clipFrames - rise, f;
                    if (a < 1) f = (int)Math.Round(pa * (rise - 1));                                     // 시계가 올라오는 만큼 팔을 든다
                    else
                    {
                        long k = (now - _clockAt - ClockAppearMs) * ClipFps / 1000, per = Math.Max(1, 2 * loop - 2), m = k % per;
                        f = rise + (int)(m < loop ? m : per - m);                                        // 든 채로 손짓(앞뒤로)
                    }
                    if (f != _frame) { _frame = f; Render(); }
                }
                if (t >= _clockFlipAt.AddMilliseconds(ClockFlipMs + ClockHoldMs)) { _clock = ClockPhase.Leave; _clockAt = now; }   // 00 이 된 뒤 5초
                return;
            }
            case ClockPhase.Leave:
            {
                double v = Math.Min(1, (now - _clockAt) / (double)ClockLeaveMs);
                double p = 1 - v * v;   // 처음엔 천천히, 모서리 뒤로 들어가듯
                ClockDraw(DateTime.Now, p, 0.3 + 0.7 * p);   // 0.3.131-I 사용자: 내려가며 작아지게
                if (_magic)
                {
                    int rise = Math.Clamp(_clipHold, 1, _clipFrames - 1), f = (int)Math.Round(p * (rise - 1));   // 시계가 내려가는 만큼 팔을 내린다
                    if (f != _frame) { _frame = f; Render(); }
                }
                if (v >= 1) { EndClock(); StartRest(); Render(); }
                return;
            }
        }
    }

    private static double EaseOutCubic(double a) { a = Math.Clamp(a, 0, 1); return 1 - Math.Pow(1 - a, 3); }
    private static double EaseOutBack(double a) { a = Math.Clamp(a, 0, 1); const double c1 = 1.2, c3 = c1 + 1; return 1 + c3 * Math.Pow(a - 1, 3) + c1 * Math.Pow(a - 1, 2); }

    /// <summary>
    /// 시계 그리기. 숫자: _clockFlipAt 전에는 :59, 그때부터 ClockFlipMs 동안 :00 으로 넘어간다.
    /// 자리: pos 0 = 화면(작업 영역) 오른쪽 아래 모서리 뒤, 1 = 마스코트 머리 위(아래 0.3.131-H 설명). k = 크기 배수(나타날 때 1,
    /// 사라질 때 1 → 0.3 — 0.3.131-I 사용자: 모서리로 내려가며 작아지게; 나타날 때도 0.3 → 1 — 0.3.131-J).
    /// 시계 창은 마스코트 창 바로 아래 층(마스코트 뒤로 지나간다 — 0.3.131-G 사용자).
    /// </summary>
    private static void ClockDraw(DateTime t, double pos, double k)
    {
        var to = (_clockHour.Hour, _clockHour.Minute);
        var before = _clockHour.AddMinutes(-1);
        (int, int) from = (before.Hour, before.Minute);
        double flip;
        if (t < _clockFlipAt) { to = from; flip = 1; }                                       // 아직 :59
        else flip = Math.Min(1, (t - _clockFlipAt).TotalMilliseconds / ClockFlipMs);
        float s = _fh / (float)HeightLogical;
        double fw = FlipClock.LogicalW * s, fhh = FlipClock.LogicalH * s, k0 = 0.3;
        double mid = _x + _cw / 2.0;
        double tx = Math.Clamp(mid, _work.left + fw / 2, _work.right - fw / 2), ty = _work.bottom - _fh - 8 * s - fhh / 2;   // 머리 위(가운데)
        double sx = _work.right - fw * k0 / 2 - 2 * s, sy = _work.bottom - fhh * k0 / 2 - 2 * s;                              // 오른쪽 아래 모서리
        // 0.3.131-H(사용자 그림 1~6): 크기 그대로, 작업 영역 오른쪽 아래 모서리 뒤(밖)에서 시계 방향으로 24° 기운 채 시작 — 모서리 위로 시계의
        // 왼쪽 위 끝만 보인다. 먼저 솟고 이어서 왼쪽으로 들어오며(2차 베지어: 시작 → (시작 x, 머리 위 y) → 머리 위) 점점 반듯해진다. 작업 영역 밖은 지운다
        double u = Math.Clamp(pos, 0, 1);
        double ox = _work.right + fw * 0.30, oy = _work.bottom + fhh * 0.42;
        double bx = ox + (tx - ox) * u * u;
        double by = ty + (oy - ty) * (1 - u) * (1 - u);
        float tilt = (float)(24 * (1 - u));
        float ss = (float)(s * Math.Clamp(k, 0.05, 1));
        FlipClock.ShowTilted(bx, by, ss, from, to, flip, _clockLight, tilt, _work.right, _work.bottom, below: _hwnd);
    }

    /// <summary>다니는 범위: 작업 표시줄 오른쪽 5분의 1 — 알림 영역·시계 위(2026-10-06 사용자 — 너무 넓지 않게).</summary>
    private static (double Min, double Max) Range()
    {
        double min = _work.right - (_work.right - _work.left) / 5.0, max = _work.right - _cw;
        return (min, Math.Max(min + 1, max));
    }

    private static void Hide(bool keepCheck)
    {
        CancelPress();
        if (_clock != ClockPhase.None) EndClock();   // 숨으면 정시 알림도 그만(시계 창도 닫는다)
        if (_hwnd != 0)
        {
            Native.KillTimer(_hwnd, TimerAnim);
            if (!keepCheck) Native.KillTimer(_hwnd, TimerCheck);
            StopTicks();
            LogLine("hide");
            Native.ShowWindow(_hwnd, Native.SW_HIDE);
        }
        _shown = false;
        SetTestProps();
    }

    private static void HideAll() { Hide(keepCheck: false); if (!_wanted) { _why = 1; SetTestProps(); } }

    /// <summary>보고 싶은데 지금은 못 보일 때: 창(타이머 주인)을 만들고 2초 점검 타이머를 건다.</summary>
    private static void RetryLater()
    {
        if (EnsureWindow()) Native.SetTimer(_hwnd, TimerCheck, CheckMs, 0);
        else _why = 9;
        SetTestProps();
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
            int fh = Scale(HeightLogical, dpi);
            if ((work.right - work.left) / 5 < fh * 5 || work.bottom - work.top < fh * 3) return false;   // 좁은 작업 영역(오른쪽 5분의 1 안을 다닌다)
            why = 0;
            return true;
        }
        catch { why = 10; return false; }
    }

    /// <summary>시작 메뉴·검색·알림/빠른 설정·작업 표시줄 메뉴·넘침 영역 등이 앞에 있으면 true. 앞 창이 없어도 true(모르면 숨김).</summary>
    private static bool SystemPanelUp()
    {
        nint fg = Native.GetForegroundWindow();
        if (fg == 0) return true;
        string cls = Native.GetClassName(fg);
        if (cls is "Windows.UI.Core.CoreWindow" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "NotifyIconOverflowWindow" or "TopLevelWindowForOverflowXamlIsland"
            or "Xaml_WindowedPopupClass" or "#32768" or "XamlExplorerHostIslandWindow" or "MultitaskingViewFrame" or "ForegroundStaging" or "TaskListThumbnailWnd"
            or "Windows.UI.Input.InputSite.WindowClass" or "ControlCenterWindow" or "LauncherTipWnd") return true;
        // 떠 있는 팝업 메뉴(#32768)가 하나라도 보이면(다른 앱의 메뉴 포함 — 아래쪽을 가릴 수 있다)
        nint menu = FindWindowW("#32768", null);
        return menu != 0 && Native.IsWindowVisible(menu);
    }

    // ------------------------------------------------------------------ 그림

    private static int Scale(int v, int dpi) => (int)Math.Round(v * dpi / 96.0);

    /// <summary>인사 그림 띠를 표시 크기로 줄여 한 장의 DIB(오른쪽 줄 + 거울 뒤집은 왼쪽 줄)로. 원본 그림은 바로 해제.</summary>
    private static bool BuildArt()
    {
        nint sprite = 0, stream = 0, bmp = 0, g = 0, walk = 0, wl = 0;
        bool ok = false;
        try
        {
            Gdiplus.Init();
            sprite = MascotGreet.Load(out _greetN);   // 0.3.121: 16 fps 인사(약 96장, JPEG 색 + 회색 투명도)
            if (sprite == 0) return false;
            GdipGetImageWidth(sprite, out uint sw); GdipGetImageHeight(sprite, out uint sh);
            int spriteW = (int)(sw / _greetN), spriteH = (int)sh;
            if (spriteW <= 0 || spriteH <= 0) return false;
            _fh = Scale(HeightLogical, _dpi);
            _fw = Math.Max(4, (int)Math.Round(_fh * (double)spriteW / spriteH));
            _cw = (int)Math.Ceiling(_fh * 0.8);   // 인사(0.64)·걷기(0.61)·상황 그림(0.67) 폭이 다 들어가게(높이 대비)
            _bob = Math.Max(1, Scale(BobLogical, _dpi));
            if (!LoadWalkSheet("mascot_walk", out walk, out int walkCellW, out int walkH, out _turnN, out _loopN, out _leadN)) return false;
            _baseH = walkH;
            // 왼쪽 걷기(따로 그린 그림, 장 수는 오른쪽과 달라도 된다). 없으면 오른쪽을 뒤집는다
            if (!LoadWalkSheet("mascot_walk_left", out wl, out int wlCellW, out int wlH, out _turnL, out _loopL, out _leadL)) { wl = 0; _turnL = _turnN; _loopL = _loopN; _leadL = _leadN; }
            _baseCells = Math.Max(_greetN, Math.Max(_turnN + _leadN + _loopN, _turnL + _leadL + _loopL));
            int w = _cw * _baseCells, h = _fh * 4;
            var bih = new BIH { biSize = (uint)sizeof(BIH), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            nint screen = Native.GetDC(0);
            _mem = Native.CreateCompatibleDC(screen);
            Native.ReleaseDC(0, screen);
            if (_mem == 0) return false;
            _dib = TestFail("test-walker-fail-dib") ? 0 : CreateDIBSection(_mem, ref bih, 0, out _bits, 0, 0);   // 시험: DIB 실패 주입
            if (_dib == 0 || _bits == 0) return false;
            _oldBmp = Native.SelectObject(_mem, _dib);
            // 위 줄: 프레임을 줄여 그린다(미리 곱한 알파 — UpdateLayeredWindow 가 그대로 쓴다)
            if (TestFail("test-walker-fail-draw")) return false;   // 시험: DIB 뒤 그리기 준비 실패 주입(부분 자원이 남는 경로)
            if (GdipCreateBitmapFromScan0(w, h, w * 4, 0xE200B /* PixelFormat32bppPARGB */, _bits, out bmp) != 0 || bmp == 0) return false;
            if (GdipGetImageGraphicsContext(bmp, out g) != 0) return false;
            GdipSetInterpolationMode(g, 7 /* HighQualityBicubic */);
            GdipSetPixelOffsetMode(g, 2 /* HighQuality */);
            GdipGraphicsClear(g, 0);
            // 셋째 줄: 인사 19장(칸 아래 가운데)
            for (int i = 0; i < _greetN; i++) GdipDrawImageRectRectI(g, sprite, i * _cw + (_cw - _fw) / 2, 2 * _fh, _fw, _fh, i * spriteW, 0, spriteW, spriteH, 2 /* UnitPixel */, 0, 0, 0);
            // 넷째 줄 첫 칸: 매달리기 그림(인사 그림과 같은 캔버스 크기라 같은 배율·자리). 없거나 못 읽으면 매달리기를 하지 않는다
            _peekOk = false;
            nint peek = LoadPng("mascot_peek.png");
            if (peek != 0)
            {
                GdipGetImageWidth(peek, out uint pw); GdipGetImageHeight(peek, out uint ph);
                _peekOk = pw > 0 && ph > 0 && GdipDrawImageRectRectI(g, peek, (_cw - _fw) / 2, 3 * _fh, _fw, _fh, 0, 0, (int)pw, (int)ph, 2, 0, 0, 0) == 0;
                GdipDisposeImage(peek);
            }
            // 넷째 줄 둘째 칸: 손만(같은 캔버스). 없으면 예전처럼 손과 몸이 함께 움직인다
            _peekHands = false;
            nint hands = _peekOk ? LoadPng("mascot_peek_hands.png") : 0;
            if (hands != 0)
            {
                GdipGetImageWidth(hands, out uint hw); GdipGetImageHeight(hands, out uint hh);
                _peekHands = hw > 0 && hh > 0 && GdipDrawImageRectRectI(g, hands, _cw + (_cw - _fw) / 2, 3 * _fh, _fw, _fh, 0, 0, (int)hw, (int)hh, 2, 0, 0, 0) == 0;
                GdipDisposeImage(hands);
            }
            // 첫째 줄: 돌아서기 + 걷기(오른쪽 보기) — 한 배율·발끝 맞춤으로 만든 그림을 칸 아래 가운데에
            int ww = Math.Max(1, (int)Math.Round(walkCellW * (double)_fh / walkH));
            for (int i = 0; i < _turnN + _leadN + _loopN; i++)
                GdipDrawImageRectRectI(g, walk, i * _cw + (_cw - ww) / 2, 0, ww, _fh, i * walkCellW, 0, walkCellW, walkH, 2, 0, 0, 0);
            // 둘째 줄: 왼쪽 보기. 따로 그린 왼쪽 걷기 그림(mascot_walk_left, 장 수가 같을 때)이 있으면 그것 — 뒤집으면 머리 장식이 반대쪽으로 가서
            // 보인다(2026-10-07 사용자). 없으면 첫째 줄을 칸마다 좌우로 뒤집는다
            bool leftDrawn = false;
            if (wl != 0)
            {
                int lw = Math.Max(1, (int)Math.Round(wlCellW * (double)_fh / wlH));
                for (int i = 0; i < _turnL + _leadL + _loopL; i++)
                    GdipDrawImageRectRectI(g, wl, i * _cw + (_cw - lw) / 2, _fh, lw, _fh, i * wlCellW, 0, wlCellW, wlH, 2, 0, 0, 0);
                leftDrawn = true;
                GdipDisposeImage(wl); wl = 0;
            }
            GdipDeleteGraphics(g); g = 0;
            GdipDisposeImage(bmp); bmp = 0;
            uint* px = (uint*)_bits;
            for (int i = 0; !leftDrawn && i < _turnN + _leadN + _loopN; i++)
                for (int y = 0; y < _fh; y++)
                {
                    uint* srow = px + y * w + i * _cw, drow = px + (_fh + y) * w + i * _cw;
                    for (int x = 0; x < _cw; x++) drow[x] = srow[_cw - 1 - x];
                }
            // 상황 그림은 걷기 그림과 같은 배율(원본 서 있는 키 _baseH → _fh): 옆으로 긴 그림은 넓게, 점프처럼 위로 솟는 그림은 높게
            double k = _fh / (double)_baseH;
            _vw = _cw; _vh = _fh;
            foreach (var c in ClipList())
            {
                _vw = Math.Max(_vw, (int)Math.Ceiling(c.CellW * k));
                _vh = Math.Max(_vh, (int)Math.Ceiling((c.H > 0 ? c.H : _baseH) * k));
            }
            _vw = Math.Min(_vw, _cw * 3); _vh = Math.Min(_vh, _fh * 2) + LiftRoom() + LabelRoom();   // LabelRoom: 시험 빌드의 번호 띠
            if (SeqTest) _vw = Math.Max(_vw, LabelWidth(7));   // 번호 띠(예: 07 105)가 잘리지 않게   // 위 여유: 점프를 더 띄울 자리
            var xb = new BIH { biSize = (uint)sizeof(BIH), biWidth = _vw, biHeight = -_vh, biPlanes = 1, biBitCount = 32 };
            nint sc2 = Native.GetDC(0); _xmem = Native.CreateCompatibleDC(sc2); Native.ReleaseDC(0, sc2);
            if (_xmem == 0) return false;
            _xdib = CreateDIBSection(_xmem, ref xb, 0, out _xbits, 0, 0);
            if (_xdib == 0 || _xbits == 0) return false;
            _xold = Native.SelectObject(_xmem, _xdib);
            ok = true;
            return true;
        }
        catch { return false; }
        finally
        {
            if (g != 0) GdipDeleteGraphics(g);
            if (bmp != 0) GdipDisposeImage(bmp);
            if (sprite != 0) GdipDisposeImage(sprite);
            if (wl != 0) GdipDisposeImage(wl);
            if (walk != 0) GdipDisposeImage(walk);
            if (stream != 0) Marshal.Release(stream);
            if (!ok) FreeArt();   // 부분 자원(메모리 DC·DIB)도 여기서 돌려준다 — 덜 만든 그림을 다음에 쓰지 않게(Codex R83-1)
        }
    }

    /// <summary>걷기 그림과 그 설명(walk.txt: "turn N loop M cellW W")을 읽는다(name = 자원 이름, 확장자 없이). 받은 쪽이 GdipDisposeImage.</summary>
    private static bool LoadWalkSheet(string name, out nint img, out int cellW, out int h, out int turn, out int loop, out int lead)
    {
        img = 0; cellW = 0; h = 0; turn = 0; loop = 0; lead = 0;
        try
        {
            using (var t = typeof(Walker).Assembly.GetManifestResourceStream(name + ".txt"))
            {
                if (t is null) return false;
                string[] p = new StreamReader(t).ReadToEnd().Trim().Split(' ');
                // "turn T loop L cellW W [lead D]" — lead 장은 돌아서기와 반복 사이에 있다(그림 순서: 돌아서기, lead, 반복)
                if (p.Length is not (6 or 8) || !int.TryParse(p[1], out turn) || !int.TryParse(p[3], out loop) || !int.TryParse(p[5], out cellW)) return false;
                if (p.Length == 8 && (p[6] != "lead" || !int.TryParse(p[7], out lead))) return false;
                if (turn is < 1 or > 32 || loop is < 2 or > 64 || lead is < 0 or > 32 || cellW is < 4 or > 512) return false;
            }
            using var s = typeof(Walker).Assembly.GetManifestResourceStream(name + ".png");
            if (s is null) return false;
            byte[] data = new byte[s.Length];
            s.ReadExactly(data);
            nint stream;
            fixed (byte* p = data) stream = SHCreateMemStream(p, (uint)data.Length);
            if (stream == 0) return false;
            try { if (GdipCreateBitmapFromStream(stream, out img) != 0 || img == 0) return false; }
            finally { Marshal.Release(stream); }
            // 이미지는 스트림을 다 읽은 뒤에도 쓰이므로 비트맵으로 복사해 둔다(스트림 해제 뒤 안전하게)
            GdipGetImageHeight(img, out uint ih); h = (int)ih;
            if (GdipCloneImage(img, out nint copy) == 0 && copy != 0) { GdipDisposeImage(img); img = copy; }
            return h > 0;
        }
        catch { if (img != 0) GdipDisposeImage(img); img = 0; return false; }
    }

    private static bool TestFail(string file) => Program.IsTestMode && File.Exists(Path.Combine(Config.Dir, file));

    /// <summary>그림 자원(DIB·메모리 DC)만 해제한다. 창은 그대로.</summary>
    private static void FreeArt()
    {
        FreeClip();
        ClearPrepared();
        _fromPx = null;
        if (_xmem != 0 && _xold != 0) Native.SelectObject(_xmem, _xold);
        if (_xdib != 0) Native.DeleteObject(_xdib);
        if (_xmem != 0) Native.DeleteDC(_xmem);
        _xmem = _xdib = _xold = _xbits = 0;
        if (_mem != 0 && _oldBmp != 0) Native.SelectObject(_mem, _oldBmp);
        if (_dib != 0) Native.DeleteObject(_dib);
        if (_mem != 0) Native.DeleteDC(_mem);
        _mem = _dib = _oldBmp = _bits = 0;
        _dpi = 0;
    }

    /// <summary>지금 프레임을 지금 자리에 내보낸다. 발은 작업 영역 아래 끝, 걷는 동안은 위로 살짝 들썩인다.</summary>
    private static bool Render()
    {
        if (_hwnd == 0 || !Compose(out int winX)) return false;
        ClearOffscreen(winX);
        DrawLabel(winX);   // 시험 빌드만(WalkerTest.cs)
        var dst = new Native.POINT { x = winX, y = _work.bottom - _vh };   // 발은 작업 영역 아래 끝(걷기 그림에 걸음의 들썩임이 들어 있다)
        var size = new SIZE { cx = _vw, cy = _vh };
        var zero = new Native.POINT();
        var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 /* AC_SRC_ALPHA */ };
        return !TestFail("test-walker-fail-push") && UpdateLayeredWindow(_hwnd, 0, ref dst, ref size, _xmem, ref zero, 0, ref blend, 2 /* ULW_ALPHA */);
    }

    /// <summary>내보낼 그림을 _xbits(창 _vw × _vh)에 만든다. 그림은 아래 끝에 붙인다(발). winX = 창 왼쪽(화면 좌표).</summary>
    private static bool Compose(out int winX)
    {
        winX = 0;
        if (_mem == 0 || _xbits == 0) return false;
        var (_, src, bits, stride, cellW, cellH) = Current();
        if (bits == 0) return false;
        int dy = _act == Act.Clip && _frame >= 0 && _frame < _clipDy.Length ? _clipDy[_frame] : 0;   // 발을 선에·점프는 위로·앉기는 아래로
        int VH = _vh, oy = Math.Max(0, VH - cellH) + (_act == Act.Peek ? _sink : 0) + dy;   // 매달리기: 내려 그리고 창 아래 끝(작업 표시줄)에서 잘린다
        // 창(폭 _vw)과 그림 칸(폭 cellW)의 가운데를 마스코트 가운데에. 작업 영역 밖으로 나가지 않게 끝에서는 안쪽으로 민다(잘리지 않게)
        int V = _vw, left = _work.left, right = _work.right;
        double mid = _x + _cw / 2.0;
        // 정시 알림으로 화면 오른쪽 밖으로 나갈 때는 오른쪽 끝에서 안으로 밀지 않는다(작업 영역 밖 부분은 아래에서 지운다)
        int rightLimit = _offscreen ? right + V : right;
        winX = Math.Clamp((int)Math.Round(mid - V / 2.0), left, Math.Max(left, rightLimit - V));
        int at0 = Math.Clamp((int)Math.Round(mid - cellW / 2.0), left, Math.Max(left, rightLimit - cellW)) - winX;
        int at = Math.Clamp(at0 + (_act == Act.Peek ? _sway : 0), 0, Math.Max(0, V - cellW));
        bool mirror = _act == Act.Clip && _clipMirror;
        // 매달리기 손: 흔들림 없이, 가장자리(PeekDepth)보다 더 내려가지 않는다 — 그 아래로는 몸만 내려간다
        bool hands = _act == Act.Peek && _peekOk && _peekHands;
        int hoy = Math.Max(0, VH - cellH) + Math.Min(_sink, PeekDepthPx()), hat = Math.Clamp(at0, 0, Math.Max(0, V - cellW));
        long since = Environment.TickCount64 - _fadeStart;
        bool fade = _fromPx is not null && since < FadeMs;
        if (!fade) _fromPx = null;
        uint t = fade ? (uint)(since * 256 / FadeMs) : 256, u = 256 - t;
        uint* outp = (uint*)_xbits;
        fixed (uint* from = _fromPx)
            for (int y = 0; y < VH; y++)
            {
                bool row = y >= oy && y - oy < cellH, hrow = hands && y >= hoy && y - hoy < cellH;
                uint* cur = (uint*)bits + (src.y + (row ? y - oy : 0)) * stride + src.x - at;
                uint* curM = (uint*)bits + (src.y + (row ? y - oy : 0)) * stride + src.x + cellW - 1 + at;   // 좌우 뒤집기: curM[-x]
                uint* hc = (uint*)bits + (src.y + (hrow ? y - hoy : 0)) * stride + _cw - hat;
                uint* o = outp + y * V;
                for (int x = 0; x < V; x++)
                {
                    uint b = row && x >= at && x < at + cellW ? (mirror ? curM[-x] : cur[x]) : 0;
                    if (hrow && x >= hat && x < hat + cellW && hc[x] >> 24 != 0) b = Over(hc[x], b);
                    if (!fade) { o[x] = b; continue; }
                    uint a = from[y * V + x];
                    o[x] = (((a >> 24) * u + (b >> 24) * t) >> 8 << 24) | ((((a >> 16) & 255) * u + ((b >> 16) & 255) * t) >> 8 << 16)
                         | ((((a >> 8) & 255) * u + ((b >> 8) & 255) * t) >> 8 << 8) | (((a & 255) * u + (b & 255) * t) >> 8);
                }
            }
        return true;
    }

    /// <summary>정시 알림으로 나가는 중: 창에서 작업 영역 오른쪽 밖에 있는 열을 지운다(옆 모니터·화면 밖에 그려지지 않게).</summary>
    private static void ClearOffscreen(int winX)
    {
        if (!_offscreen || _xbits == 0) return;
        int from = Math.Max(0, _work.right - winX);
        if (from >= _vw) return;
        uint* o = (uint*)_xbits;
        for (int y = 0; y < _vh; y++) for (int x = from; x < _vw; x++) o[y * _vw + x] = 0;
    }

    /// <summary>미리 곱한 알파 픽셀 top 을 under 위에 얹는다.</summary>
    private static uint Over(uint top, uint under)
    {
        uint k = 255 - (top >> 24);
        if (k == 0) return top;
        return top + ((((under >> 24) * k / 255) << 24) | ((((under >> 16) & 255) * k / 255) << 16) | ((((under >> 8) & 255) * k / 255) << 8) | ((under & 255) * k / 255));
    }

    /// <summary>지금 동작의 그림 자리: DC·그 안의 칸 왼쪽 위, 픽셀 주소·한 줄 폭(픽셀), 칸 폭·높이.</summary>
    private static (nint Dc, Native.POINT Src, nint Bits, int Stride, int CellW, int CellH) Current()
    {
        int baseW = _cw * _baseCells;
        return _act switch
        {
            Act.Walk => (_mem, new Native.POINT { x = (TurnN + Math.Clamp(_frame, 0, LeadN + LoopN - 1)) * _cw, y = _dir > 0 ? 0 : _fh }, _bits, baseW, _cw, _fh),
            Act.TurnOut or Act.TurnIn => (_mem, new Native.POINT { x = Math.Clamp(_frame, 0, TurnN - 1) * _cw, y = _dir > 0 ? 0 : _fh }, _bits, baseW, _cw, _fh),
            Act.Greet => (_mem, new Native.POINT { x = MascotGreet.Cell(_frame, _greetN) * _cw, y = 2 * _fh }, _bits, baseW, _cw, _fh),
            Act.Peek when _peekOk => (_mem, new Native.POINT { x = 0, y = 3 * _fh }, _bits, baseW, _cw, _fh),
            Act.Clip when _cbits != 0 => (0, new Native.POINT { x = Math.Min(_frame, _clipFrames - 1) * _ccw, y = 0 }, _cbits, _ccw * _clipFrames, _ccw, _cdh),
            _ => (_mem, new Native.POINT { x = 0, y = 2 * _fh }, _bits, baseW, _cw, _fh),   // 서 있기 = 인사 0번
        };
    }

    /// <summary>동작이 바뀌기 직전에 부른다: 지금 내보낸 그림(창 폭 그대로)을 복사해 두고 섞기를 시작한다.</summary>
    private static void BeginFade()
    {
        if (_xbits == 0 || _vw == 0) return;
        var px = new uint[_vw * _vh];
        fixed (uint* d = px) Buffer.MemoryCopy((void*)_xbits, d, px.Length * 4L, px.Length * 4L);
        _fromPx = px;
        _fadeStart = Environment.TickCount64;
    }

    /// <summary>다른 항상 위 창 뒤로 밀렸으면 위로(활성화 없이). 보일 때와 2초 점검 때만 — 프레임마다 하지 않는다.</summary>
    private static void KeepOnTop() => Native.SetWindowPos(_hwnd, (nint)(-1) /* HWND_TOPMOST */, 0, 0, 0, 0, Native.SWP_NOMOVE_ | Native.SWP_NOSIZE_ | Native.SWP_NOACTIVATE);

    // ------------------------------------------------------------------ 움직임

    private static int PeekDepthPx() => (int)Math.Round(_fh * PeekDepth);

    /// <summary>
    /// 매달리기 경과 시간 → 몸이 내려간 깊이(px, 손은 PeekDepthPx 에서 멈춘다): 섞기 동안 0 → 손과 몸이 함께 손이 가장자리에 닿을 때까지 →
    /// 손은 두고 몸만 조금 더(턱이 선 가까이) → 매달려 있기 → 살짝 더 내려갔다가 → 팔로 끌어 올려 손 높이까지(손은 그대로) → 손과 몸이 함께 올라와 선다.
    /// </summary>
    private static int PeekSink(long t)
    {
        int depth = PeekDepthPx(), grip = _peekHands ? Math.Max(1, _fh / 22) : 0, low = depth + grip;
        static double Ease(double v) { v = Math.Clamp(v, 0, 1); return v * v * (3 - 2 * v); }
        if (t < FadeMs) return 0;
        t -= FadeMs;
        if (t < PeekDownMs) return (int)Math.Round(depth * Ease(t / (double)PeekDownMs));
        t -= PeekDownMs;
        if (t < PeekGripMs) return depth + (int)Math.Round(grip * Ease(t / (double)PeekGripMs));
        t -= PeekGripMs;
        if (t < _peekHoldMs) return low;
        t -= _peekHoldMs;
        int dip = Math.Max(1, _fh / 22);   // 팔로 끌어 올리기 전에 살짝 더 내려간다
        if (t < PeekDipMs) return low + (int)Math.Round(dip * Ease(t / (double)PeekDipMs));
        t -= PeekDipMs;
        if (t < PeekPullMs) { double v = t / (double)PeekPullMs; return depth + (int)Math.Round((grip + dip) * (1 - v) * (1 - v)); }   // 처음에 빠르게(끌어 올린다)
        t -= PeekPullMs;
        if (t < PeekUpMs) { double v = t / (double)PeekUpMs; return (int)Math.Round(depth * (1 - v) * (1 - v)); }
        return 0;
    }

    /// <summary>매달려 있는 동안만 몸이 좌우로 천천히 흔들린다(1.6초 주기, 키의 약 5% — 손은 그대로). 그 밖에는 0.</summary>
    private static int PeekSway(long t)
    {
        if (!_peekHands) return 0;   // 손이 몸과 함께 움직이면 흔들림은 손이 가장자리에서 미끄러지는 것처럼 보인다
        long h = t - FadeMs - PeekDownMs - PeekGripMs;
        if (h < 0 || h >= _peekHoldMs) return 0;
        double env = Math.Min(1, Math.Min(h, _peekHoldMs - h) / 300.0);   // 시작·끝은 부드럽게
        return (int)Math.Round(Math.Sin(h * 2 * Math.PI / 1600.0) * Math.Max(1, _fh / 20.0) * env);
    }

    /// <summary>실행 파일에 넣은 PNG 를 GDI+ 이미지로(복사본 — 스트림을 바로 놓아도 된다). 실패하면 0. 받은 쪽이 GdipDisposeImage.</summary>
    private static nint LoadPng(string name)
    {
        nint img = 0, stream = 0;
        try
        {
            using var s = typeof(Walker).Assembly.GetManifestResourceStream(name);
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

    /// <summary>
    /// 걷기 시작: 쉬던 정면 그림에서 걷는 쪽으로 돌아선다(섞어서 바꾼 뒤 돌아서기 → 걷기). 길이·속도·방향은 매번 무작위(2026-10-06 사용자 —
    /// 반복된다는 느낌이 없게): 짧게 몇 걸음(35%)·보통(45%)·길게(20%), 속도 0.8~1.25배, 방향은 반반이되 범위 바깥쪽 5분의 1 에서는 안쪽으로 4분의 3.
    /// </summary>
    private static void StartWalk()
    {
        if (_shown) BeginFade();
        _clipRelease = _cbits != 0;   // 섞는 동안은 앞 그림(복사본)만 쓰므로 상황 그림은 다음 틱에 해제해도 된다
        var (min, max) = Range();
        double pos = max > min ? (_x - min) / (max - min) : 0.5;
        if (pos < 0.2) _dir = _rnd.Next(4) == 0 ? -1 : 1;
        else if (pos > 0.8) _dir = _rnd.Next(4) == 0 ? 1 : -1;
        else if (_rnd.Next(2) == 0) _dir = -_dir;
        if (_x <= min + 1) _dir = 1; else if (_x >= max - 1) _dir = -1;   // 끝에 서 있으면 안쪽으로
        _resting = false; _act = Act.TurnOut; _frame = 0; _reverseAfterTurn = false; _walkPhase = 0;
        _walkTickMs = WalkTicks[_rnd.Next(WalkTicks.Length)];
        _speedMul = 62.5 / (_walkTickMs + 0.5);   // 보고용(빠르기 배수). 걸음은 틱마다 한 장·한 걸음 폭
        int r = _rnd.Next(100);
        int ms = r < 35 ? 1200 + _rnd.Next(1800) : r < 80 ? 3000 + _rnd.Next(4000) : 7000 + _rnd.Next(5000);
        _phaseEnd = Environment.TickCount64 + ms;
    }

    /// <summary>
    /// 쉬기: 상황 그림(50% — 섞은 묶음에서 차례로, 묶음을 다 쓰기 전에는 같은 것이 다시 나오지 않음), 작업 표시줄에 매달리기(12%), 손 흔들기(10%),
    /// 그냥 서 있기(28%, 1.5~9초).
    /// 상황 그림은 재생마다 초당 5~7장. 쉬기가 끝나면 가끔(20%) 걷지 않고 쉬기를 한 번 더 한다.
    /// </summary>
    private static void StartRest()
    {
        if (_shown) BeginFade();
        _resting = true; _frame = 0;
        long now = Environment.TickCount64;
        int r = SeqTest ? 0 : _rnd.Next(100);   // 시험 빌드: 쉴 때마다 다음 상황 그림
        // 매달리기: 담벼락 내려가듯 내려갔다가 올라오는 동작 그림(clips.txt 18, x:1 pp:1 — 0.3.126)이 있으면 그것, 없으면 예전 한 장 내리기
        if (r >= 50 && r < 62 && LoadClip(HangClip))
        {
            _act = Act.Clip; _clipStart = now; _testClips++; _clipFps = ClipFps;
            AnimTick(ClipTickMs);
            BuildClipTimes();
            _phaseEnd = now + _clipT[^1] + 600 + _rnd.Next(2000);
            return;
        }
        if (r >= 50 && r < 62 && _peekOk)
        {
            _act = Act.Peek; _clipStart = now; _sink = 0;
            _peekHoldMs = 1500 + _rnd.Next(500);   // 1.5~2초(0.3.115 피드백)
            _sway = 0;
            _phaseEnd = now + FadeMs + PeekDownMs + PeekGripMs + _peekHoldMs + PeekDipMs + PeekPullMs + PeekUpMs + 300;
            return;
        }
        if (r < 50 && LoadClip())
        {
            _act = Act.Clip; _clipStart = now; _testClips++; _clipFps = ClipFps;
            AnimTick(ClipTickMs);
            BuildClipTimes();
            if (_clipMoveA >= 0) AimClipMove();
            _phaseEnd = now + _clipT[^1] + 600 + _rnd.Next(3000);
        }
        else if (r < 72) { _act = Act.Greet; _phaseEnd = now + 2200 + _rnd.Next(3500); }
        else { _act = Act.Stand; _phaseEnd = now + (_rnd.Next(3) == 0 ? 5000 + _rnd.Next(4000) : 1500 + _rnd.Next(3000)); }
    }

    // ------------------------------------------------------------------ 상황 그림

    /// <summary>
    /// 상황 그림 목록(Assets/clips/clips.txt: "번호 장수 칸폭 높이 [g:묶음] [h:장:ms]" — 높이는 0.3.93 부터, 없으면 0). 처음 한 번 읽는다.
    /// g: 같은 묶음은 섞은 차례에서 한 자리만 차지한다(비슷한 작은 동작이 자주 나오지 않게 — 0.3.111). h: 그 장에서 ms 동안 멈춘다(특징 자세를 알아볼 시간).
    /// p: 새로 섞을 때 그 % 확률로만 들어간다(가끔 나오는 특별 동작 — 구르기, 0.3.112). j:1·s:·m: 은 <see cref="ClipInfo"/>(0.3.115).
    /// </summary>
    private static ClipInfo[] ClipList()
    {
        if (_clipList is not null) return _clipList;   // (각 그림은 한 묶음 같은 배율·발끝 맞춤으로 만들어져 크기가 바뀌지 않는다 — mascot_clips.py)
        var list = new List<ClipInfo>();
        // "첫장-끝장" 과 px: 장 번호는 그림 안, 첫장 <= 끝장
        static bool Span(string[] kv, int n, out int a, out int b, out int px)
        {
            a = b = px = 0;
            string[] ab = kv.Length == 3 ? kv[1].Split('-') : Array.Empty<string>();
            return ab.Length == 2 && int.TryParse(ab[0], out a) && int.TryParse(ab[1], out b) && int.TryParse(kv[2], out px)
                && a >= 0 && b >= a && b < n && px is > 0 and <= 512;
        }
        try
        {
            using var s = typeof(Walker).Assembly.GetManifestResourceStream("mascot_clips.txt");
            if (s is not null)
                foreach (string line in new StreamReader(s).ReadToEnd().Split('\n'))
                {
                    string[] all = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    string[] p = all.Where(x => !x.Contains(':')).ToArray();
                    int hh = 0;
                    if (p.Length is 3 or 4 && int.TryParse(p[1], out int n) && int.TryParse(p[2], out int cw) && n is > 0 and <= 160 && cw is > 0 and <= 512
                        && (p.Length == 3 || int.TryParse(p[3], out hh) && hh is > 0 and <= 512))
                    {
                        string group = ""; int hold = -1, holdMs = 0, prob = 100, sitA = 0, sitB = 0, sitPx = 0, movA = 0, movB = 0, movPx = 0;
                        bool jump = false, pingPong = false, special = false; int dir = 0;
                        foreach (string o in all.Where(x => x.Contains(':')))
                        {
                            string[] kv = o.Split(':');
                            if (kv[0] == "g" && kv.Length == 2) group = kv[1];
                            else if (kv[0] == "p" && kv.Length == 2 && int.TryParse(kv[1], out int pp) && pp is >= 1 and <= 100) prob = pp;
                            else if (kv[0] == "j" && kv.Length == 2) jump = kv[1] == "1";
                            else if (kv[0] == "pp" && kv.Length == 2) pingPong = kv[1] == "1";        // 앞으로 재생한 뒤 거꾸로 되돌아온다(0.3.126 매달리기)
                            else if (kv[0] == "x" && kv.Length == 2) special = kv[1] == "1";    // 섞어 고르는 차례에 넣지 않는다(따로 부르는 동작)
                            else if (kv[0] == "d" && kv.Length == 2 && kv[1] is "1" or "-1") dir = kv[1] == "1" ? 1 : -1;
                            else if (kv[0] == "s" && Span(kv, n, out int sa, out int sb, out int sp)) { sitA = sa; sitB = sb; sitPx = sp; }
                            else if (kv[0] == "m" && Span(kv, n, out int ma, out int mb, out int mp)) { movA = ma; movB = mb; movPx = mp; }
                            else if (kv[0] == "h" && kv.Length == 3 && int.TryParse(kv[1], out int hf) && int.TryParse(kv[2], out int hm) && hf >= 0 && hf < n && hm is > 0 and <= 3000) { hold = hf; holdMs = hm; }
                        }
                        list.Add(new ClipInfo("clip_" + p[0] + ".png", n, cw, hh, group, hold, holdMs, prob, jump, sitA, sitB, sitPx, movA, movB, movPx, dir, pingPong, special));
                    }
                }
        }
        catch { }
        return _clipList = list.ToArray();
    }

    /// <summary>상황 그림을 더 띄울 수 있는 최대 높이(창 위 여유, px).</summary>
    private static int LiftRoom() => Math.Max(2, _fh / 5);

    /// <summary>
    /// 장마다 위아래로 옮길 양(_clipDy)과 공중 장면(_clipAir). 모든 장: 가장 아래 불투명 줄(서 있으면 발, 웅크리면 몸 아래, 구르면 몸의 가장 아래)을
    /// 칸 아래 끝 = 작업 표시줄 선에. 점프: 칸 아래 끝보다 1px 넘게 뜬 첫 장~마지막 장을 한 구간으로, 원래 높이를 두고 포물선(사인 반 주기,
    /// 창 위 여유 LiftRoom 안)만큼 더 위로 — 가운데가 가장 높고, 구간 안에서 잠깐 내려앉은 장이 있어도 두 번 뛰지 않는다. 앉기: 원본 px × 배율만큼
    /// 더 아래로(양끝 장은 절반).
    /// </summary>
    private static void ClipGround(nint bits, int cw, int h, int frames, in ClipInfo c, double k)
    {
        var bottom = new int[frames];
        uint* px = (uint*)bits; int stride = cw * frames;
        for (int i = 0; i < frames; i++)
        {
            bottom[i] = -1;
            for (int y = h - 1; y >= 0 && bottom[i] < 0; y--)
                for (int x = 0; x < cw; x++) if ((px[y * stride + i * cw + x] >> 24) > 60) { bottom[i] = y; break; }
        }
        var dy = new int[frames];
        var air = new bool[frames];
        for (int i = 0; i < frames; i++) dy[i] = bottom[i] < 0 ? 0 : h - 1 - bottom[i];
        if (c.Jump)
        {
            // 0.3.123: 영상의 높이 그대로 — 땅에 닿은 장(아래 틈이 작은 장)만 선에 맞추고, 뜬 장은 앞뒤 땅에 닿은 장의 맞춤값을 이어 써서
            // 영상 속 높이만큼 떠 있게 한다. 예전에는 처음 뜬 장~마지막 뜬 장을 포물선 하나로 들어 올려, 영상 속 두 번째·세 번째 점프가
            // 공중에서 다시 뛰는 것처럼 보였다(2026-10-07 사용자)
            int tol = Math.Max(2, h / 25);
            var ground = new bool[frames];
            for (int i = 0; i < frames; i++) ground[i] = bottom[i] < 0 || h - 1 - bottom[i] <= tol;
            for (int i = 0; i < frames; i++)
            {
                if (ground[i]) continue;
                int a = i - 1, b = i + 1;
                while (a >= 0 && !ground[a]) a--;
                while (b < frames && !ground[b]) b++;
                double da = a >= 0 ? dy[a] : 0, db = b < frames ? dy[b] : da;
                if (a < 0) da = db;
                dy[i] = (int)Math.Round(a >= 0 && b < frames ? da + (db - da) * (i - a) / (double)(b - a) : da);
                air[i] = false;   // 영상 속도 그대로(16 fps) — 빨리 넘기지 않는다
            }
        }
        for (int i = c.SitA; c.SitPx > 0 && i <= c.SitB && i < frames; i++)
            dy[i] += (int)Math.Round(c.SitPx * k * (i == c.SitA || i == c.SitB ? 0.5 : 1));
        _clipDy = dy; _clipAir = air;
    }

    /// <summary>
    /// 옆으로 가는 동작의 방향과 거리: 다니는 범위 안에서 갈 수 있는 쪽(둘 다면 반반, 둘 다 모자라면 넓은 쪽으로 갈 수 있는 만큼).
    /// 왼쪽이면 그림을 좌우로 뒤집는다. 재생이 끝나면 간 자리에 그대로 선다(원래 자리로 돌아가지 않는다).
    /// </summary>
    private static void AimClipMove()
    {
        var (min, max) = Range();
        double d = Math.Abs(_clipTravel), right = max - _x, left = _x - min;
        int dir = _clipDir != 0 ? _clipDir : right >= d ? (left >= d && _rnd.Next(2) == 0 ? -1 : 1) : left >= d ? -1 : right >= left ? 1 : -1;
        d = Math.Max(0, Math.Min(d, dir > 0 ? right : left));
        _clipX0 = _x; _clipTravel = dir * d; _clipMirror = _clipDir == 0 && dir < 0;   // 짝 그림은 뒤집지 않는다
    }

    /// <summary>
    /// 이번 재생의 장별 시작 시각: 보통 장 = 1000/_clipFps ms, 공중 장면 = 그 0.6배, 특징 자세 장 = 그만큼 더(h:장:ms).
    /// _clipT[i] = i 번째 장 시작, _clipT[_clipFrames] = 끝.
    /// </summary>
    private static void BuildClipTimes()
    {
        int steps = Math.Max(_clipFrames, _clipSteps);
        var t = new long[steps + 1];
        double frame = 1000.0 / Math.Max(1, _clipFps), acc = 0;
        for (int i = 0; i < steps; i++)
        {
            int f = ClipFrameAt(i);
            t[i] = (long)Math.Round(acc);
            acc += (f < _clipAir.Length && _clipAir[f] ? frame * AirFrameScale : frame) + (f == _clipHold && i < _clipFrames ? _clipHoldMs : 0);   // 멈춤은 앞으로 갈 때 한 번
        }
        t[steps] = (long)Math.Round(acc);
        _clipT = t;
    }

    /// <summary>섞는 차례(_clipBag)가 비었으면 새로 섞는다. 다음 것은 _clipBag[^1](미리 그리기도 이것을 본다 — WalkerPrep.cs).</summary>
    private static void EnsureBag(ClipInfo[] list)
    {
        if (_clipBag.Count == 0 || _clipBag.Exists(i => i >= list.Length))
        {
            _clipBag.Clear();
            for (int i = 0; i < list.Length; i++) if (!list[i].Special && list[i].Group.Length == 0 && (list[i].Prob >= 100 || _rnd.Next(100) < list[i].Prob)) _clipBag.Add(i);
            foreach (var grp in Enumerable.Range(0, list.Length).Where(i => !list[i].Special && list[i].Group.Length > 0).GroupBy(i => list[i].Group))
            {
                var m = grp.ToArray(); int one = m[_rnd.Next(m.Length)];   // 묶음에서 하나만(그 하나의 p: 확률도 따른다 — 구르기 좌우 짝, 0.3.117)
                if (list[one].Prob >= 100 || _rnd.Next(100) < list[one].Prob) _clipBag.Add(one);
            }
            if (_clipBag.Count == 0) for (int i = 0; i < list.Length; i++) if (!list[i].Special) _clipBag.Add(i);   // (확률로 모두 빠진 경우는 없게)
            for (int i = _clipBag.Count - 1; i > 0; i--) { int j = _rnd.Next(i + 1); (_clipBag[i], _clipBag[j]) = (_clipBag[j], _clipBag[i]); }
            if (_clipBag.Count > 1 && _clipBag[^1] == _clipLast) (_clipBag[0], _clipBag[^1]) = (_clipBag[^1], _clipBag[0]);   // 새 묶음의 첫 것이 바로 앞과 같지 않게
        }
    }

    /// <summary>무작위 상황 그림 하나를 칸 크기로 줄여 DIB 한 줄로(바로 앞과 다른 것). 실패하면 false(그냥 서 있기).</summary>
    private static bool LoadClip(string? only = null)
    {
        FreeClip();
        var list = ClipList();
        if (list.Length == 0 || _fh == 0) return false;
        int forced = only is null ? -1 : Array.FindIndex(list, c => c.Name == "clip_" + only + ".png");
        if (only is not null && forced < 0) return false;
        EnsureBag(list);
        int pick;
        if (forced >= 0) pick = forced;
        else if (SeqTest) pick = SeqPick(list.Length);   // 시험 빌드: 목록 순서대로(WalkerTest.cs)
        else
        {
            if (_clipBag.Count == 0) return false;
            pick = _clipBag[^1];
            _clipBag.RemoveAt(_clipBag.Count - 1);
            _clipLast = pick;
        }
        var c = list[pick];
        // 한 방향 그림(d:)인데 그쪽 자리가 모자라고 반대쪽이 더 넓으면 같은 묶음의 반대쪽 짝으로(구르기 오른쪽 ↔ 왼쪽)
        if (c.Dir != 0 && c.MovePx > 0 && c.Group.Length > 0 && !(SeqTest && forced < 0))   // 시험 빌드는 번호 그대로
        {
            var (rmin, rmax) = Range();
            double need = c.MovePx * _fh / (double)Math.Max(1, _baseH);
            double room = c.Dir > 0 ? rmax - _x : _x - rmin, other = c.Dir > 0 ? _x - rmin : rmax - _x;
            if (room < need && other > room)
                for (int i = 0; i < list.Length; i++)
                    if (list[i].Group == c.Group && list[i].Dir == -c.Dir) { c = list[i]; break; }
        }
        var (name, frames, cellW, hold, holdMs) = (c.Name, c.Frames, c.CellW, c.Hold, c.HoldMs);
        _clipLabel = name.Length > 9 ? name.Substring(5, name.Length - 9) : name;
        bool ok = false;
        try
        {
            // 걷기 그림과 같은 배율(원본 서 있는 키 _baseH → _fh). 창 크기(_vw·_vh)는 목록에서 정해져 넘지 않는다.
            // 다른 스레드에서 미리 그려 둔 것이 있으면 그것(0.3.131-E — 그 자리에서 그리면 0.2~0.4초 멈췄다: WalkerPrep.cs)
            double k = _fh / (double)Math.Max(1, _baseH);
            nint bits = TakePrepared(name, k, _vw, _vh, out int dw, out int dh);
            if (bits == 0) bits = RenderClipPixels(name, frames, cellW, k, _vw, _vh, out dw, out dh);
            if (bits == 0) return false;
            _cbits = bits;
            _clipFrames = frames; _ccw = dw; _cdh = dh; _clipHold = hold; _clipHoldMs = holdMs;
            _clipSteps = c.PingPong && frames > 1 ? 2 * frames - 1 : frames;
            ClipGround(_cbits, dw, dh, frames, c, k);
            _clipMoveA = c.MovePx > 0 ? c.MoveA : -1; _clipMoveB = c.MoveB; _clipTravel = c.MovePx * k; _clipMirror = false; _clipDir = c.Dir;
            ok = true;
            PrefetchNext();   // 다음 쉬기에 나올 그림을 지금부터 다른 스레드에서
            return true;
        }
        catch { return false; }
        finally
        {
            if (!ok) FreeClip();
        }
    }

    /// <summary>시험 전용: 지금 배율로 기본 그림 띠와 상황 그림 하나를 PNG 로 내보낸다(사람이 모양을 보려고). 성공하면 true.</summary>
    internal static bool DumpForTest(string dir, int dpi)
    {
        if (CatWidget.Use) return CatWidget.DumpForTest(dir, dpi);
        string log = Path.Combine(dir, "walker-dump.txt");
        _dpi = dpi;
        DumpIcons(Path.Combine(dir, "icons.png"), dpi);
        if (!BuildArt()) { File.WriteAllText(log, "build-art"); return false; }
        bool ok = SaveDib(_bits, _cw * _baseCells, _fh * 3, Path.Combine(dir, "walker-base.png"));
        if (!ok) { File.WriteAllText(log, "save-base"); return false; }
        for (int i = 0; i < 3 && ok; i++)
        {
            if (!LoadClip()) { File.WriteAllText(log, "load-clip " + ClipList().Length); return false; }
            ok = SaveDib(_cbits, _ccw * _clipFrames, _cdh, Path.Combine(dir, $"walker-clip{i}.png"));
        }
        // 창 폭 그림: 상황 그림마다 가운데 장면을, 작업 영역(0~1920) 오른쪽 끝에 선 자리에서(끝에서 안쪽으로 밀리는지 보려고)
        _work = new Native.RECT { left = 0, top = 0, right = 1920, bottom = 1040 };
        var lines = new List<string>();
        for (int i = 0; i < ClipList().Length && ok; i++)
        {
            FreeClip();
            string nm = ClipList()[i].Name;
            bool got = LoadClip(nm.Substring(5, nm.Length - 9));   // i 번째를 이름으로(섞는 차례에 없는 x:1 동작도 — 0.3.127)
            if (!got) { ok = false; break; }
            _act = Act.Clip; _frame = _clipFrames / 2; _x = 1920 - _cw; _fromPx = null;
            ok = Compose(out int wx);
            if (ok) DrawLabel(wx);   // 시험 빌드의 번호 띠도 그림에(WalkerTest.cs)
            ok = ok && SaveDib(_xbits, _vw, _vh, Path.Combine(dir, $"walker-view{i}.png"));
            lines.Add($"view{i} win {wx}..{wx + _vw} x {_vh} cell {_ccw}x{_cdh} frames {_clipFrames}");
        }
        if (ok && _peekOk)
        {
            FreeClip(); _act = Act.Peek; _x = 1920 - _cw; _fromPx = null;
            int depth = (int)Math.Round(_fh * PeekDepth);
            foreach (int s in new[] { 0, depth / 2, depth })
            {
                _sink = s;
                ok = Compose(out int wx) && SaveDib(_xbits, _vw, _vh, Path.Combine(dir, $"walker-peek{s}.png"));
                lines.Add($"peek sink {s} of {depth} win {wx}..{wx + _vw} x {_vh}");
            }
            _sink = 0;
        }
        else if (ok) lines.Add("peek: no picture");
        File.WriteAllLines(Path.Combine(dir, "walker-views.txt"), lines);
        File.WriteAllText(log, ok ? "ok" : "save-clip");
        FreeArt();
        return ok;
    }

    /// <summary>
    /// 시험 전용: 위의 단추 아이콘 비교 — 잠금·전원(Segoe Fluent Icons 14px, 화면과 같은 GDI 글꼴)과 위젯 단추 마스코트 윤곽(선 굵기 여러 개).
    /// 칸마다 같은 크기, 옅은 바탕에 진한 글자색.
    /// </summary>
    private static void DumpIcons(string path, int dpi)
    {
        int cell = Scale(28, dpi), n = 7, w = cell * n, h = cell * 2;
        var bih = new BIH { biSize = (uint)sizeof(BIH), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        nint screen = Native.GetDC(0), mem = Native.CreateCompatibleDC(screen);
        Native.ReleaseDC(0, screen);
        nint dib = CreateDIBSection(mem, ref bih, 0, out nint bits, 0, 0);
        if (dib == 0 || bits == 0) { Native.DeleteDC(mem); return; }
        nint old = Native.SelectObject(mem, dib);
        uint* px = (uint*)bits;
        for (int i = 0; i < w * h; i++) px[i] = 0xFFF0EEF6;
        nint font = Native.MakeFont("Segoe Fluent Icons", Scale(14, dpi), Native.FW_NORMAL);
        nint of = Native.SelectObject(mem, font);
        Native.SetBkMode(mem, 1);
        Native.SetTextColor(mem, 0x403A36);
        string[] glyphs = { "\uE72E", "\uE7E8" };   // Lock, PowerButton
        for (int i = 0; i < glyphs.Length; i++)
        {
            var rc = new Native.RECT { left = i * cell, top = 0, right = (i + 1) * cell, bottom = cell };
            Native.DrawText(mem, glyphs[i], ref rc, Native.DT_CENTER | Native.DT_VCENTER | Native.DT_SINGLELINE);
        }
        Native.SelectObject(mem, of); Native.DeleteObject(font);
        float[] widths = { 0.55f, 0.65f, 0.75f, 0.85f, 1.0f };
        int s = Scale(15, dpi);
        for (int row = 0; row < 2; row++)
            for (int i = 0; i < widths.Length; i++)
                Gdiplus.DrawMascotIcon(mem, (2 + i) * cell + (cell - s) / 2, row * cell + (cell - s) / 2 + 1, s, 0x403A36, widths[i] * dpi / 96f, face: row == 0);
        for (int i = 0; i < w * h; i++) px[i] |= 0xFF000000;
        SaveDib(bits, w, h, path);
        Native.SelectObject(mem, old); Native.DeleteObject(dib); Native.DeleteDC(mem);
    }

    private static bool SaveDib(nint bits, int w, int h, string path)
    {
        if (GdipCreateBitmapFromScan0(w, h, w * 4, 0xE200B, bits, out nint bmp) != 0 || bmp == 0) return false;
        try { Guid enc = new("557CF406-1A04-11D3-9A73-0000F81EF32E"); fixed (char* p = path) return GdipSaveImageToFile(bmp, p, &enc, 0) == 0; }   // PNG
        finally { GdipDisposeImage(bmp); }
    }

    private static void FreeClip()
    {
        if (_cbits != 0) NativeMemory.Free((void*)_cbits);   // 0.3.131-E: DIB 가 아니라 그냥 메모리(WalkerPrep.RenderClipPixels)
        _cmem = _cdib = _coldBmp = _cbits = 0;
        _clipFrames = 0; _clipSteps = 0; _ccw = 0; _cdh = 0; _clipDy = Array.Empty<int>(); _clipAir = Array.Empty<bool>(); _clipHold = -1; _clipHoldMs = 0; _clipT = Array.Empty<long>();
        _clipMoveA = _clipMoveB = -1; _clipMirror = false; _clipDir = 0;
        if (_act == Act.Clip) _act = Act.Stand;
        AnimTick(AnimMs);
    }

    /// <summary>그리기 간격을 바꾼다(보이는 동안만, 같으면 그대로). 걷기 걸음 길이·인사 장 넘김은 AnimMs 간격을 전제로 한다.</summary>
    private static void AnimTick(int ms)
    {
        if (_hwnd == 0 || !_shown || _tickMs == ms) return;
        _tickMs = ms;
        if (!StartTicks(ms)) Native.SetTimer(_hwnd, TimerAnim, (uint)ms, 0);   // 틱 스레드가 돌면 간격만 바뀐다
    }

    private static void Tick()
    {
        if (!_shown) return;
        _ticks++;
        NoteTickTiming();   // 시험 빌드: 늦게 온 틱 세기(WalkerTest.cs)
        // 2초마다 환경을 다시 본다(전체 화면·시스템 패널·작업 표시줄 바뀜) — 다른 스레드에서, 결과는 WM_ENVDONE 으로
        if (_ticks % (CheckMs / AnimMs) == 0 && Interlocked.CompareExchange(ref _envBusy, 1, 0) == 0)   // 누르는 동안에도 본다(R83-2)
        {
            nint hwnd = _hwnd; int dpiNow = _dpi; var w0 = _work;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool redo = true;
                long e0 = System.Diagnostics.Stopwatch.GetTimestamp();
                try { redo = !EnvOk(out Native.RECT work, out int dpi) || dpi != dpiNow || work.bottom != w0.bottom || work.left != w0.left || work.right != w0.right; }
                catch { }
                LogEnv(e0, redo);
                if (!Native.PostMessageW(hwnd, WM_ENVDONE, redo ? 1 : 0, 0)) Interlocked.Exchange(ref _envBusy, 0);
            });
        }
        if (_pressed) return;   // 누르는 동안 멈춘다
        long now = Environment.TickCount64;
        if (_clock == ClockPhase.None && _ticks % 16 == 0 && DateTime.Now.Minute == 59)   // 정시 알림 자전거 그림을 미리(WalkerPrep.cs)
        { Prefetch("clip_" + BikePullClip + ".png"); Prefetch("clip_" + BikeRideClip + ".png"); Prefetch("clip_" + MagicClip + ".png"); }
        if (_clock == ClockPhase.None && ClockDue(DateTime.Now)) StartClock(DateTime.Now);
        if (_clock != ClockPhase.None) { if (_clipRelease && _act != Act.Clip) { FreeClip(); _clipRelease = false; } ClockTick(NowMs()); return; }
        if (_clipRelease && _act != Act.Clip) { FreeClip(); _clipRelease = false; }
        AnimTick(_act is Act.Clip or Act.Greet ? ClipTickMs : _act == Act.Walk ? _walkTickMs : AnimMs);   // 걷기는 빠르기만큼(WalkTicks)
        bool fading = _fromPx is not null;
        if (_resting)
        {
            if (_act == Act.Greet) { _frame++; if (_frame >= MascotGreet.Period(_greetN)) { _frame = 0; _act = Act.Stand; } Render(); }
            else if (_act == Act.Peek)
            {
                int s = PeekSink(now - _clipStart), w = PeekSway(now - _clipStart);
                if (s != _sink || w != _sway || fading) { _sink = s; _sway = w; Render(); }
            }
            else if (_act == Act.Clip)
            {
                if (_clipTickKey != _clipStart) { _clipTickKey = _clipStart; _clipTicks = 0; }   // 새 그림: 틱 수를 처음부터
                long ct = (long)Math.Round(_clipTicks++ * 1000.0 / Math.Max(1, _clipFps));        // 틱마다 정확히 한 장 길이만큼
                int f = 0;
                int steps = Math.Max(_clipFrames, _clipSteps);
                while (f < steps && f + 1 < _clipT.Length && _clipT[f + 1] <= ct) f++;
                if (f >= steps) { BeginFade(); _act = Act.Stand; _frame = 0; _clipRelease = true; AnimTick(AnimMs); Render(); }   // 한 번 재생하면 서 있기로(섞기 뒤 해제)
                else
                {
                    f = ClipFrameAt(f);
                    bool moved = false;
                    if (_clipMoveA >= 0)   // 옆으로 가는 장들: 장 안에서도 고르게(멈추는 장에서는 멈춘다), 끝나면 간 자리에
                    {
                        double part = Math.Min(1, (ct - _clipT[f]) * _clipFps / 1000.0);
                        double prog = f < _clipMoveA ? 0 : f > _clipMoveB ? 1 : (f - _clipMoveA + part) / (_clipMoveB - _clipMoveA + 1);
                        double nx = _clipX0 + _clipTravel * prog;
                        moved = (int)Math.Round(nx) != (int)Math.Round(_x);
                        _x = nx;
                    }
                    if (f != _frame || moved) { _frame = f; Render(); }
                }
            }
            if (now >= _phaseEnd && _act != Act.Clip)   // 틱 수로 재생하므로 늦어진 상황 그림은 끝까지 본다
            {
                if (!_chained && _rnd.Next(100) < 20) { _chained = true; StartRest(); Render(); }   // 가끔 걷지 않고 한 번 더 쉰다
                else { _chained = false; StartWalk(); }
            }
            else if (fading && _act == Act.Stand) Render();   // 서 있기로 바뀌는 섞기는 끝날 때까지 그린다
            return;   // 서 있는 동안은 그리지 않는다
        }
        if (_act == Act.TurnOut)
        {
            _frame++;
            if (_frame >= TurnN) { _act = Act.Walk; _frame = 0; }
            Render();
            return;
        }
        if (_act == Act.TurnIn)
        {
            _frame--;
            if (_frame >= 0) { Render(); return; }
            if (_reverseAfterTurn) { _dir = -_dir; _act = Act.TurnOut; _frame = 0; _walkPhase = 0; _reverseAfterTurn = false; Render(); return; }   // 반대쪽으로 다시 돌아선다
            StartRest(); Render();   // 정면이 된 뒤 쉬기(섞어서)
            return;
        }
        double step = SpeedLogical * _dpi / 96.0 * 62.5 / 1000.0;   // 한 장에 한 걸음 폭(빠르기는 틱 간격 _walkTickMs)
        _x += _dir * step;
        var (min, max) = Range();
        bool edge = false;
        if (_x <= min) { _x = min; edge = _dir < 0; }
        else if (_x >= max) { _x = max; edge = _dir > 0; }
        _walkPhase += 1;   // 틱마다 한 장(빠르기는 틱 간격) — 같은 장이 두 번 나오거나 건너뛰지 않는다
        _frame = WalkCell((int)_walkPhase);
        _testMoves++;
        if (edge || now >= _phaseEnd)
        {
            // 끝에 닿았거나 걸을 만큼 걸었다: 정면으로 돌아선다(끝이면 그 뒤 반대쪽으로 다시 돌아선다)
            _act = Act.TurnIn; _frame = TurnN - 1; _reverseAfterTurn = edge && now < _phaseEnd;
        }
        Render();
        if (_ticks % 10 == 0) SetTestProps();
    }

    // ------------------------------------------------------------------ 클릭

    private static void CancelPress()
    {
        if (!_pressed) return;
        _pressed = false;
        if (_hwnd != 0 && Native.GetCapture() == _hwnd) Native.ReleaseCapture();
    }

    private static void SetTestProps()
    {
        if (_owner == 0) return;
        Native.SetPropW(_owner, "OneKeyWalkerWhy", _why);   // 진단(시험 모드가 아니어도): 숨은 이유 번호
        if (!Program.IsTestMode) return;
        Native.SetPropW(_owner, "OneKeyTestWalker", _shown ? 1 : 0);
        Native.SetPropW(_owner, "OneKeyTestWalkerX", (nint)(int)Math.Round(_x));
        Native.SetPropW(_owner, "OneKeyTestWalkerMoves", _testMoves);
        Native.SetPropW(_owner, "OneKeyTestWalkerShows", _testShows);
        Native.SetPropW(_owner, "OneKeyTestWalkerTimer", _hwnd != 0 && _shown ? 1 : 0);
        Native.SetPropW(_owner, "OneKeyTestWalkerClips", _testClips);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            switch (msg)
            {
                case 0x0021: return 3;   // WM_MOUSEACTIVATE → MA_NOACTIVATE: 눌러도 활성화되지 않는다
                case 0x0020:             // WM_SETCURSOR: 손 모양
                    Native.SetCursor(Native.LoadCursorW(0, (nint)32649 /* IDC_HAND */));
                    return 1;
                case 0x0201:             // WM_LBUTTONDOWN: 마스코트 안에서 누르기 시작
                    if (_shown) { _pressed = true; Native.SetCapture(hwnd); }
                    return 0;
                case 0x0202:             // WM_LBUTTONUP: 같은 누름을 마스코트 안에서 떼었을 때만 클릭
                {
                    bool was = _pressed && Native.GetCapture() == hwnd;
                    int x = (short)(lParam & 0xFFFF), y = (short)((lParam >> 16) & 0xFFFF);
                    // 창 전체(_vw × _vh)가 마스코트 자리다. 투명한 곳은 Windows 가 통과시킨다. 0.3.117 까지는 예전 칸(_cw × _fh, 창 왼쪽 위)만 봐서
                    // 그림이 높아진 새 동작들(창이 커짐)에서는 머리 왼쪽만 눌렸다(2026-10-07 사용자: 눌러도 본창이 안 열림)
                    bool inside = x >= 0 && y >= 0 && x < _vw && y < _vh;
                    _pressed = false;
                    if (Native.GetCapture() == hwnd) Native.ReleaseCapture();
                    // 확정 때도 환경을 다시 본다. 부적합하면 클릭 없이 숨긴다(R83-2)
                    if (was && inside && _shown && _owner != 0)
                    {
                        if (EnvOk(out _, out _)) Native.PostMessageW(_owner, _clickMsg, _showGen, 0);
                        else Evaluate();
                    }
                    return 0;
                }
                case 0x0215:             // WM_CAPTURECHANGED: 캡처를 잃으면 누름 취소
                    _pressed = false;
                    return 0;
                case 0x0113:             // WM_TIMER
                    if (wParam == (nint)TimerAnim) { long t0 = System.Diagnostics.Stopwatch.GetTimestamp(); Tick(); LogTick(t0); }   // LogTick: 시험 빌드 기록
                    else if (wParam == (nint)TimerCheck) { if (_wanted && !_shown) Evaluate(); else Native.KillTimer(hwnd, TimerCheck); }
                    return 0;
                case WM_ANIMTICK:        // 틱 스레드(WalkerPrep.cs)
                    Interlocked.Exchange(ref _tickPending, 0);
                    if (_tickThreadOn) { long t0 = System.Diagnostics.Stopwatch.GetTimestamp(); Tick(); LogTick(t0); }
                    return 0;
                case WM_ENVDONE:         // 다른 스레드의 2초 점검 끝: 부적합·바뀜이면 다시 맞춘다(Evaluate 가 누름 취소·숨김까지)
                    Interlocked.Exchange(ref _envBusy, 0);
                    if (_shown) { if (wParam != 0) Evaluate(); else KeepOnTop(); }
                    return 0;
                case 0x0084:             // WM_NCHITTEST: 보이지 않을 때는 통과
                    if (!_shown) return -1;   // HTTRANSPARENT
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
    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint ms);
    [DllImport("shell32.dll")] private static extern nuint SHAppBarMessage(uint msg, ref APPBARDATA data);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(Native.POINT pt, uint flags);
    [DllImport("user32.dll")] private static extern nint MonitorFromRect(ref Native.RECT rc, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowW(string? cls, string? title);
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
    [DllImport("gdiplus.dll")] private static extern int GdipSetClipRectI(nint graphics, int x, int y, int w, int h, int combineMode);
    [DllImport("gdiplus.dll")] private static extern int GdipResetClip(nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipCloneImage(nint image, out nint clone);
    [DllImport("gdiplus.dll")] private static extern int GdipSaveImageToFile(nint image, char* file, Guid* encoder, nint parameters);
    [DllImport("gdiplus.dll")] private static extern int GdipResetWorldTransform(nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipTranslateWorldTransform(nint graphics, float dx, float dy, int order);
    [DllImport("gdiplus.dll")] private static extern int GdipRotateWorldTransform(nint graphics, float angle, int order);
    [DllImport("gdiplus.dll")] private static extern int GdipDrawImageRectRectI(nint graphics, nint image, int dx, int dy, int dw, int dh, int sx, int sy, int sw, int sh, int unit, nint attrs, nint cb, nint cbData);
}
