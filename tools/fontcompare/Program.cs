using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FontCompare;

/// <summary>
/// 글꼴 비교 시안 (Codex 제안서 7.2 의 1단계, 2026-09-30 사용자 요청).
/// 같은 문구를 세 가지로 나란히 그린다: ① 맑은 고딕 + GDI(지금 1Key), ② Pretendard + GDI, ③ Pretendard + DirectWrite(Direct2D).
/// 1Key 에서 쓰는 크기(12·13·18 논리 px), 굵기, 색(본문·회색 두 단계·강조 파랑·버튼 흰 글자)을 그대로 쓴다.
/// 실제 모니터의 원래 배율에서 보고 판단하도록 만든 창이다(확대 캡처로 판정하지 않는다). 창을 배율이 다른 모니터로 옮기면 그 배율로 다시 그린다.
/// Pretendard 는 이 프로그램 안에서만 쓴다: GDI 는 AddFontResourceEx(FR_PRIVATE), DirectWrite 는 파일로 만든 전용 글꼴 모음.
/// 시스템에 설치하지 않고, 1Key 와 그 설정은 건드리지 않는다.
/// 키: T 밝게/어둡게, A DirectWrite 안티앨리어싱(ClearType/회색조), M DirectWrite 측정 방식, Esc 닫기.
/// </summary>
internal static unsafe class Program
{
    // ---------------------------------------------------------------- 1Key 와 같은 색 (Theme.cs 2026-09-30)
    private static bool _dark;
    private static uint WindowBg, CardBg, Text, Secondary, Tertiary, AccentInk, Accent;

