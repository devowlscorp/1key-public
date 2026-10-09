using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>App 의 일부: 창 바탕·카드 그리기. (App.cs 에서 화면별로 나눔, 0.3.9 — 동작은 그대로)</summary>
internal sealed unsafe partial class App
{
    // ---------------------------------------------------------------- 그리기

    /// <summary>카드, 입력 상자, 구분선, 잠금 화면의 큰 아이콘을 직접 그린다 (WM_ERASEBKGND).</summary>
    private void DrawDecorations(nint hdc)
    {
        // 페이지 스크롤 중이면(T5) 본문 장식은 보이는 칸 안에만 그리고, 아래 막대 위에 가는 선을 긋는다.
        int saved = 0;
        if (_pageScrollMax > 0)
        {
            saved = Native.SaveDC(hdc);
            Native.IntersectClipRect(hdc, 0, 0, Scale(WinW), _viewH);
        }
        bool metal = _page.Metal;   // 새 디자인 화면: 카드·구분선은 바탕 그림에 금속 조각으로 들어 있다(PageOverlay)
        if (!metal)
            foreach ((int x, int y, int w, int h) in _page.Cards)
                Gdiplus.FillRoundRect(hdc, Scale(x), Yp(y), Scale(w), Scale(h), Scale(CardRadius), Theme.CardBg);
        foreach (var rs in _page.RowStrips)
        {
            if (!Native.IsWindowEnabled(rs.Row) || !Ctl.BandHot(rs.Row)) continue;
            // 행과 같은 모양: 오른쪽은 카드의 둥근 모서리(첫/마지막 행만), 왼쪽은 행에 이어지게 직각
            int sx = Scale(rs.X), sy = Yp(rs.Y), sw = Scale(rs.W), sh = Scale(rs.H), cr = Scale(CardRadius);
            int sv = Native.SaveDC(hdc);
            Native.IntersectClipRect(hdc, sx, sy, sx + sw, sy + sh);
            int top = rs.First ? sy : sy - cr * 2, bottom = rs.Last ? sy + sh : sy + sh + cr * 2;
            Gdiplus.FillRoundRect(hdc, sx - cr * 2, top, sw + cr * 2, bottom - top, cr, Theme.RowFill(false));
            Native.RestoreDC(hdc, sv);
        }
        // 카드 1px 테두리(B): 줄 강조 뒤에 그린다(강조 채움이 카드 가장자리를 덮지 않게). 행은 자기 몫의 테두리를 스스로 그린다(Row).
        if (!metal)
            foreach ((int x, int y, int w, int h) in _page.Cards)
                Gdiplus.DrawRoundRect(hdc, Scale(x), Yp(y), Scale(w), Scale(h), Scale(CardRadius), Theme.CardBorder, Math.Max(1, Scale(1)));
        if (_cur == Screen.List)
        {
            // 바로 실행 띠: 넘치는 줄 아래 가는 가로 막대, 연필 버튼의 매끈한 가장자리(버튼 창은 동그라미 영역이라 가장자리가 계단 — 같은 원을 먼저 깐다)
            foreach (var info in _stripRows)
                if (StripBar(info, out Native.RECT tr, out Native.RECT th))
                {
                    Gdiplus.FillRoundRect(hdc, tr.left, tr.top, tr.right - tr.left, tr.bottom - tr.top, Scale(2), Theme.Mix(Theme.CardBg, Theme.ControlText, Theme.IsDark ? 0.08 : 0.05));   // 밝은 테마는 더 옅게(2026-10-05 사용자: 너무 진함)
                    Gdiplus.FillRoundRect(hdc, th.left, th.top, th.right - th.left, th.bottom - th.top, Scale(2), Theme.Mix(Theme.CardBg, Theme.ControlText, Theme.IsDark ? 0.35 : 0.18));
                }
            if (_stripBadge is { } bd) Gdiplus.FillEllipse(hdc, Scale(bd.X), Yp(bd.Y), Scale(bd.D), Scale(bd.D), Btn.BadgeFill(_stripEdit));
        }
        foreach ((int x, int y, int w) in metal ? new List<(int, int, int)>() : _page.Separators)
        {
            var rc = new Native.RECT { left = Scale(x), top = Yp(y), right = Scale(x + w), bottom = Yp(y) + Math.Max(1, Scale(1)) };
            Native.FillRect(hdc, ref rc, Theme.BorderBrush);
        }
        if (_page.ScrollTrack is { } st)
        {
            // 목록 카드 오른쪽의 가는 스크롤 막대 (트랙은 연하게, 손잡이는 진하게)
            Gdiplus.FillRoundRect(hdc, Scale(st.X), Scale(st.Y), Scale(st.W), Scale(st.H), Scale(2), Theme.Mix(Theme.CardBg, Theme.ControlText, 0.08));
            Gdiplus.FillRoundRect(hdc, Scale(st.X), Scale(st.ThumbY), Scale(st.W), Scale(st.ThumbH), Scale(2), Theme.Mix(Theme.CardBg, Theme.ControlText, 0.35));
        }
        nint focused = Native.GetFocus();
        for (int fi = 0; fi < _page.Fields.Count; fi++)
        {
            (int x, int y, int w, int h, bool onCard) = _page.Fields[fi];
            if (metal)
            {
                // 새 디자인: 홈은 바탕 그림에 있다. 커서가 있는 칸만 강조색 테두리
                if (fi < _page.FieldEdits.Count && _page.FieldEdits[fi] == focused && focused != 0)
                {
                    bool round = _page.PillFields.Contains(fi);
                    // 포커스 색: 금속 시계의 흑연색(2026-10-08 사용자: 파란 포커스는 이질적), 1.5px
                    Gdiplus.DrawRoundRect(hdc, Scale(x), Yp(y), Scale(w), Scale(h), round ? Scale(h) / 2 : Scale(Theme.FieldRadius), Metal.Ref(Metal.FocusInk(Theme.IsDark)), (float)Math.Max(1.5, 1.5 * _dpi / 96.0));
                }
                continue;
            }
            // 커서가 있는 입력칸은 강조색 테두리: 키 조합 칸·드롭다운과 같은 규칙(라이트·다크 모두 AccentInk)
            uint border = fi < _page.FieldEdits.Count && _page.FieldEdits[fi] == focused && focused != 0 ? Theme.AccentInk : Theme.FieldBorder;
            // 카드 위: 입력칸 색 / 바탕 위: 카드 색 (바탕과 구분되도록), 둘 다 가는 테두리
            Gdiplus.FillRoundRect(hdc, Scale(x), Yp(y), Scale(w), Scale(h), Scale(Theme.FieldRadius), onCard ? Theme.EditBg : Theme.CardBg);
            Gdiplus.DrawRoundRect(hdc, Scale(x), Yp(y), Scale(w), Scale(h), Scale(Theme.FieldRadius), border, Math.Max(1, Scale(1)));
        }

        if (_cur == Screen.Lock && !metal)
        {
            int size = Scale(64), ix = (Scale(WinW) - size) / 2, iy = Yp(56);
            Native.DrawIconEx(hdc, ix, iy, _iconHero, size, size, 0, 0, Native.DI_NORMAL);
            // 오른쪽 아래 자물쇠 배지
            int bd = Scale(26), bx = ix + size - bd + Scale(6), by = iy + size - bd + Scale(6);
            Gdiplus.FillEllipse(hdc, bx - 1, by - 1, bd + 2, bd + 2, Theme.WindowBg);
            Gdiplus.FillEllipse(hdc, bx, by, bd, bd, Theme.CardBg);
            nint old = Native.SelectObject(hdc, Theme.FontIcon);
            Native.SetBkMode(hdc, Native.TRANSPARENT);
            Native.SetTextColor(hdc, Theme.SecondaryText);
            var rc = new Native.RECT { left = bx, top = by, right = bx + bd, bottom = by + bd };
            Native.DrawText(hdc, IcLock, ref rc, Native.DT_CENTER | Native.DT_VCENTER | Native.DT_SINGLELINE);
            Native.SelectObject(hdc, old);
        }
        if (saved != 0)
        {
            Native.RestoreDC(hdc, saved);
            var bar = new Native.RECT { left = 0, top = _viewH, right = Scale(WinW), bottom = _viewH + Math.Max(1, Scale(1)) };
            Native.FillRect(hdc, ref bar, Theme.BorderBrush);
            // 본문 스크롤 막대: 트랙은 연하게, 손잡이는 진하게 (목록의 스크롤 막대와 같은 모양)
            if (PageScrollBar(out Native.RECT tr, out Native.RECT th))
            {
                int r = Math.Max(2, (tr.right - tr.left) / 2);
                Gdiplus.FillRoundRect(hdc, tr.left, tr.top, tr.right - tr.left, tr.bottom - tr.top, r, Theme.Mix(Theme.WindowBg, Theme.ControlText, 0.08));
                Gdiplus.FillRoundRect(hdc, th.left, th.top, th.right - th.left, th.bottom - th.top, r, Theme.Mix(Theme.WindowBg, Theme.ControlText, 0.40));
            }
        }
    }

