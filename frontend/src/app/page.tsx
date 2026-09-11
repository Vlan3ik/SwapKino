import { FeedView } from "@/components/feed/FeedView";
import { StructuredData } from "@/components/common/StructuredData";
import { absoluteUrl, SITE_NAME } from "@/lib/site";
import type { Metadata } from "next";

export const metadata: Metadata = {
  description: "Подбирайте фильмы и сериалы на вечер в СвайпКино: каталог, киноплёнки и персональные рекомендации.",
  alternates: { canonical: "/" },
  openGraph: { title: "СвайпКино — найди фильм на вечер", description: "Каталог фильмов, сериалы и киноплёнки для вашего вечера.", type: "website" },
};

export default function HomePage() { return <><StructuredData data={{ "@context": "https://schema.org", "@type": "WebSite", name: SITE_NAME, url: absoluteUrl("/"), potentialAction: { "@type": "SearchAction", target: `${absoluteUrl("/catalog")}?q={search_term_string}`, "query-input": "required name=search_term_string" } }} /><FeedView /></>; }
