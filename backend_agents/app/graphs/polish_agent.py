from __future__ import annotations
from .state import GraphState
from ..models.tasks import EditSuggestion, EditResultSummary, EditResultPayload
from ..models.common import SelectionRange, DocRef
from ..models.enums import OutputFormat

def polish_node(state: GraphState) -> GraphState:
    sel = state.get("selection_range") or SelectionRange(
        startParagraphId="p1", startOffset=0, endParagraphId="p1", endOffset=20
    )
    doc = state.get("doc") or {"doc_id":"demo_doc","doc_version_id":"v1"}  # type: ignore
    sugg = EditSuggestion(
        type="replace",
        range=sel,
        before="This paper try to...",
        after="This paper attempts to...",
        rationale="更正式且自然的表达"
    )
    payload = EditResultPayload(
        appliedTo=DocRef(**doc),
        selection_range=sel,
        output_format=OutputFormat.diff,
        summary=EditResultSummary(total_suggestions=1, notes="Demo polish"),
        suggestions=[sugg],
        trace_id=state.get("trace_id"),
    )
    state["edit_result"] = payload
    state["text_reply"] = "已完成润色，提供了更自然的表达。"
    return state
