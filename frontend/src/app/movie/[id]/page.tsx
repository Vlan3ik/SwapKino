import { MovieCardView } from "@/components/movie/MovieCardView";

export default async function MoviePage({ params, searchParams }: { params: Promise<{ id: string }>; searchParams: Promise<{ series?: string }> }) {
  const [{ id }, query] = await Promise.all([params, searchParams]);
  return <MovieCardView movieId={Number(id)} isSeries={query.series === "1"} />;
}
