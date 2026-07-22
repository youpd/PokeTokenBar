namespace PokeTokenBar.Core.Util;

public static class SupportMail
{
    public const string Address = "parkdongmin123@gmail.com";

    public static Uri Build(string version, string osDescription)
    {
        var subject = $"[PokeTokenBar] Problem report (v{version})";
        var body = $"""
            What happened:
            (Describe when, on which screen, and what you saw.)


            ---
            App version: v{version}
            Windows: {osDescription}
            Log file (please attach): {AppPaths.Default.LogFile}
            """;
        return new Uri(
            $"mailto:{Address}?subject={Escape(subject)}&body={Escape(body)}");
    }

    private static string Escape(string value) => Uri.EscapeDataString(value).Replace("+", "%2B");
}
