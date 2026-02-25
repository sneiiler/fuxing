# Expose key models for convenient imports
from .enums import Mode, OutputFormat, FinishReason, Strictness, Tone
from .common import SelectionRange, DocRef, Pagination, SSEMetadata
from .chat import (
    ChatMessage, ChatRole, ToolDefinition, ToolFunction, ToolChoice,
    ChatCompletionsRequest, ChatCompletionsResponse, Choice, Usage,
    StreamChunk, DeltaMessage, ToolCallDelta
)
from .tools import (
    ProofreadArgs, PolishArgs, ContinueWritingArgs, FileQAArgs, SetModeArgs
)
from .tasks import (
    EditSuggestion, EditSuggestionType, EditResultSummary, EditResultPayload
)
from .qa import QACitation, QAResultPayload
from .documents import (
    UploadInitResponse, DocumentMeta, IndexResponse,
    ContinueRequest, ContinueResponse
)
from .files import FileObject, FilePurpose, FileUploadInit, FileListResponse
from .embedding import EmbeddingsRequest, EmbeddingsResponse, EmbeddingObject
from .sessions import SessionCreateRequest, SessionObject
from .errors import APIError, APIErrorEnvelope
