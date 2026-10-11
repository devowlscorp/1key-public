using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>App 의 일부: 창 바탕·카드 그리기. (App.cs 에서 화면별로 나눔, 0.3.9 — 동작은 그대로)</summary>
internal sealed unsafe partial class App
{
    // ---------------------------------------------------------------- 그리기

    [DllImport("user32.dll")] private static extern nint GetDCEx(nint hwnd, nint hrgnClip, uint flags);

    /// <summary>
    /// 창 크기를 바꾸기 전에 새 화면 한 장을 메모리에 그린다(바탕 + 장식 + 컨트롤마다 WM_PRINTCLIENT). 크기를 바꾼 바로 뒤 <see cref="BlitPrePaint"/> 로
    /// 붙이면 커진 창이 빈 아래를 보이는 틈이 없다 — 화면을 다 칠하는 데(약 25 ms) 한 프레임 넘게 걸려, 그동안 커진 창 아래가 비어 보였다
    /// (2026-10-11 사용자: 설정 화면의 아래 후원 줄이 나중에 나타난다). 본문이 스크롤되는 화면은 하지 않는다(아래 막대와의 겹침을 여기서 다루지 않음).
    /// </summary>
    private nint PrePaint(int w, int h)
    {
        if (_pageScrollMax > 0 || w <= 0 || h <= 0) return 0;
        nint screen = Native.GetDC(0);
        nint mem = Native.CreateCompatibleDC(screen), bmp = Native.CreateCompatibleBitmap(screen, w, h);
        Native.ReleaseDC(0, screen);
        if (mem == 0 || bmp == 0) { if (mem != 0) Native.DeleteDC(mem); if (bmp != 0) Native.DeleteObject(bmp); return 0; }
        nint old = Native.SelectObject(mem, bmp);
        var rc = new Native.RECT { right = w, bottom = h };
        Native.SetBrushOrgEx(mem, 0, 0, 0);
        Native.FillRect(mem, ref rc, Theme.BgBrush);
        DrawDecorations(mem);
        // 컨트롤마다 제 크기의 그림에 그린 뒤 옮겨 붙인다 — 보통 그리기와 같은 원점이어야 이름표가 바탕 무늬를 제자리에 맞춘다(원점만 옮겨 그리면 어긋나 띠가 졌다)
        nint cdc = Native.CreateCompatibleDC(mem);
        foreach (nint c in _page.Controls)
        {
            if (!Native.IsWindowVisible(c) || !Native.GetClientRect(c, out Native.RECT cr) || cr.right <= 0 || cr.bottom <= 0) continue;
            var pt = new Native.POINT();
            Native.MapWindowPoints(c, _hwnd, ref pt, 1);
            nint cb = Native.CreateCompatibleBitmap(mem, cr.right, cr.bottom);
            if (cb == 0) continue;
            nint cold = Native.SelectObject(cdc, cb);
            Native.BitBlt(cdc, 0, 0, cr.right, cr.bottom, mem, pt.x, pt.y, Native.SRCCOPY);   // 그 자리의 바탕(투명하게 그리는 컨트롤)
            Native.SendMessageW(c, Native.WM_PRINTCLIENT, cdc, 0x04 | 0x08 /* PRF_CLIENT | PRF_ERASEBKGND */);
            Native.BitBlt(mem, pt.x, pt.y, cr.right, cr.bottom, cdc, 0, 0, Native.SRCCOPY);
            Native.SelectObject(cdc, cold);
            Native.DeleteObject(cb);
        }
        Native.DeleteDC(cdc);
        Native.SelectObject(mem, old);
        Native.DeleteDC(mem);
        return bmp;
    }

    [DllImport("gdi32.dll", EntryPoint = "StretchBlt")] private static extern bool StretchBltSnap(nint dst, int x, int y, int w, int h, nint src, int sx, int sy, int sw, int sh, uint rop);
    [StructLayout(LayoutKind.Sequential)] private struct SnapBlend { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [DllImport("user32.dll", EntryPoint = "UpdateLayeredWindow")]
    private static extern bool UlwSnap(nint hwnd, nint hdcDst, ref Native.POINT dst, ref Native.SIZE size, nint hdcSrc, ref Native.POINT src, uint key, ref SnapBlend blend, uint flags);

    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint h, uint attr, out Native.RECT value, uint size);

