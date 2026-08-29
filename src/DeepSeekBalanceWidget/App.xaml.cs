using System;
using System.Threading;
using System.Windows;
using DeepSeekBalanceWidget.Services;

namespace DeepSeekBalanceWidget;

public partial class App : System.Windows.Application
{
    private const string MutexName = "DeepSeekBalanceWidget_SingleInstance";
    private const string ActivateEventName = "DeepSeekBalanceWidget_Activate";

    /// <summary>应用正在退出；通知窗据此跳过重排，避免退出期访问已释放的可视对象。</summary>
    internal static bool IsShuttingDown { get; private set; }

    private Mutex? _mutex;
    private EventWaitHandle? _activateHandle;
    private readonly CancellationTokenSource _cts = new();
    private ConfigService? _configService;
    private MainWindow? _mainWindow;
    private TrayIconService? _tray;
    private bool _mutexOwned;


    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        IsShuttingDown = false;

        _mutex = new Mutex(false, MutexName, out _);
        try { _mutexOwned = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { _mutexOwned = true; }

        if (!_mutexOwned)
        {
            SignalActivate();
            Shutdown();
            return;
        }

        ListenForActivate();

        _configService = new ConfigService();
        var cfg = _configService.Load();

        bool forceMock = Array.Exists(e.Args, a => a == "--mock-scenario");
        string scenario = GetMockScenario(e.Args);
        IBalanceProvider provider = (cfg.UseMockData || forceMock)
            ? new MockBalanceService(scenario)
            : new DeepSeekApiClient(_configService.GetApiKey() ?? string.Empty);

        _mainWindow = new MainWindow(_configService, cfg, provider);
        MainWindow = _mainWindow;
        _mainWindow.RequestExit += OnRequestExit;
        _mainWindow.TrayStatusChanged += OnTrayStatusChanged;
        _mainWindow.Show();

        _tray = new TrayIconService(_mainWindow, _cts);
    }

    private void OnTrayStatusChanged(string status, bool? isPeak)
    {
        _tray?.UpdateStatus(status, isPeak);
    }

    private void OnRequestExit()
    {
        IsShuttingDown = true;
        AlarmSound.Stop();
        _cts.Cancel();
        _tray?.Dispose();
        _tray = null;
        Shutdown();
    }

    private void ListenForActivate()
    {
        try
        {
            _activateHandle = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
            var handle = _activateHandle;
            var thread = new Thread(() =>
            {
                while (handle.WaitOne())
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (_mainWindow is not null)
                            _mainWindow.RestoreAndActivate();
                    });
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }
        catch { }
    }

    private void SignalActivate()
    {
        try
        {
            using var h = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
            h.Set();
        }
        catch { }
    }

    private static string GetMockScenario(string[] args)
    {
        int i = Array.IndexOf(args, "--mock-scenario");
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : "sequence";
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _cts.Cancel();
        _tray?.Dispose();
        _activateHandle?.Dispose();
        if (_mutexOwned) { try { _mutex?.ReleaseMutex(); } catch { } }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
