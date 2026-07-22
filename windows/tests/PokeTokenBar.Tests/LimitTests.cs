using System.Net;
using System.Text;
using System.Text.Json;
using PokeTokenBar.Core.Limits;
using PokeTokenBar.Core.Models;
using PokeTokenBar.Core.Usage;
using PokeTokenBar.Core.Util;

namespace PokeTokenBar.Tests;

public sealed class LimitTests
{
    [Fact]
    public void ClaudeLegacyAndScopedResponseParsesAndDisplaysPlan()
    {
        var value = JsonSerializer.Deserialize<ClaudeLimitStatus>("""
            {"five_hour":{"utilization":12.5,"resets_at":"2026-07-22T12:00:00Z"},
             "seven_day":{"utilization":40},
             "limits":[{"kind":"session","percent":12.5},
                       {"kind":"weekly_scoped","percent":3,"scope":{"model":{"display_name":"Opus 4.8"}}}]}
            """)! with
        {
            SubscriptionType = "max",
            RateLimitTier = "default_claude_max_20x",
        };

        Assert.Equal("Max 20x", value.PlanDisplay);
        Assert.Equal(12.5, value.FiveHour!.Utilization);
        Assert.Single(value.ScopedLimitEntries);
        Assert.Equal("Opus 4.8", value.ScopedLimitEntries[0].DisplayName);
    }

    [Fact]
    public void ClaudeNewOnlyResponseKeepsAllLimitEntries()
    {
        var value = JsonSerializer.Deserialize<ClaudeLimitStatus>("""
            {"limits":[{"kind":"session","percent":22},{"kind":"weekly_all","percent":33}]}
            """)!;

        Assert.Equal(2, value.ScopedLimitEntries.Count);
    }

    [Fact]
    public void CodexBucketsAreStableSortedDeduplicatedAndIncludePersonalPercent()
    {
        var value = JsonSerializer.Deserialize<CodexRateLimitsResult>("""
            {"rateLimits":{"limitId":"codex","primary":{"usedPercent":4}},
             "rateLimitsByLimitId":{
               "z":{"limitId":"z","limitName":"Spark","primary":{"usedPercent":80},
                    "individualLimit":{"limit":100,"remainingPercent":25,"used":75}},
               "codex":{"limitId":"codex","primary":{"usedPercent":4}}}}
            """)!;

        Assert.Equal(2, value.Snapshots.Count);
        Assert.Equal("codex", value.Snapshots[0].LimitId);
        Assert.Equal("z", value.Snapshots[1].LimitId);
        Assert.Equal(75, value.Snapshots[1].IndividualLimit!.UsedPercent);
        Assert.Equal(80, value.MaxPrimaryUsedPercent);
    }

    [Fact]
    public void LimitAlertFiresAtWarningEdgeOnly()
    {
        var tiers = new Dictionary<string, int>();
        var first = LimitLogic.EvaluateLimitAlerts(
            [new("claude.fiveHour", "5h", 80)], 80, 95, tiers);
        var repeated = LimitLogic.EvaluateLimitAlerts(
            [new("claude.fiveHour", "5h", 81)], 80, 95, tiers);

        Assert.Single(first);
        Assert.False(first[0].IsCritical);
        Assert.Empty(repeated);
    }

    [Fact]
    public void LimitAlertEscalatesFromWarningToCritical()
    {
        var tiers = new Dictionary<string, int>();
        LimitLogic.EvaluateLimitAlerts([new("key", "5h", 80)], 80, 95, tiers);

        var result = LimitLogic.EvaluateLimitAlerts([new("key", "5h", 95)], 80, 95, tiers);

        Assert.Single(result);
        Assert.True(result[0].IsCritical);
    }

    [Fact]
    public void LimitAlertDoesNotRepeatAtSameCriticalTier()
    {
        var tiers = new Dictionary<string, int>();
        LimitLogic.EvaluateLimitAlerts([new("key", "5h", 99)], 80, 95, tiers);

        var result = LimitLogic.EvaluateLimitAlerts([new("key", "5h", 100)], 80, 95, tiers);

        Assert.Empty(result);
    }

    [Fact]
    public void LimitAlertRearmsOnlyAfterFallingBelowWarning()
    {
        var tiers = new Dictionary<string, int>();
        LimitLogic.EvaluateLimitAlerts([new("key", "5h", 96)], 80, 95, tiers);
        LimitLogic.EvaluateLimitAlerts([new("key", "5h", 79)], 80, 95, tiers);

        var result = LimitLogic.EvaluateLimitAlerts([new("key", "5h", 80)], 80, 95, tiers);

        Assert.Single(result);
        Assert.False(result[0].IsCritical);
    }

    [Fact]
    public void LimitAlertIdentityDoesNotDependOnResetDate()
    {
        var tiers = new Dictionary<string, int>();
        LimitLogic.EvaluateLimitAlerts([new("stable", "5h", 85)], 80, 95, tiers);

        var result = LimitLogic.EvaluateLimitAlerts([new("stable", "5h", 90)], 80, 95, tiers);

        Assert.Empty(result);
        Assert.Equal(1, tiers["stable"]);
    }

