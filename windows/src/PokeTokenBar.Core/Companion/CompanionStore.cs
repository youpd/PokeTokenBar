using System.Text.Json;
using System.Text.Json.Serialization;
using PokeTokenBar.Core.Models;
using PokeTokenBar.Core.Poke;
using PokeTokenBar.Core.Util;

namespace PokeTokenBar.Core.Companion;

public sealed class CompanionStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly IPokeProvider _provider;
    private readonly Func<DateTimeOffset> _clock;
    private readonly string _stateFile;
    private readonly IRng _rng;
    private readonly bool _enableDittoRoll;
    private readonly SemaphoreSlim _updateGate = new(1, 1);
    private bool _disposed;
    private bool _isHatching;
    private bool _isRevealingDitto;
    private bool _prefetchInFlight;
    private int? _prefetchedLineId;
    private DateTimeOffset? _eventUntil;

    public CompanionStore(
        IPokeProvider provider,
        Func<DateTimeOffset>? clock = null,
        string? stateFile = null,
        IRng? rng = null,
        bool? enableDittoRoll = null)
    {
        _provider = provider;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _stateFile = stateFile ?? DefaultStateFile();
        _rng = rng ?? new SystemRng();
        _enableDittoRoll = enableDittoRoll ?? AppEnv.IsRealApp;
        State = LoadState();
        DisplayState = State.Active is null ? CompanionStateKind.Egg : CompanionStateKind.Idle;
    }

    public event EventHandler? Changed;

    public event Action<CompanionEvent>? CompanionEventRaised;

    public CompanionState State { get; private set; }

    public CompanionStateKind DisplayState { get; private set; }

    public EvoLine? CurrentLine { get; private set; }

    public string? JustEvolvedTo { get; private set; }

    public string? JustGraduated { get; private set; }

    public bool IsHatching => _isHatching;

    public bool IsEgg => State.Active is null;

    public bool HasActive => State.Active is not null;

    public bool CurrentIsShiny => State.Active is { } active &&
        (active.DittoDisguise is null || active.DittoRevealed) && active.IsShiny;

    public long EggTokensToHatch => Math.Max(0, PokemonBalance.EggHatchThreshold - State.EggUsage);

    public double EggProgress => Math.Clamp(
        State.EggUsage / (double)PokemonBalance.EggHatchThreshold,
        0,
        1);

    public string DisplayName => State.Active is { } active && CurrentLine is not null
        ? CurrentLine.LocalizedName(active.CurrentID, State.Language)
        : "Token Egg";

    public int? CurrentSpeciesID => State.Active?.CurrentID;

    public long Threshold => State.Active is { } active
        ? PokemonBalance.PhaseThreshold(active.Rarity, active.TotalForms, active.StageIndex)
        : 1;

    public double Progress => State.Active is { } active
        ? Math.Clamp(active.UsedAtStage / (double)Math.Max(1, Threshold), 0, 1)
        : EggProgress;

    public long TokensToNext => State.Active is { } active
        ? Math.Max(0, Threshold - active.UsedAtStage)
        : EggTokensToHatch;

    public string StageText
    {
        get
        {
            if (State.Active is not { } active) return string.Empty;
            var final = CurrentLine?.Tree.Node(active.CurrentID)?.Children.Count == 0;
            return final ? "Final" : $"{active.StageIndex + 1}/{active.TotalForms}";
        }
    }

    public IReadOnlyList<DexEntry> DexEntriesSorted => State.Dex
        .OrderByDescending(entry => entry.Rarity.SortRank())
        .ThenByDescending(entry => entry.CaughtAt ?? DateTimeOffset.MinValue)
        .ToArray();

    public IReadOnlyList<(int Id, string Kind)> LineNodes
    {
        get
        {
            if (State.Active is not { } active || CurrentLine is null) return [];
            var values = active.PathIDs.Select((id, index) =>
                    (id, index < active.StageIndex ? "done" : index == active.StageIndex ? "cur" : "future"))
                .ToList();
            if (CurrentLine.Tree.Node(active.CurrentID) is { } node)
            {
                values.AddRange(node.Children.Select(child => (child.SpeciesID, "future")));
            }

            return values;
        }
    }

    public long AvailableTokens => Math.Max(0, State.UsedSinceInstall - State.SpentTokens);

    public bool OwnsShinyCharm => ItemCount(ItemKind.ShinyCharm) > 0;

    public IReadOnlyList<(ItemKind Kind, int Count)> OwnedItems =>
        Enum.GetValues<ItemKind>()
            .Select(kind => (Kind: kind, Count: ItemCount(kind)))
            .Where(item => item.Count > 0)
            .ToArray();

    public int ItemCount(ItemKind kind) =>
        State.Inventory.GetValueOrDefault(kind.StateKey());

    public bool CanBuy(ItemKind kind) =>
        (!kind.IsPassive() || ItemCount(kind) == 0) && AvailableTokens >= kind.ShopPrice();

    public bool Buy(ItemKind kind)
    {
        if (!CanBuy(kind)) return false;
        State.SpentTokens += kind.ShopPrice();
        State.Inventory[kind.StateKey()] = ItemCount(kind) + 1;
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool CanUseRareCandy => HasActive && CurrentLine is not null &&
        ItemCount(ItemKind.RareCandy) > 0;

    public async Task<CandyUseResult> UseRareCandyAsync(
        CancellationToken cancellationToken = default)
    {
        if (!CanUseRareCandy) return CandyUseResult.Unavailable;
        var beforeStage = State.Active!.StageIndex;
        State.Inventory[ItemKind.RareCandy.StateKey()] = ItemCount(ItemKind.RareCandy) - 1;
        await ApplyUsageAsync(RareCandy.Xp, cancellationToken).ConfigureAwait(false);
        var result = State.Active is null
            ? CandyUseResult.Graduated
            : State.Active.StageIndex > beforeStage
                ? CandyUseResult.Evolved
                : CandyUseResult.Progressed;
        Emit("candy", "+XP", $"+{RareCandy.Xp:N0} XP");
        return result;
    }

    public bool CanUseMint => HasActive && ItemCount(ItemKind.Mint) > 0;

    public PokemonNature? UseMint()
    {
        if (!CanUseMint) return null;
        var current = State.Active!.Nature;
        var pool = Enum.GetValues<PokemonNature>().Where(value => value != current).ToArray();
        var value = pool[(int)(_rng.Next() % (ulong)pool.Length)];
        State.Active.Nature = value;
        State.Inventory[ItemKind.Mint.StateKey()] = ItemCount(ItemKind.Mint) - 1;
        Save();
        Emit("mint", "Nature changed", value.DisplayName(State.Language));
        Changed?.Invoke(this, EventArgs.Empty);
        return value;
    }

    public async Task UpdateAsync(
        long todayTokens,
        string todayDate,
        BurnTier burnTier,
        bool limitWarning,
        bool hasUsageData,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _updateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!State.InstallBaselineSet)
            {
                if (!hasUsageData)
                {
                    DisplayState = CompanionStateKind.Egg;
                    return;
                }

                State.InstallBaselineSet = true;
                State.ClaimedTodayTokens = todayTokens;
                State.LastDate = todayDate;
            }
            else
            {
                if (!string.Equals(todayDate, State.LastDate, StringComparison.Ordinal))
                {
                    State.LastDate = todayDate;
                    State.ClaimedTodayTokens = 0;
                }

                if (todayTokens > State.ClaimedTodayTokens)
                {
                    var delta = todayTokens - State.ClaimedTodayTokens;
                    State.ClaimedTodayTokens = todayTokens;
                    State.UsedSinceInstall += delta;
                    if (State.Active is null)
                    {
                        State.EggUsage += delta;
                    }
                    else
                    {
                        await ApplyUsageAsync(delta, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            if (_eventUntil is { } until && _clock() > until)
            {
                _eventUntil = null;
                JustEvolvedTo = null;
                JustGraduated = null;
            }

            if (State.Active is null && State.InstallBaselineSet)
            {
                await EnsureEggPrefetchAsync(cancellationToken).ConfigureAwait(false);
                if (State.EggUsage >= PokemonBalance.EggHatchThreshold)
                {
                    await HatchIfNeededAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            else if (State.Active is not null && CurrentLine is null)
            {
                await LoadCurrentLineAsync(cancellationToken).ConfigureAwait(false);
            }

            DisplayState = ComputeState(burnTier, limitWarning, hasUsageData, todayTokens);
            Save();
        }
        finally
        {
            _updateGate.Release();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task ApplyUsageAsync(long delta, CancellationToken cancellationToken = default)
    {
        if (State.Active is null) return;
        State.Active.UsedAtStage += delta;
        if (CurrentLine is null)
        {
            Save();
            return;
        }

        for (var guardCount = 0; State.Active is not null && guardCount < 50; guardCount++)
        {
            var active = State.Active;
            var threshold = PokemonBalance.PhaseThreshold(
                active.Rarity,
                active.TotalForms,
                active.StageIndex);
            if (active.UsedAtStage < threshold) break;
            var node = CurrentLine.Tree.Node(active.CurrentID);
            if (node is null) break;
            if (node.Children.Count == 0)
            {
                Graduate();
                break;
            }

            if (active.DittoDisguise is not null && !active.DittoRevealed)
            {
                await RevealDittoAsync(cancellationToken).ConfigureAwait(false);
                break;
            }

            var next = PickNextChild(node, active.BaseID);
            active.PathIDs = active.PathIDs.Take(active.StageIndex + 1)
                .Append(next.SpeciesID)
                .ToList();
            active.StageIndex++;
            active.UsedAtStage -= threshold;
            JustEvolvedTo = CurrentLine.LocalizedName(next.SpeciesID, State.Language);
            _eventUntil = _clock().AddSeconds(4);
            Emit("evolve", "Evolution", $"Evolved into {JustEvolvedTo}");
        }

        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task HatchAsync(int baseId, CancellationToken cancellationToken = default)
    {
        if (_isHatching) return;
        _isHatching = true;
        try
        {
            await HatchCoreAsync(baseId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _isHatching = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public IReadOnlyList<CandyGrant> GrantCandies(
        IEnumerable<CandyWindow> windows,
        bool limitsReady)
    {
        if (!limitsReady) return [];
        var values = windows.ToArray();
        if (!State.CandyFeatureSeeded)
        {
            foreach (var window in values.Where(value => value.Utilization >= 100))
            {
                State.CandyGrantTier[window.Key] = 1;
            }

            State.CandyFeatureSeeded = true;
            Save();
            return [];
        }

        var grants = EvaluateCandyGrants(values, State.CandyGrantTier);
        foreach (var grant in grants)
        {
            State.Inventory[ItemKind.RareCandy.StateKey()] =
                ItemCount(ItemKind.RareCandy) + grant.Count;
            Emit("candy-grant", "Rare Candy", $"{grant.WindowName}: +{grant.Count}");
        }

        Save();
        if (grants.Count != 0) Changed?.Invoke(this, EventArgs.Empty);
        return grants;
    }

    public static IReadOnlyList<CandyGrant> EvaluateCandyGrants(
        IEnumerable<CandyWindow> windows,
        IDictionary<string, int> grantTier)
    {
        var grants = new List<CandyGrant>();
        foreach (var window in windows)
        {
            if (window.Utilization < 100)
            {
                grantTier.Remove(window.Key);
                continue;
            }

            if (grantTier.TryGetValue(window.Key, out var previous) && previous >= 1) continue;
            grantTier[window.Key] = 1;
            grants.Add(new CandyGrant(
                window.Key,
                window.Name,
                window.Kind == WindowClass.Weekly ? RareCandy.WeeklyGrant : 1));
        }

        return grants;
    }

    public static bool RollsShiny(ulong roll, bool charmOwned) =>
        roll % (charmOwned ? ShinyCharm.ShinyDenominator : PokemonOdds.ShinyDenominator) == 0;

    public static bool DittoDisguiseHit(Rarity rarity, int totalForms, ulong roll) =>
        rarity == Rarity.Common && totalForms >= 2 &&
        roll % PokemonOdds.DittoDisguiseDenominator == 0;

    public void SetLanguage(AppLanguage language)
    {
        State.Language = language;
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<IReadOnlyDictionary<int, string>> ResolveDexNamesAsync(
        DexEntry entry,
        CancellationToken cancellationToken = default)
    {
        if (entry.Names is null || entry.Names.Count == 0)
        {
            try
            {
                var line = await _provider.LineAsync(entry.BaseID, cancellationToken)
                    .ConfigureAwait(false);
                entry.Names = entry.ChainOrder
                    .Where(id => line.Names.ContainsKey(id))
                    .ToDictionary(
                        id => id,
                        id => line.Names[id].ToDictionary(pair => pair.Key, pair => pair.Value));
                Save();
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                AppLog.Write($"dex name backfill failed: {exception.Message}");
            }
        }

        return entry.ChainOrder.ToDictionary(
            id => id,
            id => entry.Names is not null && entry.Names.TryGetValue(id, out var names)
                ? State.Language.ResolveName(names) ?? $"#{id}"
                : $"#{id}");
    }

    public void SaveState() => Save();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _updateGate.Dispose();
    }

    public static string DefaultStateFile()
    {
        var overrideDirectory = Environment.GetEnvironmentVariable("PTB_STATE_DIR")?.Trim();
        var directory = string.IsNullOrWhiteSpace(overrideDirectory)
            ? AppPaths.Default.RootDirectory
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(overrideDirectory));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "companion-state.json");
    }

    private async Task EnsureEggPrefetchAsync(CancellationToken cancellationToken)
    {
        if (State.Active is not null || _isHatching || _prefetchInFlight) return;
        _prefetchInFlight = true;
        try
        {
            if (State.PendingHatchID is null)
            {
                State.PendingHatchID = await ChooseBaseAsync(cancellationToken).ConfigureAwait(false);
                if (State.PendingHatchID is null) return;
                Save();
            }

            if (_prefetchedLineId != State.PendingHatchID)
            {
                await _provider.LineAsync(State.PendingHatchID.Value, cancellationToken)
                    .ConfigureAwait(false);
                _prefetchedLineId = State.PendingHatchID;
            }
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            AppLog.Write($"companion prefetch failed: {exception.Message}");
        }
        finally
        {
            _prefetchInFlight = false;
        }
    }

    private async Task HatchIfNeededAsync(CancellationToken cancellationToken)
    {
        if (State.Active is not null || _isHatching ||
            State.EggUsage < PokemonBalance.EggHatchThreshold)
        {
            return;
        }

        _isHatching = true;
        try
        {
            var baseId = State.PendingHatchID ??
                await ChooseBaseAsync(cancellationToken).ConfigureAwait(false);
            if (baseId is null) return;
            State.PendingHatchID = null;
            await HatchCoreAsync(baseId.Value, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _isHatching = false;
        }
    }

    private async Task HatchCoreAsync(int baseId, CancellationToken cancellationToken)
    {
        EvoLine line;
        try
        {
            line = await _provider.LineAsync(baseId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            AppLog.Write($"hatch line fetch failed for {baseId}: {exception.Message}");
            return;
        }

        CurrentLine = line;
        var overflow = Math.Max(0, State.EggUsage - PokemonBalance.EggHatchThreshold);
        State.EggUsage = 0;
        var shiny = RollsShiny(_rng.Next(), OwnsShinyCharm);
        var natures = Enum.GetValues<PokemonNature>();
        var nature = natures[(int)(_rng.Next() % (ulong)natures.Length)];
        int? disguise = null;
        if (_enableDittoRoll && DittoDisguiseHit(line.Rarity, line.TotalForms, _rng.Next()))
        {
            disguise = line.BaseID;
        }

        State.Active = new MonState
        {
            BaseID = line.BaseID,
            PathIDs = [line.BaseID],
            StageIndex = 0,
            UsedAtStage = 0,
            Rarity = line.Rarity,
            TotalForms = line.TotalForms,
            IsShiny = shiny,
            Nature = nature,
            DittoDisguise = disguise,
        };
        JustEvolvedTo = null;
        _eventUntil = _clock().AddSeconds(4);
        DisplayState = CompanionStateKind.LevelUp;
        var name = line.LocalizedName(line.BaseID, State.Language);
        Emit("hatch", shiny && disguise is null ? "Shiny hatch!" : "Hatched!", name);
        AppLog.Write($"hatch: base={line.BaseID} rarity={line.Rarity} shiny={shiny} ditto={disguise is not null}");
        if (overflow > 0) await ApplyUsageAsync(overflow, cancellationToken).ConfigureAwait(false);
        Save();
    }

    private async Task LoadCurrentLineAsync(CancellationToken cancellationToken)
    {
        if (State.Active is null || CurrentLine is not null || _isHatching) return;
        _isHatching = true;
        try
        {
            CurrentLine = await _provider.LineAsync(State.Active.BaseID, cancellationToken)
                .ConfigureAwait(false);
            await ApplyUsageAsync(0, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            AppLog.Write($"active companion line load failed: {exception.Message}");
        }
        finally
        {
            _isHatching = false;
        }
    }

    private async Task RevealDittoAsync(CancellationToken cancellationToken)
    {
        var active = State.Active;
        if (active?.DittoDisguise is null || active.DittoRevealed || _isRevealingDitto) return;
        var threshold = PokemonBalance.PhaseThreshold(
            active.Rarity,
            active.TotalForms,
            0);
        if (active.UsedAtStage < threshold) return;
        _isRevealingDitto = true;
        try
        {
            var ditto = await _provider.LineAsync(PokemonOdds.DittoSpeciesID, cancellationToken)
                .ConfigureAwait(false);
            var carry = Math.Max(0, active.UsedAtStage - threshold);
            var disguise = CurrentLine?.LocalizedName(active.BaseID, State.Language) ?? $"#{active.BaseID}";
            active.BaseID = ditto.BaseID;
            active.PathIDs = [ditto.BaseID];
            active.StageIndex = 0;
            active.Rarity = ditto.Rarity;
            active.TotalForms = ditto.TotalForms;
            active.UsedAtStage = carry;
            active.DittoRevealed = true;
            CurrentLine = ditto;
            _eventUntil = _clock().AddSeconds(5);
            DisplayState = CompanionStateKind.LevelUp;
            Emit("ditto-reveal", "It was Ditto!", disguise);
            AppLog.Write($"ditto reveal: disguise={active.DittoDisguise} shiny={active.IsShiny}");
        }
        finally
        {
            _isRevealingDitto = false;
        }
    }

    private EvoNode PickNextChild(EvoNode node, int baseId)
    {
        var fresh = node.Children.Where(child =>
            child.FinalIDs.Any(final => !State.CollectedFinals.Contains($"{baseId}:{final}")))
            .ToArray();
        var pool = fresh.Length == 0 ? node.Children.ToArray() : fresh;
        return pool[(int)(_rng.Next() % (ulong)pool.Length)];
    }

    private void Graduate()
    {
        if (State.Active is not { } active) return;
        var finalId = active.CurrentID;
        State.CollectedFinals.Add($"{active.BaseID}:{finalId}");
        State.Dex.Add(new DexEntry
        {
            BaseID = active.BaseID,
            FinalID = finalId,
            ChainOrder = [.. active.PathIDs],
            Rarity = active.Rarity,
            CaughtAt = _clock(),
            IsShiny = active.IsShiny,
            Nature = active.Nature,
            Names = CurrentLine?.Names
                .Where(pair => active.PathIDs.Contains(pair.Key))
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.ToDictionary(value => value.Key, value => value.Value)),
        });
        JustGraduated = CurrentLine?.LocalizedName(finalId, State.Language) ?? $"#{finalId}";
        _eventUntil = _clock().AddSeconds(6);
        Emit("graduate", "Graduated!", JustGraduated);
        State.Active = null;
        State.EggUsage = 0;
        State.PendingHatchID = null;
        CurrentLine = null;
    }

    private async Task<int?> ChooseBaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            var index = await _provider.BaseSpeciesIndexAsync(cancellationToken).ConfigureAwait(false);
            if (index.Count != 0)
            {
                var weights = index.Select(entry => State.CollectedFinals.Any(value =>
                        value.StartsWith(entry.Id + ":", StringComparison.Ordinal))
                        ? Math.Max(1, entry.CaptureRate / 2)
                        : Math.Max(1, entry.CaptureRate))
                    .ToArray();
                var remaining = (long)(_rng.Next() % (ulong)weights.Sum());
                for (var indexPosition = 0; indexPosition < weights.Length; indexPosition++)
                {
                    remaining -= weights[indexPosition];
                    if (remaining < 0) return index[indexPosition].Id;
                }

                return index[^1].Id;
            }
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            AppLog.Write($"base index unavailable: {exception.Message}");
        }

        for (var attempt = 0; attempt < 16; attempt++)
        {
            var id = (int)(_rng.Next() % 649) + 1;
            try
            {
                if (await _provider.BaseSpeciesAsync(id, cancellationToken).ConfigureAwait(false) is not null)
                {
                    return id;
                }
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                AppLog.Write($"base REST fallback failed: {exception.Message}");
                return null;
            }
        }

        return null;
    }

    private CompanionStateKind ComputeState(
        BurnTier burnTier,
        bool limitWarning,
        bool hasUsageData,
        long today)
    {
        if (State.Active is null) return CompanionStateKind.Egg;
        if (_eventUntil is { } until && _clock() < until) return CompanionStateKind.LevelUp;
        if (limitWarning) return CompanionStateKind.Tired;
        if (!hasUsageData || today == 0) return CompanionStateKind.Sleep;
        return burnTier switch
        {
            BurnTier.Idle => CompanionStateKind.Idle,
            BurnTier.Normal => CompanionStateKind.Working,
            _ => CompanionStateKind.Focus,
        };
    }

    private CompanionState LoadState()
    {
        try
        {
            if (!File.Exists(_stateFile)) return new CompanionState();
            var state = JsonSerializer.Deserialize<CompanionState>(
                File.ReadAllText(_stateFile),
                JsonOptions);
            if (state?.Active is { PathIDs.Count: 0 }) return new CompanionState();
            return state ?? new CompanionState();
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            AppLog.Write($"companion state load failed: {exception.Message}");
            return new CompanionState();
        }
    }

    private void Save()
    {
        try
        {
            var fullPath = Path.GetFullPath(_stateFile);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temporary = fullPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(State, JsonOptions));
            File.Move(temporary, fullPath, true);
        }
        catch (IOException exception)
        {
            AppLog.Write($"companion state save failed: {exception.Message}");
        }
    }

    private void Emit(string kind, string title, string body) =>
        CompanionEventRaised?.Invoke(new CompanionEvent(kind, title, body));

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new SwiftDateTimeOffsetConverter());
        return options;
    }

    private sealed class SwiftDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
    {
        private static readonly DateTimeOffset Reference =
            new(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                return Reference.AddSeconds(reader.GetDouble());
            }

            if (reader.TokenType == JsonTokenType.String &&
                DateTimeOffset.TryParse(reader.GetString(), out var value))
            {
                return value;
            }

            throw new JsonException("Invalid companion date.");
        }

        public override void Write(
            Utf8JsonWriter writer,
            DateTimeOffset value,
            JsonSerializerOptions options) =>
            writer.WriteNumberValue((value.ToUniversalTime() - Reference).TotalSeconds);
    }
}
