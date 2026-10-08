using System.Text;

namespace OneKey;

/// <summary>
/// 검증 전용(ONEKEY_TEST=1, --selftest-site): 창을 띄우지 않고 사이트 채우기의 순수한 부분을 시험한다.
/// 주소 정규화, 입력란 맞추기(<see cref="Uia.Match"/>), 암호 블록 v6 의 저장·읽기(연결 줄 이스케이프, 형식 번호 6 ↔ 3).
/// 결과는 한 줄에 하나 "ok|FAIL id 설명" 으로 ONEKEY_CONFIG_DIR\selftest-site.txt 에 쓴다(시험 스크립트가 판정).
/// 설정 파일은 ONEKEY_CONFIG_DIR 안의 시험용 파일만 만든다.
/// </summary>
internal static class SiteSelfTest
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool CreateHardLinkW(string newName, string existing, nint sa);

    public static string Run()
    {
        var sb = new StringBuilder();
        void Check(string id, string want, string? got, string what) => sb.Append(want == (got ?? "(null)") ? "ok   " : "FAIL ").Append(id).Append(' ').Append(what)
            .Append(want == (got ?? "(null)") ? "" : "  expected=<" + want + "> actual=<" + (got ?? "(null)") + ">").Append('\n');

        // ---- 주소 정규화 (Codex V55-1: 쿼리·조각·끝 슬래시·경로 대소문자는 그대로. 허용 정규화는 scheme·호스트 소문자, 기본 포트, 사용자 정보, 빈 경로 = /)
        Check("SN01", "https://example.com/Login?x=1#a", SiteUrl.Normalize("https://Example.COM/Login?x=1#a"), "host lower; path case, query and fragment kept");
        Check("SN02", "https://example.com/", SiteUrl.Normalize("https://example.com:443/"), "default https port dropped, root path");
        Check("SN03", "http://intra.local:8080/app/", SiteUrl.Normalize("http://intra.local:8080/app/"), "other port kept, trailing slash kept");
        Check("SN04", "https://site.com/a", SiteUrl.Normalize("https://user:pw@site.com/a"), "user info dropped");
        Check("SN05", "(null)", SiteUrl.Normalize("ftp://x/y"), "only http/https");
        Check("SN06", "(null)", SiteUrl.Normalize("about:blank"), "no scheme separator");
        Check("SN07", "https://example.com/?x", SiteUrl.Normalize("https://example.com?x"), "no path = root, query kept");
        Check("SN08", "(null)", SiteUrl.Normalize("  "), "blank");
        Check("SN09", "example.com/Login", SiteUrl.Display("https://example.com/Login"), "display drops https://");
        string N(string u) => SiteUrl.Normalize(u) ?? "(null)";
        Check("SN10", "False", (N("https://a.com/app?tenant=A") == N("https://a.com/app?tenant=B")).ToString(), "different query = different page");
        Check("SN11", "False", (N("https://a.com/#/login") == N("https://a.com/#/reset")).ToString(), "different fragment (SPA screen) = different page");
        Check("SN12", "False", (N("https://a.com/a") == N("https://a.com/a/")).ToString(), "trailing slash = different page");
        Check("SN13", "False", (N("https://a.com/Login") == N("https://a.com/login")).ToString(), "path case = different page");
        Check("SN14", "False", (N("http://a.com/") == N("https://a.com/")).ToString(), "http and https differ");
        Check("SN15", "False", (N("https://a.com:8443/") == N("https://a.com/")).ToString(), "other port differs");
        Check("SN16", "True", (N("https://A.com:443/x") == N("https://a.com/x")).ToString(), "host case and default port are the same page");
        Check("SN17", "(null)", SiteUrl.Normalize("https://a.com:44x/"), "port must be digits");
        Check("SN18", "(null)", SiteUrl.Normalize("https://a b.com/"), "no space in the host");
        Check("SN19", "a.com/login?id=…&t=…#…", SiteUrl.Display(SiteUrl.Masked("https://a.com/login?id=123&t=x#frag")), "diagnostics mask query values and fragment");
        Check("SN20", "user########", Uia.MaskDigits("user20260930"), "diagnostics mask 4+ digits");
        Check("SN21", "localhost:18765/app/########", SiteUrl.Display(SiteUrl.Masked("https://localhost:18765/app/20260930")), "port is not masked, path digits are");

        // ---- 입력란 맞추기
        Native.RECT z = default;
        var edits = new List<Uia.Field>
        {
            new("user", "ID", false, 0, z, false),
            new("pw", "PW", true, 0, z, false),
            new("search", "Search", false, 1, z, false),
        };
        string M(SiteLink l) => Uia.Match(edits, l).ToString();
        Check("SM01", "0", M(new SiteLink("u", "user", "ID", false, 0)), "same id");
        Check("SM02", "1", M(new SiteLink("u", "pw", "PW", true, 0)), "password field by id");
        Check("SM03", "0", M(new SiteLink("u", "", "ID", false, 0)), "no id: by name");
        Check("SM04", "1", M(new SiteLink("u", "r1", "PW", true, 5)), "id changed (generated ids): by name");
        Check("SM05", "-1", M(new SiteLink("u", "", "", false, 1)), "no id, no name: never guessed by order (V55-2)");
        Check("SM06", "-1", M(new SiteLink("u", "", "Other", false, 0)), "named text field not found: no guessing by order");
        Check("SM07", "-1", M(new SiteLink("u", "x", "", true, 0)), "password field whose id is gone: no order fallback (V55-2)");
        Check("SM08", "-1", M(new SiteLink("u", "pw", "PW", false, 0)), "kind must match (text link never picks the password field)");
        var twin = new List<Uia.Field>(edits) { new("user2", "ID", false, 2, z, false) };
        Check("SM09", "-1", Uia.Match(twin, new SiteLink("u", "", "ID", false, 0)).ToString(), "two fields with the same name: ambiguous, not found");
        var changed = new List<Uia.Field> { new("newPassword", "New password", true, 0, z, false) };
        Check("SM10", "-1", Uia.Match(changed, new SiteLink("u", "loginPassword", "Login password", true, 0)).ToString(), "saved login field gone, only 'new password' left: not filled (Codex case)");
        // id·이름이 없는 칸 (0.2.69, 2026-10-01 그룹웨어 로그인 <input name="Name">): 하나뿐인 class, 또는 그 종류의 유일한 칸
        var bare = new List<Uia.Field> { new("", "", false, 0, z, false, "box"), new("Password", "", true, 0, z, false) };
        Check("SM11", "0", Uia.Match(bare, new SiteLink("u", "", "", false, 0, "*")).ToString(), "the only text field on the page (no id, no name)");
        Check("SM12", "0", Uia.Match(bare, new SiteLink("u", "", "", false, 0, "box")).ToString(), "no id, no name: by its unique class");
        var two = new List<Uia.Field>(bare) { new("", "", false, 1, z, false, "box") };
        Check("SM13", "-1", Uia.Match(two, new SiteLink("u", "", "", false, 0, "*")).ToString(), "a second text field appeared: 'only one' no longer holds, not filled");
        Check("SM14", "-1", Uia.Match(two, new SiteLink("u", "", "", false, 0, "box")).ToString(), "the class is no longer unique: not filled");
        Check("SM15", "-1", Uia.Match(bare, new SiteLink("u", "", "", true, 0, "box")).ToString(), "class link of the other kind never matches");

        // ---- 암호 블록 v6 해석의 거부 사례 (V55-8): 비밀번호 99줄 + 연결 줄
        string Body(params string[] siteLines) => string.Join('\n', Enumerable.Repeat("", Config.SlotCount)) + string.Concat(siteLines.Select(l => "\n" + l));
        string ok = "site|0|https://a.com/login|user|User ID|0|0";
        Check("SP01", "True", Config.SelfTestApplyV6(Body(ok)).ToString(), "a valid link line is accepted");
        Check("SP02", "True", Config.SelfTestApplyV6(Body("site|0|https://a.com/login|a\\\\b\\pc|N\\nX|1|0")).ToString(), "allowed escapes \\\\ \\p \\n");
        Check("SP03", "False", Config.SelfTestApplyV6(Body("site|0|https://a.com/login|a\\qb||0|0")).ToString(), "unknown escape rejected");
        Check("SP04", "False", Config.SelfTestApplyV6(Body("site|0|https://a.com/login|ab\\||0|0")).ToString(), "lone trailing backslash in a field rejected");
        Check("SP05", "False", Config.SelfTestApplyV6(Body(ok, ok)).ToString(), "two links for one slot rejected");
        Check("SP06", "False", Config.SelfTestApplyV6(Body("site|0|https://a.com/login|user||0|-1")).ToString(), "negative order rejected");
        Check("SP07", "False", Config.SelfTestApplyV6(Body("site|0|https://a.com/login|user||2|0")).ToString(), "kind other than 0/1 rejected");
        Check("SP08", "False", Config.SelfTestApplyV6(Body("site|0|https://A.com/login|user||0|0")).ToString(), "non-normalised address rejected");
        Check("SP09", "False", Config.SelfTestApplyV6(Body("site|0|https://a.com/login|user||0")).ToString(), "missing column rejected");
        Check("SP10", "False", Config.SelfTestApplyV6(Body(ok + "|x")).ToString(), "extra column rejected");
        Check("SP11", "False", Config.SelfTestApplyV6(Body(ok, "junk")).ToString(), "trailing garbage line rejected");
        Check("SP12", "False", Config.SelfTestApplyV6(string.Join('\n', Enumerable.Repeat("", Config.SlotCount - 1))).ToString(), "fewer password lines than slots rejected");
        Check("SP13", "False", Config.SelfTestApplyV6(Body("site|99|https://a.com/login|user||0|0")).ToString(), "slot out of range rejected");

        // ---- 암호 블록 v6
        try
        {
            string file = Config.FilePath;
            if (File.Exists(file)) File.Delete(file);
            var cfg = Config.Load();
            cfg.CreateMaster("Master1234");
            Slot a = cfg.Slots[0];
            a.Name = "A"; a.Password = "p|w\\x\ny";
            a.Site = new SiteLink("https://example.com/login", "i|d\\1", "Na|me\nX", true, 2);
            cfg.Slots[5].Name = "B"; cfg.Slots[5].Password = "second";
            cfg.Slots[5].Site = new SiteLink("http://intra.local:8080/app", "", "", false, 0);
            Check("SV01", "True", cfg.Save().ToString(), "save with site links");
            Check("SV02", "10|argon2id", HeaderValue(file, "secretv") + "|" + HeaderValue(file, "kdf"), "saved as format 10 with Argon2id (2026-10-05: always, older versions cannot open it)");
            var back = Config.Load();
            Check("SV03", "True", back.Unlock("Master1234").ToString(), "unlock v6");
            Check("SV04", "p|w\\x\ny|second", back.Slots[0].Password + "|" + back.Slots[5].Password, "passwords survive escaping");
            Check("SV05", a.Site.ToString(), back.Slots[0].Site?.ToString(), "link 0 round trip (| \\ newline in id/name)");
            Check("SV06", cfg.Slots[5].Site!.ToString(), back.Slots[5].Site?.ToString(), "link 5 round trip");
            Check("SV07", "", back.Slots[1].Site?.ToString() ?? "", "no link on other slots");
            back.Lock();
            Check("SV08", "True", (back.Slots[0].Site is null && back.Slots[0].Password.Length == 0).ToString(), "lock clears links and passwords from memory");
            back.Unlock("Master1234");
            back.Slots[0].Site = null; back.Slots[5].Site = null;
            Check("SV09", "True", back.Save().ToString(), "save without links");
            Check("SV10", "10", HeaderValue(file, "secretv"), "without links still format 10 (no lower format for older versions any more)");
            var again = Config.Load();
            again.Unlock("Master1234");
            Check("SV11", "True", (again.Slots[0].Site is null && again.Slots[0].Password == a.Password).ToString(), "no links, passwords intact");
            File.Delete(file);
        }
        catch (Exception ex) { sb.Append("FAIL SV00 exception ").Append(ex.GetType().Name).Append(": ").Append(ex.Message).Append('\n'); }

        // ---- 형식 8: "칸을 직접 클릭하지 않기"(0.2.92, Codex 08:01) — 켠 슬롯이 있을 때만, 머리말 값은 AAD 로 묶인다
        try
        {
            string file = Config.FilePath;
            if (File.Exists(file)) File.Delete(file);
            var cfg = Config.Load();
            cfg.CreateMaster("Master1234");
            cfg.Slots[0].Name = "N"; cfg.Slots[0].Password = "nc";
            cfg.Slots[0].Site = new SiteLink("https://example.com/login", "user", "", false, 0);
            cfg.Slots[0].NoClick = true;
            Check("NC01", "True|10", cfg.Save() + "|" + HeaderValue(file, "secretv"), "a slot with no-click: saved (format 10 carries it)");
            var back = Config.Load();
            Check("NC02", "True|True|True", back.Unlock("Master1234") + "|" + back.Slots[0].NoClick + "|" + (back.Slots[0].Site is not null), "opens: no-click and the link kept");
            // 다른 프로그램이 DPAPI 를 풀어 머리말의 noclick 만 바꿔 다시 감싸면 AAD 가 달라 열리지 않는다
            byte[]? plain = Dpapi.Unprotect(File.ReadAllBytes(file));
            string text = plain is null ? "" : Encoding.UTF8.GetString(plain);
            bool had = text.Contains("s0.noclick=1\n");
            byte[]? wrapped = Dpapi.Protect(Encoding.UTF8.GetBytes(text.Replace("s0.noclick=1\n", "s0.noclick=0\n")));
            if (wrapped is not null) File.WriteAllBytes(file, wrapped);
            var tampered = Config.Load();
            Check("NC03", "True|False", had + "|" + tampered.Unlock("Master1234"), "no-click changed outside 1Key: the file does not open (AAD)");
            File.Delete(file);
            var cfg2 = Config.Load();
            cfg2.CreateMaster("Master1234");
            cfg2.Slots[0].Name = "N"; cfg2.Slots[0].Password = "nc"; cfg2.Slots[0].Site = new SiteLink("https://example.com/login", "user", "", false, 0);
            cfg2.Slots[0].NoClick = true; cfg2.Save();
            cfg2.Slots[0].NoClick = false;
            Check("NC04", "True|10", cfg2.Save() + "|" + HeaderValue(file, "secretv"), "no-click off again: still format 10");
            File.Delete(file);
        }
        catch (Exception ex) { sb.Append("FAIL NC00 exception ").Append(ex.GetType().Name).Append(": ").Append(ex.Message).Append('\n'); }

        // ---- 화면 종류 판별(2026-10-03 단순화, Codex 05:45-B): 신호 셋 중 둘 이상 = Nexacro, 0개이고 조회 완전 = 일반, 그 밖 = 확인 불가
        const string nxId = "mainframe.VFS_MAIN.CF_LOGIN.form.divLogin.form.div_login.form.edtUserId:input";
        Check("PL01", "Nexacro", Uia.ClassifyPlatform("nexainput", nxId, true, true).ToString(), "all three Nexacro signals (Nexacro login field)");
        Check("PL02", "Nexacro", Uia.ClassifyPlatform("nexainput", "user", true, true).ToString(), "two signals are enough");
        Check("PL03", "Unknown", Uia.ClassifyPlatform("nexainput", "user", false, true).ToString(), "one signal: unknown (no click, Codex 05:45-B)");
        Check("PL04", "General", Uia.ClassifyPlatform("form-control", "user", false, true).ToString(), "no signal and a complete read: regular page");
        Check("PL05", "Unknown", Uia.ClassifyPlatform("form-control", "user", false, false).ToString(), "no signal but the parent chain could not be read: unknown, not regular");
        Check("PL06", "General", Uia.ClassifyPlatform("", "mainframe.a.form.b", false, true).ToString(), "an id that only looks partly like Nexacro (no :input) is not a signal: regular");

        // ---- 형식 9: 연결한 체크박스(2026-10-03, Codex 04:10) — 해석 규칙과 저장·열기
        {
            string Body9(params string[] extra) => "pw" + string.Concat(Enumerable.Repeat("\n", Config.SlotCount - 1)) + string.Concat(extra.Select(l => "\n" + l));
            const string site0 = "site|0|https://a.com/login|uid||0|0|", anc0 = "anchor|0|https://a.com/login|chk||0|0|";
            Config? c9 = Config.SelfTestApplyV9(Body9(site0, anc0));
            Check("AN01", "True|(none)", (c9 is not null) + "|" + (c9?.Slots[0].Anchor is null ? "(none)" : "kept"), "v9: a valid checkbox line is accepted and then dropped (0.2.112)");
            Check("AN02", "(null)", Config.SelfTestApplyV7(Body9(site0, anc0))?.ToString(), "an anchor line in a format-7/8 payload is damage");
            Check("AN03", "(null)", Config.SelfTestApplyV9(Body9(anc0))?.ToString(), "an anchor without input 1's web link is damage");
            Check("AN04", "(null)", Config.SelfTestApplyV9(Body9(site0, "anchor|0|https://b.com/login|chk||0|0|"))?.ToString(), "an anchor on another address than input 1 is damage");
            Check("AN05", "(null)", Config.SelfTestApplyV9(Body9(site0, anc0, anc0))?.ToString(), "two anchors for one slot are damage");
            Check("AN06", "(null)", Config.SelfTestApplyV9(Body9(site0, "anchor|0|https://a.com/login|chk||1|0|"))?.ToString(), "an anchor marked as a password field is damage");
        }
        try
        {
            string file = Config.FilePath;
            if (File.Exists(file)) File.Delete(file);
            var cfg = Config.Load();
            cfg.CreateMaster("Master1234");
            cfg.Slots[0].Name = "N"; cfg.Slots[0].Password = "an";
            cfg.Slots[0].Site = new SiteLink("https://example.com/login", "user", "", false, 0);
            cfg.Slots[0].Anchor = new SiteLink("https://example.com/login", "", "", false, 0, "CheckBox chk_x");
            bool saved9 = cfg.TestSaveAs(9, "Master1234");   // 0.2.111 이 쓴 형식 9 파일(PBKDF2)을 만든다 — 지금 판은 새로 쓰지 않는다
            Check("AN07", "True|9", saved9 + "|" + HeaderValue(file, "secretv"), "a 0.2.111 file with a linked checkbox is format 9");
            var back = Config.Load();
            Check("AN08", "True|(none)|True", back.Unlock("Master1234") + "|" + (back.Slots[0].Anchor?.Class ?? "(none)") + "|" + (back.Slots[0].Site is not null), "format 9 opens: the checkbox link is dropped (0.2.112), the field link kept");
            Check("AN09", "True|10", back.Save() + "|" + HeaderValue(file, "secretv"), "saving again writes format 10, not 9 (the checkbox link stays dropped)");
            File.Delete(file);
        }
        catch (Exception ex) { sb.Append("FAIL AN00 exception ").Append(ex.GetType().Name).Append(": ").Append(ex.Message).Append('\n'); }

        // ---- 형식 8 조합 (Codex 08:58 §2): NoClick 만 있는 빈 슬롯, 입력 여럿 + 8 → 7, 머리말 noclick 제거·잘못된 값·secretv 낮추기
        try
        {
            string file = Config.FilePath;
            bool Rewrap(Func<string, string> edit)
            {
                byte[]? plain = Dpapi.Unprotect(File.ReadAllBytes(file));
                if (plain is null) return false;
                string t0 = Encoding.UTF8.GetString(plain), t1 = edit(t0);
                byte[]? w = Dpapi.Protect(Encoding.UTF8.GetBytes(t1));
                if (w is null || t1 == t0) return false;
                File.WriteAllBytes(file, w);
                return true;
            }
            Config Fresh()
            {
                if (File.Exists(file)) File.Delete(file);
                var c = Config.Load(); c.CreateMaster("Master1234");
                c.Slots[0].Name = "M"; c.Slots[0].Password = "id"; c.Slots[0].More = new() { new ExtraInput("pw", null) };
                c.Slots[3].NoClick = true;   // 이름·내용·단축키가 없는 슬롯에 NoClick 만
                return c;
            }
            var c5 = Fresh();
            bool saved = c5.Save();
            var r5 = Config.Load();
            Check("NC05", "True|10|True|True|1", saved + "|" + HeaderValue(file, "secretv") + "|" + r5.Unlock("Master1234") + "|" + r5.Slots[3].NoClick + "|" + r5.Slots[0].More.Count,
                  "NoClick on an otherwise empty slot + two inputs: round trip keeps both");
            r5.Slots[3].NoClick = false;
            Check("NC06", "True|10", r5.Save() + "|" + HeaderValue(file, "secretv"), "NoClick off with two inputs: still format 10");
            Fresh().Save();
            bool cut = Rewrap(t => t.Replace("s3.noclick=1\n", ""));
            Check("NC07", "True|False", cut + "|" + Config.Load().Unlock("Master1234"), "the noclick line removed outside 1Key: does not open (AAD)");
            Fresh().Save();
            bool bad = Rewrap(t => t.Replace("s3.noclick=1\n", "s3.noclick=yes\n"));
            var r8 = Config.Load();
            Check("NC08", "True|True|False", bad + "|" + r8.LoadFailed + "|" + (r8.CorruptReason is null), "noclick=yes: the file is reported damaged (only 0/1)");
            Fresh().Save();
            bool down = Rewrap(t => t.Replace("secretv=10\n", "secretv=8\n"));
            Check("NC09", "True|False", down + "|" + Config.Load().Unlock("Master1234"), "secretv lowered from 10 to 8 outside 1Key (Argon2id kept): damaged, does not open");
            Fresh().Save();
            bool dup = Rewrap(t => t.Replace("s3.noclick=1\n", "s3.noclick=1\ns3.noclick=1\n"));
            Check("NC10", "True|True", dup + "|" + Config.Load().LoadFailed, "a duplicated noclick line: the file is reported damaged");
            File.Delete(file);
        }
        catch (Exception ex) { sb.Append("FAIL NC10 exception ").Append(ex.GetType().Name).Append(": ").Append(ex.Message).Append('\n'); }

        // ---- 작업 관리자 › 시작 앱의 사용/사용 안 함 읽기 (0.2.149, 보조 진단: 모르는 형식은 Unknown, Codex D6)
        static byte[] Ap(byte first, int len = 12) { var b = new byte[len]; if (len > 0) b[0] = first; return b; }
        Check("SA01", "Enabled|Enabled", Autostart.ParseApproval(Ap(2)) + "|" + Autostart.ParseApproval(Ap(6)), "first byte 2 or 6, 12 bytes: enabled");
        Check("SA02", "Disabled|Disabled", Autostart.ParseApproval(Ap(3)) + "|" + Autostart.ParseApproval(Ap(7)), "first byte 3 or 7, 12 bytes: disabled (Task Manager)");
        Check("SA03", "Unknown|Unknown|Unknown", Autostart.ParseApproval(Ap(1)) + "|" + Autostart.ParseApproval(Ap(2, 8)) + "|" + Autostart.ParseApproval(Ap(0, 0)), "another first byte, another length, empty: unknown");

        // ---- 포커스 판정 2순위 (2026-10-02 Nexacro 업무 사이트 재현: 페이지가 스스로 커서를 넣어 둔 칸, Chrome 의 "포커스된 요소"는 문서에 머묾)
        Check("FA01", "True", Uia.FocusAccepted(true, false, 50004, false).ToString(), "the focused element is the field itself");
        Check("FA02", "True", Uia.FocusAccepted(false, true, 50030, true).ToString(), "field says focused, focused element is the document (same process): accepted");
        Check("FA03", "True", Uia.FocusAccepted(false, true, 50026, true).ToString(), "field says focused, focused element is an outer group: accepted");
        Check("FA04", "False", Uia.FocusAccepted(false, true, 50004, true).ToString(), "focused element is another edit field: refused even if the field says focused");
        Check("FA05", "False", Uia.FocusAccepted(false, false, 50030, true).ToString(), "field does not say focused: refused");
        Check("FA06", "False", Uia.FocusAccepted(false, true, 50030, false).ToString(), "focused element of another process: refused");

        // ---- 입력 여러 개 (0.2.65, 사용자 결정 2026-10-01): 암호 블록 v7 해석과 저장
        string Body7(params string[] extra) => "myid" + string.Concat(Enumerable.Repeat("\n", Config.SlotCount - 1)) + string.Concat(extra.Select(l => "\n" + l));
        string inOk = "in|0|2|p\\pw", siteOk = "insite|0|2|https://a.com/login|pw||1|0";
        Config? c7 = Config.SelfTestApplyV7(Body7(inOk, siteOk, "in|0|3|third"));
        Check("MI01", "myid|p|w|third|True", c7 is null ? "(rejected)" : string.Join("|", c7.Slots[0].InputValues()) + "|" + (c7.Slots[0].SiteOf(1) is not null), "valid v7: second/third input with escaped | and a link");
        Check("MI02", "False", Config.SelfTestApplyV6(Body7(inOk)).ToString(), "input lines are not accepted in a format-6 file");
        Check("MI03", "(null)", Config.SelfTestApplyV7(Body7("in|0|3|x"))?.ToString(), "input 3 without input 2 rejected (numbers without gaps)");
        Check("MI04", "(null)", Config.SelfTestApplyV7(Body7(inOk, inOk))?.ToString(), "the same input twice rejected");
        Check("MI05", "(null)", Config.SelfTestApplyV7(Body7("in|0|5|x"))?.ToString(), "input number above the maximum rejected");
        Check("MI06", "(null)", Config.SelfTestApplyV7(Body7("in|0|02|x"))?.ToString(), "non-canonical number rejected");
        Check("MI07", "(null)", Config.SelfTestApplyV7(Body7("in|0|2|"))?.ToString(), "empty input value rejected");
        Check("MI08", "(null)", Config.SelfTestApplyV7(Body7(siteOk))?.ToString(), "a link for an input that does not exist rejected");
        Check("MI09", "(null)", Config.SelfTestApplyV7(Body7("in|1|2|x"))?.ToString(), "second input on a slot without a first input rejected");
        Check("MI10", "(null)", Config.SelfTestApplyV7(Body7(inOk, "insite|0|2|https://a.com/login|pw||1"))?.ToString(), "link line with a missing column rejected");
        Config? c16 = Config.SelfTestApplyV7(Body7("site|0|https://a.com/login|||0|0|*"));
        Check("MI16", "*", c16?.Slots[0].Site?.Class ?? "(rejected)", "a format-7 link line may carry the class to find a field without id/name");
        Check("MI17", "(null)", Config.SelfTestApplyV7(Body7("site|0|https://a.com/login|user||0|0|box"))?.ToString(), "a class together with an id is rejected (class only for fields without id/name)");
        Check("MI18", "False", Config.SelfTestApplyV6(Body7("site|0|https://a.com/login|||0|0|*")).ToString(), "the class column is not accepted in a format-6 file");
        try
        {
            string file = Config.FilePath;
            if (File.Exists(file)) File.Delete(file);
            var cfg = Config.Load();
            cfg.CreateMaster("Master1234");
            Slot a = cfg.Slots[0];
            a.Name = "Intranet"; a.Password = "my-id";
            a.Site = new SiteLink("http://intranet.example/app/index.html", "edtUserId:input", "", false, 0);
            a.More = new() { new ExtraInput("p|w\\x", new SiteLink("http://intranet.example/app/index.html", "edtPassword:input", "", true, 0)), new ExtraInput("third", null) };
            Check("MI11", "True|10", cfg.Save() + "|" + HeaderValue(file, "secretv"), "an item with several inputs saves (format 10)");
            var back = Config.Load();
            back.Unlock("Master1234");
            Check("MI12", "my-id|p|w\\x|third", string.Join("|", back.Slots[0].InputValues()), "all inputs come back in order");
            Check("MI13", a.More[0].Site!.ToString() + "|", back.Slots[0].SiteOf(1)?.ToString() + "|" + back.Slots[0].SiteOf(2)?.ToString(), "each input keeps its own link");
            back.Lock();
            Check("MI14", "0|", back.Slots[0].More.Count + "|" + back.Slots[0].Password, "lock clears every input from memory");
            back.Unlock("Master1234");
            back.Slots[0].More = new();
            Check("MI15", "True|10", back.Save() + "|" + HeaderValue(file, "secretv"), "back to one input: still format 10");
            File.Delete(file);
        }
        catch (Exception ex) { sb.Append("FAIL MI00 exception ").Append(ex.GetType().Name).Append(": ").Append(ex.Message).Append('\n'); }

        // ---- 항목을 넣는 길 (Codex R75-1): 연결이 하나라도 있으면 커서 자리 입력으로 가지 않는다
        SiteLink L(string u, bool pw) => new SiteLink(u, pw ? "pw" : "user", "", pw, 0);
        Slot S(SiteLink? a, SiteLink? b, AppLink? app = null) => new Slot { Password = "id", Site = a, App = app, More = new() { new ExtraInput("pw", b) } };
        string R(Slot s) => App.RouteOf(s).ToString();
        Check("SR01", "Typing", R(S(null, null)), "no link at all: typed at the cursor as before");
        Check("SR02", "Site", R(S(L("https://a.com/login", false), L("https://a.com/login", true))), "every input linked to the same page");
        Check("SR03", "Incomplete", R(S(L("https://a.com/login", false), null)), "only input 1 linked: nothing typed anywhere (not the cursor path)");
        Check("SR04", "Incomplete", R(S(null, L("https://a.com/login", true))), "only input 2 linked (input 1 link removed)");
        Check("SR05", "Incomplete", R(S(L("https://a.com/login", false), L("https://b.com/login", true))), "inputs linked to different pages");
        Check("SR06", "App", R(S(null, null, new AppLink("x.exe", "W", "Edit", 0, ""))), "program link");

        // ---- 프로그램 연결의 신원 (Codex R82, 0.2.83): 전체 경로와 자리가 있어야 쓴다. 예전 연결은 열리되(손상 아님) 다시 연결 안내
        Check("AP01", "False", new AppLink("x.exe", "W", "Edit", 0, "", 10, 10, "Host", 100, 30).Usable.ToString(), "old link with only the file name: not used (link again)");
        Check("AP02", "False", new AppLink(@"C:\p\x.exe", "W", "Edit", 0, "").Usable.ToString(), "full path but no place: not used");
        Check("AP03", "True", new AppLink(@"C:\p\x.exe", "W", "Edit", 0, "", 10, 10, "Host", 100, 30).Usable.ToString(), "full path and place: used");
        Check("AP04", @"C:\p\x.exe|x.exe|True", Config.SelfTestApplyV7(Body7(@"app|0|C:\\p\\x.exe|W|Edit|0|t|10|12|Host|100|30"))?.Slots[0].App is AppLink pa
            ? pa.Exe + "|" + pa.FileName + "|" + pa.Usable : "(rejected)", "a full path survives the stored form (backslashes escaped)");
        Check("AP05", "x.exe|False", Config.SelfTestApplyV7(Body7("app|0|x.exe|W|Edit|0|t|10|12|Host|100|30"))?.Slots[0].App is AppLink pb
            ? pb.Exe + "|" + pb.Usable : "(rejected)", "an older link still opens (not damage) and is marked to link again");

        // ---- 채우기가 멈춘 이유의 순서 (Codex C62-2): 잠금·취소 → 제한 시각 → 활성 창 변경 → 그 경로의 오류
        string C(bool x, bool e, bool t, string? o) { var (k, m) = Injector.ClassifyStop(x, e, t, o); return k + (m == T.InjStopped ? ":stopped" : m == T.FillTimeout ? ":timeout" : m == T.InjActiveChanged ? ":active" : m == "other" ? ":other" : ":?"); }
        Check("SC01", "Stopped:stopped", C(true, true, true, "other"), "lock + deadline + other window: lock wins");
        Check("SC02", "Stopped:stopped", C(true, false, false, null), "lock alone");
        Check("SC03", "Timeout:timeout", C(false, true, true, "other"), "deadline beats a changed window and the path's own error");
        Check("SC04", "Failed:active", C(false, false, true, "other"), "changed window beats the path's own error");
        Check("SC05", "Failed:other", C(false, false, false, "other"), "only the path's own error");
        Check("SC06", "5", ((int)Injector.FillOutcome.NeedsElevation).ToString(), "needs-elevation is its own result, not success (C62-1)");
        // ---- 끌어 놓기의 셸 항목 목록(CIDA) 경계 검사(Codex C28-4): 정상 대조와 잘린·범위 밖·과대·잘못된 항목
        static byte[] Cida(uint n, uint off0, uint off1, byte[] tail, int cut = 0)
        {
            var l = new List<byte>(); l.AddRange(BitConverter.GetBytes(n)); l.AddRange(BitConverter.GetBytes(off0)); l.AddRange(BitConverter.GetBytes(off1)); l.AddRange(tail);
            return l.Take(l.Count - cut).ToArray();
        }
        byte[] okTail = { 0, 0, 5, 0, (byte)'a', (byte)'b', (byte)'c', 0, 0 };   // 부모 = 빈 목록(바탕 화면), 항목 = cb 5 하나 + 끝
        Check("CI01", "True", LaunchDrop.CidaValidBytes(Cida(1, 12, 14, okTail)).ToString(), "a well-formed list (control)");
        Check("CI02", "False", LaunchDrop.CidaValidBytes(Cida(1, 12, 14, okTail, cut: 1)).ToString(), "cut before the end marker");
        Check("CI03", "False", LaunchDrop.CidaValidBytes(Cida(1, 12, 100, okTail)).ToString(), "item offset outside the buffer");
        Check("CI04", "False", LaunchDrop.CidaValidBytes(Cida(1, 12, 14, new byte[] { 0, 0, 50, 0, 1, 2, 3, 0, 0 })).ToString(), "item size runs past the end");
        Check("CI05", "False", LaunchDrop.CidaValidBytes(Cida(5000, 12, 14, okTail)).ToString(), "count over the limit");
        Check("CI06", "False", LaunchDrop.CidaValidBytes(Cida(3, 12, 14, okTail)).ToString(), "count larger than the offset table");
        Check("CI07", "False", LaunchDrop.CidaValidBytes(Cida(1, 12, 14, new byte[] { 0, 0, 1, 0, 0, 0 })).ToString(), "item size below the minimum");
        Check("CI08", "False", LaunchDrop.CidaValidBytes(Array.Empty<byte>()).ToString(), "empty buffer");

        // ---- 실행기 연계(Codex R29-1): 스냅샷·생성 시각·핸들 고정을 꾸며 PID 재사용·뿌리 둘·정상 부모/자식을 본다(실제 PID 재사용을 기다리지 않는다)
        const string Ldr = @"c:\apps\loader.exe";
        var world = new Dictionary<uint, (long C, string Img)>();
        var pinned = new HashSet<uint>();
        long? Cr(uint pid) => world.TryGetValue(pid, out var w) ? w.C : null;
        string? Im(uint pid) => world.TryGetValue(pid, out var w) ? w.Img : null;
        bool Pin(uint pid, long c) => pinned.Contains(pid) && Cr(pid) == c;
        void Snap(LaunchLineage l, long now, params (uint Pid, uint Parent, long C, string Img)[] ps)
        {
            world.Clear();
            var list = new List<LaunchLineage.Proc>();
            foreach (var p in ps) { world[p.Pid] = (p.C, p.Img); list.Add(new LaunchLineage.Proc(p.Pid, p.Parent, Path.GetFileName(p.Img))); }
            l.Update(now, list, Cr, Im, Pin);
        }
        LaunchLineage Given(bool pin, long now = 1100) => new(Ldr, "loader.exe", 900, 100, 1000, pin, now);
        LaunchLineage NoRoot() => new(Ldr, "loader.exe", 900, 0, 0, false, 1100);
        const string Term = @"c:\apps\terminal.exe", Other = @"c:\other\x.exe";

        var l1 = Given(true);
        Snap(l1, 2500, (100, 8, 1000, Ldr), (200, 100, 2000, Term), (300, 200, 2100, Term));
        Check("LL01", "True|True|False", $"{l1.IsDescendant(200, 2000)}|{l1.IsDescendant(300, 2100)}|{l1.IsDescendant(100, 1000)}", "normal: child and grandchild of the launch, the root itself is not a child");
        var l2 = Given(true);
        Snap(l2, 1200, (100, 8, 1000, Ldr));
        Snap(l2, 5000, (200, 100, 3000, Term));
        Check("LL02", "True", l2.IsDescendant(200, 3000).ToString(), "the launcher ended before its child was seen, but its handle was held (pid not reusable): child linked");
        var l3 = Given(true);
        Snap(l3, 2500, (100, 8, 1000, Ldr), (200, 100, 2000, Term));
        Snap(l3, 9500, (200, 50, 9000, Other));
        Check("LL03", "False|False", $"{l3.IsDescendant(200, 9000)}|{l3.IsDescendant(200, null)}", "old child pid reused by an unrelated process: not a child (creation time differs); unreadable creation time: not a child");
        var l4 = Given(false, 1500);
        Snap(l4, 1600, (100, 8, 1000, Ldr));
        Snap(l4, 5000, (100, 7, 4000, Other), (400, 100, 4500, Term));
        Check("LL04", "False", l4.IsDescendant(400, 4500).ToString(), "old parent pid reused (handle not held): the new owner's child is not linked");
        var l5 = Given(false, 1500);
        Snap(l5, 1600, (100, 8, 1000, Ldr));
        Snap(l5, 5000, (500, 100, 4000, Term), (510, 100, 1550, Term));
        Check("LL05", "False|True", $"{l5.IsDescendant(500, 4000)}|{l5.IsDescendant(510, 1550)}", "parent gone (handle not held): a process made after its last confirmed time is not linked; one made before it is");
        var l6 = NoRoot();
        Snap(l6, 2000, (100, 8, 1200, Ldr), (101, 9, 1300, Ldr), (200, 100, 1400, Term));
        Check("LL06", "True|False", $"{l6.Ambiguous}|{l6.IsDescendant(200, 1400)}", "no root from the shell and two loader processes after the request: unknown, nothing linked");
        var l7 = NoRoot();
        Snap(l7, 2000, (100, 8, 1200, Ldr), (200, 100, 1400, Term));
        bool l7a = l7.IsDescendant(200, 1400);
        Snap(l7, 3000, (100, 8, 1200, Ldr), (200, 100, 1400, Term), (101, 9, 2500, Ldr));
        Check("LL07", "True|True|False", $"{l7a}|{l7.Ambiguous}|{l7.IsDescendant(200, 1400)}", "a second loader appears later: becomes unknown, the earlier link is no longer used");
        var l8 = Given(true);
        Snap(l8, 2000, (100, 8, 1000, Ldr), (101, 9, 1300, Ldr), (300, 101, 1400, Term));
        Check("LL08", "False|False", $"{l8.Ambiguous}|{l8.IsDescendant(300, 1400)}", "root given by the shell: another loader process is not added as a root, its child is not linked");
        var l9 = NoRoot();
        Snap(l9, 2000, (100, 8, 1200, Ldr), (110, 100, 1250, Ldr), (200, 100, 1300, Term));
        Check("LL09", "False|True", $"{l9.Ambiguous}|{l9.IsDescendant(200, 1300)}", "explorer path: one loader (its own same-file child is not a second root) - the child is linked");
        var l10 = Given(true);
        Snap(l10, 2000, (100, 8, 1000, Ldr), (200, 100, 900, Term));
        var l10b = NoRoot();
        Snap(l10b, 2000, (100, 8, 500, Ldr), (200, 100, 1300, Term));
        Check("LL10", "False|False", $"{l10.IsDescendant(200, 900)}|{l10b.IsDescendant(200, 1300)}", "a process older than its recorded parent is not a child; a loader started before the request is not a root");

        // ---- 감시 관찰 처리(Codex C28-2 / 0.3.29 회신): 창·PID·실행 파일·앱 ID 를 바꿔 넣어 거절을 본다(실제 핸들 재사용을 기다리지 않는다)
        var sv = new Launcher.SeenInfo(1, 0x1234, 100, Term, "", true);
        Check("OB01", "True|False|False|False", $"{Launcher.SeenSame(sv, true, 100, @"C:\Apps\Terminal.exe", null, false)}|{Launcher.SeenSame(sv, false, 100, Term, null, false)}|{Launcher.SeenSame(sv, true, 101, Term, null, false)}|{Launcher.SeenSame(sv, true, 100, Other, null, false)}",
            "seen window still the same: same pid and file; window gone; handle now another process; same pid but another file");
        const string Calc = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";
        var sa = new Launcher.SeenInfo(1, 0x1234, 100, "", Calc, false);
        Check("OB02", "True|False|False|False", $"{Launcher.SeenSame(sa, true, 100, null, Calc, false)}|{Launcher.SeenSame(sa, true, 100, null, Calc, true)}|{Launcher.SeenSame(sa, true, 100, null, "Other_1!App", false)}|{Launcher.SeenSame(sa, true, 100, null, null, false)}",
            "app window: same id; id unknown; another id; no id");
        Launcher.SeenStep Plan(long latest = 1000, bool exists = true, bool same = true, bool first = true, long now = 5000, nint fg = 7, nint pressFg = 7, bool own = false)
            => Launcher.SeenPlan(1000, latest == -1 ? null : latest, exists, same, first, now, fg, 0x1234, pressFg, own);
        Check("OB03", "Ignore|Ignore|Ignore|Ignore", $"{Plan(latest: 2000)}|{Plan(latest: -1)}|{Plan(exists: false)}|{Plan(same: false)}",
            "rejected: an older watch (a newer request exists), no request, item deleted, window changed");
        Check("OB04", "Keep|Keep|Keep", $"{Plan(first: false)}|{Plan(now: 1000 + 60_001)}|{Plan(fg: 0x1234)}", "remember only: already brought forward once, more than a minute later, already in front");
        Check("OB05", "Front|Front|Front|Flash", $"{Plan()}|{Plan(fg: 9, own: true)}|{Plan(fg: 0)}|{Plan(fg: 9)}",
            "user still at the window of the press / on 1Key or the desktop / no foreground: front; elsewhere: flash only");

        // ---- 패키지 매니페스트(Codex R29-2): XML 로 해석 — 따옴표·공백·이름공간·주석·DTD·여러 패키지
        const string Fnd = "http://schemas.microsoft.com/appx/manifest/foundation/windows10", Rc = "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities";
        const string Apps = "<Applications><Application Id=\"App\" Executable=\"a.exe\" EntryPoint=\"Windows.FullTrustApplication\"/></Applications>";
        byte[] Pk(string body, string extra = "") => Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"utf-8\"?>" + extra + "<Package xmlns=\"" + Fnd + "\" xmlns:rescap=\"" + Rc + "\" IgnorableNamespaces=\"rescap\">" + body + "</Package>");
        byte[] Caps(string caps) => Pk(Apps + "<Capabilities>" + caps + "</Capabilities>");
        string Mf(params byte[]?[] x) => Launcher.CheckManifests(x, "App") ?? "ok";
        Check("MF01", "ok", Mf(Caps("<rescap:Capability Name=\"runFullTrust\"/>")), "normal desktop package (runFullTrust only)");
        Check("MF02", "admin", Mf(Caps("<rescap:Capability Name=\"runFullTrust\"/><rescap:Capability Name=\"allowElevation\"/>")), "allowElevation");
        Check("MF03", "admin", Mf(Caps("<rescap:Capability Name = 'allowElevation' />")), "allowElevation with single quotes and spaces");
        Check("MF04", "admin", Mf(Caps("<r2:Capability xmlns:r2=\"" + Rc + "\" Name=\"allowElevation\"/>")), "allowElevation under another prefix");
        Check("MF05", "admin", Mf(Caps("<Capability xmlns=\"" + Rc + "\" Name=\"allowElevation\"/>")), "allowElevation with the namespace as default");
        Check("MF06", "ok", Mf(Caps("<!-- <rescap:Capability Name=\"allowElevation\"/> --><rescap:Capability Name=\"runFullTrust\"/>")), "the text only in a comment is not a capability");
        Check("MF07", "verify", Mf(Encoding.UTF8.GetBytes("<Package xmlns=\"" + Fnd + "\"><Applications><Application Id=\"App\">")), "broken XML: cannot verify");
        Check("MF08", "verify", Mf(Pk(Apps, "<!DOCTYPE Package [<!ENTITY e \"allowElevation\">]>")), "a DTD is refused (not processed): cannot verify");
        Check("MF09", "verify", Mf(Encoding.UTF8.GetBytes("<Other/>")), "root is not Package: cannot verify");
        Check("MF10", "verify", Launcher.CheckManifests(new[] { Caps("") }, "app") ?? "ok", "the app id is not in the package (case kept): cannot verify");
        Check("MF11", "admin|verify|ok", $"{Mf(Caps(""), Caps("<rescap:Capability Name=\"allowElevation\"/>"))}|{Mf(Caps(""), null)}|{Mf(Pk(""), Caps(""))}",
            "several packages in the family: one with allowElevation - refused; one unreadable - cannot verify; a resource package without the app plus the main one - ok");

        // ---- BLAKE2b·Argon2id(보안 진단 후속 B1 — 아직 저장 형식에 연결하지 않음): 공개 시험 값과 대조
        static string Hex(ReadOnlySpan<byte> b) => Convert.ToHexString(b).ToLowerInvariant();
        static string B2(string s) { var h = new Blake2b(64); h.Update(Encoding.ASCII.GetBytes(s)); Span<byte> o = stackalloc byte[64]; h.Final(o); return Hex(o); }
        Check("AG01", "ba80a53f981c4d0d6a2797b69f12f6e94c212f14685ac4b74b12bb6fdbffa2d17d87c5392aab792dc252d5de4533cc9518d38aa8dbf1925ab92386edd4009923", B2("abc"), "BLAKE2b-512(\"abc\") (RFC 7693 appendix A)");
        Check("AG02", "786a02f742015903c6c6fd852552d272912f4740e15847618a86e217f71f5419d25e1031afee585313896444934eb04b903a685b1448b755d56f701afe9be2ce", B2(""), "BLAKE2b-512 of the empty input");
        byte[] Rep(byte v, int n) { var a = new byte[n]; Array.Fill(a, v); return a; }
        Check("AG03", "0d640df58d78766c08c037a34a8b53c9d01ef0452d75b65eb52520e96b01e659",
            Hex(Argon2.Hash(Rep(1, 32), Rep(2, 16), 3, 32, 4, 32, Rep(3, 8), Rep(4, 12))), "Argon2id RFC 9106 5.3 (m=32 KiB, t=3, p=4, secret, associated data)");
        byte[] pw = Encoding.ASCII.GetBytes("password"), salt = Encoding.ASCII.GetBytes("somesalt");
        var swA = System.Diagnostics.Stopwatch.StartNew();
        string agRef = Hex(Argon2.Hash(pw, salt, 2, 1 << 16, 1, 32));
        long msA = swA.ElapsedMilliseconds;
        Check("AG04", "09316115d5cf24ed5a15a31a3ba326e5cf32edc24702987c02b6566f61913cf7", agRef, $"Argon2id reference test (m=64 MiB, t=2, p=1) — {msA} ms here");
        Check("AG05", "9dfeb910e80bad0311fee20f9c0e2b12c17987b4cac90c2ef54d5b3021c68bfe", Hex(Argon2.Hash(pw, salt, 2, 1 << 8, 1, 32)), "Argon2id reference test (m=256 KiB, t=2, p=1)");
        Check("AG06", "6d093c501fd5999645e0ea3bf620d7b8be7fd2db59c20d9fff9539da2bf57037", Hex(Argon2.Hash(pw, salt, 2, 1 << 8, 2, 32)), "Argon2id reference test (m=256 KiB, t=2, p=2 — two lanes)");
        var swB = System.Diagnostics.Stopwatch.StartNew();
        Argon2.Hash(pw, salt, 3, 1 << 16, 1, 32);
        Check("AG07", "ok", "ok", $"timing only (not judged): m=64 MiB, t=3, p=1 took {swB.ElapsedMilliseconds} ms on this PC");
        // 웹사이트 즐겨찾기(Codex 02:46): 주소 검사·origin·리다이렉트 판단·저장 형식 보존·이미지 머리 검사·디코드
        try
        {
            string Ok(params string[] urls) => string.Join("", urls.Select(u => LaunchStore.IsWebUrl(u) ? "1" : "0"));
            Check("WB01", "1111", Ok("https://a.com", "http://intra:8080/x?y=1&z=%20", "https://例え.jp/パス", "https://a.com/#frag"), "web address: http/https absolute addresses (port, query, IDN, fragment) are accepted");
            Check("WB02", "0000000000000", Ok("ftp://a.com", "javascript:alert(1)", "https://u:p@a.com", "https://a.com/x y", "https://a.com/\"x", "https://a.com/x\n", "https://a.com/" + new string('a', 2040),
                "http:/a.com", "file:///c:/x", "https://a.com\\x", "https://", " https://a.com", "https://a.com/\t"), "web address: other schemes, user info, spaces, quotes, control characters, backslash, too long, broken are refused");
            Check("WB03", "https://naver.com|https://x.com/a|http://y", $"{LaunchStore.WebInput("naver.com")}|{LaunchStore.WebInput("  https://x.com/a  ")}|{LaunchStore.WebInput("http://y")}", "typed address: trimmed, https:// added only when there is no scheme");
            var o1 = LaunchStore.WebOrigin(LaunchStore.WebUri("http://intra:8080/a/b?c=1#d")!);
            var o2 = LaunchStore.WebOrigin(LaunchStore.WebUri("https://a.com/x")!);
            Check("WB04", "http|intra|8080/https|a.com|443", $"{o1.Scheme}|{o1.Host}|{o1.Port}/{o2.Scheme}|{o2.Host}|{o2.Port}", "favicon origin keeps scheme, host and a non-default port (no path / query)");
            string Hop(string loc) => WebIcon.NextHop("http", "intra", 8080, "/favicon.ico", loc) ?? "-";
            Check("WB05", "/fav.ico|/a/b.ico?v=2|-|-|-|-|-|-", $"{Hop("/fav.ico")}|{Hop("http://intra:8080/a/b.ico?v=2")}|{Hop("http://other:8080/x")}|{Hop("http://intra:8081/x")}|{Hop("https://intra:8080/x")}|{Hop("//evil.com/x")}|{Hop("http://u:p@intra:8080/x")}|{Hop("")}",
                "redirect: only the same scheme, host and port (other host / port / scheme, user info, empty refused)");
            string Hop2(string loc) => WebIcon.NextHop("https", "a.com", 443, "/favicon.ico", loc) ?? "-";
            Check("WB06", "-|/i.ico", $"{Hop2("http://a.com/favicon.ico")}|{Hop2("https://a.com/i.ico")}", "redirect: https to http (downgrade) is refused");
            string Alias(string orig, string loc, int port = 443, string scheme = "https") => WebIcon.NextHop(scheme, orig, orig, port, "/favicon.ico", loc) is (string h, string pq) ? h + pq : "-";
            Check("WB12", "www.a.com/favicon.ico|a.com/f.ico|-|-|-|-|-|-|-",
                $"{Alias("a.com", "https://www.a.com/favicon.ico")}|{Alias("www.a.com", "https://a.com/f.ico")}|{Alias("a.com", "https://www.a.com.evil.com/x")}|{Alias("a.com", "https://wwwa.com/x")}|{Alias("a.com", "https://www.www.a.com/x")}|{Alias("a.com", "https://mail.a.com/x")}|{Alias("a.com", "https://www.a.com:8443/x")}|{Alias("a.com", "http://www.a.com/x")}|{Alias("a.com", "https://u:p@www.a.com/x")}",
                "redirect: exactly one leading www. added or removed is followed (same scheme and port); suffix tricks, other subdomains, other port, downgrade, user info refused");
            // 저장 형식: url 항목, 브라우저 값, 모르는 브라우저·인자 붙은 url 은 실행 불가로 보존
            var keepItems = new List<LaunchItem>(LaunchStore.Items);
            LaunchStore.Parse("launchv=1\nl0.id=0000abcd\nl0.kind=url\nl0.name=Site\nl0.target=https://a.com/x\nl0.mods=0\nl0.vk=0\nl0.browser=edge\n"
                + "l1.id=0000abce\nl1.kind=url\nl1.name=Odd\nl1.target=https://b.com\nl1.mods=0\nl1.vk=0\nl1.browser=firefox\n"
                + "l2.id=0000abcf\nl2.kind=url\nl2.name=Args\nl2.target=https://c.com\nl2.args=--x\nl2.mods=0\nl2.vk=0\n"
                + "l3.id=0000abd0\nl3.kind=url\nl3.name=NoBrowser\nl3.target=https://d.com\nl3.mods=0\nl3.vk=0\n");
            var w = LaunchStore.Items;
            string ser = LaunchStore.Serialize();
            Check("WB07", "False|4|True,edge,2|False|False|True,default", $"{LaunchStore.ReadOnly}|{w.Count}|{w[0].Valid},{w[0].Browser},{w[0].Row}|{w[1].Valid}|{w[2].Valid}|{w[3].Valid},{w[3].Browser}",
                "launch list: url items read (browser edge / missing = default, third row); unknown browser or arguments = not runnable");
            Check("WB08", "True|True|True|True", $"{ser.Contains("l0.browser=edge\n")}|{ser.Contains("l1.browser=firefox\n")}|{ser.Contains("l2.args=--x\n")}|{ser.Contains("l3.browser=")}",
                "saving keeps unknown values of not-runnable items as they were; a valid item is written with its browser (default)");
            // 글자 아이콘 바탕색(0.3.93): 목록의 이름만, 없으면 기본(쓰지 않음), 모르는 이름 = 실행 불가로 보존
            LaunchStore.Parse("launchv=1\nl0.id=0000abcd\nl0.kind=url\nl0.name=Blue\nl0.target=https://a.com\nl0.mods=0\nl0.vk=0\nl0.browser=default\nl0.color=blue\n"
                + "l1.id=0000abce\nl1.kind=url\nl1.name=Pink\nl1.target=https://b.com\nl1.mods=0\nl1.vk=0\nl1.browser=default\nl1.color=pink\n"
                + "l2.id=0000abcf\nl2.kind=url\nl2.name=None\nl2.target=https://c.com\nl2.mods=0\nl2.vk=0\nl2.browser=default\n");
            var wc = LaunchStore.Items;
            string serc = LaunchStore.Serialize();
            Check("WB13", "True,blue|False|True,|True|True|False|0x2F6FDB",
                $"{wc[0].Valid},{wc[0].Color}|{wc[1].Valid}|{wc[2].Valid},{wc[2].Color}|{serc.Contains("l0.color=blue\n")}|{serc.Contains("l1.color=pink\n")}|{serc.Contains("l2.color=")}|0x{(WebIcon.LetterColor("blue") is uint bc ? ((bc & 0xFF) << 16 | (bc & 0xFF00) | (bc >> 16)) : 0):X6}",
                "letter icon colour: a listed name is kept and written, none = default (not written), an unknown name = not runnable and kept as it was");
            LaunchStore.Items.Clear(); LaunchStore.Items.AddRange(keepItems);
            // 이미지 머리: PNG 크기·ICO 프레임 수·위치, 그 밖의 형식
            byte[] Png(uint wpx, uint hpx)
            {
                var b = new byte[70];
                new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(b, 0);
                b[16] = (byte)(wpx >> 24); b[17] = (byte)(wpx >> 16); b[18] = (byte)(wpx >> 8); b[19] = (byte)wpx;
                b[20] = (byte)(hpx >> 24); b[21] = (byte)(hpx >> 16); b[22] = (byte)(hpx >> 8); b[23] = (byte)hpx;
                return b;
            }
            byte[] Ico(int frames, int offsetFix = 0)
            {
                byte[] png = Png(16, 16);
                int n = frames, at0 = 6 + 16 * n;
                var b = new byte[at0 + png.Length];
                b[2] = 1; b[4] = (byte)n; b[5] = (byte)(n >> 8);
                for (int i = 0; i < n; i++)
                {
                    int e = 6 + 16 * i;
                    BitConverter.GetBytes((uint)png.Length).CopyTo(b, e + 8);
                    BitConverter.GetBytes((uint)(at0 + offsetFix)).CopyTo(b, e + 12);
                }
                png.CopyTo(b, at0);
                return b;
            }
            string H(byte[] d) => WebIcon.HeaderOk(d) ? "1" : "0";
            Check("WB09", "1100100", H(Png(16, 16)) + H(Png(512, 512)) + H(Png(513, 10)) + H(Png(600, 400)) + H(Ico(1)) + H(Ico(17)) + H(Ico(1, 50)),
                "image header: PNG up to 512 x 512 (262144 px), ICO up to 16 frames inside the file; bigger or out of range refused before decoding");
            Check("WB10", "000", H("GIF89a\x01\x00\x01\x00"u8.ToArray().Concat(new byte[30]).ToArray()) + H(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }.Concat(new byte[40]).ToArray()) + H(Png(5000, 5000)),
                "image header: GIF / JPEG refused; a huge PNG declared in a few bytes is refused without decoding");
            byte[] tiny = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
            nint bmp = WebIcon.Decode(tiny);
            byte[]? outPng = WebIcon.ToPng(bmp);
            nint hic = WebIcon.ToIcon(bmp);
            WebIcon.Free(bmp);
            bool okIcon = hic != 0; if (hic != 0) Native.DestroyIcon(hic);
            nint letter = WebIcon.Letter("가나다", 0x00C06030, 0x00FFFFFF);
            bool okLetter = letter != 0; if (letter != 0) Native.DestroyIcon(letter);
            Check("WB11", "True|True|True|True|True", $"{bmp != 0}|{outPng is { Length: > 8 } && WebIcon.HeaderOk(outPng)}|{okIcon}|{okLetter}|{WebIcon.Decode(Png(16, 16)) == 0}",
                "decode: a real 1 x 1 PNG is redrawn to a 64 x 64 PNG and an icon; letter icon; a header-only PNG does not decode");
        }
        catch (Exception ex) { sb.Append("FAIL WB exception ").Append(ex.GetType().Name).Append(' ').Append(ex.Message).Append('\n'); }
        // R68-2: 지금 세션 키 복사 중 예외 — 만든 사본만 지우고, 원본 세션은 그대로(다시 복사·저장 가능)
        try
        {
            var ck = Config.Load(); ck.CreateMaster("Copy-Key-1234");
            int w0 = Config.TestCopyWiped;
            string thrownK = "none";
            Config.TestFailCopyKey = true;
            try { ck.CopyCurrentKey(); } catch (OutOfMemoryException) { thrownK = "oom"; }
            Config.TestFailCopyKey = false;
            var again = ck.CopyCurrentKey();
            bool same = again is { } a2 && ck.IsUnlocked;
            if (again is { } a3) Crypto.Wipe(a3.Key);
            Check("KY11", "oom|1|True", $"{thrownK}|{Config.TestCopyWiped - w0}|{same}", "copying the session key fails after the copy buffer is made: the copy is wiped, the session stays unlocked and copies again");
            ck.Lock();
        }
        catch (Exception ex) { sb.Append("FAIL KY11 exception ").Append(ex.GetType().Name).Append('\n'); }
        // R47-1: 네이티브 작업 영역을 받은 직후 예외 — 영역은 돌려주고(받은 수 = 돌려준 수), 예외는 그대로 올라오며, 다음 계산은 정상
        int a0 = Argon2.TestAllocs, f0 = Argon2.TestFrees;
        string thrown = "none";
        Argon2.TestFailAfterAlloc = true;
        try { Argon2.Hash(pw, salt, 2, 1 << 8, 1, 32); } catch (OutOfMemoryException) { thrown = "oom"; }
        Argon2.TestFailAfterAlloc = false;
        string agAfter = Hex(Argon2.Hash(pw, salt, 2, 1 << 8, 1, 32));
        Check("AG08", "oom|2|2|9dfeb910e80bad0311fee20f9c0e2b12c17987b4cac90c2ef54d5b3021c68bfe",
            $"{thrown}|{Argon2.TestAllocs - a0}|{Argon2.TestFrees - f0}|{agAfter}",
            "an exception right after the native work area is allocated: the area is still cleared and freed (allocs = frees), the next hash is correct");

        // ---- 키 보관·소유 버퍼·예전 사본 지우기·옮기기(보안 진단 후속 A, Codex 15:19)
        try
        {
            string file = Config.FilePath;
            if (File.Exists(file)) File.Delete(file);
            var k = Config.Load();
            k.CreateMaster("Key-Test-77");
            k.Slots[0].Name = "K"; k.Slots[0].Password = "secret";
            bool s1 = k.Save();
            string secret1 = HeaderValue(file, "secret");
            bool s2 = k.Save();
            string secret2 = HeaderValue(file, "secret");
            byte[] b1 = Convert.FromBase64String(secret1), b2 = Convert.FromBase64String(secret2);
            Check("KY01", "True|True|True|True", $"{s1}|{s2}|{b1.AsSpan(0, 16).SequenceEqual(b2.AsSpan(0, 16))}|{!b1.AsSpan(16, 12).SequenceEqual(b2.AsSpan(16, 12))}",
                "two saves in one unlock: same key salt, new nonce each time");
            Check("KY02", "True|False", $"{k.IsCurrentMaster("Key-Test-77")}|{k.IsCurrentMaster("Key-Test-78")}", "current master check derives again and compares keys (right / wrong)");
            bool ch = k.ChangeMaster("Key-Test-77", "Key-Test-New-99");
            var kn = Config.Load();
            var ko = Config.Load();
            Check("KY03", "True|True|False|secret", $"{ch}|{kn.Unlock("Key-Test-New-99")}|{ko.Unlock("Key-Test-77")}|{kn.Slots[0].Password}", "master change: the new one opens the file, the old one no longer does, content kept");
            kn.Lock();
            Check("KY04", "True|False|False", $"{kn.Slots[0].Password.Length == 0}|{kn.Save()}|{kn.IsCurrentMaster("Key-Test-New-99")}", "lock drops the passwords and the key: nothing can be saved or checked until the next unlock");
            var st = new SecretText(4);
            st.Append("abcdef-long-enough-to-grow");
            string seen = st.Span.ToString();
            st.Dispose();
            Check("KY05", "abcdef-long-enough-to-grow|0|True", $"{seen}|{st.Length}|{st.TestAllZero()}", "owned secret buffer: grows, reads back, and is all zero after Dispose (strings are no longer overwritten)");
            // 마스터는 맞는데 내용이 손상(줄이 남음): 실패하고 슬롯에 아무것도 넣지 않는다
            var kc = Config.Load(); kc.Unlock("Key-Test-New-99");
            Environment.SetEnvironmentVariable("ONEKEY_TEST_FAIL", "save:extra-payload");
            bool savedBad;
            try { savedBad = kc.Save(); } finally { Environment.SetEnvironmentVariable("ONEKEY_TEST_FAIL", null); }
            var kd = Config.Load();
            Check("KY06", "True|False|True|0", $"{savedBad}|{kd.Unlock("Key-Test-New-99")}|{kd.PayloadInvalid}|{kd.Slots[0].Password.Length}", "right master but damaged content: refused as damage, nothing is put into the slots");
            // 예전 사본: 물을 때 본 목록만 지운다
            string bakA = Path.Combine(Config.Dir, "config.20260101-000000.bak"), bakB = Path.Combine(Config.Dir, "config.20260101-000000-1.bak");
            string bakC = Path.Combine(Config.Dir, "config.20260101-000000-2.bak"), other = Path.Combine(Config.Dir, "other.bak");
            File.WriteAllText(bakA, "x"); File.WriteAllText(bakB, "x"); File.WriteAllText(other, "x");
            Config.BackupFile[] asked = Config.BackupFiles();
            File.WriteAllText(bakB, "changed after asking");   // 물은 뒤 바뀜
            File.WriteAllText(bakC, "new after asking");       // 물은 뒤 새로 생김
            var (del, failed) = Config.DeleteBackups(asked);
            string left = string.Join(",", Config.BackupFiles().Select(x => Path.GetFileName(x.Path)).OrderBy(x => x));
            bool otherKept = File.Exists(other);
            foreach (string f in new[] { bakA, bakB, bakC, other }) File.Delete(f);
            Check("KY07", "2|1|1|config.20260101-000000-1.bak,config.20260101-000000-2.bak|True", $"{asked.Length}|{del}|{failed}|{left}|{otherKept}",
                "old copies: only the files listed when asking are deleted; one changed after asking is skipped, a new one is not touched, other files untouched");
            // R39-3: 키 유도 실패(시험 전용 실패 지점) — 잠긴 채로, 슬롯·파일 그대로, 프로세스는 계속
            File.Delete(file);
            var kv = Config.Load(); kv.CreateMaster("Key-Test-New-99"); kv.Slots[0].Name = "K"; kv.Slots[0].Password = "secret";
            kv.TestSaveAs(8, "Key-Test-New-99");   // 옮기기가 필요한 예전 형식(유도 2회: 파일 키 + 새 키)
            string h8 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)));
            var kf = Config.Load();
            Crypto.TestFailDeriveAt = 2;   // 두 번째 유도(새 Argon2id 키)에서 실패
            bool u8 = kf.Unlock("Key-Test-New-99");
            Crypto.TestFailDeriveAt = 0;
            string h8b = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)));
            Check("KY08", "False|True|False|0|True", $"{u8}|{kf.UnlockFailed}|{kf.IsUnlocked}|{kf.Slots[0].Password.Length}|{h8 == h8b}",
                "key derivation fails after the file key worked (migration key): stays locked, nothing put into the slots, file unchanged");
            Crypto.TestFailDeriveAt = 1;
            var kn2 = Config.Load();
            bool created = kn2.CreateMaster("New-Master-1");
            Crypto.TestFailDeriveAt = 0;
            Check("KY09", "False|False", $"{created}|{kn2.IsUnlocked}", "new master: key derivation fails - reported, not unlocked, the process goes on");
            var kg = Config.Load(); kg.Unlock("Key-Test-New-99");
            Crypto.TestFailDeriveAt = 2;   // 현재 확인은 되고 새 키 유도에서 실패
            bool chg = kg.ChangeMaster("Key-Test-New-99", "Other-Master-2");
            Crypto.TestFailDeriveAt = 0;
            Check("KY10", "False|True|True", $"{chg}|{kg.IsUnlocked}|{Config.Load().Unlock("Key-Test-New-99")}", "master change: the new key cannot be derived - nothing changes, the old master still opens the file");
            File.Delete(file);
        }
        catch (Exception ex) { sb.Append("FAIL KY00 exception ").Append(ex.GetType().Name).Append(": ").Append(ex.Message).Append('\n'); }

        // ---- 형식 10(Argon2id) 옮기기·헤더 검사(보안 후속 B1, Codex 15:19)
        try
        {
            string file = Config.FilePath;
            Config Make()
            {
                if (File.Exists(file)) File.Delete(file);
                var c = Config.Load(); c.CreateMaster("Mig-Test-55");
                c.Slots[0].Name = "G"; c.Slots[0].Password = "gpw";
                c.Slots[0].Site = new SiteLink("https://example.com/login", "user", "", false, 0);
                return c;
            }
            var results = new List<string>();
            foreach (int v in new[] { 3, 5, 6, 7, 8, 9 })
            {
                var c = Make();
                bool w = c.TestSaveAs(v, "Mig-Test-55");
                string before = HeaderValue(file, "secretv") + "/" + HeaderValue(file, "kdf");
                var o = Config.Load();
                bool opened = o.Unlock("Mig-Test-55");
                string after = HeaderValue(file, "secretv") + "/" + HeaderValue(file, "kdf");
                var again = Config.Load();
                results.Add($"{v}:{w}:{before}>{after}:{opened && o.KdfUpgraded && !o.MigrationFailed}:{again.Unlock("Mig-Test-55") && !again.KdfUpgraded && again.Slots[0].Password == "gpw"}");
            }
            Check("MG01", "3:True:3/600000>10/argon2id:True:True|5:True:5/600000>10/argon2id:True:True|6:True:6/600000>10/argon2id:True:True|7:True:7/600000>10/argon2id:True:True|8:True:8/600000>10/argon2id:True:True|9:True:9/600000>10/argon2id:True:True",
                string.Join("|", results), "PBKDF2 files of formats 3-9 open, are moved to format 10 / Argon2id on unlock (notice once), and open again with the same master");
            bool Rewrap(Func<string, string> edit)
            {
                byte[]? plain = Dpapi.Unprotect(File.ReadAllBytes(file));
                if (plain is null) return false;
                string t0 = Encoding.UTF8.GetString(plain), t1 = edit(t0);
                byte[]? w = Dpapi.Protect(Encoding.UTF8.GetBytes(t1));
                if (w is null || t1 == t0) return false;
                File.WriteAllBytes(file, w);
                return true;
            }
            Make().Save();
            var w10 = Config.Load();
            Check("MG02", "True|False|True|False", $"{w10.Unlock("Mig-Test-55")}|{w10.KdfUpgraded}|{HeaderValue(file, "kdfm") == "65536" && HeaderValue(file, "kdft") == "3" && HeaderValue(file, "kdfp") == "1"}|{Config.Load().Unlock("Mig-Test-56")}",
                "format 10 file: opens with the right master, no upgrade notice, profile m=65536 t=3 p=1 in the header; a wrong master is refused");
            bool big = Rewrap(t => t.Replace("kdfm=65536\n", "kdfm=1048576\n"));
            var r1 = Config.Load();
            var swBig = System.Diagnostics.Stopwatch.StartNew();
            bool u1 = r1.Unlock("Mig-Test-55");
            Check("MG03", "True|True|False|True", $"{big}|{r1.Unsupported}|{u1}|{swBig.ElapsedMilliseconds < 50}", "another Argon2id profile (1 GiB) in the header: 'newer version' before any computing (no memory, no time)");
            Make().Save();
            bool lead = Rewrap(t => t.Replace("kdft=3\n", "kdft=03\n"));
            var r2 = Config.Load();
            Make().Save();
            bool noAlg = Rewrap(t => t.Replace("kdf=argon2id\n", "kdf=600000\n"));
            var r3 = Config.Load();
            Make().Save();
            bool noM = Rewrap(t => t.Replace("kdfm=65536\n", ""));
            var r4 = Config.Load();
            Check("MG04", "True|True|True|True|True|True", $"{lead}|{r2.LoadFailed}|{noAlg}|{r3.LoadFailed}|{noM}|{r4.LoadFailed}",
                "header damage is not repaired: a leading zero, format 10 without Argon2id, a missing parameter - all reported damaged");
            Make().Save();
            bool aad = Rewrap(t => t.Replace("autolock=10\n", "autolock=0\n"));
            Check("MG05", "True|False", $"{aad}|{Config.Load().Unlock("Mig-Test-55")}", "format 10 AAD still binds the security settings (auto-lock changed outside 1Key: does not open)");
            File.Delete(file);
        }
        catch (Exception ex) { sb.Append("FAIL MG00 exception ").Append(ex.GetType().Name).Append(": ").Append(ex.Message).Append('\n'); }

        // ---- 백업 파일(내보내기·복원, Backup.cs — Codex 16:22 B 조건)
        try
        {
            string file = Config.FilePath, launchPath = LaunchStore.FilePath;
            foreach (string f in new[] { file, launchPath }) if (File.Exists(f)) File.Delete(f);
            var src = Config.Load(); src.CreateMaster("Src-Master-1");
            src.Slots[0].Name = "Mail"; src.Slots[0].Password = "p|w\\x"; src.Slots[0].Site = new SiteLink("https://example.com/login", "user", "", false, 0);
            src.Slots[0].More = new() { new ExtraInput("second", null) };
            src.Slots[2].Name = "Bank"; src.Slots[2].Password = "b4nk";
            src.Save();
            const string launchText = "launchv=1\nl0.id=a1b2c3d4\nl0.kind=folder\nl0.name=Docs\nl0.target=C:\\\\Docs\nl0.mods=0\nl0.vk=0\n";
            var ex = src.ExportForBackup()!.Value;
            byte[] secrets = ex.Secrets.ToUtf8();
            byte[] bak = Backup.Build("Backup-Pw-9", ex.Settings, secrets, Encoding.UTF8.GetBytes(launchText))!;
            Crypto.Wipe(secrets); ex.Secrets.Dispose();
            // 복원: 다른 PC 를 흉내 — 지금 설정·목록을 다른 것으로 바꿔 두고 백업으로 덮는다
            var other = Config.Load(); other.CreateMaster("Other-Master"); other.Slots[0].Name = "X"; other.Slots[0].Password = "x"; other.Save();
            LaunchStore.WriteProtected(launchPath, "launchv=1\n");
            var (c, err) = Backup.Open(bak, "Backup-Pw-9");
            Config? restored = c is null ? null : Config.FromBackup(c.Settings, c.Secrets!.Span);
            string? lt = c?.Launch is null ? null : Encoding.UTF8.GetString(c.Launch);
            restored?.CommitCreate(Config.DeriveNewMaster("New-Pc-Master")!.Value);
            bool committed = restored is not null && Backup.Commit(Config.Dir, p => restored.SaveTo(p), lt is null ? null : p => LaunchStore.WriteProtected(p, lt)) == Backup.CommitResult.Published;
            c?.Dispose();
            var back = Config.Load();
            bool opened = back.Unlock("New-Pc-Master");
            string lnBack = Encoding.UTF8.GetString(Dpapi.Unprotect(File.ReadAllBytes(launchPath), Encoding.UTF8.GetBytes("1Key/launch-list/v1")) ?? Array.Empty<byte>());
            Check("BK01", $"None|True|True|Mail|p|w\\x|second|True|Bank|b4nk|{launchText == lnBack}|False|False",
                $"{err}|{committed}|{opened}|{back.Slots[0].Name}|{back.Slots[0].Password}|{back.Slots[0].More.FirstOrDefault()?.Value}|{back.Slots[0].Site is not null}|{back.Slots[2].Name}|{back.Slots[2].Password}|{launchText == lnBack}|{Config.Load().Unlock("Src-Master-1")}|{Config.Load().Unlock("Other-Master")}",
                "export -> restore (other PC): passwords, second input, link, names and the program list come back; only the new master opens it");
            var swBad = System.Diagnostics.Stopwatch.StartNew();
            byte[] bad = (byte[])bak.Clone(); bad[11] ^= 1;   // 메모리 값
            var e1 = Backup.Open(bad, "Backup-Pw-9").Error;
            long msBad = swBad.ElapsedMilliseconds;
            byte[] ct = (byte[])bak.Clone(); ct[^1] ^= 1;
            byte[] saltBad = (byte[])bak.Clone(); saltBad[30] ^= 1;
            byte[] ver = (byte[])bak.Clone(); ver[8] = 2;
            byte[] magic = (byte[])bak.Clone(); magic[0] = (byte)'X';
            Check("BK02", $"WrongPasswordOrDamaged|BadProfile|True|WrongPasswordOrDamaged|WrongPasswordOrDamaged|NewerFormat|NotBackup|Damaged|Damaged|TooLarge",
                $"{Backup.Open(bak, "Backup-Pw-8").Error}|{e1}|{msBad < 50}|{Backup.Open(ct, "Backup-Pw-9").Error}|{Backup.Open(saltBad, "Backup-Pw-9").Error}|{Backup.Open(ver, "Backup-Pw-9").Error}|{Backup.Open(magic, "Backup-Pw-9").Error}|{Backup.Open(bak[..^1], "Backup-Pw-9").Error}|{Backup.Open(bak.Concat(new byte[] { 0 }).ToArray(), "Backup-Pw-9").Error}|{Backup.CheckHeader(new byte[Backup.MaxFile + 1])}",
                "wrong password; another Argon2id profile refused before computing; ciphertext / salt changed; newer format; not a backup; truncated; trailing byte; over 8 MiB");
            static byte[] Sec(params (byte id, byte[] data)[] s) { var l = new List<byte>(); foreach (var (id, d) in s) { l.Add(id); l.AddRange(BitConverter.GetBytes((uint)d.Length)); l.AddRange(d); } return l.ToArray(); }
            byte[] st = Encoding.UTF8.GetBytes("v=2\n"), se = Encoding.UTF8.GetBytes("x");
            string Raw(byte[] plain) => Backup.Open(Backup.TestBuildRaw("P-1234", plain), "P-1234").Error.ToString();
            byte[] lenOver = Sec((1, st), (2, se)); lenOver[1] = 0xFF;   // 첫 섹션 길이가 남은 바이트보다 큼
            Check("BK03", "Damaged|Damaged|Damaged|Damaged|Damaged|Damaged",
                $"{Raw(Sec((2, se), (1, st)))}|{Raw(Sec((1, st), (1, st), (2, se)))}|{Raw(Sec((1, st), (2, se), (4, se)))}|{Raw(lenOver)}|{Raw(Sec((1, st), (2, se)).Concat(new byte[] { 3, 0 }).ToArray())}|{Raw(Sec((1, st)))}",
                "sections: wrong order, duplicate, unknown kind, length past the end, trailing bytes, secrets missing - all damage (authenticated, so this tests the parser)");
            var (c4, _) = Backup.Open(Backup.TestBuildRaw("P-1234", Sec((1, Encoding.UTF8.GetBytes("v=2\nsecretv=8\nkdf=600000\n")), (2, se))), "P-1234");
            Check("BK04", "True|(null)", $"{c4 is not null}|{(c4 is null ? "?" : Config.FromBackup(c4.Settings, c4.Secrets!.Span) is null ? "(null)" : "accepted")}",
                "a backup whose settings are not format 10 opens but is refused as content (no partial restore)");
            c4?.Dispose();
            // 두 파일 게시가 중간에 멈춤: 표식 전이면 예전 상태 그대로, 표식 뒤면 새 상태 전부
            string H(string f) => File.Exists(f) ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f)))[..12] : "-";
            var results = new List<string>();
            foreach (int stage in new[] { 1, 2, 3, 4 })
            {
                File.WriteAllText(file, "OLD-CONFIG"); File.WriteAllText(launchPath, "OLD-LAUNCH");
                string h0c = H(file), h0l = H(launchPath);
                Backup.TestStopAfter = stage;
                var r0 = Backup.Commit(Config.Dir, p => { File.WriteAllText(p, "NEW-CONFIG"); return true; }, p => { File.WriteAllText(p, "NEW-LAUNCH"); return true; });
                Backup.TestStopAfter = 0;
                var rec = Backup.RecoverPending(Config.Dir);
                string stateC = File.ReadAllText(file), stateL = File.ReadAllText(launchPath);
                bool leftovers = Directory.GetFiles(Config.Dir, "*.restore").Length > 0 || File.Exists(Path.Combine(Config.Dir, "restore.commit"));
                results.Add($"{stage}:{r0}:{rec}:{stateC}+{stateL}:{leftovers}");
            }
            Check("BK05", "1:NotPublished:CleanedOld:OLD-CONFIG+OLD-LAUNCH:False|2:NotPublished:CleanedOld:OLD-CONFIG+OLD-LAUNCH:False|3:Pending:FinishedNew:NEW-CONFIG+NEW-LAUNCH:False|4:Pending:FinishedNew:NEW-CONFIG+NEW-LAUNCH:False",
                string.Join("|", results), "restore stopped at each step: before the commit mark the old pair stays, after it the start-up recovery finishes the new pair - never a mix");
            File.WriteAllText(file, "OLD-CONFIG"); File.WriteAllText(launchPath, "OLD-LAUNCH");
            var r6 = Backup.Commit(Config.Dir, p => { File.WriteAllText(p, "NEW-CONFIG"); return true; }, p => false);
            Check("BK06", "NotPublished|OLD-CONFIG|OLD-LAUNCH|0", $"{r6}|{File.ReadAllText(file)}|{File.ReadAllText(launchPath)}|{Directory.GetFiles(Config.Dir, "*.restore").Length}",
                "the second file cannot be written: nothing is published, no pieces left");
            // R41-1: 첫 파일을 옮긴 뒤 두 번째 옮기기 실패 — 끝내기 전에는 저장·새 게시를 하지 않고, 다시 시도하면 새 쌍 전부
            File.WriteAllText(file, "OLD-CONFIG"); File.WriteAllText(launchPath, "OLD-LAUNCH");
            Backup.TestMoves = 0; Backup.TestFailMoveAt = 2;
            var r7 = Backup.Commit(Config.Dir, p => { File.WriteAllText(p, "NEW-CONFIG"); return true; }, p => { File.WriteAllText(p, "NEW-LAUNCH"); return true; });
            string mixed = File.ReadAllText(file) + "+" + File.ReadAllText(launchPath);
            bool pend7 = Backup.IsPending(Config.Dir);   // 그 순간의 상태(아래 Check 는 복구 뒤에 평가된다)
            var cz = Config.Load(); cz.CreateMaster("Z-12345678");
            bool zSave = cz.Save();
            string zWhy = cz.LastSaveError ?? "";
            LaunchStore.Parse("launchv=1\n");   // 쓸 수 있는 빈 목록 상태로
            bool lSave = LaunchStore.Save();
            Backup.TestFailMoveAt = 3;   // 다음 옮기기(3번째)도 실패: 앞선 게시가 해결되지 않음
            var r7b = Backup.Commit(Config.Dir, p => { File.WriteAllText(p, "OTHER"); return true; }, null);   // 같은 실패가 남아 있으면 새 게시를 시작하지 않는다
            Backup.TestFailMoveAt = 0;
            var rec7 = Backup.RecoverPending(Config.Dir);
            Check("BK07", "Pending|NEW-CONFIG+OLD-LAUNCH|True|False|restore pending|False|Blocked|FinishedNew|NEW-CONFIG+NEW-LAUNCH|False",
                $"{r7}|{mixed}|{pend7}|{zSave}|{zWhy}|{lSave}|{r7b}|{rec7}|{File.ReadAllText(file)}+{File.ReadAllText(launchPath)}|{Backup.IsPending(Config.Dir)}",
                "second move fails after the first: reported as pending; while pending settings and the program list are not saved and no new restore starts; recovery finishes the new pair");
            cz.Lock();
            // R41-1: 표식이 잘림·비어 있음 → 성공으로 세지 않고 막음(파일·조각 그대로)
            string marker = Path.Combine(Config.Dir, "restore.commit");
            var r8 = new List<string>();
            foreach (string badMark in new[] { "", "1KEY-RESTORE 1\nconfig.dat|5|", "garbage\n" })
            {
                File.WriteAllText(file, "OLD-CONFIG"); File.WriteAllText(launchPath, "OLD-LAUNCH");
                File.WriteAllText(file + ".restore", "NEW-CONFIG"); File.WriteAllText(marker, badMark);
                var rr = Backup.RecoverPending(Config.Dir);
                r8.Add($"{rr}:{File.ReadAllText(file)}:{File.Exists(file + ".restore")}:{File.Exists(marker)}");
                File.Delete(marker); File.Delete(file + ".restore");
            }
            Check("BK08", "Blocked:OLD-CONFIG:True:True|Blocked:OLD-CONFIG:True:True|Blocked:OLD-CONFIG:True:True", string.Join("|", r8),
                "an empty, cut or garbage commit mark is not taken as success: blocked, nothing moved or deleted");
            // R41-1: 표식은 맞는데 조각이 없고 최종 파일도 표식의 해시가 아님 → 막음
            File.WriteAllText(file, "OLD-CONFIG"); File.WriteAllText(launchPath, "OLD-LAUNCH");
            Backup.TestStopAfter = 3;
            Backup.Commit(Config.Dir, p => { File.WriteAllText(p, "NEW-CONFIG"); return true; }, p => { File.WriteAllText(p, "NEW-LAUNCH"); return true; });
            Backup.TestStopAfter = 0;
            File.Delete(launchPath + ".restore");   // 조각 유실
            var r9 = Backup.RecoverPending(Config.Dir);
            Check("BK09", "Blocked|True", $"{r9}|{Backup.IsPending(Config.Dir)}", "a piece listed in the mark is missing and the final file does not match its hash: blocked (not counted as already moved)");
            foreach (string f in new[] { marker, file + ".restore", launchPath + ".restore" }) if (File.Exists(f)) File.Delete(f);
            // R41-3: 내용을 담는 도중·키 유도 실패 — 만든 내용이 모두 지워진다
            int live0 = Backup.TestLive;
            Backup.TestFailSectionAt = 2;
            var e10a = Backup.Open(bak, "Backup-Pw-9").Error;
            Backup.TestFailSectionAt = 0;
            Crypto.TestFailDeriveAt = 1;
            var e10b = Backup.Open(bak, "Backup-Pw-9").Error;
            Crypto.TestFailDeriveAt = 0;
            Check("BK10", "Damaged|KdfFailed|0", $"{e10a}|{e10b}|{Backup.TestLive - live0}", "a failure while taking the sections out, or while deriving the key: no content object is left undisposed");
            // R41-4: 잘못된 UTF-8 은 대체 문자로 바꾸지 않고 거절
            var bad11 = Backup.TestBuildRaw("P-1234", Sec((1, st), (2, new byte[] { 0x61, 0xC3, 0x28 })));
            Check("BK11", "Damaged", Backup.Open(bad11, "P-1234").Error.ToString(), "a secrets section with invalid UTF-8 is refused (not decoded with replacement characters)");
            // R41-4: 같은 핸들로 상한까지만 읽기
            string big = Path.Combine(Config.Dir, "big.1keybak");
            using (var fs = new FileStream(big, FileMode.Create)) fs.SetLength(Backup.MaxFile + 1);
            var rb1 = Backup.ReadBounded(big);
            File.WriteAllBytes(big, bak);
            var rb2 = Backup.ReadBounded(big);
            File.Delete(big);
            Check("BK12", "TooLarge|True", $"{rb1.Error}|{rb2.Bytes?.Length == bak.Length}", "reading a backup: a file over 8 MiB is refused before reading; a normal file is read whole");
            // Codex 17:49: 남은 임시·조각 이름이 하드 링크(바깥 파일)여도 이름만 지우고 새로 만든다 — 바깥 파일은 그대로, 게시는 정상
            string outDir = Path.Combine(Path.GetDirectoryName(Config.Dir)!, "bk13-outside");
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            Directory.CreateDirectory(outDir);
            foreach (string f in new[] { file, launchPath }) if (File.Exists(f)) File.Delete(f);
            var cz13 = Config.Load(); cz13.CreateMaster("Z-12345678");
            File.WriteAllText(file, "OLD-CONFIG"); File.WriteAllText(launchPath, "OLD-LAUNCH");
            string[] links = { "restore.commit.tmp", "config.dat.restore", "config.dat.restore.tmp", "launch.dat.restore.tmp", "config.dat.tmp" };
            int made = 0;
            foreach (string n in links)
            {
                string o = Path.Combine(outDir, n + ".out");
                File.WriteAllText(o, "OUTSIDE-" + n);
                if (CreateHardLinkW(Path.Combine(Config.Dir, n), o, 0)) made++;
            }
            var r13 = Backup.Commit(Config.Dir, p => cz13.SaveTo(p), p => LaunchStore.WriteProtected(p, "launchv=1\n"));
            bool s13 = cz13.Save();   // 보통 저장도 config.dat.tmp(링크)를 따라 쓰지 않는다
            bool outsideSame = links.All(n => File.ReadAllText(Path.Combine(outDir, n + ".out")) == "OUTSIDE-" + n);
            bool left13 = links.Any(n => File.Exists(Path.Combine(Config.Dir, n)));
            bool open13 = Config.Load().Unlock("Z-12345678");
            Check("BK13", "Published|5|True|True|False|True", $"{r13}|{made}|{outsideSame}|{s13}|{left13}|{open13}",
                "leftover temp/piece names that are hard links to outside files: only the names are removed and new files made; the outside files stay unchanged; publish and a later save work");
            cz13.Lock();
            // Codex 17:49: 임시 표식 이름이 폴더 연결(정션)이면 게시하지 않는다 — 연결 대상에 아무것도 쓰지 않고, 조각도 남기지 않는다
            string jt = Path.Combine(outDir, "jtarget");
            Directory.CreateDirectory(jt);
            string jn = Path.Combine(Config.Dir, "restore.commit.tmp");
            using (var mk = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{jn}\" \"{jt}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true }))
                mk?.WaitForExit(10000);
            bool isJ = Directory.Exists(jn) && (File.GetAttributes(jn) & FileAttributes.ReparsePoint) != 0;
            File.WriteAllText(file, "OLD-CONFIG");
            var r14 = Backup.Commit(Config.Dir, p => { File.WriteAllText(p, "NEW-CONFIG"); return true; }, null);
            Check("BK14", "True|NotPublished|0|OLD-CONFIG|False", $"{isJ}|{r14}|{Directory.GetFileSystemEntries(jt).Length}|{File.ReadAllText(file)}|{File.Exists(file + ".restore")}",
                "the temporary commit-mark name is a directory junction: nothing is published, nothing is written into the junction target, no pieces left");
            if (Directory.Exists(jn)) Directory.Delete(jn);   // 연결만 지운다
            Directory.Delete(outDir, true);
            foreach (string f in new[] { file, launchPath }) if (File.Exists(f)) File.Delete(f);
        }
        catch (Exception ex) { sb.Append("FAIL BK00 exception ").Append(ex.GetType().Name).Append(": ").Append(ex.Message).Append('\n'); }

        // ---- 설정 폴더 권한(2026-10-05 사용자 결정, Codex 15:19 Q5) — 시험 설정 폴더 아래 시험 폴더만 쓴다(실제 사용자 폴더 아님)
        try
        {
            string user = DirAcl.CurrentUserSid() ?? "?";
            string root = Path.Combine(Config.Dir, "acltest");
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Directory.CreateDirectory(root);
            string fresh = Path.Combine(root, "new");
            DirAcl.EnsureDir(fresh);
            File.WriteAllText(Path.Combine(fresh, "config.dat"), "x");
            Check("DA01", "(ok)", DirAcl.Check(fresh, user) ?? "(ok)", "a new settings folder is created with only the user + SYSTEM (protected), and a file written into it inherits that");
            string old = Path.Combine(root, "old");
            Directory.CreateDirectory(old);
            string f1 = Path.Combine(old, "config.dat"), f2 = Path.Combine(old, "launch.dat");
            File.WriteAllText(f1, "x"); File.WriteAllText(f2, "x");
            Directory.CreateDirectory(Path.Combine(old, "sub")); File.WriteAllText(Path.Combine(old, "sub", "config.1.bak"), "x");
            bool grant = DirAcl.TestAddEveryoneRead(f1);
            string before = DirAcl.Check(old, user) ?? "(ok)";
            var (st1, _) = DirAcl.Secure(old);
            string after = DirAcl.Check(old, user) ?? "(ok)";
            var (st2, _) = DirAcl.Secure(old);
            Check("DA02", "True|old|Protected|(ok)|AlreadyProtected", $"{grant}|{before}|{st1}|{after}|{st2}",
                "an existing folder (inherited rights + a file with an explicit Everyone read entry + a subfolder): narrowed, the explicit entry removed, every item checked; a second run changes nothing");
            string link = Path.Combine(root, "link");
            var mk = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{fresh}\"") { CreateNoWindow = true, UseShellExecute = false });
            mk?.WaitForExit(5000);
            var (st3, _) = DirAcl.Secure(link);
            Check("DA03", "True|SkippedReparse", $"{Directory.Exists(link)}|{st3}", "a junction (link to another folder) is not followed or changed");
            if (Directory.Exists(link)) Directory.Delete(link);
            DirAcl.TestFailOnce = true;
            var (st4, _) = DirAcl.Secure(old);
            Check("DA04", "Failed", st4.ToString(), "a failure is reported as failed (the app shows a notice), never as protected");
            // R39-2: 안에 연결이 있으면 바꾸기 전에 그만둔다 — 폴더도, 연결이 가리키는 바깥 폴더도 그대로
            string outside = Path.Combine(root, "outside"); Directory.CreateDirectory(outside); File.WriteAllText(Path.Combine(outside, "x.txt"), "x");
            string withLink = Path.Combine(root, "withlink"); Directory.CreateDirectory(withLink); File.WriteAllText(Path.Combine(withLink, "config.dat"), "x");
            string inner = Path.Combine(withLink, "j");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{inner}\" \"{outside}\"") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit(5000);
            string? outBefore = DirAcl.TestSddlOf(outside), wlBefore = DirAcl.TestSddlOf(withLink), xBefore = DirAcl.TestSddlOf(Path.Combine(outside, "x.txt"));
            var (st5, d5) = DirAcl.Secure(withLink);
            bool same5 = outBefore == DirAcl.TestSddlOf(outside) && wlBefore == DirAcl.TestSddlOf(withLink) && xBefore == DirAcl.TestSddlOf(Path.Combine(outside, "x.txt"));
            Check("DA05", "True|SkippedChild|True", $"{Directory.Exists(inner)}|{st5}|{same5}", $"a link inside the folder: stop before changing anything - the folder and the outside target keep their rights ({d5})");
            if (Directory.Exists(inner)) Directory.Delete(inner);
            // 부모 경로의 연결
            string viaLink = Path.Combine(root, "via");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{viaLink}\" \"{outside}\"") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit(5000);
            string sub = Path.Combine(viaLink, "sub"); Directory.CreateDirectory(sub);
            var (st6, d6) = DirAcl.Secure(sub);
            Check("DA06", "SkippedReparse|parent reparse point", $"{st6}|{d6}", "a folder reached through a link in its parent path is not changed");
            Directory.Delete(sub); if (Directory.Exists(viaLink)) Directory.Delete(viaLink);
            // R39-1: 좁힌 만들기가 안 되면 보통 폴더로 대신 만들지 않는다
            string nf = Path.Combine(root, "notmade");
            DirAcl.TestFailCreateOnce = true;
            bool made = DirAcl.EnsureDir(nf);
            Check("DA07", "False|False", $"{made}|{Directory.Exists(nf)}", "restricted creation failing: no folder is made the ordinary way (the save reports it instead)");
            // R39-1: 사용자 ACE 가 읽기뿐이면 "보호됨"이 아니다(SID 만이 아니라 권한·상속까지 본다)
            string weakAcl = Path.Combine(root, "weakacl"); Directory.CreateDirectory(weakAcl); File.WriteAllText(Path.Combine(weakAcl, "config.dat"), "x");
            bool set8 = DirAcl.TestSetSddl(weakAcl, "D:P(A;OICI;FR;;;{user})(A;OICI;FA;;;SY)");
            string chk8 = DirAcl.Check(weakAcl, user) ?? "(ok)";
            var (st8, _) = DirAcl.Secure(weakAcl);
            Check("DA08", "True|weakacl|Protected|(ok)", $"{set8}|{chk8}|{st8}|{DirAcl.Check(weakAcl, user) ?? "(ok)"}", "the right users but only read access is not 'protected': it is narrowed again to full access for the user");
            Directory.Delete(root, true);
        }
        catch (Exception ex) { sb.Append("FAIL DA00 exception ").Append(ex.GetType().Name).Append(": ").Append(ex.Message).Append('\n'); }
        return sb.ToString();
    }

    /// <summary>
    /// 검증 전용(--selftest-kdf): 줄마다 "B|자료hex|출력길이" 또는 "A|비밀번호hex|salthex|t|mKiB|p|태그길이|secrethex|adhex" 를 계산해
    /// 같은 순서로 hex(실패하면 "ERR:종류")를 적는다. 독립 구현(Node·OpenSSL Argon2id, Python hashlib BLAKE2b)이 같은 경우를 계산해 비교한다.
    /// </summary>
    internal static int KdfVectors(string inPath, string outPath)
    {
        var lines = new List<string>();
        foreach (string raw in File.ReadAllLines(inPath))
        {
            if (raw.Length == 0) continue;
            string[] f = raw.Split('|');
            try
            {
                if (f[0] == "B")
                {
                    byte[] data = Convert.FromHexString(f[1]);
                    int n = int.Parse(f[2]);
                    var h = new Blake2b(n);
                    h.Update(data);
                    byte[] o = new byte[n];
                    h.Final(o);
                    lines.Add(Convert.ToHexString(o).ToLowerInvariant());
                }
                else
                {
                    byte[] tag = Argon2.Hash(Convert.FromHexString(f[1]), Convert.FromHexString(f[2]), int.Parse(f[3]), int.Parse(f[4]), int.Parse(f[5]), int.Parse(f[6]),
                        Convert.FromHexString(f[7]), Convert.FromHexString(f[8]));
                    lines.Add(Convert.ToHexString(tag).ToLowerInvariant());
                }
            }
            catch (Exception ex) { lines.Add("ERR:" + ex.GetType().Name); }
        }
        File.WriteAllLines(outPath, lines);
        return 0;
    }

    private static string HeaderValue(string file, string key)
    {
        byte[]? plain = Dpapi.Unprotect(File.ReadAllBytes(file));
        if (plain is null) return "(unreadable)";
        foreach (string line in Encoding.UTF8.GetString(plain).Split('\n'))
            if (line.StartsWith(key + "=", StringComparison.Ordinal)) return line[(key.Length + 1)..].TrimEnd('\r');
        return "(missing)";
    }
}
