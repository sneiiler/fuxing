from pydantic import BaseModel, Field
from typing import Optional, List
from .common import SelectionRange, DocRef
from .enums import Strictness, Tone, OutputFormat, Mode

# ---- Tool argument models ----

class ProofreadArgs(BaseModel):
    doc_id: str
    doc_version_id: str
    selection_range: Optional[SelectionRange] = None
    language: Optional[str] = None
    strictness: Optional[Strictness] = None
    output_format: OutputFormat

class PolishArgs(BaseModel):
    doc_id: str
    doc_version_id: str
    selection_range: Optional[SelectionRange] = None
    tone: Optional[Tone] = None
    target_audience: Optional[str] = None
    constraints: Optional[List[str]] = None
    output_format: OutputFormat

class ContinueWritingArgs(BaseModel):
    doc_id: str
    doc_version_id: str
    selection_range: Optional[SelectionRange] = None
    length_hint: Optional[str] = Field(default="medium", pattern="^(short|medium|long|\\d+tokens)$")
    style_preset: Optional[str] = None
    structure_constraints: Optional[List[str]] = None

class FileQAArgs(BaseModel):
    doc_id: str
    doc_version_id: str
    query: str
    top_k: Optional[int] = 5
    return_citations: Optional[bool] = True

class SetModeArgs(BaseModel):
    mode: Mode
