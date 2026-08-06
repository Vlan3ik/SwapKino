"use client";

import { motion } from "framer-motion";
import { Shield, ArrowLeft, Database, Cookie, Eye, UserCheck } from "lucide-react";
import { useAppStore } from "@/lib/store";

export function PrivacyView() {
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
            <Shield className="h-6 w-6" />
          </div>
          <div>
            <h1 className="text-2xl font-bold">Политика конфиденциальности</h1>
            <p className="text-sm text-muted-foreground">
              Обновлено: 6 августа 2026
            </p>
          </div>
        </div>

        <div className="space-y-6 text-sm leading-relaxed text-foreground/90">
          <section>
            <h2 className="font-bold text-base mb-2">1. Общие положения</h2>
            <p>
              СвайпКино («сервис», «мы», «нас») — некоммерческий открытый
              проект, который помогает подобрать фильм на вечер в формате
              свайпов. Сервис не является юридическим лицом и не осуществляет
              коммерческую деятельность. Настоящая Политика конфиденциальности
              описывает, какие данные мы собираем, как их используем и какие
              права у тебя есть в отношении этих данных.
            </p>
            <p className="mt-2">
              Используя сервис, ты соглашаешься с условиями данной Политики.
              Если ты не согласен с каким-либо положением — прекрати
              использование сервиса.
            </p>
          </section>

          <section>
            <h2 className="font-bold text-base mb-2 flex items-center gap-2">
              <Database className="h-4 w-4 text-rating" />
              2. Какие данные мы собираем
            </h2>
            <p className="mb-2">СвайпКино использует backend для хранения аккаунта и пользовательских действий:</p>
            <ul className="list-disc pl-5 space-y-1.5">
              <li>
                <span className="font-semibold">Избранное и оценки.</span>{" "}
                Список понравившихся фильмов и твои оценки (от 1 до 10)
                сохраняются на сервере и синхронизируются между устройствами.
              </li>
              <li>
                <span className="font-semibold">Учётные данные.</span>{" "}
                Email и пароль обрабатываются ASP.NET Identity, пароль хранится
                только в виде защищённого хеша.
              </li>
              <li>
                <span className="font-semibold">История просмотров.</span>{" "}
                Техническое состояние текущего интерфейса живёт только в
                памяти вкладки и не используется для профилирования.
              </li>
            </ul>
            <p className="mt-2 text-muted-foreground">
              Мы не используем аналитические трекеры и пиксели отслеживания.
            </p>
          </section>

          <section>
            <h2 className="font-bold text-base mb-2 flex items-center gap-2">
              <Cookie className="h-4 w-4 text-rating" />
              3. Cookies и локальное хранилище
            </h2>
            <p>
              Сервис не использует cookies для отслеживания. В браузере
              хранится только технический JWT-токен текущей сессии; избранное,
              оценки и аккаунт хранятся на backend.
            </p>
          </section>

          <section>
            <h2 className="font-bold text-base mb-2 flex items-center gap-2">
              <Eye className="h-4 w-4 text-rating" />
              4. Внешние сервисы
            </h2>
            <p className="mb-2">
              Сервис обращается к следующим внешним ресурсам для получения
              метаданных и изображений:
            </p>
            <ul className="list-disc pl-5 space-y-1.5">
              <li>
                <span className="font-semibold">The Movie Database (TMDB)</span> —{" "}
                постеры, бэкдропы, описания фильмов. Запросы идут напрямую из
                браузера к CDN <code className="text-rating">image.tmdb.org</code>.
                TMDB может собирать технические данные запроса согласно своей
                политике конфиденциальности.
              </li>
              <li>
                <span className="font-semibold">YouTube</span> — встраивание
                трейлеров через iframe. YouTube может устанавливать cookies и
                собирать данные о просмотре в соответствии с политикой Google.
              </li>
              <li>
                <span className="font-semibold">Внешние стриминговые платформы</span> —
                ссылки «Смотреть» ведут на внешние сайты (Кинопоиск, Иви,
                Кинопоиск HD и т. д.). Мы не контролируем их политики
                конфиденциальности.
              </li>
            </ul>
          </section>

          <section>
            <h2 className="font-bold text-base mb-2 flex items-center gap-2">
              <UserCheck className="h-4 w-4 text-rating" />
              5. Твои права
            </h2>
            <p className="mb-2">Ты можешь управлять данными аккаунта через сервис:</p>
            <ul className="list-disc pl-5 space-y-1.5">
              <li>Право на доступ — все данные видны тебе в профиле.</li>
              <li>Право на удаление — обратись к владельцу сервиса для удаления данных аккаунта.</li>
              <li>Право на перенос — запроси выгрузку пользовательских данных.</li>
              <li>Право на отзыв согласия — прекрати использовать сервис.</li>
            </ul>
          </section>

          <section>
            <h2 className="font-bold text-base mb-2">6. Безопасность</h2>
            <p>
              Данные аккаунта хранятся в PostgreSQL, пароли обрабатываются
              ASP.NET Identity, а доступ к API защищён JWT. Для production
              необходимо использовать HTTPS и секреты из secret-хранилища.
            </p>
          </section>

          <section>
            <h2 className="font-bold text-base mb-2">7. Дети</h2>
            <p>
              Сервис не предназначен для лиц младше 13 лет. Мы намеренно не
              собираем персональные данные детей. Если ты считаешь, что ребёнок
              предоставил нам данные, свяжись с нами через GitHub — мы удалим
              информацию.
            </p>
          </section>

          <section>
            <h2 className="font-bold text-base mb-2">8. Изменения политики</h2>
            <p>
              Мы можем обновлять эту Политику конфиденциальности. Актуальная
              версия всегда доступна на этой странице. Дата последнего
              обновления указана вверху.
            </p>
          </section>

          <section>
            <h2 className="font-bold text-base mb-2">9. Контакты</h2>
            <p>
              Вопросы по конфиденциальности можно задать через Issues на GitHub:
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
