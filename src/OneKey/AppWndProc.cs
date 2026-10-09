using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>App 의 일부: 창 메시지 처리(WndProc)와 명령 분기. (App.cs 에서 화면별로 나눔, 0.3.9 — 동작은 그대로)</summary>
internal sealed unsafe partial class App
{
    // ---------------------------------------------------------------- 메시지 처리

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint WndProcNative(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try { App? app = _self; if (app is not null) return app.WndProc(hwnd, msg, wParam, lParam); }
        catch { }
        return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (_recoveryOnly && RecoveryBlocks(msg))   // 끝나지 않은 복원(R43-1): 예전 설정으로 아무것도 하지 않는다
        {
            if (Program.IsTestMode) Native.SetPropW(_hwnd, "OneKeyTestRecoveryBlocked", ++_testRecoveryBlocked);
            return 0;
        }
        switch (msg)
        {
            case Native.WM_COMMAND:
                OnCommand((int)(wParam & 0xFFFF), (int)((wParam >> 16) & 0xFFFF));
                return 0;

            case Native.WM_HOTKEY:
                if ((int)wParam == ChipHotkeyId) { if (_chipSlot >= 0) ChipConfirm(Injector.Capture()); }
                else if (IsLaunchHotkey((int)wParam)) RunLaunch((int)wParam - LaunchHotkeyBase);   // 잠김 중에도: 잠금·입력 상태는 건드리지 않는다
                else FireSlot((int)wParam);
                return 0;

            case Native.DM_GETDEFID:
                return _page.DefaultButton != 0 ? (nint)((Native.DC_HASDEFID << 16) | (uint)_page.DefaultButton) : 0;

            case Native.WM_CTLCOLORSTATIC:
            case Native.WM_CTLCOLORBTN:
            {
                if (_edits.Contains(lParam) && Theme.MetalPage)
                {
                    // 새 디자인: 읽기 전용·쓸 수 없는 입력칸도 홈을 이어 그린다
                    Native.SetBkMode(wParam, Native.TRANSPARENT);
                    Native.SetTextColor(wParam, Metal.Ref(Metal.InkSub(Theme.IsDark)));
                    Theme.AlignBg(wParam, lParam);
                    return Theme.BgBrush;
                }
                if (_edits.Contains(lParam))
                {
                    Native.SetBkColor(wParam, Theme.EditBg);
                    Native.SetTextColor(wParam, Theme.DisabledText);
                    return Theme.EditBrush;
                }
                // 창 바탕 위 글자: 바탕이 그림(B 그라데이션)이므로 글자 뒤를 칠하지 않고, 붓 원점을 이 칸 자리에 맞춘다.
                // _staticStyle 의 붓은 화면을 만들 때 넣은 것이라, 테마가 바뀌어 붓이 새로 생겨도 "바탕 쪽"이면 지금 붓을 쓴다.
                bool onCardStatic = _staticStyle.TryGetValue(lParam, out var st) && st.Brush == Theme.CardBrush;
                if (onCardStatic && !Theme.MetalPage)   // 새 디자인 화면: 조각도 바탕 그림에 있으므로 창 바탕 위 글자와 같게
                {
                    Native.SetBkColor(wParam, Theme.CardBg);
                    Native.SetTextColor(wParam, st.Text);
                    return Theme.CardBrush;
                }
                Native.SetBkMode(wParam, Native.TRANSPARENT);
                Native.SetTextColor(wParam, st.Brush != 0 ? st.Text : Theme.ControlText);
                Theme.AlignBg(wParam, lParam);
                return Theme.BgBrush;
            }

            case Native.WM_CTLCOLOREDIT:
            case Native.WM_CTLCOLORLISTBOX:
                if (Theme.MetalPage && msg == Native.WM_CTLCOLOREDIT && _edits.Contains(lParam))
                {
                    // 새 디자인: 입력칸의 파인 홈이 바탕 그림에 있으므로 글 뒤를 칠하지 않고 그 자리를 이어 그린다
                    Native.SetBkMode(wParam, Native.TRANSPARENT);
                    Native.SetTextColor(wParam, Metal.Ref(Metal.Ink(Theme.IsDark)));
                    Theme.AlignBg(wParam, lParam);
                    return Theme.BgBrush;
                }
                if (_editsOnBg.Contains(lParam))
                {
                    Native.SetBkColor(wParam, Theme.CardBg);
                    Native.SetTextColor(wParam, Theme.EditText);
                    return Theme.CardBrush;
                }
                Native.SetBkColor(wParam, Theme.EditBg);
                Native.SetTextColor(wParam, Theme.EditText);
                return Theme.EditBrush;

            case Native.WM_ERASEBKGND:
            {
                Native.GetClientRect(hwnd, out Native.RECT rc);
                ComposeBackground();   // 바탕 그림(몸체 + 판·머리줄): 크기·테마·화면이 같으면 그대로
                Native.SetBrushOrgEx(wParam, 0, 0, 0);
                Native.FillRect(wParam, ref rc, Theme.BgBrush);
                DrawDecorations(wParam);
                return 1;
            }

            case Native.WM_LBUTTONDOWN:
            {
                int px = Native.LoWord(lParam), py = Native.HiWord(lParam);
                if (_cur == Screen.List && _page.ScrollTrack is { } st
                    && px >= Scale(st.X) - Scale(4) && px <= Scale(st.X + st.W) + Scale(4) && py >= Scale(st.Y) && py <= Scale(st.Y + st.H))
                {
                    // 스크롤 막대 트랙: 손잡이 위를 누르면 한 화면 위로, 아래를 누르면 한 화면 아래로
                    if (py < Scale(st.ThumbY)) ScrollList(_listTop - _visibleRows);
                    else if (py > Scale(st.ThumbY + st.ThumbH)) ScrollList(_listTop + _visibleRows);
                    return 0;
                }
                // 본문 스크롤 막대: 손잡이는 끌고, 트랙의 빈 곳을 누르면 한 화면씩
                if (PageScrollBar(out Native.RECT tr, out Native.RECT th)
                    && px >= tr.left - Scale(6) && px <= tr.right + Scale(6) && py >= tr.top && py <= tr.bottom)
                {
                    if (py >= th.top && py <= th.bottom) { _pageDragOffset = py - th.top; Native.SetCapture(hwnd); }
                    else ScrollPage((py < th.top ? -1 : 1) * Math.Max(Scale(RowH), _viewH - Scale(RowH)));
                    return 0;
                }
                if (StripBarDown(px, py)) return 0;   // 바로 실행 띠의 가로 막대
                FocusFieldAt(px, py);
                return 0;
            }

            case Native.WM_MOUSEMOVE:
                if (_stripDrag >= 0) { int sx = Native.LoWord(lParam); if (sx > 0x7FFF) sx -= 0x10000; StripBarMove(sx); return 0; }
                if (_pageDragOffset >= 0 && PageScrollBar(out Native.RECT tr2, out Native.RECT th2))
                {
                    int py = Native.HiWord(lParam);
                    if (py > 0x7FFF) py -= 0x10000;   // 창 위로 끌어 올린 경우(음수 좌표)
                    int room = (tr2.bottom - tr2.top) - (th2.bottom - th2.top);
                    if (room > 0)
                    {
                        int want = (int)((long)(py - _pageDragOffset - tr2.top) * _pageScrollMax / room);
                        ScrollPage(Math.Clamp(want, 0, _pageScrollMax) - _pageScroll);
                    }
                    return 0;
                }
                break;

            case Native.WM_LBUTTONUP:
                // 끄는 중이 아니어도 이 창이 마우스를 잡고 있으면 놓는다(끌기 상태만 먼저 풀린 경우 대비)
                if (_pageDragOffset >= 0 || _stripDrag >= 0 || Native.GetCapture() == hwnd) { _pageDragOffset = -1; _stripDrag = -1; Native.ReleaseCapture(); return 0; }
                break;

            case Native.WM_CAPTURECHANGED:
                _pageDragOffset = -1; _stripDrag = -1;
                break;

            case Native.WM_MOUSEWHEEL:
            {
                int delta = (short)((wParam >> 16) & 0xFFFF);
                int step = delta / 120; if (step == 0) step = delta > 0 ? 1 : -1;
                if (StripWheel((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF), -step)) return 0;   // 바로 실행 띠 위: 그 줄을 가로로
                if (_cur == Screen.List && _listScrollMax > 0) ScrollList(_listTop - step);
                else if (_pageScrollMax > 0) ScrollPage(-step * Scale(RowH));   // 작은 화면에서 긴 화면의 본문 (T5)
                return 0;
            }

            case Native.WM_LIST_REFRESH:
                if (_cur == Screen.List) RefreshList();
                return 0;

            case Native.WM_SETTINGCHANGE:
            {
                string? area = lParam != 0 ? Marshal.PtrToStringUni(lParam) : null;
                if (area is "ImmersiveColorSet" or "WindowsThemeElement") RethemeAll();
                CatWidget.EnvChanged();   // 작업 영역·작업 표시줄 설정이 바뀌었을 수 있다
                return 0;
            }

            case 0x007E:   // WM_DISPLAYCHANGE: 화면 구성·해상도
                CatWidget.EnvChanged();
                break;

            case 0x0018:   // WM_SHOWWINDOW: 본창이 보이거나 숨는다 — 처리가 끝난 뒤 마스코트를 다시 맞춘다
                Native.PostMessageW(hwnd, WM_WALKER_SYNC, 0, 0);
                break;

            case WM_WALKER_SYNC:
                SyncWalker();
                return 0;

            case WM_WALKER_CLICK:
                OnWalkerClick((int)wParam);
                return 0;

            case Native.WM_TEST_GC:
                if (!Program.IsTestMode) break;
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                return (nint)(GC.GetTotalMemory(false) / 1024);

            case Native.WM_TEST_TOAST_COUNT:
                if (!Program.IsTestMode) break;
                return Toast.ShownCount;

            case Native.WM_TEST_RENDERER:
                if (!Program.IsTestMode) break;
                return (nint)(Dw.StatusBits | (1L << 60));

            case Native.WM_TEST_RELAYOUT:
                if (!Program.IsTestMode) break;
                RelayoutKeepingView();
                return 0;

            case Native.WM_TEST_PENDING:
                if (!Program.IsTestMode) break;
                return (nint)((_testSlot is not null ? 1L : 0L) | (_diagPending ? 2L : 0L) | ((long)Math.Min(Dialog.OpenCount, 255) << 8)
                    | ((long)Math.Min(_testRestartCalls, 255) << 16) | (1L << 60));

            case Native.WM_TEST_OFFER_ELEVATION:   // 검증용: 관리자 권한 창에 넣으려 할 때의 질문을 띄운다(실제 관리자 창 없이)
                if (!Program.IsTestMode) break;
                OfferElevation();
                return 0;

            case Native.WM_TEST_GC_COUNT:
                if (!Program.IsTestMode) break;
                return (nint)((long)Math.Min(GC.CollectionCount(0), 0xFFFFF) | ((long)Math.Min(GC.CollectionCount(1), 0xFFFFF) << 20) | ((long)Math.Min(GC.CollectionCount(2), 0xFFFFF) << 40) | (1L << 60));   // 60번 비트 = 응답함(GC 0회와 구분)

            case WM_EDIT_MODE:
                if (_cur == Screen.Edit) ChangeMode(wParam == 1);
                return 0;

            case WM_PICK_REBUILD:
                RebuildPick();
                return 0;

            case WM_PICK_READY:
                OnPickReady((int)wParam);
                return 0;

            case WM_LAUNCH_DONE:
                OnLaunchDone((int)wParam);
                return 0;

            case 0x020E:   // WM_MOUSEHWHEEL(가로 휠·터치패드): 바로 실행 띠의 그 줄
            {
                int delta = (short)((wParam >> 16) & 0xFFFF);
                int step = delta / 120; if (step == 0) step = delta > 0 ? 1 : -1;
                StripWheel((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF), step);
                return 0;
            }

            case LaunchDrop.WM_LAUNCH_DROP:
                OnLaunchDrop(LaunchDrop.Take());
                return 0;

            case LaunchDrop.WM_DROPFILES:   // 관리자 권한일 때의 파일 끌어 놓기
            {
                var dropped = LaunchDrop.FromHDrop(wParam, finish: true);
                if (CanDropLaunch) OnLaunchDrop(dropped);
                return 0;
            }

            case Native.WM_APP + 44:   // 시험 전용: 설정 폴더의 drop-test.txt("경로|이름" 줄)를 놓인 것처럼 — 끌어 놓은 뒤의 처리 시험
                if (!Program.IsTestMode) break;
                try { OnLaunchDrop(File.ReadAllLines(Path.Combine(Config.Dir, "drop-test.txt")).Select(l => (l.Split('|')[0], l.Contains('|') ? l[(l.IndexOf('|') + 1)..] : "")).ToList()); } catch { }
                return 0;

            case WM_TILE_DROP:
                OnTileDrop(wParam, (int)lParam);
                return 0;

            case Native.WM_APP + 41:   // 시험 전용: 실제 아이콘을 받은 바로 실행 항목 수(기본 아이콘 아님) — 늦게 온 아이콘 시험(2026-10-05)
                return Program.IsTestMode ? LoadedIconCount : 0;

            case Native.WM_APP + 40:   // 시험 전용: 아직 풀리지 않은 접근성 객체 수(Codex 20:13 RW-3 수명 시험)
                return Program.IsTestMode ? CtlAcc.Live : 0;

            case WM_KDF_DONE:   // 키 계산이 끝났다(작업 스레드 → UI 스레드에서 반영)
                OnKdfDone();
                return 0;
            case WM_LAUNCH_SEEN:
                OnLaunchSeen((int)wParam, lParam);
                return 0;

            case WM_ICON_READY:
                OnIconReady();
                return 0;

            case WM_FAV_READY:
                OnFavReady();
                return 0;

            case WM_WEBPICK_READY:
                OnWebPickReady((int)wParam);
                return 0;

            case WM_TILE_NAV:
                if (_cur == Screen.List) OnTileNav(lParam, (int)wParam);
                return 0;

            case WM_EDIT_REBUILD:
                if (_cur == Screen.Edit) RestoreEdit(ReadEdit(), true, _editPwVisible);
                return 0;

            case WM_SETTINGS_REBUILD:
                if (_cur == Screen.Settings && C(IdAdmin) != 0 && GetCheck(IdAdmin) != _settingsAdminNote)
                {
                    _settingsDraft = ReadSettings();   // 바꾼 값은 초안으로 그대로
                    int keepScroll = _pageScroll;      // 스크롤 자리도 그대로(스위치가 아래쪽에 있다)
                    ShowScreen(Screen.Settings);
                    ScrollPage(keepScroll - _pageScroll);
                    if (C(IdAdmin) is nint a and not 0) Native.SetFocus(a);   // 누르던 스위치에 그대로
                }
                return 0;

            case WM_INJECT_RESULT:
            {
                if (Interlocked.Exchange(ref _injectNeedsElevation, 0) == 1) { OfferElevation(); return 0; }
                string? m = Interlocked.Exchange(ref _injectMessage, null);
                if (m is not null) { ShowBalloon(AppTitle, m, Native.NIIF_WARNING); Toast.Show(_hwnd, m, 6000, warn: true); }   // 알림이 꺼져 있어도 보이게
                string? n = Interlocked.Exchange(ref _injectNotice, null);
                if (n is not null && m is null) ShowBalloon(AppTitle, n, Native.NIIF_INFO);
                return 0;
            }

            case Native.WM_UIA_DONE:
                Uia.DrainDone();
                return 0;

            case Native.WM_FILL_COMMAND:
                FillNow((int)wParam);
                return 0;

            case Native.WM_FILL_DONE:
                OnFillDone();
                return 0;

            case Native.WM_TEST_SITE2:
                if (!Program.IsTestMode) break;
                // lParam 0: 감시·진단 순서(C61-4), 1: 설정 키 간격·채우기 결과·연결 결과(C61-2/3), 2: UIA 문서 경계 관찰값 + wParam 으로 실패 주입(R60-2)
                if (lParam == 2) { Uia.TestFail = (int)wParam; Uia.TestResetObservations(); return unchecked((nint)(1L << 60)); }
                if (lParam == 3) return (nint)(Uia.TestObservations() | (1L << 60));
                if (lParam == 4) { _testLinkResult = 0; Volatile.Write(ref _testFillResult, 0); Volatile.Write(ref _testFillMs, 0); Injector.TestExpireNow = false; return unchecked((nint)(1L << 60)); }
                if (lParam == 10) { Injector.TestExpireNow = wParam != 0; return unchecked((nint)(1L << 60)); }   // 제한 시각이 있는 전송을 지금 만료로(R75-2 대기 경계 시험)
                if (lParam == 11)
                {
                    // 트레이 메뉴의 항목을 고른 것과 같은 입구(TrackPopupMenu 는 하네스가 누를 수 없다). R75-1 시험
                    if ((int)wParam < 0 || (int)wParam >= Config.SlotCount) return 0;
                    TrayChip((int)wParam);
                    return unchecked((nint)(1L << 60));
                }
                if (lParam == 9)
                {
                    // 미완성 연결 만들기(시험 설정 메모리만, 저장 검사를 거치지 않음, Codex R75-1 시험): wParam = 슬롯 × 16 + 방법.
                    // 0 = 연결 없음, 1 = 입력 1 만, 2 = 입력 2 만, 3 = 두 입력이 서로 다른 주소. 입력이 둘인 항목에만.
                    int ts = (int)wParam >> 4, tm = (int)wParam & 15;
                    if (ts < 0 || ts >= Config.SlotCount || _cfg.Slots[ts].More.Count < 1) return 0;
                    Slot sl = _cfg.Slots[ts];
                    var a = new SiteLink("https://incomplete-a.test/login", "user", "", false, 0);
                    var b = new SiteLink(tm == 3 ? "https://incomplete-b.test/login" : "https://incomplete-a.test/login", "pw", "", true, 0);
                    sl.Site = tm is 1 or 3 ? a : null;
                    sl.More[0] = sl.More[0] with { Site = tm is 2 or 3 ? b : null };
                    return unchecked((nint)(1L << 60));
                }
                if (lParam == 5) { Volatile.Write(ref _testFillStage, (int)wParam); return unchecked((nint)(1L << 60)); }
                // 13: 그 브라우저 창(wParam)에 자동 완성·저장 로그인 목록이 보이는가(MainFrame 출발 reason 9 와 같은 확인): 값 + 2 (1 확인 불가, 2 없음, 3 보임), 0 = 시간 초과
                if (lParam == 13)
                    return Uia.Run(job => Uia.BrowserListVisible(wParam), 3000, null, out int lv) ? (nint)((lv + 2) | (1L << 60)) : unchecked((nint)(1L << 60));
                // 12: 제품이 보낸 입력 수(0–31)·연결 단축키 확인 횟수(32–47)·마지막 결과(48–51)·잠금 화면 CapsLock 끈 횟수(52–55) (Codex 09:45-be)
                if (lParam == 12)
                    return (nint)((long)(uint)Volatile.Read(ref Injector.TestInputsSent) | ((long)(_testHotkeyProbed & 0xFFFF) << 32)
                        | ((long)(_testHotkeyNotice & 0xF) << 48) | ((long)(_testCapsTaps & 0xF) << 52) | (1L << 60));
                if (lParam == 8)
                {
                    // 프로그램 연결 바꾸기(시험 설정 메모리만): wParam = 슬롯 × 16 + 방법. 1 = 같은 파일 이름의 다른 위치, 2 = 예전 연결(파일 이름뿐)
                    int ts = (int)wParam >> 4, tm = (int)wParam & 15;
                    if (ts < 0 || ts >= Config.SlotCount || _cfg.Slots[ts].App is not AppLink ta) return 0;
                    _cfg.Slots[ts].App = ta with { Exe = tm == 1 ? @"C:\1Key-test-other\" + ta.FileName : ta.FileName };
                    return unchecked((nint)(1L << 60));
                }
                if (lParam == 7) { Uia.TestProbeDelayMs = Math.Clamp((int)wParam, 0, 10000); return unchecked((nint)(1L << 60)); }   // 확인 지연 바꾸기(측정 전 0)
                if (lParam == 6) { Interlocked.Exchange(ref Uia.TestNextProbeDelayMs, (int)wParam); return unchecked((nint)(1L << 60)); }   // 다음 확인 한 번만 더 늦춤   // 다음 채우기 한 번: 1–3 잠금+제한 겹침 단계, 4 권한 부족
                if (lParam == 1)
                    return (nint)((long)Math.Min(_cfg.KeyDelayMs, 0xFFFF) | ((long)Math.Min(Volatile.Read(ref _testFillMs), 0xFFFF) << 16)
                        | ((long)(Volatile.Read(ref _testFillResult) & 0xF) << 32) | ((long)(_testLinkResult & 0xFF) << 36)
                        | (_editSites.Any(x => x is not null) || _editApp is not null ? 1L << 44 : 0) | ((long)(Volatile.Read(ref _testAppLinked) & 0xF) << 56) | ((long)(Volatile.Read(ref _testFocusMethodMax) & 7) << 45) | ((long)(_testHotkeyLinked & 0xFF) << 48) | (1L << 60));
                return (nint)((long)(_testProbeDone & 0xFFFF) | (_siteProbePending ? 1L << 16 : 0) | (_siteDiagPending ? 1L << 17 : 0)
                    | ((long)(_testDiagOverlap & 63) << 18) | ((long)(_testDiagDone & 63) << 24) | ((long)(_testProbeStaleKept & 0xFF) << 32) | (1L << 60));

            case Native.WM_TEST_SITE:
                if (!Program.IsTestMode) break;
                return (nint)((long)Math.Min(SiteLinks().Count, 255) | (_siteWatch ? 1L << 8 : 0) | (FillButton.Visible ? 1L << 9 : 0)
                    | (_siteLast?.Url is not null ? 1L << 10 : 0) | ((long)Math.Min(_siteLast?.Hits.Count ?? 0, 255) << 16) | (_fillRunning ? 1L << 24 : 0)
                    | ((long)(_fillToken & 0xFFFF) << 32) | (1L << 60));   // 32–47: 지금 [채우기] 토큰(R60-3 시험: 옛 토큰 클릭)

            case Native.WM_CHIP_COMMAND:
                if ((int)lParam != _chipGen || _chipSlot < 0) return 0;   // 이전 칩의 늦은 명령: 버린다
                if (wParam == 1) { if (Chip.TakePressed((int)lParam, out Injector.Request pressed)) { if (_chipLink) LinkConfirm(pressed); else ChipConfirm(pressed); } }
                else if (_chipSlot >= 0) { ReleaseChip(); ShowMainWindow(); }   // [취소]: 대기를 끝내고 목록으로 돌아간다
                return 0;

            case Native.WM_SHOWME:
                ShowMainWindow();
                return 0;

            case Native.WM_ONEKEY_VERSION:
                return VersionCode;

            case Native.WM_ONEKEY_QUIT:
                ExitApp();
                return 1;

            case Native.WM_TRAY:
            {
                uint ev = (uint)(lParam & 0xFFFF);
                if (ev == Native.WM_LBUTTONUP || ev == Native.WM_LBUTTONDBLCLK) ShowMainWindow();
                else if (ev == Native.WM_CONTEXTMENU || ev == Native.WM_RBUTTONUP) ShowTrayMenu();
                return 0;
            }

            case Native.WM_TIMER:
                OnTimer((nuint)wParam);
                return 0;

            case Native.WM_SYSCOMMAND:
                if ((wParam & 0xFFF0) == Native.SC_MINIMIZE) break;   // 최소화는 작업 표시줄로 (기본 동작)
                break;

            case Native.WM_CLOSE:
                // 잠금 화면의 × (2026-10-03 사용자): 트레이로 숨기지 않고 종료할지 묻는다(아니요면 그대로). − 는 보통 최소화.
                // 잠금을 푼 화면의 × 는 지금처럼 트레이로 숨긴다.
                if (!_exiting && _cur == Screen.Lock && Native.IsWindowVisible(_hwnd))
                {
                    if (Msg(T.ExitConfirmLocked, AppTitle, Native.MB_YESNO | Native.MB_ICONQUESTION | Native.MB_DEFBUTTON2) == Native.IDYES) ExitApp();
                    return 0;
                }
                if (!_exiting) { HideToTray(); return 0; }
                break;

            case Native.WM_DPICHANGED:
            {
                if (WorkArea.IsOverridden) return 0;   // 배율을 흉내 내는 중에는 실제 모니터 배율을 따르지 않는다
                _dpi = (uint)(wParam & 0xFFFF); if (_dpi == 0) _dpi = 96;
                CreateFont();
                nint oldHero = _iconHero; bool oldOwned = _iconHeroOwned;
                _iconHero = LoadAppIcon(64, out _iconHeroOwned);
                if (oldOwned && oldHero != 0 && oldHero != _iconHero) Native.DestroyIcon(oldHero);   // 모니터를 오갈 때마다 아이콘이 쌓이지 않도록
                var suggested = (Native.RECT*)lParam;
                Native.SetWindowPos(hwnd, 0, suggested->left, suggested->top, suggested->right - suggested->left, suggested->bottom - suggested->top, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                RebuildKeepingInput();
                return 0;
            }

            case Native.WM_ACTIVATE:
                // 편집 화면에서는 녹음을 위해 전역 단축키를 풀어 두는데, 창이 뒤로 가거나 트레이로 내려가면 녹음할 일이 없으므로
                // 다시 잡는다 (안 그러면 편집 화면인 채 닫아 둔 동안 단축키가 죽어 있다). 앞으로 오면 다시 푼다.
                if (_cur == Screen.Edit)
                {
                    if ((wParam & 0xFFFF) == 0) RegisterHotkeys(silent: true, quiet: true);
                    else for (int i = 0; i < Config.SlotCount; i++) Native.UnregisterHotKey(_hwnd, i);
                }
                break;

            case Native.WM_WTSSESSION_CHANGE:
                // 사용자가 Windows 를 잠그면(Win+L, 화면 보호기 잠금) 1Key 도 잠근다. 자리를 비운 것이 확실한 순간이다.
                // 화면에 떠 있던 1Key 는 그 자리에서 좁은 잠금 위젯으로 바뀌고, 트레이에 있던 1Key 는 숨은 채 잠긴다(2026-10-05 사용자 — 예전에는 모두 숨겼다).
                if ((uint)wParam == Native.WTS_SESSION_LOCK)
                {
                    bool wasShown = (Native.IsWindowVisible(_hwnd) && !Native.IsIconic(_hwnd))
                                 || (LockWidget.IsShown && Native.IsWindowVisible(LockWidget.Handle) && !LockWidget.IsMinimized);
                    if (Unlocked && _cfg.HasMaster) DoLock();
                    if (!TestOldLockScreen && !_widgetFailed && !_cfg.Unsupported && _cur == Screen.Lock)
                    {
                        if (wasShown && Dialog.OpenCount == 0)
                        {
                            // 떠 있던 위젯은 새로 만든다: 누르던 화살표·입력하던 글자는 버리고(잠근 뒤에 제출되지 않게), 자리는 위젯이 기억한 곳
                            if (LockWidget.IsShown) { _widgetHiddenFor = LockWidget.Instance; LockWidget.Hide(); }
                            if (ShowLockWidget(activate: false)) return 0;
                        }
                        Native.ShowWindow(_hwnd, Native.SW_HIDE); HideLockWidgetForLock();
                    }
                }
                return 0;

            case Native.WM_POWERBROADCAST:
                if ((uint)wParam == Native.PBT_APMSUSPEND)   // 절전에 들어갈 때도 잠근다
                {
                    if (Unlocked && _cfg.HasMaster) DoLock();
                    if (!TestOldLockScreen && !_widgetFailed && !_cfg.Unsupported && _cur == Screen.Lock) { Native.ShowWindow(_hwnd, Native.SW_HIDE); HideLockWidgetForLock(); }
                }
                break;

            case LockWidget.WM_LOCKWIDGET:
                OnLockWidget((int)wParam, (int)lParam);
                return 0;

            case WM_HIDE_WIDGET:
                if ((int)wParam == LockWidget.Instance && _cur == Screen.Lock) LockWidget.Hide();
                return 0;

            case Native.WM_DESTROY:
                CatWidget.Destroy();
                Launcher.Cancel(null);   // 진행 중 실행 요청: 아직 실행 호출 전이면 하지 않는다
                LaunchDrop.Revoke(hwnd);
                ReleaseChip();
                FillButton.Destroy();
                LockWidget.Hide();
                Native.WTSUnRegisterSessionNotification(hwnd);
                RemoveTrayIcon();
                for (int i = 0; i < Config.SlotCount; i++) Native.UnregisterHotKey(hwnd, i);
                Native.PostQuitMessage(0);
                return 0;
        }
        if (msg == _msgTaskbarCreated && msg != 0 && !_exiting)
        {
            // 탐색기(작업 표시줄)가 다시 시작되면 트레이 아이콘이 사라진다. 다시 넣는다.
            _trayAdded = false;
            AddTrayIcon();
            CatWidget.EnvChanged();   // 작업 표시줄이 새로 생겼다: 자리를 다시 맞춘다
            return 0;
        }
        return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void OnCommand(int id, int code)
    {
        // 입력칸에 커서가 들어오거나 나가면 그 테두리(본창이 그림)를 다시 그린다
        if (code is 0x0100 or 0x0200 && C(id) is nint fe && fe != 0 && _page.FieldEdits.Contains(fe)) Native.InvalidateRect(_hwnd, 0, false);
        MarkActivity();

        // 편집 화면에서 값이 바뀌었는지 (뒤로 갈 때 물어보기 위해)
        // 드롭다운은 값이 바뀌면 BN_CLICKED 를 보낸다. 화면을 다시 만들면 그 드롭다운도 없어지므로 알림을 다 처리한 뒤로 미룬다
        if (_cur == Screen.Edit && id == IdEMode && code == (int)Native.BN_CLICKED) { Native.PostMessageW(_hwnd, WM_EDIT_MODE, Dropdown.Get(C(IdEMode)), 0); return; }
        if (code == (int)EN_CHANGE)   // 비밀번호·확인 칸: 일치 여부 줄
        {
            if (_cur == Screen.BackupMake && id is IdBkPw1 or IdBkPw2) UpdateMatchNote(IdBkMatch, IdBkPw1, IdBkPw2);
            else if (_cur == Screen.Restore && id is IdRsNew1 or IdRsNew2) UpdateMatchNote(IdRsMatch, IdRsNew1, IdRsNew2);
            else if (_cur == Screen.Master && id is IdMNew1 or IdMNew2) UpdateMatchNote(IdMMatch, IdMNew1, IdMNew2);
        }
        if (_cur == Screen.Edit && id is IdEName or IdEPw or IdEHotkey or IdEEnter or IdEMethod or IdEAllowClick or 331 or 332 or 333
            && code is (int)Native.BN_CLICKED or (int)EN_CHANGE or (int)CBN_SELCHANGE)
            _editDirty = true;
        if (Program.IsTestMode && _cur == Screen.Lock && id is IdLockPw1 or IdLockPw2 && code is 0x0100 or 0x0200)
            Injector.TestTrace($"lockfield id={id} code={(code == 0x0100 ? "set" : "kill")} focus={(Native.GetFocus() == C(IdLockPw1) ? 101 : Native.GetFocus() == C(IdLockPw2) ? 102 : 0)}");
        if (_cur == Screen.Lock && id is IdLockPw1 or IdLockPw2 && code == 0x0100 /* EN_SETFOCUS */) KbdToEnglish(C(id));
        if (_cur == Screen.Edit && id == IdEAllowClick && code == (int)Native.BN_CLICKED) { _editNoClick = !GetCheck(IdEAllowClick); Native.PostMessageW(_hwnd, WM_EDIT_REBUILD, 0, 0); }   // 고급 머리 "설정 있음"
        if (_cur == Screen.Edit && id == IdEPerInput && code == (int)Native.BN_CLICKED) { _editPerInput = GetCheck(IdEPerInput); _editDirty |= false; Native.PostMessageW(_hwnd, WM_EDIT_REBUILD, 0, 0); }
        if (_cur == Screen.Edit && id is IdEEnter or IdEMethod) { UpdateEditNotes(); UpdateSiteRow(); }
        if (_cur == Screen.Edit && id == IdEMethod && (Dropdown.Get(C(IdEMethod)) == (int)InputMethod.Clipboard) != _editClipNote)
        {
            _editClipNote = !_editClipNote;
            Native.PostMessageW(_hwnd, WM_EDIT_REBUILD, 0, 0);   // 드롭다운 알림이 끝난 뒤
        }
        if (_cur == Screen.Settings && id is IdAutoLock or IdAdmin) UpdateSettingsNotes();
        // (B안 0.3.57: 관리자 권한 안내 줄을 없애 스위치를 바꿔도 화면을 다시 만들지 않는다 — 안내는 풍선)
        if (_cur == Screen.Settings && id == IdTheme) { PreviewTheme(); return; }
        if (_cur == Screen.Settings && id == IdLang) { PreviewLanguage(); return; }
        if (id == IdHelp) { ShowHelp(); return; }
        if (id == Native.IDCANCEL && _cur == Screen.Support) { ShowScreen(Screen.Settings); return; }   // Esc: 후원 화면에서 설정으로
        if (id >= IdTile && id < IdTile + LaunchStore.Max) { if (_cur == Screen.List) OnTile(id - IdTile); return; }
        if (_cur == Screen.PickProgram && id >= IdPkRow && id < IdPkRow + PickMax) { OnPickRow(id - IdPkRow); return; }
        if (_cur == Screen.PickProgram && id == IdPkSearch && code == (int)EN_CHANGE)
        {
            string f = Native.GetWindowText(C(IdPkSearch)).Trim();
            if (f != _pickFilter) { _pickFilter = f; Native.PostMessageW(_hwnd, WM_PICK_REBUILD, 0, 0); }   // 칸 알림이 끝난 뒤
            return;
        }

        switch (id)
        {
            // 추가 종류 고르기·실행 항목 편집·아이콘 띠
            case IdAkBack: ShowScreen(Screen.List); return;
            case IdAkPassword or IdAkLogin or IdAkMulti:
                for (int i = 0; i < Config.SlotCount; i++)
                    if (!SlotInUse(_cfg.Slots[i])) { OpenEdit(i, id == IdAkLogin ? FormLogin : id == IdAkMulti ? FormMulti : FormPhrase); return; }
                return;
            case IdAkProgram: OnAddProgram(); return;
            case IdAkFolder: OnAddFolder(); return;
            case IdAkWeb: OnAddWeb(); return;
            case IdLeTest: TestWebEdit(); return;
            case IdLeColor: OnWebColor(); return;
            case IdLeBack: LeaveLaunchEdit(ask: true); return;
            case IdLeCancel: LeaveLaunchEdit(ask: false); return;
            case IdLeSave: SaveLaunchEdit(); return;
            case IdLeDelete: DeleteLaunchEdit(); return;
            case IdLeLeft: MoveLaunchEdit(-1); return;
            case IdLeRight: MoveLaunchEdit(+1); return;
            case IdLeRepick: RepickLaunchEdit(); return;
            case IdPkBack: LeavePick(); return;
            case IdPkBrowse: OnPickBrowse(); return;
            case IdLeIcon: ChangeLaunchIcon(); return;
            case IdLeIconReset: ResetLaunchIcon(); return;
            case IdStripReset: ResetLaunchList(); return;
            case IdStripPrev: StripPage(0, -1); return;
            case IdStripNext: StripPage(0, +1); return;
            case IdStripPrev2: StripPage(1, -1); return;
            case IdStripNext2: StripPage(1, +1); return;
            case IdStripPrev3: StripPage(2, -1); return;
            case IdStripNext3: StripPage(2, +1); return;
            case IdStripEdit: _stripEdit = !_stripEdit; RefreshList(); if (C(IdStripEdit) is nint eb and not 0) Native.SetFocus(eb); return;
            // 잠금
            case IdLockBtn: OnLockButton(); return;
            case IdTrayRestore: StartRestore(); return;   // 트레이 메뉴와 같은 명령(시험이 WM_COMMAND 로 보낸다) — StartRestore 가 상태를 다시 본다

            // 목록
            case IdListLock: LockNow(); return;
            case IdListMin: if (WidgetButton) HideToTray(); else Native.ShowWindow(_hwnd, Native.SW_MINIMIZE); return;
            case IdRowSettings: ShowScreen(Screen.Settings); return;
            case IdListAutoLock:   // 목록 판의 자동 잠금 칸: 설정 화면의 자동 잠금으로
                ShowScreen(Screen.Settings);
                if (C(IdAutoLock) is nint al and not 0) { Ctl.ShowFocus = true; Native.SetFocus(al); }
                return;
            case IdSBack: LeaveSettings(); return;        // 바뀐 값이 있으면 버릴지 묻는다
            case IdSCancel: DiscardSettings(); return;    // [취소]는 묻지 않고 버린다
            case IdListExit: ConfirmExit(); return;
            case IdRowAdd: ShowScreen(Screen.AddKind); return;   // 비밀번호·프로그램·폴더 가운데 고르기(설계 v2 5장)
            case IdListClear:
                _listFilter = ""; _listTop = 0;
                ShowScreen(Screen.List);
                if (C(IdListFilter) != 0) Native.SetFocus(C(IdListFilter));
                return;
            case IdConfirmReset:
                HotkeyBox.Set(C(IdConfirmKey), Config.DefaultConfirmMods, Config.DefaultConfirmVk);
                return;
            case IdRowRestart: RestartAsAdmin(); return;
            case IdRowDiag: StartDiagnostics(); return;
            case IdRowSiteDiag: StartSiteDiagnostics(); return;
            case IdRowFillLog:
            {
                // [복사]·[파일로 저장](2026-10-03·10-07 사용자 요청: 로그성 정보는 보낼 수 있게). 값은 원래 없다.
                Dialog.Show(_hwnd, FillLog.Describe(), T.AdvFillLogRow, Native.MB_OK | Native.MB_ICONINFORMATION, share: "fill");
                return;
            }
            case IdESiteBtn: case 345: case 346: case 347:
            {
                if (_cur != Screen.Edit) return;
                int k = id == IdESiteBtn ? 0 : id - 344;
                if (k >= _editInputs) return;
                if (k == 0 && _editApp is not null) { _editApp = null; _editDirty = true; UpdateSiteRow(); return; }   // [해제]
                if (k > 0 && _editApp is not null) return;   // 꺼 둔 버튼(입력 1 이 프로그램 연결)
                bool pair = k == 0 && _editInputs == 2 && !_editPerInput;   // [연결] 한 번 = 짝 연결, [해제]도 둘 다
                if (_editSites[k] is null && !(pair && _editSites[1] is not null)) StartLinkChip(k);
                else { _editSites[k] = null; if (pair) _editSites[1] = null; _editDirty = true; UpdateSiteRow(); }
                return;
            }
            case IdEInAdd: ChangeInputs(-1); return;
            case 336: case 337: case 338: ChangeInputs(id - 335); return;
            case IdRowAdvanced: ShowScreen(Screen.Advanced); return;
            case IdRowSupport: ShowScreen(Screen.Support); return;
            case IdSupBack: case IdSupClose: ShowScreen(Screen.Settings); return;
            case IdSupOpen: OpenSupportLink(SupportLinks.IxKakaoPay); return;
            case IdSupGitHub: OpenSupportLink(SupportLinks.IxGitHub); return;
            case IdSupCoupang: ShowAffiliate(SupportLinks.IxCoupang); return;
            case IdSupMyRealTrip: ShowAffiliate(SupportLinks.IxMyRealTrip); return;
            case IdSave: SaveList(); return;

            // 편집
            case IdEBack: case IdECancel: LeaveEdit(); return;
            case IdESave: SaveEdit(); return;
            case IdEDelete: DeleteEdit(); return;
            case IdEShow: TogglePasswordVisible(); return;
            case IdETest: StartTestFromEdit(); return;

            // 고급
            // 설정에서 들어간 화면의 뒤로·취소는 직전 화면인 설정으로(2026-10-06 사용자). 설정의 저장 전 변경(초안)은 그대로 보인다
            case IdABack: case IdACancel: ShowScreen(Screen.Settings); return;
            case IdASave: SaveAdvanced(); return;

            // 마스터 바꾸기 (T3)
            case IdRowMaster: ShowScreen(Screen.Master); return;
            case IdMBack: case IdMCancel: ShowScreen(Screen.Settings); return;
            case IdMSave: SaveMaster(); return;
            case IdRowBackup: StartBackupMake(); return;
            case IdRowRestore: StartRestore(); return;
            case IdBkBack: case IdBkCancel: _restoreAfterBackup = false; ShowScreen(Screen.Settings); return;
            case IdBkSave: SaveBackupMake(); return;
            case IdRsBack: case IdRsCancel: LeaveRestore(); return;
            case IdRsBrowse: BrowseRestore(); return;
            case IdRsSave: DoRestore(); return;

            case IdUnsupportedExit: ExitApp(); return;
        }

        if (id == IdListFilter && code == (int)EN_CHANGE)
        {
            string f = Native.GetWindowText(C(IdListFilter));
            if (f != _listFilter)
            {
                _listFilter = f; _listTop = 0;
                Native.PostMessageW(_hwnd, Native.WM_LIST_REFRESH, 0, 0);   // 알림을 보낸 EDIT 를 지금 부수지 않도록 미룬다
            }
            return;
        }
        if (id >= IdRowSlot && id < IdRowSlot + Config.SlotCount && code == Native.BN_CLICKED) OpenEdit(id - IdRowSlot);
        else if (id >= IdRowInput && id < IdRowInput + Config.SlotCount && code == Native.BN_CLICKED && _cur == Screen.List) StartChip(id - IdRowInput);
        // 트레이 메뉴 항목과 같은 동작 (메뉴는 TPM_RETURNCMD 로 직접 처리하지만, 같은 id 의 명령도 같게 받는다: 검증 하네스용)
        else if (id >= IdTraySlot && id < IdTraySlot + Config.SlotCount) TrayChip(id - IdTraySlot);
    }

    private void OnTimer(nuint id)
    {
        if (id == TimerAutoLock) { CheckAutoLock(); SyncWalker(); return; }
        if (id == TimerAnim) { StepHeightAnimation(); return; }
        if (id == TimerSite) { SiteTick(); return; }
        if (id == TimerKbd) { UpdateKbd(); return; }
        if (id == TimerUsage) { UpdateUsage(); return; }   // 반복: 고급 화면의 사용량 1초마다(화면을 떠나면 스스로 끈다)   // 반복 타이머(아래의 한 번짜리 끄기 앞에서): 잠금 화면 키보드 상태 0.25초
        Native.KillTimer(_hwnd, id);
        if (id == TimerChipExpire)
        {
            if (_chipSlot < 0) return;
            bool wasLink = _chipLink;
            ReleaseChip();
            if (wasLink) ShowMainWindow();   // 입력란 연결을 기다리다 끝나면 편집하던 화면으로 돌아간다
            else ShowBalloon(AppTitle, T.ChipExpired(ChipSeconds), Native.NIIF_INFO);
            return;
        }
        if (id == TimerChipHint) { if (_chip != 0) Chip.SetHint(_chip, _chipHint, warn: false); return; }
        if (id == TimerChipDone)
        {
            Native.KillTimer(_hwnd, TimerChipDone);
            if (_chip != 0 && _chipLink) { ReleaseChip(); ShowMainWindow(); UpdateSiteRow(); }
            return;
        }
        if (id == TimerDiag)
        {
            if (!_diagPending) return;   // 잠금 등으로 취소된 대기의 늦은 알림: 지금 유효한 대기가 아니다
            _diagPending = false;
            if (!Unlocked) return;
            string report = Diag.Inspect(Native.GetForegroundWindow());
            Dialog.Show(_hwnd, report, T.DiagTitle, Native.MB_OK | Native.MB_ICONINFORMATION, share: "diag");
            return;
        }
        if (id == TimerSiteDiag)
        {
            if (!_siteDiagPending) return;
            // 시험 장벽(ONEKEY_TEST_UIA_DELAY_MS 일 때만): 주기적 확인이 대기 중일 때까지 0.1초씩 미뤄 진단이 반드시 그 확인과 겹치게 한다 (C61-4)
            if (Uia.TestProbeDelayMs > 0 && !_siteProbePending && ++_testDiagWaits < 60) { Native.SetTimer(_hwnd, TimerSiteDiag, 100, 0); return; }
            _testDiagWaits = 0;
            Native.KillTimer(_hwnd, TimerSiteDiag);
            _siteDiagPending = false;
            if (!Unlocked) return;
            if (_siteProbePending) _testDiagOverlap++;
            nint fg = Native.GetForegroundWindow();
            int gen = ++_reqGen;
            Uia.Post(_hwnd, job => Uia.Describe(fg), 5000, null, (ok, rep) =>
            {
                if (gen != _reqGen || !Unlocked) return;   // 그 사이 잠겼다
                _testDiagDone++;
                Dialog.Show(_hwnd, ok ? rep : T.DiagSiteUnavailable, T.DiagSiteTitle, Native.MB_OK | Native.MB_ICONINFORMATION, share: "site");
            });
            return;
        }
        if (id == TimerTest)
        {
            Slot? t = _testSlot; _testSlot = null;
            if (t is not null && _cur == Screen.Edit && Unlocked) Inject(t, Injector.Capture());   // 편집 화면을 떠났거나 잠겼으면 넣지 않는다
        }
    }

    private void StartDiagnostics()
    {
        int r = Msg(T.DiagGuide, T.DiagTitle, Native.MB_OK | Native.MB_ICONINFORMATION);
        if (r != Native.IDOK || !Unlocked) return;   // 안내가 떠 있는 동안 잠겨 닫혔다: 진단을 시작하지 않는다
        _diagPending = true;
        Native.SetTimer(_hwnd, TimerDiag, 3000, 0);
    }

    private void OfferElevation()
    {
        if (Native.IsElevated()) return;
        int r = Msg(
            T.ElevOffer,
            AppTitle, Native.MB_YESNO | Native.MB_ICONQUESTION);
        if (r == Native.IDYES) RestartAsAdmin();
    }

    private void RestartAsAdmin()
    {
        // 검증용(ONEKEY_TEST=1, ONEKEY_TEST_FAIL=admin:dry-run): UAC 를 띄우지 않고 불린 횟수만 센다(잠금 뒤 [예]가 재시작으로 이어지는지 시험).
        if (Program.TestFails("admin:dry-run")) { _testRestartCalls++; return; }
        if (Native.IsElevated()) { Toast.Show(_hwnd, T.ElevAlready); return; }
        if (!Unlocked) { ShowMainWindow(); return; }
        _cfg.Save();
        // T2: 잠금을 쥔 채 자식에게 넘기고, 자식 프로세스가 만들어진 것을 확인한 뒤에만 끝낸다.
        if (!Autostart.RestartElevated(Program.HandoffArg))
        {
            Msg(T.ElevFailed, AppTitle, Native.MB_OK | Native.MB_ICONWARNING);
            return;
        }
        ExitApp();
    }
}
