from pydantic import BaseModel, ConfigDict
from pydantic.alias_generators import to_camel


class ContractModel(BaseModel):
    """Base of every wire contract: camelCase JSON (as .NET and the UI use), immutable, and strict about
    unknown properties - an AI answer with an invented property is rejected, not silently ignored."""

    model_config = ConfigDict(
        alias_generator=to_camel,
        populate_by_name=True,
        extra="forbid",
        frozen=True,
        use_attribute_docstrings=True,  # field docstrings become descriptions in the published JSON Schema
    )
