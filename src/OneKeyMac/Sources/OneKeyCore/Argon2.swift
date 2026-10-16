import Darwin

/// Argon2id v1.3(RFC 9106). Windows src/OneKey/Argon2.cs 를 그대로 옮긴 것 — 외부 패키지 없음.
/// 검증: Tests/OneKeyCoreTests/Argon2Tests.swift (Windows SiteSelfTest AG01–AG06 과 같은 시험 값).
/// 레인은 순서대로 한 스레드에서 계산한다(결과는 병렬 계산과 같다).
public enum Argon2 {
    public enum Failure: Error, Equatable { case invalidParameters }

    private static let blockWords = 128   // 1 KiB 블록 = 128 × 64비트
    private static let syncPoints = 4

    /// Argon2id 태그. memoryKiB = 블록 수(KiB), iterations = 패스 수, lanes = 병렬도. secret·ad 는 없으면 빈 값.
    /// 메모리 영역은 끝나면(예외 포함) 0 으로 덮고 바로 돌려준다.
    public static func hash(password: [UInt8], salt: [UInt8], iterations: Int, memoryKiB: Int, lanes: Int, tagLength: Int,
                            secret: [UInt8] = [], ad: [UInt8] = []) throws(Failure) -> [UInt8] {
        // 잘못된 인수·지나친 크기는 계산·할당 전에 거절한다(Windows 와 같은 범위)
        guard (1...64).contains(lanes), (1...64).contains(iterations), (4...4096).contains(tagLength),
              memoryKiB >= 8 * lanes, memoryKiB <= 1 << 20, salt.count >= 8 else { throw .invalidParameters }
        let segment = memoryKiB / (syncPoints * lanes)
        let laneLen = segment * syncPoints
        let blocks = laneLen * lanes

        // H0 (64 바이트 + 블록 번호·레인 번호 LE32 두 자리)
        var b = Blake2b(outLen: 64)
        for v in [lanes, tagLength, memoryKiB, iterations, 0x13, 2 /* Argon2id */] { b.update(le32(v)) }
        for part in [password, salt, secret, ad] { b.update(le32(part.count)); b.update(part) }
        var h0 = b.final() + [UInt8](repeating: 0, count: 8)

        let words = blocks * blockWords
        let mem = UnsafeMutablePointer<UInt64>.allocate(capacity: words)
        // 작업 공간: r, t, address, input, zero (각 128 워드)
        let scratch = UnsafeMutablePointer<UInt64>.allocate(capacity: 5 * blockWords)
        var blockBytes = [UInt8](repeating: 0, count: 1024)
        defer {
            memset_s(mem, words * 8, 0, words * 8)
            mem.deallocate()
            memset_s(scratch, 5 * blockWords * 8, 0, 5 * blockWords * 8)
            scratch.deallocate()
            wipe(&blockBytes)
            wipe(&h0)
        }

        // 각 레인의 첫 두 블록
        for l in 0..<lanes {
            for k in 0..<2 {
                h0.replaceSubrange(64..<68, with: le32(k))
                h0.replaceSubrange(68..<72, with: le32(l))
                var block = hPrime(h0, outLen: 1024)
                let base = mem + (l * laneLen + k) * blockWords
                for w in 0..<blockWords { base[w] = loadLE64(block, w * 8) }
                wipe(&block)
            }
        }

        for pass in 0..<iterations {
            for slice in 0..<syncPoints {
                for l in 0..<lanes {
                    fillSegment(mem, scratch, pass: pass, slice: slice, lane: l, lanes: lanes, laneLen: laneLen,
                                segment: segment, blocks: blocks, passes: iterations)
                }
            }
        }

        // 마지막 열의 XOR → 태그
        for w in 0..<blockWords {
            var c = mem[(laneLen - 1) * blockWords + w]
            for l in 1..<lanes { c ^= mem[(l * laneLen + laneLen - 1) * blockWords + w] }
            for i in 0..<8 { blockBytes[w * 8 + i] = UInt8(truncatingIfNeeded: c >> (8 * UInt64(i))) }
        }
        return hPrime(blockBytes, outLen: tagLength)
    }

