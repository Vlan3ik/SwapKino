"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import {
  User,
  Star,
  Heart,
  Film,
  Sparkles,
  LogIn,
  UserPlus,
  ChevronRight,
  Settings,
} from "lucide-react";
import { contentKey, parseContentKey, useAppStore } from "@/lib/store";
import { motion } from "framer-motion";
import { AuthModal } from "@/components/common/AuthModal";

export function ProfileView() {
  const router = useRouter();
  const { user, movies, favorites, ratings } = useAppStore();
  const [authOpen, setAuthOpen] = useState(false);
  const [authMode, setAuthMode] = useState<"login" | "register">("login");

  const favMovies = movies.filter((m) => favorites.includes(contentKey(m.id, m.type === "series")));
  const ratedEntries = Object.entries(ratings)
    .map(([key, r]) => {
      const content = parseContentKey(key);
      return { movie: movies.find((movie) => movie.id === content.movieId && (movie.type === "series") === content.isSeries)!, rating: r };
    })
    .filter((e) => e.movie);
  const avgRating =
    ratedEntries.length > 0
      ? ratedEntries.reduce((s, e) => s + e.rating, 0) / ratedEntries.length
      : 0;
  const totalMinutes = favMovies.reduce((s, m) => s + (m.duration ?? 0), 0);

  const openAuth = (mode: "login" | "register") => {
    setAuthMode(mode);
    setAuthOpen(true);
  };

  return (
    <div className="mx-auto max-w-5xl px-4 sm:px-6 lg:px-8 py-6 space-y-8">
      {/* Шапка профиля */}
      {user ? (
        // Залогиненный пользователь
        <motion.div
          initial={{ opacity: 0, y: 10 }}
          animate={{ opacity: 1, y: 0 }}
          className="glass-panel rounded-3xl p-6 sm:p-8 relative overflow-hidden"
        >
          <div className="absolute -right-20 -top-20 w-64 h-64 bg-rating/10 rounded-full blur-3xl" />
          <div className="relative flex flex-col sm:flex-row items-start sm:items-center gap-5">
            <motion.div
              initial={{ scale: 0.8, opacity: 0 }}
              animate={{ scale: 1, opacity: 1 }}
              transition={{ type: "spring", stiffness: 200, damping: 15 }}
              className="h-20 w-20 rounded-full bg-gradient-to-br from-rating/80 to-skip/60 flex items-center justify-center border border-white/10 shadow-cinematic overflow-hidden"
            >
              {user.avatarUrl ? <img src={user.avatarUrl} alt="Аватар профиля" className="h-full w-full object-cover" /> : <span className="text-3xl font-bold text-black">{user.username[0]?.toUpperCase()}</span>}
            </motion.div>
            <div className="flex-1">
              <h1 className="text-2xl font-bold">{user.username}</h1>
              <p className="text-sm text-muted-foreground">{user.email}</p>
              <p className="text-xs text-muted-foreground/70 mt-1">
                Аккаунт создан{" "}
                {new Date(user.createdAt).toLocaleDateString("ru-RU", {
                  day: "numeric",
                  month: "long",
                  year: "numeric",
                })}
              </p>
            </div>
            <button type="button" onClick={() => router.push("/profile/settings")} className="inline-flex items-center gap-2 rounded-xl border border-white/10 bg-black/20 px-4 py-2.5 text-sm font-semibold transition hover:border-rating/50 hover:bg-rating/10">
              <Settings className="h-4 w-4 text-rating" /> Настройки
            </button>
          </div>
        </motion.div>
      ) : (
        // Гость — блок авторизации
        <motion.div
          initial={{ opacity: 0, y: 10 }}
          animate={{ opacity: 1, y: 0 }}
          className="glass-panel rounded-3xl p-6 sm:p-10 relative overflow-hidden text-center"
        >
          <div className="absolute -right-20 -top-20 w-64 h-64 bg-rating/10 rounded-full blur-3xl" />
          <div className="absolute -left-20 -bottom-20 w-64 h-64 bg-like/10 rounded-full blur-3xl" />
          <motion.div
            initial={{ scale: 0.8, opacity: 0 }}
            animate={{ scale: 1, opacity: 1 }}
            transition={{ type: "spring", stiffness: 200, damping: 15 }}
            className="relative inline-flex h-16 w-16 rounded-full bg-gradient-to-br from-rating/30 to-skip/20 items-center justify-center border border-white/10 mb-4"
          >
            <User className="h-8 w-8 text-rating" />
          </motion.div>
          <h2 className="relative text-2xl font-bold mb-2">
            Войди в аккаунт
          </h2>
          <p className="relative text-sm text-muted-foreground max-w-md mx-auto mb-6">
            Авторизуйся, чтобы синхронизировать оценки Кинопоиска и получать
            персональные рекомендации на основе твоих вкусов.
          </p>
          <div className="relative flex flex-wrap gap-3 justify-center">
            <button
              onClick={() => openAuth("login")}
              className="flex items-center gap-2 px-5 py-2.5 rounded-full bg-white text-black font-semibold text-sm hover:bg-rating transition-colors"
            >
              <LogIn className="h-4 w-4" />
              Войти
            </button>
            <button
              onClick={() => openAuth("register")}
              className="flex items-center gap-2 px-5 py-2.5 rounded-full border border-white/15 hover:bg-white/5 font-semibold text-sm transition-colors"
            >
              <UserPlus className="h-4 w-4" />
              Регистрация
            </button>
          </div>
          <p className="relative text-[10px] text-muted-foreground/70 mt-4">
            Гость может пользоваться Избранным и оценивать фильмы — данные
            сохранятся в этом браузере.
          </p>
        </motion.div>
      )}

      {/* Статистика */}
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
        <StatCard
          icon={<Heart className="h-5 w-5 text-like" />}
          value={favorites.length}
          label="В избранном"
        />
        <StatCard
          icon={<Film className="h-5 w-5 text-rating" />}
          value={favMovies.filter((m) => m.type === "film").length}
          label="Фильмов"
        />
        <StatCard
          icon={<Sparkles className="h-5 w-5 text-rating" />}
          value={ratedEntries.length > 0 ? avgRating.toFixed(1) : "—"}
          label="Средняя оценка"
        />
        <StatCard
          icon={<Star className="h-5 w-5 text-rating" />}
          value={ratedEntries.length}
          label="Оценок поставлено"
        />
      </div>

      {/* Навигационные плитки — отдельные страницы */}
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
        {/* Избранное */}
        <motion.button
          onClick={() => router.push("/favorites")}
          whileHover={{ y: -3 }}
          initial={{ opacity: 0, y: 10 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.05 }}
          className="glass-panel rounded-2xl p-5 sm:p-6 text-left relative overflow-hidden group"
        >
          <div className="absolute -right-10 -top-10 w-40 h-40 bg-like/10 rounded-full blur-2xl group-hover:bg-like/20 transition-colors" />
          <div className="relative flex items-start gap-4">
            <div className="h-12 w-12 rounded-xl bg-like/15 text-like flex items-center justify-center shrink-0">
              <Heart className="h-6 w-6" fill="currentColor" />
            </div>
            <div className="flex-1 min-w-0">
              <div className="flex items-center justify-between gap-2">
                <h3 className="text-lg font-bold">Избранное</h3>
                <ChevronRight className="h-4 w-4 text-muted-foreground group-hover:text-foreground group-hover:translate-x-1 transition-all" />
              </div>
              <p className="text-sm text-muted-foreground mt-0.5">
                {favorites.length === 0
                  ? "Пока пусто"
                  : `${favorites.length} ${pluralize(favorites.length, [
                      "фильм",
                      "фильма",
                      "фильмов",
                    ])} в подборке`}
              </p>
              {favMovies.length > 0 && (
                <div className="flex gap-1.5 mt-3">
                  {favMovies.slice(0, 5).map((m) => (
                    <div
                      key={m.id}
                      className="w-8 h-12 rounded overflow-hidden border border-white/10 shrink-0"
                    >
                      {m.posterUrl && <img
                        src={m.posterUrl}
                        alt={m.title}
                        className="w-full h-full object-cover"
                      />}
                    </div>
                  ))}
                  {favMovies.length > 5 && (
                    <div className="w-8 h-12 rounded border border-white/10 flex items-center justify-center text-[10px] text-muted-foreground bg-black/30">
                      +{favMovies.length - 5}
                    </div>
                  )}
                </div>
              )}
            </div>
          </div>
        </motion.button>

        {/* Мои оценки */}
        <motion.button
          onClick={() => router.push("/ratings")}
          whileHover={{ y: -3 }}
          initial={{ opacity: 0, y: 10 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.1 }}
          className="glass-panel rounded-2xl p-5 sm:p-6 text-left relative overflow-hidden group"
        >
          <div className="absolute -right-10 -top-10 w-40 h-40 bg-rating/10 rounded-full blur-2xl group-hover:bg-rating/20 transition-colors" />
          <div className="relative flex items-start gap-4">
            <div className="h-12 w-12 rounded-xl bg-rating/15 text-rating flex items-center justify-center shrink-0">
              <Star className="h-6 w-6" fill="currentColor" />
            </div>
            <div className="flex-1 min-w-0">
              <div className="flex items-center justify-between gap-2">
                <h3 className="text-lg font-bold">Мои оценки</h3>
                <ChevronRight className="h-4 w-4 text-muted-foreground group-hover:text-foreground group-hover:translate-x-1 transition-all" />
              </div>
              <p className="text-sm text-muted-foreground mt-0.5">
                {ratedEntries.length === 0
                  ? "Пока нет оценок"
                  : `${ratedEntries.length} ${pluralize(ratedEntries.length, [
                      "оценка",
                      "оценки",
                      "оценок",
                    ])} · средняя ${avgRating.toFixed(1)}`}
              </p>
              {ratedEntries.length > 0 && (
                <div className="flex gap-1.5 mt-3">
                  {ratedEntries
                    .slice()
                    .sort((a, b) => b.rating - a.rating)
                    .slice(0, 5)
                    .map(({ movie, rating }) => (
                      <div
                        key={movie.id}
                        className="relative w-8 h-12 rounded overflow-hidden border border-white/10 shrink-0"
                        title={`${movie.title} — ${rating}/10`}
                      >
                        {movie.posterUrl && <img
                          src={movie.posterUrl}
                          alt={movie.title}
                          className="w-full h-full object-cover"
                        />}
                        <div className="absolute bottom-0 inset-x-0 bg-rating text-black text-[8px] font-black text-center leading-3">
                          {rating}
                        </div>
                      </div>
                    ))}
                </div>
              )}
            </div>
          </div>
        </motion.button>
      </div>

      <AuthModal
        open={authOpen}
        onClose={() => setAuthOpen(false)}
        initialMode={authMode}
      />
    </div>
  );
}

function StatCard({
  icon,
  value,
  label,
}: {
  icon: React.ReactNode;
  value: string | number;
  label: string;
}) {
  return (
    <motion.div
      whileHover={{ y: -3 }}
      className="glass-panel rounded-2xl p-4"
    >
      <div className="flex items-center justify-between mb-2">
        <span className="text-xs text-muted-foreground uppercase tracking-wider">
          {label}
        </span>
        {icon}
      </div>
      <p className="text-2xl font-bold tabular-nums">{value}</p>
    </motion.div>
  );
}

function pluralize(n: number, forms: [string, string, string]): string {
  const n10 = n % 10;
  const n100 = n % 100;
  if (n10 === 1 && n100 !== 11) return forms[0];
  if (n10 >= 2 && n10 <= 4 && (n100 < 10 || n100 >= 20)) return forms[1];
  return forms[2];
}
