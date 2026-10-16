import XCTest
@testable import OneKeyCore

/// Crypto·Backup: Windows src/OneKey/Crypto.cs·Backup.cs 의 바이트 배치를 시험으로 묶는다.
final class CryptoBackupTests: XCTestCase {
    private func hex(_ b: [UInt8]) -> String { b.map { String(format: "%02x", $0) }.joined() }
    private let pw = Array("백업 비밀번호".utf8)

    // RFC 7914 11절 PBKDF2-HMAC-SHA256("passwd", "salt", c=1) 의 앞 32 바이트
    func testPbkdf2Vector() throws {
        let k = try Crypto.deriveKey(master: Array("passwd".utf8), salt: Array("salt".utf8), kdf: .pbkdf2(1))
        XCTAssertEqual(hex(k), "55ac046e56e3089fec1691c22544b605f94185216dde0465e68b9d57c20dacbc")
    }

    func testArgon2OnlyDefaultProfile() {
        let other = Kdf(argon2id: true, iterations: 0, memoryKiB: 1 << 20, passes: 3, lanes: 1)
        XCTAssertThrowsError(try Crypto.deriveKey(master: pw, salt: Crypto.newSalt(), kdf: other)) {
            XCTAssertEqual($0 as? Crypto.Failure, .unsupportedProfile)
        }
        XCTAssertEqual(Kdf.argon2Default.aadText, "argon2id,v=19,m=65536,t=3,p=1")
        XCTAssertEqual(Kdf.pbkdf2(600_000).aadText, "600000")
    }

    // 블록 = [salt|nonce|tag|암호문]; 틀린 키·바뀐 AAD·바뀐 바이트는 nil
    func testBlobLayoutAndTamper() throws {
        let key = try Crypto.deriveKey(master: pw, salt: Array("0123456789abcdef".utf8), kdf: .pbkdf2(1000))
        let salt = Array("0123456789abcdef".utf8), aad = Array("hdr".utf8), pt = Array("비밀".utf8)
        let blob = try Crypto.encrypt(pt, key: key, salt: salt, aad: aad)
        XCTAssertEqual(blob.count, Crypto.minBlobLength + pt.count)
        XCTAssertEqual(Crypto.saltOf(blob), salt)
        XCTAssertEqual(Crypto.decrypt(blob, key: key, aad: aad), pt)
        XCTAssertNil(Crypto.decrypt(blob, key: [UInt8](repeating: 0, count: 32), aad: aad))
        XCTAssertNil(Crypto.decrypt(blob, key: key, aad: Array("hdR".utf8)))
        var bad = blob; bad[bad.count - 1] ^= 1
        XCTAssertNil(Crypto.decrypt(bad, key: key, aad: aad))
        XCTAssertNil(Crypto.decrypt(Array(blob[0..<43]), key: key, aad: aad))
        XCTAssertNotEqual(try Crypto.encrypt(pt, key: key, salt: salt, aad: aad), blob)   // nonce 는 늘 새로
    }

    // 한 번 만든 백업으로 머리 배치·열기·틀린 비밀번호·변조를 본다(Argon2 64 MiB 계산은 3번)
    func testBackupRoundTripAndHeader() throws {
        let file = try XCTUnwrap(Backup.build(password: pw, settings: Array("name=a".utf8), secrets: Array("pw=1".utf8), launch: Array("l".utf8)))
        // 머리 55 바이트: 표식 | u16 1 | u8 1 | m 65536 | t 3 | p 1 | salt | nonce | 평문 길이
        XCTAssertEqual(Array(file[0..<11]), Array("1KEYBAK1".utf8) + [1, 0, 1])
        XCTAssertEqual(hex(Array(file[11..<23])), "000001000300000001000000")
        let plainLen = 5 + 6 + 5 + 4 + 5 + 1
        XCTAssertEqual(readLE32(file, 51), plainLen)
        XCTAssertEqual(file.count, 55 + 16 + plainLen)

        guard case .success(var c) = Backup.open(file, password: pw) else { return XCTFail("open") }
        XCTAssertEqual(c.settings, Array("name=a".utf8)); XCTAssertEqual(c.secrets, Array("pw=1".utf8)); XCTAssertEqual(c.launch, Array("l".utf8))
        c.wipe()
        XCTAssertNil(c.secrets)

        guard case .failure(let e) = Backup.open(file, password: Array("x".utf8)) else { return XCTFail("wrong password opened") }
        XCTAssertEqual(e, .wrongPasswordOrDamaged)
        var bad = file; bad[30] ^= 1   // salt 는 AAD 이자 키 재료 — 바뀌면 열리지 않는다
        guard case .failure(let e2) = Backup.open(bad, password: pw) else { return XCTFail("tampered opened") }
        XCTAssertEqual(e2, .wrongPasswordOrDamaged)
    }

    // 머리 검사는 키 유도 전에 끝난다
    func testHeaderRejections() {
        let hdr = Array("1KEYBAK1".utf8) + [1, 0, 1] + le32(65536) + le32(3) + le32(1) + [UInt8](repeating: 0, count: 28)
        func file(_ h: [UInt8], body: Int = 16 + 5) -> [UInt8] { h + le32(body - 16) + [UInt8](repeating: 0, count: body) }
        XCTAssertNil(Backup.checkHeader(file(hdr)))
        XCTAssertEqual(Backup.checkHeader([UInt8](repeating: 0, count: 80)), .notBackup)
        var v2 = hdr; v2[8] = 2
        XCTAssertEqual(Backup.checkHeader(file(v2)), .newerFormat)
        var v0 = hdr; v0[8] = 0
        XCTAssertEqual(Backup.checkHeader(file(v0)), .damaged)
        var kdf = hdr; kdf[10] = 2
        XCTAssertEqual(Backup.checkHeader(file(kdf)), .badProfile)
        var bigM = hdr; bigM[13] = 0x10   // m = 1 GiB
        XCTAssertEqual(Backup.checkHeader(file(bigM)), .badProfile)
        XCTAssertEqual(Backup.checkHeader(file(hdr) + [0]), .damaged)
        XCTAssertEqual(Backup.checkHeader([UInt8](repeating: 0, count: Backup.maxFile + 1)), .tooLarge)
    }

    func testStrictUTF8() {
        XCTAssertTrue(Backup.isStrictUTF8(ArraySlice(Array("가a".utf8))))
        XCTAssertFalse(Backup.isStrictUTF8([0xff]))
        XCTAssertFalse(Backup.isStrictUTF8([0xed, 0xa0, 0x80]))   // 서로게이트
        XCTAssertFalse(Backup.isStrictUTF8([0xc0, 0x80]))         // 과잉 길이
    }
}
