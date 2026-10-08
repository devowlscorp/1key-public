using System.Runtime.InteropServices;
using System.Text;

namespace OneKey;

/// <summary>비밀번호를 입력할 때 사용할 방식.</summary>
internal enum InputMethod
{
    Auto = 0,        // 스캔코드 + 실패 문자만 유니코드 보완 (권장)
    ScanCode = 1,    // 하드웨어 스캔코드
    Unicode = 2,     // KEYEVENTF_UNICODE
    PostMessage = 3, // 포커스 컨트롤에 WM_CHAR 직접 전달
    Clipboard = 4,   // 클립보드 복사 후 Ctrl+V
}

/// <summary>
/// 사이트 채우기 연결(2026-09-30 사용자 결정): 이 주소(<see cref="SiteUrl.Normalize"/>, 정확히 같을 때만)의 이 입력란에
/// 항목 내용을 [채우기] 버튼으로 넣는다. 입력란은 UIA 의 ID·이름·비밀번호 칸 여부·같은 종류 안의 순서로 찾는다(<see cref="Uia.Match"/>).
/// 암호 블록 안에 저장한다(잠금을 풀어야 보이고, 변조하면 복호화가 실패한다).
/// </summary>
/// <summary>
/// 사이트 채우기 연결. 칸은 id → 이름 순서로 찾는다. 페이지가 둘 다 주지 않는 칸(예: &lt;input name="Name"&gt; 뿐인 로그인 칸)은
/// Class 로: 그 칸의 class 가 같은 종류(글/비밀번호) 칸 가운데 하나뿐이면 그 class, class 도 없고 같은 종류 칸이 그 하나뿐이면 "*"
/// (2026-10-01 사용자 보고). 둘 다 "그 페이지에서 하나뿐"일 때만 맞는다: 나중에 같은 칸이 둘 이상이면 넣지 않는다(순서로 짐작하지 않음).
/// </summary>
internal sealed record SiteLink(string Url, string FieldId, string FieldName, bool Password, int Index, string Class = "");

/// <summary>한 항목의 두 번째 이후 입력(예: 아이디 다음의 비밀번호). 값과 그 입력의 사이트 연결. 잠금 해제 중에만 메모리에 있다(암호 블록 안, secretv=7).</summary>
internal sealed record ExtraInput(string Value, SiteLink? Site);

internal sealed class Slot
{
    public string Name = string.Empty;
    public string Password = string.Empty;   // 잠금 상태에서는 비어 있다(메모리에 두지 않음).
    public uint Mods;            // MOD_ALT=1, MOD_CONTROL=2, MOD_SHIFT=4, MOD_WIN=8
    public uint Vk;              // 가상 키 코드 (0 = 단축키 없음)
    public InputMethod Method = InputMethod.Auto;
    public bool PressEnter;
    /// <summary>
    /// 브라우저에서는 Enter 를 보내지 않는다 (사용자 결정 2026-09-29, 0.2.41). 기본은 꺼짐 = Enter 는 브라우저에서도 보낸다.
    /// 브라우저 안의 입력란은 창 하나를 같이 써서 입력 도중 다른 칸으로 옮겨 가도 알아채지 못하므로(T1), 걱정되는 항목만 켠다.
    /// PressEnter 가 켜져 있을 때만 뜻이 있고, 저장할 때 그렇게 맞춘다.
    /// </summary>
    public bool NoEnterInBrowser;
    /// <summary>
    /// 사이트·프로그램 채우기에서 칸에 커서를 넣지 못할 때 화면을 실제로 클릭하지 않는다(2026-10-02 Codex 08:01 권고, 사용자 결정: 기본은 클릭함).
    /// 켜면 웹은 SetFocus·기본 동작까지만, 프로그램은 SetFocus 까지만 한다. 클릭이 필요한 화면에서는 사용자가 칸을 먼저 누른 뒤 다시 채운다.
    /// 보안에 영향을 주는 값이라 AAD 에 넣는다(형식 8, 켠 슬롯이 있을 때만).
    /// </summary>
    public bool NoClick;
    /// <summary>
    /// 편집 화면 양식(2026-10-06 사용자 — 동료가 [+ 추가]가 어렵다고 함): 1 = 자주 사용하는 문구(한 칸), 2 = 사이트·앱 로그인(ID·PW 두 칸),
    /// 3 = 연속된 문구 입력(여러 칸, 예전 화면). 0 = 예전 판이 만든 항목(예전 화면 = 3 으로 연다). 화면 모양만 정하고 입력·연결에는 영향이 없다.
    /// 머리말 s{i}.form(AAD 밖) — 예전 판은 모르는 줄로 무시한다.
    /// </summary>
    public int Form;
    /// <summary>0.2.39~0.2.40 의 "브라우저에서도 Enter"(secretv=4 파일). 그 파일의 AAD 를 확인할 때만 쓰고, 새 형식으로 저장하면 없어진다.</summary>
    public bool LegacyEnterInBrowser;
    /// <summary>사이트 채우기 연결. 잠금 해제 중에만 메모리에 있다(암호 블록 안, secretv=6).</summary>
    public SiteLink? Site;

    /// <summary>
    /// 입력 여러 개(사용자 결정 2026-10-01): 첫 입력은 <see cref="Password"/>·<see cref="Site"/>, 두 번째부터 여기(최대 <see cref="MaxInputs"/> 개).
    /// 단축키는 입력마다 칸의 글을 비우고(모두 선택) 넣은 뒤 Tab 으로 다음 칸에 간다. 사이트 채우기는 입력마다 연결한 칸에 넣는다.
    /// 비어 있는 입력은 저장하지 않는다(편집 화면이 막는다). 잠금 해제 중에만 메모리에 있다.
    /// </summary>
    public List<ExtraInput> More = new();
    /// <summary>윈도우 프로그램 연결(첫 입력의 칸). 웹 연결(Site)과 함께 쓰지 않는다. 잠금 해제 중에만 메모리에 있다(암호 블록 안, secretv=7).</summary>
    public AppLink? App;
    /// <summary>
    /// 0.2.111 의 "체크박스에서 출발"(형식 9) 연결. 그 기능은 0.2.112 에서 뺐다(2026-10-03 사용자: 단순하게, Codex 05:45-E). 형식 9 파일은
    /// 검증해 읽되 이 값은 쓰지 않고 버린다(다음 저장에서 남은 기능에 맞는 형식으로 내려간다). 검증 전용으로만 채워진다.
    /// </summary>
    public SiteLink? Anchor;
    public const int MaxInputs = 4;
    public int InputCount => 1 + More.Count;
    public string ValueOf(int input) => input == 0 ? Password : More[input - 1].Value;
    public SiteLink? SiteOf(int input) => input == 0 ? Site : More[input - 1].Site;
    public bool HasAnySite => Site is not null || More.Any(m => m.Site is not null);
    /// <summary>단축키로 넣을 값들(순서대로, 빈 값 제외).</summary>
    public string[] InputValues() => new[] { Password }.Concat(More.Select(m => m.Value)).Where(v => v.Length > 0).ToArray();

    public bool HasHotkey => Vk != 0 && (Mods != 0 || Keys.AllowsBareHotkey(Vk));
    public bool HasPassword => Password.Length > 0;
    /// <summary>파일에 저장된 내용이 있는가. 잠금 상태(비밀번호가 메모리에 없을 때)에도 알 수 있도록 헤더에 따로 적는다.</summary>
    public bool HasContent;
    /// <summary>단축키를 등록해야 하는 슬롯인가: 내용이 있으면(잠겨 있어도) 등록한다. 잠긴 채 누르면 잠금 화면을 띄운다.</summary>
    public bool InUse => HasPassword || HasContent;

    public string DisplayName(int index)
        => Name.Length > 0 ? Name : T.SlotDefault(index + 1);

    public string HotkeyText()
    {
        if (Vk == 0) return T.CommonNone;
        var sb = new StringBuilder();
        if ((Mods & 2) != 0) sb.Append("Ctrl+");
        if ((Mods & 1) != 0) sb.Append("Alt+");
        if ((Mods & 4) != 0) sb.Append("Shift+");
        if ((Mods & 8) != 0) sb.Append("Win+");
        sb.Append(Keys.NameOf(Vk));
        return sb.ToString();
    }
}

internal sealed class Config
{
    /// <summary>슬롯 수. 파일 형식은 s{i}.* 키라 수를 늘려도 그대로다. 단축키 id(0..98)와 UI id 대역도 이 수를 따른다.</summary>
    public const int SlotCount = 99;

    public readonly Slot[] Slots = Enumerable.Range(0, SlotCount).Select(_ => new Slot()).ToArray();
    public bool AutoStart;
    public bool StartMinimized = true;
    /// <summary>창을 닫으면 작업 표시줄 위에 마스코트(선택, 기본 꺼짐). 예전 판이 이 설정 파일을 저장하면 이 값은 사라져 꺼짐으로 돌아간다(Codex 09:43 — 모르는 키는 보존하지 않음).</summary>
    public bool Walker;
    public bool RequireAdmin;
    public int KeyDelayMs = 20;      // 글자 사이 지연
    public int PreDelayMs = 80;      // 입력 시작 전 지연
    public int AutoLockMinutes = 10; // 0 = 자동 잠금 안 함
    /// <summary>
    /// 목록에서 넣기(입력 칩)의 공통 확정 키. 칩이 떠 있는 동안만 등록한다. 기본 Ctrl+Alt+Enter (mods: 1 Alt, 2 Ctrl, 4 Shift, 8 Win).
    /// 보안 값이 아니라서(칩은 잠금을 푼 사용자만 띄운다) AAD 에 넣지 않는다. 그래서 secretv=3 형식은 그대로다.
    /// </summary>
    public uint ConfirmMods = DefaultConfirmMods, ConfirmVk = DefaultConfirmVk;
    /// <summary>
    /// 화면 테마: 0 = Windows 설정을 따름, 1 = 밝게, 2 = 어둡게. 헤더에 두어 잠금 화면에도 적용한다.
    /// 보안 값이 아니라서 AAD 에 넣지 않는다(형식 번호는 그대로). 이전 판은 모르는 키로 보고 무시한다.
    /// </summary>
    public int ThemeMode;
    /// <summary>아이콘 띠에 이름을 보일 줄: 1 = 프로그램, 2 = 폴더(기본 2 — 프로그램은 아이콘으로 구분되고 폴더 아이콘은 모두 같다, 2026-10-04 사용자).</summary>
    public int LaunchNames = 2;
    /// <summary>화면 언어(L.Codes 의 값). 빈 값 = Windows 설정 따름. 헤더 값이라 잠금 화면에도 적용된다(보안과 무관해 AAD 에 넣지 않는다).</summary>
    public string Language = "";
    public const uint DefaultConfirmMods = 3, DefaultConfirmVk = 0x0D;

