import XCTest
@testable import TokenMac

final class CompanionBalanceTests: XCTestCase {
    func testXPMonotonicAndCurve() {
        XCTAssertEqual(CompanionBalance.xp(forTokens: 0), 0)
        // sqrt 곡선: 1M → floor(sqrt(1000)*4)=126
        XCTAssertEqual(CompanionBalance.xp(forTokens: 1_000_000), 126)
        // 10배 토큰이 10배 XP 가 아님(sqrt)
        XCTAssertLessThan(CompanionBalance.xp(forTokens: 10_000_000),
                          CompanionBalance.xp(forTokens: 1_000_000) * 10)
        XCTAssertGreaterThan(CompanionBalance.xp(forTokens: 10_000_000),
                             CompanionBalance.xp(forTokens: 1_000_000))
    }

    func testLevelCurveIncreasingAndCapped() {
        XCTAssertEqual(CompanionBalance.level(forXP: 0), 1)
        XCTAssertEqual(CompanionBalance.cumulativeXP(toReach: 1), 0)
        // 누적 임계값 단조 증가
        for l in 1..<CompanionBalance.maxLevel {
            XCTAssertLessThan(CompanionBalance.cumulativeXP(toReach: l),
                              CompanionBalance.cumulativeXP(toReach: l + 1))
        }
        // 매우 큰 XP 는 maxLevel 로 캡
        XCTAssertEqual(CompanionBalance.level(forXP: 100_000_000), CompanionBalance.maxLevel)
        // 임계값 직전/직후
        let xp5 = CompanionBalance.cumulativeXP(toReach: 5)
        XCTAssertEqual(CompanionBalance.level(forXP: xp5), 5)
        XCTAssertEqual(CompanionBalance.level(forXP: xp5 - 1), 4)
    }
}

/// 결정론적 RNG (졸업 추첨 테스트용)
struct SeededRNG: RandomNumberGenerator {
    var state: UInt64
    init(seed: UInt64) { state = seed }
    mutating func next() -> UInt64 {
        state = state &+ 0x9E37_79B9_7F4A_7C15
        var z = state
        z = (z ^ (z >> 30)) &* 0xBF58_476D_1CE4_E5B9
        z = (z ^ (z >> 27)) &* 0x94D0_49BB_1331_11EB
        return z ^ (z >> 31)
    }
}

@MainActor
final class CompanionStoreTests: XCTestCase {
    private func tempStore(now: Date = Date(timeIntervalSince1970: 1_700_000_000)) -> CompanionStore {
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("companion-test-\(UUID().uuidString).json")
        return CompanionStore(clock: { now }, fileURL: url, rng: SeededRNG(seed: 7))
    }
    // max 레벨에 닿을 만큼 큰 누적(테스트용 천문학적 토큰)
    private let hugeMonth = 60_000_000_000

    func testBackfillHatchesAndSetsLevel() {
        let s = tempStore()
        s.update(todayTokens: 2_000_000, todayDate: "2026-06-23", monthTotal: 50_000_000,
                 burnTier: .normal, limitWarning: false, hasUsageData: true)
        XCTAssertTrue(s.state.didApplyInitialBackfill)
        XCTAssertTrue(s.state.hatched)
        XCTAssertGreaterThan(s.level, 1)
        XCTAssertEqual(s.displayState, .working)   // burn normal
    }

    func testNoDoubleCountSameDay() {
        let s = tempStore()
        s.update(todayTokens: 1_000_000, todayDate: "2026-06-23", monthTotal: 1_000_000,
                 burnTier: .idle, limitWarning: false, hasUsageData: true)
        let xpAfterFirst = s.state.activeXP
        // 같은 날 같은 값 재호출 → totalXP 불변(중복 지급 없음)
        s.update(todayTokens: 1_000_000, todayDate: "2026-06-23", monthTotal: 1_000_000,
                 burnTier: .idle, limitWarning: false, hasUsageData: true)
        XCTAssertEqual(s.state.activeXP, xpAfterFirst)
    }

