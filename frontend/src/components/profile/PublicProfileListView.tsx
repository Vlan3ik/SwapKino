"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { ArrowLeft, ChevronLeft, ChevronRight, Heart, Star, Users } from "lucide-react";
import { api, mapApiMovie, type ApiLibraryItem, type PublicProfileListPage, type PublicUser } from "@/lib/api";

type Section = "ratings" | "favorites" | "followers" | "following";
type ListItem = ApiLibraryItem | PublicUser;

const sectionConfig: Record<Section, { title: string; icon: "ratings" | "favorites" | "people" }> = {
  ratings: { title: "Оценки", icon: "ratings" },
  favorites: { title: "Избранное", icon: "favorites" },
  followers: { title: "Подписчики", icon: "people" },
  following: { title: "Подписки", icon: "people" },
};

export function PublicProfileListView({ id, section }: { id: string; section: string }) {
  const selected = (section in sectionConfig ? section : "ratings") as Section;
  const [data, setData] = useState<PublicProfileListPage<ListItem> | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const load = useCallback(async (page: number) => {
    setLoading(true);
    try {
      const response = selected === "ratings"
        ? await api.publicProfileRatings(id, page)
        : selected === "favorites"
          ? await api.publicProfileFavorites(id, page)
          : selected === "followers"
            ? await api.publicProfileFollowers(id, page)
            : await api.publicProfileFollowing(id, page);
      setData(response as PublicProfileListPage<ListItem>);
      setError(null);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Раздел профиля недоступен");
    } finally {
      setLoading(false);
    }
  }, [id, selected]);

  // The effect synchronizes the route section with its remote page resource.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(1); }, [load]);
  const config = sectionConfig[selected];
  if (error) return <main className="mx-auto max-w-5xl px-4 py-16"><Link href={`/profile/${id}`} className="inline-flex items-center gap-2 text-sm text-muted-foreground"><ArrowLeft className="h-4 w-4" /> Профиль</Link><p className="mt-10 text-center">{error}</p></main>;
  return <main className="mx-auto max-w-5xl px-4 sm:px-6 lg:px-8 py-6">
    <Link href={`/profile/${id}`} className="inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground"><ArrowLeft className="h-4 w-4" /> Профиль</Link>
    <div className="mt-6 flex items-center gap-3"><SectionIcon type={config.icon} /><h1 className="text-2xl font-bold">{config.title}</h1></div>
    {loading && !data ? <p className="py-20 text-center text-muted-foreground">Загружаем…</p> : null}
    {!loading && data && (selected === "followers" || selected === "following") && <PeopleGrid items={data.items as PublicUser[]} />}
    {!loading && data && (selected === "ratings" || selected === "favorites") && <MovieGrid items={data.items as ApiLibraryItem[]} showRating={selected === "ratings"} />}
    {data && <Pagination page={data.page} totalPages={data.totalPages} loading={loading} onChange={(page) => void load(page)} />}
  </main>;
}

function SectionIcon({ type }: { type: "ratings" | "favorites" | "people" }) { return type === "ratings" ? <Star className="h-7 w-7 text-rating" fill="currentColor" /> : type === "favorites" ? <Heart className="h-7 w-7 text-like" fill="currentColor" /> : <Users className="h-7 w-7 text-rating" />; }
function ratingClass(value: number) { return value >= 8 ? "bg-emerald-400 text-black" : value >= 5 ? "bg-amber-300 text-black" : "bg-rose-400 text-black"; }
function MovieGrid({ items, showRating }: { items: ApiLibraryItem[]; showRating: boolean }) { if (!items.length) return <Empty />; return <div className="mt-6 grid grid-cols-2 gap-4 sm:grid-cols-4 lg:grid-cols-5">{items.map((item) => { const movie = item.movie ? mapApiMovie(item.movie) : null; return movie ? <Link key={`${item.tmdbId}:${item.isSeries}`} href={`/movie/${item.tmdbId}${item.isSeries ? "?series=1" : ""}`} className="group min-w-0"><div className="relative aspect-[2/3] overflow-hidden rounded-xl border border-white/10 bg-white/5">{movie.posterUrl && <img src={movie.posterUrl} alt={movie.title} className="h-full w-full object-cover transition group-hover:scale-105" />}{showRating && item.rating != null && <span className={`absolute bottom-2 right-2 rounded-md px-2 py-1 text-sm font-black ${ratingClass(item.rating)}`}>{item.rating}</span>}</div><p className="mt-2 truncate text-sm font-semibold">{movie.title}</p></Link> : null; })}</div>; }
function PeopleGrid({ items }: { items: PublicUser[] }) { if (!items.length) return <Empty />; return <div className="mt-6 grid gap-3 sm:grid-cols-2 lg:grid-cols-3">{items.map((person) => <Link key={person.id} href={`/profile/${person.id}`} className="glass-panel flex items-center gap-3 rounded-2xl p-4 hover:border-rating/50"><div className="grid h-12 w-12 shrink-0 place-items-center overflow-hidden rounded-full bg-rating/20 font-bold">{person.avatarUrl ? <img src={person.avatarUrl} alt="" className="h-full w-full object-cover" /> : person.name[0]?.toUpperCase()}</div><span className="truncate font-semibold">{person.name}</span></Link>)}</div>; }
function Empty() { return <p className="mt-10 rounded-2xl border border-dashed border-white/15 py-10 text-center text-sm text-muted-foreground">Пока пусто.</p>; }
function Pagination({ page, totalPages, loading, onChange }: { page: number; totalPages: number; loading: boolean; onChange: (page: number) => void }) { if (totalPages <= 1) return null; return <div className="mt-8 flex items-center justify-center gap-3"><button type="button" disabled={loading || page <= 1} onClick={() => onChange(page - 1)} className="inline-flex items-center gap-1 rounded-full border border-white/15 px-4 py-2 text-sm disabled:opacity-40"><ChevronLeft className="h-4 w-4" /> Назад</button><span className="text-sm text-muted-foreground">Страница {page} из {totalPages}</span><button type="button" disabled={loading || page >= totalPages} onClick={() => onChange(page + 1)} className="inline-flex items-center gap-1 rounded-full border border-white/15 px-4 py-2 text-sm disabled:opacity-40">Дальше <ChevronRight className="h-4 w-4" /></button></div>; }
