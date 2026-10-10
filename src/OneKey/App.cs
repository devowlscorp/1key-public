using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 메인 창. WinForms 없이 Win32 컨트롤로 직접 구성한다.
/// 창 하나가 네 화면을 번갈아 보여 준다: 잠금 → 목록 ↔ 편집 / 고급.
/// (아이폰 암호 앱 구조. docs/design/tokens.md 의 치수를 따른다.)
/// </summary>
internal sealed unsafe partial class App
{
    // ---------------------------------------------------------------- 상수

    private static readonly string ClassName = Program.WindowClass;
    private const string AppTitle = "1Key";
    public const string Version = "0.5.21-F";

    /// <summary>버전을 크기 비교가 가능한 정수로. "0.2.0" → 0x000200.</summary>
    public static int VersionCode
    {
        get
        {
            string[] p = Version.Split('-')[0].Split('.');   // "0.3.131-A"(시험 빌드) → 0.3.131
            int v = 0;
            for (int i = 0; i < 3; i++)
                v = (v << 8) | (i < p.Length && int.TryParse(p[i], out int n) ? Math.Clamp(n, 0, 255) : 0);
            return v;
        }
    }

    // 논리 좌표(96 DPI 기준)
    private const int WinW = 420, Margin = 16, CardW = WinW - Margin * 2, RowH = Row.Height, CardRadius = Theme.CardRadius;
    private const int FieldH = 28, HeaderH = 24, SliderRowH = 58;

    private const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001;
    private const uint WM_INJECT_RESULT = Native.WM_APP + 2;
    private const uint WM_EDIT_MODE = Native.WM_APP + 30;   // 편집 화면의 "용도"을 바꾼 뒤 화면 다시 만들기(드롭다운 알림이 끝난 뒤)
    private const uint WM_EDIT_REBUILD = Native.WM_APP + 31;
    private const uint WM_SETTINGS_REBUILD = Native.WM_APP + 33;   // 설정 화면 다시 만들기(초안 그대로): 관리자 권한 안내 줄이 생기거나 없어질 때
    private const uint WM_HIDE_WIDGET = Native.WM_APP + 32;   // wParam = 위젯 번호: 잠금(Win+L·절전·자동)으로 떠 있던 확인 창을 취소한 뒤 위젯을 닫는다   // 편집 화면 다시 만들기(값·초안 그대로): 입력 방식 안내 칸이 생기거나 없어질 때
    private const uint CBN_SELCHANGE = 1, EN_CHANGE = 0x0300;

    // 잠금 화면
    private const int IdLockPw1 = 101, IdLockPw2 = 102, IdLockBtn = 103, IdUnsupportedExit = 104, IdLockKbdIme = 105, IdLockKbdCaps = 106;
    // 마스터 비밀번호 바꾸기 화면 (T3)
    private const int IdMCur = 501, IdMNew1 = 502, IdMNew2 = 503, IdMCancel = 504, IdMSave = 505, IdMBack = 506, IdRowMaster = 213;
    // 목록 화면
    // 슬롯 행은 1000~1098 (누르면 편집). 다른 id 와 겹치지 않는 대역이다.
    private const int IdRowSlot = 1000, IdRowAdd = 203, IdListFilter = 230, IdListLock = 2014, IdListExit = 2015, IdListMin = 2016;
    // 슬롯 행 오른쪽의 [입력] 버튼은 1100~1198 (D안: 누르면 입력 칩). 검색 결과 수 231, 검색 지우기 232.
    private const int IdRowInput = 1100, IdListCount = 231, IdListClear = 232, IdListFull = 233;
    private const int InputBtnW = 52, InputBtnH = 28;
    // 목록 카드에 한 번에 보이는 행 수는 _visibleRows (보통 8, 작은 화면에서는 3까지 줄인다, T5). 그보다 많으면 휠·스크롤 막대로 넘긴다.
    private const int ScrollW = 10;           // 카드 오른쪽 스크롤 막대 폭
    private const int IdLaunchProgNames = 2030, IdLaunchFolderNames = 2031, IdWalker = 2032, IdCatNameLight = 2033, IdCatNameDark = 2034;   // 설정 › 바로 실행: 띠에 이름 보이기
    private const int IdAutoStart = 2001, IdStartMin = 2002, IdAdmin = 2003, IdAutoLock = 2005, IdConfirmKey = 2006, IdConfirmReset = 2007, IdTheme = 2008, IdLang = 2009;
    private static string[] ThemeNames => new[] { T.CommonFollowWindows, T.ThemeLight, T.ThemeDark };
    /// <summary>언어 선택 목록: 0 = Windows 설정 따름(지금 Windows 언어를 괄호로), 1.. = L.Codes 순서. 언어 이름은 그 언어로 쓴다.</summary>
    private static string[] LangNames => new[] { T.CommonFollowWindows + " (" + L.NativeNames[(int)L.FromWindows()] + ")" }.Concat(L.NativeNames).ToArray();
    private static int LangIndex(string code) => L.Parse(code) is Lang l ? (int)l + 1 : 0;
    private static string LangCode(int index) => index <= 0 || index > L.LangCount ? "" : L.Codes[index - 1];
    private const int IdRowRestart = 210, IdRowDiag = 211, IdRowAdvanced = 212, IdRowSettings = 220, IdSave = 2012;
    private const int IdSBack = 240, IdSCancel = 241;   // 설정 전용 화면 (T5)
    // 화면 위 [도움말] 과 선택에 따라 바뀌는 짧은 안내 (2026-09-29 사용자 결정, Codex 개선안 C)
    private const uint VK_F1 = 0x70;
    private const int IdHelp = 250, IdEEnterNote = 320, IdEMethodNote = 321, IdSLockNote = 2021, IdSAdminNote = 2022;
    private const int MaxVisibleRows = 8, MinVisibleRows = 2;   // 0.5.3: 판 위 조각(66)이 예전 행(42)보다 높아 1366×768·144dpi 에서 3행이면 창이 작업 영역을 넘었다(layout LY-144-1)
    // 편집 화면
    private const int IdEName = 301, IdEPw = 302, IdEShow = 303, IdEHotkey = 304, IdEEnter = 305, IdEMethod = 306, IdENoEnterBrowser = 312;
    // 편집 화면의 사이트 채우기: 연결 상태 글 · [연결]/[해제] · 안내 (2026-09-30 사용자 결정)
    private const int IdESiteText = 322, IdESiteBtn = 323, IdESiteNote = 324;
    // 입력 여러 개(2026-10-01 사용자 결정): 두 번째부터의 입력 칸 331–333, [×] 336–338, [+ 입력 추가] 339, 안내 334,
    // 사이트 채우기 카드의 입력별 연결 글 341–343 · [연결]/[해제] 345–347. 첫 입력은 예전 그대로 IdEPw · IdESiteText · IdESiteBtn.
    private const int IdEInNote = 334, IdEInAdd = 339;
    private const int IdEMode = 348, IdEAllowClick = 349, IdEPerInput = 353;   // 353: 고급 › 입력마다 따로 연결(2026-10-03 단순화)   // 용도: 커서가 있는 칸 / 연결한 사이트·프로그램 칸 (2026-10-01 사용자 제안)
    private static int InTextId(int k) => k == 0 ? IdEPw : 330 + k;
    private static int InDelId(int k) => 335 + k;
    private static int SiteTextId(int k) => k == 0 ? IdESiteText : 340 + k;
    private static int SiteBtnId(int k) => k == 0 ? IdESiteBtn : 344 + k;
    private const int IdRowSiteDiag = 214;   // 고급 › 진단 › 브라우저 입력란 읽기 확인
    private const int IdRowFillLog = 215;    // 고급 › 진단 › 최근 채우기 결과(메모리에만, 값 없음)
    private const int IdETest = 307, IdEDelete = 308, IdECancel = 309, IdESave = 310, IdEBack = 311;
    // 고급 화면
    private const int IdADelay = 401, IdAPre = 402, IdACancel = 403, IdASave = 404, IdABack = 405, IdACpu = 406, IdARam = 407;   // 406·407: 사용량 값(0.3.119)

