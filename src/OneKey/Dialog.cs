using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 앱 안에서 쓰는 확인 상자. 시스템 MessageBox 는 다크/라이트 테마와 글꼴을 따르지 않으므로,
/// 카드 배경·앱 버튼(Btn)·둥근 모서리로 직접 그린다. 동작은 MessageBox 와 같다: 소유 창을 비활성화한 채 중첩 메시지 루프를 돌리고
/// IDOK / IDYES / IDNO / IDCANCEL 을 돌려준다. Enter = 기본 버튼, Esc = 취소(예/아니요 상자에서는 아니요).
/// 검증 하네스는 창 클래스 "OneKeyDialog" 를 찾아 WM_COMMAND 로 버튼 id 를 보내면 된다(시스템 상자와 같은 방식).
/// </summary>
internal static unsafe class Dialog
{
    public const string ClassName = "OneKeyDialog";
    private const int Width = 380, Pad = 20, TitleH = 24, BtnW = 84, BtnH = 34, Gap = 8, IconBox = 22;
    private const int IDOK = 1, IDCANCEL = 2, IDYES = 6, IDNO = 7;
    /// <summary>
    /// 잠금 때문에 닫힌 상자의 결과(2026-09-30 사용자 결정, Codex 제안). 어떤 버튼도 누르지 않은 것이다:
    /// IDOK·IDYES 와 다르므로 "예/확인이면 한다"는 호출은 아무것도 하지 않고, [확인] 뒤에 무엇을 시작하는 호출은 이 값을 보고 멈춘다.
    /// 확인 하나뿐인 상자의 Esc·닫기는 여전히 IDOK 다(그건 사용자가 읽고 닫은 것).
    /// </summary>
    public const int IDLOCKED = 0x4C4B;
    /// <summary>확인 상자의 추가 버튼(예: 도움말의 [라이선스 보기])을 눌렀을 때의 결과.</summary>
    public const int IDEXTRA = 3;
    /// <summary>로그성 상자(share)의 왼쪽 버튼 [복사]·[파일로 저장]. 눌러도 상자는 닫히지 않는다(검증 하네스도 이 id 로 누른다).</summary>
    public const int IdShareCopy = 20, IdShareSave = 21;
    /// <summary>제목 STATIC 과 본문(STATIC 또는 읽기 전용 EDIT)의 id. 검증 하네스가 WM_GETTEXT 로 읽는다.</summary>
    public const int IdTitle = 100, IdBody = 101;
    /// <summary>섹션으로 나눈 본문(도움말)의 칸: 섹션 제목(굵게)은 110 부터, 본문 덩어리는 130 부터 차례로.</summary>
    public const int IdSectionHead = 110, IdSectionBody = 130;
    /// <summary>본문에서 이 글자로 시작하는 줄은 섹션 제목이다(도움말, Help.cs).</summary>
    public const string SectionMark = "## ";
    /// <summary>본문에서 이 글자로 시작하는 줄은 글머리 항목이다. 줄이 바뀌면 기호 폭만큼 들여 쓴다.</summary>
    public const string Bullet = "• ";
    /// <summary>
    /// 도움말 강조 줄(2026-10-05 사용자: 강조할 곳은 색으로, 그림을 넣을 곳은 그림으로).
    /// "! " = 주의(빨간 글 + 경고 표시), "&gt; " = 도움말(강조색 글 + 전구 표시), "@" = 화면 모양 그림(<see cref="HelpArt"/>: keys/btns/seg).
    /// 그림은 화면과 같은 색·모양으로 그려 테마·언어가 바뀌어도 맞는다(스크린숏을 넣지 않는다).
    /// </summary>
    public const string WarnMark = "! ", TipMark = "> ", ArtMark = "@";
    private const int BulletIndent = 14;
    /// <summary>주의·도움말 줄은 기호(아이콘 글꼴, 약 16px)가 글머리보다 넓다: 그만큼 더 들여 쓴다. 기호 칸과 글 칸이 겹치면
    /// 기호 칸 바탕이 첫 글자를 덮는다(0.5.15-O: 제휴 상자의 "이 포스팅은"이 "기 포스팅은"처럼 보였다).</summary>
    private const int MarkExtra = 6;

    private static nint Child(nint parent, string cls, string text, uint style, uint ex, int x, int y, int w, int h, int id, nint font)
    {
        nint c;
        fixed (char* pc = cls) fixed (char* pt = text)
            c = Native.CreateWindowExW(ex, pc, pt, style | Native.WS_CHILD | Native.WS_VISIBLE, x, y, w, h, parent, id, Native.GetModuleHandleW(null), 0);
        if (c != 0) { Native.SendMessageW(c, Native.WM_SETFONT, font, 1); Theme.ApplyControl(c, cls); }
        return c;
    }

