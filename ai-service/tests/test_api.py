from collections.abc import Iterator

import pytest
from fastapi.testclient import TestClient

from app.api.routes import get_suggestion_service
from app.main import app
from app.services.suggestion_service import SuggestionService
from tests.fixtures import CONTEXT
from tests.test_suggestion_service import FakeProvider

client = TestClient(app)


@pytest.fixture
def broken_model() -> Iterator[None]:
    app.dependency_overrides[get_suggestion_service] = lambda: SuggestionService(FakeProvider("not json"), 5)
    yield
    app.dependency_overrides.clear()


def test_suggests_a_control_in_camel_case_json() -> None:
    response = client.post(
        "/v1/suggest-control",
        json={"text": 'מצא עובדים פעילים שגילם נמוך מ־18 ומשכורתם גבוהה מ־15,000 ש"ח.', "context": CONTEXT},
    )

    assert response.status_code == 200
    body = response.json()
    assert body["rootEntity"] == "employee"
    assert body["conditions"][1] == {"field": "age", "operator": "lt", "value": 18}
    assert body["unmappedTerms"] == []


def test_an_unknown_request_is_a_normal_answer_without_conditions() -> None:
    response = client.post("/v1/suggest-control", json={"text": "שלום", "context": CONTEXT})

    assert response.status_code == 200
    assert response.json()["conditions"] == []
    assert "rootEntity" not in response.json()


def test_a_broken_model_answer_is_a_502_problem(broken_model: None) -> None:
    response = client.post("/v1/suggest-control", json={"text": "עובדים", "context": CONTEXT})

    assert response.status_code == 502
    assert response.headers["content-type"].startswith("application/problem+json")
    problem = response.json()
    assert problem["code"] == "invalidResponse"
    assert problem["detail"] == "תשובת ה-AI אינה במבנה הצפוי ולכן לא ניתן להשתמש בה."


def test_an_invalid_request_is_rejected_before_any_model_call() -> None:
    response = client.post("/v1/suggest-control", json={"text": "   ", "context": CONTEXT})

    assert response.status_code == 422


def test_serves_the_examples_in_camel_case_json() -> None:
    response = client.get("/v1/examples")

    assert response.status_code == 200
    first = response.json()[0]
    assert set(first) == {"label", "text"}
    assert first["label"] == "עובדים פעילים מתחת לגיל 18 עם שכר גבוה"


def test_health() -> None:
    assert client.get("/health").json() == {"status": "ok"}
