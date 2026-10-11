using System.IO;
using System.Text;
using System.Windows;

namespace AiWebGateway.Views;

public partial class LogWindow : Window
{
    private const int MaxLines = 5000;
    private static readonly string LogDir =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
    private static readonly string LogFile =
        Path.Combine(LogDir, $"gateway-{DateTime.Now:yyyyMMdd}.log");

    private readonly List<string> _lines = new();

    public LogWindow()
    {
        InitializeComponent();
        LoadHistoryFromFile();
    }

    private void LoadHistoryFromFile()
    {
        try
        {
            if (!File.Exists(LogFile)) return;
            var lines = File.ReadAllLines(LogFile, Encoding.UTF8);
            var start = Math.Max(0, lines.Length - 1000);
            for (int i = start; i < lines.Length; i++)
                _lines.Add(lines[i]);

            LogText.Text = string.Join(Environment.NewLine, _lines);
            LogText.ScrollToEnd();
        }
        catch { }
    }

    public void AppendLine(string line)
    {
        Dispatcher.Invoke(() =>
        {
            var formatted = $"{DateTime.Now:HH:mm:ss} | {line}";
            _lines.Add(formatted);
            while (_lines.Count > MaxLines) _lines.RemoveAt(0);

            // TextBox直接追加
            LogText.AppendText(formatted + Environment.NewLine);
            LogText.ScrollToEnd();

            try
            {
                Directory.CreateDirectory(LogDir);
                File.AppendAllText(LogFile, formatted + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        });
    }

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(LogText.Text); } catch { }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        LogText.Clear();
        _lines.Clear();
        LogText.Text = $"{DateTime.Now:HH:mm:ss} | (显示已清空，历史文件仍在 logs 目录)\r\n";
    }
}