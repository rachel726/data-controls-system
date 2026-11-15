import ast
import asyncio
import json
from pathlib import Path
from types import ModuleType

import pytest

from app.models.schema_context import SchemaContext, SuggestRequest
from app.models.suggestion import ControlSuggestion, SuggestedCondition, SuggestedGroup
from app.prompting.prompt_builder import PromptBuilder
from app.providers import hebrew, pattern_matcher, templates
from app.providers.mock import MockLlmProvider
from app.providers.pattern_matcher import recognize

# The schema context .NET sends for the seeded catalog (employees, departments, projects; one relation).
SEEDED = json.loads((Path(__file__).parent / "seeded_context.json").read_text(encoding="utf-8"))
CONTEXT = SchemaContext.model_validate(SEEDED)


def test_the_seeded_context_is_intact_hebrew() -> None:
    text = json.dumps(SEEDED, ensure_ascii=False)

    assert "�" not in text  # a wrongly encoded copy passes every other test silently
    assert [f.true_text for e in CONTEXT.entities for f in e.fields if f.true_text] == ["פעיל", "פעילה"]


def conditions(text: str) -> tuple[str, str, list[tuple[str, str, object]]]:
    suggestion, unknown = recognize(text, CONTEXT)
    assert suggestion is not None, f"not recognized: {unknown}"
    return (
        suggestion.root_entity or "",
        suggestion.logic,
        [(c.field, c.operator, c.value) for c in suggestion.conditions],
    )


@pytest.mark.parametrize(
    ("text", "expected"),
    [
        # the value follows the request - the point of the recognizer
        ("תביא לי עובדים שהגיל שלהם מתחת ל-30", ("employee", "and", [("age", "lt", 30)])),
        ("תביא לי עובדים שהגיל שלהם מתחת ל-20", ("employee", "and", [("age", "lt", 20)])),
        ('עובדים שהשכר החודשי שלהם גבוה מ-15 אלף ש"ח', ("employee", "and", [("salary", "gt", 15000)])),
        ("עובדים ששכר חודשי מעל 20,000", ("employee", "and", [("salary", "gt", 20000)])),
        ("עובדים שגילם בין 20 ל-30", ("employee", "and", [("age", "between", [20, 30])])),
        # yes / no fields, from the catalog's wording, negated too
        ("עובדים לא פעילים", ("employee", "and", [("isActive", "eq", False)])),
        ("מחלקות לא פעילות", ("department", "and", [("isActive", "eq", False)])),
        (
            "עובדים פעילים במחלקה שאינה פעילה",
            ("employee", "and", [("isActive", "eq", True), ("department.isActive", "eq", False)]),
        ),
        # several conditions, joined by "and" or by "or"
        (
            "עובדים שגילם מתחת ל-30 ושכרם מעל 10000",
            ("employee", "and", [("age", "lt", 30), ("salary", "gt", 10000)]),
        ),
        (
            "עובדים ללא טלפון או ללא דואר אלקטרוני",
            ("employee", "or", [("phone", "isEmpty", None), ("email", "isEmpty", None)]),
        ),
        # a related record's field, dates, list values, another entity
        ("עובדים ששכר חודשי של מנהל ישיר מעל 30000", ("employee", "and", [("manager.salary", "gt", 30000)])),
        (
            "עובדים שתאריך תחילת העסקה לפני 01/01/2020",
            ("employee", "and", [("hireDate", "lt", "2020-01-01")]),
        ),
        ("פרויקטים בסטטוס מתוכנן", ("project", "and", [("status", "eq", "Planned")])),
        ("פרויקטים שהסטטוס שלהם שווה לפעיל", ("project", "and", [("status", "eq", "Active")])),
        ("פרויקטים שתקציב מעל 100,000", ("project", "and", [("budget", "gt", 100000)])),
    ],
)
def test_recognizes_a_simple_request_from_the_catalog(text: str, expected: tuple) -> None:
    assert conditions(text) == expected


