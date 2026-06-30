import XCTest
@testable import PokeTokenBar

// 임시 parity 검증 — 실제 ~/.claude, ~/.codex 로그로 집계해 콘솔 출력 → ccusage 와 수동 대조용.
// (CI 비결정적이므로 환경변수 PTB_PARITY=1 일 때만 실행)
final class LocalUsageParityTests: XCTestCase {
    func testPrintRealAggregates() throws {
        guard ProcessInfo.processInfo.environment["PTB_PARITY"] == "1" else {
            throw XCTSkip("PTB_PARITY != 1")
        }
        let now = Date()
        let fmt = LocalUsageReader.localDayFormatter()
        let monthStart = LocalUsageReader.startOfMonth(now)
        let today = CcusageProvider.todayKey()

        let claude = LocalUsageReader.claudeEntries(modifiedSince: monthStart)
        let codex = LocalUsageReader.codexEntries(modifiedSince: monthStart)

        print("PARITY ===== today=\(today)")
        // 최근 5일 일자별
        for offset in (0..<5).reversed() {
            let d = Calendar.current.date(byAdding: .day, value: -offset, to: now)!
            let day = fmt.string(from: d)
            let c = LocalUsageReader.daily(entries: claude, localDay: day)
            let x = LocalUsageReader.daily(entries: codex, localDay: day)
            print("PARITY claude \(day): tokens=\(c?.totalTokens ?? 0) cost=\(String(format: "%.2f", c?.totalCost ?? 0))")
            print("PARITY codex  \(day): tokens=\(x?.totalTokens ?? 0) cost=\(String(format: "%.2f", x?.totalCost ?? 0))")
        }
        let block = LocalUsageReader.activeBlock(entries: claude, now: now)
        print("PARITY active block: tokens=\(block?.totalTokens ?? 0) tpm=\(String(format: "%.0f", block?.tokensPerMinute ?? 0))")
        let ws = LocalUsageReader.startOfWeek(now)
        let week = LocalUsageReader.period(entries: claude, periodKey: "w", fromDay: fmt.string(from: ws), toDay: fmt.string(from: now))
        let month = LocalUsageReader.period(entries: claude, periodKey: "m", fromDay: fmt.string(from: monthStart), toDay: fmt.string(from: now))
        print("PARITY claude week tokens=\(week.totalTokens) cost=\(String(format: "%.2f", week.totalCost))")
        print("PARITY claude month tokens=\(month.totalTokens) cost=\(String(format: "%.2f", month.totalCost))")
    }

    func testCachePerformance() async throws {
        guard ProcessInfo.processInfo.environment["PTB_PARITY"] == "1" else { throw XCTSkip("PTB_PARITY != 1") }
        let monthStart = LocalUsageReader.startOfMonth(Date())
        var t = Date()
        _ = await LocalUsageCache.shared.claudeEntries(modifiedSince: monthStart)
        let cold = Date().timeIntervalSince(t)
        t = Date()
        _ = await LocalUsageCache.shared.claudeEntries(modifiedSince: monthStart)
        let warm = Date().timeIntervalSince(t)
        print(String(format: "PARITY-PERF claude month scan: cold %.0fms  warm %.0fms", cold * 1000, warm * 1000))
    }
}
