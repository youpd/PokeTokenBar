import Foundation

/// ~/Library/Logs/PokeTokenBar.log 단순 append 로거 (디버깅/장애 추적용)
enum AppLog {
    /// 로그 파일 경로 — 설정의 "로그 파일 보기"(Finder 표시)에서 사용.
    static var logFileURL: URL { url }

    private static let url: URL = {
        let dir = FileManager.default.urls(for: .libraryDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Logs")
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir.appendingPathComponent("PokeTokenBar.log")
    }()

    private static let queue = DispatchQueue(label: "poketokenbar.log")

    /// 로그 상한 — 초과 시 .old 로 1세대 회전(무한 증가 방지, 디스크 상한 ≈ 2×maxBytes = 4MB).
    /// 24/7 메뉴바 앱이라 회전이 잦아 진단 이력이 금방 사라지던 것 완화(장애 직전 컨텍스트 보존).
    /// 레퍼런스(size-capped 회전, 수 MB)에 맞춤 — 일자별 폴더는 무한 성장/정리 필요라 채택 안 함.
    private static let maxBytes = 2 * 1024 * 1024

    static func write(_ message: String) {
        let line = "[\(ISO8601DateFormatter().string(from: Date()))] \(message)\n"
        queue.async {
            if let size = try? FileManager.default.attributesOfItem(atPath: url.path)[.size] as? Int,
               size > maxBytes {
                let old = url.deletingPathExtension().appendingPathExtension("old.log")
                try? FileManager.default.removeItem(at: old)
                try? FileManager.default.moveItem(at: url, to: old)
            }
            if let data = line.data(using: .utf8) {
                if let handle = try? FileHandle(forWritingTo: url) {
                    defer { try? handle.close() }
                    _ = try? handle.seekToEnd()
                    try? handle.write(contentsOf: data)
                } else {
                    try? data.write(to: url)
                }
            }
        }
    }
}
