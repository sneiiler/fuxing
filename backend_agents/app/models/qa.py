from pydantic import BaseModel, Field
from typing import List, Optional
from .common import DocRef

class QACitation(BaseModel):
    doc_version_id: str
    chunk_id: str
    score: float
    snippet: str
    section_path: Optional[str] = None
    page_no: Optional[int] = None

class QAResultPayload(BaseModel):
    type: str = "qa_result"
    answer: str
    citations: List[QACitation]
    trace_id: Optional[str] = None
