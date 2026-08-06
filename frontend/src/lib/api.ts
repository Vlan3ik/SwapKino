import type { Movie } from "@/types";

const API_BASE = process.env.NEXT_PUBLIC_API_URL ?? "/api/v1";
const TOKEN_KEY = "swapkino-access-token";

export interface ApiUser {
  id: string;
  email: string;
  displayName?: string | null;
  createdAt?: string | null;
}

export interface ApiLibraryItem {
  id: string;
  tmdbId: number;
  action: string;
  value?: number | null;
  createdAt: string;
}

export interface ApiMovie {
  id: number;
  tmdbId: number;
  title: string;
  originalTitle?: string | null;
  overview?: string | null;
  releaseDate?: string | null;
  runtime?: number | null;
  rating: number;
  voteCount?: number;
  posterUrl?: string | null;
  backdropUrl?: string | null;
  detailsState?: string;
  payload?: Record<string, unknown> | null;
}

export interface AuthResponse {
  accessToken: string;
  user: ApiUser;
}

export function getToken(): string | null {
  if (typeof window === "undefined") return null;
  return window.localStorage.getItem(TOKEN_KEY);
}

export function setToken(token: string | null) {
  if (typeof window === "undefined") return;
  if (token) window.localStorage.setItem(TOKEN_KEY, token);
  else window.localStorage.removeItem(TOKEN_KEY);
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  headers.set("Accept", "application/json");
  if (init.body && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }
  const token = getToken();
  if (token) headers.set("Authorization", `Bearer ${token}`);

  const response = await fetch(`${API_BASE}${path}`, {
    ...init,
    headers,
    cache: "no-store",
  });
  const body = await response.text();
  let data: unknown = null;
  if (body) {
    try {
      data = JSON.parse(body);
    } catch {
      data = body;
    }
  }
  if (!response.ok) {
    const message =
      typeof data === "object" && data && "message" in data
        ? String((data as { message: unknown }).message)
        : `Ошибка API (${response.status})`;
    throw new Error(message);
  }
  return data as T;
}

export const api = {
  register: (payload: { email: string; password: string; displayName: string }) =>
    request<AuthResponse>("/auth/register", { method: "POST", body: JSON.stringify(payload) }),
  login: (payload: { email: string; password: string }) =>
    request<AuthResponse>("/auth/login", { method: "POST", body: JSON.stringify(payload) }),
  me: () => request<ApiUser>("/auth/me"),
  movies: (page = 1, query?: string) =>
    request<{ page: number; results: ApiMovie[] }>(
      `/movies?page=${page}${query ? `&q=${encodeURIComponent(query)}` : ""}`
    ),
  movie: (id: number) => request<ApiMovie>(`/movies/${id}`),
  recommendations: (page = 1) =>
    request<{ page: number; results: ApiMovie[] }>(`/recommendations?page=${page}`),
  library: () => request<{ items: ApiLibraryItem[] }>("/library"),
  action: (payload: {
    tmdbId: number;
    actionType: string;
    value?: number;
    idempotencyKey: string;
  }) => request<{ id: string; duplicate: boolean }>("/actions", { method: "POST", body: JSON.stringify(payload) }),
  importProfile: (profileUrl: string) =>
    request<{ id: string; status: string; progress: number }>("/imports", {
      method: "POST",
      body: JSON.stringify({ profileUrl }),
    }),
  importStatus: (id: string) => request<{ status: string; progress: number; error?: string }>(`/imports/${id}`),
};

export function mapApiMovie(movie: ApiMovie): Movie {
  const payload = movie.payload ?? {};
  const genreNames: Record<number, string> = { 28: "Боевик", 18: "Драма", 35: "Комедия", 53: "Триллер", 80: "Криминал", 878: "Фантастика", 10749: "Романтика", 27: "Ужасы" };
  const genres = Array.isArray(payload.genres)
    ? payload.genres.map((genre) => (typeof genre === "object" && genre && "name" in genre ? String(genre.name) : "")).filter(Boolean)
    : Array.isArray(payload.genre_ids) ? payload.genre_ids.map((id) => genreNames[Number(id)]).filter(Boolean) : [];
  const credits = typeof payload.credits === "object" && payload.credits ? payload.credits as { cast?: unknown[]; crew?: unknown[] } : {};
  const cast = Array.isArray(credits.cast) ? credits.cast.slice(0, 8).map((person) => {
    const item = person as { id?: number; name?: string; character?: string };
    return { id: item.id ?? 0, name: item.name ?? "", role: item.character ?? "Актёр" };
  }) : [];
  const crew = Array.isArray(credits.crew) ? credits.crew : [];
  const people = crew.map((person) => person as { id?: number; name?: string; job?: string });
  const directors = people.filter((person) => person.job === "Director").map((person) => ({ id: person.id ?? 0, name: person.name ?? "", role: "Режиссёр" }));
  const writers = people.filter((person) => person.job === "Writer" || person.job === "Screenplay").map((person) => ({ id: person.id ?? 0, name: person.name ?? "", role: "Сценарий" }));
  const videos = typeof payload.videos === "object" && payload.videos ? payload.videos as { results?: unknown[] } : {};
  const trailer = Array.isArray(videos.results) ? videos.results.map((video) => video as { site?: string; type?: string; key?: string }).find((video) => video.site === "YouTube" && video.type === "Trailer") : undefined;
  const year = movie.releaseDate ? Number(movie.releaseDate.slice(0, 4)) : 0;
  return {
    id: movie.tmdbId ?? movie.id,
    title: movie.title,
    originalTitle: movie.originalTitle ?? movie.title,
    year,
    genres: genres as Movie["genres"],
    rating: movie.rating ?? 0,
    duration: movie.runtime ?? 0,
    posterUrl: movie.posterUrl ?? "",
    backdropUrl: movie.backdropUrl ?? movie.posterUrl ?? "",
    shortDescription: movie.overview ?? "Описание пока недоступно.",
    description: movie.overview ?? "Описание пока недоступно.",
    trailerYoutubeId: trailer?.key ?? "",
    watchUrl: "https://wparty.ru/",
    cast,
    directors,
    writers,
    type: "film",
  };
}
