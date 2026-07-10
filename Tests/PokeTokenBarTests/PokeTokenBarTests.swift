import XCTest
@testable import PokeTokenBar

#if os(macOS)
import Security
#endif

final class TokenFormatterTests: XCTestCase {
    func testCompact() {
        XCTAssertEqual(TokenFormatter.compact(0), "0")
        XCTAssertEqual(TokenFormatter.compact(987), "987")
        XCTAssertEqual(TokenFormatter.compact(12_345), "12.3K")
        XCTAssertEqual(TokenFormatter.compact(190_612_940), "190.6M")
        XCTAssertEqual(TokenFormatter.compact(1_240_000_000), "1.24B")
        XCTAssertEqual(TokenFormatter.compact(1_000_000), "1M")
    }
}

final class ModelDecodingTests: XCTestCase {
    // 실제 번들 ccusage 출력에서 채취한 fixture (2026-06-10)
    func testDailyReport() throws {
        let json = """
        {"daily":[{"date":"2026-06-10","inputTokens":907436,"outputTokens":1334526,
        "cacheCreationTokens":18905002,"cacheReadTokens":169465976,
        "totalTokens":190612940,"totalCost":311.30462895,
        "modelsUsed":["claude-fable-5"],"modelBreakdowns":[]}],"totals":null}
        """.data(using: .utf8)!
        let report = try JSONDecoder().decode(DailyReport.self, from: json)
        XCTAssertEqual(report.daily.count, 1)
        XCTAssertEqual(report.daily[0].totalTokens, 190_612_940)
        XCTAssertEqual(report.daily[0].date, "2026-06-10")
    }

    func testDailyReportV20Period() throws {
        // ccusage ≥20 은 "date" 대신 "period" 로 일자를 준다 (metadata/agent 필드 추가)
        let json = """
        {"daily":[{"agent":"all","period":"2026-06-15","metadata":{"agents":["claude"]},
        "inputTokens":372486,"outputTokens":167660,"cacheCreationTokens":2050363,
        "cacheReadTokens":18225182,"totalTokens":20815691,"totalCost":27.98}],"totals":null}
        """.data(using: .utf8)!
        let report = try JSONDecoder().decode(DailyReport.self, from: json)
        XCTAssertEqual(report.daily[0].date, "2026-06-15")
        XCTAssertEqual(report.daily[0].totalTokens, 20_815_691)
    }

    func testCodexDailyReportMapsCachedInputTokens() throws {
        let json = """
        {"daily":[{"date":"2026-06-17","inputTokens":10,"outputTokens":20,
        "cachedInputTokens":30,"totalTokens":60,"costUSD":0.25}]}
        """.data(using: .utf8)!

        let report = try JSONDecoder().decode(DailyReport.self, from: json)

        XCTAssertEqual(report.daily[0].cacheReadTokens, 30)
        XCTAssertEqual(report.daily[0].totalCost, 0.25)
    }

    func testDailyReportEmpty() throws {
        // ccusage-codex 데이터 없음 케이스
        let json = #"{"daily":[],"totals":null}"#.data(using: .utf8)!
        let report = try JSONDecoder().decode(DailyReport.self, from: json)
        XCTAssertTrue(report.daily.isEmpty)
    }

    func testTotalTokensFallback() throws {
        // totalTokens 누락 시 4종 토큰(input/output/cacheCreation/cacheRead) 합으로 폴백
        let json = """
        {"daily":[{"date":"2026-06-10","inputTokens":10,"outputTokens":20,
        "cacheCreationTokens":30,"cacheReadTokens":40,"costUSD":1.5}]}
        """.data(using: .utf8)!
        let report = try JSONDecoder().decode(DailyReport.self, from: json)
        XCTAssertEqual(report.daily[0].totalTokens, 100)
        XCTAssertEqual(report.daily[0].totalCost, 1.5)
    }

