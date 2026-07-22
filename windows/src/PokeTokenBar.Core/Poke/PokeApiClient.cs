using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PokeTokenBar.Core.Companion;
using PokeTokenBar.Core.Util;

namespace PokeTokenBar.Core.Poke;

public interface IPokeProvider
{
    Task<EvoLine> LineAsync(int baseSpeciesId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BaseSpecies>> BaseSpeciesIndexAsync(
        CancellationToken cancellationToken = default);

    Task<BaseSpecies?> BaseSpeciesAsync(int id, CancellationToken cancellationToken = default);
}

public sealed class PokeApiClient : IPokeProvider, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly string _baseIndexFile;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<int, SpeciesDto> _speciesCache = [];
    private readonly Dictionary<int, EvoLine> _lineCache = [];
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private IReadOnlyList<BaseSpecies>? _baseIndexCache;
    private bool _restBuildTried;

    public PokeApiClient(
        string? baseIndexFile = null,
        HttpClient? httpClient = null,
        Func<DateTimeOffset>? clock = null)
    {
        _baseIndexFile = baseIndexFile ?? AppPaths.Default.BaseIndexFile;
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<EvoLine> LineAsync(
        int baseSpeciesId,
        CancellationToken cancellationToken = default)
    {
        await _cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_lineCache.TryGetValue(baseSpeciesId, out var cached))
            {
                return cached;
            }
        }
        finally
        {
            _cacheLock.Release();
        }

        var baseSpecies = await SpeciesAsync(baseSpeciesId, cancellationToken).ConfigureAwait(false);
        if (!TryValidateChainUrl(baseSpecies.EvolutionChain.Url, out var chainUri))
        {
            throw new InvalidDataException("PokéAPI returned an unsafe evolution-chain URL.");
        }

        var chain = await GetAsync<ChainDto>(chainUri!, cancellationToken).ConfigureAwait(false);
        var tree = ToNode(chain.Chain);
        var names = new Dictionary<int, IReadOnlyDictionary<string, string>>();
        foreach (var id in AllIds(tree).Distinct())
        {
            var species = await SpeciesAsync(id, cancellationToken).ConfigureAwait(false);
            names[id] = species.Names
                .Where(name => name.Language.Name is "ko" or "en" or "ja-Hrkt" or "ja")
                .GroupBy(name => name.Language.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last().Name, StringComparer.Ordinal);
        }

