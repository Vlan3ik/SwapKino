import type { FilmReel, Genre, Movie, Person } from "@/types";

const API_BASE = process.env.NEXT_PUBLIC_API_URL ?? "/api/v1";
const TOKEN_KEY = "swapkino-access-token";

export interface ApiUser {
  id: string;
  email: string;
  displayName?: string | null;
  avatarUrl?: string | null;
  createdAt?: string | null;
}

export interface ApiLibraryItem {
  id?: string;
  tmdbId: number;
  action?: string;
  value?: number | null;
  createdAt?: string;
  isSeries?: boolean;
  movie?: ApiMovie | null;
  rating?: number | null;
  favorite?: boolean;
  watched?: boolean;
  suppressedUntil?: string | null;
}

export interface ApiLibraryPage {
  items: ApiLibraryItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasNextPage: boolean;
  nextCursor?: string | null;
}

export interface LibraryQuery {
  cursor?: string | null;
  page?: number;
  limit?: number;
  q?: string;
  genreIds?: number[];
  minRating?: number;
  yearFrom?: number;
  yearTo?: number;
  isSeries?: boolean;
  sort?: "recent" | "oldest" | "rating" | "title" | "newest";
  signal?: AbortSignal;
}

export interface ApiProfile {
  user: ApiUser;
  statistics: {
    favoritesCount: number;
    ratingsCount: number;
    watchedCount: number;
    libraryCount: number;
    averageRating: number;
  };
  previews: { favorites: ApiLibraryItem[]; ratings: ApiLibraryItem[] };
}

export interface ApiMovie {
  id: number;
  tmdbId: number;
  title: string;
  originalTitle?: string | null;
  overview?: string | null;
  releaseDate?: string | null;
  runtime?: number | null;
  isSeries?: boolean;
  rating?: number | null;
  voteCount?: number;
  posterUrl?: string | null;
  backdropUrl?: string | null;
  detailsState?: string;
  tagline?: string | null;
  genres?: Array<{ id: number; slug?: string | null; name: string }>;
  cast?: ApiPerson[];
  directors?: ApiPerson[];
  writers?: ApiPerson[];
  trailerYoutubeId?: string | null;
  watchUrl?: string | null;
  images?: unknown[];
  crew?: unknown[];
  trailers?: unknown[];
  payload?: Record<string, unknown> | null;
}

export interface ApiMoviePlayer {
  provider: string;
  name: string;
  embedUrl: string | null;
  embed?: { publisherId: string; type: string; id: string } | null;
  status?: string;
  available: boolean;
}

export interface ApiMoviePlayersResponse {
  items: ApiMoviePlayer[];
}

interface ApiPerson { id: number; name: string; role?: string | null; character?: string | null; photoUrl?: string | null; profile_path?: string | null }

export interface AuthResponse {
  accessToken: string;
  user: ApiUser;
}

export interface ImportCaptcha {
  code: string;
  message?: string | null;
  pageUrl?: string | null;
  screenshotBase64?: string | null;
  screenshotMimeType?: string | null;
  expiresInSeconds?: number | null;
  action?: string | null;
  resumeEndpoint?: string | null;
  novncUrl?: string | null;
}

export interface ImportStatus {
  id?: string;
  status: string;
  progress: number;
  importedCount?: number;
  phase?: string | null;
  phaseProgress?: number;
  overallProgress?: number;
  etaSeconds?: number | null;
  estimatedRemainingSeconds?: number | null;
  pagesProcessed?: number;
  pagesTotal?: number | null;
  discoveredCount?: number;
  matchedCount?: number;
  appliedCount?: number;
  unmatchedCount?: number;
  error?: string | null;
  captcha?: ImportCaptcha | null;
}

export interface MoviePage {
  page?: number;
  pageSize?: number;
  totalCount: number;
  totalPages?: number;
  hasNextPage?: boolean;
  results?: ApiMovie[];
  items?: ApiMovie[];
  nextCursor?: string | null;
}

export interface CatalogQuery {
  cursor?: string | null;
  page?: number;
  limit?: number;
  q?: string;
  genreIds?: number[];
  minRating?: number;
  yearFrom?: number;
  yearTo?: number;
  isSeries?: boolean;
  sort?: string;
  signal?: AbortSignal;
}

