using PokeTokenBar.Core.Util;

namespace PokeTokenBar.Core.Poke;

public sealed class SpriteStore : IDisposable
{
    private const string PokemonBase =
        "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon";
    private const string ItemBase =
        "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/items";
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly string _directory;
    private readonly Dictionary<string, byte[]> _memory = [];
    private readonly LinkedList<string> _lru = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SpriteStore(string? directory = null, HttpClient? httpClient = null)
    {
        _directory = directory ?? AppPaths.Default.SpritesDirectory;
        Directory.CreateDirectory(_directory);
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public int MemoryCount => _memory.Count;

    public async Task<byte[]?> GetSpeciesAsync(
        int speciesId,
        bool animated,
        bool shiny = false,
        CancellationToken cancellationToken = default)
    {
        var value = await GetExactSpeciesAsync(
                speciesId,
                animated,
                shiny,
                cancellationToken)
            .ConfigureAwait(false);
        if (value is not null) return value;
        if (animated)
        {
            value = await GetExactSpeciesAsync(
                    speciesId,
                    false,
                    shiny,
                    cancellationToken)
                .ConfigureAwait(false);
            if (value is not null) return value;
        }

        return shiny
            ? await GetSpeciesAsync(speciesId, animated, false, cancellationToken)
                .ConfigureAwait(false)
            : null;
    }

    public async Task<byte[]?> GetItemAsync(
        string itemName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(itemName) ||
            itemName.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            return null;
        }

        var key = "item-" + itemName;
        return await GetOrFetchAsync(
                key,
                ".png",
                new Uri($"{ItemBase}/{itemName}.png"),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<byte[]?> GetEggAsync(CancellationToken cancellationToken = default) =>
        GetOrFetchAsync(
            "egg",
            ".png",
            new Uri($"{PokemonBase}/egg.png"),
            cancellationToken);

    public static string CacheKey(int speciesId, bool animated, bool shiny) =>
        $"{speciesId}-{(shiny ? "sh" : string.Empty)}{(animated ? "a" : "s")}";

    public string? FindCachedSpeciesPath(int speciesId, bool animated, bool shiny)
    {
        foreach (var candidate in CacheCandidates(speciesId, animated, shiny))
        {
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }

        return null;
    }

    public string? FindCachedEggPath()
    {
        var path = Path.Combine(_directory, "egg.png");
        return File.Exists(path) ? Path.GetFullPath(path) : null;
    }

    public void Dispose()
    {
        _gate.Dispose();
        if (_ownsClient) _httpClient.Dispose();
    }

    private Task<byte[]?> GetExactSpeciesAsync(
        int speciesId,
        bool animated,
        bool shiny,
        CancellationToken cancellationToken)
    {
        var key = CacheKey(speciesId, animated, shiny);
        var extension = animated ? ".gif" : ".png";
        var segment = (animated, shiny) switch
        {
            (true, false) => $"versions/generation-v/black-white/animated/{speciesId}.gif",
            (true, true) => $"versions/generation-v/black-white/animated/shiny/{speciesId}.gif",
            (false, true) => $"shiny/{speciesId}.png",
            _ => $"{speciesId}.png",
        };
        return GetOrFetchAsync(
            key,
            extension,
            new Uri($"{PokemonBase}/{segment}"),
            cancellationToken);
    }

    private IEnumerable<string> CacheCandidates(int speciesId, bool animated, bool shiny)
    {
        yield return Path.Combine(
            _directory,
            CacheKey(speciesId, animated, shiny) + (animated ? ".gif" : ".png"));
        if (animated)
        {
            yield return Path.Combine(
                _directory,
                CacheKey(speciesId, false, shiny) + ".png");
        }

        if (shiny)
        {
            yield return Path.Combine(
                _directory,
                CacheKey(speciesId, animated, false) + (animated ? ".gif" : ".png"));
            if (animated)
            {
                yield return Path.Combine(
                    _directory,
                    CacheKey(speciesId, false, false) + ".png");
            }
        }
    }

    private async Task<byte[]?> GetOrFetchAsync(
        string key,
        string extension,
        Uri uri,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_memory.TryGetValue(key, out var memory))
            {
                Touch(key);
                return memory;
            }
        }
        finally
        {
            _gate.Release();
        }

        var path = Path.Combine(_directory, key + extension);
        if (File.Exists(path))
        {
            try
            {
                var disk = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
                await RememberAsync(key, disk, cancellationToken).ConfigureAwait(false);
                return disk;
            }
            catch (IOException)
            {
                // A damaged cache is replaced from the network.
            }
        }

        try
        {
            using var response = await _httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            if (bytes.Length == 0) return null;
            var temporary = path + ".tmp";
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, true);
            await RememberAsync(key, bytes, cancellationToken).ConfigureAwait(false);
            return bytes;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or TaskCanceledException)
        {
            return null;
        }
    }

    private async Task RememberAsync(string key, byte[] value, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _memory[key] = value;
            Touch(key);
            while (_lru.Count > 24)
            {
                var oldest = _lru.First!.Value;
                _lru.RemoveFirst();
                _memory.Remove(oldest);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Touch(string key)
    {
        _lru.Remove(key);
        _lru.AddLast(key);
    }
}
