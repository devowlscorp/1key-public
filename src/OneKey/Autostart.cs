using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// Windows 시작 시 자동 실행 = 사용자 시작 프로그램 폴더(FOLDERID_Startup)의 바로 가기 <c>1Key.lnk</c> 하나 (0.2.149).
/// 예전에는 레지스트리 Run 값과 작업 스케줄러(관리자 로그온 작업)를 썼는데, 1Key exe 가 자동 실행을 만들거나 지운 순간마다
/// Defender 가 탐지했다(Bearfoos 2026-09-22, Persistence 10-02, Execution 10-03). 그래서 이제 앱은
/// - 자식 프로세스(schtasks·reg·PowerShell)를 띄우지 않고 IShellLinkW 로 바로 가기만 만들고 지운다.
/// - 사용자가 설정에서 [저장]할 때만, 설치본에서만 바꾼다(새 버전 따라가기·이어받기 재등록 없음).
/// - 예전 등록(Run 값 "1Key", 작업 "1Key_AutoStart")은 읽기만 한다. 옮기고 지우는 것은 설치 파일이 한다.
/// - "항상 관리자 권한"이면 로그온 때 일반 권한으로 뜬 1Key 가 App.Run 에서 UAC 창을 한 번 띄운다(사용자 결정 D3 가).
/// 설계와 조건: docs/reviews/Claude_2026-10-03_19-51-24_…, Codex_2026-10-03_20-44-07_설계회신-… (D1–D9, R-A1–A5).
/// </summary>
internal static unsafe class Autostart
{
    /// <summary>
    /// 검증 모드(ONEKEY_TEST=1)에서는 이름에 접미사를 붙인다(접미사가 없으면 ".test"). 폴더도 격리 폴더라(<see cref="StartupDir"/>)
    /// 실제 사용자의 자동 실행과 겹치지 않는다.
    /// </summary>
    private static string Suffix => Program.InstanceSuffix.Length > 0 ? Program.InstanceSuffix : (Program.IsTestMode ? ".test" : string.Empty);
    private static string LinkFileName => "1Key" + Suffix + ".lnk";
    private const string Args = "--tray";
    private const int SW_SHOWNORMAL = 1;

    public static string ExePath => Environment.ProcessPath ?? string.Empty;

    // ---------------------------------------------------------------- 설치본 경계

    /// <summary>
    /// 엄격한 따라가기(2026-10-02): 자동 실행을 바꾸는 것은 **설치본(<see cref="InstallPath"/>)으로 실행한 1Key 만**이다.
    /// 다운로드·바탕 화면·빌드 폴더의 exe 는 자동 실행을 건드리지 않는다. 설정 화면의 스위치도 설치본에서만 바꿀 수 있다.
    /// 검증 모드에서는 지금 실행 파일을 설치본으로 본다(기존 시험이 빌드 폴더에서 돈다). 하네스는 ONEKEY_TEST_INSTALL_PATH 로
    /// 다른 설치 위치를 흉내 낸다.
    /// </summary>
    public static bool IsInstalledCopy => ExePath.Length > 0 && SamePath(ExePath, EffectiveInstallPath);

    private static string EffectiveInstallPath
    {
        get
        {
            if (!Program.IsTestMode) return InstallPath;
            return Environment.GetEnvironmentVariable("ONEKEY_TEST_INSTALL_PATH") is { Length: > 0 } e ? e : ExePath;
        }
    }

    private static bool SamePath(string a, string b)
    {
        try { return a.Length > 0 && b.Length > 0 && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    /// <summary>a 가 b 와 같거나 b 아래에 있으면 true(대소문자 무시).</summary>
    private static bool IsAtOrUnder(string a, string b)
    {
        try
        {
            string fa = Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), fb = Path.TrimEndingDirectorySeparator(Path.GetFullPath(b));
            return string.Equals(fa, fb, StringComparison.OrdinalIgnoreCase) || fa.StartsWith(fb + "\\", StringComparison.OrdinalIgnoreCase);
        }
        catch { return true; }   // 판단할 수 없으면 겹친다고 본다(쓰지 않는 쪽)
    }

    /// <summary>설치 파일(installer\1Key.nsi)이 쓰는 고정 위치: %LOCALAPPDATA%\Programs\1Key\1Key.exe.</summary>
    public static string InstallPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "1Key", "1Key.exe");

