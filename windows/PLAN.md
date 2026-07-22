# PokeTokenBar — Windows 포팅 기획서

> **버전** 1.1 (2026-07-22) · **대상 원본** PokeTokenBar macOS v2.4.4 (commit 953bea5)
> **목적**: 이 문서 하나로 코딩 에이전트(Codex)가 Windows 버전을 마일스톤 단위로 구현할 수 있게 한다.
> 원본 Swift 코드는 "동작의 최종 레퍼런스"이며, 이 문서는 그 동작을 Windows 용어로 재기술한 스펙이다.
> (각 섹션 끝에 원본 파일 경로를 달아둠 — 애매하면 원본 코드를 읽고 따를 것. 원본은 이 저장소
> 루트의 `Sources/PokeTokenBar/` 에 있다.)

---

## 0. TL;DR

- **무엇**: macOS 메뉴바 앱 PokeTokenBar(AI CLI 토큰 사용량 트래커 + 포켓몬 컴패니언 게임)를
  **Windows 시스템 트레이 앱**으로 포팅한다.
- **스택 (결정)**: **C# / .NET 10 (LTS) + WPF**, 트레이는 `H.NotifyIcon.Wpf`, 토스트는
  `Microsoft.Toolkit.Uwp.Notifications`, 테스트는 xUnit. 단일 `dotnet` 툴체인, 외부 의존 최소.
- **핵심 UX 번역**: macOS "메뉴바에 스프라이트+텍스트" → Windows "애니메이션 트레이 아이콘 +
  리치 툴팁 + 클릭 시 플라이아웃(팝오버 이식)". 트레이는 텍스트를 못 그리므로 숫자는 툴팁/플라이아웃으로.
- **데이터 소스**: macOS 와 동일한 홈 폴더 로그(`%USERPROFILE%\.claude|.codex|.gemini`)를 직접 파싱.
  Claude 한도는 **키체인 없이** `~/.claude/.credentials.json` 파일로 (Windows 가 오히려 단순).
- **개발 방식**: **이 저장소의 `windows/` 하위(모노레포)** 에서 마일스톤 M0~M6 순서로 Codex 에게
  시킨다. 루트의 macOS Swift 코드는 읽기 전용 레퍼런스. 각 마일스톤에 완료 기준(DoD)과 필수 테스트가
  정의돼 있다.

---

## 1. 목표 / 비목표

### 목표
1. macOS 버전과 **기능 패리티**(P0/P1 범위)를 가진 네이티브 Windows 10/11 트레이 앱.
2. 원본의 하드윈 규칙(회귀 방지 인바리언트, §11)을 **스펙+테스트로 계승** — 같은 버그를 다시 만들지 않는다.
3. 원본과 동일한 확장 규약: 새 프로바이더 = `IUsageProvider` 구현 1개 + 레지스트리 등록 1곳.
4. 가벼움: 상시 상주 앱으로서 idle CPU ≈ 0, 정상 새로고침 ≈ 0.1s (증분 캐시), 콜드 스타트 1회만 느림.

### 비목표 (v1 에서 하지 않음)
- macOS 와의 코드 공유 (Swift 앱은 그대로 유지 — Windows 는 별도 구현, 스펙만 공유).
- WSL 내부 로그 자동 탐지 (P2 — §6.7 `extraHomes` 설계만 반영).
- 자동 업데이트 설치(Velopack 류) — v1 은 "새 버전 알림 + 릴리스 페이지/패키지매니저 안내"까지.
- Microsoft Store 배포, 코드사이닝 인증서 구매.

---

## 2. 스택 결정

### 결정: C# / .NET 10 (LTS) + WPF

| 기준 | 판단 |
|---|---|
| 상주 트레이 앱 적합성 | WPF + `H.NotifyIcon` 은 검증된 조합 (EarTrumpet 등). 트레이 아이콘 동적 교체(애니메이션), 커스텀 툴팁, 플라이아웃 전부 가능 |
| AI 코딩 에이전트 친화성 | C#/WPF 는 학습 데이터가 가장 풍부한 축. JSON 파싱·HTTP·프로세스 실행·타이머 전부 표준 라이브러리 |
| 툴체인 단순성 | `dotnet build` / `dotnet test` / `dotnet publish` 하나로 끝. CI(windows-latest) 구성 1파일 |
| UI 요구(다크 카드형 팝오버, 픽셀 스프라이트, 진행바, 연출) | WPF 커스텀 스타일로 충분. `NearestNeighbor` 스케일링으로 픽셀아트 보존 |
| 배포 | self-contained 단일 exe (~80MB) → zip / Scoop / winget |

### 기각한 대안
| 대안 | 기각 사유 |
|---|---|
| WinUI 3 | 트레이 공식 API 부재, 언패키지드 앱 마찰(윈도잉/활성화 버그 이력), 콜드스타트 무거움. 이 앱은 셸 통합이 본체라 안정성이 미감보다 우선 |
| Tauri 2 (Rust) | 가능하나 Rust+WebView2 이중 스택. AI 반복 수정 시 빌드/borrow 마찰, 플라이아웃-트레이 통합이 WPF 보다 손이 많이 감 |
| Electron | 상시 상주 앱에 150MB+/RAM 수백 MB 는 제품 철학(가벼운 네이티브)과 정면 충돌 |
| Avalonia | 크로스플랫폼이 목표가 아님. 트레이/토스트/셸 통합이 순정 Windows 대비 한 다리 건너감 |
| Go / Flutter / Python | 트레이+커스텀 팝오버+토스트 조합의 성숙도·배포 편의 모두 열세 |

### 프로젝트 구성 (모노레포 — 이 저장소의 `windows/` 하위)

```
PokeTokenBar/                       # 기존 저장소. 루트 = macOS Swift 앱 (불가침 영역)
├─ Package.swift  Sources/  Tests/  scripts/   # macOS 원본 — 포팅의 동작 레퍼런스 (읽기 전용)
├─ AGENTS.md                        # Codex 루트 지침 (영역 구분·언어 규칙)
└─ windows/                         # ★ Windows 포팅의 전부는 이 아래
   ├─ AGENTS.md                     # 포팅 작업 규칙
   ├─ PLAN.md                       # 이 문서
   ├─ PokeTokenBar.sln
   ├─ src/PokeTokenBar.Core/        # 플랫폼 무관 로직 (net10.0) — UI 참조 금지
   │    Models/  Usage/  Limits/  Companion/  Poke/  Util/
   ├─ src/PokeTokenBar.App/         # WPF 앱 (net10.0-windows10.0.17763.0)
   │    Tray/  Flyout/  Views/  Platform/  Assets/
   └─ tests/PokeTokenBar.Tests/     # xUnit
```

- **NuGet 의존은 3개로 시작**: `H.NotifyIcon.Wpf`, `Microsoft.Toolkit.Uwp.Notifications`, `xunit`(테스트).
  그 외(JSON, zlib, HTTP)는 전부 BCL(`System.Text.Json`, `ZLibStream`, `HttpClient`). 원본의 "의존성 0" 철학 계승.
- **모노레포 규칙**:
  - macOS 영역(`Package.swift`, `Sources/`, `Tests/`, `scripts/`, `assets/`, README 들)은 **수정 금지**
    (명시 요청 시 예외). Swift 소스는 `../Sources/PokeTokenBar/` 상대 경로로 바로 읽어 참조한다(§15 매핑).
  - **릴리스 태그 분리**: macOS = `vX.Y.Z`(기존 그대로), Windows = **`win-vX.Y.Z`**. GitHub Release 도
    태그별로 따로 만든다.
  - **CI 분리**: 기존 `.github/workflows/ci.yml`(macOS, swift build/test)은 건드리지 않고,
    `windows-ci.yml` 을 `paths: ['windows/**']` 트리거로 추가한다 (§10.5).
  - 커밋·PR 은 저장소 규약대로 **영어**.
- 버전은 `0.1.0` 부터 독립 시작 (첫 태그 `win-v0.1.0`).
- **라이선스/고지 필수 계승**: MIT + "비공식·비상업 포켓몬 팬 프로젝트" 면책 고지(원본 README §License & disclaimer),
  "포켓몬 에셋은 번들하지 않고 PokéAPI 에서 런타임 fetch + 로컬 캐시" 원칙 그대로.

---

## 3. 원본 기능 인벤토리 & 패리티 등급

P0 = 없으면 제품이 아님 · P1 = v1.0 릴리스에 포함 · P2 = 이후

