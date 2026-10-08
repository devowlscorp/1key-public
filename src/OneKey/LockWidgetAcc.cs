using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 잠금 위젯의 그린 버튼(화살표·−·×)을 화면 읽기·접근성 도구에 내보내는 IAccessible(MSAA). Codex R-W1(2026-10-03 16:14):
/// 이름만이 아니라 역할(누르기 버튼)·위치·상태·기본 동작과 실행을 준다. UIA 는 MSAA 프록시로 같은 정보를 읽는다(Narrator).
///
/// - 위젯 창이 WM_GETOBJECT(OBJID_CLIENT)를 받으면 이 객체를 LresultFromObject 로 건넨다. 자식 셋(단순 요소):
///   1 = 잠금 해제(처음 설정에서는 시작하기, 그림의 화살표), 2 = 최소화(−), 3 = 종료(×).
/// - 실행(accDoDefaultAction)은 마우스로 누른 것과 **같은 명령**으로 보낸다(<see cref="LockWidget.Act"/> → 같은 처리: 종료 확인·잠금 세대 검사 공유).
/// - 위치는 지금 그려진 자리(넓어지는 애니메이션·배율 반영)를 그대로 쓴다.
/// - 버튼은 키보드 초점 대상이 아니다(초점은 늘 비밀번호 칸). 키보드로는 Enter = 잠금 해제, Alt+Space = 최소화·닫기 메뉴, Alt+F4 = 종료 묻기.
///
/// NativeAOT 라 COM 래퍼 없이 vtable 을 직접 만든다(IUnknown 3 + IDispatch 4 + IAccessible 21 = 28칸). 객체는 하나뿐이고 프로세스가 끝날 때까지
/// 살아 있다(참조 수는 세지 않음). 위젯이 닫혀 있으면 모든 호출이 연결 끊김 오류를 돌려준다. 위젯을 닫을 때 CoDisconnectObject 로 바깥 프록시를 끊는다.
/// </summary>
internal static unsafe class LockWidgetAcc
{
    private static nint _obj;            // COM 객체 = vtable 포인터 하나를 담은 메모리
    private static nint* _vtbl;

    private static readonly Guid IID_IUnknown = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid IID_IDispatch = new("00020400-0000-0000-C000-000000000046");
    private static readonly Guid IID_IAccessible = new("618736E0-3C3D-11CF-810C-00AA00389B71");

    private const int S_OK = 0, S_FALSE = 1;
    private const int E_NOINTERFACE = unchecked((int)0x80004002), E_NOTIMPL = unchecked((int)0x80004001), E_INVALIDARG = unchecked((int)0x80070057);
    private const int DISP_E_MEMBERNOTFOUND = unchecked((int)0x80020003), RPC_E_DISCONNECTED = unchecked((int)0x80010108);
    private const ushort VT_EMPTY = 0, VT_I4 = 3;
    private const int ROLE_CLIENT = 10, ROLE_PUSHBUTTON = 43;
    private const int STATE_UNAVAILABLE = 0x1, STATE_INVISIBLE = 0x8000;
    public const int ChildUnlock = 1, ChildMinimize = 2, ChildExit = 3, ChildWidget = 4;   // 4: 위젯 모드 단추(0.3.124, 처음 설정에는 없음)
    private static int Count => LockWidget.WidgetBtn ? 4 : 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct VARIANT { public ushort vt, r1, r2, r3; public nint v1, v2; }   // 64비트 24바이트. 값으로 넘기는 VARIANT 도 x64 에서는 포인터로 온다.

    /// <summary>WM_GETOBJECT 의 OBJID_CLIENT 응답. 실패하면 음수(부르는 쪽은 기본 처리로).</summary>
    public static nint Lresult(nint wParam)
    {
        Ensure();
        Guid iid = IID_IAccessible;
        return LresultFromObject(&iid, wParam, _obj);
    }

    /// <summary>위젯을 닫을 때: 바깥 프로세스가 쥐고 있는 프록시를 끊는다(닫힌 위젯을 계속 읽지 않게).</summary>
    public static void Disconnect()
    {
        if (_obj != 0) CoDisconnectObject(_obj, 0);
    }

    /// <summary>시험·진단용: 자식 번호 → 위젯 그림의 부분 번호(1 = −, 2 = ×, 3 = 화살표).</summary>
    public static int PartOf(int child) => child switch { ChildUnlock => 3, ChildMinimize => 1, ChildExit => 2, ChildWidget => 6, _ => 0 };
    private static int ChildOf(int part) => part switch { 3 => ChildUnlock, 1 => ChildMinimize, 2 => ChildExit, 6 => ChildWidget, _ => 0 };