    // ---------------------------------------------------------------- 시작 프로그램 폴더 (시험 격리, Codex D7·R-A2)

    /// <summary>
    /// 바로 가기를 둘 폴더. 실제로는 사용자 시작 프로그램 폴더. 검증 모드에서는 **절대로 실제 폴더를 쓰지 않는다**:
    /// ONEKEY_TEST_STARTUP_DIR(절대 경로, 실제 시작 프로그램 폴더와 그 아래가 아님)이 있으면 그것, 없으면 설정 폴더 아래 "startup.test".
    /// 지정한 값이 이 조건에 맞지 않으면 null(아무것도 읽거나 바꾸지 않음). 접미사만 붙여 실제 폴더에 두면 실제 로그온 때 실행되므로 격리가 아니다.
    /// </summary>
    internal static string? StartupDir(out string? error)
    {
        error = null;
        string real;
        try { real = Environment.GetFolderPath(Environment.SpecialFolder.Startup); } catch { real = ""; }
        if (!Program.IsTestMode)
        {
            if (real.Length == 0) error = T.AsNoStartupDir;
            return real.Length > 0 ? real : null;
        }
        string dir = Environment.GetEnvironmentVariable("ONEKEY_TEST_STARTUP_DIR") is { Length: > 0 } e ? e : Path.Combine(Config.Dir, "startup.test");
        if (real.Length == 0 || !Path.IsPathFullyQualified(dir) || IsAtOrUnder(dir, real))
        {
            error = "test mode: startup folder is not isolated, nothing changed";
            return null;
        }
        return dir;
    }

    // ---------------------------------------------------------------- 상태 읽기

    /// <summary>
    /// 바로 가기 상태. Ours = 설치본·--tray·작업 폴더·아이콘·일반 창이 모두 맞음. OursDiffers = 설치본을 가리키지만 나머지가 다름(고쳐 써도 됨).
    /// Foreign = 같은 이름인데 다른 파일을 가리킴(보존). Unknown = 읽지 못함·손상·권한 거부(보존).
    /// </summary>
    public enum LinkState { Missing, Ours, OursDiffers, Foreign, Unknown }

    /// <summary>작업 관리자 › 시작 앱의 사용/사용 안 함(StartupApproved, 보조 진단). 형식을 모르면 Unknown.</summary>
    public enum Approval { Enabled, Disabled, Unknown }

    /// <summary>예전 등록이 남았는가(읽기만).</summary>
    public enum Legacy { None, Present, Unknown }

    public readonly record struct Status(LinkState Link, Approval Approved, Legacy Old, string? DirError);

    private readonly record struct LinkInfo(string Target, string Arguments, string WorkDir, string Icon, int IconIndex, int ShowCmd);

    public static Status Read()
    {
        Legacy old = ReadLegacy();
        string? dir = StartupDir(out string? derr);
        if (dir is null) return new(LinkState.Unknown, Approval.Unknown, old, derr);
        LinkState st = ReadState(Path.Combine(dir, LinkFileName));
        Approval ap = st is LinkState.Ours or LinkState.OursDiffers ? ReadApproval() : Approval.Enabled;
        return new(st, ap, old, null);
    }

    private static LinkState ReadState(string lnk)
    {
        int r = ReadLink(lnk, out LinkInfo info);
        if (r == 1) return LinkState.Missing;
        if (r != 0) return LinkState.Unknown;
        string target = EffectiveInstallPath;
        if (!SamePath(info.Target, target)) return LinkState.Foreign;
        bool same = info.Arguments == Args
            && SamePath(info.WorkDir, Path.GetDirectoryName(target) ?? "")
            && SamePath(info.Icon, target) && info.IconIndex == 0
            && info.ShowCmd == SW_SHOWNORMAL;
        return same ? LinkState.Ours : LinkState.OursDiffers;
    }

