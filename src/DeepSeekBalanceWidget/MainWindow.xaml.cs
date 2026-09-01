using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Color = System.Windows.Media.Color;
using System.Windows.Threading;
using DeepSeekBalanceWidget.Models;
using DeepSeekBalanceWidget.Services;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace DeepSeekBalanceWidget;

public partial class MainWindow : Window
{
    private const double EdgeDetectionThreshold = 16;
    private const double EdgeRevealThickness = 12;

    private readonly ConfigService _configService;
    private readonly AppConfig _cfg;
    private readonly CancellationTokenSource _cts = new();
    private IBalanceProvider _provider;
    private readonly ICodexAccountsUsageProvider _codexUsageProvider;
    private readonly CodexConsumptionRateTracker _codexConsumptionTracker = new();
    private readonly CodexQuotaAlertEvaluator _codexQuotaAlerts = new();
    private IOpenCodeUsageProvider _openCodeProvider;
    private readonly OpenCodeQuotaAlertEvaluator _openCodeQuotaAlerts = new();
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _codexTimer;
    private readonly DispatcherTimer _openCodeTimer;
    private readonly DispatcherTimer _savePosTimer;
    private readonly DispatcherTimer _peakTimer;
    private readonly DispatcherTimer _autoHideTimer;
    private AlertState _alertState;
    private bool _isRefreshing;
    private bool _isAuthPaused;
    private bool _isExiting;
    private bool _isMini;
    private bool _isPeak;
    private bool _isDragging;
    private bool _isEdgeHidden;
    private bool _isSettingsOpen;
    private bool _isChangingDockPosition;
    private DockEdge _dockEdge;

    public event Action? RequestExit;
    public event Action<string, bool?>? TrayStatusChanged;

    public MainWindow(ConfigService configService, AppConfig cfg, IBalanceProvider provider)
    {
        InitializeComponent();
        _configService = configService;
        _cfg = cfg;
        _provider = provider;
        _codexUsageProvider = new CcSwitchCodexUsageProvider();
        _openCodeProvider = new OpenCodeUsageProvider(_configService.GetOpenCodeApiKey());

        _savePosTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _savePosTimer.Tick += (_, _) => { _savePosTimer.Stop(); SavePosition(); };

        _autoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _autoHideTimer.Tick += AutoHideTimer_Tick;

        Topmost = _cfg.IsAlwaysOnTop;
        UpdatePinButton();
        UpdateMiniEdgeAutoHideButton();
        ApplySavedPosition();
        ApplyMiniMode(_cfg.UseMiniMode);
        ApplyCodexAppearance();
        ApplyCodexVisibility();
        ApplyMonitoringVisibility();
        Loaded += (_, _) =>
        {
            EvaluateEdgeAutoHide();
            // 加载完成后用当前实际宽度做一次边界钳制：ctor 阶段 IsLoaded=false 跳过了
            // 钳制，若保存的位置配的是更窄的旧胶囊，OC 列加宽后会从右边溢出屏幕。
            if (_dockEdge == DockEdge.None) ClampToWorkArea();
        };

        _alertState = new AlertState(
            _cfg.LastSuccessfulBalance,
            _cfg.LastSuccessfulRefreshUtc,
            _cfg.InLowBalanceState,
            null, null)
        { IsFirstRefreshOfSession = true };

        _timer = new DispatcherTimer
        { Interval = TimeSpan.FromSeconds(Math.Clamp(_cfg.RefreshIntervalSeconds, 5, 3600)) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();

        _codexTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _codexTimer.Tick += async (_, _) => await RefreshCodexUsageAsync();
        if (_cfg.EnableCodexMonitoring) _codexTimer.Start();

        _openCodeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _openCodeTimer.Tick += async (_, _) => await RefreshOpenCodeUsageAsync();
        if (_cfg.EnableOpenCodeMonitoring) _openCodeTimer.Start();

        _peakTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _peakTimer.Tick += (_, _) => RefreshPeakStatus();
        _peakTimer.Start();

        RefreshPeakStatus();
        _ = RefreshAsync();
        if (_cfg.EnableCodexMonitoring) _ = RefreshCodexUsageAsync();
        if (_cfg.EnableOpenCodeMonitoring) _ = RefreshOpenCodeUsageAsync();
    }

    private void ApplySavedPosition()
    {
        bool hasSaved = _cfg.WindowLeft is double && _cfg.WindowTop is double;
        if (hasSaved)
        {
            Left = _cfg.WindowLeft!.Value;
            Top = _cfg.WindowTop!.Value;
        }
        else
        {
            ApplyDefaultCorner();
        }
        if (IsOffScreen()) ApplyDefaultCorner();
    }

    private void ApplyDefaultCorner()
    {
        var wa = SystemParameters.WorkArea;
        switch (_cfg.DefaultCorner)
        {
            case "BottomLeft":
                Left = wa.Left + 20;
                Top = wa.Bottom - Height - 20;
                break;
            case "BottomRight":
                Left = wa.Right - Width - 20;
                Top = wa.Bottom - Height - 20;
                break;
            default: // Remember：无历史坐标时回退右下角
                Left = wa.Right - Width - 20;
                Top = wa.Bottom - Height - 20;
                break;
        }
    }

    private bool IsOffScreen()
    {
        var wa = SystemParameters.WorkArea;
        return (Left + Width < wa.Left) || (Left > wa.Right)
            || (Top + Height < wa.Top) || (Top > wa.Bottom);
    }

    private void ClampToWorkArea()
    {
        var wa = SystemParameters.WorkArea;
        // 迷你模式 Width=NaN，必须用 ActualWidth/ActualHeight，否则 NaN 会把坐标算成 NaN
        double w = !double.IsNaN(Width) ? Width : (ActualWidth > 0 ? ActualWidth : 420);
        double h = !double.IsNaN(Height) ? Height : (ActualHeight > 0 ? ActualHeight : 120);
        Left = Math.Clamp(Left, wa.Left, Math.Max(wa.Left, wa.Right - w));
        Top = Math.Clamp(Top, wa.Top, Math.Max(wa.Top, wa.Bottom - h));
    }

    public void ResetPosition()
    {
        DisableCurrentDock();
        ApplyDefaultCorner();
        SavePosition();
    }

    private void SavePosition()
    {
        if (double.IsNaN(Left) || double.IsNaN(Top)) return;
        var position = _dockEdge == DockEdge.None
            ? new Point(Left, Top)
            : GetDockPosition(hidden: false);
        _cfg.WindowLeft = position.X;
        _cfg.WindowTop = position.Y;
        _configService.Save(_cfg);
    }

    private void ApplyMiniMode(bool mini)
    {
        _isMini = mini;
        Card.Visibility = mini ? Visibility.Collapsed : Visibility.Visible;
        MiniCard.Visibility = mini ? Visibility.Visible : Visibility.Collapsed;
        // 迷你模式宽度自适应内容（NaN=Auto）：估算公式总会留出多余空白，导致按钮右侧空一段
        Width = mini ? double.NaN : 420;
        if (IsLoaded)
        {
            if (_dockEdge != DockEdge.None)
                SetDockPosition(_isEdgeHidden);
            else
                ClampToWorkArea(); // 尺寸变化后只做边界 Clamp，不强制回角落
        }
        RearrangeMiniBlocks();
    }

    /// <summary>按 _cfg.AgentOrder 重排胶囊内容区（MiniContentPanel）里的区块，按钮固定在外层 Grid.Column 1（最右）。</summary>
    private void RearrangeMiniBlocks()
    {
        var order = _cfg.AgentOrder ?? new List<string>();
        var blocks = new Dictionary<string, UIElement>
        {
            ["deepseek"] = MiniDeepSeekBlock,
            ["chatgpt"] = MiniGptBlock,
            ["opencode"] = MiniOpenCodeBlock,
            ["workbuddy"] = MiniWorkbuddyBlock
        };

        // 内容区（DS / GPT / OC / WB）：按 AgentOrder 排序，已关闭的区块跳过不占位
        int index = 0;
        foreach (string kind in order)
        {
            if (!blocks.TryGetValue(kind, out var block)) continue;
            if (block.Visibility == Visibility.Collapsed) continue;
            if (index >= MiniContentPanel.Children.Count
                || !ReferenceEquals(MiniContentPanel.Children[index], block))
            {
                MiniContentPanel.Children.Remove(block);
                MiniContentPanel.Children.Insert(Math.Min(index, MiniContentPanel.Children.Count), block);
            }
            index++;
        }
        // 按钮（MiniButtonBar）和刷新时间在 XAML 中已固定位置，不在内容区里操作
    }

    private void MiniBtn_Click(object sender, RoutedEventArgs e)
    {
        ApplyMiniMode(!_isMini);
        _cfg.UseMiniMode = _isMini;
        _configService.Save(_cfg);
    }

    private void MiniCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        if (IsInsideButton(e.OriginalSource as DependencyObject)) return;
        e.Handled = true;

        if (e.ClickCount >= 2)
        {
            ApplyMiniMode(false);
            _cfg.UseMiniMode = false;
            _configService.Save(_cfg);
            return;
        }

        DragWindow();
    }

