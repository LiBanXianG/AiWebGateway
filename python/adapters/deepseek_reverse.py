# Python/adapters/deepseek_reverse.py
import aiohttp
import json
from typing import AsyncGenerator, Optional
from .base import UpstreamResponse
from core.pow_solver import DeepSeekPowSolver
from core.session_cache import SessionCache

_session_cache = SessionCache(ttl=7200)

_global_pow_solver: DeepSeekPowSolver = None
_global_http_session: aiohttp.ClientSession = None


def _get_global_pow_solver() -> DeepSeekPowSolver:
    global _global_pow_solver
    if _global_pow_solver is None:
        _global_pow_solver = DeepSeekPowSolver()
    return _global_pow_solver


def _get_global_http_session() -> aiohttp.ClientSession:
    global _global_http_session
    if _global_http_session is None or _global_http_session.closed:
        _global_http_session = aiohttp.ClientSession(
            timeout=aiohttp.ClientTimeout(total=300)
        )
    return _global_http_session


def _clean(s: str) -> str:
    if not s:
        return ""
    return s.replace("\r", "").replace("\n", "").strip()


def _safe_get(data, *keys):
    cur = data
    for k in keys:
        if not isinstance(cur, dict):
            return None
        cur = cur.get(k)
        if cur is None:
            return None
    return cur


