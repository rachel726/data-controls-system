from functools import lru_cache
from typing import Literal

from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    """Service settings, read from environment variables (or a local .env file)."""

    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    ai_provider: Literal["mock"] = "mock"
    """Which LLM provider implementation to use. Only the mock exists (Stage B requires no real AI call)."""

    ai_timeout_seconds: float = 20
    """How long to wait for the model before giving up."""


@lru_cache
def get_settings() -> Settings:
    return Settings()