    /// <summary>
    /// 창이 커지는 동안 새 창 자리를 덮는 그림 창(소유 창 = 본창이라 본창 바로 위, 활성화하지 않음). 창 크기를 바꾸면 화면 합성기가 새로 칠하기 전의 커진 창을
    /// 한 프레임 내보내 아래가 비어 보였다(측정: 매번 한 장) — 이 그림이 그동안 덮고, 본창을 다 칠한 뒤 걷는다.
    /// 자리는 보이는 창 경계(DWM — GetWindowRect 의 보이지 않는 테두리 7 px 는 빼야 두꺼운 테가 비치지 않는다). 위 제목 표시줄은 지금 화면의 본창 제목 줄을
    /// 그대로 옮기고(본창이 맨 앞일 때만 — 아니면 덮지 않는다: 덮인 다른 창을 옮기지 않게), 그 아래는 <see cref="PrePaint"/> 그림. 실패하면 0.
    /// </summary>
    private nint ShowSnapshot(nint pre, (int X, int Y, int W, int H, int Cx, int Cy) t, int cw, int ch)
    {
        if (pre == 0 || Native.GetForegroundWindow() != _hwnd) return 0;
        Native.GetWindowRect(_hwnd, out Native.RECT wr);
        if (DwmGetWindowAttribute(_hwnd, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out Native.RECT ext, 16) != 0) return 0;
        int il = ext.left - wr.left, it = ext.top - wr.top, ir = wr.right - ext.right, ib = wr.bottom - ext.bottom;
        int sx = t.X + il, sy = t.Y + it, sw = t.W - il - ir, sh = t.H - it - ib, ccx = t.Cx - il, ccy = t.Cy - it;
        if (sw <= 0 || sh <= 0 || ccy < 0 || ccx < 0) return 0;
        nint screen = Native.GetDC(0);
        nint mem = Native.CreateCompatibleDC(screen), bmp = Native.CreateCompatibleBitmap(screen, sw, sh), pmem = Native.CreateCompatibleDC(screen);
        nint snap = 0;
        try
        {
            if (mem == 0 || bmp == 0 || pmem == 0) return 0;
            nint o1 = Native.SelectObject(mem, bmp), o3 = Native.SelectObject(pmem, pre);
            // 테두리 색(왼쪽 테두리 가운데 한 점)으로 깔고, 위 제목 줄은 지금 화면의 것, 그 아래는 새 화면
            StretchBltSnap(mem, 0, 0, sw, sh, screen, ext.left, (ext.top + ext.bottom) / 2, 1, 1, Native.SRCCOPY);
            Native.BitBlt(mem, 0, 0, Math.Min(sw, ext.right - ext.left), ccy, screen, ext.left, ext.top, Native.SRCCOPY);
            Native.BitBlt(mem, ccx, ccy, cw, ch, pmem, 0, 0, Native.SRCCOPY);
            Native.SelectObject(pmem, o3);
            fixed (char* cls = "STATIC") fixed (char* tx = "")
                snap = Native.CreateWindowExW(0x00080000u /* WS_EX_LAYERED */ | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE, cls, tx, Native.WS_POPUP, sx, sy, sw, sh, _hwnd, 0, _hInst, 0);
            if (snap != 0)
            {
                var dst = new Native.POINT { x = sx, y = sy }; var zero = new Native.POINT(); var size = new Native.SIZE { cx = sw, cy = sh };
                var blend = new SnapBlend { SourceConstantAlpha = 255 };   // 불투명(픽셀 알파는 쓰지 않음)
                if (UlwSnap(snap, screen, ref dst, ref size, mem, ref zero, 0, ref blend, 2 /* ULW_ALPHA */)) Native.ShowWindow(snap, Native.SW_SHOWNOACTIVATE);
                else { Native.DestroyWindow(snap); snap = 0; }
            }
            Native.SelectObject(mem, o1);
            return snap;
        }
        finally
        {
            if (pmem != 0) Native.DeleteDC(pmem);
            if (mem != 0) Native.DeleteDC(mem);
            if (bmp != 0) Native.DeleteObject(bmp);
            Native.ReleaseDC(0, screen);
        }
    }

    /// <summary><see cref="PrePaint"/> 의 그림을 창에 바로 붙이고 지운다(이어서 보통 그리기가 같은 그림을 다시 그린다).</summary>
    private void BlitPrePaint(nint bmp, int w, int h)
    {
        if (bmp == 0) return;
        // 자식 자리도 덮는 DC(DCX_CACHE 만 — WS_CLIPCHILDREN 인 본창의 GetDC 는 컨트롤 자리를 빼서 그 자리가 빈 채로 보였다). 자식은 본창과 같은 그림판을 쓰므로
        // 그 자리에 그린 것이 컨트롤이 다시 그릴 때까지 보인다
        nint dc = GetDCEx(_hwnd, 0, 0x00000002 /* DCX_CACHE */), mem = Native.CreateCompatibleDC(dc);
        nint old = Native.SelectObject(mem, bmp);
        Native.BitBlt(dc, 0, 0, w, h, mem, 0, 0, Native.SRCCOPY);
        Native.SelectObject(mem, old);
        Native.DeleteDC(mem);
        Native.ReleaseDC(_hwnd, dc);
        Native.DeleteObject(bmp);
    }

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
    /// <param name="w">h: 만들 크기(px) — 창을 키우기 전에 새 크기로 미리 만들 때(ShowScreen). 생략하면 지금 창 크기.</param>
    private void ComposeBackground(int w = -1, int h = -1)
    {
        if (_hwnd == 0) return;
        Native.GetClientRect(_hwnd, out Native.RECT rc);
        if (w > 0 && h > 0) { rc.right = w; rc.bottom = h; }
        bool overlay = _page.Dials.Count > 0 || _page.ListHeader || _page.Metal;
        string key = !overlay ? "" : $"{_cur}|{string.Join(";", _page.Dials)}|{_page.ListHeader}|{_page.HeaderRight}|{L.Current}|{_pageScroll}|{_viewH}"
                                     + (_page.Metal ? $"|{string.Join(";", _page.Cards)}|{string.Join(";", _page.Separators)}|{string.Join(";", _page.Fields)}|{string.Join(";", _page.PillFields)}|{_page.Bubble}|{_page.Hero}|{_page.Title}|{_page.TitleL}|{_page.TitleR}" : "");
        if (Theme.BuildBackground(_hwnd, rc.right, rc.bottom, _dpi, key, overlay ? PageOverlay : null))
            foreach (nint c in _page.Controls) Native.InvalidateRect(c, 0, false);
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
