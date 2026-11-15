"""Writes the JSON Schema of the expected AI answer to docs/ai-response.schema.json.

Run from ai-service/:  python -m app.models.export_schema
A test fails when the file and the model differ, so the documented contract cannot drift from the code."""

import json
from pathlib import Path

from app.models.suggestion import ControlSuggestion

SCHEMA_PATH = Path(__file__).resolve().parents[3] / "docs" / "ai-response.schema.json"


def response_schema() -> dict:
    schema = ControlSuggestion.model_json_schema(by_alias=True)
    return {
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "$id": "ai-response.schema.json",
        **schema,
    }


def render() -> str:
    return json.dumps(response_schema(), ensure_ascii=False, indent=2) + "\n"


def main() -> None:
    SCHEMA_PATH.parent.mkdir(parents=True, exist_ok=True)
    SCHEMA_PATH.write_text(render(), encoding="utf-8")
    print(f"wrote {SCHEMA_PATH}")


if __name__ == "__main__":
    main()
