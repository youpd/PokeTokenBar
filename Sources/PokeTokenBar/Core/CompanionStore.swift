import Foundation
import Observation
import UserNotifications

/// 게임 상태의 출처. 설치 이후 토큰 사용량으로 포켓몬을 진화시키고, 최종체 + 추가 임계 도달 시
/// 도감(라인 전체)에 보존 + 새 알. 진화 트리/희귀도/이름은 PokeProviding 으로 런타임 주입.
@MainActor
@Observable
final class CompanionStore {
    private(set) var state = CompanionState()
    private(set) var displayState: CompanionStateKind = .egg
    private(set) var currentLine: EvoLine?
    private(set) var isHatching = false
    private(set) var justEvolvedTo: String?     // 이름(연출/문구)
    private(set) var justGraduated: String?
    private var eventUntil: Date?

    /// 부화/진화 연출 트리거 — seq 증가로 UI 가 감지, 팝오버가 닫혀 있었어도 다음 오픈에 1회 재생.
    enum Celebration: Equatable { case hatch(shiny: Bool), evolve }
    private(set) var celebration: Celebration?
    private(set) var celebrationSeq = 0
    private func fireCelebration(_ c: Celebration) { celebration = c; celebrationSeq += 1 }
    /// 연출 재생 후 UI 가 호출(1회성 보장).
    func consumeCelebration() { celebration = nil }

    private let provider: any PokeProviding
    private let clock: () -> Date
    private let fileURL: URL
    private var rng: any RandomNumberGenerator

    init(provider: any PokeProviding = PokeAPIClient.shared,
         clock: @escaping () -> Date = Date.init,
         fileURL: URL? = nil,
         rng: any RandomNumberGenerator = SystemRandomNumberGenerator()) {
        self.provider = provider
        self.clock = clock
        self.fileURL = fileURL ?? Self.defaultURL()
        self.rng = rng
        load()
        if state.active != nil { displayState = .idle }
    }

    static func defaultURL() -> URL {
        let dir = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("PokeTokenBar")
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir.appendingPathComponent("companion-state.json")
    }

    // MARK: 파생값 (UI)

    var language: AppLanguage { state.language }
    func setLanguage(_ lang: AppLanguage) { state.language = lang; save() }
    /// 앱 전체 UI 문자열 — language 변경 시 자동 재렌더.
    var l: L { L(language) }

    var hasActive: Bool { state.active != nil }
    var rarity: Rarity? { state.active?.rarity }
    var currentIsShiny: Bool { state.active?.isShiny ?? false }
    var currentNature: PokemonNature? { state.active?.nature }

    // 알 인큐베이션 (active 없을 때)
    var isEgg: Bool { state.active == nil }
    var eggStarted: Bool { state.eggUsage > 0 }
    var eggProgress: Double { min(1, max(0, Double(state.eggUsage) / Double(PokemonBalance.eggHatchThreshold))) }
    var eggTokensToHatch: Int { max(0, PokemonBalance.eggHatchThreshold - state.eggUsage) }

    var displayName: String {
        guard let a = state.active, let line = currentLine else { return "Token Egg" }
        return line.localizedName(a.currentID, state.language)
    }
    var currentSpeciesID: Int? { state.active?.currentID }
    var isFinalStage: Bool {
        guard let a = state.active, let line = currentLine else { return false }
        return line.tree.node(withID: a.currentID)?.children.isEmpty ?? true
    }
    var stageText: String {
        guard let a = state.active else { return "" }
        return isFinalStage ? l.finalForm : l.stage(a.stageIndex + 1, a.totalForms)
    }
    var threshold: Int {
        guard let a = state.active else { return 1 }
        return PokemonBalance.phaseThreshold(rarity: a.rarity, totalForms: a.totalForms, stageIndex: a.stageIndex)
    }
    var progress: Double {
        guard let a = state.active, threshold > 0 else { return 0 }
        return min(1, max(0, Double(a.usedAtStage) / Double(threshold)))
    }
    var tokensToNext: Int { guard let a = state.active else { return 0 }; return max(0, threshold - a.usedAtStage) }

    /// 진화 라인 표시용: 현재까지 경로 + 다음 후보. (id, kind) kind: done/cur/future
    var lineNodes: [(id: Int, kind: String)] {
        guard let a = state.active, let line = currentLine else { return [] }
        var out: [(Int, String)] = []
        for (i, id) in a.pathIDs.enumerated() {
            out.append((id, i < a.stageIndex ? "done" : (i == a.stageIndex ? "cur" : "future")))
        }
        if let cur = line.tree.node(withID: a.currentID) {
            for ch in cur.children { out.append((ch.speciesID, "future")) }
        }
        return out
    }
    var dexEntries: [DexEntry] { state.dex }

