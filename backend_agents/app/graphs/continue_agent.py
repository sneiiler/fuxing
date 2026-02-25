from __future__ import annotations
from .state import GraphState
from ..models.documents import ContinueResponse
from ..models.common import DocRef, SelectionRange

def continue_node(state: GraphState) -> GraphState:
    doc = state.get("doc") or {"doc_id":"demo_doc","doc_version_id":"v1"}  # type: ignore
    inserted = " In conclusion, our findings highlight practical implications and future directions."
    # You may want to return as edit_result with 'insert' suggestions. Here we return text_reply only.
    state["text_reply"] = inserted.strip()
    return state
