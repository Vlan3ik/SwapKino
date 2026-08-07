"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { ArrowLeft, Images } from "lucide-react";
import { api, mapApiMovie } from "@/lib/api";
import type { Movie } from "@/types";
import { cn } from "@/lib/utils";

export function MovieImagesView({ movieId, isSeries }: { movieId: number; isSeries: boolean }) {
  const router = useRouter();
  const [movie, setMovie] = useState<Movie | null>(null);
  const [tab, setTab] = useState<"stills" | "posters">("stills");
  const [error, setError] = useState<string | null>(null);
  const load = useCallback(async () => { setError(null); try { setMovie(mapApiMovie(await api.movie(movieId, isSeries))); } catch (cause) { setError(cause instanceof Error ? cause.message : "Не удалось загрузить изображения"); } }, [movieId, isSeries]);
  useEffect(() => { void load(); }, [load]);
  const stills = useMemo(() => movie ? [movie.backdropUrl, ...movie.images].filter((url, index, rows): url is string => Boolean(url) && rows.indexOf(url) === index) : [], [movie]);
  const posters = useMemo(() => movie?.posterUrl ? [movie.posterUrl] : [], [movie]);
  const images = tab === "stills" ? stills : posters;
  const detailsHref = `/movie/${movieId}${isSeries ? "?series=1" : ""}`;

  if (!movie && !error) return <div className="mx-auto max-w-7xl px-4 py-24 text-center text-muted-foreground">Загружаем галерею…</div>;
  if (!movie) return <div className="mx-auto max-w-2xl px-4 py-24 text-center"><h1 className="text-xl font-semibold">Галерея недоступна</h1><p className="mt-2 text-sm text-muted-foreground">{error}</p><div className="mt-5 flex justify-center gap-3"><button onClick={() => void load()} className="rounded-full bg-white px-4 py-2 text-sm text-black">Повторить</button><button onClick={() => router.back()} className="rounded-full border px-4 py-2 text-sm">Назад</button></div></div>;

  return <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8 py-7">
    <Link href={detailsHref} className="inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground"><ArrowLeft className="h-4 w-4"/>К карточке</Link>
    <div className="mt-6 flex flex-col sm:flex-row sm:items-end justify-between gap-4"><div><p className="text-xs uppercase tracking-widest text-rating">{movie.type === "series" ? "Сериал" : "Фильм"}</p><h1 className="mt-1 text-3xl sm:text-4xl font-bold">{movie.title}: галерея</h1><p className="mt-1 text-sm text-muted-foreground">{stills.length} кадров · {posters.length} постеров</p></div><div className="flex rounded-xl border border-white/10 bg-white/5 p-1" role="tablist" aria-label="Тип изображений"><Tab active={tab === "stills"} count={stills.length} onClick={() => setTab("stills")}>Кадры</Tab><Tab active={tab === "posters"} count={posters.length} onClick={() => setTab("posters")}>Постеры</Tab></div></div>
    {images.length ? <div role="tabpanel" className={cn("mt-7 grid gap-4", tab === "posters" ? "grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5" : "grid-cols-1 sm:grid-cols-2 lg:grid-cols-3")}>{images.map((url, index) => <a key={url} href={url} target="_blank" rel="noreferrer" className={cn("group overflow-hidden rounded-2xl border border-white/10 bg-white/5", tab === "posters" ? "aspect-[2/3]" : "aspect-video")}><img src={url} alt={`${tab === "posters" ? "Постер" : "Кадр"} ${index + 1}: ${movie.title}`} loading="lazy" className="h-full w-full object-cover transition-transform duration-500 group-hover:scale-105"/></a>)}</div> : <div className="mt-7 rounded-2xl border border-dashed border-white/15 py-24 text-center text-muted-foreground"><Images className="mx-auto h-9 w-9 opacity-40"/><p className="mt-3">{tab === "posters" ? "Дополнительных постеров пока нет" : "Кадров пока нет"}</p></div>}
  </div>;
}

function Tab({ active, count, onClick, children }: { active: boolean; count: number; onClick: () => void; children: React.ReactNode }) { return <button role="tab" aria-selected={active} onClick={onClick} className={cn("rounded-lg px-4 py-2 text-sm font-semibold transition", active ? "bg-white text-black" : "text-muted-foreground hover:text-white")}>{children}<span className="ml-1.5 text-xs opacity-60">{count}</span></button>; }
