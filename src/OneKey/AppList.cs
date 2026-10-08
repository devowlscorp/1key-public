using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>App 의 일부: 목록 화면과 설정. (App.cs 에서 화면별로 나눔, 0.3.9 — 동작은 그대로)</summary>
internal sealed unsafe partial class App
{
    // ---------------------------------------------------------------- 목록 화면

    // 단축키만 있고 비밀번호가 없는 슬롯은 "비어 있음"으로 본다 (예전 버전 기본값이 Ctrl+Alt+1~3 이라서).
    private static bool SlotInUse(Slot s) => s.Name.Length > 0 || s.HasPassword;

    private void BuildList()
    {
        int y = 12;
        // 왼쪽 위: [+ 단축키 추가](2026-10-04 사용자: "단축키" 제목 글자는 필요 없고, 그 자리에 추가 버튼이 효율적 — 목록 아래 추가 행은 없앰).
        // 버튼 id 는 예전 추가 행과 같은 IdRowAdd(하네스·포커스 규약). 99개가 다 차면 비활성 + 아래에 "모두 사용 중" 안내.
        var usedNow = 0;
        for (int i = 0; i < Config.SlotCount; i++) if (SlotInUse(_cfg.Slots[i])) usedNow++;
        string addText = "+  " + T.ListAdd;
        int addW = Math.Min(190, LabelW(Theme.FontStrong, addText) + 32);   // 언어마다 길이가 달라 잰다(오른쪽 [도움말]과 겹치지 않게 한도)
        nint addBtn = Button(IdRowAdd, addText, Btn.Tinted, Margin, y, addW, 32);
        if (addBtn != 0) Native.SendMessageW(addBtn, Native.WM_SETFONT, Theme.FontStrong, 1);
        if (usedNow >= Config.SlotCount && addBtn != 0) Native.EnableWindow(addBtn, false);
        Label("v" + Version, Margin + addW + 8, y + 4, 60, 28, Theme.FontSmall, Theme.SecondaryText, false);   // 같은 이유로 보조 글자 색
        // 오른쪽 위: [최소화] [잠금] [종료] 아이콘 버튼 (흰 상자 + 테두리라 버튼으로 읽힌다). 툴팁으로 이름을 보여 준다.
        Tip(Button(IdListExit, IcPower, Btn.IconBordered, WinW - Margin - 32, y, 32, 32), T.CommonExit);
        Tip(Button(IdListLock, IcLock, Btn.IconBordered, WinW - Margin - 32 - 8 - 32, y, 32, 32), T.CommonLock);
        // 위젯 모드(2026-10-06 사용자)를 켰으면 (−) 대신 위젯(타일) 아이콘: 누르면 트레이로(마스코트가 작업 표시줄 위에)
        Tip(Button(IdListMin, WidgetButton ? IcWidget : IcTray, Btn.IconBordered, WinW - Margin - 32 - 8 - 32 - 8 - 32, y, 32, 32), WidgetButton ? T.ListWidgetMode : T.CommonMinimize);
        Button(IdHelp, T.CommonHelp, Btn.Borderless, WinW - Margin - 32 - 8 - 32 - 8 - 32 - 4 - 64, y, 64, 32);
        y += 44;
        y = BuildLaunchStrip(y);   // 프로그램·폴더 아이콘 띠(항목이 있을 때만)

        // 등록된 단축키
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
            Field(IdListFilter, Margin, y, filtering ? CardW - countW - clearW - 8 : CardW, FieldH, onCard: false);
            Native.SetCueBanner(C(IdListFilter), T.ListSearch);
            Native.SetText(C(IdListFilter), _listFilter);
            if (filtering)
            {
                nint cnt = Make("STATIC", T.ListCount(used.Count, shown.Count), Native.SS_RIGHT | Native.SS_CENTERIMAGE | Native.SS_NOPREFIX,
                                Margin + CardW - countW - clearW - 4, y, countW, FieldH, IdListCount, 0, Theme.FontSmall);
                if (cnt != 0) _staticStyle[cnt] = (Theme.BgBrush, Theme.SecondaryText);
                Tip(Button(IdListClear, IcClear, Btn.Icon, Margin + CardW - clearW, y, clearW, FieldH), T.ListClearSearch);
            }
            y += FieldH + 8;
        }

