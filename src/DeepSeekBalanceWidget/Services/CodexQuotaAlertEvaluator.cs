using System;
using System.Collections.Generic;
using System.Linq;
using DeepSeekBalanceWidget.Models;

namespace DeepSeekBalanceWidget.Services;

/// <summary>
/// ChatGPT 额度预警判定：按「账号 + 额度窗口」独立跟踪剩余百分比，
/// 在剩余额度降到配置档位时预警，在额度恢复到高位时通知。
/// 平台无关，Windows 与 macOS 共用同一份判定逻辑。
/// </summary>
public sealed class CodexQuotaAlertEvaluator
{
    private const string FiveHourKind = "5h";
    private const string WeeklyKind = "weekly";
    private const string FiveHourLabel = "5 小时额度";
    private const string WeeklyLabel = "周额度";

    /// <summary>窗口时长不超过该分钟数视为 5 小时窗口，否则视为周窗口。</summary>
    private const int FiveHourWindowMaxMinutes = 360;

    /// <summary>ResetsAt 前进超过该时长才算进入新周期；小于该值视为 API 时间戳抖动。</summary>
    internal static readonly TimeSpan CycleJitterTolerance = TimeSpan.FromMinutes(1);

    private readonly Dictionary<string, CodexQuotaWindowState> _states = new();

    /// <summary>评估所有账号的额度窗口，返回本次需要提醒的事件（可能为空）。</summary>
    public IReadOnlyList<CodexQuotaAlert> Evaluate(
        IReadOnlyList<CodexAccountUsageSnapshot> usages,
        AppConfig cfg,
        DateTimeOffset now)
    {
        var alerts = new List<CodexQuotaAlert>();
        if (!cfg.EnableCodexQuotaAlerts || usages is not { Count: > 0 }) return alerts;

        var thresholds = NormalizeThresholds(cfg.CodexQuotaAlertThresholds);
        if (thresholds.Count == 0) return alerts;

        int recoveredAt = Math.Clamp(cfg.CodexQuotaRecoveredPercent, 1, 100);
        var cooldown = TimeSpan.FromSeconds(Math.Max(0, cfg.CodexQuotaAlertCooldownSeconds));

        foreach (var account in usages)
        {
            if (account.Usage is not { IsAvailable: true }) continue;

            foreach (var window in OrderWindows(account.Usage.Windows))
            {
                string kind = ClassifyWindow(window);
                if (kind == WeeklyKind && !cfg.CodexWeeklyAlertEnabled) continue;

                var state = GetOrCreateState(account.AccountId, kind);
                EvaluateWindow(account, window, kind, state, thresholds, recoveredAt, cooldown, now, alerts);
            }
        }

        return alerts;
    }

    private static void EvaluateWindow(
        CodexAccountUsageSnapshot account,
        CodexUsageWindow window,
        string kind,
        CodexQuotaWindowState state,
        List<int> thresholds,
        int recoveredAt,
        TimeSpan cooldown,
        DateTimeOffset now,
        List<CodexQuotaAlert> alerts)
    {
        int remaining = window.RemainingPercent;

        // 首次观察到该窗口时只建立基线，不打扰：避免应用刚启动就弹出历史遗留的低量预警。
        if (state.LastRemainingPercent is null)
        {
            state.LastRemainingPercent = remaining;
            state.LastResetsAt = window.ResetsAt;
            return;
        }

        // 恢复播报语义（2026-08-31 调整）：只要额度窗口真正进入新周期（ResetsAt 前进、
        // 额度重置回满），就一律播报「已恢复」，不再要求本周期先预警过——
        // 用户要求「5 小时 / 周额度只要恢复了都要提醒」。
        // ResetsAt 必须严格前进超过抖动容忍窗口才算新周期，防止 API 时间戳毫秒级抖动
        // 误清空档位记录，导致 13% 这种低位档位被反复弹出。
        bool isNewCycle = window.ResetsAt.HasValue
                          && state.LastResetsAt.HasValue
                          && window.ResetsAt.Value - state.LastResetsAt.Value > CycleJitterTolerance;
        if (isNewCycle) state.ResetCycle();

        bool recovered = isNewCycle && remaining >= recoveredAt;
        if (recovered && IsOutsideRecoveryCooldown(state, now, cooldown))
        {
            alerts.Add(new CodexQuotaAlert(
                account.AccountId, account.Email, kind, LabelOf(kind),
                remaining, null, IsRecovery: true, window.ResetsAt));
            state.LastRecoveryAlertUtc = now;
            state.LastRemainingPercent = remaining;
            state.LastResetsAt = window.ResetsAt;

            // 恢复播报与低量预警互斥，同一次刷新内恢复优先：
            // 当用户把阈值设得比恢复线还高（如阈值 99 / 恢复 95）时，
            // 两条分支会同时命中，弹出「仅剩 98%」与「已恢复」这样自相矛盾的通知。
            return;
        }

        // 低量预警：一次刷新只播报跨过的最低档位，避免连续弹窗。
        // 已提醒档位记入 NotifiedThresholds，直到额度恢复（进入新周期）才清空。
        var crossed = thresholds.Where(t => remaining <= t).ToList();
        if (crossed.Count > 0)
        {
            int lowest = crossed.Min();
            if (!state.NotifiedThresholds.Contains(lowest))
            {
                alerts.Add(new CodexQuotaAlert(
                    account.AccountId, account.Email, kind, LabelOf(kind),
                    remaining, lowest, IsRecovery: false, window.ResetsAt));
                foreach (int t in crossed) state.NotifiedThresholds.Add(t);
            }
        }

        state.LastRemainingPercent = remaining;
        state.LastResetsAt = window.ResetsAt;
    }

    /// <summary>
    /// 恢复播报的冷却判定。只约束恢复播报本身，低量预警由档位记录去重，不受此冷却影响，
    /// 否则额度快速下滑时后一档预警会被恢复播报的时间戳误伤。
    /// </summary>
    private static bool IsOutsideRecoveryCooldown(CodexQuotaWindowState state, DateTimeOffset now, TimeSpan cooldown)
        => state.LastRecoveryAlertUtc is null
           || cooldown <= TimeSpan.Zero
           || now - state.LastRecoveryAlertUtc.Value >= cooldown;

    private CodexQuotaWindowState GetOrCreateState(string accountId, string kind)
    {
        string key = accountId + "|" + kind;
        if (!_states.TryGetValue(key, out var state))
        {
            state = new CodexQuotaWindowState();
            _states[key] = state;
        }
        return state;
    }

    private static string ClassifyWindow(CodexUsageWindow window)
        => (window.DurationMinutes ?? 300) <= FiveHourWindowMaxMinutes ? FiveHourKind : WeeklyKind;

    private static string LabelOf(string kind) => kind == FiveHourKind ? FiveHourLabel : WeeklyLabel;

    private static IReadOnlyList<CodexUsageWindow> OrderWindows(IReadOnlyList<CodexUsageWindow> windows)
        => windows
            .OrderBy(window => window.DurationMinutes ?? int.MaxValue)
            .ThenBy(window => window.ResetsAt ?? DateTimeOffset.MaxValue)
            .ToArray();

    /// <summary>清洗阈值档位：只保留 1-99，去重后降序（先命中更低的档位）。</summary>
    private static List<int> NormalizeThresholds(IEnumerable<int>? raw)
        => (raw ?? Enumerable.Empty<int>())
            .Where(t => t > 0 && t < 100)
            .Distinct()
            .OrderByDescending(t => t)
            .ToList();
}
