from pydantic import BaseModel, Field, ConfigDict
from typing import List, Optional, Literal, Any
from app.models.enums import FinishReason, Mode
from app.models.common import SelectionRange, DocRef

class ChatRole(str):
    system = "system"
    user = "user"
    assistant = "assistant"
    tool = "tool"

class ToolFunction(BaseModel):
    name: str
    description: Optional[str] = None
    parameters: Optional[dict] = Field(None, description="JSON Schema for tool parameters")

class ToolDefinition(BaseModel):
    type: Literal["function"] = "function"
    function: ToolFunction

class ToolChoice(BaseModel):
    type: Literal["auto", "none", "function"] = "auto"
    function: Optional[dict] = None  # { "name": "proofread" }

class ChatMessage(BaseModel):
    model_config = ConfigDict(extra='forbid')

    role: Literal["system", "user", "assistant", "tool"]
    content: Optional[str] = None
    name: Optional[str] = None
    tool_call_id: Optional[str] = None
    metadata: Optional[dict] = None

class ChatCompletionsRequest(BaseModel):
    model: str = Field(..., description="Logical model id; maps to LangGraph config")
    messages: List[ChatMessage]
    stream: bool = Field(False, description="Enable SSE streaming")
    temperature: Optional[float] = Field(None, ge=0, le=2)
    top_p: Optional[float] = Field(None, ge=0, le=1)
    tools: Optional[List[ToolDefinition]] = None
    tool_choice: Optional[ToolChoice] = None
    # Office plugin specific metadata (optional)
    mode: Optional[Mode] = None
    doc: Optional[DocRef] = None
    selection_range: Optional[SelectionRange] = None

class Usage(BaseModel):
    prompt_tokens: int = 0
    completion_tokens: int = 0
    total_tokens: int = 0

class Choice(BaseModel):
    index: int
    message: ChatMessage
    finish_reason: Optional[FinishReason] = None

class ChatCompletionsResponse(BaseModel):
    id: str
    object: Literal["chat.completion"] = "chat.completion"
    created: int
    model: str
    choices: List[Choice]
    usage: Optional[Usage] = None

# Streaming (SSE) chunk shapes
class ToolCallDelta(BaseModel):
    id: Optional[str] = None
    type: Optional[str] = None
    function: Optional[dict] = None  # {name, arguments(delta)}

class DeltaMessage(BaseModel):
    role: Optional[Literal["assistant"]] = None
    content: Optional[str] = None
    tool_calls: Optional[List[ToolCallDelta]] = None

class StreamChunk(BaseModel):
    id: str
    object: Literal["chat.completion.chunk"] = "chat.completion.chunk"
    created: int
    model: str
    choices: List[dict]  # {index, delta: DeltaMessage, finish_reason?}
