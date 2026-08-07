import logging
import threading
from contextlib import asynccontextmanager

from fastapi import FastAPI, HTTPException, status
from fastapi.middleware.cors import CORSMiddleware

from .config import get_settings
from .captcha_sessions import CaptchaSessionStore
from .exceptions import CaptchaRequiredError, InvalidProfileError, ScraperError
from .kinopoisk import KinopoiskScraper, normalize_profile_url
from .schemas import RatedItem, RatingsRequest, RatingsResponse

settings = get_settings()
logging.basicConfig(level=settings.log_level.upper())
captcha_sessions = CaptchaSessionStore(ttl_seconds=300)
selenium_gate = threading.BoundedSemaphore(value=max(1, settings.selenium_max_concurrent))


@asynccontextmanager
async def lifespan(_: FastAPI):
    yield


app = FastAPI(
    title=settings.app_name,
    version="1.0.0",
    description="Локальный Selenium-сервис импорта публичных оценок Кинопоиска для SwapKino.",
    lifespan=lifespan,
)
if settings.cors_origins:
    app.add_middleware(
        CORSMiddleware,
        allow_origins=settings.cors_origins,
        allow_credentials=False,
        allow_methods=["GET", "POST"],
        allow_headers=["*"],
    )


@app.get("/health", tags=["system"])
def health() -> dict[str, str]:
    return {"status": "ok", "service": "selenium"}


def captcha_detail(exc: CaptchaRequiredError, session_id: str | None = None) -> dict:
    return {
        "code": "CAPTCHA_REQUIRED",
        "message": str(exc),
        "session_id": session_id,
        "page_url": exc.page_url,
        "screenshot_base64": exc.screenshot_base64,
        "screenshot_mime_type": "image/png" if exc.screenshot_base64 else None,
        "expires_in_seconds": 300,
        "action": "manual_interaction_required",
        "resume_endpoint": f"/api/v1/kinopoisk/captcha/{session_id}/resume" if session_id else None,
        "security_note": "Не передавайте cookies, HTML или CAPTCHA-токены через API.",
    }


@app.post(
    "/api/v1/kinopoisk/ratings",
    response_model=RatingsResponse,
    tags=["kinopoisk"],
    summary="Импортировать оценки публичного профиля",
)
def import_ratings(payload: RatingsRequest) -> RatingsResponse:
    scraper = KinopoiskScraper(settings)
    try:
        with selenium_gate:
            source_url, scraped = scraper.scrape(str(payload.profile_url), payload.include_unrated)
    except InvalidProfileError as exc:
        raise HTTPException(status_code=status.HTTP_422_UNPROCESSABLE_ENTITY, detail=str(exc)) from exc
    except CaptchaRequiredError as exc:
        session = captcha_sessions.add(
            exc.driver,
            normalize_profile_url(str(payload.profile_url)),
            payload.include_unrated,
        )
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail=captcha_detail(exc, session.session_id),
        ) from exc
    except ScraperError as exc:
        raise HTTPException(status_code=status.HTTP_502_BAD_GATEWAY, detail=str(exc)) from exc

    items = [RatedItem.model_validate(item.__dict__) for item in scraped]
    return RatingsResponse(
        profile_url=payload.profile_url,
        source_url=source_url,
        total=len(items),
        rated=sum(item.rating is not None for item in items),
        unrated=sum(item.rating is None for item in items),
        items=items,
    )


@app.post(
    "/api/v1/kinopoisk/captcha/{session_id}/resume",
    response_model=RatingsResponse,
    tags=["kinopoisk"],
    summary="Продолжить импорт после ручной CAPTCHA",
)
def resume_after_captcha(session_id: str) -> RatingsResponse:
    session = captcha_sessions.get(session_id)
    if session is None:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="CAPTCHA-сессия не найдена или истекла")

    scraper = KinopoiskScraper(settings)
    try:
        with selenium_gate:
            scraped = scraper.resume(session.driver, session.source_url, session.include_unrated)
    except CaptchaRequiredError as exc:
        # Selenium-контекст остаётся тем же; пользователь может завершить challenge
        # и вызвать resume ещё раз до истечения TTL.
        raise HTTPException(status_code=status.HTTP_409_CONFLICT, detail=captcha_detail(exc, session_id)) from exc
    except ScraperError as exc:
        captcha_sessions.remove(session_id)
        raise HTTPException(status_code=status.HTTP_502_BAD_GATEWAY, detail=str(exc)) from exc

    captcha_sessions.remove(session_id)
    items = [RatedItem.model_validate(item.__dict__) for item in scraped]
    return RatingsResponse(
        profile_url=session.source_url,
        source_url=session.source_url,
        total=len(items),
        rated=sum(item.rating is not None for item in items),
        unrated=sum(item.rating is None for item in items),
        items=items,
    )