    private const int IdTrayOpen = 3001, IdTrayLock = 3002, IdTrayExit = 3003, IdTrayMore = 3004, IdTraySlot = 3100;   // 슬롯 항목 3100~3198
    private const nuint TimerDiag = 1, TimerAutoLock = 2, TimerAnim = 3, TimerTest = 10, TimerChipExpire = 20, TimerChipHint = 21, TimerChipDone = 22, TimerKbd = 23, TimerSite = 30, TimerSiteDiag = 31, TimerUsage = 32;
    /// <summary>사이트 채우기: 활성 브라우저 페이지를 확인하는 간격(ms). 확인은 UIA 스레드가 하고 결과만 본창에 온다.</summary>
    private const uint SiteTickMs = 700;
    /// <summary>입력 칩의 공통 확정 키 등록 id. 슬롯 단축키 id(0..98)와 겹치지 않는다.</summary>
    private const int ChipHotkeyId = 0xB000;
    private const int ChipSeconds = 60;
    /// <summary>입력 대기 시간. 검증 모드(ONEKEY_TEST=1)에서만 ONEKEY_TEST_CHIP_MS 로 줄일 수 있다(만료 검사용).</summary>
    private static readonly int ChipMs = Program.IsTestMode && int.TryParse(Environment.GetEnvironmentVariable("ONEKEY_TEST_CHIP_MS"), out int ms) && ms > 0 ? ms : ChipSeconds * 1000;
    private const uint EM_SETSEL = 0x00B1;
    private const int AnimMs = 180;

    private static string[] MethodNames => new[] { T.MethodAuto, T.MethodScan, T.MethodUnicode, T.MethodWmchar, T.MethodClipboard };
    private static string[] ModeNames => new[] { T.EditSegCursor, T.EditSegLink };   // 넣는 곳(나란한 버튼이라 짧게)
    private static readonly int[] AutoLockChoices = { 0, 5, 10, 15, 30, 60 };

    // Segoe Fluent Icons
    private const string IcFolder = "\uE8B7";   // Folder (추가 종류: 폴더)
    private const string IcApp = "\uE7F4";   // TVMonitor (목록 행: 윈도우 프로그램에 연결)
    private const string IcSite = "", IcTyping = "";   // Globe / KeyboardClassic (목록 행: 사이트 채우기 / 커서 자리 입력)
    private const string IcLogin = "", IcMulti = "";   // Permissions(열쇠) / BulletedList — [+ 추가]의 로그인 / 연속 입력
    private const string IcClear = "";   // Cancel (×)
    // 목록 위 트레이 단추(2026-10-06 사용자, Segoe Fluent Icons): 트레이 위젯 모드 켬 = EaseOfAccess(Accessibility), 끔 = Download(Arrow Download)
    private const string IcWidget = "\uE776", IcTray = "\uE896";
    private const string IcLock = "", IcAdd = "", IcEye = "", IcEyeOff = "", IcPower = "";

    private enum Screen { Lock, List, Edit, Advanced, Master, Settings, AddKind, LaunchEdit, PickProgram, BackupMake, Restore, Extra }

    // 배포본 전용 추가 화면(src/OneKey/Extra/ — 저장소에 올리지 않는 폴더). 폴더가 없으면(저장소에서 받아 빌드) 아래 부분 메서드는
    // 구현이 없어 컴파일러가 부르는 곳까지 지운다: 설정에 줄이 생기지 않고 Screen.Extra 로 가는 길도 없다.
    partial void ExtraSettingsRow(int cx, int cw, ref int y);
    partial void ExtraBuild();
    partial void ExtraCommand(int id, ref bool handled);
    partial void ExtraPaint(Metal.Surf body, double k);

