# Python/api/openai_compat.py
import time
import uuid
import yaml
from pathlib import Path
from fastapi import APIRouter, Request
from fastapi.responses import StreamingResponse, JSONResponse
from adapters.deepseek_reverse import DeepSeekReverseAdapter
from core.sse_pipeline import UnifiedSSEPipeline
from core.tool_compat import (
    tools_to_system_prompt,
    parse_tool_calls,
    build_tool_calls_response,
    has_dsml,
)

router = APIRouter()

CONFIG_PATH = Path(__file__).parent.parent / "config.yaml"


def load_config():
    with open(CONFIG_PATH, "r", encoding="utf-8") as f:
        return yaml.safe_load(f)


def messages_to_prompt(messages: list) -> str:
    parts = []
    for m in messages:
        role = (m.get("role") or "user").lower()
        content = m.get("content") or ""
        if not content:
            continue
        if role == "system":
            parts.append(f"[System]\n{content}")
        elif role == "user":
            parts.append(f"User: {content}")
        elif role == "assistant":
            parts.append(f"Assistant: {content}")
        else:
            parts.append(f"User: {content}")
    parts.append("Assistant:")
    return "\n\n".join(parts)


@router.post("/chat/completions")
async def chat_completions(request: Request):
    body = await request.json()
    messages = list(body.get("messages", []))
    model = body.get("model", "deepseek-chat")
    stream = body.get("stream", False)
    thinking_enabled = body.get("thinking_enabled", True)
    search_enabled = body.get("search_enabled", False)
    new_session = body.get("new_session", False)
    tools = body.get("tools", [])

    conversation_id = (
        request.headers.get("x-conversation-id")
        or body.get("conversation_id")
        or uuid.uuid4().hex
    )
    if new_session:
        conversation_id = uuid.uuid4().hex

    if tools:
        tool_prompt = tools_to_system_prompt(tools)
        if tool_prompt:
            messages.insert(0, {"role": "system", "content": tool_prompt})

    config = load_config()
    accounts = config.get("deepseek", {}).get("accounts", [])
    if not accounts:
        return JSONResponse(
            {"error": {"message": "No account configured.", "type": "config_error"}},
            status_code=503,
        )

    account = accounts[0]
    single_prompt = messages_to_prompt(messages)
    adapter = DeepSeekReverseAdapter(account["token"], account["cookie"])
    pipeline = UnifiedSSEPipeline()

    #无论有没有 tools，都缓冲完整响应，检测 DSML / tool_calls
    full_content = ""
    full_reasoning = ""
    async for resp in adapter.chat(
        [{"role": "user", "content": single_prompt}],
        model=model,
        thinking_enabled=thinking_enabled,
        search_enabled=search_enabled,
        conversation_id="",
    ):
        full_content += resp.content
        full_reasoning += resp.reasoning_content

    calls = parse_tool_calls(full_content)

    if stream:
        async def gen():
            if calls:
                if full_reasoning:
                    for line in pipeline.sse_reasoning_chunk(full_reasoning, model):
                        yield line
                tc = build_tool_calls_response(calls)
                for line in pipeline.sse_tool_calls_chunk(tc, model):
                    yield line
                for line in pipeline.sse_finish_chunk(model, "tool_calls"):
                    yield line
                yield "data: [DONE]\n\n"
            else:
                if full_reasoning:
                    for line in pipeline.sse_reasoning_chunk(full_reasoning, model):
                        yield line
                if full_content:
                    for line in pipeline.sse_content_chunk(full_content, model):
                        yield line
                for line in pipeline.sse_finish_chunk(model, "stop"):
                    yield line
                yield "data: [DONE]\n\n"

        return StreamingResponse(
            gen(),
            media_type="text/event-stream",
            headers={"X-Conversation-Id": conversation_id, "Cache-Control": "no-cache"},
        )
    else:
        if calls:
            return JSONResponse({
                "id": pipeline.completion_id,
                "object": "chat.completion",
                "created": int(time.time()),
                "model": model,
                "choices": [{
                    "index": 0,
                    "message": {
                        "role": "assistant",
                        "content": None,
                        "reasoning_content": full_reasoning,
                        "tool_calls": build_tool_calls_response(calls),
                    },
                    "finish_reason": "tool_calls",
                }],
                "usage": {"prompt_tokens": 0, "completion_tokens": 0, "total_tokens": 0},
            }, headers={"X-Conversation-Id": conversation_id})

        return JSONResponse({
            "id": pipeline.completion_id,
            "object": "chat.completion",
            "created": int(time.time()),
            "model": model,
            "choices": [{
                "index": 0,
                "message": {
                    "role": "assistant",
                    "content": full_content,
                    "reasoning_content": full_reasoning,
                },
                "finish_reason": "stop",
            }],
            "usage": {"prompt_tokens": 0, "completion_tokens": 0, "total_tokens": 0},
        }, headers={"X-Conversation-Id": conversation_id})


@router.get("/models")
async def list_models():
    return {
        "object": "list",
        "data": [
            {"id": "deepseek-chat", "object": "model", "created": 1700000000, "owned_by": "deepseek"},
            {"id": "deepseek-reasoner", "object": "model", "created": 1700000000, "owned_by": "deepseek"},
        ],
    }