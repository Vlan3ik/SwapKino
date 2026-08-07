"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { Clock, Heart, Search, SlidersHorizontal, Star, X } from "lucide-react";
import { api, mapApiMovie, moviePageItems } from "@/lib/api";
import { allGenres } from "@/lib/movies";
import { useAppStore } from "@/lib/store";
import type { Movie } from "@/types";
import { cn } from "@/lib/utils";
import { ArtworkImage } from "@/components/common/ArtworkImage";

const GENRE_IDS: Record<string, number> = { "Боевик":28, "Анимация":16, "Биография":36, "Вестерн":37, "Военный":10752, "Детектив":9648, "Документальный":99, "Драма":18, "История":36, "Комедия":35, "Криминал":80, "Мелодрама":10749, "Музыка":10402, "Приключения":12, "Семейный":10751, "Триллер":53, "Ужасы":27, "Фантастика":878, "Фэнтези":14 };
const MAX_YEAR = new Date().getFullYear();
const MAX_RESTORE_PAGES = 20;

function restoredPageCount(value: string | null) {
  const parsed = Number(value ?? 1);
  return Number.isFinite(parsed) ? Math.min(MAX_RESTORE_PAGES, Math.max(1, Math.floor(parsed))) : 1;
}

function scrollStorageKey() {
  return `catalog-scroll:${window.location.pathname}${window.location.search}`;
}

function currentLocationKey() {
  return `${window.location.pathname}${window.location.search}`;
}

