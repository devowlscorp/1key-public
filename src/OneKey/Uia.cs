using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

namespace OneKey;

/// <summary>
/// 브라우저 사이트 채우기의 화면 읽기(UI Automation, 2026-09-30 사용자 결정 "A. 화면 읽기 기술").
/// 1Key 는 브라우저 안을 **읽기만** 한다: 페이지 주소(문서의 Value = URL)와 입력란의 위치·이름·ID·비밀번호 칸 여부.
/// 입력란의 값(사용자가 적은 글)은 읽지 않는다. 글자는 지금처럼 키 입력(Injector)으로 넣고, UIA 로는 커서만 그 칸에 옮긴다.
///
/// - 지원: Chromium 계열(Chrome·Edge·Whale 등, 창 클래스 Chrome_WidgetWin_1). Firefox 는 문서가 주소를 알려 주면 같이 동작한다.
/// - UIA 호출은 전용 MTA 스레드 하나에서만 한다(브라우저가 늦게 답해도 1Key 화면이 멈추지 않게). 시간 제한:
///   연결 1초·요청 1.5초(IUIAutomation2), 호출하는 쪽도 <see cref="Run"/> 의 제한 시간만 기다린다.
/// - COM 은 vtable 번호로 직접 부른다(NativeAOT, 인터롭 없음). 번호는 Windows SDK 10.0.26100 UIAutomationClient.h 에서 확인했다:
///   IUIAutomation: CompareElements 3, ElementFromHandle 6, GetFocusedElement 8, CreatePropertyCondition 23 ·
///   IUIAutomation2: put_ConnectionTimeout 61, put_TransactionTimeout 63 ·
///   IUIAutomationElement: SetFocus 3, FindFirst 5, FindAll 6, GetCurrentPatternAs 14, get_CurrentControlType 21, get_CurrentName 23,
///   get_CurrentAutomationId 29, get_CurrentIsPassword 35, get_CurrentIsOffscreen 38, get_CurrentBoundingRectangle 43 ·
///   IUIAutomationElementArray: get_Length 3, GetElement 4 · IUIAutomationValuePattern: get_CurrentValue 4.
/// </summary>
internal static unsafe class Uia
{
    /// <summary>문서 안의 입력란 하나. Index 는 같은 종류(비밀번호 칸 / 일반 칸) 안에서의 순서(0부터).</summary>
    public readonly record struct Field(string AutomationId, string Name, bool Password, int Index, Native.RECT Rect, bool Offscreen, string Class = "");

    public enum CaptureError { None, NotBrowser, NoDocument, NotField, NoUrl, NoIdentity }
    public readonly record struct CaptureResult(CaptureError Error, string Url, Field Field);
    /// <summary>연결 한 번(2026-10-03 단순화): 누른 칸과, 같은 폼·컨테이너 안에서 찾은 짝(아이디 ↔ 비밀번호). 짝을 하나로 정하지 못하면 Partner 는 null.</summary>
    public readonly record struct PairResult(CaptureError Error, string Url, Field Field, Field? Partner);
    /// <summary>화면 종류(Codex 05:45-B): Nexacro / 일반 / 확인 불가. 확인 불가를 일반으로 다루지 않는다(클릭 금지).</summary>
    public enum Platform { General, Nexacro, Unknown }

    public readonly record struct Hit(int Slot, Field Field);
    /// <summary>주기적 확인의 결과: 창, 정규화한 주소(연결과 맞지 않으면 null), 찾은 입력란들(연결 순서).</summary>
    public sealed record ProbeResult(nint Window, string? Url, List<Hit> Hits);

    private const int TreeScope_Descendants = 4;
    private const int PropControlType = 30003;
    private const int TypeEdit = 50004, TypeDocument = 50030, TypeWindow = 50032, PatternValue = 10002, PropIsDialog = 30174;

    private static readonly BlockingCollection<Action> _queue = new();
    private static Thread? _thread;
    private static nint _uia, _condEdit, _condDoc, _condDialog, _condWindow, _condTrue, _condPopupRow;
    private static bool _ok;

    /// <summary>브라우저 창인가(창 클래스). Injector 의 브라우저 판정과 같다.</summary>
    public static bool IsBrowser(nint hwnd) => hwnd != 0 && Native.GetClassName(hwnd) is "Chrome_WidgetWin_1" or "MozillaWindowClass";

    /// <summary>
    /// 연결한 주소(정규화)를 맨 앞 탭에 띄운 브라우저 창. preferred(단축키를 누른 순간의 창)를 먼저 본다. 없으면 0.
    /// 보이는 브라우저 창만, 최대 24개. 뒤쪽 탭의 페이지는 UIA 에 나오지 않아 찾지 못한다(안내 문구에 적음).
    /// </summary>
    /// <summary>LocatePage 의 결과가 "그 주소를 띄운 창이 둘 이상"이라 고르지 않았음을 뜻하는 값.</summary>
    public const nint ManyPages = -1;

    public static nint LocatePage(nint preferred, string url, Job job)
    {
        if (!_ok) return 0;
        nint found = 0;
        var cands = new List<nint>();
        if (IsBrowser(preferred)) cands.Add(preferred);
        foreach (string cls in new[] { "Chrome_WidgetWin_1", "MozillaWindowClass" })
        {
            nint h = 0;
            fixed (char* c = cls)
                while (cands.Count < 24 && (h = Native.FindWindowExW(0, h, c, null)) != 0)
                    if (h != preferred && Native.IsWindowVisible(h) && Native.GetWindowText(h).Length > 0) cands.Add(h);
        }
        // 앞의 창이 그 주소면 그 창. 아니면 그 주소를 띄운 창이 하나뿐일 때만(둘 이상이면 같은 주소라도 다른 계정·작업일 수 있어 고르지 않는다, Codex 13:19)
        foreach (nint w in cands)
        {
            if (!job.Alive) return 0;
            nint doc = FindDocument(w);
            if (doc == 0) continue;
            bool match;
            try { match = SiteUrl.Normalize(DocumentUrl(doc)) == url; }
            finally { Release(doc); }
            if (!match) continue;
            if (w == preferred) return w;
            if (found != 0) return ManyPages;
            found = w;
        }
        return found;
    }

    // ---------------------------------------------------------------- 전용 스레드

    private static void EnsureThread()
    {
        if (_thread is not null) return;
        _thread = new Thread(Loop) { IsBackground = true, Name = "1Key-UIA" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    private static void Loop()
    {
        try { Init(); } catch { _ok = false; }
        foreach (Action a in _queue.GetConsumingEnumerable())
        {
            try { a(); } catch { }
        }
    }

    private static void Init()
    {
        if (CoInitializeEx(0, 0 /* COINIT_MULTITHREADED */) < 0) return;
        Guid clsid8 = new("e22ad333-b25f-460c-83d0-0581107395c9");   // CUIAutomation8 (Windows 8+): 시간 제한을 둘 수 있다
        Guid clsid = new("ff48dba4-60ef-4201-aa87-54103eef594e");    // CUIAutomation
        Guid iid = new("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee");      // IUIAutomation
        if (CoCreateInstance(ref clsid8, 0, 1, ref iid, out _uia) < 0 && CoCreateInstance(ref clsid, 0, 1, ref iid, out _uia) < 0) return;
        Guid iid2 = new("34723aff-0c9d-49d0-9896-7ab52df8cd8a");     // IUIAutomation2
        nint u2;
        if (((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)V(_uia)[0])(_uia, &iid2, &u2) >= 0)
        {
            ((delegate* unmanaged[Stdcall]<nint, uint, int>)V(u2)[61])(u2, 1000);   // put_ConnectionTimeout
            ((delegate* unmanaged[Stdcall]<nint, uint, int>)V(u2)[63])(u2, 1500);   // put_TransactionTimeout
            Release(u2);
        }
        _condEdit = TypeCondition(TypeEdit);
        _condDoc = TypeCondition(TypeDocument);
        _condWindow = TypeCondition(TypeWindow);
        { nint ct; _condTrue = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)V(_uia)[21])(_uia, &ct) < 0 ? 0 : ct; }   // CreateTrueCondition
        {
            // 브라우저 자체의 저장된 로그인·자동 완성 목록(ClassName). 브라우저 내부 이름이라 바뀔 수 있는 보조 신호다(못 찾아도 도착 확인 규칙은
            // 그대로, Codex 08:45-ba). Chrome 줄 "PopupRowContentView"(2026-10-03 Nexacro 업무 사이트 포커스 추적), Edge 줄 "EdgePopupRowContentView"·
            // "EdgePopupRowView"와 목록 "PopupBaseView"(2026-10-03 시험 프로필의 datalist 목록 읽기 — 0.2.127–0.2.138 은 Chrome 이름만 봐서
            // Edge 목록을 놓쳤다). 하나라도 만들지 못하면 0(확인 불가 = 출발 안 함).
            nint Cls(string name)
            {
                nint b = Native.SysAllocString(name);
                if (b == 0) return 0;
                try
                {
                    var v = new VARIANT { vt = 8 /* VT_BSTR */, lo = (int)(long)b, hi = (int)((long)b >> 32) };
                    nint c;
                    return ((delegate* unmanaged[Stdcall]<nint, int, VARIANT, nint*, int>)V(_uia)[23])(_uia, 30012 /* ClassName */, v, &c) >= 0 ? c : 0;
                }
                finally { SysFreeString(b); }
            }
            nint Or(nint a, nint b2)
            {
                if (a == 0 || b2 == 0) { Release(a); Release(b2); return 0; }
                nint c;
                int hr = ((delegate* unmanaged[Stdcall]<nint, nint, nint, nint*, int>)V(_uia)[28])(_uia, a, b2, &c);   // CreateOrCondition
                Release(a); Release(b2);
                return hr >= 0 ? c : 0;
            }
            _condPopupRow = Or(Or(Cls("PopupRowContentView"), Cls("EdgePopupRowContentView")), Or(Cls("EdgePopupRowView"), Cls("PopupBaseView")));
        }
        {
            // IsDialog(30174, Windows 10 1809+) == true. 만들지 못하면 0 → 대화 상자 확인 불가 → MainFrame 출발 안 함(R115-1)
            var v = new VARIANT { vt = 11 /* VT_BOOL */, lo = -1 /* VARIANT_TRUE */ };
            nint cond;
            _condDialog = ((delegate* unmanaged[Stdcall]<nint, int, VARIANT, nint*, int>)V(_uia)[23])(_uia, PropIsDialog, v, &cond) < 0 ? 0 : cond;
        }
        _ok = _condEdit != 0 && _condDoc != 0;
    }