    func testTodayIncrementAccrues() {
        let s = tempStore()
        s.update(todayTokens: 1_000_000, todayDate: "2026-06-23", monthTotal: 1_000_000,
                 burnTier: .idle, limitWarning: false, hasUsageData: true)
        let before = s.state.activeXP
        // 오늘 토큰이 더 쌓이면 증분만큼 XP 증가
        s.update(todayTokens: 4_000_000, todayDate: "2026-06-23", monthTotal: 4_000_000,
                 burnTier: .idle, limitWarning: false, hasUsageData: true)
        XCTAssertGreaterThan(s.state.activeXP, before)
    }

    func testMidnightRolloverResetsClaim() {
        let s = tempStore()
        s.update(todayTokens: 1_000_000, todayDate: "2026-06-23", monthTotal: 1_000_000,
                 burnTier: .idle, limitWarning: false, hasUsageData: true)
        let day1 = s.state.activeXP
        // 다음 날(오늘 토큰 다시 0부터) → claimedTodayXP 리셋, 새 적립 가능
        s.update(todayTokens: 1_000_000, todayDate: "2026-06-24", monthTotal: 2_000_000,
                 burnTier: .idle, limitWarning: false, hasUsageData: true)
        XCTAssertGreaterThan(s.state.activeXP, day1)
        XCTAssertEqual(s.state.lastDate, "2026-06-24")
    }

    func testEmptyDataDoesNotConsumeBackfill() {
        let s = tempStore()
        // 기동 직후 빈 새로고침(데이터 없음) → 알 유지, backfill 미소진
        s.update(todayTokens: 0, todayDate: "2026-06-23", monthTotal: 0,
                 burnTier: .idle, limitWarning: false, hasUsageData: false)
        XCTAssertFalse(s.state.didApplyInitialBackfill)
        XCTAssertFalse(s.state.hatched)
        XCTAssertEqual(s.displayState, .egg)
        // 실제 누적 데이터 도착 → 이제 소급 부화(높은 레벨)
        s.update(todayTokens: 1_000_000, todayDate: "2026-06-23", monthTotal: 200_000_000,
                 burnTier: .normal, limitWarning: false, hasUsageData: true)
        XCTAssertTrue(s.state.didApplyInitialBackfill)
        XCTAssertTrue(s.state.hatched)
        XCTAssertGreaterThan(s.level, 3)   // 소급으로 레벨 점프
    }

    func testStateMapping() {
        let s = tempStore()
        s.update(todayTokens: 1_000_000, todayDate: "2026-06-23", monthTotal: 1_000_000,
                 burnTier: .idle, limitWarning: false, hasUsageData: true)
        XCTAssertEqual(s.displayState, .idle)
        s.update(todayTokens: 1_000_000, todayDate: "2026-06-23", monthTotal: 1_000_000,
                 burnTier: .blazing, limitWarning: false, hasUsageData: true)
        XCTAssertEqual(s.displayState, .focus)
        s.update(todayTokens: 1_000_000, todayDate: "2026-06-23", monthTotal: 1_000_000,
                 burnTier: .idle, limitWarning: true, hasUsageData: true)
        XCTAssertEqual(s.displayState, .tired)
        s.update(todayTokens: 0, todayDate: "2026-06-23", monthTotal: 1_000_000,
                 burnTier: .idle, limitWarning: false, hasUsageData: true)
        XCTAssertEqual(s.displayState, .sleep)
    }

