import { MovieImagesView } from "@/components/movie/MovieImagesView";

export default async function MovieImagesPage({ params, searchParams }: { params: Promise<{ id: string }>; searchParams: Promise<{ series?: string }> }) {
  const [{ id }, query] = await Promise.all([params, searchParams]);
  return <MovieImagesView movieId={Number(id)} isSeries={query.series === "1"}/>;
}