    /// <summary>
    /// UIA 작업 하나의 수명(Codex V55-3). 기다리던 쪽이 시간 초과로 그만두면(<see cref="Cancel"/>), 만료 시각이 지나면, 또는 호출한 쪽의
    /// 조건(잠금 세대·활성 창 등)이 깨지면 <see cref="Alive"/> 가 false 가 된다. 큐에서 꺼낼 때와 **사용자 화면을 바꾸는 호출(SetFocus) 직전**에 본다.
    /// 이미 진행 중인 COM 호출은 끊을 수 없지만 그 뒤의 부작용과 결과 적용은 막는다.
    /// </summary>
    public sealed class Job
    {
        private volatile bool _cancelled;
        private readonly long _deadline;
        private readonly Func<bool>? _stillValid;
        public Job(int timeoutMs, Func<bool>? stillValid) { _deadline = Environment.TickCount64 + timeoutMs; _stillValid = stillValid; }
        public void Cancel() => _cancelled = true;
        public bool Alive { get { if (_cancelled || Environment.TickCount64 > _deadline) return false; try { return _stillValid?.Invoke() ?? true; } catch { return false; } } }
    }

    /// <summary>
    /// fn 을 UIA 스레드에서 실행하고 최대 timeoutMs 기다린다(작업 스레드에서만 부른다: UI 스레드는 <see cref="Post"/>).
    /// 시간 초과면 작업을 취소하고 false. 취소된 작업은 큐에서 꺼내도 실행하지 않고, 실행 중이면 화면을 바꾸기 전에 멈춘다.
    /// 대기 이벤트는 작업이 늦게 Set 할 수 있으므로 여기서 Dispose 하지 않는다(GC 가 거둔다).
    /// </summary>
    public static bool Run<TR>(Func<Job, TR> fn, int timeoutMs, Func<bool>? stillValid, out TR result)
    {
        EnsureThread();
        var job = new Job(timeoutMs, stillValid);
        var done = new ManualResetEventSlim(false);
        TR r = default!;
        bool ran = false;
        _queue.Add(() => { try { if (_ok && job.Alive) { r = fn(job); ran = true; } } finally { done.Set(); } });
        if (!done.Wait(timeoutMs)) { job.Cancel(); result = default!; return false; }
        if (!ran || !job.Alive) { result = default!; return false; }
        result = r;
        return true;
    }

    /// <summary>
    /// fn 을 UIA 스레드에 올리기만 한다(UI 스레드용, 기다리지 않음). 작업이 끝나면 then 을 UI 스레드에서 부른다(notify 창에 WM_UIA_DONE).
    /// 작업이 취소·만료됐거나 UIA 를 쓸 수 없으면 then 에 default 를 넘긴다(기다리는 쪽이 멈춰 있지 않도록 늘 알린다).
    /// then 은 적용 전에 자기 세대(잠금·새 요청)를 다시 확인해야 한다.
    /// </summary>
    public static Job Post<TR>(nint notify, Func<Job, TR> fn, int timeoutMs, Func<bool>? stillValid, Action<bool, TR> then)
    {
        EnsureThread();
        var job = new Job(timeoutMs, stillValid);
        _queue.Add(() =>
        {
            TR r = default!; bool ok = false;
            try { if (_ok && job.Alive) { r = fn(job); ok = job.Alive; } }
            catch { ok = false; }
            _done.Enqueue(() => then(ok, r));
            Native.PostMessageW(notify, Native.WM_UIA_DONE, 0, 0);
        });
        return job;
    }

    private static readonly ConcurrentQueue<Action> _done = new();

    /// <summary>본창의 WM_UIA_DONE: 끝난 작업의 뒤처리를 UI 스레드에서 차례로 부른다.</summary>
    public static void DrainDone()
    {
        while (_done.TryDequeue(out Action? a)) { try { a(); } catch { } }
    }

    // ---------------------------------------------------------------- 공개 작업 (UIA 스레드에서 부른다)