    // 잠금은 두 가지 경우에만 걸린다: 사용자가 [지금 잠금]을 누르거나,
    // 마지막 키보드·마우스 입력 후 AutoLockMinutes 가 지나거나.
    // 창을 닫거나 최소화하는 것만으로는 잠그지 않는다(예전 locktray 설정은 폐기).

    // ---------- 잠금 상태 ----------
    /// <summary>암호화된 비밀번호 블록이 파일에 있는가 (= 첫 실행이 아님).</summary>
    public bool HasMaster { get; private set; }
    /// <summary>비밀번호가 메모리에 복호화되어 있는가.</summary>
    public bool IsUnlocked { get; private set; }
    /// <summary>설정 파일이 있는데 읽지 못했다(잠긴 파일, 손상, 다른 계정). 첫 실행과 구분해 사용자에게 알리고, 덮어쓰기 전에 사본을 남긴다.</summary>
    public bool LoadFailed { get; private set; }
    /// <summary>마지막 Save() 가 실패한 이유의 기술 정보(오류 종류·코드, 언어와 무관). 사용자 문장은 <see cref="LastSaveFail"/> 로 고른다. 성공하면 null.</summary>
    public string? LastSaveError { get; private set; }
    /// <summary>마지막 Save() 실패의 종류: 사용자에게 원인에 맞는 할 일을 알리는 데 쓴다 (Codex QA-07). 성공하면 None.</summary>
    public SaveFail LastSaveFail { get; private set; }
    /// <summary>0.2.22 이전 형식 파일을 열었지만 새 형식으로 다시 저장하지 못했다 (다음 저장 때 다시 시도된다).</summary>
    public bool MigrationFailed { get; private set; }

    // 잠금 해제 동안만: 마스터에서 유도한 키(고정 배열)와 그 salt·반복 횟수(재저장용). 마스터 문자열은 들고 있지 않는다(2026-10-05 보안 진단 후속).
    private byte[]? _key, _keySalt;
    private Kdf _keyKdf;
    private byte[]? _secretBlob;  // 파일에서 읽은 암호화 블록 (잠금 상태에서도 보관)
    private int _kdfIterations = Crypto.LegacyIterations;   // 파일의 PBKDF2 반복 횟수 (헤더 kdf=, 없으면 0.2.22 이전 값)
    private bool _kdfArgon, _kdfFieldsSeen;                 // 헤더 kdf=argon2id / kdfm·kdft·kdfp 가 있었다(형식 10)
    private int? _kdfM, _kdfT, _kdfP;
    /// <summary>
    /// 암호 블록의 인증 형식. 0 = 0.2.22 이전(AAD 없음), 2 = 0.2.23~0.2.25(AAD 에 슬롯 3개를 고정 순회),
    /// 3 = 0.2.27 이후(AAD 에 내용·단축키·설정이 있는 슬롯만 넣어 슬롯 수와 무관),
    /// 4 = 0.2.39~0.2.40(3 + 슬롯마다 "브라우저에서도 Enter"). 읽기만 한다: 열리면 곧바로 3/5 로 다시 저장한다.
    /// 5 = 0.2.41 이후(3 + 슬롯마다 "브라우저에서는 Enter 보내지 않기").
    /// 새 저장은 <see cref="SaveSecretVersion"/>: 보통 3, "브라우저에서는 보내지 않기"를 켠 슬롯이 있을 때만 5.
    /// </summary>
    private int _secretVersion;
    public const int CurrentSecretVersion = 3;
    /// <summary>0.2.39~0.2.40 이 쓴 형식. 읽어서 옮기기만 한다.</summary>
    public const int LegacyBrowserEnterSecretVersion = 4;
    /// <summary>
    /// 5 = 3 과 같고, 슬롯마다 "브라우저에서는 Enter 보내지 않기" 값을 AAD 에 더 넣는다. 그 값을 켠 슬롯이 하나라도 있을 때만 이 형식으로 저장한다.
    /// 그래서 켜지 않은 사용자의 파일은 3 그대로라 이전 판과 서로 열 수 있고, 켠 파일은 이전 판이 "새 형식"으로 알아보고 열지 않는다
    /// (모르는 값을 조용히 버려 브라우저에서 Enter 가 나가지 않도록. T7 규칙).
    /// </summary>
    public const int NoBrowserEnterSecretVersion = 5;
    /// <summary>
    /// 6 = 5 와 같은 AAD 규칙(머리말만 v6) + 암호 블록 안에 사이트 채우기 연결 줄을 더한다(0.2.55). 연결이 하나라도 있을 때만 이 형식으로
    /// 저장한다. 이전 판은 6 을 "새 형식"으로 알아보고 열지 않는다: 모르는 줄을 손상으로 오해하거나 연결을 조용히 버리지 않는다.
    /// </summary>
    public const int SiteSecretVersion = 6;
    /// <summary>
    /// 7 = 6 과 같은 AAD 규칙(머리말만 v7) + 암호 블록 안에 두 번째 이후 입력("in|슬롯|번호|값")과 그 연결("insite|…") 줄(0.2.65).
    /// 입력이 둘 이상인 항목이 하나라도 있을 때만 이 형식으로 저장한다. 이전 판은 7 을 "새 형식"으로 알아보고 열지 않는다.
    /// </summary>
    public const int MultiInputSecretVersion = 7;
    /// <summary>8 = 7 + 슬롯마다 "칸을 직접 클릭하지 않기"(NoClick)를 머리말(s{i}.noclick)과 AAD 에 더한다(0.2.92). 켠 슬롯이 하나라도 있을 때만.
    /// 이전 판은 8 을 "새 형식"으로 알아보고 열지 않는다(모르는 값을 버려 클릭이 다시 켜지지 않도록).</summary>
    public const int NoClickSecretVersion = 8;
    /// <summary>9 = 8 + 암호 블록 안에 연결한 체크박스("anchor|…") 줄. 0.2.111 만 썼다. 지금은 읽기만 하고(줄 형식·주소 규칙을 그대로 검증),
    /// 그 연결은 버린다. 새로 쓰지 않는다(검증 전용 <see cref="TestSaveAs"/> 제외).</summary>
    public const int AnchorSecretVersion = 9;

    /// <summary>
    /// 저장할 때의 형식: 늘 <see cref="Argon2SecretVersion"/>(Argon2id + 형식 8 의 모든 줄·AAD 규칙). 예전처럼 기능에 따라 낮은 형식으로 저장해
    /// 예전 판이 열게 하던 규칙(T7)은 2026-10-05 사용자 결정(예전 판 호환 포기)으로 끝났다. 검증 전용 <see cref="TestSaveAs"/> 만 예전 형식을 쓴다.
    /// </summary>
    private int SaveSecretVersion() => _testForceVersion ?? Argon2SecretVersion;
    private int? _testForceVersion;
    private bool _secretInvalid;                            // secret= 값이 깨져 있다 (첫 실행이 아니라 손상)

    // ---------- 저장 위치 ----------
    public static string Dir
    {
        get
        {
            // 개발/검증용(ONEKEY_TEST=1 일 때만): 설정 폴더를 따로 지정해 실제 설정을 건드리지 않고 시험한다.
            if (Environment.GetEnvironmentVariable("ONEKEY_TEST") == "1")
            {
                string? alt = Environment.GetEnvironmentVariable("ONEKEY_CONFIG_DIR");
                if (!string.IsNullOrEmpty(alt)) return alt;
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "1Key");
        }
    }

    public static string FilePath => Path.Combine(Dir, "config.dat");

    // ---------- 문자열 이스케이프 ----------
    private static string Esc(string s)
        => s.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n");

    private static string Unesc(string s) => Unesc(s.AsSpan());

