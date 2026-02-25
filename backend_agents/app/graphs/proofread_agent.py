from __future__ import annotations
from typing import List
from .state import GraphState
from ..models.tasks import EditSuggestion, EditSuggestionType, EditResultSummary, EditResultPayload
from ..models.common import SelectionRange, DocRef
from ..models.enums import OutputFormat

def proofread_node(state: GraphState) -> GraphState:
    # Fake a single suggestion for demo; replace with real model + detectors
    sel = state.get("selection_range") or SelectionRange(
        startParagraphId="p1", startOffset=0, endParagraphId="p1", endOffset=12
    )
    doc = state.get("doc") or {"doc_id":"demo_doc","doc_version_id":"v1"}  # type: ignore
    sugg = EditSuggestion(
        type="replace",
        range=sel,
        before="teh result",
        after="the result",
        rationale="拼写纠正"
    )
    payload = EditResultPayload(
        appliedTo=DocRef(**doc),
        selection_range=sel,
        output_format=OutputFormat.diff,
        summary=EditResultSummary(total_suggestions=1, notes="Demo proofread"),
        suggestions=[sugg],
        trace_id=state.get("trace_id"),
    )
    state["edit_result"] = payload
    state["text_reply"] = "已完成纠错，发现 1 处可改进之处。"
    return state
