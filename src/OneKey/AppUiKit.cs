using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>App 의 일부: 화면 만들기 공통 도구: 카드·행·버튼·칸·안내 줄·스크롤 페이지. (App.cs 에서 화면별로 나눔, 0.3.9 — 동작은 그대로)</summary>
internal sealed unsafe partial class App
{
    // ---------------------------------------------------------------- 화면 만들기 (공통 도구)

    private nint Make(string cls, string text, uint style, int x, int y, int w, int h, int id, uint ex = 0, nint font = 0)
    {
        nint handle;
        fixed (char* pc = cls) fixed (char* pt = text)
            // WS_CLIPSIBLINGS: 겹치는 형제(예: 행 위에 놓인 연필 버튼)를 덮어 그리지 않도록
            handle = Native.CreateWindowExW(ex, pc, pt, style | Native.WS_CHILD | Native.WS_VISIBLE | Native.WS_CLIPSIBLINGS,
                Scale(x), Scale(y), Scale(w), Scale(h), _hwnd, id, _hInst, 0);
        if (handle == 0) return 0;
        Native.SendMessageW(handle, Native.WM_SETFONT, font != 0 ? font : _font, 1);
        Theme.ApplyControl(handle, cls);
        _page.Controls.Add(handle);
        _page.Layout.Add((handle, x, y, w, h, false));
        _page.Placed.Add((handle, Scale(x), Scale(y), Scale(w), Scale(h), _page.BarTop > 0 && y >= _page.BarTop));
        if (id != 0) _controls[id] = handle;
        return handle;
    }

    /// <summary>STATIC 글자. onCard 면 카드 배경, 아니면 창 배경 위.</summary>
    /// userText: 사용자가 넣은 문자열(항목 이름)이면 true. & 를 단축키 밑줄 표시로 바꾸지 않고 그대로 보인다(SS_NOPREFIX).
    private nint Label(string text, int x, int y, int w, int h, nint font, uint color, bool onCard, uint align = Native.SS_LEFT, bool vcenter = true, bool userText = false)
    {
        if (_page.Metal)
        {
            // 새 디자인 화면: 조각 안 이름표(본문 글꼴)는 13.5px 굵게 · 시안 글자색, 보조 글은 시안의 작은 설명 색
            if (font == _font) { font = Theme.Sized(13.5, true); if (color == Theme.ControlText) color = Metal.Ref(Metal.Ink(Theme.IsDark)); }
            if (color == Theme.SecondaryText) color = Metal.Ref(Metal.InkNote(Theme.IsDark));
        }
        nint s = Make("STATIC", text, align | (vcenter ? Native.SS_CENTERIMAGE : 0) | (userText ? Native.SS_NOPREFIX : 0), x, y, w, h, 0, 0, font);
        if (s != 0) _staticStyle[s] = (onCard ? Theme.CardBrush : Theme.BgBrush, color);
        return s;
    }

    private void Header(string text, int y)
    {
        // 새 디자인: 판 안 묶음 이름(추가 메뉴의 "넣을 글"과 같다 — 11.5px, 이름표 색)
        if (_page.Metal) Label(text, Mx + 6, y + 2, Mw - 12, HeaderH - 4, Theme.Sized(11.5, false), Metal.Ref(Metal.InkLabel(Theme.IsDark)), false, Native.SS_LEFT | Native.SS_ENDELLIPSIS);
        else Label(text, Margin + 16, y, CardW - 32, HeaderH - 4, Theme.FontSmallStrong, Theme.SecondaryText, false);
    }

    /// <summary>카드의 왼쪽·폭(논리 px): 새 디자인 화면은 판(DialX) 안쪽 조각 자리, 아니면 예전 카드 자리.</summary>
    private int Mx => _page.Metal ? DialX + DialPadX : Margin;
    private int Mw => _page.Metal ? DialW - 2 * DialPadX : CardW;

    /// <summary>새 디자인 화면의 판: 머리줄 아래(top)부터 지금 y 까지 판을 두르고(아래 안쪽 여백 12) y 를 판 아래로 옮긴다.</summary>
    private void MetalDial(int top, ref int y)
    {
        if (!_page.Metal) return;
        y += DialPadBottom;
        _page.Dials.Add((DialX, top, DialW, y - top));
    }

