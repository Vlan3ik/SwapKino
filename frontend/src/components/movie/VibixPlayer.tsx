"use client";

import { MoviePlayer } from "./MoviePlayer";

/** Compatibility wrapper. All provider discovery and rendering lives in MoviePlayer. */
export function VibixPlayer({ movieId, isSeries, title = "Фильм" }: { movieId: number; isSeries: boolean; title?: string }) {
  return <MoviePlayer movieId={movieId} isSeries={isSeries} title={title} />;
}
