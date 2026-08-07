from functools import lru_cache
from pathlib import Path

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    app_name: str = Field(default="SwapKino Selenium Service", alias="APP_NAME")
    host: str = Field(default="0.0.0.0", alias="HOST")
    port: int = Field(default=8081, alias="PORT")
    log_level: str = Field(default="INFO", alias="LOG_LEVEL")
    selenium_headless: bool = Field(default=True, alias="SELENIUM_HEADLESS")
    selenium_page_timeout_seconds: int = Field(default=45, alias="SELENIUM_PAGE_TIMEOUT_SECONDS")
    selenium_element_timeout_seconds: int = Field(default=20, alias="SELENIUM_ELEMENT_TIMEOUT_SECONDS")
    selenium_max_pages: int = Field(default=100, alias="SELENIUM_MAX_PAGES")
    selenium_max_items: int = Field(default=10_000, alias="SELENIUM_MAX_ITEMS")
    selenium_max_concurrent: int = Field(default=1, alias="SELENIUM_MAX_CONCURRENT")
    chrome_binary: str = Field(default="", alias="CHROME_BINARY")
    chromedriver_path: str = Field(default="", alias="CHROMEDRIVER_PATH")
    allowed_origins: str = Field(default="", alias="ALLOWED_ORIGINS")

    model_config = SettingsConfigDict(env_file=".env", extra="ignore", populate_by_name=True)

    @property
    def cors_origins(self) -> list[str]:
        return [origin.strip() for origin in self.allowed_origins.split(",") if origin.strip()]

    @property
    def resolved_chromedriver_path(self) -> str | None:
        candidates = [self.chromedriver_path, "/usr/bin/chromedriver", "/usr/local/bin/chromedriver"]
        return next((path for path in candidates if path and Path(path).is_file()), None)


@lru_cache
def get_settings() -> Settings:
    return Settings()
