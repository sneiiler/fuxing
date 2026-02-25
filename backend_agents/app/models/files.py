from pydantic import BaseModel, Field
from typing import Optional, List
from enum import Enum

class FilePurpose(str, Enum):
    assistants = "assistants"
    office_doc = "office-doc"
    embeddings = "embeddings"
    fine_tune = "fine-tune"

class FileObject(BaseModel):
    id: str
    object: str = "file"
    bytes: int
    created_at: int
    filename: str
    purpose: FilePurpose
    status: Optional[str] = None
    status_details: Optional[str] = None

class FileUploadInit(BaseModel):
    purpose: FilePurpose
    filename: str
    mime_type: str
    bytes: int

class FileListResponse(BaseModel):
    data: List[FileObject]
    object: str = "list"
