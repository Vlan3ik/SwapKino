"use client";

import { useState, useMemo, useCallback, useEffect } from "react";
import { motion, useMotionValue, useTransform, PanInfo } from "framer-motion";
import { Heart, X, Info, Star, Clock, ChevronLeft } from "lucide-react";
import { useAppStore } from "@/lib/store";
import { api, getToken } from "@/lib/api";
import { filmReels, getReelMovies } from "@/lib/movies";
import { cn } from "@/lib/utils";
import { toast } from "sonner";

interface SwipeDeckProps {
  reelId: string;
  onExit: () => void;
}

export function SwipeDeck({ reelId, onExit }: SwipeDeckProps) {
  const reel = filmReels.find((r) => r.id === reelId);
  const [currentIndex, setCurrentIndex] = useState(0);
  const { movies: catalog, openMovie, toggleFavorite, isFavorite, loadMoreMovies, loadingMoreMovies, catalogHasMore } = useAppStore();
  const movies = useMemo(() => {
    if (!reel) return [];
    return getReelMovies(reel, catalog);
  }, [catalog, reel]);

  useEffect(() => {
    if (currentIndex < Math.max(0, movies.length - 3) || loadingMoreMovies || !catalogHasMore) return;
    void loadMoreMovies();
  }, [currentIndex, movies.length, loadingMoreMovies, catalogHasMore, loadMoreMovies]);

  const handleSwipe = useCallback(
    (dir: "left" | "right") => {
      const movie = movies[currentIndex];
      if (!movie) return;
      if (dir === "right") {
        if (!isFavorite(movie.id)) {
          toggleFavorite(movie.id);
          toast.success(`«${movie.title}» в избранном`, {
            description: "Можно посмотреть в разделе «Избранное»",
          });
        }
      }
      if (dir === "left" && getToken()) {
        void api.action({ tmdbId: movie.id, actionType: "swipe_left", idempotencyKey: `swipe-left:${movie.id}:${Date.now()}` }).catch(() => undefined);
      }
      setCurrentIndex((i) => i + 1);
    },
    [currentIndex, movies, isFavorite, toggleFavorite]
  );

  const onDragEnd = useCallback(
    (_: unknown, info: PanInfo) => {
      const threshold = 100;
      if (info.offset.x > threshold) {
        handleSwipe("right");
      } else if (info.offset.x < -threshold) {
        handleSwipe("left");
      }
    },
    [handleSwipe]
  );

  if (!reel || movies.length === 0) {
    return (
      <div className="text-center py-20 text-muted-foreground">
        Кинопленка пуста
      </div>
    );
  }

  // Конец ленты
  if (currentIndex >= movies.length && loadingMoreMovies) {
    return <div className="text-center py-20 text-muted-foreground">Подгружаем следующую партию фильмов…</div>;
  }

  if (currentIndex >= movies.length) {
    return (
      <motion.div
        initial={{ opacity: 0, y: 20 }}
        animate={{ opacity: 1, y: 0 }}
        className="flex flex-col items-center justify-center py-20 gap-4"
      >
        <motion.div
          initial={{ scale: 0.8, opacity: 0 }}
          animate={{ scale: 1, opacity: 1 }}
          transition={{ type: "spring", stiffness: 200, damping: 15 }}
          className="text-6xl"
        >
          🎬
        </motion.div>
        <h3 className="text-2xl font-bold">Лента закончилась</h3>
        <p className="text-muted-foreground text-center max-w-md">
          Ты посмотрел все фильмы в подборке «{reel.title}». Выбери другую
          кинопленку или загляни в каталог.
        </p>
        <div className="flex gap-3 mt-2">
          <button
            onClick={() => setCurrentIndex(0)}
            className="px-5 py-2.5 rounded-full bg-white text-black font-semibold text-sm hover:bg-rating transition-colors"
          >
            Сначала
          </button>
          <button
            onClick={onExit}
            className="px-5 py-2.5 rounded-full border border-white/15 hover:bg-white/5 font-semibold text-sm transition-colors"
          >
            К кинопленкам
          </button>
        </div>
      </motion.div>
    );
  }

  const current = movies[currentIndex];
  const next = movies[currentIndex + 1];
  const after = movies[currentIndex + 2];

  return (
    <div className="relative">
      {/* Полноэкранный анимированный бэкдроп на весь main */}
      <div className="fixed inset-0 z-0 overflow-hidden pointer-events-none">
        {/* Все бэкдропы примонтированы — плавный crossfade через opacity */}
        {movies.map((m, idx) => (
          <div
            key={`bg-${m.id}`}
            className={cn(
              "absolute inset-0 transition-opacity duration-[1100ms] ease-in-out",
              idx === currentIndex ? "opacity-60" : "opacity-0"
            )}
          >
            <img
              src={m.backdropUrl}
              alt=""
              className="absolute inset-0 w-full h-full object-cover scale-105"
              draggable={false}
            />
          </div>
        ))}
        {/* Затемнение для читаемости контента */}
        <div className="absolute inset-0 bg-gradient-to-b from-background/85 via-background/55 to-background/90" />
        <div className="absolute inset-0 backdrop-blur-[2px]" />
      </div>

      {/* Контент поверх бэкдропа */}
      <div className="relative z-10">
        {/* Кнопка выхода — минимальная */}
        <div className="flex justify-center mb-4">
          <button
            onClick={onExit}
            className="flex items-center gap-1.5 text-xs text-muted-foreground hover:text-foreground transition-colors"
          >
            <ChevronLeft className="h-3.5 w-3.5" />
            К кинопленкам
          </button>
        </div>

        {/* Дек свайпа — высота адаптивная, чтобы карточка занимала максимум
            доступного места, но не выходила за 620px на больших экранах.
            ~260px = Header(64-100 на mobile) + FeedView padding(48)
                    + кнопка выхода(40) + кнопки действий(88) + buffer */}
        <div className="relative mx-auto max-w-md h-[min(620px,calc(100vh-260px))] min-h-[420px]">
          {/* Деки под верхним */}
          {after && (
            <DeckCardLayer
              movie={after}
              className="absolute inset-0 scale-90 opacity-30 blur-[1px]"
            />
          )}
          {next && (
            <DeckCardLayer
              movie={next}
              className="absolute inset-0 scale-95 opacity-60"
            />
          )}

          {/* Верхняя карта */}
          <SwipeCard
            key={current.id}
            movie={current}
            onDragEnd={onDragEnd}
            onSwipe={handleSwipe}
            onOpen={() => openMovie(current.id)}
            isFav={isFavorite(current.id)}
          />
        </div>

        {/* Кнопки действий */}
        <div className="flex items-center justify-center gap-4 mt-6">
          <ActionButton
            variant="skip"
            onClick={() => handleSwipe("left")}
            icon={<X className="h-7 w-7" />}
          />
          <ActionButton
            variant="info"
            onClick={() => current && openMovie(current.id)}
            icon={<Info className="h-5 w-5" />}
          />
          <ActionButton
            variant="like"
            onClick={() => handleSwipe("right")}
            icon={<Heart className="h-7 w-7" />}
          />
        </div>
      </div>
    </div>
  );
}

