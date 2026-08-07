# SwapKino Selenium Service

Изолированный локальный микросервис для импорта оценок из публичного профиля Кинопоиска. Основной backend передаёт URL пользователя, сервис запускает Chromium через Selenium, сам обходит пагинацию и возвращает JSON с названиями произведений и пользовательскими оценками.

## Возможности

- `POST /api/v1/kinopoisk/ratings` — импорт списка оценок;
- автоматическое определение количества страниц;
- фильмы и сериалы с единым полем `kind`;
- название, год, жанры, оценка, URL Кинопоиска и номер страницы;
- режим исключения просмотренных произведений без оценки;
- `/health` для Docker health-check;
- Swagger UI и OpenAPI JSON;
- явная конфигурация ChromeDriver для Docker и Selenium Manager для локальной разработки.

## Запуск через Docker

```bash
cp .env.example .env
docker compose up --build
```

После запуска:

- Swagger: http://localhost:8081/docs
- ReDoc: http://localhost:8081/redoc
- OpenAPI: http://localhost:8081/openapi.json
- Health: http://localhost:8081/health

Остановить сервис:

```bash
docker compose down
```

В Docker устанавливаются совместимые `chromium` и `chromium-driver`, поэтому контейнер не зависит от драйвера на хосте.

## Локальный запуск

Требования: Python 3.12+, Google Chrome/Chromium и ChromeDriver совместимой версии.

```bash
python -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
cp .env.example .env
python run.py
```

Если Selenium Manager не может найти драйвер автоматически, укажите путь:

```bash
export CHROME_BINARY=/usr/bin/google-chrome
export CHROMEDRIVER_PATH=/usr/local/bin/chromedriver
python run.py
```

## API-пример

```bash
curl -X POST http://localhost:8081/api/v1/kinopoisk/ratings \
  -H 'Content-Type: application/json' \
  -d '{
    "profile_url": "https://www.kinopoisk.ru/user/36830857/",
    "include_unrated": true
  }'
```

Ответ содержит `total`, `rated`, `unrated`, `pages_processed`, `pages_total`, `complete` и `items`. Каждый элемент содержит стабильный `external_id`, `title`, `year`, `genres`, `rating`, `kind`, `kinopoisk_url` и `page`. Сервис не возвращает тихо обрезанный результ: если пагинация неполна или DOM не распознан, запрос завершается ошибкой.

`include_unrated=false` исключает элементы, у которых `rating=null`.

## HTTP-ошибки

| Код | Причина |
|---:|---|
| 200 | Импорт завершён |
| 409 | `CAPTCHA_REQUIRED`: Кинопоиск запросил CAPTCHA или авторизацию |
| 422 | Некорректная или неподдерживаемая ссылка профиля |
| 502 | Selenium/Chrome не смогли загрузить страницу |

CAPTCHA не обходится. Сервис возвращает структурированный `409 CAPTCHA_REQUIRED` со скриншотом текущего браузерного состояния, но не отдаёт cookies, HTML или токены. Для ручного прохождения нужен тот же живой браузерный контекст через отдельный интерактивный UI; одного API-ответа со скриншотом для продолжения недостаточно.

## Конфигурация

Все параметры находятся в `.env.example`: порт, таймауты Selenium, лимиты страниц и элементов, пути к Chrome/ChromeDriver и CORS origins.

## Структура

```text
app/
  config.py       настройки окружения
  exceptions.py   ошибки доменного слоя
  kinopoisk.py    Selenium-парсер и нормализация данных
  schemas.py      Pydantic API-контракты
  main.py         FastAPI-приложение и маршруты
run.py            локальная точка запуска
Dockerfile        production-контейнер с Chromium
docker-compose.yml
docs/API.md       контракт интеграции с основным backend
```
