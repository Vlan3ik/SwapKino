import { SITE_NAME, absoluteUrl } from "@/lib/site";
import { seoFetch } from "@/lib/seo";

export async function GET() {
  const reels = await seoFetch<{ items?: Array<{ slug: string; title: string; description?: string | null }>; results?: Array<{ slug: string; title: string; description?: string | null }> } | Array<{ slug: string; title: string; description?: string | null }>>("/filmstrips");
  const items = Array.isArray(reels) ? reels : reels?.items ?? reels?.results ?? [];
  const lines = items.map((item) => `- [${item.title}](${absoluteUrl(`/filmstrips/${item.slug}`)}): ${item.description ?? "Публичная киноплёнка SwapKino."}`).join("\n");
  const text = `# ${SITE_NAME}: публичная карта данных\n\n${SITE_NAME} предоставляет каталог фильмов, сериалов, киноплёнок и публичных профилей.\n\n## Киноплёнки\n${lines || `- Каталог киноплёнок: ${absoluteUrl("/catalog")}`}\n\n## Основные URL\n- ${absoluteUrl("/")}\n- ${absoluteUrl("/catalog")}\n- ${absoluteUrl("/sitemap.xml")}\n\nНе индексируйте API, админские разделы, настройки профиля и персональные данные.\n`;
  return new Response(text, { headers: { "Content-Type": "text/plain; charset=utf-8", "Cache-Control": "public, max-age=3600" } });
}
