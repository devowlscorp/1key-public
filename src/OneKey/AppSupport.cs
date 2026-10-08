using System.IO.Compression;

namespace OneKey;

/// <summary>
/// 후원 화면(2026-10-09 사용자: 사용성을 해치지 않고 거부감 없는 자리에). 설정 맨 아래 [후원하기 · 열기]에서만 들어온다 — 목록 머리줄·트레이 메뉴에
/// 단추를 두지 않고, 저절로 뜨거나 다시 알리지 않는다. 화면을 그리는 데 네트워크를 쓰지 않는다(QR 은 빌드에 든 그림).
/// 위에서부터: ① 고양이 + "1Key가 도움이 됐다면" + 한 문단 ② 휴대폰으로 후원 — 카카오페이 QR(흰 판, 어두운 테마에서도 흰 바탕),
/// 브라우저에서 열기 [열기] ③ 아래 [닫기]. 열 수 있는 주소는 SupportLinks.Allowed 뿐(이어 붙인 주소 없음), 여는 길은 바로 실행의 웹 주소와 같다
/// (기본 브라우저, Launcher — 관리자 권한 1Key 에서도 보통 권한으로). 시험 모드는 실제로 열지 않고 config 폴더 support-open.txt 에 적는다.
/// Esc·‹·[닫기] = 설정으로.
/// </summary>
internal sealed unsafe partial class App
{
    private const int IdSupBack = 2501, IdSupOpen = 2502, IdSupClose = 2503, IdRowSupport = 2504;
    private const int SupQrTile = 184;   // QR 흰 판(논리 px). 모듈은 늘 정수 픽셀(DPI 마다 다시 잼)

    private static bool[]? _qrMods;
    private static int _qrN;

    private void BuildSupport()
    {
        int metalTop = MetalHeader(IdSupBack, T.SupportTitle, help: false);
        bool dark = Theme.IsDark;
        uint ink = Metal.Ref(Metal.Ink(dark)), note = Metal.Ref(Metal.InkNote(dark));
        int lx = Mx + Row.PadX, rx = Mx + Mw - Row.PadX, y = metalTop + DialPadTop;

        // ① 고양이 + 인사
        const int PicH = 64;
        int tx = lx + PicH + 12, tw = rx - tx;
        int top = y;
        y += 14;
        nint hf = Theme.Sized(14.5, true), bf = Theme.Sized(12.5, false);
        int hh = Math.Max(20, TextH(hf, T.SupportHeading, tw));
        Label(T.SupportHeading, tx, y, tw, hh, hf, ink, true, Native.SS_LEFT, vcenter: false);
        y += hh + 4;
        int bh = TextH(bf, T.SupportBody, tw) + 2;
        Label(T.SupportBody, tx, y, tw, bh, bf, note, true, Native.SS_LEFT, vcenter: false);
        y += bh + 14;
        y = Math.Max(y, top + 14 + PicH + 14);
        _page.Pic = (lx, top + (y - top - PicH) / 2, PicH);
        _page.Cards.Add((Mx, top, Mw, y - top));

        // ② 휴대폰으로 후원
        y += MetalUi.TileGap + 4;
        top = y;
        y += 14;
        Label(T.SupportPhone, lx, y, rx - lx, 22, Theme.Sized(13.5, true), ink, true, Native.SS_LEFT | Native.SS_ENDELLIPSIS);
        y += 24;
        int nh = TextH(Theme.Sized(12, false), T.SupportPhoneNote, rx - lx) + 2;
        Label(T.SupportPhoneNote, lx, y, rx - lx, nh, Theme.Sized(12, false), note, true, Native.SS_LEFT, vcenter: false);
        y += nh + 12;
        int qx = Mx + (Mw - SupQrTile) / 2;
        _page.Qr = (qx, y, SupQrTile);
        y += SupQrTile + 8;
        Label(T.SupportKakao, lx, y, rx - lx, 20, Theme.Sized(13, true), ink, true, Native.SS_CENTER);
        y += 20;
        Label(T.SupportKakaoNote, lx, y, rx - lx, 18, Theme.Sized(11.5, false), note, true, Native.SS_CENTER);
        y += 18 + 12;
        _page.Separators.Add((lx, y, rx - lx));
        y += 1;
        // 브라우저에서 열기 — 카카오페이 송금 페이지  [열기]
        int ow = LabelW(Theme.Sized(12, true), T.SetBtnOpen) + 28;
        int rowH = MetalUi.SetRowH + 12, textW = rx - lx - ow - 12;
        Label(T.SupportBrowser, lx, y + 8, textW, 20, Theme.Sized(13.5, true), ink, true, Native.SS_LEFT | Native.SS_ENDELLIPSIS);
        Label(T.SupportBrowserNote, lx, y + 28, textW, 18, Theme.Sized(11.5, false), note, true, Native.SS_LEFT | Native.SS_ENDELLIPSIS);
        nint open = SmallPill(IdSupOpen, T.SetBtnOpen, rx - ow, y + 6, ow);
        if (open != 0) { CtlAcc.SetName(open, T.SupportBrowser + " — " + T.SupportBrowserNote); Tip(open, T.SupportBrowserNote); }
        y += rowH;
        _page.Cards.Add((Mx, top, Mw, y - top));

        MetalDial(metalTop, ref y);
        _page.BarTop = y;
        y += 14;
        int closeW = BarPillW(T.CommonClose, 72);
        BarPill(IdSupClose, T.CommonClose, Btn.PillMain, WinW - 22 - closeW, y, closeW);
        _page.DefaultButton = IdSupOpen;
        _page.Height = y + MetalUi.PillMainH + 16;
    }

