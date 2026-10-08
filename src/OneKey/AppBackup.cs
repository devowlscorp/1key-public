using System.Runtime.InteropServices;
using System.Text;

namespace OneKey;

/// <summary>
/// App 의 일부: 백업 파일 만들기·백업에서 복원(2026-10-05 사용자 결정 — 다른 PC·재설치에도, 백업 전용 비밀번호, 실행 목록 포함,
/// 복원 전에 지금 설정도 백업 파일로 남길지 물음. Codex 16:22 B 조건). 파일 형식·두 파일 한꺼번 게시는 <see cref="Backup"/>.
/// - 만들기: 잠금 해제 중, 현재 마스터를 다시 확인. 키 유도(현재 확인 + 백업 키)는 작업 스레드.
/// - 복원: 처음 설정(마스터 없음 — 트레이 메뉴) 또는 설정 › 보안(잠금 해제 중). 백업 비밀번호와 이 PC 에서 쓸 새 마스터를 받는다.
///   모두 검사·암호화·임시 저장한 뒤에만 한꺼번에 게시하고, 그 뒤 설정·실행 목록을 다시 읽어 잠금 화면으로(새 마스터로 잠금 해제).
/// </summary>
internal sealed unsafe partial class App
{
    private const int IdRowBackup = 216, IdRowRestore = 217, IdTrayRestore = 3005;
    private const int IdBkCur = 520, IdBkPw1 = 521, IdBkPw2 = 522, IdBkCancel = 523, IdBkSave = 524, IdBkBack = 525;
    private const int IdRsFile = 526, IdRsBrowse = 527, IdRsPw = 528, IdRsNew1 = 529, IdRsNew2 = 530, IdRsCancel = 531, IdRsSave = 532, IdRsBack = 533;

    private const int IdBkMatch = 535, IdRsMatch = 536;
    private bool _backupBusy, _restoreBusy, _restoreAfterBackup;
    /// <summary>쓰던 PC 에서 잠금을 푼 채 복원: 지금 마스터를 그대로 쓰므로 마스터 칸을 묻지 않는다(2026-10-05 사용자). 처음 설정이면 거짓.</summary>
    private bool _restoreKeepMaster;
    private string? _restorePath;

    // ---------------------------------------------------------------- 만들기

    private void BuildBackupMake()
    {
        Button(IdBkBack, T.CommonBack, Btn.Back, Margin, 12, 94, 32);
        Label(T.SetBackupMake, 110, 12, WinW - 220, 32, Theme.FontStrong, Theme.ControlText, false, Native.SS_CENTER | Native.SS_ENDELLIPSIS);
        HelpButton();
        // 2026-10-05 사용자: 칸 이름만 읽어도 무엇을 넣는지 알게("현재 비밀번호"·"한 번 더"·긴 아래 설명 없앰, 설명은 도움말). 묶음 제목 없음
        int col = LabelCol(116, 210, T.PwMasterCurrent, T.PwBackupEnter, T.PwBackupConfirm);
        int labelX = Margin + Row.PadX, valueX = labelX + col, valueW = CardW - (valueX - Margin) - Row.PadX, labelW = valueX - labelX - 8, y = 60;
        int top = y;
        Label(T.PwMasterCurrent, labelX, y, labelW, RowH, _font, Theme.ControlText, true);
        Field(IdBkCur, valueX, y + (RowH - FieldH) / 2, valueW, FieldH, Native.ES_PASSWORD);
        y += RowH;
        Card(top, y - top);
        y += 16;
        top = y;
        Label(T.PwBackupEnter, labelX, y, labelW, RowH - 1, _font, Theme.ControlText, true);
        Field(IdBkPw1, valueX, y + (RowH - FieldH) / 2, valueW, FieldH, Native.ES_PASSWORD);
        Separator(y + RowH - 1); y += RowH;
        Label(T.PwBackupConfirm, labelX, y, labelW, RowH, _font, Theme.ControlText, true);
        Field(IdBkPw2, valueX, y + (RowH - FieldH) / 2, valueW, FieldH, Native.ES_PASSWORD);
        y += RowH;
        Card(top, y - top);
        y = MatchNote(IdBkMatch, y);   // 확인 칸까지 치면 일치 여부(2026-10-05 사용자)
        y += 8;
        _page.BarTop = y;
        y += 20;
        Button(IdBkCancel, T.CommonCancel, Btn.Bordered, WinW - Margin - 80 - 8 - 72, y, 72, 34);
        Button(IdBkSave, T.BackupMakeButton, Btn.Prominent, WinW - Margin - 80, y, 80, 34, isDefault: true);
        _page.DefaultButton = IdBkSave;
        _page.Height = y + 34 + Margin;
    }

