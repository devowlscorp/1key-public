using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 끌어 놓기로 바로 실행 항목 추가(2026-10-05 사용자 결정 "넣기"): 탐색기의 exe·바로 가기·폴더, 시작 메뉴의 앱을 1Key 창에 놓으면 그 항목의 편집 화면.
/// - 1Key 가 보통 권한이면 OLE 끌어 놓기(IDropTarget): 셸 항목 목록(Shell IDList Array)에서 파일 경로나 스토어·패키지 앱 ID 를, 없으면 CF_HDROP 의 경로를 읽는다.
/// - 관리자 권한이면 Windows(UIPI)가 낮은 권한 창에서 오는 OLE 끌어 놓기를 막는다. 그때는 파일 끌어 놓기(WM_DROPFILES)만 받도록 메시지 필터를 연다 —
///   경로가 없는 스토어 앱은 받을 수 없다(사용 안내에 적음). OLE 와 WM_DROPFILES 는 같이 쓰지 않는다(OLE 를 등록하면 탐색기가 그쪽만 쓴다).
/// - 받을지는 UI 가 정한다(canAccept: 잠금 해제·목록 계열 화면). 놓인 것은 UI 스레드에 메시지로 넘긴다(끌기 도중에 확인 창을 띄우지 않게).
/// 경로·이름은 기록하지 않는다.
/// </summary>
internal static unsafe class LaunchDrop
{
    public const uint WM_LAUNCH_DROP = Native.WM_APP + 43;   // 놓인 항목이 있음(Take 로 꺼낸다)
    public const uint WM_DROPFILES = 0x0233;
    private const int MaxItems = 8;

    private static nint _hwnd;
    private static Func<bool> _canAccept = () => false;
    private static nint* _vtbl;
    private static nint _obj;
    private static bool _ok, _ole;
    private static List<(string Path, string Name)> _pending = new();
    private static uint _cfIdList;

