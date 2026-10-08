namespace OneKey;

/// <summary>
/// 바로 실행의 웹사이트 즐겨찾기 화면 쪽(2026-10-06 사용자 결정, Codex 02:46 조건): [+ 추가] › 웹사이트, 편집 화면(이름·주소·브라우저·아이콘·단축키),
/// 아이콘(사용자 이미지 → 받아 둔 파비콘 → 글자), 저장 때 한 번 파비콘 받기(백그라운드, 늦은 결과 폐기), [테스트].
/// 저장·검사는 LaunchStore, 실행은 Launcher.WorkUrl, 이미지·받기는 WebIcon.
/// </summary>
internal sealed unsafe partial class App
{
    private const int IdAkWeb = 4005, IdLeUrl = 4022, IdLeBrowser = 4023, IdLeTest = 4024, IdLeColor = 4025;
    private const int IdStripPrev3 = 4147, IdStripNext3 = 4148;   // 웹사이트 줄의 ‹ ›
    private const uint WM_FAV_READY = Native.WM_APP + 45;         // 파비콘 받기가 끝남(결과는 _favDone)
    private const uint WM_WEBPICK_READY = Native.WM_APP + 46;     // 고른 이미지 읽기·검사가 끝남: wParam = 고르기 번호

    private static readonly string[] BrowserKeys = { "default", "edge", "chrome" };
    private static string[] BrowserNames => new[] { T.LaunchBrowserDefault, "Edge", "Chrome" };
    // 글자 아이콘 바탕색(0.3.93 사용자): 0 = 기본(테마 강조색), 그다음 WebIcon.LetterColors 순서
    private static string[] ColorNames => new[] { T.LaunchColorDefault, T.LaunchColorBlue, T.LaunchColorGreen, T.LaunchColorOrange, T.LaunchColorRed, T.LaunchColorPurple, T.LaunchColorGray };
    private static string ColorKeyAt(int i) => i >= 1 && i <= WebIcon.LetterColors.Length ? WebIcon.LetterColors[i - 1].Key : "";
    private static int ColorIndexOf(string key) { for (int i = 0; i < WebIcon.LetterColors.Length; i++) if (WebIcon.LetterColors[i].Key == key) return i + 1; return 0; }
    private string WebColorField() => C(IdLeColor) is nint h and not 0 ? ColorKeyAt(Dropdown.Get(h)) : _launchEdit?.Color ?? "";

    // 편집 초안의 아이콘: 고른 이미지(검사 통과한 원본 바이트 — 저장 때 다시 그려 쓴다) / [기본]을 눌러 사용자 이미지를 지울 것
    private byte[]? _webUserImage;
    private bool _webUserClear;
    // 항목 id → 아이콘 파일 세대(파일을 쓰거나 지우면 올린다 — 띠가 다시 읽게)
    private readonly Dictionary<string, int> _webIconGen = new();
    // 글자 아이콘 캐시: id → (이름·색 열쇠, HICON)
    private readonly Dictionary<string, (string Key, nint Icon)> _letterIcons = new();

    // 파비콘 받기: 항목 id → 기다리는 요청(번호·주소). 결과는 작업 스레드가 _favDone 에 넣고 WM_FAV_READY.
    private int _favNo;
    private readonly Dictionary<string, (int No, string Target)> _favWant = new();
    private static readonly object _favLock = new();
    private static readonly List<(int No, string Id, byte[]? Png)> _favDone = new();
    private static readonly HashSet<int> _favCancelled = new();
    private int _testFavOk, _testFavFail, _testFavDropped;

    // 이미지 고르기(Codex R78-1·2): 고르기 번호, 기다리는 고르기(시작 때 상태·항목), 작업 스레드의 결과
    private int _webPickNo, _testPickDropped;
    private (JobToken Tk, string Id, int No)? _webPickWait;
    private static readonly Dictionary<int, byte[]?> _webPickDone = new();   // 고르기 번호 → 읽고 검사한 결과(null = 쓸 수 없는 이미지)

