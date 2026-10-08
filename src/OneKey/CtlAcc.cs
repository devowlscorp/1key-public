using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace OneKey;

/// <summary>
/// 자체 그리기 컨트롤의 접근성(IAccessible, MSAA — UIA 는 MSAA 프록시로 같은 정보를 읽는다). Codex QA-06: 이름만이 아니라 역할·상태·값·위치·
/// 기본 동작을 준다(UIA 코어의 MSAA 다리로 Narrator 가 같은 정보를 읽는다 — 관리 코드 System.Windows.Automation 클라이언트는 자체 Win32 프록시를 써서 Pane 으로 본다). 대상: 버튼(OneKeyButton), 목록 행(OneKeyRow), 아이콘 띠 칸(OneKeyTile), 단축키 칸(OneKeyHotkey), 슬라이더(OneKeySlider),
/// 드롭다운(OneKeyDropdown), 스위치(OneKeyToggle). 잠금 위젯의 그린 버튼은 <see cref="LockWidgetAcc"/>.
///
/// - 이름: 창 글자. 아이콘 글꼴 글자만 있는 버튼(‹ › 등)은 <see cref="SetName"/> 로 준 이름(툴팁과 같은 글). 단축키·슬라이더·드롭다운처럼
///   제 글자가 없는 칸은 바로 앞(Z 순서) 형제 STATIC 의 글(화면의 라벨)을 이름으로 쓴다.
/// - 기본 동작은 키보드·마우스와 같은 경로로 보낸다(버튼·행·칸 = BM_CLICK, 스위치 = Space). 사용할 수 없는 컨트롤은 실행하지 않는다.
/// - 객체는 컨트롤마다 하나(vtable 은 공유), 참조 수를 센다(QueryInterface·AddRef·Release). 컨트롤이 갖는 참조 1 개로 시작한다.
///   컨트롤이 없어지면 객체를 **영구 묘비**로 만든다(창 핸들을 0 으로 — 그 뒤의 이름·상태·기본 동작은 모두 연결 끊김, 새 컨트롤을 가리키지 않음),
///   바깥 프록시를 끊고(CoDisconnectObject) 컨트롤의 참조를 놓는다. 메모리는 참조가 0 이 될 때만 풀고 **다른 컨트롤에 다시 쓰지 않는다**
///   (Codex 20:13 RW-3: 같은 프로세스 안의 직접 참조가 남아 있을 수 있다).
/// NativeAOT 라 COM 래퍼 없이 vtable 을 직접 만든다(IUnknown 3 + IDispatch 4 + IAccessible 21 = 28칸).
/// </summary>
internal static unsafe class CtlAcc
{
    private enum Kind { Button, Row, Tile, Hotkey, Slider, Dropdown, Toggle }

    private static nint* _vtbl;
    private static readonly Dictionary<nint, nint> _objs = new();   // hwnd → 객체 [vtable, hwnd(0 = 묘비), 참조 수]
    private static int _live;
    /// <summary>시험용: 아직 풀리지 않은 객체 수(살아 있는 컨트롤 + 바깥 참조가 남은 묘비).</summary>
    public static int Live => _live;
    private static readonly Dictionary<nint, string> _names = new();

    private static readonly Guid IID_IUnknown = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid IID_IDispatch = new("00020400-0000-0000-C000-000000000046");
    private static readonly Guid IID_IAccessible = new("618736E0-3C3D-11CF-810C-00AA00389B71");
    private const int S_OK = 0, S_FALSE = 1;
    private const int E_NOINTERFACE = unchecked((int)0x80004002), E_NOTIMPL = unchecked((int)0x80004001), E_INVALIDARG = unchecked((int)0x80070057);
    private const int DISP_E_MEMBERNOTFOUND = unchecked((int)0x80020003), RPC_E_DISCONNECTED = unchecked((int)0x80010108);
    private const ushort VT_I4 = 3;
    private const int OBJID_CLIENT = -4;
    private const int ROLE_PUSHBUTTON = 43, ROLE_CHECKBUTTON = 44, ROLE_COMBOBOX = 46, ROLE_TEXT = 42, ROLE_SLIDER = 51, ROLE_LISTITEM = 34;
    private const int STATE_UNAVAILABLE = 0x1, STATE_FOCUSED = 0x4, STATE_PRESSED = 0x8, STATE_CHECKED = 0x10, STATE_INVISIBLE = 0x8000, STATE_OFFSCREEN = 0x10000, STATE_FOCUSABLE = 0x100000;

