import json

import pytest
from pydantic import ValidationError

from app.models.export_schema import SCHEMA_PATH, render
from app.models.schema_context import SuggestRequest
from app.models.suggestion import ControlSuggestion
from tests.fixtures import CONTEXT

# The example of PLAN §7: "A and (B or C)", a value, a field-to-field comparison, a field through a relation.
EXAMPLE = {
    "name": "עובדים פעילים עם נתוני חובה חסרים",
    "description": "עובדים פעילים שחסר להם דואר אלקטרוני או טלפון",
    "rootEntity": "employee",
    "logic": "and",
    "conditions": [
        {"field": "isActive", "operator": "eq", "value": True},
        {"field": "terminationDate", "operator": "lt", "compareToField": "hireDate"},
        {"field": "salary", "operator": "between", "value": [5000, 9000.5]},
        {"field": "department.isActive", "operator": "eq", "value": False},
    ],
    "groups": [
        {
            "logic": "or",
            "conditions": [
                {"field": "email", "operator": "isEmpty"},
                {"field": "phone", "operator": "isEmpty"},
            ],
        },
    ],
    "explanation": "הבנתי: עובדים פעילים שחסר להם אחד מנתוני הקשר.",
    "assumptions": ["'פעילים' = עובד פעיל: כן"],
    "unmappedTerms": [],
}


def test_reads_an_ai_answer_in_the_documented_format() -> None:
    suggestion = ControlSuggestion.model_validate_json(json.dumps(EXAMPLE))

    assert suggestion.root_entity == "employee"
    assert suggestion.understood
    assert suggestion.conditions[0].value is True  # a boolean, not 1
    assert suggestion.conditions[1].compare_to_field == "hireDate"
    assert suggestion.conditions[2].value == [5000, 9000.5]
    assert suggestion.groups[0].logic == "or"
    assert suggestion.model_dump(by_alias=True, exclude_none=True)["rootEntity"] == "employee"


def test_a_request_it_could_not_map_is_a_valid_answer_without_conditions() -> None:
    suggestion = ControlSuggestion.model_validate(
        {"explanation": "לא הצלחתי להבין את הבקשה.", "unmappedTerms": ["מזג אוויר"]}
    )

    assert not suggestion.understood
    assert suggestion.root_entity is None
    assert suggestion.unmapped_terms == ["מזג אוויר"]


@pytest.mark.parametrize(
    ("change", "reason"),
    [
        ({"conditions": [{"field": "age", "operator": "lessThan", "value": 18}]}, "unknown operator"),
        (
            {"conditions": [{"field": "age", "operator": "lt", "value": 18, "compareToField": "x"}]},
            "two operands",
        ),
        ({"conditions": [{"field": "", "operator": "isEmpty"}]}, "empty field"),
        ({"groups": [{"logic": "or", "conditions": []}]}, "empty group"),
        ({"logic": "xor"}, "unknown logic"),
        ({"sql": "DELETE FROM x"}, "invented property"),
        ({"rootEntity": None}, "conditions without the records they check"),
    ],
)
def test_rejects_answers_that_do_not_fit_the_contract(change: dict, reason: str) -> None:
    with pytest.raises(ValidationError):
        ControlSuggestion.model_validate({**EXAMPLE, **change})


def test_request_carries_the_text_and_the_business_schema_context() -> None:
    request = SuggestRequest.model_validate({"text": "  מצא עובדים מתחת לגיל 18  ", "context": CONTEXT})

    assert request.text == "מצא עובדים מתחת לגיל 18"
    employee = request.context.entities[0]
    assert employee.display_name_plural == "עובדים"
    assert employee.fields[2].unit == "₪"
    assert employee.relations[0].target_entity == "department"
    assert request.context.operators[3].shape == "range"
    assert request.context.max_relation_hops == 2


@pytest.mark.parametrize(
    "request_json",
    [
        {"text": "   ", "context": CONTEXT},
        {"text": "x" * 1001, "context": CONTEXT},
        {"text": "עובדים", "context": {**CONTEXT, "entities": []}},
        {"text": "עובדים", "context": {**CONTEXT, "rows": [{"salary": 1}]}},  # no data rows in the context
        {"text": "עובדים", "context": {k: v for k, v in CONTEXT.items() if k != "maxRelationHops"}},
    ],
)
def test_rejects_invalid_requests(request_json: dict) -> None:
    with pytest.raises(ValidationError):
        SuggestRequest.model_validate(request_json)


def test_published_json_schema_matches_the_model() -> None:
    assert SCHEMA_PATH.read_text(encoding="utf-8") == render(), (
        "docs/ai-response.schema.json is out of date - run: python -m app.models.export_schema"
    )


def test_published_json_schema_documents_the_contract() -> None:
    schema = json.loads(SCHEMA_PATH.read_text(encoding="utf-8"))

    assert set(schema["properties"]) == {
        "name",
        "description",
        "rootEntity",
        "logic",
        "conditions",
        "groups",
        "explanation",
        "assumptions",
        "unmappedTerms",
    }
    condition = schema["$defs"]["SuggestedCondition"]
    assert condition["required"] == ["field", "operator"]
    assert condition["additionalProperties"] is False
    assert "compareToField" in condition["properties"]
