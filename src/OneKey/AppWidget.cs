namespace OneKey;

/// <summary>
/// 위젯 화면(0.5.26, 2026-10-13 사용자: 고양이 메뉴가 오른쪽 클릭 말고는 길이 없다 — 목록 아래 [설정] 왼쪽에 [위젯], 설정 화면과 같은 모양으로 고양이에 관한 것을 모두.
/// 시안 work/design/widget-mockup). 카드 다섯: 지금 상태(그림 · 이름 · 단계 · 하트 · 점수 · 단계 줄) / 같이 놀기(쓰담 · 츄르 · 놀이 — 누르면 작업 표시줄 고양이가 바로)
/// / 오늘의 미션 / 앨범 / 이름(흰 · 검은 고양이 — 설정에서 옮겨 왔다) + 고양이 돌보는 방법. [저장]은 이름만 저장한다.
/// 지금 테마의 고양이를 보인다. 열려 있는 동안 2초마다 상태 · 기다림 · 미션 · 앨범을 고친다(화면을 다시 만들지 않고 — 입력 중인 이름은 그대로).
/// </summary>
internal sealed unsafe partial class App
{
    private const int IdRowWidget = 221, IdWBack = 242, IdWCancel = 243, IdWSave = 2013;
    private const int IdWPet = 244, IdWTreat = 245, IdWPlay = 246, IdWGuide = 247;
    private const int IdWName = 251, IdWHearts = 252, IdWScore = 253, IdWNext = 254, IdWPlayNote = 255, IdWAlbumTitle = 256;
    private const nuint TimerWidget = 33;
    private const uint WM_WIDGET_OPEN = Native.WM_APP + 80;   // 고양이 메뉴 [수첩]: 고양이 상자를 닫은 뒤(그 상자의 루프가 끝난 뒤) 위젯 화면을 연다
    private nint _wStages, _wMissions, _wAlbum, _wCat;
    private (string Light, string Dark)? _widgetNames;   // 같은 화면을 다시 만들 때(테마 · 언어가 바뀌는 등) 입력 중이던 이름

    private static readonly (int Id, string Clip, int Points)[] WidgetPlays = { (IdWPet, "I1", 6), (IdWTreat, "I2", 10), (IdWPlay, "I3", 8) };
    private static string WidgetPlayName(string clip) => clip switch { "I1" => T.CatMenuPet, "I2" => T.CatMenuTreat, _ => T.CatMenuPlay };

    private void BuildWidgetScreen()
    {
        int y = MetalHeader(IdWBack, T.ListWidget);
        int dialTop = y;
        y = BuildWidgetBody(y + DialPadX) + DialPadX;
        _page.Dials.Add((DialX, dialTop, DialW, y - dialTop));
        _page.BarTop = y;
        y += 14;
        int saveW = BarPillW(T.CommonSave, 72), cancelW = BarPillW(T.CommonCancel);
        int sx = WinW - 22 - saveW;
        BarPill(IdWCancel, T.CommonCancel, Btn.PillMain, sx - 8 - cancelW, y, cancelW);
        BarPill(IdWSave, T.CommonSave, Btn.Prominent, sx, y, saveW, isDefault: true);
        _page.DefaultButton = IdWSave;
        _page.Height = y + MetalUi.PillMainH + 16;
        var names = _widgetNames ?? (_cfg.CatNameLight, _cfg.CatNameDark);
        _widgetNames = null;
        if (C(IdCatNameLight) != 0) Native.SetText(C(IdCatNameLight), names.Light);
        if (C(IdCatNameDark) != 0) Native.SetText(C(IdCatNameDark), names.Dark);
        UpdateWidget();
        Native.SetTimer(_hwnd, TimerWidget, 2000, 0);
    }

