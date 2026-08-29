using DeepSeekBalanceWidget.Models;
using DeepSeekBalanceWidget.Services;

namespace DeepSeekBalanceWidget.Tests;

public class CodexUsageParserTests
{
    [Fact]
    public async Task GetUsageAsync_WhenLiveProbeEnabled_ReturnsAuthenticatedUsage()
    {
        if (Environment.GetEnvironmentVariable("RUN_CODEX_LIVE_TEST") != "1")
            return;

        var result = await new CodexAppServerClient(TimeSpan.FromSeconds(30))
            .GetUsageAsync(CancellationToken.None);

        Assert.True(result.IsAvailable, result.Error);
        Assert.NotEmpty(result.Windows);
    }

    [Fact]
    public void Parse_ReadsReturnedWindowsAndConvertsToRemainingPercent()
    {
        const string json = """
        {
          "id": 2,
          "result": {
            "rateLimits": {
              "planType": "plus",
              "primary": {
                "usedPercent": 49,
                "windowDurationMins": 10080,
                "resetsAt": 1786164619
              },
              "secondary": null
            }
          }
        }
        """;

        var result = CodexUsageParser.Parse(json);

        Assert.True(result.IsAvailable);
        Assert.Equal("plus", result.PlanType);
        var window = Assert.Single(result.Windows);
        Assert.Equal(51, window.RemainingPercent);
        Assert.Equal(10080, window.DurationMinutes);
        Assert.NotNull(window.ResetsAt);
    }

    [Fact]
    public void Parse_ClampsBackendPercentBeforeCalculatingRemaining()
    {
        const string json = """
        {
          "result": {
            "rateLimits": {
              "primary": { "usedPercent": 130 },
              "secondary": { "usedPercent": -5 }
            }
          }
        }
        """;

        var result = CodexUsageParser.Parse(json);

        Assert.Equal(0, result.Windows[0].RemainingPercent);
        Assert.Equal(100, result.Windows[1].RemainingPercent);
    }

    [Fact]
    public void Parse_ErrorResponseIsUnavailable()
    {
        var result = CodexUsageParser.Parse(
            """{"id":2,"error":{"message":"Not logged in"}}""");

        Assert.False(result.IsAvailable);
        Assert.Equal("Not logged in", result.Error);
    }

    [Fact]
    public void Parse_MissingWindowsIsUnavailable()
    {
        var result = CodexUsageParser.Parse(
            """{"id":2,"result":{"rateLimits":{"planType":"plus"}}}""");

        Assert.False(result.IsAvailable);
        Assert.Equal("未返回 Codex 用量窗口", result.Error);
    }

    [Theory]
    [InlineData(null, "Codex")]
    [InlineData("", "Codex")]
    [InlineData("plus", "Codex Plus")]
    public void FormatPlan_HandlesMissingAndKnownPlan(string? planType, string expected)
    {
        Assert.Equal(expected, CodexUsageFormatter.FormatPlan(planType));
    }

    [Fact]
    public void FormatReset_HandlesMissingResetTime()
    {
        var window = new CodexUsageWindow(25, 75, 300, null);

        Assert.Equal("重置时间未知", CodexUsageFormatter.FormatReset(window));
    }

    [Theory]
    [InlineData(6, 18, 30, "6 天 18 小时")]
    [InlineData(0, 8, 25, "8 小时 25 分钟")]
    [InlineData(0, 0, 12, "12 分钟")]
    public void FormatCountdown_ShowsRemainingDaysAndHours(
        int days, int hours, int minutes, string expected)
    {
        var now = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.FromHours(8));

        string result = CodexUsageFormatter.FormatCountdown(
            now.AddDays(days).AddHours(hours).AddMinutes(minutes), now);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void FormatCountdown_WhenResetPassed_ShowsImminentReset()
    {
        var now = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.FromHours(8));

