"""The answer expected from the AI: a control suggestion in its own simple format, decoupled from the engine's
definition. .NET translates it into a Stage A definition and validates it against the catalog like any manual
control - so this model checks the SHAPE only; whether "age" exists is the engine's validator's job."""

from typing import Annotated, Literal, Self

from pydantic import Field, StringConstraints, model_validator

from app.models.base import ContractModel
from app.models.schema_context import OperatorKey

Logic = Literal["and", "or"]
Scalar = bool | int | float | str
"""A literal value. bool first, so `true` stays a boolean and is not read as 1."""

FieldPath = Annotated[str, StringConstraints(min_length=1, max_length=200)]


class SuggestedCondition(ContractModel):
    """One condition: `{"field": "age", "operator": "lt", "value": 18}`, a range / list as an array,
    or a comparison with another field:
    `{"field": "terminationDate", "operator": "lt", "compareToField": "hireDate"}`.
    Field paths may walk relations: "department.isActive"."""

    field: FieldPath
    operator: OperatorKey
    value: Scalar | list[Scalar] | None = None
    compare_to_field: FieldPath | None = None

    @model_validator(mode="after")
    def _one_operand(self) -> Self:
        if self.value is not None and self.compare_to_field is not None:
            raise ValueError("a condition compares with a value or with another field, not both")
        return self


class SuggestedGroup(ContractModel):
    """Conditions joined by their own logic, e.g. "email empty OR phone empty" inside an AND."""

    logic: Logic
    conditions: list[SuggestedCondition] = Field(min_length=1)


class ControlSuggestion(ContractModel):
    """The AI's proposal. A request it could not map has no conditions,
    and lists the terms it could not map in `unmappedTerms`."""

    name: Annotated[str, StringConstraints(max_length=200)] = ""
    description: Annotated[str, StringConstraints(max_length=1000)] | None = None
    root_entity: Annotated[str, StringConstraints(min_length=1, max_length=50)] | None = None
    logic: Logic = "and"
    conditions: list[SuggestedCondition] = Field(default_factory=list)
    groups: list[SuggestedGroup] = Field(default_factory=list)
    """Optional, one nesting level - enough for "A and (B or C)"."""
    explanation: Annotated[str, StringConstraints(max_length=2000)] = ""
    """How the request was understood, in the user's language."""
    assumptions: list[Annotated[str, StringConstraints(max_length=500)]] = Field(default_factory=list)
    unmapped_terms: list[Annotated[str, StringConstraints(max_length=200)]] = Field(default_factory=list)

    @property
    def understood(self) -> bool:
        return bool(self.conditions or self.groups)

    @model_validator(mode="after")
    def _understood_needs_root(self) -> Self:
        if self.understood and self.root_entity is None:
            raise ValueError("a suggestion with conditions must say which records it checks (rootEntity)")
        return self
