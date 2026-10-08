using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 프로그램·폴더 바로 실행의 화면 쪽(설계 v2 5장): [+ 추가] 종류 고르기, 실행 항목 편집 화면, 목록 위 아이콘 띠, 실행 단축키.
/// 실행 자체는 Launcher, 저장은 LaunchStore. 실행은 단축키·아이콘 누름(Enter 포함)에서만 한다.
/// </summary>
internal sealed unsafe partial class App
{
    private const int IdAkBack = 4000, IdAkPassword = 4001, IdAkProgram = 4002, IdAkFolder = 4003, IdAkNote = 4004;
    private const int IdAkLogin = 4006, IdAkMulti = 4007;   // [+ 추가]의 사이트·앱 로그인(ID·PW) / 연속된 문구 입력
    private const int IdLeBack = 4010, IdLeName = 4011, IdLeHotkey = 4012, IdLeRepick = 4013, IdLeLeft = 4014, IdLeRight = 4015, IdLeDelete = 4016, IdLeCancel = 4017, IdLeSave = 4018;
    private const int IdLeIcon = 4019, IdLeIconReset = 4020, IdLePreview = 4021;
    private const int IdTile = 4100, IdStripEdit = 4142, IdStripNote = 4143;
    private const int IdStripPrev = 4140, IdStripNext = 4141, IdStripPrev2 = 4144, IdStripNext2 = 4145;   // 줄 양 끝 ‹ ›(넘칠 때, 넘길 쪽에만): 프로그램 줄 / 폴더 줄
    private const int IdPkBack = 4200, IdPkBrowse = 4201, IdPkSearch = 4202, IdPkRow = 4300, PickMax = 80;
    private const uint WM_PICK_REBUILD = Native.WM_APP + 35;   // 프로그램 고르기의 검색 칸이 바뀐 뒤(칸 알림이 끝난 다음) 목록 다시 만들기
    private const int LaunchHotkeyBase = 200;   // RegisterHotKey id: 비밀번호 0–98, 실행 항목 200–229
    private const uint WM_TILE_NAV = Native.WM_APP + 34;   // 띠의 칸에서 ← →: wParam = −1/+1
    private const uint WM_LAUNCH_DONE = Native.WM_APP + 36;   // 실행 작업 스레드가 끝남: wParam = 요청 번호
    private const uint WM_LAUNCH_SEEN = Native.WM_APP + 37;   // 실행을 요청한 대상의 창이 나타남: wParam = 감시 번호
    private const uint WM_ICON_READY = Native.WM_APP + 38;    // 아이콘을 읽음(IconLoader)
    private const uint WM_PICK_READY = Native.WM_APP + 39;    // 시작 메뉴 목록을 다 읽음: wParam = 읽기 번호
    internal const uint WM_TILE_DROP = Native.WM_APP + 42;    // [편집] 중 띠의 칸을 끌어 놓음: wParam = 칸 창, lParam = 놓은 칸의 가운데 x(본창 좌표)
    private const int IdStripReset = 4146;                     // [목록 새로 시작](손상된 launch.dat, 사용자 결정 D2)
    private const int DebounceMs = 500;
    // 칸: 아이콘 32 + 아래 이름 두 줄(2026-10-04 사용자: 이름이 늘 보여야 한다 — 아이콘만 늘어서면 뭐가 뭔지 모른다)
    private const int TileW = 48, TileH = 72, TileGap = 4, IconTileW = 40, IconTileH = 52;   // 0.3.25: 아이콘 사이 간격을 반으로(사용자) — 이름 칸 68→48, 아이콘만 52→40   // 이름을 보이는 칸 / 아이콘만 보이는 칸(설정 › 바로 실행)

    private LaunchItem? _launchEdit;        // 편집 중인 초안(저장 전에는 목록을 바꾸지 않는다)
    private int _launchEditIndex = -1;      // 목록의 자리. 새 항목이면 −1
    private bool _stripEdit;                // 띠의 [편집] 모드: 칸을 누르면 실행 대신 편집 화면
    private readonly record struct PickEntry(string Name, string Path, string Group, bool Pinned = false);   // Pinned: 작업 표시줄에 고정된 앱
    private List<PickEntry>? _pickAll;      // 시작 메뉴의 프로그램(바로 가기) — 고르기 화면을 처음 열 때 읽는다
    private List<PickEntry> _pickShown = new();
    private string _pickFilter = "";
    private bool _pickForRepick;            // 편집 화면의 [다시 고르기]에서 왔다(돌아갈 곳·단축키 등록 규칙)
    private nint _previewIcon;              // 편집 화면 미리보기 아이콘
    private string _previewKey = "";        // 그 아이콘을 읽은 값(대상·아이콘 파일·번호)
    private const string PreviewIconId = "\u0001preview";
    private nint _phFolder, _phProgram;     // 읽는 동안·읽지 못할 때의 기본 아이콘(디스크를 보지 않고 만든다, 지우지 않는다)
    private readonly bool[] _launchHotkeyFailed = new bool[LaunchStore.Max];
    private readonly Dictionary<string, nint> _launchIcons = new();   // 항목 id → 아이콘(대상 경로가 바뀌면 지운다)
    private readonly Dictionary<string, string> _launchIconKey = new();   // 항목 id → 그 아이콘을 읽은 대상·아이콘 값(세대)
    private readonly Dictionary<string, long> _launchOpened = new();  // 대상 세대(Launcher.KeyOf) → 실행을 요청했는데 아직 창을 못 본 시각
    private readonly Dictionary<int, (string Key, long At, string Path, nint Fg)> _launchWatch = new();    // 감시 번호 → (대상 세대, 실행 요청 시각, 실행한 파일, 누를 때 앞 창)
    private readonly Dictionary<string, long> _launchLatest = new();   // 대상 세대 → 가장 최근 실행 요청 시각(옛 감시는 아무것도 하지 않는다, C28-2)
    private readonly Dictionary<string, nint> _launchFg = new();       // 대상 세대 → 누를 때 앞에 있던 창
    private readonly HashSet<int> _launchFronted = new();               // 이미 한 번 앞으로 가져온(또는 깜박인) 감시 번호 — 한 요청에 한 번
    private readonly Dictionary<string, string> _launchRelated = new();   // 대상 세대 → 실행 뒤 실제 창을 띄운 다른 실행 파일(실행기형, 이번 실행 동안)
    private int _launchWatchNo;
    private readonly Dictionary<string, long> _launchLastPress = new();

    // ------------------------------------------------------------------ 시작·단축키

    /// <summary>시작할 때(잠김 포함) 실행 목록을 읽고 실행 항목 단축키를 등록한다. 읽기만으로는 아무것도 실행하지 않는다.</summary>
    private void InitLaunch()
    {
        LaunchStore.Load();
        RegisterLaunchHotkeys(silent: true);
    }

    /// <summary>실행 항목 단축키(잠금과 상관없이 늘 등록). 읽기 전용·사용 불가 항목은 등록하지 않는다.</summary>
    private string RegisterLaunchHotkeys(bool silent)
    {
        var failed = new List<string>();
        for (int i = 0; i < LaunchStore.Max; i++) { Native.UnregisterHotKey(_hwnd, LaunchHotkeyBase + i); _launchHotkeyFailed[i] = false; }
        if (LaunchStore.ReadOnly) return string.Empty;
        for (int i = 0; i < LaunchStore.Items.Count && i < LaunchStore.Max; i++)
        {
            LaunchItem it = LaunchStore.Items[i];
            if (!it.Valid || !it.HasHotkey) continue;
            const uint MOD_NOREPEAT = 0x4000;
            if (!Native.RegisterHotKey(_hwnd, LaunchHotkeyBase + i, it.Mods | MOD_NOREPEAT, it.Vk))
            {
                failed.Add($"{it.Name} ({HotkeyBox.Text(it.Mods, it.Vk)})");
                _launchHotkeyFailed[i] = true;
            }
        }
        if (failed.Count == 0) return string.Empty;
        string report = T.HotkeyRegFailed + "\n  " + string.Join("\n  ", failed);
        if (silent && !_quietHotkeyReport) ShowBalloon(AppTitle, report, Native.NIIF_WARNING);
        return report;
    }

    private void UnregisterLaunchHotkeys()
    {
        for (int i = 0; i < LaunchStore.Max; i++) Native.UnregisterHotKey(_hwnd, LaunchHotkeyBase + i);
    }

    /// <summary>WM_HOTKEY 의 실행 항목 범위인가.</summary>
    private static bool IsLaunchHotkey(int id) => id >= LaunchHotkeyBase && id < LaunchHotkeyBase + LaunchStore.Max;

    /// <summary>실행(단축키·아이콘 공통). 잠금 상태·마스터 입력·초안·자동 잠금은 건드리지 않는다. 실제 일은 작업 스레드(Launcher.Start),
    /// 결과는 WM_LAUNCH_DONE 에서. force = 확인 창에서 [예](다시 실행).</summary>
    private void RunLaunch(int index, bool force = false)
    {
        if (_recoveryOnly) return;   // 끝나지 않은 복원(R43-1)
        if (Program.IsTestMode) Native.SetPropW(_hwnd, "OneKeyTestLaunchCalls", ++_testLaunchCalls);
        if (index < 0 || index >= LaunchStore.Items.Count) return;
        LaunchItem it = LaunchStore.Items[index];
        if (LaunchStore.ReadOnly) { Toast.Show(_hwnd, LaunchStore.ReadOnlyReason!, 6000, warn: true); return; }
        if (!it.Valid) { LaunchToast(Launcher.Result.Unsupported, it.Name); return; }
        long now = Environment.TickCount64;
        if (!force)
        {
            if (_launchLastPress.TryGetValue(it.Id, out long last) && now - last < DebounceMs) return;   // 짧은 사이 다시 누름
            _launchLastPress[it.Id] = now;
        }
        // 끝났는데 결과 메시지가 아직 오지 않은 요청이 있으면 그 결과부터 받는다(실행 요청 기록이 새 요청보다 먼저 — RW-1)
        if (Launcher.Unconsumed is int done and not 0)
        {
            if (OnLaunchDone(done)) return;   // 확인 창을 띄웠으면 이번 누름은 거기서 끝
            if (index >= LaunchStore.Items.Count || LaunchStore.Items[index] != it) return;   // 그 사이 목록이 바뀜
        }
        string key = Launcher.KeyOf(it);
        _launchFg[key] = Native.GetForegroundWindow();   // 누를 때 사용자가 있던 창(늦게 뜬 창을 앞으로 가져와도 되는지 판단)
        long opened = _launchOpened.TryGetValue(key, out long t0) ? t0 : 0;
        if (Launcher.Start(it, opened, force, _launchRelated.GetValueOrDefault(key, ""), _hwnd, WM_LAUNCH_DONE) is null) LaunchToast(Launcher.Result.Busy, it.Name);
    }

    /// <summary>작업 스레드가 끝났다: 결과에 따라 창을 앞으로·실행 요청 상태 기록·확인 창·알림. 확인 창을 띄웠으면 true.
    /// 결과 메시지로도, 다음 누름이 먼저 올 때(RunLaunch)도 불린다 — 같은 결과는 한 번만 받는다(Take).</summary>
    private bool OnLaunchDone(int no)
    {
        Launcher.Job? j = Launcher.Take(no);
        if (Program.IsTestMode) Native.SetPropW(_hwnd, "OneKeyTestLaunchExec", Launcher.ExecCalls);
        if (j is null || j.Cancelled || _exiting) return false;   // 이미 받음·삭제·대상 변경·종료 뒤의 늦은 결과는 버린다(그 요청의 결과로만 정리)
        Launcher.Result r = j.Outcome;
        switch (r)
        {
            case Launcher.Result.Window:
                _launchOpened.Remove(j.Key);
                r = Launcher.Focus(j.Window, j.IsFolder, j.FocusPath.Length > 0 ? j.FocusPath : j.Path, j.App);
                break;
            case Launcher.Result.Opened:
                long at0 = Environment.TickCount64;
                _launchOpened[j.Key] = at0;
                int w = ++_launchWatchNo;
                _launchWatch[w] = (j.Key, at0, j.Path, _launchFg.GetValueOrDefault(j.Key));   // 대상 세대 + 실행 요청 세대(시각): 옛 감시가 새 실행의 상태를 지우지 않게
                _launchLatest[j.Key] = at0;
                if (!Launcher.Watch(w, j.IsFolder, j.Path, j.App, j.Before, j.RootPid, j.RootCreated, Launcher.TakeRootHandle(j), j.ReqFileTime, _hwnd, WM_LAUNCH_SEEN)) _launchWatch.Remove(w);
                break;
            case Launcher.Result.NeedsConfirm:
                // 사용자 결정 D1: 자동으로 두 번 실행하지 않는다. 기본 [아니요]
                if (Msg(T.LaunchConfirmRetry(j.Name), AppTitle, Native.MB_YESNO | Native.MB_ICONQUESTION | Native.MB_DEFBUTTON2) != Native.IDYES) return true;
                int at = LaunchStore.Items.FindIndex(x => Launcher.KeyOf(x) == j.Key);
                if (at >= 0) RunLaunch(at, force: true);   // 다시 창을 찾고 검사한 뒤에만 실행한다
                return true;
        }
        LaunchToast(r, j.Name);
        return false;
    }

