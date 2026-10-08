using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;

namespace OneKey;

/// <summary>
/// Argon2id v1.3(RFC 9106)와 BLAKE2b(RFC 7693). 외부 패키지 없이(NativeAOT·공급망) 1Key 안에서 계산한다.
/// 검증: 단위 시험 AG01–(RFC 9106 의 Argon2id 시험 값, 참조 구현의 공개 시험 값, BLAKE2b 시험 값).
/// 2026-10-05 보안 진단 후속 B1 — 저장 형식에 연결하는 것은 Codex 합의 뒤(아직 어디서도 부르지 않는다, 시험만).
/// 레인은 순서대로 한 스레드에서 계산한다(결과는 병렬 계산과 같다 — 같은 조각 안의 레인끼리는 서로의 그 조각을 참조하지 않는다).
/// </summary>
internal static class Argon2
{
    private const int BlockWords = 128;      // 1 KiB 블록 = 128 × 64비트
    private const int SyncPoints = 4;

    /// <summary>검증 전용: 다음 한 번은 네이티브 영역을 받은 직후 예외(AG08). 받은 수·돌려준 수.</summary>
    internal static bool TestFailAfterAlloc;
    internal static int TestAllocs, TestFrees;

    /// <summary>
    /// Argon2id 태그. memoryKiB = 블록 수(KiB), iterations = 패스 수, lanes = 병렬도. secret·ad 는 없으면 빈 값.
    /// 메모리 영역(기본 64 MiB)은 GC 힙이 아니라 운영체제에서 직접 받아, 끝나면 0 으로 덮고 바로 돌려준다
    /// (0.3.46 mem 측정: 고정 배열로 두면 덮어도 GC 가 붙잡아 잠금 해제·마스터 바꾸기마다 사용 메모리가 64 MiB 씩 남았다).
    /// </summary>
    public static unsafe byte[] Hash(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, int iterations, int memoryKiB, int lanes, int tagLength,
        ReadOnlySpan<byte> secret = default, ReadOnlySpan<byte> ad = default)
    {
        // 잘못된 인수·자리 넘침·지나친 크기는 계산·할당 전에 거절한다(출력 없음). 1Key 는 Kdf.Argon2Default 만 쓰고, 넓은 범위는 교차 시험용이다
        if (lanes is < 1 or > 64 || iterations is < 1 or > 64 || tagLength is < 4 or > 4096 || memoryKiB < 8 * lanes || memoryKiB > (1 << 20) || salt.Length < 8)
            throw new ArgumentOutOfRangeException(nameof(memoryKiB));
        int segment = memoryKiB / (SyncPoints * lanes);
        int laneLen = segment * SyncPoints;
        int blocks = laneLen * lanes;

        // H0
        Span<byte> h0 = stackalloc byte[72];   // 64 + 다음 두 LE32 자리
        var b = new Blake2b(64);
        Span<byte> le = stackalloc byte[4];
        void U32(ref Blake2b bb, uint v, Span<byte> tmp) { BinaryPrimitives.WriteUInt32LittleEndian(tmp, v); bb.Update(tmp); }
        U32(ref b, (uint)lanes, le); U32(ref b, (uint)tagLength, le); U32(ref b, (uint)memoryKiB, le); U32(ref b, (uint)iterations, le);
        U32(ref b, 0x13, le); U32(ref b, 2 /* Argon2id */, le);
        U32(ref b, (uint)password.Length, le); b.Update(password);
        U32(ref b, (uint)salt.Length, le); b.Update(salt);
        U32(ref b, (uint)secret.Length, le); b.Update(secret);
        U32(ref b, (uint)ad.Length, le); b.Update(ad);
        b.Final(h0[..64]);

        // 관리 버퍼는 네이티브 할당 전에 준비하고, 네이티브 영역은 받은 바로 다음부터 finally 가 맡는다(Codex 20:18 R47-1:
        // 받은 뒤의 할당·초기화가 예외로 끝나도 0 으로 덮고 한 번만 돌려준다 — 이 포인터는 GC 가 회수하지 않는다)
        byte[] blockBytes = new byte[1024];
        nuint bytes = (nuint)blocks * BlockWords * sizeof(ulong);
        void* raw = null;
        try
        {
            raw = NativeMemory.AlignedAlloc(bytes, 64);
            Interlocked.Increment(ref TestAllocs);
            if (TestFailAfterAlloc) { TestFailAfterAlloc = false; throw new OutOfMemoryException("test: after alloc"); }
            Span<ulong> mem = new(raw, blocks * BlockWords);
            // 각 레인의 첫 두 블록
            for (int l = 0; l < lanes; l++)
                for (int k = 0; k < 2; k++)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(h0[64..], (uint)k);
                    BinaryPrimitives.WriteUInt32LittleEndian(h0[68..], (uint)l);
                    HPrime(h0, blockBytes);
                    LoadBlock(blockBytes, mem.Slice((l * laneLen + k) * BlockWords, BlockWords));
                }

            for (int pass = 0; pass < iterations; pass++)
                for (int slice = 0; slice < SyncPoints; slice++)
                    for (int l = 0; l < lanes; l++)
                        FillSegment(mem, pass, slice, l, lanes, laneLen, segment, blocks, iterations);

            // 마지막 열의 XOR → 태그
            Span<ulong> c = stackalloc ulong[BlockWords];
            mem.Slice((laneLen - 1) * BlockWords, BlockWords).CopyTo(c);
            for (int l = 1; l < lanes; l++)
            {
                Span<ulong> last = mem.Slice((l * laneLen + laneLen - 1) * BlockWords, BlockWords);
                for (int w = 0; w < BlockWords; w++) c[w] ^= last[w];
            }
            for (int w = 0; w < BlockWords; w++) BinaryPrimitives.WriteUInt64LittleEndian(blockBytes.AsSpan(w * 8), c[w]);
            c.Clear();
            byte[] tag = new byte[tagLength];
            HPrime(blockBytes, tag);
            return tag;
        }
        finally
        {
            if (raw != null)
            {
                NativeMemory.Clear(raw, bytes);
                NativeMemory.AlignedFree(raw);
                Interlocked.Increment(ref TestFrees);
            }
            Array.Clear(blockBytes);
            h0.Clear();
        }
    }

    private static void LoadBlock(ReadOnlySpan<byte> src, Span<ulong> dst)
    {
        for (int w = 0; w < BlockWords; w++) dst[w] = BinaryPrimitives.ReadUInt64LittleEndian(src[(w * 8)..]);
    }

    /// <summary>가변 길이 해시 H'(RFC 9106 3.3).</summary>
    private static void HPrime(ReadOnlySpan<byte> input, Span<byte> output)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(len, (uint)output.Length);
        if (output.Length <= 64)
        {
            var b = new Blake2b(output.Length);
            b.Update(len); b.Update(input); b.Final(output);
            return;
        }
        Span<byte> v = stackalloc byte[64];
        var b1 = new Blake2b(64);
        b1.Update(len); b1.Update(input); b1.Final(v);
        int r = (output.Length + 31) / 32 - 2, at = 0;
        v[..32].CopyTo(output); at = 32;
        for (int i = 2; i <= r; i++)
        {
            var bi = new Blake2b(64);
            bi.Update(v); bi.Final(v);
            v[..32].CopyTo(output[at..]); at += 32;
        }
        var bl = new Blake2b(output.Length - at);
        bl.Update(v); bl.Final(output[at..]);
        v.Clear();
    }

    private static void FillSegment(Span<ulong> mem, int pass, int slice, int lane, int lanes, int laneLen, int segment, int blocks, int passes)
    {
        bool independent = pass == 0 && slice < SyncPoints / 2;   // Argon2id: 첫 패스의 앞 절반만 Argon2i 방식
        Span<ulong> address = stackalloc ulong[BlockWords], input = stackalloc ulong[BlockWords], zero = stackalloc ulong[BlockWords];
        address.Clear(); input.Clear(); zero.Clear();
        if (independent)
        {
            input[0] = (ulong)pass; input[1] = (ulong)lane; input[2] = (ulong)slice; input[3] = (ulong)blocks; input[4] = (ulong)passes; input[5] = 2;
        }
        int start = 0;
        if (pass == 0 && slice == 0)
        {
            start = 2;
            if (independent) NextAddresses(address, input, zero);
        }
        int curr = lane * laneLen + slice * segment + start;
        int prev = curr % laneLen == 0 ? curr + laneLen - 1 : curr - 1;
        for (int i = start; i < segment; i++, curr++, prev++)
        {
            if (curr % laneLen == 1) prev = curr - 1;
            ulong rnd;
            if (independent)
            {
                if (i % BlockWords == 0) NextAddresses(address, input, zero);
                rnd = address[i % BlockWords];
            }
            else rnd = mem[prev * BlockWords];
            int refLane = (int)((rnd >> 32) % (ulong)lanes);
            if (pass == 0 && slice == 0) refLane = lane;
            int refIndex = IndexAlpha(pass, slice, i, (uint)rnd, refLane == lane, laneLen, segment);
            FillBlock(mem.Slice(prev * BlockWords, BlockWords), mem.Slice((refLane * laneLen + refIndex) * BlockWords, BlockWords),
                mem.Slice(curr * BlockWords, BlockWords), withXor: pass != 0);
        }
        address.Clear(); input.Clear();
    }

    private static int IndexAlpha(int pass, int slice, int index, uint rnd, bool sameLane, int laneLen, int segment)
    {
        uint area;
        if (pass == 0)
        {
            if (slice == 0) area = (uint)(index - 1);
            else area = sameLane ? (uint)(slice * segment + index - 1) : (uint)(slice * segment + (index == 0 ? -1 : 0));
        }
        else area = sameLane ? (uint)(laneLen - segment + index - 1) : (uint)(laneLen - segment + (index == 0 ? -1 : 0));
        ulong rel = rnd;
        rel = rel * rel >> 32;
        rel = (ulong)area - 1 - ((ulong)area * rel >> 32);
        ulong startPos = pass != 0 && slice != SyncPoints - 1 ? (ulong)((slice + 1) * segment) : 0;
        return (int)((startPos + rel) % (ulong)laneLen);
    }

    private static void NextAddresses(Span<ulong> address, Span<ulong> input, ReadOnlySpan<ulong> zero)
    {
        input[6]++;
        FillBlock(zero, input, address, withXor: false);
        Span<ulong> copy = stackalloc ulong[BlockWords];
        address.CopyTo(copy);
        FillBlock(zero, copy, address, withXor: false);
        copy.Clear();
    }

    /// <summary>압축 함수 G: next = P(prev ⊕ ref) ⊕ (prev ⊕ ref) [⊕ next, 두 번째 패스부터].</summary>
    private static void FillBlock(ReadOnlySpan<ulong> prev, ReadOnlySpan<ulong> refb, Span<ulong> next, bool withXor)
    {
        Span<ulong> r = stackalloc ulong[BlockWords], t = stackalloc ulong[BlockWords];
        for (int w = 0; w < BlockWords; w++) r[w] = refb[w] ^ prev[w];
        r.CopyTo(t);
        if (withXor) for (int w = 0; w < BlockWords; w++) t[w] ^= next[w];
        for (int i = 0; i < 8; i++)
        {
            int o = 16 * i;
            Round(r, o, o + 1, o + 2, o + 3, o + 4, o + 5, o + 6, o + 7, o + 8, o + 9, o + 10, o + 11, o + 12, o + 13, o + 14, o + 15);
        }
        for (int i = 0; i < 8; i++)
        {
            int o = 2 * i;
            Round(r, o, o + 1, o + 16, o + 17, o + 32, o + 33, o + 48, o + 49, o + 64, o + 65, o + 80, o + 81, o + 96, o + 97, o + 112, o + 113);
        }
        for (int w = 0; w < BlockWords; w++) next[w] = t[w] ^ r[w];
        r.Clear(); t.Clear();
    }

    private static void Round(Span<ulong> v, int a0, int a1, int a2, int a3, int a4, int a5, int a6, int a7, int a8, int a9, int a10, int a11, int a12, int a13, int a14, int a15)
    {
        GB(v, a0, a4, a8, a12); GB(v, a1, a5, a9, a13); GB(v, a2, a6, a10, a14); GB(v, a3, a7, a11, a15);
        GB(v, a0, a5, a10, a15); GB(v, a1, a6, a11, a12); GB(v, a2, a7, a8, a13); GB(v, a3, a4, a9, a14);
    }

    private static void GB(Span<ulong> v, int a, int b, int c, int d)
    {
        ulong va = v[a], vb = v[b], vc = v[c], vd = v[d];
        va = va + vb + 2UL * (uint)va * (uint)vb; vd = BitOperations.RotateRight(vd ^ va, 32);
        vc = vc + vd + 2UL * (uint)vc * (uint)vd; vb = BitOperations.RotateRight(vb ^ vc, 24);
        va = va + vb + 2UL * (uint)va * (uint)vb; vd = BitOperations.RotateRight(vd ^ va, 16);
        vc = vc + vd + 2UL * (uint)vc * (uint)vd; vb = BitOperations.RotateRight(vb ^ vc, 63);
        v[a] = va; v[b] = vb; v[c] = vc; v[d] = vd;
    }
}

