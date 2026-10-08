using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>Win32 API 선언 모음. WinForms/WPF 의존성 없이 순수 Win32 로만 동작한다.</summary>
internal static unsafe class Native
{
    // ---------- 윈도우 메시지 ----------
    public const uint WM_CREATE = 0x0001, WM_DESTROY = 0x0002, WM_CLOSE = 0x0010;
    public const uint WM_SETFONT = 0x0030;
    public const uint WM_COMMAND = 0x0111, WM_TIMER = 0x0113, WM_HOTKEY = 0x0312;
    public const uint WM_CTLCOLORSTATIC = 0x0138, WM_CTLCOLORBTN = 0x0135, WM_CTLCOLORDLG = 0x0136;
    public const uint WM_CTLCOLOREDIT = 0x0133, WM_CTLCOLORLISTBOX = 0x0134;
    public const uint WM_ERASEBKGND = 0x0014, WM_SETTINGCHANGE = 0x001A, WM_SETICON = 0x0080;
    public const uint STM_SETICON = 0x0170, SS_ICON = 0x0003;
    public const nint ICON_SMALL = 0, ICON_BIG = 1;
    public const uint WM_SYSCOMMAND = 0x0112, WM_DPICHANGED = 0x02E0;
    public const uint WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_CHAR = 0x0102;
    public const uint WM_APP = 0x8000;
    public const uint WM_TRAY = WM_APP + 1;
    public const uint WM_SHOWME = WM_APP + 3;
    /// <summary>실행 중인 인스턴스에 버전을 묻는다(응답: 버전 코드). 0.1.5 부터 지원.</summary>
    public const uint WM_ONEKEY_VERSION = WM_APP + 4;
    /// <summary>실행 중인 인스턴스에 정상 종료를 요청한다. 0.1.5 부터 지원.</summary>
    public const uint WM_ONEKEY_QUIT = WM_APP + 5;
    /// <summary>목록 화면을 다시 만들라는 내부 요청 (검색 칸의 EN_CHANGE 처리 중에 컨트롤을 부수지 않기 위해 미룬다).</summary>
    public const uint WM_LIST_REFRESH = WM_APP + 7;
    /// <summary>칩의 [입력]/[취소]를 본창이 처리하라는 내부 요청 (칩 버튼의 알림 처리 중에 칩을 부수지 않기 위해 미룬다). wParam 1 = 입력, 0 = 취소.</summary>
    public const uint WM_CHIP_COMMAND = WM_APP + 8;
    /// <summary>검증 전용(ONEKEY_TEST=1): 전체 GC 를 한 뒤 관리 힙 크기(KiB)를 돌려준다. 메모리 측정이 "아직 걷지 않은 쓰레기"와 "새는 것"을 가르게 한다(fxmem.ps1).</summary>
    public const uint WM_TEST_GC = WM_APP + 9;
    /// <summary>검증 전용(ONEKEY_TEST=1): 지금까지 만든 토스트 수(생성 순번). 저장마다 새 토스트가 떴는지 옛 토스트와 구분한다(fxmem.ps1, Codex V47-2).</summary>
    public const uint WM_TEST_TOAST_COUNT = WM_APP + 10;
    /// <summary>검증 전용(ONEKEY_TEST=1): GC 를 하지 않고 지금까지의 자연 GC 횟수. 세대 0 | 세대 1 &lt;&lt; 20 | 세대 2 &lt;&lt; 40, 그리고 60번 비트(응답함 표시).</summary>
    public const uint WM_TEST_GC_COUNT = WM_APP + 11;
    /// <summary>검증 전용(ONEKEY_TEST=1): 기다리는 후속 작업. 1 = 테스트 입력 대기, 2 = 창 진단 대기, 그리고 떠 있는 상자 수 &lt;&lt; 8, 60번 비트 = 응답함.
    /// 잠금이 닫은 안내 뒤에 입력·진단이 시작되지 않았는지 본다(fx.ps1).</summary>
    public const uint WM_TEST_PENDING = WM_APP + 12;
    /// <summary>검증 전용(ONEKEY_TEST=1): 관리자 재시작 질문(OfferElevation)을 띄운다. PostMessage 로 보낸다(상자가 뜨므로).
    /// WM_TEST_PENDING 의 16~23번 비트가 admin:dry-run 에서 재시작이 불린 횟수다.</summary>
    public const uint WM_TEST_OFFER_ELEVATION = WM_APP + 13;
    /// <summary>검증 전용(ONEKEY_TEST=1): 글자 렌더러 상태(<see cref="Dw.StatusBits"/>) | 60번 비트(응답함).</summary>
    public const uint WM_TEST_RENDERER = WM_APP + 14;
    /// <summary>검증 전용(ONEKEY_TEST=1): 렌더러 전환 때와 같은 "상태 보존 재배치"를 지금 한다(포커스·선택·스크롤 보존 시험).</summary>
    public const uint WM_TEST_RELAYOUT = WM_APP + 15;
    /// <summary>사이트 채우기: UIA 스레드의 주기적 확인 결과가 준비됨(본창에).</summary>
    public const uint WM_SITE_PROBE = WM_APP + 16;
    /// <summary>사이트 채우기: 떠 있는 [채우기] 버튼을 눌렀다(본창에).</summary>
    public const uint WM_FILL_COMMAND = WM_APP + 17;
    /// <summary>사이트 채우기: 채우기 작업이 끝났다(오류가 있으면 알림). 본창에.</summary>
    public const uint WM_FILL_DONE = WM_APP + 18;
    /// <summary>검증 전용(ONEKEY_TEST=1): 사이트 채우기 상태(연결 수 · 감시 중 · 버튼 보임 · 마지막 확인 결과).</summary>
    public const uint WM_TEST_SITE = WM_APP + 19;
    /// <summary>자동 실행 따라가기(0.2.57) 결과가 준비됨(본창에).</summary>
    /// <summary>UIA 스레드의 작업이 끝났다: 뒤처리(Uia.DrainDone)를 UI 스레드에서.</summary>
    public const uint WM_UIA_DONE = WM_APP + 21;
    public const uint WM_TEST_SITE2 = WM_APP + 22;   // 시험 모드 전용 관찰값 (App.WndProc)
    public const uint WM_MOUSEACTIVATE = 0x0021;
    public const int MA_NOACTIVATE = 3;
    public const uint WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080;
    public const uint SS_NOPREFIX = 0x00000080, SS_ENDELLIPSIS = 0x00004000, SS_PATHELLIPSIS = 0x8000;
    public const int SW_SHOWNOACTIVATE = 4;
    public const uint ES_MULTILINE = 0x0004, ES_READONLY = 0x0800, ES_AUTOVSCROLL = 0x0040;
    [DllImport("user32.dll")] public static extern nint GetActiveWindow();
    [DllImport("user32.dll")] public static extern nint GetAncestor(nint hwnd, uint gaFlags);   // 2 = GA_ROOT
    // 검증 하네스와의 동기화용 이름 있는 이벤트 (ONEKEY_TEST=1 일 때만 쓴다, T10)
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern nint CreateEventW(nint attrs, bool manualReset, bool initialState, string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern nint OpenEventW(uint access, bool inherit, string name);
    [DllImport("kernel32.dll")] public static extern bool SetEvent(nint h);
    public const uint EVENT_MODIFY_STATE = 0x0002;
    [DllImport("user32.dll")] public static extern int SetWindowRgn(nint hWnd, nint hRgn, bool bRedraw);
    [DllImport("gdi32.dll")] public static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] public static extern int SaveDC(nint hdc);
    [DllImport("gdi32.dll")] public static extern bool RestoreDC(nint hdc, int nSavedDC);
    [DllImport("gdi32.dll")] public static extern int IntersectClipRect(nint hdc, int left, int top, int right, int bottom);
    public const uint WM_MOUSEWHEEL = 0x020A;
    public const uint WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205, WM_LBUTTONDBLCLK = 0x0203;
    public const uint WM_CONTEXTMENU = 0x007B;
    public const uint SC_MINIMIZE = 0xF020;

