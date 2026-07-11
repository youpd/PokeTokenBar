import XCTest
@testable import PokeTokenBar

// UsageStore 의 refresh 파이프라인 + 파생 표시값을 주입 스텁으로 결정적 검증.
// (실제 ccusage/Keychain/Codex 바이너리 없이 — 위협 모델: 1인 로컬, CI 없음)

// MARK: 스텁

private enum StubError: Error { case boom }

/// 호출 후에도 동작을 바꿀 수 있는 usage provider (실패 전환 테스트용). 단일 스레드 테스트 한정.
private final class FakeUsageProvider: UsageProvider, @unchecked Sendable {
    let id: String
    let displayName: String
    nonisolated(unsafe) var daily: DailyUsage?
    nonisolated(unsafe) var enrichment = ProviderEnrichment()
    nonisolated(unsafe) var failDaily = false

    init(id: String, displayName: String, daily: DailyUsage? = nil) {
        self.id = id
        self.displayName = displayName
        self.daily = daily
    }
    func fetchDaily() async throws -> DailyUsage? {
        if failDaily { throw StubError.boom }
        return daily
    }
    func fetchEnrichment() async -> ProviderEnrichment { enrichment }
}

private struct FakeClaudeLimits: ClaudeLimitsProviding {
    var status: LimitStatus?
    func fetch(allowKeychainPrompt: Bool) async throws -> LimitStatus {
        guard let status else { throw LimitsError.keychainInteractionNotAllowed }
        return status
    }
}

/// 호출마다 다른 결과 — 첫 N회는 지정 오류, 이후 성공(또는 실패) 반환. auth-expired 회복 테스트용.
private final class SequenceClaudeLimits: ClaudeLimitsProviding, @unchecked Sendable {
    nonisolated(unsafe) var errors: [any Error]
    nonisolated(unsafe) var success: LimitStatus?
    nonisolated(unsafe) var call = 0
    init(errors: [any Error], success: LimitStatus? = nil) { self.errors = errors; self.success = success }
    func fetch(allowKeychainPrompt: Bool) async throws -> LimitStatus {
        defer { call += 1 }
        if call < errors.count { throw errors[call] }
        if let success { return success }
        throw LimitsError.keychainInteractionNotAllowed
    }
}

private struct FakeCodexLimits: CodexLimitsProviding {
    var status: CodexRateLimitStatus?
    func fetch() async throws -> CodexRateLimitStatus? { status }
}

// MARK: 픽스처 헬퍼

private func todayDaily(_ tokens: Int, cost: Double = 0) -> DailyUsage {
    DailyUsage(date: LocalUsageReader.todayKey(), inputTokens: 0, outputTokens: 0,
               cacheCreationTokens: 0, cacheReadTokens: 0, totalTokens: tokens, totalCost: cost)
}

private func block(tokensPerMinute tpm: Double) -> BlockUsage {
    let json = "{\"blocks\":[{\"id\":\"b\",\"startTime\":\"\",\"endTime\":\"\",\"isActive\":true," +
               "\"totalTokens\":1000,\"costUSD\":1,\"burnRate\":{\"tokensPerMinute\":\(tpm)}}]}"
    return try! JSONDecoder().decode(BlocksReport.self, from: Data(json.utf8)).blocks[0]
}

private func claudeLimits(fiveHourUtil: Double, resetsAt: String? = nil) -> LimitStatus {
    let reset = resetsAt.map { "\"\($0)\"" } ?? "null"
    let json = "{\"five_hour\":{\"utilization\":\(fiveHourUtil),\"resets_at\":\(reset)}}"
    return try! JSONDecoder().decode(LimitStatus.self, from: Data(json.utf8))
}

private func codexLimits(primaryUsed: Int? = nil, secondaryUsed: Int? = nil) -> CodexRateLimitStatus {
    func win(_ p: Int, _ mins: Int) -> String { "{\"usedPercent\":\(p),\"windowDurationMins\":\(mins)}" }
    var parts: [String] = []
    if let primaryUsed { parts.append("\"primary\":\(win(primaryUsed, 300))") }
    if let secondaryUsed { parts.append("\"secondary\":\(win(secondaryUsed, 10080))") }
    let json = "{\"rateLimits\":{\(parts.joined(separator: ","))}}"
    return try! JSONDecoder().decode(CodexRateLimitStatus.self, from: Data(json.utf8))
}