    /// <summary>카드 아래 회색 설명문. lines 줄 높이를 잡는다.</summary>
    /// <summary>
    /// 글이 폭 w(논리 px)에서 차지하는 높이(논리 px). 줄 수를 어림으로 정하면 언어·배율에 따라 잘리거나 빈 줄이 남는다
    /// (2026-10-01 사용자: 입력 안내·사이트 안내가 잘림, 사이트 줄 아래 빈 줄) → 화면을 만들 때 실제 글꼴로 잰다. STATIC 은 DirectWrite 로 그리므로(2b)
    /// GDI 와 DirectWrite 로 모두 재서 큰 쪽 — 실행 중 GDI 로 바뀌어도 잘리지 않는다(Codex V2-3).
    /// </summary>
    private int TextH(nint font, string text, int w, bool editControl = false)
    {
        if (text.Length == 0) return 0;
        nint dc = Native.GetDC(_hwnd);
        nint old = Native.SelectObject(dc, font);
        var r = new Native.RECT { right = Scale(w) };
        Native.DrawText(dc, text, ref r, Native.DT_WORDBREAK | Native.DT_CALCRECT | Native.DT_NOPREFIX | (editControl ? 0x2000u /* DT_EDITCONTROL */ : 0u));
        Native.SelectObject(dc, old);
        Native.ReleaseDC(_hwnd, dc);
        int px = r.bottom;
        if (Dw.Measure(font, text, Scale(w)) is (int, int) m) px = Math.Max(px, m.Item2);
        return (int)Math.Ceiling(px * 96.0 / _dpi);
    }

    private int Footer(string text, int y)
    {
        int h = Math.Max(16, TextH(Theme.FontSmall, text, CardW - 32)) + 2;
        Label(text, Margin + 16, y + 4, CardW - 32, h, Theme.FontSmall, Theme.SecondaryText, false, Native.SS_LEFT, vcenter: false);
        return y + 4 + h;
    }

    /// <summary>
    /// 선택에 따라 바뀌는 짧은 안내 한 칸(Codex 개선안 C). 자리(lines 줄)는 늘 잡아 두어, 문구가 바뀌거나 비어도 화면이 들썩이지 않는다.
    /// 글은 Update*Note 가 채운다.
    /// </summary>
    /// <summary>바뀌는 안내 칸: 나올 수 있는 글(texts) 가운데 가장 긴 것이 들어가는 높이를 잡아 둔다(문구가 바뀌어도 들썩이지 않고 잘리지도 않게).</summary>
    private const int IdMMatch = 507;

    /// <summary>비밀번호·확인 칸 아래의 일치 여부 줄(작은 글씨). 확인 칸을 다 칠 때까지는 비워 둔다.</summary>
    private int MatchNote(int id, int y) => Note(id, y, T.PwMatch, T.PwMismatch);

    /// <summary>
    /// 일치 여부 줄을 고친다: 확인 칸이 비었거나 아직 앞부분만 친 중이면(첫 칸의 앞부분과 같고 더 짧음) 비우고, 그 밖에는 일치/불일치.
    /// 비밀번호는 문자열로 만들지 않고 소유 버퍼로 견준 뒤 지운다.
    /// </summary>
    private void UpdateMatchNote(int noteId, int id1, int id2)
    {
        nint h = C(noteId);
        if (h == 0 || C(id1) == 0 || C(id2) == 0) return;
        using var a = SecretText.FromWindow(C(id1));
        using var b = SecretText.FromWindow(C(id2));
        string text = "";
        bool bad = false;
        if (b.Length > 0 && !(b.Length < a.Length && a.Span[..b.Length].SequenceEqual(b.Span)))
        {
            bool ok = a.Span.SequenceEqual(b.Span);
            text = ok ? T.PwMatch : T.PwMismatch; bad = !ok;
        }
        _staticStyle[h] = (Theme.BgBrush, bad ? Theme.DangerText : Theme.SecondaryText);
        SetNote(noteId, text);
    }

    private int Note(int id, int y, params string[] texts)
    {
        int h = Math.Max(16, texts.Length == 0 ? 0 : texts.Max(t => TextH(Theme.FontSmall, t, CardW - 32))) + 2;
        nint s = Make("STATIC", "", Native.SS_LEFT, Margin + 16, y + 4, CardW - 32, h, id, 0, Theme.FontSmall);
        if (s != 0) _staticStyle[s] = (Theme.BgBrush, Theme.SecondaryText);
        return y + 4 + h;
    }

    /// <summary>
    /// 안내 줄의 글을 바꾼다. 화면을 만드는 도중(첫 그리기 전)에 넣은 글이 그려지지 않은 채 남는 경우가 있어,
    /// 바꿀 때마다 그 칸을 배경째 다시 그리게 한다.
    /// </summary>
    private void SetNote(int id, string text)
    {
        nint h = C(id);
        if (h == 0) return;
        Native.SetText(h, text);
        Native.InvalidateRect(h, 0, true);
    }

