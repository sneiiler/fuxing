"""Formatting utilities:
- Convert EditResultPayload into different presentation forms (diff/comment/inline)
- Apply suggestions to source text (best-effort)
"""

from __future__ import annotations
from typing import List
import difflib
from ..models.tasks import EditResultPayload, EditSuggestion


def to_unified_diff(
    before: str, after: str, fromfile: str = "before", tofile: str = "after"
) -> str:
    return "\n".join(
        difflib.unified_diff(
            before.splitlines(), after.splitlines(), fromfile, tofile, lineterm=""
        )
    )


def apply_suggestions(text: str, suggestions: List[EditSuggestion]) -> str:
    # naive implementation: apply replace/insert/delete; assumes ranges map to character offsets globally
    # In production, map SelectionRange to document model (paragraph-based) before applying.
    out = text
    # Note: for deterministic behavior, you may need to sort by range positions desc.
    for s in suggestions:
        if s.type == "replace" and s.before is not None and s.after is not None:
            out = out.replace(s.before, s.after, 1)
        elif s.type == "insert" and s.after is not None:
            out += s.after
        elif s.type == "delete" and s.before is not None:
            out = out.replace(s.before, "", 1)
        # comment type does not change text
    return out
