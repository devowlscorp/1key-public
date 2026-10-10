using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 작업 표시줄 고양이의 오른쪽 클릭 메뉴 판(0.5.16-C, 2026-10-09 사용자: Windows 메뉴가 고양이를 가린다 — 고양이 위쪽으로, 메인 화면처럼 입체적으로).
/// 고양이 머리 위에 뜨는 떠 있는 금속 판(<see cref="Metal.Plate"/> — 잠금 위젯 말풍선·알림과 같은 판)에 아래로 고양이를 가리키는 꼬리가 달린다.
/// 층 창(픽셀별 알파)이라 판의 그늘이 작업 표시줄·바탕 위에 비친다.
/// 0.5.18-B(2026-10-10 사용자: 글 목록 대신 격자 — 입체적으로, 짧은 이름, 아래는 2×2): 항목은 메인 화면 목록의 조각(<see cref="Metal.Tile"/>)처럼 도드라진 버튼.
/// 구분선(Id 0)으로 나뉜 묶음마다 3개면 한 줄 3칸(쓰담 · 츄르 · 놀이), 아니면 2칸씩(열기 · 잠금 / 설정 · 숨기기). 올리면 조금 밝게, 누르면 눌린 조각.
/// 쓰는 법은 Windows 메뉴와 같다: 바깥을 누르거나 Esc·다른 창으로 가면 닫히고, 화살표로 고르고 Enter·Space 로 실행.
/// 위에 자리가 없으면(작업 표시줄이 위) 고양이 아래에 꼬리를 위로 해서 뜬다. 열려 있는 동안 고양이는 새 동작(걷기·숨기)을 시작하지 않는다(<see cref="CatWidget.Hold"/>).
/// 0.5.21-G(2026-10-10 사용자: 열기 · 잠금 · 설정 · 숨기기는 아이콘으로, 올리면 설명 — 한 줄로): 아이콘이 있는 항목만으로 된 묶음은 한 줄에 모두(아이콘 버튼),
/// 올리거나 화살표로 고르면 판 바로 아래에 이름 딱지(풍선 도움말처럼).
/// 창 이름 = 항목 이름을 줄바꿈으로 이은 것(화면 읽기·시험이 읽는다).
/// </summary>
internal static unsafe class CatMenu
{
    public const string ClassName = "OneKeyCatMenu";
    /// <summary>항목. Id 0 = 묶음 나눔. Note = 이름 아래 작은 글(키우기 점수까지 남은 시간 "♡ 2분"). Icon = Segoe Fluent Icons 글자(있으면 아이콘 버튼 — 이름은 올렸을 때 딱지로).</summary>
    public readonly record struct Item(int Id, string Text, string Note = "", string Icon = "");

    // 논리 px
    // 0.5.19-I(2026-10-10 사용자: 버튼과 판의 가로 여백이 커서 타이트하게): 판 여백 10 → 8, 칸 사이 6 → 5, 칸 안 좌우 14 → 10, 칸 폭은 묶음마다 따로
    private const double Pad = 8, Radius = 16, TailW = 18, TailH = 9, Gap = 3, FontPx = 13.5;
    private const double CellH = 36, CellNoteH = 46, CellGap = 5, GroupGap = 8, CellPadX = 10, MinInner = 150;
    private const double IconPx = 16, IconCellW = 34, TipH = 22, TipGap = 4;   // 아이콘 버튼 · 이름 딱지(논리 px)
    private const double MarX = 30, MarTop = 16, MarBot = 46;   // 판 그늘(0 12px 26px)이 들어갈 자리
    private const int AnimMs = 130, Rise = 6;
    private const double NoteScale = 0.85;   // 오른쪽 작은 글 크기(본문 대비)
    private const nuint TimerAnim = 1, TimerWatch = 2;

    private static bool _registered;
    private static nint _hwnd, _mem, _dib, _old, _bits, _fg0;
    private static int _cw, _ch, _x, _y, _hot = -1, _dpi = 96;
    private static bool _pressed, _dark, _tailUp, _closing, _wasFg;
    private static double _k = 1, _px, _py, _pw, _ph, _tailX;
    private static (double X, double Y, double W, double H)[] _rects = Array.Empty<(double, double, double, double)>();   // 판 안 자리(판 왼쪽 위 기준)
    private static Item[] _items = Array.Empty<Item>();
    private static string[] _labels = Array.Empty<string>();
    private static char[] _mnemonic = Array.Empty<char>();
    private static Action<int>? _onPick;
    private static string _title = "";
    private const double TitleH = 30;
    private static long _animStart;
    private static nint _lastMove = -1;
    private static bool _downSeen = true;   // 연 순간 눌려 있던 단추(오른쪽 클릭의 떼기 전)는 바깥 클릭으로 세지 않는다

