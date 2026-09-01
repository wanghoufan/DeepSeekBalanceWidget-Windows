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

    /// <summary>DeepSeek 余额监测开关（关闭后胶囊与详细面板均不显示 DS 区块）。</summary>
    public bool EnableDeepSeekMonitoring { get; set; } = true;

    /// <summary>WorkBuddy 占位监测开关（无数据源，默认关闭）。</summary>
    public bool EnableWorkbuddyMonitoring { get; set; }

    /// <summary>OpenCode Go 额度监测开关。</summary>
    public bool EnableOpenCodeMonitoring { get; set; } = true;

    /// <summary>OpenCode Go API Key（DPAPI 加密存储）；为空时自动读本机 auth.json。</summary>
    public string? OpenCodeApiKeyEncrypted { get; set; }

    /// <summary>警报声开关（DS 低余额 / GPT 与 OpenCode 额度预警共用）。</summary>
    public bool AlertSoundEnabled { get; set; } = true;

    /// <summary>警报声风格：Soft 柔和 / Standard 标准 / Urgent 急促。</summary>
    public string AlertSoundStyle { get; set; } = "Standard";

    /// <summary>恢复提示音风格（额度恢复提醒专用，柔和系 11 选 1，见 RecoverySound.Styles）。</summary>
    public string RecoveryAlertSoundStyle { get; set; } = "Chime";

    /// <summary>警报模式：Continuous=持续直到点「知道了」；Limited=限时（至少 10 秒）。</summary>
    public string AlertMode { get; set; } = "Continuous";

    /// <summary>限时提醒的最短持续时间（秒）：10 / 30 / 60。持续模式不受影响（点「知道了」才停）。</summary>
    public int AlertMinDurationSeconds { get; set; } = 10;

    /// <summary>警报窗位置：TopRight / RightCenter / BottomRight。</summary>
    public string AlertPosition { get; set; } = "TopRight";

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
    /// <summary>胶囊区块渲染顺序：deepseek / chatgpt / opencode / workbuddy（未来），可在设置中调整。</summary>
    public List<string> AgentOrder { get; set; } = new() { "deepseek", "chatgpt", "opencode" };

    /// <summary>
    /// 加载后归一化：旧配置的 AgentOrder 缺 opencode 时补到末尾，
    /// 使升级用户无需手动调整即可看到新区块。
    /// </summary>
    public void Normalize()
    {
        AgentOrder ??= new List<string>();
        if (AgentOrder.Count == 0)
            AgentOrder.AddRange(new[] { "deepseek", "chatgpt", "opencode" });
        if (!AgentOrder.Contains("opencode")) AgentOrder.Add("opencode");
    }
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
