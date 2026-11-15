from app.config import Settings
from app.providers.base import LlmProvider
from app.providers.mock import MockLlmProvider


def create_provider(settings: Settings) -> LlmProvider:
    """The configured model. Adding a real one = a new LlmProvider class and a new AI_PROVIDER value here."""
    match settings.ai_provider:
        case "mock":
            return MockLlmProvider()
