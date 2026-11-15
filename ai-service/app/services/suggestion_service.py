"""Request -> prompt -> model -> the model's raw text -> a validated ControlSuggestion, or a typed error.
Whatever the model, its answer is never trusted: it must parse and match the contract first."""

import asyncio
import logging
from dataclasses import dataclass, field
from typing import Literal

from pydantic import ValidationError

from app.models.schema_context import SuggestRequest
from app.models.suggestion import ControlSuggestion
from app.prompting.prompt_builder import PromptBuilder
from app.providers.base import LlmProvider

log = logging.getLogger(__name__)

ErrorCode = Literal["providerUnavailable", "invalidResponse"]


@dataclass
class SuggestionError(Exception):
    code: ErrorCode
    message: str
    """User-facing, in Hebrew."""
    details: list[str] = field(default_factory=list)


class SuggestionService:
    def __init__(
        self, provider: LlmProvider, timeout_seconds: float, builder: PromptBuilder | None = None
    ) -> None:
        self._provider = provider
        self._timeout = timeout_seconds
        self._builder = builder or PromptBuilder()

    async def suggest(self, request: SuggestRequest) -> ControlSuggestion:
        prompt = self._builder.build(request)
        try:
            raw = await asyncio.wait_for(self._provider.generate(prompt), self._timeout)
        except Exception as error:
            log.exception("AI provider '%s' failed", self._provider.name)
            raise SuggestionError(
                "providerUnavailable", "שירות ה-AI אינו זמין כרגע. נסו שוב בעוד מספר רגעים."
            ) from error

        try:
            return ControlSuggestion.model_validate_json(extract_json(raw))
        except ValidationError as error:
            log.warning("AI provider '%s' answered outside the contract: %s", self._provider.name, raw[:2000])
            raise SuggestionError(
                "invalidResponse",
                "תשובת ה-AI אינה במבנה הצפוי ולכן לא ניתן להשתמש בה.",
                [f"{'.'.join(str(p) for p in e['loc']) or 'answer'}: {e['msg']}" for e in error.errors()],
            ) from error


def extract_json(raw: str) -> str:
    """Models often wrap the JSON in a ``` fence or a sentence; keep the outermost {...}."""
    start, end = raw.find("{"), raw.rfind("}")
    return raw[start : end + 1] if 0 <= start < end else raw
