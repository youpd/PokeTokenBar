import Foundation
import Observation

/// 게임 상태의 출처. UsageStore(사용량 출처)의 값을 읽어 현재 active 캐릭터에 XP/레벨을 적립하고,
/// max 도달 시 Collection 으로 졸업 + 미보유 캐릭터로 새 알 부화.
@MainActor
@Observable
final class CompanionStore {
    private(set) var state = CompanionState()
    private(set) var level = 1
    private(set) var todayXP = 0
    private(set) var displayState: CompanionStateKind = .egg
    private(set) var justLeveledUp = false
    /// 직전 졸업한 캐릭터 이름(새 알 안내 문구용). nil 이면 졸업 없음.
    private(set) var justGraduated: String?
    private var levelUpUntil: Date?

    private let clock: () -> Date
    private let fileURL: URL
    private var rng: any RandomNumberGenerator

    var activeTraits: CompanionTraits { CompanionCatalog.traits(for: state.activeCompanionID) }
    var name: String { activeTraits.displayName }
    var isMaxed: Bool { level >= CompanionBalance.maxLevel }
    var collectionInstances: [CompanionInstance] { state.collection }

    var xpIntoLevel: Int { state.activeXP - CompanionBalance.cumulativeXP(toReach: level) }
    var xpForNextLevel: Int {
        guard !isMaxed else { return max(1, xpIntoLevel) }
        return CompanionBalance.cumulativeXP(toReach: level + 1) - CompanionBalance.cumulativeXP(toReach: level)
    }
    var levelProgress: Double {
        guard xpForNextLevel > 0 else { return 1 }
        return min(1, max(0, Double(xpIntoLevel) / Double(xpForNextLevel)))
    }
    var tokensToNextLevel: Int {
        guard !isMaxed else { return 0 }
        let needXP = max(0, CompanionBalance.cumulativeXP(toReach: level + 1) - state.activeXP)
        let x = Double(needXP) / CompanionBalance.xpMultiplier
        return Int((x * x * CompanionBalance.tokenXPDivisor).rounded())
    }

    init(clock: @escaping () -> Date = Date.init, fileURL: URL? = nil,
         rng: any RandomNumberGenerator = SystemRandomNumberGenerator()) {
        self.clock = clock
        self.fileURL = fileURL ?? Self.defaultURL()
        self.rng = rng
        load()
        level = CompanionBalance.level(forXP: state.activeXP)
        if state.hatched { displayState = .idle }
    }

    static func defaultURL() -> URL {
        let dir = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("TokenMac")
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir.appendingPathComponent("companion-state.json")
    }

    func update(todayTokens: Int, todayDate: String, monthTotal: Int,
                burnTier: SpinTier, limitWarning: Bool, hasUsageData: Bool) {
        justGraduated = nil   // 이번 update 에서 졸업했을 때만 설정
        let prevLevel = level
        let isBackfillNow = !state.didApplyInitialBackfill

        if !state.didApplyInitialBackfill {
            guard hasUsageData, monthTotal > 0 || todayTokens > 0 else {
                displayState = .egg
                return
            }
            state.activeXP = CompanionBalance.xp(forTokens: max(monthTotal, todayTokens))
            state.claimedTodayXP = CompanionBalance.xp(forTokens: todayTokens)
            state.lastDate = todayDate
            state.didApplyInitialBackfill = true
            state.hatched = true
        } else {
            if todayDate != state.lastDate {
                state.lastDate = todayDate
                state.claimedTodayXP = 0
                if todayTokens > 0 { state.streakDays += 1 }
            }
            let todayXPNow = CompanionBalance.xp(forTokens: todayTokens)
            if todayXPNow > state.claimedTodayXP {
                state.activeXP += todayXPNow - state.claimedTodayXP
                state.claimedTodayXP = todayXPNow
                state.hatched = true
            }
        }

        level = CompanionBalance.level(forXP: state.activeXP)

        // Max → 졸업 + 새 알 (소급 부화 시엔 졸업 보류, 다음 실제 갱신에서)
        if level >= CompanionBalance.maxLevel && !isBackfillNow {
            graduateIfPossible()
        } else if level > prevLevel && !isBackfillNow {
            justLeveledUp = true
            levelUpUntil = clock().addingTimeInterval(4)
        } else if let until = levelUpUntil, clock() > until {
            justLeveledUp = false; levelUpUntil = nil
        }

        todayXP = CompanionBalance.xp(forTokens: todayTokens)
        displayState = computeState(burnTier: burnTier, limitWarning: limitWarning,
                                    hasUsageData: hasUsageData, today: todayTokens)
        save()
    }

    /// active 가 max 도달: Collection 으로 보존하고 미보유 캐릭터로 새 알. 미보유 없으면 maxed 유지.
    private func graduateIfPossible() {
        let owned = Set(state.collection.map(\.companionID)).union([state.activeCompanionID])
        guard let next = CompanionCatalog.nextUnowned(ownedIDs: owned, using: &rng) else {
            return   // 모든 기본 캐릭터 보유 — maxed 유지(중복/등급은 Phase 3)
        }
        state.collection.append(CompanionInstance(
            companionID: state.activeCompanionID, finalXP: state.activeXP,
            maxed: true, hatchedAt: nil, maxedAt: clock()))
        justGraduated = name
        state.activeCompanionID = next.id
        state.activeXP = 0
        level = 1
        justLeveledUp = true
        levelUpUntil = clock().addingTimeInterval(5)
    }

    private func computeState(burnTier: SpinTier, limitWarning: Bool,
                              hasUsageData: Bool, today: Int) -> CompanionStateKind {
        if !state.hatched { return .egg }
        if justLeveledUp { return .levelUp }
        if limitWarning { return .tired }
        if !hasUsageData || today == 0 { return .sleep }
        switch burnTier {
        case .idle: return .idle
        case .normal: return .working
        case .fast, .blazing: return .focus
        }
    }

    private func load() {
        guard let data = try? Data(contentsOf: fileURL),
              let s = try? JSONDecoder().decode(CompanionState.self, from: data) else { return }
        state = s
    }
    private func save() {
        guard let data = try? JSONEncoder().encode(state) else { return }
        try? data.write(to: fileURL)
    }
}
