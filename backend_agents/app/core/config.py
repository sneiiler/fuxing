from pydantic import BaseModel, Field
from pydantic_settings import BaseSettings, SettingsConfigDict

class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file='.env', env_file_encoding='utf-8', extra='ignore')
    app_name: str = "office-agent-backend"
    env: str = "dev"
    log_level: str = "info"
    port: int = 8000
    contract_version: str = "v1.0.0"

    postgres_dsn: str | None = None
    redis_url: str | None = None


    # 嵌入服务配置 (Embedding Service Configuration)
    embedding_api_key: str | None = Field(None, alias="EMBEDDING_API_KEY")
    embedding_base_url: str | None = Field(None, alias="EMBEDDING_BASE_URL")
    embedding_model_name: str = Field("text-embedding-3-small", alias="EMBEDDING_MODEL_NAME")
    embedding_dimension: int = Field(1024, alias="EMBEDDING_DIMENSION")
    embedding_batch_size: int = Field(10, alias="EMBEDDING_BATCH_SIZE")

settings = Settings()
