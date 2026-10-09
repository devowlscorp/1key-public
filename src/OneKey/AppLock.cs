using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>App 의 일부: 잠금: 창 안 잠금 화면, 잠금 위젯 연결, 잠그기·풀기. (App.cs 에서 화면별로 나눔, 0.3.9 — 동작은 그대로)</summary>
internal sealed unsafe partial class App
{
    // ---------------------------------------------------------------- 잠금 화면

    private nint _kbdIme, _kbdCaps;
    private int _testCapsTaps;
    private int _testHotkeyProbed, _testHotkeyNotice;   // 연결 항목 단축키: 페이지 찾기가 돌아온 횟수·마지막 결과(0 채우기 시작, 1 창이 앞이 아님, 2 칸을 찾지 못함, 3 잠김·바쁨, 4 같은 페이지 여럿, 5 페이지 없음)   // 시험 관찰: 잠금 화면에서 CapsLock 을 끈 횟수
    private (int Native, int Caps) _kbdShown = (-1, -1);

    /// <summary>잠금 화면의 키보드 상태 줄을 지금 상태로(바뀐 때만 다시 그림). 그 화면이 아니면 타이머를 멈춘다.</summary>
    private void UpdateKbd()
    {
        if (_cur != Screen.Lock || LockWidget.IsShown || _kbdIme == 0 || !Native.IsWindow(_kbdIme)) { Native.KillTimer(_hwnd, TimerKbd); _kbdIme = _kbdCaps = 0; return; }
        nint f = Native.GetFocus();
        nint field = f == C(IdLockPw2) && f != 0 ? f : C(IdLockPw1);
        int native = KbdNative(field) ? 1 : 0, caps = (Native.GetKeyState(0x14) & 1) != 0 ? 1 : 0;
        if (_kbdShown == (native, caps)) return;
        _kbdShown = (native, caps);
        Native.SetText(_kbdIme, native == 1 ? T.LockKbdNative : T.LockKbdEnglish);
        Native.SetText(_kbdCaps, caps == 1 ? T.LockCapsOn : T.LockCapsOff);
        _staticStyle[_kbdIme] = (Theme.BgBrush, native == 1 ? Theme.DangerText : Metal.Ref(Metal.InkNote(Theme.IsDark)));
        _staticStyle[_kbdCaps] = (Theme.BgBrush, caps == 1 ? Theme.DangerText : Metal.Ref(Metal.InkNote(Theme.IsDark)));
        Native.InvalidateRect(_kbdIme, 0, true); Native.InvalidateRect(_kbdCaps, 0, true);
    }

    /// <summary>그 칸에서 지금 한글(IME 네이티브) 입력인가. IME 문맥이 없으면 영문으로 들어가므로 false.</summary>
    private static bool KbdNative(nint field)
    {
        if (field == 0) return false;
        nint h = Native.ImmGetContext(field);
        if (h == 0) return false;
        try { return Native.ImmGetOpenStatus(h) && Native.ImmGetConversionStatus(h, out uint conv, out _) && (conv & 1 /* IME_CMODE_NATIVE */) != 0; }
        finally { Native.ImmReleaseContext(field, h); }
    }

    /// <summary>
    /// 잠금 화면의 비밀번호 칸에 커서가 들어옴: 1Key 창이 앞에 있을 때만 그 칸의 IME 를 영문으로, CapsLock 이 켜져 있으면 끈다(키 하나).
    /// 다른 프로그램을 쓰는 중에 자동 잠금으로 이 화면이 만들어져도 사용자의 키보드 상태를 건드리지 않는다.
    /// </summary>
    private void KbdToEnglish(nint field)
    {
        bool Front() => LockWidget.IsShown ? LockWidget.OwnsForeground() : Native.GetForegroundWindow() == _hwnd;   // 위젯이면 위젯·입력 창이 앞일 때
        if (field == 0 || !Front()) return;
        nint h = Native.ImmGetContext(field);
        if (h != 0)
        {
            try
            {
                if (Native.ImmGetConversionStatus(h, out uint conv, out uint sent) && (conv & 1) != 0) Native.ImmSetConversionStatus(h, conv & ~1u, sent);
                if (Native.ImmGetOpenStatus(h)) Native.ImmSetOpenStatus(h, false);
            }
            finally { Native.ImmReleaseContext(field, h); }
        }
        // CapsLock 은 Windows 전체 상태라 끄면 다른 프로그램에서도 꺼진 채다(Codex 08:45-bb). 보내기 직전에 다시 본다:
        // 1Key 창이 앞이고 커서가 그 칸이며, CapsLock·Shift·Ctrl·Alt·Win 이 손으로 눌려 있지 않을 때만 한 번. 실패해도 다시 보내지 않고 표시로 알린다.
        static bool Held(int vk) => (Native.GetAsyncKeyState(vk) & 0x8000) != 0;
        if (Program.IsTestMode)
            Injector.TestTrace($"kbd field={(field == LockField(0) ? 1 : field == LockField(1) ? 2 : 0)} caps={Native.GetKeyState(0x14) & 1} fg={(Front() ? 1 : 0)} focus={(Native.GetFocus() == field ? 1 : 0)} held={(Held(0x14) ? "C" : "")}{(Held(0x10) ? "S" : "")}{(Held(0x11) ? "T" : "")}{(Held(0x12) ? "A" : "")}{(Held(0x5B) || Held(0x5C) ? "W" : "")}");
        if ((Native.GetKeyState(0x14) & 1) != 0 && Front() && Native.GetFocus() == field
            && !Held(0x14) && !Held(0x10) && !Held(0x11) && !Held(0x12) && !Held(0x5B) && !Held(0x5C))
            _testCapsTaps += Injector.TapCapsLock() ? 1 : 0;
        UpdateKbd();
    }