    /// <summary>
    /// 감시가 새로 뜬 그 대상의 창을 알렸다(hwnd), 또는 끝났다(0 — 감시 번호·남은 관찰 정리). Codex C28-2:
    /// - 실행 요청 상태(_launchOpened)는 그 감시를 시작한 요청이 마지막일 때만 지운다.
    /// - 옛 감시(같은 대상의 더 새 요청이 있음)·지운 항목·바뀐 대상이면 아무것도 하지 않는다.
    /// - 관찰한 창이 지금도 같은 창(같은 프로세스·실행 파일·앱 ID)일 때만 기억하고 앞으로 가져온다(핸들 재사용·교체 방지).
    /// - 앞으로 가져오기는 한 요청에 한 번, 요청 뒤 1분 안, 그리고 사용자가 누를 때의 창(또는 1Key·바탕 화면)에 그대로 있을 때만.
    ///   다른 창으로 옮겨 일하는 중이면 깜박이기만 한다.
    /// - 실행한 파일과 다른 실행 파일의 창(이 실행이 만든 자식 프로세스)이면 다음 누름에서 그 창을 찾도록 기억한다(1Key 를 다시 켜면 잊음).
    /// </summary>
    private void OnLaunchSeen(int no, nint hwnd)
    {
        if (hwnd == 0) { _launchWatch.Remove(no); _launchFronted.Remove(no); Launcher.DropSeen(no); return; }
        Launcher.SeenInfo? info = Launcher.TakeSeen(no, hwnd);
        if (!_launchWatch.TryGetValue(no, out var wk)) return;
        if (_launchOpened.TryGetValue(wk.Key, out long at) && at == wk.At) _launchOpened.Remove(wk.Key);
        if (info is null) return;   // 폴더 창 알림(관찰 없음): 상태 정리만
        nint fg = Native.GetForegroundWindow();
        bool first = !_launchFronted.Contains(no);
        var step = Launcher.SeenPlan(wk.At, _launchLatest.TryGetValue(wk.Key, out long latest) ? latest : (long?)null,
            LaunchStore.Items.FindIndex(x => Launcher.KeyOf(x) == wk.Key) >= 0, Launcher.StillSame(info),
            first, Environment.TickCount64, fg, hwnd, wk.Fg, fg != 0 && IsOwnOrDesktop(fg));
        if (step == Launcher.SeenStep.Ignore) return;
        if (info.Related) _launchRelated[wk.Key] = info.Image;
        if (first) _launchFronted.Add(no);   // 앞으로 가져오기는 한 요청에 한 번(이번에 안 가져와도 첫 관찰에서 쓴다)
        if (step == Launcher.SeenStep.Front) Launcher.BringForward(hwnd);
        else if (step == Launcher.SeenStep.Flash) Launcher.Flash(hwnd);   // 사용자가 다른 창에서 일하는 중: 가로채지 않는다
    }

