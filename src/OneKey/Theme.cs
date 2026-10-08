using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 색·글꼴 체계. iOS 27 UI 키트에서 읽은 값을 Windows 에 맞춰 옮긴 것 (docs/design/tokens.md).
/// 시스템 밝게/어둡게 설정을 따라가고, 강조색은 Windows 설정의 사용자 강조색을 읽는다.
/// 순수 Win32 라 다크 모드를 자동으로 얻을 수 없으므로, 모든 컨트롤 색을 직접 지정하고
/// uxtheme 의 (비공식) 다크 모드 API 로 버튼·콤보의 외형까지 어둡게 맞춘다.
/// </summary>
internal static unsafe class Theme
{
    public static bool IsDark { get; private set; }

    // ---- 색 (COLORREF 0x00BBGGRR) ----
    public static uint WindowBg;        // 창 바탕 (iOS grouped background)
    public static uint CardBg;          // 그룹 상자
    public static uint ControlText;     // 본문 글자 (label)
    public static uint SecondaryText;   // 헤더·푸터·부제 (60%)
    public static uint TertiaryText;    // 꺾쇠·자리표시자 (30%)
    public static uint DisabledText;
    public static uint EditBg, EditText;   // 입력칸 (iOS fill 12% 위)
    public static uint BorderColor;     // 구분선 (12%)
    public static uint Accent;          // 강조색 채움: 파란 버튼의 바탕
    public static uint AccentInk;       // 강조색 선·아이콘: 포커스 테두리, 슬라이더, 아이콘 (그림 요소 대비 3:1 이상)
    public static uint AccentLabel;     // 강조색 글자: 파란 글자 버튼·"단축키 추가"·← 뒤로. 작은 글자라 모든 바탕에서 4.5:1 이상 (Codex QA-01)
    public static uint DangerText;      // 빨간 글자([삭제], 칩 경고). 아이콘은 Danger
    /// <summary>
    /// 연한 강조 채움(목록 행의 [입력], 2026-09-30 사용자 제안): 행마다 되풀이되는 동작 버튼은 진한 채움이 화면을 무겁게 하고
    /// 화면의 주 동작([저장])과 겨루므로, 강조색을 옅게 깐 바탕 + 강조색 글자로. 글자 대비 4.5:1 이상(라이트 4.6, 다크 4.6).
    /// </summary>
    public static uint TintFill;
    /// <summary>
    /// 토스트(2026-09-30 사용자 요청: 메인 창과 다른 색으로 눈에 띄게). 라이트는 짙은 숯색 + 흰 글자(뒤집힌 알림),
    /// 다크는 창보다 떠 보이는 밝은 회색 + 흰 글자. 다크에서 밝은 색으로 뒤집으면 눈이 부시므로(사용자 이전 의견) 뒤집지 않는다.
    /// </summary>
    public static uint ToastBg, ToastText, ToastBorder;
    // B 4단계(칩·알림·[채우기], 확정안 2장 6·7): 칩 제목의 "1Key" 꼬리표, 끝난 단계의 ✓, 경고 알림 머리 "⚠ 안내", [채우기] 알약
    public static uint TagFill, TagText, DoneInk, WarnInk, FillPill, FillRing;
    public static nint ToastBrush;
    public static uint AccentText = 0xFFFFFF;   // 주요 버튼 글자 (B: 어두움 남색, 밝음 흰색)
    public static uint ToggleOff;       // 토글 꺼짐 트랙
    public static uint ToggleOn;        // 토글 켜짐 트랙 (B: 강조 파랑 계열)
    public static uint Danger;
    // ---- B(Calm) 3단계에서 더한 토큰 ----
    public static uint CardBorder;      // 카드 1px 테두리
    public static uint FieldBorder;     // 입력칸·드롭다운·키 칸 1px 테두리
    public static uint SecFill;         // 보조 버튼 채움(.sec)
    public static uint TileFill, TileInk;   // 목록 행 아이콘 원
    public static uint DangerFill;      // 위험 버튼 채움(.dan)
    public static uint RingGap;         // 기본 버튼 이중 선의 안쪽 틈
    public static uint GradTop, GradMid, GradBottom, GradEnd, GlowA, GlowB;
    /// <summary>
    /// 새 디자인(2026-10-08 사용자 — 시계 사진과 Balmuda 앱 시안을 참고한 금속 질감): 목록 행 = 파인 판(카드) 위에 올라앉은 조각(Plate),
    /// 창 바탕 위 단추 = 볼록 단추(Knob: 위 밝음 → 아래 어두움 + 아래 1px 그늘).
    /// </summary>
    public static uint PlateFill, PlateHot, KnobTop, KnobBottom;
    public static double GlowAAlpha, GlowBAlpha;
    /// <summary>창 바탕 그라데이션의 주인(본창). 자식 컨트롤은 이 창 기준 자기 위치로 붓 원점을 맞춰 같은 그림을 이어 그린다.</summary>
    public static nint BgOwner;
    /// <summary>지금 화면을 새 디자인(금속 조각·판)으로 옮겼나. 화면을 만들 때 정하고(ClearPage 에서 끈다), 본창의 토글·버튼 등이 이 값으로 모양을 고른다(MetalUi.On).</summary>
    public static bool MetalPage;
    /// <summary>모서리 반지름(논리 px): 카드 16, 입력칸 10 (B 확정안 3장). 버튼은 알약(높이의 절반).</summary>
    public const int CardRadius = 16, FieldRadius = 10;
    public static uint GroupText => SecondaryText;

