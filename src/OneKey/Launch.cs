using System.Text;

namespace OneKey;

/// <summary>
/// 프로그램·폴더 바로 실행 항목 하나(설계 v2 1장, Codex 16:35 L2-1·L2-5). 비밀번호가 아니지만 경로에 사용자명·공유 이름이 있을 수 있어
/// 로그·진단에 대상·인자를 남기지 않는다.
/// </summary>
internal sealed class LaunchItem
{
    public string Id = "";
    public string Kind = "";        // folder | exe | lnk | app | url(웹사이트, 0.3.77)
    public string Name = "";
    public string Target = "";      // 폴더·실행 파일·바로 가기 경로(app 이면 AUMID, url 이면 http/https 주소)
    public string Args = "";        // 바로 가기에서 읽은 인자(사용자가 쓰는 칸 없음)
    public string Dir = "";         // 바로 가기에서 읽은 작업 폴더
    public uint Mods, Vk;
    /// <summary>웹사이트(url)를 열 브라우저: default | edge | chrome(Codex 02:46 — 그 밖의 값은 실행하지 않고 보존). 다른 종류는 비어 있다.</summary>
    public string Browser = "";
    /// <summary>웹사이트 글자 아이콘의 바탕색 이름(WebIcon.LetterColors, 0.3.93 사용자). 비어 있으면 테마 강조색. 다른 종류는 비어 있다.</summary>
    public string Color = "";
    /// <summary>사용자가 고른 아이콘(아이콘이 든 파일과 번호). 비어 있으면 대상의 기본 아이콘(2026-10-04 사용자: 폴더 아이콘만 늘어서면 구분이 안 된다).</summary>
    public string Icon = "";
    public int IconIndex;
    /// <summary>실행·단축키 등록을 해도 되는 항목. false 면 목록에 "사용할 수 없음"으로 보이고 원래 줄을 그대로 보존한다.</summary>
    public bool Valid;
    public string Problem = "";     // Valid 가 false 인 이유(화면용, 경로는 넣지 않는다)
    /// <summary>파일에서 읽은 원래 필드(이 항목의 키 → 값). 고치지 않은 항목은 저장 때 이 값을 그대로 쓴다 — 모르는 필드도 잃지 않는다.</summary>
    public Dictionary<string, string> Fields = new(StringComparer.Ordinal);
    public bool HasHotkey => Vk != 0;
    public bool IsFolder => Kind == "folder";
    public bool IsUrl => Kind == "url";
    /// <summary>띠의 줄: 0 프로그램(exe·lnk·app, 모르는 종류 포함), 1 폴더(2026-10-04 사용자), 2 웹사이트(2026-10-06 사용자: 셋째 줄).</summary>
    public int Row => Kind == "folder" ? 1 : Kind == "url" ? 2 : 0;
}

/// <summary>
/// 실행 목록 파일 launch.dat(config.dat 과 같은 폴더, DPAPI, 마스터 없이 읽힘 — 잠긴 채 단축키). config.dat 과 나눈 이유: 0.3.x 이전 판은
/// 저장할 때 아는 키만 다시 써서 새 키를 지운다. 규칙(Codex L2-1·L2-5):
/// - 형식 버전 launchv=1 만 안다. 모르는 버전·복호화 실패·크기/줄 수 초과면 **읽기 전용**: 실행·단축키·편집·저장 모두 하지 않고 원본을 그대로 둔다.
/// - **구조 손상**(버전 줄이 둘·숫자가 아님, 같은 항목에 같은 키 둘, 중복 id, 필수 필드 없음, 숫자 칸을 읽지 못함, icon/iconidx 한쪽만, 30개 초과)도
///   파일 전체를 읽기 전용으로 둔다(Codex 18:27 R34-2: 다른 항목을 저장하다 손상 흔적이 지워져 실행 가능해지는 경로를 없앤다).
///   사용자는 [목록 새로 시작]으로 원본을 launch.dat.bad 로 보관하고 빈 목록에서 다시 시작할 수 있다(사용자 결정 D2). 모르는 새 형식에는 제공하지 않는다.
/// - 구조가 온전한 항목의 값 문제(지원하지 않는 종류 app·경로 형식 등)는 그 항목만 실행하지 않고(Valid=false) 원래 필드를 그대로 다시 쓴다 — 다시 읽어도 같은 판정.
///   항목에 속하지 않는 줄도 저장 때 그대로 다시 쓴다.
/// - 저장은 임시 파일에 쓴 뒤 바꿔치기. 실패하면 이전 파일이 남고 호출한 쪽의 초안도 그대로다.
/// - 읽기만으로는 아무것도 실행하지 않는다.
/// </summary>
internal static class LaunchStore
{
    public const int Max = 30, NameMax = 64, TargetMax = 1024, ArgMax = 512, UrlMax = 2048;
    private const int FileMaxBytes = 256 * 1024, LineMax = 4000, FormatVersion = 1;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("1Key/launch-list/v1");

