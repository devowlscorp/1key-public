namespace OneKey;

/// <summary>
/// 윈도우 프로그램의 입력칸 연결(2026-10-01 사용자 요청: 인하우스 뱅킹 로그인). 웹 페이지의 "주소·칸 id" 대신
/// 실행 파일 경로 · 최상위 창 클래스 · 칸의 창 클래스 · 그 창 안에 보이는 같은 클래스 칸 가운데 몇 번째인지 · 자리로 기억한다.
/// 사용자가 [연결] 칩을 누른 순간 커서가 있던 칸(에디트 계열)만 연결한다. 항목의 첫 입력에만 쓰고, 나머지 입력은 그 칸에서
/// Tab 으로 넘어가 넣는다(사람이 Tab 을 누르는 것과 같음. 두 번째 칸부터는 식별하지 않고 프로그램의 Tab 순서를 따른다). Title 은 표시용.
/// 자리(0.2.80): 칸 가운데의 창 기준 위치(Dx, Dy)와 그 칸을 담은 바탕 칸의 클래스·크기. 어떤 프로그램(인하우스)은 커서가 없는 칸의
/// 입력창을 창 밖에 치워 두고, 사람이 그 자리를 누르면 입력창을 옮겨 온다.
/// 0.2.83(Codex R82): Exe 는 전체 경로(같은 이름의 다른 위치 프로그램은 다른 프로그램). 칸은 순서만이 아니라 순서 + 자리 + 바탕 칸이
/// 모두 맞을 때만 그 칸이다. 자리 클릭은 그 점의 맨 위 창이 바탕 칸 자체일 때만(버튼·다른 칸이 덮고 있으면 누르지 않는다).
/// </summary>
internal sealed record AppLink(string Exe, string WindowClass, string FieldClass, int Order, string Title,
                               int Dx = 0, int Dy = 0, string HostClass = "", int HostW = 0, int HostH = 0)
{
    public bool HasPlace => HostClass.Length > 0 && HostW > 0 && HostH > 0;
    /// <summary>전체 경로로 기억한 연결인가. 0.2.76–0.2.82 의 연결은 파일 이름뿐이라 다시 연결해야 쓴다(이름만으로는 넣지 않는다).</summary>
    public bool HasPath => Path.IsPathFullyQualified(Exe);
    /// <summary>연결로 쓸 수 있는가: 경로와 자리가 모두 있어야 한다.</summary>
    public bool Usable => HasPath && HasPlace;
    public string FileName => Path.GetFileName(Exe);
}

