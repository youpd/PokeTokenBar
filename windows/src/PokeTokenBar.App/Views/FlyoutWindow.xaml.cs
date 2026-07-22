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
}
