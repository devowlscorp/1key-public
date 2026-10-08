namespace OneKey;

/// <summary>화면 언어. 순서는 Strings.g.cs 의 표(T.Ko, T.En, …)와 설정 파일 값(lang=)의 순서와 같다.</summary>
internal enum Lang { Ko = 0, En = 1, ZhHans = 2, Ja = 3, Vi = 4 }

/// <summary>
/// 다국어(2026-09-30 사용자 결정: 1차 한국어·영어·중국어 간체·일본어·베트남어). 글은 src/OneKey/i18n/strings.tsv 한 곳에 있고
/// 빌드 때 tools/i18n/gen-strings.ps1 이 Strings.g.cs 로 바꾼다(키·자리표시자 검사 포함). 코드는 T.XxxYyy 로 읽는다.
/// 번역 검토는 현지 직원이 나중에 한다(TSV 는 엑셀로 열 수 있다). 빈 칸은 영어로, 영어도 없으면 한국어로 대체된다(생성 때 채움).
/// 설정의 "Windows 설정 따름"(빈 값)은 Windows 표시 언어를 따른다. 1Key 가 입력하는 대상(사내 시스템)의 글과는 무관하다.
/// </summary>
internal static class L
{
    public const int LangCount = 5;
    /// <summary>설정 파일에 쓰는 값(lang=). 빈 값 = Windows 설정 따름.</summary>
    public static readonly string[] Codes = { "ko", "en", "zh-Hans", "ja", "vi" };
    /// <summary>언어 선택 목록에 보이는 이름: 그 언어로 쓴다(읽을 수 없는 언어로 바꿨어도 되돌아갈 수 있게).</summary>
    public static readonly string[] NativeNames = { "한국어", "English", "简体中文", "日本語", "Tiếng Việt" };
    /// <summary>DirectWrite 로캘(한자 글자 모양이 언어마다 다르다: 중국어 간체 vs 일본어).</summary>
    public static readonly string[] Locales = { "ko-KR", "en-US", "zh-CN", "ja-JP", "vi-VN" };

    private static readonly string[][] Table = { T.Ko, T.En, T.ZhHans, T.Ja, T.Vi };

    /// <summary>지금 화면 언어.</summary>
    public static Lang Current { get; private set; } = FromWindows();

    public static string Get(int id) => Table[(int)Current][id];

    /// <summary>"{name}" 같은 자리표시자를 값으로 바꾼다. pairs = 자리표시자, 값, 자리표시자, 값 …</summary>
    public static string Fmt(string s, params object[] pairs)
    {
        for (int i = 0; i + 1 < pairs.Length; i += 2)
            s = s.Replace((string)pairs[i], pairs[i + 1]?.ToString() ?? "");
        return s;
    }

    /// <summary>설정 값(빈 값 = Windows 따름)으로 화면 언어를 정한다. 바뀌었으면 true.</summary>
    public static bool Apply(string setting)
    {
        Lang next = Parse(setting) ?? FromWindows();
        if (next == Current) return false;
        Current = next;
        return true;
    }

    /// <summary>설정 값 → 언어. 빈 값·모르는 값은 null(= Windows 따름).</summary>
    public static Lang? Parse(string? code)
    {
        if (string.IsNullOrEmpty(code)) return null;
        for (int i = 0; i < Codes.Length; i++) if (string.Equals(Codes[i], code, StringComparison.OrdinalIgnoreCase)) return (Lang)i;
        return null;
    }

    /// <summary>
    /// Windows 표시 언어. 한국어·영어·중국어(간체 지역)·일본어·베트남어가 아니면 영어.
    /// 중국어 번체 지역(대만·홍콩·마카오)은 간체 번역만 있으므로 간체로 보인다.
    /// 검증 모드(ONEKEY_TEST=1)에서는 ONEKEY_TEST_LANG 이 Windows 언어를 대신한다(시험은 한국어로 고정).
    /// </summary>
    public static Lang FromWindows()
    {
        if (Program.IsTestMode && Parse(Environment.GetEnvironmentVariable("ONEKEY_TEST_LANG")) is Lang forced) return forced;
        ushort id = Native.GetUserDefaultUILanguage();
        return (id & 0x3FF) switch
        {
            0x12 => Lang.Ko,
            0x04 => Lang.ZhHans,
            0x11 => Lang.Ja,
            0x2A => Lang.Vi,
            _ => Lang.En,
        };
    }
}
