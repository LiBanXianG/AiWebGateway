# Python/core/tool_compat.py
"""
OpenAI tools ↔ DeepSeek 工具调用转换。
支持DSML和JSON两种输出。
"""
import json
import re
import uuid
from typing import Optional


TOOL_SYSTEM_PROMPT = """

# 工具调用能力

你可以调用以下工具来完成任务。**当需要使用工具时，只输出一个 JSON 对象**，不要包含任何其他文字：

{"tool_calls":[{"name":"<工具名>","arguments":{<参数对象>}}]}

## 可用工具列表

{tools_desc}

## 重要规则
1. 调用工具时，不要输出前后缀、说明、markdown。
2. `arguments` 必须是合法 JSON 对象。
3. 如果不需要调用工具，正常用中文回答。
"""


def tools_to_system_prompt(tools: list) -> str:
    if not tools:
        return ""
    descs = []
    for tool in tools:
        func = tool.get("function", {}) if "function" in tool else tool
        descs.append(
            f"### {func.get('name','')}\n{func.get('description','')}\n"
            f"参数: {json.dumps(func.get('parameters', {}), ensure_ascii=False)}"
        )
    return TOOL_SYSTEM_PROMPT.replace("{tools_desc}", "\n\n".join(descs))


# ═══════════════════════════════════════════════
# DSML 解析（宽松匹配）
# ═══════════════════════════════════════════════
# 实际格式（来自 DeepSeek Harness）：
#   < | DSML | | calls>
#   < | DSML | | invoke name="pwsh">
#   < | DSML | | parameter name="command" string="true">Get-ChildItem</ | DSML | | parameter>
#   </ | DSML | | invoke>
#   </ | DSML | | calls>
#
# 标签内的空白、竖线数量、斜杠位置都不固定
# ═══════════════════════════════════════════════

# 匹配任何包含 "DSML" 和 "invoke" 的标签
_INVOKE_OPEN = re.compile(
    r'<[^<>]*DSML[^<>]*invoke[^<>]*name\s*=\s*["\']([^"\']+)["\'][^<>]*>',
    re.IGNORECASE | re.DOTALL,
)
_INVOKE_CLOSE = re.compile(
    r'<[^<>/]*/\s*[^<>]*DSML[^<>]*invoke[^<>]*>',
    re.IGNORECASE | re.DOTALL,
)
_INVOKE_CLOSE_ALT = re.compile(
    r'<[^<>]*DSML[^<>]*/\s*invoke[^<>]*>',
    re.IGNORECASE | re.DOTALL,
)

# 匹配任何包含 "DSML" 和 "parameter" 的标签
_PARAM = re.compile(
    r'<[^<>]*DSML[^<>]*parameter[^<>]*'
    r'name\s*=\s*["\']([^"\']+)["\']'
    r'(?:[^<>]*string\s*=\s*["\'](true|false)["\'])?'
    r'[^<>]*>(.*?)'
    r'<[^<>/]*/\s*[^<>]*DSML[^<>]*parameter[^<>]*>',
    re.IGNORECASE | re.DOTALL,
)


def _parse_dsml_tool_calls(text: str) -> Optional[list]:
    """解析 DSML 工具调用（宽松匹配任意空白/竖线）"""
    if "DSML" not in text.upper():
        return None

    calls = []
    # 定位所有 invoke name="..."
    for m in _INVOKE_OPEN.finditer(text):
        tool_name = m.group(1).strip()
        body_start = m.end()
        # 找最近的 invoke 结束标签
        close_match = _INVOKE_CLOSE.search(text, body_start)
        if not close_match:
            close_match = _INVOKE_CLOSE_ALT.search(text, body_start)
        body_end = close_match.start() if close_match else len(text)
        body = text[body_start:body_end]

        # 解析参数
        args = {}
        for pm in _PARAM.finditer(body):
            pname = pm.group(1).strip()
            is_string = (pm.group(2) or "").lower() == "true"
            pvalue = pm.group(3).strip()
            if is_string:
                args[pname] = pvalue
            else:
                try:
                    args[pname] = json.loads(pvalue)
                except (json.JSONDecodeError, ValueError):
                    args[pname] = pvalue

        if tool_name:
            calls.append({"name": tool_name, "arguments": args})

    return calls if calls else None


def _parse_json_tool_calls(text: str) -> Optional[list]:
    text = text.strip()
    if text.startswith("```"):
        text = re.sub(r"^```(?:json)?\s*", "", text)
        text = re.sub(r"\s*```$", "", text).strip()

    candidates = [text]
    first, last = text.find("{"), text.rfind("}")
    if first >= 0 and last > first:
        candidates.append(text[first:last + 1])

    for cand in candidates:
        try:
            obj = json.loads(cand)
        except (json.JSONDecodeError, ValueError):
            continue
        if isinstance(obj, dict) and "tool_calls" in obj:
            calls = obj["tool_calls"]
            if isinstance(calls, list) and all(
                isinstance(c, dict) and "name" in c for c in calls
            ):
                return calls
    return None


def parse_tool_calls(content: str) -> Optional[list]:
    """尝试解析工具调用。优先DSML，回退JSON。"""
    if not content:
        return None

    if "DSML" in content.upper():
        result = _parse_dsml_tool_calls(content)
        if result:
            return result

    return _parse_json_tool_calls(content)


def has_dsml(text: str) -> bool:
    return "DSML" in text.upper()


def build_tool_calls_response(calls: list) -> list:
    result = []
    for i, call in enumerate(calls):
        args = call.get("arguments", {})
        if not isinstance(args, str):
            args = json.dumps(args, ensure_ascii=False)
        result.append({
            "index": i,
            "id": f"call_{uuid.uuid4().hex[:12]}",
            "type": "function",
            "function": {
                "name": call.get("name", ""),
                "arguments": args,
            },
        })
    return result

def looks_like_tool_call(text: str) -> bool:
    """快速判断文本是否可能是工具调用, 用于流式检测提前终止"""
    if not text:
        return False
    if "DSML" in text or "dsml" in text:
        return True
    if '"tool_calls"' in text:
        return True
    if text.lstrip().startswith('{"tool_calls"'):
        return True
    return False