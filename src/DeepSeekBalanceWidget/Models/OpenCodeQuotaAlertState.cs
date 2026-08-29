namespace DeepSeekBalanceWidget.Models;

/// <summary>
/// 一次 OpenCode 额度预警事件（低量预警或额度恢复）。
/// 窗口跟踪状态复用 CodexQuotaWindowState（按「窗口类型」独立跟踪）。
/// </summary>
public sealed record OpenCodeQuotaAlert(
    /// <summary>窗口类型标识：rolling / weekly / monthly。</summary>
    string WindowKind,
    /// <summary>窗口显示名：5 小时额度 / 周额度 / 月额度。</summary>
    string WindowLabel,
    int RemainingPercent,
    /// <summary>命中的预警档位；恢复提醒为 null。</summary>
    int? Threshold,
    bool IsRecovery,
    DateTimeOffset? ResetsAt,
    /// <summary>预估已用美元（按固定限额换算）。</summary>
    decimal? EstimatedUsedUsd);