    // ---------------------------------------------------------------- 바꾸기 (설정 [저장]만)

    /// <summary>
    /// 설정에 맞춰 바로 가기를 만들거나 지운다. 실패 사유를 돌려준다(성공 시 null). 설치본에서만 바꾼다(API 경계, R-A3).
    /// 켜기: 이미 맞으면 다시 쓰지 않는다(파일 시각 그대로). 없거나 우리 것인데 다르면 쓰고, **다시 읽어 맞는지 확인**한다.
    /// 끄기: 우리 것(설치본을 가리킴)만 지운다. 없으면 성공. 같은 이름의 다른 바로 가기·읽지 못한 것은 어느 쪽이든 그대로 두고 알린다.
    /// 예전 Run 값·작업은 여기서 건드리지 않는다(설치 파일이 옮긴다, D4).
    /// </summary>
    public static string? Apply(bool enabled)
    {
        if (ExePath.Length == 0) return T.AsNoExe;
        if (!IsInstalledCopy) return T.SetAutostartInstalledOnly;
        string? dir = StartupDir(out string? derr);
        if (dir is null) return derr;
        string lnk = Path.Combine(dir, LinkFileName);
        LinkState st = ReadState(lnk);
        if (st == LinkState.Unknown) return T.AsLinkUnknown;
        if (st == LinkState.Foreign) return T.AsLinkForeign(lnk);
        if (enabled)
        {
            if (st == LinkState.Ours) return null;
            string target = EffectiveInstallPath;
            try { Directory.CreateDirectory(dir); } catch { return T.AsLinkWriteFailed; }
            if (!WriteLink(lnk, target, Path.GetDirectoryName(target) ?? "")) return T.AsLinkWriteFailed;
            return ReadState(lnk) == LinkState.Ours ? null : T.AsLinkWriteFailed;
        }
        if (st == LinkState.Missing) return null;
        fixed (char* p = lnk) DeleteFileW(p);
        return ReadState(lnk) == LinkState.Missing ? null : T.AsLinkDeleteFailed;
    }

    /// <summary>
    /// 설정 화면과 [저장] 뒤에 보일 안내(없으면 null). 설정 의도(on)와 실제 상태를 나눠 말한다(D6): 등록이 설정과 다름 / 작업 관리자에서
    /// 사용 안 함(1Key 가 되돌리지 않음) / 상태를 확인하지 못함 / 같은 이름의 다른 바로 가기 / 예전 등록이 남음.
    /// </summary>
    public static string? Note(bool on)
    {
        if (!IsInstalledCopy) return null;
        Status s = Read();
        var parts = new List<string>(2);
        if (s.DirError is not null) parts.Add(s.DirError);
        else if (s.Link == LinkState.Unknown) parts.Add(T.AsLinkUnknown);
        else if (s.Link == LinkState.Foreign) parts.Add(T.AsLinkForeign(LinkFileName));
        else if (on != (s.Link == LinkState.Ours) || s.Link == LinkState.OursDiffers) parts.Add(T.AsStateDiffers);
        else if (on && s.Approved == Approval.Disabled) parts.Add(T.AsDisabledInTaskManager);
        else if (on && s.Approved == Approval.Unknown) parts.Add(T.AsApprovalUnknown);
        if (s.Old == Legacy.Present) parts.Add(T.AsLegacyLeft);
        else if (s.Old == Legacy.Unknown) parts.Add(T.AsLegacyUnknown);
        return parts.Count == 0 ? null : string.Join("\n", parts);
    }

    // ---------------------------------------------------------------- 작업 관리자 › 시작 앱 (읽기만)

