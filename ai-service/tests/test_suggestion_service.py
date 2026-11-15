import asyncio
import json

import pytest

from app.models.example import Example
from app.models.schema_context import SuggestRequest
from app.prompting.prompt_builder import Prompt
from app.services.suggestion_service import SuggestionError, SuggestionService, extract_json
from tests.fixtures import CONTEXT

ANSWER = {
    "name": "קטינים",
    "rootEntity": "employee",
    "conditions": [{"field": "age", "operator": "lt", "value": 18}],
    "explanation": "גיל קטן מ-18",
}
REQUEST = SuggestRequest.model_validate({"text": "עובדים מתחת לגיל 18", "context": CONTEXT})


class FakeProvider:
    """Answers with a fixed raw text - or fails, or is slow - like a real model can."""

    name = "fake"

    def __init__(self, answer: str = "", error: Exception | None = None, delay: float = 0) -> None:
        self.answer, self.error, self.delay = answer, error, delay
        self.prompts: list[Prompt] = []

    def examples(self) -> list[Example]:
        return []

    async def generate(self, prompt: Prompt) -> str:
        self.prompts.append(prompt)
        await asyncio.sleep(self.delay)
        if self.error:
            raise self.error
        return self.answer


def suggest(provider: FakeProvider, timeout: float = 5):
    return asyncio.run(SuggestionService(provider, timeout).suggest(REQUEST))


def test_sends_the_built_prompt_and_returns_the_validated_suggestion() -> None:
    provider = FakeProvider(json.dumps(ANSWER, ensure_ascii=False))

    suggestion = suggest(provider)

    assert suggestion.conditions[0].field == "age"
    assert provider.prompts[0].user.endswith("<request>\nעובדים מתחת לגיל 18\n</request>")


def test_accepts_json_wrapped_in_a_fence_or_a_sentence() -> None:
    wrapped = f"Here is the control:\n```json\n{json.dumps(ANSWER, ensure_ascii=False)}\n```\nHope it helps."

    assert suggest(FakeProvider(wrapped)).name == "קטינים"


@pytest.mark.parametrize(
    "answer",
    [
        "I cannot help with that.",  # not JSON
        '{"name": "x", "rootEntity": "employee", "conditions": [',  # cut off
        json.dumps({**ANSWER, "conditions": [{"field": "age", "operator": "lessThan", "value": 18}]}),
        json.dumps({**ANSWER, "sql": "SELECT * FROM Employees"}),
    ],
)
def test_an_answer_outside_the_contract_is_a_typed_error(answer: str) -> None:
    with pytest.raises(SuggestionError) as error:
        suggest(FakeProvider(answer))

    assert error.value.code == "invalidResponse"
    assert error.value.details  # what exactly was wrong, for the log / the developer


def test_the_reason_points_at_the_offending_part_of_the_answer() -> None:
    answer = json.dumps({**ANSWER, "conditions": [{"field": "age", "operator": "lessThan", "value": 18}]})

    with pytest.raises(SuggestionError) as error:
        suggest(FakeProvider(answer))

    assert any(d.startswith("conditions.0.operator:") for d in error.value.details)


def test_a_failing_model_is_reported_as_unavailable() -> None:
    with pytest.raises(SuggestionError) as error:
        suggest(FakeProvider(error=ConnectionError("model endpoint down")))

    assert error.value.code == "providerUnavailable"
    assert "אינו זמין" in error.value.message


def test_a_model_that_does_not_answer_in_time_is_reported_as_unavailable() -> None:
    with pytest.raises(SuggestionError) as error:
        suggest(FakeProvider(json.dumps(ANSWER), delay=1), timeout=0.05)

    assert error.value.code == "providerUnavailable"


def test_extract_json_keeps_the_outermost_object() -> None:
    assert extract_json('text {"a": {"b": 1}} text') == '{"a": {"b": 1}}'
    assert extract_json("no json") == "no json"
