"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";
import { ChevronDown, ChevronLeft, Clock, Heart, Info, Star, X } from "lucide-react";
import { AnimatePresence, motion, PanInfo, useMotionValue, useTransform } from "framer-motion";
import { api, getToken, mapApiMovie, mapApiReel } from "@/lib/api";
import { filmReels, getReelMovies } from "@/lib/movies";
import { useAppStore } from "@/lib/store";
import type { FilmReel, Movie } from "@/types";
import { cn } from "@/lib/utils";
import { toast } from "sonner";
import { preloadImages } from "@/lib/image-preload";

export function SwipeDeck({ reelId, onExit }: { reelId: string; onExit?: () => void }) {
  const catalog = useAppStore((state) => state.movies);
  const fallbackReel = filmReels.find((item) => item.slug === reelId || item.id === reelId);
  const [reel, setReel] = useState<FilmReel | undefined>(fallbackReel);
  const [movies, setMovies] = useState<Movie[]>(fallbackReel ? getReelMovies(fallbackReel, catalog) : []);
  const [index, setIndex] = useState(0);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [committing, setCommitting] = useState<"left" | "right" | null>(null);
  const isFavorite = useAppStore((state) => state.isFavorite);
  const toggleFavorite = useAppStore((state) => state.toggleFavorite);

  useEffect(() => {
    let active = true; setLoading(true);
    api.reelFeed(reelId).then(async (response) => { if (!active) return; const mapped = (response.items ?? response.results ?? []).map(mapApiMovie); await preloadMovies(mapped); if (!active) return; setReel(mapApiReel(response.reel)); setMovies(mapped); setNextCursor(response.nextCursor ?? null); useAppStore.setState((state) => ({ movies: merge(state.movies, mapped) })); }).catch(() => { if (active && fallbackReel) setMovies(getReelMovies(fallbackReel, catalog)); }).finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [reelId]);

  const current = movies[index];
  const commit = useCallback((direction: "left" | "right") => {
    if (!current || committing) return;
    setCommitting(direction);
    if (getToken()) void api.action({ tmdbId: current.id, isSeries: current.type === "series", actionType: direction === "right" ? "swipe_right" : "swipe_left", idempotencyKey: `swipe:${current.type}:${current.id}:${Date.now()}` }).catch(() => undefined);
    if (direction === "right" && !isFavorite(current.id, current.type === "series")) { toggleFavorite(current.id, current.type === "series"); toast.success(`«${current.title}» в избранном`); }
    window.setTimeout(() => { setIndex((value) => value + 1); setCommitting(null); }, 260);
  }, [current, committing, isFavorite, toggleFavorite]);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => { if (event.key === "ArrowLeft") commit("left"); if (event.key === "ArrowRight") commit("right"); };
    window.addEventListener("keydown", onKey); return () => window.removeEventListener("keydown", onKey);
  }, [commit]);

  useEffect(() => {
    if (index < movies.length - 4 || !nextCursor) return;
    const cursor = nextCursor; setNextCursor(null);
    void api.reelFeed(reelId, cursor).then(async (response) => { const incoming = (response.items ?? response.results ?? []).map(mapApiMovie); await preloadMovies(incoming); setMovies((rows) => merge(rows, incoming)); setNextCursor(response.nextCursor ?? null); });
  }, [index, movies.length, nextCursor, reelId]);

  if (loading && !current) return <Status text="Собираем персональную киноплёнку…"/>;
  if (!reel || !current) return <Status text={index ? "Киноплёнка закончилась" : "В этой киноплёнке пока нет фильмов"} back/>;
  return <div className="relative">
    <div className="fixed inset-0 pointer-events-none overflow-hidden"><AnimatePresence initial={false} mode="sync"><motion.img key={`${current.type}:${current.id}`} src={current.backdropUrl ?? current.posterUrl ?? undefined} alt="" initial={{ opacity: 0, scale: 1.04 }} animate={{ opacity: .42, scale: 1 }} exit={{ opacity: 0, scale: 1.02 }} transition={{ duration: .65, ease: "easeOut" }} className="absolute inset-0 h-full w-full object-cover"/></AnimatePresence><div className="absolute inset-0 bg-gradient-to-b from-background/85 via-background/65 to-background"/></div>
    <div className="relative z-10"><div className="flex items-center justify-between mb-4"><Link href="/" onClick={onExit} className="inline-flex items-center gap-1 text-xs text-muted-foreground hover:text-white"><ChevronLeft className="h-4 w-4"/>К киноплёнкам</Link><div className="text-right"><h1 className="font-semibold">{reel.title}</h1><p className="text-xs text-muted-foreground">{reel.subtitle}</p></div></div>
      <div className="relative mx-auto max-w-md h-[min(620px,calc(100vh-260px))] min-h-[430px]"><AnimatePresence initial={false} mode="sync">{movies[index + 2] && <Layer key={`layer:${movies[index + 2].type}:${movies[index + 2].id}`} movie={movies[index + 2]} className="scale-90 opacity-30"/>}{movies[index + 1] && <Layer key={`layer:${movies[index + 1].type}:${movies[index + 1].id}`} movie={movies[index + 1]} className="scale-95 opacity-60"/>}</AnimatePresence><SwipeCard key={`${current.type}:${current.id}`} movie={current} favorite={isFavorite(current.id, current.type === "series")} committing={committing} onCommit={commit}/></div>
      <div className="mt-6 flex justify-center items-center gap-4"><Action label="Пропустить" className="h-16 w-16 text-skip border-skip/40" disabled={Boolean(committing)} onClick={() => commit("left")}><X className="h-7 w-7"/></Action><Link aria-label="Подробнее" href={`/movie/${current.id}${current.type === "series" ? "?series=1" : ""}`} className="h-12 w-12 rounded-full border border-white/20 grid place-items-center hover:bg-white hover:text-black"><Info className="h-5 w-5"/></Link><Action label="В избранное" className="h-16 w-16 text-like border-like/40" disabled={Boolean(committing)} onClick={() => commit("right")}><Heart className="h-7 w-7"/></Action></div>
    </div>
  </div>;
}

