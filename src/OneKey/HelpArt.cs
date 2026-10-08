using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// 도움말 안의 작은 화면 그림(2026-10-05 사용자: 그림을 넣을 곳은 그림으로). 스크린숏 대신 화면과 같은 색·모양으로 직접 그린다 —
/// 밝은/어두운 테마·언어가 바뀌어도 지금 화면과 맞는다. 사양 글(도움말 줄 "@..."):
///   keys:Ctrl|Alt|1          키 모양 + "+"
///   btns:입력|*저장          버튼(* = 강조 버튼, 나머지는 옅은 강조 채움)
///   seg:안함|*전송|…      나란한 선택 버튼(* = 고른 칸)
///   mark:warn / mark:tip     주의·도움말 줄의 기호
/// 창 글자는 화면 읽기용으로 읽기 쉬운 형태(키는 " + ", 나머지는 ", ")로 둔다.
/// </summary>
internal static unsafe class HelpArt
{
    public const string ClassName = "OneKeyHelpArt";
    public const int Height = 36;
    private static readonly Dictionary<nint, string> _spec = new();
    private static bool _registered;

    public static nint Create(nint parent, string spec, int x, int y, int w, int h)
    {
        if (!_registered) { Ctl.RegisterClass(Native.GetModuleHandleW(null), ClassName, &WndProc, 0); _registered = true; }
        string kind = spec.Split(':', 2)[0];
        string[] items = spec.Contains(':') ? spec.Split(':', 2)[1].Split('|') : Array.Empty<string>();
        string readable = kind switch
        {
            "keys" => string.Join(" + ", items),
            "mark" => "",
            _ => string.Join(", ", items.Select(s => s.TrimStart('*'))),
        };
        nint c;
        fixed (char* pc = ClassName) fixed (char* pt = readable)
            c = Native.CreateWindowExW(0, pc, pt, Native.WS_CHILD | Native.WS_VISIBLE, x, y, w, h, parent, 0, Native.GetModuleHandleW(null), 0);
        if (c != 0) { _spec[c] = spec; Native.SendMessageW(c, Native.WM_SETFONT, Theme.FontBody, 0); }
        return c;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            switch (msg)
            {
                case Native.WM_NCDESTROY: _spec.Remove(hwnd); break;
                case Native.WM_ERASEBKGND: return 1;
                case Native.WM_PAINT: Paint(hwnd); return 0;
                case 0x0084: return -1;   // WM_NCHITTEST: HTTRANSPARENT — 그림은 누르는 것이 아니다
            }
        }
        catch { }
        return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private static int TextW(nint dc, nint font, string s)
    {
        if (Dw.Width(font, s) is int w) return w;
        nint old = Native.SelectObject(dc, font);
        int g = Native.TextWidth(dc, s);
        Native.SelectObject(dc, old);
        return g;
    }

    private static void Paint(nint hwnd)
    {
        Ctl.Paint(hwnd, Theme.CardBrush, (dc, w, h) =>
        {
            int S(int v) => Ctl.S(hwnd, v);
            if (!_spec.TryGetValue(hwnd, out string? spec)) return;
            string kind = spec.Split(':', 2)[0];
            string[] items = spec.Contains(':') ? spec.Split(':', 2)[1].Split('|') : Array.Empty<string>();
            nint font = Theme.FontBody;
            switch (kind)
            {
                case "mark":
                {
                    bool warn = items.Length > 0 && items[0] == "warn";
                    Ctl.Text(dc, Theme.FontIconSmall, warn ? "\uE7BA" : "\uE82F", warn ? Theme.DangerText : Theme.AccentInk, 0, S(3), w, S(17), Native.DT_LEFT | Native.DT_VCENTER);
                    break;
                }
                case "keys":
                {
                    int x = 0, kh = S(26), ky = (h - kh) / 2, plus = TextW(dc, font, "+") + S(12);
                    for (int i = 0; i < items.Length; i++)
                    {
                        if (i > 0) { Ctl.Text(dc, font, "+", Theme.SecondaryText, x, 0, x + plus, h, Native.DT_CENTER | Native.DT_VCENTER); x += plus; }
                        int kw = Math.Max(kh, TextW(dc, font, items[i]) + S(16));
                        Gdiplus.FillRoundRect(dc, x, ky, kw, kh, S(5), Theme.EditBg);
                        Gdiplus.DrawRoundRect(dc, x, ky, kw, kh, S(5), Theme.FieldBorder, Math.Max(1, S(1)));
                        Gdiplus.DrawLine(dc, x + S(3), ky + kh - 1, x + kw - S(3), ky + kh - 1, Theme.FieldBorder, Math.Max(1, S(2)));   // 키 아래쪽 두께
                        Ctl.Text(dc, font, items[i], Theme.ControlText, x, ky, x + kw, ky + kh, Native.DT_CENTER | Native.DT_VCENTER);
                        x += kw;
                    }
                    break;
                }
                case "btns":
                {
                    int x = 0, bh = S(28), by = (h - bh) / 2;
                    foreach (string raw in items)
                    {
                        bool main = raw.StartsWith('*');
                        string t = raw.TrimStart('*');
                        int bw = TextW(dc, font, t) + S(28);
                        Gdiplus.FillRoundRect(dc, x, by, bw, bh, bh / 2f, main ? Theme.Accent : Theme.TintFill);
                        Ctl.Text(dc, font, t, main ? Theme.AccentText : Theme.AccentLabel, x, by, x + bw, by + bh, Native.DT_CENTER | Native.DT_VCENTER);
                        x += bw + S(8);
                    }
                    break;
                }
                case "seg":
                {
                    int sh = S(28), sy = (h - sh) / 2, r = S(Theme.FieldRadius), pad = S(20);
                    var ws = items.Select(s => TextW(dc, font, s.TrimStart('*')) + pad).ToArray();
                    int total = Math.Min(w, ws.Sum());
                    Gdiplus.FillRoundRect(dc, 0, sy, total, sh, r, Theme.EditBg);
                    int x = 0;
                    for (int i = 0; i < items.Length; i++)
                    {
                        bool sel = items[i].StartsWith('*');
                        int x1 = i == items.Length - 1 ? total : Math.Min(total, x + ws[i]);
                        if (sel)
                        {
                            int sv = Native.SaveDC(dc);
                            Native.IntersectClipRect(dc, x, sy, x1, sy + sh);
                            Gdiplus.FillRoundRect(dc, 0, sy, total, sh, r, Theme.Mix(Theme.EditBg, Theme.Accent, Theme.IsDark ? 0.35 : 0.16));
                            Native.RestoreDC(dc, sv);
                        }
                        else if (i > 0 && !items[i - 1].StartsWith('*')) Gdiplus.DrawLine(dc, x, sy + S(6), x, sy + sh - S(6), Theme.FieldBorder, Math.Max(1, S(1)));
                        Ctl.Text(dc, font, items[i].TrimStart('*'), sel ? Theme.AccentLabel : Theme.ControlText, x, sy, x1, sy + sh, Native.DT_CENTER | Native.DT_VCENTER | Native.DT_END_ELLIPSIS);
                        x = x1;
                    }
                    Gdiplus.DrawRoundRect(dc, 0, sy, total, sh, r, Theme.FieldBorder, Math.Max(1, S(1)));
                    break;
                }
            }
        });
    }
}