    // ---------- 윈도우 스타일 ----------
    public const uint WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000;
    public const uint WS_CAPTION = 0x00C00000, WS_SYSMENU = 0x00080000, WS_MINIMIZEBOX = 0x00020000;
    public const uint WS_TABSTOP = 0x00010000, WS_GROUP = 0x00020000, WS_BORDER = 0x00800000;
    public const uint WS_CLIPCHILDREN = 0x02000000, WS_CLIPSIBLINGS = 0x04000000;
    public const uint WS_VSCROLL = 0x00200000;
    public const uint WS_EX_CLIENTEDGE = 0x00000200, WS_EX_CONTROLPARENT = 0x00010000;
    public const uint WS_EX_APPWINDOW = 0x00040000;
    /// <summary>창과 자식 컨트롤을 한 장의 버퍼에 합성해서 그린다 (펼침/접힘, 크기 변경 때 깜빡임 방지).</summary>
    public const uint WS_EX_COMPOSITED = 0x02000000;
    public const uint WM_LBUTTONDOWN = 0x0201;

    public const uint BS_DEFPUSHBUTTON = 0x0001, BS_AUTOCHECKBOX = 0x0003, BS_GROUPBOX = 0x0007;
    public const uint ES_PASSWORD = 0x0020, ES_AUTOHSCROLL = 0x0080, ES_NUMBER = 0x2000;
    public const uint SS_LEFT = 0x0000, SS_CENTER = 0x0001, SS_RIGHT = 0x0002, SS_CENTERIMAGE = 0x0200;
    public const uint CBS_DROPDOWNLIST = 0x0003, CBS_HASSTRINGS = 0x0200;

    public const uint BM_GETCHECK = 0x00F0, BM_SETCHECK = 0x00F1;
    public const uint EM_SETPASSWORDCHAR = 0x00CC, EM_LIMITTEXT = 0x00C5;
    public const uint CB_ADDSTRING = 0x0143, CB_SETCURSEL = 0x014E, CB_GETCURSEL = 0x0147;
    public const int BST_UNCHECKED = 0, BST_CHECKED = 1;
    public const uint BN_CLICKED = 0;

    public const int SW_HIDE = 0, SW_SHOWNORMAL = 1, SW_RESTORE = 9;

