using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using PokeTokenBar.Core.Usage;
using PokeTokenBar.Core.Util;

namespace PokeTokenBar.App.Views;

public partial class FlyoutWindow : Window
{
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(244, 133, 133));
    private bool _isShuttingDown;
    private bool _keepOpenWhenDeactivated;

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

        var block = store.ClaudeActiveBlock;
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
