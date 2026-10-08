using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace OneKey;

/// <summary>
/// 백업 파일(.1keybak) — 다른 PC·Windows 재설치 뒤에도 옮길 수 있게(2026-10-05 사용자 결정, Codex 16:22 B 설계 조건).
/// DPAPI(이 PC·이 계정 보호)가 없고 **백업 전용 비밀번호**(사용자 결정)로만 잠근다: 파일을 얻은 누구나 그 비밀번호를 대입해 볼 수 있다.
///
/// 형식(정확한 바이트, 모두 AAD 로 인증):
///   머리 55 바이트 = "1KEYBAK1"(8) | 형식 번호 u16=1 | 키 유도 u8=1(Argon2id) | m u32 | t u32 | p u32 | salt(16) | nonce(12) | 평문 길이 u32
///   그 뒤 = 태그(16) | 암호문(평문 길이). 뒤에 남는 바이트 없음.
/// 평문 = 섹션 [종류 u8 | 길이 u32 | 내용] 을 종류 오름차순으로, 중복 없이: 1 = 설정 머리(UTF-8, 이름·단축키·설정 — 형식 10),
///   2 = 암호 블록 평문(UTF-8, 비밀번호·입력·연결), 3 = 실행 목록 평문(UTF-8, 없을 수 있음). 1·2 는 반드시.
/// 키 = 백업 비밀번호 + 새 salt → Argon2id(<see cref="Kdf.Argon2Default"/> — 다른 프로파일은 계산 전에 거절). 설정 파일의 키·salt 를 쓰지 않는다.
/// 상한: 파일 8 MiB, 섹션 4 MiB. 읽기 전에 크기부터, 키 유도 전에 머리 전체를 확인한다.
/// </summary>
internal static class Backup
{
    private static readonly byte[] Magic = "1KEYBAK1"u8.ToArray();
    public const ushort FormatVersion = 1;
    public const int HeaderLen = 55, TagLen = 16, NonceLen = 12, SaltLen = 16;
    public const int MaxFile = 8 << 20, MaxSection = 4 << 20;
    public const byte SecSettings = 1, SecSecrets = 2, SecLaunch = 3;
    public const string Extension = ".1keybak";

    /// <summary>열기 결과의 종류(안내 문구를 고른다).</summary>
    public enum Error { None, TooLarge, NotBackup, NewerFormat, BadProfile, Damaged, WrongPasswordOrDamaged, KdfFailed }