    private void UpdateMiniEdgeAutoHideButton()
    {
        MiniEdgeAutoHideBtn.Foreground = _cfg.EnableEdgeAutoHide
            ? new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7))
            : new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
        MiniEdgeAutoHideBtn.ToolTip = _cfg.EnableEdgeAutoHide ? "关闭贴边隐藏" : "开启贴边隐藏";
    }

    private void MiniEdgeAutoHideBtn_Click(object sender, RoutedEventArgs e)
    {
        _cfg.EnableEdgeAutoHide = !_cfg.EnableEdgeAutoHide;
        _configService.Save(_cfg);
        UpdateMiniEdgeAutoHideButton();
        EvaluateEdgeAutoHide();
    }

    private void MiniMinBtn_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MiniCloseBtn_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(this,
            "确定退出 DeepSeek 余额监控吗？", "退出确认",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes) ExitApp();
    }

    private void UpdatePinButton()
    {
        // 置顶按钮高亮表示当前置顶
        PinBtn.Foreground = Topmost
            ? new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7))
            : new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
        PinBtn.ToolTip = Topmost ? "取消置顶" : "始终置顶";
    }

    private void PinBtn_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        _cfg.IsAlwaysOnTop = Topmost;
        _configService.Save(_cfg);
        UpdatePinButton();
    }

    private void TrayBtn_Click(object sender, RoutedEventArgs e)
    {
        SavePosition();
        Hide(); // 收进托盘：隐藏常驻
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(this,
            "确定退出 DeepSeek 余额监控吗？", "退出确认",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes) ExitApp();
    }

    private void Window_LocationChanged(object sender, EventArgs e)
    {
        if (_isChangingDockPosition) return;
        _savePosTimer.Stop();
        _savePosTimer.Start();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        if (IsInsideButton(e.OriginalSource as DependencyObject)) return;
        DragWindow();
    }

    private void DragWindow()
    {
        _autoHideTimer.Stop();
        _isDragging = true;
        _isEdgeHidden = false;
        _dockEdge = DockEdge.None;
        try { DragMove(); } catch (InvalidOperationException) { }
        finally { _isDragging = false; }

        EvaluateEdgeAutoHide();
        SavePosition();
    }

    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        _autoHideTimer.Stop();
        if (_isEdgeHidden) SetDockPosition(hidden: false);
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_cfg.EnableEdgeAutoHide && _dockEdge != DockEdge.None && !_isDragging)
            _autoHideTimer.Start();
    }

    private void AutoHideTimer_Tick(object? sender, EventArgs e)
    {
        if (IsMouseOver)
        {
            _autoHideTimer.Stop();
            return;
        }
        if (_isDragging || _isSettingsOpen || ContextMenu?.IsOpen == true) return;

        _autoHideTimer.Stop();
        if (_cfg.EnableEdgeAutoHide && _dockEdge != DockEdge.None)
            SetDockPosition(hidden: true);
    }

    private void EvaluateEdgeAutoHide()
    {
        if (!_cfg.EnableEdgeAutoHide || _isDragging || !IsLoaded)
        {
            if (!_cfg.EnableEdgeAutoHide) DisableCurrentDock();
            return;
        }

        var window = CurrentWindowRect();
        var edge = EdgeAutoHideCalculator.Detect(
            window, SystemParameters.WorkArea, EdgeDetectionThreshold);
        if (edge == DockEdge.None) return;

        _dockEdge = edge;
        SetDockPosition(hidden: true);
        SavePosition();
    }

    private Rect CurrentWindowRect()
    {
        double width = ActualWidth > 0 ? ActualWidth : Width;
        double height = ActualHeight > 0 ? ActualHeight : Height;
        return new Rect(Left, Top, width, height);
    }

    private Point GetDockPosition(bool hidden)
    {
        var window = CurrentWindowRect();
        return hidden
            ? EdgeAutoHideCalculator.HiddenPosition(
                _dockEdge, window, SystemParameters.WorkArea, EdgeRevealThickness)
            : EdgeAutoHideCalculator.VisiblePosition(
                _dockEdge, window, SystemParameters.WorkArea);
    }

    private void SetDockPosition(bool hidden)
    {
        if (_dockEdge == DockEdge.None) return;
        var position = GetDockPosition(hidden);
        _isChangingDockPosition = true;
        try
        {
            Left = position.X;
            Top = position.Y;
            _isEdgeHidden = hidden;
        }
        finally { _isChangingDockPosition = false; }
    }

    private void DisableCurrentDock()
    {
        _autoHideTimer.Stop();
        if (_dockEdge != DockEdge.None && _isEdgeHidden)
            SetDockPosition(hidden: false);
        _isEdgeHidden = false;
        _dockEdge = DockEdge.None;
    }

    private static bool IsInsideButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private void RefreshMenu_Click(object sender, RoutedEventArgs e) => _ = RefreshNowAsync();
    private void SettingsMenu_Click(object sender, RoutedEventArgs e) => OpenSettings();
    private void ResetPositionMenu_Click(object sender, RoutedEventArgs e) => ResetPosition();
    private void ExitMenu_Click(object sender, RoutedEventArgs e) => ExitApp();

    /// <summary>菜单「测试恢复提醒」：手动触发一次绿色呼吸边框 + 恢复样式弹窗（含提示音），便于验证视觉效果。</summary>
    private void TestRecoveryAlertMenu_Click(object sender, RoutedEventArgs e)
    {
        FlashRecoveryGlow();
        if (_cfg.ShowToastNotifications)
        {
            ToastService.Show(this,
                "测试 · 5 小时额度已恢复",
                "额度已重置回 100%（这是一次手动测试弹窗）", _cfg, ToastAlertStyle.Recovery);
        }
    }

    /// <summary>菜单「测试额度预警」：手动弹一次低量预警样式（警报声 + 常驻），便于与恢复提醒对比。</summary>
    private void TestLowAlertMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_cfg.ShowToastNotifications)
        {
            ToastService.Show(this,
                "测试 · 5 小时额度仅剩 13%",
                "预计 09-01 18:00 恢复，建议提前做好上下文交接（手动测试弹窗）",
                _cfg, ToastAlertStyle.Alarm);
        }
    }

    public void RefreshNow() => _ = RefreshNowAsync();

    private async Task RefreshNowAsync()
    {
        _isAuthPaused = false;
        _timer.Start();
        var codexRefresh = _cfg.EnableCodexMonitoring
            ? RefreshCodexUsageAsync()
            : Task.CompletedTask;
        var openCodeRefresh = _cfg.EnableOpenCodeMonitoring
            ? RefreshOpenCodeUsageAsync()
            : Task.CompletedTask;
        await Task.WhenAll(RefreshAsync(), codexRefresh, openCodeRefresh);
    }

    public void OpenSettings()
    {
        if (_isEdgeHidden) SetDockPosition(hidden: false);
        var dlg = new SettingsWindow(_configService, _cfg) { Owner = this };
        _isSettingsOpen = true;
        bool saved;
        try { saved = dlg.ShowDialog() == true; }
        finally { _isSettingsOpen = false; }
        if (saved)
        {
            Topmost = _cfg.IsAlwaysOnTop;
            UpdatePinButton();
            ApplyMiniMode(_cfg.UseMiniMode);
            ApplyCodexAppearance();
            ApplyCodexMonitoring();
            // OpenCode：Key 可能已变更，重建 Provider 后再应用可见性与定时器
            if (_openCodeProvider is IDisposable d) d.Dispose();
            _openCodeProvider = new OpenCodeUsageProvider(_configService.GetOpenCodeApiKey());
            ApplyOpenCodeVisibility();
            ApplyDsVisibility();
            ApplyWorkbuddyVisibility();
            RearrangeMiniBlocks();
            _timer.Interval = TimeSpan.FromSeconds(Math.Clamp(_cfg.RefreshIntervalSeconds, 5, 3600));
            if (_provider is DeepSeekApiClient) RebuildProvider();
            _isAuthPaused = false;
            _timer.Start();
            RefreshPeakStatus(); // 设置变更后立即刷新高峰状态
            _ = RefreshAsync();
            EvaluateEdgeAutoHide();
        }
        else if (_cfg.EnableEdgeAutoHide && _dockEdge != DockEdge.None)
            _autoHideTimer.Start();
    }

    public void RestoreAndActivate()
    {
        Show();
        WindowState = WindowState.Normal;
        if (_isEdgeHidden) SetDockPosition(hidden: false);
        Activate();
    }

    private void RebuildProvider()
    {
        _provider = new DeepSeekApiClient(_configService.GetApiKey() ?? string.Empty);
    }

    public void ExitApp()
    {
        if (_isExiting) return;
        _isExiting = true;
        SavePosition();
        RequestExit?.Invoke();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExiting)
        {
            e.Cancel = true;
            SavePosition();
            Hide();
        }
        base.OnClosing(e);
    }

    private async Task RefreshAsync()
    {
        if (_isRefreshing || _isAuthPaused) return;
        _isRefreshing = true;
        try
        {
            var json = await _provider.GetBalanceJsonAsync(_cts.Token);
            var parsed = BalanceParser.Parse(json);
            if (!parsed.Success)
            {
                ShowError(parsed.Error!);
                RaiseTrayError();
                return;
            }
            var selection = CurrencySelector.Select(parsed.Balances, _cfg.SelectedCurrency);
            if (!selection.Found)
            {
                ShowUnavailableCurrency(selection.SelectedCurrency);
                RaiseTrayError();
                return;
            }
            var bal = selection.Balance!;

            decimal? prev = _cfg.LastSuccessfulBalance;
            decimal? change = BalanceChangeCalculator.Change(prev, bal.Total);
            decimal? pct = BalanceChangeCalculator.Percent(prev, bal.Total);

            ApplyBalance(bal, parsed.IsConsistent, change, pct);
            RaiseTrayStatus(bal, change);

            var decision = AlertEvaluator.Evaluate(_alertState, bal, _cfg);
            _alertState = decision.NewState;

            _cfg.LastSuccessfulBalance = decision.NewState.LastSuccessfulBalance;
            _cfg.LastSuccessfulRefreshUtc = decision.NewState.LastSuccessfulRefreshUtc;
            _cfg.InLowBalanceState = decision.NewState.InLowBalanceState;
            _configService.Save(_cfg);

            // ShowToastNotifications 此前从未被读取（配置项是死代码），这里一并接上，
            // 让余额类告警与 GPT / OpenCode 额度预警都遵守「允许弹窗通知」开关。
            // 低余额 / 异常下降走警报样式（循环警报声 + 常驻，需手动关闭）。
            if (decision.ShowLowBalance && _cfg.ShowToastNotifications)
                ToastService.Show(this, "低余额提醒",
                    $"余额 {bal.Total:0.00} {bal.Currency} 低于阈值 {_cfg.LowBalanceThreshold:0.00}",
                    _cfg, ToastAlertStyle.Alarm);
            if (decision.ShowAbnormalDrop && _cfg.ShowToastNotifications)
                ToastService.Show(this, "余额异常下降",
                    $"单次下降 {Math.Abs(pct ?? 0):0.0}%", _cfg, ToastAlertStyle.Alarm);
        }
        catch (OperationCanceledException) { }
        catch (ApiException ex)
        {
            if (ex.IsAuthFailure)
            {
                _isAuthPaused = true;
                _timer.Stop();
                ShowError("认证失败：请检查 API Key");
            }
            else ShowError("刷新失败：" + ex.Message);
            RaiseTrayError();
        }
        catch (Exception ex)
        {
            ShowError("刷新失败：" + ex.Message);
            RaiseTrayError();
        }
        finally { _isRefreshing = false; }
    }

    private bool _isCodexRefreshing;

    private async Task RefreshCodexUsageAsync()
    {
        if (!_cfg.EnableCodexMonitoring || _isCodexRefreshing) return;
        _isCodexRefreshing = true;
        try
        {
            var usages = await _codexUsageProvider.GetUsagesAsync(_cts.Token);
            ApplyCodexUsages(usages);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            ApplyCodexUsages(Array.Empty<CodexAccountUsageSnapshot>());
        }
        finally { _isCodexRefreshing = false; }
    }

    private void ApplyCodexMonitoring()
    {
        ApplyCodexVisibility();
        ApplyMiniMode(_isMini);
        if (_cfg.EnableCodexMonitoring)
        {
            _codexTimer.Start();
            _ = RefreshCodexUsageAsync();
        }
        else
        {
            _codexTimer.Stop();
        }
    }

    private void ApplyCodexVisibility()
    {
        var visibility = _cfg.EnableCodexMonitoring ? Visibility.Visible : Visibility.Collapsed;
        CodexPanel.Visibility = visibility;
        MiniGptBlock.Visibility = visibility;
    }

    /// <summary>统一应用各监测项（DS / GPT / OC / WB）在胶囊与详细面板中的可见性与定时器。</summary>
    private void ApplyMonitoringVisibility()
    {
        ApplyCodexVisibility();
        ApplyOpenCodeVisibility();
        ApplyDsVisibility();
        ApplyWorkbuddyVisibility();
        RearrangeMiniBlocks();
    }

    private void ApplyOpenCodeVisibility()
    {
        var visibility = _cfg.EnableOpenCodeMonitoring ? Visibility.Visible : Visibility.Collapsed;
        OpenCodePanel.Visibility = visibility;
        MiniOpenCodeBlock.Visibility = visibility;
        // 构造函数阶段定时器尚未创建，容错跳过（仅设置可见性，末尾会主动拉取一次）
        if (_openCodeTimer is null) return;
        if (_cfg.EnableOpenCodeMonitoring)
        {
            _openCodeTimer.Start();
            _ = RefreshOpenCodeUsageAsync();
        }
        else
        {
            _openCodeTimer.Stop();
        }
    }

    private void ApplyDsVisibility()
    {
        var visibility = _cfg.EnableDeepSeekMonitoring ? Visibility.Visible : Visibility.Collapsed;
        BalanceRow.Visibility = visibility;
        MiniDeepSeekBlock.Visibility = visibility;
    }

    private void ApplyWorkbuddyVisibility()
        => MiniWorkbuddyBlock.Visibility = _cfg.EnableWorkbuddyMonitoring
            ? Visibility.Visible
            : Visibility.Collapsed;

    private bool _isOpenCodeRefreshing;

    private async Task RefreshOpenCodeUsageAsync()
    {
        if (!_cfg.EnableOpenCodeMonitoring || _isOpenCodeRefreshing) return;
        _isOpenCodeRefreshing = true;
        try
        {
            var snapshot = await _openCodeProvider.GetUsageAsync(_cts.Token);
            ApplyOpenCodeUsage(snapshot);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            ApplyOpenCodeUsage(OpenCodeUsageSnapshot.Unavailable("刷新失败"));
        }
        finally { _isOpenCodeRefreshing = false; }
    }

    private void ApplyOpenCodeUsage(OpenCodeUsageSnapshot snapshot)
    {
        if (!snapshot.IsAvailable)
        {
            // 状态可见化：失败原因直接显示在区块标题与胶囊标签上，不再只藏 tooltip
            string reason = snapshot.Error ?? "暂不可用";
            OpenCodeTitleText.Text = $"OpenCode Go 额度（{reason}）";
            OpenCodeTitleText.Foreground = new SolidColorBrush(Colors.Orange);
            MiniOcLabel.Foreground = new SolidColorBrush(Colors.Orange);
            MiniOpenCodeBlock.ToolTip = reason;

            OpenCodeFivePct.Text = "--";
            OpenCodeFiveUsed.Text = OpenCodeFiveReset.Text = OpenCodeFiveCd.Text = "--";
            OpenCodeWeeklyPct.Text = "--";
            OpenCodeWeeklyUsed.Text = OpenCodeWeeklyReset.Text = OpenCodeWeeklyCd.Text = "--";
            OpenCodeMonthlyPct.Text = "--";
            OpenCodeMonthlyUsed.Text = OpenCodeMonthlyReset.Text = OpenCodeMonthlyCd.Text = "--";
            OpenCodePanel.ToolTip = reason;
            ClearOcMiniRow(MiniOcFivePct, MiniOcFiveCd, MiniOcFiveBar);
            ClearOcMiniRow(MiniOcWeeklyPct, MiniOcWeeklyCd, MiniOcWeeklyBar);
            ClearOcMiniRow(MiniOcMonthlyPct, MiniOcMonthlyCd, MiniOcMonthlyBar);
            return;
        }

        OpenCodeTitleText.Text = "OpenCode Go 额度";
        OpenCodeTitleText.Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xEB, 0xFF));
        MiniOcLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xEB, 0xFF));
        OpenCodePanel.ToolTip = null;
        var now = DateTimeOffset.Now;
        var byKind = snapshot.Windows.ToDictionary(window => window.Kind);

        ApplyOcRow(byKind.GetValueOrDefault("rolling"), "5h",
            OpenCodeFivePct, OpenCodeFiveUsed, OpenCodeFiveReset, OpenCodeFiveCd,
            MiniOcFivePct, MiniOcFiveCd, MiniOcFiveBar, now);
        ApplyOcRow(byKind.GetValueOrDefault("weekly"), "周",
            OpenCodeWeeklyPct, OpenCodeWeeklyUsed, OpenCodeWeeklyReset, OpenCodeWeeklyCd,
            MiniOcWeeklyPct, MiniOcWeeklyCd, MiniOcWeeklyBar, now);
        ApplyOcRow(byKind.GetValueOrDefault("monthly"), "月",
            OpenCodeMonthlyPct, OpenCodeMonthlyUsed, OpenCodeMonthlyReset, OpenCodeMonthlyCd,
            MiniOcMonthlyPct, MiniOcMonthlyCd, MiniOcMonthlyBar, now);

        RaiseOpenCodeQuotaAlerts(snapshot);
    }

    private static void ApplyOcRow(
        OpenCodeUsageWindow? window,
        string shortLabel,
        System.Windows.Controls.TextBlock pct,
        System.Windows.Controls.TextBlock used,
        System.Windows.Controls.TextBlock reset,
        System.Windows.Controls.TextBlock cd,
        System.Windows.Controls.TextBlock miniPct,
        System.Windows.Controls.TextBlock miniCd,
        System.Windows.Controls.Border miniBar,
        DateTimeOffset now)
    {
        if (window is null)
        {
            pct.Text = used.Text = reset.Text = cd.Text = "--";
            ClearOcMiniRow(miniPct, miniCd, miniBar);
            return;
        }

        pct.Text = $"{window.RemainingPercent}%";
        used.Text = OpenCodeUsageFormatter.FormatUsedEstimate(window);
        used.ToolTip = "API 只返回百分比，美元金额按 Go 套餐固定限额换算估算";
        reset.Text = OpenCodeUsageFormatter.FormatResetTime(window);
        cd.Text = OpenCodeUsageFormatter.FormatCountdown(window, now);
        pct.ToolTip = $"{shortLabel} 已用 {window.UsedPercent}%";
        System.Windows.Controls.ToolTipService.SetToolTip(cd, window.ResetsAt is DateTimeOffset r
            ? $"恢复时间：{r.ToLocalTime():yyyy-MM-dd HH:mm}"
            : null);

        miniPct.Text = $"{window.RemainingPercent}%";
        miniCd.Text = OpenCodeUsageFormatter.FormatCountdownShort(window, now);
        UpdateOcBar(miniBar, window.RemainingPercent);
    }

    private static void ClearOcMiniRow(
        System.Windows.Controls.TextBlock pct,
        System.Windows.Controls.TextBlock cd,
        System.Windows.Controls.Border bar)
    {
        pct.Text = "--";
        cd.Text = "--";
        bar.Width = 0;
    }

    /// <summary>进度条填充：宽度=剩余百分比，颜色按剩余量分档（≥60 绿 / 30-59 黄 / <30 红）。</summary>
    private static void UpdateOcBar(System.Windows.Controls.Border bar, int remainingPercent)
    {
        const double trackWidth = 28;
        bar.Width = Math.Max(0, Math.Min(trackWidth, trackWidth * remainingPercent / 100.0));
        var color = remainingPercent >= 60
            ? Color.FromRgb(0x78, 0xD7, 0x9A)
            : remainingPercent >= 30
                ? Color.FromRgb(0xEF, 0x9F, 0x27)
                : Color.FromRgb(0xE2, 0x4B, 0x4A);
        bar.Background = new SolidColorBrush(color);
    }

    /// <summary>评估 OpenCode 额度预警并弹窗：只做低量预警（响声+常驻），不做恢复提醒。</summary>
    private void RaiseOpenCodeQuotaAlerts(OpenCodeUsageSnapshot snapshot)
    {
        if (!_cfg.ShowToastNotifications) return;

        foreach (var alert in _openCodeQuotaAlerts.Evaluate(snapshot, _cfg, DateTimeOffset.Now))
        {
            string usedHint = alert.EstimatedUsedUsd.HasValue
                ? $"（已用 ≈ ${alert.EstimatedUsedUsd.Value:0.##}）"
                : string.Empty;
            string resetHint = alert.ResetsAt is DateTimeOffset resetsAt
                ? $"预计 {resetsAt.ToLocalTime():MM-dd HH:mm} 恢复"
                : "恢复时间未知";
            ToastService.Show(this,
                $"OpenCode · {alert.WindowLabel}仅剩 {alert.RemainingPercent}%",
                $"{usedHint}{resetHint}", _cfg, ToastAlertStyle.Alarm);
        }
    }

    private void ApplyCodexAppearance()
    {
        double size = Math.Clamp(_cfg.CodexFontSize, 10, 24);
        FontWeight weight = _cfg.CodexFontStyle switch
        {
            "Regular" => FontWeights.Normal,
            "Bold" => FontWeights.Bold,
            _ => FontWeights.SemiBold
        };

        var font = new System.Windows.Media.FontFamily("Segoe UI");
        foreach (var text in new[]
        {
            CodexAccount1Name, CodexAccount2Name,
            CodexFiveText, CodexFiveResetText, CodexWeeklyText, CodexWeeklyResetText,
            CodexFiveText2, CodexFiveResetText2, CodexWeeklyText2, CodexWeeklyResetText2,
            MiniDsLabel, MiniBalanceText, MiniChangeText, MiniPeakLabel,
            MiniGptA1Label, MiniGptA1Five, MiniGptA1FiveCd, MiniGptA1Weekly, MiniGptA1WeeklyCd,
            MiniGptA2Label, MiniGptA2Five, MiniGptA2FiveCd, MiniGptA2Weekly, MiniGptA2WeeklyCd,
            MiniOcLabel, MiniOcFivePct, MiniOcFiveCd, MiniOcWeeklyPct, MiniOcWeeklyCd,
            MiniOcMonthlyPct, MiniOcMonthlyCd,
            MiniWorkbuddyBlock, MiniRefreshTimeText
        })
        {
            text.FontFamily = font;
            text.FontWeight = weight;
        }

        CodexAccount1Name.FontSize = Math.Max(11, size - 1);
        CodexAccount2Name.FontSize = CodexAccount1Name.FontSize;
        CodexFiveText.FontSize = size + 2;
        CodexWeeklyText.FontSize = size + 2;
        CodexFiveResetText.FontSize = Math.Max(11, size - 1);
        CodexWeeklyResetText.FontSize = CodexFiveResetText.FontSize;
        CodexFiveText2.FontSize = size + 2;
        CodexWeeklyText2.FontSize = size + 2;
        CodexFiveResetText2.FontSize = CodexFiveResetText.FontSize;
        CodexWeeklyResetText2.FontSize = CodexFiveResetText.FontSize;

        MiniDsLabel.FontSize = Math.Max(11, size - 1);
        MiniBalanceText.FontSize = Math.Max(12, size);
        MiniChangeText.FontSize = Math.Max(9, size - 5);
        MiniPeakLabel.FontSize = Math.Max(10, size - 3);
        MiniGptA1Label.FontSize = Math.Max(10, size - 2);
        MiniGptA2Label.FontSize = MiniGptA1Label.FontSize;
        MiniGptA1Five.FontSize = Math.Max(10, size - 2);
        MiniGptA2Five.FontSize = MiniGptA1Five.FontSize;
        MiniGptA1Weekly.FontSize = MiniGptA1Five.FontSize;
        MiniGptA2Weekly.FontSize = MiniGptA1Five.FontSize;
        MiniGptA1FiveCd.FontSize = Math.Max(10, size - 3);
        MiniGptA2FiveCd.FontSize = MiniGptA1FiveCd.FontSize;
        MiniGptA1WeeklyCd.FontSize = MiniGptA1FiveCd.FontSize;
        MiniGptA2WeeklyCd.FontSize = MiniGptA1FiveCd.FontSize;
        MiniOcLabel.FontSize = Math.Max(10, size - 2);
        MiniOcFivePct.FontSize = Math.Max(10, size - 2);
        MiniOcWeeklyPct.FontSize = MiniOcFivePct.FontSize;
        MiniOcMonthlyPct.FontSize = MiniOcFivePct.FontSize;
        MiniOcFiveCd.FontSize = Math.Max(10, size - 3);
        MiniOcWeeklyCd.FontSize = MiniOcFiveCd.FontSize;
        MiniOcMonthlyCd.FontSize = MiniOcFiveCd.FontSize;
        MiniWorkbuddyBlock.FontSize = Math.Max(11, size - 1);
    }

    private void ApplyCodexUsages(IReadOnlyList<CodexAccountUsageSnapshot> usages)
    {
        var accounts = usages.Take(2).ToArray();
        if (accounts.Length == 0)
        {
            CodexAccount1Row.Visibility = Visibility.Visible;
            CodexAccount1Name.Text = "ChatGPT 账号";
            CodexAccount1Name.Foreground = new SolidColorBrush(Colors.Orange);
            CodexAccount1Name.ToolTip = "无法读取 CC Switch 账号额度";
            ClearCodexCells(CodexFiveText, CodexFiveResetText, CodexWeeklyText, CodexWeeklyResetText);
            CodexAccount2Row.Visibility = Visibility.Collapsed;
            MiniGptA1Label.Text = "M";
            ClearMiniGptRow(MiniGptA1Label, MiniGptA1Five, MiniGptA1FiveCd, MiniGptA1Weekly, MiniGptA1WeeklyCd);
            ClearMiniGptRow(MiniGptA2Label, MiniGptA2Five, MiniGptA2FiveCd, MiniGptA2Weekly, MiniGptA2WeeklyCd);
            return;
        }

        ApplyCodexAccount(accounts[0], CodexAccount1Row, CodexAccount1Name,
            CodexFiveText, CodexFiveResetText, CodexWeeklyText, CodexWeeklyResetText,
            MiniGptA1Label, MiniGptA1Five, MiniGptA1FiveCd, MiniGptA1Weekly, MiniGptA1WeeklyCd);

        bool hasSecond = accounts.Length > 1;
        CodexAccount2Row.Visibility = hasSecond ? Visibility.Visible : Visibility.Collapsed;
        if (hasSecond)
        {
            ApplyCodexAccount(accounts[1], CodexAccount2Row, CodexAccount2Name,
                CodexFiveText2, CodexFiveResetText2, CodexWeeklyText2, CodexWeeklyResetText2,
                MiniGptA2Label, MiniGptA2Five, MiniGptA2FiveCd, MiniGptA2Weekly, MiniGptA2WeeklyCd);
        }
        else
        {
            ClearMiniGptRow(MiniGptA2Label, MiniGptA2Five, MiniGptA2FiveCd, MiniGptA2Weekly, MiniGptA2WeeklyCd);
        }

        RaiseCodexQuotaAlerts(accounts);
    }

    /// <summary>
    /// 评估 ChatGPT 额度预警并弹窗：剩余额度降到阈值档位时预警，额度恢复（进入新周期、
    /// 重置回满）时一律通知，并让胶囊边框闪绿色呼吸灯以示区别（低量预警是橙色/红色调）。
    /// 文案会区分 5 小时额度 / 周额度，并写明具体账号。
    /// </summary>
    private void RaiseCodexQuotaAlerts(IReadOnlyList<CodexAccountUsageSnapshot> accounts)
    {
        foreach (var alert in _codexQuotaAlerts.Evaluate(accounts, _cfg, DateTimeOffset.Now))
        {
            string who = ShortAccountName(alert.Email);
            string what = alert.WindowLabel;

            if (alert.IsRecovery)
            {
                // 绿色呼吸边框是恢复提醒的主视觉，即使关闭弹窗通知也保留。
                FlashRecoveryGlow();
                if (!_cfg.ShowToastNotifications) continue;
                ToastService.Show(this,
                    $"{who} · {what}已恢复",
                    $"额度已重置回 {alert.RemainingPercent}%", _cfg, ToastAlertStyle.Recovery);
                continue;
            }

            if (!_cfg.ShowToastNotifications) continue;

            string resetHint = alert.ResetsAt is DateTimeOffset resetsAt
                ? $"预计 {resetsAt.ToLocalTime():MM-dd HH:mm} 恢复"
                : "恢复时间未知";
            ToastService.Show(this,
                $"{who} · {what}仅剩 {alert.RemainingPercent}%",
                $"{resetHint}，建议提前做好上下文交接",
                _cfg, ToastAlertStyle.Alarm);
        }
    }

    /// <summary>恢复提醒绿色呼吸边框：暗绿 ↔ 亮绿往复 6 次（约 6 秒）后恢复原玻璃边框。</summary>
    private void FlashRecoveryGlow()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x2E, 0xB8, 0x72));
        var anim = new ColorAnimation
        {
            From = Color.FromRgb(0x1D, 0x7A, 0x46),
            To = Color.FromRgb(0x53, 0xF0, 0x8C),
            Duration = TimeSpan.FromMilliseconds(500),
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(6)
        };
        anim.Completed += (_, _) => RestoreCardBorders();
        brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);

        Card.BorderBrush = brush;
        Card.BorderThickness = new Thickness(2);
        MiniCard.BorderBrush = brush;
        MiniCard.BorderThickness = new Thickness(2);
    }

    private void RestoreCardBorders()
    {
        if (TryFindResource("GlassBorderBrush") is System.Windows.Media.Brush glass)
        {
            Card.BorderBrush = glass;
            MiniCard.BorderBrush = glass;
        }
        Card.BorderThickness = new Thickness(1);
        MiniCard.BorderThickness = new Thickness(1);
    }

    private void ApplyCodexAccount(
        CodexAccountUsageSnapshot account,
        System.Windows.Controls.Grid rowPanel,
        System.Windows.Controls.TextBlock nameText,
        System.Windows.Controls.TextBlock fivePercentText,
        System.Windows.Controls.TextBlock fiveResetText,
        System.Windows.Controls.TextBlock weeklyPercentText,
        System.Windows.Controls.TextBlock weeklyResetText,
        System.Windows.Controls.TextBlock miniLabel,
        System.Windows.Controls.TextBlock miniFive,
        System.Windows.Controls.TextBlock miniFiveCd,
        System.Windows.Controls.TextBlock miniWeekly,
        System.Windows.Controls.TextBlock miniWeeklyCd)
    {
        nameText.Text = ShortAccountName(account.Email) + (account.IsStale ? " *" : "");
        nameText.ToolTip = FormatAccountToolTip(account);
        nameText.Foreground = account.IsStale
            ? new SolidColorBrush(Colors.Orange)
            : new SolidColorBrush(Color.FromRgb(0xDD, 0xEB, 0xFF));
        miniLabel.Text = account.MiniLabel;

        if (!account.Usage.IsAvailable || account.Usage.Windows.Count == 0)
        {
            nameText.Text = (ShortAccountName(account.Email) ?? "----") + " 暂不可用";
            nameText.ToolTip = account.RefreshError ?? account.Usage.Error ?? "未返回额度窗口";
            nameText.Foreground = new SolidColorBrush(Colors.Orange);
            ClearCodexCells(fivePercentText, fiveResetText, weeklyPercentText, weeklyResetText);
            ClearMiniGptRow(miniLabel, miniFive, miniFiveCd, miniWeekly, miniWeeklyCd);
            return;
        }

        var windows = OrderWindows(account.Usage.Windows);
        var now = DateTimeOffset.Now;
        ConsumptionRateResult rate = account.IsStale
            ? new ConsumptionRateResult(ConsumptionAlertLevel.Normal, 0, 0)
            : _codexConsumptionTracker.Observe(
                account.AccountId,
                windows.Min(window => window.RemainingPercent),
                DateTimeOffset.UtcNow);

        var five = windows[0];
        var weekly = windows.Length > 1 ? windows[1] : null;

        // 展开卡片对齐表格
        fivePercentText.Text = $"{five.RemainingPercent}%";
        fiveResetText.Text = CodexUsageFormatter.FormatResetCompact(five, now);
        ApplyConsumptionAlert(fivePercentText, rate.Level, account.IsStale);
        fivePercentText.ToolTip =
            $"近 5 分钟消耗 {rate.FiveMinuteConsumption}% · 近 1 分钟消耗 {rate.OneMinuteConsumption}%";
        if (weekly is not null)
        {
            weeklyPercentText.Text = $"{weekly.RemainingPercent}%";
            weeklyResetText.Text = CodexUsageFormatter.FormatResetCompact(weekly, now);
            ApplyConsumptionAlert(weeklyPercentText, rate.Level, account.IsStale);
        }
        else
        {
            weeklyPercentText.Text = "--";
            weeklyResetText.Text = "--";
            ApplyConsumptionAlert(weeklyPercentText, ConsumptionAlertLevel.Normal, isStale: false);
        }

        // 胶囊 GPT 区块单元格（四列：5h 额度 / 5h 倒计时 / 周额度 / 周倒计时）
        miniFive.Text = $"{five.RemainingPercent}%";
        miniFiveCd.Text = CodexUsageFormatter.FormatCountdownShort(five.ResetsAt, now);
        ApplyConsumptionAlert(miniFive, rate.Level, account.IsStale);
        if (weekly is not null)
        {
            miniWeekly.Text = $"{weekly.RemainingPercent}%";
            miniWeeklyCd.Text = CodexUsageFormatter.FormatCountdownShort(weekly.ResetsAt, now);
            ApplyConsumptionAlert(miniWeekly, rate.Level, account.IsStale);
        }
        else
        {
            miniWeekly.Text = "--";
            miniWeeklyCd.Text = "--";
            ApplyConsumptionAlert(miniWeekly, ConsumptionAlertLevel.Normal, isStale: false);
        }

        rowPanel.ToolTip = FormatAccountToolTip(account);
    }

    private static void ClearCodexCells(
        System.Windows.Controls.TextBlock fivePercent,
        System.Windows.Controls.TextBlock fiveReset,
        System.Windows.Controls.TextBlock weeklyPercent,
        System.Windows.Controls.TextBlock weeklyReset)
    {
        fivePercent.Text = "--";
        fiveReset.Text = "--";
        weeklyPercent.Text = "--";
        weeklyReset.Text = "--";
    }

    private static void ClearMiniGptRow(
        System.Windows.Controls.TextBlock label,
        System.Windows.Controls.TextBlock five,
        System.Windows.Controls.TextBlock fiveCd,
        System.Windows.Controls.TextBlock weekly,
        System.Windows.Controls.TextBlock weeklyCd)
    {
        label.Text = "--";
        five.Text = "--";
        fiveCd.Text = "--";
        weekly.Text = "--";
        weeklyCd.Text = "--";
    }

    /// <summary>
    /// 窗口排序：短的滚动窗口（5 小时）在前，长的周窗口在后；缺失时长时按重置时间兜底。
    /// </summary>
    private static CodexUsageWindow[] OrderWindows(IReadOnlyList<CodexUsageWindow> windows)
        => windows
            .OrderBy(window => window.DurationMinutes ?? int.MaxValue)
            .ThenBy(window => window.ResetsAt ?? DateTimeOffset.MaxValue)
            .ToArray();

    private static void ApplyConsumptionAlert(
        System.Windows.Controls.TextBlock text,
        ConsumptionAlertLevel level,
        bool isStale)
    {
        text.BeginAnimation(OpacityProperty, null);
        text.Opacity = 1;

        if (level == ConsumptionAlertLevel.Critical)
        {
            text.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x5F));
            text.BeginAnimation(OpacityProperty, new DoubleAnimation
            {
                From = 1,
                To = 0.35,
                Duration = TimeSpan.FromSeconds(1),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            });
        }
        else if (level == ConsumptionAlertLevel.Warning || isStale)
        {
            text.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x47));
        }
        else
        {
            text.Foreground = new SolidColorBrush(Color.FromRgb(0xBF, 0xDF, 0xFF));
        }
    }

    /// <summary>账号显示名：取邮箱 @ 前 4 个字母/数字，大写（如 MORT / WANG）。</summary>
    private static string ShortAccountName(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "----";
        string local = email.Split('@')[0];
        string letters = new string(local.Where(char.IsLetterOrDigit).Take(4).ToArray());
        return letters.Length > 0 ? letters.ToUpperInvariant() : "----";
    }

    private static string FormatAccountToolTip(CodexAccountUsageSnapshot account)
    {
        string status = account.Usage.IsAvailable && account.Usage.Windows.Count > 0
            ? string.Join(
                Environment.NewLine,
                OrderWindows(account.Usage.Windows)
                    .Select(window => CodexUsageFormatter.FormatWindowRow(window, DateTimeOffset.Now)))
            : account.RefreshError ?? account.Usage.Error ?? "暂不可用";
        string stale = account.IsStale ? Environment.NewLine + "数据已过期" : string.Empty;
        return account.Email + Environment.NewLine + status + stale;
    }

    private void RefreshPeakStatus()
    {
        _isPeak = PeakHourCalculator.IsPeak(DateTime.Now, _cfg.PeakHourRanges);
        UpdatePeakUi();
        // 计算下一边界，安排更精确的一次性定时器（60s 兜底 + 边界对齐）
        int mins = PeakHourCalculator.MinutesUntilNextBoundary(DateTime.Now, _cfg.PeakHourRanges);
        _peakTimer.Interval = TimeSpan.FromMinutes(Math.Max(1, mins));
    }

    private void UpdatePeakUi()
    {
        if (!_cfg.ShowPeakIndicator)
        {
            PeakText.Visibility = Visibility.Collapsed;
            MiniPeakDot.Visibility = Visibility.Collapsed;
            MiniPeakLabel.Visibility = Visibility.Collapsed;
            return;
        }
        PeakText.Visibility = Visibility.Visible;
        MiniPeakDot.Visibility = Visibility.Visible;
        MiniPeakLabel.Visibility = Visibility.Visible;

        // 高峰信息是次级参考状态：只用小圆点和文字，不与余额抢视觉焦点
        var peakBrush = new SolidColorBrush(Color.FromRgb(0xF2, 0x7D, 0x72));
        var normalBrush = new SolidColorBrush(Color.FromRgb(0x78, 0xD7, 0x9A));
        var labelBrush = new SolidColorBrush(Color.FromRgb(0xAE, 0xB8, 0xC4));
        PeakText.Background = System.Windows.Media.Brushes.Transparent;
        PeakDot.Foreground = _isPeak ? peakBrush : normalBrush;
        PeakLabel.Text = _isPeak ? "高峰" : "非高峰";
        PeakLabel.Foreground = labelBrush;
        MiniPeakDot.Foreground = _isPeak ? peakBrush : normalBrush;
        MiniPeakLabel.Text = _isPeak ? "高峰" : "非高峰";
        MiniPeakLabel.Foreground = labelBrush;
    }

    private void RaiseTrayStatus(ParsedBalance bal, decimal? change)
    {
        string chg = change.HasValue
            ? $"变动 {(change.Value >= 0 ? "+" : "")}{change.Value:0.00}"
            : "首次刷新";
        string peak = _cfg.ShowPeakIndicator ? (_isPeak ? "预计高峰" : "预计非高峰") : "";
        string status = $"余额 {Symbol(bal.Currency)}{bal.Total:0.00} | {chg} | {peak}".TrimEnd(' ', '|');
        TrayStatusChanged?.Invoke(status, _isPeak);
    }

    private void RaiseTrayError()
    {
        string last = _cfg.LastSuccessfulRefreshUtc.HasValue
            ? _cfg.LastSuccessfulRefreshUtc.Value.ToLocalTime().ToString("HH:mm")
            : "从未";
        string status = $"余额未知（最后成功 {last}）";
        TrayStatusChanged?.Invoke(status, _isPeak);
    }

    private void ApplyBalance(ParsedBalance bal, bool consistent, decimal? change, decimal? pct)
    {
        var normalDot = new SolidColorBrush(Color.FromRgb(0x4C, 0xC9, 0x4C));
        var dangerDot = new SolidColorBrush(Color.FromRgb(0xE8, 0x66, 0x56));
        StatusDot.Foreground = bal.IsAvailable ? normalDot : dangerDot;
        StatusDot.ToolTip = bal.IsAvailable ? "账户正常" : "账户不可用";

        if (!bal.IsAvailable)
        {
            var danger = new LinearGradientBrush(
                Color.FromArgb(0xF0, 0x3B, 0x1F, 0x22),
                Color.FromArgb(0xF0, 0x24, 0x12, 0x16), 45);
            Card.Background = danger;
            MiniCard.Background = danger;
        }

        string sym = Symbol(bal.Currency);
        string balance = sym + bal.Total.ToString("0.00");
        BalanceText.Text = balance;
        MiniBalanceText.Text = balance;

        // 完整卡片变动信息
        if (change is null)
        {
            ChangeText.Text = "首次";
            ChangeText.Foreground = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0));
            MiniChangeText.Text = "";
            MiniChangeText.Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0x99, 0xA6));
        }
        else
        {
            string sign = change.Value >= 0 ? "+" : "";
            string txt = sign + change.Value.ToString("0.00")
                         + (pct.HasValue ? "（" + pct.Value.ToString("0.0") + "%）" : "");
            ChangeText.Text = txt;
            ChangeText.Foreground = new SolidColorBrush(change.Value >= 0
                ? Color.FromRgb(0x4C, 0xC9, 0x4C) : Color.FromRgb(0xE8, 0x66, 0x56));
            // 胶囊变动信息：紧凑小字，只显示金额不含百分比
            MiniChangeText.Text = sign + change.Value.ToString("0.00");
            MiniChangeText.Foreground = new SolidColorBrush(change.Value >= 0
                ? Color.FromRgb(0x4C, 0xC9, 0x4C) : Color.FromRgb(0xE8, 0x66, 0x56));
        }

        RefreshTimeText.Text = "上次刷新 " + DateTime.Now.ToString("HH:mm:ss");
        MiniRefreshTimeText.Text = "刷新 " + DateTime.Now.ToString("HH:mm:ss");
    }

    private void ShowError(string msg)
    {
        StatusDot.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0x66, 0x56));
        ChangeText.Text = msg;
        ChangeText.Foreground = new SolidColorBrush(Colors.Orange);
    }

    private void ShowUnavailableCurrency(string currency)
    {
        StatusDot.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0x66, 0x56));
        ChangeText.Text = "未返回 " + currency + " 余额";
        ChangeText.Foreground = new SolidColorBrush(Colors.Orange);
    }

    private static string Symbol(string currency) => currency.ToUpperInvariant() switch
    {
        "CNY" => "¥",
        "USD" => "$",
        _ => currency + " "
    };
}
