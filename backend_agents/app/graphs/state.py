from typing import TypedDict, Optional, Any, Dict, List
from ..models.common import DocRef, SelectionRange
from ..models.enums import Mode
from ..models.tasks import EditResultPayload
from ..models.qa import QAResultPayload

class GraphState(TypedDict, total=False):
    # Inputs
    mode: Mode
    doc: Optional[DocRef]
    selection_range: Optional[SelectionRange]
    user_message: str
    tool_choice: Optional[str]           # proofread | polish | continue_writing | file_qa | None
    tool_args: Optional[Dict[str, Any]]  # already validated upstream (optional)
    # Working
    routed_to: Optional[str]
    intermediate_notes: List[str]
    # Outputs
    text_reply: Optional[str]
    edit_result: Optional[EditResultPayload]
    qa_result: Optional[QAResultPayload]
    trace_id: Optional[str]
