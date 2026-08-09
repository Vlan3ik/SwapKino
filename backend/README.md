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
# Контракт пользовательской библиотеки

Авторизованный frontend может работать как тонкая панель управления API:

- `GET /api/v1/profile` — профиль, статистика (`favoritesCount`, `ratingsCount`, `watchedCount`, `libraryCount`, `averageRating`) и по пять последних элементов избранного/оценок.
- `GET /api/v1/favorites` — избранные фильмы и сериалы.
- `GET /api/v1/ratings` — оценённые фильмы и сериалы.

Оба списка принимают `limit` (1–50), `page`, `cursor`, `q`, `genreIds`, `minRating`, `yearFrom`, `yearTo`, `isSeries` и `sort`. В ответе находятся `items`, `totalCount`, `totalPages`, `hasNextPage` и `nextCursor`; каждый элемент содержит пользовательское состояние и готовый `movie` в формате карточки каталога. Это позволяет фронтенду не загружать всю библиотеку и не выполнять дополнительное склеивание данных.

TMDB настраивается только через `backend/.env` (`TMDB_ACCESS_TOKEN` или `TMDB_API_KEY`). Файл `.env` не должен попадать в git.
