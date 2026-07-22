using PokeTokenBar.Core.Util;

namespace PokeTokenBar.Core.Usage;

public static class TrayText
{
    public static IReadOnlyList<string> UsageLines(
        bool hasUpdated,
        bool showTokens,
        bool showCost,
        bool showLimit,
        long todayTokens,
        double todayCost,
        string? limitLine)
    {
        if (!hasUpdated)
        {
            return ["—"];
        }

        var lines = new List<string>(3);
        if (showTokens)
        {
            lines.Add(TokenFormatter.Compact(todayTokens));
        }

        if (showCost)
        {
            lines.Add(TokenFormatter.CostCompact(todayCost));
        }

        if (showLimit && !string.IsNullOrWhiteSpace(limitLine))
        {
            lines.Add(limitLine);
        }

        return lines;
    }

    public static IReadOnlyList<string> TooltipLines(
        string header,
        bool hasUpdated,
        bool showTokens,
        bool showCost,
        bool showLimit,
        long todayTokens,
        double todayCost,
        string? limitLine)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(header);
        return
        [
            header,
            .. UsageLines(
                hasUpdated,
                showTokens,
                showCost,
                showLimit,
                todayTokens,
                todayCost,
                limitLine),
        ];
    }

    public static string FallbackText(IEnumerable<string> lines, int maximumLength = 127)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumLength, 2);
        var text = string.Join(" · ", lines);
        return text.Length <= maximumLength
            ? text
            : text[..(maximumLength - 1)] + "…";
    }
}
