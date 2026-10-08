using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>App 의 일부: 비밀번호 항목 편집 화면. (App.cs 에서 화면별로 나눔, 0.3.9 — 동작은 그대로)</summary>
internal sealed unsafe partial class App
{
    // ---------------------------------------------------------------- 편집 화면

    /// <param name="form">새 항목의 양식([+ 추가]에서 고른 것). 저장된 항목은 그 항목의 양식(예전 판 항목은 연속 입력 화면).</param>
    private void OpenEdit(int slot, int form = FormPhrase)
    {
        _editSlot = slot;
        _editDirty = false;
        _editPwVisible = false;
        Slot os = _cfg.Slots[slot];
        bool isNew = !SlotInUse(os);
        _editForm = isNew ? form : os.Form is FormPhrase or FormLogin or FormMulti ? os.Form : FormMulti;
        _editInputs = isNew && _editForm == FormLogin ? 2 : os.InputCount;   // 로그인 = ID·PW 두 칸으로 시작
        _editSites = new SiteLink?[Slot.MaxInputs];
        for (int k = 0; k < Math.Min(_editInputs, os.InputCount); k++) _editSites[k] = os.SiteOf(k);   // 새 로그인은 빈 슬롯(입력 1개)보다 칸이 많다
        _editApp = os.App;
        // 용도: 새 로그인 항목은 사이트·앱 연결, 새 문구·연속 입력은 커서 자리(2026-10-06 사용자). 저장된 항목은 연결 여부대로
        _editLinkMode = isNew ? _editForm == FormLogin : _editApp is not null || _editSites.Any(x => x is not null);
        _editClipNote = os.Method == InputMethod.Clipboard;
        // 새 항목은 화면 클릭을 기본으로 하지 않는다(2026-10-03 단순화, Codex 05:45-D). 저장된 항목은 그 값을 그대로 보존한다(05:45-E).
        _editNoClick = SlotInUse(os) ? os.NoClick : true;
        _editPerInput = false;
        // 녹음 칸에서 현재 단축키를 누르면 비밀번호가 주입되지 않도록 잠시 푼다.
        for (int i = 0; i < Config.SlotCount; i++) Native.UnregisterHotKey(_hwnd, i);
        ShowScreen(Screen.Edit);
    }

