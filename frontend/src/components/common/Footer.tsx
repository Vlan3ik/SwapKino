"use client";

import { Github, ExternalLink, Heart, FileText, Shield, ScrollText, Copyright } from "lucide-react";
import { motion } from "framer-motion";
import Link from "next/link";
import { BrandMark } from "./BrandMark";

export function Footer() {
  return (
    <footer className="mt-auto border-t border-white/5 bg-background/60 backdrop-blur-sm">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8 py-10">
        {/* Верхняя секция — описание + ссылки */}
        <div className="grid grid-cols-1 md:grid-cols-3 gap-8 mb-8">
          {/* Описание проекта */}
          <div className="md:col-span-1">
            <Link href="/"
              className="flex items-center gap-2.5 group mb-3"
            >
              <div className="relative">
                <div className="absolute inset-0 bg-rating/30 blur-lg rounded-full" />
                <BrandMark className="relative h-6 w-6" />
              </div>
              <span className="text-lg font-bold tracking-tight">
                СвайпКино
              </span>
            </Link>
            <p className="text-sm text-muted-foreground leading-relaxed max-w-sm">
              Бесплатный open-source сервис для поиска фильма на вечер в
              формате свайпов. Без рекламы, без подписок, без трекеров. Сделано
              с любовью к кино и открытому коду.
            </p>
            <a
              href="https://github.com/Vlan3ik/SwapKino"
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex items-center gap-2 mt-4 px-4 py-2 rounded-full bg-white/5 border border-white/10 hover:bg-white/10 hover:border-white/20 transition-all text-sm font-medium"
            >
              <Github className="h-4 w-4" />
              Vlan3ik/SwapKino
              <ExternalLink className="h-3 w-3 opacity-50" />
            </a>
          </div>

          {/* Навигация */}
          <div>
            <h3 className="text-xs font-bold text-foreground uppercase tracking-wider mb-3">
              Навигация
            </h3>
            <ul className="space-y-2 text-sm">
              <li>
                <Link href="/"
                  className="text-muted-foreground hover:text-foreground transition-colors"
                >
                  Лента
                </Link>
              </li>
              <li>
                <Link href="/catalog"
                  className="text-muted-foreground hover:text-foreground transition-colors"
                >
                  Каталог
                </Link>
              </li>
              <li>
                <Link href="/favorites"
                  className="text-muted-foreground hover:text-foreground transition-colors"
                >
                  Избранное
                </Link>
              </li>
              <li>
                <Link href="/ratings"
                  className="text-muted-foreground hover:text-foreground transition-colors"
                >
                  Мои оценки
                </Link>
              </li>
              <li>
                <Link href="/profile"
                  className="text-muted-foreground hover:text-foreground transition-colors"
                >
                  Профиль
                </Link>
              </li>
            </ul>
          </div>

          {/* Правовая информация */}
          <div>
            <h3 className="text-xs font-bold text-foreground uppercase tracking-wider mb-3">
              Правовая информация
            </h3>
            <ul className="space-y-2 text-sm">
              <li>
                <Link href="/license"
                  className="flex items-center gap-2 text-muted-foreground hover:text-foreground transition-colors"
                >
                  <ScrollText className="h-3.5 w-3.5" />
                  Лицензия MIT
                </Link>
              </li>
              <li>
                <Link href="/privacy"
                  className="flex items-center gap-2 text-muted-foreground hover:text-foreground transition-colors"
                >
                  <Shield className="h-3.5 w-3.5" />
                  Политика конфиденциальности
                </Link>
              </li>
              <li>
                <Link href="/terms"
                  className="flex items-center gap-2 text-muted-foreground hover:text-foreground transition-colors"
                >
                  <FileText className="h-3.5 w-3.5" />
                  Условия использования
                </Link>
              </li>
              <li>
                <Link href="/copyright" className="flex items-center gap-2 text-muted-foreground hover:text-foreground transition-colors">
                  <Copyright className="h-3.5 w-3.5" />
                  Правообладателям / Copyright
                </Link>
              </li>
              <li>
                <Link href="/about" className="flex items-center gap-2 text-muted-foreground hover:text-foreground transition-colors">
                  <ExternalLink className="h-3.5 w-3.5" />
                  О проекте и Credits
                </Link>
              </li>
            </ul>
          </div>
        </div>

        {/* Разделитель */}
        <div className="h-px bg-white/5 mb-6" />

        {/* Нижняя секция — копирайт + технологии */}
        <div className="flex flex-col sm:flex-row items-center justify-between gap-4 text-xs text-muted-foreground">
          <div className="flex items-center gap-1.5 flex-wrap justify-center">
            <span>© 2026 СвайпКино</span>
            <span className="opacity-30">·</span>
            <span>MIT License</span>
            <span className="opacity-30">·</span>
            <span className="flex items-center gap-1">
              Сделано с
              <Heart className="h-3 w-3 text-skip" fill="currentColor" />
              на открытых технологиях
            </span>
          </div>
          <div className="flex items-center gap-2 flex-wrap justify-center">
            <TechBadge label="Next.js" />
            <TechBadge label="TypeScript" />
            <TechBadge label="Tailwind" />
            <a
              href="https://www.themoviedb.org/"
              target="_blank"
              rel="noopener noreferrer"
              className="px-2 py-0.5 rounded-md bg-white/5 border border-white/5 text-[10px] font-medium text-muted-foreground/80 hover:text-foreground transition-colors"
            >
              <img src="/tmdb-logo.svg" alt="TMDB" className="h-3 w-auto" />
            </a>
          </div>
        </div>
        <p className="mt-4 text-center text-[11px] leading-relaxed text-muted-foreground/70">
          This product uses the TMDB API but is not endorsed or certified by TMDB.
        </p>
      </div>
    </footer>
  );
}

function TechBadge({ label }: { label: string }) {
  return (
    <motion.span
      whileHover={{ y: -1 }}
      className="px-2 py-0.5 rounded-md bg-white/5 border border-white/5 text-[10px] font-medium text-muted-foreground/80"
    >
      {label}
    </motion.span>
  );
}