    private sealed class State
    {
        public string Title = "", Text = "", Icon = "";
        public uint IconColor;
        public int Result, DefaultId, TextH;
        public bool Done, YesNo, HasCancel;   // HasCancel: 확인/취소 상자
        public bool ClosedByLock;
        public nint Body;                     // 스크롤 본문 EDIT(있으면). 상자 여백에서 굴린 휠도 이 본문으로 보낸다             // 잠금이 닫았다: 포커스·활성 창을 되돌리지 않는다(숨긴 본창을 앞으로 부르지 않도록)
        public uint Dpi = 96;
        // 접는 섹션(도움말, 2026-10-04 사용자 결정 D6: 처음에는 첫 섹션만 펼침). Section = 그 칸이 속한 섹션(-1 = 첫 제목 앞, 늘 보임)
        public readonly List<(nint Ctl, nint Bullet, int H, int Kind, int Section)> Blocks = new();
        public readonly Dictionary<nint, uint> Colors = new();   // 강조 줄의 글자색(주의·도움말)
        public bool[] Expanded = Array.Empty<bool>();
        public int ContentTop, Chrome, MaxH, W, Indent;
        public int[] ButtonIds = Array.Empty<int>();
        public string? Share;                 // 로그성 상자: 파일 이름에 넣을 말(diag·site·fill·error·hotkeys). null 이면 [복사]·[파일로 저장] 없음
    }
    private static readonly Dictionary<nint, State> _state = new();
    private static bool _registered;

    private const int HeadH = 26;

    /// <summary>접는 섹션 배치: 보이는 칸만 위에서부터 놓고, 상자 높이와 버튼 자리를 맞춘다. 펼쳐서 작업 영역을 넘으면 keep 섹션만 남기고 접는다.</summary>
    private static void Place(nint hwnd, State st, bool resize, int keep)
    {
        int S(int v) => (int)((long)v * st.Dpi / 96);
        int Content()
        {
            int y = 0; bool first = true;
            foreach (var b in st.Blocks)
            {
                if (b.Kind != 1 && b.Section >= 0 && !st.Expanded[b.Section]) continue;
                if (!first) y += S(b.Kind == 1 ? 12 : 2);
                y += b.H; first = false;
            }
            return Math.Max(y, S(20));
        }
        if (resize && st.Chrome + Content() > st.MaxH)
            for (int k = 0; k < st.Expanded.Length; k++) if (k != keep) st.Expanded[k] = false;
        int top = st.ContentTop; bool firstVisible = true;
        foreach (var b in st.Blocks)
        {
            bool show = b.Kind == 1 || b.Section < 0 || st.Expanded[b.Section];
            if (show && !firstVisible) top += S(b.Kind == 1 ? 12 : 2);
            int x = S(Pad), cw = st.W - 2 * S(Pad);
            if (b.Bullet != 0)
            {
                int bi = b.Kind is 3 or 4 ? st.Indent + S(MarkExtra) : st.Indent;   // 주의·도움말 기호는 더 넓다
                Native.SetWindowPos(b.Bullet, 0, x, top, bi, Math.Min(b.H, S(20)), 0x0004 | 0x0010 | (show ? 0x0040u : 0x0080u));   // NOZORDER|NOACTIVATE|SHOW/HIDE
                x += bi; cw -= bi;
            }
            Native.SetWindowPos(b.Ctl, 0, x, top, cw, b.H, 0x0004 | 0x0010 | (show ? 0x0040u : 0x0080u));
            if (b.Kind == 1 && b.Section >= 0)
            {
                nint style = Native.GetWindowLongPtrW(b.Ctl, Native.GWL_STYLE);
                nint want = st.Expanded[b.Section] ? style | (nint)Btn.Expanded : style & ~(nint)Btn.Expanded;
                if (want != style) { Native.SetWindowLongPtrW(b.Ctl, Native.GWL_STYLE, want); Native.InvalidateRect(b.Ctl, 0, false); CtlAcc.Changed(b.Ctl, value: false); }
            }
            if (show) { top += b.H; firstVisible = false; }
        }
        if (!resize) return;
        int h = st.Chrome + Content();
        Native.GetWindowRect(hwnd, out Native.RECT wr);
        Native.RECT work = WorkArea.For(hwnd);
        // 위쪽은 그대로 두고 아래로만 늘고 준다(2026-10-06 사용자: 펼칠 때마다 위아래로 움직여 불안정해 보임). 작업 영역을 넘을 때만 그만큼 올린다
        int y0 = Math.Clamp(wr.top, work.top, Math.Max(work.top, work.bottom - h));
        Native.SetWindowPos(hwnd, 0, wr.left, y0, wr.right - wr.left, h, 0x0004 | 0x0010);
        int by = h - S(Pad) - S(BtnH);
        foreach (int id in st.ButtonIds)
        {
            nint bt = Native.GetDlgItem(hwnd, id);
            if (bt == 0) continue;
            Native.GetWindowRect(bt, out Native.RECT br);
            var pt = new Native.POINT { x = br.left, y = br.top };
            Native.ScreenToClient(hwnd, ref pt);
            Native.SetWindowPos(bt, 0, pt.x, by, 0, 0, 0x0001 | 0x0004 | 0x0010);   // NOSIZE
        }
        Native.InvalidateRect(hwnd, 0, true);
    }

