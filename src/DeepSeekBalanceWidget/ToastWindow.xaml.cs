using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DeepSeekBalanceWidget;

public partial class ToastWindow : Window
{
    // 多个账号同时预警时，从屏幕右下角向上堆叠，避免相互覆盖
    private static int _activeCount;
    private readonly DispatcherTimer _timer;

    public ToastWindow(string title, string body)
    {
        InitializeComponent();
        TitleText.Text = title;
        BodyText.Text = body;

        // 预警（仅剩）用橙色、恢复用绿色，便于一眼区分
        bool isRecovery = title.Contains("已恢复");
        TitleText.Foreground = isRecovery
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6D, 0xDC, 0x6D))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xB0, 0x4D));

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _timer.Tick += (_, _) => FadeOutAndClose();
        _timer.Start();

        Loaded += (_, _) =>
        {
            var wa = SystemParameters.WorkArea;
            int idx = System.Threading.Interlocked.Increment(ref _activeCount);
            // 固定在屏幕右下角（Windows 通知的标准位置），向上堆叠
            Left = wa.Right - Width - 16;
            Top = wa.Bottom - Height - 16 - (idx - 1) * 76;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.25));
            BeginAnimation(OpacityProperty, fadeIn);
        };

        Closed += (_, _) => System.Threading.Interlocked.Decrement(ref _activeCount);
    }

    private void FadeOutAndClose()
    {
        _timer.Stop();
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.25));
        fadeOut.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fadeOut);
    }
}