| # | 기능 | 등급 | 비고 (Windows 번역) |
|---|---|---|---|
| F1 | Claude Code 로컬 로그 파싱 (오늘/주/월/5h블록, 비용) | P0 | 동일 경로·포맷 |
| F2 | Codex 로컬 로그 파싱 | P0 | 동일 |
| F3 | Gemini CLI 로컬 로그 파싱 | P0 | 동일 |
| F4 | 파일 증분 캐시 (mtime+size, 디스크 영속, zlib) | P0 | `ZLibStream` |
| F5 | 트레이 표시: 컴패니언 스프라이트 애니메이션 | P0 | 트레이 아이콘 프레임 교체 (≤5fps) |
| F6 | 트레이 표시: 오늘 토큰/비용/한도% 텍스트 | P0 | **툴팁 + 플라이아웃으로 번역** (§9.2) |
| F7 | 플라이아웃(팝오버): 홈 탭 — 오늘/주/월, 프로바이더별 상세(in/out/cache), 서비스 탭 | P0 | WPF 플라이아웃 창 |
| F8 | Claude 공식 한도 (5h/주간/모델별, oauth/usage) + 플랜 표시 | P0 | credentials.json 파일 기반 — 키체인 로직 전부 삭제 |
| F9 | Codex 공식 한도 (`codex app-server` JSON-RPC) | P0 | 숨김 프로세스로 실행 |
| F10 | 번 레이트 티어 + 5h 소진 예측(forecast) | P1 | 로직 동일 |
| F11 | 한도 알림 (엣지 트리거, 경고/위험 임계) | P1 | Windows 토스트 |
| F12 | 컴패니언 게임: 알→부화→진화→졸업→도감 | P1 | 로직 100% 이식 |
| F13 | 샤이니(1/64)·성격 25종·메타몽 위장(1/128) | P1 | 〃 |
| F14 | 아이템: 이상한사탕/민트/이로치부적 + 가방 + 상점 | P1 | 〃 |
| F15 | 사탕 지급 (한도 100% 채우면, 엣지+영속) | P1 | 〃 |
| F16 | PokéAPI 연동(GraphQL 인덱스+REST 폴백) + 스프라이트 캐시 | P1 | 동일 URL |
| F17 | i18n ko/en/ja (UI + 포켓몬 다국어 이름) | P1 | L-패턴 그대로 이식 |
| F18 | 설정 화면 (전 항목) | P0/P1 | §9.5 |
| F19 | 로그인 시 자동 시작 | P1 | HKCU Run 키. 크래시 자동 재실행(launchd KeepAlive)은 P2(작업 스케줄러) |
| F20 | 업데이트 확인 (GitHub Releases) | P1 | brew 경로 → Scoop/winget 안내로 번역, `win-v` 태그 필터 (§8.5) |
| F21 | 프로바이더 상태 페이지 배너 (statuspage.io) | P1 | 동일 |
| F22 | 전원/화면 이벤트 시 폴링 일시정지 (배터리) | P1 | 세션 잠금/절전 이벤트로 번역 |
| F23 | 진단: 파일 로그(회전), 크래시 훅, 문제 제보 mailto, 패리티 스냅샷 | P1 | 동일 설계 |
| F24 | 트레이에 숫자 아이콘(제2 아이콘, 토큰/비용/한도 중 1개 렌더) | P2 | 옵트인 (§9.2) |
| F25 | WSL 로그 루트 추가 스캔 (`extraHomes`) | P2 | §6.7 |
| F26 | 크래시 자동 재실행 워치독 | P2 | Task Scheduler |

---

## 4. macOS → Windows 플랫폼 매핑

| macOS (원본) | Windows (포팅) |
|---|---|
| `NSStatusItem` + 멀티라인 attributedTitle | `H.NotifyIcon` TaskbarIcon (아이콘 + 툴팁). 텍스트는 아이콘 옆에 못 그림 → §9.2 |
| `NSPopover` (transient) | 보더리스 WPF 창: `Topmost`, `ShowInTaskbar=false`, `Deactivated` 시 숨김, 트레이 근처 배치 |
| `UNUserNotificationCenter` | `ToastNotificationManagerCompat` (권한 요청 불필요 — 요청 플로우 삭제) |
| Keychain (`Claude Code-credentials`) | **없음.** `%USERPROFILE%\.claude\.credentials.json` 만 사용 |
| `UserDefaults` | `%LOCALAPPDATA%\PokeTokenBar\settings.json` (§10.4) |
| `~/Library/Application Support/PokeTokenBar/` | `%LOCALAPPDATA%\PokeTokenBar\` (state, cache, sprites) |
| `~/Library/Logs/PokeTokenBar.log` | `%LOCALAPPDATA%\PokeTokenBar\logs\app.log` (2MB 초과 시 `.old` 1세대 회전 — 동일) |
| `SMAppService`(launchd KeepAlive) | HKCU `...\CurrentVersion\Run` 값 (P2: Task Scheduler 재시작) |
| `NSWorkspace` 화면 슬립/웨이크 | `SystemEvents.PowerModeChanged`(Suspend/Resume), `SessionSwitch`(Lock/Unlock) |
| `NSCalendarDayChanged` | 다음 로컬 자정 타이머 + `SystemEvents.TimeChanged` + 매 refresh 시 todayKey 비교(이중 방어) |
| App Nap 방지(`beginActivity`) | 불필요 (해당 없음) |
| 저전력 모드 시 GIF 생략 | `PowerManager.EnergySaverStatus`(가능하면) 또는 생략 — 5fps 캡만으로도 충분 |
| `ProcessInfo`/`Process` | `System.Diagnostics.Process` + `CreateNoWindow=true` (§8.2) |
| ImageIO GIF 디코드 | `GifBitmapDecoder` + 프레임 메타데이터 `/grctlext/Delay` (1/100s 단위) |
| SwiftUI | WPF (XAML). 픽셀 스프라이트는 `RenderOptions.BitmapScalingMode="NearestNeighbor"` |
| 라이트/다크 자동 | 레지스트리 `HKCU\...\Themes\Personalize\AppsUseLightTheme` + `UserPreferenceChanged` 리스너 |
| 단일 인스턴스 (암묵) | 명시적 named `Mutex("Global\\PokeTokenBar")`, 중복 실행 시 기존 인스턴스에 플라이아웃 열기 신호(P2) 또는 그냥 종료 |
| mailto 문제 제보 | 동일 (`Process.Start(UseShellExecute=true)` 로 mailto URL) |
| Finder 로 로그 표시 | `explorer.exe /select,<path>` |

**주의(트레이 아이콘 핸들 릴리스)**: GDI+ 로 만든 `Icon` 을 프레임마다 교체할 때 이전 핸들을
`DestroyIcon`/`Dispose` 하지 않으면 핸들 릭으로 수 시간 뒤 앱이 죽는다. 프레임 캐시(개체당 프레임
세트를 1회 생성해 재사용)로 생성 자체를 최소화할 것.

---

## 5. 아키텍처

원본의 레이어링을 그대로 가져온다. **확장 규약(코드 리뷰에서 결함으로 보는 규칙)**:

1. **사용량 소스 추가** = `IUsageProvider` 구현 1개 + `UsageStore` 기본 프로바이더 배열 등록. 이 두 곳만.
2. **범용 동작은 프로바이더 무관 집계**: 오늘/주/월 합계·burnTier·컴패니언 리듬은 전 프로바이더
   `snapshots` reduce. 특정 프로바이더에만 계산을 붙이지 마라.
3. **프로바이더 고유 동작만 `providerId` 분기**: 공식 한도(Claude=HTTP, Codex=프로세스), 5h forecast,
   "현재 블록" 행처럼 특정 프로바이더에만 존재하는 것만. 범용 경로에 `== "claude_code"` 리터럴 금지.
4. **바이너리 탐색 경로 추가** = `BinaryLocator.CommonToolDirectories()` 한 곳만.

### Core 인터페이스 (원본 1:1)

```csharp
public interface IUsageProvider {
    string Id { get; }              // "claude_code" | "codex" | "gemini" — 원본과 동일 문자열 유지
    string DisplayName { get; }
    Task<DailyUsage?> FetchDailyAsync(CancellationToken ct);       // critical path
    Task<ProviderEnrichment> FetchEnrichmentAsync(CancellationToken ct); // best effort
}
public record ProviderEnrichment {
    public BlockUsage? ActiveBlock; public bool BlocksOK;
    public PeriodUsage? WeekTotal; public PeriodUsage? MonthTotal; public bool PeriodsOK;
}
public interface IClaudeLimitsProvider { Task<LimitStatus> FetchAsync(bool allowInteractive, CancellationToken ct); }
public interface ICodexLimitsProvider  { Task<CodexRateLimitStatus?> FetchAsync(CancellationToken ct); }
public interface IProviderStatusProvider { Task<Dictionary<string, ProviderStatus>> FetchAsync(CancellationToken ct); }
public interface IPokeProvider { /* line / baseSpeciesIndex / baseSpecies — §8.4 */ }
public interface IRng { ulong Next(); }   // 테스트 시드 주입용 (원본 RandomNumberGenerator 대응)
```

- `UsageStore`(집계·알림·백오프·메뉴 규칙)와 `CompanionStore`(게임 상태)는 UI 프레임워크 무관 클래스
  (Core). WPF 는 `INotifyPropertyChanged` 또는 이벤트로 구독.
- **판정 로직은 순수 함수로 분리** (원본과 동일): `EvaluateLimitAlerts`, `EvaluateCandyGrants`,
  `ForecastDepletion`, `WindowClass`, `TooltipLines`. 부수효과(토스트 발사, 파일 쓰기)와 분리해 테스트.
- `AppEnv.IsRealApp`: 실행 파일명이 테스트 호스트가 아닐 때만 true — 알림 발사·프로덕션 로그·스프라이트
  프리패치·패리티 스냅샷의 **단일 게이트** (원본 `AppEnv.isBundledApp` 대응. 게이트를 여러 곳에 흩뿌려
  조건이 drift 되는 것 방지).

참조: `Sources/PokeTokenBar/Core/UsageProvider.swift`, `UsageStore.swift`, `CompanionStore.swift`

---

## 6. 데이터 소스 명세 (로컬 로그)

공통: 홈 = `Environment.GetFolderPath(UserProfile)`. 스캔은 재귀, `.jsonl`(Gemini 만 `.json` 추가 허용),
**mtime ≥ 윈도우 시작** 인 파일만. 날짜는 전부 **로컬 타임존** `yyyy-MM-dd`. 파일 읽기는
`FileShare.ReadWrite`(CLI 가 쓰는 중에도 읽기), 손상 라인은 조용히 skip.

### 6.1 정규화 레코드 (Entry)

```csharp
record Entry(string Id, DateTime Date, string LocalDay, string Model,
             int Input, int Output, int CacheWrite, int CacheRead) {
    int Total => Input + Output + CacheWrite + CacheRead;
}
```
비용은 집계 시 `ModelPricing.Cost(model, ...)` 로 계산해 버킷에 누적 (§7.3).

### 6.2 Claude Code — `%USERPROFILE%\.claude\projects\**\*.jsonl`

- 라인 필터(빠른 사전검사): 문자열에 `"usage"` 와 `"assistant"` 포함 시에만 JSON 파싱.
- 조건: `type=="assistant"`, `message.usage` 존재, `timestamp`(ISO8601, 소수초 마이크로/밀리 혼재 — §6.6) 파싱 성공.
- 필드: `message.usage.input_tokens / output_tokens / cache_creation_input_tokens / cache_read_input_tokens`,
  `message.model`(없으면 "unknown"), id = `message.id + "|" + requestId`.
- **Dedup (중요)**: 같은 `(message.id, requestId)` 가 스트리밍/세션재개/sidechain 으로 여러 파일에 중복
  기록된다. output 은 증가하므로 **id 별 Total 이 최대인 항목을 남긴다** (파일 내 1차, 전체 수집 후 전역 2차).
  first-occurrence 를 남기면 비용 과소집계.

```jsonc
// 라인 예시 (관련 필드만)
{"type":"assistant","requestId":"req_1","timestamp":"2026-07-22T02:11:05.123Z",
 "message":{"id":"msg_01","model":"claude-opus-4-8",
   "usage":{"input_tokens":4,"output_tokens":220,"cache_creation_input_tokens":1024,"cache_read_input_tokens":88231}}}
