"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { Eye, Star } from "lucide-react";
import { api, type MovieSocialItem } from "@/lib/api";

function ratingClass(value: number) {
  return value >= 8 ? "bg-emerald-400 text-black" : value >= 5 ? "bg-amber-300 text-black" : "bg-rose-400 text-black";
}

export function MovieSocialPanel({ movieId, isSeries = false }: { movieId: number; isSeries?: boolean }) {
  const [items, setItems] = useState<MovieSocialItem[]>([]);
  useEffect(() => { void api.movieSocial(movieId, isSeries).then((response) => setItems(response.items)).catch(() => setItems([])); }, [movieId, isSeries]);
  if (!items.length) return null;
  return <section className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8 mb-8"><h2 className="text-xl font-bold mb-3 flex gap-2"><span className="w-1 rounded-full bg-rating" />Твои подписки смотрели</h2><div className="flex flex-wrap gap-3">{items.map((item) => <Link key={item.user.id} href={`/profile/${item.user.id}`} className="glass-panel rounded-2xl px-3 py-2 flex items-center gap-2 hover:border-rating/50 transition"><div className="h-9 w-9 rounded-full overflow-hidden bg-rating/20 grid place-items-center text-xs font-bold">{item.user.avatarUrl ? <img src={item.user.avatarUrl} alt="" className="h-full w-full object-cover" /> : item.user.name[0]?.toUpperCase()}</div><span className="text-sm font-semibold max-w-32 truncate">{item.user.name}</span>{item.rating != null ? <span className={`rounded-lg px-2 py-1 text-xs font-black ${ratingClass(item.rating)}`}><Star className="inline h-3 w-3 fill-current" /> {item.rating}</span> : <span className="text-xs text-muted-foreground"><Eye className="inline h-3 w-3" /> просмотр</span>}</Link>)}</div></section>;
}