    /// 도감 표시 순서 — 희귀도 내림차순(legendary→common), 동급은 잡은 시각 최신순.
    var dexEntriesSorted: [DexEntry] {
        state.dex.sorted { a, b in
            if a.rarity.sortRank != b.rarity.sortRank { return a.rarity.sortRank > b.rarity.sortRank }
            let ta = a.caughtAt ?? .distantPast
            let tb = b.caughtAt ?? .distantPast
            return ta > tb
        }
    }

    /// 희귀도별 도감 개수(요약 헤더용).
    func dexCount(_ rarity: Rarity) -> Int { state.dex.lazy.filter { $0.rarity == rarity }.count }

    // MARK: 갱신 (AppDelegate 가 UsageStore 값으로 호출)

    func update(todayTokens: Int, todayDate: String, monthTotal: Int,
                burnTier: BurnTier, limitWarning: Bool, hasUsageData: Bool) {
        justEvolvedTo = nil
        if !state.installBaselineSet {
            // 설치 기준선 — 실제 데이터가 도착한 시점의 today 를 baseline 으로(이전 사용량 미카운트).
            // 데이터 도착 전(기동 직후 빈 새로고침)에는 잡지 않는다.
            guard hasUsageData else { displayState = .egg; return }
            state.installBaselineSet = true
            state.claimedTodayTokens = todayTokens
            state.lastDate = todayDate
            save()
        } else {
            if todayDate != state.lastDate { state.lastDate = todayDate; state.claimedTodayTokens = 0 }
            if todayTokens > state.claimedTodayTokens {
                let delta = todayTokens - state.claimedTodayTokens
                state.claimedTodayTokens = todayTokens
                state.usedSinceInstall += delta
                if state.active == nil {
                    state.eggUsage += delta   // 알 인큐베이션 누적
                } else {
                    applyUsage(delta)
                }
            }
        }
        // 이벤트 만료
        if let until = eventUntil, clock() > until { justGraduated = nil; eventUntil = nil }
        // 알 상태 프리패칭 — 종 pre-roll + 라인/스프라이트 예열(부화 순간 딜레이 제거).
        // 성공할 때까지 매 update 틱마다 재시도(성공 후엔 no-op).
        if state.active == nil, state.installBaselineSet, !isHatching {
            Task { await ensureEggPrefetch() }
        }
        // 알이 부화 임계에 도달하면 부화
        if state.active == nil, state.eggUsage >= PokemonBalance.eggHatchThreshold, !isHatching {
            Task { await hatchIfNeeded() }
        }
        // active 인데 라인 미로딩(앱 재시작) → 로드
        if state.active != nil, currentLine == nil, !isHatching {
            Task { await loadCurrentLine() }
        }
        displayState = computeState(burnTier: burnTier, limitWarning: limitWarning,
                                    hasUsageData: hasUsageData, today: todayTokens)
        save()
    }

    /// 토큰 증분을 현재 포켓몬에 적용 — 임계 도달 시 진화/졸업.
    /// 라인 미로딩(재시작 직후·오프라인)이어도 사용량은 항상 적립한다 — 여기서 드롭하면
    /// claimedTodayTokens 는 이미 전진해 델타가 영구 유실된다. 진화 판정만 라인 로드 후로 미룬다.
    func applyUsage(_ delta: Int) {
        guard state.active != nil else { return }
        state.active!.usedAtStage += delta
        guard let line = currentLine else { save(); return }
        var guardCount = 0
        while state.active != nil, guardCount < 50 {
            guardCount += 1
            let a = state.active!
            let thr = PokemonBalance.phaseThreshold(rarity: a.rarity, totalForms: a.totalForms, stageIndex: a.stageIndex)
            guard a.usedAtStage >= thr else { break }
            guard let node = line.tree.node(withID: a.currentID) else { break }
            if node.children.isEmpty {
                graduate(); break
            } else {
                let next = pickNextChild(node, baseID: a.baseID)
                state.active!.pathIDs = Array(a.pathIDs.prefix(a.stageIndex + 1)) + [next.speciesID]
                state.active!.stageIndex += 1
                state.active!.usedAtStage = a.usedAtStage - thr   // 초과분 이월
                let newName = line.localizedName(next.speciesID, state.language)
                justEvolvedTo = newName
                fireCelebration(.evolve)
                notifyCompanionEvent(l.notifEvolveTitle, l.notifEvolveBody(newName))
            }
        }
        save()
    }

