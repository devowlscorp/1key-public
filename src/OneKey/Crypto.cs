using System.Security.Cryptography;
using System.Text;

namespace OneKey;

/// <summary>키 유도 방식. 파일 헤더에 적히고 AAD 로 묶인다.</summary>
internal readonly record struct Kdf(bool Argon2id, int Iterations, int MemoryKiB, int Passes, int Lanes)
{
    /// <summary>PBKDF2-HMAC-SHA256(형식 2–9).</summary>
    public static Kdf Pbkdf2(int iterations) => new(false, iterations, 0, 0, 0);

    /// <summary>
    /// 지원하는 유일한 Argon2id 프로파일(형식 10, 2026-10-05 Codex 15:19 수용): m = 64 MiB, t = 3, p = 1, salt 16, 출력 32, v = 0x13.
    /// 헤더의 다른 값은 계산 전에 거절한다(변조된 큰 값으로 메모리·시간을 쓰게 하지 못하게 — 범위가 아니라 정확한 목록).
    /// </summary>
    public static readonly Kdf Argon2Default = new(true, 0, 65536, 3, 1);

    /// <summary>AAD 에 넣는 표기.</summary>
    public string AadText => Argon2id ? $"argon2id,v=19,m={MemoryKiB},t={Passes},p={Lanes}" : Iterations.ToString();
}

/// <summary>
/// 마스터 비밀번호로 저장 내용을 암호화한다.
/// 키 유도(PBKDF2-SHA256 또는 Argon2id) → AES-256-GCM. 위·변조는 검출된다. 다만 바깥의 DPAPI 층은 같은 Windows 계정의 프로그램이면
/// 풀 수 있으므로, 파일을 얻은 쪽은 마스터 후보를 하나씩 대입해 볼 수 있다 — 짧거나 흔한 마스터는 그렇게 풀린다(Codex 2026-10-05 보안 진단).
/// 키 유도 방식은 대입 한 번의 비용을 키울 뿐이다.
/// 추가 인증 데이터(AAD)로 헤더의 보안 관련 설정(자동 잠금, 슬롯의 단축키·입력 방식·Enter 등)과 키 유도 방식·매개변수를 함께 묶어,
/// 헤더만 바꿔치기해도 복호화가 실패하게 한다.
///
/// 키 보관(2026-10-05 보안 진단 후속, Codex 15:19 Q4 수용): 잠금 해제 동안 마스터 문자열 대신 유도한 키(고정 배열)와 그 salt·방식만 들고 있다.
/// 다시 저장할 때는 같은 키·같은 salt 에 매번 새 무작위 96비트 nonce(RandomNumberGenerator — 실패 뒤 재시도도 새로)를 쓴다. 한 잠금 해제 동안의
/// 저장은 사람이 누르는 [저장]·설정 변경 수준(수십–수백 회)이다. 키를 메모리에 두는 것은 마스터 문자열 보관을 줄일 뿐, 같은 계정 악성
/// 프로그램이 메모리에서 키를 얻는 것을 막지 않는다.
/// </summary>
internal static class Crypto
{
    private const int SaltLen = 16;
    private const int NonceLen = 12;   // AES-GCM 표준
    private const int TagLen = 16;
    private const int KeyLen = 32;     // AES-256

    /// <summary>PBKDF2 로 새로 저장할 때 쓰던 반복 횟수 (OWASP 2023 권고: PBKDF2-HMAC-SHA256 600,000 이상). 형식 10 부터는 Argon2id.</summary>
    public const int DefaultIterations = 600_000;
    /// <summary>0.2.22 까지의 파일이 쓰던 반복 횟수 (헤더에 값이 없을 때).</summary>
    public const int LegacyIterations = 200_000;
    /// <summary>헤더에서 읽은 PBKDF2 값의 허용 범위. 변조된 큰 값으로 CPU 를 고갈시키지 못하게 한다.</summary>
    public const int MinIterations = 100_000, MaxIterations = 5_000_000;
    /// <summary>salt + nonce + tag. 이보다 짧은 블록은 형식상 있을 수 없다(손상).</summary>
    public const int MinBlobLength = SaltLen + NonceLen + TagLen;