function SwipeCard({ movie, favorite, committing, onCommit }: { movie: Movie; favorite: boolean; committing: "left" | "right" | null; onCommit: (direction: "left" | "right") => void }) {
  const x = useMotionValue(0); const rotate = useTransform(x, [-240, 240], [-14, 14]); const like = useTransform(x, [35, 110], [0, 1]); const skip = useTransform(x, [-110, -35], [1, 0]); const [expanded, setExpanded] = useState(false);
  const dragEnd = (_: unknown, info: PanInfo) => { const direction = info.offset.x > 0 ? "right" : "left"; if (Math.abs(info.offset.x) >= 90 || Math.abs(info.velocity.x) >= 650) onCommit(direction); };
  return <motion.article drag={committing ? false : "x"} dragConstraints={{ left: 0, right: 0 }} dragElastic={.65} onDragEnd={dragEnd} animate={committing ? { x: committing === "right" ? 700 : -700, opacity: 0, rotate: committing === "right" ? 18 : -18 } : { x: 0, opacity: 1, rotate: 0 }} transition={committing ? { duration: .25, ease: "easeIn" } : { type: "spring", stiffness: 400, damping: 32 }} style={committing ? undefined : { x, rotate }} className="absolute inset-0 cursor-grab active:cursor-grabbing group no-select">
    <div className="relative h-full overflow-hidden rounded-3xl border border-white/10 bg-zinc-900 shadow-cinematic">{movie.posterUrl ? <img src={movie.posterUrl} alt={movie.title} draggable={false} className="absolute inset-0 h-full w-full object-cover"/> : <div className="absolute inset-0 grid place-items-center text-muted-foreground">Нет постера</div>}<div className="absolute inset-0 bg-gradient-to-t from-black via-transparent to-black/25"/><motion.div style={{ opacity: like }} className="absolute right-7 top-8 border-4 border-like text-like rounded-xl px-3 py-1 rotate-12 font-black">БЕРУ</motion.div><motion.div style={{ opacity: skip }} className="absolute left-7 top-8 border-4 border-skip text-skip rounded-xl px-3 py-1 -rotate-12 font-black">МИМО</motion.div>
      {favorite && <span className="absolute left-4 top-4 rounded-full bg-like px-2 py-1 text-xs font-bold text-black">В избранном</span>}
      <div className="absolute inset-x-0 bottom-0 p-5 sm:p-6" onPointerDown={(event) => event.stopPropagation()}><div className="flex gap-3"><div className="min-w-0 flex-1"><h2 className="text-2xl font-bold leading-tight">{movie.title}</h2>{movie.originalTitle && <p className="truncate text-sm italic text-white/65">{movie.originalTitle}</p>}</div>{movie.rating != null && <span className="h-fit rounded-lg bg-black/60 px-2.5 py-1.5 text-sm text-rating"><Star className="inline h-3.5 w-3.5 fill-current"/> {movie.rating.toFixed(1)}</span>}</div><div className="mt-2 flex gap-2 text-xs text-white/75">{movie.year && <span>{movie.year}</span>}{movie.duration && <span><Clock className="inline h-3 w-3"/> {formatDuration(movie.duration)}</span>}{movie.genres.length > 0 && <span className="truncate">{movie.genres.join(", ")}</span>}</div>
        {movie.description && <button type="button" aria-expanded={expanded} onClick={() => setExpanded((value) => !value)} className="mt-3 w-full text-left"><span className={cn("block text-sm leading-relaxed text-white/80 transition-all duration-200", expanded ? "line-clamp-none" : "line-clamp-2 group-hover:line-clamp-3")}>{movie.description}</span><span className="mt-1 inline-flex items-center gap-1 text-[11px] text-white/55">{expanded ? "Свернуть" : "Описание"}<ChevronDown className={cn("h-3 w-3 transition-transform duration-200", expanded && "rotate-180")}/></span></button>}
      </div></div>
  </motion.article>;
}
function Layer({ movie, className }: { movie: Movie; className: string }) { return <motion.div initial={{ opacity: 0, scale: .84, y: 18 }} animate={{ opacity: 1, scale: 1, y: 0 }} exit={{ opacity: 0, scale: .96, y: -12 }} transition={{ duration: .32, ease: "easeOut" }} className={cn("absolute inset-0 overflow-hidden rounded-3xl bg-zinc-900", className)}>{movie.posterUrl && <img src={movie.posterUrl} alt="" draggable={false} className="h-full w-full object-cover"/>}</motion.div>; }
function Action({ label, onClick, disabled, className, children }: { label: string; onClick: () => void; disabled: boolean; className: string; children: React.ReactNode }) { return <button aria-label={label} disabled={disabled} onClick={onClick} className={cn("rounded-full border-2 grid place-items-center transition hover:scale-105 disabled:opacity-40", className)}>{children}</button>; }
function Status({ text, back }: { text: string; back?: boolean }) { return <div className="py-24 text-center text-muted-foreground"><p>{text}</p>{back && <Link href="/" className="mt-4 inline-block text-rating">К киноплёнкам</Link>}</div>; }
function formatDuration(value: number) { const hours = Math.floor(value / 60); const minutes = value % 60; return hours ? `${hours} ч${minutes ? ` ${minutes} мин` : ""}` : `${minutes} мин`; }
function merge(current: Movie[], incoming: Movie[]) { const map = new Map(current.map((movie) => [`${movie.type}:${movie.id}`, movie])); incoming.forEach((movie) => map.set(`${movie.type}:${movie.id}`, movie)); return [...map.values()]; }
function preloadMovies(movies: Movie[]) { return preloadImages(movies.slice(0, 8).flatMap((movie) => [movie.posterUrl, movie.backdropUrl])); }
