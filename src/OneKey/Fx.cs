using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>화면 효과용 Win32 선언 (모달 뒤 흐림, 토스트 페이드, 내장 글꼴). 0.2.45.</summary>
internal static unsafe class Fx
{
    public const uint WS_EX_LAYERED = 0x00080000, LWA_ALPHA = 0x2, SRCCOPY = 0x00CC0020;
    public const uint PW_CLIENTONLY = 0x1, PW_RENDERFULLCONTENT = 0x2;
    public const int RGN_OR = 2;
    public const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
    }

    [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(nint hwnd, uint crKey, byte bAlpha, uint dwFlags);
    [DllImport("user32.dll")] public static extern bool PrintWindow(nint hwnd, nint hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool UpdateWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("gdi32.dll")] public static extern nint AddFontMemResourceEx(void* pbFont, uint cbFont, nint pdv, out uint pcFonts);
    [DllImport("gdi32.dll")] public static extern nint CreateCompatibleDC(nint hdc);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(nint hdc);
    [DllImport("gdi32.dll")] public static extern nint CreateDIBSection(nint hdc, ref BITMAPINFOHEADER bmi, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] public static extern bool BitBlt(nint dst, int x, int y, int w, int h, nint src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] public static extern nint CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
    [DllImport("gdi32.dll")] public static extern int CombineRgn(nint dst, nint a, nint b, int mode);

    /// <summary>
    /// Windows 설정 › 접근성 › 시각 효과 › "애니메이션 효과" (SPI_GETCLIENTAREAANIMATION). 끄면 1Key 도 움직이는 효과
    /// (설정 화면 높이 변화, 토스트가 떠오르고 흐려지며 미끄러지는 것)를 쓰지 않는다 (Codex QA-06, 2026-09-30).
    /// 검증 모드에서는 ONEKEY_TEST_REDUCE_MOTION=1 로 끈 상태를 흉내 낸다.
    /// </summary>
    public static bool Animations
    {
        get
        {
            if (Program.IsTestMode && Environment.GetEnvironmentVariable("ONEKEY_TEST_REDUCE_MOTION") == "1") return false;
            int on = 1;
            return !Native.SystemParametersInfoW(0x1042, 0, &on, 0) || on != 0;
        }
    }

    /// <summary>
    /// Windows 설정 › 개인 설정 › 색 › "투명 효과". 끄면 모달 뒤 막은 흐리지 않고 어둡게만 한다 (Codex QA-06).
    /// 검증 모드에서는 ONEKEY_TEST_REDUCE_MOTION=1 이 이것도 끈다.
    /// </summary>
    public static bool Transparency
    {
        get
        {
            if (Program.IsTestMode && Environment.GetEnvironmentVariable("ONEKEY_TEST_REDUCE_MOTION") == "1") return false;
            int value = 1;
            fixed (char* sub = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")
            {
                if (Native.RegOpenKeyExW(Native.HKEY_CURRENT_USER, sub, 0, Native.KEY_READ, out nint key) == 0)
                {
                    uint data = 1, size = sizeof(uint);
                    fixed (char* name = "EnableTransparency")
                        if (Native.RegQueryValueExW(key, name, 0, 0, &data, ref size) == 0) value = (int)data;
                    Native.RegCloseKey(key);
                }
            }
            return value != 0;
        }
    }
}
