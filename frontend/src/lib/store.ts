import { create } from "zustand";
import { api, getToken, mapApiMovie, setToken } from "@/lib/api";
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
  | { name: "movie"; movieId: number };

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
    case "movie": return `/movie/${view.movieId}`;
  }
}

export function viewFromLocation(): View {
  if (typeof window === "undefined") return { name: "feed" };
  const path = window.location.pathname.replace(/\/+$/, "") || "/";
  const movieMatch = path.match(/^\/movie\/(\d+)$/);
  if (movieMatch) return { name: "movie", movieId: Number(movieMatch[1]) };
  const names: Record<string, View["name"]> = {
    "/catalog": "catalog", "/favorites": "favorites", "/ratings": "ratings",
    "/profile": "profile", "/license": "license", "/privacy": "privacy", "/terms": "terms",
  };
  return names[path] ? { name: names[path] as Exclude<View["name"], "movie"> } : { name: "feed" };
}

interface AppState {
  view: View;
  movies: Movie[];
  favorites: number[];
  ratings: Record<number, number>;
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
  catalogError: string | null;
  hydrated: boolean;

  setView: (view: View) => void;
  goBack: () => void;
  openMovie: (movieId: number) => void;
  loadMovie: (movieId: number) => Promise<void>;
  syncViewFromUrl: () => void;
  loadMovies: (query?: string) => Promise<void>;
  loadCatalogPage: (page: number, query?: string) => Promise<void>;
  loadMoreMovies: (query?: string) => Promise<boolean>;
  restoreSession: () => Promise<void>;
  toggleFavorite: (movieId: number) => void;
  isFavorite: (movieId: number) => boolean;
  setRating: (movieId: number, rating: number) => void;
  removeRating: (movieId: number) => void;
  getRating: (movieId: number) => number | null;
  setActiveReel: (reelId: string | null) => void;
  register: (data: { username: string; email: string; password: string }) => Promise<{ ok: true } | { ok: false; error: string }>;
  login: (identifier: string, password: string) => Promise<{ ok: true } | { ok: false; error: string }>;
  logout: () => void;
}

function userFromApi(user: { id: string; email: string; displayName?: string | null; createdAt?: string | null }): User {
  return { id: user.id, email: user.email, username: user.displayName || user.email, createdAt: user.createdAt ? Date.parse(user.createdAt) : Date.now() };
}

function actionKey(action: string, movieId: number) {
  return `${action}:${movieId}:${Date.now()}:${Math.random().toString(36).slice(2)}`;
}

async function syncLibrary(set: (partial: Partial<AppState>) => void) {
  const library = await api.library();
  set({
    favorites: library.items.filter((item) => item.action === "favorite").map((item) => item.tmdbId),
    ratings: Object.fromEntries(library.items.filter((item) => item.action === "rate" && item.value != null).map((item) => [item.tmdbId, item.value as number])),
  });
}

const INITIAL_CATALOG_PAGES = 3;

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
  openMovie: (movieId) => {
    const view = { name: "movie", movieId } as const;
    if (typeof window !== "undefined") window.history.pushState({}, "", viewPath(view));
    set((state) => ({ view, history: [...state.history, state.view].slice(-20), activeReelId: null }));
  },
  loadMovie: async (movieId) => {
    const movie = mapApiMovie(await api.movie(movieId));
    set((state) => ({ movies: state.movies.some((item) => item.id === movie.id) ? state.movies : [...state.movies, movie] }));
  },
  syncViewFromUrl: () => set({ view: viewFromLocation(), activeReelId: null }),

  loadMovies: async (query) => {
    set({ loadingMovies: true, catalogError: null });
    try {
      const token = get().token;
      let response = token ? await api.recommendations(1) : await api.movies(1, query);
      // Новый аккаунт ещё не имеет рекомендаций в PostgreSQL — в этом случае
      // показываем публичный каталог TMDB, а не локальный фиктивный список.
      if (token && response.results.length === 0) {
        response = await api.movies(1, query);
      }
      const rows = response.results.map(mapApiMovie);
      const unique = [...new Map(rows.map((movie) => [movie.id, movie])).values()];
      set({ movies: unique, catalogPage: 1, catalogTotalPages: "totalPages" in response ? response.totalPages : 1, catalogTotalCount: "totalCount" in response ? response.totalCount : response.results.length, catalogHasMore: "hasNextPage" in response ? response.hasNextPage : response.results.length === 20 });

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
      const response = await api.movies(page, query);
      const rows = response.results.map(mapApiMovie);
      const unique = [...new Map(rows.map((movie) => [movie.id, movie])).values()];
      set({ movies: unique, catalogPage: response.page, catalogTotalPages: response.totalPages, catalogTotalCount: response.totalCount, catalogHasMore: response.hasNextPage });
    } catch (error) {
      set({ catalogError: error instanceof Error ? error.message : "Не удалось загрузить страницу каталога" });
    } finally {
      set({ loadingMovies: false });
    }
  },

  loadMoreMovies: async (query) => {
    const state = get();
    if (state.loadingMoreMovies || !state.catalogHasMore) return false;
    const page = state.catalogPage + 1;
    set({ loadingMoreMovies: true, catalogError: null });
    try {
      const response = state.token ? await api.recommendations(page) : await api.movies(page, query);
      const incoming = response.results.map(mapApiMovie);
      const known = new Set(get().movies.map((movie) => movie.id));
      const unique = incoming.filter((movie) => !known.has(movie.id));
      set({
        movies: [...get().movies, ...unique],
        catalogPage: page,
        catalogHasMore: incoming.length > 0,
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
      await syncLibrary(set);
    } catch {
      setToken(null);
      set({ token: null, user: null, favorites: [], ratings: {} });
    } finally {
      await get().loadMovies().catch(() => undefined);
      set({ hydrated: true });
    }
  },

  toggleFavorite: (movieId) => {
    const favorite = !get().favorites.includes(movieId);
    set((state) => ({ favorites: favorite ? [...state.favorites, movieId] : state.favorites.filter((id) => id !== movieId) }));
    if (get().token) void api.action({ tmdbId: movieId, actionType: favorite ? "favorite" : "unfavorite", idempotencyKey: actionKey("favorite", movieId) }).catch(() => undefined);
  },
  isFavorite: (movieId) => get().favorites.includes(movieId),
  setRating: (movieId, rating) => {
    const value = Math.max(1, Math.min(10, Math.round(rating)));
    set((state) => ({ ratings: { ...state.ratings, [movieId]: value } }));
    if (get().token) void api.action({ tmdbId: movieId, actionType: "rate", value, idempotencyKey: actionKey("rate", movieId) }).catch(() => undefined);
  },
  removeRating: (movieId) => {
    set((state) => { const next = { ...state.ratings }; delete next[movieId]; return { ratings: next }; });
    if (get().token) void api.action({ tmdbId: movieId, actionType: "unrate", idempotencyKey: actionKey("unrate", movieId) }).catch(() => undefined);
  },
  getRating: (movieId) => get().ratings[movieId] ?? null,
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
      await syncLibrary(set);
      await get().loadMovies();
      return { ok: true };
    } catch (error) { return { ok: false, error: error instanceof Error ? error.message : "Не удалось войти" }; }
  },
  logout: () => { void api.logout().catch(() => undefined); setToken(null); set({ user: null, token: null, favorites: [], ratings: {} }); void get().loadMovies(); },
}));