    /// <summary>1Key 의 창이거나 바탕 화면·작업 표시줄(사용자가 다른 프로그램으로 옮겨 가지 않은 상태로 본다).</summary>
    private static bool IsOwnOrDesktop(nint h)
    {
        Native.GetWindowThreadProcessId(h, out uint pid);
        if (pid == (uint)Environment.ProcessId) return true;
        string cls = Native.GetClassName(h);
        return cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    private void LaunchToast(Launcher.Result r, string name)
    {
        string? msg = r switch
        {
            Launcher.Result.Opened or Launcher.Result.Focused or Launcher.Result.Cancelled or Launcher.Result.WebOpened => null,
            Launcher.Result.BrowserMissing => T.LaunchResBrowserMissing(name),
            Launcher.Result.Pending => T.LaunchResPending(name),
            Launcher.Result.Busy => T.LaunchResBusy,
            Launcher.Result.NotFound => T.LaunchResNotFound(name),
            Launcher.Result.Unsupported => T.LaunchResUnsupported(name),
            Launcher.Result.CannotVerify => T.LaunchResCannotVerify(name),
            Launcher.Result.ShellUnavailable => T.LaunchResShell,
            Launcher.Result.RightsUnknown => T.LaunchResRights,
            Launcher.Result.FocusRefused => T.LaunchResFocusRefused(name),
            Launcher.Result.ReadOnly => LaunchStore.ReadOnlyReason,
            _ => T.LaunchResFailed(name),
        };
        if (msg is null) return;
        bool warn = r is not (Launcher.Result.Pending or Launcher.Result.Busy);
        // 본창이 숨어 있어도 보이게: 알림은 화면 위에 뜨는 토스트(본창이 소유자)
        Toast.Show(_hwnd, msg, warn ? 6000 : 1800, warn: warn);
    }

    /// <summary>항목이 바뀌거나 사라졌다: 진행 중 요청을 취소하고 그 항목의 실행 요청 상태를 지운다.</summary>
    private void ForgetLaunch(string id)
    {
        Launcher.Cancel(id);
        foreach (string k in _launchOpened.Keys.Where(k => k.StartsWith(id + "\n", StringComparison.Ordinal)).ToList()) _launchOpened.Remove(k);
        foreach (string k in _launchRelated.Keys.Where(k => k.StartsWith(id + "\n", StringComparison.Ordinal)).ToList()) _launchRelated.Remove(k);
        foreach (string k in _launchLatest.Keys.Where(k => k.StartsWith(id + "\n", StringComparison.Ordinal)).ToList()) { _launchLatest.Remove(k); _launchFg.Remove(k); }
    }

    /// <summary>[목록 새로 시작](사용자 결정 D2): 확인 → 원본을 launch.dat.bad 로 보관하고 빈 목록.</summary>
    private void ResetLaunchList()
    {
        if (!LaunchStore.CanReset) return;
        if (Msg(T.LaunchResetConfirm, AppTitle, Native.MB_YESNO | Native.MB_ICONWARNING | Native.MB_DEFBUTTON2) != Native.IDYES) return;
        Launcher.Cancel(null);
        if (!LaunchStore.Reset()) { Msg(T.LaunchResetFail, AppTitle, Native.MB_OK | Native.MB_ICONERROR); return; }
        CancelAllFav();
        _launchOpened.Clear(); _launchRelated.Clear();
        foreach (string id in _launchIconKey.Keys.ToList()) DropIcon(id);
        RegisterLaunchHotkeys(silent: true);
        if (_cur == Screen.List) RefreshList(); else if (_cur == Screen.AddKind) ShowScreen(Screen.AddKind);
        Toast.Show(_hwnd, T.LaunchResetDone);
    }

    // ------------------------------------------------------------------ [+ 추가] 종류 고르기

    private void BuildAddKind()
    {
        // 새 디자인(2026-10-08, 시안 Add-light/dark.dc.html): 머리줄 ‹ · 가운데 제목 · [?], 판 안에 "넣을 글" 조각 셋과 "바로 실행" 조각 셋(높이 62, 사이 8).
        // 조각 = 둥근 표식(넣는 글은 강조색 Aa·ID·+, 바로 실행은 갈색 아이콘) + 이름 + 설명 + ›. 두 묶음(2026-10-06 사용자 — 동료가 어렵다고 함)은 그대로.
        int y = MetalHeader(IdAkBack, T.AddTitle);
        int dialTop = y;
        y += DialPadX;
        int cx = DialX + DialPadX, cw = DialW - 2 * DialPadX;
        nint labelFont = Theme.Sized(11.5, false);
        uint labelInk = Metal.Ref(Metal.InkLabel(Theme.IsDark));
        nint Choice(int id, string title, string sub, string mark, int at)
        {
            nint r = Make(Row.ClassName, title, Row.Choice | Native.WS_TABSTOP, cx - MetalUi.TileMarginX, at - MetalUi.TileMarginTop,
                          cw + 2 * MetalUi.TileMarginX, MetalUi.ChoiceH + MetalUi.TileMarginTop + MetalUi.TileMarginBottom, id);
            if (r != 0) Row.Set(r, title, sub, mark);
            return r;
        }
        bool slotsFull = Enumerable.Range(0, Config.SlotCount).All(i => SlotInUse(_cfg.Slots[i]));
        bool launchFull = LaunchStore.Items.Count >= LaunchStore.Max;
        const int Step = MetalUi.ChoiceH + MetalUi.ChoiceGap;

        Label(T.AddGroupText, cx + 6, y, cw - 12, 21, labelFont, labelInk, true, Native.SS_LEFT | Native.SS_ENDELLIPSIS);
        y += 21 + MetalUi.ChoiceGap;
        nint p = Choice(IdAkPassword, T.AddPassword, T.AddPasswordSub, "Aa", y); y += Step;
        nint lg = Choice(IdAkLogin, T.AddLogin, T.AddLoginSub, "ID", y); y += Step;
        nint mu = Choice(IdAkMulti, T.AddMulti, T.AddMultiSub, "+", y); y += MetalUi.ChoiceH;
        if (slotsFull) foreach (nint h in new[] { p, lg, mu }) if (h != 0) Native.EnableWindow(h, false);

        y += MetalUi.ChoiceGap;
        Label(T.SetLaunchSection, cx + 6, y + 6, cw - 12, 21, labelFont, labelInk, true, Native.SS_LEFT | Native.SS_ENDELLIPSIS);
        y += 27 + MetalUi.ChoiceGap;
        nint g = Choice(IdAkProgram, T.AddProgram, T.AddProgramSub, "\uF5B0", y); y += Step;   // PlaySolid(시안 ▶)
        nint f = Choice(IdAkFolder, T.AddFolder, T.AddFolderSub, IcFolder, y); y += Step;
        nint wsite = Choice(IdAkWeb, T.AddWeb, T.AddWebSub, IcSite, y); y += MetalUi.ChoiceH;
        if (LaunchStore.ReadOnly || launchFull)
        {
            if (g != 0) Native.EnableWindow(g, false);
            if (f != 0) Native.EnableWindow(f, false);
            if (wsite != 0) Native.EnableWindow(wsite, false);
        }
        y += DialPadX;
        _page.Dials.Add((DialX, dialTop, DialW, y - dialTop));
        if (LaunchStore.ReadOnly)
        {
            y = Footer(LaunchStore.ReadOnlyReason!, y);
            if (LaunchStore.CanReset) { Button(IdStripReset, T.LaunchReset, Btn.DangerBordered, Margin, y + 4, Math.Min(CardW, LabelW(_font, T.LaunchReset) + 32), 32); y += 40; }
        }
        else if (launchFull) y = Footer(T.AddFull, y);
        _page.Height = y + 14;
    }

    /// <summary>[프로그램]: 설치된 프로그램 목록(시작 메뉴) 또는 [찾아보기](2026-10-04 사용자).</summary>
    private void OnAddProgram()
    {
        _pickForRepick = false; _pickFilter = "";
        StartPickLoad();   // 들어올 때마다 새로(새로 설치한 프로그램)
        ShowScreen(Screen.PickProgram);
    }

    /// <summary>프로그램을 하나 골랐다(목록·찾아보기 공통): 새 항목이면 편집 화면으로, [다시 고르기]면 초안의 대상을 바꾼다.</summary>
    private void OnProgramChosen(string path, string? name = null)
    {
        var probe = new LaunchItem { Id = _pickForRepick && _launchEdit is not null ? _launchEdit.Id : LaunchStore.NewId(), Valid = true };
        string? reject = PrepareProgram(path, probe, name);
        if (reject is not null) { Msg(reject, AppTitle, Native.MB_OK | Native.MB_ICONWARNING); return; }
        if (_pickForRepick && _launchEdit is not null)
        {
            LaunchItem e = _launchEdit;
            e.Kind = probe.Kind; e.Target = probe.Target; e.Args = probe.Args; e.Dir = probe.Dir;
            if (e.Name.Length == 0) e.Name = probe.Name;
            ShowScreen(Screen.LaunchEdit);
            return;
        }
        OpenLaunchEdit(-1, probe);
    }

    // ------------------------------------------------------------------ 프로그램 고르기 (설치된 프로그램 목록 + 찾아보기)

    private const int PickScanMax = 2000, PickScanMs = 5000, PickScanDepth = 4;
    private int _pickLoadNo;                // 지금 기다리는 읽기 번호(화면을 다시 열면 새 번호 — 늦게 끝난 옛 읽기는 버린다)
    private bool _pickPartial;              // 상한·시간·접근 거부로 다 읽지 못함("프로그램 없음"과 구분)
    private static readonly object _pickLock = new();
    private static (int No, List<PickEntry> List, bool Partial)? _pickResult;

    /// <summary>시작 메뉴(모든 사용자 + 이 사용자)의 바로 가기를 백그라운드에서 읽기 시작한다(Codex R36-2). 끝나면 WM_PICK_READY.</summary>
    private void StartPickLoad()
    {
        _pickAll = null; _pickPartial = false;
        int no = ++_pickLoadNo;
        nint hwnd = _hwnd;
        var t = new Thread(() =>
        {
            var (list, partial) = ScanStartMenu();
            lock (_pickLock) _pickResult = (no, list, partial);
            Native.PostMessageW(hwnd, WM_PICK_READY, no, 0);
        }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);   // 스토어 앱 목록(셸 앱 폴더)은 COM
        t.Start();
    }

    private void OnPickReady(int no)
    {
        (int No, List<PickEntry> List, bool Partial)? r;
        lock (_pickLock) { r = _pickResult; if (r?.No == no) _pickResult = null; }
        if (r is null || r.Value.No != no || no != _pickLoadNo) return;   // 옛 읽기
        _pickAll = r.Value.List; _pickPartial = r.Value.Partial;
        if (_cur == Screen.PickProgram) RebuildPick();   // 화면을 떠났으면 다시 열지 않는다
    }

    /// <summary>바로 가기를 열거하며 개수·시간·깊이 상한을 바로 적용한다. 같은 경로만 하나로(이름이 같아도 다른 바로 가기는 둘 다 — 하위 폴더로 구분).
    /// 이름의 uninstall·제거 거르기는 편의 분류일 뿐 안전 검사가 아니다(고를 때 지원 범위를 검사한다).</summary>
    private static (List<PickEntry>, bool partial) ScanStartMenu()
    {
        var list = new List<PickEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool partial = false;
        long end = Environment.TickCount64 + PickScanMs;
        if (Program.TestFails("pick:slow")) Thread.Sleep(2000);
        string? testRoot = Program.IsTestMode ? Environment.GetEnvironmentVariable("ONEKEY_TEST_STARTMENU") : null;
        string[] roots = !string.IsNullOrEmpty(testRoot) ? new[] { testRoot }
            : new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), Environment.GetFolderPath(Environment.SpecialFolder.Programs) };
        foreach (string root in roots)
        {
            if (root.Length == 0 || !Directory.Exists(root)) continue;
            var stack = new Stack<(string Dir, int Depth)>();
            stack.Push((root, 0));
            while (stack.Count > 0)
            {
                if (list.Count >= PickScanMax || Environment.TickCount64 > end) return (Sorted(list), true);
                var (dir, depth) = stack.Pop();
                try
                {
                    var di = new DirectoryInfo(dir);
                    foreach (var fi in di.EnumerateFiles("*.lnk"))
                    {
                        if (list.Count >= PickScanMax || Environment.TickCount64 > end) return (Sorted(list), true);
                        if (!seen.Add(fi.FullName)) continue;
                        string name = Path.GetFileNameWithoutExtension(fi.Name);
                        if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase) || name.Contains("제거") || name.Contains("언인스톨")) continue;
                        string rel = Path.GetRelativePath(root, dir);
                        list.Add(new PickEntry(name, fi.FullName, rel == "." ? "" : rel));
                    }
                    if (depth < PickScanDepth)
                        foreach (var sub in di.EnumerateDirectories())
                            if ((sub.Attributes & FileAttributes.ReparsePoint) == 0) stack.Push((sub.FullName, depth + 1));   // 연결 지점은 따라가지 않는다(고리 방지)
                }
                catch { partial = true; }   // 접근 거부 등: 그 폴더만 빠짐
            }
        }
        // 스토어·패키지 앱(계산기 등, 2026-10-05 사용자): 시작 메뉴 폴더에 바로 가기가 없어 셸의 앱 목록에서 읽는다. 시험의 격리 시작 메뉴에서는
        // ONEKEY_TEST_STOREAPPS=1 일 때만(실제 PC 의 앱 목록이 시험 기대값을 흔들지 않게)
        if (testRoot is null || testRoot.Length == 0 || Environment.GetEnvironmentVariable("ONEKEY_TEST_STOREAPPS") == "1")
            if (!ScanStoreApps(list, end)) partial = true;
        // 작업 표시줄에 고정된 앱(2026-10-05 사용자: 계산기는 보통 작업 표시줄·시작 메뉴에 있다 — 목록 맨 위에). 시험은 ONEKEY_TEST_TASKBAR 폴더만
        string? testBar = Program.IsTestMode ? Environment.GetEnvironmentVariable("ONEKEY_TEST_TASKBAR") : null;
        string bar = !string.IsNullOrEmpty(testBar) ? testBar
            : string.IsNullOrEmpty(testRoot) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar") : "";
        if (bar.Length > 0) ScanTaskbar(bar, list);
        return (Sorted(list), partial);
    }

    private static readonly Guid CLSID_ShellLinkP = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid IID_IShellLinkWP = new("000214F9-0000-0000-C000-000000000046");
    private static readonly Guid IID_IPersistFileP = new("0000010b-0000-0000-C000-000000000046");

    /// <summary>작업 표시줄 고정 바로 가기: 실행 파일이면 그 바로 가기로, 스토어·패키지 앱이면 앱 ID 로. 그 밖(폴더·웹 등)은 뺀다.</summary>
    private static void ScanTaskbar(string dir, List<PickEntry> list)
    {
        string[] files;
        try { files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.lnk") : Array.Empty<string>(); } catch { return; }
        int hrInit = CoInitializeEx(0, 2);
        try
        {
            foreach (string lnk in files.Take(64))
            {
                nint link = 0, pf = 0, pidl = 0, item = 0;
                Guid clsid = CLSID_ShellLinkP, iid = IID_IShellLinkWP, iidPf = IID_IPersistFileP, iidItem = IID_IShellItemG;
                try
                {
                    if (CoCreateInstance(ref clsid, 0, 1, ref iid, out link) < 0 || link == 0) continue;
                    if (((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(nint**)link)[0])(link, &iidPf, &pf) < 0 || pf == 0) continue;
                    int hr;
                    fixed (char* p = lnk) hr = ((delegate* unmanaged[Stdcall]<nint, char*, uint, int>)(*(nint**)pf)[5])(pf, p, 0);   // IPersistFile::Load
                    if (hr < 0) continue;
                    if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)(*(nint**)link)[4])(link, &pidl) < 0 || pidl == 0) continue;   // IShellLinkW::GetIDList
                    if (SHCreateItemFromIDList(pidl, &iidItem, &item) < 0 || item == 0) continue;
                    var t = LaunchDrop.ItemTarget(item);
                    if (t is null || t.Value.Path.Length == 0) continue;
                    string name = Path.GetFileNameWithoutExtension(lnk);
                    if (t.Value.Path.StartsWith(Launcher.AppsFolderPrefix, StringComparison.OrdinalIgnoreCase))
                        list.Add(new PickEntry(name, t.Value.Path, T.PickStoreGroup, Pinned: true));
                    else if (t.Value.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        list.Add(new PickEntry(name, lnk, "", Pinned: true));   // 바로 가기 자체(인자·작업 폴더 그대로)
                }
                catch { }
                finally
                {
                    if (item != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)item)[2])(item);
                    if (pidl != 0) CoTaskMemFree(pidl);
                    if (pf != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)pf)[2])(pf);
                    if (link != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)link)[2])(link);
                }
            }
        }
        finally { if (hrInit >= 0) CoUninitialize(); }
    }

    [DllImport("shell32.dll")] private static extern int SHCreateItemFromIDList(nint pidl, Guid* iid, nint* item);

    private static readonly Guid FOLDERID_AppsFolder = new("1E87508D-89C2-42F0-8A7E-645A0F50CA58");
    private static readonly Guid BHID_EnumItems = new("94F60519-2850-4924-AA5A-D15E84868039");
    private static readonly Guid IID_IEnumShellItems = new("70629033-E363-4A28-A567-0DB78006E6D7");
    private static readonly Guid IID_IShellItemG = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    /// <summary>셸의 앱 목록(shell:AppsFolder)에서 앱 ID 가 "패키지!앱" 모양인 것(스토어·패키지 앱)만 더한다. 상한·시간은 같이 센다. 다 읽었으면 true.</summary>
    private static bool ScanStoreApps(List<PickEntry> list, long end)
    {
        int hrInit = CoInitializeEx(0, 2);
        nint folder = 0, en = 0;
        try
        {
            Guid kf = FOLDERID_AppsFolder, iidItem = IID_IShellItemG, bhid = BHID_EnumItems, iidEnum = IID_IEnumShellItems;
            if (SHGetKnownFolderItem(&kf, 0, 0, &iidItem, &folder) < 0 || folder == 0) return false;
            if (((delegate* unmanaged[Stdcall]<nint, nint, Guid*, Guid*, nint*, int>)(*(nint**)folder)[3])(folder, 0, &bhid, &iidEnum, &en) < 0 || en == 0) return false;   // BindToHandler
            while (true)
            {
                if (list.Count >= PickScanMax || Environment.TickCount64 > end) return false;
                nint item = 0; uint got = 0;
                if (((delegate* unmanaged[Stdcall]<nint, uint, nint*, uint*, int>)(*(nint**)en)[3])(en, 1, &item, &got) != 0 || got == 0 || item == 0) return true;   // Next
                try
                {
                    string? parse = ItemName(item, 0x80018001 /* SIGDN_PARENTRELATIVEPARSING */);
                    if (parse is null || !LaunchStore.IsAumid(parse)) continue;   // 데스크톱 앱(바로 가기로 이미 나옴)·그 밖의 항목
                    string? name = ItemName(item, 0 /* SIGDN_NORMALDISPLAY */);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    list.Add(new PickEntry(name, Launcher.AppsFolderPrefix + parse, T.PickStoreGroup));
                }
                finally { ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)item)[2])(item); }
            }
        }
        catch { return false; }
        finally
        {
            if (en != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)en)[2])(en);
            if (folder != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)folder)[2])(folder);
            if (hrInit >= 0) CoUninitialize();
        }
    }

    private static string? ItemName(nint item, uint sigdn)
    {
        char* p = null;
        if (((delegate* unmanaged[Stdcall]<nint, uint, char**, int>)(*(nint**)item)[5])(item, sigdn, &p) < 0 || p == null) return null;   // GetDisplayName
        try { return new string(p); }
        finally { CoTaskMemFree((nint)p); }
    }

    [DllImport("shell32.dll")] private static extern int SHGetKnownFolderItem(Guid* kf, uint flags, nint token, Guid* iid, nint* item);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();

    private static List<PickEntry> Sorted(List<PickEntry> l)
    {
        l.Sort((a, b) => { int c = string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); return c != 0 ? c : string.Compare(a.Group, b.Group, StringComparison.CurrentCultureIgnoreCase); });
        return l;
    }

    private void BuildPickProgram()
    {
        int metalTop = MetalHeader(IdPkBack, T.PickTitle, help: false);   // 새 디자인 머리줄(‹ · 제목)
        if (_pickAll is null && _pickLoadNo == 0) StartPickLoad();
        // 2026-10-05 사용자: 검색 칸 옆 [찾아보기…]가 "검색하고 누르는 버튼"처럼 보였다 → 검색은 설치된 프로그램 묶음의 칸(폭 전체, 돋보기),
        // 파일에서 직접 고르기는 아래에 따로 고정한 한 줄(BuildPickRows 끝)
        int y = 58;   // 판 안쪽 위 여백
        _pickDialTop = metalTop;
        Header(T.PickInstalled, y); y += HeaderH;
        Field(IdPkSearch, Mx, y, Mw, FieldH, onCard: false, leftPad: 18);
        // \uB3CB\uBCF4\uAE30: \uCE78 \uD14C\uB450\uB9AC(\uC704\u00B7\uC544\uB798 1px)\uB97C \uB36E\uC9C0 \uC54A\uAC8C \uC548\uCABD\uC5D0, \uBC14\uD0D5\uC740 \uCE78 \uC548\uCABD \uC0C9(\uBC14\uD0D5 \uC704 \uCE78 = \uCE74\uB4DC \uC0C9)\uACFC \uAC19\uAC8C(2026-10-05 \uC0AC\uC6A9\uC790: \uD14C\uB450\uB9AC\uAC00 \uC798\uB9BC)
        Label("\uE721", Mx + 6, y + 3, 20, FieldH - 6, Theme.FontIcon, Theme.SecondaryText, true, Native.SS_CENTER);   // Search
        Native.SetCueBanner(C(IdPkSearch), T.PickSearch);
        Native.SetText(C(IdPkSearch), _pickFilter);
        y += FieldH + 10;
        // 목록에 없으면: 검색 칸 바로 아래, 작업 표시줄 묶음 위에 따로 한 줄(2026-10-05 사용자 지정 순서)
        ListRow(IdPkBrowse, T.PickBrowseTitle, T.PickBrowseSub, IcFolder, Row.First | Row.Last | Row.Chevron, y);
        Card(y, RowH);
        y += RowH + 12;
        _pickKeep = _page.Mark();   // 여기까지(뒤로·제목·검색 칸·찾아보기)는 검색어가 바뀌어도 그대로 — 검색 칸을 다시 만들면 한글 조합이 끊긴다
        _pickRowsY = y;
        BuildPickRows(y);
    }

    private Page.Marks _pickKeep;
    private int _pickRowsY, _pickDialTop;

    /// <summary>
    /// 검색 결과(검색 칸과 "파일에서 직접 고르기" 아래): 작업 표시줄에 고정된 앱을 먼저, 그다음 모든 앱.
    /// 검색어가 바뀌면 이 부분만 다시 만든다(RebuildPick). 이름으로 찾고, 스토어 앱은 앱 ID(영문, 예: Calculator)로도 찾는다.
    /// </summary>
    private void BuildPickRows(int y)
    {
        if (_pickAll is null) { _pickShown = new(); y = Footer(T.PickLoading, y); }   // 읽는 중: 파일에서 고르기는 그대로 쓸 수 있다
        else
        {
            bool Match(PickEntry e) => _pickFilter.Length == 0 || e.Name.Contains(_pickFilter, StringComparison.CurrentCultureIgnoreCase)
                || (e.Path.StartsWith(Launcher.AppsFolderPrefix, StringComparison.OrdinalIgnoreCase) && e.Path.Contains(_pickFilter, StringComparison.OrdinalIgnoreCase));
            var pinned = _pickAll.Where(e => e.Pinned && Match(e)).ToList();
            var rest = _pickAll.Where(e => !e.Pinned && Match(e)).ToList();
            var showP = pinned.Take(PickMax).ToList();
            var showA = rest.Take(PickMax - showP.Count).ToList();
            _pickShown = showP.Concat(showA).ToList();
            if (_pickShown.Count == 0) y = Footer(_pickPartial && _pickFilter.Length == 0 ? T.PickPartial : T.PickNone, y);
            else
            {
                if (showP.Count > 0) { Header(T.PickTaskbar, y); y += HeaderH; y = PickCard(showP, 0, y) + 8; }
                if (showA.Count > 0) { if (showP.Count > 0) { Header(T.PickAllApps, y); y += HeaderH; } y = PickCard(showA, showP.Count, y); }
                int total = pinned.Count + rest.Count;
                if (total > _pickShown.Count) y = Footer(T.PickMore(total), y);
                if (_pickPartial) y = Footer(T.PickPartial, y);
            }
        }
        if (_page.Metal)
        {
            // 새 디자인: 판은 검색 결과 끝까지(검색어가 바뀌어 행만 다시 만들 때도 다시 잰다)
            _page.Dials.Clear();
            y += DialPadBottom;
            _page.Dials.Add((DialX, _pickDialTop, DialW, y - _pickDialTop));
            _page.Height = y + 14;
        }
        else _page.Height = y + 20;
    }

    private int PickCard(List<PickEntry> list, int k0, int y)
    {
        int top = y;
        for (int k = 0; k < list.Count; k++)
        {
            uint flags = Row.Chevron | (k == 0 ? Row.First : 0u) | (k == list.Count - 1 ? Row.Last : 0u);
            ListRow(IdPkRow + k0 + k, list[k].Name, list[k].Group, IcApp, flags, y);
            if (k != list.Count - 1) Separator(y + RowH - 1);
            y += RowH;
        }
        Card(top, y - top);
        return y;
    }

    private void OnPickRow(int k)
    {
        if (k < 0 || k >= _pickShown.Count) return;
        OnProgramChosen(_pickShown[k].Path, _pickShown[k].Name);
    }

    private void OnPickBrowse()
    {
        string? path = PickPath(folder: false);
        if (path is not null) OnProgramChosen(path);
    }

    private void LeavePick()
    {
        if (_pickForRepick && _launchEdit is not null) ShowScreen(Screen.LaunchEdit);
        else ShowScreen(Screen.AddKind);
    }

    /// <summary>
    /// 검색어가 바뀌었거나 목록을 다 읽었다: 검색 칸은 그대로 두고 그 아래 행만 다시 만든다(2026-10-05 사용자: 한글 검색이 안 됨 —
    /// 화면 전체를 다시 만들며 검색 칸도 새로 만들어 입력기의 조합이 끊겼다). 커서·입력기 상태는 검색 칸에 그대로 남는다.
    /// </summary>
    private void RebuildPick()
    {
        if (_cur != Screen.PickProgram) return;
        if (C(IdPkSearch) == 0) { ShowScreen(Screen.PickProgram); return; }
        nint focus = Native.GetFocus();
        DropAfter(_pickKeep);
        BuildPickRows(_pickRowsY);
        KeepCardBorders();
        int avail = AvailableClientH(), height = _page.Height;
        _pageScroll = _pageScrollMax = _barShift = 0; _viewH = 0;
        if (height > avail) height = SetUpPageScroll(avail);
        ResizeClient(height);
        Native.RedrawWindow(_hwnd, 0, 0, Native.RDW_INVALIDATE | Native.RDW_ERASE | Native.RDW_ALLCHILDREN);
        if (focus != 0 && Native.IsWindow(focus)) _lastFocusSeen = focus;
    }

    private void OnAddFolder()
    {
        string? path = PickPath(folder: true);
        if (path is null) return;
        AddFolderPath(path);
    }

    private void AddFolderPath(string path)
    {
        var it = new LaunchItem { Id = LaunchStore.NewId(), Valid = true, Kind = "folder", Target = path, Name = FolderName(path) };
        if (LaunchStore.Check(it) is string bad) { Msg(bad, AppTitle, Native.MB_OK | Native.MB_ICONWARNING); return; }
        OpenLaunchEdit(-1, it);
    }

    /// <summary>끌어 놓기를 받을 수 있는 때: 잠금이 풀렸고, 목록·[+ 추가]·프로그램 고르기 화면이고, 떠 있는 확인 창이 없다.</summary>
    private bool CanDropLaunch => Unlocked && !_createMode && Dialog.OpenCount == 0 && _cur is Screen.List or Screen.AddKind or Screen.PickProgram;

    /// <summary>
    /// 끌어 놓은 것(2026-10-05 사용자): 첫 항목으로 [+ 추가]와 같은 길 — 폴더면 폴더 항목, exe·바로 가기·스토어 앱이면 프로그램 항목의 편집 화면.
    /// 여러 개면 첫 것만(안내). 경로가 없는 것(시작 메뉴의 일부 데스크톱 앱)은 목록에서 고르라고 안내한다.
    /// </summary>
    private void OnLaunchDrop(List<(string Path, string Name)> items)
    {
        if (items.Count == 0 || !CanDropLaunch) return;
        if (LaunchStore.ReadOnly) { Toast.Show(_hwnd, LaunchStore.ReadOnlyReason!, 6000, warn: true); return; }
        if (LaunchStore.Items.Count >= LaunchStore.Max) { Toast.Show(_hwnd, T.AddFull, 4000, warn: true); return; }
        Native.SetForegroundWindow(_hwnd);
        var (path, name) = items[0];
        if (items.Count > 1) Toast.Show(_hwnd, T.LaunchDropOne, 4000);
        _pickForRepick = false;
        if (path.Length == 0) { Msg(T.LaunchDropPick, AppTitle, Native.MB_OK | Native.MB_ICONINFORMATION); return; }
        if (!path.StartsWith(Launcher.AppsFolderPrefix, StringComparison.OrdinalIgnoreCase) && Directory.Exists(path)) { AddFolderPath(path); return; }
        OnProgramChosen(path, name.Length > 0 && path.StartsWith(Launcher.AppsFolderPrefix, StringComparison.OrdinalIgnoreCase) ? name : null);
    }

    private static string FolderName(string path)
    {
        string n = Path.GetFileName(path.TrimEnd('\\'));
        if (n.Length == 0) n = path;
        return n.Length > LaunchStore.NameMax ? n[..LaunchStore.NameMax] : n;
    }

    /// <summary>고른 프로그램(.exe/.lnk)이 지원 범위인지 보고 항목을 채운다. 문제가 있으면 안내 문구.</summary>
    private static string? PrepareProgram(string path, LaunchItem it, string? shownName = null)
    {
        if (path.StartsWith(Launcher.AppsFolderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // 스토어·패키지 앱(목록·끌어 놓기): 대상은 앱 ID, 이름은 목록에 보인 이름
            string aumid = path[Launcher.AppsFolderPrefix.Length..];
            string an = (shownName ?? "").Trim();
            if (an.Length == 0) an = aumid;
            it.Kind = "app"; it.Target = aumid; it.Args = ""; it.Dir = "";
            it.Name = an.Length > LaunchStore.NameMax ? an[..LaunchStore.NameMax] : an;
            if (!LaunchStore.IsAumid(aumid)) return T.LaunchRejectType;
            // 관리자 권한을 요구할 수 있는 패키지 앱(allowElevation)·확인하지 못한 앱은 넣지 않는다(Codex C28-3, 실행 직전에도 다시 본다)
            if (Launcher.CheckApp(aumid) is string why) return why == "admin" ? T.LaunchRejectAdmin : T.LaunchRejectUnknown;
            return LaunchStore.Check(it);
        }
        string ext = Path.GetExtension(path).ToLowerInvariant();
        string name = Path.GetFileNameWithoutExtension(path);
        if (name.Length > LaunchStore.NameMax) name = name[..LaunchStore.NameMax];
        it.Name = name; it.Target = path;
        if (ext == ".lnk")
        {
            var link = Launcher.ReadShortcut(path);
            if (link is null) return T.LaunchRejectType;
            if (link.Value.RunAs) return T.LaunchRejectRunAs;
            it.Kind = "lnk"; it.Args = link.Value.Args; it.Dir = link.Value.Dir;
            if (!link.Value.TargetIsFolder && Launcher.CheckExe(link.Value.Target) is string why) return RejectText(why, link.Value.Target);
        }
        else if (ext == ".exe")
        {
            it.Kind = "exe";
            if (Launcher.CheckExe(path) is string why) return RejectText(why, path);
        }
        else return T.LaunchRejectType;
        return LaunchStore.Check(it);
    }

    private static string RejectText(string why, string exe) => why switch
    {
        "admin" => Launcher.ManifestLevel(exe) == -1 ? T.LaunchRejectUnknown : T.LaunchRejectAdmin,
        "verify" => T.LaunchRejectUnknown,
        "type" => T.LaunchRejectType,
        "missing" => T.LaunchRejectType,
        _ => T.LaunchRejectNetwork,
    };

    // ------------------------------------------------------------------ 실행 항목 편집 화면

    private void OpenLaunchEdit(int index, LaunchItem? fresh = null)
    {
        LaunchItem src = fresh ?? LaunchStore.Items[index];
        _launchEdit = new LaunchItem
        {
            Id = src.Id, Kind = src.Kind, Name = src.Name, Target = src.Target, Args = src.Args, Dir = src.Dir, Mods = src.Mods, Vk = src.Vk, Icon = src.Icon, IconIndex = src.IconIndex, Browser = src.Browser, Color = src.Color,
            Valid = src.Valid, Problem = src.Problem, Fields = new Dictionary<string, string>(src.Fields, StringComparer.Ordinal),
        };
        _launchEditIndex = index;
        ResetWebDraft();
        // 녹음 칸에서 지금 단축키를 누르면 실행되지 않게 잠시 모두 푼다(비밀번호 편집과 같은 규칙)
        for (int i = 0; i < Config.SlotCount; i++) Native.UnregisterHotKey(_hwnd, i);
        UnregisterLaunchHotkeys();
        ShowScreen(Screen.LaunchEdit);
    }

    private void BuildLaunchEdit()
    {
        if (_launchEdit!.IsUrl) { BuildWebEdit(); return; }
        LaunchItem e = _launchEdit!;
        bool isNew = _launchEditIndex < 0;
        int metalTop = MetalHeader(IdLeBack, isNew ? (e.IsFolder ? T.AddFolder : T.AddProgram) : e.Name, help: false);   // 새 디자인 머리줄(‹ · 제목)

        int col = LabelCol(86, 150, T.LaunchName, T.LaunchTarget, T.EditCombo, T.LaunchArgs, T.LaunchDir, T.LaunchIcon);
        int labelX = Mx + Row.PadX, valueX = labelX + col, valueW = Mw - (valueX - Mx) - Row.PadX, labelW = col - 6;
        int y = 60, top = y;   // 판 안쪽 위 여백
        Label(T.LaunchName, labelX, y, labelW, RowH - 1, _font, Theme.ControlText, true);
        Field(IdLeName, valueX, y + (RowH - FieldH) / 2, valueW, FieldH);
        Separator(y + RowH - 1); y += RowH;
        Label(T.LaunchTarget, labelX, y, labelW, RowH - 1, _font, Theme.ControlText, true);
        const int repickW = 96;
        string shown = e.Kind == "app" ? T.PickStoreGroup + "  ·  " + e.Target : e.Target;   // 스토어 앱: 경로 대신 종류와 앱 ID
        nint tl = Label(shown, valueX, y, valueW - repickW - 8, RowH - 1, Theme.FontSmall, Theme.SecondaryText, true, Native.SS_LEFT | (e.Kind == "app" ? Native.SS_ENDELLIPSIS : Native.SS_PATHELLIPSIS), userText: true);
        if (tl != 0) Tip(tl, shown);
        Button(IdLeRepick, T.LaunchRepick, Btn.Bordered, valueX + valueW - repickW, y + (RowH - FieldH) / 2, repickW, FieldH, onCard: true);
        y += RowH;
        bool fromLink = e.Kind == "lnk" && (e.Args.Length > 0 || e.Dir.Length > 0);
        if (fromLink)
        {
            Separator(y - 1);
            if (e.Args.Length > 0) { Label(T.LaunchArgs, labelX, y, labelW, RowH - 1, _font, Theme.ControlText, true); Label(e.Args, valueX, y, valueW, RowH - 1, Theme.FontSmall, Theme.SecondaryText, true, Native.SS_LEFT | Native.SS_ENDELLIPSIS, userText: true); y += RowH; }
            if (e.Dir.Length > 0) { Label(T.LaunchDir, labelX, y, labelW, RowH - 1, _font, Theme.ControlText, true); Label(e.Dir, valueX, y, valueW, RowH - 1, Theme.FontSmall, Theme.SecondaryText, true, Native.SS_LEFT | Native.SS_PATHELLIPSIS, userText: true); y += RowH; }
        }
        // 아이콘(2026-10-04 사용자: 폴더도 아이콘을 따로 고를 수 있게). 미리보기 + [바꾸기] + [기본]
        Separator(y - 1);
        Label(T.LaunchIcon, labelX, y, labelW, RowH + 8 - 1, _font, Theme.ControlText, true);
        // 미리보기 아이콘도 백그라운드에서 읽는다(Codex R36-1). 같은 값이면 이미 읽은 것을 그대로
        string pk = IconKey(e.Target, e.IsFolder, e.Icon, e.IconIndex);
        if (pk != _previewKey)
        {
            if (_previewIcon != 0) { DestroyIcon(_previewIcon); _previewIcon = 0; }
            _previewKey = pk;
            IconLoader.Request(_hwnd, WM_ICON_READY, PreviewIconId, pk, e.Target, e.IsFolder, e.Icon, e.IconIndex, app: e.Kind == "app");
        }
        nint pv = Make(Tile.ClassName, "", 0, valueX, y + 4, 42, 42, IdLePreview);
        if (pv != 0) Tile.Set(pv, _previewIcon != 0 ? _previewIcon : Placeholder(e.IsFolder), false, false, "", onCard: true);   // 카드 위: 바탕을 카드색으로(옅은 네모 없음)
        Button(IdLeIcon, T.LaunchIconChange, Btn.Bordered, valueX + 52, y + (RowH + 8 - FieldH) / 2, 88, FieldH, onCard: true);
        nint rs = Button(IdLeIconReset, T.LaunchIconReset, Btn.Bordered, valueX + 52 + 96, y + (RowH + 8 - FieldH) / 2, 72, FieldH, onCard: true);
        if (rs != 0 && e.Icon.Length == 0) Native.EnableWindow(rs, false);
        y += RowH + 8;
        Card(top, y - top);
        if (fromLink) y = Footer(T.LaunchFromShortcut, y);
        y += 12;

        Header(T.EditHotkeySection, y); y += HeaderH;
        top = y;
        Label(T.EditCombo, labelX, y, labelW, RowH - 1, _font, Theme.ControlText, true);
        Make(HotkeyBox.ClassName, "", Native.WS_TABSTOP, valueX, y + (RowH - FieldH) / 2, valueW, FieldH, IdLeHotkey);
        y += RowH;
        Card(top, y - top);
        y += 12;

        if (!isNew)
        {
            // 순서: 띠에서 앞/뒤로. 누르면 바로 옮겨 저장한다(띠의 [편집] 중에는 칸을 끌어 놓아도 된다 — OnTileDrop).
            int n = LaunchStore.Items.Count;
            nint l = Button(IdLeLeft, T.LaunchMoveLeft, Btn.Bordered, Mx, y, 88, 32);
            nint r = Button(IdLeRight, T.LaunchMoveRight, Btn.Bordered, Mx + 96, y, 88, 32);
            if (l != 0 && NeighborInRow(_launchEditIndex, -1) < 0) Native.EnableWindow(l, false);
            if (r != 0 && NeighborInRow(_launchEditIndex, +1) < 0) Native.EnableWindow(r, false);
            y += 32 + 12;
        }

        MetalDial(metalTop, ref y);
        _page.BarTop = y;
        y += 14;
        if (!isNew) BarPill(IdLeDelete, T.LaunchDelete, Btn.DangerBordered, 18, y, BarPillW(T.LaunchDelete));
        int saveW = BarPillW(T.CommonSave, 72), cancelW = BarPillW(T.CommonCancel);
        BarPill(IdLeCancel, T.CommonCancel, Btn.PillMain, WinW - 22 - saveW - 8 - cancelW, y, cancelW);
        BarPill(IdLeSave, T.CommonSave, Btn.Prominent, WinW - 22 - saveW, y, saveW, isDefault: true);
        _page.DefaultButton = IdLeSave;
        _page.Height = y + MetalUi.PillMainH + 16;

        Native.SetText(C(IdLeName), e.Name);
        Native.SendMessageW(C(IdLeName), Native.EM_LIMITTEXT, LaunchStore.NameMax, 0);
        HotkeyBox.Set(C(IdLeHotkey), e.Mods, e.Vk);
    }

    private bool LaunchEditDirty()
    {
        if (_launchEdit is null) return false;
        if (_launchEditIndex < 0) return true;
        if (_launchEdit.IsUrl) return WebEditDirty();
        LaunchItem o = LaunchStore.Items[_launchEditIndex];
        var (m, v) = HotkeyBox.Get(C(IdLeHotkey));
        return Native.GetWindowText(C(IdLeName)).Trim() != o.Name || m != o.Mods || v != o.Vk || _launchEdit.Target != o.Target
            || _launchEdit.Icon != o.Icon || _launchEdit.IconIndex != o.IconIndex;
    }

    private void LeaveLaunchEdit(bool ask)
    {
        if (ask && _cur == Screen.LaunchEdit && LaunchEditDirty())
        {
            int r = Msg(T.LaunchLeaveConfirm, AppTitle, Native.MB_YESNO | Native.MB_ICONQUESTION);
            if (r != Native.IDYES || _cur != Screen.LaunchEdit) return;
        }
        _launchEdit = null; _launchEditIndex = -1;
        ResetWebDraft();
        ShowScreen(Screen.List);
    }

    private void SaveLaunchEdit()
    {
        LaunchItem e = _launchEdit!;
        string name = Native.GetWindowText(C(IdLeName)).Trim();
        var (mods, vk) = HotkeyBox.Get(C(IdLeHotkey));
        if (name.Length == 0) { Msg(T.LaunchNameEmpty, AppTitle, Native.MB_OK | Native.MB_ICONWARNING); return; }
        if (vk != 0 && mods == 0 && !Keys.AllowsBareHotkey(vk)) { Msg(T.EditNeedMod, AppTitle, Native.MB_OK | Native.MB_ICONWARNING); return; }
        if (vk != 0 && _cfg.ConfirmVk != 0 && mods == _cfg.ConfirmMods && vk == _cfg.ConfirmVk) { Msg(T.EditIsConfirmKey(HotkeyBox.Text(mods, vk)), AppTitle, Native.MB_OK | Native.MB_ICONWARNING); return; }
        if (vk != 0)
        {
            for (int j = 0; j < Config.SlotCount; j++)
            {
                Slot o = _cfg.Slots[j];
                if (SlotInUse(o) && o.HasHotkey && o.Mods == mods && o.Vk == vk) { Msg(T.LaunchHotkeyTaken(o.DisplayName(j)), AppTitle, Native.MB_OK | Native.MB_ICONWARNING); return; }
            }
            LaunchItem? self = _launchEditIndex >= 0 ? LaunchStore.Items[_launchEditIndex] : null;
            if (LaunchStore.HotkeyOwner(mods, vk, self) is LaunchItem other) { Msg(T.LaunchHotkeyTaken(other.Name), AppTitle, Native.MB_OK | Native.MB_ICONWARNING); return; }
        }
        string target = e.Target, browser = e.Browser;
        if (e.IsUrl && !ReadWebFields(out target, out browser)) return;   // 웹사이트: 주소·브라우저 칸(검사 실패는 안내)
        var draft = new LaunchItem { Id = e.Id, Kind = e.Kind, Name = name, Target = target, Args = e.Args, Dir = e.Dir, Mods = mods, Vk = vk, Icon = e.Icon, IconIndex = e.IconIndex, Browser = browser, Color = e.IsUrl ? WebColorField() : "", Fields = e.Fields };
        if (LaunchStore.Check(draft) is string bad) { Msg(bad, AppTitle, Native.MB_OK | Native.MB_ICONWARNING); return; }
        draft.Valid = true;
        // 파일에 들어간 뒤에만 목록을 바꾼다: 먼저 바꾸고 실패하면 되돌린다
        LaunchItem? before = _launchEditIndex >= 0 ? LaunchStore.Items[_launchEditIndex] : null;
        if (before is null) LaunchStore.Items.Add(draft); else LaunchStore.Items[_launchEditIndex] = draft;
        if (!LaunchStore.Save())
        {
            if (before is null) LaunchStore.Items.Remove(draft); else LaunchStore.Items[_launchEditIndex] = before;
            Msg(T.LaunchSaveFail, AppTitle, Native.MB_OK | Native.MB_ICONERROR);
            return;
        }
        if (before is not null && (before.Target != draft.Target || before.Kind != draft.Kind)) ForgetLaunch(draft.Id);   // 대상이 바뀌면 이전 요청·실행 요청 상태는 새 대상에 쓰지 않는다
        if (before is not null && (before.Target != draft.Target || before.Icon != draft.Icon || before.IconIndex != draft.IconIndex)) DropIcon(draft.Id);
        if (draft.IsUrl) AfterWebSave(before, draft);   // 사용자 이미지 저장·지우기, 파비콘 받기(백그라운드)
        _launchEdit = null; _launchEditIndex = -1;
        ResetWebDraft();
        ShowScreen(Screen.List);
        string report = RegisterLaunchHotkeys(silent: false);
        if (report.Length > 0) Dialog.Show(_hwnd, report, AppTitle, Native.MB_OK | Native.MB_ICONWARNING, share: "hotkeys");
        else Toast.Show(_hwnd, T.SetSaved);
    }

    private void DeleteLaunchEdit()
    {
        if (_launchEditIndex < 0) return;
        LaunchItem it = LaunchStore.Items[_launchEditIndex];
        if (Msg(T.LaunchDeleteConfirm(it.Name), AppTitle, Native.MB_YESNO | Native.MB_ICONQUESTION | Native.MB_DEFBUTTON2) != Native.IDYES || _cur != Screen.LaunchEdit) return;
        int at = _launchEditIndex;
        LaunchStore.Items.RemoveAt(at);
        if (!LaunchStore.Save())
        {
            LaunchStore.Items.Insert(at, it);
            Msg(T.LaunchSaveFail, AppTitle, Native.MB_OK | Native.MB_ICONERROR);
            return;
        }
        DropIcon(it.Id);
        ForgetLaunch(it.Id);
        if (it.IsUrl) ForgetWeb(it.Id);   // 앱이 만든 아이콘 파일·받는 중인 파비콘
        _launchEdit = null; _launchEditIndex = -1;
        ResetWebDraft();
        ShowScreen(Screen.List);
        RegisterLaunchHotkeys(silent: true);
    }

    /// <summary>같은 줄(프로그램/폴더/웹사이트) 안에서 앞·뒤 항목과 자리를 바꾼다.</summary>
    private int NeighborInRow(int at, int dir)
    {
        int row = LaunchStore.Items[at].Row;
        for (int j = at + dir; j >= 0 && j < LaunchStore.Items.Count; j += dir)
            if (LaunchStore.Items[j].Row == row) return j;
        return -1;
    }

    private void MoveLaunchEdit(int dir)
    {
        int at = _launchEditIndex;
        int to = at >= 0 ? NeighborInRow(at, dir) : -1;
        if (at < 0 || to < 0 || to >= LaunchStore.Items.Count) return;
        (LaunchStore.Items[at], LaunchStore.Items[to]) = (LaunchStore.Items[to], LaunchStore.Items[at]);
        if (!LaunchStore.Save())
        {
            (LaunchStore.Items[at], LaunchStore.Items[to]) = (LaunchStore.Items[to], LaunchStore.Items[at]);
            Msg(T.LaunchSaveFail, AppTitle, Native.MB_OK | Native.MB_ICONERROR);
            return;
        }
        // 이름·단축키에 바꾼 값이 있으면 그대로 두고 자리만 옮긴다
        KeepLaunchDraft();
        _launchEditIndex = to;
        ShowScreen(Screen.LaunchEdit);
        if (C(dir < 0 ? IdLeLeft : IdLeRight) is nint b and not 0 && Native.IsWindowEnabled(b)) Native.SetFocus(b);
    }

    /// <summary>
    /// 띠의 칸을 끌어 놓음([편집] 중): 같은 줄에 보이는 다른 칸들의 가운데와 놓은 자리를 견주어 그 줄 안에서 순서를 옮기고 바로 저장한다.
    /// 단축키 번호는 목록 순서를 따르므로 옮긴 뒤 다시 등록한다. 저장이 안 되면 되돌린다.
    /// </summary>
    private void OnTileDrop(nint tile, int dropX)
    {
        int at = Native.GetDlgCtrlID(tile) - IdTile;
        if (_cur != Screen.List || !_stripEdit || at < 0 || at >= LaunchStore.Items.Count) { RefreshList(); return; }
        int row = LaunchStore.Items[at].Row;
        string? beforeId = null, afterId = null;   // 놓은 자리 바로 뒤(앞) 칸
        for (int j = 0; j < LaunchStore.Items.Count; j++)
        {
            nint t = C(IdTile + j);
            if (j == at || LaunchStore.Items[j].Row != row || t == 0) continue;
            Native.GetWindowRect(t, out Native.RECT r);
            var c = new Native.POINT { x = (r.left + r.right) / 2, y = r.top };
            Native.ScreenToClient(_hwnd, ref c);
            if (c.x > dropX) { beforeId ??= LaunchStore.Items[j].Id; }
            else afterId = LaunchStore.Items[j].Id;
        }
        LaunchItem it = LaunchStore.Items[at];
        var order = new List<LaunchItem>(LaunchStore.Items);
        order.RemoveAt(at);
        int to = beforeId is not null ? order.FindIndex(x => x.Id == beforeId)
               : afterId is not null ? order.FindIndex(x => x.Id == afterId) + 1 : at;
        if (to < 0) to = at;
        order.Insert(to, it);
        if (to == at) { RefreshList(); FocusTile(it.Id); return; }
        var before = new List<LaunchItem>(LaunchStore.Items);
        LaunchStore.Items.Clear(); LaunchStore.Items.AddRange(order);
        if (!LaunchStore.Save())
        {
            LaunchStore.Items.Clear(); LaunchStore.Items.AddRange(before);
            RefreshList();
            Msg(T.LaunchSaveFail, AppTitle, Native.MB_OK | Native.MB_ICONERROR);
            return;
        }
        RegisterLaunchHotkeys(silent: true);
        RefreshList();
        FocusTile(it.Id);
    }

    private void FocusTile(string id)
    {
        int i = LaunchStore.Items.FindIndex(x => x.Id == id);
        if (i >= 0 && C(IdTile + i) is nint h and not 0) Native.SetFocus(h);
    }

    /// <summary>화면을 다시 만들기 전에 이름·단축키 칸의 값을 초안에 옮겨 둔다.</summary>
    private void KeepLaunchDraft()
    {
        if (_launchEdit is null || C(IdLeName) == 0) return;
        _launchEdit.Name = Native.GetWindowText(C(IdLeName));
        (_launchEdit.Mods, _launchEdit.Vk) = HotkeyBox.Get(C(IdLeHotkey));
        if (_launchEdit.IsUrl) KeepWebDraft();
    }

    private void RepickLaunchEdit()
    {
        LaunchItem e = _launchEdit!;
        KeepLaunchDraft();
        if (!e.IsFolder) { _pickForRepick = true; _pickFilter = ""; StartPickLoad(); ShowScreen(Screen.PickProgram); return; }
        string? path = PickPath(folder: true);
        if (path is null) return;
        var probe = new LaunchItem { Id = e.Id, Valid = true, Kind = "folder", Target = path, Name = e.Name.Length > 0 ? e.Name : FolderName(path) };
        if (LaunchStore.Check(probe) is string bad) { Msg(bad, AppTitle, Native.MB_OK | Native.MB_ICONWARNING); return; }
        e.Target = path;
        if (e.Name.Length == 0) e.Name = probe.Name;
        ShowScreen(Screen.LaunchEdit);
    }

    /// <summary>[바꾸기]: Windows 의 아이콘 고르기 창(PickIconDlg). 폴더는 Windows 아이콘 모음에서 시작한다.</summary>
    private void ChangeLaunchIcon()
    {
        if (_launchEdit!.IsUrl) { ChangeWebIcon(); return; }
        LaunchItem e = _launchEdit!;
        KeepLaunchDraft();
        string start = e.Icon.Length > 0 ? e.Icon
                     : e.IsFolder || e.Kind == "lnk" ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "imageres.dll")
                     : e.Target;
        char* buf = stackalloc char[1024];
        int n = Math.Min(start.Length, 1023);
        for (int i = 0; i < n; i++) buf[i] = start[i];
        buf[n] = '\0';
        int idx = e.Icon.Length > 0 ? e.IconIndex : 0;
        if (PickIconDlg(_hwnd, buf, 1024, ref idx) == 0) return;   // 취소
        string chosen = Environment.ExpandEnvironmentVariables(new string(buf));
        if (!LaunchStore.IsRootedPath(chosen) || chosen.Length > LaunchStore.TargetMax) return;
        e.Icon = chosen; e.IconIndex = idx;
        ShowScreen(Screen.LaunchEdit);
        if (C(IdLeIcon) is nint b and not 0) Native.SetFocus(b);
    }

    private void ResetLaunchIcon()
    {
        if (_launchEdit!.IsUrl) { ResetWebIcon(); return; }
        LaunchItem e = _launchEdit!;
        KeepLaunchDraft();
        e.Icon = ""; e.IconIndex = 0;
        ShowScreen(Screen.LaunchEdit);
        if (C(IdLeIcon) is nint b and not 0) Native.SetFocus(b);
    }

    // ------------------------------------------------------------------ 목록 위 아이콘 띠

    /// <summary>
    /// 목록 머리 아래의 아이콘 띠: 위 줄 프로그램, 아래 줄 폴더(2026-10-04 사용자). 항목이 있는 줄만 — 없는 줄은 높이를 차지하지 않는다.
    /// 두 줄을 카드 상자 하나로 묶고, 상자 오른쪽 위 모서리에 걸친 둥근 연필 버튼이 [편집](2026-10-05 사용자). 칸이 상자보다 많으면
    /// 줄마다 따로 가로로 스크롤한다(휠·가로 휠·아래 가는 막대·← →). 읽기 전용이면 이유 한 줄. 끝난 y.
    /// </summary>
    /// <summary>바로 실행 띠 상자의 왼쪽·폭: 새 디자인 목록은 아래 판과 같은 폭(DialX·DialW), 아니면 예전 카드 자리.</summary>
    private int StripX => _page.Metal ? DialX : Margin;
    private int StripW => _page.Metal ? DialW : CardW;

    private int BuildLaunchStrip(int y)
    {
        _stripRows.Clear(); _stripBadge = null;
        if (LaunchStore.ReadOnly)
        {
            int resetW = LaunchStore.CanReset ? Math.Min(160, LabelW(_font, T.LaunchReset) + 32) : 0;
            nint n = Make("STATIC", LaunchStore.ReadOnlyReason!, Native.SS_LEFT | Native.SS_NOPREFIX, StripX, y, StripW - (resetW > 0 ? resetW + 8 : 0), 50, IdStripNote, 0, Theme.FontSmall);
            if (n != 0) _staticStyle[n] = (Theme.BgBrush, Theme.SecondaryText);
            if (resetW > 0) Button(IdStripReset, T.LaunchReset, Btn.DangerBordered, StripX + StripW - resetW, y + 9, resetW, 32);
            return y + 56;
        }
        var progs = new List<int>(); var folders = new List<int>(); var webs = new List<int>();
        for (int i = 0; i < LaunchStore.Items.Count; i++) (LaunchStore.Items[i].Row switch { 1 => folders, 2 => webs, _ => progs }).Add(i);
        if (progs.Count == 0 && folders.Count == 0 && webs.Count == 0) { _stripEdit = false; return y; }
        // 웹사이트 줄은 이름을 늘 보인다(글자 아이콘만으로는 구분이 어렵다 — 이름 표시 설정은 프로그램·폴더 줄)
        bool progNames = (_cfg.LaunchNames & 1) != 0, folderNames = (_cfg.LaunchNames & 2) != 0, webNames = true;
        // 줄의 칸 폭은 같게: 한 줄만 이름을 보여도 모든 줄이 넓은 칸이어야 위아래 아이콘이 같은 열에 선다(2026-10-05 사용자 — 52 와 68 이 섞여 어긋났다)
        int tw = (progs.Count > 0 && progNames) || (folders.Count > 0 && folderNames) || (webs.Count > 0 && webNames) ? TileW : IconTileW;
        int top = y + BadgeOut;                                   // 연필 버튼이 상자 위로 BadgeOut 만큼 나온다
        int right = StripX + StripW;
        int viewL = StripX + StripPadX, viewR = right - StripPadX;   // 좌우 여백을 작게(0.3.25 사용자)
        // 연필 버튼은 4분의 3이 상자 안(0.3.26 사용자: 버튼의 오른쪽 위 75% 지점이 모서리) — 첫 줄은 그 버튼 왼쪽 앞에서 칸이 끝난다(아이콘을 덮지 않게)
        int badgeL = right - BadgeD + BadgeOut;
        var firstRow = progs.Count > 0 ? progs : folders.Count > 0 ? folders : webs;
        int RowW(List<int> r) => r.Count * (tw + TileGap) - TileGap;
        // 넘치는 줄이 있으면 양 끝에 ‹ › 자리를 따로 비운다 — 꺾쇠가 아이콘을 가리지 않게(2026-10-05 사용자). 두 줄 모두 같은 자리를 비워 열이 맞게
        int widest = Math.Max(progs.Count, Math.Max(folders.Count, webs.Count)) * (tw + TileGap) - TileGap;
        // 넘치면 꺾쇠를 상자 양 끝에 바짝(테두리 안 1px), 칸은 그 안쪽부터(0.3.25 사용자)
        if (widest > viewR - viewL || RowW(firstRow) > badgeL - 1 - viewL) { viewL = StripX + 1 + ChevW + 2; viewR = right - 1 - ChevW - 2; }
        int topR = Math.Min(viewR, badgeL - 1);   // 첫 줄의 오른쪽 끝
        int ry = top + StripPad;
        if (progs.Count > 0) ry = StripRow(progs, 0, ry, progNames, tw, viewL, topR) + 2;
        if (folders.Count > 0) ry = StripRow(folders, 1, ry, folderNames, tw, viewL, progs.Count > 0 ? viewR : topR) + 2;
        if (webs.Count > 0) ry = StripRow(webs, 2, ry, webNames, tw, viewL, progs.Count > 0 || folders.Count > 0 ? viewR : topR) + 2;
        int bottom = ry - 2 + StripPad;
        _page.Cards.Add((StripX, top, StripW, bottom - top));
        nint b = Button(IdStripEdit, _stripEdit ? T.LaunchEditDone : T.LaunchEdit, Btn.Badge | (_stripEdit ? Btn.Expanded : 0), right - BadgeD + BadgeOut, y, BadgeD, BadgeD);
        if (b != 0)
        {
            int d = Scale(BadgeD);
            Native.SetWindowRgn(b, Fx.CreateRoundRectRgn(0, 0, d + 1, d + 1, d, d), true);   // 동그라미 밖은 부모(카드 모서리)가 보이게
            Native.SetWindowPos(b, 0 /* HWND_TOP */, 0, 0, 0, 0, Native.SWP_NOMOVE_ | Native.SWP_NOSIZE_ | Native.SWP_NOACTIVATE);
            _stripBadge = (right - BadgeD + BadgeOut, y, BadgeD);
        }
        y = bottom + 6;
        if (_stripEdit) y = Footer(T.LaunchEditHint, y - 2) + 4;
        return y;
    }

    // StripPad 위아래, StripPadX 좌우(0.3.25: 반으로). 연필 버튼은 상자 위로 BadgeOut 만큼 나와 아래 끝이 첫 줄 칸의 위 여백(아이콘 위 6px)까지만 온다
    private const int StripPad = 8, StripPadX = 4, BadgeD = 28, BadgeOut = 7, ChevW = 10;   // BadgeOut = 버튼의 4분의 1만 상자 밖

    /// <summary>띠의 한 줄(가로 스크롤 상태). 좌표는 논리 px, Scroll 은 _stripScroll[Row].</summary>
    private sealed class StripRowInfo
    {
        public int Row, Y, H, TileW, ViewL, ViewR, Step, ContentW;
        public readonly List<nint> Tiles = new();
        public nint Prev, Next;   // 줄 양 끝의 ‹ › (넘치는 줄만)
        public int Max => Math.Max(0, ContentW - (ViewR - ViewL));
    }
    private readonly List<StripRowInfo> _stripRows = new();
    private readonly int[] _stripScroll = new int[3];   // 줄별 가로 스크롤(논리 px): 0 = 프로그램, 1 = 폴더, 2 = 웹사이트. 목록을 다시 그려도 유지
    private (int X, int Y, int D)? _stripBadge;           // 연필 버튼 자리(부모가 매끈한 가장자리를 그린다)
    private int _stripDrag = -1, _stripDragOff;            // 가는 막대 손잡이를 끄는 줄과 손잡이 안 누른 자리

    /// <summary>띠의 한 줄: 모든 칸을 만들고, 상자 안(viewL–viewR)만 보이게 잘라 스크롤 자리에 놓는다. 끝난 y(넘치면 아래 가는 막대 자리 포함).</summary>
    private int StripRow(List<int> idx, int row, int y, bool names, int tw, int viewL, int viewR)
    {
        int th = names ? TileH : IconTileH;
        var info = new StripRowInfo { Row = row, Y = y, H = th, TileW = tw, ViewL = viewL, ViewR = viewR, Step = tw + TileGap, ContentW = idx.Count * (tw + TileGap) - TileGap };
        for (int k = 0; k < idx.Count; k++)
        {
            int i = idx[k];
            LaunchItem it = LaunchStore.Items[i];
            string label = it.Valid ? it.Name + (it.HasHotkey ? "  ·  " + HotkeyBox.Text(it.Mods, it.Vk) + (_launchHotkeyFailed[i] ? "  ·  " + T.ListSubRegFailed : "") : "")
                                    : T.LaunchUnusable(it.Name.Length > 0 ? it.Name : "?", it.Problem);
            nint t = Make(Tile.ClassName, label, Native.WS_TABSTOP, viewL + k * info.Step, y, tw, th, IdTile + i);
            if (t != 0)
            {
                Tile.Set(t, it.Valid ? IconOf(it) : 0, !it.Valid, _stripEdit, names ? it.Name : "", onCard: true);   // 이름을 끈 줄은 툴팁·화면 읽기로만
                Tip(t, label);
            }
            info.Tiles.Add(t);
        }
        if (info.Max > 0)
        {
            // 줄 양 끝 ‹ ›(2026-10-05 사용자: 가는 막대를 끌기는 어렵다 — 넘길 쪽에 꺾쇠, 누르면 한 화면씩). 칸 위에 겹쳐 놓고, 넘길 수 없는 쪽은 숨긴다
            // 줄 높이의 좁은 띠(칸에 마우스를 올린 것 같은 옅은 바탕) + 작은 꺾쇠만(2026-10-05 사용자 그림)
            info.Prev = Button(row switch { 0 => IdStripPrev, 1 => IdStripPrev2, _ => IdStripPrev3 }, "\uE76B", Btn.Nudge, Margin + 1, y, ChevW, th, onCard: true);                  // 상자 왼쪽 끝에 바짝
            info.Next = Button(row switch { 0 => IdStripNext, 1 => IdStripNext2, _ => IdStripNext3 }, "\uE76C", Btn.Nudge, Margin + CardW - 1 - ChevW, y, ChevW, th, onCard: true);   // 상자 오른쪽 끝에 바짝
            foreach (nint b in new[] { info.Prev, info.Next })
                if (b != 0) { Native.SetWindowPos(b, 0 /* HWND_TOP */, 0, 0, 0, 0, Native.SWP_NOMOVE_ | Native.SWP_NOSIZE_ | Native.SWP_NOACTIVATE); Tip(b, b == info.Prev ? T.LaunchPrev : T.LaunchNext); }
        }
        _stripRows.Add(info);
        ApplyStripScroll(info);
        return y + th + (info.Max > 0 ? 7 : 0);
    }

    /// <summary>줄의 칸들을 스크롤 자리로 옮기고 상자 밖으로 나간 부분은 창 영역으로 자른다. 아래 가는 막대도 다시 그린다.</summary>
    private void ApplyStripScroll(StripRowInfo info)
    {
        int s = _stripScroll[info.Row] = Math.Clamp(_stripScroll[info.Row], 0, info.Max);
        int vl = Scale(info.ViewL), vr = Scale(info.ViewR), w = Scale(info.TileW), h = Scale(info.H), py = Yp(info.Y);
        for (int k = 0; k < info.Tiles.Count; k++)
        {
            nint t = info.Tiles[k];
            if (t == 0) continue;
            int px = Scale(info.ViewL + k * info.Step - s);
            int shown = CtlAcc.Shown(t, out _);
            Native.SetWindowPos(t, 0, px, py, 0, 0, Native.SWP_NOSIZE_ | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            int l = Math.Max(0, vl - px), r = Math.Min(w, vr - px);
            // 다 보이면 자르지 않는다. 다 가려져도 창은 보이는 상태라 Tab·← → 로 닿고, 닿으면 StripEnsureVisible 이 그 칸으로 스크롤한다
            // (화면 읽기에는 다 가려진 칸을 OFFSCREEN 으로, 일부만 보이는 칸은 보이는 부분을 위치로 알린다 — CtlAcc)
            if (l <= 0 && r >= w) Native.SetWindowRgn(t, 0, true);
            else Native.SetWindowRgn(t, Native.CreateRectRgn(Math.Min(l, w), 0, Math.Max(Math.Min(l, w), r), h), true);
            CtlAcc.Clipped(t, shown);
        }
        if (info.Max > 0)
        {
            var rc = new Native.RECT { left = vl - Scale(2), top = Yp(info.Y + info.H), right = vr + Scale(2), bottom = Yp(info.Y + info.H + 7) };
            Native.InvalidateRect(_hwnd, (nint)(&rc), true);
            ShowChevron(info.Prev, s > 0, info.Next);
            ShowChevron(info.Next, s < info.Max, info.Prev);
        }
    }

    /// <summary>꺾쇠를 보이거나 숨긴다. 숨기는 꺾쇠에 포커스가 있었으면 반대쪽 꺾쇠(보이면)로 옮긴다(포커스가 사라지지 않게).</summary>
    private static void ShowChevron(nint b, bool show, nint other)
    {
        // 창을 다시 만드는 동안에는 본창이 아직 안 보여 IsWindowVisible 이 늘 false — 버튼 자신의 보임 표시(WS_VISIBLE)로 판단한다(0.3.22: 처음부터 ‹ 가 보이던 결함)
        if (b == 0 || ((Ctl.Style(b) & Native.WS_VISIBLE) != 0) == show) return;
        bool hadFocus = !show && Native.GetFocus() == b;
        Native.ShowWindow(b, show ? 8 /* SW_SHOWNA */ : Native.SW_HIDE);
        if (hadFocus && other != 0 && (Ctl.Style(other) & Native.WS_VISIBLE) != 0) Native.SetFocus(other);
    }

    /// <summary>‹ › 누름: 그 줄을 한 화면 폭(칸 하나는 겹치게)만큼 넘긴다.</summary>
    private void StripPage(int row, int dir)
    {
        foreach (var info in _stripRows)
        {
            if (info.Row != row) continue;
            _stripScroll[row] += dir * Math.Max(info.Step, info.ViewR - info.ViewL - info.Step);
            ApplyStripScroll(info);
            return;
        }
    }

    /// <summary>줄 아래 가는 막대(물리 px). 넘치지 않으면 false.</summary>
    private bool StripBar(StripRowInfo info, out Native.RECT track, out Native.RECT thumb)
    {
        track = thumb = default;
        if (info.Max <= 0) return false;
        int x0 = Scale(info.ViewL), x1 = Scale(info.ViewR), y0 = Yp(info.Y + info.H + 2), hh = Math.Max(2, Scale(3));
        int view = x1 - x0, content = Scale(info.ContentW);
        int tw = Math.Max(Scale(24), (int)((long)view * view / Math.Max(1, content)));
        int tx = x0 + (int)((long)(view - tw) * _stripScroll[info.Row] / info.Max);
        track = new Native.RECT { left = x0, top = y0, right = x1, bottom = y0 + hh };
        thumb = new Native.RECT { left = tx, top = y0, right = tx + tw, bottom = y0 + hh };
        return true;
    }

    /// <summary>휠: 커서가 넘치는 줄 위에 있으면 그 줄을 칸 단위로 가로 스크롤하고 true. steps 양수 = 오른쪽(뒤 칸)으로.</summary>
    private bool StripWheel(int screenX, int screenY, int steps)
    {
        if (_cur != Screen.List || _stripRows.Count == 0) return false;
        var p = new Native.POINT { x = screenX, y = screenY };
        Native.ScreenToClient(_hwnd, ref p);
        foreach (var info in _stripRows)
        {
            if (info.Max <= 0) continue;
            if (p.x < Scale(info.ViewL - 8) || p.x > Scale(info.ViewR + 8) || p.y < Yp(info.Y) || p.y > Yp(info.Y + info.H + 7)) continue;
            _stripScroll[info.Row] += steps * info.Step;
            ApplyStripScroll(info);
            return true;
        }
        return false;
    }

    /// <summary>가는 막대를 누름: 손잡이면 끌기 시작, 빈 곳이면 한 화면 폭만큼. 처리했으면 true.</summary>
    private bool StripBarDown(int px, int py)
    {
        if (_cur != Screen.List) return false;
        foreach (var info in _stripRows)
        {
            if (!StripBar(info, out Native.RECT tr, out Native.RECT th)) continue;
            if (px < tr.left || px > tr.right || py < tr.top - Scale(4) || py > tr.bottom + Scale(4)) continue;
            if (px >= th.left && px <= th.right) { _stripDrag = info.Row; _stripDragOff = px - th.left; Native.SetCapture(_hwnd); }
            else { _stripScroll[info.Row] += (px < th.left ? -1 : 1) * (info.ViewR - info.ViewL - info.Step); ApplyStripScroll(info); }
            return true;
        }
        return false;
    }

    /// <summary>손잡이를 끄는 중이면 그 줄을 마우스 자리로 스크롤하고 true.</summary>
    private bool StripBarMove(int px)
    {
        if (_stripDrag < 0) return false;
        foreach (var info in _stripRows)
        {
            if (info.Row != _stripDrag || !StripBar(info, out Native.RECT tr, out Native.RECT th)) continue;
            int room = (tr.right - tr.left) - (th.right - th.left);
            if (room > 0) { _stripScroll[info.Row] = (int)((long)(px - _stripDragOff - tr.left) * info.Max / room); ApplyStripScroll(info); }
        }
        return true;
    }

    /// <summary>포커스가 간 칸이 상자 밖(스크롤로 가려짐)이면 그 칸이 보이게 줄을 스크롤한다(Tab·← →).</summary>
    private void StripEnsureVisible(nint f)
    {
        if (f == 0 || _cur != Screen.List) return;
        foreach (var info in _stripRows)
        {
            int k = info.Tiles.IndexOf(f);
            if (k < 0) continue;
            int lx = info.ViewL + k * info.Step - _stripScroll[info.Row];
            if (lx < info.ViewL) _stripScroll[info.Row] -= info.ViewL - lx;
            else if (lx + info.TileW > info.ViewR) _stripScroll[info.Row] += lx + info.TileW - info.ViewR;
            else return;
            ApplyStripScroll(info);
            return;
        }
    }

    private void OnTile(int index)
    {
        if (index < 0 || index >= LaunchStore.Items.Count) return;
        if (_stripEdit) { OpenLaunchEdit(index); return; }
        RunLaunch(index);
    }

    /// <summary>띠의 칸에서 ← →: 옆 칸으로. 보이는 칸 밖이면 띠를 한 칸 옮겨 다시 그린 뒤 그 칸에.</summary>
    private void OnTileNav(nint from, int dir)
    {
        int at = Native.GetDlgCtrlID(from) - IdTile;
        if (at < 0 || at >= LaunchStore.Items.Count) return;
        int i = NeighborInRow(at, dir);
        if (i < 0) return;
        if (C(IdTile + i) is nint h and not 0) { Native.SetFocus(h); StripEnsureVisible(h); }
    }

    /// <summary>띠의 아이콘. 읽은 것이 있으면 그것, 아직이면 기본 아이콘을 보이고 백그라운드에 읽기를 맡긴다(Codex R36-1 — UI 에서 디스크·네트워크를 보지 않는다).</summary>
    private nint IconOf(LaunchItem it)
    {
        if (it.IsUrl) return WebIconOf(it);
        string key = IconKey(it.Target, it.IsFolder, it.Icon, it.IconIndex);
        if (_launchIconKey.TryGetValue(it.Id, out string? k) && k == key)
            return _launchIcons.TryGetValue(it.Id, out nint ic) && ic != 0 ? ic : Placeholder(it.IsFolder);
        DropIcon(it.Id);
        _launchIconKey[it.Id] = key;
        IconLoader.Request(_hwnd, WM_ICON_READY, it.Id, key, it.Target, it.IsFolder, it.Icon, it.IconIndex, app: it.Kind == "app");
        return Placeholder(it.IsFolder);
    }

    private static string IconKey(string target, bool folder, string icon, int index) => target + "\n" + (folder ? "d" : "f") + "\n" + icon + "\n" + index;

    /// <summary>IconLoader 가 끝낸 것을 받는다: 아직 그 값을 원하면 쓰고, 아니면(세대가 바뀜·지움) 아이콘을 해제한다.</summary>
    private void OnIconReady()
    {
        foreach (var r in IconLoader.TakeDone())
        {
            if (r.Id == PreviewIconId)
            {
                if (r.Key == _previewKey && _previewIcon == 0 && r.Result != 0)
                {
                    _previewIcon = r.Result;
                    if (_cur == Screen.LaunchEdit && C(IdLePreview) is nint pv and not 0) Tile.SetIcon(pv, _previewIcon);
                }
                else if (r.Result != 0) DestroyIcon(r.Result);
                continue;
            }
            bool wanted = _launchIconKey.TryGetValue(r.Id, out string? k) && k == r.Key && !_launchIcons.ContainsKey(r.Id);
            if (r.Result != 0 && wanted)
            {
                _launchIcons[r.Id] = r.Result;
                int i = LaunchStore.Items.FindIndex(x => x.Id == r.Id);
                if (i >= 0 && _cur == Screen.List && LaunchStore.Items[i].Valid && C(IdTile + i) is nint t and not 0) Tile.SetIcon(t, r.Result);
            }
            else if (r.Result != 0) DestroyIcon(r.Result);
            else if (wanted && !r.Abandoned && _iconRetry.GetValueOrDefault(r.Id) < 2)
            {
                // 못 읽음(늦게 올 결과도 아님): 다음에 띠를 그릴 때 다시 읽는다 — 잠깐 실패한 아이콘이 끝까지 기본 아이콘으로 남지 않게(두 번까지)
                _iconRetry[r.Id] = _iconRetry.GetValueOrDefault(r.Id) + 1;
                _launchIconKey.Remove(r.Id);
            }
        }
    }

    private int LoadedIconCount { get { int n = 0; foreach (nint v in _launchIcons.Values) if (v != 0) n++; return n; } }
    private readonly Dictionary<string, int> _iconRetry = new();   // 항목별 다시 읽은 횟수(못 읽은 아이콘)

    private void DropIcon(string id)
    {
        _launchIconKey.Remove(id);
        if (_launchIcons.Remove(id, out nint ic) && ic != 0) DestroyIcon(ic);
    }

    /// <summary>기본 아이콘(폴더/프로그램). 파일 속성만으로 만들어 디스크를 보지 않는다.</summary>
    private nint Placeholder(bool folder)
    {
        ref nint slot = ref folder ? ref _phFolder : ref _phProgram;
        if (slot != 0) return slot;
        // Windows 기본 그림(SIID_FOLDER / SIID_APPLICATION): 파일을 보지 않고 늘 있다(2026-10-05 사용자: 처음에 아이콘이 비어 보임)
        var ssi = new SHSTOCKICONINFO { cbSize = (uint)sizeof(SHSTOCKICONINFO) };
        if (SHGetStockIconInfo(folder ? 3u : 2u, 0x100 /* SHGSI_ICON */, &ssi) >= 0 && ssi.hIcon != 0) return slot = ssi.hIcon;
        var sfi = new SHFILEINFOW();
        fixed (char* p = folder ? "folder" : "program.exe")
            SHGetFileInfoW(p, folder ? 0x10u : 0x80u, ref sfi, (uint)sizeof(SHFILEINFOW), 0x100 /* SHGFI_ICON */ | 0x10 /* USEFILEATTRIBUTES */);
        return slot = sfi.hIcon;
    }

    // ------------------------------------------------------------------ 파일·폴더 고르기 (IFileOpenDialog)

    private static readonly Guid CLSID_FileOpenDialog = new("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7");
    private static readonly Guid IID_IFileOpenDialog = new("d57c7288-d4ad-4768-be02-9d969532d960");
    private bool _comInit;

    /// <summary>프로그램(.exe/.lnk — 바로 가기 자체를 돌려받는다) 또는 폴더를 고른다. 취소·실패면 null.</summary>
    private string? PickPath(bool folder)
    {
        if (!_comInit) { int hr0 = CoInitializeEx(0, 2 /* APARTMENTTHREADED */); _comInit = hr0 >= 0; }
        nint dlg = 0, item = 0;
        Guid clsid = CLSID_FileOpenDialog, iid = IID_IFileOpenDialog;
        try
        {
            if (CoCreateInstance(ref clsid, 0, 1, ref iid, out dlg) < 0 || dlg == 0) return null;
            nint* vt = *(nint**)dlg;
            uint opts = 0;
            ((delegate* unmanaged[Stdcall]<nint, uint*, int>)vt[10])(dlg, &opts);   // GetOptions
            opts |= 0x40 /* FORCEFILESYSTEM */ | 0x1000 /* FILEMUSTEXIST */ | 0x800 /* PATHMUSTEXIST */;
            if (folder) opts |= 0x20 /* PICKFOLDERS */; else opts |= 0x100000 /* NODEREFERENCELINKS: .lnk 자체를 받는다 */;
            ((delegate* unmanaged[Stdcall]<nint, uint, int>)vt[9])(dlg, opts);   // SetOptions
            fixed (char* title = folder ? T.LaunchPickFolder : T.LaunchPickProgram) ((delegate* unmanaged[Stdcall]<nint, char*, int>)vt[17])(dlg, title);   // SetTitle
            if (!folder)
            {
                fixed (char* name = T.LaunchFilterPrograms) fixed (char* spec = "*.exe;*.lnk")
                {
                    var f = new COMDLG_FILTERSPEC { pszName = name, pszSpec = spec };
                    ((delegate* unmanaged[Stdcall]<nint, uint, COMDLG_FILTERSPEC*, int>)vt[4])(dlg, 1, &f);   // SetFileTypes
                }
            }
            int hr = ((delegate* unmanaged[Stdcall]<nint, nint, int>)vt[3])(dlg, _hwnd);   // Show
            if (hr < 0) return null;   // 취소 포함
            if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)vt[20])(dlg, &item) < 0 || item == 0) return null;   // GetResult
            char* path = null;
            if (((delegate* unmanaged[Stdcall]<nint, uint, char**, int>)(*(nint**)item)[5])(item, 0x80058000 /* SIGDN_FILESYSPATH */, &path) < 0 || path == null) return null;
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

    [StructLayout(LayoutKind.Sequential)] private struct COMDLG_FILTERSPEC { public char* pszName, pszSpec; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFOW { public nint hIcon; public int iIcon; public uint dwAttributes; public fixed char szDisplayName[260]; public fixed char szTypeName[80]; }
    [DllImport("shell32.dll")] private static extern nint SHGetFileInfoW(char* path, uint attr, ref SHFILEINFOW sfi, uint size, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint h);
    [DllImport("shell32.dll")] private static extern uint ExtractIconExW(char* file, int index, nint* large, nint* small, uint n);
    [DllImport("shell32.dll", EntryPoint = "PickIconDlg")] private static extern int PickIconDlg(nint hwnd, char* path, uint cch, ref int index);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint coinit);
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, nint outer, uint ctx, ref Guid iid, out nint obj);
    [DllImport("ole32.dll")] private static extern void CoTaskMemFree(nint p);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct SHSTOCKICONINFO { public uint cbSize; public nint hIcon; public int iSysImageIndex, iIcon; public fixed char szPath[260]; }
    [DllImport("shell32.dll")] private static extern int SHGetStockIconInfo(uint siid, uint flags, SHSTOCKICONINFO* info);
}

