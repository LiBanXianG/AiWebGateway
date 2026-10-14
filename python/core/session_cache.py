# Python/core/session_cache.py
import time
from typing import Optional, Tuple


class SessionCache:
    """按 conversation_id 缓存 DeepSeek 的 chat_session_id 和 parent_message_id"""

    def __init__(self, ttl: int = 7200):
        # conversation_id -> (chat_session_id, parent_message_id, expire_at)
        self._cache: dict[str, Tuple[str, Optional[int], float]] = {}
        self._ttl = ttl

    def get(self, conversation_id: str) -> Tuple[Optional[str], Optional[int]]:
        if not conversation_id:
            return None, None
        if conversation_id in self._cache:
            sid, pmid, exp = self._cache[conversation_id]
            if time.time() < exp:
                return sid, pmid
            del self._cache[conversation_id]
        return None, None

    def set(self, conversation_id: str, chat_session_id: str,
            parent_message_id: Optional[int] = None):
        if not conversation_id:
            return
        self._cache[conversation_id] = (
            chat_session_id, parent_message_id, time.time() + self._ttl
        )

    def update_parent(self, conversation_id: str, parent_message_id: int):
        if not conversation_id or conversation_id not in self._cache:
            return
        sid, _, exp = self._cache[conversation_id]
        self._cache[conversation_id] = (sid, parent_message_id, exp)

    def remove(self, conversation_id: str):
        self._cache.pop(conversation_id, None)