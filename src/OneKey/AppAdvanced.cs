using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>App 의 일부: 고급 화면과 마스터 비밀번호 바꾸기. (App.cs 에서 화면별로 나눔, 0.3.9 — 동작은 그대로)</summary>
internal sealed unsafe partial class App
{
    // ---------------------------------------------------------------- 고급 화면

    private void BuildAdvanced()
    {
        Button(IdABack, T.CommonBack, Btn.Back, Margin, 12, 94, 32);
        Label(T.SetAdvanced, 110, 12, WinW - 220, 32, Theme.FontStrong, Theme.ControlText, false, Native.SS_CENTER | Native.SS_ENDELLIPSIS);
        HelpButton();

        int labelX = Margin + Row.PadX, y = 52;
        Header(T.AdvSpeed, y); y += HeaderH;
        int top = y;
        Label(T.AdvKeyDelay, labelX, y, CardW - 2 * Row.PadX - 80 - 8, RowH - 1, _font, Theme.ControlText, true);
        Field(IdADelay, Margin + CardW - Row.PadX - 80, y + (RowH - FieldH) / 2, 80, FieldH, Native.ES_NUMBER);
        Separator(y + RowH - 1); y += RowH;
        Label(T.AdvPreDelay, labelX, y, CardW - 2 * Row.PadX - 80 - 8, RowH, _font, Theme.ControlText, true);
        Field(IdAPre, Margin + CardW - Row.PadX - 80, y + (RowH - FieldH) / 2, 80, FieldH, Native.ES_NUMBER);
        y += RowH;
        Card(top, y - top);
        y = Footer(T.AdvFooter, y);

        y += 12; Header(T.AdvDiag, y); y += HeaderH;
        top = y;
        ListRow(IdRowDiag, T.AdvDiagRow, "", "", Row.Chevron | Row.First, y); y += RowH;
        ListRow(IdRowSiteDiag, T.AdvSiteRow, "", "", Row.Chevron, y); y += RowH;
        ListRow(IdRowFillLog, T.AdvFillLogRow, "", "", Row.Chevron | Row.Last, y); y += RowH;
        Card(top, y - top);   // 진단 방법은 누르면 먼저 안내한다(StartDiagnostics). 상시 설명은 [도움말]로

        // 사용량(2026-10-07 사용자): 1Key 프로세스의 CPU·메모리, 이 화면에 있는 동안 1초마다
        y += 12; Header(T.AdvUsage, y); y += HeaderH;
        top = y;
        UsageRow(T.AdvUsageCpu, IdACpu, y, true); y += RowH;
        UsageRow(T.AdvUsageRam, IdARam, y, false); y += RowH;
        Card(top, y - top);
        y = Footer(T.AdvUsageFooter, y);

        _page.BarTop = y;
        y += 20;
        Button(IdACancel, T.CommonCancel, Btn.Bordered, WinW - Margin - 80 - 8 - 72, y, 72, 34);
        Button(IdASave, T.CommonSave, Btn.Prominent, WinW - Margin - 80, y, 80, 34, isDefault: true);
        _page.DefaultButton = IdASave;
        _page.Height = y + 34 + Margin;

        Native.SetText(C(IdADelay), _cfg.KeyDelayMs.ToString());
        Native.SetText(C(IdAPre), _cfg.PreDelayMs.ToString());
        _usageCpu0 = -1; UpdateUsage();
        Native.SetTimer(_hwnd, TimerUsage, 1000, 0);
    }

    private void UsageRow(string name, int id, int y, bool sep)
    {
        int labelX = Margin + Row.PadX, valueW = 140;
        Label(name, labelX, y, CardW - 2 * Row.PadX - valueW - 8, RowH - (sep ? 1 : 0), _font, Theme.ControlText, true);
        nint v = Make("STATIC", "", Native.SS_RIGHT | Native.SS_CENTERIMAGE | Native.SS_NOPREFIX, Margin + CardW - Row.PadX - valueW, y, valueW, RowH - (sep ? 1 : 0), id, 0, _font);
        if (v != 0) _staticStyle[v] = (Theme.CardBrush, Theme.SecondaryText);
        if (sep) Separator(y + RowH - 1);
    }

    private long _usageCpu0 = -1, _usageWall0;

