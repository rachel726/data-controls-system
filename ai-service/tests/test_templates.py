import json

import pytest

from app.models.schema_context import SchemaContext
from app.providers.hebrew import same_word
from app.providers.mock import FIXTURES_DIR
from app.providers.templates import adapt, template
from tests.test_pattern_matcher import SEEDED, ask

CONTEXT = SchemaContext.model_validate(SEEDED)
STAGE_B = json.loads((FIXTURES_DIR / "01-active-minors-high-salary.json").read_text(encoding="utf-8"))
EXAMPLE = STAGE_B["requests"][0]  # 'מצא עובדים פעילים שגילם נמוך מ־18 ומשכורתם גבוהה מ־15,000 ש"ח.'


def conditions(text: str) -> list[tuple[str, str, object]]:
    return [(c.field, c.operator, c.value) for c in ask(text).conditions]


@pytest.mark.parametrize(
    ("text", "expected"),
    [
        # other numbers
        (
            'מצא עובדים פעילים שגילם נמוך מ־19 ומשכורתם גבוהה מ־15,000 ש"ח',
            [("age", "lt", 19), ("salary", "gt", 15000)],
        ),
        (
            'מצא עובדים פעילים שגילם נמוך מ־19 ומשכורתם גבוהה מ־20,000 ש"ח.',
            [("age", "lt", 19), ("salary", "gt", 20000)],
        ),
        # other comparisons, in other words
        (
            'מצא עובדים פעילים שגילם גבוה מ־60 ומשכורתם נמוכה מ־5,000 ש"ח',
            [("age", "gt", 60), ("salary", "lt", 5000)],
        ),
        (
            "מצא עובדים פעילים שגילם מתחת ל־21 ומשכורתם מעל 12 אלף",
            [("age", "lt", 21), ("salary", "gt", 12000)],
        ),
        (
            "מצא עובדים פעילים שגילם לפחות 65 ומשכורתם לכל היותר 4000",
            [("age", "gte", 65), ("salary", "lte", 4000)],
        ),
    ],
)
def test_an_example_with_other_values_keeps_its_prepared_answer(text: str, expected: list) -> None:
    assert conditions(text) == [("isActive", "eq", True), *expected]  # "פעילים" stays the prepared condition


def test_a_changed_example_is_named_and_explained_by_the_request() -> None:
    answer = ask('מצא עובדים פעילים שגילם גבוה מ־60 ומשכורתם נמוכה מ־5,000 ש"ח')

    assert answer.name == 'עובדים פעילים שגילם גבוה מ־60 ומשכורתם נמוכה מ־5,000 ש"ח'  # not "מתחת לגיל 18"
    assert "התאמתי את דוגמת המטלה" in answer.explanation
    assert answer.assumptions[:2] == STAGE_B["response"]["assumptions"]  # what the words mean still holds
    assert answer.assumptions[2:] == ["'גבוה מ' = גדול מ", "'נמוכה מ' = קטן מ"]


def test_the_example_itself_keeps_its_prepared_name() -> None:
    assert ask(EXAMPLE).name == STAGE_B["response"]["name"]


@pytest.mark.parametrize(
    "text",
    [
        'מצא עובדים לא פעילים שגילם נמוך מ־19 ומשכורתם גבוהה מ־15,000 ש"ח',  # a word added
        "מצא עובדים פעילים שגילם נמוך מ־19 ומשכורתם גבוהה",  # a value missing
        'מצא עובדים פעילים שגילם נמוך מ־19 ומשכורתם גבוהה מ־15,000 ש"ח בדיוק',  # a word more
    ],
)
def test_only_numbers_dates_and_comparisons_may_change(text: str) -> None:
    example = template(EXAMPLE, STAGE_B["response"])

    assert example is not None and adapt(example, text, CONTEXT) is None


def test_an_example_without_values_is_not_a_template() -> None:
    fixture = json.loads((FIXTURES_DIR / "04-active-in-inactive-department.json").read_text(encoding="utf-8"))

    assert template(fixture["requests"][0], fixture["response"]) is None  # matched exactly, as before


def test_a_final_letter_is_the_same_letter() -> None:
    assert same_word("נמוכה", "נמוך") and same_word("גבוהים", "גבוה")
