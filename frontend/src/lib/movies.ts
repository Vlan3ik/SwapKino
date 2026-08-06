import type { FilmReel } from "@/types";

// Подборки — это UI-конфигурация. Сами фильмы, постеры и описания всегда
// приходят из ASP.NET API, а не хранятся в браузере или исходниках frontend.
export const filmReels: FilmReel[] = [
  { id: "horror-evening", title: "Ужасы на вечер", subtitle: "Погрузись во тьму", genre: "Ужасы", movieIds: [] },
  { id: "drama-date", title: "Драма для свидания", subtitle: "Для долгих разговоров после", genre: "Драма", movieIds: [] },
  { id: "series-binge", title: "Залипнуть на сериалы", subtitle: "Один сезон — не повод остановиться", genre: "Сериал", movieIds: [] },
  { id: "scifi-night", title: "Фантастика на ночь", subtitle: "Космос, будущее и иные миры", genre: "Фантастика", movieIds: [] },
  { id: "golden-classics", title: "Золотая классика", subtitle: "То, что должен увидеть каждый", genre: "Драма", movieIds: [] },
  { id: "epic-evening", title: "Эпичное кино", subtitle: "Большой экран не помешает", genre: "Эпик", movieIds: [] },
  { id: "comedy-mood", title: "Комедия для настроения", subtitle: "Когда хочется улыбок", genre: "Комедия", movieIds: [] },
];

export const allGenres: string[] = [
  "Ужасы",
  "Драма",
  "Фантастика",
  "Боевик",
  "Триллер",
  "Криминал",
  "Комедия",
  "Романтика",
  "Эпик",
  "Сериал",
];