    public static readonly List<LaunchItem> Items = new();
    private static readonly List<string> Orphans = new();   // 항목에 속하지 않는 줄(보존)
    /// <summary>읽기 전용 상태의 이유(화면에 그대로 보인다). null 이면 정상.</summary>
    public static string? ReadOnlyReason => ReadOnlyWhy switch
    {
        Why.TooLarge => T.LaunchFileTooLarge, Why.Unreadable => T.LaunchFileUnreadable, Why.Unknown => T.LaunchFileUnknown, Why.Damaged => T.LaunchFileDamaged, _ => null,
    };   // 지금 화면 언어로(언어를 바꾸면 따라 바뀐다)
    public static bool ReadOnly => ReadOnlyReason is not null;
    public enum Why { None, TooLarge, Unreadable, Unknown, Damaged }
    /// <summary>읽기 전용인 까닭(화면 문구와 따로 — 언어를 바꿔도 판정이 같다, Codex 20:13).</summary>
    public static Why ReadOnlyWhy { get; private set; }
    /// <summary>읽기 전용이지만 [목록 새로 시작]을 보여도 되는가(손상·읽지 못함·과대 — 모르는 새 형식은 아님).</summary>
    public static bool CanReset => ReadOnlyWhy is Why.TooLarge or Why.Unreadable or Why.Damaged;
    private static void SetReadOnly(Why why, string text) => ReadOnlyWhy = why;
    public static string FilePath => Path.Combine(Config.Dir, "launch.dat");
    public static string? LastSaveError { get; private set; }

