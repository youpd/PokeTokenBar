using System.ComponentModel;
using System.Windows;

namespace PokeTokenBar.App.Views;

public partial class FlyoutWindow : Window
{
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
}
