"use client";

import { useState, useMemo, useEffect } from "react";
import { Search, SlidersHorizontal, Star, Clock, X, Heart } from "lucide-react";
import { allGenres } from "@/lib/movies";
import { useAppStore } from "@/lib/store";
import { cn } from "@/lib/utils";
import { motion } from "framer-motion";
import { RangeSlider } from "@/components/common/RangeSlider";
import type { Genre } from "@/types";

type SortKey = "rating-desc" | "rating-asc" | "year-desc" | "year-asc" | "title";

const PAGE_SIZE = 20;
const MAX_CATALOG_YEAR = new Date().getFullYear();

export function CatalogView() {
  const movies = useAppStore((s) => s.movies);
  const [search, setSearch] = useState("");
  const [activeGenres, setActiveGenres] = useState<Genre[]>([]);
  const [minRating, setMinRating] = useState(0);
  const [yearRange, setYearRange] = useState<[number, number]>([1970, MAX_CATALOG_YEAR]);
  const [sortBy, setSortBy] = useState<SortKey>("rating-desc");
  const [showFilters, setShowFilters] = useState(true);
  const [typeFilter, setTypeFilter] = useState<"all" | "film" | "series">("all");
  const [page, setPage] = useState(1);

  const { openMovie, toggleFavorite, isFavorite, loadCatalogPage, catalogTotalPages, loadingMovies } = useAppStore();

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    const result = movies.filter((m) => {
      if (q) {
        const hay = `${m.title} ${m.originalTitle} ${m.shortDescription}`.toLowerCase();
        if (!hay.includes(q)) return false;
      }
      if (activeGenres.length > 0 && !activeGenres.every((g) => m.genres.includes(g))) {
        return false;
      }
      if (m.rating < minRating) return false;
      if (m.year < yearRange[0] || m.year > yearRange[1]) return false;
      if (typeFilter !== "all" && m.type !== typeFilter) return false;
      return true;
    });

    result.sort((a, b) => {
      switch (sortBy) {
        case "rating-desc":
          return b.rating - a.rating;
        case "rating-asc":
          return a.rating - b.rating;
        case "year-desc":
          return b.year - a.year;
        case "year-asc":
          return a.year - b.year;
        case "title":
          return a.title.localeCompare(b.title, "ru");
      }
    });
    return result;
  }, [movies, search, activeGenres, minRating, yearRange, sortBy, typeFilter]);

  // Сброс страницы при изменении фильтров / поиска
  useEffect(() => {
    setPage(1);
  }, [search, activeGenres, minRating, yearRange, sortBy, typeFilter]);

  useEffect(() => {
    void loadCatalogPage(page, search.trim() || undefined);
  }, [page, search, loadCatalogPage]);

  const totalPages = Math.max(1, catalogTotalPages);
  const safePage = Math.min(page, totalPages);
  const paginated = filtered;
  const startIdx = filtered.length === 0 ? 0 : (safePage - 1) * PAGE_SIZE + 1;
  const endIdx = filtered.length === 0 ? 0 : startIdx + filtered.length - 1;

  const toggleGenre = (g: Genre) =>
    setActiveGenres((prev) =>
      prev.includes(g) ? prev.filter((x) => x !== g) : [...prev, g]
    );

  const resetFilters = () => {
    setSearch("");
    setActiveGenres([]);
    setMinRating(0);
    setYearRange([1970, MAX_CATALOG_YEAR]);
    setSortBy("rating-desc");
    setTypeFilter("all");
    setPage(1);
  };

  const activeFiltersCount =
    activeGenres.length +
    (minRating > 0 ? 1 : 0) +
    (typeFilter !== "all" ? 1 : 0) +
    (yearRange[0] !== 1970 || yearRange[1] !== MAX_CATALOG_YEAR ? 1 : 0);

  return (
    <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8 py-6">
      {/* Шапка */}
      <div className="mb-6">
        <h1 className="text-3xl font-bold tracking-tight">Каталог</h1>
        <p className="text-sm text-muted-foreground mt-1">
          {filtered.length} из {movies.length} фильмов и сериалов
        </p>
      </div>

      {/* Поиск + сортировка */}
      <div className="flex flex-col sm:flex-row gap-3 mb-4">
        <div className="relative flex-1">
          <Search className="absolute left-3.5 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
          <input
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Поиск по названию, описанию…"
            className="w-full glass-panel rounded-xl pl-10 pr-10 py-2.5 text-sm placeholder:text-muted-foreground outline-none focus:ring-2 focus:ring-rating/40 transition-all"
          />
          {search && (
            <button
              onClick={() => setSearch("")}
              className="absolute right-3 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground"
            >
              <X className="h-4 w-4" />
            </button>
          )}
        </div>
        <div className="flex gap-2">
          <button
            onClick={() => setShowFilters((v) => !v)}
            className={cn(
              "px-4 py-2.5 rounded-xl border text-sm font-medium flex items-center gap-2 transition-all",
              showFilters || activeFiltersCount > 0
                ? "border-rating/40 bg-rating/10 text-rating"
                : "border-white/10 hover:bg-white/5"
            )}
          >
            <SlidersHorizontal className="h-4 w-4" />
            Фильтры
            {activeFiltersCount > 0 && (
              <span className="bg-rating text-black text-xs px-1.5 rounded-full font-bold">
                {activeFiltersCount}
              </span>
            )}
          </button>
          <select
            value={sortBy}
            onChange={(e) => setSortBy(e.target.value as SortKey)}
            className="glass-panel rounded-xl px-3 py-2.5 text-sm font-medium outline-none cursor-pointer hover:bg-white/5"
          >
            <option value="rating-desc">По рейтингу ↓</option>
            <option value="rating-asc">По рейтингу ↑</option>
            <option value="year-desc">Сначала новые</option>
            <option value="year-asc">Сначала старые</option>
            <option value="title">По алфавиту</option>
          </select>
        </div>
      </div>

      {/* Панель фильтров */}
      {showFilters && (
        <motion.div
          initial={{ opacity: 0, height: 0 }}
          animate={{ opacity: 1, height: "auto" }}
          exit={{ opacity: 0, height: 0 }}
          className="glass-panel rounded-2xl p-4 sm:p-5 mb-6 space-y-4 overflow-hidden"
        >
          {/* Тип */}
          <div>
            <div className="text-xs font-semibold text-muted-foreground uppercase tracking-wider mb-2">
              Тип
            </div>
            <div className="flex gap-2">
              {(["all", "film", "series"] as const).map((t) => (
                <button
                  key={t}
                  onClick={() => setTypeFilter(t)}
                  className={cn(
                    "px-3 py-1.5 rounded-lg text-sm font-medium border transition-all",
                    typeFilter === t
                      ? "bg-white text-black border-white"
                      : "border-white/10 hover:bg-white/5"
                  )}
                >
                  {t === "all" ? "Всё" : t === "film" ? "Фильмы" : "Сериалы"}
                </button>
              ))}
            </div>
          </div>

          {/* Жанры */}
          <div>
            <div className="text-xs font-semibold text-muted-foreground uppercase tracking-wider mb-2">
              Жанры
            </div>
            <div className="flex flex-wrap gap-2">
              {allGenres.map((g) => {
                const isActive = activeGenres.includes(g as Genre);
                return (
                  <button
                    key={g}
                    onClick={() => toggleGenre(g as Genre)}
                    className={cn(
                      "px-3 py-1.5 rounded-lg text-sm font-medium border transition-all",
                      isActive
                        ? "bg-rating text-black border-rating"
                        : "border-white/10 hover:bg-white/5"
                    )}
                  >
                    {g}
                  </button>
                );
              })}
            </div>
          </div>

          {/* Рейтинг + год */}
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <div>
              <div className="flex items-center justify-between mb-2">
                <span className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">
                  Мин. рейтинг
                </span>
                <span className="text-sm font-bold text-rating tabular-nums">
                  {minRating.toFixed(1)}
                </span>
              </div>
              <input
                type="range"
                min={0}
                max={10}
                step={0.5}
                value={minRating}
                onChange={(e) => setMinRating(parseFloat(e.target.value))}
                className="w-full accent-rating"
              />
            </div>
            <div>
              <div className="flex items-center justify-between mb-2">
                <span className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">
                  Год
                </span>
                <span className="text-sm font-bold tabular-nums">
                  {yearRange[0]} — {yearRange[1]}
                </span>
              </div>
              <RangeSlider
                min={1970}
                max={MAX_CATALOG_YEAR}
                step={1}
                value={yearRange}
                onChange={setYearRange}
              />
            </div>
          </div>

          {activeFiltersCount > 0 && (
            <button
              onClick={resetFilters}
              className="text-xs text-muted-foreground hover:text-foreground transition-colors flex items-center gap-1"
            >
              <X className="h-3 w-3" />
              Сбросить фильтры
            </button>
          )}
        </motion.div>
      )}

      {/* Сетка фильмов */}
      {filtered.length === 0 ? (
        <div className="text-center py-20 text-muted-foreground">
          <div className="text-5xl mb-3">🔍</div>
          <p>Ничего не найдено. Попробуй изменить фильтры.</p>
        </div>
      ) : (
        <>
          <div className="text-xs text-muted-foreground mb-3">
            Показаны <span className="text-foreground font-semibold tabular-nums">{startIdx}–{endIdx}</span> из {" "}
            <span className="text-foreground font-semibold tabular-nums">{filtered.length}</span>
          </div>
          <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 gap-4">
          {paginated.map((m) => (
            <motion.div
              key={m.id}
              initial={{ opacity: 0, y: 10 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ duration: 0.2 }}
              className="group cursor-pointer"
              onClick={() => openMovie(m.id)}
            >
              <div className="relative aspect-[2/3] rounded-xl overflow-hidden border border-white/10 shadow-card-glow">
                <img
                  src={m.posterUrl}
                  alt={m.title}
                  loading="lazy"
                  className="w-full h-full object-cover transition-transform duration-500 group-hover:scale-105"
                />
                {/* Затемнение при наведении */}
                <div className="absolute inset-0 bg-gradient-to-t from-black/90 via-black/20 to-transparent opacity-80 group-hover:opacity-100 transition-opacity" />

                {/* Рейтинг */}
                <div className="absolute top-2 right-2 bg-black/70 backdrop-blur px-2 py-1 rounded-md flex items-center gap-1 border border-white/10">
                  <Star className="h-3 w-3 text-rating" fill="currentColor" />
                  <span className="text-xs font-bold text-rating tabular-nums">
                    {m.rating.toFixed(1)}
                  </span>
                </div>

                {/* Кнопка избранного */}
                <button
                  onClick={(e) => {
                    e.stopPropagation();
                    toggleFavorite(m.id);
                  }}
                  className={cn(
                    "absolute top-2 left-2 h-8 w-8 rounded-full backdrop-blur flex items-center justify-center border transition-all",
                    isFavorite(m.id)
                      ? "bg-like text-black border-like"
                      : "bg-black/60 border-white/10 text-white hover:bg-white hover:text-black"
                  )}
                >
                  <Heart
                    className="h-3.5 w-3.5"
                    fill={isFavorite(m.id) ? "currentColor" : "none"}
                  />
                </button>

                {/* Инфо снизу */}
                <div className="absolute inset-x-0 bottom-0 p-3">
                  <h3 className="font-semibold text-sm leading-tight line-clamp-2 mb-1">
                    {m.title}
                  </h3>
                  <div className="flex items-center gap-2 text-[10px] text-white/60">
                    <span>{m.year}</span>
                    <span className="w-1 h-1 rounded-full bg-white/30" />
                    <span className="flex items-center gap-0.5">
                      <Clock className="h-2.5 w-2.5" />
                      {m.duration < 60
                        ? `${m.duration}м`
                        : `${Math.floor(m.duration / 60)}ч`}
                    </span>
                    {m.type === "series" && (
                      <span className="px-1.5 py-0.5 bg-rating/20 text-rating rounded text-[9px] font-bold">
                        СЕРИАЛ
                      </span>
                    )}
                  </div>
                </div>
              </div>
            </motion.div>
          ))}
          </div>

          {/* Пагинация */}
          {totalPages > 1 && (
            <Pagination
              page={safePage}
              totalPages={totalPages}
              onChange={(p) => {
                setPage(p);
                window.scrollTo({ top: 0, behavior: "smooth" });
              }}
            />
          )}
          {loadingMovies && <div className="flex justify-center mt-6 text-sm text-muted-foreground animate-pulse">Загружаем страницу {safePage}…</div>}
        </>
      )}
    </div>
  );
}

