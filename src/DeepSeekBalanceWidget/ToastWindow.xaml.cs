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
/// 通知/警报窗。警报模式（Alarm）：
/// - 播放循环警报声（可选），常驻显示直到点击「知道了」（持续模式）；
/// - 或限时模式：响满设定时长（默认 10 秒，可设 30 秒/1 分钟）后自动淡出；
/// - 位置由设置决定（右上 / 右中 / 右下），多窗堆叠、开关时统一重排。
/// 普通通知（Notice）：8 秒自动淡出，无按钮不响声（恢复类通知用）。
/// </summary>
public partial class ToastWindow : Window
{
    private const double NoticeSeconds = 8;

    /// <summary>限时模式的最短持续时间（秒），由设置决定（10/30/60），也用于「点了知道了至少响满」的保底。</summary>
    private readonly double _minSeconds;

    /// <summary>当前活动的通知窗（含普通与警报），用于统一堆叠定位与警报声计数。</summary>
    private static readonly List<ToastWindow> Active = new();

    private readonly ToastAlertStyle _style;
    private readonly bool _sound;
    private readonly string _soundStyle;
    private readonly string _position;
    private readonly bool _persistent;
    private readonly DispatcherTimer? _autoCloseTimer;
    private bool _dismissed;
    private DateTime _shownUtc = DateTime.UtcNow;

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
            _shownUtc = DateTime.UtcNow;
            lock (Active)
            {
                Active.Add(this);
                RepositionAll();
            }
            if (_sound)
            {
                if (_style == ToastAlertStyle.Recovery)
                    RecoverySound.Play(_soundStyle);
                else
                    AlarmSound.Play(_soundStyle);
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
            }
            StopAlarmSoundIfLast();
        };
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
        // 保底：警报/恢复提示音至少持续设定时长，即便用户立刻点掉
        double elapsed = (DateTime.UtcNow - _shownUtc).TotalSeconds;
        if (_style != ToastAlertStyle.Notice && elapsed < _minSeconds && !_persistent)
        {
            _autoCloseTimer!.Stop();
            _autoCloseTimer.Interval = TimeSpan.FromSeconds(_minSeconds - elapsed);
            _autoCloseTimer.Tick += (_, _) => FadeOutAndClose();
            _autoCloseTimer.Start();
            return;
        }
        FadeOutAndClose();
    }

    private void FadeOutAndClose()
    {
        _autoCloseTimer?.Stop();
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.25));
        fadeOut.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fadeOut);
    }

    private void StopAlarmSoundIfLast()
    {
        bool soundActive;
        lock (Active) soundActive = Active.Exists(w => w._sound);
        if (!soundActive)
        {
            AlarmSound.Stop();
            RecoverySound.Stop();
        }
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
