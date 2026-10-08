using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// DirectWrite + Direct2D 글자 그리기 (2단계 2a, 2026-09-30 사용자 결정 "③ Pretendard + DirectWrite",
/// 렌더링 기본값 "SemiBold 600 · ClearType · 측정 Natural". 설계: docs/reviews/Claude_2026-09-30_03-59-05 v2, 04-08-35 v3).
///
/// - 글꼴: exe 에 넣은 Pretendard Regular·SemiBold 원본(OFL 1.1)을 메모리에서 DirectWrite 전용 모음으로 싣는다. 시스템에 설치하지 않는다.
/// - 그리기와 재기는 같은 함수(<see cref="MakeLayout"/>)가 만든 IDWriteTextLayout 하나로 한다(Natural). 그리기는 DrawTextLayout.
/// - 렌더 타깃은 96 DPI 의 DC 렌더 타깃: 글자 크기·사각형은 이미 배율을 곱한 물리 px 로 넘긴다(1 DIP = 1 px).
/// - 글꼴이 GDI 의 어느 역할(FontBody…)인지로 형식을 고른다. 아이콘 글꼴(Segoe Fluent Icons)과 알 수 없는 글꼴은 GDI 로 그린다(false 반환).
/// - 시작할 때 준비가 안 되면(팩터리·인터페이스·글꼴 싣기·정확한 400/600 얼굴 확인 실패) 처음부터 GDI(맑은 고딕).
/// - 실행 중 그리기 실패: 실패한 그 글자는 GDI 로 바로 그린다(빈 글자 없음). EndDraw 까지 성공한 프레임에서만 실패 횟수를 0 으로 돌리고,
///   연속 3번 실패하면 GDI 로 전환한다. 전환 처리는 메시지 전용 창에 한 번만 올려 안전한 시점에 한다(본창이 없어도 됨):
///   이 스레드의 모든 창을 다시 그리고 <see cref="RendererChanged"/>(본창의 상태 보존 재배치)를 부른다.
/// - 언어별 글꼴 정책(<see cref="Policy"/>, <see cref="SetLanguage"/>): 한국어·영어·베트남어 = 내장 Pretendard 400/600,
///   일본어 = 시스템 Yu Gothic UI 400/600, 중국어 간체 = 시스템 Microsoft YaHei UI 400/700 (Pretendard 에 한자가 없다).
///   글꼴에 없는 글자(한국어 화면의 한자 이름 등)는 DirectWrite 의 시스템 대체 글꼴이 채운다.
///
/// vtable 번호(SDK 헤더의 인터페이스 상속 순서, IUnknown 0~2):
///   ID2D1Factory: CreateDCRenderTarget 16
///   ID2D1RenderTarget: CreateSolidColorBrush 8, DrawTextLayout 28, SetTextAntialiasMode 34, BeginDraw 48, EndDraw 49 · ID2D1DCRenderTarget::BindDC 57
///   IDWriteFactory: RegisterFontFileLoader 13, UnregisterFontFileLoader 14, CreateTextFormat 15, CreateTextLayout 18, CreateEllipsisTrimmingSign 20
///   IDWriteFactory3: CreateFontCollectionFromFontSet 37 · IDWriteFactory5: CreateFontSetBuilder 43, CreateInMemoryFontFileLoader 44
///   IDWriteInMemoryFontFileLoader: CreateInMemoryFontFileReference 4 · IDWriteFontSetBuilder: CreateFontSet 6 · IDWriteFontSetBuilder1: AddFontFile 7
///   IDWriteFontCollection: GetFontFamily 4, FindFamilyName 5 · IDWriteFontFamily: GetFirstMatchingFont 7 · IDWriteFont: GetWeight 4, GetSimulations 10
///   IDWriteTextFormat(IDWriteTextLayout 가 물려받음): SetTextAlignment 3, SetParagraphAlignment 4, SetWordWrapping 5, SetTrimming 9 · IDWriteTextLayout::GetMetrics 60
/// 같은 클래스 안의 오버로드(MSVC 가 순서를 바꿔 배치)가 걸리는 함수는 쓰지 않는다.
/// </summary>
internal static unsafe class Dw
{
    public enum Role { None, Body, Strong, Small, SmallStrong, Title }

