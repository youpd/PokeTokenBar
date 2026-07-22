namespace PokeTokenBar.Core.Util;

public static class SupportIssue
{
    private const string NewIssueUrl = "https://github.com/youpd/PokeTokenBar/issues/new";

    public static Uri Build(string version, string osDescription)
    {
        var title = $"[Windows] Problem report (v{version})";
        var body = $"""
            What happened:
            (Describe when, on which screen, and what you saw.)


            ---
            App version: v{version}
            Windows: {osDescription}
            Logs: review them first, then attach them manually from Settings > Show logs.
            """;
        return new Uri($"{NewIssueUrl}?title={Escape(title)}&body={Escape(body)}");
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);
}