    /// <summary>설정 › 보안 › [백업 파일 만들기].</summary>
    private void StartBackupMake() { _restoreAfterBackup = false; ShowScreen(Screen.BackupMake); }

    private void SaveBackupMake()
    {
        if (_backupBusy || !Unlocked) return;
        var cur = SecretText.FromWindow(C(IdBkCur));
        var p1 = SecretText.FromWindow(C(IdBkPw1));
        using var p2 = SecretText.FromWindow(C(IdBkPw2));
        bool handed = false;
        try { handed = StartBackupMake(cur, p1, p2.Span); }
        finally { if (!handed) { cur.Dispose(); p1.Dispose(); } }
    }

    private bool StartBackupMake(SecretText cur, SecretText p1, ReadOnlySpan<char> p2)
    {
        JobToken tk = Token();
        if (cur.Length == 0) { Msg(T.MasterWrongCurrent, AppTitle, Native.MB_OK | Native.MB_ICONWARNING); Native.SetFocus(C(IdBkCur)); return false; }
        if (!p1.Span.SequenceEqual(p2)) { Msg(T.BackupPwMismatch, AppTitle, Native.MB_OK | Native.MB_ICONWARNING); Native.SetFocus(C(IdBkPw2)); return false; }
        if (p1.Length < 4) { Msg(T.MasterTooShort, AppTitle, Native.MB_OK | Native.MB_ICONWARNING); Native.SetFocus(C(IdBkPw1)); return false; }
        if (WeakMasterReason(p1.Span) is string weak
            && Msg(weak + "\n" + T.BackupWeakWarn + "\n\n" + T.BackupUseAnyway, AppTitle, Native.MB_YESNO | Native.MB_ICONWARNING | Native.MB_DEFBUTTON2) != Native.IDYES) return false;
        if (!StillValid(tk)) return false;   // 확인 창이 떠 있는 동안 잠겼거나 화면이 바뀌었다
        // 실행 목록: 손상·새 형식이면 조용히 빼지 않고 묻는다(B3). 정상이면 빈 목록이라도 넣는다 — 복원에서 "빈 목록"과 "목록 없음"을 가르기 위해(R41-4)
        byte[]? launch = null;
        if (LaunchStore.ReadOnly)
        {
            if (Msg(T.BackupLaunchUnreadable, AppTitle, Native.MB_YESNO | Native.MB_ICONWARNING | Native.MB_DEFBUTTON2) != Native.IDYES || !StillValid(tk)) return false;
        }
        else launch = Encoding.UTF8.GetBytes(LaunchStore.Serialize());
        string? path = PickBackupFile(save: true);
        if (path is null || !StillValid(tk)) return false;
        if (_cfg.ExportForBackup() is not { } ex) return false;
        if (_cfg.BeginChange() is not Config.ChangeJob job) { Crypto.Wipe(ex.Settings); ex.Secrets.Dispose(); return false; }
        _backupBusy = true;
        RunKdf(() =>
        {
            byte[]? file = null, secrets = null;
            try
            {
                job.RunVerify(cur.Span);
                if (!job.Wrong && !job.Failed)
                {
                    secrets = ex.Secrets.ToUtf8();
                    file = Backup.Build(p1.Span, ex.Settings, secrets, launch);
                }
            }
            finally { Crypto.Wipe(secrets); Crypto.Wipe(ex.Settings); ex.Secrets.Dispose(); cur.Dispose(); p1.Dispose(); }
            return (job, file, path);
        }, FinishBackupMake, r => r.job.Dispose(), () => _backupBusy = false, () => Msg(T.BackupFailed(""), AppTitle, Native.MB_OK | Native.MB_ICONERROR));
        return true;
    }

