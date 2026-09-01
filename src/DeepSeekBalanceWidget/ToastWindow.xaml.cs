using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DeepSeekBalanceWidget.Services;
using Color = System.Windows.Media.Color;

namespace DeepSeekBalanceWidget;

/// <summary>
/// 通知样式：Notice=普通通知（自动消失不响声）；
/// Alarm=警报（橙色描边，警报声 + 常驻/限时）；
/// Recovery=恢复提醒（绿色描边，提示音 + 常驻/限时，与警报同级但视觉/声音可区分）。
/// </summary>
public enum ToastAlertStyle
{
    Notice,
    Alarm,
    Recovery
}

/// <summary>
/// 通知/警报窗。警报/恢复模式（Alarm / Recovery）：
/// - 播放循环提示音（可选），常驻显示直到点击「知道了」（持续模式）；
/// - 或限时模式：无人点击时响满设定时长（默认 10 秒，可设 30 秒/1 分钟）后自动淡出；
/// - 点「知道了」（或点窗体）立即停声关闭，不设响满保底；
/// - 声音跟窗走（所有权机制）：发声窗关闭即停声，若还有其他响声窗则交棒给最新的一个，
///   最后一个响声窗关闭后彻底静音；
/// - 位置由设置决定（右上 / 右中 / 右下），多窗堆叠、开关时统一重排。
/// 普通通知（Notice）：8 秒自动淡出，无按钮不响声。
/// </summary>
public partial class ToastWindow : Window
{
    private const double NoticeSeconds = 8;

    /// <summary>限时模式的自动关闭时长（秒），由设置决定（10/30/60）；点「知道了」会立即关闭，不受此限制。</summary>
    private readonly double _minSeconds;

    /// <summary>当前活动的通知窗（含普通与警报），用于统一堆叠定位与警报声计数。</summary>
    private static readonly List<ToastWindow> Active = new();

    /// <summary>当前正在发声的窗口（声音所有权）：新发声窗接管，关闭时由它停声并交棒。</summary>
    private static ToastWindow? _soundOwner;

    private readonly ToastAlertStyle _style;
    private readonly bool _sound;
    private readonly string _soundStyle;
    private readonly string _position;
    private readonly bool _persistent;
    private readonly DispatcherTimer? _autoCloseTimer;
    private bool _dismissed;
    private bool _closing;

