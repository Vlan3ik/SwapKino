import unittest

from app.exceptions import InvalidProfileError
from app.kinopoisk import normalize_profile_url, with_page


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


if __name__ == "__main__":
    unittest.main()