    private void FinishBackupMake((Config.ChangeJob job, byte[]? file, string path) r)
    {
        r.job.Dispose();
        if (r.job.Wrong)
        {
            Msg(T.MasterWrongCurrent, AppTitle, Native.MB_OK | Native.MB_ICONWARNING);
            if (_cur == Screen.BackupMake) { Native.SetText(C(IdBkCur), ""); Native.SetFocus(C(IdBkCur)); }
            return;
        }
        if (r.job.Failed || r.file is null) { Msg(r.job.Failed ? T.LockKdfFailed : T.BackupFailed(""), AppTitle, Native.MB_OK | Native.MB_ICONERROR); return; }
        try
        {
            Backup.WriteNew(r.path + ".tmp", r.file);
            File.Move(r.path + ".tmp", r.path, overwrite: true);   // 덮어쓰기는 저장 대화 상자에서 확인했다
        }
        catch (Exception ex)
        {
            try { File.Delete(r.path + ".tmp"); } catch { }
            Msg(T.BackupFailed(ex.GetType().Name), AppTitle, Native.MB_OK | Native.MB_ICONERROR);
            return;
        }
        Toast.Show(_hwnd, T.BackupMade(r.path), 4000);
        if (_restoreAfterBackup) { _restoreAfterBackup = false; _restorePath = null; _restoreKeepMaster = _cfg.HasMaster && Unlocked; ShowScreen(Screen.Restore); }   // 백업 뒤 복원: 지금 세션으로 다시 정한다(R68-1)
        else ShowScreen(Screen.Settings);   // 들어온 화면(설정)으로(2026-10-06 사용자)
    }

    // ---------------------------------------------------------------- 복원

    private void BuildRestore()
    {
        Button(IdRsBack, T.CommonBack, Btn.Back, Margin, 12, 94, 32);
        Label(T.SetBackupRestore, 110, 12, WinW - 220, 32, Theme.FontStrong, Theme.ControlText, false, Native.SS_CENTER | Native.SS_ENDELLIPSIS);
        HelpButton();
        int col = LabelCol(116, 210, T.PwBackupEnter, T.PwNewMasterEnter, T.PwNewMasterConfirm);
        int labelX = Margin + Row.PadX, valueX = labelX + col, valueW = CardW - (valueX - Margin) - Row.PadX, labelW = valueX - labelX - 8, y = 52;
        Header(T.RestoreFileHeader, y); y += HeaderH;
        int top = y;
        string shown = _restorePath is null ? T.RestoreNoFile : Path.GetFileName(_restorePath);
        Label(shown, labelX, y, CardW - 2 * Row.PadX - 96 - 8, RowH, _font, _restorePath is null ? Theme.SecondaryText : Theme.ControlText, true, Native.SS_ENDELLIPSIS);
        Button(IdRsBrowse, T.RestoreBrowse, Btn.Bordered, Margin + CardW - Row.PadX - 96, y + (RowH - 30) / 2, 96, 30, onCard: true);
        y += RowH;
        Card(top, y - top);
        y += 16;
        top = y;
        Label(T.PwBackupEnter, labelX, y, labelW, RowH, _font, Theme.ControlText, true);
        Field(IdRsPw, valueX, y + (RowH - FieldH) / 2, valueW, FieldH, Native.ES_PASSWORD);
        y += RowH;
        Card(top, y - top);
        if (!_restoreKeepMaster)   // 처음 설정(새 PC·재설치)일 때만: 이 PC 에서 쓸 마스터
        {
            y += 16;
            top = y;
            Label(T.PwNewMasterEnter, labelX, y, labelW, RowH - 1, _font, Theme.ControlText, true);
            Field(IdRsNew1, valueX, y + (RowH - FieldH) / 2, valueW, FieldH, Native.ES_PASSWORD);
            Separator(y + RowH - 1); y += RowH;
            Label(T.PwNewMasterConfirm, labelX, y, labelW, RowH, _font, Theme.ControlText, true);
            Field(IdRsNew2, valueX, y + (RowH - FieldH) / 2, valueW, FieldH, Native.ES_PASSWORD);
            y += RowH;
            Card(top, y - top);
            y = MatchNote(IdRsMatch, y);   // 확인 칸까지 치면 일치 여부(2026-10-05 사용자)
        }
        y += 8;
        _page.BarTop = y;
        y += 20;
        Button(IdRsCancel, T.CommonCancel, Btn.Bordered, WinW - Margin - 80 - 8 - 72, y, 72, 34);
        Button(IdRsSave, T.RestoreButton, Btn.Prominent, WinW - Margin - 80, y, 80, 34, isDefault: true);
        _page.DefaultButton = IdRsSave;
        _page.Height = y + 34 + Margin;
    }

