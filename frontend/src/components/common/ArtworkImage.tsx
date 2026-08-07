"use client";

import { useEffect, useState, type ImgHTMLAttributes } from "react";
import { Clapperboard } from "lucide-react";
import { cn } from "@/lib/utils";

type ArtworkImageProps = Omit<ImgHTMLAttributes<HTMLImageElement>, "src"> & {
  src?: string | null;
  title: string;
  fallbackLabel?: string;
};

/** Keeps broken or absent artwork from turning a card into an empty black box. */
export function ArtworkImage({ src, title, fallbackLabel = "Изображение пока недоступно", className, onError, ...props }: ArtworkImageProps) {
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    setFailed(false);
  }, [src]);

  if (!src || failed) {
    return <div role="img" aria-label={`${fallbackLabel}: ${title}`} className={cn("relative grid place-items-center overflow-hidden bg-gradient-to-br from-zinc-800 via-zinc-900 to-neutral-950 text-center", className)}>
      <div className="absolute inset-0 opacity-30 [background-image:radial-gradient(circle_at_25%_20%,rgba(255,255,255,0.16),transparent_38%),radial-gradient(circle_at_75%_80%,rgba(250,204,21,0.13),transparent_34%)]"/>
      <div className="relative max-w-[85%] px-4 text-white/75">
        <Clapperboard className="mx-auto mb-3 h-8 w-8 text-rating/70" aria-hidden="true"/>
        <p className="line-clamp-3 text-sm font-semibold leading-snug">{title}</p>
        <p className="mt-1.5 text-[10px] font-medium uppercase tracking-[0.16em] text-white/40">{fallbackLabel}</p>
      </div>
    </div>;
  }

  return <img
    {...props}
    src={src}
    className={className}
    onError={(event) => {
      setFailed(true);
      onError?.(event);
    }}
  />;
}
