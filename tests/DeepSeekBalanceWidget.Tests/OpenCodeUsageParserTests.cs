using System;
using System.Text.Json;
using DeepSeekBalanceWidget.Models;
using DeepSeekBalanceWidget.Services;
using Xunit;

namespace DeepSeekBalanceWidget.Tests;

public class OpenCodeUsageParserTests
{
    [Fact]
    public void Parse_OfficialShape_ReturnsThreeWindows()
    {
        const string json = """
            {"usage":{"rolling":{"status":"ok","percent":8,"resetsAt":"2026-08-12T11:24:29.905Z"},
            "weekly":{"status":"ok","percent":21,"resetsAt":"2026-08-17T00:00:00.905Z"},
            "monthly":{"status":"ok","percent":38,"resetsAt":"2026-08-31T23:33:35.905Z"}}}
            """;

        var snapshot = OpenCodeUsageParser.Parse(json);

        Assert.NotNull(snapshot);
        Assert.True(snapshot!.IsAvailable);
        Assert.Equal(3, snapshot.Windows.Count);

        var rolling = Assert.Single(snapshot.Windows, w => w.Kind == "rolling");
        Assert.Equal(8, rolling.UsedPercent);
        Assert.Equal(92, rolling.RemainingPercent);
        Assert.NotNull(rolling.ResetsAt);
    }

    [Fact]
    public void Parse_ResetsAt_UnixSecondsAndEpochMilliseconds()
    {
        const string json = """
            {"usage":{"rolling":{"percent":0,"resetsAt":1789000000},
            "weekly":{"percent":0,"resetsAt":1789000000000}}}
            """;

        var snapshot = OpenCodeUsageParser.Parse(json);

        Assert.NotNull(snapshot);
        var rolling = snapshot!.Windows.Single(w => w.Kind == "rolling");
        var weekly = snapshot.Windows.Single(w => w.Kind == "weekly");

        // 1.789e9 秒 ≈ 2026 年；毫秒值解析后应为同一年附近，且两者为毫秒/秒各自正确的时刻
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1789000000).ToUnixTimeSeconds(),
            rolling.ResetsAt!.Value.ToUnixTimeSeconds());
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1789000000000).ToUnixTimeMilliseconds(),
            weekly.ResetsAt!.Value.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void Parse_MissingWindows_ReturnsOnlyPresentOnes()
    {
        const string json = """{"usage":{"monthly":{"percent":10}}}""";

        var snapshot = OpenCodeUsageParser.Parse(json);

        Assert.NotNull(snapshot);
        Assert.Single(snapshot!.Windows);
        Assert.Equal("monthly", snapshot.Windows[0].Kind);
        Assert.Equal(90, snapshot.Windows[0].RemainingPercent);
        Assert.Null(snapshot.Windows[0].ResetsAt);
    }

    [Fact]
    public void Parse_Garbage_ReturnsNull()
    {
        Assert.Null(OpenCodeUsageParser.Parse("not json"));
        Assert.Null(OpenCodeUsageParser.Parse("{}"));
        Assert.Null(OpenCodeUsageParser.Parse("""{"usage":null}"""));
    }

    [Fact]
    public void Parse_ClampsOutOfRangePercent()
    {
        const string json = """{"usage":{"rolling":{"percent":130},"weekly":{"percent":-5}}}""";

        var snapshot = OpenCodeUsageParser.Parse(json);

        Assert.NotNull(snapshot);
        Assert.Equal(0, snapshot!.Windows.Single(w => w.Kind == "rolling").RemainingPercent);
        Assert.Equal(100, snapshot.Windows.Single(w => w.Kind == "weekly").RemainingPercent);
    }

    [Fact]
    public void ReadKeyFromAuthJson_SupportsObjectAndStringEntries()
    {
        var dir = Path.Combine(Path.GetTempPath(), "oc-auth-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            string objectShape = Path.Combine(dir, "object.json");
            File.WriteAllText(objectShape, """{"openai":{"type":"oauth"},"opencode":{"type":"api","key":"sk-object"}}""");
            Assert.Equal("sk-object", OpenCodeUsageProvider.ReadKeyFromAuthJson(objectShape));

            string stringShape = Path.Combine(dir, "string.json");
            File.WriteAllText(stringShape, """{"opencode":"sk-string"}""");
            Assert.Equal("sk-string", OpenCodeUsageProvider.ReadKeyFromAuthJson(stringShape));

            string noEntry = Path.Combine(dir, "none.json");
            File.WriteAllText(noEntry, """{"openai":{"type":"oauth"}}""");
            Assert.Null(OpenCodeUsageProvider.ReadKeyFromAuthJson(noEntry));

            string missing = Path.Combine(dir, "missing.json");
            Assert.Null(OpenCodeUsageProvider.ReadKeyFromAuthJson(missing));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Formatter_EstimatesUsedUsdFromFixedLimits()
    {
        var rolling = new OpenCodeUsageWindow("rolling", 8, 92, null);
        var weekly = new OpenCodeUsageWindow("weekly", 40, 60, null);
        var monthly = new OpenCodeUsageWindow("monthly", 78, 22, null);

        Assert.Equal(0.96m, OpenCodeUsageFormatter.EstimateUsedUsd(rolling));
        Assert.Equal(12m, OpenCodeUsageFormatter.EstimateUsedUsd(weekly));
        Assert.Equal(46.8m, OpenCodeUsageFormatter.EstimateUsedUsd(monthly));
        Assert.Contains("$12", OpenCodeUsageFormatter.FormatUsedEstimate(rolling));
        Assert.Contains("$30", OpenCodeUsageFormatter.FormatUsedEstimate(weekly));
        Assert.Contains("$60", OpenCodeUsageFormatter.FormatUsedEstimate(monthly));
    }
}
