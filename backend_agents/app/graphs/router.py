from __future__ import annotations
from typing import Dict, Any, Callable
import time, uuid
from langgraph.graph import StateGraph, START, END
from .state import GraphState
from ..models.enums import Mode
from .proofread_agent import proofread_node
from .polish_agent import polish_node
from .continue_agent import continue_node
from .qa_agent import qa_node
from .review_agent import review_node

# --- Routing logic ---
KEYWORDS = {
    "proofread": ["纠错","校对","grammar","proofread","错误","错别字"],
    "polish": ["润色","polish","通顺","优化","改写","语气"],
    "continue_writing": ["续写","继续写","extend","continue"],
    "file_qa": ["问答","Q&A","根据文件","检索","查找","解释"],
}

def _heuristic_route(state: GraphState) -> str:
    # 1) explicit tool_choice wins
    tool = (state.get("tool_choice") or "").lower()
    if tool in {"proofread","polish","continue_writing","file_qa"}:
        return tool
    # 2) mode hints
    mode = state.get("mode")
    if mode == Mode.review:
        # review acts as a post step; default to proofread if selection exists, else polish
        return "proofread" if state.get("selection_range") else "polish"
    # 3) user message keywords
    text = (state.get("user_message") or "").lower()
    for route, kws in KEYWORDS.items():
        if any(k.lower() in text for k in kws):
            return route
    # 4) default by mode/chat
    if mode == Mode.chat:
        return "file_qa" if state.get("doc") else "polish"
    return "polish"

def router_node(state: GraphState) -> GraphState:
    route = _heuristic_route(state)
    notes = state.get("intermediate_notes", [])
    notes.append(f"Routed to: {route}")
    state["routed_to"] = route
    state["intermediate_notes"] = notes
    state["trace_id"] = state.get("trace_id") or f"tr_{uuid.uuid4().hex[:8]}"
    return state

# --- Build graph ---
def build_graph() -> StateGraph:
    g = StateGraph(GraphState)
    g.add_node("router", router_node)
    g.add_node("proofread", proofread_node)
    g.add_node("polish", polish_node)
    g.add_node("continue_writing", continue_node)
    g.add_node("file_qa", qa_node)
    g.add_node("review", review_node)

    g.add_edge(START, "router")
    # conditional edges based on routed_to
    def _to_target(state: GraphState) -> str:
        return state.get("routed_to","polish")

    g.add_conditional_edges("router", _to_target, {
        "proofread": "proofread",
        "polish": "polish",
        "continue_writing": "continue_writing",
        "file_qa": "file_qa",
    })

    # after each leaf, go to review -> END
    for leaf in ["proofread","polish","continue_writing","file_qa"]:
        g.add_edge(leaf, "review")
    g.add_edge("review", END)
    return g

# A convenience runner usable from your API layer
class Orchestrator:
    def __init__(self):
        self.app = build_graph().compile()

    def run(self, state: GraphState) -> GraphState:
        # Synchronous stepping; LangGraph supports async too if needed
        return self.app.invoke(state)

# Singleton orchestrator
orchestrator = Orchestrator()
