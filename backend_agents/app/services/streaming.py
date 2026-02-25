"""SSE streaming helpers for OpenAI-compatible chat.completions chunks."""
from __future__ import annotations
import json, time, asyncio
from typing import AsyncGenerator, Optional, Dict, Any

def _chunk(id: str, model: str, delta: Dict[str, Any], finish_reason: str | None = None) -> Dict[str, Any]:
    return {
        "id": id,
        "object": "chat.completion.chunk",
        "created": int(time.time()),
        "model": model,
        "choices": [{"index": 0, "delta": delta, "finish_reason": finish_reason}],
    }

async def sse_text_stream(content: str, model: str = "office-agent-4o", delay: float = 0.0) -> AsyncGenerator[str, None]:
    cid = f"chatcmpl_{int(time.time()*1000)}"
    # role
    yield f"data: {json.dumps(_chunk(cid, model, {\"role\": \"assistant\"}))}\n\n"
    await asyncio.sleep(delay)
    # content in small deltas
    for ch in content:
        yield f"data: {json.dumps(_chunk(cid, model, {\"content\": ch}))}\n\n"
        if delay:
            await asyncio.sleep(delay)
    # finish
    yield f"data: {json.dumps(_chunk(cid, model, {}, finish_reason=\"stop\"))}\n\n"
    yield "data: [DONE]\n\n"
