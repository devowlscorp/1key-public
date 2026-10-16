import CommonCrypto
import CryptoKit
import Foundation

/// 키 유도 방식. 파일 헤더에 적히고 AAD 로 묶인다(Windows src/OneKey/Crypto.cs Kdf 와 같은 값).
public struct Kdf: Equatable, Sendable {
    public let argon2id: Bool
    public let iterations: Int, memoryKiB: Int, passes: Int, lanes: Int

    /// PBKDF2-HMAC-SHA256(형식 2–9).
    public static func pbkdf2(_ iterations: Int) -> Kdf { Kdf(argon2id: false, iterations: iterations, memoryKiB: 0, passes: 0, lanes: 0) }

    /// 지원하는 유일한 Argon2id 프로파일(형식 10): m = 64 MiB, t = 3, p = 1. 다른 값은 계산 전에 거절한다.
    public static let argon2Default = Kdf(argon2id: true, iterations: 0, memoryKiB: 65536, passes: 3, lanes: 1)

    /// AAD 에 넣는 표기.
    public var aadText: String { argon2id ? "argon2id,v=19,m=\(memoryKiB),t=\(passes),p=\(lanes)" : String(iterations) }
}

/// 마스터 비밀번호로 저장 내용을 암호화한다: 키 유도(PBKDF2-SHA256 또는 Argon2id) → AES-256-GCM.
/// 블록 = [salt 16 | nonce 12 | tag 16 | 암호문] — Windows Crypto.Encrypt 와 같은 바이트 배치.
public enum Crypto {
    public static let saltLen = 16, nonceLen = 12, tagLen = 16, keyLen = 32
    /// salt + nonce + tag. 이보다 짧은 블록은 형식상 있을 수 없다(손상).
    public static let minBlobLength = saltLen + nonceLen + tagLen

    public enum Failure: Error, Equatable { case unsupportedProfile, kdfFailed }

    /// 새 무작위 바이트(SystemRandomNumberGenerator = 운영체제 CSPRNG).
    public static func randomBytes(_ n: Int) -> [UInt8] {
        var g = SystemRandomNumberGenerator()
        return (0..<n).map { _ in UInt8.random(in: .min ... .max, using: &g) }
    }

    public static func newSalt() -> [UInt8] { randomBytes(saltLen) }

    /// 블록 앞의 salt(그 블록의 키를 유도한 값). 너무 짧으면 nil.
    public static func saltOf(_ blob: [UInt8]) -> [UInt8]? { blob.count >= minBlobLength ? Array(blob[0..<saltLen]) : nil }

    /// 마스터(UTF-8 바이트, 부르는 쪽이 지운다) + salt → 32바이트 키. 다 쓰면 부르는 쪽이 wipe. Argon2id 는 argon2Default 만 계산한다.
    public static func deriveKey(master: [UInt8], salt: [UInt8], kdf: Kdf) throws(Failure) -> [UInt8] {
        if kdf.argon2id {
            guard kdf == .argon2Default else { throw .unsupportedProfile }
            do { return try Argon2.hash(password: master, salt: salt, iterations: kdf.passes, memoryKiB: kdf.memoryKiB, lanes: kdf.lanes, tagLength: keyLen) }
            catch { throw .kdfFailed }
        }
        var key = [UInt8](repeating: 0, count: keyLen)
        let rc = master.withUnsafeBufferPointer { pw in
            pw.withMemoryRebound(to: CChar.self) { pwc in
                CCKeyDerivationPBKDF(CCPBKDFAlgorithm(kCCPBKDF2), pwc.baseAddress, pw.count, salt, salt.count,
                                     CCPseudoRandomAlgorithm(kCCPRFHmacAlgSHA256), UInt32(clamping: kdf.iterations), &key, keyLen)
            }
        }
        guard rc == kCCSuccess else { wipe(&key); throw .kdfFailed }
        return key
    }

    /// 두 키가 같은가(일정 시간 비교).
    public static func sameKey(_ a: [UInt8], _ b: [UInt8]) -> Bool {
        guard a.count == b.count else { return false }
        var d: UInt8 = 0
        for i in a.indices { d |= a[i] ^ b[i] }
        return d == 0
    }

    /// 평문을 [salt|nonce|tag|암호문] 으로. salt 는 key 를 유도한 값. nonce 는 늘 새로.
    public static func encrypt(_ plaintext: [UInt8], key: [UInt8], salt: [UInt8], aad: [UInt8]) throws -> [UInt8] {
        let nonce = randomBytes(nonceLen)
        let (ct, tag) = try seal(plaintext, key: key, nonce: nonce, aad: aad)
        return salt + nonce + tag + ct
    }

    /// 복호화. 키가 틀리거나(마스터 불일치) 블록·AAD 가 변조되면 nil. 평문은 다 쓰면 부르는 쪽이 wipe.
    public static func decrypt(_ blob: [UInt8], key: [UInt8], aad: [UInt8]) -> [UInt8]? {
        guard blob.count >= minBlobLength else { return nil }
        return open(ciphertext: blob[minBlobLength...], key: key, nonce: blob[saltLen..<(saltLen + nonceLen)],
                    tag: blob[(saltLen + nonceLen)..<minBlobLength], aad: aad)
    }

    static func seal(_ plaintext: [UInt8], key: [UInt8], nonce: [UInt8], aad: [UInt8]) throws -> (ciphertext: [UInt8], tag: [UInt8]) {
        let box = try AES.GCM.seal(plaintext, using: SymmetricKey(data: key), nonce: AES.GCM.Nonce(data: nonce), authenticating: aad)
        return (Array(box.ciphertext), Array(box.tag))
    }

    static func open(ciphertext: ArraySlice<UInt8>, key: [UInt8], nonce: ArraySlice<UInt8>, tag: ArraySlice<UInt8>, aad: [UInt8]) -> [UInt8]? {
        guard let n = try? AES.GCM.Nonce(data: nonce),
              let box = try? AES.GCM.SealedBox(nonce: n, ciphertext: ciphertext, tag: tag),
              let pt = try? AES.GCM.open(box, using: SymmetricKey(data: key), authenticating: aad) else { return nil }   // 인증 실패
        return Array(pt)
    }
}