    func testBlocksReport() throws {
        let json = """
        {"blocks":[{"id":"2026-06-10T06:00:00.000Z","startTime":"2026-06-10T06:00:00.000Z",
        "endTime":"2026-06-10T11:00:00.000Z","isActive":true,"isGap":false,"entries":399,
        "totalTokens":26910731,"costUSD":51.8358331}]}
        """.data(using: .utf8)!
        let report = try JSONDecoder().decode(BlocksReport.self, from: json)
        XCTAssertEqual(report.blocks.count, 1)
        XCTAssertTrue(report.blocks[0].isActive)
        XCTAssertNotNil(report.blocks[0].endDate)
    }

    func testWeeklyMonthlyReport() throws {
        // 실제 ccusage weekly/monthly 출력 fixture (2026-06-11)
        let weekly = """
        {"weekly":[{"week":"2026-05-31","inputTokens":79280,"outputTokens":634270,
        "cacheCreationTokens":4355252,"cacheReadTokens":141260644,
        "totalTokens":146329446,"totalCost":107.0425086}]}
        """.data(using: .utf8)!
        let w = try JSONDecoder().decode(WeeklyReport.self, from: weekly)
        XCTAssertEqual(w.weekly.last?.period, "2026-05-31")
        XCTAssertEqual(w.weekly.last?.totalTokens, 146_329_446)

        let monthly = """
        {"monthly":[{"month":"2026-06","totalTokens":671185849,"totalCost":589.255}]}
        """.data(using: .utf8)!
        let m = try JSONDecoder().decode(MonthlyReport.self, from: monthly)
        XCTAssertEqual(m.monthly.last?.period, "2026-06")
        XCTAssertEqual(m.monthly.last?.totalTokens, 671_185_849)
    }

    func testCodexMonthlyReportCostUSD() throws {
        let json = """
        {"monthly":[{"month":"2026-06","inputTokens":10,"outputTokens":20,
        "cachedInputTokens":30,"totalTokens":60,"costUSD":0.25}]}
        """.data(using: .utf8)!

        let report = try JSONDecoder().decode(MonthlyReport.self, from: json)

        XCTAssertEqual(report.monthly[0].totalTokens, 60)
        XCTAssertEqual(report.monthly[0].totalCost, 0.25)
    }

    func testPeriodUsageSumsDailyRows() {
        let daily = [
            DailyUsage(date: "2026-06-16", inputTokens: 1, outputTokens: 2,
                       cacheCreationTokens: 3, cacheReadTokens: 4, totalTokens: 10, totalCost: 0.1),
            DailyUsage(date: "2026-06-17", inputTokens: 5, outputTokens: 6,
                       cacheCreationTokens: 7, cacheReadTokens: 8, totalTokens: 26, totalCost: 0.2),
        ]

        let period = PeriodUsage(period: "2026-06-14", daily: daily)

        XCTAssertEqual(period.period, "2026-06-14")
        XCTAssertEqual(period.totalTokens, 36)
        XCTAssertEqual(period.totalCost, 0.3, accuracy: 0.001)
    }

    func testBlockBurnRateDecoding() throws {
        let json = """
        {"blocks":[{"id":"b","startTime":"2026-06-11T01:00:00.000Z","endTime":"2026-06-11T06:00:00.000Z",
        "isActive":true,"totalTokens":26910731,"costUSD":51.8,
        "burnRate":{"tokensPerMinute":457194.05,"costPerHour":1.18}}]}
        """.data(using: .utf8)!
        let report = try JSONDecoder().decode(BlocksReport.self, from: json)
        XCTAssertEqual(report.blocks[0].tokensPerMinute ?? 0, 457_194.05, accuracy: 0.01)
    }

