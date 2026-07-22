using System.Globalization;

namespace PokeTokenBar.Core.Util;

public static class TokenFormatter
{
    public static string Compact(long value)
    {
        var absolute = Math.Abs((double)value);
        var sign = value < 0 ? "-" : string.Empty;

        if (absolute < 1_000)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        if (absolute < 1_000_000)
        {
            return sign + FormatTrimmed(absolute / 1_000, 1) + "K";
        }

        if (absolute < 1_000_000_000)
        {
            return sign + FormatTrimmed(absolute / 1_000_000, 1) + "M";
        }

        return sign + FormatTrimmed(absolute / 1_000_000_000, 2) + "B";
    }

    public static string Grouped(long value) =>
        value.ToString("N0", CultureInfo.InvariantCulture);

    public static string Cost(double usd) =>
        string.Create(CultureInfo.InvariantCulture, $"${usd:F2}");

    public static string CostCompact(double usd)
    {
        if (usd < 100)
        {
            return string.Create(CultureInfo.InvariantCulture, $"${usd:F1}");
        }

        if (usd < 10_000)
        {
            return string.Create(CultureInfo.InvariantCulture, $"${usd:F0}");
        }

        return string.Create(CultureInfo.InvariantCulture, $"${usd / 1_000:F1}K");
    }

    public static string Percent(double value)
    {
        var format = Math.Abs(value - Math.Round(value)) < 0.000_000_1
            ? "F0"
            : "F1";
        return value.ToString(format, CultureInfo.InvariantCulture) + "%";
    }

    private static string FormatTrimmed(double value, int decimals) =>
        value.ToString($"0.{new string('#', decimals)}", CultureInfo.InvariantCulture);
}