    public ToastWindow(
        string title,
        string body,
        ToastAlertStyle style,
        bool soundEnabled,
        string alertMode,
        string alertPosition,
        string alertSoundStyle,
        string recoverySoundStyle = "Chime",
        double minDurationSeconds = 10)
    {
        InitializeComponent();
        _style = style;
        _minSeconds = Math.Max(1, minDurationSeconds);
        bool isAlertLike = style is ToastAlertStyle.Alarm or ToastAlertStyle.Recovery;
        _sound = soundEnabled && isAlertLike;
        // 恢复提醒用独立的提示音风格（柔和系，11 选 1），与低量预警的警报声区分开
        _soundStyle = style == ToastAlertStyle.Recovery
            ? (string.IsNullOrEmpty(recoverySoundStyle) ? RecoverySound.DefaultStyle : recoverySoundStyle)
            : (string.IsNullOrEmpty(alertSoundStyle) ? "Standard" : alertSoundStyle);
        _persistent = isAlertLike
                      && alertMode.Equals("Continuous", StringComparison.OrdinalIgnoreCase);
        _position = alertPosition;

        TitleText.Text = title;
        BodyText.Text = body;

        // 预警（仅剩）用橙色、恢复用绿色，便于一眼区分
        bool isRecovery = style == ToastAlertStyle.Recovery || title.Contains("已恢复");
        TitleText.Foreground = isRecovery
            ? new SolidColorBrush(Color.FromRgb(0x6D, 0xDC, 0x6D))
            : new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x4D));

        if (isAlertLike)
        {
            // 警报/恢复视觉：橙色或绿色描边 + 「知道了」按钮，点击才关
            ToastBorder.BorderBrush = isRecovery
                ? new SolidColorBrush(Color.FromRgb(0x2E, 0xB8, 0x72))
                : new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x4D));
            ToastBorder.BorderThickness = new Thickness(1.5);
            DismissBtn.Visibility = Visibility.Visible;
            if (!_persistent)
            {
                // 限时模式：响满设定时长（10/30/60 秒）再自动淡出
                _autoCloseTimer = new DispatcherTimer
                { Interval = TimeSpan.FromSeconds(_minSeconds) };
                _autoCloseTimer.Tick += (_, _) => FadeOutAndClose();
                _autoCloseTimer.Start();
            }
        }
        else
        {
            _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(NoticeSeconds) };
            _autoCloseTimer.Tick += (_, _) => FadeOutAndClose();
            _autoCloseTimer.Start();
        }

        Loaded += (_, _) =>
        {
            lock (Active)
            {
                Active.Add(this);
                RepositionAll();
                if (_sound) PlayOwnSoundUnsafe();
            }
            Opacity = 0;
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.25));
            BeginAnimation(OpacityProperty, fadeIn);
        };
        Closed += (_, _) =>
        {
            lock (Active)
            {
                Active.Remove(this);
                if (!App.IsShuttingDown) RepositionAll();

                // 声音跟窗走：发声窗自己关掉就立即停声；
                // 若还有其他响声窗在，把发声权交给最新的那个（重放它的音色）。
                if (_soundOwner == this)
                {
                    _soundOwner = null;
                    AlarmSound.Stop();
                    RecoverySound.Stop();
                    Active.LastOrDefault(w => w._sound)?.PlayOwnSoundUnsafe();
                }
            }
        };
    }

    /// <summary>接管发声权并播放本窗的提示音（须在 lock(Active) 内调用）。</summary>
    private void PlayOwnSoundUnsafe()
    {
        _soundOwner = this;
        if (_style == ToastAlertStyle.Recovery)
        {
            AlarmSound.Stop(); // 先停另一种循环音，避免两种声音叠着响
            RecoverySound.Play(_soundStyle);
        }
        else
        {
            RecoverySound.Stop();
            AlarmSound.Play(_soundStyle);
        }
    }

    private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // 警报/恢复窗：点击主体等同「知道了」，避免用户找不到关闭方式
        if (_style != ToastAlertStyle.Notice) Dismiss();
    }

    private void DismissBtn_Click(object sender, RoutedEventArgs e) => Dismiss();

    private void Dismiss()
    {
        if (_dismissed) return;
        _dismissed = true;
        // 点「知道了」（或点窗体）立即停声并关闭——不设"至少响满"保底，
        // 保底曾导致限时时长设 1 分钟时点掉后仍继续响近一分钟（用户反馈）。
        FadeOutAndClose();
    }

    private void FadeOutAndClose()
    {
        if (_closing) return;
        _closing = true;
        _autoCloseTimer?.Stop();
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.25));
        fadeOut.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fadeOut);
    }

    /// <summary>按设置的锚点位置堆叠全部活动通知窗。</summary>
    private static void RepositionAll()
    {
        var wa = SystemParameters.WorkArea;
        double margin = 16, gap = 8;

        double totalHeight = 0;
        var heights = new List<double>();
        foreach (var w in Active)
        {
            double h = w.ActualHeight > 0 ? w.ActualHeight : w.Height;
            heights.Add(h);
            totalHeight += h;
        }
        totalHeight += gap * Math.Max(0, Active.Count - 1);

        double top;
        switch (Active.FirstOrDefault()?._position ?? "TopRight")
        {
            case "RightCenter":
                top = wa.Top + Math.Max(0, (wa.Height - totalHeight) / 2);
                break;
            case "BottomRight":
                top = wa.Bottom - margin - totalHeight;
                break;
            default: // TopRight
                top = wa.Top + margin;
                break;
        }

        double left = wa.Right - (Active.FirstOrDefault()?.ActualWidth is > 0 ? Active[0].ActualWidth : 260) - margin;
        double cursor = top;
        for (int i = 0; i < Active.Count; i++)
        {
            var w = Active[i];
            w.Left = left;
            w.Top = cursor;
            cursor += heights[i] + gap;
        }
    }
}