    /// <summary>GDI DT_* 와 같은 값(그 뜻을 DirectWrite 줄임표로 옮긴다): 경로 줄임(마지막 \ 뒤를 남김), 낱말 줄임.</summary>
    public const uint DT_PATH_ELLIPSIS = 0x4000, DT_WORD_ELLIPSIS = 0x40000;

    /// <summary>언어별 글꼴 정책. 컬렉션 0 = 시스템 글꼴 모음.</summary>
    public readonly record struct Policy(string Family, nint Collection, string Locale, int Normal, int Strong);

    private const int D2DERR_RECREATE_TARGET = unchecked((int)0x8899000C);
    private const int ClearType = 1;          // D2D1_TEXT_ANTIALIAS_MODE_CLEARTYPE (사용자 결정)
    private const int MaxFailFrames = 3;

    private static bool _tried, _ok, _failed, _recoverPosted, _loaderRegistered;
    private static bool _family, _exact400, _exact600;
    private static nint _d2d, _dw, _dw5, _loader, _coll, _rt, _msgWnd;
    private static Policy _policy;
    private static int _failFrames, _depth;
    private static int _pxBody = 13, _pxSmall = 12, _pxTitle = 24;
    private static readonly Dictionary<(Role, int), nint> _formats = new();
    private static readonly Dictionary<nint, nint> _signs = new();   // 형식 → 말줄임 기호
    private static bool _onceFailed;
    private static Lang _lang = Lang.Ko;
    private static bool _systemFamily;   // 지금 정책이 시스템 글꼴(일본어·중국어)을 찾아 쓰는가

    /// <summary>DirectWrite 로 그리고 있는가(준비됨 + 실행 중 영구 실패 없음).</summary>
    public static bool Enabled => _ok && !_failed;

    /// <summary>GDI 로 바뀐 뒤 부른다(본창의 상태 보존 재배치). 안전한 시점(메시지 처리)에서만 불린다.</summary>
    public static Action? RendererChanged;

    /// <summary>검증용 상태(WM_APP+14): 1 켜짐, 2 실행 중 실패로 꺼짐, 4 Pretendard 가족 찾음, 8 400 정확, 16 600 정확, 32 Natural, 64 ClearType,
    /// 128 시스템 글꼴 정책(일본어·중국어) 사용, 실패 프레임 수 &lt;&lt; 8, 정책 언어(Lang) &lt;&lt; 16.</summary>
    public static long StatusBits =>
        (Enabled ? 1L : 0L) | (_failed ? 2L : 0L) | (_family ? 4L : 0L) | (_exact400 ? 8L : 0L) | (_exact600 ? 16L : 0L) | 32L | 64L
        | (_systemFamily ? 128L : 0L) | ((long)Math.Min(_failFrames, 255) << 8) | ((long)_lang << 16);

    // ---------------------------------------------------------------- 준비

