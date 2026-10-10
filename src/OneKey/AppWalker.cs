namespace OneKey;

/// <summary>
/// 작업 표시줄 위 고양이(<see cref="CatWidget"/>)를 앱 상태와 잇는다(2026-10-06 사용자 결정, Codex 09:43): 설정에서 켰고, 마스터가 있고 잠금이 풀렸고,
/// 복구 전용·종료 중이 아니고, 본창이 숨었을(닫혀 트레이로 간) 때만 보고 싶다. 본창이 보이거나 숨을 때(WM_SHOWWINDOW 뒤), 잠글 때, 설정을 저장할 때,
/// 자동 잠금 점검(15초)마다 다시 맞춘다. 마스코트를 누르면 트레이 아이콘과 같은 ShowMainWindow.
/// </summary>
internal sealed unsafe partial class App
{
    private const uint WM_WALKER_CLICK = Native.WM_APP + 47;   // 마스코트를 눌렀다
    private const uint WM_WALKER_SYNC = Native.WM_APP + 48;    // 본창 보임/숨김이 바뀐 뒤 다시 맞추기

    private bool WalkerWanted => !_walkerHidden && ((_cfg.Walker && _cfg.HasMaster && Unlocked && !_createMode && !_recoveryOnly && !_exiting && !Native.IsWindowVisible(_hwnd))
        || LockedWalker);

    /// <summary>오른쪽 클릭 메뉴의 [고양이 숨기기]: 다음에 1Key 창(또는 잠금 위젯)을 열 때까지 숨긴다(ShowMainWindow 가 끈다). 설정은 바꾸지 않는다.</summary>
    private bool _walkerHidden;

    /// <summary>
    /// 잠긴 채 위젯 모드(0.3.124, 2026-10-07 사용자): 잠금 위젯의 위젯 단추를 눌렀다 — 설정의 위젯 모드와 상관없이 마스코트가 작업 표시줄에 나오고,
    /// 누르면 잠금 위젯이 다시 열린다(잠금은 그대로, 마스코트에는 비밀이 없다). 잠금 위젯이 다시 열리거나 잠금이 풀리면 끝난다.
    /// </summary>
    private bool _lockedWalker;
    private bool LockedWalker => _lockedWalker && OnLockScreen && !_createMode && _cfg.HasMaster && !_recoveryOnly && !_exiting
        && !LockWidget.IsShown && !Native.IsWindowVisible(_hwnd);

    /// <summary>출근 도장(0.5.19): 잠금이 풀려 있고 오늘 아직 안 찍었으면 찍는다(고양이가 보일 때 알린다). 잠금 해제 · 15초 점검마다.</summary>
    private void MaybeStamp()
    {
        if (!Unlocked || OnLockScreen || !_cfg.HasMaster || !CatGrowth.StampDue) return;
        if (Program.IsTestMode && Environment.GetEnvironmentVariable("ONEKEY_TEST_CAT_STAMP") != "1") return;   // 시험: 켤 때만(다른 시험의 동작 차례를 흔들지 않게)
        CatGrowth.AwardHabit(FlipClock.MascotLight(), CatGrowth.Habit.Stamp);
    }

    private void SyncWalker()
    {
        CatWidget.SetNames(_cfg.CatNameLight, _cfg.CatNameDark);   // 하트 옆 이름(설정)
        CatWidget.SetWanted(WalkerWanted);
    }


    /// <summary>목록 윗줄의 작은 마스코트 단추(위젯 모드를 켰을 때 (−) 자리): 창을 트레이로 내린다 — 그러면 마스코트가 나온다.</summary>
    private bool WidgetButton => _cfg.Walker && _cfg.HasMaster;

    private const int IdCatOpen = 3201, IdCatLock = 3202, IdCatSettings = 3203, IdCatHide = 3204, IdCatPet = 3211, IdCatTreat = 3212, IdCatPlay = 3213, IdCatNote = 3214, IdCatGuide = 3215;