    /// <summary>등록: 지금 포커스가 있는 입력란이 이 브라우저 창의 페이지 안 입력란이면 그 주소와 위치 정보를 돌려준다.</summary>
    /// 페이지가 이름도 ID 도 주지 않는 칸은 나중에 같은 칸임을 확인할 근거가 없어 연결하지 않는다(Codex V55-2).
    /// 문서 안의 다른 문서(iframe) 속 칸은 제외한다(소속 경계를 확인할 수 없는 칸을 안전하다고 가정하지 않는다).
    public static CaptureResult CaptureFocused(nint window, Job job)
    {
        if (!_ok) return new(CaptureError.NoDocument, "", default);
        if (!IsBrowser(window)) return new(CaptureError.NotBrowser, "", default);
        nint doc = FindDocument(window);
        if (doc == 0) return new(CaptureError.NoDocument, "", default);
        nint focused = 0;
        List<(nint Element, Field Field)>? all = null;
        try
        {
            string url = DocumentUrl(doc);
            if (url.Length == 0) return new(CaptureError.NoUrl, "", default);
            if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)V(_uia)[8])(_uia, &focused) < 0 || focused == 0) return new(CaptureError.NotField, url, default);
            if (ControlType(focused) != TypeEdit) return new(CaptureError.NotField, url, default);
            if (!job.Alive) return new(CaptureError.NotField, url, default);
            all = Edits(doc, keepElements: true);
            foreach (var (el, f) in all)
            {
                if (!Same(el, focused)) continue;
                // id·이름이 없으면: 같은 종류 칸 가운데 class 가 하나뿐이면 그 class, 그 종류 칸이 이것 하나뿐이면 "*". 아니면 다시 찾을 근거가 없다.
                string cls = "";
                if (f.AutomationId.Length == 0 && f.Name.Length == 0)
                {
                    var kind = all.Where(x => x.Field.Password == f.Password).Select(x => x.Field).ToList();
                    if (f.Class.Length > 0 && f.Class != "*" && kind.Count(x => x.Class == f.Class) == 1) cls = f.Class;
                    else if (kind.Count == 1) cls = "*";
                    else return new(CaptureError.NoIdentity, url, default);
                }
                return new(CaptureError.None, url, f with { Class = cls });
            }
            return new(CaptureError.NotField, url, default);   // 포커스가 이 페이지 문서 밖(주소창·iframe 등)에 있다
        }
        finally
        {
            if (all is not null) foreach (var e in all) Release(e.Element);   // 성공·불일치·예외 모두 전부 한 번씩 (Codex V55-5)
            Release(focused); Release(doc);
        }
    }

    // ---------------------------------------------------------------- 단순화(2026-10-03): Nexacro 판별, 연결 한 번에 짝 찾기

    private static nint _walker;
    /// <summary>부모 요소(UIA 스레드). 없거나 실패하면 0. 호출한 쪽이 Release.</summary>
    private static nint Parent(nint el)
    {
        if (_walker == 0)
        {
            nint wk = 0;
            if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)V(_uia)[14])(_uia, &wk) < 0) return 0;   // get_ControlViewWalker
            _walker = wk;
        }
        nint p = 0;
        if (_walker == 0 || ((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)V(_walker)[3])(_walker, el, &p) < 0) return 0;   // GetParentElement
        return p;
    }

    /// <summary>
    /// 순수 판정(단위 시험용): Nexacro 신호 세 가지 — 입력란 class "nexainput", id 가 Nexacro 경로형("mainframe." 시작·".form." 포함·":input" 끝),
    /// 조상에 id "mainframe"·class "MainFrame" 묶음. 둘 이상이면 Nexacro, 하나도 없고 조회가 완전했으면 일반, 그 밖(신호 하나·조회 불완전)은 확인 불가.
    /// </summary>
    internal static Platform ClassifyPlatform(string fieldClass, string fieldId, bool mainFrameAncestor, bool complete)
    {
        int n = (fieldClass == "nexainput" ? 1 : 0)
              + (fieldId.StartsWith("mainframe.", StringComparison.Ordinal) && fieldId.Contains(".form.", StringComparison.Ordinal) && fieldId.EndsWith(":input", StringComparison.Ordinal) ? 1 : 0)
              + (mainFrameAncestor ? 1 : 0);
        return n >= 2 ? Platform.Nexacro : n == 0 && complete ? Platform.General : Platform.Unknown;
    }

    /// <summary>마지막 판별 결과(진단).</summary>
    public static volatile int LastPlatform;

    // 조회 성공을 값과 따로 돌려주는 읽기(Codex 06:00 R113-2: 조회 실패를 빈 값·신호 없음으로 읽지 않는다)
    private static bool TryBstr(nint el, int slot, out string v)
    {
        nint b = 0;
        if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)V(el)[slot])(el, &b) < 0) { v = ""; return false; }
        v = TakeBstr(b);
        return true;
    }
    private static bool TryInt(nint el, int slot, out int v)
    {
        int x = 0;
        bool ok = ((delegate* unmanaged[Stdcall]<nint, int*, int>)V(el)[slot])(el, &x) >= 0;
        v = x;
        return ok;
    }
    /// <summary>UIA 속성 값(정수). GetCurrentPropertyValue 가 실패하거나 정수가 아니면 false.</summary>
    private static bool TryIntProperty(nint el, int prop, out int v)
    {
        VARIANT var = default;
        v = 0;
        if (((delegate* unmanaged[Stdcall]<nint, int, VARIANT*, int>)V(el)[10])(el, prop, &var) < 0) return false;
        if (var.vt == 3) { v = var.lo; return true; }   // VT_I4
        if (var.vt == 0) return true;                  // VT_EMPTY: 속성 없음(값 0)
        return false;
    }

    /// <summary>채우기 직전: 연결한 칸이 있는 지금 문서의 화면 종류. 주소가 다르거나 칸을 하나로 찾지 못하거나, 판단에 필요한 조회가
    /// 하나라도 실패하면(빈 값과 구분) 확인 불가.</summary>
    public static Platform DetectPlatform(nint window, SiteLink link, Job job)
    {
        Platform r = Platform.Unknown;
        try
        {
            if (!_ok || !job.Alive) return r;
            nint doc = FindDocument(window);
            if (doc == 0) return r;
            try
            {
                if (SiteUrl.Normalize(DocumentUrl(doc)) != link.Url) return r;
                var all = Edits(doc, keepElements: true);
                try
                {
                    int i = Match(all.Select(e => e.Field).ToList(), link);
                    if (i < 0) return r;
                    nint fe = all[i].Element;
                    bool complete = TryBstr(fe, 30, out string fcls) & TryBstr(fe, 29, out string fid);   // 칸 자신의 class·id 를 다시, 성공 여부와 함께
                    bool main = false, reachedDoc = false;
                    nint cur = fe; AddRef(cur);
                    for (int level = 0; level < 40 && complete; level++)
                    {
                        nint p = Parent(cur);
                        Release(cur);
                        cur = p;
                        if (cur == 0) { complete = false; break; }   // 조회 실패 또는 뿌리 위: 문서를 만나지 못했다
                        if (!TryInt(cur, 21, out int ct)) { complete = false; break; }   // get_CurrentControlType
                        if (ct == TypeDocument) { reachedDoc = true; break; }
                        if (!TryBstr(cur, 29, out string pid) || !TryBstr(cur, 30, out string pcls)) { complete = false; break; }
                        if (pid == "mainframe" && pcls == "MainFrame") main = true;
                    }
                    Release(cur);
                    r = ClassifyPlatform(fcls, fid, main, complete && reachedDoc);
                    return r;
                }
                finally { foreach (var e in all) Release(e.Element); }
            }
            finally { Release(doc); }
        }
        finally { LastPlatform = (int)r; }
    }

    /// <summary>id·이름이 없는 칸의 신원: 같은 종류 칸 가운데 class 가 하나뿐이면 그 class, 그 종류 칸이 그것 하나뿐이면 "*". 정할 수 없으면 null.</summary>
    private static string? IdentityClass(List<(nint Element, Field Field)> all, Field f)
    {
        if (f.AutomationId.Length > 0 || f.Name.Length > 0) return "";
        var kind = all.Where(x => x.Field.Password == f.Password).Select(x => x.Field).ToList();
        if (f.Class.Length > 0 && f.Class != "*" && kind.Count(x => x.Class == f.Class) == 1) return f.Class;
        if (kind.Count == 1) return "*";
        return null;
    }

    /// <summary>
    /// 폼 경계인가(Codex 06:00 R113-3: 부모 몇 단계가 아니라 의미 있는 폼 경계 안에서만 짝을 찾는다). 인식하는 경계:
    /// Nexacro 폼(class "Form" 또는 "Form …", 업무 사이트의 div_login.form), HTML 폼 랜드마크(LandmarkType = Form 80001).
    /// ok = 판단에 필요한 조회가 모두 성공했는가.
    /// </summary>
    private static bool IsFormBoundary(nint el, out bool ok)
    {
        ok = TryBstr(el, 30, out string cls) & TryIntProperty(el, 30157, out int landmark);   // 30157 = UIA_LandmarkTypePropertyId
        return cls == "Form" || cls.StartsWith("Form ", StringComparison.Ordinal) || landmark == 80001;
    }

    /// <summary>
    /// 연결 한 번: 지금 포커스가 있는 입력란과, **그 칸이 속한 가장 가까운 폼 경계 안**의 반대 종류 칸(일반 ↔ 비밀번호) 하나를 찾는다
    /// (Codex 05:45-C, 06:00 R113-1·3). 폼 경계를 인식하지 못하면(문서에 먼저 닿음) 자동 짝을 하지 않는다. 경계 안의 보이고 사용 가능한
    /// 반대 종류 칸이 정확히 하나이고, 그 안의 모든 후보 조회가 성공했을 때만 짝이다. 조회가 하나라도 실패하면 짝을 돌려주지 않는다
    /// (실패로 가려진 후보가 있을 수 있으므로 "유일"로 보지 않는다). 다른 문서(iframe) 속 칸은 문서 목록에서 이미 빠진다. 값은 읽지 않는다.
    /// </summary>
    public static PairResult CapturePair(nint window, Job job)
    {
        if (!_ok) return new(CaptureError.NoDocument, "", default, null);
        if (!IsBrowser(window)) return new(CaptureError.NotBrowser, "", default, null);
        nint doc = FindDocument(window);
        if (doc == 0) return new(CaptureError.NoDocument, "", default, null);
        nint focused = 0;
        List<(nint Element, Field Field)>? all = null;
        try
        {
            string url = DocumentUrl(doc);
            if (url.Length == 0) return new(CaptureError.NoUrl, "", default, null);
            if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)V(_uia)[8])(_uia, &focused) < 0 || focused == 0) return new(CaptureError.NotField, url, default, null);
            if (ControlType(focused) != TypeEdit || !job.Alive) return new(CaptureError.NotField, url, default, null);
            all = Edits(doc, keepElements: true);
            nint fo = focused;
            int me = all.FindIndex(x => Same(x.Element, fo));
            if (me < 0) return new(CaptureError.NotField, url, default, null);
            Field f = all[me].Field;
            if (IdentityClass(all, f) is not string cls) return new(CaptureError.NoIdentity, url, default, null);
            f = f with { Class = cls };
            PairFound = 0;
            Field? partner = null;
            // 1) 가장 가까운 폼 경계(최대 8단계, 문서에 먼저 닿으면 경계 없음)
            nint form = 0, cur = all[me].Element; AddRef(cur);
            bool complete = true;
            for (int level = 0; level < 8; level++)
            {
                nint p = Parent(cur);
                Release(cur);
                cur = p;
                if (cur == 0) { complete = false; break; }
                if (!TryInt(cur, 21, out int ct)) { complete = false; break; }
                if (ct == TypeDocument) break;
                bool boundary = IsFormBoundary(cur, out bool ok);
                if (!ok) { complete = false; break; }
                if (boundary) { form = cur; cur = 0; break; }
            }
            Release(cur);
            if (form == 0) { PairFound = complete ? 2 : 3; return new(CaptureError.None, url, f, null); }   // 경계 없음(2)·조회 실패(3): 자동 짝 안 함
            // 2) 그 폼 안의 반대 종류 후보 — 조회가 하나라도 실패하면 짝 없음
            var found = new List<int>();
            nint arr = 0;
            try
            {
                if (((delegate* unmanaged[Stdcall]<nint, int, nint, nint*, int>)V(form)[6])(form, TreeScope_Descendants, _condEdit, &arr) < 0 || arr == 0) complete = false;
                int n = 0;
                if (complete && ((delegate* unmanaged[Stdcall]<nint, int*, int>)V(arr)[3])(arr, &n) < 0) complete = false;   // get_Length
                for (int k = 0; complete && k < n; k++)
                {
                    nint el = 0;
                    if (((delegate* unmanaged[Stdcall]<nint, int, nint*, int>)V(arr)[4])(arr, k, &el) < 0 || el == 0) { complete = false; break; }
                    try
                    {
                        int idx = -1;
                        for (int a = 0; a < all.Count; a++)
                        {
                            int c = Compare(all[a].Element, el);
                            if (c == -1) { complete = false; break; }   // 비교 실패: 소속을 확인하지 못했다
                            if (c == 1) { idx = a; break; }
                        }
                        if (!complete) break;
                        if (idx < 0 || idx == me) continue;   // 문서 목록 밖(다른 문서 속 칸)·누른 칸 자신
                        Field c2 = all[idx].Field;
                        if (c2.Password == f.Password) continue;
                        if (!TryInt(el, 28, out int en)) { complete = false; break; }   // get_CurrentIsEnabled
                        if (!c2.Offscreen && en != 0) found.Add(idx);
                    }
                    finally { Release(el); }
                }
            }
            finally { Release(arr); Release(form); }
            if (complete && found.Count == 1 && IdentityClass(all, all[found[0]].Field) is string pc)
            {
                partner = all[found[0]].Field with { Class = pc };
                PairFound = 1;
            }
            else PairFound = !complete ? 3 : found.Count > 1 ? 4 : 5;   // 3 조회 실패, 4 모호, 5 후보 없음·신원 없음
            return new(CaptureError.None, url, f, partner);
        }
        finally
        {
            if (all is not null) foreach (var e in all) Release(e.Element);
            Release(focused); Release(doc);
        }
    }

    /// <summary>
    /// MainFrame 에서 출발(Codex 06:30-as, 제한 허용): 지금 이 순간 다음을 **모두** 확인했을 때만 true. 하나라도 조회에 실패하거나 아니면 false.
    /// ① 창의 http(s) 문서가 연결 주소이고 연결한 칸이 하나로 찾아짐 ② 그 칸에서 문서까지 부모 사슬을 빠짐없이 읽었고 그 사이에
    /// id "mainframe"·class "MainFrame" 요소가 있음(Nexacro, 같은 문서 소속) ③ UIA 포커스가 **정확히 그 MainFrame 요소**(같은 요소)
    /// ④ 덮개 없음: 연결한 칸이 화면에 보이고(offscreen 아님) 그 칸 가운데의 요소가 그 칸 자신(가려지지 않음) — 보수적 추가 검사일 뿐,
    /// 모달이 없다는 증거로 쓰지 않는다(Codex 07:00 R115-1).
    /// ⑤ 연결한 칸 자신의 종류(Edit)·id·class·비밀번호 여부를 **성공 여부와 함께** 다시 읽어 연결과 같고, 그 칸만으로 Nexacro 신호가
    /// 둘 이상(class nexainput·Nexacro 경로 id·MainFrame 조상)일 것(R115-2).
    /// ⑥ 대화 상자·팝업 없음(R115-1): 브라우저 창 전체에 보이는 대화 상자 요소(IsDialog)가 없고, 문서 안에 보이는 창(Window) 요소가 없고,
    /// 문서 안의 보이는 Nexacro 입력란이 모두 연결한 칸과 같은 프레임 경로(id 의 첫 ".form." 앞)다(팝업 폼은 다른 프레임 경로),
    /// 그 프레임의 자식 프레임(id "<프레임>.<팝업>.form…", 입력란 없는 알림 포함) 요소가 보이지 않는다.
    /// 이 조회가 하나라도 실패하거나 IsDialog 를 물을 수 없는 Windows 면 "확인 불가"로 출발하지 않는다.
    /// 실제로 어느 부품(체크박스 등)에 커서가 있는지는 알 수 없다 — 안전은 이동 뒤의 도착 확인에 둔다(값은 확인 뒤에만).
    /// reason: 0 시작 가능, 1 문서·칸 없음, 2 사슬 조회 실패, 3 MainFrame 아님, 4 포커스가 MainFrame 이 아님, 5 칸이 가려짐,
    /// 6 조회 실패, 7 칸 신원 확인 실패(R115-2), 8 대화 상자·팝업 있음 또는 확인 불가(R115-1), 9 브라우저의 저장 로그인·자동 완성 목록이 보임(Codex 08:45-ba).
    /// </summary>
    public static bool MainFrameStart(nint window, SiteLink link, Job job, out int reason)
    {
        bool r = MainFrameStartCore(window, link, job, out reason);
        LastMainFrame = reason;
        return r;
    }

    /// <summary>마지막 MainFrame 출발 확인의 reason(시험 관찰: TestObservations 24–27 비트). -1 = 아직 없음.</summary>
    internal static volatile int LastMainFrame = -1;

    private static bool MainFrameStartCore(nint window, SiteLink link, Job job, out int reason)
    {
        reason = 1;
        if (!_ok || !job.Alive) return false;
        nint doc = FindDocument(window);
        if (doc == 0) return false;
        nint focused = 0, mainEl = 0;
        List<(nint Element, Field Field)>? all = null;
        try
        {
            if (SiteUrl.Normalize(DocumentUrl(doc)) != link.Url) return false;
            all = Edits(doc, keepElements: true);
            int i = Match(all.Select(e => e.Field).ToList(), link);
            if (i < 0) return false;
            nint fe = all[i].Element;
            // ② 부모 사슬(빠짐없이)과 MainFrame
            reason = 2;
            bool reachedDoc = false;
            nint cur = fe; AddRef(cur);
            for (int level = 0; level < 40; level++)
            {
                nint p = Parent(cur);
                Release(cur);   // MainFrame 은 따로 AddRef 해 둔다
                cur = p;
                if (cur == 0) break;
                if (!TryInt(cur, 21, out int ct)) break;
                if (ct == TypeDocument) { reachedDoc = Compare(cur, doc) == 1; break; }
                if (!TryBstr(cur, 29, out string pid) || !TryBstr(cur, 30, out string pcls)) break;
                if (mainEl == 0 && pid == "mainframe" && pcls == "MainFrame") { mainEl = cur; AddRef(mainEl); }
            }
            Release(cur);
            if (!reachedDoc) return false;
            reason = 3;
            if (mainEl == 0) return false;
            // ⑤ 연결한 칸 자신의 신원(R115-2): 종류·id·class·비밀번호 여부를 성공 여부와 함께 다시 읽고, 연결 id 와 같으며 그 칸만으로 Nexacro
            reason = 7;
            if (Inject(5)) return false;   // 검증 전용: 칸 속성 조회 실패
            if (!TryInt(fe, 21, out int fct) || fct != TypeEdit) return false;
            if (!TryBstr(fe, 29, out string fid) || !TryBstr(fe, 30, out string fcls) || !TryInt(fe, 35, out int fpw)) return false;
            if (link.FieldId.Length == 0 || fid != link.FieldId || (fpw != 0) != link.Password) return false;
            if (ClassifyPlatform(fcls, fid, mainFrameAncestor: true, complete: true) != Platform.Nexacro) return false;
            // ③ 포커스가 정확히 그 MainFrame
            reason = 6;
            if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)V(_uia)[8])(_uia, &focused) < 0 || focused == 0) return false;
            int same = Compare(mainEl, focused);
            if (same == -1) return false;
            reason = 4;
            if (same != 1) return false;
            // ④ 덮개·팝업 없음: 연결한 칸이 보이고, 그 칸 가운데의 요소가 그 칸
            reason = 5;
            if (!TryInt(fe, 38, out int off) || off != 0) return false;
            Native.RECT r = default;
            if (((delegate* unmanaged[Stdcall]<nint, Native.RECT*, int>)V(fe)[43])(fe, &r) < 0 || r.right <= r.left || r.bottom <= r.top) return false;
            nint hit = 0;
            try
            {
                if (((delegate* unmanaged[Stdcall]<nint, Native.POINT, nint*, int>)V(_uia)[7])(_uia, new Native.POINT { x = (r.left + r.right) / 2, y = (r.top + r.bottom) / 2 }, &hit) < 0 || hit == 0) return false;
                if (Compare(fe, hit) != 1) return false;
            }
            finally { Release(hit); }
            // ⑥ 대화 상자·팝업 없음(R115-1). 확인할 수 없으면 출발하지 않는다
            reason = 8;
            if (Inject(6)) return false;   // 검증 전용: 대화 상자 확인 실패
            if (!NoVisible(window, 0, _condDialog) || !NoVisible(0, doc, _condWindow)) return false;
            // 브라우저 자체의 저장된 로그인·자동 완성 목록이 보이면(창 전체에서 찾되 문서 밖 = 그 브라우저 창의 UI) 출발하지 않는다(reason 9).
            // 그 목록이 열려 있으면 Shift+Tab 이 목록에 먹히거나 Tab 이 저장된 로그인을 고를 수 있다(Codex 08:45-ba)
            reason = 9;
            if (Inject(8) || _condPopupRow == 0 || !NoVisible(window, 0, _condPopupRow)) return false;
            reason = 8;
            int cut = fid.IndexOf(".form.", StringComparison.Ordinal);
            if (cut <= 0) return false;
            string frame = fid[..(cut + 6)];
            foreach (var (_, f) in all)
                if (!f.Offscreen && f.AutomationId.Contains(".form.", StringComparison.Ordinal) && !f.AutomationId.StartsWith(frame, StringComparison.Ordinal)) return false;   // 다른 프레임(팝업 폼)의 보이는 칸
            // 그 프레임의 자식 프레임(Nexacro 팝업): id 가 "<프레임 이름>." 로 시작하지만 그 프레임의 폼(frame)이 아닌 보이는 요소가 있으면 팝업이 열려 있다.
            // 실제 Nexacro 업무 사이트(2026-10-03 읽기 조사): 아이디·비밀번호 찾기 = CF_LOGIN.userIdPwPopup.form…, 입력란 없는 알림 = CF_LOGIN.cmmAlertSC0080.form…
            // (알림이 떠 있는 동안 UIA 포커스는 MainFrame, 대화 상자·창 신호 없음). 평소 로그인 화면에는 CF_LOGIN 의 자식 프레임이 없다.
            if (Inject(7) || !NoChildFrame(doc, fid[..(cut + 1)], frame)) return false;
            reason = 0;
            return job.Alive;
        }
        finally
        {
            if (all is not null) foreach (var e in all) Release(e.Element);
            Release(mainEl); Release(focused); Release(doc);
        }
    }

    /// <summary>
    /// window(브라우저 창 전체) 또는 scope(문서) 아래에 cond 에 맞는 **보이는**(offscreen 아님) 요소가 없으면 true.
    /// 조건이 없거나(만들지 못함) 찾기·읽기가 하나라도 실패하면 false(확인 불가 = 있다고 본다).
    /// </summary>
    private static bool NoVisible(nint window, nint scope, nint cond)
    {
        if (cond == 0) return false;
        nint root = scope, arr = 0;
        if (window != 0 && (((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)V(_uia)[6])(_uia, window, &root) < 0 || root == 0)) return false;
        try
        {
            if (((delegate* unmanaged[Stdcall]<nint, int, nint, nint*, int>)V(root)[6])(root, TreeScope_Descendants, cond, &arr) < 0 || arr == 0) return false;
            int n = 0;
            if (((delegate* unmanaged[Stdcall]<nint, int*, int>)V(arr)[3])(arr, &n) < 0) return false;
            for (int k = 0; k < n; k++)
            {
                nint el = 0;
                if (((delegate* unmanaged[Stdcall]<nint, int, nint*, int>)V(arr)[4])(arr, k, &el) < 0 || el == 0) return false;
                try { if (!TryInt(el, 38, out int off) || off == 0) return false; }   // 보이는 것이 있거나 못 읽음
                finally { Release(el); }
            }
            return true;
        }
        finally { Release(arr); if (window != 0) Release(root); }
    }

    /// <summary>
    /// 문서 안에 id 가 owner("…CF_LOGIN.")로 시작하지만 frame("…CF_LOGIN.form.")으로 시작하지 않는 **보이는** 요소가 없으면 true.
    /// 요소가 너무 많거나(2000 초과) 찾기·읽기가 하나라도 실패하면 false(확인 불가 = 있다고 본다).
    /// </summary>
    private static bool NoChildFrame(nint doc, string owner, string frame)
    {
        if (_condTrue == 0) return false;
        nint arr = 0;
        try
        {
            if (((delegate* unmanaged[Stdcall]<nint, int, nint, nint*, int>)V(doc)[6])(doc, TreeScope_Descendants, _condTrue, &arr) < 0 || arr == 0) return false;
            int n = 0;
            if (((delegate* unmanaged[Stdcall]<nint, int*, int>)V(arr)[3])(arr, &n) < 0 || n > 2000) return false;
            for (int k = 0; k < n; k++)
            {
                nint el = 0;
                if (((delegate* unmanaged[Stdcall]<nint, int, nint*, int>)V(arr)[4])(arr, k, &el) < 0 || el == 0) return false;
                try
                {
                    if (!TryBstr(el, 29, out string id)) return false;
                    if (!id.StartsWith(owner, StringComparison.Ordinal)) continue;
                    // 그 프레임 자신의 폼: "…CF_LOGIN.form"(폼 요소 자체, 끝에 점 없음 — 0.2.123 은 이것을 팝업으로 잘못 봐 출발이 늘 막혔다,
                    // 2026-10-03 Nexacro 업무 사이트 읽기 확인), "…CF_LOGIN.form.…", "…CF_LOGIN.form:…"
                    string rest = id[owner.Length..];
                    if (rest == "form" || rest.StartsWith("form.", StringComparison.Ordinal) || rest.StartsWith("form:", StringComparison.Ordinal)) continue;
                    if (!TryInt(el, 38, out int off) || off == 0) return false;   // 자식 프레임 요소가 보이거나 못 읽음
                }
                finally { Release(el); }
            }
            return true;
        }
        finally { Release(arr); }
    }

    /// <summary>검증 전용(Codex 09:45-bf 실제 목록 탐지 대조): 그 브라우저 창에 Chromium 자동 완성·저장 로그인 목록 줄이 보이면 1, 아니면 0, 확인 불가 -1.
    /// MainFrame 출발 ⑥ 의 reason 9 와 같은 조건(NoVisible + PopupRowContentView)을 그대로 쓴다.</summary>
    public static int BrowserListVisible(nint window) => !_ok || _condPopupRow == 0 ? -1 : NoVisible(window, 0, _condPopupRow) ? 0 : 1;

    /// <summary>마지막 짝 찾기 결과(시험·진단): 1 짝 찾음, 2 폼 경계 없음, 3 조회 실패, 4 모호, 5 후보 없음.</summary>
    public static volatile int PairFound;

    /// <summary>
    /// 주기적 확인: 창의 페이지 주소를 읽어, 그 주소에 연결된 항목이 있으면 입력란들을 찾아 위치를 돌려준다.
    /// links: (슬롯, 연결) 목록. 주소가 어느 연결과도 같지 않으면 입력란은 찾지 않는다(가볍게).
    /// </summary>
    /// <summary>검증 전용(ONEKEY_TEST=1): ONEKEY_TEST_UIA_DELAY_MS 만큼 주기적 확인을 늦춘다(가짜 지연: 확인 중에 진단·연결·잠금이 겹치는 순서를 만든다, R60-1).</summary>
    internal static int TestNextProbeDelayMs;
    internal static int TestProbeDelayMs = Program.IsTestMode && int.TryParse(Environment.GetEnvironmentVariable("ONEKEY_TEST_UIA_DELAY_MS"), out int d) && d > 0 ? Math.Min(d, 10000) : 0;

    public static ProbeResult Probe(nint window, IReadOnlyList<(int Slot, SiteLink Link)> links)
    {
        var hits = new List<Hit>();
        if (!_ok) return new(window, null, hits);
        if (TestProbeDelayMs > 0) Thread.Sleep(TestProbeDelayMs);
        int extra = Program.IsTestMode ? Interlocked.Exchange(ref TestNextProbeDelayMs, 0) : 0;
        if (extra > 0) Thread.Sleep(Math.Min(extra, 15000));   // 검증 전용: 다음 확인 한 번만 더 늦춘다(역순 콜백 시험)
        if (!IsBrowser(window)) return new(window, null, hits);
        nint doc = FindDocument(window);
        if (doc == 0) return new(window, null, hits);
        try
        {
            string? url = SiteUrl.Normalize(DocumentUrl(doc));
            if (url is null || !links.Any(l => l.Link.Url == url)) return new(window, null, hits);
            var edits = Edits(doc, keepElements: false).Select(e => e.Field).ToList();
            foreach (var (slot, link) in links)
            {
                if (link.Url != url) continue;
                int i = Match(edits, link);
                if (i >= 0) hits.Add(new Hit(slot, edits[i]));
            }
            return new(window, url, hits);
        }
        finally { Release(doc); }
    }

    public enum FocusResult { Ok, PageChanged, NotFound, FocusFailed, ClickMissed }

    /// <summary>
    /// 채우기 직전: 창의 페이지 주소가 아직 url 이고, 연결한 입력란이 딱 하나로 찾아지면 그 칸에 커서를 옮긴 뒤
    /// 정말 그 칸에 포커스가 갔는지(같은 요소, 같은 비밀번호 칸 여부) 확인한다.
    /// </summary>
    /// <param name="verifyOnly">커서를 옮기지 않고 지금 커서가 그 칸인지만 본다(Tab 으로 옮긴 뒤의 확인).</param>
    /// <returns>ClickMissed: 그 칸을 실제로 클릭했는데 커서가 그 칸에 없다. 화면이 알려 준 위치가 실제와 달랐을 수 있으므로
    /// (2026-10-03 Nexacro 업무 사이트: 위치 정보가 늦게 갱신돼 아래의 "비밀번호 찾기" 버튼이 눌림) 부르는 쪽은 더 시도하지 않고 멈춘다.</returns>
    public static FocusResult FocusField(nint window, SiteLink link, Job job, bool allowClick = true, bool verifyOnly = false)
    {
        if (!_ok) return FocusResult.FocusFailed;
        nint doc = FindDocument(window);
        if (doc == 0) return FocusResult.PageChanged;
        try
        {
            if (SiteUrl.Normalize(DocumentUrl(doc)) != link.Url) return FocusResult.PageChanged;
            var all = Edits(doc, keepElements: true);
            try
            {
                int i = Match(all.Select(e => e.Field).ToList(), link);
                if (i < 0) return FocusResult.NotFound;
                nint el = all[i].Element;
                Field field = all[i].Field;
                // 커서를 그 칸으로: ① UIA SetFocus ② 안 되면 기본 동작(Chrome: 그 칸을 누른 것처럼) ③ 그래도 안 되면 그 칸 가운데를 실제로 클릭.
                // 자기 방식으로 포커스를 관리하는 화면(Nexacro 업무 사이트 등)은 프로그램의 SetFocus 를 받아들이지 않고 원래 칸(비밀번호)으로 되돌릴 수 있다
                // (2026-10-01 사용자 보고: [채우기]가 동작하지 않음). 사용자가 직접 클릭하면 되므로 같은 동작으로 이어 간다.
                // 어느 단계든 끝에 "포커스가 정말 그 칸인가"를 확인한 뒤에만 Ok. 시간 초과·잠금·창 전환 뒤에는 커서를 옮기지 않는다 (V55-3).
                LastFocusFallback = false;
                if (verifyOnly)
                {
                    if (!FocusedIs(el, link.Password)) return FocusResult.FocusFailed;
                    SetFillTarget(el);
                    LastFocusMethod = 4;   // 4 = Tab·Shift+Tab 으로 옮겨 감(진단 표시, 부르는 쪽이 5 로 바꿀 수 있음)
                    return FocusResult.Ok;
                }
                bool clicked = false;
                for (int method = 1; method <= 3; method++)
                {
                    if (!job.Alive) return FocusResult.FocusFailed;
                    if (method < 3 && Inject(4)) continue;   // 검증 전용: 클릭 단계로 바로 (R75-3 시험)
                    if (method == 3 && !allowClick) break;   // 항목 설정 "칸을 직접 클릭하지 않기"(Codex 08:01): 화면 클릭은 하지 않는다
                    if (method == 1) { if (((delegate* unmanaged[Stdcall]<nint, int>)V(el)[3])(el) < 0) continue; Thread.Sleep(60); }
                    else if (method == 2) { if (!DefaultAction(el)) continue; Thread.Sleep(200); if (!job.Alive) return FocusResult.FocusFailed; }
                    else { if (!ClickCenter(window, el, job)) continue; clicked = true; Thread.Sleep(200); if (!job.Alive) return FocusResult.FocusFailed; }
                    if (FocusedIs(el, link.Password))
                    {
                        // 이 채우기의 대상 요소를 기억한다: 글자마다 포커스가 아직 이 요소인지 본다(FocusIsFillTarget).
                        // 브라우저는 페이지 안 모든 칸이 창 핸들 하나를 같이 써서 창·핸들 검사로는 칸 이동·탭 전환·요소 교체를 알 수 없다.
                        SetFillTarget(el);
                        LastFocusMethod = method;
                        return FocusResult.Ok;
                    }
                }
                LastFocusMethod = 0;
                return clicked ? FocusResult.ClickMissed : FocusResult.FocusFailed;
            }
            finally { foreach (var e in all) Release(e.Element); }
        }
        finally { Release(doc); }
    }

    /// <summary>지금 커서가 연결한 그 칸에 있는가(아무것도 옮기지 않음). Shift+Tab 을 보내기 전 확인(커서가 다음 입력 칸에 있을 때만).</summary>
    public static bool IsFocusedOn(nint window, SiteLink link, Job job)
    {
        if (!_ok || !job.Alive) return false;
        nint doc = FindDocument(window);
        if (doc == 0) return false;
        try
        {
            if (SiteUrl.Normalize(DocumentUrl(doc)) != link.Url) return false;
            var all = Edits(doc, keepElements: true);
            try
            {
                int i = Match(all.Select(e => e.Field).ToList(), link);
                if (i < 0) return false;
                string keep = LastProbe;
                bool on = FocusedIs(all[i].Element, link.Password);
                LastProbe = keep;   // 진단 관찰값은 그 칸 자신의 확인 결과로 둔다
                return on;
            }
            finally { foreach (var e in all) Release(e.Element); }
        }
        finally { Release(doc); }
    }

    /// <summary>마지막 채우기에서 커서를 옮긴 방법(0 실패, 1 SetFocus, 2 기본 동작, 3 클릭). 진단·시험 관찰값.</summary>
    public static volatile int LastFocusMethod;

    private static bool FocusedIs(nint el, bool password)
    {
        nint focused = 0;
        try
        {
            if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)V(_uia)[8])(_uia, &focused) < 0 || focused == 0) { LastProbe = "-|?|?"; return false; }
            return HasFocus(el, focused) && IsPassword(el) == password;
        }
        finally { Release(focused); }
    }

    /// <summary>
    /// 그 칸에 포커스가 있는가. 1순위: UIA 의 "포커스된 요소"가 그 칸. 2순위(2026-10-02 Nexacro 업무 사이트 재현): 페이지가 열리며 스스로 그 칸에 커서를
    /// 넣어 둔 경우(Nexacro, "아이디 기억하기") Chrome 의 "포커스된 요소"가 문서나 바깥 묶음에 머물러 갱신되지 않는다. 이미 커서가 있어
    /// SetFocus·클릭으로도 포커스 변경이 일어나지 않기 때문이다. 그때는 칸 자신의 HasKeyboardFocus 가 참이고, "포커스된 요소"가 다른
    /// 입력란이 아니라 문서·묶음·창 같은 바깥 요소이며 같은 프로세스일 때만 받아들인다(<see cref="FocusAccepted"/>).
    /// </summary>
    private static bool HasFocus(nint el, nint focused)
    {
        if (Same(el, focused)) { LastProbe = "same|1|1"; return true; }
        int kb = 0, pa = 0, pb = 0;
        ((delegate* unmanaged[Stdcall]<nint, int*, int>)V(el)[26])(el, &kb);          // get_CurrentHasKeyboardFocus
        ((delegate* unmanaged[Stdcall]<nint, int*, int>)V(el)[20])(el, &pa);          // get_CurrentProcessId
        ((delegate* unmanaged[Stdcall]<nint, int*, int>)V(focused)[20])(focused, &pb);
        int ft = ControlType(focused);
        // 진단(최근 채우기 결과): 포커스된 요소의 종류(비밀번호 칸이면 표시), 칸 스스로의 포커스 응답, 같은 프로세스 여부. 값·이름은 읽지 않는다.
        LastProbe = TypeName(ft) + (ft == 50004 && IsPassword(focused) ? "(pw)" : "") + "|" + (kb != 0 ? "1" : "0") + "|" + (pa != 0 && pa == pb ? "1" : "0");
        bool ok = FocusAccepted(false, kb != 0, ft, pa != 0 && pa == pb);
        if (ok) LastFocusFallback = true;
        return ok;
    }

    /// <summary>순수 판정(단위 시험용): 같은 요소면 참. 아니면 칸이 스스로 포커스를 말하고, 포커스된 요소가 문서(50030)·묶음(50026)·
    /// 창(50033)·사용자 정의(50025)이며 같은 프로세스일 때만 참. 포커스된 요소가 다른 입력란(편집 50004 등)이면 거짓.</summary>
    internal static bool FocusAccepted(bool sameElement, bool fieldSaysFocused, int focusedControlType, bool sameProcess)
        => sameElement || (fieldSaysFocused && sameProcess && focusedControlType is 50030 or 50026 or 50033 or 50025);

    /// <summary>마지막 포커스 확인의 관찰값 "포커스된 요소 종류|칸 스스로 포커스(1/0)|같은 프로세스(1/0)" (진단, 값 없음).</summary>
    public static volatile string LastProbe = "";
    private static string TypeName(int t) => t switch
    {
        50030 => "Document", 50026 => "Group", 50004 => "Edit", 50033 => "Pane", 50025 => "Custom", 50032 => "Window",
        50000 => "Button", 50002 => "CheckBox", 50020 => "Text", 50006 => "Image", 50005 => "Hyperlink", _ => t.ToString(),
    };

    /// <summary>마지막 채우기에서 2순위 판정(칸의 HasKeyboardFocus)으로 받아들인 적이 있는가. 진단 관찰값.</summary>
    public static volatile bool LastFocusFallback;

    /// <summary>LegacyIAccessible 기본 동작. Chrome 은 입력란에서 "누르기"(그 칸에 대한 마우스 누름·뗌·클릭 이벤트)를 한다.</summary>
    private static bool DefaultAction(nint el)
    {
        Guid iid = new("828055ad-355b-4435-86d5-3b51c14a9b1b");   // IUIAutomationLegacyIAccessiblePattern
        nint p = 0;
        if (((delegate* unmanaged[Stdcall]<nint, int, Guid*, nint*, int>)V(el)[14])(el, 10018, &iid, &p) < 0 || p == 0) return false;
        try { return ((delegate* unmanaged[Stdcall]<nint, int>)V(p)[4])(p) >= 0; }   // DoDefaultAction
        finally { Release(p); }
    }

    /// <summary>
    /// 그 칸 가운데를 실제로 한 번 클릭하고 마우스 포인터를 제자리로 돌린다. 창이 앞에 있고, 칸이 화면에 보이며 창 안에 있을 때만.
    /// 사용자가 누르고 있는 Ctrl·Alt 는 먼저 뗀다(단축키로 채울 때 Ctrl+클릭이 되지 않게).
    /// </summary>
    private static bool ClickCenter(nint window, nint el, Job job)
    {
        // 기다림(Ctrl·Alt 떼기)을 먼저 끝내고, 그 뒤에 작업·앞의 창·요소의 **지금** 사각형을 읽고, 그 점에 실제로 그 입력란이 있는지
        // (UIA 점 판정 = 같은 요소) 확인한 뒤에만 누른다. 가려졌거나(확인 창 등) 자리가 바뀌었으면 누르지 않는다 (Codex R75-3).
        Injector.ReleaseHeldModifiers();
        if (!Injector.TestHoldPoint("click.mods")) return false;   // 검증 전용 경계(R75-3 시험): 여기서 가림·이동·교체·취소를 일으킨다
        if (!job.Alive || Native.GetForegroundWindow() != window || !Native.GetWindowRect(window, out Native.RECT wr)) return false;
        Native.RECT r = default;
        int off = 0;
        if (((delegate* unmanaged[Stdcall]<nint, Native.RECT*, int>)V(el)[43])(el, &r) < 0) return false;
        ((delegate* unmanaged[Stdcall]<nint, int*, int>)V(el)[38])(el, &off);
        if (off != 0 || r.right <= r.left || r.bottom <= r.top || r.left < wr.left || r.right > wr.right || r.top < wr.top || r.bottom > wr.bottom) return false;
        int x = (r.left + r.right) / 2, y = (r.top + r.bottom) / 2;
        nint hit = 0;
        try
        {
            if (((delegate* unmanaged[Stdcall]<nint, Native.POINT, nint*, int>)V(_uia)[7])(_uia, new Native.POINT { x = x, y = y }, &hit) < 0 || hit == 0) return false;   // ElementFromPoint
            if (!Same(el, hit)) return false;
        }
        finally { Release(hit); }
        if (!job.Alive || Native.GetForegroundWindow() != window) return false;
        Native.GetCursorPos(out Native.POINT old);
        if (!Native.SetCursorPos(x, y)) return false;
        var inputs = stackalloc Native.INPUT[2];
        inputs[0] = new Native.INPUT { type = Native.INPUT_MOUSE };
        inputs[0].U.mi.dwFlags = Native.MOUSEEVENTF_LEFTDOWN;
        inputs[1] = new Native.INPUT { type = Native.INPUT_MOUSE };
        inputs[1].U.mi.dwFlags = Native.MOUSEEVENTF_LEFTUP;
        uint sent = Native.SendInput(2, inputs, sizeof(Native.INPUT));
        Thread.Sleep(30);
        Native.SetCursorPos(old.x, old.y);
        return sent == 2;
    }

    /// <summary>진단(고급 › 브라우저 입력란 확인): 주소와 입력란 목록. 값은 읽지 않는다.</summary>
    public static string Describe(nint window)
    {
        if (!_ok) return T.DiagSiteUnavailable;
        var sb = new StringBuilder();
        if (!IsBrowser(window)) return T.DiagSiteNoBrowser;
        nint doc = FindDocument(window);
        if (doc == 0) return T.DiagSiteNoDoc;
        try
        {
            string raw = DocumentUrl(doc);
            sb.Append("■ ").Append(T.DiagSiteUrl).Append('\n').Append("    ").Append(raw.Length > 0 ? SiteUrl.Masked(raw) : T.CommonUnknown).Append("\n\n");
            var edits = Edits(doc, keepElements: false).Select(e => e.Field).ToList();
            sb.Append("■ ").Append(T.DiagSiteFields).Append(" (").Append(edits.Count).Append(")\n");
            int shown = 0;
            foreach (Field f in edits)
            {
                if (shown++ >= 30) { sb.Append("    …\n"); break; }
                sb.Append("    ").Append(f.Password ? T.DiagSitePassword : T.DiagSiteText).Append(' ').Append(f.Index + 1)
                  .Append("  id=").Append(f.AutomationId.Length > 0 ? MaskDigits(f.AutomationId) : "-")
                  .Append("  name=").Append(f.Name.Length > 0 ? Trim(MaskDigits(f.Name), 40) : "-")
                  .Append(f.Class.Length > 0 ? "  class=" + Trim(MaskDigits(f.Class), 40) : "")
                  .Append(f.Offscreen ? "  (" + T.DiagSiteOffscreen + ")" : "").Append('\n');
            }
            return sb.ToString();
        }
        finally { Release(doc); }
    }

    // ---------------------------------------------------------------- 맞추기 (순수 함수: 시험하기 쉽게)

    /// <summary>
    /// 연결한 입력란을 목록에서 찾는다. 늘 같은 종류(비밀번호 칸 여부)끼리만 본다: ① ID 가 같은 것이 딱 하나 ② 이름이 같은 것이 딱 하나.
    /// 어느 단계에서든 둘 이상이 맞으면(어느 칸인지 모름) 찾지 못한 것이다.
    /// ②(ID 가 바뀌었어도 이름이 같은 칸이 딱 하나)는 허용한다: 페이지가 여는 때마다 ID 를 새로 만드는 경우가 흔하다. 이름은 라벨이라
    /// "새 비밀번호"와 "비밀번호"처럼 다른 동작의 칸을 구분한다.
    /// **순서로 짐작하지 않는다**(Codex V55-2): ID 도 이름도 맞지 않으면 페이지가 바뀐 것이므로 다시 연결하게 한다.
    /// 없으면 -1.
    /// </summary>
    public static int Match(List<Field> edits, SiteLink link)
    {
        const int None = -1, Ambiguous = -2;
        int One(Func<Field, bool> pred)
        {
            int found = None;
            for (int i = 0; i < edits.Count; i++)
            {
                if (edits[i].Password != link.Password || !pred(edits[i])) continue;
                if (found >= 0) return Ambiguous;
                found = i;
            }
            return found;
        }
        if (link.FieldId.Length > 0)
        {
            int r = One(f => f.AutomationId == link.FieldId);
            if (r >= 0) return r;
            if (r == Ambiguous) return None;
        }
        if (link.FieldName.Length > 0)
        {
            int r = One(f => f.Name == link.FieldName);
            if (r >= 0) return r;
            if (r == Ambiguous) return None;
        }
        // id·이름이 없는 칸: 연결할 때 하나뿐이던 근거가 지금도 하나뿐일 때만 (class 가 같은 칸 하나, 또는 그 종류 칸이 하나)
        if (link.FieldId.Length == 0 && link.FieldName.Length == 0 && link.Class.Length > 0)
        {
            int r = link.Class == "*" ? One(f => true) : One(f => f.Class == link.Class);
            return r >= 0 ? r : None;
        }
        return None;
    }

    /// <summary>진단 표시에서 4자리 이상 숫자(사번·계좌 등일 수 있음)를 가린다.</summary>
    public static string MaskDigits(string s) => System.Text.RegularExpressions.Regex.Replace(s, "[0-9]{4,}", m => new string('#', m.Length));

    private static string Trim(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    // ---------------------------------------------------------------- UIA 도구

    private static nint TypeCondition(int type)
    {
        var v = new VARIANT { vt = 3 /* VT_I4 */, lo = type };
        nint cond;
        return ((delegate* unmanaged[Stdcall]<nint, int, VARIANT, nint*, int>)V(_uia)[23])(_uia, PropControlType, v, &cond) < 0 ? 0 : cond;
    }

    /// <summary>
    /// 창의 웹 페이지 문서: 주소가 http·https 인 첫 문서. 개발자 도구(F12)가 같은 창에 붙어 있으면 그 문서(devtools://)가 먼저 나올 수 있어
    /// 건너뛴다(2026-10-01 사용자 화면). 나무 순서라 페이지가 그 안의 iframe 문서보다 먼저 나온다.
    /// </summary>
    private static nint FindDocument(nint window)
    {
        nint root = 0, arr = 0, first = 0;
        if (((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)V(_uia)[6])(_uia, window, &root) < 0 || root == 0) return 0;
        try
        {
            // 빠른 길: 첫 문서(FindFirst 는 찾으면 멈춘다)가 웹 페이지면 그것. 모든 문서를 모으는 FindAll 은 페이지 전체를 훑어 무겁다
            // (0.2.69–0.2.72 는 늘 FindAll 이라 0.7초 확인이 무거웠다: 2026-10-01 시험에서 1Key 17%·Edge 70%). 개발자 도구가 먼저일 때만 아래.
            if (((delegate* unmanaged[Stdcall]<nint, int, nint, nint*, int>)V(root)[5])(root, TreeScope_Descendants, _condDoc, &first) < 0 || first == 0) return 0;
            string fu = DocumentUrl(first);
            if (fu.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || fu.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return first;
            Release(first); first = 0;
            if (((delegate* unmanaged[Stdcall]<nint, int, nint, nint*, int>)V(root)[6])(root, TreeScope_Descendants, _condDoc, &arr) < 0 || arr == 0) return 0;
            try
            {
                int n = 0;
                ((delegate* unmanaged[Stdcall]<nint, int*, int>)V(arr)[3])(arr, &n);
                for (int i = 0; i < n; i++)
                {
                    nint d = 0;
                    if (((delegate* unmanaged[Stdcall]<nint, int, nint*, int>)V(arr)[4])(arr, i, &d) < 0 || d == 0) continue;
                    string u = DocumentUrl(d);
                    if (u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return d;
                    Release(d);
                }
                return 0;
            }
            finally { Release(arr); }
        }
        finally { Release(root); }
    }

    /// <summary>문서의 Value(= 주소). 못 읽으면 빈 글.</summary>
    private static string DocumentUrl(nint doc)
    {
        Guid iidValue = new("a94cd8b1-0844-4cd6-9d2d-640537ab39e9");   // IUIAutomationValuePattern
        nint vp = 0;
        if (((delegate* unmanaged[Stdcall]<nint, int, Guid*, nint*, int>)V(doc)[14])(doc, PatternValue, &iidValue, &vp) < 0 || vp == 0) return "";
        try
        {
            nint b = 0;
            if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)V(vp)[4])(vp, &b) < 0) return "";
            return TakeBstr(b);
        }
        finally { Release(vp); }
    }

    /// <summary>
    /// 문서 안의 입력란(Edit). keepElements 면 요소 포인터를 돌려주고(호출한 쪽이 Release), 아니면 여기서 푼다.
    /// 문서 안에 다른 문서(iframe 등)가 있으면 그 안의 칸은 뺀다: 다른 출처일 수 있고 그 주소를 확인하지 않는다(Codex V55-2).
    /// </summary>
    private static List<(nint Element, Field Field)> Edits(nint doc, bool keepElements)
    {
        var list = new List<(nint, Field)>();
        // 다른 문서 속 칸 목록을 **완전히** 얻지 못했으면(문서가 너무 많음·조회 실패) 이 페이지의 칸은 하나도 후보로 내지 않는다:
        // 소속을 확인하지 못한 칸에 커서를 옮기거나 입력하지 않는다(Codex R60-2). 그 페이지는 지원하지 않는 것으로 끝난다.
        var nested = NestedEdits(doc, out bool complete);
        if (!complete) { foreach (nint x in nested) Release(x); return list; }
        nint arr = 0;
        try
        {
            if (((delegate* unmanaged[Stdcall]<nint, int, nint, nint*, int>)V(doc)[6])(doc, TreeScope_Descendants, _condEdit, &arr) < 0 || arr == 0) return list;
            int n = 0;
            ((delegate* unmanaged[Stdcall]<nint, int*, int>)V(arr)[3])(arr, &n);
            int pwIndex = 0, textIndex = 0;
            for (int i = 0; i < n; i++)
            {
                nint el = 0;
                if (((delegate* unmanaged[Stdcall]<nint, int, nint*, int>)V(arr)[4])(arr, i, &el) < 0 || el == 0) continue;
                bool inNested = false;
                foreach (nint x in nested)
                {
                    int c = Inject(3) ? -1 : Compare(x, el);
                    if (c == -1) _obsCompareExcluded++;
                    if (c != 0) { inNested = true; break; }   // 같은 요소이거나 비교 실패(-1): 소속을 확인하지 못했으니 뺀다
                }
                if (inNested) { Release(el); continue; }
                bool pw = IsPassword(el);
                Native.RECT rc = default;
                ((delegate* unmanaged[Stdcall]<nint, Native.RECT*, int>)V(el)[43])(el, &rc);
                int off = 0;
                ((delegate* unmanaged[Stdcall]<nint, int*, int>)V(el)[38])(el, &off);
                var f = new Field(BstrProp(el, 29), BstrProp(el, 23), pw, pw ? pwIndex++ : textIndex++, rc, off != 0, BstrProp(el, 30));   // 30 = ClassName(웹: class 속성)
                if (keepElements) list.Add((el, f));
                else { Release(el); list.Add((0, f)); }
            }
        }
        finally
        {
            Release(arr);
            foreach (nint x in nested) Release(x);
        }
        return list;
    }

    /// <summary>
    /// 문서 안의 다른 문서들 속 입력란(뺄 것). 호출한 쪽이 Release. complete = 빠짐없이 모았는가:
    /// 다른 문서가 MaxNestedDocs 보다 많거나, 열거·조회가 하나라도 실패하면 false (Codex R60-2). 빈 결과(다른 문서 없음)는 true.
    /// </summary>
    private const int MaxNestedDocs = 32;

    // 검증 전용(ONEKEY_TEST): 마지막 경계 확인의 관찰값과 실패 주입. 1 = 문서 열거 실패, 2 = 문서 조회 실패, 3 = 요소 비교 실패.
    internal static volatile int TestFail;
    private static volatile int _obsDocs = -1, _obsReason, _obsCompareExcluded;
    internal static void TestResetObservations() { _obsDocs = -1; _obsReason = 0; _obsCompareExcluded = 0; LastMainFrame = -1; }
    /// <summary>0–7: 다른 문서 수 + 1(0 = 아직 없음), 8–11: 불완전 이유(0 완전, 1 너무 많음, 2 열거 실패, 3 조회 실패, 4 칸 열거 실패), 12–19: 비교 실패로 뺀 칸 수, 20–23: 주입, 24–27: MainFrame 출발 reason + 1.
    /// 주입(TestFail): 1 문서 열거 실패, 2 문서 조회 실패, 3 요소 비교 실패, 4 클릭 단계로, 5 MainFrame 출발의 칸 신원 조회 실패, 6 MainFrame 출발의 대화 상자 확인 실패, 7 MainFrame 출발의 자식 프레임 확인 실패, 8 MainFrame 출발에서 브라우저 목록이 보인 것으로.</summary>
    internal static long TestObservations()
        => (long)Math.Min(_obsDocs + 1, 255) | ((long)(_obsReason & 0xF) << 8) | ((long)Math.Min(_obsCompareExcluded, 255) << 12) | ((long)(TestFail & 0xF) << 20)
           | ((long)((LastMainFrame + 1) & 0xF) << 24);   // 24–27: 마지막 MainFrame 출발 확인 reason + 1 (0 = 없음)
    private static bool Inject(int mode) => Program.IsTestMode && TestFail == mode;

    private static List<nint> NestedEdits(nint doc, out bool complete)
    {
        var result = new List<nint>();
        complete = false;
        nint docs = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, int, nint, nint*, int>)V(doc)[6])(doc, TreeScope_Descendants, _condDoc, &docs);
        if (Inject(1)) { Release(docs); docs = 0; hr = -1; }
        if (hr < 0) { _obsReason = 2; return result; }
        if (docs == 0) { _obsDocs = 0; _obsReason = 0; complete = true; return result; }   // 다른 문서 없음
        try
        {
            int n = 0;
            if (((delegate* unmanaged[Stdcall]<nint, int*, int>)V(docs)[3])(docs, &n) < 0) { _obsReason = 2; return result; }
            _obsDocs = n;
            if (n > MaxNestedDocs) { _obsReason = 1; return result; }
            for (int i = 0; i < n; i++)
            {
                nint d = 0;
                if (Inject(2) || ((delegate* unmanaged[Stdcall]<nint, int, nint*, int>)V(docs)[4])(docs, i, &d) < 0 || d == 0) { _obsReason = 3; return result; }
                try
                {
                    nint arr = 0;
                    if (((delegate* unmanaged[Stdcall]<nint, int, nint, nint*, int>)V(d)[6])(d, TreeScope_Descendants, _condEdit, &arr) < 0) { _obsReason = 4; return result; }
                    if (arr == 0) continue;   // 그 문서에 칸 없음
                    try
                    {
                        int m = 0;
                        if (((delegate* unmanaged[Stdcall]<nint, int*, int>)V(arr)[3])(arr, &m) < 0) { _obsReason = 4; return result; }
                        for (int k = 0; k < m; k++)
                        {
                            nint el = 0;
                            if (((delegate* unmanaged[Stdcall]<nint, int, nint*, int>)V(arr)[4])(arr, k, &el) < 0 || el == 0) { _obsReason = 4; return result; }
                            result.Add(el);
                        }
                    }
                    finally { Release(arr); }
                }
                finally { Release(d); }
            }
            complete = true;
            _obsReason = 0;
            return result;
        }
        finally { Release(docs); }
    }

    private static int ControlType(nint el) { int t = 0; ((delegate* unmanaged[Stdcall]<nint, int*, int>)V(el)[21])(el, &t); return t; }
    private static bool IsPassword(nint el) { int b = 0; ((delegate* unmanaged[Stdcall]<nint, int*, int>)V(el)[35])(el, &b); return b != 0; }

    private static bool Same(nint a, nint b) => Compare(a, b) == 1;

    /// <summary>CompareElements: 1 같음, 0 다름, -1 비교 실패.</summary>
    private static int Compare(nint a, nint b)
    {
        int same = 0;
        if (((delegate* unmanaged[Stdcall]<nint, nint, nint, int*, int>)V(_uia)[3])(_uia, a, b, &same) < 0) return -1;
        return same != 0 ? 1 : 0;
    }

    private static string BstrProp(nint el, int slot)
    {
        nint b = 0;
        if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)V(el)[slot])(el, &b) < 0) return "";
        return TakeBstr(b).Trim();
    }

    private static string TakeBstr(nint b)
    {
        if (b == 0) return "";
        try { return new string((char*)b, 0, (int)SysStringLen(b)); }
        finally { SysFreeString(b); }
    }

    private static nint* V(nint o) => *(nint**)o;
    private static void Release(nint o) { if (o != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)V(o)[2])(o); }
    private static void AddRef(nint o) { if (o != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)V(o)[1])(o); }

    // 채우기 대상 요소(UIA 스레드에서만 읽고 쓴다)
    private static nint _fillTarget;
    private static void SetFillTarget(nint el) { Release(_fillTarget); _fillTarget = el; AddRef(el); }

    /// <summary>
    /// 지금 포커스가 채우기 대상 요소 그대로인가(UIA 스레드에서 <see cref="Run"/> 으로 부른다). 같은 페이지의 다른 칸으로 옮김,
    /// 다른 탭으로 전환, 페이지가 그 칸을 새 요소로 바꿈 모두 false: 남은 글자를 보내지 않는다. 확인할 수 없어도 false.
    /// </summary>
    public static bool FocusIsFillTarget(Job job)
    {
        if (!_ok || _fillTarget == 0 || !job.Alive) return false;
        nint focused = 0;
        try
        {
            if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)V(_uia)[8])(_uia, &focused) < 0 || focused == 0) return false;
            return HasFocus(_fillTarget, focused);   // 커서를 옮길 때와 같은 판정(다른 입력란으로 옮겨 가면 거짓)
        }
        finally { Release(focused); }
    }

    /// <summary>채우기가 끝나면 대상 요소를 놓는다(UIA 스레드).</summary>
    public static bool ClearFillTarget(Job job) { Release(_fillTarget); _fillTarget = 0; return true; }

    [StructLayout(LayoutKind.Sequential)] private struct VARIANT { public ushort vt, r1, r2, r3; public int lo, hi; public long pad; }

    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint coinit);
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, nint outer, uint ctx, ref Guid iid, out nint obj);
    [DllImport("oleaut32.dll")] private static extern uint SysStringLen(nint bstr);
    [DllImport("oleaut32.dll")] private static extern void SysFreeString(nint bstr);
}

