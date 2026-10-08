using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>App 의 일부: 목록 화면과 설정. (App.cs 에서 화면별로 나눔, 0.3.9 — 동작은 그대로)</summary>
internal sealed unsafe partial class App
{
    // ---------------------------------------------------------------- 목록 화면

    // 단축키만 있고 비밀번호가 없는 슬롯은 "비어 있음"으로 본다 (예전 버전 기본값이 Ctrl+Alt+1~3 이라서).
    private static bool SlotInUse(Slot s) => s.Name.Length > 0 || s.HasPassword;

    /// <summary>
    /// 새 디자인(2026-10-08, 시안 Main.dc.html): 판(DialX·DialW, 안쪽 여백 위 14 · 옆 12 · 아래 12) 안에 자동 잠금 칸(78)과 조각(높이 58 · 사이 8).
    /// ListRowH 는 조각 한 줄의 간격(58 + 8) — 화면에 맞춰 줄 수를 줄일 때(ShowScreen)도 쓴다.
    /// </summary>
    private const int ListRowH = MetalUi.TileH + MetalUi.TileGap;
    private const int DialX = 14, DialW = WinW - 2 * DialX, DialPadTop = 14, DialPadX = 12, DialPadBottom = 12, LockInfoH = 78;
    private const int IdListAutoLock = 2017;

    /// <summary>항목의 종류(편집 화면 양식): 자주 사용하는 문구 / 사이트·앱 로그인 / 연속된 문구 입력.</summary>
    private static string SlotKind(Slot s) => s.Form switch { 1 => T.AddPassword, 2 => T.AddLogin, _ => T.AddMulti };