    /// <summary>
    /// [백업에서 복원]: 잠금 해제 중이면 먼저 지금 설정도 백업 파일로 남길지 묻는다(사용자 결정). 처음 설정(마스터 없음)이면 바로 복원 화면 —
    /// 잠긴 기존 설정(마스터를 모름)에는 열지 않는다: 그 설정을 마스터 없이 덮어쓰게 하지 않는다.
    /// </summary>
    private void StartRestore()
    {
        if (_recoveryOnly) return;
        // 이번 복원 흐름의 "지금 마스터 그대로" 여부를 질문·분기 전에 정한다(Codex 23:06 R68-1: [예] 먼저 백업 경로에서 정해지지 않았다)
        _restoreKeepMaster = _cfg.HasMaster && Unlocked;
        // 마스터가 있고 잠금을 푼 세션만 "지금 설정"이 있다. 처음 설정(마스터 없음)도 Unlocked 는 참이므로 HasMaster 로 가른다
        // (0.3.45 backup.ps1 BW06: 처음 설정의 트레이 복원이 쓸 수 없는 사전 백업 질문을 띄웠다)
        if (_cfg.HasMaster && Unlocked)
        {
            // [예] = 먼저 백업 파일 만들기(권장), [아니요] = 바로 복원 화면(아직 아무것도 바꾸지 않는다 — 복원은 그 화면에서 다시 확인).
            // 예/아니요 상자에서 Esc·× 는 [아니요]로 돌아온다(Dialog 규칙) — 그래서 Esc 도 "복원 화면만 연다". 그 밖의 결과(잠금으로 닫힘 IDLOCKED 등)와
            // 그사이 잠금·화면 바뀜은 아무것도 하지 않는다(Codex 17:49 R43-2).
            JobToken tk = Token();
            int r = Msg(T.RestorePreBackup, AppTitle, Native.MB_YESNO | Native.MB_ICONQUESTION);
            if (!StillValid(tk) || r is not (Native.IDYES or Native.IDNO)) return;
            if (r == Native.IDYES) { _restoreAfterBackup = true; ShowScreen(Screen.BackupMake); return; }
        }
        else if (!_createMode) return;
        _restorePath = null;
        if (LockWidget.IsShown) LockWidget.Hide();
        ShowScreen(Screen.Restore);
        Native.ShowWindow(_hwnd, Native.SW_SHOW);
        Native.SetForegroundWindow(_hwnd);
        FocusFirst();
    }

    /// <summary>복원 화면을 떠난다: 처음 설정이면 잠금(처음 설정) 화면으로, 아니면 설정(직전 화면, 2026-10-06 사용자).</summary>
    private void LeaveRestore()
    {
        if (_cfg.HasMaster && Unlocked) { ShowScreen(Screen.Settings); return; }   // 처음 설정이면 처음 설정(잠금) 화면으로
        ShowScreen(Screen.Lock);
        if (ShowLockWidget(activate: true)) return;
        Native.ShowWindow(_hwnd, Native.SW_SHOW);
        FocusFirst();
    }

    private void BrowseRestore()
    {
        if (PickBackupFile(save: false) is not string p || _cur != Screen.Restore) return;
        _restorePath = p;
        RebuildKeepingInput();
    }

