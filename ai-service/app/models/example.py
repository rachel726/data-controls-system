from app.models.base import ContractModel
from app.models.schema_context import Text


class Example(ContractModel):
    """A ready-made request the UI offers as a one-click example: its short label and the text sent."""

    label: Text
    text: Text
