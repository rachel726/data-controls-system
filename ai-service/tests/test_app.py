from fastapi.testclient import TestClient

from app.config import Settings
from app.main import app


def test_app_starts_and_serves_openapi() -> None:
    response = TestClient(app).get("/openapi.json")

    assert response.status_code == 200
    assert response.json()["info"]["title"] == "Data Controls AI Service"


def test_default_provider_is_mock() -> None:
    assert Settings().ai_provider == "mock"
