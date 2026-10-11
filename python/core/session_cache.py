# Python/core/session_cache.py
import time
from typing import Dict, Optional


class SessionCache:
    """按 conversation_id 缓存 DeepSeek 的 chat_session_id，实现多轮对话"""

    def __init__(self, ttl: int = 3600):
        self._cache: Dict[str, tuple[str, float]] = {}
        self._ttl = ttl

    def get(self, conversation_id: str) -> Optional[str]:
        if conversation_id in self._cache:
            session_id, expire_at = self._cache[conversation_id]
            if time.time() < expire_at:
                return session_id
            del self._cache[conversation_id]
        return None

    def set(self, conversation_id: str, session_id: str):
        self._cache[conversation_id] = (session_id, time.time() + self._ttl)

    def remove(self, conversation_id: str):
        self._cache.pop(conversation_id, None)