    // ---------- 구조체 ----------
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint hwnd; public uint message; public nint wParam, lParam;
        public uint time; public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEXW
    {
        public uint cbSize, style;
        public nint lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground;
        public char* lpszMenuName, lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx, dy; public uint mouseData, dwFlags, time; public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk, wScan; public uint dwFlags, time; public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HARDWAREINPUT { public uint uMsg; public ushort wParamL, wParamH; }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT { public uint type; public INPUTUNION U; }

    public const uint INPUT_KEYBOARD = 1, INPUT_MOUSE = 0;
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004, KEYEVENTF_SCANCODE = 0x0008;

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public uint cbSize, flags;
        public nint hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public RECT rcCaret;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct LOGFONTW
    {
        public int lfHeight, lfWidth, lfEscapement, lfOrientation, lfWeight;
        public byte lfItalic, lfUnderline, lfStrikeOut, lfCharSet;
        public byte lfOutPrecision, lfClipPrecision, lfQuality, lfPitchAndFamily;
        public fixed char lfFaceName[32];
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NONCLIENTMETRICSW
    {
        public uint cbSize;
        public int iBorderWidth, iScrollWidth, iScrollHeight, iCaptionWidth, iCaptionHeight;
        public LOGFONTW lfCaptionFont;
        public int iSmCaptionWidth, iSmCaptionHeight;
        public LOGFONTW lfSmCaptionFont;
        public int iMenuWidth, iMenuHeight;
        public LOGFONTW lfMenuFont, lfStatusFont, lfMessageFont;
        public int iPaddedBorderWidth;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATAW
    {
        public uint cbSize; public nint hWnd; public uint uID, uFlags, uCallbackMessage;
        public nint hIcon;
        public fixed char szTip[128];
        public uint dwState, dwStateMask;
        public fixed char szInfo[256];
        public uint uVersion;
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    public const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    public const uint NIF_MESSAGE = 0x01, NIF_ICON = 0x02, NIF_TIP = 0x04, NIF_INFO = 0x10, NIF_SHOWTIP = 0x80;
    public const uint NOTIFYICON_VERSION_4 = 4;
    public const uint NIIF_INFO = 0x01, NIIF_WARNING = 0x02, NIIF_ERROR = 0x03;

    [StructLayout(LayoutKind.Sequential)]
    public struct INITCOMMONCONTROLSEX { public uint dwSize, dwICC; }
    public const uint ICC_STANDARD_CLASSES = 0x4000, ICC_WIN95_CLASSES = 0x000000FF;

    [StructLayout(LayoutKind.Sequential)]
    public struct DATA_BLOB { public uint cbData; public nint pbData; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHELLEXECUTEINFOW
    {
        public uint cbSize; public uint fMask; public nint hwnd;
        public char* lpVerb, lpFile, lpParameters, lpDirectory;
        public int nShow; public nint hInstApp, lpIDList;
        public char* lpClass;
        public nint hkeyClass; public uint dwHotKey; public nint hIcon; public nint hProcess;
    }

    // ---------- user32 ----------
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint CreateWindowExW(uint dwExStyle, char* lpClassName, char* lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")] public static extern bool DestroyWindow(nint hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(nint hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool EnableWindow(nint hWnd, bool bEnable);
    public const uint WS_POPUP = 0x80000000;
    public const uint WS_EX_DLGMODALFRAME = 0x00000001, WS_EX_TOPMOST = 0x00000008;
    [DllImport("user32.dll")] public static extern bool UpdateWindow(nint hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindowEnabled(nint hWnd);
    [DllImport("user32.dll")] public static extern bool IsChild(nint hWndParent, nint hWnd);

    [StructLayout(LayoutKind.Sequential)]
    public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    /// <summary>마지막 키보드·마우스 입력 이후 흐른 시간(ms). 실패하면 -1.</summary>
    public static long InputIdleMs()
    {
        var lii = new LASTINPUTINFO { cbSize = (uint)sizeof(LASTINPUTINFO) };
        if (!GetLastInputInfo(ref lii)) return -1;
        // 두 값 모두 32비트 틱 카운터라서, unchecked 로 빼면 49.7일 순환도 올바르게 처리된다.
        return unchecked((uint)Environment.TickCount - lii.dwTime);
    }
    [DllImport("user32.dll")] public static extern bool IsIconic(nint hWnd);
    [DllImport("user32.dll")] public static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetMessageW(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
    [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG lpMsg);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint DispatchMessageW(ref MSG lpMsg);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool IsDialogMessageW(nint hDlg, ref MSG lpMsg);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint SendMessageW(nint hWnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    public static extern nint SendMessageStr(nint hWnd, uint msg, nint wParam, char* lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool PostMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool SetWindowTextW(nint hWnd, char* lpString);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextW(nint hWnd, char* lpString, int nMaxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassNameW(nint hWnd, char* lpString, int nMaxCount);

    [DllImport("user32.dll")] public static extern bool MoveWindow(nint hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] public static extern bool GetClientRect(nint hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool AdjustWindowRectEx(ref RECT lpRect, uint dwStyle, bool bMenu, uint dwExStyle);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern uint GetDpiForSystem();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint LoadCursorW(nint hInstance, nint lpCursorName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint LoadIconW(nint hInstance, nint lpIconName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint LoadImageW(nint hInst, nint name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int MessageBoxW(nint hWnd, char* lpText, char* lpCaption, uint uType);

    [DllImport("user32.dll")] public static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint FindWindowW(char* lpClassName, char* lpWindowName);

    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint hWnd);
    [DllImport("user32.dll")] public static extern nint GetWindow(nint hWnd, uint uCmd);
    [DllImport("user32.dll")] public static extern nint WindowFromPoint(POINT pt);
    public const uint GW_HWNDNEXT = 2, GW_CHILD = 5;
    [DllImport("user32.dll")] public static extern nint FindWindowExW(nint hWndParent, nint hWndChildAfter, char* lpszClass, char* lpszWindow);
    [DllImport("user32.dll")] public static extern nint SetFocus(nint hWnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] public static extern bool PeekMessageW(out MSG msg, nint hWnd, uint min, uint max, uint remove);
    /// <summary>이 스레드에 메시지 큐를 만든다(PM_NOREMOVE 로 엿보기만). 큐가 없는 스레드는 AttachThreadInput 이 실패한다.</summary>
    public static void EnsureMessageQueue() => PeekMessageW(out _, 0, 0, 0, 0);
    [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO pgui);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT* pInputs, int cbSize);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] public static extern short GetKeyState(int vKey);
    [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);
    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode)] public static extern nint SysAllocString(string s);
    [DllImport("imm32.dll")] public static extern nint ImmGetContext(nint hWnd);
    [DllImport("imm32.dll")] public static extern bool ImmReleaseContext(nint hWnd, nint hIMC);
    [DllImport("imm32.dll")] public static extern bool ImmGetOpenStatus(nint hIMC);
    [DllImport("imm32.dll")] public static extern bool ImmSetOpenStatus(nint hIMC, bool open);
    [DllImport("imm32.dll")] public static extern bool ImmGetConversionStatus(nint hIMC, out uint conversion, out uint sentence);
    [DllImport("imm32.dll")] public static extern bool ImmSetConversionStatus(nint hIMC, uint conversion, uint sentence);
    [DllImport("user32.dll")] public static extern uint MapVirtualKeyW(uint uCode, uint uMapType);
    [DllImport("user32.dll")] public static extern short VkKeyScanW(char ch);
    [DllImport("user32.dll")] public static extern uint MapVirtualKeyExW(uint uCode, uint uMapType, nint dwhkl);
    [DllImport("user32.dll")] public static extern short VkKeyScanExW(char ch, nint dwhkl);
    [DllImport("user32.dll")] public static extern nint GetKeyboardLayout(uint idThread);
    [DllImport("user32.dll")] public static extern bool IsHungAppWindow(nint hWnd);
    [DllImport("user32.dll")] public static extern bool AdjustWindowRectExForDpi(ref RECT lpRect, uint dwStyle, bool bMenu, uint dwExStyle, uint dpi);
    [DllImport("user32.dll")] public static extern uint RegisterWindowMessageW(char* lpString);
    [DllImport("user32.dll")] public static extern uint RegisterClipboardFormatW(char* lpszFormat);
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] public static extern int CountClipboardFormats();
    public const uint WM_ACTIVATE = 0x0006;
    [DllImport("user32.dll")] public static extern nint SetActiveWindow(nint hWnd);
    public const uint DT_WORDBREAK = 0x10, DT_CALCRECT = 0x400;
    public const uint DWMWA_BORDER_COLOR = 34;
    [DllImport("wtsapi32.dll")] public static extern bool WTSRegisterSessionNotification(nint hWnd, uint dwFlags);
    [DllImport("wtsapi32.dll")] public static extern bool WTSUnRegisterSessionNotification(nint hWnd);
    public const uint WM_WTSSESSION_CHANGE = 0x02B1, WTS_SESSION_LOCK = 0x7, WM_POWERBROADCAST = 0x0218, PBT_APMSUSPEND = 0x4;
    [DllImport("user32.dll")] public static extern bool DestroyIcon(nint hIcon);
    [DllImport("user32.dll")] public static extern bool IsWindow(nint hWnd);
    [DllImport("user32.dll")] public static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);
    [DllImport("user32.dll")] public static extern nint MonitorFromPoint(POINT pt, uint dwFlags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SetPropW(nint hwnd, string name, nint data);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfoW(nint hMonitor, ref MONITORINFO lpmi);
    [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public uint cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const nint HWND_MESSAGE = -3;
    [DllImport("user32.dll")] public static extern bool ChangeWindowMessageFilterEx(nint hwnd, uint message, uint action, nint pChangeFilterStruct);
    public const uint MSGFLT_ALLOW = 1;

    [DllImport("user32.dll")] public static extern nint CreatePopupMenu();
    [DllImport("user32.dll")] public static extern bool DestroyMenu(nint hMenu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool AppendMenuW(nint hMenu, uint uFlags, nuint uIDNewItem, char* lpNewItem);
    [DllImport("user32.dll")]
    public static extern int TrackPopupMenu(nint hMenu, uint uFlags, int x, int y, int nReserved, nint hWnd, nint prcRect);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);

    public const uint MF_STRING = 0x0000, MF_SEPARATOR = 0x0800, MF_GRAYED = 0x0001;
    public const uint TPM_RIGHTBUTTON = 0x0002, TPM_RETURNCMD = 0x0100, TPM_BOTTOMALIGN = 0x0020;

    [DllImport("user32.dll")] public static extern nint SetTimer(nint hWnd, nuint nIDEvent, uint uElapse, nint lpTimerFunc);
    [DllImport("user32.dll")] public static extern bool KillTimer(nint hWnd, nuint nIDEvent);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool SystemParametersInfoW(uint uiAction, uint uiParam, void* pvParam, uint fWinIni);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool SystemParametersInfoForDpi(uint uiAction, uint uiParam, void* pvParam, uint fWinIni, uint dpi);
    public const uint SPI_GETNONCLIENTMETRICS = 0x0029;

    [DllImport("user32.dll")] public static extern bool InvalidateRect(nint hWnd, nint lpRect, bool bErase);
    [DllImport("user32.dll")] public static extern nint GetDC(nint hWnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(nint hWnd, nint hDC);
    [DllImport("user32.dll")] public static extern nint SendMessageW(nint hWnd, uint msg, nint wParam, ref RECT lParam);
    public const uint EM_SETRECT = 0x00B3;

    [DllImport("gdi32.dll")] public static extern nint SelectObject(nint hdc, nint h);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern bool GetTextMetricsW(nint hdc, out TEXTMETRICW lptm);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct TEXTMETRICW
    {
        public int tmHeight, tmAscent, tmDescent, tmInternalLeading, tmExternalLeading;
        public int tmAveCharWidth, tmMaxCharWidth, tmWeight, tmOverhang;
        public int tmDigitizedAspectX, tmDigitizedAspectY;
        public char tmFirstChar, tmLastChar, tmDefaultChar, tmBreakChar;
        public byte tmItalic, tmUnderlined, tmStruckOut, tmPitchAndFamily, tmCharSet;
    }
    [DllImport("user32.dll")] public static extern bool RedrawWindow(nint hWnd, nint lprc, nint hrgn, uint flags);
    public const uint RDW_INVALIDATE = 0x0001, RDW_ERASE = 0x0004, RDW_ALLCHILDREN = 0x0080, RDW_UPDATENOW = 0x0100;

    public const uint IMAGE_ICON = 1;
    public const uint LR_DEFAULTCOLOR = 0x0000, LR_SHARED = 0x8000;
    public const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    public const nint IDI_APPLICATION_RES = 32512;   // .NET SDK 가 앱 아이콘을 넣는 리소스 ID

    [DllImport("user32.dll")] public static extern bool OpenClipboard(nint hWndNewOwner);
    [DllImport("user32.dll")] public static extern bool CloseClipboard();
    [DllImport("user32.dll")] public static extern bool EmptyClipboard();
    [DllImport("user32.dll")] public static extern nint GetClipboardData(uint uFormat);
    [DllImport("user32.dll")] public static extern nint SetClipboardData(uint uFormat, nint hMem);
    [DllImport("user32.dll")] public static extern bool IsClipboardFormatAvailable(uint format);
    public const uint CF_UNICODETEXT = 13;

    // ---------- uxtheme ----------
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    public static extern int SetWindowTheme(nint hwnd, char* pszSubAppName, char* pszSubIdList);

    // ---------- gdi32 ----------
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    public static extern nint CreateFontIndirectW(ref LOGFONTW lplf);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(nint ho);
    [DllImport("gdi32.dll")] public static extern nint GetStockObject(int i);
    [DllImport("user32.dll")] public static extern int FillRect(nint hDC, ref RECT lprc, nint hbr);
    [DllImport("user32.dll")] public static extern int FrameRect(nint hDC, ref RECT lprc, nint hbr);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int PrivateExtractIconsW(char* szFileName, int nIconIndex, int cx, int cy, nint* phicon, uint* piconid, uint nIcons, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern nint LoadLibraryW(char* name);
    [DllImport("kernel32.dll")] public static extern bool FreeLibrary(nint hModule);
    [DllImport("kernel32.dll", ExactSpelling = true)] public static extern nint GetProcAddress(nint hModule, nint lpProcNameOrOrdinal);
    public const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    [DllImport("gdi32.dll")] public static extern int SetBkMode(nint hdc, int mode);
    [DllImport("gdi32.dll")] public static extern uint SetBkColor(nint hdc, uint color);
    [DllImport("gdi32.dll")] public static extern uint SetTextColor(nint hdc, uint color);
    [DllImport("gdi32.dll")] public static extern nint CreateSolidBrush(uint color);
    public const int TRANSPARENT = 1, OPAQUE = 2;

    [DllImport("user32.dll")] public static extern uint GetSysColor(int nIndex);
    [DllImport("user32.dll")] public static extern nint GetSysColorBrush(int nIndex);
    public const int COLOR_WINDOW = 5, COLOR_BTNFACE = 15, COLOR_WINDOWTEXT = 8, COLOR_BTNTEXT = 18;

    // ---------- dwmapi (Windows 11 외형) ----------
    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(nint hwnd, uint attr, void* pvAttribute, uint cbAttribute);
    public const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWCP_ROUND = 2, DWMWCP_ROUNDSMALL = 3;
    public static readonly nint HWND_TOPMOST = -1;
    public const uint SWP_SHOWWINDOW = 0x0040;

    // ---------- kernel32 ----------
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandleW(char* lpModuleName);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] public static extern nint GetCurrentProcess();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint CreateMutexW(nint lpAttr, bool bInitialOwner, char* lpName);
    [DllImport("kernel32.dll")] public static extern nint GlobalAlloc(uint uFlags, nuint dwBytes);
    [DllImport("kernel32.dll")] public static extern nint GlobalLock(nint hMem);
    [DllImport("kernel32.dll")] public static extern bool GlobalUnlock(nint hMem);
    [DllImport("kernel32.dll")] public static extern nuint GlobalSize(nint hMem);
    [DllImport("kernel32.dll")] public static extern nint GlobalFree(nint hMem);
    public const uint GMEM_MOVEABLE = 0x0002;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern bool QueryFullProcessImageNameW(nint hProcess, uint dwFlags, char* lpExeName, ref uint lpdwSize);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(nint hObject);
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint PROCESS_TERMINATE = 0x0001, SYNCHRONIZE = 0x00100000;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool TerminateProcess(nint hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(nint hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetExitCodeProcess(nint hProcess, out uint lpExitCode);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint SendMessageTimeoutW(nint hWnd, uint msg, nint wParam, nint lParam,
        uint flags, uint timeoutMs, out nint result);

    public const uint SMTO_ABORTIFHUNG = 0x0002;

    /// <summary>프로세스 핸들로 실행 파일 전체 경로를 얻는다. 실패하면 빈 문자열.</summary>
    public static string GetProcessImagePath(nint hProcess)
    {
        Span<char> buf = stackalloc char[520];
        uint size = (uint)buf.Length;
        fixed (char* p = buf)
            return QueryFullProcessImageNameW(hProcess, 0, p, ref size) && size > 0
                ? new string(p, 0, (int)size) : string.Empty;
    }

    [DllImport("kernel32.dll")] public static extern nint LocalFree(nint hMem);

    // ---------- advapi32 ----------
    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool OpenProcessToken(nint ProcessHandle, uint DesiredAccess, out nint TokenHandle);
    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool GetTokenInformation(nint TokenHandle, int TokenInformationClass,
        void* TokenInformation, uint TokenInformationLength, out uint ReturnLength);
    public const uint TOKEN_QUERY = 0x0008;
    public const int TokenElevation = 20;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int RegCreateKeyExW(nint hKey, char* lpSubKey, uint Reserved, char* lpClass,
        uint dwOptions, uint samDesired, nint lpSecurityAttributes, out nint phkResult, out uint lpdwDisposition);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int RegOpenKeyExW(nint hKey, char* lpSubKey, uint ulOptions, uint samDesired, out nint phkResult);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int RegSetValueExW(nint hKey, char* lpValueName, uint Reserved, uint dwType, void* lpData, uint cbData);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int RegQueryValueExW(nint hKey, char* lpValueName, nint lpReserved, nint lpType, void* lpData, ref uint lpcbData);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int RegDeleteValueW(nint hKey, char* lpValueName);
    [DllImport("advapi32.dll")] public static extern int RegCloseKey(nint hKey);

    public static readonly nint HKEY_CURRENT_USER = unchecked((nint)0x80000001);
    public const uint KEY_READ = 0x20019, KEY_WRITE = 0x20006;
    public const uint REG_SZ = 1;

    // ---------- crypt32 (DPAPI) ----------
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CryptProtectData(ref DATA_BLOB pDataIn, char* szDataDescr, ref DATA_BLOB pOptionalEntropy,
        nint pvReserved, nint pPromptStruct, uint dwFlags, out DATA_BLOB pDataOut);
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, nint ppszDataDescr, ref DATA_BLOB pOptionalEntropy,
        nint pvReserved, nint pPromptStruct, uint dwFlags, out DATA_BLOB pDataOut);
    public const uint CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    // ---------- shell32 ----------
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool ShellExecuteExW(ref SHELLEXECUTEINFOW lpExecInfo);

    // ---------- comctl32 ----------
    /// <summary>창 서브클래스(comctl32 v6). 표준 EDIT 에 앱 스크롤 막대를 붙일 때 쓴다(<see cref="ThinScroll"/>).</summary>
    [DllImport("comctl32.dll")] public static extern bool SetWindowSubclass(nint hWnd, delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nuint, nuint, nint> pfnSubclass, nuint uIdSubclass, nuint dwRefData);
    [DllImport("comctl32.dll")] public static extern bool RemoveWindowSubclass(nint hWnd, delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nuint, nuint, nint> pfnSubclass, nuint uIdSubclass);
    [DllImport("comctl32.dll")] public static extern nint DefSubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam);
    public const uint EM_SCROLL = 0x00B5, EM_LINESCROLL = 0x00B6, EM_GETLINECOUNT = 0x00BA, EM_GETFIRSTVISIBLELINE = 0x00CE, EM_SETMARGINS = 0x00D3;
    public const int EC_RIGHTMARGIN = 2, SB_PAGEUP = 2, SB_PAGEDOWN = 3;
    public const uint WM_SETCURSOR = 0x0020;
    [DllImport("user32.dll")] public static extern nint SetCursor(nint hCursor);
    [DllImport("user32.dll")] public static extern bool ScreenToClient(nint hWnd, ref POINT lpPoint);
    [DllImport("user32.dll")] public static extern nint GetDlgItem(nint hDlg, int id);
    [DllImport("user32.dll")] public static extern int MapWindowPoints(nint hWndFrom, nint hWndTo, ref POINT lpPoints, uint cPoints);
    [DllImport("gdi32.dll")] public static extern nint CreatePatternBrush(nint hbm);
    [DllImport("gdi32.dll")] public static extern bool SetBrushOrgEx(nint hdc, int x, int y, nint lppt);
    /// <summary>Windows 표시 언어(LANGID). 다국어의 "Windows 설정 따름"에 쓴다.</summary>
    [DllImport("kernel32.dll")] public static extern ushort GetUserDefaultUILanguage();
    /// <summary>schtasks 의 출력(콘솔 OEM 코드 페이지 바이트)을 글자로. CP_OEMCP = 1.</summary>
    [DllImport("kernel32.dll")] public static extern int MultiByteToWideChar(uint codePage, uint flags, byte* mb, int mbLen, char* wide, int wideLen);
    [DllImport("comctl32.dll")]
    public static extern bool InitCommonControlsEx(ref INITCOMMONCONTROLSEX picce);

    // ---------- 편의 함수 ----------
    public static string GetWindowText(nint h)
    {
        Span<char> buf = stackalloc char[512];
        fixed (char* p = buf) { int n = GetWindowTextW(h, p, buf.Length); return n > 0 ? new string(p, 0, n) : string.Empty; }
    }

    public static string GetClassName(nint h)
    {
        Span<char> buf = stackalloc char[256];
        fixed (char* p = buf) { int n = GetClassNameW(h, p, buf.Length); return n > 0 ? new string(p, 0, n) : string.Empty; }
    }

    public static void SetText(nint h, string s)
    {
        fixed (char* p = s) SetWindowTextW(h, p);
    }

    [DllImport("user32.dll")] public static extern nint GetWindowLongPtrW(nint hWnd, int nIndex);
    public const int GWL_STYLE = -16;

    /// <summary>글꼴 한 줄의 높이(물리 px). 단일 라인 EDIT 을 이 높이로 만들면 글자가 상하 여백 없이 딱 맞는다.</summary>
    public static int TextHeight(nint hwnd, nint font)
    {
        nint dc = GetDC(hwnd);
        if (dc == 0) return 15;
        try
        {
            nint old = SelectObject(dc, font);
            GetTextMetricsW(dc, out TEXTMETRICW tm);
            SelectObject(dc, old);
            return tm.tmHeight > 0 ? tm.tmHeight : 15;
        }
        finally { ReleaseDC(hwnd, dc); }
    }

    public static short LoWord(nint v) => (short)(v & 0xFFFF);
    public static short HiWord(nint v) => (short)((v >> 16) & 0xFFFF);

    public static int MsgBox(nint owner, string text, string caption, uint type)
    {
        fixed (char* t = text) fixed (char* c = caption) return MessageBoxW(owner, t, c, type);
    }

    public const uint MB_OK = 0, MB_OKCANCEL = 1, MB_YESNOCANCEL = 3, MB_YESNO = 4;
    public const uint MB_SETFOREGROUND = 0x00010000;
    public const uint MB_ICONERROR = 0x10, MB_ICONQUESTION = 0x20, MB_ICONWARNING = 0x30, MB_ICONINFORMATION = 0x40;
    public const int IDOK = 1, IDCANCEL = 2, IDYES = 6, IDNO = 7;
    public const uint MB_DEFBUTTON2 = 0x00000100;

    /// <summary>현재 프로세스가 관리자 권한(승격)으로 실행 중인지.</summary>
    public static bool IsElevated()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out nint token)) return false;
        try
        {
            uint elevated = 0;
            if (GetTokenInformation(token, TokenElevation, &elevated, sizeof(uint), out _))
                return elevated != 0;
            return false;
        }
        finally { CloseHandle(token); }
    }

    // ---------- v0.2 UI 기반: 커스텀 컨트롤 / 그리기 / 강조색 ----------
    public const uint WM_PAINT = 0x000F, WM_ENABLE = 0x000A, WM_SETTEXT = 0x000C, WM_GETFONT = 0x0031, WM_PRINTCLIENT = 0x0318;
    public const uint EM_GETSEL = 0x00B0, EM_SETSEL = 0x00B1;
    public const uint WM_SETFOCUS = 0x0007, WM_KILLFOCUS = 0x0008, WM_GETDLGCODE = 0x0087;
    public const uint WM_MOUSEMOVE = 0x0200, WM_MOUSELEAVE = 0x02A3, WM_CAPTURECHANGED = 0x0215;
    public const uint DLGC_BUTTON = 0x2000, DLGC_WANTARROWS = 0x0001;
    public const int VK_SPACE = 0x20;
    public const int GWLP_USERDATA = -21;

    [StructLayout(LayoutKind.Sequential)]
    public struct PAINTSTRUCT
    {
        public nint hdc; public int fErase; public RECT rcPaint;
        public int fRestore, fIncUpdate;
        public fixed byte rgbReserved[32];
    }
    [DllImport("user32.dll")] public static extern nint BeginPaint(nint hWnd, out PAINTSTRUCT lpPaint);
    [DllImport("user32.dll")] public static extern bool EndPaint(nint hWnd, ref PAINTSTRUCT lpPaint);
    [DllImport("user32.dll")] public static extern nint SetCapture(nint hWnd);
    [DllImport("user32.dll")] public static extern bool ReleaseCapture();
    [DllImport("user32.dll")] public static extern nint GetCapture();
    [DllImport("user32.dll")] public static extern nint GetFocus();
    [DllImport("user32.dll")] public static extern nint GetParent(nint hWnd);
    [DllImport("user32.dll")] public static extern int GetDlgCtrlID(nint hWnd);
    [DllImport("user32.dll")] public static extern nint SetWindowLongPtrW(nint hWnd, int nIndex, nint dwNewLong);

    [StructLayout(LayoutKind.Sequential)]
    public struct TRACKMOUSEEVENT { public uint cbSize, dwFlags; public nint hwndTrack; public uint dwHoverTime; }
    public const uint TME_LEAVE = 0x00000002;
    [DllImport("user32.dll")] public static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT lpEventTrack);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int DrawTextW(nint hdc, char* lpchText, int cchText, ref RECT lprc, uint format);
    public const uint DT_LEFT = 0x0, DT_CENTER = 0x1, DT_RIGHT = 0x2, DT_VCENTER = 0x4, DT_SINGLELINE = 0x20,
                      DT_NOPREFIX = 0x800, DT_END_ELLIPSIS = 0x8000;

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    public static extern nint CreateFontW(int cHeight, int cWidth, int cEscapement, int cOrientation, int cWeight,
        uint bItalic, uint bUnderline, uint bStrikeOut, uint iCharSet, uint iOutPrecision, uint iClipPrecision,
        uint iQuality, uint iPitchAndFamily, char* pszFaceName);
    public const int FW_NORMAL = 400, FW_SEMIBOLD = 600, FW_BOLD = 700;
    public const uint DEFAULT_CHARSET = 1, CLEARTYPE_QUALITY = 5;

    [DllImport("gdi32.dll")] public static extern nint CreateCompatibleDC(nint hdc);
    [DllImport("gdi32.dll")] public static extern nint CreateCompatibleBitmap(nint hdc, int cx, int cy);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(nint hdc);
    [DllImport("gdi32.dll")] public static extern bool BitBlt(nint hdc, int x, int y, int cx, int cy, nint hdcSrc, int x1, int y1, uint rop);
    public const uint SRCCOPY = 0x00CC0020;

    [DllImport("dwmapi.dll")] public static extern int DwmGetColorizationColor(out uint pcrColorization, out bool pfOpaqueBlend);

    public static nint MakeFont(string face, int px, int weight)
    {
        fixed (char* f = face)
            return CreateFontW(-px, 0, 0, 0, weight, 0, 0, 0, DEFAULT_CHARSET, 0, 0, CLEARTYPE_QUALITY, 0, f);
    }

    public static void DrawText(nint hdc, string s, ref RECT rc, uint fmt)
    {
        fixed (char* p = s) DrawTextW(hdc, p, s.Length, ref rc, fmt);
    }

    // ---------- v0.2 화면: 키 입력, 기본 버튼, 힌트 문구 ----------
    public const uint WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105, WM_SYSCHAR = 0x0106, WM_NCDESTROY = 0x0082;
    public const uint DLGC_WANTCHARS = 0x0080, DLGC_WANTALLKEYS = 0x0004, DLGC_DEFPUSHBUTTON = 0x0010, DLGC_UNDEFPUSHBUTTON = 0x0020;
    public const uint DM_GETDEFID = 0x0400, DM_SETDEFID = 0x0401;
    public const uint DC_HASDEFID = 0x534B;
    public const uint BM_CLICK = 0x00F5;
    public const uint EM_SETCUEBANNER = 0x1501;
    public const int VK_BACK = 0x08, VK_TAB = 0x09, VK_RETURN = 0x0D, VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12,
                     VK_ESCAPE = 0x1B, VK_DELETE = 0x2E, VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    public const int SW_SHOW = 5, SW_MINIMIZE = 6;
    [DllImport("user32.dll")] public static extern bool ClientToScreen(nint hWnd, ref POINT lpPoint);
    public const uint TPM_LEFTALIGN = 0x0000, TPM_TOPALIGN = 0x0000, MF_CHECKED = 0x0008;

    // ---------- 툴팁 (comctl32) ----------
    public const uint TTS_ALWAYSTIP = 0x01, TTS_NOPREFIX = 0x02;
    public const uint TTM_ADDTOOLW = 0x0432, TTM_DELTOOLW = 0x0433, TTM_SETMAXTIPWIDTH = 0x0418;
    public const uint TTF_IDISHWND = 0x0001, TTF_SUBCLASS = 0x0010;
    [StructLayout(LayoutKind.Sequential)]
    public struct TOOLINFOW
    {
        public uint cbSize, uFlags; public nint hwnd; public nuint uId; public RECT rect; public nint hinst; public char* lpszText; public nint lParam; public nint lpReserved;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    public static extern nint SendMessageTI(nint hWnd, uint msg, nint wParam, ref TOOLINFOW lParam);
    public const uint WS_DISABLED = 0x08000000;
    public const uint SWP_NOMOVE_ = 0x0002, SWP_NOSIZE_ = 0x0001, SWP_FRAMECHANGED = 0x0020;

    [DllImport("user32.dll")] public static extern bool DrawIconEx(nint hdc, int x, int y, nint hIcon, int cx, int cy, uint istep, nint hbr, uint flags);
    public const uint DI_NORMAL = 0x0003;

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE { public int cx, cy; }
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetTextExtentPoint32W(nint hdc, char* lpString, int c, out SIZE psizl);

    public static int TextWidth(nint hdc, string s)
    {
        fixed (char* p = s) return GetTextExtentPoint32W(hdc, p, s.Length, out SIZE sz) ? sz.cx : 0;
    }

    public static void SetCueBanner(nint edit, string text)
    {
        fixed (char* p = text) SendMessageStr(edit, EM_SETCUEBANNER, 1, p);
    }
}
