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
    ):
        super().__init__(message)
        self.page_url = page_url
        self.screenshot_base64 = screenshot_base64
        self.driver = driver


class InvalidProfileError(ScraperError):
    """The supplied profile URL is not a supported Kinopoisk URL."""