    private void DoRestore()
    {
        if (_restoreBusy) return;
        var pw = SecretText.FromWindow(C(IdRsPw));
        var n1 = _restoreKeepMaster ? SecretText.FromUtf8(Array.Empty<byte>()) : SecretText.FromWindow(C(IdRsNew1));
        using var n2 = _restoreKeepMaster ? SecretText.FromUtf8(Array.Empty<byte>()) : SecretText.FromWindow(C(IdRsNew2));
        bool handed = false;
        try { handed = StartRestoreJob(pw, n1, n2.Span); }
        finally { if (!handed) { pw.Dispose(); n1.Dispose(); } }
    }

    private bool StartRestoreJob(SecretText pw, SecretText n1, ReadOnlySpan<char> n2)
    {
        JobToken tk = Token();
        if (_restorePath is null) { Msg(T.RestoreChooseFile, AppTitle, Native.MB_OK | Native.MB_ICONWARNING); Native.SetFocus(C(IdRsBrowse)); return false; }
        if (pw.Length == 0) { Native.SetFocus(C(IdRsPw)); return false; }
        bool keep = _restoreKeepMaster;   // 지금 마스터를 그대로: 새 마스터 검사·유도가 없다
        if (!keep)
        {
            if (!n1.Span.SequenceEqual(n2)) { Msg(T.LockMismatch, AppTitle, Native.MB_OK | Native.MB_ICONWARNING); Native.SetFocus(C(IdRsNew2)); return false; }
            if (n1.Length < 4) { Msg(T.MasterTooShort, AppTitle, Native.MB_OK | Native.MB_ICONWARNING); Native.SetFocus(C(IdRsNew1)); return false; }
            if (WeakMasterReason(n1.Span) is string weak
                && Msg(weak + "\n\n" + T.LockUseAnyway, AppTitle, Native.MB_YESNO | Native.MB_ICONWARNING | Native.MB_DEFBUTTON2) != Native.IDYES) return false;
        }
        if (!StillValid(tk)) return false;
        // 같은 파일 핸들로 상한까지만 읽는다 — 그사이 커져도 더 할당하지 않는다(R41-4)
        var (file, re) = Backup.ReadBounded(_restorePath);
        if (file is null) { Msg(RestoreErrorText(re), AppTitle, Native.MB_OK | Native.MB_ICONERROR); return false; }
        if (Backup.CheckHeader(file) is var he && he != Backup.Error.None) { Msg(RestoreErrorText(he), AppTitle, Native.MB_OK | Native.MB_ICONERROR); return false; }
        if (File.Exists(Config.FilePath) || File.Exists(LaunchStore.FilePath))
            if (Msg(T.RestoreConfirm, AppTitle, Native.MB_YESNO | Native.MB_ICONWARNING | Native.MB_DEFBUTTON2) != Native.IDYES) return false;
        if (!StillValid(tk)) return false;
        _restoreBusy = true;
        RunKdf(() =>
        {
            Backup.Contents? contents = null;
            (byte[] Key, byte[] Salt)? key = null;
            bool handed = false;
            try
            {
                var (c, err) = Backup.Open(file, pw.Span);
                contents = c;
                if (c is not null && !keep) key = Config.DeriveNewMaster(n1.Span);   // keep: 키는 반영 때 지금 세션에서 복사
                handed = true;   // 이제 결과(내용·키)는 FinishRestore 또는 버리기가 맡는다
                return (contents, err, key);
            }
            finally
            {
                if (!handed) { contents?.Dispose(); if (key is { } k) Crypto.Wipe(k.Key); }   // 예외: 만든 것을 모두 지운다(R41-3)
                pw.Dispose(); n1.Dispose();
            }
        }, r => FinishRestore(r, keep), r => { r.contents?.Dispose(); if (r.key is { } k) Crypto.Wipe(k.Key); }, () => _restoreBusy = false,
           () => Msg(T.RestoreFailed(""), AppTitle, Native.MB_OK | Native.MB_ICONERROR));
        return true;
    }

