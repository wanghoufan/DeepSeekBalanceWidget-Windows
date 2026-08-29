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
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _codexTimer;
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
        Loaded += (_, _) => EvaluateEdgeAutoHide();

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

        _peakTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _peakTimer.Tick += (_, _) => RefreshPeakStatus();
        _peakTimer.Start();

        RefreshPeakStatus();
        _ = RefreshAsync();
        if (_cfg.EnableCodexMonitoring) _ = RefreshCodexUsageAsync();
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
        Left = Math.Clamp(Left, wa.Left, Math.Max(wa.Left, wa.Right - Width));
        Top = Math.Clamp(Top, wa.Top, Math.Max(wa.Top, wa.Bottom - Height));
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
        Width = mini ? GetMiniModeWidth() : 420;
        if (IsLoaded)
        {
            if (_dockEdge != DockEdge.None)
                SetDockPosition(_isEdgeHidden);
            else
                ClampToWorkArea(); // 尺寸变化后只做边界 Clamp，不强制回角落
        }
        RearrangeMiniBlocks();
    }

    private double GetMiniModeWidth()
    {
        double width = 16; // 左右内边距
        foreach (string kind in _cfg.AgentOrder)
        {
            switch (kind)
            {
                case "deepseek":
                    width += 82; // DS 标签 + 余额 + 间距
                    break;
                case "chatgpt":
                    if (_cfg.EnableCodexMonitoring)
                        width += 292; // GPT 表格容器（22+50+66+50+74 + 内边距）
                    break;
                case "workbuddy":
                    width += 72; // WB 占位
                    break;
            }
            width += 10; // 区块间距
        }
        width += 96; // 贴边/最小化/关闭三按钮 + 间距
        return Math.Clamp(width, 120, 800);
    }

    /// <summary>按 _cfg.AgentOrder 重排胶囊区块（贴边按钮固定最右），DS/WB 单行区块垂直居中。</summary>
    private void RearrangeMiniBlocks()
    {
        var order = _cfg.AgentOrder ?? new List<string>();
        var blocks = new Dictionary<string, UIElement>
        {
            ["deepseek"] = MiniDeepSeekBlock,
            ["chatgpt"] = MiniGptBlock,
            ["workbuddy"] = MiniWorkbuddyBlock
        };

        int index = 0;
        foreach (string kind in order)
        {
            if (!blocks.TryGetValue(kind, out var block)) continue;
            if (index >= MiniRowPanel.Children.Count
                || !ReferenceEquals(MiniRowPanel.Children[index], block))
            {
                MiniRowPanel.Children.Remove(block);
                MiniRowPanel.Children.Insert(Math.Min(index, MiniRowPanel.Children.Count), block);
            }
            index++;
        }

        // 贴边 / 最小化 / 关闭三按钮固定最右（顺序固定）
        foreach (var btn in new[] { MiniEdgeAutoHideBtn, MiniMinBtn, MiniCloseBtn })
        {
            MiniRowPanel.Children.Remove(btn);
            MiniRowPanel.Children.Add(btn);
        }
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

    public void RefreshNow() => _ = RefreshNowAsync();

    private async Task RefreshNowAsync()
    {
        _isAuthPaused = false;
        _timer.Start();
        var codexRefresh = _cfg.EnableCodexMonitoring
            ? RefreshCodexUsageAsync()
            : Task.CompletedTask;
        await Task.WhenAll(RefreshAsync(), codexRefresh);
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
            // 让余额类告警与 GPT 额度预警都遵守「允许弹窗通知」开关。
            if (decision.ShowLowBalance && _cfg.ShowToastNotifications)
                ToastService.Show(this, "低余额提醒", $"余额 {bal.Total:0.00} {bal.Currency} 低于阈值 {_cfg.LowBalanceThreshold:0.00}");
            if (decision.ShowAbnormalDrop && _cfg.ShowToastNotifications)
                ToastService.Show(this, "余额异常下降", $"单次下降 {Math.Abs(pct ?? 0):0.0}%");
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
            MiniDsLabel, MiniBalanceText, MiniPeakLabel,
            MiniGptA1Label, MiniGptA1Five, MiniGptA1FiveCd, MiniGptA1Weekly, MiniGptA1WeeklyCd,
            MiniGptA2Label, MiniGptA2Five, MiniGptA2FiveCd, MiniGptA2Weekly, MiniGptA2WeeklyCd,
            MiniWorkbuddyBlock
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
    /// 评估 ChatGPT 额度预警并弹窗：剩余额度降到阈值档位时预警，额度恢复时通知。
    /// 文案会区分 5 小时额度 / 周额度，并写明具体账号。
    /// </summary>
    private void RaiseCodexQuotaAlerts(IReadOnlyList<CodexAccountUsageSnapshot> accounts)
    {
        if (!_cfg.ShowToastNotifications) return;

        foreach (var alert in _codexQuotaAlerts.Evaluate(accounts, _cfg, DateTimeOffset.Now))
        {
            string who = ShortAccountName(alert.Email);
            string what = alert.WindowLabel;

            if (alert.IsRecovery)
            {
                ToastService.Show(this,
                    $"{who} · {what}已恢复",
                    $"剩余额度已回到 {alert.RemainingPercent}%");
                continue;
            }

            string resetHint = alert.ResetsAt is DateTimeOffset resetsAt
                ? $"预计 {resetsAt.ToLocalTime():MM-dd HH:mm} 恢复"
                : "恢复时间未知";
            ToastService.Show(this,
                $"{who} · {what}仅剩 {alert.RemainingPercent}%",
                $"{resetHint}，建议提前做好上下文交接");
        }
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

        if (change is null)
        {
            ChangeText.Text = "首次";
            ChangeText.Foreground = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0));
        }
        else
        {
            string sign = change.Value >= 0 ? "+" : "";
            string txt = sign + change.Value.ToString("0.00")
                         + (pct.HasValue ? "（" + pct.Value.ToString("0.0") + "%）" : "");
            ChangeText.Text = txt;
            ChangeText.Foreground = new SolidColorBrush(change.Value >= 0
                ? Color.FromRgb(0x4C, 0xC9, 0x4C) : Color.FromRgb(0xE8, 0x66, 0x56));
        }

        RefreshTimeText.Text = "上次刷新 " + DateTime.Now.ToString("HH:mm:ss");
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
