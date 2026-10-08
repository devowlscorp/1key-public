using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>App 의 일부: 알림 영역(트레이) 아이콘과 메뉴. (App.cs 에서 화면별로 나눔, 0.3.9 — 동작은 그대로)</summary>
internal sealed unsafe partial class App
{
    // ---------------------------------------------------------------- 트레이

    private Native.NOTIFYICONDATAW MakeTrayData() => new() { cbSize = (uint)sizeof(Native.NOTIFYICONDATAW), hWnd = _hwnd, uID = 1 };

    private void AddTrayIcon()
    {
        var data = MakeTrayData();
        data.uFlags = Native.NIF_ICON | Native.NIF_MESSAGE | Native.NIF_TIP | Native.NIF_SHOWTIP;
        data.uCallbackMessage = Native.WM_TRAY;
        data.hIcon = _iconSmall;
        CopyTo(data.szTip, 128, TrayTipText());
        if (!Native.Shell_NotifyIconW(Native.NIM_ADD, ref data)) return;
        _trayAdded = true;
        data.uVersion = Native.NOTIFYICON_VERSION_4;
        Native.Shell_NotifyIconW(Native.NIM_SETVERSION, ref data);
    }

    private void UpdateTrayTip()
    {
        if (!_trayAdded) return;
        var data = MakeTrayData();
        data.uFlags = Native.NIF_TIP | Native.NIF_SHOWTIP;
        CopyTo(data.szTip, 128, TrayTipText());
        Native.Shell_NotifyIconW(Native.NIM_MODIFY, ref data);
    }

    private string TrayTipText()
    {
        // 툴팁은 127자까지만 보이므로 단축키가 있는 항목 셋만 적고 나머지는 개수로
        var sb = new System.Text.StringBuilder(AppTitle);
        if (_chipSlot >= 0) sb.Append('\n').Append(T.TrayWaiting(_cfg.Slots[_chipSlot].DisplayName(_chipSlot)));
        int listed = 0, total = 0;
        for (int i = 0; i < Config.SlotCount; i++)
        {
            Slot s = _cfg.Slots[i];
            if (!(s.HasPassword && s.HasHotkey)) continue;
            total++;
            if (listed < 3) { sb.Append('\n').Append(s.DisplayName(i)).Append(" : ").Append(s.HotkeyText()); listed++; }
        }
        if (total > listed) sb.Append('\n').Append(T.TrayMore(total - listed));
        return sb.Length > 126 ? sb.ToString(0, 126) : sb.ToString();
    }

    private void RemoveTrayIcon()
    {
        if (!_trayAdded) return;
        var data = MakeTrayData();
        Native.Shell_NotifyIconW(Native.NIM_DELETE, ref data);
        _trayAdded = false;
    }

    private void ShowBalloon(string title, string text, uint iconFlag)
    {
        if (!_trayAdded) return;
        var data = MakeTrayData();
        data.uFlags = Native.NIF_INFO;
        data.dwInfoFlags = iconFlag;
        CopyTo(data.szInfoTitle, 64, title);
        CopyTo(data.szInfo, 256, text);
        Native.Shell_NotifyIconW(Native.NIM_MODIFY, ref data);
    }

    private static void CopyTo(char* dest, int capacity, string text)
    {
        int n = Math.Min(text.Length, capacity - 1);
        for (int i = 0; i < n; i++) dest[i] = text[i];
        dest[n] = '\0';
    }