    [StructLayout(LayoutKind.Sequential)]
    private struct VARIANT { public ushort vt, r1, r2, r3; public nint v1, v2; }

    /// <summary>아이콘 버튼처럼 창 글자가 이름이 아닌 컨트롤의 이름(툴팁 글과 같게).</summary>
    public static void SetName(nint h, string name) { if (h != 0) _names[h] = name; }

    /// <summary>WM_GETOBJECT. OBJID_CLIENT 이고 맡는 클래스면 LRESULT, 아니면 null(부르는 쪽은 기본 처리).</summary>
    public static nint? Get(nint h, nint wParam, nint lParam)
    {
        if ((int)lParam != OBJID_CLIENT || KindOf(h) is null) return null;
        Ensure();
        if (!_objs.TryGetValue(h, out nint obj))
        {
            obj = (nint)NativeMemory.Alloc(3, (nuint)sizeof(nint));
            ((nint*)obj)[0] = (nint)_vtbl;
            ((nint*)obj)[1] = h;
            ((nint*)obj)[2] = 1;   // 컨트롤이 가진 참조
            Interlocked.Increment(ref _live);
            _objs[h] = obj;
        }
        Guid iid = IID_IAccessible;
        return LresultFromObject(&iid, wParam, obj);
    }

    /// <summary>WM_NCDESTROY: 객체를 묘비로 만들고 바깥 프록시를 끊은 뒤 컨트롤의 참조를 놓는다(참조가 0 이면 해제, 다른 컨트롤에 다시 쓰지 않음).</summary>
    public static void Forget(nint h)
    {
        _names.Remove(h);
        if (!_objs.Remove(h, out nint obj)) return;
        ((nint*)obj)[1] = 0;          // 묘비: 다시는 어떤 창도 가리키지 않는다
        CoDisconnectObject(obj, 0);   // 바깥 프록시를 끊는다(같은 프로세스 직접 참조는 묘비가 막는다)
        ReleaseRef(obj);              // 컨트롤의 참조를 놓는다 — 남은 참조가 없으면 여기서 풀린다
    }

    /// <summary>상태·값이 바뀌었다(스위치 켬/끔, 드롭다운·슬라이더·단축키 값): 화면 읽기에 알린다.</summary>
    public static void Changed(nint h, bool value)
    {
        if (h != 0) NotifyWinEvent(value ? 0x800Eu /* EVENT_OBJECT_VALUECHANGE */ : 0x800Au /* EVENT_OBJECT_STATECHANGE */, h, OBJID_CLIENT, 0);
    }

    /// <summary>
    /// 창 영역(region)으로 잘린 컨트롤의 보이는 부분(화면 좌표 — 띠의 가로 스크롤·본문 스크롤이 상자 밖 부분을 잘라 낸다, Codex 0.3.29 회신).
    /// 2 = 다 보임(영역 없음), 1 = 일부만, 0 = 다 가려짐(vis = 창 자리 그대로 — 화면 밖에 있는 자리).
    /// </summary>
    public static int Shown(nint h, out Native.RECT vis)
    {
        Native.GetWindowRect(h, out Native.RECT wr);
        vis = wr;
        int k = GetWindowRgnBox(h, out Native.RECT b);
        if (k == 0 /* ERROR: 영역 없음 */) return 2;
        var v = new Native.RECT { left = Math.Max(wr.left, wr.left + b.left), top = Math.Max(wr.top, wr.top + b.top), right = Math.Min(wr.right, wr.left + b.right), bottom = Math.Min(wr.bottom, wr.top + b.bottom) };
        if (k == 1 /* NULLREGION */ || v.right <= v.left || v.bottom <= v.top) return 0;
        vis = v;
        return v.left == wr.left && v.top == wr.top && v.right == wr.right && v.bottom == wr.bottom ? 2 : 1;
    }

    /// <summary>스크롤로 자리·잘림이 바뀌었다(before = 바꾸기 전의 <see cref="Shown"/>): 화면 읽기에 위치 변화를, 다 가려짐이 바뀌었으면 상태 변화도 알린다.</summary>
    public static void Clipped(nint h, int before)
    {
        if (h == 0) return;
        NotifyWinEvent(0x800Bu /* EVENT_OBJECT_LOCATIONCHANGE */, h, OBJID_CLIENT, 0);
        if ((before == 0) != (Shown(h, out _) == 0)) NotifyWinEvent(0x800Au /* EVENT_OBJECT_STATECHANGE */, h, OBJID_CLIENT, 0);
    }

