"use client";

import { useState, useMemo } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { Star, Trash2, Play, Clock, ArrowUpDown, Heart } from "lucide-react";
import { useAppStore } from "@/lib/store";
import { cn } from "@/lib/utils";
import { toast } from "sonner";

type SortKey = "rating-desc" | "rating-asc" | "year-desc" | "year-asc" | "title";

const PAGE_SIZE = 10;

export function RatingsView() {
  const { movies, ratings, removeRating, openMovie, setView, toggleFavorite, isFavorite } =
    useAppStore();
  const [sortBy, setSortBy] = useState<SortKey>("rating-desc");
  const [page, setPage] = useState(1);

  const ratedEntries = useMemo(() => {
    const arr = Object.entries(ratings)
      .map(([id, r]) => ({ movie: movies.find((movie) => movie.id === parseInt(id))!, rating: r }))
      .filter((entry) => entry.movie)
      .filter((e) => e.movie);
    arr.sort((a, b) => {
      switch (sortBy) {
        case "rating-desc":
          return b.rating - a.rating;
        case "rating-asc":
          return a.rating - b.rating;
        case "year-desc":
          return b.movie.year - a.movie.year;
        case "year-asc":
          return a.movie.year - b.movie.year;
        case "title":
          return a.movie.title.localeCompare(b.movie.title, "ru");
      }
    });
    return arr;
  }, [movies, ratings, sortBy]);

  // Reset to page 1 if out of range
  const totalPages = Math.max(1, Math.ceil(ratedEntries.length / PAGE_SIZE));
  const safePage = Math.min(page, totalPages);
  const paginated = ratedEntries.slice(
    (safePage - 1) * PAGE_SIZE,
    safePage * PAGE_SIZE
  );

  const avgRating =
    ratedEntries.length > 0
      ? ratedEntries.reduce((s, e) => s + e.rating, 0) / ratedEntries.length
      : 0;

  const handleRemove = (movieId: number, title: string) => {
    removeRating(movieId);
    toast.success(`Оценка снята: «${title}»`);
  };

  return (
    <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8 py-6">
      {/* Шапка */}
      <div className="mb-6 flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-3xl font-bold tracking-tight flex items-center gap-3">
            <Star className="h-7 w-7 text-rating" fill="currentColor" />
            Мои оценки
          </h1>
          <p className="text-sm text-muted-foreground mt-1">
            {ratedEntries.length === 0
              ? "Пока нет оценок"
              : `${ratedEntries.length} ${pluralize(ratedEntries.length, [
                  "оценка",
                  "оценки",
                  "оценок",
                ])} · средняя ${avgRating.toFixed(1)}`}
          </p>
        </div>

        {ratedEntries.length > 0 && (
          <div className="flex items-center gap-2">
            <ArrowUpDown className="h-3.5 w-3.5 text-muted-foreground" />
            <select
              value={sortBy}
              onChange={(e) => {
                setSortBy(e.target.value as SortKey);
                setPage(1);
              }}
              className="glass-panel rounded-xl px-3 py-2 text-sm font-medium outline-none cursor-pointer hover:bg-white/5"
            >
              <option value="rating-desc">По оценке ↓</option>
              <option value="rating-asc">По оценке ↑</option>
              <option value="year-desc">Сначала новые</option>
              <option value="year-asc">Сначала старые</option>
              <option value="title">По алфавиту</option>
            </select>
          </div>
        )}
      </div>

      {ratedEntries.length === 0 ? (
        <div className="glass-panel rounded-3xl p-12 text-center">
          <div className="h-20 w-20 mx-auto rounded-full bg-white/5 flex items-center justify-center mb-4">
            <Star className="h-9 w-9 text-muted-foreground/50" />
          </div>
          <h3 className="text-xl font-bold mb-2">Здесь пока пусто</h3>
          <p className="text-sm text-muted-foreground max-w-md mx-auto mb-6">
            Открой любой фильм в каталоге или ленте и поставь оценку от 1 до 10 —
            она появится здесь.
          </p>
          <div className="flex flex-wrap gap-3 justify-center">
            <button
              onClick={() => setView({ name: "catalog" })}
              className="inline-flex items-center gap-2 px-5 py-2.5 rounded-full bg-white text-black font-semibold text-sm hover:bg-rating transition-colors"
            >
              Открыть Каталог
            </button>
            <button
              onClick={() => setView({ name: "feed" })}
              className="inline-flex items-center gap-2 px-5 py-2.5 rounded-full border border-white/15 hover:bg-white/5 font-semibold text-sm transition-colors"
            >
              Открыть Ленту
            </button>
          </div>
        </div>
      ) : (
        <>
          {/* Сетка оценённых фильмов */}
          <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 gap-4">
            {paginated.map((entry, i) => {
              const { movie, rating } = entry;
              const fav = isFavorite(movie.id);
              return (
                <motion.div
                  key={movie.id}
                  layout
                  initial={{ opacity: 0, y: 10 }}
                  animate={{ opacity: 1, y: 0 }}
                  exit={{ opacity: 0, scale: 0.95 }}
                  transition={{ duration: 0.2, delay: i * 0.03 }}
                  className="group cursor-pointer"
                  onClick={() => openMovie(movie.id)}
                >
                  <div className="relative aspect-[2/3] rounded-xl overflow-hidden border border-white/10 shadow-card-glow">
                    <img
                      src={movie.posterUrl}
                      alt={movie.title}
                      loading="lazy"
                      className="w-full h-full object-cover transition-transform duration-500 group-hover:scale-105"
                    />
                    <div className="absolute inset-0 bg-gradient-to-t from-black/95 via-black/30 to-transparent" />

                    {/* Оценка пользователя — крупная */}
                    <div className="absolute top-2 right-2 bg-rating text-black px-2.5 py-1.5 rounded-lg flex items-center gap-1 border border-rating shadow-lg">
                      <Star className="h-3.5 w-3.5" fill="currentColor" />
                      <span className="text-sm font-black tabular-nums">
                        {rating}
                      </span>
                      <span className="text-[10px] font-bold opacity-70">/10</span>
                    </div>

                    {/* Индикатор избранного */}
                    {fav && (
                      <div className="absolute top-2 left-2 h-8 w-8 rounded-full bg-like text-black flex items-center justify-center">
                        <Heart className="h-3.5 w-3.5" fill="currentColor" />
                      </div>
                    )}

                    {/* Удалить оценку */}
                    <button
                      onClick={(e) => {
                        e.stopPropagation();
                        handleRemove(movie.id, movie.title);
                      }}
                      className="absolute bottom-2 right-2 h-8 w-8 rounded-full bg-black/70 backdrop-blur text-white hover:bg-skip hover:text-black flex items-center justify-center border border-white/10 transition-colors opacity-0 group-hover:opacity-100"
                      aria-label="Удалить оценку"
                    >
                      <Trash2 className="h-3.5 w-3.5" />
                    </button>

                    {/* Контент снизу */}
                    <div className="absolute inset-x-0 bottom-0 p-3">
                      <h3 className="font-semibold text-sm leading-tight line-clamp-2 mb-1 drop-shadow">
                        {movie.title}
                      </h3>
                      <div className="flex items-center gap-2 text-[10px] text-white/70">
                        <span>{movie.year}</span>
                        <span className="w-1 h-1 rounded-full bg-white/30" />
                        <span className="flex items-center gap-0.5">
                          <Clock className="h-2.5 w-2.5" />
                          {movie.duration < 60
                            ? `${movie.duration}м`
                            : `${Math.floor(movie.duration / 60)}ч`}
                        </span>
                      </div>

                      {/* Смотреть + В избранное */}
                      <div className="mt-2 flex gap-1.5 opacity-0 group-hover:opacity-100 transition-opacity">
                        <a
                          href={movie.watchUrl}
                          target="_blank"
                          rel="noopener noreferrer"
                          onClick={(e) => e.stopPropagation()}
                          className="flex-1 flex items-center justify-center gap-1 px-2 py-1.5 rounded-full bg-white text-black text-[10px] font-semibold hover:bg-rating transition-colors"
                        >
                          <Play className="h-2.5 w-2.5" fill="currentColor" />
                          Смотреть
                        </a>
                        <button
                          onClick={(e) => {
                            e.stopPropagation();
                            toggleFavorite(movie.id);
                          }}
                          className={cn(
                            "h-7 w-7 rounded-full flex items-center justify-center border transition-colors",
                            fav
                              ? "bg-like text-black border-like"
                              : "bg-black/60 border-white/10 text-white hover:bg-white hover:text-black"
                          )}
                          aria-label="В избранное"
                        >
                          <Heart
                            className="h-3 w-3"
                            fill={fav ? "currentColor" : "none"}
                          />
                        </button>
                      </div>
                    </div>
                  </div>
                </motion.div>
              );
            })}
          </div>

          {/* Пагинация */}
          {totalPages > 1 && (
            <Pagination
              page={safePage}
              totalPages={totalPages}
              onChange={setPage}
            />
          )}
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
  // Show up to 5 page buttons around current
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
          {start > 2 && (
            <span className="px-1 text-muted-foreground">…</span>
          )}
        </>
      )}

      {pages.map((p) => (
        <PageBtn
          key={p}
          n={p}
          active={p === page}
          onClick={() => onChange(p)}
        />
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

function pluralize(n: number, forms: [string, string, string]): string {
  const n10 = n % 10;
  const n100 = n % 100;
  if (n10 === 1 && n100 !== 11) return forms[0];
  if (n10 >= 2 && n10 <= 4 && (n100 < 10 || n100 >= 20)) return forms[1];
  return forms[2];
}
