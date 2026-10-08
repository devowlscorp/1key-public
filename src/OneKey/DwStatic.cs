using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// DirectWrite 2b: STATIC 글자(설명문·머리·상자 본문·알림·칩 글자)도 DirectWrite 로 그린다. STATIC 을 서브클래스해서 WM_PAINT·WM_PRINTCLIENT 만
/// 맡고, 배경 붓·글자색은 지금처럼 부모의 WM_CTLCOLORSTATIC 에서 받는다(붓 원점 맞춤 포함). Dw 가 꺼져 있거나(시작 실패·실행 중 GDI 전환)
/// 맡을 수 없는 STATIC(그림·아이콘 종류, &amp; 단축키 표시를 쓰는 글)은 원래 STATIC 이 GDI 로 그린다. 한 번의 DirectWrite 그리기가 실패하면 그
/// 글자는 같은 자리에 GDI 로 바로 그린다(빈 글자 없음).
/// 글자 크기는 화면을 만들 때 <see cref="Dw.Measure"/> 와 GDI 중 큰 쪽으로 재므로, 실행 중 GDI 로 바뀌어도 잘리지 않는다(Codex V2-3).
/// </summary>
internal static unsafe class DwStatic
{
    private const nuint SubId = 0x1D57;
    private const uint SS_TYPEMASK = 0x1F, SS_CENTER = 0x1, SS_RIGHT = 0x2, SS_SIMPLE = 0xB, SS_LEFTNOWORDWRAP = 0xC, SS_NOPREFIX = 0x80,
                       SS_CENTERIMAGE = 0x200, SS_ELLIPSISMASK = 0xC000, SS_ENDELLIPSIS = 0x4000, SS_PATHELLIPSIS = 0x8000;

    /// <summary>STATIC 하나를 맡는다(여러 번 불러도 하나).</summary>
    public static void Attach(nint h)
    {
        if (h != 0) Native.SetWindowSubclass(h, &Proc, SubId, 0);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint Proc(nint h, uint msg, nint w, nint l, nuint id, nuint data)
    {
        try
        {
            switch (msg)
            {
                case Native.WM_PAINT:
                    if (!Eligible(h, out string text, out uint style)) break;
                    {
                        nint dc = Native.BeginPaint(h, out Native.PAINTSTRUCT ps);
                        try { Paint(h, dc, text, style); }
                        finally { Native.EndPaint(h, ref ps); }
                        return 0;
                    }
                case Native.WM_PRINTCLIENT:
                    if (!Eligible(h, out string t2, out uint s2)) break;
                    Paint(h, w, t2, s2);
                    return 0;
                case Native.WM_SETTEXT:
                case Native.WM_ENABLE:
                {
                    nint r = Native.DefSubclassProc(h, msg, w, l);
                    if (Dw.Enabled) Native.InvalidateRect(h, 0, true);   // 원래 STATIC 이 GDI 로 바로 그렸을 수 있다
                    return r;
                }
                case Native.WM_NCDESTROY:
                    Native.RemoveWindowSubclass(h, &Proc, SubId);
                    break;
            }
        }
        catch { }
        return Native.DefSubclassProc(h, msg, w, l);
    }

    private static bool Eligible(nint h, out string text, out uint style)
    {
        text = ""; style = 0;
        if (!Dw.Enabled) return false;
        style = (uint)Native.GetWindowLongPtrW(h, Native.GWL_STYLE);
        uint type = style & SS_TYPEMASK;
        if (type is not (0 or SS_CENTER or SS_RIGHT or SS_SIMPLE or SS_LEFTNOWORDWRAP)) return false;   // 그림·아이콘·테두리 종류
        text = Native.GetWindowText(h);
        if ((style & SS_NOPREFIX) == 0 && text.Contains('&')) return false;   // 단축키 밑줄 표시는 원래 STATIC 이
        return true;
    }

    /// <summary>원래 STATIC 과 같은 순서: 부모가 준 붓으로 바탕을 칠하고 글자를 그린다. DirectWrite 가 실패한 글자는 GDI 로.</summary>
    private static void Paint(nint h, nint dc, string text, uint style)
    {
        Native.GetClientRect(h, out Native.RECT rc);
        nint parent = Native.GetParent(h);
        int saved = SaveDC(dc);
        try
        {
            nint br = parent != 0 ? Native.SendMessageW(parent, Native.WM_CTLCOLORSTATIC, dc, h) : 0;
            if (br == 0 && parent != 0) br = Native.DefWindowProcW(parent, Native.WM_CTLCOLORSTATIC, dc, h);
            if (br != 0) Native.FillRect(dc, ref rc, br);
            uint color = Native.IsWindowEnabled(h) ? GetTextColor(dc) : Theme.DisabledText;
            nint font = Native.SendMessageW(h, Native.WM_GETFONT, 0, 0);
            if (text.Length == 0) return;
            uint dt = Native.DT_NOPREFIX;
            uint type = style & SS_TYPEMASK;
            if (type == SS_CENTER) dt |= Native.DT_CENTER; else if (type == SS_RIGHT) dt |= Native.DT_RIGHT;
            uint ell = style & SS_ELLIPSISMASK;
            bool single = (style & SS_CENTERIMAGE) != 0 || type is SS_SIMPLE or SS_LEFTNOWORDWRAP || ell != 0;
            if ((style & SS_CENTERIMAGE) != 0) dt |= Native.DT_VCENTER;
            dt |= single ? Native.DT_SINGLELINE : Native.DT_WORDBREAK;
            dt |= ell == SS_ENDELLIPSIS ? Native.DT_END_ELLIPSIS : ell == SS_PATHELLIPSIS ? Dw.DT_PATH_ELLIPSIS : ell != 0 ? Dw.DT_WORD_ELLIPSIS : 0;
            if (Dw.Text(dc, font, text, color, rc.left, rc.top, rc.right, rc.bottom, dt)) return;
            // 이 한 번은 GDI 로(같은 자리, 같은 규칙)
            nint old = Native.SelectObject(dc, font);
            Native.SetBkMode(dc, Native.TRANSPARENT);
            Native.SetTextColor(dc, color);
            Native.DrawText(dc, text, ref rc, dt);
            Native.SelectObject(dc, old);
        }
        finally { RestoreDC(dc, saved); }
    }

    [DllImport("gdi32.dll")] private static extern uint GetTextColor(nint dc);
    [DllImport("gdi32.dll")] private static extern int SaveDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool RestoreDC(nint dc, int saved);
}