    private void ResetWebDraft() { _webUserImage = null; _webUserClear = false; }

    // ------------------------------------------------------------------ 추가·편집 화면

    private void OnAddWeb()
    {
        if (LaunchStore.ReadOnly || LaunchStore.Items.Count >= LaunchStore.Max) return;
        var it = new LaunchItem { Id = LaunchStore.NewId(), Kind = "url", Browser = "default", Valid = true };
        OpenLaunchEdit(-1, it);
    }

    private void BuildWebEdit()
    {
        LaunchItem e = _launchEdit!;
        bool isNew = _launchEditIndex < 0;
        int metalTop = MetalHeader(IdLeBack, isNew ? T.AddWeb : e.Name, help: false);   // 새 디자인 머리줄(‹ · 제목)

        int col = LabelCol(86, 150, T.LaunchName, T.LaunchUrl, T.LaunchBrowser, T.LaunchIcon, T.LaunchColor, T.EditCombo);
        int labelX = Mx + Row.PadX, valueX = labelX + col, valueW = Mw - (valueX - Mx) - Row.PadX, labelW = col - 6;
        int y = 60, top = y;   // 판 안쪽 위 여백
        Label(T.LaunchName, labelX, y, labelW, RowH - 1, _font, Theme.ControlText, true);
        Field(IdLeName, valueX, y + (RowH - FieldH) / 2, valueW, FieldH);
        Separator(y + RowH - 1); y += RowH;
        Label(T.LaunchUrl, labelX, y, labelW, RowH - 1, _font, Theme.ControlText, true);
        Field(IdLeUrl, valueX, y + (RowH - FieldH) / 2, valueW, FieldH);
        Separator(y + RowH - 1); y += RowH;
        Label(T.LaunchBrowser, labelX, y, labelW, RowH - 1, _font, Theme.ControlText, true);
        Make(Dropdown.ClassName, "", Native.WS_TABSTOP | Dropdown.Seg, valueX, y + (RowH - FieldH) / 2, valueW, FieldH, IdLeBrowser);
        Separator(y + RowH - 1); y += RowH;
        // 아이콘: 미리보기 + [바꾸기](이미지 파일) + [기본](사용자 이미지를 지우고 받아 둔 파비콘·글자로)
        Label(T.LaunchIcon, labelX, y, labelW, RowH + 8 - 1, _font, Theme.ControlText, true);
        nint pv = Make(Tile.ClassName, "", 0, valueX, y + 4, 42, 42, IdLePreview);
        if (pv != 0) { Tile.Set(pv, WebPreviewIcon(e), false, false, "", onCard: true); Tip(pv, T.LaunchWebIconTip); }
        nint ch = Button(IdLeIcon, T.LaunchIconChange, Btn.Bordered, valueX + 52, y + (RowH + 8 - FieldH) / 2, 88, FieldH, onCard: true);
        if (ch != 0) Tip(ch, T.LaunchWebIconTip);
        nint rs = Button(IdLeIconReset, T.LaunchIconReset, Btn.Bordered, valueX + 52 + 96, y + (RowH + 8 - FieldH) / 2, 72, FieldH, onCard: true);
        if (rs != 0 && !HasUserIcon(e)) Native.EnableWindow(rs, false);
        Separator(y + RowH + 8 - 1); y += RowH + 8;
        // 글자 아이콘 바탕색: 이미지로 바꾼 아이콘에는 쓰이지 않으므로 그때는 끈다
        Label(T.LaunchColor, labelX, y, labelW, RowH - 1, _font, Theme.ControlText, true);
        nint cd = Make(Dropdown.ClassName, "", Native.WS_TABSTOP, valueX, y + (RowH - FieldH) / 2, Math.Min(valueW, 160), FieldH, IdLeColor);
        if (cd != 0) { Tip(cd, T.LaunchColorTip); if (HasUserIcon(e)) Native.EnableWindow(cd, false); }
        y += RowH;
        Card(top, y - top);
        y += 16;

        top = y;
        Label(T.EditCombo, labelX, y, labelW, RowH - 1, _font, Theme.ControlText, true);
        Make(HotkeyBox.ClassName, "", Native.WS_TABSTOP, valueX, y + (RowH - FieldH) / 2, valueW, FieldH, IdLeHotkey);
        y += RowH;
        Card(top, y - top);
        y += 12;

        if (!isNew)
        {
            nint l = Button(IdLeLeft, T.LaunchMoveLeft, Btn.Bordered, Mx, y, 88, 32);
            nint r = Button(IdLeRight, T.LaunchMoveRight, Btn.Bordered, Mx + 96, y, 88, 32);
            if (l != 0 && NeighborInRow(_launchEditIndex, -1) < 0) Native.EnableWindow(l, false);
            if (r != 0 && NeighborInRow(_launchEditIndex, +1) < 0) Native.EnableWindow(r, false);
            y += 32 + 12;
        }

        MetalDial(metalTop, ref y);
        _page.BarTop = y;
        y += 14;
        BarPill(IdLeTest, T.CommonTest, Btn.PillMain, 18, y, BarPillW(T.CommonTest));
        if (!isNew) BarPill(IdLeDelete, T.LaunchDelete, Btn.DangerBordered, 18 + BarPillW(T.CommonTest) + 8, y, BarPillW(T.LaunchDelete));
        int saveW = BarPillW(T.CommonSave, 72), cancelW = BarPillW(T.CommonCancel);
        BarPill(IdLeCancel, T.CommonCancel, Btn.PillMain, WinW - 22 - saveW - 8 - cancelW, y, cancelW);
        BarPill(IdLeSave, T.CommonSave, Btn.Prominent, WinW - 22 - saveW, y, saveW, isDefault: true);
        _page.DefaultButton = IdLeSave;
        _page.Height = y + MetalUi.PillMainH + 16;

        Native.SetText(C(IdLeName), e.Name);
        Native.SendMessageW(C(IdLeName), Native.EM_LIMITTEXT, LaunchStore.NameMax, 0);
        Native.SetText(C(IdLeUrl), e.Target);
        Native.SendMessageW(C(IdLeUrl), Native.EM_LIMITTEXT, LaunchStore.UrlMax, 0);
        Native.SetCueBanner(C(IdLeUrl), T.LaunchCueUrl);
        Dropdown.Set(C(IdLeBrowser), BrowserNames, Math.Max(0, Array.IndexOf(BrowserKeys, e.Browser)));
        Dropdown.Set(C(IdLeColor), ColorNames, ColorIndexOf(e.Color));
        HotkeyBox.Set(C(IdLeHotkey), e.Mods, e.Vk);
    }

