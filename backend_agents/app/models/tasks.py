from pydantic import BaseModel, Field
from typing import List, Optional
from .common import SelectionRange, DocRef
from .enums import OutputFormat

class EditSuggestionType(str):
    replace = "replace"
    comment = "comment"
    insert = "insert"
    delete = "delete"

class EditSuggestion(BaseModel):
    type: str = Field(..., description="replace|comment|insert|delete")
    range: SelectionRange
    before: Optional[str] = None
    after: Optional[str] = None
    content: Optional[str] = None
    rationale: Optional[str] = None

class EditResultSummary(BaseModel):
    total_suggestions: int
    notes: Optional[str] = None

class EditResultPayload(BaseModel):
    type: str = "edit_result"
    appliedTo: DocRef
    selection_range: Optional[SelectionRange] = None
    output_format: OutputFormat
    summary: EditResultSummary
    suggestions: List[EditSuggestion]
    trace_id: Optional[str] = None
