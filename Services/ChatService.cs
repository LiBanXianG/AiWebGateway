using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace AiWebGateway.Services;

public record StreamChunk(string? Content, string? Reasoning, List<ToolCallInfo>? ToolCalls);
public record ToolCallInfo(string Id, string Name, string Arguments);

public class ChatService
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private string _conversationId;

    public string ConversationId => _conversationId;

    public ChatService(string baseUrl = "http://127.0.0.1:8000/v1")
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _conversationId = Guid.NewGuid().ToString("N");
    }

    public void StartNewSession()
    {
        _conversationId = Guid.NewGuid().ToString("N");
    }

    public async IAsyncEnumerable<StreamChunk> StreamChatAsync(
        IEnumerable<(string role, string content)> messages,
        string model = "deepseek-chat",
        bool thinkingEnabled = true,
        bool searchEnabled = false,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var payload = new Dictionary<string, object>
        {
            ["model"] = model,
            ["stream"] = true,
            ["thinking_enabled"] = thinkingEnabled,
            ["search_enabled"] = searchEnabled,
            ["conversation_id"] = _conversationId,
            ["messages"] = messages.Select(m => new { role = m.role, content = m.content }).ToArray()
        };

        var json = JsonSerializer.Serialize(payload);
        var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        req.Headers.Add("X-Conversation-Id", _conversationId);

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var toolCallsAccumulator = new Dictionary<int, (string Id, string Name, string Args)>();

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrEmpty(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var data = line.Substring(6).Trim();
            if (data == "[DONE]")
            {
                if (toolCallsAccumulator.Count > 0)
                {
                    var list = toolCallsAccumulator.OrderBy(kv => kv.Key)
                        .Select(kv => new ToolCallInfo(kv.Value.Id, kv.Value.Name, kv.Value.Args))
                        .ToList();
                    yield return new StreamChunk(null, null, list);
                }
                yield break;
            }

            string? content = null;
            string? reasoning = null;
            List<ToolCallInfo>? toolCalls = null;

            try
            {
                using var doc = JsonDocument.Parse(data);
                if (doc.RootElement.TryGetProperty("choices", out var choices)
                    && choices.GetArrayLength() > 0)
                {
                    var delta = choices[0].GetProperty("delta");
                    if (delta.TryGetProperty("content", out var c))
                        content = c.GetString();
                    if (delta.TryGetProperty("reasoning_content", out var r))
                        reasoning = r.GetString();

                    if (delta.TryGetProperty("tool_calls", out var tcs) && tcs.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var tc in tcs.EnumerateArray())
                        {
                            int idx = tc.TryGetProperty("index", out var idxEl) ? idxEl.GetInt32() : 0;
                            string id = tc.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                            string name = "";
                            string args = "";
                            if (tc.TryGetProperty("function", out var fn))
                            {
                                if (fn.TryGetProperty("name", out var nEl))
                                    name = nEl.GetString() ?? "";
                                if (fn.TryGetProperty("arguments", out var aEl))
                                    args = aEl.GetString() ?? "";
                            }

                            if (!toolCallsAccumulator.ContainsKey(idx))
                                toolCallsAccumulator[idx] = (id, name, args);
                            else
                            {
                                var cur = toolCallsAccumulator[idx];
                                toolCallsAccumulator[idx] = (
                                    string.IsNullOrEmpty(cur.Id) ? id : cur.Id,
                                    string.IsNullOrEmpty(cur.Name) ? name : cur.Name,
                                    cur.Args + args
                                );
                            }
                        }
                    }
                }
            }
            catch { }

            if (!string.IsNullOrEmpty(content) || !string.IsNullOrEmpty(reasoning))
                yield return new StreamChunk(content, reasoning, null);
        }
    }
}