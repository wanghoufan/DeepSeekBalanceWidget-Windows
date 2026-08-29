using System;
using System.Collections.Generic;

namespace DeepSeekBalanceWidget.Models;

/// <summary>
/// OpenCode Go 的单额度窗口。Kind 为 rolling（5 小时）/ weekly（周）/ monthly（月）。
/// </summary>
public sealed record OpenCodeUsageWindow(
    string Kind,
    int UsedPercent,
    int RemainingPercent,
    DateTimeOffset? ResetsAt);

/// <summary>OpenCode Go 额度快照（单个 API Key）。</summary>
public sealed record OpenCodeUsageSnapshot(
    bool IsAvailable,
    string? Error,
    IReadOnlyList<OpenCodeUsageWindow> Windows)
{
    public static OpenCodeUsageSnapshot Unavailable(string error)
        => new(false, error, Array.Empty<OpenCodeUsageWindow>());
}
