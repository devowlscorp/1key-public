namespace OneKey;

/// <summary>
/// 후원 화면(AppSupport.cs)이 열 수 있는 주소 — 이 목록에 있는 것만 연다(글자를 이어 붙여 만든 주소는 없다).
/// 후원은 데브아울스 후원 페이지(법인 계좌 — 2026-10-12 사용자: 개인 계좌에서 법인 계좌로, 앱 화면에는 토스·카카오페이 등을 두지 않고
/// 웹 페이지 링크만)로 보낸다. 토스·카카오페이·은행 앱 고르기와 계좌 복사는 그 페이지가 한다(앱을 다시 배포하지 않고 고칠 수 있게).
/// QR 그림(Extra/qr_donate.png)은 Extra/tools/make_qr.py 가 이 상수를 읽어 만든다 — 바꾸면 그 스크립트를 다시 돌린다.
/// 제휴 링크(2026-10-09 사용자: 쿠팡 파트너스·마이리얼트립, 계정 공유형 구독 서비스는 뺌)는 공개용 추천 링크라 비밀 값이 아니다.
/// 한국어 화면에서만 보이고, 누르면 경제적 이해관계 문구가 든 상자를 먼저 띄운 뒤 [브라우저에서 열기]로만 연다.
/// </summary>
internal static class SupportLinks
{
    public const string Donate = "https://devowls.kr/donate";
    public const string Coupang = "https://link.coupang.com/a/hGU2tjGpu8";
    public const string MyRealTrip = "https://myrealt.rip/uhS543";
    public const string GitHub = "https://github.com/devowlscorp/1key-public";

    public const int IxDonate = 0, IxCoupang = 1, IxMyRealTrip = 2, IxGitHub = 3;

    /// <summary>허용 목록: 0 = 데브아울스 후원 페이지, 1 = 쿠팡(제휴), 2 = 마이리얼트립(제휴), 3 = GitHub 저장소(별 주기).</summary>
    public static readonly string[] Allowed = { Donate, Coupang, MyRealTrip, GitHub };
}
