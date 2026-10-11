using YamlDotNet.Serialization;

namespace AiWebGateway.Models;

public class AppConfig
{
    [YamlMember(Alias = "server")] public ServerConfig Server { get; set; } = new();
    [YamlMember(Alias = "router")] public RouterConfig Router { get; set; } = new();
    [YamlMember(Alias = "deepseek")] public DeepSeekConfig DeepSeek { get; set; } = new();
}

public class ServerConfig
{
    [YamlMember(Alias = "host")] public string Host { get; set; } = "127.0.0.1";
    [YamlMember(Alias = "port")] public int Port { get; set; } = 8000;
}

public class RouterConfig
{
    [YamlMember(Alias = "fail_threshold")] public int FailThreshold { get; set; } = 3;
    [YamlMember(Alias = "recovery_interval")] public int RecoveryInterval { get; set; } = 10;
}

public class DeepSeekConfig
{
    [YamlMember(Alias = "accounts")] public List<Account> Accounts { get; set; } = new();
}

public class Account
{
    [YamlMember(Alias = "id")] public string Id { get; set; } = "";
    [YamlMember(Alias = "token")] public string Token { get; set; } = "";
    [YamlMember(Alias = "cookie")] public string Cookie { get; set; } = "";
}