    /// <summary>
    /// 고양이 오른쪽 클릭 메뉴(0.5.15-V, 2026-10-09 사용자: 고양이에 오른쪽 클릭으로 무언가 할 수 있게): [쓰다듬기] [츄르 주기] [놀아 주기](밝은 고양이) /
    /// [1Key 열기] [지금 잠금](잠금이 풀려 있을 때) [설정…] / [고양이 숨기기]. 잠긴 채 위젯 모드면 [잠금 해제] / [고양이 숨기기].
    /// 0.5.16-C(사용자: Windows 메뉴가 고양이를 가린다 — 위쪽으로, 메인 화면처럼 입체적으로): 고양이 머리 위의 금속 판(CatMenu). 판을 못 띄우면 예전 Windows 메뉴.
    /// </summary>
    private void OnWalkerMenu(int gen)
    {
        if (!WalkerWanted || !CatWidget.AcceptClick(gen)) return;
        CatGrowth.MarkClicked();   // 처음 말풍선은 이제 그만
        if (CatWidget.ScreenRect(out Native.RECT cat))
        {
            bool lockedNow = LockedWalker;
            var items = new List<CatMenu.Item>();
            foreach (var (id, clip, label) in new[] { (IdCatPet, "I1", T.CatMenuPet), (IdCatTreat, "I2", T.CatMenuTreat), (IdCatPlay, "I3", T.CatMenuPlay) })
                if (CatWidget.CanInteract(clip))
                {
                    int wait = CatGrowth.WaitMinutes(CatWidget.IsLight, clip);   // 키우기 점수를 다시 받을 때까지(동작은 지금도 한다)
                    items.Add(new(id, label, wait > 0 ? T.CatMenuWait(wait) : ""));
                }
            if (items.Count > 0) items.Add(new(0, ""));
            // 수첩 · 안내(0.5.20, 2026-10-10 사용자: 지금 상태와 돌보는 방법을 메뉴에서) — 누르면 확인 상자처럼 뜨는 창
            items.Add(new(IdCatNote, T.CatMenuNote));
            items.Add(new(IdCatGuide, T.CatMenuGuide));
            items.Add(new(0, ""));
            // 아래 버튼(0.5.18-B, 사용자: 짧게 — 열기 · 잠금 · 설정 · 숨기기, 2×2). 잠겨 있으면 열기(잠금 위젯) · 숨기기
            // 0.5.21-G(사용자: 아이콘으로 바꾸고 올리면 설명 — 한 줄로): Segoe Fluent Icons — 열기 OpenInNewWindow · 잠금 Lock · 설정 Settings · 숨기기 Hide
            items.Add(new(IdCatOpen, T.CatMenuOpen, Icon: ""));
            if (!lockedNow)
            {
                if (_cfg.HasMaster) items.Add(new(IdCatLock, T.CatMenuLock, Icon: ""));
                items.Add(new(IdCatSettings, T.CatMenuSettings, Icon: ""));
            }
            items.Add(new(IdCatHide, T.CatMenuHide, Icon: ""));
            string title = CatWidget.IsLight ? _cfg.CatNameLight : _cfg.CatNameDark;   // 맨 위에 고양이 이름(설정, 비면 없음)
            if (CatMenu.Show(items.ToArray(), cat, CatWidget.Dpi, !CatWidget.IsLight, cmd => OnWalkerCommand(cmd, gen), title)) return;
        }
        nint menu = Native.CreatePopupMenu();
        if (menu == 0) return;
        try
        {
            bool locked = LockedWalker;
            // 사람과 주고받는 동작(0.5.16, 2026-10-09 사용자: 츄르 주기·쓰다듬기·놀아 주기) — 그림이 있을 때만(밝은 고양이)
            bool any = false;
            foreach (var (id, clip, label) in new[] { (IdCatPet, "I1", T.CatMenuPet), (IdCatTreat, "I2", T.CatMenuTreat), (IdCatPlay, "I3", T.CatMenuPlay) })
                if (CatWidget.CanInteract(clip)) { fixed (char* p = label) Native.AppendMenuW(menu, Native.MF_STRING, (nuint)id, p); any = true; }
            if (any) Native.AppendMenuW(menu, Native.MF_SEPARATOR, 0, null);
            fixed (char* p = locked ? T.TrayUnlock : T.TrayOpen) Native.AppendMenuW(menu, Native.MF_STRING, IdCatOpen, p);
            if (!locked)
            {
                if (_cfg.HasMaster) fixed (char* p = T.TrayLockNow) Native.AppendMenuW(menu, Native.MF_STRING, IdCatLock, p);
                fixed (char* p = T.CatMenuSettings) Native.AppendMenuW(menu, Native.MF_STRING, IdCatSettings, p);
            }
            Native.AppendMenuW(menu, Native.MF_SEPARATOR, 0, null);
            fixed (char* p = T.CatMenuHide) Native.AppendMenuW(menu, Native.MF_STRING, IdCatHide, p);
            Native.GetCursorPos(out Native.POINT pt);
            Native.SetForegroundWindow(_hwnd);
            int cmd = Native.TrackPopupMenu(menu, Native.TPM_RIGHTBUTTON | Native.TPM_RETURNCMD | Native.TPM_BOTTOMALIGN, pt.x, pt.y, 0, _hwnd, 0);
            OnWalkerCommand(cmd, gen);
        }
        finally { Native.DestroyMenu(menu); }
    }

    private void OnWalkerCommand(int cmd, int gen)
    {
        if (Program.IsTestMode) Native.SetPropW(_hwnd, "OneKeyTestCatMenu", cmd);
        switch (cmd)
        {
            case IdCatPet: CatWidget.Interact("I1"); break;
            case IdCatTreat: CatWidget.Interact("I2"); break;
            case IdCatPlay: CatWidget.Interact("I3"); break;
            case IdCatNote: Dialog.Show(_hwnd, CatNoteText(), T.CatNoteTitle, Native.MB_OK, width: 440, okText: T.CommonClose, fold: false); break;
            case IdCatGuide: Dialog.Show(_hwnd, T.HelpCat, T.CatGuideTitle, Native.MB_OK, width: 460, okText: T.CommonClose, fold: false); break;
            case IdCatOpen: OnWalkerClick(gen, fromMenu: true); break;
            case IdCatLock: if (Unlocked) LockNow(); break;
            case IdCatSettings: ShowMainWindow(); if (Unlocked && !OnLockScreen) ShowScreen(Screen.Settings); break;
            case IdCatHide:
                _walkerHidden = true;
                SyncWalker();
                ShowBalloon(AppTitle, T.CatHiddenNote, Native.NIIF_INFO);
                break;
        }
    }