export interface ApiReel {
  id?: string;
  slug: string;
  title: string;
  description?: string | null;
  subtitle?: string | null;
  genres?: Array<{ id: number; slug?: string | null; name: string }>;
  strategy?: string | null;
  coverUrl?: string | null;
}

export interface ReelFeed {
  reel: ApiReel;
  feedSessionId?: string;
  items: ApiMovie[];
  results?: ApiMovie[];
  nextCursor?: string | null;
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

async function request<T>(path: string, init: RequestInit = {}, allowRefresh = true): Promise<T> {
  const headers = new Headers(init.headers);
  headers.set("Accept", "application/json");
  if (init.body && !headers.has("Content-Type") && !(typeof FormData !== "undefined" && init.body instanceof FormData)) {
    headers.set("Content-Type", "application/json");
  }
  const token = getToken();
  if (token) headers.set("Authorization", `Bearer ${token}`);

  const response = await fetch(`${API_BASE}${path}`, {
    ...init,
    headers,
    credentials: "include",
    cache: "no-store",
  });
  if (response.status === 401 && allowRefresh && !path.startsWith("/auth/")) {
    try {
      const refreshed = await request<AuthResponse>("/auth/refresh", { method: "POST" }, false);
      setToken(refreshed.accessToken);
      return request<T>(path, init, false);
    } catch {
      setToken(null);
    }
  }
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
    const identityErrors = typeof data === "object" && data && "errors" in data && Array.isArray((data as { errors: unknown }).errors)
      ? ((data as { errors: Array<{ code?: string; description?: string }> }).errors)
      : null;
    const identityMessages = identityErrors?.map((error) => ({
      PasswordTooShort: "пароль должен содержать минимум 8 символов",
      PasswordRequiresNonAlphanumeric: "добавь специальный символ",
      PasswordRequiresLower: "добавь строчную букву",
      PasswordRequiresUpper: "добавь заглавную букву",
      PasswordRequiresDigit: "добавь цифру",
      DuplicateUserName: "пользователь с таким email уже зарегистрирован",
    }[error.code ?? ""] ?? error.description ?? "проверь введённые данные")).filter(Boolean);
    const message =
      identityMessages && identityMessages.length > 0
        ? identityMessages.join("; ")
        : typeof data === "object" && data && "message" in data
        ? String((data as { message: unknown }).message)
        : Array.isArray(data) && data.length > 0 && typeof data[0] === "object" && data[0] && "description" in data[0]
        ? data.map((item) => String((item as { description: unknown }).description)).join(" ")
        : `Ошибка API (${response.status})`;
    const error = new Error(message) as Error & { status: number; data: unknown };
    error.status = response.status;
    error.data = data;
    throw error;
  }
  return data as T;
}