    public static void Init()
    {
        if (_tried) return;
        _tried = true;
        nint builder = 0, set = 0;
        try
        {
            if (Program.TestFails("dw:init")) return;
            Guid iidD2 = new("06152247-6f50-465a-9245-118bfd3b6007");       // ID2D1Factory
            if (D2D1CreateFactory(0, ref iidD2, 0, out _d2d) < 0) return;
            Guid iidDw = new("b859ee5a-d838-4b5b-a2e8-1adc7d93db48");       // IDWriteFactory
            if (DWriteCreateFactory(0, ref iidDw, out _dw) < 0) return;
            Guid iidDw5 = new("958DB99A-BE2A-4F09-AF7D-65189803D1D3");      // IDWriteFactory5 (Windows 10 1703+): 버전 문자열이 아니라 QueryInterface 로 확인
            nint dw5;
            if (((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)V(_dw)[0])(_dw, &iidDw5, &dw5) < 0) return;
            _dw5 = dw5;

            // 메모리 글꼴 로더: 내장 리소스 바이트를 DirectWrite 가 복사한다(owner = null). 배열은 호출 뒤 버린다.
            nint loader;
            if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)V(_dw5)[44])(_dw5, &loader) < 0) return;
            _loader = loader;
            if (((delegate* unmanaged[Stdcall]<nint, nint, int>)V(_dw)[13])(_dw, _loader) < 0) return;
            _loaderRegistered = true;
            if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)V(_dw5)[43])(_dw5, &builder) < 0) return;
            foreach (string res in new[] { "Pretendard-Regular.ttf", "Pretendard-SemiBold.ttf" })
            {
                byte[]? data = ReadResource(res);
                if (data is null) return;
                nint file;
                int hr;
                fixed (byte* p = data)
                    hr = ((delegate* unmanaged[Stdcall]<nint, nint, void*, uint, nint, nint*, int>)V(_loader)[4])(_loader, _dw, p, (uint)data.Length, 0, &file);
                if (hr < 0) return;
                hr = ((delegate* unmanaged[Stdcall]<nint, nint, int>)V(builder)[7])(builder, file);   // AddFontFile
                Release(file);
                if (hr < 0) return;
            }
            if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)V(builder)[6])(builder, &set) < 0) return;
            nint coll;
            if (((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)V(_dw5)[37])(_dw5, set, &coll) < 0) return;
            _coll = coll;

            // 정확한 얼굴 확인: "Pretendard" 가족에서 400·600 을 고르면 합성 없이 그 굵기가 잡혀야 한다.
            uint idx; int exists;
            fixed (char* fam = "Pretendard")
                if (((delegate* unmanaged[Stdcall]<nint, char*, uint*, int*, int>)V(_coll)[5])(_coll, fam, &idx, &exists) < 0 || exists == 0) return;
            _family = true;
            nint family;
            if (((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)V(_coll)[4])(_coll, idx, &family) < 0) return;
            try
            {
                _exact400 = ExactFace(family, 400);
                _exact600 = ExactFace(family, 600);
            }
            finally { Release(family); }
            if (!_exact400 || !_exact600) return;

            _policy = new Policy("Pretendard", _coll, "ko-KR", 400, 600);
            CreateMessageWindow();
            _ok = true;
        }
        catch { _ok = false; }
        finally
        {
            Release(set); Release(builder);
            if (!_ok) Shutdown();
        }
    }

    private static bool ExactFace(nint family, int weight)
    {
        nint font;
        if (((delegate* unmanaged[Stdcall]<nint, int, int, int, nint*, int>)V(family)[7])(family, weight, 5, 0, &font) < 0) return false;
        try
        {
            int w = ((delegate* unmanaged[Stdcall]<nint, int>)V(font)[4])(font);
            int sim = ((delegate* unmanaged[Stdcall]<nint, int>)V(font)[10])(font);
            return w == weight && sim == 0;
        }
        finally { Release(font); }
    }

    private static byte[]? ReadResource(string name)
    {
        using var s = typeof(Dw).Assembly.GetManifestResourceStream(name);
        if (s is null) return null;
        var data = new byte[s.Length];
        int read = 0;
        while (read < data.Length) { int n = s.Read(data, read, data.Length - read); if (n <= 0) return null; read += n; }
        return data;
    }

    /// <summary>OFL 라이선스 전문(글꼴과 함께 exe 에 들어 있음). 못 읽으면 짧은 고지.</summary>
    public static string LicenseText()
    {
        using var s = typeof(Dw).Assembly.GetManifestResourceStream("OFL-Pretendard.txt");
        if (s is null) return "Pretendard — Copyright (c) 2021, Kil Hyung-jin, with Reserved Font Name Pretendard. SIL Open Font License 1.1 (https://openfontlicense.org)";
        using var r = new StreamReader(s);
        return r.ReadToEnd().Replace("\r\n", "\n").TrimEnd();
    }

    /// <summary>만든 것만 역순으로 푼다(형식 → 대상 → 모음 → 로더 등록 해제 → 로더 → 팩터리).</summary>
    private static void Shutdown()
    {
        ClearFormats();
        Release(_rt); _rt = 0;
        Release(_coll); _coll = 0;
        if (_loaderRegistered && _dw != 0) ((delegate* unmanaged[Stdcall]<nint, nint, int>)V(_dw)[14])(_dw, _loader);
        _loaderRegistered = false;
        Release(_loader); _loader = 0;
        Release(_dw5); _dw5 = 0;
        Release(_dw); _dw = 0;
        Release(_d2d); _d2d = 0;
    }

    /// <summary>
    /// 화면 언어에 맞는 글꼴 정책으로 바꾼다(Theme.CreateFonts 가 부른다). 바뀌면 형식을 버린다.
    /// 일본어·중국어의 시스템 글꼴이 이 PC 에 없으면 Pretendard 로 두고(로캘만 그 언어) 한자·가나는 시스템 대체 글꼴이 채운다.
    /// </summary>
    public static void SetLanguage(Lang lang)
    {
        if (!_ok || _dw == 0) return;
        (string family, int strong) = lang switch
        {
            Lang.Ja => ("Yu Gothic UI", 600),
            Lang.ZhHans => ("Microsoft YaHei UI", 700),
            _ => ("", 600),
        };
        bool system = family.Length > 0 && SystemHasFamily(family);
        Policy next = system
            ? new Policy(family, 0, L.Locales[(int)lang], 400, strong)
            : new Policy("Pretendard", _coll, L.Locales[(int)lang], 400, 600);
        _lang = lang; _systemFamily = system;
        if (next == _policy) return;
        _policy = next;
        ClearFormats();
    }

    /// <summary>시스템 글꼴 모음에 그 가족이 있는가 (IDWriteFactory::GetSystemFontCollection 3, IDWriteFontCollection::FindFamilyName 5).</summary>
    private static bool SystemHasFamily(string family)
    {
        nint coll;
        if (((delegate* unmanaged[Stdcall]<nint, nint*, int, int>)V(_dw)[3])(_dw, &coll, 0) < 0 || coll == 0) return false;
        try
        {
            uint idx; int exists;
            fixed (char* f = family)
                return ((delegate* unmanaged[Stdcall]<nint, char*, uint*, int*, int>)V(coll)[5])(coll, f, &idx, &exists) >= 0 && exists != 0;
        }
        finally { Release(coll); }
    }

    /// <summary>배율이 바뀌면(Theme.CreateFonts) 역할별 크기를 다시 정하고 형식을 버린다.</summary>
    public static void SetDpi(uint dpi)
    {
        int Px(int logical) => (int)((long)logical * dpi / 96);
        _pxBody = Px(13); _pxSmall = Px(12); _pxTitle = Px(24);
        ClearFormats();
    }

    private static void ClearFormats()
    {
        foreach (nint s in _signs.Values) Release(s);
        _signs.Clear();
        foreach (nint f in _formats.Values) Release(f);
        _formats.Clear();
    }

    private static Role RoleOf(nint font) =>
        font == 0 ? Role.None
        : font == Theme.FontBody ? Role.Body
        : font == Theme.FontStrong ? Role.Strong
        : font == Theme.FontSmall ? Role.Small
        : font == Theme.FontSmallStrong ? Role.SmallStrong
        : font == Theme.FontTitle ? Role.Title
        : Role.None;   // 아이콘 글꼴 등은 GDI

    private static nint Format(Role role)
    {
        int px = role switch { Role.Small or Role.SmallStrong => _pxSmall, Role.Title => _pxTitle, _ => _pxBody };
        if (_formats.TryGetValue((role, px), out nint f)) return f;
        int weight = role is Role.Strong or Role.SmallStrong ? _policy.Strong : _policy.Normal;
        // 큰 제목도 본문과 같은 글꼴·굵기(400)로 쓴다(2026-10-03 사용자 결정: 제목만 맑은 고딕 Semilight 라 글꼴이 섞여 보였다 → Pretendard Regular).
        // 시안의 얇은 300 은 내장 Pretendard 에 없는 굵기라 버린다. 일본어·중국어는 그 언어의 시스템 글꼴 400.
        string family = _policy.Family; nint coll = _policy.Collection;
        nint fmt;
        int hr;
        fixed (char* fam = family) fixed (char* loc = _policy.Locale)
            hr = ((delegate* unmanaged[Stdcall]<nint, char*, nint, int, int, int, float, char*, nint*, int>)V(_dw)[15])
                (_dw, fam, coll, weight, 0, 5, px, loc, &fmt);
        if (hr < 0) return 0;
        _formats[(role, px)] = fmt;
        return fmt;
    }

    private static nint EllipsisSign(nint fmt)
    {
        if (_signs.TryGetValue(fmt, out nint s)) return s;
        nint sign;
        if (((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)V(_dw)[20])(_dw, fmt, &sign) < 0) return 0;
        _signs[fmt] = sign;
        return sign;
    }

    /// <summary>재기와 그리기가 함께 쓰는 유일한 레이아웃 생성 함수. DT_* 뜻을 DirectWrite 설정으로 옮긴다.</summary>
    private static nint MakeLayout(string text, Role role, float width, float height, uint dt)
    {
        nint fmt = Format(role);
        if (fmt == 0) return 0;
        nint layout;
        int hr;
        fixed (char* t = text)
            hr = ((delegate* unmanaged[Stdcall]<nint, char*, uint, nint, float, float, nint*, int>)V(_dw)[18])(_dw, t, (uint)text.Length, fmt, width, height, &layout);
        if (hr < 0) return 0;
        int align = (dt & Native.DT_CENTER) != 0 ? 2 : (dt & Native.DT_RIGHT) != 0 ? 1 : 0;                  // LEADING / TRAILING / CENTER
        int para = (dt & Native.DT_VCENTER) != 0 ? 2 : (dt & 0x8 /* DT_BOTTOM */) != 0 ? 1 : 0;              // NEAR / FAR / CENTER
        int wrap = (dt & Native.DT_WORDBREAK) != 0 && (dt & Native.DT_SINGLELINE) == 0 ? 0 : 1;             // WRAP / NO_WRAP
        ((delegate* unmanaged[Stdcall]<nint, int, int>)V(layout)[3])(layout, align);
        ((delegate* unmanaged[Stdcall]<nint, int, int>)V(layout)[4])(layout, para);
        ((delegate* unmanaged[Stdcall]<nint, int, int>)V(layout)[5])(layout, wrap);
        if ((dt & (Native.DT_END_ELLIPSIS | DT_PATH_ELLIPSIS | DT_WORD_ELLIPSIS)) != 0)
        {
            var trim = (dt & DT_PATH_ELLIPSIS) != 0 ? new DWRITE_TRIMMING { granularity = 1, delimiter = '\\', delimiterCount = 1 }   // 마지막 폴더·파일 이름은 남긴다
                     : (dt & DT_WORD_ELLIPSIS) != 0 ? new DWRITE_TRIMMING { granularity = 2 /* WORD */ }
                     : new DWRITE_TRIMMING { granularity = 1 /* CHARACTER */ };
            ((delegate* unmanaged[Stdcall]<nint, DWRITE_TRIMMING*, nint, int>)V(layout)[9])(layout, &trim, EllipsisSign(fmt));
        }
        return layout;
    }

    // ---------------------------------------------------------------- 그리기·재기

    /// <summary>
    /// 한 줄/여러 줄 글자를 DirectWrite 로 그린다. 그렸으면 true. false 면 호출한 쪽이 지금처럼 GDI 로 그린다
    /// (꺼짐, 아이콘 글꼴, 그리기 실패한 이번 한 번, 중첩 한도 초과).
    /// </summary>
    public static bool Text(nint hdc, nint font, string s, uint color, int l, int t, int r, int b, uint dt)
    {
        if (!Enabled) return false;
        Role role = RoleOf(font);
        if (role == Role.None) return false;
        if (s.Length == 0 || r <= l || b <= t) return true;
        if (_depth >= 2) return false;   // 공용 + 임시 하나까지. 그보다 깊은 중첩은 그 그리기만 GDI 로

        nint layout = MakeLayout(s, role, r - l, b - t, dt);
        if (layout == 0) { FrameFailed(); return false; }
        bool nested = _depth > 0;
        nint rt = 0, brush = 0;
        _depth++;
        try
        {
            rt = nested ? CreateTarget() : (_rt != 0 ? _rt : (_rt = CreateTarget()));
            if (rt == 0) { FrameFailed(); return false; }
            var rc = new Native.RECT { left = l, top = t, right = r, bottom = b };
            if (((delegate* unmanaged[Stdcall]<nint, nint, Native.RECT*, int>)V(rt)[57])(rt, hdc, &rc) < 0) { DropTarget(rt, nested); FrameFailed(); return false; }
            ((delegate* unmanaged[Stdcall]<nint, void>)V(rt)[48])(rt);                              // BeginDraw
            int hr;
            try
            {
                ((delegate* unmanaged[Stdcall]<nint, int, void>)V(rt)[34])(rt, ClearType);         // SetTextAntialiasMode
                var c = new D2D1_COLOR_F { r = (color & 0xFF) / 255f, g = ((color >> 8) & 0xFF) / 255f, b = ((color >> 16) & 0xFF) / 255f, a = 1 };
                if (((delegate* unmanaged[Stdcall]<nint, D2D1_COLOR_F*, nint, nint*, int>)V(rt)[8])(rt, &c, 0, &brush) >= 0)
                    ((delegate* unmanaged[Stdcall]<nint, D2D1_POINT_2F, nint, nint, int, void>)V(rt)[28])(rt, default, layout, brush, 0);   // DrawTextLayout
            }
            finally
            {
                ulong t1, t2;
                hr = ((delegate* unmanaged[Stdcall]<nint, ulong*, ulong*, int>)V(rt)[49])(rt, &t1, &t2);   // EndDraw (BeginDraw 와 늘 짝)
            }
            // 검증용 실패 주입: 대상은 만들어지고 EndDraw 만 재생성을 요구하는 경우
            if (Program.TestFails("dw:recreate-always") || (Program.TestFails("dw:recreate-once") && !_onceFailed && (_onceFailed = true)))
                hr = D2DERR_RECREATE_TARGET;
            // 이번 글자는 호출한 쪽이 GDI 로 그린다. 공용 대상은 DropTarget 이 풀고, 임시 대상(중첩)은 finally 가 푼다:
            // 중첩일 때 rt 를 0 으로 만들면 finally 의 Release 가 아무것도 풀지 않아 대상이 샌다(Codex V55-6).
            if (hr < 0 || brush == 0) { if (!nested) { DropTarget(rt, false); rt = 0; } FrameFailed(); return false; }
            _failFrames = 0;   // EndDraw 까지 성공한 프레임에서만 초기화 (Codex V2-1)
            return true;
        }
        finally
        {
            Release(brush);
            Release(layout);
            if (nested) Release(rt);
            _depth--;
        }
    }

    /// <summary>한 줄로 놓았을 때의 폭(px, 올림). 꺼져 있거나 아이콘 글꼴이면 null(호출한 쪽이 GDI 로 잰다).</summary>
    public static int? Width(nint font, string s)
    {
        if (!Enabled) return null;
        Role role = RoleOf(font);
        if (role == Role.None) return null;
        if (s.Length == 0) return 0;
        nint layout = MakeLayout(s, role, 100000, 100000, Native.DT_SINGLELINE);
        if (layout == 0) return null;
        try
        {
            DWRITE_TEXT_METRICS m;
            if (((delegate* unmanaged[Stdcall]<nint, DWRITE_TEXT_METRICS*, int>)V(layout)[60])(layout, &m) < 0) return null;
            return (int)Math.Ceiling(m.widthIncludingTrailingWhitespace);
        }
        finally { Release(layout); }
    }

    /// <summary>폭 maxWidth(px) 안에서 줄 바꿈했을 때 DirectWrite 가 차지하는 (폭, 높이) px(올림). 꺼져 있거나 맡지 않는 글꼴이면 null.
    /// 화면을 만들 때 GDI 로 잰 값과 큰 쪽을 쓴다 — 지금 그리는 쪽과, 실행 중 GDI 로 바뀐 뒤 모두 잘리지 않게(Codex V2-3).</summary>
    public static (int W, int H)? Measure(nint font, string s, int maxWidth)
    {
        if (!Enabled) return null;
        Role role = RoleOf(font);
        if (role == Role.None) return null;
        if (s.Length == 0) return (0, 0);
        nint layout = MakeLayout(s, role, Math.Max(1, maxWidth), 100000, Native.DT_WORDBREAK);
        if (layout == 0) return null;
        try
        {
            DWRITE_TEXT_METRICS m;
            if (((delegate* unmanaged[Stdcall]<nint, DWRITE_TEXT_METRICS*, int>)V(layout)[60])(layout, &m) < 0) return null;
            return ((int)Math.Ceiling(m.widthIncludingTrailingWhitespace), (int)Math.Ceiling(m.height));
        }
        finally { Release(layout); }
    }

    private static nint CreateTarget()
    {
        // 소프트웨어 대상: 작은 글자를 자주 묶고 푸는 쓰임새라 GPU 왕복이 없는 쪽. 96 DPI = 물리 px.
        var props = new D2D1_RENDER_TARGET_PROPERTIES { type = 1 /* SOFTWARE */, pixelFormat = new D2D1_PIXEL_FORMAT { format = 87 /* B8G8R8A8 */, alphaMode = 3 /* IGNORE */ }, dpiX = 96, dpiY = 96 };
        nint rt;
        return ((delegate* unmanaged[Stdcall]<nint, D2D1_RENDER_TARGET_PROPERTIES*, nint*, int>)V(_d2d)[16])(_d2d, &props, &rt) < 0 ? 0 : rt;
    }

    private static void DropTarget(nint rt, bool nested)
    {
        if (nested) return;   // 임시 대상은 finally 에서 푼다
        if (rt == _rt) { Release(_rt); _rt = 0; }
    }

    private static void FrameFailed()
    {
        if (++_failFrames < MaxFailFrames || _failed) return;
        _failed = true;   // 이제부터 모두 GDI. 전환 처리는 한 번만 올린다.
        if (!_recoverPosted && _msgWnd != 0) { _recoverPosted = true; Native.PostMessageW(_msgWnd, WmRecover, 0, 0); }
    }

    // ---------------------------------------------------------------- 전환 처리 (메시지 전용 창: 본창이 없어도 동작)

    private const uint WmRecover = Native.WM_APP + 1;

    private static void CreateMessageWindow()
    {
        nint inst = Native.GetModuleHandleW(null);
        Ctl.RegisterClass(inst, "OneKeyDwRecover", &RecoverProc, 0);
        fixed (char* cls = "OneKeyDwRecover") fixed (char* cap = "")
            _msgWnd = Native.CreateWindowExW(0, cls, cap, 0, 0, 0, 0, 0, Native.HWND_MESSAGE, 0, inst, 0);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint RecoverProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            if (msg == WmRecover)
            {
                Release(_rt); _rt = 0;
                ClearFormats();
                EnumThreadWindows(Native.GetCurrentThreadId(), &RedrawOne, 0);   // 열린 상자·토스트·칩·시작 안내까지 GDI 로 다시 그림
                RendererChanged?.Invoke();                                       // 본창이 있으면 상태를 보존해 다시 배치
                return 0;
            }
        }
        catch { }
        return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int RedrawOne(nint hwnd, nint lParam)
    {
        Native.RedrawWindow(hwnd, 0, 0, Native.RDW_INVALIDATE | Native.RDW_ERASE | Native.RDW_ALLCHILDREN);
        return 1;
    }

    // ---------------------------------------------------------------- COM·구조체

    private static nint* V(nint o) => *(nint**)o;
    private static void Release(nint o) { if (o != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)V(o)[2])(o); }

    [StructLayout(LayoutKind.Sequential)] private struct D2D1_PIXEL_FORMAT { public uint format; public int alphaMode; }
    [StructLayout(LayoutKind.Sequential)] private struct D2D1_RENDER_TARGET_PROPERTIES { public int type; public D2D1_PIXEL_FORMAT pixelFormat; public float dpiX, dpiY; public int usage, minLevel; }
    [StructLayout(LayoutKind.Sequential)] private struct D2D1_COLOR_F { public float r, g, b, a; }
    [StructLayout(LayoutKind.Sequential)] private struct D2D1_POINT_2F { public float x, y; }
    [StructLayout(LayoutKind.Sequential)] private struct DWRITE_TRIMMING { public int granularity; public uint delimiter, delimiterCount; }
    [StructLayout(LayoutKind.Sequential)]
    private struct DWRITE_TEXT_METRICS { public float left, top, width, widthIncludingTrailingWhitespace, height, layoutWidth, layoutHeight; public uint maxBidiReorderingDepth, lineCount; }

    [DllImport("d2d1.dll")] private static extern int D2D1CreateFactory(int type, ref Guid riid, nint options, out nint factory);
    [DllImport("dwrite.dll")] private static extern int DWriteCreateFactory(int type, ref Guid riid, out nint factory);
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint threadId, delegate* unmanaged[Stdcall]<nint, nint, int> fn, nint lParam);
}