```

### 6.3 Codex — `%USERPROFILE%\.codex\sessions\**\rollout-*.jsonl`

- 세션 파일 단위로 파싱. 파일 내 상태 2개 유지: 현재 `model`(라인에 `"model"` 이 보이면
  `payload.model` 또는 `payload.turn_context.model` 로 갱신, 초기값 `"codex"`), `turn` 카운터.
- 조건: `payload.type=="token_count"` && `payload.info.last_token_usage` 존재(턴 델타), `timestamp` 파싱.
- 매핑: `input = max(0, input_tokens - cached_input_tokens)`, `cacheRead = cached_input_tokens`,
  `output = output_tokens`(reasoning 포함됨), `cacheWrite = 0`. id = `"codex|<파일명>|<turn>"`.
- 비용은 로그의 모델별 공식 API 텍스트 토큰 단가로 환산해 daily/week/month 에 합산한다. 이 값은
  **실제 Codex 구독 청구액이 아닌 API 환산 예상비용**이며, UI 의 Codex 행에는 `(구독)`을 함께
  표시한다. 알 수 없는 새 GPT/Codex 모델은 보수적 폴백 단가를 사용하고 단가표는 테스트로 고정한다.

### 6.4 Gemini — `%USERPROFILE%\.gemini\tmp\**\chats\*.jsonl` (+레거시 `.json`)

- 신형 `.jsonl`: 라인 레코드. `tokens` 객체가 있는 레코드를 흡수하되 **같은 `id` 는 마지막 값이 최종**
  (`message_update` 가 나중에 옴 — last-wins, 최초 등장 순서 보존). `timestamp` 없는 레코드는
  "마지막으로 본 timestamp" 를 폴백으로.
- 레거시 `.json`: 단일 객체 `{ startTime, messages:[...] }`, 각 message 의 `tokens`. 폴백 timestamp = startTime.
- 매핑(`usageMetadata` 의미 보존): `input = max(0, input - cached) + tool`, `cacheRead = cached`,
  `output = output + thoughts`, `cacheWrite = 0`. id = `"gemini|<파일명>|<msgId>"`. model 필드 없으면 "gemini".

### 6.5 증분 캐시 (LocalUsageCache)

- 키 = 파일 절대경로, 값 = `{ mtime, size, entries[] }`. `(mtime,size)` 동일하면 재파싱 안 함.
- 디스크 영속: `%LOCALAPPDATA%\PokeTokenBar\usage-cache.json` 을 **zlib 압축**(`ZLibStream`) 저장,
  로드 시 압축 해제 실패하면 평문 JSON 폴백. 저장 트리거: 변경 있을 때만, **최소 60초 간격 스로틀**.
- **prune**: mtime 이 40일보다 오래된 blob 제거 (조회 최대 윈도우가 월/주 시작이므로 충분).
- Claude 만 collect 후 전역 dedupKeepMax 1회 더.
- 목적: 콜드 파싱(수백 MB, 수십 초)을 최초 1회로 제한. 정상 갱신 ≈ 0.1s.

### 6.6 날짜/기간 유틸

- ISO8601 파서: 소수초 마이크로(`.034464+00:00`)·밀리(`.303Z`)·없음 3형태 모두 수용
  (마이크로는 3자리 절단 후 재시도 — 원본 `ISO8601Parser` 로직 이식. `DateTimeOffset.Parse` 라운드트립으로
  대부분 처리되나 테스트로 3형태 고정).
- todayKey = 로컬 `yyyy-MM-dd`. monthKey = `yyyy-MM`. 주 시작 = **사용자 로케일의 FirstDayOfWeek**
  (`CultureInfo.CurrentCulture`) — mac 의 `Calendar.current` 대응.
- **활성 5h 블록**(전 프로바이더 공통): 최근 5시간 내 엔트리를 시각순 정렬 → 첫 엔트리부터 now 까지
  분수(minutes, 최소 1) → `tokensPerMinute = totalTokens / minutes`. `endTime = first + 5h`, `isActive = true`.
  엔트리 없으면 null.
- enrichment 스캔 윈도우: 원본은 `startOfMonth` 하나로 블록/주/월을 다 뽑는다. **포팅에서는
  `min(startOfMonth, startOfWeek)` 로 소폭 수정** — 주가 월 경계를 걸칠 때(예: 월 시작이 수요일)
  월초 며칠간 주간 합계가 이전 며칠 분을 놓치는 원본의 알려진 미세 누락을 없앤다. (의도적 개선, 문서화됨)

### 6.7 (P2) extraHomes — WSL/다중 홈 지원

settings.json 에 `"extraHomes": ["\\\\wsl.localhost\\Ubuntu\\home\\me", ...]` 배열. 각 항목 아래의
`.claude/projects`, `.codex/sessions`, `.gemini/tmp` 를 **추가 스캔 루트**로 합류시킨다(캐시 키는 절대경로라
충돌 없음). UNC 경로는 느릴 수 있으므로 mtime 윈도우가 그대로 방어. v1 에선 설정 파일 직접 편집으로만.

참조: `LocalUsageReader.swift`, `LocalUsageCache.swift`, `LocalUsageProvider.swift`

---

## 7. 도메인 로직 명세 (집계·표시·알림·게임)

### 7.1 UsageStore — 새로고침 파이프라인 (순서 보존 필수)

1. **Phase 1 (critical)**: 전 프로바이더 `FetchDailyAsync` 병렬 → 스냅샷 재구성.
   - 날짜 가드: 이전 스냅샷의 today 는 `date == todayKey` 일 때만 계승 (자정 동결 방지).
   - 프로바이더 실패 → **이전 today 값 유지**. 성공했지만 오늘 데이터 없음 → 스냅샷 제외.
   - week/month/activeBlock 은 이전 값 계승 (Phase 2 전 깜빡임 방지).
   - 성공 시 `lastUpdated` 갱신. 오류가 있어도 스냅샷이 있으면 최초 1회는 lastUpdated 세팅.
   - 스냅샷이 비고 오류도 없으면 **20초 뒤 1회 재시도** 예약 (empty-usage retry).
2. **Phase 2 (best effort)**: `FetchEnrichmentAsync` 병렬 → blocksOK/periodsOK 별로만 덮어씀(실패 시 이전 값).
   - **캐리어 스냅샷 규칙**: 오늘 데이터가 없어 스냅샷이 없던 프로바이더는 **실제 activeBlock 이 있을 때만**
     today=null 스냅샷을 만든다 (어젯밤 코딩이 5h 윈도우에 남은 경우 burn/컴패니언 보존).
     주/월 누적만으로는 만들지 않는다 — "안 쓴 프로바이더 탭이 뜬다" 회귀의 원인 (§11-I3).
3. **한도 조회 (마지막)**: Claude(§8.1, 429 백오프 중이면 skip) → Codex(§8.2) → 상태페이지(§8.3).
4. `CheckLimitNotifications()` → 패리티 스냅샷 기록 → `OnRefresh` 훅(컴패니언 갱신 + 사탕 지급 —
   한도가 신선한 시점에 묶는다).

재진입 가드(`isRefreshing`), 타이머 기본 120s(tolerance 10%), 프리셋 = 수동(0)/1/2/5/15분.
세션 잠금·절전 시 타이머 정지, 해제·복귀 시 재개+즉시 1회 갱신. 자정 넘김 시 즉시 갱신.

### 7.2 파생값 (전부 원본 수치 그대로)

- `todayTotalTokens` / `todayCostTotal`: snapshots 중 `today.date == todayKey` 인 것만 합산.
- `weekTotalTokens`·`monthTotalTokens`(+cost): 스냅샷 합산 (프로바이더 무관).
- `combinedBurnPerMinute`: 전 스냅샷 `activeBlock.tokensPerMinute` 합.
- **burnTier**: `burn ≤ 1_000 → idle`, `< 100_000 → normal`, `< 400_000 → fast`, 이상 → `blazing`.
- **isStale**: lastUpdated null → true; 허용치 = interval>0 ? interval×2 : 1800s.
  → **트레이 아이콘을 stale 로 흐리게 만들지 않는다** (§11-I6). '오래됨'은 플라이아웃에서만.
- **5h forecast** (Claude 전용 — claude_code 의 activeBlock 명시 조회):
  `utilization ≥ 5 && < 100 && blockTokens > 0 && tpm ≥ 10_000` 일 때만.
  `tokensPerPercent = blockTokens / utilization`; `minutesLeft = (100-u) × tpp / tpm`; 24h 이상이면 null.
  `beforeReset = depletion < resetDate`. utilization ≥ 100 이면 즉시 도달로 취급.
- **isLimitWarning**: Claude 4개 레거시 창(5h/주간/주Opus/주Sonnet) + scoped 엔트리 percent,
  Codex 전 bucket 의 primary/secondary/individualLimit 중 하나라도 ≥ critThreshold, 또는 forecast.beforeReset.
- Claude/Codex 한도 스냅샷 **stale 임계 15분** (마지막 성공 시각 기준, 플라이아웃 배지).

### 7.3 모델 단가표 (USD / Mtok — 원본과 동일 값, ccusage 역산 0% 오차)

| model (정확 매칭) | input | output | cacheWrite | cacheRead |
|---|---|---|---|---|
| claude-opus-4-8 / claude-opus-4-7 | 5 | 25 | 6.25 | 0.5 |
| claude-sonnet-4-6 | 3 | 15 | 3.75 | 0.3 |
| claude-haiku-4-5-20251001 | 1 | 5 | 1.25 | 0.1 |
| claude-fable-5 | 0 | 0 | 0 | 0 |
| gpt-5.5 | 5 | 30 | 0 | 0.5 |
| gemini-2.5-pro | 1.25 | 10 | 0 | 0.3125 |
| gemini-2.5-flash | 0.30 | 2.5 | 0 | 0.075 |
| gemini-2.0-flash | 0.10 | 0.4 | 0 | 0.025 |

패밀리 폴백(소문자 contains): opus→(5,25,6.25,0.5) / sonnet→(3,15,3.75,0.3) / haiku→(1,5,1.25,0.1) /
gpt·codex·o4·o3→(5,30,0,0.5) / `gemini` 접두 + pro|flash → 해당 값 / 그 외 전부 0.

### 7.4 트레이 텍스트 규칙 (menuLines → TooltipLines 번역)

토글 3개: showTokens(기본 on) / showCost(off) / showLimit(off).

- lastUpdated 전이면 `["—"]`.
- 활성 항목별 1줄: 토큰 = `TokenFormatter.Compact(today)`, 비용 = `CostCompact`, 한도 = limitLine.
- **limitLine 게이트 (§11-I3)**: `오늘 토큰 > 0` 인 프로바이더만. Claude = `limits.fiveHour.utilization`,
  Codex = `codexLimits.maxPrimaryUsedPercent` → `"Claude 12% · Codex 40%"`. 없으면 줄 자체가 없음.
  (`limits != nil` 로 게이트하지 마라 — 설치만 된 프로바이더가 샌다.)
- 툴팁 1행에 앱명+컴패니언 상태(예: "PokeTokenBar — 이상해씨 (집중)"), 이하 활성 줄들.
  클래식 툴팁 127자 제한 대비: H.NotifyIcon 커스텀 툴팁 사용, 폴백은 `Compact · $ · 한도` 1줄 조인.
- mac 의 "3개 활성 시 토큰·비용 한 줄 + 한도 아랫줄"(메뉴바 높이 제약)은 툴팁에선 제약이 없어
  **각 항목 개별 줄**로 단순화한다. 단, **조합표 테스트는 유지**: 토글 3개 × 한도 유무의 전 조합(2³×2)에
  대해 기대 줄 배열을 표로 고정한 테스트를 작성 (§11-I5).
- (P2, F24) 숫자 아이콘: 설정에서 토큰|비용|한도% 중 **1개**를 골라 제2 트레이 아이콘에 텍스트 렌더.

TokenFormatter (원본 그대로): `987` / `12.3K` / `190.6M` / `1.24B`(B 는 소수 2자리, 후행 0 제거),
grouped = 천단위 콤마, cost = `$%.2f`, costCompact = `<100 → $9.5 / <10000 → $311 / 그 외 $1.2K`,
percent = 정수면 `%.0f%%` 아니면 `%.1f%%`.

### 7.5 한도 알림 (엣지 트리거 — §11-I4 의 본체)

```
EvaluateLimitAlerts(windows: [(key, name, utilization)], warn, crit, ref tiers: Dictionary<string,int>)
  → [LimitAlert(key, name, isCritical, utilization)]
