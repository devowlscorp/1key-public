namespace OneKey;

/// <summary>
/// 새 디자인(2026-10-08 금속 질감)의 컨트롤 그리기: 목록의 조각(Row.Tile)·자동 잠금 칸(Row.LockInfo)·머리줄 둥근 단추(Btn.Knob)·
/// [입력](Btn.PillInput)·[+ 추가](Btn.PillMain)·설정 링크(Btn.Borderless | Btn.Link).
/// 바탕은 단색으로 지우지 않고 본창 바탕 그림(판·몸체가 그려진 무늬 붓)을 그 자리 그대로 깐 뒤, 시안의 CSS 와 같은 식(<see cref="Metal"/>)으로 그린다.
/// 그래서 조각의 그늘과 둥근 모서리 밖으로 판의 빛 번짐·결이 이어진다. 크기는 시안의 논리 px.
/// </summary>
internal static unsafe class MetalUi
{
    /// <summary>조각: 높이 58 · 사이 8 · 반지름 12 · 단축키 칸 70. 창은 그늘 자리만큼 크다(왼쪽·오른쪽 3, 위 3, 아래 4).</summary>
    public const int TileH = 58, TileGap = 8, TileMarginX = 3, TileMarginTop = 3, TileMarginBottom = 4, KeyColW = 70;
    public const int TileWinH = TileH + TileMarginTop + TileMarginBottom;
    /// <summary>머리줄 둥근 단추: 지름 30, 창 36×37(그늘 자리).</summary>
    public const int KnobD = 30, KnobPadX = 3, KnobPadTop = 2, KnobPadBottom = 5;
    /// <summary>[+ 추가] 알약: 높이 36, 창은 좌우 4 · 위 2 · 아래 8 더 크다.</summary>
    public const int PillMainH = 36, PillPadX = 4, PillPadTop = 2, PillPadBottom = 8;
    public const int PillInputH = 28;