    /// <summary>한 화면을 이루는 컨트롤과 부모가 직접 그리는 장식.</summary>
    private sealed class Page
    {
        public readonly List<nint> Controls = new();
        public readonly List<(int X, int Y, int W, int H)> Cards = new();
        /// <summary>새 디자인의 파인 판(시계판, 논리 px). 바탕 그림에 그린다(Theme.BuildBackground 의 overlay).</summary>
        public readonly List<(int X, int Y, int W, int H)> Dials = new();
        /// <summary>목록 머리줄(고양이 윤곽 + 제목 + 버전)을 바탕 그림에 그릴지, 그 글이 넘지 않을 오른쪽 끝(논리 px).</summary>
        public bool ListHeader;
        public int HeaderRight;
        /// <summary>새 디자인으로 옮긴 화면: 카드(Cards)·구분선(Separators)을 금속 조각으로 바탕 그림에 그리고, 컨트롤도 금속 모양(Theme.MetalPage).</summary>
        public bool Metal;
        /// <summary>새 디자인 화면의 머리줄 제목(가운데, 17px 굵게 — 시안). TitleL·TitleR 사이에 놓인다(논리 px).</summary>
        public string Title = "";
        public int TitleL, TitleR;
        /// <summary>새 디자인 잠금 화면의 둥근 입력 알약: Fields 의 번호. 바탕 그림에 금속 테(5px) + 둥근 파인 칸(잠금 위젯의 넓은 알약과 같은 모양)으로 그린다.</summary>
        public readonly HashSet<int> PillFields = new();
        /// <summary>새 디자인 잠금 화면의 말풍선 판(논리 px) · 큰 둥근 단추(가운데 x, 위 y, 지름).</summary>
        public (int X, int Y, int W, int H)? Bubble;
        public (int Cx, int Y, int D)? Hero;
        /// <summary>고양이 그림(왼쪽 x, 위 y, 높이 — 작업 표시줄 고양이와 같은 그림, 테마 색). 논리 px, 바탕 그림에 그린다.</summary>
        public (int X, int Y, int H)? Pic;
        /// <summary>Pic 자리에 그릴 그림(빌드에 든 PNG 이름). 비었으면 고양이(테마 색).</summary>
        public string PicName = "";
        public readonly List<(int X, int Y, int W, int H, bool OnCard)> Fields = new();
        public readonly List<nint> FieldEdits = new();   // Fields 와 같은 순서의 입력칸(포커스면 강조 테두리)   // 입력 상자 (카드 위: 연회색 채움 / 바탕 위: 흰색 + 테두리)
        public readonly List<(int X, int Y, int W)> Separators = new();          // 카드 안 구분선
        // 목록 행과 그 [입력] 버튼 사이·둘레의 바탕(행이 버튼 자리만큼 좁다): 줄이 강조되면 부모가 같은 색으로 칠한다
        public readonly List<(nint Row, int X, int Y, int W, int H, bool First, bool Last)> RowStrips = new();
        public readonly List<(nint H, int X, int Y, int W, int Hh, bool Field)> Layout = new();
        public int Height;
        public int DefaultButton;
        /// <summary>
        /// 아래 고정 막대([취소]/[저장])가 시작하는 논리 y. 0 이면 막대 없음. 화면이 작업 영역보다 길면(T5) 이 위쪽만 스크롤되고
        /// 막대는 창 아래에 붙어 늘 보인다.
        /// </summary>
        public int BarTop;
        /// <summary>만든 컨트롤의 처음 물리 위치와 막대 소속. 페이지 스크롤이 이 값에서 옮긴다.</summary>
        public readonly List<(nint H, int X, int Y, int W, int Hh, bool Bar)> Placed = new();
        /// <summary>목록 카드 오른쪽의 스크롤 막대 (논리 px). 행이 _visibleRows 보다 많을 때만 있다.</summary>
        public (int X, int Y, int W, int H, int ThumbY, int ThumbH)? ScrollTrack;

        /// <summary>목록들의 지금 길이(화면 일부만 다시 만들 때 여기까지 남긴다).</summary>
        public readonly record struct Marks(int Controls, int Cards, int Fields, int FieldEdits, int Separators, int RowStrips, int Layout, int Placed);
        public Marks Mark() => new(Controls.Count, Cards.Count, Fields.Count, FieldEdits.Count, Separators.Count, RowStrips.Count, Layout.Count, Placed.Count);
    }

    // ---------------------------------------------------------------- 상태

    private static App? _self;

    private nint _hwnd, _hInst, _font, _iconLarge, _iconSmall, _iconHero, _tooltip;
    private readonly List<nint> _tipTools = new();
    private uint _dpi = 96;
    private int _textH = 15;
    private Config _cfg = new();
    private bool _trayAdded, _exiting, _trayHintShown;
    private bool _weakMasterAdvised;    // 쉬운 마스터 권고 풍선을 이번 실행에서 이미 보였는가 (T3)
    private string? _injectMessage;
    private string? _injectNotice;      // 입력은 됐지만 알릴 것 (정보 풍선)
    private long _lastActivityMs;
    private int _injectNeedsElevation;