    [Fact]
    public void ForecastRequiresRealBurnAndReportsBeforeReset()
    {
        var now = new DateTimeOffset(2026, 7, 22, 0, 0, 0, TimeSpan.Zero);
        var limits = new ClaudeLimitStatus(
            new LimitWindow(50, now.AddHours(4).ToString("O")),
            null, null, null, null);
        var block = new BlockUsage(
            "block", now, now.AddHours(5), true, 5_000_000, 0, 100_000);

        var result = LimitLogic.Forecast(limits, block, now);

        Assert.NotNull(result);
        Assert.True(result!.BeforeReset);
        Assert.Equal(now.AddMinutes(50), result.DepletionDate);
        Assert.Null(LimitLogic.Forecast(
            limits,
            block with { TokensPerMinute = 9_999 },
            now));
    }

    [Fact]
    public async Task TrayLimitHidesProviderUnusedTodayAndShowsUsedToday()
    {
        var today = "2026-07-22";
        var now = new DateTimeOffset(2026, 7, 22, 3, 0, 0, TimeSpan.Zero);
        var usage = new FakeUsageProvider("claude_code", null);
        var limits = new FakeClaudeLimitsProvider(new ClaudeLimitStatus(
            new LimitWindow(42, null), null, null, null, null));
        using var store = new UsageStore(
            [usage],
            () => now,
            TimeZoneInfo.Utc,
            settings: new AppSettings(),
            claudeLimitsProvider: limits);

        await store.RefreshAsync(false, TestContext.Current.CancellationToken);
        Assert.Null(store.MenuLimitLine);

        usage.Today = new DailyUsage(today, 1, 0, 0, 0, 1, 0);
        await store.RefreshAsync(false, TestContext.Current.CancellationToken);
        Assert.Equal("Claude 42%", store.MenuLimitLine);
    }

    [Fact]
    public void ProcessRunnerNeverUsesShellWindowAndRejectsPowerShellShim()
    {
        var info = ProcessRunner.BuildCodexStartInfo("C:\\tools\\codex.cmd");

        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
        Assert.Equal(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe", info.FileName);
        Assert.StartsWith("/d /s /c", info.Arguments);
        Assert.Throws<NotSupportedException>(() =>
            ProcessRunner.BuildCodexStartInfo("C:\\tools\\codex.ps1"));
    }

    [Fact]
    public void ProcessRunnerScansNoiseAndReturnsOnlyIdOneResult()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "stdout.txt");
        File.WriteAllText(path, "log line\n{\"id\":0,\"result\":{}}\n" +
            "{\"method\":\"notice\"}\n{\"id\":1,\"result\":{\"ok\":true}}\n");

        var result = ProcessRunner.TryReadResponse(path);

