"use client";

import Link from "next/link";
import { useCallback, useEffect, useRef, useState } from "react";
import { AlertCircle, ChevronLeft, ChevronRight, Play, RotateCcw } from "lucide-react";
import { motion, PanInfo } from "framer-motion";
import { api, mapApiReel } from "@/lib/api";
import type { FilmReel } from "@/types";
import { cn } from "@/lib/utils";
import { ArtworkImage } from "@/components/common/ArtworkImage";

export function FilmReelCarousel() {
  const [reels, setReels] = useState<FilmReel[]>([]);
  const [index, setIndex] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const requestId = useRef(0);
  const load = useCallback(async () => {
    const id = ++requestId.current;
    setLoading(true);
    setError(null);
    try {
      const response = await api.reels();
      if (id !== requestId.current) return;
      const rows = Array.isArray(response) ? response : response.items ?? response.results ?? [];
      setReels(rows.map(mapApiReel));
      setIndex(0);
    } catch (cause) {
      if (id !== requestId.current) return;
      setReels([]);
      setError(cause instanceof Error ? cause.message : "Не удалось загрузить киноплёнки");
    } finally {
      if (id === requestId.current) setLoading(false);
    }
  }, []);
  useEffect(() => { void load(); return () => { requestId.current += 1; }; }, [load]);
  const go = useCallback((direction: number) => setIndex((current) => reels.length ? (current + direction + reels.length) % reels.length : 0), [reels.length]);
  useEffect(() => { const onKey = (event: KeyboardEvent) => { if (event.key === "ArrowLeft") go(-1); if (event.key === "ArrowRight") go(1); }; window.addEventListener("keydown", onKey); return () => window.removeEventListener("keydown", onKey); }, [go]);
  const current = reels[index];
  const cover = current?.coverUrl;
  const visible = reels.map((item, itemIndex) => { let position = itemIndex - index; if (position > reels.length / 2) position -= reels.length; if (position < -reels.length / 2) position += reels.length; return { item, itemIndex, position }; }).filter(({ position }) => Math.abs(position) <= 2);

  return <section className="relative overflow-hidden rounded-3xl min-h-[650px]">
    {cover && <ArtworkImage src={cover} title={current.title} alt="" className="absolute inset-0 -z-10 h-full w-full object-cover opacity-20 blur-xl scale-110"/>}<div className="absolute inset-0 -z-10 bg-gradient-to-b from-background/20 to-background"/>
    <header className="flex items-end justify-between p-6 sm:p-9"><div><h1 className="text-3xl font-bold">Киноплёнки</h1><p className="text-sm text-muted-foreground mt-1">Выбери настроение, остальное мы соберём сами</p></div>{reels.length > 1 && !loading && !error && <div className="flex gap-2"><Arrow label="Предыдущая" onClick={() => go(-1)}><ChevronLeft/></Arrow><Arrow label="Следующая" onClick={() => go(1)}><ChevronRight/></Arrow></div>}</header>
    {loading ? <ReelsLoading/> : error ? <ReelsMessage title="Не удалось загрузить киноплёнки" text={error} onRetry={() => void load()}/> : reels.length === 0 ? <ReelsMessage title="Киноплёнок пока нет" text="Подборки ещё не настроены или временно недоступны." onRetry={() => void load()}/> :
    <motion.div drag="x" dragConstraints={{ left: 0, right: 0 }} dragElastic={0.15} onDragEnd={(_: unknown, info: PanInfo) => { if (info.offset.x < -80) go(1); if (info.offset.x > 80) go(-1); }} className="relative h-[520px] flex items-center justify-center cursor-grab">
      {visible.map(({ item, itemIndex, position }) => <ReelCard key={item.slug} reel={item} position={position} cover={item.coverUrl} onSide={() => setIndex(itemIndex)}/>) }
    </motion.div>}
  </section>;
}

function ReelCard({ reel, position, cover, onSide }: { reel: FilmReel; position: number; cover: string | null | undefined; onSide: () => void }) {
  const center = position === 0; const distance = Math.abs(position);
  return <motion.article animate={{ x: position * 320, rotateY: position * -25, scale: center ? 1 : .78, opacity: 1 - distance * .25, z: -distance * 220 }} transition={{ type: "spring", stiffness: 230, damping: 30 }} style={{ transformStyle: "preserve-3d", zIndex: 20 - distance }} onClick={center ? undefined : onSide} className={cn("absolute h-[470px] w-[310px] sm:w-[380px] rounded-3xl overflow-hidden border border-white/10 shadow-cinematic", !center && "cursor-pointer")}>
    <ArtworkImage src={cover} title={reel.title} fallbackLabel="Обложка не загружена" alt="" draggable={false} className="absolute inset-0 h-full w-full object-cover"/><div className="absolute inset-0 bg-gradient-to-t from-black via-black/20 to-transparent"/><div className="absolute inset-x-0 bottom-0 p-7"><h2 className="text-3xl font-bold leading-tight">{reel.title}</h2><p className="mt-2 text-sm text-white/70">{reel.subtitle}</p>{center && <Link href={`/reels/${reel.slug}`} className="mt-5 inline-flex items-center gap-2 rounded-full bg-white px-5 py-2.5 text-sm font-bold text-black hover:bg-rating"><Play className="h-4 w-4 fill-current"/>Начать ленту</Link>}</div>
  </motion.article>;
}
function ReelsLoading() { return <div className="relative h-[520px] grid place-items-center" aria-live="polite"><div className="h-[470px] w-[310px] sm:w-[380px] animate-pulse rounded-3xl border border-white/10 bg-gradient-to-br from-white/10 to-white/[0.03]"><div className="mx-7 mt-[350px] h-8 w-2/3 rounded-lg bg-white/10"/><div className="mx-7 mt-3 h-4 w-1/2 rounded bg-white/10"/></div><span className="sr-only">Загружаем киноплёнки…</span></div>; }
function ReelsMessage({ title, text, onRetry }: { title: string; text: string; onRetry: () => void }) { return <div className="h-[470px] mx-6 grid place-items-center rounded-3xl border border-dashed border-white/15 bg-white/[0.025] text-center"><div className="max-w-md px-6"><AlertCircle className="mx-auto h-10 w-10 text-rating/70"/><h2 className="mt-4 text-xl font-semibold">{title}</h2><p className="mt-2 text-sm text-muted-foreground">{text}</p><button type="button" onClick={onRetry} className="mt-5 inline-flex items-center gap-2 rounded-full bg-white px-5 py-2.5 text-sm font-bold text-black hover:bg-rating"><RotateCcw className="h-4 w-4"/>Повторить</button></div></div>; }
function Arrow({ label, onClick, children }: { label: string; onClick: () => void; children: React.ReactNode }) { return <button aria-label={label} onClick={onClick} className="h-10 w-10 rounded-full glass-panel grid place-items-center hover:bg-white/10">{children}</button>; }
