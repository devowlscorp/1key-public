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
        foreach ((int x, int y, int w) in _page.Separators)
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
            // 커서가 있는 입력칸은 강조색 테두리: 키 조합 칸·드롭다운과 같은 규칙(라이트·다크 모두 AccentInk)
            uint border = fi < _page.FieldEdits.Count && _page.FieldEdits[fi] == focused && focused != 0 ? Theme.AccentInk : Theme.FieldBorder;
            // 카드 위: 입력칸 색 / 바탕 위: 카드 색 (바탕과 구분되도록), 둘 다 가는 테두리
            Gdiplus.FillRoundRect(hdc, Scale(x), Yp(y), Scale(w), Scale(h), Scale(Theme.FieldRadius), onCard ? Theme.EditBg : Theme.CardBg);
            Gdiplus.DrawRoundRect(hdc, Scale(x), Yp(y), Scale(w), Scale(h), Scale(Theme.FieldRadius), border, Math.Max(1, Scale(1)));
        }

        if (_cur == Screen.Lock)
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
