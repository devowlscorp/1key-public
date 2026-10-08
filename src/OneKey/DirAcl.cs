using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 1Key 설정 폴더(%LOCALAPPDATA%\1Key)의 권한을 현재 사용자 + SYSTEM 만으로 좁힌다(상속 끊음). 2026-10-05 사용자 결정 "기존 폴더까지",
/// Codex 15:19 Q5 조건:
/// - 부모(LocalAppData)·사용자 프로필은 바꾸지 않는다. 연결(재분석 지점)·네트워크 위치·다른 소유자의 폴더는 따라가 바꾸지 않는다.
///   소유자는 현재 사용자, 또는 관리자 권한으로 실행 중일 때 Administrators(관리자 권한 프로세스가 만든 폴더의 기본 소유자).
/// - 폴더 상속을 끊는 것만으로 기존 파일의 명시 ACE 가 없어지지 않으므로, 안의 파일·폴더는 명시 ACE 를 지우고 부모를 물려받게 한 뒤
///   폴더와 모든 항목의 최종 권한을 다시 읽어 확인한다. 하나라도 어긋나면 "보호됨"이라 하지 않고 알린다.
/// - 이 권한은 다른 Windows 사용자·다른 계정의 관리자 도구가 읽지 못하게 할 뿐이다. 같은 계정의 프로그램·관리자(소유권을 가져오는 경우)·
///   SYSTEM 으로 도는 백업은 막지 않는다. 회사 IT 가 다른 계정으로 이 폴더를 백업하던 경우에는 읽지 못하게 된다(사용 안내에 적음).
/// </summary>
internal static unsafe class DirAcl
{
    public enum State { NotPresent, AlreadyProtected, Protected, SkippedReparse, SkippedNetwork, SkippedOwner, SkippedChild, Failed }

    /// <summary>이 실행에서 마지막으로 확인한 상태(저장할 때 다시 좁히지 않아도 되는지). 실패·건너뜀은 시작할 때 알렸다.</summary>
    public static State LastState { get; private set; } = State.NotPresent;

    private const int SE_FILE_OBJECT = 1;
    private const uint OWNER_SECURITY_INFORMATION = 0x1, DACL_SECURITY_INFORMATION = 0x4;
    private const uint PROTECTED_DACL_SECURITY_INFORMATION = 0x80000000, UNPROTECTED_DACL_SECURITY_INFORMATION = 0x20000000;
    private const ushort SE_DACL_PROTECTED = 0x1000;
    private const byte ACCESS_ALLOWED_ACE_TYPE = 0;

    /// <summary>검증 전용: 다음 Secure 를 실패로(시험 안에서 켠다).</summary>
    internal static bool TestFailOnce;

    /// <summary>현재 사용자 + SYSTEM 모두 모든 권한, 하위 폴더·파일에 물려줌, 상속 끊음.</summary>
    private static string Sddl(string userSid) => $"D:P(A;OICI;FA;;;{userSid})(A;OICI;FA;;;SY)";

    /// <summary>검증 전용: 다음 EnsureDir 의 좁힌 만들기를 실패로.</summary>
    internal static bool TestFailCreateOnce;

    /// <summary>
    /// 폴더가 없으면 처음부터 좁힌 권한으로 만든다(생성 단계부터 제한). 좁힌 만들기가 안 되면 보통 만들기로 대신하지 않고 false — 저장하는 쪽이
    /// 저장 실패(SaveFail.Protect)로 알린다(Codex 16:22 R39-1). 이미 있는 폴더는 true(시작할 때 <see cref="Secure"/> 가 확인·알림을 맡았다).
    /// </summary>
    public static bool EnsureDir(string dir)
    {
        if (Directory.Exists(dir)) return true;
        if (TestFailCreateOnce) { TestFailCreateOnce = false; return false; }
        string? parent = Path.GetDirectoryName(Path.GetFullPath(dir));
        if (parent is not null && !Directory.Exists(parent)) Directory.CreateDirectory(parent);   // 부모(LocalAppData)는 보통 있다 — 바꾸지 않는다
        string? sid = CurrentUserSid();
        nint sd = 0;
        try
        {
            if (sid is null || !ConvertStringSecurityDescriptorToSecurityDescriptorW(Sddl(sid), 1, out sd, out _)) return false;
            var sa = new SECURITY_ATTRIBUTES { nLength = (uint)sizeof(SECURITY_ATTRIBUTES), lpSecurityDescriptor = sd };
            if (!CreateDirectoryW(dir, &sa)) return false;
            LastState = State.Protected;
            return true;
        }
        finally { if (sd != 0) Native.LocalFree(sd); }
    }

