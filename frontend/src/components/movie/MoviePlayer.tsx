"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { LoaderCircle, Play, RotateCcw, TriangleAlert } from "lucide-react";
import Script from "next/script";
import { api, type ApiMoviePlayer } from "@/lib/api";
import { cn } from "@/lib/utils";

type Availability = "loading" | "available" | "unavailable" | "error";

interface MoviePlayerProps {
  movieId: number;
  isSeries: boolean;
  title: string;
  onAvailabilityChange?: (availability: Availability) => void;
}

const PROVIDERS = [
  { key: "vibix", label: "Vibix" },
] as const;

function providerKey(value: string) {
  return value.toLocaleLowerCase().replace(/[^a-zа-яё0-9]/g, "");
}

function safeEmbedUrl(value: string | null): string | null {
  if (!value) return null;
  try {
    const url = new URL(value);
    return url.protocol === "https:" || url.protocol === "http:" ? url.toString() : null;
  } catch {
    return null;
  }
}

export function MoviePlayer({ movieId, isSeries, title, onAvailabilityChange }: MoviePlayerProps) {
  const [players, setPlayers] = useState<ApiMoviePlayer[]>([]);
  const [availability, setAvailability] = useState<Availability>("loading");
  const [activeProvider, setActiveProvider] = useState<string | null>(null);
  const [frameState, setFrameState] = useState<"loading" | "ready" | "error">("loading");
  const [slow, setSlow] = useState(false);
  const [frameAttempt, setFrameAttempt] = useState(0);
  const tabRefs = useRef<Record<string, HTMLButtonElement | null>>({});

  const load = useCallback(async () => {
    setAvailability("loading");
    setPlayers([]);
    setActiveProvider(null);
    try {
      const response = await api.moviePlayers(movieId, isSeries);
      const items = Array.isArray(response.items) ? response.items : [];
      setPlayers(items);
      const first = PROVIDERS.find((provider) => {
        const item = items.find((row) => providerKey(row.provider) === provider.key);
        return item?.available && (Boolean(item.embed) || Boolean(safeEmbedUrl(item.embedUrl)));
      });
      setActiveProvider(first?.key ?? null);
      setAvailability(first ? "available" : "unavailable");
    } catch {
      setAvailability("error");
    }
  }, [movieId, isSeries]);

  useEffect(() => { void load(); }, [load]);
  useEffect(() => { onAvailabilityChange?.(availability); }, [availability, onAvailabilityChange]);

  const sources = useMemo(() => PROVIDERS.map((provider) => {
    const item = players.find((row) => providerKey(row.provider) === provider.key);
    const embedUrl = safeEmbedUrl(item?.embedUrl ?? null);
    return { ...provider, embedUrl, embed: item?.embed ?? null, available: Boolean(item?.available && (item.embed || embedUrl)) };
  }), [players]);
  const active = sources.find((source) => source.key === activeProvider && source.available) ?? null;

  useEffect(() => {
    if (!active) return;
    setFrameState(active.embed ? "ready" : "loading");
    if (active.embed) return;
    setSlow(false);
    const timeout = window.setTimeout(() => setSlow(true), 10_000);
    return () => window.clearTimeout(timeout);
  }, [active?.key, active?.embed?.id, frameAttempt]);

  const select = (key: string) => {
    const source = sources.find((row) => row.key === key);
    if (!source?.available) return;
    setActiveProvider(key);
    setFrameAttempt(0);
  };

  const moveTabFocus = (key: string, direction: 1 | -1) => {
    const enabled = sources.filter((source) => source.available);
    const index = enabled.findIndex((source) => source.key === key);
    const next = enabled[(index + direction + enabled.length) % enabled.length];
    if (next) {
      select(next.key);
      tabRefs.current[next.key]?.focus();
    }
  };

  return (
    <section id="watch" tabIndex={-1} aria-labelledby="watch-title" className="scroll-mt-20 outline-none">
      <h2 id="watch-title" className="mb-3 flex items-center gap-2 text-xl font-bold">
        <span className="w-1 self-stretch rounded-full bg-rating" />
        <Play className="h-5 w-5 fill-current" /> Смотреть
      </h2>
      <div className="overflow-hidden rounded-2xl border border-white/10 bg-black shadow-cinematic">
        <div role="tablist" aria-label="Источник видео" className="grid grid-cols-1 gap-px border-b border-white/10 bg-white/10 p-px">
          {sources.map((source) => (
            <button
              key={source.key}
              ref={(node) => { tabRefs.current[source.key] = node; }}
              type="button"
              role="tab"
              id={`player-tab-${source.key}`}
              aria-controls="movie-player-panel"
              aria-selected={source.key === activeProvider}
              aria-disabled={!source.available}
              disabled={!source.available}
              tabIndex={source.key === activeProvider || (!activeProvider && source.available) ? 0 : -1}
              onClick={() => select(source.key)}
              onKeyDown={(event) => {
                if (event.key === "ArrowRight" || event.key === "ArrowLeft") {
                  event.preventDefault();
                  moveTabFocus(source.key, event.key === "ArrowRight" ? 1 : -1);
                } else if (event.key === "Home" || event.key === "End") {
                  event.preventDefault();
                  const enabled = sources.filter((row) => row.available);
                  const target = event.key === "Home" ? enabled[0] : enabled.at(-1);
                  if (target) { select(target.key); tabRefs.current[target.key]?.focus(); }
                }
              }}
              className={cn(
                "min-h-12 bg-background/95 px-3 py-2 text-sm font-semibold transition-colors focus-visible:z-10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-rating",
                source.key === activeProvider && "bg-white text-black",
                !source.available && "cursor-not-allowed text-muted-foreground opacity-45",
                source.available && source.key !== activeProvider && "hover:bg-white/10",
              )}
            >
              {source.label}
              {!source.available && availability !== "loading" && <span className="ml-1 hidden text-[10px] font-normal sm:inline">нет</span>}
            </button>
          ))}
        </div>

        <div
          id="movie-player-panel"
          role="tabpanel"
          aria-labelledby={active ? `player-tab-${active.key}` : undefined}
          className="relative aspect-video w-full bg-black"
        >
          {availability === "loading" && <PlayerMessage icon={<LoaderCircle className="h-7 w-7 animate-spin" />} title="Ищем доступные источники…" />}
          {availability === "error" && <PlayerMessage icon={<TriangleAlert className="h-7 w-7 text-skip" />} title="Не удалось загрузить источники" text="Проверьте соединение и попробуйте ещё раз." action={<Retry onClick={() => void load()} />} />}
          {availability === "unavailable" && <PlayerMessage icon={<Play className="h-7 w-7" />} title="Просмотр пока недоступен" text="Для этого фильма нет доступных источников." />}
          {(active?.embedUrl || active?.embed) && (
            <>
              {frameState !== "ready" && frameState !== "error" && <PlayerMessage icon={<LoaderCircle className="h-7 w-7 animate-spin" />} title={slow ? "Плеер загружается дольше обычного" : "Загружаем плеер…"} text={slow ? "Можно подождать или загрузить его ещё раз." : undefined} action={slow ? <Retry onClick={() => setFrameAttempt((value) => value + 1)} /> : undefined} />}
              {frameState === "error" && <PlayerMessage icon={<TriangleAlert className="h-7 w-7 text-skip" />} title="Плеер не загрузился" text="Попробуйте ещё раз или выберите другой источник." action={<Retry onClick={() => setFrameAttempt((value) => value + 1)} />} />}
              {active.embed ? <VibixEmbed embed={active.embed} /> : <iframe
                key={`${active.key}:${frameAttempt}`}
                src={active.embedUrl ?? ""}
                title={`${active.label}: ${title}`}
                loading="lazy"
                allow="autoplay; encrypted-media; fullscreen; picture-in-picture"
                allowFullScreen
                sandbox="allow-scripts allow-same-origin allow-forms allow-presentation"
                referrerPolicy="no-referrer"
                onLoad={() => { setFrameState("ready"); setSlow(false); }}
                onError={() => setFrameState("error")}
                className={cn("absolute inset-0 h-full w-full border-0", frameState !== "ready" && "invisible")}
              />}
            </>
          )}
        </div>
      </div>
    </section>
  );
}

function VibixEmbed({ embed }: { embed: { publisherId: string; type: string; id: string } }) {
  return <><Script src="https://graphicslab.io/sdk/v2/rendex-sdk.min.js" strategy="afterInteractive" /><ins data-publisher-id={embed.publisherId} data-type={embed.type} data-id={embed.id} data-design="5" data-nopreload="true" data-width="100%" data-height="500px" className="block min-h-[280px] w-full" /></>;
}

function PlayerMessage({ icon, title, text, action }: { icon: React.ReactNode; title: string; text?: string; action?: React.ReactNode }) {
  return <div className="absolute inset-0 z-10 grid place-items-center bg-black p-5 text-center"><div className="flex max-w-md flex-col items-center"><div className="mb-3 text-white/70">{icon}</div><p className="font-semibold">{title}</p>{text && <p className="mt-1 text-sm text-white/55">{text}</p>}{action && <div className="mt-4">{action}</div>}</div></div>;
}

function Retry({ onClick }: { onClick: () => void }) {
  return <button type="button" onClick={onClick} className="inline-flex items-center gap-2 rounded-full border border-white/20 px-4 py-2 text-sm font-semibold hover:bg-white/10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-rating"><RotateCcw className="h-4 w-4" />Повторить</button>;
}