    /// <summary>화면을 다시 만들기 전에 주소·브라우저 칸을 초안에 옮겨 둔다(친 그대로 — 저장 때 검사).</summary>
    private void KeepWebDraft()
    {
        LaunchItem e = _launchEdit!;
        if (C(IdLeUrl) != 0) e.Target = Native.GetWindowText(C(IdLeUrl)).Trim();
        if (C(IdLeBrowser) != 0) e.Browser = BrowserKeys[Math.Clamp(Dropdown.Get(C(IdLeBrowser)), 0, BrowserKeys.Length - 1)];
        e.Color = WebColorField();
    }

    /// <summary>바탕색을 골랐다: 초안에 두고 미리보기만 바꾼다(저장 때 파일에).</summary>
    private void OnWebColor()
    {
        if (_cur != Screen.LaunchEdit || _launchEdit is not { IsUrl: true } e) return;
        e.Color = WebColorField();
        if (C(IdLePreview) is nint pv and not 0) Tile.SetIcon(pv, WebPreviewIcon(e));
    }

    /// <summary>저장 전 검사: 주소(https:// 를 붙이는 것 말고는 고쳐 쓰지 않음)·브라우저. 안 되면 안내하고 false.</summary>
    private bool ReadWebFields(out string target, out string browser)
    {
        target = LaunchStore.WebInput(Native.GetWindowText(C(IdLeUrl)));
        browser = BrowserKeys[Math.Clamp(Dropdown.Get(C(IdLeBrowser)), 0, BrowserKeys.Length - 1)];
        if (LaunchStore.IsWebUrl(target)) return true;
        Msg(T.LaunchWebBadUrl, AppTitle, Native.MB_OK | Native.MB_ICONWARNING);
        if (C(IdLeUrl) is nint u and not 0) Native.SetFocus(u);
        return false;
    }