```
- tier = u≥crit ? 2 : u≥warn ? 1 : 0. tier==0 → `tiers.Remove(key)` (재무장). tier > 이전 값일 때만 발화.
- **key 는 안정 식별자만** (`resets_at` 등 매 fetch 변하는 값 금지). 표시명 중복 가능 → key 로만 추적.
- 대상 창 (플라이아웃 표시와 1:1): Claude `claude.fiveHour / claude.sevenDay / claude.sevenDayOpus /
  claude.sevenDaySonnet / claude.scoped.<kind>.<model>.<i>` + Codex bucket 별
  `codex.<bucketKey>.primary|secondary|individual` (bucketKey = limitId ?? limitName ?? "codex").
- 토스트: 제목 = 경고/위험, 본문 = `창이름 utilization%`, 위험만 사운드. tag = `key-critical|warning`.
- 발화 게이트: `limitNotifications` 설정 on + `AppEnv.IsRealApp`.
- 기본 임계: warn 80 / crit 95. 슬라이더 범위 50–95 / 80–100, step 5.

### 7.6 컴패니언 게임 (전 수치 원본 고정)

**밸런스 상수**
- 알 부화 임계 5,000,000 토큰 (초과분은 부화체로 이월).
- 졸업 총량 T: common 750M / uncommon 1.875B / rare 3B / legendary 6B.
- k형태 라인의 i번째(1-based) 형태 비용 = `round(T × i / (k(k+1)/2))` → 합계 = T.
- 희귀도: `is_legendary||is_mythical → legendary`, `capture_rate ≤ 45 → rare`, `≤ 120 → uncommon`, 그 외 common.
- 샤이니 1/64 (이로치부적 보유 시 1/48) — `roll % 분모 == 0`. 성격 25종 균등 롤. 부화 순간 확정, 진화 유지.
- 메타몽 위장: common && totalForms ≥ 2 && `roll % 128 == 0` (실앱에서만 롤). 위장 중 샤이니 숨김,
  첫 진화 임계에서 진화 대신 **리빌**: 메타몽 라인(#132)으로 전환(rare·1형태), 초과분 이월, 연출+알림.
- 진화 while 루프 가드 50회. 분기 진화는 "미수집 final 우선" 풀에서 랜덤.
- 졸업 → DexEntry(체인 순서, 희귀도, 성격, 샤이니, 잡은 시각, **체인 다국어 이름 저장**) + collectedFinals
  `"base:final"` 추가 + 새 알(eggUsage=0) + 프리패치 시작. 도감 정렬: 희귀도 내림차순 → 최신순.

**사용량 적립 (update 훅)** — AppDelegate 대응 계층이 매 관찰 변경/refresh 시 호출:
- 설치 기준선: 최초로 hasUsageData 가 참이 된 시점의 todayTokens 를 claimed 로 시드 (이전 사용량 미카운트).
- 날짜 바뀌면 claimed 리셋. `delta = todayTokens - claimed` 가 양수일 때만 적립 →
  알이면 eggUsage, 아니면 `ApplyUsage(delta)` (라인 미로딩이어도 **적립은 항상**, 진화 판정만 로드 후).
- 표시 상태 우선순위: 알 → levelUp(이벤트 창 4s/졸업 6s/리빌 5s) → tired(isLimitWarning) →
  sleep(데이터 없음 또는 today==0) → burnTier(idle/working/focus).

**아이템/상점**
- 재화 = `usedSinceInstall − spentTokens` (성장 미터는 불변, 구매는 지출 원장만 증가).
- 이상한사탕: XP 100M, 가격 500M, 주간 한도 보상 5개/세션급 1개. 민트: 가격 100M, 성격을 "현재와 다른"
  것으로 재롤. 이로치부적: 가격 3B, 패시브 1회 구매(재구매 불가).
- **사탕 지급**: `EvaluateCandyGrants(windows, ref grantTier)` — 100% 미만 → 재무장(제거), 100% 이상 &&
  이전 tier<1 일 때만 지급. **grantTier 는 영속**(재시작 무한지급 방지), 재무장 변화만 있어도 저장.
  첫 실행은 "이미 100% 인 창 시드만, 지급 없음"(소급 차단). 대상 창: Claude 5h(session)+주간(weekly),
  Codex bucket primary/secondary (windowDurationMins > 1440 → weekly, 그 외/미상 → session).
  Opus/Sonnet/scoped/개인 spend limit 은 제외(이중지급 방지). Gemini 는 공식 한도 없음 → 자연 제외.

**알 프리패치**: 알 상태에서 종 pre-roll(`pendingHatchID` **영속**) → 라인 fetch(캐시 적재) → 스프라이트
예열(정적+GIF+shiny GIF, 실앱에서만). 부화 시 pending 사용 → 네트워크 0. 롤 중복 방지 락(isHatching,
prefetchInFlight) 원본 주석의 경합 시나리오 그대로 보존할 것 (`CompanionStore.swift` 참조 필수).

**영속**: `companion-state.json` (원본과 **동일 스키마·키** — mac 상태 파일을 복사하면 그대로 로드되는
호환 유지가 목표). 알 수 없는 키 무시, 누락 키 기본값(하위호환 디코딩), 빈 pathIDs → 상태 전체를 알로 폴백.
`PTB_STATE_DIR` 환경변수로 상태 디렉토리 오버라이드(QA 용) 유지. 쓰기는 atomic(temp+rename).

참조: `CompanionModel.swift`, `CompanionStore.swift`, `UsageStore.swift`

---

## 8. 외부 연동 명세

### 8.1 Claude 공식 한도 — `GET https://api.anthropic.com/api/oauth/usage`