    /// <summary>1Key 자신의 창(본창·고양이·하트 등)이거나 작업 표시줄이면 true — 앞에 와도 메뉴를 닫지 않는다.</summary>
    private static bool OursOrTaskbar(nint h)
    {
        GetWindowThreadProcessId(h, out uint pid);
        if (pid == (uint)Environment.ProcessId) return true;
        return Native.GetClassName(h) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    private static bool _still;

    public static bool IsOpen => _hwnd != 0;

    /// <summary>
    /// anchor = 고양이 창(화면 좌표). 고르면 onPick(Id), 그냥 닫히면 부르지 않는다. dpi = 고양이가 쓰는 배율.
    /// 못 띄우면 false(부르는 쪽이 Windows 메뉴로).
    /// </summary>
    public static bool Show(Item[] items, Native.RECT anchor, int dpi, bool dark, Action<int> onPick, string title = "")
    {
        Close("reopen");
        CatHearts.Close();   // 하트가 메뉴 판 위에 겹쳐 글이 가렸다(2026-10-10 사용자) — 메뉴가 열리면 하트는 닫고, 열려 있는 동안 새 하트는 미룬다(CatWidget)
        if (items.Length == 0) return false;
        Gdiplus.Init();
        nint hInst = Native.GetModuleHandleW(null);
        if (!_registered) { Ctl.RegisterClass(hInst, ClassName, &WndProc, 0); _registered = true; }
        _items = items; _onPick = onPick; _title = (title ?? "").Trim(); _dark = dark; _dpi = dpi is >= 48 and <= 480 ? dpi : 96; _k = _dpi / 96.0;
        _hot = -1; _pressed = false; _closing = false; _wasFg = false; _lastMove = -1; _downSeen = true;
        _labels = new string[items.Length]; _mnemonic = new char[items.Length];
        for (int i = 0; i < items.Length; i++) (_labels[i], _mnemonic[i]) = Strip(items[i].Text);

        // 크기: 칸 폭 = 모든 칸에서 가장 긴 글(이름 · 아래 작은 글) + 양옆 여백. 묶음마다 판 안 폭을 같게 나눈다
        double k = _k, titleW = 0;
        var need = new double[items.Length];   // 칸마다 글 폭 + 좌우 여백(묶음의 칸 폭 = 그 묶음에서 가장 넓은 것)
        if (!Fonts(out nint fam, out nint font, out nint fmt)) return false;
        try
        {
            nint dc = Native.GetDC(0);
            GdipCreateFromHDC(dc, out nint g);
            for (int i = 0; i < _labels.Length; i++)
            {
                if (items[i].Id == 0) continue;
                if (items[i].Icon.Length > 0) { need[i] = IconCellW * k; continue; }   // 아이콘 버튼: 글 폭과 상관없이
                double w = Measure(g, _labels[i], font, fmt) * 1.06;   // 버튼 글은 굵게(약간 넓다)
                if (items[i].Note.Length > 0) w = Math.Max(w, Measure(g, items[i].Note, font, fmt) * NoteScale);
                need[i] = w + 2 * CellPadX * k;
            }
            if (_title.Length > 0) titleW = Measure(g, "♥ " + _title, font, fmt) * 1.1 + 24 * k;   // 굵은 글 몫
            GdipDeleteGraphics(g); Native.ReleaseDC(0, dc);
        }
        finally { FreeFonts(fam, font, fmt); }
        var groups = new List<List<int>> { new() };
        for (int i = 0; i < items.Length; i++) { if (items[i].Id == 0) { if (groups[^1].Count > 0) groups.Add(new()); } else groups[^1].Add(i); }
        if (groups[^1].Count == 0) groups.RemoveAt(groups.Count - 1);
        int Cols(List<int> grp) => grp.All(i => items[i].Icon.Length > 0) ? grp.Count : grp.Count == 3 ? 3 : Math.Min(2, grp.Count);   // 아이콘 묶음은 한 줄
        double inner = Math.Max(MinInner * k, titleW), gap = CellGap * k;
        foreach (var grp in groups) inner = Math.Max(inner, Cols(grp) * grp.Max(i => need[i]) + (Cols(grp) - 1) * gap);
        inner = Math.Ceiling(inner);
        _pw = inner + 2 * Pad * k;
        double h = Pad * k + (_title.Length > 0 ? (TitleH + 6) * k : 0);   // 맨 위 고양이 이름 줄(설정에서 지은 이름 — 고를 수 없는 줄) + 구분선 아래 여백
        _rects = new (double, double, double, double)[items.Length];
        for (int gi = 0; gi < groups.Count; gi++)
        {
            var grp = groups[gi];
            int cols = Cols(grp), rows = (grp.Count + cols - 1) / cols;
            double cw = (inner - (cols - 1) * gap) / cols, rh = (grp.Any(i => items[i].Note.Length > 0) ? CellNoteH : CellH) * k;
            for (int j = 0; j < grp.Count; j++)
                _rects[grp[j]] = (Pad * k + (j % cols) * (cw + gap), h + (j / cols) * (rh + gap), cw, rh);
            h += rows * rh + (rows - 1) * gap + (gi < groups.Count - 1 ? GroupGap * k : 0);
        }
        _ph = Math.Ceiling(h + Pad * k);
        _cw = (int)Math.Ceiling(_pw + 2 * MarX * k);
        _ch = (int)Math.Ceiling(_ph + (MarTop + MarBot + TailH) * k);

        // 자리: 고양이 머리 위 가운데(꼬리 끝이 머리 바로 위). 위에 자리가 없으면 아래
        var cpt = new Native.POINT { x = (anchor.left + anchor.right) / 2, y = (anchor.top + anchor.bottom) / 2 };
        var mi = new Native.MONITORINFO { cbSize = (uint)sizeof(Native.MONITORINFO) };
        Native.RECT mon = Native.GetMonitorInfoW(Native.MonitorFromPoint(cpt, 2 /* NEAREST */), ref mi) ? mi.rcMonitor : anchor;
        double tail = TailH * k;
        _tailUp = anchor.top - Gap * k - tail - _ph < mon.top;
        _px = MarX * k;
        _py = _tailUp ? MarTop * k + tail : MarTop * k;
        double plateLeft = Math.Clamp(cpt.x - _pw / 2, mon.left + 4 * k, Math.Max(mon.left + 4 * k, mon.right - 4 * k - _pw));
        _x = (int)Math.Round(plateLeft - _px);
        _y = _tailUp ? (int)Math.Round(anchor.bottom + Gap * k - (_py - tail)) : (int)Math.Round(anchor.top - Gap * k - tail - _ph - _py);
        double edge = (Radius + TailW / 2 + 2) * k;
        _tailX = Math.Clamp(cpt.x - _x, _px + edge, _px + _pw - edge);

        // 그림판
        var bih = new BIH { biSize = (uint)sizeof(BIH), biWidth = _cw, biHeight = -_ch, biPlanes = 1, biBitCount = 32 };
        nint screen = Native.GetDC(0);
        _mem = Fx.CreateCompatibleDC(screen);
        Native.ReleaseDC(0, screen);
        _dib = CreateDIBSection(_mem, ref bih, 0, out _bits, 0, 0);
        if (_mem == 0 || _dib == 0 || _bits == 0) { FreeCanvas(); return false; }
        _old = Native.SelectObject(_mem, _dib);

        string cap = (_title.Length > 0 ? _title + "\n" : "") + string.Join("\n", _labels.Where((s, i) => _items[i].Id != 0));
        fixed (char* cls = ClassName) fixed (char* c = cap)
            _hwnd = Native.CreateWindowExW(Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST | Fx.WS_EX_LAYERED, cls, c, Native.WS_POPUP,
                _x, _y, _cw, _ch, 0, 0, hInst, 0);
        if (_hwnd == 0) { FreeCanvas(); return false; }
        CatWidget.Hold = true;
        _still = !Fx.Animations;
        _animStart = Environment.TickCount64;
        if (!Draw() || !Push(_still ? 1 : 0)) { Close("draw failed"); return false; }
        Dump();
        Native.ShowWindow(_hwnd, 5 /* SW_SHOW */);
        _fg0 = Native.GetForegroundWindow();
        Native.SetForegroundWindow(_hwnd);
        Native.SetFocus(_hwnd);
        Native.SetCapture(_hwnd);
        if (!_still) Native.SetTimer(_hwnd, TimerAnim, 15, 0);
        Native.SetTimer(_hwnd, TimerWatch, 50, 0);
        return true;
    }

    /// <summary>닫는다(고르지 않음). 고양이가 숨거나 앱이 잠글 때도 부른다.</summary>
    public static void Close() => Close("api");

    private static void Close(string why)
    {
        if (_hwnd == 0) { FreeCanvas(); return; }
        CatWidget.TestLog($"catmenu close {why}");
        nint h = _hwnd;
        _closing = true;
        _hwnd = 0;
        _onPick = null;
        if (Native.GetCapture() == h) Native.ReleaseCapture();
        Native.DestroyWindow(h);
        FreeCanvas();
        CatWidget.Hold = false;
    }

    private static void Pick(int idx)
    {
        if (idx < 0 || idx >= _items.Length || _items[idx].Id == 0) return;
        var cb = _onPick; int id = _items[idx].Id;
        Close("pick");
        cb?.Invoke(id);
    }

    /// <summary>"열기(&amp;O)" → ("열기", 'O'), "&amp;Open" → ("Open", 'O').</summary>
    private static (string, char) Strip(string s)
    {
        int a = s.IndexOf('&');
        if (a < 0 || a + 1 >= s.Length) return (s, '\0');
        char m = char.ToUpperInvariant(s[a + 1]);
        if (a > 0 && s[a - 1] == '(' && a + 2 < s.Length && s[a + 2] == ')') return (s.Remove(a - 1, 4).TrimEnd(), m);
        return (s.Remove(a, 1), m);
    }

    // ------------------------------------------------------------------ 그리기

    private static bool Fonts(out nint fam, out nint font, out nint fmt)
    {
        fam = font = fmt = 0;
        string face = L.Current switch { Lang.Ja => "Yu Gothic UI", Lang.ZhHans => "Microsoft YaHei UI", Lang.En or Lang.Vi => "Segoe UI", _ => "Malgun Gothic" };
        fixed (char* f = face) if (GdipCreateFontFamilyFromName(f, 0, out fam) != 0 || fam == 0) return false;
        GdipCreateFont(fam, (float)(FontPx * _k), 0, 2 /* UnitPixel */, out font);
        GdipCreateStringFormat(0x00001000 /* NoWrap */, 0, out fmt);
        return font != 0 && fmt != 0;
    }

    private static void FreeFonts(nint fam, nint font, nint fmt)
    {
        if (fmt != 0) GdipDeleteStringFormat(fmt);
        if (font != 0) GdipDeleteFont(font);
        if (fam != 0) GdipDeleteFontFamily(fam);
    }

    private static double Measure(nint g, string text, nint font, nint fmt)
    {
        var rc = new RECTF { Width = 10000, Height = 1000 };
        RECTF bound;
        fixed (char* p = text) GdipMeasureString(g, p, text.Length, font, ref rc, fmt, out bound, out _, out _);
        return bound.Width;
    }

    private static bool Draw()
    {
        if (_bits == 0) return false;
        double k = _k;
        bool d = _dark;
        var surf = new Metal.Surf((uint*)_bits, _cw, _ch);
        new Span<uint>((void*)_bits, _cw * _ch).Clear();
        bool prem = Metal.Premul;
        Metal.Premul = true;
        try
        {
            Metal.Plate(surf, _px, _py, _pw, _ph, Radius * k, k, d, true);
            Tail(surf, k, d);
            if (_title.Length > 0) Metal.Sep(surf, _px + 12 * k, Math.Round(_py + (Pad + TitleH) * k - k), _pw - 24 * k, k, d);
            for (int i = 0; i < _items.Length; i++)
            {
                if (_items[i].Id == 0) continue;
                var r = _rects[i];
                int st = i != _hot ? 0 : _pressed ? 2 : 1;   // 보통 · 올림(조금 밝게) · 누름
                Metal.Tile(surf, _px + r.X, _py + r.Y, r.W, r.H, k, d, st);
            }
        }
        finally { Metal.Premul = prem; }
        Metal.GdiFlush();

        // 글(GDI+, 미리 곱한 알파 그림 위 — 글 테두리만 안티앨리어싱)
        if (GdipCreateBitmapFromScan0(_cw, _ch, _cw * 4, 0x000E200B /* PARGB */, _bits, out nint bmp) != 0 || bmp == 0) return false;
        nint g = 0, fam = 0, font = 0, fmt = 0, brush = 0;
        try
        {
            if (GdipGetImageGraphicsContext(bmp, out g) != 0) return false;
            GdipSetTextRenderingHint(g, 4 /* AntiAlias */);
            if (!Fonts(out fam, out font, out fmt)) return false;
            GdipSetStringFormatLineAlign(fmt, 1 /* Center */);
            GdipCreateSolidFill(0xFF000000u | Metal.Ink(d), out brush);
            if (_title.Length > 0) DrawTitle(g, d);
            GdipSetStringFormatAlign(fmt, 1 /* Center */);
            nint bold = 0;
            GdipCreateFont(fam, (float)(FontPx * k), 1 /* Bold */, 2, out bold);
            for (int i = 0; i < _items.Length; i++)
            {
                if (_items[i].Id == 0) continue;
                if (_items[i].Icon.Length > 0) continue;   // 아이콘은 아래에서(아이콘 글꼴)
                var r = _rects[i];
                double dy = _pressed && i == _hot ? 1 * k : 0, noteH = _items[i].Note.Length > 0 ? 14 * k : 0;
                var rc = new RECTF { X = (float)(_px + r.X), Y = (float)(_py + r.Y + dy), Width = (float)r.W, Height = (float)(r.H - noteH) };
                if (noteH > 0) rc.Y += (float)(3 * k);
                fixed (char* p = _labels[i]) GdipDrawString(g, p, _labels[i].Length, bold != 0 ? bold : font, ref rc, fmt, brush);
                if (noteH > 0)
                {
                    var rn = new RECTF { X = rc.X, Y = (float)(_py + r.Y + dy + r.H - noteH - 6 * k), Width = rc.Width, Height = (float)noteH };
                    DrawNote(g, _items[i].Note, rn, d);
                }
            }
            if (bold != 0) GdipDeleteFont(bold);
            DrawIcons(g, brush);
            return true;
        }
        finally
        {
            if (brush != 0) GdipDeleteBrush(brush);
            FreeFonts(fam, font, fmt);
            if (g != 0) GdipDeleteGraphics(g);
            GdipDisposeImage(bmp);
        }
    }

    /// <summary>아이콘 버튼의 글자(Segoe Fluent Icons, 없으면 Segoe MDL2 Assets — 같은 코드 자리), 그리고 고른 아이콘의 이름 딱지(판 바로 아래).</summary>
    private static void DrawIcons(nint g, nint brush)
    {
        if (!_items.Any(it => it.Icon.Length > 0)) return;
        nint fam = 0, font = 0, fmt = 0;
        try
        {
            fixed (char* f = "Segoe Fluent Icons") if (GdipCreateFontFamilyFromName(f, 0, out fam) != 0 || fam == 0)
                fixed (char* f2 = "Segoe MDL2 Assets") GdipCreateFontFamilyFromName(f2, 0, out fam);
            if (fam == 0) return;
            GdipCreateFont(fam, (float)(IconPx * _k), 0, 2, out font);
            GdipCreateStringFormat(0x00001000, 0, out fmt);
            GdipSetStringFormatAlign(fmt, 1); GdipSetStringFormatLineAlign(fmt, 1);
            for (int i = 0; i < _items.Length; i++)
            {
                if (_items[i].Id == 0 || _items[i].Icon.Length == 0) continue;
                var r = _rects[i];
                double dy = _pressed && i == _hot ? 1 * _k : 0;
                var rc = new RECTF { X = (float)(_px + r.X), Y = (float)(_py + r.Y + dy), Width = (float)r.W, Height = (float)r.H };
                fixed (char* p = _items[i].Icon) GdipDrawString(g, p, _items[i].Icon.Length, font, ref rc, fmt, brush);
            }
        }
        finally
        {
            if (fmt != 0) GdipDeleteStringFormat(fmt);
            if (font != 0) GdipDeleteFont(font);
            if (fam != 0) GdipDeleteFontFamily(fam);
        }
        if (_hot >= 0 && _items[_hot].Icon.Length > 0) DrawTip(g, _hot);
    }

    /// <summary>
    /// 고른 아이콘 버튼의 이름 딱지: 판 바로 아래(꼬리가 아래면 꼬리 위에 겹쳐 — 풍선 도움말처럼 잠깐), 그 버튼 가운데에. 판 그늘 자리(MarBot) 안이라 창을 키우지 않는다.
    /// </summary>
    private static void DrawTip(nint g, int i)
    {
        if (!Fonts(out nint fam, out nint font, out nint fmt)) { FreeFonts(fam, font, fmt); return; }
        nint bold = 0, brush = 0;
        try
        {
            double k = _k;
            GdipCreateFont(fam, (float)(FontPx * 0.92 * k), 1 /* Bold */, 2, out bold);
            string text = _labels[i];
            double tw = Measure(g, text, bold, fmt) + 16 * k, th = TipH * k;
            var r = _rects[i];
            double cx = _px + r.X + r.W / 2;
            double tx = Math.Clamp(cx - tw / 2, 2 * k, _cw - tw - 2 * k), ty = _py + _ph + TipGap * k;
            var surf = new Metal.Surf((uint*)_bits, _cw, _ch);
            bool prem = Metal.Premul; double op = Metal.Opacity;
            Metal.Premul = true; Metal.Opacity = 1;
            try { Metal.Chip(surf, tx, ty, tw, th, k, _dark); }
            finally { Metal.Premul = prem; Metal.Opacity = op; }
            GdipSetStringFormatAlign(fmt, 1); GdipSetStringFormatLineAlign(fmt, 1);
            GdipCreateSolidFill(0xFF000000u | Metal.Ink(_dark), out brush);
            var rc = new RECTF { X = (float)tx, Y = (float)ty, Width = (float)tw, Height = (float)th };
            fixed (char* p = text) GdipDrawString(g, p, text.Length, bold, ref rc, fmt, brush);
        }
        finally
        {
            if (brush != 0) GdipDeleteBrush(brush);
            if (bold != 0) GdipDeleteFont(bold);
            FreeFonts(fam, font, fmt);
        }
    }

    /// <summary>맨 위 고양이 이름(굵게, 가운데, 이름표처럼 앞에 작은 ♥).</summary>
    private static void DrawTitle(nint g, bool d)
    {
        if (!Fonts(out nint fam, out nint body, out nint fmt)) { FreeFonts(fam, body, fmt); return; }
        nint font = 0, brush = 0, heart = 0;
        try
        {
            GdipCreateFont(fam, (float)(FontPx * _k), 1 /* Bold */, 2, out font);
            GdipSetStringFormatAlign(fmt, 1 /* Center */);
            GdipSetStringFormatLineAlign(fmt, 1);
            GdipCreateSolidFill(0xFF000000u | Metal.Ink(d), out brush);
            GdipCreateSolidFill(0xFF000000u | (d ? 0xFF7A90u : 0xF0607Au), out heart);   // 하트와 같은 분홍(CatHearts)
            // ♥ + 빈칸 + 이름을 가운데에: 너비를 재서 왼쪽부터 두 번 그린다(하트만 분홍)
            GdipSetStringFormatAlign(fmt, 0 /* Near */);
            string hs = "\u2665 ";
            double hw = Measure(g, hs, font, fmt), nw = Measure(g, _title, font, fmt);
            float x0 = (float)(_px + (_pw - hw - nw) / 2), y0 = (float)(_py + Pad * _k);
            var rh = new RECTF { X = x0, Y = y0, Width = (float)hw + 4, Height = (float)(TitleH * _k) };
            fixed (char* p = hs) GdipDrawString(g, p, hs.Length, font, ref rh, fmt, heart);
            var rn = new RECTF { X = x0 + (float)hw, Y = y0, Width = (float)nw + 8, Height = (float)(TitleH * _k) };
            fixed (char* p = _title) GdipDrawString(g, p, _title.Length, font, ref rn, fmt, brush);
        }
        finally
        {
            if (brush != 0) GdipDeleteBrush(brush);
            if (heart != 0) GdipDeleteBrush(heart);
            if (font != 0) GdipDeleteFont(font);
            FreeFonts(fam, body, fmt);
        }
    }

    /// <summary>버튼 이름 아래의 작은 글(옅은 먹색, 본문의 NoteScale 크기, 가운데).</summary>
    private static void DrawNote(nint g, string note, RECTF rc, bool d)
    {
        if (!Fonts(out nint fam, out nint big, out nint fmt)) { FreeFonts(fam, big, fmt); return; }
        nint font = 0, brush = 0;
        try
        {
            GdipCreateFont(fam, (float)(FontPx * NoteScale * _k), 0, 2, out font);
            GdipSetStringFormatAlign(fmt, 1 /* Center */);
            GdipSetStringFormatLineAlign(fmt, 1);
            GdipCreateSolidFill(0xFF000000u | Metal.InkSub(d), out brush);
            fixed (char* p = note) GdipDrawString(g, p, note.Length, font, ref rc, fmt, brush);
        }
        finally
        {
            if (brush != 0) GdipDeleteBrush(brush);
            if (font != 0) GdipDeleteFont(font);
            FreeFonts(fam, big, fmt);
        }
    }

    /// <summary>판에서 고양이 쪽으로 나온 꼬리(삼각형, 판과 같은 152° 색). 판 가장자리 1px 안쪽부터 그려 이음매가 없게.</summary>
    private static void Tail(Metal.Surf s, double k, bool d)
    {
        (double, uint)[] stops = d ? new[] { (0.0, 0x2E2F33u), (0.35, 0x1F2023u), (1.0, 0x121315u) } : new[] { (0.0, 0xFFFFFFu), (0.35, 0xF3F3F3u), (1.0, 0xE4E4E5u) };
        var fill = Metal.LinearAt(_px, _py, _pw, _ph, 152, stops);
        double hw = TailW / 2 * k, th = TailH * k;
        double baseY = _tailUp ? _py + 1.5 * k : _py + _ph - 1.5 * k, tipY = _tailUp ? _py - th : _py + _ph + th;
        // 꼭짓점 a(왼쪽 밑), b(오른쪽 밑), c(끝). 덮임 비율 = 4×4 표본 중 안에 든 수(가장자리 안티앨리어싱)
        double ax = _tailX - hw, bx = _tailX + hw, cx = _tailX;
        int x0 = Math.Max(0, (int)Math.Floor(ax) - 1), x1 = Math.Min(s.W, (int)Math.Ceiling(bx) + 1);
        int y0 = Math.Max(0, (int)Math.Floor(Math.Min(baseY, tipY)) - 1), y1 = Math.Min(s.H, (int)Math.Ceiling(Math.Max(baseY, tipY)) + 1);
        double sh = 1.5 * k;
        // 꼬리 아래 작은 그늘(판 그늘과 같은 아래쪽) 먼저, 그 위에 꼬리
        for (int pass = 0; pass < 2; pass++)
        {
            double oy = pass == 0 ? sh : 0;
            for (int y = y0; y < Math.Min(s.H, y1 + (int)Math.Ceiling(sh)); y++)
                for (int x = x0; x < x1; x++)
                {
                    int n = 0;
                    for (int j = 0; j < 4; j++)
                        for (int i = 0; i < 4; i++)
                            if (InTri(x + (i + 0.5) / 4, y + (j + 0.5) / 4 - oy, ax, baseY, bx, baseY, cx, tipY)) n++;
                    if (n == 0) continue;
                    double cov = n / 16.0;
                    if (pass == 0) Metal.Put(s, x, y, d ? 0x000000u : 0x28282Du, cov * (d ? 0.30 : 0.10));
                    else Metal.Put(s, x, y, fill(x + 0.5, y + 0.5), cov);
                }
        }
    }

    private static bool InTri(double px, double py, double ax, double ay, double bx, double by, double cx, double cy)
    {
        double d1 = (px - bx) * (ay - by) - (ax - bx) * (py - by);
        double d2 = (px - cx) * (by - cy) - (bx - cx) * (py - cy);
        double d3 = (px - ax) * (cy - ay) - (cx - ax) * (py - ay);
        bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }

    private static int _dumpN;

    /// <summary>시험 전용(ONEKEY_TEST_CATMENU_DUMP = 폴더): 그린 판을 그대로(미리 곱한 BGRA) 파일로 — 화면을 찍지 않고 그림을 본다.</summary>
    private static void Dump()
    {
        if (!Program.IsTestMode || Environment.GetEnvironmentVariable("ONEKEY_TEST_CATMENU_DUMP") is not string dir || dir.Length == 0 || _bits == 0) return;
        try
        {
            Directory.CreateDirectory(dir);
            var head = System.Text.Encoding.ASCII.GetBytes($"{_cw} {_ch} {_hot} {(_pressed ? 1 : 0)} {(_dark ? 1 : 0)} {(_tailUp ? 1 : 0)} {_tailX:0.0} {_x} {_y}\n");
            using var f = File.Create(Path.Combine(dir, $"menu{_dumpN++:00}.bgra"));
            f.Write(head);
            f.Write(new ReadOnlySpan<byte>((void*)_bits, _cw * _ch * 4));
        }
        catch { }
    }

    private static bool Push(double t)
    {
        if (_hwnd == 0) return false;
        double e = 1 - (1 - t) * (1 - t);   // 감속
        int dy = (int)Math.Round((1 - e) * Rise * _k) * (_tailUp ? -1 : 1);
        var size = new SIZE { cx = _cw, cy = _ch };
        var src = new Native.POINT();
        var dst = new Native.POINT { x = _x, y = _y + dy };
        var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = (byte)Math.Round(255 * Math.Clamp(e, 0, 1)), AlphaFormat = 1 };
        nint screen = Native.GetDC(0);
        bool ok = UpdateLayeredWindow(_hwnd, screen, ref dst, ref size, _mem, ref src, 0, ref blend, 2 /* ULW_ALPHA */);
        Native.ReleaseDC(0, screen);
        return ok;
    }