    /// <summary>
    /// 폴더와 안의 모든 항목을 현재 사용자 + SYSTEM 만으로. 이미 그러면 쓰지 않는다. 결과와 기술 정보(알림용).
    /// 바꾸기 **전에** 범위를 모두 확인한다(Codex 16:22 R39-2): 폴더·부모 경로에 연결(재분석 지점)이 있거나, 안에 연결·다른 소유자의 항목이
    /// 하나라도 있으면 아무것도 바꾸지 않고 그만둔다(SetNamedSecurityInfo 는 상속 ACE 를 기존 자식에 저절로 퍼뜨리므로 "나중에 건너뛰기"로는
    /// 그 자식이 안 바뀐다고 보장할 수 없다). 경로로 확인하고 바꾸는 사이의 경합은 남는다 — 같은 계정 악성 프로그램 방어가 아니다.
    /// 중간에 실패하면 어느 항목이 실패했는지와 함께 Failed(부분 적용).
    /// </summary>
    public static (State State, string Detail) Secure(string dir)
    {
        var r = SecureCore(dir);
        LastState = r.State;
        return r;
    }

    private static (State State, string Detail) SecureCore(string dir)
    {
        try
        {
            if (Program.TestFails("acl:fail") || TestFailOnce) { TestFailOnce = false; return (State.Failed, "test"); }
            if (!Directory.Exists(dir)) return (State.NotPresent, "");
            string full = Path.GetFullPath(dir).TrimEnd('\\');
            for (string? d = full; d is not null && d.Length > 3; d = Path.GetDirectoryName(d))   // 폴더와 그 부모들(드라이브 뿌리 제외)
                if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) return (State.SkippedReparse, d == full ? "reparse point" : "parent reparse point");
            if (IsRemote(full)) return (State.SkippedNetwork, "network location");
            string? user = CurrentUserSid();
            if (user is null) return (State.Failed, "user SID");
            if (!OwnedByUs(full, user)) return (State.SkippedOwner, "owner");
            var items = new List<string>();
            foreach (string item in Items(full))
            {
                if ((File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0) return (State.SkippedChild, Path.GetRelativePath(full, item) + ": link");
                if (!OwnedByUs(item, user)) return (State.SkippedChild, Path.GetRelativePath(full, item) + ": owner");
                items.Add(item);
            }
            if (Check(full, user) is null) return (State.AlreadyProtected, "");

            if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(Sddl(user), 1, out nint sd, out _)) return (State.Failed, "SDDL " + Marshal.GetLastPInvokeError());
            try
            {
                GetSecurityDescriptorDacl(sd, out _, out nint dacl, out _);
                uint e = SetNamedSecurityInfoW(full, SE_FILE_OBJECT, DACL_SECURITY_INFORMATION | PROTECTED_DACL_SECURITY_INFORMATION, 0, 0, dacl, 0);
                if (e != 0) return (State.Failed, "set folder " + e);
            }
            finally { Native.LocalFree(sd); }
            // 안의 항목: 명시 ACE 를 지우고 부모(방금 좁힌 폴더)를 물려받게. 실패한 항목은 모아 알린다
            byte* empty = stackalloc byte[16];
            if (!InitializeAcl((nint)empty, 16, 2)) return (State.Failed, "partial: empty ACL");
            var failed = new List<string>();
            foreach (string item in items)
            {
                uint e = SetNamedSecurityInfoW(item, SE_FILE_OBJECT, DACL_SECURITY_INFORMATION | UNPROTECTED_DACL_SECURITY_INFORMATION, 0, 0, (nint)empty, 0);
                if (e != 0) failed.Add(Path.GetRelativePath(full, item) + " " + e);
            }
            if (failed.Count > 0) return (State.Failed, "partial: " + string.Join(", ", failed.Take(5)));
            string? bad = Check(full, user);
            return bad is null ? (State.Protected, "") : (State.Failed, "partial: " + bad);
        }
        catch (Exception ex) { return (State.Failed, ex.GetType().Name); }
    }

    /// <summary>폴더(보호된 DACL)와 안의 모든 항목의 ACE 가 허용 ACE 이고 현재 사용자·SYSTEM 뿐인가. 맞으면 null, 아니면 어긋난 항목.</summary>
    internal static string? Check(string dir, string userSid)
    {
        if (!AclOnly(dir, userSid, requireProtected: true)) return Path.GetFileName(dir.TrimEnd('\\'));
        foreach (string item in Items(dir))
        {
            if ((File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0) return Path.GetFileName(item) + " (link)";
            if (!AclOnly(item, userSid, requireProtected: false)) return Path.GetFileName(item);
        }
        return null;
    }

    private static IEnumerable<string> Items(string dir)
    {
        var stack = new Stack<string>();
        stack.Push(dir);
        while (stack.Count > 0)
        {
            string d = stack.Pop();
            foreach (string f in Directory.EnumerateFileSystemEntries(d))
            {
                yield return f;
                if (Directory.Exists(f) && (File.GetAttributes(f) & FileAttributes.ReparsePoint) == 0) stack.Push(f);
            }
        }
    }

    private static bool AclOnly(string path, string userSid, bool requireProtected)
    {
        if (GetNamedSecurityInfoW(path, SE_FILE_OBJECT, DACL_SECURITY_INFORMATION, out _, out _, out nint dacl, out _, out nint sd) != 0) return false;
        try
        {
            if (dacl == 0) return false;   // DACL 없음 = 모두 허용
            if (requireProtected && (!GetSecurityDescriptorControl(sd, out ushort ctl, out _) || (ctl & SE_DACL_PROTECTED) == 0)) return false;
            ACL_SIZE_INFORMATION info;
            if (!GetAclInformation(dacl, &info, (uint)sizeof(ACL_SIZE_INFORMATION), 2 /* AclSizeInformation */)) return false;
            // 허용 ACE 만, 현재 사용자·SYSTEM 만, 그리고 둘 다 실제로 모든 권한(FILE_ALL_ACCESS)이 이 항목에 적용돼야 한다(INHERIT_ONLY 아님).
            // 폴더는 그 ACE 가 하위 폴더·파일에도 물려지게(OI|CI) 되어 있어야 한다(R39-1: SID 만 보고 보호됐다고 하지 않는다).
            bool user = false, system = false;
            for (uint i = 0; i < info.AceCount; i++)
            {
                if (!GetAce(dacl, i, out nint ace)) return false;
                byte type = ((byte*)ace)[0], flags = ((byte*)ace)[1];
                uint mask = *(uint*)((byte*)ace + 4);
                if (type != ACCESS_ALLOWED_ACE_TYPE) return false;
                string? sid = SidString(ace + 8);
                if (sid != userSid && sid != "S-1-5-18") return false;
                if ((flags & 0x08 /* INHERIT_ONLY */) != 0) continue;   // 이 항목에는 적용되지 않는 ACE: 충분 여부에 세지 않는다
                bool full = (mask & 0x1F01FF) == 0x1F01FF || (mask & 0x10000000 /* GENERIC_ALL */) != 0;
                bool inherits = !requireProtected || (flags & 0x03) == 0x03;   // OBJECT_INHERIT | CONTAINER_INHERIT
                if (!full || !inherits) continue;
                if (sid == userSid) user = true; else system = true;
            }
            return user && system;
        }
        finally { Native.LocalFree(sd); }
    }

    private static bool OwnedByUs(string path, string userSid)
    {
        if (GetNamedSecurityInfoW(path, SE_FILE_OBJECT, OWNER_SECURITY_INFORMATION, out nint owner, out _, out _, out _, out nint sd) != 0) return false;
        try
        {
            string? o = SidString(owner);
            return o == userSid || (o == "S-1-5-32-544" && Native.IsElevated());   // 관리자 권한 프로세스가 만든 것의 기본 소유자 = Administrators
        }
        finally { Native.LocalFree(sd); }
    }

    private static bool IsRemote(string dir)
    {
        string full = Path.GetFullPath(dir);
        if (full.StartsWith(@"\\", StringComparison.Ordinal)) return true;
        string? root = Path.GetPathRoot(full);
        return root is not null && GetDriveTypeW(root) == 4 /* DRIVE_REMOTE */;
    }

    private static string? SidString(nint sid)
    {
        if (sid == 0 || !ConvertSidToStringSidW(sid, out nint s)) return null;
        try { return Marshal.PtrToStringUni(s); }
        finally { Native.LocalFree(s); }
    }

    /// <summary>이 프로세스 토큰의 사용자 SID(문자열).</summary>
    internal static string? CurrentUserSid()
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x0008 /* TOKEN_QUERY */, out nint tok)) return null;
        try
        {
            GetTokenInformation(tok, 1 /* TokenUser */, null, 0, out uint need);
            if (need == 0 || need > 4096) return null;
            byte* buf = stackalloc byte[(int)need];
            if (!GetTokenInformation(tok, 1, buf, need, out _)) return null;
            return SidString(*(nint*)buf);   // TOKEN_USER.User.Sid
        }
        finally { Native.CloseHandle(tok); }
    }

    /// <summary>검증 전용: 항목의 DACL 을 SDDL 로 바꾼다("{user}" 는 현재 사용자 SID). 이름만 맞고 권한이 모자란 상태를 만들어 본다.</summary>
    internal static bool TestSetSddl(string path, string sddl)
    {
        if (CurrentUserSid() is not string u || !ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl.Replace("{user}", u), 1, out nint sd, out _)) return false;
        try { GetSecurityDescriptorDacl(sd, out _, out nint dacl, out _); return SetNamedSecurityInfoW(path, SE_FILE_OBJECT, DACL_SECURITY_INFORMATION | PROTECTED_DACL_SECURITY_INFORMATION, 0, 0, dacl, 0) == 0; }
        finally { Native.LocalFree(sd); }
    }

    /// <summary>검증 전용: 항목의 DACL 을 SDDL 문자열로(바뀌지 않았는지 비교용).</summary>
    internal static string? TestSddlOf(string path)
    {
        if (GetNamedSecurityInfoW(path, SE_FILE_OBJECT, DACL_SECURITY_INFORMATION, out _, out _, out _, out _, out nint sd) != 0) return null;
        try { return ConvertSecurityDescriptorToStringSecurityDescriptorW(sd, 1, DACL_SECURITY_INFORMATION, out nint str, out _) ? Marshal.PtrToStringUni(str) is string r ? Free(str, r) : null : null; }
        finally { Native.LocalFree(sd); }
        static string Free(nint p, string r) { Native.LocalFree(p); return r; }
    }

    /// <summary>검증 전용: 항목에 Everyone 읽기 명시 ACE 를 더한다(기존 파일의 명시 ACE 가 지워지는지 시험).</summary>
    internal static bool TestAddEveryoneRead(string path)
    {
        if (GetNamedSecurityInfoW(path, SE_FILE_OBJECT, DACL_SECURITY_INFORMATION, out _, out _, out nint dacl, out _, out nint sd) != 0) return false;
        try
        {
            var ea = new EXPLICIT_ACCESS_W { grfAccessPermissions = 0x120089 /* FILE_GENERIC_READ */, grfAccessMode = 2 /* GRANT_ACCESS */, grfInheritance = 0 };
            if (!ConvertStringSidToSidW("S-1-1-0", out nint everyone)) return false;   // Everyone(이름은 Windows 언어마다 달라 SID 로)
            try
            {
                ea.Trustee.TrusteeForm = 0 /* TRUSTEE_IS_SID */;
                ea.Trustee.TrusteeType = 5 /* TRUSTEE_IS_WELL_KNOWN_GROUP */;
                ea.Trustee.ptstrName = everyone;
                if (SetEntriesInAclW(1, &ea, dacl, out nint newAcl) != 0) return false;
                try { return SetNamedSecurityInfoW(path, SE_FILE_OBJECT, DACL_SECURITY_INFORMATION | UNPROTECTED_DACL_SECURITY_INFORMATION, 0, 0, newAcl, 0) == 0; }
                finally { Native.LocalFree(newAcl); }
            }
            finally { Native.LocalFree(everyone); }
        }
        finally { Native.LocalFree(sd); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct SECURITY_ATTRIBUTES { public uint nLength; public nint lpSecurityDescriptor; public int bInheritHandle; }
    [StructLayout(LayoutKind.Sequential)] private struct ACL_SIZE_INFORMATION { public uint AceCount, AclBytesInUse, AclBytesFree; }
    [StructLayout(LayoutKind.Sequential)] private struct TRUSTEE_W { public nint pMultipleTrustee; public int MultipleTrusteeOperation, TrusteeForm, TrusteeType; public nint ptstrName; }
    [StructLayout(LayoutKind.Sequential)] private struct EXPLICIT_ACCESS_W { public uint grfAccessPermissions; public int grfAccessMode; public uint grfInheritance; public TRUSTEE_W Trustee; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateDirectoryW(string path, SECURITY_ATTRIBUTES* sa);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern uint GetDriveTypeW(string root);
    [DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(nint token, int cls, void* info, uint len, out uint need);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string sddl, uint rev, out nint sd, out uint size);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern bool ConvertSidToStringSidW(nint sid, out nint str);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern bool ConvertStringSidToSidW(string str, out nint sid);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern bool ConvertSecurityDescriptorToStringSecurityDescriptorW(nint sd, uint rev, uint info, out nint str, out uint len);
    [DllImport("advapi32.dll")] private static extern bool GetSecurityDescriptorDacl(nint sd, out bool present, out nint dacl, out bool defaulted);
    [DllImport("advapi32.dll")] private static extern bool GetSecurityDescriptorControl(nint sd, out ushort control, out uint rev);
    [DllImport("advapi32.dll")] private static extern bool GetAclInformation(nint acl, void* info, uint len, int cls);
    [DllImport("advapi32.dll")] private static extern bool GetAce(nint acl, uint index, out nint ace);
    [DllImport("advapi32.dll")] private static extern bool InitializeAcl(nint acl, uint len, uint rev);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern uint GetNamedSecurityInfoW(string name, int type, uint info, out nint owner, out nint group, out nint dacl, out nint sacl, out nint sd);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern uint SetNamedSecurityInfoW(string name, int type, uint info, nint owner, nint group, nint dacl, nint sacl);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern uint SetEntriesInAclW(uint count, EXPLICIT_ACCESS_W* entries, nint oldAcl, out nint newAcl);
}