    /// 가변 길이 해시 H'(RFC 9106 3.3).
    static func hPrime(_ input: [UInt8], outLen: Int) -> [UInt8] {
        if outLen <= 64 {
            var b = Blake2b(outLen: outLen)
            b.update(le32(outLen)); b.update(input)
            return b.final()
        }
        var b1 = Blake2b(outLen: 64)
        b1.update(le32(outLen)); b1.update(input)
        var v = b1.final()
        var out = Array(v[0..<32])
        out.reserveCapacity(outLen)
        let r = (outLen + 31) / 32 - 2
        if r >= 2 {
            for _ in 2...r {
                var bi = Blake2b(outLen: 64)
                bi.update(v)
                v = bi.final()
                out += v[0..<32]
            }
        }
        var bl = Blake2b(outLen: outLen - out.count)
        bl.update(v)
        out += bl.final()
        wipe(&v)
        return out
    }

    private static func fillSegment(_ mem: UnsafeMutablePointer<UInt64>, _ scratch: UnsafeMutablePointer<UInt64>,
                                    pass: Int, slice: Int, lane: Int, lanes: Int, laneLen: Int, segment: Int, blocks: Int, passes: Int) {
        let address = scratch + 2 * blockWords, input = scratch + 3 * blockWords, zero = scratch + 4 * blockWords
        let independent = pass == 0 && slice < syncPoints / 2   // Argon2id: 첫 패스의 앞 절반만 Argon2i 방식
        address.update(repeating: 0, count: 3 * blockWords)
        if independent {
            input[0] = UInt64(pass); input[1] = UInt64(lane); input[2] = UInt64(slice)
            input[3] = UInt64(blocks); input[4] = UInt64(passes); input[5] = 2
        }
        var start = 0
        if pass == 0 && slice == 0 {
            start = 2
            if independent { nextAddresses(address, input, zero, scratch) }
        }
        var curr = lane * laneLen + slice * segment + start
        var prev = curr % laneLen == 0 ? curr + laneLen - 1 : curr - 1
        for i in start..<segment {
            if curr % laneLen == 1 { prev = curr - 1 }
            let rnd: UInt64
            if independent {
                if i % blockWords == 0 { nextAddresses(address, input, zero, scratch) }
                rnd = address[i % blockWords]
            } else {
                rnd = mem[prev * blockWords]
            }
            var refLane = Int((rnd >> 32) % UInt64(lanes))
            if pass == 0 && slice == 0 { refLane = lane }
            let refIndex = indexAlpha(pass: pass, slice: slice, index: i, rnd: UInt32(truncatingIfNeeded: rnd),
                                      sameLane: refLane == lane, laneLen: laneLen, segment: segment)
            fillBlock(mem + prev * blockWords, mem + (refLane * laneLen + refIndex) * blockWords, mem + curr * blockWords,
                      withXor: pass != 0, scratch)
            curr += 1; prev += 1
        }
    }

    private static func indexAlpha(pass: Int, slice: Int, index: Int, rnd: UInt32, sameLane: Bool, laneLen: Int, segment: Int) -> Int {
        let area: Int
        if pass == 0 {
            if slice == 0 { area = index - 1 }
            else { area = sameLane ? slice * segment + index - 1 : slice * segment + (index == 0 ? -1 : 0) }
        } else {
            area = sameLane ? laneLen - segment + index - 1 : laneLen - segment + (index == 0 ? -1 : 0)
        }
        var rel = UInt64(rnd)
        rel = (rel &* rel) >> 32   // Swift 는 >> 가 곱셈보다 먼저다
        rel = UInt64(area) - 1 - ((UInt64(area) * rel) >> 32)
        let startPos = pass != 0 && slice != syncPoints - 1 ? UInt64((slice + 1) * segment) : 0
        return Int((startPos + rel) % UInt64(laneLen))
    }