export const api = {
  register: (payload: { email: string; password: string; displayName: string; privacyConsent: boolean }) =>
    request<AuthResponse>("/auth/register", { method: "POST", body: JSON.stringify(payload) }),
  login: (payload: { email: string; password: string }) =>
    request<AuthResponse>("/auth/login", { method: "POST", body: JSON.stringify(payload) }),
  me: () => request<ApiUser>("/auth/me"),
  profile: () => request<ApiProfile>("/profile"),
  updateProfile: (payload: { displayName?: string; avatarUrl?: string }) =>
    request<ApiUser>("/profile", { method: "PATCH", body: JSON.stringify(payload) }),
  uploadAvatar: async (file: File) => {
    const body = new FormData(); body.append("file", file);
    return request<ApiUser>("/profile/avatar", { method: "POST", body });
  },
  deleteAvatar: () => request<void>("/profile/avatar", { method: "DELETE" }),
  changePassword: (payload: { currentPassword: string; newPassword: string }) =>
    request<void>("/auth/password", { method: "POST", body: JSON.stringify(payload) }),
  movies: (query: CatalogQuery | number = {}) => {
    const normalized: CatalogQuery = typeof query === "number" ? { cursor: query > 1 ? String(query) : undefined } : query;
    const params = new URLSearchParams();
    if (normalized.cursor) params.set("cursor", normalized.cursor);
    if (normalized.page != null) params.set("page", String(normalized.page));
    params.set("limit", String(normalized.limit ?? 20));
    if (normalized.q) params.set("q", normalized.q);
    if (normalized.genreIds?.length) params.set("genreIds", normalized.genreIds.join(","));
    if (normalized.minRating != null) params.set("minRating", String(normalized.minRating));
    if (normalized.yearFrom != null) params.set("yearFrom", String(normalized.yearFrom));
    if (normalized.yearTo != null) params.set("yearTo", String(normalized.yearTo));
    if (normalized.isSeries != null) params.set("isSeries", String(normalized.isSeries));
    if (normalized.sort) params.set("sort", ({ "rating-desc": "rating", "year-desc": "newest", "year-asc": "oldest" } as Record<string, string>)[normalized.sort] ?? normalized.sort);
    return request<MoviePage>(`/movies?${params}`, { signal: normalized.signal });
  },
  movie: (id: number, isSeries = false) => request<ApiMovie>(`/movies/${id}${isSeries ? "?isSeries=true" : ""}`),
  moviePlayers: (id: number, isSeries = false) =>
    request<ApiMoviePlayersResponse>(`/movies/${id}/players${isSeries ? "?isSeries=true" : ""}`),
  recommendations: (page = 1) =>
    request<{ page: number; results: ApiMovie[] }>(`/recommendations?page=${page}`),
  library: () => request<{ items: ApiLibraryItem[] }>("/library"),
  favorites: (query: LibraryQuery = {}) => request<ApiLibraryPage>(`/favorites?${libraryParams(query)}`, { signal: query.signal }),
  ratings: (query: LibraryQuery = {}) => request<ApiLibraryPage>(`/ratings?${libraryParams(query)}`, { signal: query.signal }),
  reels: () => request<{ items?: ApiReel[]; results?: ApiReel[] } | ApiReel[]>("/reels"),
  reelFeed: (slug: string, cursor?: string | null, limit = 20) => {
    const params = new URLSearchParams({ limit: String(limit) });
    if (cursor) params.set("cursor", cursor);
    return request<ReelFeed>(`/reels/${encodeURIComponent(slug)}/feed?${params}`);
  },
  action: (payload: {
    tmdbId: number;
    isSeries?: boolean;
    actionType: string;
    value?: number;
    idempotencyKey: string;
  }) => request<{ id: string; duplicate: boolean }>("/actions", { method: "POST", body: JSON.stringify(payload) }),
  importProfile: (profileUrl: string) =>
    request<{ id: string; status: string; progress: number }>("/imports", {
      method: "POST",
      body: JSON.stringify({ profileUrl }),
    }),
  importStatus: (id: string) => request<ImportStatus>(`/imports/${id}`),
  importResume: (id: string) => request<ImportStatus>(`/imports/${id}/resume`, { method: "POST" }),
  importCancel: (id: string) => request<ImportStatus>(`/imports/${id}/cancel`, { method: "POST" }),
  logout: () => request<void>("/auth/logout", { method: "POST" }),
  deleteAccount: () => request<void>("/account", { method: "DELETE" }),
};

function libraryParams(query: LibraryQuery): URLSearchParams {
  const params = new URLSearchParams();
  if (query.cursor) params.set("cursor", query.cursor);
  params.set("page", String(query.page ?? 1));
  params.set("limit", String(query.limit ?? 20));
  if (query.q) params.set("q", query.q);
  if (query.genreIds?.length) params.set("genreIds", query.genreIds.join(","));
  if (query.minRating != null) params.set("minRating", String(query.minRating));
  if (query.yearFrom != null) params.set("yearFrom", String(query.yearFrom));
  if (query.yearTo != null) params.set("yearTo", String(query.yearTo));
  if (query.isSeries != null) params.set("isSeries", String(query.isSeries));
  if (query.sort) params.set("sort", query.sort);
  return params;
}

