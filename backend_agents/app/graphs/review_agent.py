from __future__ import annotations
from .state import GraphState

def review_node(state: GraphState) -> GraphState:
    # Demo review: append a short note; in real code, run checks and normalize outputs
    notes = state.get("intermediate_notes", [])
    target = state.get("routed_to","unknown")
    notes.append(f"Reviewed output from {target}.")
    state["intermediate_notes"] = notes
    # No mutation of payloads here; real implementation would validate/deduplicate suggestions/citations
    return state