    private void BuildEdit()
    {
        Slot s = _cfg.Slots[_editSlot];
        bool isNew = !SlotInUse(s);

        // 상단 막대
        Button(IdEBack, T.CommonBack, Btn.Back, Margin, 12, 94, 32);
        string newTitle = _editForm == FormLogin ? T.AddLogin : _editForm == FormMulti ? T.AddMulti : T.AddPassword;   // 새 항목은 고른 양식 이름
        Label(isNew ? newTitle : s.DisplayName(_editSlot), 110, 12, WinW - 220, 32, Theme.FontStrong, Theme.ControlText, false, Native.SS_CENTER | Native.SS_ENDELLIPSIS, userText: true);
        HelpButton();

        // 2026-10-05 사용자 B안: 한 줄을 차지할 필요 없는 것은 버튼으로, 묶음 제목·칸 아래 설명은 없앤다(설명은 [도움말]).
        // 넣는 곳·Enter 는 나란한 버튼(Dropdown.Seg), [+ 입력 추가]는 마지막 입력 줄의 + 버튼, 고급은 한 줄로 접어 둔다.
        int col = LabelCol(86, 150, T.EditName, T.EditContent, T.EditCombo, T.EditMethod, T.EditMode, T.EditEnterLabel);   // 이름표 열: 언어마다 길이가 달라 잰다
        int labelX = Margin + Row.PadX, valueX = labelX + col, valueW = CardW - (valueX - Margin) - Row.PadX, labelW = col - 6;
        int y = 60;
        int top = y;
        Label(T.EditName, labelX, y, labelW, RowH - 1, _font, Theme.ControlText, true);
        Field(IdEName, valueX, y + (RowH - FieldH) / 2, valueW, FieldH);
        Separator(y + RowH - 1); y += RowH;
        // 용도: 고른 쪽에 따라 아래 입력마다 [연결] 줄이 생기거나 없어진다(연결이 필요한지 사용자가 따로 판단하지 않게)
        Label(T.EditMode, labelX, y, labelW, RowH - 1, _font, Theme.ControlText, true);
        Make(Dropdown.ClassName, "", Native.WS_TABSTOP | Dropdown.Seg, valueX, y + (RowH - FieldH) / 2, valueW, FieldH, IdEMode);
        Separator(y + RowH - 1); y += RowH;
        bool multi = _editInputs > 1;
        // 연결 쪽이면 [연결]/[해제]는 그 입력의 행 오른쪽 끝, 연결한 칸 설명은 그 입력 아래 한 줄(넘치면 …, 마우스를 올리면 전체).
        // 2026-10-02 사용자: 버튼은 입력과 같은 행으로, 설명은 간략히 한 줄로.
        const int linkBtnW = 64;
        int smallH = (int)Math.Ceiling(Native.TextHeight(_hwnd, Theme.FontSmall) * 96.0 / _dpi);
        // 양식(2026-10-06 사용자): 문구 = 한 칸(+ 없음), 로그인 = ID(가리지 않음)·PW(가림, 눈 단추) 두 칸 고정, 연속 입력 = 예전 화면(+·×)
        bool login = _editForm == FormLogin && _editInputs == 2;
        for (int k = 0; k < _editInputs; k++)
        {
            if (k > 0) Separator(y - 1);
            string label = login ? (k == 0 ? T.EditLoginId : T.EditLoginPw) : multi ? T.EditInput(k + 1) : T.EditContent;
            Label(label, labelX, y, labelW, RowH, _font, Theme.ControlText, true);
            bool rowBtn = _editLinkMode && (k == 0 || _editPerInput || _editInputs != 2);   // 입력 2개는 [연결] 한 번(입력 1 행)으로 짝까지
            bool addBtn = _editForm == FormMulti && k == _editInputs - 1 && _editInputs < Slot.MaxInputs;   // 입력 추가: 연속 입력 양식만, 마지막 입력 줄 끝의 +
            int fieldW = valueW - 36 - (addBtn ? 32 : 0) - (rowBtn ? linkBtnW + 8 : 0);
            bool hide = !(login && k == 0);   // 로그인의 ID 는 그냥 보인다
            Field(InTextId(k), valueX, y + (RowH - FieldH) / 2, fieldW, FieldH, hide ? Native.ES_PASSWORD : 0);
            if (login) { if (k == 1) Button(IdEShow, IcEye, Btn.Icon, valueX + fieldW + 8, y + (RowH - 28) / 2, 28, 28, onCard: true); }   // PW 만 보이기/숨기기
            else if (k == 0) Button(IdEShow, IcEye, Btn.Icon, valueX + fieldW + 8, y + (RowH - 28) / 2, 28, 28, onCard: true);   // 모든 입력을 함께 보이기/숨기기
            else Button(InDelId(k), IcClear, Btn.Icon, valueX + fieldW + 8, y + (RowH - 28) / 2, 28, 28, onCard: true);
            if (addBtn) Tip(Button(IdEInAdd, IcAdd, Btn.Icon, valueX + fieldW + 8 + 32, y + (RowH - 28) / 2, 28, 28, onCard: true), T.EditAddInputTip);
            if (rowBtn) Button(SiteBtnId(k), T.EditSiteLink, Btn.Bordered, valueX + valueW - linkBtnW, y + (RowH - FieldH) / 2, linkBtnW, FieldH, onCard: true);
            y += RowH;
            if (!_editLinkMode) continue;
            nint siteText = Make("STATIC", "", Native.SS_LEFT | Native.SS_NOPREFIX | Native.SS_ENDELLIPSIS | 0x0100 /* SS_NOTIFY: 설명 풍선 */,
                                 valueX, y - 6, valueW, smallH + 2, SiteTextId(k), 0, Theme.FontSmall);
            if (siteText != 0) _staticStyle[siteText] = (Theme.CardBrush, Theme.SecondaryText);
            y += smallH + 4;
        }
        Card(top, y - top);
        y += 16;

        top = y;
        Label(T.EditCombo, labelX, y, labelW, RowH - 1, _font, Theme.ControlText, true);
        Make(HotkeyBox.ClassName, "", Native.WS_TABSTOP, valueX, y + (RowH - FieldH) / 2, valueW, FieldH, IdEHotkey);
        Separator(y + RowH - 1); y += RowH;
        // Enter 전송: 안함 / 전송 / 브라우저 제외 전송 — 예전 두 스위치(그리고 "Enter 를 켜야만 고를 수 있는" 숨은 규칙)를 한 줄로
        Label(T.EditEnterLabel, labelX, y, labelW, RowH, _font, Theme.ControlText, true);
        Make(Dropdown.ClassName, "", Native.WS_TABSTOP | Dropdown.Seg, valueX, y + (RowH - FieldH) / 2, valueW, FieldH, IdEEnter);
        Separator(y + RowH - 1); y += RowH;
        // 입력 방식: 접는 "고급 설정" 묶음 없이 이 카드의 한 줄(2026-10-05 사용자: 하나뿐인데 묶음이 필요한가, "기본값"은 뭔가)
        Label(T.EditMethod, labelX, y, labelW, RowH, _font, Theme.ControlText, true);
        Make(Dropdown.ClassName, "", Native.WS_TABSTOP, valueX, y + (RowH - FieldH) / 2, valueW, FieldH, IdEMethod);
        y += RowH;
        Card(top, y - top);
        if (_editClipNote) y = Note(IdEMethodNote, y, T.EditNoteClipboard);   // 클립보드 붙여넣기를 고른 동안만(고르거나 바꾸면 화면을 다시 만든다)
        y += 16;

        // 사이트·앱 연결 항목에서만 쓰는 스위치: 그 항목일 때만 보인다
        if (_editLinkMode)
        {
            top = y;
            // 칸을 화면에서 직접 클릭해 커서 넣기: 새 항목은 꺼짐. 켜도 Nexacro·판별 불가 화면에서는 하지 않는다(오착 경고는 설명 풍선)
            if (_editInputs == 2) { Tip(ToggleRow(IdEPerInput, T.EditPerInput, y, false), T.EditPerInputTip); y += RowH; }
            Tip(ToggleRow(IdEAllowClick, T.EditAllowClick, y, true), T.EditAllowClickTip); y += RowH;
            Card(top, y - top);
            y += 16;
        }
        _page.BarTop = y;   // 여기부터 아래 고정 막대 (T5)
        y += 20;
        Button(IdETest, T.CommonTest, Btn.Bordered, Margin, y, 72, 34);
        if (!isNew) Button(IdEDelete, T.CommonDelete, Btn.DangerBordered, Margin + 80, y, 64, 34);
        Button(IdECancel, T.CommonCancel, Btn.Bordered, WinW - Margin - 80 - 8 - 72, y, 72, 34);
        Button(IdESave, T.CommonSave, Btn.Prominent, WinW - Margin - 80, y, 80, 34, isDefault: true);
        _page.DefaultButton = IdESave;
        _page.Height = y + 34 + Margin;

        // 값 채우기
        Native.SetText(C(IdEName), s.Name);
        for (int k = 0; k < _editInputs; k++) Native.SetText(C(InTextId(k)), k < s.InputCount ? s.ValueOf(k) : "");
        HotkeyBox.Set(C(IdEHotkey), s.Mods, s.Vk);
        Dropdown.Set(C(IdEEnter), EnterNames, EnterIndex(s));
        SetCheck(IdEAllowClick, !_editNoClick);
        SetCheck(IdEPerInput, _editPerInput);
        UpdateEditNotes((int)s.Method);
        Dropdown.Set(C(IdEMethod), MethodNames, (int)s.Method);
        Dropdown.Set(C(IdEMode), ModeNames, _editLinkMode ? 1 : 0);
        Native.SetCueBanner(C(IdEName), T.EditCueName(_editSlot + 1));
        if (!login) Native.SetCueBanner(C(IdEPw), T.EditCuePw);
        UpdateSiteRow();
        _editDirty = false;
    }

