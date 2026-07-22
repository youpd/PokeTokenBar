using PokeTokenBar.Core.Models;

namespace PokeTokenBar.Core.Usage;

public interface IUsageProvider
{
    string Id { get; }

    string DisplayName { get; }

    Task<DailyUsage?> FetchDailyAsync(CancellationToken cancellationToken);

    Task<ProviderEnrichment> FetchEnrichmentAsync(CancellationToken cancellationToken);
}
