namespace OneKey;

/// <summary>
/// 후원 화면(AppSupport.cs)이 열 수 있는 주소 — 이 목록에 있는 것만 연다(글자를 이어 붙여 만든 주소는 없다).
/// 받는 사람이 정한 공개 송금 링크라 비밀 값이 아니다(키·토큰·계좌 번호가 들어 있지 않다). QR 그림(Assets/support)은
/// tools/support/make_qr.py 가 이 상수를 읽어 만든다 — 바꾸면 그 스크립트를 다시 돌린다.
/// 토스 송금 QR(계좌 번호가 들어 있음)은 넣지 않았다(2026-10-09 사용자: 보안 문제가 있으면 빼기).
/// 제휴 링크(2026-10-09 사용자: 쿠팡 파트너스·마이리얼트립, 계정 공유형 구독 서비스는 뺌)는 공개용 추천 링크라 비밀 값이 아니다.
/// 한국어 화면에서만 보이고, 누르면 경제적 이해관계 문구가 든 상자를 먼저 띄운 뒤 [브라우저에서 열기]로만 연다.
/// </summary>
internal static class SupportLinks
{
    public const string KakaoPay = "https://qr.kakaopay.com/281006011000094683315020";
    public const string Coupang = "https://link.coupang.com/a/hGU2tjGpu8";
    public const string MyRealTrip = "https://myrealt.rip/uhS543";
    public const string GitHub = "https://github.com/devowlscorp/1key-public";

    public const int IxKakaoPay = 0, IxCoupang = 1, IxMyRealTrip = 2, IxGitHub = 3;

    /// <summary>허용 목록: 0 = 카카오페이 송금 페이지, 1 = 쿠팡(제휴), 2 = 마이리얼트립(제휴), 3 = GitHub 저장소(별 주기).</summary>
    public static readonly string[] Allowed = { KakaoPay, Coupang, MyRealTrip, GitHub };
}
