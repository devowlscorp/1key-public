// swift-tools-version:6.0
import PackageDescription

let package = Package(
    name: "OneKeyMac",
    platforms: [.macOS(.v13)],
    targets: [
        // 앱과 무관한 핵심(암호·저장 형식). Windows src/OneKey 와 같은 출력이 시험으로 묶인다
        .target(name: "OneKeyCore"),
        .testTarget(name: "OneKeyCoreTests", dependencies: ["OneKeyCore"]),
    ]
)
