using System.Net;
using System.Text;
using System.Text.Json;
using PokeTokenBar.Core.Companion;
using PokeTokenBar.Core.Models;
using PokeTokenBar.Core.Poke;

namespace PokeTokenBar.Tests;

public sealed class CompanionTests
{
    [Theory]
    [InlineData(Rarity.Common, 750_000_000)]
    [InlineData(Rarity.Uncommon, 1_875_000_000)]
    [InlineData(Rarity.Rare, 3_000_000_000)]
    [InlineData(Rarity.Legendary, 6_000_000_000)]
    public void PhaseThresholdsAlwaysSumToGraduationTotal(Rarity rarity, long total)
    {
        var values = Enumerable.Range(0, 3)
            .Select(index => PokemonBalance.PhaseThreshold(rarity, 3, index))
            .ToArray();

        Assert.Equal(total, values.Sum());
        Assert.True(values[0] < values[1]);
        Assert.True(values[1] < values[2]);
    }

    [Theory]
    [InlineData(255, false, false, Rarity.Common)]
    [InlineData(120, false, false, Rarity.Uncommon)]
    [InlineData(45, false, false, Rarity.Rare)]
    [InlineData(255, true, false, Rarity.Legendary)]
    public void RarityMatchesCaptureRateContract(
        int captureRate,
        bool legendary,
        bool mythical,
        Rarity expected) =>
        Assert.Equal(expected, RarityLogic.From(captureRate, legendary, mythical));