    private static uint Rgb(int r, int g, int b) => (uint)(r | (g << 8) | (b << 16));
    private static uint Mix(uint a, uint b, double t)
    {
        int C(int shift) => (int)Math.Round(((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t);
        return (uint)(C(0) | (C(8) << 8) | (C(16) << 16));
    }

    private static void SetTheme(bool dark)
    {
        _dark = dark;
        if (dark)
        {
            WindowBg = Rgb(0x20, 0x20, 0x20); CardBg = Rgb(0x2C, 0x2C, 0x2E); Text = Rgb(0xFF, 0xFF, 0xFF);
            Secondary = Mix(CardBg, Rgb(0xEB, 0xEB, 0xF5), 0.60); Tertiary = Mix(CardBg, Rgb(0xEB, 0xEB, 0xF5), 0.30);
            Accent = Rgb(0x17, 0x3E, 0x76); AccentInk = Rgb(0x56, 0x85, 0xD0);
        }
        else
        {
            WindowBg = Rgb(0xF2, 0xF2, 0xF7); CardBg = Rgb(0xFF, 0xFF, 0xFF); Text = Rgb(0, 0, 0);
            Secondary = Mix(CardBg, Rgb(0x3C, 0x3C, 0x43), 0.75); Tertiary = Mix(CardBg, Rgb(0x3C, 0x3C, 0x43), 0.45);
            Accent = Rgb(0x00, 0x88, 0xFF); AccentInk = Rgb(0x00, 0x88, 0xFF);
        }
    }

    // ---------------------------------------------------------------- 견본 (1Key 화면의 실제 문구)
    private enum Ink { Text, Secondary, Tertiary, Accent, OnButton }
    private readonly record struct Sample(int Size, bool Semi, Ink Ink, string S, int Lines = 1, bool Button = false);

    private static readonly Sample[] Samples =
    {
        new(18, true,  Ink.Text,      "단축키 목록"),
        new(13, false, Ink.Text,      "입력 후 Enter 키 보내기"),
        new(13, true,  Ink.Text,      "브라우저에서는 Enter 보내지 않기"),
        new(13, false, Ink.Text,      "결재 비밀번호 · Ctrl + Alt + 1"),
        new(13, false, Ink.Text,      "ABCDEFG abcdefg 0123456789 !@#$%"),
        new(12, true,  Ink.Secondary, "자동 잠금"),
        new(12, false, Ink.Secondary, "그 칸에 들어간 내용이 Enter 로 전송될 수 있습니다."),
        new(12, false, Ink.Secondary, "단축키를 누르면 커서가 있는 칸에 저장한 내용이 입력됩니다."),
        new(12, false, Ink.Tertiary,  "4자 이상, 8자 이상 권장. 예전 .bak 사본은 이전 비밀번호로 열립니다."),
        new(12, false, Ink.Secondary, "브라우저 안의 입력란은 서로 구분할 수 없어, 입력 도중 커서가 페이지 안의 다른 칸으로 옮겨 가도 1Key 는 알아채지 못합니다.", Lines: 3),
        new(13, false, Ink.Accent,    "도움말"),
        new(13, true,  Ink.OnButton,  "저장", Button: true),
    };

    private static uint InkColor(Ink k) => k switch
    {
        Ink.Text => Text, Ink.Secondary => Secondary, Ink.Tertiary => Tertiary, Ink.Accent => AccentInk, _ => Rgb(0xFF, 0xFF, 0xFF),
    };

    // ---------------------------------------------------------------- 배치 (논리 px)
    private const int Margin = 16, ColW = 340, Gap = 16, Pad = 14, HeaderH = 104, ColHeadH = 30;
    private static int RowH(Sample s) => s.Button ? 44 : s.Lines > 1 ? (int)Math.Ceiling(s.Size * 1.6 * s.Lines) + 8 : (int)Math.Ceiling(s.Size * 2.0) + 6;
    private static int ContentH() { int h = 0; foreach (var s in Samples) h += RowH(s); return h; }
    private static int ClientW => Margin * 2 + ColW * 3 + Gap * 2;
    private static int ClientH => HeaderH + ColHeadH + Pad * 2 + ContentH() + Margin;

    private static uint _dpi = 96;
    private static int S(int v) => (int)((long)v * _dpi / 96);

    // ---------------------------------------------------------------- 상태
    private static nint _hwnd;
    private static string _fontDir = "";
    private static bool _gdiLoaded;
    private static string _gdiFaceRegular = "?", _gdiFaceSemi = "?";
    private static nint _dwFactory, _d2dFactory, _dwCollection;
    private static string _dwStatus = "준비 전";
    private static int _aaMode = 2;          // D2D1_TEXT_ANTIALIAS_MODE: 1 ClearType, 2 회색조 (사용자 선택 기본값, 2026-09-30)
    private static int _strong = 600;        // ②③ 굵은 글자의 굵기: 600 SemiBold / 700 Bold (W 로 바꿈)
    private static int _measure = 0;         // DWRITE_MEASURING_MODE: 0 Natural, 1 GDI classic, 2 GDI natural

    [STAThread]
    private static int Main()
    {
        SetProcessDpiAwarenessContext(-4);   // 모니터별 배율 v2
        SetTheme(ReadAppsUseLightTheme() == 0);
        _fontDir = FindFontDir();
        LoadGdiFonts();
        InitDirectWrite();

        nint hInst = GetModuleHandleW(null);
        fixed (char* cls = "OneKeyFontCompare")
        {
            var wc = new WNDCLASSEXW { cbSize = (uint)sizeof(WNDCLASSEXW), lpfnWndProc = &WndProc, hInstance = hInst, lpszClassName = cls, hCursor = LoadCursorW(0, 32512) };
            RegisterClassExW(ref wc);
            fixed (char* title = "1Key 글꼴 비교 시안")
                _hwnd = CreateWindowExW(0, cls, title, 0x00CF0000 /* WS_OVERLAPPEDWINDOW */, 80, 60, 400, 300, 0, 0, hInst, 0);
        }
        _dpi = GetDpiForWindow(_hwnd);
        FitWindow();
        ApplyTitleBar();
        ShowWindow(_hwnd, 5);
        while (GetMessageW(out MSG m, 0, 0, 0) > 0) { TranslateMessage(ref m); DispatchMessageW(ref m); }
        return 0;
    }

    private static void FitWindow()
    {
        var rc = new RECT { right = S(ClientW), bottom = S(ClientH) };
        AdjustWindowRectExForDpi(ref rc, 0x00CF0000, false, 0, _dpi);
        SetWindowPos(_hwnd, 0, 0, 0, rc.right - rc.left, rc.bottom - rc.top, 0x0002 | 0x0004 | 0x0010);   // NOMOVE | NOZORDER | NOACTIVATE
    }

    private static void ApplyTitleBar() { int v = _dark ? 1 : 0; DwmSetWindowAttribute(_hwnd, 20, &v, 4); }

    /// <summary>exe 옆의 Fonts 폴더, 없으면 저장소의 src\OneKey\Fonts.</summary>
    private static string FindFontDir()
    {
        string exeDir = AppContext.BaseDirectory;
        foreach (string d in new[] { Path.Combine(exeDir, "Fonts"), exeDir, Path.GetFullPath(Path.Combine(exeDir, @"..\..\src\OneKey\Fonts")) })
            if (File.Exists(Path.Combine(d, "Pretendard-Regular.ttf"))) return d;
        return exeDir;
    }

    // ---------------------------------------------------------------- GDI: Pretendard 를 이 프로세스에만
    private static void LoadGdiFonts()
    {
        int n = 0;
        foreach (string f in new[] { "Pretendard-Regular.ttf", "Pretendard-SemiBold.ttf", "Pretendard-Bold.ttf" })
        {
            string p = Path.Combine(_fontDir, f);
            if (!File.Exists(p)) continue;
            fixed (char* pp = p) n += AddFontResourceExW(pp, 0x10 /* FR_PRIVATE */, 0);
        }
        _gdiLoaded = n > 0;
        // GDI 가 실제로 고른 글꼴 이름(다른 글꼴로 대체되면 여기서 드러난다)
        nint dc = CreateCompatibleDC(0);
        foreach (bool semi in new[] { false, true })
        {
            nint f = GdiFont(1, 16, semi), old = SelectObject(dc, f);
            if (semi) _gdiFaceSemi = FaceOf(dc); else _gdiFaceRegular = FaceOf(dc);
            SelectObject(dc, old); DeleteObject(f);
        }
        DeleteDC(dc);
    }

    // Pretendard SemiBold 의 GDI(옛 이름표) 이름은 "Pretendard SemiBold" 다. 맑은 고딕은 앱과 같이 FW_SEMIBOLD(굵게로 그려짐).
    private static nint GdiFont(int col, int px, bool semi) => col == 0
        ? MakeFont("Malgun Gothic", px, semi ? 600 : 400)
        : !semi ? MakeFont("Pretendard", px, 400)
        : _strong == 700 ? MakeFont("Pretendard", px, 700)   // Bold 파일은 GDI 이름표도 "Pretendard" 굵게
        : MakeFont("Pretendard SemiBold", px, 400);

    private static nint MakeFont(string face, int px, int weight)
    {
        fixed (char* f = face) return CreateFontW(-px, 0, 0, 0, weight, 0, 0, 0, 1 /* DEFAULT_CHARSET */, 0, 0, 5 /* CLEARTYPE_QUALITY */, 0, f);
    }

    private static string FaceOf(nint hdc)
    {
        char* buf = stackalloc char[64];
        buf[0] = '\0';
        return GetTextFaceW(hdc, 64, buf) > 0 ? new string(buf) : "?";
    }

    // ---------------------------------------------------------------- DirectWrite + Direct2D (COM 직접 호출)
    private static nint* V(nint o) => *(nint**)o;
    private static void Release(nint o) { if (o != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)V(o)[2])(o); }

    private static void InitDirectWrite()
    {
        try
        {
            Guid iidD2 = new("06152247-6f50-465a-9245-118bfd3b6007");
            int hr = D2D1CreateFactory(0, ref iidD2, 0, out _d2dFactory);
            if (hr < 0) { _dwStatus = $"Direct2D 실패 0x{hr:X8}"; return; }
            Guid iidDw5 = new("958DB99A-BE2A-4F09-AF7D-65189803D1D3");   // IDWriteFactory5 (Windows 10 1703+)
            hr = DWriteCreateFactory(0, ref iidDw5, out _dwFactory);
            if (hr < 0) { _dwStatus = $"DirectWrite 5 실패 0x{hr:X8}"; return; }

            // 파일 두 개로 전용 글꼴 모음을 만든다: IDWriteFactory5::CreateFontSetBuilder → IDWriteFontSetBuilder1::AddFontFile
            nint builder;
            hr = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)V(_dwFactory)[43])(_dwFactory, &builder);
            if (hr < 0) { _dwStatus = $"FontSetBuilder 실패 0x{hr:X8}"; return; }
            int added = 0;
            foreach (string f in new[] { "Pretendard-Regular.ttf", "Pretendard-SemiBold.ttf", "Pretendard-Bold.ttf" })
            {
                string p = Path.Combine(_fontDir, f);
                if (!File.Exists(p)) continue;
                nint file;
                fixed (char* pp = p) hr = ((delegate* unmanaged[Stdcall]<nint, char*, nint, nint*, int>)V(_dwFactory)[7])(_dwFactory, pp, 0, &file);   // CreateFontFileReference
                if (hr < 0) continue;
                hr = ((delegate* unmanaged[Stdcall]<nint, nint, int>)V(builder)[7])(builder, file);   // AddFontFile
                if (hr >= 0) added++;
                Release(file);
            }
            nint set;
            hr = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)V(builder)[6])(builder, &set);   // CreateFontSet
            Release(builder);
            if (hr < 0) { _dwStatus = $"FontSet 실패 0x{hr:X8}"; return; }
            nint coll;
            hr = ((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)V(_dwFactory)[37])(_dwFactory, set, &coll);   // CreateFontCollectionFromFontSet
            Release(set);
            if (hr < 0) { _dwStatus = $"FontCollection 실패 0x{hr:X8}"; return; }
            _dwCollection = coll;

            // 확인: "Pretendard" 가족이 있고, 600 을 고르면 합성 없이 SemiBold 파일이 잡히는지
            uint idx; int exists;
            fixed (char* fam = "Pretendard") hr = ((delegate* unmanaged[Stdcall]<nint, char*, uint*, int*, int>)V(coll)[5])(coll, fam, &idx, &exists);   // FindFamilyName
            if (hr < 0 || exists == 0) { _dwStatus = $"파일 {added}개, 'Pretendard' 가족 없음"; return; }
            nint family, font;
            ((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)V(coll)[4])(coll, idx, &family);   // GetFontFamily
            string weights = "";
            foreach (int want in new[] { 600, 700 })
            {
                ((delegate* unmanaged[Stdcall]<nint, int, int, int, nint*, int>)V(family)[7])(family, want, 5, 0, &font);   // GetFirstMatchingFont
                int w = ((delegate* unmanaged[Stdcall]<nint, int>)V(font)[4])(font);   // GetWeight
                int sim = ((delegate* unmanaged[Stdcall]<nint, int>)V(font)[10])(font);   // GetSimulations
                weights += $" · {want}→{w}" + (sim == 0 ? "" : $" 합성{sim}");
                Release(font);
            }
            uint count = ((delegate* unmanaged[Stdcall]<nint, uint>)V(family)[4])(family);   // GetFontCount
            Release(family);
            _dwStatus = $"파일 {added}개 · Pretendard {count}종{weights}";
        }
        catch (Exception e) { _dwStatus = "오류: " + e.GetType().Name; }
    }

    private static nint DwFormat(int px, bool semi, bool wrap)
    {
        nint fmt;
        int hr;
        fixed (char* fam = "Pretendard") fixed (char* loc = "ko-kr")
            hr = ((delegate* unmanaged[Stdcall]<nint, char*, nint, int, int, int, float, char*, nint*, int>)V(_dwFactory)[15])
                (_dwFactory, fam, _dwCollection, semi ? _strong : 400, 0, 5, px, loc, &fmt);   // CreateTextFormat (크기는 DIP, 렌더 타깃 96dpi 라 = 물리 px)
        if (hr < 0) return 0;
        ((delegate* unmanaged[Stdcall]<nint, int, int>)V(fmt)[4])(fmt, wrap ? 0 : 2);   // SetParagraphAlignment: NEAR / CENTER
        ((delegate* unmanaged[Stdcall]<nint, int, int>)V(fmt)[5])(fmt, wrap ? 0 : 1);   // SetWordWrapping: WRAP / NO_WRAP
        return fmt;
    }

    // ---------------------------------------------------------------- 그리기
    private static void Paint(nint hwnd)
    {
        nint hdc = BeginPaint(hwnd, out PAINTSTRUCT ps);
        GetClientRect(hwnd, out RECT rc);
        nint mem = CreateCompatibleDC(hdc), bmp = CreateCompatibleBitmap(hdc, rc.right, rc.bottom), oldBmp = SelectObject(mem, bmp);
        try
        {
            Fill(mem, rc, WindowBg);
            SetBkMode(mem, 1);

            // 머리말
            nint hTitle = MakeFont("Malgun Gothic", S(16), 700), hSmall = MakeFont("Malgun Gothic", S(12), 400);
            nint old = SelectObject(mem, hTitle);
            DrawStr(mem, "글꼴 비교 시안 — 1Key 에는 아무 영향 없음", Text, S(Margin), S(10), S(ClientW - Margin), S(34), 0x24);
            SelectObject(mem, hSmall);
            string aa = _aaMode == 1 ? "ClearType" : "회색조";
            string ms = _measure switch { 1 => "GDI classic", 2 => "GDI natural", _ => "Natural" };
            DrawStr(mem, "T 밝게/어둡게 · W ②③ 굵은 글자 600/700 · A ③ 안티앨리어싱 · M ③ 측정 방식 · Esc 닫기 · 다른 모니터로 옮기면 그 배율로 다시 그림", Secondary, S(Margin), S(38), S(ClientW - Margin), S(58), 0x24);
            DrawStr(mem, $"배율 {_dpi * 100 / 96}% · {(_dark ? "어둡게" : "밝게")} · ②③ 굵은 글자 {(_strong == 700 ? "Bold 700" : "SemiBold 600")} · ③ 안티앨리어싱 {aa} · ③ 측정 {ms}", Secondary, S(Margin), S(58), S(ClientW - Margin), S(78), 0x24);
            DrawStr(mem, $"② GDI Pretendard: {(_gdiLoaded ? "불러옴" : "파일 없음")} ({_gdiFaceRegular} / {_gdiFaceSemi}) · ③ DirectWrite: {_dwStatus}", Secondary, S(Margin), S(78), S(ClientW - Margin), S(98), 0x24);
            SelectObject(mem, old);
            DeleteObject(hTitle);

            string[] heads = { "① 맑은 고딕 · GDI (지금 1Key)", "② Pretendard · GDI", "③ Pretendard · DirectWrite" };
            for (int c = 0; c < 3; c++)
            {
                int x0 = S(Margin + c * (ColW + Gap));
                old = SelectObject(mem, hSmall);
                DrawStr(mem, heads[c], Secondary, x0 + S(4), S(HeaderH), x0 + S(ColW), S(HeaderH + ColHeadH - 6), 0x24);
                SelectObject(mem, old);
                Fill(mem, new RECT { left = x0, top = S(HeaderH + ColHeadH), right = x0 + S(ColW), bottom = S(ClientH - Margin) }, CardBg);
            }
            DeleteObject(hSmall);

            // ①② GDI, 그리고 버튼 바탕(세 칸 모두)
            for (int c = 0; c < 3; c++)
            {
                int x0 = S(Margin + c * (ColW + Gap) + Pad), x1 = S(Margin + c * (ColW + Gap) + ColW - Pad), y = S(HeaderH + ColHeadH + Pad);
                foreach (var s in Samples)
                {
                    int h = S(RowH(s));
                    RECT r = RowRect(s, x0, x1, y, h);
                    if (s.Button) Fill(mem, r, Accent);
                    if (c < 2)
                    {
                        nint f = GdiFont(c, S(s.Size), s.Semi);
                        old = SelectObject(mem, f);
                        uint fmt = s.Lines > 1 ? 0x10u /* WORDBREAK */ : (0x20u /* SINGLELINE */ | 0x04u /* VCENTER */ | (s.Button ? 0x01u : 0u) /* CENTER */);
                        DrawStr(mem, s.S, InkColor(s.Ink), r.left, r.top, r.right, r.bottom, fmt | 0x800 /* NOPREFIX */);
                        SelectObject(mem, old);
                        DeleteObject(f);
                    }
                    y += h;
                }
            }

            // ③ DirectWrite (Direct2D DC 렌더 타깃을 같은 메모리 DC 에 묶는다)
            if (_d2dFactory != 0 && _dwCollection != 0) DrawDirectWrite(mem, rc);
            BitBlt(hdc, 0, 0, rc.right, rc.bottom, mem, 0, 0, 0x00CC0020);
        }
        finally
        {
            SelectObject(mem, oldBmp); DeleteObject(bmp); DeleteDC(mem);
            EndPaint(hwnd, ref ps);
        }
    }

    private static RECT RowRect(Sample s, int x0, int x1, int y, int h)
    {
        if (!s.Button) return new RECT { left = x0, top = y, right = x1, bottom = y + h };
        int bw = S(80), bh = S(34), top = y + (h - bh) / 2;
        return new RECT { left = x0, top = top, right = x0 + bw, bottom = top + bh };
    }

    private static void DrawDirectWrite(nint mem, RECT rc)
    {
        var props = new D2D1_RENDER_TARGET_PROPERTIES { pixelFormat = new D2D1_PIXEL_FORMAT { format = 87 /* B8G8R8A8_UNORM */, alphaMode = 3 /* IGNORE */ }, dpiX = 96, dpiY = 96 };
        nint rt;
        if (((delegate* unmanaged[Stdcall]<nint, D2D1_RENDER_TARGET_PROPERTIES*, nint*, int>)V(_d2dFactory)[16])(_d2dFactory, &props, &rt) < 0) return;   // CreateDCRenderTarget
        try
        {
            if (((delegate* unmanaged[Stdcall]<nint, nint, RECT*, int>)V(rt)[57])(rt, mem, &rc) < 0) return;   // BindDC
            ((delegate* unmanaged[Stdcall]<nint, void>)V(rt)[48])(rt);   // BeginDraw
            ((delegate* unmanaged[Stdcall]<nint, int, void>)V(rt)[34])(rt, _aaMode);   // SetTextAntialiasMode
            int c = 2;
            int x0 = S(Margin + c * (ColW + Gap) + Pad), x1 = S(Margin + c * (ColW + Gap) + ColW - Pad), y = S(HeaderH + ColHeadH + Pad);
            foreach (var s in Samples)
            {
                int h = S(RowH(s));
                RECT r = RowRect(s, x0, x1, y, h);
                nint fmt = DwFormat(S(s.Size), s.Semi, s.Lines > 1);
                if (fmt != 0)
                {
                    if (s.Button) ((delegate* unmanaged[Stdcall]<nint, int, int>)V(fmt)[3])(fmt, 2);   // SetTextAlignment CENTER
                    uint col = InkColor(s.Ink);
                    var color = new D2D1_COLOR_F { r = (col & 0xFF) / 255f, g = ((col >> 8) & 0xFF) / 255f, b = ((col >> 16) & 0xFF) / 255f, a = 1 };
                    nint brush;
                    if (((delegate* unmanaged[Stdcall]<nint, D2D1_COLOR_F*, nint, nint*, int>)V(rt)[8])(rt, &color, 0, &brush) >= 0)   // CreateSolidColorBrush
                    {
                        var lr = new D2D1_RECT_F { left = r.left, top = r.top, right = r.right, bottom = r.bottom };
                        fixed (char* t = s.S)
                            ((delegate* unmanaged[Stdcall]<nint, char*, uint, nint, D2D1_RECT_F*, nint, int, int, void>)V(rt)[27])(rt, t, (uint)s.S.Length, fmt, &lr, brush, 0, _measure);   // DrawText
                        Release(brush);
                    }
                    Release(fmt);
                }
                y += h;
            }
            ulong t1, t2;
            ((delegate* unmanaged[Stdcall]<nint, ulong*, ulong*, int>)V(rt)[49])(rt, &t1, &t2);   // EndDraw
        }
        finally { Release(rt); }
    }

    private static void Fill(nint hdc, RECT r, uint color) { nint b = CreateSolidBrush(color); FillRect(hdc, ref r, b); DeleteObject(b); }

    private static void DrawStr(nint hdc, string s, uint color, int l, int t, int r, int b, uint fmt)
    {
        SetTextColor(hdc, color);
        var rc = new RECT { left = l, top = t, right = r, bottom = b };
        fixed (char* p = s) DrawTextW(hdc, p, s.Length, ref rc, fmt);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            switch (msg)
            {
                case 0x0014: return 1;   // WM_ERASEBKGND: 다시 그릴 때 깜박이지 않게
                case 0x000F: Paint(hwnd); return 0;   // WM_PAINT
                case 0x0100:   // WM_KEYDOWN
                    switch ((int)wParam)
                    {
                        case 0x54: SetTheme(!_dark); ApplyTitleBar(); break;          // T
                        case 0x41: _aaMode = _aaMode == 1 ? 2 : 1; break;             // A
                        case 0x4D: _measure = (_measure + 1) % 3; break;              // M
                        case 0x57: _strong = _strong == 600 ? 700 : 600; break;      // W
                        case 0x1B: DestroyWindow(hwnd); return 0;                     // Esc
                    }
                    InvalidateRect(hwnd, 0, false);
                    return 0;
                case 0x02E0:   // WM_DPICHANGED
                {
                    _dpi = (uint)(wParam & 0xFFFF);
                    RECT* r = (RECT*)lParam;
                    SetWindowPos(hwnd, 0, r->left, r->top, r->right - r->left, r->bottom - r->top, 0x0004 | 0x0010);
                    InvalidateRect(hwnd, 0, false);
                    return 0;
                }
                case 0x0002: PostQuitMessage(0); return 0;   // WM_DESTROY
            }
        }
        catch { }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private static int ReadAppsUseLightTheme()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v ? v : 1;
        }
        catch { return 1; }
    }

    // ---------------------------------------------------------------- Win32
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] private struct MSG { public nint hwnd; public uint message; public nint wParam, lParam; public uint time; public POINT pt; }
    [StructLayout(LayoutKind.Sequential)] private struct PAINTSTRUCT { public nint hdc; public int fErase; public RECT rcPaint; public int fRestore, fIncUpdate; public fixed byte rgbReserved[32]; }
    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEXW
    {
        public uint cbSize, style; public delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint> lpfnWndProc; public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground; public char* lpszMenuName, lpszClassName; public nint hIconSm;
    }
    [StructLayout(LayoutKind.Sequential)] private struct D2D1_PIXEL_FORMAT { public uint format; public int alphaMode; }
    [StructLayout(LayoutKind.Sequential)] private struct D2D1_RENDER_TARGET_PROPERTIES { public int type; public D2D1_PIXEL_FORMAT pixelFormat; public float dpiX, dpiY; public int usage, minLevel; }
    [StructLayout(LayoutKind.Sequential)] private struct D2D1_COLOR_F { public float r, g, b, a; }
    [StructLayout(LayoutKind.Sequential)] private struct D2D1_RECT_F { public float left, top, right, bottom; }

    [DllImport("user32.dll")] private static extern nint SetProcessDpiAwarenessContext(nint value);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool AdjustWindowRectExForDpi(ref RECT rc, uint style, bool menu, uint exStyle, uint dpi);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? name);
    [DllImport("user32.dll")] private static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);
    [DllImport("user32.dll")] private static extern nint CreateWindowExW(uint ex, char* cls, char* title, uint style, int x, int y, int w, int h, nint parent, nint menu, nint inst, nint param);
    [DllImport("user32.dll")] private static extern nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern int GetMessageW(out MSG msg, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern nint DispatchMessageW(ref MSG msg);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(nint hwnd, nint rect, bool erase);
    [DllImport("user32.dll")] private static extern nint LoadCursorW(nint inst, nint name);
    [DllImport("user32.dll")] private static extern nint BeginPaint(nint hwnd, out PAINTSTRUCT ps);
    [DllImport("user32.dll")] private static extern bool EndPaint(nint hwnd, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out RECT rc);
    [DllImport("user32.dll")] private static extern int FillRect(nint hdc, ref RECT rc, nint brush);
    [DllImport("user32.dll")] private static extern int DrawTextW(nint hdc, char* s, int n, ref RECT rc, uint fmt);
    [DllImport("gdi32.dll")] private static extern nint CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] private static extern nint CreateFontW(int h, int w, int esc, int orient, int weight, uint italic, uint underline, uint strike, uint charset, uint outPrec, uint clipPrec, uint quality, uint pitch, char* face);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint hdc, nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(nint hdc, uint color);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(nint hdc, int mode);
    [DllImport("gdi32.dll")] private static extern int GetTextFaceW(nint hdc, int n, char* buf);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint hdc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleBitmap(nint hdc, int w, int h);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint hdc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(nint dst, int x, int y, int w, int h, nint src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] private static extern int AddFontResourceExW(char* path, uint fl, nint res);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attr, void* value, int size);
    [DllImport("d2d1.dll")] private static extern int D2D1CreateFactory(int type, ref Guid riid, nint options, out nint factory);
    [DllImport("dwrite.dll")] private static extern int DWriteCreateFactory(int type, ref Guid riid, out nint factory);
}