    private int BuildWidgetBody(int y)
    {
        const int RH = MetalUi.SetRowH, Gap = 10, Pad = 14;
        int cx = DialX + DialPadX, cw = DialW - 2 * DialPadX, lx = cx + Row.PadX, rx = cx + cw - Row.PadX;
        nint nameFont = Theme.Sized(13.5, true), headFont = Theme.Sized(14, true), smallFont = Theme.Sized(12, false);
        uint ink = Metal.Ref(Metal.Ink(Theme.IsDark)), note = Metal.Ref(Metal.InkNote(Theme.IsDark));
        bool light = CatWidget.IsLight;
        int top;
        void Sep(int at) => _page.Separators.Add((lx, at, rx - lx));
        nint Art(string spec, int x, int at, int w, int h)
        {
            HelpArt.EnsureClass();
            nint a = Make(HelpArt.ClassName, "", 0, x, at, w, h, 0);
            HelpArt.SetSpec(a, spec, pageBg: true);
            return a;
        }
        int Wrapped(int id, string text, int at, string alt = "")   // alt: 바뀌어 들어올 수 있는 글(높이는 둘 가운데 큰 쪽)
        {
            int h = Math.Max(16, Math.Max(TextH(smallFont, text, rx - lx), TextH(smallFont, alt, rx - lx))) + 2;
            Label(text, lx, at, rx - lx, h, smallFont, note, true, Native.SS_LEFT, vcenter: false, id: id);
            return at + h;
        }
        nint Line(int id, int x, int at, int w, int h, nint font, uint color)
        {
            return Label("", x, at, w, h, font, color, true, Native.SS_LEFT | Native.SS_ENDELLIPSIS, userText: true, id: id);
        }

        // 1. 지금 상태: 왼쪽 고양이 그림, 오른쪽 이름 · 단계 / 하트 / 점수 · 크기 / 다음 단계까지, 아래 줄어듦 안내와 단계 줄
        top = y;
        y += Pad;
        const int Pic = 84;
        int picW = Pic * 238 / 256, tx = lx + picW + 14, tw = rx - tx;
        _wCat = Art("cat:" + (light ? "light" : "dark"), lx, y, picW, Pic);
        Line(IdWName, tx, y + 2, tw, 24, Theme.Sized(15.5, true), ink);
        Line(IdWHearts, tx, y + 28, tw, 22, Theme.Sized(15, false), Theme.IsDark ? 0x907AFFu : 0x7A60F0u);
        Line(IdWScore, tx, y + 52, tw, 18, smallFont, note);
        Line(IdWNext, tx, y + 70, tw, 18, smallFont, note);
        y += Pic + 6;
        y = Wrapped(0, T.CatNoteDecay, y) + 8;
        _wStages = Art("stagebar:0", lx, y, rx - lx, HelpArt.HeightFor("stagebar:0"));
        y += HelpArt.HeightFor("stagebar:0") + Pad;
        _page.Cards.Add((cx, top, cw, y - top));

        // 2. 같이 놀기: 알약 셋(누르면 작업 표시줄 고양이가 바로 — 점수는 같은 놀이 3분에 한 번)
        y += Gap;
        top = y;
        Label(T.WidgetPlay, lx, y + Pad - 2, rx - lx, 22, headFont, ink, true, Native.SS_LEFT | Native.SS_ENDELLIPSIS);
        y += Pad + 22;
        nint pf = Theme.Sized(12, true);
        int px = lx;
        foreach (var (id, clip, pts) in WidgetPlays)
        {
            string a = WidgetPlayName(clip) + " +" + pts, b = WidgetPlayName(clip) + " · " + T.WidgetWait(3);
            int w = Math.Max(LabelW(pf, a), LabelW(pf, b)) + 28;
            SmallPill(id, a, px, y, w);
            px += w + 8;
        }
        y += RH;
        y = Wrapped(IdWPlayNote, T.WidgetPlayNote, y, T.WidgetPlayHidden) + Pad - 4;
        _page.Cards.Add((cx, top, cw, y - top));

        // 3. 오늘의 미션
        y += Gap;
        top = y;
        Label(T.WidgetMissions, lx, y + Pad - 2, rx - lx, 22, headFont, ink, true, Native.SS_LEFT | Native.SS_ENDELLIPSIS);
        y += Pad + 22 + 4;
        string ms = WidgetMissionSpec();
        _wMissions = Art(ms, lx, y, rx - lx, HelpArt.HeightFor(ms));
        y += HelpArt.HeightFor(ms) + Pad;
        _page.Cards.Add((cx, top, cw, y - top));

        // 4. 앨범(5열 작은 칸)
        y += Gap;
        top = y;
        Line(IdWAlbumTitle, lx, y + Pad - 2, rx - lx, 22, headFont, ink);
        y += Pad + 22 + 4;
        string al = WidgetAlbumSpec();
        _wAlbum = Art(al, lx, y, rx - lx, HelpArt.HeightFor(al));
        y += HelpArt.HeightFor(al) + Pad;
        _page.Cards.Add((cx, top, cw, y - top));

        // 5. 이름(설정에서 옮겨 왔다 — 흰 · 검은 고양이 따로) + 고양이 돌보는 방법
        y += Gap;
        top = y;
        int nameW = Math.Min(150, (rx - lx) / 2);
        Label(T.SetCatNameLight, lx, y, rx - lx - nameW - 8, RH, nameFont, ink, true, Native.SS_LEFT | Native.SS_ENDELLIPSIS);
        Tip(CatNameField(IdCatNameLight, T.SetCatNameLightCue, rx - nameW, y, nameW), T.SetCatNameTip);
        Sep(y + RH); y += RH + 1;
        Label(T.SetCatNameDark, lx, y, rx - lx - nameW - 8, RH, nameFont, ink, true, Native.SS_LEFT | Native.SS_ENDELLIPSIS);
        Tip(CatNameField(IdCatNameDark, T.SetCatNameDarkCue, rx - nameW, y, nameW), T.SetCatNameTip);
        Sep(y + RH); y += RH + 1;
        y = LabeledButtons(T.CatGuideTitle, y, true, (IdWGuide, T.SetBtnOpen));
        _page.Cards.Add((cx, top, cw, y - top));
        return y;
    }

