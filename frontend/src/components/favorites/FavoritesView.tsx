"use client";

import { motion } from "framer-motion";
import { Heart, Star, Clock, Trash2, Play } from "lucide-react";
import { useAppStore } from "@/lib/store";

export function FavoritesView() {
  const { movies, favorites, toggleFavorite, openMovie, setView } = useAppStore();
  const favMovies = movies.filter((m) => favorites.includes(m.id));

  return (
    <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8 py-6">
      {/* Шапка */}
      <div className="mb-6 flex items-end justify-between">
        <div>
          <h1 className="text-3xl font-bold tracking-tight flex items-center gap-3">
            <Heart className="h-7 w-7 text-like" fill="currentColor" />
            Избранное
          </h1>
          <p className="text-sm text-muted-foreground mt-1">
            {favMovies.length === 0
              ? "Пока ничего не добавлено"
              : `${favMovies.length} ${pluralize(favMovies.length, ["фильм", "фильма", "фильмов"])} в твоей подборке`}
          </p>
        </div>
        {favMovies.length > 0 && (
          <button
            onClick={() => setView({ name: "feed" })}
            className="text-sm text-rating hover:underline"
          >
            ← В ленту
          </button>
        )}
      </div>

      {favMovies.length === 0 ? (
        <div className="glass-panel rounded-3xl p-12 text-center">
          <div className="h-20 w-20 mx-auto rounded-full bg-white/5 flex items-center justify-center mb-4">
            <Heart className="h-9 w-9 text-muted-foreground/50" />
          </div>
          <h3 className="text-xl font-bold mb-2">Здесь пока пусто</h3>
          <p className="text-sm text-muted-foreground max-w-md mx-auto mb-6">
            Чтобы добавить фильм в избранное, открой Ленту, выбери кинопленку и
            свайпай вправо те фильмы, которые хочешь посмотреть.
          </p>
          <button
            onClick={() => setView({ name: "feed" })}
            className="inline-flex items-center gap-2 px-5 py-2.5 rounded-full bg-white text-black font-semibold text-sm hover:bg-rating transition-colors"
          >
            Открыть Ленту
          </button>
        </div>
      ) : (
        <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 gap-4">
          {favMovies.map((m, i) => (
            <motion.div
              key={m.id}
              initial={{ opacity: 0, y: 10 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ duration: 0.2, delay: i * 0.04 }}
              className="group cursor-pointer"
              onClick={() => openMovie(m.id)}
            >
              <div className="relative aspect-[2/3] rounded-xl overflow-hidden border border-white/10 shadow-card-glow">
                <img
                  src={m.posterUrl}
                  alt={m.title}
                  className="w-full h-full object-cover transition-transform duration-500 group-hover:scale-105"
                />
                <div className="absolute inset-0 bg-gradient-to-t from-black/90 via-black/20 to-transparent opacity-80 group-hover:opacity-100 transition-opacity" />

                {/* Рейтинг */}
                <div className="absolute top-2 right-2 bg-black/70 backdrop-blur px-2 py-1 rounded-md flex items-center gap-1 border border-white/10">
                  <Star className="h-3 w-3 text-rating" fill="currentColor" />
                  <span className="text-xs font-bold text-rating tabular-nums">
                    {m.rating.toFixed(1)}
                  </span>
                </div>

                {/* Лайк индикатор */}
                <div className="absolute top-2 left-2 h-8 w-8 rounded-full bg-like text-black flex items-center justify-center">
                  <Heart className="h-3.5 w-3.5" fill="currentColor" />
                </div>

                {/* Удалить */}
                <button
                  onClick={(e) => {
                    e.stopPropagation();
                    toggleFavorite(m.id);
                  }}
                  className="absolute bottom-2 right-2 h-8 w-8 rounded-full bg-black/70 backdrop-blur text-white hover:bg-skip hover:text-black flex items-center justify-center border border-white/10 transition-colors opacity-0 group-hover:opacity-100"
                  aria-label="Убрать из избранного"
                >
                  <Trash2 className="h-3.5 w-3.5" />
                </button>

                {/* Контент */}
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
                  </div>

                  {/* Смотреть */}
                  <a
                    href={m.watchUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                    onClick={(e) => e.stopPropagation()}
                    className="mt-2 flex items-center justify-center gap-1.5 px-3 py-1.5 rounded-full bg-white text-black text-xs font-semibold hover:bg-rating transition-colors opacity-0 group-hover:opacity-100"
                  >
                    <Play className="h-3 w-3" fill="currentColor" />
                    Смотреть
                  </a>
                </div>
              </div>
            </motion.div>
          ))}
        </div>
      )}
    </div>
  );
}

function pluralize(n: number, forms: [string, string, string]): string {
  const n10 = n % 10;
  const n100 = n % 100;
  if (n10 === 1 && n100 !== 11) return forms[0];
  if (n10 >= 2 && n10 <= 4 && (n100 < 10 || n100 >= 20)) return forms[1];
  return forms[2];
}