    private Screen _cur = Screen.Lock;
    private Page _page = new();
    private bool _createMode;           // 잠금 화면: 첫 실행(마스터 생성)인가
    private int _widgetHiddenFor;       // Win+L·절전으로 숨긴 잠금 위젯의 번호(LockWidget.Instance). 그 위젯의 늦은 실패 알림은 본창을 띄우지 않는다(R148-3)
    private bool _settingsAdminNote;    // 설정 화면을 만들 때 관리자 권한 안내 줄을 넣었는가(켬 상태일 때만 넣는다)
    private bool _widgetFailed;       // 이 표시 세션에서 잠금 위젯이 실패해 기존 잠금 화면으로 돌아왔다(R-W4): 잠금을 풀 때까지 다시 시도하지 않는다
    private bool _lockBusy;             // 잠금 해제·처음 설정 처리 중(확인 창이 떠 있는 동안 Enter·화살표가 다시 제출되지 않게, R-W3)
    /// <summary>검증 전용: ONEKEY_TEST_LOCKWIDGET=0 이면 기존 잠금 화면(본창)을 쓴다. 기존 창 시험과 위젯 실패 대체 화면을 그대로 시험한다.</summary>
    private static readonly bool TestOldLockScreen = Program.IsTestMode && Environment.GetEnvironmentVariable("ONEKEY_TEST_LOCKWIDGET") == "0";
    private bool _hideAfterUnlock;      // 자동 실행으로 켜졌고 "트레이로 내리기"가 켜져 있으면, 잠금을 푼 직후 트레이로
    private bool _quietHotkeyReport;    // SaveEdit 가 직접 결과를 보여 줄 때, 화면 전환 중의 자동 등록은 풍선을 띄우지 않는다
    private uint _msgTaskbarCreated;    // 탐색기가 다시 시작되면 트레이 아이콘을 다시 넣어야 한다
    private bool _iconHeroOwned;        // _iconHero 가 PrivateExtractIcons 로 얻은 것(해제 필요)인가
    private int _visibleRows = MaxVisibleRows;   // 목록 카드의 행 수 (작업 영역에 맞춰 8 → 2, T5)
    private int _listRowsBuilt;                  // 마지막으로 만든 목록 행 수
    // 페이지 스크롤 (T5): 화면이 작업 영역보다 길 때. 모두 물리 px. _pageScrollMax 가 0 이면 스크롤 없음.
    private int _pageScroll, _pageScrollMax, _viewH, _barShift;
    private int _pageDragOffset = -1;   // 본문 스크롤 막대 손잡이를 끄는 중이면 누른 곳과 손잡이 위끝의 거리(물리 px), 아니면 -1
    private nint _lastFocusSeen;
    private bool _dropSettingsDraft;             // 설정 화면의 [취소]: 이번 화면 전환에서 초안을 남기지 않는다
    private int _listTop;                    // 목록에서 맨 위에 보이는 행의 위치 (걸러진 목록 기준)
    private int _listScrollMax;              // _listTop 의 최댓값 (0 이면 스크롤 없음)
    private string _listFilter = "";         // 검색 칸의 글자 (이름·단축키로 거른다)
    private readonly bool[] _hotkeyFailed = new bool[Config.SlotCount];   // 마지막 등록에서 실패한 슬롯 (목록에 표시)
    /// <summary>목록 화면 설정 부분의 저장하지 않은 값. 접거나 다른 화면에 다녀와도 잃지 않도록 보관하고, 저장하거나 잠그면 버린다.</summary>
    private (bool AutoStart, bool StartMin, bool Admin, int LockIdx, uint ConfirmMods, uint ConfirmVk, int ThemeIdx, int LangIdx, bool ProgNames, bool FolderNames, bool Walker, string NameLight, string NameDark)? _settingsDraft;
    private bool _autostartOn;   // 설정 화면을 만들 때 읽은 실제 자동 실행 등록 상태
    // 입력 칩(D안) 대기 상태. _chipSlot >= 0 이면 대기 중이다. 해제는 ReleaseChip 한 곳에서만 한다.
    private nint _chip;
    private int _chipSlot = -1;
    private int _chipGen;               // 칩 대기 세대: 시작·해제 때마다 올라, 늦게 도착한 명령을 버린다 (V35-3)
    private bool _chipHotkey;           // 확정 키를 등록했는가
    private string _chipHint = "";      // 칩의 평소 안내 글자 (경고 뒤 되돌릴 때)
    private int _clientH;               // 지금 창의 클라이언트 높이(논리 px)
    private int _animFrom, _animTo; private long _animStart;   // 높이 애니메이션
    private int _editSlot = -1;         // 편집 화면: 어느 슬롯인가
    private bool _editDirty, _editPwVisible;
    // 편집 화면 양식(Slot.Form): 문구 한 칸 / 로그인 ID·PW / 연속 입력 여러 칸(예전 화면)
    private const int FormPhrase = 1, FormLogin = 2, FormMulti = 3;
    private int _editForm = FormMulti;
    private Slot? _testSlot;            // 테스트 버튼: 편집 중인 값으로 만든 임시 슬롯
    private bool _diagPending;          // 창 진단: 3초 뒤 검사를 기다리는 중
    private bool _siteDiagPending;      // 브라우저 입력란 확인: 3초 뒤 검사를 기다리는 중
    // 사이트 채우기 (2026-09-30 사용자 결정): 편집 중인 연결, 칩이 입력란 연결용인가, 주기적 확인 상태, 채우는 중
    private SiteLink?[] _editSites = new SiteLink?[Slot.MaxInputs];   // 편집 중인 입력별 연결(0 = 첫 입력)
    private int _editInputs = 1, _linkInput;                           // 편집 중인 입력 수, 칩이 연결할 입력
    private AppLink? _editApp;
    // 편집 중인 "용도"(2026-10-01 사용자 제안): false = 커서가 있는 칸(연결 없음), true = 연결한 사이트·프로그램 칸(입력마다 [연결]).
    // 따로 저장하지 않는다: 연결이 있는 항목은 연결 쪽으로 연다. 연결 쪽에서 연결 없이 저장할 수 없다.
    private bool _editLinkMode;
    private bool _editClipNote;
    private bool _editNoClick;    // 화면에 그 스위치가 없을 때(커서 쪽) 편집 중인 값   // 입력 방식이 클립보드 붙여넣기일 때만 그 주의 칸을 둔다(늘 비워 두면 아래에 빈자리가 남는다, 2026-10-02 사용자)                                          // 편집 중인 프로그램 연결(첫 입력)
    private bool _chipLink;
    private bool _editPerInput;
    private SiteLink? _sameFieldWarned;   // 단계 칩 ②: 아이디 칸과 같은 칸을 한 번 알렸다(같은 칸으로 한 번 더 누르면 그대로 연결)   // 고급 › 입력마다 따로 연결(화면 보기 방식, 저장하지 않음). 꺼져 있으면 입력 2개 항목은 [연결] 한 번으로 짝을 찾는다
    private bool _siteWatch, _siteProbePending, _fillRunning;
    private Uia.ProbeResult? _siteLast;
    /// <summary>
    /// UIA 결과의 세대(Codex V55-3/4): 잠글 때·감시를 켜고 끌 때·새 요청 때 오른다. 늦게 도착한 확인 결과·연결 캡처·진단·[채우기] 명령은
    /// 자기 세대가 지금과 다르면 버린다(잠금 → 해제 뒤 이전 결과가 새 작업에 쓰이지 않게).
    /// </summary>
    private int _uiaGen, _siteLastGen = -1;
    /// <summary>
    /// 연결 캡처·사이트 진단 요청의 세대(Codex R60-1): 감시(_uiaGen)와 나눈다. 예전에는 요청이 _uiaGen 을 올려, 그 사이 끝난 확인의
    /// 대기 표시가 풀리지 않아 감시가 멈출 수 있었다. 잠글 때는 둘 다 오른다.
    /// </summary>
    private int _reqGen;
    /// <summary>확인 요청 id: 대기 표시(_siteProbePending)는 그 표시를 건 요청의 콜백만 푼다(오래된 콜백이 새 요청의 표시를 지우지 않게).</summary>
    private int _probeSeq, _probePendingId;
    // 검증 전용 관찰값(ONEKEY_TEST, WM_TEST_SITE2): 적용된 확인 수, 진단이 대기 중인 확인과 겹친 수·끝난 수, 마지막 연결·채우기 결과
    private int _testProbeStaleKept, _testHotkeyLinked, _testFocusMethodMax, _testAppLinked;
    private int _testProbeDone, _testDiagOverlap, _testDiagDone, _testDiagWaits, _testLinkResult, _testFillResult, _testFillMs, _testFillStage;
    /// <summary>
    /// [채우기] 버튼 토큰(Codex R60-3): 버튼을 보일 때 정하고 클릭 메시지가 싣고 온다. 결과가 바뀌거나 숨기거나 잠그면 무효.
    /// 같은 결과(창·주소·칸)로 다시 보일 때는 그대로 둬, 0.7초마다의 확인이 방금 누른 클릭을 무효로 만들지 않게 한다.
    /// </summary>
    private int _fillToken, _fillTokenSeq, _fillTokenGen;
    private string _fillSignature = "";
    private string? _fillError, _fillNotice;
    private int _testRestartCalls;      // 검증용: admin:dry-run 에서 관리자 재시작이 불린 횟수

