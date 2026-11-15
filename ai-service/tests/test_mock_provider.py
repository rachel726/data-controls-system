import asyncio

import pytest

from app.config import Settings
from app.models.schema_context import SuggestRequest
from app.models.suggestion import ControlSuggestion
from app.prompting.prompt_builder import PromptBuilder
from app.providers.factory import create_provider
from app.providers.mock import MockLlmProvider, normalize
from tests.fixtures import CONTEXT

# Verbatim from the assignment (with its Hebrew maqaf "־" and gershayim-like quotes).
STAGE_B_EXAMPLE = 'מצא עובדים פעילים שגילם נמוך מ־18 ומשכורתם גבוהה מ־15,000 ש"ח.'
ASSIGNMENT_EXAMPLES = [
    'איתור עובדים שגילם נמוך מ־18 ושכרם גבוה מ־15,000 ש"ח.',
    "איתור עובדים שתאריך סיום העסקתם מוקדם מתאריך תחילת העסקתם.",
    "איתור עובדים פעילים המשויכים למחלקה שאינה פעילה.",
    "איתור רשומות שבהן חסרים נתונים המחויבים לפי כללים עסקיים.",
]


def ask(text: str) -> ControlSuggestion:
    """Through the real prompt, as a model receives it."""
    prompt = PromptBuilder().build(SuggestRequest.model_validate({"text": text, "context": CONTEXT}))
    return ControlSuggestion.model_validate_json(asyncio.run(MockLlmProvider().generate(prompt)))


@pytest.mark.parametrize("text", [STAGE_B_EXAMPLE, *ASSIGNMENT_EXAMPLES])
def test_answers_every_assignment_example_within_the_contract(text: str) -> None:
    suggestion = ask(text)

    assert suggestion.understood
    assert suggestion.root_entity == "employee"
    assert suggestion.name and suggestion.explanation


def test_the_stage_b_example_is_mapped_to_its_three_conditions() -> None:
    suggestion = ask(STAGE_B_EXAMPLE)

    assert [(c.field, c.operator, c.value) for c in suggestion.conditions] == [
        ("isActive", "eq", True),
        ("age", "lt", 18),
        ("salary", "gt", 15000),
    ]
    assert suggestion.assumptions  # e.g. how "active" was read


def test_examples_cover_a_field_comparison_a_relation_path_and_an_or_group() -> None:
    termination, department, missing = (ask(t) for t in ASSIGNMENT_EXAMPLES[1:])

    assert termination.conditions[0].compare_to_field == "hireDate"
    assert department.conditions[1].field == "department.isActive"
    assert missing.groups[0].logic == "or"
    assert [c.field for c in missing.groups[0].conditions] == ["email", "phone", "department.name"]


@pytest.mark.parametrize(
    "typed",
    [
        'מצא עובדים פעילים שגילם נמוך מ-18 ומשכורתם גבוהה מ-15,000 ש"ח',  # plain hyphen, no full stop
        "  מצא עובדים פעילים  שגילם נמוך מ־18 ומשכורתם גבוהה מ־15,000 ש״ח  ",  # spaces, gershayim
    ],
)
def test_the_same_request_typed_differently_still_matches(typed: str) -> None:
    assert normalize(typed) == normalize(STAGE_B_EXAMPLE)
    assert ask(typed).conditions == ask(STAGE_B_EXAMPLE).conditions


def test_the_error_demo_refers_to_a_field_that_does_not_exist() -> None:
    suggestion = ask("דמה שגיאה")

    assert suggestion.conditions[0].field == "bonus"  # a valid shape - the catalog check (in .NET) rejects it


@pytest.mark.parametrize("text", ["מה מזג האוויר מחר?", "בקשה ארוכה שאינה מוכרת " * 40])
def test_an_unknown_request_is_answered_as_not_understood(text: str) -> None:
    # Regression: the whole request went into unmappedTerms - over 200 characters it broke the contract.
    # Now each word it could not place is listed, at most ten, each a single word.
    suggestion = ask(text)

    assert not suggestion.understood
    assert 0 < len(suggestion.unmapped_terms) <= 10
    assert all(len(term) <= 200 and " " not in term for term in suggestion.unmapped_terms)
    assert "לא הצלחתי להבין" in suggestion.explanation


def test_the_configured_provider_is_the_mock() -> None:
    assert isinstance(create_provider(Settings()), MockLlmProvider)


def test_offers_its_labelled_fixtures_as_examples_each_answered() -> None:
    examples = MockLlmProvider().examples()

    assert examples[0].label == "עובדים פעילים מתחת לגיל 18 עם שכר גבוה"
    assert examples[0].text == STAGE_B_EXAMPLE
    assert len(examples) == 5
    # an example button can never answer "not understood"
    assert all(ask(e.text).conditions or ask(e.text).groups for e in examples)