        // 목록 카드: 최대 _visibleRows 행만 만들고(가상 목록), 나머지는 휠·스크롤 막대로 넘긴다.
        int top = y;
        bool scroll = shown.Count > _visibleRows;
        _listScrollMax = Math.Max(0, shown.Count - _visibleRows);
        _listTop = Math.Clamp(_listTop, 0, _listScrollMax);
        int rowW = scroll ? CardW - ScrollW : CardW;
        int first = scroll ? _listTop : 0, count = Math.Min(_visibleRows, shown.Count - first);
        if (used.Count > 0 && shown.Count == 0)
        {
            Label(T.ListNoMatch, Margin + Row.PadX, y, CardW - 2 * Row.PadX, RowH, _font, Theme.SecondaryText, true);
            y += RowH;
        }
        _listRowsBuilt = Math.Max(0, count);
        for (int k = 0; k < count; k++)
        {
            int i = shown[first + k];
            Slot s = _cfg.Slots[i];
            // 부제: 이 항목을 넣는 방법. 단축키와 사이트 채우기 연결이 있으면 둘 다(사이트는 주소의 호스트만). 행마다 같던 열쇠 아이콘은
            // 정보가 없는 되풀이라 뺐다(2026-09-30 사용자 제안). 연결만 있고 단축키가 없으면 "단축키 없음"을 되풀이하지 않는다.
            SiteLink? firstLink = s.Site ?? s.More.Select(x => x.Site).FirstOrDefault(x => x is not null);
            string? site = s.App is AppLink al ? (al.Usable ? T.ListSubApp(al.FileName) : T.ListSubAppRelink(al.FileName))
                         : firstLink is SiteLink sl ? (LinkedInputs(s) is null ? T.ListSubLinkIncomplete : T.ListSubSite(SiteHost(sl.Url))) : null;
            string keys = !s.HasHotkey ? (site is null ? T.ListSubNoHotkey : "")
                        : _hotkeyFailed[i] ? s.HotkeyText() + "  ·  " + T.ListSubRegFailed
                        : s.HotkeyText() + (s.PressEnter ? "  ·  " + (s.NoEnterInBrowser ? T.ListSubEnterNoBrowser : T.ListSubEnter) : "");
            string sub = !s.HasPassword ? T.ListSubNoContent
                       : site is null ? keys
                       : keys.Length == 0 ? site : keys + "  ·  " + site;
            // 행을 누르면 편집 화면. 오른쪽 [입력]을 누르면 본창이 내려가고 화면 위에 입력 칩이 뜬다(D안, 최종수정안 T11):
            // 사용자가 넣을 칸을 클릭한 뒤 칩의 [입력]이나 확정 키를 누른 그 순간의 칸에 넣는다. 행 자체는 입력하지 않는다.
            // 행은 버튼 자리만큼 좁게 만들고(오른쪽 직각), 버튼은 카드 위에 따로 놓는다. 탭 순서: 행 → 그 행의 [입력].
            int inputArea = InputBtnW + Row.PadX + 8;
            uint flags = Row.SquareRight | Row.EditHint | (k == 0 ? Row.First : 0u) | (k == count - 1 ? Row.Last : 0u);
            nint r = Make(Row.ClassName, s.DisplayName(i), flags | Native.WS_TABSTOP, Margin, y, rowW - inputArea, RowH, IdRowSlot + i);
            // 아이콘으로 넣는 방식을 구분한다(2026-10-01 사용자 요청): 웹 페이지에 연결한 항목은 지구본, 커서 자리에 넣는 항목은 키보드.
            // (예전 열쇠 아이콘은 모든 행에 같아 뺐다. 이제는 행마다 뜻이 다르다.)
            if (r != 0) Row.Set(r, s.DisplayName(i), sub, s.App is not null ? IcApp : s.HasAnySite ? IcSite : IcTyping);
            nint b = Button(IdRowInput + i, T.CommonInput, Btn.Tinted, Margin + rowW - Row.PadX - InputBtnW, y + (RowH - InputBtnH) / 2, InputBtnW, InputBtnH, onCard: true);
            if (b != 0 && !s.HasPassword) Native.EnableWindow(b, false);   // 내용이 없는 항목은 넣을 것이 없다
            if (r != 0 && b != 0)
            {
                Ctl.Partner[r] = b; Ctl.Partner[b] = r;   // 행이나 버튼 어느 쪽에 마우스가 있어도 줄 전체 강조
                _page.RowStrips.Add((r, Margin + rowW - inputArea, y, inputArea, RowH, k == 0, k == count - 1));
            }
            if (k != count - 1) _page.Separators.Add((Margin + rowW - inputArea, y + RowH - 1, inputArea));   // 행의 구분선을 버튼 자리까지 잇는다
            y += RowH;
        }
        if (used.Count > 0) Card(top, y - top);
        if (scroll)
        {
            int trackX = Margin + CardW - ScrollW + 3, trackY = top + 6, trackH = count * RowH - 12;
            int thumbH = Math.Max(24, trackH * _visibleRows / shown.Count);
            int thumbY = trackY + (trackH - thumbH) * _listTop / _listScrollMax;
            _page.ScrollTrack = (trackX, trackY, 4, trackH, thumbY, thumbH);
        }

