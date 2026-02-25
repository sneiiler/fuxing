from __future__ import annotations
from .state import GraphState
from ..models.qa import QAResultPayload, QACitation

def qa_node(state: GraphState) -> GraphState:
    # Stub: echo a deterministic answer with a fake citation
    answer = "根据已索引的文件：该方法通过两步检索与重排序提高相关性。"
    citations = [QACitation(doc_version_id=(state.get("doc") or {"doc_version_id":"v1"})["doc_version_id"],  # type: ignore
                            chunk_id="c1", score=0.83, snippet="...two-stage retrieval and reranking...")]
    state["qa_result"] = QAResultPayload(answer=answer, citations=citations, trace_id=state.get("trace_id"))
    state["text_reply"] = answer
    return state