export function CatalogView() {
  const router = useRouter();
  const params = useSearchParams();
  const initialQuery = params.get("q") ?? "";
  const [search, setSearch] = useState(initialQuery);
  const [items, setItems] = useState<Movie[]>([]);
  const [total, setTotal] = useState(0);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [loadedPages, setLoadedPages] = useState(() => restoredPageCount(params.get("pages")));
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [filtersOpen, setFiltersOpen] = useState(false);
  const observerTarget = useRef<HTMLDivElement>(null);
  const requestId = useRef(0);
  const initialAbort = useRef<AbortController | null>(null);
  const loadMoreInFlight = useRef(false);
  const claimedCursor = useRef<string | null>(null);
  const loadMoreRef = useRef<() => void>(() => undefined);

  const genresParam = params.get("genres") ?? "";
  const genres = useMemo(() => genresParam.split(",").filter(Boolean), [genresParam]);
  const minRating = Number(params.get("rating") || 0);
  const yearFrom = Number(params.get("from") || 1970);
  const yearTo = Number(params.get("to") || MAX_YEAR);
  const type = params.get("type") ?? "all";
  const sort = params.get("sort") ?? "popular";

  const replaceParams = useCallback((changes: Record<string, string | null>, resetPages = true) => {
    const next = new URLSearchParams(window.location.search);
    Object.entries(changes).forEach(([key, value]) => value ? next.set(key, value) : next.delete(key));
    if (resetPages) next.delete("pages");
    router.replace(`/catalog${next.size ? `?${next}` : ""}`, { scroll: false });
  }, [router]);

  useEffect(() => {
    setSearch(initialQuery);
  }, [initialQuery]);

  useEffect(() => {
    const timer = window.setTimeout(() => {
      const value = search.trim();
      if (value !== initialQuery) replaceParams({ q: value || null });
    }, 350);
    return () => window.clearTimeout(timer);
  }, [search, initialQuery, replaceParams]);

  const query = useMemo(() => ({
    q: initialQuery || undefined,
    genreIds: genres.map((name) => GENRE_IDS[name]).filter(Boolean),
    minRating: minRating || undefined,
    yearFrom: yearFrom !== 1970 ? yearFrom : undefined,
    yearTo: yearTo !== MAX_YEAR ? yearTo : undefined,
    isSeries: type === "all" ? undefined : type === "series",
    sort,
    limit: 20,
  }), [initialQuery, genres, minRating, yearFrom, yearTo, type, sort]);
  const queryKey = JSON.stringify(query);

  const loadInitial = useCallback(async () => {
    initialAbort.current?.abort();
    const controller = new AbortController();
    initialAbort.current = controller;
    const id = ++requestId.current;
    setLoading(true); setError(null);
    try {
      let cursor: string | null = null;
      const collected: Movie[] = [];
      let count = 0;
      let pagesLoaded = 0;
      const pagesToRestore = restoredPageCount(new URLSearchParams(window.location.search).get("pages"));
      for (let page = 0; page < pagesToRestore; page += 1) {
        const response = await api.movies({ ...query, cursor, signal: controller.signal });
        if (id !== requestId.current) return;
        collected.push(...moviePageItems(response).map(mapApiMovie));
        count = response.totalCount;
        cursor = response.nextCursor ?? null;
        pagesLoaded += 1;
        if (!cursor) break;
      }
      const unique = [...new Map(collected.map((movie) => [`${movie.type}:${movie.id}`, movie])).values()];
      claimedCursor.current = null;
      setItems(unique); setTotal(count); setNextCursor(cursor); setLoadedPages(Math.max(1, pagesLoaded));
      useAppStore.setState((state) => ({ movies: mergeMovies(state.movies, unique) }));
      if (pagesToRestore !== Number(new URLSearchParams(window.location.search).get("pages") || 1)) {
        const url = new URL(window.location.href);
        url.searchParams.set("pages", String(pagesToRestore));
        window.history.replaceState({ ...window.history.state, catalog: { key: `${url.pathname}${url.search}`, pages: pagesToRestore, scrollY: 0 } }, "", `${url.pathname}${url.search}`);
      }
      const catalogState = window.history.state?.catalog;
      const historyY = catalogState?.key === currentLocationKey() ? Number(catalogState.scrollY) : 0;
      const y = Number.isFinite(historyY) && historyY > 0 ? historyY : Number(sessionStorage.getItem(scrollStorageKey()) || 0);
      if (y > 0) requestAnimationFrame(() => requestAnimationFrame(() => window.scrollTo({ top: y })));
    } catch (cause) {
      if ((cause as Error).name !== "AbortError" && id === requestId.current) setError(cause instanceof Error ? cause.message : "Не удалось загрузить каталог");
    } finally { if (id === requestId.current) setLoading(false); }
  }, [query, queryKey]);

  useEffect(() => {
    void loadInitial();
    return () => initialAbort.current?.abort();
  }, [loadInitial]);

  const loadMore = useCallback(async () => {
    if (!nextCursor || loadMoreInFlight.current || claimedCursor.current === nextCursor) return;
    const cursor = nextCursor;
    loadMoreInFlight.current = true;
    claimedCursor.current = cursor;
    setLoadingMore(true); setError(null);
    try {
      const response = await api.movies({ ...query, cursor });
      const incoming = moviePageItems(response).map(mapApiMovie);
      setItems((current) => [...new Map([...current, ...incoming].map((movie) => [`${movie.type}:${movie.id}`, movie])).values()]);
      setNextCursor(response.nextCursor ?? null);
      setLoadedPages((current) => {
        const next = Math.min(MAX_RESTORE_PAGES, current + 1);
        const url = new URL(window.location.href);
        url.searchParams.set("pages", String(next));
        const key = `${url.pathname}${url.search}`;
        window.history.replaceState({ ...window.history.state, catalog: { key, pages: next, scrollY: window.scrollY } }, "", key);
        return next;
      });
      useAppStore.setState((state) => ({ movies: mergeMovies(state.movies, incoming) }));
    } catch (cause) {
      claimedCursor.current = null;
      setError(cause instanceof Error ? cause.message : "Не удалось загрузить следующую порцию");
    } finally {
      loadMoreInFlight.current = false;
      setLoadingMore(false);
    }
  }, [nextCursor, query, queryKey]);

  loadMoreRef.current = () => { void loadMore(); };

  useEffect(() => {
    const node = observerTarget.current;
    if (!node) return;
    const observer = new IntersectionObserver(([entry]) => { if (entry.isIntersecting) loadMoreRef.current(); }, { rootMargin: "500px" });
    observer.observe(node); return () => observer.disconnect();
  }, []);

  const rememberScroll = () => {
    const scrollY = window.scrollY;
    sessionStorage.setItem(scrollStorageKey(), String(scrollY));
    window.history.replaceState({ ...window.history.state, catalog: { key: currentLocationKey(), pages: loadedPages, scrollY } }, "", window.location.href);
  };
  const toggleFavorite = useAppStore((state) => state.toggleFavorite);
  const isFavorite = useAppStore((state) => state.isFavorite);
  const activeCount = genres.length + Number(minRating > 0) + Number(type !== "all") + Number(yearFrom !== 1970 || yearTo !== MAX_YEAR);

  return <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8 py-7">
    <div className="mb-6"><h1 className="text-3xl font-bold">Каталог</h1><p className="text-sm text-muted-foreground mt-1">{loading ? "Собираем фильмы…" : `${total.toLocaleString("ru-RU")} фильмов и сериалов`}</p></div>
    <div className="flex flex-col sm:flex-row gap-3 mb-4">
      <label className="relative flex-1"><Search className="absolute left-3.5 top-3 h-4 w-4 text-muted-foreground"/><input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Название фильма или сериала" className="w-full glass-panel rounded-xl pl-10 pr-10 py-2.5 text-sm outline-none focus:ring-2 focus:ring-rating/40"/>{search && <button aria-label="Очистить поиск" onClick={() => setSearch("")} className="absolute right-3 top-2.5"><X className="h-4 w-4"/></button>}</label>
      <button onClick={() => setFiltersOpen((value) => !value)} className={cn("px-4 py-2.5 rounded-xl border flex items-center gap-2 text-sm", (filtersOpen || activeCount) && "border-rating/40 bg-rating/10 text-rating")}><SlidersHorizontal className="h-4 w-4"/>Фильтры{activeCount > 0 && <span className="rounded-full bg-rating px-1.5 text-black">{activeCount}</span>}</button>
      <select value={sort} onChange={(e) => replaceParams({ sort: e.target.value === "popular" ? null : e.target.value })} className="glass-panel rounded-xl px-3 py-2.5 text-sm"><option value="popular">Популярные</option><option value="rating-desc">По рейтингу</option><option value="year-desc">Сначала новые</option><option value="year-asc">Сначала старые</option><option value="title">По алфавиту</option></select>
    </div>
    {filtersOpen && <div className="glass-panel rounded-2xl p-5 mb-5 space-y-5">
      <div><p className="text-xs uppercase text-muted-foreground mb-2">Тип</p><div className="flex gap-2">{[["all", "Всё"], ["film", "Фильмы"], ["series", "Сериалы"]].map(([value,label]) => <button key={value} onClick={() => replaceParams({ type: value === "all" ? null : value })} className={cn("px-3 py-1.5 rounded-lg border text-sm", type === value && "bg-white text-black")}>{label}</button>)}</div></div>
      <div><p className="text-xs uppercase text-muted-foreground mb-2">Жанры <span className="normal-case">(любой из выбранных)</span></p><div className="flex flex-wrap gap-2">{allGenres.map((genre) => <button key={genre} onClick={() => { const next = genres.includes(genre) ? genres.filter((item) => item !== genre) : [...genres, genre]; replaceParams({ genres: next.join(",") || null }); }} className={cn("px-3 py-1.5 rounded-lg border text-sm", genres.includes(genre) && "bg-rating text-black border-rating")}>{genre}</button>)}</div></div>
      <div className="grid sm:grid-cols-3 gap-4"><label className="text-xs text-muted-foreground">Рейтинг от <b className="text-foreground">{minRating}</b><input className="block w-full mt-2 accent-rating" type="range" min="0" max="9" step="0.5" value={minRating} onChange={(e) => replaceParams({ rating: e.target.value === "0" ? null : e.target.value })}/></label><label className="text-xs text-muted-foreground">Год от<input className="block mt-2 w-full rounded-lg bg-black/30 border border-white/10 p-2 text-foreground" type="number" value={yearFrom} onChange={(e) => replaceParams({ from: e.target.value === "1970" ? null : e.target.value })}/></label><label className="text-xs text-muted-foreground">Год до<input className="block mt-2 w-full rounded-lg bg-black/30 border border-white/10 p-2 text-foreground" type="number" value={yearTo} onChange={(e) => replaceParams({ to: e.target.value === String(MAX_YEAR) ? null : e.target.value })}/></label></div>
      <button onClick={() => { setSearch(""); router.replace("/catalog", { scroll: false }); }} className="text-xs text-muted-foreground hover:text-white flex gap-1"><X className="h-3 w-3"/>Сбросить всё</button>
    </div>}
    {activeCount > 0 && <div className="flex flex-wrap gap-2 mb-5">{genres.map((genre) => <button key={genre} onClick={() => replaceParams({ genres: genres.filter((item) => item !== genre).join(",") || null })} className="rounded-full bg-white/8 px-3 py-1 text-xs">{genre} ×</button>)}</div>}
    {loading ? <CatalogSkeleton/> : items.length === 0 && !error ? <Empty/> : <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 gap-4">{items.map((movie) => <CatalogCard key={`${movie.type}:${movie.id}`} movie={movie} favorite={isFavorite(movie.id, movie.type === "series")} onFavorite={() => toggleFavorite(movie.id, movie.type === "series")} onOpen={rememberScroll}/>)}</div>}
    {error && <div className="my-8 rounded-xl border border-skip/30 bg-skip/10 p-4 text-sm"><p>{error}</p><button onClick={() => items.length ? void loadMore() : void loadInitial()} className="mt-2 underline">Повторить</button></div>}
    <div ref={observerTarget} className="h-16 flex items-center justify-center text-sm text-muted-foreground">{loadingMore ? "Загружаем ещё…" : nextCursor ? `Загружено ${items.length} из ${total}` : items.length ? "Это весь каталог" : null}</div>
  </div>;
}

function CatalogCard({ movie, favorite, onFavorite, onOpen }: { movie: Movie; favorite: boolean; onFavorite: () => void; onOpen: () => void }) {
  const href = `/movie/${movie.id}${movie.type === "series" ? "?series=1" : ""}`;
  return <article className="group"><div className="relative aspect-[2/3] overflow-hidden rounded-xl border border-white/10 bg-white/5"><ArtworkImage src={movie.posterUrl} title={movie.title} fallbackLabel="Постер не загружен" alt={movie.title} loading="lazy" className="h-full w-full object-cover transition-transform duration-500 group-hover:scale-105"/><div className="absolute inset-0 bg-gradient-to-t from-black via-transparent to-transparent"/>{movie.rating != null && <span className="absolute right-2 top-2 rounded-md bg-black/70 px-2 py-1 text-xs text-rating"><Star className="inline h-3 w-3 fill-current"/> {movie.rating.toFixed(1)}</span>}<button aria-label={favorite ? "Убрать из избранного" : "В избранное"} onClick={onFavorite} className={cn("absolute left-2 top-2 h-8 w-8 rounded-full grid place-items-center bg-black/70", favorite && "bg-like text-black")}><Heart className="h-4 w-4" fill={favorite ? "currentColor" : "none"}/></button><Link href={href} onClick={onOpen} className="absolute inset-0" aria-label={`Открыть ${movie.title}`}/><div className="absolute inset-x-0 bottom-0 p-3 pointer-events-none"><h2 className="text-sm font-semibold line-clamp-2">{movie.title}</h2><div className="mt-1 flex gap-2 text-[10px] text-white/65">{movie.year && <span>{movie.year}</span>}{movie.duration && <span><Clock className="inline h-2.5 w-2.5"/> {formatDuration(movie.duration)}</span>}{movie.type === "series" && <span className="text-rating">СЕРИАЛ</span>}</div></div></div></article>;
}
function CatalogSkeleton() { return <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 gap-4">{Array.from({ length: 10 }, (_, i) => <div key={i} className="aspect-[2/3] rounded-xl bg-white/5 animate-pulse"/>)}</div>; }
function Empty() { return <div className="py-24 text-center"><div className="text-4xl mb-3">🎬</div><h2 className="font-semibold">Ничего не нашлось</h2><p className="text-sm text-muted-foreground">Сними часть фильтров или измени запрос.</p></div>; }
function formatDuration(value: number) { return value < 60 ? `${value} мин` : `${Math.floor(value / 60)} ч ${value % 60 || ""}`.trim(); }
function mergeMovies(current: Movie[], incoming: Movie[]) { const map = new Map(current.map((movie) => [`${movie.type}:${movie.id}`, movie])); incoming.forEach((movie) => map.set(`${movie.type}:${movie.id}`, movie)); return [...map.values()]; }