    private static Kind? KindOf(nint h)
    {
        var sb = new StringBuilder(64);
        GetClassNameW(h, sb, 64);
        return sb.ToString() switch
        {
            Btn.ClassName => Kind.Button, Row.ClassName => Kind.Row, Tile.ClassName => Kind.Tile, HotkeyBox.ClassName => Kind.Hotkey,
            Slider.ClassName => Kind.Slider, Dropdown.ClassName => Kind.Dropdown, Toggle.ClassName => Kind.Toggle, _ => null,
        };
    }

    private static nint Hwnd(nint self) { nint h = ((nint*)self)[1]; return h != 0 && Native.IsWindow(h) ? h : 0; }

    private static string Name(nint h, Kind k)
    {
        if (_names.TryGetValue(h, out string? n)) return n;
        string t = Native.GetWindowText(h);
        bool iconOnly = t.Length > 0 && t.All(c => c >= 0xE000 && c <= 0xF8FF);   // 아이콘 글꼴 글자(사용자 영역)
        if (k is Kind.Hotkey or Kind.Slider or Kind.Dropdown || t.Length == 0 || iconOnly)
        {
            for (nint p = Native.GetWindow(h, 3 /* GW_HWNDPREV */); p != 0; p = Native.GetWindow(p, 3))
            {
                var sb = new StringBuilder(16); GetClassNameW(p, sb, 16);
                if (sb.ToString() != "Static") break;
                string l = Native.GetWindowText(p);
                if (l.Length > 0) return l;
            }
            if (iconOnly) return "";
        }
        return t;
    }

    private static string? Value(nint h, Kind k) => k switch
    {
        Kind.Hotkey => HotkeyBox.Get(h) is var (m, v) && v != 0 ? HotkeyBox.Text(m, v) : "",
        Kind.Slider => Slider.Text(h),
        Kind.Dropdown => Dropdown.Text(h),
        _ => null,
    };

    private static void Ensure()
    {
        if (_vtbl != null) return;
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
    }

