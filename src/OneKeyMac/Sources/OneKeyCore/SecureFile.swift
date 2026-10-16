import Darwin

/// 설정 파일 쓰기·읽기: 본인만(폴더 0700, 파일 0600). 임시 파일에 다 쓰고 디스크로 밀어 넣은 뒤 바꿔치기 —
/// 쓰는 도중 꺼져도 반쪽짜리 파일이 남지 않는다(Windows Config.Save 의 .tmp → Move 와 같은 순서).
public enum SecureFile {
    public enum Failure: Error, Equatable { case io(Int32), tooLarge }

    public static func write(_ bytes: [UInt8], to path: String) throws(Failure) {
        let dir = path.lastIndex(of: "/").map { String(path[..<$0]) } ?? "."
        if mkdir(dir, 0o700) != 0 && errno != EEXIST { throw .io(errno) }
        let tmp = path + ".tmp"
        unlink(tmp)   // 남은 임시 이름(링크일 수도)을 따라 쓰지 않는다: 지우고 O_EXCL 로 새로 만든다
        let fd = open(tmp, O_WRONLY | O_CREAT | O_EXCL | O_NOFOLLOW, 0o600)
        guard fd >= 0 else { throw .io(errno) }
        var ok = bytes.withUnsafeBytes { p -> Bool in
            var off = 0
            while off < p.count {
                let n = Darwin.write(fd, p.baseAddress.map { $0 + off }, p.count - off)
                if n <= 0 { return false }
                off += n
            }
            return true
        }
        ok = ok && fsync(fd) == 0
        let err = errno
        close(fd)
        guard ok, rename(tmp, path) == 0 else { let e = ok ? errno : err; unlink(tmp); throw .io(e) }
    }

    /// 상한까지만 읽는다. 없으면 nil.
    public static func read(_ path: String, max: Int) throws(Failure) -> [UInt8]? {
        let fd = open(path, O_RDONLY | O_NOFOLLOW)
        if fd < 0 { if errno == ENOENT { return nil }; throw .io(errno) }
        defer { close(fd) }
        var out = [UInt8](), buf = [UInt8](repeating: 0, count: 65536)
        while true {
            let n = buf.withUnsafeMutableBytes { Darwin.read(fd, $0.baseAddress, $0.count) }
            if n < 0 { throw .io(errno) }
            if n == 0 { return out }
            out += buf[0..<n]
            if out.count > max { throw .tooLarge }
        }
    }
}