    /// <summary>
    /// 새 디자인 화면의 머리줄(시안): 왼쪽 ‹ 둥근 단추(backId, 화면 읽기 이름 = 뒤로) · 가운데 제목(바탕 그림, 17px 굵게) · 오른쪽 [?](help 면).
    /// 양옆 여백 20. 이 화면을 새 디자인으로 표시한다(_page.Metal). 판이 시작할 y(머리줄 아래 12)를 돌려준다.
    /// </summary>
    private int MetalHeader(int backId, string title, bool help = true)
    {
        _page.Metal = Theme.MetalPage = true;
        int y = 8;
        int kw = MetalUi.KnobD + 2 * MetalUi.KnobPadX, kh = MetalUi.KnobD + MetalUi.KnobPadTop + MetalUi.KnobPadBottom, ky = y - MetalUi.KnobPadTop;
        nint back = Button(backId, "\uE76B", Btn.Knob, 20 - MetalUi.KnobPadX, ky, kw, kh);   // ChevronLeft
        if (back != 0) Tip(back, T.CommonBack);
        if (help)
        {
            nint h = Button(IdHelp, "?", Btn.Knob, WinW - 20 - MetalUi.KnobD - MetalUi.KnobPadX, ky, kw, kh);
            if (h != 0) { CtlAcc.SetName(h, T.CommonHelp); Tip(h, T.CommonHelp); }
        }
        // 제목은 실제 STATIC(화면 읽기 프로그램이 읽고, 항목 이름의 & 는 그대로 — SS_NOPREFIX). 바탕은 바탕 그림을 이어 그린다
        int tl = 20 + MetalUi.KnobD + 8, tr = WinW - 20 - MetalUi.KnobD - 8;
        Label(title, tl, y, tr - tl, MetalUi.KnobD, Theme.Sized(17, true), Metal.Ref(Metal.Ink(Theme.IsDark)), false, Native.SS_CENTER | Native.SS_ENDELLIPSIS, userText: true);
        return y + MetalUi.KnobD + 12;
    }

    /// <summary>새 디자인 아래 막대의 큰 알약 버튼(높이 36, 창은 그늘 자리만큼 크다). kind = PillMain([취소]·[테스트]) · Prominent([저장]) · DangerBordered([삭제]).</summary>
    private nint BarPill(int id, string text, uint kind, int x, int y, int w, bool isDefault = false)
        => Button(id, text, kind, x - MetalUi.PillPadX, y - MetalUi.PillPadTop, w + 2 * MetalUi.PillPadX, MetalUi.PillMainH + MetalUi.PillPadTop + MetalUi.PillPadBottom, isDefault: isDefault);

    /// <summary>아래 막대 알약의 폭: 글 폭 + 40(최소 min).</summary>
    private int BarPillW(string text, int min = 64) => Math.Max(min, LabelW(Theme.Sized(13, true), text) + 40);

    /// <summary>화면 위 오른쪽의 [도움말] (← 뒤로 막대가 있는 화면).</summary>
    private void HelpButton() => Button(IdHelp, T.CommonHelp, Btn.Borderless, WinW - Margin - 72, 12, 72, 32);   // 윗줄은 목록 화면([+ 추가]·아이콘 버튼)과 같은 자리(2026-10-05 사용자: 들어가면 버튼이 옮겨 가 보임)

    /// <summary>글 폭(논리 px). STATIC 이름표는 아직 GDI 로 그리므로(2b 전) GDI 로 잰다.</summary>
    private int LabelW(nint font, string s)
    {
        nint dc = Native.GetDC(_hwnd);
        nint old = Native.SelectObject(dc, font);
        int w = Native.TextWidth(dc, s);
        Native.SelectObject(dc, old);
        Native.ReleaseDC(_hwnd, dc);
        return (int)Math.Ceiling(w * 96.0 / _dpi);
    }

    /// <summary>카드 왼쪽 이름표 열의 폭(논리 px): 가장 긴 이름표 + 여백을 min~max 로. 언어마다 이름표 길이가 달라 고정 폭이면 잘린다(다국어).</summary>
    private int LabelCol(int min, int max, params string[] labels) => Math.Clamp(labels.Max(s => LabelW(_page.Metal ? Theme.Sized(13.5, true) : _font, s)) + 14, min, max);   // 오른쪽 여백은 본문 스크롤 막대 자리

