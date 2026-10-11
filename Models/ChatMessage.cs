using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace AiWebGateway.Models;

public class ChatMessage : INotifyPropertyChanged
{
    private string _content = "";
    private string _reasoning = "";

    public string Content
    {
        get => _content;
        set { _content = value; OnPropertyChanged(); }
    }

    public string ReasoningContent
    {
        get => _reasoning;
        set { _reasoning = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasReasoning)); }
    }

    [JsonIgnore]
    public bool HasReasoning => !string.IsNullOrEmpty(_reasoning);

    public string Role { get; set; } = "user";

    [JsonIgnore]
    public bool IsUser => Role == "user";

    [JsonIgnore]
    public bool IsAssistant => Role == "assistant";

    public DateTime Timestamp { get; set; } = DateTime.Now;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}