    private bool WebEditDirty()
    {
        LaunchItem o = LaunchStore.Items[_launchEditIndex];
        var (m, v) = HotkeyBox.Get(C(IdLeHotkey));
        string url = LaunchStore.WebInput(Native.GetWindowText(C(IdLeUrl)));
        string br = BrowserKeys[Math.Clamp(Dropdown.Get(C(IdLeBrowser)), 0, BrowserKeys.Length - 1)];
        return Native.GetWindowText(C(IdLeName)).Trim() != o.Name || m != o.Mods || v != o.Vk || url != o.Target || br != o.Browser || WebColorField() != o.Color
            || _webUserImage is not null || _webUserClear;
    }

    // ------------------------------------------------------------------ 아이콘

    private bool HasUserIcon(LaunchItem e) => _webUserImage is not null || (!_webUserClear && File.Exists(WebIcon.UserPath(e.Id) ?? ""));

    /// <summary>
    /// [바꾸기]: PNG·ICO 파일을 고르면 작업 스레드에서 읽고 검사한 뒤(UNC 지연이 화면을 멈추지 않게) 초안에 둔다 — 저장 때 다시 그려 icons\&lt;id&gt;.user.png 로.
    /// 원본 파일은 읽기만 한다. 고르기 창이 떠 있는 동안·읽는 동안 잠김·화면 바뀜·다른 항목이면 결과를 버리고 편집 화면을 다시 열지 않는다(Codex R78-1).
    /// </summary>
    private void ChangeWebIcon()
    {
        KeepLaunchDraft();
        JobToken tk = Token();
        string id = _launchEdit!.Id;
        int no = ++_webPickNo;
        string? path = PickImage();
        if (path is null) return;
        if (!WebPickStill(tk, id, no)) { _testPickDropped++; SetFavTestProps(); return; }   // 고르는 동안 잠김·화면 바뀜
        _webPickWait = (tk, id, no);
        nint hwnd = _hwnd;
        var t = new Thread(() =>
        {
            byte[]? data = null;
            try
            {
                if (Program.IsTestMode && Path.GetFileName(path).StartsWith("slow-", StringComparison.OrdinalIgnoreCase)) Thread.Sleep(2500);   // 시험: 느린 읽기(연속 고르기 경쟁)
                data = WebIcon.ReadUserFile(path);
                if (data is not null) { nint bmp = WebIcon.Decode(data); if (bmp == 0) data = null; WebIcon.Free(bmp); }
            }
            catch { data = null; }
            lock (_favLock) _webPickDone[no] = data;
            Native.PostMessageW(hwnd, WM_WEBPICK_READY, no, 0);
        }) { IsBackground = true };
        t.Start();
    }

    private bool WebPickStill(JobToken tk, string id, int no) => StillValid(tk) && _cur == Screen.LaunchEdit && _launchEdit?.Id == id && no == _webPickNo;

    /// <summary>고른 이미지를 다 읽고 검사했다(UI): 아직 같은 상태·같은 항목이면 초안에 두고 화면을 다시 만든다. 아니면 버린다.</summary>
    private void OnWebPickReady(int no)
    {
        byte[]? data;
        lock (_favLock) { if (!_webPickDone.Remove(no, out data)) return; }
        // 지금 기다리는 고르기가 아니면(더 나중에 다시 골랐음) 이 결과만 버린다 — 지금 기다리는 고르기의 상태는 건드리지 않는다(Codex 07:22)
        if (_webPickWait is not { } w || w.No != no) { _testPickDropped++; SetFavTestProps(); return; }
        _webPickWait = null;
        if (!WebPickStill(w.Tk, w.Id, no)) { _testPickDropped++; SetFavTestProps(); return; }
        if (data is null) { Msg(T.LaunchWebImageBad, AppTitle, Native.MB_OK | Native.MB_ICONWARNING); return; }
        _webUserImage = data; _webUserClear = false;
        ShowScreen(Screen.LaunchEdit);
        if (C(IdLeIcon) is nint b and not 0) Native.SetFocus(b);
    }