    private static bool Self(VARIANT* v) => v != null && v->vt == VT_I4 && v->v1 == 0;
    private static void SetI4(VARIANT* v, int value) { if (v == null) return; *v = default; v->vt = VT_I4; v->v1 = value; }
    private static void SetEmpty(VARIANT* v) { if (v != null) *v = default; }
    private static int Bstr(nint* p, string? s)
    {
        if (p == null) return E_INVALIDARG;
        if (s is null) { *p = 0; return S_FALSE; }
        fixed (char* c = s) *p = SysAllocString(c);
        return S_OK;
    }
    /// <summary>공통 입구: 살아 있는 창과 그 종류. 없으면 연결 끊김.</summary>
    private static int Target(nint self, VARIANT* v, out nint h, out Kind k)
    {
        h = Hwnd(self); k = default;
        if (h == 0 || KindOf(h) is not Kind kk) return RPC_E_DISCONNECTED;
        k = kk;
        return Self(v) ? S_OK : E_INVALIDARG;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int QueryInterface(nint self, Guid* riid, nint* ppv)
    {
        if (ppv == null) return E_INVALIDARG;
        if (*riid == IID_IUnknown || *riid == IID_IDispatch || *riid == IID_IAccessible) { Interlocked.Increment(ref *(long*)((nint*)self + 2)); *ppv = self; return S_OK; }
        *ppv = 0;
        return E_NOINTERFACE;
    }
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] private static uint AddRef(nint self) => (uint)Interlocked.Increment(ref *(long*)((nint*)self + 2));
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] private static uint Release(nint self) => ReleaseRef(self);

    private static uint ReleaseRef(nint self)
    {
        long n = Interlocked.Decrement(ref *(long*)((nint*)self + 2));
        if (n == 0 && ((nint*)self)[1] == 0)   // 묘비이고 아무도 쥐고 있지 않다: 이제 푼다(살아 있는 컨트롤의 객체는 컨트롤 참조 1 이 남는다)
        {
            NativeMemory.Free((void*)self);
            Interlocked.Decrement(ref _live);
        }
        return (uint)Math.Max(0, n);
    }
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] private static int GetTypeInfoCount(nint self, uint* n) { if (n != null) *n = 0; return S_OK; }
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] private static int GetTypeInfo(nint self, uint i, uint lcid, nint* pp) { if (pp != null) *pp = 0; return E_NOTIMPL; }
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] private static int GetIDsOfNames(nint self, Guid* riid, nint names, uint count, uint lcid, int* ids) => E_NOTIMPL;
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] private static int Invoke(nint self, int id, Guid* riid, uint lcid, ushort flags, nint p, nint r, nint e, uint* a) => E_NOTIMPL;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int GetParent(nint self, nint* pp)
    {
        if (pp == null) return E_INVALIDARG;
        *pp = 0;
        nint h = Hwnd(self);
        if (h == 0) return RPC_E_DISCONNECTED;
        Guid iid = IID_IDispatch;
        return CreateStdAccessibleObject(h, 0 /* OBJID_WINDOW */, &iid, pp);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int GetChildCount(nint self, int* n) { if (n == null) return E_INVALIDARG; *n = 0; return Hwnd(self) != 0 ? S_OK : RPC_E_DISCONNECTED; }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int GetChild(nint self, VARIANT* v, nint* pp) { if (pp != null) *pp = 0; return E_INVALIDARG; }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int GetName(nint self, VARIANT* v, nint* p)
    {
        int hr = Target(self, v, out nint h, out Kind k);
        if (hr != S_OK) { if (p != null) *p = 0; return hr; }
        try { return Bstr(p, Name(h, k)); } catch { return E_INVALIDARG; }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int GetValue(nint self, VARIANT* v, nint* p)
    {
        int hr = Target(self, v, out nint h, out Kind k);
        if (hr != S_OK) { if (p != null) *p = 0; return hr; }
        try { string? s = Value(h, k); if (s is null) { if (p != null) *p = 0; return DISP_E_MEMBERNOTFOUND; } return Bstr(p, s); } catch { return E_INVALIDARG; }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int GetDescription(nint self, VARIANT* v, nint* p)
    {
        int hr = Target(self, v, out nint h, out Kind k);
        if (hr != S_OK) { if (p != null) *p = 0; return hr; }
        try { return Bstr(p, k == Kind.Row && Row.Subtitle(h) is string s && s.Length > 0 ? s : null); } catch { return E_INVALIDARG; }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int GetRole(nint self, VARIANT* v, VARIANT* r)
    {
        int hr = Target(self, v, out _, out Kind k);
        if (hr != S_OK) { SetEmpty(r); return hr; }
        SetI4(r, k switch
        {
            Kind.Toggle => ROLE_CHECKBUTTON, Kind.Hotkey => ROLE_TEXT,   // 키 조합 칸: 편집 칸(UIA Edit, 값 = 키 조합). HOTKEYFIELD 는 UIA 에서 Pane 으로 보인다
            Kind.Slider => ROLE_SLIDER, Kind.Dropdown => ROLE_COMBOBOX,
            Kind.Row => ROLE_LISTITEM, _ => ROLE_PUSHBUTTON,
        });
        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int GetState(nint self, VARIANT* v, VARIANT* r)
    {
        int hr = Target(self, v, out nint h, out Kind k);
        if (hr != S_OK) { SetEmpty(r); return hr; }
        int st = 0;
        if (!Native.IsWindowEnabled(h)) st |= STATE_UNAVAILABLE;
        if (!Native.IsWindowVisible(h)) st |= STATE_INVISIBLE;
        else if (Shown(h, out _) == 0) st |= STATE_OFFSCREEN;   // 상자 밖으로 다 밀려 가려짐(일부라도 보이면 아님). 포커스가 오면 그 칸으로 스크롤된다
        if ((Native.GetWindowLongPtrW(h, Native.GWL_STYLE) & Native.WS_TABSTOP) != 0) st |= STATE_FOCUSABLE;
        if (Native.GetFocus() == h) st |= STATE_FOCUSED | STATE_FOCUSABLE;
        if (k == Kind.Toggle && Native.SendMessageW(h, 0x00F0 /* BM_GETCHECK */, 0, 0) == 1) st |= STATE_CHECKED;
        if (k is Kind.Button or Kind.Tile or Kind.Row && (Ctl.State(h) & Ctl.StPressed) != 0) st |= STATE_PRESSED;
        if (k == Kind.Button && (Ctl.Style(h) & Btn.KindMask) == Btn.Section) st |= (Ctl.Style(h) & Btn.Expanded) != 0 ? 0x200 /* EXPANDED */ : 0x400 /* COLLAPSED */;
        SetI4(r, st);
        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int GetHelp(nint self, VARIANT* v, nint* p) { if (p != null) *p = 0; return S_FALSE; }
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int GetHelpTopic(nint self, nint* file, VARIANT* v, int* topic) { if (file != null) *file = 0; if (topic != null) *topic = 0; return S_FALSE; }
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int GetKeyboardShortcut(nint self, VARIANT* v, nint* p) { if (p != null) *p = 0; return S_FALSE; }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int GetFocus(nint self, VARIANT* r)
    {
        nint h = Hwnd(self);
        if (h != 0 && Native.GetFocus() == h) { SetI4(r, 0); return S_OK; }
        SetEmpty(r);
        return S_FALSE;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int GetSelection(nint self, VARIANT* r) { SetEmpty(r); return S_FALSE; }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int GetDefaultAction(nint self, VARIANT* v, nint* p)
    {
        int hr = Target(self, v, out _, out Kind k);
        if (hr != S_OK) { if (p != null) *p = 0; return hr; }
        try { return Bstr(p, k is Kind.Hotkey or Kind.Slider ? null : k == Kind.Toggle ? T.A11yToggle : T.LockA11yPress); } catch { return E_INVALIDARG; }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int Select(nint self, int flags, VARIANT* v)
    {
        int hr = Target(self, v, out nint h, out _);
        if (hr != S_OK) return hr;
        if ((flags & 0x1 /* SELFLAG_TAKEFOCUS */) != 0 && Native.IsWindowEnabled(h)) Native.SetFocus(h);
        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int Location(nint self, int* x, int* y, int* w, int* hh, VARIANT* v)
    {
        if (x == null || y == null || w == null || hh == null) return E_INVALIDARG;
        *x = *y = *w = *hh = 0;
        int hr = Target(self, v, out nint h, out _);
        if (hr != S_OK) return hr;
        Shown(h, out Native.RECT r);   // 일부만 보이면 보이는 부분, 다 가려졌으면 창 자리(상태 OFFSCREEN)
        *x = r.left; *y = r.top; *w = r.right - r.left; *hh = r.bottom - r.top;
        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int Navigate(nint self, int dir, VARIANT* start, VARIANT* r) { SetEmpty(r); return S_FALSE; }   // 형제 이동은 창 구조로

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int HitTest(nint self, int x, int y, VARIANT* r)
    {
        SetEmpty(r);
        nint h = Hwnd(self);
        if (h == 0) return RPC_E_DISCONNECTED;
        if (Shown(h, out Native.RECT wr) == 0 || x < wr.left || x >= wr.right || y < wr.top || y >= wr.bottom) return S_FALSE;
        SetI4(r, 0);
        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int DoDefaultAction(nint self, VARIANT* v)
    {
        int hr = Target(self, v, out nint h, out Kind k);
        if (hr != S_OK) return hr;
        if (!Native.IsWindowEnabled(h) || k is Kind.Hotkey or Kind.Slider) return DISP_E_MEMBERNOTFOUND;
        // 키보드·마우스와 같은 경로(이 호출이 끝난 뒤 처리되도록 메시지로)
        if (k == Kind.Toggle) Native.PostMessageW(h, Native.WM_KEYDOWN, 0x20 /* VK_SPACE */, 0);
        else Native.PostMessageW(h, 0x00F5 /* BM_CLICK */, 0, 0);
        return S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] private static int PutName(nint self, VARIANT* v, nint s) => E_NOTIMPL;
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })] private static int PutValue(nint self, VARIANT* v, nint s) => E_NOTIMPL;

    [DllImport("oleacc.dll")] private static extern nint LresultFromObject(Guid* riid, nint wParam, nint punk);
    [DllImport("oleacc.dll")] private static extern int CreateStdAccessibleObject(nint hwnd, int idObject, Guid* riid, nint* ppv);
    [DllImport("ole32.dll")] private static extern int CoDisconnectObject(nint punk, uint reserved);
    [DllImport("oleaut32.dll")] private static extern nint SysAllocString(char* s);
    [DllImport("user32.dll")] private static extern void NotifyWinEvent(uint evt, nint hwnd, int idObject, int idChild);
    [DllImport("user32.dll")] private static extern int GetWindowRgnBox(nint hwnd, out Native.RECT box);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint h, StringBuilder s, int n);
}