// MARK: 테스트

@MainActor
final class UsageStoreTests: XCTestCase {
    /// 테스트 전용 defaults suite — 실제 사용자 설정(UserDefaults.standard)을 절대 건드리지 않는다.
    private var testDefaults: UserDefaults!
    private var suiteName: String!

    override func setUp() {
        super.setUp()
        suiteName = "ptb-test-\(UUID().uuidString)"
        testDefaults = UserDefaults(suiteName: suiteName)
        KeychainAccessGate.isDisabled = false
    }

    override func tearDown() {
        testDefaults.removePersistentDomain(forName: suiteName)
        super.tearDown()
    }

    private func makeStore(
        providers: [any UsageProvider],
        claude: LimitStatus? = nil,
        codex: CodexRateLimitStatus? = nil
    ) -> UsageStore {
        UsageStore(providers: providers,
                   claudeLimitsProvider: FakeClaudeLimits(status: claude),
                   codexLimitsProvider: FakeCodexLimits(status: codex),
                   autoRefresh: false,
                   defaults: testDefaults)
    }

    // MARK: 집계

    func testAggregatesTodayTokensAcrossProviders() async {
        let claude = FakeUsageProvider(id: "claude_code", displayName: "Claude Code", daily: todayDaily(100_000_000))
        let codex = FakeUsageProvider(id: "codex", displayName: "Codex", daily: todayDaily(50_000_000))
        let store = makeStore(providers: [claude, codex])
        await store.refresh(scheduleEmptyRetry: false)
        XCTAssertEqual(store.todayTotalTokens, 150_000_000)
        XCTAssertNotNil(store.lastUpdated)
        XCTAssertNil(store.lastErrorDescription)
    }

    func testCodexOnlyWhenClaudeHasNoData() async {
        let claude = FakeUsageProvider(id: "claude_code", displayName: "Claude Code", daily: nil) // 데이터 없음
        let codex = FakeUsageProvider(id: "codex", displayName: "Codex", daily: todayDaily(50_000_000))
        let store = makeStore(providers: [claude, codex])
        await store.refresh(scheduleEmptyRetry: false)
        XCTAssertEqual(store.todayTotalTokens, 50_000_000)
        XCTAssertTrue(store.hasUsageData)
        XCTAssertEqual(store.snapshots.count, 1)        // claude 는 today nil → 스냅샷 미생성
        XCTAssertEqual(store.snapshots.first?.providerID, "codex")
    }

    func testStaleDatedSnapshotExcludedFromTodayTotal() async {
        let claude = FakeUsageProvider(id: "claude_code", displayName: "Claude Code", daily: todayDaily(100_000_000))
        // 어제(다른 날짜) 데이터 — 날짜 가드로 오늘 합계에서 제외돼야 함
        let codex = FakeUsageProvider(id: "codex", displayName: "Codex",
            daily: DailyUsage(date: "2000-01-01", inputTokens: 0, outputTokens: 0,
                              cacheCreationTokens: 0, cacheReadTokens: 0, totalTokens: 999, totalCost: 0))
        let store = makeStore(providers: [claude, codex])
        await store.refresh(scheduleEmptyRetry: false)
        XCTAssertEqual(store.todayTotalTokens, 100_000_000)   // codex 999 제외
    }

    func testProviderFailureKeepsPreviousTodayValue() async {
        let claude = FakeUsageProvider(id: "claude_code", displayName: "Claude Code", daily: todayDaily(100_000_000))
        let store = makeStore(providers: [claude])
        await store.refresh(scheduleEmptyRetry: false)
        XCTAssertEqual(store.todayTotalTokens, 100_000_000)

        claude.failDaily = true
        await store.refresh(scheduleEmptyRetry: false)
        XCTAssertEqual(store.todayTotalTokens, 100_000_000)    // 실패 → 이전 값 유지
        XCTAssertNotNil(store.lastErrorDescription)            // 에러는 표면화
    }

    // MARK: 메뉴바 타이틀

