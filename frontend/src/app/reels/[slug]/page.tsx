import { SwipeDeck } from "@/components/feed/SwipeDeck";

export default async function ReelPage({ params }: { params: Promise<{ slug: string }> }) {
  const { slug } = await params;
  return <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8 py-6"><SwipeDeck reelId={slug} /></div>;
}