        Assert.Equal("即将重置", CodexUsageFormatter.FormatCountdown(now.AddSeconds(-1), now));
        Assert.Equal("--", CodexUsageFormatter.FormatCountdown(null, now));
    }

    [Theory]
    [InlineData(300, "5 小时")]
    [InlineData(10080, "每周")]
    [InlineData(120, "2 小时")]
    [InlineData(45, "45 分钟")]
    public void FormatDuration_UsesReturnedWindowLength(int minutes, string expected)
    {
        Assert.Equal(expected, CodexUsageFormatter.FormatDuration(minutes));
    }

    [Fact]
    public void FormatWindowRow_ShowsRemainingResetTimeAndCountdown()
    {
        var now = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.FromHours(8));
        var fiveHour = new CodexUsageWindow(25, 75, 300, now.AddHours(2).AddMinutes(30));
        var weekly = new CodexUsageWindow(22, 78, 10080, now.AddDays(4).AddHours(12));

        string fiveHourRow = CodexUsageFormatter.FormatWindowRow(fiveHour, now);
        Assert.Contains("5 小时剩余 75%", fiveHourRow);
        Assert.Contains("2 小时 30 分钟后", fiveHourRow);
        Assert.DoesNotContain("重置（", fiveHourRow);

        string weeklyRow = CodexUsageFormatter.FormatWindowRow(weekly, now);
        Assert.Contains("每周剩余 78%", weeklyRow);
        Assert.Contains("4 天 12 小时后", weeklyRow);
        Assert.DoesNotContain("重置（", weeklyRow);
    }

    [Fact]
    public void FormatWindowRow_MissingResetTimeShowsUnknown()
    {
        var window = new CodexUsageWindow(25, 75, 300, null);

        Assert.Contains("重置时间未知", CodexUsageFormatter.FormatWindowRow(window, DateTimeOffset.Now));
    }

    [Fact]
    public void FormatMiniWindow_ShowsCompactWindowAndCountdown()
    {
        var now = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.FromHours(8));

        Assert.Equal(
            "5h 75%·2h30m",
            CodexUsageFormatter.FormatMiniWindow(
                new CodexUsageWindow(25, 75, 300, now.AddHours(2).AddMinutes(30)), now));
        Assert.Equal(
            "周 78%·4d",
            CodexUsageFormatter.FormatMiniWindow(
                new CodexUsageWindow(22, 78, 10080, now.AddDays(4).AddHours(12)), now));
    }

    [Theory]
    [InlineData(6, 18, 30, "6d")]
    [InlineData(2, 5, 0, "2d5h")]
    [InlineData(0, 8, 25, "8h25m")]
    [InlineData(0, 0, 12, "12m")]
    [InlineData(0, 0, 59, "59m")]
    public void FormatCountdownShort_ShowsCompactRemaining(
        int days, int hours, int minutes, string expected)
    {
        var now = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.FromHours(8));

        string result = CodexUsageFormatter.FormatCountdownShort(
            now.AddDays(days).AddHours(hours).AddMinutes(minutes), now);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void FormatCountdownShort_MissingOrPassedReset()
    {
        var now = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.FromHours(8));

        Assert.Equal("--", CodexUsageFormatter.FormatCountdownShort(null, now));
        Assert.Equal("即将重置", CodexUsageFormatter.FormatCountdownShort(now.AddSeconds(-1), now));
    }

    [Fact]
    public void FormatResetCompact_FiveHourShowsCountdownOnly()
    {
        var now = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.FromHours(8));
        var fiveHour = new CodexUsageWindow(25, 75, 300, now.AddHours(2).AddMinutes(30));

        Assert.Equal("2h30m", CodexUsageFormatter.FormatResetCompact(fiveHour, now));
    }

    [Fact]
    public void FormatResetCompact_WeeklyShowsCountdownOnly()
    {
        var now = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.FromHours(8));
        var weekly = new CodexUsageWindow(22, 78, 10080, now.AddDays(4).AddHours(12));

        Assert.Equal("4d", CodexUsageFormatter.FormatResetCompact(weekly, now));
    }

    [Fact]
    public void FormatResetCompact_MissingResetTimeShowsDash()
    {
        var window = new CodexUsageWindow(25, 75, 300, null);

        Assert.Equal("--", CodexUsageFormatter.FormatResetCompact(window, DateTimeOffset.Now));
    }
}
