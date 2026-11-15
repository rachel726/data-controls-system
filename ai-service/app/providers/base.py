from typing import Protocol

from app.models.example import Example
from app.prompting.prompt_builder import Prompt


class LlmProvider(Protocol):
    """A language model behind one call: a prompt in, the model's raw text out - exactly what a real model
    returns, so the answer is parsed and validated the same way whichever model is used.
    Swapping a model = a new class implementing this + the AI_PROVIDER setting; nothing else changes."""

    name: str

    async def generate(self, prompt: Prompt) -> str: ...

    def examples(self) -> list[Example]:
        """Requests to offer as one-click examples - ones this model is known to handle (may be none)."""
        ...