    /// <summary>지금 떠 있는(메시지 루프가 도는) 상자 수. 검증용 조회에 쓴다.</summary>
    public static int OpenCount => _state.Count;

    /// <summary>
    /// 잠금 세대: 잠글 때마다 1 씩 는다(<see cref="CancelAllForLock"/>). 상자가 떠 있던 사이에 잠금이 한 번이라도 있었으면
    /// 그 상자의 답은 버리고 <see cref="IDLOCKED"/> 를 돌려준다. 자동으로 닫을 수 없는 시스템 MessageBox 대체 경로에서도
    /// 잠금 뒤 누른 [예]가 종료·관리자 재시작으로 이어지지 않게 한다(Codex 0.2.49 검토 2절). 지금 잠겨 있는지가 아니라
    /// "중간에 잠금이 있었는가"를 본다: 잠갔다가 다시 푼 뒤 누른 답도 버린다.
    /// </summary>
    private static int _lockGeneration;
    /// <summary>지금 잠금 세대(잠글 때마다 +1). 화면 재배치가 "그 사이 잠겼는지"를 볼 때 쓴다.</summary>
    public static int LockGeneration => _lockGeneration;

    /// <summary>
    /// 잠글 때 떠 있는 상자를 모두 취소로 닫는다(2026-09-30 사용자 결정). 버튼을 누른 것으로 치지 않고 결과는 <see cref="IDLOCKED"/>.
    /// 상자는 바로 숨기고, 각 상자의 중첩 루프는 제어가 돌아오는 대로 끝난다(안쪽부터). 호출하는 쪽(DoLock)은 잠금 상태를 먼저 확정한다.
    /// </summary>
    public static void CancelAllForLock()
    {
        _lockGeneration++;
        foreach (var (h, st) in _state.ToArray())
        {
            if (st.Done) continue;
            st.Result = IDLOCKED; st.Done = true; st.ClosedByLock = true;
            Native.ShowWindow(h, Native.SW_HIDE);
            Native.PostMessageW(h, 0, 0, 0);   // WM_NULL: 그 상자의 루프가 GetMessage 에서 기다리고 있어도 깨어나 끝나게
        }
    }

