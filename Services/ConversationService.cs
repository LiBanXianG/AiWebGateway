using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AiWebGateway.Models;

namespace AiWebGateway.Services;

public class ConversationService
{
    private readonly string _dir;
    private readonly JsonSerializerOptions _jsonOptions;

    public ConversationService()
    {
        _dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "conversations");
        Directory.CreateDirectory(_dir);
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
    }

    public List<Conversation> LoadAll()
    {
        var list = new List<Conversation>();
        try
        {
            foreach (var file in Directory.GetFiles(_dir, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var c = JsonSerializer.Deserialize<Conversation>(json, _jsonOptions);
                    if (c != null) list.Add(c);
                }
                catch { }
            }
        }
        catch { }
        // 按更新时间倒序
        return list.OrderByDescending(c => c.UpdatedAt).ToList();
    }

    public void Save(Conversation conv)
    {
        try
        {
            conv.UpdatedAt = DateTime.Now;
            var path = Path.Combine(_dir, $"{conv.Id}.json");
            var json = JsonSerializer.Serialize(conv, _jsonOptions);
            File.WriteAllText(path, json);
        }
        catch { }
    }

    public void Delete(string id)
    {
        try
        {
            var path = Path.Combine(_dir, $"{id}.json");
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }
}