    private static string WidgetMissionSpec()
    {
        bool light = CatWidget.IsLight, grows = CatWidget.HasInteractArt(light);
        var ms = new List<string> { "stamp=" + (CatGrowth.StampDue ? 0 : 1) };
        if (grows)
        {
            ms.Add("pet=" + (CatGrowth.DoneToday(light, "I1") ? 1 : 0));
            ms.Add("treat=" + (CatGrowth.DoneToday(light, "I2") ? 1 : 0));
            ms.Add("play=" + (CatGrowth.DoneToday(light, "I3") ? 1 : 0));
            ms.Add("toy=" + (CatGrowth.ToyToday ? 1 : 0));
        }
        ms.Add("break=" + (CatGrowth.BreakToday ? 1 : 0));
        ms.Add($"pw={CatGrowth.PasswordToday}/3");
        ms.Add("backup=" + (CatGrowth.WaitDays(CatGrowth.Habit.Backup) > 0 ? 1 : 0));
        ms.Add("master=" + (CatGrowth.WaitDays(CatGrowth.Habit.Master) > 0 ? 1 : 0));
        return "missions:" + string.Join("|", ms);
    }

    private static string WidgetAlbumSpec()
        => "albumw:" + string.Join("|", CatGrowth.AlbumKeys.Select(k => k + "=" + (CatGrowth.IsSeen(k) ? "1" : "0")));

    /// <summary>위젯 화면의 바뀌는 값(상태 · 놀기 단추 · 미션 · 앨범)을 지금 값으로. 화면을 떠나면 타이머를 끈다.</summary>
    private void UpdateWidget()
    {
        if (_cur != Screen.Widget) { Native.KillTimer(_hwnd, TimerWidget); return; }
        bool light = CatWidget.IsLight, grows = CatWidget.HasInteractArt(light);
        string name = light ? _cfg.CatNameLight : _cfg.CatNameDark;
        int score = CatGrowth.Score(light), step = CatGrowth.Step(light);
        double level = CatGrowth.Level(light);
        void Put(int id, string text)
        {
            nint h = C(id);
            if (h == 0 || Native.GetWindowText(h) == text) return;
            Native.SetText(h, text);
            Native.InvalidateRect(h, 0, true);
        }
        Put(IdWName, (name.Length > 0 ? name + " · " : "") + CatGrowth.StageName(step));
        if (grows)
        {
            string hearts = "";
            for (int i = 0; i < CatGrowth.Steps; i++) hearts += level - i >= 1 ? "♥" : "♡";
            Put(IdWHearts, hearts);
            Put(IdWScore, T.WidgetScore(score, (int)Math.Round(CatGrowth.Scale(light, true) * 100)));
            Put(IdWNext, step < CatGrowth.Steps ? T.CatNoteNext(CatGrowth.StageName(step + 1), (step + 1) * CatGrowth.StepScore - score) : T.CatNoteMax);
        }
        else { Put(IdWHearts, ""); Put(IdWScore, T.WidgetScore(score, 100)); Put(IdWNext, ""); }
        HelpArt.SetSpec(_wCat, "cat:" + (light ? "light" : "dark"), true);
        HelpArt.SetSpec(_wStages, "stagebar:" + step, true);
        HelpArt.SetSpec(_wMissions, WidgetMissionSpec(), true);
        HelpArt.SetSpec(_wAlbum, WidgetAlbumSpec(), true);
        Put(IdWAlbumTitle, T.CatAlbumTitle(CatGrowth.SeenCount, CatGrowth.AlbumKeys.Length));
        bool any = false;
        foreach (var (id, clip, pts) in WidgetPlays)
        {
            nint b = C(id);
            if (b == 0) continue;
            bool can = CatWidget.CanInteract(clip);
            any |= can;
            int wait = can ? CatGrowth.WaitMinutes(light, clip) : 0;
            Put(id, WidgetPlayName(clip) + (wait > 0 ? " · " + T.WidgetWait(wait) : " +" + pts));
            if (Native.IsWindowEnabled(b) != can) Native.EnableWindow(b, can);
        }
        Put(IdWPlayNote, any ? T.WidgetPlayNote : T.WidgetPlayHidden);
    }

