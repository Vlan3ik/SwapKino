import { Suspense } from "react";
import { CatalogView } from "@/components/catalog/CatalogView";

export default function CatalogPage() {
  return <Suspense fallback={<div className="mx-auto max-w-7xl px-4 py-20 text-muted-foreground">Загружаем каталог…</div>}><CatalogView /></Suspense>;
}
