using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 바로 실행 아이콘을 UI 밖에서 읽는다(Codex 18:27 R36-1). 띠와 편집 화면은 기본 아이콘을 먼저 보이고, 다 읽으면 WM_ICON_READY 로 바꾼다.
/// - 대기열에서 동시에 MaxActive 개까지 각자의 도우미 스레드로 읽는다(Pump). ItemMs 를 넘기면 UI 에 Abandoned 로 알리고, 늦게라도 끝나면 그 아이콘을
///   새 결과로 다시 알린다 — 3초를 넘긴 아이콘이 끝내 안 뜨던 결함(0.3.16), 한 줄로 읽어 느린 하나가 뒤를 막던 것(0.3.18 첫 빌드, 2026-10-05 사용자).
///   느린 것이 MaxActive 개 모두면 나머지는 기다린다(멈춘 위치가 스레드를 끝없이 쌓지 않게).
/// - 고정 드라이브의 로컬 경로만 읽는다: UNC·매핑 드라이브·이동식, 실제 위치(재분석 지점 뒤)가 네트워크인 것은 읽지 않는다.
/// - 결과를 쓸지(세대가 같은지)는 UI 가 정하고, 쓰지 않는 아이콘은 UI 가 해제한다.
/// </summary>
internal static unsafe class IconLoader
{
    internal sealed class Req
    {
        public string Id = "", Key = "", Target = "", Icon = "";
        public bool Folder, App;   // App: Target 은 스토어·패키지 앱 ID
        public bool Web;           // 웹사이트: Target = 항목 id, Index 0 = 사용자 이미지 → 파비콘, 1 = 파비콘만(편집 중 [기본])
        public int Index;
        public nint Result;
        public bool Abandoned;   // 시간 안에 못 끝냄: 늦게 끝나면 같은 Id·Key 의 새 결과로 한 번 더 알린다
        public bool Finished;    // 도우미가 끝남(_lock 안에서만 읽고 쓴다)
        public Timer? Watch;     // 시간 초과 알림(끝나면 버린다)
    }

    private const int ItemMs = 3000;
    private static readonly object _lock = new();
    private static readonly Queue<Req> _queue = new();
    private static readonly List<Req> _done = new();

    public static void Request(nint notify, uint msg, string id, string key, string target, bool folder, string icon, int index, bool app = false, bool web = false)
    {
        lock (_lock)
        {
            foreach (Req q in _queue) if (q.Id == id && q.Key == key) return;
            _queue.Enqueue(new Req { Id = id, Key = key, Target = target, Folder = folder, Icon = icon, Index = index, App = app, Web = web });
        }
        Pump(notify, msg);
    }

    public static List<Req> TakeDone()
    {
        lock (_lock) { var r = new List<Req>(_done); _done.Clear(); return r; }
    }

    /// <summary>
    /// 대기열에서 꺼내 동시에 MaxActive 개까지 읽는다(2026-10-05 사용자: 아이콘이 한참 뒤에 뜸 — 한 줄로 차례로 읽어 느린 하나가 뒤를 막았다).
    /// 하나가 ItemMs 를 넘기면 그 자리는 계속 쓰지만 UI 에는 Abandoned 로 알리고, 늦게 끝나면 새 결과로 다시 알린다.
    /// 오래 걸리는 것이 MaxActive 개 모두면 나머지는 그것이 끝날 때까지 기다린다(멈춘 네트워크 위치가 스레드를 끝없이 쌓지 않게).
    /// </summary>
    private static void Pump(nint notify, uint msg)
    {
        lock (_lock)
        {
            while (_active < MaxActive && _queue.Count > 0)
            {
                Req r = _queue.Dequeue();
                _active++;
                var h = new Thread(() => Run(r, notify, msg)) { IsBackground = true };
                h.SetApartmentState(ApartmentState.STA);
                h.Start();
                r.Watch = new Timer(_ =>
                {
                    bool tell = false;
                    lock (_lock) { if (!r.Finished && !r.Abandoned) { r.Abandoned = true; _done.Add(r); tell = true; } }
                    if (tell) Native.PostMessageW(notify, msg, 0, 0);
                }, null, ItemMs, Timeout.Infinite);
            }
        }
    }

    private static void Run(Req r, nint notify, uint msg)
    {
        int hr = CoInitializeEx(0, 2);
        nint ic = 0;
        try { ic = Load(r); } catch { }
        finally { if (hr >= 0) CoUninitialize(); }
        r.Watch?.Dispose();
        lock (_lock)
        {
            r.Finished = true;
            _active--;
            if (!r.Abandoned) { r.Result = ic; _done.Add(r); }
            else if (ic != 0) _done.Add(new Req { Id = r.Id, Key = r.Key, Result = ic });   // 늦게 끝남: 새 결과로(쓸지는 UI 가 세대로 정함)
        }
        Native.PostMessageW(notify, msg, 0, 0);
        Pump(notify, msg);
    }