    private void BuildList()
    {
        // 머리줄(시안): 왼쪽 고양이 윤곽 + "내 항목" + 버전(바탕 그림에 그린다 — PageOverlay), 오른쪽 둥근 단추 [?] [위젯·최소화] [잠금] [종료].
        // 시안에 없는 [위젯·최소화]는 기능이 있어 넣었다. 단추 사이 10, 오른쪽 여백 22. 창은 그늘 자리만큼 단추보다 크다.
        int y = 8;
        int kw = MetalUi.KnobD + 2 * MetalUi.KnobPadX, kh = MetalUi.KnobD + MetalUi.KnobPadTop + MetalUi.KnobPadBottom, ky = y - MetalUi.KnobPadTop;
        int kx = WinW - 22 - MetalUi.KnobD;
        Tip(Button(IdListExit, IcPower, Btn.Knob, kx - MetalUi.KnobPadX, ky, kw, kh), T.CommonExit); kx -= MetalUi.KnobD + 10;
        Tip(Button(IdListLock, IcLock, Btn.Knob, kx - MetalUi.KnobPadX, ky, kw, kh), T.CommonLock); kx -= MetalUi.KnobD + 10;
        // 위젯 모드(2026-10-06 사용자)를 켰으면 (−) 대신 위젯 아이콘: 누르면 트레이로(마스코트가 작업 표시줄 위에)
        Tip(Button(IdListMin, WidgetButton ? IcWidget : IcTray, Btn.Knob, kx - MetalUi.KnobPadX, ky, kw, kh), WidgetButton ? T.ListWidgetMode : T.CommonMinimize); kx -= MetalUi.KnobD + 10;
        nint help = Button(IdHelp, "?", Btn.Knob, kx - MetalUi.KnobPadX, ky, kw, kh);
        if (help != 0) { CtlAcc.SetName(help, T.CommonHelp); Tip(help, T.CommonHelp); }
        _page.HeaderRight = kx - 8;
        _page.ListHeader = true;
        _page.Metal = Theme.MetalPage = true;   // 새 디자인: 바로 실행 띠 상자·검색 칸도 금속 조각·파인 홈으로(바탕 그림)
        y += MetalUi.KnobD + 14;
        y = BuildLaunchStrip(y);   // 프로그램·폴더 아이콘 띠(항목이 있을 때만)

        // 등록된 항목
        var used = new List<int>();
        for (int i = 0; i < Config.SlotCount; i++) if (SlotInUse(_cfg.Slots[i])) used.Add(i);
        bool canAdd = used.Count < Config.SlotCount;

        // 검색 칸: 한 화면(8행)에 다 들어오지 않을 때만 보인다. 이름이나 단축키 글자로 거른다.
        var shown = used;
        if (used.Count > MaxVisibleRows || _listFilter.Length > 0)
        {
            // 검색 중이면 오른쪽에 "n개 중 m개"와 [×] 지우기 (T14). 결과가 0건이어도 보인다.
            bool filtering = _listFilter.Length > 0;
            if (filtering) shown = used.Where(i => MatchesFilter(_cfg.Slots[i], i, _listFilter)).ToList();
            const int countW = 84, clearW = 28;
            Field(IdListFilter, StripX, y, filtering ? StripW - countW - clearW - 8 : StripW, FieldH, onCard: false);
            Native.SetCueBanner(C(IdListFilter), T.ListSearch);
            Native.SetText(C(IdListFilter), _listFilter);
            if (filtering)
            {
                nint cnt = Make("STATIC", T.ListCount(used.Count, shown.Count), Native.SS_RIGHT | Native.SS_CENTERIMAGE | Native.SS_NOPREFIX,
                                StripX + StripW - countW - clearW - 4, y, countW, FieldH, IdListCount, 0, Theme.FontSmall);
                if (cnt != 0) _staticStyle[cnt] = (Theme.BgBrush, Theme.SecondaryText);
                Tip(Button(IdListClear, IcClear, Btn.Icon, StripX + StripW - clearW, y, clearW, FieldH), T.ListClearSearch);
            }
            y += FieldH + 8;
        }

        // 판: 맨 위 자동 잠금 칸(시안의 큰 숫자). 누르면 설정 화면의 자동 잠금으로 간다 — 목록에서 바로 바꾸면 "바꾸고 [저장]" 규칙이 깨져 스위치 대신 꺾쇠
        int dialTop = y;
        y += DialPadTop;
        int mins = _cfg.AutoLockMinutes;
        string num = mins > 0 ? mins.ToString() : T.SetLockOffShort;
        string unit = mins > 0 ? T.SetMinutes(mins).Replace(num, "").Trim() : "";
        nint li = Make(Row.ClassName, T.SetAutolock + " " + (mins > 0 ? T.SetMinutes(mins) : T.SetLockOffShort), Row.LockInfo | Native.WS_TABSTOP,
                       DialX + DialPadX, y, DialW - 2 * DialPadX, LockInfoH, IdListAutoLock);
        if (li != 0) { Row.SetPlate(li, T.SetAutolock + " " + (mins > 0 ? T.SetMinutes(mins) : T.SetLockOffShort), unit, T.SetAutolock, num); Tip(li, T.ListSettings); }
        y += LockInfoH;

        // 조각: 최대 _visibleRows 개만 만들고(가상 목록), 나머지는 휠·스크롤 막대로 넘긴다.
        bool scroll = shown.Count > _visibleRows;
        _listScrollMax = Math.Max(0, shown.Count - _visibleRows);
        _listTop = Math.Clamp(_listTop, 0, _listScrollMax);
        int first = scroll ? _listTop : 0, count = Math.Min(_visibleRows, shown.Count - first);
        if (used.Count > 0 && shown.Count == 0)
        {
            Label(T.ListNoMatch, DialX + DialPadX + 8, y, DialW - 2 * DialPadX - 16, RowH, _font, Theme.SecondaryText, false, userText: false);
            y += RowH;
        }
        _listRowsBuilt = Math.Max(0, count);
        int tileX = DialX + DialPadX, tileW = DialW - 2 * DialPadX;
        // [입력] 창: 조각의 오른쪽 끝(알약 + 오른쪽 여백 10 + 왼쪽 틈 8 + 그늘 3). 행 창은 그 앞까지
        int zoneW = LabelW(Theme.Sized(12, true), T.CommonInput) + 24 + 10 + 8 + MetalUi.TileMarginX;
        int listTop = y;
        for (int k = 0; k < count; k++)
        {
            int i = shown[first + k];
            Slot s = _cfg.Slots[i];
            // 위 작은 글: 종류 + 넣는 방법(Enter·등록 실패·사이트·프로그램). 단축키는 왼쪽 칸(조합 작게 · 키 크게)
            SiteLink? firstLink = s.Site ?? s.More.Select(x => x.Site).FirstOrDefault(x => x is not null);
            string? site = s.App is AppLink al ? (al.Usable ? T.ListSubApp(al.FileName) : T.ListSubAppRelink(al.FileName))
                         : firstLink is SiteLink sl ? (LinkedInputs(s) is null ? T.ListSubLinkIncomplete : T.ListSubSite(SiteHost(sl.Url))) : null;
            string hk = s.HasHotkey ? s.HotkeyText() : "";
            int cut = hk.Length > 1 && hk.EndsWith("+") ? hk.Length - 2 : hk.LastIndexOf('+');
            string mod = cut > 0 ? hk[..cut] : "", key = cut > 0 ? hk[(cut + 1)..] : hk;
            var extra = new List<string> { SlotKind(s) };
            if (!s.HasPassword) extra.Add(T.ListSubNoContent);
            else
            {
                if (s.HasHotkey && _hotkeyFailed[i]) extra.Add(T.ListSubRegFailed);
                else if (s.HasHotkey && s.PressEnter) extra.Add(s.NoEnterInBrowser ? T.ListSubEnterNoBrowser : T.ListSubEnter);
                if (site is not null) extra.Add(site);
            }
            string plateSub = string.Join("  ·  ", extra);
            // 행을 누르면 편집 화면. 오른쪽 [입력]을 누르면 본창이 내려가고 화면 위에 입력 칩이 뜬다(D안, 최종수정안 T11).
            // 탭 순서: 행 → 그 행의 [입력].
            int wy = y - MetalUi.TileMarginTop, rx = tileX - MetalUi.TileMarginX, bx = tileX + tileW + MetalUi.TileMarginX - zoneW;
            nint r = Make(Row.ClassName, s.DisplayName(i), Row.Tile | Row.SquareRight | Row.EditHint | Native.WS_TABSTOP, rx, wy, bx - rx, MetalUi.TileWinH, IdRowSlot + i);
            if (r != 0) Row.SetPlate(r, s.DisplayName(i), plateSub, mod, key);
            nint b = Button(IdRowInput + i, T.CommonInput, Btn.PillInput, bx, wy, zoneW, MetalUi.TileWinH);
            if (b != 0 && !s.HasPassword) Native.EnableWindow(b, false);   // 내용이 없는 항목은 넣을 것이 없다
            if (r != 0 && b != 0) { Ctl.Partner[r] = b; Ctl.Partner[b] = r; }   // 행이나 버튼 어느 쪽에 마우스가 있어도 조각 전체 강조
            y += ListRowH;
        }
        if (count > 0) y -= MetalUi.TileGap;
        y += DialPadBottom;
        _page.Dials.Add((DialX, dialTop, DialW, y - dialTop));
        if (scroll)
        {
            // 판 오른쪽 안쪽 여백의 가는 스크롤 막대
            int trackX = DialX + DialW - 8, trackY = listTop + 4, trackH = count * ListRowH - MetalUi.TileGap - 8;
            int thumbH = Math.Max(24, trackH * _visibleRows / shown.Count);
            int thumbY = trackY + (trackH - thumbH) * _listTop / _listScrollMax;
            _page.ScrollTrack = (trackX, trackY, 4, trackH, thumbY, thumbH);
        }

        if (!canAdd)
        {
            // 한도(99개)에 닿으면 [+ 추가]가 꺼진 이유를 판 아래에 보인다 (Codex QA-08). 평소에는 없다.
            y += 8;
            nint full = Make("STATIC", T.ListFull(Config.SlotCount),
                             Native.SS_LEFT | Native.SS_CENTERIMAGE | Native.SS_NOPREFIX | Native.SS_ENDELLIPSIS, Margin + 6, y, CardW - 12, RowH, IdListFull, 0, Theme.FontSmall);
            if (full != 0) _staticStyle[full] = (Theme.BgBrush, Theme.SecondaryText);
            y += RowH;
        }
        // 항목이 없을 때만 시작 안내
        if (used.Count == 0)
            y = Footer(T.ListIntro, y);

        // 아래 줄(시안): 왼쪽 [+ 추가] 알약, 오른쪽 "설정 ›" 글 링크. 버튼 id 는 예전 그대로(IdRowAdd · IdRowSettings — 하네스·포커스 규약)
        int by = y + 14;
        string addText = "+ " + T.ListAdd;
        int addW = LabelW(Theme.Sized(13, true), addText) + 36;
        nint addBtn = Button(IdRowAdd, addText, Btn.PillMain, 22 - MetalUi.PillPadX, by - MetalUi.PillPadTop, addW + 2 * MetalUi.PillPadX, MetalUi.PillMainH + MetalUi.PillPadTop + MetalUi.PillPadBottom);
        if (!canAdd && addBtn != 0) Native.EnableWindow(addBtn, false);
        var draft = _settingsDraft;
        bool draftDirty = draft is { } d0 && SettingsDiffer(d0);
        string setText = draftDirty ? T.ListSettings + " \u00B7 " + T.ListSettingsDirty : T.ListSettings;
        int setW = Math.Min(WinW - 44 - addW - 16, LabelW(Theme.Sized(13, false), setText + " \u203A") + 24);
        nint set = Button(IdRowSettings, setText, Btn.Borderless | Btn.Link, WinW - 22 + 12 - setW, by + 2, setW, 32);
        if (set != 0 && !draftDirty) Tip(set, T.ListSettingsSummary);
        _page.Height = by + MetalUi.PillMainH + 16;
    }

