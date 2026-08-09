"use client";

import Script from "next/script";

const PUBLISHER_ID = "678712186";

export function VibixPlayer({ movieId, isSeries }: { movieId: number; isSeries: boolean }) {
  return (
    <section id="watch" className="scroll-mt-20">
      <Script src="https://graphicslab.io/sdk/v2/rendex-sdk.min.js" strategy="afterInteractive" />
      <h2 className="mb-3 flex items-center gap-2 text-xl font-bold">
        <span className="w-1 self-stretch rounded-full bg-rating" />
        Смотреть
      </h2>
      <div className="overflow-hidden rounded-2xl border border-white/10 bg-black shadow-cinematic">
        <ins
          data-publisher-id={PUBLISHER_ID}
          data-type={isSeries ? "series" : "movie"}
          data-id={String(movieId)}
          data-design="5"
          data-nopreload="true"
          data-width="100%"
          data-height="500px"
          className="block min-h-[280px] w-full"
        />
      </div>
    </section>
  );
}