    private void BuildLock()
    {
        // 새 디자인(2026-10-08, 잠금 위젯 시안 Lock-light/dark.dc.html 과 같은 말): 큰 둥근 단추(고양이 윤곽 + 자물쇠) · 말풍선 판(제목·안내) ·
        // 금속 테의 둥근 입력 알약(한/영·Caps 는 알약 안 오른쪽) · 강조색 [잠금 해제] 알약 · 오른쪽 위 버전.
        _page.Metal = Theme.MetalPage = true;
        uint sub = Metal.Ref(Metal.InkLabel(Theme.IsDark)), ink = Metal.Ref(Metal.Ink(Theme.IsDark));
        Label("v" + Version, WinW - Margin - 90, 8, 90, 18, Theme.Sized(11, false), sub, false, Native.SS_RIGHT);
        _page.Hero = (WinW / 2, 34, 76);
        int y = 128;
        if (_cfg.Unsupported)
        {
            // T9: 더 새 1Key 가 저장한 형식. 열지도, 고치지도, 새 마스터를 만들지도 않는다. 할 수 있는 것은 종료뿐이다.
            Label(T.LockUnsupportedTitle, 0, y, WinW, 28, Theme.FontTitle, ink, false, Native.SS_CENTER);
            Label(T.LockUnsupportedBody(Version, _cfg.UnsupportedReason ?? ""),
                  Margin, y + 36, CardW, 64, Theme.FontSmall, sub, false, Native.SS_CENTER, vcenter: false);
            BarPill(IdUnsupportedExit, T.CommonExit, Btn.Prominent, 100, y + 112, 220, isDefault: true);
            _page.DefaultButton = IdUnsupportedExit;
            _page.Height = y + 112 + MetalUi.PillMainH + Margin + 16;
            return;
        }
        string t1 = _createMode ? T.LockCreateTitle : T.LockLockedTitle, t2 = _createMode ? T.LockCreateSub : T.LockLockedSub;
        nint f1 = Theme.Sized(14.5, true), f2 = Theme.Sized(12, false);
        int bw = Math.Min(WinW - 2 * Margin, Math.Max(LabelW(f1, t1), LabelW(f2, t2)) + 48), bx = (WinW - bw) / 2;
        _page.Bubble = (bx, y, bw, 64);
        Label(t1, bx + 8, y + 11, bw - 16, 24, f1, ink, true, Native.SS_CENTER | Native.SS_ENDELLIPSIS);
        Label(t2, bx + 8, y + 34, bw - 16, 20, f2, sub, true, Native.SS_CENTER | Native.SS_ENDELLIPSIS);

        // 입력 알약: 테 포함 폭 320(가운데), 둥근 파인 칸 48. 첫 칸 오른쪽 안에 한/영·Caps(시안 10px)
        const int PillW = 320, WellH = 48, KbW = 62;
        int px = (WinW - PillW) / 2, wx = px + 5, ww = PillW - 10;
        int fy = y + 64 + 20;
        // 첫 설정은 두 칸 모두 점만 보이게 되므로 칸 위에 고정 라벨을 둔다 (Codex QA-03). 잠금 해제는 칸이 하나라 자리표시자로 충분하다.
        const int LabelH = 18;
        nint lf = Theme.Sized(11.5, false);
        if (_createMode) { Label(T.LockLabelMaster, wx + 16, fy, ww - 32, LabelH, lf, sub, false); fy += LabelH + 6; }
        int fy1 = fy;
        nint e1 = Field(IdLockPw1, wx, fy + 5, ww, WellH, Native.ES_PASSWORD, onCard: false, leftPad: 8, rightPad: KbW);
        _page.PillFields.Add(_page.Fields.Count - 1);
        Native.SetCueBanner(e1, _createMode ? T.LockCueNew : T.LockLabelMaster);
        fy += WellH + 10;
        if (_createMode)
        {
            fy += 10;
            Label(T.LockLabelAgain, wx + 16, fy, ww - 32, LabelH, lf, sub, false); fy += LabelH + 6;
            nint e2 = Field(IdLockPw2, wx, fy + 5, ww, WellH, Native.ES_PASSWORD, onCard: false, leftPad: 8);
            _page.PillFields.Add(_page.Fields.Count - 1);
            Native.SetCueBanner(e2, T.LockLabelAgain);
            fy += WellH + 10;
        }
        // 키보드 상태(2026-10-03 사용자): 첫 칸 오른쪽에 작은 글씨 두 줄(보조 정보 — "영문" / "Caps 꺼짐"). 마스터 비밀번호는 영문·숫자·특수문자만
        // 쓰므로 칸에 들어가면 영문·CapsLock 끔으로 바꾸고(KbdToEnglish), 사용자가 다시 바꾸면 경고색으로 보인다(TimerKbd 0.25초).
        nint kf = Theme.Sized(10, false);
        int kx = wx + ww - KbW - 14, kw = KbW;
        _kbdIme = Label("", kx, fy1 + 5 + WellH / 2 - 14, kw, 14, kf, Metal.Ref(Metal.InkNote(Theme.IsDark)), false, Native.SS_RIGHT | Native.SS_ENDELLIPSIS);
        _kbdCaps = Label("", kx, fy1 + 5 + WellH / 2, kw, 14, kf, Metal.Ref(Metal.InkNote(Theme.IsDark)), false, Native.SS_RIGHT | Native.SS_ENDELLIPSIS);
        Native.SetWindowLongPtrW(_kbdIme, -12 /* GWLP_ID */, IdLockKbdIme); Native.SetWindowLongPtrW(_kbdCaps, -12, IdLockKbdCaps);   // 시험이 글을 읽는다
        _kbdShown = (-1, -1);
        UpdateKbd();
        Native.SetTimer(_hwnd, TimerKbd, 250, 0);
        fy += 14;
        BarPill(IdLockBtn, _createMode ? T.LockStart : T.LockUnlock, Btn.Prominent, 100, fy, 220, isDefault: true);
        _page.DefaultButton = IdLockBtn;

        fy += MetalUi.PillMainH + 16;
        // 잠금 해제 화면의 ×·− 설명 줄은 뺐다(2026-10-04 사용자: 불필요). 처음 설정의 안내만 남긴다
        if (_createMode)
        {
            Label(T.LockCreateNote, 0, fy, WinW, 18, Theme.Sized(11.5, false), sub, false, Native.SS_CENTER);   // 흐린 3단계 회색은 대비 부족 (QA-01)
            fy += 18;
        }
        _page.Height = fy + Margin;
    }