export function mapApiMovie(movie: ApiMovie): Movie {
  const payload = movie.payload ?? {};
  const genreNames: Record<number, string> = { 28: "Боевик", 18: "Драма", 35: "Комедия", 53: "Триллер", 80: "Криминал", 878: "Фантастика", 10749: "Романтика", 27: "Ужасы", 12: "Приключения", 14: "Фэнтези", 16: "Мультфильмы", 9648: "Детектив", 10751: "Семейный", 36: "История", 10752: "Военный", 10402: "Музыка", 99: "Документальный" };
  const canonicalGenres: Genre[] = Array.isArray(movie.genres)
    ? movie.genres.map((genre) => ({ id: genre.id, slug: genre.slug || String(genre.id), name: genre.name }))
    : [];
  const rawGenreNames = Array.isArray(payload.genres)
    ? payload.genres.map((genre) => (typeof genre === "object" && genre && "name" in genre ? String(genre.name) : "")).filter(Boolean)
    : Array.isArray(payload.genre_ids) ? payload.genre_ids.map((id) => genreNames[Number(id)]).filter(Boolean) : [];
  const genreItems = canonicalGenres.length > 0
    ? canonicalGenres
    : rawGenreNames.map((name, index) => ({ id: -(index + 1), slug: name.toLowerCase().replace(/\s+/g, "-"), name }));
  const credits = typeof payload.credits === "object" && payload.credits ? payload.credits as { cast?: unknown[]; crew?: unknown[] } : {};
  const mapPerson = (person: ApiPerson, fallback: string): Person => ({ id: person.id ?? 0, name: person.name ?? "", role: person.role || person.character || fallback, photoUrl: person.photoUrl ?? (person.profile_path ? `https://image.tmdb.org/t/p/w185${person.profile_path}` : null) });
  const cast = movie.cast?.map((person) => mapPerson(person, "Актёр")) ?? (Array.isArray(credits.cast) ? credits.cast.slice(0, 8).map((person) => {
    const item = person as { id?: number; name?: string; character?: string; profile_path?: string };
    return { id: item.id ?? 0, name: item.name ?? "", role: item.character ?? "Актёр", photoUrl: item.profile_path ? `https://image.tmdb.org/t/p/w185${item.profile_path}` : null };
  }) : []);
  const crew = movie.crew ?? (Array.isArray(credits.crew) ? credits.crew : []);
  const people = crew.map((person) => person as { id?: number; name?: string; job?: string });
  const directors = movie.directors?.map((person) => mapPerson(person, "Режиссёр")) ?? people.filter((person) => person.job === "Director").map((person) => ({ id: person.id ?? 0, name: person.name ?? "", role: "Режиссёр" }));
  const writers = movie.writers?.map((person) => mapPerson(person, "Сценарий")) ?? people.filter((person) => person.job === "Writer" || person.job === "Screenplay").map((person) => ({ id: person.id ?? 0, name: person.name ?? "", role: "Сценарий" }));
  const videos = typeof payload.videos === "object" && payload.videos ? payload.videos as { results?: unknown[] } : {};
  const videoRows = movie.trailers ?? (Array.isArray(videos.results) ? videos.results : []);
  const trailer = videoRows.map((video) => video as { site?: string; type?: string; key?: string }).find((video) => video.site === "YouTube" && video.type === "Trailer");
  const parsedYear = movie.releaseDate ? Number(movie.releaseDate.slice(0, 4)) : NaN;
  const images = (movie.images ?? []).map((image) => {
    if (typeof image === "string") return image;
    if (typeof image === "object" && image && "file_path" in image) return `https://image.tmdb.org/t/p/original${String((image as { file_path: unknown }).file_path)}`;
    return null;
  }).filter((image): image is string => Boolean(image));
  return {
    id: movie.tmdbId ?? movie.id,
    title: movie.title,
    originalTitle: movie.originalTitle && movie.originalTitle !== movie.title ? movie.originalTitle : null,
    year: Number.isFinite(parsedYear) ? parsedYear : null,
    genres: genreItems.map((genre) => genre.name),
    genreItems,
    rating: movie.rating && movie.rating > 0 ? movie.rating : null,
    duration: movie.runtime && movie.runtime > 0 ? movie.runtime : null,
    posterUrl: movie.posterUrl ?? null,
    backdropUrl: movie.backdropUrl ?? movie.posterUrl ?? null,
    shortDescription: movie.tagline?.trim() || null,
    description: movie.overview?.trim() || null,
    trailerYoutubeId: movie.trailerYoutubeId ?? trailer?.key ?? null,
    watchUrl: movie.watchUrl ?? null,
    cast,
    directors,
    writers,
    images,
    type: movie.isSeries ? "series" : "film",
    detailsState: movie.detailsState ?? null,
  };
}

export function mapApiReel(reel: ApiReel): FilmReel {
  return {
    id: reel.id ?? reel.slug,
    slug: reel.slug,
    title: reel.title,
    subtitle: reel.description ?? reel.subtitle ?? "",
    genres: reel.genres?.map((genre) => genre.name) ?? [],
    strategy: reel.strategy ?? undefined,
    coverUrl: reel.coverUrl ?? null,
  };
}

export function moviePageItems(page: MoviePage): ApiMovie[] {
  return page.items ?? page.results ?? [];
}
