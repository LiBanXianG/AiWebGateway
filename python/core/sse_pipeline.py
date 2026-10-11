# Python/core/sse_pipeline.py
import json
import time
import uuid
from typing import AsyncGenerator


class UnifiedSSEPipeline:
    def __init__(self):
        self.completion_id = f"chatcmpl-{uuid.uuid4().hex[:12]}"

    def _base(self, model: str) -> dict:
        return {
            "id": self.completion_id,
            "object": "chat.completion.chunk",
            "created": int(time.time()),
            "model": model,
        }

    def sse_reasoning_chunk(self, reasoning: str, model: str):
        chunk = self._base(model)
        chunk["choices"] = [{
            "index": 0,
            "delta": {"reasoning_content": reasoning},
            "finish_reason": None,
        }]
        yield f"data: {json.dumps(chunk, ensure_ascii=False)}\n\n"

    def sse_content_chunk(self, content: str, model: str):
        chunk = self._base(model)
        chunk["choices"] = [{
            "index": 0,
            "delta": {"content": content},
            "finish_reason": None,
        }]
        yield f"data: {json.dumps(chunk, ensure_ascii=False)}\n\n"

    def sse_tool_calls_chunk(self, tool_calls: list, model: str):
        chunk = self._base(model)
        chunk["choices"] = [{
            "index": 0,
            "delta": {"tool_calls": tool_calls},
            "finish_reason": None,
        }]
        yield f"data: {json.dumps(chunk, ensure_ascii=False)}\n\n"

    def sse_finish_chunk(self, model: str, reason: str = "stop"):
        chunk = self._base(model)
        chunk["choices"] = [{
            "index": 0,
            "delta": {},
            "finish_reason": reason,
        }]
        yield f"data: {json.dumps(chunk, ensure_ascii=False)}\n\n"

    async def stream(self, upstream_gen, model: str) -> AsyncGenerator[str, None]:
        # 首帧 role
        first = self._base(model)
        first["choices"] = [{
            "index": 0,
            "delta": {"role": "assistant", "content": ""},
            "finish_reason": None,
        }]
        yield f"data: {json.dumps(first, ensure_ascii=False)}\n\n"

        async for resp in upstream_gen:
            delta = {}
            if resp.reasoning_content:
                delta["reasoning_content"] = resp.reasoning_content
            if resp.content:
                delta["content"] = resp.content
            if not delta:
                continue

            chunk = self._base(model)
            chunk["choices"] = [{
                "index": 0,
                "delta": delta,
                "finish_reason": None,
            }]
            yield f"data: {json.dumps(chunk, ensure_ascii=False)}\n\n"

        # 结束帧
        final = self._base(model)
        final["choices"] = [{
            "index": 0,
            "delta": {},
            "finish_reason": "stop",
        }]
        yield f"data: {json.dumps(final, ensure_ascii=False)}\n\n"
        yield "data: [DONE]\n\n"