    /// <summary>이스케이프를 푼 새 문자열. 중간 글은 고정 배열에서 만들고 지운다(비밀번호 줄에도 쓴다).</summary>
    private static string Unesc(ReadOnlySpan<char> s)
    {
        if (s.IsEmpty) return string.Empty;
        char[] buf = GC.AllocateArray<char>(s.Length, pinned: true);
        int n = 0;
        try
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char c = s[++i];
                    buf[n++] = c switch { 'n' => '\n', 'r' => '\r', '\\' => '\\', _ => c };
                }
                else buf[n++] = s[i];
            }
            return new string(buf, 0, n);
        }
        finally { Array.Clear(buf); }
    }

    // ---------- 헤더 직렬화 (비밀번호 제외) ----------
    private string SerializeHeader(Kdf kdf, bool[] has, int secretVersion)
    {
        var sb = new StringBuilder();
        sb.Append("v=2\n");
        if (kdf.Argon2id) sb.Append("kdf=argon2id\nkdfm=").Append(kdf.MemoryKiB).Append("\nkdft=").Append(kdf.Passes).Append("\nkdfp=").Append(kdf.Lanes).Append('\n');
        else sb.Append($"kdf={kdf.Iterations}\n");
        sb.Append("secretv=").Append(secretVersion).Append('\n');   // secret 이 아래 SecurityAad() 로 묶여 있다
        sb.Append($"autostart={(AutoStart ? 1 : 0)}\n");
        sb.Append($"startmin={(StartMinimized ? 1 : 0)}\n");
        if (Walker) sb.Append("walker=1\n");
        sb.Append($"admin={(RequireAdmin ? 1 : 0)}\n");
        sb.Append($"keydelay={KeyDelayMs}\n");
        sb.Append($"predelay={PreDelayMs}\n");
        sb.Append($"autolock={AutoLockMinutes}\n");
        sb.Append($"confirmmods={ConfirmMods}\n");
        sb.Append($"confirmvk={ConfirmVk}\n");
        sb.Append($"theme={ThemeMode}\n");
        sb.Append($"launchnames={LaunchNames}\n");
        sb.Append($"lang={Language}\n");   // 옛 버전은 모르는 키를 무시한다
        for (int i = 0; i < SlotCount; i++)
        {
            Slot s = Slots[i];
            sb.Append($"s{i}.name={Esc(s.Name)}\n");
            sb.Append($"s{i}.has={(has[i] ? 1 : 0)}\n");
            sb.Append($"s{i}.mods={s.Mods}\n");
            sb.Append($"s{i}.vk={s.Vk}\n");
            sb.Append($"s{i}.method={(int)s.Method}\n");
            sb.Append($"s{i}.enter={(s.PressEnter ? 1 : 0)}\n");
            if (secretVersion >= NoBrowserEnterSecretVersion) sb.Append($"s{i}.noenterbrowser={(s.NoEnterInBrowser ? 1 : 0)}\n");
            if (secretVersion >= NoClickSecretVersion) sb.Append($"s{i}.noclick={(s.NoClick ? 1 : 0)}\n");
            if (has[i] && s.Form is >= 1 and <= 3) sb.Append($"s{i}.form={s.Form}\n");
        }
        return sb.ToString();
    }

    /// <summary>
    /// 헤더 가운데 보안에 영향을 주는 값들을 정해진 순서로 이어 붙인 것. AES-GCM 의 추가 인증 데이터(AAD)로 쓴다.
    /// 같은 계정의 다른 프로그램이 DPAPI 를 풀어 자동 잠금이나 단축키·Enter 설정만 바꿔 다시 감싸면, 마스터가 맞아도 복호화가 실패한다.
    /// (이름·시작 옵션·입력 지연은 보안과 무관하므로 넣지 않는다. 넣으면 잠긴 상태에서 바꿀 수 없는 값이 늘어난다.)
    /// </summary>
    /// <summary>
    /// version 2: 슬롯 0~2 를 무조건 넣는다 (그 시절 SlotCount 가 3 이었다). 그 파일을 열 때만 쓴다.
    /// version 3: 비어 있지 않은 슬롯만 넣는다. 그래서 SlotCount 를 바꿔도 AAD 가 달라지지 않는다 (Codex 검토 E1).
    ///            빈 슬롯을 끼워 넣거나 빼는 변조는 AAD 를 바꾸므로(있는 슬롯이 늘거나 줄면) 여전히 검출된다.
    /// version 4: 3 에 슬롯마다 "브라우저에서도 Enter"(옛 값) 를 더한다. 0.2.39~0.2.40 파일을 열 때만 쓴다.
    /// version 5: 3 에 슬롯마다 "브라우저에서는 Enter 보내지 않기" 를 더한다. 머리말이 달라 서로 섞이지 않는다.
    /// </summary>
    private byte[] SecurityAad(int version, Kdf kdf, bool[] has)
    {
        var sb = new StringBuilder();
        // 10: 키 유도 방식·매개변수 전체(argon2id,v=19,m,t,p)를 묶는다. 그 밖은 예전 그대로 반복 횟수
        sb.Append(version >= Argon2SecretVersion ? "1Key-aad-v10|kdf=" : version == 9 ? "1Key-aad-v9|kdf=" : version == 8 ? "1Key-aad-v8|kdf=" : version == 7 ? "1Key-aad-v7|kdf=" : version == 6 ? "1Key-aad-v6|kdf=" : version == 5 ? "1Key-aad-v5|kdf=" : version == 4 ? "1Key-aad-v4|kdf=" : version >= 3 ? "1Key-aad-v3|kdf=" : "1Key-aad-v2|kdf=").Append(kdf.AadText).Append("|autolock=").Append(AutoLockMinutes).Append("|admin=").Append(RequireAdmin ? 1 : 0);
        int n = version >= 3 ? SlotCount : Math.Min(3, SlotCount);
        for (int i = 0; i < n; i++)
        {
            Slot s = Slots[i];
            bool flag = version == 4 ? s.LegacyEnterInBrowser : version >= 5 && s.NoEnterInBrowser;
            bool noClick = version >= 8 && s.NoClick;
            bool empty = s.Mods == 0 && s.Vk == 0 && s.Method == InputMethod.Auto && !s.PressEnter && !flag && !noClick && !has[i];
            if (version >= 3 && empty) continue;
            sb.Append("|s").Append(i).Append('=').Append(s.Mods).Append(',').Append(s.Vk).Append(',').Append((int)s.Method).Append(',').Append(s.PressEnter ? 1 : 0).Append(',').Append(has[i] ? 1 : 0);
            if (version >= 4) sb.Append(',').Append(flag ? 1 : 0);
            if (version >= 8) sb.Append(',').Append(noClick ? 1 : 0);
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>
    /// 헤더를 읽는다 (T9). 돌려주는 값: null = 정상, 그 밖 = 손상 이유. 지원하지 않는 형식이면 <see cref="UnsupportedReason"/> 을 채운다.
    /// 규칙: v=2 만 지원(0.1.4 부터 모든 판이 v=2 를 썼다). secretv 는 없음(0)·2·3·4·5 만. 같은 키가 두 번 나오거나,
    /// 슬롯 번호가 표준 표기(s0, s12 — 앞자리 0 금지)가 아니거나 범위 밖이면 손상이다. 조용히 무시하거나 덮어쓰지 않는다.
    /// 모르는 일반 키·슬롯 필드는 무시한다(새 버전이 키를 더해도 옛 버전이 열 수 있도록).
    /// </summary>
    private string? DeserializeHeader(string text)
    {
        bool sawHas = false;
        string? version = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (line.Length == 0) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) return T.CorruptBadLine;
            string key = line[..eq];
            string val = line[(eq + 1)..];
            if (!seen.Add(key)) return T.CorruptDupKey(key);

            if (key == "v") { version = val; continue; }
            if (key == "secret")
            {
                try { _secretBlob = Convert.FromBase64String(val); } catch { _secretBlob = null; _secretInvalid = true; }
                continue;
            }

            // s{i}.{field}: i 는 한 자리일 수도 두 자리일 수도 있다 (s0.name, s12.vk). 예전 코드는 한 자리만 읽었다.
            int dot;
            if (key.Length > 3 && key[0] == 's' && char.IsAsciiDigit(key[1]) && (dot = key.IndexOf('.')) > 1)
            {
                ReadOnlySpan<char> num = key.AsSpan(1, dot - 1);
                bool canonical = num.Length <= 2 && (num.Length == 1 || num[0] != '0');
                foreach (char c in num) if (!char.IsAsciiDigit(c)) canonical = false;
                if (!canonical || !int.TryParse(num, out int idx)) return T.CorruptSlotFormat(key);
                if (idx >= SlotCount) return T.CorruptSlotRange(key);
                Slot s = Slots[idx];
                switch (key[(dot + 1)..])
                {
                    case "name": s.Name = Unesc(val); break;
                    case "has": s.HasContent = val == "1"; sawHas = true; break;
                    case "mods": s.Mods = ParseU(val) & 0xF; break;
                    case "vk": s.Vk = Math.Min(ParseU(val), 254u); break;
                    case "method": s.Method = (InputMethod)Math.Clamp((int)ParseU(val), 0, 4); break;
                    case "enter": s.PressEnter = val == "1"; break;
                    case "enterbrowser": s.LegacyEnterInBrowser = val == "1"; break;
                    case "noenterbrowser": s.NoEnterInBrowser = val == "1"; break;
                    case "noclick":   // 새 필드는 0/1 만(Codex 08:58): 다른 값은 손상
                        if (val is not ("0" or "1")) return T.CorruptBadValue(key);
                        s.NoClick = val == "1"; break;
                    case "form": s.Form = val is "1" or "2" or "3" ? val[0] - '0' : 0; break;   // 화면 모양만: 모르는 값은 예전 화면(0)
                }
                continue;
            }

            switch (key)
            {
                case "kdf":
                    if (val == "argon2id") _kdfArgon = true;
                    else _kdfIterations = Math.Clamp((int)Math.Min(ParseU(val), int.MaxValue), Crypto.MinIterations, Crypto.MaxIterations);
                    break;
                case "kdfm" or "kdft" or "kdfp":   // 형식 10 의 Argon2id 매개변수: 고치거나 기본값으로 바꾸지 않는다(Codex 15:19)
                    if (!CanonicalInt(val, out int kv)) return T.CorruptBadValue(key);
                    if (key == "kdfm") _kdfM = kv; else if (key == "kdft") _kdfT = kv; else _kdfP = kv;
                    _kdfFieldsSeen = true;
                    break;
                case "secretv":
                    if (!uint.TryParse(val, out uint sv)) return T.CorruptSecretvNaN;
                    if (sv is not (2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10)) { UnsupportedReason = $"secretv={val}"; return null; }
                    _secretVersion = (int)sv;
                    break;
                case "autostart": AutoStart = val == "1"; break;
                case "startmin": StartMinimized = val == "1"; break;
                case "walker": Walker = val == "1"; break;
                case "admin": RequireAdmin = val == "1"; break;
                case "keydelay": KeyDelayMs = Math.Clamp((int)ParseU(val), 0, 500); break;
                case "predelay": PreDelayMs = Math.Clamp((int)ParseU(val), 0, 3000); break;
                case "autolock": AutoLockMinutes = Math.Clamp((int)ParseU(val), 0, 240); break;
                case "confirmmods": ConfirmMods = ParseU(val) & 0xF; break;
                case "confirmvk": ConfirmVk = Math.Min(ParseU(val), 254u); break;
                case "theme": ThemeMode = (int)Math.Min(ParseU(val), 2u); break;
                case "launchnames": LaunchNames = (int)Math.Min(ParseU(val), 3u); break;
                case "lang": Language = L.Parse(val) is Lang lang ? L.Codes[(int)lang] : ""; break;   // 모르는 값 = Windows 따름
                // "locktray" 는 더 이상 쓰지 않는다(옛 설정 파일에 있어도 무시).
            }
        }
        if (version is null) return T.CorruptNoVersion;
        // 해당 형식이 아니면 AAD 가 이 값들을 묶지 않는다. 누가 끼워 넣은 값일 수 있으므로 쓰지 않는다.
        foreach (Slot s in Slots)
        {
            if (_secretVersion != LegacyBrowserEnterSecretVersion) s.LegacyEnterInBrowser = false;
            if (_secretVersion < NoBrowserEnterSecretVersion) s.NoEnterInBrowser = false;
            if (_secretVersion < NoClickSecretVersion) s.NoClick = false;
        }
        if (version != "2") { UnsupportedReason = $"v={version}"; return null; }
        // 형식 10 과 Argon2id 는 짝(Codex 15:19): 하나만 있으면 손상. 매개변수는 지원하는 정확한 프로파일만 — 다르면 계산·할당 전에 거절
        // (더 새 판의 다른 프로파일일 수 있어 "새 버전 필요"로, 형식이 깨진 값은 위에서 손상으로)
        if (_kdfArgon || _kdfFieldsSeen || _secretVersion == Argon2SecretVersion)
        {
            if (!_kdfArgon || _secretVersion != Argon2SecretVersion || _kdfM is not int km || _kdfT is not int kt || _kdfP is not int kp) return T.CorruptKdf;
            if (new Kdf(true, 0, km, kt, kp) != Kdf.Argon2Default) { UnsupportedReason = $"kdf=argon2id m={km} t={kt} p={kp}"; return null; }
        }
        // 0.2.20 이전 파일에는 has 가 없다: 이름이 있는 슬롯을 내용이 있는 것으로 본다 (다음 저장 때 정확해진다).
        if (!sawHas) foreach (Slot s in Slots) s.HasContent = s.Name.Length > 0;
        return null;
    }

    /// <summary>
    /// 이 버전이 모르는 새 형식의 파일이다(예: 더 새 1Key 가 저장). 열지도, 저장하지도, 새 마스터를 만들지도 않는다.
    /// 손상(<see cref="LoadFailed"/>)·첫 실행과 구분한다. null 이면 해당 없음.
    /// </summary>
    public string? UnsupportedReason { get; private set; }
    public bool Unsupported => UnsupportedReason is not null;

    /// <summary>파일이 손상되었다고 판단한 이유(헤더 규칙 위반 등). 안내에 쓴다.</summary>
    public string? CorruptReason { get; private set; }

    /// <summary>잠금 해제에서 마스터는 맞았지만 안의 내용이 형식에 맞지 않았다(손상). 틀린 마스터와 구분해 알린다.</summary>
    public bool PayloadInvalid { get; private set; }

    private static uint ParseU(string s) => uint.TryParse(s, out uint v) ? v : 0u;

    /// <summary>부호·앞자리 0·공백 없는 십진수(1–9자리). 아니면 false.</summary>
    private static bool CanonicalInt(string s, out int v)
    {
        v = 0;
        if (s.Length is 0 or > 9 || (s.Length > 1 && s[0] == '0')) return false;
        foreach (char c in s) if (!char.IsAsciiDigit(c)) return false;
        return int.TryParse(s, out v);
    }

    /// <summary>
    /// 저장된 화면 언어만 읽는다(창을 만들기 전의 안내 — 이미 실행 중 등 — 를 사용자가 고른 언어로 보이기 위해). 못 읽으면 빈 값(Windows 따름).
    /// 파일을 바꾸지 않는다.
    /// </summary>
    public static string PeekLanguage()
    {
        try { return Load().Language; }
        catch { return ""; }
    }

    // ---------- 로드 ----------
    public static Config Load()
    {
        var cfg = new Config();
        try
        {
            if (!File.Exists(FilePath)) return Defaults(cfg);   // 첫 실행
            byte[] blob = File.ReadAllBytes(FilePath);
            byte[]? plain = Dpapi.Unprotect(blob);
            if (plain is null) { cfg = Defaults(new Config()); cfg.LoadFailed = true; return cfg; }   // 손상/다른 사용자
            string? corrupt = cfg.DeserializeHeader(Encoding.UTF8.GetString(plain));
            Array.Clear(plain);
            if (cfg.UnsupportedReason is string why)
            {
                // 새 형식: 헤더 값(자동 실행·관리자 권한 등)을 믿고 쓰지 않도록 기본값으로 두고 "미지원"만 표시한다.
                cfg = Defaults(new Config()); cfg.UnsupportedReason = why; return cfg;
            }
            // 파일이 있는데 암호 블록이 깨졌거나(Base64 오류) 아예 없거나 너무 짧으면 첫 실행이 아니라 손상이다.
            // (Save 는 내용이 비어 있어도 secret= 을 반드시 쓴다. 그러니 "파일 없음"만 첫 실행이다.)
            if (corrupt is null && (cfg._secretInvalid || cfg._secretBlob is null || cfg._secretBlob.Length < Crypto.MinBlobLength))
                corrupt = T.CorruptSecret;
            if (corrupt is not null)
            { cfg = Defaults(new Config()); cfg.LoadFailed = true; cfg.CorruptReason = corrupt; return cfg; }
            cfg.HasMaster = true;
        }
        catch
        {
            // 백신·백업이 파일을 잡고 있는 순간 등. 첫 실행처럼 보이게 하면 사용자가 새 마스터를 만들며 파일을 덮어쓴다.
            cfg = Defaults(new Config()); cfg.LoadFailed = true; return cfg;
        }
        return cfg;
    }

    private static Config Defaults(Config cfg)
    {
        // 처음 실행: 앞의 세 슬롯에 Ctrl+Alt+1 / 2 / 3 (내용이 없으므로 등록되지는 않고, 편집 화면에 미리 채워질 뿐이다)
        for (int i = 0; i < Math.Min(3, SlotCount); i++)
        {
            cfg.Slots[i].Mods = 2 | 1;                 // Ctrl+Alt
            cfg.Slots[i].Vk = (uint)('1' + i);         // '1','2','3'
        }
        cfg.HasMaster = false;
        return cfg;
    }

    // ---------- 마스터 비밀번호 / 잠금 ----------

    /// <summary>
    /// 10 = 형식 8 의 모든 줄·AAD 규칙(사이트·입력 여럿·프로그램 연결·브라우저 Enter·NoClick) + 키 유도 Argon2id(<see cref="Kdf.Argon2Default"/>).
    /// 이 판은 늘 이 형식으로 저장한다. 예전 판은 10 을 "새 버전 필요"로 거절하고 파일을 바꾸지 않는다
    /// (2026-10-05 사용자 결정: 예전 판 호환 포기, 처음 잠금 해제 때 자동으로 옮기고 한 번 알림. Codex 15:19 조건부 수용).
    /// </summary>
    public const int Argon2SecretVersion = 10;

    /// <summary>방금 잠금 해제에서 예전 방식(PBKDF2) 파일을 Argon2id 로 옮겨 저장했다 — 화면이 한 번 알리고 <see cref="AckKdfUpgraded"/>.</summary>
    public bool KdfUpgraded { get; private set; }
    public void AckKdfUpgraded() => KdfUpgraded = false;

    /// <summary>파일 헤더가 말하는 키 유도 방식(형식 10 이면 헤더 검사에서 지원 프로파일임을 확인했다).</summary>
    private Kdf FileKdf => _kdfArgon ? Kdf.Argon2Default : Kdf.Pbkdf2(_kdfIterations);

    /// <summary>
    /// 새 마스터의 키를 유도한다(새 salt, Argon2id). 다른 스레드에서 불러도 된다(설정 상태를 읽지 않음). 실패(메모리 부족·암호 API 오류)면 null —
    /// 프로세스를 끝내지 않고 사용자에게 알린다(Codex 16:22 R39-3).
    /// </summary>
    internal static (byte[] Key, byte[] Salt)? DeriveNewMaster(ReadOnlySpan<char> master)
    {
        try
        {
            byte[] salt = Crypto.NewSalt();
            return (Crypto.DeriveKey(master, salt, Kdf.Argon2Default), salt);
        }
        catch { return null; }
    }

    /// <summary>
    /// 지금 잠금 해제된 세션의 키·salt 사본(UI 스레드). 쓰던 PC 에서 잠금을 푼 채 복원할 때 지금 마스터를 그대로 쓰기 위함(2026-10-05 사용자) —
    /// 복원한 설정을 같은 키로 다시 암호화한다(저장마다 새 nonce, 마스터를 바꾸지 않은 저장과 같다). Argon2 기본 프로파일 키가 아니면 null.
    /// 받은 쪽이 소유하고(CommitCreate 로 넘기거나 지운다), 사본은 고정 버퍼다.
    /// </summary>
    internal (byte[] Key, byte[] Salt)? CopyCurrentKey()
    {
        if (!IsUnlocked || !HasMaster || _key is null || _keySalt is null || _keyKdf != Kdf.Argon2Default) return null;
        byte[] salt = (byte[])_keySalt.Clone();   // salt 먼저(비밀 아님), 키 사본은 마지막에(Codex 23:06 R68-2)
        byte[]? k = null;
        try
        {
            k = GC.AllocateArray<byte>(_key.Length, pinned: true);
            if (TestFailCopyKey) { TestFailCopyKey = false; throw new OutOfMemoryException("test: copy key"); }
            _key.CopyTo(k, 0);
            return (k, salt);
        }
        catch
        {
            if (k is not null) { Crypto.Wipe(k); TestCopyWiped++; }   // 넘기지 못한 사본만 지운다(원본 세션 키는 그대로)
            throw;
        }
    }

    /// <summary>검증 전용: 다음 한 번의 키 복사가 사본을 만든 뒤 예외(KY11). 지운 사본 수.</summary>
    internal static bool TestFailCopyKey;
    internal static int TestCopyWiped;

    /// <summary>미리 유도한 새 마스터 키로 잠금 해제 상태가 된다(UI 스레드). 키는 이제 이 설정이 소유한다.</summary>
    public void CommitCreate((byte[] Key, byte[] Salt) k)
    {
        SetKey(k.Key, k.Salt, Kdf.Argon2Default);
        IsUnlocked = true;
        HasMaster = true;   // 저장 시 secret 블록이 만들어진다.
    }

    /// <summary>첫 실행에서 마스터 비밀번호를 정한다(같은 스레드에서 유도 + 반영). 유도에 실패하면 false(상태 그대로).</summary>
    public bool CreateMaster(ReadOnlySpan<char> master)
    {
        if (DeriveNewMaster(master) is not { } k) return false;
        CommitCreate(k);
        return true;
    }

    /// <summary>
    /// 잠금 해제 한 번의 계산: 파일 방식으로 키 유도 → 복호화 → 해석 → (형식 10 이 아니면) 같은 마스터에서 새 salt 로 Argon2id 키 유도까지.
    /// <see cref="BeginUnlock"/> 이 UI 스레드에서 필요한 값을 복사해 만들고, <see cref="Run"/> 은 다른 스레드에서 돌려도 된다(설정 객체를 건드리지
    /// 않음). 결과는 <see cref="FinishUnlock"/> 이 UI 스레드에서 한 번에 반영한다 — 반영하지 않으면 Dispose 로 키를 지운다.
    /// </summary>
    internal sealed class UnlockJob : IDisposable
    {
        private readonly byte[] _blob, _salt, _aad;
        private readonly int _version;
        private readonly Kdf _fileKdf;
        internal byte[]? NewKey, NewSalt;
        internal Kdf NewKdf;
        internal Parsed? Result;
        internal bool Wrong, Invalid, Failed, Upgrade, KdfChange;
        internal readonly byte[] Blob;

        internal UnlockJob(byte[] blob, byte[] salt, int version, Kdf fileKdf, byte[] aad)
        { _blob = blob; Blob = blob; _salt = salt; _version = version; _fileKdf = fileKdf; _aad = aad; }

        public void Run(ReadOnlySpan<char> master)
        {
            byte[]? key = null, plain = null;
            SecretText? text = null;
            try
            {
                key = Crypto.DeriveKey(master, _salt, _fileKdf);
                plain = Crypto.Decrypt(_blob, key, _aad);
                if (plain is null) { Wrong = true; return; }
                text = SecretText.FromUtf8(plain);
                Crypto.Wipe(plain); plain = null;
                // 마스터는 맞았는데 안의 줄이 형식에 맞지 않는다: 손상이다. 남는 줄을 조용히 버리지 않는다 (T9).
                Result = _version >= SiteSecretVersion ? ParseV6(text.Span, _version >= MultiInputSecretVersion, _version == AnchorSecretVersion) : ParseOld(text.Span);
                if (Result is null) { Invalid = true; return; }
                Upgrade = _version != Argon2SecretVersion;
                KdfChange = !_fileKdf.Argon2id;
                if (Upgrade)
                {
                    // 새 방식의 키는 마스터에서 새로 유도한다(예전 키를 Argon2id 의 입력으로 쓰지 않는다 — Codex 15:19)
                    NewSalt = Crypto.NewSalt();
                    NewKey = Crypto.DeriveKey(master, NewSalt, Kdf.Argon2Default);
                    NewKdf = Kdf.Argon2Default;
                }
                else { NewKey = key; NewSalt = _salt; NewKdf = _fileKdf; key = null; }
            }
            catch { Failed = true; Result = null; Crypto.Wipe(NewKey); NewKey = null; }   // 메모리 부족·암호 API 오류: 잠긴 채로(R39-3)
            finally { Crypto.Wipe(key); Crypto.Wipe(plain); text?.Dispose(); }
        }

        /// <summary>반영하지 않은 키를 지운다(늦게 끝난 계산·실패).</summary>
        public void Dispose() { Crypto.Wipe(NewKey); NewKey = null; Result = null; }
    }

    /// <summary>잠금 해제 계산에 필요한 값을 복사한다(UI 스레드). 암호 블록이 없으면 null.</summary>
    internal UnlockJob? BeginUnlock()
    {
        // 암호 블록이 없으면 검증할 수 없다. 마스터는 CreateMaster 로만 만든다 (예전에는 여기서 아무 값이나 받아들였다).
        if (_secretBlob is null || Crypto.SaltOf(_secretBlob) is not byte[] salt) return null;
        Kdf fileKdf = FileKdf;
        // 파일이 적힌 그 형식의 규칙으로 AAD 를 다시 만든다 (0.2.22 이전 = AAD 없음, v2 는 슬롯 3개 고정, v3 이후는 비어 있지 않은 슬롯만).
        bool[] has = Slots.Select(s => s.HasContent).ToArray();
        return new UnlockJob(_secretBlob, salt, _secretVersion, fileKdf, _secretVersion >= 2 ? SecurityAad(_secretVersion, fileKdf, has) : Array.Empty<byte>());
    }

    /// <summary>계산이 실패했다(메모리 부족·암호 API 오류) — 틀린 마스터와 구분해 알린다.</summary>
    public bool UnlockFailed { get; private set; }

    /// <summary>
    /// 계산 결과를 한 번에 반영한다(UI 스레드): 해석 결과·키가 모두 준비됐을 때만 슬롯·키·잠금 해제 상태를 바꾼다(Codex 16:22 R39-3).
    /// 그사이 파일이 바뀌었으면(다른 저장) 반영하지 않는다. 형식 10 이 아니면 바로 형식 10 으로 다시 저장 — 실패해도 잠금 해제는 유효하고
    /// 원본 파일은 그대로이며(원자적 교체), 다음 저장 때 같은 새 키로 다시 시도된다(<see cref="MigrationFailed"/>).
    /// </summary>
    internal bool FinishUnlock(UnlockJob job)
    {
        PayloadInvalid = job.Invalid;
        UnlockFailed = job.Failed;
        if (job.Result is null || job.NewKey is null || job.NewSalt is null || !ReferenceEquals(job.Blob, _secretBlob) || IsUnlocked) { job.Dispose(); return false; }
        job.Result.CommitTo(this);
        SetKey(job.NewKey, job.NewSalt, job.NewKdf);
        job.NewKey = null;   // 이제 이 설정이 소유한다
        IsUnlocked = true;
        if (job.Upgrade)
        {
            MigrationFailed = !Save();
            if (!MigrationFailed && job.KdfChange) KdfUpgraded = true;
        }
        return true;
    }

    /// <summary>마스터 비밀번호로 잠금을 푼다(같은 스레드에서 계산 + 반영). 성공하면 true.</summary>
    public bool Unlock(ReadOnlySpan<char> master)
    {
        PayloadInvalid = false; UnlockFailed = false;
        if (BeginUnlock() is not UnlockJob job) return false;
        job.Run(master);
        return FinishUnlock(job);
    }

    /// <summary>복호화한 내용을 해석한 결과. 모두 맞을 때만 슬롯에 넣는다(Codex 15:19: 실패하면 새 상태를 게시하지 않는다).</summary>
    internal sealed class Parsed
    {
        public readonly string[] Passwords = new string[SlotCount];
        public readonly SiteLink?[] Sites = new SiteLink?[SlotCount];
        public readonly List<ExtraInput>[] More = new List<ExtraInput>[SlotCount];
        public readonly AppLink?[] Apps = new AppLink?[SlotCount];

        public void CommitTo(Config c)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                Slot s = c.Slots[i];
                s.Password = Passwords[i] ?? string.Empty;
                s.HasContent = s.HasPassword;
                s.Site = Sites[i]; s.More = More[i] ?? new(); s.App = Apps[i];
                s.Anchor = null;   // 형식 9 의 체크박스 연결은 검증만 하고 버린다(0.2.112)
            }
        }
    }

    /// <summary>줄의 범위 목록(Split('\n') 과 같은 개수) — 줄 문자열을 만들지 않는다.</summary>
    private static List<Range> Lines(ReadOnlySpan<char> s)
    {
        var r = new List<Range>();
        int start = 0;
        for (int i = 0; i <= s.Length; i++)
            if (i == s.Length || s[i] == '\n') { r.Add(new Range(start, i)); start = i + 1; }
        return r;
    }

    /// <summary>형식 5 이하: 슬롯 수 이하의 비밀번호 줄뿐. 줄이 슬롯 수보다 많으면 손상(null).</summary>
    private static Parsed? ParseOld(ReadOnlySpan<char> payload)
    {
        var lines = Lines(payload);
        if (lines.Count > SlotCount) return null;
        var p = new Parsed();
        for (int i = 0; i < SlotCount; i++) p.Passwords[i] = i < lines.Count ? Unesc(payload[lines[i]]) : string.Empty;
        return p;
    }

    /// <summary>
    /// 암호 블록 v6: 슬롯 수만큼의 비밀번호 줄(늘 전부) 뒤에, 연결마다 한 줄
    /// "site|슬롯|주소|ID|이름|비밀번호칸(0/1)|순서". 각 칸은 \, 줄바꿈, | 를 이스케이프한다.
    /// v7(multi): 두 번째 이후 입력 "in|슬롯|번호(2..MaxInputs)|값" 과 그 연결 "insite|슬롯|번호|주소|ID|이름|0/1|순서" 를 더한다.
    /// 번호는 슬롯마다 2 부터 빠짐없이, 값은 비어 있지 않고 첫 입력도 있어야 한다. 하나라도 어긋나면 손상(null — 조용히 버리지 않는다).
    /// </summary>
    private static Parsed? ParseV6(ReadOnlySpan<char> payload, bool multi, bool anchors)
    {
        var lines = Lines(payload);
        if (lines.Count < SlotCount) return null;
        var p = new Parsed();
        var values = new string?[SlotCount, Slot.MaxInputs];
        var moreSites = new SiteLink?[SlotCount, Slot.MaxInputs];
        var anchorLinks = new SiteLink?[SlotCount];
        static SiteLink? Link(string[] f, int at)
        {
            if (!int.TryParse(f[at + 4], out int index) || index < 0 || f[at + 3] is not ("0" or "1")) return null;
            string? url = SiteUrl.Normalize(f[at]);
            if (url is null || url != f[at]) return null;
            string cls = f.Length > at + 5 ? f[at + 5] : "";   // v7: 마지막 칸 = 찾는 class("*" = 그 종류의 유일한 칸)
            if (cls.Length > 0 && (f[at + 1].Length > 0 || f[at + 2].Length > 0)) return null;   // class 는 id·이름이 없을 때만 쓴다
            return new SiteLink(url, f[at + 1], f[at + 2], f[at + 3] == "1", index, cls);
        }
        for (int k = SlotCount; k < lines.Count; k++)
        {
            string[] f = SplitSite(payload[lines[k]]);
            if (f.Length == 0) return null;
            if (anchors && f[0] == "anchor")
            {
                // anchor|슬롯|주소|id|이름|0|순서|class — 체크박스이므로 종류는 늘 0. 슬롯마다 하나.
                if (f.Length != 8 || !int.TryParse(f[1], out int slot) || slot < 0 || slot >= SlotCount || f[1] != slot.ToString() || anchorLinks[slot] is not null || f[5] != "0") return null;
                if ((anchorLinks[slot] = Link(f, 2)) is null) return null;
            }
            else if (f[0] == "site")
            {
                if (!(f.Length == 7 || (multi && f.Length == 8)) || !int.TryParse(f[1], out int slot) || slot < 0 || slot >= SlotCount || p.Sites[slot] is not null) return null;
                if ((p.Sites[slot] = Link(f, 2)) is null) return null;
            }
            else if (multi && f[0] == "in")
            {
                if (f.Length != 4 || !int.TryParse(f[1], out int slot) || slot < 0 || slot >= SlotCount) return null;
                if (!int.TryParse(f[2], out int n) || n < 2 || n > Slot.MaxInputs || f[2] != n.ToString() || values[slot, n - 1] is not null || f[3].Length == 0) return null;
                values[slot, n - 1] = f[3];
            }
            else if (multi && f[0] == "app")
            {
                // app|슬롯|실행 파일|창 클래스|칸 클래스|순서|제목[|자리x|자리y|바탕 클래스|바탕 너비|바탕 높이] (자리는 0.2.80 부터)
                if (f.Length is not (7 or 12) || !int.TryParse(f[1], out int slot) || slot < 0 || slot >= SlotCount || p.Apps[slot] is not null) return null;
                if (!int.TryParse(f[5], out int order) || order < 0 || f[5] != order.ToString() || f[2].Length == 0 || f[3].Length == 0 || f[4].Length == 0) return null;
                int dx = 0, dy = 0, hw = 0, hh = 0;
                if (f.Length == 12 && !(int.TryParse(f[7], out dx) && int.TryParse(f[8], out dy) && int.TryParse(f[10], out hw) && int.TryParse(f[11], out hh) && hw >= 0 && hh >= 0)) return null;
                p.Apps[slot] = new AppLink(f[2], f[3], f[4], order, f[6], dx, dy, f.Length == 12 ? f[9] : "", hw, hh);
            }
            else if (multi && f[0] == "insite")
            {
                if (f.Length is not (8 or 9) || !int.TryParse(f[1], out int slot) || slot < 0 || slot >= SlotCount) return null;
                if (!int.TryParse(f[2], out int n) || n < 2 || n > Slot.MaxInputs || f[2] != n.ToString() || moreSites[slot, n - 1] is not null) return null;
                if ((moreSites[slot, n - 1] = Link(f, 3)) is null) return null;
            }
            else return null;
        }
        for (int i = 0; i < SlotCount; i++)
        {
            p.More[i] = new();
            bool gap = false;
            for (int n = 2; n <= Slot.MaxInputs; n++)
            {
                string? v = values[i, n - 1];
                if (v is null) { gap = true; if (moreSites[i, n - 1] is not null) return null; continue; }   // 입력 없는 연결
                if (gap) return null;   // 번호가 빠졌다
                p.More[i].Add(new ExtraInput(v, moreSites[i, n - 1]));
            }
        }
        for (int i = 0; i < SlotCount; i++) p.Passwords[i] = Unesc(payload[lines[i]]);
        for (int i = 0; i < SlotCount; i++)
        {
            bool hasPw = p.Passwords[i].Length > 0;
            if (p.More[i].Count > 0 && !hasPw) return null;   // 첫 입력 없이 뒤 입력만 있을 수 없다
            if (p.Apps[i] is not null && (p.Sites[i] is not null || !hasPw)) return null;   // 프로그램 연결은 웹 연결과 함께 쓰지 않는다
            if (anchorLinks[i] is SiteLink an && (p.Sites[i] is not SiteLink s1 || s1.Url != an.Url)) return null;   // 체크박스는 첫 입력의 웹 연결과 같은 주소일 때만
        }
        return p;
    }

    /// <summary>들고 있는 키를 바꾼다(예전 키는 0 으로 덮는다). 마스터에서 새로 유도하는 판.</summary>
    private void DeriveAndSetKey(ReadOnlySpan<char> master, byte[] salt, Kdf kdf) => SetKey(Crypto.DeriveKey(master, salt, kdf), salt, kdf);

    private void SetKey(byte[]? key, byte[]? salt, Kdf kdf)
    {
        if (!ReferenceEquals(_key, key)) Crypto.Wipe(_key);
        _key = key; _keySalt = salt; _keyKdf = kdf;
    }

    /// <summary>잠금 해제 중에 입력한 값이 지금 마스터와 같은가: 들고 있는 salt·방식으로 다시 유도해 키를 일정 시간 비교한다(유도 한 번만큼 걸린다).</summary>
    public bool IsCurrentMaster(ReadOnlySpan<char> candidate)
    {
        if (!IsUnlocked || _key is null || _keySalt is null) return false;
        try
        {
            byte[] k = Crypto.DeriveKey(candidate, _keySalt, _keyKdf);
            try { return Crypto.SameKey(k, _key); }
            finally { Crypto.Wipe(k); }
        }
        catch { return false; }
    }

    /// <summary>
    /// 마스터 바꾸기 한 번의 계산: 현재 마스터 확인(유도 1회) + 새 마스터 키(새 salt, Argon2id) 유도 — 확인을 두 번 하지 않는다(R39-3).
    /// <see cref="BeginChange"/> 가 지금 키의 사본을 만들어 주고, <see cref="Run"/> 은 다른 스레드에서 돌려도 된다. 반영은 <see cref="FinishChange"/>.
    /// </summary>
    internal sealed class ChangeJob : IDisposable
    {
        private byte[]? _curKey;   // 비교용 사본(고정 배열) — Run 이 끝나면 지운다
        private readonly byte[] _curSalt;
        private readonly Kdf _curKdf;
        internal readonly byte[] CurRef;   // 반영할 때 그사이 키가 바뀌지 않았는지(잠금·다른 바꾸기) 확인
        internal byte[]? NewKey, NewSalt;
        internal bool Wrong, Failed;

        internal ChangeJob(byte[] cur, byte[] salt, Kdf kdf)
        {
            CurRef = cur;
            _curKey = GC.AllocateArray<byte>(cur.Length, pinned: true);
            cur.CopyTo(_curKey, 0);
            _curSalt = salt; _curKdf = kdf;
        }

        public void Run(ReadOnlySpan<char> current, ReadOnlySpan<char> next)
        {
            byte[]? k = null;
            try
            {
                k = Crypto.DeriveKey(current, _curSalt, _curKdf);
                if (!Crypto.SameKey(k, _curKey!)) { Wrong = true; return; }
                NewSalt = Crypto.NewSalt();
                NewKey = Crypto.DeriveKey(next, NewSalt, Kdf.Argon2Default);
            }
            catch { Failed = true; Crypto.Wipe(NewKey); NewKey = null; }
            finally { Crypto.Wipe(k); Crypto.Wipe(_curKey); _curKey = null; }
        }

        /// <summary>현재 마스터 확인만(백업 파일 만들기 — 새 키는 만들지 않는다).</summary>
        public void RunVerify(ReadOnlySpan<char> current)
        {
            byte[]? k = null;
            try { k = Crypto.DeriveKey(current, _curSalt, _curKdf); if (!Crypto.SameKey(k, _curKey!)) Wrong = true; }
            catch { Failed = true; }
            finally { Crypto.Wipe(k); Crypto.Wipe(_curKey); _curKey = null; }
        }

        public void Dispose() { Crypto.Wipe(NewKey); NewKey = null; Crypto.Wipe(_curKey); _curKey = null; }
    }

    // ---------- 백업 파일(내보내기·복원, Backup.cs) ----------

    /// <summary>
    /// 백업에 넣을 내용(UI 스레드, 잠금 해제 중): 설정 머리(형식 10 규칙, secret 줄 없음)의 UTF-8 과 암호 블록 평문(소유한 고정 버퍼).
    /// 부르는 쪽이 둘 다 지운다(Crypto.Wipe / Dispose).
    /// </summary>
    internal (byte[] Settings, SecretText Secrets)? ExportForBackup()
    {
        if (!IsUnlocked) return null;
        bool[] has = Slots.Select(s => s.HasPassword).ToArray();
        string header = SerializeHeader(Kdf.Argon2Default, has, Argon2SecretVersion);
        byte[] settings = GC.AllocateArray<byte>(Encoding.UTF8.GetByteCount(header), pinned: true);
        Encoding.UTF8.GetBytes(header, settings);
        var secrets = new SecretText(1024);
        BuildPasswordPayload(Argon2SecretVersion, secrets);
        return (settings, secrets);
    }

    /// <summary>
    /// 백업 내용으로 새 설정(잠긴 상태, 키 없음)을 만든다. 머리는 형식 10 규칙 그대로 검사하고(손상·미지원·다른 형식 = null), 암호 블록 평문도
    /// 형식 10 규칙으로 해석한다(줄 하나라도 어긋나면 null). 부르는 쪽이 CommitCreate 로 새 키를 준 뒤 SaveTo.
    /// </summary>
    internal static Config? FromBackup(byte[] settingsUtf8, ReadOnlySpan<char> secrets)
    {
        var c = new Config();
        string text;
        try { text = new UTF8Encoding(false, true).GetString(settingsUtf8); } catch { return null; }
        if (c.DeserializeHeader(text) is not null || c.Unsupported || c._secretVersion != Argon2SecretVersion || c._secretBlob is not null) return null;
        Parsed? p = ParseV6(secrets, true, false);
        if (p is null) return null;
        p.CommitTo(c);
        return c;
    }

    private string? _saveTarget;

    /// <summary>다른 경로에 저장한다(복원의 *.restore). 상태 갱신은 Save 와 같다(복원용 설정 객체는 쓰고 버린다).</summary>
    internal bool SaveTo(string path)
    {
        _saveTarget = path;
        try { return Save(); }
        finally { _saveTarget = null; }
    }

    /// <summary>마스터 바꾸기 계산을 준비한다(UI 스레드). 잠겨 있으면 null.</summary>
    internal ChangeJob? BeginChange() => IsUnlocked && _key is not null && _keySalt is not null ? new ChangeJob(_key, _keySalt, _keyKdf) : null;

    /// <summary>
    /// 마스터 바꾸기를 반영한다(UI 스레드, T3): 새 키로 전체를 다시 암호화해 원자적으로 저장하고, **파일에 들어간 뒤에만** 메모리의 키를 바꾸고
    /// 예전 키를 지운다. 틀린 현재 마스터·계산 실패·그사이 잠김이면 아무것도 바꾸지 않는다. 실패하면 예전 키·파일·잠금 상태 그대로.
    /// </summary>
    internal bool FinishChange(ChangeJob job)
    {
        LastSaveError = null;
        if (job.Wrong) { job.Dispose(); LastSaveError = "wrong current master"; LastSaveFail = SaveFail.Other; return false; }
        if (job.Failed || job.NewKey is null || job.NewSalt is null || !IsUnlocked || !ReferenceEquals(_key, job.CurRef))
        { job.Dispose(); LastSaveError = job.Failed ? "key derivation failed" : "state changed"; LastSaveFail = SaveFail.Other; return false; }
        byte[] oldKey = _key!, oldSalt = _keySalt!;
        Kdf oldKdf = _keyKdf;
        _key = job.NewKey; _keySalt = job.NewSalt; _keyKdf = Kdf.Argon2Default;
        job.NewKey = null;   // 이제 이 설정이 소유하거나(성공) 아래에서 지운다(실패)
        if (Save()) { Crypto.Wipe(oldKey); return true; }
        Crypto.Wipe(_key);
        _key = oldKey; _keySalt = oldSalt; _keyKdf = oldKdf;   // Save 는 실패하면 파일·암호문·형식 상태를 바꾸지 않는다
        return false;
    }

    /// <summary>마스터 비밀번호를 바꾼다(같은 스레드에서 계산 + 반영). 틀린 현재 마스터면 false + LastSaveError.</summary>
    public bool ChangeMaster(ReadOnlySpan<char> current, ReadOnlySpan<char> next)
    {
        if (BeginChange() is not ChangeJob job) { LastSaveError = "locked"; LastSaveFail = SaveFail.Other; return false; }
        job.Run(current, next);
        return FinishChange(job);
    }

    /// <summary>마스터를 만들었지만 저장에 실패했을 때 되돌린다 (다시 만들기 화면으로).</summary>
    public void DiscardMaster()
    {
        SetKey(null, null, default);
        IsUnlocked = false;
        HasMaster = _secretBlob is not null;
    }

    /// <summary>
    /// 메모리에서 비밀번호와 키를 지우고 다시 잠근다. 키(소유한 고정 배열)는 0 으로 덮는다. 슬롯의 비밀번호 문자열은 참조만 버린다 —
    /// 문자열은 바꾸지 않는 값이고 입력 스레드 등이 아직 읽고 있을 수 있어 덮지 않는다(Codex 15:19 S36-1). 그 문자열과 입력칸·런타임의 사본은
    /// 메모리에 남을 수 있다.
    /// </summary>
    public void Lock()
    {
        foreach (Slot s in Slots) { s.Password = string.Empty; s.Site = null; s.More = new(); s.App = null; s.Anchor = null; }
        SetKey(null, null, default);
        IsUnlocked = false;
    }

    /// <summary>연결 줄을 | 로 나누고 각 칸의 이스케이프를 푼다.</summary>
    /// 허용 이스케이프는 \\ \n \r \p 뿐이다. 모르는 이스케이프·끝의 홀로 남은 \ 는 형식 오류(빈 배열 → 손상, Codex V55-8).
    /// 칸을 모으는 중간 글은 고정 배열에서 만들고 지운다(입력 값 "in|…" 도 이 길로 온다).
    private static string[] SplitSite(ReadOnlySpan<char> line)
    {
        var parts = new List<string>();
        char[] buf = GC.AllocateArray<char>(Math.Max(line.Length, 1), pinned: true);
        int n = 0;
        try
        {
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '|') { parts.Add(new string(buf, 0, n)); Array.Clear(buf, 0, n); n = 0; continue; }
                if (c == '\\')
                {
                    if (i + 1 >= line.Length) return Array.Empty<string>();
                    char e = line[++i];
                    if (e is not ('\\' or 'n' or 'r' or 'p')) return Array.Empty<string>();
                    buf[n++] = e switch { 'n' => '\n', 'r' => '\r', 'p' => '|', _ => '\\' };
                    continue;
                }
                buf[n++] = c;
            }
            parts.Add(new string(buf, 0, n));
            return parts.ToArray();
        }
        finally { Array.Clear(buf); }
    }

    /// <summary>검증 전용: 암호 블록 v6 의 해석만 시험한다(복호화 없이). 받아들이면 true.</summary>
    internal static bool SelfTestApplyV6(string payload) { var c = new Config(); var p = ParseV6(payload, false, false); p?.CommitTo(c); return p is not null; }
    /// <summary>검증 전용: 암호 블록 v7(입력 여러 개) 해석. 받아들이면 그 설정을, 아니면 null.</summary>
    internal static Config? SelfTestApplyV7(string payload) { var c = new Config(); var p = ParseV6(payload, true, false); p?.CommitTo(c); return p is null ? null : c; }
    /// <summary>검증 전용: 암호 블록 v9(연결한 체크박스) 해석. 받아들이면 그 설정을, 아니면 null.</summary>
    internal static Config? SelfTestApplyV9(string payload) { var c = new Config(); var p = ParseV6(payload, true, true); p?.CommitTo(c); return p is null ? null : c; }

    /// <summary>
    /// 검증 전용: 지금 내용을 예전 형식(version 2–9, PBKDF2 600,000회, 주어진 마스터)으로 저장한다 — 0.2.x·0.3.36 이하가 쓴 파일을 만들어
    /// 열기·옮기기를 시험한다. 들고 있는 키·방식은 그대로 되돌린다.
    /// </summary>
    internal bool TestSaveAs(int version, string master)
    {
        if (!IsUnlocked || version >= Argon2SecretVersion) return false;
        byte[]? k0 = _key, s0 = _keySalt;
        Kdf kd0 = _keyKdf;
        byte[] salt = Crypto.NewSalt();
        _key = Crypto.DeriveKey(master, salt, Kdf.Pbkdf2(Crypto.DefaultIterations)); _keySalt = salt; _keyKdf = Kdf.Pbkdf2(Crypto.DefaultIterations);
        _testForceVersion = version;
        try { return Save(); }
        finally { Crypto.Wipe(_key); _key = k0; _keySalt = s0; _keyKdf = kd0; _testForceVersion = null; }
    }

    private static void EscInto(SecretText sb, string s)
    {
        foreach (char c in s)
            switch (c)
            {
                case '\\': sb.Append('\\'); sb.Append('\\'); break;
                case '\r': sb.Append('\\'); sb.Append('r'); break;
                case '\n': sb.Append('\\'); sb.Append('n'); break;
                default: sb.Append(c); break;
            }
    }

    private static void EscSiteInto(SecretText sb, string s)
    {
        foreach (char c in s)
            switch (c)
            {
                case '\\': sb.Append('\\'); sb.Append('\\'); break;
                case '\n': sb.Append('\\'); sb.Append('n'); break;
                case '\r': sb.Append('\\'); sb.Append('r'); break;
                case '|': sb.Append('\\'); sb.Append('p'); break;
                default: sb.Append(c); break;
            }
    }

    /// <summary>저장할 평문을 소유한 고정 버퍼(sb)에 쓴다 — 중간 문자열을 만들지 않는다.</summary>
    private void BuildPasswordPayload(int version, SecretText sb)
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (i > 0) sb.Append('\n');
            EscInto(sb, Slots[i].Password);
        }
        if (version >= SiteSecretVersion)
            for (int i = 0; i < SlotCount; i++)
                if (Slots[i].Site is SiteLink l)
                {
                    sb.Append("\nsite|"); sb.Append(i); sb.Append('|'); EscSiteInto(sb, l.Url); sb.Append('|'); EscSiteInto(sb, l.FieldId); sb.Append('|');
                    EscSiteInto(sb, l.FieldName); sb.Append('|'); sb.Append(l.Password ? '1' : '0'); sb.Append('|'); sb.Append(l.Index);
                    if (version >= MultiInputSecretVersion) { sb.Append('|'); EscSiteInto(sb, l.Class); }
                }
        if (version >= MultiInputSecretVersion)
            for (int i = 0; i < SlotCount; i++)
                if (Slots[i].App is AppLink a)
                {
                    sb.Append("\napp|"); sb.Append(i); sb.Append('|'); EscSiteInto(sb, a.Exe); sb.Append('|'); EscSiteInto(sb, a.WindowClass); sb.Append('|');
                    EscSiteInto(sb, a.FieldClass); sb.Append('|'); sb.Append(a.Order); sb.Append('|'); EscSiteInto(sb, a.Title);
                    sb.Append('|'); sb.Append(a.Dx); sb.Append('|'); sb.Append(a.Dy); sb.Append('|'); EscSiteInto(sb, a.HostClass); sb.Append('|'); sb.Append(a.HostW); sb.Append('|'); sb.Append(a.HostH);
                }
        if (version >= MultiInputSecretVersion)
            for (int i = 0; i < SlotCount; i++)
                for (int k = 0; k < Slots[i].More.Count; k++)
                {
                    ExtraInput m = Slots[i].More[k];
                    sb.Append("\nin|"); sb.Append(i); sb.Append('|'); sb.Append(k + 2); sb.Append('|'); EscSiteInto(sb, m.Value);
                    if (m.Site is SiteLink l)
                    {
                        sb.Append("\ninsite|"); sb.Append(i); sb.Append('|'); sb.Append(k + 2); sb.Append('|'); EscSiteInto(sb, l.Url); sb.Append('|'); EscSiteInto(sb, l.FieldId); sb.Append('|');
                        EscSiteInto(sb, l.FieldName); sb.Append('|'); sb.Append(l.Password ? '1' : '0'); sb.Append('|'); sb.Append(l.Index); sb.Append('|'); EscSiteInto(sb, l.Class);
                    }
                }
        if (version == AnchorSecretVersion)   // 검증 전용(TestSaveAs 9): 0.2.111 이 쓴 체크박스 연결 줄
            for (int i = 0; i < SlotCount; i++)
                if (Slots[i].Anchor is SiteLink a)
                {
                    sb.Append("\nanchor|"); sb.Append(i); sb.Append('|'); EscSiteInto(sb, a.Url); sb.Append('|'); EscSiteInto(sb, a.FieldId); sb.Append('|');
                    EscSiteInto(sb, a.FieldName); sb.Append("|0|"); sb.Append(a.Index); sb.Append('|'); EscSiteInto(sb, a.Class);
                }
        // 검증 전용(ONEKEY_TEST=1): 슬롯 수보다 많은 줄을 쓴 파일을 만들어, 읽는 쪽이 손상으로 거절하는지 시험한다 (T9).
        if (Program.IsTestMode && (Environment.GetEnvironmentVariable("ONEKEY_TEST_FAIL") ?? "").Contains("save:extra-payload"))
            sb.Append("\nextra");
    }

    // ---------- 저장 (DPAPI + 마스터 암호화) ----------
    public bool Save()
    {
        LastSaveError = null;
        LastSaveFail = SaveFail.None;
        // 검증 전용(ONEKEY_TEST=1, ONEKEY_TEST_FAIL=save:fail): 저장이 실패하는 경우. 시험이 실패를 성공으로 세지 않는지 본다(mem.ps1, Codex V47-1).
        // save:access / save:inuse / save:disk / save:dpapi: 원인별 안내 문구 시험(QA-07).
        if (Program.TestFails("save:fail")) { LastSaveError = "검증용 저장 실패(save:fail)"; LastSaveFail = SaveFail.Other; return false; }
        if (Program.TestFails("save:access")) { LastSaveError = "test: access denied"; LastSaveFail = SaveFail.Access; return false; }
        if (Program.TestFails("save:inuse")) { LastSaveError = "test: sharing violation"; LastSaveFail = SaveFail.InUse; return false; }
        if (Program.TestFails("save:disk")) { LastSaveError = "test: disk full"; LastSaveFail = SaveFail.DiskFull; return false; }
        if (Program.TestFails("save:dpapi")) { LastSaveError = "test: CryptProtectData"; LastSaveFail = SaveFail.Dpapi; return false; }
        if (Unsupported) { LastSaveError = "unsupported newer file format"; LastSaveFail = SaveFail.Other; return false; }
        if (_saveTarget is null && Backup.IsPending(Dir)) { LastSaveError = "restore pending"; LastSaveFail = SaveFail.Other; return false; }   // 끝나지 않은 복원 위에 쓰지 않는다(R41-1)
        if (_key is null || _keySalt is null) { LastSaveError = "locked"; LastSaveFail = SaveFail.Other; return false; }
        try
        {
            // 처음 만들 때부터 본인 + SYSTEM 만(2026-10-05 사용자 결정). 좁힌 권한으로 못 만들면 보통 폴더로 대신 만들지 않고 저장 실패로 알린다(R39-1)
            if (!DirAcl.EnsureDir(Dir)) { LastSaveError = "protected folder create"; LastSaveFail = SaveFail.Protect; return false; }

            // 저장은 잠금 해제 중에만 하므로 실제 내용이 기준. 파일에 들어가기 전에는 슬롯의 HasContent 를 건드리지 않는다
            // (실패했을 때 호출한 쪽이 되돌린 값과 AAD 가 어긋나 다음 잠금 해제가 실패하지 않도록).
            bool[] has = Slots.Select(s => s.HasPassword).ToArray();
            Kdf kdf = _keyKdf;   // 들고 있는 키의 유도 방식(새 마스터·옮긴 파일 = Argon2id)
            int version = SaveSecretVersion();
            if ((version == Argon2SecretVersion) != kdf.Argon2id) { LastSaveError = "kdf/format mismatch"; LastSaveFail = SaveFail.Other; return false; }
            // 같은 키·같은 salt 에 새 nonce. 평문은 소유한 고정 버퍼에서 만들고 지운다(문자열을 만들지 않음)
            byte[] pt;
            using (var payload = new SecretText(1024)) { BuildPasswordPayload(version, payload); pt = payload.ToUtf8(); }
            byte[] secret;
            try { secret = Crypto.Encrypt(pt, _key, _keySalt, SecurityAad(version, kdf, has)); }
            finally { Crypto.Wipe(pt); }

            string header = SerializeHeader(kdf, has, version) + "secret=" + Convert.ToBase64String(secret) + "\n";
            byte[] plain = Encoding.UTF8.GetBytes(header);
            byte[]? blob = Dpapi.Protect(plain);
            Array.Clear(plain);
            if (blob is null) { LastSaveError = "CryptProtectData 0x" + Dpapi.LastError.ToString("X8"); LastSaveFail = SaveFail.Dpapi; return false; }

            // 읽지 못했던 파일을 덮어쓰기 전에 사본을 남긴다. 사본을 못 만들면 저장하지 않는다 (원본을 잃지 않도록).
            if (LoadFailed && _saveTarget is null && File.Exists(FilePath))
            {
                string? bak = BackupLoadFailedFile();
                if (bak is null) { LastSaveError = "config.*.bak"; LastSaveFail = SaveFail.Backup; return false; }
            }

            // 임시 파일에 다 쓴 뒤 바꿔치기: 쓰는 도중 꺼져도 반쪽짜리 파일이 남지 않는다.
            string target = _saveTarget ?? FilePath;
            string tmp = target + ".tmp";
            Backup.WriteNew(tmp, blob);   // 남은 임시 이름(링크일 수도)을 따라 쓰지 않는다
            File.Move(tmp, target, overwrite: true);

            // 파일에 실제로 들어간 뒤에만 메모리 상태를 바꾼다.
            for (int i = 0; i < SlotCount; i++) { Slots[i].HasContent = has[i]; Slots[i].LegacyEnterInBrowser = false; }   // 옛 값은 이제 파일에 없다
            _secretBlob = secret;
            _secretVersion = version;
            _kdfArgon = kdf.Argon2id;
            if (!kdf.Argon2id) _kdfIterations = kdf.Iterations;
            HasMaster = true;
            LoadFailed = false;
            MigrationFailed = false;   // 새 형식으로 저장됐으니 이전 실패 상태도 끝났다
            return true;
        }
        catch (Exception ex)
        {
            LastSaveError = ex.GetType().Name + " 0x" + ex.HResult.ToString("X8") + ": " + ex.Message;
            LastSaveFail = ClassifySaveError(ex);
            return false;
        }
    }

    /// <summary>저장 예외를 사용자가 할 일이 다른 몇 갈래로 나눈다 (Codex QA-07).</summary>
    private static SaveFail ClassifySaveError(Exception ex)
    {
        const int ShareViolation = unchecked((int)0x80070020), LockViolation = unchecked((int)0x80070021);
        const int DiskFull = unchecked((int)0x80070070), HandleDiskFull = unchecked((int)0x80070027);
        return ex switch
        {
            UnauthorizedAccessException => SaveFail.Access,
            System.Security.SecurityException => SaveFail.Access,
            IOException io when io.HResult is ShareViolation or LockViolation => SaveFail.InUse,
            IOException io when io.HResult is DiskFull or HandleDiskFull => SaveFail.DiskFull,
            _ => SaveFail.Other,
        };
    }

    /// <summary>예전 설정 사본 하나: 물을 때 본 그대로인지 지우기 직전에 다시 확인하는 값.</summary>
    public readonly record struct BackupFile(string Path, long Length, DateTime WrittenUtc);

    /// <summary>설정 폴더의 예전 설정 사본(config.*.bak — 읽지 못한 파일을 덮어쓰기 전에 남긴 것). 연결(재분석 지점)·못 읽는 항목은 뺀다.</summary>
    public static BackupFile[] BackupFiles()
    {
        var r = new List<BackupFile>();
        try
        {
            if (!Directory.Exists(Dir)) return Array.Empty<BackupFile>();
            foreach (string f in Directory.GetFiles(Dir, "config.*.bak"))
            {
                try
                {
                    var fi = new FileInfo(f);
                    if ((fi.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    r.Add(new BackupFile(fi.FullName, fi.Length, fi.LastWriteTimeUtc));
                }
                catch { }
            }
        }
        catch { }
        return r.ToArray();
    }

    /// <summary>
    /// 사용자가 마스터를 바꾼 뒤 [예]를 누른 그 목록만 지운다(Codex 15:19 S36-2: 물은 뒤 새로 생긴 사본은 지우지 않는다). 그사이 크기·시각이
    /// 바뀌었거나 연결로 바뀐 항목은 건너뛰어 못 지운 수에 넣는다. 자동으로 지우지 않는다. 보통 삭제다: 디스크에서 완전히 지워진다는 보장은 없다.
    /// </summary>
    public static (int Deleted, int Failed) DeleteBackups(IReadOnlyList<BackupFile> asked)
    {
        int ok = 0, fail = 0;
        foreach (BackupFile b in asked)
        {
            try
            {
                var fi = new FileInfo(b.Path);
                if (!fi.Exists) { ok++; continue; }   // 이미 없다(다른 곳에서 지움)
                if ((fi.Attributes & FileAttributes.ReparsePoint) != 0 || fi.Length != b.Length || fi.LastWriteTimeUtc != b.WrittenUtc) { fail++; continue; }
                File.Delete(b.Path);
                if (File.Exists(b.Path)) fail++; else ok++;
            }
            catch { fail++; }
        }
        return (ok, fail);
    }
    /// <summary>읽지 못한 config.dat 를 겹치지 않는 이름의 .bak 으로 복사한다. 성공하면 그 경로, 실패하면 null.</summary>
    internal static string? BackupLoadFailedFile()
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        for (int n = 0; n < 100; n++)
        {
            string bak = Path.Combine(Dir, n == 0 ? $"config.{stamp}.bak" : $"config.{stamp}-{n}.bak");
            if (File.Exists(bak) || Directory.Exists(bak)) continue;
            try
            {
                File.Copy(FilePath, bak, overwrite: false);
                // 복사가 진짜 됐는지 크기로 확인한다.
                if (new FileInfo(bak).Length == new FileInfo(FilePath).Length) return bak;
            }
            catch { }
        }
        return null;
    }
}