    /// <summary>MessageBox 와 같은 인자. flags: MB_OK / MB_OKCANCEL / MB_YESNO + MB_ICON* + MB_DEFBUTTON2.
    /// width·okText: 도움말 창용(넓게, [닫기]). 그 밖에는 기본값.</summary>
    /// <summary>extraText: 확인 상자(MB_OK)에 [확인] 왼쪽의 추가 버튼(누르면 IDEXTRA). maxBodyH: 본문이 이 높이(논리 px)를 넘으면
    /// 작업 영역과 상관없이 스크롤·복사되는 읽기 전용 본문으로(라이선스 전문처럼 긴 글).</summary>
    /// <summary>share: 로그성 안내면 파일 이름에 넣을 짧은 말 — 왼쪽에 [복사]·[파일로 저장](제목·버전·시각을 붙여서, 상자는 그대로, 2026-10-07 사용자).</summary>
    public static int Show(nint owner, string text, string title, uint flags, int width = Width, string? okText = null, string? extraText = null, int maxBodyH = 0, string? share = null)
    {
        okText ??= T.CommonOk;
        // 섹션 제목("## " 로 시작하는 줄)이 있으면 제목은 굵게, 본문은 보통 글꼴로 칸을 나눠 만든다 (2026-09-29 사용자 요청).
        // "• " 로 시작하는 줄은 글머리 항목: "•" 와 본문을 따로 두어, 줄이 바뀌어도 본문 첫 글자에 맞춰 들여 쓴다(내어쓰기).
        const int KPara = 0, KHead = 1, KBullet = 2, KWarn = 3, KTip = 4, KArt = 5;
        var blocks = new List<(string Text, int Kind, int H)>();
        if (text.Contains(SectionMark))
        {
            var para = new List<string>();
            void Flush() { if (para.Count > 0) { blocks.Add((string.Join("\n", para).Trim('\n'), KPara, 0)); para.Clear(); } }
            foreach (string line in text.Split('\n'))
            {
                if (line.StartsWith(SectionMark)) { Flush(); blocks.Add((line[SectionMark.Length..], KHead, 0)); }
                else if (line.StartsWith(Bullet)) { Flush(); blocks.Add((line[Bullet.Length..], KBullet, 0)); }
                else if (line.StartsWith(WarnMark)) { Flush(); blocks.Add((line[WarnMark.Length..], KWarn, 0)); }
                else if (line.StartsWith(TipMark)) { Flush(); blocks.Add((line[TipMark.Length..], KTip, 0)); }
                else if (line.StartsWith(ArtMark)) { Flush(); blocks.Add((line[ArtMark.Length..], KArt, 0)); }
                else para.Add(line);
            }
            Flush();
            blocks.RemoveAll(b => b.Text.Length == 0);
        }
        // 섹션 제목이 둘 이상이면(도움말) 섹션을 접고 편다: 제목을 누르면 그 섹션의 본문이 보이거나 숨는다(D6)
        int heads = blocks.Count(b => b.Kind == KHead);
        bool foldable = heads >= 2;
        var sectionOf = new int[blocks.Count];
        { int sec = -1; for (int i = 0; i < blocks.Count; i++) { if (blocks[i].Kind == KHead) sec++; sectionOf[i] = sec; } }
        if (!_registered)
        {
            Ctl.RegisterClass(Native.GetModuleHandleW(null), ClassName, &WndProc, 0);
            _registered = true;
        }

        var st = new State { Share = share, Title = title, Text = text, YesNo = (flags & 0xF) == Native.MB_YESNO, HasCancel = (flags & 0xF) is Native.MB_OKCANCEL or Native.MB_YESNOCANCEL };
        st.Dpi = WorkArea.DpiFor(owner);
        int S(int v) => (int)((long)v * st.Dpi / 96);
        switch (flags & 0xF0)
        {
            case Native.MB_ICONERROR: st.Icon = ""; st.IconColor = Theme.Danger; break;        // ErrorBadge
            case Native.MB_ICONWARNING: st.Icon = ""; st.IconColor = Theme.Danger; break;      // Warning
            case Native.MB_ICONQUESTION: st.Icon = ""; st.IconColor = Theme.AccentInk; break;     // Unknown (물음표)
            case Native.MB_ICONINFORMATION: st.Icon = ""; st.IconColor = Theme.AccentInk; break;  // Info
        }

        // 본문 높이: 글꼴로 줄 바꿈해서 잰다.
        nint dc = Native.GetDC(0);
        nint oldFont = Native.SelectObject(dc, Theme.FontBody);
        int indent = S(BulletIndent);
        int Measure(string t, nint font, int less = 0)
        {
            Native.SelectObject(dc, font);
            var r = new Native.RECT { left = 0, top = 0, right = S(width - 2 * Pad) - less, bottom = 0 };
            Native.DrawText(dc, t, ref r, Native.DT_WORDBREAK | Native.DT_CALCRECT | Native.DT_NOPREFIX);
            // 본문 STATIC 은 DirectWrite 로 그린다(2b): 두 방식 중 큰 높이 — 상자가 떠 있는 동안 GDI 로 바뀌어도 잘리지 않게(Codex V2-3)
            int dwH = Dw.Measure(font, t, S(width - 2 * Pad) - less) is (int, int) m ? m.Item2 : 0;
            // DirectWrite 로 그리면 그 높이(+조금). GDI 측정을 함께 키우면 GDI 가 한 줄 더 접혀 문단 아래에 빈 줄이 남았다(2026-10-05 사용자 캡처)
            return dwH > 0 ? dwH + S(2) : r.bottom;
        }
        if (blocks.Count > 0)
        {
            int total = 0;
            for (int i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
                bool ind = b.Kind is KBullet or KWarn or KTip;
                int hh = b.Kind == KArt ? S(HelpArt.Height) : Measure(b.Text, b.Kind == KHead ? Theme.FontStrong : Theme.FontBody, !ind ? 0 : b.Kind is KWarn or KTip ? indent + S(MarkExtra) : indent);
                if (foldable && b.Kind == KHead) hh = Math.Max(hh, S(HeadH));   // 접는 제목은 누르는 버튼이라 조금 높게
                blocks[i] = (b.Text, b.Kind, hh);
                if (foldable && b.Kind != KHead && sectionOf[i] > 0) continue;   // 처음에는 첫 섹션만 펼침
                total += hh + (i == 0 ? 0 : S(b.Kind == KHead ? 12 : 2));   // 섹션 사이는 넓게, 제목과 본문·항목 사이는 좁게
            }
            st.TextH = Math.Max(total, S(20));
        }
        else st.TextH = Math.Max(Measure(text, Theme.FontBody), S(20));
        Native.SelectObject(dc, oldFont);
        Native.ReleaseDC(0, dc);

        // 소유 창 가운데(숨겨져 있으면 그 모니터의 작업 영역 가운데)
        int x, y;
        Native.RECT work = WorkArea.For(owner);

        // 본문이 작업 영역보다 길면(긴 안내·높은 배율) 상자를 작업 영역에 맞추고 본문만 스크롤한다 (T16).
        int w = S(width);
        int chrome = S(Pad) + S(TitleH) + S(10) + S(22) + S(BtnH) + S(Pad);
        bool scrollBody = chrome + st.TextH > work.bottom - work.top || (maxBodyH > 0 && st.TextH > S(maxBodyH));
        if (scrollBody) foldable = false;   // 접은 상태로도 넘치면 지금처럼 한 칸 스크롤 본문
        if (scrollBody) st.TextH = Math.Max(S(40), Math.Min(work.bottom - work.top - chrome, maxBodyH > 0 ? S(maxBodyH) : int.MaxValue));
        int h = chrome + st.TextH;
        var mi = new Native.MONITORINFO { cbSize = (uint)sizeof(Native.MONITORINFO), rcWork = work };
        if (owner != 0 && Native.IsWindowVisible(owner) && !Native.IsIconic(owner) && Native.GetWindowRect(owner, out Native.RECT or))
        { x = or.left + (or.right - or.left - w) / 2; y = or.top + (or.bottom - or.top - h) / 2; }
        else
        { x = mi.rcWork.left + (mi.rcWork.right - mi.rcWork.left - w) / 2; y = mi.rcWork.top + (mi.rcWork.bottom - mi.rcWork.top - h) / 2; }
        x = Math.Clamp(x, mi.rcWork.left, Math.Max(mi.rcWork.left, mi.rcWork.right - w));
        // 접는 도움말: 1Key 창의 제목 표시줄 바로 밑에 위쪽을 맞춘다(펼치면 아래로만 늘어난다, 2026-10-06 사용자)
        if (foldable && owner != 0 && Native.IsWindowVisible(owner) && !Native.IsIconic(owner))
        {
            var cp = new Native.POINT();
            if (Native.ClientToScreen(owner, ref cp)) y = cp.y;
        }
        y = Math.Clamp(y, mi.rcWork.top, Math.Max(mi.rcWork.top, mi.rcWork.bottom - h));

        // 여기부터 만든 것(흐림 막, 상자, 소유 창 비활성)은 아래 finally 한 곳에서 정리한다: 메시지 루프에 들어가기 전의 예외도 포함 (Codex R2).
        nint backdrop = 0, hwnd = 0, prevActive = 0, prevFocus = 0;
        bool ownerDisabled = false;
        int lockGen = _lockGeneration;
        try
        {
            // 본창을 흐리고 어둡게 덮는 막(웹 모달처럼 전경이 잘 보이게, 2026-09-29 사용자 요청). 상자보다 먼저 만들어 상자가 그 위에 온다.
            backdrop = Backdrop.Show(owner);
            // 검증용 실패 주입 dialog:use-msgbox: 상자를 못 만든 것처럼 시스템 MessageBox 대체 경로로 간다.
            if (!Program.TestFails("dialog:use-msgbox"))
                fixed (char* cls = ClassName) fixed (char* cap = title)
                    hwnd = Native.CreateWindowExW(Native.WS_EX_CONTROLPARENT | Native.WS_EX_COMPOSITED, cls, cap,
                        Native.WS_POPUP | Native.WS_CLIPCHILDREN, x, y, w, h, owner, 0, Native.GetModuleHandleW(null), 0);
            if (hwnd == 0)
            {
                // 만들지 못하면 시스템 상자로. 이 상자는 잠금이 닫지 못하므로, 떠 있는 사이 잠금이 있었으면 답을 버린다.
                Backdrop.Close(backdrop); backdrop = 0;
                int sysResult = Native.MsgBox(owner, text, title, flags);
                return _lockGeneration != lockGen ? IDLOCKED : sysResult;
            }
            _state[hwnd] = st;

            // Windows 11: 둥근 모서리와 테마색 테두리. 지원하지 않는 버전에서는 조용히 무시된다.
            uint corner = (uint)Native.DWMWCP_ROUND; Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, &corner, sizeof(uint));
            uint border = Theme.BorderColor; Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_BORDER_COLOR, &border, sizeof(uint));

