import { MovieCastView } from "@/components/movie/MovieCastView";

export default async function MovieCastPage({ params, searchParams }: { params: Promise<{ id: string }>; searchParams: Promise<{ series?: string }> }) {
  const [{ id }, query] = await Promise.all([params, searchParams]);
  return <MovieCastView movieId={Number(id)} isSeries={query.series === "1"}/>;
}
