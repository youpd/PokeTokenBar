using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PokeTokenBar.App.Platform;
using PokeTokenBar.Core.Companion;
using PokeTokenBar.Core.Models;
using PokeTokenBar.Core.Poke;
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
    private readonly CompanionStore? _companionStore;
    private readonly SpriteStore? _spriteStore;
    private readonly DispatcherTimer _bobTimer;
    private bool _bobUp;
    private int _spriteRequestId;
    private BitmapSource? _eggSprite;
    private Task<byte[]?>? _eggSpriteTask;
    private readonly HashSet<string> _dexBackfills = new(StringComparer.Ordinal);
    private AvailableUpdate? _availableUpdate;

    public FlyoutWindow() : this(null, null)
    {
    }

    public FlyoutWindow(CompanionStore? companionStore, SpriteStore? spriteStore)
    {
        _companionStore = companionStore;
        _spriteStore = spriteStore;
        InitializeComponent();
        _bobTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _bobTimer.Tick += (_, _) =>
        {
            _bobUp = !_bobUp;
            CompanionBobTransform.Y = _bobUp ? -3 : 1;
        };
        _bobTimer.Start();
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

    public Action? ApplyUpdateRequested { get; set; }

    public Action? SkipUpdateRequested { get; set; }

    public void UpdateAvailableUpdate(AvailableUpdate? update)
    {
        _availableUpdate = update;
        ApplyLanguage();
    }

    public void UpdateDisplay(UsageStore store)
    {
        _lastStore = store;
        ApplyLanguage();
        var text = Text;
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
        UpdateCompanionDisplay();
        UpdateShopAndBag();
        UpdateCollection();

        var block = selected?.ActiveBlock;
        BlockTokensText.Text = block is null
            ? text.NoActiveBlock
            : TokenFormatter.Grouped(block.TotalTokens);
        BurnRateText.Text = block is null
            ? string.Empty
            : text.PerMinute((long)block.TokensPerMinute);

        RefreshButton.IsEnabled = !store.IsRefreshing;
        if (!string.IsNullOrWhiteSpace(store.LastErrorDescription))
        {
            RefreshStatusText.Foreground = ErrorBrush;
            RefreshStatusText.Text = text.PartialReadError;
        }
        else if (store.LastUpdated is { } updated)
        {
            RefreshStatusText.Foreground = (Brush)FindResource("SecondaryTextBrush");
            RefreshStatusText.Text = text.Updated(updated);
        }
        else
        {
            RefreshStatusText.Foreground = (Brush)FindResource("SecondaryTextBrush");
            RefreshStatusText.Text = text.Refreshing;
        }
    }

    private L Text => new(_companionStore?.State.Language ?? AppLanguage.En);

    private void ApplyLanguage()
    {
        var text = Text;
        SubtitleText.Text = text.Subtitle;
        HomeTab.Header = text.Home;
        ShopTab.Header = text.Shop;
        BagTab.Header = text.Bag;
        CollectionTab.Header = text.Collection;
        TodayLabel.Text = text.Today;
        CostLabel.Text = text.Cost;
        WeekLabel.Text = text.Week;
        MonthLabel.Text = text.Month;
        ProvidersLabel.Text = text.Providers;
        InputLabel.Text = text.Input;
        OutputLabel.Text = text.Output;
        CacheWriteLabel.Text = text.CacheWrite;
        CacheReadLabel.Text = text.CacheRead;
        CurrentBlockLabel.Text = text.CurrentBlock;
        LimitSectionTitle.Text = text.OfficialLimits;
        ReloadClaudeLimitsButton.Content = text.Reload;
        WalletLabel.Text = text.Wallet;
        RefreshButton.ToolTip = text.RefreshNow;
        UpdateBanner.Visibility = _availableUpdate is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (_availableUpdate is { } update)
        {
            UpdateBannerText.Text = text.UpdateAvailable(update.Version);
            ApplyUpdateButton.Content = text.ApplyUpdate;
            SkipUpdateButton.Content = text.SkipUpdate;
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
            SelectedProviderNameText.Text = Text.NoProvider;
            SelectedProviderTodayText.Text = "—";
            SetTokenBreakdown(null);
            return;
        }

        var today = snapshot.Today;
        SelectedProviderNameText.Text = snapshot.DisplayName;
        var tokens = today?.TotalTokens ?? 0;
        var cost = today?.TotalCost ?? 0;
        SelectedProviderTodayText.Text = snapshot.ProviderId == "codex"
            ? Text.CodexTodayProvider(tokens, cost)
            : Text.TodayProvider(tokens, cost);
        SetTokenBreakdown(today);
    }

    private void UpdateCompanionDisplay()
    {
        if (_companionStore is null)
        {
            return;
        }

        var active = _companionStore.State.Active;
        var request = ++_spriteRequestId;
        if (active is null)
        {
            CompanionSpriteImage.Source = _eggSprite;
            CompanionSpriteImage.Visibility = _eggSprite is null
                ? Visibility.Collapsed
                : Visibility.Visible;
            CompanionEmojiText.Visibility = _eggSprite is null
                ? Visibility.Visible
                : Visibility.Collapsed;
            CompanionEmojiText.Text = "🥚";
            CompanionNameText.Text = Text.TokenEgg;
            CompanionMetaText.Text = Text.ToHatch(_companionStore.EggTokensToHatch);
            CompanionLineText.Text = string.Empty;
            if (_eggSprite is null)
            {
                _ = LoadEggSpriteAsync(request);
            }
        }
        else
        {
            CompanionEmojiText.Visibility = Visibility.Collapsed;
            CompanionSpriteImage.Visibility = Visibility.Visible;
            CompanionNameText.Text = (_companionStore.CurrentIsShiny ? "✨ " : string.Empty) +
                _companionStore.DisplayName;
            var nature = active.Nature?.DisplayName(_companionStore.State.Language) ?? "?";
            CompanionMetaText.Text = $"{Text.Rarity(active.Rarity)} · {_companionStore.StageText} · {nature}";
            CompanionLineText.Text = string.Join(
                "  →  ",
                _companionStore.LineNodes.Select(node =>
                    node.Kind == "cur" ? $"● #{node.Id}" : $"○ #{node.Id}"));
            _ = LoadCompanionSpriteAsync(
                request,
                active.CurrentID,
                _companionStore.CurrentIsShiny);
        }

        CompanionProgressBar.Value = _companionStore.Progress * 100;
        CompanionProgressText.Text = TokenFormatter.Percent(_companionStore.Progress * 100);
        CompanionStatusText.Text = _companionStore.DisplayState switch
        {
            CompanionStateKind.Egg => Text.EggStatus,
            CompanionStateKind.Working => Text.Working,
            CompanionStateKind.Focus => Text.Focus,
            CompanionStateKind.Tired => Text.Tired,
            CompanionStateKind.Sleep => Text.Sleeping,
            CompanionStateKind.LevelUp => _companionStore.JustGraduated is { } graduated
                ? Text.Graduated(graduated)
                : _companionStore.JustEvolvedTo is { } evolved
                    ? Text.Evolved(evolved)
                    : Text.LevelUp,
            _ => Text.Ready,
        };
    }

    private void UpdateShopAndBag()
    {
        if (_companionStore is null)
        {
            return;
        }

        WalletText.Text = TokenFormatter.Compact(_companionStore.AvailableTokens);
        ShopPanel.Children.Clear();
        foreach (var kind in Enum.GetValues<ItemKind>())
        {
            var button = new Button
            {
                Content = kind.IsPassive() && _companionStore.ItemCount(kind) > 0
                    ? Text.Active
                    : Text.Buy,
                IsEnabled = _companionStore.CanBuy(kind),
                MinWidth = 64,
            };
            button.Click += (_, _) =>
            {
                _companionStore.Buy(kind);
                UpdateShopAndBag();
            };
            ShopPanel.Children.Add(BuildItemCard(
                kind,
                Text.Item(kind),
                Text.TokensPrice(kind.ShopPrice()),
                button));
        }

        BagPanel.Children.Clear();
        if (_companionStore.OwnedItems.Count == 0)
        {
            BagPanel.Children.Add(new TextBlock
            {
                Text = Text.EmptyBag,
                Foreground = (Brush)FindResource("SecondaryTextBrush"),
                Margin = new Thickness(4),
            });
        }

        foreach (var item in _companionStore.OwnedItems)
        {
            var button = new Button { MinWidth = 64 };
            if (item.Kind.IsPassive())
            {
                button.Content = Text.Active;
                button.IsEnabled = false;
            }
            else if (item.Kind == ItemKind.RareCandy)
            {
                button.Content = Text.Use;
                button.IsEnabled = _companionStore.CanUseRareCandy;
                button.Click += async (_, _) =>
                {
                    await _companionStore.UseRareCandyAsync();
                    UpdateDisplay(_lastStore!);
                };
            }
            else
            {
                button.Content = Text.Use;
                button.IsEnabled = _companionStore.CanUseMint;
                button.Click += (_, _) =>
                {
                    _companionStore.UseMint();
                    UpdateDisplay(_lastStore!);
                };
            }

            BagPanel.Children.Add(BuildItemCard(
                item.Kind,
                Text.Item(item.Kind),
                $"×{item.Count}",
                button));
        }
    }

    private void UpdateCollection()
    {
        if (_companionStore is null)
        {
            return;
        }

        var entries = _companionStore.DexEntriesSorted;
        CollectionSummaryText.Text = Text.CollectionSummary(entries.Count) + " · " +
            string.Join("  ", Enum.GetValues<Rarity>()
                .Reverse()
                .Select(rarity => $"{Text.Rarity(rarity)} {entries.Count(entry => entry.Rarity == rarity)}"));
        CollectionPanel.Children.Clear();
        if (entries.Count == 0)
        {
            CollectionPanel.Children.Add(new TextBlock
            {
                Text = Text.EmptyDex,
                Foreground = (Brush)FindResource("SecondaryTextBrush"),
                Margin = new Thickness(4),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        foreach (var entry in entries)
        {
            var name = entry.Names is not null &&
                entry.Names.TryGetValue(entry.FinalID, out var values)
                ? _companionStore.State.Language.ResolveName(values) ?? $"#{entry.FinalID}"
                : $"#{entry.FinalID}";
            if (entry.Names is null || entry.Names.Count == 0)
            {
                _ = BackfillDexAsync(entry);
            }
            var image = new Image { Width = 52, Height = 52 };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            var text = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
            text.Children.Add(new TextBlock
            {
                Text = (entry.IsShiny ? "✨ " : string.Empty) + name,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("PrimaryTextBrush"),
            });
            text.Children.Add(new TextBlock
            {
                Text = $"{string.Join(" → ", entry.ChainOrder.Select(id => "#" + id))} · {Text.Rarity(entry.Rarity)}" +
                    (entry.Nature is { } nature
                        ? $" · {nature.DisplayName(_companionStore.State.Language)}"
                        : string.Empty),
                Foreground = (Brush)FindResource("SecondaryTextBrush"),
                TextWrapping = TextWrapping.Wrap,
            });
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(image);
            panel.Children.Add(text);
            CollectionPanel.Children.Add(new Border
            {
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(12),
                Background = (Brush)FindResource("CardBackgroundBrush"),
                CornerRadius = new CornerRadius(10),
                Child = panel,
            });
            _ = LoadImageAsync(image, entry.FinalID, entry.IsShiny);
        }
    }

    private Border BuildItemCard(ItemKind kind, string title, string subtitle, Button action)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var iconGrid = new Grid { Width = 42, Height = 42 };
        var emoji = new TextBlock
        {
            Text = kind.FallbackEmoji(),
            FontFamily = new FontFamily("Segoe UI Emoji"),
            FontSize = 28,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        iconGrid.Children.Add(emoji);
        if (kind.SpriteName() is { } spriteName)
        {
            var image = new Image { Width = 38, Height = 38 };
            iconGrid.Children.Add(image);
            _ = LoadItemImageAsync(image, emoji, spriteName);
        }

        grid.Children.Add(iconGrid);
        var labels = new StackPanel { Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(new TextBlock { Text = title, Foreground = (Brush)FindResource("PrimaryTextBrush"), FontWeight = FontWeights.SemiBold });
        labels.Children.Add(new TextBlock { Text = subtitle, Foreground = (Brush)FindResource("SecondaryTextBrush") });
        Grid.SetColumn(labels, 1);
        grid.Children.Add(labels);
        Grid.SetColumn(action, 2);
        grid.Children.Add(action);
        return new Border
        {
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(12),
            Background = (Brush)FindResource("CardBackgroundBrush"),
            CornerRadius = new CornerRadius(10),
            Child = grid,
        };
    }

    private async Task LoadEggSpriteAsync(int request)
    {
        if (_spriteStore is null) return;
        _eggSpriteTask ??= _spriteStore.GetEggAsync();
        var bytes = await _eggSpriteTask;
        if (bytes is null)
        {
            _eggSpriteTask = null;
            return;
        }

        try
        {
            _eggSprite ??= SpriteBitmap.Decode(bytes, cropTransparent: true);
            if (request == _spriteRequestId && _companionStore?.State.Active is null)
            {
                CompanionSpriteImage.Source = _eggSprite;
                CompanionSpriteImage.Visibility = Visibility.Visible;
                CompanionEmojiText.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception exception)
        {
            AppLog.Write($"egg sprite decode failed: {exception.Message}");
            _eggSpriteTask = null;
        }
    }

    private async Task LoadCompanionSpriteAsync(int request, int id, bool shiny)
    {
        if (_spriteStore is null) return;
        var bytes = await _spriteStore.GetSpeciesAsync(id, animated: true, shiny);
        if (request == _spriteRequestId && bytes is not null)
        {
            CompanionSpriteImage.Source = ToBitmap(bytes);
        }
    }

    private async Task BackfillDexAsync(DexEntry entry)
    {
        if (_companionStore is null || !_dexBackfills.Add(entry.Id)) return;
        try
        {
            await _companionStore.ResolveDexNamesAsync(entry);
            UpdateCollection();
        }
        finally
        {
            _dexBackfills.Remove(entry.Id);
        }
    }

    private async Task LoadImageAsync(Image image, int id, bool shiny)
    {
        if (_spriteStore is null) return;
        var bytes = await _spriteStore.GetSpeciesAsync(id, animated: false, shiny);
        if (bytes is not null) image.Source = ToBitmap(bytes);
    }

    private async Task LoadItemImageAsync(Image image, TextBlock fallback, string name)
    {
        if (_spriteStore is null) return;
        var bytes = await _spriteStore.GetItemAsync(name);
        if (bytes is not null)
        {
            image.Source = ToBitmap(bytes);
            fallback.Visibility = Visibility.Collapsed;
        }
    }

    internal static BitmapImage ToBitmap(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
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
            StatusBannerText.Text = Text.Incident(provider, incident.Description);
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
                ? $"Claude {Text.OfficialLimits}"
                : $"Claude · {plan}";
            if (store.ClaudeLimitsAuthExpired)
            {
                LimitSectionMessage.Text = Text.ClaudeAuthExpired;
                LimitSectionMessage.Visibility = Visibility.Visible;
                ReloadClaudeLimitsButton.Visibility = Visibility.Visible;
                return;
            }

            var limits = store.ClaudeLimits;
            AddLimitRow(Text.FiveHours, limits?.FiveHour?.Utilization, limits?.FiveHour?.ResetDate);
            AddLimitRow(Text.Weekly, limits?.SevenDay?.Utilization, limits?.SevenDay?.ResetDate);
            AddLimitRow(Text.WeeklyOpus, limits?.SevenDayOpus?.Utilization, limits?.SevenDayOpus?.ResetDate);
            AddLimitRow(Text.WeeklySonnet, limits?.SevenDaySonnet?.Utilization, limits?.SevenDaySonnet?.ResetDate);
            foreach (var entry in limits?.ScopedLimitEntries ?? [])
            {
                AddLimitRow(
                    entry.DisplayName,
                    entry.Percent,
                    DateTimeOffset.TryParse(entry.ResetsAt, out var reset) ? reset : null);
            }

            if (LimitRowsPanel.Children.Count == 0)
            {
                LimitSectionMessage.Text = Text.LimitsUnavailable;
                LimitSectionMessage.Visibility = Visibility.Visible;
                ReloadClaudeLimitsButton.Visibility = Visibility.Visible;
            }
            else if (store.AreClaudeLimitsStale)
            {
                LimitSectionMessage.Text = Text.LimitsStale;
                LimitSectionMessage.Visibility = Visibility.Visible;
            }

            if (store.FiveHourForecast is { } forecast)
            {
                var forecastText = forecast.BeforeReset
                    ? Text.ForecastAt(forecast.DepletionDate)
                    : Text.ForecastAfterReset;
                LimitSectionMessage.Text = forecastText;
                LimitSectionMessage.Visibility = Visibility.Visible;
            }

            return;
        }

        if (providerId == "codex" && store.CodexLimits is { } codex)
        {
            LimitSectionBorder.Visibility = Visibility.Visible;
            LimitSectionTitle.Text = $"Codex {Text.OfficialLimits}";
            foreach (var snapshot in codex.Snapshots.Where(snapshot => snapshot.IsVisible))
            {
                var prefix = codex.Snapshots.Count > 1 ? snapshot.DisplayName + " " : string.Empty;
                AddLimitRow(prefix + Text.Primary, snapshot.Primary?.UsedPercent, snapshot.Primary?.ResetDate);
                AddLimitRow(prefix + Text.Secondary, snapshot.Secondary?.UsedPercent, snapshot.Secondary?.ResetDate);
                AddLimitRow(prefix + Text.Individual, snapshot.IndividualLimit?.UsedPercent, snapshot.IndividualLimit?.ResetDate);
            }

            if (store.AreCodexLimitsStale)
            {
                LimitSectionMessage.Text = Text.LimitsStale;
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
        _bobTimer.Stop();
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
        RefreshStatusText.Text = Text.Refreshing;
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

    private void ApplyUpdateButton_OnClick(object sender, RoutedEventArgs e) =>
        ApplyUpdateRequested?.Invoke();

    private void SkipUpdateButton_OnClick(object sender, RoutedEventArgs e) =>
        SkipUpdateRequested?.Invoke();
}
