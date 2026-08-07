import { create } from "zustand";
import { api, getToken, mapApiMovie, moviePageItems, setToken } from "@/lib/api";
import type { Movie } from "@/types";

export type View =
  | { name: "feed" }
  | { name: "catalog" }
  | { name: "profile" }
  | { name: "favorites" }
  | { name: "ratings" }
  | { name: "license" }
  | { name: "privacy" }
  | { name: "terms" }
  | { name: "movie"; movieId: number; isSeries?: boolean };

export interface User {
  id: string;
  username: string;
  email: string;
  createdAt: number;
}

export function viewPath(view: View): string {
  switch (view.name) {
    case "feed": return "/";
    case "catalog": return "/catalog";
    case "favorites": return "/favorites";
    case "ratings": return "/ratings";
    case "profile": return "/profile";
    case "license": return "/license";
    case "privacy": return "/privacy";
    case "terms": return "/terms";
    case "movie": return `/movie/${view.movieId}${view.isSeries ? "?series=1" : ""}`;
  }
}

export function viewFromLocation(): View {
  if (typeof window === "undefined") return { name: "feed" };
  const path = window.location.pathname.replace(/\/+$/, "") || "/";
  const movieMatch = path.match(/^\/movie\/(\d+)$/);
  if (movieMatch) return { name: "movie", movieId: Number(movieMatch[1]), isSeries: new URLSearchParams(window.location.search).get("series") === "1" };
  const names: Record<string, View["name"]> = {
    "/catalog": "catalog", "/favorites": "favorites", "/ratings": "ratings",
    "/profile": "profile", "/license": "license", "/privacy": "privacy", "/terms": "terms",
  };
  return names[path] ? { name: names[path] as Exclude<View["name"], "movie"> } : { name: "feed" };
}

interface AppState {
  view: View;
  movies: Movie[];
  favorites: string[];
  ratings: Record<string, number>;
  activeReelId: string | null;
  history: View[];
  user: User | null;
  token: string | null;
  loadingMovies: boolean;
  loadingMoreMovies: boolean;
  catalogPage: number;
  catalogTotalPages: number;
  catalogTotalCount: number;
  catalogHasMore: boolean;
  catalogNextCursor: string | null;
  catalogError: string | null;
  hydrated: boolean;

  setView: (view: View) => void;
  goBack: () => void;
  openMovie: (movieId: number, isSeries?: boolean) => void;
  loadMovie: (movieId: number, isSeries?: boolean) => Promise<void>;
  syncViewFromUrl: () => void;
  loadMovies: (query?: string) => Promise<void>;
  loadCatalogPage: (page: number, query?: string) => Promise<void>;
  loadMoreMovies: (query?: string) => Promise<boolean>;
  restoreSession: () => Promise<void>;
  refreshLibrary: () => Promise<void>;
  toggleFavorite: (movieId: number, isSeries?: boolean) => void;
  isFavorite: (movieId: number, isSeries?: boolean) => boolean;
  setRating: (movieId: number, rating: number, isSeries?: boolean) => void;
  removeRating: (movieId: number, isSeries?: boolean) => void;
  getRating: (movieId: number, isSeries?: boolean) => number | null;
  setActiveReel: (reelId: string | null) => void;
  register: (data: { username: string; email: string; password: string }) => Promise<{ ok: true } | { ok: false; error: string }>;
  login: (identifier: string, password: string) => Promise<{ ok: true } | { ok: false; error: string }>;
  logout: () => void;
}

function userFromApi(user: { id: string; email: string; displayName?: string | null; createdAt?: string | null }): User {
  return { id: user.id, email: user.email, username: user.displayName || user.email, createdAt: user.createdAt ? Date.parse(user.createdAt) : Date.now() };
}

export function contentKey(movieId: number, isSeries = false) {
  return `${isSeries ? "series" : "movie"}:${movieId}`;
}

export function parseContentKey(key: string) {
  const [type, rawId] = key.split(":");
  return { movieId: Number(rawId), isSeries: type === "series" };
}

function actionKey(action: string, movieId: number, isSeries = false) {
  return `${action}:${contentKey(movieId, isSeries)}:${Date.now()}:${Math.random().toString(36).slice(2)}`;
}

async function syncLibrary(set: (partial: Partial<AppState>) => void, currentMovies: Movie[]) {
  const library = await api.library();
  const libraryMovies = library.items
    .map((item) => item.movie)
    .filter((movie): movie is NonNullable<typeof movie> => Boolean(movie))
    .map(mapApiMovie);
  set({
    movies: (() => {
      const merged = new Map(currentMovies.map((movie) => [contentKey(movie.id, movie.type === "series"), movie]));
      for (const movie of libraryMovies) merged.set(contentKey(movie.id, movie.type === "series"), movie);
      return [...merged.values()];
    })(),
    favorites: library.items.filter((item) => item.favorite ?? item.action === "favorite").map((item) => contentKey(item.tmdbId, item.isSeries)),
    ratings: Object.fromEntries(library.items.filter((item) => (item.rating ?? item.value) != null && (!item.action || item.action === "rate" || item.action === "rating")).map((item) => [contentKey(item.tmdbId, item.isSeries), (item.rating ?? item.value) as number])),
  });
}

