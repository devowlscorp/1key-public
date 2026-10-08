using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>App 의 일부: 단축키 등록과 입력 실행(키 보내기·목록에서 넣기 칩). (App.cs 에서 화면별로 나눔, 0.3.9 — 동작은 그대로)</summary>
internal sealed unsafe partial class App
{
    // ---------------------------------------------------------------- 단축키

    /// <param name="silent">true 면 실패를 풍선으로만 알린다 (자동 등록). false 면 호출한 쪽이 보고문을 받아 직접 보여 준다.</param>
    /// <param name="quiet">true 면 실패해도 아무것도 띄우지 않는다 (창 활성/비활성에 따른 잦은 재등록).</param>
    private string RegisterHotkeys(bool silent, bool quiet = false)
    {
        var failed = new List<string>();
        for (int i = 0; i < Config.SlotCount; i++)
        {
            Native.UnregisterHotKey(_hwnd, i);
            _hotkeyFailed[i] = false;
            Slot s = _cfg.Slots[i];
            // 내용이 없는 슬롯은 등록하지 않는다 (예전 기본값 Ctrl+Alt+1~3 이 빈 슬롯에 남아 있어도 키를 점유하지 않도록).
            // 잠긴 상태에서는 비밀번호가 메모리에 없으므로 헤더의 HasContent 로 판단한다 (잠긴 채 누르면 잠금 화면을 띄운다).
            if (!s.HasHotkey || !s.InUse) continue;
            const uint MOD_NOREPEAT = 0x4000;
            if (!Native.RegisterHotKey(_hwnd, i, s.Mods | MOD_NOREPEAT, s.Vk))
            {
                failed.Add($"{s.DisplayName(i)} ({s.HotkeyText()})");
                _hotkeyFailed[i] = true;   // 목록 행에 "등록 실패"로 남겨 풍선을 놓쳐도 알 수 있게
            }
        }
        if (failed.Count == 0) return string.Empty;
        string report = T.HotkeyRegFailed + "\n  " + string.Join("\n  ", failed);
        if (silent && !quiet && !_quietHotkeyReport) ShowBalloon(AppTitle, report, Native.NIIF_WARNING);
        return report;
    }

    // ---------------------------------------------------------------- 입력 실행

    private void FireSlot(int index)
    {
        if (_recoveryOnly || index < 0 || index >= Config.SlotCount) return;
        if (Program.IsTestMode) Native.SetPropW(_hwnd, "OneKeyTestFireCalls", ++_testFireCalls);
        Injector.Request target = Injector.Capture();   // 단축키를 누른 바로 그 순간의 창과 입력란이 입력 대상이다
        Injector.PreventAltMenu();   // 단축키가 눌린 직후, 사용자가 Alt 를 떼기 전에 (첫 글자 유실 방지)
        if (_cfg.HasMaster && !_cfg.IsUnlocked)
        {
            // 잠긴 채로는 입력하지 않고 잠금 화면을 앞으로 가져온다. 풀고 나서 대상 입력란을 다시 클릭하고 단축키를 누르면 된다.
            ShowBalloon(AppTitle, T.BalloonLockedFire, Native.NIIF_WARNING);
            ShowMainWindow();
            return;
        }
        MarkActivity();
        Slot s = _cfg.Slots[index];
        if (!s.HasPassword) { ShowBalloon(AppTitle, T.BalloonEmptySlot(s.DisplayName(index)), Native.NIIF_WARNING); return; }
        // 사이트에 연결한 항목은 연결한 페이지에만 넣는다: 지금 앞의 창이 아니라 그 주소를 띄운 브라우저 창을 찾아 앞으로 가져와 넣고,
        // 없으면 아무 데도 넣지 않는다(2026-10-01 사용자 보고: 단축키가 앞에 있던 대화 창에 아이디·비밀번호를 넣고 Enter 로 보내 버림).
        switch (RouteOf(s))   // 연결이 하나라도 있으면 커서 자리 입력으로는 절대 가지 않는다 (Codex R75-1)
        {
            case FillRoute.App: AppLinkedFill(index, target, s.App!); return;
            case FillRoute.Site: HotkeyLinkedFill(index, target, LinkedInputs(s)!); return;
            case FillRoute.Incomplete: FillNotice(T.FillLinksIncomplete(s.DisplayName(index))); return;
        }
        Inject(s, target);
    }