    /// <summary>
    /// 설정 본문. 2026-10-05 사용자 B안: 묶음 제목·칸 아래 설명을 없애고(설명은 [도움말]·마우스를 올리면 나오는 풍선), 한 줄을 차지할 필요 없는 것은
    /// 버튼으로 모아 높이를 줄인다 — 카드 넷: 시작·권한 / 화면(테마는 나란한 버튼, 바로 실행 이름 표시는 한 줄에 둘) / 보안(자동 잠금 + 버튼들) / 넣기·도구.
    /// 새 디자인(2026-10-08, 시안 Settings-light/dark.dc.html): 카드는 판(DialX) 안의 금속 조각(x 26 · 폭 368 · 사이 10), 줄 높이 44 + 구분선 1(양옆 14 안쪽),
    /// 이름 13.5px 굵게. 자동 잠금은 ‹ 큰 숫자 › + 단계 점(Slider 의 새 모양). 시안의 칸 아래 설명 줄은 B안대로 풍선에 둔다(항목이 시안보다 많다).
    /// 컨트롤 번호는 그대로(저장·시험이 같은 번호를 쓴다). 끝난 y(마지막 조각의 아래)를 돌려준다.
    /// </summary>
    private int BuildSettingsBody(int y)
    {
        const int RH = MetalUi.SetRowH, Gap = 10;
        int cx = DialX + DialPadX, cw = DialW - 2 * DialPadX, lx = cx + Row.PadX, rx = cx + cw - Row.PadX;
        nint nameFont = Theme.Sized(13.5, true);
        uint ink = Metal.Ref(Metal.Ink(Theme.IsDark));
        int top;
        void Sep(int at) => _page.Separators.Add((lx, at, rx - lx));
        nint Switch(int id, string text, int x, int at, int w) => Make(Toggle.ClassName, text, Toggle.StyleTrailing | Native.WS_TABSTOP, x, at, w, RH, id, 0, nameFont);
        void Name(string text, int at, int w) => Label(text, lx, at, w, RH, nameFont, ink, true, Native.SS_LEFT | Native.SS_ENDELLIPSIS);
        int half = (rx - lx - 16) / 2;
        // 스위치 둘을 한 줄에: 두 이름이 반 칸에 다 들어갈 때만. 넘치면(영어·일본어·베트남어 등) 한 줄씩 — 이름이 잘리지 않게
        bool Fits(string t)   // 그리는 방식(DirectWrite)으로 잰다 — GDI 로 재면 넉넉히 나와 한국어도 한 줄씩 갈라졌다
        {
            nint dc = Native.GetDC(_hwnd);
            int pw = MetalUi.TextWidth(dc, 13.5, true, t);
            Native.ReleaseDC(_hwnd, dc);
            return pw * 96.0 / _dpi + 9 + MetalUi.SwitchW <= half;
        }
        int Pair(int at, int id1, string t1, int id2, string t2, bool last, out nint second)
        {
            if (Fits(t1) && Fits(t2)) { Switch(id1, t1, lx, at, half); second = Switch(id2, t2, rx - half, at, half); }
            else { Switch(id1, t1, lx, at, rx - lx); Sep(at + RH); at += RH + 1; second = Switch(id2, t2, lx, at, rx - lx); }
            if (last) return at + RH;
            Sep(at + RH);
            return at + RH + 1;
        }

        // 시작·권한
        top = y;
        // 자동 실행(0.3.103 시험 A → 0.3.105 시험 B 내장형): 스위치는 실제 등록 상태를 보이고, 바꾸고 [저장]하면 1Key 안의 같은 PowerShell 명령을 연다.
        bool toolOk = Autostart.IsInstalledCopy;   // 시험 B(내장형): 설치 폴더의 도구 파일이 없어도 된다
        Tip(Switch(IdAutoStart, T.SetAutostart, lx, y, rx - lx), !Autostart.IsInstalledCopy ? T.SetAutostartInstalledOnly : toolOk ? T.SetAutostartToolTip : T.SetAutostartToolMissing);
        if (!toolOk) Native.EnableWindow(C(IdAutoStart), false);
        Sep(y + RH); y += RH + 1;
        // 시작 시 트레이 · 트레이 시 위젯모드: 한 줄에 둘(2026-10-06 사용자 — 바로 실행 이름 표시 줄과 같은 모양)
        y = Pair(y, IdStartMin, T.SetStartMin, IdWalker, T.SetWalker, false, out nint wk);
        if (wk != 0) Tip(wk, T.SetWalkerTip);
        Tip(Switch(IdAdmin, T.SetAdmin, lx, y, rx - lx), T.SetAdminNote); y += RH;
        _page.Cards.Add((cx, top, cw, y - top));
        _settingsAdminNote = _settingsDraft?.Admin ?? _cfg.RequireAdmin;

        // 화면: 테마(나란한 버튼, 오른쪽 끝에 글 폭만큼) · 언어(값 글 + 꺾쇠) · 바로 실행 띠의 이름 표시(한 줄에 둘)
        y += Gap;
        top = y;
        int segW = Math.Min(MetalUi.SegWidth(_hwnd, ThemeNames), rx - lx - LabelW(nameFont, T.SetTheme) - 12);
        Name(T.SetTheme, y, rx - lx - segW - 8);
        Make(Dropdown.ClassName, "", Native.WS_TABSTOP | Dropdown.Seg, rx - segW, y + (RH - 34) / 2, segW, 34, IdTheme);
        Sep(y + RH); y += RH + 1;
        int langLabel = Math.Min(LabelW(nameFont, T.SetLanguage) + 12, (rx - lx) / 2);
        Name(T.SetLanguage, y, langLabel);
        Make(Dropdown.ClassName, "", Native.WS_TABSTOP, lx + langLabel, y + (RH - 34) / 2, rx + 6 - lx - langLabel, 34, IdLang);
        Sep(y + RH); y += RH + 1;
        y = Pair(y, IdLaunchProgNames, T.SetLaunchProgNames, IdLaunchFolderNames, T.SetLaunchFolderNames, true, out _);
        _page.Cards.Add((cx, top, cw, y - top));

        // 보안: 자동 잠금(이름 · ‹ 숫자 › · 단계 점) + 마스터·백업 버튼
        y += Gap;
        top = y;
        Label(T.SetAutolock, lx, y + 6, rx - lx, 32, nameFont, ink, true, Native.SS_LEFT | Native.SS_ENDELLIPSIS);
        Tip(Make(Slider.ClassName, "", Native.WS_TABSTOP, lx, y + 38, rx - lx, MetalUi.StepH, IdAutoLock), T.SetLockNoteOn);
        y += 38 + MetalUi.StepH;
        Sep(y); y += 1;
        // 다른 줄과 같은 "왼쪽 이름 · 오른쪽 조작" 모양(2026-10-05 사용자 3안)
        y = LabeledButtons(T.LockLabelMaster, y, false, (IdRowMaster, T.SetBtnMaster));
        y = LabeledButtons(T.RestoreFileHeader, y, true, (IdRowBackup, T.SetBtnBackup), (IdRowRestore, T.SetBtnRestore));
        _page.Cards.Add((cx, top, cw, y - top));

        // 목록에서 넣기(입력 칩의 확정 키) · 도구
        y += Gap;
        top = y;
        int resetW = LabelW(Theme.Sized(12, true), T.CommonDefault) + 28;
        int keyLabel = Math.Min(LabelW(nameFont, T.SetConfirmKey) + 12, (rx - lx) / 2);
        Name(T.SetConfirmKey, y, keyLabel);
        Tip(Make(HotkeyBox.ClassName, "", Native.WS_TABSTOP, lx + keyLabel, y + (RH - 30) / 2, rx - lx - keyLabel - resetW - 8, 30, IdConfirmKey), T.SetChipNote);
        SmallPill(IdConfirmReset, T.CommonDefault, rx - resetW, y, resetW);
        Sep(y + RH); y += RH + 1;
        y = LabeledButtons(T.SetRestartAdmin, y, false, (IdRowRestart, T.SetBtnRestart));
        y = LabeledButtons(T.SetAdvanced, y, true, (IdRowAdvanced, T.SetBtnOpen));
        _page.Cards.Add((cx, top, cw, y - top));

        // 후원(2026-10-09 사용자: 사용성을 해치지 않고 거부감 없는 자리): 설정 맨 아래 한 줄. 목록·트레이에는 두지 않고 저절로 열지 않는다(AppSupport.cs)
        y += Gap;
        top = y;
        y = LabeledButtons(T.SupportTitle, y, true, (IdRowSupport, T.SetBtnOpen));
        _page.Cards.Add((cx, top, cw, y - top));
        return y;
    }

