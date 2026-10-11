using System.Windows;

namespace AiWebGateway.Views;

public partial class ApiInfoWindow : Window
{
    public ApiInfoWindow(string baseUrl, string apiKey, string model)
    {
        InitializeComponent();
        UrlBox.Text = baseUrl;
        KeyBox.Text = apiKey;
        ModelBox.Text = model;

        ExampleBox.Text =
$@"=== Cherry Studio（推荐） ===
设置 → 模型服务 → 自定义模型 API → 添加
  Provider ID     : aiwebgateway
  显示名称        : AI Web Gateway
  API 地址        : {baseUrl}
  API 协议        : OpenAI Chat Completions
  API 密钥        : {apiKey}
  模型目录        : 添加 {model}
                      添加 deepseek-reasoner

=== ChatBox ===
设置 → 模型 → 添加自定义提供方
  API 类型        : OpenAI API
  API 域名        : {baseUrl}
  API 密钥        : {apiKey}
  模型名          : {model}


=== NextChat / LobeChat ===
  自定义接口地址   : {baseUrl}
  API Key         : {apiKey}
  模型            : {model}


=== Python ===
from openai import OpenAI
client = OpenAI(
    base_url=""{baseUrl}"",
    api_key=""{apiKey}""
)
resp = client.chat.completions.create(
    model=""{model}"",
    messages=[{{""role"": ""user"", ""content"": ""你好""}}],
    stream=True,
    # 扩展字段（可选）
    extra_body={{
        ""thinking_enabled"": True,   # 深度思考
        ""search_enabled"": False,    # 联网搜索
    }},
)
for chunk in resp:
    d = chunk.choices[0].delta
    if hasattr(d, ""reasoning_content"") and d.reasoning_content:
        print(f""[思考] {{d.reasoning_content}}"")
    if d.content:
        print(d.content, end="""")


=== cURL ===
curl {baseUrl}/chat/completions ^
  -H ""Authorization: Bearer {apiKey}"" ^
  -H ""Content-Type: application/json"" ^
  -d ""{{\""model\"":\""{model}\"",\""stream\"":true,\""thinking_enabled\"":true,\""messages\"":[{{\""role\"":\""user\"",\""content\"":\""hi\""}}]}}""


=== Node.js ===
import OpenAI from 'openai';
const client = new OpenAI({{
    baseURL: '{baseUrl}',
    apiKey: '{apiKey}'
}});
const stream = await client.chat.completions.create({{
    model: '{model}',
    messages: [{{ role: 'user', content: '你好' }}],
    stream: true,
}});
for await (const c of stream) {{
    process.stdout.write(c.choices[0]?.delta?.content || '');
}}

本 API 完全兼容 OpenAI Chat Completions 协议，
额外支持两个可选字段：
  thinking_enabled (bool)  默认 true  —— 深度思考
  search_enabled   (bool)  默认 false —— 联网搜索
";
    }

    private void CopyUrl_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(UrlBox.Text);
    }

    private void CopyKey_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(KeyBox.Text);
    }
}