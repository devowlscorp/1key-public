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

    private void SyncWalker() => CatWidget.SetWanted(WalkerWanted);


    /// <summary>목록 윗줄의 작은 마스코트 단추(위젯 모드를 켰을 때 (−) 자리): 창을 트레이로 내린다 — 그러면 마스코트가 나온다.</summary>
    private bool WidgetButton => _cfg.Walker && _cfg.HasMaster;

    private const int IdCatOpen = 3201, IdCatLock = 3202, IdCatSettings = 3203, IdCatHide = 3204;

    /// <summary>
    /// 고양이 오른쪽 클릭 메뉴(0.5.15-V, 2026-10-09 사용자: 고양이에 오른쪽 클릭으로 무언가 할 수 있게 — 사람과 주고받는 동작(쓰다듬기·츄르·놀아 주기)은
    /// 그림이 오면 위에 더한다). 트레이 메뉴와 같은 Windows 메뉴: [1Key 열기] [지금 잠금](잠금이 풀려 있을 때) [설정…] / [고양이 숨기기].
    /// 잠긴 채 위젯 모드면 [잠금 해제] / [고양이 숨기기].
    /// </summary>
    private void OnWalkerMenu(int gen)
    {
        if (!WalkerWanted || !CatWidget.AcceptClick(gen)) return;
        nint menu = Native.CreatePopupMenu();
        if (menu == 0) return;
        try
        {
            bool locked = LockedWalker;
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
            if (Program.IsTestMode) Native.SetPropW(_hwnd, "OneKeyTestCatMenu", cmd);
            switch (cmd)
            {
                case IdCatOpen: OnWalkerClick(gen); break;
                case IdCatLock: if (Unlocked) LockNow(); break;
                case IdCatSettings: ShowMainWindow(); if (Unlocked && !OnLockScreen) ShowScreen(Screen.Settings); break;
                case IdCatHide:
                    _walkerHidden = true;
                    SyncWalker();
                    ShowBalloon(AppTitle, T.CatHiddenNote, Native.NIIF_INFO);
                    break;
            }
        }
        finally { Native.DestroyMenu(menu); }
    }

    private void OnWalkerClick(int gen)
    {
        if (!WalkerWanted || !CatWidget.AcceptClick(gen)) return;   // 그사이 잠김·끔·창이 보임·다시 보임(낡은 클릭)·환경 부적합이면 무시(Codex R83-2)
        bool locked = LockedWalker;
        _lockedWalker = false;
        ShowMainWindow();   // 잠겨 있으면 잠금 위젯(ShowMainWindow → ShowLockWidget)
        if (locked) SyncWalker();
    }
}
