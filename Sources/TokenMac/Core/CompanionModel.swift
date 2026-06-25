import Foundation

/// 캐릭터의 현재 표시 상태 — UsageStore 신호(burn tier/한도)로 결정
enum CompanionStateKind: String, Sendable {
    case egg, idle, working, focus, tired, sleep, levelUp
}

/// 고양이 변종 마킹 (Phase 2: 고양이 3종). 강아지/오브젝트는 후속 PR.
enum CatMarking: String, Codable, Sendable {
    case tabby       // 크림 태비 (Mochi)
    case grayTabby   // 그레이 태비 (Smoke, Pusheen 풍)
    case points      // 샴 — 다크 포인트 + 블루 눈 (Coco)
}

/// 카탈로그의 캐릭터 정의(트레이트). 인스턴스(실제 키우는 개체)와 분리.
struct CompanionTraits: Sendable, Identifiable {
    let id: String
    let displayName: String
    let marking: CatMarking
    /// 희소도 — 낮을수록 흔함(먼저 부화). Phase 3 등급 가중의 씨앗.
    let rarity: Int
}

enum CompanionCatalog {
    static let all: [CompanionTraits] = [
        CompanionTraits(id: "mochi", displayName: "Mochi", marking: .tabby, rarity: 0),
        CompanionTraits(id: "smoke", displayName: "Smoke", marking: .grayTabby, rarity: 1),
        CompanionTraits(id: "coco",  displayName: "Coco",  marking: .points, rarity: 1),
    ]
    static func traits(for id: String) -> CompanionTraits {
        all.first { $0.id == id } ?? all[0]
    }
    /// 수집 단계: 미보유 중에서 희소도 낮은 군을 우선, 그 안에서 랜덤. 중복 없음.
    static func nextUnowned(ownedIDs: Set<String>, using rng: inout any RandomNumberGenerator) -> CompanionTraits? {
        let pool = all.filter { !ownedIDs.contains($0.id) }
        guard let minRarity = pool.map(\.rarity).min() else { return nil }
        let tier = pool.filter { $0.rarity == minRarity }
        return tier[Int(rng.next() % UInt64(tier.count))]
    }
}

/// XP/레벨 밸런스 — 한 곳의 테이블로.
enum CompanionBalance {
    static let tokenXPDivisor = 1_000.0
    static let xpMultiplier = 4.0
    static let maxLevel = 50

    static func xp(forTokens tokens: Int) -> Int {
        guard tokens > 0 else { return 0 }
        return Int((sqrt(Double(tokens) / tokenXPDivisor) * xpMultiplier).rounded(.down))
    }
    static func cumulativeXP(toReach level: Int) -> Int {
        let l = max(1, level)
        if l <= 1 { return 0 }
        return Int((20.0 * pow(Double(l - 1), 1.85)).rounded())
    }
    static func level(forXP xp: Int) -> Int {
        var lvl = 1
        while lvl < maxLevel && cumulativeXP(toReach: lvl + 1) <= xp { lvl += 1 }
        return lvl
    }
}

/// 컬렉션에 보존되는 졸업(maxed) 개체. 도감 표시용.
struct CompanionInstance: Codable, Sendable, Identifiable {
    var id = UUID().uuidString
    var companionID: String
    var finalXP: Int
    var maxed: Bool = true
    var hatchedAt: Date?
    var maxedAt: Date?
    var level: Int { CompanionBalance.level(forXP: finalXP) }
}

/// 영속 상태. Phase 1(단일 totalXP) → Phase 2(active + collection) 마이그레이션 안전.
struct CompanionState: Codable, Sendable {
    // 토큰 적립 부기 (졸업과 무관하게 지속)
    var claimedTodayXP = 0
    var didApplyInitialBackfill = false
    var lastDate = ""
    var streakDays = 0
    var hatched = false
    // 현재 키우는 개체
    var activeCompanionID = "mochi"
    var activeXP = 0
    // 졸업 보관함(도감)
    var collection: [CompanionInstance] = []

    init() {}

    init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        claimedTodayXP = try c.decodeIfPresent(Int.self, forKey: .claimedTodayXP) ?? 0
        didApplyInitialBackfill = try c.decodeIfPresent(Bool.self, forKey: .didApplyInitialBackfill) ?? false
        lastDate = try c.decodeIfPresent(String.self, forKey: .lastDate) ?? ""
        streakDays = try c.decodeIfPresent(Int.self, forKey: .streakDays) ?? 0
        hatched = try c.decodeIfPresent(Bool.self, forKey: .hatched) ?? false
        activeCompanionID = try c.decodeIfPresent(String.self, forKey: .activeCompanionID) ?? "mochi"
        // Phase 1 마이그레이션: 옛 totalXP → activeXP
        activeXP = try c.decodeIfPresent(Int.self, forKey: .activeXP)
            ?? c.decodeIfPresent(Int.self, forKey: .totalXP) ?? 0
        collection = try c.decodeIfPresent([CompanionInstance].self, forKey: .collection) ?? []
    }

    func encode(to encoder: any Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(claimedTodayXP, forKey: .claimedTodayXP)
        try c.encode(didApplyInitialBackfill, forKey: .didApplyInitialBackfill)
        try c.encode(lastDate, forKey: .lastDate)
        try c.encode(streakDays, forKey: .streakDays)
        try c.encode(hatched, forKey: .hatched)
        try c.encode(activeCompanionID, forKey: .activeCompanionID)
        try c.encode(activeXP, forKey: .activeXP)
        try c.encode(collection, forKey: .collection)
    }

    enum CodingKeys: String, CodingKey {
        case claimedTodayXP, didApplyInitialBackfill, lastDate, streakDays, hatched
        case activeCompanionID, activeXP, collection
        case totalXP   // legacy(Phase 1) — decode 전용
    }
}
