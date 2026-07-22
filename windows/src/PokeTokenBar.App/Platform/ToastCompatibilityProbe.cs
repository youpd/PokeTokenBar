using Microsoft.Toolkit.Uwp.Notifications;

namespace PokeTokenBar.App.Platform;

internal static class ToastCompatibilityProbe
{
    public static string AssemblyVersion =>
        typeof(ToastContentBuilder).Assembly.GetName().Version?.ToString() ?? "unknown";
}
