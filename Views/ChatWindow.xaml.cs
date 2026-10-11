using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AiWebGateway.Models;
using AiWebGateway.Services;

namespace AiWebGateway.Views;

public partial class ChatWindow : Window
{
    public ObservableCollection<ChatMessage> Messages { get; } = new();
    public ObservableCollection<Conversation> Conversations { get; } = new();

    private readonly ConversationService _conversationService = new();
    private Conversation _current;
    private ChatService? _chat;
    private bool _isStreaming;
    private bool _suppressSelection;

    public ChatWindow()
    {
        InitializeComponent();
        DataContext = this;

        LoadAllConversations();

        // 默认打开最近一条或新建
        if (Conversations.Count > 0)
        {
            _current = Conversations[0];
            LoadConversation(_current);
            HistoryList.SelectedItem = _current;
        }
        else
        {
            CreateNewConversation();
        }
    }

    // ───────────── 历史管理 ─────────────

    private void LoadAllConversations()
    {
        Conversations.Clear();
        foreach (var c in _conversationService.LoadAll())
            Conversations.Add(c);
    }

    private void LoadConversation(Conversation conv)
    {
        _suppressSelection = true;
        try
        {
            Messages.Clear();
            foreach (var m in conv.Messages)
                Messages.Add(m);
            TitleText.Text = conv.Title;
            ScrollToBottom();
        }
        finally
        {
            _suppressSelection = false;
        }
    }

    private void CreateNewConversation()
    {
        _current = new Conversation
        {
            Title = "新对话",
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
        };
        _current.Messages.Add(new ChatMessage
        {
            Role = "assistant",
            Content = "你好！我是本地 AI 助手。\n\nEnter 发送 · Ctrl+Enter 换行"
        });
        _conversationService.Save(_current);

        _suppressSelection = true;
        Conversations.Insert(0, _current);
        Messages.Clear();
        foreach (var m in _current.Messages) Messages.Add(m);
        TitleText.Text = _current.Title;
        HistoryList.SelectedItem = _current;
        _suppressSelection = false;

        _chat = null;  // 重置 ChatService
    }

    private void PersistCurrent()
    {
        if (_current == null) return;
        _current.Messages = Messages.ToList();
        _current.UpdatedAt = DateTime.Now;
        _conversationService.Save(_current);

        // 按 UpdatedAt 倒序，把当前对话移到最上方
        var sorted = Conversations.OrderByDescending(c => c.UpdatedAt).ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            var item = sorted[i];
            var curIdx = Conversations.IndexOf(item);
            if (curIdx != i)
            {
                Conversations.Move(curIdx, i);
            }
        }

        // 保持选中
        _suppressSelection = true;
        HistoryList.SelectedItem = _current;
        _suppressSelection = false;
    }

    private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection) return;
        if (HistoryList.SelectedItem is Conversation conv && conv != _current)
        {
            PersistCurrent();
            _current = conv;
            LoadConversation(conv);
            _chat = null;
        }
    }

    private void DeleteHistory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not Conversation conv)
            return;

        var result = MessageBox.Show(
            $"确定删除对话 \"{conv.Title}\" 吗？",
            "确认删除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        _conversationService.Delete(conv.Id);
        Conversations.Remove(conv);

        if (conv == _current)
        {
            if (Conversations.Count > 0)
            {
                _current = Conversations[0];
                LoadConversation(_current);
                HistoryList.SelectedItem = _current;
            }
            else
            {
                CreateNewConversation();
            }
            _chat = null;
        }
    }

    private void NewChat_Click(object sender, RoutedEventArgs e)
    {
        PersistCurrent();
        CreateNewConversation();
    }

    // ───────────── 聊天 ─────────────

    private void EnsureService()
    {
        _chat ??= new ChatService("http://127.0.0.1:8000/v1");
    }

    private async void Send_Click(object sender, RoutedEventArgs e) => await SendAsync();

    private async void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            var caret = InputBox.CaretIndex;
            InputBox.Text = InputBox.Text.Insert(caret, Environment.NewLine);
            InputBox.CaretIndex = caret + Environment.NewLine.Length;
            e.Handled = true;
        }
        else
        {
            e.Handled = true;
            await SendAsync();
        }
    }

    private async Task SendAsync()
    {
        if (_isStreaming) return;
        var text = InputBox.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        EnsureService();
        InputBox.Clear();

        // 更新对话标题
        var isFirstUserMessage = !Messages.Any(m => m.IsUser);
        if (isFirstUserMessage)
        {
            _current.Title = text.Length > 20 ? text.Substring(0, 20) + "..." : text;
            TitleText.Text = _current.Title;
        }

        Messages.Add(new ChatMessage { Role = "user", Content = text });
        var reply = new ChatMessage { Role = "assistant", Content = "" };
        Messages.Add(reply);

        ScrollToBottom();
        _isStreaming = true;
        SendButton.IsEnabled = false;

        try
        {
            var history = Messages
                .Where(m => m != reply)
                .Select(m => (m.Role, m.Content))
                .ToList();

            var thinking = ThinkingToggle.IsChecked == true;
            var searching = SearchToggle.IsChecked == true;

            await foreach (var chunk in _chat!.StreamChatAsync(
                history, "deepseek-chat", thinking, searching))
            {
                if (!string.IsNullOrEmpty(chunk.Reasoning))
                    reply.ReasoningContent += chunk.Reasoning;
                if (!string.IsNullOrEmpty(chunk.Content))
                    reply.Content += chunk.Content;

                // 工具调用：显示成卡片而非原始DSML
                if (chunk.ToolCalls != null && chunk.ToolCalls.Count > 0)
                {
                    foreach (var tc in chunk.ToolCalls)
                    {
                        reply.Content += $"\n\n**调用工具** `{tc.Name}`\n```json\n{tc.Arguments}\n```";
                    }
                }

                ScrollToBottom();
            }

            if (string.IsNullOrEmpty(reply.Content))
                reply.Content = "[空回复]";
        }
        catch (HttpRequestException)
        {
            reply.Content = "[请求失败] 无法连接到本地服务，请确认托盘右键已启动服务。";
        }
        catch (Exception ex)
        {
            reply.Content = string.IsNullOrEmpty(reply.Content)
                ? $"[请求失败] {ex.Message}"
                : reply.Content + $"\n[中断] {ex.Message}";
        }
        finally
        {
            _isStreaming = false;
            SendButton.IsEnabled = true;

            // 保存到磁盘
            PersistCurrent();
        }
    }

    private void ScrollToBottom()
    {
        if (MessageList.Items.Count > 0)
            MessageList.ScrollIntoView(MessageList.Items[^1]);
    }
}