    func testMenuTitleReflectsToggles() async {
        let claude = FakeUsageProvider(id: "claude_code", displayName: "Claude Code", daily: todayDaily(100_000_000, cost: 12.5))
        let store = makeStore(providers: [claude])
        store.showTokensInMenu = true
        store.showCostInMenu = false
        store.showLimitInMenu = false
        await store.refresh(scheduleEmptyRetry: false)
        XCTAssertEqual(store.menuTitle, "100M")

        store.showCostInMenu = true
        XCTAssertEqual(store.menuTitle, "100M · $12.5")
    }

    func testMenuTitleShowsLimitPercents() async {
        let claude = FakeUsageProvider(id: "claude_code", displayName: "Claude Code", daily: todayDaily(10_000_000))
        let store = makeStore(providers: [claude],
                              claude: claudeLimits(fiveHourUtil: 42),
                              codex: codexLimits(primaryUsed: 73))
        store.showTokensInMenu = false
        store.showCostInMenu = false
        store.showLimitInMenu = true
        await store.refresh(scheduleEmptyRetry: false)
        XCTAssertEqual(store.menuTitle, "Claude 42% · Codex 73%")
    }

    // MARK: 한도 경고

    func testLimitWarningWhenClaudeOverCritical() async {
        let claude = FakeUsageProvider(id: "claude_code", displayName: "Claude Code", daily: todayDaily(10_000_000))
        let store = makeStore(providers: [claude], claude: claudeLimits(fiveHourUtil: 96))
        store.critThreshold = 95
        await store.refresh(scheduleEmptyRetry: false)
        XCTAssertTrue(store.isLimitWarning)
        XCTAssertNotNil(store.limits, "한도가 로드돼야 한다")
    }

    func testNoLimitWarningWhenUnderCritical() async {
        let claude = FakeUsageProvider(id: "claude_code", displayName: "Claude Code", daily: todayDaily(10_000_000))
        let store = makeStore(providers: [claude], claude: claudeLimits(fiveHourUtil: 50))
        store.critThreshold = 95
        await store.refresh(scheduleEmptyRetry: false)
        XCTAssertFalse(store.isLimitWarning)
    }

    func testLimitWarningFromCodexSecondary() async {
        let claude = FakeUsageProvider(id: "claude_code", displayName: "Claude Code", daily: todayDaily(10_000_000))
        let store = makeStore(providers: [claude], codex: codexLimits(primaryUsed: 10, secondaryUsed: 97))
        store.critThreshold = 95
        await store.refresh(scheduleEmptyRetry: false)
        XCTAssertTrue(store.isLimitWarning)
    }

    func testLimitWarningFromForecastAtFullUtilization() async {
        // crit 을 100 초과로 올려 임계 분기를 끄고, util 100 → 예측 분기만으로 경고가 켜지는지 확인
        let claude = FakeUsageProvider(id: "claude_code", displayName: "Claude Code", daily: todayDaily(10_000_000))
        let store = makeStore(providers: [claude],
                              claude: claudeLimits(fiveHourUtil: 100, resetsAt: "2099-01-01T00:00:00Z"))
        store.critThreshold = 101
        await store.refresh(scheduleEmptyRetry: false)
        XCTAssertTrue(store.isLimitWarning)   // fiveHourForecast(beforeReset:true)
    }

    // MARK: burn tier

    func testBurnTierThresholds() async {
        func tier(_ tpm: Double) async -> BurnTier {
            let p = FakeUsageProvider(id: "claude_code", displayName: "Claude Code", daily: todayDaily(10_000_000))
            p.enrichment = ProviderEnrichment(activeBlock: block(tokensPerMinute: tpm), blocksOK: true,
                                              weekTotal: nil, monthTotal: nil, periodsOK: false)
            let store = makeStore(providers: [p])
            await store.refresh(scheduleEmptyRetry: false)
            return store.burnTier
        }
        let idle = await tier(500)         // <=1000 → idle
        let normal = await tier(50_000)    // <100k → normal
        let fast = await tier(200_000)     // <400k → fast
        let blazing = await tier(500_000)  // >=400k → blazing
        XCTAssertEqual(idle, .idle)
        XCTAssertEqual(normal, .normal)
        XCTAssertEqual(fast, .fast)
        XCTAssertEqual(blazing, .blazing)
    }