function SwipeCard({
  movie,
  onDragEnd,
  onOpen,
  isFav,
}: {
  movie: Movie;
  onDragEnd: (_: unknown, info: PanInfo) => void;
  onSwipe: (dir: "left" | "right") => void;
  onOpen: () => void;
  isFav: boolean;
}) {
  const x = useMotionValue(0);
  const rotate = useTransform(x, [-200, 200], [-15, 15]);
  const likeOpacity = useTransform(x, [40, 120], [0, 1]);
  const skipOpacity = useTransform(x, [-120, -40], [1, 0]);

  return (
    <motion.div
      drag="x"
      dragConstraints={{ left: 0, right: 0 }}
      dragElastic={0.7}
      onDragEnd={onDragEnd}
      style={{ x, rotate }}
      className="absolute inset-0 cursor-grab active:cursor-grabbing no-select"
      whileTap={{ cursor: "grabbing" }}
    >
      <div className="relative w-full h-full rounded-3xl overflow-hidden glass-panel border border-white/10 shadow-cinematic">
        {/* Постер на весь блок карточки */}
        <motion.img
          src={movie.posterUrl}
          alt={movie.title}
          draggable={false}
          initial={{ scale: 1.04, opacity: 0.9 }}
          animate={{ scale: 1, opacity: 1 }}
          transition={{ duration: 0.6, ease: "easeOut" }}
          className="absolute inset-0 w-full h-full object-cover"
        />

        {/* Градиенты для читаемости */}
        <div className="absolute inset-x-0 top-0 h-32 bg-gradient-to-b from-black/70 to-transparent" />
        <div className="absolute inset-x-0 bottom-0 h-2/5 bg-gradient-to-t from-black via-black/70 to-transparent" />

        {/* Лейблы свайпа */}
        <motion.div
          style={{ opacity: likeOpacity }}
          className="absolute top-8 right-8 border-4 border-like rounded-2xl px-3 py-1 rotate-12 z-10"
        >
          <span className="text-like font-black text-2xl tracking-wider">
            В ИЗБРАННОЕ
          </span>
        </motion.div>
        <motion.div
          style={{ opacity: skipOpacity }}
          className="absolute top-8 left-8 border-4 border-skip rounded-2xl px-3 py-1 -rotate-12 z-10"
        >
          <span className="text-skip font-black text-2xl tracking-wider">
            ПРОПУСК
          </span>
        </motion.div>

        {/* Инфо снизу */}
        <div className="absolute inset-x-0 bottom-0 p-5 sm:p-6 z-10">
          <div className="flex items-start justify-between gap-3 mb-2">
            <div className="flex-1 min-w-0">
              <motion.h3
                initial={{ opacity: 0, y: 10 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ delay: 0.2 }}
                className="text-2xl font-bold leading-tight truncate drop-shadow-lg"
              >
                {movie.title}
              </motion.h3>
              <motion.p
                initial={{ opacity: 0, y: 10 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ delay: 0.25 }}
                className="text-sm text-white/70 italic truncate drop-shadow"
              >
                {movie.originalTitle}
              </motion.p>
            </div>
            <RatingBadge rating={movie.rating} />
          </div>

          <div className="flex items-center gap-3 text-xs text-white/80 mb-2 drop-shadow">
            <span>{movie.year}</span>
            <span className="w-1 h-1 rounded-full bg-white/40" />
            <span className="flex items-center gap-1">
              <Clock className="h-3 w-3" />
              {formatDuration(movie.duration)}
            </span>
            <span className="w-1 h-1 rounded-full bg-white/40" />
            <span className="truncate">{movie.genres.join(", ")}</span>
          </div>
        </div>

        {isFav && (
          <div className="absolute top-4 left-4 bg-like/90 text-black text-xs font-bold px-2 py-1 rounded-full flex items-center gap-1 z-10">
            <Heart className="h-3 w-3" fill="currentColor" />
            В избранном
          </div>
        )}

        {/* Кликабельная зона для открытия карточки */}
        <button
          onClick={onOpen}
          aria-label="Открыть карточку фильма"
          className="absolute inset-0 z-0 cursor-pointer"
        />
      </div>
    </motion.div>
  );
}