    /// <summary>
    /// 고양이 수첩(0.5.20): 지금 상태 한 장 — 이름 · 단계, 하트 · 점수 · 크기, 다음 단계까지, 오늘(출근 도장 · 놀이 · 비밀번호 · 백업 · 마스터), 알아 두기.
    /// 앨범 그림이 오면 같은 자리에 앨범을 더한다(work/reviews 앨범 요청).
    /// </summary>
    private string CatNoteText()
    {
        bool light = CatWidget.IsLight, grows = CatWidget.HasInteractArt(light);
        string name = light ? _cfg.CatNameLight : _cfg.CatNameDark;
        int score = CatGrowth.Score(light), step = CatGrowth.Step(light);
        double level = CatGrowth.Level(light);
        var sb = new System.Text.StringBuilder();
        sb.Append("## ").Append(name.Length > 0 ? name + " \u00B7 " : "").Append(CatGrowth.StageName(step)).Append('\n');
        if (grows) sb.Append(Dialog.ArtMark).Append("stages:").Append(step).Append('\n');   // 빵 여섯 개(지금 단계는 크게)
        if (grows)
        {
            string hearts = "";
            for (int i = 0; i < CatGrowth.Steps; i++) hearts += level - i >= 1 ? "\u2665" : "\u2661";
            int pct = (int)Math.Round(CatGrowth.Scale(light, true) * 100);
            sb.Append("\u2022 ").Append(T.CatNoteHearts(hearts, score, pct)).Append('\n');
            sb.Append("\u2022 ").Append(step < CatGrowth.Steps ? T.CatNoteNext(CatGrowth.StageName(step + 1), (step + 1) * CatGrowth.StepScore - score) : T.CatNoteMax).Append('\n');
        }
        else sb.Append("\u2022 ").Append(T.CatNoteNoGrow).Append('\n');
        // 오늘의 할 일(0.5.21-G, 사용자: 오늘 섹션은 미션 같다 — 크기 단계처럼 칸을 미리 두고 달성하면 채우기): 동그란 칸들(HelpArt missions)
        sb.Append("## ").Append(T.CatNoteToday).Append('\n');
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
        ms.Add("backup=" + (CatGrowth.WaitDays(CatGrowth.Habit.Backup) > 0 ? 1 : 0));   // 백업 · 마스터는 그 기간(7일 · 30일) 안에 했으면 채움
        ms.Add("master=" + (CatGrowth.WaitDays(CatGrowth.Habit.Master) > 0 ? 1 : 0));
        sb.Append(Dialog.ArtMark).Append("missions:").Append(string.Join("|", ms)).Append('\n');
        if (!CatGrowth.StampDue) sb.Append("\u2022 ").Append(T.CatNoteStampDone(CatGrowth.Streak, CatGrowth.StampTotal)).Append('\n');
        sb.Append("\u2022 ").Append(T.CatMisHint).Append('\n');
        // 앨범(0.5.20): 처음 본 장면은 그림 + 이름, 못 본 장면은 흐린 실루엣 + ?
        sb.Append("## ").Append(T.CatAlbumTitle(CatGrowth.SeenCount, CatGrowth.AlbumKeys.Length)).Append('\n');
        sb.Append(Dialog.ArtMark).Append("album:").Append(string.Join("|", CatGrowth.AlbumKeys.Select(k => k + "=" + (CatGrowth.IsSeen(k) ? "1" : "0"))) ).Append('\n');
        sb.Append("\u2022 ").Append(T.CatAlbumHint).Append('\n');
        sb.Append("## ").Append(T.CatNoteKnow).Append('\n');
        sb.Append("\u2022 ").Append(T.CatNoteDecay);
        return sb.ToString();
    }

    private void OnWalkerClick(int gen, bool fromMenu = false)
    {
        // 메뉴에서 고른 [열기]는 고양이 클릭 검사를 다시 하지 않는다(메뉴 판이 닫힌 직후에는 앞 창이 없어 EnvOk 가 숨김으로 본다)
        if (!WalkerWanted || (!fromMenu && !CatWidget.AcceptClick(gen))) return;   // 그사이 잠김·끔·창이 보임·다시 보임(낡은 클릭)·환경 부적합이면 무시(Codex R83-2)
        bool locked = LockedWalker;
        _lockedWalker = false;
        ShowMainWindow();   // 잠겨 있으면 잠금 위젯(ShowMainWindow → ShowLockWidget)
        if (locked) SyncWalker();
    }
}