    /// <summary>허용 목록의 주소 하나를 연다(바로 실행의 웹 주소와 같은 길 — 기본 브라우저).</summary>
    private void OpenSupportLink(int which)
    {
        if (_cur != Screen.Support || which < 0 || which >= SupportLinks.Allowed.Length) return;
        string url = SupportLinks.Allowed[which];
        if (Program.IsTestMode)   // 시험: 브라우저를 띄우지 않고 무엇을 열려 했는지만 남긴다
        {
            try { File.AppendAllText(Path.Combine(Config.Dir, "support-open.txt"), url + Environment.NewLine); } catch { }
            Native.SetPropW(_hwnd, "OneKeyTestSupportOpen", ++_testSupportOpen);
            return;
        }
        var it = new LaunchItem { Id = "support", Kind = "url", Name = T.SupportKakao, Target = url, Browser = "default", Valid = true };
        if (Launcher.Unconsumed is int done and not 0) OnLaunchDone(done);
        if (Launcher.Start(it, 0, false, "", _hwnd, WM_LAUNCH_DONE) is null) LaunchToast(Launcher.Result.Busy, it.Name);
    }

    private int _testSupportOpen;

    /// <summary>QR 모듈(빌드에 든 qr_kakaopay.png — 회색조 8비트, 한 모듈 = 한 픽셀, 둘레 4 모듈 빈칸 포함). 실패하면 null.</summary>
    private static bool[]? QrModules(out int n)
    {
        n = _qrN;
        if (_qrMods is not null) return _qrMods;
        try
        {
            using var s = typeof(App).Assembly.GetManifestResourceStream("qr_kakaopay.png");
            if (s is null) return null;
            byte[] png = new byte[s.Length]; s.ReadExactly(png);
            int w = 0, h = 0, pos = 8;
            using var idat = new MemoryStream();
            while (pos + 8 <= png.Length)
            {
                int len = (png[pos] << 24) | (png[pos + 1] << 16) | (png[pos + 2] << 8) | png[pos + 3];
                string type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
                if (type == "IHDR") { w = (png[pos + 8] << 24) | (png[pos + 9] << 16) | (png[pos + 10] << 8) | png[pos + 11]; h = (png[pos + 12] << 24) | (png[pos + 13] << 16) | (png[pos + 14] << 8) | png[pos + 15]; if (png[pos + 16] != 8 || png[pos + 17] != 0) return null; }
                else if (type == "IDAT") idat.Write(png, pos + 8, len);
                pos += 12 + len;
            }
            if (w <= 0 || w != h || w > 200) return null;
            idat.Position = 0;
            using var z = new ZLibStream(idat, CompressionMode.Decompress);
            byte[] raw = new byte[(w + 1) * h]; z.ReadExactly(raw);
            var mods = new bool[w * h];
            for (int y = 0; y < h; y++)
            {
                if (raw[y * (w + 1)] != 0) return null;   // 만든 스크립트(make_qr.py)는 거르기 없이 쓴다
                for (int x = 0; x < w; x++) mods[y * w + x] = raw[y * (w + 1) + 1 + x] < 128;
            }
            _qrN = n = w; _qrMods = mods;
            return mods;
        }
        catch { return null; }
    }

    /// <summary>바탕 그림에 QR 판을 그린다: 흰 둥근 판(테마와 상관없이 흰색 — 휴대폰이 읽도록) + 정수 픽셀 모듈(가장자리 번짐 없음).</summary>
    private void DrawSupportQr(Metal.Surf s, int x, int y, int size, double k)
    {
        double px = x * k, py = Yp(y), ps = size * k;
        Metal.Shadow(s, px, py, ps, ps, 14 * k, 0, 1 * k, 3 * k, 0, 0x000000, Theme.IsDark ? 0.5 : 0.14);
        Metal.RRect(s, px, py, ps, ps, 14 * k, 0xFFFFFF);
        if (QrModules(out int n) is not bool[] m) return;
        int mod = Math.Max(1, (int)Math.Floor((ps - 8 * k) / n));   // 판 안쪽 4px 여백(빈칸 4 모듈은 그림에 들어 있다)
        int side = mod * n, ox = (int)Math.Round(px + (ps - side) / 2), oy = (int)Math.Round(py + (ps - side) / 2);
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                if (!m[j * n + i]) continue;
                for (int yy = 0; yy < mod; yy++)
                    for (int xx = 0; xx < mod; xx++) Metal.Put(s, ox + i * mod + xx, oy + j * mod + yy, 0x000000, 1);
            }
    }
}