    private void FinishRestore((Backup.Contents? contents, Backup.Error err, (byte[] Key, byte[] Salt)? key) r, bool keep)
    {
        using Backup.Contents? c = r.contents;
        byte[]? keyOwned = r.key?.Key;   // 새 설정 객체에 넘기기 전까지 이 함수가 소유한다
        Config? cfg = null;
        JobToken tk = Token();
        try
        {
            if (c is null || c.Secrets is null) { Msg(RestoreErrorText(r.err), AppTitle, Native.MB_OK | Native.MB_ICONERROR); return; }
            // keep: 지금 세션의 키 사본(토큰이 같으므로 같은 세션). 복사 예외는 아래 catch 의 실패 안내로(R68-2) — 게시·세션 교체 없음
            if (keep) { r.key = _cfg.CopyCurrentKey(); keyOwned = r.key?.Key; }
            if (r.key is not { } key) { Msg(keep ? T.RestoreFailed("") : T.LockKdfFailed, AppTitle, Native.MB_OK | Native.MB_ICONERROR); return; }
            cfg = Config.FromBackup(c.Settings, c.Secrets.Span);
            if (cfg is null) { Msg(RestoreErrorText(Backup.Error.Damaged), AppTitle, Native.MB_OK | Native.MB_ICONERROR); return; }
            // 실행 목록(R41-4): 섹션이 있으면 그것(빈 목록이면 빈 목록으로), 손상이면 "지금 목록 그대로 두고 복원?", 없으면(만들 때 읽지 못함) 그대로 둘지 비울지 묻는다
            string? launch;
            if (c.Launch is not null)
            {
                try { launch = new UTF8Encoding(false, true).GetString(c.Launch); } catch { launch = null; }
                if (launch is null || !LaunchStore.ValidateText(launch))
                {
                    int a = Msg(T.RestoreLaunchDamaged, AppTitle, Native.MB_YESNO | Native.MB_ICONWARNING | Native.MB_DEFBUTTON2);
                    if (a != Native.IDYES || !StillValid(tk)) return;   // 아니요·잠금·화면 바뀜 = 진행 동의 아님
                    launch = null;   // 지금 목록은 그대로
                }
            }
            else
            {
                // [예] = 지금 목록 그대로, [아니요] = 빈 목록, [취소]·Esc·× = 복원 그만둠. 그 밖의 결과(잠금으로 닫힘 등)도 그만둔다(R43-2)
                int a = Msg(T.RestoreNoLaunch, AppTitle, Native.MB_YESNOCANCEL | Native.MB_ICONQUESTION);
                if (!StillValid(tk) || a is not (Native.IDYES or Native.IDNO)) return;
                launch = a == Native.IDYES ? null : "launchv=1\n";
            }
            cfg.CommitCreate(key);   // 이 PC 의 새 마스터 키(새 salt) — 이제 cfg 가 소유한다(아래 finally 에서 cfg.Lock 으로 지움)
            keyOwned = null;
            if (_cfg.LoadFailed && File.Exists(Config.FilePath)) Config.BackupLoadFailedFile();   // 읽지 못하던 예전 파일은 사본으로 남긴다
            string l = launch ?? "";
            Config nc = cfg;
            var res = !DirAcl.EnsureDir(Config.Dir) ? Backup.CommitResult.NotPublished
                    : Backup.Commit(Config.Dir, p => nc.SaveTo(p), launch is null ? null : p => LaunchStore.WriteProtected(p, l));
            switch (res)
            {
                case Backup.CommitResult.Published: ReloadAfterRestore(); break;
                case Backup.CommitResult.NotPublished: Dialog.Show(_hwnd, T.RestoreFailed(cfg.LastSaveError ?? ""), AppTitle, Native.MB_OK | Native.MB_ICONERROR, share: "error"); break;
                default: ResolvePendingRestore(); break;   // 표식 뒤에 멈춤·앞선 게시 미해결: 끝내기 전에는 쓰지 않는다
            }
        }
        catch (Exception ex) { Dialog.Show(_hwnd, T.RestoreFailed(ex.GetType().Name), AppTitle, Native.MB_OK | Native.MB_ICONERROR, share: "error"); }
        finally
        {
            cfg?.Lock();   // 새 설정 객체의 키를 지운다(이 객체는 버린다)
            if (keyOwned is not null) Crypto.Wipe(keyOwned);
        }
    }