    func testMaxGraduatesToNewUnownedCompanion() {
        let s = tempStore()
        // 소급으로 max 도달(졸업은 보류)
        s.update(todayTokens: 1, todayDate: "2026-06-23", monthTotal: hugeMonth,
                 burnTier: .idle, limitWarning: false, hasUsageData: true)
        XCTAssertEqual(s.level, CompanionBalance.maxLevel)
        XCTAssertTrue(s.collectionInstances.isEmpty)
        XCTAssertEqual(s.state.activeCompanionID, "mochi")
        // 다음 실제 갱신에서 졸업 → 미보유 캐릭터 부화
        s.update(todayTokens: 2_000_000, todayDate: "2026-06-23", monthTotal: hugeMonth,
                 burnTier: .normal, limitWarning: false, hasUsageData: true)
        XCTAssertEqual(s.collectionInstances.count, 1)
        XCTAssertEqual(s.collectionInstances[0].companionID, "mochi")
        XCTAssertTrue(s.collectionInstances[0].maxed)
        XCTAssertNotEqual(s.state.activeCompanionID, "mochi")  // 새 캐릭터
        XCTAssertEqual(s.level, 1)                              // 새 캐릭터는 처음부터
        XCTAssertEqual(s.justGraduated, "Mochi")
    }

    func testCollectionPhaseNoDuplicates() {
        let s = tempStore()
        s.update(todayTokens: 1, todayDate: "d0", monthTotal: hugeMonth,
                 burnTier: .idle, limitWarning: false, hasUsageData: true)   // backfill: mochi max
        // 매일 큰 today 로 active 를 다시 max → 졸업 반복 (날짜 바뀌면 claimedTodayXP 리셋)
        for d in 1...4 {
            s.update(todayTokens: hugeMonth, todayDate: "d\(d)", monthTotal: hugeMonth,
                     burnTier: .idle, limitWarning: false, hasUsageData: true)
        }
        let ownedIDs = Set(s.collectionInstances.map(\.companionID)).union([s.state.activeCompanionID])
        XCTAssertEqual(ownedIDs, Set(CompanionCatalog.all.map(\.id)))                 // 모두 수집
        XCTAssertEqual(s.collectionInstances.count, CompanionCatalog.all.count - 1)   // 졸업 = 전체-1
        XCTAssertEqual(Set(s.collectionInstances.map(\.companionID)).count,           // 중복 없음
                       s.collectionInstances.count)
    }

    func testLegacyPhase1StateMigrates() throws {
        // Phase 1 JSON(totalXP/hatched) → Phase 2(activeXP/mochi) 마이그레이션
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("companion-legacy-\(UUID().uuidString).json")
        let legacy = #"{"totalXP":5000,"hatched":true,"didApplyInitialBackfill":true,"displayName":"Mochi","lastDate":"2026-06-20","claimedTodayXP":0,"streakDays":2}"#
        try legacy.data(using: .utf8)!.write(to: url)
        let s = CompanionStore(clock: { Date() }, fileURL: url)
        XCTAssertEqual(s.state.activeXP, 5000)
        XCTAssertEqual(s.state.activeCompanionID, "mochi")
        XCTAssertTrue(s.state.hatched)
        XCTAssertEqual(s.level, CompanionBalance.level(forXP: 5000))
    }

    func testCatalogHasUniqueIDs() {
        let ids = CompanionCatalog.all.map(\.id)
        XCTAssertEqual(Set(ids).count, ids.count)
        XCTAssertGreaterThanOrEqual(ids.count, 3)
    }

    func testPersistenceRoundTrip() {
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("companion-persist-\(UUID().uuidString).json")
        let now = Date(timeIntervalSince1970: 1_700_000_000)
        let s1 = CompanionStore(clock: { now }, fileURL: url)
        s1.update(todayTokens: 5_000_000, todayDate: "2026-06-23", monthTotal: 80_000_000,
                  burnTier: .normal, limitWarning: false, hasUsageData: true)
        let lvl = s1.level
        let s2 = CompanionStore(clock: { now }, fileURL: url)
        XCTAssertEqual(s2.level, lvl)
        XCTAssertTrue(s2.state.hatched)
    }
}
