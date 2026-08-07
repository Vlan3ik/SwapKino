class ScraperError(Exception):
    """Base error raised by the Kinopoisk scraper."""


class CaptchaRequiredError(ScraperError):
    """The target site requires an interactive CAPTCHA/authentication step."""

    def __init__(
        self,
        message: str,
        *,
        page_url: str | None = None,
        screenshot_base64: str | None = None,
        driver: object | None = None,
        collected_items: list[object] | None = None,
        page_number: int = 1,
        pages_total: int | None = None,
    ):
        super().__init__(message)
        self.page_url = page_url
        self.screenshot_base64 = screenshot_base64
        self.driver = driver
        self.collected_items = collected_items or []
        self.page_number = page_number
        self.pages_total = pages_total


class InvalidProfileError(ScraperError):
    """The supplied profile URL is not a supported Kinopoisk URL."""
