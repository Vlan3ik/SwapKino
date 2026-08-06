"use client";

import { motion, AnimatePresence } from "framer-motion";
import { FilmReelCarousel } from "./FilmReelCarousel";
import { SwipeDeck } from "./SwipeDeck";
import { useAppStore } from "@/lib/store";

export function FeedView() {
  const { activeReelId, setActiveReel, movies, loadingMovies, catalogError } = useAppStore();

  if (loadingMovies || (!movies.length && !catalogError)) {
    return <div className="mx-auto max-w-7xl px-4 py-24 text-center text-muted-foreground">Загружаем фильмы…</div>;
  }

  if (catalogError && !movies.length) {
    return <div className="mx-auto max-w-7xl px-4 py-24 text-center text-skip">Не удалось загрузить фильмы: {catalogError}</div>;
  }

  return (
    <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8 py-6">
      <AnimatePresence mode="wait">
        {!activeReelId ? (
          <motion.div
            key="carousel"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0, y: -20 }}
            transition={{ duration: 0.3 }}
          >
            <FilmReelCarousel />
          </motion.div>
        ) : (
          <motion.div
            key="deck"
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: -20 }}
            transition={{ duration: 0.3 }}
          >
            <SwipeDeck reelId={activeReelId} onExit={() => setActiveReel(null)} />
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}
