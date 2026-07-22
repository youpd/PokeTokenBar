using System.Text.Json.Serialization;

namespace PokeTokenBar.Core.Companion;

public enum CompanionStateKind { Egg, Idle, Working, Focus, Tired, Sleep, LevelUp }

public enum AppLanguage { Ko, En, Ja }

public static class AppLanguageExtensions
{
    public static AppLanguage SystemDefault
    {
        get
        {
            var code = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return code.Equals("ko", StringComparison.OrdinalIgnoreCase)
                ? AppLanguage.Ko
                : code.Equals("ja", StringComparison.OrdinalIgnoreCase)
                    ? AppLanguage.Ja
                    : AppLanguage.En;
        }
    }

    public static string? ResolveName(
        this AppLanguage language,
        IReadOnlyDictionary<string, string> values)
    {
        var codes = language switch
        {
            AppLanguage.Ko => new[] { "ko" },
            AppLanguage.Ja => new[] { "ja-Hrkt", "ja" },
            _ => new[] { "en" },
        };
        foreach (var code in codes)
        {
            if (values.TryGetValue(code, out var name))
            {
                return name;
            }
        }

        return values.GetValueOrDefault("en");
    }
}

public enum Rarity { Common, Uncommon, Rare, Legendary }

public static class RarityLogic
{
    public static Rarity From(int captureRate, bool isLegendary, bool isMythical) =>
        isLegendary || isMythical
            ? Rarity.Legendary
            : captureRate <= 45
                ? Rarity.Rare
                : captureRate <= 120
                    ? Rarity.Uncommon
                    : Rarity.Common;

    public static int SortRank(this Rarity rarity) => rarity switch
    {
        Rarity.Legendary => 3,
        Rarity.Rare => 2,
        Rarity.Uncommon => 1,
        _ => 0,
    };
}

public static class PokemonBalance
{
    public const long EggHatchThreshold = 5_000_000;

    public static long GraduationTotal(Rarity rarity) => rarity switch
    {
        Rarity.Common => 750_000_000,
        Rarity.Uncommon => 1_875_000_000,
        Rarity.Rare => 3_000_000_000,
        _ => 6_000_000_000,
    };

    public static long PhaseThreshold(Rarity rarity, int totalForms, int stageIndex)
    {
        var count = Math.Max(1, totalForms);
        var index = stageIndex + 1;
        var denominator = count * (count + 1) / 2d;
        return (long)Math.Round(
            GraduationTotal(rarity) * index / denominator,
            MidpointRounding.AwayFromZero);
    }
}

public enum ItemKind { RareCandy, Mint, ShinyCharm }

public static class ItemKindExtensions
{
    public static string StateKey(this ItemKind item) => item switch
    {
        ItemKind.RareCandy => "rareCandy",
        ItemKind.Mint => "mint",
        _ => "shinyCharm",
    };

    public static string? SpriteName(this ItemKind item) => item switch
    {
        ItemKind.RareCandy => "rare-candy",
        ItemKind.ShinyCharm => "shiny-charm",
        _ => null,
    };

    public static string FallbackEmoji(this ItemKind item) => item switch
    {
        ItemKind.RareCandy => "🍬",
        ItemKind.Mint => "🌿",
        _ => "✨",
    };

    public static long ShopPrice(this ItemKind item) => item switch
    {
        ItemKind.RareCandy => RareCandy.Price,
        ItemKind.Mint => Mint.Price,
        _ => ShinyCharm.Price,
    };

    public static bool IsPassive(this ItemKind item) => item == ItemKind.ShinyCharm;
}

public static class RareCandy
{
    public const long Xp = 100_000_000;
    public const int WeeklyGrant = 5;
    public const long Price = 500_000_000;
}

public static class Mint { public const long Price = 100_000_000; }

public static class ShinyCharm
{
    public const long Price = 3_000_000_000;
    public const ulong ShinyDenominator = 48;
}

public enum WindowClass { Session, Weekly }

public sealed record CandyWindow(
    string Key,
    string Name,
    WindowClass Kind,
    double Utilization);

public sealed record CandyGrant(
    string WindowKey,
    string WindowName,
    int Count);

public sealed class EvoNode
{
    public int SpeciesID { get; set; }

    public List<EvoNode> Children { get; set; } = [];

    [JsonIgnore]
    public int Depth => 1 + (Children.Count == 0 ? 0 : Children.Max(child => child.Depth));

    public EvoNode? Node(int id) => SpeciesID == id
        ? this
        : Children.Select(child => child.Node(id)).FirstOrDefault(node => node is not null);