    /// <summary>조각 안의 작은 알약 버튼(높이 28, 줄 가운데). 창은 그늘 자리만큼 크다(MetalUi.Small*).</summary>
    private nint SmallPill(int id, string text, int x, int rowY, int w)
        => Button(id, text, Btn.Bordered, x - MetalUi.SmallPadX, rowY + (MetalUi.SetRowH - MetalUi.SmallPillH) / 2 - MetalUi.SmallPadTop,
                  w + 2 * MetalUi.SmallPadX, MetalUi.SmallPillH + MetalUi.SmallPadTop + MetalUi.SmallPadBottom, onCard: true);

    /// <summary>조각 안 한 줄(새 디자인 설정): 왼쪽 이름(13.5px 굵게), 오른쪽 끝에 작은 알약 버튼(들, 사이 6). last 면 아래 구분선 없음. 끝난 y.</summary>
    private int LabeledButtons(string label, int y, bool last, params (int Id, string Text)[] buttons)
    {
        int cx = DialX + DialPadX, cw = DialW - 2 * DialPadX, lx = cx + Row.PadX, right = cx + cw - Row.PadX;
        nint pf = Theme.Sized(12, true);
        var widths = buttons.Select(b => LabelW(pf, b.Text) + 28).ToArray();
        int total = widths.Sum() + 6 * (buttons.Length - 1);
        Label(label, lx, y, right - lx - total - 8, MetalUi.SetRowH, Theme.Sized(13.5, true), Metal.Ref(Metal.Ink(Theme.IsDark)), true, Native.SS_LEFT | Native.SS_ENDELLIPSIS);
        int x = right - total;
        for (int i = 0; i < buttons.Length; i++) { SmallPill(buttons[i].Id, buttons[i].Text, x, y, widths[i]); x += widths[i] + 6; }
        if (last) return y + MetalUi.SetRowH;
        _page.Separators.Add((lx, y + MetalUi.SetRowH, right - lx));
        return y + MetalUi.SetRowH + 1;
    }