    private static void Ensure()
    {
        if (_obj != 0) return;
        _vtbl = (nint*)NativeMemory.Alloc(28, (nuint)sizeof(nint));
        int k = 0;
        _vtbl[k++] = (nint)(delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)&QueryInterface;
        _vtbl[k++] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&AddRef;
        _vtbl[k++] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&Release;
        _vtbl[k++] = (nint)(delegate* unmanaged[Stdcall]<nint, uint*, int>)&GetTypeInfoCount;
        _vtbl[k++] = (nint)(delegate* unmanaged[Stdcall]<nint, uint, uint, nint*, int>)&GetTypeInfo;
        _vtbl[k++] = (nint)(delegate* unmanaged[Stdcall]<nint, Guid*, nint, uint, uint, int*, int>)&GetIDsOfNames;
        _vtbl[k++] = (nint)(delegate* unmanaged[Stdcall]<nint, int, Guid*, uint, ushort, nint, nint, nint, uint*, int>)&Invoke;
        _vtbl[k++] = (nint)(delegate* unmanaged[Stdcall]<nint, nint*, int>)&GetParent;
        _vtbl[k++] = (nint)(delegate* unmanaged[Stdcall]<nint, int*, int>)&GetChildCount;
        _vtbl[k++] = (nint)(delegate* unmanaged[Stdcall]<nint, VARIANT*, nint*, int>)&GetChild;
        _vtbl[k++] = (nint)(delegate* unmanaged[Stdcall]<nint, VARIANT*, nint*, int>)&GetName;
        _vtbl[k++] = (nint)(delegate* unmanaged[Stdcall]<nint, VARIANT*, nint*, int>)&GetValue;
        _vtbl[k++] = (nint)(delegate* unmanaged[Stdcall]<nint, VARIANT*, nint*, int>)&GetDescription;
        _vtbl[k++] = (nint)(delegate* unmanaged[Stdcall]<nint, VARIANT*, VARIANT*, int>)&GetRole;
        _vtbl[k++] = (nint)(delegate* unmanaged[Stdcall]<nint, VARIANT*, VARIANT*, int>)&GetState;
        _vtbl[k++] = (nint)(delegate* unmanaged[Stdcall]<nint, VARIANT*, nint*, int>)&GetHelp;
        _vtbl[k++] = (nint)(delegate* unmanaged[Stdcall]<nint, nint*, VARIANT*, int*, int>)&GetHelpTopic;
        _vtbl[k++] = (nint)(delegate* unmanaged[Stdcall]<nint, VARIANT*, nint*, int>)&GetKeyboardShortcut;
        _vtbl[k++] = (nint)(delegate* unmanaged[Stdcall]<nint, VARIANT*, int>)&GetFocus;
        _vtbl[k++] = (nint)(delegate* unmanaged[Stdcall]<nint, VARIANT*, int>)&GetSelection;
        _vtbl[k++] = (nint)(delegate* unmanaged[Stdcall]<nint, VARIANT*, nint*, int>)&GetDefaultAction;
        _vtbl[k++] = (nint)(delegate* unmanaged[Stdcall]<nint, int, VARIANT*, int>)&Select;
        _vtbl[k++] = (nint)(delegate* unmanaged[Stdcall]<nint, int*, int*, int*, int*, VARIANT*, int>)&Location;
        _vtbl[k++] = (nint)(delegate* unmanaged[Stdcall]<nint, int, VARIANT*, VARIANT*, int>)&Navigate;
        _vtbl[k++] = (nint)(delegate* unmanaged[Stdcall]<nint, int, int, VARIANT*, int>)&HitTest;
        _vtbl[k++] = (nint)(delegate* unmanaged[Stdcall]<nint, VARIANT*, int>)&DoDefaultAction;
        _vtbl[k++] = (nint)(delegate* unmanaged[Stdcall]<nint, VARIANT*, nint, int>)&PutName;
        _vtbl[k++] = (nint)(delegate* unmanaged[Stdcall]<nint, VARIANT*, nint, int>)&PutValue;
        nint* obj = (nint*)NativeMemory.Alloc(1, (nuint)sizeof(nint));
        obj[0] = (nint)_vtbl;
        _obj = (nint)obj;
    }

    // ---------------------------------------------------------------- 도우미