const INITIAL_CATALOG_PAGES = 3;
const MAX_RESTORE_PAGES = 20;

export const useAppStore = create<AppState>((set, get) => ({
  view: { name: "feed" },
  movies: [],
  favorites: [],
  ratings: {},
  activeReelId: null,
  history: [],
  user: null,
  token: getToken(),
  loadingMovies: false,
  loadingMoreMovies: false,
  catalogPage: 0,
  catalogTotalPages: 1,
  catalogTotalCount: 0,
  catalogHasMore: true,
  catalogNextCursor: null,
  catalogError: null,
  hydrated: false,

  setView: (view) => {
    if (typeof window !== "undefined") window.history.pushState({}, "", viewPath(view));
    set((state) => ({ view, history: [...state.history, state.view].slice(-20), activeReelId: null }));
  },
  goBack: () => {
    if (typeof window !== "undefined" && window.history.length > 1) window.history.back();
    else get().setView({ name: "feed" });
  },
  openMovie: (movieId, isSeries = false) => {
    const view = { name: "movie", movieId, isSeries } as const;
    if (typeof window !== "undefined") window.history.pushState({}, "", viewPath(view));
    set((state) => ({ view, history: [...state.history, state.view].slice(-20), activeReelId: null }));
  },
  loadMovie: async (movieId, isSeries = false) => {
    const movie = mapApiMovie(await api.movie(movieId, isSeries));
    const key = contentKey(movie.id, movie.type === "series");
    set((state) => ({ movies: state.movies.some((item) => contentKey(item.id, item.type === "series") === key) ? state.movies.map((item) => contentKey(item.id, item.type === "series") === key ? movie : item) : [...state.movies, movie] }));
  },
  syncViewFromUrl: () => set({ view: viewFromLocation(), activeReelId: null }),

  loadMovies: async (query) => {
    set({ loadingMovies: true, catalogError: null });
    try {
      const response = await api.movies({ q: query, limit: 20 });
      const responseItems = moviePageItems(response);
      const rows = responseItems.map(mapApiMovie);
      const unique = [...new Map(rows.map((movie) => [contentKey(movie.id, movie.type === "series"), movie])).values()];
      set({ movies: unique, catalogPage: 1, catalogTotalPages: response.totalPages ?? 1, catalogTotalCount: response.totalCount, catalogHasMore: Boolean(response.nextCursor), catalogNextCursor: response.nextCursor ?? null });

      // Первую карточку показываем сразу, а ещё две страницы спокойно
      // догружаем после отрисовки. Это не блокирует первый экран и сохраняет
      // разнообразие киноплёнок в фоне.
      if (typeof window !== "undefined") {
        window.setTimeout(() => {
          void (async () => {
            for (let page = 1; page < INITIAL_CATALOG_PAGES; page++) {
              if (!(await get().loadMoreMovies(query))) break;
            }
          })();
        }, 180);
      }
    } catch (error) {
      set({ catalogError: error instanceof Error ? error.message : "Не удалось загрузить фильмы" });
      throw error;
    } finally {
      set({ loadingMovies: false });
    }
  },

  loadCatalogPage: async (page, query) => {
    set({ loadingMovies: true, catalogError: null });
    try {
      const targetPage = Math.min(MAX_RESTORE_PAGES, Math.max(1, Math.floor(page)));
      let cursor: string | null = null;
      let totalCount = 0;
      let totalPages = 1;
      let loadedPage = 0;
      const rows: Movie[] = [];
      for (let currentPage = 1; currentPage <= targetPage; currentPage += 1) {
        const response = await api.movies({ cursor, q: query, limit: 20 });
        rows.push(...moviePageItems(response).map(mapApiMovie));
        totalCount = response.totalCount;
        totalPages = response.totalPages ?? totalPages;
        cursor = response.nextCursor ?? null;
        loadedPage = currentPage;
        if (!cursor) break;
      }
      const unique = [...new Map(rows.map((movie) => [contentKey(movie.id, movie.type === "series"), movie])).values()];
      set({ movies: unique, catalogPage: loadedPage, catalogTotalPages: totalPages, catalogTotalCount: totalCount, catalogHasMore: Boolean(cursor), catalogNextCursor: cursor });
    } catch (error) {
      set({ catalogError: error instanceof Error ? error.message : "Не удалось загрузить страницу каталога" });
    } finally {
      set({ loadingMovies: false });
    }
  },

  loadMoreMovies: async (query) => {
    const state = get();
    if (state.loadingMoreMovies || !state.catalogHasMore) return false;
    const cursor = state.catalogNextCursor;
    if (!cursor) return false;
    const page = state.catalogPage + 1;
    set({ loadingMoreMovies: true, catalogError: null });
    try {
      const response = await api.movies({ cursor, q: query, limit: 20 });
      const incoming = moviePageItems(response).map(mapApiMovie);
      const known = new Set(get().movies.map((movie) => contentKey(movie.id, movie.type === "series")));
      const unique = incoming.filter((movie) => !known.has(contentKey(movie.id, movie.type === "series")));
      set({
        movies: [...get().movies, ...unique],
        catalogPage: page,
        catalogHasMore: Boolean(response.nextCursor),
        catalogNextCursor: response.nextCursor ?? null,
      });
      return unique.length > 0;
    } catch (error) {
      set({ catalogError: error instanceof Error ? error.message : "Не удалось загрузить следующую партию" });
      return false;
    } finally {
      set({ loadingMoreMovies: false });
    }
  },

  restoreSession: async () => {
    const token = getToken();
    if (!token) {
      set({ token: null, hydrated: true });
      await get().loadMovies();
      return;
    }
    set({ token });
    try {
      const user = await api.me();
      set({ user: userFromApi(user) });
      // Сначала загружаем каталог, затем добавляем в него все карточки из
      // библиотеки пользователя. Иначе loadMovies перезаписывал импортированные
      // фильмы и на экране оставались только случайные совпадения с каталогом.
      await get().loadMovies().catch(() => undefined);
      await syncLibrary(set, get().movies);
    } catch {
      setToken(null);
      set({ token: null, user: null, favorites: [], ratings: {} });
    } finally {
      set({ hydrated: true });
    }
  },

  refreshLibrary: async () => {
    if (!get().token) return;
    await syncLibrary(set, get().movies);
  },

  toggleFavorite: (movieId, isSeries = false) => {
    const key = contentKey(movieId, isSeries);
    const favorite = !get().favorites.includes(key);
    set((state) => ({ favorites: favorite ? [...state.favorites, key] : state.favorites.filter((id) => id !== key) }));
    if (get().token) void api.action({ tmdbId: movieId, isSeries, actionType: favorite ? "favorite" : "unfavorite", idempotencyKey: actionKey("favorite", movieId, isSeries) }).catch(() => undefined);
  },
  isFavorite: (movieId, isSeries = false) => get().favorites.includes(contentKey(movieId, isSeries)),
  setRating: (movieId, rating, isSeries = false) => {
    const value = Math.max(1, Math.min(10, Math.round(rating)));
    const key = contentKey(movieId, isSeries);
    set((state) => ({ ratings: { ...state.ratings, [key]: value } }));
    if (get().token) void api.action({ tmdbId: movieId, isSeries, actionType: "rate", value, idempotencyKey: actionKey("rate", movieId, isSeries) }).catch(() => undefined);
  },
  removeRating: (movieId, isSeries = false) => {
    const key = contentKey(movieId, isSeries);
    set((state) => { const next = { ...state.ratings }; delete next[key]; return { ratings: next }; });
    if (get().token) void api.action({ tmdbId: movieId, isSeries, actionType: "unrate", idempotencyKey: actionKey("unrate", movieId, isSeries) }).catch(() => undefined);
  },
  getRating: (movieId, isSeries = false) => get().ratings[contentKey(movieId, isSeries)] ?? null,
  setActiveReel: (reelId) => set({ activeReelId: reelId }),

  register: async ({ username, email, password }) => {
    try {
      const response = await api.register({ email: email.trim().toLowerCase(), password, displayName: username.trim() });
      setToken(response.accessToken);
      set({ token: response.accessToken, user: userFromApi(response.user) });
      await get().loadMovies();
      return { ok: true };
    } catch (error) { return { ok: false, error: error instanceof Error ? error.message : "Не удалось зарегистрироваться" }; }
  },
  login: async (identifier, password) => {
    try {
      const response = await api.login({ email: identifier.trim(), password });
      setToken(response.accessToken);
      set({ token: response.accessToken, user: userFromApi(response.user) });
      await get().loadMovies();
      await syncLibrary(set, get().movies);
      return { ok: true };
    } catch (error) { return { ok: false, error: error instanceof Error ? error.message : "Не удалось войти" }; }
  },
  logout: () => { void api.logout().catch(() => undefined); setToken(null); set({ user: null, token: null, favorites: [], ratings: {} }); void get().loadMovies(); },
}));
