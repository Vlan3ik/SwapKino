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
  catalogError: string | null;
  hydrated: boolean;

  setView: (view: View) => void;
  goBack: () => void;
  openMovie: (movieId: number) => void;
  loadMovies: (query?: string) => Promise<void>;
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
  catalogError: null,
  hydrated: false,

  setView: (view) => set((state) => ({ view, history: [...state.history, state.view].slice(-20), activeReelId: view.name === "feed" ? state.activeReelId : null })),
  goBack: () => set((state) => state.history.length === 0 ? state : { view: state.history.at(-1)!, history: state.history.slice(0, -1) }),
  openMovie: (movieId) => set((state) => ({ view: { name: "movie", movieId }, history: [...state.history, state.view].slice(-20) })),

  loadMovies: async (query) => {
    set({ loadingMovies: true, catalogError: null });
    try {
      let response = get().token ? await api.recommendations() : await api.movies(1, query);
      // Новый аккаунт ещё не имеет рекомендаций в PostgreSQL — в этом случае
      // показываем публичный каталог TMDB, а не локальный фиктивный список.
      if (get().token && response.results.length === 0) {
        response = await api.movies(1, query);
      }
      set({ movies: response.results.map(mapApiMovie) });
    } catch (error) {
      set({ catalogError: error instanceof Error ? error.message : "Не удалось загрузить фильмы" });
      throw error;
    } finally {
      set({ loadingMovies: false });
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
  logout: () => { setToken(null); set({ user: null, token: null, favorites: [], ratings: {} }); void get().loadMovies(); },
}));
