import type { MetadataRoute } from "next";
import { absoluteUrl } from "@/lib/site";
import { seoFetch } from "@/lib/seo";

type MoviePage = { items?: Array<{ tmdbId?: number; id?: number; isSeries?: boolean }>; results?: Array<{ tmdbId?: number; id?: number; isSeries?: boolean }>; totalPages?: number };
type Reel = { slug: string; updatedAt?: string };
type ProfilePage = { items?: Array<{ id: string; lastModified?: string }>; totalPages?: number };

export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  const staticPages = ["/", "/catalog", "/about", "/license", "/privacy", "/terms", "/copyright"].map((path) => ({ url: absoluteUrl(path), changeFrequency: "weekly" as const, priority: path === "/" ? 1 : 0.5 }));
  const movies: MetadataRoute.Sitemap = [];
  for (let page = 1; page <= 20; page += 1) {
    const data = await seoFetch<MoviePage>(`/movies?page=${page}&limit=100&sort=newest`);
    const rows = data?.items ?? data?.results ?? [];
    for (const movie of rows) {
      const id = movie.tmdbId ?? movie.id;
      if (id) movies.push({ url: absoluteUrl(`/movie/${id}${movie.isSeries ? "?series=1" : ""}`), changeFrequency: "monthly", priority: 0.7 });
    }
    if (!data || !data.totalPages || page >= data.totalPages || rows.length === 0) break;
  }
  const reels = await seoFetch<{ items?: Reel[]; results?: Reel[] } | Reel[]>("/filmstrips");
  const reelItems = Array.isArray(reels) ? reels : reels?.items ?? reels?.results ?? [];
  const reelUrls = reelItems.map((reel) => ({ url: absoluteUrl(`/filmstrips/${reel.slug}`), lastModified: reel.updatedAt ? new Date(reel.updatedAt) : undefined, changeFrequency: "weekly" as const, priority: 0.7 }));
  const profiles: MetadataRoute.Sitemap = [];
  for (let page = 1; page <= 20; page += 1) {
    const data = await seoFetch<ProfilePage>(`/users/sitemap?page=${page}&pageSize=500`);
    const rows = data?.items ?? [];
    profiles.push(...rows.map((profile) => ({ url: absoluteUrl(`/profile/${profile.id}`), lastModified: profile.lastModified ? new Date(profile.lastModified) : undefined, changeFrequency: "weekly" as const, priority: 0.4 })));
    if (!data || !data.totalPages || page >= data.totalPages || rows.length === 0) break;
  }
  return [...staticPages, ...movies, ...reelUrls, ...profiles];
}
