using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using AiWebGateway.Models;
using AiWebGateway.Services;

namespace AiWebGateway.Views;

public partial class SettingsWindow : Window
{
    private readonly ConfigService _configService = new();
    public ObservableCollection<Account> Accounts { get; } = new();

    public SettingsWindow()
    {
        InitializeComponent();
        AccountGrid.ItemsSource = Accounts;
        LoadConfig();
    }

    private void LoadConfig()
    {
        try
        {
            var cfg = _configService.Load();

            HostBox.Text = cfg?.Server?.Host ?? "127.0.0.1";
            PortBox.Text = (cfg?.Server?.Port ?? 8000).ToString();
            FailThresholdBox.Text = (cfg?.Router?.FailThreshold ?? 3).ToString();
            RecoveryIntervalBox.Text = (cfg?.Router?.RecoveryInterval ?? 10).ToString();

            Accounts.Clear();
            if (cfg?.DeepSeek?.Accounts != null)
            {
                foreach (var a in cfg.DeepSeek.Accounts)
                    Accounts.Add(a);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"加载配置失败：{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ── 添加账号──
    private void AddAccount_Click(object sender, RoutedEventArgs e)
    {
        var template = new Account { Id = $"acc-{Accounts.Count + 1}" };
        var editor = new AccountEditorWindow(template) { Owner = this };

        if (editor.ShowDialog() == true)
        {
            Accounts.Add(editor.Result);
        }
    }

    // ── 编辑账号──
    private void EditAccount_Click(object sender, RoutedEventArgs e)
    {
        if (AccountGrid.SelectedItem is not Account selected)
        {
            MessageBox.Show("请先选中一个账号", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var editor = new AccountEditorWindow(selected) { Owner = this };
        if (editor.ShowDialog() == true)
        {
            int idx = Accounts.IndexOf(selected);
            if (idx >= 0) Accounts[idx] = editor.Result;
        }
    }

    // ── 双击行=编辑 ──
    private void AccountGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        EditAccount_Click(sender, e);
    }

    // ── 删除账号 ──
    private void DeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        if (AccountGrid.SelectedItem is not Account selected)
        {
            MessageBox.Show("请先选中要删除的行", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"确定要删除账号 \"{selected.Id}\" 吗？",
            "确认删除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm == MessageBoxResult.Yes)
        {
            Accounts.Remove(selected);
        }
    }

    // ── 保存配置 ──
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var cfg = new AppConfig
            {
                Server = new ServerConfig
                {
                    Host = HostBox.Text.Trim(),
                    Port = int.TryParse(PortBox.Text, out var p) ? p : 8000,
                },
                Router = new RouterConfig
                {
                    FailThreshold = int.TryParse(FailThresholdBox.Text, out var f) ? f : 3,
                    RecoveryInterval = int.TryParse(RecoveryIntervalBox.Text, out var r) ? r : 10,
                },
                DeepSeek = new DeepSeekConfig
                {
                    Accounts = Accounts.ToList(),
                },
            };

            _configService.Save(cfg);
            MessageBox.Show("配置已保存", "成功",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败：{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}