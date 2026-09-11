import { MovieCardView } from "@/components/movie/MovieCardView";
import type { Metadata } from "next";
import { getSeoMovie } from "@/lib/seo";
import { absoluteUrl, truncateDescription } from "@/lib/site";
import { StructuredData } from "@/components/common/StructuredData";

export async function generateMetadata({ params, searchParams }: { params: Promise<{ id: string }>; searchParams: Promise<{ series?: string }> }): Promise<Metadata> {
  const [{ id }, query] = await Promise.all([params, searchParams]);
  const movie = await getSeoMovie(id, query.series === "1");
  const title = movie?.title ?? "Фильм";
  const description = truncateDescription(movie?.overview, `Описание фильма «${title}» в СвайпКино.`);
  const path = `/movie/${id}${query.series === "1" ? "?series=1" : ""}`;
  return { title, description, alternates: { canonical: path }, openGraph: { title, description, url: absoluteUrl(path), type: "video.movie", images: movie?.posterUrl ? [{ url: movie.posterUrl, alt: title }] : [{ url: absoluteUrl("/brand/swapkino-logo-white.png") }] }, twitter: { card: "summary_large_image", title, description, images: movie?.posterUrl ? [movie.posterUrl] : [absoluteUrl("/brand/swapkino-logo-white.png")] } };
}

export default async function MoviePage({ params, searchParams }: { params: Promise<{ id: string }>; searchParams: Promise<{ series?: string }> }) {
  const [{ id }, query] = await Promise.all([params, searchParams]);
  const movie = await getSeoMovie(id, query.series === "1");
  return <><StructuredData data={{ "@context": "https://schema.org", "@type": query.series === "1" ? "TVSeries" : "Movie", name: movie?.title ?? "Фильм", description: movie?.overview ?? undefined, image: movie?.posterUrl ? [movie.posterUrl] : undefined, dateCreated: movie?.releaseDate ?? undefined, aggregateRating: movie?.rating ? { "@type": "AggregateRating", ratingValue: movie.rating, bestRating: 10, worstRating: 0 } : undefined, url: absoluteUrl(`/movie/${id}${query.series === "1" ? "?series=1" : ""}`) }} /><MovieCardView movieId={Number(id)} isSeries={query.series === "1"} /></>;
}
