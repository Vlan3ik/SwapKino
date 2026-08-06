// Типы данных СвайпКино

export type Genre =
  | "Ужасы"
  | "Драма"
  | "Фантастика"
  | "Боевик"
  | "Триллер"
  | "Криминал"
  | "Комедия"
  | "Романтика"
  | "Эпик"
  | "Сериал";

export interface Person {
  id: number;
  name: string;
  role: string;
  photoUrl?: string;
}

export interface Movie {
  id: number;
  title: string;
  originalTitle: string;
  year: number;
  genres: Genre[];
  rating: number;
  duration: number;
  posterUrl: string;
  backdropUrl: string;
  shortDescription: string;
  description: string;
  trailerYoutubeId: string;
  watchUrl: string;
  cast: Person[];
  directors: Person[];
  writers: Person[];
  type: "film" | "series";
}

export interface FilmReel {
  id: string;
  title: string;
  subtitle: string;
  genre: Genre;
  movieIds: number[];
}