    /// <summary>목록 새로 시작·백업 복원: 받는 중인 파비콘 요청을 모두 무효로(같은 id·주소가 새 목록에 다시 있어도 옛 결과를 쓰지 않는다, Codex 04:34).</summary>
    private void CancelAllFav()
    {
        lock (_favLock) foreach (var (_, w) in _favWant) _favCancelled.Add(w.No);
        _favWant.Clear();
        _webPickWait = null;
    }

    /// <summary>[기본]: 사용자 이미지를 지운다(저장 때) — 받아 둔 파비콘, 없으면 글자 아이콘.</summary>
    private void ResetWebIcon()
    {
        KeepLaunchDraft();
        _webUserImage = null; _webUserClear = true;
        ShowScreen(Screen.LaunchEdit);
        if (C(IdLeIcon) is nint b and not 0) Native.SetFocus(b);
    }

    /// <summary>편집 화면 미리보기: 초안 이미지가 있으면 그것(메모리, 바로), 아니면 저장된 파일을 백그라운드로 읽고 그동안 글자 아이콘.</summary>
    private nint WebPreviewIcon(LaunchItem e)
    {
        if (_webUserImage is not null)
        {
            string mk = "web-mem\n" + e.Id + "\n" + _webUserImage.Length + "\n" + _webUserImage.GetHashCode();
            if (mk != _previewKey)
            {
                if (_previewIcon != 0) { DestroyIcon(_previewIcon); _previewIcon = 0; }
                _previewKey = mk;
                nint bmp = WebIcon.Decode(_webUserImage);
                try { _previewIcon = WebIcon.ToIcon(bmp); } finally { WebIcon.Free(bmp); }
            }
            return _previewIcon != 0 ? _previewIcon : LetterIcon(e, draft: true);
        }
        string pk = "web\n" + e.Id + "\n" + _webIconGen.GetValueOrDefault(e.Id) + "\n" + (_webUserClear ? 1 : 0) + "\n" + e.Color;
        if (pk != _previewKey)
        {
            if (_previewIcon != 0) { DestroyIcon(_previewIcon); _previewIcon = 0; }
            _previewKey = pk;
            int mode = WebLoadMode(e.Color, _webUserClear);
            if (mode >= 0) IconLoader.Request(_hwnd, WM_ICON_READY, PreviewIconId, pk, e.Id, false, "", mode, web: true);
        }
        return _previewIcon != 0 ? _previewIcon : LetterIcon(e, draft: true);
    }

    /// <summary>띠의 웹사이트 아이콘: 저장된 파일을 백그라운드로 읽는다(세대가 바뀌면 다시). 그동안·없으면 글자 아이콘.</summary>
    private nint WebIconOf(LaunchItem it)
    {
        string key = "web\n" + it.Id + "\n" + _webIconGen.GetValueOrDefault(it.Id) + "\n" + it.Color;
        if (_launchIconKey.TryGetValue(it.Id, out string? k) && k == key)
            return _launchIcons.TryGetValue(it.Id, out nint ic) && ic != 0 ? ic : LetterIcon(it);
        DropIcon(it.Id);
        _launchIconKey[it.Id] = key;
        IconLoader.Request(_hwnd, WM_ICON_READY, it.Id, key, it.Id, false, "", WebLoadMode(it.Color, false), web: true);
        return LetterIcon(it);
    }

