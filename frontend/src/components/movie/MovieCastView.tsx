"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ArrowLeft, Users } from "lucide-react";
import { api, mapApiMovie } from "@/lib/api";
import type { Movie } from "@/types";

export function MovieCastView({ movieId, isSeries }: { movieId: number; isSeries: boolean }) {
  const router = useRouter();
  const [movie, setMovie] = useState<Movie | null>(null);
  const [error, setError] = useState<string | null>(null);
  const load = useCallback(async () => { setError(null); try { setMovie(mapApiMovie(await api.movie(movieId, isSeries))); } catch (cause) { setError(cause instanceof Error ? cause.message : "Не удалось загрузить актёрский состав"); } }, [movieId, isSeries]);
  useEffect(() => { void load(); }, [load]);
  const detailsHref = `/movie/${movieId}${isSeries ? "?series=1" : ""}`;

  if (!movie && !error) return <div className="mx-auto max-w-7xl px-4 py-24 text-center text-muted-foreground">Загружаем актёрский состав…</div>;
  if (!movie) return <div className="mx-auto max-w-2xl px-4 py-24 text-center"><h1 className="text-xl font-semibold">Актёрский состав недоступен</h1><p className="mt-2 text-sm text-muted-foreground">{error}</p><div className="mt-5 flex justify-center gap-3"><button onClick={() => void load()} className="rounded-full bg-white px-4 py-2 text-sm text-black">Повторить</button><button onClick={() => router.back()} className="rounded-full border px-4 py-2 text-sm">Назад</button></div></div>;

  return <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8 py-7">
    <Link href={detailsHref} className="inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground"><ArrowLeft className="h-4 w-4"/>К карточке</Link>
    <header className="mt-6"><p className="text-xs uppercase tracking-widest text-rating">{movie.type === "series" ? "Сериал" : "Фильм"}</p><h1 className="mt-1 text-3xl sm:text-4xl font-bold">{movie.title}: актёрский состав</h1><p className="mt-2 text-sm text-muted-foreground">{movie.cast.length ? `${movie.cast.length} ${plural(movie.cast.length)}` : "Состав пока не опубликован"}</p></header>
    {movie.cast.length > 0 ? <div className="mt-7 grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-6 gap-4">{movie.cast.map((person) => <article key={`${person.id}:${person.name}:${person.role}`} className="glass-panel overflow-hidden rounded-2xl">{person.photoUrl ? <img src={person.photoUrl} alt={person.name} loading="lazy" className="aspect-[3/4] w-full object-cover"/> : <div className="aspect-[3/4] grid place-items-center bg-white/5"><Users className="h-10 w-10 text-white/20"/></div>}<div className="p-4"><h2 className="font-semibold leading-tight">{person.name}</h2>{person.role && <p className="mt-1 text-xs leading-relaxed text-muted-foreground">{person.role}</p>}</div></article>)}</div> : <div className="mt-7 rounded-2xl border border-dashed border-white/15 py-24 text-center text-muted-foreground"><Users className="mx-auto h-10 w-10 opacity-30"/><p className="mt-3">Данные об актёрах пока не загружены</p><Link href={detailsHref} className="mt-4 inline-block text-sm text-rating hover:underline">Вернуться к карточке</Link></div>}
  </div>;
}

function plural(count: number) { const mod10 = count % 10; const mod100 = count % 100; if (mod10 === 1 && mod100 !== 11) return "актёр"; if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return "актёра"; return "актёров"; }