    /// <summary>위젯 화면에서 바꾼 이름이 저장된 값과 다른가.</summary>
    private bool WidgetDirty()
        => C(IdCatNameLight) != 0 && (Config.CleanCatName(Native.GetWindowText(C(IdCatNameLight))) != _cfg.CatNameLight
                                      || Config.CleanCatName(Native.GetWindowText(C(IdCatNameDark))) != _cfg.CatNameDark);

    /// <summary>←: 이름을 바꿨으면 버릴지 묻는다(설정과 같은 규칙). [취소]는 묻지 않고 버린다.</summary>
    private void LeaveWidget(bool ask)
    {
        if (ask && _cur == Screen.Widget && WidgetDirty())
        {
            int r = Msg(T.SetLeaveConfirm, AppTitle, Native.MB_YESNO | Native.MB_ICONQUESTION);
            if (r != Native.IDYES || _cur != Screen.Widget) return;
        }
        ShowScreen(Screen.List);
    }

    /// <summary>[저장]: 이름만 저장한다. 설정 화면에 저장하지 않은 초안이 있으면 그 이름도 새 값으로(나중에 설정을 저장해도 이름이 되돌아가지 않게).</summary>
    private void SaveWidget()
    {
        if (C(IdCatNameLight) == 0) return;
        string nl = Config.CleanCatName(Native.GetWindowText(C(IdCatNameLight))), nd = Config.CleanCatName(Native.GetWindowText(C(IdCatNameDark)));
        if (nl != _cfg.CatNameLight || nd != _cfg.CatNameDark)
        {
            var keep = (_cfg.CatNameLight, _cfg.CatNameDark);
            (_cfg.CatNameLight, _cfg.CatNameDark) = (nl, nd);
            if (!_cfg.Save())
            {
                (_cfg.CatNameLight, _cfg.CatNameDark) = keep;
                Msg(SaveFailMsg(), AppTitle, Native.MB_OK | Native.MB_ICONERROR);
                return;
            }
            if (_settingsDraft is { } d) { d.NameLight = nl; d.NameDark = nd; _settingsDraft = d; }
            SyncWalker();
        }
        ShowScreen(Screen.List);
        Toast.Show(_hwnd, T.SetSaved);
    }

    /// <summary>고양이 메뉴의 [수첩](0.5.26): 잠금이 풀려 있으면 위젯 화면을 연다 — 떠 있는 고양이 상자(안내)는 닫고, 그 상자가 끝난 뒤에(WM_WIDGET_OPEN).
    /// 잠겨 있으면 예전 수첩 상자(비밀이 없다).</summary>
    private void OnCatNote()
    {
        Dialog.CloseTagged("cat");
        if (!Unlocked || OnLockScreen)
            Dialog.Show(_hwnd, CatNoteText(), T.CatNoteTitle, Native.MB_OK, width: 440, okText: T.CommonClose, fold: false, tag: "cat");
        else Native.PostMessageW(_hwnd, WM_WIDGET_OPEN, 0, 0);
    }

    private void OpenWidgetFromCat()
    {
        if (!Unlocked || OnLockScreen || !_cfg.Walker) return;
        if (ModalUp) { BringModalForward(); return; }   // 그사이 다른 상자가 떴다
        // 숨어 있던 본창이면 화면을 숨긴 채 먼저 만들고 보인다(설정과 같다 — 목록이 먼저 보였다가 바뀌지 않게)
        if (!Native.IsWindowVisible(_hwnd) && !Native.IsIconic(_hwnd)) { ShowScreen(Screen.Widget); ShowMainWindow(); return; }
        ShowMainWindow();
        if (Unlocked && !OnLockScreen) ShowScreen(Screen.Widget);
    }
}