    private readonly Dictionary<int, nint> _controls = new();
    private readonly HashSet<nint> _edits = new();
    private readonly HashSet<nint> _editsOnBg = new();   // 바탕 위에 놓인 입력칸 (카드 색으로 칠한다)
    private readonly Dictionary<nint, (nint Brush, uint Text)> _staticStyle = new();

    private nint C(int id) => _controls.TryGetValue(id, out nint h) ? h : 0;
    private int Scale(int v) => (int)((long)v * _dpi / 96);
    /// <summary>목록·편집을 보여도 되는가. 모르는 새 형식의 파일이면(T9) 마스터가 없어 보여도 절대 열지 않는다.</summary>
    private bool Unlocked => !_cfg.Unsupported && (!_cfg.HasMaster || _cfg.IsUnlocked);
    /// <summary>잠금 화면(잠김 또는 처음 설정)이 지금 화면인가. 처음 설정은 마스터가 없어 <see cref="Unlocked"/> 가 참이므로 따로 본다.</summary>
    private bool OnLockScreen => _cur == Screen.Lock && (!Unlocked || _createMode);
    private void MarkActivity() => _lastActivityMs = Environment.TickCount64;

    // ---------------------------------------------------------------- 시작

    public int Run(bool fromAutostart)
    {
        _self = this;
        _hInst = Native.GetModuleHandleW(null);
        // 복원 도중 멈췄으면 예전 쌍 또는 새 쌍 전부로 끝낸다(설정을 읽기 전에). 끝내지 못하면 설정을 읽지도 쓰지도 않는다 — 다시 시도 또는 종료(R41-1)
        while (Backup.RecoverPending(Config.Dir) == Backup.RecoverResult.Blocked)
            if (Native.MsgBox(0, T.RestorePendingBlocked(Config.Dir), AppTitle, 0x5 /* MB_RETRYCANCEL */ | Native.MB_ICONERROR) != 4 /* IDRETRY */) return 0;
        _cfg = Config.Load();
        L.Apply(_cfg.Language);   // 화면 언어: 헤더 값이라 잠금 화면에도 적용된다(빈 값 = Windows 표시 언어)

        Theme.InitProcessDarkMode();
        Theme.Mode = _cfg.ThemeMode;   // 헤더 값이라 잠긴 상태(잠금 화면)에도 적용된다
        Theme.Detect();
        Dw.Init();                            // 글자 그리기: Pretendard + DirectWrite. 준비가 안 되면 GDI(맑은 고딕)로 시작한다
        Dw.RendererChanged = OnRendererChanged;

        // "항상 관리자 권한" 설정이 켜져 있으면 승격해서 다시 띄운다.
        // T2: 단일 인스턴스 잠금은 **쥔 채로** 자식에게 이 프로세스 번호를 넘긴다. 자식은 이 프로세스가 끝나기를(최대 10초) 기다린 뒤
        // 잠금을 잡는다. 그래서 쓰기 가능한 인스턴스가 동시에 둘이 되는 순간이 없다. UAC 를 취소하면 이 프로세스가 그대로 계속한다.
        // 모르는 새 형식의 파일이면(T9) 그 헤더의 관리자 권한 값은 믿지 않는다(기본값으로 읽혀 여기 오지 않는다).
        if (_cfg.RequireAdmin && !Native.IsElevated() && !_cfg.Unsupported)
        {
            string args = (fromAutostart ? "--tray " : string.Empty) + (Program.TookOver ? "--took-over " : string.Empty) + Program.HandoffArg;
            if (Autostart.RestartElevated(args)) return 0;
        }

        // 시작할 때는 늘 잠겨 있으므로 창(잠금 화면)을 바로 보인다. 자동 실행이고 "트레이로 내리기"가 켜져 있으면
        // 잠금을 푼 직후 트레이로 내린다. (예전처럼 숨긴 채 시작하면 단축키가 안 되는 이유를 알 수 없었다.)
        _hideAfterUnlock = fromAutostart && _cfg.StartMinimized && _cfg.HasMaster;

        var icc = new Native.INITCOMMONCONTROLSEX { dwSize = (uint)sizeof(Native.INITCOMMONCONTROLSEX), dwICC = Native.ICC_STANDARD_CLASSES | Native.ICC_WIN95_CLASSES };
        Native.InitCommonControlsEx(ref icc);
        Gdiplus.Init();
        Toggle.Register(_hInst);
        Btn.Register(_hInst);
        Row.Register(_hInst);
        HotkeyBox.Register(_hInst);
        Tile.Register(_hInst);
        Slider.Register(_hInst);
        Dropdown.Register(_hInst);

        if (!RegisterWindowClass()) return 1;
        if (!CreateMainWindow()) return 1;
        CatWidget.Init(_hwnd, WM_WALKER_CLICK);

        // 관리자 권한으로 실행 중일 때도 일반 권한의 새 버전/탐색기가 보내는 메시지를 받도록 허용한다 (UIPI).
        fixed (char* tc = "TaskbarCreated") _msgTaskbarCreated = Native.RegisterWindowMessageW(tc);
        foreach (uint m in new[] { Native.WM_SHOWME, Native.WM_ONEKEY_VERSION, Native.WM_ONEKEY_QUIT, _msgTaskbarCreated })
            if (m != 0) Native.ChangeWindowMessageFilterEx(_hwnd, m, Native.MSGFLT_ALLOW, 0);

        Native.WTSRegisterSessionNotification(_hwnd, 0);   // Windows 잠금(Win+L)·절전 때 함께 잠그기 위해
        LaunchDrop.Register(_hwnd, () => CanDropLaunch, Launcher.ElevationState() == true);   // 끌어 놓기로 바로 실행 추가(관리자 권한이면 파일만)

        if (_cfg.LoadFailed)
            Msg(T.LoadFailed(Config.FilePath, _cfg.CorruptReason is string why ? "(" + why + ")" : ""), AppTitle, Native.MB_OK | Native.MB_ICONWARNING);

        RegisterHotkeys(silent: true);
        InitLaunch();   // 실행 목록(launch.dat)을 읽고 실행 항목 단축키를 등록 — 잠김 중에도 동작, 읽기만으로는 실행하지 않는다
        AddTrayIcon();
        SecureConfigFolder();
        MarkActivity();
        Native.SetTimer(_hwnd, TimerAutoLock, 15000, 0);

        _createMode = !_cfg.HasMaster;
        ShowScreen(Unlocked && !_createMode ? Screen.List : Screen.Lock);

        if (!ShowLockWidget(activate: true))   // 잠겨 있으면 본창 대신 마스코트 위젯(디자인 개편 B). 못 만들면 기존 잠금 화면
        {
            Native.ShowWindow(_hwnd, Native.SW_SHOWNORMAL);
            KeepOnScreen();   // 기본 위치가 작업 영역을 벗어나면 안으로 (T5)
            Native.UpdateWindow(_hwnd);
            FocusFirst();
        }

        int code = MessageLoop();
        Cleanup();
        return code;
    }

