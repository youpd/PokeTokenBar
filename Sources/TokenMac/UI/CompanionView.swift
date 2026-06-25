import SwiftUI

/// 팝오버용 active 캐릭터 애니메이션 — 상태별 코드 드로잉 NSImage 를 ~12fps 로 갱신.
struct CompanionView: View {
    let store: CompanionStore
    var size: CGFloat = 88

    var body: some View {
        TimelineView(.periodic(from: .now, by: 1.0 / 12.0)) { context in
            Image(nsImage: CompanionRenderer.image(
                size: size, traits: store.activeTraits, state: store.displayState,
                level: store.level, time: context.date.timeIntervalSinceReferenceDate))
            .frame(width: size, height: size)
        }
        .frame(width: size, height: size)
    }
}

/// 컬렉션 도감용 정적 썸네일(idle 포즈, 애니메이션 없음).
struct CompanionThumbnail: View {
    let traits: CompanionTraits
    var level: Int = 1
    var size: CGFloat = 52
    var body: some View {
        Image(nsImage: CompanionRenderer.image(
            size: size, traits: traits, state: .idle, level: level, time: 0.4))
        .frame(width: size, height: size)
    }
}

/// 팝오버 상단 캐릭터 섹션 — 스프라이트 + 이름/레벨 + XP 바 + 오늘 XP + 다음 레벨까지.
struct CompanionHeader: View {
    let store: CompanionStore

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .center, spacing: 12) {
                CompanionView(store: store, size: 72)
                VStack(alignment: .leading, spacing: 4) {
                    HStack(spacing: 6) {
                        Text(store.name).font(.callout.weight(.semibold))
                        Text(store.isMaxed ? "Lv. \(store.level) · MAX" : "Lv. \(store.level)")
                            .font(.caption).foregroundStyle(.secondary)
                    }
                    ProgressView(value: store.levelProgress).controlSize(.small).tint(.orange)
                    HStack(spacing: 8) {
                        if store.todayXP > 0 {
                            Text("오늘 +\(store.todayXP) XP").font(.caption2).foregroundStyle(.secondary)
                        }
                        if !store.isMaxed, store.tokensToNextLevel > 0 {
                            Text("다음 레벨까지 \(TokenFormatter.compact(store.tokensToNextLevel))")
                                .font(.caption2).foregroundStyle(.tertiary)
                        }
                    }
                    Text(statusLine).font(.caption2).foregroundStyle(.secondary)
                }
                Spacer()
            }
            if let graduated = store.justGraduated {
                Text("\(graduated)가 졸업했어요. 새 Token Egg가 도착했어요!")
                    .font(.caption2).foregroundStyle(.orange)
            }
        }
    }

    private var statusLine: String {
        switch store.displayState {
        case .egg:     return "곧 깨어나요."
        case .idle:    return "오늘은 조용히 자리를 지켜요."
        case .working: return "오늘의 작업 흔적이 쌓이고 있어요."
        case .focus:   return "지금은 집중 모드예요."
        case .tired:   return "한도에 가까워요. 잠깐 쉬어도 괜찮아요."
        case .sleep:   return "지금은 자고 있어요."
        case .levelUp: return "\(store.name)가 한 단계 자랐어요!"
        }
    }
}

/// Collection 탭 — 졸업 보관함 도감 + 현재 키우는 캐릭터.
struct CollectionView: View {
    let store: CompanionStore
    private let cols = [GridItem(.adaptive(minimum: 84), spacing: 10)]

    var body: some View {
        ScrollView {
            LazyVGrid(columns: cols, spacing: 10) {
                // 현재 active
                cell(traits: store.activeTraits, level: store.level, maxed: store.isMaxed, active: true)
                // 졸업 보관함
                ForEach(store.collectionInstances) { inst in
                    cell(traits: CompanionCatalog.traits(for: inst.companionID),
                         level: inst.level, maxed: inst.maxed, active: false)
                }
            }
            .padding(.vertical, 4)
            Text("토큰을 먹고 자라요. max 달성 캐릭터는 여기 보존돼요.")
                .font(.caption2).foregroundStyle(.tertiary)
                .frame(maxWidth: .infinity, alignment: .leading)
        }
        .frame(maxHeight: 240)
    }

    private func cell(traits: CompanionTraits, level: Int, maxed: Bool, active: Bool) -> some View {
        VStack(spacing: 2) {
            ZStack(alignment: .topTrailing) {
                CompanionThumbnail(traits: traits, level: level, size: 52)
                if maxed {
                    Image(systemName: "crown.fill").font(.system(size: 9))
                        .foregroundStyle(.yellow).padding(2)
                }
            }
            Text(traits.displayName).font(.caption2)
            Text(active ? "Lv. \(level)" : "Lv. \(level) · MAX")
                .font(.system(size: 9)).foregroundStyle(.secondary)
        }
        .padding(6)
        .background(active ? Color.orange.opacity(0.10) : Color.secondary.opacity(0.06))
        .clipShape(RoundedRectangle(cornerRadius: 8))
    }
}
