namespace OneKey;

/// <summary>
/// 화면별 도움말 문구 (2026-09-29 사용자 결정, Codex 개선안 C). 화면에는 짧은 상태와 선택에 따른 주의만 두고,
/// 사용법·예외는 화면 위 [도움말](또는 F1)로 여는 이 글로 옮겼다. 글은 다국어 표(i18n/strings.tsv 의 help.*)에 있다
/// (사용안내 문서와 같은 동작을 설명해야 한다: docs/1Key-사용안내.md).
/// "## " 로 시작하는 줄은 섹션 제목이다: 도움말 창이 굵게 따로 보여 준다(<see cref="Dialog.SectionMark"/>).
/// 편집 도움말은 위에 사용 순서, 예외는 맨 아래 "주의사항"으로 모았다 (Codex QA-05).
/// </summary>
internal static class Help
{
    public static string ListTitle => T.HelpListTitle;
    public static string List => T.HelpList + "\n" + T.HelpCat + "\n## " + T.HelpFontHeading + "\n• " + FontNotice;   // 고양이 키우기(0.5.20)

    /// <summary>글꼴 고지(OFL 1.1): 목록 도움말과 잠금 화면 정보에 쓴다. 전문은 [라이선스 보기].</summary>
    public static string FontNotice => T.HelpFontNotice;
    public static string LicenseButton => T.HelpLicenseButton;
    public static string LicenseTitle => T.HelpLicenseTitle;

    /// <summary>잠금 화면의 F1: 잠금을 풀지 않고 볼 수 있는 정보(버전, 글꼴 라이선스). 비밀·잠금 해제와 연결하지 않는다.</summary>
    public static string InfoTitle => T.HelpInfoTitle;
    public static string Info(string version) => "1Key v" + version + "\n## " + T.HelpFontHeading + "\n• " + FontNotice;

    public static string EditTitle => T.HelpEditTitle;
    public static string Edit => T.HelpEdit;
    public static string SettingsTitle => T.HelpSettingsTitle;
    public static string Settings => T.HelpSettings;
    public static string AdvancedTitle => T.HelpAdvancedTitle;
    public static string Advanced => T.HelpAdvanced;
    public static string MasterTitle => T.HelpMasterTitle;
    public static string Master => T.HelpMaster;
}