    /// <summary>지금 화면의 도움말을 연다. 모달이라 입력 중인 값·초안은 그대로이고, 닫으면 누르기 전 칸으로 포커스가 돌아간다.
    /// 잠금 화면에서는 잠금을 풀지 않고 볼 수 있는 정보(버전·글꼴 라이선스)를 연다. 목록 도움말과 정보에는 [라이선스 보기]가 있다.</summary>
    private void ShowHelp()
    {
        if (_cur == Screen.Lock)
        {
            if (Dialog.Show(_hwnd, Help.Info(Version), Help.InfoTitle, Native.MB_OK, width: 460, okText: T.CommonClose, extraText: Help.LicenseButton) == Dialog.IDEXTRA)
                ShowLicense();
            return;
        }
        if (_cur == Screen.List)
        {
            if (Dialog.Show(_hwnd, Help.List, Help.ListTitle, Native.MB_OK, width: 460, okText: T.CommonClose, extraText: Help.LicenseButton) == Dialog.IDEXTRA)
                ShowLicense();
            return;
        }
        (string title, string body)? h = _cur switch
        {
            Screen.List => (Help.ListTitle, Help.List),
            Screen.AddKind => (Help.ListTitle, Help.List),   // 새 디자인 머리줄의 [?](시안): 추가할 것들의 설명은 목록 도움말에 있다
            Screen.Edit => (Help.EditTitle, Help.Edit),
            Screen.Settings => (Help.SettingsTitle, Help.Settings),
            Screen.Advanced => (Help.AdvancedTitle, Help.Advanced),
            Screen.Master => (Help.MasterTitle, Help.Master),
            Screen.BackupMake or Screen.Restore => (T.HelpBackupTitle, T.HelpBackup),
            _ => null,
        };
        if (h is { } t) Dialog.Show(_hwnd, t.body, t.title, Native.MB_OK, width: 460, okText: T.CommonClose);
    }

    /// <summary>글꼴 라이선스 전문(OFL 1.1, 저작권 고지 포함). 스크롤·복사되는 읽기 전용 본문.</summary>
    private void ShowLicense() => Dialog.Show(_hwnd, Dw.LicenseText(), Help.LicenseTitle, Native.MB_OK, width: 520, okText: T.CommonClose, maxBodyH: 360);

    /// <summary>렌더러가 GDI 로 바뀐 뒤(Dw 의 메시지 전용 창이 안전한 시점에 부름): 지금 화면을 상태를 보존해 다시 배치한다.</summary>
    private void OnRendererChanged() => RelayoutKeepingView();

    /// <summary>
    /// 화면을 다시 만들되 입력값(RebuildKeepingInput)에 더해 포커스·편집칸 선택·본문 스크롤을 보존한다 (Codex v2 검토 V2-2).
    /// 모달이 떠 있으면 본창에서 포커스를 옮기지 않는다(상자가 닫힐 때 원래 칸으로 돌아간다). 그 사이 잠겼으면 아무것도 되돌리지 않는다.
    /// </summary>
    private void RelayoutKeepingView()
    {
        var v = CaptureView();
        RebuildKeepingInput();
        RestoreView(v);
    }

    private readonly record struct ViewState(Screen Screen, int FocusId, int SelStart, int SelEnd, int Scroll, int LockGen, bool Unlocked);

    private ViewState CaptureView()
    {
        nint f = Native.GetFocus();
        int id = f != 0 && Native.IsChild(_hwnd, f) ? Native.GetDlgCtrlID(f) : 0;
        int s0 = -1, s1 = -1;
        if (id != 0 && Native.GetClassName(f).Equals("Edit", StringComparison.OrdinalIgnoreCase))
        {
            int a = 0, b = 0;
            Native.SendMessageW(f, Native.EM_GETSEL, (nint)(&a), (nint)(&b));
            s0 = a; s1 = b;
        }
        return new ViewState(_cur, id, s0, s1, _pageScroll, Dialog.LockGeneration, Unlocked);
    }

    private void RestoreView(ViewState v)
    {
        // 잠금이 우선: 그 사이 잠겼거나(세대가 바뀜) 다른 화면이 되었으면 초안·선택·스크롤을 되쓰지 않는다
        if (!v.Unlocked && v.Screen != Screen.Lock) return;
        if (Dialog.LockGeneration != v.LockGen || _cur != v.Screen) return;
        if (v.Scroll > 0 && _pageScrollMax > 0) ScrollPage(Math.Min(v.Scroll, _pageScrollMax) - _pageScroll);
        try
        {
            if (Dialog.OpenCount > 0 || v.FocusId == 0) return;
            nint h = C(v.FocusId);
            if (h == 0) return;
            Native.SetFocus(h);
            if (v.SelStart >= 0) Native.SendMessageW(h, Native.EM_SETSEL, v.SelStart, v.SelEnd);
        }
        finally
        {
            // 되살린 포커스를 "이미 본 포커스"로 둔다: 안 그러면 메시지 루프의 EnsureFocusVisible 이 새 포커스로 보고
            // 그 컨트롤이 보이도록 본문을 다시 스크롤해, 되살린 스크롤 위치를 잃는다(0.2.55 시험 DW05).
            _lastFocusSeen = Native.GetFocus();
        }
    }

    private void Card(int y, int h) => _page.Cards.Add((Mx, y, Mw, h));

    private void Separator(int y)
    {
        if (_page.Metal) _page.Separators.Add((Mx + Row.PadX, y, Mw - 2 * Row.PadX));   // 새 디자인: 양옆 14 안쪽(시안)
        else _page.Separators.Add((Margin + Row.PadX, y, CardW - Row.PadX));
    }

