import Foundation
import XCTest
@testable import OneKeyCore

/// 바깥 층(Vault)·파일 쓰기·잠금 상태 기계. 키체인은 건드리지 않는다(시험 중 대화상자 방지) — 감싸기 키는 시험이 직접 만든다.
final class VaultLockTests: XCTestCase {
    private let wrap = Crypto.randomBytes(32), pt = Array("secret=abc\n".utf8)

    func testVaultRoundTripAndRejections() throws {
        let blob = try Vault.protect(pt, wrapKey: wrap, purpose: Vault.configPurpose)
        XCTAssertEqual(Array(blob[0..<8]), Array("1KEYMAC1".utf8))
        XCTAssertEqual(Vault.unprotect(blob, wrapKey: wrap, purpose: Vault.configPurpose), pt)
        XCTAssertNil(Vault.unprotect(blob, wrapKey: Crypto.randomBytes(32), purpose: Vault.configPurpose))   // 다른 Mac·다른 계정의 키
        XCTAssertNil(Vault.unprotect(blob, wrapKey: wrap, purpose: Array("1Key/launch/v1".utf8)))           // 다른 용도의 파일
        var bad = blob; bad[8] ^= 1
        XCTAssertNil(Vault.unprotect(bad, wrapKey: wrap, purpose: Vault.configPurpose))
        XCTAssertNil(Vault.unprotect(Array(blob[0..<35]), wrapKey: wrap, purpose: Vault.configPurpose))
    }

    func testSecureFileWritesOwnerOnly() throws {
        let dir = NSTemporaryDirectory() + "onekey-\(UUID().uuidString)/sub"
        let path = dir + "/config.dat"
        defer { try? FileManager.default.removeItem(atPath: (dir as NSString).deletingLastPathComponent) }
        try FileManager.default.createDirectory(atPath: (dir as NSString).deletingLastPathComponent, withIntermediateDirectories: true)
        XCTAssertNil(try SecureFile.read(path, max: 100))
        try SecureFile.write([1, 2, 3], to: path)
        try SecureFile.write([4, 5], to: path)   // 바꿔치기
        XCTAssertEqual(try SecureFile.read(path, max: 100), [4, 5])
        let attrs = try FileManager.default.attributesOfItem(atPath: path)
        XCTAssertEqual((attrs[.posixPermissions] as? NSNumber)?.intValue, 0o600)
        let dattrs = try FileManager.default.attributesOfItem(atPath: dir)
        XCTAssertEqual((dattrs[.posixPermissions] as? NSNumber)?.intValue, 0o700)
        XCTAssertFalse(FileManager.default.fileExists(atPath: path + ".tmp"))
        XCTAssertThrowsError(try SecureFile.read(path, max: 1)) { XCTAssertEqual($0 as? SecureFile.Failure, .tooLarge) }
    }

    // 상태 전이: 없음 → 설정(해제) → 잠금 → 틀린 비밀번호(잠김 유지) → 해제 → 자동 잠금
    func testLockTransitions() throws {
        let kdf = Kdf.pbkdf2(1000), master = Array("마스터".utf8)
        var lock = MasterLock(hasMaster: false, autoLockMinutes: 10)
        XCTAssertEqual(lock.state, .noMaster)
        XCTAssertNil(lock.withKey { k, _, _ in k })
        try lock.setMaster(master, kdf: kdf)
        XCTAssertEqual(lock.state, .unlocked)

        // 해제 중 저장: 키로 블록을 만든다(Windows 와 같이 같은 키·salt + 새 nonce)
        let aad = Array("1Key-aad-v10".utf8)
        let blob = try XCTUnwrap(try lock.withKey { k, s, _ in try Crypto.encrypt(Array("pw".utf8), key: k, salt: s, aad: aad) })

        lock.lock()
        XCTAssertEqual(lock.state, .locked)
        XCTAssertNil(lock.withKey { k, _, _ in k })
        XCTAssertThrowsError(try lock.setMaster(master, kdf: kdf)) { XCTAssertEqual($0 as? MasterLock.Failure, .wrongState) }

        let salt = try XCTUnwrap(Crypto.saltOf(blob))
        let verify: ([UInt8]) -> Bool = { Crypto.decrypt(blob, key: $0, aad: aad) != nil }
        XCTAssertThrowsError(try lock.unlock(Array("틀림".utf8), salt: salt, kdf: kdf, verify: verify)) {
            XCTAssertEqual($0 as? MasterLock.Failure, .wrongPassword)
        }
        XCTAssertEqual(lock.state, .locked)
        try lock.unlock(master, salt: salt, kdf: kdf, verify: verify)
        XCTAssertEqual(lock.state, .unlocked)
        XCTAssertThrowsError(try lock.unlock(master, salt: salt, kdf: kdf, verify: verify)) { XCTAssertEqual($0 as? MasterLock.Failure, .wrongState) }

        XCTAssertFalse(lock.tick(idleSeconds: 599))
        XCTAssertEqual(lock.state, .unlocked)
        XCTAssertTrue(lock.tick(idleSeconds: 600))
        XCTAssertEqual(lock.state, .locked)
        XCTAssertFalse(lock.tick(idleSeconds: 9999))   // 이미 잠김

        var never = MasterLock(hasMaster: false, autoLockMinutes: 0)
        try never.setMaster(master, kdf: kdf)
        XCTAssertFalse(never.tick(idleSeconds: 1e9))   // 0 = 자동 잠금 안 함
        XCTAssertEqual(never.state, .unlocked)
    }

    func testUnsupportedArgonProfileRefusedAtUnlock() {
        var lock = MasterLock(hasMaster: true)
        let big = Kdf(argon2id: true, iterations: 0, memoryKiB: 1 << 20, passes: 3, lanes: 1)
        XCTAssertThrowsError(try lock.unlock([1], salt: Crypto.newSalt(), kdf: big, verify: { _ in true })) {
            XCTAssertEqual($0 as? MasterLock.Failure, .kdf(.unsupportedProfile))
        }
        XCTAssertEqual(lock.state, .locked)
    }
}