    /// Codex 전용 사용자도 burn tier 가 반영되는지 (프로바이더 종속 제거 회귀 방지).
    func testBurnTierFromNonClaudeProvider() async {
        let codex = FakeUsageProvider(id: "codex", displayName: "Codex", daily: todayDaily(10_000_000))
        codex.enrichment = ProviderEnrichment(activeBlock: block(tokensPerMinute: 200_000), blocksOK: true,
                                              weekTotal: nil, monthTotal: nil, periodsOK: false)
        let store = makeStore(providers: [codex])
        await store.refresh(scheduleEmptyRetry: false)
        XCTAssertEqual(store.burnTier, .fast)
    }

    /// 자정 직후 — 오늘 토큰 0이지만 **활성 5h 블록**이 있으면 캐리어 스냅샷 생성.
    /// (없으면 매일 자정~첫토큰 창에서 burn/forecast/주월이 소실되던 버그 회귀 방지)
    func testMidnightCarrierSnapshotFromActiveBlock() async {
        let claude = FakeUsageProvider(id: "claude_code", displayName: "Claude Code", daily: nil)
        claude.enrichment = ProviderEnrichment(
            activeBlock: block(tokensPerMinute: 200_000), blocksOK: true,
            weekTotal: PeriodUsage(period: "w", totalTokens: 90_000_000, totalCost: 0),
            monthTotal: PeriodUsage(period: "m", totalTokens: 300_000_000, totalCost: 0),
            periodsOK: true)
        let store = makeStore(providers: [claude])
        await store.refresh(scheduleEmptyRetry: false)

        let snap = store.snapshot(preferring: "claude_code")
        XCTAssertEqual(snap?.providerID, "claude_code", "캐리어 스냅샷 미생성")
        XCTAssertNil(snap?.today, "오늘 데이터는 없어야(today=nil)")
        XCTAssertEqual(store.weekTotalTokens, 90_000_000)
        XCTAssertEqual(store.burnTier, .fast, "자정 직후에도 활성 블록으로 burn 반영(idle 아님)")
    }

    /// 오늘·최근 미사용(활성 블록 없음)인데 주/월 기록만 있으면 캐리어를 만들지 않는다 —
    /// weekTotal 이 항상 non-nil 이라 탭이 뜨던 회귀 방지("안 썼는데 왜 뜨지").
    func testNoCarrierForWeekMonthOnlyWithoutActiveBlock() async {
        let codex = FakeUsageProvider(id: "codex", displayName: "Codex", daily: nil)
        codex.enrichment = ProviderEnrichment(
            activeBlock: nil, blocksOK: true,   // 최근 5h 사용 없음 → 블록 없음
            weekTotal: PeriodUsage(period: "w", totalTokens: 50_000_000, totalCost: 0),
            monthTotal: PeriodUsage(period: "m", totalTokens: 80_000_000, totalCost: 0),
            periodsOK: true)
        let store = makeStore(providers: [codex])
        await store.refresh(scheduleEmptyRetry: false)
        XCTAssertFalse(store.snapshots.contains { $0.providerID == "codex" },
                       "오늘·최근 미사용 프로바이더는 탭이 뜨면 안 됨")
    }

    /// 여러 프로바이더의 burn 은 합산된다 (60k + 60k = 120k → fast).
    func testBurnTierCombinesProviders() async {
        let claude = FakeUsageProvider(id: "claude_code", displayName: "Claude Code", daily: todayDaily(10_000_000))
        claude.enrichment = ProviderEnrichment(activeBlock: block(tokensPerMinute: 60_000), blocksOK: true,
                                               weekTotal: nil, monthTotal: nil, periodsOK: false)
        let codex = FakeUsageProvider(id: "codex", displayName: "Codex", daily: todayDaily(10_000_000))
        codex.enrichment = ProviderEnrichment(activeBlock: block(tokensPerMinute: 60_000), blocksOK: true,
                                              weekTotal: nil, monthTotal: nil, periodsOK: false)
        let store = makeStore(providers: [claude, codex])
        await store.refresh(scheduleEmptyRetry: false)
        XCTAssertEqual(store.burnTier, .fast)
    }

