"use client";

import { PointerEvent, useEffect, useMemo, useRef, useState } from "react";
import { Check, Minus, Plus, X } from "lucide-react";

type Props = { file: File | null; onCancel: () => void; onComplete: (file: File) => void };
type Size = { width: number; height: number };
type Point = { x: number; y: number };

export function AvatarCropModal({ file, onCancel, onComplete }: Props) {
  const imageUrl = useMemo(() => file ? URL.createObjectURL(file) : null, [file]);
  const viewportRef = useRef<HTMLDivElement>(null);
  const [viewport, setViewport] = useState(360);
  const [natural, setNatural] = useState<Size>({ width: 0, height: 0 });
  const [zoom, setZoom] = useState(1);
  const [offset, setOffset] = useState<Point>({ x: 0, y: 0 });
  const [drag, setDrag] = useState<{ x: number; y: number; ox: number; oy: number } | null>(null);
  const [saving, setSaving] = useState(false);

  useEffect(() => () => { if (imageUrl) URL.revokeObjectURL(imageUrl); }, [imageUrl]);
  useEffect(() => {
    setZoom(1);
    setOffset({ x: 0, y: 0 });
    setNatural({ width: 0, height: 0 });
  }, [file]);
  useEffect(() => {
    if (!viewportRef.current) return;
    const resize = () => setViewport(Math.min(380, Math.max(260, viewportRef.current?.clientWidth ?? 360)));
    resize();
    const observer = new ResizeObserver(resize);
    observer.observe(viewportRef.current);
    return () => observer.disconnect();
  }, [file]);

  if (!file || !imageUrl) return null;

  const scale = natural.width ? Math.max(viewport / natural.width, viewport / natural.height) * zoom : 1;
  const rendered = { width: natural.width * scale, height: natural.height * scale };
  const limit = { x: Math.max(0, (rendered.width - viewport) / 2), y: Math.max(0, (rendered.height - viewport) / 2) };
  const clampOffset = (x: number, y: number) => ({
    x: Math.max(-limit.x, Math.min(limit.x, x)),
    y: Math.max(-limit.y, Math.min(limit.y, y)),
  });
  const move = (event: PointerEvent<HTMLDivElement>) => {
    if (!drag) return;
    setOffset(clampOffset(drag.ox + event.clientX - drag.x, drag.oy + event.clientY - drag.y));
  };
  const updateZoom = (next: number) => {
    setZoom(next);
    setOffset(value => {
      const nextScale = natural.width ? Math.max(viewport / natural.width, viewport / natural.height) * next : 1;
      const nextRendered = { width: natural.width * nextScale, height: natural.height * nextScale };
      return {
        x: Math.max(-(nextRendered.width - viewport) / 2, Math.min((nextRendered.width - viewport) / 2, value.x)),
        y: Math.max(-(nextRendered.height - viewport) / 2, Math.min((nextRendered.height - viewport) / 2, value.y)),
      };
    });
  };
  const complete = async () => {
    if (!natural.width) return;
    setSaving(true);
    try { onComplete(await cropImage(imageUrl, natural, viewport, scale, offset)); }
    finally { setSaving(false); }
  };

  return <div className="fixed inset-0 z-[100] flex items-center justify-center bg-black/80 p-4 backdrop-blur-sm" role="dialog" aria-modal="true" aria-label="Кадрирование аватарки">
    <div className="w-full max-w-xl overflow-hidden rounded-3xl border border-white/10 bg-[#15151b] shadow-2xl">
      <div className="flex items-center justify-between border-b border-white/10 px-5 py-4">
        <div><h2 className="font-bold">Настрой аватарку</h2><p className="mt-0.5 text-xs text-muted-foreground">Перетащи фото и выбери квадрат для профиля</p></div>
        <button type="button" onClick={onCancel} className="rounded-full p-2 text-muted-foreground hover:bg-white/10 hover:text-foreground" aria-label="Закрыть"><X className="h-5 w-5" /></button>
      </div>

      <div ref={viewportRef} onPointerDown={event => { event.currentTarget.setPointerCapture(event.pointerId); setDrag({ x: event.clientX, y: event.clientY, ox: offset.x, oy: offset.y }); }} onPointerMove={move} onPointerUp={() => setDrag(null)} onPointerCancel={() => setDrag(null)} className="relative mx-auto aspect-square w-full max-w-[380px] cursor-grab touch-none overflow-hidden bg-[#09090b] active:cursor-grabbing">
        {/* Изображение должно существовать уже до onLoad, иначе onLoad никогда не вызовется. */}
        <img onLoad={event => setNatural({ width: event.currentTarget.naturalWidth, height: event.currentTarget.naturalHeight })} src={imageUrl} alt="Кадрирование аватарки" draggable={false} className="pointer-events-none absolute select-none" style={natural.width ? { left: "50%", top: "50%", width: rendered.width, height: rendered.height, maxWidth: "none", transform: `translate(-50%, -50%) translate(${offset.x}px, ${offset.y}px)` } : { inset: 0, width: "100%", height: "100%", objectFit: "contain" }} />
        <div className="pointer-events-none absolute inset-0 z-10 border-[3px] border-white/90 shadow-[0_0_0_9999px_rgba(0,0,0,.48)]" />
        {natural.width === 0 && <div className="pointer-events-none absolute inset-x-0 bottom-4 z-20 text-center text-xs text-white/65">Загружаем изображение…</div>}
      </div>

      <div className="space-y-4 border-t border-white/10 px-5 py-5">
        <div>
          <div className="mb-2 flex items-center justify-between text-xs text-muted-foreground"><span>Масштаб</span><span>{zoom.toFixed(1)}×</span></div>
          <div className="flex items-center gap-3"><Minus className="h-4 w-4 text-muted-foreground" /><input aria-label="Масштаб изображения" type="range" min={1} max={3} step={0.05} value={zoom} onChange={event => updateZoom(Number(event.target.value))} className="w-full accent-rating" /><Plus className="h-4 w-4 text-muted-foreground" /></div>
        </div>

        <div className="flex items-center gap-4 rounded-2xl border border-white/10 bg-white/[.03] p-3">
          <div className="relative h-20 w-20 shrink-0 overflow-hidden rounded-full border-2 border-rating/80 bg-black shadow-[0_0_22px_rgba(255,205,87,.16)]">
            {natural.width > 0 && <img src={imageUrl} alt="Предпросмотр аватарки" draggable={false} className="pointer-events-none absolute max-w-none select-none" style={{ left: "50%", top: "50%", width: rendered.width * (80 / viewport), height: rendered.height * (80 / viewport), transform: `translate(-50%, -50%) translate(${offset.x * (80 / viewport)}px, ${offset.y * (80 / viewport)}px)` }} />}
          </div>
          <div><p className="text-sm font-semibold">Так будет выглядеть аватар</p><p className="mt-1 text-xs text-muted-foreground">Круг справа показывает итоговый кадр профиля.</p></div>
        </div>

        <p className="text-xs text-muted-foreground">Потяни изображение мышью или пальцем. В квадрат попадёт только выбранная область.</p>
        <div className="flex justify-end gap-2 pt-1"><button type="button" onClick={onCancel} className="rounded-xl border border-white/10 px-4 py-2.5 text-sm font-semibold hover:bg-white/5">Отмена</button><button type="button" disabled={saving || !natural.width} onClick={() => void complete()} className="inline-flex items-center gap-2 rounded-xl bg-rating px-4 py-2.5 text-sm font-bold text-black hover:bg-rating/90 disabled:opacity-50"><Check className="h-4 w-4" /> {saving ? "Готовим…" : "Применить кадр"}</button></div>
      </div>
    </div>
  </div>;
}

