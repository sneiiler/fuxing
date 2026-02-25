from fastapi import FastAPI, Request, Response
from fastapi.middleware.cors import CORSMiddleware
from app.api.v1.health import router as health_router
from app.api.v1.chat import router as chat_router
from app.api.v1.files import router as files_router
from app.core.config import settings

app = FastAPI(title=settings.app_name)

# CORS (adjust as needed)
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Routes
app.include_router(health_router, prefix="/v1")
app.include_router(chat_router, prefix="/v1")
app.include_router(files_router, prefix="/v1")

@app.middleware("http")
async def contract_header_mw(request: Request, call_next):
    response: Response = await call_next(request)
    response.headers["X-Contract-Version"] = settings.contract_version
    return response