- 헤더: `Authorization: Bearer <accessToken>`, `anthropic-beta: oauth-2025-04-20`. 타임아웃 15s.
- **토큰 취득 (Windows 단순화)**: `%USERPROFILE%\.claude\.credentials.json` →
  `claudeAiOauth { accessToken, expiresAt(초 또는 밀리초 — 값 > 10^10 이면 ms), subscriptionType, rateLimitTier }`.
  만료 판정: `expiresAt ≤ now + 60s`. 인메모리 캐시, 만료/401 시 파일 재읽기. **키체인 코드 전부 불필요.**
  - 원본의 `allowKeychainPrompt` 분기는 "인터랙티브 UI 를 띄울 수 있나" 의미만 남긴다. Windows 파일
    읽기는 무프롬프트이므로 자동 폴링도 파일 재읽기 가능 (인바리언트: **백그라운드 폴링은 어떤 UI 도
    띄우지 않는다** — §11-I7).
  - 파일이 없거나 만료됐고 API 401 → `limitsAuthExpired` 플래그 → 플라이아웃에 "Claude Code 를
    실행하면 자동 갱신됩니다" 안내 + 재시도 버튼 (mac 의 키체인 버튼 대응).
- 응답 (레거시 + 신형 공존):

```jsonc
{ "five_hour": {"utilization": 12.5, "resets_at": "2026-07-22T05:00:00.000000+00:00"},
  "seven_day": {...}, "seven_day_opus": null, "seven_day_sonnet": null,
  "limits": [ {"kind":"session","percent":12.5,"resets_at":"...","is_active":true},
              {"kind":"weekly_all", ...},
              {"kind":"weekly_scoped","percent":3,"scope":{"model":{"display_name":"Opus 4.8"}}} ] }
```
- `scopedLimitEntries` = 레거시 5h/7d 가 하나라도 있으면 `limits[]` 에서 kind ∈ {session, weekly_all} 제외한
  나머지, 레거시가 전부 비면 `limits[]` 전체.
- 플랜 표시: `subscriptionType` 첫 글자 대문자 + `rateLimitTier` 를 `_` 로 쪼개 `숫자+x` 토큰이 있으면 붙임
  (`max` + `default_claude_max_20x` → `"Max 20x"`).
- 오류 처리: 401/403 → 토큰 캐시 무효화 후 1회 재시도, 그래도 실패면 authExpired. 429 →
  `Retry-After`(초 형식만, 3600 캡) 또는 지수 백오프 300s→×2→3600s 캡, 백오프 중 폴링 skip.
  실패해도 토큰 표시는 무영향(한도 섹션만 숨김/stale).

### 8.2 Codex 공식 한도 — `codex app-server` JSON-RPC (stdio)

- 요청 3줄 (newline-delimited JSON):
  1. `{"method":"initialize","id":0,"params":{"clientInfo":{"name":"token_win","title":"PokeTokenBar","version":"<appVer>"},"capabilities":{"experimentalApi":true}}}`
  2. `{"method":"initialized","params":{}}`
  3. `{"method":"account/rateLimits/read","id":1,"params":{}}`
- stdout 을 라인 단위로 스캔해 `id==1` 인 라인의 `result` 만 취함 (로그/notification 섞임 무시,
  `error` 필드면 예외). 폴링 200ms, 타임아웃 20s → `Kill(entireProcessTree: true)`.
- 응답 (camelCase): `rateLimits { limitId?, limitName?, primary { usedPercent, windowDurationMins?, resetsAt(epoch초)? },
  secondary {...}, credits { balance?, hasCredits, unlimited }, individualLimit { limit, remainingPercent, resetsAt, used },
  planType?, rateLimitReachedType? }` + `rateLimitsByLimitId { "<id>": snapshot }`.
  - snapshots 합성: top-level 우선, byLimitId 는 키 정렬 후 `limitId == (top.limitId ?? "codex")` 중복 제외하고 append.
  - `individualLimit.usedPercent = clamp(100 - remainingPercent, 0, 100)`.
  - `maxPrimaryUsedPercent` = visible snapshots 의 primary.usedPercent 최대값 (트레이 한도 줄에 사용).
- **프로세스 실행 (Windows 규칙)**: `CreateNoWindow=true, UseShellExecute=false`, stdout/stderr →
  임시 파일(`%TEMP%`), 실패 시 stderr 마지막 300자를 로그로. `.cmd`/`.bat` 은
  `cmd.exe /d /s /c "<full path>" app-server` 로, `.ps1` 만 있으면 skip 하고 다음 후보. 자식 PATH 에
  `CommonToolDirectories()` 를 앞에 보강해 전달 (버전매니저 shim 이 본체를 찾도록 — 원본 실측 이슈).
- **BinaryLocator (Windows)**: 해석 순서 = 사용자 수동 지정(settings `codexPath`) → PATH 스캔
  (`PATH` 를 `;` 분리, 각 dir × PATHEXT `.EXE/.CMD/.BAT` — GUI 앱도 사용자 PATH 를 상속하므로 mac 처럼
  로그인 셸을 띄울 필요 없음) → 정적 후보. 성공 캐시 영구(경로 소멸 시 재해석), 미탐지 캐시 TTL 600s.
  정적 후보(존재 검증 필수, 구현 시 최신 관행 재확인):
  - `%APPDATA%\npm\codex.cmd` (npm 전역 기본)
  - `%USERPROFILE%\scoop\shims\codex.exe`
  - `%LOCALAPPDATA%\Microsoft\WinGet\Links\codex.exe`
  - `%USERPROFILE%\.bun\bin\codex.exe`
  - `%LOCALAPPDATA%\pnpm\codex.cmd`
  - `%LOCALAPPDATA%\Volta\bin\codex.exe` (Volta — 설치 시 검증)
  - `%APPDATA%\nvm\` 현재 버전 + `%ProgramFiles%\nodejs\codex.cmd` (nvm-windows symlink)
- codex 미발견 → null 반환(한도 섹션 자연 미표시), 오류 아님.

### 8.3 프로바이더 상태 페이지 (statuspage.io)

`https://status.anthropic.com/api/v2/status.json` → `claude_code`, `https://status.openai.com/api/v2/status.json`
→ `codex`. `{ status: { indicator, description } }`, indicator ∈ none(=정상)/minor/major/critical/maintenance,
미지 값 unknown. 타임아웃 10s, UA `PokeTokenBar`. **실패한 프로바이더는 결과에서 생략 → 이전 값 유지.**
설정 off → 저장 상태 비움 + 표시 즉시 차단. 표시 전용(알림 금지). 별도 타이머 없이 refresh 에 편승.