    /// <summary>설정 폴더를 본인 + SYSTEM 만으로 좁힌다(2026-10-05 사용자 결정 "기존 폴더까지"). 못 하면 이유와 함께 알린다 — 조용히 보호됐다고 하지 않는다.</summary>
    private void SecureConfigFolder()
    {
        var (state, detail) = DirAcl.Secure(Config.Dir);
        if (Program.IsTestMode) Native.SetPropW(_hwnd, "OneKeyTestAcl", (nint)state);
        if (state is DirAcl.State.Failed or DirAcl.State.SkippedReparse or DirAcl.State.SkippedNetwork or DirAcl.State.SkippedOwner)
            ShowBalloon(AppTitle, T.AclNotApplied(state + (detail.Length > 0 ? ": " + detail : "")), Native.NIIF_WARNING);
    }

    private bool RegisterWindowClass()
    {
        fixed (char* cls = ClassName)
        {
            var wc = new Native.WNDCLASSEXW
            {
                cbSize = (uint)sizeof(Native.WNDCLASSEXW),
                lpfnWndProc = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&WndProcNative,
                hInstance = _hInst,
                hCursor = Native.LoadCursorW(0, 32512),
                hbrBackground = 0,
                lpszClassName = cls,
                hIcon = LoadAppIcon(32, out _),
                hIconSm = LoadAppIcon(16, out _),
            };
            return Native.RegisterClassExW(ref wc) != 0;
        }
    }

    /// <summary>실행 파일의 아이콘. PrivateExtractIcons 가 준 것은 우리 소유라 다 쓰면 DestroyIcon 해야 한다(owned). 대체 아이콘은 공유 자원이다.</summary>
    private nint LoadAppIcon(int logicalSize, out bool owned)
    {
        int px = Scale(logicalSize);
        string exe = Environment.ProcessPath ?? string.Empty;
        if (exe.Length > 0)
        {
            nint hicon = 0; uint id = 0;
            fixed (char* p = exe)
                if (Native.PrivateExtractIconsW(p, 0, px, px, &hicon, &id, 1, 0) > 0 && hicon != 0) { owned = true; return hicon; }
        }
        owned = false;
        return Native.LoadIconW(0, 32512);
    }

