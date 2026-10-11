using System.IO.Compression;

namespace OneKey;

/// <summary>
/// 후원 화면(2026-10-09 사용자: 사용성을 해치지 않고 거부감 없는 자리에). 이 폴더(Extra/)를 빼고 빌드하면 화면도 빠진다
/// (App.cs 의 부분 메서드 네 개 — 후원 없는 판은 그렇게 만든다).
/// 설정 맨 아래 [후원하기 · 열기]에서만 들어온다 — 목록 머리줄·트레이 메뉴에
/// 단추를 두지 않고, 저절로 뜨거나 다시 알리지 않는다. 화면을 그리는 데 네트워크를 쓰지 않는다(QR 은 빌드에 든 그림).
/// 위에서부터: ① 고양이 + "1Key가 도움이 됐다면" + 한 문단 ② GitHub 에서 별 주기 [열기](모든 언어 — 돈이 들지 않는 응원을 먼저)
/// ③ 휴대폰으로 후원 — 데브아울스 후원 페이지 QR(흰 판, 어두운 테마에서도 흰 바탕), 브라우저에서 열기 [열기]. 2026-10-12(사용자):
/// 개인 카카오페이 대신 법인 계좌 후원 페이지 — 토스·카카오페이·은행 앱 고르기는 그 페이지가 한다(이 화면에는 두지 않는다)
/// ④ 필요한 걸 사실 때(한국어 화면만 — 한국 서비스) 쿠팡·마이리얼트립 [자세히] → 확인 상자(경제적 이해관계 문구를 강조 줄로, 가는 곳,
/// 1Key 는 아무것도 모으지 않음) [브라우저에서 열기]/[취소] ⑤ 아래 [닫기]. 제휴 링크는 상자를 거치지 않고는 열리지 않는다(문구를 먼저 보게).
/// 열 수 있는 주소는 SupportLinks.Allowed 뿐(이어 붙인 주소 없음), 여는 길은 바로 실행의 웹 주소와 같다
/// (기본 브라우저, Launcher — 관리자 권한 1Key 에서도 보통 권한으로). 시험 모드는 실제로 열지 않고 config 폴더 support-open.txt 에 적는다.
/// Esc·‹·[닫기] = 설정으로.
/// </summary>
internal sealed unsafe partial class App
{
    private const int IdSupBack = 2501, IdSupOpen = 2502, IdSupClose = 2503, IdRowSupport = 2504;
    private const int IdSupCoupang = 2505, IdSupMyRealTrip = 2506, IdSupGitHub = 2507;
    private const int SupQrTile = 184;   // QR 흰 판(논리 px). 모듈은 늘 정수 픽셀(DPI 마다 다시 잼)

    // 배포본 전용 추가 화면의 연결(App.cs 의 부분 메서드): 설정 맨 아래 [후원하기 · 열기], 화면 만들기, 명령, 바탕 그림의 QR
    private (int X, int Y, int S)? _supQr;

    partial void ExtraSettingsRow(int cx, int cw, ref int y)
    {
        // 후원(2026-10-09 사용자: 사용성을 해치지 않고 거부감 없는 자리): 설정 맨 아래 — 목록·트레이에는 두지 않고 저절로 열지 않는다.
        // 0.5.15-R(사용자: 너무 숨어 있다, 맨 앞에 두면 늘 보여 거슬리니 지금 자리에서 눈에 잘 띄게): 다른 줄(글 + 연한 작은 버튼)과 달리
        // 커피 잔 그림 + 두 줄 글 + 진한 [후원하기] 알약의 카드
        y += 10;   // 설정 카드 사이 간격(BuildSettingsBody 의 Gap)
        int top = y;
        const int H = 68, PicH = 44;
        bool dark = Theme.IsDark;
        uint ink = Metal.Ref(Metal.Ink(dark)), note = Metal.Ref(Metal.InkNote(dark));
        int lx = cx + Row.PadX, rx = cx + cw - Row.PadX;
        int bw = BarPillW(ST.SupportRowButton, 84);
        int tx = lx + PicH + 10, tw = rx - bw - 10 - tx;
        _page.Pic = (lx - 2, top + (H - PicH) / 2, PicH);
        // 고양이 대신 커피 잔(2026-10-09 사용자: 문맥에 맞게, 다크·라이트 모드에 맞춰): 밝은 모드 흰 잔, 어두운 모드 검은 잔(Extra/tools/make_coffee.py)
        _page.PicName = dark ? "coffee_dark.png" : "coffee_light.png";
        Label(ST.SupportRowTitle, tx, top + 13, tw, 22, Theme.Sized(13.5, true), ink, true, Native.SS_LEFT | Native.SS_ENDELLIPSIS);
        Label(ST.SupportRowNote, tx, top + 36, tw, 18, Theme.Sized(11.5, false), note, true, Native.SS_LEFT | Native.SS_ENDELLIPSIS);
        nint b = BarPill(IdRowSupport, ST.SupportRowButton, Btn.Prominent, rx - bw, top + (H - MetalUi.PillMainH) / 2, bw);
        if (b != 0) { CtlAcc.SetName(b, ST.SupportRowButton + " — " + ST.SupportRowTitle); Tip(b, ST.SupportRowNote); }
        _page.Cards.Add((cx, top, cw, H));
        y = top + H;
    }