/// <summary>BLAKE2b(RFC 7693), 키 없음, 출력 1–64 바이트.</summary>
internal unsafe struct Blake2b
{
    private static readonly ulong[] IV =
    {
        0x6a09e667f3bcc908, 0xbb67ae8584caa73b, 0x3c6ef372fe94f82b, 0xa54ff53a5f1d36f1,
        0x510e527fade682d1, 0x9b05688c2b3e6c1f, 0x1f83d9abfb41bd6b, 0x5be0cd19137e2179,
    };
    private static readonly byte[] Sigma =
    {
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
        14, 10, 4, 8, 9, 15, 13, 6, 1, 12, 0, 2, 11, 7, 5, 3,
        11, 8, 12, 0, 5, 2, 15, 13, 10, 14, 3, 6, 7, 1, 9, 4,
        7, 9, 3, 1, 13, 12, 11, 14, 2, 6, 5, 10, 4, 0, 15, 8,
        9, 0, 5, 7, 2, 4, 10, 15, 14, 1, 11, 12, 6, 8, 3, 13,
        2, 12, 6, 10, 0, 11, 8, 3, 4, 13, 7, 5, 15, 14, 1, 9,
        12, 5, 1, 15, 14, 13, 4, 10, 0, 7, 6, 3, 9, 2, 8, 11,
        13, 11, 7, 14, 12, 1, 3, 9, 5, 0, 15, 4, 8, 6, 2, 10,
        6, 15, 14, 9, 11, 3, 0, 8, 12, 2, 13, 7, 1, 4, 10, 5,
        10, 2, 8, 4, 7, 6, 1, 5, 15, 11, 9, 14, 3, 12, 13, 0,
    };

