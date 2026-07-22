using System.Windows;
using System.Windows.Controls;
using PokeTokenBar.Core.Models;
using PokeTokenBar.Core.Util;

namespace PokeTokenBar.App.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly SettingsStore _settingsStore;

    public event EventHandler? Saved;

    public SettingsWindow(AppSettings settings, SettingsStore settingsStore)
    {
        _settings = settings;
        _settingsStore = settingsStore;

        InitializeComponent();
        ShowTokensCheckBox.IsChecked = settings.ShowTokensInMenu;
        ShowCostCheckBox.IsChecked = settings.ShowCostInMenu;
        ShowLimitCheckBox.IsChecked = settings.ShowLimitInMenu;
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
}