/// <summary>
/// 아이콘 띠의 칸: 아이콘 하나(32px), 마우스를 올리면 옅은 채움, 키보드 포커스면 강조 테두리. 창 글자 = 이름·단축키(툴팁·화면 읽기).
/// Enter/Space·클릭 = BN_CLICKED, ← → = 부모에 WM_TILE_NAV. 사용할 수 없는 항목은 경고 표시, [편집] 모드면 연필 표시.
/// </summary>
internal static unsafe class Tile
{
    public const string ClassName = "OneKeyTile";
    private sealed class Data
    {
        public nint Icon; public bool Bad, Edit, OnCard; public string Name = "";
        // 끌어 놓기([편집] 중에만, 2026-10-05 사용자): 누른 화면 좌표, 칸의 원래 자리(본창 좌표), 끄는 중인지
        public bool Armed, Dragging; public int DownX, DownY, OrigLeft, OrigTop;
    }
    private static readonly Dictionary<nint, Data> _data = new();

    public static void Register(nint hInst) => Ctl.RegisterClass(hInst, ClassName, &WndProc, 16);

    public static void Set(nint hwnd, nint icon, bool bad, bool edit, string name, bool onCard = false)
    {
        _data[hwnd] = new Data { Icon = icon, Bad = bad, Edit = edit, Name = name, OnCard = onCard };
        Native.InvalidateRect(hwnd, 0, false);
    }

