import { PublicProfileView } from "@/components/profile/PublicProfileView";
import type { Metadata } from "next";
import { getSeoProfile } from "@/lib/seo";
import { absoluteUrl, truncateDescription } from "@/lib/site";
import { StructuredData } from "@/components/common/StructuredData";

export async function generateMetadata({ params }: { params: Promise<{ id: string }> }): Promise<Metadata> {
  const { id } = await params;
  const profile = await getSeoProfile(id);
  const name = profile?.user.name ?? "Публичный профиль";
  const description = profile ? `${name}: ${profile.statistics.ratingsCount} оценок, ${profile.statistics.watchedCount} просмотренных фильмов в СвайпКино.` : "Публичный профиль пользователя СвайпКино.";
  const path = `/profile/${id}`;
  return { title: name, description: truncateDescription(description, "Публичный профиль СвайпКино."), alternates: { canonical: path }, openGraph: { title: name, description, url: absoluteUrl(path), type: "profile", images: profile?.user.avatarUrl ? [{ url: profile.user.avatarUrl, alt: name }] : [{ url: absoluteUrl("/brand/swapkino-logo-white.png") }] }, twitter: { card: "summary", title: name, description } };
}

export default async function Page({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const profile = await getSeoProfile(id);
  return <><StructuredData data={{ "@context": "https://schema.org", "@type": "ProfilePage", name: profile?.user.name ?? "Публичный профиль", url: absoluteUrl(`/profile/${id}`), mainEntity: { "@type": "Person", name: profile?.user.name ?? "Пользователь", image: profile?.user.avatarUrl ?? undefined } }} /><PublicProfileView id={id} /></>;
}
