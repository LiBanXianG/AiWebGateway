AiWebGateway

一个基于 C# WPF 与 Python 混合开发的 AI 网关应用。专注于稳定接入 DeepSeek 网页端会话在本地使用API调用，支持账号池管理、多轮对话上下文维护以及 SSE 流式输出。
目前只适配了deepseek harness的一部分原生工具调用

技术栈

前端/主程序：C# WPF (.NET)
后端 API 服务：Python (FastAPI / Uvicorn)
核心依赖：DeepSeek API 适配器、WASM SHA3 计算、客户端无状态会话缓存

项目结构

`Models/` - C# 数据模型 (ChatMessage, Conversation 等)
`Services/` - C# 业务逻辑 (ChatService, ConfigService, GatewayService 等)
`Views/` - WPF 界面 (聊天窗口, 设置窗口等)
`python/` - Python FastAPI 后端源码
`adapters/` - DeepSeek 等接口适配器
`api/` - OpenAI 兼容接口路由
`core/` - 核心逻辑 (账号池, Session 缓存, SSE 管道等)
`test/` - 测试脚本 (test_client.py)