    /// <summary>VARIANT 의 자식 번호. VT_I4 0..3 만 받는다(그 밖은 -1).</summary>
    private static int ChildId(VARIANT* v) => v != null && v->vt == VT_I4 && (int)v->v1 is int c && c >= 0 && c <= Count ? c : -1;
    private static void SetI4(VARIANT* v, int value) { if (v == null) return; *v = default; v->vt = VT_I4; v->v1 = value; }
    private static void SetEmpty(VARIANT* v) { if (v != null) *v = default; }
    private static int Bstr(nint* p, string? s)
    {
        if (p == null) return E_INVALIDARG;
        if (s is null) { *p = 0; return S_FALSE; }
        fixed (char* c = s) *p = SysAllocString(c);
        return S_OK;
    }

    private static string? NameOf(int child) => child switch
    {
        0 => "1Key",
        ChildUnlock => LockWidget.IsCreate ? T.LockStart : T.LockUnlock,
        ChildMinimize => T.CommonMinimize,
        ChildExit => T.CommonExit,
        ChildWidget => T.ListWidgetMode,
        _ => null,
    };

    // ---------------------------------------------------------------- IUnknown · IDispatch

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int QueryInterface(nint self, Guid* riid, nint* ppv)
    {
        if (ppv == null) return E_INVALIDARG;
        if (*riid == IID_IUnknown || *riid == IID_IDispatch || *riid == IID_IAccessible) { *ppv = self; return S_OK; }
        *ppv = 0;
        return E_NOINTERFACE;
    }
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] private static uint AddRef(nint self) => 2;
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] private static uint Release(nint self) => 1;
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] private static int GetTypeInfoCount(nint self, uint* n) { if (n != null) *n = 0; return S_OK; }
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] private static int GetTypeInfo(nint self, uint i, uint lcid, nint* pp) { if (pp != null) *pp = 0; return E_NOTIMPL; }
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] private static int GetIDsOfNames(nint self, Guid* riid, nint names, uint count, uint lcid, int* ids) => E_NOTIMPL;
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] private static int Invoke(nint self, int id, Guid* riid, uint lcid, ushort flags, nint p, nint r, nint e, uint* a) => E_NOTIMPL;

    // ---------------------------------------------------------------- IAccessible

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int GetParent(nint self, nint* pp)
    {
        if (pp == null) return E_INVALIDARG;
        *pp = 0;
        if (!LockWidget.Alive) return RPC_E_DISCONNECTED;
        Guid iid = IID_IDispatch;
        return CreateStdAccessibleObject(LockWidget.Handle, 0 /* OBJID_WINDOW */, &iid, pp);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int GetChildCount(nint self, int* n)
    {
        if (n == null) return E_INVALIDARG;
        *n = LockWidget.Alive ? Count : 0;
        return LockWidget.Alive ? S_OK : RPC_E_DISCONNECTED;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int GetChild(nint self, VARIANT* v, nint* pp)
    {
        if (pp == null) return E_INVALIDARG;
        *pp = 0;
        int c = ChildId(v);
        return c >= 1 ? S_FALSE /* 단순 요소: 부모에게 직접 묻는다 */ : E_INVALIDARG;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int GetName(nint self, VARIANT* v, nint* p)
    {
        if (!LockWidget.Alive) { if (p != null) *p = 0; return RPC_E_DISCONNECTED; }
        int c = ChildId(v);
        if (c < 0) { if (p != null) *p = 0; return E_INVALIDARG; }
        try { return Bstr(p, NameOf(c)); } catch { return E_INVALIDARG; }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int GetValue(nint self, VARIANT* v, nint* p) { if (p != null) *p = 0; return DISP_E_MEMBERNOTFOUND; }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int GetDescription(nint self, VARIANT* v, nint* p) { if (p != null) *p = 0; return S_FALSE; }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int GetRole(nint self, VARIANT* v, VARIANT* r)
    {
        int c = ChildId(v);
        if (c < 0) { SetEmpty(r); return E_INVALIDARG; }
        SetI4(r, c == 0 ? ROLE_CLIENT : ROLE_PUSHBUTTON);
        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int GetState(nint self, VARIANT* v, VARIANT* r)
    {
        int c = ChildId(v);
        if (c < 0) { SetEmpty(r); return E_INVALIDARG; }
        int st = 0;
        if (!LockWidget.Alive || LockWidget.IsMinimized) st |= STATE_INVISIBLE;
        if (c >= 1 && LockWidget.IsModal) st |= STATE_UNAVAILABLE;   // 확인 창이 떠 있는 동안은 누를 수 없다
        SetI4(r, st);
        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int GetHelp(nint self, VARIANT* v, nint* p) { if (p != null) *p = 0; return S_FALSE; }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int GetHelpTopic(nint self, nint* file, VARIANT* v, int* topic) { if (file != null) *file = 0; if (topic != null) *topic = 0; return S_FALSE; }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int GetKeyboardShortcut(nint self, VARIANT* v, nint* p)
    {
        int c = ChildId(v);
        try { return Bstr(p, c == ChildUnlock ? "Enter" : c == ChildExit ? "Alt+F4" : null); } catch { return E_INVALIDARG; }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int GetFocus(nint self, VARIANT* r) { SetEmpty(r); return S_FALSE; }   // 버튼은 초점을 받지 않는다(초점은 비밀번호 칸)

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int GetSelection(nint self, VARIANT* r) { SetEmpty(r); return S_FALSE; }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int GetDefaultAction(nint self, VARIANT* v, nint* p)
    {
        int c = ChildId(v);
        try { return Bstr(p, c >= 1 ? T.LockA11yPress : null); } catch { return E_INVALIDARG; }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int Select(nint self, int flags, VARIANT* v) => DISP_E_MEMBERNOTFOUND;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int Location(nint self, int* x, int* y, int* w, int* h, VARIANT* v)
    {
        if (x == null || y == null || w == null || h == null) return E_INVALIDARG;
        *x = *y = *w = *h = 0;
        if (!LockWidget.Alive) return RPC_E_DISCONNECTED;
        int c = ChildId(v);
        if (c < 0) return E_INVALIDARG;
        if (c == 0)
        {
            Native.GetWindowRect(LockWidget.Handle, out Native.RECT wr);
            *x = wr.left; *y = wr.top; *w = wr.right - wr.left; *h = wr.bottom - wr.top;
            return S_OK;
        }
        if (!LockWidget.PartScreenRect(PartOf(c), out Native.RECT r)) return S_FALSE;
        *x = r.left; *y = r.top; *w = r.right - r.left; *h = r.bottom - r.top;
        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int Navigate(nint self, int dir, VARIANT* start, VARIANT* r)
    {
        SetEmpty(r);
        int c = ChildId(start);
        if (c < 0) return E_INVALIDARG;
        int to = (c, dir) switch
        {
            (0, 7 /* NAVDIR_FIRSTCHILD */) => 1,
            (0, 8 /* NAVDIR_LASTCHILD */) => Count,
            (>= 1, 5 /* NAVDIR_NEXT */) when c < Count => c + 1,
            (>= 1, 6 /* NAVDIR_PREVIOUS */) when c > 1 => c - 1,
            _ => 0,
        };
        if (to == 0) return S_FALSE;
        SetI4(r, to);
        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int HitTest(nint self, int x, int y, VARIANT* r)
    {
        SetEmpty(r);
        if (!LockWidget.Alive) return RPC_E_DISCONNECTED;
        Native.GetWindowRect(LockWidget.Handle, out Native.RECT wr);
        if (x < wr.left || x >= wr.right || y < wr.top || y >= wr.bottom) return S_FALSE;
        SetI4(r, ChildOf(LockWidget.PartAtScreen(x, y)));
        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int DoDefaultAction(nint self, VARIANT* v)
    {
        if (!LockWidget.Alive) return RPC_E_DISCONNECTED;
        int c = ChildId(v);
        if (c < 1) return c == 0 ? DISP_E_MEMBERNOTFOUND : E_INVALIDARG;
        if (LockWidget.IsModal) return DISP_E_MEMBERNOTFOUND;   // 확인 창이 떠 있는 동안은 실행하지 않는다
        LockWidget.Act(PartOf(c));   // 마우스로 누른 것과 같은 명령(메시지로 보내 이 호출이 끝난 뒤 처리)
        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] private static int PutName(nint self, VARIANT* v, nint s) => E_NOTIMPL;
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] private static int PutValue(nint self, VARIANT* v, nint s) => E_NOTIMPL;

    // ---------------------------------------------------------------- P/Invoke

    [DllImport("oleacc.dll")] private static extern nint LresultFromObject(Guid* riid, nint wParam, nint punk);
    [DllImport("oleacc.dll")] private static extern int CreateStdAccessibleObject(nint hwnd, int idObject, Guid* riid, nint* ppv);
    [DllImport("ole32.dll")] private static extern int CoDisconnectObject(nint punk, uint reserved);
    [DllImport("oleaut32.dll")] private static extern nint SysAllocString(char* s);
}