    [Fact]
    public void CompanionStateLoadsMacSchemaAndWritesCompatibleCamelCase()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "companion-state.json");
        File.WriteAllText(path, """
            {"installBaselineSet":true,"usedSinceInstall":123,"spentTokens":5,"eggUsage":0,
             "claimedTodayTokens":10,"lastDate":"2026-07-22",
             "active":{"baseID":1,"pathIDs":[1],"stageIndex":0,"usedAtStage":9,
                       "rarity":"common","totalForms":3,"isShiny":true,"nature":"jolly"},
             "dex":[{"id":"d","baseID":1,"finalID":3,"chainOrder":[1,2,3],
                     "rarity":"common","caughtAt":804556800,"isShiny":false}],
             "collectedFinals":["1:3"],"language":"ko","inventory":{"rareCandy":2}}
            """);
        using var store = new CompanionStore(new FakePokeProvider(), stateFile: path);

        Assert.Equal(123, store.State.UsedSinceInstall);
        Assert.True(store.State.Active!.IsShiny);
        Assert.Equal(PokemonNature.Jolly, store.State.Active.Nature);
        Assert.Equal(AppLanguage.Ko, store.State.Language);
        Assert.Equal(2, store.ItemCount(ItemKind.RareCandy));

        store.SaveState();
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.True(document.RootElement.GetProperty("installBaselineSet").GetBoolean());
        Assert.Equal("common", document.RootElement.GetProperty("active").GetProperty("rarity").GetString());
        Assert.Equal("jolly", document.RootElement.GetProperty("active").GetProperty("nature").GetString());
    }

    [Fact]
    public void EmptyPathIdsRejectsDamagedStateAsWhole()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "companion-state.json");
        File.WriteAllText(path, """
            {"installBaselineSet":true,"active":{"baseID":1,"pathIDs":[],"stageIndex":0,
             "usedAtStage":0,"rarity":"common","totalForms":3}}
            """);

        using var store = new CompanionStore(new FakePokeProvider(), stateFile: path);

        Assert.False(store.State.InstallBaselineSet);
        Assert.Null(store.State.Active);
    }

    [Fact]
    public async Task FirstRealUsageSeedsBaselineWithoutRetroactiveGrowth()
    {
        using var temporary = new TemporaryDirectory();
        using var store = new CompanionStore(
            new FakePokeProvider(),
            stateFile: Path.Combine(temporary.Path, "state.json"));

        await store.UpdateAsync(100_000_000, "2026-07-22", BurnTier.Idle, false, true,
            TestContext.Current.CancellationToken);

        Assert.True(store.State.InstallBaselineSet);
        Assert.Equal(0, store.State.UsedSinceInstall);
        Assert.Equal(0, store.State.EggUsage);
        Assert.Equal(100_000_000, store.State.ClaimedTodayTokens);
    }

    [Fact]
    public async Task DateRolloverAccruesNewDayWithoutLosingClaimedState()
    {
        using var temporary = new TemporaryDirectory();
        using var store = new CompanionStore(
            new FakePokeProvider(),
            stateFile: Path.Combine(temporary.Path, "state.json"));
        await store.UpdateAsync(100, "2026-07-21", BurnTier.Idle, false, true,
            TestContext.Current.CancellationToken);

        await store.UpdateAsync(25, "2026-07-22", BurnTier.Idle, false, true,
            TestContext.Current.CancellationToken);

        Assert.Equal(25, store.State.UsedSinceInstall);
        Assert.Equal(25, store.State.EggUsage);
        Assert.Equal(25, store.State.ClaimedTodayTokens);
    }

    [Fact]
    public async Task EggHatchesAndCarriesOverflowIntoGrowth()
    {
        using var temporary = new TemporaryDirectory();
        var provider = new FakePokeProvider();
        using var store = new CompanionStore(
            provider,
            stateFile: Path.Combine(temporary.Path, "state.json"),
            rng: new SequenceRng(1, 0, 1),
            enableDittoRoll: false);
        store.State.InstallBaselineSet = true;
        store.State.LastDate = "2026-07-22";

        await store.UpdateAsync(
            PokemonBalance.EggHatchThreshold + 10_000,
            "2026-07-22",
            BurnTier.Normal,
            false,
            true,
            TestContext.Current.CancellationToken);

        Assert.NotNull(store.State.Active);
        Assert.Equal(1, store.State.Active!.BaseID);
        Assert.Equal(10_000, store.State.Active.UsedAtStage);
        Assert.Equal(0, store.State.EggUsage);
    }

    [Fact]
    public async Task FullEvolutionCycleGraduatesToDexAndStartsNewEgg()
    {
        using var temporary = new TemporaryDirectory();
        var provider = new FakePokeProvider();
        using var store = new CompanionStore(
            provider,
            stateFile: Path.Combine(temporary.Path, "state.json"),
            rng: new SequenceRng(0, 0, 0),
            enableDittoRoll: false);
        store.State.InstallBaselineSet = true;
        store.State.LastDate = "2026-07-22";
        store.State.Active = ActiveBulbasaur();

        await store.UpdateAsync(
            PokemonBalance.GraduationTotal(Rarity.Common),
            "2026-07-22",
            BurnTier.Blazing,
            false,
            true,
            TestContext.Current.CancellationToken);

        Assert.Null(store.State.Active);
        var entry = Assert.Single(store.State.Dex);
        Assert.Equal([1, 2, 3], entry.ChainOrder);
        Assert.Equal("1:3", Assert.Single(store.State.CollectedFinals));
        Assert.Equal("Venusaur", entry.Names![3]["en"]);
        Assert.Equal(0, store.State.EggUsage);
    }

    [Fact]
    public async Task UsageAccruesWhileLineUnloadedThenEvolvesOnLoad()
    {
        using var temporary = new TemporaryDirectory();
        var provider = new FakePokeProvider { FailLine = true };
        using var store = new CompanionStore(
            provider,
            stateFile: Path.Combine(temporary.Path, "state.json"));
        store.State.InstallBaselineSet = true;
        store.State.LastDate = "2026-07-22";
        store.State.Active = ActiveBulbasaur();

        await store.UpdateAsync(200_000_000, "2026-07-22", BurnTier.Fast, false, true,
            TestContext.Current.CancellationToken);
        Assert.Equal(200_000_000, store.State.Active!.UsedAtStage);
        Assert.Equal(0, store.State.Active.StageIndex);

        provider.FailLine = false;
        await store.UpdateAsync(200_000_000, "2026-07-22", BurnTier.Fast, false, true,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, store.State.Active!.StageIndex);
        Assert.Equal(75_000_000, store.State.Active.UsedAtStage);
    }

    [Fact]
    public async Task RareCandyProgressesWithoutChangingRealUsageLedger()
    {
        using var temporary = new TemporaryDirectory();
        using var store = new CompanionStore(
            new FakePokeProvider(),
            stateFile: Path.Combine(temporary.Path, "state.json"));
        store.State.InstallBaselineSet = true;
        store.State.Active = ActiveBulbasaur();
        store.State.Inventory[ItemKind.RareCandy.StateKey()] = 1;
        await store.UpdateAsync(0, "2026-07-22", BurnTier.Idle, false, true,
            TestContext.Current.CancellationToken);

        var result = await store.UseRareCandyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CandyUseResult.Progressed, result);
        Assert.Equal(RareCandy.Xp, store.State.Active!.UsedAtStage);
        Assert.Equal(0, store.State.UsedSinceInstall);
        Assert.Equal(0, store.ItemCount(ItemKind.RareCandy));
    }

    [Fact]
    public async Task MintAlwaysChangesNatureAndConsumesOne()
    {
        using var temporary = new TemporaryDirectory();
        using var store = new CompanionStore(
            new FakePokeProvider(),
            stateFile: Path.Combine(temporary.Path, "state.json"),
            rng: new SequenceRng(0));
        store.State.Active = ActiveBulbasaur();
        store.State.Active.Nature = PokemonNature.Hardy;
        store.State.Inventory[ItemKind.Mint.StateKey()] = 1;

        var value = store.UseMint();

        Assert.NotNull(value);
        Assert.NotEqual(PokemonNature.Hardy, value);
        Assert.Equal(0, store.ItemCount(ItemKind.Mint));
    }

    [Fact]
    public void ShopUsesSeparateSpendLedgerAndPassiveItemCannotBeRepurchased()
    {
        using var temporary = new TemporaryDirectory();
        using var store = new CompanionStore(
            new FakePokeProvider(),
            stateFile: Path.Combine(temporary.Path, "state.json"));
        store.State.UsedSinceInstall = 4_000_000_000;

        Assert.True(store.Buy(ItemKind.ShinyCharm));
        Assert.Equal(4_000_000_000, store.State.UsedSinceInstall);
        Assert.Equal(1_000_000_000, store.AvailableTokens);
        Assert.True(store.OwnsShinyCharm);
        Assert.False(store.Buy(ItemKind.ShinyCharm));
    }

    [Fact]
    public void ShinyAndDittoOddsUseFixedDivisorsAndEligibility()
    {
        Assert.True(CompanionStore.RollsShiny(64, false));
        Assert.False(CompanionStore.RollsShiny(48, false));
        Assert.True(CompanionStore.RollsShiny(48, true));
        Assert.True(CompanionStore.DittoDisguiseHit(Rarity.Common, 2, 128));
        Assert.False(CompanionStore.DittoDisguiseHit(Rarity.Rare, 2, 128));
        Assert.False(CompanionStore.DittoDisguiseHit(Rarity.Common, 1, 128));
    }

    [Fact]
    public async Task DittoDisguiseRevealsAtFirstEvolutionThresholdAndKeepsIdentity()
    {
        using var temporary = new TemporaryDirectory();
        var provider = new FakePokeProvider();
        using var store = new CompanionStore(
            provider,
            stateFile: Path.Combine(temporary.Path, "state.json"),
            rng: new SequenceRng(1, 0, 128),
            enableDittoRoll: true);

        await store.HatchAsync(1, TestContext.Current.CancellationToken);
        Assert.Equal(1, store.State.Active!.DittoDisguise);
        var nature = store.State.Active.Nature;
        await store.ApplyUsageAsync(
            PokemonBalance.PhaseThreshold(Rarity.Common, 3, 0) + 5,
            TestContext.Current.CancellationToken);

        Assert.Equal(PokemonOdds.DittoSpeciesID, store.State.Active!.BaseID);
        Assert.True(store.State.Active.DittoRevealed);
        Assert.Equal(5, store.State.Active.UsedAtStage);
        Assert.Equal(nature, store.State.Active.Nature);
    }

    [Fact]
    public void CandyGrantIsEdgeTriggeredWeeklyFiveAndRearmsBelowHundred()
    {
        var tiers = new Dictionary<string, int>();
        var first = CompanionStore.EvaluateCandyGrants(
            [new("weekly", "Weekly", WindowClass.Weekly, 100)], tiers);
        var repeated = CompanionStore.EvaluateCandyGrants(
            [new("weekly", "Weekly", WindowClass.Weekly, 101)], tiers);
        CompanionStore.EvaluateCandyGrants(
            [new("weekly", "Weekly", WindowClass.Weekly, 99)], tiers);
        var rearmed = CompanionStore.EvaluateCandyGrants(
            [new("weekly", "Weekly", WindowClass.Weekly, 100)], tiers);

        Assert.Equal(5, Assert.Single(first).Count);
        Assert.Empty(repeated);
        Assert.Equal(5, Assert.Single(rearmed).Count);
    }

    [Fact]
    public void CandyGrantSessionIsOneAndStableKeyIsIdentity()
    {
        var tiers = new Dictionary<string, int>();
        var grant = CompanionStore.EvaluateCandyGrants(
            [new("codex.codex.primary", "Codex", WindowClass.Session, 100)], tiers);

        Assert.Equal(1, Assert.Single(grant).Count);
        Assert.Equal(1, tiers["codex.codex.primary"]);
    }

    [Fact]
    public void PokeApiRejectsUntrustedEvolutionChainUrls()
    {
        Assert.True(PokeApiClient.TryValidateChainUrl(
            "https://pokeapi.co/api/v2/evolution-chain/1/",
            out _));
        Assert.False(PokeApiClient.TryValidateChainUrl("http://pokeapi.co/x", out _));
        Assert.False(PokeApiClient.TryValidateChainUrl("https://evil.example/x", out _));
        Assert.Equal(42, PokeApiClient.SpeciesIdFromUrl(
            "https://pokeapi.co/api/v2/pokemon-species/42/"));
    }

    [Fact]
    public async Task PokeApiBuildsEvolutionTreeNamesAndRarity()
    {
        using var temporary = new TemporaryDirectory();
        var handler = new RoutingHandler(request =>
        {
            var id = request.RequestUri!.AbsolutePath.EndsWith("/1") ? 1 : 2;
            if (request.RequestUri.AbsolutePath.Contains("evolution-chain"))
            {
                return Json("""
                    {"chain":{"species":{"name":"one","url":"https://pokeapi.co/api/v2/pokemon-species/1/"},
                    "evolves_to":[{"species":{"name":"two","url":"https://pokeapi.co/api/v2/pokemon-species/2/"},"evolves_to":[]}]}}
                    """);
            }

            object? evolvesFrom = id == 1 ? null : new { name = "one", url = (string?)null };
            return Json(JsonSerializer.Serialize(new
            {
                capture_rate = id == 1 ? 45 : 100,
                is_legendary = false,
                is_mythical = false,
                names = new[]
                {
                    new
                    {
                        name = $"Name {id}",
                        language = new { name = "en", url = (string?)null },
                    },
                },
                evolution_chain = new { url = "https://pokeapi.co/api/v2/evolution-chain/1/" },
                evolves_from_species = evolvesFrom,
            }));
        });
        using var client = new PokeApiClient(
            Path.Combine(temporary.Path, "index.json"),
            new HttpClient(handler));

        var line = await client.LineAsync(1, TestContext.Current.CancellationToken);

        Assert.Equal(Rarity.Rare, line.Rarity);
        Assert.Equal(2, line.TotalForms);
        Assert.Equal(2, line.Tree.Children[0].SpeciesID);
        Assert.Equal("Name 2", line.LocalizedName(2, AppLanguage.En));
    }

    [Fact]
    public async Task SpriteStoreUsesExpectedKeysDiskCacheAndLruBound()
    {
        using var temporary = new TemporaryDirectory();
        Uri? eggUri = null;
        var handler = new RoutingHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/egg.png", StringComparison.Ordinal) == true)
            {
                eggUri = request.RequestUri;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3]),
            };
        });
        using var store = new SpriteStore(temporary.Path, new HttpClient(handler));

        for (var id = 1; id <= 30; id++)
        {
            Assert.NotNull(await store.GetSpeciesAsync(
                id,
                animated: false,
                cancellationToken: TestContext.Current.CancellationToken));
        }

        Assert.NotNull(await store.GetEggAsync(TestContext.Current.CancellationToken));

        Assert.Equal("25-sha", SpriteStore.CacheKey(25, animated: true, shiny: true));
        Assert.True(store.MemoryCount <= 24);
        Assert.Equal(
            "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/egg.png",
            eggUri?.AbsoluteUri);
        Assert.True(File.Exists(Path.Combine(temporary.Path, "egg.png")));
        Assert.Equal(
            Path.GetFullPath(Path.Combine(temporary.Path, "egg.png")),
            store.FindCachedEggPath());
        Assert.True(File.Exists(Path.Combine(temporary.Path, "1-s.png")));
        Assert.Equal(
            Path.GetFullPath(Path.Combine(temporary.Path, "1-s.png")),
            store.FindCachedSpeciesPath(1, animated: true, shiny: true));
    }

    private static MonState ActiveBulbasaur() => new()
    {
        BaseID = 1,
        PathIDs = [1],
        StageIndex = 0,
        UsedAtStage = 0,
        Rarity = Rarity.Common,
        TotalForms = 3,
    };

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json"),
    };

    private sealed class RoutingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(callback(request));
    }

    private sealed class SequenceRng(params ulong[] values) : IRng
    {
        private readonly Queue<ulong> _values = new(values);

        public ulong Next() => _values.Count == 0 ? 1 : _values.Dequeue();
    }

    private sealed class FakePokeProvider : IPokeProvider
    {
        public bool FailLine { get; set; }

        public Task<EvoLine> LineAsync(int baseSpeciesId, CancellationToken cancellationToken = default)
        {
            if (FailLine) return Task.FromException<EvoLine>(new IOException("offline"));
            return Task.FromResult(baseSpeciesId == PokemonOdds.DittoSpeciesID
                ? DittoLine()
                : BulbasaurLine());
        }

        public Task<IReadOnlyList<BaseSpecies>> BaseSpeciesIndexAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BaseSpecies>>([new(1, 255)]);

        public Task<BaseSpecies?> BaseSpeciesAsync(
            int id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<BaseSpecies?>(new BaseSpecies(id, 255));

        private static EvoLine BulbasaurLine()
        {
            var root = new EvoNode
            {
                SpeciesID = 1,
                Children =
                [
                    new EvoNode
                    {
                        SpeciesID = 2,
                        Children = [new EvoNode { SpeciesID = 3 }],
                    },
                ],
            };
            return new EvoLine(
                1,
                root,
                Rarity.Common,
                new Dictionary<int, IReadOnlyDictionary<string, string>>
                {
                    [1] = new Dictionary<string, string> { ["en"] = "Bulbasaur", ["ko"] = "이상해씨" },
                    [2] = new Dictionary<string, string> { ["en"] = "Ivysaur", ["ko"] = "이상해풀" },
                    [3] = new Dictionary<string, string> { ["en"] = "Venusaur", ["ko"] = "이상해꽃" },
                });
        }

        private static EvoLine DittoLine() => new(
            PokemonOdds.DittoSpeciesID,
            new EvoNode { SpeciesID = PokemonOdds.DittoSpeciesID },
            Rarity.Rare,
            new Dictionary<int, IReadOnlyDictionary<string, string>>
            {
                [PokemonOdds.DittoSpeciesID] = new Dictionary<string, string>
                {
                    ["en"] = "Ditto",
                    ["ko"] = "메타몽",
                },
            });
    }
}