    private func pickNextChild(_ node: EvoNode, baseID: Int) -> EvoNode {
        let fresh = node.children.filter { ch in
            ch.finalIDs.contains { !state.collectedFinals.contains("\(baseID):\($0)") }
        }
        let pool = fresh.isEmpty ? node.children : fresh
        return pool[Int(rng.next() % UInt64(pool.count))]
    }

    private func graduate() {
        guard let a = state.active else { return }
        let finalID = a.currentID
        state.collectedFinals.insert("\(a.baseID):\(finalID)")
        state.dex.append(DexEntry(baseID: a.baseID, finalID: finalID,
                                  chainOrder: a.pathIDs, rarity: a.rarity, caughtAt: clock(),
                                  isShiny: a.isShiny, nature: a.nature))
        let name = currentLine?.localizedName(finalID, state.language) ?? ""
        justGraduated = name
        notifyCompanionEvent(l.notifGraduateTitle, l.notifGraduateBody(name))
        eventUntil = clock().addingTimeInterval(6)
        state.active = nil
        currentLine = nil
        state.eggUsage = 0   // 새 알은 처음부터 인큐베이션
        // "알을 받는 순간" 즉시 프리패칭 시작 — 다음 부화의 종·라인·스프라이트 예열.
        Task { await self.ensureEggPrefetch() }
    }

    /// companion 이벤트 시스템 알림(.app + 토글 ON 일 때만). 한도 알림과 독립.
    private var notifSeq = 0
    private func notifyCompanionEvent(_ title: String, _ body: String) {
        guard Bundle.main.bundleIdentifier != nil, Bundle.main.bundlePath.hasSuffix(".app") else { return }
        guard UserDefaults.standard.object(forKey: "companionNotifications") as? Bool ?? true else { return }
        notifSeq += 1
        let content = UNMutableNotificationContent()
        content.title = title
        content.body = body
        content.sound = .default
        UNUserNotificationCenter.current().add(
            UNNotificationRequest(identifier: "companion-event-\(notifSeq)", content: content, trigger: nil))
    }

    // MARK: 부화

    func hatchIfNeeded() async {
        guard state.active == nil, !isHatching, state.eggUsage >= PokemonBalance.eggHatchThreshold else { return }
        // 프리패치가 "종 롤 중"(pending 미확정)일 때만 대기 — 이중 rng 소비 방지.
        // pending 확정 후의 예열(라인/스프라이트)과는 동시 진행해도 안전하다.
        guard state.pendingHatchID != nil || !prefetchInFlight else { return }
        // 샘플링(네트워크) 동안 중복 진입 차단 — hatch() 자체 가드와 별개.
        isHatching = true
        // 프리패칭된 종이 있으면 그대로 사용(라인·스프라이트 예열됨 → 딜레이 ~0), 없으면 지금 롤.
        let base: Int?
        if let pending = state.pendingHatchID {
            base = pending
        } else {
            base = await chooseBase()
        }
        isHatching = false
        guard let base else { return }   // 네트워크 불안정 → 알 유지, 다음 update 틱에 재시도
        state.pendingHatchID = nil
        await hatch(baseID: base)
    }

    // MARK: 알 프리패칭

    private var prefetchInFlight = false
    private var prefetchedLineID: Int?   // 라인·스프라이트 예열 완료한 종(세션 메모리)

    /// 알 상태에서 부화를 미리 준비 — ① 종 pre-roll(pendingHatchID, 영속) ② 진화 라인
    /// fetch(provider 캐시 적재) ③ 스프라이트 예열(정적+애니메이션+shiny 애니메이션).
    /// 전부 성공하면 부화 순간 네트워크 0. 실패 지점부터 다음 update 틱에 이어서 재시도.
    private func ensureEggPrefetch() async {
        guard state.active == nil, !isHatching, !prefetchInFlight else { return }
        prefetchInFlight = true
        defer { prefetchInFlight = false }

        if state.pendingHatchID == nil {
            guard let id = await chooseBase() else { return }   // 오프라인 → 다음 틱 재시도
            guard state.active == nil else { return }           // await 사이 부화 완료 케이스 방어
            state.pendingHatchID = id
            save()
        }
        guard let id = state.pendingHatchID, prefetchedLineID != id else { return }
        guard let line = try? await provider.line(baseSpeciesID: id) else { return }   // 라인 예열
        // 스프라이트 예열 — 부화 직후 보일 것들: base 정적+애니메이션, shiny 롤(1/64) 대비 shiny 애니메이션.
        // .app 번들에서만(단위 테스트가 실네트워크에 닿지 않도록 — 알림과 동일한 게이트).
        if Bundle.main.bundlePath.hasSuffix(".app") {
            _ = await SpriteStore.shared.data(speciesID: line.baseID, animated: false, shiny: false)
            _ = await SpriteStore.shared.data(speciesID: line.baseID, animated: true, shiny: false)
            _ = await SpriteStore.shared.data(speciesID: line.baseID, animated: true, shiny: true)
        }
        prefetchedLineID = id
    }