    /// <summary>
    /// 웹사이트 아이콘을 무엇에서 읽을지(LaunchIcons 의 번호): 0 = 사용자 이미지, 없으면 받아 둔 사이트 아이콘 / 1 = 사이트 아이콘만(사용자 이미지 지움) /
    /// 2 = 사용자 이미지만 / -1 = 읽지 않음(글자 아이콘). 바탕색을 골랐으면 글자 아이콘을 원하는 것이므로 사이트 아이콘은 쓰지 않는다(0.3.93).
    /// </summary>
    private static int WebLoadMode(string color, bool userCleared) => color.Length == 0 ? (userCleared ? 1 : 0) : (userCleared ? -1 : 2);

    /// <summary>
    /// 이름 첫 글자 아이콘(고른 바탕색, 없으면 테마 강조색). 이름·색이 같으면 만들어 둔 것을 그대로. draft = 편집 화면 미리보기(저장 전 색) —
    /// 띠의 아이콘과 따로 둔다(한쪽을 다시 만들 때 다른 쪽이 쓰는 아이콘을 지우지 않게).
    /// </summary>
    private nint LetterIcon(LaunchItem it, bool draft = false)
    {
        string name = it.Name.Length > 0 ? it.Name : (LaunchStore.WebUri(LaunchStore.WebInput(it.Target))?.Host ?? "?");
        uint? pick = WebIcon.LetterColor(it.Color);
        uint bg = pick ?? Theme.Accent, fg = pick is null ? Theme.AccentText : 0xFFFFFF;
        string key = name + "\n" + bg + "\n" + fg, slot = draft ? it.Id + "\nedit" : it.Id;
        if (_letterIcons.TryGetValue(slot, out var c) && c.Key == key && c.Icon != 0) return c.Icon;
        if (c.Icon != 0) DestroyIcon(c.Icon);
        nint ic = WebIcon.Letter(name, bg, fg);
        _letterIcons[slot] = (key, ic);
        return ic != 0 ? ic : Placeholder(false);
    }

    private void BumpWebIcon(string id)
    {
        _webIconGen[id] = _webIconGen.GetValueOrDefault(id) + 1;
        DropIcon(id);
    }

    // ------------------------------------------------------------------ 저장 뒤·지울 때

    /// <summary>
    /// 웹사이트 항목을 저장한 뒤: 초안의 사용자 이미지를 다시 그려 쓰거나 지우고, 새 항목·origin 이 바뀜·파비콘 없음이면 파비콘을 받는다(백그라운드).
    /// 아이콘을 쓰지 못해도 항목 저장은 그대로다(안내만). 받기 실패는 조용히 — 이전에 받아 둔 아이콘은 지우지 않는다(Codex 02:46).
    /// </summary>
    private void AfterWebSave(LaunchItem? before, LaunchItem saved)
    {
        bool changed = false;
        if (_webUserImage is not null)
        {
            nint bmp = WebIcon.Decode(_webUserImage);
            byte[]? png;
            try { png = WebIcon.ToPng(bmp); } finally { WebIcon.Free(bmp); }
            if (png is null || !WebIcon.Write(WebIcon.UserPath(saved.Id), png)) Toast.Show(_hwnd, T.LaunchWebIconSaveFail, 5000, warn: true);
            changed = true;
        }
        else if (_webUserClear) { WebIcon.Delete(saved.Id, fav: false, user: true); changed = true; }
        Uri? now = LaunchStore.WebUri(saved.Target), old = before is null ? null : LaunchStore.WebUri(before.Target);
        bool originChanged = old is null || now is null || LaunchStore.WebOrigin(old) != LaunchStore.WebOrigin(now);
        if (originChanged || !File.Exists(WebIcon.FavPath(saved.Id) ?? "")) StartFavFetch(saved);
        if (changed) BumpWebIcon(saved.Id);
    }