    private static void Redraw()
    {
        if (_hwnd == 0) return;
        Draw();
        Dump();
        double t = _still ? 1 : Math.Min(1, (Environment.TickCount64 - _animStart) / (double)AnimMs);
        Push(t);
    }

    private static void FreeCanvas()
    {
        if (_mem != 0 && _old != 0) Native.SelectObject(_mem, _old);
        if (_dib != 0) Native.DeleteObject(_dib);
        if (_mem != 0) Native.DeleteDC(_mem);
        _mem = _dib = _old = _bits = 0;
    }

    // ------------------------------------------------------------------ 입력

    /// <summary>창 좌표 → 항목 번호(구분선·바깥 = -1). 판 밖이면 outside.</summary>
    private static int Hit(int x, int y, out bool outside)
    {
        double k = _k;
        bool inPlate = x >= _px && x < _px + _pw && y >= _py && y < _py + _ph;
        bool inTail = Math.Abs(x - _tailX) <= TailW / 2 * k && (_tailUp ? y >= _py - TailH * k && y < _py : y >= _py + _ph && y < _py + _ph + TailH * k);
        outside = !inPlate && !inTail;
        if (!inPlate) return -1;
        double rx = x - _px, ry = y - _py;
        for (int i = 0; i < _items.Length; i++)
        {
            if (_items[i].Id == 0) continue;
            var r = _rects[i];
            if (rx >= r.X && rx < r.X + r.W && ry >= r.Y && ry < r.Y + r.H) return i;
        }
        return -1;
    }

