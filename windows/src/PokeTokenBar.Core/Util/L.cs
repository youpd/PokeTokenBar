using PokeTokenBar.Core.Companion;

namespace PokeTokenBar.Core.Util;

public readonly record struct L(AppLanguage Language)
{
    private string T(string ko, string en, string ja) => Language switch
    {
        AppLanguage.Ko => ko,
        AppLanguage.Ja => ja,
        _ => en,
    };

    public string Home => T("홈", "Home", "ホーム");
    public string Subtitle => T("AI 사용량 컴패니언", "AI usage companion", "AI 使用量コンパニオン");
    public string Shop => T("상점", "Shop", "ショップ");
    public string Bag => T("가방", "Bag", "バッグ");
    public string Collection => T("컬렉션", "Collection", "コレクション");
    public string Today => T("오늘", "Today", "今日");
    public string Cost => T("API 환산 예상비용", "API-equivalent estimate", "API換算見積");
    public string Week => T("이번 주", "This week", "今週");
    public string Month => T("이번 달", "This month", "今月");
    public string Providers => T("프로바이더", "Providers", "プロバイダー");
    public string Input => T("입력", "Input", "入力");
    public string Output => T("출력", "Output", "出力");
    public string CacheWrite => T("캐시 생성", "Cache write", "キャッシュ作成");
    public string CacheRead => T("캐시 읽기", "Cache read", "キャッシュ読込");
    public string CurrentBlock => T("선택 프로바이더 5시간 블록", "Selected provider 5h block", "選択プロバイダーの5時間ブロック");
    public string NoActiveBlock => T("활성 블록 없음", "No active block", "アクティブなブロックなし");
    public string Refreshing => T("사용량 불러오는 중…", "Loading usage…", "使用量を読み込み中…");
    public string RefreshNow => T("지금 새로고침", "Refresh now", "今すぐ更新");
    public string Open => T("열기", "Open", "開く");
    public string Settings => T("설정", "Settings", "設定");
    public string Quit => T("종료", "Quit", "終了");
    public string OfficialLimits => T("공식 한도", "Official limits", "公式上限");
    public string Reload => T("다시 읽기", "Reload", "再読込");
    public string Wallet => T("사용 가능한 토큰", "Available tokens", "利用可能なトークン");
    public string Buy => T("구매", "Buy", "購入");
    public string Use => T("사용", "Use", "使う");
    public string Active => T("적용 중", "Active", "適用中");
    public string EmptyBag => T("가방이 비어 있습니다.", "Your bag is empty.", "バッグは空です。");
    public string EmptyDex => T("포켓몬을 졸업시키면 이곳에 보존됩니다.", "Graduate a companion to add it here.", "コンパニオンを卒業させるとここに保存されます。");
    public string NoProvider => T("프로바이더 없음", "No provider", "プロバイダーなし");
    public string PartialReadError => T("일부 로그를 읽지 못했습니다. 이전 값을 유지합니다.", "Some logs could not be read. Previous values are kept.", "一部のログを読み込めませんでした。以前の値を維持します。");
    public string TokenEgg => T("토큰 알", "Token Egg", "トークンエッグ");
    public string TokensPrice(long amount) => T(
        $"{TokenFormatter.Compact(amount)} 토큰",
        $"{TokenFormatter.Compact(amount)} tokens",
        $"{TokenFormatter.Compact(amount)} トークン");
    public string TodayProvider(long amount, double cost) => T(
        $"오늘 {TokenFormatter.Compact(amount)} · {TokenFormatter.Cost(cost)}",
        $"Today {TokenFormatter.Compact(amount)} · {TokenFormatter.Cost(cost)}",
        $"今日 {TokenFormatter.Compact(amount)} · {TokenFormatter.Cost(cost)}");
    public string CodexTodayProvider(long amount, double cost) => T(
        $"오늘 {TokenFormatter.Compact(amount)} · API 환산 예상비용 {TokenFormatter.Cost(cost)} (구독제 상태)",
        $"Today {TokenFormatter.Compact(amount)} · API estimate {TokenFormatter.Cost(cost)} (subscription plan)",
        $"今日 {TokenFormatter.Compact(amount)} · API換算見積 {TokenFormatter.Cost(cost)}（サブスク利用）");
    public string PerMinute(long amount) => T(
        $"{TokenFormatter.Compact(amount)}/분",
        $"{TokenFormatter.Compact(amount)}/min",
        $"{TokenFormatter.Compact(amount)}/分");
    public string CollectionSummary(int count) => T(
        $"컬렉션 {count}", $"Collection {count}", $"コレクション {count}");
    public string Graduated(string name) => T(
        $"{name} 졸업!", $"{name} graduated!", $"{name} が卒業しました！");
    public string Evolved(string name) => T(
        $"{name}(으)로 진화!", $"Evolved into {name}!", $"{name} に進化しました！");
    public string UpdateAvailable(string version) => T(
        $"Windows v{version} 업데이트가 있습니다.",
        $"Windows v{version} is available.",
        $"Windows v{version} を利用できます。");
    public string ApplyUpdate => T("업데이트", "Update", "更新");
    public string SkipUpdate => T("건너뛰기", "Skip", "スキップ");
    public string ClaudeAuthExpired => T(
        "Claude Code를 실행하면 인증이 자동 갱신됩니다.",
        "Run Claude Code to renew authentication.",
        "Claude Code を実行すると認証が更新されます。");
    public string LimitsUnavailable => T(
        "공식 한도를 불러오지 못했습니다.",
        "Official limits are unavailable.",
        "公式上限を読み込めませんでした。");
    public string LimitsStale => T(
        "15분 이상 지난 한도 정보입니다.",
        "Limit information is more than 15 minutes old.",
        "15分以上前の上限情報です。");
    public string FiveHours => T("5시간", "5 hours", "5時間");
    public string Weekly => T("주간", "Weekly", "週間");
    public string WeeklyOpus => T("주간 Opus", "Weekly Opus", "週間 Opus");
    public string WeeklySonnet => T("주간 Sonnet", "Weekly Sonnet", "週間 Sonnet");
    public string Primary => T("기본", "Primary", "プライマリ");
    public string Secondary => T("보조", "Secondary", "セカンダリ");
    public string Individual => T("개인", "Individual", "個人");
    public string ForecastAt(DateTimeOffset time) => T(
        $"현재 속도면 {time.ToLocalTime():HH:mm}에 소진 예상",
        $"Estimated depletion at {time.ToLocalTime():HH:mm}",
        $"現在のペースでは {time.ToLocalTime():HH:mm} に消費見込み");
    public string ForecastAfterReset => T(
        "현재 속도면 리셋 전 소진되지 않음",
        "Not expected to deplete before reset",
        "リセット前に消費しない見込みです");
    public string LimitWarning => T("한도 경고", "Limit warning", "上限警告");
    public string LimitCritical => T("한도 위험", "Limit critical", "上限危険");
    public string ToHatch(long amount) => T(
        $"부화까지 {TokenFormatter.Compact(amount)} 토큰",
        $"{TokenFormatter.Compact(amount)} tokens to hatch",
        $"孵化まで {TokenFormatter.Compact(amount)} トークン");
    public string EggStatus => T("알에서 자라는 중", "Growing inside the egg", "タマゴの中で成長中");
    public string Working => T("함께 작업 중", "Working with you", "一緒に作業中");
    public string Focus => T("집중 모드!", "Deep focus!", "集中モード！");
    public string Tired => T("한도 전에 잠깐 쉬어요", "Rest before the limit", "上限前に少し休みましょう");
    public string Sleeping => T("자는 중", "Sleeping", "睡眠中");
    public string Ready => T("준비 완료", "Ready", "準備完了");
    public string LevelUp => T("성장했어요!", "Leveled up!", "成長しました！");
    public string Updated(DateTimeOffset time) => T(
        $"마지막 갱신 {time.ToLocalTime():HH:mm:ss}",
        $"Updated {time.ToLocalTime():HH:mm:ss}",
        $"更新 {time.ToLocalTime():HH:mm:ss}");
    public string Incident(string provider, string description) => T(
        $"{provider} 상태: {description}",
        $"{provider} status: {description}",
        $"{provider} 状態: {description}");
    public string LanguageName(AppLanguage language) => language switch
    {
        AppLanguage.Ko => "한국어",
        AppLanguage.Ja => "日本語",
        _ => "English",
    };
    public string Rarity(Rarity rarity) => rarity switch
    {
        PokeTokenBar.Core.Companion.Rarity.Common => T("일반", "Common", "ノーマル"),
        PokeTokenBar.Core.Companion.Rarity.Uncommon => T("고급", "Uncommon", "アンコモン"),
        PokeTokenBar.Core.Companion.Rarity.Rare => T("희귀", "Rare", "レア"),
        _ => T("전설", "Legendary", "伝説"),
    };
    public string Item(ItemKind kind) => kind switch
    {
        ItemKind.RareCandy => T("이상한 사탕", "Rare Candy", "ふしぎなアメ"),
        ItemKind.Mint => T("민트", "Mint", "ミント"),
        _ => T("이로치 부적", "Shiny Charm", "ひかるおまもり"),
    };
}
