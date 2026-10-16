/// 마스터 비밀번호 잠금 상태 기계. 잠금 해제 동안 마스터 대신 유도한 키·salt·방식만 들고 있다(Windows Config 와 같은 보관 방식).
/// 앱은 이 값을 메인 액터에서만 가진다. 전이: 마스터 없음 →(설정) 해제 ⇄ 잠김. 자동 잠금 = 마지막 입력 뒤 autoLockMinutes(0 = 안 함).
public struct MasterLock {
    public enum State: Equatable, Sendable { case noMaster, locked, unlocked }
    public enum Failure: Error, Equatable { case wrongState, wrongPassword, kdf(Crypto.Failure) }

    public private(set) var state: State
    public var autoLockMinutes: Int
    private var key: [UInt8] = [], salt: [UInt8] = [], kdf: Kdf = .argon2Default

    public init(hasMaster: Bool, autoLockMinutes: Int = 10) {
        state = hasMaster ? .locked : .noMaster
        self.autoLockMinutes = autoLockMinutes
    }

    /// 새 마스터(처음 만들기·바꾸기): 새 salt 로 키를 만들고 해제 상태가 된다. 잠긴 상태에서는 바꿀 수 없다.
    public mutating func setMaster(_ master: [UInt8], kdf newKdf: Kdf = .argon2Default) throws(Failure) {
        guard state != .locked else { throw .wrongState }
        let newSalt = Crypto.newSalt()
        let newKey: [UInt8]
        do { newKey = try Crypto.deriveKey(master: master, salt: newSalt, kdf: newKdf) } catch { throw .kdf(error) }
        wipeKey()
        (key, salt, kdf, state) = (newKey, newSalt, newKdf, .unlocked)
    }

    /// 잠금 해제: 저장된 블록의 salt·방식으로 키를 만들고, verify(키)가 참일 때만(= 블록이 그 키로 풀릴 때만) 해제한다.
    public mutating func unlock(_ master: [UInt8], salt blobSalt: [UInt8], kdf blobKdf: Kdf, verify: ([UInt8]) -> Bool) throws(Failure) {
        guard state == .locked else { throw .wrongState }
        var k: [UInt8]
        do { k = try Crypto.deriveKey(master: master, salt: blobSalt, kdf: blobKdf) } catch { throw .kdf(error) }
        guard verify(k) else { wipe(&k); throw .wrongPassword }
        (key, salt, kdf, state) = (k, blobSalt, blobKdf, .unlocked)
    }

    /// 잠근다: 키를 0 으로 덮고 버린다. 이미 잠겼거나 마스터가 없으면 그대로.
    public mutating func lock() {
        guard state == .unlocked else { return }
        wipeKey()
        state = .locked
    }

    /// 주기적으로 부른다(idleSeconds = 마지막 키보드·마우스 입력 뒤 지난 초). 자동 잠금이 일어났으면 true.
    public mutating func tick(idleSeconds: Double) -> Bool {
        guard state == .unlocked, autoLockMinutes > 0, idleSeconds >= Double(autoLockMinutes) * 60 else { return false }
        lock()
        return true
    }

    /// 해제 중에만 키·salt·방식을 빌려준다(저장 = 같은 키·같은 salt 에 새 nonce). 잠겼으면 nil.
    public func withKey<R>(_ body: (_ key: [UInt8], _ salt: [UInt8], _ kdf: Kdf) throws -> R) rethrows -> R? {
        guard state == .unlocked else { return nil }
        return try body(key, salt, kdf)
    }

    private mutating func wipeKey() { wipe(&key); key = []; salt = [] }
}
