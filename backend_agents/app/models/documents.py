from pydantic import BaseModel, Field
from typing import Optional, List
from .common import DocRef, SelectionRange

class UploadInitResponse(BaseModel):
    file_id: str
    upload_url: str
    content_type: str
    max_bytes: int = 50 * 1024 * 1024

class DocumentMeta(BaseModel):
    doc_id: str
    latest_version_id: str
    filename: str
    mime_type: str
    size_bytes: int

class IndexResponse(BaseModel):
    doc_id: str
    doc_version_id: str
    chunks_indexed: int

class ContinueRequest(BaseModel):
    doc: DocRef
    selection_range: Optional[SelectionRange] = None
    length_hint: Optional[str] = "medium"
    style_preset: Optional[str] = None

class ContinueResponse(BaseModel):
    doc: DocRef
    inserted_text: str
    trace_id: Optional[str] = None