    /// <summary>창에 끌어 놓기를 연다. elevated = 1Key 가 관리자 권한(그때는 파일만).</summary>
    public static void Register(nint hwnd, Func<bool> canAccept, bool elevated)
    {
        _hwnd = hwnd; _canAccept = canAccept;
        if (elevated)
        {
            // 낮은 권한의 탐색기에서 오는 파일 끌어 놓기에 필요한 세 메시지만 이 창에 연다: WM_DROPFILES(놓인 파일 목록 핸들),
            // WM_COPYGLOBALDATA(0x49, 그 목록 메모리를 권한 경계 너머로 넘기는 시스템 내부 메시지), WM_COPYDATA(같은 배관이 쓰는 경우 대비).
            // 1Key 는 WM_COPYDATA·0x49 를 해석하지 않는다. 셋 중 하나라도 못 열면 끌어 놓기를 받지 않는다(Codex C28-4: 결과 확인).
            bool ok = ChangeWindowMessageFilterEx(hwnd, WM_DROPFILES, 1 /* MSGFLT_ALLOW */, 0)
                   && ChangeWindowMessageFilterEx(hwnd, 0x0049 /* WM_COPYGLOBALDATA */, 1, 0)
                   && ChangeWindowMessageFilterEx(hwnd, 0x004A /* WM_COPYDATA */, 1, 0);
            DragAcceptFiles(hwnd, ok);
            return;
        }
        if (OleInitialize(0) < 0) return;
        fixed (char* n = "Shell IDList Array") _cfIdList = RegisterClipboardFormatW(n);
        _vtbl = (nint*)NativeMemory.Alloc(7, (nuint)sizeof(nint));
        _vtbl[0] = (nint)(delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)&QueryInterface;
        _vtbl[1] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&AddRef;
        _vtbl[2] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&Release;
        _vtbl[3] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, uint, long, uint*, int>)&DragEnter;
        _vtbl[4] = (nint)(delegate* unmanaged[Stdcall]<nint, uint, long, uint*, int>)&DragOver;
        _vtbl[5] = (nint)(delegate* unmanaged[Stdcall]<nint, int>)&DragLeave;
        _vtbl[6] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, uint, long, uint*, int>)&Drop;
        _obj = (nint)NativeMemory.Alloc(1, (nuint)sizeof(nint));
        *(nint*)_obj = (nint)_vtbl;   // 창이 사는 동안 하나뿐인 객체: 참조 수를 세지 않는다
        _ole = RegisterDragDrop(hwnd, _obj) >= 0;
    }

    public static void Revoke(nint hwnd) { if (_ole) { RevokeDragDrop(hwnd); _ole = false; } }

    /// <summary>놓인 항목을 꺼낸다(UI 스레드).</summary>
    public static List<(string Path, string Name)> Take() { var r = _pending; _pending = new(); return r; }

    /// <summary>WM_DROPFILES 의 경로들(이름은 빈 값 — 파일 이름에서 정한다). 핸들을 닫는다.</summary>
    public static List<(string Path, string Name)> FromHDrop(nint hdrop, bool finish)
    {
        var r = new List<(string, string)>();
        try
        {
            uint n = DragQueryFileW(hdrop, 0xFFFFFFFF, null, 0);
            char* buf = stackalloc char[1024];
            for (uint i = 0; i < n && r.Count < MaxItems; i++)
            {
                uint len = DragQueryFileW(hdrop, i, buf, 1024);
                if (len > 0 && len < 1024) r.Add((new string(buf, 0, (int)len), ""));
            }
        }
        catch { }
        finally { if (finish) DragFinish(hdrop); }
        return r;
    }

    // ------------------------------------------------------------------ IDropTarget

    private static readonly Guid IID_IUnknown = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid IID_IDropTarget = new("00000122-0000-0000-C000-000000000046");

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })]
    private static int QueryInterface(nint self, Guid* riid, nint* ppv)
    {
        if (*riid == IID_IUnknown || *riid == IID_IDropTarget) { *ppv = self; return 0; }
        *ppv = 0; return unchecked((int)0x80004002);   // E_NOINTERFACE
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })]
    private static uint AddRef(nint self) => 1;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })]
    private static uint Release(nint self) => 1;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })]
    private static int DragEnter(nint self, nint data, uint keys, long pt, uint* effect)
    {
        try { _ok = _canAccept() && (Has(data, (ushort)_cfIdList) || Has(data, 15 /* CF_HDROP */)); } catch { _ok = false; }
        *effect = _ok ? Pick(*effect) : 0;
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })]
    private static int DragOver(nint self, uint keys, long pt, uint* effect)
    {
        *effect = _ok ? Pick(*effect) : 0;
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })]
    private static int DragLeave(nint self) { _ok = false; return 0; }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })]
    private static int Drop(nint self, nint data, uint keys, long pt, uint* effect)
    {
        uint want = *effect;
        *effect = 0;
        try
        {
            if (!_ok || !_canAccept()) return 0;
            var items = ReadIdList(data);
            if (items.Count == 0) items = ReadHDrop(data);
            if (items.Count == 0) return 0;
            _pending = items;
            *effect = Pick(want);
            Native.PostMessageW(_hwnd, WM_LAUNCH_DROP, 0, 0);
        }
        catch { }
        finally { _ok = false; }
        return 0;
    }

    /// <summary>바로 가기(링크) 표시가 되면 링크, 아니면 복사. 원본을 옮기거나 지우는 일은 없다.</summary>
    private static uint Pick(uint allowed) => (allowed & 4) != 0 ? 4u /* DROPEFFECT_LINK */ : (allowed & 1) != 0 ? 1u /* COPY */ : 0u;

    // ------------------------------------------------------------------ 데이터 읽기

    [StructLayout(LayoutKind.Sequential)] private struct FORMATETC { public ushort cfFormat; public nint ptd; public uint dwAspect; public int lindex; public uint tymed; }
    [StructLayout(LayoutKind.Sequential)] private struct STGMEDIUM { public uint tymed; public nint hGlobal; public nint pUnkForRelease; }

    private static nint* V(nint p) => *(nint**)p;

    private static bool Has(nint data, ushort cf)
    {
        if (cf == 0) return false;
        var f = new FORMATETC { cfFormat = cf, dwAspect = 1, lindex = -1, tymed = 1 /* TYMED_HGLOBAL */ };
        return ((delegate* unmanaged[Stdcall]<nint, FORMATETC*, int>)V(data)[5])(data, &f) == 0;   // QueryGetData
    }

    private static bool Get(nint data, ushort cf, out STGMEDIUM m)
    {
        m = default;
        if (cf == 0) return false;
        var f = new FORMATETC { cfFormat = cf, dwAspect = 1, lindex = -1, tymed = 1 };
        STGMEDIUM s;
        if (((delegate* unmanaged[Stdcall]<nint, FORMATETC*, STGMEDIUM*, int>)V(data)[3])(data, &f, &s) < 0) return false;   // GetData
        m = s;
        if (s.tymed != 1 || s.hGlobal == 0) { ReleaseStgMedium(&s); return false; }
        return true;
    }

    private static List<(string, string)> ReadHDrop(nint data)
    {
        if (!Get(data, 15, out STGMEDIUM m)) return new();
        try { return FromHDrop(m.hGlobal, finish: false); }
        finally { ReleaseStgMedium(&m); }
    }

    private static readonly Guid IID_IShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    private const string AppsFolderClsid = "{4234D49B-0245-4DF3-B780-3893943456E1}";

    /// <summary>셸 항목 목록(CIDA): 파일 시스템 항목은 경로, 앱 목록(시작 메뉴)의 항목은 스토어·패키지 앱 ID 또는 알려진 폴더 기준 경로.</summary>
    private static List<(string, string)> ReadIdList(nint data)
    {
        var r = new List<(string, string)>();
        if (!Get(data, (ushort)_cfIdList, out STGMEDIUM m)) return r;
        byte* p = (byte*)GlobalLock(m.hGlobal);
        try
        {
            // 다른 프로그램이 준 메모리는 믿지 않는다: 실제 크기 안에 개수·위치 표·모든 항목 목록(PIDL)이 들어 있을 때만 읽는다(Codex C28-4)
            if (p == null || !CidaValid(p, (long)(ulong)GlobalSize(m.hGlobal), out uint n)) return r;
            uint* off = (uint*)(p + 4);
            for (uint i = 1; i <= n && r.Count < MaxItems; i++)
            {
                nint abs = ILCombine((nint)(p + off[0]), (nint)(p + off[i]));
                if (abs == 0) continue;
                nint item = 0;
                try
                {
                    Guid iid = IID_IShellItem;
                    if (SHCreateItemFromIDList(abs, &iid, &item) < 0 || item == 0) continue;
                    if (ItemTarget(item) is { } t) r.Add(t);
                }
                finally
                {
                    if (item != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)V(item)[2])(item);
                    ILFree(abs);
                }
            }
        }
        catch { }
        finally { if (p != null) GlobalUnlock(m.hGlobal); ReleaseStgMedium(&m); }
        return r;
    }

    /// <summary>
    /// CIDA(셸 항목 목록) 경계 검사: 개수(1–4096), 개수+1 개의 위치 표, 각 위치의 항목 목록이 버퍼 안에서 0 으로 끝나는지
    /// (각 항목 cb ≥ 3, 버퍼를 넘지 않음, 항목 256개 이하). 하나라도 어긋나면 false — 끌어 놓기를 받지 않는다(Codex C28-4).
    /// </summary>
    internal static bool CidaValid(byte* p, long size, out uint n)
    {
        n = 0;
        if (p == null || size < 12) return false;
        uint count = *(uint*)p;
        if (count == 0 || count > 4096) return false;
        if (4L + 4L * (count + 1) > size) return false;
        uint* off = (uint*)(p + 4);
        for (uint i = 0; i <= count; i++) if (!PidlInside(p, size, off[i])) return false;
        n = count;
        return true;
    }

    private static bool PidlInside(byte* p, long size, uint off)
    {
        long pos = off;
        for (int k = 0; k < 256; k++)
        {
            if (pos + 2 > size) return false;
            ushort cb = *(ushort*)(p + pos);
            if (cb == 0) return true;
            if (cb < 3 || pos + cb > size) return false;
            pos += cb;
        }
        return false;
    }

    /// <summary>시험(--selftest-site): 바이트 배열 전체를 CIDA 로 보고 검사한다.</summary>
    internal static bool CidaValidBytes(byte[] b) { fixed (byte* p = b) return CidaValid(b.Length == 0 ? null : p, b.Length, out _); }

    /// <summary>
    /// 셸 항목 → (경로, 이름): 파일 시스템 항목은 (경로, ""), 앱 목록(시작 메뉴)의 스토어·패키지 앱은 ("shell:AppsFolder\앱 ID", 보이는 이름),
    /// 알려진 폴더 기준 데스크톱 앱은 (실제 exe 경로, 이름), 그 밖의 앱 목록 항목은 ("", 이름) = 목록에서 고르라는 안내. 앱 목록 밖의 가상 항목은 null.
    /// </summary>
    internal static (string Path, string Name)? ItemTarget(nint item)
    {
        string name = Name(item, 0 /* SIGDN_NORMALDISPLAY */) ?? "";
        string? path = Name(item, 0x80058000 /* SIGDN_FILESYSPATH */);
        if (path is not null) return (path, "");
        string? parse = Name(item, 0x80028000 /* SIGDN_DESKTOPABSOLUTEPARSING */);
        if (parse is null || !parse.Contains(AppsFolderClsid, StringComparison.OrdinalIgnoreCase)) return null;
        string rel = parse[(parse.IndexOf(AppsFolderClsid, StringComparison.OrdinalIgnoreCase) + AppsFolderClsid.Length)..].TrimStart('\\');
        if (LaunchStore.IsAumid(rel)) return (Launcher.AppsFolderPrefix + rel, name);
        // 시작 메뉴의 데스크톱 앱: "{알려진 폴더}\하위\앱.exe" 모양이면 실제 경로로. 그 밖(앱 ID 만 있는 데스크톱 앱)은 빈 경로
        return (KnownFolderPath(rel) ?? "", name);
    }

    private static string? Name(nint item, uint sigdn)
    {
        char* s = null;
        if (((delegate* unmanaged[Stdcall]<nint, uint, char**, int>)V(item)[5])(item, sigdn, &s) < 0 || s == null) return null;   // GetDisplayName
        try { return new string(s); }
        finally { CoTaskMemFree((nint)s); }
    }

    /// <summary>"{GUID}\나머지" → 알려진 폴더 경로 + 나머지(있는 .exe 만). 아니면 null.</summary>
    private static string? KnownFolderPath(string rel)
    {
        if (rel.Length < 40 || rel[0] != '{') return null;
        int close = rel.IndexOf('}');
        if (close != 37 || !Guid.TryParse(rel[..(close + 1)], out Guid kf)) return null;
        nint pp = 0;
        if (SHGetKnownFolderPath(&kf, 0, 0, &pp) < 0 || pp == 0) return null;
        try
        {
            string full = Path.Combine(new string((char*)pp), rel[(close + 1)..].TrimStart('\\'));
            return full.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(full) ? full : null;
        }
        catch { return null; }
        finally { CoTaskMemFree(pp); }
    }

    [DllImport("ole32.dll")] private static extern int OleInitialize(nint reserved);
    [DllImport("ole32.dll")] private static extern int RegisterDragDrop(nint hwnd, nint target);
    [DllImport("ole32.dll")] private static extern int RevokeDragDrop(nint hwnd);
    [DllImport("ole32.dll")] private static extern void ReleaseStgMedium(STGMEDIUM* m);
    [DllImport("ole32.dll")] private static extern void CoTaskMemFree(nint p);
    [DllImport("user32.dll")] private static extern uint RegisterClipboardFormatW(char* name);
    [DllImport("user32.dll")] private static extern bool ChangeWindowMessageFilterEx(nint hwnd, uint msg, uint action, nint info);
    [DllImport("shell32.dll")] private static extern void DragAcceptFiles(nint hwnd, bool accept);
    [DllImport("shell32.dll")] private static extern uint DragQueryFileW(nint hdrop, uint i, char* buf, uint cch);
    [DllImport("shell32.dll")] private static extern void DragFinish(nint hdrop);
    [DllImport("shell32.dll")] private static extern nint ILCombine(nint a, nint b);
    [DllImport("shell32.dll")] private static extern void ILFree(nint p);
    [DllImport("shell32.dll")] private static extern int SHCreateItemFromIDList(nint pidl, Guid* iid, nint* item);
    [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath(Guid* kf, uint flags, nint token, nint* path);
    [DllImport("kernel32.dll")] private static extern void* GlobalLock(nint h);
    [DllImport("kernel32.dll")] private static extern nuint GlobalSize(nint h);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint h);
}