    /// <summary>
    /// 쉬운 마스터 비밀번호에 대한 권고 문구. 저장 파일은 같은 Windows 계정의 다른 프로그램이 읽을 수 있고, 그러면 마스터 후보를
    /// 파일만으로 검사할 수 있으므로(실측: 후보 하나에 수십 ms) 마스터의 경우의 수가 곧 보호 강도다.
    /// 강제하지는 않는다(업무용이라 사용자가 감당할 수 있는 길이를 스스로 정한다). 4자 미만만 막는다. 문제 없으면 null.
    /// </summary>
    private static string? WeakMasterReason(ReadOnlySpan<char> p)
    {
        if (p.Length < 8) return T.WeakShort;
        bool same = true, ascending = true, descending = true;
        for (int i = 1; i < p.Length; i++)
        {
            if (p[i] != p[0]) same = false;
            if (p[i] != p[i - 1] + 1) ascending = false;
            if (p[i] != p[i - 1] - 1) descending = false;
        }
        if (same || ascending || descending) return T.WeakPattern + "\n" + T.WeakWhy;
        // 흔한 비밀번호·자판 순서(2026-10-05 보안 진단 후속으로 넓힘). 들어 있기만 해도 알린다(권고일 뿐, 막지 않는다)
        foreach (string common in new[] { "password", "passw0rd", "qwerty", "11111111", "12341234", "123456", "asdfasdf", "1q2w3e4r", "q1w2e3r4", "1qaz2wsx",
                                          "qwer1234", "asdf1234", "zxcvbnm", "iloveyou", "letmein", "welcome", "abc12345", "saranghae" })
            if (p.Contains(common, StringComparison.OrdinalIgnoreCase)) return T.WeakCommon + "\n" + T.WeakWhy;   // 소문자 사본을 만들지 않는다
        return null;
    }

    private int FirstSlotRowId()
    {
        for (int i = 0; i < Config.SlotCount; i++) if (C(IdRowSlot + i) != 0) return IdRowSlot + i;
        return 0;
    }

    private void CancelTest()
    {
        Native.KillTimer(_hwnd, TimerTest);
        _testSlot = null;
    }

    /// <summary>
    /// 테마·DPI 가 바뀌어 화면을 다시 만들 때, 입력 중이던 값을 잃지 않도록 옮겨 담는다.
    /// (편집 화면의 이름·내용·단축키, 고급 화면의 숫자, 잠금 화면에 치던 비밀번호)
    /// </summary>
    private void RebuildKeepingInput()
    {
        switch (_cur)
        {
            case Screen.Edit:
            {
                RestoreEdit(ReadEdit(), _editDirty, _editPwVisible);
                return;
            }
            case Screen.Advanced:
            {
                string d = Native.GetWindowText(C(IdADelay)), p = Native.GetWindowText(C(IdAPre));
                ShowScreen(Screen.Advanced);
                if (_cur != Screen.Advanced) return;
                Native.SetText(C(IdADelay), d); Native.SetText(C(IdAPre), p);
                return;
            }
            case Screen.Master:
            {
                string a = Native.GetWindowText(C(IdMCur)), b = Native.GetWindowText(C(IdMNew1)), c = Native.GetWindowText(C(IdMNew2));
                ShowScreen(Screen.Master);
                if (_cur != Screen.Master) return;
                Native.SetText(C(IdMCur), a); Native.SetText(C(IdMNew1), b); Native.SetText(C(IdMNew2), c);
                return;
            }
            case Screen.Lock:
            {
                string p1 = Native.GetWindowText(C(IdLockPw1)), p2 = C(IdLockPw2) != 0 ? Native.GetWindowText(C(IdLockPw2)) : "";
                ShowScreen(Screen.Lock);
                Native.SetText(C(IdLockPw1), p1); if (C(IdLockPw2) != 0) Native.SetText(C(IdLockPw2), p2);
                return;
            }
            default:
                ShowScreen(_cur);
                return;
        }
    }

    // ---------------------------------------------------------------- 잠금 위젯 (디자인 개편 B, Codex 16:14 R-W1~5)

