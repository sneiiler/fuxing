from fastapi import HTTPException, status
from ..models.errors import APIError, APIErrorEnvelope

def http_error(message: str, code: str | None = None, status_code: int = status.HTTP_400_BAD_REQUEST):
    err = APIError(message=message, code=code)
    raise HTTPException(status_code=status_code, detail=APIErrorEnvelope(error=err).model_dump())