    private static func nextAddresses(_ address: UnsafeMutablePointer<UInt64>, _ input: UnsafeMutablePointer<UInt64>,
                                      _ zero: UnsafeMutablePointer<UInt64>, _ scratch: UnsafeMutablePointer<UInt64>) {
        input[6] &+= 1
        fillBlock(zero, input, address, withXor: false, scratch)
        // ref 와 next 가 같아도 된다: fillBlock 은 ref 를 다 읽은 뒤에야 next 를 쓴다
        fillBlock(zero, address, address, withXor: false, scratch)
    }

    /// 압축 함수 G: next = P(prev ⊕ ref) ⊕ (prev ⊕ ref) [⊕ next, 두 번째 패스부터].
    private static func fillBlock(_ prev: UnsafeMutablePointer<UInt64>, _ refb: UnsafeMutablePointer<UInt64>,
                                  _ next: UnsafeMutablePointer<UInt64>, withXor: Bool, _ scratch: UnsafeMutablePointer<UInt64>) {
        let r = scratch, t = scratch + blockWords
        for w in 0..<blockWords { r[w] = refb[w] ^ prev[w] }
        for w in 0..<blockWords { t[w] = withXor ? r[w] ^ next[w] : r[w] }
        for i in 0..<8 {
            let o = 16 * i
            round(r, o, o + 1, o + 2, o + 3, o + 4, o + 5, o + 6, o + 7, o + 8, o + 9, o + 10, o + 11, o + 12, o + 13, o + 14, o + 15)
        }
        for i in 0..<8 {
            let o = 2 * i
            round(r, o, o + 1, o + 16, o + 17, o + 32, o + 33, o + 48, o + 49, o + 64, o + 65, o + 80, o + 81, o + 96, o + 97, o + 112, o + 113)
        }
        for w in 0..<blockWords { next[w] = t[w] ^ r[w] }
    }

    @inline(__always)
    private static func round(_ v: UnsafeMutablePointer<UInt64>, _ a0: Int, _ a1: Int, _ a2: Int, _ a3: Int, _ a4: Int, _ a5: Int,
                              _ a6: Int, _ a7: Int, _ a8: Int, _ a9: Int, _ a10: Int, _ a11: Int, _ a12: Int, _ a13: Int, _ a14: Int, _ a15: Int) {
        gb(v, a0, a4, a8, a12); gb(v, a1, a5, a9, a13); gb(v, a2, a6, a10, a14); gb(v, a3, a7, a11, a15)
        gb(v, a0, a5, a10, a15); gb(v, a1, a6, a11, a12); gb(v, a2, a7, a8, a13); gb(v, a3, a4, a9, a14)
    }

    @inline(__always)
    private static func gb(_ v: UnsafeMutablePointer<UInt64>, _ a: Int, _ b: Int, _ c: Int, _ d: Int) {
        var va = v[a], vb = v[b], vc = v[c], vd = v[d]
        va = va &+ vb &+ 2 &* (va & 0xffff_ffff) &* (vb & 0xffff_ffff); vd = rotr(vd ^ va, 32)
        vc = vc &+ vd &+ 2 &* (vc & 0xffff_ffff) &* (vd & 0xffff_ffff); vb = rotr(vb ^ vc, 24)
        va = va &+ vb &+ 2 &* (va & 0xffff_ffff) &* (vb & 0xffff_ffff); vd = rotr(vd ^ va, 16)
        vc = vc &+ vd &+ 2 &* (vc & 0xffff_ffff) &* (vd & 0xffff_ffff); vb = rotr(vb ^ vc, 63)
        v[a] = va; v[b] = vb; v[c] = vc; v[d] = vd
    }
}

/// BLAKE2b(RFC 7693), 키 없음, 출력 1–64 바이트.
struct Blake2b {
    private static let iv: [UInt64] = [
        0x6a09e667f3bcc908, 0xbb67ae8584caa73b, 0x3c6ef372fe94f82b, 0xa54ff53a5f1d36f1,
        0x510e527fade682d1, 0x9b05688c2b3e6c1f, 0x1f83d9abfb41bd6b, 0x5be0cd19137e2179,
    ]
    private static let sigma: [Int] = [
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
    ]

    private var h: [UInt64]
    private var buf = [UInt8](repeating: 0, count: 128)
    private var bufLen = 0
    private let outLen: Int
    private var t0: UInt64 = 0, t1: UInt64 = 0