    /// <summary>
    /// 카드 안에 버튼을 왼쪽부터 나란히 놓는다(넘치면 다음 줄). 한 줄씩 쓰던 "›" 행을 모아 높이를 줄인다(2026-10-05 B안).
    /// lastRight: 마지막 버튼은 오른쪽 끝에 붙인다(같은 줄에 들어갈 때). 끝난 y.
    /// </summary>
    private int ButtonFlow(int y, bool lastRight, params (int Id, string Text)[] buttons)
    {
        int left = Margin + Row.PadX, right = Margin + CardW - Row.PadX, x = left, h = 30;
        for (int i = 0; i < buttons.Length; i++)
        {
            var (id, text) = buttons[i];
            int w = Math.Min(right - left, LabelW(_font, text) + 28);
            if (x > left && x + w > right) { x = left; y += h + 8; }
            int bx = lastRight && i == buttons.Length - 1 ? right - w : x;
            Button(id, text, Btn.Bordered, bx, y + 9, w, h, onCard: true);
            x += w + 8;
        }
        return y + h + 18;
    }

    /// <summary>
    /// 설정 전용 화면 (T5). 작은 화면·높은 배율에서 목록 안에 펼칠 자리가 없을 때 쓴다. 본문은 목록 안의 설정과 같고,
    /// [취소]/[저장]은 아래 고정 막대에 있어 본문이 스크롤되어도 늘 보인다.
    /// ←(뒤로)는 입력값을 초안으로 남기고(목록에서 접을 때와 같다), [취소]는 초안을 버린다.
    /// </summary>
    private void BuildSettingsScreen()
    {
        // 새 디자인(시안): 머리줄 = 왼쪽 ‹ 둥근 단추(뒤로) · 가운데 "설정"(바탕 그림) · 오른쪽 [?] 둥근 단추
        int y = MetalHeader(IdSBack, T.ListSettings);
        int dialTop = y;
        y = BuildSettingsBody(y + DialPadX) + DialPadX;
        _page.Dials.Add((DialX, dialTop, DialW, y - dialTop));
        _page.BarTop = y;
        // [취소] [저장]: 오른쪽 여백 22, 사이 8 (시안). 창은 그늘 자리만큼 크다
        y += 14;
        int saveW = BarPillW(T.CommonSave, 72), cancelW = BarPillW(T.CommonCancel);
        int sx = WinW - 22 - saveW;
        BarPill(IdSCancel, T.CommonCancel, Btn.PillMain, sx - 8 - cancelW, y, cancelW);
        BarPill(IdSave, T.CommonSave, Btn.Prominent, sx, y, saveW, isDefault: true);
        _page.DefaultButton = IdSave;
        _page.Height = y + MetalUi.PillMainH + 16;
        FillSettings();
    }

