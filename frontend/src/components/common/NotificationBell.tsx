"use client";

import { useEffect, useState } from "react";
import { Bell } from "lucide-react";
import { useRouter } from "next/navigation";
import { api, type ApiNotification } from "@/lib/api";

function message(item: ApiNotification) { return item.type === "followed" ? "подписался на вас" : item.type === "comment_replied" ? "ответил на ваш комментарий" : item.type === "comment_disliked" ? "поставил дизлайк вашему комментарию" : "лайкнул ваш комментарий"; }

export function NotificationBell() {
  const router = useRouter(); const [open, setOpen] = useState(false); const [items, setItems] = useState<ApiNotification[]>([]); const [unread, setUnread] = useState(0);
  const load = async (mark = false) => { const response = await api.notifications(); setItems(response.items); setUnread(mark ? 0 : response.unreadCount); if (mark && response.unreadCount) { await api.markNotificationsRead(); } };
  useEffect(() => { void load(); }, []);
  const openItem = (item: ApiNotification) => { setOpen(false); if (item.type === "followed") router.push(`/profile/${item.actor.id}`); else if (item.tmdbId) { const query = new URLSearchParams(); if (item.isSeries) query.set("series", "1"); if (item.commentId) query.set("comment", item.commentId); router.push(`/movie/${item.tmdbId}?${query}`); } };
  return <div className="relative"><button type="button" aria-label="Уведомления" onClick={() => { const next = !open; setOpen(next); if (next) void load(true); }} className="relative h-9 w-9 rounded-full border border-white/10 flex items-center justify-center text-muted-foreground hover:text-foreground hover:bg-white/5"><Bell className="h-4 w-4" />{unread > 0 && <span className="absolute -top-1 -right-1 min-w-[15px] h-[15px] rounded-full bg-rating px-1 text-[9px] font-bold text-black flex items-center justify-center">{unread > 99 ? "99+" : unread}</span>}</button>{open && <div className="absolute right-0 top-full mt-2 w-80 max-w-[calc(100vw-2rem)] glass-panel-strong rounded-xl p-2 shadow-cinematic z-50"><div className="px-2 py-2 text-sm font-bold">Уведомления</div>{items.length ? items.slice(0, 10).map((item) => <button type="button" key={item.id} onClick={() => openItem(item)} className="w-full rounded-lg px-2 py-2 text-left text-xs hover:bg-white/5 flex gap-2"><span className="h-7 w-7 shrink-0 rounded-full overflow-hidden bg-rating/20 grid place-items-center font-bold">{item.actor.avatarUrl ? <img src={item.actor.avatarUrl} alt="" className="h-full w-full object-cover" /> : item.actor.name[0]?.toUpperCase()}</span><span><b>{item.actor.name}</b> {message(item)}<time className="block mt-1 text-muted-foreground">{new Date(item.createdAt).toLocaleString("ru-RU")}</time></span></button>) : <p className="px-2 py-5 text-center text-xs text-muted-foreground">Новых уведомлений нет</p>}</div>}</div>;
}