    private fixed ulong _h[8];
    private fixed byte _buf[128];
    private int _bufLen, _outLen;
    private ulong _t0, _t1;

    public Blake2b(int outLen)
    {
        if (outLen is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(outLen));
        _outLen = outLen; _bufLen = 0; _t0 = _t1 = 0;
        for (int i = 0; i < 8; i++) _h[i] = IV[i];
        _h[0] ^= 0x01010000UL ^ (ulong)outLen;
    }

    public void Update(ReadOnlySpan<byte> data)
    {
        while (data.Length > 0)
        {
            if (_bufLen == 128)   // 버퍼가 찼고 더 올 것이 있다: 마지막 블록이 아니다
            {
                Add(128);
                fixed (byte* p = _buf) Compress(new ReadOnlySpan<byte>(p, 128), false);
                _bufLen = 0;
            }
            int n = Math.Min(128 - _bufLen, data.Length);
            fixed (byte* p = _buf) data[..n].CopyTo(new Span<byte>(p + _bufLen, n));
            _bufLen += n;
            data = data[n..];
        }
    }

    public void Final(Span<byte> output)
    {
        Add((ulong)_bufLen);
        fixed (byte* p = _buf)
        {
            new Span<byte>(p + _bufLen, 128 - _bufLen).Clear();
            Compress(new ReadOnlySpan<byte>(p, 128), true);
            new Span<byte>(p, 128).Clear();
        }
        Span<byte> full = stackalloc byte[64];
        for (int i = 0; i < 8; i++) BinaryPrimitives.WriteUInt64LittleEndian(full[(i * 8)..], _h[i]);
        full[.._outLen].CopyTo(output);
        full.Clear();
        for (int i = 0; i < 8; i++) _h[i] = 0;
    }

