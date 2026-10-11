using System.Windows;
using AiWebGateway.Services;
using AiWebGateway.Views;

namespace AiWebGateway;

public partial class MainWindow : Window
{
    private readonly GatewayService _gateway = new();
    private readonly ConfigService _configService = new();
    private SettingsWindow? _settingsWindow;
    private LogWindow? _logWindow;
    private ChatWindow? _chatWindow;
    private ApiInfoWindow? _apiInfoWindow;

    private string _currentHost = "127.0.0.1";
    private int _currentPort = 8000;

    public MainWindow()
    {
        InitializeComponent();

        _gateway.LogReceived += line =>
            Dispatcher.Invoke(() => _logWindow?.AppendLine(line));

        _gateway.Started += (host, port) => Dispatcher.Invoke(() =>
        {
            _currentHost = host;
            _currentPort = port;
            ToggleMenuItem.Header = "停止服务";
            TrayIcon.ToolTipText = $"AI Web Gateway - 运行中 http://{host}:{port}";
        });

        _gateway.Stopped += () => Dispatcher.Invoke(() =>
        {
            ToggleMenuItem.Header = "启动服务";
            TrayIcon.ToolTipText = "AI Web Gateway - 已停止";
        });

        _gateway.ErrorOccurred += msg => Dispatcher.Invoke(() =>
            MessageBox.Show(msg, "错误",
                MessageBoxButton.OK, MessageBoxImage.Warning));

        Loaded += (_, _) => Hide();
    }

    // ───────── 服务控制 ─────────
    private bool _serviceToggling = false;

    private async void ToggleService_Click(object sender, RoutedEventArgs e)
    {
        if (_serviceToggling) return;
        _serviceToggling = true;
        ToggleMenuItem.IsEnabled = false;

        try
        {
            if (_gateway.IsRunning)
            {
                await _gateway.StopAsync();
            }
            else
            {
                var cfg = _configService.Load();
                _currentHost = cfg.Server.Host;
                _currentPort = cfg.Server.Port;
                _gateway.Start(cfg.Server.Host, cfg.Server.Port);
            }
        }
        finally
        {
            _serviceToggling = false;
            ToggleMenuItem.IsEnabled = true;
        }
    }

    // ───────── 聊天窗口 ─────────
    private void OpenChat_Click(object sender, RoutedEventArgs e) => ShowChat();

    private void ShowChat()
    {
        if (_chatWindow == null || !_chatWindow.IsLoaded)
        {
            _chatWindow = new ChatWindow();
            _chatWindow.Closed += (_, _) => _chatWindow = null;
        }
        _chatWindow.Show();
        _chatWindow.Activate();
    }

    // ───────── API 信息窗口 ─────────
    private void OpenApiInfo_Click(object sender, RoutedEventArgs e) => ShowApiInfo();

    private void ShowApiInfo()
    {
        var baseUrl = $"http://{_currentHost}:{_currentPort}/v1";
        const string apiKey = "sk-local";
        const string model = "deepseek-chat";

        if (_apiInfoWindow == null || !_apiInfoWindow.IsLoaded)
        {
            _apiInfoWindow = new ApiInfoWindow(baseUrl, apiKey, model);
            _apiInfoWindow.Closed += (_, _) => _apiInfoWindow = null;
        }
        _apiInfoWindow.Show();
        _apiInfoWindow.Activate();
    }

    // ───────── 配置窗口 ─────────
    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow == null || !_settingsWindow.IsLoaded)
        {
            _settingsWindow = new SettingsWindow();
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    // ───────── 日志窗口 ─────────
    private void OpenLogs_Click(object sender, RoutedEventArgs e) => ShowLog();

    private void ShowLog()
    {
        if (_logWindow == null || !_logWindow.IsLoaded)
        {
            _logWindow = new LogWindow();
            _logWindow.Closed += (_, _) => _logWindow = null;
        }
        _logWindow.Show();
        _logWindow.Activate();
    }

    // ───────── 托盘双击 → 打开聊天窗口 ─────────
    private void TrayIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
    {
        ShowChat();
    }

    // ───────── 退出 ─────────
    private async void ExitApp_Click(object sender, RoutedEventArgs e)
    {
        try { await _gateway.StopAsync(); } catch { }

        try
        {
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("python"))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
            }
        }
        catch { }

        try { TrayIcon.Dispose(); } catch { }
        Environment.Exit(0);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        WindowState = WindowState.Minimized;
    }
}