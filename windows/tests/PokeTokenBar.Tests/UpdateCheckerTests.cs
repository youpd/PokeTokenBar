using System.Net;
using System.Text;
using PokeTokenBar.Core.Companion;
using PokeTokenBar.Core.Util;

namespace PokeTokenBar.Tests;

public sealed class UpdateCheckerTests
{
    [Fact]
    public async Task SelectsHighestStableWindowsReleaseWithSafePage()
    {
        var json = """
            [
              {"tag_name":"v99.0.0","html_url":"https://github.com/youpd/PokeTokenBar/releases/tag/v99.0.0","draft":false,"prerelease":false},
              {"tag_name":"win-v9.bad.0","html_url":"https://github.com/youpd/PokeTokenBar/releases/tag/win-v9.bad.0","draft":false,"prerelease":false},
              {"tag_name":"win-v3.0.0","html_url":"https://evil.example/release","draft":false,"prerelease":false},
              {"tag_name":"win-v2.0.0","html_url":"https://github.com/youpd/PokeTokenBar/releases/tag/win-v2.0.0","draft":false,"prerelease":true},
              {"tag_name":"win-v1.10.0","html_url":"https://github.com/youpd/PokeTokenBar/releases/tag/win-v1.10.0","draft":false,"prerelease":false},
              {"tag_name":"win-v1.2.0","html_url":"https://github.com/youpd/PokeTokenBar/releases/tag/win-v1.2.0","draft":false,"prerelease":false}
            ]
            """;
        using var client = new HttpClient(new JsonHandler(json));
        using var checker = new UpdateChecker("1.1.0", client);

        var update = await checker.CheckAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(update);
        Assert.Equal("1.10.0", update.Version);
        Assert.Equal("github.com", update.PageUri.Host);
    }

    [Fact]
    public async Task HonorsSkipAndMinimumInterval()
    {
        var handler = new JsonHandler("""
            [{"tag_name":"win-v0.2.0","html_url":"https://github.com/youpd/PokeTokenBar/releases/tag/win-v0.2.0","draft":false,"prerelease":false}]
            """);
        using var client = new HttpClient(handler);
        using var checker = new UpdateChecker("0.1.0", client);

        var first = await checker.CheckAsync(cancellationToken: TestContext.Current.CancellationToken);
        var cached = await checker.CheckAsync(cancellationToken: TestContext.Current.CancellationToken);
        var skipped = await checker.CheckAsync("0.2.0", TimeSpan.Zero, TestContext.Current.CancellationToken);

        Assert.NotNull(first);
        Assert.Same(first, cached);
        Assert.Null(skipped);
        Assert.Equal(2, handler.RequestCount);
    }

    [Theory]
    [InlineData("1.2.0", "1.1.9", true)]
    [InlineData("1.2.0", "1.2.0", false)]
    [InlineData("9.bad.0", "1.2.0", false)]
    public void NumericVersionComparisonRejectsMalformedTags(
        string candidate,
        string current,
        bool expected) =>
        Assert.Equal(expected, UpdateChecker.IsNewer(candidate, current));

    [Fact]
    public void LocalizationAndSupportIssueCoverAllLanguagesAndSafeDiagnostics()
    {
        Assert.Equal("홈", new L(AppLanguage.Ko).Home);
        Assert.Equal("Home", new L(AppLanguage.En).Home);
        Assert.Equal("ホーム", new L(AppLanguage.Ja).Home);
        Assert.Equal(
            "오늘 1.2M · API 환산 예상비용 $4.40 (구독제 상태)",
            new L(AppLanguage.Ko).CodexTodayProvider(1_200_000, 4.4));
        Assert.Equal(
            "Today 1.2M · API estimate $4.40 (subscription plan)",
            new L(AppLanguage.En).CodexTodayProvider(1_200_000, 4.4));
        Assert.Equal(
            "今日 1.2M · API換算見積 $4.40（サブスク利用）",
            new L(AppLanguage.Ja).CodexTodayProvider(1_200_000, 4.4));

        var issue = SupportIssue.Build("0.1.0+test", "Windows Test + x64");
        Assert.Equal(Uri.UriSchemeHttps, issue.Scheme);
        Assert.Equal("github.com", issue.Host);
        Assert.Equal("/youpd/PokeTokenBar/issues/new", issue.AbsolutePath);
        Assert.Contains("0.1.0%2Btest", issue.OriginalString, StringComparison.Ordinal);
        Assert.Contains("Windows%20Test%20%2B%20x64", issue.OriginalString, StringComparison.Ordinal);
        Assert.DoesNotContain("mailto:", issue.OriginalString, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("app.log", issue.OriginalString, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