    public IReadOnlyList<int> FinalIDs => Children.Count == 0
        ? [SpeciesID]
        : Children.SelectMany(child => child.FinalIDs).ToArray();
}

public sealed record EvoLine(
    int BaseID,
    EvoNode Tree,
    Rarity Rarity,
    IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>> Names)
{
    public int TotalForms => Tree.Depth;

    public string LocalizedName(int id, AppLanguage language) =>
        Names.TryGetValue(id, out var names)
            ? language.ResolveName(names) ?? $"#{id}"
            : $"#{id}";
}

public enum PokemonNature
{
    Hardy, Lonely, Brave, Adamant, Naughty,
    Bold, Docile, Relaxed, Impish, Lax,
    Timid, Hasty, Serious, Jolly, Naive,
    Modest, Mild, Quiet, Bashful, Rash,
    Calm, Gentle, Sassy, Careful, Quirky,
}

public static class PokemonNatureExtensions
{
    private static readonly string[] Korean =
    [
        "노력", "외로움", "용감", "고집", "개구쟁이", "대담", "온순", "무사태평", "장난꾸러기", "촐랑",
        "겁쟁이", "성급", "성실", "명랑", "천진난만", "조심", "의젓", "냉정", "수줍음", "덜렁",
        "차분", "얌전", "건방", "신중", "변덕",
    ];

    private static readonly string[] Japanese =
    [
        "がんばりや", "さみしがり", "ゆうかん", "いじっぱり", "やんちゃ", "ずぶとい", "すなお", "のんき", "わんぱく", "のうてんき",
        "おくびょう", "せっかち", "まじめ", "ようき", "むじゃき", "ひかえめ", "おっとり", "れいせい", "てれや", "うっかりや",
        "おだやか", "おとなしい", "なまいき", "しんちょう", "きまぐれ",
    ];

    public static string DisplayName(this PokemonNature nature, AppLanguage language) => language switch
    {
        AppLanguage.Ko => Korean[(int)nature],
        AppLanguage.Ja => Japanese[(int)nature],
        _ => nature.ToString(),
    };
}

public static class PokemonOdds
{
    public const ulong ShinyDenominator = 64;
    public const ulong DittoDisguiseDenominator = 128;
    public const int DittoSpeciesID = 132;
}

public sealed class MonState
{
    public int BaseID { get; set; }
    public List<int> PathIDs { get; set; } = [];
    public int StageIndex { get; set; }
    public long UsedAtStage { get; set; }
    public Rarity Rarity { get; set; }
    public int TotalForms { get; set; }
    public bool IsShiny { get; set; }
    public PokemonNature? Nature { get; set; }
    public int? DittoDisguise { get; set; }
    public bool DittoRevealed { get; set; }

    [JsonIgnore]
    public int CurrentID => PathIDs.Count == 0
        ? BaseID
        : PathIDs[Math.Min(StageIndex, PathIDs.Count - 1)];
}

public sealed class DexEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public int BaseID { get; set; }
    public int FinalID { get; set; }
    public List<int> ChainOrder { get; set; } = [];
    public Rarity Rarity { get; set; }
    public DateTimeOffset? CaughtAt { get; set; }
    public bool IsShiny { get; set; }
    public PokemonNature? Nature { get; set; }
    public Dictionary<int, Dictionary<string, string>>? Names { get; set; }
}

public sealed class CompanionState
{
    public bool InstallBaselineSet { get; set; }
    public long UsedSinceInstall { get; set; }
    public long SpentTokens { get; set; }
    public long EggUsage { get; set; }
    public int? PendingHatchID { get; set; }
    public long ClaimedTodayTokens { get; set; }
    public string LastDate { get; set; } = string.Empty;
    public MonState? Active { get; set; }
    public List<DexEntry> Dex { get; set; } = [];
    public HashSet<string> CollectedFinals { get; set; } = [];
    public AppLanguage Language { get; set; } = AppLanguageExtensions.SystemDefault;
    public Dictionary<string, int> Inventory { get; set; } = [];
    public Dictionary<string, int> CandyGrantTier { get; set; } = [];
    public bool CandyFeatureSeeded { get; set; }
}

public sealed record BaseSpecies(int Id, int CaptureRate);

public sealed record CompanionEvent(string Kind, string Title, string Body);

public enum CandyUseResult { Evolved, Graduated, Progressed, Unavailable }

public interface IRng { ulong Next(); }

public sealed class SystemRng : IRng
{
    public ulong Next() => (ulong)Random.Shared.NextInt64(long.MaxValue);
}
