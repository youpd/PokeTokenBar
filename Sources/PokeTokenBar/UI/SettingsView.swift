import ServiceManagement
import SwiftUI

struct SettingsView: View {
    @Environment(UsageStore.self) private var store
    @Environment(CompanionStore.self) private var companion
    /// 팝오버 내부 화면 전환 방식 — sheet/dismiss 를 쓰지 않는다 (PopoverView 의 NOTE 참조)
    var onClose: () -> Void
    @State private var launchAtLogin = SMAppService.mainApp.status == .enabled
    @State private var launchAtLoginError: String?
    @State private var reportError: String?

    private var l: L { companion.l }

    private var isBundledApp: Bool {
        Bundle.main.bundlePath.hasSuffix(".app")
    }

    /// 현재 앱 버전 — 업데이트 적용 여부 확인용으로 설정창 하단에 표기.
    private static var appVersion: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "—"
    }

    /// 문제점 알리기 — 진단 정보(버전·macOS)가 채워진 리포트 메일을 기본 메일 앱으로 연다.
    /// 메일 앱이 없거나 열기에 실패하면 수신 주소를 안내(복사 가능)한다.
    private func reportProblem() {
        let subject = l.reportMailSubject(Self.appVersion)
        let body = l.reportMailBody(
            version: Self.appVersion,
            os: ProcessInfo.processInfo.operatingSystemVersionString)
        guard let url = SupportMail.mailtoURL(subject: subject, body: body),
              NSWorkspace.shared.open(url) else {
            reportError = l.reportMailFallback(SupportMail.address)
            return
        }
        reportError = nil
    }

    /// 푸터 링크 — 버전 표기와 동일한 크기·색을 상속하고 밑줄로만 구분(부모 HStack 스타일 사용).
    private func footerLink(_ title: String, _ urlString: String) -> some View {
        Button {
            if let url = URL(string: urlString) { NSWorkspace.shared.open(url) }
        } label: {
            Text(title).underline()
        }
        .buttonStyle(.plain)
        .help(urlString)
    }

    var body: some View {
        @Bindable var store = store
        VStack(alignment: .leading, spacing: 14) {
            Text(l.settings)
                .font(.headline)

            Picker(l.language, selection: Binding(
                get: { companion.language },
                set: { companion.setLanguage($0); store.localizationLanguage = $0 })) {
                ForEach(AppLanguage.allCases, id: \.self) { lang in
                    Text(lang.label).tag(lang)
                }
            }
            .pickerStyle(.menu)

            Picker(l.refreshInterval, selection: $store.refreshInterval) {
                ForEach(UsageStore.intervalPresets, id: \.value) { preset in
                    Text(l.intervalLabel(preset.value)).tag(preset.value)
                }
            }
            .pickerStyle(.menu)

            VStack(alignment: .leading, spacing: 4) {
                Text(l.menuBarItems)
                    .font(.callout)
                Toggle(l.todayTokensShort, isOn: $store.showTokensInMenu)
                Toggle(l.todayCost, isOn: $store.showCostInMenu)
                Toggle(l.limitPercent, isOn: $store.showLimitInMenu)
                Text(l.allOffHint)
                    .font(.caption2)
                    .foregroundStyle(.tertiary)
            }

            VStack(alignment: .leading, spacing: 4) {
                Toggle(l.disableKeychain, isOn: $store.disableKeychainAccess)
                Text(l.disableKeychainHint)
                    .font(.caption2)
                    .foregroundStyle(.tertiary)
                HStack(spacing: 8) {
                    Button {
                        Task { await store.refreshLimitTokenFromKeychain() }
                    } label: {
                        if store.isRefreshingLimitToken {
                            ProgressView()
                                .controlSize(.small)
                        } else {
                            Text(l.refreshLimitToken)
                        }
                    }
                    .disabled(store.disableKeychainAccess || store.isRefreshingLimitToken)
                    Text(l.onlyOnPress)
                        .font(.caption2)
                        .foregroundStyle(.tertiary)
                }
                if let limitTokenRefreshError = store.limitTokenRefreshError {
                    Text(limitTokenRefreshError)
                        .font(.caption2)
                        .foregroundStyle(.orange)
                        .lineLimit(2)
                }
            }

            VStack(alignment: .leading, spacing: 4) {
                Toggle(l.launchAtLogin, isOn: $launchAtLogin)
                    .disabled(!isBundledApp)
                    .onChange(of: launchAtLogin) { _, newValue in
                        do {
                            if newValue {
                                try SMAppService.mainApp.register()
                            } else {
                                try SMAppService.mainApp.unregister()
                            }
                            launchAtLoginError = nil
                        } catch {
                            launchAtLoginError = "\(error.localizedDescription)"
                            launchAtLogin = SMAppService.mainApp.status == .enabled
                        }
                    }
                if !isBundledApp {
                    Text(l.bundledOnly)
                        .font(.caption)
                        .foregroundStyle(.tertiary)
                }
                if let launchAtLoginError {
                    Text(launchAtLoginError)
                        .font(.caption)
                        .foregroundStyle(.red)
                }
            }

            VStack(alignment: .leading, spacing: 6) {
                Text(l.notificationsSection).font(.callout)
                Toggle(l.limitNotificationsLabel, isOn: $store.limitNotifications)
                if store.limitNotifications {
                    // 한도 알림 켜진 경우에만 임계값 슬라이더 노출
                    HStack {
                        Text(l.warning)
                        Slider(value: $store.warnThreshold, in: 50...95, step: 5)
                        Text(TokenFormatter.percent(store.warnThreshold))
                            .monospacedDigit().frame(width: 40, alignment: .trailing)
                    }
                    .font(.caption).padding(.leading, 12)
                    HStack {
                        Text(l.critical)
                        Slider(value: $store.critThreshold, in: 80...100, step: 5)
                        Text(TokenFormatter.percent(store.critThreshold))
                            .monospacedDigit().frame(width: 40, alignment: .trailing)
                    }
                    .font(.caption).padding(.leading, 12)
                }
                Toggle(l.companionNotificationsLabel, isOn: $store.companionNotifications)
            }

            VStack(alignment: .leading, spacing: 4) {
                HStack(spacing: 8) {
                    Button(l.reportProblem) { reportProblem() }
                    Button(l.showLogFile) {
                        NSWorkspace.shared.activateFileViewerSelecting([AppLog.logFileURL])
                    }
                }
                Text(l.reportAttachHint)
                    .font(.caption2)
                    .foregroundStyle(.tertiary)
                if let reportError {
                    Text(reportError)
                        .font(.caption2)
                        .foregroundStyle(.orange)
                        .textSelection(.enabled)   // 폴백 주소를 복사할 수 있게
                }
            }

            Text(l.aggregationNote)
                .font(.caption2)
                .foregroundStyle(.tertiary)

            HStack {
                HStack(spacing: 5) {
                    // 버전과 같은 크기·색, 밑줄로만 링크임을 표시
                    Text("v\(Self.appVersion)")
                    Text("·")
                    footerLink("GitHub", "https://github.com/chattymin/PokeTokenBar")
                    Text("·")
                    footerLink("Web", "https://chattymin.github.io/PokeTokenBar/")
                    Text("·")
                    // 개발자 후원 — 기능 잠금·너지 없는 푸터 링크 (GitHub/Web 과 동급 톤 유지)
                    footerLink("♥ Sponsor", "https://github.com/sponsors/chattymin")
                }
                .font(.caption2)
                .foregroundStyle(.tertiary)
                Spacer()
                Button(l.close) { onClose() }
                    .keyboardShortcut(.defaultAction)
            }
        }
        .padding(16)
    }
}
