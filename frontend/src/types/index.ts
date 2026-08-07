export type GenreName = string;

export interface Genre {
  id: number;
  slug: string;
  name: GenreName;
}

export interface Person {
  id: number;
  name: string;
  role: string;
  photoUrl?: string | null;
}

export interface Movie {
  id: number;
  title: string;
  originalTitle: string | null;
  year: number | null;
  genres: GenreName[];
  genreItems: Genre[];
  rating: number | null;
  duration: number | null;
  posterUrl: string | null;
  backdropUrl: string | null;
  shortDescription: string | null;
  description: string | null;
  trailerYoutubeId: string | null;
  watchUrl: string | null;
  cast: Person[];
  directors: Person[];
  writers: Person[];
  images: string[];
  type: "film" | "series";
  detailsState?: string | null;
}

export interface FilmReel {
  id: string;
  slug: string;
  title: string;
  subtitle: string;
  genres: GenreName[];
  strategy?: string;
  coverUrl?: string | null;
}