    private Slot ReadEdit()
    {
        (uint mods, uint vk) = HotkeyBox.Get(C(IdEHotkey));
        int m = Dropdown.Get(C(IdEMethod));
        return new Slot
        {
            Name = Native.GetWindowText(C(IdEName)).Trim(),
            Password = Native.GetWindowText(C(IdEPw)),
            Mods = mods, Vk = vk,
            Method = (InputMethod)Math.Clamp(m, 0, MethodNames.Length - 1),
            Site = _editSites[0],
            App = _editApp,
            More = Enumerable.Range(1, Math.Max(_editInputs - 1, 0)).Select(k => new ExtraInput(Native.GetWindowText(C(InTextId(k))), _editSites[k])).ToList(),
            PressEnter = Dropdown.Get(C(IdEEnter)) >= 1,
            NoEnterInBrowser = Dropdown.Get(C(IdEEnter)) == 2,   // Enter 전송: 0 안함, 1 전송, 2 브라우저 제외 전송
            NoClick = _editLinkMode && C(IdEAllowClick) != 0 ? !GetCheck(IdEAllowClick) : _editNoClick,   // 그 칸이 없는 화면(커서 쪽)에서는 값만 유지
            Form = _editForm,
        };
    }

    /// <summary>Enter 전송의 고를 것(0 안함, 1 전송, 2 브라우저 제외 전송).</summary>
    private static string[] EnterNames => new[] { T.EditEnterNo, T.EditEnterYes, T.EditEnterNotBrowser };
    private static int EnterIndex(Slot s) => !s.PressEnter ? 0 : s.NoEnterInBrowser ? 2 : 1;

