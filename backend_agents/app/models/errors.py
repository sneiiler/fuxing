from pydantic import BaseModel
from typing import Optional

class APIError(BaseModel):
    message: str
    type: str = "invalid_request_error"
    code: Optional[str] = None
    param: Optional[str] = None

class APIErrorEnvelope(BaseModel):
    error: APIError
