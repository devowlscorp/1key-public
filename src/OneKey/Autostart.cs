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

    private static bool AdminTaskPresent() => AdminTaskState() == true;

    /// <summary>
    /// 관리자 로그온 작업이 있는가: true 있음 · false 없음(파일 · 경로 없음) · null 확인하지 못함(권한 거부 등 — 없음으로 보지 않는다, AS6 · Codex 2026-10-12).
    /// 검증 모드에서 ONEKEY_TEST_ADMIN_TASK_FILE 이 있으면 그 표식 파일로 대신한다(창 시험이 관리자 등록을 흉내 낸다 — RunBuiltin 참고).
    /// </summary>
    private static bool? AdminTaskState()
    {
        if (Program.IsTestMode && _testTaskOverride) return _testRemoveRan ? _testTaskAfter : _testTaskBefore;   // 자체 시험(RL26–RL28)만: 조회 실패를 흉내
        string file;
        if (TestTaskFile is string tf) file = tf;
        else
        {
            string? sid;
            try { sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value; } catch { sid = null; }
            if (string.IsNullOrEmpty(sid)) return null;
            file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Tasks", "1Key-AdminStartup-" + sid + Suffix);
        }
        uint a;
        fixed (char* p = file) a = GetFileAttributesW(p);
        if (a != 0xFFFFFFFF) return true;
        int e = Marshal.GetLastWin32Error();
        return e is 2 or 3 ? false : null;   // ERROR_FILE_NOT_FOUND · ERROR_PATH_NOT_FOUND 만 "없음"
    }

    private static bool _testTaskOverride, _testRemoveRan;
    private static bool? _testTaskBefore, _testTaskAfter;

    /// <summary>자체 시험 전용: 관리자 작업 상태를 지우기(ToolRemove) 전 · 뒤 값으로 흉내 낸다(null = 확인 못 함). off 로 끈다.</summary>
    internal static void SelfTestTaskState(bool on, bool? before = null, bool? after = null)
    {
        if (!Program.IsTestMode) return;
        _testTaskOverride = on; _testTaskBefore = before; _testTaskAfter = after; _testRemoveRan = false;
    }

    /// <summary>검증 전용: 관리자 작업 대신 쓰는 표식 파일(검증 모드 + ONEKEY_TEST_ADMIN_TASK_FILE 이 절대 경로일 때만).</summary>
    private static string? TestTaskFile
        => Program.IsTestMode && Environment.GetEnvironmentVariable("ONEKEY_TEST_ADMIN_TASK_FILE") is { Length: > 0 } f && Path.IsPathRooted(f) ? f : null;

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
    /// <summary>등록이 1분 넘게 걸리면 한 번 부른다(작업 스레드에서 — 화면에 "아직 하는 중"을 알리게). D03.</summary>
    public static Action? SlowNotice;

    public static string? RunBuiltin(string name, bool onlyTask = false)
    {
        if (!IsInstalledCopy) return T.SetAutostartInstalledOnly;
        string script = name == ToolAdmin ? AutostartScripts.RegisterAdmin : name == ToolUser ? AutostartScripts.RegisterUser : AutostartScripts.Remove;
        if (onlyTask && name == ToolRemove) script = "$onlyTask = $true; " + script;   // R04: 관리자 작업만(바로 가기는 둔다)
        // D03: 호출마다 다른 결과 파일(앞 작업이 늦게 끝나도 섞이지 않게) — 이름에는 따옴표가 들어가지 않는다
        string resultFile = Path.Combine(Path.GetTempPath(), $"1Key-autostart-result-{Environment.ProcessId}-{Guid.NewGuid():N}.txt");
        script = "$resultFile = '" + resultFile.Replace("'", "''") + "'; " + script;
        bool elevate = name == ToolAdmin || (name == ToolRemove && AdminTaskState() != false);   // C7-1: 확인 못 하면 있을 수 있다 — 지우기는 관리자 권한으로
        if (Program.IsTestMode)
        {
            if (name == ToolRemove) _testRemoveRan = true;
            try { File.AppendAllText(Path.Combine(Config.Dir, "autostart-tool-runs.txt"), "builtin " + name + (elevate ? " runas" : "") + (onlyTask ? " onlytask" : "") + "\n"); } catch { }
            // 흉내(ONEKEY_TEST_ADMIN_TASK_FILE 이 있을 때만, AS6 시험): 관리자 작업 = 표식 파일, 바로 가기 = 시험 폴더의 실제 바로 가기.
            // ONEKEY_TEST_AUTOSTART_CANCEL=1 이면 관리자 승인이 필요한 호출을 UAC [아니요]처럼 아무것도 바꾸지 않고 실패시킨다. _DELAY_MS 는 끝나기 전 잠금 · 종료를 맞추려고
            if (TestTaskFile is string tf)
            {
                if (int.TryParse(Environment.GetEnvironmentVariable("ONEKEY_TEST_AUTOSTART_DELAY_MS"), out int dms) && dms > 0) Thread.Sleep(dms);
                if (elevate && Environment.GetEnvironmentVariable("ONEKEY_TEST_AUTOSTART_CANCEL") == "1") return T.SetAutostartToolFailed;
                try
                {
                    if (name == ToolAdmin) { File.WriteAllText(tf, "task"); return null; }
                    if (name == ToolUser) return Apply(true);
                    if (File.Exists(tf)) File.Delete(tf);
                    return onlyTask ? null : Apply(false);
                }
                catch (Exception ex) { return ex.Message; }
            }
            return null;
        }
        // E01(0.5.22-F, Codex 재검토): 실제 일을 하는 PowerShell 이 작업 자물쇠를 직접 쥔다 — 1Key 가 도중에 꺼져도 자식이 끝날 때까지 자물쇠가 남아
        // 제거 · 다음 등록이 기다린다. 자물쇠는 조정 자물쇠(WithLock)가 미리 만들어 둔 것을 연다 — 못 열면 스크립트의 catch 로 가서 아무것도 바꾸지 않는다
        if (_workGate == 0) return T.SetAutostartLockFailed;   // E02: 조정 자물쇠 없이 부르지 않는다
        int t = script.IndexOf("try { ", StringComparison.Ordinal);
        if (t < 0) return T.SetAutostartLockFailed;
        // F01(0.5.22-G, Codex 재검토): 이번 호출만의 인계표. 1Key 가 자식이 끝날 때까지 열어 두고, 자식은 작업 자물쇠를 얻은 "뒤에" 이것을 연다 —
        // 자식이 자물쇠를 얻기 전에 1Key 가 강제 종료되면 인계표가 사라져, 늦게 깬 자식이 (제거 등이 새로 만든) 같은 이름의 작업 자물쇠를 얻더라도 아무것도 바꾸지 않는다
        string ticketName = "Local" + "\\" + "1Key-autostart-ticket-" + Sid + Suffix + "-" + Guid.NewGuid().ToString("N");
        nint ticket;
        fixed (char* n = ticketName) ticket = Native.CreateMutexW(0, false, n);
        if (ticket == 0) return T.SetAutostartLockFailed;
        try
        {
            script = script[..(t + 6)] + HandoffJoin(ticketName) + script[(t + 6)..];
            return RunBuiltinLocked(name, elevate, script, resultFile);
        }
        finally { Native.CloseHandle(ticket); }
    }

    /// <summary>자식 스크립트의 "try {" 바로 안에 넣는 줄: 작업 자물쇠를 얻고(버려짐도 얻은 것), 그다음 이번 호출의 인계표를 연다 — 둘 중 하나라도 못 열면 스크립트의 catch 로(아무것도 바꾸지 않음).</summary>
    internal static string HandoffJoin(string ticketName)
        => "$gate = [System.Threading.Mutex]::OpenExisting('" + WorkLockName + "'); try { [void]$gate.WaitOne() } catch [System.Threading.AbandonedMutexException] { }; "
         + "$ticket = [System.Threading.Mutex]::OpenExisting('" + ticketName + "'); ";

    // ---------------------------------------------------------------- 자물쇠(E01 · E02, 0.5.22-F)
    // 조정 자물쇠(op): 상태 읽기 → 실행 → 다시 읽기 → 예전 등록 정리까지 한 번의 바꾸기를 감싼다(1Key 의 설정 저장 · 제거 프로그램의 --autostart-remove).
    // 작업 자물쇠(work): PowerShell 자식이 쥐고 끝날 때 놓는다(종료 = 버려짐). 바꾸기를 시작할 때 작업 자물쇠가 비기를 기다린 뒤에 상태를 읽는다 —
    // 부모가 꺼진 뒤 남은 자식이 아직 등록 중이면 그것이 끝난 뒤의 상태를 본다(버려진 조정 자물쇠를 끝난 증거로 쓰지 않는다).
    // 이름은 사용자 SID 로 구분한다. 작업 자물쇠는 1Key(보통 권한)가 먼저 만들어 열어 두므로 관리자 권한 자식도 연다.
    private static nint _workGate;
    private static string Sid { get { try { return System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? "nosid"; } catch { return "nosid"; } } }
    private static string OpLockName => "Local" + "\\" + "1Key-autostart-op-" + Sid + Suffix;
    private static string WorkLockName => "Local" + "\\" + "1Key-autostart-work-" + Sid + Suffix;
    private const uint WaitAbandoned = 0x80, WaitTimeout = 0x102, Infinite = 0xFFFFFFFF;
    internal static string TestOpLockName => OpLockName;      // 자체 시험(RL07–RL09)만
    internal static string TestWorkLockName => WorkLockName;

    /// <summary>
    /// 한 번의 바꾸기를 자물쇠 안에서: wait = 앞 바꾸기 · 남은 자식이 끝나기를 기다린다(제거), 아니면 바쁘면 바로 "다른 작업 중".
    /// E02: 자물쇠를 만들거나 얻지 못하면(이름 충돌 · 권한 · WAIT_FAILED) 실행하지 않고 사유를 돌려준다. 얻은 것만 놓는다.
    /// </summary>
    public static string? WithLock(Func<string?> op, bool wait)
    {
        nint opGate, work;
        fixed (char* n = OpLockName) opGate = Native.CreateMutexW(0, false, n);
        if (opGate == 0) return T.SetAutostartLockFailed;
        uint w = Native.WaitForSingleObject(opGate, wait ? Infinite : 0);
        if (w == WaitTimeout) { Native.CloseHandle(opGate); return T.SetAutostartBusyOther; }
        if (w != 0 && w != WaitAbandoned) { Native.CloseHandle(opGate); return T.SetAutostartLockFailed; }
        try
        {
            fixed (char* n = WorkLockName) work = Native.CreateMutexW(0, false, n);
            if (work == 0) return T.SetAutostartLockFailed;
            try
            {
                // 남은 자식(앞 바꾸기의 부모가 꺼졌어도)이 끝나기를 — 끝나야 그 결과가 반영된 상태를 읽는다
                uint ww = Native.WaitForSingleObject(work, wait ? Infinite : 0);
                if (ww == WaitTimeout) return T.SetAutostartBusyOther;
                if (ww != 0 && ww != WaitAbandoned) return T.SetAutostartLockFailed;
                ReleaseMutex(work);   // 우리가 쥐지 않는다 — 자식이 쥔다(그동안 핸들은 열어 둔다: 이름이 살아 있게)
                _workGate = work;
                try { return op(); }
                finally { _workGate = 0; }
            }
            finally { Native.CloseHandle(work); }
        }
        finally { ReleaseMutex(opGate); Native.CloseHandle(opGate); }
    }

    [DllImport("kernel32.dll")] private static extern bool ReleaseMutex(nint mutex);

    private static string? RunBuiltinLocked(string name, bool elevate, string script, string resultFile)
    {
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
        if (handle == 0) return T.SetAutostartToolFailed;   // E02: 기다릴 프로세스가 없으면 끝났다고 하지 않는다
        {
            // D03: 프로세스가 끝날 때까지 기다린다(작업 스레드 — 화면은 막히지 않는다). 1분이 지나면 "아직 하는 중"을 한 번 알리고 계속 기다린다 —
            // 시간 초과를 끝으로 보고 다음 요청을 받으면 늦게 끝난 앞 작업이 뒤 요청을 뒤집었다. 강제로 끝내지 않는다(등록 중간 상태를 남기지 않게)
            bool told = false;
            uint pw;
            while ((pw = Native.WaitForSingleObject(handle, 60_000)) == WaitTimeout)
                if (!told) { told = true; try { SlowNotice?.Invoke(); } catch { } }
            bool gotExit = pw == 0 && Native.GetExitCodeProcess(handle, out exit);
            Native.CloseHandle(handle);
            if (!gotExit) return T.SetAutostartToolFailed;   // E02: WAIT_FAILED · 종료 코드를 못 읽음 = 완료가 아니다
        }
        // 스크립트가 catch 에서 사유를 파일로 남긴다(성공이면 없음). 파일이 있으면 그 사유, 없고 실패 코드면 일반 안내.
        try { if (File.Exists(resultFile)) { string msg = File.ReadAllText(resultFile).Trim(); File.Delete(resultFile); if (msg.Length > 0) return msg; } } catch { }
        return exit == 0 ? null : T.SetAutostartToolFailed;
    }

    // ---------------------------------------------------------------- 등록 방식(0.5.22-C, Codex 배포 전 검토 R04 · R05)

    /// <summary>자동 실행의 실제 등록 방식: 없음 · 보통(시작 프로그램 바로 가기) · 관리자(로그온 작업 — 있으면 이쪽).</summary>
    public enum Mode { Off, User, Admin }

    public static Mode CurrentMode()
    {
        if (!IsInstalledCopy) return Mode.Off;
        if (AdminTaskPresent()) return Mode.Admin;
        return Read().Link is LinkState.Ours or LinkState.OursDiffers ? Mode.User : Mode.Off;
    }

    /// <summary>
    /// 지금 등록 방식. 확인하지 못하면 null(관리자 작업 상태를 못 읽음, 또는 작업은 없는데 바로 가기를 못 읽음) — 꺼짐으로 보지 않는다(AS6, Codex 2026-10-12).
    /// </summary>
    public static Mode? CurrentModeOrUnknown()
    {
        if (!IsInstalledCopy) return Mode.Off;
        bool? task = AdminTaskState();
        if (task == true) return Mode.Admin;
        if (task is null) return null;
        Status s = Read();
        if (s.Link is LinkState.Ours or LinkState.OursDiffers) return Mode.User;
        return s.Link == LinkState.Unknown || s.DirError is not null ? null : Mode.Off;
    }

    private static bool LinkIsOurs() => Read().Link is LinkState.Ours or LinkState.OursDiffers;

    /// <summary>관리자 자동 실행인데 예전 보통 자동 실행(바로 가기)이 남아 있다 — 같은 방식으로 저장해도 정리를 다시 한다(D04).</summary>
    public static bool NeedsCleanup(Mode want) => want == Mode.Admin && IsInstalledCopy && AdminTaskPresent() && LinkIsOurs();

    /// <summary>
    /// 한 번의 바꾸기 결과(AS6 — 실패 사유 하나만 보고 판단하지 않게): Error 성공이면 null · Ran 바꾸기를 실제로 시작했는가(자물쇠를 못 얻으면 false — 아무것도 바꾸지 않음)
    /// · Before/After 바꾸기 앞뒤의 실제 등록(null = 확인 못 함) · BothLeft 이번 요청이 만든 바로 가기를 되돌리지 못해 관리자 작업과 함께 남았다.
    /// </summary>
    public readonly record struct SwitchResult(string? Error, bool Ran, Mode? Before, Mode? After, bool BothLeft);

    /// <summary>
    /// 요청한 방식으로 바꾼다(R04: 켜져 있는 채로 "항상 관리자 권한"만 바꾸면 예전에는 아무것도 하지 않아 관리자 작업이 남거나 바로 가기만 남았다).
    /// 새 방식을 먼저 등록하고 실제로 생겼는지 다시 읽은 뒤에 예전 것을 지운다 — 새 등록이 안 되면 예전 등록을 잃지 않는다. 오래 걸릴 수 있어(UAC · PowerShell)
    /// 화면 스레드가 아닌 곳에서 부른다(R05, AppList.StartAutostartSwitch). 앞뒤 실제 등록을 자물쇠 안에서 함께 읽어 돌려준다(AS6).
    /// </summary>
    public static SwitchResult SwitchTo(Mode want)
    {
        bool ran = false, both = false;
        Mode? before = null, after = null;
        string? err = WithLock(() =>
        {
            ran = true;
            before = CurrentModeOrUnknown();
            try { return SwitchToLocked(want, out both); }
            finally { after = CurrentModeOrUnknown(); }
        }, wait: false);
        return new SwitchResult(err, ran, before, after, both);
    }

    /// <summary>바꾸기 결과의 종류: 성공 · 원하는 방식은 됐지만 예전 것이 남음 · 예전 그대로(실패) · 다른 방식이 됨(부분) · 확인 못 함.</summary>
    public enum Outcome { Done, DoneLeft, Kept, Partial, Unverified }

    /// <summary>바꾸기 뒤 설정의 두 값: Restore 이면 AutoStart · Admin 으로 되돌려 저장한다. Actual = 판단에 쓴 실제 등록(null = 모름).</summary>
    public readonly record struct Settle(Outcome Kind, bool Restore, bool AutoStart, bool Admin, Mode? Actual);

    /// <summary>
    /// AS6-1(Codex 2026-10-12): 바꾸기가 끝난 뒤 설정의 "자동 실행" · "항상 관리자 권한" 두 값을 실제 등록에 맞춘다. 실패 사유 하나로 저장 전 값을
    /// 무조건 되돌리지 않는다 — 부분 성공이면 반대로 어긋나므로, 실제 등록(After)을 기준으로 정한다. prev = 이번 저장 전의 두 값.
    /// - 성공: 그대로. - 원하는 방식이 됐는데 사유가 있음(예전 바로 가기를 못 지움): 그대로(설정은 실제와 같다).
    /// - 자물쇠를 못 얻어 시작도 못 함: 아무것도 안 바꿨으니 저장 전 값으로.
    /// - 실제 등록을 읽었고 원하는 방식이 아님: 그 등록에 맞는 값으로(관리자 = 켬 · 켬, 보통 = 켬 · 끔, 꺼짐 = 끔 · 저장 전 관리자 값).
    /// - 확인 못 함: 바꾸기 전 등록(읽었으면)에 맞추고 아니면 저장 전 값 — 새 등록을 먼저 하고 예전 것을 지우므로 실패하면 예전 등록이 남는 쪽이 보통이다. 안내는 "확인 못 함".
    /// </summary>
    public static Settle SettleAfter(SwitchResult r, Mode want, bool prevAuto, bool prevAdmin)
    {
        // C7-1(Codex 2026-10-12): 오류 문구가 없어도 실제 등록을 확인하지 못했거나 원하는 방식이 아니면 성공이 아니다 — 등록(After)을 먼저 본다
        if (!r.Ran) return new(Outcome.Kept, true, prevAuto, prevAdmin, r.After);
        if (r.After is not Mode a)
        {
            (bool Auto, bool Admin) b = r.Before is Mode bm ? SettingsFor(bm, prevAdmin) : (prevAuto, prevAdmin);
            return new(Outcome.Unverified, true, b.Auto, b.Admin, null);
        }
        if (a == want) return r.Error is null ? new(Outcome.Done, false, false, false, a) : new(Outcome.DoneLeft, false, false, false, a);
        var v = SettingsFor(a, prevAdmin);
        return new(a == r.Before ? Outcome.Kept : Outcome.Partial, true, v.Auto, v.Admin, a);
    }

    /// <summary>등록 방식에 맞는 설정의 두 값: 관리자 = 켬 · 켬, 보통 = 켬 · 끔, 꺼짐 = 끔 · (관리자 값은 저장 전 그대로 — 수동 실행 때의 권한 설정).</summary>
    public static (bool Auto, bool Admin) SettingsFor(Mode m, bool prevAdmin) => m switch { Mode.Admin => (true, true), Mode.User => (true, false), _ => (false, prevAdmin) };

    /// <summary>안내용 등록 방식 이름.</summary>
    public static string ModeName(Mode? m) => m switch { Mode.Admin => T.SetAutostartModeAdmin, Mode.User => T.SetAutostartModeUser, Mode.Off => T.SetAutostartModeOff, _ => T.SetAutostartModeUnknown };

    private static string? SwitchToLocked(Mode want, out bool bothLeft)
    {
        bothLeft = false;
        // C7-1: 지금 등록을 확인하지 못하면 아무것도 바꾸지 않는다(관리자 작업 조회 실패를 "없음"으로 줄이지 않는다)
        if (CurrentModeOrUnknown() is not Mode cur) return T.SetAutostartCannotCheck;
        if (cur == want && !NeedsCleanup(want)) return null;
        switch (want)
        {
            case Mode.Off:
                return RunBuiltin(ToolRemove);
            case Mode.User:
            {
                bool hadLink = LinkIsOurs();
                if (!hadLink) { string? e = RunBuiltin(ToolUser); if (e is not null) return e; }
                if (!LinkIsOurs()) return T.SetAutostartToolFailed;
                if (cur != Mode.Admin) return null;
                string? r = RunBuiltin(ToolRemove, onlyTask: true);   // 바로 가기는 이미 있으니 관리자 작업만
                bool? task = AdminTaskState();
                if (task == false) return null;
                if (task is null) return r ?? T.SetAutostartCannotCheck;   // C7-1: 지웠는지 모른다 — 바로 가기는 두고(보통 방식은 됐을 수 있다) 확인 못 함으로
                // AS6-2(Codex 2026-10-12): 관리자 작업을 못 지웠다(UAC 취소 등) — 예전 관리자 등록은 그대로 두고, 이번 요청이 만든 바로 가기만 되돌린다
                // (원래 있던 바로 가기 · 다른 대상은 건드리지 않는다). 못 지우면 둘 다 남았다고 알린다
                if (!hadLink) { string? le = Apply(false); bothLeft = le is not null || LinkIsOurs(); }
                return r ?? T.SetAutostartAdminLeft(AdminTaskName);
            }
            default:   // Admin
            {
                if (cur != Mode.Admin)
                {
                    string? e = RunBuiltin(ToolAdmin);
                    if (e is not null) return e;
                    bool? made = AdminTaskState();
                    if (made is null) return T.SetAutostartCannotCheck;   // C7-1
                    if (made == false) return T.SetAutostartToolFailed;
                }
                // 관리자 작업이 대신한다 — 예전 바로 가기를 지운다. 못 지우면 성공이라 하지 않고 남은 것을 알린다(D04) — 다음 저장 때 다시 지운다(NeedsCleanup)
                if (LinkIsOurs()) { string? le = Apply(false); if (le is not null || LinkIsOurs()) return T.SetAutostartUserLeft(le ?? ""); }
                return null;
            }
        }
    }

    /// <summary>이 사용자의 관리자 로그온 작업 이름(안내용).</summary>
    public static string AdminTaskName
    {
        get
        {
            string? sid;
            try { sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value; } catch { sid = null; }
            return "1Key-AdminStartup-" + (sid ?? "?") + Suffix;
        }
    }

    /// <summary>
    /// 제거 프로그램에서(R03, 1Key.nsi — "--autostart-remove"): 이 사용자의 관리자 로그온 작업과 바로 가기를 지운다. 관리자 작업이 없으면 바로 끝(0).
    /// 남았으면 1 — 조용한 제거가 아니면 작업 이름과 지우는 방법을 알린다. 자물쇠를 얻지 못해 확인하지 못했으면 2(F02 — "남은 것 없음"으로 보지 않는다).
    /// </summary>
    public static int RemoveForUninstall(bool quiet, Action<string> notice)
    {
        // E01: 진행 중인 등록(1Key 가 꺼진 뒤 남은 자식 포함)이 끝나기를 먼저 기다린 뒤 상태를 읽는다 — "지금 작업 없음"만 보고 끝내지 않는다
        bool left = false, unknown = false; string? err = null;
        string? lockErr = WithLock(() =>
        {
            if (AdminTaskState() == false) return null;   // 없음(확인함). C7-1: 확인 못 하면 있을 수 있으니 지우기는 시도한다
            err = RunBuiltin(ToolRemove);
            bool? after = AdminTaskState();
            left = after == true; unknown = after is null;
            return null;
        }, wait: true);
        // F02(0.5.22-G, Codex 재검토): 자물쇠를 얻지 못했으면 진행 중인 등록이 없다고 말할 수 없다 — 지금 작업이 안 보여도 성공(0)으로 돌려주지 않는다
        if (lockErr is not null)
        {
            if (!quiet) notice(T.SetAutostartUninstallUnknown(AdminTaskName) + "\n\n" + lockErr);
            return 2;
        }
        if (unknown)   // C7-1: 지운 뒤 남았는지 확인하지 못했다 — "남은 것 없음(0)"이 아니라 확인 못 함(2)
        {
            if (!quiet) notice(T.SetAutostartUninstallUnknown(AdminTaskName) + (err is null ? "" : "\n\n" + err));
            return 2;
        }
        if (!left) return 0;
        if (!quiet) notice(T.SetAutostartUninstallLeft(AdminTaskName) + (err is null ? "" : "\n\n" + err));
        return 1;
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
