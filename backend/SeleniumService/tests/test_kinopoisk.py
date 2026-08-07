import unittest
from types import SimpleNamespace
from unittest.mock import patch
from urllib.parse import parse_qs, urlparse

from selenium.common.exceptions import TimeoutException

from app.exceptions import InvalidProfileError, ScraperError
from app.kinopoisk import KinopoiskScraper, parse_content_identity, normalize_profile_url, with_page


def card(external_id: str, page: int, kind: str = "film") -> dict:
    return {
        "external_id": external_id,
        "title": f"Title {external_id}",
        "year": 2024,
        "genres": "драма",
        "rating": 8.0,
        "kind": kind,
        "kinopoisk_url": f"https://www.kinopoisk.ru/{kind}/{external_id}/",
        "page": page,
    }


class FakeDriver:
    def __init__(self, pages: dict[int, list[dict]], pages_total: int):
        self.pages = pages
        self.pages_total = pages_total
        self.current_page = 1
        self.current_url = "https://www.kinopoisk.ru/user/1/movies/voted-watched/?page=1"
        self.page_source = "ratings"

    def get(self, url: str) -> None:
        self.current_url = url
        self.current_page = int(parse_qs(urlparse(url).query).get("page", ["1"])[0])

    def execute_script(self, script: str, *args):
        if "Math.max" in script:
            return self.pages_total
        if "document.body" in script:
            return ""
        return self.pages.get(self.current_page, [])


class FakeWait:
    def __init__(self, driver: FakeDriver, _: int):
        self.driver = driver

    def until(self, _condition):
        if not self.driver.pages.get(self.driver.current_page):
            raise TimeoutException()
        return True


class KinopoiskUrlTests(unittest.TestCase):
    def test_profile_url_is_normalized_to_ratings_page(self):
        self.assertEqual(
            normalize_profile_url("https://www.kinopoisk.ru/user/36830857/"),
            "https://www.kinopoisk.ru/user/36830857/movies/voted-watched/",
        )

    def test_profile_path_must_contain_numeric_user_id(self):
        with self.assertRaises(InvalidProfileError):
            normalize_profile_url("https://www.kinopoisk.ru/user/not-a-user/")

    def test_page_parameter_replaces_existing_page(self):
        self.assertEqual(
            with_page("https://www.kinopoisk.ru/user/1/movies/voted-watched/?page=2&foo=bar", 7),
            "https://www.kinopoisk.ru/user/1/movies/voted-watched/?page=7&foo=bar",
        )

    def test_film_identity_is_stable_across_slug_and_query_changes(self):
        self.assertEqual(
            parse_content_identity("https://www.kinopoisk.ru/film/447301/?utm_source=test"),
            ("447301", "film"),
        )

    def test_series_identity_preserves_content_type(self):
        self.assertEqual(
            parse_content_identity("https://www.kinopoisk.ru/series/123456/season/1/"),
            ("123456", "series"),
        )

    def test_unknown_card_url_fails_instead_of_creating_unstable_identity(self):
        with self.assertRaises(ScraperError):
            parse_content_identity("https://www.kinopoisk.ru/lists/movies/top250/")


class KinopoiskCompletenessTests(unittest.TestCase):
    def scraper(self) -> KinopoiskScraper:
        return KinopoiskScraper(SimpleNamespace(
            selenium_element_timeout_seconds=1,
            selenium_max_pages=100,
            selenium_max_items=10_000,
        ))

    @patch("app.kinopoisk.WebDriverWait", FakeWait)
    def test_all_discovered_pages_are_returned_with_completeness_metadata(self):
        driver = FakeDriver({1: [card("1", 1)], 2: [card("2", 2, "series")]}, 2)
        result = self.scraper()._collect(driver, driver.current_url, True)
        self.assertEqual([item.external_id for item in result.items], ["1", "2"])
        self.assertEqual((result.pages_processed, result.pages_total), (2, 2))

    @patch("app.kinopoisk.WebDriverWait", FakeWait)
    def test_missing_intermediate_page_fails_instead_of_returning_partial_data(self):
        driver = FakeDriver({1: [card("1", 1)], 2: []}, 2)
        with self.assertRaisesRegex(ScraperError, "не содержит карточек"):
            self.scraper()._collect(driver, driver.current_url, True)

    @patch("app.kinopoisk.WebDriverWait", FakeWait)
    def test_repeated_page_fails_instead_of_looking_complete(self):
        driver = FakeDriver({1: [card("1", 1)], 2: [card("1", 2)]}, 2)
        with self.assertRaisesRegex(ScraperError, "повторную страницу"):
            self.scraper()._collect(driver, driver.current_url, True)


if __name__ == "__main__":
    unittest.main()
