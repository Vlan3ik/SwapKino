# SonarQube report — SwapKino

Финальный scan: 2026-09-10 (Europe/Moscow)  
Проект: `swapkino`  
Dashboard: [http://127.0.0.1:9002/dashboard?id=swapkino](http://127.0.0.1:9002/dashboard?id=swapkino)

## Severity summary

| Severity | Count |
|---|---:|
| Blocker | 0 |
| Critical | 0 |
| Major | 0 |
| Minor | 147 |
| Info | 19 |

Все блокирующие, критические и средние (`MAJOR`) нарушения устранены.

## Quality Gate

Статус: **FAILED**

| Метрика | Результат | Порог | Статус |
|---|---:|---:|---|
| New coverage | 15,0% | 80% | FAIL |
| New violations | 87 | 0 | FAIL |
| New duplicated lines density | 0,00114% | 3% | OK |

Quality Gate не проходит из-за покрытия и оставшихся `MINOR/INFO`-замечаний; требований по `BLOCKER/CRITICAL/MAJOR` больше нет.

## Выполненные изменения

- Добавлен workflow [`.github/workflows/sonarqube.yml`](/home/krl/Документы/SwapKino/.github/workflows/sonarqube.yml) для CI-сканирования.
- Добавлен OpenCover через `coverlet.collector`.
- Исправлены проблемы секретов и прав доступа для CAPTCHA-сессий, удалён пароль БД из конфигурации.
- Устранены проблемы безопасности, пустые обработчики исключений, опасные retry/regex, сложные методы и вложенные условия.
- TMDB admin API вынесен в отдельный `TmdbAdminController`; зависимости `ApiController` сгруппированы.
- Исправлены accessibility/UI-проблемы и типы входных моделей.
- Добавлен набор из 23 unit-тестов для TMDB, Vibix, DTO, рекомендаций и защитных веток.
- Добавлен интеграционный тест recommendation pipeline: построение deck и preview из независимых TMDB-источников.

## Проверки

- `dotnet build backend/SwapKino.sln --no-restore` — успешно, 0 ошибок и 0 предупреждений.
- `npm run build` в `frontend` — успешно.
- `dotnet test` — 46 passed, 12 skipped legacy-тестов старой удалённой модели `Movie/Genre`, 0 failed.
- Локальный OpenCover для API: 78,53% строк, 38,07% ветвлений.
- Coverage-файл импортирован в финальный scan: `backend/SwapKino.IntegrationTests/TestResults/cea516c1-7c3d-482c-ada1-cea1e08e2204/coverage.opencover.xml`.
- Фактическое покрытие проекта в SonarQube: 22,2%; расхождение связано с тем, что Sonar учитывает весь backend, включая Worker, а текущий OpenCover-файл содержит покрытие API.