    /// <summary>입력칸. (x,y,w,h) 는 보이는 상자. 실제 EDIT 는 글자 높이로 상자 가운데에 놓인다.</summary>
    private nint Field(int id, int x, int y, int w, int h, uint extra = 0, bool center = false, bool onCard = true, int leftPad = 0, int rightPad = 0)
    {
        int t = Math.Max(1, Scale(1));
        int bh = Scale(h), ih = Math.Min(_textH, bh - 2 * t), pad = Scale(10), lp = Scale(leftPad), rp = Scale(rightPad);   // leftPad: 상자 왼쪽 안에 아이콘 자리, rightPad: 오른쪽 안 글 자리
        uint style = Native.ES_AUTOHSCROLL | Native.WS_TABSTOP | extra | Native.WS_CHILD | Native.WS_VISIBLE | (center ? 1u : 0u);
        nint e;
        fixed (char* pc = "EDIT") fixed (char* pt = "")
            e = Native.CreateWindowExW(0, pc, pt, style, Scale(x) + pad + lp, Scale(y) + (bh - ih) / 2, Scale(w) - 2 * pad - lp - rp, ih, _hwnd, id, _hInst, 0);
        if (e == 0) return 0;
        Native.SendMessageW(e, Native.WM_SETFONT, _font, 1);
        Theme.ApplyControl(e, "EDIT");
        _page.Controls.Add(e);
        _page.Fields.Add((x, y, w, h, onCard));
        _page.FieldEdits.Add(e);
        _page.Layout.Add((e, x, y, w, h, true));
        _page.Placed.Add((e, Scale(x) + pad + lp, Scale(y) + (bh - ih) / 2, Scale(w) - 2 * pad - lp - rp, ih, _page.BarTop > 0 && y >= _page.BarTop));
        _edits.Add(e);
        if (!onCard) _editsOnBg.Add(e);
        _controls[id] = e;
        return e;
    }

    private nint Button(int id, string text, uint kind, int x, int y, int w, int h, bool onCard = false, bool isDefault = false)
        => Make(Btn.ClassName, text, kind | (onCard ? Btn.OnCard : 0) | (isDefault ? Btn.Default : 0) | Native.WS_TABSTOP, x, y, w, h, id);

    /// <summary>설명을 바꾼다(처음이면 붙인다). 한 줄로 줄여 보이는 글의 전체 내용 등.</summary>
    private void SetTip(nint ctrl, string text)
    {
        if (_tooltip == 0 || ctrl == 0) return;
        if (!_tipTools.Contains(ctrl)) { Tip(ctrl, text); return; }
        fixed (char* p = text)
        {
            var ti = new Native.TOOLINFOW { cbSize = (uint)sizeof(Native.TOOLINFOW), uFlags = Native.TTF_IDISHWND, hwnd = _hwnd, uId = (nuint)ctrl, lpszText = p };
            Native.SendMessageTI(_tooltip, 0x0439 /* TTM_UPDATETIPTEXTW */, 0, ref ti);
        }
    }

    /// <summary>아이콘 버튼처럼 글자가 없는 컨트롤에 마우스를 올리면 뜨는 설명.</summary>
    private void Tip(nint ctrl, string text)
    {
        // 아이콘 글자만 있는 버튼(‹ › 등)은 툴팁 글이 접근성 이름(Codex QA-06)
        if (ctrl != 0 && Native.GetWindowText(ctrl) is string t0 && t0.Length > 0 && t0.All(c => c >= 0xE000 && c <= 0xF8FF)) CtlAcc.SetName(ctrl, text);
        if (_tooltip == 0 || ctrl == 0) return;
        fixed (char* p = text)
        {
            var ti = new Native.TOOLINFOW { cbSize = (uint)sizeof(Native.TOOLINFOW), uFlags = Native.TTF_IDISHWND | Native.TTF_SUBCLASS, hwnd = _hwnd, uId = (nuint)ctrl, lpszText = p };
            Native.SendMessageTI(_tooltip, Native.TTM_ADDTOOLW, 0, ref ti);
        }
        _tipTools.Add(ctrl);
    }

    private nint ListRow(int id, string title, string subtitle, string icon, uint flags, int y)
    {
        nint r = Make(Row.ClassName, title, flags | Native.WS_TABSTOP, Mx, y, Mw, RowH, id);
        if (r != 0) Row.Set(r, title, subtitle, icon);
        return r;
    }

    private nint ToggleRow(int id, string text, int y, bool last)
    {
        nint t = Make(Toggle.ClassName, text, Toggle.StyleTrailing | (last ? 0 : Toggle.StyleSeparator) | Native.WS_TABSTOP, Mx + Row.PadX, y, Mw - Row.PadX * 2, RowH, id, 0, _page.Metal ? Theme.Sized(13.5, true) : 0);
        if (!last) Separator(y + RowH - 1);   // 컨트롤 오른쪽 바깥 14px 구간은 부모가 이어 그린다
        return t;
    }