/// <summary>저장 실패의 종류(Codex QA-07). 종류마다 사용자에게 다른 할 일을 알린다.</summary>
internal enum SaveFail { None, Access, InUse, DiskFull, Dpapi, Backup, Protect, Other }

/// <summary>Windows DPAPI 래퍼. 현재 사용자 계정(그 계정으로 도는 모든 프로그램)만 복호화할 수 있다 — 다른 Windows 사용자로부터의 보호.</summary>
internal static unsafe class Dpapi
{
    /// <summary>마지막 Protect 실패의 Win32 오류 코드(진단용, 자세한 정보에만 보인다).</summary>
    public static int LastError { get; private set; }
    // 부가 엔트로피: 다른 용도로 DPAPI 를 쓴 값과 섞이지 않게 하는 구분값일 뿐 비밀이 아니다(실행 파일에서 읽힌다). DPAPI 는 앱이 아니라
    // Windows 계정 경계다 — 같은 계정으로 도는 프로그램은 이 값을 넣어 같은 파일을 풀 수 있다(Codex 2026-10-05 보안 진단).
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("1Key/approval-password/v1");

    public static byte[]? Protect(byte[] data) => Protect(data, Entropy);

    /// <summary>엔트로피를 따로 주는 판(실행 목록 launch.dat 은 설정 파일과 다른 엔트로피를 쓴다).</summary>
    public static byte[]? Protect(byte[] data, byte[] entropy)
    {
        fixed (byte* pIn = data)
        fixed (byte* pEnt = entropy)
        {
            var inBlob = new Native.DATA_BLOB { cbData = (uint)data.Length, pbData = (nint)pIn };
            var entBlob = new Native.DATA_BLOB { cbData = (uint)entropy.Length, pbData = (nint)pEnt };
            if (!Native.CryptProtectData(ref inBlob, null, ref entBlob, 0, 0,
                    Native.CRYPTPROTECT_UI_FORBIDDEN, out Native.DATA_BLOB outBlob))
            {
                LastError = Marshal.GetLastPInvokeError();
                return null;
            }
            try
            {
                var result = new byte[outBlob.cbData];
                Marshal.Copy(outBlob.pbData, result, 0, (int)outBlob.cbData);
                return result;
            }
            finally { NativeMemory.Clear((void*)outBlob.pbData, outBlob.cbData); Native.LocalFree(outBlob.pbData); }   // 반납 전에 0 으로(보안 진단 후속)
        }
    }

    public static byte[]? Unprotect(byte[] data) => Unprotect(data, Entropy);

    public static byte[]? Unprotect(byte[] data, byte[] entropy)
    {
        if (data.Length == 0) return null;
        fixed (byte* pIn = data)
        fixed (byte* pEnt = entropy)
        {
            var inBlob = new Native.DATA_BLOB { cbData = (uint)data.Length, pbData = (nint)pIn };
            var entBlob = new Native.DATA_BLOB { cbData = (uint)entropy.Length, pbData = (nint)pEnt };
            if (!Native.CryptUnprotectData(ref inBlob, 0, ref entBlob, 0, 0,
                    Native.CRYPTPROTECT_UI_FORBIDDEN, out Native.DATA_BLOB outBlob))
                return null;
            try
            {
                var result = new byte[outBlob.cbData];
                Marshal.Copy(outBlob.pbData, result, 0, (int)outBlob.cbData);
                return result;
            }
            finally { NativeMemory.Clear((void*)outBlob.pbData, outBlob.cbData); Native.LocalFree(outBlob.pbData); }   // 반납 전에 0 으로(보안 진단 후속)
        }
    }
}
