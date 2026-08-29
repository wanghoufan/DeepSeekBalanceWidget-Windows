using System;

namespace DeepSeekBalanceWidget.Models;

public sealed class AppConfig
{
    public int ConfigVersion { get; set; } = 1;
    public string? ApiKeyEncrypted { get; set; }
    public string SelectedCurrency { get; set; } = "CNY";
    public int RefreshIntervalSeconds { get; set; } = 30;
    public decimal LowBalanceThreshold { get; set; } = 10m;
    public decimal AbnormalChangePercent { get; set; } = 10m;
    public int LowBalanceCooldownSeconds { get; set; } = 1800;
    public int AbnormalAlertCooldownSeconds { get; set; } = 600;
    public bool ShowToastNotifications { get; set; } = true;
    public bool IsAlwaysOnTop { get; set; } = true;
    public bool EnableEdgeAutoHide { get; set; }
    public bool UseMockData { get; set; }
    public bool UseMiniMode { get; set; }
    public bool EnableCodexMonitoring { get; set; } = true;

    /// <summary>ChatGPT 额度预警总开关（剩余百分比预警 + 额度恢复通知）。</summary>
    public bool EnableCodexQuotaAlerts { get; set; } = true;

    /// <summary>剩余百分比预警档位（降序生效，默认 20% / 10% 各提醒一次）。</summary>
    public List<int> CodexQuotaAlertThresholds { get; set; } = new() { 20, 10 };

    /// <summary>周额度是否也参与低量预警（周额度耗尽需等一周才恢复）。</summary>
    public bool CodexWeeklyAlertEnabled { get; set; } = true;

    /// <summary>判定“额度已恢复”的剩余百分比阈值；低于该值后再回到该值才播报恢复。</summary>
    public int CodexQuotaRecoveredPercent { get; set; } = 95;

    /// <summary>恢复播报的最小间隔秒数，吸收剩余百分比在阈值附近抖动的重复播报。</summary>
    public int CodexQuotaAlertCooldownSeconds { get; set; } = 300;

    public double CodexFontSize { get; set; } = 14;
    public string CodexFontStyle { get; set; } = "DeepSeek"; // DeepSeek / Regular / Bold
    /// <summary>胶囊区块渲染顺序：deepseek / chatgpt / workbuddy / te（未来），可在设置中调整。</summary>
    public List<string> AgentOrder { get; set; } = new() { "deepseek", "chatgpt", "workbuddy" };
    public string DefaultCorner { get; set; } = "Remember"; // Remember / BottomRight / BottomLeft
    public bool ShowPeakIndicator { get; set; } = true;
    public List<PeakRange> PeakHourRanges { get; set; } = new()
    {
        new PeakRange(9, 12),
        new PeakRange(14, 18)
    };
    public bool AutoStart { get; set; }
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public decimal? LastSuccessfulBalance { get; set; }
    public DateTimeOffset? LastSuccessfulRefreshUtc { get; set; }
    public bool InLowBalanceState { get; set; }
}
