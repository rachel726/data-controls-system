from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse

from app.api.routes import router
from app.services.suggestion_service import SuggestionError

app = FastAPI(
    title="Data Controls AI Service",
    version="0.1.0",
    description="Suggests a data-control definition from free text and a business schema context.",
)
app.include_router(router)


@app.exception_handler(SuggestionError)
async def suggestion_error(_: Request, error: SuggestionError) -> JSONResponse:
    """The model failed or broke the contract: 502 (an upstream problem), as RFC 9457 problem details."""
    return JSONResponse(
        status_code=502,
        media_type="application/problem+json",
        content={
            "type": f"https://data-controls/errors/ai/{error.code}",
            "title": "הצעת ה-AI נכשלה",
            "status": 502,
            "detail": error.message,
            "code": error.code,
            "errors": error.details,
        },
    )


@app.get("/health", include_in_schema=False)
async def health() -> dict[str, str]:
    return {"status": "ok"}