    // MARK: 확장 규약 — 프로바이더 무관 집계 (CLAUDE.md "확장 규약" 강제)

    /// 하드코딩 allow-list 없이 *임의의 미래 프로바이더*(id 가 claude_code/codex/gemini 어느 것도 아님)가
    /// 범용 집계 경로 전부에 흘러가는지 강제한다. 누군가 오늘/주/월/burn 에 `== "claude_code"` 류
    /// id 분기를 넣어 특정 프로바이더만 세면 이 테스트가 깨진다.
    func testUnknownFutureProviderFlowsThroughAllAggregation() async {
        let future = FakeUsageProvider(
            id: "future_tool_xyz", displayName: "Future Tool", daily: todayDaily(42_000_000))
        future.enrichment = ProviderEnrichment(
            activeBlock: block(tokensPerMinute: 200_000), blocksOK: true,
            weekTotal: PeriodUsage(period: "w", totalTokens: 90_000_000, totalCost: 0),
            monthTotal: PeriodUsage(period: "m", totalTokens: 300_000_000, totalCost: 0),
            periodsOK: true)
        let store = makeStore(providers: [future])
        await store.refresh(scheduleEmptyRetry: false)

        XCTAssertEqual(store.todayTotalTokens, 42_000_000, "오늘 합계가 id 로 필터링됨")
        XCTAssertEqual(store.weekTotalTokens, 90_000_000, "주 합계가 id 로 필터링됨")
        XCTAssertEqual(store.monthTotalTokens, 300_000_000, "월 합계가 id 로 필터링됨")
        XCTAssertEqual(store.burnTier, .fast, "burn 이 특정 프로바이더에만 종속됨")
        // preferring 은 미스 시 .first 폴백이라 id 일치까지 확인해야 유효(theater 방지)
        XCTAssertEqual(store.snapshot(preferring: "future_tool_xyz")?.providerID, "future_tool_xyz",
                       "탭에 노출 안 됨")
    }

    /// 기본 등록 프로바이더 레지스트리 무결성 — 비어 있지 않고 id 가 유일.
    /// (새 프로바이더를 배열에 등록하는 단일 지점이 살아있는지 최소 보증.)
    func testDefaultProviderRegistryHasUniqueIds() {
        let store = UsageStore(autoRefresh: false, defaults: testDefaults)
        let ids = store.registeredProviderIDs
        XCTAssertFalse(ids.isEmpty)
        XCTAssertEqual(Set(ids).count, ids.count, "프로바이더 id 중복")
    }

    // MARK: stale

    // MARK: 세션 만료(401) UX

    func testLimitsAuthExpiredSetOn401AndClearedOnSuccess() async {
        let claude = FakeUsageProvider(id: "claude_code", displayName: "Claude Code", daily: todayDaily(10_000_000))
        let seq = SequenceClaudeLimits(errors: [LimitsError.httpStatus(401)],
                                       success: claudeLimits(fiveHourUtil: 12, resetsAt: "2099-01-01T00:00:00Z"))
        let store = UsageStore(providers: [claude], claudeLimitsProvider: seq,
                               codexLimitsProvider: FakeCodexLimits(status: nil),
                               autoRefresh: false, defaults: testDefaults)
        await store.refresh(scheduleEmptyRetry: false)
        XCTAssertTrue(store.limitsAuthExpired, "401 → 세션 만료 안내 상태")
        await store.refresh(scheduleEmptyRetry: false)   // 이번엔 성공
        XCTAssertFalse(store.limitsAuthExpired, "성공 시 해제")
    }

    func testLimitsAuthExpiredNotSetOnNon401() async {
        let claude = FakeUsageProvider(id: "claude_code", displayName: "Claude Code", daily: todayDaily(10_000_000))
        let store = UsageStore(providers: [claude],
                               claudeLimitsProvider: SequenceClaudeLimits(errors: [LimitsError.httpStatus(500)]),
                               codexLimitsProvider: FakeCodexLimits(status: nil),
                               autoRefresh: false, defaults: testDefaults)
        await store.refresh(scheduleEmptyRetry: false)
        XCTAssertFalse(store.limitsAuthExpired, "500 은 세션 만료 아님 — 오탐 방지")
    }

