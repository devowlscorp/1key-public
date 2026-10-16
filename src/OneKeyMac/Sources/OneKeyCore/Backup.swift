/// 백업 파일(.1keybak) — Windows src/OneKey/Backup.cs 와 같은 바이트 형식. 백업 전용 비밀번호로만 잠근다.
///
/// 형식(정확한 바이트, 모두 AAD 로 인증):
///   머리 55 바이트 = "1KEYBAK1"(8) | 형식 번호 u16=1 | 키 유도 u8=1(Argon2id) | m u32 | t u32 | p u32 | salt(16) | nonce(12) | 평문 길이 u32
///   그 뒤 = 태그(16) | 암호문(평문 길이). 뒤에 남는 바이트 없음.
/// 평문 = 섹션 [종류 u8 | 길이 u32 | 내용] 을 종류 오름차순으로, 중복 없이: 1 = 설정 머리, 2 = 암호 블록 평문, 3 = 실행 목록(없을 수 있음).
/// 1·2 는 반드시, 모든 섹션은 엄격한 UTF-8. 상한: 파일 8 MiB, 섹션 4 MiB. 키 유도 전에 머리 전체를 확인한다.
public enum Backup {
    static let magic = Array("1KEYBAK1".utf8)
    public static let formatVersion: UInt16 = 1
    public static let headerLen = 55, tagLen = 16, nonceLen = 12, saltLen = 16
    public static let maxFile = 8 << 20, maxSection = 4 << 20
    public static let secSettings: UInt8 = 1, secSecrets: UInt8 = 2, secLaunch: UInt8 = 3
    public static let fileExtension = ".1keybak"

    /// 열기 결과의 종류(안내 문구를 고른다). Windows Backup.Error 와 같은 구분.
    public enum Failure: Error, Equatable { case tooLarge, notBackup, newerFormat, badProfile, damaged, wrongPasswordOrDamaged, kdfFailed }

    /// 열어 낸 백업 내용. 다 쓰면 wipe() 로 지운다.
    public struct Contents {
        public var settings: [UInt8] = []
        public var secrets: [UInt8]? = nil
        public var launch: [UInt8]? = nil
        public mutating func wipe() {
            OneKeyCore.wipe(&settings)
            if var s = secrets { OneKeyCore.wipe(&s); secrets = nil }
            if var l = launch { OneKeyCore.wipe(&l); launch = nil }
        }
    }

    /// 백업 파일 바이트를 만든다. 입력(password·secrets)은 부르는 쪽 소유 — 여기서 지우지 않는다. 실패하면 nil.
    public static func build(password: [UInt8], settings: [UInt8], secrets: [UInt8], launch: [UInt8] = []) -> [UInt8]? {
        var sections: [(UInt8, [UInt8])] = [(secSettings, settings), (secSecrets, secrets)]
        if !launch.isEmpty { sections.append((secLaunch, launch)) }
        return buildRaw(password: password, plain: sections.flatMap { [$0.0] + le32($0.1.count) + $0.1 },
                        sectionsOK: sections.allSatisfy { $0.1.count <= maxSection })
    }

    /// 평문(섹션 바이트)을 그대로 넣는다 — 시험은 잘못된 섹션 배열의 거절을 이것으로 본다.
    static func buildRaw(password: [UInt8], plain plainIn: [UInt8], sectionsOK: Bool = true) -> [UInt8]? {
        var plain = plainIn
        defer { wipe(&plain) }
        guard sectionsOK, headerLen + tagLen + plain.count <= maxFile else { return nil }
        let salt = Crypto.newSalt(), nonce = Crypto.randomBytes(nonceLen)
        guard var key = try? Crypto.deriveKey(master: password, salt: salt, kdf: .argon2Default) else { return nil }
        defer { wipe(&key) }
        let k = Kdf.argon2Default
        let header = magic + le16(formatVersion) + [1] + le32(k.memoryKiB) + le32(k.passes) + le32(k.lanes) + salt + nonce + le32(plain.count)
        guard let (ct, tag) = try? Crypto.seal(plain, key: key, nonce: nonce, aad: header) else { return nil }
        return header + tag + ct
    }

