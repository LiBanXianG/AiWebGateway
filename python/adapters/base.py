from dataclasses import dataclass
from typing import AsyncGenerator

@dataclass
class UpstreamResponse:
    content: str = ""
    reasoning_content: str = ""
    finish_reason: str = "stop"