from typing import Annotated

from fastapi import APIRouter, Depends

from app.config import Settings, get_settings
from app.models.example import Example
from app.models.schema_context import SuggestRequest
from app.models.suggestion import ControlSuggestion
from app.providers.factory import create_provider
from app.services.suggestion_service import SuggestionService

router = APIRouter(prefix="/v1")


@router.get(
    "/examples",
    response_model=list[Example],
    response_model_by_alias=True,
    summary="Requests offered as one-click examples",
)
async def examples(settings: Annotated[Settings, Depends(get_settings)]) -> list[Example]:
    """The configured model's ready-made requests - the single source of the UI's examples."""
    return create_provider(settings).examples()


def get_suggestion_service(settings: Annotated[Settings, Depends(get_settings)]) -> SuggestionService:
    return SuggestionService(create_provider(settings), settings.ai_timeout_seconds)


@router.post(
    "/suggest-control",
    response_model=ControlSuggestion,
    response_model_by_alias=True,
    response_model_exclude_none=True,
    summary="Suggest a control from free text",
    responses={502: {"description": "The model failed or answered outside the contract (problem details)."}},
)
async def suggest_control(
    request: SuggestRequest, service: Annotated[SuggestionService, Depends(get_suggestion_service)]
) -> ControlSuggestion:
    """A business request + the business schema context -> a control suggestion. Nothing is run here."""
    return await service.suggest(request)