    /// <summary>
    /// HKCU\…\Explorer\StartupApproved\StartupFolder 의 값(이름 = 바로 가기 파일 이름). 문서화되지 않은 형식이라 진단으로만 쓴다:
    /// 값이 없으면 사용, 12바이트이고 첫 바이트가 2·6 이면 사용, 3·7 이면 사용 안 함, 그 밖은 모름. 1Key 는 이 값을 쓰지 않는다.
    /// </summary>
    private static Approval ReadApproval()
    {
        const string sub = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";
        fixed (char* s = sub)
        {
            int open = Native.RegOpenKeyExW(Native.HKEY_CURRENT_USER, s, 0, Native.KEY_READ, out nint key);
            if (open == 2) return Approval.Enabled;
            if (open != 0) return Approval.Unknown;
            try
            {
                byte* buf = stackalloc byte[64];
                uint size = 64, type = 0;
                int r;
                fixed (char* name = LinkFileName) r = Native.RegQueryValueExW(key, name, 0, (nint)(&type), buf, ref size);
                if (r == 2) return Approval.Enabled;
                if (r != 0 || type != 3 /* REG_BINARY */) return Approval.Unknown;
                return ParseApproval(new ReadOnlySpan<byte>(buf, (int)size));
            }
            finally { Native.RegCloseKey(key); }
        }
    }

    internal static Approval ParseApproval(ReadOnlySpan<byte> v)
    {
        if (v.Length != 12) return Approval.Unknown;
        return v[0] switch { 2 or 6 => Approval.Enabled, 3 or 7 => Approval.Disabled, _ => Approval.Unknown };
    }

    // ---------------------------------------------------------------- 예전 등록 (읽기만, D4·R-A4)

    /// <summary>
    /// 예전 Run 값(HKCU Run "1Key") 또는 예전 작업("1Key_AutoStart")이 남았는가. 작업은 schtasks 를 부르지 않고
    /// %SystemRoot%\System32\Tasks\&lt;이름&gt; 파일 속성만 본다(일반 권한으로 있음/없음이 구분됨, 2026-10-03 VM 실측).
    /// </summary>
    private static Legacy ReadLegacy()
    {
        Legacy run = ReadRunExists();
        Legacy task;
        string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Tasks", "1Key_AutoStart" + Suffix);
        uint a;
        fixed (char* p = file) a = GetFileAttributesW(p);
        if (a != 0xFFFFFFFF) task = Legacy.Present;
        else
        {
            int e = Marshal.GetLastPInvokeError();
            task = e is 2 or 3 ? Legacy.None : Legacy.Unknown;
        }
        if (run == Legacy.Present || task == Legacy.Present) return Legacy.Present;
        if (run == Legacy.Unknown || task == Legacy.Unknown) return Legacy.Unknown;
        return Legacy.None;
    }

    private static Legacy ReadRunExists()
    {
        const string sub = @"Software\Microsoft\Windows\CurrentVersion\Run";
        fixed (char* s = sub)
        {
            int open = Native.RegOpenKeyExW(Native.HKEY_CURRENT_USER, s, 0, Native.KEY_READ, out nint key);
            if (open == 2) return Legacy.None;
            if (open != 0) return Legacy.Unknown;
            try
            {
                uint size = 0;
                int r;
                fixed (char* name = "1Key" + Suffix) r = Native.RegQueryValueExW(key, name, 0, 0, null, ref size);
                return r == 0 ? Legacy.Present : r == 2 ? Legacy.None : Legacy.Unknown;
            }
            finally { Native.RegCloseKey(key); }
        }
    }

    // ---------------------------------------------------------------- IShellLinkW (COM, 자식 프로세스 없음)

    private static readonly Guid CLSID_ShellLink = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid IID_IShellLinkW = new("000214F9-0000-0000-C000-000000000046");
    private static readonly Guid IID_IPersistFile = new("0000010b-0000-0000-C000-000000000046");

