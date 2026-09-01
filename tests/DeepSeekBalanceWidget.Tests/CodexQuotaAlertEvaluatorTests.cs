using System;
using System.Collections.Generic;
using DeepSeekBalanceWidget.Models;
using DeepSeekBalanceWidget.Services;

namespace DeepSeekBalanceWidget.Tests;

public class CodexQuotaAlertEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Cycle1Reset = Now.AddHours(3);
    private static readonly DateTimeOffset Cycle2Reset = Cycle1Reset.AddHours(5);

    private const int FiveHourMinutes = 300;
    private const int WeeklyMinutes = 10080;

    private static AppConfig Cfg() => new()
    {
        EnableCodexQuotaAlerts = true,
        CodexQuotaAlertThresholds = new List<int> { 20, 10 },
        CodexWeeklyAlertEnabled = true,
        CodexQuotaRecoveredPercent = 95,
        CodexQuotaAlertCooldownSeconds = 0
    };

    private static CodexUsageWindow Win(int remaining, int minutes, DateTimeOffset? resetsAt = null)
        => new(100 - remaining, remaining, minutes, resetsAt);

    private static CodexAccountUsageSnapshot Account(string id, params CodexUsageWindow[] windows)
        => new(id, id + "@example.com", id.ToUpperInvariant(),
            new CodexUsageSnapshot(true, "plus", windows, null),
            Now, false);

    [Fact]
    public void FirstObservation_EstablishesBaseline_NoAlert()
    {
        var alerts = new CodexQuotaAlertEvaluator()
            .Evaluate(new[] { Account("a", Win(18, FiveHourMinutes)) }, Cfg(), Now);

        Assert.Empty(alerts);
    }

    [Fact]
    public void DropToThreshold_AlertsOnce_WithWindowAndAccount()
    {
        var evaluator = new CodexQuotaAlertEvaluator();
        var cfg = Cfg();

        evaluator.Evaluate(new[] { Account("a", Win(80, FiveHourMinutes)) }, cfg, Now);
        var alerts = evaluator.Evaluate(new[] { Account("a", Win(18, FiveHourMinutes)) }, cfg, Now);

        var alert = Assert.Single(alerts);
        Assert.False(alert.IsRecovery);
        Assert.Equal(20, alert.Threshold);
        Assert.Equal("5 小时额度", alert.WindowLabel);
        Assert.Equal("a@example.com", alert.Email);
        Assert.Equal(18, alert.RemainingPercent);
    }

    [Fact]
    public void SameThreshold_RepeatedRefresh_DoesNotDuplicate()
    {
        var evaluator = new CodexQuotaAlertEvaluator();
        var cfg = Cfg();

        evaluator.Evaluate(new[] { Account("a", Win(80, FiveHourMinutes)) }, cfg, Now);
        Assert.Single(evaluator.Evaluate(new[] { Account("a", Win(18, FiveHourMinutes)) }, cfg, Now));
        Assert.Empty(evaluator.Evaluate(new[] { Account("a", Win(15, FiveHourMinutes)) }, cfg, Now));
    }

    [Fact]
    public void DropToLowerThreshold_AlertsAgain()
    {
        var evaluator = new CodexQuotaAlertEvaluator();
        var cfg = Cfg();

        evaluator.Evaluate(new[] { Account("a", Win(80, FiveHourMinutes)) }, cfg, Now);
        evaluator.Evaluate(new[] { Account("a", Win(18, FiveHourMinutes)) }, cfg, Now);

        var alert = Assert.Single(
            evaluator.Evaluate(new[] { Account("a", Win(8, FiveHourMinutes)) }, cfg, Now));
        Assert.Equal(10, alert.Threshold);
    }

    [Fact]
    public void CrossingMultipleThresholds_AlertsLowestOnly()
    {
        var evaluator = new CodexQuotaAlertEvaluator();
        var cfg = Cfg();

        evaluator.Evaluate(new[] { Account("a", Win(80, FiveHourMinutes)) }, cfg, Now);

        // 一次刷新从 80% 直接掉到 5%，同时跨过 20% 与 10%，只应播报最低档。
        var alert = Assert.Single(
            evaluator.Evaluate(new[] { Account("a", Win(5, FiveHourMinutes)) }, cfg, Now));
        Assert.Equal(10, alert.Threshold);
    }

    [Fact]
    public void Recovery_AnnouncedOnce_AndReenablesNextCycle()
    {
        var evaluator = new CodexQuotaAlertEvaluator();
        var cfg = Cfg();

        evaluator.Evaluate(new[] { Account("a", Win(80, FiveHourMinutes, Cycle1Reset)) }, cfg, Now);
        evaluator.Evaluate(new[] { Account("a", Win(5, FiveHourMinutes, Cycle1Reset)) }, cfg, Now);

        var alert = Assert.Single(evaluator.Evaluate(
            new[] { Account("a", Win(100, FiveHourMinutes, Cycle2Reset)) }, cfg, Now));
        Assert.True(alert.IsRecovery);
        Assert.Null(alert.Threshold);
        Assert.Equal("5 小时额度", alert.WindowLabel);
        Assert.Equal(100, alert.RemainingPercent);

        // 恢复后提醒记录已清空，下一个周期可以重新预警。
        var again = Assert.Single(evaluator.Evaluate(
            new[] { Account("a", Win(18, FiveHourMinutes, Cycle2Reset)) }, cfg, Now));
        Assert.Equal(20, again.Threshold);
    }

    [Fact]
    public void ResetToFull_AlwaysAnnouncesRecovery()
    {
        var evaluator = new CodexQuotaAlertEvaluator();
        var cfg = Cfg();

        evaluator.Evaluate(new[] { Account("a", Win(60, FiveHourMinutes, Cycle1Reset)) }, cfg, Now);

        // 用户要求：只要额度恢复（重置回满）都要提醒，即使本周期从未跌破档位。
        var alert = Assert.Single(evaluator.Evaluate(
            new[] { Account("a", Win(100, FiveHourMinutes, Cycle2Reset)) }, cfg, Now));
        Assert.True(alert.IsRecovery);
        Assert.Equal(100, alert.RemainingPercent);
    }

    [Fact]
    public void SameCycleRebound_DoesNotAnnounceRecovery()
    {
        var evaluator = new CodexQuotaAlertEvaluator();
        var cfg = Cfg();

        evaluator.Evaluate(new[] { Account("a", Win(60, FiveHourMinutes, Cycle1Reset)) }, cfg, Now);

        // ResetsAt 未前进（同一周期内数据回弹到 100%）不算恢复，不打扰用户。
        Assert.Empty(evaluator.Evaluate(
            new[] { Account("a", Win(100, FiveHourMinutes, Cycle1Reset)) }, cfg, Now));
    }

    [Fact]
    public void ResetsAtJitter_WithinTolerance_NotTreatedAsNewCycle()
    {
        var evaluator = new CodexQuotaAlertEvaluator();
        var cfg = Cfg();

        evaluator.Evaluate(new[] { Account("a", Win(60, FiveHourMinutes, Cycle1Reset)) }, cfg, Now);
        evaluator.Evaluate(new[] { Account("a", Win(5, FiveHourMinutes, Cycle1Reset)) }, cfg, Now);

        // ResetsAt 只前进 30 秒（小于 1 分钟抖动容忍窗口）：不视为新周期，
        // 既不播报恢复，也不清空档位记录。
        Assert.Empty(evaluator.Evaluate(
            new[] { Account("a", Win(100, FiveHourMinutes, Cycle1Reset.AddSeconds(30))) }, cfg, Now));
    }

    [Fact]
    public void WeeklyWindow_TrackedSeparately()
    {
        var evaluator = new CodexQuotaAlertEvaluator();
        var cfg = Cfg();
        var fiveHour = Win(80, FiveHourMinutes);

        evaluator.Evaluate(new[] { Account("a", fiveHour, Win(80, WeeklyMinutes)) }, cfg, Now);

        var alert = Assert.Single(evaluator.Evaluate(
            new[] { Account("a", fiveHour, Win(7, WeeklyMinutes)) }, cfg, Now));
        Assert.Equal("周额度", alert.WindowLabel);
        Assert.Equal(10, alert.Threshold);
    }

    [Fact]
    public void WeeklyAlertDisabled_SkipsWeeklyWindow()
    {
        var cfg = Cfg();
        cfg.CodexWeeklyAlertEnabled = false;
        var evaluator = new CodexQuotaAlertEvaluator();

        evaluator.Evaluate(
            new[] { Account("a", Win(80, FiveHourMinutes), Win(80, WeeklyMinutes)) }, cfg, Now);

        Assert.Empty(evaluator.Evaluate(
            new[] { Account("a", Win(80, FiveHourMinutes), Win(5, WeeklyMinutes)) }, cfg, Now));
    }

    [Fact]
    public void MasterSwitchOff_NoAlerts()
    {
        var cfg = Cfg();
        cfg.EnableCodexQuotaAlerts = false;
        var evaluator = new CodexQuotaAlertEvaluator();

        evaluator.Evaluate(new[] { Account("a", Win(80, FiveHourMinutes)) }, cfg, Now);
        Assert.Empty(evaluator.Evaluate(new[] { Account("a", Win(3, FiveHourMinutes)) }, cfg, Now));
    }

    [Fact]
    public void MultipleAccounts_AlertIndependently()
    {
        var evaluator = new CodexQuotaAlertEvaluator();
        var cfg = Cfg();

        evaluator.Evaluate(
            new[] { Account("a", Win(80, FiveHourMinutes)), Account("b", Win(80, FiveHourMinutes)) },
            cfg, Now);

        var alerts = evaluator.Evaluate(
            new[] { Account("a", Win(15, FiveHourMinutes)), Account("b", Win(9, FiveHourMinutes)) },
            cfg, Now);

        Assert.Equal(2, alerts.Count);
        Assert.Contains(alerts, a => a.Email == "a@example.com" && a.Threshold == 20);
        Assert.Contains(alerts, a => a.Email == "b@example.com" && a.Threshold == 10);
    }

    [Fact]
    public void UnavailableAccount_Skipped()
    {
        var unavailable = new CodexAccountUsageSnapshot("a", "a@example.com", "A",
            CodexUsageSnapshot.Unavailable("读取失败"), null, true, "读取失败");

        Assert.Empty(new CodexQuotaAlertEvaluator().Evaluate(new[] { unavailable }, Cfg(), Now));
    }

    [Fact]
    public void RecoveryFlapping_WithinCooldown_AnnouncedOnce()
    {
        var cfg = Cfg();
        cfg.CodexQuotaAlertCooldownSeconds = 300;
        var evaluator = new CodexQuotaAlertEvaluator();

        evaluator.Evaluate(new[] { Account("a", Win(80, FiveHourMinutes, Cycle1Reset)) }, cfg, Now);
        evaluator.Evaluate(new[] { Account("a", Win(15, FiveHourMinutes, Cycle1Reset)) }, cfg, Now);
        Assert.Single(evaluator.Evaluate(
            new[] { Account("a", Win(100, FiveHourMinutes, Cycle2Reset)) }, cfg, Now));

        // 冷却期内又走完一轮「下滑 → 恢复」，恢复播报应被冷却吸收，避免反复打扰。
        evaluator.Evaluate(new[] { Account("a", Win(15, FiveHourMinutes, Cycle2Reset)) }, cfg, Now.AddSeconds(30));
        Assert.Empty(evaluator.Evaluate(
            new[] { Account("a", Win(100, FiveHourMinutes, Cycle2Reset)) }, cfg, Now.AddSeconds(60)));
    }

    [Fact]
    public void LowThresholdAlert_NotBlockedByRecoveryCooldown()
    {
        var cfg = Cfg();
        cfg.CodexQuotaAlertCooldownSeconds = 3600;
        var evaluator = new CodexQuotaAlertEvaluator();

        evaluator.Evaluate(new[] { Account("a", Win(80, FiveHourMinutes, Cycle1Reset)) }, cfg, Now);
        evaluator.Evaluate(new[] { Account("a", Win(15, FiveHourMinutes, Cycle1Reset)) }, cfg, Now);
        evaluator.Evaluate(new[] { Account("a", Win(100, FiveHourMinutes, Cycle2Reset)) }, cfg, Now);
        evaluator.Evaluate(new[] { Account("a", Win(15, FiveHourMinutes, Cycle2Reset)) }, cfg, Now.AddMinutes(1));

        // 恢复冷却只约束恢复播报；额度继续下滑到更低档位时，预警必须照常发出。
        var alert = Assert.Single(evaluator.Evaluate(
            new[] { Account("a", Win(8, FiveHourMinutes, Cycle2Reset)) }, cfg, Now.AddMinutes(2)));
        Assert.Equal(10, alert.Threshold);
    }
}
