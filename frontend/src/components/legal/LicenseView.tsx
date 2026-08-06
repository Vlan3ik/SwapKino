"use client";

import { motion } from "framer-motion";
import { ScrollText, ExternalLink, ArrowLeft } from "lucide-react";
import { useAppStore } from "@/lib/store";

export function LicenseView() {
  const setView = useAppStore((s) => s.setView);

  return (
    <div className="mx-auto max-w-3xl px-4 sm:px-6 lg:px-8 py-8">
      <button
        onClick={() => setView({ name: "feed" })}
        className="flex items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground transition-colors mb-6"
      >
        <ArrowLeft className="h-3.5 w-3.5" />
        На главную
      </button>

      <motion.div
        initial={{ opacity: 0, y: 10 }}
        animate={{ opacity: 1, y: 0 }}
        className="glass-panel rounded-3xl p-6 sm:p-10"
      >
        <div className="flex items-center gap-3 mb-6">
          <div className="h-12 w-12 rounded-xl bg-rating/15 text-rating flex items-center justify-center">
            <ScrollText className="h-6 w-6" />
          </div>
          <div>
            <h1 className="text-2xl font-bold">Лицензия</h1>
            <p className="text-sm text-muted-foreground">MIT License</p>
          </div>
        </div>

        <div className="prose prose-invert max-w-none space-y-4 text-sm leading-relaxed text-foreground/90">
          <p className="text-muted-foreground italic">
            СвайпКино — открытый проект. Ниже приведён текст лицензии MIT, под
            которой распространяется исходный код.
          </p>

          <div className="glass-panel rounded-xl p-5 border border-white/5">
            <h2 className="font-bold text-base mb-2">MIT License</h2>
            <p className="mb-2">Copyright (c) 2026 Vlan3ik</p>
            <p>
              Permission is hereby granted, free of charge, to any person
              obtaining a copy of this software and associated documentation
              files (the «Software»), to deal in the Software without
              restriction, including without limitation the rights to use,
              copy, modify, merge, publish, distribute, sublicense, and/or sell
              copies of the Software, and to permit persons to whom the
              Software is furnished to do so, subject to the following
              conditions:
            </p>
            <p className="mt-2">
              The above copyright notice and this permission notice shall be
              included in all copies or substantial portions of the Software.
            </p>
            <p className="mt-2">
              THE SOFTWARE IS PROVIDED «AS IS», WITHOUT WARRANTY OF ANY KIND,
              EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
              OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
              NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
              HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
              WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
              OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
              DEALINGS IN THE SOFTWARE.
            </p>
          </div>

          <h2 className="font-bold text-base pt-2">Что это значит простыми словами</h2>
          <p>
            MIT — одна из самых宽松 лицензий в мире открытого ПО. Она
            разрешает практически любое использование проекта: ты можешь
            свободно копировать код, изменять его, использовать в коммерческих
            и некоммерческих целях, распространять и даже выпускать собственные
            продукты на его основе. Единственное условие — сохранять
            уведомление об авторских правах и текст лицензии во всех копиях
            или существенных частях кода.
          </p>
          <p>
            Автор проекта не несёт никакой ответственности за последствия
            использования сервиса. Программа распространяется «как есть» — без
            каких-либо гарантий работоспособности, пригодности для конкретной
            задачи или отсутствия ошибок.
          </p>

          <h2 className="font-bold text-base pt-2">Использованные материалы</h2>
          <p>
            Постеры, бэкдропы и метаданные фильмов берутся из открытых
            источников: The Movie Database (TMDB) и Кинопоиска. Эти материалы
            защищены авторским правом соответствующих правообладателей и
            используются в демонстрационных целях. СвайпКино не хранит и не
            распространяет видеофайлы — сервис лишь помогает выбрать фильм на
            вечер и предоставляет ссылки на внешние легальные платформы для
            просмотра.
          </p>

          <h2 className="font-bold text-base pt-2">Исходный код</h2>
          <p>
            Полный исходный код проекта доступен в открытом доступе на GitHub.
            Ты можешь изучить его, сообщить о баге или предложить улучшения
            через Issues и Pull Requests.
          </p>
          <a
            href="https://github.com/Vlan3ik/SwapKino"
            target="_blank"
            rel="noopener noreferrer"
            className="inline-flex items-center gap-2 px-5 py-2.5 rounded-full bg-white text-black font-semibold text-sm hover:bg-rating transition-colors mt-2"
          >
            <ExternalLink className="h-4 w-4" />
            github.com/Vlan3ik/SwapKino
          </a>
        </div>
      </motion.div>
    </div>
  );
}