    /// <summary>새 무작위 salt.</summary>
    public static byte[] NewSalt() => RandomNumberGenerator.GetBytes(SaltLen);

    /// <summary>블록 앞의 salt(그 블록의 키를 유도한 값). 너무 짧으면 null.</summary>
    public static byte[]? SaltOf(byte[] blob) => blob.Length >= MinBlobLength ? blob[..SaltLen] : null;

    /// <summary>
    /// 마스터 + salt → 32바이트 키. GC 가 옮기며 사본을 남기지 않게 고정 배열로 만든다. 마스터의 UTF-8 바이트도 고정 배열에 두고 바로 지운다.
    /// 다 쓰면 부르는 쪽이 <see cref="Wipe"/>. Argon2id 는 <see cref="Kdf.Argon2Default"/> 만 계산한다.
    /// </summary>
    public static byte[] DeriveKey(ReadOnlySpan<char> master, byte[] salt, Kdf kdf)
    {
        if (kdf.Argon2id && kdf != Kdf.Argon2Default) throw new CryptographicException("unsupported Argon2id profile");
        if (TestFailDeriveAt > 0 && Interlocked.Decrement(ref TestFailDeriveAt) == 0) throw new CryptographicException("test: derive failed");   // 검증 전용(R39-3)
        byte[] pw = GC.AllocateArray<byte>(Encoding.UTF8.GetByteCount(master), pinned: true);
        try
        {
            Encoding.UTF8.GetBytes(master, pw);
            if (kdf.Argon2id)
            {
                byte[] tag = Argon2.Hash(pw, salt, kdf.Passes, kdf.MemoryKiB, kdf.Lanes, KeyLen);
                byte[] key = GC.AllocateArray<byte>(KeyLen, pinned: true);
                tag.CopyTo(key, 0);
                CryptographicOperations.ZeroMemory(tag);
                return key;
            }
            byte[] k = GC.AllocateArray<byte>(KeyLen, pinned: true);
            Rfc2898DeriveBytes.Pbkdf2(pw, salt, k, kdf.Iterations, HashAlgorithmName.SHA256);
            return k;
        }
        finally { CryptographicOperations.ZeroMemory(pw); }
    }

    /// <summary>검증 전용: n 번째 키 유도를 실패로(0 = 끔). 메모리 부족·암호 API 오류 대신 — 실제 큰 할당을 일으키지 않는다(Codex 16:22 R39-3).</summary>
    internal static int TestFailDeriveAt;

    /// <summary>두 키가 같은가(일정 시간 비교).</summary>
    public static bool SameKey(byte[] a, byte[] b) => CryptographicOperations.FixedTimeEquals(a, b);

    /// <summary>평문 바이트를 [salt|nonce|tag|ciphertext] 로 암호화한다. salt 는 key 를 유도한 값(블록에 적어 다음 잠금 해제에 쓴다). nonce 는 늘 새로.</summary>
    public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, byte[] key, byte[] salt, byte[] aad)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceLen);
        var blob = new byte[SaltLen + NonceLen + TagLen + plaintext.Length];
        Buffer.BlockCopy(salt, 0, blob, 0, SaltLen);
        Buffer.BlockCopy(nonce, 0, blob, SaltLen, NonceLen);
        using (var gcm = new AesGcm(key, TagLen))
            gcm.Encrypt(nonce, plaintext, blob.AsSpan(MinBlobLength), blob.AsSpan(SaltLen + NonceLen, TagLen), aad);
        return blob;
    }

    /// <summary>
    /// 복호화. 키가 틀리거나(마스터 불일치) 블록·AAD 가 변조되면 null. 돌려주는 평문은 고정 배열 — 다 쓰면 부르는 쪽이 <see cref="Wipe"/>.
    /// </summary>
    public static byte[]? Decrypt(byte[] blob, byte[] key, byte[] aad)
    {
        if (blob.Length < MinBlobLength) return null;
        int ctLen = blob.Length - MinBlobLength;
        byte[] pt = GC.AllocateArray<byte>(ctLen, pinned: true);
        try
        {
            using (var gcm = new AesGcm(key, TagLen))
                gcm.Decrypt(blob.AsSpan(SaltLen, NonceLen), blob.AsSpan(MinBlobLength), blob.AsSpan(SaltLen + NonceLen, TagLen), pt, aad);   // 인증 실패 시 예외
            return pt;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(pt);
            return null;   // 마스터 비밀번호 불일치 또는 변조
        }
    }

    /// <summary>바이트 배열을 0 으로 덮는다(null 이면 아무것도 안 함).</summary>
    public static void Wipe(byte[]? b) { if (b is not null) CryptographicOperations.ZeroMemory(b); }
}