### 8.4 PokéAPI

- REST `https://pokeapi.co/api/v2/pokemon-species/{id}`: `capture_rate, is_legendary, is_mythical,
  names[](name, language.name), evolution_chain.url, evolves_from_species(null=base)`. 종 캐시(메모리).
- evolution chain URL 은 **https + host==pokeapi.co 검증 후** fetch (SSRF 가드), 체인 트리 →
  `EvoNode(speciesID, children[])`. 라인 전 종의 다국어 이름(ko/en/ja-Hrkt/ja 만) 수집.
- base 인덱스 (부화 후보): GraphQL `https://graphql.pokeapi.co/v1beta2` POST
  `{ pokemonspecies(where:{evolves_from_species_id:{_is_null:true}, id:{_lte:649, _neq:132}}, order_by:{id:asc}) { id capture_rate } }`
  → 디스크 캐시 `base-index.json` 30일 TTL → 만료돼도 오프라인이면 사용 → GraphQL 다운+캐시 없음 시:
  per-hatch REST 폴백(무작위 id 1~649, base 인지 확인, 16회 시도) + 백그라운드 REST 전수 인덱스 구축
  (배치 6, 결과 ≥150 일 때만 영속, 세션당 1회).
- 부화 선택: capture_rate 가중 1롤 (수집済 base 는 가중 ½, 최소 1).
- 스프라이트: `https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon` 기준
  정적 `/{id}.png`, shiny `/shiny/{id}.png`, 애니 `/versions/generation-v/black-white/animated[/shiny]/{id}.gif`,
  아이템 `/sprites/items/{name}.png` (rare-candy, shiny-charm; 민트는 미제공 → 이모지 폴백 🌿).
  캐시: 디스크 `sprites\{id}-[sh][a|s].(png|gif)`·`item-{name}.png` (atomic 쓰기), 메모리 LRU 24개.
  shiny 미제공 → 일반 폴백. **에셋 번들 금지(라이선스)** — 런타임 fetch + 캐시만.

### 8.5 업데이트 확인 — GitHub Releases (모노레포 주의)

이 저장소는 macOS 릴리스(`vX.Y.Z`)와 Windows 릴리스(`win-vX.Y.Z`)가 공존하므로 **`releases/latest` 를
쓰면 안 된다** (맥 릴리스가 latest 로 잡힘). 대신:
`GET https://api.github.com/repos/<owner>/<repo>/releases?per_page=20` → `tag_name` 이 `win-v` 접두인
것 중 최신(semver 숫자 비교, draft/prerelease 제외). `html_url` 은 https+github.com 검증 후에만 열기.
minInterval 1800s (수동 확인은 0). `skippedUpdateVersion` 저장 시 그 버전 무시. 적용 버튼:
Scoop 설치본이면(`scoop` 존재 && `scoop list poke-token-bar` 성공) 안내 다이얼로그로
`scoop update poke-token-bar` 제시(v1 은 자동 실행 없이 안내+페이지 열기), 그 외 릴리스 페이지 열기.

참조: `OAuthLimitsProvider.swift`, `CodexRateLimitsProvider.swift`, `ProcessRunner.swift`,
`BinaryLocator.swift`, `ProviderStatusChecker.swift`, `PokeAPIClient.swift`, `SpriteLoader.swift`, `UpdateChecker.swift`

---

## 9. UI 명세

### 9.1 전반

- 플라이아웃 폭 360px 고정(내용 높이 가변), 설정 화면 높이 460px — 원본 비율 유지.
- 테마: 시스템 라이트/다크 추종(§4 매핑), 라이트/다크 리소스 딕셔너리 2벌.
- 스프라이트는 전부 NearestNeighbor. 숫자는 tabular(고정폭) 숫자 폰트 피처 사용.
- DPI: PerMonitorV2 (app.manifest).

### 9.2 트레이 (menu bar 번역 — 이 포팅의 핵심 결정)

- **아이콘 = 컴패니언**: 알(🥚 2프레임 bob) 또는 현재 포켓몬. 정적 스프라이트 bob(0.5s×2프레임)을 먼저,
  Gen-V GIF 로드되면 실프레임 교체. 프레임 delay 하한 0.2s(≈5fps 캡). 아이콘 크기는 DPI 에 맞춰
  16/20/24/32px 렌더.
- 정지 조건: 세션 잠금, 절전. (mac 의 occlusion 대응은 Windows 에 없음 — 잠금/절전만.)
- **텍스트(오늘 토큰/비용/한도)는 툴팁으로** (§7.4 규칙). 클릭(좌) → 플라이아웃 토글. 우클릭 →
  최소 컨텍스트 메뉴(열기 / 지금 새로고침 / 설정 / 종료).
- 트레이 아이콘은 **시간 경과로 흐려지지 않는다** (§11-I6).
- (P2) 숫자 전용 제2 아이콘 — 기본 off, 설정에서 표시 항목 1개 선택.

### 9.3 플라이아웃 — 탭 4개 (Home / Shop / Bag / Collection) + 설정은 내부 화면 전환

- 헤더: 앱 이름/부제 + 핀 토글(활성화 시 포커스를 잃어도 유지) + 새로고침 버튼.
- 상단: 업데이트 배너(있고 updateNotificationsEnabled 일 때 — 적용/나중에 버튼).
- 탭 전환 세그먼트. **플라이아웃을 닫았다 열면 항상 Home 으로 리셋** (프로바이더 탭 선택은 유지).
- 하단 푸터: (Home 탭에서만) 새로고침 버튼 + "갱신 n분 전" + 오류 삼각형(호버에 상세), 전역으로 설정⚙·종료⏻.

**Home 탭** (위→아래):
1. CompanionHeader — 스프라이트(GIF 애니), 이름(현지화)·단계(i/k 또는 최종형)·성격, 진행바
   (usedAtStage/threshold), 상태 문구(egg/idle/working/focus/tired/sleep/levelUp/진화·졸업 문구),
   진화 라인(done/cur/future 노드), 연출 오버레이(부화/진화/리빌 — 샤이니 전용 버스트 구분),
   사탕 +XP·민트 성격 변경 1회성 피드백 (seq+consume 패턴 그대로).
2. 오늘 토큰: Compact 큰 숫자 + grouped 보조 + $비용. 이번 주/이번 달 행(합산, >0 일 때).
3. 프로바이더 칩 탭(스냅샷 2개 이상일 때) — 합계는 위에 유지, 상세·한도만 탭 스코프.
4. 선택 프로바이더 행: 이름 + 오늘 토큰·비용 + `in / out / cache w / cache r` 분해. Codex 비용은
   `API $… (구독)`으로 간결하게 표시해 실제 구독 청구액과 구분한다.
5. 상태 페이지 배너 (인시던트 있을 때만, 색점+라벨+설명 1줄).
6. 한도 섹션 (프로바이더 고유):
   - Claude: 플랜 행 → [authExpired 안내+재시도] 또는 [최초/stale 시 "공식 한도 불러오기/갱신" 행] →
     5h(진행바+%+리셋 상대시각) → forecast 행(⚠ HH:mm 도달 / ✓ 리셋 전 도달 없음) → 주간 → 주Opus →
     주Sonnet → scoped 엔트리들 → "Claude 현재 5h 블록"(블록 토큰 + 리셋 상대시각). authExpired 면 수치 50% 흐림.
   - Codex: 메타 행(플랜/한도 도달/stale 배지) → bucket 별 (2개 이상이면 bucket 라벨) primary/secondary
     (진행바+%+리셋) → 개인 spend limit (`used / limit` + % + 리셋).
   - 진행바 색: ≥crit 빨강 / ≥warn 주황 / 그 외 초록.
   - Gemini: 한도 섹션 없음.
- 한도 섹션 노출 게이트: Claude = 한도 조회 켜져 있으면(값이 없어도 "불러오기" 행 발견성) / Codex =
  hasVisibleLimit / 그 외 false.

**Shop 탭**: 잔액(= usedSinceInstall − spent, Compact) + 아이템 카드(스프라이트/이모지, 이름, 설명, 가격,
구매 버튼 — canBuy 게이트, 패시브는 보유 시 "적용 중").
**Bag 탭**: 보유 아이템 카드(개수, 사용 버튼 — 사탕은 canUseRareCandy(라인 로딩 필요), 민트는 canUseMint).
**Collection 탭**: 희귀도 집계 헤더 + 도감 리스트(최종형 스프라이트, 이름, 체인 이름들, 희귀도 뱃지,
성격, ✨ 샤이니 뱃지, 잡은 날짜). 체인 이름은 저장분 즉시 표시, 구버전 항목은 fetch 후 백필.

### 9.4 알림 (토스트)

- 한도 경고/위험 (§7.5), 컴패니언 이벤트: 부화(샤이니 별도 카피)/진화/졸업/리빌(샤이니 별도)/사탕 지급
  ("왜 받는지" = 창 이름 포함). 각각 토글로 독립 제어. Windows 는 권한 요청 불필요.

### 9.5 설정 화면 (그룹 → 항목, 기본값)

