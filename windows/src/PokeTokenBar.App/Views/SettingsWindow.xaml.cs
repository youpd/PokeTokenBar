using System.Windows;
using System.Windows.Controls;
using PokeTokenBar.Core.Models;
using PokeTokenBar.Core.Util;

namespace PokeTokenBar.App.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly SettingsStore _settingsStore;
    private readonly Func<Task>? _reloadClaudeLimits;

    public event EventHandler? Saved;

    public SettingsWindow(
        AppSettings settings,
        SettingsStore settingsStore,
        Func<Task>? reloadClaudeLimits = null)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _reloadClaudeLimits = reloadClaudeLimits;

        InitializeComponent();
        ShowTokensCheckBox.IsChecked = settings.ShowTokensInMenu;
        ShowCostCheckBox.IsChecked = settings.ShowCostInMenu;
        ShowLimitCheckBox.IsChecked = settings.ShowLimitInMenu;
        LimitNotificationsCheckBox.IsChecked = settings.LimitNotifications;
        StatusChecksCheckBox.IsChecked = settings.StatusChecksEnabled;
        DisableClaudeLimitsCheckBox.IsChecked = settings.ClaudeLimitsDisabled;
        WarnThresholdTextBox.Text = settings.WarnThreshold.ToString();
        CritThresholdTextBox.Text = settings.CritThreshold.ToString();
        CodexPathTextBox.Text = settings.CodexPath ?? string.Empty;
        RefreshIntervalComboBox.SelectedItem = RefreshIntervalComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == settings.RefreshInterval.ToString())
            ?? RefreshIntervalComboBox.Items[2];
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        _settings.ShowTokensInMenu = ShowTokensCheckBox.IsChecked == true;
        _settings.ShowCostInMenu = ShowCostCheckBox.IsChecked == true;
        _settings.ShowLimitInMenu = ShowLimitCheckBox.IsChecked == true;
        _settings.LimitNotifications = LimitNotificationsCheckBox.IsChecked == true;
        _settings.StatusChecksEnabled = StatusChecksCheckBox.IsChecked == true;
        _settings.ClaudeLimitsDisabled = DisableClaudeLimitsCheckBox.IsChecked == true;
        if (int.TryParse(WarnThresholdTextBox.Text, out var warn))
        {
            _settings.WarnThreshold = Math.Clamp(warn, 50, 95);
        }

        if (int.TryParse(CritThresholdTextBox.Text, out var critical))
        {
            _settings.CritThreshold = Math.Clamp(critical, 80, 100);
        }

        _settings.CodexPath = string.IsNullOrWhiteSpace(CodexPathTextBox.Text)
            ? null
            : CodexPathTextBox.Text.Trim();
        if (RefreshIntervalComboBox.SelectedItem is ComboBoxItem selected &&
            int.TryParse(selected.Tag?.ToString(), out var refreshInterval))
        {
            _settings.RefreshInterval = refreshInterval;
        }

        _settingsStore.Save(_settings);
        Saved?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void ReloadClaudeLimitsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_reloadClaudeLimits is not null)
        {
            await _reloadClaudeLimits();
        }
    }
}