@pytest.mark.parametrize(
    ("text", "unknown"),
    [
        ("עובדים עם מספר נעליים מעל 40", ["מספר", "נעליים", "מעל", "40"]),  # no such field
        ("מה מזג האוויר", ["מה", "מזג", "האוויר"]),  # no records named
        ("עובדים שגילם מתחת לממוצע", ["מתחת ל", "לממוצע"]),  # no value
        ("עובדים ששמם מתחיל ב-א", ["ששמם", "מתחיל", "ב", "א"]),  # "שם" names several fields
    ],
)
def test_answers_only_when_it_accounts_for_every_word(text: str, unknown: list[str]) -> None:
    suggestion, words = recognize(text, CONTEXT)

    assert suggestion is None
    assert words == unknown  # the user's own words, to show what was not understood


def test_says_how_it_read_the_everyday_words() -> None:
    suggestion, _ = recognize("עובדים שגילם מתחת ל-30 ושכרם מעל 10000", CONTEXT)

    assert suggestion is not None
    assert suggestion.assumptions == ["'מתחת ל' = קטן מ", "'ושכרם' = שכר חודשי", "'מעל' = גדול מ"]
    assert suggestion.name == "עובדים שגילם מתחת ל-30 ושכרם מעל 10000"


@pytest.mark.parametrize("module", [pattern_matcher, hebrew, templates])
def test_names_come_only_from_the_context(module: ModuleType) -> None:
    """Generic: the mock's code (docstrings and comments aside) holds no entity, field or relation of the
    catalog - a renamed field or a new entity is recognized without a code change."""
    tree = ast.parse(Path(module.__file__ or "").read_text(encoding="utf-8"))
    docstrings = {
        id(node.body[0].value)
        for node in ast.walk(tree)
        if isinstance(node, ast.Module | ast.FunctionDef | ast.ClassDef)
        and node.body
        and isinstance(node.body[0], ast.Expr)
        and isinstance(node.body[0].value, ast.Constant)
    }
    strings = {
        n.value
        for n in ast.walk(tree)
        if isinstance(n, ast.Constant) and isinstance(n.value, str) and id(n) not in docstrings
    }
    catalog = {
        name
        for entity in CONTEXT.entities
        for name in [entity.key, entity.display_name, entity.display_name_plural]
        + [n for f in entity.fields for n in (f.key, f.display_name, f.true_text, f.false_text) if n]
        + [n for r in entity.relations for n in (r.key, r.display_name)]
    }

    # the answer contract's own property names ("name", "conditions", ...) are not business names
    contract = {
        f.alias or name
        for model in (ControlSuggestion, SuggestedCondition, SuggestedGroup)
        for name, f in model.model_fields.items()
    }

    assert not (strings - contract) & catalog


# ---------- through the mock, as the API reaches it ----------


def ask(text: str) -> ControlSuggestion:
    prompt = PromptBuilder().build(SuggestRequest.model_validate({"text": text, "context": SEEDED}))
    return ControlSuggestion.model_validate_json(asyncio.run(MockLlmProvider().generate(prompt)))


def test_the_mock_answers_a_changed_value_with_a_changed_control() -> None:
    under_30, under_20 = ask("עובדים שהגיל שלהם מתחת ל-30"), ask("עובדים שהגיל שלהם מתחת ל-20")

    assert (under_30.conditions[0].value, under_20.conditions[0].value) == (30, 20)


def test_the_mock_still_answers_an_assignment_example_with_its_prepared_answer() -> None:
    prepared = ask('מצא עובדים פעילים שגילם נמוך מ־18 ומשכורתם גבוהה מ־15,000 ש"ח.')

    assert len(prepared.conditions) == 3 and prepared.name == "עובדים פעילים מתחת לגיל 18 עם שכר גבוה"


def test_the_mock_lists_the_words_it_could_not_place() -> None:
    answer = ask("עובדים עם מספר נעליים מעל 40")

    assert not answer.understood
    assert answer.unmapped_terms == ["מספר", "נעליים", "מעל", "40"]