    /// <summary>
    /// 사용량 줄을 고친다. CPU = 지난 측정 뒤 이 프로세스가 쓴 시간 ÷ (흐른 시간 × 논리 프로세서 수) — 작업 관리자와 같은 기준(모든 코어 합).
    /// 메모리 = 작업 집합(지금 실제 메모리에 올라 있는 크기). 고급 화면을 떠나면 타이머를 끈다.
    /// </summary>
    private void UpdateUsage()
    {
        if (_cur != Screen.Advanced) { Native.KillTimer(_hwnd, TimerUsage); return; }
        long wall = Environment.TickCount64;
        string cpu = T.AdvUsageMeasuring;
        if (GetProcessTimes(Native.GetCurrentProcess(), out _, out _, out long k, out long u))
        {
            long used = k + u;   // 100 ns 단위
            if (_usageCpu0 >= 0 && wall > _usageWall0)
            {
                double pct = (used - _usageCpu0) / 10000.0 / (wall - _usageWall0) / Math.Max(1, Environment.ProcessorCount) * 100.0;
                cpu = Math.Clamp(pct, 0, 100).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " %";
            }
            _usageCpu0 = used; _usageWall0 = wall;
        }
        else cpu = "-";
        var mc = new PROCESS_MEMORY_COUNTERS { cb = (uint)sizeof(PROCESS_MEMORY_COUNTERS) };
        string ram = K32GetProcessMemoryInfo(Native.GetCurrentProcess(), ref mc, mc.cb)
            ? (mc.WorkingSetSize / 1048576.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB" : "-";
        if (C(IdACpu) is nint hc && hc != 0 && Native.GetWindowText(hc) != cpu) Native.SetText(hc, cpu);
        if (C(IdARam) is nint hr && hr != 0 && Native.GetWindowText(hr) != ram) Native.SetText(hr, ram);
        if (Program.IsTestMode) { Native.SetPropW(_hwnd, "OneKeyTestUsageCpu", (nint)(cpu == T.AdvUsageMeasuring ? -1 : 1)); Native.SetPropW(_hwnd, "OneKeyTestUsageRamKb", (nint)(long)(mc.WorkingSetSize / 1024)); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_MEMORY_COUNTERS
    {
        public uint cb, PageFaultCount;
        public nuint PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage, QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage;
    }
    [DllImport("kernel32.dll")] private static extern bool K32GetProcessMemoryInfo(nint process, ref PROCESS_MEMORY_COUNTERS counters, uint cb);
    [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(nint process, out long created, out long exited, out long kernel, out long user);

    // ---------------------------------------------------------------- 마스터 비밀번호 바꾸기 (T3)

    private void BuildMaster()
    {
        Button(IdMBack, T.CommonBack, Btn.Back, Margin, 12, 94, 32);
        Label(T.SetChangeMaster, 110, 12, WinW - 220, 32, Theme.FontStrong, Theme.ControlText, false, Native.SS_CENTER | Native.SS_ENDELLIPSIS);
        HelpButton();

        // 칸 이름만 읽어도 알게(2026-10-05 사용자) — 묶음 제목 없음
        int col = LabelCol(116, 210, T.PwMasterCurrent, T.PwNewMasterEnter, T.PwNewMasterConfirm);   // 이름표 열: 언어마다 길이가 달라 잰다
        int labelX = Margin + Row.PadX, valueX = labelX + col, valueW = CardW - (valueX - Margin) - Row.PadX, labelW = valueX - labelX - 8, y = 60;   // 이름표가 입력칸 상자(둥근 모서리)를 덮지 않도록
        int top = y;
        Label(T.PwMasterCurrent, labelX, y, labelW, RowH, _font, Theme.ControlText, true);
        Field(IdMCur, valueX, y + (RowH - FieldH) / 2, valueW, FieldH, Native.ES_PASSWORD);
        y += RowH;
        Card(top, y - top);
        y += 16;

        top = y;
        Label(T.PwNewMasterEnter, labelX, y, labelW, RowH - 1, _font, Theme.ControlText, true);
        Field(IdMNew1, valueX, y + (RowH - FieldH) / 2, valueW, FieldH, Native.ES_PASSWORD);
        Separator(y + RowH - 1); y += RowH;
        Label(T.PwNewMasterConfirm, labelX, y, labelW, RowH, _font, Theme.ControlText, true);
        Field(IdMNew2, valueX, y + (RowH - FieldH) / 2, valueW, FieldH, Native.ES_PASSWORD);
        y += RowH;
        Card(top, y - top);
        y = MatchNote(IdMMatch, y);   // 확인 칸까지 치면 일치 여부(2026-10-05 사용자)
        y = Footer(T.MasterFooter, y);

        _page.BarTop = y;
        y += 20;
        Button(IdMCancel, T.CommonCancel, Btn.Bordered, WinW - Margin - 80 - 8 - 72, y, 72, 34);
        Button(IdMSave, T.CommonChange, Btn.Prominent, WinW - Margin - 80, y, 80, 34, isDefault: true);
        _page.DefaultButton = IdMSave;
        _page.Height = y + 34 + Margin;
    }

    private void SaveMaster()
    {
        if (_masterBusy) return;   // 계산 중에 다시 누름
        // 칸의 마스터들은 소유한 고정 버퍼로 읽고 다 쓰면 지운다(문자열을 만들지 않음, Codex 15:19 S36-1)
        var cur = SecretText.FromWindow(C(IdMCur));
        var n1 = SecretText.FromWindow(C(IdMNew1));
        using var n2 = SecretText.FromWindow(C(IdMNew2));
        bool handed = false;
        try { handed = StartSaveMaster(cur, n1, n2.Span); }
        finally { if (!handed) { cur.Dispose(); n1.Dispose(); } }
    }

    private bool _masterBusy;

    /// <summary>키 계산 없이 볼 수 있는 것을 먼저 확인하고, 현재 마스터 확인 + 새 키 유도를 한 작업으로 작업 스레드에 넘긴다(R39-3). 넘겼으면 true.</summary>
    private bool StartSaveMaster(SecretText curT, SecretText n1T, ReadOnlySpan<char> n2)
    {
        ReadOnlySpan<char> cur = curT.Span, n1 = n1T.Span;
        if (cur.Length == 0) { Msg(T.MasterWrongCurrent, AppTitle, Native.MB_OK | Native.MB_ICONWARNING); if (_cur == Screen.Master) Native.SetFocus(C(IdMCur)); return false; }
        if (!n1.SequenceEqual(n2)) { Msg(T.MasterMismatch, AppTitle, Native.MB_OK | Native.MB_ICONWARNING); if (_cur == Screen.Master) Native.SetFocus(C(IdMNew2)); return false; }
        if (n1.Length < 4) { Msg(T.MasterTooShort, AppTitle, Native.MB_OK | Native.MB_ICONWARNING); if (_cur == Screen.Master) Native.SetFocus(C(IdMNew1)); return false; }
        if (n1.SequenceEqual(cur)) { Msg(T.MasterSame, AppTitle, Native.MB_OK | Native.MB_ICONWARNING); return false; }
        if (WeakMasterReason(n1) is string weak)
        {
            int r = Msg(weak + "\n\n" + T.MasterChangeAnyway, AppTitle, Native.MB_YESNO | Native.MB_ICONWARNING | Native.MB_DEFBUTTON2);
            if (r != Native.IDYES) return false;
        }
        if (_cur != Screen.Master || !Unlocked) return false;   // 확인 상자가 떠 있는 동안 잠겼다
        if (_cfg.BeginChange() is not Config.ChangeJob job) return false;
        _masterBusy = true;
        RunKdf(() => { try { job.Run(curT.Span, n1T.Span); } finally { curT.Dispose(); n1T.Dispose(); } return job; },
               FinishSaveMaster, j => j.Dispose(), () => _masterBusy = false);
        return true;
    }

    /// <summary>마스터 바꾸기 계산이 끝났다(UI 스레드).</summary>
    private void FinishSaveMaster(Config.ChangeJob job)
    {
        if (job.Wrong)
        {
            job.Dispose();
            Msg(T.MasterWrongCurrent, AppTitle, Native.MB_OK | Native.MB_ICONWARNING);
            if (_cur == Screen.Master) { Native.SetText(C(IdMCur), ""); Native.SetFocus(C(IdMCur)); }
            return;
        }
        bool failed = job.Failed;
        if (!_cfg.FinishChange(job))
        {
            // 예전 마스터·파일이 그대로다.
            Msg(failed ? T.LockKdfFailed : T.MasterFailed + "\n" + SaveFailTodo() + SaveFailDetail(), AppTitle, Native.MB_OK | Native.MB_ICONERROR);
            return;
        }
        ShowScreen(Screen.Settings);   // 들어온 화면(설정)으로(2026-10-06 사용자)
        // 예전 비밀번호로 열리는 사본(.bak)이 있으면 지울지 묻는다(기본 아니요, 자동으로 지우지 않음 — 2026-10-05 보안 진단 후속)
        Config.BackupFile[] baks = Config.BackupFiles();   // 물을 때 본 목록만 지운다(S36-2)
        if (baks.Length > 0 && Msg(T.MasterBakAsk(baks.Length), AppTitle, Native.MB_YESNO | Native.MB_ICONQUESTION | Native.MB_DEFBUTTON2) == Native.IDYES)
        {
            var (ok, fail) = Config.DeleteBackups(baks);
            if (fail > 0) Msg(T.MasterChanged + "\n\n" + T.MasterBakPartial(ok, fail, Config.Dir), AppTitle, Native.MB_OK | Native.MB_ICONWARNING);
            else Toast.Show(_hwnd, T.MasterChanged + "\n" + T.MasterBakDeleted(ok), 3000);
            return;
        }
        // 성공 알림은 토스트(확인할 것이 없다). .bak 안내는 이 화면 아래 설명과 도움말에 있다.
        Toast.Show(_hwnd, T.MasterChanged, 2400);
    }

    private void SaveAdvanced()
    {
        var keep = (_cfg.KeyDelayMs, _cfg.PreDelayMs);
        _cfg.KeyDelayMs = int.TryParse(Native.GetWindowText(C(IdADelay)), out int d) ? Math.Clamp(d, 0, 500) : 20;
        _cfg.PreDelayMs = int.TryParse(Native.GetWindowText(C(IdAPre)), out int p) ? Math.Clamp(p, 0, 3000) : 80;
        if (!_cfg.Save())
        {
            (_cfg.KeyDelayMs, _cfg.PreDelayMs) = keep;   // 파일과 어긋나지 않게 되돌린다. 화면의 값은 그대로다.
            Msg( SaveFailMsg(), AppTitle, Native.MB_OK | Native.MB_ICONERROR);
            return;
        }
        ShowScreen(Screen.Settings);   // 들어온 화면(설정)으로(2026-10-06 사용자)
    }

    /// <summary>앱 테마를 따르는 확인 상자 (시스템 MessageBox 대신). 인자와 반환값은 MessageBox 와 같다.</summary>
    private int Msg(string text, string title, uint flags) => Dialog.Show(_hwnd, text, title, flags, share: ShareOf(text));

    /// <summary>오류의 "자세한 정보"(SaveFailDetail)가 붙은 안내는 [복사]·[파일로 저장]을 단다 — 문의할 때 보낼 수 있게(2026-10-07 사용자).</summary>
    internal static string? ShareOf(string text) => text.Contains("\n\n" + T.SaveDetail + ": ") ? "error" : null;

    /// <summary>
    /// 저장 실패 안내(Codex QA-07): 원인에 맞는 할 일 한두 문장을 먼저, 오류 코드·경로는 맨 아래 "자세한 정보"에.
    /// DPAPI 실패를 평문 저장으로 우회하지 않는다. 화면의 값은 그대로 두었으니 원인을 없앤 뒤 다시 [저장]하면 된다.
    /// </summary>
    private string SaveFailMsg()
        => T.SaveFailed + "\n" + SaveFailTodo() + SaveFailDetail();

    private string SaveFailDetail() => "\n\n" + T.SaveDetail + ": " + (_cfg.LastSaveError ?? "-") + "\n" + Config.FilePath;

    private string SaveFailTodo()
        => _cfg.LastSaveFail switch
        {
            SaveFail.Access => T.SaveAccess,
            SaveFail.InUse => T.SaveInuse,
            SaveFail.DiskFull => T.SaveDisk,
            SaveFail.Dpapi => T.SaveDpapi,
            SaveFail.Backup => T.SaveBackup,
            SaveFail.Protect => T.SaveProtect,
            _ => T.SaveOther,
        };
}