    func testForecastDepletion() {
        let now = Date(timeIntervalSinceReferenceDate: 0)
        // 블록 23M 토큰 = 23% → 1% ≈ 1M 토큰. 잔여 77% ≈ 77M 토큰. 분당 1M → 77분 후 도달
        let d = UsageStore.forecastDepletion(
            blockTokens: 23_000_000, tokensPerMinute: 1_000_000, utilization: 23, now: now)
        XCTAssertNotNil(d)
        XCTAssertEqual(d!.timeIntervalSince(now) / 60, 77, accuracy: 0.5)

        // 추정 불안정 구간은 nil: 낮은 utilization / 낮은 burn / 24시간 초과
        XCTAssertNil(UsageStore.forecastDepletion(
            blockTokens: 1_000_000, tokensPerMinute: 1_000_000, utilization: 3, now: now))
        XCTAssertNil(UsageStore.forecastDepletion(
            blockTokens: 23_000_000, tokensPerMinute: 5_000, utilization: 23, now: now))
        XCTAssertNil(UsageStore.forecastDepletion(
            blockTokens: 100_000_000, tokensPerMinute: 10_000, utilization: 10, now: now))
    }

    func testLimitStatus() throws {
        // 실제 /api/oauth/usage 응답 fixture (2026-06-10, 마이크로초 정밀도 resets_at)
        let json = """
        {"five_hour":{"utilization":23.0,"resets_at":"2026-06-10T11:10:00.034464+00:00"},
        "seven_day":{"utilization":16.0,"resets_at":"2026-06-14T03:00:01.034496+00:00"},
        "seven_day_opus":null,
        "seven_day_sonnet":{"utilization":0.0,"resets_at":"2026-06-14T03:00:01.034508+00:00"},
        "seven_day_omelette":{"utilization":0.0,"resets_at":null},
        "extra_usage":{"is_enabled":false}}
        """.data(using: .utf8)!
        let status = try JSONDecoder().decode(LimitStatus.self, from: json)
        XCTAssertEqual(status.fiveHour?.utilization, 23.0)
        XCTAssertNotNil(status.fiveHour?.resetDate, "마이크로초 정밀도 ISO8601 파싱 실패")
        XCTAssertNil(status.sevenDayOpus)
        XCTAssertEqual(status.sevenDay?.utilization, 16.0)
    }

    /// oauth/usage 신형 limits[] — 레거시 필드가 커버하는 session/weekly_all 은 제외,
    /// weekly_scoped(모델별 주간)만 추가 노출 대상. (2026-07 실측 스키마 기반)
    func testLimitStatusScopedEntries() throws {
        let json = """
        {"five_hour":{"utilization":32.0,"resets_at":"2026-07-10T04:10:00.497904+00:00"},
        "seven_day":{"utilization":7.0,"resets_at":"2026-07-12T03:00:00.497928+00:00"},
        "seven_day_opus":null,"seven_day_sonnet":null,
        "limits":[
        {"kind":"session","group":"session","percent":32,"severity":"normal","resets_at":"2026-07-10T04:10:00.497904+00:00","scope":null,"is_active":true},
        {"kind":"weekly_all","group":"weekly","percent":7,"severity":"normal","resets_at":"2026-07-12T03:00:00.497928+00:00","scope":null,"is_active":false},
        {"kind":"weekly_scoped","group":"weekly","percent":41,"severity":"normal","resets_at":"2026-07-12T03:00:00.498239+00:00","scope":{"model":{"id":null,"display_name":"Fable"},"surface":null},"is_active":false}
        ]}
        """.data(using: .utf8)!

        let status = try JSONDecoder().decode(LimitStatus.self, from: json)

        XCTAssertEqual(status.limits?.count, 3)
        XCTAssertEqual(status.scopedLimitEntries.count, 1)
        let scoped = try XCTUnwrap(status.scopedLimitEntries.first)
        XCTAssertEqual(scoped.kind, "weekly_scoped")
        XCTAssertEqual(scoped.percent, 41)
        XCTAssertEqual(scoped.scope?.model?.displayName, "Fable")
        XCTAssertNotNil(scoped.resetDate)
    }

