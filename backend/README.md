# SwapKino Backend — ASP.NET Core

Backend состоит из ASP.NET Core Web API на C#, отдельного .NET Worker, Entity Framework Core, PostgreSQL, двух Redis-инстансов, MinIO и Selenium-сервиса.

## Запуск

```bash
cp .env.example .env
# заполнить TMDB_ACCESS_TOKEN или TMDB_API_KEY
docker compose up --build
```

API: `http://localhost:8000`, Swagger: `http://localhost:8000/swagger`.

В конфигурации применены следующие контуры:

- `api` — ASP.NET Core API, JWT auth, каталог, рекомендации, библиотека, действия и задания импорта;
- `worker` — .NET Worker, transactional outbox и Redis Streams;
- PostgreSQL — устойчивое состояние;
- `redis-runtime` — streams/locks/runtime с `noeviction`;
- `redis-cache` — кэш с LRU eviction;
- `minio` — объектное хранилище для будущих диагностических артефактов;
- `selenium-service` — изолированный Kinopoisk scraper.

Для продакшена обязательно заменить `JWT_SECRET`, пароли PostgreSQL/MinIO и TMDB credentials через секрет-хранилище.

Vibix настраивается через `VIBIX_API_KEY`. Backend разрешает источник только по сохранённым Kinopoisk/IMDb ID через официальные `GET /api/v1/publisher/videos/kp/{id}` и `GET /api/v1/publisher/videos/imdb/{id}`. Готовый `iframe_url` имеет приоритет; если API вернул только publisher attributes, используется SDK с внешним `kp`/`imdb` ID. Поиск по названию и внутренний `iframe_video_id` в playback-пути не используются. Токен хранится только в `.env`/секрет-хранилище и не включается в исходники.
# Контракт пользовательской библиотеки

Авторизованный frontend может работать как тонкая панель управления API:

- `GET /api/v1/profile` — профиль, статистика (`favoritesCount`, `ratingsCount`, `watchedCount`, `libraryCount`, `averageRating`) и по пять последних элементов избранного/оценок.
- `GET /api/v1/favorites` — избранные фильмы и сериалы.
- `GET /api/v1/ratings` — оценённые фильмы и сериалы.

Оба списка принимают `limit` (1–50), `page`, `cursor`, `q`, `genreIds`, `minRating`, `yearFrom`, `yearTo`, `isSeries` и `sort`. В ответе находятся `items`, `totalCount`, `totalPages`, `hasNextPage` и `nextCursor`; каждый элемент содержит пользовательское состояние и готовый `movie` в формате карточки каталога. Это позволяет фронтенду не загружать всю библиотеку и не выполнять дополнительное склеивание данных.

TMDB настраивается только через `backend/.env` (`TMDB_ACCESS_TOKEN` или `TMDB_API_KEY`). Файл `.env` не должен попадать в git.
