from pydantic import BaseModel, Field
from typing import Optional
from .enums import Mode

class SessionCreateRequest(BaseModel):
    mode: Mode = Mode.chat
    doc_id: Optional[str] = None

class SessionObject(BaseModel):
    session_id: str
    mode: Mode
    doc_id: Optional[str] = None