    /// <summary>입력 방식이 클립보드 붙여넣기일 때만 그림·파일 주의. method &lt; 0 이면 드롭다운에서 읽는다.</summary>
    private void UpdateEditNotes(int method = -1)
    {
        if (method < 0 && C(IdEMethod) != 0) method = Dropdown.Get(C(IdEMethod));
        if (C(IdEMethod) != 0)
            SetNote(IdEMethodNote, method == (int)InputMethod.Clipboard ? T.EditNoteClipboard : "");
    }

    private void SaveEdit()
    {
        Slot e = ReadEdit();

        if (e.Vk != 0 && e.Mods == 0 && !Keys.AllowsBareHotkey(e.Vk))
        {
            Msg(T.EditNeedMod, AppTitle, Native.MB_OK | Native.MB_ICONWARNING);
            return;
        }
        if (e.HasHotkey && _cfg.ConfirmVk != 0 && e.Mods == _cfg.ConfirmMods && e.Vk == _cfg.ConfirmVk)
        {
            Msg(T.EditIsConfirmKey(HotkeyBox.Text(e.Mods, e.Vk)), AppTitle, Native.MB_OK | Native.MB_ICONWARNING);
            return;
        }
        for (int j = 0; j < Config.SlotCount; j++)
        {
            if (j == _editSlot) continue;
            Slot o = _cfg.Slots[j];
            if (!SlotInUse(o)) continue;   // 빈 슬롯에 남은 예전 기본값(Ctrl+Alt+2, 3)은 쓰는 게 아니다
            if (e.HasHotkey && o.HasHotkey && o.Vk == e.Vk && o.Mods == e.Mods)
            {
                Msg(T.EditDupHotkey(o.DisplayName(j)), AppTitle, Native.MB_OK | Native.MB_ICONWARNING);
                return;
            }
        }
        if (e.HasHotkey && LaunchStore.HotkeyOwner(e.Mods, e.Vk, null) is LaunchItem li)   // 프로그램·폴더 단축키와도 겹치면 안 된다
        {
            Msg(T.EditDupHotkey(li.Name), AppTitle, Native.MB_OK | Native.MB_ICONWARNING);
            return;
        }

        // 입력이 둘 이상이면 모든 입력에 내용이 있어야 한다: 빈 입력은 단축키의 Tab 순서와 연결을 헷갈리게 한다
        if (e.More.Count > 0)
            for (int k = 0; k < e.InputCount; k++)
                if (e.ValueOf(k).Length == 0)
                {
                    Msg(T.EditInputEmpty(k + 1), AppTitle, Native.MB_OK | Native.MB_ICONWARNING);
                    Native.SetFocus(C(InTextId(k)));
                    return;
                }

        // 커서 쪽 항목에는 연결을 저장하지 않는다(전환할 때 지우지만, 그 흐름에만 기대지 않고 저장 직전에도 맞춘다, Codex 06:48 §1)
        if (!_editLinkMode)
        {
            e.Site = null; e.App = null; e.NoClick = false;
            e.More = e.More.Select(x => x with { Site = null }).ToList();
        }
        // 연결한 칸에 넣는 항목은 연결이 있어야 한다(웹은 모든 입력, 프로그램은 입력 1)
        if (_editLinkMode && RouteOf(e) == FillRoute.Typing)
        {
            Msg(T.EditNeedLinks, AppTitle, Native.MB_OK | Native.MB_ICONWARNING);
            return;
        }
        // 사이트 연결은 모든 입력을 같은 주소에 연결하거나 모두 해제 (R75-1: 일부만 연결된 항목은 실행 때도 넣지 않지만 저장부터 막는다)
        if (RouteOf(e) == FillRoute.Incomplete)
        {
            Msg(T.EditLinksIncomplete, AppTitle, Native.MB_OK | Native.MB_ICONWARNING);
            return;
        }

        Slot s = _cfg.Slots[_editSlot];
        var keep = (s.Name, s.Password, s.Mods, s.Vk, s.Method, s.PressEnter, s.NoEnterInBrowser, s.Site, s.More, s.App, s.NoClick, s.Form);
        s.Name = e.Name; s.Password = e.Password; s.Mods = e.Mods; s.Vk = e.Vk; s.Method = e.Method; s.PressEnter = e.PressEnter; s.NoEnterInBrowser = e.NoEnterInBrowser; s.Site = e.Site; s.More = e.More; s.App = e.App; s.NoClick = e.NoClick; s.Form = e.Form;

        if (!_cfg.Save())
        {
            (s.Name, s.Password, s.Mods, s.Vk, s.Method, s.PressEnter, s.NoEnterInBrowser, s.Site, s.More, s.App, s.NoClick, s.Form) = keep;   // 파일과 메모리가 어긋나지 않도록 되돌린다. 화면의 값은 그대로다.
            Msg( SaveFailMsg(), AppTitle, Native.MB_OK | Native.MB_ICONERROR);
            return;
        }
        _editDirty = false;
        _quietHotkeyReport = true;
        try { ShowScreen(Screen.List); }   // 여기서 단축키가 다시 등록된다 (결과는 아래에서 한 번만 보여 준다)
        finally { _quietHotkeyReport = false; }
        string report = RegisterHotkeys(silent: false);
        if (report.Length > 0) Dialog.Show(_hwnd, report, AppTitle, Native.MB_OK | Native.MB_ICONWARNING, share: "hotkeys");
        UpdateTrayTip();
    }

