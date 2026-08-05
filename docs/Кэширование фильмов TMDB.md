# Кэширование фильмов TMDB

## Общая схема

Используется **ленивая загрузка с постоянным хранением в PostgreSQL**:

```text
TMDB candidate
→ краткие данные в БД
→ пользователь открывает карточку
→ полных данных нет
→ загрузка из TMDB
→ сохранение полного фильма в БД
→ все следующие запросы читают БД
```

TMDB никогда не вызывается напрямую из контроллера. Вся работа проходит через `MovieDetailsService`.

---

## Состояния фильма

```csharp
public enum MovieDetailsState
{
    SummaryOnly, // Есть данные из discover/recommendations
    Loading,     // Другой запрос уже загружает детали
    Ready,       // Полные данные сохранены
    Failed       // Последняя загрузка завершилась ошибкой
}
```

---

## Таблица `Movies`

```text
TmdbId                  bigint PK
Title                   text
OriginalTitle           text
Overview                text
Tagline                 text
ReleaseDate             date
RuntimeMinutes          int
OriginalLanguage        text
Status                  text
Adult                   bool
Budget                  bigint
Revenue                 bigint
Homepage                text
ImdbId                  text

VoteAverage             decimal
VoteCount               int
Popularity              decimal

PosterPath              text
BackdropPath            text

DetailsState            enum
DetailsLoadedAt         timestamp null
DetailsUpdatedAt        timestamp null
NeedsRefresh            bool
LastLoadError           text null
LoadFailedAt            timestamp null

TmdbPayload             jsonb
RowVersion              bigint
```

`TmdbPayload` хранит оригинальный полный ответ TMDB. Основные поля дополнительно раскладываются по колонкам для фильтрации и поиска.

---

## Связанные таблицы

```text
MovieGenres
- MovieTmdbId
- GenreTmdbId

MovieKeywords
- MovieTmdbId
- KeywordTmdbId
- Name

MovieCredits
- MovieTmdbId
- PersonTmdbId
- Name
- Type: Actor | Director
- Character
- SortOrder
- ProfilePath

MovieVideos
- MovieTmdbId
- TmdbVideoId
- Site
- Key
- Type
- Official

MovieImages
- MovieTmdbId
- FilePath
- Type: Poster | Backdrop | Logo
- Language
- Width
- Height
- VoteAverage

MovieWatchProviders
- MovieTmdbId
- Region
- ProviderTmdbId
- ProviderName
- Type: Flatrate | Rent | Buy | Free | Ads
- LogoPath
```

---

## Сохранение кратких данных

Результаты `discover`, `similar`, `recommendations` не должны вызывать загрузку полной карточки.

Для каждого найденного фильма выполняется upsert:

```text
TmdbId
Title
Overview
ReleaseDate
GenreIds
VoteAverage
VoteCount
Popularity
PosterPath
BackdropPath
DetailsState = SummaryOnly
```

Существующая полная запись при таком upsert не перезаписывается краткой.

---

## Открытие карточки

```http
GET /api/movies/{tmdbId}
```

Алгоритм:

```text
1. Найти Movie по TmdbId.
2. Если DetailsState = Ready:
   вернуть данные из PostgreSQL.
3. Если фильма нет:
   создать запись SummaryOnly.
4. Получить lock по ключу movie-details:{tmdbId}.
5. Повторно проверить запись после получения lock.
6. Если другой запрос уже загрузил фильм:
   вернуть данные из БД.
7. Загрузить полный набор данных из TMDB.
8. В одной транзакции обновить Movie и связанные таблицы.
9. Установить DetailsState = Ready.
10. Вернуть DTO, построенный из сохранённых данных.
```

Для загрузки используется один составной запрос:

```http
GET /movie/{tmdbId}
    ?language=ru-RU
    &append_to_response=credits,keywords,videos,images,release_dates,watch/providers,external_ids
    &include_image_language=ru,en,null
```

TMDB поддерживает добавление нескольких дочерних ресурсов в запрос деталей через `append_to_response`, поэтому карточку можно загрузить одним HTTP-запросом. Параметр языка также влияет на изображения, поэтому отдельно передаётся `include_image_language`.

---

## Защита от одновременной загрузки

Если десять пользователей одновременно откроют новый фильм, TMDB должен получить только один запрос.

Использовать Redis-lock:

```text
key: movie-details:lock:{tmdbId}
value: requestId
TTL: 30 секунд
SET NX
```

Получивший lock запрос загружает данные.

Остальные запросы:

```text
1. Проверяют БД каждые 100–200 мс.
2. Завершаются, когда DetailsState становится Ready.
3. Максимальное ожидание — таймаут HTTP-запроса.
```

Удалять lock можно только при совпадении сохранённого `requestId`, чтобы один запрос не удалил lock другого.