internal static unsafe class AppLinks
{
    /// <summary>창을 만든 프로세스의 실행 파일 전체 경로. 못 읽으면 빈 글(그러면 어떤 연결에도 맞지 않는다).</summary>
    public static string PathOf(nint hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return "";
        nint h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == 0) return "";
        try { string p = Native.GetProcessImagePath(h); return Path.IsPathFullyQualified(p) ? p : ""; }
        finally { Native.CloseHandle(h); }
    }

    private static bool EditLike(string cls) => cls.Contains("edit", StringComparison.OrdinalIgnoreCase);

    private static bool Inside(in Native.RECT r, in Native.RECT wr)
        => r.right > r.left && r.bottom > r.top && r.left >= wr.left && r.right <= wr.right && r.top >= wr.top && r.bottom <= wr.bottom;

    /// <summary>
    /// 최상위 창 안에서 클래스가 cls 인 칸들 가운데 **창 안에 보이는 것만**, 창 나무 순서(Z 순서 깊이 우선)대로.
    /// 창 밖으로 치워 둔 칸은 세지도 넣지도 않는다(인하우스: 커서가 없는 칸의 입력창은 창 밖에 있다).
    /// </summary>
    public static List<nint> Fields(nint root, string cls)
    {
        var list = new List<nint>();
        if (!Native.GetWindowRect(root, out Native.RECT wr)) return list;
        void Walk(nint parent, int depth)
        {
            if (depth > 32 || list.Count > 256) return;
            for (nint c = Native.GetWindow(parent, Native.GW_CHILD); c != 0; c = Native.GetWindow(c, Native.GW_HWNDNEXT))
            {
                if (!Native.IsWindowVisible(c)) continue;
                if (Native.GetClassName(c) == cls && Native.GetWindowRect(c, out Native.RECT r) && Inside(r, wr)) list.Add(c);
                Walk(c, depth + 1);
            }
        }
        Walk(root, 0);
        return list;
    }

    /// <summary>[연결] 칩을 누른 순간의 창·칸으로 연결을 만든다. 에디트 계열 칸이 아니거나 찾을 근거(경로·자리)가 없으면 null.</summary>
    public static AppLink? Capture(in Injector.Request r)
    {
        if (r.Window == 0 || r.Focus == 0 || r.Focus == r.Window) return null;
        if (!EditLike(Native.GetClassName(r.Focus))) return null;
        // 커서가 있는 입력창의 부모도 에디트 계열이면(예: TicEdit 안의 TicEditor) 그 부모 칸을 기억한다.
        nint field = r.Focus, parent = Native.GetParent(r.Focus);
        if (parent != 0 && parent != r.Window && EditLike(Native.GetClassName(parent))) field = parent;
        string fcls = Native.GetClassName(field);
        int order = Fields(r.Window, fcls).IndexOf(field);
        if (order < 0) return null;
        string exe = PathOf(r.Window);
        if (exe.Length == 0) return null;
        string title = Native.GetWindowText(r.Window);
        if (title.Length > 60) title = title[..60];
        // 자리: 칸 가운데(창 기준)와 그 칸을 담은 바탕 칸(최상위 창 자신일 수도 있다. 그때는 자리 클릭은 하지 않는다)
        nint host = Native.GetParent(field);
        if (host == 0 || !Native.GetWindowRect(r.Window, out Native.RECT wr) || !Native.GetWindowRect(field, out Native.RECT fr)
            || !Native.GetWindowRect(host, out Native.RECT hr)) return null;
        int dx = (fr.left + fr.right) / 2 - wr.left, dy = (fr.top + fr.bottom) / 2 - wr.top;
        int hw = hr.right - hr.left, hh = hr.bottom - hr.top;
        string hostCls = Native.GetClassName(host);
        if (hostCls.Length == 0 || hw <= 0 || hh <= 0) return null;
        return new AppLink(exe, Native.GetClassName(r.Window), fcls, order, title, dx, dy, hostCls, hw, hh);
    }

    /// <summary>
    /// 연결한 프로그램의 보이는 최상위 창(실행 파일 경로·창 클래스가 같은 창). 앞에 있는 창이 맞으면 그 창. 아니면 맞는 창이 하나뿐일 때만
    /// 그 창(둘 이상이면 어느 창인지 정하지 않는다: many = true, Codex 13:19 권고). 없으면 0.
    /// </summary>
    public static nint FindWindow(AppLink l, nint preferred, out bool many)
    {
        many = false;
        if (!l.HasPath) return 0;
        if (Matches(preferred, l)) return preferred;
        nint found = 0, h = 0;
        fixed (char* c = l.WindowClass)
            while ((h = Native.FindWindowExW(0, h, c, null)) != 0)
                if (Matches(h, l))
                {
                    if (found != 0) { many = true; return 0; }
                    found = h;
                }
        return found;
    }

    /// <summary>경로까지 같은 창인가. 경로를 못 읽으면 맞지 않는 것으로 본다(파일 이름 비교로 낮추지 않는다, Codex R82).</summary>
    public static bool Matches(nint h, AppLink l)
        => h != 0 && l.HasPath && Native.IsWindowVisible(h) && Native.GetClassName(h) == l.WindowClass
           && string.Equals(PathOf(h), l.Exe, StringComparison.OrdinalIgnoreCase);

    /// <summary>연결한 창이 없을 때: 파일 이름·창 클래스는 같은데 다른 위치에서 실행된 창이 있으면 그 경로(안내용). 없으면 null.</summary>
    public static string? OtherPath(AppLink l)
    {
        nint h = 0;
        fixed (char* c = l.WindowClass)
            while ((h = Native.FindWindowExW(0, h, c, null)) != 0)
            {
                if (!Native.IsWindowVisible(h)) continue;
                string p = PathOf(h);
                if (p.Length > 0 && !string.Equals(p, l.Exe, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Path.GetFileName(p), l.FileName, StringComparison.OrdinalIgnoreCase)) return p;
            }
        return null;
    }

    /// <summary>칸이 연결한 자리에 있는가: 칸 가운데가 기억한 자리(±6)이고, 바로 위 창이 같은 클래스·같은 크기(±2)의 바탕 칸.</summary>
    private static bool AtPlace(nint field, AppLink l, in Native.RECT wr)
    {
        if (!Native.GetWindowRect(field, out Native.RECT fr)) return false;
        int cx = (fr.left + fr.right) / 2 - wr.left, cy = (fr.top + fr.bottom) / 2 - wr.top;
        if (Math.Abs(cx - l.Dx) > 6 || Math.Abs(cy - l.Dy) > 6) return false;
        nint host = Native.GetParent(field);
        return host != 0 && Native.GetClassName(host) == l.HostClass && Native.GetWindowRect(host, out Native.RECT hr)
               && Math.Abs(hr.right - hr.left - l.HostW) <= 2 && Math.Abs(hr.bottom - hr.top - l.HostH) <= 2;
    }

    /// <summary>
    /// 그 창 안에 보이는 연결한 칸. 같은 클래스 칸의 순서만으로 정하지 않는다(R82-2): 그 순서의 칸이 연결한 자리·바탕 칸에도 맞아야 하고,
    /// 그 자리에 맞는 같은 클래스 칸이 그 하나뿐이어야 한다. 칸이 더해지거나 빠지거나 자리가 바뀌었으면(다른 화면 포함) 0.
    /// </summary>
    public static nint FindField(nint root, AppLink l)
    {
        if (!l.Usable || !Native.GetWindowRect(root, out Native.RECT wr)) return 0;
        var fields = Fields(root, l.FieldClass);
        if (l.Order >= fields.Count) return 0;
        nint f = fields[l.Order];
        if (!AtPlace(f, l, wr)) return 0;
        foreach (nint o in fields) if (o != f && AtPlace(o, l, wr)) return 0;   // 같은 자리에 둘: 모호하면 거절
        return f;
    }

    /// <summary>
    /// 입력창이 화면에 없을 때: 연결한 자리를 눌러도 되는가. 그 점의 맨 위 창(WindowFromPoint)이 **바탕 칸 자체**(같은 클래스·같은 크기 ±2)일 때만.
    /// 바탕 칸 안의 버튼·다른 칸·알 수 없는 창이 그 점을 덮고 있으면 그 창이 나오므로 누르지 않는다(R82-1: 부모가 맞으면 통과하던 것을 없앰).
    /// 바탕 칸이 최상위 창 자신이면 누르지 않는다. 누르기 바로 앞의 확인이다(다른 화면·창 크기 변경이면 누르지 않는다).
    /// </summary>
    public static bool PlaceAt(nint root, AppLink l, out int x, out int y, out Native.RECT hostRect, out string why)
    {
        x = y = 0; hostRect = default; why = "";
        if (!l.Usable || !Native.GetWindowRect(root, out Native.RECT wr)) { why = "notfound"; return false; }
        x = wr.left + l.Dx; y = wr.top + l.Dy;
        if (x <= wr.left || x >= wr.right || y <= wr.top || y >= wr.bottom) { why = "place"; return false; }
        nint hit = Native.WindowFromPoint(new Native.POINT { x = x, y = y });
        if (hit == 0 || hit == root || Native.GetAncestor(hit, 2) != root) { why = "place"; return false; }
        string cls = Native.GetClassName(hit);
        if (cls != l.HostClass) { why = "place-hit=" + cls; return false; }
        if (!Native.GetWindowRect(hit, out Native.RECT hr) || Math.Abs(hr.right - hr.left - l.HostW) > 2 || Math.Abs(hr.bottom - hr.top - l.HostH) > 2
            || !Inside(hr, wr)) { why = "place"; return false; }
        hostRect = hr;
        return true;
    }

    /// <summary>
    /// 연결한 칸에 커서를 넣고 그 칸(바깥 칸)을 돌려준다. 실패하면 0 과 짧은 이유.
    /// ① 입력창이 화면에 있으면: 입력 스레드에 잠시 붙어 SetFocus → 안 되면 그 칸을 한 번 클릭.
    /// ② 없으면: 연결한 자리의 맨 위 창이 바탕 칸 자체일 때만 그 자리를 한 번 클릭해 입력창을 불러온다.
    /// 끝에 포커스가 연결한 칸(순서·자리·바탕 칸이 모두 맞는 칸, 또는 그 안의 입력창)인지 확인한다. 창이 앞에 있을 때만.
    /// </summary>
    public static nint Focus(nint root, AppLink l, Func<bool> alive, out string why, bool allowClick = true)
    {
        why = "";
        if (!l.Usable) { why = "relink"; return 0; }
        if (!alive()) { why = "stopped"; return 0; }
        if (Native.GetForegroundWindow() != root) { why = "front=" + Native.GetClassName(Native.GetForegroundWindow()); return 0; }
        uint tid = Native.GetWindowThreadProcessId(root, out _);
        nint field = FindField(root, l);
        if (field != 0)
        {
            if (InField(Injector.FocusOf(tid), field)) return field;
            uint me = Native.GetCurrentThreadId();
            // 채우기 작업 스레드는 창을 만들지 않아 메시지 큐가 없을 수 있다. 큐가 없으면 AttachThreadInput 이 실패하고 SetFocus 도
            // 다른 스레드의 창에는 듣지 않아, 지금까지 늘 클릭으로 넘어갔다(0.2.94 IJ62: 클릭 끔이면 칸이 보여도 못 넣음)
            Native.EnsureMessageQueue();
            bool attached = tid != 0 && tid != me && Native.AttachThreadInput(me, tid, true);
            if (Program.IsTestMode) Injector.TestTrace($"appfocus attach={attached}");
            try { Native.SetFocus(field); }
            finally { if (attached) Native.AttachThreadInput(me, tid, false); }
            for (int i = 0; i < 6; i++) { Thread.Sleep(30); if (InField(Injector.FocusOf(tid), field)) return field; }
            if (!allowClick) { why = "noclick"; return 0; }   // 항목 설정: 화면을 클릭하지 않는다
            // 누르기 직전: 기다림(Ctrl·Alt 떼기)을 먼저, 그 뒤 작업·앞의 창·칸의 지금 사각형과 그 점의 창이 그 칸(또는 그 안)인지 (R75-3)
            Injector.ReleaseHeldModifiers();
            if (!Injector.TestHoldPoint("appclick.mods")) { why = "stopped"; return 0; }   // 검증 전용 경계
            if (!alive() || Native.GetForegroundWindow() != root) { why = "stopped"; return 0; }
            if (!Native.GetWindowRect(field, out Native.RECT r)) { why = "rect"; return 0; }
            int cx = (r.left + r.right) / 2, cy = (r.top + r.bottom) / 2;
            if (!InField(Native.WindowFromPoint(new Native.POINT { x = cx, y = cy }), field)) { why = "covered"; return 0; }
            if (!ClickAt(cx, cy)) { why = "click-blocked"; return 0; }
            for (int i = 0; i < 10; i++) { Thread.Sleep(30); if (InField(Injector.FocusOf(tid), field)) return field; }
            why = "focus=" + FocusName(Injector.FocusOf(tid));
            return 0;
        }
        if (!allowClick) { why = "noclick"; return 0; }   // 항목 설정: 자리 클릭도 하지 않는다(사용자가 칸을 먼저 누른다)
        Injector.ReleaseHeldModifiers();   // 기다림을 먼저 끝내고, 자리 확인은 누르기 바로 앞에서 (R75-3)
        if (!Injector.TestHoldPoint("appplace.mods")) { why = "stopped"; return 0; }   // 검증 전용 경계: 여기서 자리를 가리거나 바꾼다
        if (!alive() || Native.GetForegroundWindow() != root) { why = "stopped"; return 0; }
        if (!PlaceAt(root, l, out int x, out int y, out _, out why)) return 0;
        if (!ClickAt(x, y)) { why = "click-blocked"; return 0; }
        for (int i = 0; i < 15; i++)
        {
            Thread.Sleep(40);
            nint box = FindField(root, l);   // 불려 온 입력창이 연결한 칸(순서·자리·바탕 칸)이고 포커스가 그 안일 때만
            if (box != 0 && InField(Injector.FocusOf(tid), box)) return box;
        }
        why = "focus=" + FocusName(Injector.FocusOf(tid));
        return 0;
    }

    private static string FocusName(nint f) => f == 0 ? "none" : Native.GetClassName(f);

    /// <summary>화면 한 점을 한 번 클릭하고 포인터를 되돌린다. 사용자가 누르고 있는 Ctrl·Alt 는 먼저 뗀다.</summary>
    private static bool ClickAt(int x, int y)
    {
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

    /// <summary>포커스 창이 그 칸이거나 그 칸 안의 입력창인가(3단계까지).</summary>
    public static bool InField(nint focus, nint field)
    {
        for (int i = 0; i < 3 && focus != 0; i++, focus = Native.GetParent(focus))
            if (focus == field) return true;
        return false;
    }

    /// <summary>[채우기] 버튼 자리: 연결한 칸이 보이면 그 칸, 없으면 눌러도 되는 자리의 바탕 칸. 둘 다 없으면 false.</summary>
    public static bool ButtonAnchor(nint root, AppLink l, out Native.RECT anchor)
    {
        nint field = FindField(root, l);
        if (field != 0 && Native.GetWindowRect(field, out anchor)) return true;
        return PlaceAt(root, l, out _, out _, out anchor, out _);
    }

    /// <summary>편집 화면 표시(한 줄): "Terminal01.exe · TicEdit 1번째 칸 · 실행 파일 경로"(예전 연결이면 다시 연결 안내).</summary>
    public static string Describe(AppLink l)
        => T.EditAppLinked(l.FileName, l.FieldClass, l.Order + 1) + " · " + (l.Usable ? l.Exe : T.EditAppRelink);
}
