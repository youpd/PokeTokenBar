using System.Globalization;

namespace PokeTokenBar.Core.Usage;

public static class UsageProviderRegistry
{
    public static IReadOnlyList<IUsageProvider> CreateDefault(
        LocalUsageCache cache,
        Func<DateTimeOffset>? clock = null,
        TimeZoneInfo? timeZone = null,
        CultureInfo? culture = null) =>
        [
            new LocalClaudeProvider(cache, clock, timeZone, culture),
            new LocalCodexProvider(cache, clock, timeZone, culture),
            new LocalGeminiProvider(cache, clock, timeZone, culture),
        ];
}
