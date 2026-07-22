using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PokeTokenBar.Core.Models;
using PokeTokenBar.Core.Usage;
using PokeTokenBar.Core.Util;

namespace PokeTokenBar.App.Views;

public partial class FlyoutWindow : Window
{
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(244, 133, 133));
    private static readonly Brush SelectedChipBrush = new SolidColorBrush(Color.FromRgb(55, 105, 80));
    private static readonly Brush ChipBrush = new SolidColorBrush(Color.FromRgb(42, 46, 54));
    private bool _isShuttingDown;
    private bool _keepOpenWhenDeactivated;
    private string? _selectedProviderId;
    private UsageStore? _lastStore;

    public FlyoutWindow()
    {
        InitializeComponent();
        Deactivated += (_, _) =>
        {
            if (!_keepOpenWhenDeactivated)
            {
                Hide();
            }
        };
    }

    public Func<Task>? RefreshRequested { get; set; }

    public Func<Task>? ClaudeLimitsRefreshRequested { get; set; }

    public void UpdateDisplay(UsageStore store)
    {
        _lastStore = store;
        TodayTokensText.Text = store.LastUpdated is null
            ? "—"
            : TokenFormatter.Grouped(store.TodayTotalTokens);
        TodayCostText.Text = store.LastUpdated is null
            ? "—"
            : TokenFormatter.Cost(store.TodayCostTotal);
        WeekTokensText.Text = store.LastUpdated is null
            ? "—"
            : TokenFormatter.Grouped(store.WeekTotalTokens);
        MonthTokensText.Text = store.LastUpdated is null
            ? "—"
            : TokenFormatter.Grouped(store.MonthTotalTokens);

        UpdateProviderChips(store);
        var selected = store.SnapshotPreferring(_selectedProviderId);
        _selectedProviderId = selected?.ProviderId;
        UpdateProviderDetails(selected);
        UpdateStatusBanner(store);
        UpdateLimits(store, selected);

        var block = selected?.ActiveBlock;
        BlockTokensText.Text = block is null
            ? "활성 블록 없음"
            : TokenFormatter.Grouped(block.TotalTokens);
        BurnRateText.Text = block is null
            ? string.Empty
            : $"{TokenFormatter.Compact((long)block.TokensPerMinute)}/분";

        RefreshButton.IsEnabled = !store.IsRefreshing;
        if (!string.IsNullOrWhiteSpace(store.LastErrorDescription))
        {
            RefreshStatusText.Foreground = ErrorBrush;
            RefreshStatusText.Text = "일부 로그를 읽지 못했습니다. 이전 값을 유지합니다.";
        }
        else if (store.LastUpdated is { } updated)
        {
            RefreshStatusText.Foreground = (Brush)FindResource("SecondaryTextBrush");
            RefreshStatusText.Text = $"마지막 갱신 {updated.ToLocalTime():HH:mm:ss}";
        }
        else
        {
            RefreshStatusText.Foreground = (Brush)FindResource("SecondaryTextBrush");
            RefreshStatusText.Text = "사용량 불러오는 중…";
        }
    }

    private void UpdateProviderChips(UsageStore store)
    {
        ProviderChipsContainer.Visibility = store.Snapshots.Count >= 2
            ? Visibility.Visible
            : Visibility.Collapsed;
        ProviderChipsPanel.Children.Clear();
        if (store.Snapshots.Count < 2)
        {
            return;
        }

        var selected = store.SnapshotPreferring(_selectedProviderId);
        foreach (var snapshot in store.Snapshots)
        {
            var isSelected = snapshot.ProviderId == selected?.ProviderId;
            var button = new Button
            {
                Tag = snapshot.ProviderId,
                Content = snapshot.DisplayName,
                Margin = new Thickness(0, 0, 7, 0),
                Padding = new Thickness(12, 5, 12, 5),
                Background = isSelected ? SelectedChipBrush : ChipBrush,
                BorderBrush = isSelected
                    ? (Brush)FindResource("AccentBrush")
                    : new SolidColorBrush(Color.FromRgb(75, 85, 101)),
                BorderThickness = new Thickness(1),
                Foreground = (Brush)FindResource("PrimaryTextBrush"),
                FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal,
            };
            button.Click += ProviderChip_OnClick;
            ProviderChipsPanel.Children.Add(button);
        }
    }

    private void UpdateProviderDetails(ProviderSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            SelectedProviderNameText.Text = "프로바이더 없음";
            SelectedProviderTodayText.Text = "—";
            SetTokenBreakdown(null);
            return;
        }

        var today = snapshot.Today;
        SelectedProviderNameText.Text = snapshot.DisplayName;
        SelectedProviderTodayText.Text = today is null
            ? "오늘 0"
            : $"오늘 {TokenFormatter.Compact(today.TotalTokens)} · {TokenFormatter.Cost(today.TotalCost)}";
        SetTokenBreakdown(today);
    }

    private void UpdateStatusBanner(UsageStore store)
    {
        var incident = store.ProviderStatuses.Values
            .Where(status => status.IsIncident)
            .OrderByDescending(status => status.Indicator)
            .FirstOrDefault();
        StatusBanner.Visibility = incident is null ? Visibility.Collapsed : Visibility.Visible;
        if (incident is not null)
        {
            var provider = incident.ProviderId == "claude_code" ? "Claude" : "Codex";
            StatusBannerText.Text = $"{provider} 상태: {incident.Description}";
        }
    }

    private void UpdateLimits(UsageStore store, ProviderSnapshot? selected)
    {
        LimitRowsPanel.Children.Clear();
        ReloadClaudeLimitsButton.Visibility = Visibility.Collapsed;
        LimitSectionMessage.Visibility = Visibility.Collapsed;
        var providerId = selected?.ProviderId;
        if (providerId == "claude_code")
        {
            LimitSectionBorder.Visibility = Visibility.Visible;
            var plan = store.ClaudeLimits?.PlanDisplay;
            LimitSectionTitle.Text = string.IsNullOrWhiteSpace(plan)
                ? "Claude 공식 한도"
                : $"Claude · {plan}";
            if (store.ClaudeLimitsAuthExpired)
            {
                LimitSectionMessage.Text = "Claude Code를 실행하면 인증이 자동 갱신됩니다.";
                LimitSectionMessage.Visibility = Visibility.Visible;
                ReloadClaudeLimitsButton.Visibility = Visibility.Visible;
                return;
            }

            var limits = store.ClaudeLimits;
            AddLimitRow("5시간", limits?.FiveHour?.Utilization, limits?.FiveHour?.ResetDate);
            AddLimitRow("주간", limits?.SevenDay?.Utilization, limits?.SevenDay?.ResetDate);
            AddLimitRow("주간 Opus", limits?.SevenDayOpus?.Utilization, limits?.SevenDayOpus?.ResetDate);
            AddLimitRow("주간 Sonnet", limits?.SevenDaySonnet?.Utilization, limits?.SevenDaySonnet?.ResetDate);
            foreach (var entry in limits?.ScopedLimitEntries ?? [])
            {
                AddLimitRow(
                    entry.DisplayName,
                    entry.Percent,
                    DateTimeOffset.TryParse(entry.ResetsAt, out var reset) ? reset : null);
            }

            if (LimitRowsPanel.Children.Count == 0)
            {
                LimitSectionMessage.Text = "공식 한도를 불러오지 못했습니다.";
                LimitSectionMessage.Visibility = Visibility.Visible;
                ReloadClaudeLimitsButton.Visibility = Visibility.Visible;
            }
            else if (store.AreClaudeLimitsStale)
            {
                LimitSectionMessage.Text = "15분 이상 지난 한도 정보입니다.";
                LimitSectionMessage.Visibility = Visibility.Visible;
            }

            if (store.FiveHourForecast is { } forecast)
            {
                var forecastText = forecast.BeforeReset
                    ? $"현재 속도면 {forecast.DepletionDate.ToLocalTime():HH:mm}에 소진 예상"
                    : "현재 속도면 리셋 전 소진되지 않음";
                LimitSectionMessage.Text = forecastText;
                LimitSectionMessage.Visibility = Visibility.Visible;
            }

            return;
        }

        if (providerId == "codex" && store.CodexLimits is { } codex)
        {
            LimitSectionBorder.Visibility = Visibility.Visible;
            LimitSectionTitle.Text = "Codex 공식 한도";
            foreach (var snapshot in codex.Snapshots.Where(snapshot => snapshot.IsVisible))
            {
                var prefix = codex.Snapshots.Count > 1 ? snapshot.DisplayName + " " : string.Empty;
                AddLimitRow(prefix + "기본", snapshot.Primary?.UsedPercent, snapshot.Primary?.ResetDate);
                AddLimitRow(prefix + "보조", snapshot.Secondary?.UsedPercent, snapshot.Secondary?.ResetDate);
                AddLimitRow(prefix + "개인", snapshot.IndividualLimit?.UsedPercent, snapshot.IndividualLimit?.ResetDate);
            }

            if (store.AreCodexLimitsStale)
            {
                LimitSectionMessage.Text = "15분 이상 지난 한도 정보입니다.";
                LimitSectionMessage.Visibility = Visibility.Visible;
            }

            return;
        }

        LimitSectionBorder.Visibility = Visibility.Collapsed;
    }

    private void AddLimitRow(string name, double? utilization, DateTimeOffset? reset)
    {
        if (utilization is null)
        {
            return;
        }

        var row = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        var label = new Grid();
        label.ColumnDefinitions.Add(new ColumnDefinition());
        label.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        label.Children.Add(new TextBlock
        {
            Text = name,
            Foreground = (Brush)FindResource("SecondaryTextBrush"),
        });
        var value = new TextBlock
        {
            Text = reset is null
                ? TokenFormatter.Percent(utilization.Value)
                : $"{TokenFormatter.Percent(utilization.Value)} · {reset.Value.ToLocalTime():M/d HH:mm}",
            Foreground = utilization >= 95
                ? ErrorBrush
                : utilization >= 80
                    ? Brushes.Goldenrod
                    : (Brush)FindResource("PrimaryTextBrush"),
        };
        Grid.SetColumn(value, 1);
        label.Children.Add(value);
        row.Children.Add(label);
        row.Children.Add(new ProgressBar
        {
            Margin = new Thickness(0, 3, 0, 0),
            Height = 4,
            Minimum = 0,
            Maximum = 100,
            Value = Math.Clamp(utilization.Value, 0, 100),
        });
        LimitRowsPanel.Children.Add(row);
    }

    private void SetTokenBreakdown(DailyUsage? usage)
    {
        SetTokenValue(InputTokensText, usage?.InputTokens);
        SetTokenValue(OutputTokensText, usage?.OutputTokens);
        SetTokenValue(CacheWriteTokensText, usage?.CacheCreationTokens);
        SetTokenValue(CacheReadTokensText, usage?.CacheReadTokens);
    }

    private static void SetTokenValue(TextBlock textBlock, long? value)
    {
        textBlock.Text = value is null ? "—" : TokenFormatter.Compact(value.Value);
        textBlock.ToolTip = value is null ? null : TokenFormatter.Grouped(value.Value);
    }

    private void ProviderChip_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string providerId } && _lastStore is not null)
        {
            _selectedProviderId = providerId;
            UpdateDisplay(_lastStore);
        }
    }

    public void ShowNear(Point trayPosition, bool keepOpenWhenDeactivated = false)
    {
        _keepOpenWhenDeactivated = keepOpenWhenDeactivated;
        var workArea = SystemParameters.WorkArea;
        var targetLeft = trayPosition.X - (Width / 2);
        var targetTop = trayPosition.Y > workArea.Top + (workArea.Height / 2)
            ? trayPosition.Y - Height - 8
            : trayPosition.Y + 8;

        Left = Math.Clamp(targetLeft, workArea.Left + 8, workArea.Right - Width - 8);
        Top = Math.Clamp(targetTop, workArea.Top + 8, workArea.Bottom - Height - 8);

        Show();
        Activate();
    }

    public void CloseForShutdown()
    {
        _isShuttingDown = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isShuttingDown)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    private async void RefreshButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (RefreshRequested is null)
        {
            return;
        }

        RefreshButton.IsEnabled = false;
        RefreshStatusText.Text = "새로고침 중…";
        try
        {
            await RefreshRequested();
        }
        catch (Exception exception)
        {
            AppLog.Write($"flyout manual refresh failed: {exception}");
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private async void ReloadClaudeLimitsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (ClaudeLimitsRefreshRequested is null)
        {
            return;
        }

        ReloadClaudeLimitsButton.IsEnabled = false;
        try
        {
            await ClaudeLimitsRefreshRequested();
        }
        finally
        {
            ReloadClaudeLimitsButton.IsEnabled = true;
        }
    }
}
