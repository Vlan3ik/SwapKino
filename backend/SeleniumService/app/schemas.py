from typing import Literal

from pydantic import AnyHttpUrl, BaseModel, Field


class RatingsRequest(BaseModel):
    profile_url: AnyHttpUrl = Field(..., description="Публичная ссылка на профиль Кинопоиска")
    include_unrated: bool = Field(True, description="Включать просмотренные элементы без оценки")

    model_config = {
        "json_schema_extra": {
            "example": {
                "profile_url": "https://www.kinopoisk.ru/user/36830857/",
                "include_unrated": True,
            }
        }
    }


class RatedItem(BaseModel):
    external_id: str = Field(..., description="Stable numeric Kinopoisk film/series ID")
    title: str
    year: int | None = None
    genres: str | None = None
    rating: float | None = Field(None, ge=0, le=10)
    kind: Literal["film", "series"]
    kinopoisk_url: AnyHttpUrl
    page: int


class RatingsResponse(BaseModel):
    profile_url: AnyHttpUrl
    source_url: AnyHttpUrl
    total: int
    rated: int
    unrated: int
    pages_processed: int
    pages_total: int
    complete: bool = True
    items: list[RatedItem]
