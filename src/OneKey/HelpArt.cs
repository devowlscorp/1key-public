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
///   stages:2                 고양이 크기 단계 빵 여섯 개(지금 단계는 크게, 아직 안 된 단계는 흐린 실루엣) — 고양이 수첩(0.5.20)
///   album:wave=1|groom=0|…   고양이 앨범 칸(본 장면 = 그림 + 이름, 못 본 장면 = 흐린 실루엣 + "?") — 고양이 수첩
/// 창 글자는 화면 읽기용으로 읽기 쉬운 형태(키는 " + ", 나머지는 ", ")로 둔다.
/// </summary>
internal static unsafe class HelpArt
{
    public const string ClassName = "OneKeyHelpArt";
    public const int Height = 36;
    private const int AlbumCell = 84, AlbumCap = 18, AlbumGap = 8, AlbumCols = 4, StagesH = 48;

    /// <summary>그림 줄의 높이(논리 px): 앨범은 칸 줄 수만큼, 단계 줄은 48, 나머지는 36.</summary>
    public static int HeightFor(string spec)
    {
        string kind = spec.Split(':', 2)[0];
        if (kind == "album")
        {
            int n = spec.Contains(':') ? spec.Split(':', 2)[1].Split('|').Length : 0, rows = (n + AlbumCols - 1) / AlbumCols;
            return rows * (AlbumCell + AlbumCap) + Math.Max(0, rows - 1) * AlbumGap;
        }
        return kind == "stages" ? StagesH : Height;
    }

    private static readonly Dictionary<string, nint> _images = new();
    private static nint Image(string name)
    {
        if (_images.TryGetValue(name, out nint img)) return img;
        Gdiplus.Init();
        return _images[name] = CatWidget.LoadPng(name);   // 작은 그림 18장 — 프로세스가 끝날 때까지 둔다
    }
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
            "stages" => "",
            "album" => string.Join(", ", items.Where(s => s.EndsWith("=1")).Select(s => CatGrowth.AlbumName(s.Split('=')[0]))),
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
                case "stages":
                {
                    int cur = items.Length > 0 && int.TryParse(items[0], out int c) ? c : 0;
                    int big = S(40), small = S(28), gap = S(10), x = (w - (big + 5 * small + 5 * gap)) / 2;
                    for (int i = 0; i < 6; i++)
                    {
                        int sz = i == cur ? big : small, y = (h - sz) / 2;
                        nint img = Image($"album_stage{i}.png");
                        if (i <= cur) Gdiplus.DrawImageSmooth(dc, img, x, y, sz, sz);
                        else Gdiplus.DrawImageTinted(dc, img, x, y, sz, sz, Theme.SecondaryText, 0.30f);   // 아직 안 된 단계
                        x += sz + gap;
                    }
                    return;
                }
                case "album":
                {
                    int cell = S(AlbumCell), cap = S(AlbumCap), gap = S(AlbumGap);
                    int x0 = (w - (AlbumCols * cell + (AlbumCols - 1) * gap)) / 2;
                    for (int i = 0; i < items.Length; i++)
                    {
                        string[] kv = items[i].Split('=');
                        bool seen = kv.Length > 1 && kv[1] == "1";
                        int cx = x0 + (i % AlbumCols) * (cell + gap), cy = (i / AlbumCols) * (cell + cap + gap), pad = S(4);
                        Gdiplus.FillRoundRect(dc, cx, cy, cell, cell, S(10), Theme.Mix(Theme.CardBg, Theme.ControlText, Theme.IsDark ? 0.07 : 0.045));
                        nint img = Image($"album_{kv[0]}.png");
                        if (seen) Gdiplus.DrawImageSmooth(dc, img, cx + pad, cy + pad, cell - 2 * pad, cell - 2 * pad);
                        else
                        {
                            Gdiplus.DrawImageTinted(dc, img, cx + pad, cy + pad, cell - 2 * pad, cell - 2 * pad, Theme.SecondaryText, 0.22f);   // 실루엣
                            Ctl.Text(dc, Theme.FontStrong, "?", Theme.SecondaryText, cx, cy, cx + cell, cy + cell, Native.DT_CENTER | Native.DT_VCENTER);
                        }
                        Ctl.Text(dc, Theme.FontSmall, seen ? CatGrowth.AlbumName(kv[0]) : "???", seen ? Theme.ControlText : Theme.SecondaryText, cx - gap / 2, cy + cell, cx + cell + gap / 2, cy + cell + cap, Native.DT_CENTER | Native.DT_VCENTER);
                    }
                    return;
                }
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
