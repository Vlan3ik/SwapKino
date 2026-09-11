"use client";

import { useEffect } from "react";
import { usePathname } from "next/navigation";

declare global {
  interface Window {
    plausible?: ((event: string, options?: { props?: Record<string, string | number | boolean> }) => void) & { q?: unknown[] };
  }
}

const enabled = process.env.NEXT_PUBLIC_PLAUSIBLE_ENABLED === "true";
const basePath = (process.env.NEXT_PUBLIC_PLAUSIBLE_BASE_PATH ?? "/statsadmin").replace(/\/$/, "");
const domain = process.env.NEXT_PUBLIC_PLAUSIBLE_DOMAIN ?? "stmstmstmstmstm.ru";

export function PlausibleAnalytics() {
  const pathname = usePathname();

  useEffect(() => {
    if (!enabled) return;
    window.plausible = window.plausible || Object.assign(((...args: unknown[]) => {
      (window.plausible!.q = window.plausible!.q || []).push(args);
    }) as NonNullable<Window["plausible"]>, { q: [] });
    if (!document.getElementById("plausible-script")) {
      const script = document.createElement("script");
      script.id = "plausible-script";
      script.defer = true;
      script.dataset.domain = domain;
      script.src = `${basePath}/js/script.js`;
      document.head.appendChild(script);
    }
  }, []);

  useEffect(() => {
    if (enabled && pathname) window.plausible?.("pageview", { props: { path: pathname } });
  }, [pathname]);

  return null;
}

export function trackEvent(name: string, props?: Record<string, string | number | boolean>) {
  if (enabled && typeof window !== "undefined") window.plausible?.(name, props ? { props } : undefined);
}
