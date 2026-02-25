from enum import Enum

class Mode(str, Enum):
    edit = "edit"
    review = "review"
    chat = "chat"

class OutputFormat(str, Enum):
    diff = "diff"
    comment = "comment"
    inline = "inline"

class FinishReason(str, Enum):
    stop = "stop"
    length = "length"
    tool_calls = "tool_calls"
    error = "error"

class Strictness(str, Enum):
    low = "low"
    medium = "medium"
    high = "high"

class Tone(str, Enum):
    academic = "academic"
    business = "business"
    plain = "plain"
