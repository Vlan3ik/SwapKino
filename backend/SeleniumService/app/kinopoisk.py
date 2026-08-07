import logging
import re
from dataclasses import dataclass
from urllib.parse import parse_qs, urlencode, urlparse, urlunparse

from selenium import webdriver
from selenium.common.exceptions import TimeoutException, WebDriverException
from selenium.webdriver.chrome.options import Options
from selenium.webdriver.chrome.service import Service
from selenium.webdriver.common.by import By
from selenium.webdriver.support import expected_conditions as EC
from selenium.webdriver.support.ui import WebDriverWait

from .config import Settings
from .exceptions import CaptchaRequiredError, InvalidProfileError, ScraperError

logger = logging.getLogger(__name__)

CARD_SELECTOR = 'a[href^="/film/"] img[alt], a[href^="/series/"] img[alt]'
KINOPOISK_HOSTS = {"kinopoisk.ru", "www.kinopoisk.ru"}
EMPTY_MARKERS = (
    "список пуст",
    "нет оцененных фильмов",
    "нет просмотренных фильмов",
    "вы ещё не оценили",
)


@dataclass
class ScrapedItem:
    external_id: str
    title: str
    year: int | None
    genres: str | None
    rating: float | None
    kind: str
    kinopoisk_url: str
    page: int


@dataclass
class ScrapeResult:
    items: list[ScrapedItem]
    pages_processed: int
    pages_total: int


def parse_content_identity(url: str) -> tuple[str, str]:
    """Return the canonical Kinopoisk ID and content kind from any card URL."""
    parsed = urlparse(url)
    match = re.search(r"/(film|series)/(\d+)(?:/|$)", parsed.path)
    if not match:
        raise ScraperError(f"Неизвестный формат ссылки карточки: {url}")
    return match.group(2), "series" if match.group(1) == "series" else "film"


def normalize_profile_url(profile_url: str) -> str:
    parsed = urlparse(profile_url)
    if parsed.scheme not in {"http", "https"} or parsed.hostname not in KINOPOISK_HOSTS:
        raise InvalidProfileError("Ожидалась ссылка на профиль www.kinopoisk.ru")
    match = re.fullmatch(r"/user/(\d+)(?:/.*)?", parsed.path.rstrip("/"))
    if not match:
        raise InvalidProfileError("Ссылка должна иметь вид /user/{id}/")
    return f"https://www.kinopoisk.ru/user/{match.group(1)}/movies/voted-watched/"


def with_page(base_url: str, page: int) -> str:
    parsed = urlparse(base_url)
    query = parse_qs(parsed.query)
    query["page"] = [str(page)]
    return urlunparse(parsed._replace(query=urlencode(query, doseq=True)))


