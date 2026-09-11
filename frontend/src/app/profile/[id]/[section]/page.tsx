import { PublicProfileListView } from "@/components/profile/PublicProfileListView";
import type { Metadata } from "next";
import { getSeoProfile } from "@/lib/seo";

export async function generateMetadata({ params }: { params: Promise<{ id: string; section: string }> }): Promise<Metadata> {
  const { id, section } = await params;
  const profile = await getSeoProfile(id);
  const titles: Record<string, string> = { ratings: "Оценки", favorites: "Избранное", followers: "Подписчики", following: "Подписки" };
  const sectionTitle = titles[section] ?? "Профиль";
  return { title: `${sectionTitle}${profile ? ` — ${profile.user.name}` : ""}`, robots: { index: Boolean(profile), follow: Boolean(profile) }, alternates: { canonical: `/profile/${id}/${section}` } };
}

export default async function Page({ params }: { params: Promise<{ id: string; section: string }> }) {
  const { id, section } = await params;
  return <PublicProfileListView id={id} section={section} />;
}
