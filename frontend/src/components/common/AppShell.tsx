"use client";

import { useEffect } from "react";
import { usePathname } from "next/navigation";
import { Toaster as SonnerToaster } from "sonner";
import { Header } from "./Header";
import { Footer } from "./Footer";
import { useAppStore } from "@/lib/store";

export function AppShell({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const restoreSession = useAppStore((state) => state.restoreSession);
  useEffect(() => { void restoreSession().catch(() => undefined); }, [restoreSession]);
  const focusedReel = pathname.startsWith("/reels/");

  return (
    <div className="min-h-screen flex flex-col bg-background">
      <Header />
      <main className="flex-1">{children}</main>
      {!focusedReel && <Footer />}
      <SonnerToaster position="bottom-center" theme="dark" />
    </div>
  );
}
