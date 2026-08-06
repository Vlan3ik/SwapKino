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