    private void SetCheck(int id, bool on) => Native.SendMessageW(C(id), Native.BM_SETCHECK, on ? Native.BST_CHECKED : Native.BST_UNCHECKED, 0);
    private bool GetCheck(int id) => Native.SendMessageW(C(id), Native.BM_GETCHECK, 0, 0) == Native.BST_CHECKED;
    private int ComboSel(int id) => (int)Native.SendMessageW(C(id), Native.CB_GETCURSEL, 0, 0);
    private void ComboSel(int id, int i) => Native.SendMessageW(C(id), Native.CB_SETCURSEL, i, 0);

    private void ClearPage()
    {
        foreach (nint h in _page.Controls) DestroyPageControl(h);
        var stale = _controls.Where(kv => _page.Controls.Contains(kv.Value)).Select(kv => kv.Key).ToList();
        foreach (int k in stale) _controls.Remove(k);
        _page = new Page();
        Theme.MetalPage = false;   // 새 디자인 화면이면 그 화면을 만드는 쪽이 다시 켠다
    }

    private void DestroyPageControl(nint h)
    {
        if (_tooltip != 0 && _tipTools.Remove(h))
        {
            var ti = new Native.TOOLINFOW { cbSize = (uint)sizeof(Native.TOOLINFOW), uFlags = Native.TTF_IDISHWND, hwnd = _hwnd, uId = (nuint)h };
            Native.SendMessageTI(_tooltip, Native.TTM_DELTOOLW, 0, ref ti);
        }
        Ctl.Partner.Remove(h);
        _edits.Remove(h);
        _editsOnBg.Remove(h);
        _staticStyle.Remove(h);
        Native.DestroyWindow(h);
    }

    /// <summary>표시(m) 뒤에 만든 컨트롤·장식만 없앤다 — 화면의 앞부분(검색 칸 등)은 그대로 두고 아래만 다시 만들 때.</summary>
    private void DropAfter(Page.Marks m)
    {
        var gone = _page.Controls.Skip(m.Controls).ToList();
        foreach (nint h in gone) DestroyPageControl(h);
        foreach (int k in _controls.Where(kv => gone.Contains(kv.Value)).Select(kv => kv.Key).ToList()) _controls.Remove(k);
        _page.Controls.RemoveRange(m.Controls, _page.Controls.Count - m.Controls);
        _page.Cards.RemoveRange(m.Cards, _page.Cards.Count - m.Cards);
        _page.Fields.RemoveRange(m.Fields, _page.Fields.Count - m.Fields);
        _page.FieldEdits.RemoveRange(m.FieldEdits, _page.FieldEdits.Count - m.FieldEdits);
        _page.Separators.RemoveRange(m.Separators, _page.Separators.Count - m.Separators);
        _page.RowStrips.RemoveRange(m.RowStrips, _page.RowStrips.Count - m.RowStrips);
        _page.Layout.RemoveRange(m.Layout, _page.Layout.Count - m.Layout);
        _page.Placed.RemoveRange(m.Placed, _page.Placed.Count - m.Placed);
    }

