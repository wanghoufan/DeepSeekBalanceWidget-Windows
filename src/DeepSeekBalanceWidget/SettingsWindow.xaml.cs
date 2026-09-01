using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using DeepSeekBalanceWidget.Models;
using DeepSeekBalanceWidget.Services;

namespace DeepSeekBalanceWidget;

public partial class SettingsWindow : Window
{
    private readonly ConfigService _configService;
    private readonly AppConfig _cfg;
    private readonly List<string> _agentOrder = new();

    public SettingsWindow(ConfigService configService, AppConfig cfg)
    {
        InitializeComponent();
        _configService = configService;
        _cfg = cfg;

        _agentOrder.AddRange(cfg.AgentOrder is { Count: > 0 }
            ? cfg.AgentOrder
            : new List<string> { "deepseek", "chatgpt", "opencode" });
        RefreshAgentOrderList();

        ApiKeyBox.Password = configService.GetApiKey() ?? "";
        OpenCodeKeyBox.Password = configService.GetOpenCodeApiKey() ?? "";
        IntervalBox.Text = cfg.RefreshIntervalSeconds.ToString();
        ThresholdBox.Text = cfg.LowBalanceThreshold.ToString("0.##");
        ChangePercentBox.Text = cfg.AbnormalChangePercent.ToString("0.##");
        CurrencyBox.SelectedIndex = cfg.SelectedCurrency.Equals("USD", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        TopmostCheck.IsChecked = cfg.IsAlwaysOnTop;
        EdgeAutoHideCheck.IsChecked = cfg.EnableEdgeAutoHide;
        MiniModeCheck.IsChecked = cfg.UseMiniMode;
        EnableDsCheck.IsChecked = cfg.EnableDeepSeekMonitoring;
        EnableCodexCheck.IsChecked = cfg.EnableCodexMonitoring;
        EnableWbCheck.IsChecked = cfg.EnableWorkbuddyMonitoring;
        EnableOcCheck.IsChecked = cfg.EnableOpenCodeMonitoring;
        EnableQuotaAlertCheck.IsChecked = cfg.EnableCodexQuotaAlerts;
        QuotaThresholdsBox.Text = cfg.CodexQuotaAlertThresholds is { Count: > 0 } thresholds
            ? string.Join(", ", thresholds)
            : "20, 10";
        QuotaRecoveredBox.Text = Math.Clamp(cfg.CodexQuotaRecoveredPercent, 1, 100).ToString();
        WeeklyAlertCheck.IsChecked = cfg.CodexWeeklyAlertEnabled;
        CodexFontSizeBox.Text = Math.Clamp(cfg.CodexFontSize, 10, 24).ToString("0.#");
        CodexFontStyleBox.SelectedIndex = cfg.CodexFontStyle switch
        {
            "Regular" => 1,
            "Bold" => 2,
            _ => 0
        };
        MockCheck.IsChecked = cfg.UseMockData;
        AutoStartCheck.IsChecked = AutoStartService.IsEnabled();

        // 预警行为
        AlertSoundCheck.IsChecked = cfg.AlertSoundEnabled;
        AlertSoundStyleBox.SelectedIndex = cfg.AlertSoundStyle switch
        {
            "Soft" => 0,
            "Urgent" => 2,
            _ => 1
        };
        RecoverySoundStyleBox.SelectedIndex = Math.Max(0,
            Array.IndexOf(RecoverySound.Styles, cfg.RecoveryAlertSoundStyle));
        (cfg.AlertMode.Equals("Limited", StringComparison.OrdinalIgnoreCase)
            ? AlertLimitedRadio
            : AlertContinuousRadio).IsChecked = true;
        AlertDurationBox.SelectedIndex = cfg.AlertMinDurationSeconds switch
        {
            30 => 1,
            60 => 2,
            _ => 0
        };
        AlertPositionBox.SelectedIndex = cfg.AlertPosition switch
        {
            "RightCenter" => 1,
            "BottomRight" => 2,
            _ => 0
        };

        // 高峰区间（配置不足两段时回退官方默认）
        var ranges = cfg.PeakHourRanges.Count >= 2
            ? cfg.PeakHourRanges
            : new List<PeakRange> { new(9, 12), new(14, 18) };
        Peak1StartBox.Text = ranges[0].StartHour.ToString();
        Peak1EndBox.Text = ranges[0].EndHour.ToString();
        Peak2StartBox.Text = ranges[1].StartHour.ToString();
        Peak2EndBox.Text = ranges[1].EndHour.ToString();
        ShowPeakCheck.IsChecked = cfg.ShowPeakIndicator;
        CornerBox.SelectedIndex = cfg.DefaultCorner switch
        {
            "BottomRight" => 1,
            "BottomLeft" => 2,
            _ => 0
        };
    }

    private void ClearKey_Click(object sender, RoutedEventArgs e)
    {
        ApiKeyBox.Password = "";
        _configService.SetApiKey(_cfg, null);
    }

    /// <summary>左侧导航切换：按选中的导航项显隐对应内容面板。</summary>
    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        PanelMonitoring.Visibility = NavMonitoring.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelAlerts.Visibility = NavAlerts.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelAppearance.Visibility = NavAppearance.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelGeneral.Visibility = NavGeneral.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string AgentDisplayName(string kind) => kind switch
    {
        "deepseek" => "DeepSeek 余额",
        "chatgpt" => "ChatGPT 额度",
        "opencode" => "OpenCode 额度",
        "workbuddy" => "WorkBuddy",
        "te" => "tE 积分",
        _ => kind
    };

    private void RefreshAgentOrderList()
    {
        AgentOrderBox.ItemsSource = _agentOrder.Select(AgentDisplayName).ToList();
    }

    private void AgentOrderUp_Click(object sender, RoutedEventArgs e)
    {
        int index = AgentOrderBox.SelectedIndex;
        if (index <= 0) return;
        (_agentOrder[index - 1], _agentOrder[index]) = (_agentOrder[index], _agentOrder[index - 1]);
        RefreshAgentOrderList();
        AgentOrderBox.SelectedIndex = index - 1;
    }

    private void AgentOrderDown_Click(object sender, RoutedEventArgs e)
    {
        int index = AgentOrderBox.SelectedIndex;
        if (index < 0 || index >= _agentOrder.Count - 1) return;
        (_agentOrder[index + 1], _agentOrder[index]) = (_agentOrder[index], _agentOrder[index + 1]);
        RefreshAgentOrderList();
        AgentOrderBox.SelectedIndex = index + 1;
    }

    private async void TestDs_Click(object sender, RoutedEventArgs e)
    {
        string key = ApiKeyBox.Password is { Length: > 0 } typed
            ? typed
            : _configService.GetApiKey() ?? "";
        if (string.IsNullOrWhiteSpace(key))
        {
            SetTestResult(DsTestResult, false, "✗ 请先填写 DeepSeek API Key");
            return;
        }
        await RunTestAsync(TestDsBtn, DsTestResult, async () =>
        {
            var client = new DeepSeekApiClient(key);
            var json = await client.GetBalanceJsonAsync(CancellationToken.None);
            var parsed = BalanceParser.Parse(json);
            if (!parsed.Success) return (false, "✗ " + (parsed.Error ?? "解析失败"));
            var selection = CurrencySelector.Select(parsed.Balances, _cfg.SelectedCurrency);
            if (!selection.Found) return (false, $"✗ 未返回 {_cfg.SelectedCurrency} 余额");
            return (true, $"✓ 连接成功，余额 {selection.Balance!.Total:0.00} {selection.Balance!.Currency}");
        });
    }

    private async void TestCodex_Click(object sender, RoutedEventArgs e)
    {
        await RunTestAsync(TestCodexBtn, CodexTestResult, async () =>
        {
            var provider = new CcSwitchCodexUsageProvider();
            var usages = await provider.GetUsagesAsync(CancellationToken.None);
            var available = usages.Where(u => u.Usage is { IsAvailable: true }).ToList();
            if (available.Count == 0)
            {
                string reason = usages.FirstOrDefault()?.Usage?.Error
                    ?? usages.FirstOrDefault()?.RefreshError
                    ?? "未检测到可用账号";
                return (false, "✗ " + reason);
            }
            string summary = string.Join("；", available.Select(u =>
                $"{ShortName(u.Email)}：5h {DescribeWindow(u, "5h")}" +
                (DescribeWindow(u, "weekly") is { Length: > 0 } weekly ? $" · 周 {weekly}" : "")));
            return (true, $"✓ {available.Count} 个账号 · {summary}");
        });
    }

    private async void TestOc_Click(object sender, RoutedEventArgs e)
    {
        string key = OpenCodeKeyBox.Password is { Length: > 0 } typed ? typed : null;
        await RunTestAsync(TestOcBtn, OcTestResult, async () =>
        {
            using var provider = new OpenCodeUsageProvider(key);
            var snapshot = await provider.GetUsageAsync(CancellationToken.None);
            if (!snapshot.IsAvailable)
            {
                return (false, "✗ " + (snapshot.Error ?? "暂不可用")
                    + (key is null && snapshot.Error is not null ? "（当前使用 auth.json 兜底）" : ""));
            }
            string summary = string.Join(" · ", snapshot.Windows.Select(w =>
                $"{OpenCodeUsageFormatter.ShortLabelOf(w.Kind)} {w.RemainingPercent}%"));
            return (true, $"✓ 连接成功 · {summary}");
        });
    }

    private void TestAlarm_Click(object sender, RoutedEventArgs e)
    {
        if (AlertSoundCheck.IsChecked != true)
        {
            SetTestResult(OcTestResult, false, "警报声已关闭，请先勾选「播放警报声」");
            return;
        }
        TestAlarmBtn.IsEnabled = false;
        string style = (AlertSoundStyleBox.SelectedItem as System.Windows.Controls.ComboBoxItem)
            ?.Tag?.ToString() ?? "Standard";
        AlarmSound.Play(style);
        var stop = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        stop.Tick += (_, _) =>
        {
            stop.Stop();
            AlarmSound.Stop();
            TestAlarmBtn.IsEnabled = true;
        };
        stop.Start();
    }

    private void TestRecoverySound_Click(object sender, RoutedEventArgs e)
    {
        if (AlertSoundCheck.IsChecked != true)
        {
            SetTestResult(OcTestResult, false, "提示音已关闭，请先勾选「播放警报声」");
            return;
        }
        TestRecoverySoundBtn.IsEnabled = false;
        string style = (RecoverySoundStyleBox.SelectedItem as System.Windows.Controls.ComboBoxItem)
            ?.Tag?.ToString() ?? RecoverySound.DefaultStyle;
        RecoverySound.Play(style);
        var stop = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        stop.Tick += (_, _) =>
        {
            stop.Stop();
            RecoverySound.Stop();
            TestRecoverySoundBtn.IsEnabled = true;
        };
        stop.Start();
    }

    /// <summary>后台线程执行测试，按钮置灰防连点，结果写回对应状态行。</summary>
    private async Task RunTestAsync(
        System.Windows.Controls.Button button,
        System.Windows.Controls.TextBlock result,
        Func<Task<(bool ok, string message)>> test)
    {
        button.IsEnabled = false;
        SetTestResult(result, null, "测试中…");
        try
        {
            var (ok, message) = await Task.Run(test);
            SetTestResult(result, ok, message);
        }
        catch (Exception ex)
        {
            SetTestResult(result, false, "✗ " + ex.Message);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void SetTestResult(System.Windows.Controls.TextBlock target, bool? ok, string message)
    {
        target.Text = message;
        target.Foreground = ok switch
        {
            true => new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x2E, 0x8B, 0x3C)),
            false => new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xC0, 0x39, 0x2B)),
            null => new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88))
        };
    }

    private static string ShortName(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "----";
        string local = email.Split('@')[0];
        string letters = new(local.Where(char.IsLetterOrDigit).Take(4).ToArray());
        return letters.Length > 0 ? letters.ToUpperInvariant() : "----";
    }

    /// <summary>取账号指定窗口的「剩余%」摘要（找不到该窗口返回空串）。</summary>
    private static string? DescribeWindow(CodexAccountUsageSnapshot account, string kind)
    {
        // CC Switch 账号窗口无类型标记，按窗口时长近似分类（最短≈5h，次短≈周）
        var ordered = account.Usage.Windows
            .OrderBy(w => w.DurationMinutes ?? int.MaxValue).ToList();
        var window = kind == "5h" ? ordered.FirstOrDefault() : ordered.Skip(1).FirstOrDefault();
        return window is null ? null : $"{window.RemainingPercent}%";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(IntervalBox.Text, out int interval) || interval < 5 || interval > 3600)
        { System.Windows.MessageBox.Show("刷新间隔需在 5-3600 之间"); return; }
        if (!decimal.TryParse(ThresholdBox.Text, out decimal threshold) || threshold < 0)
        { System.Windows.MessageBox.Show("阈值不能为负"); return; }
        if (!decimal.TryParse(ChangePercentBox.Text, out decimal pct) || pct < 0 || pct > 100)
        { System.Windows.MessageBox.Show("异常百分比需在 0-100 之间"); return; }
        if (!double.TryParse(CodexFontSizeBox.Text, out double codexFontSize) ||
            codexFontSize < 10 || codexFontSize > 24)
        { System.Windows.MessageBox.Show("额度文字大小需在 10-24 之间"); return; }
        if (!TryParseThresholds(QuotaThresholdsBox.Text, out var quotaThresholds))
        { System.Windows.MessageBox.Show("预警档位需为 1-99 的整数，多个用逗号分隔（如 20, 10）"); return; }
        if (!int.TryParse(QuotaRecoveredBox.Text, out int recoveredPct) || recoveredPct < 1 || recoveredPct > 100)
        { System.Windows.MessageBox.Show("恢复判定阈值需在 1-100 之间"); return; }

        // 高峰区间校验：整数小时，Start 0-23、End 1-24（半开区间支持 24），Start < End
        if (!TryParseHour(Peak1StartBox.Text, out int p1s) || p1s < 0 || p1s > 23 ||
            !TryParseHour(Peak1EndBox.Text, out int p1e) || p1e < 1 || p1e > 24 || p1s >= p1e ||
            !TryParseHour(Peak2StartBox.Text, out int p2s) || p2s < 0 || p2s > 23 ||
            !TryParseHour(Peak2EndBox.Text, out int p2e) || p2e < 1 || p2e > 24 || p2s >= p2e)
        {
            System.Windows.MessageBox.Show("高峰时段需为整数小时，且每段开始须小于结束（如 9-12、14-18）");
            return;
        }

        // 仅当输入框有内容时才更新 Key；留空保留原 Key（显式清除请用「清除 Key」按钮），
        // 避免"空框保存"静默抹掉已存 Key 导致每次都要重新输入。
        if (!string.IsNullOrEmpty(ApiKeyBox.Password))
            _configService.SetApiKey(_cfg, ApiKeyBox.Password);
        if (!string.IsNullOrEmpty(OpenCodeKeyBox.Password))
            _configService.SetOpenCodeApiKey(_cfg, OpenCodeKeyBox.Password);

        _cfg.RefreshIntervalSeconds = interval;
        _cfg.LowBalanceThreshold = threshold;
        _cfg.AbnormalChangePercent = pct;
        _cfg.SelectedCurrency = (CurrencyBox.SelectedIndex == 1) ? "USD" : "CNY";
        _cfg.IsAlwaysOnTop = TopmostCheck.IsChecked == true;
        _cfg.EnableEdgeAutoHide = EdgeAutoHideCheck.IsChecked == true;
        _cfg.UseMiniMode = MiniModeCheck.IsChecked == true;
        _cfg.EnableDeepSeekMonitoring = EnableDsCheck.IsChecked == true;
        _cfg.EnableCodexMonitoring = EnableCodexCheck.IsChecked == true;
        _cfg.EnableWorkbuddyMonitoring = EnableWbCheck.IsChecked == true;
        _cfg.EnableOpenCodeMonitoring = EnableOcCheck.IsChecked == true;
        _cfg.EnableCodexQuotaAlerts = EnableQuotaAlertCheck.IsChecked == true;
        _cfg.CodexQuotaAlertThresholds = quotaThresholds;
        _cfg.CodexQuotaRecoveredPercent = recoveredPct;
        _cfg.CodexWeeklyAlertEnabled = WeeklyAlertCheck.IsChecked == true;
        _cfg.CodexFontSize = codexFontSize;
        _cfg.CodexFontStyle = (CodexFontStyleBox.SelectedItem as System.Windows.Controls.ComboBoxItem)
            ?.Tag?.ToString() ?? "DeepSeek";
        _cfg.UseMockData = MockCheck.IsChecked == true;
        AutoStartService.Set(AutoStartCheck.IsChecked == true);

        // 预警行为
        _cfg.AlertSoundEnabled = AlertSoundCheck.IsChecked == true;
        _cfg.AlertSoundStyle = (AlertSoundStyleBox.SelectedItem as System.Windows.Controls.ComboBoxItem)
                ?.Tag?.ToString() ?? "Standard";
        _cfg.RecoveryAlertSoundStyle = (RecoverySoundStyleBox.SelectedItem as System.Windows.Controls.ComboBoxItem)
                ?.Tag?.ToString() ?? RecoverySound.DefaultStyle;
        _cfg.AlertMode = AlertLimitedRadio.IsChecked == true ? "Limited" : "Continuous";
        _cfg.AlertMinDurationSeconds = (AlertDurationBox.SelectedItem as System.Windows.Controls.ComboBoxItem)
                ?.Tag?.ToString() switch
        {
            "30" => 30,
            "60" => 60,
            _ => 10
        };
        _cfg.AlertPosition = (AlertPositionBox.SelectedItem as System.Windows.Controls.ComboBoxItem)
                ?.Tag?.ToString() ?? "TopRight";

        _cfg.PeakHourRanges = new List<PeakRange> { new(p1s, p1e), new(p2s, p2e) };
        _cfg.ShowPeakIndicator = ShowPeakCheck.IsChecked == true;
        _cfg.DefaultCorner = (CornerBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "Remember";
        _cfg.AgentOrder = new List<string>(_agentOrder);

        _configService.Save(_cfg);
        DialogResult = true;
        Close();
    }

    private static bool TryParseHour(string s, out int h) => int.TryParse(s, out h);

    /// <summary>
    /// 解析预警档位，如 "20, 10" → [20, 10]。
    /// 任一档位不是 1-99 的整数则判定失败；全部留空时回退默认档位 20 / 10。
    /// </summary>
    private static bool TryParseThresholds(string? text, out List<int> thresholds)
    {
        thresholds = new List<int>();
        var parts = (text ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string part in parts)
        {
            if (!int.TryParse(part, out int value) || value < 1 || value > 99) return false;
            if (!thresholds.Contains(value)) thresholds.Add(value);
        }
        if (thresholds.Count == 0) thresholds = new List<int> { 20, 10 };
        return true;
    }
}
