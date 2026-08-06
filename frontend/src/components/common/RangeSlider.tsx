"use client";

import {
  useRef,
  useCallback,
  useEffect,
  useState,
  PointerEvent as ReactPointerEvent,
} from "react";

interface RangeSliderProps {
  min: number;
  max: number;
  step?: number;
  value: [number, number];
  onChange: (value: [number, number]) => void;
  /** Цвет активной дорожки */
  accent?: string;
  /** Высота зоны взаимодействия */
  height?: number;
}

type Thumb = "lo" | "hi";

/**
 * Двухползунковый range slider с pointer events.
 * Полностью кастомный — без нативных input[type=range].
 * Поддерживает тач, мышь, клавиатуру.
 */
export function RangeSlider({
  min,
  max,
  step = 1,
  value,
  onChange,
  accent = "var(--rating)",
  height = 36,
}: RangeSliderProps) {
  const [lo, hi] = value;
  const trackRef = useRef<HTMLDivElement | null>(null);
  const [activeThumb, setActiveThumb] = useState<Thumb | null>(null);
  const [hoveredThumb, setHoveredThumb] = useState<Thumb | null>(null);
  const [containerWidth, setContainerWidth] = useState(0);

  const range = max - min;
  const loPct = range === 0 ? 0 : ((lo - min) / range) * 100;
  const hiPct = range === 0 ? 100 : ((hi - min) / range) * 100;

  // Следим за шириной трека
  useEffect(() => {
    if (!trackRef.current) return;
    const el = trackRef.current;
    const update = () => setContainerWidth(el.offsetWidth);
    update();
    const ro = new ResizeObserver(update);
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  // Преобразовать px в значение
  const pxToValue = useCallback(
    (px: number) => {
      if (!trackRef.current || containerWidth === 0) return min;
      const rect = trackRef.current.getBoundingClientRect();
      const rel = px - rect.left;
      const pct = Math.max(0, Math.min(1, rel / rect.width));
      const raw = min + pct * range;
      // Округлить до step
      const stepped = Math.round(raw / step) * step;
      return Math.max(min, Math.min(max, stepped));
    },
    [min, max, range, step, containerWidth]
  );

  const updateValue = useCallback(
    (thumb: Thumb, px: number) => {
      const v = pxToValue(px);
      if (thumb === "lo") {
        const next = Math.min(v, hi - step);
        if (next !== lo) onChange([next, hi]);
      } else {
        const next = Math.max(v, lo + step);
        if (next !== hi) onChange([lo, next]);
      }
    },
    [lo, hi, step, onChange, pxToValue]
  );

  // Pointer events на самом треке — клик двигает ближайший ползунок
  const handleTrackPointerDown = (e: ReactPointerEvent<HTMLDivElement>) => {
    if (e.button !== 0 && e.pointerType === "mouse") return;
    const v = pxToValue(e.clientX);
    // Какой ползунок ближе к клику
    const distLo = Math.abs(v - lo);
    const distHi = Math.abs(v - hi);
    const thumb: Thumb = distLo <= distHi ? "lo" : "hi";
    setActiveThumb(thumb);
    updateValue(thumb, e.clientX);
    (e.target as HTMLElement).setPointerCapture?.(e.pointerId);
  };

  // Pointer move на документе — для перетаскивания
  useEffect(() => {
    if (!activeThumb) return;
    const handleMove = (e: PointerEvent) => {
      updateValue(activeThumb, e.clientX);
    };
    const handleUp = () => setActiveThumb(null);
    document.addEventListener("pointermove", handleMove);
    document.addEventListener("pointerup", handleUp);
    document.addEventListener("pointercancel", handleUp);
    return () => {
      document.removeEventListener("pointermove", handleMove);
      document.removeEventListener("pointerup", handleUp);
      document.removeEventListener("pointercancel", handleUp);
    };
  }, [activeThumb, updateValue]);

  // Клавиатура
  const handleKeyDown = (thumb: Thumb, e: React.KeyboardEvent) => {
    const cur = thumb === "lo" ? lo : hi;
    let next = cur;
    switch (e.key) {
      case "ArrowLeft":
      case "ArrowDown":
        next = cur - step;
        break;
      case "ArrowRight":
      case "ArrowUp":
        next = cur + step;
        break;
      case "Home":
        next = thumb === "lo" ? min : lo + step;
        break;
      case "End":
        next = thumb === "hi" ? max : hi - step;
        break;
      default:
        return;
    }
    e.preventDefault();
    if (thumb === "lo") {
      const clamped = Math.max(min, Math.min(next, hi - step));
      if (clamped !== lo) onChange([clamped, hi]);
    } else {
      const clamped = Math.max(lo + step, Math.min(next, max));
      if (clamped !== hi) onChange([lo, clamped]);
    }
  };

  return (
    <div
      className="relative w-full select-none touch-none"
      style={{ height }}
    >
      {/* Трек — кликабельная зона */}
      <div
        ref={trackRef}
        onPointerDown={handleTrackPointerDown}
        className="absolute left-0 right-0 cursor-pointer"
        style={{
          top: "50%",
          transform: "translateY(-50%)",
          height: Math.max(height, 28),
        }}
      >
        {/* Фоновая дорожка */}
        <div
          className="absolute left-0 right-0 rounded-full bg-white/10"
          style={{
            top: "50%",
            transform: "translateY(-50%)",
            height: 6,
          }}
        />
        {/* Активный диапазон */}
        <div
          className="absolute rounded-full pointer-events-none"
          style={{
            top: "50%",
            transform: "translateY(-50%)",
            height: 6,
            left: `${loPct}%`,
            width: `${Math.max(0, hiPct - loPct)}%`,
            background: accent,
            boxShadow: `0 0 12px ${accent}40`,
          }}
        />
      </div>

      {/* Ползунок LO */}
      <ThumbButton
        pct={loPct}
        accent={accent}
        isActive={activeThumb === "lo"}
        isHovered={hoveredThumb === "lo"}
        onPointerEnter={() => setHoveredThumb("lo")}
        onPointerLeave={() => setHoveredThumb(null)}
        onPointerDown={(e) => {
          e.stopPropagation();
          (e.target as HTMLElement).setPointerCapture?.(e.pointerId);
          setActiveThumb("lo");
        }}
        onKeyDown={(e) => handleKeyDown("lo", e)}
        ariaLabel={`Минимальный год: ${lo}`}
        ariaValueNow={lo}
        ariaValueMin={min}
        ariaValueMax={max}
      />

      {/* Ползунок HI */}
      <ThumbButton
        pct={hiPct}
        accent={accent}
        isActive={activeThumb === "hi"}
        isHovered={hoveredThumb === "hi"}
        onPointerEnter={() => setHoveredThumb("hi")}
        onPointerLeave={() => setHoveredThumb(null)}
        onPointerDown={(e) => {
          e.stopPropagation();
          (e.target as HTMLElement).setPointerCapture?.(e.pointerId);
          setActiveThumb("hi");
        }}
        onKeyDown={(e) => handleKeyDown("hi", e)}
        ariaLabel={`Максимальный год: ${hi}`}
        ariaValueNow={hi}
        ariaValueMin={min}
        ariaValueMax={max}
      />
    </div>
  );
}

function ThumbButton({
  pct,
  accent,
  isActive,
  isHovered,
  onPointerEnter,
  onPointerLeave,
  onPointerDown,
  onKeyDown,
  ariaLabel,
  ariaValueNow,
  ariaValueMin,
  ariaValueMax,
}: {
  pct: number;
  accent: string;
  isActive: boolean;
  isHovered: boolean;
  onPointerEnter: () => void;
  onPointerLeave: () => void;
  onPointerDown: (e: ReactPointerEvent<HTMLDivElement>) => void;
  onKeyDown: (e: React.KeyboardEvent) => void;
  ariaLabel: string;
  ariaValueNow: number;
  ariaValueMin: number;
  ariaValueMax: number;
}) {
  return (
    <div
      role="slider"
      tabIndex={0}
      aria-label={ariaLabel}
      aria-valuenow={ariaValueNow}
      aria-valuemin={ariaValueMin}
      aria-valuemax={ariaValueMax}
      onPointerDown={onPointerDown}
      onPointerEnter={onPointerEnter}
      onPointerLeave={onPointerLeave}
      onKeyDown={onKeyDown}
      className="absolute top-1/2 outline-none cursor-grab active:cursor-grabbing"
      style={{
        left: `${pct}%`,
        transform: "translate(-50%, -50%)",
        width: 22,
        height: 22,
        borderRadius: "9999px",
        background: "white",
        border: `2px solid ${accent}`,
        boxShadow: isActive
          ? `0 0 0 6px ${accent}30, 0 4px 14px rgba(0,0,0,0.5)`
          : isHovered
          ? `0 0 0 4px ${accent}20, 0 2px 8px rgba(0,0,0,0.4)`
          : "0 2px 8px rgba(0,0,0,0.4)",
        transition: isActive
          ? "none"
          : "box-shadow 0.15s ease, transform 0.15s ease",
        zIndex: isActive ? 20 : 10,
        touchAction: "none",
      }}
    />
  );
}