    private void ShowScreen(Screen s, bool animate = false)
    {
        // 잠겨 있으면 어떤 경로로 와도 잠금 화면이다. (예: 편집 화면의 확인 상자가 떠 있는 동안 자동 잠금이 걸린 뒤 "예"를 누른 경우)
        // 처음 설정(마스터 없음)에서만 복원 화면은 예외(다른 PC 의 백업을 가져와 시작 — 지울 기존 설정이 없다)
        if (s != Screen.Lock && !Unlocked && !(s == Screen.Restore && _createMode)) s = Screen.Lock;

        int prevH = _clientH;
        bool leavingEdit = _cur == Screen.Edit && s != Screen.Edit;
        if (leavingEdit) CancelTest();   // 편집 화면을 떠나면 3초 뒤 테스트 입력도 취소
        if (leavingEdit) RegisterHotkeys(silent: true);   // 편집 중엔 녹음을 위해 풀어 두었다. 목록을 만들기 전에 해야 등록 실패 표시가 맞다.
        // 실행 항목 편집도 녹음 동안 모두 풀어 두었다. [다시 고르기]로 프로그램 고르기에 다녀오는 동안은 그대로 푼 채로 둔다
        if (_cur == Screen.LaunchEdit && s != Screen.LaunchEdit && !(s == Screen.PickProgram && _pickForRepick)) { RegisterHotkeys(silent: true); RegisterLaunchHotkeys(silent: true); }
        if (_cur == Screen.PickProgram && _pickForRepick && s != Screen.LaunchEdit && s != Screen.PickProgram) { RegisterHotkeys(silent: true); RegisterLaunchHotkeys(silent: true); }
        bool settingsShown = _cur == Screen.Settings;
        // 접거나 하위 화면으로 떠나도 입력값 유지. 잠금으로 가는 길에는 모으지 않는다(Codex R1: DoLock 이 버린 초안을 여기서 다시 읽어
        // 잠금 해제 뒤 되살리면 안 된다). 잠겨 있으면 s 는 위에서 이미 Lock 이다.
        if (settingsShown && C(IdAutoStart) != 0 && !_dropSettingsDraft && s != Screen.Lock) _settingsDraft = ReadSettings();
        _dropSettingsDraft = false;
        // 테마 미리보기(2026-09-29 사용자 요청): 설정 화면에서는 초안의 테마, 그 밖의 화면은 저장된 테마.
        // 그래서 [저장]하지 않고 어디로 나가도(←·[취소]·하위 화면·잠금) 원래 테마로 돌아오고, 초안이 남은 채 설정으로 돌아오면 다시 미리 보인다.
        int wantTheme = s == Screen.Settings ? (_settingsDraft?.ThemeIdx ?? _cfg.ThemeMode) : _cfg.ThemeMode;
        if (wantTheme != Theme.Mode) { Theme.Mode = wantTheme; Theme.Detect(); Theme.ApplyTitleBar(_hwnd); }
        // 언어도 테마와 같게(다국어, 2026-09-30): 설정 화면에서는 초안의 언어를 미리 보이고, 그 밖에는 저장된 언어.
        int wantLang = s == Screen.Settings ? (_settingsDraft?.LangIdx ?? LangIndex(_cfg.Language)) : LangIndex(_cfg.Language);
        ApplyLanguage(LangCode(wantLang));
        if (_pageDragOffset >= 0) { _pageDragOffset = -1; Native.ReleaseCapture(); }   // 스크롤 막대를 끄는 중에 화면이 바뀌면 끌기를 끝낸다
        _pageScroll = _pageScrollMax = _barShift = 0; _viewH = 0; _lastFocusSeen = 0;
        ClearPage();
        if (s != _cur) _screenSerial++;   // 다른 화면으로: 그 전 화면에서 시작한 작업의 결과는 버린다(R41-2)
        _cur = s;
        int avail = AvailableClientH();
        switch (s)
        {
            case Screen.Lock: BuildLock(); break;
            case Screen.List:
                // 작업 영역에 맞춰 목록 행 수를 8 → 3 까지 줄인다 (T5). 목록이 그보다 짧으면 줄여도 소용없으니 멈춘다.
                _visibleRows = MaxVisibleRows;
                BuildList();
                if (_page.Height > avail && _listRowsBuilt >= _visibleRows)
                {
                    // 넘치는 만큼 한 번에 줄여 다시 만든다 (행마다 다시 만들면 설정을 펼친 화면에서 느리다)
                    int drop = (_page.Height - avail + ListRowH - 1) / ListRowH;   // 목록 행은 판 위 조각 높이
                    int rows = Math.Max(MinVisibleRows, _visibleRows - drop);
                    if (rows < _visibleRows) { _visibleRows = rows; ClearPage(); BuildList(); }
                }
                break;
            case Screen.Edit: BuildEdit(); break;
            case Screen.Advanced: BuildAdvanced(); break;
            case Screen.Master: BuildMaster(); break;
            case Screen.Settings: BuildSettingsScreen(); break;
            case Screen.AddKind: BuildAddKind(); break;
            case Screen.LaunchEdit: BuildLaunchEdit(); break;
            case Screen.PickProgram: BuildPickProgram(); break;
            case Screen.BackupMake: BuildBackupMake(); break;
            case Screen.Restore: BuildRestore(); break;
        }
        KeepCardBorders();
        int height = _page.Height;
        if (s != Screen.List && height > avail) height = SetUpPageScroll(avail);   // 그래도 길면 본문만 스크롤, 아래 막대는 고정
        if (animate && prevH > 0 && prevH != height && Fx.Animations) StartHeightAnimation(prevH, height);
        else { Native.KillTimer(_hwnd, TimerAnim); ResizeClient(height); }   // 진행 중이던 애니메이션이 새 화면 높이를 덮어쓰지 않도록
        Native.SetText(_hwnd, AppTitle + (Unlocked ? "" : "  " + T.CommonLockedSuffix));   // 작업 표시줄에도 같은 글자가 보이므로 버전은 창 안에 둔다
        ComposeBackground();   // 새 디자인: 판·머리줄이 든 바탕 그림을 자식보다 먼저(자식은 그 그림을 이어 그린다)
        Native.RedrawWindow(_hwnd, 0, 0, Native.RDW_INVALIDATE | Native.RDW_ERASE | Native.RDW_ALLCHILDREN | Native.RDW_UPDATENOW);
        FocusFirst();
        UpdateSiteWatch();   // 사이트 채우기: 잠금 해제·저장·삭제·잠금 뒤 감시를 켜거나 끈다
    }