    private bool CreateMainWindow()
    {
        const uint style = Native.WS_CAPTION | Native.WS_SYSMENU | Native.WS_MINIMIZEBOX | Native.WS_CLIPCHILDREN | Native.WS_CLIPSIBLINGS;
        const int CW_USEDEFAULT = unchecked((int)0x80000000);
        string caption = AppTitle;
        fixed (char* cls = ClassName) fixed (char* title = caption)
            _hwnd = Native.CreateWindowExW(Native.WS_EX_CONTROLPARENT | Native.WS_EX_COMPOSITED, cls, title, style,
                CW_USEDEFAULT, CW_USEDEFAULT, Scale(WinW), Scale(400), 0, 0, _hInst, 0);
        if (_hwnd == 0) return false;
        // 목록 줄 강조: 행이나 그 버튼의 호버가 바뀌면 둘 사이 바탕(RowStrips)을 다시 칠한다
        Ctl.HotChanged = h =>
        {
            foreach (var rs in _page.RowStrips)
            {
                if (rs.Row != h && !(Ctl.Partner.TryGetValue(h, out nint p) && p == rs.Row)) continue;
                var rc = new Native.RECT { left = Scale(rs.X), top = Yp(rs.Y), right = Scale(rs.X + rs.W), bottom = Yp(rs.Y) + Scale(rs.H) };
                Native.InvalidateRect(_hwnd, (nint)(&rc), true);
                Native.InvalidateRect(rs.Row, 0, false);
            }
        };

        _dpi = WorkArea.DpiFor(_hwnd);   // 검증 모드의 작은 화면 흉내(ONEKEY_TEST_WORKAREA)면 그 배율
        _iconLarge = LoadAppIcon(32, out _);
        _iconSmall = LoadAppIcon(16, out _);
        _iconHero = LoadAppIcon(64, out _iconHeroOwned);
        Native.SendMessageW(_hwnd, Native.WM_SETICON, Native.ICON_BIG, _iconLarge);
        Native.SendMessageW(_hwnd, Native.WM_SETICON, Native.ICON_SMALL, _iconSmall);

        fixed (char* tc = "tooltips_class32")
            _tooltip = Native.CreateWindowExW(Native.WS_EX_TOPMOST, tc, null, Native.WS_POPUP | Native.TTS_ALWAYSTIP | Native.TTS_NOPREFIX,
                0, 0, 0, 0, _hwnd, 0, _hInst, 0);
        if (_tooltip != 0) { Theme.ApplyControl(_tooltip, "TOOLTIP"); Native.SendMessageW(_tooltip, Native.TTM_SETMAXTIPWIDTH, 0, 420); }   // 긴 설명(연결한 칸의 경로 등)은 줄 바꿈

        int corner = Native.DWMWCP_ROUND;
        Native.DwmSetWindowAttribute(_hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, &corner, sizeof(int));
        Theme.ApplyTitleBar(_hwnd);
        CreateFont();
        return true;
    }

    private void CreateFont()
    {
        Theme.CreateFonts(_dpi);
        _font = Theme.FontBody;
        _textH = Native.TextHeight(_hwnd, _font);
    }

