import { SwipeDeck } from "@/components/feed/SwipeDeck";

export default async function FilmstripPage({ params }: { params: Promise<{ slug: string }> }) {
  const { slug } = await params;
  return <div className="mx-auto max-w-7xl px-4 py-6 sm:px-6 lg:px-8"><SwipeDeck reelId={slug} /></div>;
}