| 그룹 | 항목 | 기본 |
|---|---|---|
| 일반 | 언어 ko/en/ja (시스템 로케일로 초기값: ko/ja 매칭, 그 외 en) | system |
| | 새로고침 간격 수동/1/2/5/15분 | 2분 |
| | 로그인 시 자동 시작 | off |
| 트레이 표시 | 오늘 토큰 / 오늘 비용 / 한도 % (복수 선택, 전부 끄면 캐릭터만) | on/off/off |
| 알림 | 한도 알림 + 경고(50–95)/위험(80–100) 슬라이더 | on, 80/95 |
| | 컴패니언 이벤트 알림 | on |
| | 프로바이더 상태 확인 (표시 전용) | on |
| 업데이트 | 새 버전 배너 표시 / 지금 확인 버튼(+결과 행) | on |
| 고급(접힘) | Claude 한도 조회 끄기 (credentials 파일 접근 안 함 — 한도 섹션만 숨김) | off |
| | 한도 토큰 재읽기 버튼 (파일 강제 재읽기 + API 재시도) | – |
| | 집계 방식 설명 문구 | – |
| 정보·지원 | 문제 제보(mailto, 버전·OS 자동 첨부) / 로그 파일 표시 / 버전 + GitHub·Web·Sponsor 링크 | – |

### 9.6 i18n

원본 `Localization.swift` 의 `L` 패턴 이식: `record L(AppLanguage lang)` + `T(ko, en, ja)` 메서드,
전 문자열 프로퍼티를 **원본에서 그대로 복사·번역 유지**(수백 개 — 기계적 이식, resx 안 씀).
포켓몬 이름은 PokéAPI names (ko / en / ja-Hrkt→ja 폴백→en). 성격 25종·창 이름 등 도메인 문자열 포함.

참조: `PopoverView.swift`, `SettingsView.swift`, `CompanionView.swift`, `BagView.swift`, `ShopView.swift`, `Localization.swift`

---

## 10. 시스템 통합·운영

### 10.1 파일 배치 (`%LOCALAPPDATA%\PokeTokenBar\`)

```
settings.json          # UserDefaults 대응 (아래 키)
companion-state.json   # 게임 상태 (mac 과 스키마 호환)
usage-cache.json       # zlib 압축 증분 캐시
base-index.json        # PokéAPI base 인덱스 (30d)
last-snapshot.json     # 패리티/QA 스냅샷 (todayTotal, tooltipLines, provider 별 오늘값, lastError)
sprites\               # 스프라이트 캐시
logs\app.log(.old)     # 진단 로그 2MB 회전
```

settings.json 키 (원본 UserDefaults 키 계승): `refreshInterval(120)`, `warnThreshold(80)`,
`critThreshold(95)`, `showTokensInMenu(true)`, `showCostInMenu(false)`, `showLimitInMenu(false)`,
`limitNotifications(true)`, `companionNotifications(true)`, `updateNotificationsEnabled(true)`,
`statusChecksEnabled(true)`, `claudeLimitsDisabled(false)` (구 disableKeychainAccess),
`skippedUpdateVersion`, `codexPath`(수동 지정), (P2) `extraHomes[]`, `numericTrayIcon`.

### 10.2 로그·크래시

- AppLog: ISO8601 접두 append, 실앱 게이트, 2MB 초과 시 `.old` 1세대 회전. 주요 이벤트(phase1 결과,
  한도 갱신/실패, 부화/리빌, 바이너리 해석)를 원본과 같은 밀도로 기록 — 원격 진단의 생명줄.
- 크래시 훅: `AppDomain.UnhandledException` + `TaskScheduler.UnobservedTaskException` +
  `DispatcherUnhandledException` → 로그에 스택 기록 후 (가능하면) 재던짐.

### 10.3 전원/세션

`SystemEvents.PowerModeChanged`: Suspend → 폴링/애니 정지, Resume → 재개+즉시 갱신.
`SystemEvents.SessionSwitch`: Lock → 정지, Unlock → 재개+즉시 갱신. 자정 타이머(§4).

### 10.4 배포

1. **GitHub Releases (이 저장소, `win-vX.Y.Z` 태그)**: `dotnet publish -c Release -r win-x64
   --self-contained -p:PublishSingleFile=true` → `PokeTokenBar-win-x64.zip` 자산. macOS 릴리스(`vX.Y.Z`)와
   같은 저장소에 공존 — 태그 접두로 구분. (트리밍은 WPF 미지원 — 사이즈는 수용.)
2. **Scoop** (brew tap 의 Windows 대응 — 우선): `scoop bucket add` 용 버킷 repo + 매니페스트
   (autoupdate 는 `win-v*` 태그의 자산 URL 패턴). 설치명 `poke-token-bar`.
3. **winget** (P1.5): 커뮤니티 repo PR — 버전마다 자동화(wingetcreate) 가능.
4. 서명 없음 → SmartScreen 경고는 README 에 안내 (원본의 ad-hoc 서명 + quarantine 해제와 동일 포지션).

### 10.5 CI (GitHub Actions)

- **`windows-ci.yml` 신설**: `runs-on: windows-latest`, 트리거 `push`/`pull_request` 에
  **`paths: ['windows/**']`** 필터 — mac 커밋에는 안 돈다. 스텝: `dotnet build windows/PokeTokenBar.sln`
  + `dotnet test windows/PokeTokenBar.sln`.
- 기존 `ci.yml`(macOS swift build/test)은 **수정하지 않는다**. (windows/ 변경에도 돌지만 무해 —
  paths-ignore 추가는 mac 쪽 결정으로 남김.)
- 릴리스 워크플로(후속): `tags: ['win-v*']` 트리거로 publish + zip + Release 생성 + scoop 매니페스트 갱신.

---

## 11. 회귀 방지 인바리언트 (원본에서 피 흘리며 배운 것 — 위반 = 결함)

각 항목은 **필수 테스트**와 짝이다. Codex 는 구현 시 이 목록을 리뷰 체크리스트로 쓴다.

- **I1. 옵셔널 동어반복 금지**: 생산자가 항상 채우는 필드의 `!= null` 검사는 항상 참이다.
  "값이 있나" 는 의미값으로 (`totalTokens > 0`, 진짜 null 가능한 `activeBlock`). — weekTotal 회귀.
  ☑ `WeekMonthOnly_NoCarrierSnapshot` 테스트.
- **I2. 범용 집계는 프로바이더 무관**: 오늘/주/월·burnTier·컴패니언 리듬은 전 스냅샷 reduce.
  ☑ `UnknownFutureProviderFlowsThroughAllAggregation` (가짜 id "future_provider" 가 모든 합계·티어에 반영).
- **I3. 컴팩트 표시는 오늘 사용한 프로바이더만**: 트레이 툴팁/숫자 아이콘의 한도 줄은
  `오늘 토큰 > 0` 게이트. `limits != null` 게이트 금지 (설치만 된 Codex 가 새는 회귀).
  팝오버 상세는 전체 노출 유지. ☑ `TrayLimit_HidesProviderUnusedToday / ShowsUsedToday`.
- **I4. 휘발성 필드를 dedup/identity 키에 금지 + 엣지 트리거**: 알림·사탕은 "임계를 새로 넘는 순간"만,
  경고선 아래로 내려가면 재무장. key 에 `resets_at` 류 금지. 판정은 순수 함수, 부수효과 분리.
  사탕 tier 는 영속, 알림 tier 는 메모리(원본 동일). ☑ `LimitAlert*` 5종 + `CandyGrant*` 시리즈 이식.
- **I5. 다중 토글 표시 규칙은 조합표 전수 테스트**: 토글 3개 × 한도 유무 전 조합의 기대 툴팁 줄 배열
  고정. ☑ `TooltipLinesAllCombinations`.
- **I6. 트레이 아이콘 stale dim 금지**: 시간 기반 stale 로 아이콘/텍스트를 흐리게 하지 않는다
  (슬립 복귀 직후 "고장" 오인). '오래됨' 신호는 플라이아웃에서만.
- **I7. 백그라운드 폴링은 어떤 인터랙티브 UI 도 트리거하지 않는다**: 콘솔 창 플래시(CreateNoWindow 누락),
  UAC, 자격증명 프롬프트, 포커스 스틸 전부 금지. 사용자 눈앞에 뭔가 뜨는 건 사용자 버튼뿐.
  ☑ 프로세스 스타트인포 단위 테스트 + 수동 QA 체크리스트.
- **I8. 실패 시 keep-previous**: 프로바이더 실패 → 이전 today 유지(단 날짜 가드), 상태페이지 실패 →
  이전 값 유지, enrichment 실패 → 이전 블록/주월 유지. ☑ `ProviderFailureKeepsPreviousTodayValue` 등.
- **I9. 자정 가드**: 스냅샷 date != todayKey 면 합계 제외·계승 금지. 캐리어 스냅샷은 activeBlock 있을 때만.
  ☑ `StaleDatedSnapshotExcluded`, `MidnightCarrierSnapshotFromActiveBlock`.
