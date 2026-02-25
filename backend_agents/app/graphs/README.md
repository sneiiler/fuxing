# LangGraph Orchestration

- `router.py` builds a `StateGraph` with nodes: router → (proofread|polish|continue_writing|file_qa) → review.
- `orchestrator.run(state)` executes the compiled graph and returns the final state.

## Minimal usage (inside your API layer)

```python
from app.graphs.router import orchestrator
from app.models.enums import Mode
from app.models.common import DocRef

initial = {
    "mode": Mode.chat,
    "doc": DocRef(doc_id="d1", doc_version_id="v1"),
    "selection_range": None,
    "user_message": "请根据文件回答：这个方法如何工作？",
    "tool_choice": None,
}
final_state = orchestrator.run(initial)
print(final_state["text_reply"])
```
