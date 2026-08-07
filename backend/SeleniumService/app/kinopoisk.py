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


@dataclass
class ScrapedItem:
    title: str
    year: int | None
    genres: str | None
    rating: float | None
    kind: str
    kinopoisk_url: str
    page: int


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
    def _captcha_error(driver: webdriver.Chrome, collected_items: list[ScrapedItem] | None = None, page_number: int = 1) -> CaptchaRequiredError:
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
        )

    def _extract_current_page(self, driver: webdriver.Chrome, page_number: int) -> list[ScrapedItem]:
        script = """
        const selector = arguments[0];
        const pageNumber = arguments[1];
        return [...document.querySelectorAll(selector)].map(img => {
          const link = img.closest('a[href^="/film/"], a[href^="/series/"]');
          if (!link) return null;
          const card = img.closest('div[data-tid]');
          const caption = card?.querySelector('a[aria-hidden="true"]');
          const raw = img.alt || '';
          const match = raw.match(/^(.*)\\. (\\d{4}), (.*)$/);
          const value = card?.querySelector('span[class*="value"]')?.textContent?.trim() || null;
          const number = value && /^\\d+(?:[.,]\\d+)?$/.test(value)
            ? Number(value.replace(',', '.')) : null;
          return {
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
        return [ScrapedItem(**item) for item in raw_items]

    def _last_page(self, driver: webdriver.Chrome) -> int:
        return int(driver.execute_script("""
          return Math.max(1, ...[...document.querySelectorAll('a[href*="page="]')]
            .map(a => Number(new URL(a.href, location.origin).searchParams.get('page')) || 1));
        """) or 1)

    def _collect(self, driver: webdriver.Chrome, source_url: str, include_unrated: bool, start_page: int = 1, initial_items: list[ScrapedItem] | None = None) -> list[ScrapedItem]:
        items: list[ScrapedItem] = list(initial_items or [])
        if self._captcha_or_blocked(driver):
            raise self._captcha_error(driver, items, start_page)
        try:
            WebDriverWait(driver, self.settings.selenium_element_timeout_seconds).until(
                EC.presence_of_element_located((By.CSS_SELECTOR, CARD_SELECTOR))
            )
        except TimeoutException:
            # Пустой профиль — валидный результат, а не ошибка Selenium.
            return [] if not items else items
        last_page = min(max(self._last_page(driver), start_page), self.settings.selenium_max_pages)
        for page_number in range(start_page, last_page + 1):
            if page_number > start_page:
                driver.get(with_page(source_url, page_number))
                if self._captcha_or_blocked(driver):
                    raise self._captcha_error(driver, items, page_number)
                try:
                    WebDriverWait(driver, self.settings.selenium_element_timeout_seconds).until(
                        EC.presence_of_element_located((By.CSS_SELECTOR, CARD_SELECTOR))
                    )
                except TimeoutException:
                    break
            items.extend(self._extract_current_page(driver, page_number))
            if len(items) >= self.settings.selenium_max_items:
                break
        unique = {item.kinopoisk_url: item for item in items}
        result = list(unique.values())[: self.settings.selenium_max_items]
        return result if include_unrated else [item for item in result if item.rating is not None]

    def scrape(self, profile_url: str, include_unrated: bool = True) -> tuple[str, list[ScrapedItem]]:
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

    def resume(self, driver: webdriver.Chrome, source_url: str, include_unrated: bool, initial_items: list[ScrapedItem] | None = None, start_page: int = 1) -> list[ScrapedItem]:
        return self._collect(driver, source_url, include_unrated, start_page=start_page, initial_items=initial_items)
