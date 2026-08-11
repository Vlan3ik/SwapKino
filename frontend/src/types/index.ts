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

export interface MovieFeedItem {
  kind: "movie";
  movie: Movie;
}

export interface TasteProbeFeedItem {
  kind: "taste_probe";
  probeId: string;
  movieId: number;
  prompt: string;
  options: Array<"more_like_this" | "less_like_this" | "not_for_me" | "already_watched" | "rate_inline">;
}

export type FeedItem = MovieFeedItem | TasteProbeFeedItem;

export interface FilmReel {
  id: string;
  slug: string;
  title: string;
  subtitle: string;
  genres: GenreName[];
  strategy?: string;
  coverUrl?: string | null;
}