    private static int _active;
    private const int MaxActive = 4;

    private static nint Load(Req r)
    {
        if (Program.TestFails("icon:slow")) Thread.Sleep(ItemMs + 2000);   // 시험: 멈춘 위치 — 기본 아이콘 그대로, UI 는 멈추지 않는다
        if (r.Web)
        {
            // 웹사이트: 설정 폴더에 저장해 둔 PNG 만(네트워크를 보지 않는다 — 파비콘은 저장 때 한 번 받는다). 없으면 0 = UI 의 글자 아이콘
            // r.Index: 0 = 사용자 이미지, 없으면 사이트 아이콘 / 1 = 사이트 아이콘만 / 2 = 사용자 이미지만(바탕색을 고른 글자 아이콘 — App.WebLoadMode)
            nint w = r.Index != 1 ? WebIcon.IconFromFile(WebIcon.UserPath(r.Target)) : 0;
            return w != 0 || r.Index == 2 ? w : WebIcon.IconFromFile(WebIcon.FavPath(r.Target));
        }
        if (r.Icon.Length > 0 && Launcher.IsLocalFixed(r.Icon, out _))
        {
            nint large = 0;
            fixed (char* p = r.Icon) if (ExtractIconExW(p, r.Index, &large, null, 1) > 0 && large != 0) return large;
        }
        if (r.App) return AppIcon(r.Target);
        if (!Launcher.IsLocalFixed(r.Target, out bool exists) || !exists) return 0;   // 기본 아이콘(UI)
        var sfi = new SHFILEINFOW();
        fixed (char* p = r.Target) SHGetFileInfoW(p, 0, ref sfi, (uint)sizeof(SHFILEINFOW), 0x100 /* SHGFI_ICON */);
        return sfi.hIcon;
    }

    private static readonly Guid IID_IShellItemImageFactory = new("BCC18B79-BA16-442F-80C4-8A59C30C463B");
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] private struct ICONINFO { public int fIcon, xHotspot, yHotspot; public nint hbmMask, hbmColor; }
    [StructLayout(LayoutKind.Sequential)] private struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public nint bmBits; }

    /// <summary>스토어·패키지 앱의 아이콘: 셸 앱 목록 항목의 그림(48px, 아이콘만)을 HICON 으로. 못 읽으면 0(기본 아이콘).</summary>
    private static nint AppIcon(string aumid)
    {
        if (!LaunchStore.IsAumid(aumid)) return 0;
        nint f = 0, bmp = 0, mask = 0;
        Guid iid = IID_IShellItemImageFactory;
        try
        {
            int hr;
            fixed (char* p = Launcher.AppsFolderPrefix + aumid) hr = SHCreateItemFromParsingName(p, 0, &iid, &f);
            if (hr < 0 || f == 0) return 0;
            var sz = new SIZE { cx = 48, cy = 48 };
            if (((delegate* unmanaged[Stdcall]<nint, SIZE, int, nint*, int>)(*(nint**)f)[3])(f, sz, 0x4 /* SIIGBF_ICONONLY */, &bmp) < 0 || bmp == 0) return 0;   // GetImage
            BITMAP bm;
            if (GetObjectW(bmp, sizeof(BITMAP), &bm) == 0 || bm.bmWidth <= 0 || bm.bmHeight == 0) return 0;
            mask = CreateBitmap(bm.bmWidth, Math.Abs(bm.bmHeight), 1, 1, null);
            var ii = new ICONINFO { fIcon = 1, hbmMask = mask, hbmColor = bmp };
            return CreateIconIndirect(&ii);
        }
        catch { return 0; }
        finally
        {
            if (mask != 0) DeleteObject(mask);
            if (bmp != 0) DeleteObject(bmp);
            if (f != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)f)[2])(f);
        }
    }

    [DllImport("shell32.dll")] private static extern int SHCreateItemFromParsingName(char* name, nint bc, Guid* iid, nint* item);
    [DllImport("gdi32.dll")] private static extern int GetObjectW(nint h, int size, void* obj);
    [DllImport("gdi32.dll")] private static extern nint CreateBitmap(int w, int h, uint planes, uint bpp, void* bits);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint h);
    [DllImport("user32.dll")] private static extern nint CreateIconIndirect(ICONINFO* ii);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFOW { public nint hIcon; public int iIcon; public uint dwAttributes; public fixed char szDisplayName[260]; public fixed char szTypeName[80]; }
    [DllImport("shell32.dll")] private static extern nint SHGetFileInfoW(char* path, uint attr, ref SHFILEINFOW sfi, uint size, uint flags);
    [DllImport("shell32.dll")] private static extern uint ExtractIconExW(char* file, int index, nint* large, nint* small, uint n);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint h);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint coinit);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