    /// <summary>
    /// 복원 게시를 끝내지 못했다(R41-1): 새 쌍으로 마저 끝낼 때까지 설정을 읽거나 저장하지 않는다. 먼저 복구 전용 상태로 들어가
    /// (<see cref="EnterRecoveryOnly"/>) 예전 설정으로는 아무것도 하지 못하게 한 뒤 안내한다(Codex 17:49 R43-1).
    /// [확인] = 다시 시도, [취소]·Esc·× = 1Key 끝내기(다음 시작 때 다시 시도한다). 잠금 등으로 상자가 닫히면 다시 묻는다 — 고를 수 있는 것은 이 둘뿐.
    /// 끝나면 다시 읽어 잠금 화면으로(새 마스터로 잠금 해제).
    /// </summary>
    private void ResolvePendingRestore()
    {
        EnterRecoveryOnly();
        while (true)
        {
            var rr = Backup.RecoverPending(Config.Dir);
            if (rr != Backup.RecoverResult.Blocked) { ReloadAfterRestore(); return; }
            int a = Msg(T.RestorePendingBlocked(Config.Dir), AppTitle, Native.MB_OKCANCEL | Native.MB_ICONERROR);
            if (a == Native.IDCANCEL) { ExitApp(); return; }
        }
    }

    /// <summary>
    /// 복구 전용 상태(R43-1): 예전 설정의 세션을 잠그고(입력·사이트 채우기·칩·계산 중이던 결과도 끝냄), 비밀번호·실행 단축키 등록을 모두 풀고,
    /// 잠금 위젯을 닫는다. 이 상태에서는 WndProc 이 단축키·트레이·화면 명령·잠금 해제 알림·세션 알림을 처리하지 않는다(<see cref="RecoveryBlocks"/>) —
    /// 복구 안내 상자의 메시지 루프 안에서도. 끝나면 <see cref="ReloadAfterRestore"/> 가 풀고 다시 등록한다. 고칠 수 없으면 종료뿐.
    /// </summary>
    private void EnterRecoveryOnly()
    {
        _recoveryOnly = true;
        Walker.SetWanted(false);
        if (Program.IsTestMode) Native.SetPropW(_hwnd, "OneKeyTestRecovery", 1);
        if (Unlocked) DoLock();
        _kdfGen++;
        ReleaseChip();
        Injector.Cancel();
        HideFill(); _siteLast = null; _uiaGen++; _reqGen++;
        for (int i = 0; i < Config.SlotCount; i++) Native.UnregisterHotKey(_hwnd, i);
        UnregisterLaunchHotkeys();
        if (LockWidget.IsShown) LockWidget.Hide();
    }

    /// <summary>복구 전용 상태에서 처리하지 않는 메시지: 단축키, 명령(버튼·메뉴), 트레이, 다른 인스턴스의 "보여 줘", 잠금 위젯 알림, 세션 잠금·절전 알림.</summary>
    private static bool RecoveryBlocks(uint msg) =>
        msg is Native.WM_HOTKEY or Native.WM_COMMAND or Native.WM_TRAY or Native.WM_SHOWME or LockWidget.WM_LOCKWIDGET
            or Native.WM_WTSSESSION_CHANGE or Native.WM_POWERBROADCAST;

    private bool _recoveryOnly;
    private int _testRecoveryBlocked;

    /// <summary>복원을 게시한 뒤: 잠그고 설정·실행 목록을 다시 읽어 단축키를 다시 등록하고, 잠금 화면에서 새 마스터로 열게 한다.</summary>
    private void ReloadAfterRestore()
    {
        _recoveryOnly = false;
        if (Program.IsTestMode) Native.SetPropW(_hwnd, "OneKeyTestRecovery", 0);
        if (Unlocked) DoLock();
        _kdfGen++;
        _cfg = Config.Load();
        _createMode = !_cfg.HasMaster;
        _launchOpened.Clear(); _launchRelated.Clear(); _launchLatest.Clear(); _launchFg.Clear();
        CancelAllFav();   // 복원 전 목록의 파비콘 요청 결과를 새 목록에 쓰지 않는다
        foreach (string id in _launchIconKey.Keys.ToList()) DropIcon(id);
        InitLaunch();
        RegisterHotkeys(silent: true);
        _restorePath = null;
        ShowScreen(Screen.Lock);
        if (!ShowLockWidget(activate: true)) { Native.ShowWindow(_hwnd, Native.SW_SHOW); FocusFirst(); }
        ShowBalloon(AppTitle, _restoreKeepMaster ? T.RestoreDoneKeep : T.RestoreDone, Native.NIIF_INFO);
    }

