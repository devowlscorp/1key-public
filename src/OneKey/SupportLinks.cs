namespace OneKey;

/// <summary>
/// 후원 화면(AppSupport.cs)이 열 수 있는 주소 — 이 목록에 있는 것만 연다(글자를 이어 붙여 만든 주소는 없다).
/// 받는 사람이 정한 공개 송금 링크라 비밀 값이 아니다(키·토큰·계좌 번호가 들어 있지 않다). QR 그림(Assets/support)은
/// tools/support/make_qr.py 가 이 상수를 읽어 만든다 — 바꾸면 그 스크립트를 다시 돌린다.
/// 토스 송금 QR(계좌 번호가 들어 있음)은 넣지 않았다(2026-10-09 사용자: 보안 문제가 있으면 빼기).
/// </summary>
internal static class SupportLinks
{
    public const string KakaoPay = "https://qr.kakaopay.com/281006011000094683315020";

    /// <summary>허용 목록: 0 = 카카오페이 송금 페이지.</summary>
    public static readonly string[] Allowed = { KakaoPay };
}