    /// <summary>마우스를 올렸을 때 / 눌렀을 때 카드 위에 섞는 글자색 비율. 라이트 모드의 호버가 바탕색과 같아 보이지 않도록 라이트는 더 진하게.</summary>
    public static double HoverMix => IsDark ? 0.05 : 0.10;
    public static double PressMix => IsDark ? 0.09 : 0.16;

    /// <summary>
    /// 카드 위 줄의 올림·누름 바탕. 밝은 테마는 회색(글자색 섞기)이면 창 바탕(옅은 회색 그라데이션)과 같아져 카드에 구멍이 뚫린 것처럼
    /// 보였다(2026-10-05 사용자) — 밝은 테마는 강조색을 옅게 섞는다. 어두운 테마는 그대로.
    /// </summary>
    /// <summary>창 바탕 위 버튼(목록 윗줄 [+ 추가]·최소화·잠금·종료)의 채움. 밝은 테마는 창 바탕과 비슷해 잘 안 보여 함께 조금 진하게(2026-10-05 사용자: 테두리 대신 다 같이).</summary>
    public static uint TopFill => Mix(WindowBg, ControlText, IsDark ? 0.12 : 0.11);

    public static uint RowFill(bool pressed) => IsDark ? Mix(CardBg, ControlText, pressed ? PressMix : HoverMix) : Mix(CardBg, Accent, pressed ? 0.16 : 0.09);

    public static nint BgBrush, CardBrush, EditBrush, BorderBrush;

    // ---- 글꼴 (DPI 에 맞춰 CreateFonts 에서 만든다) ----
    public static nint FontBody, FontStrong, FontSmall, FontSmallStrong, FontTitle, FontIcon, FontIconSmall, FontIconTiny;

    private static bool _darkModeInited;

    private static uint Rgb(byte r, byte g, byte b) => (uint)(r | (g << 8) | (b << 16));

