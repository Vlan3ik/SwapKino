import { SwipeDeck } from "@/components/feed/SwipeDeck";
import type { Metadata } from "next";
import { getSeoReel } from "@/lib/seo";
import { absoluteUrl, truncateDescription } from "@/lib/site";

export async function generateMetadata({ params }: { params: Promise<{ slug: string }> }): Promise<Metadata> {
  const { slug } = await params;
  const reel = await getSeoReel(slug);
  const title = reel?.title ?? "Киноплёнка";
  const description = truncateDescription(reel?.description ?? reel?.subtitle, "Подборка фильмов и сериалов в СвайпКино.");
  const path = `/filmstrips/${slug}`;
  return { title, description, alternates: { canonical: path }, openGraph: { title, description, url: absoluteUrl(path), type: "website", images: reel?.coverUrl ? [{ url: reel.coverUrl, alt: title }] : [{ url: absoluteUrl("/brand/swapkino-logo-white.png") }] }, twitter: { card: "summary_large_image", title, description } };
}

export default async function FilmstripPage({ params }: { params: Promise<{ slug: string }> }) {
  const { slug } = await params;
  return <div className="mx-auto max-w-7xl px-4 py-6 sm:px-6 lg:px-8"><SwipeDeck reelId={slug} /></div>;
}
