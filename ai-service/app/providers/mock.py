"""A stand-in for a real model (Stage B requires no real AI call). It reads the request from the prompt, as a
model would, and answers in three steps:
1. the assignment's examples get prepared JSON (the full Schema: several conditions, a relation, an OR
   group, assumptions) - also with other numbers or comparisons ("נמוך מ־19", see templates);
2. a simple request is recognized from the catalog it is given ("עובדים שהגיל שלהם מתחת ל-30" -> age lt 30,
   see pattern_matcher);
3. anything else gets the answer a model gives when it cannot map a request: no conditions, why, and the
   words it could not place."""

import json
import re
import unicodedata
from pathlib import Path

from app.models.example import Example
from app.prompting.prompt_builder import REQUEST_TAG, Prompt
from app.providers.pattern_matcher import recognize
from app.providers.templates import Template, adapt, template

FIXTURES_DIR = Path(__file__).parent / "mock_fixtures"

_REQUEST = re.compile(rf"<{REQUEST_TAG}>\n(.*)\n</{REQUEST_TAG}>\s*$", re.DOTALL)
_DASHES = re.compile("[־‐-―]")  # Hebrew maqaf and the dash family -> "-"
_QUOTES = re.compile("[״“”„]")  # Hebrew gershayim and typographic double quotes -> '"'
_PUNCTUATION = re.compile(r"[.,;:!?]")


def normalize(text: str) -> str:
    """The same request typed slightly differently still matches: dashes, quotes, punctuation, spacing."""
    text = unicodedata.normalize("NFKC", text)
    text = _DASHES.sub("-", text)
    text = _QUOTES.sub('"', text)
    text = _PUNCTUATION.sub(" ", text)
    return " ".join(text.split())


class MockLlmProvider:
    name = "mock"

    def __init__(self, fixtures_dir: Path = FIXTURES_DIR) -> None:
        self._answers: dict[str, str] = {}
        self._examples: list[Example] = []
        self._templates: list[Template] = []
        for file in sorted(fixtures_dir.glob("*.json")):
            fixture = json.loads(file.read_text(encoding="utf-8"))
            answer = json.dumps(fixture["response"], ensure_ascii=False)
            for request in fixture["requests"]:
                self._answers[normalize(request)] = answer
                if (example := template(request, fixture["response"])) is not None:
                    self._templates.append(example)
            # a fixture with an "example" label is offered in the UI, sending the first request it answers
            if "example" in fixture:
                self._examples.append(Example(label=fixture["example"], text=fixture["requests"][0]))

    def examples(self) -> list[Example]:
        return list(self._examples)

    async def generate(self, prompt: Prompt) -> str:
        match = _REQUEST.search(prompt.user)
        if not match:
            raise ValueError("The prompt has no request.")
        request = match.group(1)
        if (prepared := self._answers.get(normalize(request))) is not None:
            return prepared

        for example in self._templates:
            if (adapted := adapt(example, request, prompt.context)) is not None:
                return adapted.model_dump_json(by_alias=True, exclude_none=True)

        suggestion, unknown = recognize(request, prompt.context)
        if suggestion is not None:
            return suggestion.model_dump_json(by_alias=True, exclude_none=True)
        return json.dumps(
            {
                "explanation": _NOT_UNDERSTOOD,
                "unmappedTerms": [term[:200] for term in dict.fromkeys(unknown)][:10],
            },
            ensure_ascii=False,
        )


_NOT_UNDERSTOOD = (
    "לא הצלחתי להבין את הבקשה. זוהי הדגמה (Mock): היא מכירה את הדוגמאות של המטלה ומזהה בקשות פשוטות לפי שמות "
    "השדות והפעולות שבמבנה הנתונים. מודל AI אמיתי יקבל את אותו מבנה נתונים ויבין גם בקשות אחרות."
)