    partial void ExtraBuild() { _supQr = null; BuildSupport(); }

    partial void ExtraCommand(int id, ref bool handled)
    {
        handled = true;
        switch (id)
        {
            case IdRowSupport: ShowScreen(Screen.Extra); return;
            case IdSupBack: case IdSupClose: ShowScreen(Screen.Settings); return;
            case IdSupOpen: OpenSupportLink(SupportLinks.IxDonate); return;
            case IdSupGitHub: OpenSupportLink(SupportLinks.IxGitHub); return;
            case IdSupCoupang: ShowAffiliate(SupportLinks.IxCoupang); return;
            case IdSupMyRealTrip: ShowAffiliate(SupportLinks.IxMyRealTrip); return;
            default: handled = false; return;
        }
    }

    partial void ExtraPaint(Metal.Surf body, double k)
    {
        if (_cur == Screen.Extra && _supQr is { } qr) DrawSupportQr(body, qr.X, qr.Y, qr.S, k);
    }

    private static bool[]? _qrMods;
    private static int _qrN;

    private void BuildSupport()
    {
        int metalTop = MetalHeader(IdSupBack, ST.SupportTitle, help: false);
        bool dark = Theme.IsDark;
        uint ink = Metal.Ref(Metal.Ink(dark)), note = Metal.Ref(Metal.InkNote(dark));
        int lx = Mx + Row.PadX, rx = Mx + Mw - Row.PadX, y = metalTop + DialPadTop;

        // ① 고양이 + 인사
        const int PicH = 64;
        int tx = lx + PicH + 12, tw = rx - tx;
        int top = y;
        y += 14;
        nint hf = Theme.Sized(14.5, true), bf = Theme.Sized(12.5, false);
        int hh = Math.Max(20, TextH(hf, ST.SupportHeading, tw));
        Label(ST.SupportHeading, tx, y, tw, hh, hf, ink, true, Native.SS_LEFT, vcenter: false);
        y += hh + 4;
        int bh = TextH(bf, ST.SupportBody, tw) + 2;
        Label(ST.SupportBody, tx, y, tw, bh, bf, note, true, Native.SS_LEFT, vcenter: false);
        y += bh + 14;
        y = Math.Max(y, top + 14 + PicH + 14);
        _page.Pic = (lx, top + (y - top - PicH) / 2, PicH);
        _page.Cards.Add((Mx, top, Mw, y - top));

        // 한 줄: 제목·설명 + 오른쪽 둥근 단추. 읽기 프로그램 이름 = "제목 — 설명"(무엇을 여는지 말한다)
        int PillRow(int id, string title, string sub, string btn, int at)
        {
            int ow = LabelW(Theme.Sized(12, true), btn) + 28, textW = rx - lx - ow - 12;
            Label(title, lx, at + 8, textW, 20, Theme.Sized(13.5, true), ink, true, Native.SS_LEFT | Native.SS_ENDELLIPSIS);
            Label(sub, lx, at + 28, textW, 18, Theme.Sized(11.5, false), note, true, Native.SS_LEFT | Native.SS_ENDELLIPSIS);
            nint b = SmallPill(id, btn, rx - ow, at + 6, ow);
            if (b != 0) { CtlAcc.SetName(b, title + " — " + sub); Tip(b, sub); }
            return at + MetalUi.SetRowH + 12;
        }

        // ② GitHub 에서 별 주기(모든 언어)
        y += MetalUi.TileGap + 4;
        top = y;
        y = PillRow(IdSupGitHub, ST.SupportGithub, ST.SupportGithubNote, T.SetBtnOpen, y + 4) + 4;
        _page.Cards.Add((Mx, top, Mw, y - top));

        // ③ 휴대폰으로 후원
        y += MetalUi.TileGap + 4;
        top = y;
        y += 14;
        Label(ST.SupportPhone, lx, y, rx - lx, 22, Theme.Sized(13.5, true), ink, true, Native.SS_LEFT | Native.SS_ENDELLIPSIS);
        y += 24;
        int nh = TextH(Theme.Sized(12, false), ST.SupportPhoneNote, rx - lx) + 2;
        Label(ST.SupportPhoneNote, lx, y, rx - lx, nh, Theme.Sized(12, false), note, true, Native.SS_LEFT, vcenter: false);
        y += nh + 12;
        int qx = Mx + (Mw - SupQrTile) / 2;
        _supQr = (qx, y, SupQrTile);
        y += SupQrTile + 8;
        Label(ST.SupportPage, lx, y, rx - lx, 20, Theme.Sized(13, true), ink, true, Native.SS_CENTER);
        y += 20;
        Label(ST.SupportPageNote, lx, y, rx - lx, 18, Theme.Sized(11.5, false), note, true, Native.SS_CENTER);
        y += 18 + 12;
        _page.Separators.Add((lx, y, rx - lx));
        y += 1;
        // 브라우저에서 열기 — 데브아울스 후원 페이지  [열기]
        y = PillRow(IdSupOpen, ST.SupportBrowser, ST.SupportBrowserNote, T.SetBtnOpen, y);
        _page.Cards.Add((Mx, top, Mw, y - top));

        // ④ 필요한 걸 사실 때(한국어 화면만): 제휴 링크 — [자세히]는 문구 상자를 먼저 띄운다
        if (L.Current == Lang.Ko)
        {
            y += MetalUi.TileGap + 4;
            top = y;
            y += 14;
            Label(ST.SupportShop, lx, y, rx - lx, 22, Theme.Sized(13.5, true), ink, true, Native.SS_LEFT | Native.SS_ENDELLIPSIS);
            y += 24;
            int sh = TextH(Theme.Sized(12, false), ST.SupportShopNote, rx - lx) + 2;
            Label(ST.SupportShopNote, lx, y, rx - lx, sh, Theme.Sized(12, false), note, true, Native.SS_LEFT, vcenter: false);
            y += sh + 8;
            _page.Separators.Add((lx, y, rx - lx));
            y += 1;
            y = PillRow(IdSupCoupang, ST.SupportCoupang, ST.SupportCoupangNote, ST.SupportBtnMore, y);
            _page.Separators.Add((lx, y, rx - lx));
            y += 1;
            y = PillRow(IdSupMyRealTrip, ST.SupportMrt, ST.SupportMrtNote, ST.SupportBtnMore, y);
            _page.Cards.Add((Mx, top, Mw, y - top));
        }

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
        if (_cur != Screen.Extra || which < 0 || which >= SupportLinks.Allowed.Length) return;
        string url = SupportLinks.Allowed[which];
        if (Program.IsTestMode)   // 시험: 브라우저를 띄우지 않고 무엇을 열려 했는지만 남긴다
        {
            try { File.AppendAllText(Path.Combine(Config.Dir, "support-open.txt"), url + Environment.NewLine); } catch { }
            Native.SetPropW(_hwnd, "OneKeyTestSupportOpen", ++_testSupportOpen);
            return;
        }
        string name = which switch { SupportLinks.IxCoupang => ST.SupportCoupang, SupportLinks.IxMyRealTrip => ST.SupportMrt, SupportLinks.IxGitHub => "GitHub", _ => ST.SupportPage };
        var it = new LaunchItem { Id = "support", Kind = "url", Name = name, Target = url, Browser = "default", Valid = true };
        if (Launcher.Unconsumed is int done and not 0) OnLaunchDone(done);
        if (Launcher.Start(it, 0, false, "", _hwnd, WM_LAUNCH_DONE) is null) LaunchToast(Launcher.Result.Busy, it.Name);
    }