    func hatch(baseID: Int) async {
        guard !isHatching else { return }
        isHatching = true
        defer { isHatching = false }
        guard let line = try? await provider.line(baseSpeciesID: baseID) else { return }
        currentLine = line
        // 부화 임계 초과분은 부화체 성장에 이월(낭비 없음).
        let overflow = max(0, state.eggUsage - PokemonBalance.eggHatchThreshold)
        state.eggUsage = 0
        // 개체 롤 — shiny(1/64)·성격(25종)은 부화 순간 확정, 진화해도 유지.
        let isShiny = rng.next() % PokemonOdds.shinyDenominator == 0
        let nature = PokemonNature.allCases[Int(rng.next() % UInt64(PokemonNature.allCases.count))]
        state.active = MonState(baseID: line.baseID, pathIDs: [line.baseID], stageIndex: 0,
                                usedAtStage: 0, rarity: line.rarity, totalForms: line.totalForms,
                                isShiny: isShiny, nature: nature)
        let name = line.localizedName(line.baseID, state.language)
        notifyCompanionEvent(isShiny ? l.notifShinyHatchTitle : l.notifHatchTitle,
                             isShiny ? l.notifShinyHatchBody(name) : l.notifHatchBody(name))
        displayState = .levelUp
        eventUntil = clock().addingTimeInterval(4)
        if overflow > 0 { applyUsage(overflow) }   // 이월분 즉시 반영(필요 시 진화까지)
        // 연출은 이월 진화 뒤에 발화 — 이월 evolve 가 shiny 부화 버스트를 덮지 않도록
        // 마지막 이벤트를 hatch 로 유지한다. 이월로 즉시 졸업한 극단 케이스면 생략(이미 도감행).
        if state.active != nil { fireCelebration(.hatch(shiny: isShiny)) }
        save()
    }

    private func loadCurrentLine() async {
        guard let a = state.active, currentLine == nil, !isHatching else { return }
        isHatching = true
        defer { isHatching = false }
        if let line = try? await provider.line(baseSpeciesID: a.baseID) {
            currentLine = line
            applyUsage(0)   // 라인 미로딩 동안 적립된 사용량이 임계를 넘었으면 지금 진화 판정
        }
    }

    /// 부화 종 선정 — 하드코딩 풀 없이 PokéAPI 1~5세대 base 전체(329종)에서 가중 선택.
    ///   ① base 인덱스(id + capture_rate)를 GraphQL 1쿼리로 취득(30일 디스크 캐시 → 보통 0콜)
    ///   ② 가중치 = 공식 capture_rate 그대로(캐터피 255 vs 뮤츠 3 = 85:1, 전설군 ≈ 0.77%)
    ///      단, 이미 수집한 base 는 가중치 ½(미수집 부스트 — 재부화/shiny 사냥은 열어둠)
    ///   ③ 누적 가중치에서 정확히 1롤 — 루프/재롤 없음, 시간 상한 확정적
    /// 인덱스 취득 실패(오프라인 + 캐시 없음) 시 nil → 알 유지, 다음 갱신 틱 재시도.
    private func chooseBase() async -> Int? {
        guard let index = try? await provider.baseSpeciesIndex(), !index.isEmpty else { return nil }
        let weights = index.map { e in
            state.collectedFinals.contains(where: { $0.hasPrefix("\(e.id):") })
                ? max(1, e.captureRate / 2) : max(1, e.captureRate)
        }
        let total = weights.reduce(0, +)
        var r = Int(rng.next() % UInt64(total))
        for (i, w) in weights.enumerated() {
            r -= w
            if r < 0 { return index[i].id }
        }
        return index.last?.id   // 도달 불가(방어)
    }

    private func computeState(burnTier: BurnTier, limitWarning: Bool, hasUsageData: Bool, today: Int) -> CompanionStateKind {
        if state.active == nil { return .egg }
        if justGraduated != nil || (eventUntil != nil && clock() < eventUntil!) { return .levelUp }
        if limitWarning { return .tired }
        if !hasUsageData || today == 0 { return .sleep }
        switch burnTier {
        case .idle: return .idle
        case .normal: return .working
        case .fast, .blazing: return .focus
        }
    }

    // MARK: 영속
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