/// <summary>
/// 비밀 글(마스터·복호화한 평문)을 담는 고정 char 배열. 이 객체가 버퍼의 유일한 소유자이고, 다 쓰면 Dispose 로 0 으로 덮는다(Codex 15:19 S36-1:
/// 문자열을 직접 덮지 않고 소유한 버퍼를 지운다). 문자열로 바꾸지 않고 Span 으로만 읽는다.
/// </summary>
internal sealed class SecretText : IDisposable
{
    private char[] _buf;
    public int Length { get; private set; }
    public ReadOnlySpan<char> Span => _buf.AsSpan(0, Length);

    public SecretText(int capacity) { _buf = GC.AllocateArray<char>(Math.Max(capacity, 16), pinned: true); }

    /// <summary>입력칸(비밀번호 칸)의 글을 문자열을 만들지 않고 읽는다. 칸 안의 버퍼는 칸이 비워지거나 닫힐 때까지 남는다.</summary>
    public static unsafe SecretText FromWindow(nint h)
    {
        // 예전 읽기(Native.GetWindowText, 512 칸 버퍼)와 같은 최대 511자 — 그 길이로 만든 마스터가 계속 맞게
        int n = Math.Min(Math.Max(0, GetWindowTextLengthW(h)), 511);
        var t = new SecretText(n + 1);
        fixed (char* p = t._buf) t.Length = Math.Max(0, Native.GetWindowTextW(h, p, n + 1));
        return t;
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowTextLengthW(nint hwnd);

    /// <summary>UTF-8 바이트를 글로(바이트는 부르는 쪽이 지운다).</summary>
    public static SecretText FromUtf8(ReadOnlySpan<byte> utf8)
    {
        var t = new SecretText(System.Text.Encoding.UTF8.GetCharCount(utf8));
        t.Length = System.Text.Encoding.UTF8.GetChars(utf8, t._buf);
        return t;
    }

    public void Append(char c) { Ensure(1); _buf[Length++] = c; }
    public void Append(ReadOnlySpan<char> s) { Ensure(s.Length); s.CopyTo(_buf.AsSpan(Length)); Length += s.Length; }
    public void Append(int v) { Span<char> d = stackalloc char[12]; v.TryFormat(d, out int n); Append(d[..n]); }

    /// <summary>모자라면 두 배로 늘린다: 새 고정 배열로 옮기고 예전 배열은 0 으로 덮는다(사본이 남지 않게).</summary>
    private void Ensure(int more)
    {
        if (Length + more <= _buf.Length) return;
        char[] next = GC.AllocateArray<char>(Math.Max(_buf.Length * 2, Length + more), pinned: true);
        _buf.AsSpan(0, Length).CopyTo(next);
        Array.Clear(_buf);
        _buf = next;
    }

    /// <summary>UTF-8 로 바꾼 고정 바이트 배열(부르는 쪽이 <see cref="Crypto.Wipe"/>).</summary>
    public byte[] ToUtf8()
    {
        byte[] b = GC.AllocateArray<byte>(System.Text.Encoding.UTF8.GetByteCount(Span), pinned: true);
        System.Text.Encoding.UTF8.GetBytes(Span, b);
        return b;
    }

    public void Dispose() { Array.Clear(_buf); Length = 0; }

    /// <summary>검증 전용: 버퍼가 모두 0 인가(Dispose 뒤 확인).</summary>
    internal bool TestAllZero() => Array.TrueForAll(_buf, c => c == '\0');
}