    /// <summary>설정 칸에 값 채우기 (저장하지 않은 초안이 있으면 그 값으로).</summary>
    private void FillSettings()
    {
        var draft = _settingsDraft;
        _autostartOn = Autostart.IsOn();   // 실제 등록 상태(읽기만)
        SetCheck(IdAutoStart, draft?.AutoStart ?? _autostartOn);
        SetCheck(IdStartMin, draft?.StartMin ?? _cfg.StartMinimized);
        SetCheck(IdWalker, draft?.Walker ?? _cfg.Walker);
        SetCheck(IdLaunchProgNames, draft?.ProgNames ?? (_cfg.LaunchNames & 1) != 0);
        SetCheck(IdLaunchFolderNames, draft?.FolderNames ?? (_cfg.LaunchNames & 2) != 0);
        SetCheck(IdAdmin, draft?.Admin ?? _cfg.RequireAdmin);
        Slider.Set(C(IdAutoLock), AutoLockChoices.Select(m => m == 0 ? T.SetLockOffShort : T.SetMinutes(m)).ToArray(), draft?.LockIdx ?? AutoLockIndex(_cfg.AutoLockMinutes));
        HotkeyBox.Set(C(IdConfirmKey), draft?.ConfirmMods ?? _cfg.ConfirmMods, draft?.ConfirmVk ?? _cfg.ConfirmVk);
        Dropdown.Set(C(IdTheme), ThemeNames, draft?.ThemeIdx ?? _cfg.ThemeMode);
        Dropdown.Set(C(IdLang), LangNames, draft?.LangIdx ?? LangIndex(_cfg.Language));
        UpdateSettingsNotes();
    }

    /// <summary>설정 화면의 조건부 안내: 자동 잠금 "사용 안 함"이면 직접 잠그기, 관리자 권한을 켰으면 실행 때 권한 확인.</summary>
    private void UpdateSettingsNotes()
    {
        if (C(IdAutoLock) != 0)
            SetNote(IdSLockNote, Slider.Get(C(IdAutoLock)) == 0 ? T.SetLockNoteOff : T.SetLockNoteOn);
        if (C(IdAdmin) != 0)
            SetNote(IdSAdminNote, GetCheck(IdAdmin) ? T.SetAdminNote : "");
    }

    /// <summary>설정 입력값(또는 초안)이 저장된 값과 다른가. 테마 포함.</summary>
    private bool SettingsDiffer((bool AutoStart, bool StartMin, bool Admin, int LockIdx, uint ConfirmMods, uint ConfirmVk, int ThemeIdx, int LangIdx, bool ProgNames, bool FolderNames, bool Walker) d)
        => d.AutoStart != _autostartOn || d.StartMin != _cfg.StartMinimized || d.Admin != _cfg.RequireAdmin || d.LockIdx != AutoLockIndex(_cfg.AutoLockMinutes)
           || d.ConfirmMods != _cfg.ConfirmMods || d.ConfirmVk != _cfg.ConfirmVk || d.ThemeIdx != _cfg.ThemeMode || d.LangIdx != LangIndex(_cfg.Language)
           || d.ProgNames != ((_cfg.LaunchNames & 1) != 0) || d.FolderNames != ((_cfg.LaunchNames & 2) != 0) || d.Walker != _cfg.Walker;

    /// <summary>테마를 고르면 바로 미리 보인다. 설정 화면을 다시 만들면 입력값이 초안으로 옮겨 담기고, ShowScreen 이 초안의 테마를 적용한다.</summary>
    private void PreviewTheme()
    {
        if (C(IdTheme) == 0) return;
        int idx = Math.Clamp(Dropdown.Get(C(IdTheme)), 0, ThemeNames.Length - 1);
        if (idx == Theme.Mode) return;
        ShowScreen(Screen.Settings);
        if (C(IdTheme) is nint t and not 0) Native.SetFocus(t);   // 고르던 칸에 그대로
    }