    /// <summary>잠금 화면 칸: 위젯이 보이면 위젯의 칸, 아니면 본창(기존 잠금 화면)의 칸. i = 0 첫 칸, 1 둘째 칸(처음 설정).</summary>
    private nint LockField(int i) => LockWidget.IsShown ? LockWidget.Edit(i) : C(i == 0 ? IdLockPw1 : IdLockPw2);

    /// <summary>잠금 화면 칸에 커서. 위젯이면 위젯이 앞의 창일 때만(다른 앱을 쓰는 중이면 건드리지 않는다).</summary>
    private void FocusLockField(int i)
    {
        if (LockWidget.IsShown) LockWidget.FocusEdit(i);
        else if (C(i == 0 ? IdLockPw1 : IdLockPw2) is nint h && h != 0) Native.SetFocus(h);
    }

    /// <summary>잠금 화면에서 띄우는 확인 창: 위젯이 보이면 위젯을 소유자로(위젯 위에 뜸) 띄우고, 떠 있는 동안 위젯 입력을 막는다.</summary>
    private int LockMsg(string text, string title, uint flags)
    {
        if (!LockWidget.IsShown) return Msg(text, title, flags);
        int inst = LockWidget.Instance;
        LockWidget.SetModal(true);
        try { return Dialog.Show(LockWidget.Handle, text, title, flags, share: ShareOf(text)); }
        finally { if (LockWidget.IsShown && LockWidget.Instance == inst) LockWidget.SetModal(false); }
    }

    /// <summary>
    /// 잠겨 있으면 본창 대신 잠금 위젯을 보인다(본창은 숨기고, 그 안의 기존 잠금 화면은 대체 화면으로 남는다). 이미 떠 있으면 앞으로(activate)만.
    /// 위젯을 쓰지 않는 경우(잠금 해제 상태·지원하지 않는 형식·이번 표시 세션에 실패함·시험의 기존 화면 지정)나 만들지 못하면 false.
    /// </summary>
    private bool ShowLockWidget(bool activate)
    {
        if (!OnLockScreen || TestOldLockScreen || _widgetFailed || _cfg.Unsupported) return false;
        if (LockWidget.IsShown && LockWidget.IsCreate == _createMode)
        {
            LockWidget.Collapse();   // 다시 열기·트레이 [잠금]: 넓어져 있었어도 좁게 시작(칸 클릭·키 입력으로 다시 넓어짐)
            if (activate)
            {
                if (LockWidget.IsMinimized) Native.ShowWindow(LockWidget.Handle, Native.SW_RESTORE);
                Native.SetForegroundWindow(LockWidget.Handle);
            }
        }
        else if (!LockWidget.Show(_hwnd, _createMode, activate, _hwnd))
        {
            _widgetFailed = true;   // 처음 그림부터 실패: 입력 전이므로 안내 없이 기존 잠금 화면
            return false;
        }
        Native.KillTimer(_hwnd, TimerKbd);   // 숨은 본창의 키보드 표시 타이머는 쓰지 않는다
        Native.ShowWindow(_hwnd, Native.SW_HIDE);
        if (_lockedWalker) { _lockedWalker = false; SyncWalker(); }   // 잠금 위젯이 다시 열렸다: 잠긴 채 위젯 모드는 끝
        return true;
    }

    /// <summary>
    /// 잠금 위젯이 보이는 중에 그리기가 실패함(R-W4): 잠금은 그대로, 입력 값은 넘기지 않고 빈 기존 잠금 화면으로. 안내는 한 번,
    /// 이번 표시 세션에는 위젯을 다시 시도하지 않는다. 위젯이 앞의 창이었으면 본창을 앞으로(확인 창), 아니면 앞으로 가져오지 않고 풍선으로 알린다.
    /// </summary>
    private void LockWidgetFallback(bool wasFront)
    {
        _widgetFailed = true;
        LockWidget.Hide();
        if (!OnLockScreen) return;
        ShowScreen(Screen.Lock);   // 칸이 빈 새 잠금 화면(처음 설정이면 처음 설정 화면 — 파일은 아직 만들지 않았다)
        if (wasFront)
        {
            Native.ShowWindow(_hwnd, Native.SW_SHOW);
            Native.SetForegroundWindow(_hwnd);
            FocusFirst();
            Msg(T.LockWidgetFallback, AppTitle, Native.MB_OK | Native.MB_ICONINFORMATION);
            FocusFirst();
        }
        else
        {
            Native.ShowWindow(_hwnd, Native.SW_SHOWNOACTIVATE);
            ShowBalloon(AppTitle, T.LockWidgetFallback, Native.NIIF_INFO);
        }
    }