        // 단축키 추가는 왼쪽 위 버튼(2026-10-04). 한도에 닿았을 때만 이유를 목록 아래에 보인다.
        if (!canAdd)
        {
            // 한도(99개)에 닿으면 추가 행이 사라진 이유를 같은 자리에 보인다 (Codex QA-08). 평소에는 없다.
            y += 8;
            int fullTop = y;
            nint full = Make("STATIC", T.ListFull(Config.SlotCount),
                             Native.SS_LEFT | Native.SS_CENTERIMAGE | Native.SS_NOPREFIX | Native.SS_ENDELLIPSIS, Margin + Row.PadX, y, CardW - 2 * Row.PadX, RowH, IdListFull, 0, Theme.FontSmall);
            if (full != 0) _staticStyle[full] = (Theme.CardBrush, Theme.SecondaryText);
            y += RowH;
            Card(fullTop, RowH);
        }
        // 항목이 있으면 사용법을 되풀이하지 않는다(행의 [입력]·항목 누르기·[도움말]로 찾는다). 처음에만 시작 안내.
        if (used.Count == 0)
            y = Footer(T.ListIntro, y);

        // 설정: 누르면 설정 전용 화면으로 간다(2026-09-29 사용자 결정). 예전에는 목록 아래에 펼쳤는데, 설정이 늘어
        // 1920×1080 에서도 항목 몇 개만 있으면 넘쳐 항목 수에 따라 펼침/전용 화면이 바뀌었다. 동작을 한 가지로 고정한다.
        y += 12;
        int top2 = y;
        var draft = _settingsDraft;
        bool draftDirty = draft is { } d0 && SettingsDiffer(d0);
        ListRow(IdRowSettings, T.ListSettings, draftDirty ? T.ListSettingsDirty : T.ListSettingsSummary, "",
                Row.First | Row.Last | Row.InlineSubtitle | Row.Chevron, y); y += RowH;
        Card(top2, y - top2);