        var result = new EvoLine(
            baseSpeciesId,
            tree,
            RarityLogic.From(
                baseSpecies.CaptureRate,
                baseSpecies.IsLegendary,
                baseSpecies.IsMythical),
            names);
        await _cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _lineCache[baseSpeciesId] = result;
        }
        finally
        {
            _cacheLock.Release();
        }

        return result;
    }

    public async Task<IReadOnlyList<BaseSpecies>> BaseSpeciesIndexAsync(
        CancellationToken cancellationToken = default)
    {
        if (_baseIndexCache is not null)
        {
            return _baseIndexCache;
        }

        var disk = ReadBaseIndex();
        if (disk is { Entries.Count: > 0 } &&
            _clock() - disk.FetchedAt < TimeSpan.FromDays(30))
        {
            return _baseIndexCache = disk.Entries;
        }

        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                    "https://graphql.pokeapi.co/v1beta2",
                    new
                    {
                        query = "{ pokemonspecies(where: {evolves_from_species_id: {_is_null: true}, " +
                            $"id: {{_lte: 649, _neq: {PokemonOdds.DittoSpeciesID}}}}}, " +
                            "order_by: {id: asc}) { id capture_rate } }",
                    },
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            var decoded = await JsonSerializer.DeserializeAsync<GraphQlResponse>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            var entries = decoded?.Data.PokemonSpecies
                .Select(row => new BaseSpecies(row.Id, row.CaptureRate))
                .ToArray() ?? [];
            if (entries.Length == 0)
            {
                throw new JsonException("PokéAPI returned an empty base index.");
            }

            _baseIndexCache = entries;
            WriteBaseIndex(new BaseIndexSnapshot(_clock(), entries));
            return entries;
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException ||
            !cancellationToken.IsCancellationRequested)
        {
            if (disk is { Entries.Count: > 0 })
            {
                return _baseIndexCache = disk.Entries;
            }

            if (!_restBuildTried)
            {
                _restBuildTried = true;
                _ = Task.Run(BuildBaseIndexViaRestAsync);
            }

            throw;
        }
    }

    public async Task<BaseSpecies?> BaseSpeciesAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (id == PokemonOdds.DittoSpeciesID)
        {
            return null;
        }

        var value = await SpeciesAsync(id, cancellationToken).ConfigureAwait(false);
        return value.EvolvesFromSpecies is null
            ? new BaseSpecies(id, value.CaptureRate)
            : null;
    }

    public void Dispose()
    {
        _cacheLock.Dispose();
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }

    public static int SpeciesIdFromUrl(string value)
    {
        var path = value.TrimEnd('/');
        return int.TryParse(path[(path.LastIndexOf('/') + 1)..], out var id) ? id : 0;
    }

    public static bool TryValidateChainUrl(string value, out Uri? uri)
    {
        var valid = Uri.TryCreate(value, UriKind.Absolute, out uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            uri.Host.Equals("pokeapi.co", StringComparison.OrdinalIgnoreCase);
        if (!valid)
        {
            uri = null;
        }

        return valid;
    }

    private async Task<SpeciesDto> SpeciesAsync(int id, CancellationToken cancellationToken)
    {
        await _cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_speciesCache.TryGetValue(id, out var cached))
            {
                return cached;
            }
        }
        finally
        {
            _cacheLock.Release();
        }

        var result = await GetAsync<SpeciesDto>(
                new Uri($"https://pokeapi.co/api/v2/pokemon-species/{id}"),
                cancellationToken)
            .ConfigureAwait(false);
        await _cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _speciesCache[id] = result;
        }
        finally
        {
            _cacheLock.Release();
        }

        return result;
    }

    private async Task<T> GetAsync<T>(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
                   .ConfigureAwait(false)
               ?? throw new JsonException("PokéAPI response was empty.");
    }

    private async Task BuildBaseIndexViaRestAsync()
    {
        var values = new List<BaseSpecies>();
        for (var start = 1; start <= 649; start += 6)
        {
            var tasks = Enumerable.Range(start, Math.Min(6, 650 - start))
                .Select(async id =>
                {
                    try { return await BaseSpeciesAsync(id).ConfigureAwait(false); }
                    catch { return null; }
                });
            values.AddRange((await Task.WhenAll(tasks).ConfigureAwait(false)).OfType<BaseSpecies>());
        }

        if (values.Count < 150)
        {
            AppLog.Write($"base index REST build incomplete: {values.Count}");
            return;
        }

        _baseIndexCache = values.OrderBy(value => value.Id).ToArray();
        WriteBaseIndex(new BaseIndexSnapshot(_clock(), _baseIndexCache));
        AppLog.Write($"base index REST build complete: {values.Count}");
    }

    private BaseIndexSnapshot? ReadBaseIndex()
    {
        try
        {
            return File.Exists(_baseIndexFile)
                ? JsonSerializer.Deserialize<BaseIndexSnapshot>(
                    File.ReadAllText(_baseIndexFile),
                    JsonOptions)
                : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return null;
        }
    }

    private void WriteBaseIndex(BaseIndexSnapshot snapshot)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(_baseIndexFile));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temporary = _baseIndexFile + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot, JsonOptions));
            File.Move(temporary, _baseIndexFile, true);
        }
        catch (IOException exception)
        {
            AppLog.Write($"base index cache write failed: {exception.Message}");
        }
    }

    private static EvoNode ToNode(ChainLinkDto link) => new()
    {
        SpeciesID = SpeciesIdFromUrl(link.Species.Url ?? string.Empty),
        Children = link.EvolvesTo.Select(ToNode).ToList(),
    };

    private static IEnumerable<int> AllIds(EvoNode node) =>
        new[] { node.SpeciesID }.Concat(node.Children.SelectMany(AllIds));

    private sealed record BaseIndexSnapshot(DateTimeOffset FetchedAt, IReadOnlyList<BaseSpecies> Entries);

    private sealed class GraphQlResponse
    {
        public required GraphQlData Data { get; init; }
    }

    private sealed class GraphQlData
    {
        [JsonPropertyName("pokemonspecies")]
        public List<GraphQlRow> PokemonSpecies { get; init; } = [];
    }

    private sealed class GraphQlRow
    {
        public int Id { get; init; }

        [JsonPropertyName("capture_rate")]
        public int CaptureRate { get; init; }
    }

    private sealed class SpeciesDto
    {
        [JsonPropertyName("capture_rate")]
        public int CaptureRate { get; init; }

        [JsonPropertyName("is_legendary")]
        public bool IsLegendary { get; init; }

        [JsonPropertyName("is_mythical")]
        public bool IsMythical { get; init; }

        public List<NameDto> Names { get; init; } = [];

        [JsonPropertyName("evolution_chain")]
        public required UrlRef EvolutionChain { get; init; }

        [JsonPropertyName("evolves_from_species")]
        public NamedRef? EvolvesFromSpecies { get; init; }
    }

    private sealed record NameDto(string Name, NamedRef Language);
    private sealed record NamedRef(string Name, string? Url);
    private sealed record UrlRef(string Url);
    private sealed record ChainDto(ChainLinkDto Chain);

    private sealed class ChainLinkDto
    {
        public required NamedRef Species { get; init; }

        [JsonPropertyName("evolves_to")]
        public List<ChainLinkDto> EvolvesTo { get; init; } = [];
    }
}
