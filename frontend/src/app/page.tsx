"use client";

import { useEffect } from "react";
import { Toaster as SonnerToaster } from "sonner";
import { Header } from "@/components/common/Header";
import { Footer } from "@/components/common/Footer";
import { FeedView } from "@/components/feed/FeedView";
import { CatalogView } from "@/components/catalog/CatalogView";
import { ProfileView } from "@/components/profile/ProfileView";
import { FavoritesView } from "@/components/favorites/FavoritesView";
import { RatingsView } from "@/components/ratings/RatingsView";
import { LicenseView } from "@/components/legal/LicenseView";
import { PrivacyView } from "@/components/legal/PrivacyView";
import { TermsView } from "@/components/legal/TermsView";
import { MovieCardView } from "@/components/movie/MovieCardView";
import { useAppStore } from "@/lib/store";
import { AnimatePresence, motion } from "framer-motion";

export default function Home() {
  const view = useAppStore((s) => s.view);
  const activeReelId = useAppStore((s) => s.activeReelId);
  const restoreSession = useAppStore((s) => s.restoreSession);
  const syncViewFromUrl = useAppStore((s) => s.syncViewFromUrl);

  useEffect(() => {
    void restoreSession().catch(() => undefined);
  }, [restoreSession]);

  useEffect(() => {
    syncViewFromUrl();
    window.addEventListener("popstate", syncViewFromUrl);
    return () => window.removeEventListener("popstate", syncViewFromUrl);
  }, [syncViewFromUrl]);

  // Скрываем футер в режиме свайпа — карточка должна занимать максимум места,
  // а футер там только отвлекает от фокуса на выборе фильма.
  const showFooter = !(view.name === "feed" && activeReelId);

  return (
    <div className="min-h-screen flex flex-col bg-background">
      <Header />

      <main className="flex-1">
        <AnimatePresence mode="wait">
          <motion.div
            key={viewKey(view)}
            initial={{ opacity: 0, y: 8 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: -8 }}
            transition={{ duration: 0.22, ease: "easeOut" }}
          >
            {renderView(view)}
          </motion.div>
        </AnimatePresence>
      </main>

      {/* Футер (скрыт в фокусированном режиме свайпа) */}
      {showFooter && <Footer />}

      {/* Sonner для тостов */}
      <SonnerToaster
        position="bottom-center"
        theme="dark"
        toastOptions={{
          style: {
            background: "oklch(0.16 0.006 270 / 95%)",
            border: "1px solid oklch(1 0 0 / 10%)",
            color: "oklch(0.98 0 0)",
            backdropFilter: "blur(20px)",
          },
        }}
      />
    </div>
  );
}

function viewKey(view: ReturnType<typeof useAppStore.getState>["view"]): string {
  if (view.name === "movie") return `movie-${view.movieId}`;
  return view.name;
}

function renderView(view: ReturnType<typeof useAppStore.getState>["view"]) {
  switch (view.name) {
    case "feed":
      return <FeedView />;
    case "catalog":
      return <CatalogView />;
    case "profile":
      return <ProfileView />;
    case "favorites":
      return <FavoritesView />;
    case "ratings":
      return <RatingsView />;
    case "license":
      return <LicenseView />;
    case "privacy":
      return <PrivacyView />;
    case "terms":
      return <TermsView />;
    case "movie":
      return <MovieCardView movieId={view.movieId} />;
    default:
      return <FeedView />;
  }
}
