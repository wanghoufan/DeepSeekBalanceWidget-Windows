using System.Windows;
using DeepSeekBalanceWidget.Models;

namespace DeepSeekBalanceWidget.Services;

public static class ToastService
{
    /// <summary>
    /// 弹出普通通知（自动消失、不响警报声；用于恢复类播报）。
    /// </summary>
    public static void Show(Window owner, string title, string body, AppConfig cfg)
        => Show(owner, title, body, cfg, ToastAlertStyle.Notice);

    /// <summary>
    /// 弹出通知/警报。alarm/recovery 样式按配置播放循环提示音并常驻（或限时 ≥10 秒），
    /// 直到用户点击「知道了」。位置由 cfg.AlertPosition 决定（右上/右中/右下）。
    /// Recovery 样式使用独立的恢复提示音风格（cfg.RecoveryAlertSoundStyle）。
    /// </summary>
    public static void Show(Window owner, string title, string body, AppConfig cfg, ToastAlertStyle style)
    {
        var toast = new ToastWindow(
            title, body, style,
            soundEnabled: cfg.AlertSoundEnabled,
            alertMode: cfg.AlertMode,
            alertPosition: cfg.AlertPosition,
            alertSoundStyle: cfg.AlertSoundStyle,
            recoverySoundStyle: cfg.RecoveryAlertSoundStyle,
            minDurationSeconds: cfg.AlertMinDurationSeconds)
        {
            Owner = owner
        };
        toast.Show();
    }
}