            // 제목과 본문은 STATIC(길면 읽기 전용 EDIT) 컨트롤이다: 화면 읽기 프로그램이 읽을 수 있도록 (T16). 아이콘과 구분선만 직접 그린다.
            int tx = S(Pad) + (st.Icon.Length > 0 ? S(IconBox) + S(8) : 0);
            Child(hwnd, "STATIC", title, Native.SS_LEFT | Native.SS_NOPREFIX | Native.SS_CENTERIMAGE | Native.SS_ENDELLIPSIS, 0,
                  tx, S(Pad), w - tx - S(Pad), S(TitleH), IdTitle, Theme.FontStrong);
            if (scrollBody)
            {
                // 작업 영역보다 길면 한 칸짜리 스크롤 본문(섹션 제목 표시는 떼고 보통 글꼴). 막대는 Windows 기본이 아니라 앱 막대(ThinScroll):
                // 본문을 오른쪽 여백 쪽으로 넓히고 막대는 그 여백 안에 그린다(2026-09-30 사용자 결정)
                nint body = Child(hwnd, "EDIT", text.Replace(SectionMark, "").Replace("\r\n", "\n").Replace("\n", "\r\n"),
                      Native.ES_MULTILINE | Native.ES_READONLY | Native.ES_AUTOVSCROLL | Native.WS_TABSTOP, 0,
                      S(Pad), S(Pad) + S(TitleH) + S(10), w - 2 * S(Pad) + S(ThinScroll.Gutter) - S(6), st.TextH, IdBody, Theme.FontBody);
                ThinScroll.Attach(body, Theme.CardBg, st.Dpi);
                st.Body = body;
            }
            else if (blocks.Count > 0)
            {
                // 섹션 제목·본문을 각각 STATIC 으로(화면 읽기 프로그램도 차례로 읽는다)
                int by0 = S(Pad) + S(TitleH) + S(10), nh = 0, nb = 0;
                for (int i = 0; i < blocks.Count; i++)
                {
                    var b = blocks[i];
                    if (i > 0) by0 += S(b.Kind == KHead ? 12 : 2);
                    int bx0 = S(Pad), bw = w - 2 * S(Pad);
                    nint bullet = 0, ctl;
                    if (b.Kind == KBullet)
                    {
                        // 글머리 기호는 따로, 본문은 기호 폭만큼 안쪽에서 시작해 줄이 바뀌어도 첫 글자에 맞춰 정렬된다
                        bullet = Child(hwnd, "STATIC", "•", Native.SS_LEFT | Native.SS_NOPREFIX, 0, bx0, by0, indent, Math.Min(b.H, S(20)), 0, Theme.FontBody);
                        bx0 += indent; bw -= indent;
                    }
                    else if (b.Kind is KWarn or KTip)
                    {
                        // 주의·도움말: 기호 자리에 경고·전구 표시(아이콘 글꼴), 본문은 그 색으로
                        int markW = indent + S(MarkExtra);
                        bullet = HelpArt.Create(hwnd, b.Kind == KWarn ? "mark:warn" : "mark:tip", bx0, by0, markW, Math.Min(b.H, S(20)));
                        bx0 += markW; bw -= markW;
                    }
                    if (b.Kind == KArt)
                    {
                        ctl = HelpArt.Create(hwnd, b.Text, bx0, by0, bw, b.H);
                        st.Blocks.Add((ctl, 0, b.H, b.Kind, sectionOf[i]));
                        by0 += b.H; nb++;
                        continue;
                    }
                    if (foldable && b.Kind == KHead)
                    {
                        // 접는 제목: 꺾쇠 + 굵은 글(누르면 접기/펴기, 화면 읽기에는 펼침/접힘 상태)
                        uint style = Btn.Section | Btn.OnCard | Native.WS_TABSTOP | (sectionOf[i] == 0 ? Btn.Expanded : 0);
                        ctl = Child(hwnd, Btn.ClassName, b.Text, style, 0, bx0, by0, bw, b.H, IdSectionHead + nh++, Theme.FontStrong);
                    }
                    else
                        ctl = Child(hwnd, "STATIC", b.Text, Native.SS_LEFT | Native.SS_NOPREFIX, 0, bx0, by0, bw, b.H,
                              b.Kind == KHead ? IdSectionHead + nh++ : (nb == 0 ? IdBody : IdSectionBody + nb - 1), b.Kind == KHead ? Theme.FontStrong : Theme.FontBody);
                    if (b.Kind != KHead) nb++;
                    if (b.Kind == KWarn) st.Colors[ctl] = Theme.DangerText;
                    else if (b.Kind == KTip) st.Colors[ctl] = Theme.AccentLabel;
                    st.Blocks.Add((ctl, bullet, b.H, b.Kind, sectionOf[i]));
                    by0 += b.H;
                }
                if (foldable)
                {
                    st.Expanded = new bool[heads]; st.Expanded[0] = true;
                    st.ContentTop = S(Pad) + S(TitleH) + S(10); st.Chrome = chrome; st.MaxH = work.bottom - y; st.W = w; st.Indent = indent;
                    Place(hwnd, st, resize: false, keep: 0);
                }
            }
            else
                Child(hwnd, "STATIC", text, Native.SS_LEFT | Native.SS_NOPREFIX, 0,
                      S(Pad), S(Pad) + S(TitleH) + S(10), w - 2 * S(Pad), st.TextH, IdBody, Theme.FontBody);