async function cropImage(src: string, natural: Size, viewport: number, scale: number, offset: Point) {
  const image = await loadImage(src);
  const sourceWidth = viewport / scale;
  const sourceHeight = viewport / scale;
  const sourceX = Math.max(0, Math.min(natural.width - sourceWidth, (natural.width - sourceWidth) / 2 - offset.x / scale));
  const sourceY = Math.max(0, Math.min(natural.height - sourceHeight, (natural.height - sourceHeight) / 2 - offset.y / scale));
  const canvas = document.createElement("canvas");
  canvas.width = 640;
  canvas.height = 640;
  const context = canvas.getContext("2d");
  if (!context) throw new Error("Не удалось обработать изображение");
  context.drawImage(image, sourceX, sourceY, sourceWidth, sourceHeight, 0, 0, 640, 640);
  const blob = await new Promise<Blob>((resolve, reject) => canvas.toBlob(value => value ? resolve(value) : reject(new Error("Не удалось подготовить изображение")), "image/jpeg", .9));
  return new File([blob], "avatar.jpg", { type: "image/jpeg" });
}

function loadImage(src: string) { return new Promise<HTMLImageElement>((resolve, reject) => { const image = new Image(); image.onload = () => resolve(image); image.onerror = reject; image.src = src; }); }