class KinopoiskScraper:
    def __init__(self, settings: Settings):
        self.settings = settings

    def _driver(self) -> webdriver.Chrome:
        options = Options()
        if self.settings.selenium_headless:
            options.add_argument("--headless=new")
        options.add_argument("--no-sandbox")
        options.add_argument("--disable-dev-shm-usage")
        options.add_argument("--disable-gpu")
        options.add_argument("--window-size=1440,1200")
        options.add_argument("--lang=ru-RU")
        options.page_load_strategy = "eager"
        if self.settings.chrome_binary:
            options.binary_location = self.settings.chrome_binary
        driver_path = self.settings.resolved_chromedriver_path
        service = Service(executable_path=driver_path) if driver_path else None
        try:
            # В Docker путь задан явно. Локально Selenium Manager может использовать
            # уже установленный или закэшированный совместимый chromedriver.
            driver = webdriver.Chrome(service=service, options=options)
        except WebDriverException as exc:
            raise ScraperError(
                "Не удалось запустить ChromeDriver. Укажите CHROMEDRIVER_PATH "
                "или запустите сервис через Docker."
            ) from exc
        driver.set_page_load_timeout(self.settings.selenium_page_timeout_seconds)
        return driver

    @staticmethod
    def _captcha_or_blocked(driver: webdriver.Chrome) -> bool:
        url = driver.current_url.lower()
        source = driver.page_source.lower()
        markers = ("captcha", "smartcaptcha", "не робот", "подтвердите, что вы не робот")
        return "sso." in url or "passport." in url or any(marker in source for marker in markers)

    @staticmethod
    def _captcha_error(driver: webdriver.Chrome, collected_items: list[ScrapedItem] | None = None, page_number: int = 1, pages_total: int | None = None) -> CaptchaRequiredError:
        screenshot = None
        try:
            screenshot = driver.get_screenshot_as_base64()
        except WebDriverException:
            logger.warning("Could not capture CAPTCHA screenshot", exc_info=True)
        return CaptchaRequiredError(
            "Кинопоиск запросил CAPTCHA или авторизацию",
            page_url=driver.current_url,
            screenshot_base64=screenshot,
            driver=driver,
            collected_items=collected_items,
            page_number=page_number,
            pages_total=pages_total,
        )

    def _extract_current_page(self, driver: webdriver.Chrome, page_number: int) -> list[ScrapedItem]:
        script = r"""
        const selector = arguments[0];
        const pageNumber = arguments[1];
        return [...document.querySelectorAll(selector)].map(img => {
          const link = img.closest('a[href^="/film/"], a[href^="/series/"]');
          if (!link) return null;
          const card = img.closest('div[data-tid]');
          const caption = card?.querySelector('a[aria-hidden="true"]');
          const raw = img.alt || '';
          const match = raw.match(/^(.*)\. (\d{4}), (.*)$/);
          const value = card?.querySelector('span[class*="value"]')?.textContent?.trim() || null;
          const number = value && /^\d+(?:[.,]\d+)?$/.test(value)
            ? Number(value.replace(',', '.')) : null;
          const identity = link.pathname.match(/^\/(film|series)\/(\d+)(?:\/|$)/);
          if (!identity) return null;
          return {
            external_id: identity[2],
            title: caption?.querySelector('[class*="title"]')?.innerText?.trim() || match?.[1] || raw,
            year: match?.[2] ? Number(match[2]) : null,
            genres: match?.[3] || null,
            rating: number,
            kind: link.pathname.startsWith('/series/') ? 'series' : 'film',
            kinopoisk_url: new URL(link.href, location.origin).href,
            page: pageNumber
          };
        }).filter(Boolean);
        """
        raw_items = driver.execute_script(script, CARD_SELECTOR, page_number) or []
        result = [ScrapedItem(**item) for item in raw_items]
        for item in result:
            external_id, kind = parse_content_identity(item.kinopoisk_url)
            if external_id != item.external_id or kind != item.kind:
                raise ScraperError("Кинопоиск вернул несогласованный ID или тип карточки")
        return result

    def _last_page(self, driver: webdriver.Chrome) -> int:
        return int(driver.execute_script("""
          return Math.max(1, ...[...document.querySelectorAll('a[href*="page="]')]
            .map(a => Number(new URL(a.href, location.origin).searchParams.get('page')) || 1));
        """) or 1)

    @staticmethod
    def _is_explicit_empty(driver: webdriver.Chrome) -> bool:
        text = (driver.execute_script("return document.body?.innerText || ''") or "").lower()
        return any(marker in text for marker in EMPTY_MARKERS)

    def _collect(self, driver: webdriver.Chrome, source_url: str, include_unrated: bool, start_page: int = 1, initial_items: list[ScrapedItem] | None = None, known_pages_total: int | None = None) -> ScrapeResult:
        items: list[ScrapedItem] = list(initial_items or [])
        if self._captcha_or_blocked(driver):
            raise self._captcha_error(driver, items, start_page, known_pages_total)
        try:
            WebDriverWait(driver, self.settings.selenium_element_timeout_seconds).until(
                EC.presence_of_element_located((By.CSS_SELECTOR, CARD_SELECTOR))
            )
        except TimeoutException:
            if not items and self._is_explicit_empty(driver):
                return ScrapeResult([], 1, 1)
            raise ScraperError("Кинопоиск не вернул карточки: возможно, изменилась DOM-разметка или профиль недоступен")
        discovered_last_page = max(self._last_page(driver), start_page)
        last_page = max(known_pages_total or 1, discovered_last_page)
        if last_page > self.settings.selenium_max_pages:
            raise ScraperError(f"Импорт содержит {last_page} страниц, что превышает безопасный лимит {self.settings.selenium_max_pages}; частичный импорт не применён")
        seen = {item.external_id for item in items}
        pages_processed = max((item.page for item in items), default=start_page - 1)
        for page_number in range(start_page, last_page + 1):
            if page_number > start_page:
                driver.get(with_page(source_url, page_number))
                if self._captcha_or_blocked(driver):
                    raise self._captcha_error(driver, items, page_number, last_page)
                try:
                    WebDriverWait(driver, self.settings.selenium_element_timeout_seconds).until(
                        EC.presence_of_element_located((By.CSS_SELECTOR, CARD_SELECTOR))
                    )
                except TimeoutException:
                    raise ScraperError(f"Страница {page_number} не содержит карточек; импорт остановлен как неполный")
            page_items = self._extract_current_page(driver, page_number)
            if not page_items:
                raise ScraperError(f"На странице {page_number} не удалось распознать ни одной карточки")
            page_ids = {item.external_id for item in page_items}
            if page_number > start_page and page_ids and page_ids.issubset(seen):
                raise ScraperError(f"Пагинация вернула повторную страницу {page_number}; импорт не применён")
            items.extend(item for item in page_items if item.external_id not in seen)
            seen.update(page_ids)
            pages_processed = page_number
            if len(items) > self.settings.selenium_max_items:
                raise ScraperError(f"Импорт превышает лимит {self.settings.selenium_max_items} позиций; частичный импорт не применён")
        result = items if include_unrated else [item for item in items if item.rating is not None]
        return ScrapeResult(result, pages_processed, last_page)

    def scrape(self, profile_url: str, include_unrated: bool = True) -> tuple[str, ScrapeResult]:
        source_url = normalize_profile_url(str(profile_url))
        driver = None
        keep_driver = False
        try:
            driver = self._driver()
            driver.get(with_page(source_url, 1))
            return source_url, self._collect(driver, source_url, include_unrated)
        except CaptchaRequiredError as exc:
            keep_driver = True
            raise
        except ScraperError:
            raise
        except (TimeoutException, WebDriverException) as exc:
            logger.exception("Selenium failed while scraping %s", source_url)
            raise ScraperError(f"Selenium не смог загрузить страницу: {exc.__class__.__name__}") from exc
        finally:
            if driver is not None and not keep_driver:
                driver.quit()

    def resume(self, driver: webdriver.Chrome, source_url: str, include_unrated: bool, initial_items: list[ScrapedItem] | None = None, start_page: int = 1, pages_total: int | None = None) -> ScrapeResult:
        return self._collect(driver, source_url, include_unrated, start_page=start_page, initial_items=initial_items, known_pages_total=pages_total)
