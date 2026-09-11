import type { ApiMovie, ApiReel, PublicProfile } from "@/lib/api";

function apiOrigin() {
  const configured = process.env.SEO_API_URL ?? process.env.NEXT_PUBLIC_API_URL;
  if (configured && /^https?:\/\//.test(configured)) return configured.replace(/\/$/, "");
  return "http://api:8000/api/v1";
}

export async function seoFetch<T>(path: string): Promise<T | null> {
  try {
    const response = await fetch(`${apiOrigin()}${path}`, { next: { revalidate: 300 } });
    if (!response.ok) return null;
    return (await response.json()) as T;
  } catch {
    return null;
  }
}

export async function getSeoMovie(id: string, isSeries = false) {
  return seoFetch<ApiMovie>(`/movies/${encodeURIComponent(id)}${isSeries ? "?isSeries=true" : ""}`);
}

export async function getSeoProfile(id: string) {
  return seoFetch<PublicProfile>(`/users/${encodeURIComponent(id)}`);
}

export async function getSeoReel(slug: string) {
  const response = await seoFetch<{ items?: ApiReel[]; results?: ApiReel[] } | ApiReel[]>("/filmstrips");
  const items = Array.isArray(response) ? response : response?.items ?? response?.results ?? [];
  return items.find((item) => item.slug === slug) ?? null;
}
