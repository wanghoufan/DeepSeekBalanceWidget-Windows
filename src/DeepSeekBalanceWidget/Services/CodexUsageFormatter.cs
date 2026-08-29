using DeepSeekBalanceWidget.Models;

namespace DeepSeekBalanceWidget.Services;

public static class CodexUsageFormatter
{
    public static string FormatPlan(string? planType)
    {
        if (string.IsNullOrWhiteSpace(planType)) return "Codex";
        return "Codex " + char.ToUpperInvariant(planType[0]) + planType[1..];
    }

    public static string FormatWindow(CodexUsageWindow window)
        => $"{FormatDuration(window.DurationMinutes)}剩余 {window.RemainingPercent}%";

    public static string FormatReset(CodexUsageWindow window)
        => window.ResetsAt is null
            ? "重置时间未知"
            : window.ResetsAt.Value.ToLocalTime().ToString("MM-dd HH:mm") + " 重置";

    /// <summary>
    /// 单窗口完整行：如「5 小时剩余 45% · 2 小时 30 分钟后」。
    /// 只保留剩余额度与距重置倒计时，不再显示具体重置日期/时间。
    /// </summary>
    public static string FormatWindowRow(CodexUsageWindow window, DateTimeOffset now)
    {
        string remaining = $"{FormatDuration(window.DurationMinutes)}剩余 {window.RemainingPercent}%";
        string reset = FormatResetWithCountdown(window, now);
        return $"{remaining} · {reset}";
    }

    /// <summary>
    /// 胶囊用的紧凑单窗口：如「5h 45%·2h30m」或「周 78%·4d12h」。
    /// </summary>
    public static string FormatMiniWindow(CodexUsageWindow window, DateTimeOffset now)
        => $"{FormatDurationShort(window.DurationMinutes)} {window.RemainingPercent}%·{FormatCountdownShort(window.ResetsAt, now)}";

    public static string FormatResetWithCountdown(CodexUsageWindow window, DateTimeOffset now)
    {
        if (window.ResetsAt is null) return "重置时间未知";
        string countdown = FormatCountdown(window.ResetsAt, now);
        string suffix = countdown == "即将重置" ? string.Empty : "后";
        return $"{countdown}{suffix}";
    }

    /// <summary>
    /// 对齐表格用的紧凑恢复列：仅倒计时，5 小时窗口「2h30m」，周窗口「4d12h」。
    /// </summary>
    public static string FormatResetCompact(CodexUsageWindow window, DateTimeOffset now)
    {
        if (window.ResetsAt is null) return "--";
        return FormatCountdownShort(window.ResetsAt, now);
    }

    public static string FormatCountdown(DateTimeOffset? resetsAt, DateTimeOffset now)
    {
        if (resetsAt is null) return "--";
        TimeSpan remaining = resetsAt.Value - now;
        if (remaining <= TimeSpan.Zero) return "即将重置";
        if (remaining.Days > 0) return $"{remaining.Days} 天 {remaining.Hours} 小时";
        if (remaining.Hours > 0) return $"{remaining.Hours} 小时 {remaining.Minutes} 分钟";
        return $"{Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes))} 分钟";
    }

    /// <summary>
    /// 胶囊用的紧凑倒计时：2h30m / 4d12h / 28d / 45m / 即将重置 / --。
    /// 当倒计时 ≥ 3 天时只显示「Xd」，去掉小时以节省胶囊横向空间。
    /// </summary>
    public static string FormatCountdownShort(DateTimeOffset? resetsAt, DateTimeOffset now)
    {
        if (resetsAt is null) return "--";
        TimeSpan remaining = resetsAt.Value - now;
        if (remaining <= TimeSpan.Zero) return "即将重置";
        if (remaining.TotalMinutes < 60)
            return $"{Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes))}m";
        if (remaining.TotalHours < 24)
            return $"{(int)Math.Floor(remaining.TotalHours)}h{remaining.Minutes}m";
        int days = (int)Math.Floor(remaining.TotalDays);
        if (days >= 3) return $"{days}d";
        return $"{days}d{(int)Math.Floor(remaining.TotalHours % 24)}h";
    }

    public static string FormatDuration(int? minutes) => minutes switch
    {
        300 => "5 小时",
        10080 => "每周",
        > 0 when minutes.Value % 60 == 0 => $"{minutes.Value / 60} 小时",
        > 0 => $"{minutes.Value} 分钟",
        _ => "当前窗口"
    };

    /// <summary>胶囊用的紧凑窗口名：5h / 周 / 2h / 45m。</summary>
    public static string FormatDurationShort(int? minutes) => minutes switch
    {
        300 => "5h",
        10080 => "周",
        > 0 when minutes.Value % 60 == 0 => $"{minutes.Value / 60}h",
        > 0 => $"{minutes.Value}m",
        _ => "当前"
    };
}