    /// <summary>언어를 고르면 바로 미리 보인다(테마와 같은 방식). 설정 화면을 다시 만들면 초안의 언어가 적용된다.</summary>
    private void PreviewLanguage()
    {
        if (C(IdLang) == 0) return;
        int idx = Math.Clamp(Dropdown.Get(C(IdLang)), 0, L.LangCount);
        if ((Lang?)L.Parse(LangCode(idx)) is Lang want ? want == L.Current : L.FromWindows() == L.Current)
        {
            // 보이는 언어는 그대로(예: 한국어 Windows 에서 "따름" ↔ "한국어")여도 초안에는 담아 둔다
            _settingsDraft = ReadSettings();
            return;
        }
        ShowScreen(Screen.Settings);
        if (C(IdLang) is nint h and not 0) Native.SetFocus(h);
    }

    /// <summary>화면 언어를 바꾼다(설정 값: 빈 값 = Windows 따름). 바뀌면 그 언어의 글꼴을 다시 만들고 트레이 설명을 고친다. 화면은 호출한 쪽이 다시 만든다.</summary>
    private void ApplyLanguage(string code)
    {
        if (!L.Apply(code)) return;
        if (_hwnd != 0) CreateFont();
        UpdateTrayTip();
    }

    /// <summary>
    /// 설정의 ←(2026-09-29 사용자 요청): 바뀐 값이 있으면 "저장하지 않으면 적용되지 않는다, 변경을 취소할까" 를 묻는다.
    /// [예] = 변경을 버리고 목록으로(테마도 원래대로), [아니요] = 설정 화면에 남는다. 바뀐 값이 없으면 묻지 않고 목록으로.
    /// </summary>
    private void LeaveSettings()
    {
        if (_cur == Screen.Settings && C(IdAutoStart) != 0 && SettingsDiffer(ReadSettings()))
        {
            int r = Msg(T.SetLeaveConfirm, AppTitle, Native.MB_YESNO | Native.MB_ICONQUESTION);
            if (r != Native.IDYES || _cur != Screen.Settings) return;   // 아니요, 또는 상자가 떠 있는 동안 잠겼다
        }
        DiscardSettings();
    }

    /// <summary>설정 변경을 버리고 목록으로. 테마 미리보기도 ShowScreen 에서 저장된 테마로 돌아간다.</summary>
    private void DiscardSettings()
    {
        _settingsDraft = null;
        _dropSettingsDraft = true;
        ShowScreen(Screen.List);
    }

    /// <summary>슬라이더 단계에 없는 값(옛 설정)은 가장 가까운 단계로.</summary>
    private static int AutoLockIndex(int minutes)
    {
        int idx = 0;
        for (int i = 0; i < AutoLockChoices.Length; i++)
            if (Math.Abs(AutoLockChoices[i] - minutes) < Math.Abs(AutoLockChoices[idx] - minutes)) idx = i;
        return idx;
    }

    private (bool AutoStart, bool StartMin, bool Admin, int LockIdx, uint ConfirmMods, uint ConfirmVk, int ThemeIdx, int LangIdx, bool ProgNames, bool FolderNames, bool Walker) ReadSettings()
    {
        (uint cm, uint cv) = HotkeyBox.Get(C(IdConfirmKey));
        // 자동 실행 스위치: 실제 등록 상태(_autostartOn)에서 바꿨는지만 본다 — 저장값(_cfg.AutoStart)과 비교하지 않는다(허위 변경 확인 방지, Codex R157-2)
        return (C(IdAutoStart) != 0 && Native.IsWindowEnabled(C(IdAutoStart)) ? GetCheck(IdAutoStart) : _autostartOn, GetCheck(IdStartMin), GetCheck(IdAdmin), Math.Clamp(Slider.Get(C(IdAutoLock)), 0, AutoLockChoices.Length - 1), cm, cv,
                Math.Clamp(Dropdown.Get(C(IdTheme)), 0, ThemeNames.Length - 1), Math.Clamp(Dropdown.Get(C(IdLang)), 0, L.LangCount),
                GetCheck(IdLaunchProgNames), GetCheck(IdLaunchFolderNames), GetCheck(IdWalker));
    }

    /// <summary>확정 키가 쓸 수 있는 조합인지. 문제가 있으면 이유, 없으면 null. 비우면(없음) 칩의 [입력] 버튼만 쓴다.</summary>
    private string? ConfirmKeyProblem(uint mods, uint vk)
    {
        if (vk == 0) return null;
        if (mods == 0 && !Keys.AllowsBareHotkey(vk))
            return T.SetConfirmNeedsMod;
        for (int j = 0; j < Config.SlotCount; j++)
        {
            Slot o = _cfg.Slots[j];
            if (SlotInUse(o) && o.HasHotkey && o.Mods == mods && o.Vk == vk)
                return T.SetConfirmSameAs(o.DisplayName(j));
        }
        if (LaunchStore.HotkeyOwner(mods, vk, null) is LaunchItem li) return T.SetConfirmSameAs(li.Name);
        return null;
    }

