"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { ArrowLeft, Calendar, Clapperboard, Clock, Heart, PenLine, Play, Star, Users } from "lucide-react";
import { api, mapApiMovie } from "@/lib/api";
import { useAppStore } from "@/lib/store";
import type { Movie, Person } from "@/types";
import { cn } from "@/lib/utils";
import { RatingControl } from "@/components/common/RatingControl";
import { ArtworkImage } from "@/components/common/ArtworkImage";
import { VibixPlayer } from "@/components/movie/VibixPlayer";

export function MovieCardView({ movieId, isSeries = false }: { movieId: number; isSeries?: boolean }) {
  const router = useRouter();
  const cached = useAppStore((state) => state.movies.find((movie) => movie.id === movieId && (movie.type === "series") === isSeries));
  const [movie, setMovie] = useState<Movie | undefined>(cached);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notFound, setNotFound] = useState(false);
  const toggleFavorite = useAppStore((state) => state.toggleFavorite);
  const isFavorite = useAppStore((state) => state.isFavorite);
  const load = useCallback(async () => {
    setLoading(true); setError(null); setNotFound(false);
    try { const details = mapApiMovie(await api.movie(movieId, isSeries)); setMovie(details); useAppStore.setState((state) => ({ movies: merge(state.movies, details) })); }
    catch (cause) { const apiError = cause as Error & { status?: number }; if (apiError.status === 404) setNotFound(true); else setError(apiError.message || "Не удалось загрузить карточку"); }
    finally { setLoading(false); }
  }, [movieId, isSeries]);
  useEffect(() => { void load(); }, [load]);
  if (notFound) return <State title="Фильм не найден" text="Возможно, запись была удалена или ссылка устарела." onBack={() => router.back()}/>;
  if (!movie && loading) return <State title="Загружаем карточку…"/>;
  if (!movie) return <State title="Не удалось открыть фильм" text={error ?? undefined} onRetry={() => void load()} onBack={() => router.back()}/>;
  const favorite = isFavorite(movie.id, isSeries);
  return <div className="pb-12">
    <section className="relative min-h-[480px] overflow-hidden mb-9">{movie.backdropUrl && <img src={movie.backdropUrl} alt="" className="absolute inset-0 h-full w-full object-cover"/>}<div className="absolute inset-0 bg-gradient-to-t from-background via-background/65 to-background/30"/><button onClick={() => router.back()} className="absolute left-4 sm:left-8 top-4 z-10 rounded-full glass-panel-strong px-4 py-2 text-sm flex items-center gap-2"><ArrowLeft className="h-4 w-4"/>Назад</button>
      <div className="relative mx-auto max-w-7xl px-4 sm:px-6 lg:px-8 pt-28 pb-8 min-h-[480px] flex items-end"><div className="flex gap-6 items-end"><ArtworkImage src={movie.posterUrl} title={movie.title} fallbackLabel="Постер не загружен" alt={movie.title} className="hidden sm:grid w-48 aspect-[2/3] rounded-2xl object-cover border border-white/10 shadow-cinematic"/><div><div className="flex flex-wrap gap-2 text-[10px] uppercase tracking-widest text-muted-foreground">{movie.type === "series" && <span className="text-rating">Сериал</span>}{movie.genres.map((genre) => <span key={genre}>{genre}</span>)}</div><h1 className="mt-2 text-4xl sm:text-5xl font-bold">{movie.title}</h1>{movie.originalTitle && <p className="mt-1 text-lg italic text-muted-foreground">{movie.originalTitle}</p>}<div className="mt-4 flex flex-wrap gap-4 text-sm text-muted-foreground">{movie.rating != null && <span className="text-rating font-bold"><Star className="inline h-5 w-5 fill-current"/> {movie.rating.toFixed(1)}</span>}{movie.year && <span><Calendar className="inline h-4 w-4"/> {movie.year}</span>}{movie.duration && <span><Clock className="inline h-4 w-4"/> {formatDuration(movie.duration)}</span>}</div>{movie.shortDescription && <p className="mt-4 max-w-2xl text-lg leading-relaxed">{movie.shortDescription}</p>}<div className="mt-5 flex gap-3">{movie.watchUrl && <a href={movie.watchUrl} target="_blank" rel="noreferrer" className="rounded-full bg-white text-black px-5 py-3 text-sm font-bold flex gap-2"><Play className="h-4 w-4 fill-current"/>Смотреть</a>}<button onClick={() => toggleFavorite(movie.id, isSeries)} className={cn("rounded-full border px-5 py-3 text-sm font-bold flex gap-2", favorite && "bg-like border-like text-black")}><Heart className="h-4 w-4" fill={favorite ? "currentColor" : "none"}/>{favorite ? "В избранном" : "В избранное"}</button></div></div></div></div>
    </section>
    {error && <div className="mx-auto max-w-7xl px-4 mb-6 rounded-xl border border-skip/30 bg-skip/10 p-4 text-sm">Полные данные временно недоступны: {error} <button className="underline ml-2" onClick={() => void load()}>Повторить</button></div>}
    <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8 space-y-10">
      {(movie.description || true) && <section className="grid lg:grid-cols-3 gap-6">{movie.description && <div className="lg:col-span-2"><Heading>О {movie.type === "series" ? "сериале" : "фильме"}</Heading><p className="leading-relaxed text-foreground/80 whitespace-pre-line">{movie.description}</p></div>}<RatingControl movieId={movie.id} isSeries={isSeries}/></section>}
      {movie.trailerYoutubeId && <section><Heading>Трейлер</Heading><div className="aspect-video max-w-4xl rounded-2xl overflow-hidden bg-black"><iframe title={`Трейлер: ${movie.title}`} src={`https://www.youtube.com/embed/${movie.trailerYoutubeId}?rel=0`} allowFullScreen className="h-full w-full"/></div></section>}
      <VibixPlayer movieId={movie.id} isSeries={isSeries} />
      {(movie.images.length > 0 || movie.posterUrl || movie.backdropUrl) && <section><div className="mb-3 flex flex-wrap items-center justify-between gap-3"><Heading>Кадры и постеры</Heading><Link href={`/movie/${movie.id}/images${movie.type === "series" ? "?series=1" : ""}`} className="rounded-full border border-white/15 px-4 py-2 text-sm font-semibold hover:bg-white/5">Все кадры и постеры</Link></div><div className="grid grid-cols-2 sm:grid-cols-3 gap-3 max-w-5xl">{[movie.posterUrl, movie.backdropUrl, ...movie.images].filter((url, index, rows): url is string => Boolean(url) && rows.indexOf(url) === index).slice(0, 3).map((url, index) => <Link key={url} href={`/movie/${movie.id}/images${movie.type === "series" ? "?series=1" : ""}`} className={cn("overflow-hidden rounded-xl border border-white/10 bg-white/5", index === 0 ? "aspect-[2/3]" : "aspect-video")}><img src={url} alt={index === 0 ? `Постер: ${movie.title}` : `Кадр: ${movie.title}`} className="h-full w-full object-cover transition-transform duration-300 hover:scale-105"/></Link>)}</div></section>}
      {(movie.directors.length > 0 || movie.writers.length > 0) && <section className="grid sm:grid-cols-2 gap-8">{movie.directors.length > 0 && <People title="Режиссёр" icon={<Clapperboard className="h-4 w-4"/>} people={movie.directors}/>} {movie.writers.length > 0 && <People title="Сценарий" icon={<PenLine className="h-4 w-4"/>} people={movie.writers}/>}</section>}
      {movie.cast.length > 0 && <section><div className="mb-3 flex flex-wrap items-center justify-between gap-3"><Heading>В ролях</Heading><Link href={`/movie/${movie.id}/cast${movie.type === "series" ? "?series=1" : ""}`} className="rounded-full border border-white/15 px-4 py-2 text-sm font-semibold hover:bg-white/5">Весь актёрский состав</Link></div><div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-3">{movie.cast.slice(0, 6).map((person) => <Link href={`/movie/${movie.id}/cast${movie.type === "series" ? "?series=1" : ""}`} key={`${person.id}:${person.role}`} className="glass-panel rounded-xl overflow-hidden transition hover:-translate-y-1">{person.photoUrl ? <img src={person.photoUrl} alt={person.name} className="aspect-square w-full object-cover"/> : <div className="aspect-square grid place-items-center bg-white/5"><Users className="h-8 w-8 text-white/20"/></div>}<div className="p-3"><p className="font-semibold text-sm">{person.name}</p><p className="text-xs text-muted-foreground">{person.role}</p></div></Link>)}</div></section>}
    </div>
  </div>;
}
function Heading({ children }: { children: React.ReactNode }) { return <h2 className="text-xl font-bold mb-3 flex gap-2"><span className="w-1 rounded-full bg-rating"/>{children}</h2>; }
function People({ title, icon, people }: { title: string; icon: React.ReactNode; people: Person[] }) { return <div><h3 className="font-semibold flex gap-2 mb-3">{icon}{title}</h3>{people.map((person) => <p key={`${person.id}:${person.name}`} className="text-sm text-muted-foreground">{person.name}</p>)}</div>; }
function State({ title, text, onRetry, onBack }: { title: string; text?: string; onRetry?: () => void; onBack?: () => void }) { return <div className="py-28 text-center"><h1 className="text-xl font-semibold">{title}</h1>{text && <p className="mt-2 text-sm text-muted-foreground">{text}</p>}<div className="mt-5 flex justify-center gap-3">{onRetry && <button onClick={onRetry} className="rounded-full bg-white px-4 py-2 text-sm text-black">Повторить</button>}{onBack && <button onClick={onBack} className="rounded-full border px-4 py-2 text-sm">Назад</button>}</div></div>; }
function formatDuration(value: number) { const hours = Math.floor(value / 60); const minutes = value % 60; return hours ? `${hours} ч${minutes ? ` ${minutes} мин` : ""}` : `${minutes} мин`; }
function merge(current: Movie[], incoming: Movie) { const key = `${incoming.type}:${incoming.id}`; const found = current.some((movie) => `${movie.type}:${movie.id}` === key); return found ? current.map((movie) => `${movie.type}:${movie.id}` === key ? incoming : movie) : [...current, incoming]; }