    private void ResizeClient(int logicalH)
    {
        _clientH = logicalH;
        var r = new Native.RECT { left = 0, top = 0, right = Scale(WinW), bottom = Scale(logicalH) };
        const uint style = Native.WS_CAPTION | Native.WS_SYSMENU | Native.WS_MINIMIZEBOX;
        // PerMonitorV2 에서는 제목 표시줄 높이가 모니터 DPI 를 따르므로, 그 DPI 로 계산해야 다른 모니터에서 아래가 잘리지 않는다.
        Native.AdjustWindowRectExForDpi(ref r, style, false, Native.WS_EX_CONTROLPARENT | Native.WS_EX_COMPOSITED, _dpi);
        Native.SetWindowPos(_hwnd, 0, 0, 0, r.right - r.left, r.bottom - r.top, SWP_NOMOVE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        KeepOnScreen();
    }

    private const uint MainStyle = Native.WS_CAPTION | Native.WS_SYSMENU | Native.WS_MINIMIZEBOX;
    private const uint MainExStyle = Native.WS_EX_CONTROLPARENT | Native.WS_EX_COMPOSITED;

    /// <summary>
    /// 창을 작업 영역 안에 둔다 (T5). 아래가 작업 표시줄 밑으로 가면 위로, 오른쪽이 넘치면 왼쪽으로 옮긴다.
    /// 화면보다 긴 내용은 창을 키우지 않고 본문을 스크롤하므로(<see cref="SetUpPageScroll"/>) 창은 늘 작업 영역에 들어간다.
    /// </summary>
    private void KeepOnScreen()
    {
        if (!Native.IsWindowVisible(_hwnd) || Native.IsIconic(_hwnd)) return;
        Native.RECT work = WorkArea.For(_hwnd);
        Native.GetWindowRect(_hwnd, out Native.RECT wr);
        int w = wr.right - wr.left, h = wr.bottom - wr.top;
        int x = Math.Clamp(wr.left, work.left, Math.Max(work.left, work.right - w));
        int y = Math.Clamp(wr.top, work.top, Math.Max(work.top, work.bottom - h));
        if (x != wr.left || y != wr.top)
            Native.SetWindowPos(_hwnd, 0, x, y, 0, 0, Native.SWP_NOSIZE_ | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    /// <summary>창의 클라이언트 영역으로 쓸 수 있는 최대 높이(논리 px): 작업 영역 높이에서 제목 표시줄·테두리를 뺀 값 (T5).</summary>
    private int AvailableClientH()
    {
        Native.RECT work = WorkArea.For(_hwnd);
        var r = new Native.RECT { left = 0, top = 0, right = Scale(WinW), bottom = 0 };
        Native.AdjustWindowRectExForDpi(ref r, MainStyle, false, MainExStyle, _dpi);
        int px = (work.bottom - work.top) - (r.bottom - r.top);
        return Math.Max(200, (int)((long)px * 96 / _dpi));
    }

    /// <summary>
    /// 화면이 작업 영역보다 길 때(T5): 창 높이는 avail 로 두고, 아래 고정 막대(BarTop 부터)는 창 아래에 붙이고 그 위 본문만 스크롤한다.
    /// 휠·Tab 으로 움직인다(포커스가 간 컨트롤은 보이도록 스크롤). 돌려주는 값은 창에 쓸 높이(논리 px).
    /// </summary>
    private int SetUpPageScroll(int avail)
    {
        int contentEnd = _page.BarTop > 0 ? _page.BarTop : _page.Height;
        int barH = _page.Height - contentEnd;
        _viewH = Scale(avail - barH);
        _pageScrollMax = Math.Max(0, Scale(contentEnd) - _viewH);
        _barShift = -_pageScrollMax;
        _pageScroll = 0;
        ApplyPageScroll();
        return avail;
    }

    /// <summary>본문 컨트롤을 스크롤 위치로 옮기고, 보이는 칸 밖으로 나간 부분은 창 영역(region)으로 잘라 막대를 덮지 않게 한다.</summary>
    private void ApplyPageScroll()
    {
        foreach (var c in _page.Placed)
        {
            int ny = c.Bar ? c.Y + _barShift : c.Y - _pageScroll;
            int shown = c.Bar ? 2 : CtlAcc.Shown(c.H, out _);
            Native.SetWindowPos(c.H, 0, c.X, ny, 0, 0, Native.SWP_NOSIZE_ | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            if (c.Bar) continue;
            int top = Math.Max(0, -ny), bottom = Math.Min(c.Hh, _viewH - ny);
            // 영역을 비워도 컨트롤은 보이는 상태(WS_VISIBLE)라 Tab 으로 닿는다. 닿으면 EnsureFocusVisible 이 그 자리로 스크롤한다.
            if (top <= 0 && bottom >= c.Hh) Native.SetWindowRgn(c.H, 0, true);
            else Native.SetWindowRgn(c.H, Native.CreateRectRgn(0, top, c.W, Math.Max(top, bottom)), true);
            CtlAcc.Clipped(c.H, shown);
        }
        Native.RedrawWindow(_hwnd, 0, 0, Native.RDW_INVALIDATE | Native.RDW_ERASE | Native.RDW_ALLCHILDREN);
    }

    /// <summary>
    /// 본문 스크롤 막대(2026-09-29 사용자 요청: 높은 배율에서 설정 아래가 가려져도 더 있다는 것을 알 수 있게).
    /// 오른쪽 여백의 가는 트랙과 손잡이, 물리 px. 스크롤이 없으면 false.
    /// </summary>
    private bool PageScrollBar(out Native.RECT track, out Native.RECT thumb)
    {
        track = thumb = default;
        if (_pageScrollMax <= 0 || _viewH <= 0) return false;
        int w = Math.Max(3, Scale(4)), x = Scale(WinW) - Scale(6) - w / 2;   // [도움말]·카드(오른쪽 여백 14·16)와 겹치지 않는 가장자리
        int top = Scale(6), bottom = _viewH - Scale(6);
        if (bottom - top < Scale(40)) return false;
        track = new Native.RECT { left = x, top = top, right = x + w, bottom = bottom };
        int trackH = bottom - top, content = _viewH + _pageScrollMax;
        int thumbH = Math.Max(Scale(28), (int)((long)trackH * _viewH / content));
        int thumbY = top + (int)((long)(trackH - thumbH) * _pageScroll / _pageScrollMax);
        thumb = new Native.RECT { left = x, top = thumbY, right = x + w, bottom = thumbY + thumbH };
        return true;
    }

    private void ScrollPage(int deltaPx)
    {
        int next = Math.Clamp(_pageScroll + deltaPx, 0, _pageScrollMax);
        if (next == _pageScroll) return;
        _pageScroll = next;
        ApplyPageScroll();
    }

    /// <summary>포커스가 바뀌었으면 그 컨트롤이 보이도록 본문을 스크롤한다 (키보드로 가려진 칸에 닿았을 때).</summary>
    private void EnsureFocusVisible()
    {
        if (_stripRows.Count > 0 && Native.GetFocus() is nint sf && sf != _lastFocusSeen) StripEnsureVisible(sf);   // 바로 실행 띠의 가려진 칸
        if (_pageScrollMax <= 0) return;
        nint f = Native.GetFocus();
        if (f == _lastFocusSeen) return;
        _lastFocusSeen = f;
        foreach (var c in _page.Placed)
        {
            if (c.H != f || c.Bar) continue;
            int pad = Scale(8), top = c.Y - _pageScroll;
            if (top < pad) ScrollPage(top - pad);
            else if (top + c.Hh > _viewH - pad) ScrollPage(top + c.Hh - _viewH + pad);
            return;
        }
    }

    /// <summary>논리 y 를 지금 스크롤 상태의 물리 y 로 (장식 그리기·클릭 판정용).</summary>
    private int Yp(int logicalY)
        => _page.BarTop > 0 && logicalY >= _page.BarTop ? Scale(logicalY) + _barShift : Scale(logicalY) - _pageScroll;

    // ---------------------------------------------------------------- 루프 / 정리

    private int MessageLoop()
    {
        while (true)
        {
            int r = Native.GetMessageW(out Native.MSG msg, 0, 0, 0);
            if (r == 0) return (int)msg.wParam;
            if (r == -1) return 1;
            // 포커스 링: 키보드로 이동할 때만 보이고, 마우스를 누르면 숨긴다.
            if (msg.message == Native.WM_KEYDOWN && (msg.wParam == Native.VK_TAB || (msg.wParam >= 0x25 && msg.wParam <= 0x28)))
                SetShowFocus(true);
            else if (msg.message == Native.WM_LBUTTONDOWN)
                SetShowFocus(false);
            // F1 = 지금 화면의 도움말. 1Key 창 안의 키만(전역 단축키가 아니다). 키 조합 칸에서는 키 기록이 우선이다.
            if (msg.message == Native.WM_KEYDOWN && msg.wParam == VK_F1 && (msg.hwnd == _hwnd || Native.IsChild(_hwnd, msg.hwnd))
                && Native.GetClassName(msg.hwnd) != HotkeyBox.ClassName)
            {
                try { ShowHelp(); } catch { }   // 창 프로시저 밖이므로 여기서 막는다: 도움말을 못 띄워도 앱은 계속
                continue;
            }

            if (!Native.IsDialogMessageW(_hwnd, ref msg))
            {
                Native.TranslateMessage(ref msg);
                Native.DispatchMessageW(ref msg);
            }
            EnsureFocusVisible();   // Tab 등으로 스크롤 밖 컨트롤에 포커스가 가면 보이게 (T5)
        }
    }

    private void SetShowFocus(bool on)
    {
        if (Ctl.ShowFocus == on) return;
        Ctl.ShowFocus = on;
        nint f = Native.GetFocus();
        if (f != 0) Native.InvalidateRect(f, 0, false);
    }

    private void Cleanup()
    {
        _exiting = true;
        OnKdfDone();   // 끝난 계산의 결과가 큐에 남아 있으면 반영하지 않고 지운다(R41-3). 아직 계산 중인 것은 프로세스와 함께 사라진다
        RemoveTrayIcon();
        _self = null;
    }
}
