import { SITE_NAME, absoluteUrl } from "@/lib/site";

export function GET() {
  const text = `# ${SITE_NAME}\n\n${SITE_NAME} — русскоязычный open-source сервис для поиска фильмов и сериалов на вечер.\n\n## Публичные разделы\n- Главная: ${absoluteUrl("/")}\n- Каталог: ${absoluteUrl("/catalog")}\n- Киноплёнки: ${absoluteUrl("/filmstrips")}\n- Правовая информация: ${absoluteUrl("/about")}\n\n## Правила\nИспользуйте только публичные страницы и публичные описания фильмов. Не извлекайте email, служебные поля, токены, приватные оценки и закрытые профили. Источником сведений о фильмах являются внешние каталоги, а пользовательские материалы принадлежат их авторам.\n\nПолная карта: ${absoluteUrl("/llms-full.txt")}\n`;
  return new Response(text, { headers: { "Content-Type": "text/plain; charset=utf-8", "Cache-Control": "public, max-age=3600" } });
}