        // 하단. 위쪽은 제목 행(44) 때문에 무게가 있으므로, 아래 여백을 좌우(16)보다 조금 넉넉히 둬야 균형이 맞아 보인다.
        _page.Height = y + 20;
    }

    /// <summary>
    /// 설정 본문. 2026-10-05 사용자 B안: 묶음 제목·칸 아래 설명을 없애고(설명은 [도움말]·마우스를 올리면 나오는 풍선), 한 줄을 차지할 필요 없는 것은
    /// 버튼으로 모아 높이를 줄인다 — 카드 넷: 시작·권한 / 화면(테마는 나란한 버튼, 바로 실행 이름 표시는 한 줄에 둘) / 보안(자동 잠금 + 버튼들) / 넣기·도구.
    /// 컨트롤 번호는 그대로(저장·시험이 같은 번호를 쓴다). 끝난 y 를 돌려준다.
    /// </summary>
    private int BuildSettingsBody(int y)
    {
        int top;
        int col = LabelCol(96, 170, T.SetTheme, T.SetLanguage, T.SetConfirmKey, T.SetAutolock);
        int lx = Margin + Row.PadX, vx = lx + col, vw = CardW - (vx - Margin) - Row.PadX;

        // 시작·권한
        y += 20;
        top = y;
        // 자동 실행(0.3.103 시험 A → 0.3.105 시험 B 내장형): 스위치는 실제 등록 상태를 보이고, 바꾸고 [저장]하면 1Key 안의 같은 PowerShell 명령을 연다.
        bool toolOk = Autostart.IsInstalledCopy;   // 시험 B(내장형): 설치 폴더의 도구 파일이 없어도 된다
        Tip(ToggleRow(IdAutoStart, T.SetAutostart, y, false), !Autostart.IsInstalledCopy ? T.SetAutostartInstalledOnly : toolOk ? T.SetAutostartToolTip : T.SetAutostartToolMissing); y += RowH;
        if (!toolOk) Native.EnableWindow(C(IdAutoStart), false);
        // 시작 시 트레이 · 트레이 시 위젯모드: 한 줄에 둘(2026-10-06 사용자 — 바로 실행 이름 표시 줄과 같은 모양)
        int halfS = (CardW - Row.PadX * 2 - 16) / 2;
        Make(Toggle.ClassName, T.SetStartMin, Toggle.StyleTrailing | Toggle.StyleSeparator | Native.WS_TABSTOP, Margin + Row.PadX, y, halfS, RowH, IdStartMin);
        nint wk = Make(Toggle.ClassName, T.SetWalker, Toggle.StyleTrailing | Toggle.StyleSeparator | Native.WS_TABSTOP, Margin + Row.PadX + halfS + 16, y, halfS, RowH, IdWalker);
        if (wk != 0) Tip(wk, T.SetWalkerTip);
        Separator(y + RowH - 1); y += RowH;
        Tip(ToggleRow(IdAdmin, T.SetAdmin, y, true), T.SetAdminNote); y += RowH;
        Card(top, y - top);
        _settingsAdminNote = _settingsDraft?.Admin ?? _cfg.RequireAdmin;

        // 화면: 테마(나란한 버튼) · 언어 · 바로 실행 띠의 이름 표시(한 줄에 둘)
        y += 16;
        top = y;
        Label(T.SetTheme, lx, y, col - 6, RowH - 1, _font, Theme.ControlText, true);
        Make(Dropdown.ClassName, "", Native.WS_TABSTOP | Dropdown.Seg, vx, y + (RowH - FieldH) / 2, vw, FieldH, IdTheme);
        Separator(y + RowH - 1); y += RowH;
        Label(T.SetLanguage, lx, y, col - 6, RowH - 1, _font, Theme.ControlText, true);
        Make(Dropdown.ClassName, "", Native.WS_TABSTOP, vx, y + (RowH - FieldH) / 2, vw, FieldH, IdLang);
        Separator(y + RowH - 1); y += RowH;
        int half = (CardW - Row.PadX * 2 - 16) / 2;
        Make(Toggle.ClassName, T.SetLaunchProgNames, Toggle.StyleTrailing | Native.WS_TABSTOP, Margin + Row.PadX, y, half, RowH, IdLaunchProgNames);
        Make(Toggle.ClassName, T.SetLaunchFolderNames, Toggle.StyleTrailing | Native.WS_TABSTOP, Margin + Row.PadX + half + 16, y, half, RowH, IdLaunchFolderNames);
        y += RowH;
        Card(top, y - top);

        // 보안: 자동 잠금 + 마스터·백업 버튼
        y += 16;
        top = y;
        // 아래 구분선이 보이도록 글자·슬라이더를 행 높이보다 1px 짧게 둔다
        Label(T.SetAutolock, Margin + Row.PadX, y, CardW - 2 * Row.PadX - 220 - 8, SliderRowH - 1, _font, Theme.ControlText, true);
        Tip(Make(Slider.ClassName, "", Native.WS_TABSTOP, Margin + CardW - Row.PadX - 220, y, 220, SliderRowH - 1, IdAutoLock), T.SetLockNoteOn);
        Separator(y + SliderRowH - 1);
        y += SliderRowH;
        // 다른 줄과 같은 "왼쪽 이름 · 오른쪽 조작" 모양(2026-10-05 사용자 3안)
        y = LabeledButtons(T.LockLabelMaster, y, false, (IdRowMaster, T.SetBtnMaster));
        y = LabeledButtons(T.RestoreFileHeader, y, true, (IdRowBackup, T.SetBtnBackup), (IdRowRestore, T.SetBtnRestore));
        Card(top, y - top);

        // 목록에서 넣기(입력 칩의 확정 키) · 도구
        y += 16;
        top = y;
        Label(T.SetConfirmKey, lx, y, col - 6, RowH - 1, _font, Theme.ControlText, true);
        Tip(Make(HotkeyBox.ClassName, "", Native.WS_TABSTOP, vx, y + (RowH - FieldH) / 2, vw - 64 - 8, FieldH, IdConfirmKey), T.SetChipNote);
        Button(IdConfirmReset, T.CommonDefault, Btn.Bordered, Margin + CardW - Row.PadX - 64, y + (RowH - FieldH) / 2, 64, FieldH, onCard: true);
        Separator(y + RowH - 1); y += RowH;
        y = LabeledButtons(T.SetRestartAdmin, y, false, (IdRowRestart, T.SetBtnRestart));
        y = LabeledButtons(T.SetAdvanced, y, true, (IdRowAdvanced, T.SetBtnOpen));
        Card(top, y - top);
        return y + 12;
    }

    /// <summary>카드 안 한 줄: 왼쪽 이름, 오른쪽 끝에 작은 버튼(들). last 면 아래 구분선 없음. 끝난 y.</summary>
    private int LabeledButtons(string label, int y, bool last, params (int Id, string Text)[] buttons)
    {
        int right = Margin + CardW - Row.PadX, h = 30, x = right;
        var widths = buttons.Select(b => LabelW(_font, b.Text) + 28).ToArray();
        int total = widths.Sum() + 6 * (buttons.Length - 1);
        Label(label, Margin + Row.PadX, y, CardW - 2 * Row.PadX - total - 8, last ? RowH : RowH - 1, _font, Theme.ControlText, true, Native.SS_LEFT | Native.SS_ENDELLIPSIS);
        x = right - total;
        for (int i = 0; i < buttons.Length; i++) { Button(buttons[i].Id, buttons[i].Text, Btn.Bordered, x, y + (RowH - h) / 2, widths[i], h, onCard: true); x += widths[i] + 6; }
        if (!last) Separator(y + RowH - 1);
        return y + RowH;
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
        Button(IdSBack, T.CommonBack, Btn.Back, Margin, 12, 94, 32);
        Label(T.ListSettings, 110, 12, WinW - 220, 32, Theme.FontStrong, Theme.ControlText, false, Native.SS_CENTER | Native.SS_ENDELLIPSIS);
        HelpButton();
        int y = BuildSettingsBody(36);
        _page.BarTop = y;
        y += 20;
        Button(IdSCancel, T.CommonCancel, Btn.Bordered, WinW - Margin - 96 - 8 - 72, y, 72, 34);
        Button(IdSave, T.CommonSave, Btn.Prominent, WinW - Margin - 96, y, 96, 34, isDefault: true);
        _page.DefaultButton = IdSave;
        _page.Height = y + 34 + Margin;
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
        (_cfg.AutoStart, _cfg.StartMinimized, _cfg.RequireAdmin, _cfg.AutoLockMinutes, _cfg.ConfirmMods, _cfg.ConfirmVk, _cfg.ThemeMode, _cfg.Language, _cfg.LaunchNames, _cfg.Walker)
            = (v.AutoStart, v.StartMin, v.Admin, AutoLockChoices[v.LockIdx], v.ConfirmMods, v.ConfirmVk, v.ThemeIdx, LangCode(v.LangIdx), (v.ProgNames ? 1 : 0) | (v.FolderNames ? 2 : 0), v.Walker);
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