    /// <summary>항목을 지웠다: 받는 중인 파비콘은 버리고, 이 항목의 앱 소유 아이콘 파일만 지운다.</summary>
    private void ForgetWeb(string id)
    {
        if (_favWant.Remove(id, out var w)) lock (_favLock) _favCancelled.Add(w.No);
        WebIcon.Delete(id);
        _webIconGen.Remove(id);
        if (_letterIcons.Remove(id, out var c) && c.Icon != 0) DestroyIcon(c.Icon);
        if (_letterIcons.Remove(id + "\nedit", out var d) && d.Icon != 0) DestroyIcon(d.Icon);
    }

    // ------------------------------------------------------------------ 파비콘 받기(백그라운드)

    private void StartFavFetch(LaunchItem it)
    {
        Uri? u = LaunchStore.WebUri(it.Target);
        if (u is null) return;
        if (_favWant.TryGetValue(it.Id, out var old)) lock (_favLock) _favCancelled.Add(old.No);   // 앞선 요청(옛 주소)의 결과는 버린다
        int no = ++_favNo;
        _favWant[it.Id] = (no, it.Target);
        string id = it.Id;
        nint hwnd = _hwnd;
        var t = new Thread(() =>
        {
            byte[]? png = null;
            try
            {
                bool Cancelled() { lock (_favLock) return _favCancelled.Contains(no); }
                byte[]? raw = WebIcon.Fetch(u, Cancelled);
                if (raw is not null && !Cancelled())
                {
                    nint bmp = WebIcon.Decode(raw);
                    try { png = WebIcon.ToPng(bmp); } finally { WebIcon.Free(bmp); }
                }
            }
            catch { png = null; }
            lock (_favLock) _favDone.Add((no, id, png));
            Native.PostMessageW(hwnd, WM_FAV_READY, 0, 0);
        }) { IsBackground = true };
        t.Start();
    }

    /// <summary>받기가 끝났다(UI): 아직 그 항목·그 주소를 원하면 파일로 쓰고 아이콘을 바꾼다. 지움·주소 변경·새 요청 뒤의 늦은 결과는 버린다.</summary>
    private void OnFavReady()
    {
        List<(int No, string Id, byte[]? Png)> done;
        lock (_favLock) { done = new(_favDone); _favDone.Clear(); foreach (var d in done) _favCancelled.Remove(d.No); }
        foreach (var (no, id, png) in done)
        {
            bool wanted = _favWant.TryGetValue(id, out var w) && w.No == no
                          && LaunchStore.Items.Find(x => x.Id == id) is LaunchItem it && it.IsUrl && it.Target == w.Target;
            if (!wanted) { _testFavDropped++; SetFavTestProps(); continue; }
            _favWant.Remove(id);
            if (png is not null && WebIcon.Write(WebIcon.FavPath(id), png))
            {
                _testFavOk++;
                BumpWebIcon(id);
                if (_cur == Screen.List) RefreshList();
            }
            else _testFavFail++;   // 실패: 이전 아이콘(있으면) 그대로, 없으면 글자 아이콘
            SetFavTestProps();
        }
    }

    private void SetFavTestProps()
    {
        if (!Program.IsTestMode) return;
        Native.SetPropW(_hwnd, "OneKeyTestFavOk", _testFavOk);
        Native.SetPropW(_hwnd, "OneKeyTestFavFail", _testFavFail);
        Native.SetPropW(_hwnd, "OneKeyTestFavDropped", _testFavDropped);
        Native.SetPropW(_hwnd, "OneKeyTestPickDropped", _testPickDropped);
    }

    // ------------------------------------------------------------------ [테스트]

    /// <summary>[테스트]: 지금 칸의 주소·브라우저로 열어 본다(저장하지 않음). 실행 경로는 단축키·아이콘과 같다(실행 직전 재검사).</summary>
    private void TestWebEdit()
    {
        if (_cur != Screen.LaunchEdit || _launchEdit is not { IsUrl: true } e) return;
        JobToken tk = Token();
        if (!ReadWebFields(out string target, out string browser)) return;
        if (!StillValid(tk) || _launchEdit?.Id != e.Id) return;   // 안내 상자 등으로 그사이 상태가 바뀌었으면 열지 않는다(Codex R78-1)
        string name = Native.GetWindowText(C(IdLeName)).Trim();
        var probe = new LaunchItem { Id = e.Id, Kind = "url", Name = name.Length > 0 ? name : target, Target = target, Browser = browser, Valid = true };
        if (Launcher.Unconsumed is int done and not 0) OnLaunchDone(done);
        if (Launcher.Start(probe, 0, false, "", _hwnd, WM_LAUNCH_DONE) is null) LaunchToast(Launcher.Result.Busy, probe.Name);
    }