    /// 레거시 필드가 전부 비면 limits[] 전체가 표시 대상 (신형 응답 전환 대비 폴백).
    func testLimitStatusLegacyEmptyFallsBackToAllEntries() throws {
        let json = """
        {"five_hour":null,"seven_day":null,
        "limits":[
        {"kind":"session","group":"session","percent":10,"severity":"normal","resets_at":"2026-07-10T04:10:00+00:00","scope":null,"is_active":true},
        {"kind":"weekly_all","group":"weekly","percent":5,"severity":"normal","resets_at":"2026-07-12T03:00:00+00:00","scope":null,"is_active":false}
        ]}
        """.data(using: .utf8)!

        let status = try JSONDecoder().decode(LimitStatus.self, from: json)
        XCTAssertEqual(status.scopedLimitEntries.count, 2)
    }

    func testCodexRateLimitStatus() throws {
        let json = """
        {"rateLimits":{"limitId":"codex","limitName":null,
        "primary":{"usedPercent":86,"windowDurationMins":300,"resetsAt":1781694161},
        "secondary":{"usedPercent":58,"windowDurationMins":10080,"resetsAt":1781855658},
        "credits":{"hasCredits":false,"unlimited":false,"balance":null},
        "individualLimit":null,"planType":"team","rateLimitReachedType":null},
        "rateLimitsByLimitId":{"codex":{"limitId":"codex","limitName":null,
        "primary":{"usedPercent":86,"windowDurationMins":300,"resetsAt":1781694161},
        "secondary":{"usedPercent":58,"windowDurationMins":10080,"resetsAt":1781855658},
        "credits":{"hasCredits":false,"unlimited":false,"balance":null},
        "individualLimit":null,"planType":"team","rateLimitReachedType":null}}}
        """.data(using: .utf8)!

        let status = try JSONDecoder().decode(CodexRateLimitStatus.self, from: json)

        // 단일 bucket — top-level 과 byLimitId["codex"] 가 같은 스냅샷이므로 1개로 dedup
        XCTAssertEqual(status.snapshots.count, 1)
        let codex = try XCTUnwrap(status.visibleSnapshots.first)
        XCTAssertEqual(codex.primary?.usedPercent, 86)
        XCTAssertEqual(codex.primary?.displayName, "5시간 세션")
        XCTAssertEqual(codex.secondary?.displayName, "주간")
        XCTAssertEqual(codex.planType, "team")
        XCTAssertTrue(status.hasVisibleLimit)
        XCTAssertNotNil(codex.primary?.resetDate)
        XCTAssertEqual(status.maxPrimaryUsedPercent, 86)
    }

    /// 다중 bucket 계정 (codex + codex_other) — 리포트된 버그 시나리오:
    /// "codex" bucket 은 미사용(0%), 실사용은 codex_other(주간 93%). 두 bucket 모두 노출돼야 한다.
    func testCodexRateLimitStatusMultiBucket() throws {
        let json = """
        {"rateLimits":{"limitId":"codex","limitName":null,
        "primary":{"usedPercent":0,"windowDurationMins":300,"resetsAt":1781694161},
        "secondary":{"usedPercent":0,"windowDurationMins":10080,"resetsAt":1781855658},
        "credits":null,"individualLimit":null,"planType":"plus","rateLimitReachedType":null},
        "rateLimitsByLimitId":{
        "codex":{"limitId":"codex","limitName":null,
        "primary":{"usedPercent":0,"windowDurationMins":300,"resetsAt":1781694161},
        "secondary":{"usedPercent":0,"windowDurationMins":10080,"resetsAt":1781855658},
        "credits":null,"individualLimit":null,"planType":"plus","rateLimitReachedType":null},
        "codex_other":{"limitId":"codex_other","limitName":"codex_other",
        "primary":{"usedPercent":41,"windowDurationMins":300,"resetsAt":1781694161},
        "secondary":{"usedPercent":93,"windowDurationMins":10080,"resetsAt":1781855658},
        "credits":null,"individualLimit":null,"planType":"plus","rateLimitReachedType":null}}}
        """.data(using: .utf8)!

        let status = try JSONDecoder().decode(CodexRateLimitStatus.self, from: json)

        XCTAssertEqual(status.snapshots.count, 2)
        XCTAssertEqual(status.snapshots[0].limitId, "codex")          // top-level 우선
        XCTAssertEqual(status.snapshots[1].limitId, "codex_other")
        XCTAssertEqual(status.snapshots[1].secondary?.usedPercent, 93)
        XCTAssertEqual(status.maxPrimaryUsedPercent, 41)              // 메뉴바 = bucket 최대값
        XCTAssertEqual(status.snapshots[1].bucketDisplayName, "Codex other")
        XCTAssertEqual(status.snapshots[0].bucketDisplayName, "Codex")
    }

