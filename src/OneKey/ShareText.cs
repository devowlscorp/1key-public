using System.Runtime.InteropServices;
using System.Text;

namespace OneKey;

/// <summary>
/// 로그성 안내(진단·채우기 결과·저장 실패의 자세한 정보 등)를 다른 사람에게 보낼 수 있게 [복사]·[파일로 저장](2026-10-07 사용자 요청).
/// 글 앞에 제목·1Key 버전·시각을 붙인다. 파일은 UTF-8(BOM) .txt — 메모장에서 바로 열린다.
/// </summary>
internal static unsafe class ShareText
{
    /// <summary>보낼 글: 제목, "1Key vX · 날짜 시각", 빈 줄, 본문(줄 끝 CRLF, 도움말 표시 "## " 는 뗀다).</summary>
    public static string Compose(string title, string text)
        => (title + "\n1Key v" + App.Version + " · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n\n" + text.Replace(Dialog.SectionMark, ""))
            .Replace("\r\n", "\n").Replace("\n", "\r\n");

    public static bool Copy(nint owner, string content) => Injector.CopyPlainText(owner, content);

    /// <summary>저장할 곳을 묻고 쓴다. 취소면 null, 성공이면 경로, 실패면 error 에 이유.</summary>
    public static string? Save(nint owner, string stem, string content, out string? error)
    {
        error = null;
        string? path = PickPath(owner, "1Key-" + stem + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        if (path is null) return null;
        try { File.WriteAllText(path, content, new UTF8Encoding(true)); return path; }
        catch (Exception ex) { error = ex.Message; return null; }
    }

    private static readonly Guid CLSID_FileSaveDialog = new("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B");
    private static readonly Guid IID_IFileSaveDialog = new("84bccd23-5fde-4cdb-aea4-af64b83d78ab");
    [StructLayout(LayoutKind.Sequential)] private struct COMDLG_FILTERSPEC { public char* pszName; public char* pszSpec; }
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint coInit);
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, nint outer, uint ctx, ref Guid iid, out nint obj);
    [DllImport("ole32.dll")] private static extern void CoTaskMemFree(nint p);

    /// <summary>Windows 저장 대화 상자(.txt). 백업 파일 고르기(AppBackup.PickBackupFile)와 같은 방식. 취소·실패면 null.</summary>
    private static string? PickPath(nint owner, string fileName)
    {
        // 검증 전용(ONEKEY_TEST=1): ONEKEY_TEST_PICK 가 가리키는 글 파일의 내용을 고른 경로로(빈 내용 = 취소) — 백업 시험과 같은 약속
        if (Program.IsTestMode && Environment.GetEnvironmentVariable("ONEKEY_TEST_PICK") is string pick && pick.Length > 0)
        {
            try { string v = File.ReadAllText(pick).Trim(); return v.Length > 0 ? v : null; } catch { return null; }
        }
        CoInitializeEx(0, 2);   // 이미 초기화돼 있으면 S_FALSE/RPC_E_CHANGED_MODE — 어느 쪽이든 아래 생성으로 판단한다
        nint dlg = 0, item = 0;
        Guid clsid = CLSID_FileSaveDialog, iid = IID_IFileSaveDialog;
        try
        {
            if (CoCreateInstance(ref clsid, 0, 1, ref iid, out dlg) < 0 || dlg == 0) return null;
            nint* vt = *(nint**)dlg;
            uint opts = 0;
            ((delegate* unmanaged[Stdcall]<nint, uint*, int>)vt[10])(dlg, &opts);
            opts |= 0x40 /* FORCEFILESYSTEM */ | 0x800 /* PATHMUSTEXIST */ | 0x2u /* OVERWRITEPROMPT */;
            ((delegate* unmanaged[Stdcall]<nint, uint, int>)vt[9])(dlg, opts);
            fixed (char* title = T.LogPickTitle) ((delegate* unmanaged[Stdcall]<nint, char*, int>)vt[17])(dlg, title);
            fixed (char* name = T.LogFilter) fixed (char* spec = "*.txt")
            {
                var f = new COMDLG_FILTERSPEC { pszName = name, pszSpec = spec };
                ((delegate* unmanaged[Stdcall]<nint, uint, COMDLG_FILTERSPEC*, int>)vt[4])(dlg, 1, &f);
            }
            fixed (char* ext = "txt") ((delegate* unmanaged[Stdcall]<nint, char*, int>)vt[22])(dlg, ext);   // SetDefaultExtension
            fixed (char* fn = fileName) ((delegate* unmanaged[Stdcall]<nint, char*, int>)vt[15])(dlg, fn);   // SetFileName
            if (((delegate* unmanaged[Stdcall]<nint, nint, int>)vt[3])(dlg, owner) < 0) return null;       // Show (취소 포함)
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
