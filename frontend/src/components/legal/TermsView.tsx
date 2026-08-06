"use client";

import { motion } from "framer-motion";
import { FileText, ArrowLeft } from "lucide-react";
import { useAppStore } from "@/lib/store";

export function TermsView() {
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
            <FileText className="h-6 w-6" />
          </div>
          <div>
            <h1 className="text-2xl font-bold">Условия использования</h1>
            <p className="text-sm text-muted-foreground">
              Обновлено: 6 августа 2026
            </p>
          </div>
        </div>

        <div className="space-y-6 text-sm leading-relaxed text-foreground/90">
          <section>
            <h2 className="font-bold text-base mb-2">1. Принятие условий</h2>
            <p>
              Используя сервис СвайпКино («сервис»), ты соглашаешься с
              настоящими Условиями использования. Если ты не согласен с
              каким-либо положением — прекрати использование сервиса. Продолжая
              использование после обновления Условий, ты подтверждаешь согласие
              с новой редакцией.
            </p>
          </section>

          <section>
            <h2 className="font-bold text-base mb-2">2. Описание сервиса</h2>
            <p>
              СвайпКино — некоммерческий открытый проект, который помогает
              выбрать фильм на вечер. Сервис предоставляет интерфейс для
              просмотра каталога фильмов, подборок по жанрам, оценки и
              сохранения избранного. Сервис не хранит и не распространяет
              видеофайлы — все ссылки «Смотреть» ведут на внешние легальные
              стриминговые платформы.
            </p>
          </section>

          <section>
            <h2 className="font-bold text-base mb-2">3. Авторские права</h2>
            <p className="mb-2">
              Все постеры, трейлеры, описания и метаданные фильмов принадлежат
              их правообладателям. СвайпКино использует эти материалы в
              информационных целях через открытые API:
            </p>
            <ul className="list-disc pl-5 space-y-1.5">
              <li>
                The Movie Database (TMDB) — изображения и тексты согласно
                условиям использования TMDB.
              </li>
              <li>
                YouTube — встраивание трейлеров через официальный iframe API.
              </li>
            </ul>
            <p className="mt-2">
              СвайпКино не претендует на авторские права этих материалов.
              Исходный код сервиса распространяется под лицензией MIT — см.
              раздел «Лицензия».
            </p>
          </section>

          <section>
            <h2 className="font-bold text-base mb-2">4. Запрещённые действия</h2>
            <p className="mb-2">При использовании сервиса ты обязуешься не:</p>
            <ul className="list-disc pl-5 space-y-1.5">
              <li>Использовать сервис в целях, нарушающих законодательство.</li>
              <li>
                Пытаться получить несанкционированный доступ к коду или данным
                других пользователей.
              </li>
              <li>
                Передавать пароль, используемый в других сервисах.
              </li>
              <li>
                Модифицировать, декомпилировать или распространять сервис с
                нарушением лицензии MIT.
              </li>
              <li>
                Использовать автоматизированные скрипты, нагружающие
                инфраструктуру TMDB или других внешних сервисов.
              </li>
            </ul>
          </section>

          <section>
            <h2 className="font-bold text-base mb-2">5. Учётная запись</h2>
            <p>
              Регистрация и вход работают через серверный API. Пароли не
              хранятся в открытом виде. Используй уникальный пароль и не
              передавай пароль от других сервисов.
            </p>
          </section>

          <section>
            <h2 className="font-bold text-base mb-2">6. Отказ от ответственности</h2>
            <p>
              Сервис предоставляется «как есть», без каких-либо явных или
              подразумеваемых гарантий. Мы не гарантируем, что сервис будет
              работать бесперебойно, без ошибок или что метаданные фильмов
              (рейтинги, описания, постеры) будут точными и актуальными. Мы не
              несём ответственности за любые последствия использования сервиса,
              включая невозможность посмотреть фильм по внешней ссылке.
            </p>
          </section>

          <section>
            <h2 className="font-bold text-base mb-2">7. Ограничение ответственности</h2>
            <p>
              Поскольку сервис является некоммерческим и бесплатным, мы не
              несём ответственности перед пользователями за упущенную выгоду,
              потерю данных или любой косвенный ущерб, возникший в результате
              использования или невозможности использования сервиса.
            </p>
          </section>

          <section>
            <h2 className="font-bold text-base mb-2">8. Внешние ссылки</h2>
            <p>
              Сервис содержит ссылки на внешние сайты (стриминговые платформы,
              YouTube, Кинопоиск). Мы не контролируем содержимое этих сайтов и
              не несём ответственности за их работу, политику конфиденциальности
              или качество услуг. Переходя по внешним ссылкам, ты соглашаешься
              с условиями соответствующих сервисов.
            </p>
          </section>

          <section>
            <h2 className="font-bold text-base mb-2">9. Изменения условий</h2>
            <p>
              Мы оставляем за собой право изменять настоящие Условия в любое
              время. Актуальная версия всегда доступна на этой странице.
              Продолжение использования сервиса после вступления изменений в
              силу означает согласие с обновлёнными Условиями.
            </p>
          </section>

          <section>
            <h2 className="font-bold text-base mb-2">10. Применимое право</h2>
            <p>
              Настоящие Условия регулируются законодательством Российской
              Федерации. Все споры разрешаются в соответствии с применимым
              правом, с учётом некоммерческого и открытого характера проекта.
            </p>
          </section>

          <section>
            <h2 className="font-bold text-base mb-2">11. Контакты</h2>
            <p>
              Вопросы по Условиям использования можно задать через Issues на
              GitHub:
              <a
                href="https://github.com/Vlan3ik/SwapKino/issues"
                target="_blank"
                rel="noopener noreferrer"
                className="text-rating hover:underline ml-1"
              >
                github.com/Vlan3ik/SwapKino/issues
              </a>
            </p>
          </section>
        </div>
      </motion.div>
    </div>
  );
}
