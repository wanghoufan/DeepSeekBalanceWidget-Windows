using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DeepSeekBalanceWidget.Services;
using Color = System.Windows.Media.Color;

namespace DeepSeekBalanceWidget;

/// <summary>通知样式：Notice=普通通知（自动消失不响声）；Alarm=警报（响声 + 常驻/限时）。</summary>
public enum ToastAlertStyle
{
    Notice,
    Alarm
}

/// <summary>
/// 通知/警报窗。警报模式（Alarm）：
/// - 播放循环警报声（可选），常驻显示直到点击「知道了」（持续模式）；
/// - 或限时模式：至少响 10 秒后自动淡出；
/// - 位置由设置决定（右上 / 右中 / 右下），多窗堆叠、开关时统一重排。
/// 普通通知（Notice）：8 秒自动淡出，无按钮不响声（恢复类通知用）。
/// </summary>
public partial class ToastWindow : Window
{
    private const double MinAlarmSeconds = 10;
    private const double NoticeSeconds = 8;

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
        string alertSoundStyle)
    {
        InitializeComponent();
        _style = style;
        _sound = soundEnabled && style == ToastAlertStyle.Alarm;
        _soundStyle = string.IsNullOrEmpty(alertSoundStyle) ? "Standard" : alertSoundStyle;
        _persistent = style == ToastAlertStyle.Alarm
                      && alertMode.Equals("Continuous", StringComparison.OrdinalIgnoreCase);
        _position = alertPosition;

        TitleText.Text = title;
        BodyText.Text = body;

        // 预警（仅剩）用橙色、恢复用绿色，便于一眼区分
        bool isRecovery = title.Contains("已恢复");
        TitleText.Foreground = isRecovery
            ? new SolidColorBrush(Color.FromRgb(0x6D, 0xDC, 0x6D))
            : new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x4D));

        if (style == ToastAlertStyle.Alarm)
        {
            // 警报视觉：橙色描边 + 「知道了」按钮，点击才关
            ToastBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x4D));
            ToastBorder.BorderThickness = new Thickness(1.5);
            DismissBtn.Visibility = Visibility.Visible;
            if (!_persistent)
            {
                // 限时模式：至少响 10 秒再自动淡出
                _autoCloseTimer = new DispatcherTimer
                { Interval = TimeSpan.FromSeconds(MinAlarmSeconds) };
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
            if (_sound) AlarmSound.Play(_soundStyle);
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
        // 警报窗：点击主体等同「知道了」，避免用户找不到关闭方式
        if (_style == ToastAlertStyle.Alarm) Dismiss();
    }

    private void DismissBtn_Click(object sender, RoutedEventArgs e) => Dismiss();

    private void Dismiss()
    {
        if (_dismissed) return;
        _dismissed = true;
        // 保底：警报声至少持续 10 秒，即便用户立刻点掉
        double elapsed = (DateTime.UtcNow - _shownUtc).TotalSeconds;
        if (_style == ToastAlertStyle.Alarm && elapsed < MinAlarmSeconds && !_persistent)
        {
            _autoCloseTimer!.Stop();
            _autoCloseTimer.Interval = TimeSpan.FromSeconds(MinAlarmSeconds - elapsed);
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
        bool alarmActive;
        lock (Active) alarmActive = Active.Exists(w => w._sound);
        if (!alarmActive) AlarmSound.Stop();
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
