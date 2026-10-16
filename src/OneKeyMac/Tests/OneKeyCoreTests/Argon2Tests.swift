import XCTest
@testable import OneKeyCore

/// Windows src/OneKey/SiteSelfTest.cs AG01–AG06 과 같은 입력·기대값 — 두 구현이 같은 바이트를 내야 한다.
final class Argon2Tests: XCTestCase {
    private func hex(_ b: [UInt8]) -> String { b.map { String(format: "%02x", $0) }.joined() }
    private func b2(_ s: String) -> String { var h = Blake2b(outLen: 64); h.update(Array(s.utf8)); return hex(h.final()) }
    private func rep(_ v: UInt8, _ n: Int) -> [UInt8] { [UInt8](repeating: v, count: n) }
    private let pw = Array("password".utf8), salt = Array("somesalt".utf8)

    func testAG01_Blake2bAbc() {
        XCTAssertEqual(b2("abc"), "ba80a53f981c4d0d6a2797b69f12f6e94c212f14685ac4b74b12bb6fdbffa2d17d87c5392aab792dc252d5de4533cc9518d38aa8dbf1925ab92386edd4009923")
    }

    func testAG02_Blake2bEmpty() {
        XCTAssertEqual(b2(""), "786a02f742015903c6c6fd852552d272912f4740e15847618a86e217f71f5419d25e1031afee585313896444934eb04b903a685b1448b755d56f701afe9be2ce")
    }

    func testAG03_Rfc9106() throws {
        let tag = try Argon2.hash(password: rep(1, 32), salt: rep(2, 16), iterations: 3, memoryKiB: 32, lanes: 4, tagLength: 32,
                                  secret: rep(3, 8), ad: rep(4, 12))
        XCTAssertEqual(hex(tag), "0d640df58d78766c08c037a34a8b53c9d01ef0452d75b65eb52520e96b01e659")
    }

    func testAG04_Reference64MiB() throws {
        XCTAssertEqual(hex(try Argon2.hash(password: pw, salt: salt, iterations: 2, memoryKiB: 1 << 16, lanes: 1, tagLength: 32)),
                       "09316115d5cf24ed5a15a31a3ba326e5cf32edc24702987c02b6566f61913cf7")
    }

    func testAG05_Reference256KiB() throws {
        XCTAssertEqual(hex(try Argon2.hash(password: pw, salt: salt, iterations: 2, memoryKiB: 1 << 8, lanes: 1, tagLength: 32)),
                       "9dfeb910e80bad0311fee20f9c0e2b12c17987b4cac90c2ef54d5b3021c68bfe")
    }

    func testAG06_TwoLanes() throws {
        XCTAssertEqual(hex(try Argon2.hash(password: pw, salt: salt, iterations: 2, memoryKiB: 1 << 8, lanes: 2, tagLength: 32)),
                       "6d093c501fd5999645e0ea3bf620d7b8be7fd2db59c20d9fff9539da2bf57037")
    }

    // 범위 밖 인수는 계산 전에 거절(Windows 의 ArgumentOutOfRange 와 같은 경계)
    func testInvalidParametersRejected() {
        for (m, t, p, len, s) in [(256, 2, 0, 32, 8), (256, 0, 1, 32, 8), (256, 2, 1, 3, 8), (15, 2, 2, 32, 8), (256, 2, 1, 32, 7), ((1 << 20) + 1, 1, 1, 32, 8)] {
            XCTAssertThrowsError(try Argon2.hash(password: pw, salt: rep(0, s), iterations: t, memoryKiB: m, lanes: p, tagLength: len)) {
                XCTAssertEqual($0 as? Argon2.Failure, .invalidParameters)
            }
        }
    }
}
