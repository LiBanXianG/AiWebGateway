from openai import OpenAI

client = OpenAI(
    base_url="http://127.0.0.1:8000/v1",
    api_key="any-string"  # 本地网关不需要真实 key
)

try:
    response = client.chat.completions.create(
        model="deepseek-chat",
        messages=[{"role": "user", "content": "用一句话介绍自己"}],
        stream=True
    )
    print("AI 回复: ", end="", flush=True)
    for chunk in response:
        if chunk.choices[0].delta.content:
            print(chunk.choices[0].delta.content, end="", flush=True)
    print()
except Exception as e:
    print(f"\n调用失败: {e}")