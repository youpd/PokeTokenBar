# PokeTokenBar — Claude 프로젝트 지침

## 릴리스 (자연어 트리거)

사용자가 **버전 배포를 자연어로 요청**하면 — 예: "배포해줘", "릴리스 올려줘", "패치 배포",
"2.1.1 배포", "release", "다음 버전 내줘" — 한 줄 명령을 시키지 말고 아래를 직접 수행한다.

1. **문서 일관성 검토**: `./scripts/release.sh --check-only` 실행. 경고(README/랜딩/cask 의
   stale 버전·제거된 의존성, **UI 변경 시 스크린샷 stale** 등)가 있으면 **먼저 문서를 갱신**한다 —
   README.md/ko/ja, gh-pages 랜딩 `index.html`(3개 언어 i18n 사전 정합 유지), homebrew-tap cask caveat.
   **UI(`Sources/PokeTokenBar/UI/`)를 바꿨으면 `assets/` 스크린샷을 재생성**한다 — 언어별 이미지
   (`settings.png`/`-ko`/`-ja` 등) 각 README 참조. 팝오버 라이브 캡처가 막히면(Keychain 프롬프트·
   NSPopover 창 미노출) 실제 UI 를 HTML 로 렌더해 반영. (`RELEASE.md` 체크리스트)
2. **버전 결정** (2026-07-03 확정 규칙): 사용자가 말한 **단어가 곧 세그먼트 지정 명령**이다 —
   릴리스에 기능이 포함돼 있어도 변경 내용으로 재해석하지 않는다.
   - "**패치**(해줘)" → x.y.**Z+1** / "**마이너**" → x.**Y+1**.0 / "**메이저**" → **X+1**.0.0
   - 버전을 직접 명시하면("2.4.1 배포") 그 값 그대로.
   - 세그먼트 지정 없이 "배포/릴리스"만 말하면: 변경 성격 기준으로 제안 후 확인받고 진행.
3. **릴리스 노트 작성** 후 실행 (반드시 `main` 브랜치에서):
   ```bash
   # 직전 릴리스 이후 변경을 요약해 노트 파일 작성
   PTB_NOTES_FILE=/tmp/ptb-notes.md ./scripts/release.sh <version>
   ```
   스크립트가 test-gate → 문서검토 → 범프 → 빌드검증 → 커밋·push → GitHub Release → cask → Pages 를 순서대로 수행.
4. **검증**: 완료 후 `brew upgrade --cask poke-token-bar` 로 실제 업그레이드 동작 확인.

릴리스는 외부 공개(비가역)이므로 실행 직전 **적용할 버전과 노트 요약을 한 번 보여준 뒤** 진행한다.
세부 절차·체크리스트는 `RELEASE.md` 참고.

## 확장 규약 (새 프로바이더/툴 추가 시 — 리뷰에서 위반 확인)

새 AI CLI(사용량 소스)·버전매니저를 더할 때 특정 플랫폼에 종속된 분기를 만들지 않는다.
아래는 절차이며, 코드 리뷰 시 이 규약 위반을 결함으로 본다.

- **사용량 소스 추가** = `UsageProvider` 프로토콜(`Core/UsageProvider.swift`) 구현체 1개 작성 +
  `UsageStore.init` 의 기본 `providers:` 배열(`Core/UsageStore.swift`)에 등록. 이 두 곳이 유일한 손댈 지점.
- **범용 동작은 프로바이더 무관하게 집계**: 오늘/주/월 합계·burn tier·companion 리듬은 전 프로바이더
  합산이어야 한다(`snapshots` reduce). 한 프로바이더에만 계산을 붙이지 마라(과거 회귀: burn 이 Claude
  블록만 관측 → Codex/Gemini 전용 사용자 companion 이 항상 idle). 패리티 테스트가 이를 강제한다
  (`UsageStoreTests` 의 "unknown provider" 계열).
- **프로바이더 고유 동작만 `providerID` 로 명시 분기**: 공식 한도(Claude=HTTP·Codex=프로세스),
  5h forecast·"현재 블록" 행처럼 *특정 프로바이더에만 존재하는* 기능만 id 로 조건 분기한다.
  범용 경로에 `== "claude_code"` 류 리터럴 분기를 추가하는 건 금지.
- **버전매니저/설치경로 추가** = `BinaryLocator.commonToolDirectories()` 한 곳에만 추가한다
  (탐색·자식 프로세스 PATH 보강이 이 단일 소스를 공유).