    /// <summary>
    /// 본창 바탕 그림을 지금 화면에 맞춘다(Theme.BuildBackground): 몸체 + 이 화면의 판(_page.Dials)·목록 머리줄. 새로 만들었으면 자식 컨트롤을 다시 그리게 한다
    /// — 자식은 그 그림을 자기 자리로 이어 그리므로(새 디자인) 그림이 바뀌면 같이 바뀌어야 한다.
    /// </summary>
    private void ComposeBackground()
    {
        if (_hwnd == 0) return;
        Native.GetClientRect(_hwnd, out Native.RECT rc);
        bool overlay = _page.Dials.Count > 0 || _page.ListHeader || _page.Metal;
        string key = !overlay ? "" : $"{_cur}|{string.Join(";", _page.Dials)}|{_page.ListHeader}|{_page.HeaderRight}|{L.Current}|{_pageScroll}|{_viewH}"
                                     + (_page.Metal ? $"|{string.Join(";", _page.Cards)}|{string.Join(";", _page.Separators)}|{string.Join(";", _page.Fields)}|{string.Join(";", _page.PillFields)}|{_page.Bubble}|{_page.Hero}|{_page.Title}|{_page.TitleL}|{_page.TitleR}" : "");
        if (Theme.BuildBackground(_hwnd, rc.right, rc.bottom, _dpi, key, overlay ? PageOverlay : null))
            foreach (nint h in _page.Controls) Native.InvalidateRect(h, 0, false);
    }