    private static string RestoreErrorText(Backup.Error e) => e switch
    {
        Backup.Error.TooLarge => T.RestoreErrTooLarge,
        Backup.Error.NotBackup => T.RestoreErrNotBackup,
        Backup.Error.NewerFormat => T.RestoreErrNewer,
        Backup.Error.BadProfile => T.RestoreErrProfile,
        Backup.Error.WrongPasswordOrDamaged => T.RestoreErrPassword,
        Backup.Error.KdfFailed => T.LockKdfFailed,
        _ => T.RestoreErrDamaged,
    };

    // ---------------------------------------------------------------- 파일 대화 상자

    private static readonly Guid CLSID_FileSaveDialog = new("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B");
    private static readonly Guid IID_IFileSaveDialog = new("84bccd23-5fde-4cdb-aea4-af64b83d78ab");

    /// <summary>백업 파일(.1keybak)을 저장할 곳 / 열 파일을 고른다. 취소·실패면 null.</summary>
    private string? PickBackupFile(bool save)
    {
        // 검증 전용(ONEKEY_TEST=1): ONEKEY_TEST_PICK 가 가리키는 글 파일의 내용을 고른 경로로 쓴다(시험이 단계마다 바꾼다). 빈 내용 = 취소
        if (Program.IsTestMode && Environment.GetEnvironmentVariable("ONEKEY_TEST_PICK") is string pick && pick.Length > 0)
        {
            try { string v = File.ReadAllText(pick).Trim(); return v.Length > 0 ? v : null; } catch { return null; }
        }
        if (!_comInit) { int hr0 = CoInitializeEx(0, 2); _comInit = hr0 >= 0; }
        nint dlg = 0, item = 0;
        Guid clsid = save ? CLSID_FileSaveDialog : CLSID_FileOpenDialog, iid = save ? IID_IFileSaveDialog : IID_IFileOpenDialog;
        try
        {
            if (CoCreateInstance(ref clsid, 0, 1, ref iid, out dlg) < 0 || dlg == 0) return null;
            nint* vt = *(nint**)dlg;
            uint opts = 0;
            ((delegate* unmanaged[Stdcall]<nint, uint*, int>)vt[10])(dlg, &opts);
            opts |= 0x40 /* FORCEFILESYSTEM */ | 0x800 /* PATHMUSTEXIST */ | (save ? 0x2u /* OVERWRITEPROMPT */ : 0x1000u /* FILEMUSTEXIST */);
            ((delegate* unmanaged[Stdcall]<nint, uint, int>)vt[9])(dlg, opts);
            fixed (char* title = save ? T.BackupPickSave : T.BackupPickOpen) ((delegate* unmanaged[Stdcall]<nint, char*, int>)vt[17])(dlg, title);
            fixed (char* name = T.BackupFilter) fixed (char* spec = "*" + Backup.Extension)
            {
                var f = new COMDLG_FILTERSPEC { pszName = name, pszSpec = spec };
                ((delegate* unmanaged[Stdcall]<nint, uint, COMDLG_FILTERSPEC*, int>)vt[4])(dlg, 1, &f);
            }
            if (save)
            {
                fixed (char* ext = Backup.Extension[1..]) ((delegate* unmanaged[Stdcall]<nint, char*, int>)vt[22])(dlg, ext);   // SetDefaultExtension
                fixed (char* fn = "1Key-" + DateTime.Now.ToString("yyyyMMdd") + Backup.Extension) ((delegate* unmanaged[Stdcall]<nint, char*, int>)vt[15])(dlg, fn);   // SetFileName
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