    /// <summary>잠금 위젯의 알림(WM_LOCKWIDGET). lParam 이 지금 위젯 번호가 아니면 이미 닫힌 위젯의 늦은 알림이라 버린다(R-W3).</summary>
    private void OnLockWidget(int code, int instance)
    {
        if (instance != LockWidget.Instance) return;
        if (code == 4)
        {
            // 늦게 온 실패 알림(Codex R148-3): 그사이 잠금이 풀렸으면 버린다(다음 잠금은 위젯을 다시 쓴다). 이 위젯이 Win+L·절전으로
            // 숨겨졌으면 본창을 다시 보이지 않고, 이번 표시 세션에 위젯을 다시 쓰지 않게만 한다 — 다음에 열면 기존 잠금 화면.
            // (위젯은 실패하면 스스로 닫은 뒤 알리므로 IsShown 으로는 가를 수 없다 — 잠금으로 숨긴 위젯 번호로 가른다.)
            if (!OnLockScreen) return;
            if (_widgetHiddenFor == instance) { _widgetFailed = true; return; }
            LockWidgetFallback(LockWidget.FailedWhileFront);
            return;
        }
        if (!LockWidget.IsShown || !OnLockScreen) return;
        if (code == 1) { if (!_lockBusy && !LockWidget.IsModal) OnLockButton(); }
        else if (code == 2)
        {
            if (_lockBusy || LockWidget.IsModal) return;
            _lockBusy = true;
            try { if (LockMsg(T.ExitConfirmLocked, AppTitle, Native.MB_YESNO | Native.MB_ICONQUESTION | Native.MB_DEFBUTTON2) == Native.IDYES) ExitApp(); }
            finally { _lockBusy = false; }
            if (LockWidget.IsShown) FocusLockField(Native.GetFocus() == LockWidget.Edit(1) && LockWidget.Edit(1) != 0 ? 1 : 0);
        }
        else if (code == 5)
        {
            // 위젯 모드로 내려가기(잠금 위젯의 위젯 단추): 잠금은 그대로, 위젯을 닫고 작업 표시줄 마스코트
            if (_lockBusy || LockWidget.IsModal || _createMode) return;
            LockWidget.Hide();
            _lockedWalker = true;
            SyncWalker();
        }
        else if (code == 3)
        {
            nint f = Native.GetFocus();
            if (f != 0 && (f == LockWidget.Edit(0) || f == LockWidget.Edit(1))) KbdToEnglish(f);
        }
    }

    /// <summary>
    /// 잠금(Win+L·절전·자동 잠금)으로 모든 창을 숨길 때 잠금 위젯도: 위젯 위의 확인 창은 잠금 취소로 닫고(IDLOCKED), 그 루프가 풀린 뒤 위젯을 닫는다.
    /// 다시 보이는 것은 사용자가 트레이·작업 표시줄·단축키로 열 때뿐이다(B-W03).
    /// </summary>
    private void HideLockWidgetForLock()
    {
        _widgetHiddenFor = LockWidget.Instance;   // 이 위젯의 늦은 실패 알림은 본창을 다시 띄우지 않는다(R148-3, 실패해 스스로 닫힌 직후의 잠금 포함)
        if (!LockWidget.IsShown) return;
        if (Dialog.OpenCount > 0)
        {
            Dialog.CancelAllForLock();
            Native.PostMessageW(_hwnd, WM_HIDE_WIDGET, LockWidget.Instance, 0);
        }
        else LockWidget.Hide();
    }

    /// <summary>잠금 위젯으로 잠금을 풀었거나 처음 설정을 마친 뒤: 위젯을 닫고 본창(목록)을 보인다. 트레이로 내릴 차례면 보이지 않는다.</summary>
    private void AfterLockWidgetDone()
    {
        _widgetFailed = false;   // 다음 잠금에서는 위젯을 다시 쓴다
        if (!LockWidget.IsShown) return;
        // 본창은 마스코트가 있던 자리에 나온다(2026-10-04 사용자: 잠금을 풀면 본창이 늘 같은 자리로 옮겨 갔다). 잠글 때 위젯이 본창
        // 가운데에 뜨는 것(LockWidget.AnchorAtWindow)과 반대 방향: 위젯 가운데 = 본창 가운데, 작업 영역 밖이면 안으로.
        if (Native.GetWindowRect(LockWidget.Handle, out Native.RECT wr)) CenterMainAt((wr.left + wr.right) / 2, (wr.top + wr.bottom) / 2);
        LockWidget.Hide();
        if (_hideAfterUnlock) return;   // 바로 아래에서 트레이로 내린다
        Native.ShowWindow(_hwnd, Native.SW_SHOW);
        Native.SetForegroundWindow(_hwnd);
        FocusFirst();
    }

