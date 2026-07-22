using System.Windows;
using PokeTokenBar.Core.Models;
using PokeTokenBar.Core.Util;

namespace PokeTokenBar.App.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly SettingsStore _settingsStore;

    public SettingsWindow(AppSettings settings, SettingsStore settingsStore)
    {
        _settings = settings;
        _settingsStore = settingsStore;

        InitializeComponent();
        ShowTokensCheckBox.IsChecked = settings.ShowTokensInMenu;
        ShowCostCheckBox.IsChecked = settings.ShowCostInMenu;
        ShowLimitCheckBox.IsChecked = settings.ShowLimitInMenu;
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        _settings.ShowTokensInMenu = ShowTokensCheckBox.IsChecked == true;
        _settings.ShowCostInMenu = ShowCostCheckBox.IsChecked == true;
        _settings.ShowLimitInMenu = ShowLimitCheckBox.IsChecked == true;
        _settingsStore.Save(_settings);
        Close();
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