    /// <summary>32비트 DIB 에 더블 버퍼로 그린다: 바탕 그림을 이 창 자리로 깔고 draw(DC, 픽셀, 배율). 픽셀 그리기를 먼저, 글자를 나중에.</summary>
    public static void PaintDib(nint hwnd, Action<nint, Metal.Surf, double> draw)
    {
        nint printDc = Ctl.PrintDc;
        Native.PAINTSTRUCT ps = default;
        nint hdc = printDc != 0 ? printDc : Native.BeginPaint(hwnd, out ps);
        Native.GetClientRect(hwnd, out Native.RECT rc);
        int w = rc.right, h = rc.bottom;
        if (w > 0 && h > 0)
        {
            var bih = new Fx.BITMAPINFOHEADER { biSize = (uint)sizeof(Fx.BITMAPINFOHEADER), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            nint mem = Native.CreateCompatibleDC(hdc);
            nint bmp = Fx.CreateDIBSection(hdc, ref bih, 0, out nint bits, 0, 0);
            if (mem != 0 && bmp != 0 && bits != 0)
            {
                nint old = Native.SelectObject(mem, bmp);
                if (Native.GetParent(hwnd) == Theme.BgOwner) { Theme.AlignBg(mem, hwnd); Native.FillRect(mem, ref rc, Theme.BgBrush); }
                else Native.FillRect(mem, ref rc, Theme.CardBrush);   // 확인 상자·입력 칩: 그 창의 바탕(카드색)
                Metal.GdiFlush();
                Native.SetBkMode(mem, Native.TRANSPARENT);
                uint dpi = WorkArea.DpiFor(hwnd);   // 검증 모드의 배율 흉내(ONEKEY_TEST_WORKAREA)도 따른다
                draw(mem, new Metal.Surf((uint*)bits, w, h), (dpi == 0 ? 96 : dpi) / 96.0);
                Metal.GdiFlush();
                Native.BitBlt(hdc, 0, 0, w, h, mem, 0, 0, Native.SRCCOPY);
                Native.SelectObject(mem, old);
            }
            if (bmp != 0) Native.DeleteObject(bmp);
            if (mem != 0) Native.DeleteDC(mem);
        }
        if (printDc == 0) Native.EndPaint(hwnd, ref ps);
    }

    private static int R(double v) => (int)Math.Round(v);

    /// <summary>이 컨트롤을 금속 모양으로 그리나: 새 디자인으로 옮긴 화면(Theme.MetalPage)의 본창 자식만 — 대화상자·칩 안의 같은 컨트롤은 예전 모양 그대로.</summary>
    public static bool On(nint hwnd)
    {
        nint parent = Native.GetParent(hwnd);
        if (parent == 0) return false;
        if (parent == Theme.BgOwner) return Theme.MetalPage && Theme.BgOwner != 0;
        return OnPopup(parent);
    }

    /// <summary>확인 상자·입력 칩의 버튼도 새 디자인 알약으로(2026-10-08). 바탕은 그 창의 카드색(아래 PaintDib).</summary>
    private static bool OnPopup(nint parent)
    {
        string cls = Native.GetClassName(parent);
        return cls == Dialog.ClassName || cls == Chip.ClassName;
    }

    private static double Dpi(nint hwnd) { uint d = WorkArea.DpiFor(hwnd); return (d == 0 ? 96 : d) / 96.0; }

    /// <summary>설정의 줄 높이 44 · 스위치 42×24 · 작은 알약 높이 28(창은 좌우 3 · 위 2 · 아래 4 더 크다 — 그늘 자리).</summary>
    public const int SetRowH = 44, SwitchW = 42, SwitchH = 24, SmallPillH = 28, SmallPadX = 3, SmallPadTop = 2, SmallPadBottom = 4;

    /// <summary>스위치 줄(Toggle): 왼쪽 이름(창 글꼴), 오른쪽 끝 스위치. 쓸 수 없으면 흐리게.</summary>
    public static void PaintToggle(nint hwnd, nint font, bool on, bool hot, bool pressed, bool enabled, bool focus, bool trailing)
    {
        bool dark = Theme.IsDark;
        string label = Native.GetWindowText(hwnd);
        PaintDib(hwnd, (dc, s, k) =>
        {
            int w = s.W, h = s.H;
            uint[]? snap = enabled ? null : Metal.Snap(s);
            double sw = SwitchW * k, sh = SwitchH * k, sx = trailing ? w - sw : 0, sy = (h - sh) / 2;
            Metal.Switch(s, sx, sy, k, dark, on, !enabled ? 0 : pressed ? 2 : hot ? 1 : 0);
            if (snap is not null) Metal.Fade(s, snap, 0.55);
            double gap = 8 * k;
            double l = trailing ? 0 : sw + gap, r = trailing ? sx - gap : w;
            Ctl.Text(dc, font, label, Metal.Ref(enabled ? Metal.Ink(dark) : Metal.InkSub(dark)), R(l), 0, R(r), h, Native.DT_LEFT | Native.DT_VCENTER | Native.DT_END_ELLIPSIS);
            if (focus) Ring(dc, sx, sy, sw, sh, sh / 2, k);
        });
    }

    /// <summary>
    /// 테마 고르기(Dropdown.Seg)의 칸 경계: 홈 안쪽 3px, 칸 사이 2px, 칸 폭은 글 폭 + 24 에 비례. 경계 값은 칸 사이 틈의 가운데라
    /// 누른 자리로 고를 때 그대로 쓴다(맨 앞 0, 맨 끝 w).
    /// </summary>
    public static double[] SegEdges(nint hwnd, string[] items, int w)
    {
        double k = Dpi(hwnd);
        int n = Math.Max(1, items.Length);
        nint dc = Native.GetDC(hwnd);
        var want = new double[n];
        double sum = 0;
        for (int i = 0; i < n; i++) { want[i] = (i < items.Length ? TextWidth(dc, 12, true, items[i]) : 0) + 24 * k; sum += want[i]; }
        Native.ReleaseDC(hwnd, dc);
        double inner = w - 6 * k - 2 * k * (n - 1), x = 3 * k;
        var e = new double[n + 1];
        e[0] = 0;
        for (int i = 0; i < n; i++) { x += inner * want[i] / Math.Max(1, sum); e[i + 1] = i == n - 1 ? w : x + k; x += 2 * k; }
        return e;
    }

    /// <summary>테마 고르기의 자연스러운 폭(논리 px): 글 폭 + 24 씩, 사이 2, 홈 안쪽 3.</summary>
    public static int SegWidth(nint anyHwnd, string[] items)
    {
        double k = Dpi(anyHwnd);
        nint dc = Native.GetDC(anyHwnd);
        double sum = 0;
        foreach (string it in items) sum += TextWidth(dc, 12, true, it) / k + 24;
        Native.ReleaseDC(anyHwnd, dc);
        return (int)Math.Ceiling(sum + 2 * (items.Length - 1) + 6);
    }

    public static void PaintSeg(nint hwnd, string[] items, int cur)
    {
        bool dark = Theme.IsDark, enabled = Native.IsWindowEnabled(hwnd), focus = Native.GetFocus() == hwnd && Ctl.ShowFocus;
        PaintDib(hwnd, (dc, s, k) =>
        {
            int w = s.W, h = s.H;
            Metal.Well(s, 0, 0, w, h, 11 * k, k, dark);
            double[] e = SegEdges(hwnd, items, w);
            for (int i = 0; i < items.Length; i++)
            {
                double x0 = i == 0 ? 3 * k : e[i] + k, x1 = i == items.Length - 1 ? w - 3 * k : e[i + 1] - k;
                if (i == cur) Metal.SegChip(s, x0, 3 * k, x1 - x0, h - 6 * k, k, dark);
            }
            for (int i = 0; i < items.Length; i++)
            {
                double x0 = i == 0 ? 3 * k : e[i] + k, x1 = i == items.Length - 1 ? w - 3 * k : e[i + 1] - k;
                bool sel = i == cur;
                uint ink = !enabled ? Metal.InkSub(dark) : sel ? (dark ? 0xFFFFFFu : Metal.Ink(dark)) : Metal.InkSeg(dark);
                Text(dc, 12, sel, items[i], ink, x0, 0, x1, h, Native.DT_CENTER | Native.DT_VCENTER | Native.DT_END_ELLIPSIS);
            }
            if (focus) Ring(dc, 0, 0, w, h, 11 * k, k);
        });
    }

    /// <summary>언어 고르기(Dropdown): 시안의 "한국어 ›"처럼 오른쪽 끝에 값 글 + 아래 꺾쇠(누르면 목록이 펼쳐진다는 표시). 올리면 옅은 알약.</summary>
    public static void PaintDropdown(nint hwnd, string text)
    {
        nint st = Ctl.State(hwnd);
        bool dark = Theme.IsDark, enabled = Native.IsWindowEnabled(hwnd);
        bool hot = (st & (Ctl.StHot | Ctl.StPressed)) != 0 && enabled, pressed = (st & Ctl.StPressed) != 0 && enabled;
        bool focus = Native.GetFocus() == hwnd && Ctl.ShowFocus;
        PaintDib(hwnd, (dc, s, k) =>
        {
            int w = s.W, h = s.H;
            uint ink = enabled ? Metal.InkLink(dark) : Metal.InkSub(dark);
            double gw = 14 * k, right = w - 8 * k;
            int tw = Math.Min(TextWidth(dc, 12.5, false, text), (int)(right - gw - 4 * k - 12 * k));
            double pill = tw + gw + 4 * k + 16 * k, py = (h - 30 * k) / 2;
            if (hot) Metal.RRect(s, w - pill, py, pill, 30 * k, 15 * k, dark ? 0xFFFFFFu : 0x000000u, pressed ? 0.10 : 0.05);
            Text(dc, 12.5, false, text, ink, right - gw - 4 * k - tw, 0, right - gw - 4 * k, h, Native.DT_RIGHT | Native.DT_VCENTER | Native.DT_END_ELLIPSIS);
            Ctl.Text(dc, Theme.FontIconSmall, "", Metal.Ref(ink), R(right - gw), 0, R(right), h, Native.DT_CENTER | Native.DT_VCENTER);
            if (focus) Ring(dc, w - pill + 2 * k, py + 2 * k, pill - 4 * k, 26 * k, 13 * k, k);
        });
    }

    /// <summary>키 조합 칸(HotkeyBox): 파인 홈(반지름 10) 가운데에 조합 글. 비었으면 "(없음)", 포커스면 "키를 누르세요" + 강조 테두리.</summary>
    public static void PaintHotkey(nint hwnd, string text, string empty)
    {
        bool dark = Theme.IsDark, enabled = Native.IsWindowEnabled(hwnd), focus = Native.GetFocus() == hwnd;
        bool hot = (Ctl.State(hwnd) & Ctl.StHot) != 0 && enabled;
        PaintDib(hwnd, (dc, s, k) =>
        {
            int w = s.W, h = s.H;
            Metal.Well(s, 0, 0, w, h, 10 * k, k, dark, hot || focus ? 1 : 0);
            bool none = text.Length == 0;
            Text(dc, 12.5, !none, none ? empty : text, !enabled || none ? Metal.InkSub(dark) : Metal.Ink(dark), 10 * k, 0, w - 10 * k, h, Native.DT_CENTER | Native.DT_VCENTER | Native.DT_END_ELLIPSIS);
            if (focus) Gdiplus.DrawRoundRect(dc, 0, 0, w, h, (float)(10 * k), Metal.Ref(Metal.FocusInk(Theme.IsDark)), (float)Math.Max(1.5, 1.5 * k));
        });
    }

    // ---------------------------------------------------------------- 조각 안의 보통 행(고급 설정의 진단 행, 프로그램 고르기 목록 등)

    /// <summary>
    /// 조각 위 행: 바탕(조각 픽셀)을 그대로 두고, 올리면 옅은 둥근 강조 · 왼쪽 아이콘(작은 둥근 단추) · 이름 13.5px 굵게 · 설명 11px · 오른쪽 꺾쇠 ·
    /// 마지막 행이 아니면 아래 구분선(양옆 14 안쪽). 예전 카드 모양(테두리·둥근 모서리)은 조각이 대신한다.
    /// </summary>
    public static void PaintPlainRow(nint hwnd, uint style, string sub, string icon)
    {
        nint st = Ctl.State(hwnd);
        bool enabled = Native.IsWindowEnabled(hwnd);
        bool hot = Ctl.BandHot(hwnd) && enabled, pressed = (st & Ctl.StPressed) != 0 && enabled;
        bool focus = Ctl.HasFocusRing(hwnd), dark = Theme.IsDark;
        string title = Native.GetWindowText(hwnd);
        PaintDib(hwnd, (dc, s, k) =>
        {
            int w = s.W, h = s.H;
            if (hot || pressed) Metal.RRect(s, 4 * k, 3 * k, w - 8 * k, h - 6 * k, 9 * k, dark ? 0xFFFFFFu : 0x000000u, dark ? (pressed ? 0.09 : 0.05) : (pressed ? 0.07 : 0.035));
            if ((style & Row.Last) == 0) Metal.Sep(s, Row.PadX * k, h - Math.Max(1, Math.Round(k)), w - 2 * Row.PadX * k, k, dark);
            double x = Row.PadX * k;
            if (icon.Length > 0)
            {
                double d = 28 * k, iy = (h - d) / 2;
                if ((style & Row.PlainIcon) == 0) Metal.Knob(s, x, iy, d, k, dark, 0);
                uint ic = !enabled ? InkSubOf(dark) : (style & Row.AccentTitle) != 0 ? Metal.InkAccent(dark) : Metal.InkIcon(dark);
                Ctl.Text(dc, Theme.FontIconSmall, icon, Metal.Ref(ic), R(x), R(iy), R(x + d), R(iy + d), Native.DT_CENTER | Native.DT_VCENTER);
                x += d + 12 * k;
            }
            double right = w - Row.PadX * k;
            if ((style & (Row.Chevron | Row.ChevronDown | Row.ChevronUp)) != 0)
            {
                string glyph = (style & Row.ChevronDown) != 0 ? "" : (style & Row.ChevronUp) != 0 ? "" : "";
                Ctl.Text(dc, Theme.FontIconSmall, glyph, Metal.Ref(dark ? 0x8A8A8Au : 0x8E8E8Eu), R(right - 14 * k), 0, R(right), h, Native.DT_RIGHT | Native.DT_VCENTER);
                right -= 22 * k;
            }
            uint ink = !enabled ? InkSubOf(dark) : (style & Row.AccentTitle) != 0 ? Metal.InkAccent(dark) : Metal.Ink(dark);
            if (sub.Length > 0 && (style & Row.InlineSubtitle) != 0)
            {
                int tw = TextWidth(dc, 13.5, true, title);
                Text(dc, 13.5, true, title, ink, x, 0, right, h, Native.DT_LEFT | Native.DT_VCENTER | Native.DT_END_ELLIPSIS);
                Text(dc, 11, false, sub, Metal.InkNote(dark), x + tw + 10 * k, 0, right, h, Native.DT_LEFT | Native.DT_VCENTER | Native.DT_END_ELLIPSIS);
            }
            else if (sub.Length > 0)
            {
                Text(dc, 13.5, true, title, ink, x, 5 * k, right, 24 * k, Native.DT_LEFT | Native.DT_VCENTER | Native.DT_END_ELLIPSIS);
                Text(dc, 11, false, sub, Metal.InkNote(dark), x, 23 * k, right, h - 4 * k, Native.DT_LEFT | Native.DT_VCENTER | Native.DT_END_ELLIPSIS);
            }
            else Text(dc, 13.5, true, title, ink, x, 0, right, h, Native.DT_LEFT | Native.DT_VCENTER | Native.DT_END_ELLIPSIS);
            if (focus) Ring(dc, 4 * k, 3 * k, w - 8 * k, h - 6 * k, 9 * k, k);
        });
    }

    private static uint InkSubOf(bool dark) => Metal.InkSub(dark);

    // ---------------------------------------------------------------- 추가 메뉴의 고르기 조각(Row.Choice, 시안 Add-light/dark.dc.html)

    /// <summary>고르기 조각: 높이 62, 사이 8, 둥근 표식 34(왼쪽 12 안쪽), 글은 표식 뒤 12.</summary>
    public const int ChoiceH = 62, ChoiceGap = 8;

    public static void PaintChoice(nint hwnd, string sub, string mark)
    {
        nint st = Ctl.State(hwnd);
        bool enabled = Native.IsWindowEnabled(hwnd);
        bool hot = (st & Ctl.StHot) != 0 && enabled, pressed = (st & Ctl.StPressed) != 0 && enabled;
        bool focus = Ctl.HasFocusRing(hwnd), dark = Theme.IsDark;
        string title = Native.GetWindowText(hwnd);
        PaintDib(hwnd, (dc, s, k) =>
        {
            int w = s.W, h = s.H;
            uint[]? snap = enabled ? null : Metal.Snap(s);
            double tx = TileMarginX * k, ty = TileMarginTop * k, tw = w - 2 * tx, th = h - (TileMarginTop + TileMarginBottom) * k;
            Metal.Tile(s, tx, ty, tw, th, k, dark, pressed ? 2 : hot ? 1 : 0);
            double d = 34 * k, kx = tx + 12 * k, ky = ty + (th - d) / 2;
            Metal.Knob(s, kx, ky, d, k, dark, 0);
            if (snap is not null) Metal.Fade(s, snap, 0.55);
            // 표식: 넣는 글 세 양식은 강조색 짧은 글(Aa · ID · +), 바로 실행은 갈색 아이콘(시안의 ▶ ▢ ◎)
            bool glyph = IsGlyph(mark);
            uint markInk = !enabled ? Metal.InkSub(dark) : glyph ? (dark ? 0xD9C4A8u : 0x7A6A58u) : Metal.InkAccent(dark);
            if (glyph) Ctl.Text(dc, Theme.FontIcon, mark, Metal.Ref(markInk), R(kx), R(ky), R(kx + d), R(ky + d), Native.DT_CENTER | Native.DT_VCENTER);
            else Text(dc, 13, true, mark, markInk, kx, ky, kx + d, ky + d, Native.DT_CENTER | Native.DT_VCENTER);
            double x = kx + d + 12 * k, right = tx + tw - 24 * k, top = ty + (th - 38.5 * k) / 2;
            Text(dc, 14, true, title, enabled ? Metal.Ink(dark) : Metal.InkSub(dark), x, top, right, top + 21 * k, Native.DT_LEFT | Native.DT_VCENTER | Native.DT_END_ELLIPSIS);
            Text(dc, 11, false, sub, enabled ? Metal.InkNote(dark) : Metal.InkSub(dark), x, top + 22 * k, right, top + 38.5 * k, Native.DT_LEFT | Native.DT_VCENTER | Native.DT_END_ELLIPSIS);
            Text(dc, 16, false, "›", dark ? 0x8A8A8Au : 0x8E8E8Eu, right, ty, tx + tw - 12 * k, ty + th, Native.DT_RIGHT | Native.DT_VCENTER);
            if (focus) Ring(dc, tx + 2 * k, ty + 2 * k, tw - 4 * k, th - 4 * k, 10 * k, k);
        });
    }

    // ---------------------------------------------------------------- 자동 잠금(Slider): ‹ 큰 숫자 › + 설명 + 단계 점

    /// <summary>줄 높이 56(‹ › 지름 34 가 가운데), 단계 점은 위에서 64, 창 높이 83. 단추는 양 끝에서 6 안쪽.</summary>
    public const int StepRowH = 56, StepKnobD = 34, StepPadX = 6, StepDotsY = 64, StepH = 83;
    /// <summary>누르고 있는 부분: 1 ‹ · 2 › · 3 점(끌기).</summary>
    private static int _stepPart;

    private static double[] DotXs(double w, double k, int n, int idx)
    {
        double total = (n - 1) * 5 * k + 22 * k + (n - 1) * 6 * k, x = (w - total) / 2;
        var xs = new double[n];
        for (int i = 0; i < n; i++) { xs[i] = x; x += (i == idx ? 22 : 5) * k + 6 * k; }
        return xs;
    }

    /// <summary>누른 자리: 바꿀 단계(없으면 -1). 처음 누를 때 ‹(1) ›(2) 점(3) 가운데 어디인지 기억하고, 끄는 동안은 점에서만 움직인다.</summary>
    public static int StepHit(nint hwnd, int x, int y, int n, int idx, bool dragging)
    {
        double k = Dpi(hwnd);
        Native.GetClientRect(hwnd, out Native.RECT rc);
        double w = rc.right, d = StepKnobD * k, ky = (StepRowH * k - d) / 2;
        if (!dragging)
        {
            _stepPart = 0;
            if (y >= ky - 4 * k && y <= ky + d + 4 * k)
            {
                if (x >= StepPadX * k - 4 * k && x <= StepPadX * k + d + 4 * k) { _stepPart = 1; return Math.Max(0, idx - 1); }
                if (x >= w - StepPadX * k - d - 4 * k && x <= w - StepPadX * k + 4 * k) { _stepPart = 2; return Math.Min(n - 1, idx + 1); }
            }
            if (y < StepDotsY * k - 12 * k || y > StepDotsY * k + 17 * k) return -1;
            _stepPart = 3;
        }
        else if (_stepPart != 3) return -1;
        double[] xs = DotXs(w, k, n, idx);
        int best = 0; double bd = double.MaxValue;
        for (int i = 0; i < n; i++)
        {
            double c = xs[i] + (i == idx ? 11 : 2.5) * k, dd = Math.Abs(x - c);
            if (dd < bd) { bd = dd; best = i; }
        }
        return best;
    }

    public static void StepRelease() => _stepPart = 0;

    public static void PaintStepper(nint hwnd, string[] labels, int idx)
    {
        nint st = Ctl.State(hwnd);
        bool dark = Theme.IsDark, enabled = Native.IsWindowEnabled(hwnd);
        bool pressed = (st & Ctl.StPressed) != 0 && enabled, focus = Ctl.HasFocusRing(hwnd);
        int n = labels.Length;
        string label = idx >= 0 && idx < n ? labels[idx] : "";
        PaintDib(hwnd, (dc, s, k) =>
        {
            int w = s.W;
            uint[]? snap = enabled ? null : Metal.Snap(s);
            double d = StepKnobD * k, ky = (StepRowH * k - d) / 2, lx = StepPadX * k, rx = w - StepPadX * k - d;
            Metal.Knob(s, lx, ky, d, k, dark, pressed && _stepPart == 1 ? 2 : 0, 2);
            Metal.Knob(s, rx, ky, d, k, dark, pressed && _stepPart == 2 ? 2 : 0, 2);
            double[] xs = DotXs(w, k, n, idx);
            for (int i = 0; i < n; i++) Metal.StepDot(s, xs[i], StepDotsY * k, k, dark, i == idx);
            if (snap is not null) Metal.Fade(s, snap, 0.55);
            uint ink = enabled ? Metal.Ink(dark) : Metal.InkSub(dark), icon = enabled ? Metal.InkIcon(dark) : Metal.InkSub(dark);
            Ctl.Text(dc, Theme.FontIcon, "", Metal.Ref(icon), R(lx), R(ky), R(lx + d), R(ky + d), Native.DT_CENTER | Native.DT_VCENTER);
            Ctl.Text(dc, Theme.FontIcon, "", Metal.Ref(icon), R(rx), R(ky), R(rx + d), R(ky + d), Native.DT_CENTER | Native.DT_VCENTER);
            // 큰 숫자 36 + 단위 16(글자 바닥줄을 맞춘다), 아래에 작은 설명
            int digits = 0; while (digits < label.Length && char.IsDigit(label[digits])) digits++;
            string num = digits > 0 ? label[..digits] : label, unit = digits > 0 ? label[digits..].Trim() : "";
            int nw = TextWidth(dc, 36, false, num), uw = unit.Length > 0 ? TextWidth(dc, 16, false, unit) + R(3 * k) : 0;
            double cx = w / 2.0, x0 = cx - (nw + uw) / 2.0, mid = 20 * k;
            Text(dc, 36, false, num, ink, x0, mid - 26 * k, x0 + nw + 2, mid + 26 * k, Native.DT_LEFT | Native.DT_VCENTER);
            if (unit.Length > 0) Text(dc, 16, false, unit, ink, x0 + nw + 3 * k, mid + 6.8 * k - 12 * k, x0 + nw + uw + 4 * k, mid + 6.8 * k + 12 * k, Native.DT_LEFT | Native.DT_VCENTER);
            string note = digits > 0 ? T.SetLockAfter : T.SetLockManual;
            Text(dc, 11, false, note, enabled ? Metal.InkNote(dark) : Metal.InkSub(dark), lx + d + 4 * k, 40 * k, rx - 4 * k, 56 * k, Native.DT_CENTER | Native.DT_VCENTER | Native.DT_END_ELLIPSIS);
            if (focus) Ring(dc, 2 * k, 2 * k, w - 4 * k, s.H - 4 * k, 12 * k, k);
        });
    }

    /// <summary>시안 크기(논리 px)·굵기의 글자. 색은 0xRRGGBB.</summary>
    public static void Text(nint dc, double size, bool strong, string s, uint rgb, double l, double t, double r, double b, uint fmt)
        => Ctl.Text(dc, Theme.Sized(size, strong), s, Metal.Ref(rgb), R(l), R(t), R(r), R(b), fmt);

    /// <summary>글 폭(물리 px).</summary>
    public static int TextWidth(nint dc, double size, bool strong, string s)
    {
        nint f = Theme.Sized(size, strong);
        if (Dw.Width(f, s) is int dw) return dw;
        nint old = Native.SelectObject(dc, f);
        int w = Native.TextWidth(dc, s);
        Native.SelectObject(dc, old);
        return w;
    }

    private static bool IsGlyph(string s) => s.Length > 0 && s.All(c => c >= 0xE000 && c <= 0xF8FF);

    private static void Ring(nint dc, double x, double y, double w, double h, double r, double k)
        => Gdiplus.DrawRoundRect(dc, R(x - 2 * k), R(y - 2 * k), R(w + 4 * k), R(h + 4 * k), (float)(r + 2 * k), Metal.Ref(Metal.FocusInk(Theme.IsDark)), (float)Math.Max(1.5, 1.5 * k));

    public static void PaintButton(nint hwnd, uint style, uint kind)
    {
        nint st = Ctl.State(hwnd);
        bool enabled = Native.IsWindowEnabled(hwnd);
        bool hot = (st & Ctl.StHot) != 0 && enabled, pressed = (st & Ctl.StPressed) != 0 && enabled;
        bool focus = Ctl.HasFocusRing(hwnd), dark = Theme.IsDark;
        int state = pressed ? 2 : hot ? 1 : 0;
        string text = Native.GetWindowText(hwnd);
        PaintDib(hwnd, (dc, s, k) =>
        {
            int w = s.W, h = s.H;
            switch (kind)
            {
                case Btn.Knob:
                {
                    double d = KnobD * k, x = KnobPadX * k, y = KnobPadTop * k;
                    Metal.Knob(s, x, y, d, k, dark, state);
                    bool glyph = IsGlyph(text);
                    uint ink = !enabled ? Metal.InkSub(dark) : glyph ? Metal.InkIcon(dark) : Metal.InkLabel(dark);
                    if (glyph) Ctl.Text(dc, Theme.FontIcon, text, Metal.Ref(ink), R(x), R(y), R(x + d), R(y + d), Native.DT_CENTER | Native.DT_VCENTER);
                    else Text(dc, 14, false, text, ink, x, y, x + d, y + d, Native.DT_CENTER | Native.DT_VCENTER);
                    if (focus) Ring(dc, x, y, d, d, d / 2, k);
                    break;
                }
                case Btn.PillMain:
                {
                    double x = PillPadX * k, y = PillPadTop * k, pw = w - 2 * PillPadX * k, ph = PillMainH * k;
                    Metal.Pill(s, x, y, pw, ph, k, dark, true, state);
                    Text(dc, 13, true, text, enabled ? Metal.Ink(dark) : Metal.InkSub(dark), x, y, x + pw, y + ph, Native.DT_CENTER | Native.DT_VCENTER);
                    if (focus) Ring(dc, x, y, pw, ph, ph / 2, k);
                    break;
                }
                case Btn.PillInput:
                {
                    // 짝 조각의 오른쪽 끝: 왼쪽은 조각에 이어 직각(바깥으로 밀어 그린다), 오른쪽은 둥근 모서리
                    bool band = Ctl.BandHot(hwnd);
                    bool rowPressed = Ctl.Partner.TryGetValue(hwnd, out nint row) && (Ctl.State(row) & Ctl.StPressed) != 0;
                    double tx = -24 * k, ty = TileMarginTop * k, tw = w - TileMarginX * k - tx, th = h - (TileMarginTop + TileMarginBottom) * k;
                    Metal.Tile(s, tx, ty, tw, th, k, dark, rowPressed ? 2 : band ? 1 : 0);
                    double pw = TextWidth(dc, 12, true, text) + 24 * k, ph = PillInputH * k, px = tx + tw - 10 * k - pw, py = ty + (th - ph) / 2;
                    Metal.Pill(s, px, py, pw, ph, k, dark, false, enabled ? state : 0);
                    Text(dc, 12, true, text, enabled ? Metal.InkAccent(dark) : Metal.InkSub(dark), px, py, px + pw, py + ph, Native.DT_CENTER | Native.DT_VCENTER);
                    if (focus) Ring(dc, px, py, pw, ph, ph / 2, k);
                    break;
                }
                case Btn.Bordered:
                case Btn.DangerBordered:
                {
                    // 창이 [+ 추가]만 하면(높이 46 — 아래 막대의 [테스트]·[삭제]) 큰 알약(36), 아니면 조각 안의 작은 알약(설정의 [변경]·[백업]·[기본값] 등, 28).
                    // 위험 버튼([삭제])은 같은 알약에 빨간 글자
                    bool big = h >= (PillMainH + PillPadTop + PillPadBottom) * k - 1;
                    double x = (big ? PillPadX : SmallPadX) * k, y = (big ? PillPadTop : SmallPadTop) * k;
                    double pw = w - 2 * x, ph = h - (big ? PillPadTop + PillPadBottom : SmallPadTop + SmallPadBottom) * k;
                    uint[]? snap = enabled ? null : Metal.Snap(s);
                    Metal.Pill(s, x, y, pw, ph, k, dark, big, state);
                    if (snap is not null) Metal.Fade(s, snap, 0.55);
                    uint ink = !enabled ? Metal.InkSub(dark) : kind == Btn.DangerBordered ? Metal.Ref(Theme.DangerText) : Metal.Ink(dark);
                    Text(dc, big ? 13 : 12, true, text, ink, x, y, x + pw, y + ph, Native.DT_CENTER | Native.DT_VCENTER | Native.DT_END_ELLIPSIS);
                    if (focus) Ring(dc, x, y, pw, ph, ph / 2, k);
                    break;
                }
                case Btn.Icon:
                {
                    // 칸 옆 아이콘 단추(보이기 눈 · 지우기 × · 입력 추가 +): 작은 둥근 금속 단추 + 아이콘
                    double dd = Math.Min(w, h) - 5 * k, x = (w - dd) / 2, y = (h - dd) / 2 - 1 * k;
                    uint[]? snap = enabled ? null : Metal.Snap(s);
                    Metal.Knob(s, x, y, dd, k, dark, state);
                    if (snap is not null) Metal.Fade(s, snap, 0.55);
                    Ctl.Text(dc, Theme.FontIconSmall, text, Metal.Ref(enabled ? Metal.InkIcon(dark) : Metal.InkSub(dark)), R(x), R(y), R(x + dd), R(y + dd), Native.DT_CENTER | Native.DT_VCENTER);
                    if (focus) Ring(dc, x, y, dd, dd, dd / 2, k);
                    break;
                }
                case Btn.Prominent:
                {
                    // [저장]: 강조색 알약(높이 36, 창 여백은 [+ 추가]와 같다). 기본 버튼 이중 선은 시안에 없어 그리지 않는다(포커스 선은 그대로).
                    // 창이 그만큼 크지 않으면(확인 상자·입력 칩의 버튼) 작은 여백으로 창 안을 채운다
                    bool big = h >= (PillMainH + PillPadTop + PillPadBottom) * k - 1;
                    double x = (big ? PillPadX : SmallPadX) * k, y = (big ? PillPadTop : SmallPadTop) * k;
                    double pw = w - 2 * x, ph = h - (big ? PillPadTop + PillPadBottom : SmallPadTop + SmallPadBottom) * k;
                    uint[]? snap = enabled ? null : Metal.Snap(s);
                    Metal.AccentPill(s, x, y, pw, ph, k, dark, state);
                    if (snap is not null) Metal.Fade(s, snap, 0.55);
                    Text(dc, 13, true, text, Metal.OnAccent(dark), x, y, x + pw, y + ph, Native.DT_CENTER | Native.DT_VCENTER | Native.DT_END_ELLIPSIS);
                    if (focus) Ring(dc, x, y, pw, ph, ph / 2, k);
                    break;
                }
                default:   // 링크(설정 ›)
                {
                    if (hot || pressed) Metal.RRect(s, 0, 0, w, h, h / 2.0, dark ? 0xFFFFFFu : 0x000000u, pressed ? 0.10 : 0.05);
                    Text(dc, 13, false, text + " ›", Metal.InkLink(dark), 0, 0, w, h, Native.DT_CENTER | Native.DT_VCENTER | Native.DT_END_ELLIPSIS);
                    if (focus) Ring(dc, 2 * k, 2 * k, w - 4 * k, h - 4 * k, (h - 4 * k) / 2, k);
                    break;
                }
            }
        });
    }

    public static void PaintRow(nint hwnd, uint style, string sub, string mod, string key)
    {
        nint st = Ctl.State(hwnd);
        bool enabled = Native.IsWindowEnabled(hwnd);
        bool hot = Ctl.BandHot(hwnd) && enabled, pressed = (st & Ctl.StPressed) != 0 && enabled;
        bool focus = Ctl.HasFocusRing(hwnd), dark = Theme.IsDark;
        string title = Native.GetWindowText(hwnd);
        PaintDib(hwnd, (dc, s, k) =>
        {
            int w = s.W, h = s.H;
            if ((style & Row.LockInfo) != 0)
            {
                // 판 안의 자동 잠금: 이름표(시계 + 글) / 큰 숫자 + 단위, 오른쪽에 설정으로 가는 꺾쇠 단추. 시안의 스위치는 [저장] 규칙 때문에 여기서 바꾸지 않는다
                if (hot || pressed) Metal.RRect(s, 0, 0, w, h, 14 * k, 0xFFFFFF, dark ? (pressed ? 0.07 : 0.04) : (pressed ? 0.30 : 0.45));
                double p = 8 * k, c = (21.4 + 24.5) * k, d = 26 * k, kx = w - p - d, ky = c - d / 2;
                Metal.Knob(s, kx, ky, d, k, dark, pressed ? 2 : hot ? 1 : 0);
                Gdiplus.DrawClockGlyph(dc, (float)p, (float)(2 * k + 2.2 * k), (float)(13 * k), Metal.Ref(Metal.InkLabel(dark)));
                Text(dc, 12, false, mod, Metal.InkLabel(dark), p + 19 * k, 2 * k, kx - p, 19.4 * k, Native.DT_LEFT | Native.DT_VCENTER | Native.DT_END_ELLIPSIS);
                int nw = TextWidth(dc, 34, false, key);
                Text(dc, 34, false, key, Metal.Ink(dark), p, c - 26 * k, p + nw + 2, c + 26 * k, Native.DT_LEFT | Native.DT_VCENTER);
                if (sub.Length > 0)
                    Text(dc, 16, false, sub, Metal.Ink(dark), p + nw + 3 * k, c + 6.1 * k - 12 * k, kx - 4 * k, c + 6.1 * k + 12 * k, Native.DT_LEFT | Native.DT_VCENTER | Native.DT_END_ELLIPSIS);
                Ctl.Text(dc, Theme.FontIconSmall, "", Metal.Ref(Metal.InkIcon(dark)), R(kx), R(ky), R(kx + d), R(ky + d), Native.DT_CENTER | Native.DT_VCENTER);
                if (focus) Ring(dc, 2 * k, 2 * k, w - 4 * k, h - 4 * k, 12 * k, k);
                return;
            }
            // 판 위 조각: 오른쪽은 짝 [입력] 창이 이어 그리므로 직각으로 밀어 그린다
            bool square = (style & Row.SquareRight) != 0;
            double tx = TileMarginX * k, ty = TileMarginTop * k, th = h - (TileMarginTop + TileMarginBottom) * k;
            double tw = square ? w - tx + 24 * k : w - 2 * tx;
            Metal.Tile(s, tx, ty, tw, th, k, dark, pressed ? 2 : hot ? 1 : 0);
            double col = KeyColW * k;
            Metal.RRect(s, tx + col, ty + 10 * k, Math.Max(1, Math.Round(k)), th - 20 * k, 0, Metal.Divider(dark));
            bool noKey = key.Length == 0;
            Text(dc, 10, false, noKey ? T.ListTitle : mod, Metal.InkSub(dark), tx + 2 * k, ty + 9.25 * k, tx + col - 2 * k, ty + 23.75 * k, Native.DT_CENTER | Native.DT_VCENTER | Native.DT_END_ELLIPSIS);
            Text(dc, 16, true, noKey ? "—" : key, noKey ? Metal.InkSub(dark) : Metal.Ink(dark), tx, ty + 24.75 * k, tx + col, ty + 47.9 * k, Native.DT_CENTER | Native.DT_VCENTER);
            double x = tx + col + 1 + 14 * k, right = w - 6 * k;
            if ((style & Row.EditHint) != 0 && enabled && (hot || focus || Native.GetFocus() == hwnd))
            {
                // "✎ 편집": 행을 누르면 편집, [입력]은 입력 칩(Codex QA-04)
                int ew = TextWidth(dc, 11, false, Row.EditHintText);
                Text(dc, 11, false, Row.EditHintText, Metal.InkSub(dark), right - ew, ty, right, ty + th, Native.DT_RIGHT | Native.DT_VCENTER);
                Ctl.Text(dc, Theme.FontIconSmall, "", Metal.Ref(Metal.InkSub(dark)), R(right - ew - 18 * k), R(ty), R(right - ew - 4 * k), R(ty + th), Native.DT_RIGHT | Native.DT_VCENTER);
                right -= ew + 24 * k;
            }
            Text(dc, 10.5, false, sub, Metal.InkSub(dark), x, ty + 10.4 * k, right, ty + 25.6 * k, Native.DT_LEFT | Native.DT_VCENTER | Native.DT_END_ELLIPSIS);
            Text(dc, 14.5, true, title, enabled ? Metal.Ink(dark) : Metal.InkSub(dark), x, ty + 26.6 * k, right, ty + 47.6 * k, Native.DT_LEFT | Native.DT_VCENTER | Native.DT_END_ELLIPSIS);
            if (focus) Ring(dc, tx + 2 * k, ty + 2 * k, (square ? w - tx : tw) - 4 * k, th - 4 * k, 10 * k, k);
        });
    }
}