    func testIsStaleBeforeFirstRefreshThenFreshAfter() async {
        let claude = FakeUsageProvider(id: "claude_code", displayName: "Claude Code", daily: todayDaily(10_000_000))
        let store = makeStore(providers: [claude])
        XCTAssertTrue(store.isStale)   // lastUpdated nil
        await store.refresh(scheduleEmptyRetry: false)
        XCTAssertFalse(store.isStale)
    }

    // MARK: 프로바이더 탭 선택 해석

    func testSnapshotPreferringSelection() async {
        let claude = FakeUsageProvider(id: "claude_code", displayName: "Claude Code", daily: todayDaily(1_000))
        let gemini = FakeUsageProvider(id: "gemini", displayName: "Gemini", daily: todayDaily(2_000))
        let store = makeStore(providers: [claude, gemini])
        await store.refresh(scheduleEmptyRetry: false)
        XCTAssertEqual(store.snapshot(preferring: "gemini")?.providerID, "gemini")
        XCTAssertEqual(store.snapshot(preferring: nil)?.providerID, "claude_code", "선호 없음 → 첫 번째")
        XCTAssertEqual(store.snapshot(preferring: "cursor")?.providerID, "claude_code", "미연결 id → 첫 번째 폴백")
    }

    // MARK: 주/월 누적 유지 (팝오버 깜빡임 회귀 방지)

    /// phase1 재빌드가 이전 스냅샷의 주/월 누적을 이어받지 않으면, 다음 enrichment 가
    /// 다시 채우기 전까지 nil 이 되어 팝오버 "이번 주/이번 달" 행이 사라졌다 나타난다.
    /// enrichment 가 주월을 못 채우는(periodsOK=false) 갱신에서도 이전 값이 유지돼야 한다.
    func testWeekMonthPersistAcrossRefreshWhenEnrichmentSkips() async {
        let claude = FakeUsageProvider(id: "claude_code", displayName: "Claude Code", daily: todayDaily(1_000))
        claude.enrichment = ProviderEnrichment(
            activeBlock: nil, blocksOK: false,
            weekTotal: PeriodUsage(period: "2026-06-28", totalTokens: 7_000, totalCost: 0),
            monthTotal: PeriodUsage(period: "2026-06", totalTokens: 30_000, totalCost: 0),
            periodsOK: true)
        let store = makeStore(providers: [claude])
        await store.refresh(scheduleEmptyRetry: false)
        XCTAssertEqual(store.weekTotalTokens, 7_000)
        XCTAssertEqual(store.monthTotalTokens, 30_000)

        // 다음 갱신: enrichment 가 주월을 채우지 못함 → 이전 값이 살아있어야 한다.
        claude.enrichment = ProviderEnrichment(
            activeBlock: nil, blocksOK: false, weekTotal: nil, monthTotal: nil, periodsOK: false)
        await store.refresh(scheduleEmptyRetry: false)
        XCTAssertEqual(store.weekTotalTokens, 7_000, "주 누적이 재빌드에서 사라지면 안 된다")
        XCTAssertEqual(store.monthTotalTokens, 30_000, "월 누적이 재빌드에서 사라지면 안 된다")
    }

    // MARK: friendlyLimitError 매핑

    func testFriendlyLimitErrorMapping() {
        let l = L(.en)
        XCTAssertEqual(UsageStore.friendlyLimitError(LimitsError.httpStatus(401), l), l.limitRefreshHTTPError(401))
        XCTAssertEqual(UsageStore.friendlyLimitError(LimitsError.httpStatus(500), l), l.limitRefreshHTTPError(500))
        XCTAssertEqual(UsageStore.friendlyLimitError(LimitsError.credentialFormat, l), l.limitRefreshNoCredential)
        XCTAssertEqual(UsageStore.friendlyLimitError(LimitsError.keychainInteractionNotAllowed, l), l.limitRefreshGeneric)
        XCTAssertEqual(UsageStore.friendlyLimitError(StubError.boom, l), l.limitRefreshGeneric)   // 비 LimitsError
    }
}