            // 버튼: 오른쪽 정렬. 기본 버튼은 강조색(Prominent), 나머지는 테두리(Bordered).
            (int Id, string Text)[] buttons = (flags & 0xF) switch
            {
                Native.MB_YESNO => new[] { (IDYES, T.CommonYes), (IDNO, T.CommonNo) },
                Native.MB_YESNOCANCEL => new[] { (IDYES, T.CommonYes), (IDNO, T.CommonNo), (IDCANCEL, T.CommonCancel) },   // Esc·× = 취소(아니요가 아님)
                Native.MB_OKCANCEL => new[] { (IDOK, okText), (IDCANCEL, T.CommonCancel) },   // okText: 예) 제휴 상자의 [브라우저에서 열기]
                _ => extraText is null ? new[] { (IDOK, okText) } : new[] { (IDEXTRA, extraText), (IDOK, okText) },
            };
            int defIndex = (flags & Native.MB_DEFBUTTON2) != 0 && buttons.Length > 1 ? 1 : 0;
            if (extraText is not null && (flags & 0xF) == Native.MB_OK) defIndex = 1;   // 기본은 [확인]/[닫기]
            st.DefaultId = buttons[defIndex].Id;
            int by = h - S(Pad) - S(BtnH);
            int bx = w - S(Pad) - S(BtnW);
            bool wideOk = (flags & 0xF) == Native.MB_OKCANCEL && okText != T.CommonOk;
            nint defHwnd = 0;
            for (int i = buttons.Length - 1; i >= 0; i--)
            {
                uint kind = (i == defIndex ? Btn.Prominent | Btn.Default : Btn.Bordered) | Btn.OnCard | Native.WS_TABSTOP | Native.WS_CHILD | Native.WS_VISIBLE;
                nint b;
                fixed (char* bc = Btn.ClassName) fixed (char* bt = buttons[i].Text)
                {
                    int bw = buttons[i].Id == IDEXTRA ? S(BtnW + 40) : buttons[i].Id == IDOK && wideOk ? S(BtnW + 60) : S(BtnW);   // 추가 버튼 글("라이선스 보기")·확인/취소 상자의 바꾼 확인 글("브라우저에서 열기")은 더 길다
                    b = Native.CreateWindowExW(0, bc, bt, kind, bx + S(BtnW) - bw, by, bw, S(BtnH), hwnd, buttons[i].Id, Native.GetModuleHandleW(null), 0);
                    bx -= bw - S(BtnW);
                }
                Native.SendMessageW(b, Native.WM_SETFONT, Theme.FontBody, 1);
                if (i == defIndex) defHwnd = b;
                st.ButtonIds = st.ButtonIds.Append(buttons[i].Id).ToArray();
                bx -= S(BtnW) + S(Gap);
            }