    /// <summary>
    /// 제휴 링크 [자세히]: 경제적 이해관계 문구(공정위 추천·보증 심사지침 — 쿠팡은 지정 문구 그대로, 마이리얼트립은 "수수료 지급"이 든 확정 표현)를
    /// 강조 줄로 먼저 보이고, [브라우저에서 열기]를 눌렀을 때만 연다. 한국어 화면에서만(그 밖의 언어에는 단추가 없다).
    /// </summary>
    private void ShowAffiliate(int which)
    {
        if (_cur != Screen.Extra || L.Current != Lang.Ko) return;
        string body = which == SupportLinks.IxCoupang ? ST.SupportCoupangBody : ST.SupportMrtBody;
        if (Dialog.Show(_hwnd, body, ST.SupportTitle, Native.MB_OKCANCEL, okText: ST.SupportBrowser) == 1) OpenSupportLink(which);
    }

    private int _testSupportOpen;

    /// <summary>QR 모듈(빌드에 든 qr_donate.png — 회색조 8비트, 한 모듈 = 한 픽셀, 둘레 4 모듈 빈칸 포함). 실패하면 null.</summary>
    private static bool[]? QrModules(out int n)
    {
        n = _qrN;
        if (_qrMods is not null) return _qrMods;
        try
        {
            using var s = typeof(App).Assembly.GetManifestResourceStream("qr_donate.png");
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
