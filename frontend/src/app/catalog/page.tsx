import { Suspense } from "react";
import { CatalogView } from "@/components/catalog/CatalogView";
import type { Metadata } from "next";

export const metadata: Metadata = {
  title: "Каталог фильмов и сериалов",
  description: "Каталог фильмов и сериалов СвайпКино с фильтрами по жанру, году и рейтингу.",
  alternates: { canonical: "/catalog" },
  openGraph: { title: "Каталог фильмов и сериалов", description: "Выберите фильм на вечер в каталоге СвайпКино.", type: "website" },
};

export default function CatalogPage() {
  return <Suspense fallback={<div className="mx-auto max-w-7xl px-4 py-20 text-muted-foreground">Загружаем каталог…</div>}><CatalogView /></Suspense>;
}
