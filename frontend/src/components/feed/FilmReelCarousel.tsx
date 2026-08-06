"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { motion, AnimatePresence, useMotionValue, animate, PanInfo } from "framer-motion";
import { ChevronLeft, ChevronRight, Play } from "lucide-react";
import { filmReels } from "@/lib/movies";
import { useAppStore } from "@/lib/store";
import { cn } from "@/lib/utils";
import type { FilmReel } from "@/types";

/**
 * Карусель «Кинопленок» с drag мышью.
 *  - Зацикленная (loop)
 *  - Drag мышью / тачем для листания
 *  - Карточки вытянутые по высоте, минимальный контент: только название + кнопка «Начать ленту»
 *  - Фон карточки — обложка первого фильма
 */
export function FilmReelCarousel() {
  const reels = filmReels;
  const [index, setIndex] = useState(0);
  const { movies: catalog, setActiveReel } = useAppStore();
  const trackRef = useRef<HTMLDivElement | null>(null);

  const go = useCallback(
    (dir: number) => {
      setIndex((prev) => (prev + dir + reels.length) % reels.length);
    },
    [reels.length]
  );

  const goTo = useCallback(
    (i: number) => {
      setIndex(((i % reels.length) + reels.length) % reels.length);
    },
    [reels.length]
  );

  // Клавиатура
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (e.key === "ArrowLeft") go(-1);
      if (e.key === "ArrowRight") go(1);
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [go]);

  // Drag-обработчик для всего трека
  const onDragEnd = useCallback(
    (_: unknown, info: PanInfo) => {
      // порог в пикселях
      if (info.offset.x < -80) go(1);
      else if (info.offset.x > 80) go(-1);
    },
    [go]
  );

  const currentReel = reels[index];
  const firstMovie = catalog.find((movie) => currentReel.movieIds.includes(movie.id)) ?? catalog.find((movie) => movie.genres.includes(currentReel.genre)) ?? catalog[0];

  return (
    <div className="relative w-full">
      {/* Большой фон от первого фильма в текущей кинопленке */}
      <AnimatePresence mode="popLayout">
        <motion.div
          key={currentReel.id}
          initial={{ opacity: 0, scale: 1.05 }}
          animate={{ opacity: 1, scale: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.8, ease: "easeOut" }}
          className="absolute inset-0 -z-10 overflow-hidden rounded-3xl"
        >
          {firstMovie && (
            <img
              src={firstMovie.backdropUrl}
              alt=""
              className="w-full h-full object-cover scale-110 blur-md opacity-40"
            />
          )}
          <div className="absolute inset-0 bg-gradient-to-b from-background/60 via-background/85 to-background" />
        </motion.div>
      </AnimatePresence>

      {/* Контейнер карусели */}
      <div className="relative px-4 sm:px-8 pt-8 pb-10">
        {/* Заголовок секции + стрелки */}
        <div className="flex items-center justify-between mb-6 sm:mb-8">
          <div>
            <motion.h2
              initial={{ opacity: 0, x: -10 }}
              animate={{ opacity: 1, x: 0 }}
              className="text-2xl sm:text-3xl font-bold tracking-tight"
            >
              Кинопленки
            </motion.h2>
            <motion.p
              initial={{ opacity: 0, x: -10 }}
              animate={{ opacity: 1, x: 0 }}
              transition={{ delay: 0.05 }}
              className="text-sm text-muted-foreground mt-0.5"
            >
              Тяни мышью или используй стрелки
            </motion.p>
          </div>
          <div className="flex items-center gap-2">
            <CarouselArrow direction="left" onClick={() => go(-1)} />
            <CarouselArrow direction="right" onClick={() => go(1)} />
          </div>
        </div>

        {/* Трек с drag */}
        <motion.div
          ref={trackRef}
          drag="x"
          dragConstraints={{ left: 0, right: 0 }}
          dragElastic={0.18}
          onDragEnd={onDragEnd}
          className="relative h-[480px] sm:h-[560px] flex items-center justify-center cursor-grab active:cursor-grabbing no-select"
          style={{ perspective: "2000px" }}
        >
          {reels.map((reel, i) => {
            let pos = i - index;
            // Нормализация для кратчайшего пути (зацикленность)
            if (pos > reels.length / 2) pos -= reels.length;
            if (pos < -reels.length / 2) pos += reels.length;
            return (
              <ReelCard
                key={reel.id}
                reel={reel}
                pos={pos}
                isCenter={pos === 0}
                onSelect={() => {
                  if (pos === 0) {
                    setActiveReel(reel.id);
                  } else {
                    goTo(i);
                  }
                }}
              />
            );
          })}
        </motion.div>

        {/* Прогресс-точки */}
        <div className="flex items-center justify-center gap-2 mt-6">
          {reels.map((r, i) => (
            <button
              key={r.id}
              onClick={() => goTo(i)}
              className={cn(
                "h-1.5 rounded-full transition-all",
                i === index
                  ? "w-10 bg-rating"
                  : "w-1.5 bg-white/20 hover:bg-white/40"
              )}
              aria-label={`Кинопленка: ${r.title}`}
            />
          ))}
        </div>
      </div>
    </div>
  );
}