/// <summary>
/// 사이트 주소의 비교용 형태. 연결은 이 값이 **정확히 같을 때만** 맞는다(2026-09-30 사용자 결정, Codex V55-1).
/// 허용하는 정규화는 이것뿐이다: scheme·호스트 소문자, 기본 포트(https 443 / http 80) 제거, 사용자 정보(user:pass@) 제거,
/// 경로가 없으면 "/". **경로(대소문자·끝의 /)·쿼리(?…)·조각(#…)은 글자 그대로 둔다**: 같은 호스트에서도 쿼리로 계정·테넌트를,
/// 조각으로 SPA 화면을, 끝의 / 로 다른 자원을 가리킬 수 있다. http·https 의 절대 주소만, 호스트는 공백 없이, 포트는 숫자만.
/// 0.2.55~0.2.57 의 연결(쿼리·조각을 버린 값)은 그대로 읽히지만 쿼리·조각이 있는 페이지와는 더는 맞지 않는다(범위가 좁아질 뿐 넓어지지 않음):
/// 그런 페이지는 다시 연결한다.
/// </summary>
internal static class SiteUrl
{
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string s = raw.Trim();
        int sep = s.IndexOf("://", StringComparison.Ordinal);
        if (sep <= 0) return null;
        string scheme = s[..sep].ToLowerInvariant();
        if (scheme is not ("http" or "https")) return null;
        string rest = s[(sep + 3)..];
        int end = rest.IndexOfAny(new[] { '/', '?', '#' });
        string host = (end < 0 ? rest : rest[..end]).ToLowerInvariant();
        string tail = end < 0 ? "" : rest[end..];
        int at = host.LastIndexOf('@');
        if (at >= 0) host = host[(at + 1)..];   // 사용자 정보(user:pass@)는 버린다
        if (host.Length == 0 || host.Any(char.IsWhiteSpace) || tail.Any(c => c is ' ' or '\t' or '\r' or '\n')) return null;
        int colon = host.LastIndexOf(':');
        if (colon >= 0 && host.IndexOf(']') < colon)   // IPv6 [::1]:8080 도 마지막 : 뒤가 포트
        {
            string port = host[(colon + 1)..];
            if (port.Length == 0 || port.Length > 5 || !port.All(char.IsAsciiDigit)) return null;
            if ((scheme == "https" && port == "443") || (scheme == "http" && port == "80")) host = host[..colon];
        }
        if (host.StartsWith(':')) return null;
        if (tail.Length == 0 || tail[0] != '/') tail = "/" + tail;   // "https://a.com?x" → "https://a.com/?x"
        return scheme + "://" + host + tail;
    }

    /// <summary>진단 표시용: 쿼리 값과 조각은 가리고(키만 남김) 4자리 이상 숫자도 가린다. 비교에는 쓰지 않는다.</summary>
    public static string Masked(string raw)
    {
        string url = Normalize(raw) ?? raw;
        int hash = url.IndexOf('#');
        if (hash >= 0) url = url[..hash] + "#…";
        int q = url.IndexOf('?');
        if (q >= 0)
        {
            string end = url.Contains('#') ? url[url.IndexOf('#')..] : "";
            string query = url[(q + 1)..(url.Length - end.Length)];
            var keys = query.Split('&').Select(kv => { int eq = kv.IndexOf('='); return eq < 0 ? kv : kv[..eq] + "=…"; });
            url = url[..(q + 1)] + string.Join("&", keys) + end;
        }
        // 숫자 가리기는 경로·쿼리에만: 호스트·포트(예: localhost:18765)는 민감 정보가 아니고, 가리면 어느 사이트인지 알 수 없다
        int sep = url.IndexOf("://", StringComparison.Ordinal);
        int pathStart = sep < 0 ? -1 : url.IndexOf('/', sep + 3);
        return pathStart < 0 ? url : url[..pathStart] + Uia.MaskDigits(url[pathStart..]);
    }

    /// <summary>화면에 보일 짧은 형태: "host/path" (scheme 는 https 가 아닐 때만 앞에 둔다).</summary>
    public static string Display(string url)
    {
        if (url.StartsWith("https://", StringComparison.Ordinal)) return url[8..].TrimEnd('/');
        return url;
    }
}