    /// <summary>↑↓: 위·아래 줄에서 가로 가운데가 가장 가까운 칸(없으면 그대로). 아직 고른 칸이 없으면 첫 칸.</summary>
    private static void MoveVert(int dir)
    {
        if (_hot < 0) { Move(1); return; }
        var h = _rects[_hot];
        double cx = h.X + h.W / 2, cy = h.Y + h.H / 2;
        int best = -1; double bdy = double.MaxValue, bdx = double.MaxValue;
        for (int i = 0; i < _items.Length; i++)
        {
            if (_items[i].Id == 0 || i == _hot) continue;
            var r = _rects[i];
            double dy = (r.Y + r.H / 2 - cy) * dir, dx = Math.Abs(r.X + r.W / 2 - cx);
            if (dy <= 1) continue;
            if (dy < bdy - 1 || (Math.Abs(dy - bdy) <= 1 && dx < bdx)) { best = i; bdy = dy; bdx = dx; }
        }
        if (best >= 0) { _hot = best; _pressed = false; Redraw(); }
    }

    private static void Move(int dir)
    {
        int n = _items.Length;
        if (n == 0) return;
        int i = _hot;
        if (i < 0) i = dir > 0 ? -1 : n;
        for (int step = 0; step < n; step++)
        {
            i = ((i + dir) % n + n) % n;
            if (_items[i].Id != 0) { _hot = i; _pressed = false; Redraw(); return; }
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            if (hwnd != _hwnd) return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
            switch (msg)
            {
                case 0x0200:   // WM_MOUSEMOVE
                {
                    // 마우스가 그대로인데 오는 이동 알림(밑에서 걷는 고양이 창이 움직일 때마다 Windows 가 보낸다)은 무시 — 키보드로 고른 줄이 풀렸다
                    if (lParam == _lastMove) return 0;
                    _lastMove = lParam;
                    int hit = Hit((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF), out _);
                    if (hit != _hot) { _hot = hit; _pressed = false; Redraw(); }
                    return 0;
                }
                case 0x0201: case 0x0204:   // WM_LBUTTONDOWN · WM_RBUTTONDOWN
                {
                    int hit = Hit((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF), out bool outside);
                    if (outside) { Close("outside"); return 0; }
                    _hot = hit; _pressed = hit >= 0; Redraw();
                    return 0;
                }
                case 0x0202: case 0x0205:   // WM_LBUTTONUP · WM_RBUTTONUP
                {
                    int hit = Hit((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF), out _);
                    bool was = _pressed;
                    _pressed = false;
                    if (was && hit >= 0 && hit == _hot) { Pick(hit); return 0; }
                    Redraw();
                    return 0;
                }
                case 0x0100:   // WM_KEYDOWN
                {
                    int vk = (int)wParam;
                    switch (vk)
                    {
                        case 0x1B: Close("esc"); return 0;                       // Esc
                        case 0x26: MoveVert(-1); return 0;                  // ↑
                        case 0x28: MoveVert(1); return 0;                   // ↓
                        case 0x25: Move(-1); return 0;                      // ←
                        case 0x27: Move(1); return 0;                       // →
                        case 0x24: _hot = -1; Move(1); return 0;            // Home
                        case 0x23: _hot = -1; Move(-1); return 0;           // End
                        case 0x0D: case 0x20: if (_hot >= 0) Pick(_hot); return 0;   // Enter · Space
                    }
                    if (vk is >= 0x30 and <= 0x5A)
                        for (int i = 0; i < _items.Length; i++)
                            if (_items[i].Id != 0 && _mnemonic[i] == (char)vk) { Pick(i); return 0; }
                    return 0;
                }
                case 0x0104:   // WM_SYSKEYDOWN(Alt, F10): 메뉴처럼 닫는다
                    Close("syskey");
                    return 0;
                case Native.WM_ACTIVATE:
                    // 비활성이 되어도 바로 닫지 않는다(0.5.17-H): 작업 표시줄을 누른 뒤에는 작업 표시줄이 앞 창을 곧바로 되가져가 메뉴가 열리자마자 닫혔다.
                    // 닫을지는 TimerWatch 가 정한다(다른 앱 창이 앞에 오면 · 바깥을 누르면)
                    if ((wParam & 0xFFFF) != 0) _wasFg = true;
                    return 0;
                case Native.WM_CAPTURECHANGED:
                    return 0;   // 다시 잡는다(TimerWatch)
                case Native.WM_CLOSE:
                    if (hwnd == _hwnd) Close("wmclose");
                    return 0;
                case 0x0113:   // WM_TIMER
                    if (wParam == (nint)TimerAnim)
                    {
                        double t = Math.Min(1, (Environment.TickCount64 - _animStart) / (double)AnimMs);
                        Push(t);
                        if (t >= 1) Native.KillTimer(hwnd, TimerAnim);
                    }
                    else if (wParam == (nint)TimerWatch)
                    {
                        // 닫는 때(0.5.17-H): 다른 앱의 창이 앞에 왔다(작업 표시줄·1Key 자신의 창은 빼고 — 작업 표시줄은 눌린 뒤 앞 창을 되가져간다),
                        // 메뉴 판 바깥에서 마우스 단추가 눌렸다(앞 창이 아니면 다른 스레드의 창 클릭은 이 창에 오지 않으므로 단추 상태를 직접 본다), 고양이가 숨었다
                        nint fg = Native.GetForegroundWindow();
                        if (fg == hwnd) _wasFg = true;
                        else if (fg != 0 && fg != _fg0 && !OursOrTaskbar(fg)) { Close($"foreground {Native.GetClassName(fg)}"); return 0; }
                        else if (fg != 0 && fg == _fg0 && _wasFg && !OursOrTaskbar(fg)) { Close($"foreground back {Native.GetClassName(fg)}"); return 0; }
                        if (!CatWidget.IsShown) { Close("cat hidden"); return 0; }
                        bool down = (GetAsyncKeyState(0x01) & 0x8000) != 0 || (GetAsyncKeyState(0x02) & 0x8000) != 0;
                        if (down && !_downSeen)
                        {
                            Native.GetCursorPos(out Native.POINT cp);
                            if (Hit(cp.x - _x, cp.y - _y, out bool outside) < 0 && outside) { Close("outside click"); return 0; }
                        }
                        _downSeen = down;
                        if (Native.GetCapture() != hwnd && !down) Native.SetCapture(hwnd);
                    }
                    return 0;
                case Native.WM_MOUSEACTIVATE:
                    return 1;   // MA_ACTIVATE
            }
        }
        catch { }
        return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    // ------------------------------------------------------------------ P/Invoke

    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential)] private struct BIH { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biX, biY; public uint biClrUsed, biClrImportant; }
    [StructLayout(LayoutKind.Sequential)] private struct RECTF { public float X, Y, Width, Height; }

    [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(nint hwnd, nint hdcDst, ref Native.POINT pptDst, ref SIZE psize, nint hdcSrc, ref Native.POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint hdc, ref BIH bmi, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateBitmapFromScan0(int w, int h, int stride, int format, nint scan0, out nint bitmap);
    [DllImport("gdiplus.dll")] private static extern int GdipGetImageGraphicsContext(nint image, out nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateFromHDC(nint hdc, out nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteGraphics(nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipDisposeImage(nint image);
    [DllImport("gdiplus.dll")] private static extern int GdipSetTextRenderingHint(nint graphics, int mode);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateSolidFill(uint argb, out nint brush);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteBrush(nint brush);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateFontFamilyFromName(char* name, nint collection, out nint family);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteFontFamily(nint family);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateFont(nint family, float emSize, int style, int unit, out nint font);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteFont(nint font);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateStringFormat(int flags, int lang, out nint format);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteStringFormat(nint format);
    [DllImport("gdiplus.dll")] private static extern int GdipSetStringFormatLineAlign(nint format, int align);
    [DllImport("gdiplus.dll")] private static extern int GdipSetStringFormatAlign(nint format, int align);
    [DllImport("gdiplus.dll")] private static extern int GdipDrawString(nint graphics, char* text, int len, nint font, ref RECTF layout, nint format, nint brush);
    [DllImport("gdiplus.dll")] private static extern int GdipMeasureString(nint graphics, char* text, int len, nint font, ref RECTF layout, nint format, out RECTF bound, out int cp, out int lines);
}