    /// <summary>
    /// 항목의 모든 입력(값이 있는 것)이 같은 주소의 칸에 연결돼 있으면 그 목록, 아니면 null.
    /// 이때 브라우저에서 누른 단축키는 커서 위치가 아니라 연결한 칸에 넣는다(2026-10-01 사용자 보고: Nexacro 업무 사이트 는 처음 커서가 비밀번호 칸이라
    /// "ID → Tab → 비밀번호"가 비밀번호 칸에 아이디를 넣었다).
    /// </summary>
    /// <summary>
    /// 항목을 넣는 길(Codex R75-1): 연결이 하나도 없으면 커서 자리(Typing), 프로그램 연결이면 App, 모든 입력이 같은 주소에 연결이면 Site,
    /// 그 밖(일부만 연결·주소가 다름)은 Incomplete = 아무 데도 넣지 않고 안내. 단축키·목록 [입력]·트레이가 모두 이 판정을 쓴다.
    /// </summary>
    internal enum FillRoute { Typing, App, Site, Incomplete }
    internal static FillRoute RouteOf(Slot s)
        => s.App is not null ? FillRoute.App : !s.HasAnySite ? FillRoute.Typing : LinkedInputs(s) is not null ? FillRoute.Site : FillRoute.Incomplete;

    internal static List<(int Input, SiteLink Link)>? LinkedInputs(Slot s)
    {
        var list = new List<(int, SiteLink)>();
        for (int k = 0; k < s.InputCount; k++)
        {
            if (s.ValueOf(k).Length == 0) continue;
            if (s.SiteOf(k) is not SiteLink l) return null;
            if (list.Count > 0 && list[0].Item2.Url != l.Url) return null;
            list.Add((k, l));
        }
        return list.Count > 0 ? list : null;
    }

    /// <summary>
    /// 사이트에 연결한 항목의 단축키: 그 주소를 띄운 브라우저 창을 찾아(앞의 창부터) 앞으로 가져오고, 연결한 칸을 모두 찾으면 [채우기]와 같은
    /// 방법으로 입력마다 그 칸에 넣는다(커서가 어디에 있든). Enter 는 항목 설정을 따른다(브라우저에서 보내지 않기가 꺼져 있을 때).
    /// 창이 없거나·앞으로 못 가져오거나·칸을 다 찾지 못하면 **아무 데도 넣지 않고** 이유를 알린다(앞의 다른 창에 넣지 않는다).
    /// </summary>
    private void HotkeyLinkedFill(int index, Injector.Request target, List<(int Input, SiteLink Link)> linked)
    {
        if (_fillRunning) return;
        Slot s = _cfg.Slots[index];
        string url = linked[0].Link.Url, name = s.DisplayName(index);
        int gen = _uiaGen;
        var links = linked.Select(x => (index * Slot.MaxInputs + x.Input, x.Link)).ToList();
        // ① 그 주소를 띄운 브라우저 창 찾기(앞의 창부터) ② 앞으로 가져오기 ③ 그 창에서 연결한 칸을 모두 찾으면 [채우기]와 같은 방법으로 넣기
        Uia.Post(_hwnd, job => Uia.LocatePage(target.Window, url, job), 4000, null, (found, w) =>
        {
            _testHotkeyProbed++;   // 검증 관찰값: 이 요청의 페이지 찾기가 돌아왔다(새 요청 처리 번호, Codex 09:45-be)
            if (!Unlocked || gen != _uiaGen || _fillRunning) { _testHotkeyNotice = 3; return; }   // 그 사이 잠겼다
            if (found && w == Uia.ManyPages) { _testHotkeyNotice = 4; FillNotice(T.FillPageMany(name)); return; }
            if (!found || w == 0) { _testHotkeyNotice = 5; FillNotice(T.FillPageNotOpen(name, SiteUrl.Display(url))); return; }
            if (Native.GetForegroundWindow() != w)
            {
                if (Native.IsIconic(w)) Native.ShowWindow(w, Native.SW_RESTORE);
                Native.SetForegroundWindow(w);
            }
            Uia.Post(_hwnd, job => Uia.Probe(w, links), 3000, null, (ok, r) => HotkeyLinkedProbed(index, gen, w, linked, links.Count, ok, r));
        });
    }