    /// limitId 없는 구형 응답 — byLimitId["codex"] 가 top-level 과 동일 내용이면 중복 노출 금지.
    func testCodexRateLimitStatusLegacyNilLimitIdDedup() throws {
        let json = """
        {"rateLimits":{"limitId":null,"limitName":null,
        "primary":{"usedPercent":30,"windowDurationMins":300,"resetsAt":1},
        "secondary":null,"credits":null,"individualLimit":null,"planType":null,"rateLimitReachedType":null},
        "rateLimitsByLimitId":{"codex":{"limitId":null,"limitName":null,
        "primary":{"usedPercent":30,"windowDurationMins":300,"resetsAt":1},
        "secondary":null,"credits":null,"individualLimit":null,"planType":null,"rateLimitReachedType":null}}}
        """.data(using: .utf8)!

        let status = try JSONDecoder().decode(CodexRateLimitStatus.self, from: json)
        XCTAssertEqual(status.snapshots.count, 1)
        XCTAssertEqual(status.maxPrimaryUsedPercent, 30)
    }
}

#if os(macOS)
final class KeychainNoUIQueryTests: XCTestCase {
    func testNoUIQueryAddsAuthenticationContextAndFailPolicy() {
        var query: [String: Any] = [:]

        KeychainNoUIQuery.apply(to: &query)

        XCTAssertNotNil(query[kSecUseAuthenticationContext as String])
        XCTAssertEqual(
            query[kSecUseAuthenticationUI as String] as? String,
            KeychainNoUIQuery.uiFailPolicyForTesting())
    }
}
#endif

final class OAuthCredentialDataTests: XCTestCase {
    func testCredentialParsesMillisecondExpiration() {
        let futureMillis = Int(Date().addingTimeInterval(3600).timeIntervalSince1970 * 1000)
        let data = """
        {"claudeAiOauth":{"accessToken":"token-1","expiresAt":\(futureMillis)}}
        """.data(using: .utf8)!

        let credential = OAuthCredentialData.credential(from: data)

        XCTAssertEqual(credential?.accessToken, "token-1")
        XCTAssertEqual(credential?.isExpired, false)
    }

    func testCredentialTreatsPastExpirationAsExpired() {
        let pastMillis = Int(Date().addingTimeInterval(-3600).timeIntervalSince1970 * 1000)
        let data = """
        {"claudeAiOauth":{"accessToken":"token-1","expiresAt":\(pastMillis)}}
        """.data(using: .utf8)!

        XCTAssertEqual(OAuthCredentialData.credential(from: data)?.isExpired, true)
    }

    func testCredentialParsesSecurityCLIOutputWithTrailingNewline() {
        let data = """
        {"claudeAiOauth":{"accessToken":"token-1"}}

        """.data(using: .utf8)!

        XCTAssertEqual(OAuthCredentialData.credential(from: data)?.accessToken, "token-1")
    }
}