    /// <summary>
    /// 백업 파일 바이트를 만든다(작업 스레드에서 불러도 된다). 비밀 평문(secrets)은 부르는 쪽 소유 — 여기서 지우지 않는다. 실패하면 null.
    /// </summary>
    public static byte[]? Build(ReadOnlySpan<char> password, ReadOnlySpan<byte> settings, ReadOnlySpan<byte> secrets, ReadOnlySpan<byte> launch)
    {
        long plainLen = 5L + settings.Length + 5L + secrets.Length + (launch.IsEmpty ? 0 : 5L + launch.Length);
        if (settings.Length > MaxSection || secrets.Length > MaxSection || launch.Length > MaxSection || HeaderLen + TagLen + plainLen > MaxFile) return null;
        byte[] salt = Crypto.NewSalt(), nonce = RandomNumberGenerator.GetBytes(NonceLen);
        byte[]? key = null, plain = null;
        try
        {
            key = Crypto.DeriveKey(password, salt, Kdf.Argon2Default);
            plain = GC.AllocateArray<byte>((int)plainLen, pinned: true);
            int o = 0;
            void Sec(byte id, ReadOnlySpan<byte> data, byte[] buf) { buf[o] = id; BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(o + 1), (uint)data.Length); data.CopyTo(buf.AsSpan(o + 5)); o += 5 + data.Length; }
            Sec(SecSettings, settings, plain);
            Sec(SecSecrets, secrets, plain);
            if (!launch.IsEmpty) Sec(SecLaunch, launch, plain);
            var file = new byte[HeaderLen + TagLen + plain.Length];
            WriteHeader(file.AsSpan(0, HeaderLen), salt, nonce, (uint)plain.Length);
            using (var gcm = new AesGcm(key, TagLen))
                gcm.Encrypt(nonce, plain, file.AsSpan(HeaderLen + TagLen), file.AsSpan(HeaderLen, TagLen), file.AsSpan(0, HeaderLen));
            return file;
        }
        catch { return null; }
        finally { Crypto.Wipe(key); Crypto.Wipe(plain); }
    }

    /// <summary>검증 전용: 평문(섹션 바이트)을 그대로 넣은 백업 파일 — 잘못된 섹션 배열의 거절을 시험한다.</summary>
    internal static byte[] TestBuildRaw(ReadOnlySpan<char> password, byte[] plain)
    {
        byte[] salt = Crypto.NewSalt(), nonce = RandomNumberGenerator.GetBytes(NonceLen);
        byte[] key = Crypto.DeriveKey(password, salt, Kdf.Argon2Default);
        try
        {
            var file = new byte[HeaderLen + TagLen + plain.Length];
            WriteHeader(file.AsSpan(0, HeaderLen), salt, nonce, (uint)plain.Length);
            using (var gcm = new AesGcm(key, TagLen))
                gcm.Encrypt(nonce, plain, file.AsSpan(HeaderLen + TagLen), file.AsSpan(HeaderLen, TagLen), file.AsSpan(0, HeaderLen));
            return file;
        }
        finally { Crypto.Wipe(key); }
    }

    private static void WriteHeader(Span<byte> h, byte[] salt, byte[] nonce, uint plainLen)
    {
        Magic.CopyTo(h);
        BinaryPrimitives.WriteUInt16LittleEndian(h[8..], FormatVersion);
        h[10] = 1;   // Argon2id
        BinaryPrimitives.WriteUInt32LittleEndian(h[11..], (uint)Kdf.Argon2Default.MemoryKiB);
        BinaryPrimitives.WriteUInt32LittleEndian(h[15..], (uint)Kdf.Argon2Default.Passes);
        BinaryPrimitives.WriteUInt32LittleEndian(h[19..], (uint)Kdf.Argon2Default.Lanes);
        salt.CopyTo(h[23..]);
        nonce.CopyTo(h[39..]);
        BinaryPrimitives.WriteUInt32LittleEndian(h[51..], plainLen);
    }

    /// <summary>열어 낸 백업 내용. 소유한 고정 배열이며 Dispose 로 지운다(여러 번 불러도 된다).</summary>
    internal sealed class Contents : IDisposable
    {
        public byte[] Settings = Array.Empty<byte>();
        public SecretText? Secrets;
        public byte[]? Launch;
        private int _disposed;
        public Contents() => Interlocked.Increment(ref TestLive);
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Crypto.Wipe(Settings); Secrets?.Dispose(); Crypto.Wipe(Launch);
            Interlocked.Decrement(ref TestLive);
        }
    }

    /// <summary>검증 전용: 지우지 않은(Dispose 안 한) 내용 객체 수 — 실패 경로가 모두 정리하는지 본다.</summary>
    internal static int TestLive;
    /// <summary>검증 전용: 섹션을 담는 도중(n 번째 섹션) 실패를 일으킨다. 0 = 끔.</summary>
    internal static int TestFailSectionAt;

    /// <summary>
    /// 백업 파일을 읽는다: 같은 파일 핸들로 상한(MaxFile)까지만 읽고, 더 있으면(그사이 커진 경우 포함) 그 이상 할당하기 전에 거절(Codex 17:03 R41-4).
    /// 결과: 바이트 또는 null + 오류 종류.
    /// </summary>
    public static (byte[]? Bytes, Error Error) ReadBounded(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length > MaxFile) return (null, Error.TooLarge);
            var buf = new byte[(int)fs.Length];
            int got = 0;
            while (got < buf.Length) { int n = fs.Read(buf, got, buf.Length - got); if (n <= 0) break; got += n; }
            if (got != buf.Length) return (null, Error.Damaged);   // 그사이 줄어듦
            if (fs.ReadByte() != -1) return (null, Error.TooLarge);   // 그사이 커짐: 더 할당하지 않는다
            return (buf, Error.None);
        }
        catch { return (null, Error.NotBackup); }
    }

    /// <summary>
    /// 머리만 확인한다(키 유도 전, 다른 스레드에서 불러도 된다): 크기·표식·형식 번호·키 유도 방식·정확한 프로파일·길이가 맞는가.
    /// </summary>
    public static Error CheckHeader(ReadOnlySpan<byte> file)
    {
        if (file.Length > MaxFile) return Error.TooLarge;
        if (file.Length < HeaderLen + TagLen || !file[..8].SequenceEqual(Magic)) return Error.NotBackup;
        ushort ver = BinaryPrimitives.ReadUInt16LittleEndian(file[8..]);
        if (ver != FormatVersion) return ver > FormatVersion ? Error.NewerFormat : Error.Damaged;
        if (file[10] != 1) return Error.BadProfile;
        var k = new Kdf(true, 0, (int)BinaryPrimitives.ReadUInt32LittleEndian(file[11..]), (int)BinaryPrimitives.ReadUInt32LittleEndian(file[15..]), (int)BinaryPrimitives.ReadUInt32LittleEndian(file[19..]));
        if (k != Kdf.Argon2Default) return Error.BadProfile;   // 다른 프로파일은 계산·할당 전에 거절
        uint plainLen = BinaryPrimitives.ReadUInt32LittleEndian(file[51..]);
        if ((long)HeaderLen + TagLen + plainLen != file.Length) return Error.Damaged;   // 잘림·뒤에 남는 바이트
        return Error.None;
    }

    /// <summary>
    /// 백업 파일을 연다(작업 스레드에서 불러도 된다): 머리 → 키 유도 → 복호화(머리 전체 AAD) → 섹션 해석. 하나라도 어긋나면 내용 없이 오류.
    /// </summary>
    public static (Contents? Contents, Error Error) Open(byte[] file, ReadOnlySpan<char> password)
    {
        Error e = CheckHeader(file);
        if (e != Error.None) return (null, e);
        byte[]? key = null, plain = null;
        try
        {
            try { key = Crypto.DeriveKey(password, file.AsSpan(23, SaltLen).ToArray(), Kdf.Argon2Default); }
            catch { return (null, Error.KdfFailed); }
            int plainLen = file.Length - HeaderLen - TagLen;
            plain = GC.AllocateArray<byte>(plainLen, pinned: true);
            try
            {
                using var gcm = new AesGcm(key, TagLen);
                gcm.Decrypt(file.AsSpan(39, NonceLen), file.AsSpan(HeaderLen + TagLen), file.AsSpan(HeaderLen, TagLen), plain, file.AsSpan(0, HeaderLen));
            }
            catch (CryptographicException) { return (null, Error.WrongPasswordOrDamaged); }
            var c = new Contents();
            bool handed = false;
            try
            {
                int o = 0, last = 0, count = 0;
                while (o < plain.Length)
                {
                    if (plain.Length - o < 5) return (null, Error.Damaged);
                    byte id = plain[o];
                    uint len = BinaryPrimitives.ReadUInt32LittleEndian(plain.AsSpan(o + 1));
                    if (id <= last || id > SecLaunch || len > MaxSection || len > (uint)(plain.Length - o - 5)) return (null, Error.Damaged);   // 순서·중복·종류·길이
                    var data = plain.AsSpan(o + 5, (int)len);
                    if (TestFailSectionAt > 0 && ++count == TestFailSectionAt) throw new OutOfMemoryException("test: section");
                    // 모든 섹션은 엄격한 UTF-8(잘못된 바이트를 대체 문자로 바꾸지 않고 거절 — 설정 머리와 같은 규칙, R41-4)
                    if (!IsStrictUtf8(data)) return (null, Error.Damaged);
                    switch (id)
                    {
                        case SecSettings: c.Settings = GC.AllocateArray<byte>(data.Length, pinned: true); data.CopyTo(c.Settings); break;
                        case SecSecrets: c.Secrets = SecretText.FromUtf8(data); break;
                        case SecLaunch: c.Launch = GC.AllocateArray<byte>(data.Length, pinned: true); data.CopyTo(c.Launch); break;
                    }
                    last = id;
                    o += 5 + (int)len;
                }
                if (c.Settings.Length == 0 || c.Secrets is null) return (null, Error.Damaged);
                handed = true;
                return (c, Error.None);
            }
            catch { return (null, Error.Damaged); }
            finally { if (!handed) c.Dispose(); }   // 넘기지 못한 내용은 어느 경로든 지운다(R41-3)
        }
        finally { Crypto.Wipe(key); Crypto.Wipe(plain); }
    }

    private static bool IsStrictUtf8(ReadOnlySpan<byte> b)
    {
        try { new UTF8Encoding(false, true).GetCharCount(b); return true; }
        catch (DecoderFallbackException) { return false; }
    }

    // ---------------------------------------------------------------- 두 파일을 한 번에(설정 + 실행 목록)

    private const string MarkerName = "restore.commit", NewSuffix = ".restore", MarkerHead = "1KEY-RESTORE 1", MarkerEnd = "end";

    /// <summary>두 파일 게시의 결과(Codex 17:03 R41-1).</summary>
    public enum CommitResult
    {
        /// <summary>게시하지 않았다 — 예전 쌍 그대로, 남은 조각 없음.</summary>
        NotPublished,
        /// <summary>새 쌍을 모두 게시했다.</summary>
        Published,
        /// <summary>표식은 생겼는데 옮기기를 끝내지 못했다 — 새 쌍으로 마저 끝내기 전에는 설정을 읽거나 저장하면 안 된다(<see cref="RecoverPending"/>).</summary>
        Pending,
        /// <summary>앞선 게시가 해결되지 않아 새 게시를 시작하지 않았다.</summary>
        Blocked,
    }

    /// <summary>시작할 때의 복구 결과.</summary>
    public enum RecoverResult { Nothing, FinishedNew, CleanedOld, Blocked }

    /// <summary>검증 전용: 커밋을 이 단계 뒤에서 멈춘다(1 = 새 설정 씀, 2 = 새 목록 씀, 3 = 표식 씀, 4 = 첫 파일 옮김). 0 = 끔.</summary>
    internal static int TestStopAfter;
    /// <summary>검증 전용: n 번째 옮기기(File.Move)를 실패로(잠긴 파일 흉내). 0 = 끔.</summary>
    internal static int TestFailMoveAt;
    /// <summary>검증 전용: 옮기기 횟수(TestFailMoveAt 과 견준다 — 시험이 0 으로 되돌린다).</summary>
    internal static int TestMoves;

    /// <summary>표식이 남아 있다(게시를 끝내지 못함) — 이 상태에서는 설정·실행 목록을 저장하지 않는다.</summary>
    public static bool IsPending(string dir) => File.Exists(Path.Combine(dir, MarkerName));

    /// <summary>
    /// 새 설정·새 실행 목록을 한 번에 게시한다(Codex 16:22 B5, 17:03 R41-1):
    /// 1) 앞선 게시가 남아 있으면 먼저 해결하고, 해결되지 않으면 시작하지 않는다(Blocked).
    /// 2) 둘 다 <c>*.restore</c> 로 완전히 쓰고 디스크로 밀어 넣는다(FlushFileBuffers — 쓰기 완료를 요청할 뿐, 실제 전원 손실 내구성을 시험한 것은 아니다).
    /// 3) 표식 = 형식 머리 + 파일마다 "이름|크기|SHA-256" + 끝 줄을 임시 파일로 쓰고 밀어 넣은 뒤 원자적으로 바꿔 놓는다 — 여기부터는 "새 쌍"으로 끝낸다.
    /// 4) 표식대로 옮기고(크기·해시 확인) 표식을 지운다. 옮기기 실패면 Pending(새 쌍으로 마저 끝내기 전에는 쓰지 않는다).
    /// writeConfig·writeLaunch 는 주어진 경로에 완전한 파일을 쓰고 성공하면 true. writeLaunch 가 null 이면 실행 목록은 그대로 둔다.
    /// </summary>
    public static CommitResult Commit(string dir, Func<string, bool> writeConfig, Func<string, bool>? writeLaunch)
    {
        if (RecoverPending(dir) == RecoverResult.Blocked) return CommitResult.Blocked;
        string marker = Path.Combine(dir, MarkerName);
        var names = new List<string> { "config.dat" };
        if (writeLaunch is not null) names.Add("launch.dat");
        try
        {
            if (!SafeDir(dir)) return CommitResult.NotPublished;
            Cleanup(dir);   // 남은 조각·임시 이름은 이름만 지운다(하드 링크여도 대상은 그대로) — 아래에서 모두 새로 만든다
            if (!writeConfig(Path.Combine(dir, "config.dat" + NewSuffix))) { Cleanup(dir); return CommitResult.NotPublished; }
            if (TestStopAfter == 1) return CommitResult.NotPublished;
            if (writeLaunch is not null && !writeLaunch(Path.Combine(dir, "launch.dat" + NewSuffix))) { Cleanup(dir); return CommitResult.NotPublished; }
            if (TestStopAfter == 2) return CommitResult.NotPublished;
            var sb = new StringBuilder(MarkerHead).Append('\n');
            foreach (string n in names)
            {
                string f = Path.Combine(dir, n + NewSuffix);
                Flush(f);
                sb.Append(n).Append('|').Append(new FileInfo(f).Length).Append('|').Append(Hash(f)).Append('\n');
            }
            sb.Append(MarkerEnd).Append('\n');
            WriteNew(marker + ".tmp", Encoding.UTF8.GetBytes(sb.ToString()));   // 고정 이름이지만 새 파일로만 만든다(Codex 17:49)
            File.Move(marker + ".tmp", marker, overwrite: true);   // 여기부터는 "새 쌍"으로 끝낸다
            if (TestStopAfter == 3) return CommitResult.Pending;
            return Finish(dir) ? CommitResult.Published : CommitResult.Pending;
        }
        catch { if (File.Exists(marker)) return CommitResult.Pending; Cleanup(dir); return CommitResult.NotPublished; }
    }

    /// <summary>
    /// 임시 파일을 새로 만들어 쓰고 디스크로 밀어 넣는다: 같은 이름이 있으면 이름만 지우고(하드 링크·연결이어도 그 대상은 건드리지 않는다)
    /// CreateNew 로 연다 — 그사이 누가 다시 만들어 두면 따라 쓰지 않고 실패한다. 같은 사용자 프로세스와의 모든 교체 경쟁을 막는다는 뜻은 아니다
    /// (만든 뒤 옮기기 전에 이름을 바꿔치기하는 경우 등).
    /// </summary>
    internal static void WriteNew(string path, byte[] data)
    {
        if (File.Exists(path)) File.Delete(path);
        using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        fs.Write(data);
        fs.Flush(flushToDisk: true);
    }

    private static void Flush(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        fs.Flush(flushToDisk: true);
    }

    private static string Hash(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(fs));
    }

    /// <summary>폴더와 그 부모·안의 대상 이름이 연결(재분석 지점)이 아닌가.</summary>
    private static bool SafeDir(string dir)
    {
        string full = Path.GetFullPath(dir).TrimEnd('\\');
        for (string? d = full; d is not null && d.Length > 3; d = Path.GetDirectoryName(d))
            if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) return false;
        // 쓰거나 옮기는 이름 모두(임시 표식·조각의 임시 파일 포함, Codex 17:49). 파일뿐 아니라 같은 이름의 폴더 연결(정션)도 본다
        foreach (string n in new[] { "config.dat", "launch.dat" })
            foreach (string sfx in new[] { "", ".tmp", NewSuffix, NewSuffix + ".tmp" })
                if (IsLink(Path.Combine(full, n + sfx))) return false;
        return !IsLink(Path.Combine(full, MarkerName)) && !IsLink(Path.Combine(full, MarkerName + ".tmp"));
    }

    private static bool IsLink(string path) => Path.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    /// <summary>검증 전용(ONEKEY_TEST=1): 설정 폴더에 이 이름의 파일이 있는 동안 launch.dat 옮기기가 실패한다(창 시험이 게시 미완료를 오래 유지).</summary>
    private const string TestFailMoveFile = "test-fail-move";

    private static void TestMoveFault(string dir, string name)
    {
        if (TestFailMoveAt > 0 && Interlocked.Increment(ref TestMoves) == TestFailMoveAt) throw new IOException("test: move");
        if (Program.IsTestMode && name == "launch.dat" && File.Exists(Path.Combine(dir, TestFailMoveFile))) throw new IOException("test: move (file)");
    }

    /// <summary>표식을 정확히 읽는다: 형식 머리, 알려진 이름(설정 필수, 목록 선택)·중복 없음, 크기·해시 형식, 끝 줄. 아니면 null.</summary>
    private static List<(string Name, long Size, string Hash)>? ReadMarker(string marker)
    {
        string[] lines;
        try
        {
            var fi = new FileInfo(marker);
            if (fi.Length > 4096) return null;
            lines = File.ReadAllText(marker).Split('\n');
        }
        catch { return null; }
        if (lines.Length < 4 || lines[0] != MarkerHead || lines[^1] != "" || lines[^2] != MarkerEnd) return null;
        var r = new List<(string, long, string)>();
        for (int i = 1; i < lines.Length - 2; i++)
        {
            string[] f = lines[i].Split('|');
            if (f.Length != 3 || f[0] is not ("config.dat" or "launch.dat") || r.Any(x => x.Item1 == f[0])) return null;
            if (!long.TryParse(f[1], out long size) || size <= 0 || f[2].Length != 64 || !f[2].All(Uri.IsHexDigit)) return null;
            r.Add((f[0], size, f[2]));
        }
        return r.Count > 0 && r[0].Item1 == "config.dat" ? r : null;
    }

    /// <summary>
    /// 표식대로 마저 옮긴다. 이름마다: *.restore 가 표식의 크기·해시와 같으면 옮기고, 없으면 최종 파일이 이미 그 크기·해시여야 한다(이미 옮김).
    /// 어느 쪽도 아니면(조각 유실·바뀜·잘린 표식) 실패 — 표식·파일을 그대로 둔다.
    /// </summary>
    private static bool Finish(string dir)
    {
        string marker = Path.Combine(dir, MarkerName);
        if (!SafeDir(dir) || ReadMarker(marker) is not { } list) return false;
        foreach (var (name, size, hash) in list)
        {
            string src = Path.Combine(dir, name + NewSuffix), dst = Path.Combine(dir, name);
            if (File.Exists(src))
            {
                if (new FileInfo(src).Length != size || Hash(src) != hash) return false;
                TestMoveFault(dir, name);
                File.Move(src, dst, overwrite: true);
                if (TestStopAfter == 4) return false;
            }
            else if (!File.Exists(dst) || new FileInfo(dst).Length != size || Hash(dst) != hash) return false;
        }
        File.Delete(marker);
        return true;
    }

    private static void Cleanup(string dir)
    {
        foreach (string n in new[] { "config.dat", "launch.dat" })
            foreach (string sfx in new[] { NewSuffix, NewSuffix + ".tmp" })
                try { File.Delete(Path.Combine(dir, n + sfx)); } catch { }
        try { File.Delete(Path.Combine(dir, MarkerName + ".tmp")); } catch { }
    }

    /// <summary>
    /// 시작할 때(설정을 읽기 전)·새 게시 전: 표식이 있으면 마저 옮겨 새 쌍으로, 없으면 남은 *.restore 를 지워 예전 쌍으로.
    /// 표식이 있는데 끝내지 못하면 Blocked — 부르는 쪽은 설정을 읽거나 저장하지 않고(복구 안내 상태) 파일·조각을 그대로 둔다.
    /// </summary>
    public static RecoverResult RecoverPending(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return RecoverResult.Nothing;
            if (File.Exists(Path.Combine(dir, MarkerName))) return Finish(dir) ? RecoverResult.FinishedNew : RecoverResult.Blocked;
            bool any = File.Exists(Path.Combine(dir, "config.dat" + NewSuffix)) || File.Exists(Path.Combine(dir, "launch.dat" + NewSuffix)) || File.Exists(Path.Combine(dir, MarkerName + ".tmp"));
            if (!any) return RecoverResult.Nothing;
            Cleanup(dir);
            return RecoverResult.CleanedOld;
        }
        catch { return File.Exists(Path.Combine(dir, MarkerName)) ? RecoverResult.Blocked : RecoverResult.Nothing; }
    }
}