    private static bool MatchesFilter(Slot s, int index, string filter)
        => s.DisplayName(index).Contains(filter, StringComparison.OrdinalIgnoreCase)
        || (s.HasHotkey && s.HotkeyText().Contains(filter, StringComparison.OrdinalIgnoreCase));

    /// <summary>목록을 위치 top 으로 넘긴다. 검색 칸에 포커스가 있었으면 그대로 둔다.</summary>
    private void ScrollList(int top)
    {
        top = Math.Clamp(top, 0, _listScrollMax);
        if (top == _listTop || _cur != Screen.List) return;
        _listTop = top;
        RefreshList();
    }

    /// <summary>목록 화면을 다시 만든다. 검색 칸에 포커스가 있었으면 포커스와 커서를 돌려 놓는다.</summary>
    private void RefreshList()
    {
        bool filterHadFocus = C(IdListFilter) != 0 && Native.GetFocus() == C(IdListFilter);
        ShowScreen(Screen.List);
        nint e = C(IdListFilter);
        if (filterHadFocus && e != 0) { Native.SetFocus(e); Native.SendMessageW(e, EM_SETSEL, _listFilter.Length, _listFilter.Length); }
    }

    private void SaveList()
    {
        if (_cur != Screen.Settings) return;
        var v = ReadSettings();
        if (ConfirmKeyProblem(v.ConfirmMods, v.ConfirmVk) is string bad)
        {
            Msg(bad, AppTitle, Native.MB_OK | Native.MB_ICONWARNING);
            return;
        }
        // 파일에 들어간 뒤에만 설정을 바꾼다. 실패하면 화면의 값은 그대로 두고(초안), 설정은 이전 값으로 남는다.
        var keep = (_cfg.AutoStart, _cfg.StartMinimized, _cfg.RequireAdmin, _cfg.AutoLockMinutes, _cfg.ConfirmMods, _cfg.ConfirmVk, _cfg.ThemeMode, _cfg.Language, _cfg.LaunchNames, _cfg.Walker);
        // 자동 실행 스위치를 쓸 수 없으면(설치본이 아님) 저장된 값은 그대로 둔다 — 화면의 스위치는 실제 등록 상태만 보여 주므로 그 값으로 덮으면
        // 다른 설정을 저장할 때 autostart=1 이 0 으로 바뀌었다(settingsro SR03, Codex R157-2 의 뜻)
        bool autostartEditable = C(IdAutoStart) != 0 && Native.IsWindowEnabled(C(IdAutoStart));
        (_cfg.AutoStart, _cfg.StartMinimized, _cfg.RequireAdmin, _cfg.AutoLockMinutes, _cfg.ConfirmMods, _cfg.ConfirmVk, _cfg.ThemeMode, _cfg.Language, _cfg.LaunchNames, _cfg.Walker)
            = (autostartEditable ? v.AutoStart : _cfg.AutoStart, v.StartMin, v.Admin, AutoLockChoices[v.LockIdx], v.ConfirmMods, v.ConfirmVk, v.ThemeIdx, LangCode(v.LangIdx), (v.ProgNames ? 1 : 0) | (v.FolderNames ? 2 : 0), v.Walker);
        if (!_cfg.Save())
        {
            (_cfg.AutoStart, _cfg.StartMinimized, _cfg.RequireAdmin, _cfg.AutoLockMinutes, _cfg.ConfirmMods, _cfg.ConfirmVk, _cfg.ThemeMode, _cfg.Language, _cfg.LaunchNames, _cfg.Walker) = keep;
            Msg( SaveFailMsg(), AppTitle, Native.MB_OK | Native.MB_ICONERROR);
            return;
        }
        _settingsDraft = null;
        _dropSettingsDraft = true;
        ShowScreen(Screen.List);   // 다른 화면([저장] 뒤 목록)과 같게. 테마는 이제 저장된 값이다(ShowScreen)

        // 자동 실행: 바꿨으면 창 없이 등록·지우기를 한다(켬 = 관리자 권한으로 실행이 켜져 있으면 관리자 로그온 작업, 아니면 시작 프로그램 바로 가기 / 끔 = 지우기).
        // UAC 만 뜨고 결과는 토스트로 알린다. 실제 상태(_autostartOn)는 다시 읽어 스위치에 반영한다.
        if (v.AutoStart != _autostartOn)
        {
            string tool = v.AutoStart ? (v.Admin ? Autostart.ToolAdmin : Autostart.ToolUser) : Autostart.ToolRemove;
            string? err = Autostart.RunBuiltin(tool);   // 시험 B(내장형). 시험 A 는 RunTool(설치 폴더 tools\*.cmd)
            if (err is not null) Msg(err, AppTitle, Native.MB_OK | Native.MB_ICONWARNING);
            else Toast.Show(_hwnd, Autostart.IsOn() ? T.SetAutostartOn : T.SetAutostartOff, 5000);
        }
        else Toast.Show(_hwnd, T.SetSaved);
        UpdateTrayTip();
        SyncWalker();
    }
}