    /// <summary>
    /// 프로그램에 연결한 항목: 그 프로그램 창을 찾아 앞으로 가져오고, 연결한 칸을 눌러 커서를 넣은 뒤(포커스 확인) 지금의 단축키 입력과
    /// 같은 방법으로 넣는다(첫 칸의 글은 바꿔 씀, 입력이 여럿이면 Tab, Enter 는 항목 설정). 창·칸이 없거나 커서를 못 옮기면 아무 데도 넣지 않는다.
    /// </summary>
    private void AppLinkedFill(int index, Injector.Request pressed, AppLink l)
    {
        Slot s = _cfg.Slots[index];
        string name = s.DisplayName(index);
        // 0.2.76–0.2.82 의 연결(파일 이름뿐·자리 없음)은 쓰지 않는다: 다시 연결 안내만 (Codex R82: 이름만으로 넣지 않는다)
        if (!l.Usable) { FillNotice(T.FillAppRelink(name)); return; }
        nint w = AppLinks.FindWindow(l, pressed.Window, out bool many);
        if (many) { FillNotice(T.FillAppMany(name)); return; }
        if (w == 0)
        {
            // 같은 이름의 프로그램이 다른 위치에서 실행 중이면 두 경로를 보여 주고 다시 연결하게 한다(업데이트로 옮겨 간 경우 포함)
            FillNotice(AppLinks.OtherPath(l) is string other ? T.FillAppPathChanged(name, l.Exe, other) : T.FillAppNotOpen(name, l.FileName));
            return;
        }
        if (Injector.NeedsElevationFor(w)) { OfferElevation(); return; }   // 누르기·입력 모두 막힌다: 먼저 권한 안내
        if (Native.GetForegroundWindow() != w)
        {
            if (Native.IsIconic(w)) Native.ShowWindow(w, Native.SW_RESTORE);
            Native.SetForegroundWindow(w);
        }
        if (!Injector.TryBegin()) return;
        int gen = Injector.Generation;
        Config cfg = _cfg;
        nint me = _hwnd;
        var thread = new Thread(() =>
        {
            bool sent = false;
            string? err = null;
            try
            {
                for (int i = 0; i < 20 && Native.GetForegroundWindow() != w; i++) Thread.Sleep(50);
                nint field = 0;
                string why = "";
                if (Native.GetForegroundWindow() != w) err = T.FillPageNotFront(name);
                else if (Injector.Generation != gen || !cfg.IsUnlocked) err = T.InjStopped;
                else if ((field = AppLinks.Focus(w, l, () => Injector.Generation == gen && cfg.IsUnlocked, out why, !s.NoClick)) == 0)
                    err = why is "relink" ? T.FillAppRelink(name)
                        : why is "notfound" or "place" or "noclick" || why.StartsWith("place-hit=", StringComparison.Ordinal) ? T.FillAppFieldNotFound(name) + " [" + why + "]" : T.FillAppFocusFailed(name) + " [" + why + "]";
                else
                {
                    uint tid = Native.GetWindowThreadProcessId(w, out _);
                    nint focus = Injector.FocusOf(tid);   // 칸 안의 실제 입력창(칸 자체일 수도 있다)
                    Interlocked.Increment(ref _testAppLinked);
                    sent = true;   // 여기부터 Send 가 사용권을 놓는다
                    Injector.Result r = Injector.Send(s, cfg, new Injector.Request(w, tid, focus), gen, replaceFirst: true);
                    if (r.Status == Injector.Status.NeedsElevation) { Volatile.Write(ref _injectNeedsElevation, 1); Native.PostMessageW(me, WM_INJECT_RESULT, 0, 0); }
                    else if (r.Status is Injector.Status.Error or Injector.Status.Empty or Injector.Status.Timeout) err = r.Message ?? T.InjectFailed;
                    else if (r.Message is not null) { Volatile.Write(ref _injectNotice, r.Message); Native.PostMessageW(me, WM_INJECT_RESULT, 0, 0); }
                }
            }
            catch (Exception ex) { err = T.InjError(ex.Message); }
            finally
            {
                if (!sent) Injector.Abandon();
                if (err is not null) { Volatile.Write(ref _injectMessage, err); Native.PostMessageW(me, WM_INJECT_RESULT, 0, 0); }
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        try { thread.Start(); }
        catch { Injector.Abandon(); throw; }
    }

    /// <summary>찾은 창을 앞으로 가져온 뒤의 확인 결과: 그 창이 앞에 있고 연결한 칸을 모두 하나씩 찾았을 때만 넣는다.</summary>
    private void HotkeyLinkedProbed(int index, int gen, nint w, List<(int Input, SiteLink Link)> linked, int count, bool ok, Uia.ProbeResult? r)
    {
            Slot s = _cfg.Slots[index];
            string name = s.DisplayName(index);
            if (!Unlocked || gen != _uiaGen || _fillRunning) { _testHotkeyNotice = 3; return; }   // 그 사이 잠겼다
            if (Native.GetForegroundWindow() != w) { _testHotkeyNotice = 1; FillNotice(T.FillPageNotFront(name)); return; }
            bool here = ok && r is not null && r.Url == linked[0].Link.Url && r.Hits.Count == count
                        && r.Hits.Select(h => h.Slot).Distinct().Count() == count
                        && r.Hits.GroupBy(h => (h.Field.Password, h.Field.AutomationId, h.Field.Name, h.Field.Index)).All(g => g.Count() == 1);
            if (!here) { _testHotkeyNotice = 2; FillNotice(T.FillFieldsNotFound(name)); return; }
            _testHotkeyNotice = 0;
            var jobs = linked.OrderBy(x => x.Input)
                .Select(x => (FillLabel(s, index, x.Input), x.Link, new Slot { Name = s.Name, Password = s.ValueOf(x.Input), Method = s.Method, PressEnter = false, NoClick = s.NoClick }))
                .ToList();
            _fillRunning = true;
            HideFill();
            MarkActivity();
            _testHotkeyLinked++;
            RunFill(w, jobs, enter: s.PressEnter && !s.NoEnterInBrowser, byButton: false);
    }

    /// <summary>
    /// 입력 작업을 수락하고 작업 스레드를 띄운다. 수락(busy)·대상 창·취소 세대는 모두 여기 UI 스레드에서 정해서 넘긴다.
    /// 그래야 스레드가 늦게 떠도 "단축키를 누른 순간의 창"에만 넣고, 그 사이의 잠금(세대 증가)을 놓치지 않는다.
    /// </summary>
    private void Inject(Slot s, Injector.Request target)
    {
        if (!Injector.TryBegin()) return;   // 이미 입력 중이면 스레드를 만들지 않는다
        int gen = Injector.Generation;
        Config cfg = _cfg;
        var thread = new Thread(() =>
        {
            Injector.Result r = Injector.Send(s, cfg, target, gen);
            if (r.Status == Injector.Status.NeedsElevation)
            {
                Volatile.Write(ref _injectNeedsElevation, 1);
                Native.PostMessageW(_hwnd, WM_INJECT_RESULT, 0, 0);
            }
            else if (r.Status is Injector.Status.Error or Injector.Status.Empty or Injector.Status.Timeout)
            {
                Volatile.Write(ref _injectMessage, r.Message ?? T.InjectFailed);
                Native.PostMessageW(_hwnd, WM_INJECT_RESULT, 0, 0);
            }
            else if (r.Message is not null)   // 성공했지만 알릴 것이 있다 (예: 브라우저라 Enter 를 보내지 않음, T1)
            {
                Volatile.Write(ref _injectNotice, r.Message);
                Native.PostMessageW(_hwnd, WM_INJECT_RESULT, 0, 0);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        try { thread.Start(); }
        catch { Injector.Abandon(); throw; }
    }

    // ---------------------------------------------------------------- 목록에서 넣기 (D안 입력 칩, 최종수정안 T11)

    /// <summary>
    /// 목록의 [입력]: 확정 키를 등록하고, 칩을 만든 뒤 본창을 내리고 칩을 보인다. 아직 아무 대상도 정하지 않는다.
    /// 대상은 사용자가 칩의 [입력]이나 확정 키를 누른 그 순간에 정한다(<see cref="ChipConfirm"/>).
    /// </summary>
    private void StartChip(int slot)
    {
        if (!Unlocked || slot < 0 || slot >= Config.SlotCount) return;
        if (!_cfg.Slots[slot].HasPassword) return;
        ReleaseChip();
        // 사이트에 연결한 항목: 칩(누른 뒤 클릭한 칸에 넣기)이 아니라 단축키와 같이 연결한 페이지를 찾아 그 칸에 넣는다
        // (2026-10-01 사용자 보고: 다른 업무 사이트 에서 커서가 비밀번호 칸일 때 목록 [입력] 이 아이디를 비밀번호 칸에 넣음). 트레이 메뉴도 여기로 온다.
        Slot cs = _cfg.Slots[slot];
        switch (RouteOf(cs))   // R75-1: 연결이 있으면 칩(커서 자리)으로 가지 않는다
        {
            case FillRoute.App: AppLinkedFill(slot, default, cs.App!); return;
            case FillRoute.Site: HotkeyLinkedFill(slot, default, LinkedInputs(cs)!); return;
            case FillRoute.Incomplete: FillNotice(T.FillLinksIncomplete(cs.DisplayName(slot))); return;
        }

        // 확정 키: 칩을 보이기 전에 등록해 보고, 안 되면 먼저 알린다(버튼으로만 넣을 수 있음).
        const uint MOD_NOREPEAT = 0x4000;
        bool hk = _cfg.ConfirmVk != 0 && Native.RegisterHotKey(_hwnd, ChipHotkeyId, _cfg.ConfirmMods | MOD_NOREPEAT, _cfg.ConfirmVk);
        string keyText = HotkeyBox.Text(_cfg.ConfirmMods, _cfg.ConfirmVk).Replace(" + ", "+");   // 칩 안내가 잘리지 않도록 짧게
        if (!hk)
        {
            string why = _cfg.ConfirmVk == 0
                ? T.ChipNoConfirmKey
                : T.ChipConfirmKeyBusy(keyText);
            int r = Msg(why + "\n\n" + T.ChipButtonOnly,
                        AppTitle, Native.MB_YESNO | Native.MB_ICONWARNING | Native.MB_DEFBUTTON2);   // 기본은 아니요: 키보드 사용자를 마우스 전용 대기로 이끌지 않는다
            if (r != Native.IDYES || !Unlocked || !_cfg.Slots[slot].HasPassword) return;   // 트레이에서도 오므로 화면은 따지지 않는다
        }

        string hint = hk ? T.ChipHintKey(keyText) : T.ChipHint;
        _chipGen++;
        nint chip = Chip.Create(_hwnd, _chipGen, _cfg.Slots[slot].DisplayName(slot), hint, _dpi, WorkArea.For(_hwnd));
        if (chip == 0)
        {
            // 칩을 못 만들었으면 본창은 그대로 두고(아직 내리지 않았다) 알린다. 입력은 시도하지 않는다.
            if (hk) Native.UnregisterHotKey(_hwnd, ChipHotkeyId);
            Msg(T.ChipCreateFailed, AppTitle, Native.MB_OK | Native.MB_ICONERROR);
            return;
        }
        _chip = chip; _chipSlot = slot; _chipHotkey = hk; _chipHint = hint;
        Native.ShowWindow(_hwnd, Native.SW_HIDE);            // 본창을 내리면 Windows 가 직전 창을 다시 활성화한다
        Native.ShowWindow(_chip, Native.SW_SHOWNOACTIVATE);   // 칩은 활성화하지 않고 보이기만
        Native.SetTimer(_hwnd, TimerChipExpire, (uint)ChipMs, 0);
        UpdateTrayTip();
    }

    /// <summary>칩의 [입력] 또는 확정 키. 그 순간의 활성 창·입력란을 읽어 넣을 만하면 넣고, 아니면 칩을 두고 안내한다.</summary>
    private void ChipConfirm(Injector.Request target)
    {
        if (_chipSlot < 0) return;
        if (!Unlocked) { ReleaseChip(); return; }
        int slot = _chipSlot;
        Slot s = _cfg.Slots[slot];
        if (!s.HasPassword) { ReleaseChip(); return; }

        // target 은 누른 바로 그 순간 잡은 것이다(칩: 버튼 알림 안에서, 확정 키: WM_HOTKEY 처리 안에서). 오래된 대상을 쓰지 않는다.
        switch (Injector.Check(target))
        {
            case Injector.TargetCheck.Ok:
                break;
            case Injector.TargetCheck.NeedsElevation:
                // 관리자 권한 창: 일반 권한의 1Key 는 넣을 수 없다. 칩을 닫고 기존 승격 흐름으로 보낸다(다시 시작한 뒤 다시 누른다).
                ReleaseChip();
                OfferElevation();
                return;
            default:
                ChipHint(T.ChipClickFirst);
                return;
        }
        Injector.PreventAltMenu();   // 확정 키에 Alt 가 있으면 대상 창의 메뉴가 열리지 않도록
        ReleaseChip();                // 한 번만: 입력을 시작하면 대기는 끝난다
        MarkActivity();
        Inject(s, target);
    }

    private void ChipHint(string warning)
    {
        if (_chip == 0) return;
        Chip.SetHint(_chip, warning, warn: true);
        Native.SetTimer(_hwnd, TimerChipHint, 1500, 0);
    }

    /// <summary>
    /// 입력 대기를 끝낸다. 취소·만료·잠금·본창 다시 열기·슬롯 삭제·입력 시작·종료가 모두 여기로 온다.
    /// 여러 번 불려도 한 번만 정리한다: 타이머 두 개, 확정 키 등록, 칩 창.
    /// </summary>
    private void ReleaseChip()
    {
        if (_chipSlot < 0 && _chip == 0 && !_chipHotkey) return;
        Native.KillTimer(_hwnd, TimerChipExpire);
        Native.KillTimer(_hwnd, TimerChipHint);
        if (_chipHotkey) { Native.UnregisterHotKey(_hwnd, ChipHotkeyId); _chipHotkey = false; }
        _chipGen++;   // 이미 보내 둔 칩 명령은 이제 무효
        nint c = _chip;
        _chip = 0; _chipSlot = -1; _chipHint = ""; _chipLink = false;
        if (c != 0) Native.DestroyWindow(c);
        UpdateTrayTip();
    }
}