    /// <summary>파일을 읽는다. 없으면 빈 목록. 실행은 하지 않는다.</summary>
    public static void Load()
    {
        Items.Clear(); Orphans.Clear(); ReadOnlyWhy = Why.None;
        string path = FilePath;
        byte[] blob;
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return;
            if (fi.Length > FileMaxBytes) { SetReadOnly(Why.TooLarge, T.LaunchFileTooLarge); return; }
            blob = File.ReadAllBytes(path);
        }
        catch { SetReadOnly(Why.Unreadable, T.LaunchFileUnreadable); return; }
        byte[]? plain = Dpapi.Unprotect(blob, Entropy);
        if (plain is null) { SetReadOnly(Why.Unreadable, T.LaunchFileUnreadable); return; }
        string text;
        try { text = Encoding.UTF8.GetString(plain); } finally { Array.Clear(plain); }
        Parse(text);
    }

    /// <summary>시험·Load 공용: 평문을 해석한다.</summary>
    internal static void Parse(string text)
    {
        Items.Clear(); Orphans.Clear(); ReadOnlyWhy = Why.None;
        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Length > LineMax) { SetReadOnly(Why.TooLarge, T.LaunchFileTooLarge); return; }
        int version = -1, versionLines = 0;
        bool damaged = false;
        var byIndex = new SortedDictionary<int, LaunchItem>();
        foreach (string raw in lines)
        {
            if (raw.Length == 0) continue;
            int eq = raw.IndexOf('=');
            string key = eq > 0 ? raw[..eq] : "", val = eq > 0 ? Unesc(raw[(eq + 1)..]) : "";
            if (key == "launchv")
            {
                if (++versionLines > 1 || !StrictInt(val, out version)) damaged = true;   // 버전 줄이 둘이거나 숫자가 아님(-1 다음 1 포함)
                continue;
            }
            // l{i}.{field}
            if (key.Length > 3 && key[0] == 'l' && key.IndexOf('.') is int dot && dot > 1
                && int.TryParse(key.AsSpan(1, dot - 1), out int idx) && idx >= 0 && idx < 1000)
            {
                string field = key[(dot + 1)..];
                if (!byIndex.TryGetValue(idx, out LaunchItem? it)) byIndex[idx] = it = new LaunchItem();
                if (!it.Fields.TryAdd(field, val)) damaged = true;   // 같은 항목에 같은 키가 둘
                continue;
            }
            Orphans.Add(raw);
        }
        if (!damaged && versionLines == 1 && version != FormatVersion) { SetReadOnly(Why.Unknown, T.LaunchFileUnknown); Items.Clear(); Orphans.Clear(); return; }
        if (versionLines == 0) damaged = true;
        if (byIndex.Count > Max) damaged = true;

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, it) in byIndex)
        {
            if (!Fill(it) || !ids.Add(it.Id)) damaged = true;   // 필수 필드·숫자 칸·아이콘 조합 / 중복 id
            Items.Add(it);
        }
        if (damaged) { SetReadOnly(Why.Damaged, T.LaunchFileDamaged); Items.Clear(); Orphans.Clear(); }
    }

    /// <summary>필드에서 속성을 채우고 값을 검사한다. 구조가 손상됐으면 false(파일 전체 읽기 전용).</summary>
    private static bool Fill(LaunchItem it)
    {
        var f = it.Fields;
        string F(string k) => f.TryGetValue(k, out string? v) ? v : "";
        foreach (string req in Required) if (!f.ContainsKey(req)) return false;
        it.Id = F("id"); it.Kind = F("kind"); it.Name = F("name"); it.Target = F("target"); it.Args = F("args"); it.Dir = F("dir");
        it.Browser = it.Kind == "url" ? (f.TryGetValue("browser", out string? b) ? b : "default") : "";
        it.Color = it.Kind == "url" && f.TryGetValue("color", out string? c) ? c : "";
        if (!StrictUint(F("mods"), out it.Mods) || !StrictUint(F("vk"), out it.Vk)) return false;
        bool hasIcon = f.ContainsKey("icon"), hasIdx = f.ContainsKey("iconidx");
        if (hasIcon != hasIdx) return false;
        it.Icon = F("icon"); it.IconIndex = 0;
        if (hasIdx && !StrictInt(F("iconidx"), out it.IconIndex)) return false;
        string? problem = Check(it);
        it.Valid = problem is null;
        it.Problem = problem ?? "";
        return true;
    }

    private static readonly string[] Required = { "id", "kind", "name", "target", "mods", "vk" };

    /// <summary>0–9 만(부호·공백 없음), 10자리 이하.</summary>
    private static bool StrictUint(string s, out uint v)
    {
        v = 0;
        if (s.Length is 0 or > 10) return false;
        foreach (char c in s) if (c is < '0' or > '9') return false;
        return uint.TryParse(s, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out v);
    }

    /// <summary>앞에 '-' 하나 허용, 나머지는 0–9 만.</summary>
    private static bool StrictInt(string s, out int v)
    {
        v = 0;
        string digits = s.StartsWith('-') ? s[1..] : s;
        if (digits.Length is 0 or > 10) return false;
        foreach (char c in digits) if (c is < '0' or > '9') return false;
        return int.TryParse(s, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out v);
    }

    /// <summary>[목록 새로 시작](사용자 결정 D2): 읽기 전용인 원본을 launch.dat.bad(있으면 시각을 붙인 이름)로 옮기고 빈 목록으로. 실패하면 false(아무것도 바꾸지 않음).</summary>
    public static bool Reset()
    {
        if (!CanReset) return false;
        if (Program.TestFails("launch:reset")) return false;
        try
        {
            string path = FilePath;
            if (File.Exists(path))
            {
                // 보관 이름이 겹치면 번호를 붙인다(덮어쓰지 않는다). 옮기기가 실패하면 원본·목록 모두 그대로
                string bad = path + ".bad";
                for (int n = 1; File.Exists(bad) || Directory.Exists(bad); n++)
                {
                    if (n > 99) return false;
                    bad = Path.Combine(Config.Dir, $"launch-{DateTime.Now:yyyyMMdd-HHmmss}-{n}.dat.bad");
                }
                File.Move(path, bad);
            }
        }
        catch { return false; }
        Items.Clear(); Orphans.Clear(); ReadOnlyWhy = Why.None;
        return true;
    }

    /// <summary>항목 값 검사(저장·읽기 공용). 문제가 없으면 null. 실행 직전 검사(Launcher)는 따로 한다.</summary>
    public static string? Check(LaunchItem it)
    {
        if (it.Id.Length != 8 || !it.Id.All(Uri.IsHexDigit)) return T.LaunchItemDamaged;
        if (it.Kind is not ("folder" or "exe" or "lnk" or "app" or "url")) return T.LaunchItemDamaged;
        if (it.Name.Length is 0 or > NameMax || it.Target.Length == 0 || it.Target.Length > (it.Kind == "url" ? UrlMax : TargetMax) || it.Args.Length > ArgMax || it.Dir.Length > ArgMax) return T.LaunchItemDamaged;
        if (HasBad(it.Name) || HasBad(it.Target) || it.Target.Contains('"') || HasBad(it.Dir) || it.Dir.Contains('"') || HasControl(it.Args)) return T.LaunchItemDamaged;
        if (it.Kind == "url")
        {
            // 웹사이트(Codex 02:46): http/https 주소만, 인자·작업 폴더·아이콘 파일 경로 없음(아이콘은 설정 폴더의 icons\<id>.*.png), 브라우저는 셋 중 하나
            if (!IsWebUrl(it.Target) || it.Args.Length > 0 || it.Dir.Length > 0 || it.Icon.Length > 0) return T.LaunchItemDamaged;
            if (it.Browser is not ("default" or "edge" or "chrome")) return T.LaunchItemDamaged;
            if (it.Color.Length > 0 && WebIcon.LetterColor(it.Color) is null) return T.LaunchItemDamaged;
        }
        else if (it.Kind == "app")
        {
            // Microsoft Store·패키지 앱(2026-10-05 사용자 — 0.3.17 까지는 보존만): 대상은 앱 ID(AUMID), 인자·작업 폴더 없음
            if (!IsAumid(it.Target) || it.Args.Length > 0 || it.Dir.Length > 0) return T.LaunchItemDamaged;
        }
        else
        {
            if (!IsRootedPath(it.Target)) return T.LaunchItemDamaged;
            if (it.Kind != "folder" && it.Target.StartsWith(@"\\", StringComparison.Ordinal)) return T.LaunchNetworkExe;   // 네트워크 위치의 실행 파일은 지원하지 않는다
        }
        if (it.Vk > 0xFE || (it.Mods & ~0x000Fu) != 0) return T.LaunchItemDamaged;
        if (it.Icon.Length > TargetMax || HasBad(it.Icon) || (it.Icon.Length > 0 && !IsRootedPath(it.Icon)) || it.IconIndex is < -65535 or > 65535) return T.LaunchItemDamaged;
        return null;
    }

    private static bool HasControl(string s) { foreach (char c in s) if (c < 0x20) return true; return false; }

    /// <summary>
    /// 웹사이트 주소 검사(저장·실행 모두, Codex 02:46): http:// 또는 https:// 로 시작하는 절대 주소, 사용자 정보(user:pass@) 없음, UrlMax 이하,
    /// 공백·제어 문자·따옴표·꺾쇠·역슬래시 없음(브라우저에 인자 하나로 넘길 때 갈라지지 않게 — 고쳐 쓰지 않고 거절한다).
    /// </summary>
    public static bool IsWebUrl(string s) => WebUri(s) is not null;

    /// <summary><see cref="IsWebUrl"/> 를 통과한 주소의 해석 결과(아니면 null).</summary>
    public static Uri? WebUri(string s)
    {
        if (s.Length is < 10 or > UrlMax) return null;
        if (!s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return null;
        foreach (char c in s)
            if (c <= 0x20 || c == 0x7F || c is '"' or '<' or '>' or '\\' or '^' or '`' or '{' or '}' or '|' || char.IsWhiteSpace(c) || char.IsControl(c)) return null;
        if (!Uri.TryCreate(s, UriKind.Absolute, out Uri? u)) return null;
        if (u.Scheme is not ("http" or "https") || u.UserInfo.Length > 0 || u.Host.Length == 0) return null;
        return u;
    }

    /// <summary>
    /// 사용자가 친 주소를 저장할 모양으로: 앞뒤 공백을 떼고, "://" 가 없으면 https:// 를 붙인다(예: naver.com). 그 밖에는 고쳐 쓰지 않는다
    /// (표시 주소와 실행 주소가 달라지지 않게). 검사는 <see cref="IsWebUrl"/>.
    /// </summary>
    public static string WebInput(string typed)
    {
        string s = typed.Trim();
        if (s.Length > 0 && !s.Contains("://", StringComparison.Ordinal)) s = "https://" + s;
        return s;
    }

    /// <summary>파비콘을 받을 주소: 입력 주소의 scheme·host·port 를 그대로 둔 origin + "/favicon.ico"(경로·쿼리·조각은 보내지 않는다, Codex 02:46).</summary>
    public static (string Scheme, string Host, int Port) WebOrigin(Uri u) => (u.Scheme, u.IdnHost, u.Port);

    /// <summary>패키지 앱 ID(AUMID) 모양: "패키지 패밀리 이름!앱 ID" — 느낌표 하나, 영문·숫자·. _ - 만, 256자 이하.</summary>
    public static bool IsAumid(string s)
    {
        if (s.Length is < 3 or > 256 || s[0] == '!' || s[^1] == '!') return false;
        int bang = 0;
        foreach (char c in s)
        {
            if (c == '!') { bang++; continue; }
            if (!(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')) return false;
        }
        return bang == 1;
    }
    private static bool HasBad(string s) => HasControl(s);

    /// <summary>C:\... 또는 \\서버\공유\... 만(상대 경로·장치 경로·드라이브 상대 경로 아님).</summary>
    public static bool IsRootedPath(string p)
    {
        if (p.Length >= 3 && char.IsAsciiLetter(p[0]) && p[1] == ':' && p[2] == '\\') return true;
        if (p.StartsWith(@"\\", StringComparison.Ordinal) && !p.StartsWith(@"\\?\", StringComparison.Ordinal) && !p.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            string[] parts = p[2..].Split('\\');
            return parts.Length >= 2 && parts[0].Length > 0 && parts[1].Length > 0;
        }
        return false;
    }

    public static string NewId()
    {
        var used = new HashSet<string>(Items.Select(i => i.Id));
        for (int n = 0; n < 100; n++)
        {
            string id = Convert.ToHexString(BitConverter.GetBytes(Random.Shared.Next())).ToLowerInvariant();
            if (!used.Contains(id)) return id;
        }
        return Guid.NewGuid().ToString("N")[..8];
    }

    /// <summary>다른 비밀번호 칸·확정 키와의 충돌은 App 이 본다. 여기서는 실행 항목끼리(자기 자신 제외).</summary>
    public static LaunchItem? HotkeyOwner(uint mods, uint vk, LaunchItem? except)
    {
        if (vk == 0) return null;
        foreach (var it in Items) if (it != except && it.Valid && it.Mods == mods && it.Vk == vk) return it;
        return null;
    }

    /// <summary>평문 만들기: 고친 항목은 속성에서, 보존 항목은 원래 필드 그대로. 위치(i)는 지금 순서로 다시 매긴다.</summary>
    internal static string Serialize()
    {
        var sb = new StringBuilder();
        sb.Append("launchv=").Append(FormatVersion).Append('\n');
        for (int i = 0; i < Items.Count; i++)
        {
            LaunchItem it = Items[i];
            var f = new Dictionary<string, string>(it.Fields, StringComparer.Ordinal);
            if (it.Valid)
            {
                f["id"] = it.Id; f["kind"] = it.Kind; f["name"] = it.Name; f["target"] = it.Target;
                f["mods"] = it.Mods.ToString(); f["vk"] = it.Vk.ToString();
                if (it.Args.Length > 0) f["args"] = it.Args; else f.Remove("args");
                if (it.Dir.Length > 0) f["dir"] = it.Dir; else f.Remove("dir");
                if (it.Icon.Length > 0) { f["icon"] = it.Icon; f["iconidx"] = it.IconIndex.ToString(); } else { f.Remove("icon"); f.Remove("iconidx"); }
                if (it.IsUrl) f["browser"] = it.Browser; else f.Remove("browser");
                if (it.IsUrl && it.Color.Length > 0) f["color"] = it.Color; else f.Remove("color");
            }
            foreach (var (k, v) in f) sb.Append('l').Append(i).Append('.').Append(k).Append('=').Append(Esc(v)).Append('\n');
        }
        foreach (string o in Orphans) sb.Append(o).Append('\n');
        return sb.ToString();
    }

    /// <summary>
    /// 백업에서 온 실행 목록 평문이 이 판의 규칙으로 온전한가(복원 전 검사, UI 스레드). 지금 목록은 바꾸지 않는다 — 검사 뒤 그대로 되돌린다.
    /// 해석 성공을 실행 승인으로 보지 않는다: 항목은 쓸 때마다 이 PC 에서 다시 검사한다(Check·Resolve).
    /// </summary>
    internal static bool ValidateText(string text)
    {
        var items = new List<LaunchItem>(Items);
        var orphans = new List<string>(Orphans);
        Why why = ReadOnlyWhy;
        try { Parse(text); return !ReadOnly; }
        finally { Items.Clear(); Items.AddRange(items); Orphans.Clear(); Orphans.AddRange(orphans); ReadOnlyWhy = why; }
    }

    /// <summary>실행 목록 평문을 이 PC 의 DPAPI 로 감싸 주어진 경로에 쓴다(복원의 launch.dat.restore). 성공하면 true.</summary>
    internal static bool WriteProtected(string path, string text)
    {
        try
        {
            byte[] plain = Encoding.UTF8.GetBytes(text);
            byte[]? blob = Dpapi.Protect(plain, Entropy);
            Array.Clear(plain);
            if (blob is null) return false;
            Backup.WriteNew(path + ".tmp", blob);   // 남은 임시 이름(링크일 수도)을 따라 쓰지 않는다
            File.Move(path + ".tmp", path, overwrite: true);
            return true;
        }
        catch { return false; }
    }

    /// <summary>저장. 읽기 전용이면 하지 않는다(false). 실패하면 이전 파일이 그대로 남는다.</summary>
    public static bool Save()
    {
        LastSaveError = null;
        if (ReadOnly) { LastSaveError = "read-only"; return false; }
        if (Backup.IsPending(Config.Dir)) { LastSaveError = "restore pending"; return false; }   // 끝나지 않은 복원 위에 쓰지 않는다(R41-1)
        if (Program.TestFails("launch:save")) { LastSaveError = "test: launch:save"; return false; }
        try
        {
            byte[] plain = Encoding.UTF8.GetBytes(Serialize());
            byte[]? blob = Dpapi.Protect(plain, Entropy);
            Array.Clear(plain);
            if (blob is null) { LastSaveError = "CryptProtectData"; return false; }
            if (!DirAcl.EnsureDir(Config.Dir)) { LastSaveError = "protected folder"; return false; }   // 좁힌 권한으로 못 만들면 저장하지 않는다(R39-1)
            string tmp = FilePath + ".tmp";
            File.WriteAllBytes(tmp, blob);
            File.Move(tmp, FilePath, overwrite: true);
            // 고친 항목의 원래 필드를 지금 값으로 맞춰 둔다(다음 저장에서 같은 값)
            foreach (var it in Items)
            {
                if (!it.Valid) continue;
                var f = it.Fields;
                f["id"] = it.Id; f["kind"] = it.Kind; f["name"] = it.Name; f["target"] = it.Target; f["mods"] = it.Mods.ToString(); f["vk"] = it.Vk.ToString();
                if (it.Args.Length > 0) f["args"] = it.Args; else f.Remove("args");
                if (it.Dir.Length > 0) f["dir"] = it.Dir; else f.Remove("dir");
                if (it.Icon.Length > 0) { f["icon"] = it.Icon; f["iconidx"] = it.IconIndex.ToString(); } else { f.Remove("icon"); f.Remove("iconidx"); }
                if (it.IsUrl) f["browser"] = it.Browser; else f.Remove("browser");
                if (it.IsUrl && it.Color.Length > 0) f["color"] = it.Color; else f.Remove("color");
            }
            return true;
        }
        catch (Exception ex) { LastSaveError = ex.GetType().Name; return false; }
    }

    private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r");
    private static string Unesc(string s)
    {
        if (s.IndexOf('\\') < 0) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length) { char c = s[++i]; sb.Append(c switch { 'n' => '\n', 'r' => '\r', _ => c }); }
            else sb.Append(s[i]);
        }
        return sb.ToString();
    }
}
