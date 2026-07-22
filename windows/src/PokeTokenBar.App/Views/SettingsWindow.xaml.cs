using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using PokeTokenBar.App.Platform;
using PokeTokenBar.Core.Companion;
using PokeTokenBar.Core.Models;
using PokeTokenBar.Core.Util;

namespace PokeTokenBar.App.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly SettingsStore _settingsStore;
    private readonly CompanionStore _companionStore;
    private readonly UpdateChecker _updateChecker;
    private readonly string _version;
    private readonly Func<Task>? _reloadClaudeLimits;

    public event EventHandler? Saved;

    public SettingsWindow(
        AppSettings settings,
        SettingsStore settingsStore,
        CompanionStore companionStore,
        UpdateChecker updateChecker,
        string version,
        Func<Task>? reloadClaudeLimits = null)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _companionStore = companionStore;
        _updateChecker = updateChecker;
        _version = version;
        _reloadClaudeLimits = reloadClaudeLimits;
        InitializeComponent();
        LoadValues();
        ApplyLanguage();
        LanguageComboBox.SelectionChanged += (_, _) => ApplyLanguage();
    }

    private AppLanguage SelectedLanguage =>
        LanguageComboBox.SelectedItem is ComboBoxItem { Tag: string tag }
            ? tag switch { "ko" => AppLanguage.Ko, "ja" => AppLanguage.Ja, _ => AppLanguage.En }
            : _companionStore.State.Language;

    private void LoadValues()
    {
        Select(LanguageComboBox, _companionStore.State.Language.ToString().ToLowerInvariant());
        Select(RefreshIntervalComboBox, _settings.RefreshInterval.ToString());
        Select(NumericTrayIconComboBox, _settings.NumericTrayIcon ?? string.Empty);
        AutoStartCheckBox.IsChecked = AutostartManager.IsEnabled;
        ShowTokensCheckBox.IsChecked = _settings.ShowTokensInMenu;
        ShowCostCheckBox.IsChecked = _settings.ShowCostInMenu;
        ShowLimitCheckBox.IsChecked = _settings.ShowLimitInMenu;
        LimitNotificationsCheckBox.IsChecked = _settings.LimitNotifications;
        CompanionNotificationsCheckBox.IsChecked = _settings.CompanionNotifications;
        StatusChecksCheckBox.IsChecked = _settings.StatusChecksEnabled;
        UpdateNotificationsCheckBox.IsChecked = _settings.UpdateNotificationsEnabled;
        DisableClaudeLimitsCheckBox.IsChecked = _settings.ClaudeLimitsDisabled;
        WarnThresholdTextBox.Text = _settings.WarnThreshold.ToString();
        CritThresholdTextBox.Text = _settings.CritThreshold.ToString();
        CodexPathTextBox.Text = _settings.CodexPath ?? string.Empty;
        ExtraHomesTextBox.Text = string.Join(Environment.NewLine, _settings.ExtraHomes ?? []);
        VersionText.Text = $"PokeTokenBar v{_version} · Windows {Environment.OSVersion.Version}";
    }

    private void ApplyLanguage()
    {
        var language = SelectedLanguage;
        var text = new L(language);
        Title = $"PokeTokenBar · {text.Settings}";
        TitleText.Text = text.Settings;
        GeneralHeader.Text = language switch { AppLanguage.Ko => "일반", AppLanguage.Ja => "一般", _ => "General" };
        LanguageLabel.Text = language switch { AppLanguage.Ko => "언어", AppLanguage.Ja => "言語", _ => "Language" };
        RefreshLabel.Text = language switch { AppLanguage.Ko => "새로고침 간격", AppLanguage.Ja => "更新間隔", _ => "Refresh interval" };
        SetItemText(RefreshIntervalComboBox, 0, language switch { AppLanguage.Ko => "수동", AppLanguage.Ja => "手動", _ => "Manual" });
        SetItemText(RefreshIntervalComboBox, 1, language switch { AppLanguage.Ko => "1분", AppLanguage.Ja => "1分", _ => "1 min" });
        SetItemText(RefreshIntervalComboBox, 2, language switch { AppLanguage.Ko => "2분", AppLanguage.Ja => "2分", _ => "2 min" });
        SetItemText(RefreshIntervalComboBox, 3, language switch { AppLanguage.Ko => "5분", AppLanguage.Ja => "5分", _ => "5 min" });
        SetItemText(RefreshIntervalComboBox, 4, language switch { AppLanguage.Ko => "15분", AppLanguage.Ja => "15分", _ => "15 min" });
        AutoStartCheckBox.Content = language switch { AppLanguage.Ko => "로그인 시 자동 시작", AppLanguage.Ja => "ログイン時に自動起動", _ => "Launch at login" };
        TrayHeader.Text = language switch { AppLanguage.Ko => "트레이 표시", AppLanguage.Ja => "トレイ表示", _ => "Tray display" };
        ShowTokensCheckBox.Content = language switch { AppLanguage.Ko => "오늘 토큰", AppLanguage.Ja => "本日のトークン", _ => "Today's tokens" };
        ShowCostCheckBox.Content = language switch { AppLanguage.Ko => "오늘 비용", AppLanguage.Ja => "本日のコスト", _ => "Today's cost" };
        ShowLimitCheckBox.Content = language switch { AppLanguage.Ko => "한도 %", AppLanguage.Ja => "上限 %", _ => "Limit %" };
        NumericIconLabel.Text = language switch { AppLanguage.Ko => "숫자 트레이 아이콘", AppLanguage.Ja => "数値トレイアイコン", _ => "Numeric tray icon" };
        SetItemText(NumericTrayIconComboBox, 0, language switch { AppLanguage.Ko => "캐릭터", AppLanguage.Ja => "キャラクター", _ => "Character" });
        SetItemText(NumericTrayIconComboBox, 1, language switch { AppLanguage.Ko => "토큰", AppLanguage.Ja => "トークン", _ => "Tokens" });
        SetItemText(NumericTrayIconComboBox, 2, language switch { AppLanguage.Ko => "비용", AppLanguage.Ja => "コスト", _ => "Cost" });
        SetItemText(NumericTrayIconComboBox, 3, language switch { AppLanguage.Ko => "한도 %", AppLanguage.Ja => "上限 %", _ => "Limit %" });
        NotificationsHeader.Text = language switch { AppLanguage.Ko => "알림", AppLanguage.Ja => "通知", _ => "Notifications" };
        LimitNotificationsCheckBox.Content = language switch { AppLanguage.Ko => "한도 알림", AppLanguage.Ja => "上限通知", _ => "Limit alerts" };
        CompanionNotificationsCheckBox.Content = language switch { AppLanguage.Ko => "컴패니언 이벤트 알림", AppLanguage.Ja => "コンパニオンイベント通知", _ => "Companion event notifications" };
        StatusChecksCheckBox.Content = language switch { AppLanguage.Ko => "프로바이더 상태 확인", AppLanguage.Ja => "プロバイダー状態チェック", _ => "Provider status checks" };
        UpdateNotificationsCheckBox.Content = language switch { AppLanguage.Ko => "업데이트 배너 표시", AppLanguage.Ja => "更新バナーを表示", _ => "Show update banner" };
        WarnLabel.Text = language switch { AppLanguage.Ko => "경고 %", AppLanguage.Ja => "警告 %", _ => "Warning %" };
        CriticalLabel.Text = language switch { AppLanguage.Ko => "위험 %", AppLanguage.Ja => "危険 %", _ => "Critical %" };
        AdvancedHeader.Text = language switch { AppLanguage.Ko => "고급", AppLanguage.Ja => "詳細", _ => "Advanced" };
        DisableClaudeLimitsCheckBox.Content = language switch { AppLanguage.Ko => "Claude 공식 한도 조회 끄기", AppLanguage.Ja => "Claude 公式上限を無効化", _ => "Disable Claude official limits" };
        CodexPathLabel.Text = language switch { AppLanguage.Ko => "Codex 실행 파일(선택)", AppLanguage.Ja => "Codex 実行ファイル（任意）", _ => "Codex executable (optional)" };
        ExtraHomesLabel.Text = language switch { AppLanguage.Ko => "추가 홈 디렉터리(한 줄에 하나)", AppLanguage.Ja => "追加ホーム（1行に1件）", _ => "Extra home directories (one per line)" };
        ReloadClaudeLimitsButton.Content = language switch { AppLanguage.Ko => "Claude 토큰 다시 읽기", AppLanguage.Ja => "Claude トークンを再読込", _ => "Reload Claude credential" };
        AggregationNoteText.Text = language switch { AppLanguage.Ko => "집계 기준: input + output + cache, 로컬 날짜.", AppLanguage.Ja => "集計基準: input + output + cache、ローカル日付。", _ => "Token basis: input + output + cache, local date." };
        UpdatesHeader.Text = language switch { AppLanguage.Ko => "업데이트", AppLanguage.Ja => "アップデート", _ => "Updates" };
        CheckUpdatesButton.Content = language switch { AppLanguage.Ko => "지금 확인", AppLanguage.Ja => "今すぐ確認", _ => "Check now" };
        SupportHeader.Text = language switch { AppLanguage.Ko => "정보 & 지원", AppLanguage.Ja => "情報とサポート", _ => "About & Support" };
        ReportProblemButton.Content = language switch { AppLanguage.Ko => "문제점 알리기", AppLanguage.Ja => "問題を報告", _ => "Report a problem" };
        ShowLogsButton.Content = language switch { AppLanguage.Ko => "로그 보기", AppLanguage.Ja => "ログを表示", _ => "Show logs" };
        CancelButton.Content = language switch { AppLanguage.Ko => "취소", AppLanguage.Ja => "キャンセル", _ => "Cancel" };
        SaveButton.Content = language switch { AppLanguage.Ko => "저장", AppLanguage.Ja => "保存", _ => "Save" };
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        _settings.ShowTokensInMenu = ShowTokensCheckBox.IsChecked == true;
        _settings.ShowCostInMenu = ShowCostCheckBox.IsChecked == true;
        _settings.ShowLimitInMenu = ShowLimitCheckBox.IsChecked == true;
        _settings.LimitNotifications = LimitNotificationsCheckBox.IsChecked == true;
        _settings.CompanionNotifications = CompanionNotificationsCheckBox.IsChecked == true;
        _settings.StatusChecksEnabled = StatusChecksCheckBox.IsChecked == true;
        _settings.UpdateNotificationsEnabled = UpdateNotificationsCheckBox.IsChecked == true;
        _settings.ClaudeLimitsDisabled = DisableClaudeLimitsCheckBox.IsChecked == true;
        _settings.WarnThreshold = ReadThreshold(WarnThresholdTextBox.Text, 50, 95, 80);
        _settings.CritThreshold = ReadThreshold(CritThresholdTextBox.Text, 80, 100, 95);
        _settings.CodexPath = EmptyToNull(CodexPathTextBox.Text);
        _settings.ExtraHomes = ExtraHomesTextBox.Text
            .Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _settings.RefreshInterval = SelectedInt(RefreshIntervalComboBox, 120);
        _settings.NumericTrayIcon = SelectedTag(NumericTrayIconComboBox) is { Length: > 0 } icon ? icon : null;
        try
        {
            AutostartManager.SetEnabled(AutoStartCheckBox.IsChecked == true);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _companionStore.SetLanguage(SelectedLanguage);
        _settingsStore.Save(_settings);
        Saved?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private async void ReloadClaudeLimitsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_reloadClaudeLimits is not null) await _reloadClaudeLimits();
    }

    private async void CheckUpdatesButton_OnClick(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        var update = await _updateChecker.CheckAsync(
            _settings.SkippedUpdateVersion,
            TimeSpan.Zero);
        UpdateResultText.Text = update is null
            ? _updateChecker.LastError is null
                ? SelectedLanguage switch { AppLanguage.Ko => "최신 버전입니다", AppLanguage.Ja => "最新版です", _ => "Up to date" }
                : SelectedLanguage switch { AppLanguage.Ko => "확인 실패", AppLanguage.Ja => "確認に失敗", _ => "Check failed" }
            : new L(SelectedLanguage).UpdateAvailable(update.Version);
        CheckUpdatesButton.IsEnabled = true;
    }

    private void ReportProblemButton_OnClick(object sender, RoutedEventArgs e) =>
        OpenUri(SupportMail.Build(_version, RuntimeInformation.OSDescription));

    private void ShowLogsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var directory = Path.GetDirectoryName(AppPaths.Default.LogFile)!;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", directory)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            AppLog.Write($"open logs failed: {exception.Message}");
        }
    }

    private void GitHubButton_OnClick(object sender, RoutedEventArgs e) =>
        OpenUri(new Uri("https://github.com/chattymin/PokeTokenBar"));

    private void SponsorButton_OnClick(object sender, RoutedEventArgs e) =>
        OpenUri(new Uri("https://github.com/sponsors/chattymin"));

    private static void OpenUri(Uri uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppLog.Write($"open URI failed: {exception.Message}");
        }
    }

    private static void Select(ComboBox comboBox, string tag) => comboBox.SelectedItem =
        comboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
        ?? comboBox.Items[0];

    private static void SetItemText(ComboBox comboBox, int index, string value)
    {
        if (comboBox.Items[index] is ComboBoxItem item) item.Content = value;
    }

    private static int SelectedInt(ComboBox comboBox, int fallback) =>
        int.TryParse(SelectedTag(comboBox), out var value) ? value : fallback;

    private static string? SelectedTag(ComboBox comboBox) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();

    private static string? EmptyToNull(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int ReadThreshold(string value, int min, int max, int fallback) =>
        int.TryParse(value, out var parsed) ? Math.Clamp(parsed, min, max) : fallback;

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => Close();
}