function DeckCardLayer({
  movie,
  className,
}: {
  movie: Movie;
  className?: string;
}) {
  return (
    <div className={cn("rounded-3xl overflow-hidden", className)}>
      <img
        src={movie.posterUrl}
        alt={movie.title}
        className="w-full h-full object-cover"
        draggable={false}
      />
    </div>
  );
}

function RatingBadge({ rating }: { rating: number }) {
  return (
    <div className="flex items-center gap-1 bg-black/60 backdrop-blur px-2.5 py-1.5 rounded-lg border border-white/10">
      <Star className="h-3.5 w-3.5 text-rating" fill="currentColor" />
      <span className="text-sm font-bold tabular-nums text-rating">
        {rating.toFixed(1)}
      </span>
    </div>
  );
}

function ActionButton({
  variant,
  onClick,
  icon,
}: {
  variant: "like" | "skip" | "info";
  onClick: () => void;
  icon: React.ReactNode;
}) {
  const styles = {
    like: "w-16 h-16 border-like/40 text-like hover:bg-like hover:text-black",
    skip: "w-16 h-16 border-skip/40 text-skip hover:bg-skip hover:text-black",
    info:
      "w-12 h-12 border-white/20 text-foreground hover:bg-white hover:text-black",
  } as const;

  return (
    <motion.button
      whileHover={{ scale: 1.08 }}
      whileTap={{ scale: 0.92 }}
      onClick={onClick}
      className={cn(
        "rounded-full border-2 flex items-center justify-center transition-all",
        styles[variant]
      )}
      aria-label={
        variant === "like"
          ? "В избранное"
          : variant === "skip"
          ? "Пропустить"
          : "Карточка фильма"
      }
    >
      {icon}
    </motion.button>
  );
}

function formatDuration(min: number): string {
  if (min < 60) return `${min} мин`;
  const h = Math.floor(min / 60);
  const m = min % 60;
  return m === 0 ? `${h} ч` : `${h} ч ${m} мин`;
}