        Assert.True(result!.Value.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task ClaudeProviderReadsMillisecondExpiryAndRequiredHeaders()
    {
        using var temporary = new TemporaryDirectory();
        var now = new DateTimeOffset(2026, 7, 22, 0, 0, 0, TimeSpan.Zero);
        var credentials = Path.Combine(temporary.Path, ".credentials.json");
        File.WriteAllText(credentials, JsonSerializer.Serialize(new
        {
            claudeAiOauth = new
            {
                accessToken = "secret-token",
                expiresAt = now.AddHours(1).ToUnixTimeMilliseconds(),
                subscriptionType = "max",
                rateLimitTier = "default_claude_max_20x",
            },
        }));
        HttpRequestMessage? captured = null;
        var handler = new DelegateHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"five_hour\":{\"utilization\":12}}"),
            };
        });
        using var provider = new ClaudeLimitsProvider(
            credentials,
            new HttpClient(handler),
            () => now);

        var result = await provider.FetchAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Max 20x", result.PlanDisplay);
        Assert.Equal("Bearer", captured!.Headers.Authorization!.Scheme);
        Assert.Equal("secret-token", captured.Headers.Authorization.Parameter);
        Assert.Contains("oauth-2025-04-20", captured.Headers.GetValues("anthropic-beta"));
    }

    [Fact]
    public async Task ClaudeProviderRetriesAuthOnceThenMarksExpired()
    {
        using var temporary = new TemporaryDirectory();
        var now = DateTimeOffset.UtcNow;
        var path = Path.Combine(temporary.Path, ".credentials.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            claudeAiOauth = new { accessToken = "secret", expiresAt = now.AddHours(1).ToUnixTimeSeconds() },
        }));
        var calls = 0;
        var handler = new DelegateHandler(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        });
        using var provider = new ClaudeLimitsProvider(path, new HttpClient(handler), () => now);

        var exception = await Assert.ThrowsAsync<ClaudeLimitsException>(() =>
            provider.FetchAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(2, calls);
        Assert.True(exception.IsAuthenticationExpired);
    }

    [Fact]
    public async Task ClaudeProviderUsesRetryAfterAndSkipsDuringBackoff()
    {
        using var temporary = new TemporaryDirectory();
        var now = DateTimeOffset.UtcNow;
        var path = Path.Combine(temporary.Path, ".credentials.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            claudeAiOauth = new { accessToken = "secret", expiresAt = now.AddHours(1).ToUnixTimeSeconds() },
        }));
        var calls = 0;
        var handler = new DelegateHandler(_ =>
        {
            calls++;
            var response = new HttpResponseMessage((HttpStatusCode)429);
            response.Headers.TryAddWithoutValidation("Retry-After", "90");
            return response;
        });
        using var provider = new ClaudeLimitsProvider(path, new HttpClient(handler), () => now);

        var first = await Assert.ThrowsAsync<ClaudeLimitsException>(() =>
            provider.FetchAsync(cancellationToken: TestContext.Current.CancellationToken));
        var second = await Assert.ThrowsAsync<ClaudeLimitsException>(() =>
            provider.FetchAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromSeconds(90), first.RetryAfter);
        Assert.Equal(1, calls);
        Assert.NotNull(second.RetryAfter);
    }

    [Fact]
    public async Task CodexStatusIgnoresUnrelatedOpenAiIncidents()
    {
        var endpoints = new Dictionary<string, Uri>(StringComparer.Ordinal)
        {
            ["claude_code"] = new("https://status.test/anthropic"),
            ["codex"] = new("https://status.test/openai-components"),
        };
        var handler = new DelegateHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                request.RequestUri!.AbsolutePath.Contains("anthropic", StringComparison.Ordinal)
                    ? "{\"status\":{\"indicator\":\"none\",\"description\":\"All Systems Operational\"}}"
                    : """
                      {"components":[
                        {"name":"Image Generation","status":"partial_outage"},
                        {"name":"Codex API","status":"operational"},
                        {"name":"Codex Web","status":"operational"},
                        {"name":"CLI","status":"operational"},
                        {"name":"VS Code extension","status":"operational"}]}
                      """,
                Encoding.UTF8,
                "application/json"),
        });
        using var provider = new StatuspageProvider(new HttpClient(handler), endpoints);

        var result = await provider.FetchAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ProviderStatusIndicator.None, result["claude_code"].Indicator);
        Assert.Equal(ProviderStatusIndicator.None, result["codex"].Indicator);
        Assert.Equal("Operational", result["codex"].Description);
    }

    [Fact]
    public async Task CodexStatusReportsOnlyAffectedCodexComponents()
    {
        var endpoints = new Dictionary<string, Uri>(StringComparer.Ordinal)
        {
            ["codex"] = new("https://status.test/openai-components"),
        };
        var handler = new DelegateHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"components":[
                  {"name":"Codex API","status":"degraded_performance"},
                  {"name":"CLI","status":"partial_outage"},
                  {"name":"File uploads","status":"major_outage"}]}
                """, Encoding.UTF8, "application/json"),
        });
        using var provider = new StatuspageProvider(new HttpClient(handler), endpoints);

        var result = await provider.FetchAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ProviderStatusIndicator.Major, result["codex"].Indicator);
        Assert.Contains("Codex API: degraded performance", result["codex"].Description);
        Assert.Contains("CLI: partial outage", result["codex"].Description);
        Assert.DoesNotContain("File uploads", result["codex"].Description);
    }

    [Fact]
    public void BinaryLocatorHonorsManualPathAndNullCacheTtl()
    {
        using var temporary = new TemporaryDirectory();
        var manual = Path.Combine(temporary.Path, "codex.cmd");
        var exists = false;
        var now = DateTimeOffset.UtcNow;
        var locator = new BinaryLocator(() => now, path => exists && path == manual);

        Assert.Null(locator.LocateCodex(manual));
        exists = true;
        Assert.Null(locator.LocateCodex(manual));
        now = now.AddMinutes(11);
        Assert.Equal(manual, locator.LocateCodex(manual));
    }

    [Fact]
    public async Task OptionalLiveCodexAppServerProbe()
    {
        if (Environment.GetEnvironmentVariable("PTB_RUN_CODEX_PROBE") != "1")
        {
            return;
        }

        var binary = new BinaryLocator().LocateCodex();
        Assert.False(string.IsNullOrWhiteSpace(binary));
        var response = await new ProcessRunner().RunCodexRateLimitsAsync(
            binary!,
            "0.1.0-test",
            TestContext.Current.CancellationToken);
        var limits = response.Deserialize<CodexRateLimitsResult>();

        Assert.NotNull(limits);
        Assert.NotEmpty(limits!.Snapshots);
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, HttpResponseMessage> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(callback(request));
    }

    private sealed class FakeUsageProvider : IUsageProvider
    {
        public FakeUsageProvider(string id, DailyUsage? today)
        {
            Id = id;
            Today = today;
        }

        public string Id { get; }

        public string DisplayName => Id;

        public DailyUsage? Today { get; set; }

        public Task<DailyUsage?> FetchDailyAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Today);

        public Task<ProviderEnrichment> FetchEnrichmentAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderEnrichment());
    }

    private sealed class FakeClaudeLimitsProvider(ClaudeLimitStatus value) : IClaudeLimitsProvider
    {
        public Task<ClaudeLimitStatus> FetchAsync(
            bool forceCredentialReload = false,
            CancellationToken cancellationToken = default) => Task.FromResult(value);
    }
}
