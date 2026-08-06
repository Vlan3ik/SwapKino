import type { FilmReel, Genre, Movie } from "@/types";

// Киноплёнки — это только тематические правила. Фильмы, постеры и описания
// всегда приходят из ASP.NET API; при нехватке жанра используется общий каталог.
export const filmReels: FilmReel[] = [
  { id: "no-sleep-tonight", title: "Ночь без сна", subtitle: "Когда хочется пощекотать нервы", genre: "Ужасы", movieIds: [] },
  { id: "plot-twist", title: "Поворот сюжета", subtitle: "Триллеры, после которых молчишь", genre: "Триллер", movieIds: [] },
  { id: "case-files", title: "Дело закрыто", subtitle: "Тайны, улики и неожиданные ответы", genre: "Детектив", movieIds: [] },
  { id: "dark-side", title: "Тёмная сторона", subtitle: "Криминальные истории без глянца", genre: "Криминал", movieIds: [] },
  { id: "adrenaline", title: "На предельной скорости", subtitle: "Боевики для тех, кто не любит паузы", genre: "Боевик", movieIds: [] },
  { id: "great-adventure", title: "Большое приключение", subtitle: "Дорога начинается с первого кадра", genre: "Приключения", movieIds: [] },
  { id: "other-worlds", title: "Миры за гранью", subtitle: "Фэнтези, где невозможное — норма", genre: "Фэнтези", movieIds: [] },
  { id: "space-signal", title: "Сигнал из космоса", subtitle: "Будущее, звёзды и чужие цивилизации", genre: "Фантастика", movieIds: [] },
  { id: "serious-conversation", title: "Разговор по душам", subtitle: "Драмы, которые остаются после титров", genre: "Драма", movieIds: [] },
  { id: "sparks", title: "Искры между строк", subtitle: "Истории о встречах и чувствах", genre: "Романтика", movieIds: [] },
  { id: "laugh-out-loud", title: "Смешно и точка", subtitle: "Комедии для перезагрузки", genre: "Комедия", movieIds: [] },
  { id: "whole-family", title: "Смотреть всей семьёй", subtitle: "Добрые истории для большого дивана", genre: "Семейный", movieIds: [] },
  { id: "animated-universe", title: "Вселенная на ладони", subtitle: "Анимация, которая не знает границ", genre: "Мультфильмы", movieIds: [] },
  { id: "pages-of-history", title: "Страницы истории", subtitle: "Прошлое, которое звучит современно", genre: "История", movieIds: [] },
  { id: "war-and-peace", title: "Война и мир", subtitle: "Большие события и человеческие судьбы", genre: "Военный", movieIds: [] },
  { id: "music-in-frame", title: "Музыка в кадре", subtitle: "Ритм, сцена и живые эмоции", genre: "Музыка", movieIds: [] },
  { id: "real-world", title: "Реальный мир", subtitle: "Документальные истории без фильтров", genre: "Документальный", movieIds: [] },
  { id: "golden-shelf", title: "С золотой полки", subtitle: "Фильмы, которые не стареют", genre: "Драма", movieIds: [] },
  { id: "late-night-comedy", title: "После полуночи", subtitle: "Странно, смешно и немного безумно", genre: "Комедия", movieIds: [] },
  { id: "epic-scale", title: "Крупный план", subtitle: "Истории с размахом большого экрана", genre: "Боевик", movieIds: [] },
];

export function getReelMovies(reel: FilmReel, catalog: Movie[]): Movie[] {
  const matching = catalog.filter((movie) => movie.genres.includes(reel.genre));
  return matching.length > 0 ? matching : catalog;
}

export const allGenres: Genre[] = [
  "Ужасы", "Драма", "Фантастика", "Боевик", "Триллер", "Криминал",
  "Комедия", "Романтика", "Приключения", "Фэнтези", "Мультфильмы",
  "Детектив", "Семейный", "История", "Военный", "Музыка", "Документальный",
];
