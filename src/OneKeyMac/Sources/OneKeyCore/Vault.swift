import Foundation
import Security

/// 설정 파일의 바깥 층 — Windows 의 DPAPI 자리(PO 결정 2026-10-16: 키체인에 둔 감싸기 키로 감싼다).
/// 감싸기 키(32 바이트 무작위)는 로그인 키체인에 이 Mac 전용·잠금 해제 중에만 읽히게 둔다 — 다른 macOS 사용자로부터의 보호.
/// DPAPI 와 같이 같은 계정의 프로그램이 키체인 접근을 허락받으면 풀 수 있다: 안쪽의 마스터 암호화가 본 방어선이다.
/// 형식: "1KEYMAC1"(8) | nonce(12) | tag(16) | 암호문. AAD = 표식 + 용도 구분값(설정·실행 목록이 서로 바꿔 끼워지지 않게).
public enum Vault {
    static let magic = Array("1KEYMAC1".utf8)
    public static let configPurpose = Array("1Key/approval-password/v1".utf8)

    public static func protect(_ plain: [UInt8], wrapKey: [UInt8], purpose: [UInt8]) throws -> [UInt8] {
        let nonce = Crypto.randomBytes(Crypto.nonceLen)
        let (ct, tag) = try Crypto.seal(plain, key: wrapKey, nonce: nonce, aad: magic + purpose)
        return magic + nonce + tag + ct
    }

    /// 표식이 다르거나 키·용도가 틀리거나 변조되면 nil.
    public static func unprotect(_ blob: [UInt8], wrapKey: [UInt8], purpose: [UInt8]) -> [UInt8]? {
        let h = magic.count, n = Crypto.nonceLen, t = Crypto.tagLen
        guard blob.count >= h + n + t, Array(blob[0..<h]) == magic else { return nil }
        return Crypto.open(ciphertext: blob[(h + n + t)...], key: wrapKey, nonce: blob[h..<(h + n)], tag: blob[(h + n)..<(h + n + t)], aad: magic + purpose)
    }
}

/// 감싸기 키를 어디에 두는가. 시험은 메모리 판을 쓴다(키체인 대화상자를 띄우지 않게).
public protocol WrapKeyStore: Sendable {
    /// 있으면 읽고, 없으면(그때만) 새로 만들어 둔다.
    func loadOrCreate() throws(WrapKeyError) -> [UInt8]
}

public enum WrapKeyError: Error, Equatable {
    /// 키체인이 거절(잠김·접근 거부 등). 새 키를 만들지 않는다 — 만들면 기존 설정 파일을 영영 못 연다.
    case keychain(OSStatus)
}

public struct KeychainWrapKeyStore: WrapKeyStore {
    let service: String, account: String
    public init(service: String = "kr.devowls.1key", account: String = "config-wrap-v1") { self.service = service; self.account = account }

    public func loadOrCreate() throws(WrapKeyError) -> [UInt8] {
        if let k = try read() { return k }
        let key = Crypto.randomBytes(Crypto.keyLen)
        let add: [String: Any] = base.merging([
            kSecValueData as String: Data(key),
            kSecAttrAccessible as String: kSecAttrAccessibleWhenUnlockedThisDeviceOnly,
        ]) { a, _ in a }
        let st = SecItemAdd(add as CFDictionary, nil)
        // 두 인스턴스가 동시에 만들면 늦은 쪽은 중복 — 먼저 들어간 키를 쓴다(읽기와 쓰기 사이 경쟁을 고유성으로 푼다)
        if st == errSecDuplicateItem, let k = try read() { return k }
        guard st == errSecSuccess else { throw .keychain(st) }
        return key
    }

    private var base: [String: Any] {
        [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service, kSecAttrAccount as String: account]
    }

    /// 없으면 nil, 그 밖의 실패는 오류.
    private func read() throws(WrapKeyError) -> [UInt8]? {
        let q = base.merging([kSecReturnData as String: true, kSecMatchLimit as String: kSecMatchLimitOne]) { a, _ in a }
        var out: CFTypeRef?
        let st = SecItemCopyMatching(q as CFDictionary, &out)
        if st == errSecItemNotFound { return nil }
        guard st == errSecSuccess, let data = out as? Data, data.count == Crypto.keyLen else {
            throw .keychain(st == errSecSuccess ? errSecDecode : st)
        }
        return Array(data)
    }
}