            if (share is not null)
            {
                // 왼쪽 끝부터 [복사] [파일로 저장] (테두리 버튼, 기본 버튼 아님)
                int lx = S(Pad);
                foreach (var (id, label, bw0) in new[] { (IdShareCopy, T.DiagCopy, BtnW), (IdShareSave, T.LogSaveFile, BtnW + 30) })
                {
                    nint b;
                    fixed (char* bc = Btn.ClassName) fixed (char* bt = label)
                        b = Native.CreateWindowExW(0, bc, bt, Btn.Bordered | Btn.OnCard | Native.WS_TABSTOP | Native.WS_CHILD | Native.WS_VISIBLE,
                                                   lx, by, S(bw0), S(BtnH), hwnd, id, Native.GetModuleHandleW(null), 0);
                    Native.SendMessageW(b, Native.WM_SETFONT, Theme.FontBody, 1);
                    st.ButtonIds = st.ButtonIds.Append(id).ToArray();
                    lx += S(bw0) + S(Gap);
                }
            }

            if (Program.TestFails("dialog:before-loop")) throw new InvalidOperationException("test: dialog:before-loop");

            // 모달: 소유 창을 막고 중첩 루프. 닫힌 뒤 돌려놓을 활성 창과 포커스를 기억한다 (T16).
            // 소유 창은 이 상자가 막았을 때만 푼다: 상자가 겹치면 안쪽 상자가 닫히며 바깥 상자의 소유 창까지 풀면 안 된다 (Codex).
            prevActive = Native.GetActiveWindow(); prevFocus = Native.GetFocus();
            if (owner != 0 && Native.IsWindowEnabled(owner)) { Native.EnableWindow(owner, false); ownerDisabled = true; }
            Native.ShowWindow(hwnd, Native.SW_SHOW);
            Native.SetForegroundWindow(hwnd);
            if (defHwnd != 0) Native.SetFocus(defHwnd);
            while (!st.Done)
            {
                int r = Native.GetMessageW(out Native.MSG msg, 0, 0, 0);
                if (r <= 0) { if (r == 0) Native.PostQuitMessage((int)msg.wParam); break; }   // 앱이 끝나는 중: 종료 요청을 되돌려 놓고 나간다
                // Alt+F4 = 닫기(취소/아니요). 테두리 없는 팝업이라 시스템 메뉴 처리에 맡기지 않고 여기서 받는다 (T16).
                if (msg.message == Native.WM_SYSKEYDOWN && msg.wParam == 0x73 && (Native.GetAncestor(msg.hwnd, 2) == hwnd || msg.hwnd == hwnd))
                {
                    Native.SendMessageW(hwnd, Native.WM_CLOSE, 0, 0);
                    continue;
                }
                if (Native.IsDialogMessageW(hwnd, ref msg)) continue;
                Native.TranslateMessage(ref msg);
                Native.DispatchMessageW(ref msg);
            }
        }
        finally
        {
            // 소유 창을 먼저 풀어야 상자가 사라질 때 Windows 가 다른 프로그램을 활성화하지 않는다.
            if (ownerDisabled) Native.EnableWindow(owner, true);
            if (!st.ClosedByLock && prevActive != 0 && Native.IsWindow(prevActive)) Native.SetActiveWindow(prevActive);
            if (hwnd != 0) { Native.DestroyWindow(hwnd); _state.Remove(hwnd); }
            Backdrop.Close(backdrop);   // 잠금이 먼저 치웠으면 아무것도 하지 않는다
            if (!st.ClosedByLock && prevFocus != 0 && Native.IsWindow(prevFocus) && Native.IsWindowVisible(prevFocus) && Native.IsWindowEnabled(prevFocus))
                Native.SetFocus(prevFocus);   // 상자를 띄우기 전의 컨트롤로 포커스를 돌려준다
        }
        if (st.ClosedByLock || _lockGeneration != lockGen) return IDLOCKED;   // 떠 있는 사이 잠금이 있었다: 어떤 답도 쓰지 않는다
        return st.Result != 0 ? st.Result : (st.YesNo ? IDNO : IDCANCEL);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            _state.TryGetValue(hwnd, out State? st);
            switch (msg)
            {
                case Native.WM_COMMAND:
                {
                    if (st is null) break;
                    if (st.Done) return 0;   // 이미 끝난 상자(잠금 취소 포함): 늦게 온 버튼 메시지로 결과를 바꾸지 않는다
                    int id = (int)(wParam & 0xFFFF);
                    if (id >= IdSectionHead && id < IdSectionHead + st.Expanded.Length)
                    {
                        // 섹션 제목: 접기/펴기(상자는 닫지 않는다)
                        // 한 번에 한 섹션만 펼친다(2026-10-05 사용자: 여러 개 펼치면 상자가 화면 높이만큼 커져 아래가 까맣게 남음)
                        int k = id - IdSectionHead;
                        bool open = !st.Expanded[k];
                        for (int j = 0; j < st.Expanded.Length; j++) st.Expanded[j] = false;
                        st.Expanded[k] = open;
                        Place(hwnd, st, resize: true, keep: k);
                        return 0;
                    }
                    if (st.Share is not null && id is IdShareCopy or IdShareSave)
                    {
                        string content = ShareText.Compose(st.Title, st.Text);
                        if (id == IdShareCopy) Toast.Show(hwnd, ShareText.Copy(hwnd, content) ? T.DiagCopied : T.DiagCopyFailed);
                        else if (ShareText.Save(hwnd, st.Share, content, out string? err) is string path) Toast.Show(hwnd, T.LogSaved(Path.GetFileName(path)), 2400);
                        else if (err is not null) Toast.Show(hwnd, T.LogSaveFailed(err), 3000, warn: true);
                        return 0;   // 상자는 닫지 않는다
                    }
                    if (id == IDCANCEL && st.YesNo) id = IDNO;          // Esc
                    if (id == IDCANCEL && !st.YesNo && !st.HasCancel) id = IDOK;   // 확인만 있는 상자에서 Esc = 확인
                    if (id is IDOK or IDCANCEL or IDYES or IDNO or IDEXTRA) { st.Result = id; st.Done = true; }
                    return 0;
                }
                case Native.WM_MOUSEWHEEL:
                    if (st is not null && st.Body != 0) { Native.SendMessageW(st.Body, msg, wParam, lParam); return 0; }
                    break;
                case Native.WM_CLOSE:
                    if (st is not null && !st.Done) { st.Result = st.YesNo ? IDNO : (st.HasCancel ? IDCANCEL : IDOK); st.Done = true; }
                    return 0;
                case Native.DM_GETDEFID:
                    return st is not null ? (nint)((Native.DC_HASDEFID << 16) | (uint)st.DefaultId) : 0;
                case Native.WM_ERASEBKGND:
                {
                    Native.GetClientRect(hwnd, out Native.RECT rc);
                    Native.FillRect(wParam, ref rc, Theme.CardBrush);
                    return 1;
                }
                case Native.WM_CTLCOLORSTATIC:   // 제목·본문 STATIC 과 읽기 전용 EDIT
                    Native.SetBkColor(wParam, Theme.CardBg);
                    Native.SetTextColor(wParam, st is not null && st.Colors.TryGetValue(lParam, out uint tc) ? tc : Theme.ControlText);
                    return Theme.CardBrush;
                case Native.WM_PAINT:
                    if (st is not null) Paint(hwnd, st);
                    return 0;
            }
        }
        catch { }
        return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private static void Paint(nint hwnd, State st)
    {
        int S(int v) => (int)((long)v * st.Dpi / 96);
        nint hdc = Native.BeginPaint(hwnd, out Native.PAINTSTRUCT ps);
        try
        {
            Native.GetClientRect(hwnd, out Native.RECT rc);
            Native.FillRect(hdc, ref rc, Theme.CardBrush);
            Native.SetBkMode(hdc, Native.TRANSPARENT);
            // 제목·본문은 자식 컨트롤이 그린다. 여기서는 아이콘과 버튼 위 구분선만.
            int x = S(Pad), y = S(Pad);
            if (st.Icon.Length > 0)
                Ctl.Text(hdc, Theme.FontIcon, st.Icon, st.IconColor, x, y, x + S(IconBox), y + S(TitleH), Native.DT_LEFT | Native.DT_VCENTER);
            // 버튼 위 가는 구분선
            var sep = new Native.RECT { left = 0, top = rc.bottom - S(Pad) - S(BtnH) - S(12), right = rc.right, bottom = rc.bottom - S(Pad) - S(BtnH) - S(12) + Math.Max(1, S(1)) };
            Native.FillRect(hdc, ref sep, Theme.BorderBrush);
        }
        finally { Native.EndPaint(hwnd, ref ps); }
    }
}