function CarouselArrow({
  direction,
  onClick,
}: {
  direction: "left" | "right";
  onClick: () => void;
}) {
  const isLeft = direction === "left";
  return (
    <motion.button
      whileHover={{ scale: 1.05 }}
      whileTap={{ scale: 0.92 }}
      onClick={onClick}
      className={cn(
        "h-10 w-10 rounded-full flex items-center justify-center",
        "glass-panel hover:bg-white/10 transition-colors",
        "border border-white/10"
      )}
      aria-label={isLeft ? "Назад" : "Вперёд"}
    >
      {isLeft ? (
        <ChevronLeft className="h-4 w-4" />
      ) : (
        <ChevronRight className="h-4 w-4" />
      )}
    </motion.button>
  );
}

function ReelCard({
  reel,
  pos,
  isCenter,
  onSelect,
}: {
  reel: FilmReel;
  pos: number;
  isCenter: boolean;
  onSelect: () => void;
}) {
  const catalog = useAppStore((state) => state.movies);
  const movies = useMemo(() => {
    const linked = catalog.filter((movie) => reel.movieIds.includes(movie.id));
    return linked.length ? linked : catalog.filter((movie) => movie.genres.includes(reel.genre)).slice(0, 12);
  }, [catalog, reel]);
  const bgMovie = movies[0];
  const absPos = Math.abs(pos);

  // Геометрия: центральный фронтально, боковые уезжают в перспективу
  const x = pos * 320;
  const rotateY = pos * -28;
  const scale = isCenter ? 1 : 0.78 - Math.min(absPos - 1, 2) * 0.06;
  const z = -absPos * 240;
  const opacity = absPos > 2 ? 0 : 1 - absPos * 0.22;

  return (
    <motion.div
      animate={{
        x,
        rotateY,
        scale,
        z,
        opacity,
      }}
      transition={{
        type: "spring",
        stiffness: 240,
        damping: 30,
      }}
      style={{
        transformStyle: "preserve-3d",
        zIndex: 100 - absPos,
        pointerEvents: absPos > 2 ? "none" : "auto",
      }}
      onClick={onSelect}
      className={cn(
        "absolute w-[300px] sm:w-[380px] h-[440px] sm:h-[520px]",
        "rounded-3xl overflow-hidden cursor-pointer",
        "border border-white/10",
        "shadow-cinematic",
        isCenter && "ring-2 ring-rating/40"
      )}
    >
      {/* Фон-обложка первого фильма */}
      {bgMovie && (
        <img
          src={bgMovie.backdropUrl}
          alt={bgMovie.title}
          className="absolute inset-0 w-full h-full object-cover"
          draggable={false}
        />
      )}

      {/* Градиент-затемнение */}
      <div className="absolute inset-0 bg-gradient-to-t from-black via-black/40 to-transparent" />
      <div className="absolute inset-0 bg-gradient-to-r from-black/60 via-transparent to-transparent" />

      {/* Минимальный контент: только название и кнопка */}
      <div className="absolute inset-0 p-6 sm:p-8 flex flex-col justify-end">
        <h3 className="text-3xl sm:text-4xl font-bold text-white leading-tight mb-4 drop-shadow-lg">
          {reel.title}
        </h3>

        {isCenter && (
          <motion.button
            initial={{ opacity: 0, y: 12 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ delay: 0.15 }}
            onClick={(e) => {
              e.stopPropagation();
              onSelect();
            }}
            whileHover={{ scale: 1.04 }}
            whileTap={{ scale: 0.96 }}
            className="self-start flex items-center gap-2 px-5 py-2.5 rounded-full bg-white text-black text-sm font-semibold hover:bg-rating transition-colors shadow-cinematic"
          >
            <Play className="h-4 w-4" fill="currentColor" />
            Начать ленту
          </motion.button>
        )}
      </div>
    </motion.div>
  );
}