    private void DeleteEdit()
    {
        Slot s = _cfg.Slots[_editSlot];
        int r = Msg(T.EditDeleteConfirm(s.DisplayName(_editSlot)), AppTitle, Native.MB_YESNO | Native.MB_ICONWARNING);
        if (r != Native.IDYES) return;
        if (_cur != Screen.Edit || !Unlocked) return;   // 확인 상자가 떠 있는 동안 잠겼거나 화면이 바뀌었다
        // 파일에 반영된 뒤에만 메모리를 바꾼다. 저장이 실패하면 아무것도 지워지지 않은 상태로 남긴다.
        var keep = (s.Name, s.Password, s.Mods, s.Vk, s.Method, s.PressEnter, s.NoEnterInBrowser, s.Site, s.More, s.App, s.Form);
        s.Name = ""; s.Password = ""; s.Mods = 0; s.Vk = 0; s.Method = InputMethod.Auto; s.PressEnter = false; s.NoEnterInBrowser = false; s.Site = null; s.More = new(); s.App = null; s.Form = 0;
        if (!_cfg.Save())
        {
            (s.Name, s.Password, s.Mods, s.Vk, s.Method, s.PressEnter, s.NoEnterInBrowser, s.Site, s.More, s.App, s.Form) = keep;
            Msg(T.EditNotDeleted + " " + SaveFailMsg(), AppTitle, Native.MB_OK | Native.MB_ICONERROR);
            return;
        }
        if (_chipSlot == _editSlot) ReleaseChip();   // T15/T11: 지운 항목의 입력 대기는 남기지 않는다
        _editDirty = false;
        ShowScreen(Screen.List);
        UpdateTrayTip();
    }

    private void LeaveEdit()
    {
        if (_editDirty)
        {
            int r = Msg(T.EditDiscard, AppTitle, Native.MB_YESNO | Native.MB_ICONQUESTION);
            if (r != Native.IDYES) return;
            if (_cur != Screen.Edit) return;   // 확인 상자가 떠 있는 동안 화면이 바뀌었다(잠금 등)
        }
        ShowScreen(Screen.List);
    }
}
