from pydantic import BaseModel, Field, field_validator
from typing import Optional

class SelectionRange(BaseModel):
    startParagraphId: str = Field(..., description="ID of the starting paragraph")
    startOffset: int = Field(..., ge=0, description="Character offset within start paragraph")
    endParagraphId: str = Field(..., description="ID of the ending paragraph")
    endOffset: int = Field(..., ge=0, description="Character offset within end paragraph")

class DocRef(BaseModel):
    doc_id: str = Field(..., description="Document unique ID")
    doc_version_id: str = Field(..., description="Specific version of the document")

class Pagination(BaseModel):
    limit: int = Field(20, ge=1, le=200)
    cursor: Optional[str] = None

class SSEMetadata(BaseModel):
    request_id: str
    contract_version: Optional[str] = Field(None, description="X-Contract-Version echoed back")