    // ------------------------------------------------------------------ 이미지 파일 고르기

    /// <summary>아이콘 이미지(.png/.ico) 하나를 고른다. 취소·실패면 null.</summary>
    private string? PickImage()
    {
        if (!_comInit) { int hr0 = CoInitializeEx(0, 2); _comInit = hr0 >= 0; }
        nint dlg = 0, item = 0;
        Guid clsid = CLSID_FileOpenDialog, iid = IID_IFileOpenDialog;
        try
        {
            if (CoCreateInstance(ref clsid, 0, 1, ref iid, out dlg) < 0 || dlg == 0) return null;
            nint* vt = *(nint**)dlg;
            uint opts = 0;
            ((delegate* unmanaged[Stdcall]<nint, uint*, int>)vt[10])(dlg, &opts);
            opts |= 0x40 /* FORCEFILESYSTEM */ | 0x1000 /* FILEMUSTEXIST */ | 0x800 /* PATHMUSTEXIST */;
            ((delegate* unmanaged[Stdcall]<nint, uint, int>)vt[9])(dlg, opts);
            fixed (char* title = T.LaunchWebPickImage) ((delegate* unmanaged[Stdcall]<nint, char*, int>)vt[17])(dlg, title);
            fixed (char* name = T.LaunchWebFilterImages) fixed (char* spec = "*.png;*.ico")
            {
                var f = new COMDLG_FILTERSPEC { pszName = name, pszSpec = spec };
                ((delegate* unmanaged[Stdcall]<nint, uint, COMDLG_FILTERSPEC*, int>)vt[4])(dlg, 1, &f);
            }
            if (Program.IsTestMode && Environment.GetEnvironmentVariable("ONEKEY_TEST_PICK") is string pickFile && File.Exists(pickFile))
            {
                string p0 = File.ReadAllText(pickFile).Trim();   // 시험: 고르기 창 대신 파일에 적힌 경로(백업 시험과 같은 훅)
                if (p0.StartsWith("delay=", StringComparison.Ordinal) && p0.IndexOf('|') is int bar and > 6 && int.TryParse(p0.AsSpan(6, bar - 6), out int ms))
                {
                    // "delay=<ms>|<경로>": 고르기 창이 떠 있는 동안처럼 메시지를 처리하며 기다린 뒤 돌려준다(R78-1 시험)
                    long end = Environment.TickCount64 + ms;
                    while (Environment.TickCount64 < end)
                    {
                        while (Native.PeekMessageW(out Native.MSG msg, 0, 0, 0, 1 /* PM_REMOVE */)) { Native.TranslateMessage(ref msg); Native.DispatchMessageW(ref msg); }
                        Thread.Sleep(10);
                    }
                    p0 = p0[(bar + 1)..];
                }
                return p0.Length > 0 ? p0 : null;
            }
            int hr = ((delegate* unmanaged[Stdcall]<nint, nint, int>)vt[3])(dlg, _hwnd);
            if (hr < 0) return null;
            if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)vt[20])(dlg, &item) < 0 || item == 0) return null;
            char* path = null;
            if (((delegate* unmanaged[Stdcall]<nint, uint, char**, int>)(*(nint**)item)[5])(item, 0x80058000, &path) < 0 || path == null) return null;
            try { return new string(path); }
            finally { CoTaskMemFree((nint)path); }
        }
        catch { return null; }
        finally
        {
            if (item != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)item)[2])(item);
            if (dlg != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)dlg)[2])(dlg);
        }
    }
}