    private void Add(ulong n) { _t0 += n; if (_t0 < n) _t1++; }

    private void Compress(ReadOnlySpan<byte> block, bool last)
    {
        Span<ulong> m = stackalloc ulong[16], v = stackalloc ulong[16];
        for (int i = 0; i < 16; i++) m[i] = BinaryPrimitives.ReadUInt64LittleEndian(block[(i * 8)..]);
        for (int i = 0; i < 8; i++) { v[i] = _h[i]; v[i + 8] = IV[i]; }
        v[12] ^= _t0; v[13] ^= _t1;
        if (last) v[14] = ~v[14];
        for (int r = 0; r < 12; r++)
        {
            int s = (r % 10) * 16;
            G(v, 0, 4, 8, 12, m[Sigma[s + 0]], m[Sigma[s + 1]]);
            G(v, 1, 5, 9, 13, m[Sigma[s + 2]], m[Sigma[s + 3]]);
            G(v, 2, 6, 10, 14, m[Sigma[s + 4]], m[Sigma[s + 5]]);
            G(v, 3, 7, 11, 15, m[Sigma[s + 6]], m[Sigma[s + 7]]);
            G(v, 0, 5, 10, 15, m[Sigma[s + 8]], m[Sigma[s + 9]]);
            G(v, 1, 6, 11, 12, m[Sigma[s + 10]], m[Sigma[s + 11]]);
            G(v, 2, 7, 8, 13, m[Sigma[s + 12]], m[Sigma[s + 13]]);
            G(v, 3, 4, 9, 14, m[Sigma[s + 14]], m[Sigma[s + 15]]);
        }
        for (int i = 0; i < 8; i++) _h[i] ^= v[i] ^ v[i + 8];
        m.Clear(); v.Clear();
    }

    private static void G(Span<ulong> v, int a, int b, int c, int d, ulong x, ulong y)
    {
        v[a] = v[a] + v[b] + x; v[d] = BitOperations.RotateRight(v[d] ^ v[a], 32);
        v[c] = v[c] + v[d]; v[b] = BitOperations.RotateRight(v[b] ^ v[c], 24);
        v[a] = v[a] + v[b] + y; v[d] = BitOperations.RotateRight(v[d] ^ v[a], 16);
        v[c] = v[c] + v[d]; v[b] = BitOperations.RotateRight(v[b] ^ v[c], 63);
    }
}