class DeepSeekReverseAdapter:
    BASE_URL = "https://chat.deepseek.com"
    COMPLETION_PATH = "/api/v0/chat/completion"
    UA = ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
          "AppleWebKit/537.36 (KHTML, like Gecko) "
          "Chrome/120.0.0.0 Safari/537.36")

    def __init__(self, token: str, cookie: str):
        self.token = _clean(token)
        self.cookie = _clean(cookie)
        self.pow_solver = _get_global_pow_solver()
        # 记录本次请求用到的 session_id 与最新 response_message_id
        self.last_chat_session_id: Optional[str] = None
        self._last_response_message_id: Optional[int] = None

    def _headers(self) -> dict:
        return {
            "Authorization": self.token,
            "Cookie": self.cookie,
            "User-Agent": self.UA,
            "Content-Type": "application/json",
        }

    async def _get_session(self) -> aiohttp.ClientSession:
        return _get_global_http_session()

    async def _create_session(self) -> str:
        session = await self._get_session()
        async with session.post(
            f"{self.BASE_URL}/api/v0/chat_session/create",
            json={},
            headers=self._headers(),
        ) as resp:
            text = await resp.text()
            if resp.status != 200:
                raise ConnectionError(f"create_session HTTP {resp.status}: {text[:300]}")
            try:
                data = json.loads(text)
            except json.JSONDecodeError:
                raise ConnectionError(f"create_session invalid JSON: {text[:300]}")
            sid = _safe_get(data, "data", "biz_data", "id")
            if not sid:
                raise ConnectionError(
                    f"create_session no session_id (token/cookie 可能已过期): {text[:300]}"
                )
            return sid

    async def _get_pow_challenge(self) -> dict:
        session = await self._get_session()
        async with session.post(
            f"{self.BASE_URL}/api/v0/chat/create_pow_challenge",
            json={"target_path": "/api/v0/chat/completion"},
            headers=self._headers(),
        ) as resp:
            text = await resp.text()
            if resp.status != 200:
                raise ConnectionError(f"pow_challenge HTTP {resp.status}: {text[:300]}")
            try:
                data = json.loads(text)
            except json.JSONDecodeError:
                raise ConnectionError(f"pow_challenge invalid JSON: {text[:300]}")
            ch = _safe_get(data, "data", "biz_data", "challenge")
            if not isinstance(ch, dict):
                raise ConnectionError(
                    f"pow_challenge no challenge (token/cookie 可能已过期): {text[:300]}"
                )
            return ch

    async def chat(
        self,
        messages: list,
        model: str = "deepseek-chat",
        stream: bool = True,
        thinking_enabled: bool = True,
        search_enabled: bool = False,
        conversation_id: str = "",
        **kwargs,
    ) -> AsyncGenerator[UpstreamResponse, None]:
        session = await self._get_session()
        # 只取最后一条 user 消息作为 prompt
        prompt = messages[-1]["content"]
        self._last_response_message_id = None

        try:
            # 从缓存复用 chat_session_id + parent_message_id
            chat_session_id: Optional[str] = None
            parent_message_id: Optional[int] = None

            if conversation_id:
                chat_session_id, parent_message_id = _session_cache.get(conversation_id)

            is_new_session = False
            if not chat_session_id:
                chat_session_id = await self._create_session()
                parent_message_id = None
                is_new_session = True
                if conversation_id:
                    _session_cache.set(conversation_id, chat_session_id, None)

            # 记录本次用到的 session
            self.last_chat_session_id = chat_session_id

            challenge_data = await self._get_pow_challenge()
            algorithm = challenge_data.get("algorithm", "DeepSeekHashV1")
            challenge = challenge_data["challenge"]
            salt = challenge_data["salt"]
            signature = challenge_data.get("signature", "")
            difficulty = challenge_data.get("difficulty", 144000)
            expire_at = challenge_data.get("expire_at", 0)

            pow_response = await self.pow_solver.solve(
                algorithm, challenge, salt, difficulty, expire_at,
                signature=signature,
            )
            pow_response = _clean(pow_response)

            headers = self._headers()
            headers["x-ds-pow-response"] = pow_response

            # body 与浏览器抓包一致
            body = {
                "action": None,
                "chat_session_id": chat_session_id,
                "model_type": "default" if is_new_session else None,
                "parent_message_id": parent_message_id,   # null 或数字
                "preempt": False,
                "prompt": prompt,
                "ref_file_ids": [],
                "search_enabled": bool(search_enabled),
                "thinking_enabled": bool(thinking_enabled),
            }

            async with session.post(
                f"{self.BASE_URL}{self.COMPLETION_PATH}",
                json=body,
                headers=headers,
            ) as resp:
                if resp.status != 200:
                    error_text = await resp.text()
                    # session 失效 → 清缓存，下次重建
                    if conversation_id and resp.status in (400, 401, 403, 404):
                        _session_cache.remove(conversation_id)
                    raise ConnectionError(
                        f"completion HTTP {resp.status}: {error_text[:300]}"
                    )

                async for chunk in self._parse_stream(resp):
                    yield chunk

            # 流结束后保存 response_message_id
            if conversation_id and self._last_response_message_id is not None:
                _session_cache.update_parent(conversation_id, self._last_response_message_id)

        except Exception as e:
            yield UpstreamResponse(
                content=f"[reverse failed] {e}", finish_reason="stop"
            )

    async def _parse_stream(self, resp) -> AsyncGenerator[UpstreamResponse, None]:
        buffer = ""
        current_type = None
        last_reasoning = ""
        last_content = ""
        current_event = None

        async for line in resp.content:
            buffer += line.decode("utf-8", errors="replace")
            while "\n" in buffer:
                raw_line, buffer = buffer.split("\n", 1)
                raw_line = raw_line.strip()

                if not raw_line:
                    continue

                # 记录 event: 行，识别 ready 帧
                if raw_line.startswith("event:"):
                    current_event = raw_line[6:].strip()
                    continue

                if raw_line.startswith(":") or not raw_line.startswith("data:"):
                    continue

                payload = raw_line[5:].strip()
                if not payload:
                    continue

                # 捕获 ready 帧里的 response_message_id
                if current_event == "ready":
                    try:
                        r = json.loads(payload)
                        rmid = r.get("response_message_id")
                        if isinstance(rmid, int):
                            self._last_response_message_id = rmid
                    except json.JSONDecodeError:
                        pass
                    current_event = None
                    continue

                current_event = None

                try:
                    frame = json.loads(payload)
                except json.JSONDecodeError:
                    continue

                content = ""
                reasoning = ""

                if isinstance(frame.get("v"), dict):
                    resp_obj = frame["v"].get("response", {})
                    mid = resp_obj.get("message_id")
                    if isinstance(mid, int) and self._last_response_message_id is None:
                        self._last_response_message_id = mid

                    for frag in resp_obj.get("fragments", []):
                        ftype = frag.get("type")
                        if ftype:
                            current_type = ftype
                        fcontent = frag.get("content") or ""
                        if ftype == "THINK":
                            reasoning += fcontent
                        elif ftype == "RESPONSE":
                            content += fcontent

                elif isinstance(frame.get("v"), str) and "p" not in frame:
                    if current_type == "THINK":
                        reasoning = frame["v"]
                    elif current_type == "RESPONSE":
                        content = frame["v"]

                elif "p" in frame:
                    path = frame.get("p", "")
                    v = frame.get("v")

                    if path == "response/status" and v == "FINISHED":
                        yield UpstreamResponse(finish_reason="stop")
                        return

                    elif path == "response/fragments" and isinstance(v, list):
                        for frag in v:
                            ftype = frag.get("type")
                            if ftype:
                                current_type = ftype
                            fcontent = frag.get("content") or ""
                            if ftype == "THINK":
                                reasoning += fcontent
                            elif ftype == "RESPONSE":
                                content += fcontent

                    elif path.endswith("/content") and isinstance(v, str):
                        if current_type == "THINK":
                            reasoning = v
                        elif current_type == "RESPONSE":
                            content = v

                    elif path.endswith("/results") and isinstance(v, list):
                        reasoning = f"[Search] Found {len(v)} web pages.\n"

                    elif path == "response/fragments/-1" and isinstance(v, list):
                        for item in v:
                            if item.get("p") == "content" and item.get("v"):
                                reasoning = f"[Search] {item['v']}\n"

                if reasoning and reasoning != last_reasoning:
                    last_reasoning = reasoning
                    yield UpstreamResponse(reasoning_content=reasoning)
                if content and content != last_content:
                    last_content = content
                    yield UpstreamResponse(content=content)