- **I10. 프로세스 실행 지점 단일화**: 외부 바이너리 실행은 `ProcessRunner` 1곳. stderr tail 로그 필수.
- **I11. 서버 제어 URL 검증**: PokéAPI chain URL(https+pokeapi.co), GitHub html_url(https+github.com) 검증
  후에만 fetch/열기.
- **I12. 델타 유실 금지**: 컴패니언 적립은 라인 미로딩이어도 항상 수행(진화 판정만 연기). claimed 는
  적립과 원자적으로 전진. ☑ `UsageAccruesWhileLineUnloadedThenEvolvesOnLoad`.
- **I13. 결함 대응 프로토콜 계승**: 결함 발견 시 ① 5-whys(테스트가 왜 못 걸렀나 포함) ② 같은 부류 전수
  스윕 ③ 트리거 브랜치 그대로 재현하는 회귀 테스트(`A||B` 면 B 단독도) ④ 기계적 재발 방지(테스트/게이트/문서).
  이 목록에 새 인바리언트를 추가한다.

---

## 12. 테스트 전략

- 프레임워크 xUnit. 원본 21개 테스트 파일 중 **플랫폼 무관 로직 전부 이식** (mac 전용 키체인/셸 마커
  테스트 제외). 우선순위: UsageStoreTests(집계·알림·조합표) → LocalUsageReader/CacheTests(+실 로그 픽스처)
  → RareCandy/Shop/Mint/ShinyCharm/Ditto/CompanionTests(게임) → ModelLogicTests(파서·한도 파생) →
  UpdateChecker/TokenFormatter.
- 픽스처: 원본 테스트(`../Tests/PokeTokenBarTests/`)의 JSONL 문자열을 그대로 복사 (포맷이 곧 계약).
- RNG: `IRng` 주입 + 시드 LCG 로 결정적 테스트 (기대값은 C# 시퀀스 기준으로 재산출 — Swift 와 비트 동일
  불필요, "분모 배수 → 히트" 같은 성질 검증 우선).
- 통합 스모크(수동/CI 옵션): 실사용자 로그 존재 시 전체 파이프라인 1회 → `last-snapshot.json` 산출 검증
  (원본 `LocalUsageParityTests` 대응). 패리티 대조 기준: 맥이 있으면 같은 로그 폴더로 두 앱 비교,
  없으면 **`npx ccusage@latest daily` 의 오늘 합계와 대조** (원본 파서가 ccusage 패리티를 기준으로
  역산·검증된 구현이므로 유효한 기준).
- 성능 목표: 정상상태 refresh ≤ 0.2s / 콜드 전수 파싱은 백그라운드 스레드에서(UI 무블로킹).

---

## 13. 마일스톤 (Codex 실행 계획)

각 마일스톤 = 독립 PR 단위. **DoD: `dotnet test` 그린 + 해당 수동 QA 통과.**

- **M0 — 골격** ✅DoD: 앱 실행 → 트레이에 🥚 아이콘, 클릭 시 빈 플라이아웃, 우클릭 메뉴, 단일 인스턴스,
  settings.json 로드/저장, 로그·크래시 훅, `windows-ci.yml` 그린.
- **M1 — Claude 사용량** ✅DoD: `.claude` 파싱+캐시+오늘/주/월/블록, 홈 탭 숫자, 툴팁 규칙(조합표 테스트),
  자정/전원 이벤트, 새로고침 타이머·수동. 테스트: Reader/Cache/Store 집계·I5·I8·I9.
- **M2 — Codex·Gemini 사용량** ✅DoD: 프로바이더 3종 + 칩 탭 + in/out/cache 분해 + 단가표. 테스트: I2,
  Codex/Gemini 파서.
- **M3 — 공식 한도 + 알림** ✅DoD: Claude oauth/usage(파일 토큰·백오프·authExpired), codex app-server
  (숨김 실행), forecast, stale 배지, 상태페이지 배너, 엣지 트리거 토스트, I3/I4/I7 테스트.
- **M4 — 컴패니언 게임** ✅DoD: 알→부화→진화→졸업→도감 전 사이클 + 샤이니/성격/메타몽 + 사탕/민트/
  부적/상점/가방 + PokéAPI(인덱스·폴백·스프라이트 캐시) + 트레이 아이콘 애니메이션 + 연출 + 컴패니언
  알림. 게임 테스트 스위트 전체 이식. (데모: `PTB_STATE_DIR` + 시드 상태로 QA.)
- **M5 — 마감** ✅DoD: i18n 3개 언어 전 화면, 설정 전 항목, 자동 시작, 업데이트 확인, 문제 제보,
  테마/DPI 검수, 패리티 스냅샷 + §12 패리티 대조(ccusage) 통과.
- **M6 — 배포** ✅DoD: publish 파이프라인, `win-v0.1.0` zip 릴리스, Scoop 버킷, `windows/README.md`
  (+루트 README 에 Windows 안내 1줄은 별도 PR 로 제안만) + 라이선스 고지.

**Codex 운영 팁**: ① 루트 `AGENTS.md` 와 `windows/AGENTS.md` 가 준비돼 있다 — Codex 를 **저장소 루트에서
실행**하면 지침·이 문서·원본 Swift 를 모두 읽을 수 있다 ② 마일스톤당 "windows/PLAN.md §N 을 구현하라,
원본 Swift 파일 X 를 레퍼런스로" 형식으로 지시 ③ 리뷰 시 §11 체크리스트를 그대로 들이댄다 ④ 완료
주장은 `dotnet test` 출력 첨부를 요구한다.

---

## 14. 구현 시 검증 필요 항목 (스펙의 가정 — M1~M3 에서 실측 확인)

| 가정 | 확인 방법 | 어긋나면 |
|---|---|---|
| Windows Claude Code 도 `~/.claude/projects/**/*.jsonl` 에 동일 스키마 기록 | 이 PC 의 실 로그 열기 | 파서 필드 조정 |
| Windows Claude Code 자격증명이 `~/.claude/.credentials.json` (평문) | 파일 존재·스키마 확인 | 대체 위치 탐색 (DPAPI 등) |
| codex CLI Windows 에서 `app-server --stdio` 동작·응답 스키마 동일 | 수동 1회 실행 | JSON-RPC 라인 조정 |
| Gemini CLI Windows 도 `~/.gemini/tmp/**/chats` | 실 로그 확인 | 경로 조정 |
| `Microsoft.Toolkit.Uwp.Notifications` 가 언패키지드 .NET 10 WPF 에서 동작 | M0 스파이크 | H.NotifyIcon 풍선/커스텀 팝업 폴백 |
| H.NotifyIcon 아이콘 고빈도 교체 시 핸들 안정 | M4 장시간 구동 | 프레임 캐시·fps 하향 |
| Volta/nvm 등 정적 경로 정확성 | 각 매니저 문서 | 목록 수정 (PATH 스캔이 주력이라 영향 작음) |

## 15. 부록 — 원본 파일 ↔ 스펙 매핑

원본 경로는 저장소 루트 기준 `Sources/PokeTokenBar/` (windows/ 에서는 `../Sources/PokeTokenBar/`).

| 원본 (Sources/PokeTokenBar/) | 스펙 | 포팅 대상 (Core/App) |
|---|---|---|
| Core/Models.swift | §6.1, §8.1–8.2 | Core/Models/* |
| Core/LocalUsageReader.swift | §6.2–6.6 | Core/Usage/LocalUsageReader |
| Core/LocalUsageCache.swift | §6.5 | Core/Usage/LocalUsageCache |
| Core/LocalUsageProvider.swift | §6, §5 | Core/Usage/Providers |
| Core/UsageStore.swift | §7.1–7.5 | Core/Usage/UsageStore |
| Core/ModelPricing.swift | §7.3 | Core/Usage/ModelPricing |
| Core/OAuthLimitsProvider.swift | §8.1 | Core/Limits/ClaudeLimitsProvider |
| Core/CodexRateLimitsProvider.swift + ProcessRunner.swift | §8.2 | Core/Limits/* + Util/ProcessRunner |
| Core/BinaryLocator.swift | §8.2 | Core/Util/BinaryLocator |
| Core/ProviderStatusChecker.swift | §8.3 | Core/Limits/StatuspageProvider |
| Core/CompanionModel.swift / CompanionStore.swift | §7.6 | Core/Companion/* |
| Core/PokeAPIClient.swift / UI/SpriteLoader.swift | §8.4 | Core/Poke/* |
| Core/UpdateChecker.swift | §8.5 | Core/Util/UpdateChecker |
| Core/TokenFormatter.swift | §7.4 | Core/Util/TokenFormatter |
| Core/Localization.swift | §9.6 | Core/Util/L |
| Core/AppLog.swift / CrashReporter.swift / SupportMail.swift | §10.2 | Core/Util/* |
| Core/LoginItem.swift | §4, F19 | App/Platform/Autostart |
| PokeTokenBarApp.swift (AppDelegate) | §9.2, §7.1 훅 | App/Tray/* |
| UI/PopoverView.swift 외 뷰 | §9.3 | App/Flyout, App/Views |
| UI/SpriteAnimation.swift (GIFDecoder) | §4 GIF | App/Platform/GifDecoder |
| Tests/* | §12 | PokeTokenBar.Tests |
