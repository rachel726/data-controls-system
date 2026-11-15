"""What the AI is told about the data: the business catalog only - names, meanings, types, allowed values,
relations and operators. Never table or column names, never data rows. Built by .NET from the same catalog
the engine uses, so a suggestion can only refer to things the engine knows."""

from typing import Annotated, Literal

from pydantic import Field, StringConstraints

from app.models.base import ContractModel

DataType = Literal["text", "integer", "decimal", "date", "boolean", "enum"]
OperatorKey = Literal[
    "eq", "neq", "gt", "gte", "lt", "lte", "between", "in", "contains", "isEmpty", "isNotEmpty"
]
OperandShape = Literal["none", "value", "range", "list"]
Cardinality = Literal["manyToOne", "oneToMany"]

Key = Annotated[str, StringConstraints(min_length=1, max_length=50)]
Text = Annotated[str, StringConstraints(min_length=1, max_length=500)]


class AllowedValue(ContractModel):
    value: Key
    label: Text


class FieldContext(ContractModel):
    """A business attribute, e.g. key "age" = "גיל" (integer)."""

    key: Key
    display_name: Text
    description: Text | None = None
    data_type: DataType
    allowed_values: list[AllowedValue] = Field(default_factory=list)
    unit: str | None = None
    true_text: Text | None = None
    """How a yes/no field is said of its record: "פעיל" ("עובד פעיל"); `false_text` = "אינו פעיל"."""
    false_text: Text | None = None


class RelationContext(ContractModel):
    """A link to another entity, e.g. employee "department" → department (many-to-one)."""

    key: Key
    display_name: Text
    description: Text | None = None
    target_entity: Key
    cardinality: Cardinality


class EntityContext(ContractModel):
    """A business object, e.g. "employee" = "עובד" / "עובדים"."""

    key: Key
    display_name: Text
    display_name_plural: Text
    description: Text | None = None
    fields: list[FieldContext] = Field(min_length=1)
    relations: list[RelationContext] = Field(default_factory=list)


class OperatorContext(ContractModel):
    """A comparison the engine supports, e.g. "lt" = "קטן מ" for integer / decimal / date."""

    key: OperatorKey
    label: Text
    allowed_types: list[DataType] = Field(min_length=1)
    shape: OperandShape
    allows_field_operand: bool


class SchemaContext(ContractModel):
    entities: list[EntityContext] = Field(min_length=1)
    operators: list[OperatorContext] = Field(min_length=1)
    max_relation_hops: int = Field(ge=0)
    """The engine's limit: many-to-one relations a field path may walk ("manager.department.name" = 2)."""


class SuggestRequest(ContractModel):
    """A business user's free-text request, with the schema context to interpret it against."""

    text: Annotated[str, StringConstraints(strip_whitespace=True, min_length=1, max_length=1000)]
    context: SchemaContext