    private void ShowTrayMenu()
    {
        nint menu = Native.CreatePopupMenu();
        if (menu == 0) return;
        try
        {
            fixed (char* p = T.TrayOpen) Native.AppendMenuW(menu, Native.MF_STRING, IdTrayOpen, p);
            if (_cfg.HasMaster)
            {
                string lockLabel = Unlocked ? T.TrayLockNow : T.TrayUnlock;
                fixed (char* p = lockLabel) Native.AppendMenuW(menu, Native.MF_STRING, IdTrayLock, p);
            }
            if (_createMode && !_cfg.Unsupported)   // 처음 설정: 다른 PC 의 백업으로 시작할 수 있다
                fixed (char* p = T.TrayRestore) Native.AppendMenuW(menu, Native.MF_STRING, IdTrayRestore, p);
            Native.AppendMenuW(menu, Native.MF_SEPARATOR, 0, null);
            // 슬롯은 앞의 12개까지만 메뉴에 (그 이상이면 메뉴가 화면을 넘는다). 나머지는 창에서 찾아 누른다.
            const int MaxMenuSlots = 12;
            int listed = 0, total = 0;
            for (int i = 0; i < Config.SlotCount; i++)
            {
                Slot s = _cfg.Slots[i];
                if (!SlotInUse(s)) continue;
                total++;
                if (listed >= MaxMenuSlots) continue;
                string label = s.HasHotkey ? $"{s.DisplayName(i)}  ({s.HotkeyText()})" : s.DisplayName(i);
                uint flags = Native.MF_STRING | (s.HasPassword ? 0 : Native.MF_GRAYED);
                fixed (char* p = label) Native.AppendMenuW(menu, flags, (nuint)(IdTraySlot + i), p);
                listed++;
            }
            if (total > listed)
                fixed (char* p = T.TrayMoreMenu(total - listed)) Native.AppendMenuW(menu, Native.MF_STRING, IdTrayMore, p);
            Native.AppendMenuW(menu, Native.MF_SEPARATOR, 0, null);
            fixed (char* p = T.TrayExit) Native.AppendMenuW(menu, Native.MF_STRING, IdTrayExit, p);

            Native.GetCursorPos(out Native.POINT pt);
            Native.SetForegroundWindow(_hwnd);
            int cmd = Native.TrackPopupMenu(menu, Native.TPM_RIGHTBUTTON | Native.TPM_RETURNCMD | Native.TPM_BOTTOMALIGN, pt.x, pt.y, 0, _hwnd, 0);

            if (cmd == IdTrayOpen || cmd == IdTrayMore) ShowMainWindow();
            else if (cmd == IdTrayLock) { if (Unlocked) LockNow(); else ShowMainWindow(); }
            else if (cmd == IdTrayExit) ExitApp();
            else if (cmd == IdTrayRestore) StartRestore();
            else if (cmd >= IdTraySlot && cmd < IdTraySlot + Config.SlotCount) TrayChip(cmd - IdTraySlot);
        }
        finally { Native.DestroyMenu(menu); }
    }

    /// <summary>
    /// 트레이 메뉴의 항목: 입력 칩을 띄운다 (2026-09-29 사용자 결정). 예전에는 곧바로 입력했는데, 메뉴를 띄우느라 1Key 가
    /// 앞에 와 있어서 늘 "1Key 창이 앞에 있어 입력하지 않았습니다"로 끝났다. 칩이면 메뉴가 닫힌 뒤 넣을 칸을 클릭하고 누르면 된다.
    /// </summary>
    private void TrayChip(int slot)
    {
        if (_cfg.HasMaster && !_cfg.IsUnlocked)
        {
            ShowBalloon(AppTitle, T.BalloonLockedTray, Native.NIIF_WARNING);
            ShowMainWindow();
            return;
        }
        StartChip(slot);
    }

    private void HideToTray()
    {
        Native.ShowWindow(_hwnd, Native.SW_HIDE);
        if (!_trayHintShown)
        {
            _trayHintShown = true;
            ShowBalloon(AppTitle, T.BalloonTray, Native.NIIF_INFO);
        }
    }

    /// <summary>완전 종료 전에 한 번 묻는다. 종료하면 단축키도 멈추기 때문이다.</summary>
    private void ConfirmExit()
    {
        // 기본 버튼은 '아니요' (T16): Enter 를 무심코 눌러도 종료되지 않는다.
        int r = Msg(T.ExitConfirm,
            AppTitle, Native.MB_YESNO | Native.MB_ICONQUESTION | Native.MB_DEFBUTTON2);
        if (r == Native.IDYES) ExitApp();
    }

    private void ExitApp()
    {
        _exiting = true;
        Native.DestroyWindow(_hwnd);
    }
}