    /// <summary>
    /// 바탕 그림에 얹는 고정 장식: 파인 판, 새 디자인 화면의 조각(카드)·구분선·가운데 제목, 목록 머리줄(고양이 윤곽 18 + "내 항목" 15 + 버전 11 — 시안).
    /// 본문이 스크롤되면(T5) 보이는 칸 밖은 그리지 않는다 — 아래 고정 막대 자리를 덮지 않게.
    /// </summary>
    private void PageOverlay(nint dc, Metal.Surf s)
    {
        double k = _dpi / 96.0;
        bool dark = Theme.IsDark;
        Metal.Surf body = _pageScrollMax > 0 && _viewH > 0 && _viewH < s.H ? new Metal.Surf(s.P, s.W, _viewH) : s;
        foreach ((int x, int y, int w, int h) in _page.Dials)
            Metal.Dial(body, x * k, Yp(y), w * k, h * k, k, dark);
        if (_page.Metal)
        {
            foreach ((int x, int y, int w, int h) in _page.Cards)
                Metal.Tile(body, x * k, Yp(y), w * k, h * k, k, dark, 0);
            foreach ((int x, int y, int w) in _page.Separators)
                Metal.Sep(body, x * k, Yp(y), w * k, k, dark);
            if (_page.Hero is { } hero)
            {
                // 잠금 화면의 큰 둥근 금속 단추: 안에 지금 상태(잠김 = 자물쇠, 처음 설정 = 열쇠) — 2026-10-08 사용자: 고양이 윤곽은 뜬금없다
                double hd = hero.D * k, hx = hero.Cx * k - hd / 2, hy = Yp(hero.Y);
                Metal.Knob(body, hx, hy, hd, k, dark, 0, 2);
            }
            ExtraPaint(body, k);   // 배포본 전용 추가 화면(App.cs)
            if (_page.Bubble is { } bb) Metal.Plate(body, bb.X * k, Yp(bb.Y), bb.W * k, bb.H * k, 18 * k, k, dark, true);
            for (int fi = 0; fi < _page.Fields.Count; fi++)
            {
                (int x, int y, int w, int h, bool _) = _page.Fields[fi];
                if (_page.PillFields.Contains(fi))
                {
                    Metal.Plate(body, (x - 5) * k, Yp(y) - 5 * k, (w + 10) * k, (h + 10) * k, (h + 10) / 2.0 * k, k, dark, false);
                    Metal.LockWell(body, x * k, Yp(y), w * k, h * k, k, dark, false);
                }
                else Metal.Well(body, x * k, Yp(y), w * k, h * k, Theme.FieldRadius * k, k, dark);
            }
            if (_page.Hero is { } he)
            {
                double hd = he.D * k, hx = he.Cx * k - hd / 2, hy = Yp(he.Y);
                nint big = Native.MakeFont("Segoe Fluent Icons", (int)Math.Round(30 * k), Native.FW_NORMAL);
                if (big != 0)
                {
                    Ctl.Text(dc, big, _createMode ? "" : IcLock, Metal.Ref(Metal.InkIcon(dark)), (int)hx, (int)hy, (int)(hx + hd), (int)(hy + hd), Native.DT_CENTER | Native.DT_VCENTER);   // Permissions(열쇠) / Lock
                    Native.DeleteObject(big);
                }
            }
            if (_page.Title.Length > 0)
            {
                double tmid = Yp(8) + MetalUi.KnobD / 2.0 * k;
                int sv = Native.SaveDC(dc);
                if (body.H < s.H) Native.IntersectClipRect(dc, 0, 0, s.W, body.H);
                MetalUi.Text(dc, 17, true, _page.Title, Metal.Ink(dark), _page.TitleL * k, tmid - 14 * k, _page.TitleR * k, tmid + 14 * k, Native.DT_CENTER | Native.DT_VCENTER | Native.DT_END_ELLIPSIS);
                Native.RestoreDC(dc, sv);
            }
        }
        if (_page.Pic is { } pic)   // 고양이 그림(작업 표시줄 고양이와 같은 그림, 테마 색)
        {
            nint img = CatWidget.LoadPng(_page.PicName.Length > 0 ? _page.PicName : Theme.IsDark ? "cat_dark_center.png" : "cat_light_center.png");
            if (img != 0) { Gdiplus.DrawImageSmooth(dc, img, (int)Math.Round(pic.X * k), Yp(pic.Y), (int)Math.Round(pic.H * k), (int)Math.Round(pic.H * k)); CatWidget.FreeImage(img); }
        }
        if (!_page.ListHeader) return;
        double top = Yp(8), mid = top + MetalUi.KnobD / 2.0 * k;   // 머리줄 단추의 가운데 줄
        Gdiplus.DrawCatGlyph(dc, (float)(22 * k), (float)(mid - 9 * k), (float)(18 * k), Metal.Ref(Metal.Ink(dark)));
        double tx = 50 * k, right = _page.HeaderRight * k;
        MetalUi.Text(dc, 15, true, T.ListHeading, Metal.Ink(dark), tx, mid - 12 * k, right, mid + 12 * k, Native.DT_LEFT | Native.DT_VCENTER | Native.DT_END_ELLIPSIS);
        int tw = MetalUi.TextWidth(dc, 15, true, T.ListHeading);
        if (tx + tw + 8 * k < right)
            MetalUi.Text(dc, 11, false, "v" + Version, Metal.InkSub(dark), tx + tw + 8 * k, mid - 10 * k, right, mid + 11 * k, Native.DT_LEFT | Native.DT_VCENTER | Native.DT_END_ELLIPSIS);
    }

    private void FocusFieldAt(int px, int py)
    {
        foreach ((nint h, int x, int y, int w, int hh, bool field) in _page.Layout)
        {
            if (!field || !Native.IsWindowEnabled(h)) continue;
            if (px >= Scale(x) && px < Scale(x + w) && py >= Yp(y) && py < Yp(y) + Scale(hh)) { Native.SetFocus(h); return; }
        }
    }

    private void RethemeAll()
    {
        Theme.Detect();
        Theme.ApplyTitleBar(_hwnd);
        RebuildKeepingInput();   // 색 브러시가 바뀌었으므로 화면을 다시 만든다 (입력 중인 값은 유지)
        LockWidget.Retheme();    // 잠금 위젯도 같은 자리에서 색만 (입력 중인 값 유지)
    }
}
