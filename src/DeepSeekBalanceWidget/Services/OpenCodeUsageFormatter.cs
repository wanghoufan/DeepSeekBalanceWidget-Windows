using System;
using DeepSeekBalanceWidget.Models;

namespace DeepSeekBalanceWidget.Services;

/// <summary>OpenCode Go 套餐固定限额（美元），官方：5h=$12 / 周=$30 / 月=$60。</summary>
public static class OpenCodeUsageFormatter
{
    public const decimal RollingLimitUsd = 12m;
    public const decimal WeeklyLimitUsd = 30m;
    public const decimal MonthlyLimitUsd = 60m;

    /// <summary>窗口显示名：5 小时额度 / 周额度 / 月额度。</summary>
    public static string LabelOf(string kind) => kind switch
    {
        OpenCodeUsageProvider.RollingKind => "5 小时额度",
        OpenCodeUsageProvider.WeeklyKind => "周额度",
        OpenCodeUsageProvider.MonthlyKind => "月额度",
        _ => kind
    };

    /// <summary>胶囊/紧凑场景的窗口短名：5h / 周 / 月。</summary>
    public static string ShortLabelOf(string kind) => kind switch
    {
        OpenCodeUsageProvider.RollingKind => "5h",
        OpenCodeUsageProvider.WeeklyKind => "周",
        OpenCodeUsageProvider.MonthlyKind => "月",
        _ => kind
    };

    /// <summary>窗口对应的美元限额。</summary>
    public static decimal LimitUsdOf(string kind) => kind switch
    {
        OpenCodeUsageProvider.RollingKind => RollingLimitUsd,
        OpenCodeUsageProvider.WeeklyKind => WeeklyLimitUsd,
        OpenCodeUsageProvider.MonthlyKind => MonthlyLimitUsd,
        _ => 0m
    };

    /// <summary>按已用百分比估算的美元用量（API 只返回百分比，此处为常量限额换算的估算值）。</summary>
    public static decimal EstimateUsedUsd(OpenCodeUsageWindow window)
        => Math.Round(LimitUsdOf(window.Kind) * window.UsedPercent / 100m, 2);

    /// <summary>详细面板的已用/限额列：如「≈ $0.96 / $12」。</summary>
    public static string FormatUsedEstimate(OpenCodeUsageWindow window)
        => $"≈ ${EstimateUsedUsd(window):0.##} / ${LimitUsdOf(window.Kind):0}";

    /// <summary>详细面板的恢复时间点列：如「08-29 22:15」。</summary>
    public static string FormatResetTime(OpenCodeUsageWindow window)
        => window.ResetsAt is null ? "--" : window.ResetsAt.Value.ToLocalTime().ToString("MM-dd HH:mm");

    /// <summary>详细面板的倒计时列（复用 GPT 的紧凑倒计时格式）。</summary>
    public static string FormatCountdown(OpenCodeUsageWindow window, DateTimeOffset now)
        => CodexUsageFormatter.FormatCountdownShort(window.ResetsAt, now);

    /// <summary>胶囊行倒计时（同 GPT 胶囊）。</summary>
    public static string FormatCountdownShort(OpenCodeUsageWindow window, DateTimeOffset now)
        => CodexUsageFormatter.FormatCountdownShort(window.ResetsAt, now);
}
