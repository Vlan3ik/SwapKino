import unittest

from app.captcha_sessions import CaptchaSessionStore


class Driver:
    def __init__(self):
        self.quit_calls = 0

    def quit(self):
        self.quit_calls += 1


class CaptchaSessionStoreTests(unittest.TestCase):
    def test_remove_closes_driver(self):
        store = CaptchaSessionStore(ttl_seconds=300)
        driver = Driver()
        session = store.add(driver, "https://www.kinopoisk.ru/user/1/movies/voted-watched/", True)
        store.remove(session.session_id)
        self.assertIsNone(store.get(session.session_id))
        self.assertEqual(driver.quit_calls, 1)

    def test_unknown_session_is_not_returned(self):
        self.assertIsNone(CaptchaSessionStore().get("missing"))

    def test_close_all_closes_every_driver(self):
        store = CaptchaSessionStore()
        first, second = Driver(), Driver()
        store.add(first, "https://www.kinopoisk.ru/user/1/movies/voted-watched/", True)
        store.add(second, "https://www.kinopoisk.ru/user/2/movies/voted-watched/", True)
        store.close_all()
        self.assertEqual(first.quit_calls, 1)
        self.assertEqual(second.quit_calls, 1)


if __name__ == "__main__":
    unittest.main()
