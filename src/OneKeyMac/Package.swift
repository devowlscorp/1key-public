// swift-tools-version:6.0
import PackageDescription

let package = Package(
    name: "OneKeyMac",
    platforms: [.macOS(.v13)],
    targets: [
        // 앱과 무관한 핵심(암호·저장 형식). Windows src/OneKey 와 같은 출력이 시험으로 묶인다
        // 디버그에서도 최적화: 최적화 없이는 Argon2id 64 MiB 한 번이 13초(잠금 해제·시험이 느려진다)
        .target(name: "OneKeyCore", swiftSettings: [.unsafeFlags(["-O"])]),
        .testTarget(name: "OneKeyCoreTests", dependencies: ["OneKeyCore"]),
    ]
)
