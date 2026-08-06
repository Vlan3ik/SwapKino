"use client";

import { useState } from "react";
import { Star, X } from "lucide-react";
import { motion } from "framer-motion";
import { useAppStore } from "@/lib/store";
import { cn } from "@/lib/utils";

interface RatingControlProps {
  movieId: number;
  /** Показывать компактную версию (только звёзды) */
  compact?: boolean;
}

/**
 * Контрол оценки фильма 1-10.
 * Использует звёзды (10 звёзд = 10 баллов).
 * Значение сохраняется на backend для авторизованного пользователя.
 */
export function RatingControl({ movieId, compact = false }: RatingControlProps) {
  const { getRating, setRating, removeRating } = useAppStore();
  const stored = getRating(movieId);
  const [hover, setHover] = useState<number | null>(null);

  const display = hover ?? stored ?? 0;

  if (compact) {
    return (
      <div className="flex items-center gap-1">
        {[1, 2, 3, 4, 5, 6, 7, 8, 9, 10].map((n) => (
          <button
            key={n}
            onMouseEnter={() => setHover(n)}
            onMouseLeave={() => setHover(null)}
            onClick={() => {
              if (stored === n) {
                removeRating(movieId);
              } else {
                setRating(movieId, n);
              }
            }}
            className="p-0.5"
            aria-label={`Оценка ${n}`}
          >
            <Star
              className={cn(
                "h-3.5 w-3.5 transition-colors",
                n <= display ? "text-rating" : "text-white/15"
              )}
              fill={n <= display ? "currentColor" : "none"}
            />
          </button>
        ))}
        {stored !== null && (
          <span className="ml-1 text-xs font-bold text-rating tabular-nums">
            {stored}/10
          </span>
        )}
      </div>
    );
  }

  return (
    <div className="glass-panel rounded-2xl p-5">
      <div className="flex items-center justify-between mb-3">
        <div>
          <h3 className="font-semibold text-sm">Твоя оценка</h3>
          <p className="text-xs text-muted-foreground mt-0.5">
            {stored === null
              ? "Кликни на звезду, чтобы оценить"
              : `Ты поставил ${stored} из 10`}
          </p>
        </div>
        {stored !== null && (
          <div className="text-right">
            <div className="text-2xl font-bold text-rating tabular-nums leading-none">
              {stored}
              <span className="text-sm text-muted-foreground">/10</span>
            </div>
            <button
              onClick={() => removeRating(movieId)}
              className="text-[10px] text-muted-foreground hover:text-foreground flex items-center gap-1 mt-1"
            >
              <X className="h-3 w-3" />
              Сбросить
            </button>
          </div>
        )}
      </div>

      <div className="flex items-center gap-1">
        {[1, 2, 3, 4, 5, 6, 7, 8, 9, 10].map((n) => (
          <motion.button
            key={n}
            whileHover={{ scale: 1.15, y: -2 }}
            whileTap={{ scale: 0.9 }}
            onMouseEnter={() => setHover(n)}
            onMouseLeave={() => setHover(null)}
            onClick={() => setRating(movieId, n)}
            aria-label={`Поставить ${n}`}
            className="p-1"
          >
            <Star
              className={cn(
                "h-5 w-5 sm:h-6 sm:w-6 transition-all",
                n <= display
                  ? "text-rating drop-shadow-[0_0_8px_var(--rating)]"
                  : "text-white/15"
              )}
              fill={n <= display ? "currentColor" : "none"}
            />
          </motion.button>
        ))}
      </div>
      <div className="flex justify-between mt-2 text-[10px] text-muted-foreground/60">
        <span>1</span>
        <span>5</span>
        <span>10</span>
      </div>
    </div>
  );
}
