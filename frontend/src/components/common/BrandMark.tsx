import { cn } from "@/lib/utils";

export function BrandMark({ className, alt = "" }: { className?: string; alt?: string }) {
  return (
    <img
      src="/brand/swapkino-logo-white.png"
      alt={alt}
      aria-hidden={alt ? undefined : true}
      className={cn("object-contain", className)}
    />
  );
}
