"use client";

import { motion } from "framer-motion";
import {
  Star,
  Clock,
  Calendar,
  Play,
  ArrowLeft,
  Heart,
  ExternalLink,
  Users,
  Clapperboard,
  PenLine,
  Share2,
} from "lucide-react";
import { useAppStore } from "@/lib/store";
import { cn } from "@/lib/utils";
import { useEffect, useState } from "react";
import { RatingControl } from "@/components/common/RatingControl";
import { toast } from "sonner";

export function MovieCardView({ movieId }: { movieId: number }) {
  const movie = useAppStore((s) => s.movies.find((item) => item.id === movieId));
  const { goBack, toggleFavorite, isFavorite, loadMovie } = useAppStore();
  const [imgError, setImgError] = useState(false);
  const [sharing, setSharing] = useState(false);

  useEffect(() => {
    if (!movie) void loadMovie(movieId).catch(() => undefined);
  }, [movie, movieId, loadMovie]);

  const shareMovie = async () => {
    if (!movie || typeof window === "undefined") return;
    setSharing(true);
    const url = window.location.href;
    try {
      if (navigator.share) {
        await navigator.share({ title: movie.title, text: `Посмотри фильм «${movie.title}» в СвайпКино`, url });
      } else {
        await navigator.clipboard.writeText(url);
        toast.success("Ссылка скопирована", { description: "Можно отправить её другу" });
      }
    } catch {
      // Пользователь мог закрыть системное окно Share — это не ошибка сайта.
    } finally {
      setSharing(false);
    }
  };

  if (!movie) {
    return (
      <div className="text-center py-20">
        <p className="text-muted-foreground">Загружаем фильм…</p>
        <button
          onClick={goBack}
          className="mt-4 text-rating hover:underline text-sm"
        >
          ← Вернуться назад
        </button>
      </div>
    );
  }

  const fav = isFavorite(movie.id);

  return (
    <div className="pb-12">
      {/* Hero-секция с фоном */}
      <div className="relative h-[420px] sm:h-[520px] -mt-6 mb-8 overflow-hidden">
        {/* Бэкдроп */}
        <img
          src={movie.backdropUrl}
          alt={movie.title}
          className="absolute inset-0 w-full h-full object-cover"
          onError={() => setImgError(true)}
        />
        <div className="absolute inset-0 bg-gradient-to-t from-background via-background/70 to-background/30" />
        <div className="absolute inset-0 bg-gradient-to-r from-background/80 via-transparent to-transparent" />

        {/* Кнопка назад */}
        <div className="absolute top-4 left-4 sm:left-8 z-10">
          <button
            onClick={goBack}
            className="flex items-center gap-2 px-4 py-2 rounded-full glass-panel-strong text-sm font-medium hover:bg-white/10 transition-colors"
          >
            <ArrowLeft className="h-4 w-4" />
            Назад
          </button>
        </div>

        {/* Hero контент */}
        <div className="absolute inset-x-0 bottom-0">
          <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8 pb-6 sm:pb-8">
            <motion.div
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ duration: 0.4 }}
              className="flex flex-col sm:flex-row gap-6 items-start sm:items-end"
            >
              {/* Постер */}
              <div className="hidden sm:block w-44 lg:w-52 shrink-0">
                <div className="aspect-[2/3] rounded-2xl overflow-hidden border border-white/10 shadow-cinematic">
                  <img
                    src={movie.posterUrl}
                    alt={movie.title}
                    className="w-full h-full object-cover"
                  />
                </div>
              </div>

              {/* Заголовок + мета */}
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2 mb-2">
                  {movie.type === "series" && (
                    <span className="text-[10px] uppercase tracking-widest text-rating font-bold bg-rating/15 px-2 py-0.5 rounded">
                      Сериал
                    </span>
                  )}
                  {movie.genres.map((g) => (
                    <span
                      key={g}
                      className="text-[10px] uppercase tracking-widest text-muted-foreground"
                    >
                      {g}
                    </span>
                  ))}
                </div>
                <h1 className="text-3xl sm:text-5xl font-bold tracking-tight leading-tight">
                  {movie.title}
                </h1>
                <p className="text-base sm:text-lg text-muted-foreground italic mt-1">
                  {movie.originalTitle}
                </p>

                {/* Мета-инфо */}
                <div className="flex flex-wrap items-center gap-4 mt-4">
                  <div className="flex items-center gap-1.5">
                    <Star className="h-5 w-5 text-rating" fill="currentColor" />
                    <span className="text-xl font-bold text-rating tabular-nums">
                      {movie.rating.toFixed(1)}
                    </span>
                    <span className="text-xs text-muted-foreground">/10</span>
                  </div>
                  <div className="flex items-center gap-1.5 text-sm text-muted-foreground">
                    <Calendar className="h-4 w-4" />
                    {movie.year}
                  </div>
                  <div className="flex items-center gap-1.5 text-sm text-muted-foreground">
                    <Clock className="h-4 w-4" />
                    {formatDuration(movie.duration)}
                  </div>
                </div>

                {/* Краткое описание */}
                <p className="text-base text-foreground/90 mt-4 max-w-2xl leading-relaxed">
                  {movie.shortDescription}
                </p>

                {/* Кнопки */}
                <div className="flex flex-wrap gap-3 mt-5">
                  <a
                    href={movie.watchUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="flex items-center gap-2 px-6 py-3 rounded-full bg-white text-black font-semibold text-sm hover:bg-rating transition-colors"
                  >
                    <Play className="h-4 w-4" fill="currentColor" />
                    Смотреть
                    <ExternalLink className="h-3 w-3 opacity-60" />
                  </a>
                  <button
                    onClick={() => toggleFavorite(movie.id)}
                    className={cn(
                      "flex items-center gap-2 px-5 py-3 rounded-full font-semibold text-sm border transition-all",
                      fav
                        ? "bg-like text-black border-like"
                        : "border-white/15 hover:bg-white/5"
                    )}
                  >
                    <Heart className="h-4 w-4" fill={fav ? "currentColor" : "none"} />
                    {fav ? "В избранном" : "В избранное"}
                  </button>
                  <button
                    onClick={shareMovie}
                    disabled={sharing}
                    className="flex items-center gap-2 px-5 py-3 rounded-full font-semibold text-sm border border-white/15 hover:bg-white/5 transition-all disabled:opacity-50"
                  >
                    <Share2 className="h-4 w-4" />
                    {sharing ? "Подготавливаем…" : "Поделиться"}
                  </button>
                </div>
              </div>
            </motion.div>
          </div>
        </div>
      </div>

      {/* Основной контент */}
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8 space-y-10">
        {/* Подробное описание + оценка */}
        <section className="grid grid-cols-1 lg:grid-cols-3 gap-6">
          <div className="lg:col-span-2">
            <h2 className="text-xl font-bold mb-3 flex items-center gap-2">
              <span className="w-1 h-5 bg-rating rounded-full" />
              О фильме
            </h2>
            <p className="text-base text-foreground/80 leading-relaxed">
              {movie.description}
            </p>
          </div>
          <div className="lg:col-span-1">
            <RatingControl movieId={movie.id} />
          </div>
        </section>

        {/* Трейлер */}
        <section>
          <h2 className="text-xl font-bold mb-3 flex items-center gap-2">
            <span className="w-1 h-5 bg-rating rounded-full" />
            Трейлер
          </h2>
          <div className="relative aspect-video rounded-2xl overflow-hidden border border-white/10 bg-black shadow-card-glow max-w-4xl">
            <iframe
              src={`https://www.youtube.com/embed/${movie.trailerYoutubeId}?rel=0&modestbranding=1`}
              title={`Трейлер: ${movie.title}`}
              allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture"
              allowFullScreen
              className="absolute inset-0 w-full h-full"
            />
          </div>
        </section>

        {/* Кадры / галерея (постер + бэкдроп + повтор) */}
        <section>
          <h2 className="text-xl font-bold mb-3 flex items-center gap-2">
            <span className="w-1 h-5 bg-rating rounded-full" />
            Кадры и постеры
          </h2>
          <div className="grid grid-cols-2 sm:grid-cols-3 gap-3 max-w-4xl">
            <div className="aspect-[2/3] rounded-xl overflow-hidden border border-white/10">
              <img
                src={movie.posterUrl}
                alt="Постер"
                className="w-full h-full object-cover"
              />
            </div>
            <div className="aspect-video col-span-1 sm:col-span-2 rounded-xl overflow-hidden border border-white/10">
              <img
                src={movie.backdropUrl}
                alt="Кадр"
                className="w-full h-full object-cover"
              />
            </div>
            <div className="aspect-video rounded-xl overflow-hidden border border-white/10">
              <img
                src={`${movie.backdropUrl.split("/original/")[0]}/w780${movie.backdropUrl.split("/original/")[1]}`}
                alt="Кадр 2"
                className="w-full h-full object-cover"
                onError={(e) => {
                  (e.currentTarget as HTMLImageElement).src = movie.backdropUrl;
                }}
              />
            </div>
          </div>
        </section>

        {/* Съёмочная группа + Актёры */}
        <section className="grid grid-cols-1 lg:grid-cols-3 gap-8">
          {/* Режиссёры */}
          <CrewBlock
            title="Режиссёр"
            icon={<Clapperboard className="h-4 w-4" />}
            people={movie.directors}
          />
          {/* Сценаристы */}
          <CrewBlock
            title="Сценарий"
            icon={<PenLine className="h-4 w-4" />}
            people={movie.writers}
          />
          {/* Роли озвучки / прочее — пропускаем, т.к. нет данных */}
          <CrewBlock
            title="Тип"
            icon={<Star className="h-4 w-4" />}
            people={[
              {
                id: 0,
                name: movie.type === "series" ? "Сериал" : "Фильм",
                role: `${movie.duration} мин`,
              },
            ]}
          />
        </section>

        {/* Актёрский состав */}
        <section>
          <h2 className="text-xl font-bold mb-4 flex items-center gap-2">
            <span className="w-1 h-5 bg-rating rounded-full" />
            В ролях
            <span className="text-sm text-muted-foreground font-normal">
              {movie.cast.length}
            </span>
          </h2>
          <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 gap-4">
            {movie.cast.map((p) => (
              <div
                key={p.id}
                className="glass-panel rounded-xl p-3 hover:bg-white/5 transition-colors"
              >
                <div className="aspect-square rounded-lg bg-gradient-to-br from-zinc-700 to-zinc-900 flex items-center justify-center mb-2 border border-white/5">
                  <Users className="h-8 w-8 text-muted-foreground/40" />
                </div>
                <p className="font-semibold text-sm leading-tight">{p.name}</p>
                <p className="text-xs text-muted-foreground mt-0.5 line-clamp-2">
                  {p.role}
                </p>
              </div>
            ))}
          </div>
        </section>

        {/* CTA снизу */}
        <section className="glass-panel rounded-2xl p-6 text-center">
          <h3 className="text-xl font-bold mb-2">Готов к просмотру?</h3>
          <p className="text-sm text-muted-foreground mb-4">
            Открой фильм в WParty или другом сервисе и наслаждайся вечером.
          </p>
          <div className="flex flex-wrap gap-3 justify-center">
            <a
              href={movie.watchUrl}
              target="_blank"
              rel="noopener noreferrer"
              className="flex items-center gap-2 px-6 py-3 rounded-full bg-white text-black font-semibold text-sm hover:bg-rating transition-colors"
            >
              <Play className="h-4 w-4" fill="currentColor" />
              Смотреть
            </a>
            <button
              onClick={shareMovie}
              disabled={sharing}
              className="flex items-center gap-2 px-5 py-3 rounded-full border border-white/15 hover:bg-white/5 font-semibold text-sm transition-colors"
            >
              <Share2 className="h-4 w-4" />
              Поделиться фильмом
            </button>
          </div>
        </section>
      </div>
    </div>
  );
}

function CrewBlock({
  title,
  icon,
  people,
}: {
  title: string;
  icon: React.ReactNode;
  people: { id: number; name: string; role: string }[];
}) {
  return (
    <div className="glass-panel rounded-2xl p-5">
      <div className="flex items-center gap-2 mb-3 text-muted-foreground">
        {icon}
        <span className="text-xs font-semibold uppercase tracking-wider">
          {title}
        </span>
      </div>
      <ul className="space-y-2">
        {people.map((p) => (
          <li key={p.id} className="flex flex-col">
            <span className="font-semibold text-sm">{p.name}</span>
            <span className="text-xs text-muted-foreground">{p.role}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}

function formatDuration(min: number): string {
  if (min < 60) return `${min} мин`;
  const h = Math.floor(min / 60);
  const m = min % 60;
  return m === 0 ? `${h} ч` : `${h} ч ${m} мин`;
}