    /// <summary>아이콘만 바꾼다(백그라운드에서 읽은 아이콘이 도착했을 때). 아이콘의 주인은 부르는 쪽.</summary>
    public static void SetIcon(nint hwnd, nint icon)
    {
        if (!_data.TryGetValue(hwnd, out Data? d) || d.Bad) return;
        d.Icon = icon;
        Native.InvalidateRect(hwnd, 0, false);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            if (msg == Native.WM_GETDLGCODE) return (nint)(Native.DLGC_BUTTON | 0x0001 /* DLGC_WANTARROWS */ | (Native.GetFocus() == hwnd ? Native.DLGC_DEFPUSHBUTTON : 0));
            if (msg == Native.WM_NCDESTROY) _data.Remove(hwnd);
            if (Drag(hwnd, msg, wParam, lParam, out nint dr)) return dr;
            if (msg == Native.WM_KEYDOWN && (wParam == 0x25 || wParam == 0x27))   // ← →
            {
                Ctl.ShowFocus = true;
                Native.PostMessageW(Native.GetParent(hwnd), Native.WM_APP + 34, wParam == 0x25 ? -1 : 1, hwnd);
                return 0;
            }
            if (Ctl.Common(hwnd, msg, wParam, lParam, Ctl.NotifyParent, out nint r)) return r;
            if (msg == Native.WM_PAINT) { Paint(hwnd); return 0; }
        }
        catch { }
        return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    /// <summary>
    /// [편집] 중 칸 끌기: 누른 채 6px 넘게 움직이면 그 칸이 마우스를 따라 가로로 움직이고(맨 위로), 놓으면 본창에 WM_TILE_DROP 을 보낸다.
    /// 끌었으면 클릭(편집 열기)은 하지 않는다. Esc·포커스를 잃어 끌기가 끊기면 제자리로 돌아간다. 처리했으면 true.
    /// </summary>
    private static bool Drag(nint hwnd, uint msg, nint wParam, nint lParam, out nint result)
    {
        result = 0;
        if (!_data.TryGetValue(hwnd, out Data? d) || !d.Edit) return false;
        switch (msg)
        {
            case Native.WM_LBUTTONDOWN:
                Native.POINT down = ScreenPt(hwnd, lParam);
                d.Armed = true; d.Dragging = false; d.DownX = down.x; d.DownY = down.y;
                return false;   // 누름 표시·캡처는 공통 처리
            case Native.WM_MOUSEMOVE:
            {
                if (!d.Armed || Native.GetCapture() != hwnd) return false;
                Native.POINT pt = ScreenPt(hwnd, lParam);   // 메시지의 좌표(칸이 움직여도 그 순간 칸 기준) → 화면 좌표
                if (!d.Dragging)
                {
                    int slop = Ctl.S(hwnd, 6);
                    if (Math.Abs(pt.x - d.DownX) <= slop && Math.Abs(pt.y - d.DownY) <= slop) return false;
                    d.Dragging = true;
                    Native.GetWindowRect(hwnd, out Native.RECT wr);
                    var tl = new Native.POINT { x = wr.left, y = wr.top };
                    Native.ScreenToClient(Native.GetParent(hwnd), ref tl);
                    d.OrigLeft = tl.x; d.OrigTop = tl.y;
                    Native.SetWindowRgn(hwnd, 0, true);   // 상자 가장자리에서 잘려 있던 칸도 끄는 동안은 다 보이게
                    Native.SetWindowPos(hwnd, 0 /* HWND_TOP */, 0, 0, 0, 0, Native.SWP_NOMOVE_ | Native.SWP_NOSIZE_ | Native.SWP_NOACTIVATE);
                }
                Native.SetWindowPos(hwnd, 0, d.OrigLeft + pt.x - d.DownX, d.OrigTop, 0, 0, Native.SWP_NOSIZE_ | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                Native.UpdateWindow(Native.GetParent(hwnd));
                return true;
            }
            case Native.WM_LBUTTONUP:
            {
                d.Armed = false;
                if (!d.Dragging) return false;   // 끌지 않았으면 보통 클릭
                d.Dragging = false;               // 아래 캡처 해제가 "끊김"으로 되돌리지 않게 먼저
                Native.ReleaseCapture();
                Ctl.SetState(hwnd, Ctl.State(hwnd) & ~Ctl.StPressed);
                Native.GetWindowRect(hwnd, out Native.RECT wr);
                var c = new Native.POINT { x = (wr.left + wr.right) / 2, y = wr.top };
                Native.ScreenToClient(Native.GetParent(hwnd), ref c);
                Native.PostMessageW(Native.GetParent(hwnd), App.WM_TILE_DROP, hwnd, c.x);
                return true;
            }
            case Native.WM_KEYDOWN when wParam == 0x1B && d.Dragging:   // Esc: 끌기 취소
                Native.ReleaseCapture();
                return true;
            case Native.WM_CAPTURECHANGED:
                if (d.Dragging)
                {
                    d.Dragging = false; d.Armed = false;
                    Native.SetWindowPos(hwnd, 0, d.OrigLeft, d.OrigTop, 0, 0, Native.SWP_NOSIZE_ | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                }
                d.Armed = false;
                return false;   // 누름 표시 해제는 공통 처리
        }
        return false;
    }

    private static Native.POINT ScreenPt(nint hwnd, nint lParam)
    {
        var p = new Native.POINT { x = (short)(lParam & 0xFFFF), y = (short)((lParam >> 16) & 0xFFFF) };
        Native.ClientToScreen(hwnd, ref p);
        return p;
    }

    private static void Paint(nint hwnd)
    {
        _data.TryGetValue(hwnd, out Data? d);
        bool onCard = d?.OnCard == true && !MetalUi.On(hwnd);   // 새 디자인 화면: 조각도 바탕 그림에 있다
        Ctl.Paint(hwnd, onCard ? Theme.CardBrush : Theme.BgBrush, (dc, w, h) =>
        {
            nint st = Ctl.State(hwnd);
            bool hot = (st & Ctl.StHot) != 0, pressed = (st & Ctl.StPressed) != 0;
            int r = Ctl.S(hwnd, 10);
            if (hot || pressed)
                Gdiplus.FillRoundRect(dc, 0, 0, w, h, r, Theme.Mix(onCard ? Theme.CardBg : Theme.WindowBg, Theme.ControlText, pressed ? Theme.PressMix : Theme.HoverMix + 0.04));
            if (Ctl.HasFocusRing(hwnd)) Gdiplus.DrawRoundRect(dc, 0, 0, w, h, r, Theme.AccentInk, Ctl.S(hwnd, 1));
            int s = Ctl.S(hwnd, 32);
            bool named = d is not null && d.Name.Length > 0;
            int iy = named ? Ctl.S(hwnd, 6) : (h - s) / 2;   // 이름이 있으면 아이콘은 위, 이름은 아래 두 줄
            if (d?.Icon is nint ic && ic != 0) Gdiplus.DrawIconSmooth(dc, (w - s) / 2, iy, s, ic);   // 크기가 다른 아이콘(글자·사용자 그림 64px)은 매끄럽게 줄여서
            if (d?.Bad == true) Ctl.Text(dc, Theme.FontIcon, "", Theme.DangerText, 0, iy, w, iy + s, Native.DT_CENTER | Native.DT_VCENTER);   // 경고
            if (named)
            {
                nint old = Native.SelectObject(dc, Theme.FontSmall);
                Native.SetTextColor(dc, d!.Bad ? Theme.DangerText : Theme.ControlText);
                var rc = new Native.RECT { left = Ctl.S(hwnd, 2), top = iy + s + Ctl.S(hwnd, 3), right = w - Ctl.S(hwnd, 2), bottom = h - Ctl.S(hwnd, 1) };
                Native.DrawText(dc, d.Name, ref rc, Native.DT_CENTER | Native.DT_WORDBREAK | Native.DT_NOPREFIX | 0x8000 /* DT_END_ELLIPSIS */ | 0x2000 /* DT_EDITCONTROL */);
                Native.SelectObject(dc, old);
            }
            if (d?.Edit == true)
            {
                int b = Ctl.S(hwnd, 18);
                // 상자 모서리 [편집] 연필 버튼과 같은 Tinted(옅은 강조 채움 + 강조색 연필), 연필은 7px(2026-10-05 사용자: 14px 는 동그라미 밖으로 나옴)
                Gdiplus.FillEllipse(dc, w - b, h - b, b, b, Btn.BadgeFill(false));
                Ctl.Text(dc, Theme.FontIconTiny, "", Theme.AccentLabel, w - b, h - b, w, h, Native.DT_CENTER | Native.DT_VCENTER);   // 연필
            }
        });
    }
}