    /// <summary>
    /// COM 작업은 짧은 전용 스레드에서 한다(호출 스레드의 COM 상태를 바꾸지 않게). CoInitializeEx 가 실패하면(이미 다른 모드 포함)
    /// 그 스레드의 기존 상태로 진행하고, 성공했을 때만 CoUninitialize 한다.
    /// </summary>
    private static T OnComThread<T>(Func<T> work, T fallback)
    {
        T result = fallback;
        var t = new Thread(() =>
        {
            int hr = CoInitializeEx(0, 2 /* COINIT_APARTMENTTHREADED */);
            try { result = work(); }
            catch { result = fallback; }
            finally { if (hr >= 0) CoUninitialize(); }
        }) { IsBackground = true };
        t.Start();
        return t.Join(10000) ? result : fallback;
    }

    private static nint* V(nint p) => *(nint**)p;
    private static void Release(nint p) { if (p != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)V(p)[2])(p); }

    /// <summary>0 = 읽음, 1 = 없음, -1 = 알 수 없음(폴더·권한 거부·손상·COM 실패).</summary>
    private static int ReadLink(string path, out LinkInfo info)
    {
        info = default;
        uint attr;
        fixed (char* p = path) attr = GetFileAttributesW(p);
        if (attr == 0xFFFFFFFF)
        {
            int e = Marshal.GetLastPInvokeError();
            return e is 2 or 3 ? 1 : -1;
        }
        if ((attr & 0x10 /* DIRECTORY */) != 0) return -1;
        var (ok, got) = OnComThread(() => ReadLinkCom(path), (false, default(LinkInfo)));
        info = got;
        return ok ? 0 : -1;
    }

    private static (bool, LinkInfo) ReadLinkCom(string path)
    {
        nint link = 0, pf = 0;
        Guid clsid = CLSID_ShellLink, iid = IID_IShellLinkW, iidPf = IID_IPersistFile;
        try
        {
            if (CoCreateInstance(ref clsid, 0, 1, ref iid, out link) < 0 || link == 0) return (false, default);
            if (((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)V(link)[0])(link, &iidPf, &pf) < 0 || pf == 0) return (false, default);
            int hr;
            fixed (char* p = path) hr = ((delegate* unmanaged[Stdcall]<nint, char*, uint, int>)V(pf)[5])(pf, p, 0 /* STGM_READ */);   // IPersistFile::Load
            if (hr < 0) return (false, default);
            const int N = 1024;
            char* buf = stackalloc char[N];
            static string Get(nint link, int slot, char* b)
            {
                b[0] = '\0';
                if (((delegate* unmanaged[Stdcall]<nint, char*, int, int>)V(link)[slot])(link, b, N) < 0) return "";
                return Environment.ExpandEnvironmentVariables(new string(b));
            }
            buf[0] = '\0';
            if (((delegate* unmanaged[Stdcall]<nint, char*, int, void*, uint, int>)V(link)[3])(link, buf, N, null, 4 /* SLGP_RAWPATH */) < 0) return (false, default);   // GetPath
            string target = Environment.ExpandEnvironmentVariables(new string(buf));
            string args = Get(link, 10, buf);      // GetArguments
            string work = Get(link, 8, buf);       // GetWorkingDirectory
            int show = 0, iconIdx = 0;
            ((delegate* unmanaged[Stdcall]<nint, int*, int>)V(link)[14])(link, &show);   // GetShowCmd
            buf[0] = '\0';
            string icon = ((delegate* unmanaged[Stdcall]<nint, char*, int, int*, int>)V(link)[16])(link, buf, N, &iconIdx) < 0   // GetIconLocation
                ? "" : Environment.ExpandEnvironmentVariables(new string(buf));
            return (true, new LinkInfo(target, args, work, icon, iconIdx, show));
        }
        finally { Release(pf); Release(link); }
    }

    private static bool WriteLink(string path, string target, string workDir)
        => OnComThread(() => WriteLinkCom(path, target, workDir), false);

    private static bool WriteLinkCom(string path, string target, string workDir)
    {
        nint link = 0, pf = 0;
        Guid clsid = CLSID_ShellLink, iid = IID_IShellLinkW, iidPf = IID_IPersistFile;
        try
        {
            if (CoCreateInstance(ref clsid, 0, 1, ref iid, out link) < 0 || link == 0) return false;
            bool Set(int slot, string v) { fixed (char* p = v) return ((delegate* unmanaged[Stdcall]<nint, char*, int>)V(link)[slot])(link, p) >= 0; }
            if (!Set(20, target) || !Set(11, Args) || !Set(9, workDir) || !Set(7, "1Key")) return false;   // SetPath, SetArguments, SetWorkingDirectory, SetDescription
            int hr;
            fixed (char* p = target) hr = ((delegate* unmanaged[Stdcall]<nint, char*, int, int>)V(link)[17])(link, p, 0);   // SetIconLocation
            if (hr < 0) return false;
            if (((delegate* unmanaged[Stdcall]<nint, int, int>)V(link)[15])(link, SW_SHOWNORMAL) < 0) return false;    // SetShowCmd
            if (((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)V(link)[0])(link, &iidPf, &pf) < 0 || pf == 0) return false;
            fixed (char* p = path) hr = ((delegate* unmanaged[Stdcall]<nint, char*, int, int>)V(pf)[6])(pf, p, 1);      // IPersistFile::Save(fRemember)
            return hr >= 0;
        }
        catch { return false; }
        finally { Release(pf); Release(link); }
    }

    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint coinit);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, nint outer, uint ctx, ref Guid iid, out nint obj);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetFileAttributesW(char* path);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DeleteFileW(char* path);

    // ---------------------------------------------------------------- 검증 전용 (ONEKEY_TEST=1)

    /// <summary>검증 전용: 설정 [저장]과 같은 경로(Apply, 성공이면 남은 안내 Note)를 부르고 결과 문구를 돌려준다. 다 맞으면 "OK".</summary>
    public static string SelfTestApply(bool enabled) => Apply(enabled) ?? Note(enabled) ?? "OK";

    /// <summary>검증 전용: 지금 상태 "링크|승인|예전|폴더오류" 와 바로 가기 경로.</summary>
    public static string SelfTestState()
    {
        Status s = Read();
        string? dir = StartupDir(out _);
        return $"{s.Link}|{s.Approved}|{s.Old}|{(s.DirError is null ? "-" : "dir")}|{(dir is null ? "" : Path.Combine(dir, LinkFileName))}";
    }

    /// <summary>검증 전용: 시험 폴더의 바로 가기를 주어진 값으로 쓴다(다른 대상·다른 인자 준비). 실제 시작 프로그램 폴더면 쓰지 않는다.</summary>
    public static bool SelfTestWriteLink(string target, string args)
    {
        if (!Program.IsTestMode) return false;
        string? dir = StartupDir(out _);
        if (dir is null) return false;
        try { Directory.CreateDirectory(dir); } catch { return false; }
        string lnk = Path.Combine(dir, LinkFileName);
        return OnComThread(() =>
        {
            nint link = 0, pf = 0;
            Guid clsid = CLSID_ShellLink, iid = IID_IShellLinkW, iidPf = IID_IPersistFile;
            try
            {
                if (CoCreateInstance(ref clsid, 0, 1, ref iid, out link) < 0) return false;
                fixed (char* p = target) ((delegate* unmanaged[Stdcall]<nint, char*, int>)V(link)[20])(link, p);
                fixed (char* p = args) ((delegate* unmanaged[Stdcall]<nint, char*, int>)V(link)[11])(link, p);
                if (((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)V(link)[0])(link, &iidPf, &pf) < 0) return false;
                fixed (char* p = lnk) return ((delegate* unmanaged[Stdcall]<nint, char*, int, int>)V(pf)[6])(pf, p, 1) >= 0;
            }
            finally { Release(pf); Release(link); }
        }, false);
    }

    // ---------------------------------------------------------------- 등록 도구(설치 폴더 tools\*.cmd, 0.3.103 시험)

    // 2026-10-06 사용자: 사용자가 직접 실행한 등록 스크립트(관리자 로그온 작업)는 재부팅 뒤에도 Defender 에 걸리지 않았다. 앱·설치 파일이
    // 직접 등록하면 걸렸으므로(0.2.148·0.2.150·0.2.154) 1Key 는 등록 코드를 갖지 않고, 설치 폴더의 그 스크립트를 실행만 한다(시험 A).
    public const string ToolAdmin = "Register-1Key-AdminStartup.cmd", ToolUser = "Register-1Key-Startup.cmd", ToolRemove = "Remove-1Key-Startup.cmd";

    /// <summary>설치 폴더의 등록 도구 경로(없으면 null).</summary>
    public static string? ToolPath(string name)
    {
        if (!IsInstalledCopy) return null;
        string p = Path.Combine(Path.GetDirectoryName(ExePath) ?? "", "tools", name);
        return File.Exists(p) ? p : null;
    }

    /// <summary>
    /// 지금 자동 실행이 등록돼 있는가(읽기만): 시작 프로그램 폴더의 1Key.lnk 가 설치본을 가리킴, 또는 등록 도구가 만든 관리자 로그온 작업
    /// "1Key-AdminStartup-&lt;SID&gt;" 가 있음(작업 파일 속성만 — schtasks 를 부르지 않는다, 일반 권한으로 보임 2026-10-06 실측).
    /// </summary>
    public static bool IsOn()
    {
        if (!IsInstalledCopy) return false;
        var s = Read();
        if (s.Link is LinkState.Ours or LinkState.OursDiffers) return true;
        return AdminTaskPresent();
    }

    private static bool AdminTaskPresent()
    {
        string? sid;
        try { sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value; } catch { sid = null; }
        if (string.IsNullOrEmpty(sid)) return false;
        string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Tasks", "1Key-AdminStartup-" + sid + Suffix);
        uint a;
        fixed (char* p = file) a = GetFileAttributesW(p);
        return a != 0xFFFFFFFF;
    }

    /// <summary>
    /// 등록 도구 하나를 연다(명령 창 — 안내는 스크립트가 한다). 등록 도구(관리자용)는 스스로 UAC 를 묻는다. 지우기 도구는 스스로 묻지 않으므로
    /// 관리자 로그온 작업이 있으면 관리자 권한(runas)으로 연다. 시험 모드에서는 실행하지 않고 설정 폴더에 이름만 남긴다. 실패 사유(성공 시 null).
    /// </summary>
    public static string? RunTool(string name)
    {
        bool elevate = name == ToolRemove && AdminTaskPresent();
        string? path = ToolPath(name);
        if (path is null) return T.SetAutostartToolMissing;
        if (Program.IsTestMode)
        {
            try { File.AppendAllText(Path.Combine(Config.Dir, "autostart-tool-runs.txt"), name + (elevate ? " runas" : "") + "\n"); } catch { }
            return null;
        }
        string dir = Path.GetDirectoryName(path) ?? "";
        fixed (char* verb = elevate ? "runas" : "open")
        fixed (char* file = path)
        fixed (char* wd = dir)
        {
            var info = new Native.SHELLEXECUTEINFOW
            {
                cbSize = (uint)sizeof(Native.SHELLEXECUTEINFOW),
                fMask = 0,
                lpVerb = verb,
                lpFile = file,
                lpDirectory = wd,
                nShow = Native.SW_SHOWNORMAL,
            };
            return Native.ShellExecuteExW(ref info) ? null : T.SetAutostartToolFailed;
        }
    }

    /// <summary>
    /// 시험 B(내장형, 0.3.105→0.3.106 창 숨김): 등록 도구 파일 대신 1Key 안의 같은 명령(<see cref="AutostartScripts"/>)으로 powershell.exe 를
    /// 창 없이 실행하고(-WindowStyle Hidden, SW_HIDE) 끝날 때까지 기다린다. 관리자 등록은 runas(UAC), 지우기는 관리자 작업이 있으면 runas, 바로 가기
    /// 등록은 보통 권한. 결과: 성공이면 null, 스크립트가 실패하면 그 사유(스크립트가 남긴 파일), 실행·확인 실패면 일반 안내. 시험 모드는 실행하지 않고 이름만 남긴다.
    /// </summary>
    public static string? RunBuiltin(string name)
    {
        if (!IsInstalledCopy) return T.SetAutostartInstalledOnly;
        string script = name == ToolAdmin ? AutostartScripts.RegisterAdmin : name == ToolUser ? AutostartScripts.RegisterUser : AutostartScripts.Remove;
        bool elevate = name == ToolAdmin || (name == ToolRemove && AdminTaskPresent());
        if (Program.IsTestMode)
        {
            try { File.AppendAllText(Path.Combine(Config.Dir, "autostart-tool-runs.txt"), "builtin " + name + (elevate ? " runas" : "") + "\n"); } catch { }
            return null;
        }
        string resultFile = Path.Combine(Path.GetTempPath(), "1Key-autostart-result.txt");
        try { if (File.Exists(resultFile)) File.Delete(resultFile); } catch { }
        string ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        string args = "-NoLogo -NoProfile -WindowStyle Hidden -Command \"" + script + "\"";
        string dir = Path.GetDirectoryName(ExePath) ?? "";
        nint handle = 0;
        fixed (char* verb = elevate ? "runas" : "open")
        fixed (char* file = ps)
        fixed (char* par = args)
        fixed (char* wd = dir)
        {
            var info = new Native.SHELLEXECUTEINFOW
            {
                cbSize = (uint)sizeof(Native.SHELLEXECUTEINFOW),
                fMask = 0x00000040,   // SEE_MASK_NOCLOSEPROCESS: 프로세스 핸들을 받아 끝날 때까지 기다린다
                lpVerb = verb,
                lpFile = file,
                lpParameters = par,
                lpDirectory = wd,
                nShow = Native.SW_HIDE,
            };
            if (!Native.ShellExecuteExW(ref info)) return T.SetAutostartToolFailed;   // 실행 실패·UAC 취소
            handle = info.hProcess;
        }
        uint exit = 0;
        if (handle != 0)
        {
            Native.WaitForSingleObject(handle, 60000);
            Native.GetExitCodeProcess(handle, out exit);
            Native.CloseHandle(handle);
        }
        // 스크립트가 catch 에서 사유를 파일로 남긴다(성공이면 없음). 파일이 있으면 그 사유, 없고 실패 코드면 일반 안내.
        try { if (File.Exists(resultFile)) { string msg = File.ReadAllText(resultFile).Trim(); File.Delete(resultFile); if (msg.Length > 0) return msg; } } catch { }
        return exit == 0 ? null : T.SetAutostartToolFailed;
    }

    // ---------------------------------------------------------------- 관리자 권한 재시작

    /// <summary>같은 실행 파일을 관리자 권한으로 다시 띄운다. 성공하면 true(호출 측에서 종료할 것).</summary>
    public static bool RestartElevated(string extraArgs)
    {
        string path = ExePath;
        if (path.Length == 0) return false;

        fixed (char* verb = "runas")
        fixed (char* file = path)
        fixed (char* args = extraArgs)
        {
            var info = new Native.SHELLEXECUTEINFOW
            {
                cbSize = (uint)sizeof(Native.SHELLEXECUTEINFOW),
                fMask = 0x00000040,      // SEE_MASK_NOCLOSEPROCESS
                lpVerb = verb,
                lpFile = file,
                lpParameters = extraArgs.Length > 0 ? args : null,
                nShow = Native.SW_SHOWNORMAL,
            };
            if (!Native.ShellExecuteExW(ref info)) return false;   // 사용자가 UAC 를 취소한 경우 포함
            if (info.hProcess != 0) Native.CloseHandle(info.hProcess);
            return true;
        }
    }
}