Дополнительно внутри одного экземпляра приложения можно использовать `ConcurrentDictionary<long, Lazy<Task<Movie>>>`, чтобы не обращаться к Redis для одинаковых локальных запросов.

---

## Псевдокод сервиса

```csharp
public async Task<MovieDetailsDto> GetOrLoadAsync(
    long tmdbId,
    CancellationToken cancellationToken)
{
    var movie = await repository.GetFullAsync(tmdbId, cancellationToken);

    if (movie?.DetailsState == MovieDetailsState.Ready &&
        !movie.NeedsRefresh)
    {
        return mapper.Map(movie);
    }

    await using var handle = await lockService.TryAcquireAsync(
        $"movie-details:{tmdbId}",
        TimeSpan.FromSeconds(30),
        cancellationToken);

    if (handle is null)
    {
        movie = await waitService.WaitUntilReadyAsync(
            tmdbId,
            cancellationToken);

        return mapper.Map(movie);
    }

    // Повторная проверка обязательна.
    movie = await repository.GetFullAsync(tmdbId, cancellationToken);

    if (movie?.DetailsState == MovieDetailsState.Ready &&
        !movie.NeedsRefresh)
    {
        return mapper.Map(movie);
    }

    await repository.MarkLoadingAsync(tmdbId, cancellationToken);

    try
    {
        var payload = await tmdbClient.GetMovieDetailsAsync(
            tmdbId,
            cancellationToken);

        await repository.SaveFullMovieAsync(
            payload,
            cancellationToken);

        movie = await repository.GetFullAsync(
            tmdbId,
            cancellationToken);

        return mapper.Map(movie);
    }
    catch (Exception exception)
    {
        await repository.MarkFailedAsync(
            tmdbId,
            exception.Message,
            cancellationToken);

        throw;
    }
}
```

---

## Транзакционное сохранение

`SaveFullMovieAsync` выполняется одной транзакцией:

```text
1. Upsert Movies.
2. Удалить старые MovieGenres и вставить актуальные.
3. Заменить MovieKeywords.
4. Заменить MovieCredits.
5. Заменить MovieVideos.
6. Заменить MovieImages.
7. Заменить MovieWatchProviders.
8. Сохранить исходный JSON в TmdbPayload.
9. DetailsState = Ready.
10. DetailsLoadedAt устанавливается только при первой загрузке.
11. DetailsUpdatedAt устанавливается при каждой синхронизации.
12. NeedsRefresh = false.
```

Пользователь не должен получить частично записанную карточку.

---

## Обработка ошибок

### TMDB недоступен, полных данных ещё нет

```text
DetailsState = Failed
LoadFailedAt = now
LastLoadError = текст ошибки
```

Возвращается `503 Service Unavailable`.

Повторную попытку разрешать не раньше чем через 1–5 минут, чтобы не отправлять одинаковые запросы при каждой перезагрузке страницы.

### TMDB недоступен, но в БД есть старая карточка

Вернуть старые данные из БД. Ошибка обновления не должна ломать карточку.

### TMDB вернул `404`

Сохранить:

```text
Status = Deleted
DetailsState = Failed
```

Повторно проверять такой фильм не чаще одного раза в 30 дней.

### TMDB вернул `429`

Соблюдать `Retry-After`, применять exponential backoff и ограничивать общий параллелизм через `SemaphoreSlim`. TMDB сохраняет защитные ограничения запросов и требует корректно обрабатывать `429`.

---

## Обновление данных

После первой загрузки карточка всегда отдаётся из БД.

Для актуализации используется ежедневный job:

```text
1. Получить список изменённых TMDB movie ID.
2. Найти среди них фильмы, сохранённые в SwapKino.
3. Установить NeedsRefresh = true.
4. Фоново обновить популярные фильмы.
5. Остальные обновить при следующем открытии карточки.
```

TMDB предоставляет списки изменённых идентификаторов за последние 24 часа с возможностью запросить период до 14 дней.

Дополнительный fallback:

```text
если DetailsUpdatedAt старше 30 дней:
    NeedsRefresh = true
```

---

## Разделение статических и динамических данных

Статические данные обновляются редко:

```text
название;
описание;
жанры;
keywords;
актёры;
режиссёр;
длительность;
дата выхода;
изображения.
```

Динамические данные обновляются чаще:

```text
VoteAverage;
VoteCount;
Popularity;
WatchProviders.
```

Их можно обновлять отдельным лёгким запросом каждые 1–7 дней для фильмов, которые часто показываются в рекомендациях.

---

## Итоговый поток

```text
TMDB discover/recommendations
→ Movie Summary upsert
→ пользователь открывает карточку
→ PostgreSQL Ready?
    ├── да → вернуть из БД
    └── нет → distributed lock
              → TMDB details
              → transaction upsert
              → вернуть из БД
```

Ключевое правило: **после первой успешной загрузки карточка никогда не зависит от доступности TMDB — TMDB используется только для первоначального заполнения и последующей синхронизации.**