    /// 머리만 확인한다(키 유도 전): 크기·표식·형식 번호·키 유도 방식·정확한 프로파일·길이.
    public static func checkHeader(_ file: [UInt8]) -> Failure? {
        if file.count > maxFile { return .tooLarge }
        if file.count < headerLen + tagLen || Array(file[0..<8]) != magic { return .notBackup }
        let ver = UInt16(file[8]) | UInt16(file[9]) << 8
        if ver != formatVersion { return ver > formatVersion ? .newerFormat : .damaged }
        if file[10] != 1 { return .badProfile }
        let k = Kdf(argon2id: true, iterations: 0, memoryKiB: readLE32(file, 11), passes: readLE32(file, 15), lanes: readLE32(file, 19))
        if k != .argon2Default { return .badProfile }   // 다른 프로파일은 계산·할당 전에 거절
        if headerLen + tagLen + readLE32(file, 51) != file.count { return .damaged }   // 잘림·뒤에 남는 바이트
        return nil
    }

    /// 백업 파일을 연다: 머리 → 키 유도 → 복호화(머리 전체 AAD) → 섹션 해석. 하나라도 어긋나면 내용 없이 오류.
    public static func open(_ file: [UInt8], password: [UInt8]) -> Result<Contents, Failure> {
        if let e = checkHeader(file) { return .failure(e) }
        guard var key = try? Crypto.deriveKey(master: password, salt: Array(file[23..<(23 + saltLen)]), kdf: .argon2Default) else {
            return .failure(.kdfFailed)
        }
        defer { wipe(&key) }
        guard var plain = Crypto.open(ciphertext: file[(headerLen + tagLen)...], key: key, nonce: file[39..<(39 + nonceLen)],
                                      tag: file[headerLen..<(headerLen + tagLen)], aad: Array(file[0..<headerLen])) else {
            return .failure(.wrongPasswordOrDamaged)
        }
        defer { wipe(&plain) }
        var c = Contents()
        var o = 0, last: UInt8 = 0
        while o < plain.count {
            guard plain.count - o >= 5 else { c.wipe(); return .failure(.damaged) }
            let id = plain[o], len = readLE32(plain, o + 1)
            // 순서·중복·종류·길이, 그리고 엄격한 UTF-8(잘못된 바이트를 대체 문자로 바꾸지 않고 거절)
            guard id > last, id <= secLaunch, len <= maxSection, len <= plain.count - o - 5,
                  isStrictUTF8(plain[(o + 5)..<(o + 5 + len)]) else { c.wipe(); return .failure(.damaged) }
            let data = Array(plain[(o + 5)..<(o + 5 + len)])
            switch id {
            case secSettings: c.settings = data
            case secSecrets: c.secrets = data
            default: c.launch = data
            }
            last = id
            o += 5 + len
        }
        guard !c.settings.isEmpty, c.secrets != nil else { c.wipe(); return .failure(.damaged) }
        return .success(c)
    }

    static func isStrictUTF8(_ b: ArraySlice<UInt8>) -> Bool {
        var it = b.makeIterator(), dec = UTF8()
        while true {
            switch dec.decode(&it) {
            case .scalarValue: continue
            case .emptyInput: return true
            case .error: return false
            }
        }
    }
}

func le16(_ v: UInt16) -> [UInt8] { [UInt8(truncatingIfNeeded: v), UInt8(truncatingIfNeeded: v >> 8)] }

func le32(_ v: Int) -> [UInt8] {
    let u = UInt32(truncatingIfNeeded: v)
    return [UInt8(truncatingIfNeeded: u), UInt8(truncatingIfNeeded: u >> 8), UInt8(truncatingIfNeeded: u >> 16), UInt8(truncatingIfNeeded: u >> 24)]
}

func readLE32(_ b: [UInt8], _ at: Int) -> Int {
    Int(UInt32(b[at]) | UInt32(b[at + 1]) << 8 | UInt32(b[at + 2]) << 16 | UInt32(b[at + 3]) << 24)
}