    /// <summary>a 위에 b 를 t(0~1) 만큼 섞는다.</summary>
    public static uint Mix(uint a, uint b, double t)
    {
        byte C(int shift) => (byte)Math.Round(((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t);
        return (uint)(C(0) | (C(8) << 8) | (C(16) << 16));
    }

    /// <summary>사용자가 고른 테마: 0 = Windows 설정을 따름, 1 = 밝게, 2 = 어둡게 (설정 › 화면).</summary>
    public static int Mode;

    /// <summary>고른 테마(따름이면 레지스트리의 앱 모드)로 색과 브러시를 준비한다.</summary>
    public static void Detect()
    {
        IsDark = Mode switch { 1 => false, 2 => true, _ => ReadAppsUseLightTheme() == 0 };
        // 검증·문서 캡처 전용(ONEKEY_TEST=1): ONEKEY_TEST_THEME=light|dark 로 시스템 설정과 무관하게 고정
        if (Program.IsTestMode && Environment.GetEnvironmentVariable("ONEKEY_TEST_THEME") is string forced)
            IsDark = forced == "dark" ? true : forced == "light" ? false : IsDark;

        // 디자인 개편 B(Calm) 3단계 토큰 (docs/design/2026-10-03-B-상세/B_확정안.md 4장, 시안 B_상세시안.html 의 .dk/.lt 변수).
        // 카드·입력칸은 실제 투명이 아니라 단색 + 1px 테두리(Codex 차용 1). 반투명 값(흰 70% 등)은 그 아래 단색과 섞은 색으로 둔다.
        // WindowBg 는 그라데이션의 가운데 색: 단색이 필요한 곳(스크롤 막대 트랙, 잠금 배지 고리)과 그라데이션을 못 만들 때의 대체 바탕.
        if (IsDark)
        {
            // 어두움 = 차콜(2026-10-04 사용자: 남색 B 가 너무 밝다 — 눈이 편한 검은 회색 바탕, 단색 검정은 아님, A안 "남색 기운 조금").
            // 위에서 아래로 조금씩 밝아지는 그라데이션과 옅은 빛 번짐은 입체감을 위해 남긴다(사용자). 주 버튼은 흰색 대신 파랑 바탕 + 흰 글자.
            // 새 디자인(2026-10-08): 흑연색 금속 몸체 — 왼쪽 위가 밝고 오른쪽 아래로 어두워진다. 빛 번짐은 없다
            GradTop = Rgb(0x2C, 0x2D, 0x31); GradMid = Rgb(0x20, 0x21, 0x24); GradBottom = Rgb(0x17, 0x18, 0x1A); GradEnd = Rgb(0x0F, 0x10, 0x12);   // 검은 금속(2026-10-08 사용자 — 시안 List-dark 보다 어둡게). Metal.Body 와 같은 색
            GlowA = Rgb(0x8C, 0x78, 0xFF); GlowAAlpha = 0;
            GlowB = Rgb(0x50, 0xAA, 0xFF); GlowBAlpha = 0;
            WindowBg = GradMid;
            CardBg = Rgb(0x28, 0x29, 0x2D);                    // 파인 판(시계판)
            ControlText = Rgb(0xEC, 0xEC, 0xEE);
            SecondaryText = Mix(CardBg, ControlText, 0.76);    // 몸체 왼쪽 위(가장 밝은 곳)에서도 4.5:1
            TertiaryText = Mix(CardBg, ControlText, 0.52);
            DisabledText = Mix(CardBg, ControlText, 0.40);
            EditBg = Rgb(0x1E, 0x1F, 0x22);
            EditText = ControlText;
            CardBorder = Mix(CardBg, ControlText, 0.10);
            FieldBorder = Mix(EditBg, ControlText, 0.18);
            BorderColor = Mix(CardBg, ControlText, 0.07);      // 카드 안 구분선
            SecFill = Mix(CardBg, ControlText, 0.09);          // 보조 버튼·아이콘 원(.sec)
            TileFill = Mix(CardBg, ControlText, 0.09); TileInk = Rgb(0xC9, 0xD2, 0xFF);
            ToggleOff = Mix(CardBg, ControlText, 0.22);
            ToggleOn = Rgb(0x5B, 0x6C, 0xE0);
            Danger = Rgb(0xFF, 0x9B, 0x9B);
            DangerText = Danger;
            DangerFill = Mix(CardBg, Rgb(0xFF, 0x78, 0x78), 0.14);
            Accent = Rgb(0x4C, 0x5D, 0xD8);                    // 주 버튼: 파랑 바탕 + 흰 글자(흰 버튼은 어두운 화면에서 너무 튄다)
            AccentText = Rgb(0xFF, 0xFF, 0xFF);
            AccentInk = Rgb(0x9A, 0xAB, 0xFF);                 // 초점 선·슬라이더·아이콘
            AccentLabel = Rgb(0xA9, 0xB6, 0xFF);               // 강조 글자(.acc)
            RingGap = GradMid;                                 // 기본 버튼 이중 선의 안쪽 틈
            PlateFill = Rgb(0x3A, 0x3B, 0x40); PlateHot = Rgb(0x43, 0x44, 0x4A);
            KnobTop = Rgb(0x5C, 0x5D, 0x62); KnobBottom = Rgb(0x33, 0x34, 0x38);
        }
        else
        {
            // 새 디자인(2026-10-08): 은색 무광 알루미늄 몸체 — 왼쪽 위 흰빛에서 오른쪽 아래 회색으로
            GradTop = Rgb(0xFF, 0xFF, 0xFF); GradMid = Rgb(0xF3, 0xF3, 0xF3); GradBottom = Rgb(0xE6, 0xE6, 0xE7); GradEnd = Rgb(0xD8, 0xD8, 0xDA);   // 시안 Main 그대로
            GlowA = Rgb(0xFF, 0xC4, 0xAA); GlowAAlpha = 0;
            GlowB = Rgb(0x96, 0xC8, 0xFF); GlowBAlpha = 0;
            WindowBg = GradMid;
            CardBg = Rgb(0xE5, 0xE5, 0xE6);                    // 파인 판(시계판)
            ControlText = Rgb(0x1F, 0x1F, 0x22);
            SecondaryText = Rgb(0x55, 0x55, 0x5A);
            TertiaryText = Rgb(0x6E, 0x6E, 0x73);
            DisabledText = Mix(CardBg, ControlText, 0.40);
            EditBg = Rgb(0xFF, 0xFF, 0xFF);
            EditText = ControlText;
            CardBorder = Mix(CardBg, ControlText, 0.07);
            FieldBorder = Mix(EditBg, ControlText, 0.16);
            BorderColor = Mix(CardBg, ControlText, 0.08);
            SecFill = Mix(CardBg, ControlText, 0.07);
            TileFill = Mix(CardBg, Rgb(0x4A, 0x5B, 0xD6), 0.10); TileInk = Rgb(0x3D, 0x4F, 0xCF);
            ToggleOff = Mix(CardBg, ControlText, 0.20);
            ToggleOn = Rgb(0x4A, 0x5B, 0xD6);
            Danger = Rgb(0xB3, 0x26, 0x1E);
            DangerText = Danger;
            DangerFill = Mix(CardBg, Danger, 0.08);
            Accent = Rgb(0x26, 0x33, 0x7F);                    // 주요 버튼: 남색 바탕 + 흰 글자
            AccentText = Rgb(0xFF, 0xFF, 0xFF);
            AccentInk = Rgb(0x4A, 0x5B, 0xD6);
            AccentLabel = Rgb(0x3D, 0x4F, 0xCF);
            RingGap = Rgb(0xFF, 0xFF, 0xFF);
            PlateFill = Rgb(0xFC, 0xFC, 0xFC); PlateHot = Mix(PlateFill, Accent, 0.07);
            KnobTop = Rgb(0xFF, 0xFF, 0xFF); KnobBottom = Rgb(0xD9, 0xD9, 0xDB);
        }

        // 강조색은 Windows 설정을 따르지 않는다(확정안 4: 강조색은 파랑 그대로). 대비는 tools/design/contrast-b.ps1 로 잰다.
        // 목록 행의 [입력]: 연한 채움 + 강조 글자. 어두운 B 는 아이콘 원(12%)보다 옅게(8%) 해야 강조 글자가 4.5:1 (contrast-b.ps1)
        TintFill = IsDark ? Mix(CardBg, AccentInk, 0.12) : TileFill;   // 어두운 차콜: 옅은 파랑 기운 채움(회색이면 [입력]이 비활성처럼 보인다)
        // 알림: B 시안 .toast(어두움 남색 94%, 밝음 흰 바탕 + 본문 글자). 예전 iOS 회색은 버린다.
        ToastBg = IsDark ? Rgb(0x1F, 0x21, 0x2A) : Rgb(0xFF, 0xFF, 0xFF);
        ToastText = IsDark ? Rgb(0xE9, 0xEA, 0xF0) : Rgb(0x16, 0x1D, 0x45);
        TagFill = IsDark ? Mix(CardBg, Rgb(0xFF, 0xFF, 0xFF), 0.16) : Mix(CardBg, Rgb(0x26, 0x33, 0x7F), 0.10);
        TagText = IsDark ? Rgb(0xFF, 0xFF, 0xFF) : Rgb(0x26, 0x33, 0x7F);
        DoneInk = IsDark ? Rgb(0x7E, 0xE2, 0xA8) : Rgb(0x1B, 0x8A, 0x4C);
        WarnInk = IsDark ? Rgb(0xFF, 0xCF, 0x7A) : Rgb(0xA3, 0x5A, 0x00);
        FillPill = Rgb(0x32, 0x3F, 0xA6);   // 시안 .fillbtn 그라데이션(#3a4fc4 → #2a2f86)의 가운데. 웹 페이지 위에 뜨므로 두 테마 같은 색
        FillRing = Mix(FillPill, Rgb(0xA0, 0xAF, 0xFF), 0.6);
        ToastBorder = IsDark ? Mix(ToastBg, Rgb(0xFF, 0xFF, 0xFF), 0.14) : ToastBg;

        FreeBackground();
        if (BgBrush != 0) Native.DeleteObject(BgBrush);
        if (CardBrush != 0) Native.DeleteObject(CardBrush);
        if (EditBrush != 0) Native.DeleteObject(EditBrush);
        if (BorderBrush != 0) Native.DeleteObject(BorderBrush);
        if (ToastBrush != 0) Native.DeleteObject(ToastBrush);
        BgBrush = Native.CreateSolidBrush(WindowBg);
        CardBrush = Native.CreateSolidBrush(CardBg);
        EditBrush = Native.CreateSolidBrush(EditBg);
        BorderBrush = Native.CreateSolidBrush(BorderColor);
        ToastBrush = Native.CreateSolidBrush(ToastBg);
    }

    // ---------------------------------------------------------------- 창 바탕 그림 (B 3단계)

    private static nint _bgBitmap;
    private static int _bgW, _bgH;
    private static bool _bgDark;

    /// <summary>바탕 그림 붓을 버리고 단색 붓을 쓸 수 있게 한다(BgBrush 가 그림이면 0 으로 둔다).</summary>
    private static void FreeBackground()
    {
        if (_bgBitmap == 0) return;
        if (BgBrush != 0) Native.DeleteObject(BgBrush);
        Native.DeleteObject(_bgBitmap);
        _bgBitmap = 0; BgBrush = 0; _bgW = _bgH = 0;
    }

    /// <summary>
    /// 본창 크기(물리 px)의 바탕 그림을 만들어 <see cref="BgBrush"/> 를 그 무늬 붓으로 바꾼다. 같은 크기·테마면 그대로 둔다.
    /// 시안 .dk/.lt 의 --bg: 165° 선형 그라데이션(세 색) 위에 타원 빛 번짐 둘(오른쪽 위, 왼쪽 아래). (별 장식은 0.2.171 에서 뺐다.)
    /// 실패하면 단색 붓(WindowBg)을 그대로 쓴다. 자식 컨트롤은 <see cref="AlignBg"/> 로 붓 원점을 맞춰 같은 그림을 이어 그린다.
    /// </summary>
    /// <summary>창 맨 위에서 제목 표시줄 색으로 모으는 띠의 높이(논리 px).</summary>
    private const double TopBand = 48;

    /// <summary>화면 바탕에 얹는 고정 장식(새 디자인: 목록의 파인 판·머리줄). dc 는 바탕 그림이 선택된 DC, s 는 같은 픽셀.</summary>
    public delegate void PageOverlay(nint dc, Metal.Surf s);

    private static uint[]? _body;
    private static int _bodyW, _bodyH;
    private static bool _bodyDark;
    private static uint _bodyDpi;
    private static string _pageKey = "";

    /// <summary>
    /// 본창 바탕 그림을 만든다: 몸체(그라데이션·안쪽 빛과 그늘, 크기·테마·배율이 같으면 다시 계산하지 않는다) 위에 화면의 고정 장식(overlay)을 얹어
    /// <see cref="BgBrush"/> 를 그 무늬 붓으로 바꾼다. pageKey 가 같으면 그대로 두고 false. 새로 만들었으면 true(자식 컨트롤을 다시 그려야 한다).
    /// 자식 컨트롤은 <see cref="AlignBg"/> 로 붓 원점을 맞춰 판·몸체의 그 자리 픽셀을 그대로 이어 그린다(새 디자인: 단색으로 지우면 판의 빛 번짐이 칸마다 끊긴다).
    /// </summary>
    public static bool BuildBackground(nint owner, int w, int h, uint dpi, string pageKey = "", PageOverlay? overlay = null)
    {
        BgOwner = owner;
        if (w <= 0 || h <= 0) return false;
        bool bodySame = _body is not null && _bodyW == w && _bodyH == h && _bodyDark == IsDark && _bodyDpi == dpi;
        if (bodySame && _bgBitmap != 0 && _bgW == w && _bgH == h && _bgDark == IsDark && _pageKey == pageKey) return false;
        if (!bodySame)
        {
            _body = new uint[w * h];
            fixed (uint* b = _body) FillBackground(b, w, h, dpi);
            _bodyW = w; _bodyH = h; _bodyDark = IsDark; _bodyDpi = dpi;
        }
        var bih = new Fx.BITMAPINFOHEADER { biSize = (uint)sizeof(Fx.BITMAPINFOHEADER), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        nint bmp = Fx.CreateDIBSection(0, ref bih, 0, out nint bits, 0, 0);
        if (bmp == 0 || bits == 0) { if (bmp != 0) Native.DeleteObject(bmp); return false; }
        fixed (uint* b = _body) Buffer.MemoryCopy(b, (void*)bits, (long)w * h * 4, (long)w * h * 4);
        if (overlay is not null)
        {
            nint mem = Native.CreateCompatibleDC(0);
            nint old = Native.SelectObject(mem, bmp);
            Native.SetBkMode(mem, Native.TRANSPARENT);
            try { overlay(mem, new Metal.Surf((uint*)bits, w, h)); }
            catch { }
            Metal.GdiFlush();
            Native.SelectObject(mem, old);
            Native.DeleteDC(mem);
        }
        nint brush = Native.CreatePatternBrush(bmp);
        if (brush == 0) { Native.DeleteObject(bmp); return false; }
        FreeBackground();
        if (BgBrush != 0) Native.DeleteObject(BgBrush);   // 단색 붓
        BgBrush = brush; _bgBitmap = bmp; _bgW = w; _bgH = h; _bgDark = IsDark; _pageKey = pageKey;
        return true;
    }

    /// <summary>바탕 그림의 픽셀(BGRA)을 채운다. 본창 바탕과 대비 실측(--selftest-background)이 같은 함수를 쓴다.</summary>
    private static void FillBackground(uint* px, int w, int h, uint dpi)
    {
        // 새 디자인(2026-10-08): 시안의 창 몸체(152° 네 색 + 안쪽 빛·그늘 셋)를 같은 식으로. 빛 번짐(Glow*)은 쓰지 않는다
        Metal.Body(new Metal.Surf(px, w, h), dpi / 96.0, IsDark, TopBand * dpi / 96.0);
    }

    /// <summary>
    /// 검증 전용(--selftest-background, Codex R166-C1 3): 본창 크기의 실제 바탕 그림(그라데이션 + 빛 번짐 둘 + 맨 위 띠)을 같은 함수로
    /// 만들어, 바탕 위에 바로 그리는 글자 토큰마다 모든 픽셀에 대한 최저 대비와 그 자리를 낸다.
    /// </summary>
    public static string SelfTestBackground(int w, int h, uint dpi)
    {
        var sb = new System.Text.StringBuilder();
        int keep = Mode;
        foreach (int mode in new[] { 2, 1 })
        {
            Mode = mode; Detect();
            uint[] buf = new uint[w * h];
            fixed (uint* px = buf) FillBackground(px, w, h, dpi);
            (string name, uint ink, double need)[] inks =
            {
                ("body", ControlText, 4.5), ("secondary", SecondaryText, 4.5), ("tertiary (no text on the background since 0.2.169 - version label uses SecondaryText; icons 3.0)", TertiaryText, 3.0), ("accent label", AccentLabel, 4.5),
            };
            foreach (var k in inks)
            {
                double min = 99; int at = 0;
                for (int i = 0; i < buf.Length; i++)
                {
                    uint p = buf[i];
                    uint c = (p >> 16 & 0xFF) | (p & 0xFF00) | ((p & 0xFF) << 16);
                    double r = Ratio(k.ink, c);
                    if (r < min) { min = r; at = i; }
                }
                sb.Append($"{(IsDark ? "dark" : "light")} {w}x{h}@{dpi} {k.name}: min {min:F2} at ({at % w},{at / w}) need {k.need} {(min >= k.need ? "ok" : "FAIL")}");
                sb.Append('\n');
            }
        }
        Mode = keep; Detect();
        return sb.ToString();
    }


    private static double Lum(uint c)
    {
        static double L(double v) { v /= 255.0; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        return 0.2126 * L(c & 0xFF) + 0.7152 * L((c >> 8) & 0xFF) + 0.0722 * L((c >> 16) & 0xFF);
    }
    private static double Ratio(uint a, uint b) { double la = Lum(a), lb = Lum(b); return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05); }

    private static uint Glow(uint c, int x, int y, int w, int h, (double rx, double ry, double cx, double cy, double stop) g, uint color, double alpha)
    {
        double ex = (x + 0.5 - g.cx * w) / (g.rx * w), ey = (y + 0.5 - g.cy * h) / (g.ry * h);
        double d = Math.Sqrt(ex * ex + ey * ey);
        if (d >= g.stop) return c;
        return Mix(c, color, alpha * (1 - d / g.stop));
    }

    /// <summary>
    /// dc 에 바탕 붓을 쓸 때 붓 원점을 본창 기준으로 맞춘다(자식 컨트롤이 본창의 같은 자리 그림을 그리도록).
    /// 바탕이 그림이 아니거나 본창 밖의 창이면 아무것도 하지 않는다.
    /// </summary>
    public static void AlignBg(nint dc, nint hwnd)
    {
        if (_bgBitmap == 0 || BgOwner == 0 || hwnd == BgOwner) return;
        var pt = new Native.POINT();
        Native.MapWindowPoints(hwnd, BgOwner, ref pt, 1);
        Native.SetBrushOrgEx(dc, -pt.x, -pt.y, 0);
    }

    /// <summary>Windows 설정의 사용자 강조색. 못 읽으면 iOS 파랑.</summary>
    private static uint ReadAccent()
    {
        uint fallback = IsDark ? Rgb(0x00, 0x91, 0xFF) : Rgb(0x00, 0x88, 0xFF);
        try
        {
            if (Native.DwmGetColorizationColor(out uint argb, out _) != 0) return fallback;
            byte r = (byte)(argb >> 16), g = (byte)(argb >> 8), b = (byte)argb;
            uint c = Rgb(r, g, b);
            // 흰 글자가 읽히도록 너무 밝은 강조색은 조금 어둡게, 다크에서는 너무 어두우면 조금 밝게.
            double lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
            if (lum > 0.62) c = Mix(c, Rgb(0, 0, 0), 0.25);
            else if (IsDark && lum < 0.30) c = Mix(c, Rgb(0xFF, 0xFF, 0xFF), 0.20);
            return c;
        }
        catch { return fallback; }
    }

    /// <summary>DPI 에 맞는 글꼴을 만든다. 이전 글꼴은 지운다.</summary>
    public static void CreateFonts(uint dpi)
    {
        FreeSized();
        foreach (nint f in new[] { FontBody, FontStrong, FontSmall, FontSmallStrong, FontTitle, FontIcon, FontIconSmall, FontIconTiny })
            if (f != 0) Native.DeleteObject(f);

        int Px(int logical) => (int)((long)logical * dpi / 96);
        // GDI 글꼴(입력칸·STATIC·DirectWrite 대체 경로). 한국어는 맑은 고딕을 직접 지정한다 (2026-09-29 사용자 결정): ClearType 에 맞춰
        // 힌팅된 한글 글꼴이라 작은 글씨도 또렷하다. 맑은 고딕에는 보통·굵게만 있어 FW_SEMIBOLD 는 굵게(700)로 그려진다.
        // 다른 언어는 그 언어의 Windows UI 글꼴(다국어, 2026-09-30): 일본어 Yu Gothic UI, 중국어 간체 Microsoft YaHei UI, 영어·베트남어 Segoe UI.
        // 입력칸에 다른 문자(예: 영어 화면에서 한글 이름)를 넣어도 Windows 의 글꼴 연결이 맞는 글꼴로 그린다.
        string face = L.Current switch
        {
            Lang.Ja => "Yu Gothic UI",
            Lang.ZhHans => "Microsoft YaHei UI",
            Lang.En or Lang.Vi => "Segoe UI",
            _ => "Malgun Gothic",
        };
        FontBody        = Native.MakeFont(face, Px(13), Native.FW_NORMAL);
        FontStrong      = Native.MakeFont(face, Px(13), Native.FW_SEMIBOLD);
        FontSmall       = Native.MakeFont(face, Px(12), Native.FW_NORMAL);
        FontSmallStrong = Native.MakeFont(face, Px(12), Native.FW_SEMIBOLD);
        // B 3단계: 큰 제목 24px. 본문과 같은 글꼴·보통 굵기(2026-10-03 사용자 결정 — 얇은 제목 글꼴이 본문과 섞여 보였다).
        // DirectWrite 쪽은 내장 Pretendard 400, 이 GDI 글꼴은 DirectWrite 를 못 쓸 때의 대체 경로다.
        FontTitle       = Native.MakeFont(face, Px(24), Native.FW_NORMAL);
        FontIcon        = Native.MakeFont("Segoe Fluent Icons", Px(14), Native.FW_NORMAL);
        // 작은 둥근 버튼(28px 띠 [편집])의 아이콘: 32px 버튼의 14px 와 같은 비율(2026-10-05 사용자: 연필이 자물쇠보다 커 보임)
        FontIconSmall   = Native.MakeFont("Segoe Fluent Icons", Px(12), Native.FW_NORMAL);
        // 편집 중 칸 모서리의 18px 동그라미 연필: 14px 의 반(2026-10-05 사용자: 연필이 동그라미 밖으로 나옴)
        FontIconTiny    = Native.MakeFont("Segoe Fluent Icons", Px(7), Native.FW_NORMAL);
        _fontDpi = dpi; _fontFace = face;
        Dw.SetDpi(dpi);   // DirectWrite 형식도 같은 크기로 다시 (GDI 글꼴은 대체 경로와 입력칸이 쓴다)
        Dw.SetLanguage(L.Current);   // 그리고 같은 언어의 글꼴 정책으로
    }

    private static readonly Dictionary<(int Tenths, bool Strong), nint> _sized = new();
    private static uint _fontDpi = 96;
    private static string _fontFace = "Malgun Gothic";

    /// <summary>
    /// 새 디자인의 글자 크기(논리 px, 소수 가능)와 굵기의 글꼴. GDI 글꼴을 만들어 DirectWrite 에도 같은 크기·굵기로 알린다(<see cref="Dw.RegisterSized"/>).
    /// 같은 값은 한 번만 만든다. 배율·언어가 바뀌면 CreateFonts 가 모두 지운다.
    /// </summary>
    public static nint Sized(double logical, bool strong)
    {
        var key = ((int)Math.Round(logical * 10), strong);
        if (_sized.TryGetValue(key, out nint f)) return f;
        float px = (float)(logical * _fontDpi / 96.0);
        f = Native.MakeFont(_fontFace, (int)Math.Round(px), strong ? Native.FW_SEMIBOLD : Native.FW_NORMAL);
        if (f != 0) { _sized[key] = f; Dw.RegisterSized(f, px, strong); }
        return f;
    }

    private static void FreeSized()
    {
        foreach (nint f in _sized.Values) Native.DeleteObject(f);
        _sized.Clear();
        Dw.ForgetSized();
    }

    private static int ReadAppsUseLightTheme()
    {
        int value = 1;   // 기본값: 라이트
        fixed (char* sub = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")
        {
            if (Native.RegOpenKeyExW(Native.HKEY_CURRENT_USER, sub, 0, Native.KEY_READ, out nint key) == 0)
            {
                uint data = 0, size = sizeof(uint);
                fixed (char* name = "AppsUseLightTheme")
                    Native.RegQueryValueExW(key, name, 0, 0, &data, ref size);
                value = (int)data;
                Native.RegCloseKey(key);
            }
        }
        return value;
    }

    // ---------------------------------------------------------------- uxtheme (비공식)

    private static nint _pSetPreferredAppMode, _pAllowDarkWindow, _pFlushMenuThemes, _pRefreshPolicy;
    private const int AppMode_AllowDark = 1;

    /// <summary>프로세스 전체에 다크 모드를 허용한다(한 번만). 실패해도 색상 처리로 대체된다.</summary>
    public static void InitProcessDarkMode()
    {
        if (_darkModeInited) return;
        _darkModeInited = true;

        fixed (char* name = "uxtheme.dll")
        {
            nint ux = Native.LoadLibraryW(name);
            if (ux == 0) return;
            _pSetPreferredAppMode = Native.GetProcAddress(ux, (nint)135);   // SetPreferredAppMode
            _pAllowDarkWindow = Native.GetProcAddress(ux, (nint)133);       // AllowDarkModeForWindow
            _pFlushMenuThemes = Native.GetProcAddress(ux, (nint)136);       // FlushMenuThemes
            _pRefreshPolicy = Native.GetProcAddress(ux, (nint)104);         // RefreshImmersiveColorPolicyState
        }

        if (_pSetPreferredAppMode != 0)
            ((delegate* unmanaged[Stdcall]<int, int>)(void*)_pSetPreferredAppMode)(AppMode_AllowDark);
        if (_pRefreshPolicy != 0)
            ((delegate* unmanaged[Stdcall]<void>)(void*)_pRefreshPolicy)();
    }

    private static void AllowDarkModeForWindow(nint hwnd, bool allow)
    {
        if (_pAllowDarkWindow != 0)
            ((delegate* unmanaged[Stdcall]<nint, int, int>)(void*)_pAllowDarkWindow)(hwnd, allow ? 1 : 0);
    }

    /// <summary>창의 제목 표시줄을 현재 테마에 맞춰 어둡게/밝게 한다.</summary>
    public static void ApplyTitleBar(nint hwnd)
    {
        AllowDarkModeForWindow(hwnd, IsDark);
        int dark = IsDark ? 1 : 0;
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, &dark, sizeof(int));
        // 제목 표시줄을 창 바탕의 맨 위 색으로(2026-10-03 사용자 결정, Windows 11 22000+; 그 전 Windows 는 무시하고 기본 색).
        // 그라데이션은 못 그리므로 단색. 글자는 본문 글자색.
        uint caption = GradTop, text = Metal.Ref(IsDark ? 0xB0B0B0u : 0x5F5F5Fu);   // 시안의 제목 줄 "1Key" 글자색
        Native.DwmSetWindowAttribute(hwnd, 35 /* DWMWA_CAPTION_COLOR */, &caption, sizeof(uint));
        Native.DwmSetWindowAttribute(hwnd, 36 /* DWMWA_TEXT_COLOR */, &text, sizeof(uint));
        if (_pFlushMenuThemes != 0)
            ((delegate* unmanaged[Stdcall]<void>)(void*)_pFlushMenuThemes)();
    }

    /// <summary>컨트롤 하나의 서브테마를 현재 테마에 맞춘다(버튼·콤보가 어둡게 그려지도록).</summary>
    public static void ApplyControl(nint ctrl, string cls)
    {
        if (cls == Toggle.ClassName) return;   // 직접 그린다
        AllowDarkModeForWindow(ctrl, IsDark);
        if (cls == "STATIC") DwStatic.Attach(ctrl);   // DirectWrite 2b: 글자는 DirectWrite 로(꺼져 있으면 원래 STATIC)

        // EDIT 은 테마를 걷어낸다. 배경·글자색은 WM_CTLCOLOREDIT 로, 상자는 부모가 직접 그린다.
        if (cls == "EDIT")
        {
            fixed (char* empty = "") Native.SetWindowTheme(ctrl, empty, empty);
            return;
        }

        string sub = cls switch
        {
            "COMBOBOX" => IsDark ? "DarkMode_CFD" : "CFD",
            "TOOLTIP" => IsDark ? "DarkMode_Explorer" : "Explorer",
            _ => IsDark ? "DarkMode_Explorer" : "Explorer",   // BUTTON / STATIC 등
        };
        fixed (char* p = sub) Native.SetWindowTheme(ctrl, p, null);
    }
}
