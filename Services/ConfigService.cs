using System;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using AiWebGateway.Models;

namespace AiWebGateway.Services;

public class ConfigService
{
    private readonly string _configPath;
    private readonly ISerializer _serializer;
    private readonly IDeserializer _deserializer;

    public ConfigService()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        _configPath = Path.Combine(baseDir, "envs", "python_embed", "Python", "config.yaml");

        _serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();
    }

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(_configPath)) return new AppConfig();
            var yaml = File.ReadAllText(_configPath);
            var config = _deserializer.Deserialize<AppConfig>(yaml) ?? new AppConfig();

            config.Server ??= new ServerConfig();
            config.Router ??= new RouterConfig();
            config.DeepSeek ??= new DeepSeekConfig();
            config.DeepSeek.Accounts ??= new System.Collections.Generic.List<Account>();

            return config;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"配置加载失败: {ex}");
            return new AppConfig();
        }
    }

    public void Save(AppConfig config)
    {
        var yaml = _serializer.Serialize(config);
        var dir = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_configPath, yaml);
    }
}