function Pagination({
  page,
  totalPages,
  onChange,
}: {
  page: number;
  totalPages: number;
  onChange: (p: number) => void;
}) {
  const pages: number[] = [];
  const start = Math.max(1, page - 2);
  const end = Math.min(totalPages, start + 4);
  for (let i = start; i <= end; i++) pages.push(i);

  return (
    <div className="flex items-center justify-center gap-2 mt-8">
      <button
        onClick={() => onChange(Math.max(1, page - 1))}
        disabled={page === 1}
        className={cn(
          "h-9 px-3 rounded-lg text-sm font-medium border transition-all",
          page === 1
            ? "border-white/5 text-muted-foreground/40 cursor-not-allowed"
            : "border-white/10 hover:bg-white/5"
        )}
      >
        Назад
      </button>

      {start > 1 && (
        <>
          <PageBtn n={1} active={page === 1} onClick={() => onChange(1)} />
          {start > 2 && <span className="px-1 text-muted-foreground">…</span>}
        </>
      )}

      {pages.map((p) => (
        <PageBtn key={p} n={p} active={p === page} onClick={() => onChange(p)} />
      ))}

      {end < totalPages && (
        <>
          {end < totalPages - 1 && (
            <span className="px-1 text-muted-foreground">…</span>
          )}
          <PageBtn
            n={totalPages}
            active={page === totalPages}
            onClick={() => onChange(totalPages)}
          />
        </>
      )}

      <button
        onClick={() => onChange(Math.min(totalPages, page + 1))}
        disabled={page === totalPages}
        className={cn(
          "h-9 px-3 rounded-lg text-sm font-medium border transition-all",
          page === totalPages
            ? "border-white/5 text-muted-foreground/40 cursor-not-allowed"
            : "border-white/10 hover:bg-white/5"
        )}
      >
        Вперёд
      </button>
    </div>
  );
}

function PageBtn({
  n,
  active,
  onClick,
}: {
  n: number;
  active: boolean;
  onClick: () => void;
}) {
  return (
    <button
      onClick={onClick}
      className={cn(
        "h-9 w-9 rounded-lg text-sm font-bold border transition-all tabular-nums",
        active
          ? "bg-rating text-black border-rating"
          : "border-white/10 hover:bg-white/5"
      )}
    >
      {n}
    </button>
  );
}
