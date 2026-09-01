using System;
using System.Collections.Generic;
using System.Linq;
using DeepSeekBalanceWidget.Models;
using DeepSeekBalanceWidget.Services;
using Xunit;

namespace DeepSeekBalanceWidget.Tests;

/// <summary>
/// OpenCode 额度预警判定测试：档位与 GPT 共用配置，
/// 三窗口（5h/周/月）独立跟踪，遵循「基线 / 不播报恢复 / 新周期重新武装档位」规则。
/// </summary>
public class OpenCodeQuotaAlertEvaluatorTests
{
    private static readonly DateTimeOffset BaseNow = new(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);

    private static AppConfig Config() => new()
    {
        EnableCodexQuotaAlerts = true,
        CodexQuotaAlertThresholds = new List<int> { 20, 10 },
        CodexQuotaRecoveredPercent = 95,
        CodexQuotaAlertCooldownSeconds = 0
    };

    private static OpenCodeUsageSnapshot Snapshot(params (string kind, int remaining)[] windows)
        => new(true, null,
            windows.Select(w => new OpenCodeUsageWindow(
                w.kind, 100 - w.remaining, w.remaining,
                BaseNow.AddHours(2))).ToArray());

    /// <summary>与 Snapshot 相同但 ResetsAt 前进 5 小时，模拟进入新周期。</summary>
    private static OpenCodeUsageSnapshot NextCycleSnapshot(params (string kind, int remaining)[] windows)
        => new(true, null,
            windows.Select(w => new OpenCodeUsageWindow(
                w.kind, 100 - w.remaining, w.remaining,
                BaseNow.AddHours(7))).ToArray());

    private readonly OpenCodeQuotaAlertEvaluator _evaluator = new();

    [Fact]
    public void FirstObservation_IsBaselineOnly_NoAlert()
    {
        var alerts = _evaluator.Evaluate(
            Snapshot(("rolling", 5), ("weekly", 3), ("monthly", 1)),
            Config(), DateTimeOffset.Now);

        Assert.Empty(alerts);

        // 第二次观察才开始按档位预警（一次只报跨过的最低档）
        var second = _evaluator.Evaluate(
            Snapshot(("rolling", 5), ("weekly", 3), ("monthly", 1)),
            Config(), DateTimeOffset.Now.AddMinutes(1));

        Assert.Equal(3, second.Count);
        Assert.All(second, a => Assert.False(a.IsRecovery));
        Assert.Contains(second, a => a.WindowKind == "rolling" && a.Threshold == 10);
    }

    [Fact]
    public void CrossingLowestThreshold_OnlyReportsLowestOnce()
    {
        var cfg = Config();
        var now = DateTimeOffset.Now;

        _evaluator.Evaluate(Snapshot(("rolling", 50)), cfg, now); // 基线

        var at20 = _evaluator.Evaluate(Snapshot(("rolling", 20)), cfg, now.AddMinutes(1));
        Assert.Single(at20);
        Assert.Equal(20, at20[0].Threshold);

        // 同档位不再重复
        var again = _evaluator.Evaluate(Snapshot(("rolling", 19)), cfg, now.AddMinutes(2));
        Assert.Empty(again);

        // 跌到更低档位报最低档（10）
        var at10 = _evaluator.Evaluate(Snapshot(("rolling", 10)), cfg, now.AddMinutes(3));
        Assert.Single(at10);
        Assert.Equal(10, at10[0].Threshold);
    }

    [Fact]
    public void Recovery_NotAnnounced_AndCycleResetRearmsAlerts()
    {
        var cfg = Config();

        _evaluator.Evaluate(Snapshot(("rolling", 60)), cfg, BaseNow); // 基线

        // 用户要求：OpenCode 不做恢复提醒——即使进入新周期、额度重置回满也不播报。
        Assert.Empty(_evaluator.Evaluate(NextCycleSnapshot(("rolling", 100)), cfg, BaseNow.AddMinutes(1)));

        // 但新周期已清空档位记录，随后跌破档位时低量预警可重新触发。
        var reAlert = _evaluator.Evaluate(Snapshot(("rolling", 15)), cfg, BaseNow.AddMinutes(2));
        Assert.Single(reAlert);
        Assert.False(reAlert[0].IsRecovery);
        Assert.Equal(20, reAlert[0].Threshold);
    }

    [Fact]
    public void Windows_TrackedIndependently()
    {
        var cfg = Config();
        var now = DateTimeOffset.Now;

        _evaluator.Evaluate(
            Snapshot(("rolling", 50), ("weekly", 50), ("monthly", 50)),
            cfg, now); // 基线

        var alerts = _evaluator.Evaluate(
            Snapshot(("rolling", 15), ("weekly", 50), ("monthly", 50)),
            cfg, now.AddMinutes(1));

        Assert.Single(alerts);
        Assert.Equal("rolling", alerts[0].WindowKind);
    }

    [Fact]
    public void DisabledAlerts_ReturnEmpty()
    {
        var cfg = Config();
        cfg.EnableCodexQuotaAlerts = false;
        _evaluator.Evaluate(Snapshot(("rolling", 50)), cfg, DateTimeOffset.Now);
        Assert.Empty(_evaluator.Evaluate(
            Snapshot(("rolling", 5)), cfg, DateTimeOffset.Now.AddMinutes(1)));
    }

    [Fact]
    public void LowAlert_CarriesEstimatedUsedUsd()
    {
        var cfg = Config();
        var now = DateTimeOffset.Now;
        _evaluator.Evaluate(Snapshot(("monthly", 50)), cfg, now);

        var alerts = _evaluator.Evaluate(Snapshot(("monthly", 10)), cfg, now.AddMinutes(1));
        Assert.Single(alerts);
        // 90% 已用 × $60 = $54
        Assert.Equal(54m, alerts[0].EstimatedUsedUsd);
    }
}
