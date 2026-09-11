"use client";

import { ArrowLeft, Copyright } from "lucide-react";
import { motion } from "framer-motion";
import Link from "next/link";
import { useAppStore } from "@/lib/store";

export function CopyrightView() {
  const setView = useAppStore((s) => s.setView);

  return (
    <div className="mx-auto max-w-3xl px-4 sm:px-6 lg:px-8 py-8">
      <button type="button" onClick={() => setView({ name: "feed" })} className="flex items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground mb-6">
        <ArrowLeft className="h-3.5 w-3.5" /> На главную
      </button>
      <motion.article initial={{ opacity: 0, y: 10 }} animate={{ opacity: 1, y: 0 }} className="glass-panel rounded-3xl p-6 sm:p-10">
        <header className="flex items-center gap-3 mb-8">
          <div className="h-12 w-12 rounded-xl bg-rating/15 text-rating flex items-center justify-center"><Copyright className="h-6 w-6" /></div>
          <div><h1 className="text-2xl font-bold">Правообладателям / Copyright</h1><p className="text-sm text-muted-foreground">Порядок обращений по контенту</p></div>
        </header>
        <div className="space-y-5 text-sm leading-relaxed text-foreground/90">
          <p>СвайпКино не размещает собственные копии фильмов и сериалов. Названия, описания, постеры, рейтинги и другие сведения о произведениях используются как метаданные, получаемые от внешних источников. Внешние ссылки и встроенные материалы доступны только при наличии соответствующей интеграции и ведут на сторонние ресурсы.</p>
          <p>Профили, аватары, оценки и комментарии пользователей являются пользовательским контентом. Публикуя материал, пользователь должен иметь право на его размещение и несёт ответственность за его содержание.</p>
          <h2 className="font-bold text-base">Запрос на удаление или исправление</h2>
          <p>Напишите на <a href="mailto:steammail_38@mail.ru" className="text-rating hover:underline">steammail_38@mail.ru</a> с темой «Copyright». Укажите название произведения, ссылку на страницу, описание права или нарушения и контакт для ответа. Для быстрого рассмотрения приложите документы или иные сведения, подтверждающие ваши права. Мы рассмотрим обращение и при необходимости ограничим отображение спорного материала.</p>
          <h2 className="font-bold text-base">Права на программное обеспечение и медиаматериалы</h2>
          <p>Исходный код проекта распространяется по <Link href="/license" className="text-rating hover:underline">MIT License</Link>. Это не передаёт права на названия, изображения, логотипы, трейлеры, метаданные и иные материалы третьих лиц. Их права сохраняются за соответствующими правообладателями. Атрибуция TMDB размещена на странице <Link href="/about" className="text-rating hover:underline">«О проекте и Credits»</Link>.</p>
          <p className="text-muted-foreground">Для запросов о персональных данных используйте тот же адрес с темой «Privacy». Этот раздел носит информационный характер и не заменяет юридическую консультацию.</p>
        </div>
      </motion.article>
    </div>
  );
}