    /// <summary>
    /// 카드의 1px 테두리(B)는 부모 창이 그린다. 카드 안 컨트롤(글자 칸·슬라이더·드롭다운 등)이 그 1px 에 걸치면 그 자리만 테두리가 끊겨 보인다
    /// (2026-10-03 사용자: 설정 카드 테두리가 중간에 끊김). 화면을 만든 뒤 테두리에 걸친 컨트롤을 테두리 두께만큼 카드 안쪽으로 줄인다.
    /// 스크롤 위치 목록(Placed)도 같이 고쳐 스크롤해도 다시 걸치지 않게 한다. 목록 행(Row)은 자기 몫의 테두리를 직접 그리므로 뺀다.
    /// </summary>
    private void KeepCardBorders()
    {
        if (_page.Cards.Count == 0) return;
        int bw = Math.Max(1, Scale(1));
        for (int i = 0; i < _page.Placed.Count; i++)
        {
            var c = _page.Placed[i];
            string cls = Native.GetClassName(c.H);
            if (cls == Row.ClassName) continue;
            // 바로 실행 띠: 칸은 스크롤로 상자 밖까지 놓이고 창 영역으로 잘린다, 연필 버튼은 일부러 모서리에 걸친다
            if (cls == Tile.ClassName || (cls == Btn.ClassName && (Ctl.Style(c.H) & Btn.KindMask) == Btn.Badge)) continue;
            int l = c.X, t = c.Y, r = c.X + c.W, b = c.Y + c.Hh;
            foreach ((int x, int y, int w, int h) in _page.Cards)
            {
                int cl = Scale(x), ct = Scale(y), cr = Scale(x + w), cb = Scale(y + h);
                if (r <= cl || l >= cr || b <= ct || t >= cb) continue;   // 이 카드와 겹치지 않음
                if (t < ct + bw && b > ct + bw) t = ct + bw;              // 위 테두리
                if (b > cb - bw && t < cb - bw) b = cb - bw;              // 아래 테두리
                if (l < cl + bw && r > cl + bw) l = cl + bw;              // 왼쪽 테두리
                if (r > cr - bw && l < cr - bw) r = cr - bw;              // 오른쪽 테두리
            }
            if (l == c.X && t == c.Y && r == c.X + c.W && b == c.Y + c.Hh) continue;
            _page.Placed[i] = (c.H, l, t, r - l, b - t, c.Bar);
            Native.SetWindowPos(c.H, 0, l, t, r - l, b - t, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }
    }

    /// <summary>창 높이를 감속 곡선으로 부드럽게 바꾼다 (설정 펼침/접힘). 내용은 이미 바뀌어 있고 창이 따라간다.</summary>
    private void StartHeightAnimation(int from, int to)
    {
        _animFrom = from; _animTo = to; _animStart = Environment.TickCount64;
        Native.KillTimer(_hwnd, TimerAnim);
        Native.SetTimer(_hwnd, TimerAnim, 10, 0);
    }

    private void StepHeightAnimation()
    {
        double t = Math.Clamp((Environment.TickCount64 - _animStart) / (double)AnimMs, 0, 1);
        double eased = 1 - Math.Pow(1 - t, 3);   // ease-out
        int h = (int)Math.Round(_animFrom + (_animTo - _animFrom) * eased);
        ResizeClient(h);
        if (t >= 1) { Native.KillTimer(_hwnd, TimerAnim); ResizeClient(_animTo); }
    }

    private void FocusFirst()
    {
        if (!Native.IsWindowVisible(_hwnd)) return;   // 숨은 본창의 칸에 커서를 주면 본창이 활성화돼 잠금 위젯이 줄어든다
        int id = _cur switch { Screen.Lock => IdLockPw1, Screen.Edit => IdEName, Screen.Advanced => IdADelay, Screen.Master => IdMCur,
                               Screen.AddKind => IdAkPassword, Screen.LaunchEdit => IdLeName, Screen.PickProgram => IdPkSearch,
                               Screen.BackupMake => IdBkCur, Screen.Restore => _restorePath is null ? IdRsBrowse : IdRsPw,
                               // 목록: 검색 칸 → 추가 행 → 첫 슬롯 행. 슬롯 행은 Enter 로 입력이 실행되므로 처음 포커스로는 두지 않는다.
                               Screen.List => C(IdListFilter) != 0 ? IdListFilter : C(IdRowAdd) != 0 ? IdRowAdd : FirstSlotRowId(), _ => 0 };
        nint h = id != 0 ? C(id) : (_page.Controls.Count > 0 ? _page.Controls.FirstOrDefault(c => (Ctl.Style(c) & Native.WS_TABSTOP) != 0) : 0);
        if (h != 0) Native.SetFocus(h);
    }
}