    init(outLen: Int) {
        precondition((1...64).contains(outLen), "BLAKE2b 출력은 1–64 바이트")
        self.outLen = outLen
        h = Self.iv
        h[0] ^= 0x0101_0000 ^ UInt64(outLen)
    }

    mutating func update(_ data: some Collection<UInt8>) {
        var i = data.startIndex
        while i != data.endIndex {
            if bufLen == 128 {   // 버퍼가 찼고 더 올 것이 있다: 마지막 블록이 아니다
                add(128)
                compress(last: false)
                bufLen = 0
            }
            buf[bufLen] = data[i]
            bufLen += 1
            i = data.index(after: i)
        }
    }

    mutating func final() -> [UInt8] {
        add(UInt64(bufLen))
        for k in bufLen..<128 { buf[k] = 0 }
        compress(last: true)
        var out = [UInt8](repeating: 0, count: outLen)
        for k in 0..<outLen { out[k] = UInt8(truncatingIfNeeded: h[k / 8] >> (8 * UInt64(k % 8))) }
        wipe(&buf)
        for k in 0..<8 { h[k] = 0 }
        return out
    }

    private mutating func add(_ n: UInt64) { t0 &+= n; if t0 < n { t1 &+= 1 } }

    private mutating func compress(last: Bool) {
        var m = [UInt64](repeating: 0, count: 16)
        for k in 0..<16 { m[k] = loadLE64(buf, k * 8) }
        var v = h + Self.iv
        v[12] ^= t0; v[13] ^= t1
        if last { v[14] = ~v[14] }
        for r in 0..<12 {
            let s = (r % 10) * 16
            let sg = Self.sigma
            Self.g(&v, 0, 4, 8, 12, m[sg[s + 0]], m[sg[s + 1]])
            Self.g(&v, 1, 5, 9, 13, m[sg[s + 2]], m[sg[s + 3]])
            Self.g(&v, 2, 6, 10, 14, m[sg[s + 4]], m[sg[s + 5]])
            Self.g(&v, 3, 7, 11, 15, m[sg[s + 6]], m[sg[s + 7]])
            Self.g(&v, 0, 5, 10, 15, m[sg[s + 8]], m[sg[s + 9]])
            Self.g(&v, 1, 6, 11, 12, m[sg[s + 10]], m[sg[s + 11]])
            Self.g(&v, 2, 7, 8, 13, m[sg[s + 12]], m[sg[s + 13]])
            Self.g(&v, 3, 4, 9, 14, m[sg[s + 14]], m[sg[s + 15]])
        }
        for k in 0..<8 { h[k] ^= v[k] ^ v[k + 8] }
        for k in 0..<16 { m[k] = 0; v[k] = 0 }
    }

    private static func g(_ v: inout [UInt64], _ a: Int, _ b: Int, _ c: Int, _ d: Int, _ x: UInt64, _ y: UInt64) {
        v[a] = v[a] &+ v[b] &+ x; v[d] = rotr(v[d] ^ v[a], 32)
        v[c] = v[c] &+ v[d]; v[b] = rotr(v[b] ^ v[c], 24)
        v[a] = v[a] &+ v[b] &+ y; v[d] = rotr(v[d] ^ v[a], 16)
        v[c] = v[c] &+ v[d]; v[b] = rotr(v[b] ^ v[c], 63)
    }
}

@inline(__always) private func rotr(_ x: UInt64, _ n: UInt64) -> UInt64 { (x &>> n) | (x &<< (64 - n)) }

private func loadLE64(_ b: [UInt8], _ at: Int) -> UInt64 {
    var r: UInt64 = 0
    for i in 0..<8 { r |= UInt64(b[at + i]) << (8 * UInt64(i)) }
    return r
}

/// 비밀이 담겼던 배열을 0 으로 덮는다(최적화로 지워지지 않게 memset_s).
func wipe(_ a: inout [UInt8]) {
    let n = a.count
    a.withUnsafeMutableBytes { p in if let base = p.baseAddress { _ = memset_s(base, n, 0, n) } }
}
