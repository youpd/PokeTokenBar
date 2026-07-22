namespace PokeTokenBar.Core.Usage;

public static class ModelPricing
{
    private static readonly IReadOnlyDictionary<string, Prices> Exact =
        new Dictionary<string, Prices>(StringComparer.OrdinalIgnoreCase)
        {
            ["claude-opus-4-8"] = new(5, 25, 6.25, 0.5),
            ["claude-opus-4-7"] = new(5, 25, 6.25, 0.5),
            ["claude-sonnet-4-6"] = new(3, 15, 3.75, 0.3),
            ["claude-haiku-4-5-20251001"] = new(1, 5, 1.25, 0.1),
            ["claude-fable-5"] = new(0, 0, 0, 0),
            ["gpt-5.5"] = new(5, 30, 0, 0.5),
            ["gemini-2.5-pro"] = new(1.25, 10, 0, 0.3125),
            ["gemini-2.5-flash"] = new(0.30, 2.5, 0, 0.075),
            ["gemini-2.0-flash"] = new(0.10, 0.4, 0, 0.025),
        };

    public static double Cost(
        string model,
        long input,
        long output,
        long cacheWrite,
        long cacheRead)
    {
        var prices = Resolve(model);
        return ((input * prices.Input) +
                (output * prices.Output) +
                (cacheWrite * prices.CacheWrite) +
                (cacheRead * prices.CacheRead)) / 1_000_000d;
    }

    private static Prices Resolve(string model)
    {
        if (Exact.TryGetValue(model, out var exact))
        {
            return exact;
        }

        var value = model.ToLowerInvariant();
        if (value.Contains("opus", StringComparison.Ordinal))
        {
            return new(5, 25, 6.25, 0.5);
        }

        if (value.Contains("sonnet", StringComparison.Ordinal))
        {
            return new(3, 15, 3.75, 0.3);
        }

        if (value.Contains("haiku", StringComparison.Ordinal))
        {
            return new(1, 5, 1.25, 0.1);
        }

        if (value.Contains("gpt", StringComparison.Ordinal) ||
            value.Contains("codex", StringComparison.Ordinal) ||
            value.Contains("o4", StringComparison.Ordinal) ||
            value.Contains("o3", StringComparison.Ordinal))
        {
            return new(5, 30, 0, 0.5);
        }

        if (value.StartsWith("gemini", StringComparison.Ordinal))
        {
            if (value.Contains("pro", StringComparison.Ordinal))
            {
                return new(1.25, 10, 0, 0.3125);
            }

            if (value.Contains("flash", StringComparison.Ordinal))
            {
                return new(0.30, 2.5, 0, 0.075);
            }
        }

        return new(0, 0, 0, 0);
    }

    private sealed record Prices(
        double Input,
        double Output,
        double CacheWrite,
        double CacheRead);
}