    /// <summary>본창 가운데를 화면 좌표 (cx, cy) 에 맞춘다. 그 모니터의 작업 영역 밖으로 나가면 안으로(창이 숨어 있어도 된다).</summary>
    private void CenterMainAt(int cx, int cy)
    {
        if (!Native.GetWindowRect(_hwnd, out Native.RECT r)) return;
        int w = r.right - r.left, h = r.bottom - r.top;
        int x = cx - w / 2, y = cy - h / 2;
        Native.SetWindowPos(_hwnd, 0, x, y, 0, 0, Native.SWP_NOSIZE_ | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        Native.RECT work = WorkArea.For(_hwnd);   // 옮긴 자리의 모니터
        int nx = Math.Clamp(x, work.left, Math.Max(work.left, work.right - w));
        int ny = Math.Clamp(y, work.top, Math.Max(work.top, work.bottom - h));
        if (nx != x || ny != y) Native.SetWindowPos(_hwnd, 0, nx, ny, 0, 0, Native.SWP_NOSIZE_ | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    private int _testSubmitCalls, _testSubmits, _testFireCalls, _testLaunchCalls, _testKdfStarted, _testKdfDone, _testKdfDiscarded, _testKdfFailed;
    private void TestKdfCount(string name, ref int n) { if (Program.IsTestMode) Native.SetPropW(_hwnd, name, ++n); }   // 검증 전용: 잠금 해제 요청이 들어온 횟수 / 실제로 처리한 횟수(창 속성으로 시험이 읽는다, Codex R172 IX03)

    private void OnLockButton()
    {
        if (Program.IsTestMode) Native.SetPropW(_hwnd, "OneKeyTestSubmitCalls", ++_testSubmitCalls);
        if (_lockBusy || _recoveryOnly) return;   // 확인 창이 떠 있거나 키 계산 중에 다시 눌린 Enter·화살표(R-W3)
        if (Program.IsTestMode) Native.SetPropW(_hwnd, "OneKeyTestSubmits", ++_testSubmits);
        _lockBusy = true;
        bool pending = false;
        try { pending = OnLockButtonCore(); }
        finally { if (!pending) _lockBusy = false; }   // 계산을 넘겼으면 끝난 뒤(OnKdfDone) 푼다
    }

    /// <summary>계산을 작업 스레드로 넘겼으면 true(그때는 p1 의 소유도 넘어갔다).</summary>
    private bool OnLockButtonCore()
    {
        // 칸의 마스터는 문자열을 만들지 않고 소유한 고정 버퍼로 읽고, 다 쓰면 지운다(Codex 15:19 S36-1). 입력칸 안의 버퍼는 칸을 비우거나 닫을 때까지 남는다
        var p1 = SecretText.FromWindow(LockField(0));
        SecretText? p2 = null;
        bool handed = false;
        try { handed = OnLockButtonCore(p1, ref p2); return handed; }
        finally { p2?.Dispose(); if (!handed) p1.Dispose(); }
    }

    private bool OnLockButtonCore(SecretText p1, ref SecretText? p2)
    {
        if (p1.Length == 0) { FocusLockField(0); return false; }

        if (_createMode)
        {
            p2 = SecretText.FromWindow(LockField(1));
            if (!p1.Span.SequenceEqual(p2.Span)) { LockMsg(T.LockMismatch, AppTitle, Native.MB_OK | Native.MB_ICONWARNING); Native.SetText(LockField(1), ""); FocusLockField(1); return false; }
            if (p1.Length < 4) { LockMsg(T.MasterTooShort, AppTitle, Native.MB_OK | Native.MB_ICONWARNING); FocusLockField(0); return false; }
            if (WeakMasterReason(p1.Span) is string weak)
            {
                // 권고만 한다. 그래도 쓰겠다고 하면 받아들인다.
                int r = LockMsg(weak + "\n\n" + T.LockUseAnyway, AppTitle, Native.MB_YESNO | Native.MB_ICONWARNING | Native.MB_DEFBUTTON2);
                if (r != Native.IDYES) { FocusLockField(0); return false; }
            }
            if (!_createMode || _cfg.HasMaster) return false;   // 확인 창이 떠 있는 사이 상태가 바뀌었다(다른 경로로 만들어짐 등)
            // 키 유도(Argon2id)는 작업 스레드에서 — 창이 멈추지 않게(Codex 16:22 R39-3). 반영은 끝난 뒤 UI 스레드에서
            RunKdf(() => { try { return Config.DeriveNewMaster(p1.Span); } finally { p1.Dispose(); } },
                   FinishCreateMaster, k => { if (k is { } kk) Crypto.Wipe(kk.Key); }, () => _lockBusy = false);
            return true;
        }

        bool isWeak = WeakMasterReason(p1.Span) is not null;
        if (_cfg.BeginUnlock() is not Config.UnlockJob job) { ShowUnlockFailure(false); return false; }
        RunKdf(() => { try { job.Run(p1.Span); } finally { p1.Dispose(); } return job; },
               j => FinishUnlockUi(j, isWeak), j => j.Dispose(), () => _lockBusy = false);
        return true;
    }

    /// <summary>새 마스터 키 계산이 끝났다(UI 스레드).</summary>
    private void FinishCreateMaster((byte[] Key, byte[] Salt)? k)
    {
        if (k is not { } kk) { LockMsg(T.LockKdfFailed, AppTitle, Native.MB_OK | Native.MB_ICONERROR); FocusLockField(0); return; }
        if (!_createMode || _cfg.HasMaster) { Crypto.Wipe(kk.Key); return; }   // 그사이 상태가 바뀌었다
        _cfg.CommitCreate(kk);
        if (!_cfg.Save())
        {
            // 저장이 안 됐는데 넘어가면, 마스터가 없는 채로 잠금 화면이 아무 비밀번호나 받아들이게 된다.
            _cfg.DiscardMaster();
            LockMsg( SaveFailMsg(), AppTitle, Native.MB_OK | Native.MB_ICONERROR);
            return;
        }
        _createMode = false;
        MarkActivity();
        ShowScreen(Screen.List);
        AfterLockWidgetDone();
    }

    /// <summary>잠금 해제 계산이 끝났다(UI 스레드): 모두 준비됐을 때만 한 번에 반영(Config.FinishUnlock).</summary>
    private void FinishUnlockUi(Config.UnlockJob job, bool weak)
    {
        bool failed = job.Failed;
        if (_cfg.FinishUnlock(job))
        {
            MarkActivity();
            if (_cfg.AutoLockMinutes > 0) CatGrowth.AwardHabit(FlipClock.MascotLight(), CatGrowth.Habit.AutoLock);   // 고양이 보안 습관 보상(하루 한 번)
            MaybeStamp();   // 출근 도장(그날 처음 잠금을 풀 때)
            RegisterHotkeys(silent: true);   // 잠긴 채 시작했으면 아직 등록되지 않은 단축키가 있다
            ShowScreen(Screen.List);
            AfterLockWidgetDone();
            // T3: 쉬운 마스터면 실행 세션마다 한 번 바꾸기를 권한다(강제하지 않음).
            bool weakNow = !_weakMasterAdvised && weak;
            if (weakNow) _weakMasterAdvised = true;
            if (_cfg.KdfUpgraded)   // 예전 방식 파일을 Argon2id 로 옮겼다: 한 번 알린다(2026-10-05 사용자 결정)
            {
                _cfg.AckKdfUpgraded();
                ShowBalloon(AppTitle, T.BalloonKdfUpgraded + (weakNow ? "\n" + T.BalloonWeakMaster : ""), Native.NIIF_INFO);
            }
            else if (weakNow) ShowBalloon(AppTitle, T.BalloonWeakMaster, Native.NIIF_INFO);
            if (_cfg.MigrationFailed)
                ShowBalloon(AppTitle, T.BalloonMigrationFailed + "\n" + SaveFailTodo(), Native.NIIF_WARNING);
            // 자동 실행은 여기서(잠금 해제·처음 설정 완료·이어받기) 등록하지 않는다. 설정 [저장]만 바꾼다(0.2.149, Codex D5).
            if (_hideAfterUnlock) { _hideAfterUnlock = false; HideToTray(); }
            return;
        }
        ShowUnlockFailure(failed);
    }

    private void ShowUnlockFailure(bool kdfFailed)
    {
        if (kdfFailed)   // 계산 실패(메모리 부족·암호 API 오류): 틀린 마스터가 아니다. 칸은 비우지 않는다
        {
            LockMsg(T.LockKdfFailed, AppTitle, Native.MB_OK | Native.MB_ICONERROR);
            FocusLockField(0);
            return;
        }
        if (_cfg.PayloadInvalid)
            LockMsg(T.LockPayloadInvalid(Config.FilePath), AppTitle, Native.MB_OK | Native.MB_ICONERROR);
        else
        {
            if (LockWidget.IsShown) LockWidget.Sad();   // 잠금 위젯 고양이가 귀를 내렸다 올린다(0.5.15-A) — 알림 창이 떠 있는 동안에도 움직인다
            LockMsg(T.LockWrongMaster, AppTitle, Native.MB_OK | Native.MB_ICONERROR);
        }
        Native.SetText(LockField(0), "");
        FocusLockField(0);
    }

    // ---------------------------------------------------------------- 키 계산(작업 스레드)

    private const uint WM_KDF_DONE = 0x8000 + 70;
    private int _kdfGen;   // 잠금·종료 때 올린다: 그 전에 시작한 계산의 결과는 반영하지 않고 지운다
    private readonly Queue<Action> _kdfDone = new();

    /// <summary>
    /// 키 유도(Argon2id 64 MiB) 같은 무거운 계산을 작업 스레드에서 돌린다(창이 멈추지 않게 — Codex 16:22 R39-3). 끝나면 UI 스레드에서, 그사이
    /// 잠그거나 끝내지 않았으면 done, 아니면 discard(키를 지운다). after 는 어느 쪽이든 마지막에(바쁨 표시 풀기). 계산 자체를 중간에 멈추지는 않는다
    /// — 늦게 끝난 결과를 버릴 뿐이다.
    /// </summary>
    private void RunKdf<TR>(Func<TR> work, Action<TR> done, Action<TR> discard, Action after, Action? failed = null)
    {
        JobToken token = Token();
        if (Program.IsTestMode) Native.SetPropW(_hwnd, "OneKeyTestKdfStarted", ++_testKdfStarted);   // 검증 전용: 계산을 실제로 시작했는가(T44-2)
        var t = new Thread(() =>
        {
            if (TestKdfDelayMs > 0) Thread.Sleep(TestKdfDelayMs);   // 검증 전용: 계산 중 뒤로·잠금을 창 시험이 맞출 수 있게
            TR r = default!;
            bool ok = false;
            try { r = work(); ok = true; } catch { }
            lock (_kdfDone)
                _kdfDone.Enqueue(() =>
                {
                    try
                    {
                        // 검증 전용 계측(T44-2): 반영 / 버림 / 예외 수 — 시험이 계산 중 뒤로·잠금 뒤 "버려졌음"을 확인한다
                        if (ok && StillValid(token)) { TestKdfCount("OneKeyTestKdfDone", ref _testKdfDone); done(r); }
                        else if (ok) { TestKdfCount("OneKeyTestKdfDiscarded", ref _testKdfDiscarded); discard(r); }
                        else { TestKdfCount("OneKeyTestKdfFailed", ref _testKdfFailed); if (StillValid(token)) failed?.Invoke(); }   // 계산이 예외로 끝났다: 아직 그 화면이면 알린다(R41-3)
                    }
                    finally { after(); }
                });
            Native.PostMessageW(_hwnd, WM_KDF_DONE, 0, 0);
        }) { IsBackground = true };
        t.Start();
    }

    /// <summary>
    /// 작업을 시작한 때의 상태: 잠금 세대·화면 일련번호(다른 화면으로 바뀔 때마다)·화면·잠금 여부. 계산이 끝났을 때와 확인 창·파일 대화 상자 뒤에
    /// 이것이 그대로일 때만 계속한다 — 그사이 잠금·뒤로·취소·다른 화면이면 결과를 버린다(Codex 17:03 R41-2).
    /// </summary>
    /// <summary>검증 전용(ONEKEY_TEST=1): 키 계산 작업마다 앞에 둘 지연(ms) — ONEKEY_TEST_KDF_DELAY.</summary>
    private static readonly int TestKdfDelayMs = Program.IsTestMode && int.TryParse(Environment.GetEnvironmentVariable("ONEKEY_TEST_KDF_DELAY"), out int kd) && kd > 0 ? kd : 0;

    private readonly record struct JobToken(int Gen, int Serial, Screen Screen, bool Unlocked);
    private int _screenSerial;
    private JobToken Token() => new(_kdfGen, _screenSerial, _cur, Unlocked);
    private bool StillValid(JobToken t) => !_exiting && t.Gen == _kdfGen && t.Serial == _screenSerial && t.Screen == _cur && t.Unlocked == Unlocked;

    private void OnKdfDone()
    {
        while (true)
        {
            Action a;
            lock (_kdfDone) { if (_kdfDone.Count == 0) return; a = _kdfDone.Dequeue(); }
            a();
        }
    }

    // ---------------------------------------------------------------- 잠금

    private void DoLock()
    {
        CatWidget.SetWanted(false);   // 잠기면 마스코트는 숨는다(잠금 위젯만)
        _kdfGen++;              // 계산 중이던 마스터 바꾸기 결과는 반영하지 않는다
        ReleaseChip();          // 잠그면 입력 대기도 끝난다 (자동 잠금·Win+L·절전 포함)
        if (!_cfg.IsUnlocked) return;
        CancelTest();
        Injector.Cancel();      // 입력 중이던 글자도 여기서 멈춘다
        _settingsDraft = null;  // 저장하지 않은 설정 변경은 버린다 (ShowScreen 도 잠금으로 갈 때는 다시 모으지 않는다)
        _dropSettingsDraft = true;
        _listFilter = ""; _listTop = 0;
        Backdrop.CloseAll();    // 확인 상자·도움말 뒤의 흐린 그림(비밀번호 표시를 켠 편집 화면일 수도 있다)을 잠금과 함께 버린다
        Native.KillTimer(_hwnd, TimerDiag); _diagPending = false;   // 기다리던 창 진단도 멈춘다
        Native.KillTimer(_hwnd, TimerSiteDiag); _siteDiagPending = false;
        HideFill(); _siteLast = null; _uiaGen++; _reqGen++;   // [채우기]는 잠금과 함께 사라지고, 진행 중인 UIA 작업의 결과는 버린다(채우는 중이면 Injector.Cancel 로 멈춘다)
        if (Native.IsWindowVisible(_hwnd) && !Native.IsIconic(_hwnd)) LockWidget.AnchorAtWindow(_hwnd);   // 잠금 위젯은 본창이 있던 자리에 뜬다
        _cfg.Lock();
        // 떠 있는 확인 상자·안내·도움말은 취소로 닫는다(2026-09-30 사용자 결정): 잠금 뒤에는 그 질문의 전제가 없다.
        // 잠금 상태를 먼저 확정했으므로, 상자가 풀린 뒤 호출한 쪽은 IDLOCKED 와 !Unlocked 를 보고 아무것도 시작하지 않는다.
        Dialog.CancelAllForLock();
        ShowScreen(Screen.Lock);
    }

    /// <summary>자동 잠금: 잠그고 창을 트레이로 내린다(자리를 비운 상태이므로).</summary>
    private void LockAndHide()
    {
        DoLock();
        Native.ShowWindow(_hwnd, Native.SW_HIDE);
        HideLockWidgetForLock();   // 본창·잠금 위젯·입력 창 모두 숨김(B-W03)
    }

    /// <summary>사용자가 직접 잠갔을 때: 창은 그대로 두고 잠금 화면을 보인다.</summary>
    private void LockNow()
    {
        DoLock();
        ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        ReleaseChip();   // 본창을 다시 열면 입력 대기는 끝난다
        _walkerHidden = false;   // 고양이 메뉴의 [고양이 숨기기]는 다음에 창을 열 때까지(AppWalker.cs)
        if (OnLockScreen && ShowLockWidget(activate: true)) { MarkActivity(); return; }   // 잠겨 있으면 마스코트 위젯(사용자가 연 경우만)
        Native.ShowWindow(_hwnd, Native.SW_RESTORE);
        Native.SetForegroundWindow(_hwnd);
        FocusFirst();
        MarkActivity();
    }

    /// <summary>
    /// 검증 전용(ONEKEY_TEST=1): ONEKEY_TEST_NO_AUTOLOCK=1 이면 자동 잠금을 쉰다. 사람이 없는 PC 에서 시험하면 시스템 유휴가 10분을 넘어
    /// 기본 자동 잠금이 시험 도중 잠가 버린다(2026-09-30 inject·mem). 모든 시험이 켜고(lib/Check.ps1), 자동 잠금 시험만 끈다.
    /// </summary>
    private static readonly bool TestNoAutoLock = Program.IsTestMode && Environment.GetEnvironmentVariable("ONEKEY_TEST_NO_AUTOLOCK") == "1";

    private void CheckAutoLock()
    {
        if (TestNoAutoLock) return;
        if (!_cfg.IsUnlocked || _cfg.AutoLockMinutes <= 0) return;
        long idle = Native.InputIdleMs();
        if (idle < 0) idle = Environment.TickCount64 - _lastActivityMs;
        if (idle >= (long)_cfg.AutoLockMinutes * 60_000)
        {
            LockAndHide();
            ShowBalloon(AppTitle, T.BalloonAutoLocked(_cfg.AutoLockMinutes), Native.NIIF_INFO);
        }
    }
}
